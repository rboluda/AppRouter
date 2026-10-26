using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AppRouter;

/// <summary>
/// Límite de velocidad por programa. sing-box no tiene ninguno, así que los programas con límite
/// se envían (salida "socks" lim-N) a este proxy SOCKS5 local, que reparte la velocidad con un
/// cubo de tokens por programa y sentido, y sale por la interfaz elegida igual que sing-box
/// (IP_UNICAST_IF). Soporta TCP (CONNECT) y UDP (UDP ASSOCIATE).
///
/// Cada programa con límite ocupa un «hueco» fijo (1..SlotCount) para que añadir o cambiar
/// límites no cambie config.json (no reinicia sing-box): qué programa va en cada hueco está en
/// los rule-sets, y el límite y la interfaz de cada hueco en limits.json, que se relee en caliente.
/// </summary>
public static class LimiterMap
{
    public const int SlotCount = 16;

    public record Slot(string Interface, double Mbps);

    /// <summary>Archivo de huecos (se puede cambiar para pruebas).</summary>
    public static string FilePath { get; set; } = Paths.LimitsPath;

    static readonly object gate = new();
    static DateTime cachedAt;
    static Dictionary<int, Slot> cached = [];

    public static string Serialize(Dictionary<int, Slot> slots) =>
        JsonSerializer.Serialize(slots.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Huecos actuales según limits.json (con caché mientras el archivo no cambie).</summary>
    public static Dictionary<int, Slot> Read()
    {
        lock (gate)
        {
            try
            {
                var t = File.GetLastWriteTimeUtc(FilePath);
                if (t == cachedAt) return cached;
                // Se abre permitiendo que la ventana lo reemplace mientras tanto (escritura atómica).
                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var raw = JsonSerializer.Deserialize<Dictionary<string, Slot>>(fs) ?? [];
                cached = raw.Where(kv => int.TryParse(kv.Key, out _)).ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
                cachedAt = t;
            }
            catch { /* sin archivo o a medio escribir: se usa lo último leído */ }
            return cached;
        }
    }

    /// <summary>Megabits por segundo → bytes por segundo.</summary>
    public static double BytesPerSecond(double mbps) => mbps * 1_000_000 / 8;
}

/// <summary>Cubo de tokens compartido por todas las conexiones de un programa en un sentido.</summary>
public sealed class TokenBucket(double bytesPerSecond)
{
    readonly object gate = new();
    double rate = bytesPerSecond;
    double tokens = Capacity(bytesPerSecond);
    long last = Environment.TickCount64;

    static double Capacity(double rate) => Math.Max(rate * 0.2, 32 * 1024); // ráfaga de ~200 ms

    public double Rate
    {
        get { lock (gate) return rate; }
        set { lock (gate) { Refill(); rate = value; tokens = Math.Min(tokens, Capacity(value)); } }
    }

    void Refill()
    {
        var now = Environment.TickCount64;
        tokens = Math.Min(Capacity(rate), tokens + (now - last) / 1000.0 * rate);
        last = now;
    }

    /// <summary>
    /// Reserva <paramref name="n"/> bytes; devuelve cuánto hay que esperar antes de usarlos.
    /// Los tokens pueden quedar en negativo: así las conexiones se turnan en orden de llegada.
    /// </summary>
    public TimeSpan Reserve(int n)
    {
        lock (gate)
        {
            Refill();
            tokens -= n;
            return tokens >= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(-tokens / rate);
        }
    }

    public async Task TakeAsync(int n, CancellationToken ct)
    {
        var wait = Reserve(n);
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
    }
}

/// <summary>Proxy SOCKS5 limitador. Se ejecuta dentro del servicio de AppRouter, junto a sing-box.</summary>
public sealed class LimiterServer : IDisposable
{
    const int BufferSize = 16 * 1024;
    static readonly TimeSpan MaxUdpDelay = TimeSpan.FromMilliseconds(250); // UDP más retrasado que esto se descarta

    readonly int port;
    readonly string secret;
    readonly Action<string> log;
    readonly CancellationTokenSource cts = new();
    readonly Dictionary<int, (TokenBucket Up, TokenBucket Down)> buckets = [];
    Socket? listener;

    public LimiterServer(int port, string secret, Action<string> log)
    {
        this.port = port;
        this.secret = secret;
        this.log = log;
    }

    public void Start()
    {
        listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
        listener.Listen(256);
        _ = AcceptLoop();
        _ = ReloadLoop();
        log($"Limitador de velocidad escuchando en 127.0.0.1:{port}");
    }

    public void Dispose()
    {
        cts.Cancel();
        try { listener?.Dispose(); } catch { }
    }

    async Task AcceptLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener!.AcceptAsync(cts.Token); }
            catch when (cts.IsCancellationRequested) { return; }
            catch (Exception ex) { log("Limitador: " + ex.Message); continue; }
            client.NoDelay = true;
            _ = HandleAsync(client);
        }
    }

    /// <summary>Aplica en caliente los cambios de limits.json a las conexiones abiertas.</summary>
    async Task ReloadLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var map = LimiterMap.Read();
                lock (buckets)
                    foreach (var (slot, (up, down)) in buckets)
                        if (map.TryGetValue(slot, out var s) && s.Mbps > 0)
                            up.Rate = down.Rate = LimiterMap.BytesPerSecond(s.Mbps);
                await Task.Delay(2000, cts.Token);
            }
            catch when (cts.IsCancellationRequested) { return; }
            catch { /* se reintenta */ }
        }
    }

    (TokenBucket Up, TokenBucket Down) BucketsFor(int slot, double mbps)
    {
        var rate = LimiterMap.BytesPerSecond(mbps);
        lock (buckets)
        {
            if (!buckets.TryGetValue(slot, out var b))
                buckets[slot] = b = (new TokenBucket(rate), new TokenBucket(rate));
            else if (Math.Abs(b.Up.Rate - rate) > 1)
                b.Up.Rate = b.Down.Rate = rate;
            return b;
        }
    }

    // ---------- SOCKS5 ----------

    async Task HandleAsync(Socket client)
    {
        using var _ = client;
        using var ns = new NetworkStream(client, ownsSocket: false);
        var ct = cts.Token;
        try
        {
            // Saludo: solo usuario/contraseña (usuario = hueco, contraseña = secreto de AppRouter).
            var hello = await ReadExactly(ns, 2, ct);
            var methods = await ReadExactly(ns, hello[1], ct);
            if (hello[0] != 5 || !methods.Contains((byte)2)) { await ns.WriteAsync(new byte[] { 5, 0xFF }, ct); return; }
            await ns.WriteAsync(new byte[] { 5, 2 }, ct);

            var authHead = await ReadExactly(ns, 2, ct);
            var user = Encoding.ASCII.GetString(await ReadExactly(ns, authHead[1], ct));
            var passLen = (await ReadExactly(ns, 1, ct))[0];
            var pass = Encoding.ASCII.GetString(await ReadExactly(ns, passLen, ct));
            var map = LimiterMap.Read();
            if (pass != secret || !int.TryParse(user, out var slot) || !map.TryGetValue(slot, out var target))
            {
                await ns.WriteAsync(new byte[] { 1, 1 }, ct);
                return;
            }
            await ns.WriteAsync(new byte[] { 1, 0 }, ct);

            var req = await ReadExactly(ns, 3, ct); // VER CMD RSV
            var (host, ip, dport) = await ReadAddress(ns, ct);
            var (up, down) = target.Mbps > 0 ? BucketsFor(slot, target.Mbps) : (null!, null!);

            switch (req[1])
            {
                case 1: await ConnectAsync(ns, target.Interface, host, ip, dport, up, down, ct); break;
                case 3: await UdpAssociateAsync(ns, target.Interface, up, down, ct); break;
                default: await Reply(ns, 7, ct); break; // comando no soportado
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or EndOfStreamException) { }
        catch (Exception ex) { log("Limitador: " + ex.Message); }
    }

    async Task ConnectAsync(NetworkStream ns, string iface, string? host, IPAddress? ip, int dport,
        TokenBucket? up, TokenBucket? down, CancellationToken ct)
    {
        Socket remote;
        try
        {
            ip ??= await Resolve(host!, ct);
            remote = Bound(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp, iface);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await remote.ConnectAsync(new IPEndPoint(ip, dport), timeout.Token);
            remote.NoDelay = true;
        }
        catch (Exception ex)
        {
            await Reply(ns, ex is SocketException { SocketErrorCode: SocketError.ConnectionRefused } ? (byte)5 : (byte)4, ct);
            return;
        }

        using (remote)
        using (var rs = new NetworkStream(remote, ownsSocket: false))
        {
            await Reply(ns, 0, ct);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var a = Pump(ns, rs, up, linked.Token);   // programa → internet (subida)
            var b = Pump(rs, ns, down, linked.Token); // internet → programa (bajada)
            await Task.WhenAny(a, b);
            linked.Cancel();
            try { await Task.WhenAll(a, b); } catch { }
        }
    }

    static async Task Pump(Stream from, Stream to, TokenBucket? bucket, CancellationToken ct)
    {
        var buf = new byte[BufferSize];
        try
        {
            int n;
            while ((n = await from.ReadAsync(buf, ct)) > 0)
            {
                if (bucket is not null) await bucket.TakeAsync(n, ct);
                await to.WriteAsync(buf.AsMemory(0, n), ct);
            }
        }
        catch { /* una de las dos partes cerró */ }
    }

    async Task UdpAssociateAsync(NetworkStream control, string iface, TokenBucket? up, TokenBucket? down, CancellationToken ct)
    {
        using var local = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        local.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        NoConnReset(local);
        Socket? out4 = null, out6 = null;
        try
        {
            out4 = Bound(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp, iface);
            out4.Bind(new IPEndPoint(IPAddress.Any, 0));
            NoConnReset(out4);
        }
        catch { out4?.Dispose(); out4 = null; }
        try
        {
            out6 = Bound(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp, iface);
            out6.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
            NoConnReset(out6);
        }
        catch { out6?.Dispose(); out6 = null; }
        if (out4 is null && out6 is null) { await Reply(control, 4, ct); return; }

        var bnd = (IPEndPoint)local.LocalEndPoint!;
        var reply = new byte[10];
        reply[0] = 5; reply[3] = 1;
        IPAddress.Loopback.GetAddressBytes().CopyTo(reply, 4);
        BinaryPrimitives.WriteUInt16BigEndian(reply.AsSpan(8), (ushort)bnd.Port);
        await control.WriteAsync(reply, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        EndPoint? clientEp = null;

        // programa → internet
        var toRemote = Task.Run(async () =>
        {
            var buf = new byte[65536];
            while (!linked.IsCancellationRequested)
            {
              try
              {
                var r = await local.ReceiveFromAsync(buf, new IPEndPoint(IPAddress.Any, 0), linked.Token);
                clientEp = r.RemoteEndPoint;
                var (dest, payloadOffset) = await ParseUdpHeader(buf, r.ReceivedBytes, linked.Token);
                if (dest is null) continue;
                var len = r.ReceivedBytes - payloadOffset;
                if (up is not null && up.Reserve(len) is var wait && wait > TimeSpan.Zero)
                {
                    if (wait > MaxUdpDelay) continue; // sobre el límite: se descarta, como haría un router
                    await Task.Delay(wait, linked.Token);
                }
                var sock = dest.AddressFamily == AddressFamily.InterNetworkV6 ? out6 : out4;
                if (sock is not null) await sock.SendToAsync(buf.AsMemory(payloadOffset, len), dest, linked.Token);
              }
              catch (OperationCanceledException) { break; }
              catch (Exception ex) { LogUdp(ex); }
            }
        }, linked.Token);

        // internet → programa
        async Task FromRemote(Socket? sock, IPAddress any)
        {
            if (sock is null) return;
            var buf = new byte[65536];
            var header = new byte[22];
            while (!linked.IsCancellationRequested)
            {
                try
                {
                    var r = await sock.ReceiveFromAsync(buf, new IPEndPoint(any, 0), linked.Token);
                    if (clientEp is null) continue;
                    if (down is not null && down.Reserve(r.ReceivedBytes) is var wait && wait > TimeSpan.Zero)
                    {
                        if (wait > MaxUdpDelay) continue;
                        await Task.Delay(wait, linked.Token);
                    }
                    var src = (IPEndPoint)r.RemoteEndPoint;
                    var srcIp = src.Address.IsIPv4MappedToIPv6 ? src.Address.MapToIPv4() : src.Address;
                    var addr = srcIp.GetAddressBytes();
                    var hlen = 4 + addr.Length + 2;
                    header[3] = (byte)(addr.Length == 4 ? 1 : 4);
                    addr.CopyTo(header, 4);
                    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4 + addr.Length), (ushort)src.Port);
                    var packet = new byte[hlen + r.ReceivedBytes];
                    header.AsSpan(0, hlen).CopyTo(packet);
                    buf.AsSpan(0, r.ReceivedBytes).CopyTo(packet.AsSpan(hlen));
                    await local.SendToAsync(packet, clientEp, linked.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { LogUdp(ex); }
            }
        }

        var from4 = FromRemote(out4, IPAddress.Any);
        var from6 = FromRemote(out6, IPAddress.IPv6Any);

        // La asociación dura lo que la conexión TCP de control.
        var probe = new byte[1];
        try { while (await control.ReadAsync(probe, ct) > 0) { } } catch { }
        linked.Cancel();
        try { await Task.WhenAll(toRemote, from4, from6); } catch { }
        out4?.Dispose();
        out6?.Dispose();
    }

    async Task<(IPEndPoint? Dest, int PayloadOffset)> ParseUdpHeader(byte[] b, int len, CancellationToken ct)
    {
        if (len < 10 || b[2] != 0) return (null, 0); // fragmentación no soportada
        int o = 4;
        IPAddress ip;
        switch (b[3])
        {
            case 1: ip = new IPAddress(b.AsSpan(o, 4)); o += 4; break;
            case 4: if (len < 22) return (null, 0); ip = new IPAddress(b.AsSpan(o, 16)); o += 16; break;
            case 3:
                var hl = b[o];
                var host = Encoding.ASCII.GetString(b, o + 1, hl);
                o += 1 + hl;
                try { ip = await Resolve(host, ct); } catch { return (null, 0); }
                break;
            default: return (null, 0);
        }
        var port = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o));
        return (new IPEndPoint(ip, port), o + 2);
    }

    // ---------- Utilidades ----------

    /// <summary>
    /// En Windows, un «puerto inalcanzable» (ICMP) hace que el siguiente ReceiveFrom de ese socket UDP
    /// lance una excepción; se desactiva para que una respuesta perdida no corte la asociación.
    /// </summary>
    static void NoConnReset(Socket s)
    {
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        try { s.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null); } catch { /* no disponible: se ignora */ }
    }

    DateTime lastUdpLog;
    void LogUdp(Exception ex)
    {
        // Como mucho un mensaje por minuto: el UDP puede fallar muchas veces seguidas.
        if (DateTime.UtcNow - lastUdpLog < TimeSpan.FromMinutes(1)) return;
        lastUdpLog = DateTime.UtcNow;
        log("Limitador (UDP): " + ex.Message);
    }

    static async Task<(string? Host, IPAddress? Ip, int Port)> ReadAddress(Stream s, CancellationToken ct)
    {
        var atyp = (await ReadExactly(s, 1, ct))[0];
        string? host = null;
        IPAddress? ip = null;
        switch (atyp)
        {
            case 1: ip = new IPAddress(await ReadExactly(s, 4, ct)); break;
            case 4: ip = new IPAddress(await ReadExactly(s, 16, ct)); break;
            case 3:
                var len = (await ReadExactly(s, 1, ct))[0];
                host = Encoding.ASCII.GetString(await ReadExactly(s, len, ct));
                break;
            default: throw new IOException("ATYP no soportado");
        }
        var port = BinaryPrimitives.ReadUInt16BigEndian(await ReadExactly(s, 2, ct));
        return (host, ip, port);
    }

    static async Task<IPAddress> Resolve(string host, CancellationToken ct)
    {
        var all = await Dns.GetHostAddressesAsync(host, ct);
        return all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.First();
    }

    static async Task Reply(Stream s, byte code, CancellationToken ct) =>
        await s.WriteAsync(new byte[] { 5, code, 0, 1, 0, 0, 0, 0, 0, 0 }, ct);

    static async Task<byte[]> ReadExactly(Stream s, int n, CancellationToken ct)
    {
        var b = new byte[n];
        await s.ReadExactlyAsync(b, ct);
        return b;
    }

    /// <summary>
    /// Socket obligado a salir por <paramref name="iface"/> (IP_UNICAST_IF / IPV6_UNICAST_IF), igual
    /// que las salidas «direct» de sing-box. Así no vuelve a entrar en el túnel.
    /// </summary>
    static Socket Bound(AddressFamily af, SocketType type, ProtocolType proto, string iface)
    {
        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(iface, StringComparison.OrdinalIgnoreCase))
                  ?? throw new SocketException((int)SocketError.NetworkUnreachable);
        var s = new Socket(af, type, proto);
        try
        {
            if (af == AddressFamily.InterNetwork)
            {
                var index = nic.GetIPProperties().GetIPv4Properties()?.Index ?? throw new SocketException((int)SocketError.NetworkUnreachable);
                // IP_UNICAST_IF (31) espera el índice en orden de red.
                s.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31, IPAddress.HostToNetworkOrder(index));
            }
            else
            {
                var index = nic.GetIPProperties().GetIPv6Properties()?.Index ?? throw new SocketException((int)SocketError.NetworkUnreachable);
                s.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)31, index); // IPV6_UNICAST_IF: orden del equipo
            }
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }
}
