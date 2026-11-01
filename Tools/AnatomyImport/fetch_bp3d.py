"""Parallel range download of the BodyParts3D IS-A OBJ archive (the server throttles each connection to a few KB/s).
Resumable: finished chunks are recorded in <zip>.chunks. Usage: python fetch_bp3d.py"""
import os, sys, threading, urllib.request, json, time
from concurrent.futures import ThreadPoolExecutor

URL = "https://dbarchive.biosciencedbc.jp/data/bodyparts3d/LATEST/isa_BP3D_4.0_obj_99.zip"
OUT = "D:/MedSimXR/Tools/AnatomyImport/raw/isa_BP3D_4.0_obj_99.zip"
CHUNK = 192 * 1024
THREADS = 64
state_path = OUT + ".chunks"

req = urllib.request.Request(URL, method="HEAD", headers={"User-Agent": "Mozilla/5.0"})
size = int(urllib.request.urlopen(req, timeout=60).headers["Content-Length"])
n = (size + CHUNK - 1) // CHUNK
done = set(json.load(open(state_path))) if os.path.exists(state_path) else set()
if not os.path.exists(OUT) or os.path.getsize(OUT) != size:
    with open(OUT, "wb") as f:
        f.truncate(size)
    done = set()
lock = threading.Lock()
fh = open(OUT, "r+b")

def grab(i):
    if i in done:
        return
    a, b = i * CHUNK, min(size, (i + 1) * CHUNK) - 1
    for attempt in range(8):
        try:
            r = urllib.request.Request(URL, headers={"User-Agent": "Mozilla/5.0", "Range": "bytes=%d-%d" % (a, b)})
            data = urllib.request.urlopen(r, timeout=90).read()
            if len(data) != b - a + 1:
                raise IOError("short read")
            with lock:
                fh.seek(a); fh.write(data); done.add(i)
                if len(done) % 4 == 0:
                    json.dump(sorted(done), open(state_path, "w"))
                    print("%d/%d chunks" % (len(done), n), flush=True)
            return
        except Exception as e:
            time.sleep(2 + attempt)
    print("chunk %d failed" % i, flush=True)

with ThreadPoolExecutor(THREADS) as ex:
    list(ex.map(grab, range(n)))
fh.flush(); fh.close()
json.dump(sorted(done), open(state_path, "w"))
print("DONE" if len(done) == n else "INCOMPLETE %d/%d" % (len(done), n), flush=True)

