"""Zombies 5.5 - tileable texture generator (numpy; Blender is only used to write PNGs).
Run: blender --background --factory-startup --python make_textures.py
Writes <Name>_Albedo.png / _Normal.png (/ _Mask.png) to Assets/Art/Textures/.
Wrap-around everywhere: noise is FFT-synthesised, patterns use modulo, gradients use np.roll."""
import os
import numpy as np
import bpy

N = 1024
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "Art", "Textures"))
os.makedirs(OUT, exist_ok=True)


def noise(beta=2.0, fmin=1, fmax=None, ax=1.0, ay=1.0, seed=0):
    """Tileable noise in [-1,1], power spectrum ~ f^-beta, band limited [fmin,fmax] (cycles/texture).
    ax/ay > 1 stretch features along that axis (lower frequency)."""
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
    """Gaussian blur (wrapping) via FFT."""
    fy = np.fft.fftfreq(N)[:, None]
    fx = np.fft.rfftfreq(N)[None, :]
    g = np.exp(-2 * (np.pi * px) ** 2 * (fx ** 2 + fy ** 2))
    return np.fft.irfft2(np.fft.rfft2(a) * g, s=(N, N))


def normal_from_height(h, strength):
    """OpenGL (+Y up) tangent-space normal; image row 0 is the top of the texture."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 0.5   # +Y = up in the image
    n = np.stack([-dx * strength, -dy * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def set_mean(a, target):
    a = np.clip(a, 0, None)
    return np.clip(a * (target / a.mean()), 0, 1)


def save(name, suffix, arr):
    """float HxW or HxWx3 in 0..1 -> PNG (Blender images are bottom-up, so flip rows)."""
    arr = np.clip(arr, 0, 1)
    if arr.ndim == 2:
        arr = np.repeat(arr[..., None], 3, -1)
    rgba = np.concatenate([arr, np.ones((N, N, 1))], -1)[::-1].astype(np.float32)
    img = bpy.data.images.new(f"{name}_{suffix}", N, N, alpha=False)
    img.colorspace_settings.name = "sRGB" if suffix == "Albedo" else "Non-Color"
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(OUT, f"{name}_{suffix}.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def tint(g, warm=0.0):
    """Gray -> near-neutral albedo with a tiny warm shift."""
    return np.stack([g * (1 + warm), g, g * (1 - warm)], -1)


def brick():
    rows, per = 26, 9                        # 2 m / 26 courses ~ 77 mm; 9 bricks ~ 222 mm
    ch, cw = N / rows, N / per
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    row = np.floor(y / ch).astype(int) % rows
    xo = x + (row % 2) * cw / 2              # running bond: odd courses shifted half a brick
    col = np.floor(xo / cw).astype(int) % per
    ly, lx = y % ch, xo % cw
    mh, mw = 5.0, 5.0                        # mortar joint thickness in px
    d = np.minimum.reduce([ly - mh / 2, ch - mh / 2 - ly, lx - mw / 2, cw - mw / 2 - lx])
    inb = np.clip(d / 3.0, 0, 1)
    inb = inb * inb * (3 - 2 * inb)          # 0 = mortar (recessed), 1 = brick face
    var = np.random.default_rng(3).random((rows, per))[row, col] * 2 - 1
    grain = noise(1.2, 8, 200, seed=11) * 0.5 + noise(2.0, 1, 40, seed=12) * 0.5
    fine = noise(0.0, 100, 400, seed=13)
    brick_v = 0.78 + var * 0.11 + grain * 0.07 + fine * 0.04
    mortar_v = 0.62 + noise(0.5, 60, 300, seed=14) * 0.07
    alb = mortar_v * (1 - inb) + brick_v * inb
    h = inb * 0.8 + (grain * 0.05 + fine * 0.03) * inb + noise(0.5, 80, 300, seed=15) * 0.03 * (1 - inb)
    save("Brick", "Albedo", tint(set_mean(alb, 0.74), 0.01))
    save("Brick", "Normal", normal_from_height(h, 14))


def plaster():
    h = noise(2.2, 2, 30, seed=21) * 0.5 + noise(1.0, 20, 180, seed=22) * 0.25 + noise(0.0, 150, 480, seed=23) * 0.1
    trowel = noise(3.0, 1, 12, ay=0.6, seed=24)       # soft sweeping trowel marks
    alb = 0.78 + h * 0.07 + trowel * 0.04
    save("Plaster", "Albedo", tint(set_mean(alb, 0.78), 0.005))
    save("Plaster", "Normal", normal_from_height(h * 0.6 + trowel * 0.2, 5))


def concrete():
    grain = noise(0.5, 40, 480, seed=31) * 0.5 + noise(1.5, 4, 60, seed=32) * 0.5
    blotch = noise(3.0, 1, 10, seed=33)
    pores = (noise(0.0, 100, 500, seed=34) > 0.55).astype(np.float32)
    joint = np.zeros((N, N), np.float32)     # slab joints every 1 m = every N/2 px (2 m texture)
    for c in (0, N // 2):
        for w, a in ((0, 1.0), (1, 0.6), (-1, 0.6), (2, 0.25), (-2, 0.25)):
            joint[:, (c + w) % N] = np.maximum(joint[:, (c + w) % N], a)
            joint[(c + w) % N, :] = np.maximum(joint[(c + w) % N, :], a)
    alb = 0.76 + grain * 0.06 + blotch * 0.04 - pores * 0.06 - joint * 0.28
    h = grain * 0.15 + blotch * 0.05 - pores * 0.2 - joint * 0.9
    mask = 0.35 + blotch * 0.08 + grain * 0.05 - joint * 0.15
    save("Concrete", "Albedo", tint(set_mean(alb, 0.72), 0.005))
    save("Concrete", "Normal", normal_from_height(h, 6))
    save("Concrete", "Mask", np.clip(mask, 0, 1))


def asphalt():
    fine = noise(0.0, 120, 500, seed=41)
    agg = blur((noise(0.0, 60, 400, seed=42) > 0.25).astype(np.float32), 0.8)   # aggregate speckle
    dark = noise(2.0, 1, 12, seed=43)
    patches = blur((noise(3.5, 1, 6, seed=44) > 0.35).astype(np.float32), 3)
    crack = np.zeros((N, N), np.float32)     # tileable random-walk cracks with branches
    r = np.random.default_rng(45)

    def walk(p, ang, steps, wobble):
        p = p.copy()
        base = ang                           # mean-reverting heading keeps cracks fairly straight
        for _ in range(steps):
            ang += r.normal() * wobble - (ang - base) * 0.06
            p += np.array([np.cos(ang), np.sin(ang)]) * 2.5
            crack[int(p[1]) % N, int(p[0]) % N] = 1
            if steps > 100 and r.random() < 0.012:
                walk(p, ang + r.normal() * 0.9, 80, 0.2)

    for _ in range(4):
        walk(r.random(2) * N, r.random() * 6.28, 450, 0.18)
    crack = np.clip(blur(crack, 1.0) * 6, 0, 1)
    alb = 0.74 + fine * 0.05 + agg * 0.2 + dark * 0.035 - patches * 0.05 - crack * 0.3
    h = fine * 0.1 + agg * 0.35 - patches * 0.04 - crack * 0.9
    mask = 0.35 + agg * 0.2 + patches * 0.12 - crack * 0.2 + dark * 0.04
    save("Asphalt", "Albedo", tint(set_mean(alb, 0.7), 0.004))
    save("Asphalt", "Normal", normal_from_height(blur(h, 0.6), 5))
    save("Asphalt", "Mask", np.clip(mask, 0, 1))


def wood():
    planks = 6                               # 6 boards across 1 m
    pw = N / planks
    y, x = np.mgrid[0:N, 0:N].astype(np.float32)
    pid = (x // pw).astype(int) % planks
    lx = x % pw
    r = np.random.default_rng(51)
    tone = (r.random(planks) * 2 - 1)[pid]
    off = r.random(planks)[pid]
    g1 = noise(2.0, 1, 10, ax=6.0, ay=6.0, seed=52)           # grain stretched along the board
    g2 = noise(0.5, 6, 90, ay=30.0, seed=53)
    rings = np.sin(g1 * 3.5 + off * 20 + lx * 0.11) * 0.5 + 0.5
    gap = np.clip(1 - np.minimum(lx, pw - lx) / 4.0, 0, 1)   # gaps between boards
    jy = (r.random(planks) * N)[pid]                         # one staggered butt joint per board
    jd = np.abs(((y - jy + N / 2) % N) - N / 2)
    gap = np.maximum(gap, np.clip(1 - jd / 3.0, 0, 1))
    alb = 0.74 + tone * 0.06 + (rings - 0.5) * 0.14 + g2 * 0.07 - gap * 0.42
    h = (rings - 0.5) * 0.12 + g2 * 0.06 - gap * 0.9
    save("Wood", "Albedo", tint(set_mean(alb, 0.72), 0.03))
    save("Wood", "Normal", normal_from_height(h, 7))


def roof():
    stones = noise(0.0, 70, 260, seed=61)
    s2 = noise(0.0, 30, 120, seed=62)
    peb = blur(np.clip((stones - 0.1) * 3, 0, 1) * 0.6 + np.clip((s2 - 0.25) * 3, 0, 1) * 0.4, 0.7)
    tar = noise(3.0, 1, 8, seed=63)
    alb = 0.7 + peb * 0.12 + stones * 0.05 + tar * 0.04
    h = peb * 0.8 + stones * 0.1
    save("Roof", "Albedo", tint(set_mean(alb, 0.72), 0.005))
    save("Roof", "Normal", normal_from_height(h, 5))


for fn in (brick, plaster, concrete, asphalt, wood, roof):
    fn()
    print("made", fn.__name__)
