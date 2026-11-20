"""Gun_Pistol / Gun_SMG / Gun_Shotgun / Gun_Rifle / Gun_DMR / Gun_LMG -> Assets/Art/Models/Weapons/*.fbx
Run: blender --background --factory-startup --python make_weapons.py
Pivot = top of the pistol grip behind the trigger (where the right hand closes), +Z (Blender -Y) forward,
`Muzzle` empty at the barrel tip. Original designs. Slots: GunMetal, GunPolymer, GunGrip, Sight (tiny emissive dots only), ScopeGlass (lenses), Warhead (Proj_Grenade40).
Options (after --): --out <dir>  export dir (default Assets/Art/Models/Weapons); previews go to <out>/previews when --out is given.
Authoring: (x right, f forward, z up) relative to the pivot; V(x, f, z) converts.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bl_human import *
from make_body import POSES
from bl_common import aim

M, PL, GR, SI = "GunMetal", "GunPolymer", "GunGrip", "Sight"
SG, WH = "ScopeGlass", "Warhead"      # scope lens (non-emissive) / grenade warhead
C.COLORS.update({"ScopeGlass": (.05, .10, .14), "Warhead": (.50, .52, .18)})


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

    def xcyl(self, slot, x0, x1, f, z, r, r2=None, n=16):
        """Cylinder whose axis runs across x (drums, pins, wheels)."""
        r2 = r if r2 is None else r2
        self.P.cyl(V((x0 + x1) / 2, f, z), r, r2, abs(x1 - x0), slot, n, aim((1, 0, 0)))

    def radial(self, slot, f0, f1, cx, z, r, angles, w, th):
        """Small boxes arranged round an axis along f at (cx, z); angle 0 = top, +90 = right. w tangent, th radial."""
        for a in angles:
            ar = math.radians(a)
            self.P.rbox(V(cx + r * math.sin(ar), (f0 + f1) / 2, z + r * math.cos(ar)), (w, f1 - f0, th), slot, 0, 1,
                        Euler((0, ar, 0)))

    def ring(self, slot, f0, f1, cx, z, r, th, n=10):
        self.radial(slot, f0, f1, cx, z, r, [i * 360.0 / n for i in range(n)], 2 * math.pi * r / n * 1.12, th)

    def curve(self, slot, f, z, angs, L, hw, depth, bevel=.004, seg=1):
        """Chain of slabs (a curved magazine). Returns the (f, z) of the bottom end and the last angle."""
        for a in angs:
            self.slab(slot, f, z, L, a, hw, depth, bevel, seg)
            r = math.radians(a)
            f -= math.sin(r) * L
            z -= math.cos(r) * L
        return f, z, angs[-1]

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
    g.box(SG, .119, .121, .100, .122, .0135, bevel=0)                               # lens (non-emissive glass)
    g.box(SI, .1185, .1195, .109, .113, .0013, bevel=0)                             # reticle dot
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
    g.cyl(SG, .299, .3015, sz, .0215, n=20)                                         # front lens
    g.cyl(SG, -.0605, -.059, sz, .0150, n=20)                                       # rear lens
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


# ------------------------------------------------------------------ revolver "Mule .44" (~0.30 m, tier 0)
def revolver():
    g = Gun("Gun_Revolver")
    zb = .031                                                                       # bore / cylinder axis height
    ga = 15                                                                         # grip rake
    # frame: rear block, topstrap, front block (forcing cone)
    g.box(M, -.040, .003, -.004, .046, .0125, bevel=.004, seg=2)                    # rear frame / sideplate
    g.box(M, -.040, .062, .042, .053, .0105, bevel=.003, seg=2)                     # topstrap
    g.box(M, .052, .082, .008, .050, .0125, bevel=.003, seg=2)                      # front frame block
    g.box(M, -.002, .060, -.012, .010, .0115, bevel=.003)                           # lower frame under cylinder
    # fluted 6-round cylinder
    g.cyl(M, .004, .054, zb, .0232, n=24)
    g.radial(PL, .012, .046, 0, zb, .0232, [i * 60 + 30 for i in range(6)], .0105, .0042)   # flutes
    g.cyl(M, .0035, .0075, zb, .0245, n=24)                                         # rear ring
    g.cyl(M, .0505, .0545, zb, .0245, n=24)                                         # front ring
    for i in range(6):                                                              # chamber mouths
        a = math.radians(i * 60)
        g.cyl(PL, .054, .0556, zb + .0148 * math.cos(a), .0050, cx=.0148 * math.sin(a), n=10)
    g.cyl(M, .054, .0575, zb, .0075, n=12)                                          # ejector star hub
    # barrel, rib, full underlug, ejector rod
    g.cyl(M, .082, .242, zb + .003, .0102, n=16)
    g.box(M, .082, .236, zb + .0098, zb + .0150, .0042, bevel=.001)                 # top rib
    g.cyl(M, .238, .245, zb + .003, .0118, .0112, n=16)                             # crown
    g.box(M, .082, .196, zb - .0130, zb - .0020, .0072, bevel=.002, seg=2)          # underlug web
    g.cyl(M, .082, .198, zb - .0128, .0098, n=14)                                   # full underlug
    g.cyl(M, .195, .212, zb - .0128, .0036, n=10)                                   # ejector rod
    g.cyl(M, .208, .218, zb - .0128, .0060, n=12)                                   # rod head
    g.cyl(M, .076, .084, zb + .003, .0120, n=16)                                    # barrel shoulder
    # sights
    g.box(M, -.046, -.030, .053, .061, .0100, bevel=.001)                           # rear blade
    for s in (-1, 1):
        g.box(M, -.046, -.030, .058, .062, .0035, cx=s * .0065, bevel=0)
    g.box(M, .222, .236, zb + .0145, zb + .0200, .0020, bevel=.0008)                # front ramp
    g.box(SI, .226, .230, zb + .0200, zb + .0225, .0012, bevel=0)                   # tritium dot
    # hammer + trigger
    g.prism(M, [(-.040, .040), (-.050, .054), (-.059, .053), (-.058, .047), (-.047, .034), (-.040, .026)], .0048, bevel=.0012)
    for i in range(3):
        g.box(M, -.056 + i * .0035, -.054 + i * .0035, .048, .054, .0052, bevel=0)
    g.box(M, .020, .027, -.032, -.002, .0035, bevel=.001)                           # trigger
    g.box(M, .062, .068, -.036, -.008, .0058, bevel=.0015)                          # trigger guard (front)
    g.box(M, .012, .068, -.040, -.032, .0058, bevel=.0015)                          # trigger guard (bottom)
    g.box(M, -.014, .010, .004, .016, .0128, bevel=.002)                            # cylinder release plate
    g.box(M, -.010, .004, .018, .024, .0135, bevel=.001)                            # latch
    # rubber finger-groove grip
    L = .118
    g.slab(M, -.014, -.002, L - .012, ga, .0120, .030, bevel=.002)                  # grip frame core
    g.slab(GR, -.006, .0, L, ga, .0165, .036, bevel=.004, seg=3)                    # grip body
    ar = math.radians(ga)
    for k in range(3):                                                              # finger swells on the front strap
        t = .030 + k * .026
        fc = -.006 - math.sin(ar) * t + .019 * math.cos(ar)
        zc = -math.cos(ar) * t
        g.P.cyl(V(0, fc, zc), .0066, .0066, .035, GR, 10, aim((1, 0, 0)))
    g.slab(M, -.006 - math.sin(ar) * (L - .004) + .001, -math.cos(ar) * (L - .004), .010, ga, .0172, .0385, bevel=.002)  # butt cap
    return g.finish((0, .245, zb + .003))


# ------------------------------------------------------------------ carbine "Rook" (~0.78 m, tier 1)
def carbine():
    g = Gun("Gun_Carbine")
    zb = .050
    g.box(M, -.075, .220, .024, .080, .0215, bevel=.004, seg=2)                     # upper receiver
    g.box(PL, -.075, .118, -.018, .024, .0205, bevel=.004, seg=2)                   # lower receiver
    g.box(M, .020, .068, .052, .072, .0225, bevel=.002)                             # ejection port
    g.box(M, -.060, -.040, .080, .088, .0080, bevel=.002)                           # charging handle
    g.box(M, .118, .150, .020, .030, .0215, bevel=.002)                             # magwell flare
    g.box(PL, .220, .400, .016, .082, .0225, bevel=.005, seg=2)                     # short handguard
    for i in range(5):                                                              # M-lok style slots
        for s in (-1, 1):
            g.box(M, .242 + i * .030, .260 + i * .030, .028, .036, .0004, bevel=0, cx=s * .0227)
    g.rail(-.070, .220, .080, hw=.0105)
    g.rail(.220, .400, .082, hw=.0105)
    for i in range(3):                                                              # side rail stubs
        for s in (-1, 1):
            g.box(M, .300 + i * .028, .300 + i * .028 + .016, .046, .054, .0020, bevel=0, cx=s * .0245)
    g.cyl(M, .400, .500, zb + .005, .0100, n=16)                                    # barrel
    g.box(M, .400, .428, zb - .012, zb + .024, .0135, bevel=.003)                   # gas block
    g.cyl(M, .500, .560, zb + .005, .0140, n=16)                                    # flash hider
    for i in range(3):
        g.ribs(M, .506 + i * .008, .508 + i * .008, zb + .005, .0152, n=14)
    g.radial(PL, .528, .556, 0, zb + .005, .0140, [i * 60 + 30 for i in range(6)], .0045, .0030)  # hider slots
    g.slab(PL, -.004, -.012, .118, 22, .0185, .040, bevel=.004, seg=2)              # pistol grip
    for s in (-1, 1):
        g.slab(GR, -.004, -.020, .100, 22, .0014, .034, bevel=.0008, cx=s * .0195)
    g.box(PL, .040, .110, -.040, -.010, .0115, bevel=.003)                          # trigger guard
    g.box(M, .018, .024, -.040, -.012, .0035, bevel=.001)
    f, z, a = g.curve(PL, .102, -.016, [-3, -10, -18, -27, -36], .036, .0195, .040, bevel=.004)   # curved 30-rd mag
    g.slab(M, f, z, .008, a, .0200, .042, bevel=.002)
    # angled foregrip
    g.box(M, .340, .385, .004, .018, .0105, bevel=.002)                             # mount
    g.slab(GR, .362, .010, .082, -24, .0128, .030, bevel=.004, seg=2)
    g.slab(PL, .362, .010, .016, -24, .0138, .034, bevel=.002)
    # collapsible stock, half extended
    g.cyl(M, -.150, -.075, .040, .0180, n=14)
    g.prism(PL, [(-.125, .062), (-.195, .062), (-.212, .056), (-.216, -.066), (-.198, -.072), (-.160, -.030), (-.125, .006)],
            .0200, bevel=.005, seg=2)
    g.prism(GR, [(-.212, .054), (-.2185, .052), (-.2225, -.066), (-.216, -.0675)], .0202, bevel=.0015)
    g.box(M, -.150, -.126, .028, .036, .0130, bevel=.002)                           # adjuster collar
    g.box(M, -.138, -.130, .018, .030, .0055, bevel=.001)                           # lever
    # small reflex sight
    g.box(M, .045, .098, .082, .090, .0150, bevel=.002)
    for s in (-1, 1):
        g.box(M, .050, .098, .090, .112, .0017, cx=s * .0138, bevel=.0006)
    g.box(M, .088, .100, .108, .118, .0160, bevel=.002)                             # hood
    g.box(M, .045, .054, .088, .104, .0150, bevel=.002)                             # battery cover
    g.box(SG, .0905, .0915, .090, .109, .0125, bevel=0)                             # lens
    g.box(SI, .0895, .0905, .098, .101, .0012, bevel=0)                             # reticle dot
    # front sight post
    g.box(M, .410, .420, .060, .098, .0030, bevel=.001)
    g.box(SI, .4125, .4175, .094, .099, .0012, bevel=0)
    return g.finish((0, .560, zb + .005))


# ------------------------------------------------------------------ auto shotgun "Bulldog" (~0.95 m, tier 2)
def autoshotgun():
    g = Gun("Gun_AutoShotgun")
    zb = .046
    g.box(M, -.090, .195, .004, .072, .0265, bevel=.005, seg=2)                     # chunky receiver
    g.box(PL, -.090, .150, -.026, .004, .0235, bevel=.004, seg=2)                   # lower housing
    g.box(M, .028, .088, .046, .066, .0272, bevel=.002)                             # ejection port
    g.box(M, .090, .118, .058, .072, .0070, cx=.030, bevel=.002)                    # charging handle
    g.cyl(M, .090, .118, .066, .0075, cx=.0385, n=10)
    g.rail(-.085, .190, .072, hw=.0115, h=.004, pitch=.0095)
    g.cyl(M, .195, .562, zb + .004, .0125, n=16)                                    # barrel
    g.cyl(M, .195, .430, zb - .018, .0085, n=12)                                    # gas tube
    g.cyl(M, .225, .480, zb + .004, .0205, n=16)                                    # vented heat shield
    g.cyl(M, .225, .236, zb + .004, .0225, n=16)
    g.cyl(M, .469, .480, zb + .004, .0225, n=16)
    f = .246
    while f < .462:
        g.radial(PL, f, f + .012, 0, zb + .004, .0204, [-70, -35, 0, 35, 70], .0055, .0030)
        f += .020
    g.box(PL, .236, .430, .000, .030, .0215, bevel=.005, seg=2)                     # forend
    for i in range(7):
        g.box(GR, .246 + i * .026, .256 + i * .026, -.002, .006, .0195, bevel=.001)
    g.cyl(M, .562, .622, zb + .004, .0165, n=16)                                    # muzzle brake / choke
    for i in range(3):
        g.ribs(M, .566 + i * .010, .568 + i * .010, zb + .004, .0180, n=14)
    g.radial(PL, .590, .616, 0, zb + .004, .0165, [-90, -45, 45, 90, 135, -135], .0050, .0030)
    # magazine: 8-round drum
    g.box(PL, .150, .218, -.012, .006, .0225, bevel=.003)                           # feed neck
    dz, df, dr = -.062, .188, .056
    g.xcyl(PL, -.0215, .0215, df, dz, dr, n=28)
    for s in (-1, 1):
        g.xcyl(M, s * .0210, s * .0240, df, dz, dr + .001, dr + .001, n=28)         # rim bands
        g.xcyl(GR, s * .0215, s * .0232, df, dz, .040, n=24)                        # face plate
        g.xcyl(M, s * .0225, s * .0250, df, dz, .013, n=14)                         # hub
    for i in range(8):                                                              # rim notches
        a = math.radians(i * 45 + 22.5)
        g.P.rbox(V(0, df + (dr + .001) * math.sin(a), dz + (dr + .001) * math.cos(a)), (.047, .008, .004), PL, 0, 1,
                 Euler((-a, 0, 0)))
    g.box(M, .150, .226, .004, .012, .0215, bevel=.002)
    # pistol grip + trigger guard
    g.slab(PL, .0, .0, .120, 18, .0185, .042, bevel=.004, seg=2)
    for s in (-1, 1):
        g.slab(GR, .0, -.008, .104, 18, .0014, .036, bevel=.0008, cx=s * .0196)
    g.box(PL, .040, .110, -.040, -.002, .0115, bevel=.003)
    g.box(M, .012, .018, -.034, -.010, .0035, bevel=.001)
    # fixed stock
    g.prism(PL, [(-.090, .064), (-.20, .058), (-.300, .050), (-.322, .046), (-.325, -.072), (-.300, -.076), (-.200, -.024),
                 (-.120, -.010), (-.090, -.006)], .0235, bevel=.005, seg=2)
    g.prism(GR, [(-.322, .044), (-.3295, .042), (-.3325, -.072), (-.325, -.0735)], .0238, bevel=.0015)
    # ghost-ring sights: rear aperture on the receiver, front hooded post on the barrel
    rz = .072 + .004
    g.box(M, -.070, -.030, .072, .080, .0120, bevel=.002)
    g.ring(M, -.056, -.046, 0, rz + .016, .0095, .0030, n=10)
    for s in (-1, 1):
        g.box(M, -.058, -.044, .074, rz + .022, .0022, cx=s * .0135, bevel=0)
    g.box(M, -.058, -.044, rz + .020, rz + .024, .0140, bevel=0)
    g.box(M, .520, .546, zb + .016, zb + .026, .0045, bevel=.001)
    g.ring(M, .524, .536, 0, zb + .046, .0100, .0025, n=10)
    g.box(M, .528, .532, zb + .026, zb + .046, .0013, bevel=0)
    g.box(SI, .5275, .5325, zb + .0395, zb + .0445, .0012, bevel=0)
    return g.finish((0, .622, zb + .004))


# ------------------------------------------------------------------ anti-materiel rifle "Warden .50" (~1.40 m, tier 4)
def sniper50():
    g = Gun("Gun_Sniper50")
    zb = .058
    g.box(M, -.110, .160, .016, .094, .0265, bevel=.005, seg=2)                     # heavy receiver
    g.box(PL, -.110, .120, -.022, .016, .0225, bevel=.004, seg=2)                   # lower / chassis
    g.box(M, .030, .130, .066, .086, .0270, bevel=.002)                             # ejection port
    g.cyl(M, -.150, -.100, .068, .0185, n=16)                                       # bolt body
    g.xcyl(M, .016, .076, -.056, .076, .0045, n=10)                                 # bolt handle stem
    g.P.ico(V(.086, -.056, .076), (.0125, .0125, .0125), GR, 1)                     # bolt knob
    g.rail(-.100, .160, .094, hw=.0120, h=.005, pitch=.008)
    # fluted heavy barrel inside a free-float handguard
    g.cyl(M, .160, .800, zb + .002, .0155, n=18)                                    # barrel
    g.radial(PL, .470, .775, 0, zb + .002, .0155, [i * 45 + 22 for i in range(8)], .0070, .0030)  # flutes
    g.cyl(PL, .160, .440, zb + .002, .0270, n=8)                                    # handguard
    g.cyl(M, .158, .168, zb + .002, .0285, n=8)
    g.cyl(M, .434, .446, zb + .002, .0285, n=8)
    f = .190
    while f < .420:                                                                 # side vent slots
        g.radial(M, f, f + .014, 0, zb + .002, .0270, [-90, 90], .0070, .0030)
        f += .028
    g.rail(.200, .420, zb + .002 + .0270, hw=.0110, h=.004, pitch=.009)
    # muzzle brake
    g.cyl(M, .800, .925, zb + .002, .0245, n=18)
    for i in range(5):
        g.cyl(M, .806 + i * .022, .812 + i * .022, zb + .002, .0268, n=18)
    for i in range(4):
        g.radial(PL, .818 + i * .022, .832 + i * .022, 0, zb + .002, .0245, [-90, 90, -45, 45, 0], .0070, .0034)
    g.cyl(M, .925, .932, zb + .002, .0210, .0195, n=18)
    # folded bipod under the handguard
    for s in (-1, 1):
        g.box(M, .470, .640, .018, .028, .0050, cx=s * .019, bevel=.0012)
        g.box(GR, .590, .640, .014, .030, .0055, cx=s * .019, bevel=.0012)
    g.box(M, .445, .490, .016, .040, .0245, bevel=.003)
    # pistol grip + trigger
    g.slab(PL, -.004, -.014, .124, 18, .0195, .042, bevel=.004, seg=2)
    for s in (-1, 1):
        g.slab(GR, -.004, -.022, .106, 18, .0014, .036, bevel=.0008, cx=s * .0205)
    g.box(PL, .040, .115, -.040, -.012, .0115, bevel=.003)
    g.box(M, .018, .024, -.040, -.012, .0035, bevel=.001)
    # 5-round box magazine
    g.slab(PL, .082, -.020, .104, -4, .0205, .074, bevel=.005, seg=2)
    g.slab(M, .082 + .0072, -.020 - .1037 + .001, .010, -4, .0210, .076, bevel=.002)
    # chassis stock with adjustable cheek riser
    g.prism(PL, [(-.110, .074), (-.230, .076), (-.430, .074), (-.468, .066), (-.472, -.108), (-.440, -.114), (-.300, -.052),
                 (-.200, -.024), (-.110, -.014)], .0215, bevel=.005, seg=2)
    g.prism(GR, [(-.468, .064), (-.4765, .062), (-.4805, -.108), (-.472, -.1095)], .0218, bevel=.0015)
    g.box(PL, -.400, -.250, .074, .098, .0225, bevel=.005, seg=2)                   # cheek riser
    for s in (-1, 1):
        g.box(M, -.380, -.375, .074, .086, .0030, cx=s * .012, bevel=0)
        g.box(M, -.290, -.285, .074, .086, .0030, cx=s * .012, bevel=0)
    g.box(M, -.300, -.255, -.062, -.050, .0160, bevel=.002)                         # rear monopod mount
    # big scope
    sz = .140
    g.cyl(M, -.020, .300, sz, .0225, n=24)                                          # main tube
    g.cyl(M, .300, .395, sz, .0225, .0345, n=24)                                    # objective bell
    g.cyl(M, .395, .455, sz, .0355, .0355, n=24)                                    # sunshade
    g.cyl(M, -.100, -.020, sz, .0300, .0225, n=24)                                  # eyepiece
    g.cyl(SG, .454, .4565, sz, .0335, n=24)                                         # objective lens
    g.cyl(SG, -.1010, -.0995, sz, .0235, n=24)                                      # ocular lens
    for fr in (.040, .220):
        g.box(M, fr, fr + .028, .096, sz - .010, .0150, bevel=.003)                 # rings
        g.cylz(M, fr + .014, sz + .010, sz + .030, .0120, n=14)
    g.cylz(M, .130, sz + .016, sz + .038, .0100, n=14)                              # elevation turret
    g.cylz(M, .130, sz + .038, sz + .042, .0120, n=14)
    g.xcyl(M, .0215, .0425, .130, sz, .0090, n=14)                                  # windage turret
    g.box(M, .005, .075, .088, .096, .0130, bevel=.002)                             # scope base
    return g.finish((0, .932, zb + .002))


# ------------------------------------------------------------------ 40 mm grenade launcher "Hammerhead" (~0.75 m, tier 4)
def grenade_launcher():
    g = Gun("Gun_GrenadeLauncher")
    zb = .052
    g.box(M, -.070, .040, .016, .086, .0270, bevel=.005, seg=2)                     # receiver housing
    g.box(PL, -.070, .060, -.024, .016, .0225, bevel=.004, seg=2)
    g.rail(-.065, .036, .086, hw=.0115, h=.004, pitch=.0095)
    # revolving 6-shot cylinder
    g.cyl(M, .040, .150, zb, .0540, n=28)
    g.cyl(M, .036, .044, zb, .0560, n=28)
    g.cyl(M, .146, .156, zb, .0560, n=28)
    g.radial(PL, .056, .136, 0, zb, .0540, [i * 60 for i in range(6)], .0200, .0045)    # chamber bulges
    for i in range(6):                                                                  # chamber mouths
        a = math.radians(i * 60 + 30)
        g.cyl(PL, .155, .1575, zb + .0300 * math.cos(a), .0140, cx=.0300 * math.sin(a), n=14)
    g.cyl(M, .155, .160, zb, .0100, n=12)                                               # centre pin
    for i in range(12):                                                                 # rim index notches
        a = math.radians(i * 30)
        g.P.rbox(V(.0540 * math.sin(a), .041, zb + .0540 * math.cos(a)), (.008, .004, .004), PL, 0, 1, Euler((0, a, 0)))
    # barrel + shroud
    g.cyl(M, .156, .430, zb, .0240, n=20)
    g.cyl(M, .156, .300, zb, .0275, n=20)
    g.ribs(M, .170, .300, zb, .0290, step=.020, n=20)
    g.cyl(M, .430, .450, zb, .0285, .0270, n=20)                                        # muzzle collar
    g.radial(PL, .400, .424, 0, zb, .0240, [-90, 90, 0], .0070, .0030)
    # pistol grip, trigger guard
    g.slab(PL, -.004, -.014, .118, 20, .0190, .042, bevel=.004, seg=2)
    for s in (-1, 1):
        g.slab(GR, -.004, -.022, .100, 20, .0014, .036, bevel=.0008, cx=s * .0200)
    g.box(PL, .036, .112, -.044, -.010, .0115, bevel=.003)
    g.box(M, .018, .024, -.040, -.012, .0035, bevel=.001)
    # front vertical grip on a rail block
    g.box(M, .200, .250, zb - .036, zb - .020, .0120, bevel=.002)
    g.box(M, .212, .238, -.004, .016, .0145, bevel=.002)
    g.cylz(GR, .225, -.088, -.004, .0175, n=14)
    for i in range(4):
        g.cylz(M, .225, -.074 + i * .018, -.072 + i * .018, .0182, n=14)
    g.cylz(M, .225, -.092, -.088, .0185, n=14)
    # folding stock (extended): two arms + butt plate
    for s in (-1, 1):
        g.box(M, -.310, -.070, .030, .040, .0050, cx=s * .020, bevel=.0015)
    g.box(M, -.130, -.070, .016, .046, .0230, bevel=.003)                               # hinge block
    g.box(M, -.310, -.298, -.008, .064, .027, bevel=.0035, seg=2)
    g.box(GR, -.322, -.310, -.004, .060, .0245, bevel=.003)                             # butt pad
    # flip-up ladder sight (up) on the receiver
    g.box(M, .008, .030, .086, .092, .0120, bevel=.001)
    for s in (-1, 1):
        g.box(M, .010, .018, .090, .136, .0020, cx=s * .0100, bevel=0)
    g.box(M, .010, .018, .126, .134, .0120, bevel=0)
    g.box(M, .010, .018, .104, .108, .0100, bevel=0)
    g.box(SI, .0095, .0105, .114, .118, .0012, bevel=0)
    # front blade
    g.box(M, .412, .422, zb + .0230, zb + .0430, .0030, bevel=.001)
    g.box(SI, .4145, .4195, zb + .0390, zb + .0430, .0012, bevel=0)
    return g.finish((0, .450, zb))


def grenade40():
    """40 mm grenade round (~0.10 m), origin at the centre, nose along +f (Unity +Z). Slots GunMetal + Warhead."""
    P = Part("Proj_Grenade40")

    def cy(f0, f1, r, r2=None, slot=M, n=24):
        P.cyl(V(0, (f0 + f1) / 2, 0), r, r if r2 is None else r2, f1 - f0, slot, n, aim((0, -1, 0)))
    cy(-.050, .000, .0195)                                       # brass-look case
    cy(-.050, -.0455, .0212)                                     # rim
    cy(-.0510, -.0500, .0075, n=14)                              # primer
    cy(-.002, .006, .0212)                                       # driving band / crimp
    cy(.006, .024, .0200, .0195, slot=WH)                        # warhead body
    cy(.024, .038, .0195, .0140, slot=WH)                        # ogive
    cy(.038, .047, .0140, .0070, slot=WH)
    cy(.047, .050, .0070, .0035, slot=WH, n=14)
    cy(.014, .018, .0204, slot=M)                                # band ring
    ob = P.build(smooth=40, flat=())
    ob.name = ob.data.name = "Proj_Grenade40"
    return ob


GUNS = [pistol, smg, shotgun, rifle, dmr, lmg, revolver, carbine, autoshotgun, sniper50, grenade_launcher]
NEW_HOLD = {"Gun_Revolver": "revolver", "Gun_Carbine": "carbine", "Gun_AutoShotgun": "autoshotgun",
            "Gun_Sniper50": "sniper50", "Gun_GrenadeLauncher": "grenadelauncher"}

# Left-palm position under the handguard: gun-space forward position of the palm (f). The height is computed from the mesh
# (lowest vertex near that f, minus FIT_PALM below it). Existing guns use the values in SceneBuilder.Combat.cs LongGunFit.
FIT_PALM_F = {"Gun_SMG": .200, "Gun_Shotgun": .310, "Gun_Rifle": .270, "Gun_DMR": .240, "Gun_LMG": .250,
              "Gun_Carbine": .290, "Gun_AutoShotgun": .320, "Gun_Sniper50": .330, "Gun_GrenadeLauncher": .215}
FIT_PALM = .012
FIT_OVERRIDE = {"Gun_GrenadeLauncher": [0.0, -.046, .225]}   # palm wraps the front vertical grip centre (no handguard to sit under)
CS_FIT = {   # (foregrip y, f), (stock y, f) as currently in SceneBuilder.Combat.cs (x is 0)
    "Gun_SMG": ((.002, .200), (.028, -.205)), "Gun_Shotgun": ((-.022, .310), (0.0, -.328)),
    "Gun_Rifle": ((.002, .270), (-.01, -.243)), "Gun_DMR": ((.006, .240), (-.02, -.305)),
    "Gun_LMG": ((.006, .250), (-.02, -.297))}


def export_gun(ob, muz, d):
    C.ensure_dir(d)
    for o in bpy.data.objects:                     # the exported empty must be named exactly 'Muzzle'
        if o.type == 'EMPTY' and o.name == 'Muzzle':
            o.name = 'Muzzle_' + o.parent.name
    muz.name = 'Muzzle'
    select_only(ob, muz)
    _fbx(os.path.join(d, ob.name + ".fbx"), {'MESH', 'EMPTY'})
    muz.name = 'Muzzle_' + ob.name


def _fbx(path, types):
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types=types)


def fit_for(ob, muz):
    """Foregrip / stock in LongGunFit's convention (gun space: x right, y up, z forward; origin = pivot)."""
    vs = [v.co for v in ob.data.vertices]          # Blender coords: f = -y
    name = ob.name
    out = {}
    if name in FIT_PALM_F:
        pf = FIT_PALM_F[name]
        hits = [C.raycast(ob, V(x, pf, -.4), (0, 0, 1))[0] for x in (-.008, 0.0, .008)]
        under = min(h.z for h in hits if h is not None)           # underside of the handguard / pump at the palm position
        out["foregrip"] = [0.0, round(under - FIT_PALM, 3), pf]
        rear = -max(v.y for v in vs)              # most rearward f
        near = [v for v in vs if -v.y < rear + .012]
        zs = [v.z for v in near]
        out["stock"] = [0.0, round((min(zs) + max(zs)) / 2, 3), round(rear + .003, 3)]
        out["under_z"] = round(under, 4)
        if name in FIT_OVERRIDE:
            out["foregrip"] = FIT_OVERRIDE[name]
    return out


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = os.path.join(C.MODELS, "Weapons")
    if "--out" in argv:
        out = os.path.abspath(argv[argv.index("--out") + 1])
        P = os.path.join(out, "previews")
    else:
        P = C.PREVIEWS
    HOLD_DIR[0] = P
    C.reset_scene()
    guns = []
    fit = {}
    for fn in GUNS:
        ob, muz = fn()
        mn, mx = C.dims(ob)
        length = mx.y * -1 - (-mn.y) * -1 if False else (-mn.y) - (-mx.y)
        slots = [m.name for m in ob.data.materials]
        tris = C.tri_count(ob)
        print("GUN %-20s tris=%5d length=%.3f muzzle(f)=%.3f slots=%s" % (ob.name, tris, length, -muz.location.y, slots))
        guns.append((ob, muz))
        ob.hide_render = muz.hide_render = True
        e = {"overall_length_m": round(length, 3), "muzzle": [0.0, round(muz.location.z, 4), round(-muz.location.y, 4)],
             "tris": tris, "slots": slots}
        e.update(fit_for(ob, muz))
        if ob.name in CS_FIT:
            (fy, ff), (sy, sf) = CS_FIT[ob.name]
            e["foregrip_current_cs"] = [0.0, fy, ff]
            e["stock_current_cs"] = [0.0, sy, sf]
            print("   fit computed fg=%s stock=%s | Combat.cs fg=(%.3f,%.3f) stock=(%.3f,%.3f)" % (
                e["foregrip"], e["stock"], fy, ff, sy, sf))
        elif "foregrip" in e:
            print("   fit fg=%s stock=%s" % (e["foregrip"], e["stock"]))
        fit[ob.name] = e
    proj = grenade40()
    proj.hide_render = True
    mn, mx = C.dims(proj)
    ptris = C.tri_count(proj)
    pslots = [m.name for m in proj.data.materials]
    print("PROJ %-20s tris=%5d length=%.3f slots=%s" % (proj.name, ptris, (-mn.y) - (-mx.y), pslots))
    fit["Proj_Grenade40"] = {"overall_length_m": round((-mn.y) - (-mx.y), 3), "tris": ptris, "slots": pslots}
    for ob, muz in guns:
        export_gun(ob, muz, out)
    select_only(proj)
    _fbx(os.path.join(out, "Proj_Grenade40.fbx"), {'MESH'})
    import json
    with open(os.path.join(out, "gun_fit.json"), "w") as fh:
        json.dump({"convention": "LongGunFit: gun space x right, y up, z forward, origin = pivot (top of pistol grip). "
                                 "foregrip = left palm centre under the handguard (y = handguard underside - 0.012); "
                                 "stock = butt pad rear face point.", "guns": fit}, fh, indent=2)
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
    proj.hide_render = False
    render_view([proj], os.path.join(P, "Proj_Grenade40.png"), az=60, el=14, size=(700, 500), scale=.16,
                target=(0, 0, 0), floor=False)
    proj.hide_render = True
    # all guns stacked for a size comparison
    for i, (ob, muz) in enumerate(guns):
        ob.location = (0, 0, -i * .30); ob.hide_render = False
    n = len(guns)
    render_view([o for o, _ in guns], os.path.join(P, "Guns_all_side.png"), az=80, el=8, size=(1800, 1800), scale=3.7,
                target=(0, -.22, -.15 * (n - 1) - .02), floor=False)
    for ob, _ in guns:
        ob.location = (0, 0, 0)
    # ---- guns in the character's hands
    body, rig = build_body()
    keep = [x for x, _ in guns]
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o not in keep and o is not body:
            bpy.data.objects.remove(o, do_unlink=True)
    hold_preview(body, rig, guns)


def hold_preview(body, rig, guns):
    from mathutils import Matrix
    P = HOLD_DIR[0]
    arm_rest = arm_matrix(1)
    hand_local = Vector((-.012, .02, -.625))           # palm centre in arm-part space (x inward, f, z)
    todo = [("Gun_Rifle", "rifle"), ("Gun_Pistol", "pistol"), ("Gun_SMG", "smg")] + list(NEW_HOLD.items())
    for gname, tag in todo:
        gun = [o for o, _ in guns if o.name == gname][0]
        b2, r2 = instance(body, rig, (0, 0, 0))
        spec = {"ArmR": (-80, 0, 0), "ArmL": (-80, 0, 32)}
        pose(r2, spec)
        g2 = clone_into_scene(gun)
        g2.parent = None
        hp = arm_rest @ V(*hand_local)
        rest = Matrix.Translation(hp) @ Matrix.Rotation(math.radians(80), 4, 'X')
        place(g2, r2, "ArmR", rest)
        gun.hide_render = False
        mn, mx = C.dims(gun)
        cf = -(mn.y + mx.y) / 2
        sc = max(1.1, (mx.y - mn.y) * 1.55)
        for az, nm in ((90, "side"), (40, "3q"), (0, "front")):
            render_view([b2, g2], os.path.join(P, "Hold_%s_%s.png" % (tag, nm)), az=az, el=8, size=(900, 900), scale=sc,
                        target=(0, -(cf + .35) if az != 0 else -.2, 1.35), floor=False)
        bpy.data.objects.remove(g2, do_unlink=True)
        bpy.data.objects.remove(b2, do_unlink=True)
        bpy.data.objects.remove(r2, do_unlink=True)


HOLD_DIR = [C.PREVIEWS]

if __name__ == "__main__":
    main()
