"""Write cg_fov / cg_fovScale in the running game for quick experiments.

Usage: python tools/set_fov.py <fov> <scale>
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


def find_pid(name="iw5sp.exe"):
    out = subprocess.check_output(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"], text=True)
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def all_readable_regions(process):
    regions = []
    address = 0x10000
    mbi = MEMORY_BASIC_INFORMATION64()
    while address < 0x7FFFFFFEFFFF:
        if kernel32.VirtualQueryEx(process, ctypes.c_void_p(address), ctypes.byref(mbi), ctypes.sizeof(mbi)) == 0:
            break
        if mbi.RegionSize == 0:
            break
        if (
            mbi.State == MEM_COMMIT
            and (mbi.Protect & READABLE)
            and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            and mbi.Type in (0x20000, 0x1000000)
        ):
            regions.append((mbi.BaseAddress, mbi.RegionSize))
        address = mbi.BaseAddress + mbi.RegionSize
    return regions


def read(process, address, size):
    buffer = ctypes.create_string_buffer(size)
    read_bytes = ctypes.c_size_t()
    if not kernel32.ReadProcessMemory(process, ctypes.c_void_p(address), buffer, size, ctypes.byref(read_bytes)):
        return b""
    return buffer.raw[: read_bytes.value]


def write(process, address, data):
    written = ctypes.c_size_t()
    return bool(kernel32.WriteProcessMemory(
        process, ctypes.c_void_p(address), data, len(data), ctypes.byref(written)
    ))


def find_float_dvar(process, name, expected_reset):
    needle = name.encode() + b"\0"
    snapshot = [(start, read(process, start, length)) for start, length in all_readable_regions(process)]
    for start, data in snapshot:
        index = 0
        while True:
            index = data.find(needle, index)
            if index < 0:
                break
            pattern = struct.pack("<Q", start + index)
            for other_start, other in snapshot:
                position = 0
                while True:
                    position = other.find(pattern, position)
                    if position < 0:
                        break
                    if position % 8 == 0:
                        candidate = other_start + position
                        block = read(process, candidate, 0x60)
                        if len(block) == 0x60 and block[0x0C] == 1:
                            reset = struct.unpack_from("<f", block, 0x30)[0]
                            if abs(reset - expected_reset) < 0.001:
                                return candidate
                    position += 1
            index += 1
    return None


def main():
    fov = float(sys.argv[1]) if len(sys.argv) > 1 else 90.0
    scale = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0

    pid = find_pid()
    if not pid:
        print("iw5sp.exe is not running")
        return 1
    access = PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION
    process = kernel32.OpenProcess(access, False, pid)
    if not process:
        print("OpenProcess failed", ctypes.get_last_error())
        return 1

    fov_address = find_float_dvar(process, "cg_fov", 65.0)
    scale_address = find_float_dvar(process, "cg_fovScale", 1.0)
    print(f"cg_fov {fov_address and hex(fov_address)}  cg_fovScale {scale_address and hex(scale_address)}")

    if fov_address:
        write(process, fov_address + 0x10, struct.pack("<f", fov))
        write(process, fov_address + 0x20, struct.pack("<f", fov))
    if scale_address:
        write(process, scale_address + 0x10, struct.pack("<f", scale))
        write(process, scale_address + 0x20, struct.pack("<f", scale))
    print(f"wrote cg_fov={fov} cg_fovScale={scale}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
