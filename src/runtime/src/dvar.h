#pragma once

#include <cstddef>
#include <cstdint>

struct DvarRef {
    uintptr_t addr = 0;
    uintptr_t namePtr = 0;
    size_t valueOffset = 0;
    unsigned char type = 0;  // dvar type tag: 0 bool, 1 float, 5 int, 6 enum

    bool valid() const { return addr != 0; }
};

// Resolves a float dvar by name from the running game's image. Layout is
// verified at runtime rather than hardcoded, because the game binary changes
// between builds.
bool FindDvar(const char* name, DvarRef& out);

// Same, for a dvar of any type tag. Flinch mixes a float and an int.
bool FindDvarTyped(const char* name, unsigned char type, DvarRef& out);

// Extra logging for the scan itself; off unless settings.ini asks for it.
extern bool g_dvarScanVerbose;

// Cheap re-check used every tick: the dvar keeps existing but stops being the
// struct we resolved after a level change tears the frontend state down.
bool StillValid(const DvarRef& dvar, const char* name);

float ReadFloat(const DvarRef& dvar);
void WriteFloat(const DvarRef& dvar, float value);
int ReadInt(const DvarRef& dvar);
void WriteInt(const DvarRef& dvar, int value);
