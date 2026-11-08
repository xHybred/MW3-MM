#include "weapon.h"

#include "log.h"
#include "settings.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace
{
    // 0x14004E86E0 is indexed by client and holds the WeaponCompleteDef the
    // engine uses for that client: FUN_140022630 reads adsZoomFov at +0x90 from
    // it. The recoil, spread and effect data live in the WeaponDef that sits
    // behind +0x08.
    constexpr size_t kWeaponDefArray = 0x4E86E0;
    constexpr size_t kRedirectIndex = 0xC8;
    constexpr size_t kWeaponDefPointer = 0x08;
    // WeaponDef::viewFlashEffect: the first-person muzzle flash, smoke puff
    // included. There is no separate smoke slot in the weapon definition.
    constexpr size_t kFlashEffect = 0x70;
    // The engine asks for entity numbers here, not just the four local clients,
    // so survival bots come through with values well above 3.
    constexpr int kMaxClients = 1024;
    // A kick amplitude is a small fraction of a degree and a spread is a cone
    // in degrees. Anything outside this is not a weapon def.
    constexpr float kPlausibleKick = 100.0f;
    constexpr size_t kDefSlots = 64;

    enum class Group
    {
        Kick,
        Accuracy,
        Bloom,
    };

    struct Field
    {
        size_t offset;
        Group group;
    };

    constexpr Field kFields[] = {
        // Amplitudes only. Accel, speed and decay stay native so the kick keeps
        // its original timing and only travels less far.
        {0x72C, Group::Kick},  // ads pitch min
        {0x730, Group::Kick},  // ads pitch max
        {0x734, Group::Kick},  // ads yaw min
        {0x738, Group::Kick},  // ads yaw max
        {0x770, Group::Kick},  // hip pitch min
        {0x774, Group::Kick},  // hip pitch max
        {0x778, Group::Kick},  // hip yaw min
        {0x77C, Group::Kick},  // hip yaw max
        // The resting cone for each stance. The spread routine lerps between
        // the min and the max, so scaling both to zero leaves nothing for it to
        // randomise and every hipfire bullet follows the crosshair.
        {0x58C, Group::Accuracy},  // stand min
        {0x590, Group::Accuracy},  // ducked min
        {0x594, Group::Accuracy},  // prone min
        {0x598, Group::Accuracy},  // stand max
        {0x59C, Group::Accuracy},  // ducked max
        {0x5A0, Group::Accuracy},  // prone max
        // Added to the cone on each shot, which is the bloom.
        {0x5A8, Group::Bloom},  // fire add
    };
    constexpr size_t kFieldCount = sizeof(kFields) / sizeof(kFields[0]);

    struct DefSlot
    {
        void* def;
        float vanilla[kFieldCount];
        void* vanillaFlash;
        bool usable;
    };

    DefSlot g_defs[kDefSlots];
    size_t g_defCount = 0;
    bool g_slotsFull = false;

    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_strength{1.0f};
    std::atomic<bool> g_flashEnabled{true};
    std::atomic<bool> g_spreadEnabled{true};
    std::atomic<float> g_spreadAccuracy{0.0f};
    std::atomic<float> g_spreadBloom{0.0f};
    std::atomic<bool> g_log{false};
    std::atomic<bool> g_reportedClient{false};
    bool g_seenClient[kMaxClients] = {};

    uintptr_t ImageBase()
    {
        return reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    }

    bool Plausible(const float* values)
    {
        for (size_t i = 0; i < kFieldCount; ++i)
        {
            if (!std::isfinite(values[i]) || std::fabs(values[i]) > kPlausibleKick)
                return false;
        }
        return true;
    }

    float Vanilla(const DefSlot& slot, size_t offset)
    {
        for (size_t i = 0; i < kFieldCount; ++i)
        {
            if (kFields[i].offset == offset)
                return slot.vanilla[i];
        }
        return 0.0f;
    }

    // Weapon names are either the asset name ("acr", "mp412") or the
    // multiplayer style ("iw5_fnfiveseven_mp"), so match the shape rather than
    // a prefix: something printable and reasonably short.
    bool IsWeaponName(const char* name)
    {
        if (!name || !name[0])
            return false;

        for (int i = 0; i < 64; ++i)
        {
            const char c = name[i];
            if (!c)
                return i > 0;
            const bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                            (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
            if (!ok)
                return false;
        }
        return false;
    }

    // Keeps one set of original values per weapon. Without this, writing a
    // scaled value and later scaling it again would compound every frame.
    DefSlot* SlotFor(void* def, const char* name, bool announce)
    {
        for (size_t i = 0; i < g_defCount; ++i)
        {
            if (g_defs[i].def == def)
                return g_defs[i].usable ? &g_defs[i] : nullptr;
        }

        if (g_defCount >= kDefSlots)
        {
            if (!g_slotsFull)
            {
                g_slotsFull = true;
                LogF(L"weapon: %zu weapon defs tracked, leaving the rest alone", kDefSlots);
            }
            return nullptr;
        }

        DefSlot* slot = &g_defs[g_defCount++];
        slot->def = def;

        auto* base = static_cast<unsigned char*>(def);
        for (size_t i = 0; i < kFieldCount; ++i)
            slot->vanilla[i] = *reinterpret_cast<float*>(base + kFields[i].offset);
        slot->vanillaFlash = *reinterpret_cast<void**>(base + kFlashEffect);

        slot->usable = Plausible(slot->vanilla);
        if (!slot->usable)
        {
            LogF(L"weapon: %S at %p does not look like a weapon def, left alone", name, def);
            return nullptr;
        }

        if (announce)
        {
            LogF(L"weapon: %S def %p kick pitch %.3f..%.3f yaw %.3f..%.3f", name, def,
                 Vanilla(*slot, 0x72C), Vanilla(*slot, 0x730), Vanilla(*slot, 0x734), Vanilla(*slot, 0x738));
            LogF(L"weapon:   hip pitch %.3f..%.3f yaw %.3f..%.3f", Vanilla(*slot, 0x770),
                 Vanilla(*slot, 0x774), Vanilla(*slot, 0x778), Vanilla(*slot, 0x77C));
            LogF(L"weapon:   spread stand %.3f..%.3f fire add %.3f", Vanilla(*slot, 0x58C),
                 Vanilla(*slot, 0x598), Vanilla(*slot, 0x5A8));

            auto* fx = static_cast<unsigned char*>(slot->vanillaFlash);
            if (fx)
            {
                const char* fxName = *reinterpret_cast<char**>(fx);
                LogF(L"weapon:   flash %p %S elems %d/%d/%d", slot->vanillaFlash,
                     fxName ? fxName : "?", *reinterpret_cast<int*>(fx + 0x14),
                     *reinterpret_cast<int*>(fx + 0x18), *reinterpret_cast<int*>(fx + 0x1C));
            }
            else
            {
                LogF(L"weapon:   flash none");
            }
        }
        return slot;
    }
}

void weapon::ApplyFromClient(int clientNum)
{
    const bool logging = g_log.load(std::memory_order_relaxed);

    if (clientNum < 0 || clientNum >= kMaxClients)
    {
        if (logging && !g_reportedClient.exchange(true))
            LogF(L"weapon: engine asked for %d, outside the table", clientNum);
        return;
    }

    auto** defs = reinterpret_cast<void**>(ImageBase() + kWeaponDefArray);
    void* def = defs[clientNum];

    // Which clients the engine actually asks about, and what each slot holds.
    // This is the only way to see why a weapon's fields are never touched.
    if (logging && !g_seenClient[clientNum])
    {
        g_seenClient[clientNum] = true;
        LogF(L"weapon: client %d def %p", clientNum, def);
    }

    if (!def)
        return;

    auto* name = *reinterpret_cast<char**>(def);

    // Clients that hold a redirect entry point at another slot; the engine
    // follows the byte at +0xC8 to the real def. Anything that is still not a
    // weapon after that is not ours to touch.
    if (!IsWeaponName(name))
    {
        const auto redirect =
            *reinterpret_cast<const unsigned char*>(static_cast<const char*>(def) + kRedirectIndex);
        if (redirect >= kMaxClients || !defs[redirect])
            return;
        def = defs[redirect];
        name = *reinterpret_cast<char**>(def);
        if (!IsWeaponName(name))
            return;
    }

    void* weaponDef = *reinterpret_cast<void* const*>(static_cast<char*>(def) + kWeaponDefPointer);
    if (!weaponDef)
        return;

    DefSlot* slot = SlotFor(weaponDef, name, logging);
    if (!slot)
        return;

    const float kickStrength =
        g_enabled.load(std::memory_order_relaxed) ? g_strength.load(std::memory_order_relaxed) : 1.0f;
    const bool spreadOn = g_spreadEnabled.load(std::memory_order_relaxed);
    const float accuracyStrength =
        spreadOn ? 1.0f - g_spreadAccuracy.load(std::memory_order_relaxed) : 1.0f;
    const float bloomStrength =
        spreadOn ? 1.0f - g_spreadBloom.load(std::memory_order_relaxed) : 1.0f;

    auto* base = static_cast<unsigned char*>(weaponDef);
    for (size_t i = 0; i < kFieldCount; ++i)
    {
        float strength = kickStrength;
        if (kFields[i].group == Group::Accuracy)
            strength = accuracyStrength;
        else if (kFields[i].group == Group::Bloom)
            strength = bloomStrength;

        const float wanted = slot->vanilla[i] * std::clamp(strength, 0.0f, 1.0f);
        auto* value = reinterpret_cast<float*>(base + kFields[i].offset);
        if (std::fabs(*value - wanted) > 0.0005f)
        {
            if (logging)
            {
                LogF(L"weapon: %S %p +0x%03zX %.3f -> %.3f", name, weaponDef, kFields[i].offset, *value,
                     wanted);
            }
            *value = wanted;
        }
    }

    // Nulling the effect is the only lever for the muzzle flash: there is no
    // dvar for it and no separate smoke slot to switch independently.
    void* wantedFlash = g_flashEnabled.load(std::memory_order_relaxed) ? slot->vanillaFlash : nullptr;
    auto* flashSlot = reinterpret_cast<void**>(base + kFlashEffect);
    if (*flashSlot != wantedFlash)
    {
        if (logging)
            LogF(L"weapon: %S flash %p -> %p", name, *flashSlot, wantedFlash);
        *flashSlot = wantedFlash;
    }
}

void weapon::Update(const ModSettings& settings)
{
    g_enabled.store(settings.recoilEnabled, std::memory_order_relaxed);
    g_strength.store(settings.recoilStrength, std::memory_order_relaxed);
    g_flashEnabled.store(settings.flashEnabled, std::memory_order_relaxed);
    g_spreadEnabled.store(settings.spreadEnabled, std::memory_order_relaxed);
    g_spreadAccuracy.store(settings.spreadAccuracy, std::memory_order_relaxed);
    g_spreadBloom.store(settings.spreadBloom, std::memory_order_relaxed);
    g_log.store(settings.recoilDebug || settings.spreadDebug, std::memory_order_relaxed);
}
