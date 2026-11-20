"""Four power-up pickups -> Assets/Art/Models/Pickups/Pickup_<Name>.fbx + Assets/Art/Textures/Pickups_Decals.png
Run: blender --background --factory-startup --python make_pickups.py [-- noprev]

Pickup_AmmoCache, Pickup_Shockwave, Pickup_Deadeye, Pickup_Payday.
Pivot = bottom centre, +Z (Blender -Y) forward, 0.45-0.6 m across. Slots: PickupBody, PickupMetal, PickupGlow
(separate faces so Unity can pulse them), PickupDecal (alpha atlas), Paint. Authoring is in Blender coords
(x right, y = -forward, z up). Decal faces carry hand-made UVs into the atlas, everything else is world-meter box UV.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import bl_common
import bl_street2 as b2
from bl_street2 import *
from bl_common import ring_circle, aim

NAMES = ["AmmoCache", "Shockwave", "Deadeye", "Payday"]
DECAL_PNG = os.path.join(TEX, "Pickups_Decals.png")
bl_common.COLORS.update({
    "PickupBody": (.3, .36, .25), "PickupMetal": (.22, .23, .25), "PickupGlow": (1., .75, .2),
    "PickupDecal": (.92, .9, .8)})

# ====================================================================== decal atlas
# 1024 x 1024, 2 columns x 4 rows of 512 x 256 cells; cell i: col = i % 2, row = i // 2 (row 0 at the top)
#  0 "CAL. MIXED / RESUPPLY" stencil   1 "LOT 55-C / CACHE-A" small stencil   2 "ARC-7 / EMITTER"
#  3 hazard stripes (opaque)           4 "DEADEYE / .50 AP" headstamp          5 reticle ticks (36 around a ring, 360 deg = full u)
#  6 banknote face (fictional)         7 bank band label
CW, CH = 512, 256


def cell_uv(i):
    col, row = i % 2, i // 2
    e = .004
    return (col * .5 + e, 1 - (row + 1) / 4 + e, col * .5 + .5 - e, 1 - row / 4 - e)


def make_decals():
    A = np.zeros((1024, 1024, 4), np.float32)
    font = "C:/Windows/Fonts/STENCIL.TTF"
    yy, xx = np.mgrid[0:CH, 0:CW].astype(np.float32)

    def put(i, mask, color, alpha=1.0):
        col, row = i % 2, i // 2
        sl = (slice(row * CH, (row + 1) * CH), slice(col * CW, (col + 1) * CW))
        a = np.clip(mask, 0, 1) * alpha
        A[sl][..., :3] = np.where(a[..., None] > A[sl][..., 3:4], np.array(color, np.float32), A[sl][..., :3])
        A[sl][..., 3] = np.maximum(A[sl][..., 3], a)

    def place(txt, w, h, x0, y0, fill=.86, f=font, ls=1.0):
        m = np.zeros((CH, CW), np.float32)
        m[y0:y0 + h, x0:x0 + w] = text_mask(txt, w, h, fill, f, ls)
        return m

    def rect_line(inset, wd):
        o = (xx >= inset) & (xx < CW - inset) & (yy >= inset) & (yy < CH - inset)
        i = (xx >= inset + wd) & (xx < CW - inset - wd) & (yy >= inset + wd) & (yy < CH - inset - wd)
        return (o & ~i).astype(np.float32)

    cream = (.93, .9, .76)
    # 0 and 1: crate stencils
    m0 = place("CAL. MIXED\nRESUPPLY", 440, 190, 36, 33, .95, ls=.92)
    m0 = np.maximum(m0, rect_line(4, 5))
    put(0, m0, cream)
    m1 = place("LOT 55-C\nCACHE-A", 400, 150, 56, 30, .95, ls=.92)
    bars = ((yy > 196) & (yy < 226) & (xx > 60) & (xx < 452) & ((xx // 14) % 2 == 0)).astype(np.float32)
    put(1, np.maximum(m1, bars), cream)
    # 2: shockwave label
    m2 = place("ARC-7\nEMITTER", 430, 200, 41, 28, .95, ls=.92)
    put(2, np.maximum(m2, rect_line(4, 4)), (.95, .95, .92))
    # 3: hazard stripes, opaque
    stripes = (((xx + yy) // 32) % 2 == 0)
    A3 = np.where(stripes, 1.0, .0)
    put(3, np.ones((CH, CW), np.float32), (.04, .04, .045))
    put(3, A3 * (1 - rect_line(0, 8) * 0), (.95, .72, .05))
    # 4: headstamp
    m4 = place("DEADEYE\n.50 AP", 440, 200, 36, 28, .95, ls=.9)
    put(4, m4, (.22, .12, .03))
    # 5: reticle ticks (u wraps the ring); ticks hang from the outer edge (v = 1)
    t5 = np.zeros((CH, CW), np.float32)
    for k in range(36):
        cx = int((k + .5) * CW / 36)
        major = (k % 3 == 0)
        ln = 100 if major else 56
        wd = 5 if major else 3
        t5[0:ln, cx - wd // 2:cx + wd // 2 + 1] = 1
    t5[150:154, :] = 1                                  # thin inner circle line
    put(5, t5, (.92, .9, .8))
    # 6: banknote face (fictional "FIRST CITY TREASURY")
    ink = (.05, .2, .09)
    ell = ((xx - 256) / 92) ** 2 + ((yy - 128) / 68) ** 2
    ring = ((ell > .8) & (ell < 1.0)).astype(np.float32)
    guil = ((np.sin(yy * .55 + np.sin(xx * .09) * 3.2) > .55) &
            (((xx > 30) & (xx < 118)) | ((xx > 394) & (xx < 482))) & (yy > 44) & (yy < 212)).astype(np.float32)
    n100 = place("100", 110, 70, 201, 93, .9, f="C:/Windows/Fonts/arialbd.ttf")
    c1 = place("100", 64, 34, 28, 22, .95, f="C:/Windows/Fonts/arialbd.ttf")
    c2 = np.roll(np.roll(c1, 480 - 64 - 28 * 2 + 20, 1), 0, 0)
    c3 = np.roll(c1, 256 - 22 * 2 - 34 + 10, 0)
    top = place("FIRST CITY TREASURY", 300, 26, 106, 20, .95, f="C:/Windows/Fonts/arialbd.ttf")
    bot = place("ONE HUNDRED CREDITS", 300, 22, 106, 212, .95, f="C:/Windows/Fonts/arialbd.ttf")
    m6 = np.maximum.reduce([rect_line(7, 4), rect_line(18, 2), ring, guil * .75, n100, c1, c2, c3, top, bot])
    put(6, m6, ink, .92)
    # 7: bank band label (dark on the glowing band)
    m7 = place("FIRST CITY\nTREASURY 10,000", 430, 190, 41, 33, .95, f="C:/Windows/Fonts/arialbd.ttf", ls=.95)
    put(7, m7, (.03, .03, .03))
    # wear: chipped paint and scratches
    n = noise2d(1024, 2.4, seed=7, sc=24.0)
    sc = noise2d(1024, 1.2, seed=9, aniso=(6, 0.5), sc=64.0)
    wear = 1 - .55 * smoothstep(.58, .8, n) - .35 * smoothstep(.8, .9, sc)
    for i in (0, 1, 2, 4):
        col, row = i % 2, i // 2
        sl = (slice(row * CH, (row + 1) * CH), slice(col * CW, (col + 1) * CW))
        A[sl][..., 3] *= wear[sl]
    # bleed colour into transparent texels (mip safety)
    A[..., :3] = np.where(A[..., 3:4] < .01, A[..., :3] * 0 + np.array([.9, .9, .85], np.float32), A[..., :3])
    write_png(DECAL_PNG, A)


# ====================================================================== geometry helpers (Blender coords)
def X(P, c, size, slot, bevel=.006, rot=None):
    P.rbox(c, size, slot, bevel, 1, rot)


def cyl2(P, c, r1, r2, h, slot, n=16, rot=None, bevel=0.0):
    before = P.snapshot()
    P.bm.verts.ensure_lookup_table()
    r = bmesh.ops.create_cone(P.bm, cap_ends=True, segments=n, radius1=r1, radius2=r2, depth=h,
                              matrix=xf(c, (1, 1, 1), rot))
    if bevel > 0:
        caps = [f for f in P.bm.faces if f not in before and len(f.verts) == n]
        edges = list({e for f in caps for e in f.edges})
        bmesh.ops.bevel(P.bm, geom=edges, offset=bevel, segments=1, affect='EDGES')
    P._tag(before, slot)


def torus(P, center, R, rt, slot, nseg=24, n=6):
    rings = []
    for i in range(nseg + 1):
        t = 2 * math.pi * (i % nseg) / nseg
        p = Vector(center) + Vector((R * math.cos(t), R * math.sin(t), 0))
        rings.append(ring_circle(p, (-math.sin(t), math.cos(t), 0), rt, n))
    vs = P.loft(rings, slot, cap0=False, cap1=False)
    bmesh.ops.remove_doubles(P.bm, verts=[v for r in vs for v in r], dist=1e-5)


def dquad(P, c, ux, uy, w, h, cell):
    """Decal quad centred at c; outward normal = ux x uy; text reads along ux with uy up."""
    uvl = P.bm.loops.layers.uv.verify()
    ux = Vector(ux).normalized(); uy = Vector(uy).normalized(); c = Vector(c)
    pts = [c - ux * w / 2 - uy * h / 2, c + ux * w / 2 - uy * h / 2, c + ux * w / 2 + uy * h / 2, c - ux * w / 2 + uy * h / 2]
    u0, v0, u1, v1 = cell_uv(cell)
    f = P.face(pts, "PickupDecal")
    for l, uv in zip(f.loops, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]):
        l[uvl].uv = uv


def arc_decal(P, r, z0, z1, a0, a1, cell, n=6):
    uvl = P.bm.loops.layers.uv.verify()
    u0, v0, u1, v1 = cell_uv(cell)
    for i in range(n):
        t0 = a0 + (a1 - a0) * i / n; t1 = a0 + (a1 - a0) * (i + 1) / n
        pts = [Vector((r * math.cos(t0), r * math.sin(t0), z0)), Vector((r * math.cos(t1), r * math.sin(t1), z0)),
               Vector((r * math.cos(t1), r * math.sin(t1), z1)), Vector((r * math.cos(t0), r * math.sin(t0), z1))]
        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
        f = P.face(pts, "PickupDecal")
        for l, uv in zip(f.loops, [(ua, v0), (ub, v0), (ub, v1), (ua, v1)]):
            l[uvl].uv = uv


def ring_decal(P, z, r0, r1, cell, n=36):
    uvl = P.bm.loops.layers.uv.verify()
    u0, v0, u1, v1 = cell_uv(cell)
    for i in range(n):
        a0 = 2 * math.pi * i / n; a1 = 2 * math.pi * (i + 1) / n
        pts = [Vector((r0 * math.cos(a0), r0 * math.sin(a0), z)), Vector((r1 * math.cos(a0), r1 * math.sin(a0), z)),
               Vector((r1 * math.cos(a1), r1 * math.sin(a1), z)), Vector((r0 * math.cos(a1), r0 * math.sin(a1), z))]
        ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
        f = P.face(pts, "PickupDecal")
        for l, uv in zip(f.loops, [(ua, v0), (ua, v1), (ub, v1), (ub, v0)]):
            l[uvl].uv = uv


def finish(P, name):
    bm = P.bm
    uvl = bm.loops.layers.uv.verify()
    di = P.slots.index("PickupDecal") if "PickupDecal" in P.slots else -1
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if f.material_index != di])
    saved = {f: [l[uvl].uv.copy() for l in f.loops] for f in bm.faces if f.material_index == di}
    bl_common.project_uvs(bm)
    for f, uvs in saved.items():
        for l, uv in zip(f.loops, uvs):
            l[uvl].uv = uv
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for s in P.slots:
        me.materials.append(mat(s))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    select_only(ob)
    for p in me.polygons:
        p.use_smooth = True
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    return ob


def newpart(name):
    P = Part(name)
    for s in ("PickupBody", "PickupMetal", "PickupGlow", "PickupDecal"):
        P.mi(s)
    return P


def Z(a):  # unit vector in the XY plane at angle a
    return Vector((math.cos(a), math.sin(a), 0))


# ====================================================================== 1. Ammo cache
def ammo_cache():
    P = newpart("Pickup_AmmoCache")
    B, M, G, Pn = "PickupBody", "PickupMetal", "PickupGlow", "Paint"
    W, D, H = .50, .28, .24
    # body, skids, lid
    X(P, (0, 0, H / 2), (W, D, H), B, .012)
    for sx in (-1, 1):
        X(P, (sx * .17, 0, .008), (.07, D + .02, .016), M, .004)
    X(P, (0, 0, H + .035), (W + .02, D + .02, .07), B, .014)
    # glowing band wrapping the lower body
    X(P, (0, 0, .062), (W + .008, D + .008, .042), G, .005)
    # corner braces
    for sx in (-1, 1):
        for sy in (-1, 1):
            X(P, (sx * (W / 2 - .004), sy * (D / 2 - .004), H / 2 + .005), (.024, .024, H + .01), M, .005)
    # latches (front) and hinges (back)
    for sx in (-.15, .15):
        X(P, (sx, -(D / 2 + .004), .20), (.062, .008, .085), M, .003)
        X(P, (sx, -(D / 2 + .014), .275), (.058, .008, .075), M, .003)
        cyl2(P, (sx, -(D / 2 + .016), .24), .012, .012, .05, M, 10, Euler((0, math.pi / 2, 0)))
        X(P, (sx, D / 2 + .006, .24), (.07, .012, .07), M, .003)
    # carry handle: bent steel rod on two mounts
    for sx in (-1, 1):
        X(P, (sx * .13, 0, H + .07 + .006), (.055, .055, .012), M, .003)
    pts = [(-.13, 0, .32), (-.13, 0, .375), (-.115, 0, .405), (-.085, 0, .418),
           (.085, 0, .418), (.115, 0, .405), (.13, 0, .375), (.13, 0, .32)]
    P.tube([Vector(p) for p in pts], [.012] * len(pts), M, 8, True)
    # ammo-type stripes on the lid + stencils
    for sy in (-1, 1):
        X(P, (0, sy * .125, H + .07 + .0015), (.34, .045, .003), Pn, .001)
    zt = H + .07 + .0012
    dquad(P, (0, -.02, zt), (1, 0, 0), (0, 1, 0), .20, .10, 1)
    fz = .155
    dquad(P, (0, -(D / 2 + .0012), fz), (1, 0, 0), (0, 0, 1), .27, .135, 0)
    dquad(P, (0, D / 2 + .0012, fz), (-1, 0, 0), (0, 0, 1), .27, .135, 0)
    dquad(P, (W / 2 + .0012 + .006, 0, fz), (0, 1, 0), (0, 0, 1), .16, .08, 1)
    dquad(P, (-(W / 2 + .0012 + .006), 0, fz), (0, -1, 0), (0, 0, 1), .16, .08, 1)
    return finish(P, "Pickup_AmmoCache")


# ====================================================================== 2. Shockwave
def shockwave():
    P = newpart("Pickup_Shockwave")
    B, M, G = "PickupBody", "PickupMetal", "PickupGlow"
    rb, rc = .225, .21
    oct_rot = Euler((0, 0, math.pi / 8))
    cyl2(P, (0, 0, .045), rb, rb, .09, B, 8, oct_rot, .009)          # base
    cyl2(P, (0, 0, .40), rc, rc, .06, B, 8, oct_rot, .008)           # cap
    ap = rb * math.cos(math.pi / 8)
    for a_deg, cell in ((270, 2), (225, 3), (315, 3)):
        a = math.radians(a_deg)
        n = Z(a); t = Vector((-math.sin(a), math.cos(a), 0))
        dquad(P, n * (ap + .0012) + Vector((0, 0, .045)), t, (0, 0, 1), .14, .07, cell)
    # emitter core: ribs with a glowing shaft between them
    cyl2(P, (0, 0, .23), .068, .068, .29, G, 16)
    for k in range(6):
        cyl2(P, (0, 0, .125 + k * .045), .097, .097, .022, M, 16)
    # glowing ring around the core, held by four spokes
    torus(P, (0, 0, .23), .152, .017, G, 28, 6)
    for k in range(4):
        a = k * math.pi / 2
        X(P, Z(a) * .122 + Vector((0, 0, .23)), (.05, .014, .014), M, .003, Euler((0, 0, a)))
    # protective cage: six bowed bars + two hoops
    def rbar(z):
        return .19 + .027 * (1 - ((z - .23) / .14) ** 2)
    zs = [.09, .15, .23, .31, .37]
    for k in range(6):
        a = k * math.pi / 3
        pts = [Z(a) * rbar(z) + Vector((0, 0, z)) for z in zs]
        P.tube(pts, [.013] * len(pts), M, 6, True)
    for z in (.15, .31):
        torus(P, (0, 0, z), rbar(z), .009, M, 24, 5)
    # carry loop + cap bolts
    arch = [Vector((-.07 * math.cos(t), 0, .43 + .075 * math.sin(t))) for t in [i * math.pi / 6 for i in range(7)]]
    P.tube(arch, [.013] * 7, M, 6, True)
    for k in range(4):
        cyl2(P, Z(k * math.pi / 2 + math.pi / 4) * .15 + Vector((0, 0, .434)), .014, .014, .012, M, 6)
    return finish(P, "Pickup_Shockwave")


# ====================================================================== 3. Deadeye
def deadeye():
    P = newpart("Pickup_Deadeye")
    B, M, G = "PickupBody", "PickupMetal", "PickupGlow"
    prof = [(0, 0), (.062, 0), (.068, .004), (.068, .018), (.063, .022), (.060, .028), (.060, .034), (.062, .038),
            (.060, .20), (.058, .30), (.050, .325), (.042, .345), (.039, .36), (.041, .366),     # case, shoulder, neck, crimp
            (.039, .372), (.0385, .40), (.035, .43), (.029, .465), (.019, .50), (.009, .525), (.0, .54)]  # bullet ogive

    def slot(i, j):
        zm = (prof[i][1] + prof[i + 1][1]) / 2
        if zm > .435:
            return G
        return B if zm < .372 else M
    P.lathe(prof, slot, n=28)
    # reticle ring: chamfered square section, 36 sides (ticks line up with the sides)
    ri, ro, z0, z1, b = .135, .26, .15, .195, .009
    rp = [(ri + b, z0), (ro - b, z0), (ro, z0 + b), (ro, z1 - b), (ro - b, z1), (ri + b, z1), (ri, z1 - b), (ri, z0 + b), (ri + b, z0)]
    vs = P.lathe(rp, M, n=36)
    bmesh.ops.remove_doubles(P.bm, verts=[v for r in vs for v in r], dist=1e-5)
    # crosshair spokes
    for k in range(4):
        a = k * math.pi / 2
        X(P, Z(a) * ((.056 + ri + .004) / 2) + Vector((0, 0, .1725)), (ri - .052, .02, .02), M, .004, Euler((0, 0, a)))
    # four outward pointers on the ring (sight posts) to keep the silhouette from collapsing edge-on
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        X(P, Z(a) * (ro - .01) + Vector((0, 0, z1 + .025)), (.03, .03, .05), M, .006, Euler((0, 0, a)))
    ring_decal(P, z1 + .0012, ri + b + .004, ro - b - .004, 5, 36)
    for a in (-math.pi / 2, math.pi / 2):
        arc_decal(P, .0603, .228, .289, a - math.radians(56), a + math.radians(56), 4, 8)
    return finish(P, "Pickup_Deadeye")


# ====================================================================== 4. Payday
def payday():
    P = newpart("Pickup_Payday")
    B, M, G = "PickupBody", "PickupMetal", "PickupGlow"
    L, Wd, H = .36, .16, .085
    bricks = []   # (centre, rot angle, along-x?)
    for sy in (-1, 1):
        bricks.append((Vector((0, sy * .083, H * .5)), math.radians(sy * 1.5), 0))
    for sx in (-1, 1):
        bricks.append((Vector((sx * .083, 0, H * 1.5 + .001)), math.radians(90 + sx * 2), 1))
    bricks.append((Vector((0, 0, H * 2.5 + .002)), math.radians(3), 2))
    top = None
    for c, ang, lv in bricks:
        R = Euler((0, 0, ang))
        q = R.to_quaternion()
        X(P, c, (L, Wd, H), B, .006, R)
        ex = q @ Vector((1, 0, 0)); ey = q @ Vector((0, 1, 0))
        zt = c.z + H / 2
        dquad(P, c + Vector((0, 0, H / 2 + .0012)), ex, ey, L - .02, (L - .02) / 2.2, 6)
        for s in ((-1, 1) if lv != 1 else (-1, 1)):
            bc = c + ex * (s * .115)
            X(P, bc, (.05, Wd + .008, H + .008), G, .004, R)
        if lv == 2:
            top = (c, q, ex, ey, zt)
    # label on the top brick's band
    c, q, ex, ey, zt = top
    dquad(P, c + ex * .115 + Vector((0, 0, H / 2 + .0042 + .0012)), ey, -ex, .15, .068, 7)
    # gold money clip over the top brick centre
    for s in (-1, 1):
        X(P, c + ey * (s * (Wd / 2 + .004)) + Vector((0, 0, .004)), (.07, .008, .085), M, .003, q)
    X(P, c + Vector((0, 0, H / 2 + .016)), (.07, Wd + .016, .008), M, .003, q)
    # two loose bills fanning out from under the clip (symmetric V)
    bl, bw, bt = .28, .14, .0045
    for sgn, yaw in ((1, math.radians(14)), (-1, math.radians(-12))):
        Rm = Matrix.Rotation(math.pi if sgn < 0 else 0, 3, 'Z') @ Matrix.Rotation(yaw, 3, 'Z') @ Matrix.Rotation(-math.radians(30), 3, 'Y')
        rq = Rm.to_quaternion()
        root = c + Vector((0, 0, H / 2 + .0075))
        d = Rm @ Vector((1, 0, 0)); w = Rm @ Vector((0, 1, 0)); n = Rm @ Vector((0, 0, 1))
        ctr = root + d * (bl / 2)
        X(P, ctr, (bl, bw, bt), B, 0, rq)
        dquad(P, ctr + n * (bt / 2 + .0012), d, w, bl - .006, bw - .003, 6)
        dquad(P, ctr - n * (bt / 2 + .0012), d, -w, bl - .006, bw - .003, 6)
    return finish(P, "Pickup_Payday")


# ====================================================================== previews
PAL = {
    "AmmoCache": dict(body=(.12, .16, .07), metal=(.05, .055, .06), paint=(.8, .55, .04), glow=(1., .55, .03), bm=0, mm=.7),
    "Shockwave": dict(body=(.05, .06, .075), metal=(.45, .47, .5), paint=(.8, .1, .05), glow=(.1, .75, 1.), bm=0, mm=.9),
    "Deadeye": dict(body=(.78, .5, .12), metal=(.07, .07, .08), paint=(.8, .1, .05), glow=(1., .02, .0), bm=1.0, mm=.8),
    "Payday": dict(body=(.12, .36, .14), metal=(.95, .68, .16), paint=(.8, .1, .05), glow=(.55, 1., .15), bm=0, mm=1.0),
}


def pv_mat(model, slot):
    key = "PVP_%s_%s" % (model, slot)
    m = bpy.data.materials.get(key)
    if m:
        return m
    m = bpy.data.materials.new(key)
    nt, b = b2._principled(m)
    p = PAL[model]
    if slot == "PickupBody":
        b2._set(b, "Base Color", (*p["body"], 1)); b2._set(b, "Roughness", .38 if p["bm"] else .6); b2._set(b, "Metallic", p["bm"])
    elif slot == "PickupMetal":
        b2._set(b, "Base Color", (*p["metal"], 1)); b2._set(b, "Roughness", .3); b2._set(b, "Metallic", p["mm"])
    elif slot == "Paint":
        b2._set(b, "Base Color", (*p["paint"], 1)); b2._set(b, "Roughness", .5)
    elif slot == "PickupGlow":
        g = p["glow"]
        b2._set(b, "Base Color", (*[c * .3 for c in g], 1)); b2._set(b, "Emission Color", (*g, 1)); b2._set(b, "Emission Strength", 1.7)
    elif slot == "PickupDecal":
        tex = nt.nodes.new("ShaderNodeTexImage")
        im = bpy.data.images.load(DECAL_PNG); im.alpha_mode = 'STRAIGHT'
        tex.image = im
        uvn = nt.nodes.new("ShaderNodeUVMap"); nt.links.new(uvn.outputs[0], tex.inputs[0])
        nt.links.new(tex.outputs[0], b.inputs["Base Color"]); nt.links.new(tex.outputs[1], b.inputs["Alpha"])
        b2._set(b, "Roughness", .6)
    return m


def restyle(ob, model):
    for i, s in enumerate(ob.data.materials):
        ob.data.materials[i] = pv_mat(model, s.name.split(".")[0])


def crop_png(path, w, h):
    im = bpy.data.images.load(path)
    W, H = im.size
    a = np.array(im.pixels[:], np.float32).reshape(H, W, 4)[::-1]
    y0, x0 = (H - h) // 2, (W - w) // 2
    a = a[y0:y0 + h, x0:x0 + w, :3]
    bpy.data.images.remove(im)
    write_png(path, a)


def shot(vis, path, loc, tgt, lens, size, crop=None):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = size
    for o in scn.objects:
        if o.type == 'MESH' and o.name.startswith("Pickup_"):
            o.hide_render = o not in vis
    for o in [o for o in scn.objects if o.name.startswith("cam")]:
        bpy.data.objects.remove(o, do_unlink=True)
    look_cam(loc, tgt, lens)
    scn.render.filepath = path
    ensure_dir(os.path.dirname(path))
    bpy.ops.render.render(write_still=True)
    if crop:
        crop_png(path, *crop)
    print("PREVIEW", path)


def previews(objs):
    scn = setup_render((1000, 800), samples=64, exposure=-.3, sun_az=-40, sun_elev=45)
    g = textured_ground(80, "concrete", 0.0, (0, 0), 2.0)
    wall = textured_ground(120, "brick", 0.0, (0, 0), 3.0)
    wall.rotation_euler = (math.pi / 2, 0, 0); wall.location = (0, 16, 40)
    for o, nm in zip(objs, NAMES):
        restyle(o, nm)
    away = Vector((0, 0, 0))
    for o, nm in zip(objs, NAMES):
        for oo in objs:
            oo.location = (0, 0, 0)
        o.location = (0, 0, 0)
        pts = [Vector(c) for c in o.bound_box]
        rad = (max(p.x for p in pts) - min(p.x for p in pts)) / 2
        h = max(p.z for p in pts)
        ctr = Vector((0, 0, h / 2))
        a, e = math.radians(35), math.radians(20)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        shot([o], os.path.join(PREVIEWS, "Pickup_%s_34.png" % nm), ctr + d * 1.9, ctr, 50, (1000, 800))
        # gameplay view: 10 m away, camera at 1.7 m, pickup hovering 0.9 m
        o.location = (0, 0, .9)
        shot([o], os.path.join(PREVIEWS, "Pickup_%s_far.png" % nm), (0, -10, 1.7), (0, 0, 1.2), 50, (1280, 720), (640, 360))
        o.location = (0, 0, 0)
    xs = (-1.1, -.37, .37, 1.1)
    for o, x in zip(objs, xs):
        o.location = (x, 0, 0)
    shot(objs, os.path.join(PREVIEWS, "Pickup_Lineup.png"), (1.2, -4.1, 1.0), (0, 0, .3), 50, (1400, 600))
    for o, x in zip(objs, xs):
        o.location = (x * 1.25, 0, .9)
    shot(objs, os.path.join(PREVIEWS, "Pickup_Lineup_far.png"), (0, -10, 1.7), (0, 0, 1.2), 50, (1280, 720), (800, 450))
    for o in objs:
        o.location = (0, 0, 0)


# ====================================================================== main
def main():
    make_decals()
    reset_scene()
    b2._FONT = None
    objs = []
    for nm, fn in zip(NAMES, (ammo_cache, shockwave, deadeye, payday)):
        ob = fn()
        report(ob)
        print("   slots:", [s.name for s in ob.data.materials])
        export_fbx([ob], "Pickups", ob.name)
        objs.append(ob)
    if "noprev" not in sys.argv:
        previews(objs)


main()
