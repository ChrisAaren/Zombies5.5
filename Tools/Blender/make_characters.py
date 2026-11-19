"""Character body parts for Zombies 5.5 -> Assets/Art/Models/Characters/*.fbx
Run: blender --background --factory-startup --python make_characters.py
Parts are separate meshes with pivots at the joints (see ASSET_CONTRACT.md).
Part space: x right, f forward (Unity +Z), z up; V(x, f, z) maps to Blender coordinates.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *


# ------------------------------------------------------------------ torso (pivot: hips center)
def make_torso():
    p = Part("Char_Torso")
    # (z, half width, half depth, forward offset)
    R = [(-.09, .15, .10, 0), (-.04, .185, .115, 0), (0.0, .19, .12, 0), (.08, .178, .116, 0), (.12, .17, .114, 0),
         (.22, .155, .10, 0), (.34, .175, .11, .005), (.44, .205, .125, .01), (.50, .215, .125, .008),
         (.545, .22, .105, 0), (.575, .17, .09, 0), (.595, .095, .07, 0), (.60, .07, .06, 0)]
    rings = [ring_z(z, 0, cf, hw, hd, 22, 2.4) for z, hw, hd, cf in R]
    # Pants for the hip / belt area, shirt above
    p.loft(rings, lambda i, j: "Pants" if (R[i][0] + R[i + 1][0]) / 2 < .09 else "Top")
    return p.build(smooth=50)


# ------------------------------------------------------------------ head (pivot: neck base)
HEAD = [(.045, .04, .045, .06), (.085, .062, .078, .045), (.13, .072, .093, .03), (.17, .078, .10, .02),
        (.205, .079, .105, .014), (.24, .076, .10, .012), (.265, .068, .092, .008), (.285, .05, .07, .004),
        (.30, .018, .025, 0)]


def make_head():
    p = Part("Char_Head")
    # neck
    p.loft([ring_z(z, 0, cf, r, r * 1.02, 14) for z, r, cf in ((-.04, .05, -.005), (.03, .047, -.005), (.09, .046, 0))], "Skin")
    # skull + jaw
    p.loft([ring_z(z, 0, cf, hw, hd, 22, 2.25) for z, hw, hd, cf in HEAD], "Skin")
    # face features
    p.sphere(V(0, .122, .163), (.012, .02, .027), "Skin", (10, 8), Euler((math.radians(-25), 0, 0)))   # nose bridge
    p.sphere(V(0, .136, .15), (.014, .014, .012), "Skin", (10, 8))                                        # nose tip
    p.sphere(V(0, .106, .222), (.056, .017, .011), "Skin", (12, 8))                                       # brow ridge
    for s in (-1, 1):
        p.sphere(V(s * .035, .102, .197), (.0135, .0095, .0135), "Eye", (10, 8))                          # eyes
        p.sphere(V(s * .079, .018, .18), (.008, .02, .03), "Skin", (8, 6), Euler((0, math.radians(s * 8), 0)))  # ears
    p.sphere(V(0, .1, .085), (.026, .018, .016), "Skin", (8, 6))                                           # chin
    return p.build(smooth=60)


def shell_rings(z_list, grow=1.07, extra=.004):
    """Head cross-sections inflated for hair / cap shells."""
    f = {k: pchip([(h[0], h[k]) for h in HEAD]) for k in (1, 2, 3)}
    return [ring_z(z, 0, f[3](z), f[1](z) * grow + extra, f[2](z) * grow + extra, 22, 2.25) for z in z_list]


def make_hair(long):
    p = Part("Char_HairLong" if long else "Char_HairShort")
    zs = [.15, .17, .205, .24, .265, .285, .30]
    rings = shell_rings(zs)
    if long:
        rings = [ring_z(-.03, 0, -.055, .07, .07, 22, 2.25), ring_z(.04, 0, -.045, .09, .085, 22, 2.25),
                 ring_z(.10, 0, -.01, .088, .1, 22, 2.25)] + shell_rings([.13] + zs[1:])
    rings.append(ring_z(.312, 0, 0, .014, .018, 22, 2.25))
    p.loft(rings, "Hair", False, True)
    limit = -.01 if long else .02          # front of head left bare below the hairline
    p.drop_faces(lambda c: (-c.y) > limit and c.z < .235)
    return p.build(solidify=.008, smooth=50)


def make_cap():
    p = Part("Char_Cap")
    rings = shell_rings([.20, .215, .24, .265, .285, .30], 1.09, .006)
    rings.append(ring_z(.318, 0, 0, .014, .018, 22, 2.25))
    p.loft(rings, "Cap", False, True)
    p.sphere(V(0, 0, .321), (.011, .011, .007), "Cap", (8, 6))
    dome = p.build(solidify=.006, smooth=50, name="cap_dome")
    # brim: curved visor shell in front of the forehead
    b = Part("brim")
    steps = [math.radians(a) for a in range(-80, 81, 16)]
    for a0, a1 in zip(steps[:-1], steps[1:]):
        def pt(a, out):
            return V(math.sin(a) * (.092 + out * .03), math.cos(a) * (.115 + out * .1) + .0, .22 - out * .012)
        b.face([pt(a0, 0), pt(a1, 0), pt(a1, 1), pt(a0, 1)], "Cap")
    brim = b.build(solidify=.006, smooth=0, name="cap_brim")
    return join([dome, brim], "Char_Cap")


# ------------------------------------------------------------------ arm (pivot: shoulder)
def make_arm():
    p = Part("Char_Arm")
    S = [(.04, .042, .044), (.01, .057, .06), (-.08, .058, .06), (-.20, .05, .05), (-.30, .046, .046),
         (-.42, .042, .043), (-.49, .037, .039), (-.505, .039, .041)]
    p.loft([ring_z(z, 0, 0, rx, rf, 14) for z, rx, rf in S], "Top", True, False)   # sleeve with cuff
    H = [(-.50, .034, .034), (-.53, .043, .021), (-.58, .046, .022), (-.63, .041, .02), (-.655, .02, .012), (-.66, 0, 0)]
    p.loft([ring_z(z, 0, 0, hw, hd, 14) for z, hw, hd in H], "Skin", False, False)  # mitten hand
    p.sphere(V(0, .03, -.55), (.014, .016, .035), "Skin", (8, 6), Euler((math.radians(-12), 0, 0)))  # thumb
    return p.build(smooth=60)


# ------------------------------------------------------------------ leg (pivot: hip joint)
def make_leg():
    p = Part("Char_Leg")
    L = [(.05, .075, .08, 0), (0.0, .095, .1, 0), (-.12, .1, .105, 0), (-.30, .085, .09, 0), (-.50, .066, .07, 0),
         (-.62, .068, .072, -.008), (-.80, .048, .052, 0), (-.86, .044, .048, 0), (-.9, .047, .051, 0)]
    p.loft([ring_z(z, 0, cf, rx, rf, 16) for z, rx, rf, cf in L], "Pants", True, False)
    # shoe: rings along the forward axis (heel -> toe), flat sole at z = -0.95
    Sh = [(-.09, .035, .045), (-.06, .042, .056), (0.0, .046, .06), (.08, .05, .048), (.17, .052, .036),
          (.24, .046, .03), (.28, .03, .022), (.292, .008, .008)]
    p.loft([ring_f(f, 0, -.95 + hh, hw, hh, 14, 3.0) for f, hw, hh in Sh], "Shoe", True, True)
    p.loft([ring_f(f, 0, -.942, hw + .004, .008, 14, 3.0) for f, hw, hh in Sh[:-1]], "Shoe", True, True)  # sole welt
    return p.build(smooth=60)


# ------------------------------------------------------------------ backpack (pivot: top center of back face)
def make_backpack():
    p = Part("Char_Backpack")
    p.rbox(V(0, -.085, -.21), (.32, .17, .42), "Bag", .04, 3)                 # main body
    p.rbox(V(0, -.095, -.07), (.325, .19, .14), "Bag", .035, 3)               # top flap
    for s in (-1, 1):
        p.rbox(V(s * .17, -.095, -.3), (.06, .12, .2), "Bag", .02, 2)         # side pockets
    p.rbox(V(0, -.19, -.28), (.22, .05, .16), "Bag", .02, 2)                  # front pocket
    p.rbox(V(0, -.2, -.1), (.04, .02, .06), "Bag", .005, 1)                   # buckle
    return p.build(smooth=45)


# ------------------------------------------------------------------ build / assemble
EXPECT = {
    "Char_Torso": dict(zmax=.60, w=.44),
    "Char_Head": dict(zmax=.30),
    "Char_Arm": dict(zmin=-.66),
    "Char_Leg": dict(zmin=-.95),
}


def main():
    reset_scene()
    makers = [make_torso, make_head, lambda: make_hair(False), lambda: make_hair(True), make_cap, make_arm, make_leg,
              make_backpack]
    objs = {}
    for mk in makers:
        ob = mk()
        objs[ob.name] = ob
        u, umin, umax = report(ob)
        export_fbx([ob], "Characters", ob.name)
    for k, ex in EXPECT.items():
        u, umin, umax = report(objs[k])
        if "zmax" in ex and abs(umax[1] - ex["zmax"]) > .005: print("  WARNING", k, "top", umax[1])
        if "zmin" in ex and abs(umin[1] - ex["zmin"]) > .005: print("  WARNING", k, "bottom", umin[1])
        if "w" in ex and abs(u[0] - ex["w"]) > .02: print("  WARNING", k, "width", u[0])
    for name, ob in objs.items():
        render_preview([ob], os.path.join(PREVIEWS, name + ".png"), size=(600, 800), az=35, el=15)
    assemble(objs)


def place(src, loc, rot=(0, 0, 0)):
    c = src.copy()
    c.location = loc
    c.rotation_euler = Euler(rot, 'XYZ')
    bpy.context.scene.collection.objects.link(c)
    bpy.context.view_layer.update()
    return c


def assemble(objs, ):
    """Render the character at the rig placement: standing / mid-stride, cap+backpack / long hair."""
    for o in objs.values():
        bpy.context.scene.collection.objects.unlink(o)
    mat("Skin")
    for stride in (False, True):
        for look in ("cap", "hair"):
            placed = []
            H = .95
            leg_sw = 35 if stride else 0
            arm_sw = 30 if stride else 0
            r = math.radians
            # forward swing = negative rotation about X (forward is -Y in Blender)
            for sx, sgn in ((-1, 1), (1, -1)):   # left leg forward, right leg back
                placed.append(place(objs["Char_Leg"], (sx * .11, 0, H), (r(-leg_sw * sgn), 0, 0)))
                placed.append(place(objs["Char_Arm"], (-sx * .21, 0, H + .55), (r(-arm_sw * sgn), r(sx * 8), 0)))
            placed.append(place(objs["Char_Torso"], (0, 0, H)))
            placed.append(place(objs["Char_Head"], (0, 0, H + .6)))
            if look == "cap":
                placed.append(place(objs["Char_Cap"], (0, 0, H + .6)))
                placed.append(place(objs["Char_Backpack"], (0, .13, H + .55)))
            else:
                placed.append(place(objs["Char_HairLong"], (0, 0, H + .6)))
            tag = ("Stride" if stride else "Stand") + ("_CapPack" if look == "cap" else "_LongHair")
            for az in (35, 215):
                render_preview(placed, os.path.join(PREVIEWS, "Assembly_%s_%d.png" % (tag, az)), az=az, el=8,
                               size=(700, 900))
            for o in placed:
                bpy.data.objects.remove(o, do_unlink=True)


main()
