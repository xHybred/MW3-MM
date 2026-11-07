#include "dvar.h"

#include "log.h"

#include <windows.h>
#include <cmath>
#include <cstring>
#include <vector>

namespace {

struct Range {
    uintptr_t begin;
    size_t size;
};

bool ImageRange(uintptr_t& base, size_t& size) {
    HMODULE module = GetModuleHandleW(nullptr);
    if (!module) return false;
    base = reinterpret_cast<uintptr_t>(module);

    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return false;

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
    if (nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64) return false;

    size = nt->OptionalHeader.SizeOfImage;
    return true;
}

// The image's virtual size is much larger than its raw size (Steam's DRM stub
// reserves a big zeroed .data), so walk the committed regions instead of
// assuming everything from base to base+SizeOfImage is readable.
void CollectReadableRanges(std::vector<Range>& out, uintptr_t base, size_t size) {
    const uintptr_t end = base + size;
    uintptr_t cursor = base;
    while (cursor < end) {
        MEMORY_BASIC_INFORMATION mbi{};
        if (VirtualQuery(reinterpret_cast<LPCVOID>(cursor), &mbi, sizeof(mbi)) == 0) break;

        const uintptr_t regionBase = reinterpret_cast<uintptr_t>(mbi.BaseAddress);
        const size_t regionSize = mbi.RegionSize;
        if (regionSize == 0) break;

        const DWORD readable = PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY |
                               PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY;
        const bool usable = mbi.State == MEM_COMMIT &&
                            (mbi.Protect & readable) != 0 &&
                            (mbi.Protect & PAGE_GUARD) == 0 &&
                            (mbi.Protect & PAGE_NOACCESS) == 0;
        if (usable) {
            const uintptr_t begin = regionBase > base ? regionBase : base;
            const uintptr_t stop = (regionBase + regionSize) < end ? (regionBase + regionSize) : end;
            if (stop > begin) out.push_back({begin, static_cast<size_t>(stop - begin)});
        }

        cursor = regionBase + regionSize;
    }
}

uintptr_t FindNameString(const char* name, const std::vector<Range>& ranges) {
    const size_t len = strlen(name);
    for (const Range& range : ranges) {
        const auto* data = reinterpret_cast<const unsigned char*>(range.begin);
        if (range.size < len + 1) continue;

        size_t offset = 0;
        while (offset + len < range.size) {
            const void* hit = memchr(data + offset, name[0], range.size - offset - len);
            if (!hit) break;
            const size_t index = static_cast<size_t>(static_cast<const unsigned char*>(hit) - data);
            if (memcmp(data + index, name, len) == 0 && data[index + len] == 0) {
                return range.begin + index;
            }
            offset = index + 1;
        }
    }
    return 0;
}

bool SafeStrEq(const char* left, const char* right) {
    __try {
        return strcmp(left, right) == 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeReadU8(uintptr_t address, unsigned char& out) {
    __try {
        out = *reinterpret_cast<const unsigned char*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeReadPtr(uintptr_t address, uintptr_t& out) {
    __try {
        out = *reinterpret_cast<const uintptr_t*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

bool SafeReadF32(uintptr_t address, float& out) {
    __try {
        out = *reinterpret_cast<const float*>(address);
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
}

// cg_fovScale's vanilla value is exactly 1.0, so the band has to include it and
// anything a player could reasonably scale to.
bool PlausibleFov(float value) {
    return value >= 0.05f && value <= 1000.0f;
}

// dvar_t starts with a pointer to the dvar's name. Walk the image for a pointer
// to the string we found, then check that the pointed-to struct actually looks
// like a float dvar and not some unrelated string reference.
bool ValidateCandidate(uintptr_t address, const char* name, float expectedReset, size_t& valueOffset,
                       uintptr_t& namePtr) {
    unsigned char type = 0;
    if (!SafeReadU8(address + 0x0C, type) || type != 1) return false;

    float current = 0.0f;
    if (!SafeReadF32(address + 0x10, current) || !PlausibleFov(current)) return false;

    float reset = 0.0f;
    if (!SafeReadF32(address + 0x30, reset)) return false;

    const char* candidateName = nullptr;
    __try {
        candidateName = *reinterpret_cast<const char* const*>(address);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        return false;
    }
    if (!candidateName || !SafeStrEq(candidateName, name)) return false;

    namePtr = reinterpret_cast<uintptr_t>(candidateName);
    valueOffset = 0x10;

    // If the compiled default is present at the documented reset slot, the
    // layout is the one we expect. Otherwise derive the value offset from it.
    if (fabsf(reset - expectedReset) > 0.001f) {
        for (size_t offset = 0x18; offset + 4 <= 0x60; offset += 4) {
            float probe = 0.0f;
            if (!SafeReadF32(address + offset, probe)) continue;
            if (fabsf(probe - expectedReset) <= 0.001f && offset >= 0x20) {
                valueOffset = offset - 0x20;
                return true;
            }
        }
        return true;
    }
    return true;
}

}  // namespace

bool g_dvarScanVerbose = false;

bool FindDvar(const char* name, DvarRef& out) {
    uintptr_t base = 0;
    size_t size = 0;
    if (!ImageRange(base, size)) {
        if (g_dvarScanVerbose) LogF(L"%S: no image range", name);
        return false;
    }

    std::vector<Range> ranges;
    CollectReadableRanges(ranges, base, size);

    size_t total = 0;
    for (const Range& range : ranges) total += range.size;
    if (g_dvarScanVerbose) {
        LogF(L"%S: image %p size 0x%zx, %zu ranges, %zu bytes", name,
             reinterpret_cast<void*>(base), size, ranges.size(), total);
    }

    const uintptr_t nameAddress = FindNameString(name, ranges);
    if (nameAddress == 0) {
        if (g_dvarScanVerbose) LogF(L"%S: name string not found in image", name);
        return false;
    }
    if (g_dvarScanVerbose) LogF(L"%S: name string at %p", name, reinterpret_cast<void*>(nameAddress));

    const float expectedReset = strcmp(name, "cg_fov") == 0 ? 65.0f : 1.0f;

    int hits = 0;
    for (const Range& range : ranges) {
        for (size_t offset = 0; offset + sizeof(uintptr_t) <= range.size; offset += sizeof(uintptr_t)) {
            uintptr_t candidate = 0;
            if (!SafeReadPtr(range.begin + offset, candidate)) continue;
            if (candidate != nameAddress) continue;
            if (range.begin + offset < 0x10000) continue;
            ++hits;

            size_t valueOffset = 0;
            uintptr_t namePtr = 0;
            const uintptr_t address = range.begin + offset;
            if (ValidateCandidate(address, name, expectedReset, valueOffset, namePtr)) {
                out.addr = address;
                out.namePtr = namePtr;
                out.valueOffset = valueOffset;
                if (g_dvarScanVerbose) LogF(L"%S: dvar at %p (+0x%zx)", name, reinterpret_cast<void*>(address), valueOffset);
                return true;
            }
            if (g_dvarScanVerbose) LogF(L"%S: rejected pointer hit at %p", name, reinterpret_cast<void*>(address));
        }
    }
    if (g_dvarScanVerbose) LogF(L"%S: %d pointer hit(s), none valid", name, hits);
    return false;
}

bool StillValid(const DvarRef& dvar, const char* name) {
    if (!dvar.valid()) return false;

    unsigned char type = 0;
    if (!SafeReadU8(dvar.addr + 0x0C, type) || type != 1) return false;

    uintptr_t namePtr = 0;
    if (!SafeReadPtr(dvar.addr, namePtr) || namePtr != dvar.namePtr) return false;

    return SafeStrEq(reinterpret_cast<const char*>(namePtr), name);
}

float ReadFloat(const DvarRef& dvar) {
    float value = 0.0f;
    if (!dvar.valid()) return value;
    SafeReadF32(dvar.addr + dvar.valueOffset, value);
    return value;
}

void WriteFloat(const DvarRef& dvar, float value) {
    if (!dvar.valid()) return;
    __try {
        *reinterpret_cast<float*>(dvar.addr + dvar.valueOffset) = value;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogF(L"write fault at %p", reinterpret_cast<void*>(dvar.addr));
    }
}
