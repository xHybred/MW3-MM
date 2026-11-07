"""Temporarily force FUN_140022630 (the current weapon's ADS FOV) to return a fixed value.

Takes a read-only executable page, allocates a scratch page for the float constant,
and rewrites the first bytes of the function to:

    movss xmm0, [rip+0]
    ret

Usage: python tools/patch_weapon_fov.py <value>
       python tools/patch_weapon_fov.py --restore
"""

import ctypes
import ctypes.wintypes as wt
import struct
import sys

from set_fov import find_pid, read, write

PAGE_EXECUTE_READWRITE = 0x40
PAGE_EXECUTE_READ = 0x20
MEM_COMMIT = 0x1000
MEM_RESERVE = 0x2000
MEM_RELEASE = 0x8000

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.VirtualAllocEx.argtypes = [
    wt.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wt.DWORD, wt.DWORD
]
kernel32.VirtualAllocEx.restype = ctypes.c_void_p
kernel32.OpenProcess.restype = wt.HANDLE

TARGET = 0x140022630


def protect(process, address, size, new_protect):
    old = wt.DWORD()
    ok = kernel32.VirtualProtectEx(
        process, ctypes.c_void_p(address), size, new_protect, ctypes.byref(old)
    )
    if not ok:
        raise OSError(f"VirtualProtectEx failed: {ctypes.get_last_error()}")
    return old.value


def main():
    pid = find_pid()
    if not pid:
        print("iw5sp.exe is not running")
        return 1

    process = kernel32.OpenProcess(0x1F0FFF, False, pid)  # PROCESS_ALL_ACCESS
    if not process:
        print("OpenProcess failed", ctypes.get_last_error())
        return 1

    if sys.argv[1] == "--restore":
        # The stub always leaves the scratch pointer in a fixed place? No: we
        # simply restore the original prologue saved next to this script.
        with open(__file__ + ".orig", "rb") as handle:
            original = handle.read()
        protect(process, TARGET, 16, PAGE_EXECUTE_READWRITE)
        write(process, TARGET, original)
        protect(process, TARGET, 16, PAGE_EXECUTE_READ)
        print("restored original prologue")
        return 0

    value = float(sys.argv[1])

    original = read(process, TARGET, 16)
    if len(original) != 16:
        print("could not read target function")
        return 1

    # movss xmm0,[rip+0] / ret / padding, then the constant 16 bytes later.
    # The stub reaches the constant through a 32-bit RIP-relative displacement,
    # so the scratch page has to sit within 2 GB of the function.
    scratch = 0
    for hint in (0x145000000, 0x150000000, 0x160000000, 0x100000000, 0x40000000):
        scratch = kernel32.VirtualAllocEx(
            process, ctypes.c_void_p(hint), 0x1000,
            MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE,
        )
        if scratch:
            break
    if not scratch:
        print("VirtualAllocEx failed", ctypes.get_last_error())
        return 1

    constant = scratch + 0x800
    write(process, constant, struct.pack("<f", value))

    displacement = constant - (TARGET + 8)
    stub = b"\xF3\x0F\x10\x05" + struct.pack("<i", displacement) + b"\xC3" + b"\xCC" * 6

    with open(__file__ + ".orig", "wb") as handle:
        handle.write(original)

    protect(process, TARGET, 16, PAGE_EXECUTE_READWRITE)
    write(process, TARGET, stub)
    protect(process, TARGET, 16, PAGE_EXECUTE_READ)

    print(f"patched {hex(TARGET)} to return {value}")
    print(f"orig bytes {original.hex()}")
    print(f"stub {stub.hex()}  constant at {hex(constant)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
