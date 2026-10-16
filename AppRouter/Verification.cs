using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AppRouter;

/// <summary>Una conexión activa según la API de sing-box.</summary>
public record Connection(string Id, string Network, string ProcessPath, string Host, IPAddress? DestIp, int DestPort, string OutboundTag, long Upload, long Download, DateTimeOffset? Start)
{
    public string ProcessName => ProcessPath == "" ? L.T("(desconocido)") : Path.GetFileName(ProcessPath);
    public string Destination => (Host != "" ? Host : DestIp?.ToString() ?? "?") + ":" + DestPort;
}

/// <summary>Cliente mínimo de la API Clash de sing-box (solo escucha en 127.0.0.1).</summary>
public sealed class SingBoxApi(int port, string secret) : IDisposable
{
    readonly HttpClient http = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
        Timeout = TimeSpan.FromSeconds(5),
        DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", secret) },
    };

    public async Task<bool> IsAliveAsync()
    {
        try { return (await http.GetAsync("version")).IsSuccessStatusCode; }
        catch { return false; }
    }

    public async Task<List<Connection>> GetConnectionsAsync()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync("connections"));
        var list = new List<Connection>();
        if (!doc.RootElement.TryGetProperty("connections", out var conns) || conns.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var c in conns.EnumerateArray())
        {
            var md = c.GetProperty("metadata");
            // "chains" va de la salida final hacia atrás; con salidas direct solo hay una.
            var chain = c.TryGetProperty("chains", out var ch) && ch.GetArrayLength() > 0 ? ch[0].GetString() ?? "" : "";
            IPAddress.TryParse(Str(md, "destinationIP"), out var ip);
            int.TryParse(Str(md, "destinationPort"), out var port);
            list.Add(new Connection(
                Str(c, "id"), Str(md, "network"), Str(md, "processPath"), Str(md, "host"),
                ip is null ? null : Normalize(ip), port, chain,
                Num(c, "upload"), Num(c, "download"),
                DateTimeOffset.TryParse(Str(c, "start"), out var start) ? start : null));
        }
        return list;
    }

    /// <summary>Cierra una conexión; el programa vuelve a conectar y se enruta con las reglas actuales.</summary>
    public async Task<bool> CloseAsync(string id)
    {
        try { return (await http.DeleteAsync("connections/" + Uri.EscapeDataString(id))).IsSuccessStatusCode; }
        catch { return false; }
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        } : "";

    static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    public static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public void Dispose() => http.Dispose();
}

/// <summary>
/// Comprobación independiente de sing-box: pregunta a Windows qué IP local usan los sockets
/// TCP de sing-box.exe y a qué adaptador pertenece esa IP.
/// </summary>
public static class WindowsSockets
{
    public record TcpRow(IPAddress Local, IPAddress Remote, int RemotePort, int Pid);

    public static Dictionary<IPAddress, string> AddressToAdapter()
    {
        var map = new Dictionary<IPAddress, string>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var a in n.GetIPProperties().UnicastAddresses)
                map.TryAdd(SingBoxApi.Normalize(a.Address), n.Name);
        return map;
    }

    public static HashSet<int> SingBoxPids()
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName("sing-box")) { pids.Add(p.Id); p.Dispose(); }
        return pids;
    }

    /// <summary>Conexiones TCP (IPv4 e IPv6) de los procesos indicados.</summary>
    public static List<TcpRow> TcpConnections(HashSet<int> pids)
    {
        var rows = new List<TcpRow>();
        Read(AF_INET, 24, buf => (
            new IPAddress((uint)Marshal.ReadInt32(buf, 4)),
            new IPAddress((uint)Marshal.ReadInt32(buf, 12)),
            Port(Marshal.ReadInt32(buf, 16)),
            Marshal.ReadInt32(buf, 20)), pids, rows);
        Read(AF_INET6, 56, buf => (
            new IPAddress(Bytes(buf, 0, 16)),
            new IPAddress(Bytes(buf, 24, 16)),
            Port(Marshal.ReadInt32(buf, 44)),
            Marshal.ReadInt32(buf, 52)), pids, rows);
        return rows;
    }

    const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_ALL = 5;

    [DllImport("iphlpapi.dll")]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);

    static void Read(int af, int rowSize, Func<IntPtr, (IPAddress, IPAddress, int, int)> parse, HashSet<int> pids, List<TcpRow> rows)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0);
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_ALL, 0) != 0) return;
            var count = Marshal.ReadInt32(buf);
            for (var i = 0; i < count; i++)
            {
                var (local, remote, port, pid) = parse(buf + 4 + i * rowSize);
                if (pids.Contains(pid))
                    rows.Add(new TcpRow(SingBoxApi.Normalize(local), SingBoxApi.Normalize(remote), port, pid));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static int Port(int raw) => ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);

    static byte[] Bytes(IntPtr p, int offset, int len)
    {
        var b = new byte[len];
        Marshal.Copy(p + offset, b, 0, len);
        return b;
    }
}

/// <summary>Resolución DNS con caché de un minuto (la ventana refresca cada segundo).</summary>
public sealed class DnsCache
{
    readonly Dictionary<string, (DateTime at, IPAddress[] ips)> cache = new(StringComparer.OrdinalIgnoreCase);

    public IPAddress[] Resolve(string host)
    {
        lock (cache)
            if (cache.TryGetValue(host, out var e) && DateTime.UtcNow - e.at < TimeSpan.FromMinutes(1))
                return e.ips;

        IPAddress[] ips;
        try
        {
            var t = Dns.GetHostAddressesAsync(host);
            ips = t.Wait(TimeSpan.FromSeconds(2)) ? t.Result.Select(SingBoxApi.Normalize).ToArray() : [];
        }
        catch { ips = []; }

        lock (cache) cache[host] = (DateTime.UtcNow, ips);
        return ips;
    }
}

/// <summary>Stale = va por otra salida, pero se abrió antes del último cambio (es lo esperado: se mantiene hasta que se cierre).</summary>
public enum Verdict { Ok, Wrong, Stale, Unknown }

/// <summary>
/// Expected y ViaSingBox son destinos (nombre de interfaz, <see cref="Routing.Blocked"/> o
/// <see cref="Routing.Auto"/>); se muestran con <see cref="Routing.Display"/>.
/// </summary>
public record CheckedConnection(Connection Conn, string Expected, string ViaSingBox, string ViaWindows, Verdict Verdict);

public static class Verifier
{
    /// <summary>
    /// Para cada conexión: destino esperado ahora (regla, «resto del tráfico» o el de respaldo si su
    /// conexión está caída), el que dice sing-box y el que dice Windows (IP local del socket real).
    /// Correcta solo si coinciden todos los conocidos.
    /// </summary>
    public static List<CheckedConnection> Evaluate(
        IEnumerable<Connection> conns, AppSettings settings, ISet<string> usable,
        List<WindowsSockets.TcpRow> singBoxSockets, Dictionary<IPAddress, string> addrToAdapter,
        Func<string, IPAddress[]>? resolve = null)
    {
        var result = new List<CheckedConnection>();
        foreach (var c in conns)
        {
            var expected = Routing.Effective(settings, Routing.RuleFor(settings, c.ProcessName), usable);
            var via = SingBoxConfig.TargetFromTag(c.OutboundTag) ?? (c.OutboundTag == "" ? "" : c.OutboundTag);

            // Si sing-box solo conoce el nombre (p. ej. conexiones por dominio), se resuelve aquí.
            IPAddress[] destIps = c.DestIp is not null ? [c.DestIp]
                : c.Host != "" && resolve is not null ? resolve(c.Host) : [];

            string viaWindows;
            if (c.Network != "tcp" || destIps.Length == 0)
                viaWindows = "—"; // UDP: Windows no asocia destino al socket
            else
            {
                var adapters = singBoxSockets
                    .Where(s => s.RemotePort == c.DestPort && destIps.Contains(s.Remote))
                    .Select(s => addrToAdapter.TryGetValue(s.Local, out var a) ? a : s.Local.ToString())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                viaWindows = adapters.Count switch
                {
                    0 => "—",
                    1 => adapters[0],
                    _ => string.Join(" / ", adapters), // varias conexiones al mismo destino por adaptadores distintos
                };
            }

            var verdict = Verdict.Ok;
            if (via == "") verdict = Verdict.Unknown;
            else if (!via.Equals(expected, StringComparison.OrdinalIgnoreCase)) verdict = Verdict.Wrong;
            // Con destino «Automática» no hay un adaptador concreto que comparar con Windows.
            if (verdict == Verdict.Ok && expected != Routing.Auto && viaWindows != "—"
                && !viaWindows.Equals(expected, StringComparison.OrdinalIgnoreCase))
                verdict = viaWindows.Contains(" / ") ? Verdict.Unknown : Verdict.Wrong;

            if (verdict == Verdict.Wrong && c.Start is { } st && settings.AppliedAt is { } applied && st < applied)
                verdict = Verdict.Stale;

            result.Add(new CheckedConnection(c, expected, via, viaWindows, verdict));
        }
        return result;
    }
}
