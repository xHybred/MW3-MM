#pragma once

#include "settings.h"

namespace viewmodel
{
    // Hooks the per-frame viewmodel origin calculation so the gun can be
    // shifted sideways and vertically without moving the world camera.
    // Returns false when the engine build does not match.
    bool Install();
    void Update(const ModSettings& settings);
}
