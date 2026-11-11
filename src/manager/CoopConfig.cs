using System.IO;

namespace MW3ModManager;

// The Survival workarounds live in players2\config.cfg, which the game reads
// before its first frame and rewrites when it exits. It may also be read-only
// on purpose, so every write clears that attribute and puts it back.
public static class CoopConfig
{
    public const int DefaultFrameCap = 60;

    public sealed record State(bool Exists, bool ReadOnly, int FrameCap, string? Vsync,
        string? MaxPackets, string? PacketDup);

    public static string? ConfigPath(string? gameDir) =>
        gameDir is null ? null : Path.Combine(gameDir, "players2", "config.cfg");

    public static bool IsReadOnly(string? gameDir)
    {
        string? path = ConfigPath(gameDir);
        if (path is null || !File.Exists(path)) return false;
        return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
    }

    public static void SetReadOnly(string gameDir, bool readOnly)
    {
        string path = ConfigPath(gameDir) ?? throw new InvalidOperationException("The game folder is not set.");
        if (!File.Exists(path))
            throw new FileNotFoundException("players2\\config.cfg does not exist yet. Launch the game once, close it, then try again.", path);

        FileAttributes attributes = File.GetAttributes(path);
        File.SetAttributes(path, readOnly
            ? attributes | FileAttributes.ReadOnly
            : attributes & ~FileAttributes.ReadOnly);
    }

    public static State Read(string? gameDir)
    {
        string? path = ConfigPath(gameDir);
        if (path is null || !File.Exists(path))
            return new State(false, false, 0, null, null, null);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path))
        {
            if (TryParse(line, out string key, out string value))
                values[key] = value;
        }

        values.TryGetValue("com_maxfps", out string? frameCap);
        int cap = int.TryParse(frameCap, out int parsed) ? parsed : 0;

        values.TryGetValue("r_vsync", out string? vsync);
        values.TryGetValue("cl_maxpackets", out string? maxPackets);
        values.TryGetValue("cl_packetdup", out string? packetDup);

        return new State(true, IsReadOnly(gameDir), cap, vsync, maxPackets, packetDup);
    }

    /// <summary>
    /// Writes the four community settings into config.cfg. Returns true when the
    /// file was read-only and has been left read-only again.
    /// </summary>
    public static bool Apply(string gameDir, int frameCap)
    {
        if (frameCap != 60 && frameCap != 120)
            throw new ArgumentOutOfRangeException(nameof(frameCap), "The frame cap must be 60 or 120.");

        string path = ConfigPath(gameDir) ?? throw new InvalidOperationException("The game folder is not set.");
        if (!File.Exists(path))
            throw new FileNotFoundException("players2\\config.cfg does not exist yet. Launch the game once, close it, then try again.", path);

        bool readOnly = IsReadOnly(gameDir);
        try
        {
            if (readOnly) SetReadOnly(gameDir, false);

            string text = File.ReadAllText(path);
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = text.Split(newline).ToList();

            Set(lines, "r_vsync", "1");
            Set(lines, "cl_maxpackets", "100");
            Set(lines, "cl_packetdup", "2");
            Set(lines, "com_maxfps", frameCap.ToString());

            File.WriteAllText(path, string.Join(newline, lines));
        }
        finally
        {
            if (readOnly) SetReadOnly(gameDir, true);
        }

        return readOnly;
    }

    private static void Set(List<string> lines, string key, string value)
    {
        string wanted = $"seta {key} \"{value}\"";

        for (int i = 0; i < lines.Count; ++i)
        {
            if (TryParse(lines[i], out string existing, out _) &&
                existing.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = wanted;
                return;
            }
        }

        // Keep a trailing newline if the game wrote one, so the next launch does
        // not see a half-written final line.
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.Insert(lines.Count - 1, wanted);
        else
            lines.Add(wanted);
    }

    private static bool TryParse(string line, out string key, out string value)
    {
        key = "";
        value = "";

        string trimmed = line.Trim().TrimStart('\uFEFF');
        int firstSpace = trimmed.IndexOf(' ');
        if (firstSpace <= 0) return false;

        string command = trimmed[..firstSpace];
        if (!command.Equals("seta", StringComparison.OrdinalIgnoreCase) &&
            !command.Equals("set", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = trimmed[(firstSpace + 1)..].TrimStart();
        int secondSpace = rest.IndexOf(' ');
        if (secondSpace <= 0) return false;

        key = rest[..secondSpace];
        value = rest[(secondSpace + 1)..].Trim().Trim('"');
        return true;
    }
}
