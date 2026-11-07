"""Inspect a running iw5sp process for dvar structs.

Development aid: reads the game's memory the same way the runtime does, but
prints what it finds so layout assumptions can be checked against the live
binary.

Usage: python tools/inspect.py [dvar_name ...]
"""

import ctypes
import ctypes.wintypes as wt
import struct
import sys

PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
MEM_COMMIT = 0x1000
PAGE_GUARD = 0x100
PAGE_NOACCESS = 0x01
READABLE = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)


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


class MODULEINFO(ctypes.Structure):
    _fields_ = [
        ("lpBaseOfDll", ctypes.c_void_p),
        ("SizeOfImage", wt.DWORD),
        ("EntryPoint", ctypes.c_void_p),
    ]


def find_pid(name="iw5sp.exe"):
    import subprocess

    out = subprocess.check_output(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"], text=True)
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def main():
    pid = find_pid()
    if not pid:
        print("iw5sp.exe is not running")
        return 1

    process = kernel32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not process:
        print("OpenProcess failed", ctypes.get_last_error())
        return 1

    base, size = module_info(process)
    print(f"pid={pid} base=0x{base:x} size=0x{size:x}")

    names = sys.argv[1:] or ["cg_fov", "cg_fovScale", "cg_fovMin"]
    snapshot = [(start, read(process, start, length)) for start, length in all_readable_regions(process)]
    snapshot = [(start, data) for start, data in snapshot if data]
    print(f"snapshot: {len(snapshot)} regions, {sum(len(d) for _, d in snapshot) / 2**20:.0f} MiB")

    for name in names:
        needle = name.encode() + b"\0"
        hits = []
        for start, data in snapshot:
            index = 0
            while True:
                index = data.find(needle, index)
                if index < 0:
                    break
                hits.append(start + index)
                index += 1
        print(f"\n{name}: {len(hits)} string occurrence(s)")
        for address in hits:
            print(f"  string at 0x{address:x}")
            pattern = struct.pack("<Q", address)
            for start, data in snapshot:
                index = 0
                while True:
                    index = data.find(pattern, index)
                    if index < 0:
                        break
                    if index % 8 == 0:
                        dump_candidate(process, start + index, name)
                    index += 1


def module_info(process):
    modules = (ctypes.c_void_p * 1024)()
    needed = wt.DWORD()
    if not psapi.EnumProcessModules(process, modules, ctypes.sizeof(modules), ctypes.byref(needed)):
        raise OSError(ctypes.get_last_error())
    count = needed.value // ctypes.sizeof(ctypes.c_void_p)
    main_module = modules[0]
    info = MODULEINFO()
    if not psapi.GetModuleInformation(process, ctypes.c_void_p(main_module), ctypes.byref(info), ctypes.sizeof(info)):
        raise OSError(ctypes.get_last_error())
    return main_module, info.SizeOfImage


def all_readable_regions(process):
    regions = []
    address = 0x10000
    max_address = 0x7FFFFFFEFFFF
    mbi = MEMORY_BASIC_INFORMATION64()
    while address < max_address:
        if kernel32.VirtualQueryEx(process, ctypes.c_void_p(address), ctypes.byref(mbi), ctypes.sizeof(mbi)) == 0:
            break
        if mbi.RegionSize == 0:
            break
        if (
            mbi.State == MEM_COMMIT
            and (mbi.Protect & READABLE)
            and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            and mbi.Type in (0x20000, 0x1000000)  # MEM_PRIVATE heaps and MEM_IMAGE
        ):
            regions.append((mbi.BaseAddress, mbi.RegionSize))
        address = mbi.BaseAddress + mbi.RegionSize
    return regions


def readable_regions(process, base, size):
    regions = []
    address = base
    end = base + size
    mbi = MEMORY_BASIC_INFORMATION64()
    while address < end:
        if kernel32.VirtualQueryEx(process, ctypes.c_void_p(address), ctypes.byref(mbi), ctypes.sizeof(mbi)) == 0:
            break
        if mbi.RegionSize == 0:
            break
        start = max(mbi.BaseAddress, base)
        stop = min(mbi.BaseAddress + mbi.RegionSize, end)
        if (
            mbi.State == MEM_COMMIT
            and (mbi.Protect & READABLE)
            and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            and stop > start
        ):
            regions.append((start, stop - start))
        address = mbi.BaseAddress + mbi.RegionSize
    return regions


def read(process, address, size):
    buffer = ctypes.create_string_buffer(size)
    read_bytes = ctypes.c_size_t()
    if not kernel32.ReadProcessMemory(process, ctypes.c_void_p(address), buffer, size, ctypes.byref(read_bytes)):
        return b""
    return buffer.raw[: read_bytes.value]


def dump_candidate(process, address, name):
    data = read(process, address, 0x60)
    if len(data) < 0x60:
        return
    name_ptr = struct.unpack_from("<Q", data, 0x00)[0]
    flags = struct.unpack_from("<I", data, 0x08)[0]
    type_byte = data[0x0C]
    current = struct.unpack_from("<f", data, 0x10)[0]
    latched = struct.unpack_from("<f", data, 0x20)[0]
    reset = struct.unpack_from("<f", data, 0x30)[0]
    print(
        f"    candidate 0x{address:x} name=0x{name_ptr:x} flags=0x{flags:x} type={type_byte} "
        f"current={current:.3f} latched={latched:.3f} reset={reset:.3f}"
    )
    if type_byte != 1:
        print("      raw:", data.hex())


if __name__ == "__main__":
    sys.exit(main())
