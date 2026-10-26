using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.ServiceProcess;
using Microsoft.Win32;

namespace AppRouter;

/// <summary>
/// Instalación autocontenida: copia el propio .exe y sing-box a Program Files, registra el
/// servicio (inicio manual: lo arranca la app), el inicio automático al iniciar sesión,
/// los accesos directos y la entrada en "Aplicaciones instaladas".
/// </summary>
public static class Installer
{
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppRouter";
    const string ShortcutName = Paths.AppName + ".lnk";

    static string StartMenuDir => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public static bool IsInstalled => File.Exists(Paths.InstalledExe) && SingBoxService.Exists();

    public static void Install(IProgress<string> progress)
    {
        progress.Report(L.T("Cerrando versiones anteriores…"));
        // Primero se para el servicio (si se matara su proceso, Windows lo relanzaría a los 5 s
        // por la recuperación ante fallos, en mitad de la copia) y después las ventanas abiertas.
        var wasInstalled = SingBoxService.Exists();
        if (wasInstalled) StopService(Paths.ServiceName);
        KillOtherAppRouterInstances();
        MigrateLegacy();
        KillSingBoxUnder(Paths.InstallDir);

        progress.Report(L.T("Copiando archivos…"));
        Directory.CreateDirectory(Paths.InstallDir);
        if (!Paths.RunningFromInstallDir)
            File.Copy(Paths.CurrentExe, Paths.InstalledExe, overwrite: true);

        progress.Report(L.F("Instalando sing-box {0}…", Paths.SingBoxVersion));
        ExtractSingBox();

        progress.Report(L.T("Preparando la configuración…"));
        var settings = AppSettings.Load();
        var interfaces = AppSettings.GetInterfaces();
        settings.DefaultInterface ??= AppSettings.GuessEthernet(interfaces);
        if (settings.DefaultInterface is null)
            throw new InvalidOperationException(L.T("No se ha encontrado ningún adaptador de red."));
        var error = SingBoxService.Commit(SingBoxService.Plan(settings));
        if (error is not null) throw new InvalidOperationException(L.T("Configuración inicial no válida:") + "\n" + error);
        settings.AppliedAt = DateTimeOffset.UtcNow;
        settings.Save();

        progress.Report(L.T("Registrando el servicio…"));
        // Inicio manual: el enrutado solo está activo mientras AppRouter está abierto.
        var bin = $"\\\"{Paths.InstalledExe}\\\" --service";
        if (wasInstalled)
            Sc($"config {Paths.ServiceName} binPath= \"{bin}\" start= demand");
        else
            Sc($"create {Paths.ServiceName} binPath= \"{bin}\" start= demand DisplayName= \"{Paths.AppName} (sing-box)\"");
        Sc($"description {Paths.ServiceName} \"{L.F("Envía el tráfico de cada programa por el adaptador de red elegido en AppRouter (sing-box {0}).", Paths.SingBoxVersion)}\"");
        Sc($"failure {Paths.ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000");

        progress.Report(L.T("Creando accesos directos…"));
        CreateShortcut(Path.Combine(StartMenuDir, ShortcutName));
        CreateShortcut(Path.Combine(DesktopDir, ShortcutName));
        Autostart.Enable();
        RegisterUninstall();
    }

    public static void Uninstall()
    {
        if (SingBoxService.Exists())
        {
            StopService(Paths.ServiceName);
            Sc($"delete {Paths.ServiceName}", throwOnError: false);
        }
        KillOtherAppRouterInstances();
        KillSingBoxUnder(Paths.InstallDir);
        Autostart.Disable();
        foreach (var s in new[] { Path.Combine(StartMenuDir, ShortcutName), Path.Combine(DesktopDir, ShortcutName) })
            if (File.Exists(s)) File.Delete(s);
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        try { Directory.Delete(Paths.DataDir, recursive: true); } catch { /* log abierto, etc. */ }

        // El .exe en uso no puede borrarse a sí mismo: lo hace cmd cuando cerremos.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{Paths.InstallDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    /// <summary>
    /// Quita la instalación de las primeras versiones ("SingBoxRouter" / "Enrutado por programa"),
    /// conservando su lista de programas.
    /// </summary>
    static void MigrateLegacy()
    {
        var legacyService = ServiceController.GetServices()
            .Any(s => s.ServiceName.Equals(Paths.Legacy.ServiceName, StringComparison.OrdinalIgnoreCase));
        if (!legacyService && !Directory.Exists(Paths.Legacy.InstallDir)) return;

        if (legacyService)
        {
            StopService(Paths.Legacy.ServiceName);
            Sc($"delete {Paths.Legacy.ServiceName}", throwOnError: false);
        }
        foreach (var p in Process.GetProcessesByName("SingBoxRouter"))
            using (p) try { p.Kill(); p.WaitForExit(5000); } catch { /* ya cerrado */ }
        KillSingBoxUnder(Paths.Legacy.InstallDir);

        var oldSettings = Path.Combine(Paths.Legacy.DataDir, "settings.json");
        if (File.Exists(oldSettings) && !File.Exists(Paths.SettingsPath))
        {
            Directory.CreateDirectory(Paths.DataDir);
            File.Copy(oldSettings, Paths.SettingsPath);
        }

        foreach (var s in new[] { Path.Combine(StartMenuDir, Paths.Legacy.ShortcutName), Path.Combine(DesktopDir, Paths.Legacy.ShortcutName) })
            try { if (File.Exists(s)) File.Delete(s); } catch { }
        Registry.LocalMachine.DeleteSubKeyTree(Paths.Legacy.UninstallKey, throwOnMissingSubKey: false);
        try { Directory.Delete(Paths.Legacy.InstallDir, recursive: true); } catch { }
        try { Directory.Delete(Paths.Legacy.DataDir, recursive: true); } catch { }
    }

    static void StopService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)) sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        catch { /* no existe o ya parado */ }
    }

    /// <summary>Cierra otras copias de AppRouter (p. ej. la del área de notificación) para poder sustituir el .exe.</summary>
    static void KillOtherAppRouterInstances()
    {
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("AppRouter"))
            using (p)
                if (p.Id != me)
                    try { p.Kill(); p.WaitForExit(5000); } catch { /* ya cerrado */ }
    }

    static void ExtractSingBox()
    {
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("sing-box.zip")
                        ?? throw new InvalidOperationException(L.T("Este ejecutable no incluye sing-box."));
        using var zip = new ZipArchive(res);
        Directory.CreateDirectory(Paths.SingBoxDir);
        foreach (var entry in zip.Entries.Where(e => e.Name != ""))
        {
            // Se aplana la carpeta "sing-box-x.y.z-windows-amd64/" del zip.
            var name = entry.Name == "LICENSE" ? "LICENSE-sing-box.txt" : entry.Name;
            entry.ExtractToFile(Path.Combine(Paths.SingBoxDir, name), overwrite: true);
        }
        File.WriteAllText(Path.Combine(Paths.SingBoxDir, "LEEME.txt"),
            $"sing-box {Paths.SingBoxVersion} sin modificar, distribuido bajo GPLv3 (ver LICENSE-sing-box.txt).\r\n" +
            $"Código fuente: {Paths.SingBoxSourceUrl}\r\n");
    }

    static void KillSingBoxUnder(string dir)
    {
        foreach (var p in Process.GetProcessesByName("sing-box"))
        {
            try
            {
                if (p.MainModule?.FileName?.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true)
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { /* proceso ajeno o ya terminado */ }
            finally { p.Dispose(); }
        }
    }

    internal static string Run(string exe, string args, bool throwOnError = true)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && throwOnError)
            throw new InvalidOperationException(L.F("{0} {1} falló ({2}):", Path.GetFileName(exe), args.Split(' ')[0], p.ExitCode) + "\n" + output.Trim());
        return output;
    }

    static void Sc(string args, bool throwOnError = true) => Run("sc.exe", args, throwOnError);

    static void CreateShortcut(string path)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic lnk = shell.CreateShortcut(path);
            lnk.TargetPath = Paths.InstalledExe;
            lnk.WorkingDirectory = Paths.InstallDir;
            lnk.Description = L.T("Elige por qué conexión de red sale cada programa.");
            lnk.IconLocation = Paths.InstalledExe + ",0";
            lnk.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    static void RegisterUninstall()
    {
        using var k = Registry.LocalMachine.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", Paths.AppName);
        k.SetValue("DisplayVersion", Paths.Version);
        k.SetValue("DisplayIcon", Paths.InstalledExe);
        k.SetValue("InstallLocation", Paths.InstallDir);
        k.SetValue("UninstallString", $"\"{Paths.InstalledExe}\" --uninstall");
        if (Paths.ProjectUrl != "") k.SetValue("URLInfoAbout", Paths.ProjectUrl);
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        var kb = Directory.EnumerateFiles(Paths.InstallDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
        k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
    }
}
