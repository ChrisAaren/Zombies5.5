"""Procedural textures for Char_Human (numpy). Albedos are near-neutral except Skin; normals are OpenGL (+Y up)."""
import os
import numpy as np
import bpy

TEX_DIR = r"E:\Unity\Zombies5.5\Assets\Art\Textures\Characters"


def save_png(name, arr, srgb=True):
    os.makedirs(TEX_DIR, exist_ok=True)
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), arr.dtype)], axis=2)
    img = bpy.data.images.new(name, w, h, alpha=False)
    img.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = 'PNG'
    img.save()
    return img


def tile_noise(size, cells, seed, octaves=1, persistence=0.5):
    """Periodic value noise in [0,1], tileable."""
    rng = np.random.RandomState(seed)
    out = np.zeros((size, size), np.float32)
    amp, tot = 1.0, 0.0
    c = cells
    for o in range(octaves):
        g = rng.rand(c, c).astype(np.float32)
        xs = np.arange(size) * c / size
        i0 = np.floor(xs).astype(int); f = xs - i0
        f = f * f * (3 - 2 * f)
        i1 = (i0 + 1) % c
        gx = g[:, i0] * (1 - f)[None, :] + g[:, i1] * f[None, :]          # c x size
        v = gx[i0, :] * (1 - f)[:, None] + gx[i1, :] * f[:, None]
        out += amp * v
        tot += amp
        amp *= persistence
        c *= 2
    return out / tot


def twill(size, period, seed, strength=1.0):
    """3/1 right-hand twill height map (tileable): diagonal ribs from warp-over-three-weft floats."""
    n = size
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    cx = xx * period / n
    cy = yy * period / n
    ci = np.floor(cx).astype(int); cj = np.floor(cy).astype(int)
    fx = cx - ci; fy = cy - cj
    over = ((ci - cj) % 4) != 0                      # warp (vertical thread) on top 3 of 4
    warp = 0.55 + 0.45 * np.cos(np.pi * (fx - 0.5)) ** 1.5          # round across the thread
    weft = 0.40 + 0.35 * np.cos(np.pi * (fy - 0.5)) ** 1.5
    h = np.where(over, warp, weft)
    h = h + 0.07 * (tile_noise(n, 96, seed, 2) - 0.5)
    return h.astype(np.float32) * strength


def height_to_normal(h, strength=2.0, wrap=True):
    if wrap:
        dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
        dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    else:
        dy, dx = np.gradient(h)
    nx, ny = -dx * strength, -dy * strength         # OpenGL: +Y up; rows increase with v
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return n * 0.5 + 0.5


def make_cloth(size=512):
    h = twill(size, 160, 11)
    fib = tile_noise(size, 128, 3) - 0.5
    a = 0.80 + 0.12 * (h - 0.5) + 0.05 * fib + 0.05 * (tile_noise(size, 8, 5, 3) - 0.5)
    alb = np.stack([a, a * 0.995, a * 0.99], -1)
    save_png("Cloth_Albedo", alb, True)
    save_png("Cloth_Normal", height_to_normal(h, 1.8), False)


def make_denim(size=512):
    h = twill(size, 160, 21)
    # warp threads (indigo) vs weft (whiter): strong thread contrast, low-frequency fade + abrasion
    thread = tile_noise(size, 256, 8) - 0.5
    fade = tile_noise(size, 6, 13, 4)
    a = 0.60 + 0.30 * (h - 0.5) + 0.10 * thread + 0.20 * (fade - 0.5)
    alb = np.stack([a * 0.93, a * 0.98, a * 1.06], -1)           # light blue tint
    save_png("Denim_Albedo", alb, True)
    save_png("Denim_Normal", height_to_normal(h * 1.1 + 0.15 * (fade - 0.5), 3.4), False)


def make_leather(size=512):
    g = tile_noise(size, 192, 31, 3, 0.5)
    cell = tile_noise(size, 96, 32, 2)
    crease = np.abs(tile_noise(size, 12, 33, 3) - 0.5)
    h = 0.45 * g + 0.15 * cell - 0.25 * np.exp(-crease * 18) * 0.35
    a = 0.72 + 0.10 * (h - 0.5) + 0.04 * (tile_noise(size, 6, 34, 3) - 0.5)
    alb = np.stack([a, a * 0.98, a * 0.96], -1)
    save_png("Leather_Albedo", alb, True)
    save_png("Leather_Normal", height_to_normal(h, 2.4), False)


def make_eye(size=512):
    """Iris + pupil disc centred, radius 0.5 of the texture (the Iris faces are mapped to this)."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    cx = cy = (size - 1) / 2
    dx, dy = (xx - cx) / (size / 2), (yy - cy) / (size / 2)
    r = np.sqrt(dx * dx + dy * dy)
    ang = np.arctan2(dy, dx)
    rng = np.random.RandomState(5)
    fib = np.zeros_like(r)
    for k in range(3):
        f = 18 + 14 * k
        ph = rng.rand() * 6.28
        fib += np.sin(ang * f + ph + 3 * r) * (0.5 / (k + 1))
    fib = 0.5 + 0.35 * fib
    # colour: hazel-brown with a greener ring near the pupil
    base = np.stack([0.42 + 0.0 * r, 0.31 + 0 * r, 0.16 + 0 * r], -1)
    ring = np.exp(-((r - 0.38) / 0.12) ** 2)[..., None] * np.array([-0.04, 0.05, 0.0])
    col = base * (0.65 + 0.7 * fib)[..., None] + ring
    col *= (0.75 + 0.25 * np.clip((r - 0.2) / 0.5, 0, 1))[..., None]
    limbal = np.clip((r - 0.80) / 0.18, 0, 1)
    col *= (1 - 0.75 * limbal)[..., None]
    pupil = 1 - np.clip((r - 0.30) / 0.03, 0, 1)
    col = col * (1 - pupil)[..., None] + 0.01 * pupil[..., None]
    col = np.where((r > 1.0)[..., None], np.array([0.05, 0.04, 0.03]), col)
    save_png("Eye_Albedo", col, True)


# ------------------------------------------------------------------ skin bake
def rasterize(me, uv_layer, tri_pred, size):
    """Returns pos map (H,W,3), nor map, valid mask for the loop triangles accepted by tri_pred(poly)."""
    pos = np.zeros((size, size, 3), np.float32)
    nor = np.zeros((size, size, 3), np.float32)
    valid = np.zeros((size, size), bool)
    verts = np.array([v.co[:] for v in me.vertices], np.float32)
    vn = np.array([v.normal[:] for v in me.vertices], np.float32)
    uvs = np.zeros((len(me.loops), 2), np.float32)
    uv_layer.data.foreach_get("uv", uvs.ravel())
    me.calc_loop_triangles()
    for lt in me.loop_triangles:
        poly = me.polygons[lt.polygon_index]
        if not tri_pred(poly):
            continue
        li = lt.loops
        vi = lt.vertices
        t = uvs[list(li)] * size
        mn = np.floor(t.min(0)).astype(int); mx = np.ceil(t.max(0)).astype(int)
        x0, y0 = max(mn[0], 0), max(mn[1], 0)
        x1, y1 = min(mx[0], size - 1), min(mx[1], size - 1)
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        a, b, c = t[0], t[1], t[2]
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        l1 = ((b[1] - c[1]) * (gx - c[0]) + (c[0] - b[0]) * (gy - c[1])) / d
        l2 = ((c[1] - a[1]) * (gx - c[0]) + (a[0] - c[0]) * (gy - c[1])) / d
        l3 = 1 - l1 - l2
        m = (l1 >= -0.06) & (l2 >= -0.06) & (l3 >= -0.06)
        if not m.any():
            continue
        P = verts[vi[0]][None, None, :] * l1[..., None] + verts[vi[1]][None, None, :] * l2[..., None] + verts[vi[2]][None, None, :] * l3[..., None]
        Nn = vn[vi[0]][None, None, :] * l1[..., None] + vn[vi[1]][None, None, :] * l2[..., None] + vn[vi[2]][None, None, :] * l3[..., None]
        ys, xs = np.nonzero(m)
        pos[y0 + ys, x0 + xs] = P[ys, xs]
        nor[y0 + ys, x0 + xs] = Nn[ys, xs]
        valid[y0 + ys, x0 + xs] = True
    return pos, nor, valid


def dilate(arr_list, valid, iters=6):
    valid = valid.copy()
    for _ in range(iters):
        nv = valid.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh_valid = np.roll(valid, (dy, dx), (0, 1))
            take = sh_valid & ~nv
            for a in arr_list:
                a[take] = np.roll(a, (dy, dx), (0, 1))[take]
            nv |= take
        valid = nv
    return valid


def G3(P, c, s):
    d = P - np.array(c, np.float32)
    return np.exp(-0.5 * (d * d).sum(-1) / (s * s))


def bake_skin(me, uv_layer, skin_slot, size=2048):
    pred = lambda poly: poly.material_index == skin_slot
    pos, nor, valid = rasterize(me, uv_layer, pred, size)
    valid = dilate([pos, nor], valid, 12)
    P = pos
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    ax = np.abs(x)
    base = np.array([0.790, 0.585, 0.470], np.float32)
    col = np.broadcast_to(base, (size, size, 3)).copy()
    rng = np.random.RandomState(7)
    # 3D mottling: sum of random plane waves
    mott = np.zeros((size, size), np.float32)
    for k in range(7):
        d = rng.randn(3); d /= np.linalg.norm(d)
        f = rng.uniform(70, 260)
        mott += np.sin((P @ d.astype(np.float32)) * f + rng.rand() * 6.28) / 7.0
    uvn = tile_noise(size, 24, 41, 3) - 0.5
    col *= (1 + 0.04 * mott)[..., None]
    col += np.array([0.015, -0.005, -0.012], np.float32) * mott[..., None] * 2
    # redness: cheeks, nose, ears, forehead warmth, knuckles, fingertips
    red = np.zeros((size, size), np.float32)
    for sx in (1, -1):
        red += 0.50 * G3(P, (sx * 0.046, -0.078, 1.676), 0.022)
        red += 0.55 * G3(P, (sx * 0.0175, -0.106, 1.662), 0.0085)       # nose wings
    red += 0.95 * G3(P, (0, -0.117, 1.667), 0.0105)                      # nose tip
    red += 0.35 * G3(P, (0, -0.105, 1.690), 0.020)                       # nose bridge
    for sx in (1, -1):
        red += 0.55 * G3(P, (sx * 0.086, 0.016, 1.688), 0.024)
    red += 0.25 * G3(P, (0, -0.075, 1.755), 0.035)                       # forehead
    for sx in (1, -1):
        for fy in (-0.037, -0.0125, 0.0105, 0.030):
            red += 0.55 * G3(P, (sx * 0.797, fy, 1.452), 0.0085)         # knuckles (dorsal)
            red += 0.30 * G3(P, (sx * 0.836, fy, 1.452), 0.007)          # PIP
        red += 0.4 * G3(P, (sx * 0.735, 0.0, 1.455), 0.02)               # wrist/back of hand
    col[..., 0] += 0.060 * red
    col[..., 1] -= 0.075 * red
    col[..., 2] -= 0.050 * red
    # lips: pinker and slightly darker
    lips = np.clip(G3(P, (0, -0.093, 1.6490), 0.011) * (np.abs(z - 1.6405) < 0.0155) * (np.abs(x) < 0.021), 0, 1)
    lips = np.maximum(lips, G3(P, (0, -0.095, 1.6330), 0.010) * (z < 1.6405) * (np.abs(x) < 0.02))
    lipcol = np.array([0.62, 0.34, 0.33], np.float32)
    col = col * (1 - 0.9 * lips[..., None]) + lipcol * 0.9 * lips[..., None]
    # mouth line darker
    col *= (1 - 0.55 * np.exp(-0.5 * ((z - 1.6405) / 0.0016) ** 2) * (ax < 0.019) * (y < -0.07))[..., None]
    # periorbital shadow, eyelid tint
    for sx in (1, -1):
        sh = G3(P, (sx * 0.032, -0.077, 1.690), 0.013) + 0.6 * G3(P, (sx * 0.0325, -0.074, 1.708), 0.014)
        col[..., 0] -= 0.05 * sh; col[..., 1] -= 0.075 * sh; col[..., 2] -= 0.02 * sh
    # stubble / beard shadow on the lower face
    stub_area = ((z > 1.572) & (z < 1.655) & (ax < 0.075)).astype(np.float32) * np.clip((0.045 - y) / 0.07, 0, 1)
    stub_area *= (1 - 0.9 * lips)
    stub_area *= np.clip((1.655 - z) / 0.012, 0, 1) * np.clip((z - 1.572) / 0.01, 0, 1)
    sn = tile_noise(size, 900, 43)
    stub = stub_area * (0.55 + 0.45 * np.clip((sn - 0.35) / 0.4, 0, 1))
    col *= (1 - 0.10 * stub)[..., None]
    col[..., 2] += 0.012 * stub
    # neck a touch darker; ears lighter rim
    col *= (1 - 0.06 * np.clip((1.585 - z) / 0.06, 0, 1) * (z > 1.50))[..., None]
    # nails: dorsal fingertips
    for sx in (1, -1):
        for fy, x0, x1, hw in ((-0.037, 0.870, 0.903, 0.0062), (-0.0125, 0.880, 0.914, 0.0064), (0.0105, 0.872, 0.903, 0.0060), (0.030, 0.846, 0.872, 0.0053)):
            nail = ((ax > x0) & (ax < x1) & (np.abs(y - fy) < hw) & (z > 1.4435 - (0 if fy != 0.030 else 0.0015))).astype(np.float32)
            col = col * (1 - 0.7 * nail[..., None]) + np.array([0.86, 0.66, 0.60], np.float32) * 0.7 * nail[..., None]
        # thumb nail
        pass
    # fine grain
    col *= (1 + 0.008 * (tile_noise(size, 700, 47) - 0.5))[..., None]
    col = np.clip(col, 0, 1)
    col[~valid] = base * 0.9
    # --- normal map: pores + fine wrinkle noise (UV space; texel density is uniform across the skin)
    pores = tile_noise(size, 800, 51, 1)
    pore = np.clip((pores - 0.70) / 0.30, 0, 1) ** 2
    fine = tile_noise(size, 256, 52, 3, 0.55)
    skin_h = -0.40 * pore + 0.35 * fine + 0.2 * (tile_noise(size, 40, 53, 2) - 0.5)
    # stronger pores on nose/cheeks, fewer on lips and hands
    pore_gain = 0.8 + 0.7 * np.clip(red, 0, 1) - 0.5 * lips
    h = skin_h * pore_gain
    nm = height_to_normal(h, 1.8, wrap=False)
    nm[~valid] = (0.5, 0.5, 1.0)
    save_png("Skin_Albedo", col, True)
    save_png("Skin_Normal", nm, False)
    return valid
