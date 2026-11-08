#pragma once

#include "settings.h"

namespace crosshair {

// Hooks the engine's crosshair material getter so the drawn size can be scaled.
bool Install();

// Resolves the crosshair dvars; they only exist once a level is running.
bool Resolve();
bool Valid();
void Apply(const ModSettings& settings);
void Restore();
void Update(const ModSettings& settings);

}  // namespace crosshair
