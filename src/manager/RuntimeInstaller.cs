using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MW3ModManager;

public static class RuntimeInstaller
{
    // The game ships without this file, loads it from its own folder before the
    // system copy, and our build forwards the two X3DAudio exports onward.
    public const string ProxyName = "X3DAudio1_7.dll";
    public const string DxvkName = "d3d9.dll";

    public static string BundlePath => Path.Combine(AppContext.BaseDirectory, "MW3Mod.dll");

    public static string? BundledVersion =>
        File.Exists(BundlePath) ? ReadVersionString(BundlePath, "MW3Mod ") : null;

    public static string? ProxyVersion(string? gameDir)
    {
        if (gameDir is null) return null;
        string path = Path.Combine(gameDir, ProxyName);
        if (!File.Exists(path)) return null;
        return ReadVersionString(path, "MW3Mod ");
    }

    public static bool InstallProxy(string gameDir)
    {
        if (!File.Exists(BundlePath))
            throw new FileNotFoundException("MW3Mod.dll is missing next to the manager.", BundlePath);

        File.Copy(BundlePath, Path.Combine(gameDir, ProxyName), overwrite: true);
        return true;
    }

    public static bool RemoveProxy(string gameDir)
    {
        string path = Path.Combine(gameDir, ProxyName);
        if (!File.Exists(path)) return false;
        if (ReadVersionString(path, "MW3Mod ") is null)
            throw new InvalidOperationException(
                "The installed " + ProxyName + " is not ours, leaving it alone.");

        File.Delete(path);
        return true;
    }

    public static bool DxvkInstalled(string? gameDir) =>
        gameDir is not null && File.Exists(Path.Combine(gameDir, DxvkName)) &&
        Probe(Path.Combine(gameDir, DxvkName)).Ours;

    public static string? DxvkVersion(string? gameDir) =>
        gameDir is null ? null : Probe(Path.Combine(gameDir, DxvkName)).Version;

    public static void InstallDxvk(string gameDir, string sourceDll)
    {
        if (!Probe(sourceDll).Ours)
            throw new InvalidOperationException("That file does not look like a DXVK d3d9.dll.");

        File.Copy(sourceDll, Path.Combine(gameDir, DxvkName), overwrite: true);
        Probed.Clear();
    }

    public static bool RemoveDxvk(string gameDir)
    {
        string path = Path.Combine(gameDir, DxvkName);
        if (!File.Exists(path) || !Probe(path).Ours) return false;
        File.Delete(path);
        Probed.Clear();
        return true;
    }

    private sealed record DllProbe(bool Ours, string? Version);

    // Answers cost a full read of a 7 MB DLL, so keep each answer until the file changes.
    private static readonly Dictionary<string, (long Length, DateTime Written, DllProbe Probe)> Probed = [];

    private static DllProbe Probe(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (Probed.TryGetValue(path, out var known) &&
                known.Length == file.Length && known.Written == file.LastWriteTimeUtc)
            {
                return known.Probe;
            }

            byte[] bytes = File.ReadAllBytes(path);
            bool ours = bytes.AsSpan().IndexOf("DXVK"u8) >= 0;
            var probe = new DllProbe(ours, ours ? FindVersion(bytes) : null);
            Probed[path] = (file.Length, file.LastWriteTimeUtc, probe);
            return probe;
        }
        catch (FileNotFoundException)
        {
            return new DllProbe(false, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new DllProbe(false, null);
        }
        catch (IOException)
        {
            return new DllProbe(false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new DllProbe(false, null);
        }
    }

    // DXVK ships a fake Windows version resource, but stamps the real number into
    // the image next to its build line ("v3.1.1 Build: x86_64").
    private static string? FindVersion(byte[] bytes)
    {
        var match = Regex.Match(Encoding.ASCII.GetString(bytes), @"\bv(\d+\.\d+\.\d+)\b");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadVersionString(string path, string prefix)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            int at = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(prefix));
            if (at < 0) return null;

            var text = new StringBuilder();
            for (int i = at; i < bytes.Length && text.Length < 40; i++)
            {
                byte b = bytes[i];
                if (b < 0x20 || b > 0x7E) break;
                text.Append((char)b);
            }
            return text.ToString();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
