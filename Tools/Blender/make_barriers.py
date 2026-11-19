"""District barrier kit -> Assets/Art/Models/Props/{Barrier_FencePanel,Barrier_Jersey,Gate_Terminal,Barrier_Sign}.fbx
plus Assets/Art/Textures/Fence_Albedo.png (tileable chain link, alpha cutout, 1 m per tile).
Run: blender --background --factory-startup --python make_barriers.py
Authoring uses (x, f, z) = (right, forward, up) -> V(x, f, z). Pivot is bottom centre, front faces +Z (f > 0).
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import numpy as np
import bl_common as C
from bl_common import V, Part, mat
from mathutils import Euler
from bl_human import render_view

C.COLORS.update({"Hazard": (.95, .75, .05), "KioskScreen": (.2, .8, 1.), "Fence": (.62, .65, .68)})

TEX_OUT = os.path.join(C.ROOT, "Assets", "Art", "Textures")


def box(P, slot, x0, x1, f0, f1, z0, z1, bevel=.0, seg=1):
    P.rbox(V((x0 + x1) / 2, (f0 + f1) / 2, (z0 + z1) / 2), (x1 - x0, f1 - f0, z1 - z0), slot, bevel, seg)


def hull(P, pts, slot):
    """Closed convex solid from points (Blender coords), tagged with slot."""
    before = P.snapshot()
    vs = [P.bm.verts.new(p) for p in pts]
    bmesh.ops.convex_hull(P.bm, input=vs, use_existing_faces=False)
    P._tag(before, slot)


# ------------------------------------------------------------------ chain-link texture
def fence_texture(n=10, size=512):
    N = size
    yy, xx = np.mgrid[0:N, 0:N]
    u = (xx + .5) / N
    v = (yy + .5) / N
    rng = np.random.default_rng(5)
    wire = 4.2 / N          # wire width in uv units
    aa = 1.3 / N
    amp = .20

    def fam(sign):
        s = u + sign * v
        zig = amp * np.sin(2 * np.pi * (u - sign * v) * n)
        return np.abs((s * n + zig + .5) % 1.0 - .5) / n / np.sqrt(2)
    d = np.minimum(fam(1), fam(-1))
    alpha = np.clip((wire * .5 - d) / aa + .5, 0, 1)
    edge = np.clip(d / (wire * .5), 0, 1)
    base = np.array([.66, .69, .72])
    shade = (1.0 - .35 * edge ** 2)[..., None]
    grain = (1 + .12 * rng.normal(size=(N, N)))[..., None]
    col = base * shade * grain
    low = rng.normal(size=(N // 16, N // 16))
    low = np.kron(low, np.ones((16, 16)))
    for _ in range(3):
        low = (np.roll(low, 1, 0) + np.roll(low, -1, 0) + np.roll(low, 1, 1) + np.roll(low, -1, 1) + low) / 5
    rust = np.clip((low - .35) * 2.2, 0, 1)[..., None]
    col = col * (1 - .55 * rust) + np.array([.42, .24, .12]) * .55 * rust
    # transparent texels keep the wire colour so mip filtering has no dark fringe
    rgba = np.concatenate([np.clip(col, 0, 1), alpha[..., None]], -1)[::-1].astype(np.float32)
    img = bpy.data.images.new("Fence_Albedo", N, N, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.alpha_mode = 'STRAIGHT'
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = os.path.join(TEX_OUT, "Fence_Albedo.png")
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


# ------------------------------------------------------------------ fence panel (3.0 x 2.6, depth 0.5 at the feet)
def fence_panel():
    P = Part("Barrier_FencePanel")
    W, H = 3.0, 2.6
    px = W / 2 - .26
    for s in (-1, 1):
        x = s * px
        P.cyl(V(x, 0, H / 2 + .1), .042, .042, H - .2, "Metal", 12)                      # galvanized post
        P.cyl(V(x, 0, H - .02), .05, .05, .05, "Metal", 12)                               # cap
        box(P, "Concrete", x - .25, x + .25, -.25, .25, 0, .22, .03, 2)                   # scuffed concrete foot
        box(P, "Concrete", x - .17, x + .17, -.17, .17, .22, .36, .02, 1)
        box(P, "Metal", x - .07, x + .07, -.05, .05, .36, .40, .005)                      # base collar
    for z, r in ((H - .08, .026), (.46, .02), (1.3, .016)):
        P.cyl(V(0, 0, z), r, r, W - .12, "Metal", 10, rot=Euler((0, math.radians(90), 0)))
    for i in range(6):
        x = -W / 2 + .45 + i * .42
        P.cyl(V(x, 0, H - .1), .004, .004, .06, "Metal", 6)
    # tension band on both faces
    P.face([V(-px + .03, .028, .50), V(px - .03, .028, H - .12), V(px - .03, .028, H - .15), V(-px + .03, .028, .47)], "Metal")
    P.face([V(-px + .03, -.028, .47), V(px - .03, -.028, H - .15), V(px - .03, -.028, H - .12), V(-px + .03, -.028, .50)], "Metal")
    # chain-link infill: one quad, double sided in Unity (alpha cutout, 1 m per texture tile)
    P.face([V(-px, 0, .5), V(px, 0, .5), V(px, 0, H - .1), V(-px, 0, H - .1)], "Fence")
    ob = P.build(smooth=40)
    ob.name = ob.data.name = "Barrier_FencePanel"
    return ob


# ------------------------------------------------------------------ concrete jersey block (2.0 x 0.8 x 0.6)
JPROF = [(.30, 0.0), (.30, .09), (.19, .33), (.12, .80)]


def jersey_hw(h):
    for (w0, h0), (w1, h1) in zip(JPROF, JPROF[1:]):
        if h0 <= h <= h1:
            return w0 + (w1 - w0) * (h - h0) / max(h1 - h0, 1e-6)
    return JPROF[-1][0]


def jersey():
    P = Part("Barrier_Jersey")
    L = 1.0
    ring = [(f, h) for f, h in JPROF] + [(-f, h) for f, h in reversed(JPROF)]
    P.loft([[V(-L, f, h) for f, h in ring], [V(L, f, h) for f, h in ring]], "Concrete")
    # worn hazard stripes per side: slanted, uneven, missing in places
    keep = {0: (0, 1, 3, 4, 6), 1: (0, 2, 3, 5, 6)}
    for si, s in enumerate((-1, 1)):
        for i in keep[si]:
            xa = -.88 + i * .27
            h0, h1 = .40, .74
            quad = [(xa, h0), (xa + .11, h0), (xa + .26, h1), (xa + .15, h1)]
            inner = [V(x, s * (jersey_hw(h) - .012), h) for x, h in quad]
            outer = [V(x, s * (jersey_hw(h) + .005), h) for x, h in quad]
            hull(P, inner + outer, "Hazard")
    box(P, "Concrete", -L - .01, -L + .03, -.30, .30, 0, .06, .01)
    box(P, "Concrete", L - .03, L + .01, -.30, .30, 0, .06, .01)
    ob = P.build(smooth=30)
    ob.name = ob.data.name = "Barrier_Jersey"
    return ob


# ------------------------------------------------------------------ gate terminal (0.5 x 1.3 x 0.4)
def terminal():
    P = Part("Gate_Terminal")
    box(P, "Metal", -.25, .25, -.20, .20, 0, .07, .012, 2)
    for sx in (-1, 1):
        for sf in (-1, 1):
            P.cyl(V(sx * .20, sf * .15, .08), .018, .018, .02, "Metal", 8)
    box(P, "Paint", -.12, .12, -.10, .10, .07, .92, .012, 2)
    box(P, "Hazard", -.125, .125, -.105, .105, .22, .32, .004)
    box(P, "Metal", -.14, .14, -.12, .12, .07, .10, .008)
    box(P, "Paint", -.21, .21, -.16, .16, .92, 1.27, .02, 2)
    box(P, "Metal", -.23, .23, -.19, .20, 1.27, 1.30, .01, 1)
    box(P, "Metal", -.185, .185, .157, .168, .96, 1.20, .008)
    box(P, "KioskScreen", -.16, .16, .160, .172, .99, 1.17, .002)
    P.cyl(V(0, .17, .94), .022, .022, .02, "Metal", 12, rot=Euler((math.radians(90), 0, 0)))
    ob = P.build(smooth=40)
    ob.name = ob.data.name = "Gate_Terminal"
    return ob


# ------------------------------------------------------------------ sign plate (1.2 x 0.6)
def sign():
    P = Part("Barrier_Sign")
    W, H, T = 1.2, .6, .03
    box(P, "Paint", -W / 2, W / 2, 0, T, 0, H, .01, 2)
    for z0, z1 in ((.0, .05), (H - .05, H)):
        box(P, "Hazard", -W / 2 + .02, W / 2 - .02, T, T + .004, z0 + .006, z1 - .006)
    for sx in (-1, 1):
        for z in (.09, H - .09):
            P.cyl(V(sx * (W / 2 - .06), T + .004, z), .014, .014, .012, "Metal", 8, rot=Euler((math.radians(90), 0, 0)))
    for sx in (-1, 1):
        box(P, "Metal", sx * .38 - .03, sx * .38 + .03, -.02, 0, .04, H - .04, .004)
    ob = P.build(smooth=40)
    ob.name = ob.data.name = "Barrier_Sign"
    return ob


def main():
    C.reset_scene()
    fence_texture()
    fp = fence_panel(); C.report(fp, (3.0, 2.6, .5))
    jr = jersey(); C.report(jr, (2.0, .8, .6))
    tm = terminal(); C.report(tm, (.5, 1.3, .4))
    sg = sign(); C.report(sg, (1.2, .6, .05))
    for ob in (fp, jr, tm, sg):
        ob.location = (0, 0, 0)
        C.export_fbx([ob], "Props", ob.name)
    P = C.PREVIEWS
    jr.location = (3.5, 0, 0)
    tm.location = (6, 0, 0)
    sg.location = (8, 0, 0)
    render_view([fp, jr, tm, sg], os.path.join(P, "Barriers.png"), az=30, el=12, size=(1600, 700), scale=7.5, target=(4, 0, 1.2), floor=False)


if __name__ == "__main__":
    main()
