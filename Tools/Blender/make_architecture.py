"""Zombies 5.5 - architecture kit builder (Blender 5.x, headless).
Run: blender --background --factory-startup --python make_architecture.py

Meshes are authored in *Unity-style coordinates* (x right, y up, z outward from the facade) and converted
to Blender axes (x, -z, y) just before the mesh is created, so facades face Blender -Y = Unity +Z as the
contract requires. Every piece is a set of closed solids (boxes / extruded profiles) that do not share
coplanar visible faces; seam edges (x = +-1.5, floor planes) are never bevelled so pieces tile.
Outputs: FBX into Assets/Art/Models/Architecture, previews into Tools/Blender/previews, bbox report.
"""
import math
import os
import bpy
import bmesh
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FBX_DIR = os.path.join(ROOT, "Assets", "Art", "Models", "Architecture")
PREV_DIR = os.path.join(HERE, "previews")
os.makedirs(FBX_DIR, exist_ok=True)
os.makedirs(PREV_DIR, exist_ok=True)

BAY = 3.0
H_UP, H_GROUND = 3.2, 4.0
WALL_T = 0.3          # wall body depth (z = -0.3 .. 0)
RECESS = 0.15         # window / shop recess depth

# ------------------------------------------------------------------ scene + materials
def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for d in list(coll):
            coll.remove(d)

# slot name -> plausible viewport colour (Unity re-tints from its Palette)
MAT_COLORS = {
    "Wall": (0.55, 0.27, 0.20), "Trim": (0.80, 0.77, 0.70), "Frame": (0.13, 0.22, 0.20),
    "Glass": (0.18, 0.30, 0.40), "Interior": (0.015, 0.015, 0.02), "Door": (0.35, 0.12, 0.10),
    "Awning": (0.72, 0.18, 0.16), "Roof": (0.28, 0.28, 0.30), "Metal": (0.16, 0.17, 0.19),
    "Concrete": (0.55, 0.55, 0.53), "Wood": (0.50, 0.33, 0.18),
}
MATS = {}

def make_materials():
    for name, c in MAT_COLORS.items():
        m = bpy.data.materials.new(name)
        m.diffuse_color = (*c, 1.0)
        MATS[name] = m


# ------------------------------------------------------------------ piece builder
class Piece:
    """Collects solids in Unity coords, then bevels, UVs, converts axes and builds the mesh object."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.bevels = []                      # (edges, width, segments)

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def _register_bevel(self, verts, width, seg, keep):
        if not width:
            return
        edges = {e for v in verts for e in v.link_edges}
        def on_plane(e):
            return any(all(abs(v.co[ax] - val) < 1e-5 for v in e.verts) for ax, val in keep)
        edges = [e for e in edges if not on_plane(e)]
        self.bevels.append((edges, width, seg))

    def box(self, x0, x1, y0, y1, z0, z1, mat, bev=0.0, seg=1, keep=()):
        """Axis-aligned box. keep = [(axis, value)] planes whose edges stay sharp (tiling seams)."""
        verts = bmesh.ops.create_cube(self.bm, size=2.0)["verts"]
        for v in verts:
            v.co = (x1 if v.co.x > 0 else x0, y1 if v.co.y > 0 else y0, z1 if v.co.z > 0 else z0)
        idx = self.mi(mat)
        for v in verts:
            for f in v.link_faces:
                f.material_index = idx
        self._register_bevel(verts, bev, seg, keep)

    def prism(self, prof, x0, x1, mat, bev=0.0, seg=1, keep=()):
        """Extrude a (z, y) profile polygon along x from x0 to x1."""
        a = [self.bm.verts.new((x0, y, z)) for z, y in prof]
        b = [self.bm.verts.new((x1, y, z)) for z, y in prof]
        faces = [self.bm.faces.new(a), self.bm.faces.new(b)]
        n = len(prof)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        idx = self.mi(mat)
        for f in faces:
            f.material_index = idx
        self._register_bevel(a + b, bev, seg, keep)

    def cyl(self, cx, cz, y0, y1, r0, r1, seg, mat):
        """Vertical cylinder / cone frustum (r0 at y0, r1 at y1)."""
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg,
                                    radius1=r0, radius2=r1, depth=(y1 - y0))
        idx = self.mi(mat)
        for v in res["verts"]:
            x, y, z = v.co            # cone axis is Z; swap into Y-up, rotate so a seam face is flat
            v.co = (cx + x, y0 + (z + (y1 - y0) / 2), cz + y)
            for f in v.link_faces:
                f.material_index = idx

    def finish(self):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for edges, w, seg in self.bevels:
            edges = [e for e in edges if e.is_valid]
            bmesh.ops.bevel(bm, geom=edges, offset=w, offset_type="OFFSET", segments=seg,
                            profile=0.5, affect="EDGES", clamp_overlap=True)
        bm.normal_update()
        # world-meter box-projection UVs (1 UV unit = 1 m), computed in Unity coords
        uv = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                x, y, z = l.vert.co
                l[uv].uv = (x, y) if ax == 2 else ((z, y) if ax == 0 else (x, z))
        for v in bm.verts:                     # Unity (x,y,z) -> Blender (x,-z,y), a pure rotation
            x, y, z = v.co
            v.co = (x, -z, y)
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for m in self.mats:
            me.materials.append(MATS[m])
        for p in me.polygons:
            p.use_smooth = False
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# ------------------------------------------------------------------ facade helpers
BAY_KEEP = [(0, -1.5), (0, 1.5)]


def wall_with_holes(p, h, holes):
    """Wall slab z[-0.3,0] with rectangular through-holes; dark Interior panel fills the recess."""
    x = -BAY / 2
    for x0, x1, y0, y1 in sorted(holes):
        if x0 > x:
            p.box(x, x0, 0, h, -WALL_T, 0, "Wall")
        if y0 > 0:
            p.box(x0, x1, 0, y0, -WALL_T, 0, "Wall")
        if y1 < h:
            p.box(x0, x1, y1, h, -WALL_T, 0, "Wall")
        p.box(x0, x1, y0, y1, -WALL_T, -RECESS, "Interior")
        x = x1
    if x < BAY / 2:
        p.box(x, BAY / 2, 0, h, -WALL_T, 0, "Wall")


def frame_grid(p, x0, x1, y0, y1, t=0.06, z0=-0.13, z1=-0.03, vm=(), hb=(), mt=0.045, zg=-0.085):
    """Window frame: perimeter bars, vertical mullions (vm = x centres), horizontal bars (hb = y centres),
    and one glass slab sitting inside the frame depth."""
    bev = 0.0  # thin bars: no bevel (keeps tri count in budget)
    p.box(x0, x1, y0, y0 + t, z0, z1, "Frame", bev)
    p.box(x0, x1, y1 - t, y1, z0, z1, "Frame", bev)
    verts = [(x0, x0 + t)] + [(c - mt / 2, c + mt / 2) for c in vm] + [(x1 - t, x1)]
    for a, b in verts:
        p.box(a, b, y0 + t, y1 - t, z0, z1, "Frame", bev)
    for yc in hb:
        for (_, a), (b, _) in zip(verts[:-1], verts[1:]):
            p.box(a, b, yc - mt / 2, yc + mt / 2, z0, z1, "Frame", bev)
    p.box(x0 + t, x1 - t, y0 + t, y1 - t, zg - 0.005, zg + 0.005, "Glass")


def window_unit(p, cx, w, ya, yb, mullion):
    """One recessed window centred at cx: returns the wall hole; adds frame, glass, sill and lintel."""
    x0, x1 = cx - w / 2, cx + w / 2
    frame_grid(p, x0, x1, ya, yb, vm=[cx] if mullion else [], hb=[yb - 0.65])
    p.box(x0 - 0.1, x1 + 0.1, ya - 0.10, ya, -RECESS, 0.12, "Trim", 0.02, 2)           # stone sill
    p.box(x0 - 0.12, x1 + 0.12, yb, yb + 0.20, -RECESS, 0.08, "Trim", 0.02, 2)        # lintel
    return (x0, x1, ya - 0.02, yb + 0.02)


def build_window():
    p = Piece("Facade_Window")
    hole = window_unit(p, 0, 1.3, 0.87, 2.63, True)
    wall_with_holes(p, H_UP, [hole])
    return p


def build_window_pair():
    p = Piece("Facade_WindowPair")
    holes = [window_unit(p, c, 0.8, 0.90, 2.60, False) for c in (-0.72, 0.72)]
    wall_with_holes(p, H_UP, holes)
    return p


def build_blank():
    p = Piece("Facade_Blank")
    wall_with_holes(p, H_UP, [])
    p.box(-1.5, 1.5, 0.77, 0.87, -0.05, 0.03, "Trim", 0.01, 1, BAY_KEEP)             # subtle string course
    return p


def ledge(p, y0):
    """Full-width stone ledge above the shopfront; continues across neighbouring bays."""
    p.box(-1.5, 1.5, y0, y0 + 0.20, -RECESS, 0.06, "Trim", 0.015, 2, BAY_KEEP)


def build_storefront():
    p = Piece("Facade_Storefront")
    ya, yb = 0.64, 2.88
    x0, x1 = -1.3, 1.3
    wall_with_holes(p, H_GROUND, [(x0, x1, ya - 0.04, yb + 0.02)])
    frame_grid(p, x0, x1, ya, yb, t=0.08, z0=-0.14, z1=-0.05, vm=[-0.45, 0.45], hb=[yb - 0.5], mt=0.06, zg=-0.10)
    p.box(x0 - 0.05, x1 + 0.05, 0.56, ya, -RECESS, 0.05, "Trim", 0.015, 2)             # bulkhead cap
    ledge(p, yb)
    p.box(-1.5, 1.5, 0.28, 0.34, -0.05, 0.02, "Trim", 0.008, 1, BAY_KEEP)             # base moulding
    return p


def build_entrance():
    p = Piece("Facade_Entrance")
    x0, x1 = -0.55, 0.55
    ya, yb = 0.15, 2.88
    wall_with_holes(p, H_GROUND, [(x0, x1, ya, yb + 0.02)])
    frame_grid(p, x0, x1, ya, yb, t=0.06, z0=-0.14, z1=-0.05, hb=[2.31], mt=0.06, zg=-0.10)
    # door leaf with glazed upper panel (dark Interior behind the glass)
    zl0, zl1 = -0.22, -0.14
    lx0, lx1 = x0 + 0.06, x1 - 0.06
    p.box(lx0, lx0 + 0.16, ya, 2.28, zl0, zl1, "Door", 0.006)
    p.box(lx1 - 0.16, lx1, ya, 2.28, zl0, zl1, "Door", 0.006)
    p.box(lx0 + 0.16, lx1 - 0.16, ya, 1.15, zl0, zl1, "Door", 0.006)
    p.box(lx0 + 0.16, lx1 - 0.16, 2.10, 2.28, zl0, zl1, "Door", 0.006)
    p.box(lx0 + 0.16, lx1 - 0.16, 1.15, 2.10, -0.20, -0.19, "Interior")
    p.box(lx0 + 0.16, lx1 - 0.16, 1.15, 2.10, -0.17, -0.16, "Glass")
    p.box(-0.26, 0.26, 0.30, 1.00, zl1, zl1 + 0.02, "Door", 0.008)                    # raised kick panel
    p.box(0.31, 0.35, 0.95, 1.40, zl1, zl1 + 0.07, "Metal", 0.006)                     # pull handle
    p.box(-1.0, 1.0, 0.0, 0.15, 0.0, 0.2, "Concrete", 0.015, 2)                       # entrance step
    ledge(p, yb)
    p.box(-1.5, 1.5, 0.28, 0.34, -0.05, 0.02, "Trim", 0.008, 1, BAY_KEEP)
    return p


def build_pilaster(name, h, ground):
    p = Piece(name)
    keep = [(1, 0.0), (1, h)]
    p.box(-0.2, 0.2, 0, h, -0.2, 0.2, "Trim", 0.015, 2, keep)
    if ground:
        p.box(-0.23, 0.23, 0, 0.30, -0.23, 0.23, "Trim", 0.015, 2, [(1, 0.0)])         # plinth
        p.box(-0.22, 0.22, 2.88, 3.08, -0.22, 0.22, "Trim", 0.012, 1)                  # matches shopfront ledge
    else:
        p.box(-0.215, 0.215, 0.77, 0.87, -0.215, 0.215, "Trim", 0.01, 1)               # matches string course
    return p


CORNICE_PROFILE = [(-0.3, 0), (0.05, 0), (0.05, 0.10), (0.12, 0.10), (0.12, 0.24), (0.35, 0.40),
                   (0.35, 0.52), (0.30, 0.52), (0.30, 0.60), (-0.3, 0.60)]       # (z, y)


def build_cornice():
    p = Piece("Cornice")
    p.prism(CORNICE_PROFILE, -1.5, 1.5, "Trim", 0.01, 1, BAY_KEEP)
    return p


def build_cornice_corner():
    """Mitred outer corner: a square-ring loft of the cornice's outward profile; pivot = building corner.
    Inflated 5 mm so it never z-fights with the overlapping end of the adjacent Cornice segment."""
    p = Piece("Cornice_Corner")
    e = 0.005
    ib = -0.35
    rings = [(0.05, 0.0), (0.05, 0.10), (0.12, 0.10), (0.12, 0.24), (0.35, 0.40), (0.35, 0.52), (0.30, 0.52), (0.30, 0.60)]
    rings = [(o + e, y + (-e if i == 0 else (e if i == len(rings) - 1 else 0))) for i, (o, y) in enumerate(rings)]
    cache = {}
    def vert(x, y, z):
        k = (round(x, 5), round(y, 5), round(z, 5))
        if k not in cache:
            cache[k] = p.bm.verts.new((x, y, z))
        return cache[k]
    def ring(o, y):
        return [vert(ib, y, ib), vert(o, y, ib), vert(o, y, o), vert(ib, y, o)]
    def face(vs):
        vs2 = []
        for v in vs:
            if v not in vs2:
                vs2.append(v)
        if len(vs2) < 3:
            return
        pts = [v.co for v in vs2]
        area = ((pts[1] - pts[0]).cross(pts[2] - pts[0])).length
        if area < 1e-9 or p.bm.faces.get(vs2):
            return
        f = p.bm.faces.new(vs2)
        f.material_index = p.mi("Trim")
    rs = [ring(o, y) for o, y in rings]
    for r0, r1 in zip(rs[:-1], rs[1:]):
        for i in range(4):
            j = (i + 1) % 4
            face([r0[i], r0[j], r1[j], r1[i]])
    face(rs[0])
    face(rs[-1])
    return p


def build_awning():
    p = Piece("Awning")
    prof = [(0, 0), (0.9, -0.38), (0.9, -0.58), (0.86, -0.58), (0.86, -0.405), (0, -0.04)]
    p.prism(prof, -1.4, 1.4, "Awning")
    arm = [(0.02, -0.12), (0.86, -0.42), (0.86, -0.45), (0.02, -0.15)]
    for x in (-1.3, 1.3):
        p.prism(arm, x - 0.015, x + 0.015, "Metal")
        p.box(x - 0.025, x + 0.025, -0.20, -0.10, -0.03, 0.02, "Metal", 0.004)          # wall bracket
    return p


# ------------------------------------------------------------------ rooftop units
def build_ac():
    p = Piece("Roof_ACUnit")
    for x in (-0.5, 0.5):
        p.box(x - 0.05, x + 0.05, 0, 0.12, -0.45, 0.45, "Metal", 0.006)               # skid rails
    p.box(-0.6, 0.6, 0.12, 0.90, -0.45, 0.45, "Metal", 0.02, 2)                         # housing
    for i in range(7):                                                                   # front louvres
        y = 0.22 + i * 0.085
        p.box(-0.5, 0.5, y, y + 0.035, 0.45, 0.48, "Metal", 0.005)
    p.cyl(0, 0, 0.90, 0.96, 0.35, 0.35, 16, "Metal")                                    # fan shroud
    p.cyl(0, 0, 0.90, 0.965, 0.30, 0.30, 16, "Interior")                                # dark fan well
    p.box(-0.30, 0.30, 0.97, 0.985, -0.025, 0.025, "Metal")
    p.box(-0.025, 0.025, 0.97, 0.985, -0.30, 0.30, "Metal")
    p.box(0.62, 0.70, 0.30, 0.62, -0.15, 0.15, "Metal", 0.008)                          # electrical box
    return p


def build_water_tank():
    p = Piece("Roof_WaterTank")
    for i in range(6):
        a = i * math.pi / 3
        x, z = math.cos(a) * 1.0, math.sin(a) * 1.0
        p.box(x - 0.06, x + 0.06, 0, 1.4, z - 0.06, z + 0.06, "Metal", 0.005)           # steel legs
    p.cyl(0, 0, 1.4, 1.5, 1.25, 1.25, 16, "Metal")                                      # platform ring
    p.cyl(0, 0, 1.5, 3.9, 1.2, 1.2, 16, "Wood")                                         # staved barrel
    for y in (1.8, 2.6, 3.4):
        p.cyl(0, 0, y, y + 0.07, 1.235, 1.235, 16, "Metal")                             # steel hoops
    p.cyl(0, 0, 3.9, 4.6, 1.25, 0.08, 16, "Wood")                                       # conical lid
    return p


def build_vent():
    p = Piece("Roof_Vent")
    p.box(-0.3, 0.3, 0, 0.18, -0.3, 0.3, "Concrete", 0.015, 2)                          # curb
    p.cyl(0, 0, 0.18, 0.80, 0.12, 0.12, 12, "Metal")                                    # pipe
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.box(sx * 0.2 - 0.015, sx * 0.2 + 0.015, 0.78, 0.92, sz * 0.2 - 0.015, sz * 0.2 + 0.015, "Metal")
    p.cyl(0, 0, 0.92, 0.98, 0.32, 0.32, 16, "Metal")                                    # rain cap
    p.cyl(0, 0, 0.98, 1.08, 0.32, 0.05, 16, "Metal")
    return p


# ------------------------------------------------------------------ export, stats, verification
def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                             apply_unit_scale=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             object_types={"MESH", "EMPTY"})


def tri_count(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


# expected Unity-space bounds: name -> ((xmin,xmax),(ymin,ymax),(zmin,zmax)); None = free / check max only
EXPECT = {
    "Facade_Window": ((-1.5, 1.5), (0, 3.2), (-0.3, None)),
    "Facade_WindowPair": ((-1.5, 1.5), (0, 3.2), (-0.3, None)),
    "Facade_Blank": ((-1.5, 1.5), (0, 3.2), (-0.3, None)),
    "Facade_Storefront": ((-1.5, 1.5), (0, 4.0), (-0.3, None)),
    "Facade_Entrance": ((-1.5, 1.5), (0, 4.0), (-0.3, None)),
    "Corner_Pilaster": ((-0.215, 0.215), (0, 3.2), (-0.215, 0.215)),
    "Corner_Pilaster_Ground": ((-0.23, 0.23), (0, 4.0), (-0.23, 0.23)),
    "Cornice": ((-1.5, 1.5), (0, 0.6), (-0.3, 0.35)),
    "Cornice_Corner": ((-0.35, 0.35), (0, 0.6), (-0.35, 0.35)),
    "Awning": ((-1.4, 1.4), (-0.58, 0), (-0.03, 0.9)),
}


def verify_fbx(name, path):
    """Re-import the FBX into a scratch state and report Unity-space bounds against the contract."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y")
    new = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    pts = [o.matrix_world @ Vector(c) for o in new for c in o.bound_box]
    ux = [p.x for p in pts]; uy = [p.z for p in pts]; uz = [-p.y for p in pts]
    got = ((min(ux), max(ux)), (min(uy), max(uy)), (min(uz), max(uz)))
    ok = True
    exp = EXPECT.get(name)
    if exp:
        for (g0, g1), (e0, e1) in zip(got, exp):
            if e0 is not None and abs(g0 - e0) > 0.012: ok = False
            if e1 is not None and abs(g1 - e1) > 0.012: ok = False
        if name.startswith("Facade") and got[2][1] > 0.2 + 1e-3: ok = False
        if name.startswith("Facade") and abs(got[1][1] - EXPECT[name][1][1]) > 1e-3: ok = False
    rot = [tuple(round(math.degrees(a), 2) for a in o.rotation_euler) for o in new]
    scl = [tuple(round(s, 3) for s in o.scale) for o in new]
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)
    return got, ok, len(new)


# ------------------------------------------------------------------ preview rendering
def setup_render(res=900):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.image_settings.file_format = "PNG"
    sc.display.render_aa = "8"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "MATERIAL"
    sh.show_shadows = True
    sh.shadow_intensity = 0.55
    sh.show_cavity = True
    sh.cavity_type = "BOTH"
    sh.show_object_outline = False
    sc.display.light_direction = (0.5, -0.6, 0.65)
    w = bpy.data.worlds.new("W")
    w.color = (0.62, 0.68, 0.75)
    sc.world = w
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 40
    cam.data.clip_end = 500
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def u2b(x, y, z):
    return Vector((x, -z, y))


def render(cam, objs, name, target_u, dir_u=(0.55, 0.38, 0.75), dist=8.0, res=None):
    """Render only objs; camera looks at target_u (Unity coords) from direction dir_u."""
    for o in bpy.data.objects:
        if o != cam:
            o.hide_render = o not in objs
            o.hide_viewport = o.hide_render
    t = u2b(*target_u)
    d = u2b(*dir_u).normalized()
    cam.location = t + d * dist
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc = bpy.context.scene
    if res:
        sc.render.resolution_x = sc.render.resolution_y = res
    sc.render.filepath = os.path.join(PREV_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)


def place(proto, x, y, z, rotz=0.0):
    """Instance a prototype at Blender-space position with a rotation about Z (degrees)."""
    o = proto.copy()
    o.location = (x, y, z)
    o.rotation_euler = (0, 0, math.radians(rotz))
    bpy.context.scene.collection.objects.link(o)
    return o


def build_assembly(protos):
    """3-bay x 4-floor building corner (front 3 bays, right side 2 bays) from the kit pieces."""
    objs = []
    floors = [("ground", 0.0)] + [("up", H_GROUND + H_UP * i) for i in range(3)]
    front_up = [["Facade_Window", "Facade_Window", "Facade_Window"],
                ["Facade_Window", "Facade_WindowPair", "Facade_Window"],
                ["Facade_WindowPair", "Facade_Blank", "Facade_WindowPair"]]
    front_ground = ["Facade_Storefront", "Facade_Entrance", "Facade_Storefront"]
    side_up = [["Facade_Window", "Facade_WindowPair"], ["Facade_WindowPair", "Facade_Window"],
               ["Facade_Window", "Facade_Window"]]
    xs = (-3.0, 0.0, 3.0)
    for i, x in enumerate(xs):                                      # front, outward = Blender -Y
        objs.append(place(protos[front_ground[i]], x, 0, 0))
        for fl in range(3):
            objs.append(place(protos[front_up[fl][i]], x, 0, H_GROUND + H_UP * fl))
    for i, y in enumerate((1.5, 4.5)):                              # right side at x = 4.5, outward +X
        objs.append(place(protos["Facade_Storefront"], 4.5, y, 0, 90))
        for fl in range(3):
            objs.append(place(protos[side_up[fl][i]], 4.5, y, H_GROUND + H_UP * fl, 90))
    top = H_GROUND + 3 * H_UP
    for x in xs:
        objs.append(place(protos["Cornice"], x, 0, top))
    for y in (1.5, 4.5):
        objs.append(place(protos["Cornice"], 4.5, y, top, 90))
    objs.append(place(protos["Cornice_Corner"], 4.5, 0, top))
    objs.append(place(protos["Cornice_Corner"], -4.5, 0, top, -90))
    # pilasters at both corners, stacked per floor
    for x in (-4.5, 4.5):
        objs.append(place(protos["Corner_Pilaster_Ground"], x, 0, 0))
        for fl in range(3):
            objs.append(place(protos["Corner_Pilaster"], x, 0, H_GROUND + H_UP * fl))
    for x in (-3.0, 3.0):                                           # awnings over the two shopfronts
        objs.append(place(protos["Awning"], x, 0, 3.2))
    objs.append(place(protos["Awning"], 4.5, 1.5, 3.2, 90))
    # context: roof slab, sidewalk
    ctx = Piece("Asm_Context")
    ctx.box(-4.2, 4.2, top - 0.6, top + 0.3, -5.7, -0.3, "Roof")
    ctx.box(-9.0, 7.0, -0.3, 0.0, 0.0, 4.0, "Concrete")
    ctx.box(-9.0, 7.0, -0.3, 0.0, -8.0, 0.0, "Concrete")
    objs.append(ctx.finish())
    roof_y = top + 0.3
    for name, x, z, rot in (("Roof_ACUnit", -2.2, -2.0, 0), ("Roof_ACUnit", 2.8, -1.4, 90),
                            ("Roof_WaterTank", 1.2, -3.6, 0), ("Roof_Vent", -0.6, -4.6, 0),
                            ("Roof_Vent", -3.0, -4.2, 0)):
        objs.append(place(protos[name], x, -z, roof_y, rot))
    return objs


def build_tiling_check(protos):
    """Window/WindowPair/Blank bays side by side and stacked, to check seams."""
    objs = []
    for c, names in enumerate((["Facade_Window"] * 3, ["Facade_WindowPair", "Facade_Blank", "Facade_Window"])):
        for r, n in enumerate(names):
            objs.append(place(protos[n], c * 3.0 - 1.5, 0, r * H_UP))
    return objs


# ------------------------------------------------------------------ main
def main():
    clear_scene()
    make_materials()
    builders = [
        build_window, build_window_pair, build_blank, build_storefront, build_entrance,
        lambda: build_pilaster("Corner_Pilaster", H_UP, False),
        lambda: build_pilaster("Corner_Pilaster_Ground", H_GROUND, True),
        build_cornice, build_cornice_corner, build_awning, build_ac, build_water_tank, build_vent,
    ]
    protos = {}
    for b in builders:
        o = b().finish()
        protos[o.name] = o
    cam = setup_render()
    report = []
    for name, o in protos.items():
        path = os.path.join(FBX_DIR, name + ".fbx")
        export_fbx(o, path)
        report.append((name, tri_count(o), [m.name for m in o.data.materials], path))
    # verify exported files by re-import (before instancing the assembly)
    print("\n=== BBOX CHECK (Unity coords) ===")
    allok = True
    for name, tris, mats, path in report:
        got, ok, n = verify_fbx(name, path)
        allok &= ok
        print(f"{name:24s} tris={tris:4d} objs={n} x[{got[0][0]:.3f},{got[0][1]:.3f}] "
              f"y[{got[1][0]:.3f},{got[1][1]:.3f}] z[{got[2][0]:.3f},{got[2][1]:.3f}] "
              f"{'OK' if ok else 'MISMATCH'} mats={mats}")
    print("ALL BBOX OK" if allok else "BBOX PROBLEMS")
    # previews
    for name, o in protos.items():
        b = [o.matrix_world @ Vector(c) for c in o.bound_box]
        mn = Vector((min(v.x for v in b), min(v.y for v in b), min(v.z for v in b)))
        mx = Vector((max(v.x for v in b), max(v.y for v in b), max(v.z for v in b)))
        c = (mn + mx) / 2
        size = (mx - mn).length
        render(cam, [o], name, (c.x, c.z, -c.y), dist=size * 1.9, res=700)
    asm = build_assembly(protos)
    render(cam, asm, "Test_Assembly", (0.5, 6.8, -1.0), dir_u=(0.62, 0.30, 0.72), dist=27, res=1200)
    render(cam, asm, "Test_Assembly_Street", (2.0, 2.2, 0.0), dir_u=(0.45, 0.10, 0.88), dist=17, res=1200)
    tile = build_tiling_check(protos)
    render(cam, tile, "Tiling_Check", (0, 4.5, 0), dir_u=(0.0, 0.0, 1.0), dist=22, res=1000)
    render(cam, tile, "Tiling_Check_Angle", (0, 4.5, 0), dir_u=(0.6, 0.25, 0.75), dist=20, res=1000)
    print("tri summary:", {n: t for n, t, _, _ in report})


main()
