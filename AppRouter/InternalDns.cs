using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using System.Text.RegularExpressions;

namespace AppRouter;

/// <summary>
/// «Dominios internos» (DNS dividido): los nombres de la red de la empresa (p. ej. *.medioambiental.local)
/// y la inversa de las redes excluidas (p. ej. 10.in-addr.arpa) se preguntan solo a los DNS internos,
/// por la conexión que llega a la intranet. El resto de consultas siguen como siempre.
/// </summary>
public static partial class InternalDns
{
    /// <summary>Lo que se ha encontrado en la configuración de red de Windows.</summary>
    public sealed record Detected(string Interface, List<string> Domains, List<string> Servers);

    /// <summary>¿Está activo? Hace falta al menos un servidor DNS interno.</summary>
    public static bool Enabled(AppSettings s) => s.InternalDnsServers.Count > 0;

    /// <summary>
    /// Busca la conexión de la intranet: la que tiene servidores DNS dentro de las redes excluidas
    /// (las que no pasan por AppRouter). Devuelve su sufijo DNS y esos servidores. Null si no hay ninguna.
    /// </summary>
    public static Detected? Detect(IEnumerable<string> excludedSubnets)
    {
        var nets = ParseNets(excludedSubnets);
        Detected? best = null;
        foreach (var n in UsableNics())
        {
            var p = n.GetIPProperties();
            var servers = p.DnsAddresses
                .Where(a => !a.IsIPv6LinkLocal && !a.IsIPv6SiteLocal && nets.Any(net => net.Contains(a)))
                .Select(a => a.ToString()).Distinct().ToList();
            if (servers.Count == 0) continue;
            var domains = p.DnsSuffix is { Length: > 0 } suffix && NormalizeDomain(suffix) is { } d ? [d] : new List<string>();
            var found = new Detected(n.Name, domains, servers);
            if (best is null || best.Domains.Count == 0 && domains.Count > 0) best = found;
        }
        return best;
    }

    /// <summary>
    /// Conexión por la que se llega a esos servidores: la que tiene una dirección en la misma red excluida
    /// que alguno de ellos o los tiene como DNS. Null si no se encuentra.
    /// </summary>
    public static string? DetectInterface(IEnumerable<string> servers, IEnumerable<string> excludedSubnets)
    {
        var ips = servers.Select(s => IPAddress.TryParse(s, out var ip) ? ip : null).OfType<IPAddress>().ToList();
        var nets = ParseNets(excludedSubnets).Where(net => ips.Any(net.Contains)).ToList();
        var nics = UsableNics().ToList();
        return nics.FirstOrDefault(n => n.GetIPProperties().DnsAddresses.Any(ips.Contains))?.Name
            ?? nics.FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(u => nets.Any(net => net.Contains(u.Address))))?.Name;
    }

    static IEnumerable<NetworkInterface> UsableNics() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && !n.Name.Equals(SingBoxConfig.TunInterfaceName, StringComparison.OrdinalIgnoreCase));

    static List<IPNetwork> ParseNets(IEnumerable<string> subnets) =>
        subnets.Select(s => IPNetwork.TryParse(s, out var n) ? (IPNetwork?)n : null).OfType<IPNetwork>().ToList();

    // ---------- Validación ----------

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?)(\.[a-z0-9_]([a-z0-9_-]{0,61}[a-z0-9_])?)*$")]
    private static partial Regex DomainRegex();

    /// <summary>Sufijo en minúsculas, sin punto inicial ni final; null si no es un nombre válido.</summary>
    public static string? NormalizeDomain(string s)
    {
        s = s.Trim().Trim('.').ToLowerInvariant();
        if (s.StartsWith("*.")) s = s[2..];
        return DomainRegex().IsMatch(s) ? s : null;
    }

    /// <summary>Dirección IP normalizada; null si no es válida.</summary>
    public static string? NormalizeServer(string s) =>
        IPAddress.TryParse(s.Trim(), out var ip) && s.Trim().Length > 0 ? ip.ToString() : null;

    /// <summary>Separa una lista escrita a mano (comas, punto y coma o espacios).</summary>
    public static List<string> SplitList(string text) =>
        text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------- Zonas inversas ----------

    /// <summary>
    /// Zonas de resolución inversa de una red: 10.0.0.0/8 → 10.in-addr.arpa. Si el prefijo no cae en un
    /// límite de octeto (IPv4) o de nibble (IPv6) se enumeran las zonas del siguiente límite (como mucho 128).
    /// </summary>
    public static List<string> ReverseZones(IPNetwork net)
    {
        var bytes = net.BaseAddress.GetAddressBytes();
        var v4 = net.BaseAddress.AddressFamily == AddressFamily.InterNetwork;
        var unit = v4 ? 8 : 4;                       // bits por etiqueta
        var bits = (net.PrefixLength + unit - 1) / unit * unit;
        var count = 1 << (bits - net.PrefixLength);
        var zones = new List<string>();
        var start = ToBig(bytes);
        var step = BigInteger.One << (bytes.Length * 8 - bits);
        for (var i = 0; i < count; i++)
        {
            var addr = FromBig(start + step * i, bytes.Length);
            var labels = new List<string>();
            if (v4)
                for (var o = 0; o < bits / 8; o++) labels.Add(addr[o].ToString());
            else
                for (var nib = 0; nib < bits / 4; nib++)
                    labels.Add(((addr[nib / 2] >> (nib % 2 == 0 ? 4 : 0)) & 0xF).ToString("x"));
            labels.Reverse();
            labels.Add(v4 ? "in-addr.arpa" : "ip6.arpa");
            zones.Add(string.Join('.', labels));
        }
        return zones;
    }

    // ---------- Restar direcciones a una red ----------

    /// <summary>
    /// Quita direcciones sueltas de una lista de redes: 10.0.0.0/8 menos 10.10.100.1 da las redes que cubren
    /// el resto de 10.0.0.0/8 (unas 24). Así esas direcciones entran en el túnel y el resto no cambia.
    /// </summary>
    public static List<string> Subtract(IEnumerable<string> subnets, IEnumerable<IPAddress> hosts)
    {
        var result = ParseNets(subnets);
        foreach (var h in hosts)
            result = result.SelectMany(n => Subtract(n, h)).ToList();
        return result.Select(n => n.ToString()).ToList();
    }

    static IEnumerable<IPNetwork> Subtract(IPNetwork net, IPAddress host)
    {
        if (host.AddressFamily != net.BaseAddress.AddressFamily || !net.Contains(host))
        {
            yield return net;
            yield break;
        }
        var size = net.BaseAddress.GetAddressBytes().Length;
        var max = size * 8;
        if (net.PrefixLength == max) yield break; // la red es justo esa dirección
        var baseNum = ToBig(net.BaseAddress.GetAddressBytes());
        var half = BigInteger.One << (max - net.PrefixLength - 1);
        foreach (var b in new[] { baseNum, baseNum + half })
        {
            var sub = new IPNetwork(new IPAddress(FromBig(b, size)), net.PrefixLength + 1);
            foreach (var r in Subtract(sub, host)) yield return r;
        }
    }

    static BigInteger ToBig(byte[] bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    static byte[] FromBig(BigInteger value, int size)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        var bytes = new byte[size];
        raw.AsSpan(Math.Max(0, raw.Length - size)).CopyTo(bytes.AsSpan(size - Math.Min(size, raw.Length)));
        return bytes;
    }
}
