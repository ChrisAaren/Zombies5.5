"""Zombies 5.5: Char2_Backpack_V2, a day-pack fitted to the MPFB_Courier's back.

Imports MPFB_Courier.fbx (the file Unity uses), measures the jacket's back plane and the torso surface, then builds:
  - a rounded-rectangle pack, flat against the back, with fabric creases (anisotropic noise, sag folds near the base);
  - a raised centre zipper with pull tabs, side-seam piping and a top carry handle;
  - a front pocket (a bulge on the outer face) with a zip line, flap seam and piping around it;
  - two shoulder straps draped over the jacket (BVH nearest-surface): up the back, over the trapezius, down the chest with a
    sternum-height adjuster buckle, and under the arm back to the pack's lower corner.
Authored in the character's rest-pose world space (pivot at the origin), so Unity places it with the character root and
parents it to the upper-chest bone, exactly like the cap. Slots: Bag (fabric), BagStrap (webbing and zipper tape).

Run: blender -b -P Tools/Blender/make_backpack.py   (no --factory-startup: not needed, MPFB is not used here)
Output: Assets/Art/Models/Characters/Char2_Backpack_V2.fbx
"""
import math, os
import bpy, bmesh
from mathutils import Vector, noise
from mathutils.bvhtree import BVHTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Art", "Models", "Characters", "MPFB_Courier.fbx")
OUT = os.environ.get("BP_OUT") or os.path.join(ROOT, "Assets", "Art", "Models", "Characters", "Char2_Backpack_V2.fbx")

ZB, ZT = 1.050, 1.455          # pack bottom and top
HW = 0.150                     # half width
DEPTH = 0.135                  # thickness at the fullest
CORNER = 0.032                 # rounded-edge radius at the top and bottom
NV, NU = 46, 56                # rings, samples around
BAG, STRAP = 0, 1


def sgn_pow(v, e):
    return math.copysign(abs(v) ** e, v)


def sstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


class Mesh:
    def __init__(self):
        self.bm = bmesh.new()
        self.slot = BAG

    def v(self, p):
        return self.bm.verts.new(Vector(p))

    def f(self, *vs, slot=None):
        try:
            fc = self.bm.faces.new(vs)
        except ValueError:
            return None
        fc.material_index = self.slot if slot is None else slot
        return fc

    def grid(self, pts, closed_u=False, flip=False, slot=None):
        vs = [[self.v(p) for p in row] for row in pts]
        for i in range(len(vs) - 1):
            n = len(vs[i])
            for j in range(n if closed_u else n - 1):
                a, b = vs[i][j], vs[i][(j + 1) % n]
                c, d = vs[i + 1][(j + 1) % n], vs[i + 1][j]
                q = (a, b, c, d)[::-1] if flip else (a, b, c, d)
                if len(set(q)) == 4:
                    self.f(*q, slot=slot)
        return vs

    def ribbon(self, pts, nors, width, height, slot=None, round_top=True):
        """Raised band along pts, lying on a surface with normals nors; a bevelled 4-point section."""
        rows = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])
            n = nors[i]
            side = t.cross(n)
            if side.length < 1e-9:
                continue
            side.normalize()
            hw = width * 0.5
            rows.append([p - side * hw, p - side * hw * 0.75 + n * height, p + side * hw * 0.75 + n * height, p + side * hw])
        vv = [[self.v(q) for q in r] for r in rows]
        for i in range(len(vv) - 1):
            for k in range(3):
                self.f(vv[i][k], vv[i][k + 1], vv[i + 1][k + 1], vv[i + 1][k], slot=slot)
        for r, flip in ((vv[0], True), (vv[-1], False)):
            self.f(*(r[::-1] if flip else r), slot=slot)

    def box(self, c, ax, ay, az, hx, hy, hz, slot=None):
        """Axis-aligned-in-(ax,ay,az) box centred at c with half sizes."""
        pts = []
        for sx in (-1, 1):
            for sy in (-1, 1):
                for sz in (-1, 1):
                    pts.append(self.v(c + ax * hx * sx + ay * hy * sy + az * hz * sz))
        idx = lambda sx, sy, sz: ((sx > 0) * 4 + (sy > 0) * 2 + (sz > 0))
        for quad in (((-1, -1, -1), (-1, 1, -1), (-1, 1, 1), (-1, -1, 1)), ((1, -1, -1), (1, -1, 1), (1, 1, 1), (1, 1, -1)),
                     ((-1, -1, -1), (-1, -1, 1), (1, -1, 1), (1, -1, -1)), ((-1, 1, -1), (1, 1, -1), (1, 1, 1), (-1, 1, 1)),
                     ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)), ((-1, -1, 1), (-1, 1, 1), (1, 1, 1), (1, -1, 1))):
            self.f(*[pts[idx(*q)] for q in quad], slot=slot)


def load_body():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    cl = [o for o in bpy.context.scene.objects if o.name == "Courier_Clothes"][0]
    cl.data.calc_loop_triangles()
    verts = [cl.matrix_world @ v.co for v in cl.data.vertices]
    tris = [tuple(t.vertices) for t in cl.data.loop_triangles]
    full = BVHTree.FromPolygons(verts, tris)
    torso = [t for t in tris if abs((verts[t[0]].x + verts[t[1]].x + verts[t[2]].x) / 3) < 0.185]
    return full, BVHTree.FromPolygons(verts, torso)


def main():
    full, torso = load_body()

    # ---- back plane: the jacket's most protruding back surface behind the pack's footprint
    yb = -9.0
    for x in (-0.10, -0.05, 0.0, 0.05, 0.10):
        for z in [ZB + 0.06 + 0.04 * i for i in range(9)]:
            h = full.ray_cast(Vector((x, 0.8, z)), Vector((0, -1, 0)), 1.5)
            if h[0] is not None:
                yb = max(yb, h[0].y)
    yb += 0.0015
    print("back plane y =", round(yb, 4))

    def sect(t_z):
        """Half width and depth at height z, with the rounded top and bottom edge."""
        h = min(t_z - ZB, ZT - t_z)
        inset = 0.0
        if h < CORNER:
            inset = CORNER - math.sqrt(max(0.0, CORNER * CORNER - (CORNER - h) ** 2))
        tt = (t_z - ZB) / (ZT - ZB)
        d = DEPTH * (0.80 + 0.26 * (1 - tt) + 0.10 * math.sin(math.pi * tt)) - inset * 1.2
        hw = HW * (1.0 + 0.06 * (1 - tt) - 0.10 * tt) - inset
        return hw, max(0.012, d)

    E = 3.4
    EB = 9.0

    def surf_point(x_frac, sgn_s, z):
        """Point on the pack surface at height z: x as a fraction of the half width, sgn_s > 0 the outer face."""
        hw, d = sect(z)
        x = hw * x_frac
        s = (max(0.0, 1.0 - abs(x_frac) ** E)) ** (1.0 / E)
        return Vector((x, yb + d * 0.5 * (1 + sgn_s * s), z)), hw, d

    m = Mesh()
    # ---- main body lattice
    rings = []
    zs = [ZB + (ZT - ZB) * i / NV for i in range(NV + 1)]
    for z in zs:
        hw, d = sect(z)
        tt = (z - ZB) / (ZT - ZB)
        row = []
        for j in range(NU):
            a = 2 * math.pi * j / NU
            c, s = math.cos(a), math.sin(a)
            x = hw * sgn_pow(c, 2.0 / E)
            if s >= 0:
                y = yb + d * 0.5 * (1 + sgn_pow(s, 2.0 / E))
            else:
                y = yb + d * 0.5 * (1 + sgn_pow(s, 2.0 / EB))      # the body side stays nearly flat
            # fabric: horizontal creases and sag folds, strongest on the outer face and sides, none on the body side
            w = sstep(-0.25, 0.55, s)
            fold = noise.noise(Vector((x * 14.0, z * 5.0, 3.1))) * 0.0030 + noise.noise(Vector((x * 30.0, z * 11.0, 9.7))) * 0.0014
            sag = math.sin((z - ZB) * 52.0 + x * 9.0) * 0.0018 * (1 - tt) ** 1.5
            ridge = -0.0028 * math.exp(-((x) / 0.045) ** 2) * (0.6 + 0.4 * math.sin(z * 31.0))   # pinch along the centre zipper
            y += w * (fold + sag + ridge) * (1.0 if s > 0 else 0.5)
            row.append(Vector((x, y, z)))
        rings.append(row)
    vs = m.grid(rings, closed_u=True, flip=True)
    m.f(*[vs[0][j] for j in range(NU)][::-1], slot=BAG)       # bottom cap
    m.f(*[vs[-1][j] for j in range(NU)], slot=BAG)            # top cap

    # ---- side-seam piping (x extremes) around the pack, and a seam across the top face edge
    for sgn in (-1, 1):
        pts, nors = [], []
        for z in zs[2:-2]:
            p, hw, d = surf_point(sgn * 0.999, 1.0, z)
            p = Vector((sgn * hw, yb + d * 0.5, z))
            pts.append(p); nors.append(Vector((sgn, 0, 0)))
        m.ribbon(pts, nors, 0.011, 0.0042, slot=BAG)

    # ---- centre zipper on the outer face
    zp, zn = [], []
    for z in [ZB + 0.035 + (ZT - ZB - 0.07) * k / 40 for k in range(41)]:
        p, hw, d = surf_point(0.0, 1.0, z)
        zp.append(p + Vector((0, -0.0012, 0))); zn.append(Vector((0, 1, 0)))
    m.ribbon(zp, zn, 0.015, 0.0034, slot=STRAP)
    for zt0 in (ZT - 0.060, ZB + 0.190):
        p, hw, d = surf_point(0.0, 1.0, zt0)
        m.box(p + Vector((0, 0.0075, 0.0)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.0085, 0.0045, 0.012, slot=STRAP)
        m.box(p + Vector((0, 0.0105, -0.026)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.0035, 0.0022, 0.020, slot=STRAP)

    # ---- front pocket: a bulge on the outer face
    PZ0, PZ1, PHW, PPROT = ZB + 0.035, ZB + 0.205, 0.108, 0.040
    npx, npz = 26, 20
    prow = []
    for iz in range(npz + 1):
        zv = PZ0 + (PZ1 - PZ0) * iz / npz
        v = 2 * (zv - PZ0) / (PZ1 - PZ0) - 1
        row = []
        for ix in range(npx + 1):
            u = 2 * ix / npx - 1
            hw, d = sect(zv)
            xf = u * PHW / hw
            base, _, _ = surf_point(xf, 1.0, zv)
            prot = PPROT * (max(0.0, 1 - abs(u) ** 3.2)) ** (1 / 3.2) * (max(0.0, 1 - abs(v) ** 3.2)) ** (1 / 3.2)
            prot *= 1.0 - 0.18 * (v + 1) / 2                       # a little fuller towards the bottom
            crease = noise.noise(Vector((u * 5.0, v * 4.0, 1.7))) * 0.0025 * min(1.0, prot / 0.01)
            row.append(base + Vector((0, prot + crease, 0)))
        prow.append(row)
    pv = m.grid(prow, closed_u=False, flip=True, slot=BAG)
    # pocket flap seam and zip, piping round the border
    for frac, w in ((0.74, 0.012),):
        zv = PZ0 + (PZ1 - PZ0) * frac
        pts = [row for row in prow]
        iz = int(frac * npz)
        line = [prow[iz][ix] + Vector((0, 0.0012, 0)) for ix in range(3, npx - 2)]
        m.ribbon(line, [Vector((0, 1, 0))] * len(line), 0.013, 0.0030, slot=STRAP)
    zt = prow[int(0.74 * npz)][npx // 2 + 4] + Vector((0, 0.0075, 0))
    m.box(zt, Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.008, 0.0042, 0.011, slot=STRAP)
    m.box(zt + Vector((0, 0.0028, -0.022)), Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), 0.0032, 0.002, 0.018, slot=STRAP)
    border = [prow[0][ix] for ix in range(npx + 1)] + [prow[iz][npx] for iz in range(1, npz + 1)] + \
             [prow[npz][ix] for ix in range(npx - 1, -1, -1)] + [prow[iz][0] for iz in range(npz - 1, 0, -1)]
    # piping just inside the border (pocket is welded to the surface there)
    inner = [prow[1][ix] + Vector((0, 0.0016, 0)) for ix in range(1, npx)]
    m.ribbon(inner, [Vector((0, 1, 0))] * len(inner), 0.009, 0.0026, slot=BAG)
    inner = [prow[npz - 1][ix] + Vector((0, 0.0016, 0)) for ix in range(1, npx)]
    m.ribbon(inner, [Vector((0, 1, 0))] * len(inner), 0.009, 0.0026, slot=BAG)
    for ixc in (1, npx - 1):
        inner = [prow[iz][ixc] + Vector((0, 0.0016, 0)) for iz in range(1, npz)]
        m.ribbon(inner, [Vector((0, 1, 0))] * len(inner), 0.009, 0.0026, slot=BAG)

    # ---- top carry handle: a flat webbing loop standing on the top face
    cap_y = yb + sect(ZT)[1] * 0.5
    hp = []
    for k in range(13):
        a = math.pi * k / 12
        hp.append(Vector((-0.052 * math.cos(a), cap_y, ZT - 0.003 + 0.050 * math.sin(a) ** 0.8)))
    hn = [Vector((0, 1, 0))] * len(hp)
    m.ribbon(hp, hn, 0.024, 0.006, slot=STRAP)
    m.ribbon([Vector((-0.055, cap_y, ZT - 0.004)), Vector((-0.055, cap_y, ZT + 0.004))], [Vector((0, 1, 0))] * 2, 0.034, 0.003, slot=STRAP)
    m.ribbon([Vector((0.055, cap_y, ZT - 0.004)), Vector((0.055, cap_y, ZT + 0.004))], [Vector((0, 1, 0))] * 2, 0.034, 0.003, slot=STRAP)

    # ---- shoulder straps, draped on the torso surface
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
        key = [Vector((sx * 0.082, yb + 0.002, ZT - 0.02)), Vector((sx * 0.092, 0.106, 1.470)), Vector((sx * 0.100, 0.060, 1.525)),
               Vector((sx * 0.104, 0.010, 1.565)), Vector((sx * 0.108, -0.050, 1.545)), Vector((sx * 0.112, -0.105, 1.490)),
               Vector((sx * 0.118, -0.140, 1.420)), Vector((sx * 0.124, -0.158, 1.340)), Vector((sx * 0.140, -0.150, 1.270)),
               Vector((sx * 0.172, -0.090, 1.215)), Vector((sx * 0.182, 0.000, 1.190)), Vector((sx * 0.172, 0.075, 1.150)),
               Vector((sx * 0.150, yb + 0.002, ZB + 0.07))]
        path = catmull(key, 5)
        pts, nors = [], []
        for p in path:
            loc, n, idx, dist = torso.find_nearest(p)
            if loc is None:
                continue
            if (loc - p).length > 0.05:        # key point far from the cloth: keep the guess
                loc, n = p, (p - Vector((0, 0, p.z))).normalized()
            front = p.y < 0.04 and p.z > 1.28       # chest and collar: the jacket's lapel folds are lumpy, stand the band off further
            pts.append(loc + n * (0.016 if front else 0.0095)); nors.append(n)
        # smooth the draped path a little so the band does not ripple with the cloth
        for _ in range(10):
            sm = [pts[0]] + [(pts[i - 1] + pts[i] * 2 + pts[i + 1]) * 0.25 for i in range(1, len(pts) - 1)] + [pts[-1]]
            pts = sm
        # the band's two hinges: width 0.046, a padded profile 0.011 high
        m.ribbon(pts, nors, 0.046, 0.011, slot=STRAP)
        # edge stitch lines
        for off in (-0.0155, 0.0155):
            line, ln = [], []
            for i, p in enumerate(pts):
                t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)])
                side = t.cross(nors[i]).normalized()
                line.append(p + side * off + nors[i] * 0.0108); ln.append(nors[i])
            m.ribbon(line, ln, 0.0022, 0.0009, slot=STRAP)
        # sternum-height adjuster buckle (a small slotted box on the chest part of the strap)
        i0 = min(range(len(pts)), key=lambda i: abs(pts[i].z - 1.34) + (0 if pts[i].y < -0.05 else 5))
        n = nors[i0]; t = (pts[min(i0 + 1, len(pts) - 1)] - pts[max(i0 - 1, 0)]).normalized(); side = t.cross(n).normalized()
        m.box(pts[i0] + n * 0.0125, side, t, n, 0.0175, 0.0115, 0.0035, slot=BAG)
        # webbing tail
        m.box(pts[min(i0 + 5, len(pts) - 1)] + n * 0.0128, side, t, n, 0.0155, 0.0170, 0.0014, slot=STRAP)

    # ---- finish
    bm = m.bm
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("Char2_Backpack_V2")
    bm.to_mesh(me)
    uvl = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            p = me.vertices[me.loops[li].vertex_index].co
            u, v = ((p.x, p.z), (p.y, p.z), (p.x, p.y))[0 if ax == 1 else 1 if ax == 0 else 2]
            uvl.data[li].uv = (u / 0.25, v / 0.25)
        poly.use_smooth = True
    bm.free()
    for name in ("Bag", "BagStrap"):
        me.materials.append(bpy.data.materials.new(name))
    # keep sharp seams crisp on the small hardware, smooth the fabric: auto-smooth by angle
    try:
        bpy.ops.object.shade_auto_smooth  # noqa
    except Exception:
        pass
    ob = bpy.data.objects.new("Char2_Backpack_V2", me)
    bpy.context.scene.collection.objects.link(ob)
    for o in list(bpy.context.scene.objects):
        o.select_set(o is ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(
        filepath=OUT, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False, object_types={'MESH'})
    print("backpack exported", OUT, len(me.vertices), "verts", len(me.polygons), "faces")


main()
