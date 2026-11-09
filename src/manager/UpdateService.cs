using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MW3ModManager;

internal sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string Name,
    string Notes,
    string PageUrl,
    string SetupName,
    string SetupUrl,
    string? ChecksumUrl,
    DateTimeOffset Published);

internal sealed record UpdateDownload(string Path, string Sha256, bool ChecksumVerified);

// Talks to the GitHub Releases API and hands the installer to Windows. The
// download is verified before it is executed; the manager only closes after
// the elevated setup process has actually started.
internal static class UpdateService
{
    private const string DefaultRepository = "xHybred/MW3-MM";
    private const string UserAgent = "MW3ModManager";

    public static string Repository
    {
        get
        {
#if DEBUG
            string? value = Environment.GetEnvironmentVariable("MW3MOD_UPDATE_REPO");
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
#endif
            return DefaultRepository;
        }
    }

    public static Version CurrentVersion =>
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public static bool IsNewer(ReleaseInfo release) => release.Version > CurrentVersion;

    public static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    public static async Task<ReleaseInfo> FindLatestAsync(HttpClient http, CancellationToken token)
    {
        string repository = Repository;
        if (!repository.Contains('/'))
            throw new InvalidOperationException("The update repository must be in owner/name form.");

        string url = "https://api.github.com/repos/" + repository + "/releases/latest";
        using var request = GitHubRequest(url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "GitHub refused the update check. This is usually the hourly rate limit; try again later.");
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "No releases were found at github.com/" + repository + ".");
        }
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        JsonElement root = document.RootElement;

        string tag = GetString(root, "tag_name");
        Version version = ParseVersion(tag)
            ?? throw new InvalidOperationException("The latest release has an unrecognised version tag: " + tag);
        string pageUrl = GetString(root, "html_url");
        string name = GetString(root, "name");
        string notes = GetString(root, "body");
        DateTimeOffset published = root.TryGetProperty("published_at", out JsonElement publishedElement)
            && publishedElement.TryGetDateTimeOffset(out DateTimeOffset date)
            ? date
            : DateTimeOffset.MinValue;

        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The latest release has no downloadable files.");

        string? setupUrl = null;
        string? fallbackSetupUrl = null;
        string setupName = "MW3ModSetup.exe";
        string fallbackSetupName = "MW3ModSetup.exe";
        var checksums = new List<(string Name, string Url)>();

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string assetName = GetString(asset, "name");
            string assetUrl = GetString(asset, "browser_download_url");
            if (assetName.Length == 0 || assetUrl.Length == 0) continue;

            if (assetName.Equals("MW3ModSetup.exe", StringComparison.OrdinalIgnoreCase))
            {
                setupUrl = assetUrl;
                setupName = assetName;
            }
            else if (fallbackSetupUrl is null &&
                     assetName.StartsWith("MW3ModSetup", StringComparison.OrdinalIgnoreCase) &&
                     assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fallbackSetupUrl = assetUrl;
                fallbackSetupName = assetName;
            }

            if (assetName.Contains("sha256", StringComparison.OrdinalIgnoreCase) &&
                (assetName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                 assetName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            {
                checksums.Add((assetName, assetUrl));
            }
        }

        if (setupUrl is null && fallbackSetupUrl is not null)
        {
            setupUrl = fallbackSetupUrl;
            setupName = fallbackSetupName;
        }

        if (setupUrl is null)
            throw new InvalidOperationException("The latest release does not include " + setupName + ".");

        string? checksumUrl =
            checksums.FirstOrDefault(c => c.Name.Equals(setupName + ".sha256", StringComparison.OrdinalIgnoreCase)).Url ??
            checksums.FirstOrDefault(c => c.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)).Url ??
            checksums.FirstOrDefault(c => c.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Url;

        if (checksumUrl is null)
            throw new InvalidDataException("The latest release does not publish a SHA-256 checksum for " + setupName + ".");

        return new ReleaseInfo(
            Normalize(version), tag, name.Length == 0 ? tag : name, notes, pageUrl,
            setupName, setupUrl, checksumUrl, published);
    }

    public static async Task<UpdateDownload> DownloadAsync(
        HttpClient http, ReleaseInfo release, IProgress<double>? progress, CancellationToken token)
    {
        if (release.ChecksumUrl is null)
            throw new InvalidDataException("The release does not publish a SHA-256 checksum, so its installer cannot be verified.");

        string folder = Path.Combine(ModConfig.DataDir, "Updates", SafeName(release.Tag));
        Directory.CreateDirectory(folder);

        string finalPath = Path.Combine(folder, release.SetupName);
        string partPath = finalPath + ".part";
        string? actualHash = null;

        try
        {
            using var request = GitHubRequest(release.SetupUrl);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? 0;
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[131072];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress?.Report(total > 0 ? (double)received / total : -1);
                }

                actualHash = Convert.ToHexString(hash.GetHashAndReset());
            }

            long length = new FileInfo(partPath).Length;
            if (length < 1_000_000)
                throw new InvalidDataException("The downloaded installer is too small to be complete.");

            string expected = await ReadChecksumAsync(http, release, token);
            if (!expected.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded installer did not match the published checksum.");

            VerifyInstallerMetadata(partPath, release.Version);
            File.Move(partPath, finalPath, overwrite: true);
            return new UpdateDownload(finalPath, actualHash, true);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    public static void Install(string setupPath, string installDir, bool relaunch)
    {
        if (!File.Exists(setupPath))
            throw new FileNotFoundException("The downloaded installer is missing.", setupPath);

        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        Directory.CreateDirectory(ModConfig.LogDir);
        string logPath = Path.Combine(ModConfig.LogDir, "update.log");
        string arguments =
            "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /LOG=\"" +
            logPath + "\" /DIR=\"" + directory + "\"";
        if (relaunch) arguments += " /RELAUNCH=1";

        var start = new ProcessStartInfo(setupPath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? directory,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start the update installer.");
    }

    private static HttpRequestMessage GitHubRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent + "/" + CurrentVersionText);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<string> ReadChecksumAsync(
        HttpClient http, ReleaseInfo release, CancellationToken token)
    {
        using var request = GitHubRequest(release.ChecksumUrl!);
        using var response = await http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(token);

        string? fallback = null;
        foreach (string line in text.Split('\n'))
        {
            Match match = Regex.Match(line, @"\b[0-9a-fA-F]{64}\b");
            if (!match.Success) continue;

            if (line.Contains(release.SetupName, StringComparison.OrdinalIgnoreCase))
                return match.Value;
            fallback ??= match.Value;
        }

        return fallback
            ?? throw new InvalidDataException("The release checksum file did not contain a SHA-256 hash.");
    }

    private static void VerifyInstallerMetadata(string path, Version expected)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
        bool company = (info.CompanyName ?? "").Contains("Hybred Tools", StringComparison.OrdinalIgnoreCase);
        bool product = (info.ProductName ?? "").Contains("MW3 Mod Manager", StringComparison.OrdinalIgnoreCase);
        bool version = System.Version.TryParse(info.ProductVersion, out Version? actual) &&
                       Normalize(actual) == Normalize(expected);

        if (!company || !product || !version)
            throw new InvalidDataException("The downloaded file is not the expected MW3 Mod Manager installer.");
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static Version? ParseVersion(string tag)
    {
        Match match = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success) return null;

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new Version(major, minor, build);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);

    private static string SafeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
            builder.Append(invalid.Contains(c) ? '_' : c);
        string result = builder.ToString().Trim();
        return result.Length == 0 ? "latest" : result;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
