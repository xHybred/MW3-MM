"""Find dvars in the running game and print their fields.

Usage: python tools/probe_dvar.py cg_fov bg_viewKickScale
       python tools/probe_dvar.py --set bg_viewKickScale=0.5

A dvar_t is 0x60 bytes: name pointer, flags, type, current value, latched,
reset. The candidate is only accepted when the name pointer it holds points
back at the string we searched for, so a chance pointer to a copy of the name
somewhere else cannot be mistaken for a dvar.
"""

import ctypes
import ctypes.wintypes as wt
import struct
import subprocess
import sys

PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
PROCESS_VM_WRITE = 0x0020
PROCESS_VM_OPERATION = 0x0008
MEM_COMMIT = 0x1000
PAGE_GUARD = 0x100
PAGE_NOACCESS = 0x01
READABLE = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80

TYPES = {0: "bool", 1: "float", 2: "vec2", 3: "vec3", 4: "vec4",
         5: "int", 6: "enum", 7: "string", 8: "color", 9: "vec3color"}

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)


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


def find_pid(name="iw5sp.exe"):
    out = subprocess.check_output(
        ["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"], text=True)
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def regions(process):
    found = []
    address = 0x10000
    mbi = MEMORY_BASIC_INFORMATION64()
    while address < 0x7FFFFFFEFFFF:
        if kernel32.VirtualQueryEx(process, ctypes.c_void_p(address),
                                   ctypes.byref(mbi), ctypes.sizeof(mbi)) == 0:
            break
        if mbi.RegionSize == 0:
            break
        if (mbi.State == MEM_COMMIT and (mbi.Protect & READABLE)
                and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS))
                and mbi.Type in (0x20000, 0x1000000)):
            found.append((mbi.BaseAddress, mbi.RegionSize))
        address = mbi.BaseAddress + mbi.RegionSize
    return found


def read(process, address, size):
    buffer = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t()
    if not kernel32.ReadProcessMemory(process, ctypes.c_void_p(address), buffer,
                                      size, ctypes.byref(got)):
        return b""
    return buffer.raw[: got.value]


def write(process, address, data):
    done = ctypes.c_size_t()
    return bool(kernel32.WriteProcessMemory(process, ctypes.c_void_p(address), data,
                                            len(data), ctypes.byref(done)))


def find(process, snapshot, name):
    needle = name.encode() + b"\0"
    addresses = []
    for start, data in snapshot:
        index = data.find(needle)
        while index >= 0:
            addresses.append(start + index)
            index = data.find(needle, index + 1)
    hits = []
    for string_address in addresses:
        pattern = struct.pack("<Q", string_address)
        for start, data in snapshot:
            position = data.find(pattern)
            while position >= 0:
                if position % 8 == 0:
                    candidate = start + position
                    block = read(process, candidate, 0x60)
                    if (len(block) == 0x60 and block[0x0C] in TYPES
                            and struct.unpack_from("<Q", block, 0x00)[0] == string_address):
                        hits.append(candidate)
                position = data.find(pattern, position + 1)
    return hits


def describe(process, address, name):
    block = read(process, address, 0x60)
    flags = struct.unpack_from("<Q", block, 0x08)[0]
    kind = block[0x0C]
    raw = block[0x10:0x20]
    if kind in (0, 5, 6):
        current, latched = struct.unpack_from("<ii", raw, 0)
        reset = struct.unpack_from("<i", block, 0x30)[0]
        shown = f"{current} (latched {latched}, reset {reset})"
    else:
        current = struct.unpack_from("<f", raw, 0)[0]
        latched = struct.unpack_from("<f", block, 0x20)[0]
        reset = struct.unpack_from("<f", block, 0x30)[0]
        shown = f"{current:g} (latched {latched:g}, reset {reset:g})"
    print(f"{name:34} {address:#014x} flags {flags:#06x} {TYPES[kind]:9} {shown}")


def main():
    args = sys.argv[1:]
    setter = None
    if args and args[0] == "--set":
        name, _, value = args[1].partition("=")
        setter = (name, value)
        args = [name]

    pid = find_pid()
    if not pid:
        print("iw5sp.exe is not running")
        return 1
    access = (PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE
              | PROCESS_VM_OPERATION)
    process = kernel32.OpenProcess(access, False, pid)
    if not process:
        print("OpenProcess failed", ctypes.get_last_error())
        return 1

    snapshot = [(start, read(process, start, length)) for start, length in regions(process)]
    for name in args:
        hits = find(process, snapshot, name)
        if not hits:
            print(f"{name:34} not registered")
            continue
        for address in hits:
            describe(process, address, name)
            if setter:
                block = read(process, address, 0x60)
                kind = block[0x0C]
                target = float(setter[1])
                payload = (struct.pack("<i", int(target)) if kind in (0, 5, 6)
                           else struct.pack("<f", target))
                write(process, address + 0x10, payload)
                write(process, address + 0x20, payload)
                print(f"{'':34} set to {target:g}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
