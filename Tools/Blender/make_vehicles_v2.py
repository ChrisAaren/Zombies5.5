"""Vehicles V2 -> Assets/Art/Models/Vehicles/V2/CarV2_{Sedan,Coupe,Hatch,Wagon,Van,Pickup,Cab,SUV,Minivan}.fbx
Textures -> Assets/Art/Textures/{PlatesV2_Albedo,CarDirt_Mask}.png
Run: blender --background --factory-startup --python make_vehicles_v2.py [--only Sedan,Cab] [--tex]

Pivot: ground, between the wheels (bottom center). Forward = Unity +Z. Slots: CarPaint, Glass, Chrome,
Rubber, Interior, Headlight, Taillight, Metal, Plate. 2nd UV map "UVDirt" (-> Unity uv2) is the dirt-mask map.

Method: hull and greenhouse are lofted from cross-sections; arches are boolean-cut; the hull is opened
under the greenhouse so a lining shell, seats, dash and steering wheel show through the glass. Lights,
trim, grooves (panel gaps), handles, wipers, plates are placed by ray-casting onto the finished shell.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_street2 import *

COLORS["Plate"] = (.9, .9, .85)

# ---------------------------------------------------------------------------------- specs
SEDAN = dict(
    name="CarV2_Sedan", expect=(1.8, 1.45, 4.5), color=(.30, .36, .45),
    fmin=-2.25, fmax=2.25, hw=.86,
    bottom=[(-2.25, .42), (-2.1, .30), (-1.9, .22), (-1.0, .2), (1.0, .2), (1.9, .22), (2.1, .30), (2.25, .40)],
    top=[(-2.25, .80), (-2.0, .93), (-1.5, .97), (-.9, .96), (0, .96), (.8, .97), (1.2, .95), (1.8, .88), (2.1, .78), (2.25, .68)],
    width=[(-2.25, .52), (-2.15, .70), (-1.9, .80), (-1.4, .86), (1.5, .86), (1.9, .83), (2.15, .72), (2.25, .55)],
    wheel_f=(1.4, -1.4), wr=.33, tw=.21, track=.72, rim="spoke5",
    cab_hw=(.78, .76, .69, .60, .36), zbase=.8, belt=.98, win_h=9,
    roof=[(.85, 1.0), (.38, 1.40), (.15, 1.445), (-.2, 1.45), (-.6, 1.44), (-.92, 1.40), (-1.3, 1.04), (-1.42, .985)],
    stations=[(.85, .96), (.38, 1.0), (.15, 1.0), (-.10, 1.0), (-.20, 1.0), (-.55, 1.0), (-.92, 1.0), (-1.3, .93), (-1.42, .9)],
    ws=(.85, .38), ws_cols=(3, 8), rear_glass=(-.92, -1.3), side_win=[(.38, -.10), (-.20, -.92)],
    seams=[.62, -.15, -1.1], handles=[-.05, -.85], hl=(.52, .72, .27, .11), tl=(.54, .88, .30, .09),
    mirror_f=.52, mirror_z=1.0, seats=(0.0, -.72), plate_z=.62, bonnet=(.78, 1.85), trunk=(-1.5, -2.1),
)
CAB = dict(SEDAN, name="CarV2_Cab", color=(.80, .66, .18), sign=True, rim="steel", plate_cell=9)
COUPE = dict(
    name="CarV2_Coupe", expect=(1.8, 1.35, 4.4), color=(.42, .12, .12),
    fmin=-2.2, fmax=2.2, hw=.87,
    bottom=[(-2.2, .42), (-2.05, .30), (-1.85, .22), (-1.0, .2), (1.0, .2), (1.85, .22), (2.05, .30), (2.2, .40)],
    top=[(-2.2, .82), (-1.95, .94), (-1.4, .98), (-.8, .97), (0, .96), (.8, .97), (1.2, .95), (1.8, .86), (2.05, .76), (2.2, .66)],
    width=[(-2.2, .54), (-2.1, .72), (-1.85, .83), (-1.4, .87), (1.4, .87), (1.85, .84), (2.1, .74), (2.2, .56)],
    wheel_f=(1.33, -1.3), wr=.335, tw=.225, track=.73, rim="spoke5",
    cab_hw=(.79, .77, .70, .60, .36), zbase=.8, belt=.97, win_h=9,
    roof=[(.78, .99), (.30, 1.30), (0, 1.345), (-.45, 1.34), (-.95, 1.24), (-1.4, 1.05), (-1.55, .985)],
    stations=[(.78, .96), (.3, 1.0), (0, 1.0), (-.3, 1.0), (-.6, 1.0), (-.95, 1.0), (-1.25, .97), (-1.45, .94), (-1.55, .9)],
    ws=(.78, .30), ws_cols=(3, 8), rear_glass=(-.95, -1.45), side_win=[(.30, -.6), (-.6, -.95)],
    seams=[.72, -.55], handles=[-.42], hl=(.52, .70, .26, .10), tl=(.56, .88, .30, .09),
    mirror_f=.48, mirror_z=.99, seats=(0.05, -.75), plate_z=.6, bonnet=(.75, 1.85), trunk=(-1.6, -2.05),
)
HATCH = dict(
    name="CarV2_Hatch", expect=(1.75, 1.5, 4.0), color=(.55, .56, .58),
    fmin=-2.0, fmax=2.0, hw=.82,
    bottom=[(-2.0, .36), (-1.85, .27), (-1.7, .22), (-1.0, .2), (1.0, .2), (1.7, .22), (1.88, .30), (2.0, .38)],
    top=[(-2.0, .90), (-1.8, .95), (-1.3, .97), (0, .96), (.7, .97), (1.1, .94), (1.6, .88), (1.9, .78), (2.0, .68)],
    width=[(-2.0, .56), (-1.92, .68), (-1.7, .78), (-1.3, .82), (1.3, .82), (1.7, .80), (1.92, .70), (2.0, .55)],
    wheel_f=(1.25, -1.2), wr=.31, tw=.2, track=.69, rim="spoke5",
    cab_hw=(.75, .73, .67, .58, .35), zbase=.8, belt=.98, win_h=9,
    roof=[(.72, 1.0), (.32, 1.42), (.05, 1.5), (-.2, 1.51), (-1.2, 1.49), (-1.6, 1.38), (-1.93, 1.08), (-1.99, 1.0)],
    stations=[(.72, .95), (.32, 1.0), (.05, 1.0), (-.12, 1.0), (-.22, 1.0), (-.7, 1.0), (-1.2, 1.0), (-1.6, .98), (-1.93, .94), (-1.99, .9)],
    ws=(.72, .32), ws_cols=(3, 8), rear_glass=(-1.2, -1.93), side_win=[(.32, -.12), (-.22, -1.2)],
    seams=[.55, -.17, -1.0], handles=[-.07, -.9], hl=(.52, .72, .25, .11), tl=(.56, .86, .13, .30),
    mirror_f=.5, mirror_z=1.0, tl_vertical=True, seats=(0.0, -.7), plate_z=.72, bonnet=(.66, 1.65), hatch=True,
)
WAGON = dict(
    name="CarV2_Wagon", expect=(1.8, 1.5, 4.8), color=(.22, .30, .25),
    fmin=-2.4, fmax=2.3, hw=.86,
    bottom=[(-2.4, .42), (-2.25, .30), (-2.05, .22), (-1.0, .2), (1.0, .2), (1.95, .22), (2.15, .30), (2.3, .40)],
    top=[(-2.4, .95), (-2.2, .98), (-1.5, .97), (-.9, .96), (0, .96), (.8, .97), (1.2, .95), (1.85, .88), (2.15, .78), (2.3, .68)],
    width=[(-2.4, .62), (-2.3, .76), (-2.05, .83), (-1.4, .86), (1.5, .86), (1.95, .83), (2.2, .72), (2.3, .55)],
    wheel_f=(1.45, -1.45), wr=.335, tw=.215, track=.72, rim="spoke5",
    cab_hw=(.78, .76, .70, .62, .4), zbase=.8, belt=.98, win_h=9,
    roof=[(.85, 1.0), (.38, 1.40), (.15, 1.455), (-.2, 1.46), (-1.8, 1.45), (-2.12, 1.38), (-2.3, 1.05)],
    stations=[(.85, .96), (.38, 1.0), (.15, 1.0), (-.10, 1.0), (-.20, 1.0), (-.6, 1.0), (-1.2, 1.0), (-1.8, 1.0), (-2.12, .97), (-2.3, .92)],
    ws=(.85, .38), ws_cols=(3, 8), rear_glass=(-1.8, -2.3), side_win=[(.38, -.10), (-.20, -.6), (-.6, -1.8)],
    seams=[.62, -.15, -1.1], handles=[-.05, -.85], hl=(.52, .72, .27, .11), tl=(.62, .98, .12, .34),
    mirror_f=.52, mirror_z=1.0, tl_vertical=True, seats=(0.0, -.72), plate_z=.80, bonnet=(.78, 1.9), hatch=True,
)
VAN = dict(
    name="CarV2_Van", expect=(1.95, 1.95, 4.9), color=(.72, .72, .70),
    fmin=-2.4, fmax=2.4, hw=.895,
    bottom=[(-2.4, .40), (-2.3, .30), (-2.1, .25), (-1.0, .25), (1.0, .25), (2.0, .27), (2.3, .33), (2.4, .42)],
    top=[(-2.4, .95), (-2.3, .98), (0, .98), (1.6, .98), (1.95, .93), (2.3, .82), (2.4, .72)],
    width=[(-2.4, .66), (-2.35, .82), (-2.2, .89), (-1.9, .895), (1.8, .895), (2.1, .87), (2.35, .78), (2.4, .62)],
    wheel_f=(1.55, -1.45), wr=.35, tw=.22, track=.76, rim="steel",
    cab_hw=(.905, .905, .89, .84, .6), zbase=.8, belt=.98, win_h=.5,
    roof=[(1.75, 1.05), (1.05, 1.88), (.8, 1.94), (.5, 1.95), (-2.3, 1.95), (-2.37, 1.91), (-2.4, 1.85)],
    stations=[(1.75, .93), (1.05, 1.0), (.8, 1.0), (.5, 1.0), (-.4, 1.0), (-1.4, 1.0), (-2.3, 1.0), (-2.37, .96), (-2.4, .92)],
    ws=(1.75, 1.05), ws_cols=(2, 9), rear_glass=None, side_win=[(1.05, .5)],
    seams=[.5, -.5, -1.7], handles=[.2, -.7], hl=(.56, .72, .26, .12), tl=(.7, 1.0, .14, .4),
    mirror_f=1.0, mirror_z=1.35, tl_vertical=True, box_rear=True, seam_top=1.75, seats=(.95,), plate_z=.72, bonnet=(1.7, 2.2),
)
SUV = dict(
    name="CarV2_SUV", expect=(1.85, 1.75, 4.6), color=(.10, .11, .13),
    fmin=-2.3, fmax=2.3, hw=.9,
    bottom=[(-2.3, .5), (-2.15, .38), (-1.95, .3), (-1.0, .28), (1.0, .28), (1.95, .3), (2.15, .4), (2.3, .5)],
    top=[(-2.3, 1.0), (-2.2, 1.06), (-1.6, 1.08), (0, 1.08), (1.2, 1.07), (1.8, 1.02), (2.15, .92), (2.3, .84)],
    width=[(-2.3, .70), (-2.2, .84), (-2.0, .89), (-1.6, .9), (1.6, .9), (2.0, .88), (2.2, .8), (2.3, .66)],
    wheel_f=(1.4, -1.38), wr=.385, tw=.245, track=.76, rim="spoke6", hullN=29,
    cab_hw=(.86, .85, .80, .72, .48), zbase=.9, belt=1.1, win_h=9,
    roof=[(.95, 1.12), (.5, 1.66), (.2, 1.72), (-1.0, 1.72), (-1.7, 1.63), (-2.05, 1.5), (-2.25, 1.2)],
    stations=[(.95, .95), (.5, 1.0), (.2, 1.0), (-.15, 1.0), (-.25, 1.0), (-.8, 1.0), (-1.5, 1.0), (-2.0, 1.0), (-2.2, .97), (-2.25, .94)],
    ws=(.95, .5), ws_cols=(3, 8), rear_glass=(-1.7, -2.25), side_win=[(.5, -.15), (-.25, -.8), (-.8, -1.7)],
    seams=[.7, -.2, -1.15], handles=[-.08, -.9], hl=(.6, .86, .3, .13), tl=(.66, 1.0, .12, .36),
    mirror_f=.62, mirror_z=1.1, tl_vertical=True, seats=(.12, -.7, -1.4), plate_z=.9, bonnet=(.9, 1.85), hatch=True,
)
MINIVAN = dict(
    name="CarV2_Minivan", expect=(1.9, 1.78, 4.85), color=(.62, .63, .62),
    fmin=-2.45, fmax=2.4, hw=.9,
    bottom=[(-2.45, .40), (-2.3, .30), (-2.1, .25), (-1.0, .25), (1.0, .25), (2.0, .27), (2.3, .33), (2.4, .40)],
    top=[(-2.45, 1.0), (-2.35, 1.04), (-1.5, 1.05), (0, 1.04), (1.4, 1.03), (1.9, .96), (2.25, .84), (2.4, .74)],
    width=[(-2.45, .68), (-2.35, .82), (-2.15, .89), (-1.8, .9), (1.8, .9), (2.1, .87), (2.3, .78), (2.4, .64)],
    wheel_f=(1.5, -1.35), wr=.345, tw=.215, track=.76, rim="spoke6", hullN=29,
    cab_hw=(.86, .84, .78, .70, .45), zbase=.85, belt=1.05, win_h=9,
    roof=[(1.55, 1.07), (.8, 1.72), (.5, 1.77), (-1.5, 1.77), (-2.0, 1.68), (-2.3, 1.45), (-2.38, 1.15)],
    stations=[(1.55, .94), (.8, 1.0), (.5, 1.0), (.1, 1.0), (0, 1.0), (-.5, 1.0), (-1.0, 1.0), (-1.45, 1.0), (-1.9, 1.0), (-2.25, .96), (-2.38, .92)],
    ws=(1.55, .8), ws_cols=(3, 8), rear_glass=(-2.25, -2.38), side_win=[(.8, .1), (0, -1.0), (-1.0, -1.9)],
    seams=[.8, .0, -1.05], handles=[-.1, -.95], hl=(.58, .8, .3, .12), tl=(.62, 1.0, .12, .36),
    mirror_f=1.0, mirror_z=1.1, tl_vertical=True, seats=(.55, -.35, -1.15), plate_z=.9, bonnet=(1.5, 2.2), hatch=True,
)
PICKUP = dict(
    name="CarV2_Pickup", expect=(1.9, 1.75, 5.2), color=(.50, .45, .36),
    fmin=-2.65, fmax=2.55, hw=.92,
    bottom=[(-2.65, .48), (-2.5, .36), (-2.3, .3), (-1.0, .3), (1.0, .3), (2.0, .32), (2.4, .4), (2.55, .5)],
    top=[(-2.65, .98), (-2.5, 1.04), (-1.0, 1.05), (0, 1.05), (1.2, 1.04), (1.9, .98), (2.35, .88), (2.55, .8)],
    width=[(-2.65, .8), (-2.55, .87), (-2.3, .91), (-1.0, .92), (1.9, .92), (2.2, .88), (2.45, .78), (2.55, .62)],
    wheel_f=(1.65, -1.6), wr=.4, tw=.26, track=.78, rim="steel",
    cab_hw=(.84, .82, .78, .7, .46), zbase=.9, belt=1.1, win_h=9,
    roof=[(.95, 1.12), (.55, 1.62), (.3, 1.70), (-.35, 1.70), (-.62, 1.64), (-.7, 1.15)],
    stations=[(.95, .95), (.55, 1.0), (.3, 1.0), (-.05, 1.0), (-.15, 1.0), (-.5, 1.0), (-.62, .97), (-.7, .95)],
    ws=(.95, .55), ws_cols=(3, 8), rear_glass=None, rear_window=(.5, .36, 1.4), side_win=[(.55, -.05), (-.15, -.5)],
    seams=[.72, -.1, -.68], handles=[-.0], hl=(.62, .88, .3, .13), tl=(.84, .96, .12, .22),
    mirror_f=.62, mirror_z=1.12, seats=(.2,), plate_z=.75, bonnet=(.9, 1.95), bed=(-.82, -2.58), tail_bumper=True,
)
ALL = [SEDAN, COUPE, HATCH, WAGON, VAN, PICKUP, CAB, SUV, MINIVAN]


# ---------------------------------------------------------------------------------- textures
def make_plates():
    W, H, cols, rows = 1024, 512, 4, 4
    cw, ch = W // cols, H // rows
    rng = np.random.default_rng(7)
    out = np.zeros((H, W, 3), np.float32)
    nz = noise2d(1024, 1.8, 3, m=512)
    nums = ["214", "807", "336", "519", "642", "073", "958", "125", "480", "761", "392", "846", "205", "617", "934", "158"]
    for i in range(16):
        cx, cy = (i % cols) * cw, (i // cols) * ch
        bg = [(.88, .88, .84), (.9, .9, .88), (.92, .86, .5), (.86, .88, .84)][i % 4]
        if i == 9:
            bg = (.95, .78, .2)
        cell = np.empty((ch, cw, 3), np.float32); cell[:] = bg
        cell *= (.93 + .07 * nz[cy:cy + ch, cx:cx + cw, None])
        ink = np.array((.09, .12, .30), np.float32)
        # border
        m = np.zeros((ch, cw), np.float32)
        m[6:9, 6:-6] = 1; m[-9:-6, 6:-6] = 1; m[6:-6, 6:9] = 1; m[6:-6, -9:-6] = 1
        txt = text_mask("ZB 5.5-" + nums[i], cw - 40, 74, .9)
        top = text_mask("ZOMBIES 5.5", cw - 120, 22, .9)
        m2 = np.zeros((ch, cw), np.float32)
        m2[40:40 + 74, 20:20 + cw - 40] = txt
        m2[14:14 + 22, 60:60 + cw - 120] = top * .8
        m3 = np.zeros((ch, cw), np.float32)
        yy, xx = np.mgrid[0:ch, 0:cw]
        for sx in (18, cw - 18):
            m3 += ((xx - sx) ** 2 + (yy - ch // 2) ** 2 < 25)
        ink_a = np.clip(m + m2 + m3 * .6, 0, 1)[..., None]
        cell = cell * (1 - ink_a) + ink * ink_a
        # embossed edge darkening
        cell *= (.9 + .1 * smoothstep(0, 5, np.minimum(np.minimum(xx, cw - 1 - xx), np.minimum(yy, ch - 1 - yy)))[..., None])
        out[cy:cy + ch, cx:cx + cw] = cell
    write_png(os.path.join(TEX, "PlatesV2_Albedo.png"), out)


def make_dirt(S=512):
    """CarDirt_Mask: R=G=B = dirt amount. UV: u = body length remapped so rear axle=0.2, front axle=0.8;
    v = height / 2 m (v 0 = ground)."""
    nz = noise2d(S, 2.0, 41)
    n2 = noise2d(S, 1.2, 42)
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    u = (xx + .5) / S
    z = (1 - (yy + .5) / S) * 2.0               # meters above ground
    sill = np.clip(1 - z / .6, 0, 1) ** 1.5 * .85
    dirt = sill * (.65 + .6 * nz)
    for ax in (.2, .8):                          # wheel arches: spray up the arch and behind the wheel
        du = (u - ax) * 4.6                      # ~meters along the body
        arch = np.exp(-((du ** 2) / (.55 ** 2) + ((z - .35) ** 2) / (.55 ** 2)))
        dirt += arch * (.55 + .4 * n2)
        behind = np.exp(-(((du + .45) ** 2) / (.5 ** 2) + ((z - .12) ** 2) / (.18 ** 2))) * .5
        dirt += behind
    dirt += np.clip(1 - z / 1.8, 0, 1) * .05 * n2      # faint road film everywhere
    dirt += np.clip((nz - .62) * 2.2, 0, 1) * np.clip(1 - z / 1.0, 0, 1) * .4   # streaks
    dirt = np.clip(dirt, 0, 1)
    write_png(os.path.join(TEX, "CarDirt_Mask.png"), np.stack([dirt] * 3, -1))


# ---------------------------------------------------------------------------------- body
BODY_RIGHT = [(0, 0), (.66, 0), (.86, .035), (.95, .12), (.985, .26), (1.0, .45), (.995, .62), (.975, .70), (.985, .76),
              (.95, .88), (.80, .965), (.5, .995), (0, 1.0)]


def body_ring(f, zb, zt, hw):
    h = zt - zb
    r = [(hw * a, zb + h * b) for a, b in BODY_RIGHT]
    return [V(x, f, z) for x, z in r] + [V(-x, f, z) for x, z in reversed(r[1:-1])]


def make_hull(s):
    fb, ft, fw = pchip(s["bottom"]), pchip(s["top"]), pchip(s["width"])
    N = s.get("hullN", 35)
    fs = [s["fmin"] + (s["fmax"] - s["fmin"]) * (1 - math.cos(math.pi * i / (N - 1))) / 2 for i in range(N)]
    p = P2("hull")
    p.loft([body_ring(f, fb(f), max(ft(f), fb(f) + .05), fw(f)) for f in fs], "CarPaint")
    bmesh.ops.recalc_face_normals(p.bm, faces=p.bm.faces[:])
    p.bm.normal_update()
    i_in = p.mi("Interior")
    for f in p.bm.faces:
        if f.normal.z < -.6:
            f.material_index = i_in
    return p.build2(smooth=40, name="hull")


def cab_ring(s, f, roof, sc):
    hb, hw2, hw3, hr, h5 = [x * sc for x in s["cab_hw"]]
    z = [s["zbase"], s["belt"], s["belt"] + .03]
    z3 = min(roof - .085, z[2] + s["win_h"])
    z += [z3, roof - .03, roof - .008, roof]
    for i in range(1, 7):
        z[i] = max(z[i - 1] + .0005, min(z[i], roof - (6 - i) * .0008))
    xs = [hb, hb, hw2, hw3, hr, h5, 0]
    r = [V(x, f, zz) for x, zz in zip(xs, z)]
    return r + [V(-x, f, zz) for x, zz in reversed(list(zip(xs, z))[1:-1])]


def make_cabin(s):
    roof = pchip(sorted(s["roof"]))
    st = s["stations"]
    p = P2("cabin")
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
    gi = p.mi("Glass")
    glass = [f for f in p.bm.faces if f.material_index == gi]
    res = bmesh.ops.inset_region(p.bm, faces=glass, thickness=.028, depth=-.012, use_even_offset=True)
    ri = p.mi("Rubber")
    for f in res["faces"]:
        f.material_index = ri
    # open the greenhouse underneath (remove the floor-diagonal faces and end caps)
    bm = p.bm
    n = len(rings[0])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    kill = [f for f in bm.faces if f.normal.z < -.15 or (abs(f.normal.y) > .98 and f.calc_center_median().z < s["belt"] - .05)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    return p.build2(smooth=40, name="cabin")


def make_liner(s):
    """Dark inner lining so the interior reads through the glass (normals face inward)."""
    roof = pchip(sorted(s["roof"]))
    p = P2("liner")
    rings = []
    zf = .32
    for f, sc in s["stations"]:
        hb, hw2, hw3, hr, h5 = [x * sc - .07 for x in s["cab_hw"]]
        rz = roof(f)
        zb = s["belt"] + .01
        z3 = max(min(rz - .11, zb + .5), zb + .02) if s["win_h"] > 1 else zb + .45
        z3 = min(z3, rz - .1)
        pts = [(hb, zf), (hb, zb), (hw2, zb + .05), (hw3, z3), (hr, rz - .08), (0, rz - .075)]
        rings.append([V(x, f, z) for x, z in pts] + [V(-x, f, z) for x, z in reversed(pts[1:-1])] + [V(-hb, f, zf)])
    p.loft(rings, "Interior")
    ob = p.build2(smooth=0, name="liner")
    ob.data.flip_normals()
    return ob


def edit_mesh(ob, fn):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    fn(bm)
    bm.to_mesh(ob.data); bm.free()
    ob.data.update()


# ---------------------------------------------------------------------------------- detail helpers
def lens(p, loc, n, size, m, bevel=.025, inset=.3):
    q = n.to_track_quat('Z', 'Y')
    c = loc + n * (size[2] * (.5 - inset))
    p.rbox(c, size, m, bevel, 2, q)


def wheel(p, s, x, f, side):
    wr, tw = s["wr"], s["tw"]
    ax = Vector((side, 0, 0))
    c = V(x, f, wr)
    k = wr / .33
    t = tw / .21
    # tire profile: bead, sidewall (with lettering ridge), shoulder, 4 tread ribs + 3 grooves
    half = [(.205, .098), (.216, .106), (.245, .111), (.262, .114), (.266, .117), (.27, .114), (.292, .104), (.314, .086),
            (.325, .07), (.33, .064), (.33, .036), (.322, .03), (.322, .016), (.33, .01), (.33, 0)]
    prof = [(r * k, -h * t) for r, h in half] + [(r * k, h * t) for r, h in reversed(half[:-1])]
    # grooves: mirror the right side so both halves match
    p.lathe([(r, h * side) for r, h in prof], "Rubber", Vector((1, 0, 0)), c, 24)
    h0 = tw / 2 - .02
    # inner disc and outer rim
    p.lathe([(.2 * k, -h0 * side * .9), (0, -h0 * side * .9)], "Interior", Vector((1, 0, 0)), c, 14)
    rim = s["rim"]
    ro = .205 * k
    if rim.startswith("spoke"):
        p.lathe([(ro, h0 * side), (ro * .98, h0 * side * 1.0 - side * .006), (ro * .86, h0 * side - side * .025), (ro * .86, h0 * side - side * .075)],
                "Chrome", Vector((1, 0, 0)), c, 24)
        ns = 5 if rim == "spoke5" else 6
        for i in range(ns):
            a = 2 * math.pi * i / ns + .3
            d = Vector((0, math.cos(a), math.sin(a)))
            dp = Vector((0, -d.z, d.y))
            p0 = c + ax * (h0 * .92) + d * .035 * k
            p1 = c + ax * (h0 * .98) + d * .185 * k
            p.bar(p0 - dp * .004, p1 + dp * .012, .05 * k, .018, "Chrome", .003, 1)
            p.bar(p0, p1, .03 * k, .024, "Chrome")
        p.lathe([(.04 * k, h0 * side), (.035 * k, (h0 + .012) * side), (0, (h0 + .016) * side)], "Chrome", Vector((1, 0, 0)), c, 14)
        # brake disc and caliper visible between the spokes
        p.lathe([(.17 * k, (h0 - .09) * side), (.17 * k, (h0 - .07) * side), (.06 * k, (h0 - .07) * side)], "Metal", Vector((1, 0, 0)), c, 24)
        p.rbox(c + Vector((0, 0, 0)) + Vector((0, .13 * k, .0)) + ax * (h0 - .12), (.05, .09 * k, .1 * k), "Paint", .01, 1)
    else:   # steel wheel with a hubcap and slots
        p.lathe([(ro, h0 * side), (ro * .98, h0 * side - side * .006), (ro * .9, h0 * side - side * .02), (.07 * k, h0 * side - side * .02)],
                "Metal", Vector((1, 0, 0)), c, 24)
        p.lathe([(.12 * k, (h0 - .018) * side), (.115 * k, (h0 + .004) * side), (.06 * k, (h0 + .02) * side), (0, (h0 + .022) * side)], "Chrome",
                Vector((1, 0, 0)), c, 24)
        for i in range(8):
            a = 2 * math.pi * i / 8
            d = Vector((0, math.cos(a), math.sin(a)))
            p.cyl(c + d * .15 * k + ax * (h0 - .015), .016, .016, .012, "Rubber", 6, ax.to_track_quat('Z', 'Y'))
    for i in range(5):  # lug nuts
        a = 2 * math.pi * i / 5
        d = Vector((0, math.cos(a), math.sin(a)))
        p.cyl(c + d * .06 * k + ax * (h0 + .006), .011, .011, .014, "Chrome", 6, ax.to_track_quat('Z', 'Y'))


def groove(d, pts, w=.004):
    pts = [q for q in pts if q is not None]
    if len(pts) > 1:
        d.tube(pts, [(w, w * 1.6)] * len(pts), "Rubber", 4, cap=False)


def build_car(s, idx=0):
    reset_scene()
    hull = make_hull(s)
    cutters = []
    for sd in (-1, 1):
        for wf in s["wheel_f"]:
            cp = Part("cut")
            x0, x1 = s["track"] - .15, s["hw"] + .15
            r = s["wr"] + .07
            cp.cyl(V(sd * (x0 + x1) / 2, wf, s["wr"] + .012), r, r, x1 - x0, "Interior", 28, Euler((0, math.radians(90), 0)))
            cutters.append(cp.build(smooth=0, name="cutter"))
    boolean_diff(hull, cutters)
    cabin = make_cabin(s)
    st_sorted = sorted(s["stations"])
    st_f = [q[0] for q in st_sorted]; st_s = [q[1] for q in st_sorted]

    f_hi = s["ws"][0] if s["ws"] else s["stations"][0][0]
    f_lo = s["stations"][-1][0]

    def kill_top(bm):
        kill = []
        for f in bm.faces:
            c = f.calc_center_median()
            fv = -c.y
            if f.normal.z > .25 and abs(c.x) < s["cab_hw"][0] * float(np.interp(fv, st_f, st_s)) * .96 and f_lo - .02 < fv < f_hi + .02 and c.z > .6:
                kill.append(f)
            elif s.get("bed") and f.normal.z > .25 and s["bed"][1] < fv < s["bed"][0] and abs(c.x) < s["hw"] - .13 and c.z > .6:
                kill.append(f)
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    edit_mesh(hull, kill_top)
    shell = join([hull, cabin], "shell")
    bpy.context.view_layer.update()
    bvh = BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())

    def ray(o, dr):
        loc, nrm, i, dist = bvh.ray_cast(Vector(o), Vector(dr).normalized())
        return (loc, nrm) if loc is not None else (None, None)

    def hit_fb(x, z, fsgn):          # onto nose (fsgn>0) or tail (fsgn<0)
        return ray(V(x, math.copysign(6, fsgn), z), V(0, -math.copysign(1, fsgn), 0) - V(0, 0, 0))

    def hit_side(f, z, side):
        return ray(Vector((side * 6, -f, z)), Vector((-side, 0, 0)))

    def hit_top(x, f):
        return ray(V(x, f, 3.0), Vector((0, 0, -1)))

    def lift(h, k=.003):
        return None if h[0] is None else h[0] + h[1] * k

    d = P2("details")
    hw = s["hw"]
    plates = []

    # ---------------- liner and interior
    liner = make_liner(s)
    ip = P2("interior")
    f0 = s["ws"][0] if s["ws"] else s["stations"][0][0]
    # dashboard, cowl, binnacle, console
    ip.rbox(V(0, f0 - .27, s["belt"] - .24), (1.38, .5, .22), "Interior", .05, 2)
    ip.rbox(V(0, f0 - .42, s["belt"] - .06), (1.3, .3, .1), "Interior", .04, 2)
    ip.rbox(V(-.36, f0 - .42, s["belt"] - .01), (.42, .2, .1), "Interior", .04, 2)
    ip.rbox(V(0, f0 - .58, s["belt"] - .36), (.24, .6, .34), "Interior", .04, 2)
    # steering wheel and column
    wc = V(-.36, f0 - .44, s["belt"] - .12)
    axis = V(0, -1, .42)
    ring = ring_circle(wc, axis, .19, 22)
    ip.tube(ring + [ring[0]], [.014] * 23, "Rubber", 6, cap=False)
    ip.cyl(wc, .035, .035, .05, "Rubber", 8, axis.to_track_quat('Z', 'Y'))
    ip.bar(wc - Vector((.17, 0, 0)), wc + Vector((.17, 0, 0)), .02, .015, "Rubber")
    ip.bar(wc, wc + Vector((0, .0, -.17)), .02, .015, "Rubber")
    ip.cyl(wc + Vector((0, .15, -.1)), .03, .03, .3, "Rubber", 8, axis.to_track_quat('Z', 'Y'))
    # seats
    for si, sf in enumerate(s["seats"]):
        xs = (-.36, .36)
        for x in xs:
            ip.rbox(V(x, sf, .44), (.5, .52, .13), "Interior", .04, 2)
            back = V(x, sf - .27, .74)
            ip.rbox(back, (.5, .12, .56), "Interior", .045, 2, Euler((math.radians(-14), 0, 0)))
            ip.rbox(V(x, sf - .31, 1.13), (.22, .09, .17), "Interior", .04, 2, Euler((math.radians(-10), 0, 0)))
    # shift lever and handbrake
    ip.cyl(V(0, f0 - .64, s["belt"] - .24), .012, .012, .12, "Rubber", 6)
    ip.rbox(V(0, f0 - 1.0, .45), (.2, .45, .2), "Interior", .04, 2)

    # ---------------- front/rear features
    hlx, hlz, hlw, hlh = s["hl"]
    tlx, tlz, tlw, tlh = s["tl"]
    for sd in (-1, 1):
        loc, n = hit_fb(sd * hlx, hlz, 6)
        if loc:
            lens(d, loc, n, (hlw + .035, hlh + .035, .05), "Chrome", .02, inset=.05)           # bezel
            q = n.to_track_quat('Z', 'Y')
            d.lathe([(.02, 0), (.045, .012), (.066, .03), (.074, .045)], "Chrome", n, loc - n * .012, 16)  # reflector cup
            d.sphere(loc + n * .004, (.022, .022, .022), "Headlight", (8, 6))                 # bulb
            lens(d, loc + n * .012 + q @ Vector((hlw * .36, -hlh * .06, 0)), n, (hlw * .3, hlh * .5, .02), "Headlight", .01)  # signal/DRL
            lens(d, loc + n * .012 - q @ Vector((hlw * .36, hlh * .06, 0)), n, (hlw * .26, hlh * .5, .02), "Chrome", .008)
            fl, fn = hit_fb(sd * (hlx - .02), hlz - hlh - .26, 6)
            if fl:
                d.lathe([(.015, 0), (.03, .008), (.04, .02)], "Chrome", fn, fl - fn * .006, 12)
                d.sphere(fl + fn * .004, (.014, .014, .014), "Headlight", (6, 4))
        loc, n = hit_fb(sd * tlx, tlz, -6)
        if loc:
            lens(d, loc, n, (tlw, tlh, .06), "Taillight", .02)
            lens(d, loc + n * .02, n, (tlw * .9, .008, .02), "Rubber", .002) if not s.get("tl_vertical") else lens(d, loc + n * .02, n, (.008, tlh * .9, .02), "Rubber", .002)
            q = n.to_track_quat('Z', 'Y')
            d.lathe([(.012, 0), (.022, .008), (.032, .018)], "Chrome", n, loc + n * .028 + q @ Vector((tlw * .22, tlh * .2, 0)), 10)
            lens(d, loc + q @ Vector((-tlw * .25, -tlh * .28, 0)) + n * .02, n, (tlw * .25, min(tlh * .25, .05), .02), "Headlight", .006)  # reverse lamp
        rl, rn = hit_fb(sd * (tlx - .1), .38, -6)
        if rl:
            lens(d, rl, rn, (.07, .03, .02), "Taillight", .008)                               # reflector
    # grille, intake, bumpers
    loc, n = hit_fb(0, hlz - .06, 6)
    if loc:
        gw = hlx * 1.5
        lens(d, loc, n, (gw, .12, .04), "Rubber", .018)
        for k in range(3):
            lens(d, loc + n * .004 + Vector((0, 0, (k - 1) * .035)), n, (gw * .95, .012, .03), "Chrome", .004, inset=.05)
        lens(d, loc, n, (gw + .03, .135, .02), "Chrome", .01, inset=.1)
    loc, n = hit_fb(0, .43, 6)
    if loc:
        lens(d, loc, n, (hw * 1.1, .13, .045), "Rubber", .03)
        lens(d, loc + Vector((0, 0, .09)), n, (hw * .7, .02, .03), "Rubber", .005)
    for fsgn, zz in ((6, .52), (-6, .52)):
        pts = []
        for i in range(11):
            x = -hw * .76 + i * hw * .152
            l = lift(hit_fb(x, zz, fsgn), .006)
            pts.append(l)
        pts = [q for q in pts if q is not None]
        if len(pts) > 2:
            d.tube(pts, [(.008, .022)] * len(pts), "Chrome", 4)
    # tail lower valance + exhaust
    loc, n = hit_fb(0, .4, -6)
    if loc:
        lens(d, loc, n, (hw * 1.0, .12, .04), "Rubber", .03)
    for sx in ((-.5, .5) if s.get("tail_bumper") else (.55,)):
        d.cyl(V(sx, s["fmin"] + .1, .27), .036, .036, .2, "Chrome", 12, Euler((math.radians(90), 0, 0)))
    # plates with a recessed frame
    for fsgn, tag in ((6, "F"), (-6, "R")):
        pz = s["plate_z"]
        loc, n = hit_fb(0, pz, fsgn)
        if loc:
            q = n.to_track_quat('Z', 'Y')
            q = n.to_track_quat('Z', 'Y')
            lens(d, loc, n, (.37, .2, .014), "Rubber", .006, inset=.5)                  # backing / recess floor
            for ex, ey, ew, eh in ((0, .09, .37, .02), (0, -.09, .37, .02), (-.175, 0, .02, .2), (.175, 0, .02, .2)):
                d.rbox(loc + q @ Vector((ex, ey, 0)) + n * .014, (ew, eh, .028) if False else (ew, eh, .028), "Rubber", 0.0, 1, q)   # raised rim
            lens(d, loc + n * .004, n, (.305, .152, .004), "Plate", .0, inset=.0)
            plates.append((loc + n * .01, 1 if fsgn > 0 else -1))
    # ---------------- sides
    mz = s["mirror_z"]
    zlow = .34
    ztop = s.get("seam_top", s["belt"] - .03)
    for sd in (-1, 1):
        seams = s["seams"]
        # door outline: verticals and bottom edge
        for sf in seams:
            pts = []
            for k in range(9):
                z = zlow + (ztop - zlow) * k / 8
                pts.append(lift(hit_side(sf, z, sd), .002))
            groove(d, pts)
        pts = [lift(hit_side(seams[0] + (seams[-1] - seams[0]) * k / 10, zlow, sd), .002) for k in range(11)]
        groove(d, pts)
        for hf in s["handles"]:
            a, na = hit_side(hf, s["belt"] - .1, sd)
            if a:
                lens(d, a, na, (.14, .035, .03), "Chrome", .01, inset=.2)
                lens(d, a + Vector((0, 0, .0)), na, (.17, .06, .012), "Rubber", .006, inset=.0)
        mx, mf = sd * (hw + .02), s["mirror_f"]
        d.sphere(V(mx, mf, mz), (.05, .085, .06), "CarPaint", (10, 8), Euler((0, 0, math.radians(sd * 10))))
        d.sphere(V(mx - sd * .005, mf - .045, mz), (.04, .02, .05), "Chrome", (8, 6))
        d.bar(V(sd * (hw - .12), mf + .06, mz - .04), V(mx - sd * .03, mf, mz - .01), .03, .02, "CarPaint")
        # sill trim and belt chrome
        a, na = hit_side(1.2, .3, sd); b, nb = hit_side(-1.2, .3, sd)
        if a and b:
            d.bar(a + na * .004, b + nb * .004, .035, .01, "Rubber")
        wins = s["side_win"]
        fa, fb_ = max(w[0] for w in wins), min(w[1] for w in wins)
        pts = [lift(hit_side(fa + (fb_ - fa) * k / 14, s["belt"] + .02, sd), .004) for k in range(15)]
        pts = [q for q in pts if q is not None]
        if len(pts) > 2:
            d.tube(pts, [(.005, .011)] * len(pts), "Chrome", 4, cap=False)
        # arch trim
        for wf in s["wheel_f"]:
            pts = []
            for k in range(15):
                a = math.radians(8 + k * (164 / 14))
                zz = s["wr"] + .012 + (s["wr"] + .095) * math.sin(a)
                ff = wf + (s["wr"] + .095) * math.cos(a)
                pts.append(lift(hit_side(ff, zz, sd), .004))
            pts = [q for q in pts if q is not None]
            if len(pts) > 4:
                d.tube(pts, [(.009, .02)] * len(pts), "Rubber", 4, cap=False)
        # side marker lamps
        for fz, mm in ((s["fmax"] - .3, "Headlight"), (s["fmin"] + .35, "Taillight")):
            a, na = hit_side(fz, .66, sd)
            if a:
                lens(d, a, na, (.1, .035, .02), mm, .008)
    # ---------------- top: hood, trunk, wipers
    if s.get("bonnet"):
        b0, b1 = s["bonnet"]
        wx = hw * .72
        pts = []
        for x, f in ((-wx, b0), (-wx, b1), (wx, b1), (wx, b0)):
            pass
        line = [(-wx, b0 - .02)] + [(-wx, b0 + (b1 - b0) * k / 6) for k in range(1, 7)] + [(-wx + 2 * wx * k / 10, b1) for k in range(1, 10)] + \
               [(wx, b1 - (b1 - b0) * k / 6) for k in range(0, 7)]
        groove(d, [lift(hit_top(x, f), .002) for x, f in line])
    if s.get("trunk"):
        t0, t1 = s["trunk"]
        wx = hw * .7
        line = [(-wx, t0)] + [(-wx, t0 + (t1 - t0) * k / 5) for k in range(1, 6)] + [(-wx + 2 * wx * k / 10, t1) for k in range(1, 10)] + \
               [(wx, t1 - (t1 - t0) * k / 5) for k in range(0, 6)]
        groove(d, [lift(hit_top(x, f), .002) for x, f in line])
    elif s.get("hatch") or s.get("box_rear"):
        for sd in (-1, 1):
            pts = [lift(hit_fb(sd * hw * .82 + 0 * k, .45 + k * .06, -6), .002) for k in range(9)]
            groove(d, pts)
        groove(d, [lift(hit_fb(-hw * .82 + hw * 1.64 * k / 10, .42, -6), .002) for k in range(11)])
    if s["ws"]:
        fb0 = s["ws"][0] - .06
        for wx0, wx1 in ((-.62, -.1), (-.02, .5)):
            a = hit_top(wx0, fb0); b = hit_top(wx1, fb0 - .05)
            if a[0] and b[0]:
                d.bar(a[0] + a[1] * .012, b[0] + b[1] * .012, .018, .008, "Rubber")
                d.bar(a[0] + a[1] * .012, a[0] + a[1] * .012 + (b[0] - a[0]).normalized() * .05, .006, .006, "Rubber")
    if s.get("box_rear"):
        for sd in (-1, 1):
            loc, n = hit_fb(sd * .42, 1.45, -6)
            if loc:
                lens(d, loc, n, (.6, .45, .04), "Glass", .03)
        loc, n = hit_fb(0, 1.0, -6)
        if loc:
            lens(d, loc + Vector((0, 0, .35)), n, (.012, 1.1, .02), "Rubber", .004)
    if s.get("rear_window"):
        rw, rh, rz = s["rear_window"]
        loc, n = hit_fb(0, rz, -6)
        if loc:
            lens(d, loc, n, (rw * 2, rh, .04), "Glass", .03)
    if s.get("bed"):
        b0, b1 = s["bed"]
        zf = .8
        wb = s["hw"] - .16
        d.rbox(V(0, (b0 + b1) / 2, zf), (wb * 2, b0 - b1, .04), "Rubber", .01, 1)
        for sd in (-1, 1):
            d.rbox(V(sd * (s["hw"] - .09), (b0 + b1) / 2, (zf + 1.02) / 2), (.05, b0 - b1, 1.02 - zf), "Rubber", .01, 1)
            d.bar(V(sd * (s["hw"] - .06), b0, 1.045), V(sd * (s["hw"] - .06), b1, 1.045), .09, .035, "Chrome", .01, 1)
        d.rbox(V(0, b0 - .03, (zf + 1.02) / 2 - .02), (wb * 2 + .1, .06, .24), "Rubber", .015, 1)    # low bed headboard
        for sd in (-1, 1):                                                                           # wheel wells inside the bed
            d.rbox(V(sd * (s["hw"] - .09 - .2), s["wheel_f"][1], zf + .12), (.34, .86, .24), "CarPaint", .06, 2)
        d.rbox(V(0, b1 + .02, 1.0), (wb * 2 + .12, .035, .035), "Rubber", .008, 1)                   # tailgate seam
        d.rbox(V(0, b1 + .05, (zf + 1.02) / 2), (wb * 2, .06, 1.02 - zf), "Rubber", .01, 1)
        d.bar(V(-s["hw"] + .06, b1, 1.045), V(s["hw"] - .06, b1, 1.045), .09, .035, "Chrome", .01, 1)
        d.bar(V(-s["hw"] + .1, b0, 1.04), V(s["hw"] - .1, b0, 1.04), .09, .03, "Rubber", .01, 1)
        d.rbox(V(0, s["fmin"] + .06, .46), (hw * 1.9, .12, .1), "Chrome", .03, 2)
    if s.get("sign"):                                              # roof sign box
        rz = max(v[1] for v in s["roof"])
        d.rbox(V(0, -.25, rz + .09), (.62, .22, .17), "Paint", .04, 2)
        d.rbox(V(0, -.25, rz + .18), (.5, .16, .04), "LampGlass", .015, 1)
        d.rbox(V(-.18, -.25, rz + .015), (.05, .2, .03), "Metal", .008, 1)
        d.rbox(V(.18, -.25, rz + .015), (.05, .2, .03), "Metal", .008, 1)
        for sd in (-1, 1):
            d.rbox(V(sd * .3, -.25, rz + .09), (.02, .2, .13), "LampGlass", .006, 1)
    # wheels
    for sd in (-1, 1):
        for wf in s["wheel_f"]:
            wheel(d, s, sd * s["track"], wf, sd)

    # ---------------- uv fixes: plates, dirt map
    pcell = s.get("plate_cell")

    def uvfix(bm):
        uv = bm.loops.layers.uv.verify()
        pi = d.slots.index("Plate") if "Plate" in d.slots else -1
        for f in bm.faces:
            if f.material_index == pi and abs(f.normal.y) > .5:
                c = f.calc_center_median()
                best = min(plates, key=lambda q: (q[0] - c).length) if plates else (c, 1)
                sgn = 1 if f.normal.y < 0 else -1          # front (normal -Y): screen-right = -x
                cell = (pcell if pcell is not None else (idx * 2 + (0 if sgn > 0 else 1)) % 16)
                cu, cv = cell % 4, 3 - cell // 4
                for l in f.loops:
                    p3 = l.vert.co
                    ul = (p3.x - c.x) / .305 * (1 if sgn > 0 else -1)
                    vl = (p3.z - c.z) / .152
                    l[uv].uv = ((cu + .5 + ul * .96) / 4, (cv + .5 + vl * .96) / 4)

    # dirt UV map is generated after joining (needs the whole car) -> below
    dets = d.build2(smooth=40, name="details", uvfix=uvfix)
    ipo = ip.build2(smooth=40, name="interior_parts")
    car = join([shell, dets, liner, ipo], s["name"])
    # UVDirt on the joined mesh
    fb_, ff_ = s["wheel_f"][1], s["wheel_f"][0]

    def umap(fv):
        if fv <= fb_:
            u = .2 * (fv - s["fmin"]) / (fb_ - s["fmin"])
        elif fv <= ff_:
            u = .2 + .6 * (fv - fb_) / (ff_ - fb_)
        else:
            u = .8 + .2 * (fv - ff_) / (s["fmax"] - ff_)
        return max(0.0, min(1.0, u))

    bm = bmesh.new(); bm.from_mesh(car.data)
    layer = bm.loops.layers.uv.get("UVDirt") or bm.loops.layers.uv.new("UVDirt")
    for f in bm.faces:
        fnm = f.normal
        endface = abs(fnm.y) > .6
        for l in f.loops:
            p3 = l.vert.co
            fv = -p3.y
            u = (.5 + .04 * p3.x) if endface else umap(fv)
            l[layer].uv = (u, min(max(p3.z / 2.0, 0), 1))
    bm.to_mesh(car.data); bm.free()
    car.data.update()
    return car


def main():
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = a.split("=")[1].split(",")
    reset_scene()
    if "--tex" in sys.argv or not os.path.exists(os.path.join(TEX, "PlatesV2_Albedo.png")):
        make_plates()
    if "--tex" in sys.argv or not os.path.exists(os.path.join(TEX, "CarDirt_Mask.png")):
        make_dirt()
    for i, s in enumerate(ALL):
        if only and s["name"].split("_")[1] not in only:
            continue
        car = build_car(s, i)
        report(car, s["expect"], .12)
        export_fbx([car], V2_VEH, s["name"])
        short = s["name"].split("_")[1]
        render_item([car], os.path.join(PREVIEWS, "V2s_Car_%s.png" % short), az=35, el=14, size=(1000, 700), car_color=s["color"], ground="asphalt", fit=.62)
        render_item([car], os.path.join(PREVIEWS, "V2s_Car_%s_rear.png" % short), az=145, el=14, size=(1000, 700), car_color=s["color"], ground="asphalt", fit=.62)
        if short in ("Sedan", "Cab", "Pickup"):
            render_item([car], os.path.join(PREVIEWS, "V2s_Car_%s_close.png" % short), az=62, el=24, size=(1000, 700), car_color=s["color"], ground="asphalt", fit=.5)
            render_item([car], os.path.join(PREVIEWS, "V2s_Car_%s_front.png" % short), az=12, el=8, size=(1000, 700), car_color=s["color"], ground="asphalt", fit=.5)


if "--noauto" not in sys.argv:
    main()
