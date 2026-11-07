#include "flinch.h"

#include "dvar.h"
#include "log.h"

#include <cmath>

namespace flinch {
namespace {

struct Vanilla {
    float scale;
    float minKick;
    float maxKick;
    float random;
};

DvarRef g_scale;
DvarRef g_min;
DvarRef g_max;
DvarRef g_random;
Vanilla g_vanilla{};
bool g_resolved = false;

void ScaleFloat(const DvarRef& dvar, float vanilla, float factor, const wchar_t* label) {
    const float want = vanilla * factor;
    const float have = ReadFloat(dvar);
    if (fabsf(have - want) <= 0.001f) return;
    WriteFloat(dvar, want);
    LogF(L"%s %.3f -> %.3f", label, have, want);
}

}  // namespace

bool Resolve() {
    if (!FindDvar("bg_viewKickScale", g_scale)) return false;
    if (!FindDvar("bg_viewKickMin", g_min)) return false;
    if (!FindDvar("bg_viewKickMax", g_max)) return false;
    if (!FindDvar("bg_viewKickRandom", g_random)) return false;

    g_vanilla.scale = ReadFloat(g_scale);
    g_vanilla.minKick = ReadFloat(g_min);
    g_vanilla.maxKick = ReadFloat(g_max);
    g_vanilla.random = ReadFloat(g_random);
    g_resolved = true;

    LogF(L"flinch: %.2f scale, %.1f..%.1f deg, %.2f random",
         g_vanilla.scale, g_vanilla.minKick, g_vanilla.maxKick, g_vanilla.random);
    return true;
}

bool Valid() {
    if (!g_resolved) return false;

    // A level change can rebuild the dvar table under us.
    if (!StillValid(g_scale, "bg_viewKickScale") || !StillValid(g_min, "bg_viewKickMin") ||
        !StillValid(g_max, "bg_viewKickMax") || !StillValid(g_random, "bg_viewKickRandom")) {
        g_resolved = false;
        g_scale = DvarRef{};
        g_min = DvarRef{};
        g_max = DvarRef{};
        g_random = DvarRef{};
        return false;
    }
    return true;
}

void Apply(const ModSettings& settings) {
    if (!Valid()) return;
    const float strength = settings.flinchStrength;
    ScaleFloat(g_scale, g_vanilla.scale, strength, L"bg_viewKickScale");
    ScaleFloat(g_min, g_vanilla.minKick, strength, L"bg_viewKickMin");
    ScaleFloat(g_max, g_vanilla.maxKick, strength, L"bg_viewKickMax");
    ScaleFloat(g_random, g_vanilla.random, strength, L"bg_viewKickRandom");
}

void Restore() {
    if (!Valid()) return;
    ScaleFloat(g_scale, g_vanilla.scale, 1.0f, L"bg_viewKickScale");
    ScaleFloat(g_min, g_vanilla.minKick, 1.0f, L"bg_viewKickMin");
    ScaleFloat(g_max, g_vanilla.maxKick, 1.0f, L"bg_viewKickMax");
    ScaleFloat(g_random, g_vanilla.random, 1.0f, L"bg_viewKickRandom");
}

}  // namespace flinch
