using System.ServiceProcess;

namespace AppRouter;

public class MainForm : Form
{
    readonly AppSettings settings;
    List<string> interfaces = [];
    bool dirty;

    readonly TableLayoutPanel rows = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 3,
        Padding = new Padding(8, 4, 8, 4),
    };
    readonly ComboBox newCombo = MakeComboShell();
    readonly ComboBox defaultCombo = MakeComboShell();
    readonly Button saveButton = new() { Text = "Guardar y aplicar", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button checkButton = new() { Text = "Comprobar…", AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolTip tips = new();
    readonly NotifyIcon tray = new();
    bool allowShow;      // false mientras arranca oculto (inicio de sesión)
    bool exiting;        // true solo al salir de verdad desde el menú del icono
    bool hintShown;

    public MainForm(bool startHidden = false) : this(AppSettings.Load(), startHidden) { }

    internal MainForm(AppSettings settings, bool startHidden)
    {
        this.settings = settings;
        allowShow = !startHidden;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Paths.AppName;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(480, 560);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        // Cabecera: NOMBRE | INTERFAZ
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 3, Padding = new Padding(8, 0, 8, 0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        header.Controls.Add(HeaderLabel("NOMBRE"), 0, 0);
        header.Controls.Add(HeaderLabel("INTERFAZ"), 1, 0);
        var headerLine = new Label { Dock = DockStyle.Top, Height = 1, BackColor = SystemColors.ControlDark };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Window };
        scroll.Controls.Add(rows);

        // Pie: resto del tráfico + botones + estado
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 112, ColumnCount = 3, RowCount = 3, Padding = new Padding(8, 6, 8, 4) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var refresh = new Button { Text = "↻", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left };
        tips.SetToolTip(refresh, "Volver a leer los adaptadores de red");
        refresh.Click += (_, _) => { LoadInterfaces(); Rebuild(); };
        defaultCombo.SelectedIndexChanged += (_, _) => dirty = true;
        footer.Controls.Add(new Label { Text = "Resto del tráfico:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        footer.Controls.Add(defaultCombo, 1, 0);
        footer.Controls.Add(refresh, 2, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        saveButton.Click += async (_, _) => await SaveAndApply();
        checkButton.Click += async (_, _) => await OpenCheck();
        tips.SetToolTip(checkButton, "Ver en directo por qué conexión sale cada programa");
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(checkButton);
        footer.Controls.Add(buttons, 0, 1);
        footer.SetColumnSpan(buttons, 3);
        footer.Controls.Add(status, 0, 2);
        footer.SetColumnSpan(status, 3);
        var footerLine = new Label { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        Controls.Add(scroll);
        Controls.Add(headerLine);
        Controls.Add(header);
        Controls.Add(footerLine);
        Controls.Add(footer);

        DragEnter += (_, e) =>
            e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Any(IsExe) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddPrograms(files.Where(IsExe));
        };
        FormClosing += OnClosing;
        FormClosed += (_, _) => { tray.Visible = false; tray.Dispose(); };

        ResumeLayout(false);
        PerformLayout();

        LoadInterfaces();
        Rebuild();
        dirty = false;
        SetupTray();
        ShowServiceStatus();
        _ = EnsureRoutingAsync();
    }

    // ---------- Área de notificación ----------

    void SetupTray()
    {
        Icon = AppIcon.Load(new Size(32, 32));
        tray.Icon = AppIcon.Load();
        tray.Text = Paths.AppName;
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Abrir AppRouter", null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add("Comprobar…", null, async (_, _) => { ShowFromTray(); await OpenCheck(); });
        menu.Items.Add(new ToolStripSeparator());
        var autostart = new ToolStripMenuItem("Iniciar con Windows") { CheckOnClick = true };
        menu.Opening += (_, _) => autostart.Checked = Autostart.IsEnabled();
        autostart.Click += (_, _) =>
        {
            try { if (autostart.Checked) Autostart.Enable(); else Autostart.Disable(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        menu.Items.Add(autostart);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir y desactivar el enrutado", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.Visible = true;
    }

    /// <summary>Muestra la ventana (desde el icono, o cuando se abre AppRouter estando ya en marcha).</summary>
    public void ShowFromTray()
    {
        allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // Permite arrancar oculto (solo icono) al iniciar sesión.
    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow)
        {
            value = false;
            if (!IsHandleCreated) CreateHandle();
        }
        base.SetVisibleCore(value);
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (exiting) return;
        if (e.CloseReason == CloseReason.UserClosing)
        {
            // La X solo oculta: el enrutado sigue activo mientras AppRouter esté abierto.
            e.Cancel = true;
            Hide();
            if (!hintShown)
            {
                tray.ShowBalloonTip(5000, Paths.AppName,
                    "Sigue funcionando aquí. Para desactivarlo: clic derecho en este icono → Salir.", ToolTipIcon.Info);
                hintShown = true;
            }
            return;
        }
        // Cierre de sesión / apagado: AppRouter se cierra, así que el enrutado también.
        try { SingBoxService.Stop(TimeSpan.FromSeconds(10)); } catch { /* el apagado lo parará igualmente */ }
    }

    /// <summary>Salir de verdad: detiene el servicio y todo vuelve a salir por la conexión normal de Windows.</summary>
    void ExitApp()
    {
        ShowFromTray();
        if (dirty && MessageBox.Show(this, "Hay cambios sin aplicar que se perderán. ¿Salir igualmente?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        UseWaitCursor = true;
        var windowsDefault = AppSettings.WindowsDefaultInterface() ?? settings.DefaultInterface ?? "la conexión por defecto";
        UseWaitCursor = false;
        if (MessageBox.Show(this,
                "Al salir se desactiva el enrutado por programa.\n\n" +
                $"Todas las conexiones volverán a salir por la conexión por defecto de Windows: {windowsDefault}.\n\n" +
                "Las conexiones abiertas ahora mismo (por ejemplo, descargas en curso) se cortarán un momento.\n\n¿Salir?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        UseWaitCursor = true;
        try { SingBoxService.Stop(TimeSpan.FromSeconds(20)); }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, "No se pudo detener el servicio:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        exiting = true;
        tray.Visible = false;
        Application.Exit();
    }

    /// <summary>Abrir AppRouter activa el enrutado (el servicio es de inicio manual).</summary>
    async Task EnsureRoutingAsync()
    {
        if (SingBoxService.Status() is not ServiceControllerStatus.Stopped || !File.Exists(Paths.ConfigPath)) return;
        status.Text = "Activando el enrutado…";
        try
        {
            await Task.Run(() => SingBoxService.Restart(TimeSpan.FromSeconds(20)));
            ShowServiceStatus();
        }
        catch (Exception ex)
        {
            status.Text = "No se pudo activar el enrutado.";
            tray.ShowBalloonTip(5000, Paths.AppName, "No se pudo activar el enrutado: " + ex.Message, ToolTipIcon.Error);
        }
    }

    // ---------- Adaptadores ----------

    void LoadInterfaces()
    {
        interfaces = AppSettings.GetInterfaces();
        var def = settings.DefaultInterface ?? AppSettings.GuessEthernet(interfaces);
        FillCombo(defaultCombo, defaultCombo.SelectedItem as string ?? def);
        FillCombo(newCombo, newCombo.SelectedItem as string ?? def);
    }

    void FillCombo(ComboBox combo, string? selected)
    {
        combo.Items.Clear();
        foreach (var i in interfaces) combo.Items.Add(i);
        // Si el adaptador guardado ya no existe, se conserva para no perder la regla.
        if (!string.IsNullOrEmpty(selected) && !combo.Items.Contains(selected)) combo.Items.Add(selected);
        combo.SelectedItem = selected;
    }

    // ---------- Filas ----------

    void Rebuild()
    {
        rows.SuspendLayout();
        rows.Controls.Clear();
        rows.RowStyles.Clear();
        rows.RowCount = 0;

        foreach (var rule in settings.Rules)
        {
            var name = new Label
            {
                Text = rule.ProcessName,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font.FontFamily, 10.5f),
            };
            tips.SetToolTip(name, rule.Path);

            var combo = MakeComboShell();
            FillCombo(combo, rule.Interface);
            combo.SelectedIndexChanged += (_, _) => { rule.Interface = combo.SelectedItem as string ?? ""; dirty = true; };

            var remove = new Button { Text = "✕", Width = LogicalToDeviceUnits(28), Height = LogicalToDeviceUnits(27), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None };
            remove.FlatAppearance.BorderSize = 0;
            tips.SetToolTip(remove, "Quitar");
            remove.Click += (_, _) => { settings.Rules.Remove(rule); dirty = true; Rebuild(); };

            AddRow(name, combo, remove);
        }

        var add = new LinkLabel
        {
            Text = "+ Añadir programa",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        add.LinkClicked += (_, _) => BrowsePrograms();
        tips.SetToolTip(newCombo, "Interfaz para el programa que se añada");
        AddRow(add, newCombo, null);

        rows.ResumeLayout();
    }

    void AddRow(Control c0, Control c1, Control? c2)
    {
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(36)));
        var r = rows.RowCount++;
        rows.Controls.Add(c0, 0, r);
        rows.Controls.Add(c1, 1, r);
        if (c2 is not null) rows.Controls.Add(c2, 2, r);
    }

    void BrowsePrograms()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Elegir programa",
            Filter = "Programas (*.exe)|*.exe",
            Multiselect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) AddPrograms(dlg.FileNames);
    }

    void AddPrograms(IEnumerable<string> paths)
    {
        var iface = newCombo.SelectedItem as string ?? defaultCombo.SelectedItem as string ?? "";
        foreach (var p in paths)
        {
            if (settings.Rules.Any(r => r.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
            settings.Rules.Add(new ProgramRule { Path = p, Interface = iface });
            dirty = true;
        }
        Rebuild();
    }

    // ---------- Guardar y comprobar ----------

    async Task<bool> SaveAndApply()
    {
        if (defaultCombo.SelectedItem is not string finalIface)
        {
            MessageBox.Show(this, "Elige la conexión para el resto del tráfico.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var missing = settings.Rules.Where(r => !interfaces.Contains(r.Interface)).Select(r => $"{r.ProcessName} → {r.Interface}").ToList();
        if (!interfaces.Contains(finalIface)) missing.Add($"(resto del tráfico) → {finalIface}");
        if (missing.Count > 0 && MessageBox.Show(this,
                "Estas conexiones no existen ahora mismo en el equipo:\n\n" + string.Join("\n", missing) + "\n\n¿Guardar igualmente?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

        settings.DefaultInterface = finalIface;
        var plan = SingBoxService.Plan(settings, finalIface);
        var running = SingBoxService.Status() == ServiceControllerStatus.Running;

        // Solo un adaptador nuevo obliga a reiniciar sing-box, y eso corta lo que esté abierto.
        if (plan.NeedsRestart && running && MessageBox.Show(this,
                (plan.NewInterfaces.Count > 0
                    ? "Hay conexiones de red nuevas en el equipo:\n\n" + string.Join("\n", plan.NewInterfaces) + "\n\n"
                    : "") +
                "Para aplicar este cambio hay que reiniciar el servicio, y se cortarán un momento todas las conexiones " +
                "abiertas (por ejemplo, las descargas en curso, que se reanudarán solas si el programa lo permite).\n\n¿Continuar?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

        UseWaitCursor = true;
        saveButton.Enabled = checkButton.Enabled = false;
        status.Text = "Aplicando…";
        try
        {
            var error = await Task.Run(() => SingBoxService.Commit(plan));
            if (error is not null)
            {
                status.Text = "La configuración no es válida; no se ha aplicado.";
                MessageBox.Show(this, "sing-box ha rechazado la configuración:\n\n" + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            settings.AppliedAt = DateTimeOffset.UtcNow;
            settings.Save();
            dirty = false;

            var restarted = plan.NeedsRestart || !running;
            if (restarted)
            {
                status.Text = "Reiniciando el servicio…";
                await Task.Run(() => SingBoxService.Restart(TimeSpan.FromSeconds(20)));
            }

            // El servicio puede estar "en ejecución" aunque sing-box no haya podido arrancar.
            using var api = new SingBoxApi(settings.ApiPort, settings.ApiSecret);
            var alive = await api.IsAliveAsync();
            for (var i = 0; i < 20 && !alive; i++)
            {
                await Task.Delay(500);
                alive = await api.IsAliveAsync();
            }
            if (!alive)
            {
                status.Text = "sing-box no está funcionando.";
                MessageBox.Show(this, "Se guardó la configuración, pero sing-box no está funcionando.\n\nÚltimas líneas del registro:\n\n" + LogTail(12),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            status.Text = restarted
                ? $"✔ Aplicado a las {DateTime.Now:HH:mm:ss} (servicio reiniciado). Funcionando."
                : $"✔ Aplicado a las {DateTime.Now:HH:mm:ss} sin cortar conexiones. Las ya abiertas siguen por donde iban.";
            tips.SetToolTip(status, status.Text);
            return true;
        }
        catch (Exception ex)
        {
            status.Text = "Error al aplicar.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            UseWaitCursor = false;
            saveButton.Enabled = checkButton.Enabled = true;
        }
    }

    async Task OpenCheck()
    {
        if (dirty)
        {
            if (MessageBox.Show(this, "Hay cambios sin aplicar. Para comprobarlos hay que guardarlos primero.\n\n¿Guardar y aplicar ahora?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (!await SaveAndApply()) return;
        }
        // Se compara con lo guardado (lo que sing-box está usando), no con la pantalla.
        using var f = new CheckForm(AppSettings.Load());
        f.ShowDialog(this);
    }

    void ShowServiceStatus()
    {
        status.Text = SingBoxService.Status() switch
        {
            ServiceControllerStatus.Running => "● Enrutado activo.",
            null => "El servicio no está instalado.",
            var s => $"Enrutado desactivado (servicio: {s}). Pulsa «Guardar y aplicar» para activarlo.",
        };
        tray.Text = Paths.AppName + (SingBoxService.Status() == ServiceControllerStatus.Running ? " · activo" : " · desactivado");
    }

    static string LogTail(int lines)
    {
        try
        {
            using var fs = new FileStream(Paths.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs);
            var all = sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", all.TakeLast(lines));
        }
        catch { return "(sin registro)"; }
    }

    // ---------- Utilidades ----------

    static ComboBox MakeComboShell() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };

    static Label HeaderLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        ForeColor = SystemColors.GrayText,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
    };

    static bool IsExe(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}
