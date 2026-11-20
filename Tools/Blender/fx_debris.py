"""Explosion debris meshes (helper for make_explosion_fx.py, runs inside Blender).
Parts are authored in Blender coords (z up, forward = -Y via P(x, f, z)); the mesh is re-centred on its centre of mass and
exported one FBX each with the project's standard export settings into staging/explosions/debris/.

Panels (hood, door, bumper, trunk lid, scrap, barrel shells) share one generator:
  surface grid (u, v) -> deformation (crown, curls, folds, twist, crumple) -> ragged torn outline (cells removed by a noise
  threshold, boundary jittered) -> manual solidify (real thickness, side walls) -> slots by face (Paint outside, CarMetal inside /
  torn edges, Charred patches from noise).
"""
import bpy, bmesh, math, os, sys, json, random
from mathutils import Vector, Matrix, Euler, noise
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bl_common as bc

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "staging", "explosions")
bc.COLORS.update({"Paint": (.62, .10, .08), "CarMetal": (.50, .50, .52), "Charred": (.035, .03, .027), "Rubber": (.035, .035, .04),
                  "Rim": (.62, .63, .66), "BarrelPaint": (.10, .26, .44)})
MAXTRIS = 1500
PREVIEW = True


def P(x, f, z):
    return Vector((x, -f, z))


def sm(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def N(p, seed=0, f=1.0):
    q = Vector(p) * f + Vector((seed * 17.31 + 3.7, seed * 5.13 + 1.3, seed * 9.77 + 8.1))
    return noise.noise(q)


def FBM(p, seed=0, f=1.0, o=3):
    a, s, t = 1.0, 0.0, 0.0
    for i in range(o):
        s += a * N(p, seed + i * 3, f * (2 ** i)); t += a; a *= .5
    return s / t


def curl(d, k, thmax):
    """distance d past the edge line -> (along, up) for a rolled / folded edge of curvature k, total angle thmax"""
    if d <= 0:
        return d, 0.0
    s1 = thmax / k
    if d <= s1:
        return math.sin(k * d) / k, (1 - math.cos(k * d)) / k
    e = d - s1
    return math.sin(thmax) / k + e * math.cos(thmax), (1 - math.cos(thmax)) / k + e * math.sin(thmax)


def curl_pt(p, q, n, k, thmax):
    """Curl panel-space point p about the edge line through q with in-plane unit normal n (curl side = +n)."""
    n = Vector(n); n.z = 0; n.normalize()
    d = (p - q).dot(n)
    if d <= 0:
        return p
    a, up = curl(d, k, thmax)
    return p + n * (a - d) + Vector((0, 0, up))


def twist(p, axis_dir, ang_per_m, center=Vector((0, 0, 0))):
    a = axis_dir.normalized()
    t = (p - center).dot(a)
    return center + Matrix.Rotation(ang_per_m * t, 3, a) @ (p - center)


# ---------------------------------------------------------------------------------------------- shell generator
def com_of(bm):
    vol, c = 0.0, Vector()
    area, ca = 0.0, Vector()
    for f in bm.faces:
        vs = [l.vert.co for l in f.loops]
        for i in range(1, len(vs) - 1):
            a, b, d = vs[0], vs[i], vs[i + 1]
            v = a.dot(b.cross(d)) / 6.0
            vol += v; c += v * (a + b + d) / 4
            ar = (b - a).cross(d - a).length / 2
            area += ar; ca += ar * (a + b + d) / 3
    if abs(vol) > 1e-7:
        return c / vol, abs(vol)
    return ca / max(area, 1e-9), 0.0


def shell(P_, nu, nv, pos_fn, keep_fn, t, mat_fn, wrap=False, out_fn=None, tear_jit=0.0, seed=0, field_fn=None, chip=0.0):
    """Grid (nu x nv cells) over u,v in 0..1 -> deformed surface -> solidified shell added to Part P_.
    pos_fn(u, v) -> Vector; keep_fn(u, v) -> bool for the cell; mat_fn(kind, p, u, v) -> slot name ('top' | 'bot' | 'side').
    out_fn(p) -> expected outward direction (to orient normals)."""
    bm = P_.bm
    if field_fn is not None:
        keep_fn = lambda u, v: field_fn(u, v) > 0
    pos = {}

    def pv(i, j):
        j = j % nv if wrap else j
        k = (i, j)
        if k not in pos:
            u, v = i / nu, (j / nv)
            pos[k] = pos_fn(u, v)
        return pos[k]
    tmp = bmesh.new()
    vm = {}
    vuv = {}

    def tv(i, j):
        j = j % nv if wrap else j
        k = (i, j)
        if k not in vm:
            vm[k] = tmp.verts.new(pv(i, j))
            vuv[vm[k]] = (i / nu, j / nv)
        return vm[k]
    for i in range(nu):
        for j in range(nv):
            # corners (di, dj); diagonal direction alternates for a less regular tear
            if (i * 7 + j * 13 + seed) % 2:
                tris = [((0, 0), (1, 0), (1, 1)), ((0, 0), (1, 1), (0, 1))]
            else:
                tris = [((0, 0), (1, 0), (0, 1)), ((1, 0), (1, 1), (0, 1))]
            for tri in tris:
                uc = (i + sum(c[0] for c in tri) / 3) / nu
                vc = (j + sum(c[1] for c in tri) / 3) / nv
                if not keep_fn(uc, vc):
                    continue
                try:
                    tmp.faces.new([tv(i + c[0], j + c[1]) for c in tri])
                except ValueError:
                    pass
    if field_fn is not None:
        # slide boundary vertices that fell outside the implicit outline back onto it: smooth lobes instead of a stair-step fringe
        fv = {v: field_fn(*vuv[v]) for v in tmp.verts}
        moves = {}
        for v in tmp.verts:
            if fv[v] < 0 and any(e.is_boundary for e in v.link_edges):
                best = None
                for e in v.link_edges:
                    w = e.other_vert(v)
                    if fv[w] > 0 and (best is None or fv[w] > fv[best]):
                        best = w
                if best is not None:
                    tt = fv[v] / (fv[v] - fv[best])
                    moves[v] = v.co.lerp(best.co, min(max(tt, 0.0), 1.0))
        for v, co in moves.items():
            v.co = co
    tmp.normal_update()
    if out_fn is not None:
        s = sum(f.normal.dot(out_fn(f.calc_center_median())) * f.calc_area() for f in tmp.faces)
        if s < 0:
            bmesh.ops.reverse_faces(tmp, faces=tmp.faces[:])
            tmp.normal_update()
    # solidify
    tmp.verts.index_update()
    top = {}
    bot = {}
    for v in tmp.verts:
        top[v.index] = bm.verts.new(v.co)
        bot[v.index] = bm.verts.new(v.co - v.normal * t)
    tmp.verts.index_update()
    for f in tmp.faces:
        vs = [l.vert for l in f.loops]
        c = f.calc_center_median()
        uv_ = (0, 0)
        mt = mat_fn('top', c, *uv_)
        if field_fn is not None and chip > 0:
            fu = sum(vuv[v][0] for v in vs) / len(vs); fvv = sum(vuv[v][1] for v in vs) / len(vs)
            if field_fn(fu, fvv) < chip * (.55 + .9 * (N(c, seed + 40, 3.0) * .5 + .5)):
                mt = "CarMetal" if N(c, seed + 41, 2.0) > -.45 else "Charred"      # paint chipped back from the torn edge
        try:
            ft = bm.faces.new([top[v.index] for v in vs]); ft.material_index = P_.mi(mt)
            fb = bm.faces.new([bot[v.index] for v in reversed(vs)]); fb.material_index = P_.mi(mat_fn('bot', c, *uv_))
        except ValueError:
            pass
        for l in f.loops:
            if l.edge.is_boundary:
                a, b = l.vert, l.link_loop_next.vert
                try:
                    fs = bm.faces.new([top[a.index], top[b.index], bot[b.index], bot[a.index]])
                    fs.material_index = P_.mi(mat_fn('side', c, *uv_))
                except ValueError:
                    pass
    tmp.free()


def jag(u, v, seed, amp, f=9.0):
    """ragged edge threshold in 0..amp"""
    n = abs(N((u * 3.1, v * 3.1, 0), seed, f / 3)) * 1.6 + abs(N((u * 3.1, v * 3.1, 1.7), seed + 7, f / 1.5)) * .8
    return min(1.0, n) * amp


def inside_rect(u, v, tear_fn, seed, edge_amp=0.0, e=8.0):
    """superellipse-ish keep test with torn (jagged) inset weighted by tear_fn(u,v) in 0..1"""
    x, y = abs(u - .5) * 2, abs(v - .5) * 2
    r = (x ** e + y ** e) ** (1 / e)
    inset = tear_fn(u, v) * jag(u, v, seed, edge_amp) if edge_amp else 0.0
    return r < 1.0 - inset


def field_rect(u, v, tear_fn, seed, amp, e=8.0, lobes=2.0):
    """signed outline field (>0 inside): superellipse inset by a few large smooth lobes (weighted by tear_fn)"""
    x, y = abs(u - .5) * 2, abs(v - .5) * 2
    r = (x ** e + y ** e) ** (1 / e)
    lob = N((u * lobes + 7, v * lobes, 0), seed, 1.0) * .5 + .5
    lob2 = N((u * lobes * 2.3, v * lobes * 2.3, 3), seed + 3, 1.0) * .5 + .5
    return (1 - r) - tear_fn(u, v) * amp * (lob ** 1.5 * .85 + lob2 * .15)


def cut(u, v, a, b, c):
    """straight sheared edge: >0 on the kept side (normalised distance in uv units)"""
    return (a * u + b * v + c) / math.hypot(a, b)


def burn_mat(paint, kind, c, seed, burn=.35, scrape=.18, f=1.1):
    """slot choice for a panel face"""
    n = FBM(c, seed, f, 2)
    if kind == 'side':
        return "CarMetal" if n < .1 else "Charred"
    if kind == 'bot':
        return "Charred" if n > -.05 else "CarMetal"
    if n > 0.5 - burn * .6:
        return "Charred"
    if n < -0.55 + scrape * .5:
        return "CarMetal"
    return paint


def finish(Pt, name, mass, notes=""):
    bm = Pt.bm
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    c, vol = com_of(bm)
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=-c)
    ob = Pt.build(smooth=38, name=name)
    for s in ob.data.materials:
        if s.name == "CarMetal": s.metallic = 0.85; s.roughness = 0.5
        if s.name == "Rim": s.metallic = 0.9; s.roughness = 0.35
    ob.data.update()
    mn, mx = bc.dims(ob)
    size = mx - mn
    info = dict(name=name, tris=bc.tri_count(ob), dims_m=[round(size.x, 3), round(size.z, 3), round(size.y, 3)],
                mass_kg=mass, slots=[s.name for s in ob.data.materials], origin="centre of mass",
                com_offset_m=[round(c.x, 3), round(c.z, 3), round(-c.y, 3)], notes=notes)
    if info["tris"] > MAXTRIS:
        print("WARNING %s tris %d > %d" % (name, info["tris"], MAXTRIS))
    return ob, info


def new_part(name):
    return bc.Part(name)


# ---------------------------------------------------------------------------------------------- panels
def hood():
    Pt = new_part("Debris_Hood")
    hw, hl, seed = .64, .60, 3
    def pos(u, v, zoff=0.0):
        x, f = (u - .5) * 2 * hw, (v - .5) * 2 * hl
        z = .055 * (1 - (x / hw) ** 2) - .03 * (f / hl) ** 2 + zoff
        z -= .07 * sm((f / hl - .72) / .28)                        # front lip rolls down
        p = Vector((x, f, z))
        # crease fold across the middle (hinged crumple), oblique
        p = curl_pt(p, Vector((0, -.05, 0)), Vector((.25, 1, 0)), 14.0, 0.42)
        # corner rolled up and back
        p = curl_pt(p, Vector((-.2, .28, 0)), Vector((-.62, .78, 0)), 3.6, 2.5)
        p = twist(p, Vector((0, 1, 0)), .30)
        p.z += FBM(p, seed, 3.2) * .05 * (0.4 + sm((p.y + .3) / .6)) + .02 * abs(N(p, 9, 7))
        return P(p.x, p.y, p.z)
    def tear(u, v):
        return 1.0 if (u < .22 or v > .86 or (u > .8 and v < .3)) else 0.25
    fld = lambda u, v: min(field_rect(u, v, tear, seed, .42, 8, 1.6), cut(u, v, 1, -1, .62), cut(u, v, -.3, 1, .55) * 1.4)
    shell(Pt, 16, 14, pos, None, .014, lambda k, c, u, v: burn_mat("Paint", k, c, seed, .38), out_fn=lambda p: Vector((0, 0, 1)), seed=seed, field_fn=fld, chip=.07)
    # inner stiffener frame: ribs + border, follows the same deformation
    def ribs(u, v):
        return max(.09 - min(u, 1 - u, v, 1 - v), .05 - abs(u - .5), .05 - abs(v - .45))
    shell(Pt, 16, 14, lambda u, v: pos(u, v, -.04), None, .01, lambda k, c, u, v: "Charred" if FBM(c, seed + 4, 2.5) > .0 else "CarMetal", out_fn=lambda p: Vector((0, 0, -1)), seed=seed,
          field_fn=lambda u, v: min(fld(u, v), ribs(u, v)))
    return finish(Pt, "Debris_Hood", 14, "outer skin + inner stiffener frame; corner curled, mid crease, torn rear edge")


def trunklid():
    Pt = new_part("Debris_Trunklid")
    hw, hl, seed = .60, .30, 11
    def pos(u, v, zoff=0.0):
        x, f = (u - .5) * 2 * hw, (v - .5) * 2 * hl
        z = .04 * (1 - (x / hw) ** 2) + .035 * sm((f / hl - .6) / .4) + zoff     # raised lip at the rear
        p = Vector((x, f, z))
        p = curl_pt(p, Vector((.2, 0, 0)), Vector((1, -.3, 0)), 9.0, .55)
        p = curl_pt(p, Vector((-.3, -.1, 0)), Vector((-.8, -.6, 0)), 4.0, 2.2)
        p = twist(p, Vector((1, 0, 0)), -.55)
        p.z += FBM(p, seed, 3.8) * .045
        return P(p.x, p.y, p.z)
    tear = lambda u, v: 1.0 if (u > .75 or v < .2) else .2
    fld = lambda u, v: min(field_rect(u, v, tear, seed, .40, 8, 1.5), cut(u, v, -1, -1, 1.62))
    shell(Pt, 18, 9, pos, None, .013, lambda k, c, u, v: burn_mat("Paint", k, c, seed, .4), out_fn=lambda p: Vector((0, 0, 1)), seed=seed, field_fn=fld, chip=.07)
    shell(Pt, 18, 9, lambda u, v: pos(u, v, -.035), None, .01, lambda k, c, u, v: "Charred" if FBM(c, seed + 4, 2.5) > -.1 else "CarMetal", out_fn=lambda p: Vector((0, 0, -1)), seed=seed,
          field_fn=lambda u, v: min(fld(u, v), max(.13 - min(u, 1 - u, v, 1 - v), .07 - abs(u - .5))))
    return finish(Pt, "Debris_Trunklid", 11, "outer skin + inner frame; folded hinge side, torn latch edge")


def door():
    Pt = new_part("Debris_Door")
    hw, hh, seed = .52, .40, 21     # panel space: a = along car (f), b = height, c = outward (x)
    M = lambda a, b, c: P(c, a, b)  # a -> forward, b -> up, c -> x (outward)
    def pos(u, v, zoff=0.0):
        a, b = (u - .5) * 2 * hw, (v - .5) * 2 * hh
        c = .025 * (1 - (a / hw) ** 2) * sm((.5 - abs(v - .4)) * 2) + zoff + .012 * math.sin(b * 6)   # shoulder swell
        p = Vector((a, b, c))
        p = curl_pt(p, Vector((.1, -.15, 0)), Vector((.3, -1, 0)), 5.0, 1.9)      # bottom edge curled inward
        p = curl_pt(p, Vector((.15, .1, 0)), Vector((1, .15, 0)), 18.0, .55)      # rear fold
        p = twist(p, Vector((1, 0, 0)), .55)
        p.z += FBM(p, seed, 3.0) * .06 * (0.3 + sm((.5 - p.x) * 1.4)) + .012 * N(p, 3, 9)
        return M(p.x, p.y, p.z)
    tear = lambda u, v: 1.0 if (v < .28 or u > .85) else .2
    def win(u, v):   # signed field of the window opening (>0 outside the opening)
        return max(abs(u - .5) - .36, abs(v - .775) - .165)
    fld = lambda u, v: min(field_rect(u, v, tear, seed, .38, 7, 1.5), win(u, v), cut(u, v, 1, 1, -.12))
    mf = lambda k, c, u, v: burn_mat("Paint", k, c, seed, .4)
    shell(Pt, 16, 13, pos, None, .018, mf, out_fn=lambda p: Vector((1, 0, 0)), seed=seed, field_fn=fld, chip=.06)
    shell(Pt, 16, 13, lambda u, v: pos(u, v, -.045), None, .01, lambda k, c, u, v: "Charred" if FBM(c, seed + 4, 2.5) > -.05 else "CarMetal", out_fn=lambda p: Vector((-1, 0, 0)), seed=seed,
          field_fn=lambda u, v: min(fld(u, v), max(.12 - min(u, 1 - u, v, 1 - v), .06 - abs(v - .4))))
    # handle stub
    Pt.rbox(P(.06, -.06, .02), (.03, .13, .03), "CarMetal", .006, 1, rot=None)
    return finish(Pt, "Debris_Door", 20, "outer skin with window opening + inner frame; bottom edge curled, twisted")


def bumper():
    Pt = new_part("Debris_Bumper")
    hw, hh, seed = .85, .14, 31
    def pos(u, v, zoff=0.0):
        x, z = (u - .5) * 2 * hw, (v - .5) * 2 * hh
        f = -.10 * (x / hw) ** 2 + .035 * math.cos(z / hh * 1.4) + zoff          # wraps back at the ends + profile bulge
        p = Vector((x, f, z))
        p = curl_pt(p, Vector((.45, 0, 0)), Vector((1, .1, 0)), 3.0, 1.4) if False else p
        # kink: bumper bends back hard at one end, ripped at the other
        k = Vector((p.x - .38, p.y, 0))
        ang = .65 * sm((p.x - .38) / .12)
        p = Vector((.38 + (p.x - .38) * (math.cos(ang) if p.x > .38 else 1), p.y + (math.sin(ang) * (p.x - .38) if p.x > .38 else 0), p.z))
        p = twist(p, Vector((1, 0, 0)), .6)
        p.y += FBM(p, seed, 3.0) * .035 + .03 * sm((-p.x - .45) / .2) * N(p, 3, 5)
        return P(p.x, p.y, p.z)
    tear = lambda u, v: 1.0 if u < .3 else .15
    fld = lambda u, v: min(field_rect(u, v, tear, seed, .22, 10, 1.4), cut(u, v, 1, -.35, .135))
    shell(Pt, 34, 7, pos, None, .016, lambda k, c, u, v: burn_mat("Paint", k, c, seed, .35), out_fn=lambda p: Vector((0, -1, 0)), seed=seed, field_fn=fld, chip=.09)
    # reinforcement beam (bent), exposed metal
    beam = [P(x, -.07 - .06 * (x / .85) ** 2 - (.08 * ((x - .2) / .5) ** 2 if x < .3 else 0) * 0.0, .0) for x in (-.8, -.5, -.2, .1, .4, .7)]
    Pt.tube([b + Vector((0, 0, .015 * math.sin(i))) for i, b in enumerate(beam)], [(.04, .03)] * len(beam), "CarMetal", 6, cap=True)
    return finish(Pt, "Debris_Bumper", 9, "bumper cover (one end kinked, one end ripped off) + bent steel beam")


def scrap(idx):
    names = "ABCD"
    cfg = [dict(w=.80, l=.55, nu=16, nv=12, e=3.0, k1=5.5, th1=2.6, tw=1.6, cr=.06, f1=3.2, mass=3.0),
           dict(w=.50, l=.85, nu=12, nv=16, e=2.2, k1=8.0, th1=3.1, tw=-2.2, cr=.07, f1=3.9, mass=3.5),
           dict(w=.42, l=.40, nu=11, nv=11, e=2.4, k1=9.0, th1=3.6, tw=1.2, cr=.08, f1=4.6, mass=1.4),
           dict(w=1.0, l=.28, nu=20, nv=9, e=4.0, k1=4.0, th1=2.0, tw=2.8, cr=.05, f1=3.0, mass=2.6)][idx]
    seed = 40 + idx * 13
    Pt = new_part("Debris_Scrap_" + names[idx])
    hw, hl = cfg["w"] / 2, cfg["l"] / 2
    ang = random.Random(seed).uniform(0, 6.28)
    def pos(u, v):
        x, y = (u - .5) * 2 * hw, (v - .5) * 2 * hl
        p = Vector((x, y, .02 * math.sin(x * 5 + idx)))
        p = curl_pt(p, Vector((math.cos(ang) * .06, math.sin(ang) * .06, 0)), Vector((math.cos(ang), math.sin(ang), 0)), cfg["k1"], cfg["th1"])
        p = curl_pt(p, Vector((-.1, -.1 + idx * .04, 0)), Vector((-.8, -.5, 0)), 14.0, .9)
        p = twist(p, Vector((1, .35, 0)), cfg["tw"])
        p.z += FBM(p, seed, cfg["f1"]) * cfg["cr"] + .03 * abs(N(p, seed + 2, 8))
        return P(p.x, p.y, p.z)
    tear = lambda u, v: 1.0
    rg = random.Random(seed + 5)
    c1 = (rg.uniform(.6, 1.4) * rg.choice([-1, 1]), rg.uniform(.6, 1.4) * rg.choice([-1, 1]))
    c2 = (rg.uniform(.5, 1.2) * rg.choice([-1, 1]), rg.uniform(.5, 1.2) * rg.choice([-1, 1]))
    fld = lambda u, v: min(field_rect(u, v, tear, seed, .55, cfg["e"], 1.3), cut(u, v, c1[0], c1[1], .78 - .5 * (c1[0] + c1[1])), cut(u, v, c2[0], c2[1], .85 - .5 * (c2[0] + c2[1])))
    shell(Pt, cfg["nu"], cfg["nv"], pos, None, .012, lambda k, c, u, v: burn_mat("Paint" if idx % 2 == 0 else "CarMetal", k, c, seed, .55, .3, 3.0), out_fn=lambda p: Vector((0, 0, 1)), seed=seed, field_fn=fld, chip=.08)
    return finish(Pt, "Debris_Scrap_" + names[idx], cfg["mass"], "torn, twisted sheet metal")


# ---------------------------------------------------------------------------------------------- solids
def wheel():
    Pt = new_part("Debris_Wheel")
    k, t = 1.0, 1.0
    half = [(.205, .092), (.216, .103), (.240, .110), (.268, .113), (.295, .104), (.318, .082), (.330, .056), (.330, 0)]
    prof = [(r, -h) for r, h in half] + [(r, h) for r, h in reversed(half[:-1])]
    # tread notches: alternate radius on the two outer rings
    Pt.lathe([(r, h) for r, h in prof], "Rubber", Vector((1, 0, 0)), Vector((0, 0, 0)), 20)
    # rim: barrel + lips + dish + spokes
    X = Vector((1, 0, 0))
    Pt.lathe([(.195, -.095), (.205, -.098), (.205, -.085), (.188, -.07), (.185, .0), (.188, .07), (.205, .085), (.205, .098), (.195, .095)], "Rim", X, Vector(), 24)
    Pt.lathe([(.0, .05), (.04, .05), (.045, .075), (.085, .075), (.09, .05), (.185, .03)], "Rim", X, Vector(), 20, cap_ends=False)
    Pt.lathe([(.0, .09), (.04, .09), (.05, .075)], "CarMetal", X, Vector(), 16)
    for i in range(5):                                             # five thick spokes on the face, lug nuts
        a = i / 5 * math.tau + .3
        d = Vector((0, math.cos(a), math.sin(a)))
        Pt.bar(d * .08 + X * .062, d * .19 + X * .062, .05, .02, "Rim", .004, 1)
        Pt.cyl(d * .062 + X * .085, .011, .011, .02, "CarMetal", 6, X.to_track_quat('Z', 'Y'))
    # brake disc behind the dish, scorched
    Pt.lathe([(.07, -.03), (.14, -.03), (.14, -.045), (.07, -.045)], "Charred", X, Vector(), 20)
    # damage: dent in the tyre shoulder (flat spot) through vertex push
    for v in Pt.bm.verts:
        r = math.hypot(v.co.y, v.co.z)
        if r > .2:
            a = math.atan2(v.co.z, v.co.y)
            d = max(0, math.cos(a - 1.1)) ** 6
            v.co.y *= 1 - .06 * d; v.co.z *= 1 - .06 * d
    return finish(Pt, "Debris_Wheel", 21, "tyre (Rubber) + rim (Rim) + 5 spokes, brake disc; axis = X")


def lump(Pt, c, r, slot_fn, seed, sub=2, squash=(1, 1, 1), crumple=.25, facets=0.0):
    before = Pt.snapshot()
    vs = Pt.ico(c, (r * squash[0], r * squash[1], r * squash[2]), "CarMetal", sub)
    for v in vs:
        p = v.co - Vector(c)
        n = p.normalized()
        v.co = Vector(c) + p * (1 + crumple * FBM(n * 1.6, seed, 1.6, 3))
    for f in Pt.bm.faces:
        if f not in before:
            f.material_index = Pt.mi(slot_fn(f.calc_center_median()))


def chunk(idx):
    seed = 70 + idx * 9
    names = "ABC"
    Pt = new_part("Debris_Chunk_" + names[idx])
    if idx == 0:        # engine block: cylinder bank + head + sump, crushed
        Pt.rbox(Vector((0, 0, 0)), (.42, .56, .34), "CarMetal", .035, 2)
        Pt.rbox(Vector((0, .02, .22)), (.38, .5, .09), "Charred", .02, 1)            # head
        for i in range(4):
            Pt.cyl(Vector((0, -.2 + i * .13, .30)), .045, .04, .08, "CarMetal", 8)  # plug towers
        Pt.rbox(Vector((0, .0, -.22)), (.3, .5, .1), "Charred", .03, 1)             # sump
        Pt.cyl(Vector((.0, -.34, .02)), .07, .07, .14, "CarMetal", 10, Euler((math.pi / 2, 0, 0)))  # crank snout
        Pt.cyl(Vector((.24, .0, .12)), .03, .03, .42, "CarMetal", 8, Euler((math.pi / 2, 0, 0)))     # manifold pipe
        mass = 45
    elif idx == 1:      # gearbox / transmission housing
        Pt.cyl(Vector((0, 0, 0)), .17, .12, .56, "CarMetal", 14, Euler((math.pi / 2, 0, 0)))
        Pt.cyl(Vector((0, -.18, .02)), .2, .2, .14, "Charred", 14, Euler((math.pi / 2, 0, 0)))
        Pt.rbox(Vector((.0, .08, -.14)), (.18, .26, .08), "CarMetal", .02, 1)
        Pt.cyl(Vector((0, .33, 0)), .05, .05, .16, "CarMetal", 10, Euler((math.pi / 2, 0, 0)))
        mass = 28
    else:               # crushed lump of metal
        lump(Pt, (0, 0, 0), .26, lambda c: "Charred" if FBM(c, seed, 3) > .0 else "CarMetal", seed, 3, (1.0, .8, .7), .55)
        Pt.rbox(Vector((.1, .05, .12)), (.2, .02, .2), "CarMetal", .004, 1, Euler((0, 0, .6)))
        mass = 9
    for v in Pt.bm.verts:                               # crush: low-frequency squash + noise
        p = v.co
        p += Vector((N(p, seed, 4), N(p, seed + 1, 4), N(p, seed + 2, 4))) * .02
    # burnt/scraped slots by noise on every face
    for f in Pt.bm.faces:
        cname = Pt.slots[f.material_index]
        if cname == "CarMetal" and FBM(f.calc_center_median(), seed + 5, 4.0) > .15:
            f.material_index = Pt.mi("Charred")
    return finish(Pt, "Debris_Chunk_" + names[idx], mass, "engine / drivetrain lump, convex-friendly")


# ---------------------------------------------------------------------------------------------- barrel
BR, BH = .29, .88
def barrel_r(z):
    """200 L steel drum profile: rolled top/bottom chimes and two pressed hoops"""
    r = BR
    for zc in (.22, .44, .66):                                  # pressed ribs (rounded, ~2 cm tall)
        r += .020 * math.exp(-((z - zc) / .030) ** 2)
    r += .022 * math.exp(-((z - (BH - .018)) / .020) ** 2)      # rolled top chime
    r += .022 * math.exp(-((z - .018) / .020) ** 2)             # rolled bottom chime
    r -= .012 * (sm((z - (BH - .05)) / .03))                    # shoulder steps in slightly under the top rim
    return r


def barrel_half(which):
    """which 0 = base end (A, torn at the top), 1 = lid end (B, torn at the bottom).
    Cylindrical shell about 240 deg of the circumference with a few outward-peeled petals along the torn edge."""
    seed = 90 + which * 5
    Pt = new_part("Debris_BarrelHalf_" + "AB"[which])
    rg = random.Random(seed)
    npet = 5
    span = 4.25
    th0 = which * 2.2 + .4
    zb0 = .50 if which == 0 else .38                               # nominal tear height
    petL = [rg.uniform(.20, .32) for _ in range(npet)]
    petB = [zb0 + rg.uniform(-.08, .08) for _ in range(npet)]      # petal base height per petal
    slit = .045                                                    # slit width between petals (fraction of petal)

    def petal(u):
        x = u * npet
        i = min(int(x), npet - 1)
        return i, x - i

    def pos(u, v):
        th = th0 + u * span
        z = v * BH
        i, t = petal(u)
        # distance toward the torn side; petals hinge a petal-length below the tear line and bend OUT (away from the blast centre)
        zb = petB[i] + .05 * math.sin(t * math.pi)
        sg = 1 if which == 0 else -1
        dz = sg * (z - zb)                                        # positive on the torn side
        hinge = -petL[i] * (.95 + .12 * math.sin(i * 1.7))        # hinge position (negative = below the tear line)
        r = barrel_r(z)
        zz = z
        d = dz - hinge
        if d > 0:
            al, up = curl(d, 3.2 + i * .35, 1.25 + .3 * math.sin(i * 2.3))
            zz = zb + sg * (hinge + al)
            r = r + up
        # straight vertical tear edges peel outward a little
        e = min(u, 1 - u)
        r += .05 * sm((.07 - e) / .07) ** 1.5 * (.6 + .4 * math.sin(v * 9 + which))
        # blast dent: broad, shallow, not crumpled
        r *= 1 - .07 * sm(1 - abs(u - (.5 if which == 0 else .35)) / .22) * sm((1 - abs(v - .5) * 1.6))
        return Vector((r * math.cos(th), r * math.sin(th), zz))

    def field(u, v):
        th = th0 + u * span
        z = v * BH
        i, t = petal(u)
        zb = petB[i] + .05 * math.sin(t * math.pi)
        # pointed petal tips + narrow slits between petals
        tip = .055 * abs(t - .5) * 2 * petL[i] * 1.6
        dz = ((zb - tip) - z) if which == 0 else (z - (zb + tip))   # >0 = kept
        slitf = (min(t, 1 - t) - slit) * .5                          # <0 inside a slit (only where the petal is bent)
        slit_ok = slitf if (dz < petL[i] * .75) else 1.0
        ends = min(u, 1 - u) - .01 - .02 * math.sin(v * 11 + which * 3)
        return min(dz * 1.0, slit_ok * 1.0 if slit_ok < 0 else 1.0, ends * 3.0)

    # sample the field in the grid: nv spans the full height, torn side removed by the field
    mf = lambda k, c, u, v: ("Charred" if FBM(c, seed, 1.3, 2) > .38 else "CarMetal") if k != 'top' else ("BarrelPaint" if FBM(c, seed, 1.0, 2) < .35 else "Charred")
    shell(Pt, 30, 18, pos, None, .007, mf, out_fn=lambda p: Vector((p.x, p.y, 0)), seed=seed, field_fn=field, chip=.05)
    return finish(Pt, "Debris_BarrelHalf_" + "AB"[which], 4.0, "half of a 200 L steel drum shell (A = base end, B = lid end): rolled chimes, pressed hoops, petalled torn edge")


def barrel_lid():
    Pt = new_part("Debris_BarrelLid")
    seed = 99
    R = BR
    def pos(u, v):
        x, y = (u - .5) * 2 * R, (v - .5) * 2 * R
        r = math.hypot(x, y)
        z = -.025 * sm((r - R * .82) / (R * .18)) * 0 + .018 * sm((R * .86 - r) / .1) * 0 + (.03 if r > R * .88 else 0) * sm((r - R * .86) / .02)
        z += -.006 * math.cos(r / R * 9) * (r < R * .8)
        p = Vector((x, y, z))
        p = curl_pt(p, Vector((.08, -.05, 0)), Vector((.9, .3, 0)), 5.0, 1.6)
        p = twist(p, Vector((1, .2, 0)), 1.5)
        p.z += FBM(p, seed, 4) * .05 * sm(r / R)
        return Vector((p.x, p.y, p.z))
    keep = lambda u, v: inside_rect(u, v, lambda *_: 1.0, seed, .12, 2.0)
    shell(Pt, 20, 20, pos, keep, .012, lambda k, c, u, v: "Charred" if (FBM(c, seed, 2.5) > .15 and k != 'bot') or (k == 'bot' and FBM(c, seed, 2) > -.1) else ("CarMetal" if k != 'top' else "BarrelPaint"),
          out_fn=lambda p: Vector((0, 0, 1)), seed=seed)
    Pt.cyl(Vector((.1, .05, .02)), .03, .03, .035, "CarMetal", 8)       # bung
    return finish(Pt, "Debris_BarrelLid", 1.4, "dished drum lid with rolled rim, bent and torn")


# ---------------------------------------------------------------------------------------------- build all + export
PARTS = [hood, door, bumper, trunklid, wheel] + [lambda i=i: scrap(i) for i in range(4)] + [lambda i=i: chunk(i) for i in range(3)] + \
        [barrel_lid, lambda: barrel_half(0), lambda: barrel_half(1)]


def export(ob, folder):
    os.makedirs(folder, exist_ok=True)
    bc.select_only(ob)
    bpy.ops.export_scene.fbx(filepath=os.path.join(folder, ob.name + ".fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False, object_types={'MESH'})


def run_debris(only=None):
    bc.reset_scene()
    random.seed(5)
    folder = os.path.join(OUT, "debris")
    infos, objs = [], []
    for fn in PARTS:
        ob, info = fn()
        if only and not any(o in info["name"] for o in only):
            bpy.data.objects.remove(ob); continue
        ob.location = (0, 0, 0)
        export(ob, folder)
        info["fbx"] = "debris/" + info["name"] + ".fbx"
        infos.append(info); objs.append(ob)
        print("DEBRIS %-20s tris=%4d dims(x,y,z)=%s mass=%s" % (info["name"], info["tris"], info["dims_m"], info["mass_kg"]))
    json.dump(dict(convention="Unity axes: X right, Y up, Z forward; origin = centre of mass; dims_m = bounding box (x, y, z) in metres; "
                   "slots Paint (tint to body colour), CarMetal, Charred, Rubber, Rim, BarrelPaint; one convex collider per part.",
                   parts=infos), open(os.path.join(OUT, "Debris_Parts.json"), "w"), indent=2)
    if PREVIEW:
        lineup(objs, os.path.join(OUT, 'previews', 'debris_lineup.png'))
    return objs, infos


def shot(objs, path, az=20, el=18, size=(1600, 900), pad=1.02, floor=True):
    """Workbench preview with a tight perspective camera (85 mm) framing the bounding box of objs."""
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    sh = scn.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = True; sh.shadow_intensity = .45
    sh.show_cavity = True; sh.cavity_type = 'BOTH'; sh.show_object_outline = False
    scn.render.resolution_x, scn.render.resolution_y = size
    scn.render.image_settings.file_format = 'PNG'; scn.render.filepath = path
    scn.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("W"); w.color = (.58, .6, .64); scn.world = w
    keep = set(objs)
    for o in scn.objects:
        o.hide_render = o not in keep
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) / 2
    made = []
    if floor:
        me = bpy.data.meshes.new("fl"); b = bmesh.new()
        bmesh.ops.create_grid(b, x_segments=1, y_segments=1, size=max((mx - mn).x, (mx - mn).y) * 0.9, matrix=Matrix.Translation((ctr.x, ctr.y, mn.z - .003)))
        b.to_mesh(me); b.free()
        fm = bpy.data.materials.new("FloorP"); fm.diffuse_color = (.45, .45, .44, 1); me.materials.append(fm)
        fo = bpy.data.objects.new("fl", me); scn.collection.objects.link(fo); made.append(fo)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); made.append(cam)
    cam.data.lens = 85; cam.data.sensor_width = 36
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    q = (-d).to_track_quat('-Z', 'Y'); R = q.to_matrix()
    rx, ry = R.col[0], R.col[1]
    ext_x = max(abs((p - ctr).dot(rx)) for p in pts); ext_y = max(abs((p - ctr).dot(ry)) for p in pts)
    hfov = 2 * math.atan(18 / 85); aspect = size[0] / size[1]
    dist = max(ext_x / math.tan(hfov / 2), ext_y / (math.tan(hfov / 2) / aspect)) * pad 
    cam.location = ctr + d * dist; cam.rotation_euler = q.to_euler(); scn.camera = cam
    bpy.ops.render.render(write_still=True)
    for o in made: bpy.data.objects.remove(o, do_unlink=True)
    for o in scn.objects: o.hide_render = False
    bpy.data.worlds.remove(w)


def lineup(objs, path, az=22, el=16, size=(2000, 900)):
    """Two rows of parts beside a 1.8 m figure (real scale), Workbench preview."""
    fig = bc.Part("Figure")
    fig.cyl(Vector((-.1, 0, .42)), .08, .06, .84, "Pants", 10); fig.cyl(Vector((.1, 0, .42)), .08, .06, .84, "Pants", 10)
    fig.rbox(Vector((0, 0, 1.2)), (.42, .22, .56), "Top", .05, 2)
    fig.sphere(Vector((0, 0, 1.68)), (.1, .11, .12), "Skin")
    fig.cyl(Vector((-.27, 0, 1.15)), .045, .04, .55, "Top", 8); fig.cyl(Vector((.27, 0, 1.15)), .045, .04, .55, "Top", 8)
    fo = fig.build(smooth=0, name="Figure")
    fo.location = (-.9, 0, 0)
    big = ["Debris_Hood", "Debris_Door", "Debris_Bumper", "Debris_Trunklid"]
    x1, x2 = .2, -.3
    for ob in objs:
        bpy.context.view_layer.update()
        mn, mx = bc.dims(ob)
        w = mx.x - mn.x
        if ob.name in big:
            ob.location = (x1 - mn.x, 0, -mn.z); x1 += w + .3
        else:
            ob.location = (x2 - mn.x, 1.9, -mn.z); x2 += w + .22
    bpy.context.view_layer.update()
    shot([fo] + list(objs), path, az, el, size)
