"""Char2_* accessories re-fitted to Char_Human: HairShort, HairLong, Cap, Hood, Backpack (rigid meshes).

Pivots (contract): Hair/Cap/Hood at the Head bone head (0, 0, 1.59); Backpack at the Chest bone head (0, 0.01, 1.25).
Every mesh is authored in world space around the real Char_Human surface (BVH of the built body), then shifted so the
object origin sits at the pivot (object transform identity) and exported with MESH only.

Run: blender --background --factory-startup --python make_human_accessories.py
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix, noise
from mathutils.bvhtree import BVHTree

import make_human as MH

OUT = os.path.join(MH.ROOT, "Assets", "Art", "Models", "Characters")
HEAD_PIVOT = Vector((0.0, 0.0, 1.59))
CHEST_PIVOT = Vector((0.0, 0.01, 1.25))
HC = Vector((0.0, 0.008, 1.690))          # head centre used for the hair/cap parameterisations


def sstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


class Body:
    def __init__(self, obj):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.transform(obj.matrix_world)
        # keep only the main body component (drop eyes, brows, ribbon islands)
        bm.faces.ensure_lookup_table()
        seen, comps = set(), []
        for f0 in bm.faces:
            if f0 in seen:
                continue
            st, comp = [f0], []
            seen.add(f0)
            while st:
                g = st.pop(); comp.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h not in seen:
                            seen.add(h); st.append(h)
            comps.append(comp)
        comps.sort(key=len, reverse=True)
        keep = set(comps[0])
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f not in keep], context='FACES')
        self.bvh = BVHTree.FromBMesh(bm)
        drop = [f for f in bm.faces if abs(f.calc_center_median().x) > 0.205 and f.calc_center_median().z > 1.33]
        bmesh.ops.delete(bm, geom=drop, context='FACES')
        self.tbvh = BVHTree.FromBMesh(bm)          # torso only (no arms) for straps
        bm.free()

    def ray(self, o, d, maxd=2.0):
        h = self.bvh.ray_cast(Vector(o), Vector(d), maxd)
        return h if h[0] is not None else None

    def head_hit(self, az, el, c=HC):
        """Surface point seen from the head centre in direction (az, el): az 0 = front, +az toward +X."""
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.cos(e) * math.sin(a), -math.cos(e) * math.cos(a), math.sin(e)))
        h = self.ray(c, d, 0.5)
        return (h[0], h[1], d) if h else (None, None, d)

    def push_out(self, p, clear):
        loc, n, idx, dist = self.bvh.find_nearest(p)
        if loc is None:
            return p
        inside = (p - loc).dot(n) < 0
        if inside or dist < clear:
            return loc + n * clear
        return p


# ------------------------------------------------------------------ mesh helpers
class M:
    def __init__(self, name, slot):
        self.bm = bmesh.new()
        self.name = name
        self.slot = slot

    def v(self, p):
        return self.bm.verts.new(Vector(p))

    def f(self, *vs):
        try:
            return self.bm.faces.new(vs)
        except ValueError:
            return None

    def grid(self, pts, closed_u=False, flip=False):
        """pts: [rows][cols] of Vectors -> quads."""
        vs = [[self.v(p) for p in row] for row in pts]
        for i in range(len(vs) - 1):
            n = len(vs[i])
            for j in range(n if closed_u else n - 1):
                a, b = vs[i][j], vs[i][(j + 1) % n]
                c, d = vs[i + 1][(j + 1) % n], vs[i + 1][j]
                q = (a, b, c, d)
                if flip:
                    q = q[::-1]
                if len(set(q)) == 4:
                    self.f(*q)
                elif len(set(q)) == 3:
                    t = []
                    for x in q:
                        if x not in t:
                            t.append(x)
                    self.f(*t)
        return vs

    def tube(self, pts, radii, sides=3, flat=1.0, up=None, cap_end=True):
        """Tapered prism along a polyline; flat < 1 squashes the section along `up` (a hair lock)."""
        rings = []
        n = len(pts)
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)])
            if t.length < 1e-9:
                t = Vector((0, 0, -1))
            t.normalize()
            ref = up if up is not None else (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))
            s = t.cross(ref)
            if s.length < 1e-6:
                s = t.cross(Vector((1, 0, 0)))
            s.normalize()
            u = s.cross(t).normalized()
            r = radii[i] if isinstance(radii, (list, tuple)) else radii
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                ring.append(self.v(p + s * math.cos(a) * r + u * math.sin(a) * r * flat))
            rings.append(ring)
        for i in range(n - 1):
            for k in range(sides):
                self.f(rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k])
        if cap_end and radii:
            self.f(*rings[0][::-1])
            last = rings[-1]
            if len(set(last)) >= 3:
                self.f(*last)

    def finish(self, pivot, mat_color, mat_name, rough=0.8):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.transform(Matrix.Translation(-pivot))
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = True
        mt = bpy.data.materials.new(mat_name)
        mt.use_nodes = True
        mt.diffuse_color = (*mat_color, 1)
        b = mt.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (*mat_color, 1)
        b.inputs["Roughness"].default_value = rough
        me.materials.append(mt)
        # simple world-metre box UVs
        me.uv_layers.new(name="UVMap")
        bm2 = bmesh.new(); bm2.from_mesh(me)
        uv = bm2.loops.layers.uv.active
        for f in bm2.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                if ax == 1:
                    u, v = p.x, p.z
                elif ax == 0:
                    u, v = p.y, p.z
                else:
                    u, v = p.x, p.y
                l[uv].uv = (u / 0.25, v / 0.25)
        bm2.to_mesh(me); bm2.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def export_obj(ob):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(OUT, ob.name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z',
                             axis_up='Y', bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types={'MESH'})
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print("[export] %s tris=%d" % (path, tris))
    return tris


# ------------------------------------------------------------------ hair
def hairline_el(az):
    """Lowest elevation (deg) hair grows to, by azimuth: forehead line, temples, sideburn, behind the ear, nape."""
    a = abs(((az + 180) % 360) - 180)
    keys = [(0, 47), (25, 44), (45, 37), (62, 25), (72, 8), (80, -6), (88, 4), (98, 28), (112, 28), (125, 14), (145, -8), (165, -30), (180, -38)]
    for (a0, e0), (a1, e1) in zip(keys, keys[1:]):
        if a0 <= a <= a1:
            t = (a - a0) / (a1 - a0)
            return e0 + (e1 - e0) * t * t * (3 - 2 * t)
    return keys[-1][1]


def hair_shell(body, mesh, thick, clump_n=26, clump_amp=0.0035, rings=15, segs=56, ear_skip=True, seed=1):
    """Scalp cap: grid in (azimuth, elevation) with a natural hairline; radial clump ridges swirl out of the crown."""
    rnd = random.Random(seed)
    pts = []
    for i in range(rings + 1):
        f = i / rings
        row = []
        for j in range(segs):
            az = 360.0 * j / segs - 180.0 + 0.0
            el_min = hairline_el(az)
            # ragged hairline: noise on the lower edge
            el_min += 4.0 * noise.noise(Vector((az * 0.09, seed, 0.3)))
            el = 90.0 - f * (90.0 - el_min)
            hit, nrm, d = body.head_hit(az, el)
            if hit is None:
                hit = HC + d * 0.09; nrm = d
            nn = (nrm * 0.5 + d * 0.5).normalized()
            # clump pattern: ridges along the combing direction (swirl about the crown)
            swirl = az + 0.9 * (90.0 - el) * (1 if az > 0 else -1) * 0.5
            cl = 0.5 + 0.5 * math.sin(math.radians(swirl) * clump_n * 0.5 + 1.7 * noise.noise(Vector((az * 0.05, el * 0.05, seed))))
            tt = thick * (0.55 + 0.45 * math.sin(math.pi * min(1.0, f * 1.15))) if i < rings else thick * 0.25
            t = thick * 0.7 + clump_amp * cl * (0.4 + 0.6 * min(1.0, f * 3))
            if i == rings:
                t *= 0.35
            if i == 0:
                t = thick * 1.05
            p = hit + nn * t
            row.append(p)
        pts.append(row)
    # crown pole: collapse ring 0
    c0 = sum(pts[0], Vector()) / len(pts[0])
    pts[0] = [c0 for _ in pts[0]]
    vs = mesh.grid(pts, closed_u=True)
    # underside rim to close the shell back onto the scalp
    inner = []
    for j in range(segs):
        az = 360.0 * j / segs - 180.0
        el_min = hairline_el(az) + 4.0 * noise.noise(Vector((az * 0.09, seed, 0.3)))
        hit, nrm, d = body.head_hit(az, el_min - 3.0)
        inner.append((hit if hit else HC + d * 0.09) + (nrm if nrm else d) * 0.0008)
    last = [vs[-1][j] for j in range(segs)]
    iv = [mesh.v(p) for p in inner]
    for j in range(segs):
        mesh.f(last[j], last[(j + 1) % segs], iv[(j + 1) % segs], iv[j])
    return vs


def comb_locks(body, mesh, n, seed, length=(0.025, 0.045), width=(0.0055, 0.008), az_range=(-110, 110), el_range=(30, 86),
               swirl=0.5, lift=0.0075, flat=0.45, forward_bias=0.0):
    rnd = random.Random(seed)
    for k in range(n):
        az = rnd.uniform(*az_range)
        el = rnd.uniform(*el_range)
        L = rnd.uniform(*length)
        w = rnd.uniform(*width)
        # comb direction in (az, el): flow away from the crown, swirled
        ang = math.radians(az)
        flow_az = (1 if az >= 0 else -1) * (0.4 + swirl * (90 - el) / 60.0)
        pts = []
        steps = 4
        paz, pel = az, el
        for s in range(steps + 1):
            hit, nrm, d = body.head_hit(paz, pel)
            if hit is None:
                break
            base = lift * (1 - 0.35 * s / steps) + 0.0012 * s
            pts.append(hit + (nrm * 0.5 + d * 0.5).normalized() * base)
            step_deg = (L / steps) / 0.09 * 57.3
            paz += step_deg * (flow_az * math.sin(math.radians(90 - el)) * 0.9 + forward_bias * (1 if abs(az) < 80 else 0))
            pel -= step_deg * (0.9 if el > 55 else 1.2)
            paz = max(-170, min(170, paz))
        if len(pts) >= 3:
            rad = [w * (1.0 - 0.6 * (i / (len(pts) - 1)) ** 1.3) for i in range(len(pts))]
            mesh.tube(pts, rad, sides=3, flat=flat, up=None)


def make_hair_short(body):
    m = M("Char2_HairShort", "Hair")
    hair_shell(body, m, thick=0.0055, clump_n=30, clump_amp=0.0038, rings=14, segs=56, seed=3)
    comb_locks(body, m, 60, seed=5, length=(0.03, 0.05), width=(0.0065, 0.0095), az_range=(-105, 105), el_range=(34, 84), lift=0.0040, flat=0.35)
    # front fringe locks lifting off the forehead hairline
    comb_locks(body, m, 14, seed=9, length=(0.02, 0.03), width=(0.0055, 0.0075), az_range=(-52, 52), el_range=(36, 46),
               swirl=0.1, forward_bias=0.25, lift=0.0042, flat=0.35)
    return m.finish(HEAD_PIVOT, (0.20, 0.13, 0.08), "Hair", 0.55)


def long_lock(body, rnd, az, el, length, width, flat=0.5):
    hit, nrm, d = body.head_hit(az, el)
    if hit is None:
        return None
    p = hit + (nrm * 0.5 + d * 0.5).normalized() * 0.006
    # initial direction: away from the crown along the surface
    tang = Vector((math.sin(math.radians(az)), -math.cos(math.radians(az)), 0)) * 0.6 + Vector((0, 0, -0.3))
    dirn = (Vector(d) + Vector((0, 0, -0.9))).normalized() if el < 60 else (tang + d * 0.5).normalized()
    pts = [p]
    step = length / 8.0
    for s in range(8):
        g = Vector((0, 0, -1.0))
        dirn = (dirn * 0.62 + g * 0.55 + Vector((math.sin(s * 0.9 + az) * 0.05, rnd.uniform(-1, 1) * 0.02, 0))).normalized()
        q = p + dirn * step
        # hair swept slightly backward overall (fashion: tucked behind the ears)
        q = body.push_out(q, 0.0085 + 0.0006 * s)
        pts.append(q)
        p = q
    return pts


def make_hair_long(body):
    m = M("Char2_HairLong", "Hair")
    hair_shell(body, m, thick=0.0065, clump_n=22, clump_amp=0.0045, rings=14, segs=56, seed=7)
    rnd = random.Random(11)
    n = 0
    for k in range(150):
        az = rnd.uniform(-175, 175)
        # no long locks over the face
        if abs(az) < 38:
            continue
        el = rnd.uniform(-5, 62) if abs(az) > 70 else rnd.uniform(12, 40)
        pts = long_lock(body, rnd, az, el, rnd.uniform(0.17, 0.30) if abs(az) > 100 else rnd.uniform(0.10, 0.20), 0.011)
        if not pts:
            continue
        rad = [0.0125 * (1.0 - 0.5 * (i / (len(pts) - 1)) ** 1.2) for i in range(len(pts))]
        m.tube(pts, rad, sides=3, flat=0.45)
        n += 1
    comb_locks(body, m, 26, seed=21, length=(0.03, 0.05), width=(0.008, 0.011), az_range=(-100, 100), el_range=(36, 84), lift=0.0045, flat=0.35)
    return m.finish(HEAD_PIVOT, (0.14, 0.09, 0.05), "Hair", 0.5)


# ------------------------------------------------------------------ cap
def make_cap(body):
    m = M("Char2_Cap", "Cap")
    segs, rings = 48, 11
    ca = Vector((0, 0.010, 1.705))
    rx, ry, rz = 0.0905, 0.1105, 0.092

    def rim_el(az):
        # cap rim: sits on the forehead at the front, higher at the back; slightly raised at the sides
        a = abs(az)
        return 12.0 + 22.0 * math.cos(math.radians(min(a, 120.0)) * 0.75) ** 2 * (1.0 if a < 100 else 0.0) + 0.0
    outer, inner = [], []
    for i in range(rings + 1):
        row_o, row_i = [], []
        f = i / rings
        for j in range(segs):
            az = 360.0 * j / segs - 180.0
            el0 = rim_el(az)
            el = 90.0 - f * (90.0 - el0)
            e, a = math.radians(el), math.radians(az)
            d = Vector((math.cos(e) * math.sin(a), -math.cos(e) * math.cos(a), math.sin(e)))
            # panel seams: 6 panels, seam at az = 0, 60, ...: grooves and slight puff
            ph = (az % 60.0) / 60.0
            seam = math.exp(-0.5 * ((min(ph, 1 - ph)) / 0.035) ** 2)
            puff = math.sin(math.pi * ph) * 0.0018 * min(1.0, (1 - f) * 3)
            p = ca + Vector((d.x * rx, d.y * ry, d.z * rz)) + d * (puff - 0.0016 * seam)
            # keep clear of the head
            h = body.ray(ca, d, 0.5)
            if h:
                need = (h[0] - ca).length + 0.0045
                if (p - ca).length < need:
                    p = ca + d * need
            row_o.append(p)
            row_i.append(p - d * 0.0035)
        outer.append(row_o); inner.append(row_i)
    c = sum(outer[0], Vector()) / segs
    outer[0] = [c for _ in range(segs)]
    ci = sum(inner[0], Vector()) / segs
    inner[0] = [ci for _ in range(segs)]
    vo = m.grid(outer, closed_u=True)
    vi = m.grid(inner, closed_u=True, flip=True)
    for j in range(segs):                     # rim thickness
        m.f(vo[-1][j], vi[-1][j], vi[-1][(j + 1) % segs], vo[-1][(j + 1) % segs])
    # button
    top = outer[0][0] + Vector((0, 0, 0.001))
    b = bmesh.ops.create_uvsphere(m.bm, u_segments=12, v_segments=8, radius=0.0085, matrix=Matrix.Translation(top + Vector((0, 0, 0.0)))
                                  @ Matrix.Diagonal((1.0, 1.0, 0.55, 1.0)))
    # brim: curved plate hanging off the front of the rim
    bs, bl = 22, 9
    top_rows, bot_rows = [], []
    for i in range(bl + 1):
        t = i / bl
        row_t, row_b = [], []
        for j in range(bs + 1):
            u = (j / bs - 0.5)                       # -0.5 .. 0.5
            az = u * 150.0                           # brim covers +-75 deg
            el0 = rim_el(az)
            e0, a0 = math.radians(el0), math.radians(az)
            d0 = Vector((math.cos(e0) * math.sin(a0), -math.cos(e0) * math.cos(a0), math.sin(e0)))
            p0 = ca + Vector((d0.x * rx, d0.y * ry, d0.z * rz))
            out = Vector((math.sin(a0) * 0.6, -math.cos(a0), 0)).normalized()
            reach = 0.075 * (1.0 - 0.28 * (2 * abs(u)) ** 2)
            drop = -0.014 * t * t - 0.012 * t * (2 * abs(u)) ** 2 + 0.002
            p = p0 + out * (reach * t) + Vector((0, 0, drop)) + Vector((0, 0, -0.0))
            thick = 0.0030 * (1.0 - 0.45 * t)
            row_t.append(p + Vector((0, 0, thick)))
            row_b.append(p - Vector((0, 0, thick)))
        top_rows.append(row_t); bot_rows.append(row_b)
    vt = m.grid(top_rows, closed_u=False, flip=True)
    vb = m.grid(bot_rows, closed_u=False)
    for j in range(bs):                                      # outer edge, and the two ends
        m.f(vt[-1][j], vt[-1][j + 1], vb[-1][j + 1], vb[-1][j])
    for i in range(bl):
        m.f(vt[i][0], vb[i][0], vb[i + 1][0], vt[i + 1][0])
        m.f(vt[i][bs], vt[i + 1][bs], vb[i + 1][bs], vb[i][bs])
    return m.finish(HEAD_PIVOT, (0.70, 0.16, 0.14), "Cap", 0.75)


# ------------------------------------------------------------------ hood
def make_hood(body):
    m = M("Char2_Hood", "Hood")
    segs, rings = 44, 22
    C = Vector((0, 0.020, 1.690))
    z_bot = 1.455
    outer, inner = [], []
    for i in range(rings + 1):
        f = i / rings
        # face opening half-angle: closed on top, opens to ~64 deg at the brow, narrows to a V on the chest
        half = 2.0 + 62.0 * sstep((f - 0.10) / 0.14) - 28.0 * sstep((f - 0.50) / 0.5)
        row = []
        for j in range(segs + 1):
            az = half + (360.0 - 2 * half) * (j / segs)          # 0 = front, runs round the back
            a = math.radians(az)
            if f < 0.55:
                e = math.radians(90.0 - f / 0.55 * 90.0)
                d = Vector((math.cos(e) * math.sin(a), -math.cos(e) * math.cos(a), math.sin(e)))
                p = C + Vector((d.x * 0.108, d.y * 0.128, d.z * 0.112))
            else:
                t = (f - 0.55) / 0.45
                z = C.z - t * (C.z - z_bot)
                dirv = Vector((math.sin(a), -math.cos(a), 0))
                hit = body.ray(Vector((0, 0.012, z)), dirv, 0.6)
                r_body = (hit[0] - Vector((0, 0.012, z))).length if hit else 0.1
                rr = max(r_body + 0.020 + 0.012 * t, 0.112 - 0.004 * t)
                p = Vector((0, 0.012, z)) + dirv * rr
                if abs(az - 180) < 60:
                    p.y += 0.012 * t
            p = body.push_out(p, 0.012)
            row.append(p)
        outer.append(row)
    c = sum(outer[0], Vector()) / len(outer[0])
    for j in range(len(outer[0])):
        outer[0][j] = c
    vo = m.grid(outer, closed_u=False)
    inn = []
    for row in outer:
        r2 = []
        for p in row:
            n = (p - C); n.z *= 0.3; n.normalize()
            r2.append(p - n * 0.0045)
        inn.append(r2)
    vi = m.grid(inn, closed_u=False, flip=True)
    # rolled edges: along the opening (first/last column) and the bottom hem
    for i in range(rings):
        m.f(vo[i][0], vi[i][0], vi[i + 1][0], vo[i + 1][0])
        m.f(vo[i][-1], vo[i + 1][-1], vi[i + 1][-1], vi[i][-1])
    for j in range(segs):
        m.f(vo[-1][j], vo[-1][j + 1], vi[-1][j + 1], vi[-1][j])
    return m.finish(HEAD_PIVOT, (0.22, 0.24, 0.27), "Hood", 0.9)


# ------------------------------------------------------------------ backpack
def body_ray_back(body, x, z):
    return None


def make_backpack(body):
    m = M("Char2_Backpack", "Bag")
    bm = m.bm
    # pack volume: rounded box against the upper back; back face of the jacket sits near y = +0.13
    # build by a lattice of rings (z) x superellipse (x,y), pushed away from the jacket
    zt, zb = 1.455, 1.040
    n_z, n_c = 16, 28
    exps = 3.2
    rings = []
    for i in range(n_z + 1):
        t = i / n_z
        z = zt + (zb - zt) * t
        # outline widens slightly toward the bottom (soft sag), flatter at the top
        hw = 0.155 + 0.012 * math.sin(math.pi * t) + 0.01 * t
        # depth: bulges mid-height
        hd = 0.088 + 0.030 * math.sin(math.pi * min(1.0, t * 1.05)) * 0.8
        # front (toward -y, against the back) face stays flat
        row = []
        for j in range(n_c):
            a = 2 * math.pi * j / n_c
            c, s = math.cos(a), math.sin(a)
            x = hw * math.copysign(abs(c) ** (2 / exps), c)
            y = hd * math.copysign(abs(s) ** (2 / exps), s)
            row.append(Vector((x, 0.0, z)) + Vector((0, y, 0)))
        rings.append(row)
    # centre depth: back face against the jacket
    for i, row in enumerate(rings):
        z = row[0].z
        hit = body.ray(Vector((0, 0.6, z)), Vector((0, -1, 0)), 0.8)
        yb = hit[0].y if hit else 0.13
        hd = max(abs(p.y) for p in row)
        yc = yb + 0.010 + hd
        for p in row:
            p.y += yc
    # taper the top and bottom edges (rounded)
    for i in (0, n_z):
        for p in rings[i]:
            c0 = sum(rings[i], Vector()) / n_c
            p.x = c0.x + (p.x - c0.x) * 0.80
            p.y = c0.y + (p.y - c0.y) * 0.80
    vs = m.grid(rings, closed_u=True)
    top = [vs[0][j] for j in range(n_c)]
    bot = [vs[-1][j] for j in range(n_c)]
    m.f(*top[::-1]); m.f(*bot)
    # flap seam: a raised ribbon around the pack at 62% height, plus a front pocket outline and zipper line
    for tt, h in ((0.34, 0.0045), (0.62, 0.0035)):
        i = int(tt * n_z)
        ring = [p.copy() for p in rings[i]]
        c0 = sum(ring, Vector()) / n_c
        pts = [c0 + (p - c0) * 1.012 for p in ring]
        pts2 = [c0 + (p - c0) * 1.012 + Vector((0, 0, -0.012)) for p in ring]
        m.grid([pts, pts2], closed_u=True)
    # top handle
    hp = [Vector((-0.045, 0.255, 1.452 + 0.0)), Vector((-0.04, 0.255, 1.488)), Vector((0.0, 0.255, 1.502)), Vector((0.04, 0.255, 1.488)), Vector((0.045, 0.255, 1.452))]
    # shoulder straps: hug the jacket over the trapezius, down the chest, under the arm and back to the pack
    def catmull(pts, n=6):
        out = []
        for i in range(len(pts) - 1):
            p0 = pts[max(i - 1, 0)]; p1 = pts[i]; p2 = pts[i + 1]; p3 = pts[min(i + 2, len(pts) - 1)]
            for k in range(n):
                t = k / n
                out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
        out.append(pts[-1])
        return out
    for sx in (1, -1):
        key = [Vector((sx * 0.085, 0.155, 1.43)), Vector((sx * 0.095, 0.10, 1.49)), Vector((sx * 0.108, 0.03, 1.525)),
               Vector((sx * 0.115, -0.04, 1.50)), Vector((sx * 0.120, -0.100, 1.43)), Vector((sx * 0.122, -0.122, 1.345)),
               Vector((sx * 0.135, -0.108, 1.27)), Vector((sx * 0.170, -0.04, 1.235)), Vector((sx * 0.175, 0.05, 1.225)),
               Vector((sx * 0.150, 0.130, 1.20))]
        path = catmull(key, 5)
        pts, nors = [], []
        for p in path:
            loc, n, idx, dist = body.tbvh.find_nearest(p)
            if loc is None:
                continue
            pts.append(loc + n * 0.0075); nors.append(n)
        rows = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            n = nors[i]
            side = t.cross(n)
            if side.length < 1e-6:
                continue
            side.normalize()
            rows.append([p - side * 0.0235, p - side * 0.0235 + n * 0.0085, p + side * 0.0235 + n * 0.0085, p + side * 0.0235])
        vv = [[m.v(q) for q in r] for r in rows]
        for i in range(len(vv) - 1):
            for k in range(3):
                m.f(vv[i][k], vv[i][k + 1], vv[i + 1][k + 1], vv[i + 1][k])
        for r in (vv[0], vv[-1]):
            m.f(*r)
    # top carry handle
    hp = [Vector((-0.050, 0.236, 1.447)), Vector((-0.042, 0.250, 1.480)), Vector((-0.015, 0.256, 1.498)), Vector((0.015, 0.256, 1.498)),
          Vector((0.042, 0.250, 1.480)), Vector((0.050, 0.236, 1.447))]
    m.tube(catmull(hp, 3), [0.0085] * (len(catmull(hp, 3))), sides=4, flat=0.5, up=Vector((0, 1, 0)))
    # centre zipper line and flap stitch down the back face
    for xo in (-0.060, 0.060):
        zr = []
        for k in range(14):
            z = 1.435 - k * 0.0275
            hit = body_ray_back(body, xo, z)
            if hit is not None:
                zr.append(hit)
        if len(zr) > 2:
            m.tube(zr, [0.0035] * len(zr), sides=4, flat=0.6, up=Vector((1, 0, 0)))
    return m.finish(CHEST_PIVOT, (0.16, 0.44, 0.30), "Bag", 0.85)


PIVOTS = {"Char2_HairShort": HEAD_PIVOT, "Char2_HairLong": HEAD_PIVOT, "Char2_Cap": HEAD_PIVOT,
          "Char2_Hood": HEAD_PIVOT, "Char2_Backpack": CHEST_PIVOT}


def build_all(obj):
    body = Body(obj)
    return [fn(body) for fn in (make_hair_short, make_hair_long, make_cap, make_hood, make_backpack)]


def main():
    t0 = __import__("time").time()
    obj, rig = MH.build_character(bake_textures=False, export=False)
    outs = build_all(obj)
    os.makedirs(OUT, exist_ok=True)
    for ob in outs:
        export_obj(ob)
    print("[acc] %.1fs" % (__import__("time").time() - t0))
    return obj, rig, outs


if __name__ == "__main__":
    main()
