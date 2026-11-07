"""Patch a byte range in the running game, with an undo file.

Usage:
  python patch_bytes.py show <va> <len>
  python patch_bytes.py ret  <va>            # write C3 (ret) at va
  python patch_bytes.py write <va> <hex>     # write raw bytes, e.g. 48895c2408
  python patch_bytes.py restore <undo.json>
"""
import ctypes
import json
import struct
import sys
from ctypes import wintypes

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wintypes.HANDLE
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                   ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.VirtualProtectEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t,
                                 wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]

ACCESS = 0x0010 | 0x0008 | 0x0020 | 0x0400
PAGE_EXECUTE_READWRITE = 0x40


def find_pid(name="iw5sp.exe"):
    import subprocess
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) > 1 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def open_game():
    return k32.OpenProcess(ACCESS, False, find_pid())


def read(handle, addr, size):
    buf = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t()
    if not k32.ReadProcessMemory(handle, ctypes.c_void_p(addr), buf, size, ctypes.byref(got)):
        raise OSError(ctypes.get_last_error(), "ReadProcessMemory failed")
    return buf.raw[:got.value]


def write(handle, addr, data):
    old = wintypes.DWORD()
    if not k32.VirtualProtectEx(handle, ctypes.c_void_p(addr), len(data),
                                PAGE_EXECUTE_READWRITE, ctypes.byref(old)):
        raise OSError(ctypes.get_last_error(), "VirtualProtectEx failed")
    got = ctypes.c_size_t()
    ok = k32.WriteProcessMemory(handle, ctypes.c_void_p(addr), data, len(data),
                                ctypes.byref(got))
    k32.VirtualProtectEx(handle, ctypes.c_void_p(addr), len(data), old.value,
                         ctypes.byref(old))
    if not ok:
        raise OSError(ctypes.get_last_error(), "WriteProcessMemory failed")


def main():
    mode = sys.argv[1]
    handle = open_game()
    if mode == "show":
        addr = int(sys.argv[2], 0)
        data = read(handle, addr, int(sys.argv[3], 0) if len(sys.argv) > 3 else 16)
        print(addr + 0x140000000 if addr < 0x140000000 else addr,
              data.hex(" "))
    elif mode == "ret":
        addr = int(sys.argv[2], 0)
        original = read(handle, addr, 1)
        write(handle, addr, b"\xc3")
        print(f"patched {addr:#x}: {original.hex()} -> c3")
    elif mode == "write":
        addr = int(sys.argv[2], 0)
        data = bytes.fromhex(sys.argv[3])
        original = read(handle, addr, len(data))
        write(handle, addr, data)
        print(f"patched {addr:#x}: {original.hex()} -> {data.hex()}")
    elif mode == "restore":
        state = json.load(open(sys.argv[2]))
        for item in state:
            write(handle, int(item["addr"], 0), bytes.fromhex(item["bytes"]))
            print(f"restored {int(item['addr'], 0):#x} = {item['bytes']}")
    else:
        raise SystemExit(f"unknown mode {mode}")


main()
