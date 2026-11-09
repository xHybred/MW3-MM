#pragma once

#include "settings.h"

namespace overlay {

// Draws the crosshair ourselves over the finished frame. The engine's crosshair
// is a set of textured quads fed by an 8x8 texture, which is why it looks soft
// at high resolution; drawing in screen pixels keeps the edges clean.
bool Install();

void Update(const ModSettings& settings);

// Called from the engine's crosshair drawer, so the overlay only appears when
// the game itself would have drawn one (not over menus or a paused screen).
void MarkEngineCrosshair();

// Called from the aimed-cone query. Lets the custom crosshair stay up while
// aiming down sights, when the option is enabled.
void MarkAiming();

void SetVerticalOffset(float offset);

void SetColor(float r, float g, float b, float a);

}  // namespace overlay
