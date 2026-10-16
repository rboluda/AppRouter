using System.Diagnostics;

namespace AppRouter;

/// <summary>Asistente de una sola pantalla que aparece al abrir el .exe descargado.</summary>
public class InstallForm : Form
{
    readonly Label info = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 16, 16, 0) };
    readonly Label step = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(16, 0, 16, 0), ForeColor = SystemColors.GrayText };
    readonly ProgressBar bar = new() { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Marquee, Visible = false };
    readonly Button install = new() { AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
    readonly Button cancel = new() { Text = "Cancelar", AutoSize = true, Padding = new Padding(8, 3, 8, 3) };

    public InstallForm()
    {
        var update = Installer.IsInstalled;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{Paths.AppName} {Paths.Version}" + (update ? " — Actualizar" : " — Instalar");
        Font = new Font("Segoe UI", 10f);
        Icon = AppIcon.Load(new Size(32, 32));
        ClientSize = new Size(560, 380);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var legacy = Directory.Exists(Paths.Legacy.InstallDir);
        info.Text = update
            ? $"Ya está instalado en este equipo.\n\nPulsa «Actualizar» para pasar a la versión {Paths.Version}. Tu lista de programas se conserva.\n\nDurante la actualización se cortarán un momento las conexiones abiertas (por ejemplo, descargas en curso)."
            : "AppRouter permite elegir por qué conexión de red (cable, Wi-Fi…) sale a internet cada programa.\n\n" +
              "Se instalará en «Archivos de programa», con acceso directo en el escritorio y en el menú Inicio, y se abrirá solo al iniciar sesión " +
              "(junto al reloj). Mientras AppRouter esté abierto, cada programa sale por la conexión que elijas; al salir de AppRouter todo vuelve a la normalidad.\n\n" +
              (legacy ? "Sustituye a la versión anterior («Enrutado por programa») y conserva su lista de programas.\n\n" : "") +
              $"Incluye sing-box {Paths.SingBoxVersion} (licencia GPLv3).";

        install.Text = update ? "Actualizar" : "Instalar";
        install.Click += async (_, _) => await RunInstall();
        cancel.Click += (_, _) => Close();
        AcceptButton = install;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(install);

        Controls.Add(info);
        Controls.Add(step);
        Controls.Add(bar);
        Controls.Add(buttons);
        ResumeLayout(false);
        PerformLayout();
    }

    async Task RunInstall()
    {
        install.Enabled = cancel.Enabled = false;
        bar.Visible = true;
        UseWaitCursor = true;
        var progress = new Progress<string>(s => step.Text = s);
        try
        {
            await Task.Run(() => Installer.Install(progress));
            bar.Visible = false;
            UseWaitCursor = false;
            MessageBox.Show(this, "Instalación terminada.\n\nAhora se abrirá AppRouter: añade los programas y elige su conexión. Al cerrar la ventana seguirá funcionando junto al reloj.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo(Paths.InstalledExe) { UseShellExecute = true, WorkingDirectory = Paths.InstallDir });
            Close();
        }
        catch (Exception ex)
        {
            bar.Visible = false;
            UseWaitCursor = false;
            step.Text = "La instalación no se ha completado.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = cancel.Enabled = true;
        }
    }
}
