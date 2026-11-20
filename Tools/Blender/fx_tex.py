"""Explosion FX textures, procedural numpy (helper for make_explosion_fx.py, runs inside Blender's python).
Arrays are float32, shape (H, W, 4), row 0 = TOP of the image, straight (non-premultiplied) alpha, sRGB display values 0..1.
"""
import bpy, os, math
import numpy as np

OUT = r"E:\Unity\Zombies5.5\Tools\Blender\staging\explosions"


# ----------------------------------------------------------------------------------------------------- io
def save_png(arr, path):
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), np.float32)], -1)
    arr = np.clip(arr, 0, 1).astype(np.float32)
    name = os.path.basename(path)
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    im.colorspace_settings.name = 'Non-Color'
    im.alpha_mode = 'STRAIGHT'
    im.pixels.foreach_set(arr[::-1].ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


def load_img(path):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)[::-1].copy()


# ----------------------------------------------------------------------------------------------------- noise
def sstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def lerp(a, b, t):
    return a + (b - a) * t


def pnoise(n, fx, fy=None, seed=0):
    """periodic value noise n x n, fx x fy lattice cells, values 0..1"""
    fy = fx if fy is None else fy
    rng = np.random.default_rng(seed)
    g = rng.random((fy, fx)).astype(np.float32)
    x = np.arange(n, dtype=np.float32) / n * fx
    y = np.arange(n, dtype=np.float32) / n * fy
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
    tx = sstep(x - x0); ty = sstep(y - y0)
    x0 %= fx; y0 %= fy; x1 = (x0 + 1) % fx; y1 = (y0 + 1) % fy
    a = g[np.ix_(y0, x0)]; b = g[np.ix_(y0, x1)]; c = g[np.ix_(y1, x0)]; d = g[np.ix_(y1, x1)]
    tx = tx[None, :]; ty = ty[:, None]
    return lerp(lerp(a, b, tx), lerp(c, d, tx), ty)


def fbm(n, f, octaves=5, seed=0, gain=.5, fy=None):
    s = np.zeros((n, n), np.float32); a = 1.0; t = 0.0
    for o in range(octaves):
        s += a * pnoise(n, f * 2 ** o, None if fy is None else fy * 2 ** o, seed + o * 31); t += a; a *= gain
    return s / t


def pnoise1(x, f, seed=0):
    """periodic 1-D value noise; x in [0,1) any shape"""
    rng = np.random.default_rng(seed)
    g = rng.random(f).astype(np.float32)
    xx = x * f
    i0 = np.floor(xx).astype(int); t = sstep(xx - i0)
    return lerp(g[i0 % f], g[(i0 + 1) % f], t)


def fbm1(x, f, octaves=4, seed=0):
    s = 0; a = 1.0; t = 0
    for o in range(octaves):
        s = s + a * pnoise1(x, f * 2 ** o, seed + o * 17); t += a; a *= .5
    return s / t


def worley(n, g, seed=0):
    """periodic cellular distance (F1) on a g x g lattice, returns (f1, cellid)"""
    rng = np.random.default_rng(seed)
    pts = rng.random((g, g, 2)).astype(np.float32)
    ids = rng.random((g, g)).astype(np.float32)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    u = xs / n * g; v = ys / n * g
    iu = np.floor(u).astype(int); iv = np.floor(v).astype(int)
    best = np.full((n, n), 9.0, np.float32); bid = np.zeros((n, n), np.float32)
    for dv in (-1, 0, 1):
        for du in (-1, 0, 1):
            ci = (iu + du) % g; cj = (iv + dv) % g
            px = iu + du + pts[cj, ci, 0]; py = iv + dv + pts[cj, ci, 1]
            d = np.hypot(u - px, v - py)
            m = d < best
            best = np.where(m, d, best); bid = np.where(m, ids[cj, ci], bid)
    return best, bid


def blur(a, k=2, passes=2):
    for _ in range(passes):
        acc = np.zeros_like(a)
        for d in range(-k, k + 1):
            acc += np.roll(a, d, 0)
        a = acc / (2 * k + 1)
        acc = np.zeros_like(a)
        for d in range(-k, k + 1):
            acc += np.roll(a, d, 1)
        a = acc / (2 * k + 1)
    return a


def normal_from_height(h, strength, tile=True):
    """tangent-space normal map (OpenGL / Unity: +Y = up in the image) from a height field (row 0 = top)"""
    if tile:
        dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * .5
        dy_row = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * .5
    else:
        dx = np.gradient(h, axis=1); dy_row = np.gradient(h, axis=0)
    nx = -dx * strength; ny = dy_row * strength; nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l * .5 + .5, ny / l * .5 + .5, nz / l * .5 + .5, np.ones_like(h)], -1).astype(np.float32)


# ----------------------------------------------------------------------------------------------------- scorch
def scorch(n=1024):
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    x = (xs + .5) / n * 2 - 1; y = (ys + .5) / n * 2 - 1
    r = np.hypot(x, y); th = (np.arctan2(y, x) / (2 * np.pi)) % 1.0
    # irregular blast silhouette: radius modulated by angle noise + 2-D warp
    warp = (fbm(n, 4, 5, 3) - .5)
    ang = fbm1(th, 7, 4, 11)
    rr = r * (1 + .55 * (ang - .5) + .45 * warp)
    # soot streaks radiating outward: long thin rays whose length varies per ray
    ray = fbm1(th, 38, 3, 21)
    ray2 = fbm1(th, 91, 2, 5)
    length = .28 + .70 * np.clip(fbm1(th, 13, 3, 41) * 1.6 - .25, 0, 1)
    streak = np.clip((ray * .6 + ray2 * .4 - .30) * 4.2, 0, 1) * np.clip(1 - (rr - .30) / np.maximum(length - .30, .05), 0, 1) * (rr > .22)
    streak *= .55 + .45 * fbm(n, 24, 3, 8)                     # break the rays up
    core = 1 - sstep((rr - .30) / .20)                         # dense core
    mid = 1 - sstep((rr - .44) / .34)                          # blotchy burnt halo
    blotch = fbm(n, 9, 5, 13)
    halo = mid * np.clip((blotch - .28) * 2.4, 0, 1)
    alpha = np.clip(core * 1.0 + halo * .8 + streak * .85, 0, 1)
    # fine fleck dots (debris spatter) outside the main blast
    spat = sstep((fbm(n, 90, 2, 77) - .66) * 9) * (1 - sstep((rr - .55) / .25)) * sstep((rr - .30) / .15)
    alpha = np.clip(alpha + spat * .45, 0, 1)
    # mottle alpha so it is never a flat tint
    alpha *= .78 + .22 * fbm(n, 30, 4, 55)
    # hard fade to 0 at the texture border
    edge = 1 - sstep((np.maximum(np.abs(x), np.abs(y)) - .86) / .13)
    edge *= 1 - sstep((r - .86) / .14)
    alpha *= edge
    # colour: charcoal, warmer / browner where lighter, a hint of grey ash in the core
    v = .018 + .05 * fbm(n, 14, 5, 3) + .04 * halo * fbm(n, 40, 3, 19)
    ash = np.clip((fbm(n, 26, 4, 61) - .62) * 4, 0, 1) * core * .1
    v = v + ash
    rgb = np.stack([v * 1.12, v * 1.0, v * .88], -1)
    # blistered / flaked highlight near edges of the core
    rim = np.exp(-((rr - .34) / .05) ** 2) * fbm(n, 36, 3, 91)
    rgb += (rim * .06)[..., None] * np.array([1, .8, .6])
    arr = np.concatenate([rgb, alpha[..., None]], -1).astype(np.float32)
    # matching normal map: pitted core, raised soot crust, flat elsewhere
    h = (fbm(n, 60, 4, 9) * .6 + fbm(n, 15, 4, 4) * .4) * alpha + .5 * rim
    nm = normal_from_height(h, 5.0, tile=False)
    nm[..., 3] = np.clip(alpha * 1.6, 0, 1)
    return arr, nm


# ----------------------------------------------------------------------------------------------------- shockwave ring
def shockwave(n=512):
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    x = (xs + .5) / n * 2 - 1; y = (ys + .5) / n * 2 - 1
    r = np.hypot(x, y); th = (np.arctan2(y, x) / (2 * np.pi)) % 1.0
    rr = r * (1 + .05 * (fbm1(th, 9, 3, 3) - .5) + .03 * (fbm(n, 6, 3, 2) - .5))
    R = .66
    d = rr - R
    # sharp leading edge outside, long soft tail inside, thin crisp bright line at the front
    prof = np.where(d > 0, np.exp(-(d / .028) ** 2), np.exp(-(-d / .20) ** 1.4)) * .75
    line = np.exp(-(d / .010) ** 2) * .6
    breakup = .8 + .2 * fbm(n, 14, 4, 6)
    a = np.clip((prof + line) * breakup, 0, 1)
    a *= 1 - sstep((r - .90) / .10)
    a *= sstep(rr / .12)
    lum = np.clip(.62 + .38 * np.clip(line / .6, 0, 1) + .1 * (fbm(n, 20, 3, 8) - .5), 0, 1)
    return np.stack([lum, lum * .97, lum * .93, a], -1).astype(np.float32)


# ----------------------------------------------------------------------------------------------------- debris bits
def _poly_sdf(px, py, poly):
    """signed distance (negative inside) of points to polygon"""
    n = len(poly); d = np.full(px.shape, 1e9, np.float32); sgn = np.ones(px.shape, np.float32)
    for i in range(n):
        ax, ay = poly[i]; bx, by = poly[(i + 1) % n]
        ex, ey = bx - ax, by - ay
        t = np.clip(((px - ax) * ex + (py - ay) * ey) / (ex * ex + ey * ey + 1e-9), 0, 1)
        dd = np.hypot(px - (ax + t * ex), py - (ay + t * ey))
        d = np.minimum(d, dd)
        c1 = (ay <= py) != (by <= py)
        xint = ax + (py - ay) * ex / (ey + 1e-12)
        sgn = np.where(c1 & (px < xint), -sgn, sgn)
    return d * sgn


def debris_bits(n=256):
    rng = np.random.default_rng(12)
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    cells = []
    L = np.array([-.55, -.62, .56], np.float32); L /= np.linalg.norm(L)       # light from the top-left
    specs = [  # kind, base colour (linear-ish display), roughness of facets
        ("chunk", (.34, .34, .35), 1.0), ("lump", (.07, .065, .06), .8), ("shard", (.62, .63, .66), .5), ("flake", (.42, .09, .07), .4),
        ("pebble", (.26, .25, .24), .6), ("splinter", (.38, .2, .1), .4), ("curl", (.5, .5, .52), .5), ("slab", (.32, .30, .27), 1.0)]
    for k, (kind, col, rough) in enumerate(specs):
        c = n / 2 + rng.uniform(-6, 6, 2)
        if kind == "shard":
            poly = [(c[0] - 100, c[1] + 6), (c[0] - 20, c[1] - 24), (c[0] + 105, c[1] - 6), (c[0] + 30, c[1] + 22)]
        elif kind == "splinter":
            poly = [(c[0] - 108, c[1] + 4), (c[0] + 100, c[1] - 7), (c[0] + 112, c[1] - 2), (c[0] - 20, c[1] + 11)]
        elif kind == "flake":
            poly = [(c[0] - 70, c[1] - 30), (c[0] + 10, c[1] - 62), (c[0] + 74, c[1] - 12), (c[0] + 40, c[1] + 50), (c[0] - 38, c[1] + 58)]
        elif kind == "curl":
            poly = [(c[0] - 80, c[1] - 14), (c[0] - 30, c[1] - 50), (c[0] + 40, c[1] - 46), (c[0] + 90, c[1] + 4), (c[0] + 30, c[1] + 40), (c[0] - 40, c[1] + 28)]
        else:
            m = rng.integers(7, 10); rad = {"chunk": 84, "lump": 78, "pebble": 52, "slab": 88}[kind]
            a0 = rng.uniform(0, 6.28)
            poly = []
            for i in range(m):
                a = a0 + i / m * 6.283 + rng.uniform(-.18, .18)
                rr = rad * rng.uniform(.62, 1.0) * (1.0 if kind != "slab" else (1.0 if i % 2 == 0 else .75))
                poly.append((c[0] + math.cos(a) * rr * (1.15 if kind == "slab" else 1), c[1] + math.sin(a) * rr * (.7 if kind == "slab" else 1)))
        sd = _poly_sdf(xs + .5, ys + .5, poly)
        sd = sd + (fbm(n, 10, 4, 500 + k) - .5) * 9 + (fbm(n, 40, 2, 600 + k) - .5) * 3
        alpha = np.clip(.5 - sd, 0, 1)
        depth = np.clip(-sd, 0, 40)
        # faceted height: coarse cells with random tilt + soft bevel near the silhouette
        cellg = 5 if kind in ("chunk", "slab", "lump") else 3
        wd, wid = worley(n, cellg * 2, 100 + k)
        h = depth / 40 * (.9 if kind not in ("shard", "splinter", "flake") else .35) + (wid - .5) * .35 * rough + fbm(n, 6, 4, 200 + k) * .18 * rough
        if kind in ("curl",):
            h += .3 * np.sin((xs - c[0]) / 13.0)
        h = blur(h, 2, 2)
        h += fbm(n, 30, 3, 700 + k) * .05
        gx = np.gradient(h, axis=1) * n * .045; gy = np.gradient(h, axis=0) * n * .045
        nx, ny = -gx, -gy
        l = np.sqrt(nx * nx + ny * ny + 1)
        lam = np.clip((nx * L[0] + ny * L[1] + L[2]) / l, 0, 1)
        shade = .16 + .95 * lam ** 1.1
        spec = (np.clip((nx * -.2 + ny * -.3 + 1) / l, 0, 1) ** 28) * (.5 if kind in ("shard", "curl", "splinter") else .12)
        mott = .82 + .36 * fbm(n, 12, 4, 300 + k)
        base = np.array(col, np.float32)[None, None, :] * (shade * mott)[..., None]
        if kind in ("chunk", "slab", "shard", "curl"):                    # soot edges
            sooty = np.clip(1 - depth / 18, 0, 1) * .6 * fbm(n, 9, 3, 400 + k)
            base *= (1 - sooty)[..., None]
        rgb = np.clip(base + spec[..., None], 0, 1)
        # darker thin rim to separate from backgrounds
        rim = np.clip(1 - depth / 3.0, 0, 1) * .5
        rgb *= (1 - rim)[..., None]
        cells.append(np.concatenate([rgb, alpha[..., None]], -1))
    row0 = np.concatenate(cells[:4], 1); row1 = np.concatenate(cells[4:], 1)
    return np.concatenate([row0, row1], 0).astype(np.float32)


# ----------------------------------------------------------------------------------------------------- spark streak
def spark_streak(w=256, h=64):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    x = (xs + .5) / w; y = ((ys + .5) / h - .5) * 2
    # head at the right (x ~ .86), tail fades to the left
    t = np.clip((.90 - x) / .82, 0, 1)                      # 0 at head, 1 at tail end
    wid = .07 + .30 * (1 - t) ** 1.6 * 0 + .035 + .02 * (1 - t)      # thin line, slightly fatter at the head
    wid = .10 * (1 - t * .85)
    inten = np.exp(-t * 3.2) * (1 - sstep((x - .90) / .09))
    jit = .85 + .3 * pnoise1(x, 18, 3)[None, :] if False else 1.0
    core = np.exp(-(y / (wid * .55)) ** 2) * inten
    glow = np.exp(-(y / (wid * 2.6)) ** 2) * inten * .35
    head = np.exp(-(((x - .88) / .035) ** 2 + (y / .34) ** 2))
    v = np.clip(core + glow * .8 + head * .9, 0, 1)
    tail_fade = sstep((x - .02) / .18)                       # nothing at the left border
    a = np.clip(v * 1.15, 0, 1) * tail_fade
    hot = np.clip(core * 1.2 + head, 0, 1)
    rgb = np.stack([np.ones_like(v), .55 + .45 * hot, .12 + .70 * hot ** 1.5 * (1 - t)], -1)
    rgb = np.clip(rgb, 0, 1)
    return np.concatenate([rgb, a[..., None]], -1).astype(np.float32)


# ----------------------------------------------------------------------------------------------------- charred metal
def charred(n=1024):
    h_scale = 1.0
    big = fbm(n, 3, 5, 1)                                    # low-frequency soot / paint-loss mask
    mid = fbm(n, 12, 5, 2)
    fine = fbm(n, 64, 3, 3)
    soot = np.clip((big - .30) * 2.0, 0, 1)
    # blisters: cellular domes of burnt paint
    d1, cid = worley(n, 24, 4)
    d2, cid2 = worley(n, 52, 5)
    bl1 = np.clip(1 - d1 / (.62 + .3 * cid), 0, 1) ** 1.3
    bl2 = np.clip(1 - d2 / (.55 + .35 * cid2), 0, 1) ** 1.3
    blister_mask = sstep((mid - .38) * 4.0)
    blisters = np.maximum(bl1 * blister_mask, bl2 * sstep((fbm(n, 7, 3, 6) - .45) * 4))
    # flaked areas expose bare steel / rust
    flake = sstep((fbm(n, 9, 5, 7) - .58) * 6)
    rust_bloom = sstep((fbm(n, 5, 6, 8) - .50) * 4) * (.6 + .4 * fine)
    # scratches (periodic streaks)
    scr = np.zeros((n, n), np.float32)
    # colour layers
    paint = np.array([.12, .11, .10], np.float32)         # charred paint
    greyp = np.array([.30, .30, .30], np.float32)           # surviving grey primer / scorched paint
    sootc = np.array([.018, .016, .015], np.float32)
    steel = np.array([.36, .35, .34], np.float32)
    rust = np.array([.34, .15, .06], np.float32)
    rust_d = np.array([.18, .07, .03], np.float32)
    col = paint[None, None] * (.7 + .6 * mid)[..., None]
    col = lerp(col, greyp[None, None] * (.6 + .8 * fine)[..., None], (sstep((big - .55) * 3) * .75 * (1 - soot * .4))[..., None])
    col = lerp(col, sootc[None, None] * (.6 + .8 * fine)[..., None], (soot * .85)[..., None])
    col = col * (1 + .22 * (blisters - .3))[..., None]       # blister tops catch a little light
    rcol = lerp(rust_d[None, None], rust[None, None], fine[..., None])
    col = lerp(col, rcol, (flake * .8 + rust_bloom * .65 * (1 - soot * .5)).clip(0, 1)[..., None])
    col = lerp(col, steel[None, None] * (.5 + .7 * fine)[..., None], (flake * (1 - rust_bloom) * .55).clip(0, 1)[..., None])
    col += (scr * .09)[..., None]
    # soot rubs darker in crevices (between blisters)
    col *= (.72 + .28 * np.clip(blisters * 1.2, 0, 1) + .0)[..., None] * (1 - .18 * (1 - blisters) * blister_mask)[..., None]
    alb = np.clip(col, 0, 1) ** (1 / 1.0)
    alb = np.concatenate([alb, np.ones((n, n, 1), np.float32)], -1)
    # height -> normal
    hgt = blisters * .55 * (1 - flake) + fbm(n, 40, 4, 9) * .12 + flake * -.25 + rust_bloom * (fbm(n, 110, 2, 12) * .18) - scr * .08 + mid * .08
    nm = normal_from_height(blur(hgt, 1, 1), 5.0, tile=True)
    return alb.astype(np.float32), nm


def run_textures():
    os.makedirs(OUT, exist_ok=True)
    import time
    t0 = time.time()
    sc, scn = scorch(1024)
    save_png(sc, os.path.join(OUT, "FX_Scorch.png")); save_png(scn, os.path.join(OUT, "FX_Scorch_Normal.png"))
    save_png(shockwave(512), os.path.join(OUT, "FX_ShockwaveRing.png"))
    save_png(debris_bits(256), os.path.join(OUT, "FX_DebrisBits_4x2.png"))
    save_png(spark_streak(256, 64), os.path.join(OUT, "FX_SparkStreak.png"))
    a, nrm = charred(1024)
    save_png(a, os.path.join(OUT, "FX_Charred_Albedo.png")); save_png(nrm, os.path.join(OUT, "FX_Charred_Normal.png"))
    print("TEXTURES done in %.1fs" % (time.time() - t0))
