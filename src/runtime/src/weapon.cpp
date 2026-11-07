#include "weapon.h"

#include "log.h"
#include "settings.h"

#include <windows.h>

#include <atomic>
#include <cmath>
#include <cstdint>

namespace
{
    // 0x14004E86E0 is indexed by client and holds the WeaponCompleteDef the
    // engine uses for that client: FUN_140022630 reads adsZoomFov at +0x90 from
    // it. The recoil numbers live in the WeaponDef that sits behind +0x08.
    constexpr size_t kWeaponDefArray = 0x4E86E0;
    constexpr size_t kRedirectIndex = 0xC8;
    constexpr size_t kWeaponDefPointer = 0x08;
    // The engine asks for entity numbers here, not just the four local clients,
    // so survival bots come through with values well above 3.
    constexpr int kMaxClients = 1024;
    // A kick amplitude is a small fraction of a degree. Anything outside this is
    // not a weapon def and must not be written to.
    constexpr float kPlausibleKick = 100.0f;

    struct Field
    {
        size_t offset;
    };

    // Amplitudes only. Accel, speed and decay stay native so the kick keeps its
    // original timing and only travels less far.
    constexpr Field kFields[] = {
        {0x72C},  // ads pitch min
        {0x730},  // ads pitch max
        {0x734},  // ads yaw min
        {0x738},  // ads yaw max
        {0x770},  // hip pitch min
        {0x774},  // hip pitch max
        {0x778},  // hip yaw min
        {0x77C},  // hip yaw max
    };
    constexpr size_t kFieldCount = sizeof(kFields) / sizeof(kFields[0]);
    constexpr size_t kDefSlots = 64;

    struct DefSlot
    {
        void* def;
        float vanilla[kFieldCount];
        bool usable;
    };

    DefSlot g_defs[kDefSlots];
    size_t g_defCount = 0;
    bool g_slotsFull = false;

    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_strength{1.0f};
    std::atomic<bool> g_log{false};
    std::atomic<bool> g_reportedClient{false};

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
        slot->usable = Plausible(slot->vanilla);

        if (!slot->usable)
        {
            LogF(L"weapon: %S at %p does not look like a weapon def, left alone", name, def);
            return nullptr;
        }

        if (announce)
        {
            LogF(L"weapon: %S def %p kick pitch %.3f..%.3f yaw %.3f..%.3f", name, def,
                 slot->vanilla[0], slot->vanilla[1], slot->vanilla[2], slot->vanilla[3]);
            LogF(L"weapon:   hip pitch %.3f..%.3f yaw %.3f..%.3f", slot->vanilla[4],
                 slot->vanilla[5], slot->vanilla[6], slot->vanilla[7]);
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

    if (!def)
        return;

    // Some clients point at a redirect entry; the engine follows the byte at
    // +0xC8 to the real def. Same check, same order.
    const auto* name = *reinterpret_cast<const char**>(def);
    if (name && !(name[0] == 'i' && name[1] == 'w' && name[2] == '5' && name[3] == '_'))
    {
        const unsigned char redirect =
            *reinterpret_cast<const unsigned char*>(static_cast<const char*>(def) + kRedirectIndex);
        if (redirect < kMaxClients && defs[redirect])
        {
            def = defs[redirect];
            name = *reinterpret_cast<const char**>(def);
        }
    }

    void* weaponDef = *reinterpret_cast<void* const*>(static_cast<char*>(def) + kWeaponDefPointer);
    if (!weaponDef)
        return;

    DefSlot* slot = SlotFor(weaponDef, name ? name : "?", logging);
    if (!slot)
        return;

    const float strength = g_enabled.load(std::memory_order_relaxed)
                               ? g_strength.load(std::memory_order_relaxed)
                               : 1.0f;

    auto* base = static_cast<unsigned char*>(weaponDef);
    for (size_t i = 0; i < kFieldCount; ++i)
    {
        const float wanted = slot->vanilla[i] * strength;
        auto* value = reinterpret_cast<float*>(base + kFields[i].offset);
        if (std::fabs(*value - wanted) > 0.0005f)
        {
            if (g_log.load(std::memory_order_relaxed))
                LogF(L"weapon: %S +0x%03zX %.3f -> %.3f", name ? name : "?", kFields[i].offset,
                     *value, wanted);
            *value = wanted;
        }
    }
}

void weapon::Update(const ModSettings& settings)
{
    g_enabled.store(settings.recoilEnabled, std::memory_order_relaxed);
    g_strength.store(settings.recoilStrength, std::memory_order_relaxed);
    g_log.store(settings.recoilDebug, std::memory_order_relaxed);
}
