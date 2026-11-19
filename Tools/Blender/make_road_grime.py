"""Zombies 5.5: RoadGrime_Albedo.png, a 4 x 4 RGBA decal atlas of road wear (each cell is a 2 x 2 m tile, laid flat).

Cells (row-major from the top-left, same layout as RoadMarkingsV2):
   0 tyre-wear band pair (vertical, tiles along v)   1 single wheel path (tiles along v)
   2 oil stain (parking spot)                         3 oil drips
   4 patched asphalt rectangle, tar-sealed edge       5 utility-cut trench patch, double tar line
   6 tar-sealed crack                                 7 manhole cover on a patch
   8 utility steel lid on a patch                     9 gutter wet band (tiles along v)
  10 gutter litter and dust (tiles along v)          11 brake skid pair
  12 alligator cracking                              13 soft dirt blotch
  14 tar seam strip along the curb (tiles along v)   15 dust / sand drift (tiles along v)
Colours are unpremultiplied; alpha carries the strength, kept low so everything reads as subtle weathering.

Run: blender -b -P Tools/Blender/make_road_grime.py     Output: Assets/Art/Textures/RoadGrime_Albedo.png
"""
import os
import numpy as np
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.environ.get("RG_OUT") or os.path.join(ROOT, "Assets", "Art", "Textures", "RoadGrime_Albedo.png")
N = 512
RNG = np.random.default_rng(1305)
YY, XX = np.mgrid[0:N, 0:N].astype(np.float32)
U = (XX + 0.5) / N
V = (YY + 0.5) / N           # row 0 is the top of the cell (v = 1 in Unity); only used for shapes


def fnoise(sx, sy=None, beta=2.0, seed=None):
    """Periodic noise in [0,1]; sx, sy are feature counts across the tile (anisotropic when they differ)."""
    sy = sx if sy is None else sy
    r = np.random.default_rng(seed if seed is not None else RNG.integers(1 << 30))
    f = np.fft.fft2(r.standard_normal((N, N)))
    fx = np.fft.fftfreq(N) * N
    kx, ky = np.meshgrid(fx, fx)
    k = np.sqrt((kx / sx) ** 2 + (ky / sy) ** 2) + 1e-3
    f *= 1.0 / (1 + k) ** beta
    a = np.real(np.fft.ifft2(f)).astype(np.float32)
    a -= a.min()
    return a / max(a.max(), 1e-6)


def smooth(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def blank():
    return np.zeros((N, N, 4), np.float32)


def fill(img, color, alpha):
    img[..., :3] = np.array(color, np.float32)
    img[..., 3] = np.clip(alpha, 0, 1)
    return img


def vor_edges(npts, seed):
    r = np.random.default_rng(seed)
    pts = r.random((npts, 2))
    d1 = np.full((N, N), 9.0, np.float32)
    d2 = np.full((N, N), 9.0, np.float32)
    for p in pts:
        for ox in (-1, 0, 1):
            for oy in (-1, 0, 1):
                d = np.sqrt((U - p[0] - ox) ** 2 + (V - p[1] - oy) ** 2)
                m = d < d1
                d2 = np.where(m, d1, np.minimum(d2, d))
                d1 = np.minimum(d1, d)
    return d2 - d1


DARK = (0.035, 0.035, 0.04)
TAR = (0.02, 0.02, 0.022)


def tire_pair():
    c = blank()
    n = fnoise(2, 36, 1.6)
    n2 = fnoise(8, 60, 1.2)
    a = np.zeros((N, N), np.float32)
    for cx in (0.5 - 0.39, 0.5 + 0.39):
        w = 0.085 + 0.02 * (fnoise(1, 4, 2) - 0.5)
        a = np.maximum(a, np.exp(-((U - cx) / w) ** 2))
    a *= 0.28 + 0.40 * smooth(n, 0.25, 0.8) + 0.18 * n2
    a *= smooth(U, 0.0, 0.1) * smooth(1 - U, 0.0, 0.1)
    return fill(c, DARK, a * 0.62)


def tire_single():
    c = blank()
    n = fnoise(2, 36, 1.6)
    n2 = fnoise(8, 60, 1.2)
    a = np.exp(-((U - 0.5) / 0.12) ** 2) * (0.3 + 0.45 * smooth(n, 0.25, 0.8) + 0.2 * n2)
    a *= smooth(U, 0.0, 0.1) * smooth(1 - U, 0.0, 0.1)
    return fill(c, DARK, a * 0.62)


def oil_spot():
    c = blank()
    n = fnoise(3, 3, 1.8)
    n2 = fnoise(10, 10, 1.4)
    rr = np.sqrt(((U - 0.5) / 0.9) ** 2 + ((V - 0.5) / 0.7) ** 2)
    field = (0.34 - rr) + (n - 0.5) * 0.30 + (n2 - 0.5) * 0.06
    a = smooth(field, -0.02, 0.10)
    core = smooth(field, 0.06, 0.20)
    # a drip tail running back
    tail = np.exp(-((U - 0.5 + 0.1 * (n - 0.5)) / 0.025) ** 2) * smooth(V, 0.45, 0.62) * smooth(0.9 - V, 0, 0.2) * 0.6
    a = np.maximum(a * (0.45 + 0.35 * core), tail * 0.5)
    col = np.array((0.045, 0.040, 0.037), np.float32)
    c[..., :3] = col
    c[..., 3] = np.clip(a * 0.75, 0, 1)
    return c


def oil_drips():
    c = blank()
    a = np.zeros((N, N), np.float32)
    r = np.random.default_rng(7)
    for _ in range(9):
        x, y = r.random(2) * 0.7 + 0.15
        rad = r.uniform(0.012, 0.04)
        a = np.maximum(a, smooth(1 - np.sqrt((U - x) ** 2 + (V - y) ** 2) / rad, 0, 0.5) * r.uniform(0.5, 0.9))
    a *= 0.6 + 0.4 * fnoise(12, 12)
    return fill(c, (0.045, 0.040, 0.037), a * 0.7)


def rect_mask(x0, y0, x1, y1, wob=0.004, soft=0.003):
    nx = (fnoise(40, 2, 1.0) - 0.5) * wob
    ny = (fnoise(2, 40, 1.0) - 0.5) * wob
    dx = np.minimum(U - x0 + nx, x1 - U + nx)
    dy = np.minimum(V - y0 + ny, y1 - V + ny)
    return smooth(np.minimum(dx, dy), 0.0, soft), np.minimum(dx, dy)


def patch_rect():
    c = blank()
    m, d = rect_mask(0.03, 0.03, 0.97, 0.97)
    grain = fnoise(90, 90, 0.8)
    tone = 0.05 + 0.03 * grain + 0.02 * fnoise(5, 5, 1.5)
    border = smooth(0.016 - np.abs(d - 0.016), 0.0, 0.012) * (0.75 + 0.25 * fnoise(30, 30))
    col = np.stack([tone, tone, tone * 1.04], -1)
    a = m * 0.55
    c[..., :3] = col
    c[..., 3] = a
    c[..., :3] = c[..., :3] * (1 - border[..., None]) + np.array(TAR) * border[..., None]
    c[..., 3] = np.maximum(c[..., 3], border * 0.9 * (d > -0.002))
    return c


def patch_trench():
    c = blank()
    m, d = rect_mask(0.03, 0.03, 0.97, 0.97)
    grain = fnoise(90, 90, 0.8)
    tone = 0.095 + 0.035 * grain + 0.02 * fnoise(4, 4, 1.5)
    col = np.stack([tone, tone, tone * 1.03], -1)
    b1 = smooth(0.012 - np.abs(d - 0.012), 0.0, 0.01)
    b2 = smooth(0.010 - np.abs(d - 0.052), 0.0, 0.008) * 0.8
    border = np.clip(b1 + b2, 0, 1) * (0.75 + 0.25 * fnoise(30, 30))
    c[..., :3] = col * (1 - border[..., None]) + np.array(TAR) * border[..., None]
    c[..., 3] = np.maximum(m * 0.6, border * 0.9 * (d > -0.002))
    return c


def crack_line():
    c = blank()
    a = np.zeros((N, N), np.float32)
    r = np.random.default_rng(11)

    def wander(y0, amp, branches):
        nonlocal a
        ys = y0 + np.cumsum(r.normal(0, amp, N))
        ys -= np.linspace(ys[0], ys[-1], N) - y0   # periodic-ish
        for i in range(N):
            yy = ys[i] + 0.012 * np.sin(i / 17.0)
            a[:, i] = np.maximum(a[:, i], np.exp(-(((np.arange(N) / N) - yy) / 0.0042) ** 2) * (0.55 + 0.45 * np.sin(i / 23.0 + 1) ** 2))
        return ys

    ys = wander(0.5, 0.0016, 0)
    for _ in range(3):
        i0 = r.integers(40, N - 120)
        L = r.integers(40, 110)
        sgn = r.choice([-1, 1])
        y = ys[i0]
        for k in range(L):
            y += sgn * 0.0025 + r.normal(0, 0.0012)
            x = i0 + k
            if x >= N:
                break
            j = int(np.clip(y * N, 0, N - 1))
            a[max(j - 1, 0):j + 2, x] = np.maximum(a[max(j - 1, 0):j + 2, x], 0.55)
    a = np.clip(a, 0, 1)
    a *= smooth(U, 0, 0.06) * smooth(1 - U, 0, 0.06)
    return fill(c, TAR, a * 0.85)


def manhole():
    c = blank()
    rr = np.sqrt((U - 0.5) ** 2 + (V - 0.5) ** 2)
    halo_m, d = rect_mask(0.2, 0.2, 0.8, 0.8, wob=0.01, soft=0.01)
    halo = halo_m * smooth(0.34 - np.sqrt((U - 0.5) ** 2 + (V - 0.5) ** 2) * 0.0, 0, 1) * 0.4
    disk = smooth(0.19 - rr, 0.0, 0.006)
    rim = smooth(0.012 - np.abs(rr - 0.18), 0, 0.01)
    ring_g = (np.sin(rr * 160) > 0.2).astype(np.float32) * smooth(0.165 - rr, 0, 0.01)
    cross = ((np.abs(np.sin((U + V) * 70)) > 0.85) | (np.abs(np.sin((U - V) * 70)) > 0.85)).astype(np.float32) * smooth(0.14 - rr, 0, 0.01)
    iron = 0.085 + 0.03 * fnoise(40, 40) + 0.03 * ring_g * 0 + 0.03 * cross
    iron += 0.05 * rim
    col = np.stack([iron, iron * 0.98, iron * 1.0], -1)
    patchcol = np.array((0.055, 0.055, 0.06), np.float32)
    c[..., :3] = patchcol
    c[..., 3] = halo
    c[..., :3] = c[..., :3] * (1 - disk[..., None]) + col * disk[..., None]
    c[..., 3] = np.maximum(c[..., 3], disk * 0.95)
    gap = smooth(0.008 - np.abs(rr - 0.198), 0, 0.006)
    c[..., :3] = c[..., :3] * (1 - gap[..., None]) + np.array(TAR) * gap[..., None]
    c[..., 3] = np.maximum(c[..., 3], gap * 0.9)
    return c


def utility_lid():
    c = blank()
    m, d = rect_mask(0.12, 0.2, 0.88, 0.8, wob=0.01, soft=0.01)
    lid, dl = rect_mask(0.24, 0.3, 0.76, 0.7, wob=0.002, soft=0.004)
    diamond = (np.abs(((U * 38) % 1) - 0.5) + np.abs(((V * 38) % 1) - 0.5) < 0.42).astype(np.float32)
    iron = 0.08 + 0.035 * diamond + 0.025 * fnoise(30, 30)
    col = np.stack([iron, iron, iron * 1.02], -1)
    c[..., :3] = np.array((0.055, 0.055, 0.06), np.float32)
    c[..., 3] = m * 0.45
    c[..., :3] = c[..., :3] * (1 - lid[..., None]) + col * lid[..., None]
    c[..., 3] = np.maximum(c[..., 3], lid * 0.95)
    edge = smooth(0.008 - np.abs(dl - 0.004), 0, 0.006)
    c[..., :3] = c[..., :3] * (1 - edge[..., None]) + np.array(TAR)[None, None] * edge[..., None]
    c[..., 3] = np.maximum(c[..., 3], edge * 0.9)
    return c


def gutter_wet():
    c = blank()
    n = fnoise(3, 22, 1.6)
    edge = 0.62 + 0.10 * (n - 0.5)                  # wet line along the curb at the right of the tile
    a = smooth(U, edge - 0.45, edge) * smooth(0.97 - U, 0.0, 0.06)
    a *= 0.45 + 0.55 * fnoise(4, 30, 1.4)
    a = a ** 1.2
    return fill(c, (0.045, 0.047, 0.05), a * 0.50)


def gutter_litter():
    c = blank()
    n = fnoise(6, 30, 1.2)
    n2 = fnoise(30, 80, 0.9)
    base = smooth(U, 0.35, 0.8) * smooth(0.98 - U, 0, 0.05)
    dark = base * smooth(n, 0.45, 0.85)
    c[..., :3] = np.array((0.05, 0.048, 0.045), np.float32)
    dust = base * smooth(n2, 0.62, 0.9) * (1 - dark)
    c[..., :3] = c[..., :3] * (1 - dust[..., None]) + np.array((0.30, 0.27, 0.22), np.float32) * dust[..., None]
    c[..., 3] = np.clip(dark * 0.45 + dust * 0.30, 0, 1)
    return c


def skid():
    c = blank()
    a = np.zeros((N, N), np.float32)
    n = fnoise(10, 30, 1.2)
    for cx in (0.5 - 0.39, 0.5 + 0.39):
        path = cx + 0.05 * np.sin(V * 3.0) * 0.4
        w = 0.022 + 0.008 * n
        a = np.maximum(a, np.exp(-((U - path) / w) ** 2))
    a *= smooth(V, 0.05, 0.4) * smooth(0.95 - V, 0.0, 0.2)
    a *= 0.5 + 0.5 * smooth(n, 0.3, 0.8)
    return fill(c, (0.03, 0.03, 0.033), a * 0.5)


def alligator():
    c = blank()
    e = vor_edges(26, 5)
    line = smooth(0.012 - e, 0.0, 0.008) * (0.6 + 0.4 * fnoise(20, 20))
    rr = np.sqrt((U - 0.5) ** 2 + (V - 0.5) ** 2)
    fade = smooth(0.42 - rr + 0.12 * (fnoise(3, 3) - 0.5), 0, 0.25)
    return fill(c, TAR, line * fade * 0.8)


def blotch():
    c = blank()
    n = fnoise(2, 2, 2.0)
    n2 = fnoise(7, 7, 1.3)
    rr = np.sqrt(((U - 0.5) / 0.5) ** 2 + ((V - 0.5) / 0.5) ** 2)
    f = (0.9 - rr) + (n - 0.5) * 0.9 + (n2 - 0.5) * 0.2
    a = smooth(f, 0.0, 0.5)
    return fill(c, (0.05, 0.05, 0.055), a * 0.30)


def curb_seam():
    c = blank()
    n = fnoise(4, 18, 1.4)
    # tar strip hugging the curb at the right: a hard dark band with a ragged road-side edge
    edge = 0.78 + 0.07 * (n - 0.5)
    a = smooth(U, edge, edge + 0.03) * smooth(0.985 - U, 0.0, 0.02)
    a *= 0.7 + 0.3 * fnoise(10, 40)
    soft = smooth(U, edge - 0.25, edge) * 0.25
    return fill(c, (0.03, 0.03, 0.033), np.clip(a * 0.7 + soft * smooth(0.985 - U, 0, 0.02), 0, 1))


def dust_drift():
    c = blank()
    n = fnoise(5, 26, 1.4)
    a = smooth(U, 0.5, 0.95) * smooth(0.99 - U, 0.0, 0.03) * smooth(n, 0.35, 0.85)
    return fill(c, (0.34, 0.31, 0.26), a * 0.22)


CELLS = [tire_pair, tire_single, oil_spot, oil_drips, patch_rect, patch_trench, crack_line, manhole,
         utility_lid, gutter_wet, gutter_litter, skid, alligator, blotch, curb_seam, dust_drift]


def main():
    atlas = np.zeros((N * 4, N * 4, 4), np.float32)
    for i, fn in enumerate(CELLS):
        cell = fn()
        r, cc = divmod(i, 4)
        atlas[r * N:(r + 1) * N, cc * N:(cc + 1) * N] = cell
        print("cell", i, fn.__name__, "mean alpha %.3f" % cell[..., 3].mean())
    # bleed colour into transparent texels so bilinear/mip filtering does not halo
    rgb = atlas[..., :3]
    a = atlas[..., 3:4]
    mean_col = (rgb * a).sum() / max(a.sum(), 1e-6)
    atlas[..., :3] = np.where(a > 0.002, rgb, mean_col)
    # Blender image origin is bottom-left: flip rows so cell 0 lands top-left in the file
    flipped = atlas[::-1].copy()
    img = bpy.data.images.new("RoadGrime_Albedo", N * 4, N * 4, alpha=True, float_buffer=False)
    img.colorspace_settings.name = 'sRGB'
    lin = flipped.copy()
    img.pixels.foreach_set(lin.reshape(-1))
    img.filepath_raw = OUT
    img.file_format = 'PNG'
    img.alpha_mode = 'STRAIGHT'
    img.save()
    print("saved", OUT)


main()
