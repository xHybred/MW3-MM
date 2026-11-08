using System.Globalization;
using System.IO;

namespace MW3ModManager;

// Mirrors the [FOV] and [Debug] sections the runtime reads with
// GetPrivateProfileString, so the two never disagree about a key name.
public sealed class ModConfig
{
    // Kept in step with the runtime's own clamps in dllmain.cpp and adszoom.cpp.
    public const float MinFov = 40f;
    public const float MaxFov = 130f;
    public const float MinZoom = 1.0f;
    public const float MaxZoom = 1.35f;

    public bool Enabled { get; set; } = true;
    public bool Affected { get; set; } = true;
    public float Fov { get; set; } = 90f;
    public bool AimEnabled { get; set; } = true;
    public bool AimFlat { get; set; }
    public float AimZoom { get; set; } = 1.1f;
    public bool FlinchEnabled { get; set; } = true;
    public float FlinchStrength { get; set; } = 1.0f;
    public bool BlurEnabled { get; set; } = true;
    public float BlurStrength { get; set; } = 1.0f;
    public bool BlurDebug { get; set; }
    public bool RecoilEnabled { get; set; } = true;
    public float RecoilStrength { get; set; } = 1.0f;
    public bool RecoilDebug { get; set; }

    public bool SwayEnabled { get; set; } = true;
    public float SwayStrength { get; set; } = 1.0f;
    public bool FlashEnabled { get; set; } = true;
    public bool ViewmodelEnabled { get; set; } = true;
    public float ViewmodelX { get; set; }
    public float ViewmodelZ { get; set; }
    public float ViewmodelY { get; set; }
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
                            config.Fov = Math.Clamp(fov, MinFov, MaxFov);
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
                            config.AimZoom = Math.Clamp(zoom, MinZoom, MaxZoom);
                        break;
                }
            }
            else if (section.Equals("Flinch", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.FlinchEnabled = ParseBool(value, config.FlinchEnabled); break;
                    case "strength":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float strength))
                            config.FlinchStrength = Math.Clamp(strength, 0f, 1f);
                        break;
                }
            }
            else if (section.Equals("Blur", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.BlurEnabled = ParseBool(value, config.BlurEnabled); break;
                    case "strength":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float blurStrength))
                            config.BlurStrength = Math.Clamp(blurStrength, 0f, 1f);
                        break;
                }
            }
            else if (section.Equals("Recoil", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.RecoilEnabled = ParseBool(value, config.RecoilEnabled); break;
                    case "strength":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float recoilStrength))
                            config.RecoilStrength = Math.Clamp(recoilStrength, 0f, 1f);
                        break;
                }
            }
            else if (section.Equals("Sway", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.SwayEnabled = ParseBool(value, config.SwayEnabled); break;
                    case "strength":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float swayStrength))
                            config.SwayStrength = Math.Clamp(swayStrength, 0f, 1f);
                        break;
                }
            }
            else if (section.Equals("Flash", StringComparison.OrdinalIgnoreCase))
            {
                if (key.ToLowerInvariant() == "enabled")
                    config.FlashEnabled = ParseBool(value, config.FlashEnabled);
            }
            else if (section.Equals("Viewmodel", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.ViewmodelEnabled = ParseBool(value, config.ViewmodelEnabled); break;
                    case "xoffset":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                            config.ViewmodelX = Math.Clamp(x, -1.5f, 3f);
                        break;
                    case "zoffset":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                            config.ViewmodelZ = Math.Clamp(z, -3f, 3f);
                        break;
                    case "yoffset":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                            config.ViewmodelY = Math.Clamp(y, -3f, 3f);
                        break;
                }
            }
            else if (section.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "logeffectivefov": config.LogEffectiveFov = ParseBool(value, config.LogEffectiveFov); break;
                    case "dvarscan": config.DvarScan = ParseBool(value, config.DvarScan); break;
                    case "blurcalls": config.BlurDebug = ParseBool(value, config.BlurDebug); break;
                    case "recoil": config.RecoilDebug = ParseBool(value, config.RecoilDebug); break;
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
            "[Flinch]",
            $"Enabled={(FlinchEnabled ? 1 : 0)}",
            "Strength=" + FlinchStrength.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Blur]",
            $"Enabled={(BlurEnabled ? 1 : 0)}",
            "Strength=" + BlurStrength.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Recoil]",
            $"Enabled={(RecoilEnabled ? 1 : 0)}",
            "Strength=" + RecoilStrength.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Sway]",
            $"Enabled={(SwayEnabled ? 1 : 0)}",
            "Strength=" + SwayStrength.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Flash]",
            $"Enabled={(FlashEnabled ? 1 : 0)}",
            "",
            "[Viewmodel]",
            $"Enabled={(ViewmodelEnabled ? 1 : 0)}",
            "XOffset=" + ViewmodelX.ToString("0.###", CultureInfo.InvariantCulture),
            "ZOffset=" + ViewmodelZ.ToString("0.###", CultureInfo.InvariantCulture),
            "YOffset=" + ViewmodelY.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Debug]",
            $"LogEffectiveFov={(LogEffectiveFov ? 1 : 0)}",
            $"DvarScan={(DvarScan ? 1 : 0)}",
            $"Recoil={(RecoilDebug ? 1 : 0)}",
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
