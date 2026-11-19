"""ExplosiveBarrel and WeaponKiosk -> Assets/Art/Models/Props/*.fbx
Run: blender --background --factory-startup --python make_survival_props.py
Pivot bottom centre, front facing +Z (Blender -Y). Authoring helpers use (x, f, z) -> V(x, f, z).
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bl_human import *      # also registers the survival slot colors
from bl_common import aim, ring_circle


def empty(name, loc, parent, size=.1):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'ARROWS'; e.empty_display_size = size
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    e.parent = parent
    return e


# ------------------------------------------------------------------ explosive barrel (0.6 m diameter x 0.9 m)
def barrel():
    P = Part("ExplosiveBarrel")
    R = .295
    prof = [(.255, 0.0), (.282, .004), (.298, .016), (.303, .032), (.298, .046), (.288, .052), (.2925, .066),   # rolled bottom rim
            (R, .09)]
    z = .09
    # body with two rolling hoops (beads) at z=.30 and z=.60
    def hoop(zc):
        return [(R, zc - .035), (R + .004, zc - .016), (R + .012, zc - .006), (R + .014, zc), (R + .012, zc + .006),
                (R + .004, zc + .016), (R, zc + .035)]
    prof += hoop(.30)
    prof += [(R, .45)]
    prof += hoop(.60)
    prof += [(R, .76), (R, .812), (.2975, .826), (.303, .842), (.3035, .858), (.298, .872), (.288, .878), (.2925, .866),  # top rolled rim
             (.282, .860), (.262, .857), (.252, .850), (.245, .847), (.0, .847)]
    prof_s = [(p[0] * .985 if p[0] > .01 else p[0], p[1] * 1.025) for p in prof]

    def slot(i, j):
        zm = (prof_s[i][1] + prof_s[i + 1][1]) / 2
        r = (prof_s[i][0] + prof_s[i + 1][0]) / 2
        if zm < .09 or zm > .80 or r < .26:
            return "Metal"
        if abs(zm - .30) < .036 or abs(zm - .60) < .036:
            return "Metal"
        if .335 <= zm <= .565:
            return "Hazard"
        return "Paint"
    rings = [ring_circle((0, 0, h), (0, 0, 1), max(r, 0.0), 40) for r, h in prof_s]
    P.loft(rings, slot, cap0=True, cap1=False)
    # bungs and lid details
    for (x, f) in ((.11, .05), (-.12, -.09)):
        P.cyl(V(x, f, .858), .032, .032, .014, "Metal", 16)
        P.cyl(V(x, f, .868), .022, .022, .008, "Metal", 16)
    P.rbox(V(0, 0, .855), (.20, .028, .010), "Metal", .002, 1)            # lid stiffener
    ob = P.build(smooth=50, radial=True)
    ob.name = ob.data.name = "ExplosiveBarrel"
    return ob


# ------------------------------------------------------------------ weapon kiosk (1.3 x 2.3 x 0.6)
def kiosk():
    P = Part("WeaponKiosk")

    def kb(slot, x0, x1, f0, f1, z0, z1, bevel=.006, seg=1):
        P.rbox(V((x0 + x1) / 2, (f0 + f1) / 2, (z0 + z1) / 2), (x1 - x0, f1 - f0, z1 - z0), slot, bevel, seg)

    W, D = 1.30, .60
    hx, hf = W / 2, D / 2
    # plinth
    kb("Metal", -hx, hx, -hf, hf, 0, .12, .010, 2)
    kb("Metal", -hx + .02, hx - .02, -hf + .02, hf - .02, .12, .16, .006)
    # lower cabinet (painted) with two door panels and a vent
    kb("Paint", -hx + .02, hx - .02, -hf + .03, hf - .03, .16, .80, .012, 2)
    for s in (-1, 1):
        kb("Metal", s * .325 - .30, s * .325 + .30, hf - .036, hf - .022, .22, .74, .004)      # door insets
        kb("Metal", s * .06 - .012, s * .06 + .012, hf - .026, hf - .004, .42, .60, .005)      # door handles
    for i in range(6):                                                                         # vent slats
        kb("Metal", -.22, .22, hf - .034, hf - .020, .24 + i * .012, .249 + i * .012, .001)
    # display section: back + side walls + top/bottom
    z0, z1 = .80, 1.86
    kb("Metal", -hx + .02, hx - .02, -hf + .03, -hf + .06, z0, z1, .004)                      # back wall
    for s in (-1, 1):
        kb("Paint", s * (hx - .045) - .025, s * (hx - .045) + .025, -hf + .03, hf - .03, z0, z1, .008, 2)   # side walls
    kb("Paint", -hx + .02, hx - .02, -hf + .03, hf - .03, z0, z0 + .04, .006)                  # display floor
    kb("Metal", -hx + .07, hx - .07, -hf + .06, hf - .09, z0 + .04, z0 + .07, .004)          # plinth for the gun
    kb("Paint", -hx + .02, hx - .02, -hf + .03, hf - .03, z1 - .05, z1, .006)                  # display ceiling
    # glass front with frame
    fz = hf - .035
    kb("Glass", -hx + .06, hx - .06, fz - .006, fz + .006, z0 + .06, z1 - .06, .002)
    kb("Frame", -hx + .02, hx - .02, fz - .02, fz + .02, z0 + .005, z0 + .06, .006, 2)       # bottom rail
    kb("Frame", -hx + .02, hx - .02, fz - .02, fz + .02, z1 - .06, z1 - .005, .006, 2)       # top rail
    for s in (-1, 1):
        kb("Frame", s * (hx - .04) - .02, s * (hx - .04) + .02, fz - .02, fz + .02, z0, z1, .006, 2)   # stiles
    kb("Frame", -.01, .01, fz - .014, fz + .014, z0 + .06, z1 - .06, .003)                    # centre mullion
    # security grille bars inside the glass
    for i in range(-5, 6):
        kb("Metal", i * .105 - .004, i * .105 + .004, fz - .02, fz - .012, z0 + .07, z1 - .07, .0015)
    # price / screen housing
    zs0, zs1 = z1, 2.23
    kb("Paint", -hx + .02, hx - .02, -hf + .03, hf - .03, zs0, zs1, .010, 2)
    kb("Metal", -.50, .50, hf - .036, hf - .018, zs0 + .04, zs1 - .04, .006, 2)               # screen bezel
    kb("KioskScreen", -.46, .46, hf - .020, hf - .014, zs0 + .07, zs1 - .07, .002)             # emissive price panel
    # canopy
    kb("Metal", -hx - .02, hx + .02, -hf - .01, hf + .035, zs1, zs1 + .07, .012, 2)
    kb("Metal", -hx + .01, hx - .01, hf + .02, hf + .035, zs1 - .03, zs1, .004)               # canopy drip lip
    # sign strip above the glass
    kb("Frame", -hx + .05, hx - .05, hf - .03, hf - .015, z1 - .045, z1 - .02, .003)
    # side vents on both sides + corner trims
    for s in (-1, 1):
        for i in range(8):
            kb("Metal", s * hx - (.004 if s > 0 else -.004) - .003, s * hx + (.004 if s > 0 else -.004) + .003, -.12, .12, .30 + i * .028, .310 + i * .028, .001)
        kb("Frame", s * (hx - .02) - .018, s * (hx - .02) + .018, hf - .05, hf, .16, .80, .006)
    ob = P.build(smooth=40)
    ob.name = ob.data.name = "WeaponKiosk"
    return ob


def export(ob, name, extra=()):
    C.export_fbx([ob, *extra], "Props", name)


def main():
    C.reset_scene()
    out = []
    b = barrel()
    C.report(b, (.606, .878, .606))
    b.location = (-1.2, 0, 0)
    k = kiosk()
    C.report(k, (1.34, 2.30, .68))
    disp = empty("Display", V(0, 0.02, 1.34), k, .15)
    k.location = (0, 0, 0)
    print("Display empty at", tuple(round(v, 3) for v in disp.location))
    # export at the origin
    b.location = (0, 0, 0)
    export(b, "ExplosiveBarrel")
    export(k, "WeaponKiosk", (disp,))
    P = C.PREVIEWS
    b.location = (3, 0, 0)
    render_view([b], os.path.join(P, "ExplosiveBarrel.png"), az=35, el=14, size=(700, 800), scale=1.3, target=(3, 0, .45), floor=False)
    render_view([b], os.path.join(P, "ExplosiveBarrel_side.png"), az=90, el=0, size=(600, 800), scale=1.1, target=(3, 0, .45), floor=False)
    b.location = (0, 0, 0)
    render_view([k], os.path.join(P, "WeaponKiosk.png"), az=30, el=10, size=(800, 1100), scale=2.9, target=(0, 0, 1.15), floor=False)
    render_view([k], os.path.join(P, "WeaponKiosk_front.png"), az=0, el=4, size=(800, 1100), scale=2.9, target=(0, 0, 1.15), floor=False)
    render_view([k], os.path.join(P, "WeaponKiosk_back.png"), az=205, el=10, size=(800, 1100), scale=2.9, target=(0, 0, 1.15), floor=False)


if __name__ == "__main__":
    main()
