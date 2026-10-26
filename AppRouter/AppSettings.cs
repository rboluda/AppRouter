using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppRouter;

public class ProgramRule
{
    public string Path { get; set; } = "";

    /// <summary>Nombre del adaptador, o <see cref="Routing.Blocked"/> para dejar el programa sin internet.</summary>
    public string Interface { get; set; } = "";

    /// <summary>Límite de velocidad en megabits por segundo (bajada y subida, cada una); 0 = ilimitado.</summary>
    public double LimitMbps { get; set; }

    [JsonIgnore]
    public string ProcessName => System.IO.Path.GetFileName(Path);
}

/// <summary>Qué hacer con un programa cuando se desconecta la interfaz que tiene asignada.</summary>
public enum DisconnectAction
{
    /// <summary>Se queda sin internet hasta que vuelva (nunca sale por otra conexión).</summary>
    Block,
    /// <summary>Usa la conexión del resto del tráfico mientras tanto.</summary>
    Fallback,
}

/// <summary>Estado de la app, persistido en %ProgramData%\AppRouter\settings.json.</summary>
public class AppSettings
{
    public string? DefaultInterface { get; set; }
    public List<string> ExcludedSubnets { get; set; } = ["10.0.0.0/8"];
    public List<ProgramRule> Rules { get; set; } = [];

    // --- Preferencias (ventana Configuración) ---

    /// <summary>"es", "en", "zh" o null (idioma del sistema).</summary>
    public string? Language { get; set; }

    /// <summary>Interfaces que se ofrecen en los desplegables. Vacía = todas.</summary>
    public List<string> VisibleInterfaces { get; set; } = [];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DisconnectAction OnDisconnect { get; set; } = DisconnectAction.Block;

    public bool NotifyNetworkChanges { get; set; } = true;

    /// <summary>Consultar a GitHub si hay versión nueva (desactivado por defecto: ver la sección Privacy del README).</summary>
    public bool CheckUpdates { get; set; }
    public DateTimeOffset? LastUpdateCheck { get; set; }

    // --- Interno ---

    /// <summary>
    /// Adaptadores con salida en config.json. Solo crece: así quitar o desconectar un adaptador
    /// no obliga a reiniciar sing-box (y cortar conexiones); solo lo hace uno nuevo.
    /// </summary>
    public List<string> KnownInterfaces { get; set; } = [];

    /// <summary>Último cambio de reglas (UTC): las conexiones anteriores pueden ir aún por la salida vieja.</summary>
    public DateTimeOffset? AppliedAt { get; set; }
    /// <summary>API local de sing-box (solo 127.0.0.1) que usan la comprobación y el tráfico por programa.</summary>
    public int ApiPort { get; set; } = 9097;
    public string ApiSecret { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    /// <summary>Puerto local del limitador de velocidad (dentro del servicio).</summary>
    public int LimiterPort { get; set; } = 9098;

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
            MessageBox.Show(L.F("No se pudo leer {0}:\n{1}\n\nSe empezará con una lista vacía.", Paths.SettingsPath, ex.Message),
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(Paths.SettingsPath, JsonSerializer.Serialize(this, JsonOpts));
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts))!;

    /// <summary>Copia las preferencias de la ventana Configuración (no la lista de programas).</summary>
    public void CopyPreferencesFrom(AppSettings o)
    {
        Language = o.Language;
        VisibleInterfaces = [.. o.VisibleInterfaces];
        OnDisconnect = o.OnDisconnect;
        ExcludedSubnets = [.. o.ExcludedSubnets];
        NotifyNetworkChanges = o.NotifyNetworkChanges;
        CheckUpdates = o.CheckUpdates;
    }

    // --- Exportar / importar ---

    public sealed class Exported
    {
        public string App { get; set; } = Paths.AppName;
        public int Format { get; set; } = 1;
        public string? DefaultInterface { get; set; }
        public List<ProgramRule> Rules { get; set; } = [];
        public List<string> ExcludedSubnets { get; set; } = [];
        public List<string> VisibleInterfaces { get; set; } = [];
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DisconnectAction OnDisconnect { get; set; }
        public string? Language { get; set; }
        public bool NotifyNetworkChanges { get; set; } = true;
        public bool CheckUpdates { get; set; }
    }

    public string Export() => JsonSerializer.Serialize(new Exported
    {
        DefaultInterface = DefaultInterface,
        Rules = Rules,
        ExcludedSubnets = ExcludedSubnets,
        VisibleInterfaces = VisibleInterfaces,
        OnDisconnect = OnDisconnect,
        Language = Language,
        NotifyNetworkChanges = NotifyNetworkChanges,
        CheckUpdates = CheckUpdates,
    }, JsonOpts);

    /// <summary>Carga un archivo exportado sobre estos ajustes. Lanza excepción si no es válido.</summary>
    public void Import(string json)
    {
        var e = JsonSerializer.Deserialize<Exported>(json)
                ?? throw new InvalidDataException(L.T("El archivo está vacío."));
        if (e.App != Paths.AppName || e.Format != 1)
            throw new InvalidDataException(L.T("No es un archivo de configuración de AppRouter."));
        foreach (var s in e.ExcludedSubnets)
            if (!IPNetwork.TryParse(s, out _))
                throw new InvalidDataException(L.F("Red no válida en el archivo: {0}", s));

        DefaultInterface = e.DefaultInterface;
        Rules = e.Rules.Where(r => r.Path != "" && r.Interface != "").ToList();
        ExcludedSubnets = e.ExcludedSubnets;
        VisibleInterfaces = e.VisibleInterfaces;
        OnDisconnect = e.OnDisconnect;
        Language = e.Language;
        NotifyNetworkChanges = e.NotifyNetworkChanges;
        CheckUpdates = e.CheckUpdates;
    }

    // --- Adaptadores ---

    /// <summary>Adaptadores seleccionables (sin loopback ni el propio túnel), activos primero.</summary>
    public static List<string> GetInterfaces() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && !n.Name.Equals(SingBoxConfig.TunInterfaceName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.OperationalStatus == OperationalStatus.Up ? 0 : 1)
            .ThenBy(n => n.Name)
            .Select(n => n.Name)
            .ToList();

    /// <summary>Los adaptadores que se ofrecen en los desplegables (las preferidas, o todas).</summary>
    public List<string> GetSelectableInterfaces()
    {
        var all = GetInterfaces();
        if (VisibleInterfaces.Count == 0) return all;
        var visible = all.Where(i => VisibleInterfaces.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        return visible.Count > 0 ? visible : all; // las elegidas ya no existen: mejor mostrar todas que ninguna
    }

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
