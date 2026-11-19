"""Vehicles V3 -> Assets/Art/Models/Vehicles/V3/CarV3_{Sedan,Compact,Hatch,Minivan,SUV,Pickup,Van,Cab}.fbx
Run: blender --background --factory-startup --python make_vehicles_v3.py [-- --only=Sedan,Cab] [--noprev] [--nolineup]

Same conventions as V2: pivot = ground, between the wheels (bottom centre); forward = Unity +Z;
slots CarPaint, Glass, Chrome, Rubber, Interior, Headlight, Taillight, Metal, Paint, Plate (+LampGlass on the Cab);
2nd UV map "UVDirt" (-> uv2) for CarDirt_Mask; Plate quads UV into PlatesV2_Albedo cells.

Method (what is new compared with V2):
 * Body: a QUAD CAGE of ~18-vertex cross-sections lofted along the car (stations are denser at the nose / tail / pillars),
   shoulder and sill edges carry crease weights, then a Subdivision Surface (level 2) is APPLIED.
   Ring = sill roll, tumblehomed flanks, crisp shoulder line, crowned hood/deck. Plan view = corner radii + fender bulges.
 * Greenhouse: its own quad cage (belt, tumblehomed glass, rail band, roof crown) -> subsurf; windows are the Glass cells,
   cut out with a rubber gasket and re-created as a SEPARATE glass shell; a lining shell + seats / dash / wheel sit inside.
 * Arches are boolean-cut from the subdivided body and lined with a wheel-well shell (real depth); wheels sit flush.
 * Lamps / plates are conformal patches ray-projected onto the finished body (lens, reflector cups, bulbs, recessed plate).
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_street2 import *

V3_VEH = "Vehicles/V3"
COLORS["Plate"] = (.9, .9, .85)
ARGS = [a for a in sys.argv if a.startswith("--")]


# ====================================================================================== small helpers
def lerp(a, b, t):
    return a + (b - a) * t


def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def plin(pts):
    """piecewise-linear f(x) through sorted (x, y)"""
    pts = sorted(pts)
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return lambda x: float(np.interp(x, xs, ys))


class Cage:
    """bmesh with named material slots"""
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []

    def mi(self, m):
        if m not in self.slots:
            self.slots.append(m)
        return self.slots.index(m)

    def grid(self, rows, mat_fn, closed=True, flip=False):
        bm = self.bm
        vs = [[bm.verts.new(p) for p in r] for r in rows]
        n = len(rows[0]); m = n if closed else n - 1
        out = {}
        for i in range(len(rows) - 1):
            for j in range(m):
                q = [vs[i][j], vs[i][(j + 1) % n], vs[i + 1][(j + 1) % n], vs[i + 1][j]]
                if flip:
                    q = q[::-1]
                if len(set(q)) < 3:
                    continue
                try:
                    f = bm.faces.new(q)
                except ValueError:
                    continue
                f.material_index = self.mi(mat_fn(i, j) if callable(mat_fn) else mat_fn)
                out[(i, j)] = f
        return vs, out


def orient_outward(bm, center, outward=True):
    """flip the whole shell if its normals point to the wrong side of `center` on average"""
    bm.normal_update()
    s = 0.0
    for f in bm.faces:
        s += (f.calc_center_median() - center).dot(f.normal) * f.calc_area()
    if (s < 0) == outward:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])


def to_object(cg, name, creases=None, levels=0, smooth=42):
    bm = cg.bm
    if creases:
        cl = bm.edges.layers.float.new("crease_edge")
        for e in bm.edges:
            w = creases(e)
            if w:
                e[cl] = w
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    for s in cg.slots:
        me.materials.append(mat(s))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    select_only(ob)
    if levels:
        md = ob.modifiers.new("ss", 'SUBSURF')
        md.levels = levels; md.render_levels = levels
        bpy.ops.object.modifier_apply(modifier=md.name)
    for p in me.polygons:
        p.use_smooth = True
    if smooth:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
    return ob


def edit_mesh(ob, fn):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    fn(bm)
    bm.to_mesh(ob.data); bm.free()
    ob.data.update()


# ====================================================================================== body cage
def make_funcs(s):
    s["z0f"] = plin(s["z0"]); s["ztf"] = plin(s["zt"])
    fmin, fmax, hw0 = s["fmin"], s["fmax"], s["hw0"]
    rcf, rcr = s.get("rcf", .55), s.get("rcr", .45)
    wf, wr_ = s["wheels"]

    def tap(d, rc):
        return hw0 if d >= rc else hw0 - rc + math.sqrt(max(rc * rc - (rc - d) ** 2, 0.0))

    def hwf(f):
        b = min(tap(fmax - f, rcf), tap(f - fmin, rcr))
        bulge = .028 * (math.exp(-((f - wf) / .5) ** 2) + math.exp(-((f - wr_) / .5) ** 2)) - .008
        return b + bulge * clamp(min(fmax - f, f - fmin) / .5)
    s["hwf"] = hwf
    roof = list(s["roof"])
    fw0, fb0 = roof[0][0], roof[-1][0]
    s["fw0"], s["fb0"] = fw0, fb0
    pts = []
    for k, (f, z) in enumerate(roof):
        if z is None:
            z = s["ztf"](f) + .03
        pts.append((f, z))
    s["roofpts"] = pts
    s["zrf"] = plin(pts)
    s["shp"] = s.get("sh", (.955, .925, .875))


def body_ring(s, f):
    hw = s["hwf"](f); z0 = s["z0f"](f); zt = s["ztf"](f)
    a, b, c = s["shp"]
    H = max(zt - z0, .1)
    R = [(0, z0), (.70 * hw, z0), (.955 * hw, z0 + .05), (.99 * hw, z0 + .20 * H), (hw, z0 + .50 * H),
         (a * hw, zt - .10 * H), (b * hw, zt - .004), (c * hw, zt + .004), (.5 * hw, zt + .014), (0, zt + .02)]
    right = [V(x, f, z) for x, z in R]
    left = [V(-x, f, z) for x, z in reversed(R[1:-1])]
    return right + left


def body_stations(s):
    fmin, fmax = s["fmin"], s["fmax"]
    st = [fmin + d for d in (0, .04, .12, .26, .5)] + [fmax - d for d in (0, .04, .12, .26, .5)]
    st += [f for f, _ in s["zt"]] + [s["fw0"], s["fb0"]] + list(s.get("body_extra", []))
    st = sorted(set(round(x, 3) for x in st if fmin - 1e-6 <= x <= fmax + 1e-6))
    out = [st[0]]
    for x in st[1:]:
        if x - out[-1] >= .035:
            out.append(x)
    fill = []
    for a, b in zip(out[:-1], out[1:]):
        n = int((b - a) / .64)
        fill += [a + (b - a) * k / (n + 1) for k in range(1, n + 1)]
    return sorted(out + fill)


def make_body(s):
    cg = Cage("body")
    fs = body_stations(s)
    rows = [body_ring(s, f) for f in fs]
    n = len(rows[0])
    vs, faces = cg.grid(rows, "CarPaint")
    for ring in (vs[0], vs[-1]):
        try:
            f = cg.bm.faces.new(ring[::-1] if ring is vs[0] else ring)
            f.material_index = cg.mi("CarPaint")
        except ValueError:
            pass
    c = Vector((0, -(s["fmin"] + s["fmax"]) / 2, .6))
    orient_outward(cg.bm, c)
    ui = cg.mi("Metal")
    cg.bm.normal_update()
    for f in cg.bm.faces:
        if f.normal.z < -.7:
            f.material_index = ui
    keyv = {}
    for i, ring in enumerate(vs):
        for j, v in enumerate(ring):
            keyv[v] = (i, j)
    # left half indices: ring = right[0..9] + left[R8..R1] -> left of R_k is index 10 + (8 - k)
    def creases2(e):
        a, b = e.verts
        (ia, ja), (ib, jb) = keyv.get(a, (0, 0)), keyv.get(b, (0, 0))
        if ja == jb and ia != ib:
            if ja in (6, 10 + (8 - 6)):
                return .85
            if ja in (2, 10 + (8 - 2)):
                return .7
        return 0
    return to_object(cg, "body", creases2, levels=s.get("ss_body", 2))


# ====================================================================================== greenhouse cage
def cab_stations(s):
    pts = s["roofpts"]
    st = [p[0] for p in pts]
    for k in range(1, len(pts) - 1):
        f = pts[k][0]
        for sgn, nb in ((1, pts[k - 1][0]), (-1, pts[k + 1][0])):
            d = min(s.get("sup", .13), abs(nb - f) * .42)
            st.append(f + (nb - f) / abs(nb - f) * d)
    for fa, fb in s.get("side_win", []):
        st += [fa, fb]
    st += list(s.get("cab_extra", []))
    st = sorted(set(round(x, 3) for x in st), reverse=True)
    out = [st[0]]
    for x in st[1:]:
        if out[-1] - x >= .06:
            out.append(x)
    if abs(out[-1] - pts[-1][0]) > .001:
        out[-1] = pts[-1][0]
    return out


def cab_ring_pts(s, f, inset=0.0):
    zb = s["ztf"](f) + .004
    zr = max(s["zrf"](f), zb + .012)
    h = zr - zb
    hw = s["hwf"](f)
    cb = s["shp"][2] * hw
    fw0, fb0 = s["fw0"], s["fb0"]
    fr0, fr1 = s["roofpts"][1][0], s["roofpts"][-2][0]
    tum = s.get("tumb", .17)
    if f > fr0:
        t = clamp((fw0 - f) / max(fw0 - fr0, 1e-3))
    elif f < fr1:
        t = clamp((f - fb0) / max(fr1 - fb0, 1e-3))
    else:
        t = 1.0
    t = t * t * (3 - 2 * t)
    tm = tum * (.12 + .88 * t)
    cr = cb - tm
    dz = min(.095, .30 * h)
    half = [(cb, zb - .03), (cb - .004, zb + min(.035, .3 * h)), (cb - tm * .42, zb + h * .46),
            (cr + .02, zr - dz), (cr - .015, zr - .014), (cr * .62, zr - .004), (0, zr)]
    if inset:
        half = [(max(x - inset, 0.0) if x > .01 else 0.0, z - (inset if k >= 3 else 0)) for k, (x, z) in enumerate(half)]
    return half


def cab_ring(s, f, inset=0.0):
    half = cab_ring_pts(s, f, inset)
    return [V(-x, f, z) for x, z in half[:-1]] + [V(0, f, half[-1][1])] + [V(x, f, z) for x, z in reversed(half[:-1])]


def cab_mat_fn(s, fs):
    ws, bl = s["ws"], s.get("bl")

    def inr(a, b, rng):
        return rng[1] - 1e-4 <= b and a <= rng[0] + 1e-4

    def mat_fn(i, j):
        k = j if j < 6 else 11 - j
        a, b = fs[i], fs[i + 1]
        if inr(a, b, ws) and k in s.get("ws_k", (4, 5)):
            return "Glass"
        if bl and inr(a, b, bl) and k in s.get("bl_k", (4, 5)):
            return "Glass"
        if k in (1, 2) and any(inr(a, b, w) for w in s.get("side_win", [])):
            return "Glass"
        return "CarPaint"
    return mat_fn


def make_cabin(s):
    fs = cab_stations(s)
    rows = [cab_ring(s, f) for f in fs]
    mat_fn = cab_mat_fn(s, fs)
    cg = Cage("cabin")
    vs, faces = cg.grid(rows, mat_fn, closed=False)
    cg.mi("Glass")
    for ring, rev in ((vs[0], True), (vs[-1], False)):
        try:
            f = cg.bm.faces.new(ring[::-1] if rev else ring)
            f.material_index = cg.mi("CarPaint")
        except ValueError:
            pass
    orient_outward(cg.bm, Vector((0, -sum(fs) / len(fs), s["ztf"](0) - .4)))
    keyv = {}
    for i, ring in enumerate(vs):
        for j, v in enumerate(ring):
            keyv[v] = (i, j)

    def creases(e):
        a, b = e.verts
        (ia, ja), (ib, jb) = keyv.get(a, (0, 0)), keyv.get(b, (0, 0))
        nr = len(vs)
        if (ia == ib and ia in (0, nr - 1)) or (ja == jb and ia != ib and ja in (0, 12)):
            return 1.0                                        # keep the base outline exactly where it was built
        if ja == jb and ia != ib and ja in (3, 9):       # rail line
            return .35
        if ja == jb and ia != ib and ja in (1, 11):      # belt edge
            return .5
        return 0
    ob = to_object(cg, "cabin", creases, levels=s.get("ss_cab", 2))
    return ob


def make_liner(s):
    """lining shell inside the greenhouse (headliner, pillars, door-top), normals inward, Interior slot; open at the windows"""
    fs = cab_stations(s)
    rows = [cab_ring(s, f, inset=.06) for f in fs]
    mf = cab_mat_fn(s, fs)
    cg = Cage("liner")
    vs, faces = cg.grid(rows, "Interior", closed=False)
    for (i, j), f in list(faces.items()):
        if mf(i, j) == "Glass":
            cg.bm.faces.remove(f)
    orient_outward(cg.bm, Vector((0, -sum(fs) / len(fs), 0.3)), outward=False)
    return to_object(cg, "liner", None, levels=1, smooth=0)


# ====================================================================================== wheels
TIRE_HALF = [(.205, .092), (.216, .103), (.240, .110), (.268, .113), (.295, .104), (.318, .082), (.330, .056), (.330, 0)]


def wheel(P, s, x, f, side):
    wr, tw, k = s["wr"], s["tw"], s["wr"] / .33
    t = tw / .21
    ax = Vector((side, 0, 0))
    c = V(x, f, wr)
    half = TIRE_HALF
    prof = [(r * k, -h * t) for r, h in half] + [(r * k, h * t) for r, h in reversed(half[:-1])]
    P.lathe([(r, h * side) for r, h in prof], "Rubber", Vector((1, 0, 0)), c, 18)
    h0 = tw / 2 - .012                          # outer plane of the rim lip
    ro = .203 * k
    rim = s["rim"]
    X = Vector((1, 0, 0))
    # inner barrel (dark) behind the dish
    P.lathe([(ro, (h0 - .02) * side), (ro * .93, -(h0 - .02) * side)], "Metal", X, c, 18)
    dish = .05 if rim.startswith("alloy") else .028
    if rim.startswith("alloy"):
        # lip, dished face, centre cap
        P.lathe([(ro * 1.0, (h0 + .006) * side), (ro * .985, (h0 - .006) * side), (ro * .90, (h0 - .014) * side),
                 (ro * .70, (h0 - dish) * side), (ro * .26, (h0 - dish - .004) * side)], "Chrome", X, c, 18)
        ns = {"alloy5": 5, "alloy6": 6, "alloy10": 10}[rim]
        for i in range(ns):
            a = 2 * math.pi * i / ns + .3
            d = Vector((0, -math.cos(a), math.sin(a)))
            dp = Vector((0, -d.z, d.y))
            p0 = c + ax * ((h0 - dish) * 1.0) + d * .05 * k
            p1 = c + ax * (h0 - .016) + d * .195 * k
            wdt = .062 * k if ns <= 6 else .034 * k
            P.bar(p0 - dp * .004, p1 + dp * .006, .03, wdt, "Chrome", .004, 1)
        P.lathe([(.075 * k, (h0 - dish + .004) * side), (.06 * k, (h0 - dish + .016) * side), (0, (h0 - dish + .02) * side)], "Metal", X, c, 14)
    else:
        P.lathe([(ro * 1.0, (h0 + .004) * side), (ro * .97, (h0 - .008) * side), (ro * .88, (h0 - .02) * side), (ro * .5, (h0 - dish) * side)],
                "Metal", X, c, 18)
        # plastic hubcap with vent slots + dome
        P.lathe([(ro * .86, (h0 - .014) * side), (ro * .80, (h0 + .004) * side), (ro * .52, (h0 + .012) * side),
                 (ro * .30, (h0 + .02) * side), (ro * .12, (h0 + .026) * side), (0, (h0 + .028) * side)], "Chrome", X, c, 18)
        for i in range(8):
            a = 2 * math.pi * (i + .5) / 8
            d = Vector((0, math.cos(a), math.sin(a)))
            P.cyl(c + d * .15 * k + ax * (h0 + .006), .014, .014, .012, "Rubber", 6, ax.to_track_quat('Z', 'Y'))
    for i in range(5):
        a = 2 * math.pi * i / 5
        d = Vector((0, math.cos(a), math.sin(a)))
        P.cyl(c + d * .052 * k + ax * (h0 - dish + .006 if rim.startswith("alloy") else h0 + .02), .011, .011, .016, "Chrome", 5, ax.to_track_quat('Z', 'Y'))
    # brake disc + caliper just behind the spokes
    P.lathe([(.165 * k, (h0 - .105) * side), (.165 * k, (h0 - .09) * side), (.07 * k, (h0 - .09) * side)], "Metal", X, c, 18)
    P.rbox(c + Vector((0, .12 * k, .06 * k)) + ax * (h0 - .12), (.045, .08 * k, .11 * k), "Paint", 0.0, 1)


def aim_xy(ax, d):
    return Matrix((ax.cross(d), d, ax)).transposed().to_quaternion() if False else ax.to_track_quat('Z', 'Y')


def well_shell(P, s, sd, wf):
    wr = s["wr"]
    R = wr + .05
    xo = s["hw0"] + .015
    xi = s["track"] - .165
    n = 22
    angs = [math.radians(-14 + 208 * i / (n - 1)) for i in range(n)]

    def ring_at(x):
        return [V(x, wf + R * math.cos(a), wr + .012 + R * math.sin(a)) for a in angs]
    xs = [xo, (xo + xi) / 2 + .02, xi + .02]
    rows = [ring_at(x * sd) for x in xs]
    bm = P.bm
    vs = [[bm.verts.new(p) for p in r] for r in rows]
    mi = P.mi("Rubber")
    for i in range(len(rows) - 1):
        for j in range(n - 1):
            q = [vs[i][j], vs[i][j + 1], vs[i + 1][j + 1], vs[i + 1][j]]
            f = bm.faces.new(q); f.material_index = mi
    # inner wall: fan of the inner ring
    ctr = bm.verts.new(V(sd * xi, wf, wr + .012))
    for j in range(n - 1):
        f = bm.faces.new([ctr, vs[2][j], vs[2][j + 1]] if sd > 0 else [ctr, vs[2][j + 1], vs[2][j]])
        f.material_index = mi


# ====================================================================================== details
class Surf:
    """ray tools on the finished shell"""
    def __init__(self, ob):
        bpy.context.view_layer.update()
        self.bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())

    def ray(self, o, d):
        loc, n, i, dist = self.bvh.ray_cast(Vector(o), Vector(d).normalized())
        return (loc, n) if loc is not None else (None, None)

    def fb(self, x, z, sg):            # onto nose (sg>0) or tail (sg<0)
        return self.ray(V(x, math.copysign(7, sg), z), V(0, -math.copysign(1, sg), 0) - V(0, 0, 0))

    def side(self, f, z, sd):
        return self.ray(Vector((sd * 7, -f, z)), Vector((-sd, 0, 0)))

    def top(self, x, f):
        return self.ray(V(x, f, 4.0), Vector((0, 0, -1)))

    def lift(self, h, k=.003):
        return None if h[0] is None else h[0] + h[1] * k


def groove(D, pts, w=.0042):
    pts = [q for q in pts if q is not None]
    if len(pts) > 1:
        D.tube(pts, [(w, w * 1.6)] * len(pts), "Rubber", 3, cap=False)


def lens(D, loc, n, size, m, bevel=.02, inset=.3):
    q = n.to_track_quat('Z', 'Y')
    D.rbox(loc + n * (size[2] * (.5 - inset)), size, m, bevel, 1, q)


def patch(S, PT, fn, nu, nv, off, mat_fn, us=None, vs_=None):
    """conformal patch: fn(u,v) -> (origin, dir); nu x nv cells (optionally non-uniform us / vs_ lists)"""
    bm = PT.bm
    us = us or [i / nu for i in range(nu + 1)]
    vs_ = vs_ or [j / nv for j in range(nv + 1)]
    g = []
    for u in us:
        row = []
        for v in vs_:
            o, d = fn(u, v)
            loc, n = S.ray(o, d)
            if loc is None:
                return None
            row.append((loc, n))
        g.append(row)
    vv = [[bm.verts.new(p + n * off) for p, n in r] for r in g]
    faces = {}
    for i in range(len(us) - 1):
        for j in range(len(vs_) - 1):
            q = [vv[i][j], vv[i + 1][j], vv[i + 1][j + 1], vv[i][j + 1]]
            try:
                f = bm.faces.new(q)
            except ValueError:
                continue
            nrm = sum((g[a][b][1] for a, b in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))), Vector())
            f.normal_update()
            if f.normal.dot(nrm) < 0:
                f.normal_flip()
            f.material_index = PT.mi(mat_fn(i, j) if callable(mat_fn) else mat_fn)
            faces[(i, j)] = f
    return vv, faces


def inset_faces(PT, faces, th, depth, mat=None):
    r = bmesh.ops.inset_region(PT.bm, faces=faces, thickness=th, depth=depth, use_even_offset=True)
    if mat:
        for f in r["faces"]:
            f.material_index = PT.mi(mat)
    return r["faces"]


def lamp_fn(s, front, sd, x0, x1, zlo, zhi):
    """wraps from x0 (u=0) to x1 (u=1) around the corner with horizontal rays shot from inside the body;
    zlo(u), zhi(u) give the bottom/top edge"""
    fc = (s["fmax"] - 1.0) if front else (s["fmin"] + 1.0)
    sgn = 1 if front else -1

    def fn(u, v):
        x = lerp(x0, x1, u)
        th = math.atan2(x, 1.0)
        zz = lerp(zlo(u), zhi(u), v)
        return Vector((0, -fc, zz)), Vector((sd * math.sin(th), -sgn * math.cos(th), 0))
    return fn


def ulist(n, border):
    inner = [border + (1 - 2 * border) * k / n for k in range(n + 1)]
    return [0.0] + inner + [1.0]


def add_lamps(S, s, PT, D):
    """headlight + taillight clusters: bezel, recessed chrome reflector with bulbs, clear cover; taillight with lens + reverse lamp.
    Patches are subdivided so they follow the curvature of the nose / tail corner."""
    hx0, hx1, hz0, hz1 = s["hl"]
    tx0, tx1, tz0, tz1 = s["tl"]
    hzl = lambda u: hz0 + .02 * u
    hzh = lambda u: hz1 - .04 * u
    tzl = lambda u: tz0 + s.get("tl_slope", .0) * u
    tzh = lambda u: tz1 - s.get("tl_slope", .0) * u
    NU = 10
    for sd in (-1, 1):
        fn = lamp_fn(s, True, sd, hx0, hx1, hzl, hzh)
        us = ulist(NU, .03); vs_ = [0, .06, .5, .94, 1]
        r = patch(S, PT, fn, 0, 0, .03, "Rubber", us=us, vs_=vs_)
        if r:
            vv, fcs = r
            inner = [f for (i, j), f in fcs.items() if 0 < i < len(us) - 2 and 0 < j < len(vs_) - 2]
            inset_faces(PT, inner, .012, -.022, "Chrome")
            fn2 = lamp_fn(s, True, sd, lerp(hx0, hx1, .03), lerp(hx0, hx1, .97),
                          lambda u: hzl(lerp(.03, .97, u)) + .007, lambda u: hzh(lerp(.03, .97, u)) - .007)
            patch(S, PT, fn2, NU, 3, .036, "Glass")
            for (u, v, rr) in ((.28, .5, .032), (.72, .5, .024)):
                o, d = fn(u, v)
                loc, n = S.ray(o, d)
                if loc:
                    D.sphere(loc + n * .016, (rr, rr, rr * .8), "Headlight", (10, 6))
                    D.lathe([(.012, 0), (rr * 1.4, .01), (rr * 2.0, .026), (rr * 2.3, .04)], "Chrome", n, loc + n * .008, 14)
            o, d = fn(.5, .85)
            loc, n = S.ray(o, d)
            if loc:
                lens(D, loc + n * .022, n, (abs(hx1 - hx0) * .45, .014, .012), "Headlight", .004, inset=.0)
        fn = lamp_fn(s, False, sd, tx0, tx1, tzl, tzh)
        us = ulist(NU, .03); vs_ = [0, .07, .5, .93, 1]
        r = patch(S, PT, fn, 0, 0, .022, "Rubber", us=us, vs_=vs_)
        if r:
            vv, fcs = r
            inner = [f for (i, j), f in fcs.items() if 0 < i < len(us) - 2 and 0 < j < len(vs_) - 2]
            inset_faces(PT, inner, .004, -.008, None)
            for f in inner:
                f.material_index = PT.mi("Taillight")
            o, d = fn(.16, .5)
            loc, n = S.ray(o, d)
            if loc:
                lens(D, loc + n * .016, n, (abs(tx1 - tx0) * .15, (tz1 - tz0) * .3, .012), "Headlight", .004, inset=.0)
            for (u, v) in ((.6, .5), (.8, .5)):
                o, d = fn(u, v)
                loc, n = S.ray(o, d)
                if loc:
                    D.lathe([(.0, 0), (.02, .004), (.034, .012)], "Chrome", n, loc + n * .014, 10)


def add_plates(S, s, PT, plates):
    for sg, zc in ((1, s["plate_zf"]), (-1, s["plate_zr"])):
        def fn(u, v, sg=sg, zc=zc):
            return V(lerp(-.22, .22, u), sg * 7, lerp(zc - .13, zc + .13, v)), Vector((0, sg, 0))
        r = patch(S, PT, fn, 3, 3, .02, "Rubber", us=[0, .06, .94, 1], vs_=[0, .12, .88, 1])
        if not r:
            continue
        vv, fcs = r
        mid = fcs[(1, 1)]
        res = bmesh.ops.inset_region(PT.bm, faces=[mid], thickness=.006, depth=-.012, use_even_offset=True)
        for f in res["faces"]:
            f.material_index = PT.mi("Rubber")
        mid.material_index = PT.mi("Plate")
        plates.append(mid.calc_center_median())


def add_details(S, s, D, PT, plates):
    hw = s["hw0"]
    # ---- lamps and plates
    add_lamps(S, s, PT, D)
    add_plates(S, s, PT, plates)
    # ---- grille (upper opening between the lamps) and intake
    hz0, hz1 = s["hl"][2], s["hl"][3]
    gx = s["hl"][0] * .98
    zc = s["grille_z"]
    loc, n = S.fb(0, zc, 6)
    if loc:
        gh = s.get("grille_h", .10)
        lens(D, loc, n, (gx * 2, gh, .03), "Rubber", .016, inset=.25)                       # opening with depth
        for k in range(3):
            lens(D, loc + n * .004 + Vector((0, 0, (k - 1) * gh * .3)), n, (gx * 1.9, .011, .03), "Chrome", .004, inset=.05)
        lens(D, loc + n * -.006, n, (gx * 2 + .035, gh + .03, .016), "Chrome", .008, inset=.0)
    loc, n = S.fb(0, s["intake_z"], 6)
    if loc:
        iw = s.get("intake_w", hw * 1.25)
        lens(D, loc, n, (iw, .12, .03), "Rubber", .03, inset=.25)
        for k in range(4):
            lens(D, loc + n * .008 + Vector((0, 0, (k - 1.5) * .027)), n, (iw * .96, .01, .03), "Metal", .003, inset=.05)
    for x in (-.62 * hw, .62 * hw):                       # fog lamps
        loc, n = S.fb(x, s["intake_z"] - .02, 6)
        if loc:
            D.lathe([(.012, 0), (.034, .008), (.044, .02)], "Chrome", n, loc - n * .008, 12)
            D.sphere(loc + n * .004, (.016, .016, .016), "Headlight", (6, 4))
    # shut lines: bumper/hood, bumper/fender
    pts = [S.lift(S.fb(x, s["bumper_line"], 6), .002) for x in np.linspace(-hw * .88, hw * .88, 13)]
    groove(D, pts)
    pts = [S.lift(S.fb(x, s["bumper_line_r"], -6), .002) for x in np.linspace(-hw * .88, hw * .88, 13)]
    groove(D, pts)
    # bumper lower valance + chrome/black strip
    for sg, zz, wd in ((6, s["intake_z"] - .09, 1.3), (-6, s["z0"][0][1] + .1, 1.2)):
        loc, n = S.fb(0, zz, sg)
        if loc:
            lens(D, loc, n, (hw * wd, .07, .02), "Rubber", .02, inset=.25)
    # exhaust
    for sx in s.get("exh", (.55,)):
        D.cyl(V(sx, s["fmin"] + s.get("exh_f", .2), .24), .036, .036, .26, "Chrome", 12, Euler((math.radians(90), 0, 0)))
    # ---- sides
    for sd in (-1, 1):
        seams = s["seams"]
        zlow = .37
        ztop = s["ztf"](0) - .04
        for sf in seams:
            groove(D, [S.lift(S.side(sf, zlow + (ztop - zlow) * k / 9, sd), .002) for k in range(10)])
        groove(D, [S.lift(S.side(seams[0] + (seams[-1] - seams[0]) * k / 10, zlow, sd), .002) for k in range(11)])
        for hf in s["handles"]:
            a, na = S.side(hf, s["ztf"](hf) - .1, sd)
            if a:
                lens(D, a, na, (.15, .04, .035), "Rubber", .012, inset=.2)
                lens(D, a + na * .004, na, (.13, .022, .03), s.get("handle_m", "Chrome"), .008, inset=.2)
        mx, mf, mz = sd * (hw + .035), s["mirror_f"], s["mirror_z"]
        cbx = s["shp"][2] * hw
        D.sphere(V(mx - sd * .01, mf, mz), (.045, .11, .075), "CarPaint", (12, 8), Euler((0, 0, math.radians(sd * 8))))
        D.sphere(V(mx - sd * .01, mf - .098, mz), (.036, .016, .058), "Chrome", (8, 6))
        D.bar(V(sd * (cbx - .03), mf + .12, mz - .075), V(mx - sd * .03, mf + .05, mz - .04), .04, .02, "CarPaint", .006)
        a, na = S.side(1.2, .3, sd); b, nb = S.side(-1.2, .3, sd)
        if a and b:
            D.bar(a + na * .004, b + nb * .004, .04, .012, "Rubber")                  # sill cladding
        for k in range(3):
            pass
        wins = s["side_win"]
        fa, fb_ = max(w[0] for w in wins), min(w[1] for w in wins)
        pts = [S.lift(S.side(fa + (fb_ - fa) * k / 14, s["ztf"](0) + .015, sd), .004) for k in range(15)]
        pts = [q for q in pts if q is not None]
        if len(pts) > 2 and s.get("belt_chrome", True):
            D.tube(pts, [(.005, .011)] * len(pts), "Chrome", 4, cap=False)
        for wf in s["wheels"]:
            pts = []
            for k in range(17):
                a = math.radians(4 + k * (172 / 16))
                zz = s["wr"] + .012 + (s["wr"] + .064) * math.sin(a)
                ff = wf + (s["wr"] + .064) * math.cos(a)
                pts.append(S.lift(S.side(ff, zz, sd), .003))
            pts = [q for q in pts if q is not None]
            if len(pts) > 4:
                D.tube(pts, [(.009, s.get("arch_lip", .012))] * len(pts), "Rubber", 4, cap=False)
        for fz, mm in ((s["fmax"] - .28, "Headlight"), (s["fmin"] + .33, "Taillight")):
            a, na = S.side(fz, s["hl"][2] + .05 if mm == "Headlight" else s["tl"][2] + .1, sd)
            if a:
                lens(D, a, na, (.1, .03, .018), mm, .006)
    # ---- hood / deck lines, wipers
    if s.get("bonnet"):
        b0, b1 = s["bonnet"]
        wx = hw * .74
        line = [(-wx, b0)] + [(-wx, b0 + (b1 - b0) * k / 6) for k in range(1, 7)] + [(-wx + 2 * wx * k / 10, b1) for k in range(1, 10)] + \
               [(wx, b1 - (b1 - b0) * k / 6) for k in range(0, 7)] + [(wx, b0)]
        groove(D, [S.lift(S.top(x, f), .002) for x, f in line])
    if s.get("trunk"):
        t0, t1 = s["trunk"]
        wx = hw * .72
        line = [(-wx, t0)] + [(-wx, t0 + (t1 - t0) * k / 5) for k in range(1, 6)] + [(-wx + 2 * wx * k / 10, t1) for k in range(1, 10)] + \
               [(wx, t1 - (t1 - t0) * k / 5) for k in range(0, 6)] + [(wx, t0)]
        groove(D, [S.lift(S.top(x, f), .002) for x, f in line])
    elif s.get("hatch"):
        zb = s.get("hatch_z", .45)
        groove(D, [S.lift(S.fb(-hw * .86 + hw * 1.72 * k / 14, zb, -6), .002) for k in range(15)])
    fb0 = s["fw0"] - .05
    for wx0, wx1 in ((-.64, -.08), (-.02, .52)):
        a = S.top(wx0, fb0); b = S.top(wx1, fb0 - .06)
        if a[0] and b[0]:
            D.bar(a[0] + a[1] * .012, b[0] + b[1] * .012, .018, .008, "Rubber")
            D.bar(a[0] + a[1] * .012, a[0] + a[1] * .012 + (b[0] - a[0]).normalized() * .05, .006, .006, "Rubber")
    # cowl strip
    pts = [S.lift(S.top(x, s["fw0"] + .035), .004) for x in np.linspace(-hw * .8, hw * .8, 12)]
    pts = [q for q in pts if q is not None]
    if len(pts) > 2:
        D.tube(pts, [(.018, .006)] * len(pts), "Rubber", 4, cap=False)


# ====================================================================================== interior
def add_interior(s, I):
    zb = s["ztf"](0)
    fw0 = s["fw0"]
    cw = s["shp"][2] * s["hw0"]
    fd = s.get("dash_f", fw0 - .3)
    I.rbox(V(0, fd, zb - .22), (cw * 1.9, .5, .24), "Interior", .06, 2)                       # dash body
    I.rbox(V(0, fd - .06, zb - .06), (cw * 1.8, .36, .09), "Interior", .04, 2)                  # dash top pad
    I.rbox(V(-.36, fd - .14, zb - .015), (.44, .22, .1), "Interior", .04, 2)                    # instrument hood
    I.rbox(V(0, fd - .22, zb - .38), (.26, .62, .36), "Interior", .05, 2)                       # centre stack / console
    wc = V(-.36, fd - .30, zb - .15)
    axis = V(0, -1, .46)
    ring = ring_circle(wc, axis, .19, 20)
    I.tube(ring + [ring[0]], [.016] * 21, "Interior", 6, cap=False)
    I.cyl(wc, .05, .05, .05, "Interior", 10, axis.to_track_quat('Z', 'Y'))
    I.bar(wc - Vector((.17, 0, 0)), wc + Vector((.17, 0, 0)), .03, .02, "Interior")
    I.bar(wc, wc + Vector((0, .0, -.16)), .03, .02, "Interior")
    I.cyl(wc + Vector((0, .16, -.1)), .035, .035, .3, "Interior", 8, axis.to_track_quat('Z', 'Y'))
    zs = s.get("seat_z", .42)
    for si, sf in enumerate(s["seats"]):
        for x in (-.36, .36):
            if si == 0 or s.get("bench_split", True) or True:
                I.rbox(V(x, sf, zs + .05), (.5, .54, .14), "Interior", .05, 1)                        # cushion
                I.rbox(V(x, sf - .12, zs - .08), (.46, .4, .2), "Interior", .04, 1)
                back = V(x, sf - .27, zs + .35)
                I.rbox(back, (.5, .13, .62), "Interior", .05, 2, Euler((math.radians(-13), 0, 0)))    # back
                for sx in (-1, 1):
                    I.rbox(back + Vector((sx * .2, .05, -.04)), (.07, .12, .5), "Interior", .03, 1, Euler((math.radians(-13), 0, 0)))
                hp = V(x, sf - .32, zs + .79)
                I.rbox(hp, (.24, .1, .17), "Interior", .045, 1, Euler((math.radians(-9), 0, 0)))      # headrest
    # door cards + floor
    f0, f1 = fw0 - .25, s["fb0"] + .15
    for sd in (-1, 1):
        I.rbox(V(sd * (cw - .05), (f0 + f1) / 2, zb - .27), (.07, f0 - f1, .5), "Interior", .02, 1)
        I.rbox(V(sd * (cw - .09), (f0 + f1) / 2, zb - .5), (.1, f0 - f1, .06), "Interior", .02, 1)
    I.rbox(V(0, (f0 + f1) / 2, .3), (cw * 1.9, f0 - f1, .04), "Interior", .01, 1)
    I.rbox(V(0, s.get("dash_f", fw0 - .3) - .95, .45), (.2, .6, .22), "Interior", .05, 2)         # console between seats


# ====================================================================================== car builder
def build_car(s, idx):
    make_funcs(s)
    body = make_body(s)
    # ---- arches: boolean-cut from the subdivided body
    cutters = []
    for sd in (-1, 1):
        for wf in s["wheels"]:
            cp = Part("cut")
            x0, x1 = s["track"] - .17, s["hw0"] + .15
            r = s["wr"] + .042
            cp.cyl(V(sd * (x0 + x1) / 2, wf, s["wr"] + .012), r, r, x1 - x0, "Rubber", 28, Euler((0, math.radians(90), 0)))
            cutters.append(cp.build(smooth=0, name="cutter"))
    # open the top of the body under the greenhouse (and the bed / cargo area)
    op = s.get("open", (s["fb0"] + .03, s["fw0"] - .03))
    hwf = s["hwf"]; shp = s["shp"]

    def kill_top(bm):
        kill = []
        for f in bm.faces:
            c = f.calc_center_median()
            fv = -c.y
            if f.normal.z > .3 and op[0] < fv < op[1] and abs(c.x) < (shp[2] - .03) * hwf(fv) and c.z > .65:
                kill.append(f)
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    edit_mesh(body, kill_top)
    boolean_diff(body, cutters)
    cabin = make_cabin(s)
    # ---- windows: gasket + separate glass shell
    cabin_bm = bmesh.new(); cabin_bm.from_mesh(cabin.data)
    mats = [m.name for m in cabin.data.materials]
    gi, ci = mats.index("Glass"), mats.index("CarPaint")
    ri = len(mats)
    cabin.data.materials.append(mat("Rubber"))
    glass = [f for f in cabin_bm.faces if f.material_index == gi]
    res = bmesh.ops.inset_region(cabin_bm, faces=glass, thickness=.016, depth=-.012, use_even_offset=True)
    for f in res["faces"]:
        f.material_index = ri
    dup = bmesh.ops.duplicate(cabin_bm, geom=glass)
    new_faces = [g for g in dup["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.delete(cabin_bm, geom=glass, context='FACES')
    cabin_bm.to_mesh(cabin.data); cabin_bm.free()
    cabin.data.update()
    print("SHELL", tri_count(body), tri_count(cabin))
    shell = join([body, cabin], "shell")
    S = Surf(shell)
    liner = make_liner(s)

    D = P2("details")
    PT = P2("patches")
    plates = []
    add_details(S, s, D, PT, plates)
    # ---- bed / cargo / sign extras
    if s.get("extras"):
        s["extras"](s, D, S)
    _tc = lambda bm: sum(len(f.verts) - 2 for f in bm.faces)
    print("D pre-wheel", _tc(D.bm))
    # ---- wheels + wells
    for sd in (-1, 1):
        for wf in s["wheels"]:
            wheel(D, s, sd * s["track"], wf, sd)
            well_shell(D, s, sd, wf)
    print("D post-wheel", _tc(D.bm))
    I = P2("interior")
    add_interior(s, I)
    det = D.build2(smooth=40, name="details")
    pbm = PT.bm
    pme = bpy.data.meshes.new("patches"); pbm.to_mesh(pme); pbm.free()
    for sl in PT.slots:
        pme.materials.append(mat(sl))
    pob = bpy.data.objects.new("patches", pme); bpy.context.scene.collection.objects.link(pob)
    select_only(pob)
    for p in pme.polygons:
        p.use_smooth = True
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    interior = I.build2(smooth=40, name="interior_parts")
    # glass shell object (copy of the glass faces)
    print("PARTS", s["name"], {o.name: tri_count(o) for o in (det, pob, liner, interior)})
    car = join([shell, det, pob, liner, interior], s["name"])
    # ---- UVs: world-meter box projection, plate cells, UVDirt
    wfr, wre = s["wheels"]
    pcell = s.get("plate_cell")

    def umap(fv):
        if fv <= wre:
            u = .2 * (fv - s["fmin"]) / (wre - s["fmin"])
        elif fv <= wfr:
            u = .2 + .6 * (fv - wre) / (wfr - wre)
        else:
            u = .8 + .2 * (fv - wfr) / (s["fmax"] - wfr)
        return clamp(u)
    bm = bmesh.new(); bm.from_mesh(car.data)
    project_uvs(bm)
    uv = bm.loops.layers.uv.verify()
    plate_i = [i for i, m in enumerate(car.data.materials) if m and m.name.startswith("Plate")]
    for f in bm.faces:
        if f.material_index in plate_i and abs(f.normal.y) > .5:
            sgn = 1 if f.normal.y < 0 else -1
            cell = pcell if pcell is not None else (idx * 2 + (0 if sgn > 0 else 1)) % 16
            cu, cv = cell % 4, 3 - cell // 4
            xs = [l.vert.co.x for l in f.loops]; zs = [l.vert.co.z for l in f.loops]
            x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
            for l in f.loops:
                p3 = l.vert.co
                ul = (p3.x - x0) / max(x1 - x0, 1e-4)
                if sgn < 0:
                    ul = 1 - ul
                vl = (p3.z - z0) / max(z1 - z0, 1e-4)
                l[uv].uv = ((cu + .02 + ul * .96) / 4, (cv + .02 + vl * .96) / 4)
    layer = bm.loops.layers.uv.new("UVDirt")
    for f in bm.faces:
        endface = abs(f.normal.y) > .6
        for l in f.loops:
            p3 = l.vert.co
            u = (.5 + .04 * p3.x) if endface else umap(-p3.y)
            l[layer].uv = (u, min(max(p3.z / 2.0, 0), 1))
    bm.to_mesh(car.data); bm.free()
    car.data.update()
    # separate glass shell must stay separate: it is already disjoint geometry inside the same mesh island set
    return car


# ====================================================================================== extras per body type
def extras_cab(s, D, S):
    rz = max(z for _, z in s["roofpts"])
    f0 = s.get("sign_f", -.22)
    D.rbox(V(0, f0, rz + .085), (.64, .24, .15), "Paint", .04, 2)
    D.rbox(V(0, f0, rz + .165), (.52, .17, .035), "LampGlass", .014, 1)
    for sx in (-.2, .2):
        D.rbox(V(sx, f0, rz + .015), (.05, .2, .035), "Metal", .008, 1)
    for sd in (-1, 1):
        D.rbox(V(sd * .325, f0, rz + .085), (.02, .2, .12), "LampGlass", .006, 1)
    D.rbox(V(0, f0 + .125, rz + .08), (.4, .008, .09), "LampGlass", .004, 1)
    D.rbox(V(0, f0 - .125, rz + .08), (.4, .008, .09), "LampGlass", .004, 1)


def extras_pickup(s, D, S):
    hw = s["hw0"]
    b0, b1 = -.70, s["fmin"] + .1
    zf = .86
    L = b0 - b1
    c = (b0 + b1) / 2
    D.rbox(V(0, c, zf - .02), ((hw * .875 - .03) * 2, L, .04), "Rubber", .01, 1)
    for sd in (-1, 1):
        D.rbox(V(sd * (hw * .875 - .03), c, (zf + 1.04) / 2), (.04, L, 1.04 - zf), "CarPaint", .01, 1)
        D.rbox(V(sd * (hw * .875 - .2), s["wheels"][1], zf + .09), (.3, .82, .2), "CarPaint", .08, 2)
        D.bar(V(sd * (hw * .93), b0, 1.075), V(sd * (hw * .93), b1, 1.075), .06, .02, "Chrome", .006, 1)
    D.rbox(V(0, b0 + .02, (zf + 1.1) / 2), (hw * 1.75, .05, 1.1 - zf), "CarPaint", .012, 1)
    D.rbox(V(0, b1 + .04, (zf + 1.04) / 2), (hw * 1.75, .04, 1.04 - zf), "CarPaint", .01, 1)
    D.rbox(V(0, s["fmin"] + .05, .5), (hw * 1.9, .12, .12), "Chrome", .03, 2)
    D.rbox(V(0, s["fmax"] - .05, .40), (hw * 1.7, .12, .1), "Chrome", .03, 2)
    for sx in (-.55, .55):
        D.cyl(V(sx, s["fmin"] + .05, .28), .04, .04, .2, "Chrome", 10, Euler((math.radians(90), 0, 0)))


def extras_van(s, D, S):
    hw = s["hw0"]
    f0, f1 = 1.0, s["fmin"] + .12
    cw = .80
    D.rbox(V(0, (f0 + f1) / 2, .62), (cw * 2, f0 - f1, .05), "Interior", .01, 1)
    for sd in (-1, 1):
        D.rbox(V(sd * cw, (f0 + f1) / 2, 1.2), (.04, f0 - f1, 1.1), "Interior", .01, 1)
    D.rbox(V(0, f0, 1.2), (cw * 2, .04, 1.1), "Interior", .01, 1)
    loc, n = S.fb(0, 1.0, -6)
    if loc:
        groove(D, [S.lift(S.fb(0, .5 + k * .2, -6), .002) for k in range(9)])
    for sx in (-.43, .43):
        loc, n = S.fb(sx, 1.55, -6)
        if loc:
            lens(D, loc, n, (.64, .5, .03), "Glass", .03, inset=.3)
            lens(D, loc, n, (.7, .56, .016), "Rubber", .03, inset=.0)
    for sx in (-.07, .07):
        loc, n = S.fb(sx, 1.0, -6)
        if loc:
            lens(D, loc, n, (.04, .14, .03), "Chrome", .01, inset=.2)
    D.rbox(V(0, s["fmin"] + .04, .5), (hw * 1.9, .12, .12), "Rubber", .03, 2)
    for sd in (-1, 1):
        D.rbox(V(sd * .6, s["fmin"] + .06, .38), (.5, .1, .06), "Metal", .02, 1)


# ====================================================================================== specs
SEDAN = dict(
    name="CarV3_Sedan", color=(.30, .36, .45),
    fmin=-2.42, fmax=2.32, hw0=.905, rcf=.44, rcr=.40,
    z0=[(-2.42, .40), (-2.30, .31), (-2.15, .25), (-1.85, .20), (-1.2, .18), (1.2, .18), (1.85, .20), (2.05, .23), (2.20, .28), (2.32, .36)],
    zt=[(-2.42, .88), (-2.36, .95), (-2.15, .99), (-1.7, 1.0), (0, .99), (.95, .975), (1.3, .94), (1.8, .885), (2.15, .835), (2.27, .805), (2.32, .75)],
    roof=[(.95, None), (.12, 1.455), (-1.0, 1.45), (-1.70, None)],
    ws=(.95, .12), bl=(-1.0, -1.70), side_win=[(.62, -.10), (-.24, -1.0)], tumb=.17,
    wheels=(1.36, -1.36), wr=.33, tw=.215, track=.76, rim="alloy5",
    hl=(.30, .86, .60, .80), tl=(.30, .86, .74, .93), plate_zf=.50, plate_zr=.66,
    grille_z=.64, grille_h=.09, intake_z=.47, bumper_line=.60, bumper_line_r=.76,
    seams=[.62, -.17, -.97], handles=[-.02, -.82], mirror_f=.62, mirror_z=1.08, seats=(-.04, -.95),
    bonnet=(.90, 2.15), trunk=(-1.72, -2.25), exh=(.55,),
)
CAB = dict(SEDAN, name="CarV3_Cab", color=(.86, .66, .10), rim="steel", plate_cell=9, extras=extras_cab, handle_m="Chrome")
COMPACT = dict(
    name="CarV3_Compact", color=(.50, .12, .12),
    fmin=-2.25, fmax=2.20, hw0=.87, rcf=.52, rcr=.42,
    z0=[(-2.25, .42), (-2.13, .31), (-2.0, .25), (-1.75, .2), (-1.1, .18), (1.1, .18), (1.75, .2), (1.95, .24), (2.08, .30), (2.2, .40)],
    zt=[(-2.25, .84), (-2.19, .92), (-2.0, .96), (-1.55, .97), (0, .96), (.9, .945), (1.2, .91), (1.7, .85), (2.0, .79), (2.12, .755), (2.2, .69)],
    roof=[(.90, None), (.12, 1.43), (-.9, 1.425), (-1.55, None)],
    ws=(.90, .12), bl=(-.9, -1.55), side_win=[(.58, -.10), (-.24, -.9)], tumb=.16,
    wheels=(1.30, -1.30), wr=.31, tw=.195, track=.715, rim="steel",
    hl=(.38, .78, .60, .74), tl=(.36, .78, .74, .90), plate_zf=.50, plate_zr=.68,
    grille_z=.57, grille_h=.085, intake_z=.40, bumper_line=.60, bumper_line_r=.78,
    seams=[.58, -.17, -.84], handles=[-.02, -.72], mirror_f=.58, mirror_z=1.05, seats=(-.05, -.85),
    bonnet=(.85, 2.05), trunk=(-1.58, -2.12), exh=(.5,), handle_m="CarPaint",
)
HATCH = dict(
    name="CarV3_Hatch", color=(.62, .64, .66),
    fmin=-1.95, fmax=2.05, hw0=.85, rcf=.50, rcr=.38,
    z0=[(-1.95, .40), (-1.83, .30), (-1.7, .24), (-1.45, .2), (-1.0, .18), (1.0, .18), (1.5, .2), (1.75, .24), (1.9, .30), (2.05, .40)],
    zt=[(-1.95, .88), (-1.9, .97), (-1.7, 1.0), (0, .97), (.8, .95), (1.1, .92), (1.6, .85), (1.9, .79), (2.0, .755), (2.05, .69)],
    roof=[(.80, None), (.10, 1.48), (-1.28, 1.46), (-1.82, None)],
    ws=(.80, .10), bl=(-1.28, -1.82), side_win=[(.52, -.10), (-.24, -1.15)], tumb=.16,
    wheels=(1.225, -1.225), wr=.30, tw=.185, track=.70, rim="steel",
    hl=(.36, .76, .60, .74), tl=(.40, .78, .70, .96), plate_zf=.50, plate_zr=.58,
    grille_z=.57, grille_h=.08, intake_z=.40, bumper_line=.60, bumper_line_r=.50,
    seams=[.52, -.15, -.98], handles=[-.03, -.80], mirror_f=.52, mirror_z=1.06, seats=(-.05, -.78),
    bonnet=(.75, 1.9), hatch=True, hatch_z=.48, exh=(.45,), handle_m="CarPaint", cab_extra=[-1.15],
)
MINIVAN = dict(
    name="CarV3_Minivan", color=(.66, .68, .70),
    fmin=-2.55, fmax=2.40, hw0=.93, rcf=.62, rcr=.42, sh=(.985, .95, .905),
    z0=[(-2.55, .42), (-2.45, .32), (-2.3, .26), (-2.0, .21), (-1.5, .2), (1.5, .2), (1.95, .22), (2.15, .28), (2.28, .34), (2.40, .42)],
    zt=[(-2.55, 1.0), (-2.5, 1.1), (-2.3, 1.12), (-1.5, 1.11), (0, 1.06), (1.0, 1.02), (1.3, .99), (1.7, .94), (2.1, .87), (2.28, .8), (2.40, .72)],
    roof=[(1.28, None), (.60, 1.70), (-.5, 1.74), (-1.6, 1.70), (-2.42, None)],
    ws=(1.28, .60), bl=(-1.6, -2.42), side_win=[(.85, .05), (-.10, -.95), (-1.05, -1.55)], tumb=.2,
    cab_extra=[.85, .05, -.10, -.95, -1.05],
    wheels=(1.46, -1.46), wr=.345, track=.765, tw=.215, rim="alloy6",
    hl=(.46, .88, .66, .82), tl=(.46, .88, .80, 1.0), plate_zf=.50, plate_zr=.62,
    grille_z=.60, grille_h=.09, intake_z=.40, bumper_line=.66, bumper_line_r=.55,
    seams=[.88, .0, -1.15], handles=[.12, -.14], mirror_f=.95, mirror_z=1.14, seats=(.3, -.5),
    bonnet=(1.15, 2.15), hatch=True, hatch_z=.5, exh=(.6,), handle_m="CarPaint", dash_f=1.0,
)
SUV = dict(
    name="CarV3_SUV", color=(.12, .14, .16),
    fmin=-2.37, fmax=2.34, hw0=.925, rcf=.52, rcr=.40, sh=(.985, .955, .90),
    z0=[(-2.37, .5), (-2.28, .38), (-2.15, .3), (-1.9, .24), (-1.3, .22), (1.3, .22), (1.9, .24), (2.1, .28), (2.25, .36), (2.34, .46)],
    zt=[(-2.37, 1.0), (-2.30, 1.08), (-2.1, 1.13), (-1.5, 1.13), (0, 1.12), (1.0, 1.1), (1.4, 1.03), (1.9, .97), (2.2, .9), (2.30, .85), (2.34, .78)],
    roof=[(1.12, None), (.34, 1.73), (-1.7, 1.74), (-2.27, None)],
    ws=(1.12, .34), bl=(-1.7, -2.27), side_win=[(.78, -.12), (-.25, -1.0), (-1.12, -1.55)], tumb=.17,
    cab_extra=[.78, -.12, -.25, -1.0, -1.12, -1.55],
    wheels=(1.39, -1.39), wr=.36, tw=.235, track=.78, rim="alloy6",
    hl=(.46, .88, .72, .88), tl=(.46, .88, .86, 1.04), plate_zf=.56, plate_zr=.72,
    grille_z=.68, grille_h=.1, intake_z=.46, bumper_line=.75, bumper_line_r=.6,
    seams=[.72, -.18, -1.2], handles=[-.06, -1.0], mirror_f=.72, mirror_z=1.2, seats=(.12, -.8),
    bonnet=(.95, 2.1), hatch=True, hatch_z=.55, exh=(.6,), handle_m="CarPaint", arch_lip=.02, dash_f=.75,
)
PICKUP = dict(
    name="CarV3_Pickup", color=(.55, .48, .36),
    fmin=-2.75, fmax=2.55, hw0=.965, rcf=.5, rcr=.3, sh=(.985, .955, .90),
    z0=[(-2.75, .55), (-2.65, .42), (-2.45, .33), (-2.2, .28), (-1.5, .26), (1.5, .26), (2.0, .28), (2.3, .33), (2.45, .40), (2.55, .5)],
    zt=[(-2.75, 1.0), (-2.7, 1.06), (-2.4, 1.07), (-.6, 1.07), (0, 1.07), (1.0, 1.06), (1.5, 1.0), (2.0, .95), (2.4, .9), (2.5, .86), (2.55, .8)],
    roof=[(1.0, None), (.40, 1.73), (-.35, 1.74), (-.60, None)],
    ws=(1.0, .40), bl=(-.35, -.455), bl_k=(5,), side_win=[(.75, .0), (-.12, -.455)], tumb=.17,
    cab_extra=[.75, .0, -.12],
    wheels=(1.6, -1.6), wr=.385, tw=.245, track=.80, rim="steel",
    hl=(.40, .92, .74, .92), tl=(.60, .96, .80, 1.0), plate_zf=.56, plate_zr=.62,
    grille_z=.7, grille_h=.11, intake_z=.48, bumper_line=.76, bumper_line_r=.56,
    seams=[.78, -.07, -.58], handles=[.05, -.43], mirror_f=.78, mirror_z=1.22, seats=(.1,),
    bonnet=(.95, 2.4), exh=(.6,), handle_m="Chrome", arch_lip=.03, extras=extras_pickup, open=(-2.62, .97), dash_f=.72, exh_f=.55,
)
VAN = dict(
    name="CarV3_Van", color=(.78, .78, .76),
    fmin=-2.55, fmax=2.45, hw0=.96, rcf=.6, rcr=.25, sh=(.995, .985, .975),
    z0=[(-2.55, .5), (-2.5, .38), (-2.4, .3), (-2.2, .26), (-1.5, .25), (1.5, .25), (2.0, .27), (2.3, .32), (2.4, .38), (2.45, .45)],
    zt=[(-2.55, 1.1), (-2.5, 1.15), (-2.3, 1.15), (0, 1.15), (1.3, 1.1), (1.7, 1.05), (2.0, 1.0), (2.3, .92), (2.40, .86), (2.45, .80)],
    roof=[(1.78, None), (1.20, 2.0), (-2.40, 2.0), (-2.50, None)],
    ws=(1.78, 1.20), bl=None, side_win=[(1.12, .5)], tumb=.05,
    cab_extra=[1.12, .5],
    wheels=(1.5, -1.5), wr=.345, tw=.205, track=.81, rim="steel",
    hl=(.50, .92, .70, .86), tl=(.80, .96, .70, 1.2), plate_zf=.52, plate_zr=.62,
    grille_z=.72, grille_h=.12, intake_z=.45, bumper_line=.82, bumper_line_r=.5,
    seams=[1.18, .32, -.9], handles=[.45, .12], mirror_f=1.2, mirror_z=1.48, seats=(.8,),
    bonnet=(1.7, 2.3), exh=(.6,), handle_m="Chrome", arch_lip=.025, extras=extras_van, open=(-2.45, 1.75), dash_f=1.48,
    seat_z=.55, belt_chrome=False,
)
ALL = [SEDAN, COMPACT, HATCH, MINIVAN, SUV, PICKUP, VAN, CAB]


# ====================================================================================== preview styling / rendering
_orig_style = style_mat


def style_mat3(slot, color=None, uvmap=None, cell=None):
    m = _orig_style(slot, color, uvmap, cell)
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        if slot == "Interior":
            b.inputs["Base Color"].default_value = (.20, .185, .16, 1)
            b.inputs["Roughness"].default_value = .75
        elif slot == "Glass":
            b.inputs["Base Color"].default_value = (.04, .055, .06, 1)
            b.inputs["Alpha"].default_value = .3
            b.inputs["Roughness"].default_value = .02
        elif slot == "Rubber":
            b.inputs["Base Color"].default_value = (.03, .03, .033, 1)
        elif slot == "Taillight":
            b.inputs["Base Color"].default_value = (.22, .0, .0, 1)
            b.inputs["Roughness"].default_value = .5
            b.inputs["Emission Color"].default_value = (.9, .02, .0, 1)
            b.inputs["Emission Strength"].default_value = .45
            try:
                b.inputs["Specular IOR Level"].default_value = .1
            except Exception:
                pass
        elif slot == "Metal":
            b.inputs["Base Color"].default_value = (.32, .33, .35, 1)
    return m


import bl_street2
bl_street2.style_mat = style_mat3


def render_car(car, s, short):
    kw = dict(size=(1100, 700), car_color=s["color"], ground="asphalt", fit=.66)
    render_item([car], os.path.join(PREVIEWS, "V3_Car_%s_front34.png" % short), az=35, el=10, **kw)
    render_item([car], os.path.join(PREVIEWS, "V3_Car_%s_rear34.png" % short), az=145, el=10, **kw)
    render_item([car], os.path.join(PREVIEWS, "V3_Car_%s_side.png" % short), az=90, el=4, lens=70, **kw)


LINEUP_ORDER = ["Sedan", "Minivan", "Cab", "SUV", "Hatch", "Pickup", "Compact", "Van"]
LINEUP_COLOR = {"Sedan": (.16, .22, .34), "Minivan": (.60, .58, .50), "Cab": (.86, .66, .10), "SUV": (.07, .08, .10),
                "Hatch": (.45, .58, .70), "Pickup": (.45, .12, .10), "Compact": (.60, .62, .64), "Van": (.78, .78, .76)}


def box_obj(name, size, loc, color, rough=.8):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= size[0]; v.co.y *= size[1]; v.co.z *= size[2]
    bm.to_mesh(me); bm.free()
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = rough
    me.materials.append(m)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    return ob


def lineup(cars, outdir):
    scn = bpy.context.scene
    byname = {s["name"].split("_")[1]: (s, c) for s, c in cars}
    order = [n for n in LINEUP_ORDER if n in byname]
    cursor, xs = -2.0, {}
    gaps = [.7, .45, .85, .5, .6, .4, .7, .5]
    for i, n in enumerate(order):
        s, car = byname[n]
        car.hide_render = False
        for m in car.data.materials:
            pass
        style_object(car, LINEUP_COLOR.get(n, s["color"]))
        car.rotation_euler = (0, 0, math.radians(90))
        car.location = (cursor - s["fmin"], 0, 0)
        xs[n] = (cursor, cursor + s["fmax"] - s["fmin"])
        cursor += s["fmax"] - s["fmin"] + gaps[i % len(gaps)]
    keep = set(c for _, c in cars)
    total = cursor
    # street
    ground = textured_ground(400, "asphalt", 0.0, (total / 2, 0), tile=4.0)
    side = textured_ground(400, "concrete", .15, (total / 2, 1.2 + 200), tile=1.5)
    curb = box_obj("curb", (400, .16, .15), (total / 2, 1.2 - .08, .075), (.55, .54, .5), .9)
    wall = textured_ground(200, "brick", 0.0, (total / 2, 0), tile=3.0)
    wall.rotation_euler = (math.radians(90), 0, 0); wall.location = (0, 9.0, 100.0)
    keep |= {ground, side, curb, wall}
    for o in scn.objects:
        if o.type in ('MESH',):
            o.hide_render = o not in keep
    bpy.context.view_layer.update()
    sizes = (1700, 720)
    setup_render(sizes, exposure=-.3, sun_az=-50)
    scn.eevee.taa_render_samples = 64

    def shot(path, cam_loc, target, lens):
        look_cam(cam_loc, target, lens)
        scn.render.filepath = path
        bpy.ops.render.render(write_still=True)
        for o in list(scn.objects):
            if o.name.startswith("cam"):
                bpy.data.objects.remove(o, do_unlink=True)
        print("PREVIEW", path)
    # along the curb, from the sidewalk, like real_12
    shot(os.path.join(outdir, "V3_Lineup_curb.png"), (-7.5, 2.9, 1.55), (12.0, -0.3, 1.0), 26)
    # across the street from the opposite sidewalk (two halves)
    mid1 = (xs[order[0]][0] + xs[order[3]][1]) / 2
    mid2 = (xs[order[4]][0] + xs[order[-1]][1]) / 2
    shot(os.path.join(outdir, "V3_Lineup_across_a.png"), (mid1 - 1, -13.5, 1.55), (mid1, 0, 1.0), 32)
    shot(os.path.join(outdir, "V3_Lineup_across_b.png"), (mid2 - 1, -13.5, 1.55), (mid2, 0, 1.0), 32)
    shot(os.path.join(outdir, "V3_Lineup_curb_close.png"), (xs[order[0]][0] - 4.0, 3.2, 1.5), (xs[order[1]][0] + 3, 0.2, 1.0), 38)


def main():
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = a.split("=")[1].split(",")
    reset_scene()
    cars = []
    for i, s in enumerate(ALL):
        short = s["name"].split("_")[1]
        if only and short not in only:
            continue
        car = build_car(dict(s), i)
        u, umin, umax = report(car, None)
        s["_bounds"] = (u, umin, umax, tri_count(car))
        export_fbx([car], V3_VEH, s["name"])
        if "--noprev" not in ARGS:
            render_car(car, s, short)
        cars.append((s, car))
    if "--nolineup" not in ARGS and len(cars) > 1:
        lineup(cars, PREVIEWS)
    print("DONE", [(s["name"], s["_bounds"][3]) for s, _ in cars])


if "--noauto" not in sys.argv:
    main()
