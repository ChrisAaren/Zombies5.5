"""Gritty street dressing -> Assets/Art/Models/Props/*.fbx  (see ASSET_CONTRACT.md, "Gritty urban look")
Run: blender --background --factory-startup --python make_props_gritty.py
Forward = Unity +Z = Blender -Y; V(x, forward, up). Previews: Tools/Blender/previews/Gritty_<Name>.png"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *

COLORS.update({"SignPanel": (.55, .2, .15), "SignText": (.95, .85, .5), "TrashBag": (.04, .045, .05)})

AX_X = Euler((0, math.radians(90), 0))
AX_F = Euler((math.radians(90), 0, 0))


def fire_escape():
    """One 3.2 m floor segment. Pivot bottom centre of the facade's outer wall plane (wall at f=0), spans
    x -1.4..1.4, protrudes 0.96 m (stair to 1.56 m). Deck top at y=0.9; a stair descends left to right to the
    deck of the segment below (y=-2.3). Wires/colliders: none."""
    p = Part("FireEscape_Floor"); M = "Metal"
    y0 = .9
    p.rbox(V(0, .48, y0 - .03), (2.8, .96, .04), M, .006, 1)                            # deck
    for x in (-1.4, -.7, 0, .7, 1.4):
        p.bar(V(x, .02, y0 - .075), V(x, .94, y0 - .075), .05, .06, M)                     # deck joists
    p.bar(V(-1.4, .94, y0 - .075), V(1.4, .94, y0 - .075), .05, .06, M)
    for x in (-1.0, 1.0):                                                                   # wall brackets
        p.bar(V(x, 0, y0 - .9), V(x, .9, y0 - .08), .04, .05, M)
        p.bar(V(x, 0, y0 - .9), V(x, -.06, y0 - .9), .06, .06, M)
    # front railing with gaps at both ends (stair top on the left, stair foot on the right)
    zt = y0 + 1.0
    for x in (-.8, -.4, 0, .4, .8):
        p.bar(V(x, .94, y0), V(x, .94, zt), .04, .04, M)
    p.bar(V(-.9, .94, zt), V(.9, .94, zt), .045, .045, M)
    p.bar(V(-.9, .94, y0 + .5), V(.9, .94, y0 + .5), .035, .035, M)
    for i in range(1, 12):
        x = -.8 + i * (1.6 / 12)
        p.bar(V(x, .94, y0), V(x, .94, y0 + .5), .015, .015, M)
    # side railings (both ends) up against the wall
    for sx in (-1.4, 1.4):
        for f in (.0, .48, .94):
            p.bar(V(sx, f, y0), V(sx, f, zt), .04, .04, M)
        p.bar(V(sx, 0, zt), V(sx, .94, zt), .045, .045, M)
        p.bar(V(sx, 0, y0 + .5), V(sx, .94, y0 + .5), .035, .035, M)
    # stair flight in front of the deck, descending +x
    xs, xe = -1.15, 1.15
    zs, ze = y0, y0 - 3.2
    f0, f1 = 1.0, 1.56
    for k in (f0, f1):
        p.bar(V(xs, k, zs - .06), V(xe, k, ze - .06), .045, .12, M)                        # stringers
    n = 13
    for i in range(n):
        t = i / (n - 1)
        x = xs + (xe - xs) * t
        z = zs + (ze - zs) * t
        p.rbox(V(x, (f0 + f1) / 2, z), (.22, f1 - f0, .03), M, .004, 1)                      # treads
    for t in (0.0, 0.5, 1.0):                                                               # handrail posts + rail
        x = xs + (xe - xs) * t; z = zs + (ze - zs) * t
        p.bar(V(x, f1, z), V(x, f1, z + .95), .04, .04, M)
    p.bar(V(xs, f1, zs + .95), V(xe, f1, ze + .95), .045, .045, M)
    # landing platform joining stair top to the deck
    p.rbox(V(-1.15, (f0 + f1) / 2, y0 - .03), (.5, f1 - f0 + .1, .04), M, .006, 1)
    return p.build(smooth=40, flat=())


def utility_pole():
    """9.2 m wooden pole, pivot at the foot. Crossarm along local X; wires run along local Z.
    Wire attach points (local): (-0.9, 8.62, 0), (0, 8.62, 0), (0.9, 8.62, 0), (0, 9.18, 0)."""
    p = Part("UtilityPole")
    p.lathe([(0, 0), (.17, 0), (.165, .6), (.14, 4.0), (.115, 8.0), (.1, 9.1), (0, 9.15)], "Wood", n=14)
    p.rbox(V(0, 0, 8.4), (2.2, .1, .12), "Wood", .01, 1)                                    # crossarm
    for sx in (-1, 1):
        p.bar(V(sx * .15, .06, 7.85), V(sx * .8, .06, 8.37), .05, .06, "Wood")              # braces
        p.cyl(V(sx * .9, 0, 8.52), .03, .045, .16, "Metal", 8)                              # insulators
    p.cyl(V(0, 0, 8.52), .03, .045, .16, "Metal", 8)
    p.cyl(V(0, 0, 9.12), .03, .045, .12, "Metal", 8)
    p.cyl(V(.28, .1, 7.3), .2, .2, .62, "Metal", 14)                                        # transformer can
    p.cyl(V(.28, .1, 7.63), .21, .15, .06, "Metal", 14)
    for i in range(7):                                                                      # climbing pegs / bolts
        p.bar(V(-.1, .08, 2.0 + i * .5), V(-.1, .2, 2.0 + i * .5), .025, .025, "Metal")
    p.bar(V(-.16, .0, 2.0), V(-.16, .0, 6.4), .02, .02, "Metal")                            # grounding strap
    p.rbox(V(0, .17, 1.6), (.22, .02, .3), "Metal", .004, 1)                                # utility tag plate
    return p.build(smooth=40)


def wire():
    """1 m along +X, sagging below the end points (y: 0 at both ends, -0.02 mid). Unity scales it UNIFORMLY by
    the span length so sag stays 2% of the span; the tube radius is authored tiny to compensate."""
    p = Part("Wire")
    pts = []
    for i in range(17):
        t = i / 16
        pts.append(V(t, 0, -.02 * 4 * t * (1 - t) * 1.0))
    p.tube(pts, [.0006] * 17, "Rubber", 5)
    return p.build(smooth=60)


def dumpster():
    """1.8 x 1.1 x 1.3 m, front +Z, pivot bottom centre."""
    p = Part("Dumpster")
    p.rbox(V(0, 0, .72), (1.8, 1.1, .92), "Paint", .025, 2)
    p.rbox(V(0, 0, .2), (1.7, 1.0, .08), "Metal", .02, 1)                                   # base frame
    for sx in (-.85, .85):
        for sf in (-.5, .5):
            p.cyl(V(sx, sf, .1), .09, .09, .08, "Rubber", 10, AX_X)                          # wheels
    for x in (-.6, -.2, .2, .6):
        for sf in (-1, 1):
            p.rbox(V(x, sf * .56, .72), (.07, .045, .84), "Paint", .008, 1)                  # ribs
    for sf in (-1, 1):
        p.rbox(V(0, sf * .57, 1.2), (1.84, .06, .05), "Metal", .01, 1)                       # rim
    for sx in (-1, 1):
        p.rbox(V(sx * .92, 0, 1.2), (.06, 1.16, .05), "Metal", .01, 1)
    p.rbox(V(0, .275, 1.235), (1.8, .55, .035), "Metal", .008, 1, Euler((math.radians(-4), 0, 0)))   # closed front lid
    p.rbox(V(0, -.6, 1.49), (1.8, .55, .035), "Metal", .008, 1, Euler((math.radians(-100), 0, 0)))   # open back lid
    p.rbox(V(0, .575, .5), (.3, .02, .12), "Interior", .004, 1)                              # forklift pocket
    return p.build(smooth=30)


def trash_bags():
    p = Part("TrashBags")
    for i, (x, f, r, s) in enumerate(((-.25, 0, .3, 1), (.2, .08, .27, 2), (0, -.3, .26, 3), (.38, -.2, .22, 4), (-.35, .3, .2, 5))):
        noise_blob(p, V(x, f, r * .85), (r * 1.1, r, r * .85), "TrashBag", s, .18, 1.6, 2, flat_below=0.0)
        p.cyl(V(x, f, r * 1.55), .035, .005, .08, "TrashBag", 6)                              # tied neck
    return p.build(smooth=60)


def news_box():
    p = Part("NewsBox")
    for sx in (-.17, .17):
        p.bar(V(sx, 0, 0), V(sx, 0, .45), .06, .06, "Metal", .008)                          # legs
    p.rbox(V(0, 0, .78), (.5, .44, .62), "Paint", .02, 2)                                   # body
    p.rbox(V(0, 0, 1.12), (.54, .48, .04), "Metal", .01, 1)                                 # lid
    p.rbox(V(0, .222, .9), (.38, .01, .3), "Glass", .004, 1)                                # window
    p.rbox(V(0, .226, .62), (.2, .014, .12), "Metal", .004, 1)                              # coin plate
    p.rbox(V(0, .23, .55), (.1, .02, .03), "Interior", .002, 1)
    return p.build(smooth=35)


def manhole():
    p = Part("Manhole")
    p.lathe([(.36, 0), (.36, .014), (.31, .014), (.31, .0)], "Metal", n=32)                 # frame
    p.lathe([(0, .02), (.31, .02), (.31, .0), (0, .0)], "Metal", n=32)                      # cover
    p.lathe([(.24, .02), (.24, .028), (.22, .028), (.22, .02)], "Metal", n=32)              # rings
    p.lathe([(.14, .02), (.14, .028), (.12, .028), (.12, .02)], "Metal", n=32)
    for i in range(6):
        a = i * math.pi / 6
        p.rbox(V(0, 0, .024), (.5, .028, .008), "Metal", 0, 1, Euler((0, 0, a)))            # ribs
    return p.build(smooth=25)


def storm_grate():
    """0.9 x 0.5 m curbside drain, bars run along forward. Pivot bottom centre, top at 0.03."""
    p = Part("StormGrate")
    p.rbox(V(0, 0, .004), (.9, .5, .008), "Interior", 0, 1)
    for sx in (-.43, .43):
        p.rbox(V(sx, 0, .016), (.04, .5, .032), "Metal", .004, 1)
    for sf in (-.23, .23):
        p.rbox(V(0, sf, .016), (.9, .04, .032), "Metal", .004, 1)
    x = -.38
    while x < .39:
        p.rbox(V(x, 0, .014), (.028, .46, .028), "Metal", .003, 1)
        x += .065
    return p.build(smooth=25)


def ac_window_unit():
    """Window air conditioner. Pivot bottom centre at the wall plane; body protrudes to f = 0.5."""
    p = Part("AC_WindowUnit")
    p.rbox(V(0, .25, .2), (.68, .5, .4), "Paint", .025, 2)
    p.rbox(V(0, .505, .2), (.52, .012, .28), "Interior", .004, 1)
    for i in range(6):
        p.rbox(V(0, .52, .09 + i * .045), (.5, .02, .014), "Metal", .002, 1)               # fins
    p.rbox(V(.26, .51, .35), (.1, .014, .05), "Metal", .004, 1)                              # control plate
    p.rbox(V(0, -.04, .22), (.5, .08, .3), "Metal", .01, 1)                                  # rear housing in the window
    p.bar(V(-.25, .0, .0), V(-.25, .3, .0), .03, .03, "Metal")
    return p.build(smooth=35)


SIGNS = ["DELGADO HARDWARE", "LUCKY NOODLE", "ORTIZ PAWN", "CORNER DELI", "BLUEBIRD LAUNDROMAT", "REX PHARMACY"]


def sign(i):
    """Storefront sign 2.6 x 0.6 m, pivot bottom centre, back at the wall plane (f=0), lettering out to f=0.17."""
    name = "Storefront_Sign_%d" % i
    text = SIGNS[i - 1]
    p = Part(name)
    p.rbox(V(0, .06, .3), (2.6, .12, .6), "SignPanel", .015, 1)
    for z in (.03, .57):
        p.rbox(V(0, .13, z), (2.54, .02, .04), "Metal", .004, 1)
    for x in (-1.27, 1.27):
        p.rbox(V(x, .13, .3), (.04, .02, .56), "Metal", .004, 1)
    for x in (-.9, .9):
        p.bar(V(x, -.04, .6), V(x, .0, .62), .04, .04, "Metal")                              # hangers
    panel = p.build(smooth=30)
    cu = bpy.data.curves.new("txt", 'FONT')
    cu.body = text
    cu.align_x = 'CENTER'; cu.align_y = 'CENTER'
    cu.size = 1.0; cu.extrude = .03
    to = bpy.data.objects.new("txt", cu)
    bpy.context.scene.collection.objects.link(to)
    select_only(to)
    bpy.ops.object.convert(target='MESH')
    to = bpy.context.view_layer.objects.active
    me = to.data
    xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    s = min(2.3 / w, .38 / max(h, 1e-6))
    cx, cy, zmin = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, min(zs)
    for v in me.vertices:
        x, y, z = v.co.x, v.co.y, v.co.z
        v.co = Vector(((x - cx) * s, -(.125 + (z - zmin) * s * .6), .3 + (y - cy) * s))
    me.update()
    me.materials.clear()
    me.materials.append(mat("SignText"))
    for pg in me.polygons:
        pg.material_index = 0
        pg.use_smooth = False
    to.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    return join([panel, to], name)


def main():
    jobs = [("FireEscape_Floor", fire_escape, (2.8, 3.8, 1.56), (35, 20)), ("UtilityPole", utility_pole, (2.2, 9.15, None), (35, 12)),
            ("Wire", wire, (1.0, None, None), (20, 30)), ("Dumpster", dumpster, (1.84, 1.6, 1.2), (35, 20)),
            ("TrashBags", trash_bags, None, (35, 25)), ("NewsBox", news_box, None, (35, 20)),
            ("Manhole", manhole, None, (35, 45)), ("StormGrate", storm_grate, None, (35, 45)),
            ("AC_WindowUnit", ac_window_unit, None, (35, 20))]
    for i in range(1, 7):
        jobs.append(("Storefront_Sign_%d" % i, (lambda k=i: sign(k)), (2.6, .6, None), (0, 8)))
    for name, fn, exp, (az, el) in jobs:
        reset_scene()
        o = fn()
        report(o, exp, .12)
        export_fbx([o], "Props", o.name)
        render_preview([o], os.path.join(PREVIEWS, "Gritty_" + name + ".png"), az=az, el=el, size=(800, 800))


main()
