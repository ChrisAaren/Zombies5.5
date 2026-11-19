"""Street props -> Assets/Art/Models/Props/*.fbx  (pivot bottom center, front = Unity +Z)
Run: blender --background --factory-startup --python make_props.py
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *

AX_X = Euler((0, math.radians(90), 0))        # rotates a Z-axis cylinder onto X
AX_F = Euler((math.radians(90), 0, 0))        # ... onto forward (Blender -Y)


def street_lamp():
    p = Part("StreetLamp")
    p.lathe([(.17, 0), (.17, .1), (.11, .2), (.075, .38), (.055, .5)], "Metal", n=16, cap_ends=True)
    p.lathe([(.075, .46), (.075, .52), (.065, .56)], "Metal", n=16)
    p.lathe([(.055, .5), (.045, 4.4)], "Metal", n=14)
    p.lathe([(.075, 1.2), (.075, 1.26)], "Metal", n=14)
    arm = []
    for i in range(9):                                # curved arm: rises then bends toward the front
        a = math.pi / 2 * i / 8
        arm.append(V(0, .85 * (1 - math.cos(a)), 4.35 + .5 * math.sin(a)))
    p.tube(arm, [.045 - .008 * i / 8 for i in range(9)], "Metal", 10)
    p.rbox(V(0, .95, 4.915), (.26, .6, .1), "Metal", .035, 2)                   # lamp housing
    p.rbox(V(0, .95, 4.86), (.2, .46, .03), "LampGlass", .01, 1)                # lens
    p.sphere(V(0, .66, 4.9), (.07, .07, .07), "Metal", (10, 6))                 # arm-to-head knuckle
    return p.build(smooth=40)


def bench():
    p = Part("Bench")
    for sx in (-1, 1):
        x = sx * .8
        p.bar(V(x, .2, 0), V(x, .2, .46), .045, .045, "Metal", .008)                    # front leg
        p.bar(V(x, -.2, 0), V(x, -.31, .86), .045, .045, "Metal", .008)                 # rear leg (leans back)
        p.bar(V(x, .21, .43), V(x, -.26, .43), .045, .04, "Metal", .008)                # seat rail
        p.bar(V(x, .2, .46), V(x, .2, .68), .04, .04, "Metal", .008)                    # arm post
        p.bar(V(x, .21, .67), V(x, -.31, .67), .05, .035, "Metal", .008)                 # armrest
        p.bar(V(x, .26, .02), V(x, .1, .02), .05, .04, "Metal", .01)                    # feet
        p.bar(V(x, -.17, .02), V(x, -.3, .02), .05, .04, "Metal", .01)
    p.bar(V(-.8, .0, .2), V(.8, .0, .2), .03, .03, "Metal", .005)                       # stretcher
    for f in (-.165, -.055, .055, .165):                                                # seat slats
        p.rbox(V(0, f, .45), (1.76, .1, .035), "Wood", .007, 1)
    tilt = math.atan(.11 / .86)
    for z in (.58, .69, .8):                                                            # backrest slats
        p.rbox(V(0, -.2 - .128 * z - .03, z), (1.76, .03, .1), "Wood", .007, 1, Euler((-tilt, 0, 0)))
    return p.build(smooth=40)


def hydrant():
    p = Part("Hydrant")
    p.lathe([(0, 0), (.165, 0), (.165, .05), (.115, .09), (.1, .13), (.1, .36), (.125, .38), (.125, .44), (.1, .46),
             (.1, .52), (.125, .54), (.125, .56), (.09, .58), (.08, .64), (.055, .7), (.03, .735), (0, .745)], "Paint", n=20)
    p.cyl(V(0, 0, .74), .03, .03, .02, "Metal", 10)                                        # top nut
    for sx in (-1, 1):
        p.cyl(V(sx * .15, 0, .32), .045, .045, .2, "Paint", 12, AX_X)                      # side nozzles
        p.cyl(V(sx * .255, 0, .32), .055, .055, .03, "Metal", 12, AX_X)
    p.cyl(V(0, .14, .29), .06, .06, .2, "Paint", 14, AX_F)                                 # pumper nozzle (front)
    p.cyl(V(0, .255, .29), .072, .072, .035, "Metal", 14, AX_F)
    return p.build(smooth=45)


def trash_can():
    p = Part("TrashCan")
    p.lathe([(0, .02), (.19, .02), (.21, 0), (.215, .05), (.225, .06), (.265, .8), (.285, .8), (.285, .835),
             (.27, .845), (.245, .845), (.22, .9), (.12, .935), (0, .94)], "Metal", n=20)
    for z in (.3, .55):                              # reinforcing bands (wall radius grows with height)
        r = .225 + (z - .06) * .054
        p.lathe([(r, z - .02), (r + .014, z), (r, z + .02)], "Metal", n=20)
    p.rbox(V(0, .255, .7), (.24, .03, .14), "Interior", .02, 1)                            # dark opening on the front
    p.tube([V(-.05, 0, .925), V(-.045, 0, .95), V(0, 0, .957), V(.045, 0, .95), V(.05, 0, .925)], [.012] * 5, "Metal", 6)
    return p.build(smooth=45)


def bollard():
    p = Part("Bollard")
    p.lathe([(0, 0), (.15, 0), (.15, .05), (.115, .09), (.1, .12), (.1, .75), (.092, .83), (.06, .88), (0, .9)], "Concrete", n=20)
    p.lathe([(.104, .68), (.104, .76)], "Paint", n=20)
    return p.build(smooth=45)


def planter():
    p = Part("Planter")
    p.rbox(V(0, .215, .21), (1.2, .07, .42), "Concrete", .012, 1)
    p.rbox(V(0, -.215, .21), (1.2, .07, .42), "Concrete", .012, 1)
    for sx in (-1, 1):
        p.rbox(V(sx * .565, 0, .21), (.07, .5, .42), "Concrete", .012, 1)
    p.rbox(V(0, 0, .03), (1.16, .46, .06), "Concrete", .01, 1)
    p.rbox(V(0, 0, .36), (1.06, .36, .07), "Bark", 0, 1)                                    # soil
    for x, f, r, s in ((-.38, 0, .16, 1), (-.12, .04, .15, 2), (.14, -.04, .17, 3), (.4, .02, .14, 4)):
        noise_blob(p, V(x, f, .42), (r + .04, r, r * .9), "Leaf", s, .3, 1.5, 1)
    return p.build(smooth=40, flat=("Leaf",))


def crate():
    p = Part("Crate")
    p.rbox(V(0, 0, .55), (1.0, 1.0, 1.0), "Wood", 0, 1)                                     # core
    for sx in (-1, 1):
        for sf in (-1, 1):
            p.rbox(V(sx * .5, sf * .5, .55), (.1, .1, 1.1), "Wood", .01, 1)                 # corner posts
    for z in (.2, .55, .9):
        for sd in (-1, 1):
            p.rbox(V(0, sd * .535, z), (.9, .03, .26), "Wood", .006, 1)                     # front/back boards
            p.rbox(V(sd * .535, 0, z), (.03, .9, .26), "Wood", .006, 1)                     # side boards
    ang = math.atan2(.86, .8)
    for sd in (-1, 1):                                                                       # diagonal braces
        p.rbox(V(0, sd * .55, .55), (1.17, .02, .08), "Wood", .004, 1, Euler((0, -ang, 0)))
        p.rbox(V(sd * .55, 0, .55), (.02, 1.17, .08), "Wood", .004, 1, Euler((ang, 0, 0)))
    for i in range(3):                                                                       # lid planks (flat top at 1.1)
        p.rbox(V(0, (i - 1) * .34, 1.08), (1.1, .32, .04), "Wood", .006, 1)
    return p.build(smooth=40)


def fountain():
    p = Part("Fountain")
    N = 40
    basin = [(0, 0), (3.9, 0), (4.0, .05), (4.0, .55), (3.9, .62), (3.65, .62), (3.58, .55), (3.5, .2), (3.3, .15), (0, .15)]
    p.lathe(basin, "Concrete", n=N)
    ped = [(0, .1), (.95, .1), (.9, .3), (.6, .45), (.42, .7), (.32, 1.0), (.3, 1.15), (.28, 1.3)]
    p.lathe(ped + [(0, 1.3)], "Concrete", n=N)
    mid = [(0, .95), (.5, .95), (1.4, 1.08), (1.9, 1.35), (1.95, 1.48), (1.82, 1.48), (1.5, 1.28), (1.0, 1.18), (0, 1.15)]
    p.lathe(mid, "Concrete", n=N)
    p.lathe([(0, 1.1), (.27, 1.1), (.25, 1.8), (.32, 1.95), (.4, 2.0), (0, 2.0)], "Concrete", n=N)   # upper column
    top = [(0, 2.0), (.35, 2.05), (1.0, 2.38), (1.25, 2.7), (1.2, 2.75), (1.05, 2.68), (.6, 2.52), (0, 2.48)]
    p.lathe(top, "Concrete", n=N)
    stone = p.build(smooth=35, radial=True, name="Fountain")
    w = Part("Fountain_Water")
    w.lathe([(0, .38), (3.55, .38), (3.55, .42), (0, .42)], "Water", n=N)             # basin water
    w.lathe([(0, 1.3), (1.62, 1.3), (1.62, 1.35), (0, 1.35)], "Water", n=N)           # mid bowl water
    w.lathe([(0, 2.55), (1.0, 2.55), (1.0, 2.6), (0, 2.6)], "Water", n=N)             # top bowl water
    w.lathe([(.07, 2.58), (.06, 3.1), (.03, 3.3), (0, 3.35)], "Water", n=10)          # jet
    water = w.build(smooth=30, name="Fountain_Water")
    return stone, water


def main():
    jobs = [("StreetLamp", street_lamp, (None, 5.0, None)), ("Bench", bench, (1.8, .85, .5)),
            ("Hydrant", hydrant, (None, .75, None)), ("TrashCan", trash_can, (None, .95, None)),
            ("Bollard", bollard, (None, .9, None)), ("Planter", planter, (1.2, .6, .5)),
            ("Crate", crate, (1.1, 1.1, 1.1)), ("Fountain", fountain, (8.0, None, 8.0))]
    for name, fn, exp in jobs:
        reset_scene()
        r = fn()
        objs = list(r) if isinstance(r, tuple) else [r]
        for o in objs:
            report(o, exp if o.name == name else None, .08)
            export_fbx([o], "Props", o.name)
        render_preview(objs, os.path.join(PREVIEWS, name + ".png"), az=35, el=20, size=(800, 800))


main()
