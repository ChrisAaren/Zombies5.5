"""Clothing relief for the courier: collar, hem, cuffs, folds (displacement) + stitched seams, zip, pockets, laces
(thin ribbon islands laid on the surface with BVH raycasts). Materials are assigned by region at the end."""
import math
import bmesh
import numpy as np
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

HEM_Z = 1.015          # jacket hem line (loop of the cage)
COLLAR_TOP = 1.533     # skin begins above this
CUFF_X = 0.7045        # skin begins beyond this (|x|)
JEAN_HEM = 0.0895      # jeans end / shoe begins
TAG_TOP, TAG_PANTS, TAG_SHOE = 4, 5, 6


def sstep(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def G1(x, c, s):
    return np.exp(-0.5 * ((x - c) / s) ** 2)


def region_of(P):
    """0 skin, 4 top, 5 pants, 6 shoe (vertex positions)."""
    x, y, z = np.abs(P[:, 0]), P[:, 1], P[:, 2]
    r = np.zeros(len(P), int)
    arm = (x > 0.19) & (z > 1.30)
    torso_or_arm = (z > HEM_Z) & (z < COLLAR_TOP) & ~(arm & (x > CUFF_X))
    # hand (beyond the cuff)
    r[torso_or_arm] = TAG_TOP
    r[(z <= HEM_Z) & (z > JEAN_HEM)] = TAG_PANTS
    r[(z <= JEAN_HEM) | ((y < -0.046) & (z < 0.11))] = TAG_SHOE
    r[(x > CUFF_X) & (z > 1.30)] = 0
    r[(z >= COLLAR_TOP)] = 0
    return r


def displace(bm):
    bm.normal_update()
    vs = [v for v in bm.verts if True]
    P = np.array([v.co[:] for v in vs])
    N = np.array([v.normal[:] for v in vs])
    x, y, z = P[:, 0], P[:, 1], P[:, 2]
    ax = np.abs(x)
    reg = region_of(P)
    d = np.zeros(len(P))
    top = reg == TAG_TOP
    pants = reg == TAG_PANTS
    shoe = reg == TAG_SHOE
    torso_zone = top & (ax < 0.24) | (top & (z < 1.30))
    arm_zone = top & (ax > 0.19) & (z > 1.30)

    # --- jacket
    d += top * (0.0045 * np.exp(-0.5 * ((z - (HEM_Z + 0.011)) / 0.0075) ** 2))             # hem lip (roll)
    d += top * (-0.0035 * np.exp(-0.5 * ((z - (HEM_Z + 0.0035)) / 0.0035) ** 2))           # hem stitch groove
    # collar stand (radial offset)
    col = (z > 1.488) & (z < 1.545) & (reg != 0) | ((z > 1.488) & (z < COLLAR_TOP + 0.012) & (ax < 0.12))
    prof = 0.0175 * sstep((z - 1.492) / 0.013) * (1 - sstep((z - 1.521) / 0.014)) + 0.0040 * G1(z, 1.5285, 0.0042)
    side = 1 - sstep((ax - 0.055) / 0.045)
    d += np.where(col, prof * side, 0.0)
    # cuffs
    cuff = arm_zone | ((ax > 0.64) & (ax < 0.70) & (z > 1.30))
    d += cuff * (0.0020 * sstep((ax - 0.683) / 0.003) * (1 - sstep((ax - 0.7000) / 0.004))
                 - 0.0016 * G1(ax, 0.6815, 0.0017)
                 + 0.0009 * np.sin(2 * np.pi * (ax - 0.64) / 0.0085) * sstep((ax - 0.645) / 0.01) * (1 - sstep((ax - 0.678) / 0.006)))
    # elbow creases on the inner side of the bend
    under = np.clip(-N[:, 2], 0, 1)
    d += arm_zone * under * 0.0024 * np.sin(2 * np.pi * (ax - 0.405) / 0.020) * G1(ax, 0.455, 0.040)
    d += arm_zone * 0.0014 * G1(ax, 0.47, 0.03) * np.clip(N[:, 2], 0, 1)
    # underarm / chest drag folds
    d += top * 0.0016 * np.sin(2 * np.pi * (ax - 0.20) / 0.030 + 4 * z) * G1(ax, 0.215, 0.035) * G1(z, 1.37, 0.05)
    # belly + back bunching above the hem
    front = np.clip(-N[:, 1], 0, 1); back = np.clip(N[:, 1], 0, 1)
    d += top * 0.0022 * np.sin(2 * np.pi * (z - 1.05) / 0.040) * G1(z, 1.075, 0.040) * (0.5 * front + 1.0 * back) * (ax < 0.22)
    # chest panel relief: gentle pec/upper-back shaping
    d += top * 0.0035 * G1(z, 1.350, 0.045) * G1(ax, 0.085, 0.045) * front
    d += top * 0.0030 * G1(z, 1.395, 0.050) * G1(ax, 0.07, 0.06) * back
    # --- jeans
    d += pants * (-0.0060 * sstep((z - 0.93) / 0.075))
    kn = pants * ((z > 0.44) & (z < 0.60))
    d += kn * 0.0022 * np.sin(2 * np.pi * (z - 0.52) / 0.034) * G1(z, 0.52, 0.04) * (0.6 * back + 0.4 * front)
    d += pants * 0.0016 * np.sin(2 * np.pi * (z - 0.88) / 0.05 + 3 * x) * G1(z, 0.90, 0.05) * (ax < 0.14) * front
    ank = pants * (z < 0.20)
    d += ank * (0.0048 * np.sin(2 * np.pi * (z - 0.0895) / 0.0225) * np.exp(-(z - 0.0895) / 0.05))
    d += pants * 0.0032 * G1(z, 0.1010, 0.0085)                                        # hem roll
    # --- shoes
    d += shoe * (0.0045 * G1(z, 0.014, 0.010) - 0.0034 * G1(z, 0.0345, 0.0038) + 0.0020 * G1(z, 0.046, 0.006))          # midsole lip + groove
    # --- cloth noise (subtle)
    nz = np.array([noise.noise(Vector((p[0] * 14, p[1] * 14, p[2] * 14))) for p in P])
    d += (top | pants) * 0.0012 * nz
    for v, p, n, dd in zip(vs, P, N, d):
        if dd != 0.0:
            v.co = Vector(p + n * dd)
    return reg


# ------------------------------------------------------------------ ribbons
def _cast(bvh, o, d, maxd=1.0):
    hit = bvh.ray_cast(Vector(o), Vector(d), maxd)
    return hit if hit[0] is not None else None


def ribbon(bm, bvh, tag, tv, rays, widths, lifts, closed=False, flip=False):
    """rays: list of (origin, direction). widths/lifts: lateral offsets and normal lifts for each column."""
    pts = []
    for o, dvec in rays:
        h = _cast(bvh, o, dvec)
        pts.append(h)
    if any(p is None for p in pts):
        pts = [p for p in pts if p is not None]
    if len(pts) < 2:
        return
    rows = []
    n_ = len(pts)
    for i, h in enumerate(pts):
        loc, nor = h[0], h[1]
        a = pts[(i - 1) % n_][0] if (closed or i > 0) else loc
        b = pts[(i + 1) % n_][0] if (closed or i < n_ - 1) else loc
        t = (b - a)
        if t.length < 1e-6:
            t = Vector((0, 0, 1))
        t.normalize()
        s = t.cross(nor)
        if s.length < 1e-6:
            continue
        s.normalize()
        row = []
        lf_row = lifts(i) if callable(lifts) else lifts
        for w, lf in zip(widths, lf_row):
            q = loc + s * w
            h2 = _cast(bvh, q + nor * 0.03, -nor, 0.08)
            base = h2[0] if h2 else loc
            nn = h2[1] if h2 else nor
            row.append(bm.verts.new(base + nn * lf))
        rows.append(row)
    m = len(rows)
    cols = len(widths)
    last = m if closed else m - 1
    for i in range(last):
        r0, r1 = rows[i], rows[(i + 1) % m]
        for j in range(cols - 1):
            q = (r0[j], r1[j], r1[j + 1], r0[j + 1])
            if flip:
                q = q[::-1]
            try:
                f = bm.faces.new(q)
                f[tag] = tv
            except ValueError:
                pass


def line_rays(p0, p1, n, origin_dir, dist=0.5):
    """n+1 rays along p0->p1; each starts `dist` away from the sample along -origin_dir and points along origin_dir."""
    od = Vector(origin_dir).normalized()
    out = []
    for i in range(n + 1):
        t = i / n
        p = Vector(p0).lerp(Vector(p1), t)
        out.append((p - od * dist, od))
    return out


def ring_rays(center, axis, radius_out, n, z_or_x, plane):
    """Closed ring of rays around an axis; plane gives the two in-plane axes."""
    pass


def decorate(bm, tag):
    """Zip, pockets, seams, laces. Returns nothing; adds ribbon faces tagged for materials."""
    bm.normal_update()
    bvh = BVHTree.FromBMesh(bm)
    F = (0, 1, 0)            # ray direction: from the front (-Y) toward +Y
    B = (0, -1, 0)
    # zipper tape + teeth, front centre
    rays = line_rays((0, -0.3, 1.532), (0, -0.3, 1.020), 38, F)
    ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0105, -0.0035, 0.0035, 0.0105], lambda i: [0.0005, 0.0034 if i % 2 == 0 else 0.0018, 0.0034 if i % 2 == 1 else 0.0018, 0.0005])
    # zipper pull
    rays = line_rays((0, -0.3, 1.510), (0, -0.3, 1.470), 4, F)
    ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0055, 0.0055], [0.0060, 0.0060])
    # front placket edge lines (stitched) either side of the zip
    for sx in (1, -1):
        rays = line_rays((sx * 0.031, -0.3, 1.526), (sx * 0.031, -0.3, 1.022), 42, F)
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0012, 0.0012], [0.0010, 0.0010])
    # chest pockets (outline stitched) + hem pockets (angled welt)
    for sx in (1, -1):
        cx = sx * 0.088
        corners = [(cx - 0.036, 1.345), (cx + 0.036, 1.345), (cx + 0.036, 1.292), (cx - 0.036, 1.292)]
        _loop(bm, bvh, tag, TAG_TOP, corners, 7, F, 0.0020, 0.0030)
        flap = [(cx - 0.036, 1.321), (cx + 0.036, 1.321)]
        rays = line_rays((flap[0][0], -0.3, flap[0][1]), (flap[1][0], -0.3, flap[1][1]), 8, F)
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0075, 0.0, 0.0075], [0.0010, 0.0030, 0.0010])
        hx = sx * 0.125
        rays = line_rays((hx - 0.020, -0.3, 1.150), (hx + 0.030, -0.3, 1.075), 10, F)
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0040, 0.0, 0.0040], [0.0010, 0.0028, 0.0010])
    # side seams
    for sx in (1, -1):
        rays = []
        for i in range(36):
            t = i / 35
            z = 1.32 - t * 0.30
            rays.append((Vector((sx * 0.5, 0.012, z)), Vector((-sx, 0, 0))))
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0018, 0.0018], [0.0011, 0.0011])
    # sleeve seams (underside of the arm) and shoulder seams
    for sx in (1, -1):
        rays = []
        for i in range(48):
            t = i / 47
            x = sx * (0.20 + t * 0.475)
            rays.append((Vector((x, 0.02, 1.2)), Vector((0, 0, 1))))
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0016, 0.0016], [0.0010, 0.0010])
        rays = []
        for i in range(14):
            t = i / 13
            x = sx * (0.07 + t * 0.12)
            z0 = 0.0
            rays.append((Vector((x, 0.008, 1.62)), Vector((0, 0, -1))))
        ribbon(bm, bvh, tag, TAG_TOP, rays, [-0.0016, 0.0016], [0.0011, 0.0011])
    # cuff stitch rings + hem stitch + collar top stitch
    for sx in (1, -1):
        for xr in (0.690, 0.6745):
            _ring_x(bm, bvh, tag, TAG_TOP, sx * xr, 1.442, 0.010, 20, 0.0010)
    _ring_z(bm, bvh, tag, TAG_TOP, HEM_Z + 0.0105, 24, 0.0010, "torso")
    _ring_z(bm, bvh, tag, TAG_TOP, 1.518, 14, 0.0010, "neck")
    # jeans: out-seams, hem stitches
    for sx in (1, -1):
        rays = []
        for i in range(60):
            t = i / 59
            z = 0.99 - t * 0.89
            rays.append((Vector((sx * 0.5, 0.0, z)), Vector((-sx, 0, 0))))
        ribbon(bm, bvh, tag, TAG_PANTS, rays, [-0.0022, 0.0022], [0.0011, 0.0011])
        _ring_z(bm, bvh, tag, TAG_PANTS, 0.0990, 20, 0.0009, "leg%d" % sx)
    # hip pocket curves on the jeans front
    for sx in (1, -1):
        pts = [(sx * 0.050, 0.995), (sx * 0.062, 0.955), (sx * 0.088, 0.925), (sx * 0.130, 0.915)]
        rays = []
        for i in range(12):
            t = i / 11
            k = t * 3
            j = min(int(k), 2)
            f = k - j
            p = (pts[j][0] * (1 - f) + pts[j + 1][0] * f, pts[j][1] * (1 - f) + pts[j + 1][1] * f)
            rays.append((Vector((p[0], -0.3, p[1])), Vector(F)))
        ribbon(bm, bvh, tag, TAG_PANTS, rays, [-0.0012, 0.0012], [0.0009, 0.0009])
    # shoes: laces across the instep, eyelet rows, toe-cap seam
    for sx in (1, -1):
        x0 = sx * 0.096
        for i in range(6):
            y = -0.058 - i * 0.0165
            rays = []
            for k in range(9):
                u = (k / 8 - 0.5) * 2 * 0.026
                rays.append((Vector((x0 + u, y, 0.30)), Vector((0, 0, -1))))
            ribbon(bm, bvh, tag, TAG_SHOE, rays, [-0.0040, -0.0015, 0.0015, 0.0040], [0.0008, 0.0030, 0.0030, 0.0008])
        for side in (-1, 1):
            rays = [(Vector((x0 + side * 0.026, -0.058 - k * 0.0080, 0.3)), Vector((0, 0, -1))) for k in range(11)]
            ribbon(bm, bvh, tag, TAG_SHOE, rays, [-0.0045, 0.0, 0.0045], [0.0008, 0.0026, 0.0008])
        rays = []
        for k in range(13):
            u = (k / 12 - 0.5) * 2 * 0.036
            rays.append((Vector((x0 + u, -0.138 - 0.010 * (1 - (2 * k / 12 - 1) ** 2) * -1, 0.3)), Vector((0, 0, -1))))
        ribbon(bm, bvh, tag, TAG_SHOE, rays, [-0.0012, 0.0012], [0.0010, 0.0010])


def _loop(bm, bvh, tag, tv, corners, per, direction, lift, width):
    rays = []
    n = len(corners)
    for i in range(n):
        a = corners[i]; b = corners[(i + 1) % n]
        for k in range(per):
            t = k / per
            p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
            rays.append((Vector((p[0], -0.3, p[1])), Vector(direction)))
    ribbon(bm, bvh, tag, tv, rays, [-width / 2, width / 2], [lift, lift], closed=True)


def _ring_x(bm, bvh, tag, tv, x, zc, r, n, lift):
    rays = []
    for i in range(n):
        a = 2 * math.pi * i / n
        d = Vector((0, -math.cos(a), -math.sin(a)))
        rays.append((Vector((x, 0.010, zc)) - d * 0.12, d))
    ribbon(bm, bvh, tag, tv, rays, [-0.0009, 0.0009], [lift, lift], closed=True)


def _ring_z(bm, bvh, tag, tv, z, n, lift, which):
    if which == "torso":
        c = Vector((0, 0.011, z)); R = 0.30
    elif which == "neck":
        c = Vector((0, 0.011, z)); R = 0.09
    else:
        sx = int(which[3:])
        c = Vector((sx * 0.096, 0.017, z)); R = 0.075
    rays = []
    for i in range(n):
        a = 2 * math.pi * i / n
        out = Vector((math.cos(a), math.sin(a), 0))
        rays.append((c + out * R, -out))
    ribbon(bm, bvh, tag, tv, rays, [-0.0009, 0.0009], [lift, lift], closed=True)
