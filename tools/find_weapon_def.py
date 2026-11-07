"""Find weapon defs in the running game by looking for the adsZoomFov float.

A weapon def starts with its display name, so for every float that could be
adsZoomFov we print the printable string sitting behind it. The offset that
lines up with a real name is the real offset.
"""
import ctypes
import struct
import sys
from ctypes import wintypes

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wintypes.HANDLE
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]

ACCESS = 0x0010 | 0x0008 | 0x0020 | 0x0400
BASE = 0x140000000
SIZE = 0x44BE000


def find_pid(name="iw5sp.exe"):
    import subprocess
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {name}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True).stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) > 1 and parts[0].lower() == name.lower():
            return int(parts[1])
    return None


def read(h, addr, size):
    buf = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t()
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(got)):
        return None
    return buf.raw[:got.value]


def name_at(data, off):
    if off < 0 or off + 8 > len(data):
        return None
    end = data.find(b"\0", off, min(off + 64, len(data)))
    if end < 0 or end - off < 3:
        return None
    s = data[off:end]
    if all(32 <= c < 127 for c in s):
        return s.decode("ascii")
    return None


def main():
    pid = find_pid()
    if not pid:
        raise SystemExit("game not running")
    h = k32.OpenProcess(ACCESS, False, pid)
    want = float(sys.argv[1]) if len(sys.argv) > 1 else 40.0
    target = struct.pack("<f", want)
    hits = []
    for start in range(BASE, BASE + SIZE, 0x100000):
        data = read(h, start, 0x100000)
        if not data:
            continue
        i = data.find(target)
        while i >= 0:
            if i % 4 == 0:
                hits.append((start + i, data, i))
            i = data.find(target, i + 1)
    print(f"{len(hits)} float {want} hits")
    seen = 0
    for addr, data, i in hits:
        for back in (0x1C, 0x90, 0x74, 0x38):
            nm = name_at(data, i - back)
            if nm and len(nm) >= 4 and nm.replace("_", "").isalnum():
                print(f"  fov at {addr:#x}  name -0x{back:x}: {nm!r}")
                seen += 1
                break
        if seen >= 40:
            break


main()
