"""Shared helpers for the Zombies 5.5 asset scripts (run inside Blender, headless).

Authoring convention: helpers take (x, f, z) where f is FORWARD (Unity +Z) and z is up.
V(x, f, z) converts to Blender coordinates (forward = -Y). Everything is built into a
bmesh `Part`, finished into an object, joined, UV-projected in world meters and exported.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix, Euler, noise
from mathutils.bvhtree import BVHTree

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))  # project root (Tools/Blender/<this file> -> up 3)
MODELS = os.path.join(ROOT, "Assets", "Art", "Models")
PREVIEWS = os.path.join(ROOT, "Tools", "Blender", "previews")

# Plausible viewport colors only; Unity tints from its palette.
COLORS = {
    "Wall": (.75, .6, .5), "Trim": (.8, .78, .72), "Frame": (.9, .9, .9), "Glass": (.07, .1, .13),
    "Interior": (.025, .025, .03), "Door": (.4, .25, .15), "Awning": (.7, .3, .2), "Roof": (.35, .35, .37),
    "Metal": (.13, .14, .16), "Concrete": (.62, .62, .6), "Wood": (.55, .38, .22), "Paint": (.75, .1, .08),
    "CarPaint": (.12, .25, .55), "Chrome": (.82, .84, .88), "Rubber": (.04, .04, .045),
    "Headlight": (1., .92, .65), "Taillight": (.8, .04, .04), "LampGlass": (1., .88, .55),
    "Bark": (.3, .2, .12), "Leaf": (.2, .5, .15), "Water": (.25, .5, .7), "Skin": (.85, .62, .5),
    "Top": (.25, .4, .65), "Pants": (.2, .22, .3), "Shoe": (.12, .1, .1), "Hair": (.2, .12, .07),
    "Cap": (.75, .15, .15), "Bag": (.15, .45, .3), "Eye": (.02, .02, .03),
}


def V(x, f, z):
    return Vector((x, -f, z))


def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        c = COLORS.get(name, (.5, .5, .5))
        m.diffuse_color = (*c, 1.0)
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*c, 1.0)
        if name in ("Chrome",):
            m.metallic = 1.0
    return m


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure_dir(p):
    os.makedirs(p, exist_ok=True)


def xf(center=(0, 0, 0), scale=(1, 1, 1), rot=None):
    """Matrix: translate(center) * rot * scale. rot = Euler / Matrix3 / Quaternion / None."""
    T = Matrix.Translation(Vector(center))
    R = rot.to_matrix().to_4x4() if rot is not None else Matrix.Identity(4)
    S = Matrix.Diagonal((*scale, 1.0))
    return T @ R @ S


def aim(direction):
    """Rotation whose local +Z points along direction."""
    return Vector(direction).normalized().to_track_quat('Z', 'Y')


# ---------------------------------------------------------------- ring generators
def ring_z(z, cx, cf, hw, hd, n=16, e=2.0):
    """Horizontal superellipse ring (x / forward plane) at height z; e=2 ellipse, larger = boxier."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        x = hw * math.copysign(abs(c) ** (2 / e), c)
        f = hd * math.copysign(abs(s) ** (2 / e), s)
        pts.append(V(cx + x, cf + f, z))
    return pts


def ring_f(f, cx, cz, hw, hh, n=16, e=2.0):
    """Vertical superellipse ring (x / z plane) at forward position f."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        pts.append(V(cx + hw * math.copysign(abs(c) ** (2 / e), c), f, cz + hh * math.copysign(abs(s) ** (2 / e), s)))
    return pts


def ring_circle(center, axis, r, n=16, r2=None):
    """Ring around axis through center."""
    axis = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(axis.z) < .9 else Vector((1, 0, 0))
    u = axis.cross(ref).normalized()
    v = axis.cross(u).normalized()
    r2 = r if r2 is None else r2
    return [Vector(center) + u * math.cos(2 * math.pi * i / n) * r + v * math.sin(2 * math.pi * i / n) * r2
            for i in range(n)]


def pchip(pts):
    """Monotone cubic interpolation through (x, y) points; returns f(x)."""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    n = len(xs)
    d = [(ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) for i in range(n - 1)]
    m = [d[0]] + [0 if d[i - 1] * d[i] <= 0 else 2 * d[i - 1] * d[i] / (d[i - 1] + d[i]) for i in range(1, n - 1)] + [d[-1]]

    def f(x):
        if x <= xs[0]: return ys[0]
        if x >= xs[-1]: return ys[-1]
        i = max(k for k in range(n - 1) if xs[k] <= x)
        h = xs[i + 1] - xs[i]; t = (x - xs[i]) / h
        h00 = 2 * t**3 - 3 * t**2 + 1; h10 = t**3 - 2 * t**2 + t
        h01 = -2 * t**3 + 3 * t**2; h11 = t**3 - t**2
        return h00 * ys[i] + h10 * h * m[i] + h01 * ys[i + 1] + h11 * h * m[i + 1]
    return f


# ---------------------------------------------------------------- Part (bmesh builder)
class Part:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []

    def mi(self, m):
        if m not in self.slots:
            self.slots.append(m)
        return self.slots.index(m)

    def _tag(self, before, m):
        i = self.mi(m)
        for f in self.bm.faces:
            if f not in before:
                f.material_index = i

    def snapshot(self):
        return set(self.bm.faces)

    def face(self, pts, m):
        try:
            vs = [self.bm.verts.new(p) for p in pts]
            f = self.bm.faces.new(vs)
            f.material_index = self.mi(m)
            return f
        except ValueError:
            return None

    def loft(self, rings, m, cap0=True, cap1=True):
        """Skin consecutive rings (equal vertex counts). m: slot name or fn(i, j) -> slot name."""
        bm = self.bm
        n = len(rings[0])
        verts = []
        for ring in rings:
            c = sum(ring, Vector()) / len(ring)
            if max((p - c).length for p in ring) < 1e-6:
                verts.append([bm.verts.new(c)] * n)
            else:
                verts.append([bm.verts.new(p) for p in ring])
        for i in range(len(rings) - 1):
            for j in range(n):
                a, b = verts[i][j], verts[i][(j + 1) % n]
                c, d = verts[i + 1][(j + 1) % n], verts[i + 1][j]
                quad = [v for k, v in enumerate((a, b, c, d)) if v is not (a, b, c, d)[k - 1]]
                if len(set(quad)) < 3:
                    continue
                try:
                    f = bm.faces.new(quad)
                except ValueError:
                    continue
                f.material_index = self.mi(m(i, j) if callable(m) else m)
        for ring_v, cap in ((verts[0], cap0), (verts[-1], cap1)):
            if cap and len(set(ring_v)) == n:
                try:
                    f = bm.faces.new(ring_v)
                    f.material_index = self.mi(m(0, 0) if callable(m) else m)
                except ValueError:
                    pass
        return verts

    def tube(self, pts, radii, m, n=8, cap=True, ry=None):
        """Tube along a polyline. radii: float list (or (rx, ry) tuples)."""
        rings = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            r = radii[i]
            rx, ry_ = (r if isinstance(r, tuple) else (r, r))
            rings.append(ring_circle(p, t, rx, n, ry_))
        return self.loft(rings, m, cap, cap)

    def lathe(self, profile, m, axis=(0, 0, 1), center=(0, 0, 0), n=24, cap_ends=False):
        """Revolve [(radius, height)] about axis through center. Radius 0 collapses to a point."""
        axis = Vector(axis).normalized(); center = Vector(center)
        rings = [ring_circle(center + axis * h, axis, max(r, 0.0), n) for r, h in profile]
        return self.loft(rings, m, cap0=cap_ends, cap1=cap_ends)

    # --- primitives (bmesh.ops) with material tagging
    def sphere(self, center, radii, m, seg=(12, 8), rot=None):
        before = self.snapshot()
        bmesh.ops.create_uvsphere(self.bm, u_segments=seg[0], v_segments=seg[1], radius=1.0,
                                  matrix=xf(center, radii, rot))
        self._tag(before, m)

    def ico(self, center, radii, m, sub=2, rot=None):
        before = self.snapshot()
        r = bmesh.ops.create_icosphere(self.bm, subdivisions=sub, radius=1.0, matrix=xf(center, radii, rot))
        self._tag(before, m)
        return r["verts"]

    def cyl(self, center, r1, r2, h, m, n=16, rot=None, caps=True):
        before = self.snapshot()
        bmesh.ops.create_cone(self.bm, cap_ends=caps, segments=n, radius1=r1, radius2=r2, depth=h,
                              matrix=xf(center, (1, 1, 1), rot))
        self._tag(before, m)

    def rbox(self, center, size, m, bevel=0.0, seg=2, rot=None):
        """Box centered at center (Blender coords), size = (x, y, z) extents, optional bevel."""
        before = self.snapshot()
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=xf(center, size, rot))
        if bevel > 0:
            edges = list({e for v in r["verts"] for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=seg, affect='EDGES', profile=0.5)
        self._tag(before, m)

    def bar(self, p0, p1, w, t, m, bevel=0.0, seg=1):
        """Rectangular bar from p0 to p1 with cross-section w x t."""
        p0, p1 = Vector(p0), Vector(p1)
        self.rbox((p0 + p1) / 2, (w, t, (p1 - p0).length), m, bevel, seg, aim(p1 - p0))

    def drop_faces(self, pred):
        """Delete faces for which pred(center_in_Blender_coords) is true."""
        bmesh.ops.delete(self.bm, geom=[f for f in self.bm.faces if pred(f.calc_center_median())], context='FACES')

    # --- finishing
    def build(self, solidify=0.0, smooth=35, flat=(), radial=False, name=None):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        if solidify:
            bmesh.ops.solidify(bm, geom=bm.faces[:] + bm.edges[:] + bm.verts[:], thickness=solidify)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        project_uvs(bm, radial)
        name = name or self.name
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for s in self.slots:
            me.materials.append(mat(s))
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        select_only(ob)
        for p in me.polygons:
            p.use_smooth = True
        if smooth:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
        if flat:
            idx = {i for i, s in enumerate(self.slots) if s in flat}
            for p in me.polygons:
                if p.material_index in idx:
                    p.use_smooth = False
        return ob


def select_only(*objs):
    for o in bpy.context.scene.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def project_uvs(bm, radial=False):
    """Box projection in world meters (1 UV unit = 1 m); optional cylindrical wrap for round stone."""
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        c = f.calc_center_median()
        ax = max(range(3), key=lambda k: abs(n[k]))
        for l in f.loops:
            p = l.vert.co
            if radial and abs(n.z) < 0.7:
                rc = max(math.hypot(c.x, c.y), 1e-3)
                da = math.atan2(p.y, p.x) - math.atan2(c.y, c.x)
                da = (da + math.pi) % (2 * math.pi) - math.pi
                u = rc * (math.atan2(c.y, c.x) + da)
                l[uv].uv = (u, p.z)
            elif ax == 0:
                l[uv].uv = (p.y, p.z)
            elif ax == 1:
                l[uv].uv = (p.x, p.z)
            else:
                l[uv].uv = (p.x, p.y)


def join(objs, name):
    select_only(*objs)
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def boolean_diff(ob, cutters):
    """Subtract cutter objects (then deleted) from ob using the EXACT solver."""
    for c in cutters:
        md = ob.modifiers.new("cut", 'BOOLEAN')
        md.operation = 'DIFFERENCE'; md.object = c; md.solver = 'EXACT'
        select_only(ob)
        bpy.ops.object.modifier_apply(modifier=md.name)
        bpy.data.objects.remove(c, do_unlink=True)


def raycast(ob, origin, direction):
    """Hit location + normal on ob's surface (None if no hit)."""
    bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    loc, nrm, idx, dist = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    return (loc, nrm) if loc is not None else (None, None)


def tri_count(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


def dims(ob):
    """Bounding box in Unity terms: (x, y=up, z=forward) size plus min/max."""
    vs = [v.co for v in ob.data.vertices]
    mn = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    mx = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return mn, mx


def report(ob, expect=None, tol=0.06):
    mn, mx = dims(ob)
    size = mx - mn
    # Unity axes: x, y(up)=Blender z, z(fwd)=-Blender y
    u = (size.x, size.z, size.y)
    umin = (mn.x, mn.z, -mx.y); umax = (mx.x, mx.z, -mn.y)
    msg = "ASSET %-20s size(x,y,z)=%.3f %.3f %.3f  min=(%.3f %.3f %.3f) max=(%.3f %.3f %.3f)  tris=%d" % (
        ob.name, *u, *umin, *umax, tri_count(ob))
    print(msg)
    if expect:
        for a, e, nm in zip(u, expect, "xyz"):
            if e is not None and abs(a - e) > tol:
                print("  WARNING %s: %s extent %.3f vs contract %.3f" % (ob.name, nm, a, e))
    return u, umin, umax


def export_fbx(objs, category, name):
    d = os.path.join(MODELS, category)
    ensure_dir(d)
    select_only(*objs)
    bpy.ops.export_scene.fbx(filepath=os.path.join(d, name + ".fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types={'MESH', 'EMPTY'})


# ---------------------------------------------------------------- preview rendering (Workbench)
def render_preview(objs, path, az=35, el=18, size=(960, 720), name_floor=True, extra_fit=1.0):
    ensure_dir(os.path.dirname(path))
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    sh = scn.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'MATERIAL'
    for k, v in (("show_shadows", True), ("shadow_intensity", 0.35), ("show_cavity", True), ("cavity_type", 'BOTH'),
                 ("show_object_outline", False), ("show_specular_highlight", True)):
        try: setattr(sh, k, v)
        except Exception: pass
    scn.render.resolution_x, scn.render.resolution_y = size
    scn.render.image_settings.file_format = 'PNG'
    scn.render.filepath = path
    scn.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("W"); w.color = (.62, .66, .72); scn.world = w
    keep = set(objs)
    for o in scn.objects:
        o.hide_render = o not in keep
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) / 2
    rad = (mx - mn).length / 2
    made = []
    if name_floor:
        fl = bpy.data.meshes.new("floor"); b = bmesh.new()
        s = rad * 1.8
        bmesh.ops.create_grid(b, x_segments=1, y_segments=1, size=s, matrix=Matrix.Translation((ctr.x, ctr.y, mn.z - 0.002)))
        b.to_mesh(fl); b.free()
        fm = bpy.data.materials.new("FloorPrev"); fm.diffuse_color = (.72, .74, .76, 1)
        fl.materials.append(fm)
        fo = bpy.data.objects.new("floor", fl); scn.collection.objects.link(fo); made.append(fo); keep.add(fo)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); made.append(cam); keep.add(cam)
    cam.data.lens = 50
    aspect = min(size) / max(size)
    fov = 2 * math.atan((36 * aspect / 2) / 50)
    dist = rad / math.sin(fov / 2) * 1.05 * extra_fit
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = ctr + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scn.camera = cam
    bpy.ops.render.render(write_still=True)
    for o in made:
        bpy.data.objects.remove(o, do_unlink=True)
    for o in scn.objects:
        o.hide_render = False
    bpy.data.worlds.remove(w)


def noise_blob(p, center, radii, m, seed=0, amp=.22, freq=1.1, sub=2, flat_below=None):
    """Low-poly organic clump: icosphere displaced by fractal noise (foliage, shrubs)."""
    verts = p.ico(center, radii, m, sub)
    c = Vector(center)
    off = Vector((seed * 13.1, seed * 7.7, seed * 3.3))
    for v in verts:
        d = v.co - c
        u = Vector((d.x / radii[0], d.y / radii[1], d.z / radii[2]))
        k = 1.0 + amp * noise.fractal(u * freq + off, 1.0, 2.0, 3)
        v.co = c + Vector((u.x * radii[0], u.y * radii[1], u.z * radii[2])) * k
        if flat_below is not None and v.co.z < flat_below:
            v.co.z = flat_below + (v.co.z - flat_below) * .3
