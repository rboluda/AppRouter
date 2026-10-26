using System.Diagnostics;
using System.ServiceProcess;

namespace AppRouter;

internal static class Program
{
    const string InstanceMutex = @"Local\AppRouter.Gui";
    const string ShowEvent = @"Local\AppRouter.Show";

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--service"))
        {
            ServiceBase.Run(new SingBoxHostService());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        L.Set(File.Exists(Paths.SettingsPath) ? AppSettings.Load().Language : null);

        if (args.Contains("--uninstall"))
        {
            if (MessageBox.Show(L.T("¿Desinstalar AppRouter?") + "\n\n" + L.T("Todo el tráfico volverá a salir por la conexión normal de Windows."),
                    Paths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            try
            {
                Installer.Uninstall();
                MessageBox.Show(L.T("AppRouter se ha desinstalado."), Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // Actualización automática: el instalador descargado se ejecuta sin ventanas y reabre AppRouter.
        if (args.Contains("--update") && !Paths.RunningFromInstallDir)
            return SilentUpdate();

        // El .exe descargado (fuera de Program Files) hace de instalador / actualizador.
        if (!Paths.RunningFromInstallDir || !SingBoxService.Exists())
        {
            Application.Run(new InstallForm());
            return 0;
        }

        // Una sola copia: si ya está abierta (p. ej. en el área de notificación), se le pide que se muestre.
        // Con --restart (cambio de idioma) se espera a que la copia anterior termine de cerrarse.
        using var mutex = new Mutex(false, InstanceMutex);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
        if (!Acquire(mutex, args.Contains("--restart") ? TimeSpan.FromSeconds(15) : TimeSpan.Zero))
        {
            show.Set();
            return 0;
        }

        var form = new MainForm(startHidden: args.Contains("--tray"), updated: args.Contains("--updated"));
        var listener = new Thread(() =>
        {
            while (show.WaitOne())
                if (!form.IsDisposed) form.BeginInvoke(form.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();

        Application.Run(form);
        return 0;
    }

    static int SilentUpdate()
    {
        var log = Path.Combine(Paths.DataDir, "update.log");
        void Log(string m)
        {
            try { File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {m}{Environment.NewLine}"); } catch { }
        }
        try
        {
            Log($"Actualizando a {Paths.Version}…");
            Installer.Install(new Progress<string>(Log));
            Log("Actualización terminada.");
            Process.Start(new ProcessStartInfo(Paths.InstalledExe, "--tray --updated") { UseShellExecute = false, WorkingDirectory = Paths.InstallDir });
            return 0;
        }
        catch (Exception ex)
        {
            Log("Error: " + ex);
            MessageBox.Show(L.T("No se pudo instalar la actualización de AppRouter:") + "\n\n" + ex.Message + "\n\n" +
                            L.F("Detalles en {0}", log), Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            // Se reabre la versión instalada (la anterior, o la nueva si llegó a copiarse) para no dejar el enrutado apagado.
            if (File.Exists(Paths.InstalledExe))
                try { Process.Start(new ProcessStartInfo(Paths.InstalledExe, "--tray") { UseShellExecute = false, WorkingDirectory = Paths.InstallDir }); } catch { }
            return 1;
        }
    }

    static bool Acquire(Mutex m, TimeSpan wait)
    {
        try { return m.WaitOne(wait); }
        catch (AbandonedMutexException) { return true; } // la copia anterior se cerró sin liberarlo: ya es nuestro
    }
}
