"""Texture generators for architecture kit v2 (numpy; Blender only writes the PNGs).
Imported by make_architecture_v2.py.  Sets: BrickRed / BrickBrown / BrickTan (1.5 m x 1.6 m tile, bond aligned to the
kit's 3 m bays and 3.2 / 4.0 m floors), StoneTrim (2 m), MetalShutter (1 m), WindowInteriors atlas (+ emission), SideAds.
Everything tiling is periodic (FFT noise, integer brick grid)."""
import os, math
import numpy as np
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Art", "Textures"))
os.makedirs(OUT, exist_ok=True)


# ------------------------------------------------------------------ numpy helpers
def noise(H, W, beta=2.0, fmin=1, fmax=None, kx=1.0, ky=1.0, seed=0):
    """Periodic 1/f^beta noise in [-1, 1].  kx > 1 gives narrow (high-frequency in x) features: vertical streaks."""
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(H)[:, None] * H * ky
    fx = np.fft.rfftfreq(W)[None, :] * W * kx
    f = np.sqrt(fx ** 2 + fy ** 2)
    amp = np.where(f >= fmin, np.maximum(f, 1e-6) ** (-beta / 2), 0.0)
    if fmax:
        amp = amp * (f <= fmax)
    spec = amp * (r.normal(size=f.shape) + 1j * r.normal(size=f.shape))
    n = np.fft.irfft2(spec, s=(H, W))
    return n / (np.abs(n).max() + 1e-9)


def sstep(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def blur(a, px):
    H, W = a.shape[:2]
    fy = np.fft.fftfreq(H)[:, None]
    fx = np.fft.rfftfreq(W)[None, :]
    g = np.exp(-2 * (np.pi * px) ** 2 * (fx ** 2 + fy ** 2))
    if a.ndim == 2:
        return np.fft.irfft2(np.fft.rfft2(a) * g, s=(H, W))
    return np.stack([blur(a[..., i], px) for i in range(a.shape[-1])], -1)


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 0.5
    n = np.stack([-dx * strength, -dy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def save(name, suffix, arr, alpha=None, srgb=None):
    arr = np.clip(arr, 0, 1)
    if arr.ndim == 2:
        arr = np.repeat(arr[..., None], 3, -1)
    H, W = arr.shape[:2]
    a = np.ones((H, W, 1)) if alpha is None else np.clip(alpha, 0, 1)[..., None]
    rgba = np.concatenate([arr, a], -1)[::-1].astype(np.float32)
    img = bpy.data.images.new(f"{name}_{suffix}", W, H, alpha=alpha is not None)
    srgb = (suffix == "Albedo") if srgb is None else srgb
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    if alpha is not None:
        img.alpha_mode = 'CHANNEL_PACKED'
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, f"{name}_{suffix}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print("TEX", name, suffix, W, H)


# ------------------------------------------------------------------ brick
BRICK_PAL = {
    "BrickRed": ((0.60, 0.25, 0.18), 0.040),
    "BrickBrown": ((0.38, 0.22, 0.16), 0.032),
    "BrickTan": ((0.66, 0.53, 0.38), 0.012),
}
# per-brick value variance and rate of burnt / pale outliers; tan gets a calm bond so it does not alias into a zig-zag
BRICK_VAR = {"BrickRed": (0.15, 0.07, 0.04), "BrickBrown": (0.14, 0.06, 0.04), "BrickTan": (0.07, 0.02, 0.015)}


def make_brick(name, seed):
    base, hue_j = BRICK_PAL[name]
    vvar, burnt_p, pale_p = BRICK_VAR[name]
    W, H = 1536, 1600
    BW, BH, ROWS, COLS, M = 256, 80, 20, 6, 10
    r = np.random.default_rng(seed)
    yy = np.arange(H)
    row = yy // BH
    yl = (yy % BH)[:, None]
    off = (row % 2) * (BW // 2)
    xs = (np.arange(W)[None, :] + off[:, None]) % W
    col = (xs // BW)
    xl = xs % BW
    rowg = np.broadcast_to(row[:, None], (H, W))
    # per-brick randomness
    v_rand = r.normal(0, 1, (ROWS, COLS))
    h_rand = r.normal(0, 1, (ROWS, COLS, 3))
    burnt = r.random((ROWS, COLS)) < burnt_p
    pale = r.random((ROWS, COLS)) < pale_p
    tilt_x = r.normal(0, 1, (ROWS, COLS)); tilt_y = r.normal(0, 1, (ROWS, COLS))
    bid_v = v_rand[rowg, col]
    # distance to brick edge in pixels
    d = np.minimum(np.minimum(xl, BW - 1 - xl), np.minimum(yl, BH - 1 - yl)).astype(np.float32)
    # slightly irregular mortar edge
    wob = noise(H, W, 1.2, 20, 200, seed=seed + 1) * 2.2
    d = d + wob
    brick = sstep(d, M / 2 - 1.5, M / 2 + 2.5)               # 0 mortar .. 1 brick face
    edge_round = sstep(d, M / 2, M / 2 + 12)                # rounded arris
    # per brick colour
    bc = np.array(base)[None, None, :] * (1 + vvar * bid_v[..., None] + hue_j * h_rand[rowg, col] * np.array([1.0, 0.7, 0.5]))
    bc = np.where(burnt[rowg, col][..., None], bc * np.array([0.74, 0.72, 0.74]), bc)
    bc = np.where(pale[rowg, col][..., None], bc * np.array([1.12, 1.15, 1.2]) + 0.03, bc)
    # surface texture on the face
    grain = noise(H, W, 0.6, 30, 400, seed=seed + 2)
    mott = noise(H, W, 2.4, 3, 60, seed=seed + 3)
    bc = bc * (1 + 0.10 * grain[..., None] + 0.10 * mott[..., None])
    bc = bc * (0.86 + 0.14 * edge_round[..., None])          # darker toward the arris
    # mortar
    mort = np.array([0.50, 0.47, 0.42])[None, None, :] * (1 + 0.22 * noise(H, W, 0.4, 40, 500, seed=seed + 4)[..., None])
    alb = bc * brick[..., None] + mort * (1 - brick[..., None])
    # soot: vertical streaks, heavier toward the top of the tile (wraps, so use band noise)
    soot = noise(H, W, 3.2, 2, 80, kx=1.0, ky=5.0, seed=seed + 5)
    soot2 = noise(H, W, 2.6, 1, 20, seed=seed + 6)
    soot = sstep(soot * 0.8 + soot2 * 0.5, 0.05, 0.7)
    alb = alb * (1 - 0.24 * soot[..., None])
    # efflorescence: pale salts, strongest on mortar lines and arrises, patchy
    patch = sstep(noise(H, W, 2.8, 2, 30, ky=2.0, seed=seed + 7), 0.25, 0.6)
    speck = sstep(noise(H, W, 0.3, 40, 500, seed=seed + 8), 0.1, 0.6)
    eff = patch * (0.35 + 0.65 * speck) * (0.45 + 0.55 * (1 - edge_round))
    alb = alb * (1 - 0.30 * eff[..., None]) + np.array([0.80, 0.78, 0.72])[None, None, :] * 0.30 * eff[..., None]
    # tone: keep hue, lift values for the game's bright exposure
    g = alb.mean(-1, keepdims=True)
    alb = np.clip((g + (alb - g) * 0.86) * 1.12, 0, 1)
    save(name, "Albedo", alb)
    # height in pixels: mortar recessed ~ 5 px, brick faces slightly warped per brick
    face = edge_round * 5.0 + brick * 1.0
    face += 1.2 * grain + 1.0 * mott
    face += (tilt_x[rowg, col] * (xl - BW / 2) / BW + tilt_y[rowg, col] * (yl - BH / 2) / BH) * 1.6
    face -= 1.4 * sstep(noise(H, W, 0.4, 60, 500, seed=seed + 9), 0.35, 0.6)   # pits
    nrm = normal_from_height(face, 0.9)
    save(name, "Normal", nrm)
    smooth = 0.28 + 0.1 * grain - 0.12 * eff + 0.0
    save(name, "Mask", np.clip(smooth, 0, 1))


# ------------------------------------------------------------------ stone trim
def make_stone():
    N = 1024
    name = "StoneTrim"
    s = 500
    mott = noise(N, N, 2.4, 2, 40, seed=s)
    fine = noise(N, N, 0.8, 30, 450, seed=s + 1)
    bed = noise(N, N, 2.0, 1, 12, kx=1.5, ky=0.15, seed=s + 2)          # horizontal bedding
    pits = sstep(noise(N, N, 0.3, 25, 500, seed=s + 3), 0.28, 0.55)
    base = 0.72 + 0.05 * mott + 0.05 * fine + 0.04 * bed
    # weathering: rain streaks + grime pockets
    streak = sstep(noise(N, N, 3.0, 2, 60, ky=6.0, seed=s + 4) * 0.9 + noise(N, N, 2.4, 1, 10, seed=s + 5) * 0.4, 0.05, 0.6)
    grime = sstep(noise(N, N, 2.6, 1, 14, seed=s + 6), 0.2, 0.7)
    alb = np.repeat(base[..., None], 3, -1) * np.array([1.0, 0.985, 0.95])[None, None, :]
    alb *= (1 - 0.18 * streak[..., None]) * (1 - 0.2 * grime[..., None]) * (1 - 0.35 * pits[..., None])
    # a few hairline cracks
    crack = np.abs(noise(N, N, 2.0, 2, 14, seed=s + 7))
    alb *= (1 - 0.08 * (1 - sstep(crack, 0.0, 0.012)))[..., None]
    alb = np.clip(alb * 0.97, 0, 1)
    save(name, "Albedo", alb)
    h = 2.0 * fine + 3.0 * mott + 1.5 * bed - 2.5 * pits - 2.0 * (1 - sstep(crack, 0.0, 0.01))
    save(name, "Normal", normal_from_height(h, 0.35))
    save(name, "Mask", np.clip(0.3 + 0.12 * fine - 0.1 * grime, 0, 1))


# ------------------------------------------------------------------ corrugated shutter
def make_shutter():
    N = 1024
    name = "MetalShutter"
    s = 900
    RID = 16                                    # corrugations per metre (tile = 1 m)
    y = np.arange(N)[:, None] / N
    ph = (y * RID) % 1.0
    prof = 0.5 - 0.5 * np.cos(2 * np.pi * ph)                      # 0 valley .. 1 crest
    prof = np.broadcast_to(prof, (N, N)).copy()
    dent = noise(N, N, 3.0, 1, 8, seed=s) * 6.0
    scr = sstep(np.abs(noise(N, N, 0.2, 60, 500, kx=8.0, ky=0.2, seed=s + 1)), 0.55, 0.9)
    rust_streak = sstep(noise(N, N, 3.0, 2, 70, ky=7.0, seed=s + 2) * 0.9 + noise(N, N, 2.4, 1, 10, seed=s + 3) * 0.5, 0.1, 0.65)
    rust_blot = sstep(noise(N, N, 2.8, 1, 18, seed=s + 4), 0.25, 0.7)
    rust = np.clip(rust_streak * 0.8 + rust_blot * 0.5, 0, 1) * sstep(noise(N, N, 2.0, 1, 6, seed=s + 5), -0.3, 0.4)
    paint = 0.52 + 0.05 * noise(N, N, 2.4, 2, 30, seed=s + 6) + 0.03 * noise(N, N, 0.6, 30, 400, seed=s + 7)
    alb = np.repeat(paint[..., None], 3, -1) * np.array([0.97, 1.0, 0.98])[None, None, :]
    alb *= (0.74 + 0.26 * prof)[..., None]                          # dirt gathers in the valleys
    rustc = np.array([0.50, 0.26, 0.12])[None, None, :]
    alb = alb * (1 - 0.7 * rust[..., None]) + rustc * 0.9 * rust[..., None] * (0.7 + 0.3 * prof[..., None])
    alb = alb * (1 - 0.5 * scr[..., None]) + 0.35 * scr[..., None]
    alb = np.clip(alb * 1.05, 0, 1)
    save(name, "Albedo", alb)
    h = prof * 10.0 + dent + 1.0 * noise(N, N, 0.6, 30, 400, seed=s + 8) - 1.8 * rust
    save(name, "Normal", normal_from_height(h, 0.55))
    save(name, "Mask", np.clip(0.42 + 0.2 * prof - 0.35 * rust, 0, 1))


# ------------------------------------------------------------------ window interiors atlas
CW, CH = 512, 680
COLS, ROWS = 4, 3
CELLS = ["BlindsHalf", "CurtainsRed", "CurtainsBlue", "CurtainsCream", "DarkFurniture", "ACUnit",
         "Plant", "LampLit", "EmptyDark", "BlindsClosed", "TVRoom", "CurtainsGreenPlant"]


def cell_uv(i):
    """UV rect (u0, v0, u1, v1) of cell i in Blender's bottom-up UV space."""
    c, r = i % COLS, i // COLS
    tw, th = COLS * CW, 2048
    u0, u1 = c * CW / tw, (c + 1) * CW / tw
    v1 = 1 - r * CH / th
    v0 = 1 - (r + 1) * CH / th
    return (u0 + 0.002, v0 + 0.002, u1 - 0.002, v1 - 0.002)


class Canvas:
    def __init__(self, rng):
        self.rng = rng
        y, x = np.mgrid[0:CH, 0:CW].astype(np.float32)
        self.X, self.Y = x / CW, y / CH
        self.img = np.zeros((CH, CW, 3), np.float32)
        self.em = np.zeros((CH, CW, 3), np.float32)

    def fill(self, c):
        self.img[:] = c

    def mask_rect(self, x0, y0, x1, y1, soft=0.004):
        return (sstep(self.X, x0, x0 + soft) * (1 - sstep(self.X, x1 - soft, x1)) *
                sstep(self.Y, y0, y0 + soft) * (1 - sstep(self.Y, y1 - soft, y1)))

    def mask_ell(self, cx, cy, rx, ry, soft=0.02):
        d = np.sqrt(((self.X - cx) / rx) ** 2 + ((self.Y - cy) / ry) ** 2)
        return 1 - sstep(d, 1 - soft, 1)

    def paint(self, m, c, a=1.0):
        c = np.array(c, np.float32)
        self.img = self.img * (1 - m[..., None] * a) + c[None, None, :] * (m[..., None] * a)

    def mul(self, m, k):
        k = np.asarray(k, np.float32)
        if k.ndim == 2:
            k = k[..., None]
        self.img = self.img * (1 - m[..., None]) + self.img * k * m[..., None]

    def glow(self, m, c, a=1.0):
        self.em = np.maximum(self.em, m[..., None] * np.array(c, np.float32)[None, None, :] * a)


def room_base(cv, wall, floor, ceil=None, lit=0.0):
    """Back wall (top 80 %), floor, ceiling shading and a soft vignette."""
    cv.fill(wall)
    cv.paint(cv.mask_rect(0, 0.78, 1, 1), floor)
    cv.mul(cv.mask_rect(0, 0, 1, 0.10), 0.7)
    vig = 1 - 0.35 * (((cv.X - 0.5) * 1.6) ** 2 + ((cv.Y - 0.5)) ** 2)
    cv.img *= np.clip(vig, 0.4, 1)[..., None]


def folds(cv, x0, x1, n, phase=0.0):
    u = (cv.X - x0) / (x1 - x0)
    return 0.55 + 0.45 * np.sin(u * n * 2 * np.pi + phase + cv.Y * 1.5)


def draw_cell(i, rng):
    cv = Canvas(rng)
    kind = CELLS[i]
    if kind == "BlindsHalf":
        room_base(cv, (0.16, 0.14, 0.12), (0.08, 0.06, 0.05))
        top = 0.52
        m = cv.mask_rect(0.02, 0, 0.98, top)
        slat = 0.5 + 0.5 * np.sin(cv.Y * 2 * np.pi * 26)
        cv.paint(m, (0.70, 0.66, 0.56))
        cv.mul(m, 0.78 + 0.22 * slat)
        cv.paint(cv.mask_rect(0.02, top, 0.98, top + 0.018), (0.45, 0.42, 0.36))
        cv.paint(cv.mask_rect(0.49, top + 0.018, 0.51, top + 0.16), (0.6, 0.57, 0.5))   # pull cord
        cv.paint(cv.mask_ell(0.5, top + 0.17, 0.018, 0.012), (0.7, 0.66, 0.6))
        cv.mul(cv.mask_rect(0.0, top, 1.0, 1.0), 0.8)
        cv.paint(cv.mask_rect(0.62, 0.62, 0.9, 0.9), (0.09, 0.075, 0.065), 0.9)         # dark furniture
    elif kind in ("CurtainsRed", "CurtainsBlue", "CurtainsCream", "CurtainsGreenPlant"):
        col = {"CurtainsRed": (0.50, 0.12, 0.10), "CurtainsBlue": (0.18, 0.28, 0.50),
               "CurtainsCream": (0.80, 0.72, 0.54), "CurtainsGreenPlant": (0.22, 0.42, 0.24)}[kind]
        room_base(cv, (0.13, 0.12, 0.11), (0.07, 0.055, 0.05))
        gap = 0.10 if kind != "CurtainsCream" else 0.22
        for side in (0, 1):
            if side == 0:
                x0, x1 = 0.0, 0.5 - gap / 2 * 0.0 - gap / 2 + 0.0
                x1 = 0.5 - gap / 2
            else:
                x0, x1 = 0.5 + gap / 2, 1.0
            m = cv.mask_rect(x0, 0.0, x1, 0.93 if kind != "CurtainsCream" else 0.70)
            f = folds(cv, x0, x1, 4, 0.5 + side)
            cv.paint(m, col)
            cv.mul(m, 0.55 + 0.6 * f)
        # rod and tie
        cv.paint(cv.mask_rect(0, 0.0, 1, 0.025), (0.12, 0.1, 0.08))
        cv.paint(cv.mask_rect(0.5 - gap / 2 - 0.02, 0.6, 0.5 - gap / 2 + 0.03, 0.64), tuple(np.array(col) * 0.5))
        if kind == "CurtainsCream":
            cv.paint(cv.mask_rect(0.5 - gap / 2, 0.0, 0.5 + gap / 2, 0.70), (0.30, 0.27, 0.2), 0.8)
            cv.paint(cv.mask_rect(0.3, 0.72, 0.8, 0.92), (0.09, 0.075, 0.065), 0.8)
        if kind == "CurtainsGreenPlant":
            cv.paint(cv.mask_rect(0.50 - gap / 2, 0.8, 0.5 + gap / 2, 1.0), (0.2, 0.12, 0.08))
            for (cx, cy, rx, ry) in [(0.5, 0.74, 0.07, 0.07), (0.45, 0.68, 0.05, 0.05), (0.56, 0.7, 0.05, 0.06)]:
                cv.paint(cv.mask_ell(cx, cy, rx, ry), (0.1, 0.28, 0.1))
            cv.glow(cv.mask_ell(0.7, 0.45, 0.10, 0.18), (0.8, 0.55, 0.25), 0.2) if False else None
    elif kind == "DarkFurniture":
        room_base(cv, (0.11, 0.10, 0.10), (0.06, 0.05, 0.045))
        # faint window-light wash from the top left
        wash = np.clip(1.0 - (cv.X * 0.8 + cv.Y * 1.0), 0, 1) ** 2
        cv.img += wash[..., None] * np.array([0.06, 0.06, 0.07])[None, None, :]
        sil = (0.035, 0.03, 0.03)
        cv.paint(cv.mask_rect(0.05, 0.15, 0.40, 0.82), sil)                    # bookshelf
        for yy in np.linspace(0.22, 0.76, 6):
            cv.paint(cv.mask_rect(0.06, yy, 0.39, yy + 0.008), (0.1, 0.085, 0.07))
            for xx in np.linspace(0.08, 0.36, 6):
                cv.paint(cv.mask_rect(xx, yy - 0.06, xx + 0.03, yy), (0.06 + 0.03 * rng.random(), 0.05, 0.05))
        cv.paint(cv.mask_rect(0.45, 0.70, 0.98, 0.90), sil)                    # sofa
        cv.paint(cv.mask_rect(0.45, 0.60, 0.52, 0.90), sil)
        cv.paint(cv.mask_rect(0.91, 0.60, 0.98, 0.90), sil)
        cv.paint(cv.mask_rect(0.70, 0.28, 0.72, 0.70), sil)                    # floor lamp
        cv.paint(cv.mask_ell(0.71, 0.27, 0.07, 0.05), (0.07, 0.06, 0.05))
    elif kind == "ACUnit":
        room_base(cv, (0.17, 0.17, 0.17), (0.1, 0.1, 0.1))
        cv.paint(cv.mask_rect(0.0, 0.0, 1.0, 0.45), (0.07, 0.07, 0.09), 0.8)
        m = cv.mask_rect(0.03, 0.42, 0.97, 0.98)
        cv.paint(m, (0.58, 0.58, 0.56))
        cv.paint(cv.mask_rect(0.03, 0.42, 0.97, 0.47), (0.78, 0.78, 0.76))
        grille = cv.mask_rect(0.12, 0.55, 0.88, 0.90)
        cv.paint(grille, (0.20, 0.20, 0.21))
        for k in range(10):
            cv.paint(cv.mask_rect(0.12, 0.57 + k * 0.032, 0.88, 0.57 + k * 0.032 + 0.012), (0.66, 0.66, 0.64))
        cv.mul(cv.mask_rect(0.03, 0.93, 0.97, 0.98), 0.6)
        cv.paint(cv.mask_rect(0.0, 0.0, 0.03, 1), (0.05, 0.05, 0.05))
        cv.paint(cv.mask_rect(0.97, 0.0, 1.0, 1), (0.05, 0.05, 0.05))
    elif kind == "Plant":
        room_base(cv, (0.19, 0.18, 0.15), (0.09, 0.07, 0.06))
        sheer = 0.5 + 0.5 * np.sin(cv.X * 2 * np.pi * 7)
        cv.paint(cv.mask_rect(0, 0, 1, 0.8), (0.55, 0.52, 0.44), 0.25 + 0.1 * sheer.mean())
        cv.paint(cv.mask_rect(0.34, 0.80, 0.66, 0.97), (0.22, 0.10, 0.07))
        for k in range(11):
            a = -1.2 + k * 0.24
            cx = 0.5 + math.sin(a) * 0.21
            cy = 0.78 - math.cos(a) * 0.28 * (0.6 + 0.4 * rng.random())
            cv.paint(cv.mask_ell(cx, cy, 0.11, 0.045 + 0.02 * rng.random()), (0.08 + 0.05 * rng.random(), 0.25 + 0.12 * rng.random(), 0.07))
        cv.paint(cv.mask_rect(0.495, 0.46, 0.505, 0.82), (0.06, 0.18, 0.06))
    elif kind == "LampLit":
        room_base(cv, (0.55, 0.38, 0.22), (0.25, 0.14, 0.08))
        glow = np.clip(1.0 - np.sqrt((cv.X - 0.62) ** 2 * 1.1 + (cv.Y - 0.36) ** 2), 0, 1)
        cv.img *= (0.45 + 0.9 * glow)[..., None]
        cv.paint(cv.mask_rect(0.05, 0.15, 0.30, 0.50), (0.35, 0.22, 0.14))                # picture
        cv.paint(cv.mask_rect(0.07, 0.17, 0.28, 0.48), (0.5, 0.45, 0.35))
        cv.paint(cv.mask_rect(0.55, 0.55, 0.57, 0.84), (0.2, 0.12, 0.08))                # lamp stem
        cv.paint(cv.mask_ell(0.56, 0.33, 0.10, 0.075), (1.0, 0.82, 0.5))                  # shade
        cv.paint(cv.mask_rect(0.50, 0.30, 0.62, 0.40), (1.0, 0.85, 0.55))
        cv.paint(cv.mask_rect(0.10, 0.74, 0.95, 0.92), (0.16, 0.10, 0.07))                # sofa/cabinet
        cv.glow(cv.mask_ell(0.56, 0.35, 0.16, 0.20, 0.9) * 0.35, (1.0, 0.7, 0.35))
        cv.glow(cv.mask_ell(0.56, 0.33, 0.10, 0.075), (1.0, 0.78, 0.4), 1.0)
        cv.glow(cv.mask_rect(0.0, 0.0, 1, 0.78) * (0.15 + 0.35 * glow), (1.0, 0.62, 0.3), 0.7)
    elif kind == "EmptyDark":
        room_base(cv, (0.07, 0.065, 0.065), (0.035, 0.03, 0.03))
        cv.paint(cv.mask_rect(0.72, 0.18, 0.96, 0.80), (0.05, 0.045, 0.045))             # door frame
        cv.paint(cv.mask_rect(0.74, 0.20, 0.94, 0.80), (0.03, 0.028, 0.028))
        cv.paint(cv.mask_rect(0.0, 0.76, 1.0, 0.78), (0.12, 0.11, 0.10))                  # skirting
    elif kind == "BlindsClosed":
        room_base(cv, (0.2, 0.2, 0.2), (0.1, 0.1, 0.1))
        m = cv.mask_rect(0.02, 0.0, 0.98, 0.96)
        slat = 0.5 + 0.5 * np.sin(cv.Y * 2 * np.pi * 36)
        cv.paint(m, (0.52, 0.5, 0.46))
        cv.mul(m, 0.62 + 0.38 * slat)
        cv.paint(cv.mask_rect(0.02, 0.96, 0.98, 0.985), (0.3, 0.29, 0.27))
        cv.mul(cv.mask_rect(0.0, 0.0, 1, 1) * np.clip(cv.X * 0 + (cv.Y - 0.3), 0, 1), 0.85)
    elif kind == "TVRoom":
        room_base(cv, (0.06, 0.065, 0.08), (0.035, 0.035, 0.045))
        cv.paint(cv.mask_rect(0.18, 0.38, 0.62, 0.62), (0.02, 0.02, 0.03))
        cv.paint(cv.mask_rect(0.20, 0.40, 0.60, 0.60), (0.35, 0.5, 0.75))
        cv.glow(cv.mask_rect(0.20, 0.40, 0.60, 0.60), (0.35, 0.55, 0.9), 0.55)
        cv.glow(cv.mask_ell(0.4, 0.5, 0.5, 0.5, 0.95) * 0.5, (0.2, 0.3, 0.55), 0.15)
        cv.paint(cv.mask_rect(0.55, 0.72, 1.0, 0.92), (0.04, 0.04, 0.05))                 # sofa back-light
        cv.paint(cv.mask_ell(0.72, 0.68, 0.06, 0.07), (0.05, 0.045, 0.05))                # head silhouette
    # film grain so it never looks flat
    cv.img += (rng.random((CH, CW, 1)).astype(np.float32) - 0.5) * 0.02
    return np.clip(cv.img, 0, 1), np.clip(cv.em, 0, 1)


def make_window_atlas():
    rng = np.random.default_rng(31)
    A = np.zeros((2048, COLS * CW, 3), np.float32)
    E = np.zeros_like(A)
    A[:] = (0.03, 0.03, 0.03)
    for i in range(len(CELLS)):
        c, r = i % COLS, i // COLS
        a, e = draw_cell(i, rng)
        A[r * CH:(r + 1) * CH, c * CW:(c + 1) * CW] = a
        E[r * CH:(r + 1) * CH, c * CW:(c + 1) * CW] = e
    save("WindowInteriors", "Albedo", A)
    save("WindowInteriors", "Emission", E, srgb=True)


# ------------------------------------------------------------------ painted side ads
ADS = [
    ("STERLING THREAD CO.", "FINE COTTONS & LINENS", "EST. 1911", (0.62, 0.15, 0.11), (0.93, 0.88, 0.74)),
    ("KESTREL BOOT & SHOE", "RESOLED WHILE YOU WAIT", "FIFTH & MAPLE", (0.13, 0.25, 0.38), (0.92, 0.88, 0.76)),
    ("MERIDIAN TONIC", "FOR THE WEARY TRAVELLER", "SOLD HERE", (0.85, 0.72, 0.35), (0.20, 0.12, 0.08)),
    ("HOTEL ARDEN", "ROOMS BY THE WEEK", "BATHS - TELEPHONE", (0.18, 0.34, 0.24), (0.93, 0.9, 0.78)),
]


def render_text_masks(lines, size):
    """Render each (text, rel_height) to a coverage mask with Workbench; returns list of numpy (h, w)."""
    sc = bpy.context.scene
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    out = []
    W, H = size
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x, sc.render.resolution_y = W, H
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'SINGLE'
    sc.display.shading.single_color = (1, 1, 1)
    sc.display.render_aa = '8'
    w = bpy.data.worlds.new("TW"); w.color = (0, 0, 0); sc.world = w
    cam = bpy.data.objects.new("tcam", bpy.data.cameras.new("tcam"))
    sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.location = (0, 0, 5)
    sc.camera = cam
    for text, _ in lines:
        cu = bpy.data.curves.new("t", 'FONT')
        cu.body = text
        cu.align_x = 'CENTER'; cu.align_y = 'CENTER'
        cu.offset = 0.035
        ob = bpy.data.objects.new("t", cu)
        sc.collection.objects.link(ob)
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.convert(target='MESH')
        ob = bpy.context.view_layer.objects.active
        bpy.context.view_layer.update()
        wd = max(ob.dimensions.x, 1e-3)
        hd = max(ob.dimensions.y, 1e-3)
        cam.data.ortho_scale = max(wd * 1.04, hd * 1.04 * W / H)
        sc.render.filepath = os.path.join(bpy.app.tempdir, "txt.png")
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(sc.render.filepath)
        px = np.array(img.pixels[:], np.float32).reshape(H, W, 4)[::-1, :, 0]
        bpy.data.images.remove(img)
        out.append((px, wd / cam.data.ortho_scale))
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
    bpy.data.objects.remove(cam, do_unlink=True)
    bpy.data.worlds.remove(w)
    return out


def make_side_ads():
    CW2, CH2 = 1024, 768
    TW, TH = CW2 - 140, 250
    lines = []
    for a in ADS:
        lines += [(a[0], 1), (a[1], 1), (a[2], 1)]
    masks = render_text_masks(lines, (TW, TH))
    A = np.zeros((2 * CH2, 2 * CW2, 3), np.float32)
    AL = np.zeros((2 * CH2, 2 * CW2), np.float32)
    rng = np.random.default_rng(77)
    for i, (t1, t2, t3, bg, fg) in enumerate(ADS):
        c, r = i % 2, i // 2
        y, x = np.mgrid[0:CH2, 0:CW2].astype(np.float32)
        cell = np.zeros((CH2, CW2, 3), np.float32) + np.array(bg)
        cell *= (0.88 + 0.12 * np.sin(y * 0.002))[..., None]
        edge = np.minimum(np.minimum(x, CW2 - 1 - x), np.minimum(y, CH2 - 1 - y))
        frame = ((edge > 30) & (edge < 46)) | ((edge > 56) & (edge < 62))
        cell[frame] = np.array(fg)
        alpha = (edge > 8).astype(np.float32)
        # text blocks (the mask is rendered at width TW, fit on a 1024 cell)
        def put(mk, cy, scale, color):
            m, fill = mk
            hh, ww = m.shape
            sh = int(hh * scale)
            sw = int(ww * scale)
            # resample by simple stride sampling to keep numpy-only
            yi = (np.arange(sh) / scale).astype(int).clip(0, hh - 1)
            xi = (np.arange(sw) / scale).astype(int).clip(0, ww - 1)
            ms = m[yi][:, xi]
            y0 = int(cy - sh / 2)
            x0 = (CW2 - sw) // 2
            sub = cell[y0:y0 + sh, x0:x0 + sw]
            sub[:] = sub * (1 - ms[..., None]) + np.array(color)[None, None, :] * ms[..., None]
        m1, m2, m3 = masks[i * 3], masks[i * 3 + 1], masks[i * 3 + 2]
        # scale so the headline fills ~88 % of the cell width; sub-lines smaller
        def width_scale(mk, target):
            m = mk[0]
            cols = np.where(m.max(0) > 0.5)[0]
            span = max(cols.max() - cols.min(), 1) if len(cols) else 1
            return target / span, m
        s1, _ = width_scale(m1, CW2 * 0.84)
        s2, _ = width_scale(m2, CW2 * 0.60)
        s3, _ = width_scale(m3, CW2 * 0.34)
        put(m1, 250, s1, fg)
        put(m2, 420, s2, fg)
        put(m3, 540, s3, fg)
        cell[int(CH2 * 0.60) - 6:int(CH2 * 0.60) + 2, 120:CW2 - 120] = np.array(fg)
        # age: flaking paint (alpha holes), fade, grime, brick showing through
        fade = noise(CH2, CW2, 2.6, 1, 14, seed=200 + i)
        flake = sstep(noise(CH2, CW2, 1.6, 4, 120, seed=210 + i) + 0.55 * fade, 0.35, 0.8)
        alpha = alpha * (1 - 0.85 * flake)
        alpha *= 0.82 + 0.1 * fade
        streak = sstep(noise(CH2, CW2, 3.0, 2, 60, ky=6.0, seed=220 + i), 0.0, 0.6)
        cell *= (1 - 0.35 * streak[..., None])
        mean = cell.mean(-1, keepdims=True)
        cell = mean + (cell - mean) * 0.8                # desaturate / fade
        cell = np.clip(cell * 0.92, 0, 1)
        A[r * CH2:(r + 1) * CH2, c * CW2:(c + 1) * CW2] = cell
        AL[r * CH2:(r + 1) * CH2, c * CW2:(c + 1) * CW2] = alpha
    save("SideAds", "Albedo", A, alpha=AL)


def ad_uv(i):
    c, r = i % 2, i // 2
    return (c * 0.5 + 0.002, 1 - (r + 1) * 0.5 + 0.002, (c + 1) * 0.5 - 0.002, 1 - r * 0.5 - 0.002)


def make_all():
    make_brick("BrickRed", 11)
    make_brick("BrickBrown", 12)
    make_brick("BrickTan", 13)
    make_stone()
    make_shutter()
    make_window_atlas()
    make_side_ads()
