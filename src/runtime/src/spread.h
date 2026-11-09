#pragma once

struct ModSettings;

// The cone a bullet is randomised inside while aiming is not stored where the
// other spread values are read from. It is handed out by one getter, so the aim
// slider scales that getter instead of the float behind it.
namespace spread
{
    bool Install();
    void Update(const ModSettings& settings);
}
