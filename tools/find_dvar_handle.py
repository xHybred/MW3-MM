"""Find the cached dvar handle for a dvar_t address.

The engine caches dvar_t pointers in .data globals at registration time, so a
qword scan of the image finds every global that points at the dvar.
"""
import struct
import sys
import ctypes
from ctypes import wintypes

sys.path.insert(0, r"C:\Users\Kingo\Documents\GitHub\MW3Mod\tools")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wintypes.HANDLE
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400


def find_pid(name="iw5sp.exe"):
    import subprocess
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) > 1 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def main():
    target = int(sys.argv[1], 0)
    pid = find_pid()
    handle = k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not handle:
        raise SystemExit("OpenProcess failed")
    base = 0x140000000
    size = 0x44BE000
    needle = struct.pack("<Q", target)
    chunk = 8 << 20
    hits = []
    addr = base
    while addr < base + size:
        buf = ctypes.create_string_buffer(chunk)
        got = ctypes.c_size_t()
        ok = k32.ReadProcessMemory(handle, ctypes.c_void_p(addr), buf, chunk,
                                   ctypes.byref(got))
        if ok:
            data = buf.raw[:got.value]
            start = 0
            while True:
                i = data.find(needle, start)
                if i < 0:
                    break
                if (addr + i) % 8 == 0:
                    hits.append(addr + i)
                start = i + 1
        addr += chunk
    for h in hits:
        print(f"handle global {h:#x}")


main()
