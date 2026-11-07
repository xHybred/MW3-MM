#pragma once

struct ModSettings;

namespace sway
{
    // Hooks the engine's sway update so the drift can be scaled after it has
    // been worked out. Returns false when the code does not match this build.
    bool Install();

    void Update(const ModSettings& settings);
}
