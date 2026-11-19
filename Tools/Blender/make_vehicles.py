"""Cars for Zombies 5.5 -> Assets/Art/Models/Vehicles/{Car_Sedan,Car_Hatchback,Car_Van}.fbx
Run: blender --background --factory-startup --python make_vehicles.py

Method: the lower body and the greenhouse are lofted from cross-sections (smooth, rounded), wheel
arches are boolean-cut, then lights / mirrors / seams / bumper trim are placed by ray-casting onto
the finished shell. Pivot = ground, between the wheels; forward = Unity +Z (Blender -Y).
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *

# ---------------------------------------------------------------------------------- specs
# body: bottom/top/half-width profiles along forward f. cabin: stations (f, roof z) + width scale.
SEDAN = dict(
    name="Car_Sedan", expect=(1.8, 1.45, 4.5), color=(.12, .25, .55),
    fmin=-2.25, fmax=2.25, hw=.86,
    bottom=[(-2.25, .42), (-2.1, .30), (-1.9, .22), (-1.0, .2), (1.0, .2), (1.9, .22), (2.1, .30), (2.25, .40)],
    top=[(-2.25, .80), (-2.0, .93), (-1.5, .97), (-.9, .96), (0, .96), (.8, .97), (1.2, .95), (1.8, .88), (2.1, .78), (2.25, .68)],
    width=[(-2.25, .52), (-2.15, .70), (-1.9, .80), (-1.4, .86), (1.5, .86), (1.9, .83), (2.15, .72), (2.25, .55)],
    wheel_f=(1.4, -1.4), wr=.33, tw=.21, track=.72,
    cab_hw=(.78, .76, .69, .60, .36), zbase=.8, belt=.98, win_h=9,
    roof=[(.85, 1.0), (.38, 1.40), (.15, 1.445), (-.2, 1.45), (-.6, 1.44), (-.92, 1.40), (-1.3, 1.04), (-1.42, .985)],
    stations=[(.85, .96), (.38, 1.0), (.15, 1.0), (-.10, 1.0), (-.20, 1.0), (-.55, 1.0), (-.92, 1.0), (-1.3, .93), (-1.42, .9)],
    ws=(.85, .38), ws_cols=(3, 8), rear_glass=(-.92, -1.3), side_win=[(.38, -.10), (-.20, -.92)],
    seams=[.62, -.15, -1.1], handles=[-.05, -.85], hl=(.52, .72, .27, .11), tl=(.54, .88, .30, .09),
    mirror_f=.52, mirror_z=1.0, exhaust=False,
)
HATCH = dict(
    name="Car_Hatchback", expect=(1.75, 1.5, 4.0), color=(.75, .65, .1),
    fmin=-2.0, fmax=2.0, hw=.82,
    bottom=[(-2.0, .36), (-1.85, .27), (-1.7, .22), (-1.0, .2), (1.0, .2), (1.7, .22), (1.88, .30), (2.0, .38)],
    top=[(-2.0, .90), (-1.8, .95), (-1.3, .97), (0, .96), (.7, .97), (1.1, .94), (1.6, .88), (1.9, .78), (2.0, .68)],
    width=[(-2.0, .56), (-1.92, .68), (-1.7, .78), (-1.3, .82), (1.3, .82), (1.7, .80), (1.92, .70), (2.0, .55)],
    wheel_f=(1.25, -1.2), wr=.31, tw=.2, track=.69,
    cab_hw=(.75, .73, .67, .58, .35), zbase=.8, belt=.98, win_h=9,
    roof=[(.72, 1.0), (.32, 1.42), (.05, 1.5), (-.2, 1.51), (-1.2, 1.49), (-1.6, 1.38), (-1.93, 1.08), (-1.99, 1.0)],
    stations=[(.72, .95), (.32, 1.0), (.05, 1.0), (-.12, 1.0), (-.22, 1.0), (-.7, 1.0), (-1.2, 1.0), (-1.6, .98), (-1.93, .94), (-1.99, .9)],
    ws=(.72, .32), ws_cols=(3, 8), rear_glass=(-1.2, -1.93), side_win=[(.32, -.12), (-.22, -1.2)],
    seams=[.55, -.17, -1.0], handles=[-.07, -.9], hl=(.52, .72, .25, .11), tl=(.56, .86, .13, .30),
    mirror_f=.5, mirror_z=1.0, exhaust=False, tl_vertical=True,
)
VAN = dict(
    name="Car_Van", expect=(1.95, 1.95, 4.9), color=(.85, .85, .85),
    fmin=-2.4, fmax=2.4, hw=.895,
    bottom=[(-2.4, .40), (-2.3, .30), (-2.1, .25), (-1.0, .25), (1.0, .25), (2.0, .27), (2.3, .33), (2.4, .42)],
    top=[(-2.4, .95), (-2.3, .98), (0, .98), (1.6, .98), (1.95, .93), (2.3, .82), (2.4, .72)],
    width=[(-2.4, .66), (-2.35, .82), (-2.2, .89), (-1.9, .895), (1.8, .895), (2.1, .87), (2.35, .78), (2.4, .62)],
    wheel_f=(1.55, -1.45), wr=.35, tw=.22, track=.76,
    cab_hw=(.905, .905, .89, .84, .6), zbase=.8, belt=.98, win_h=.5,
    roof=[(1.75, 1.05), (1.05, 1.88), (.8, 1.94), (.5, 1.95), (-2.3, 1.95), (-2.37, 1.91), (-2.4, 1.85)],
    stations=[(1.75, .93), (1.05, 1.0), (.8, 1.0), (.5, 1.0), (-.4, 1.0), (-1.4, 1.0), (-2.3, 1.0), (-2.37, .96), (-2.4, .92)],
    ws=(1.75, 1.05), ws_cols=(2, 9), rear_glass=None, side_win=[(1.05, .5)],
    seams=[.5, -.5, -1.7], handles=[.2, -.7], hl=(.56, .72, .26, .12), tl=(.7, 1.0, .14, .4),
    mirror_f=1.0, mirror_z=1.35, exhaust=False, tl_vertical=True, box_rear=True, seam_top=1.75,
)


# ---------------------------------------------------------------------------------- body
BODY_RIGHT = [(0, 0), (.70, 0), (.92, .06), (1.0, .22), (1.0, .55), (.975, .80), (.90, .93), (.72, .985), (.4, .995), (0, 1.0)]


def body_ring(f, zb, zt, hw):
    h = zt - zb
    r = [(hw * a, zb + h * b) for a, b in BODY_RIGHT]
    return [V(x, f, z) for x, z in r] + [V(-x, f, z) for x, z in reversed(r[1:-1])]


def make_body(s):
    fb, ft, fw = pchip(s["bottom"]), pchip(s["top"]), pchip(s["width"])
    N = 28
    fs = [s["fmin"] + (s["fmax"] - s["fmin"]) * (1 - math.cos(math.pi * i / (N - 1))) / 2 for i in range(N)]
    p = Part("body")
    p.loft([body_ring(f, fb(f), max(ft(f), fb(f) + .05), fw(f)) for f in fs], "CarPaint")
    bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces[:])
    p.bm.normal_update()
    # dark underside
    i_in = p.mi("Interior")
    for f in p.bm.faces:
        if f.normal.z < -.6:
            f.material_index = i_in
    return p.build(smooth=40, name="body")


def cab_ring(s, f, roof, sc):
    hb, hw2, hw3, hr, h5 = [x * sc for x in s["cab_hw"]]
    z = [s["zbase"], s["belt"], s["belt"] + .03]
    z3 = min(roof - .085, z[2] + s["win_h"])
    z += [z3, roof - .03, roof - .008, roof]
    for i in range(1, 7):           # keep rows monotonic when the roof is low (windshield base, tail)
        z[i] = max(z[i - 1] + .0005, min(z[i], roof - (6 - i) * .0008))
    xs = [hb, hb, hw2, hw3, hr, h5, 0]
    r = [V(x, f, zz) for x, zz in zip(xs, z)]
    return r + [V(-x, f, zz) for x, zz in reversed(list(zip(xs, z))[1:-1])]


def make_cabin(s):
    roof = pchip(sorted(s["roof"]))
    st = s["stations"]
    p = Part("cabin")
    rings = [cab_ring(s, f, roof(f), sc) for f, sc in st]
    fl = [f for f, _ in st]

    def inside(a, b, rng):
        lo, hi = min(rng), max(rng)
        return lo - 1e-6 <= min(a, b) and max(a, b) <= hi + 1e-6

    def mat_fn(i, j):
        a, b = fl[i], fl[i + 1]
        if s["ws"] and inside(a, b, s["ws"]) and s["ws_cols"][0] <= j <= s["ws_cols"][1]:
            return "Glass"
        if s["rear_glass"] and inside(a, b, s["rear_glass"]) and 3 <= j <= 8:
            return "Glass"
        if j in (2, 9) and any(inside(a, b, w) for w in s["side_win"]):
            return "Glass"
        return "CarPaint"
    p.loft(rings, mat_fn)
    # recess the glass: inset + push in, with a rubber seal ring
    gi = p.mi("Glass")
    glass = [f for f in p.bm.faces if f.material_index == gi]
    res = bmesh.ops.inset_region(p.bm, faces=glass, thickness=.028, depth=-.012, use_even_offset=True)
    ri = p.mi("Rubber")
    for f in res["faces"]:
        f.material_index = ri
    return p.build(smooth=40, name="cabin")


def lens(p, loc, n, size, m, bevel=.025, inset=.3):
    """Flat rounded plate lying on a surface: size = (width, height, thickness)."""
    q = n.to_track_quat('Z', 'Y')
    c = loc + n * (size[2] * (.5 - inset))
    p.rbox(c, size, m, bevel, 2, q)


def wheel(p, s, x, f, side):
    """Tire + rim for one wheel; +side = outward direction."""
    wr, tw = s["wr"], s["tw"]
    ax = Vector((side, 0, 0))
    c = V(x, f, wr)
    k = wr / .33
    tire = [(.2, -.1), (.26, -.108), (.31, -.098), (.33, -.06), (.33, .06), (.31, .098), (.26, .108), (.2, .1)]
    p.lathe([(r * k, h * tw / .21) for r, h in tire], "Rubber", ax, c, 16)
    h0 = tw / 2 - .035
    p.lathe([(0, h0 - .01), (.2 * k, h0 - .01)], "Interior", ax, c, 14)
    p.lathe([(.18 * k, h0 + .005), (.205 * k, h0 + .025), (.213 * k, h0 + .02), (.213 * k, h0 - .06)], "Chrome", ax, c, 16)
    for i in range(5):
        a = 2 * math.pi * i / 5
        d = Vector((0, math.cos(a), math.sin(a)))
        p.bar(c + ax * (h0 + .01) + d * .03 * k, c + ax * (h0 + .01) + d * .19 * k, .05 * k, .02, "Chrome")
    p.sphere(c + ax * (h0 + .01), (.045 * k, .045 * k, .02), "Chrome", (8, 5), ax.to_track_quat('Z', 'Y'))


def build_car(s):
    reset_scene()
    mat("CarPaint").diffuse_color = (*s["color"], 1)
    body = make_body(s)
    # wheel-arch cutters (dark interior material shows as the wheel well)
    cutters = []
    for sd in (-1, 1):
        for wf in s["wheel_f"]:
            cp = Part("cut")
            x0, x1 = s["track"] - .14, s["hw"] + .15
            r = s["wr"] + .06
            cp.cyl(V(sd * (x0 + x1) / 2, wf, s["wr"] + .01), r, r, x1 - x0, "Interior", 24, Euler((0, math.radians(90), 0)))
            cutters.append(cp.build(smooth=0, name="cutter"))
    boolean_diff(body, cutters)
    cabin = make_cabin(s)
    shell = join([body, cabin], "shell")
    bpy.context.view_layer.update()

    d = Part("details")
    hw = s["hw"]

    def hit(x, z, f_far=6.0, side=None):
        if side is None:      # front (+) / rear (-)
            o = V(x, math.copysign(6, f_far), z)
            return raycast(shell, o, V(0, -math.copysign(1, f_far), 0) - V(0, 0, 0))
        return raycast(shell, Vector((side * 6, -f_far, z)), Vector((-side, 0, 0)))

    # front / rear features on the nose and tail
    hlx, hlz, hlw, hlh = s["hl"]
    for sd in (-1, 1):
        loc, n = hit(sd * hlx, hlz, 6)
        if loc:
            lens(d, loc, n, (hlw, hlh, .07), "Headlight", .03)
            lens(d, loc, n, (hlw + .03, hlh + .03, .05), "Chrome", .02, inset=.1)  # bezel behind
        tlx, tlz, tlw, tlh = s["tl"]
        loc, n = hit(sd * tlx, tlz, -6)
        if loc:
            lens(d, loc, n, (tlw, tlh, .06), "Taillight", .025)
    # grille, lower intake, chrome bumper line, rear bar
    loc, n = hit(0, hlz - .05, 6)
    if loc:
        lens(d, loc, n, (hlx * .9, .1, .05), "Rubber", .02)
        lens(d, loc, n, (hlx * .9 + .04, .035, .035), "Chrome", .012, inset=.1)
    loc, n = hit(0, .42, 6)
    if loc:
        lens(d, loc, n, (hw * 1.05, .12, .05), "Rubber", .03)
    for fsgn in (6, -6):
        pts = []
        for i in range(9):
            x = -hw * .72 + i * hw * .18
            l, nn = hit(x, .5, fsgn)
            if l:
                pts.append(l + nn * .006)
        if len(pts) > 2:
            d.tube(pts, [(.007, .02)] * len(pts), "Chrome", 4)
    if s.get("exhaust"):
        d.cyl(V(.55, -2.2, .27), .035, .035, .16, "Chrome", 10, Euler((math.radians(90), 0, 0)))

    # side details: door seams, handles, sills
    for sd in (-1, 1):
        for sf in s["seams"]:
            a, na = hit(0, .32, sf, sd)
            b, nb = hit(0, s.get("seam_top", s["belt"] - .02), sf, sd)
            if a and b:
                d.bar(a + na * .002, b + nb * .002, .012, .008, "Interior")
        for hf in s["handles"]:
            a, na = hit(0, s["belt"] - .1, hf, sd)
            if a:
                lens(d, a, na, (.13, .03, .03), "Chrome", .01, inset=.2)
        # mirrors
        mx, mf, mz = sd * (hw + .02), s["mirror_f"], s["mirror_z"]
        d.sphere(V(mx, mf, mz), (.05, .085, .06), "CarPaint", (10, 8), Euler((0, 0, math.radians(sd * 10))))
        d.sphere(V(mx - sd * .005, mf - .045, mz), (.04, .02, .05), "Chrome", (8, 6))
        d.bar(V(sd * (hw - .12), mf + .06, mz - .04), V(mx - sd * .03, mf, mz - .01), .03, .02, "CarPaint")
        # sill trim
        a, na = hit(0, .3, 1.2, sd)
        b, nb = hit(0, .3, -1.2, sd)
        if a and b:
            d.bar(a + na * .004, b + nb * .004, .035, .01, "Rubber")
    if s.get("box_rear"):                         # van rear doors: glass panes
        for sd in (-1, 1):
            loc, n = hit(sd * .42, 1.45, -6)
            if loc:
                lens(d, loc, n, (.6, .45, .04), "Glass", .03)
        loc, n = hit(0, 1.0, -6)
        if loc:
            lens(d, (loc + Vector((0, 0, .35))), n, (.012, 1.1, .02), "Interior", 0.004)

    # wheels
    for sd in (-1, 1):
        for wf in s["wheel_f"]:
            wheel(d, s, sd * s["track"], wf, sd)
    dets = d.build(smooth=40, name="details")
    car = join([shell, dets], s["name"])
    return car


def main():
    for s in (SEDAN, HATCH, VAN):
        car = build_car(s)
        report(car, s["expect"], .1)
        export_fbx([car], "Vehicles", s["name"])
        render_preview([car], os.path.join(PREVIEWS, s["name"] + ".png"), az=35, el=16, size=(1000, 700))
        render_preview([car], os.path.join(PREVIEWS, s["name"] + "_rear.png"), az=145, el=16, size=(1000, 700))


main()
