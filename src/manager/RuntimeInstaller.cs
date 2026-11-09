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
    public const string DxgiName = "dxgi.dll";
    public const string DgVoodooConfName = "dgVoodoo.conf";

    public enum D3d9Owner { Nobody, Dxvk, DgVoodoo, ReShade, SomethingElse }

    public enum ReShadeMode { None, Direct3D9, Direct3D11 }

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

        string target = Path.Combine(gameDir, ProxyName);
        if (File.Exists(target) && ReadVersionString(target, "MW3Mod ") is null)
            throw new InvalidOperationException(
                "The game's " + ProxyName + " belongs to another program. Remove it yourself if you want to replace it.");

        File.Copy(BundlePath, target, overwrite: true);
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

        if (InstalledMode(gameDir) != ReShadeMode.None)
            throw new InvalidOperationException(
                "ReShade is installed. Remove it first; DXVK needs ReShade's Vulkan install to run together.");

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
        if (taken == D3d9Owner.ReShade)
        {
            // ReShade has to move from the native Direct3D 9 slot to dxgi.dll
            // before dgVoodoo can take d3d9.dll; otherwise it is left behind.
            string dxgi = Path.Combine(gameDir, DxgiName);
            if (File.Exists(dxgi) && !Probe(dxgi).ReShade)
                throw new InvalidOperationException(
                    "dxgi.dll is taken by another program, so ReShade cannot move aside for Direct3D 11.");

            if (!File.Exists(dxgi))
                File.Copy(Path.Combine(gameDir, D3d9Name), dxgi);
        }
        else if (taken != D3d9Owner.Nobody && taken != D3d9Owner.DgVoodoo)
        {
            throw new InvalidOperationException(
                $"d3d9.dll is taken by {OwnerName(taken)}. Remove it first.");
        }

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

        // The game is back on native Direct3D 9, so ReShade has to move from
        // dxgi.dll to the d3d9.dll slot it now owns.
        string dxgi = Path.Combine(gameDir, DxgiName);
        if (File.Exists(dxgi) && Probe(dxgi).ReShade)
        {
            File.Copy(dxgi, Path.Combine(gameDir, D3d9Name), overwrite: true);
            File.Delete(dxgi);
        }

        Probed.Clear();
        return true;
    }

    public static string ReShadeAssetsDir =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "ReShade");

    public static string ReShadeSource => Path.Combine(ReShadeAssetsDir, "ReShade64.dll");

    public static ReShadeMode InstalledMode(string? gameDir)
    {
        if (gameDir is null) return ReShadeMode.None;

        string d3d9 = Path.Combine(gameDir, D3d9Name);
        if (File.Exists(d3d9) && Probe(d3d9).ReShade) return ReShadeMode.Direct3D9;

        // A dxgi.dll copy only means anything while dgVoodoo owns d3d9.dll and
        // turns the game into a Direct3D 11 client.
        string dxgi = Path.Combine(gameDir, DxgiName);
        if (File.Exists(dxgi) && Probe(dxgi).ReShade && Owner(gameDir) == D3d9Owner.DgVoodoo)
            return ReShadeMode.Direct3D11;

        return ReShadeMode.None;
    }

    public static ReShadeMode TargetMode(string? gameDir)
    {
        if (gameDir is null) return ReShadeMode.None;

        var owner = Owner(gameDir);
        if (owner == D3d9Owner.DgVoodoo) return ReShadeMode.Direct3D11;
        if (owner == D3d9Owner.Nobody || owner == D3d9Owner.ReShade) return ReShadeMode.Direct3D9;
        return ReShadeMode.None;
    }

    public static string? ReShadeBlocker(string? gameDir)
    {
        if (gameDir is null) return "Pick the game folder first.";
        if (!File.Exists(ReShadeSource))
            return "The bundled ReShade files are missing next to the manager.";

        var owner = Owner(gameDir);
        if (owner == D3d9Owner.Dxvk)
            return "DXVK and ReShade cannot share d3d9.dll. Remove DXVK first.";
        if (owner == D3d9Owner.SomethingElse)
            return "d3d9.dll is taken by another program. Remove it first.";

        if (owner == D3d9Owner.DgVoodoo)
        {
            string dxgi = Path.Combine(gameDir, DxgiName);
            if (File.Exists(dxgi) && !Probe(dxgi).ReShade)
                return "dxgi.dll is taken by another program. Remove it first.";
        }

        return null;
    }

    public static string? ReShadeVersion(string? gameDir)
    {
        var mode = InstalledMode(gameDir);
        if (gameDir is null || mode == ReShadeMode.None) return null;

        string path = Path.Combine(gameDir, mode == ReShadeMode.Direct3D11 ? DxgiName : D3d9Name);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            string? text = info.ProductVersion ?? info.FileVersion;
            var match = text is null ? null : Regex.Match(text, @"\d+\.\d+\.\d+");
            return match is { Success: true } ? match.Value : null;
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

    public static ReShadeMode InstallReShade(string gameDir)
    {
        string? blocker = ReShadeBlocker(gameDir);
        if (blocker is not null) throw new InvalidOperationException(blocker);

        var target = TargetMode(gameDir);
        string targetName = target == ReShadeMode.Direct3D11 ? DxgiName : D3d9Name;

        File.Copy(ReShadeSource, Path.Combine(gameDir, targetName), overwrite: true);

        // Changing between native Direct3D 9 and the Direct3D 11 wrapper changes
        // the proxy name, so the copy under the old name is no longer loaded.
        string otherName = target == ReShadeMode.Direct3D11 ? D3d9Name : DxgiName;
        string other = Path.Combine(gameDir, otherName);
        if (File.Exists(other) && Probe(other).ReShade) File.Delete(other);

        CopyReShadeAssets(gameDir);
        Probed.Clear();
        return target;
    }

    public static bool RemoveReShade(string gameDir)
    {
        bool removed = false;

        foreach (string name in new[] { DxgiName, D3d9Name })
        {
            string path = Path.Combine(gameDir, name);
            if (!File.Exists(path) || !Probe(path).ReShade) continue;

            File.Delete(path);
            removed = true;
        }

        if (removed) Probed.Clear();
        return removed;
    }

    private static void CopyReShadeAssets(string gameDir)
    {
        CopyIfMissing(Path.Combine(ReShadeAssetsDir, "ReShade.ini"),
                      Path.Combine(gameDir, "ReShade.ini"));
        CopyIfMissing(Path.Combine(ReShadeAssetsDir, "ShaderToggler.addon64"),
                      Path.Combine(gameDir, "ShaderToggler.addon64"));

        CopyTree(Path.Combine(ReShadeAssetsDir, "ReShadePresets"),
                 Path.Combine(gameDir, "ReShadePresets"));
        CopyTree(Path.Combine(ReShadeAssetsDir, "reshade-shaders"),
                 Path.Combine(gameDir, "reshade-shaders"));
    }

    // A returning user keeps their own ReShade.ini, presets and shaders; only
    // the pieces that are missing from the game folder are filled in.
    private static void CopyTree(string source, string target)
    {
        if (!Directory.Exists(source)) return;

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            CopyIfMissing(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target)) return;

        string? folder = Path.GetDirectoryName(target);
        if (folder is not null) Directory.CreateDirectory(folder);

        File.Copy(source, target);
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
