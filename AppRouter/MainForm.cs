using System.Diagnostics;
using System.Net.NetworkInformation;
using System.ServiceProcess;

namespace AppRouter;

public class MainForm : Form
{
    /// <summary>Elemento de los desplegables: el valor es la interfaz (o un destino especial), el texto lo que se ve.</summary>
    sealed record ComboItem(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    readonly AppSettings settings;
    List<string> interfaces = [];
    HashSet<string> usable = [];
    bool dirty;

    readonly TableLayoutPanel rows = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 4,
        Padding = new Padding(8, 4, 8, 4),
    };
    readonly ComboBox newCombo = MakeComboShell();
    readonly ComboBox defaultCombo = MakeComboShell();
    readonly Button saveButton = new() { AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    readonly Button checkButton = new() { AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
    readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolTip tips = new();
    readonly NotifyIcon tray = new();
    readonly TrafficTracker traffic = new();
    readonly System.Windows.Forms.Timer trafficTimer = new() { Interval = 2000 };
    readonly System.Windows.Forms.Timer networkDebounce = new() { Interval = 2500 };
    readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 6 * 60 * 60 * 1000 };
    readonly SingBoxApi api;
    HashSet<string> lastUsable = [];
    string? pendingUpdateUrl;
    bool allowShow;      // false mientras arranca oculto (inicio de sesión)
    bool exiting;        // true solo al salir de verdad (o al reiniciar para cambiar el idioma)
    bool hintShown;
    bool trafficBusy;
    bool filling;        // rellenando desplegables: no cuenta como cambio del usuario

    public MainForm(bool startHidden = false) : this(AppSettings.Load(), startHidden) { }

    /// <param name="preview">Solo la ventana, sin tocar el servicio, la red ni las reglas (capturas de pantalla).</param>
    internal MainForm(AppSettings settings, bool startHidden, bool preview = false)
    {
        this.settings = settings;
        api = new SingBoxApi(settings.ApiPort, settings.ApiSecret);
        allowShow = !startHidden;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = Paths.AppName;
        Font = new Font(L.FontName, 9.5f);
        ClientSize = new Size(610, 580);
        MinimumSize = new Size(540, 340);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));

        // Cabecera: NOMBRE | INTERFAZ | LÍMITE
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 30, ColumnCount = 4, Padding = new Padding(8, 0, 8, 0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        header.Controls.Add(HeaderLabel(L.T("NOMBRE")), 0, 0);
        header.Controls.Add(HeaderLabel(L.T("INTERFAZ")), 1, 0);
        header.Controls.Add(HeaderLabel(L.T("LÍMITE")), 2, 0);
        var headerLine = new Label { Dock = DockStyle.Top, Height = 1, BackColor = SystemColors.ControlDark };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Window };
        scroll.Controls.Add(rows);

        // Pie: resto del tráfico + botones + estado
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 112, ColumnCount = 3, RowCount = 3, Padding = new Padding(8, 6, 8, 4) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var refresh = new Button { Text = "↻", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left };
        tips.SetToolTip(refresh, L.T("Volver a leer los adaptadores de red"));
        refresh.Click += (_, _) => { LoadInterfaces(); Rebuild(); };
        defaultCombo.SelectedIndexChanged += (_, _) => { if (!filling) dirty = true; };
        footer.Controls.Add(new Label { Text = L.T("Resto del tráfico:"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        footer.Controls.Add(defaultCombo, 1, 0);
        footer.Controls.Add(refresh, 2, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        saveButton.Text = L.T("Guardar y aplicar");
        checkButton.Text = L.T("Comprobar…");
        saveButton.Click += async (_, _) => await SaveAndApply();
        checkButton.Click += async (_, _) => await OpenCheck();
        tips.SetToolTip(checkButton, L.T("Ver en directo por qué conexión sale cada programa"));
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(checkButton);
        footer.Controls.Add(buttons, 0, 1);
        footer.SetColumnSpan(buttons, 3);
        footer.Controls.Add(status, 0, 2);
        footer.SetColumnSpan(status, 3);
        var footerLine = new Label { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        var menu = BuildMenu();
        Controls.Add(scroll);
        Controls.Add(headerLine);
        Controls.Add(header);
        Controls.Add(footerLine);
        Controls.Add(footer);
        Controls.Add(menu);
        MainMenuStrip = menu;

        DragEnter += (_, e) =>
            e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] f && f.Any(IsExe) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) AddPrograms(files.Where(IsExe));
        };
        FormClosing += OnClosing;
        FormClosed += (_, _) =>
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkEvent;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkEvent;
            tray.Visible = false;
            tray.Dispose();
            api.Dispose();
        };

        ResumeLayout(false);
        PerformLayout();

        LoadInterfaces();
        Rebuild();
        dirty = false;
        SetupTray();
        ShowServiceStatus();
        if (preview) return;

        lastUsable = usable;
        NetworkChange.NetworkAddressChanged += OnNetworkEvent;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkEvent;
        networkDebounce.Tick += (_, _) => { networkDebounce.Stop(); OnNetworkChanged(); };
        trafficTimer.Tick += async (_, _) => await PollTrafficAsync();
        trafficTimer.Start();
        updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(silent: true);
        updateTimer.Start();
        _ = StartupAsync();
    }

    async Task StartupAsync()
    {
        await EnsureRoutingAsync();
        await CheckForUpdatesAsync(silent: true);
    }

    // ---------- Menú ----------

    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, Padding = new Padding(4, 2, 0, 2) };

        var file = new ToolStripMenuItem(L.T("&Archivo"));
        file.DropDownItems.Add(L.T("Exportar configuración…"), null, (_, _) => ExportSettings());
        file.DropDownItems.Add(L.T("Importar configuración…"), null, (_, _) => ImportSettings());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(L.T("Cerrar ventana (sigue funcionando)"), null, (_, _) => Close());
        file.DropDownItems.Add(L.T("Salir y desactivar el enrutado"), null, (_, _) => ExitApp());

        var tools = new ToolStripMenuItem(L.T("&Herramientas"));
        tools.DropDownItems.Add(new ToolStripMenuItem(L.T("Comprobar…"), null, async (_, _) => await OpenCheck()) { ShortcutKeys = Keys.F5 });
        tools.DropDownItems.Add(new ToolStripMenuItem(L.T("Configuración…"), null, async (_, _) => await OpenSettings()) { ShortcutKeys = Keys.Control | Keys.Oemcomma });

        var help = new ToolStripMenuItem(L.T("A&yuda"));
        help.DropDownItems.Add(L.T("Buscar actualizaciones"), null, async (_, _) => await CheckForUpdatesAsync(silent: false));
        help.DropDownItems.Add(L.T("Página del proyecto en GitHub"), null, (_, _) => OpenUrl(Paths.ProjectUrl));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(L.T("Acerca de AppRouter"), null, (_, _) => ShowAbout());

        menu.Items.AddRange([file, tools, help]);
        return menu;
    }

    void ShowAbout() =>
        MessageBox.Show(this,
            $"{Paths.AppName} {Paths.Version}\n\n" +
            L.T("Elige por qué conexión de red sale cada programa.") + "\n\n" +
            L.F("Incluye sing-box {0} (GPLv3).", Paths.SingBoxVersion) + "\n" +
            L.T("Software libre bajo la licencia GPLv3.") + "\n\n" + Paths.ProjectUrl,
            L.T("Acerca de AppRouter"), MessageBoxButtons.OK, MessageBoxIcon.Information);

    static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---------- Área de notificación ----------

    void SetupTray()
    {
        Icon = AppIcon.Load(new Size(32, 32));
        tray.Icon = AppIcon.Load();
        tray.Text = Paths.AppName;
        var menu = new ContextMenuStrip();
        var open = menu.Items.Add(L.T("Abrir AppRouter"), null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(L.T("Comprobar…"), null, async (_, _) => { ShowFromTray(); await OpenCheck(); });
        menu.Items.Add(L.T("Configuración…"), null, async (_, _) => { ShowFromTray(); await OpenSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("Salir y desactivar el enrutado"), null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        tray.BalloonTipClicked += (_, _) =>
        {
            if (pendingUpdateUrl is { } url) { pendingUpdateUrl = null; OpenUrl(url); }
            else ShowFromTray();
        };
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
                pendingUpdateUrl = null;
                tray.ShowBalloonTip(5000, Paths.AppName,
                    L.T("Sigue funcionando aquí. Para desactivarlo: clic derecho en este icono → Salir."), ToolTipIcon.Info);
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
        if (dirty && MessageBox.Show(this, L.T("Hay cambios sin aplicar que se perderán. ¿Salir igualmente?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        UseWaitCursor = true;
        var windowsDefault = AppSettings.WindowsDefaultInterface() ?? settings.DefaultInterface ?? L.T("la conexión por defecto");
        UseWaitCursor = false;
        if (MessageBox.Show(this,
                L.T("Al salir se desactiva el enrutado por programa.") + "\n\n" +
                L.F("Todas las conexiones volverán a salir por la conexión por defecto de Windows: {0}.", windowsDefault) + "\n\n" +
                L.T("Las conexiones abiertas ahora mismo (por ejemplo, descargas en curso) se cortarán un momento.") + "\n\n" +
                L.T("¿Salir?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        UseWaitCursor = true;
        try { SingBoxService.Stop(TimeSpan.FromSeconds(20)); }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            MessageBox.Show(this, L.T("No se pudo detener el servicio:") + "\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        exiting = true;
        tray.Visible = false;
        Application.Exit();
    }

    /// <summary>Vuelve a abrir AppRouter (para cambiar el idioma) sin desactivar el enrutado.</summary>
    void RestartApp()
    {
        if (dirty && MessageBox.Show(this, L.T("Hay cambios sin aplicar que se perderán. ¿Reiniciar igualmente?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        Process.Start(new ProcessStartInfo(Paths.CurrentExe, "--restart") { UseShellExecute = false });
        exiting = true;
        tray.Visible = false;
        Application.Exit();
    }

    /// <summary>Abrir AppRouter activa el enrutado (el servicio es de inicio manual).</summary>
    async Task EnsureRoutingAsync()
    {
        if (!File.Exists(Paths.ConfigPath)) return;
        // Rule-sets al día con la red actual antes de arrancar (por si algo se desconectó con AppRouter cerrado).
        try { SingBoxService.RefreshRuleSets(AppSettings.Load(), NetworkState.Usable()); } catch { /* se reintenta al cambiar la red */ }
        if (SingBoxService.Status() is not ServiceControllerStatus.Stopped) return;
        status.Text = L.T("Activando el enrutado…");
        try
        {
            await Task.Run(() => SingBoxService.Restart(TimeSpan.FromSeconds(20)));
            ShowServiceStatus();
        }
        catch (Exception ex)
        {
            status.Text = L.T("No se pudo activar el enrutado.");
            ShowBalloon(L.T("No se pudo activar el enrutado:") + " " + ex.Message, ToolTipIcon.Error);
        }
    }

    void ShowBalloon(string text, ToolTipIcon icon, string? url = null)
    {
        pendingUpdateUrl = url;
        tray.ShowBalloonTip(8000, Paths.AppName, text.Length > 250 ? text[..247] + "…" : text, icon);
    }

    // ---------- Cambios de red ----------

    void OnNetworkEvent(object? sender, EventArgs e)
    {
        // Llega desde otro hilo y en ráfagas: se agrupa y se procesa en el hilo de la ventana.
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(() => { networkDebounce.Stop(); networkDebounce.Start(); });
    }

    void OnNetworkChanged()
    {
        var now = NetworkState.Usable();
        var saved = AppSettings.Load();
        var watched = saved.Rules.Select(r => r.Interface).Append(saved.DefaultInterface ?? "")
            .Where(i => i != "" && i != Routing.Blocked)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var wentDown = watched.Where(i => lastUsable.Contains(i) && !now.Contains(i)).ToList();
        var cameBack = watched.Where(i => !lastUsable.Contains(i) && now.Contains(i)).ToList();
        lastUsable = now;

        // Con «usar la del resto del tráfico», los programas afectados cambian de salida sin cortar nada más.
        if (SingBoxService.Status() == ServiceControllerStatus.Running && File.Exists(Paths.ConfigPath))
        {
            try
            {
                if (SingBoxService.RefreshRuleSets(saved, now))
                {
                    saved.AppliedAt = DateTimeOffset.UtcNow;
                    saved.Save();
                    settings.AppliedAt = saved.AppliedAt;
                }
            }
            catch (Exception ex) { status.Text = L.T("Error al actualizar las reglas:") + " " + ex.Message; }
        }

        LoadInterfaces();
        Rebuild();

        if (!saved.NotifyNetworkChanges || (wentDown.Count == 0 && cameBack.Count == 0)) return;
        var lines = new List<string>();
        foreach (var i in wentDown)
        {
            var progs = ProgramsOn(saved, i);
            var line = L.F("{0} se ha desconectado.", i);
            if (progs != "")
                line += " " + (saved.OnDisconnect == DisconnectAction.Fallback
                    ? L.F("{0} sale por {1} mientras tanto.", progs, Routing.Display(Routing.EffectiveDefault(saved, now)))
                    : L.F("{0} se queda sin internet hasta que vuelva.", progs));
            lines.Add(line);
        }
        foreach (var i in cameBack)
        {
            var progs = ProgramsOn(saved, i);
            lines.Add(L.F("{0} vuelve a estar conectada.", i) +
                      (progs != "" && saved.OnDisconnect == DisconnectAction.Fallback ? " " + L.F("{0} vuelve a salir por ella.", progs) : ""));
        }
        ShowBalloon(string.Join("\n", lines), wentDown.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    static string ProgramsOn(AppSettings s, string iface) => string.Join(", ",
        s.Rules.Where(r => r.Interface.Equals(iface, StringComparison.OrdinalIgnoreCase)).Select(r => r.ProcessName));

    // ---------- Tráfico y actualizaciones ----------

    async Task PollTrafficAsync()
    {
        if (trafficBusy) return;
        trafficBusy = true;
        try { traffic.Update(await api.GetConnectionsAsync()); }
        catch { /* servicio parado o arrancando */ }
        finally { trafficBusy = false; }
    }

    async Task CheckForUpdatesAsync(bool silent)
    {
        if (silent && (!settings.CheckUpdates || settings.LastUpdateCheck is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(20)))
            return;
        try
        {
            var latest = await UpdateChecker.GetLatestAsync();
            var saved = AppSettings.Load();
            saved.LastUpdateCheck = settings.LastUpdateCheck = DateTimeOffset.UtcNow;
            saved.Save();

            if (latest is not null && UpdateChecker.IsNewer(latest))
            {
                var msg = L.F("Hay una versión nueva de AppRouter: {0} (tienes la {1}).", latest.Tag, Paths.Version);
                if (silent)
                    ShowBalloon(msg + " " + L.T("Haz clic aquí para descargarla."), ToolTipIcon.Info, latest.Url);
                else if (MessageBox.Show(this, msg + "\n\n" + L.T("¿Abrir la página de descarga?"), Text,
                             MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenUrl(latest.Url);
            }
            else if (!silent)
                MessageBox.Show(this, L.F("Tienes la última versión ({0}).", Paths.Version), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show(this, L.T("No se pudo consultar si hay actualizaciones:") + "\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------- Adaptadores ----------

    void LoadInterfaces()
    {
        interfaces = settings.GetSelectableInterfaces();
        usable = NetworkState.Usable();
        var def = settings.DefaultInterface ?? AppSettings.GuessEthernet(interfaces);
        FillCombo(defaultCombo, (defaultCombo.SelectedItem as ComboItem)?.Value ?? def, allowBlocked: false);
        FillCombo(newCombo, (newCombo.SelectedItem as ComboItem)?.Value ?? def, allowBlocked: true);
    }

    void FillCombo(ComboBox combo, string? selected, bool allowBlocked)
    {
        filling = true;
        combo.BeginUpdate();
        combo.Items.Clear();
        ComboItem Item(string iface) =>
            new(iface, usable.Contains(iface) ? iface : iface + "  " + L.T("(desconectada)"));
        foreach (var i in interfaces) combo.Items.Add(Item(i));
        // Si el adaptador guardado no está en la lista (oculto o ya no existe), se conserva para no perder la regla.
        if (!string.IsNullOrEmpty(selected) && selected != Routing.Blocked && !interfaces.Contains(selected, StringComparer.OrdinalIgnoreCase))
            combo.Items.Add(Item(selected));
        if (allowBlocked) combo.Items.Add(new ComboItem(Routing.Blocked, "⛔ " + Routing.Display(Routing.Blocked)));
        combo.SelectedItem = combo.Items.Cast<ComboItem>().FirstOrDefault(c => c.Value.Equals(selected, StringComparison.OrdinalIgnoreCase));
        combo.EndUpdate();
        filling = false;
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
            FillCombo(combo, rule.Interface, allowBlocked: true);
            var limit = MakeLimitCombo(rule);
            combo.SelectedIndexChanged += (_, _) =>
            {
                rule.Interface = (combo.SelectedItem as ComboItem)?.Value ?? "";
                limit.Enabled = rule.Interface != Routing.Blocked; // sin internet no hay nada que limitar
                dirty = true;
            };

            var remove = new Button { Text = "✕", Width = LogicalToDeviceUnits(28), Height = LogicalToDeviceUnits(27), FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.None };
            remove.FlatAppearance.BorderSize = 0;
            tips.SetToolTip(remove, L.T("Quitar"));
            remove.Click += (_, _) => { settings.Rules.Remove(rule); dirty = true; Rebuild(); };

            AddRow(name, combo, limit, remove);
        }

        var add = new LinkLabel
        {
            Text = L.T("+ Añadir programa"),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        add.LinkClicked += (_, _) => PickPrograms();
        tips.SetToolTip(newCombo, L.T("Interfaz para el programa que se añada"));
        AddRow(add, newCombo, null, null);

        rows.ResumeLayout();
    }

    void AddRow(Control c0, Control c1, Control? c2, Control? c3)
    {
        rows.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(36)));
        var r = rows.RowCount++;
        rows.Controls.Add(c0, 0, r);
        rows.Controls.Add(c1, 1, r);
        if (c2 is not null) rows.Controls.Add(c2, 2, r);
        if (c3 is not null) rows.Controls.Add(c3, 3, r);
    }

    // ---------- Límite de velocidad ----------

    static readonly double[] LimitPresets = [1, 2, 5, 10, 20, 50, 100];

    /// <summary>Desplegable editable: «Ilimitado», valores típicos o el número de Mbps que se escriba.</summary>
    ComboBox MakeLimitCombo(ProgramRule rule)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };
        combo.Items.Add(FormatLimit(0));
        foreach (var v in LimitPresets) combo.Items.Add(FormatLimit(v));
        combo.Text = FormatLimit(rule.LimitMbps);
        combo.Enabled = rule.Interface != Routing.Blocked;
        tips.SetToolTip(combo, L.T("Velocidad máxima de este programa, en megabits por segundo (bajada y subida). Puedes escribir cualquier valor."));

        void Commit()
        {
            if (ParseLimit(combo.Text) is { } v)
            {
                if (Math.Abs(v - rule.LimitMbps) > 0.0001) { rule.LimitMbps = v; dirty = true; }
                combo.Text = FormatLimit(v);
            }
            else combo.Text = FormatLimit(rule.LimitMbps); // no era un número: se deja como estaba
        }
        combo.SelectionChangeCommitted += (_, _) => BeginInvoke(Commit);
        combo.Validating += (_, _) => Commit();
        combo.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; } };
        return combo;
    }

    static string FormatLimit(double mbps) =>
        mbps <= 0 ? L.T("Ilimitado") : mbps.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " Mbps";

    /// <summary>"10", "10 Mbps", "2,5", "Ilimitado"… → Mbps (0 = ilimitado); null si no se entiende.</summary>
    static double? ParseLimit(string text)
    {
        var t = text.Trim();
        if (t == "" || t.Equals(L.T("Ilimitado"), StringComparison.OrdinalIgnoreCase) || t == "∞") return 0;
        t = t.Replace("mbps", "", StringComparison.OrdinalIgnoreCase).Replace("mb/s", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(',', '.');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
               && v >= 0 && v <= 100_000 ? Math.Round(v, 2) : null;
    }

    void PickPrograms()
    {
        using var picker = new ProcessPickerForm(settings.Rules.Select(r => r.Path));
        if (picker.ShowDialog(this) == DialogResult.OK) AddPrograms(picker.Selected);
    }

    void AddPrograms(IEnumerable<string> paths)
    {
        var target = (newCombo.SelectedItem as ComboItem)?.Value ?? (defaultCombo.SelectedItem as ComboItem)?.Value ?? "";
        foreach (var p in paths)
        {
            if (settings.Rules.Any(r => r.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
            settings.Rules.Add(new ProgramRule { Path = p, Interface = target });
            dirty = true;
        }
        Rebuild();
    }

    // ---------- Guardar y aplicar ----------

    async Task<bool> SaveAndApply()
    {
        if (defaultCombo.SelectedItem is not ComboItem { Value: var finalIface })
        {
            MessageBox.Show(this, L.T("Elige la conexión para el resto del tráfico."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        var all = AppSettings.GetInterfaces();
        var missing = settings.Rules
            .Where(r => r.Interface != Routing.Blocked && !all.Contains(r.Interface, StringComparer.OrdinalIgnoreCase))
            .Select(r => $"{r.ProcessName} → {r.Interface}").ToList();
        if (!all.Contains(finalIface, StringComparer.OrdinalIgnoreCase)) missing.Add($"({L.T("resto del tráfico")}) → {finalIface}");
        if (missing.Count > 0 && MessageBox.Show(this,
                L.T("Estas conexiones no existen ahora mismo en el equipo:") + "\n\n" + string.Join("\n", missing) + "\n\n" + L.T("¿Guardar igualmente?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

        var limited = settings.Rules.Where(r => r.LimitMbps > 0 && r.Interface != Routing.Blocked)
            .Select(r => r.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (limited > LimiterMap.SlotCount)
        {
            MessageBox.Show(this, L.F("Como máximo puede haber {0} programas con límite de velocidad (ahora hay {1}).", LimiterMap.SlotCount, limited),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        settings.DefaultInterface = finalIface;
        if (!await ApplyAsync(settings)) return false;
        dirty = false;
        return true;
    }

    /// <summary>
    /// Escribe y aplica la configuración de <paramref name="target"/> (y la guarda). Solo reinicia
    /// sing-box si cambia su estructura (adaptador nuevo, redes excluidas…), y avisa antes.
    /// </summary>
    async Task<bool> ApplyAsync(AppSettings target)
    {
        var plan = SingBoxService.Plan(target);
        var running = SingBoxService.Status() == ServiceControllerStatus.Running;

        if (plan.NeedsRestart && running && MessageBox.Show(this,
                (plan.NewInterfaces.Count > 0
                    ? L.T("Hay conexiones de red nuevas en el equipo:") + "\n\n" + string.Join("\n", plan.NewInterfaces) + "\n\n"
                    : "") +
                L.T("Para aplicar este cambio hay que reiniciar el servicio, y se cortarán un momento todas las conexiones abiertas (por ejemplo, las descargas en curso, que se reanudarán solas si el programa lo permite).") +
                "\n\n" + L.T("¿Continuar?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;

        UseWaitCursor = true;
        saveButton.Enabled = checkButton.Enabled = false;
        status.Text = L.T("Aplicando…");
        try
        {
            var error = await Task.Run(() => SingBoxService.Commit(plan));
            if (error is not null)
            {
                status.Text = L.T("La configuración no es válida; no se ha aplicado.");
                MessageBox.Show(this, L.T("sing-box ha rechazado la configuración:") + "\n\n" + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            target.AppliedAt = DateTimeOffset.UtcNow;
            target.Save();
            settings.AppliedAt = target.AppliedAt;
            settings.KnownInterfaces = [.. target.KnownInterfaces];
            lastUsable = NetworkState.Usable();

            var restarted = plan.NeedsRestart || !running;
            if (restarted)
            {
                status.Text = L.T("Reiniciando el servicio…");
                await Task.Run(() => SingBoxService.Restart(TimeSpan.FromSeconds(20)));
            }

            // El servicio puede estar "en ejecución" aunque sing-box no haya podido arrancar.
            var alive = await api.IsAliveAsync();
            for (var i = 0; i < 20 && !alive; i++)
            {
                await Task.Delay(500);
                alive = await api.IsAliveAsync();
            }
            if (!alive)
            {
                status.Text = L.T("sing-box no está funcionando.");
                MessageBox.Show(this, L.T("Se guardó la configuración, pero sing-box no está funcionando.") + "\n\n" +
                                      L.T("Últimas líneas del registro:") + "\n\n" + LogTail(12),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            status.Text = restarted
                ? L.F("✔ Aplicado a las {0:HH:mm:ss} (servicio reiniciado). Funcionando.", DateTime.Now)
                : L.F("✔ Aplicado a las {0:HH:mm:ss} sin cortar conexiones. Las ya abiertas siguen por donde iban.", DateTime.Now);
            tips.SetToolTip(status, status.Text);
            tray.Text = Paths.AppName + " · " + L.T("activo");
            return true;
        }
        catch (Exception ex)
        {
            status.Text = L.T("Error al aplicar.");
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            UseWaitCursor = false;
            saveButton.Enabled = checkButton.Enabled = true;
        }
    }

    // ---------- Comprobar, configuración, exportar/importar ----------

    async Task OpenCheck()
    {
        if (dirty)
        {
            if (MessageBox.Show(this, L.T("Hay cambios sin aplicar. Para comprobarlos hay que guardarlos primero.") + "\n\n" + L.T("¿Guardar y aplicar ahora?"),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (!await SaveAndApply()) return;
        }
        // Se compara con lo guardado (lo que sing-box está usando), no con la pantalla.
        using var f = new CheckForm(AppSettings.Load(), traffic);
        f.ShowDialog(this);
    }

    async Task OpenSettings(int tab = 0)
    {
        using var f = new SettingsForm(settings, ExportSettings, tab);
        var result = f.ShowDialog(this);
        if (f.ImportRequested) { ImportSettings(); return; }
        if (result != DialogResult.OK) return;
        var r = f.Result;

        if (f.AutostartChanged)
        {
            try { if (f.AutostartEnabled) Autostart.Enable(); else Autostart.Disable(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        var languageChanged = r.Language != settings.Language;
        var visibleChanged = !r.VisibleInterfaces.SequenceEqual(settings.VisibleInterfaces);
        var routingChanged = r.OnDisconnect != settings.OnDisconnect || !r.ExcludedSubnets.SequenceEqual(settings.ExcludedSubnets);
        var updatesEnabled = r.CheckUpdates && !settings.CheckUpdates;

        // Las preferencias se guardan sobre lo guardado (no sobre cambios de la lista aún sin aplicar).
        var saved = AppSettings.Load();
        saved.CopyPreferencesFrom(r);
        settings.CopyPreferencesFrom(r);
        if (routingChanged && File.Exists(Paths.ConfigPath))
        {
            if (!await ApplyAsync(saved))
            {
                saved.Save(); // se aplicarán en el próximo «Guardar y aplicar»
                status.Text = L.T("Ajustes guardados; se aplicarán en el próximo «Guardar y aplicar».");
            }
        }
        else saved.Save();

        if (visibleChanged || routingChanged) { LoadInterfaces(); Rebuild(); }
        if (updatesEnabled) await CheckForUpdatesAsync(silent: false);
        if (languageChanged && MessageBox.Show(this, L.T("El idioma cambiará al reiniciar AppRouter. El enrutado no se interrumpe.") + "\n\n" + L.T("¿Reiniciar ahora?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            RestartApp();
    }

    void ExportSettings() => ExportSettings(this);

    void ExportSettings(IWin32Window owner)
    {
        using var dlg = new SaveFileDialog
        {
            Title = L.T("Exportar configuración"),
            Filter = L.T("Configuración de AppRouter (*.json)|*.json"),
            FileName = $"AppRouter-{Environment.MachineName}.json",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, settings.Export());
            status.Text = L.F("Configuración exportada a {0}", Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void ImportSettings()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.T("Importar configuración"),
            Filter = L.T("Configuración de AppRouter (*.json)|*.json"),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, L.T("Se sustituirán la lista de programas y los ajustes por los del archivo. ¿Continuar?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            settings.Import(File.ReadAllText(dlg.FileName));
            defaultCombo.SelectedItem = null;
            LoadInterfaces();
            Rebuild();
            dirty = true;
            status.Text = L.T("Configuración importada. Revísala y pulsa «Guardar y aplicar».");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.T("No se pudo importar el archivo:") + "\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void ShowServiceStatus()
    {
        var st = SingBoxService.Status();
        status.Text = st switch
        {
            ServiceControllerStatus.Running => L.T("● Enrutado activo."),
            null => L.T("El servicio no está instalado."),
            var s => L.F("Enrutado desactivado (servicio: {0}). Pulsa «Guardar y aplicar» para activarlo.", s),
        };
        tray.Text = Paths.AppName + " · " + (st == ServiceControllerStatus.Running ? L.T("activo") : L.T("desactivado"));
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
        catch { return L.T("(sin registro)"); }
    }

    // ---------- Utilidades ----------

    static ComboBox MakeComboShell() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 5, 3, 3) };

    static Label HeaderLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        ForeColor = SystemColors.GrayText,
        Font = new Font(L.FontName, 8.5f, FontStyle.Bold),
    };

    static bool IsExe(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
}
