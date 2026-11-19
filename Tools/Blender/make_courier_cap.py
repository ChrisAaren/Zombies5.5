"""Zombies 5.5: Char2_Cap_V2, a baseball cap fitted to the MPFB_Courier head.

Imports MPFB_Courier.fbx (the same file Unity uses), casts rays from the skull centre against the skin + hair, and builds a
smooth crown shell just outside the outermost surface, with a rolled band edge and a curved bill. The cap is authored in the
character rest-pose world space (pivot at the origin), so Unity places it with the character root and parents it to the
Head bone.

Run: blender -b -P Tools/Blender/make_courier_cap.py   (no --factory-startup: MPFB must be enabled)
Output: Assets/Art/Models/Characters/Char2_Cap_V2.fbx
"""
import math, os
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "Assets", "Art", "Models", "Characters", "MPFB_Courier.fbx")
OUT = os.path.join(ROOT, "Assets", "Art", "Models", "Characters", "Char2_Cap_V2.fbx")

NT, NJ = 64, 18                  # azimuth samples, rings from band to crown top
CENTER = Vector((0.0, -0.062, 1.700))
EDGE_FRONT, EDGE_BACK = 1.716, 1.688   # world Z of the band lower edge, front and back (sides interpolate)
OVER = 0.007                     # crown sits this far outside the outermost hair / skin
BAND_H = 0.014
BILL_LEN, BILL_HALF_ANGLE = 0.108, math.radians(46)


def build_bvh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    tris, verts = [], []
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or not (o.name.endswith("Body") or o.name.endswith("Hair")):
            continue
        base = len(verts)
        verts += [o.matrix_world @ v.co for v in o.data.vertices]
        o.data.calc_loop_triangles()
        tris += [tuple(base + i for i in t.vertices) for t in o.data.loop_triangles]
    return BVHTree.FromPolygons(verts, tris)


def hit_dist(bvh, d, start=0.45):
    """Outermost surface along the ray from the centre in direction d (distance from the centre)."""
    origin = CENTER + d * start
    loc, nor, idx, dist = bvh.ray_cast(origin, -d, start)
    return (start - dist) if loc is not None else None


def smooth_grid(r, passes):
    for _ in range(passes):
        n = [row[:] for row in r]
        for i in range(NT):
            for j in range(NJ):
                a = r[(i - 1) % NT][j]
                b = r[(i + 1) % NT][j]
                c = r[i][max(j - 1, 0)]
                d = r[i][min(j + 1, NJ - 1)]
                n[i][j] = r[i][j] * 0.5 + (a + b + c + d) * 0.125
        r[:] = n


def main():
    bvh = build_bvh()
    dh = [Vector((math.sin(2 * math.pi * i / NT), -math.cos(2 * math.pi * i / NT), 0.0)) for i in range(NT)]

    def edge_z(i):
        w = (1 - math.cos(2 * math.pi * i / NT)) * 0.5
        return EDGE_FRONT + (EDGE_BACK - EDGE_FRONT) * w

    def dir_at(i, phi):
        c, s = math.cos(phi), math.sin(phi)
        return Vector((dh[i].x * c, dh[i].y * c, s))

    rad = [[0.0] * NJ for _ in range(NT)]
    floor = [[0.0] * NJ for _ in range(NT)]
    phis = [[0.0] * NJ for _ in range(NT)]
    for i in range(NT):
        ze = edge_z(i)
        origin = Vector((CENTER.x, CENTER.y, ze)) + dh[i] * 0.45
        loc, _, _, _ = bvh.ray_cast(origin, -dh[i], 0.45)
        R = (Vector((loc.x - CENTER.x, loc.y - CENTER.y, 0)).length) if loc is not None else 0.1
        phi_e = math.atan2(ze - CENTER.z, R)
        for j in range(NJ):
            t = j / (NJ - 1)
            phi = phi_e + (math.pi / 2 - phi_e) * t
            phis[i][j] = phi
            h = hit_dist(bvh, dir_at(i, phi))
            h = h if h is not None else 0.1
            floor[i][j] = h + OVER
            rad[i][j] = h + OVER + 0.004
    smooth_grid(rad, 14)
    for i in range(NT):
        for j in range(NJ):
            rad[i][j] = max(rad[i][j], floor[i][j])
    smooth_grid(rad, 2)
    for i in range(NT):
        for j in range(NJ):
            # a structured front panel: the crown stands a little proud of the head over the forehead
            lift = 0.011 * max(0.0, math.cos(2 * math.pi * i / NT)) ** 2 * math.sin(math.pi * min(1.0, j / (NJ - 1) * 1.4)) 
            rad[i][j] = max(rad[i][j], floor[i][j] - 0.002) + lift

    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    grid = [[None] * NJ for _ in range(NT)]
    for i in range(NT):
        for j in range(NJ):
            if j == NJ - 1 and i > 0:
                grid[i][j] = grid[0][j]
                continue
            grid[i][j] = bm.verts.new(CENTER + dir_at(i, phis[i][j]) * rad[i][j])

    def face(vs, uvs, outward_from=None, up=None):
        vs = list(dict.fromkeys(vs))
        if len(vs) < 3:
            return
        f = bm.faces.new(vs)
        f.normal_update()
        n = f.normal
        flip = False
        if outward_from is not None:
            flip = n.dot(f.calc_center_median() - outward_from) < 0
        elif up is not None:
            flip = n.z * up < 0
        if flip:
            f.normal_flip()
        for l, (u, v) in zip(f.loops, [uvs[0]] * len(vs)):
            l[uv].uv = (u, v)
        f.smooth = True

    for i in range(NT):
        i2 = (i + 1) % NT
        for j in range(NJ - 1):
            face([grid[i][j], grid[i2][j], grid[i2][j + 1], grid[i][j + 1]], [(i / NT, j * 0.04)], outward_from=CENTER)

    # button on the crown top
    top_v = grid[0][NJ - 1]
    btn = []
    for k in range(10):
        a = 2 * math.pi * k / 10
        btn.append(bm.verts.new(top_v.co + Vector((math.cos(a) * 0.011, math.sin(a) * 0.011, -0.002))))
    cap_v = bm.verts.new(top_v.co + Vector((0, 0, 0.007)))
    for k in range(10):
        face([btn[k], btn[(k + 1) % 10], cap_v], [(0, 0)], outward_from=CENTER)

    # band: a ring dropping from the crown edge, bulged slightly, then tucked in (rolled edge)
    band_o, band_i = [], []
    for i in range(NT):
        p0 = grid[i][0].co
        outward = Vector((dh[i].x, dh[i].y, 0))
        p1 = Vector((p0.x, p0.y, p0.z - BAND_H)) + outward * 0.003
        p2 = Vector((p1.x, p1.y, p1.z - 0.0015)) - outward * 0.011
        band_o.append(bm.verts.new(p1))
        band_i.append(bm.verts.new(p2))
    for i in range(NT):
        i2 = (i + 1) % NT
        face([grid[i][0], grid[i2][0], band_o[i2], band_o[i]], [(i / NT, 0.0)], outward_from=CENTER)
        face([band_o[i], band_o[i2], band_i[i2], band_i[i]], [(i / NT, 0.0)], outward_from=CENTER)

    # bill: a curved plate with thickness along the front arc of the band
    ntb, nvb = 21, 7
    top = [[None] * nvb for _ in range(ntb)]
    bot = [[None] * nvb for _ in range(ntb)]
    for a in range(ntb):
        ang = -BILL_HALF_ANGLE + 2 * BILL_HALF_ANGLE * a / (ntb - 1)
        idx = (ang / (2 * math.pi) * NT) % NT
        i0 = int(math.floor(idx)) % NT
        i1 = (i0 + 1) % NT
        f = idx - math.floor(idx)
        pb = band_o[i0].co.lerp(band_o[i1].co, f) + Vector((0, 0, 0.012))
        out = Vector((math.sin(ang), -math.cos(ang), 0))
        length = BILL_LEN * max(0.0, math.cos(ang * (math.pi / 2) / BILL_HALF_ANGLE)) ** 0.6 + 0.004
        for v in range(nvb):
            s = v / (nvb - 1)
            drop = length * s * 0.24 + 0.05 * (ang / BILL_HALF_ANGLE) ** 2 * length * s
            p = pb + out * (length * s) - Vector((0, 0, drop))
            top[a][v] = bm.verts.new(p + Vector((0, 0, 0.0025)))
            bot[a][v] = bm.verts.new(p - Vector((0, 0, 0.0025)))
    for a in range(ntb - 1):
        for v in range(nvb - 1):
            face([top[a][v], top[a + 1][v], top[a + 1][v + 1], top[a][v + 1]], [(a / ntb, v / nvb)], up=1)
            face([bot[a][v], bot[a + 1][v], bot[a + 1][v + 1], bot[a][v + 1]], [(a / ntb, v / nvb)], up=-1)
    for a in range(ntb - 1):
        face([top[a][nvb - 1], top[a + 1][nvb - 1], bot[a + 1][nvb - 1], bot[a][nvb - 1]], [(0, 0)], outward_from=CENTER)
    for a in (0, ntb - 1):
        for v in range(nvb - 1):
            face([top[a][v], top[a][v + 1], bot[a][v + 1], bot[a][v]], [(0, 0)], outward_from=CENTER)

    me = bpy.data.meshes.new("Char2_Cap_V2")
    bm.to_mesh(me)
    bm.free()
    mat = bpy.data.materials.new("Cap")
    me.materials.append(mat)
    ob = bpy.data.objects.new("Char2_Cap_V2", me)
    bpy.context.scene.collection.objects.link(ob)
    for o in list(bpy.context.scene.objects):
        o.select_set(o is ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(
        filepath=OUT, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False)
    print("cap exported", OUT, len(me.vertices), "verts")


main()
