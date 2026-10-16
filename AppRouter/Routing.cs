using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace AppRouter;

/// <summary>
/// Por dónde debe salir cada programa en este momento, teniendo en cuenta qué interfaces están
/// conectadas y la opción «si se desconecta la conexión de un programa».
/// </summary>
public static class Routing
{
    /// <summary>Destino especial: sin internet (sing-box rechaza sus conexiones).</summary>
    public const string Blocked = "@blocked";
    /// <summary>Destino especial: la ruta por defecto de Windows (cuando cae la del resto del tráfico).</summary>
    public const string Auto = "@auto";

    public static string Display(string target) => target switch
    {
        Blocked => L.T("Sin internet"),
        Auto => L.T("Automática (Windows)"),
        "" => "?",
        _ => target,
    };

    public static string EffectiveDefault(AppSettings s, ISet<string> usable)
    {
        var d = s.DefaultInterface;
        if (string.IsNullOrEmpty(d)) return Auto;
        return s.OnDisconnect == DisconnectAction.Fallback && !usable.Contains(d) ? Auto : d;
    }

    /// <summary>Destino actual de un programa (rule = null: un programa que no está en la lista).</summary>
    public static string Effective(AppSettings s, ProgramRule? rule, ISet<string> usable)
    {
        if (rule is null) return EffectiveDefault(s, usable);
        if (rule.Interface == Blocked) return Blocked;
        if (s.OnDisconnect == DisconnectAction.Fallback && !usable.Contains(rule.Interface))
            return EffectiveDefault(s, usable);
        return rule.Interface;
    }

    public static ProgramRule? RuleFor(AppSettings s, string processName) =>
        s.Rules.FirstOrDefault(r => r.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
}

public enum NicKind { Ethernet, WiFi, Mobile, Virtual, Other }

public record NicInfo(string Name, string Description, NicKind Kind, bool Connected, string Address);

/// <summary>Adaptadores de red del equipo y si están realmente conectados.</summary>
public static class NetworkState
{
    static readonly string[] VirtualHints =
        ["virtual", "vpn", "tap-", "tap ", "wintun", "wireguard", "hyper-v", "vmware", "virtualbox", "bluetooth",
         "wan miniport", "wi-fi direct", "loopback", "tunnel", "openvpn", "zerotier", "tailscale"];

    static readonly string[] MobileHints = ["mobile", "lte", "wwan", "5g", "4g", "remote ndis", "hilink", "cellular"];

    public static List<NicInfo> All() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && !n.Name.Equals(SingBoxConfig.TunInterfaceName, StringComparison.OrdinalIgnoreCase))
            .Select(n => new NicInfo(n.Name, n.Description, Kind(n), IsConnected(n), MainAddress(n)))
            .OrderBy(n => n.Connected ? 0 : 1).ThenBy(n => n.Kind).ThenBy(n => n.Name)
            .ToList();

    /// <summary>Nombres de los adaptadores conectados (activos y con una IP válida).</summary>
    public static HashSet<string> Usable() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsConnected)
            .Select(n => n.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    static bool IsConnected(NetworkInterface n)
    {
        if (n.OperationalStatus != OperationalStatus.Up) return false;
        try { return n.GetIPProperties().UnicastAddresses.Any(a => IsUsableAddress(a.Address)); }
        catch { return false; }
    }

    // Una IP 169.254.x.x (o fe80::) indica cable conectado pero sin red real.
    static bool IsUsableAddress(IPAddress a) =>
        a.AddressFamily == AddressFamily.InterNetwork
            ? !(a.GetAddressBytes() is [169, 254, ..]) && !IPAddress.IsLoopback(a)
            : a.AddressFamily == AddressFamily.InterNetworkV6 && !a.IsIPv6LinkLocal && !IPAddress.IsLoopback(a);

    static string MainAddress(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().UnicastAddresses
                .Select(a => a.Address)
                .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .FirstOrDefault(IsUsableAddress)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    static NicKind Kind(NetworkInterface n)
    {
        var d = (n.Description + " " + n.Name).ToLowerInvariant();
        if (n.NetworkInterfaceType is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2
            || MobileHints.Any(d.Contains)) return NicKind.Mobile;
        if (VirtualHints.Any(d.Contains) || n.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            return NicKind.Virtual;
        return n.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => NicKind.WiFi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx => NicKind.Ethernet,
            _ => NicKind.Other,
        };
    }

    public static string KindLabel(NicKind k) => k switch
    {
        NicKind.Ethernet => L.T("Cable (Ethernet)"),
        NicKind.WiFi => "Wi-Fi",
        NicKind.Mobile => L.T("Móvil (4G/5G)"),
        NicKind.Virtual => L.T("Virtual / VPN"),
        _ => L.T("Otra"),
    };
}
