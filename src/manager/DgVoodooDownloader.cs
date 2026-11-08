using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MW3ModManager;

// dgVoodoo 2 is the maintained Direct3D 9 -> Direct3D 11 wrapper. Its zip holds
// one D3D9.dll per architecture, and only the x64 one can load into this game;
// the x86 build sits in the folder, is refused by the loader and the game falls
// back to its own Direct3D 9 with nothing visibly wrong.
public static class DgVoodooDownloader
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/dege-diosg/dgVoodoo2/releases/latest";

    public sealed record Release(string Tag, string AssetName, string Url);

    public sealed record Payload(string Dll, string Conf);

    public static async Task<Release> FindLatestAsync(HttpClient http, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        // GitHub rejects requests that carry no user agent.
        request.Headers.UserAgent.ParseAdd("MW3ModManager");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(response.StatusCode == HttpStatusCode.Forbidden
                ? "GitHub is rate limiting this machine right now. Try again later, or use From file..."
                : $"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));

        string tag = document.RootElement.GetProperty("tag_name").GetString() ?? "unknown";

        if (!document.RootElement.TryGetProperty("assets", out JsonElement assets))
            throw new InvalidOperationException("The latest dgVoodoo release lists no downloads.");

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name is null) continue;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.StartsWith("dgvoodoo2", StringComparison.OrdinalIgnoreCase)) continue;
            // The debug and developer archives carry the same DLLs and are not for players.
            if (name.Contains("_dbg", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("_dev", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = asset.GetProperty("browser_download_url").GetString();
            if (url is null) continue;

            return new Release(tag, name, url);
        }

        throw new InvalidOperationException($"dgVoodoo {tag} ships no .zip download.");
    }

    public static async Task<Payload> DownloadAsync(
        HttpClient http, Release release, string workDir, IProgress<double> progress, CancellationToken token)
    {
        Directory.CreateDirectory(workDir);
        string archive = Path.Combine(workDir, release.AssetName);

        using (var response = await http
            .GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? 0;
            using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var file = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                received += read;
                if (total > 0) progress.Report((double)received / total);
            }
        }

        return Extract(archive, workDir);
    }

    private static Payload Extract(string archive, string workDir)
    {
        string dll = Path.Combine(workDir, "d3d9.dll");
        string conf = Path.Combine(workDir, "dgVoodoo.conf");
        bool foundDll = false;
        bool foundConf = false;

        using var file = File.OpenRead(archive);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            string name = entry.FullName.Replace('\\', '/').TrimStart('.', '/');
            if (name.Length == 0) continue;

            if (!foundDll && name.EndsWith("/x64/d3d9.dll", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(dll, overwrite: true);
                foundDll = true;
            }
            else if (!foundConf && name.EndsWith("dgvoodoo.conf", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(conf, overwrite: true);
                foundConf = true;
            }
        }

        if (!foundDll) throw new InvalidOperationException("That dgVoodoo archive contains no 64-bit D3D9.dll.");

        return new Payload(dll, conf);
    }
}
