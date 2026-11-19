"""Shared human-body model for Zombies 5.5 (run inside Blender).

Authoring convention as in bl_common: (x, f, z) = (right, forward, up) -> V(x, f, z).
The body is a set of smooth lofted parts (head, neck, jacket, pelvis, legs, arms, hands, shoes)
that overlap at the joints, are weighted per part, and are joined into one skinned mesh.
`skull_point` is also used by make_headwear.py to fit hair, cap and hood.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
from mathutils import Vector, Matrix, Euler
import bl_common as C
from bl_common import V, Part, mat, pchip, ring_z, ring_f, select_only, join

C.COLORS.update({"GunMetal": (.10, .10, .115), "GunPolymer": (.06, .06, .065), "GunGrip": (.04, .04, .04),
                 "Sight": (.2, 1., .3), "Hazard": (.95, .75, .05), "KioskScreen": (.2, .8, 1.),
                 "Hood": (.2, .2, .24)})


def sm(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def g(u):
    return math.exp(-u * u)


def sp(v, p):
    return math.copysign(abs(v) ** p, v)


# ------------------------------------------------------------------ skull
ZCHIN = 1.56          # chin underside height
HH = 0.225            # chin -> crown
T0 = 0.70             # height fraction where the dome starts
_T = [0, .04, .1, .2, .3, .4, .5, .6, .7]
_HW = [.036, .052, .060, .066, .070, .073, .076, .078, .078]
_F = [.030, .078, .088, .094, .090, .086, .083, .088, .083]
_B = [-.02, -.035, -.055, -.075, -.090, -.098, -.101, -.101, -.098]
f_hw, f_F, f_B = pchip(list(zip(_T, _HW))), pchip(list(zip(_T, _F))), pchip(list(zip(_T, _B)))


def skull_dims(t):
    """(half width, front, back) of the skull ring at height fraction t (0 chin .. 1 crown)."""
    if t <= T0:
        return f_hw(t), f_F(t), f_B(t)
    u = (t - T0) / (1 - T0)
    k = math.sqrt(max(0.0, 1 - u * u))
    cfc, hd = (_F[-1] + _B[-1]) / 2, (_F[-1] - _B[-1]) / 2
    return _HW[-1] * k, cfc + hd * k, cfc - hd * k


def warp(psi0, a=0.5):
    """Concentrate azimuth samples toward the front (psi=0)."""
    return psi0 - a * math.sin(psi0)


def skull_xy(t, psi, e=2.3, off=0.0):
    hw, F, B = skull_dims(t)
    hw += off; F += off; B -= off
    cf, hd = (F + B) / 2, (F - B) / 2
    return hw * sp(math.sin(psi), 2 / e), cf + hd * sp(math.cos(psi), 2 / e)


def skull_point(t, psi, off=0.0, e=2.3):
    """Blender-space point on the (feature-free) skull surface, offset outward by `off`."""
    x, f = skull_xy(t, psi, e, off)
    return V(x, f, ZCHIN + t * HH)


def face_disp(x, t):
    ax = abs(x)
    d = 0.0
    d += .008 * g((t - .46) / .075) * g(ax / .009)                         # nose bridge
    d += .019 * g((t - .345) / .030) * g(ax / .012)                        # nose tip
    d += .011 * g((t - .335) / .020) * g((ax - .017) / .009)               # alae
    d -= .006 * g((t - .305) / .012) * g((ax - .012) / .008)               # nostrils
    d += .008 * g((t - .565) / .030) * g(ax / .055)                        # brow ridge
    d -= .007 * g((t - .50) / .035) * g((ax - .033) / .020)                # eye sockets
    d += .003 * g((t - .527) / .012) * g((ax - .032) / .020)               # upper lid
    d += .0025 * g((t - .474) / .012) * g((ax - .032) / .018)              # lower lid
    d += .005 * g((t - .40) / .040) * g((ax - .055) / .020)                # cheekbones
    d += .0065 * g((t - .226) / .018) * g(ax / .026)                       # upper lip
    d += .0070 * g((t - .168) / .019) * g(ax / .025)                       # lower lip
    d -= .0030 * g((t - .197) / .007) * g(ax / .028)                       # mouth line
    d -= .0035 * g((t - .128) / .014) * g(ax / .02)                        # under lip
    d += .004 * g((t - .26) / .010) * g(ax / .006)                         # philtrum ridge
    return d


HEAD_TS = [0, .03, .06, .085, .105, .12, .134, .148, .16, .172, .184, .196, .208, .22, .232, .244, .258, .272,
           .286, .30, .314, .328, .342, .356, .372, .39, .41, .43, .45, .47, .49, .51, .53, .55, .57, .59, .62, .66, .70] + \
          [T0 + (1 - T0) * math.sin(math.radians(a)) for a in (20, 40, 58, 74, 86, 90)]
HEAD_N = 44


def build_head(features=True, name="head"):
    P = Part(name)
    rings = []
    for t in HEAD_TS:
        hw, F, B = skull_dims(t)
        ring = []
        for k in range(HEAD_N):
            psi = warp(2 * math.pi * k / HEAD_N)
            x, f = skull_xy(t, psi, 2.0 if t < .06 else 2.3)
            if features and math.cos(psi) > 0.15 and 0 < t < .95:
                d = face_disp(x, t) * sm((math.cos(psi) - .15) / .3)
                cf, hd = (F + B) / 2, (F - B) / 2
                nx, nf = x / max(hw * hw, 1e-6), (f - cf) / max(hd * hd, 1e-6)
                L = math.hypot(nx, nf) or 1
                x, f = x + d * nx / L, f + d * nf / L
            ring.append(V(x, f, ZCHIN + t * HH))
        rings.append(ring)
    P.loft(rings, "Skin", cap0=True, cap1=True)
    return P


# ------------------------------------------------------------------ limb helpers
def zrings(tab, n=16, e=2.0, sx=1.0):
    """tab rows: (z, cx, cf, rx, rf) -> rings (horizontal, vertical limb)."""
    return [ring_z(z, sx * cx, cf, rx, rf, n, e) for z, cx, cf, rx, rf in tab]


def scaled_tab(tab, s):
    return [(z, cx, cf, rx * s, rf * s) for z, cx, cf, rx, rf in tab]


def xform(P, M):
    bmesh.ops.transform(P.bm, matrix=M, verts=P.bm.verts)


JACKET = [  # z, hw, hd, cf, e
    (0.90, .198, .112, 0, 2.4), (0.95, .198, .112, 0, 2.4), (1.02, .182, .106, 0, 2.3), (1.10, .172, .10, 0, 2.2),
    (1.17, .166, .098, 0, 2.2), (1.24, .160, .105, 0, 2.3), (1.31, .178, .115, -.003, 2.4),
    (1.38, .192, .12, -.005, 2.5), (1.44, .205, .118, -.005, 2.5), (1.47, .202, .102, -.006, 2.4),
    (1.488, .186, .090, -.008, 2.2), (1.502, .165, .080, -.010, 2.1), (1.513, .135, .070, -.011, 2.0),
    (1.522, .098, .062, -.012, 2.0), (1.528, .078, .060, -.012, 2.0)]


def build_jacket():
    P = Part("jacket")
    zs = [z for z, *_ in JACKET]
    fh = [pchip([(z, r[i]) for z, *r in JACKET]) for i in range(4)]
    rings = []

    def ring(z, s=1.0, ds=0.0):
        hw, hd, cf, e = fh[0](z), fh[1](z), fh[2](z), fh[3](z)
        return ring_z(z, 0, cf, hw * s + ds, hd * s + ds, 28, e)
    # hem with a folded lip, slight flare
    rings.append(ring(.926, .90))
    rings.append(ring(.908, 1.0, .006))
    rings.append(ring(.935, 1.0, .007))
    z = .96
    while z < 1.47:
        rings.append(ring(z, 1.0, .005 * max(0, 1 - (z - .9) / .25)))
        z += .03
    for z in (1.488, 1.495, 1.502, 1.508, 1.513, 1.518, 1.522, 1.528):
        rings.append(ring(z))
    # collar
    for z, hw, hd in ((1.533, .074, .063), (1.544, .068, .058), (1.547, .050, .045)):
        rings.append(ring_z(z, 0, -.012, hw, hd, 28, 2.0))
    # zip line: a shallow groove down the front centre (ring vertex 7 is the front middle)
    for r in rings[3:-3]:
        r[7] = r[7] + Vector((0, .0035, 0))
    P.loft(rings, "Top")
    return P


def build_pelvis():
    P = Part("pelvis")
    R = [(.87, .125, .080, 0), (.90, .160, .090, 0), (.95, .170, .094, 0), (1.0, .165, .092, 0)]
    P.loft([ring_z(z, 0, cf, hw, hd, 28, 2.3) for z, hw, hd, cf in R], "Pants")
    return P


LEG = [  # z, cx, cf, rx, rf
    (1.04, .100, 0, .066, .064), (.98, .100, 0, .076, .078), (.90, .100, 0, .084, .092), (.85, .100, 0, .088, .094), (.72, .099, .002, .082, .084),
    (.60, .097, .005, .067, .070), (.53, .096, .008, .060, .063), (.50, .096, .012, .058, .060),
    (.46, .096, .008, .054, .057), (.40, .096, .002, .057, .063), (.33, .096, -.004, .058, .065),
    (.22, .096, -.003, .047, .050), (.14, .096, -.002, .041, .043), (.10, .096, 0, .038, .040)]


def build_leg(sx):
    P = Part("leg%+d" % sx)
    fn = [pchip([(z, r[i]) for z, *r in reversed(LEG)]) for i in range(4)]
    zs = [z for z, *_ in LEG]
    tab = []
    z = 1.04
    while z > .12:
        tab.append((z, fn[0](z), fn[1](z), fn[2](z) * 1.06, fn[3](z) * 1.06))
        z -= .05 if z > .6 else .04
    # trouser hem
    for z, s in ((.125, 1.04), (.095, 1.12), (.078, 1.12)):
        tab.append((z, fn[0](.1), fn[1](.1), fn[2](.1) * s * 1.06, fn[3](.1) * s * 1.06))
    tab.append((.082, fn[0](.1), fn[1](.1), fn[2](.1) * 1.0, fn[3](.1) * 1.0))
    P.loft(zrings(tab, 16, 2.0, sx), "Pants", cap1=False)
    return P


def build_shoe(sx):
    P = Part("shoe%+d" % sx)
    cx = sx * .096
    K = [(-.078, .020, .060), (-.07, .030, .085), (-.05, .035, .100), (-.02, .036, .106), (.02, .042, .100),
         (.06, .049, .088), (.10, .052, .076), (.14, .052, .065), (.18, .045, .052), (.205, .031, .040),
         (.217, .016, .031)]
    up = [ring_f(f, cx, (.016 + zt) / 2, hw, (zt - .016) / 2, 14, 2.6) for f, hw, zt in K]
    P.loft(up, "Shoe")
    so = [ring_f(f, cx, .012, hw + .004, .012, 14, 3.2) for f, hw, zt in K[:-1]]
    so.append(ring_f(.222, cx, .012, .012, .009, 14, 3.0))
    P.loft(so, "Shoe")
    return P


ARM = [  # local z (shoulder joint at 0), cx, cf, rx, rf
    (.004, 0, 0, .018, .020), (-.006, 0, 0, .034, .037), (-.02, 0, 0, .042, .045), (-.06, 0, 0, .046, .049),
    (-.12, 0, .002, .043, .047), (-.20, 0, .004, .040, .044), (-.27, 0, .004, .036, .040), (-.31, 0, .0, .035, .038),
    (-.35, 0, .006, .038, .041), (-.43, 0, .012, .034, .035), (-.50, 0, .016, .028, .026), (-.54, 0, .017, .025, .023)]
TILT = math.radians(8)


def arm_matrix(sx):
    return Matrix.Translation(V(sx * .21, 0, 1.50)) @ Matrix.Rotation(-sx * TILT, 4, 'Y')


def build_arm(sx):
    P = Part("arm%+d" % sx)
    fn = [pchip([(z, r[i]) for z, *r in reversed(ARM)]) for i in range(4)]
    tab = []
    z = .004
    while z > -.50:
        tab.append((z, -.022 * (1 - sm(-z / .30)), fn[1](z), fn[2](z) * 1.06, fn[3](z) * 1.06))
        z -= .01 if z > -.03 else .05
    for z, s in ((-.50, 1.12), (-.545, 1.14)):
        tab.append((z, 0, fn[1](-.54), fn[2](-.54) * s * 1.06, fn[3](-.54) * s * 1.06))
    tab.append((-.535, 0, fn[1](-.54), fn[2](-.54) * 1.0, fn[3](-.54) * 1.0))
    P.loft(zrings(tab, 14, 2.0, sx), "Top", cap1=False)
    xform(P, arm_matrix(sx))
    return P


def build_hand(sx):
    P = Part("hand%+d" % sx)
    H = [(-.50, .018, .020, .019), (-.55, .019, .021, .021), (-.585, .021, .027, .038), (-.63, .024, .026, .043),
         (-.67, .027, .025, .041), (-.71, .032, .022, .037), (-.745, .039, .018, .029), (-.765, .043, .012, .018),
         (-.772, .045, .005, .008)]
    NH = 24
    rings = []
    for z, cf, rx, rf in H:
        ring = ring_z(z, 0, cf, rx, rf, NH, 2.2)
        k = sm((-z - .64) / .05) * (1 - sm((-z - .74) / .03))      # finger zone
        out = []
        for p in ring:
            u = (-p.y - cf) / max(rf, 1e-6)                          # -1..1 across the hand width
            dep = sum(g((u - c) / .09) for c in (-.5, 0, .5)) * .16 * k
            out.append(Vector((p.x * (1 - dep), p.y, p.z)))
        rings.append(out)
    P.loft(rings, "Skin")
    for s in (sx * 1,):
        pts = [V(-sx * .006, .020, -.580), V(-sx * .012, .044, -.612), V(-sx * .014, .056, -.650), V(-sx * .012, .062, -.682)]
        P.tube([Vector(p) for p in pts], [.0140, .0125, .0105, .0075], "Skin", n=8)
    xform(P, arm_matrix(sx))
    return P


def build_neck():
    P = Part("neck")
    T = [(1.45, -.018, .056, .054), (1.50, -.016, .054, .052), (1.56, -.012, .052, .050), (1.60, -.006, .052, .050),
         (1.64, -.004, .050, .048)]
    P.loft([ring_z(z, 0, cf, rx, rf, 16, 2.0) for z, cf, rx, rf in T], "Skin")
    return P


def build_ears():
    P = Part("ears")
    zc = ZCHIN + .43 * HH
    for s in (-1, 1):
        b = bmesh.new()
        bmesh.ops.create_uvsphere(b, u_segments=10, v_segments=8, radius=1.0,
                                  matrix=Matrix.Diagonal((.0065, .018, .031, 1)))
        for v in b.verts:           # dimple on the outer face
            if abs(v.co.x) > .004:
                d = math.hypot(v.co.y / .018, v.co.z / .031)
                v.co.x -= math.copysign(.0035 * g(d * 1.7), v.co.x) * (1 if v.co.x * s > 0 else 0)
        M = Matrix.Translation(V(s * .079, -.012, zc)) @ Matrix.Rotation(s * math.radians(-22), 4, 'Z') @ \
            Matrix.Rotation(math.radians(-8), 4, 'X') @ Matrix.Translation((s * .004, 0, 0))
        bmesh.ops.transform(b, matrix=M, verts=b.verts)
        before = P.snapshot()
        mp = {}
        for v in b.verts:
            mp[v] = P.bm.verts.new(v.co)
        for f in b.faces:
            nf = P.bm.faces.new([mp[v] for v in f.verts])
            nf.material_index = P.mi("Skin")
        b.free()
    return P


def head_probe(headobj, x, t, from_f=.3):
    """Front surface hit of the head object at lateral x and height fraction t -> forward f or None."""
    loc, _ = C.raycast(headobj, V(x, from_f, ZCHIN + t * HH), Vector((0, 1, 0)))
    return None if loc is None else -loc.y


def build_face_extras(headobj):
    """Eyes (Eye slot) and eyebrows (Hair slot), fitted to the head surface by raycast."""
    P = Part("face")
    for s in (-1, 1):
        x = s * .0315
        f = head_probe(headobj, x, .50)
        P.sphere(V(x, f - .0075, ZCHIN + .50 * HH), (.0115, .0115, .0115), "Eye", (12, 8))
        pts, rad = [], []
        for i, (ax, dt) in enumerate(((.008, 0), (.020, .006), (.032, .010), (.044, .008), (.054, .0)) ):
            xx = s * ax
            t = .585 + dt
            ff = head_probe(headobj, xx, t)
            pts.append(V(xx, ff + .001, ZCHIN + t * HH))
            rad.append((.0024, .0048 if i not in (0, 4) else .0036))
        P.tube(pts, rad, "Hair", n=6)
    return P


# ------------------------------------------------------------------ weights
BONES = [("Body", (0, .95, 0), None), ("LegL", (-.11, .95, 0), "Body"), ("LegR", (.11, .95, 0), "Body"),
         ("ArmL", (-.21, 1.50, 0), "Body"), ("ArmR", (.21, 1.50, 0), "Body"), ("Head", (0, 1.55, 0), "Body")]


def weights_for(kind, co):
    """co: Blender-space vertex position. Returns {bone: weight}."""
    x, z = co.x, co.z
    ax = abs(x)
    side = "R" if x > 0 else "L"
    w = {}
    if kind == "head":
        w["Head"] = 1.0
    elif kind == "neck":
        w["Head"] = sm((z - 1.50) / .08)
    elif kind == "jacket":
        wh = .4 * sm((z - 1.48) / .06) * (1 - sm((ax - .05) / .09))
        wa = .65 * sm((ax - .08) / .12) * sm((z - 1.30) / .15) * (1 - .8 * sm((z - 1.475) / .03))
        wl = sm((.99 - z) / .07) * sm(ax / .06)
        w["Head"] = wh; w["Arm" + side] = wa; w["Leg" + side] = wl
    elif kind == "pelvis":
        w["Leg" + side] = sm((1.0 - z) / .10) * sm(ax / .05)
    elif kind.startswith("leg"):
        sd = "R" if kind.endswith("+1") else "L"
        w["Leg" + sd] = 1 - .7 * sm((z - .95) / .09)
    elif kind.startswith("arm"):
        sd = "R" if kind.endswith("+1") else "L"
        w["Arm" + sd] = 1.0 - .45 * sm((z - 1.465) / .06)
    elif kind.startswith("shoe") or kind.startswith("hand"):
        sd = "R" if kind.endswith("+1") else "L"
        w[("Leg" if kind.startswith("shoe") else "Arm") + sd] = 1.0
    tot = sum(w.values())
    if tot < 1:
        w["Body"] = 1 - tot
    else:
        w = {k: v / tot for k, v in w.items()}
    w = {k: v for k, v in w.items() if v > 0.004}
    s = sum(w.values())
    return {k: v / s for k, v in w.items()}


def assign_weights(ob, kind):
    for n, *_ in BONES:
        ob.vertex_groups.new(name=n)
    for v in ob.data.vertices:
        for b, wt in weights_for(kind, v.co).items():
            ob.vertex_groups[b].add([v.index], wt, 'REPLACE')


def make_armature():
    am = bpy.data.armatures.new("Armature")
    ob = bpy.data.objects.new("Armature", am)
    bpy.context.scene.collection.objects.link(ob)
    select_only(ob)
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (x, y, z), parent in BONES:
        eb = am.edit_bones.new(name)
        eb.head = V(x, z, y)
        eb.tail = eb.head + Vector((0, .10, 0))     # all bones point along +Y so local axes == world axes
        eb.roll = 0
        if parent:
            eb.parent = am.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    return ob


# ------------------------------------------------------------------ the body
def build_body():
    """Builds Char_Body + Armature in the current scene. Returns (body, armature)."""
    parts = []   # (Part, kind)
    headP = build_head()
    head = headP.build(smooth=75)
    head.name = "head_ref"
    face = build_face_extras(head)
    parts.append((headP, "head", head))
    objs = [(head, "head")]
    for P, kind in ((face, "head"), (build_ears(), "head"), (build_neck(), "neck"), (build_jacket(), "jacket"),
                    (build_pelvis(), "pelvis"), (build_leg(-1), "leg-1"), (build_leg(1), "leg+1"),
                    (build_shoe(-1), "shoe-1"), (build_shoe(1), "shoe+1"), (build_arm(-1), "arm-1"),
                    (build_arm(1), "arm+1"), (build_hand(-1), "hand-1"), (build_hand(1), "hand+1")):
        ob = P.build(smooth=75)
        objs.append((ob, kind))
    for ob, kind in objs:
        assign_weights(ob, kind)
        print("  part %-10s tris %d" % (ob.name, C.tri_count(ob)))
    body = join([o for o, _ in objs], "Char_Body")
    rig = make_armature()
    body.parent = rig
    md = body.modifiers.new("Armature", 'ARMATURE')
    md.object = rig
    select_only(body)
    for p in body.data.polygons:
        p.use_smooth = True
    return body, rig


# ------------------------------------------------------------------ posing / instances
def pose(rig, spec):
    """spec: {bone: (rx, ry, rz) degrees about world X / Y / Z}"""
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0, 0, 0)
    for b, (rx, ry, rz) in spec.items():
        rig.pose.bones[b].rotation_euler = (math.radians(rx), math.radians(ry), math.radians(rz))
    bpy.context.view_layer.update()


def instance(body, rig, offset=(0, 0, 0)):
    """Duplicate body+rig (independent pose) at an offset. Returns (body2, rig2)."""
    rig2 = rig.copy(); rig2.data = rig.data.copy()
    body2 = body.copy(); body2.data = body.data
    bpy.context.scene.collection.objects.link(rig2)
    bpy.context.scene.collection.objects.link(body2)
    body2.parent = rig2
    body2.modifiers["Armature"].object = rig2
    rig2.location = Vector(offset)
    return body2, rig2


# ------------------------------------------------------------------ rendering (Workbench, orthographic)
def render_view(objs, path, az=0, el=8, size=(900, 900), target=None, scale=2.2, floor=True, floor_size=6.0):
    """Orthographic Workbench render of `objs`. az 0 = looking at the character's front (camera at +forward)."""
    C.ensure_dir(os.path.dirname(path))
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    sh = scn.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'
    for k, v in (("show_shadows", True), ("shadow_intensity", 0.3), ("show_cavity", True), ("cavity_type", 'BOTH'),
                 ("show_object_outline", False), ("show_specular_highlight", True)):
        try: setattr(sh, k, v)
        except Exception: pass
    scn.render.resolution_x, scn.render.resolution_y = size
    scn.render.image_settings.file_format = 'PNG'
    scn.render.filepath = path
    scn.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("W"); w.color = (.62, .66, .72); scn.world = w
    keep = set(objs)
    made = []
    for o in scn.objects:
        o.hide_render = o not in keep
    tgt = Vector(target) if target is not None else Vector((0, 0, .9))
    if floor:
        fl = bpy.data.meshes.new("floor"); b = bmesh.new()
        bmesh.ops.create_grid(b, x_segments=1, y_segments=1, size=floor_size, matrix=Matrix.Translation((tgt.x, 0, -.002)))
        b.to_mesh(fl); b.free()
        fm = bpy.data.materials.new("FloorPrev"); fm.diffuse_color = (.72, .74, .76, 1); fl.materials.append(fm)
        fo = bpy.data.objects.new("floor", fl); scn.collection.objects.link(fo); made.append(fo)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); scn.collection.objects.link(cam); made.append(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = scale; cam.data.clip_end = 100
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    cam.location = tgt + d * 8
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scn.camera = cam
    bpy.ops.render.render(write_still=True)
    for o in made:
        bpy.data.objects.remove(o, do_unlink=True)
    for o in scn.objects:
        o.hide_render = False
    bpy.data.worlds.remove(w)


def place(ob, rig, bone, rest_local):
    """Rigidly attach `ob` to a posed bone: rest_local is the object's armature-space matrix in the rest pose."""
    bpy.context.view_layer.update()
    pb = rig.pose.bones[bone]
    head = rig.data.bones[bone].head_local
    ob.matrix_world = rig.matrix_world @ pb.matrix @ Matrix.Translation(-head) @ rest_local


def clone_into_scene(ob):
    o2 = ob.copy()
    bpy.context.scene.collection.objects.link(o2)
    return o2
