"""Stick graph + Skin modifier -> base quad body topology for Char_Human (T-pose, Blender coords)."""
import bpy, bmesh, math
from mathutils import Vector

# name -> (pos, (rx, ry)); left side (+X); mirrored automatically. Radii are Skin modifier radii.
L = {}
EDGES_L = []
CENTER = {}
EDGES_C = []
RAD_SCALE = 1.20


def build_graph():
    C = {}      # center verts
    S = {}      # left verts
    ce, se, xe = [], [], []   # center edges, left edges, cross (center-left) edges

    def c(n, pos, r): C[n] = (pos, r)
    def s(n, pos, r): S[n] = (pos, r)

    # torso + head
    c("pelvis", (0, .005, .985), (.150, .108))
    c("waist", (0, .010, 1.105), (.140, .100))
    c("ribs", (0, .015, 1.225), (.150, .106))
    c("chest", (0, .020, 1.335), (.160, .115))
    c("shoulder", (0, .015, 1.440), (.100, .100))
    c("neck0", (0, .015, 1.505), (.062, .066))
    c("neck1", (0, .012, 1.560), (.056, .060))
    c("head0", (0, .004, 1.625), (.060, .075))
    c("head1", (0, .000, 1.690), (.082, .097))
    c("head2", (0, .000, 1.755), (.078, .095))
    c("head3", (0, .002, 1.795), (.045, .060))
    for a, b in (("pelvis", "waist"), ("waist", "ribs"), ("ribs", "chest"), ("chest", "shoulder"),
                 ("shoulder", "neck0"), ("neck0", "neck1"), ("neck1", "head0"), ("head0", "head1"),
                 ("head1", "head2"), ("head2", "head3")):
        ce.append((a, b))

    # arm (left)
    s("sh0", (.130, .018, 1.442), (.072, .072))
    s("sh1", (.190, .020, 1.442), (.063, .063))
    s("ua0", (.270, .020, 1.442), (.054, .054))
    s("ua1", (.365, .020, 1.442), (.050, .050))
    s("elb", (.450, .020, 1.442), (.047, .047))
    s("fa0", (.520, .016, 1.442), (.045, .045))
    s("fa1", (.610, .012, 1.442), (.039, .039))
    s("cuff", (.688, .010, 1.442), (.040, .040))
    s("wr", (.716, .010, 1.442), (.028, .028))
    xe.append(("shoulder", "sh0"))
    for a, b in (("sh0", "sh1"), ("sh1", "ua0"), ("ua0", "ua1"), ("ua1", "elb"), ("elb", "fa0"), ("fa0", "fa1"),
                 ("fa1", "cuff"), ("cuff", "wr")):
        se.append((a, b))
    # leg (left)
    s("hip", (.095, .004, .945), (.088, .092))
    s("th0", (.095, -.002, .84), (.085, .090))
    s("th1", (.095, -.006, .70), (.072, .078))
    s("knee", (.095, -.010, .52), (.060, .064))
    s("cf0", (.095, -.004, .39), (.062, .066))
    s("cf1", (.095, .004, .25), (.052, .055))
    s("ank0", (.095, .018, .14), (.046, .048))
    s("ank", (.095, .020, .088), (.048, .050))
    xe.append(("pelvis", "hip"))
    for a, b in (("hip", "th0"), ("th0", "th1"), ("th1", "knee"), ("knee", "cf0"), ("cf0", "cf1"), ("cf1", "ank0"),
                 ("ank0", "ank")):
        se.append((a, b))
    # foot (shoe)
    s("heel", (.095, .070, .045), (.044, .044))
    s("fmid", (.095, -.040, .048), (.048, .040))
    s("ball", (.095, -.110, .042), (.052, .036))
    s("toe0", (.095, -.170, .036), (.046, .030))
    s("toe1", (.095, -.205, .032), (.034, .026))
    se.append(("ank", "heel")) if False else None
    for a, b in (("ank", "fmid"), ("fmid", "ball"), ("ball", "toe0"), ("toe0", "toe1")):
        se.append((a, b))
    se.append(("ank", "heel"))
    return C, S, ce, se, xe


def make_cage():
    C, S, ce, se, xe = build_graph()
    bm = bmesh.new()
    skin = bm.verts.layers.skin.verify()
    ids = {}

    def addv(key, pos, r, root=False):
        v = bm.verts.new(Vector(pos))
        sk = v[skin]
        sk.radius = (r[0] * RAD_SCALE, r[1] * RAD_SCALE)
        if root: sk.use_root = True
        ids[key] = v
        return v
    for n, (p, r) in C.items():
        addv(n, p, r, root=(n == "pelvis"))
    for sgn, tag in ((1, "L"), (-1, "R")):
        for n, (p, r) in S.items():
            addv(tag + n, (p[0] * sgn, p[1], p[2]), r)

    def edge(a, b):
        try:
            bm.edges.new((ids[a], ids[b]))
        except ValueError:
            pass
    for a, b in ce: edge(a, b)
    for tag in ("L", "R"):
        for a, b in se: edge(tag + a, tag + b)
        for a, b in xe: edge(a, tag + b)
    me = bpy.data.meshes.new("HumanSkin")
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("HumanSkin", me)
    bpy.context.scene.collection.objects.link(ob)
    m = ob.modifiers.new("skin", 'SKIN')
    m.branch_smoothing = 0.7
    m.use_smooth_shade = True
    m.use_x_symmetry = False
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    cage = bmesh.new()
    cage.from_mesh(bpy.data.meshes.new_from_object(ev))
    bpy.data.objects.remove(ob, do_unlink=True)
    return cage


def make_skin_mesh(subsurf=2, hands=True):
    """Cage (skin modifier + hand cages) -> subdivided mesh object 'Human'."""
    import hb_hand
    cage = make_cage()
    if hands:
        hb_hand.add_hands(cage)
    bmesh.ops.recalc_face_normals(cage, faces=cage.faces[:])
    me = bpy.data.meshes.new("HumanCage")
    cage.to_mesh(me); cage.free()
    ob = bpy.data.objects.new("HumanCage", me)
    bpy.context.scene.collection.objects.link(ob)
    sd = ob.modifiers.new("sub", 'SUBSURF')
    sd.levels = subsurf; sd.render_levels = subsurf
    sd.quality = 3
    dg = bpy.context.evaluated_depsgraph_get()
    me2 = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob2 = bpy.data.objects.new("Human", me2)
    bpy.context.scene.collection.objects.link(ob2)
    bpy.data.objects.remove(ob, do_unlink=True)
    return ob2
