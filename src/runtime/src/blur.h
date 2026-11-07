#pragma once

#include "settings.h"

namespace blur {

// Hooks the engine's script-level blur setter so the radius can be scaled or
// switched off. Returns false (and leaves the game alone) if the prologue does
// not match the build this was written against.
bool Install();
void Update(const ModSettings& settings);

}  // namespace blur
