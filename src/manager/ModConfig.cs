using System.Globalization;
using System.IO;

namespace MW3ModManager;

// Mirrors the [FOV] and [Debug] sections the runtime reads with
// GetPrivateProfileString, so the two never disagree about a key name.
public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public bool Affected { get; set; } = true;
    public float Fov { get; set; } = 90f;
    public bool AimEnabled { get; set; } = true;
    public bool AimFlat { get; set; }
    public float AimZoom { get; set; } = 1.1f;
    public bool LogEffectiveFov { get; set; }
    public bool DvarScan { get; set; }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MW3Mod");

    public static string SettingsPath => Path.Combine(DataDir, "settings.ini");

    public static string LogPath => Path.Combine(DataDir, "runtime.log");

    public static ModConfig Load()
    {
        var config = new ModConfig();
        string path = SettingsPath;
        if (!File.Exists(path)) return config;

        string section = "";
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                section = close > 1 ? line[1..close].Trim() : "";
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (section.Equals("FOV", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.Enabled = ParseBool(value, config.Enabled); break;
                    case "affected": config.Affected = ParseBool(value, config.Affected); break;
                    case "value":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fov))
                            config.Fov = Math.Clamp(fov, 40f, 160f);
                        break;
                }
            }
            else if (section.Equals("Aim", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.AimEnabled = ParseBool(value, config.AimEnabled); break;
                    case "flat": config.AimFlat = ParseBool(value, config.AimFlat); break;
                    case "zoom":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float zoom))
                            config.AimZoom = Math.Clamp(zoom, 1f, 2f);
                        break;
                }
            }
            else if (section.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "logeffectivefov": config.LogEffectiveFov = ParseBool(value, config.LogEffectiveFov); break;
                    case "dvarscan": config.DvarScan = ParseBool(value, config.DvarScan); break;
                }
            }
        }
        return config;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        string text = string.Join(Environment.NewLine, new[]
        {
            "; Written by the MW3 Mod Manager. The runtime re-reads this every 250 ms.",
            "[FOV]",
            $"Enabled={(Enabled ? 1 : 0)}",
            $"Affected={(Affected ? 1 : 0)}",
            "Value=" + Fov.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Aim]",
            $"Enabled={(AimEnabled ? 1 : 0)}",
            $"Flat={(AimFlat ? 1 : 0)}",
            "Zoom=" + AimZoom.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Debug]",
            $"LogEffectiveFov={(LogEffectiveFov ? 1 : 0)}",
            $"DvarScan={(DvarScan ? 1 : 0)}",
            "",
        });

        // The runtime polls the file, so never let it see a half-written one.
        string temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, SettingsPath, overwrite: true);
    }

    private static bool ParseBool(string value, bool fallback) => value switch
    {
        "1" or "true" or "True" or "yes" => true,
        "0" or "false" or "False" or "no" => false,
        _ => fallback,
    };
}
