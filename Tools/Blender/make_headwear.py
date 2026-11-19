"""Char_HairShort, Char_HairLong, Char_Cap, Char_Hood, Char_Backpack, refitted to the Char_Body head/back.
Run: blender --background --factory-startup --python make_headwear.py
Pivot of the head pieces = the Head bone (neck base, world (0, 1.55, 0)); the backpack pivot = top centre of its
back face (placed at (0, 1.50, -0.13) in character space). Overwrites the old FBX files.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bl_human import *
from make_body import POSES

NECK_BASE = Vector((0, 0, 1.55))      # Blender coords of the Head bone head
CENTER = V(0, -.005, ZCHIN + .10)     # radial offset centre inside the skull


def nz(a, b=0.0):
    """cheap deterministic pseudo-noise in [-1, 1]"""
    return (math.sin(a * 12.9898 + b * 78.233) * 43758.5453) % 1.0 * 2 - 1


# ------------------------------------------------------------------ generic hair-like shell around the head
def pchip_c(pts):
    return pchip(pts)


class Style:
    """Extension of the head rings below the skull (t < ~0.3): half width, front, back as a function of t."""
    def __init__(self, hw, F, B):
        self.hw, self.F, self.B = pchip(hw), pchip(F), pchip(B)


def shell_point(t, psi, thick, style, e=2.3):
    zt = ZCHIN + t * HH
    if t >= .0:
        tt = max(t, 0.0)
        x, f = skull_xy(min(tt, 1.0), psi, e)
        P0 = V(x, f, zt)
        d = (P0 - CENTER)
        d = d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))
        Ps = P0 + d * thick
    if t >= .36:
        return Ps
    hw, F, B = style.hw(t), style.F(t), style.B(t)
    cf, hd = (F + B) / 2, (F - B) / 2
    Pe = V(hw * sp(math.sin(psi), 2 / e), cf + hd * sp(math.cos(psi), 2 / e), zt)
    if t < 0:
        return Pe
    b = sm((.36 - t) / .32)
    return Ps * (1 - b) + Pe * b


def build_shell(name, slot, style, t_end, thick, n=36, rows=14, inner_rows=4, p=1.25, e=2.3, inner_off=.002):
    """Closed shell: outer surface from the crown pole down to a per-column end height, then back up on the inside.
    t_end(psi) -> end height fraction; thick(t, psi, v) -> outward thickness of the outer surface."""
    P = Part(name)
    psis = [warp(2 * math.pi * k / n, .35) for k in range(n)]
    ends = [t_end(ps) for ps in psis]
    rings = []
    for i in range(rows + 1):
        v = (i / rows) ** p
        ring = []
        for k, ps in enumerate(psis):
            t = 1 - (1 - ends[k]) * v
            ring.append(shell_point(t, ps, thick(t, ps, v), style, e))
        rings.append(ring)
    for i in range(inner_rows + 1):
        v = (1 - i / inner_rows) ** p
        ring = []
        for k, ps in enumerate(psis):
            t = 1 - (1 - ends[k]) * v
            ring.append(shell_point(t, ps, inner_off, style, e))
        rings.append(ring)
    P.loft(rings, slot, cap0=False, cap1=False)
    return P


def finish_head_part(P, name, flat=False):
    ob = P.build(smooth=60, name=name)
    for pl in ob.data.polygons:
        pl.use_smooth = True
    # pivot: neck base
    ob.data.transform(Matrix.Translation(-NECK_BASE))
    return ob


# ------------------------------------------------------------------ hair
SHORT = Style([(-1, .09), (0, .09), (.3, .088)], [(-1, .0), (0, .0), (.3, .02)], [(-1, -.1), (0, -.1), (.3, -.1)])
LONG = Style([(-1.3, .105), (-.6, .105), (0, .098), (.3, .090)],
             [(-1.3, -.02), (-.6, -.0), (0, .0), (.3, .02)],
             [(-1.3, -.150), (-.6, -.140), (0, -.115), (.3, -.103)])
SHORT_END = pchip([(0, .80), (.4, .78), (.8, .72), (1.2, .64), (1.57, .58), (2.0, .45), (2.5, .30), (3.15, .24)])
LONG_END = pchip([(0, .80), (.5, .76), (.9, .66), (1.25, .42), (1.5, -.15), (2.0, -.55), (2.6, -.85), (3.15, -.95)])


def sym(psi):
    p = (psi + math.pi) % (2 * math.pi) - math.pi
    return abs(p)


def hair_short():
    def t_end(psi):
        a = sym(psi)
        jag = .035 * math.sin(a * 9 + .7) + .02 * math.sin(a * 17)
        return SHORT_END(a) + jag * sm((a - .15) / .3)

    def thick(t, psi, v):
        a = sym(psi)
        clump = .0095 * abs(math.sin(psi * 8.5 + 3.0 * v + 1.0 * math.sin(psi * 3))) ** .6 + .004 * abs(math.sin(psi * 19 + 5 * v))
        vol = .010 * sm((t - .35) / .5)
        fringe = .014 * g((t - .80) / .10) * g(a / .55)       # front tuft
        crown = .005 * g((t - .96) / .08)
        edge = -.003 * sm((v - .8) / .2)                       # taper at the ends
        return .008 + vol + clump + fringe + crown + edge

    return build_shell("Char_HairShort", "Hair", SHORT, t_end, thick, n=40, rows=13, inner_rows=3)


def hair_long():
    def t_end(psi):
        a = sym(psi)
        jag = .06 * math.sin(a * 11 + .3) + .04 * math.sin(a * 23 + 1.0)
        return LONG_END(a) + jag * sm((a - .9) / .5)

    def thick(t, psi, v):
        a = sym(psi)
        clump = .011 * abs(math.sin(psi * 9.5 + 1.5 * v + .8 * math.sin(psi * 4))) ** .6 + .004 * abs(math.sin(psi * 21 + 2 * v))
        vol = .012 * sm((t - .35) / .5)
        part = .008 * g((a - 0.0) / .5) * sm((t - .7) / .15)    # centre part volume
        sway = .010 * sm(-t / .6) * math.sin(psi * 3 + 1.0)       # lengths wave
        edge = -.004 * sm((v - .85) / .15)
        return .009 + vol + clump + part + sway * (0 if t > 0 else 1) + edge

    return build_shell("Char_HairLong", "Hair", LONG, t_end, thick, n=40, rows=18, inner_rows=3, p=1.0)


# ------------------------------------------------------------------ hood
HOOD = Style([(-1.0, .135), (-.5, .140), (0, .125), (.3, .108)],
             [(-1.0, .035), (-.5, .060), (0, .075), (.3, .088)],
             [(-1.0, -.165), (-.5, -.165), (0, -.14), (.3, -.12)])
HOOD_END = pchip([(0, .80), (.5, .76), (.9, .56), (1.15, .30), (1.35, .0), (1.6, -.38), (2.2, -.62), (3.15, -.72)])


def hood():
    def t_end(psi):
        a = sym(psi)
        return HOOD_END(a) + .02 * math.sin(a * 5)

    def thick(t, psi, v):
        a = sym(psi)
        fold = .007 * math.sin(psi * 6 + 4 * v) * sm(v / .3)
        drape = .015 * sm(-t / .5) * (1 + math.sin(psi * 3))
        rim = .012 * sm((v - .86) / .1) * (1 - sm((a - 1.2) / .3))      # rolled opening edge
        return .028 + .008 * sm((t - .3) / .5) + fold + drape + rim - .02 * 0

    return build_shell("Char_Hood", "Hood", HOOD, t_end, thick, n=40, rows=18, inner_rows=4, p=1.1, inner_off=.014)


# ------------------------------------------------------------------ cap
def cap():
    CAP = Style([(-1, .09), (0, .09), (.3, .09)], [(-1, .0), (0, .0), (.3, .02)], [(-1, -.1), (0, -.1), (.3, -.1)])
    end_t = lambda psi: .60 - .10 * sm((sym(psi) - .5) / 2.0) - .03 * sm((sym(psi) - 1.2) / .6)

    def thick(t, psi, v):
        panel = -.0025 * g(((psi * 3 / math.pi + .5) % 1 - .5) / .07)          # 6 panel seams
        button = .008 * g((1 - t) / .03)
        band = .003 * sm((v - .8) / .2)                                          # slightly rolled lower edge
        return .014 + .004 * sm((t - .5) / .4) + panel + button + band

    P = build_shell("Char_Cap", "Cap", CAP, end_t, thick, n=36, rows=12, inner_rows=3, p=1.1, inner_off=.003)
    # brim: thin curved visor, starts inside the dome so it is rooted in the crown
    f0, L, W0 = .03, .145, .078
    zb = ZCHIN + .59 * HH
    rings = []
    for i in range(12):
        s = i / 11
        f = f0 + L * s
        w = W0 * (1 + .10 * math.sin(s * math.pi)) * (1 - s ** 2.6) ** .5 + .004
        ring = ring_f(f, 0, zb - .016 * s ** 1.4, w, .0028, 16, 2.4)
        for p in ring:
            p.z -= .022 * (p.x / W0) ** 2 * s
        rings.append(ring)
    P.loft(rings, "Cap", cap0=True, cap1=True)
    return P


# ------------------------------------------------------------------ backpack (pivot: top centre of the back face)
def ribbon(P, pts, hw, ht, slot, n=10):
    """Flat strap along pts; width direction ~ +x, thickness direction = tangent x width."""
    rings = []
    X = Vector((1, 0, 0))
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        nrm = t.cross(X).normalized()
        rings.append([p + X * (hw * math.cos(2 * math.pi * k / n)) + nrm * (ht * math.sin(2 * math.pi * k / n))
                      for k in range(n)])
    P.loft(rings, slot)


def backpack():
    P = Part("Char_Backpack")
    # local space: x right, f forward (character front). Back face = plane f = 0; the pack extends to -f, hangs to -z.
    prof = [(-.005, .150, .060), (-.03, .163, .080), (-.12, .168, .088), (-.28, .166, .088), (-.40, .150, .078),
            (-.455, .115, .050)]
    pad = lambda z: .026 * (1 - sm(-z / .38))      # back panel follows the curve of the upper back
    P.loft([ring_z(z, 0, -hd + pad(z), hw, hd, 22, 3.0) for z, hw, hd in prof], "Bag")
    # top lid
    P.loft([ring_z(z, 0, -hd + pad(z), hw, hd, 22, 3.0) for z, hw, hd in
            [(-.005, .156, .064), (-.02, .171, .090), (-.115, .174, .095), (-.17, .166, .092), (-.19, .150, .072)]], "Bag")
    # front pocket
    P.loft([ring_z(z, 0, -.178, hw, hd, 18, 3.4) for z, hw, hd in
            [(-.15, .095, .006), (-.17, .112, .03), (-.30, .115, .036), (-.37, .105, .02), (-.385, .085, .006)]], "Bag")
    # bottle pockets
    for s in (-1, 1):
        P.loft([ring_z(z, s * .170, -.085, r, r * .9, 12, 2.2) for z, r in
                [(-.20, .02), (-.23, .038), (-.33, .040), (-.37, .030), (-.38, .01)]], "Bag")
    # top carry handle
    P.tube([V(-.04, -.085, -.01), V(-.04, -.085, .018), V(0, -.085, .028), V(.04, -.085, .018), V(.04, -.085, -.01)],
           [.007] * 5, "Bag", n=8)
    # shoulder straps: back face -> over the shoulder -> chest
    path = [(.085, .02, -.06), (.088, .035, -.01), (.092, .08, .03), (.098, .13, .036), (.106, .19, .005),
            (.112, .232, -.07), (.120, .238, -.16), (.150, .215, -.24), (.182, .13, -.27), (.178, .05, -.27), (.168, 0.0, -.26)]
    for s in (-1, 1):
        ribbon(P, [V(s * x, f, z) for x, f, z in path], .020, .004, "Bag")
    return P


# ------------------------------------------------------------------ main
def export_obj(ob, name):
    ob.name = name; ob.data.name = name
    C.export_fbx([ob], "Characters", name)


def sheet(body, rig, made, names, poses, rots, path, az=0, el=6, spacing=.95):
    """One row of dressed characters. names: headwear/pack names; poses: pose names; rots: z rotation (deg) each."""
    objs, extra = [], []
    n = len(poses)
    for i in range(n):
        b2, r2 = instance(body, rig, ((i - (n - 1) / 2) * spacing, 0, 0))
        r2.rotation_euler.z = math.radians(rots[i])
        pose(r2, POSES[poses[i]])
        objs.append(b2); extra.append(r2)
        for nm in names:
            o = clone_into_scene(made[nm])
            if nm == "Char_Backpack":
                place(o, r2, "Body", Matrix.Translation(V(0, -.13, 1.50)))
            else:
                place(o, r2, "Head", Matrix.Translation(NECK_BASE))
            objs.append(o)
    render_view(objs, path, az=az, el=el, size=(420 * n, 840), target=(0, 0, .95), scale=n * spacing + .2, floor_size=5)
    for o in objs + extra:
        bpy.data.objects.remove(o, do_unlink=True)


def main():
    C.reset_scene()
    body, rig = build_body()
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o is not body:
            bpy.data.objects.remove(o, do_unlink=True)
    made = {}
    for nm, fn in (("Char_HairShort", hair_short), ("Char_HairLong", hair_long), ("Char_Hood", hood), ("Char_Cap", cap)):
        made[nm] = finish_head_part(fn(), nm)
    made["Char_Backpack"] = backpack().build(smooth=45, name="Char_Backpack")
    for nm, ob in made.items():
        ob.hide_render = True
        C.report(ob)
        C.export_fbx([ob], "Characters", nm)
    P = C.PREVIEWS
    for nm, ob in made.items():           # individual previews (pivot at origin; shown at its place on the body)
        ob.hide_render = False
        if nm == "Char_Backpack":
            ob.location = V(0, -.13, 1.50)
        else:
            ob.location = NECK_BASE
        bpy.context.view_layer.update()
        render_view([ob, body], os.path.join(P, nm + ".png"), az=35, el=10, size=(900, 900), scale=.62 if nm != "Char_Backpack" else .9,
                    target=(0, 0, 1.64) if nm != "Char_Backpack" else (0, -.2, 1.3), floor=False)
        render_view([ob], os.path.join(P, nm + "_alone.png"), az=35, el=15, size=(700, 700), scale=.7 if nm != "Char_Backpack" else .8,
                    target=(0, 0, .15 + (0 if nm == "Char_Backpack" else 1.65 - .15 * 0) * 0) if False else (ob.location + Vector((0, 0, .1 if nm != "Char_Backpack" else -.22))))
        ob.hide_render = True
        ob.location = (0, 0, 0)
    sets = {"CapPack": ["Char_Cap", "Char_Backpack"], "LongHair": ["Char_HairLong"], "Hood": ["Char_Hood"],
            "ShortHair": ["Char_HairShort", "Char_Backpack"]}
    for key, names in sets.items():
        sheet(body, rig, made, names, ["rest"] * 4, [0, 50, 90, 180], os.path.join(P, "Assembly_%s_stand.png" % key))
        sheet(body, rig, made, names, ["combined", "legs_fwd_back", "head_yaw", "arms_raised_fwd"], [30, 30, 30, 30],
              os.path.join(P, "Assembly_%s_posed.png" % key))


if __name__ == "__main__":
    main()
