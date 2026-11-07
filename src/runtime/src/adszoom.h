#pragma once

struct ModSettings;

namespace adszoom {

// Rewrites the weapon ADS FOV the engine asks for, so a 1x sight can zoom as
// little as a modern Call of Duty one does. Returns false when the game build
// does not match the signature this was built against.
bool Install();

// Publishes settings for the hook. Called from the mod thread; the hook runs on
// the game thread and only reads these atomics.
void Update(const ModSettings& settings, float pivotFov);

}  // namespace adszoom
