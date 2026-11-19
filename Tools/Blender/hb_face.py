"""Head refinement + face sculpt (height-field displacements), eyes, ears."""
import math
import bpy, bmesh, numpy as np
from mathutils import Vector

# landmarks (world, Blender coords: forward = -Y)
EYE_Z = 1.700
EYE_X = 0.0325
BROW_Z = 1.727
NOSE_TIP_Z = 1.668
NOSE_BASE_Z = 1.6605
MOUTH_Z = 1.6405
CHIN_Z = 1.594


def _subdivide(bm, faces, cuts=1):
    edges = list({e for f in faces for e in f.edges})
    return bmesh.ops.subdivide_edges(bm, edges=edges, cuts=cuts, use_grid_fill=True, quad_corner_type='STRAIGHT_CUT')


def refine_region(bm, body, pred):
    faces = [f for f in bm.faces if pred(f.calc_center_median())]
    res = _subdivide(bm, faces)
    new = [v for v in bm.verts if v.is_valid]
    return res


def project_verts(bm, body, verts, iters=3):
    P = np.array([v.co[:] for v in verts])
    P2 = body.project(P, iters=iters, maxstep=.01)
    for v, p in zip(verts, P2):
        v.co = Vector(p)


def refine_head(bm, body):
    before = set(bm.verts)
    refine_region(bm, body, lambda c: c.z > 1.575 and abs(c.x) < .11)
    nv = [v for v in bm.verts if v not in before]
    project_verts(bm, body, nv)
    before = set(bm.verts)
    # face region: front of the head, brow to chin
    refine_region(bm, body, lambda c: c.y < -.015 and 1.585 < c.z < 1.775 and abs(c.x) < .066)
    nv = [v for v in bm.verts if v not in before]
    project_verts(bm, body, nv)


# ------------------------------------------------------------------ sculpt
def G(x, z, cx, cz, sx, sz):
    return np.exp(-0.5 * ((x - cx) / sx) ** 2 - 0.5 * ((z - cz) / sz) ** 2)


def _interp(z, zs, vs):
    return np.interp(z, zs, vs)


def face_height(X, Z):
    """Forward (-Y) displacement of the face surface in meters. X: signed x, Z: height."""
    x = np.abs(X)
    h = np.zeros_like(x)
    # brow ridge + glabella, eye sockets, under-eye
    h += 0.0070 * G(x, Z, 0.031, BROW_Z + 0.001, 0.027, 0.0065)
    h += 0.0045 * G(x, Z, 0.0, BROW_Z - 0.001, 0.013, 0.008)
    h += -0.0085 * G(x, Z, EYE_X, EYE_Z + 0.001, 0.0185, 0.0125)
    h += -0.0030 * G(x, Z, EYE_X + 0.002, 1.6815, 0.020, 0.0055)
    h += -0.0050 * G(x, Z, 0.012, 1.708, 0.008, 0.012)          # nasion dip
    # nose: bridge profile (height above the face plane) and width vs z
    zz = np.array([1.649, 1.657, 1.663, 1.668, 1.674, 1.685, 1.696, 1.706, 1.714, 1.722])
    aa = np.array([0.000, 0.0080, 0.0150, 0.0185, 0.0170, 0.0135, 0.0100, 0.0062, 0.0030, 0.0000])
    ss = np.array([0.013, 0.0125, 0.0120, 0.0112, 0.0102, 0.0088, 0.0076, 0.0066, 0.0062, 0.0060])
    a = _interp(Z, zz, aa); s = _interp(Z, zz, ss)
    h += a * np.exp(-0.5 * (x / s) ** 2) * np.where(x < 0.03, 1.0, 0.0)
    h += 0.0022 * G(x, Z, 0.0, 1.6685, 0.0075, 0.0050)             # tip
    h += 0.0085 * G(x, Z, 0.0172, 1.6635, 0.0068, 0.0062)           # alae
    h += -0.0060 * G(x, Z, 0.0090, 1.6570, 0.0040, 0.0030)          # nostrils
    h += -0.0030 * G(x, Z, 0.0265, 1.653, 0.0045, 0.0140)           # nasolabial
    # cheeks
    h += 0.0060 * G(x, Z, 0.047, 1.688, 0.017, 0.012)
    h += -0.0035 * G(x, Z, 0.041, 1.652, 0.014, 0.012)
    h += 0.0030 * G(x, Z, 0.040, 1.630, 0.019, 0.013)
    # mouth
    h += 0.0105 * G(x, Z, 0.0, 1.6480, 0.0190, 0.0052)              # upper lip
    h += 0.0105 * G(x, Z, 0.0, 1.6325, 0.0205, 0.0062)              # lower lip
    h += -0.0090 * G(x, Z, 0.0, MOUTH_Z, 0.0180, 0.0014)            # mouth line
    h += -0.0030 * G(x, Z, 0.0222, MOUTH_Z - 0.001, 0.0042, 0.0042)  # corners
    h += -0.0015 * G(x, Z, 0.0, 1.6535, 0.0030, 0.0050)             # philtrum groove
    h += 0.0012 * G(x, Z, 0.0058, 1.6535, 0.0020, 0.0050)           # philtrum ridges
    h += -0.0045 * G(x, Z, 0.0, 1.6175, 0.0150, 0.0034)             # mentolabial
    h += 0.0095 * G(x, Z, 0.0, CHIN_Z + 0.012, 0.0155, 0.0105)      # chin
    h += 0.0035 * G(x, Z, 0.034, 1.598, 0.012, 0.010)               # jaw / mental
    h += 0.0030 * G(x, Z, 0.0, 1.746, 0.030, 0.014)                 # forehead
    return h


def sculpt_face(bm):
    vs = [v for v in bm.verts if v.co.z > 1.57 and v.co.y < 0.0 and abs(v.co.x) < 0.085]
    P = np.array([v.co[:] for v in vs])
    X = P[:, 0].copy()
    # a touch of asymmetry: nose and mouth drift, slightly different brow heights
    shift = 0.0009 * np.exp(-0.5 * ((P[:, 2] - 1.665) / 0.025) ** 2)
    Zc = P[:, 2] + np.where(X > 0, 0.0007, -0.0004)
    h = face_height(X - shift, Zc)
    bm.normal_update()
    nf = np.clip((-np.array([v.normal.y for v in vs]) - 0.30) / 0.45, 0, 1)
    front = np.clip((-P[:, 1] - 0.020) / 0.040, 0, 1) * np.clip((P[:, 2] - 1.576) / 0.020, 0, 1) * nf
    front = front * front * (3 - 2 * front)
    P[:, 1] -= h * front
    for v, p in zip(vs, P):
        v.co = Vector(p)


# ------------------------------------------------------------------ eyes
EYE_R = 0.0131
EYE_CY = -0.0660           # eyeball centre y (world)
OPEN_A = 0.0131            # half width of the palpebral fissure
OPEN_BU = 0.0060           # upper half height (lid covers a sliver of the iris)
OPEN_BL = 0.0070
IRIS_R = 0.0062
PUPIL_R = 0.0022


def _smooth(t):
    t = np.clip(t, 0, 1)
    return t * t * (3 - 2 * t)


def _rho(x, z, sx):
    dx = (x - sx * EYE_X) * sx            # + = outward
    dz = z - (EYE_Z + 0.0002) - 0.085 * dx
    # upper arch peaks toward the inner third: shift the ellipse centre outward for the lower lid
    b = np.where(dz > 0, OPEN_BU, OPEN_BL)
    return np.sqrt((dx / OPEN_A) ** 2 + (dz / b) ** 2)


def refine_features(bm, body):
    def around_eye(c):
        return c.y < -0.03 and min(abs(c.x - EYE_X), abs(c.x + EYE_X)) < 0.026 and abs(c.z - EYE_Z) < 0.017
    before = set(bm.verts)
    refine_region(bm, body, around_eye)
    project_verts(bm, body, [v for v in bm.verts if v not in before])
    # nose + mouth: one extra level so lips, philtrum and nostrils resolve
    def nose_mouth(c):
        return c.y < -0.05 and abs(c.x) < 0.034 and 1.598 < c.z < 1.670
    before = set(bm.verts)
    refine_region(bm, body, nose_mouth)
    project_verts(bm, body, [v for v in bm.verts if v not in before])
    # ears: one extra level for the relief
    def around_ear(c):
        return abs(c.x) > 0.060 and abs(c.x) < 0.095 and abs(c.y - 0.016) < 0.026 and abs(c.z - 1.688) < 0.040
    before = set(bm.verts)
    refine_region(bm, body, around_ear)
    project_verts(bm, body, [v for v in bm.verts if v not in before])


def wrap_eyes(bm):
    """Shape the lids around the eyeballs and cut the fissures. Returns the eye data."""
    for sx in (1, -1):
        C = Vector((sx * EYE_X, EYE_CY, EYE_Z))
        for v in bm.verts:
            if v.co.y > -0.03 or v.co.z < 1.67 or v.co.z > 1.73 or abs(abs(v.co.x) - EYE_X) > 0.03:
                continue
            if (v.co.x * sx) < 0:
                continue
            rho = _rho(v.co.x, v.co.z, sx)
            if rho >= 2.4:
                continue
            d = v.co - C
            L = d.length
            dirn = d / L
            lift = 0.0009 + 0.0030 * float(_smooth((rho - 1.0) / 1.4))
            tgt = C + dirn * (EYE_R + lift)
            w = 1.0 - float(_smooth((rho - 1.15) / 1.25))
            v.co = v.co.lerp(tgt, w)
        # fissure: delete the faces inside the opening
        kill = [f for f in bm.faces if f.calc_center_median().y < -0.03 and
                f.calc_center_median().x * sx > 0 and float(_rho(f.calc_center_median().x, f.calc_center_median().z, sx)) < 0.98]
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    # lid margin: thicken the boundary loops
    bm.edges.ensure_lookup_table()
    for sx in (1, -1):
        C = Vector((sx * EYE_X, EYE_CY, EYE_Z))
        be = [e for e in bm.edges if e.is_boundary and e.verts[0].co.y < -0.03 and e.verts[0].co.z > 1.67 and
              e.verts[0].co.z < 1.73 and e.verts[0].co.x * sx > 0 and abs(e.verts[0].co.x * sx - EYE_X) < 0.02]
        res = bmesh.ops.extrude_edge_only(bm, edges=be)
        new_v = [g for g in res['geom'] if isinstance(g, bmesh.types.BMVert)]
        outer = set()
        for e in be:
            outer.update(e.verts)
        for v in outer:       # old boundary: lid margin, pushed out for thickness
            d = (v.co - C).normalized()
            v.co = C + d * (EYE_R + 0.0017)
        for v in new_v:       # new inner ring: resting on the globe
            d = (v.co - C).normalized()
            v.co = C + d * (EYE_R + 0.0001)
        ring_edges = [g for g in res['geom'] if isinstance(g, bmesh.types.BMEdge) and all(v in new_v for v in g.verts)]
        bmesh.ops.holes_fill(bm, edges=ring_edges, sides=80)


# ------------------------------------------------------------------ ears
EAR_C = (0.0165, 1.6875)       # (y, z)
EAR_A = 0.0185                 # half extent along y
EAR_B = 0.0345                 # half extent along z
EAR_TILT = math.radians(-14)   # top leans backwards


def ear_relief(bm):
    for sx in (1, -1):
        vs = [v for v in bm.verts if v.co.x * sx > 0.05 and abs(v.co.y - EAR_C[0]) < 0.034 and abs(v.co.z - EAR_C[1]) < 0.05]
        for v in vs:
            dy = v.co.y - EAR_C[0]; dz = v.co.z - EAR_C[1]
            c, s = math.cos(EAR_TILT), math.sin(EAR_TILT)
            u = (dy * c - dz * s) / EAR_A
            w = (dy * s + dz * c) / EAR_B
            rho = math.sqrt(u * u + w * w)
            if rho >= 1.35:
                continue
            # outline: smooth bump, steeper at the back edge so it reads as a flap
            edge = 1.0 - float(_smooth((rho - 0.78) / 0.50))
            # profile: helix rim ridge, antihelix, concha bowl, lobe
            rim = math.exp(-0.5 * ((rho - 0.70) / 0.13) ** 2)
            bowl = math.exp(-0.5 * (((u + 0.12) ** 2 + (w - 0.05) ** 2) / 0.12 ** 2 * 1.0))
            anti = math.exp(-0.5 * (((u - 0.18) ** 2 + (w - 0.32) ** 2) / 0.17 ** 2))
            lobe = math.exp(-0.5 * (((u + 0.05) ** 2 + (w + 0.78) ** 2) / 0.26 ** 2))
            height = 0.0090 * edge + 0.0050 * rim * edge - 0.0035 * bowl * edge + 0.0025 * anti * edge + 0.0030 * lobe * edge
            height += 0.0045 * edge * max(0.0, u)              # flares away from the head toward the back
            v.co.x += sx * height


# ------------------------------------------------------------------ eyeballs and brows (island geometry)
def add_eyeballs(bm, tag):
    rings = [0, 6, 12, 18, 23.5, 28.5, 37, 52, 70, 92, 118]
    seg = 20
    for sx in (1, -1):
        C = Vector((sx * EYE_X, EYE_CY, EYE_Z))
        rows = []
        for th in rings:
            t = math.radians(th)
            row = []
            for k in range(seg if th > 0 else 1):
                ph = 2 * math.pi * k / seg
                d = Vector((math.sin(t) * math.cos(ph), -math.cos(t), math.sin(t) * math.sin(ph)))
                r = EYE_R
                if th <= 28.5:
                    r += 0.0007 * (1.0 - th / 28.5) ** 1.3      # corneal bulge
                    if th < 11:
                        r -= 0.0004 * (1 - th / 11)               # pupil recess
                row.append(bm.verts.new(C + d * r))
            rows.append(row)
        faces = []
        for i in range(len(rings) - 1):
            a, b = rows[i], rows[i + 1]
            for k in range(seg):
                if len(a) == 1:
                    f = bm.faces.new((a[0], b[k], b[(k + 1) % seg]))
                else:
                    f = bm.faces.new((a[k], b[k], b[(k + 1) % seg], a[(k + 1) % seg]))
                th_hi = rings[i + 1]
                f[tag] = 2 if th_hi <= 28.5 else 1
                if th_hi <= 11.0 and False:
                    f[tag] = 2
        # back cap
        cap = bm.faces.new(rows[-1][::-1])
        cap[tag] = 1


def add_brows(bm, tag, bvh):
    """A tapered, arched ribbon of hair-card quads lying on the brow ridge."""
    for sx in (1, -1):
        pts = []
        N = 9
        for i in range(N):
            t = i / (N - 1)
            x = 0.0105 + t * 0.047
            zc = 1.7165 + 0.0085 * math.sin(math.pi * min(1.0, t * 0.85 + 0.12)) - 0.0040 * t * t + (0.0012 if sx > 0 else 0)
            w = (0.0042 - 0.0026 * t ** 1.4) * (1.0 + 0.5 * math.exp(-t * 6))
            pts.append((x, zc, w))
        rows = []
        for (x, z, w) in pts:
            row = []
            for dz in (-1.0, -0.33, 0.33, 1.0):
                px = sx * x; pz = z + dz * w
                hit = bvh.ray_cast(Vector((px, -0.30, pz)), Vector((0, 1, 0)), 0.5)
                y = hit[0].y if hit[0] is not None else -0.1
                lift = 0.0011 if abs(dz) < 0.5 else 0.0004
                row.append(bm.verts.new(Vector((px, y - lift, pz))))
            rows.append(row)
        for i in range(N - 1):
            for j in range(3):
                f = bm.faces.new((rows[i][j], rows[i + 1][j], rows[i + 1][j + 1], rows[i][j + 1]) if sx > 0
                                 else (rows[i][j], rows[i][j + 1], rows[i + 1][j + 1], rows[i + 1][j]))
                f[tag] = 3
        # end caps: backs are open on the skin side (hidden)
