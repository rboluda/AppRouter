using System.Diagnostics;
using System.Net.NetworkInformation;

namespace AppRouter;

/// <summary>
/// Ventana "Comprobar": muestra en directo por qué adaptador sale cada conexión de los programas
/// de la lista (según sing-box y según Windows), el tráfico por adaptador y el acumulado por programa.
/// </summary>
public class CheckForm : Form
{
    static readonly Color OkBack = Color.FromArgb(222, 245, 225);
    static readonly Color BadBack = Color.FromArgb(253, 222, 220);
    static readonly Color IdleBack = Color.FromArgb(245, 245, 245);
    static readonly Color StaleBack = Color.FromArgb(255, 243, 205);

    readonly AppSettings settings;
    readonly TrafficTracker tracker;
    readonly SingBoxApi api;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly DataGridView summary = MakeGrid();
    readonly DataGridView details = MakeGrid();
    readonly DataGridView totals = MakeGrid();
    readonly CheckBox onlyListed = new() { Checked = true, AutoSize = true };
    readonly Label banner = new() { Dock = DockStyle.Top, Height = 0, Padding = new Padding(10, 6, 10, 6), BackColor = BadBack, AutoSize = false };
    readonly Label traffic = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(10, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
    readonly Label totalsSince = new() { AutoSize = true, Margin = new Padding(3, 7, 12, 0), ForeColor = SystemColors.GrayText };
    readonly Dictionary<string, (long rx, long tx)> lastBytes = [];
    readonly DnsCache dns = new();
    readonly Button moveButton = new() { AutoSize = true, Visible = false, Margin = new Padding(16, 0, 0, 0) };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
    List<CheckedConnection> lastChecked = [];
    bool busy;

    public CheckForm(AppSettings settings, TrafficTracker tracker)
    {
        this.settings = settings;
        this.tracker = tracker;
        api = new SingBoxApi(settings.ApiPort, settings.ApiSecret);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.T("Comprobar por dónde sale cada programa");
        Icon = AppIcon.Load(new Size(32, 32));
        Font = new Font(L.FontName, 9.5f);
        ClientSize = new Size(880, 620);
        MinimumSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;

        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(10, 8, 10, 0),
            Text = L.T("Abre los programas de la lista y úsalos (por ejemplo, empieza una descarga). Cada conexión se comprueba dos veces: lo que dice sing-box y la IP que Windows usa de verdad.") + " " +
                   L.T("Verde = sale por donde debe. Rojo = no. Ámbar = abierta antes del último cambio: sigue por la conexión anterior hasta que se cierre."),
        };

        summary.Columns.Add("prog", L.T("Programa"));
        summary.Columns.Add("expected", L.T("Debería"));
        summary.Columns.Add("count", L.T("Conex."));
        summary.Columns.Add("actual", L.T("Sale por"));
        summary.Columns.Add("result", L.T("Resultado"));
        summary.Columns["prog"]!.FillWeight = 140;
        summary.Columns["count"]!.FillWeight = 55;
        summary.Columns["result"]!.FillWeight = 210;

        details.Columns.Add("prog", L.T("Programa"));
        details.Columns.Add("dest", L.T("Destino"));
        details.Columns.Add("net", L.T("Tipo"));
        details.Columns.Add("expected", L.T("Debería"));
        details.Columns.Add("sb", L.T("sing-box dice"));
        details.Columns.Add("win", L.T("Windows dice"));
        details.Columns.Add("down", L.T("Recibido"));
        details.Columns["dest"]!.FillWeight = 180;
        details.Columns["net"]!.FillWeight = 45;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        var summaryBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0) };
        summaryBar.Controls.Add(new Label { Text = L.T("Resumen por programa"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 7, 0, 0) });
        summaryBar.Controls.Add(moveButton);
        moveButton.Click += async (_, _) => await MoveStaleAsync();
        split.Panel1.Controls.Add(summary);
        split.Panel1.Controls.Add(summaryBar);
        var detailsBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 4, 6, 0) };
        detailsBar.Controls.Add(new Label { Text = L.T("Conexiones activas"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 20, 0) });
        onlyListed.Text = L.T("Solo programas de la lista");
        detailsBar.Controls.Add(onlyListed);
        split.Panel2.Controls.Add(details);
        split.Panel2.Controls.Add(detailsBar);

        var checkPage = new TabPage(L.T("Comprobación"));
        checkPage.Controls.Add(split);
        tabs.TabPages.Add(checkPage);
        tabs.TabPages.Add(TotalsPage());

        var logLink = new LinkLabel { Text = L.T("Abrir registro de sing-box"), Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(10, 0, 10, 0) };
        logLink.LinkClicked += (_, _) => OpenLog();

        Controls.Add(tabs);
        Controls.Add(banner);
        Controls.Add(help);
        Controls.Add(logLink);
        Controls.Add(traffic);

        onlyListed.CheckedChanged += async (_, _) => await RefreshAsync();
        timer.Tick += async (_, _) => await RefreshAsync();
        Shown += async (_, _) => { await RefreshAsync(); timer.Start(); };
        FormClosed += (_, _) => { timer.Dispose(); api.Dispose(); };
        ResumeLayout(false);
        PerformLayout();
    }

    TabPage TotalsPage()
    {
        var page = new TabPage(L.T("Tráfico por programa"));
        totals.Columns.Add("prog", L.T("Programa"));
        totals.Columns.Add("via", L.T("Sale por"));
        totals.Columns.Add("up", L.T("Enviado"));
        totals.Columns.Add("down", L.T("Recibido"));
        totals.Columns.Add("sum", L.T("Total"));
        totals.Columns["prog"]!.FillWeight = 160;
        foreach (var c in new[] { "up", "down", "sum" })
            totals.Columns[c]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        var reset = new Button { Text = L.T("Poner a cero"), AutoSize = true };
        reset.Click += (_, _) => { tracker.Reset(); FillTotals(); };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0) };
        bar.Controls.AddRange([totalsSince, reset]);
        page.Controls.Add(totals);
        page.Controls.Add(bar);
        return page;
    }

    async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            UpdateTraffic();
            FillTotals();

            List<Connection> conns;
            try { conns = await api.GetConnectionsAsync(); }
            catch
            {
                var st = SingBoxService.Status();
                ShowBanner(st is null
                    ? L.T("El servicio no está instalado.")
                    : L.F("No se puede hablar con sing-box (servicio: {0}). Puede que no haya arrancado: abre el registro para ver el motivo.", st));
                summary.Rows.Clear();
                details.Rows.Clear();
                return;
            }
            ShowBanner(null);

            var (checkedConns, usable) = await Task.Run(() =>
            {
                var up = NetworkState.Usable();
                var sockets = WindowsSockets.TcpConnections(WindowsSockets.SingBoxPids());
                return (Verifier.Evaluate(conns, settings, up, sockets, WindowsSockets.AddressToAdapter(), dns.Resolve), up);
            });
            if (IsDisposed) return;
            lastChecked = checkedConns;
            var staleCount = checkedConns.Count(c => c.Verdict == Verdict.Stale);
            moveButton.Visible = staleCount > 0;
            moveButton.Text = L.F("Mover ya las conexiones antiguas ({0})", staleCount);
            FillSummary(checkedConns, usable);
            FillDetails(checkedConns);
        }
        finally { busy = false; }
    }

    void FillSummary(List<CheckedConnection> all, HashSet<string> usable)
    {
        var rows = new List<object[]>();
        var colors = new List<Color>();

        var programs = settings.Rules
            .Select(r => (Name: r.ProcessName, Rule: (ProgramRule?)r))
            .Append((Name: L.T("Resto de programas"), Rule: null));
        foreach (var (name, rule) in programs)
        {
            var expected = Routing.Effective(settings, rule, usable);
            var mine = all.Where(c => rule is null
                ? Routing.RuleFor(settings, c.Conn.ProcessName) is null
                : c.Conn.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

            var actual = string.Join(", ", mine.Select(c => c.ViaWindows != "—" ? c.ViaWindows : Routing.Display(c.ViaSingBox)).Distinct());
            var wrong = mine.Count(c => c.Verdict == Verdict.Wrong);
            var stale = mine.Count(c => c.Verdict == Verdict.Stale);
            string result;
            Color color;
            if (mine.Count == 0 && expected == Routing.Blocked) { result = L.T("✔ Sin internet, como se pidió"); color = OkBack; }
            else if (mine.Count == 0) { result = L.T("Sin conexiones (úsalo para comprobar)"); color = IdleBack; }
            else if (wrong > 0) { result = L.F("✖ {0} de {1} salen por donde no deben", wrong, mine.Count); color = BadBack; }
            else if (stale > 0)
            {
                var oldVia = string.Join(", ", mine.Where(c => c.Verdict == Verdict.Stale).Select(c => Routing.Display(c.ViaSingBox)).Distinct());
                result = L.F("✔ Correcto ({0} antiguas aún por {1})", stale, oldVia);
                color = StaleBack;
            }
            else { result = L.T("✔ Correcto"); color = OkBack; }

            // Si su conexión está caída y va por otra (o sin internet), se indica junto a la esperada.
            var expectedText = Routing.Display(expected);
            if (rule is not null && rule.Interface != Routing.Blocked && !usable.Contains(rule.Interface))
                expectedText = L.F("{0} (desconectada)", rule.Interface) +
                               (expected != rule.Interface ? " → " + Routing.Display(expected) : "");
            rows.Add([name, expectedText, mine.Count, actual, result]);
            colors.Add(color);
        }
        Replace(summary, rows, colors);
    }

    /// <summary>
    /// Cierra las conexiones abiertas antes del último cambio que van por otra salida. Cada programa
    /// vuelve a conectar y su nueva conexión ya sale por la interfaz actual. No toca nada más.
    /// </summary>
    async Task MoveStaleAsync()
    {
        var stale = lastChecked.Where(c => c.Verdict == Verdict.Stale).ToList();
        if (stale.Count == 0) return;

        var detail = string.Join("\n", stale
            .GroupBy(c => (c.Conn.ProcessName, c.ViaSingBox, c.Expected))
            .Select(g => L.F("• {0}: {1} por {2} → pasarán a {3}", g.Key.ProcessName, g.Count(), Routing.Display(g.Key.ViaSingBox), Routing.Display(g.Key.Expected))));
        if (MessageBox.Show(this,
                L.T("Se cerrarán estas conexiones antiguas para que el programa las vuelva a abrir por su conexión actual:") + "\n\n" + detail + "\n\n" +
                L.T("Si alguna es una descarga, se interrumpirá un momento (normalmente el programa la reanuda solo). Las demás conexiones no se tocan.") + "\n\n" +
                L.T("¿Continuar?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        moveButton.Enabled = false;
        try
        {
            var results = await Task.WhenAll(stale.Select(c => api.CloseAsync(c.Conn.Id)));
            var failed = results.Count(ok => !ok);
            if (failed > 0)
                MessageBox.Show(this, L.F("No se pudieron cerrar {0} de {1} conexiones (puede que ya se hubieran cerrado).", failed, stale.Count),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { moveButton.Enabled = true; }
        await RefreshAsync();
    }

    void FillDetails(List<CheckedConnection> all)
    {
        var shown = all
            .Where(c => !onlyListed.Checked || Routing.RuleFor(settings, c.Conn.ProcessName) is not null)
            .OrderBy(c => c.Conn.ProcessName).ThenByDescending(c => c.Conn.Download)
            .ToList();
        Replace(details,
            shown.Select(c => new object[]
            {
                c.Conn.ProcessName, c.Conn.Destination, c.Conn.Network.ToUpperInvariant(),
                Routing.Display(c.Expected), Routing.Display(c.ViaSingBox), c.ViaWindows, FormatBytes(c.Conn.Download),
            }).ToList(),
            shown.Select(c => c.Verdict switch { Verdict.Ok => OkBack, Verdict.Wrong => BadBack, Verdict.Stale => StaleBack, _ => IdleBack }).ToList());
    }

    void FillTotals()
    {
        totalsSince.Text = L.F("Desde las {0:HH:mm} (mientras AppRouter está abierto)", tracker.Since);
        var rows = tracker.Snapshot();
        Replace(totals,
            rows.Select(r => new object[]
            {
                r.Program, Routing.Display(r.Target), FormatBytes(r.Upload), FormatBytes(r.Download), FormatBytes(r.Upload + r.Download),
            }).ToList(),
            rows.Select(_ => SystemColors.Window).ToList());
    }

    /// <summary>Reemplaza filas conservando el desplazamiento para que no salte cada segundo.</summary>
    static void Replace(DataGridView grid, List<object[]> rows, List<Color> colors)
    {
        var top = grid.FirstDisplayedScrollingRowIndex;
        grid.SuspendLayout();
        grid.Rows.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var idx = grid.Rows.Add(rows[i]);
            grid.Rows[idx].DefaultCellStyle.BackColor = colors[i];
            grid.Rows[idx].DefaultCellStyle.SelectionBackColor = ControlPaint.Dark(colors[i], 0.05f);
            grid.Rows[idx].DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        }
        if (top >= 0 && top < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = top;
        grid.ClearSelection();
        grid.ResumeLayout();
    }

    void UpdateTraffic()
    {
        var parts = new List<string>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (n.Name.Equals(SingBoxConfig.TunInterfaceName, StringComparison.OrdinalIgnoreCase)) continue;
            var s = n.GetIPStatistics();
            if (lastBytes.TryGetValue(n.Name, out var prev))
                parts.Add($"{n.Name}: ↓ {FormatBytes(s.BytesReceived - prev.rx)}/s  ↑ {FormatBytes(s.BytesSent - prev.tx)}/s");
            lastBytes[n.Name] = (s.BytesReceived, s.BytesSent);
        }
        traffic.Text = parts.Count > 0 ? L.T("Tráfico por adaptador") + " ·  " + string.Join("   ·   ", parts) : L.T("Midiendo tráfico…");
    }

    void ShowBanner(string? text)
    {
        banner.Text = text ?? "";
        banner.Height = text is null ? 0 : 34;
    }

    static void OpenLog()
    {
        if (File.Exists(Paths.LogPath))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Paths.LogPath}\"") { UseShellExecute = true });
        else
            MessageBox.Show(L.T("Todavía no hay registro."), Paths.AppName);
    }

    static string FormatBytes(long b) => b switch
    {
        < 0 => "0 B",
        < 1024 => $"{b} B",
        < 1024 * 1024 => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024:0.#} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.##} GB",
    };

    static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
    };
}
