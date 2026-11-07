#pragma once

#include "settings.h"

namespace viewmodel
{
    // Hooks the per-frame viewmodel origin calculation so the gun can be
    // zoomed independently of the world camera. Returns false when the
    // engine build does not match, in which case the world FOV is untouched.
    bool Install();
    void Update(const ModSettings& settings, float pivotFov);
}
