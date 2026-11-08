using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MW3ModManager;

public static class RuntimeInstaller
{
    // The game ships without this file, loads it from its own folder before the
    // system copy, and our build forwards the two X3DAudio exports onward.
    public const string ProxyName = "X3DAudio1_7.dll";

    // Only one wrapper can own this name, so DXVK, dgVoodoo and ReShade all
    // compete for the same file.
    public const string D3d9Name = "d3d9.dll";
    public const string DgVoodooConfName = "dgVoodoo.conf";

    public enum D3d9Owner { Nobody, Dxvk, DgVoodoo, ReShade, SomethingElse }

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

    public static D3d9Owner Owner(string? gameDir)
    {
        if (gameDir is null) return D3d9Owner.Nobody;

        string path = Path.Combine(gameDir, D3d9Name);
        if (!File.Exists(path)) return D3d9Owner.Nobody;

        var probe = Probe(path);
        if (probe.Dxvk) return D3d9Owner.Dxvk;
        if (probe.DgVoodoo) return D3d9Owner.DgVoodoo;
        if (probe.ReShade) return D3d9Owner.ReShade;
        return D3d9Owner.SomethingElse;
    }

    public static string OwnerName(D3d9Owner owner) => owner switch
    {
        D3d9Owner.Dxvk => "DXVK",
        D3d9Owner.DgVoodoo => "Direct3D 11",
        D3d9Owner.ReShade => "ReShade",
        D3d9Owner.SomethingElse => "another program",
        _ => "nothing",
    };

    public static bool DxvkInstalled(string? gameDir) =>
        Owner(gameDir) == D3d9Owner.Dxvk;

    public static string? DxvkVersion(string? gameDir) =>
        gameDir is null ? null : Probe(Path.Combine(gameDir, D3d9Name)).Version;

    public static void InstallDxvk(string gameDir, string sourceDll)
    {
        if (!Probe(sourceDll).Dxvk)
            throw new InvalidOperationException("That file does not look like a DXVK d3d9.dll.");

        var taken = Owner(gameDir);
        if (taken != D3d9Owner.Nobody && taken != D3d9Owner.Dxvk)
            throw new InvalidOperationException(
                $"d3d9.dll is taken by {OwnerName(taken)}. Remove it first.");

        File.Copy(sourceDll, Path.Combine(gameDir, D3d9Name), overwrite: true);
        Probed.Clear();
    }

    public static bool RemoveDxvk(string gameDir)
    {
        string path = Path.Combine(gameDir, D3d9Name);
        if (!File.Exists(path) || !Probe(path).Dxvk) return false;
        File.Delete(path);
        Probed.Clear();
        return true;
    }

    public static bool D3d11Installed(string? gameDir) =>
        Owner(gameDir) == D3d9Owner.DgVoodoo;

    public static string? D3d11Version(string? gameDir)
    {
        if (gameDir is null) return null;

        string path = Path.Combine(gameDir, D3d9Name);
        if (!File.Exists(path) || !Probe(path).DgVoodoo) return null;

        try
        {
            // Unlike DXVK, dgVoodoo fills in its Windows version resource for
            // real, and the file description names the release.
            string? text = FileVersionInfo.GetVersionInfo(path).FileDescription;
            var match = text is null ? null : Regex.Match(text, @"dgVoodoo (\d+\.\d+\.\d+)");
            return match is { Success: true } ? match.Groups[1].Value : null;
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

    public static void InstallD3d11(string gameDir, string sourceDll, string? sourceConf)
    {
        // The x86 build sits in the folder, is refused by the loader and the game
        // quietly goes back to its own Direct3D 9, so it is worth refusing here.
        if (!Is64Bit(sourceDll))
            throw new InvalidOperationException(
                "That D3D9.dll is the 32-bit build. A 64-bit game only loads the x64 one.");

        var taken = Owner(gameDir);
        if (taken != D3d9Owner.Nobody && taken != D3d9Owner.DgVoodoo)
            throw new InvalidOperationException(
                $"d3d9.dll is taken by {OwnerName(taken)}. Remove it first.");

        File.Copy(sourceDll, Path.Combine(gameDir, D3d9Name), overwrite: true);

        if (sourceConf is not null && File.Exists(sourceConf))
        {
            string conf = Path.Combine(gameDir, DgVoodooConfName);
            File.Copy(sourceConf, conf, overwrite: true);
            RaiseVideoMemory(conf);
        }

        Probed.Clear();
    }

    // dgVoodoo's shipped conf reports 256 MB of video memory, and with that the
    // game renders a mission but never starts it: the loading card stays on
    // screen and the level never registers. 1024 MB is what the wrapper's own
    // DX9 preset uses, and missions load with it.
    private static void RaiseVideoMemory(string conf)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllText(conf).Split('\n');
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            int equals = lines[i].IndexOf('=');
            if (equals < 0) continue;
            if (!lines[i][..equals].Trim().Equals("VRAM", StringComparison.OrdinalIgnoreCase)) continue;

            string ending = lines[i].EndsWith('\r') ? "\r" : "";
            lines[i] = "VRAM                                = 1024" + ending;
            changed = true;
            break;
        }

        if (!changed) return;

        try
        {
            File.WriteAllText(conf, string.Join('\n', lines));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static bool RemoveD3d11(string gameDir)
    {
        string path = Path.Combine(gameDir, D3d9Name);
        if (!File.Exists(path) || !Probe(path).DgVoodoo) return false;

        File.Delete(path);

        string conf = Path.Combine(gameDir, DgVoodooConfName);
        if (File.Exists(conf)) File.Delete(conf);

        Probed.Clear();
        return true;
    }

    public static bool Is64Bit(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            stream.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();

            stream.Seek(peOffset + 4, SeekOrigin.Begin);
            return reader.ReadUInt16() == 0x8664;
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

    private sealed record DllProbe(bool Dxvk, bool DgVoodoo, bool ReShade, string? Version);

    // Answers cost a full read of the file, so keep each answer until it changes.
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

            bool dxvk = Mentions(bytes, "DXVK");
            bool dgVoodoo = Mentions(bytes, "dgVoodoo");
            bool reShade = Mentions(bytes, "ReShade");

            var probe = new DllProbe(dxvk, dgVoodoo, reShade, dxvk ? FindVersion(bytes) : null);
            Probed[path] = (file.Length, file.LastWriteTimeUtc, probe);
            return probe;
        }
        catch (FileNotFoundException)
        {
            return new DllProbe(false, false, false, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new DllProbe(false, false, false, null);
        }
        catch (IOException)
        {
            return new DllProbe(false, false, false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new DllProbe(false, false, false, null);
        }
    }

    // DXVK and ReShade name themselves in plain ASCII, while dgVoodoo only does so
    // inside its UTF-16 version resource, so both encodings are searched.
    private static bool Mentions(byte[] bytes, string text)
    {
        return bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0 ||
               bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;
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
