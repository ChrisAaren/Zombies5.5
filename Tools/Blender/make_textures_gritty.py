"""Zombies 5.5 - gritty weathered texture sets (numpy; Blender only writes PNGs).
Run: blender --background --factory-startup --python make_textures_gritty.py
Per set: <Name>_Albedo.png, <Name>_Normal.png (OpenGL), and for materials with a response map
<Name>_Mask.png (R = smoothness, contract format) plus <Name>_MS.png, the same data packed the way
URP Lit wants it (R = metallic, A = smoothness) so Unity can plug it into the metallic map slot.
Albedo is near-neutral (Unity tints it), mean value ~0.55-0.7. Everything wraps."""
import os
import numpy as np
import bpy

N = 1024
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Art", "Textures"))
os.makedirs(OUT, exist_ok=True)


def noise(beta=2.0, fmin=1, fmax=None, ax=1.0, ay=1.0, seed=0):
    r = np.random.default_rng(seed)
    fy = np.fft.fftfreq(N)[:, None] * N / ay
    fx = np.fft.rfftfreq(N)[None, :] * N / ax
    f = np.sqrt(fx ** 2 + fy ** 2)
    amp = np.where(f >= fmin, np.maximum(f, 1e-6) ** (-beta / 2), 0.0)
    if fmax:
        amp = amp * (f <= fmax)
    spec = amp * (r.normal(size=f.shape) + 1j * r.normal(size=f.shape))
    n = np.fft.irfft2(spec, s=(N, N))
    return n / (np.abs(n).max() + 1e-9)


def blur(a, px):
    fy = np.fft.fftfreq(N)[:, None]
    fx = np.fft.rfftfreq(N)[None, :]
    g = np.exp(-2 * (np.pi * px) ** 2 * (fx ** 2 + fy ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(N, N))


def normal_from_height(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 0.5
    n = np.stack([-dx * strength, -dy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def set_mean(a, target):
    a = np.clip(a, 0, None)
    return np.clip(a * (target / a.mean()), 0, 1)


def sstep(a, lo, hi):
    t = np.clip((a - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def save(name, suffix, arr, alpha=None):
    arr = np.clip(arr, 0, 1)
    if arr.ndim == 2:
        arr = np.repeat(arr[..., None], 3, -1)
    a = np.ones((N, N, 1)) if alpha is None else np.clip(alpha, 0, 1)[..., None]
    rgba = np.concatenate([arr, a], -1)[::-1].astype(np.float32)
    img = bpy.data.images.new(f"{name}_{suffix}", N, N, alpha=alpha is not None)
    img.colorspace_settings.name = "sRGB" if suffix == "Albedo" else "Non-Color"
    if alpha is not None:
        img.alpha_mode = 'CHANNEL_PACKED'
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, f"{name}_{suffix}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def save_mask(name, smooth, metal=None):
    """Mask (R = smoothness) and MS (R = metallic, A = smoothness)."""
    s = np.clip(smooth, 0, 1)
    save(name, "Mask", s)
    m = np.zeros_like(s) if metal is None else np.clip(metal, 0, 1)
    save(name, "MS", np.stack([m, np.zeros_like(m), np.zeros_like(m)], -1), alpha=s)


def tint(g, warm=0.0):
    return np.stack([g * (1 + warm), g, g * (1 - warm)], -1)


def streaks(seed, density=0.5, ay=16.0):
    """Vertical weathering streaks 0..1 (long in y)."""
    s = noise(1.3, 3, 140, ax=0.6, ay=ay, seed=seed)
    s = sstep(s, -0.1 + (1 - density) * 0.5, 0.45 + (1 - density) * 0.3)
    return s * (0.5 + 0.5 * sstep(noise(2.5, 1, 6, seed=seed + 1), -0.4, 0.5))


def cracks(seed, count, steps=450, branch=0.012, width=1.0):
    crack = np.zeros((N, N), np.float32)
    r = np.random.default_rng(seed)

    def walk(p, ang, n, wobble):
        p = p.copy(); base = ang
        for _ in range(n):
            ang += r.normal() * wobble - (ang - base) * 0.06
            p += np.array([np.cos(ang), np.sin(ang)]) * 2.5
            crack[int(p[1]) % N, int(p[0]) % N] = 1
            if n > 100 and r.random() < branch:
                walk(p, ang + r.normal() * 0.9, 80, 0.25)
    for _ in range(count):
        walk(r.random(2) * N, r.random() * 6.28, steps, 0.2)
    return np.clip(blur(crack, width) * 6, 0, 1)


def blobs(seed, thresh, soft=0.15, fmin=1, fmax=8):
    return sstep(noise(3.0, fmin, fmax, seed=seed), thresh, thresh + soft)


def rect_patches(seed, count, wr=(120, 360), hr=(120, 320)):
    """Wrapped rectangles: fill (0/1) and a one-pixel outline (seam)."""
    r = np.random.default_rng(seed)
    fill = np.zeros((N, N), np.float32)
    seam = np.zeros((N, N), np.float32)
    for _ in range(count):
        w, hh = int(r.integers(*wr)), int(r.integers(*hr))
        x0, y0 = int(r.integers(0, N)), int(r.integers(0, N))
        xs = (np.arange(w) + x0) % N
        ys = (np.arange(hh) + y0) % N
        fill[np.ix_(ys, xs)] = r.uniform(0.4, 1.0)
        seam[ys, xs[0]] = 1; seam[ys, xs[-1]] = 1; seam[ys[0], xs] = 1; seam[ys[-1], xs] = 1
    return fill, seam


def brick_worn():
    rows, per = 26, 9
    ch, cw = N / rows, N / per
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = np.floor(y / ch).astype(int) % rows
    xo = x + (row % 2) * cw / 2
    col = np.floor(xo / cw).astype(int) % per
    ly, lx = y % ch, xo % cw
    mh = 6.5
    d = np.minimum.reduce([ly - mh / 2, ch - mh / 2 - ly, lx - mh / 2, cw - mh / 2 - lx])
    rr = np.random.default_rng(7)
    d = d + noise(0.8, 30, 250, seed=101) * 5            # chipped / eroded edges
    inb = sstep(d, 0, 4.5)
    var = rr.random((rows, per))[row, col] * 2 - 1
    spall = (rr.random((rows, per)) > 0.93)[row, col]    # a few spalled bricks (recessed, darker)
    grain = noise(1.2, 8, 200, seed=111) * 0.5 + noise(2.0, 1, 40, seed=112) * 0.5
    fine = noise(0.0, 100, 400, seed=113)
    brick_v = 0.66 + var * 0.15 + grain * 0.09 + fine * 0.05 - spall * 0.14
    mortar_v = 0.5 + noise(0.5, 60, 300, seed=114) * 0.08
    alb = mortar_v * (1 - inb) + brick_v * inb
    soot = streaks(120, 0.6) * 0.38
    efflo = streaks(130, 0.35, ay=10) * sstep(noise(3, 1, 5, seed=132), 0.1, 0.5)
    alb = alb * (1 - soot) + efflo * 0.22
    alb = alb - blobs(140, 0.25, 0.3, 1, 6) * 0.1
    h = (inb * 0.8 + (grain * 0.05 + fine * 0.03) * inb - spall * 0.25 * inb
         + noise(0.5, 80, 300, seed=115) * 0.03 * (1 - inb) - efflo * 0.03)
    save("BrickWorn", "Albedo", tint(set_mean(alb, 0.55), 0.015))
    save("BrickWorn", "Normal", normal_from_height(h, 13))
    sm = 0.22 + inb * 0.05 - soot * 0.15 + efflo * 0.1 + noise(1, 4, 60, seed=150) * 0.04
    save_mask("BrickWorn", sm)


def concrete_stained():
    grain = noise(0.5, 40, 480, seed=201) * 0.5 + noise(1.5, 4, 60, seed=202) * 0.5
    blotch = noise(3.0, 1, 10, seed=203)
    pores = (noise(0.0, 100, 500, seed=204) > 0.5).astype(np.float32)
    joint = np.zeros((N, N), np.float32)
    for c in (0, N // 2):
        for w, a in ((0, 1.0), (1, 0.7), (-1, 0.7), (2, 0.3), (-2, 0.3), (3, 0.1), (-3, 0.1)):
            joint[:, (c + w) % N] = np.maximum(joint[:, (c + w) % N], a)
            joint[(c + w) % N, :] = np.maximum(joint[(c + w) % N, :], a)
    water = streaks(210, 0.55, ay=12) * 0.34
    stain = blobs(220, 0.2, 0.3, 1, 5)
    patch, seam = rect_patches(230, 5)
    cr = cracks(240, 3, 380)
    alb = (0.66 + grain * 0.06 + blotch * 0.05 - pores * 0.07 - joint * 0.32 - water - stain * 0.12
           + (patch - 0.5) * 0.1 * (patch > 0) - seam * 0.12 - cr * 0.22)
    h = grain * 0.15 + blotch * 0.05 - pores * 0.2 - joint * 0.9 - seam * 0.5 - cr * 0.9
    sm = 0.3 + blotch * 0.08 + grain * 0.05 - joint * 0.12 - water * 0.2 + stain * 0.05
    save("ConcreteStained", "Albedo", tint(set_mean(alb, 0.58), 0.005))
    save("ConcreteStained", "Normal", normal_from_height(h, 6))
    save_mask("ConcreteStained", sm)


def asphalt_worn():
    fine = noise(0.0, 120, 500, seed=301)
    agg = blur((noise(0.0, 60, 400, seed=302) > 0.2).astype(np.float32), 0.8)
    dark = noise(2.0, 1, 12, seed=303)
    patches = blur((noise(3.5, 1, 6, seed=304) > 0.3).astype(np.float32), 3)
    cr = cracks(305, 5, 420, 0.012)
    pat, seam = rect_patches(306, 4, (160, 480), (120, 360))
    seam = np.clip(blur(seam, 1.2) * 5, 0, 1)
    pat = blur(pat, 1.0)
    oil = blobs(310, 0.35, 0.18, 2, 14) * sstep(noise(2, 1, 30, seed=311), -0.2, 0.3)
    xx = np.arange(N)[None, :] / N                       # tire polish: two soft wheel-track bands
    track = np.exp(-((xx - 0.28) / 0.06) ** 2) + np.exp(-((xx - 0.72) / 0.06) ** 2)
    track = track * (0.6 + 0.4 * sstep(noise(2, 1, 8, ax=0.5, ay=6, seed=312), -0.3, 0.4))
    alb = (0.62 + fine * 0.05 + agg * 0.18 + dark * 0.045 - patches * 0.05 - cr * 0.2 - pat * 0.07
           - seam * 0.12 - oil * 0.2 + track * 0.035)
    h = fine * 0.1 + agg * 0.35 - patches * 0.04 - cr * 0.9 - seam * 0.35 - pat * 0.04
    sm = 0.3 + agg * 0.12 + patches * 0.1 - cr * 0.2 + dark * 0.04 + pat * 0.1 + oil * 0.32 + track * 0.14
    save("AsphaltWorn", "Albedo", tint(set_mean(alb, 0.55), 0.004))
    save("AsphaltWorn", "Normal", normal_from_height(blur(h, 0.6), 5))
    save_mask("AsphaltWorn", sm)


def plaster_dirty():
    h = noise(2.2, 2, 30, seed=401) * 0.5 + noise(1.0, 20, 180, seed=402) * 0.25 + noise(0.0, 150, 480, seed=403) * 0.1
    trowel = noise(3.0, 1, 12, ay=0.6, seed=404)
    dirt = streaks(410, 0.6) * 0.3 + blobs(411, 0.15, 0.4, 1, 6) * 0.07
    peel = blobs(420, 0.5, 0.08, 5, 28)                  # flaking patches exposing a darker coat
    edge = np.clip(blur(peel, 2) - peel * 0.9, 0, 1)
    cr = cracks(430, 4, 380)
    alb = 0.7 + h * 0.07 + trowel * 0.04 - dirt
    alb = alb * (1 - peel) + (0.56 + noise(1, 20, 200, seed=421) * 0.08) * peel - edge * 0.1 - cr * 0.3
    hh = h * 0.6 + trowel * 0.2 - peel * 0.6 - cr * 0.7
    save("PlasterDirty", "Albedo", tint(set_mean(alb, 0.6), 0.008))
    save("PlasterDirty", "Normal", normal_from_height(hh, 6))
    save_mask("PlasterDirty", 0.22 - dirt * 0.3 + h * 0.03 - peel * 0.05)


def metal_painted():
    brush = noise(0.2, 40, 480, ax=30.0, ay=1.0, seed=501)
    blot = noise(2.5, 1, 10, seed=502)
    chip = blobs(510, 0.48, 0.05, 3, 24) * sstep(noise(0.5, 50, 400, seed=511), -0.5, 0.2)
    rust = chip * sstep(noise(2, 2, 30, seed=512), -0.2, 0.4)
    scratch = np.zeros((N, N), np.float32)
    r = np.random.default_rng(513)
    for _ in range(40):
        x0, y0 = r.random(2) * N
        a = r.random() * 6.28
        L = r.uniform(40, 220)
        t = np.arange(0, L, 1.0)
        scratch[(y0 + np.sin(a) * t).astype(int) % N, (x0 + np.cos(a) * t).astype(int) % N] = 1
    scratch = np.clip(blur(scratch, 0.7) * 3, 0, 1)
    drips = streaks(514, 0.4) * 0.15
    paint = 0.7 + brush * 0.03 + blot * 0.05 - drips
    alb = paint * (1 - chip) + (0.4 - rust * 0.12) * chip - scratch * 0.08
    h = brush * 0.02 - chip * 0.25 - scratch * 0.1
    sm = (0.5 + blot * 0.1 - drips * 0.3) * (1 - chip) + 0.35 * chip - scratch * 0.15
    save("MetalPainted", "Albedo", tint(set_mean(alb, 0.6), 0.01))
    save("MetalPainted", "Normal", normal_from_height(h, 8))
    save_mask("MetalPainted", sm, chip * 0.7)


def grime():
    """Overlay decal. RGB = dark brown-gray soot, A = opacity: dense at the top edge, drips running
    down and fading out. Tiles horizontally only (use it on quads under sills and cornices)."""
    yy = np.arange(N)[:, None] / N
    s = streaks(601, 0.75, ay=5)
    s2 = streaks(602, 0.6, ay=9)
    top = np.clip(1 - yy / 0.35, 0, 1) ** 1.3
    reach = 0.25 + 0.75 * sstep(noise(2.0, 2, 40, ax=0.5, ay=30, seed=603), -0.3, 0.5)
    drips = np.clip(1 - yy / np.maximum(reach, 0.05), 0, 1) * np.maximum(s, s2 * 0.6)
    a = np.clip(top * (0.45 + 0.3 * sstep(noise(2, 3, 60, seed=604), -0.4, 0.4)) + drips * 0.9, 0, 1)
    a = a * (1 - 0.35 * sstep(noise(0, 100, 400, seed=605), 0.2, 0.7))
    a[-8:, :] = 0
    col = np.stack([0.20 + s * 0.04, 0.17 + s * 0.03, 0.14 + s * 0.02], -1)
    save("Grime", "Albedo", col, alpha=np.clip(a * 1.1, 0, 1))
    save("Grime", "Normal", normal_from_height(a * 0.1, 3))


def road_markings():
    """Worn painted line, runs top to bottom, fills x in 0.12..0.88. A = paint opacity."""
    xx = np.arange(N)[None, :] / N
    edge = noise(1.0, 20, 200, ax=0.4, ay=3, seed=701) * 0.012
    inside = sstep(0.5 - np.abs(xx - 0.5 + edge), 0.10, 0.12)
    wear = sstep(noise(2.0, 2, 120, seed=702) + noise(0.5, 80, 400, seed=703) * 0.4, -0.45, -0.1)
    a = inside * (0.35 + 0.65 * wear)
    a = a * (1 - 0.3 * sstep(noise(0, 150, 450, seed=704), 0.3, 0.8))
    v = 0.86 - (1 - wear) * 0.12 + noise(1, 4, 80, seed=705) * 0.04
    save("RoadMarkings", "Albedo", tint(np.clip(v, 0, 1), 0.04), alpha=a)
    save("RoadMarkings", "Normal", normal_from_height(a * 0.05, 2))


for fn in (brick_worn, concrete_stained, asphalt_worn, plaster_dirty, metal_painted, grime, road_markings):
    fn()
    print("made", fn.__name__)
