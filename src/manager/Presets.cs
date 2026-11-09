using System.Collections.Generic;

namespace MW3ModManager;

/// <summary>
/// A named set of feature values. Adding a preset is one entry in <see cref="All"/>;
/// the manager builds one button per entry and highlights whichever one the live
/// configuration still matches.
/// </summary>
public sealed class Preset
{
    public Preset(string name, string description, ModConfig values)
    {
        Name = name;
        Description = description;
        Values = values;
    }

    public string Name { get; }

    public string Description { get; }

    public ModConfig Values { get; }
}

public static class Presets
{
    public static Preset Default { get; } = new(
        "Default",
        "Everything the game ships with: 65 degree hipfire, stock damage feedback, no viewmodel or crosshair changes.",
        new ModConfig
        {
            Enabled = true,
            Affected = true,
            Fov = 65f,

            // Vanilla irons are a fixed 1.625x zoom and the zoom slider has no
            // value for that, so leaving the weapon data alone is the only way
            // to get stock aiming back.
            AimEnabled = false,
            AimFlat = false,
            AimZoom = 1.1f,

            FlinchStrength = 1.0f,
            BlurStrength = 1.0f,
            RecoilStrength = 1.0f,
            SwayStrength = 1.0f,
            FlashEnabled = true,
            SpreadAccuracy = 0f,
            SpreadAimAccuracy = 0f,
            SpreadBloom = 0f,

            ViewmodelEnabled = true,
            ViewmodelX = 0f,
            ViewmodelZ = 0f,
            ViewmodelY = 0f,

            CrosshairEnabled = true,
            CrosshairSize = 1.0f,
            CrosshairOpacity = 1.0f,
            CrosshairDynamic = false,
            CrosshairShow = true,
            CrosshairStyle = 0,
        });

    public static Preset Recommended { get; } = new(
        "Recommended",
        "A wide view with quieter damage feedback, a steadier gun and a clean crosshair.",
        new ModConfig
        {
            Enabled = true,
            Affected = true,
            Fov = 105f,

            AimEnabled = true,
            AimFlat = false,
            AimZoom = 1.07f,

            FlinchStrength = 0.2f,
            BlurStrength = 0f,
            RecoilStrength = 0.4f,
            SwayStrength = 0f,
            FlashEnabled = false,
            SpreadAccuracy = 0.25f,
            SpreadAimAccuracy = 0.25f,
            SpreadBloom = 1f,

            ViewmodelEnabled = true,
            ViewmodelX = -1.2f,
            ViewmodelZ = 0f,
            ViewmodelY = 0f,

            CrosshairEnabled = true,
            CrosshairSize = 0.9f,
            CrosshairOpacity = 0.85f,
            CrosshairDynamic = false,
            CrosshairShow = true,
            CrosshairStyle = 0,
        });

    public static Preset Halo { get; } = new(
        "Halo",
        "A wide view with quieter damage feedback, a steadier gun and a clean crosshair.",
        new ModConfig
        {
            Enabled = true,
            Affected = true,
            Fov = 105f,

            AimEnabled = true,
            AimFlat = false,
            AimZoom = 1.07f,

            FlinchStrength = 0.2f,
            BlurStrength = 0f,
            RecoilStrength = 0.4f,
            SwayStrength = 0f,
            FlashEnabled = false,
            SpreadAccuracy = 0.25f,
            SpreadAimAccuracy = 0.25f,
            SpreadBloom = 1f,

            ViewmodelEnabled = true,
            ViewmodelX = -1.2f,
            ViewmodelZ = 0f,
            ViewmodelY = 0f,

            CrosshairEnabled = true,
            CrosshairSize = 0.9f,
            CrosshairOpacity = 0.85f,
            CrosshairDynamic = false,
            CrosshairShow = true,
            CrosshairStyle = 0,
        });

    public static Preset Bodycam { get; } = new(
        "Bodycam",
        "The handheld-camera look: pulled-in gun, perfect hipfire, half the sway and a small faint crosshair.",
        new ModConfig
        {
            Enabled = true,
            Affected = true,
            Fov = 105f,

            AimEnabled = true,
            AimFlat = false,
            AimZoom = 1.02f,

            FlinchStrength = 0.4f,
            BlurStrength = 0f,
            RecoilStrength = 0.4f,
            SwayStrength = 0.5f,
            FlashEnabled = false,
            SpreadAccuracy = 1.0f,
            SpreadAimAccuracy = 1.0f,
            SpreadBloom = 1.0f,

            ViewmodelEnabled = true,
            ViewmodelX = 1.2f,
            ViewmodelZ = 2.6f,
            ViewmodelY = 1.4f,

            CrosshairEnabled = true,
            CrosshairSize = 0.65f,
            CrosshairOpacity = 0.85f,
            CrosshairDynamic = false,
            CrosshairShow = true,
            CrosshairStyle = 0,
        });

    public static IReadOnlyList<Preset> All { get; } = new[] { Default, Recommended, Halo, Bodycam };
}
