using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MW3ModManager;

// Lives here rather than in the registry-only path lookup because the game can
// sit in any Steam library, and the 2011 folder name is not the one Steam uses
// for the app title.
public static class GameLocator
{
    public const string InstallFolderName = "Call of Duty Modern Warfare 3";
    public const string ExeName = "iw5sp.exe";

    public static string? FindGame()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steam in SteamRoots())
        {
            if (!seen.Add(steam)) continue;

            string? fromManifest = ReadInstallDir(Path.Combine(steam, "steamapps", "appmanifest_42680.acf"));
            if (fromManifest is not null)
            {
                string candidate = Path.Combine(steam, "steamapps", "common", fromManifest);
                if (IsGameFolder(candidate)) return candidate;
            }

            string plain = Path.Combine(steam, "steamapps", "common", InstallFolderName);
            if (IsGameFolder(plain)) return plain;
        }

        foreach (string library in SteamLibraries())
        {
            if (!seen.Add(library)) continue;
            string candidate = Path.Combine(library, "steamapps", "common", InstallFolderName);
            if (IsGameFolder(candidate)) return candidate;
        }

        return null;
    }

    public static bool IsGameFolder(string? folder) =>
        !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, ExeName));

    private static IEnumerable<string> SteamRoots()
    {
        foreach (string key in new[] { @"Software\Valve\Steam", @"Software\WOW6432Node\Valve\Steam" })
        {
            string? path = ReadValue(Registry.CurrentUser, key, "SteamPath")
                           ?? ReadValue(Registry.LocalMachine, key, "InstallPath");
            if (!string.IsNullOrWhiteSpace(path)) yield return path.Replace('/', '\\');
        }
    }

    // libraryfolders.vdf is an ad-hoc key file; a path regex is enough for the
    // three fields that matter and avoids shipping a vdf parser.
    private static IEnumerable<string> SteamLibraries()
    {
        foreach (string steam in SteamRoots().ToArray())
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { continue; }

            foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path)) yield return path;
            }
        }
    }

    private static string? ReadInstallDir(string acfPath)
    {
        if (!File.Exists(acfPath)) return null;
        try
        {
            Match match = Regex.Match(File.ReadAllText(acfPath), "\"installdir\"\\s+\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? ReadValue(RegistryKey root, string keyPath, string name)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(keyPath);
            return key?.GetValue(name) as string;
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
    }
}
