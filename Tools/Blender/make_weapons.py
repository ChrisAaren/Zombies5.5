"""Gun_Pistol / Gun_SMG / Gun_Shotgun / Gun_Rifle / Gun_DMR / Gun_LMG -> Assets/Art/Models/Weapons/*.fbx
Run: blender --background --factory-startup --python make_weapons.py
Pivot = top of the pistol grip behind the trigger (where the right hand closes), +Z (Blender -Y) forward,
`Muzzle` empty at the barrel tip. Original designs. Slots: GunMetal, GunPolymer, GunGrip, Sight.
Authoring: (x right, f forward, z up) relative to the pivot; V(x, f, z) converts.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bl_human import *
from make_body import POSES
from bl_common import aim

M, PL, GR, SI = "GunMetal", "GunPolymer", "GunGrip", "Sight"


class Gun:
    def __init__(self, name):
        self.name = name
        self.P = Part(name)
        self.muzzle = (0, 0, 0)

    # --- primitives (ranges in f / z, half width hw)
    def box(self, slot, f0, f1, z0, z1, hw, cx=0.0, bevel=.0025, seg=1, rot=None):
        self.P.rbox(V(cx, (f0 + f1) / 2, (z0 + z1) / 2), (2 * hw, f1 - f0, z1 - z0), slot, bevel, seg, rot)

    def slab(self, slot, top_f, top_z, length, ang, hw, depth, bevel=.003, seg=1, cx=0.0):
        """Box hanging from (top_f, top_z), tilted `ang` degrees (+ = bottom swings rearward)."""
        a = math.radians(ang)
        cf = top_f - math.sin(a) * length / 2
        cz = top_z - math.cos(a) * length / 2
        self.P.rbox(V(cx, cf, cz), (2 * hw, depth, length), slot, bevel, seg, Euler((a, 0, 0)))

    def prism(self, slot, pts, hw, cx=0.0, bevel=.003, seg=1):
        """Side-profile polygon pts [(f, z)...] (convex-ish) extruded across x by +-hw, with beveled edges."""
        bm = self.P.bm
        before = self.P.snapshot()
        lo = [bm.verts.new(V(cx - hw, f, z)) for f, z in pts]
        hi = [bm.verts.new(V(cx + hw, f, z)) for f, z in pts]
        n = len(pts)
        bm.faces.new(lo); bm.faces.new(list(reversed(hi)))
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([lo[i], lo[j], hi[j], hi[i]])
        new = [f for f in bm.faces if f not in before]
        bmesh.ops.recalc_face_normals(bm, faces=new)
        if bevel > 0:
            edges = list({e for f in new for e in f.edges})
            bmesh.ops.bevel(bm, geom=edges, offset=bevel, segments=seg, affect='EDGES', profile=0.5)
        self.P._tag(before, slot)

    def cyl(self, slot, f0, f1, z, r, r2=None, cx=0.0, n=14):
        r2 = r if r2 is None else r2
        self.P.cyl(V(cx, (f0 + f1) / 2, z), r, r2, f1 - f0, slot, n, aim((0, -1, 0)))

    def cylz(self, slot, f, z0, z1, r, cx=0.0, n=10, r2=None):
        r2 = r if r2 is None else r2
        self.P.cyl(V(cx, f, (z0 + z1) / 2), r, r2, abs(z1 - z0), slot, n)

    def rail(self, f0, f1, z, hw=.011, h=.006, pitch=.0075, slot=M):
        self.box(slot, f0, f1, z - .003, z, hw * .8, bevel=0)
        f = f0
        while f + .004 <= f1:
            self.box(slot, f, f + .004, z, z + h, hw, bevel=0)
            f += pitch

    def vents(self, f0, f1, z, r, hw, rows=2, slot=M, n=8):
        """Row of round cooling holes drawn as dark inset discs on both sides (cylinders along x)."""
        f = f0
        while f <= f1:
            for s in (-1, 1):
                self.P.cyl(V(s * hw, f, z), r, r, .003, slot, n, aim((s, 0, 0)))
            f += 2.6 * r

    def ribs(self, slot, f0, f1, z, r, step=.008, n=12):
        f = f0
        while f <= f1:
            self.cyl(slot, f, f + .0035, z, r, n=n)
            f += step

    def finish(self, muzzle):
        ob = self.P.build(smooth=40, flat=())
        ob.name = ob.data.name = self.name
        # keep hard-edged: use auto smooth by angle already applied; join mesh stays at origin
        m = bpy.data.objects.new("Muzzle_" + self.name, None)
        m.empty_display_type = 'ARROWS'; m.empty_display_size = .04
        bpy.context.scene.collection.objects.link(m)
        m.location = V(*muzzle)
        m.parent = ob
        return ob, m


# ------------------------------------------------------------------ pistol (~0.20 m)
def pistol():
    g = Gun("Gun_Pistol")
    zb = .020
    g.box(M, -.048, .152, .004, .040, .0155, bevel=.004, seg=3)                     # slide
    g.box(M, .152, .158, .010, .034, .0125, bevel=.002)                             # muzzle face / bushing
    g.cyl(M, .144, .164, zb + .001, .0055, n=12)                                    # barrel tip
    for i in range(6):                                                              # rear serrations
        g.box(M, -.040 + i * .0065, -.036 + i * .0065, .006, .036, .0172, bevel=0)
    for i in range(4):                                                              # front serrations
        g.box(M, .105 + i * .0065, .109 + i * .0065, .006, .036, .0172, bevel=0)
    g.box(M, .018, .062, .026, .037, .0168, bevel=.0015)                            # ejection port insert
    g.box(PL, -.034, .125, -.022, .004, .0135, bevel=.003, seg=3)                   # frame / dust cover
    g.box(PL, .040, .125, -.036, -.022, .0125, bevel=.003)                          # rail section lower
    for i in range(3):
        g.box(M, .075 + i * .014, .083 + i * .014, -.030, -.022, .0128, bevel=0)
    g.slab(PL, -.002, .0, .118, 14, .0155, .040, bevel=.004, seg=3)                 # grip
    for s in (-1, 1):
        g.slab(GR, -.002, -.006, .102, 14, .0014, .034, bevel=.0008, cx=s * .0165)  # grip panels
    # trigger guard
    g.box(PL, .052, .060, -.052, -.022, .006, bevel=.0015)
    g.box(PL, .004, .060, -.052, -.044, .006, bevel=.0015)
    g.box(M, .018, .024, -.044, -.018, .0035, bevel=.0012)                         # trigger
    g.box(PL, -.040, -.030, -.012, .012, .0132, bevel=.002)                         # beavertail
    g.box(M, -.056, -.046, .020, .034, .005, bevel=.002)                            # hammer / rear plate
    g.box(M, -.010, .006, -.013, -.008, .0168, bevel=.001)                          # slide stop pin
    g.slab(M, -.022, -.118, .014, 14, .0145, .040, bevel=.002)                      # mag base plate
    # grip texture ridges and slide cut details
    for i in range(7):
        for s in (-1, 1):
            g.slab(GR, -.006 - i * .0006, -.020 - i * .012, .006, 14, .0017, .034, bevel=0, cx=s * .0165)
    g.box(M, .100, .150, .030, .036, .0160, bevel=.002, seg=2)
    g.box(PL, .090, .130, -.012, -.004, .0140, bevel=.002, seg=2)
    # sights (emissive dots)
    g.box(SI, .140, .146, .040, .047, .0016, bevel=0)
    g.box(M, .136, .150, .040, .043, .0030, bevel=.0008)
    for s in (-1, 1):
        g.box(SI, -.044, -.039, .040, .046, .0016, cx=s * .0055, bevel=0)
    g.box(M, -.048, -.036, .040, .043, .0125, bevel=.0008)
    return g.finish((0, .164, zb))


# ------------------------------------------------------------------ SMG (~0.50 m)
def smg():
    g = Gun("Gun_SMG")
    zb = .040
    g.box(M, -.095, .175, .008, .062, .023, bevel=.004, seg=2)                      # receiver
    g.box(PL, -.095, .105, -.014, .008, .021, bevel=.003, seg=2)                    # lower receiver
    g.box(M, .175, .280, .016, .066, .0245, bevel=.004, seg=2)                      # shroud
    g.vents(.185, .268, .043, .0058, .0245)
    g.box(M, .280, .300, .022, .058, .017, bevel=.003)                              # front block
    g.cyl(M, .290, .325, zb + .004, .0095, n=16)                                    # muzzle device
    g.cyl(M, .315, .335, zb + .004, .0115, .0095, n=16)
    g.rail(-.08, .17, .062, hw=.0105)
    g.box(M, -.070, -.010, .026, .044, .0245, bevel=.001)                           # port cover recess
    g.box(M, .030, .062, .050, .066, .0255, bevel=.003)                             # charging block
    g.box(M, .034, .058, .066, .075, .005, bevel=.002)
    g.slab(PL, .0, .0, .122, 20, .0185, .042, bevel=.004, seg=2)                    # pistol grip
    for s in (-1, 1):
        g.slab(GR, .0, -.008, .105, 20, .0014, .036, bevel=.0008, cx=s * .0195)
    g.slab(PL, .078, -.012, .205, -6, .0175, .036, bevel=.004, seg=2)               # magazine (long, slight forward lean)
    g.slab(M, .104, -.214, .012, -6, .018, .038, bevel=.002)
    g.box(PL, .040, .130, -.036, -.012, .011, bevel=.003)                           # trigger guard / foregrip block
    g.box(M, .012, .018, -.034, -.010, .0035, bevel=.001)
    g.box(PL, .118, .132, -.052, -.012, .011, bevel=.003)
    # folding stock (extended): two arms + butt plate
    for s in (-1, 1):
        g.box(M, -.205, -.095, .030, .038, .0045, cx=s * .019, bevel=.0015)
    g.box(M, -.205, -.196, .000, .056, .026, bevel=.0035, seg=2)
    g.box(PL, -.212, -.205, .004, .052, .024, bevel=.003)
    g.box(M, -.130, -.095, .014, .030, .0185, bevel=.002)                           # hinge block
    # sights
    g.box(M, .242, .252, .066, .082, .0035, bevel=.001); g.box(SI, .244, .250, .078, .084, .0018, bevel=0)
    g.box(M, -.092, -.070, .062, .070, .012, bevel=.001)
    for s in (-1, 1):
        g.box(SI, -.088, -.082, .070, .076, .0014, cx=s * .0045, bevel=0)
    return g.finish((0, .335, zb + .004))


# ------------------------------------------------------------------ pump shotgun (~0.95 m)
def shotgun():
    g = Gun("Gun_Shotgun")
    zb = .036
    g.box(M, -.085, .130, .006, .060, .0225, bevel=.004, seg=2)                     # receiver
    g.box(M, -.040, .010, .056, .064, .0125, bevel=.002)
    g.cyl(M, .130, .640, zb + .008, .0118, n=16)                                    # barrel
    g.cyl(M, .130, .600, zb - .026, .0128, n=14)                                    # magazine tube
    g.cyl(M, .600, .612, zb - .026, .0155, n=14)                                    # tube cap
    g.box(M, .130, .600, zb - .018 + .001, zb - .012, .0032, bevel=0)                  # barrel/tube web
    g.cyl(PL, .310, .475, zb - .026, .0215, n=16)                                   # pump forend
    g.ribs(GR, .322, .462, zb - .026, .0228, step=.0125, n=14)
    g.rail(.0, .12, .060, hw=.0115, h=.004, pitch=.0095)
    g.slab(PL, .0, .0, .120, 18, .0185, .042, bevel=.004, seg=2)                    # pistol grip
    for s in (-1, 1):
        g.slab(GR, .0, -.008, .104, 18, .0014, .036, bevel=.0008, cx=s * .0196)
    g.box(PL, .040, .090, -.030, .006, .0115, bevel=.003)                           # trigger guard
    g.box(M, .012, .018, -.034, -.010, .0035, bevel=.001)
    # fixed stock: wrist -> cheek -> butt
    g.prism(PL, [(-.085, .060), (-.20, .052), (-.300, .044), (-.322, .040), (-.325, -.068), (-.300, -.072), (-.200, -.020),
                 (-.120, -.008), (-.085, -.004)], .0215, bevel=.005, seg=2)
    g.prism(GR, [(-.322, .038), (-.3295, .036), (-.3325, -.070), (-.325, -.0685)], .0218, bevel=.0015)   # recoil pad
    # sights
    g.cyl(M, .560, .566, zb + .028, .0012, n=6)
    g.box(M, .545, .562, zb + .017, zb + .021, .002, bevel=0)
    g.box(SI, .552, .558, zb + .021, zb + .030, .0016, bevel=0)
    g.box(M, .625, .640, zb + .020, zb + .028, .0016, bevel=0)
    return g.finish((0, .640, zb + .008))


# ------------------------------------------------------------------ assault rifle (~0.85 m)
def rifle():
    g = Gun("Gun_Rifle")
    zb = .050
    g.box(M, -.075, .255, .024, .080, .0215, bevel=.004, seg=2)                     # upper receiver
    g.box(PL, -.075, .115, -.018, .024, .0205, bevel=.004, seg=2)                   # lower receiver
    g.box(PL, .255, .470, .014, .084, .0235, bevel=.005, seg=2)                     # handguard
    g.vents(.272, .455, .049, .0065, .0238, n=8)
    g.cyl(M, .470, .600, zb + .005, .0105, n=16)                                    # barrel
    g.cyl(M, .600, .665, zb + .005, .0145, .0145, n=16)                             # muzzle brake
    for i in range(4):
        g.ribs(M, .612 + i * .012, .614 + i * .012, zb + .005, .0160, n=14)
    g.box(M, .470, .500, zb - .014, zb + .024, .0135, bevel=.003)                     # gas block
    g.rail(-.070, .255, .080, hw=.0105)
    g.rail(.255, .470, .084, hw=.0105)
    g.box(M, .020, .060, .056, .074, .0225, bevel=.002)                             # ejection region
    g.box(M, .160, .175, .060, .074, .0235, bevel=.001)                             # handguard cap line
    g.slab(PL, -.004, -.012, .118, 22, .0185, .040, bevel=.004, seg=2)              # pistol grip
    for s in (-1, 1):
        g.slab(GR, -.004, -.020, .100, 22, .0014, .034, bevel=.0008, cx=s * .0195)
    g.box(PL, .040, .110, -.040, -.010, .0115, bevel=.003)                          # trigger guard
    g.box(M, .018, .024, -.040, -.012, .0035, bevel=.001)
    g.slab(PL, .102, -.016, .185, -12, .0195, .040, bevel=.004, seg=2)              # magazine, curved forward
    g.slab(PL, .132, -.186, .090, -22, .0195, .038, bevel=.004, seg=1)
    # stock (collapsible): buffer tube + stock
    g.cyl(M, -.150, -.075, .036, .0185, n=14)
    g.prism(PL, [(-.150, .060), (-.215, .060), (-.232, .054), (-.236, -.072), (-.218, -.078), (-.175, -.034), (-.150, .004)],
            .0200, bevel=.005, seg=2)
    g.prism(GR, [(-.232, .052), (-.2395, .050), (-.2435, -.072), (-.236, -.0735)], .0202, bevel=.0015)
    # optic: reflex sight body + lens
    g.box(M, .040, .120, .086, .098, .0165, bevel=.003, seg=2)
    g.box(M, .040, .120, .098, .124, .0165, bevel=.003, seg=2)
    g.box(M, .115, .127, .092, .126, .018, bevel=.002)
    g.box(M, .026, .040, .088, .120, .018, bevel=.002)
    g.box(SI, .119, .121, .100, .122, .0135, bevel=0)                               # lens
    # front sight post / rear flip
    g.box(M, .490, .510, .064, .108, .0035, bevel=.001)
    g.box(M, .495, .505, .104, .114, .0095, bevel=.0008)
    g.box(SI, .498, .502, .108, .116, .0012, bevel=0)
    return g.finish((0, .665, zb + .005))


# ------------------------------------------------------------------ marksman rifle (~1.05 m)
def dmr():
    g = Gun("Gun_DMR")
    zb = .052
    g.box(M, -.085, .270, .020, .084, .0225, bevel=.004, seg=2)                     # receiver
    g.box(PL, -.085, .120, -.020, .020, .0205, bevel=.004, seg=2)
    g.box(PL, .270, .520, .020, .082, .0200, bevel=.005, seg=2)                     # slim free-float handguard
    g.vents(.290, .505, .051, .0052, .0200, n=8)
    g.cyl(M, .520, .700, zb + .004, .0100, n=16)                                    # long barrel
    g.cyl(M, .700, .762, zb + .004, .0150, n=16)                                    # compensator
    for i in range(3):
        g.ribs(M, .712 + i * .014, .714 + i * .014, zb + .004, .0165, n=14)
    g.rail(-.080, .270, .084, hw=.0105)
    g.rail(.270, .520, .082, hw=.0105)
    g.slab(PL, -.004, -.012, .118, 18, .0185, .040, bevel=.004, seg=2)
    for s in (-1, 1):
        g.slab(GR, -.004, -.020, .100, 18, .0014, .034, bevel=.0008, cx=s * .0195)
    g.box(PL, .040, .110, -.040, -.010, .0115, bevel=.003)
    g.box(M, .018, .024, -.040, -.012, .0035, bevel=.001)
    g.slab(PL, .104, -.016, .130, -3, .0185, .044, bevel=.004, seg=2)               # straight 20-rd mag
    # fixed stock with cheek piece
    g.prism(PL, [(-.085, .066), (-.20, .084), (-.282, .062), (-.296, .050), (-.299, -.094), (-.272, -.100), (-.200, -.034),
                 (-.130, -.012), (-.085, -.008)], .0210, bevel=.005, seg=2)
    g.prism(GR, [(-.296, .048), (-.3035, .046), (-.3065, -.094), (-.2995, -.0955)], .0213, bevel=.0015)
    # long scope on rings
    sz = .122
    g.cyl(M, -.010, .235, sz, .0165, n=20)                                          # main tube
    g.cyl(M, .235, .300, sz, .0165, .0230, n=20)                                    # objective bell
    g.cyl(M, -.060, -.010, sz, .0200, .0165, n=20)                                  # eyepiece
    g.cyl(SI, .299, .3015, sz, .0215, n=20)                                         # front lens
    g.cyl(SI, -.0605, -.059, sz, .0150, n=20)                                       # rear lens
    for f in (.030, .180):
        g.box(M, f, f + .020, .086, sz - .008, .0120, bevel=.002)                   # rings
        g.cylz(M, f + .010, sz + .005, sz + .0285, .0105, n=14)
    g.cylz(M, .105, sz + .010, sz + .028, .0085, n=12)                               # elevation turret
    g.cyl(M, .105, .125, sz, .0058, cx=.0215, n=10)                                  # windage turret
    return g.finish((0, .762, zb + .004))


# ------------------------------------------------------------------ light machine gun (~1.0 m)
def lmg():
    g = Gun("Gun_LMG")
    zb = .056
    g.box(M, -.090, .245, .020, .092, .0245, bevel=.004, seg=2)                     # receiver
    g.box(PL, -.090, .110, -.024, .020, .0215, bevel=.004, seg=2)
    g.box(M, .245, .470, .026, .088, .0195, bevel=.004, seg=2)                      # perforated shroud
    g.vents(.260, .460, .058, .0070, .0197, n=8)
    g.cyl(M, .470, .700, zb + .004, .0135, n=16)                                    # heavy barrel
    g.cyl(M, .700, .745, zb + .004, .0185, n=16)                                    # flash hider
    for i in range(3):
        g.ribs(M, .708 + i * .012, .710 + i * .012, zb + .004, .0200, n=14)
    g.rail(-.085, .245, .092, hw=.0115)
    g.box(M, .060, .105, .098, .112, .0155, bevel=.003)                              # carry handle (rear base)
    g.box(M, .060, .340, .112, .124, .0155, bevel=.003, seg=2)
    g.box(M, .325, .345, .092, .124, .0155, bevel=.003)
    g.slab(PL, -.004, -.014, .120, 18, .0195, .042, bevel=.004, seg=2)              # pistol grip
    for s in (-1, 1):
        g.slab(GR, -.004, -.022, .102, 18, .0014, .036, bevel=.0008, cx=s * .0205)
    g.box(PL, .040, .110, -.044, -.012, .0115, bevel=.003)
    g.box(M, .018, .024, -.044, -.014, .0035, bevel=.001)
    # box magazine
    g.box(PL, .035, .175, -.150, -.024, .037, bevel=.006, seg=2)
    g.box(M, .030, .180, -.160, -.150, .038, bevel=.003)
    g.box(GR, .036, .174, -.126, -.116, .0382, bevel=.0015)
    # stock
    g.prism(PL, [(-.090, .078), (-.20, .080), (-.270, .066), (-.286, .056), (-.290, -.106), (-.262, -.112), (-.190, -.044),
                 (-.130, -.026), (-.090, -.024)], .0225, bevel=.005, seg=2)
    g.prism(GR, [(-.286, .054), (-.2935, .052), (-.2975, -.106), (-.2905, -.1075)], .0228, bevel=.0015)
    # folded bipod (legs along the barrel underside)
    for s in (-1, 1):
        g.box(M, .395, .520, .030, .036, .0045, cx=s * .014, bevel=.001)
    g.box(M, .385, .405, .026, .050, .020, bevel=.002)
    # sights
    g.box(M, .500, .520, .090, .122, .0035, bevel=.001); g.box(SI, .506, .514, .118, .126, .0016, bevel=0)
    g.box(M, .020, .060, .092, .104, .010, bevel=.001)
    g.box(SI, .044, .046, .104, .110, .0016, bevel=0)
    return g.finish((0, .745, zb + .004))


GUNS = [pistol, smg, shotgun, rifle, dmr, lmg]


def export_gun(ob, muz):
    d = os.path.join(C.MODELS, "Weapons")
    C.ensure_dir(d)
    for o in bpy.data.objects:                     # the exported empty must be named exactly 'Muzzle'
        if o.type == 'EMPTY' and o.name == 'Muzzle':
            o.name = 'Muzzle_' + o.parent.name
    muz.name = 'Muzzle'
    select_only(ob, muz)
    bpy.ops.export_scene.fbx(filepath=os.path.join(d, ob.name + ".fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types={'MESH', 'EMPTY'})


def main():
    C.reset_scene()
    guns = []
    for fn in GUNS:
        ob, muz = fn()
        mn, mx = C.dims(ob)
        length = mx.y * -1 - (-mn.y) * -1 if False else (-mn.y) - (-mx.y)
        print("GUN %-12s tris=%d length=%.3f muzzle(f)=%.3f slots=%s" % (ob.name, C.tri_count(ob), length, -muz.location.y,
              [m.name for m in ob.data.materials]))
        guns.append((ob, muz))
        ob.hide_render = muz.hide_render = True
    for ob, muz in guns:
        export_gun(ob, muz)
    P = C.PREVIEWS
    for ob, muz in guns:
        ob.hide_render = False
        mn, mx = C.dims(ob)
        cf = -(mn.y + mx.y) / 2
        cz = (mn.z + mx.z) / 2
        sc = max((mx.y - mn.y) * 1.25, (mx.z - mn.z) * 1.4 / .64)
        for az, el, tag in ((90, 12, "side"), (35, 14, "")):
            render_view([ob], os.path.join(P, ob.name + ("_" + tag if tag else "") + ".png"), az=az, el=el,
                        size=(1000, 640), scale=sc, target=(0, -cf, cz), floor=False)
        ob.hide_render = True
    # all guns stacked for a size comparison
    for i, (ob, muz) in enumerate(guns):
        ob.location = (0, 0, -i * .30); ob.hide_render = False
    render_view([o for o, _ in guns], os.path.join(P, "Guns_all_side.png"), az=80, el=8, size=(1500, 1500), scale=1.5,
                target=(0, -.28, -.9), floor=False)
    for ob, _ in guns:
        ob.location = (0, 0, 0)
    # ---- rifle in the character's hands
    body, rig = build_body()
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o not in [x for x, _ in guns] and o is not body:
            bpy.data.objects.remove(o, do_unlink=True)
    rifle_ob = [o for o, _ in guns if o.name == "Gun_Rifle"][0]
    hold_preview(body, rig, guns)


def hold_preview(body, rig, guns):
    from mathutils import Matrix
    P = C.PREVIEWS
    # hand position: ArmR hanging: hand centre ~ local (0, ?, -.63) in arm space; with the arm raised forward the hand
    # grips the pistol grip. The bone ArmR head = (0.21, 1.50); the hand's palm centre sits ~0.66 m along the arm.
    arm_rest = arm_matrix(1)
    hand_local = Vector((-.012, .02, -.625))           # palm centre in arm-part space (x inward, f, z)
    for gname, tag in (("Gun_Rifle", "rifle"), ("Gun_Pistol", "pistol"), ("Gun_SMG", "smg")):
        gun = [o for o, _ in guns if o.name == gname][0]
        b2, r2 = instance(body, rig, (0, 0, 0))
        spec = {"ArmR": (-80, 0, 0), "ArmL": (-80, 0, 32)}
        pose(r2, spec)
        g2 = clone_into_scene(gun)
        g2.parent = None
        # rest matrix: gun pivot at the hand centre, barrel forward; the hand's long axis (down the arm) is the gun's
        # grip axis rotated: with the arm raised forward the hand points forward, so tilt the gun grip (down axis)
        # into the hand: gun local -Z (grip) maps to the hand's thumb side. We place the pivot at the palm centre.
        hp = arm_rest @ V(*hand_local)
        rest = Matrix.Translation(hp) @ Matrix.Rotation(math.radians(80), 4, 'X')
        place(g2, r2, "ArmR", rest)
        gun.hide_render = False
        for az, nm in ((90, "side"), (40, "3q"), (0, "front")):
            render_view([b2, g2], os.path.join(P, "Hold_%s_%s.png" % (tag, nm)), az=az, el=8, size=(900, 900), scale=1.1,
                        target=(0, -.2, 1.35), floor=False)
        bpy.data.objects.remove(g2, do_unlink=True)
        bpy.data.objects.remove(b2, do_unlink=True)
        bpy.data.objects.remove(r2, do_unlink=True)


if __name__ == "__main__":
    main()
