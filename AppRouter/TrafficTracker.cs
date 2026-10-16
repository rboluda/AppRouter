namespace AppRouter;

/// <summary>
/// Totales de datos enviados/recibidos por programa y destino desde que se abrió AppRouter.
/// Se alimenta consultando la API de sing-box cada pocos segundos: cuando una conexión desaparece,
/// sus últimos totales pasan al acumulado (lo de sus últimos segundos puede no contarse).
/// </summary>
public sealed class TrafficTracker
{
    public record Row(string Program, string Target, long Upload, long Download);

    readonly object gate = new();
    readonly Dictionary<string, (string Program, string Target, long Up, long Down)> open = [];
    readonly Dictionary<(string Program, string Target), (long Up, long Down)> closed = [];

    public DateTime Since { get; private set; } = DateTime.Now;

    public void Update(IEnumerable<Connection> conns)
    {
        lock (gate)
        {
            var seen = new HashSet<string>();
            foreach (var c in conns)
            {
                seen.Add(c.Id);
                open[c.Id] = (c.ProcessName, SingBoxConfig.TargetFromTag(c.OutboundTag) ?? "", c.Upload, c.Download);
            }
            foreach (var id in open.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                var (p, t, up, down) = open[id];
                closed.TryGetValue((p, t), out var acc);
                closed[(p, t)] = (acc.Up + up, acc.Down + down);
                open.Remove(id);
            }
        }
    }

    public List<Row> Snapshot()
    {
        lock (gate)
        {
            var totals = new Dictionary<(string, string), (long Up, long Down)>(closed);
            foreach (var (p, t, up, down) in open.Values)
            {
                totals.TryGetValue((p, t), out var acc);
                totals[(p, t)] = (acc.Up + up, acc.Down + down);
            }
            return totals.Select(kv => new Row(kv.Key.Item1, kv.Key.Item2, kv.Value.Up, kv.Value.Down))
                .OrderByDescending(r => r.Upload + r.Download).ToList();
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            closed.Clear();
            // Las conexiones abiertas empiezan a contar desde cero a partir de ahora.
            foreach (var id in open.Keys.ToList())
            {
                var (p, t, up, down) = open[id];
                closed[(p, t)] = closed.TryGetValue((p, t), out var acc) ? (acc.Up - up, acc.Down - down) : (-up, -down);
            }
            Since = DateTime.Now;
        }
    }
}

/// <summary>Consulta la última versión publicada en GitHub (solo si el usuario lo activa o lo pide).</summary>
public static class UpdateChecker
{
    public record Release(Version Version, string Tag, string Url);

    public static async Task<Release?> GetLatestAsync()
    {
        var repo = new Uri(Paths.ProjectUrl).AbsolutePath.Trim('/'); // "rboluda/approuter"
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AppRouter/{Paths.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var doc = System.Text.Json.JsonDocument.Parse(
            await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString() ?? Paths.ProjectUrl;
        return ParseVersion(tag) is { } v ? new Release(v, tag, url) : null;
    }

    public static bool IsNewer(Release r) => ParseVersion(Paths.Version) is { } current && r.Version > current;

    static Version? ParseVersion(string s) =>
        Version.TryParse(s.TrimStart('v', 'V').Split('-', '+')[0], out var v) ? v : null;
}
