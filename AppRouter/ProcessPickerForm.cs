using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AppRouter;

/// <summary>
/// Elegir programas de entre los que se están ejecutando (con su icono), o buscar un .exe.
/// </summary>
public class ProcessPickerForm : Form
{
    readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        ShowItemToolTips = true,
    };
    readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "" };
    readonly CheckBox showSystem = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly ImageList icons = new() { ColorDepth = ColorDepth.Depth32Bit };
    readonly HashSet<string> alreadyAdded;
    List<(string Path, string Name, bool HasWindow)> all = [];

    /// <summary>Rutas de los programas elegidos.</summary>
    public List<string> Selected { get; } = [];

    public ProcessPickerForm(IEnumerable<string> alreadyAddedPaths)
    {
        alreadyAdded = alreadyAddedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.T("Añadir programas");
        Icon = AppIcon.Load(new Size(32, 32));
        Font = new Font(L.FontName, 9.5f);
        ClientSize = new Size(620, 520);
        MinimumSize = new Size(460, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;

        icons.ImageSize = new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
        list.SmallImageList = icons;
        list.Columns.Add(L.T("Programa"), LogicalToDeviceUnits(220));
        list.Columns.Add(L.T("Ubicación"), LogicalToDeviceUnits(420));
        list.Groups.Add(new ListViewGroup("win", L.T("Con ventana abierta")));
        list.Groups.Add(new ListViewGroup("bg", L.T("En segundo plano")));

        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(10, 8, 10, 0),
            Text = L.T("Marca los programas que quieres añadir. Si no está abierto, ábrelo antes o búscalo con «Examinar…»."),
        };

        search.PlaceholderText = L.T("Buscar…");
        search.TextChanged += (_, _) => Fill();
        showSystem.Text = L.T("Mostrar procesos de Windows");
        showSystem.CheckedChanged += (_, _) => { Load(); Fill(); };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2, Padding = new Padding(8, 4, 8, 4) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(search, 0, 0);
        top.Controls.Add(showSystem, 1, 0);

        var add = new Button { Text = L.T("Añadir"), AutoSize = true, Padding = new Padding(10, 2, 10, 2), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = L.T("Cancelar"), AutoSize = true, Padding = new Padding(6, 2, 6, 2), DialogResult = DialogResult.Cancel };
        var browse = new Button { Text = L.T("Examinar…"), AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        browse.Click += (_, _) => Browse();
        var refresh = new Button { Text = "↻", AutoSize = true };
        refresh.Click += (_, _) => { Load(); Fill(); };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        buttons.Controls.AddRange([cancel, add, browse, refresh]);
        AcceptButton = add;
        CancelButton = cancel;

        Controls.Add(list);
        Controls.Add(top);
        Controls.Add(help);
        Controls.Add(buttons);
        ResumeLayout(false);
        PerformLayout();

        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK)
                Selected.AddRange(list.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!));
        };
        list.ItemChecked += (_, _) => add.Enabled = list.CheckedItems.Count > 0;
        Shown += (_, _) => { Load(); Fill(); add.Enabled = false; search.Focus(); };
    }

    void Browse()
    {
        using var dlg = new OpenFileDialog
        {
            Title = L.T("Elegir programa"),
            Filter = L.T("Programas (*.exe)|*.exe"),
            Multiselect = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Selected.AddRange(dlg.FileNames);
        DialogResult = DialogResult.OK; // Selected ya tiene lo elegido; no se mezcla con la lista
        list.Items.Clear();
        Close();
    }

    new void Load()
    {
        UseWaitCursor = true;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var me = Environment.ProcessId;
        var found = new Dictionary<string, (string Path, string Name, bool HasWindow)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == me || p.Id <= 4) continue;
                var path = ImagePath(p.Id);
                if (path is null || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(Paths.InstallDir, StringComparison.OrdinalIgnoreCase)) continue; // AppRouter y su sing-box
                if (!showSystem.Checked && path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
                bool hasWindow;
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { hasWindow = false; }
                var name = System.IO.Path.GetFileName(path);
                found[path] = found.TryGetValue(path, out var prev) ? prev with { HasWindow = prev.HasWindow || hasWindow } : (path, name, hasWindow);
            }
        }
        all = found.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        UseWaitCursor = false;
    }

    void Fill()
    {
        var checkedPaths = list.CheckedItems.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var q = search.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var (path, name, hasWindow) in all)
        {
            if (q != "" && !name.Contains(q, StringComparison.OrdinalIgnoreCase) && !path.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!icons.Images.ContainsKey(path))
            {
                try { using var ic = Icon.ExtractAssociatedIcon(path); if (ic is not null) icons.Images.Add(path, ic.ToBitmap()); }
                catch { /* sin icono */ }
            }
            var added = alreadyAdded.Contains(path);
            var item = new ListViewItem([added ? name + "  " + L.T("(ya en la lista)") : name, System.IO.Path.GetDirectoryName(path) ?? ""])
            {
                Tag = path,
                ImageKey = path,
                Group = list.Groups[hasWindow ? 0 : 1],
                Checked = checkedPaths.Contains(path),
                ToolTipText = path,
                ForeColor = added ? SystemColors.GrayText : SystemColors.WindowText,
            };
            list.Items.Add(item);
        }
        list.EndUpdate();
    }

    // Ruta del ejecutable con permisos mínimos (funciona también con procesos de otros usuarios o de 32 bits).
    static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32")]
    static extern bool CloseHandle(IntPtr h);
}
