using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MW3ModManager;

// Pulls the latest release straight from dxvk's GitHub and takes only the
// 64-bit d3d9.dll out of the tarball. The archive layout has changed before,
// so anything ending in d3d9.dll outside x32/ is an acceptable second choice.
public static class DxvkDownloader
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/doitsujin/dxvk/releases/latest";

    public sealed record Release(string Tag, string AssetName, string Url);

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
            throw new InvalidOperationException("The latest DXVK release lists no downloads.");

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (name is null) continue;
            if (!name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.StartsWith("dxvk-", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("native", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = asset.GetProperty("browser_download_url").GetString();
            if (url is null) continue;

            return new Release(tag, name, url);
        }

        throw new InvalidOperationException($"DXVK {tag} ships no .tar.gz download.");
    }

    public static async Task<string> DownloadAsync(
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

        return ExtractD3d9(archive, workDir);
    }

    private static string ExtractD3d9(string archive, string workDir)
    {
        string output = Path.Combine(workDir, "d3d9.dll");
        bool found = false;
        bool foundFallback = false;

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;

            string name = entry.Name.Replace('\\', '/').TrimStart('.', '/');
            if (!name.EndsWith("d3d9.dll", StringComparison.OrdinalIgnoreCase)) continue;

            if (name.Contains("/x64/", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(output, overwrite: true);
                found = true;
                break;
            }

            if (!foundFallback && !name.Contains("/x32/", StringComparison.OrdinalIgnoreCase))
            {
                entry.ExtractToFile(output, overwrite: true);
                foundFallback = true;
            }
        }

        if (found || foundFallback) return output;

        throw new InvalidOperationException("That DXVK archive contains no 64-bit d3d9.dll.");
    }
}
