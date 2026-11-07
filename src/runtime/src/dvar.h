#pragma once

#include <cstddef>
#include <cstdint>

struct DvarRef {
    uintptr_t addr = 0;
    uintptr_t namePtr = 0;
    size_t valueOffset = 0;

    bool valid() const { return addr != 0; }
};

// Resolves a dvar by name from the running game's image. Layout is verified at
// runtime rather than hardcoded, because the game binary changes between builds.
bool FindDvar(const char* name, DvarRef& out);

// Extra logging for the scan itself; off unless settings.ini asks for it.
extern bool g_dvarScanVerbose;

// Cheap re-check used every tick: the dvar keeps existing but stops being the
// struct we resolved after a level change tears the frontend state down.
bool StillValid(const DvarRef& dvar, const char* name);

float ReadFloat(const DvarRef& dvar);
void WriteFloat(const DvarRef& dvar, float value);
