using System.Diagnostics;
using System.ServiceProcess;

namespace AppRouter;

/// <summary>Valida y escribe el config de sing-box y controla el servicio de Windows.</summary>
public static class SingBoxService
{
    public static bool Exists() =>
        ServiceController.GetServices().Any(s => s.ServiceName.Equals(Paths.ServiceName, StringComparison.OrdinalIgnoreCase));

    public static ServiceControllerStatus? Status()
    {
        try
        {
            using var sc = new ServiceController(Paths.ServiceName);
            return sc.Status;
        }
        catch (InvalidOperationException) { return null; } // no existe
    }

    /// <summary>Ejecuta "sing-box check". Devuelve null si es válido, o el mensaje de error.</summary>
    public static string? Check(string configFile)
    {
        if (!File.Exists(Paths.SingBoxExe)) return null; // sin sing-box no se puede validar (modo desarrollo)
        var psi = new ProcessStartInfo(Paths.SingBoxExe)
        {
            ArgumentList = { "check", "-c", configFile },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000)) { p.Kill(); return L.T("sing-box check no respondió."); }
        return p.ExitCode == 0 ? null : (stderr.Result + stdout.Result).Trim();
    }

    /// <summary>Lo que hay que escribir y si eso obliga a reiniciar sing-box.</summary>
    public sealed record ApplyPlan(string Config, Dictionary<string, string> RuleSets, bool NeedsRestart, List<string> NewInterfaces);

    /// <summary>
    /// Prepara los cambios. Solo hace falta reiniciar (y cortar conexiones) si cambia config.json,
    /// es decir, si aparece un adaptador nuevo; cambiar programas o el adaptador por defecto
    /// solo toca los rule-sets, que sing-box recarga en caliente.
    /// </summary>
    public static ApplyPlan Plan(AppSettings settings)
    {
        var wanted = AppSettings.GetInterfaces()
            .Concat(settings.Rules.Select(r => r.Interface))
            .Append(settings.DefaultInterface ?? "")
            .Where(i => !string.IsNullOrWhiteSpace(i) && i is not (Routing.Blocked or Routing.Auto));
        var added = wanted.Except(settings.KnownInterfaces, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.KnownInterfaces = settings.KnownInterfaces.Concat(added)
            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList();

        var config = SingBoxConfig.BuildConfig(settings);
        var current = File.Exists(Paths.ConfigPath) ? File.ReadAllText(Paths.ConfigPath) : null;
        return new ApplyPlan(config, SingBoxConfig.BuildRuleSets(settings, NetworkState.Usable()), current != config, added);
    }

    /// <summary>
    /// Tras un cambio de red: vuelve a escribir solo los rule-sets (recarga en caliente, sin cortar nada).
    /// Devuelve true si alguno cambió.
    /// </summary>
    public static bool RefreshRuleSets(AppSettings settings, ISet<string> usable)
    {
        Directory.CreateDirectory(Paths.RulesDir);
        var changed = false;
        foreach (var (path, json) in SingBoxConfig.BuildRuleSets(settings, usable))
            changed |= WriteAtomic(path, json);
        return changed;
    }

    /// <summary>
    /// Escribe rule-sets y, si cambió, config.json (validado con sing-box check, con copia .bak).
    /// Devuelve null si todo va bien, o el error de validación.
    /// </summary>
    public static string? Commit(ApplyPlan plan)
    {
        Directory.CreateDirectory(Paths.RulesDir);
        foreach (var (path, json) in plan.RuleSets)
            WriteAtomic(path, json);

        if (!plan.NeedsRestart) return null;

        var tmp = Paths.ConfigPath + ".new";
        File.WriteAllText(tmp, plan.Config);
        var error = Check(tmp);
        if (error is not null)
        {
            File.Delete(tmp);
            return error;
        }
        if (File.Exists(Paths.ConfigPath)) File.Copy(Paths.ConfigPath, Paths.ConfigPath + ".bak", overwrite: true);
        File.Move(tmp, Paths.ConfigPath, overwrite: true);
        return null;
    }

    /// <summary>Escritura atómica (sing-box nunca lee un archivo a medias). No toca el archivo si no cambia.</summary>
    static bool WriteAtomic(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return false;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        // sing-box o el limitador pueden estar leyéndolo justo en ese instante: se reintenta un poco.
        for (var attempt = 1; ; attempt++)
        {
            try { File.Move(tmp, path, overwrite: true); return true; }
            catch (IOException) when (attempt < 10) { Thread.Sleep(50); }
        }
    }

    public static void Stop(TimeSpan timeout)
    {
        using var sc = new ServiceController(Paths.ServiceName);
        if (sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
    }

    public static void Restart(TimeSpan timeout)
    {
        Stop(timeout);
        using var sc = new ServiceController(Paths.ServiceName);
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }
}
