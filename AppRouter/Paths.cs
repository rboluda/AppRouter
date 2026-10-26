using System.Reflection;

namespace AppRouter;

/// <summary>Ubicaciones fijas de la instalación.</summary>
public static class Paths
{
    public const string AppName = "AppRouter";
    public const string ServiceName = "AppRouter";
    /// <summary>Versión de sing-box incluida (de &lt;SingBoxVersion&gt; en el .csproj).</summary>
    public static string SingBoxVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SingBoxVersion")?.Value ?? "?";
    public static string SingBoxSourceUrl => $"https://github.com/SagerNet/sing-box/tree/v{SingBoxVersion}";

    /// <summary>Página del proyecto (repositorio de GitHub).</summary>
    public static readonly string ProjectUrl = "https://github.com/rboluda/approuter";

    /// <summary>Versión de la app (de &lt;Version&gt; en el .csproj), sin el sufijo "+commit".</summary>
    public static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AppRouter");
    public static string InstalledExe => Path.Combine(InstallDir, "AppRouter.exe");
    public static string SingBoxDir => Path.Combine(InstallDir, "sing-box");
    public static string SingBoxExe => Path.Combine(SingBoxDir, "sing-box.exe");

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppRouter");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string ConfigPath => Path.Combine(DataDir, "config.json");
    public static string RulesDir => Path.Combine(DataDir, "rules");
    public static string LimitsPath => Path.Combine(DataDir, "limits.json");
    public static string LogPath => Path.Combine(DataDir, "sing-box.log");

    public static string CurrentExe => Environment.ProcessPath!;

    public static bool RunningFromInstallDir =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>Instalación de las primeras versiones (antes de llamarse AppRouter), para migrarla.</summary>
    public static class Legacy
    {
        public const string ServiceName = "SingBoxRouter";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SingBoxRouter";
        public const string ShortcutName = "Enrutado por programa.lnk";
        public static string InstallDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SingBoxRouter");
        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SingBoxRouter");
    }
}
