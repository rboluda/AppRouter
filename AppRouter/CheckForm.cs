using System.Diagnostics;
using System.Net.NetworkInformation;

namespace AppRouter;

/// <summary>
/// Ventana "Comprobar": muestra en directo por qué adaptador sale cada conexión de los
/// programas de la lista, según sing-box y según Windows, y el tráfico de cada adaptador.
/// </summary>
public class CheckForm : Form
{
    static readonly Color OkBack = Color.FromArgb(222, 245, 225);
    static readonly Color BadBack = Color.FromArgb(253, 222, 220);
    static readonly Color IdleBack = Color.FromArgb(245, 245, 245);
    static readonly Color StaleBack = Color.FromArgb(255, 243, 205);

    readonly AppSettings settings;
    readonly SingBoxApi api;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly DataGridView summary = MakeGrid();
    readonly DataGridView details = MakeGrid();
    readonly CheckBox onlyListed = new() { Text = "Solo programas de la lista", Checked = true, AutoSize = true };
    readonly Label banner = new() { Dock = DockStyle.Top, Height = 0, Padding = new Padding(10, 6, 10, 6), BackColor = BadBack, AutoSize = false };
    readonly Label traffic = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(10, 0, 10, 0), TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
    readonly Dictionary<string, (long rx, long tx)> lastBytes = [];
    readonly DnsCache dns = new();
    readonly Button moveButton = new() { AutoSize = true, Visible = false, Margin = new Padding(16, 0, 0, 0) };
    List<CheckedConnection> lastChecked = [];
    bool busy;

    public CheckForm(AppSettings settings)
    {
        this.settings = settings;
        api = new SingBoxApi(settings.ApiPort, settings.ApiSecret);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Comprobar por dónde sale cada programa";
        Icon = AppIcon.Load(new Size(32, 32));
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(860, 600);
        MinimumSize = new Size(600, 400);
        StartPosition = FormStartPosition.CenterParent;

        var help = new Label
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(10, 8, 10, 0),
            Text = "Abre los programas de la lista y úsalos (por ejemplo, empieza una descarga). " +
                   "Cada conexión se comprueba dos veces: lo que dice sing-box y la IP que Windows usa de verdad. " +
                   "Verde = sale por donde debe. Rojo = no. Ámbar = abierta antes del último cambio: sigue por la conexión anterior hasta que se cierre.",
        };

        summary.Columns.Add("prog", "Programa");
        summary.Columns.Add("expected", "Debería");
        summary.Columns.Add("count", "Conex.");
        summary.Columns.Add("actual", "Sale por");
        summary.Columns.Add("result", "Resultado");
        summary.Columns["prog"]!.FillWeight = 140;
        summary.Columns["count"]!.FillWeight = 55;
        summary.Columns["result"]!.FillWeight = 210;

        details.Columns.Add("prog", "Programa");
        details.Columns.Add("dest", "Destino");
        details.Columns.Add("net", "Tipo");
        details.Columns.Add("expected", "Debería");
        details.Columns.Add("sb", "sing-box dice");
        details.Columns.Add("win", "Windows dice");
        details.Columns.Add("down", "Recibido");
        details.Columns["dest"]!.FillWeight = 180;
        details.Columns["net"]!.FillWeight = 45;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
        var summaryBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0) };
        summaryBar.Controls.Add(new Label { Text = "Resumen por programa", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 7, 0, 0) });
        summaryBar.Controls.Add(moveButton);
        moveButton.Click += async (_, _) => await MoveStaleAsync();
        split.Panel1.Controls.Add(summary);
        split.Panel1.Controls.Add(summaryBar);
        var detailsBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(6, 4, 6, 0) };
        detailsBar.Controls.Add(new Label { Text = "Conexiones activas", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 20, 0) });
        detailsBar.Controls.Add(onlyListed);
        split.Panel2.Controls.Add(details);
        split.Panel2.Controls.Add(detailsBar);

        var logLink = new LinkLabel { Text = "Abrir registro de sing-box", Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(10, 0, 10, 0) };
        logLink.LinkClicked += (_, _) => OpenLog();

        Controls.Add(split);
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

    async Task RefreshAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            UpdateTraffic();

            List<Connection> conns;
            try { conns = await api.GetConnectionsAsync(); }
            catch
            {
                var st = SingBoxService.Status();
                ShowBanner(st is null
                    ? "El servicio no está instalado."
                    : $"No se puede hablar con sing-box (servicio: {st}). Puede que no haya arrancado: abre el registro para ver el motivo.");
                summary.Rows.Clear();
                details.Rows.Clear();
                return;
            }
            ShowBanner(null);

            var checkedConns = await Task.Run(() =>
            {
                var sockets = WindowsSockets.TcpConnections(WindowsSockets.SingBoxPids());
                return Verifier.Evaluate(conns, settings, sockets, WindowsSockets.AddressToAdapter(), dns.Resolve);
            });
            if (IsDisposed) return;
            lastChecked = checkedConns;
            var staleCount = checkedConns.Count(c => c.Verdict == Verdict.Stale);
            moveButton.Visible = staleCount > 0;
            moveButton.Text = $"Mover ya las conexiones antiguas ({staleCount})";
            FillSummary(checkedConns);
            FillDetails(checkedConns);
        }
        finally { busy = false; }
    }

    void FillSummary(List<CheckedConnection> all)
    {
        var rows = new List<object[]>();
        var colors = new List<Color>();

        var programs = settings.Rules.Select(r => (Name: r.ProcessName, Expected: r.Interface))
            .Append((Name: "Resto de programas", Expected: settings.DefaultInterface ?? "?"));
        foreach (var (name, expected) in programs)
        {
            var isRest = name == "Resto de programas";
            var mine = all.Where(c => isRest
                ? !settings.Rules.Any(r => r.ProcessName.Equals(c.Conn.ProcessName, StringComparison.OrdinalIgnoreCase))
                : c.Conn.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();

            var actual = string.Join(", ", mine.Select(c => c.ViaWindows != "—" ? c.ViaWindows : c.ViaSingBox).Distinct());
            var wrong = mine.Count(c => c.Verdict == Verdict.Wrong);
            var stale = mine.Count(c => c.Verdict == Verdict.Stale);
            string result;
            Color color;
            if (mine.Count == 0) { result = "Sin conexiones (úsalo para comprobar)"; color = IdleBack; }
            else if (wrong > 0) { result = $"✖ {wrong} de {mine.Count} salen por donde no deben"; color = BadBack; }
            else if (stale > 0)
            {
                var oldVia = string.Join(", ", mine.Where(c => c.Verdict == Verdict.Stale).Select(c => c.ViaSingBox).Distinct());
                result = $"✔ Correcto ({stale} antiguas aún por {oldVia})";
                color = StaleBack;
            }
            else { result = "✔ Correcto"; color = OkBack; }

            rows.Add([name, expected, mine.Count, actual, result]);
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
            .Select(g => $"• {g.Key.ProcessName}: {g.Count()} por {g.Key.ViaSingBox} → pasarán a {g.Key.Expected}"));
        if (MessageBox.Show(this,
                "Se cerrarán estas conexiones antiguas para que el programa las vuelva a abrir por su conexión actual:\n\n" + detail +
                "\n\nSi alguna es una descarga, se interrumpirá un momento (normalmente el programa la reanuda solo). " +
                "Las demás conexiones no se tocan.\n\n¿Continuar?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        moveButton.Enabled = false;
        try
        {
            var results = await Task.WhenAll(stale.Select(c => api.CloseAsync(c.Conn.Id)));
            var failed = results.Count(ok => !ok);
            if (failed > 0)
                MessageBox.Show(this, $"No se pudieron cerrar {failed} de {stale.Count} conexiones (puede que ya se hubieran cerrado).",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { moveButton.Enabled = true; }
        await RefreshAsync();
    }

    void FillDetails(List<CheckedConnection> all)
    {
        var shown = all
            .Where(c => !onlyListed.Checked || settings.Rules.Any(r => r.ProcessName.Equals(c.Conn.ProcessName, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.Conn.ProcessName).ThenByDescending(c => c.Conn.Download)
            .ToList();
        Replace(details,
            shown.Select(c => new object[]
            {
                c.Conn.ProcessName, c.Conn.Destination, c.Conn.Network.ToUpperInvariant(),
                c.Expected, c.ViaSingBox, c.ViaWindows, FormatBytes(c.Conn.Download),
            }).ToList(),
            shown.Select(c => c.Verdict switch { Verdict.Ok => OkBack, Verdict.Wrong => BadBack, Verdict.Stale => StaleBack, _ => IdleBack }).ToList());
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
        traffic.Text = parts.Count > 0 ? "Tráfico por adaptador ·  " + string.Join("   ·   ", parts) : "Midiendo tráfico…";
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
            MessageBox.Show("Todavía no hay registro.", Paths.AppName);
    }

    static string FormatBytes(long b) => b switch
    {
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
