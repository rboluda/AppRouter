using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace AppRouter;

/// <summary>
/// Actualizaciones: consulta la última Release de GitHub y, si el usuario lo permite, descarga el
/// instalador, comprueba su SHA-256 con el SHA256SUMS.txt publicado junto a él y su versión, y lo
/// ejecuta en modo «--update» (instalación silenciosa que vuelve a abrir AppRouter).
/// </summary>
public static class UpdateChecker
{
    public record Release(Version Version, string Tag, string Url, string? ExeName, string? ExeUrl, string? SumsUrl);

    /// <summary>Carpeta de descargas: dentro de Program Files, donde solo pueden escribir los administradores.</summary>
    internal static string UpdateDir { get; set; } = Path.Combine(Paths.InstallDir, "update"); // configurable para pruebas
    static string PendingPath => Path.Combine(UpdateDir, "pending.json");

    record PendingInfo(string Version, string File, string Sha256);

    static HttpClient Http(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AppRouter/{Paths.Version}");
        return http;
    }

    public static async Task<Release?> GetLatestAsync()
    {
        var repo = new Uri(Paths.ProjectUrl).AbsolutePath.Trim('/'); // "rboluda/approuter"
        using var http = Http(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var url = root.GetProperty("html_url").GetString() ?? Paths.ProjectUrl;
        if (ParseVersion(tag) is not { } v) return null;

        string? exeName = null, exeUrl = null, sumsUrl = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var dl = a.GetProperty("browser_download_url").GetString();
                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = dl;
                else if (name.StartsWith("AppRouter-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    (exeName, exeUrl) = (name, dl);
            }
        return new Release(v, tag, url, exeName, exeUrl, sumsUrl);
    }

    public static bool IsNewer(Release r) => IsNewer(r.Version);
    static bool IsNewer(Version v) => ParseVersion(Paths.Version) is { } current && v > current;

    static Version? ParseVersion(string s) =>
        Version.TryParse(s.TrimStart('v', 'V').Split('-', '+')[0], out var v) ? v : null;

    /// <summary>
    /// Descarga el instalador de <paramref name="r"/> y lo verifica. Devuelve su ruta, lista para
    /// <see cref="Launch"/>. Lanza excepción si algo no cuadra (y no deja nada a medias).
    /// </summary>
    public static async Task<string> DownloadAsync(Release r, IProgress<int>? percent, CancellationToken ct)
    {
        if (r.ExeUrl is null || r.ExeName is null || r.SumsUrl is null)
            throw new InvalidOperationException(L.T("La versión publicada no incluye el instalador o su suma de comprobación."));

        using var http = Http(TimeSpan.FromMinutes(30));
        var expected = ExpectedHash(await http.GetStringAsync(r.SumsUrl, ct), r.ExeName)
                       ?? throw new InvalidOperationException(L.T("SHA256SUMS.txt no contiene el instalador."));

        Directory.CreateDirectory(UpdateDir);
        var target = Path.Combine(UpdateDir, r.ExeName);
        var part = target + ".part";
        try
        {
            using (var resp = await http.GetAsync(r.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? 0;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[128 * 1024];
                long done = 0;
                int n, lastPct = -1;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    hash.AppendData(buf, 0, n);
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (total > 0 && (int)(done * 100 / total) is var pct && pct != lastPct) percent?.Report(lastPct = pct);
                }
                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("El archivo descargado no coincide con su suma de comprobación (SHA-256). No se ha instalado nada."));
            }
            CheckIdentity(part, r.Version);
            File.Move(part, target, overwrite: true);
            File.WriteAllText(PendingPath, JsonSerializer.Serialize(new PendingInfo(r.Version.ToString(3), target, expected)));
            return target;
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }

    /// <summary>Línea «hash  nombre» de SHA256SUMS.txt.</summary>
    internal static string? ExpectedHash(string sums, string fileName) =>
        sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2 && p[1].Trim().TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .Select(p => p[0])
            .FirstOrDefault(h => h.Length == 64);

    static void CheckIdentity(string file, Version version)
    {
        var info = FileVersionInfo.GetVersionInfo(file);
        if (info.ProductName != Paths.AppName || !(info.ProductVersion ?? "").StartsWith(version.ToString(3), StringComparison.Ordinal))
            throw new InvalidOperationException(L.F("El archivo descargado no es AppRouter {0}. No se ha instalado nada.", version.ToString(3)));
    }

    /// <summary>Actualización ya descargada y verificada, más nueva que la instalada (o null).</summary>
    public static (string Version, string File)? Pending()
    {
        try
        {
            if (!File.Exists(PendingPath)) return null;
            var p = JsonSerializer.Deserialize<PendingInfo>(File.ReadAllText(PendingPath));
            if (p is null || !File.Exists(p.File) || ParseVersion(p.Version) is not { } v || !IsNewer(v)) return null;
            return (p.Version, p.File);
        }
        catch { return null; }
    }

    /// <summary>
    /// Ejecuta el instalador descargado en modo silencioso. Antes vuelve a comprobar su SHA-256
    /// (por si el archivo hubiera cambiado desde la descarga).
    /// </summary>
    public static void Launch(string file)
    {
        var p = JsonSerializer.Deserialize<PendingInfo>(File.ReadAllText(PendingPath))
                ?? throw new InvalidOperationException("pending.json");
        using (var fs = File.OpenRead(file))
            if (!Convert.ToHexString(SHA256.HashData(fs)).Equals(p.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.T("El archivo descargado no coincide con su suma de comprobación (SHA-256). No se ha instalado nada."));
        Process.Start(new ProcessStartInfo(file, "--update") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    /// <summary>Borra descargas ya instaladas u obsoletas (al arrancar la versión nueva).</summary>
    public static void Cleanup()
    {
        if (Pending() is null)
            try { if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, recursive: true); } catch { /* se reintenta otro día */ }
    }
}
