#pragma once

struct ModSettings;

namespace weapon
{
    // Called every frame from the ADS FOV hook, on the game's own thread, with
    // the client the engine just resolved. Reading the weapon def here is safe
    // because the engine touched the same pointer moments earlier.
    void ApplyFromClient(int clientNum);

    void Update(const ModSettings& settings);
}
