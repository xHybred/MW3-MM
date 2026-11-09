#include "weapon.h"

#include "log.h"
#include "settings.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace
{
    // 0x14004E86E0 is indexed by client and holds the WeaponCompleteDef the
    // engine uses for that client: FUN_140022630 reads adsZoomFov at +0x90 from
    // it, and the effect pointers at +0x08 lead to the WeaponDef the renderer
    // uses.
    constexpr size_t kWeaponDefArray = 0x4E86E0;
    // 0x14004E8E60 is a second per-slot table, and this is the object the
    // gameplay code actually consults: the spread routine at 0x14002690A reads
    // +0x58C..+0x5A0 from it, the fire path at 0x1400274C4 reads +0x5A8, and
    // the kick routine at 0x14002B40C reads +0x72C..+0x77C from it. On a level
    // where this entry is the same object as the WeaponDef, writing the def is
    // enough; where the engine keeps a separate copy, the def write alone never
    // reached gameplay (hipfire stayed vanilla until the weapon was re-equipped
    // and the copy was rebuilt). Both targets are written.
    constexpr size_t kWeaponStateArray = 0x4E8E60;
    constexpr size_t kRedirectIndex = 0xC8;
    constexpr size_t kWeaponDefPointer = 0x08;
    // WeaponDef::viewFlashEffect: the first-person muzzle flash, smoke puff
    // included. There is no separate smoke slot in the weapon definition.
    constexpr size_t kFlashEffect = 0x70;
    // The engine asks for entity numbers here, not just the four local clients,
    // so survival bots come through with values well above 3.
    constexpr int kMaxClients = 1024;
    // A kick amplitude is a small fraction of a degree and a spread is a cone
    // in degrees. Anything outside this is not one of our fields.
    constexpr float kPlausibleValue = 100.0f;
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
        // The cone the bullet can land in while aiming (adsSpread) is not
        // written here. Its only reader is the getter at module+0x221F0, and
        // the fire path blends that value on its own, so spread.cpp scales the
        // getter's result instead of the stored float.
        // Added to the cone on each shot, which is the bloom.
        {0x5A8, Group::Bloom},  // fire add
    };
    constexpr size_t kFieldCount = sizeof(kFields) / sizeof(kFields[0]);

    struct DefSlot
    {
        void* def;
        float vanilla[kFieldCount];
        // The state copy does not necessarily share the whole layout with the
        // WeaponDef, so each offset is judged on its own instead of rejecting
        // the object wholesale.
        bool fieldOk[kFieldCount];
        void* vanillaFlash;
        size_t usableFields;
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

    bool PlausibleValue(float value)
    {
        return std::isfinite(value) && std::fabs(value) <= kPlausibleValue;
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

    // Keeps one set of original values per object. Without this, writing a
    // scaled value and later scaling it again would compound every frame.
    DefSlot* SlotFor(void* def, const char* name, const char* label, bool withFlash, bool announce)
    {
        for (size_t i = 0; i < g_defCount; ++i)
        {
            if (g_defs[i].def == def)
                return g_defs[i].usableFields > 0 ? &g_defs[i] : nullptr;
        }

        if (g_defCount >= kDefSlots)
        {
            if (!g_slotsFull)
            {
                g_slotsFull = true;
                LogF(L"weapon: %zu weapon objects tracked, leaving the rest alone", kDefSlots);
            }
            return nullptr;
        }

        DefSlot* slot = &g_defs[g_defCount++];
        slot->def = def;
        slot->usableFields = 0;

        auto* base = static_cast<unsigned char*>(def);
        for (size_t i = 0; i < kFieldCount; ++i)
        {
            const float value = *reinterpret_cast<float*>(base + kFields[i].offset);
            slot->vanilla[i] = value;
            slot->fieldOk[i] = PlausibleValue(value);
            if (slot->fieldOk[i])
                ++slot->usableFields;
        }
        slot->vanillaFlash = *reinterpret_cast<void**>(base + kFlashEffect);

        if (slot->usableFields == 0)
        {
            LogF(L"weapon: %S %S at %p holds nothing we recognise, left alone", name, label, def);
            return nullptr;
        }

        if (announce)
        {
            LogF(L"weapon: %S %S %p kick pitch %.3f..%.3f yaw %.3f..%.3f", name, label, def,
                 Vanilla(*slot, 0x72C), Vanilla(*slot, 0x730), Vanilla(*slot, 0x734),
                 Vanilla(*slot, 0x738));
            LogF(L"weapon:   hip pitch %.3f..%.3f yaw %.3f..%.3f", Vanilla(*slot, 0x770),
                 Vanilla(*slot, 0x774), Vanilla(*slot, 0x778), Vanilla(*slot, 0x77C));
            LogF(L"weapon:   spread stand %.3f..%.3f fire add %.3f aimed %.3f", Vanilla(*slot, 0x58C),
                 Vanilla(*slot, 0x598), Vanilla(*slot, 0x5A8), Vanilla(*slot, 0x764));

            if (withFlash)
            {
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
        }
        return slot;
    }

    struct Strengths
    {
        float kick;
        float accuracy;
        float bloom;
    };

    // Scales the fields this object exposes. Offsets whose original value does
    // not look like one of ours are skipped, so a state copy with a different
    // layout cannot be damaged.
    void ApplyToTarget(void* target, const char* name, const char* label, bool withFlash,
                       const Strengths& strengths, bool logging)
    {
        DefSlot* slot = SlotFor(target, name, label, withFlash, logging);
        if (!slot)
            return;

        auto* base = static_cast<unsigned char*>(target);
        for (size_t i = 0; i < kFieldCount; ++i)
        {
            if (!slot->fieldOk[i])
                continue;

            float strength = strengths.kick;
            if (kFields[i].group == Group::Accuracy)
                strength = strengths.accuracy;
            else if (kFields[i].group == Group::Bloom)
                strength = strengths.bloom;

            const float wanted = slot->vanilla[i] * std::clamp(strength, 0.0f, 1.0f);
            auto* value = reinterpret_cast<float*>(base + kFields[i].offset);
            if (std::fabs(*value - wanted) > 0.0005f)
            {
                if (logging)
                {
                    LogF(L"weapon: %S %S %p +0x%03zX %.3f -> %.3f", name, label, target,
                         kFields[i].offset, *value, wanted);
                }
                *value = wanted;
            }
        }

        // Nulling the effect is the only lever for the muzzle flash: there is no
        // dvar for it and no separate smoke slot to switch independently. Only
        // the WeaponDef carries effect pointers.
        if (withFlash)
        {
            void* wantedFlash = g_flashEnabled.load(std::memory_order_relaxed) ? slot->vanillaFlash : nullptr;
            auto* flashSlot = reinterpret_cast<void**>(base + kFlashEffect);
            if (*flashSlot != wantedFlash)
            {
                if (logging)
                    LogF(L"weapon: %S flash %p -> %p", name, *flashSlot, wantedFlash);
                *flashSlot = wantedFlash;
            }
        }
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
    auto** states = reinterpret_cast<void**>(ImageBase() + kWeaponStateArray);
    void* def = defs[clientNum];
    void* rawDef = def;
    const char* rawName = def ? *reinterpret_cast<char**>(def) : nullptr;
    int redirect = -1;
    int index = clientNum;

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

    // The engine picks the slot like this: a def whose name starts with "iw5_"
    // is its own slot, anything else follows the byte at +0xC8 (see 0x140022630
    // and 0x1400268CA). Single-player names such as "acr" take the redirect path
    // too, and an attachment variant can point at a different slot than the
    // entry the client table handed us - a hybrid optic is exactly that case.
    const int redirectByte =
        *reinterpret_cast<const unsigned char*>(static_cast<const char*>(def) + kRedirectIndex);
    const bool mpName = name != nullptr && std::strncmp(name, "iw5_", 4) == 0;
    if (!mpName && redirectByte > 0 && redirectByte < kMaxClients && defs[redirectByte])
    {
        void* redirected = defs[redirectByte];
        char* redirectedName = *reinterpret_cast<char**>(redirected);
        // Slot 0 is "none" and some entries are placeholders; following those
        // would replace a real weapon with nothing.
        if (IsWeaponName(redirectedName) && std::strcmp(redirectedName, "none") != 0 &&
            std::strcmp(redirectedName, "defaultweapon") != 0)
        {
            redirect = redirectByte;
            index = redirectByte;
            def = redirected;
            name = redirectedName;
        }
    }

    if (!IsWeaponName(name))
        return;

    void* weaponDef = *reinterpret_cast<void* const*>(static_cast<char*>(def) + kWeaponDefPointer);
    if (!weaponDef)
        return;

    const bool enabled = g_enabled.load(std::memory_order_relaxed);
    const bool spreadOn = g_spreadEnabled.load(std::memory_order_relaxed);
    const Strengths strengths{
        enabled ? g_strength.load(std::memory_order_relaxed) : 1.0f,
        spreadOn ? 1.0f - g_spreadAccuracy.load(std::memory_order_relaxed) : 1.0f,
        spreadOn ? 1.0f - g_spreadBloom.load(std::memory_order_relaxed) : 1.0f,
    };

    ApplyToTarget(weaponDef, name, "def", true, strengths, logging);

    // The gameplay readers use the state table, which may be a separate copy.
    void* state = states[index];
    if (state && state != weaponDef)
        ApplyToTarget(state, name, "state", false, strengths, logging);

    // What the engine holds for this slot right now. An attachment or optic that
    // resolves somewhere else shows up as a different pointer here, so a report
    // like "the hybrid sight ignores the spread" can be read instead of guessed.
    if (logging)
    {
        static ULONGLONG nextWatch = 0;
        const ULONGLONG now = GetTickCount64();
        if (now >= nextWatch)
        {
            nextWatch = now + 2000;
            void* target = state ? state : weaponDef;
            const auto read = [target](size_t offset)
            {
                float value = 0.0f;
                std::memcpy(&value, static_cast<const char*>(target) + offset, sizeof(value));
                return value;
            };
            LogF(L"weapon: watch raw %p %S byte %d redirect %d slot %d def %p %S state %p stand %.2f/%.2f fire add %.2f aimed %.2f kick %.2f/%.2f",
                 rawDef, rawName ? rawName : "(null)", redirectByte, redirect, index, weaponDef, name, state,
                 read(0x58C), read(0x598), read(0x5A8), read(0x764), read(0x72C), read(0x770));
        }
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
