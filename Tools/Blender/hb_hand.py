"""Hand cages (palm slab + five extruded digits) spliced into the skin-modifier cage at each wrist."""
import math
import bmesh
from mathutils import Vector, Quaternion
import hb_shape as H

ZC = 1.4415
SEC = [  # slab sections: x, y-scale, half thickness
    (0.746, 0.80, 0.0140),
    (0.772, 0.96, 0.0130),
    (0.795, 1.00, 0.0118),
]
YB = [-0.0445, -0.0225, -0.0005, 0.0195, 0.0390]       # column boundaries (cell centers ~ finger axes)
RING_HW = [0.0102, 0.0095, 0.0086, 0.0072, 0.0050]            # finger ring half-widths (R0..R3)
RING_HT = [0.0114, 0.0098, 0.0088, 0.0074, 0.0052]


def _best_assign(A, corners):
    """Map the 4 wrist-ring verts onto the slab corners (TL, TR, BR, BL), searching cyclic shifts/reflections."""
    cyc = A
    best = None
    for refl in (False, True):
        seq = cyc[::-1] if refl else cyc
        for sh in range(4):
            seq2 = seq[sh:] + seq[:sh]
            d = sum((a.co - c).length_squared for a, c in zip(seq2, corners))
            if best is None or d < best[0]:
                best = (d, seq2)
    return best[1]


def _order_ring(A, sx):
    """Sort a 4-vert ring cyclically by angle around its centre in the y-z plane."""
    c = sum((v.co for v in A), Vector()) / 4
    return sorted(A, key=lambda v: math.atan2(v.co.z - c.z, v.co.y - c.y))


def add_hands(bm):
    for sx in (1.0, -1.0):
        _add_hand(bm, sx)


def _add_hand(bm, sx):
    caps = [f for f in bm.faces if len(f.verts) == 4 and all(abs(v.co.x * sx - .716) < .004 for v in f.verts)]
    assert len(caps) == 1, "wrist cap faces: %d" % len(caps)
    A = _order_ring(list(caps[0].verts), sx)
    bmesh.ops.delete(bm, geom=[caps[0]], context='FACES_ONLY')

    def V(x, y, z):
        return bm.verts.new(Vector((sx * x, y, z)))
    T, B = [], []
    for (x, ys, t) in SEC:
        T.append([V(x, y * ys, ZC + t) for y in YB])
        B.append([V(x, y * ys, ZC - t) for y in YB])

    def F(*vs):
        return bm.faces.new(vs)

    for s in range(2):
        for j in range(4):
            F(T[s][j], T[s][j + 1], T[s + 1][j + 1], T[s + 1][j])
            F(B[s][j], B[s + 1][j], B[s + 1][j + 1], B[s][j + 1])
        if s != 0 or sx < 0 and False:
            F(T[s][0], T[s + 1][0], B[s + 1][0], B[s][0])
        F(T[s][4], B[s][4], B[s + 1][4], T[s + 1][4])
    # bridge the 4-vert wrist ring onto the 10-vert slab ring (two 7-gons top/bottom, two quads on the sides)
    corners = [T[0][0].co, T[0][4].co, B[0][4].co, B[0][0].co]
    TL, TR, BR, BL = _best_assign(A, corners)
    F(TL, T[0][0], T[0][1], T[0][2], T[0][3], T[0][4], TR)
    F(BR, B[0][4], B[0][3], B[0][2], B[0][1], B[0][0], BL)
    F(TR, T[0][4], B[0][4], BR)
    F(BL, B[0][0], T[0][0], TL)

    # four fingers extruded from the knuckle cells
    for j, (fn, (pts, r0)) in enumerate(H.FINGER_DEF.items()):
        prev = [T[2][j], T[2][j + 1], B[2][j + 1], B[2][j]]
        for i in range(1, 6):
            if i <= 3:
                p = pts[i]; hw = r0 * (1 - .07 * i) * 1.08
            elif i == 4:
                p = (pts[4][0] - .001, pts[4][1], pts[4][2]); hw = r0 * .72
            else:
                p = (pts[4][0] + .0055, pts[4][1], pts[4][2] - .0005); hw = r0 * .40
            ht = hw * 1.04
            ring = [V(p[0], p[1] - hw, p[2] + ht), V(p[0], p[1] + hw, p[2] + ht),
                    V(p[0], p[1] + hw, p[2] - ht), V(p[0], p[1] - hw, p[2] - ht)]
            for k in range(4):
                F(prev[k], prev[(k + 1) % 4], ring[(k + 1) % 4], ring[k])
            prev = ring
        F(*prev)

    # thumb from the index-side wall of the palm (between section 0 and 1)
    prev = [T[0][0], B[0][0], B[1][0], T[1][0]]
    c0 = sum((v.co for v in prev), Vector()) / 4
    path = [(.757, -.060, 1.4335, .97), (.782, -.078, 1.4265, .90), (.803, -.094, 1.4200, .84), (.817, -.104, 1.4170, .75)]
    d_old = Vector((0, -1, 0))
    c_old = c0
    for (px, py, pz, sc) in path:
        c_new = Vector((sx * px, py, pz))
        d_new = (c_new - c_old).normalized()
        q = d_old.rotation_difference(d_new)
        ring = []
        for v in prev:
            r = v.co - c_old
            r = q @ r
            ring.append(bm.verts.new(c_new + r * sc))
        # ring scaling is cumulative through the replaced prev ring positions
        for k in range(4):
            F(prev[k], prev[(k + 1) % 4], ring[(k + 1) % 4], ring[k])
        prev = ring
        c_old = c_new
        d_old = d_new
    F(*prev)
