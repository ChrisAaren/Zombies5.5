"""List / extract selected BVH files from the 100STYLE zip on Zenodo via HTTP range reads (no full download).
Data only: files are written, never executed.  usage: fetch_100style.py list | get <outdir> <substr>..."""
import io, sys, os, zipfile, urllib.request
URL = "https://zenodo.org/records/8127870/files/100STYLE.zip?download=1"

class HttpRange(io.RawIOBase):
    def __init__(self, url):
        r = urllib.request.urlopen(urllib.request.Request(url, method="HEAD"))
        self.url = r.geturl(); self.size = int(r.headers["Content-Length"]); self.pos = 0
    def seekable(self): return True
    def readable(self): return True
    def tell(self): return self.pos
    def seek(self, off, wh=0):
        self.pos = off if wh == 0 else self.pos + off if wh == 1 else self.size + off
        return self.pos
    def read(self, n=-1):
        if n < 0: n = self.size - self.pos
        n = min(n, self.size - self.pos)
        if n <= 0: return b""
        req = urllib.request.Request(self.url, headers={"Range": "bytes=%d-%d" % (self.pos, self.pos + n - 1)})
        d = urllib.request.urlopen(req).read(); self.pos += len(d); return d
    def readinto(self, b):
        d = self.read(len(b)); b[:len(d)] = d; return len(d)

z = zipfile.ZipFile(io.BufferedReader(HttpRange(URL), 1 << 20))
if sys.argv[1] == "list":
    for i in z.infolist(): print(i.filename, i.file_size)
else:
    out = sys.argv[2]
    for i in z.infolist():
        base = os.path.basename(i.filename)
        if base and any(s in base for s in sys.argv[3:]):
            open(os.path.join(out, base), "wb").write(z.read(i)); print("got", base, i.file_size)
