#pragma once

#include <algorithm>

struct ModSettings;

namespace adszoom {

// Native magnification: how much the weapon's sights already zoom. cg_fov 65
// over an iron sight ADS FOV of 40 is 1.625x.
inline constexpr float kIronMagnification = 1.625f;

// The zoom the player picks is expressed as the magnification iron sights
// should get, and the bands below are defined at this value so the slider
// scales them instead of only moving iron sights.
inline constexpr float kDefaultZoom = 1.10f;

// A weapon cannot aim wider than its own FOV, and no sensible optic zooms past
// this; both are here so a bad weapon value cannot drive the view wild.
inline constexpr float kMaxMagnification = 4.0f;

// Ceiling the manager offers for the zoom slider.
inline constexpr float kMaxZoom = 1.35f;

// MW3 tunes aim zoom per weapon by feel, so two pistols can differ far more
// than their sights do. These bands collapse every 1x sight onto the same few
// numbers, the way newer titles do. Anything past the last split is a real
// optic and keeps a scaled-down share of its magnification.
inline constexpr float kBandNoZoom = 1.15f;
inline constexpr float kBandLight = 1.45f;
inline constexpr float kBandIron = 1.85f;
inline constexpr float kZoomNoZoom = 1.06f;
inline constexpr float kZoomLight = 1.08f;

// Rewrites the ADS FOV the engine asks for, so a 1x sight can zoom as little as
// a modern Call of Duty one does. Returns false when the game build does not
// match the signature this was built against.
bool Install();

// Publishes settings for the hook. Called from the mod thread; the hook runs on
// the game thread and only reads these atomics.
void Update(const ModSettings& settings, float pivotFov);

// The magnification the mod asks for, given what the weapon natively does.
// Pure arithmetic so it can be exercised without the game.
inline float MappedMagnification(float vanillaMag, float zoom, bool flat) {
    if (!(zoom >= 1.0f)) zoom = 1.0f;
    zoom = std::min(zoom, kMaxZoom);

    if (flat) return std::min(zoom, kMaxMagnification);

    // How far the slider is from its default, so the low bands move with it.
    const float spread = (zoom - 1.0f) / (kDefaultZoom - 1.0f);

    float mag;
    if (vanillaMag <= kBandNoZoom) {
        mag = 1.0f + (kZoomNoZoom - 1.0f) * spread;
    } else if (vanillaMag <= kBandLight) {
        mag = 1.0f + (kZoomLight - 1.0f) * spread;
    } else if (vanillaMag <= kBandIron) {
        mag = zoom;
    } else {
        mag = 1.0f + (vanillaMag - 1.0f) * (zoom - 1.0f) / (kIronMagnification - 1.0f);
        mag = std::min(mag, vanillaMag);
    }

    return std::clamp(mag, 1.0f, kMaxMagnification);
}

}  // namespace adszoom
