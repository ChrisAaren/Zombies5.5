"""Street trees V2 -> Assets/Art/Models/Nature/V2/{StreetTree_A,B,C,TreePitGrate}.fbx
Textures -> Assets/Art/Textures/{LeafCards_Albedo,LeafCards_Normal,BarkV2_Albedo,BarkV2_Normal}.png
Run: blender --background --factory-startup --python make_trees_v2.py

Real branching (trunk -> limbs -> twigs, tapered, bark UVs wrapped around each tube) plus foliage as
alpha-cutout leaf-cluster cards (folded 4-tri cards) with crown-radial custom normals for soft volume.
Slots: Bark, LeafCard.  Pivot: trunk base on the ground (z = 0), trunk centered at the origin.
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_street2 import *

COLORS["LeafCard"] = (.38, .44, .18)
COLORS["Bark"] = (.38, .33, .28)


# ====================================================================== textures
def make_bark(S=512):
    n1 = noise2d(S, 2.4, 11, aniso=(.5, 2.4), sc=2.5)          # long vertical fissures
    n2 = noise2d(S, 2.2, 12, aniso=(.6, 1.4), sc=3.0)
    n3 = noise2d(S, 1.2, 13, sc=1.5)
    ridge = 1 - np.abs(n1 * 2 - 1)                       # ridges between cracks
    h = ridge ** 1.4 * .7 + n2 * .22 + n3 * .08
    crev = smoothstep(.55, .9, 1 - ridge)                # dark crevices
    v = .34 + .52 * h - .3 * crev
    v *= .88 + .24 * n3
    rgb = np.stack([v * 1.04, v * .97, v * .9], -1)
    write_png(os.path.join(TEX, "BarkV2_Albedo.png"), rgb)
    write_png(os.path.join(TEX, "BarkV2_Normal.png"), normal_from_height(h, 5.0))


LEAF_CELLS = {0: "maple cluster", 1: "plane-tree cluster", 2: "honey-locust sprig", 3: "small broadleaf filler"}


def draw_leaf(R, A, H, cx, cy, ang, L, wd, kind, col, hoff, nz, rng):
    S = R.shape[0]
    x0, x1 = max(int(cx - L * 1.05), 0), min(int(cx + L * 1.05), S)
    y0, y1 = max(int(cy - L * 1.05), 0), min(int(cy + L * 1.05), S)
    if x1 <= x0 or y1 <= y0:
        return
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    dx, dy = xs - cx, ys - cy
    ca, sa = math.cos(ang), math.sin(ang)
    u = dx * ca + dy * sa
    v = -dx * sa + dy * ca
    if kind == "lobed":
        u0 = .5 * L
        uu, rr = u - u0, np.hypot(u - u0, v)
        th = np.arctan2(v, uu)
        edge = .5 * L * (.40 + .60 * np.abs(np.cos(2.5 * th)) ** .75) * (1 + .12 * np.cos(th)) * (1 + .05 * np.sin(26 * th))
        d = edge - rr
        t = np.clip(u / L, 0, 1)
        veins = (np.abs(np.sin(2.5 * th)) < .055) & (rr < edge * .93)
        veins |= (np.abs(v) < 1.6) & (u > 0)
        petiole = (np.abs(v) < 2.3) & (u < 4) & (u > -.26 * L)
        d = np.where(petiole, np.maximum(d, 1.0), d)
    else:
        t = np.clip(u / L, 0, 1)
        hw = wd * .5 * L * (np.sin(np.pi * t ** .72)) ** .9 * (1 - .18 * t)
        d = np.where((u > 0) & (u < L), hw - np.abs(v), -1)
        veins = (np.abs(v) < 1.3) | (np.abs(np.sin((u - 1.7 * np.abs(v)) * .085)) < .09)
        petiole = (np.abs(v) < 1.7) & (u < 2) & (u > -.12 * L)
        d = np.where(petiole, np.maximum(d, 1.0), d)
    al = smoothstep(-.6, 1.3, d).astype(np.float32)
    if al.max() <= 0:
        return
    shade = (.74 + .38 * t) * (.9 + .2 * nz[y0:y1, x0:x1])
    shade = shade * (.88 + .12 * smoothstep(0, 9, d)) * np.where(veins, 1.12, 1.0)
    c = np.array(col, np.float32)[None, None, :] * shade[..., None]
    hh = hoff + .06 * t + np.where(veins, -.03, 0) + .02 * nz[y0:y1, x0:x1]
    r = R[y0:y1, x0:x1]; a = A[y0:y1, x0:x1]; h = H[y0:y1, x0:x1]
    m = al[..., None]
    r[:] = r * (1 - m) + c * m
    h[:] = h * (1 - al) + hh * al
    a[:] = a + al * (1 - a)


def cluster_cell(R, A, H, cell, nz, rng):
    S = R.shape[0]
    cx = S / 2
    pal = {0: [(.34, .43, .15), (.40, .47, .17), (.47, .50, .18), (.31, .40, .14), (.46, .48, .20)],
           1: [(.33, .41, .15), (.39, .46, .17), (.44, .48, .18), (.30, .38, .14), (.43, .46, .19)],
           2: [(.43, .50, .18), (.50, .55, .20), (.38, .46, .16), (.47, .51, .21)],
           3: [(.35, .43, .16), (.41, .47, .17), (.30, .38, .13), (.48, .5, .18)]}[cell]
    leaves = []
    if cell in (0, 1):
        L0, n = (250, 14) if cell == 0 else (320, 11)
        for i in range(n):
            for _ in range(60):
                t = rng.uniform(.0, 1.0)
                bx = cx + 30 * math.sin(t * 2.6) + rng.normal(0, 24)
                by = S - 70 - t * S * .52
                ang = -math.pi / 2 + rng.uniform(-1.15, 1.15)
                L = L0 * rng.uniform(.82, 1.12)
                ex, ey = bx + math.cos(ang) * L, by + math.sin(ang) * L
                side = L * .5
                if side + 14 < ex < S - 14 - side * .2 and ey > 20 and by < S - 8 and ex - side > 6 and ex + side < S - 6 and ey - side * .5 > 4:
                    leaves.append((bx, by, ang, L, 1.0, "lobed"))
                    break
    elif cell == 2:
        def sprig(bx, by, ang, nl, Lf):
            curve = rng.uniform(-.25, .25)
            x, y, a = bx, by, ang
            step = Lf * .62
            for k in range(nl):
                x += math.cos(a) * step; y += math.sin(a) * step; a += curve * .12
                s = Lf * (1.0 - .35 * k / nl) * rng.uniform(.9, 1.1)
                for sd in (-1, 1):
                    la = a + sd * rng.uniform(.95, 1.25)
                    leaves.append((x, y, la, s, .42, "ovate"))
            leaves.append((x, y, a, Lf * .9, .42, "ovate"))
        sprig(cx, S - 40, -math.pi / 2 + .05, 9, 112)
        sprig(cx - 20, S - 330, -math.pi / 2 - .75, 6, 100)
        sprig(cx + 10, S - 480, -math.pi / 2 + .7, 5, 100)
        
    else:
        for tw in range(4):
            a0 = -math.pi / 2 + (tw - 1.5) * .42
            for k in range(6):
                bx = cx + (tw - 1.5) * 90 + math.cos(a0) * k * 70
                by = S - 90 + math.sin(a0) * k * 85
                for sd in (-1, 1):
                    leaves.append((bx, by, a0 + sd * rng.uniform(.6, 1.1), rng.uniform(150, 190), .55, "ovate"))
    order = list(range(len(leaves)))
    rng.shuffle(order)
    for rank, i in enumerate(order):
        bx, by, ang, L, wd, kind = leaves[i]
        col = pal[rng.integers(len(pal))]
        depth = rank / max(len(order) - 1, 1)
        col = tuple(c * (.78 + .22 * depth) for c in col)
        draw_leaf(R, A, H, bx, by, ang, L, wd, kind, col, depth * .35, nz, rng)
    # thin twig lines for the lobed clusters (petioles meet here)
    if cell in (0, 1):
        ys, xs = np.mgrid[0:S, 0:S].astype(np.float32)
        tt = np.clip((S - 40 - ys) / (S * .55), 0, 1)
        xc = cx + 30 * np.sin(tt * 2.6)
        tw = (np.abs(xs - xc) < 4 * (1 - .5 * tt)) & (ys > S - 40 - S * .55) & (ys < S - 20)
        woody = np.array((.30, .26, .16), np.float32)
        R[tw] = woody; A[tw] = 1; H[tw] = .1
    elif cell == 2:
        pass


def make_leaf_atlas(S=2048):
    C = S // 2
    rng = np.random.default_rng(21)
    nz = noise2d(S, 1.6, 5)
    R = np.empty((S, S, 3), np.float32); R[:] = (.37, .44, .17)
    A = np.zeros((S, S), np.float32); H = np.zeros((S, S), np.float32)
    for cell in range(4):
        cx0, cy0 = (cell % 2) * C, (cell // 2) * C
        sl = (slice(cy0, cy0 + C), slice(cx0, cx0 + C))
        cluster_cell(R[sl], A[sl], H[sl], cell, nz[sl], rng)
    H = blur(H, 1)
    rgb = np.clip(R, 0, 1)
    out = np.dstack([rgb, A])
    write_png(os.path.join(TEX, "LeafCards_Albedo.png"), out)
    nm = normal_from_height(H, 14.0)
    nm[A < .02] = (.5, .5, 1.0)
    write_png(os.path.join(TEX, "LeafCards_Normal.png"), nm)
    print("leaf mean rgb(opaque)", rgb[A > .5].mean(0))


# ====================================================================== geometry
def add_tube(p, pts, radii, n, mat, rng, uvu=1.0):
    """Tapered tube with parallel-transport frames and bark UVs (u around, v along, in meters)."""
    bm = p.bm; uvl = p.uvl(); mi = p.mi(mat)
    rings = []
    u = None
    for i, q in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if u is None:
            ref = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
            u = t.cross(ref).normalized()
        else:
            u = (u - t * u.dot(t)).normalized()
        w = t.cross(u).normalized()
        r = radii[i]
        rings.append([bm.verts.new(q + (u * math.cos(2 * math.pi * j / n) + w * math.sin(2 * math.pi * j / n)) * r) for j in range(n)])
    v0 = rng.random()
    cum = [0.0]
    for i in range(1, len(pts)):
        cum.append(cum[-1] + (pts[i] - pts[i - 1]).length)
    C = max(2 * math.pi * sum(radii) / len(radii), .22) * uvu * 2.0
    for i in range(len(pts) - 1):
        for j in range(n):
            j2 = (j + 1) % n
            try:
                f = bm.faces.new((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]))
            except ValueError:
                continue
            f.material_index = mi
            for l, (jj, ii) in zip(f.loops, ((j, i), (j + 1, i), (j + 1, i + 1), (j, i + 1))):
                l[uvl].uv = (jj / n * C, (v0 + cum[ii]) * 2.0)
    return rings


def add_card(p, c, up, right, w, h, cell, rng, fold=.16):
    """Folded alpha card. c = bottom-center. UV cell (0..3) in the 2x2 atlas."""
    bm = p.bm; uvl = p.uvl(); mi = p.mi("LeafCard")
    nrm = right.cross(up).normalized()
    cx, cy = cell % 2, cell // 2
    pad = .006
    u0, u1 = cx * .5 + pad, cx * .5 + .5 - pad
    vb, vt = 1 - (cy * .5 + .5) + pad, 1 - cy * .5 - pad        # PNG row 0 is the top => v flipped
    vv = [bm.verts.new(c + right * (sx * w / 2) + up * (ty * h) + nrm * (fold * w * (1 - abs(sx)) * (.5 + .5 * ty * 0 + .5)))
          for ty in (0, 1) for sx in (-1, 0, 1)]
    uvs = {}
    for k, (ty, sx) in enumerate([(ty, sx) for ty in (0, 1) for sx in (-1, 0, 1)]):
        uvs[k] = ((u0 + (sx + 1) / 2 * (u1 - u0)), (vb + ty * (vt - vb)))
    for quad in ((0, 1, 4, 3), (1, 2, 5, 4)):
        f = bm.faces.new([vv[i] for i in quad]); f.material_index = mi
        for l, i in zip(f.loops, quad):
            l[uvl].uv = uvs[i]


def lerp(a, b, t):
    return a + (b - a) * t


def rot_about(v, axis, ang):
    return Matrix.Rotation(ang, 3, axis) @ v


class Tree:
    def __init__(self, cfg, seed):
        self.c = cfg
        self.rng = random.Random(seed)
        self.nrng = np.random.default_rng(seed)
        self.p = P2("tree")
        self.tips = []     # (position, direction, level)

    def rv(self, a=1.0):
        r = self.rng
        return Vector((r.uniform(-1, 1), r.uniform(-1, 1), r.uniform(-1, 1))) * a

    def branch(self, start, d, length, r0, r1, level):
        c = self.c
        segs = c["segs"][level]
        pts = [Vector(start)]
        d = Vector(d).normalized()
        for i in range(segs):
            d = (d + self.rv(c["wob"][level]) + Vector((0, 0, c["trop"][level]))).normalized()
            pts.append(pts[-1] + d * (length / segs))
        radii = [lerp(r0, r1, (i / segs) ** .85) for i in range(segs + 1)]
        add_tube(self.p, pts, radii, c["sides"][level], "Bark", self.rng)
        if level < 3:
            nk = c["kids"][level]
            for k in range(nk):
                t = lerp(c["kid_t0"][level], 1.0, (k + self.rng.random() * .7) / nk)
                i = min(int(t * segs), segs - 1)
                f = t * segs - i
                pos = pts[i].lerp(pts[i + 1], f)
                pd = (pts[i + 1] - pts[i]).normalized()
                ax = pd.cross(Vector((0, 0, 1)))
                if ax.length < 1e-3:
                    ax = Vector((1, 0, 0))
                ax.normalize()
                ang = math.radians(c["kid_ang"][level] * self.rng.uniform(.75, 1.25))
                nd = rot_about(pd, ax, ang)
                nd = rot_about(nd, pd, self.rng.uniform(0, 2 * math.pi) + k * 2.4)
                rem = 1.0 - t
                ln = length * c["kid_len"][level] * (.55 + .6 * rem) * self.rng.uniform(.8, 1.15)
                rk = lerp(r0, r1, t ** .85) * c["kid_r"][level]
                self.branch(pos - pd * .02, nd, ln, rk, max(rk * .35, .006), level + 1)
        else:
            pass
        self.tips.append((pts[-1], d, level))
        if level >= 2:
            for t in (.5,):
                i = int(len(pts) * t)
                self.tips.append((pts[i], (pts[min(i + 1, len(pts) - 1)] - pts[i]).normalized(), level))

    def grow(self):
        c, rng = self.c, self.rng
        H0 = c["split"]
        # trunk (leaner, root flare)
        n = 15
        lean = Vector(c["lean"])
        pts = []
        for i in range(n):
            t = i / (n - 1)
            z = -.05 + (H0 + .05) * t
            sway = Vector((math.sin(t * 3.1 + c["seed_ph"]) * c["sway"], math.cos(t * 2.3 + c["seed_ph"]) * c["sway"], 0))
            pts.append(Vector((0, 0, z)) + lean * t + sway * t)
        rt = c["r_base"]
        radii = [lerp(rt * 1.45, rt * .78, min(1, (i / (n - 1)) * 1.6)) if i < n * .45 else lerp(rt * .98 * .78 , rt * .66, (i / (n - 1) - .45) / .55 ) for i in range(n)]
        radii = [max(radii[i], .001) for i in range(n)]
        for i in range(n):   # enforce monotone, with flare at the foot
            if i > 2:
                radii[i] = min(radii[i], radii[i - 1] * .995)
        radii[-1] *= .8; radii[-2] *= .93
        add_tube(self.p, pts, radii, c["sides"][0] + 3, "Bark", rng)
        top = pts[-1]
        rtop = radii[-1]
        # limbs
        N = c["limbs"]
        a0 = rng.uniform(0, 6.28)
        for k in range(N):
            az = a0 + k * 2.39996 + rng.uniform(-.25, .25)
            el = math.radians(rng.uniform(*c["limb_el"]))
            d = Vector((math.sin(el) * math.cos(az), math.sin(el) * math.sin(az), math.cos(el)))
            zs = lerp(H0 - c["limb_drop"], H0, k / max(N - 1, 1))
            t = (zs + .05) / (H0 + .05)
            i = min(int(t * (n - 1)), n - 2)
            pos = pts[i].lerp(pts[i + 1], t * (n - 1) - i)
            ln = rng.uniform(*c["limb_len"])
            self.branch(pos, d, ln, rtop * c["limb_r"] * rng.uniform(.85, 1.1), rtop * .22, 1)
        # leader
        if c["leader"] > 0:
            self.branch(top - Vector((0, 0, .12)), Vector((rng.uniform(-.15, .15), rng.uniform(-.15, .15), 1)), c["leader"], rtop * 1.0, rtop * .22, 1)

    def foliage(self):
        c, rng = self.c, self.rng
        cc = Vector(c["crown_c"])
        tips = [t for t in self.tips if t[2] >= 2 and t[0].z > c["foliage_min_z"]]
        rng.shuffle(tips)
        target = c["cards"]
        cards = 0
        i = 0
        while cards < target and tips:
            pos, d, lv = tips[i % len(tips)]
            i += 1
            out = (pos - cc)
            out = Vector((out.x / c["crown_r"][0], out.y / c["crown_r"][0], out.z / c["crown_r"][1]))
            on = out.normalized() if out.length > 1e-4 else Vector((0, 0, 1))
            # card up axis: mostly along the twig, nudged outward/up
            up = (d * .55 + on * .35 + Vector((0, 0, .25))).normalized()
            roll = rng.uniform(0, 2 * math.pi)
            ref = Vector((0, 0, 1)) if abs(up.z) < .9 else Vector((1, 0, 0))
            right = up.cross(ref).normalized()
            right = rot_about(right, up, roll)
            sz = rng.uniform(*c["card"])
            cell = c["cells"][rng.randrange(len(c["cells"]))]
            hgt = sz * c["card_aspect"]
            base = pos - up * hgt * .18 + self.rv(.12)
            add_card(self.p, base, up, right, sz, hgt, cell, rng, c["fold"])
            cards += 1
            if cards > target * 3:
                break

    def finish(self, name):
        ob = self.p.build2(name=name, smooth=0, keep_uv=True)
        me = ob.data
        # crown-radial custom normals on foliage, plain smooth on bark
        li = [i for i, m in enumerate(me.materials) if m and m.name == "LeafCard"][0]
        for po in me.polygons:
            po.use_smooth = True
        cc = Vector(self.c["crown_c"])
        try:
            cn = [tuple(n.vector) for n in me.corner_normals]
            for po in me.polygons:
                if po.material_index == li:
                    fn = Vector(po.normal)
                    ctr = Vector(po.center)
                    if fn.dot(ctr - cc) < 0:
                        fn = -fn
                    for l in po.loop_indices:
                        vp = me.vertices[me.loops[l].vertex_index].co
                        rad = (vp - cc)
                        rad = Vector((rad.x / self.c["crown_r"][0], rad.y / self.c["crown_r"][0], rad.z / self.c["crown_r"][1])).normalized()
                        n = (rad * .75 + fn * .25 + Vector((0, 0, .12))).normalized()
                        cn[l] = tuple(n)
            me.normals_split_custom_set(cn)
        except Exception as e:
            print("custom normals failed:", e)
        return ob


CFG_A = dict(  # London-plane: tall, broad, big leaves, pale mottled trunk
    split=3.3, r_base=.27, lean=(.12, .05, 0), sway=.05, seed_ph=.4, limbs=5, limb_drop=.5, limb_el=(38, 62), limb_len=(2.9, 3.5),
    limb_r=.62, leader=1.9,
    sides=[6, 6, 5, 4], segs=[0, 7, 4, 3], wob=[0, .11, .22, .3], trop=[0, .06, .04, .02],
    kids=[0, 5, 3, 2], kid_t0=[0, .3, .3, .3], kid_ang=[0, 42, 48, 50], kid_len=[0, .55, .55, .5], kid_r=[0, .5, .6, .6],
    crown_c=(0, 0, 6.3), crown_r=(4.4, 3.1), foliage_min_z=3.8, cards=420, card=(2.3, 3.2), card_aspect=1.0, fold=.14, cells=[1, 1, 1, 3])
CFG_B = dict(  # honey-locust: vase-shaped, airy, fine compound foliage
    split=2.9, r_base=.19, lean=(-.15, .1, 0), sway=.06, seed_ph=1.7, limbs=6, limb_drop=.5, limb_el=(24, 40), limb_len=(2.6, 3.3),
    limb_r=.55, leader=1.7,
    sides=[6, 5, 4, 4], segs=[0, 6, 4, 3], wob=[0, .14, .26, .34], trop=[0, .05, .03, .02],
    kids=[0, 6, 4, 2], kid_t0=[0, .25, .3, .3], kid_ang=[0, 44, 50, 55], kid_len=[0, .5, .55, .5], kid_r=[0, .45, .55, .6],
    crown_c=(0, 0, 5.6), crown_r=(3.5, 3.0), foliage_min_z=3.4, cards=430, card=(1.5, 2.2), card_aspect=1.15, fold=.1, cells=[2, 2, 2, 3])
CFG_C = dict(  # young maple: slim, oval, dense
    split=2.2, r_base=.15, lean=(.05, -.08, 0), sway=.035, seed_ph=2.9, limbs=7, limb_drop=1.4, limb_el=(20, 40), limb_len=(1.8, 2.5),
    limb_r=.5, leader=2.3,
    sides=[6, 5, 4, 4], segs=[0, 6, 4, 3], wob=[0, .1, .2, .28], trop=[0, .07, .05, .03],
    kids=[0, 5, 3, 2], kid_t0=[0, .2, .3, .3], kid_ang=[0, 40, 48, 50], kid_len=[0, .5, .55, .5], kid_r=[0, .5, .6, .6],
    crown_c=(0, 0, 4.6), crown_r=(2.7, 2.6), foliage_min_z=2.6, cards=430, card=(1.6, 2.3), card_aspect=1.0, fold=.15, cells=[0, 0, 0, 3])


def make_tree(name, cfg, seed):
    t = Tree(cfg, seed)
    t.grow()
    t.foliage()
    return t.finish(name)


def make_grate():
    p = Part("TreePitGrate")
    H, T = 1.2, .035
    hole = .2                  # trunk hole radius
    # frame
    for sx in (-1, 1):
        p.rbox(V(sx * (H / 2 - .03), 0, T / 2 + .0), (.06, H, T), "Metal", .005, 1)
        p.rbox(V(0, sx * (H / 2 - .03), T / 2), (H - .12, .06, T), "Metal", .005, 1)
    inner = H / 2 - .06
    step = .1
    xs = [-inner + step * (k + .5) for k in range(int(2 * inner / step))]
    for c in xs:                       # bars along forward, clipped around the trunk hole
        lim = math.sqrt(max((hole + .02) ** 2 - c * c, 0))
        segs = [(-inner, inner)] if lim == 0 else [(-inner, -lim), (lim, inner)]
        for a, b in segs:
            p.rbox(V(c, (a + b) / 2, T * .45), (.022, b - a, T * .9), "Metal", .003, 1)
    for c in xs[::2]:                  # cross ribs, fewer
        lim = math.sqrt(max((hole + .02) ** 2 - c * c, 0))
        segs = [(-inner, inner)] if lim == 0 else [(-inner, -lim), (lim, inner)]
        for a, b in segs:
            p.rbox(V((a + b) / 2, c, T * .3), (b - a, .02, T * .6), "Metal", .003, 1)
    p.lathe([(hole + .03, 0), (hole + .03, T), (hole - .005, T), (hole - .005, 0)], "Metal", (0, 0, 1), (0, 0, 0), 20)
    ob = p.build(smooth=40)
    return ob


def main():
    reset_scene()
    if "--tex" in sys.argv or not os.path.exists(os.path.join(TEX, "LeafCards_Albedo.png")):
        make_bark()
        make_leaf_atlas()
    reset_scene()
    items = []
    for name, cfg, seed in (("StreetTree_A", CFG_A, 7), ("StreetTree_B", CFG_B, 19), ("StreetTree_C", CFG_C, 31)):
        reset_scene()
        ob = make_tree(name, cfg, seed)
        u, umin, umax = report(ob, None)
        export_fbx([ob], V2_NATURE, name)
        render_item([ob], os.path.join(PREVIEWS, "V2s_%s.png" % name), az=25, el=9, size=(800, 1000), ground="concrete", fit=.8, lens=60)
    reset_scene()
    g = make_grate()
    report(g, (1.2, .035, 1.2), .05)
    export_fbx([g], V2_NATURE, "TreePitGrate")
    render_item([g], os.path.join(PREVIEWS, "V2s_TreePitGrate.png"), az=30, el=40, size=(800, 600), ground="concrete")


if "--noauto" not in sys.argv:
    main()
