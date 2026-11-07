#pragma once

#include "settings.h"

// Damage view kick, the jump the view takes when the player is hit. The engine
// implements it with dvars, one of them an int, so none of it needs a hook.
namespace flinch {

// Finds the render and flinch dvars that exist from startup and captures what
// they held before we touched them.
bool Resolve();

// True while every dvar still looks like the struct we resolved.
bool Valid();

// Scales the captured values by the user's factors. Cheap enough for the tick:
// it only writes when a value has drifted away from the wanted one.
void Apply(const ModSettings& settings);

// Puts the captured values back.
void Restore();

}  // namespace flinch
