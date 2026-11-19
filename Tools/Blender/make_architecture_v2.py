"""Zombies 5.5 - architecture kit v2 (Blender 5.x, headless).
Run:  blender --background --factory-startup --python make_architecture_v2.py [-- stage ...]
Stages (default = all):  tex  pieces  preview  street
  tex      textures (bl_v2_textures.py): BrickRed/Brown/Tan, StoneTrim, MetalShutter, WindowInteriors(+Emission), SideAds
  pieces   builds every V2_* piece, exports FBX to Assets/Art/Models/Architecture/V2/, prints bbox + triangle report
  preview  Eevee renders of each piece -> Tools/Blender/previews/V2_<name>.png
  street   3-building street front -> previews/V2_Street_*.png
Pieces are authored in Unity coordinates (x right, y up, z outward from the facade) and converted to Blender axes
(x, -z, y) at the end, same as v1.  Wall brick UV: u = x + 1.5, v = y (piece origin at the floor line) so bond lines
line up across bays (3 m = 2 brick tiles of 1.5 m) and floors (3.2 and 4.0 m are whole numbers of 0.08 m courses).
Rule used everywhere to avoid z-fighting: trim that sits on the wall starts at z = 0 (never overlaps the wall slab)."""
import math, os, sys
import bpy, bmesh
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FBX_DIR = os.path.join(ROOT, "Assets", "Art", "Models", "Architecture", "V2")
PREV_DIR = os.path.join(HERE, "previews")
os.makedirs(FBX_DIR, exist_ok=True)
os.makedirs(PREV_DIR, exist_ok=True)
import bl_v2_textures as T

H_UP, H_GR = 3.2, 4.0
HW, YB, YT = 0.62, 0.85, 2.55          # window half-width, sill height, head height
WALL_Z = -0.33                         # front wall layer back face (upper bays): reveal 0.25 to the frame front at -0.25
EDGE = [(0, -1.5), (0, 1.5)]


# ------------------------------------------------------------------ piece builder
class Piece:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.bevels = []
        self.uvl = self.bm.loops.layers.uv.new("UVMap")
        self.cu = self.bm.faces.layers.int.new("cu")
        self.sm = self.bm.faces.layers.int.new("sm")

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def _done(self, faces, m, smooth=False):
        faces = list(faces)
        bmesh.ops.recalc_face_normals(self.bm, faces=faces)
        i = self.mi(m)
        for f in faces:
            f.material_index = i
            if smooth:
                f[self.sm] = 1

    def _reg_bevel(self, verts, w, seg, keep):
        if not w:
            return
        edges = {e for v in verts for e in v.link_edges}
        edges = [e for e in edges if not any(all(abs(v.co[a] - val) < 1e-5 for v in e.verts) for a, val in keep)]
        self.bevels.append((edges, w, seg))

    def box(self, x0, x1, y0, y1, z0, z1, m, bev=0.0, seg=1, keep=()):
        if x0 > x1: x0, x1 = x1, x0
        if y0 > y1: y0, y1 = y1, y0
        if z0 > z1: z0, z1 = z1, z0
        vs = bmesh.ops.create_cube(self.bm, size=2.0)["verts"]
        for v in vs:
            v.co = (x1 if v.co.x > 0 else x0, y1 if v.co.y > 0 else y0, z1 if v.co.z > 0 else z0)
        fs = {f for v in vs for f in v.link_faces}
        self._done(fs, m)
        self._reg_bevel(vs, bev, seg, keep)

    def prism_xy(self, pts, z0, z1, m):
        """Polygon in the x/y plane (CCW seen from +z) extruded from z0 (back) to z1 (front)."""
        a = [self.bm.verts.new((x, y, z0)) for x, y in pts]
        b = [self.bm.verts.new((x, y, z1)) for x, y in pts]
        n = len(pts)
        fs = [self.bm.faces.new(b), self.bm.faces.new(a[::-1])]
        for i in range(n):
            j = (i + 1) % n
            fs.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        self._done(fs, m)

    def prism_zy(self, prof, x0, x1, m):
        """Profile (z, y) extruded along x."""
        a = [self.bm.verts.new((x0, y, z)) for z, y in prof]
        b = [self.bm.verts.new((x1, y, z)) for z, y in prof]
        n = len(prof)
        fs = [self.bm.faces.new(a), self.bm.faces.new(b[::-1])]
        for i in range(n):
            j = (i + 1) % n
            fs.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        self._done(fs, m)

    def prism_xz(self, poly, y0, y1, m):
        """Polygon in the x/z plane extruded in y."""
        a = [self.bm.verts.new((x, y0, z)) for x, z in poly]
        b = [self.bm.verts.new((x, y1, z)) for x, z in poly]
        n = len(poly)
        fs = [self.bm.faces.new(a), self.bm.faces.new(b[::-1])]
        for i in range(n):
            j = (i + 1) % n
            fs.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        self._done(fs, m)

    def cyl(self, cx, cz, y0, y1, r0, r1, seg, m, smooth=True):
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg,
                                    radius1=r0, radius2=max(r1, 1e-4), depth=(y1 - y0))
        fs = set()
        for v in res["verts"]:
            x, y, z = v.co
            v.co = (cx + x, y0 + z + (y1 - y0) / 2, cz + y)
            fs.update(v.link_faces)
        self._done(fs, m, smooth)

    def bar(self, p0, p1, w, t, m):
        """Rectangular bar from p0 to p1 (Unity coords) with cross-section w x t (w measured sideways in x/z plane)."""
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        if L < 1e-6:
            return
        z = d / L
        ref = Vector((0, 0, 1)) if abs(z.z) < 0.9 else Vector((1, 0, 0))
        xax = z.cross(ref).normalized() if abs(z.y) < 0.999 else Vector((1, 0, 0))
        if abs(z.y) < 0.999:
            # keep the wide side facing the facade (z) when possible
            xax = Vector((0, 0, 1)).cross(z)
            xax = xax.normalized() if xax.length > 1e-4 else Vector((1, 0, 0))
        yax = z.cross(xax).normalized()
        M = Matrix([[xax.x * w, yax.x * t, z.x * L, 0], [xax.y * w, yax.y * t, z.y * L, 0],
                    [xax.z * w, yax.z * t, z.z * L, 0], [0, 0, 0, 1]])
        M = Matrix.Translation((p0 + p1) / 2) @ M
        res = bmesh.ops.create_cube(self.bm, size=1.0, matrix=M)
        fs = {f for v in res["verts"] for f in v.link_faces}
        self._done(fs, m)

    def quad(self, pts, m, uv=None, custom=False):
        """Single (open) face; pts CCW seen from the outside."""
        f = self.bm.faces.new([self.bm.verts.new(p) for p in pts])
        f.material_index = self.mi(m)
        if uv:
            for l, c in zip(f.loops, uv):
                l[self.uvl].uv = c
            f[self.cu] = 1
        return f

    def card(self, x0, x1, y0, y1, z, rect, m):
        u0, v0, u1, v1 = rect
        self.quad([(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], m, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)])

    def mirror_x(self):
        for v in self.bm.verts:
            v.co.x = -v.co.x
        bmesh.ops.reverse_faces(self.bm, faces=self.bm.faces[:])

    def finish(self):
        bm = self.bm
        for edges, w, seg in self.bevels:
            edges = [e for e in edges if e.is_valid]
            bmesh.ops.bevel(bm, geom=edges, offset=w, offset_type="OFFSET", segments=seg, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        bm.normal_update()
        wall = self.mats.index("Wall") if "Wall" in self.mats else -1
        for f in bm.faces:
            if f[self.cu]:
                continue
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                x, y, z = l.vert.co
                if ax == 2:
                    u, v = x, y
                elif ax == 0:
                    u, v = z, y
                else:
                    u, v = x, z
                if f.material_index == wall:
                    u += 1.5
                l[self.uvl].uv = (u, v)
        for v in bm.verts:
            x, y, z = v.co
            v.co = (x, -z, y)
        flags = [f[self.sm] for f in bm.faces]
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        for m in self.mats:
            me.materials.append(bpy.data.materials.get(m) or make_slot_material(m))
        for pol, fl in zip(me.polygons, flags):
            pol.use_smooth = bool(fl)
        bm.free()
        ob = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


SLOT_COL = {"Wall": (.55, .27, .2), "Trim": (.8, .77, .7), "Frame": (.07, .16, .12), "Glass": (.05, .07, .09),
            "Interior": (.01, .01, .012), "WindowInterior": (.3, .3, .3), "Door": (.2, .1, .08), "Awning": (.7, .2, .16),
            "Roof": (.28, .28, .3), "Metal": (.05, .05, .055), "Concrete": (.55, .55, .53), "Wood": (.5, .33, .18),
            "Shutter": (.4, .45, .42), "SignPanel": (.12, .28, .2), "SideAd": (.7, .7, .6), "Paint": (.5, .1, .1)}


def make_slot_material(name):
    m = bpy.data.materials.new(name)
    c = SLOT_COL.get(name, (.5, .5, .5))
    m.diffuse_color = (*c, 1.0)
    return m


# ------------------------------------------------------------------ wall helpers
def wbox(p, x0, x1, y0, y1, z0, z1, mat="Wall", split=None, low="Trim"):
    """Wall box, optionally switching to a stone base material below y = split."""
    if split is not None and y0 < split < y1:
        p.box(x0, x1, y0, split, z0, z1, low)
        p.box(x0, x1, split, y1, z0, z1, mat)
    elif split is not None and y1 <= split:
        p.box(x0, x1, y0, y1, z0, z1, low)
    else:
        p.box(x0, x1, y0, y1, z0, z1, mat)


def top_fn(yt, rise, hw):
    return (lambda x: yt + rise * (1 - (x / hw) ** 2)) if rise else (lambda x: yt)


def front_wall(p, H, holes, z0=WALL_Z, split=None, mat="Wall"):
    """Front wall layer z[z0, 0] with holes = [(x0, x1, yb, yt, rise)]; arched heads are built from sloped strips."""
    x = -1.5
    for x0, x1, yb, yt, rise in sorted(holes):
        if x0 > x + 1e-6:
            wbox(p, x, x0, 0, H, z0, 0, mat, split)
        if yb > 0:
            wbox(p, x0, x1, 0, yb, z0, 0, mat, split)
        hw = (x1 - x0) / 2
        cx = (x0 + x1) / 2
        f = top_fn(yt, rise, hw)
        n = 1 if not rise else 10
        xs = [x0 + (x1 - x0) * i / n for i in range(n + 1)]
        for a, b in zip(xs[:-1], xs[1:]):
            ya, yb2 = f(a - cx), f(b - cx)
            if rise == 0:
                wbox(p, a, b, yt, H, z0, 0, mat, split)
            else:
                p.prism_xy([(a, ya), (b, yb2), (b, H), (a, H)], z0, 0, mat)
        x = x1
    if x < 1.5 - 1e-6:
        wbox(p, x, 1.5, 0, H, z0, 0, mat, split)


def arc_bar(p, cx, hw, yt, rise, t, z0, z1, m, n=10, inset=0.0):
    """Curved bar following the window head: from top(x)-t to top(x) over x in [cx-hw+inset, cx+hw-inset]."""
    f = top_fn(yt, rise, hw)
    xs = [-hw + inset + (2 * (hw - inset)) * i / n for i in range(n + 1)] if rise else [-hw + inset, hw - inset]
    for a, b in zip(xs[:-1], xs[1:]):
        p.prism_xy([(cx + a, f(a) - t), (cx + b, f(b) - t), (cx + b, f(b)), (cx + a, f(a))], z0, z1, m)


def sash_window(p, cx, hw, yb, yt, rise=0.0, guard=False):
    """Deep-reveal two-over-two double-hung sash: main frame, meeting rail, both sashes with a muntin each,
    one glass pane, optional window-guard bars on the lower sash."""
    t = 0.07
    f = top_fn(yt, rise, hw)
    n = 10 if rise else 1
    z0, z1 = -0.33, -0.25
    # main frame (follows the head)
    p.box(cx - hw, cx - hw + t, yb, yt, z0, z1, "Frame")
    p.box(cx + hw - t, cx + hw, yb, yt, z0, z1, "Frame")
    p.box(cx - hw + t, cx + hw - t, yb, yb + t, z0, z1, "Frame")
    arc_bar(p, cx, hw, yt, rise, t, z0, z1, "Frame", n, inset=t)
    ym = yb + (yt - yb) * 0.5
    ix0, ix1 = cx - hw + t, cx + hw - t
    p.box(ix0, ix1, ym - 0.035, ym + 0.035, z0, z1, "Frame")                       # meeting rail
    s = 0.05
    # lower sash (in front)
    za, zb = -0.285, -0.225
    p.box(ix0, ix0 + s, yb + t, ym - 0.035, za, zb, "Frame")
    p.box(ix1 - s, ix1, yb + t, ym - 0.035, za, zb, "Frame")
    p.box(ix0 + s, ix1 - s, yb + t, yb + t + 0.07, za, zb, "Frame")
    p.box(ix0 + s, ix1 - s, ym - 0.035 - 0.05, ym - 0.035, za, zb, "Frame")
    p.box(cx - 0.0175, cx + 0.0175, yb + t + 0.07, ym - 0.085, za + 0.012, zb - 0.012, "Frame")
    # upper sash (set back)
    za, zb = -0.33, -0.27
    p.box(ix0, ix0 + s, ym + 0.035, f(-hw + t) - t, za, zb, "Frame")
    p.box(ix1 - s, ix1, ym + 0.035, f(hw - t) - t, za, zb, "Frame")
    p.box(ix0 + s, ix1 - s, ym + 0.035, ym + 0.035 + 0.05, za, zb, "Frame")
    arc_bar(p, cx, hw, yt - t - 0.0, rise, s, za, zb, "Frame", n, inset=t + s)
    p.box(cx - 0.0175, cx + 0.0175, ym + 0.085, f(0) - t - s, za + 0.012, zb - 0.012, "Frame")
    # glass (single pane, reflective in Unity) at the middle of the frame depth
    xs = [ix0 + (ix1 - ix0) * i / n for i in range(n + 1)]
    pts = [(ix0, yb + t, -0.295), (ix1, yb + t, -0.295)] + [(x, f(x - cx) - t, -0.295) for x in xs[::-1]]
    p.quad(pts, "Glass")
    if guard:
        zg = -0.2
        yg0, yg1 = yb + 0.09, ym - 0.02
        for k in range(1, 8):
            x = ix0 + (ix1 - ix0) * k / 8
            p.box(x - 0.008, x + 0.008, yg0, yg1, zg - 0.016, zg, "Metal")
        p.box(ix0, ix1, yg0, yg0 + 0.03, zg - 0.02, zg, "Metal")
        p.box(ix0, ix1, yg1 - 0.03, yg1, zg - 0.02, zg, "Metal")
        p.box(ix0 - 0.0, ix0 + 0.03, yg0, yg1, zg - 0.02, zg, "Metal")
        p.box(ix1 - 0.03, ix1, yg0, yg1, zg - 0.02, zg, "Metal")


def window_back(p, cx, hw, yb, yt, rise, cell):
    """Dark liner around the void behind the glass + the interior card 0.15 m behind it."""
    zc = -0.295 - 0.15
    p.box(cx - hw - 0.08, cx - hw, yb - 0.08, yt + rise + 0.08, zc - 0.02, WALL_Z, "Interior")
    p.box(cx + hw, cx + hw + 0.08, yb - 0.08, yt + rise + 0.08, zc - 0.02, WALL_Z, "Interior")
    p.box(cx - hw, cx + hw, yb - 0.08, yb, zc - 0.02, WALL_Z, "Interior")
    p.box(cx - hw, cx + hw, yt + rise, yt + rise + 0.08, zc - 0.02, WALL_Z, "Interior")
    if cell is not None:
        p.card(cx - hw, cx + hw, yb, yt + rise, zc, T.cell_uv(cell), "WindowInterior")
    else:
        p.box(cx - hw, cx + hw, yb, yt + rise, zc - 0.02, zc, "Interior")


def sill(p, cx, hw, yb):
    p.box(cx - hw - 0.14, cx + hw + 0.14, yb - 0.09, yb, 0.0, 0.13, "Trim", 0.012, 1)
    for s in (-1, 1):
        p.box(cx + s * (hw + 0.04) - 0.05, cx + s * (hw + 0.04) + 0.05, yb - 0.22, yb - 0.09, 0.0, 0.08, "Trim")


def lintel_flat(p, cx, hw, yt):
    p.box(cx - hw - 0.13, cx + hw + 0.13, yt, yt + 0.22, 0.0, 0.05, "Trim", 0.01, 1)


def lintel_seg(p, cx, hw, yt, rise):
    f = top_fn(yt, rise, hw)
    n = 10
    xs = [-hw + 2 * hw * i / n for i in range(n + 1)]
    for i, (a, b) in enumerate(zip(xs[:-1], xs[1:])):
        zf = 0.032 if i % 2 == 0 else 0.016
        p.prism_xy([(cx + a, f(a) + 0.0), (cx + b, f(b)), (cx + b, f(b) + 0.22), (cx + a, f(a) + 0.22)], 0.0, zf, "Trim")
    for s in (-1, 1):
        p.box(cx + s * (hw + 0.08) - 0.08, cx + s * (hw + 0.08) + 0.08, yt - 0.06, yt + 0.28, 0.0, 0.06, "Trim", 0.008, 1)
    # keystone
    p.box(cx - 0.07, cx + 0.07, yt + rise - 0.03, yt + rise + 0.30, 0.0, 0.07, "Trim", 0.008, 1)


def lintel_key(p, cx, hw, yt):
    a = [cx + (-hw + 2 * hw * i / 5) for i in range(6)]
    b = [cx + (-hw + 2 * hw * i / 5) * 1.22 for i in range(6)]
    for i in range(5):
        top = yt + 0.22
        if i == 2:
            p.prism_xy([(a[2] + 0.0, yt - 0.05), (a[3], yt - 0.05), (b[3] - 0.03, top + 0.1), (b[2] + 0.03, top + 0.1)], 0.0, 0.08, "Trim")
        else:
            p.prism_xy([(a[i], yt), (a[i + 1], yt), (b[i + 1], top), (b[i], top)], 0.0, 0.045, "Trim")


def lintel_hood(p, cx, hw, yt):
    """Pedimented hood (real-world reference style): consoles, a cornice slab and a fan-shaped plaque."""
    p.box(cx - hw - 0.12, cx + hw + 0.12, yt, yt + 0.12, 0.0, 0.06, "Trim", 0.008, 1)
    for s in (-1, 1):
        p.box(cx + s * (hw + 0.08) - 0.06, cx + s * (hw + 0.08) + 0.06, yt - 0.25, yt + 0.12, 0.0, 0.10, "Trim", 0.006, 1)
    p.prism_zy([(0.0, yt + 0.12), (0.12, yt + 0.12), (0.14, yt + 0.16), (0.14, yt + 0.2), (0.17, yt + 0.24),
                (0.17, yt + 0.30), (0.0, yt + 0.30)], cx - hw - 0.2, cx + hw + 0.2, "Trim")
    # fan plaque with stepped louvres
    for k in range(4):
        w = 0.12 + 0.07 * k
        p.box(cx - w, cx + w, yt + 0.12 + 0.03 * k, yt + 0.15 + 0.03 * k, 0.0, 0.07 - 0.004 * k, "Trim")
    p.box(cx - 0.035, cx + 0.035, yt + 0.12, yt + 0.27, 0.0, 0.09, "Trim")


WINDOW_VARIANTS = [   # (suffix, lintel, guard)  one per atlas cell
    ("Flat_BlindsHalf", "flat", False), ("Seg_CurtainsRed", "seg", False), ("Key_CurtainsBlue", "key", False),
    ("Hood_CurtainsCream", "hood", False), ("Flat_DarkFurniture", "flat", True), ("Seg_ACUnit", "seg", False),
    ("Hood_PlantGuard", "hood", True), ("Key_LampLit", "key", False), ("Flat_EmptyDark", "flat", False),
    ("Seg_BlindsClosedGuard", "seg", True), ("Hood_TVRoom", "hood", False), ("Key_GreenPlantGuard", "key", True),
]


def build_window(i):
    suffix, lin, guard = WINDOW_VARIANTS[i]
    p = Piece("V2_Window_" + suffix)
    rise = 0.12 if lin == "seg" else 0.0
    front_wall(p, H_UP, [(-HW, HW, YB, YT, rise)])
    sash_window(p, 0, HW, YB, YT, rise, guard)
    window_back(p, 0, HW, YB, YT, rise, i)
    sill(p, 0, HW, YB)
    {"flat": lambda: lintel_flat(p, 0, HW, YT), "seg": lambda: lintel_seg(p, 0, HW, YT, rise),
     "key": lambda: lintel_key(p, 0, HW, YT), "hood": lambda: lintel_hood(p, 0, HW, YT)}[lin]()
    return p


def build_juliet():
    p = Piece("V2_Facade_Juliet")
    hw, yb, yt = 0.56, 0.28, 2.6
    front_wall(p, H_UP, [(-hw, hw, yb, yt, 0.0)])
    sash_window(p, 0, hw, yb, yt, 0.0, False)
    window_back(p, 0, hw, yb, yt, 0.0, 1)
    lintel_flat(p, 0, hw, yt)
    # balcony floor slab with brackets, iron guard rail with a swelled front
    p.box(-hw - 0.2, hw + 0.2, 0.12, 0.26, 0.0, 0.40, "Trim", 0.01, 1)
    for s in (-1, 1):
        p.box(s * 0.45 - 0.05, s * 0.45 + 0.05, -0.1, 0.12, 0.0, 0.30, "Trim")
    xs = [-hw - 0.14 + (2 * hw + 0.28) * k / 10 for k in range(11)]
    zf = lambda x: 0.04 + 0.30 * max(0.0, 1 - (x / (hw + 0.14)) ** 4)
    pts = [(x, zf(x)) for x in xs]
    for (xa, za), (xb, zb) in zip(pts[:-1], pts[1:]):
        p.bar((xa, 1.08, za), (xb, 1.08, zb), 0.035, 0.035, "Metal")
        p.bar((xa, 0.34, za), (xb, 0.34, zb), 0.025, 0.025, "Metal")
        p.bar((xa, 0.72, za), (xb, 0.72, zb), 0.02, 0.02, "Metal")
    for k in range(18):
        x = -hw - 0.14 + (2 * hw + 0.28) * (k + 0.5) / 18
        p.box(x - 0.01, x + 0.01, 0.34, 1.08, zf(x) - 0.01, zf(x) + 0.01, "Metal")
    for s in (-1, 1):
        p.box(s * (hw + 0.14) - 0.02, s * (hw + 0.14) + 0.02, 0.26, 1.1, 0.02, 0.07, "Metal")
        p.bar((s * (hw + 0.14), 1.08, 0.04), (s * (hw + 0.14), 1.08, 0.4), 0.03, 0.03, "Metal")
    return p


def build_beltcourse():
    p = Piece("V2_Facade_BeltCourse")
    wbox(p, -1.5, 1.5, 0, H_UP, WALL_Z, 0)
    p.box(-1.5, 1.5, 2.64, 2.72, 0.0, 0.075, "Trim", 0.006, 1, EDGE)
    p.box(-1.5, 1.5, 2.72, 2.96, 0.0, 0.03, "Wall")
    p.box(-1.5, 1.5, 2.96, 3.20, 0.0, 0.06, "Wall")
    return p


def build_bricked_up():
    p = Piece("V2_Facade_BrickedUp")
    front_wall(p, H_UP, [(-HW, HW, YB, YT, 0.0)])
    p.box(-HW, HW, YB, YT, WALL_Z, -0.05, "Wall")                 # infill, 5 cm proud of the old reveal
    sill(p, 0, HW, YB)
    lintel_flat(p, 0, HW, YT)
    p.box(-HW, HW, YB + 0.5, YB + 0.53, -0.05, -0.04, "Trim")      # rusty tie plate stand-in
    return p


def build_side(name, ad=None, h=H_UP):
    p = Piece(name)
    wbox(p, -1.5, 1.5, 0, h, WALL_Z, 0)
    if ad is not None:
        p.card(-1.25, 1.25, 0.6, 2.58, 0.015, T.ad_uv(ad), "SideAd")
    return p


def build_quoin(name, h, left):
    p = Piece(name)
    s = 1 if left else -1
    n = int(round(h / 0.4))
    for k in range(n):
        y0, y1 = k * 0.4 + 0.01, (k + 1) * 0.4
        if k % 2 == 0:
            xa, xb = sorted((-0.22 * s, 0.44 * s))
            p.box(xa, xb, y0, y1, -0.22, 0.05, "Trim", 0.012, 1)
        else:
            xa, xb = sorted((-0.22 * s, 0.05 * s))
            p.box(xa, xb, y0, y1, -0.44, 0.05, "Trim", 0.012, 1)
    p.box(-0.24, 0.24, 0.0, 0.01, -0.24, 0.24, "Trim") if False else None
    return p


def pipe(p, x, z, y0, y1, r, seg=8, bands=()):
    p.cyl(x, z, y0, y1, r, r, seg, "Metal")
    for yb in bands:
        p.box(x - r - 0.012, x + r + 0.012, yb - 0.02, yb + 0.02, z - r - 0.012, 0.0, "Metal")


def build_downspout(name, h, foot):
    p = Piece(name)
    z = 0.065
    ys = [0.4 + 0.8 * k for k in range(int(h / 0.8))]
    if foot:
        p.cyl(0, z, 0.55, h, 0.065, 0.065, 8, "Metal")
        p.bar((0, 0.55, z), (0, 0.38, z + 0.22), 0.13, 0.13, "Metal")
        p.bar((0, 0.38, z + 0.22), (0, 0.22, z + 0.32), 0.13, 0.13, "Metal")
        p.box(-0.075, 0.075, 0.18, 0.22, z + 0.28, z + 0.45, "Metal")
    else:
        p.cyl(0, z, 0.0, h, 0.065, 0.065, 8, "Metal")
    for yb in ys:
        if yb > 0.5 or not foot:
            p.box(-0.08, 0.08, yb - 0.025, yb + 0.025, 0.0, 0.14, "Metal")
    return p


def build_conduit():
    p = Piece("V2_Conduit_3_2")
    x = -0.5
    p.cyl(x, 0.03, 0.0, H_UP, 0.022, 0.022, 6, "Metal")
    for k in range(6):
        p.box(x - 0.04, x + 0.04, 0.25 + 0.5 * k - 0.012, 0.25 + 0.5 * k + 0.012, 0.0, 0.058, "Metal")
    p.box(x - 0.12, x + 0.12, 1.5, 1.74, 0.0, 0.10, "Metal", 0.005, 1)             # junction box
    p.box(x - 0.1, x + 0.1, 1.52, 1.72, 0.1, 0.112, "Metal")
    p.bar((x, 1.74, 0.03), (x + 0.6, 1.9, 0.03), 0.04, 0.04, "Metal")
    return p


# ------------------------------------------------------------------ ground floor
def shop_frame(p, x0, x1, y0, y1, t=0.07, z0=-0.30, z1=-0.22, vm=(), hb=(), glass_z=-0.26, back=True):
    p.box(x0, x1, y0, y0 + t, z0, z1, "Frame")
    p.box(x0, x1, y1 - t, y1, z0, z1, "Frame")
    p.box(x0, x0 + t, y0 + t, y1 - t, z0, z1, "Frame")
    p.box(x1 - t, x1, y0 + t, y1 - t, z0, z1, "Frame")
    for c in vm:
        p.box(c - 0.025, c + 0.025, y0 + t, y1 - t, z0, z1, "Frame")
    for yc in hb:
        p.box(x0 + t, x1 - t, yc - 0.025, yc + 0.025, z0, z1, "Frame")
    p.quad([(x0 + t, y0 + t, glass_z), (x1 - t, y0 + t, glass_z), (x1 - t, y1 - t, glass_z), (x0 + t, y1 - t, glass_z)], "Glass")
    if back:
        p.box(x0 + t, x1 - t, y0 + t, y1 - t, z0, z0 + 0.02, "Interior")


def cap_profile(p, x0, x1, y):
    p.prism_zy([(0.0, y), (0.10, y), (0.12, y + 0.03), (0.12, y + 0.07), (0.15, y + 0.10), (0.15, y + 0.16), (0.0, y + 0.16)], x0, x1, "Trim")


def sign_band(p):
    p.box(-1.5, 1.5, 3.2, 3.92, 0.0, 0.07, "SignPanel")
    p.box(-1.5, 1.5, 3.2, 3.26, 0.07, 0.10, "Trim", 0.004, 1, EDGE)
    p.box(-1.5, 1.5, 3.86, 3.92, 0.07, 0.10, "Trim", 0.004, 1, EDGE)
    p.box(-1.5, 1.5, 3.92, 4.0, 0.0, 0.12, "Trim", 0.006, 1, EDGE)


def build_stoop():
    p = Piece("V2_Facade_Stoop")
    z0 = -0.30
    front_wall(p, H_GR, [(-0.55, 0.55, 1.0, 3.55, 0.0), (-1.34, -0.98, 1.3, 3.2, 0.0), (0.98, 1.34, 1.3, 3.2, 0.0)], z0, split=0.9)
    p.box(-1.5, 1.5, 0.9, 0.97, 0.0, 0.08, "Trim", 0.008, 1, EDGE)                    # water table
    # recessed door with transom
    p.box(-0.55, -0.49, 1.0, 3.55, -0.30, -0.22, "Frame"); p.box(0.49, 0.55, 1.0, 3.55, -0.30, -0.22, "Frame")
    p.box(-0.49, 0.49, 3.49, 3.55, -0.30, -0.22, "Frame"); p.box(-0.49, 0.49, 3.2, 3.26, -0.30, -0.22, "Frame")
    for sx in (-1, 1):
        p.box(sx * 0.41 - 0.08, sx * 0.41 + 0.08, 1.0, 3.2, -0.27, -0.20, "Door")
    p.box(-0.33, 0.33, 1.0, 1.22, -0.27, -0.20, "Door"); p.box(-0.33, 0.33, 1.95, 2.05, -0.27, -0.20, "Door")
    p.box(-0.33, 0.33, 3.08, 3.2, -0.27, -0.20, "Door")
    for sx in (-1, 1):
        p.box(sx * 0.16 - 0.14, sx * 0.16 + 0.14, 1.28, 1.9, -0.20, -0.185, "Door", 0.006, 1)
    p.box(-0.33, 0.33, 2.05, 3.08, -0.30, -0.28, "Interior")
    p.quad([(-0.33, 2.05, -0.235), (0.33, 2.05, -0.235), (0.33, 3.08, -0.235), (-0.33, 3.08, -0.235)], "Glass")
    p.box(-0.49, 0.49, 3.26, 3.49, -0.30, -0.28, "Interior")
    p.quad([(-0.49, 3.26, -0.25), (0.49, 3.26, -0.25), (0.49, 3.49, -0.25), (-0.49, 3.49, -0.25)], "Glass")
    p.box(0.36, 0.40, 1.95, 2.08, -0.20, -0.15, "Metal")
    # stone surround, entablature, hood
    for sx in (-1, 1):
        p.box(sx * 0.69 - 0.09, sx * 0.69 + 0.09, 1.0, 3.55, 0.0, 0.07, "Trim", 0.008, 1)
    p.box(-0.9, 0.9, 3.55, 3.80, 0.0, 0.07, "Trim", 0.008, 1)
    cap_profile(p, -0.96, 0.96, 3.80)
    p.box(-0.1, 0.1, 3.55, 3.80, 0.07, 0.09, "Trim")
    # parlor side windows + barred basement windows on the stone base
    for sx in (-1, 1):
        cx = sx * 1.16
        shop_frame(p, cx - 0.18, cx + 0.18, 1.3, 3.2, 0.06, hb=(2.25,))
        p.box(cx - 0.28, cx + 0.28, 1.21, 1.3, 0.0, 0.12, "Trim", 0.01, 1)
        p.box(cx - 0.26, cx + 0.26, 3.2, 3.38, 0.0, 0.05, "Trim", 0.01, 1)
        p.box(cx - 0.18, cx + 0.18, 0.28, 0.72, 0.0, 0.015, "Interior")
        for k in range(1, 5):
            xx = cx - 0.18 + 0.36 * k / 5
            p.box(xx - 0.008, xx + 0.008, 0.28, 0.72, 0.015, 0.035, "Metal")
        p.box(cx - 0.2, cx + 0.2, 0.72, 0.8, 0.0, 0.05, "Trim", 0.006, 1)
    # steps: 4 treads + landing = 5 stone steps, nosing line projecting to z = +1.6
    for k, ytop in enumerate([0.2, 0.4, 0.6, 0.8]):
        za, zb = 1.32 - 0.28 * k, 1.6 - 0.28 * k
        p.box(-0.8, 0.8, 0.0, ytop, za, zb, "Trim", 0.01, 1, [(1, 0.0)])
    p.box(-0.8, 0.8, 0.0, 1.0, 0.0, 0.48, "Trim", 0.01, 1, [(1, 0.0)])
    # cheek walls with sloped coping, newel piers, iron handrails + balusters on both sides
    ycheek = lambda z: 1.25 if z <= 0.48 else 0.45 + (1.6 - z) * 0.714
    rh = 0.65
    for sx in (-1, 1):
        xa, xb = sorted((sx * 0.8, sx * 0.95))
        p.prism_zy([(0.0, 0.0), (1.6, 0.0), (1.6, 0.45), (0.48, 1.25), (0.0, 1.25)], xa, xb, "Trim")
        x = sx * 0.875
        p.box(x - 0.09, x + 0.09, 0.45, 0.78, 1.5, 1.62, "Trim", 0.012, 1)
        p.bar((x, 0.45 + rh + 0.05, 1.57), (x, 1.25 + rh, 0.48), 0.035, 0.035, "Metal")
        p.bar((x, 1.25 + rh, 0.48), (x, 1.25 + rh, 0.06), 0.035, 0.035, "Metal")
        z = 1.40
        while z > 0.1:
            yc = ycheek(z)
            p.box(x - 0.011, x + 0.011, yc, yc + rh + 0.02 - (0.0 if z > 0.48 else 0.0), z - 0.011, z + 0.011, "Metal")
            z -= 0.14
        p.box(x - 0.025, x + 0.025, 0.45, 0.45 + rh + 0.08, 1.5, 1.56, "Metal")
        p.box(x - 0.025, x + 0.025, 1.25, 1.25 + rh + 0.02, 0.04, 0.10, "Metal")
    return p


def build_shuttershop(name, half):
    p = Piece(name)
    z0 = -0.30
    ya, yb = 0.6, 3.1
    front_wall(p, H_GR, [(-1.3, 1.3, ya, yb, 0.0)], z0, split=0.6)
    p.box(-1.5, 1.5, 0.56, 0.62, 0.0, 0.09, "Trim", 0.006, 1, EDGE)                  # bulkhead cap
    for k in range(1, 12):                                                           # tile joints
        p.box(-1.5 + 0.25 * k - 0.004, -1.5 + 0.25 * k + 0.004, 0.04, 0.54, 0.0, 0.006, "Interior")
    for yy in (0.17, 0.30, 0.43):
        p.box(-1.5, 1.5, yy - 0.004, yy + 0.004, 0.0, 0.006, "Interior")
    shop_frame(p, -1.3, 1.3, ya, 2.9, 0.08, vm=(-0.45, 0.45), hb=(2.35,), glass_z=-0.25, back=True)
    p.box(-1.3, 1.3, 2.9, 3.1, -0.22, -0.02, "Metal", 0.01, 1)                       # roller housing
    for sx in (-1, 1):
        p.box(sx * 1.25 - 0.03, sx * 1.25 + 0.03, ya, 2.9, -0.17, -0.06, "Metal")  # guide rails
    ytop = 2.9
    ybot = 1.55 if half else 0.68
    p.box(-1.22, 1.22, ybot, ytop, -0.15, -0.11, "Shutter")
    p.box(-1.24, 1.24, ybot - 0.07, ybot, -0.18, -0.09, "Metal", 0.006, 1)           # bottom bar
    for sx in (-1, 1):
        p.box(sx * 0.9 - 0.04, sx * 0.9 + 0.04, ybot - 0.045, ybot - 0.02, -0.09, -0.04, "Metal")
    p.box(-0.04, 0.04, ybot - 0.09, ybot - 0.05, -0.09, -0.06, "Metal")
    sign_band(p)
    return p


def build_shopdoorway():
    p = Piece("V2_Facade_ShopDoorway")
    z0 = -0.30
    front_wall(p, H_GR, [(-1.4, -0.78, 0.6, 3.0, 0.0), (-0.65, 0.65, 0.0, 3.0, 0.0), (0.78, 1.4, 0.6, 3.0, 0.0)], z0, split=0.6)
    for sx in (-1, 1):
        cx = sx * 1.09
        shop_frame(p, cx - 0.31, cx + 0.31, 0.6, 3.0, 0.07, hb=(2.4,), glass_z=-0.12, z0=-0.20, z1=-0.10)
    p.box(-1.5, 1.5, 0.56, 0.62, 0.0, 0.09, "Trim", 0.006, 1, EDGE)
    # recessed entry: splayed display wings, glazed door with transom, threshold
    for sx in (-1, 1):
        xa, xb = (-0.65, -0.46) if sx < 0 else (0.46, 0.65)
        za, zb = (-0.04, -0.28) if sx < 0 else (-0.28, -0.04)
        p.quad([(xa, 0.6, za), (xb, 0.6, zb), (xb, 3.0, zb), (xa, 3.0, za)], "Glass")
        p.prism_xz([(xa, za - 0.03), (xb, zb - 0.03), (xb, zb), (xa, za)], 0.0, 0.6, "Trim")
        p.prism_xz([(xa, za - 0.04), (xb, zb - 0.04), (xb, zb - 0.02), (xa, za - 0.02)], 0.6, 3.0, "Interior")
    p.box(-0.46, 0.46, 0.0, 0.06, -0.30, 0.0, "Trim")
    p.box(-0.46, -0.40, 0.06, 3.0, -0.30, -0.22, "Frame"); p.box(0.40, 0.46, 0.06, 3.0, -0.30, -0.22, "Frame")
    p.box(-0.40, 0.40, 2.94, 3.0, -0.30, -0.22, "Frame"); p.box(-0.40, 0.40, 2.45, 2.51, -0.30, -0.22, "Frame")
    for sx in (-1, 1):
        p.box(sx * 0.36 - 0.04, sx * 0.36 + 0.04, 0.06, 2.45, -0.27, -0.20, "Door")
    p.box(-0.32, 0.32, 0.06, 0.40, -0.27, -0.20, "Door"); p.box(-0.32, 0.32, 2.3, 2.45, -0.27, -0.20, "Door")
    p.box(-0.32, 0.32, 0.40, 2.3, -0.30, -0.28, "Interior")
    p.quad([(-0.32, 0.40, -0.24), (0.32, 0.40, -0.24), (0.32, 2.3, -0.24), (-0.32, 2.3, -0.24)], "Glass")
    p.box(-0.40, 0.40, 2.51, 2.94, -0.30, -0.28, "Interior")
    p.quad([(-0.40, 2.51, -0.25), (0.40, 2.51, -0.25), (0.40, 2.94, -0.25), (-0.40, 2.94, -0.25)], "Glass")
    p.box(0.25, 0.29, 0.95, 1.4, -0.20, -0.14, "Metal")
    p.box(-0.5, 0.5, 0.0, 0.1, 0.0, 0.45, "Concrete", 0.01, 1)
    sign_band(p)
    return p


def build_servicedoor():
    p = Piece("V2_Facade_ServiceDoor")
    front_wall(p, H_GR, [(-0.55, 0.55, 0.0, 2.45, 0.0)], -0.30, split=0.5)
    p.box(-1.5, 1.5, 0.46, 0.52, 0.0, 0.07, "Trim", 0.006, 1, EDGE)
    p.box(-0.55, -0.47, 0.0, 2.45, -0.30, -0.20, "Metal"); p.box(0.47, 0.55, 0.0, 2.45, -0.30, -0.20, "Metal")
    p.box(-0.47, 0.47, 2.37, 2.45, -0.30, -0.20, "Metal")
    p.box(-0.47, 0.47, 0.0, 2.37, -0.27, -0.23, "Door")
    p.box(-0.42, 0.42, 0.06, 0.34, -0.23, -0.215, "Metal")
    for k in range(7):
        p.box(-0.3, 0.3, 1.85 + k * 0.05, 1.85 + k * 0.05 + 0.02, -0.23, -0.20, "Metal")
    p.box(-0.3, 0.3, 0.98, 1.02, -0.19, -0.17, "Metal")
    p.box(-0.3, -0.26, 0.95, 1.05, -0.23, -0.17, "Metal"); p.box(0.26, 0.3, 0.95, 1.05, -0.23, -0.17, "Metal")
    p.box(0.33, 0.43, 1.35, 1.42, -0.23, -0.2, "Metal")
    p.box(-0.9, -0.64, 1.3, 1.5, 0.0, 0.015, "Metal")
    p.box(-0.64, 0.64, 2.45, 2.65, 0.0, 0.06, "Trim", 0.01, 1)
    p.box(-0.5, 0.5, 0.0, 0.1, 0.0, 0.55, "Concrete", 0.01, 1)
    p.box(0.7, 0.9, 2.5, 2.7, 0.0, 0.1, "Metal", 0.006, 1)
    p.box(0.72, 0.88, 2.52, 2.68, 0.1, 0.13, "Metal")
    return p


# ------------------------------------------------------------------ roofline
BR_PROFILE = [(-0.30, 0.50), (0.40, 0.50), (0.40, 0.60), (0.50, 0.70), (0.50, 0.80), (0.44, 0.80), (0.44, 0.86), (-0.30, 0.86)]


def build_cornice_bracketed():
    p = Piece("V2_Cornice_Bracketed")
    p.box(-1.5, 1.5, 0.0, 0.5, -0.30, 0.08, "Trim", 0.006, 1, EDGE)                    # frieze
    p.box(-1.5, 1.5, 0.0, 0.07, 0.08, 0.13, "Trim", 0.005, 1, EDGE)                    # bead
    p.prism_zy(BR_PROFILE, -1.5, 1.5, "Trim")                                          # crown + fascia
    for k in range(6):                                                                  # modillion brackets every 0.5 m
        x = -1.25 + 0.5 * k
        p.prism_zy([(0.08, 0.1), (0.2, 0.1), (0.36, 0.44), (0.36, 0.5), (0.08, 0.5)], x - 0.07, x + 0.07, "Trim")
        p.box(x - 0.05, x + 0.05, 0.06, 0.1, 0.13, 0.2, "Trim")
    return p


def build_cornice_bracketed_corner(left):
    p = Piece("V2_Cornice_Bracketed_Corner_" + ("L" if left else "R"))
    e = 0.005
    ib = -0.30
    prof = [(0.08, 0.0), (0.13, 0.0), (0.13, 0.07), (0.08, 0.07), (0.08, 0.5), (0.40, 0.5), (0.40, 0.6), (0.5, 0.7), (0.5, 0.8), (0.44, 0.8), (0.44, 0.86)]
    prof = [(o + e, y + (-e if i == 0 else (e if i == len(prof) - 1 else 0))) for i, (o, y) in enumerate(prof)]
    cache = {}
    def vert(x, y, z):
        k = (round(x, 5), round(y, 5), round(z, 5))
        if k not in cache:
            cache[k] = p.bm.verts.new((x, y, z))
        return cache[k]
    def ring(o, y):
        return [vert(ib, y, ib), vert(o, y, ib), vert(o, y, o), vert(ib, y, o)]
    rs = [ring(o, y) for o, y in prof]
    faces = []
    def face(vs):
        vs2 = []
        for v in vs:
            if v not in vs2:
                vs2.append(v)
        if len(vs2) < 3 or p.bm.faces.get(vs2):
            return
        pts = [v.co for v in vs2]
        if ((pts[1] - pts[0]).cross(pts[2] - pts[0])).length < 1e-9:
            return
        faces.append(p.bm.faces.new(vs2))
    for r0, r1 in zip(rs[:-1], rs[1:]):
        for i in range(4):
            face([r0[i], r0[(i + 1) % 4], r1[(i + 1) % 4], r1[i]])
    face(rs[0]); face(rs[-1])
    p._done(faces, "Trim")
    p.prism_xz([(0.0, 0.12), (0.28, 0.40), (0.40, 0.28), (0.12, 0.0), (0.06, 0.06)], 0.1, 0.5, "Trim")   # diagonal corner bracket
    if left:
        p.mirror_x()
    return p


def build_cornice_corbel():
    p = Piece("V2_Cornice_Corbel")
    p.box(-1.5, 1.5, 0.0, 0.48, -0.30, 0.0, "Wall")
    for a, b, z in [(0.00, 0.08, 0.04), (0.08, 0.16, 0.08), (0.16, 0.24, 0.12), (0.24, 0.32, 0.16)]:
        p.box(-1.5, 1.5, a, b, 0.0, z, "Wall")
    for k in range(12):                                                                # dentils
        x = -1.5 + 0.125 + 0.25 * k
        p.box(x - 0.065, x + 0.065, 0.32, 0.40, 0.0, 0.17, "Wall")
    p.box(-1.5, 1.5, 0.40, 0.48, 0.0, 0.19, "Wall")
    p.box(-1.5, 1.5, 0.48, 0.56, -0.34, 0.24, "Trim", 0.008, 1, EDGE)                  # stone cap
    return p


def build_parapet():
    p = Piece("V2_Parapet_Coped")
    p.box(-1.5, 1.5, 0.0, 0.80, -0.30, 0.0, "Wall")
    p.box(-1.38, 1.38, 0.14, 0.66, 0.0, 0.03, "Wall")
    p.box(-1.5, 1.5, 0.80, 0.90, -0.36, 0.07, "Trim", 0.01, 1, EDGE)                  # stone coping
    p.box(-1.5, 1.5, 0.0, 0.08, 0.0, 0.05, "Trim", 0.005, 1, EDGE)
    return p


def build_chimney(name, twin):
    p = Piece(name)
    hx = 0.8 if twin else 0.45
    hz = 0.3
    p.box(-hx, hx, 0.0, 2.0, -hz, hz, "Wall")
    p.box(-hx - 0.04, hx + 0.04, 2.0, 2.08, -hz - 0.04, hz + 0.04, "Wall")
    p.box(-hx - 0.08, hx + 0.08, 2.08, 2.16, -hz - 0.08, hz + 0.08, "Wall")
    p.box(-hx - 0.12, hx + 0.12, 2.16, 2.26, -hz - 0.12, hz + 0.12, "Trim", 0.008, 1)
    for cx in ((-0.42, 0.42) if twin else (0.0,)):
        p.cyl(cx, 0.0, 2.26, 2.62, 0.17, 0.15, 10, "Metal")
        p.cyl(cx, 0.0, 2.62, 2.66, 0.19, 0.19, 10, "Metal")
    p.box(-hx - 0.14, hx + 0.14, 0.0, 0.14, -hz - 0.14, hz + 0.14, "Roof")
    return p


def build_bulkhead():
    p = Piece("V2_Roof_Bulkhead")
    p.box(-1.2, 1.2, 0.0, 2.4, -1.1, 0.85, "Wall")
    wbox(p, -1.2, -0.5, 0, 2.4, 0.85, 1.1)
    wbox(p, 0.5, 1.2, 0, 2.4, 0.85, 1.1)
    wbox(p, -0.5, 0.5, 2.15, 2.4, 0.85, 1.1)
    p.box(-0.5, 0.5, 0.0, 2.15, 0.85, 0.89, "Interior")
    p.box(-0.5, -0.44, 0.0, 2.15, 0.89, 0.99, "Metal"); p.box(0.44, 0.5, 0.0, 2.15, 0.89, 0.99, "Metal")
    p.box(-0.44, 0.44, 2.09, 2.15, 0.89, 0.99, "Metal")
    p.box(-0.44, 0.44, 0.0, 2.09, 0.91, 0.97, "Door")
    for k in range(6):
        p.box(-0.3, 0.3, 1.45 + k * 0.06, 1.45 + k * 0.06 + 0.03, 0.97, 1.0, "Metal")
    p.box(0.32, 0.4, 1.0, 1.08, 0.97, 1.05, "Metal")
    p.box(-0.7, 0.7, 0.0, 0.12, 1.1, 1.6, "Concrete", 0.01, 1)
    p.box(-0.6, 0.6, 2.4, 2.46, 1.1, 1.14, "Trim", 0.006, 1)
    p.prism_zy([(-1.15, 2.4), (1.15, 2.4), (1.15, 2.52), (-1.15, 2.64)], -1.28, 1.28, "Roof")
    p.box(-1.28, 1.28, 2.34, 2.4, -1.15, 1.15, "Trim")
    p.box(0.7, 0.9, 2.2, 2.38, 1.1, 1.2, "Metal", 0.005, 1)
    return p


def build_watertower():
    p = Piece("V2_Roof_WaterTower")
    L = 1.2
    ytop = 3.4
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box(sx * L - 0.07, sx * L + 0.07, 0.0, ytop, sz * L - 0.07, sz * L + 0.07, "Metal")
    for y in (0.0, 1.5, ytop - 0.12):
        for sx in (-1, 1):
            p.box(sx * L - 0.05, sx * L + 0.05, y + 0.06, y + 0.16, -L, L, "Metal")
        for sz in (-1, 1):
            p.box(-L, L, y + 0.06, y + 0.16, sz * L - 0.05, sz * L + 0.05, "Metal")
    for (ya, yb) in ((0.2, 1.5), (1.6, ytop - 0.12)):
        for sx in (-1, 1):
            p.bar((sx * L, ya, -L), (sx * L, yb, L), 0.05, 0.05, "Metal")
            p.bar((sx * L, ya, L), (sx * L, yb, -L), 0.05, 0.05, "Metal")
        for sz in (-1, 1):
            p.bar((-L, ya, sz * L), (L, yb, sz * L), 0.05, 0.05, "Metal")
            p.bar((L, ya, sz * L), (-L, yb, sz * L), 0.05, 0.05, "Metal")
    p.box(-1.5, 1.5, ytop, ytop + 0.1, -1.5, 1.5, "Metal", 0.01, 1)
    base = ytop + 0.1
    p.cyl(0, 0, base + 0.02, base + 2.75, 1.3, 1.3, 24, "Wood")
    for k in range(7):
        y = base + 0.18 + k * 0.4
        p.cyl(0, 0, y, y + 0.06, 1.335, 1.335, 24, "Metal")
    p.cyl(0, 0, base + 2.75, base + 3.95, 1.45, 0.06, 24, "Roof", smooth=False)
    p.cyl(0, 0, base + 3.95, base + 4.25, 0.05, 0.05, 8, "Metal")
    p.cyl(0, 0, 0.25, base + 0.02, 0.11, 0.11, 10, "Metal")
    p.box(-0.18, 0.18, 1.1, 1.3, -0.18, 0.18, "Metal", 0.01, 1)
    for sx in (-1, 1):
        p.box(sx * 0.28 - 0.025, sx * 0.28 + 0.025, 0.0, base + 1.9, 1.34, 1.40, "Metal")
    for k in range(int((base + 1.8) / 0.3)):
        p.box(-0.28, 0.28, 0.2 + 0.3 * k - 0.015, 0.2 + 0.3 * k + 0.015, 1.34, 1.40, "Metal")
    return p


def build_awning():
    p = Piece("V2_Awning")
    prof = [(0, 0), (0.9, -0.38), (0.9, -0.58), (0.86, -0.58), (0.86, -0.405), (0, -0.04)]
    p.prism_zy(prof, -1.4, 1.4, "Awning")
    for k in range(1, 7):
        x = -1.4 + 2.8 * k / 7
        p.prism_zy([(0.01, -0.01), (0.91, -0.37), (0.91, -0.385), (0.01, -0.025)], x - 0.012, x + 0.012, "Awning")
    for x in (-1.3, 1.3):
        p.prism_zy([(0.02, -0.12), (0.86, -0.42), (0.86, -0.45), (0.02, -0.15)], x - 0.015, x + 0.015, "Metal")
        p.box(x - 0.025, x + 0.025, -0.20, -0.10, -0.03, 0.02, "Metal", 0.004, 1)
    return p


# ------------------------------------------------------------------ fire escapes (zig-zag)
def rail_x(p, x0, x1, z, y0, h, pitch=0.15):
    p.box(x0, x1, y0 + h - 0.03, y0 + h, z - 0.02, z + 0.02, "Metal")
    p.box(x0, x1, y0 + 0.45, y0 + 0.475, z - 0.012, z + 0.012, "Metal")
    n = max(2, int((x1 - x0) / pitch))
    for k in range(n + 1):
        x = x0 + (x1 - x0) * k / n
        p.box(x - 0.011, x + 0.011, y0, y0 + h - 0.03, z - 0.011, z + 0.011, "Metal")


def rail_z(p, x, z0, z1, y0, h, pitch=0.16):
    p.box(x - 0.02, x + 0.02, y0 + h - 0.03, y0 + h, z0, z1, "Metal")
    p.box(x - 0.012, x + 0.012, y0 + 0.45, y0 + 0.475, z0, z1, "Metal")
    n = max(2, int((z1 - z0) / pitch))
    for k in range(n + 1):
        z = z0 + (z1 - z0) * k / n
        p.box(x - 0.011, x + 0.011, y0, y0 + h - 0.03, z - 0.011, z + 0.011, "Metal")


def deck(p, full=False):
    zb, zf = 0.12, 1.0
    for sz in (zb, zf - 0.05):
        p.box(-1.45, 1.45, 0.06, 0.18, sz, sz + 0.05, "Metal")
    for sx in (-1.45, 1.40):
        p.box(sx, sx + 0.05, 0.06, 0.18, zb, zf, "Metal")
    for k in range(16):
        x = -1.38 + 2.76 * k / 15
        p.box(x - 0.015, x + 0.015, 0.14, 0.18, zb + 0.05, zf - 0.05, "Metal")
    for x in (-1.4, 0.0, 1.4):                                                        # diagonal brackets
        p.bar((x, -0.5, 0.0), (x, 0.08, zf - 0.03), 0.04, 0.05, "Metal")
        p.box(x - 0.025, x + 0.025, -0.5, 0.1, 0.0, 0.03, "Metal")
    xa, xb = (-0.95, 0.95) if not full else (-1.43, 1.43)
    rail_x(p, xa, xb, zf - 0.02, 0.18, 0.95)
    for sx in (-1, 1):
        rail_z(p, sx * 1.43, zb + 0.02, zf - 0.02, 0.18, 0.95)


def build_zig(name, left_start):
    """One floor segment: deck + one full-width flight up to the next floor's deck.  A climbs toward +x, B toward -x.
    Stack A, B, A, B...  The flight arrives exactly at the next piece's deck end, so the zig-zag lines up."""
    p = Piece(name)
    deck(p)
    s = 1 if left_start else -1
    x0, x1 = -1.2 * s, 1.2 * s
    y0, y1 = 0.18, 3.38
    zs0, zs1 = 1.0, 1.6
    n = 17
    for k in range(n):
        xa = x0 + (x1 - x0) * k / n
        xb = x0 + (x1 - x0) * (k + 1) / n
        yt = y0 + (y1 - y0) * (k + 1) / n
        p.box(min(xa, xb), max(xa, xb), yt - 0.035, yt, zs0 + 0.02, zs1 - 0.02, "Metal")
    for z in (zs0 + 0.025, zs1 - 0.025):
        p.bar((x0, y0 - 0.12, z), (x1, y1 - 0.12, z), 0.05, 0.14, "Metal")
        p.bar((x0, y0 + 0.88, z), (x1, y1 + 0.88, z), 0.035, 0.035, "Metal")
        for k in range(0, n + 1, 2):
            xk = x0 + (x1 - x0) * k / n
            yk = y0 + (y1 - y0) * k / n
            p.box(xk - 0.011, xk + 0.011, yk, yk + 0.88, z - 0.011, z + 0.011, "Metal")
    return p


def build_fe_ladder():
    p = Piece("V2_FireEscape_DropLadder")
    for sx in (-1, 1):
        p.box(sx * 0.2 - 0.02, sx * 0.2 + 0.02, -3.6, 0.12, 0.97, 1.03, "Metal")
    for k in range(12):
        y = -3.5 + 0.3 * k
        p.box(-0.2, 0.2, y - 0.012, y + 0.012, 0.97, 1.03, "Metal")
    return p


def build_fe_top():
    p = Piece("V2_FireEscape_Top")
    deck(p, full=True)
    return p


# ------------------------------------------------------------------ registry + export
def all_builders():
    b = []
    for i in range(len(WINDOW_VARIANTS)):
        b.append(lambda i=i: build_window(i))
    b += [build_juliet, build_beltcourse, build_bricked_up,
          lambda: build_side("V2_Side_Blank"), lambda: build_side("V2_Side_Blank_Ground", None, H_GR),
          lambda: build_side("V2_Side_Ad_Sterling", 0), lambda: build_side("V2_Side_Ad_Kestrel", 1),
          lambda: build_side("V2_Side_Ad_Meridian", 2), lambda: build_side("V2_Side_Ad_Arden", 3),
          lambda: build_quoin("V2_Corner_Quoin_L", 3.2, True), lambda: build_quoin("V2_Corner_Quoin_R", 3.2, False),
          lambda: build_quoin("V2_Corner_Quoin_Ground_L", 4.0, True), lambda: build_quoin("V2_Corner_Quoin_Ground_R", 4.0, False),
          lambda: build_downspout("V2_Pipe_Downspout", 3.2, False), lambda: build_downspout("V2_Pipe_Downspout_Foot", 4.0, True),
          build_conduit,
          build_stoop, lambda: build_shuttershop("V2_Facade_ShutterShop_Half", True),
          lambda: build_shuttershop("V2_Facade_ShutterShop_Down", False), build_shopdoorway, build_servicedoor,
          build_cornice_bracketed, lambda: build_cornice_bracketed_corner(True), lambda: build_cornice_bracketed_corner(False), build_cornice_corbel, build_parapet,
          lambda: build_chimney("V2_Roof_Chimney_A", False), lambda: build_chimney("V2_Roof_Chimney_B", True),
          build_bulkhead, build_watertower, build_awning,
          lambda: build_zig("V2_FireEscape_ZigA", True), lambda: build_zig("V2_FireEscape_ZigB", False),
          build_fe_ladder, build_fe_top]
    return b


def export_fbx(ob, name):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=os.path.join(FBX_DIR, name + ".fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types={'MESH', 'EMPTY'})


def tri_count(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


def build_all(export=True):
    objs = {}
    report = []
    for b in all_builders():
        p = b()
        ob = p.finish()
        vs = ob.data.vertices
        mn = Vector((min(v.co.x for v in vs), min(v.co.y for v in vs), min(v.co.z for v in vs)))
        mx = Vector((max(v.co.x for v in vs), max(v.co.y for v in vs), max(v.co.z for v in vs)))
        ext = ((mn.x, mx.x), (mn.z, mx.z), (-mx.y, -mn.y))      # Unity axes: x, y = Blender z, z = -Blender y
        t = tri_count(ob)
        report.append((ob.name, ext, t, list(p.mats)))
        objs[ob.name] = ob
        print("PIECE %-34s x[%.2f %.2f] y[%.2f %.2f] z[%.2f %.2f] tris=%d slots=%s" % (
            ob.name, *ext[0], *ext[1], *ext[2], t, ",".join(p.mats)))
        if export:
            export_fbx(ob, ob.name)
    return objs, report


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stages = [a for a in args if not a.startswith("V2_")] or ["tex", "pieces", "preview", "street"]
    if "tex" in stages:
        T.make_all()
    if set(stages) & {"pieces", "preview", "street"}:
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        objs, report = build_all(export="pieces" in stages)
        if set(stages) & {"preview", "street"}:
            import bl_v2_render as R
            if "preview" in stages:
                R.render_pieces(objs, PREV_DIR, only=[a for a in args if a.startswith("V2_")] or None)
            if "street" in stages:
                R.render_street(objs, PREV_DIR)


main()
