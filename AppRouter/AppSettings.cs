using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;

namespace AppRouter;

public class ProgramRule
{
    public string Path { get; set; } = "";
    public string Interface { get; set; } = "";

    public string ProcessName => System.IO.Path.GetFileName(Path);
}

/// <summary>Estado de la app, persistido en %ProgramData%\AppRouter\settings.json.</summary>
public class AppSettings
{
    public string? DefaultInterface { get; set; }
    public List<string> ExcludedSubnets { get; set; } = ["10.0.0.0/8"];
    public List<ProgramRule> Rules { get; set; } = [];

    /// <summary>
    /// Adaptadores con salida en config.json. Solo crece: así quitar o desconectar un adaptador
    /// no obliga a reiniciar sing-box (y cortar conexiones); solo lo hace uno nuevo.
    /// </summary>
    public List<string> KnownInterfaces { get; set; } = [];

    /// <summary>Último "Guardar y aplicar" (UTC): las conexiones anteriores pueden ir aún por la salida vieja.</summary>
    public DateTimeOffset? AppliedAt { get; set; }
    /// <summary>API local de sing-box (solo 127.0.0.1) que usa la ventana de comprobación.</summary>
    public int ApiPort { get; set; } = 9097;
    public string ApiSecret { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsPath)) ?? new();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo leer {Paths.SettingsPath}:\n{ex.Message}\n\nSe empezará con una lista vacía.",
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(Paths.SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
    }

    /// <summary>Adaptadores seleccionables (sin loopback ni el propio túnel), activos primero.</summary>
    public static List<string> GetInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && !n.Name.Equals(SingBoxConfig.TunInterfaceName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.OperationalStatus == OperationalStatus.Up ? 0 : 1)
            .ThenBy(n => n.Name)
            .Select(n => n.Name)
            .ToList();

    /// <summary>
    /// Adaptador por el que saldría todo si AppRouter no estuviera: la ruta por defecto de Windows
    /// con menor métrica (ruta + interfaz), sin contar el túnel. Null si no se puede averiguar.
    /// </summary>
    public static string? WindowsDefaultInterface()
    {
        const string script =
            "[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
            "Get-NetRoute -DestinationPrefix 0.0.0.0/0 -ErrorAction SilentlyContinue | " +
            $"Where-Object InterfaceAlias -ne '{SingBoxConfig.TunInterfaceName}' | " +
            "ForEach-Object { [pscustomobject]@{A=$_.InterfaceAlias; M=$_.RouteMetric + (Get-NetIPInterface -InterfaceIndex $_.ifIndex -AddressFamily IPv4).InterfaceMetric} } | " +
            "Sort-Object M | Select-Object -First 1 -ExpandProperty A";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
            {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(8000)) { p.Kill(); return null; }
            var name = output.Result.Trim();
            return name == "" ? null : name;
        }
        catch { return null; }
    }

    /// <summary>Adaptador "Ethernet" o, si no existe, el primer adaptador cableado activo.</summary>
    public static string? GuessEthernet(List<string> interfaces) =>
        interfaces.FirstOrDefault(n => n.Equals("Ethernet", StringComparison.OrdinalIgnoreCase))
        ?? NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet && n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name).FirstOrDefault(interfaces.Contains)
        ?? interfaces.FirstOrDefault();
}
