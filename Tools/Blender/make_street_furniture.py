"""Street furniture V2 -> Assets/Art/Models/Props/V2/*.fbx
Textures -> Assets/Art/Textures/{StreetSigns_Albedo,RoadMarkingsV2_Albedo}.png
Run: blender --background --factory-startup --python make_street_furniture.py [--tex] [--only=A,B]

Pivot: bottom center on the ground, front faces +Z (heads/signs face +Z), arms extend to +X.
All names are fictional. Slots: Metal, Concrete, Glass, Interior, Paint, SignFace (atlas),
SignalRed / SignalAmber / SignalGreen (emissive), LampGlass, Chrome, Rubber.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_street2 import *

for k, c in (("SignFace", (.2, .4, .3)), ("SignalRed", (1, .1, .05)), ("SignalAmber", (1, .6, .05)), ("SignalGreen", (.1, 1, .4))):
    COLORS[k] = c

BLADES = ["MERCER AVE", "HALSEY ST", "ORCHARD BLVD", "GARNET ST", "PELL AVE", "RUSKIN ST", "DALTON RD", "FENWICK LN"]
ATLAS = 2048


# ====================================================================== sign atlas
def rrect(h, w, m, r):
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    x = np.maximum(np.abs(xx + .5 - w / 2) - (w / 2 - m - r), 0)
    y = np.maximum(np.abs(yy + .5 - h / 2) - (h / 2 - m - r), 0)
    return np.clip(.5 - (np.hypot(x, y) - r), 0, 1)


def put(img, y0, x0, tile):
    img[y0:y0 + tile.shape[0], x0:x0 + tile.shape[1]] = tile


def mix(base, col, m):
    return base * (1 - m[..., None]) + np.array(col, np.float32) * m[..., None]


def blade_tile(name, nz):
    h, w = 256, 1024
    t = np.empty((h, w, 3), np.float32); t[:] = (.04, .30, .20)
    ring = rrect(h, w, 8, 26) - rrect(h, w, 20, 16)
    t = mix(t, (.93, .93, .88), np.clip(ring, 0, 1))
    tx = text_mask(name, w - 150, h - 100, .82)
    m = np.zeros((h, w), np.float32); m[50:50 + h - 100, 75:75 + w - 150] = tx
    t = mix(t, (.95, .95, .9), m)
    for sx in (44, w - 44):
        yy, xx = np.mgrid[0:h, 0:w]
        t = mix(t, (.75, .75, .72), ((xx - sx) ** 2 + (yy - h // 2) ** 2 < 36).astype(np.float32))
    t *= (.94 + .06 * nz[:h, :w, None])
    return t


def octagon(S, R):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    x, y = np.abs(xx + .5 - S / 2), np.abs(yy + .5 - S / 2)
    d = np.maximum(np.maximum(x, y) - R, (x + y) / 1.41421 - R)
    return np.clip(.5 - d, 0, 1)


def make_signs():
    rng = np.random.default_rng(3)
    nz = noise2d(ATLAS, 1.6, 8, m=ATLAS)
    A = np.empty((ATLAS, ATLAS, 3), np.float32); A[:] = (.5, .5, .5)
    for b, nm in enumerate(BLADES):
        put(A, (b // 2) * 256, (b % 2) * 1024, blade_tile(nm, nz))
    y0 = 1024
    # 0: STOP (octagon, 0.75 m across)
    S = 512
    t = np.empty((S, S, 3), np.float32); t[:] = (.78, .08, .08)
    outer = octagon(S, 250); inner = octagon(S, 228)
    t = mix(t, (.94, .94, .92), np.clip(outer - inner, 0, 1))
    tx = text_mask("STOP", 400, 170, .9)
    m = np.zeros((S, S), np.float32); m[170:340, 56:456] = tx
    t = mix(t, (.96, .96, .94), m)
    put(A, y0, 0, t * (.95 + .05 * nz[:S, :S, None]))
    # 1: NO PARKING (0.45 x 0.6 m => 384 x 512)
    w, h = 384, 512
    t = np.empty((h, w, 3), np.float32); t[:] = (.93, .93, .9)
    t = mix(t, (.75, .1, .1), np.clip(rrect(h, w, 10, 18) - rrect(h, w, 22, 10), 0, 1))
    m = np.zeros((h, w), np.float32)
    m[50:170, 40:344] = text_mask("NO", 304, 120, .95)
    m[180:300, 40:344] = text_mask("PARKING", 304, 120, .95)
    t = mix(t, (.75, .1, .1), m)
    t[318:330, 40:344] = (.75, .1, .1)
    m = np.zeros((h, w), np.float32)
    m[345:405, 40:344] = text_mask("8AM - 6PM", 304, 60, .9)
    m[420:470, 40:344] = text_mask("MON - FRI", 304, 50, .9)
    t = mix(t, (.08, .08, .1), m)
    put(A, y0, 512, t * (.95 + .05 * nz[:h, :w, None]))
    # 2: BUS STOP (blue, 0.45 x 0.6)
    t = np.empty((h, w, 3), np.float32); t[:] = (.08, .27, .55)
    t = mix(t, (.94, .94, .92), np.clip(rrect(h, w, 10, 18) - rrect(h, w, 20, 10), 0, 1))
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    body = rrect(h, w, 0, 1) * 0
    bus = np.clip(.5 - (np.maximum(np.abs(xx - w / 2) - 120, 0) ** 2 + np.maximum(np.abs(yy - 150) - 68, 0) ** 2) ** .5 + 0, 0, 1)
    bus = np.clip(.5 - (np.hypot(np.maximum(np.abs(xx - w / 2) - 112, 0), np.maximum(np.abs(yy - 150) - 66, 0)) - 14), 0, 1)
    t = mix(t, (.95, .95, .93), bus)
    win = np.zeros((h, w), np.float32)
    for k in range(4):
        cx = w / 2 - 84 + k * 56
        win += ((np.abs(xx - cx) < 20) & (np.abs(yy - 128) < 24)).astype(np.float32)
    win += ((np.abs(xx - w / 2) < 106) & (np.abs(yy - 184) < 5)).astype(np.float32) * 0
    t = mix(t, (.08, .27, .55), np.clip(win, 0, 1))
    for cx in (w / 2 - 70, w / 2 + 70):
        t = mix(t, (.08, .27, .55), ((xx - cx) ** 2 + (yy - 214) ** 2 < 32 ** 2).astype(np.float32))
        t = mix(t, (.95, .95, .93), ((xx - cx) ** 2 + (yy - 214) ** 2 < 22 ** 2).astype(np.float32))
    m = np.zeros((h, w), np.float32)
    m[285:385, 40:344] = text_mask("BUS", 304, 100, .95)
    m[395:480, 40:344] = text_mask("STOP", 304, 85, .95)
    t = mix(t, (.96, .96, .94), m)
    put(A, y0, 1024, t * (.95 + .05 * nz[:h, :w, None]))
    # 3: telephone header (512 x 128)
    t = np.empty((128, 512, 3), np.float32); t[:] = (.08, .14, .32)
    t = mix(t, (.9, .9, .88), np.clip(rrect(128, 512, 6, 10) - rrect(128, 512, 14, 6), 0, 1))
    m = np.zeros((128, 512), np.float32); m[26:102, 40:472] = text_mask("TELEPHONE", 432, 76, .9)
    t = mix(t, (.95, .95, .9), m)
    put(A, y0, 1536, t)
    # 4: ONE WAY (black, 512 x 160)
    t = np.empty((160, 512, 3), np.float32); t[:] = (.06, .06, .07)
    t = mix(t, (.93, .93, .9), np.clip(rrect(160, 512, 6, 12) - rrect(160, 512, 14, 8), 0, 1))
    m = np.zeros((160, 512), np.float32); m[22:138, 130:480] = text_mask("ONE WAY", 350, 116, .9)
    yy, xx = np.mgrid[0:160, 0:512].astype(np.float32)
    m += ((np.abs(yy - 80) < 10) & (xx > 24) & (xx < 100)).astype(np.float32)
    tri = ((xx > 90) & (xx < 124) & (np.abs(yy - 80) < (xx - 90) * -1 + 34 + 0)).astype(np.float32) * 0
    t = mix(t, (.95, .95, .92), np.clip(m, 0, 1))
    put(A, y0 + 512, 0, t)
    # 5: METER "PAY HERE" plate (256 x 128)
    t = np.empty((128, 256, 3), np.float32); t[:] = (.12, .16, .2)
    m = np.zeros((128, 256), np.float32); m[20:108, 20:236] = text_mask("PAY HERE", 216, 88, .85)
    t = mix(t, (.85, .9, .8), m)
    put(A, y0 + 512, 512, t)
    write_png(os.path.join(TEX, "StreetSigns_Albedo.png"), A)


def cell_uv(kind, i=0):
    """UV rect (u0, v0, u1, v1), v from the bottom, for an atlas cell."""
    def r(x0, y0, x1, y1):
        return (x0 / ATLAS, 1 - y1 / ATLAS, x1 / ATLAS, 1 - y0 / ATLAS)
    if kind == "blade":
        return r((i % 2) * 1024, (i // 2) * 256, (i % 2) * 1024 + 1024, (i // 2) * 256 + 256)
    return {"stop": r(0, 1024, 512, 1536), "nopark": r(512, 1024, 512 + 384, 1536), "bus": r(1024, 1024, 1024 + 384, 1536),
            "phone": r(1536, 1024, 2048, 1152), "oneway": r(0, 1536, 512, 1696), "meter": r(512, 1536, 768, 1664)}[kind]


# ====================================================================== road markings
YEL = np.array((.86, .68, .10), np.float32)
WHT = np.array((.90, .90, .86), np.float32)


def make_markings():
    S, C = 512, 4
    out = np.zeros((2048, 2048, 4), np.float32)
    rng = np.random.default_rng(9)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)

    def bar(x0, x1, y0=0, y1=S):
        return ((xx >= x0) & (xx < x1) & (yy >= y0) & (yy < y1)).astype(np.float32)

    def soft(m):
        return blur(m, 1) * .7 + m * .3

    def worn(m, seed, amount=.55):
        n = noise2d(S, 1.7, seed, sc=3.0) * .6 + noise2d(S, 2.5, seed + 1, sc=1.2) * .4
        lo = .30 + amount * .35
        keep = smoothstep(lo, lo + .18, n)
        return m * keep

    def cracked(m, seed):
        r = np.random.default_rng(seed)
        cr = np.zeros((S, S), np.float32)
        for _ in range(4):
            x, y = r.uniform(0, S), r.uniform(0, S)
            ang = r.uniform(0, 6.28)
            for k in range(220):
                ang += r.normal(0, .22)
                x += math.cos(ang) * 2.2; y += math.sin(ang) * 2.2
                xi, yi = int(x) % S, int(y) % S
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        cr[(yi + dy) % S, (xi + dx) % S] = 1
                if r.random() < .012:                      # branch
                    pass
        return m * (1 - cr * .95), cr

    cells = {}
    dy = bar(218, 243) + bar(268, 293)
    cells[0] = (dy, YEL)
    cells[1] = (worn(dy, 21), YEL * .93)
    c2, cr2 = cracked(worn(dy, 22, .25), 5)
    cells[2] = (c2, YEL * .95)
    wl = bar(241, 271)
    cells[3] = (wl, WHT)
    dash = bar(243, 269, 0, 307) * 1.0
    cells[4] = (dash, WHT)
    cells[5] = (worn(wl, 23), WHT * .93)
    c6, cr6 = cracked(worn(wl, 24, .2), 8)
    cells[6] = (c6, WHT * .95)
    cells[7] = (bar(247, 265), YEL)
    sb = bar(0, S, 192, 320)
    cells[8] = (sb, WHT)
    cells[9] = (worn(sb, 25, .6), WHT * .93)
    cw = bar(64, 192) + bar(320, 448)
    cells[10] = (cw, WHT)
    cells[11] = (worn(cw, 26, .6), WHT * .93)
    # straight arrow (shaft 0.18 m wide, head 0.7 m wide) centered, ~1.6 m long
    shaft = bar(233, 279, 150, 420)
    head = ((np.abs(xx - 256) < (yy - 40) * .52) & (yy > 40) & (yy < 160)).astype(np.float32)
    cells[12] = (np.clip(shaft + head, 0, 1), WHT)
    # decals
    n1 = noise2d(S, 1.6, 31, sc=2.0)
    crack_only = np.zeros((S, S), np.float32)
    for sd in (11, 12, 13):
        _, cr = cracked(np.ones((S, S), np.float32), sd)
        crack_only = np.maximum(crack_only, cr)
    cells[13] = (blur(crack_only, 1) * .75, np.array((.05, .045, .04), np.float32))
    patch = bar(60, 450, 120, 380)
    edge = np.clip(patch - blur(patch, 3) * .0, 0, 1)
    seam = np.clip(blur(bar(56, 64, 116, 384) + bar(446, 454, 116, 384) + bar(56, 454, 116, 124) + bar(56, 454, 376, 384), 1), 0, 1)
    cells[14] = (np.clip(patch * (.35 + .25 * n1) + seam * .6, 0, .9), np.array((.07, .07, .075), np.float32))
    tire = (bar(120, 190) + bar(322, 392)) * (.2 + .5 * noise2d(S, 1.8, 33, aniso=(2.5, .3), sc=3.0))
    cells[15] = (np.clip(tire, 0, .6), np.array((.04, .04, .04), np.float32))
    for i, (a, col) in cells.items():
        cx, cy = (i % C) * S, (i // C) * S
        a = np.clip(soft(a), 0, 1) if i < 13 else a
        shade = (.9 + .1 * noise2d(S, 1.5, 100 + i))
        rgb = np.empty((S, S, 3), np.float32); rgb[:] = col
        rgb = rgb * shade[..., None]
        out[cy:cy + S, cx:cx + S, :3] = rgb
        out[cy:cy + S, cx:cx + S, 3] = a
    write_png(os.path.join(TEX, "RoadMarkingsV2_Albedo.png"), out)


# ====================================================================== geometry helpers
def ring_xz(x, y, z, r, n=16):
    return [Vector((x + r * math.cos(2 * math.pi * i / n), y, z + r * math.sin(2 * math.pi * i / n))) for i in range(n)]


def pole(p, x, f, z0, z1, r0, r1, m="Metal", n=14):
    p.tube([V(x, f, z0), V(x, f, (z0 + z1) / 2), V(x, f, z1)], [r0, (r0 + r1) / 2, r1], m, n)


def plate(p, center, R, U, w, h, t, front=None, back=None, body="Metal", fuv=None, buv=None):
    """Thin closed plate. R = right dir, U = up dir (viewer on the front side). front/back: slot names with UV rects."""
    R, U = Vector(R).normalized(), Vector(U).normalized()
    N = R.cross(U).normalized()            # outward normal on the front
    M = Matrix.Identity(4)
    for i, vec in enumerate((R * w, -N * t, U * h)):
        M[0][i] = vec.x; M[1][i] = vec.y; M[2][i] = vec.z
    M[0][3], M[1][3], M[2][3] = center.x, center.y, center.z
    before = p.snapshot()
    r = bmesh.ops.create_cube(p.bm, size=1.0, matrix=M)
    faces = [f for f in p.bm.faces if f not in before]
    bi = p.mi(body)
    uvl = p.uvl()
    for f in faces:
        f.material_index = bi
    for f in faces:
        fn = f.normal
        if front and fn.dot(N) > .9 or back and fn.dot(N) < -.9:
            sign = 1 if fn.dot(N) > 0 else -1
            m, rect = (front, fuv) if sign > 0 else (back, buv)
            f.material_index = p.mi(m)
            for l in f.loops:
                d = l.vert.co - center
                u = d.dot(R) / w * sign + .5
                v = d.dot(U) / h + .5
                l[uvl].uv = (rect[0] + u * (rect[2] - rect[0]), rect[1] + v * (rect[3] - rect[1]))
    p.fixed = getattr(p, "fixed", [])
    p.fixed += [f for f in faces if p.slots[f.material_index] == front or p.slots[f.material_index] == back]
    return faces


def build_props(p, name, smooth=40, flat=()):
    fixed = set(getattr(p, "fixed", []))

    def uvfix(bm):
        pass
    # project_uvs would overwrite hand UVs: protect them
    saved = {}
    uvl = p.bm.loops.layers.uv.verify()
    for f in fixed:
        saved[f] = [tuple(l[uvl].uv) for l in f.loops]
    verts = {f: [l.vert for l in f.loops] for f in fixed}

    def restore(bm):
        for f, uvs in saved.items():
            uvmap = {v: u for v, u in zip(verts[f], uvs)}
            for l in f.loops:
                l[uvl].uv = uvmap[l.vert]
    return p.build2(name=name, smooth=smooth, flat=flat, uvfix=restore)


def head3(p, c, w=.34, d=.3, h=1.02, visor=True):
    """Three-lamp signal head facing +f. c = center."""
    cx, cf, cz = c.x, -c.y, c.z
    p.rbox(V(cx, cf, cz), (w, d, h), "Metal", .025, 2)
    zs = (.33, 0, -.33)
    for z, slot in zip(zs, ("SignalRed", "SignalAmber", "SignalGreen")):
        lc = V(cx, cf + d / 2, cz + z)
        p.cyl(lc + V(0, -.004, 0), .125, .125, .02, "Metal", 16, Euler((math.radians(90), 0, 0)))
        p.sphere(lc + V(0, .006, 0), (.1, .028, .1), slot, (14, 6))
        if visor:
            p.rbox(V(cx, cf + d / 2 + .12, cz + z + .15), (w - .02, .26, .016), "Metal", .004, 1)
            for sd in (-1, 1):
                p.rbox(V(cx + sd * (w / 2 - .02), cf + d / 2 + .12, cz + z + .06), (.016, .26, .2), "Metal", .004, 1)
    return p


# ====================================================================== items
def traffic_pole():
    p = P2("TrafficLight_Pole")
    pole(p, 0, 0, 0, 6.3, .14, .085, n=14)
    p.cyl(V(0, 0, .12), .22, .22, .24, "Metal", 14)                           # base shroud
    p.rbox(V(0, .1, .9), (.12, .06, .3), "Metal", .01, 1)                    # handhole plate
    arm = [V(.05 * 0 + x, 0, 5.95 + .45 * (x / 5.6) ** 1.2 - .02 * x * 0) for x in np.linspace(0, 5.6, 14)]
    p.tube(arm, [.075 - .03 * i / 13 for i in range(14)], "Metal", 10)
    for x in (2.3, 4.3):
        p.rbox(V(x, 0, 6.18 + .02), (.1, .1, .2), "Metal", .01, 1)             # hanger clamp
        p.tube([V(x, 0, 6.2), V(x, 0, 5.98)], [.04, .04], "Metal", 6)
        head3(p, V(x, 0, 5.46))
        p.rbox(V(x, -.17, 5.46), (.5, .03, 1.2), "Metal", .01, 1)             # backplate
    p.rbox(V(1.0, 0, 5.84), (.14, .14, .5), "Metal", .02, 1)
    head3(p, V(0, .35, 2.7), .3, .26, .92, False)                              # near-side head on the pole
    p.rbox(V(0, .17, 2.7), (.14, .22, .12), "Metal", .02, 1)
    return p.build2(smooth=40)


def traffic_post():
    p = P2("TrafficLight_Post")
    pole(p, 0, 0, 0, 3.7, .06, .052, n=12)
    p.cyl(V(0, 0, .09), .12, .12, .18, "Metal", 12)
    head3(p, V(0, .3, 3.3), .32, .28, .98)
    p.rbox(V(0, .14, 3.3), (.5, .03, 1.2), "Metal", .01, 1) if False else p.rbox(V(0, -.0, 3.3), (.46, .03, 1.16), "Metal", .01, 1)
    p.rbox(V(0, .14, 3.3), (.1, .2, .12), "Metal", .02, 1)
    p.rbox(V(0, .1, 1.15), (.12, .08, .2), "Metal", .02, 1)                   # push-button box
    p.sphere(V(0, .155, 1.17), (.025, .01, .025), "Chrome", (8, 4))
    return p.build2(smooth=40)


def sign_post_base(p, h, r=.03):
    pole(p, 0, 0, 0, h, r, r, n=10)
    p.cyl(V(0, 0, h + .01), r * 1.15, r * 1.15, .02, "Metal", 10)


def street_sign(variant):
    p = P2("StreetSign_Post")
    sign_post_base(p, 3.35, .033)
    names = list(range(4 * variant, 4 * variant + 4))
    zs = (3.16, 2.92)
    R_alongx = Vector((1, 0, 0))
    # blade 1 along X (faces +Z / -Z), blade 2 along Z (faces +X / -X)
    w, h, t = .9, .225, .014
    off = .06
    for z, axis, (a, b) in zip(zs, ("x", "z"), ((names[0], names[1]), (names[2], names[3]))):
        if axis == "x":
            c = V(0, 0, z) + Vector((.0, 0, 0))
            R = Vector((1, 0, 0)); N = Vector((0, -1, 0))
        else:
            c = V(0, 0, z)
            R = Vector((0, -1, 0)); N = Vector((-1, 0, 0))            # viewer at -X: right = +f (forward)
        U = Vector((0, 0, 1))
        # center the blade so the post passes near one end (blades extend both sides a little)
        c = c + R * (w / 2 - .075)
        plate(p, c, R, U, w, h, t, "SignFace", "SignFace", "Metal", cell_uv("blade", a), cell_uv("blade", b))
        for sx in (-.08, .08):
            p.rbox(V(0, 0, z) + R * sx * 0 + Vector((0, 0, 0)), (.075, .075, h * .8), "Metal", .008, 1) if False else None
        p.rbox(V(0, 0, z), (.085, .085, h + .01), "Metal", .01, 1)                  # clamp block on the post
    return p


def stop_sign():
    p = P2("StopSign")
    sign_post_base(p, 2.55, .03)
    ap = .375
    Rr = ap / math.cos(math.radians(22.5))
    z = 2.2
    pts = [(Rr * math.cos(math.radians(22.5 + 45 * i)), Rr * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)]
    # back plate (metal) + front face with the atlas
    bm = p.bm; uvl = p.uvl()
    off = .045
    t = .012
    fr = [bm.verts.new(V(x, off + t, z + y)) for x, y in pts]
    bk = [bm.verts.new(V(x, off, z + y)) for x, y in pts]
    u0, v0, u1, v1 = cell_uv("stop")
    ff = bm.faces.new(fr[::-1]) if False else bm.faces.new(fr)
    ff.material_index = p.mi("SignFace")
    for l, (x, y) in zip(ff.loops, pts):
        l[uvl].uv = (u0 + (x / (2 * ap) + .5) * (u1 - u0) * 1.0, v0 + (y / (2 * ap) + .5) * (v1 - v0))
    fb = bm.faces.new(bk[::-1]); fb.material_index = p.mi("Metal")
    for i in range(8):
        q = bm.faces.new((fr[i], fr[(i + 1) % 8], bk[(i + 1) % 8], bk[i])); q.material_index = p.mi("Metal")
    p.fixed = [ff]
    p.rbox(V(0, .02, z + .15), (.06, .03, .08), "Metal", .005, 1); p.rbox(V(0, .02, z - .15), (.06, .03, .08), "Metal", .005, 1)
    return p


def rect_sign_post(kind, h, w, hh, z, name, extra=None):
    p = P2(name)
    sign_post_base(p, h, .03)
    off = .045
    rect = cell_uv(kind)
    plate(p, V(0, off, z), Vector((1, 0, 0)), Vector((0, 0, 1)), w, hh, .014, "SignFace", None, "Metal", rect, None)
    for dz in (-hh * .3, hh * .3):
        p.rbox(V(0, .02, z + dz), (.07, .05, .05), "Metal", .008, 1)
    if extra:
        extra(p)
    return p


def parking_meter():
    p = P2("ParkingMeter")
    pole(p, 0, 0, 0, 1.18, .035, .03, n=10)
    p.cyl(V(0, 0, .03), .06, .06, .06, "Metal", 10)
    p.rbox(V(0, 0, 1.38), (.2, .17, .34), "Metal", .05, 3)
    p.sphere(V(0, 0, 1.55), (.1, .085, .08), "Metal", (12, 6))
    p.rbox(V(0, .0, 1.2), (.14, .12, .06), "Paint", .015, 1)                 # collar
    loc = V(0, .088, 1.45)
    p.rbox(loc, (.13, .012, .11), "Glass", .01, 1)
    p.rbox(V(0, .09, 1.34), (.11, .012, .03), "Interior", .006, 1)            # coin slot
    p.sphere(V(.0, .095, 1.28), (.022, .012, .022), "Chrome", (8, 4))
    p.rbox(V(0, .0, 1.58), (.12, .1, .04), "Paint", .01, 1) if False else None
    return p


def phone_booth():
    p = P2("PhoneBooth")
    W, D, H = .9, .9, 2.35
    p.rbox(V(0, 0, .06), (W + .06, D + .06, .12), "Concrete", .01, 1)
    for sx in (-1, 1):
        for sf in (-1, 1):
            p.rbox(V(sx * W / 2, sf * D / 2, H / 2 + .06), (.06, .06, H - .1), "Metal", .008, 1)
    p.rbox(V(0, 0, H + .02), (W + .1, D + .1, .08), "Metal", .015, 1)
    p.rbox(V(0, 0, H + .11), (W + .02, D + .02, .1), "Metal", .02, 2)
    for z in (.14, 1.0, H - .02):
        for sx in (-1, 1):
            p.rbox(V(sx * W / 2, 0, z), (.05, D, .05), "Metal", .006, 1)
        p.rbox(V(0, -D / 2, z), (W, .05, .05), "Metal", .006, 1)
        p.rbox(V(0, D / 2, z), (W, .05, .05), "Metal", .006, 1)
    for sx in (-1, 1):                                       # side + back glass
        p.rbox(V(sx * W / 2, 0, 1.2 + .07), (.012, D - .08, 1.9), "Glass", 0, 1)
    p.rbox(V(0, -D / 2, 1.2 + .07), (W - .08, .012, 1.9), "Glass", 0, 1)
    p.rbox(V(0, D / 2, 1.6), (W - .08, .012, 1.1), "Glass", 0, 1)           # fixed upper front pane
    p.rbox(V(-W / 2 + .1, D / 2, .6), (.05, .05, .9), "Metal", .005, 1)       # door jamb
    # phone unit on the back wall
    p.rbox(V(0, -D / 2 + .09, 1.35), (.3, .12, .45), "Metal", .03, 2)
    p.rbox(V(0, -D / 2 + .15, 1.5), (.2, .02, .12), "Interior", .005, 1)
    p.rbox(V(-.11, -D / 2 + .16, 1.32), (.04, .06, .3), "Chrome", .01, 1)
    for i in range(3):
        for j in range(4):
            p.rbox(V(-.05 + i * .05, -D / 2 + .155, 1.28 - j * .045), (.03, .015, .03), "Interior", .004, 1)
    p.rbox(V(0, -D / 2 + .13, 1.05), (.5, .16, .03), "Metal", .005, 1)       # shelf
    plate(p, V(0, D / 2 + .03, H + .11), Vector((1, 0, 0)), Vector((0, 0, 1)), W - .04, .2, .02, "SignFace", None, "Metal", cell_uv("phone"), None)
    return p


def street_lamp():
    p = P2("StreetLamp_V2")
    pts = [V(0, 0, z) for z in (0, 1.0, 3.5, 6.0, 8.5)]
    p.tube(pts, [.17, .12, .095, .08, .068], "Metal", 12)
    p.lathe([(.26, 0), (.26, .1), (.2, .55), (.15, 1.05), (.0, 1.05)], "Metal", (0, 0, 1), (0, 0, 0), 14)   # base shroud
    p.rbox(V(0, .12, .5), (.14, .06, .3), "Metal", .01, 1)
    arm = []
    for i in range(14):
        t = i / 13
        arm.append(V(2.5 * t, 0, 8.4 + .95 * (1 - (1 - t) ** 2.2)))
    p.tube([V(0, 0, 8.35)] + arm, [.07] + [.058 - .016 * i / 13 for i in range(14)], "Metal", 10)
    # cobra head housing (loft along X)
    L, W, H = 1.2, .52, .36
    xs = [2.0 + L * i / 9 for i in range(10)]
    rings = []
    for i, x in enumerate(xs):
        t = i / 9
        sw = W * (.55 + .45 * math.sin(math.pi * (.15 + .85 * t))) * (1 if t > .1 else .6)
        sh = H * (.45 + .55 * math.sin(math.pi * (.1 + .8 * (1 - t) ** .8)))
        sh = max(sh, .08)
        zc = 9.1 - .02 * t
        r = []
        for k in range(14):
            a = 2 * math.pi * k / 14
            c_, s_ = math.cos(a), math.sin(a)
            r.append(Vector((x, sw * .5 * math.copysign(abs(c_) ** .7, c_), zc + sh * (.5 * math.copysign(abs(s_) ** .7, s_) if s_ > 0 else .22 * s_))))
        rings.append(r)
    p.loft(rings, "Metal")
    p.rbox(V(2.6, 0, 9.045), (.98, .44, .03), "Interior", .01, 1)
    p.rbox(V(2.6, 0, 9.03), (.86, .36, .03), "LampGlass", .014, 1)
    p.rbox(V(2.12, 0, 9.22), (.16, .14, .12), "Metal", .03, 1)
    return p


def bollard():
    p = P2("Bollard_V2")
    p.lathe([(.0, 0), (.115, 0), (.115, .02), (.085, .05), (.075, .08), (.075, .86), (.068, .89), (.05, .91), (.0, .92)], "Metal", (0, 0, 1), (0, 0, 0), 20)
    p.lathe([(.0775, .68), (.0775, .76)], "Paint", (0, 0, 1), (0, 0, 0), 20)
    p.lathe([(.0765, .66), (.0765, .78)], "Paint", (0, 0, 1), (0, 0, 0), 20)
    return p


def curb_drain():
    p = P2("CurbDrain")
    p.rbox(V(0, -.2, .09), (1.3, .5, .18), "Concrete", .01, 1)                  # curb section with the throat
    p.rbox(V(0, .02, .075), (.9, .1, .12), "Interior", .004, 1)                 # opening in the curb face
    p.rbox(V(0, .3, .008), (1.0, .55, .016), "Concrete", .004, 1)               # gutter pan
    # cast iron grate in the gutter
    p.rbox(V(0, .28, .02), (.96, .5, .024), "Metal", .004, 1)
    for i in range(9):
        p.rbox(V(-.4 + i * .1, .28, .0), (.018, .44, .01), "Interior", 0, 1)
    p.rbox(V(0, .28, .036), (.9, .44, .004), "Interior", 0, 1)
    for i in range(10):
        p.rbox(V(-.45 + i * .1, .28, .034), (.04, .44, .012), "Metal", .002, 1)
    return p


# ====================================================================== main
ITEMS = []


def make_all():
    jobs = [
        ("TrafficLight_Pole", traffic_pole, (36, 12), (700, 1000), 1.0),
        ("TrafficLight_Post", traffic_post, (35, 12), (700, 1000), 1.0),
        ("StreetSign_Post", lambda: street_sign(0), (28, 8), (700, 1000), 1.0),
        ("StreetSign_Post_B", lambda: street_sign(1), (150, 8), (700, 1000), 1.0),
        ("StopSign", stop_sign, (25, 8), (700, 1000), 1.0),
        ("NoParkingSign", lambda: rect_sign_post("nopark", 2.5, .45, .6, 2.05, "NoParkingSign"), (25, 8), (700, 1000), 1.0),
        ("BusStopSign", lambda: rect_sign_post("bus", 3.1, .45, .6, 2.65, "BusStopSign", lambda p: plate(p, V(0, .045, 2.15), Vector((1, 0, 0)), Vector((0, 0, 1)), .45, .22, .014, None, None, "Metal"))),
        ("ParkingMeter", parking_meter, (35, 10), (700, 900), .9),
        ("PhoneBooth", phone_booth, (35, 12), (800, 900), 1.0),
        ("StreetLamp_V2", street_lamp, (40, 10), (700, 1000), 1.0),
        ("Bollard_V2", bollard, (35, 12), (700, 700), 1.0),
        ("CurbDrain", curb_drain, (35, 30), (900, 600), 1.0),
    ]
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = a.split("=")[1].split(",")
    for job in jobs:
        name, fn = job[0], job[1]
        view = job[2] if len(job) > 2 and isinstance(job[2][0], (int, float)) else (25, 8)
        size = job[3] if len(job) > 3 else (700, 1000)
        if only and name not in only:
            continue
        reset_scene()
        p = fn()
        ob = build_props(p, name) if isinstance(p, P2) else p
        ob.name = name; ob.data.name = name
        report(ob, None)
        export_fbx([ob], V2_PROPS, name)
        render_item([ob], os.path.join(PREVIEWS, "V2s_%s.png" % name), az=view[0], el=view[1], size=size, ground="concrete", fit=.95 if size[1] > size[0] else 1.0, lens=60)


def main():
    reset_scene()
    if "--tex" in sys.argv or not os.path.exists(os.path.join(TEX, "StreetSigns_Albedo.png")):
        make_signs()
    if "--tex" in sys.argv or not os.path.exists(os.path.join(TEX, "RoadMarkingsV2_Albedo.png")):
        make_markings()
    make_all()


if "--noauto" not in sys.argv:
    main()
