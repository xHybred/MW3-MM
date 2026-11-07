using System.IO;
using System.Text;

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
        FileContains(Path.Combine(gameDir, DxvkName), "DXVK");

    public static void InstallDxvk(string gameDir, string sourceDll)
    {
        if (!FileContains(sourceDll, "DXVK"))
            throw new InvalidOperationException("That file does not look like a DXVK d3d9.dll.");

        File.Copy(sourceDll, Path.Combine(gameDir, DxvkName), overwrite: true);
    }

    public static bool RemoveDxvk(string gameDir)
    {
        string path = Path.Combine(gameDir, DxvkName);
        if (!File.Exists(path) || !FileContains(path, "DXVK")) return false;
        File.Delete(path);
        return true;
    }

    private static bool FileContains(string path, string needle)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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
