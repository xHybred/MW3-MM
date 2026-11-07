"""List every dvar the running game has registered.

Usage: python tools/list_dvars.py [keyword ...]

A dvar_t begins with a pointer to its name. Scanning the module image for
eight-byte values that point at an identifier string, then checking the type
byte and the rest of the layout, finds the whole table at once instead of one
name at a time. Output is name, address, type and current value, filtered by
keyword when any are given.
"""

import ctypes
import ctypes.wintypes as wt
import re
import struct
import sys

from probe_dvar import find_pid, kernel32, read

TYPES = {0: "bool", 1: "float", 2: "vec2", 3: "vec3", 4: "vec4",
         5: "int", 6: "enum", 7: "string", 8: "color", 9: "vec3color"}

IDENTIFIER = re.compile(rb"[a-z][a-z0-9_]{2,40}\x00")


class MODULEINFO(ctypes.Structure):
    _fields_ = [("lpBaseOfDll", ctypes.c_void_p),
                ("SizeOfImage", wt.DWORD),
                ("EntryPoint", ctypes.c_void_p)]


class MEMORY_BASIC_INFORMATION64(ctypes.Structure):
    _fields_ = [
        ("BaseAddress", ctypes.c_ulonglong),
        ("AllocationBase", ctypes.c_ulonglong),
        ("AllocationProtect", wt.DWORD),
        ("__alignment1", wt.DWORD),
        ("RegionSize", ctypes.c_ulonglong),
        ("State", wt.DWORD),
        ("Protect", wt.DWORD),
        ("Type", wt.DWORD),
        ("__alignment2", wt.DWORD),
    ]


WRITABLE = 0x04 | 0x08 | 0x40 | 0x80


def writable_pages(process, base, size):
    """The set of 64 KB pages in the image we are allowed to write to.

    Dvars always live in writable data; the same layout appearing in .rdata is
    a float table that happens to start with something that looks like a name
    pointer.
    """
    pages = set()
    address = base
    end = base + size
    mbi = MEMORY_BASIC_INFORMATION64()
    while address < end:
        if kernel32.VirtualQueryEx(process, ctypes.c_void_p(address),
                                   ctypes.byref(mbi), ctypes.sizeof(mbi)) == 0:
            break
        if mbi.RegionSize == 0:
            break
        if mbi.State == 0x1000 and (mbi.Protect & WRITABLE) \
                and not (mbi.Protect & 0x100):
            first = max(address, base) // 0x10000
            last = min(address + mbi.RegionSize, end) // 0x10000
            pages.update(range(first, last + 1))
        address = mbi.BaseAddress + mbi.RegionSize
    return pages



def main():
    keywords = [k.lower() for k in sys.argv[1:]]
    pid = find_pid()
    if not pid:
        print("iw5sp.exe is not running")
        return 1
    process = kernel32.OpenProcess(0x0400 | 0x0010, False, pid)
    if not process:
        print("OpenProcess failed", ctypes.get_last_error())
        return 1

    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    module = MODULEINFO()
    if not psapi.GetModuleInformation(process, ctypes.c_void_p(0x140000000),
                                      ctypes.byref(module), ctypes.sizeof(module)):
        print("GetModuleInformation failed")
        return 1
    base = module.lpBaseOfDll or 0x140000000
    size = module.SizeOfImage

    image = bytearray(size)
    for offset in range(0, size, 0x100000):
        chunk = read(process, base + offset, min(0x100000, size - offset))
        image[offset:offset + len(chunk)] = chunk

    names = {m.start(): m.group()[:-1].decode() for m in IDENTIFIER.finditer(image)}
    writable = writable_pages(process, base, size)

    found = {}
    for offset in range(0, size - 0x60, 8):
        target = struct.unpack_from("<Q", image, offset)[0]
        if not target:
            continue
        relative = target - base
        if relative not in names:
            continue
        if (base + offset) // 0x10000 not in writable:
            continue
        kind = image[offset + 0x0C]
        if kind not in TYPES:
            continue
        if struct.unpack_from("<Q", image, offset)[0] != target:
            continue
        name = names[relative]

        # Several structures share the first eight bytes with a dvar. Score the
        # fields that a dvar always keeps sane and let the best one win.
        score = 0
        if kind in (0, 5, 6):
            value = struct.unpack_from("<i", image, offset + 0x10)[0]
            reset = struct.unpack_from("<i", image, offset + 0x30)[0]
            if 0 <= value <= 4096:
                score += 2
            if 0 <= reset <= 4096:
                score += 2
            if reset == value:
                score += 1
        elif kind == 1:
            value = struct.unpack_from("<f", image, offset + 0x10)[0]
            reset = struct.unpack_from("<f", image, offset + 0x30)[0]
            for candidate in (value, reset):
                if candidate == candidate and abs(candidate) < 1e6:
                    score += 2
            if value == reset:
                score += 1
        else:
            value = image[offset + 0x10:offset + 0x20].hex()

        if name not in found or score > found[name][0]:
            found[name] = (score, base + offset, kind, value)

    selected = sorted((n, v) for n, v in found.items()
                      if not keywords or any(k in n.lower() for k in keywords))
    print(f"{len(found)} dvars, {len(selected)} shown")
    for name, (score, address, kind, value) in selected:
        shown = f"{value:g}" if isinstance(value, float) else str(value)
        print(f"  {name:38} {address:#014x} {TYPES[kind]:9} {shown}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
