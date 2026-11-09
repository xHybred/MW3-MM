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
    public const float MinCrosshairSize = 0.5f;
    public const float MaxCrosshairSize = 2.0f;

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
    public bool SpreadEnabled { get; set; } = true;
    public float SpreadAccuracy { get; set; }
    public float SpreadAimAccuracy { get; set; }
    public float SpreadBloom { get; set; }
    public bool ViewmodelEnabled { get; set; } = true;
    public float ViewmodelX { get; set; }
    public float ViewmodelZ { get; set; }
    public float ViewmodelY { get; set; }
    public bool CrosshairEnabled { get; set; } = true;
    public float CrosshairSize { get; set; } = 1.0f;
    public float CrosshairOpacity { get; set; } = 1.0f;
    public bool CrosshairDynamic { get; set; }
    public bool CrosshairShow { get; set; } = true;
    public int CrosshairStyle { get; set; }
    public bool CrosshairDebug { get; set; }
    public bool SkipIntro { get; set; }
    public bool CheckOnLaunch { get; set; } = true;
    public bool LogEffectiveFov { get; set; }
    public bool DvarScan { get; set; }

    /// <summary>
    /// Copies the values a preset owns. Debug switches are deliberately left
    /// alone: they belong to whoever is chasing a problem, not to a preset.
    /// </summary>
    public void CopyFeatureValuesFrom(ModConfig other)
    {
        Enabled = other.Enabled;
        Affected = other.Affected;
        Fov = other.Fov;

        AimEnabled = other.AimEnabled;
        AimFlat = other.AimFlat;
        AimZoom = other.AimZoom;

        FlinchStrength = other.FlinchStrength;
        BlurStrength = other.BlurStrength;
        RecoilStrength = other.RecoilStrength;
        SwayStrength = other.SwayStrength;
        FlashEnabled = other.FlashEnabled;
        SpreadAccuracy = other.SpreadAccuracy;
        SpreadAimAccuracy = other.SpreadAimAccuracy;
        SpreadBloom = other.SpreadBloom;

        ViewmodelEnabled = other.ViewmodelEnabled;
        ViewmodelX = other.ViewmodelX;
        ViewmodelZ = other.ViewmodelZ;
        ViewmodelY = other.ViewmodelY;

        CrosshairEnabled = other.CrosshairEnabled;
        CrosshairSize = other.CrosshairSize;
        CrosshairOpacity = other.CrosshairOpacity;
        CrosshairDynamic = other.CrosshairDynamic;
        CrosshairShow = other.CrosshairShow;
        CrosshairStyle = other.CrosshairStyle;
    }

    /// <summary>True when every value a preset sets still matches this config.</summary>
    public bool MatchesFeatureValues(ModConfig other)
    {
        const float eps = 0.0005f;

        bool Same(float a, float b) => Math.Abs(a - b) <= eps;

        return Enabled == other.Enabled &&
               Affected == other.Affected &&
               Same(Fov, other.Fov) &&
               AimEnabled == other.AimEnabled &&
               AimFlat == other.AimFlat &&
               Same(AimZoom, other.AimZoom) &&
               Same(FlinchStrength, other.FlinchStrength) &&
               Same(BlurStrength, other.BlurStrength) &&
               Same(RecoilStrength, other.RecoilStrength) &&
               Same(SwayStrength, other.SwayStrength) &&
               FlashEnabled == other.FlashEnabled &&
               Same(SpreadAccuracy, other.SpreadAccuracy) &&
               Same(SpreadAimAccuracy, other.SpreadAimAccuracy) &&
               Same(SpreadBloom, other.SpreadBloom) &&
               ViewmodelEnabled == other.ViewmodelEnabled &&
               Same(ViewmodelX, other.ViewmodelX) &&
               Same(ViewmodelZ, other.ViewmodelZ) &&
               Same(ViewmodelY, other.ViewmodelY) &&
               CrosshairEnabled == other.CrosshairEnabled &&
               Same(CrosshairSize, other.CrosshairSize) &&
               Same(CrosshairOpacity, other.CrosshairOpacity) &&
               CrosshairDynamic == other.CrosshairDynamic &&
               CrosshairShow == other.CrosshairShow &&
               CrosshairStyle == other.CrosshairStyle;
    }

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MW3Mod");

    public static string SettingsPath => Path.Combine(DataDir, "settings.ini");

    public static string LogDir => Path.Combine(DataDir, "Log");

    public static string RuntimeLogPath => Path.Combine(LogDir, "runtime.log");

    public static string ManagerLogPath => Path.Combine(LogDir, "manager.log");

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
            else if (section.Equals("Spread", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.SpreadEnabled = ParseBool(value, config.SpreadEnabled); break;
                    case "accuracy":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float accuracy))
                            config.SpreadAccuracy = Math.Clamp(accuracy, 0f, 1f);
                        break;
                    case "aimaccuracy":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float aimAccuracy))
                            config.SpreadAimAccuracy = Math.Clamp(aimAccuracy, 0f, 1f);
                        break;
                    case "bloom":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float bloom))
                            config.SpreadBloom = Math.Clamp(bloom, 0f, 1f);
                        break;
                }
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
            else if (section.Equals("Crosshair", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "enabled": config.CrosshairEnabled = ParseBool(value, config.CrosshairEnabled); break;
                    case "size":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
                            config.CrosshairSize = Math.Clamp(size, MinCrosshairSize, MaxCrosshairSize);
                        break;
                    case "opacity":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float opacity))
                            config.CrosshairOpacity = Math.Clamp(opacity, 0f, 1f);
                        break;
                    case "dynamic": config.CrosshairDynamic = ParseBool(value, config.CrosshairDynamic); break;
                    case "show": config.CrosshairShow = ParseBool(value, config.CrosshairShow); break;
                    case "style":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int style))
                            config.CrosshairStyle = Math.Clamp(style, 0, 2);
                        break;
                }
            }
            else if (section.Equals("Startup", StringComparison.OrdinalIgnoreCase))
            {
                if (key.ToLowerInvariant() == "skipintro")
                    config.SkipIntro = ParseBool(value, config.SkipIntro);
            }
            else if (section.Equals("Update", StringComparison.OrdinalIgnoreCase))
            {
                if (key.ToLowerInvariant() == "checkonlaunch")
                    config.CheckOnLaunch = ParseBool(value, config.CheckOnLaunch);
            }
            else if (section.Equals("Debug", StringComparison.OrdinalIgnoreCase))
            {
                switch (key.ToLowerInvariant())
                {
                    case "logeffectivefov": config.LogEffectiveFov = ParseBool(value, config.LogEffectiveFov); break;
                    case "dvarscan": config.DvarScan = ParseBool(value, config.DvarScan); break;
                    case "blurcalls": config.BlurDebug = ParseBool(value, config.BlurDebug); break;
                    case "recoil": config.RecoilDebug = ParseBool(value, config.RecoilDebug); break;
                    case "crosshair": config.CrosshairDebug = ParseBool(value, config.CrosshairDebug); break;
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
            "[Spread]",
            $"Enabled={(SpreadEnabled ? 1 : 0)}",
            "Accuracy=" + SpreadAccuracy.ToString("0.###", CultureInfo.InvariantCulture),
            "AimAccuracy=" + SpreadAimAccuracy.ToString("0.###", CultureInfo.InvariantCulture),
            "Bloom=" + SpreadBloom.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Viewmodel]",
            $"Enabled={(ViewmodelEnabled ? 1 : 0)}",
            "XOffset=" + ViewmodelX.ToString("0.###", CultureInfo.InvariantCulture),
            "ZOffset=" + ViewmodelZ.ToString("0.###", CultureInfo.InvariantCulture),
            "YOffset=" + ViewmodelY.ToString("0.###", CultureInfo.InvariantCulture),
            "",
            "[Crosshair]",
            $"Enabled={(CrosshairEnabled ? 1 : 0)}",
            "Size=" + CrosshairSize.ToString("0.###", CultureInfo.InvariantCulture),
            "Opacity=" + CrosshairOpacity.ToString("0.###", CultureInfo.InvariantCulture),
            $"Dynamic={(CrosshairDynamic ? 1 : 0)}",
            $"Show={(CrosshairShow ? 1 : 0)}",
            $"Style={CrosshairStyle}",
            "",
            "[Startup]",
            $"SkipIntro={(SkipIntro ? 1 : 0)}",
            "",
            "[Update]",
            $"CheckOnLaunch={(CheckOnLaunch ? 1 : 0)}",
            "",
            "[Debug]",
            $"LogEffectiveFov={(LogEffectiveFov ? 1 : 0)}",
            $"DvarScan={(DvarScan ? 1 : 0)}",
            $"Recoil={(RecoilDebug ? 1 : 0)}",
            $"Crosshair={(CrosshairDebug ? 1 : 0)}",
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
