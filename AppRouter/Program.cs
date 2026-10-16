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

        if (args.Contains("--uninstall"))
        {
            if (MessageBox.Show($"¿Desinstalar {Paths.AppName}?\n\nTodo el tráfico volverá a salir por la conexión normal de Windows.",
                    Paths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return 1;
            try
            {
                Installer.Uninstall();
                MessageBox.Show($"{Paths.AppName} se ha desinstalado.", Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        // El .exe descargado (fuera de Program Files) hace de instalador / actualizador.
        if (!Paths.RunningFromInstallDir || !SingBoxService.Exists())
        {
            Application.Run(new InstallForm());
            return 0;
        }

        // Una sola copia: si ya está abierta (p. ej. en el área de notificación), se le pide que se muestre.
        using var mutex = new Mutex(true, InstanceMutex, out var first);
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
        if (!first)
        {
            show.Set();
            return 0;
        }

        var form = new MainForm(startHidden: args.Contains("--tray"));
        var listener = new Thread(() =>
        {
            while (show.WaitOne())
                if (!form.IsDisposed) form.BeginInvoke(form.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();

        Application.Run(form);
        return 0;
    }
}
