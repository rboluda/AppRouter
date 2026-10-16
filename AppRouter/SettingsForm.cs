using System.Diagnostics;
using System.Net;

namespace AppRouter;

/// <summary>
/// Ventana Configuración. Trabaja sobre una copia de las preferencias; al aceptar,
/// <see cref="Result"/> tiene la copia modificada y <see cref="AutostartChanged"/> si cambió el arranque.
/// </summary>
public class SettingsForm : Form
{
    readonly AppSettings edit;
    readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    readonly CheckBox autostart = new() { AutoSize = true };
    readonly CheckBox notify = new() { AutoSize = true };
    readonly CheckBox updates = new() { AutoSize = true };
    readonly ListView nics = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        ShowItemToolTips = true,
    };
    readonly RadioButton onDownBlock = new() { AutoSize = true };
    readonly RadioButton onDownFallback = new() { AutoSize = true };
    readonly ListBox excluded = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly TextBox newSubnet = new() { Width = 220 };
    readonly bool autostartWas;

    public AppSettings Result => edit;
    public bool AutostartEnabled => autostart.Checked;
    public bool AutostartChanged => autostart.Checked != autostartWas;
    /// <summary>El usuario pulsó «Importar…»: se cierra la ventana y lo hace la ventana principal.</summary>
    public bool ImportRequested { get; private set; }

    public SettingsForm(AppSettings current, Action<IWin32Window> exportAction, int initialTab = 0)
    {
        edit = current.Clone();
        autostartWas = Autostart.IsEnabled();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.T("Configuración");
        Icon = AppIcon.Load(new Size(32, 32));
        Font = new Font(L.FontName, 9.5f);
        ClientSize = new Size(640, 520);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(GeneralTab(exportAction));
        tabs.TabPages.Add(InterfacesTab());
        tabs.TabPages.Add(RoutingTab());
        tabs.SelectedIndex = Math.Clamp(initialTab, 0, tabs.TabCount - 1);

        var ok = new Button { Text = L.T("Aceptar"), AutoSize = true, Padding = new Padding(10, 2, 10, 2) };
        var cancel = new Button { Text = L.T("Cancelar"), AutoSize = true, Padding = new Padding(6, 2, 6, 2), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => { if (Collect()) { DialogResult = DialogResult.OK; Close(); } };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        buttons.Controls.AddRange([cancel, ok]);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(tabs);
        Controls.Add(buttons);
        ResumeLayout(false);
        PerformLayout();
    }

    // ---------- General ----------

    TabPage GeneralTab(Action<IWin32Window> exportAction)
    {
        var page = new TabPage(L.T("General")) { Padding = new Padding(12) };
        var t = Table(page);

        foreach (var (code, name) in L.Languages) language.Items.Add(name);
        language.Items.Add(L.T("Automático (idioma de Windows)"));
        language.SelectedIndex = Array.FindIndex(L.Languages, l => l.Code == edit.Language) is var i and >= 0 ? i : L.Languages.Length;
        AddRow(t, Heading(L.T("Idioma")));
        AddRow(t, language);
        AddRow(t, Note(L.T("El cambio de idioma se aplica al reiniciar AppRouter (te lo ofrecerá al aceptar).")));

        AddRow(t, Heading(L.T("Funcionamiento")));
        autostart.Text = L.T("Abrir AppRouter al iniciar sesión en Windows");
        autostart.Checked = autostartWas;
        notify.Text = L.T("Avisarme cuando se desconecte o vuelva una conexión que uso");
        notify.Checked = edit.NotifyNetworkChanges;
        updates.Text = L.T("Buscar actualizaciones automáticamente (consulta GitHub una vez al día)");
        updates.Checked = edit.CheckUpdates;
        AddRow(t, autostart);
        AddRow(t, notify);
        AddRow(t, updates);

        AddRow(t, Heading(L.T("Copia de la configuración")));
        var exp = new Button { Text = L.T("Exportar…"), AutoSize = true };
        var imp = new Button { Text = L.T("Importar…"), AutoSize = true };
        exp.Click += (_, _) => exportAction(this);
        imp.Click += (_, _) => { ImportRequested = true; DialogResult = DialogResult.Cancel; Close(); };
        var row = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        row.Controls.AddRange([exp, imp]);
        AddRow(t, row);
        AddRow(t, Note(L.T("Guarda en un archivo la lista de programas y estos ajustes para llevarlos a otro equipo.")));

        var data = new LinkLabel { Text = L.T("Abrir la carpeta de configuración y registros"), AutoSize = true, Margin = new Padding(3, 14, 3, 3) };
        data.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.DataDir}\"") { UseShellExecute = true });
        AddRow(t, data);
        return page;
    }

    // ---------- Interfaces ----------

    TabPage InterfacesTab()
    {
        var page = new TabPage(L.T("Interfaces")) { Padding = new Padding(12) };
        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = L.T("Marca las conexiones que quieres ver en los desplegables. Si no marcas ninguna, se muestran todas."),
        };
        nics.Columns.Add(L.T("Conexión"), LogicalToDeviceUnits(220));
        nics.Columns.Add(L.T("Tipo"), LogicalToDeviceUnits(130));
        nics.Columns.Add("IP", LogicalToDeviceUnits(120));
        nics.Columns.Add(L.T("Adaptador"), LogicalToDeviceUnits(260));
        nics.Groups.Add(new ListViewGroup("up", L.T("Conectadas")));
        nics.Groups.Add(new ListViewGroup("down", L.T("Desconectadas")));
        FillNics();

        var all = new Button { Text = L.T("Marcar todas"), AutoSize = true };
        var none = new Button { Text = L.T("Ninguna"), AutoSize = true };
        var physical = new Button { Text = L.T("Solo físicas"), AutoSize = true };
        var refresh = new Button { Text = "↻", AutoSize = true };
        all.Click += (_, _) => SetChecks(_ => true);
        none.Click += (_, _) => SetChecks(_ => false);
        physical.Click += (_, _) => SetChecks(n => n.Kind is NicKind.Ethernet or NicKind.WiFi or NicKind.Mobile);
        refresh.Click += (_, _) => FillNics();
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 6, 0, 0) };
        bar.Controls.AddRange([all, none, physical, refresh]);

        page.Controls.Add(nics);
        page.Controls.Add(help);
        page.Controls.Add(bar);
        return page;
    }

    void FillNics()
    {
        var keep = nics.Items.Count > 0
            ? nics.CheckedItems.Cast<ListViewItem>().Select(i => ((NicInfo)i.Tag!).Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : edit.VisibleInterfaces.ToHashSet(StringComparer.OrdinalIgnoreCase);
        nics.BeginUpdate();
        nics.Items.Clear();
        foreach (var n in NetworkState.All())
        {
            nics.Items.Add(new ListViewItem([n.Name, NetworkState.KindLabel(n.Kind), n.Address, n.Description])
            {
                Tag = n,
                Checked = keep.Contains(n.Name),
                Group = nics.Groups[n.Connected ? 0 : 1],
                ForeColor = n.Connected ? SystemColors.WindowText : SystemColors.GrayText,
                ToolTipText = n.Description,
            });
        }
        nics.EndUpdate();
    }

    void SetChecks(Func<NicInfo, bool> pick)
    {
        foreach (ListViewItem i in nics.Items) i.Checked = pick((NicInfo)i.Tag!);
    }

    // ---------- Enrutado ----------

    TabPage RoutingTab()
    {
        var page = new TabPage(L.T("Enrutado")) { Padding = new Padding(12) };
        var t = Table(page);

        AddRow(t, Heading(L.T("Si se desconecta la conexión de un programa")));
        onDownBlock.Text = L.T("Dejarlo sin internet hasta que vuelva (nunca sale por otra conexión)");
        onDownFallback.Text = L.T("Usar mientras tanto la conexión del resto del tráfico");
        onDownBlock.Checked = edit.OnDisconnect == DisconnectAction.Block;
        onDownFallback.Checked = edit.OnDisconnect == DisconnectAction.Fallback;
        AddRow(t, onDownBlock);
        AddRow(t, onDownFallback);
        AddRow(t, Note(L.T("Con la segunda opción, si se desconecta también la del resto del tráfico, se usa la que Windows tenga disponible. El cambio es automático y no corta las demás conexiones.")));

        AddRow(t, Heading(L.T("Redes que no pasan por AppRouter")));
        AddRow(t, Note(L.T("El tráfico hacia estas redes (por ejemplo, la red local de la oficina) sigue siempre el camino normal de Windows. Cambiarlas reinicia el servicio y corta un momento las conexiones.")));
        foreach (var s in edit.ExcludedSubnets) excluded.Items.Add(s);
        var listHost = new Panel { Height = LogicalToDeviceUnits(110), Dock = DockStyle.Top, Margin = new Padding(3) };
        listHost.Controls.Add(excluded);
        AddRow(t, listHost);

        newSubnet.PlaceholderText = L.T("p. ej. 192.168.1.0/24");
        var add = new Button { Text = L.T("Añadir"), AutoSize = true };
        var remove = new Button { Text = L.T("Quitar"), AutoSize = true };
        add.Click += (_, _) => AddSubnet();
        newSubnet.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddSubnet(); e.SuppressKeyPress = true; } };
        remove.Click += (_, _) => { if (excluded.SelectedItem is { } s) excluded.Items.Remove(s); };
        var row = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        row.Controls.AddRange([newSubnet, add, remove]);
        AddRow(t, row);
        return page;
    }

    void AddSubnet()
    {
        var s = newSubnet.Text.Trim();
        if (s == "") return;
        if (!s.Contains('/') && IPAddress.TryParse(s, out var ip))
            s += ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? "/32" : "/128";
        if (!IPNetwork.TryParse(s, out var net))
        {
            MessageBox.Show(this, L.F("«{0}» no es una red válida. Usa el formato dirección/prefijo, por ejemplo 192.168.1.0/24.", s),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var normalized = net.ToString();
        if (!excluded.Items.Contains(normalized)) excluded.Items.Add(normalized);
        newSubnet.Clear();
    }

    // ---------- Aceptar ----------

    bool Collect()
    {
        edit.Language = language.SelectedIndex < L.Languages.Length ? L.Languages[language.SelectedIndex].Code : null;
        edit.NotifyNetworkChanges = notify.Checked;
        edit.CheckUpdates = updates.Checked;
        edit.VisibleInterfaces = nics.CheckedItems.Cast<ListViewItem>().Select(i => ((NicInfo)i.Tag!).Name).ToList();
        edit.OnDisconnect = onDownFallback.Checked ? DisconnectAction.Fallback : DisconnectAction.Block;
        if (newSubnet.Text.Trim() != "")
        {
            AddSubnet(); // lo escrito y no añadido todavía
            if (newSubnet.Text.Trim() != "") return false; // no era válido: se queda abierta
        }
        edit.ExcludedSubnets = excluded.Items.Cast<string>().ToList();
        return true;
    }

    // ---------- Maquetación ----------

    TableLayoutPanel Table(TabPage page)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(t);
        return t;
    }

    static void AddRow(TableLayoutPanel t, Control c)
    {
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, 0, t.RowCount++);
    }

    Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(Font, FontStyle.Bold),
        Margin = new Padding(3, 12, 3, 4),
    };

    Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(LogicalToDeviceUnits(580), 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 6),
    };
}
