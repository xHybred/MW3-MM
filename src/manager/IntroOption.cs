using System.IO;

namespace MW3ModManager;

// The opening movie is gated by the archived "intro" dvar, which the game reads
// from players2\config.cfg before it draws a frame. The runtime can write the
// dvar too, but only after the engine is up, so the manager edits the file the
// engine reads at startup. The game rewrites this file when it exits, which is
// why a running game has to be closed before we touch it.
public static class IntroOption
{
    public static string? ConfigPath(string? gameDir) =>
        gameDir is null ? null : Path.Combine(gameDir, "players2", "config.cfg");

    public static bool IsSkipped(string? gameDir)
    {
        string? path = ConfigPath(gameDir);
        if (path is null || !File.Exists(path)) return false;

        foreach (string line in File.ReadAllLines(path))
        {
            if (TryParse(line, out bool play)) return !play;
        }
        return false;
    }

    public static void SetSkip(string gameDir, bool skip)
    {
        string path = Path.Combine(gameDir, "players2", "config.cfg");
        string wanted = skip ? "seta intro \"0\"" : "seta intro \"1\"";

        bool readOnly = File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        try
        {
            if (readOnly) CoopConfig.SetReadOnly(gameDir, false);

            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            // Splitting on \n and rejoining the same way keeps whatever line endings
            // the game wrote. It uses LF here, but a CR would ride along harmlessly.
            string[] lines = text.Split('\n');

            int found = -1;
            for (int i = 0; i < lines.Length; ++i)
            {
                if (TryParse(lines[i], out _))
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                lines[found] = wanted;
                File.WriteAllText(path, string.Join("\n", lines));
            }
            else
            {
                string separator = text.Length > 0 && !text.EndsWith('\n') ? "\n" : "";
                File.WriteAllText(path, text + separator + wanted + "\n");
            }
        }
        finally
        {
            // The co-op card may have marked this file read-only, and the intro
            // switch should not silently undo that protection.
            if (readOnly) CoopConfig.SetReadOnly(gameDir, true);
        }
    }

    private static bool TryParse(string line, out bool play)
    {
        play = true;
        string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!parts[0].Equals("seta", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[1].Equals("intro", StringComparison.OrdinalIgnoreCase)) return false;

        play = parts[2].Trim('"') != "0";
        return true;
    }
}
