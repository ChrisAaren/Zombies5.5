"""Stage 5: numeric checks + labeled sheet for one version folder.

  blender -b --factory-startup --python check.py -- --character testcourier --out work/testcourier/v001 [--prev work/testcourier/v000]

Reads skin.blend, joints.json, skeleton.json, cleanup.json, poses.json, config. Writes <out>/report.json,
<out>/sheet.png and (when --prev has a sheet.png) <out>/sheet_compare.png.
"""
import bpy, bmesh, os, sys, json, math, time
import numpy as np
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pl_common as pc
import pl_render as pr
import bl_skeleton as sk

A = pc.parse_args({"character": "testcourier", "out": "", "prev": ""})
OUT = os.path.abspath(A["out"])
NAME = A["character"]
CFG = pc.load_config(OUT)
CC = CFG["check"]
BUD = CFG["budget"]
VERSION = os.path.basename(OUT)

pc.open_blend(os.path.join(OUT, "skin.blend"))
arm = bpy.data.objects["Armature"]
hi = bpy.data.objects["HI"]
proxy = bpy.data.objects["PROXY"]
LODS = [bpy.data.objects["%s_LOD%d" % (NAME, i)] for i in range(len(CFG["lod_ratios"]))]
lod0 = LODS[0]
J = pc.read_json(os.path.join(OUT, "joints.json"))
SKJ = pc.read_json(os.path.join(OUT, "skeleton.json"))
CLJ = pc.read_json(os.path.join(OUT, "cleanup.json"))
POSES = pc.read_json(os.path.join(pc.HERE, "poses.json"))["poses"]
H = J["height"]
CHECKS = []


def lim(li, key):
    """Per-LOD limit: check.lod_limits[str(li)][key], else the global check[key]."""
    return CC.get("lod_limits", {}).get(str(li), {}).get(key, CC[key])


LIMITS_APPLIED = {"LOD%d" % i: {k: lim(i, k) for k in ("max_edge_stretch_ratio", "max_self_intersections_per_1k_tris")}
                  for i in range(len(CFG["lod_ratios"]))}


def add(cid, group, value, threshold, op, detail=None, info_only=False):
    """op: '<=', '>=', '==' or 'bool'. pass is computed here."""
    if op == "<=":
        ok = value <= threshold
    elif op == ">=":
        ok = value >= threshold
    elif op == "==":
        ok = value == threshold
    else:
        ok = bool(value)
    CHECKS.append({"id": cid, "group": group, "value": value, "threshold": threshold, "op": op,
                   "pass": bool(ok) if not info_only else True, "info_only": info_only, "detail": detail})
    return ok


# ------------------------------------------------------------------ mesh topology
def topo(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.edges.ensure_lookup_table()
    boundary = [e for e in bm.edges if len(e.link_faces) == 1]
    nonman = [e for e in bm.edges if len(e.link_faces) > 2]
    loose = [v for v in bm.verts if not v.link_edges]
    degenerate = [f for f in bm.faces if f.calc_area() < 1e-10]
    # boundary loops via union-find over boundary-edge vertices
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for e in boundary:
        a, b = find(e.verts[0].index), find(e.verts[1].index)
        if a != b:
            parent[a] = b
    loops = len({find(v) for v in list(parent)})
    out = {"boundary_edges": len(boundary), "boundary_loops": loops, "non_manifold_edges": len(nonman),
           "loose_verts": len(loose), "degenerate_faces": len(degenerate)}
    bm.free()
    return out


def tris(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


topo_hi, topo_g, topo_p = topo(hi), topo(lod0), topo(proxy)
tier = BUD
t0 = tris(lod0)
add("mesh.tris_lod0", "mesh", t0, [tier["tris_min"], tier["tris_max"]], "range",
    "LOD0 triangles vs tier budget '%s'" % CFG["tier"])
CHECKS[-1]["pass"] = tier["tris_min"] <= t0 <= tier["tris_max"]
for i, ob in enumerate(LODS[1:], start=1):
    r = CFG["lod_ratios"][i]
    tr = tris(ob)
    tol = CFG["lod_ratio_tolerance"]
    add("mesh.tris_lod%d" % i, "mesh", round(tr / t0, 3), [round(r - tol * r, 3), round(r + tol * r, 3)], "range",
        "LOD%d triangle ratio to LOD0 (%d tris)" % (i, tr))
    CHECKS[-1]["pass"] = r * (1 - tol) <= tr / t0 <= r * (1 + tol)
RM = CLJ.get("hidden_body_removal", {})
exp_loops = RM.get("boundary_loops_after_removal", topo_hi["boundary_loops"])
add("mesh.new_boundary_loops", "mesh", topo_g["boundary_loops"] - exp_loops, CC["max_new_boundary_loops"], "<=",
    "game boundary loops %d vs expected %d = source's intentional openings (%d: cuffs/neck/hem of clothes) plus the openings left by the intentional hidden-body removal (source mesh after removal: %d loops)"
    % (topo_g["boundary_loops"], exp_loops, topo_hi["boundary_loops"], exp_loops))
add("mesh.non_manifold_edges_game", "mesh", topo_g["non_manifold_edges"], topo_hi["non_manifold_edges"] + CC["non_manifold_slack"], "<=",
    "game %d vs source %d" % (topo_g["non_manifold_edges"], topo_hi["non_manifold_edges"]))
add("mesh.degenerate_faces_game", "mesh", topo_g["degenerate_faces"], 0, "<=")
add("mesh.loose_verts_game", "mesh", topo_g["loose_verts"], 0, "<=")
add("mesh.proxy_watertight", "mesh", topo_p["boundary_edges"] + topo_p["non_manifold_edges"], 0, "==",
    "proxy boundary edges %d, non-manifold edges %d, tris %d" % (topo_p["boundary_edges"], topo_p["non_manifold_edges"], tris(proxy)))
removal_report = dict(RM)
if RM.get("enabled"):
    # LODs are decimated from a mesh that already lacks the removed faces, so the per-LOD saving is an estimate:
    # tris the LOD would need to cover the same surface = N / (1 - r), r = removed share of the source triangles.
    r_ = RM.get("removed_tris", 0) / max(CLJ.get("source_tris", 1), 1)
    removal_report["removed_tri_share_of_source"] = round(r_, 4)
    removal_report["per_lod"] = {
        "LOD%d" % i: {"tris_in_lod": tris(o), "estimated_body_tris_removed": int(round(tris(o) * r_ / max(1 - r_, 1e-6)))}
        for i, o in enumerate(LODS)}
# textures
imgs = {}
for kind in ("Albedo", "Normal"):
    im = bpy.data.images.get("%s_%s" % (NAME, kind))
    p = os.path.join(OUT, "textures", "%s_%s.png" % (NAME, kind))
    ok = os.path.exists(p)
    sz = list(im.size) if im else [0, 0]
    imgs[kind] = sz
    add("mesh.texture_%s" % kind.lower(), "mesh", sz[0] if ok else 0, BUD["texture_size"], "==",
        "baked texture size %s, file exists: %s" % (sz, ok))
uv = lod0.data.uv_layers.active
uvs = np.empty(len(uv.data) * 2)
uv.data.foreach_get("uv", uvs)
uvs = uvs.reshape(-1, 2)
oob = int(((uvs < -0.001) | (uvs > 1.001)).any(axis=1).sum())
add("mesh.uv_out_of_bounds_loops", "mesh", oob, 0, "<=")

# ------------------------------------------------------------------ skeleton
bones_present = {b.name for b in arm.data.bones}
missing = [b for b in sk.BONE_NAMES if b not in bones_present]
add("skeleton.missing_humanoid_bones", "skeleton", len(missing), 0, "<=", "missing: %s" % missing)
add("skeleton.mirror_mismatch_m", "skeleton", round(SKJ["mirror_max_error_m"], 6), CC["mirror_tolerance_m"], "<=",
    "worst bone %s" % SKJ["mirror_worst_bone"])
low = sorted([(k, v["confidence"]) for k, v in J["joints"].items() if v["confidence"] < CFG["joints"]["min_confidence"]],
             key=lambda t: t[1])
add("skeleton.low_confidence_joints", "skeleton", len(low), 0, "<=", "joints below %.2f: %s" % (CFG["joints"]["min_confidence"], low[:12]))
est = [k for k, v in J["joints"].items() if v["method"] == "estimated"]
add("skeleton.estimated_joints", "skeleton", len(est), len(est), "<=", "informational: joints placed by standard proportions: %s" % est, info_only=True)
nonleaf_ok = all(b.use_deform for b in arm.data.bones)
add("skeleton.all_bones_deform", "skeleton", nonleaf_ok, True, "bool")

# ------------------------------------------------------------------ proxy fingers (source vs proxy)
V_hi, T_hi = pc.mesh_np(hi)
V_px, T_px = pc.mesh_np(proxy)
E_hi, E_px = pc.mesh_edges(T_hi, len(V_hi)), pc.mesh_edges(T_px, len(V_px))
finger_detail = {}
for side in ("Left", "Right"):
    hp = J["hand_params"][side]
    ts_p, _ = pc.hand_tips(V_px, T_px, hp["wrist"], hp["axis"], edges=E_px)
    ts_s, _ = pc.hand_tips(V_hi, T_hi, hp["wrist"], hp["axis"], edges=E_hi)
    plane_max = []
    for Vv, Tt in ((V_hi, T_hi), (V_px, T_px)):
        best = 0
        for pl in J["hand_planes"][side]:
            cs = [c for c in pc.slice_plane(Vv, Tt, pl["origin"], pl["normal"]) if c["n"] >= 5 and
                  np.linalg.norm(c["centroid"] - np.array(pl["origin"])) < 0.13]
            best = max(best, len(cs))
        plane_max.append(best)
    finger_detail[side] = {"source_tips": len(ts_s), "proxy_tips": len(ts_p), "source_max_slice_pieces": plane_max[0],
                           "proxy_max_slice_pieces": plane_max[1]}
    ok = len(ts_p) == len(ts_s) and len(ts_p) >= 5
    add("proxy.fingers_%s" % side.lower(), "proxy", len(ts_p), len(ts_s), "==",
        "fingertips (geodesic maxima) proxy %d vs source %d; max slice pieces across the hand: proxy %d vs source %d"
        % (len(ts_p), len(ts_s), plane_max[1], plane_max[0]))
    CHECKS[-1]["pass"] = ok

# ------------------------------------------------------------------ weights
BONES = [b.name for b in arm.data.bones]
BIDX = {n: i for i, n in enumerate(BONES)}
HEADS = np.array([arm.data.bones[n].head_local[:] for n in BONES])
TAILS = np.array([arm.data.bones[n].tail_local[:] for n in BONES])
BONE_REGION = {n: pc.region_of_bone(n) for n in BONES}
REGIONS = sorted(set(BONE_REGION.values()))
FINGER_KEYS = ("Thumb", "Index", "Middle", "Ring", "Little")


def weight_matrix(ob):
    n = len(ob.data.vertices)
    Wm = np.zeros((n, len(BONES)))
    gi = {vg.index: BIDX.get(vg.name, -1) for vg in ob.vertex_groups}
    for v in ob.data.vertices:
        for g in v.groups:
            b = gi.get(g.group, -1)
            if b >= 0:
                Wm[v.index, b] = g.weight
    return Wm


def seg_dist(P, a, b):
    ab = b - a
    t = np.clip((P - a) @ ab / max(float(ab @ ab), 1e-12), 0, 1)
    return np.linalg.norm(P - (a + ab * t[:, None]), axis=1)


def all_seg_dist(P):
    out = np.empty((len(P), len(BONES)))
    for i in range(len(BONES)):
        out[:, i] = seg_dist(P, HEADS[i], TAILS[i])
    return out


WEIGHT_INFO = {}
for li, ob in enumerate(LODS):
    Wm = weight_matrix(ob)
    P, _ = pc.mesh_np(ob)
    ssum = Wm.sum(axis=1)
    infl = (Wm > 1e-6).sum(axis=1)
    strongest = Wm.argmax(axis=1)
    d_str = np.linalg.norm(P - np.array([0, 0, 0]), axis=1)  # placeholder, replaced below
    dist_all = all_seg_dist(P)
    d_str = dist_all[np.arange(len(P)), strongest]
    far_lim = CC["far_bone_fraction_of_height"] * H
    far = (d_str > far_lim) & (ssum > 0)
    pre = "weights.lod%d." % li
    add(pre + "unweighted_verts", "weights", int((ssum < 1e-6).sum()), 0, "<=")
    add(pre + "verts_over_4_influences", "weights", int((infl > 4).sum()), 0, "<=")
    add(pre + "weights_not_normalized", "weights", int((np.abs(ssum - 1.0) > CC["weight_sum_tolerance"]).sum()), 0, "<=",
        "max |sum-1| = %.5f" % float(np.abs(ssum - 1).max()))
    add(pre + "strongest_bone_far", "weights", int(far.sum()), 0, "<=",
        "vertices whose strongest bone is > %.0f%% of height (%.2f m) away; max distance %.3f m" % (
            CC["far_bone_fraction_of_height"] * 100, far_lim, float(d_str.max())))
    if li == 0:
        WEIGHT_INFO = {"W": Wm, "P": P, "strongest": strongest, "dist": dist_all, "far": far, "ssum": ssum}
        # hand leak and finger cross bleed
        leak_total, finger_verts, bleed = 0, 0, 0
        leak_names = {}
        for side in ("Left", "Right"):
            hp = J["hand_params"][side]
            Wp, ax = np.array(hp["wrist"]), np.array(hp["axis"])
            rel = (P - Wp) @ ax
            hand_zone = (rel > 0.015) & (np.linalg.norm(P - Wp, axis=1) < hp["hand_len"] * 1.6)
            allowed = [BIDX[side + "Hand"], BIDX[side + "LowerArm"]] + [BIDX[side + f + p] for f in FINGER_KEYS for p in sk.PHALANGES]
            others = np.ones(len(BONES), bool)
            others[allowed] = False
            leak_w = Wm[:, others].sum(axis=1)
            leak = hand_zone & (leak_w > 0.05)
            leak_total += int(leak.sum())
            for v in np.nonzero(leak)[0][:200]:
                b = int(np.argmax(np.where(others, Wm[v], -1)))
                leak_names[BONES[b]] = leak_names.get(BONES[b], 0) + 1
            fidx = [BIDX[side + f + p] for f in FINGER_KEYS for p in sk.PHALANGES]
            fd = dist_all[:, fidx]
            nearest = fd.argmin(axis=1)
            on_finger = hand_zone & (fd.min(axis=1) < 0.012) & (rel > hp["hand_len"] * 0.45)
            finger_of = lambda bi: BONES[bi][len(side):].replace("Proximal", "").replace("Intermediate", "").replace("Distal", "")
            for v in np.nonzero(on_finger)[0]:
                sb = BONES[strongest[v]]
                if not any(sb == side + f + p for f in FINGER_KEYS for p in sk.PHALANGES):
                    continue
                finger_verts += 1
                if finger_of(fidx[nearest[v]]) != sb[len(side):].replace("Proximal", "").replace("Intermediate", "").replace("Distal", ""):
                    bleed += 1
        add("weights.hand_leak_verts", "weights", leak_total, CC["max_hand_leak_verts"], "<=",
            "hand-zone vertices with >5%% weight on bones other than hand/fingers/lower arm; by bone: %s" % leak_names)
        ratio = bleed / max(finger_verts, 1)
        add("weights.finger_cross_bleed_ratio", "weights", round(ratio, 4), CC["max_finger_cross_bleed_ratio"], "<=",
            "%d of %d finger-surface vertices are dominated by a different finger's bone" % (bleed, finger_verts))
        WEIGHT_INFO["far_idx"] = np.nonzero(far)[0]
        WEIGHT_INFO["far_detail"] = [{"pos": P[v].round(3).tolist(), "strongest": BONES[int(strongest[v])], "dist": round(float(d_str[v]), 3)}
                                     for v in WEIGHT_INFO["far_idx"][:8]]

# ------------------------------------------------------------------ stress poses
pb = arm.pose.bones
arm.data.pose_position = 'POSE'
arm.rotation_mode = 'XYZ'


def reset_pose():
    for p in pb:
        p.rotation_mode = 'QUATERNION'
        p.rotation_quaternion = (1, 0, 0, 0)
        p.location = (0, 0, 0)
        p.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def apply_world_rotation(p, axis, deg):
    bpy.context.view_layer.update()
    m = p.matrix.copy()
    head = m.translation.copy()
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis).normalized())
    T1, T2 = Matrix.Translation(head), Matrix.Translation(-head)
    p.matrix = T1 @ R @ T2 @ m
    bpy.context.view_layer.update()


def bone_dir(p):
    m = p.matrix
    return (m.to_3x3() @ Vector((0, 1, 0))).normalized()


def apply_pose(pose):
    reset_pose()
    for op in pose["ops"]:
        for side in op.get("sides", [None]):
            name = op["bone"].replace("{S}", side or "")
            sign = -1.0 if (side == "Right" and op.get("mirror_x")) else 1.0
            p = pb[name]

            def mir(v):
                return Vector((v[0] * sign, v[1], v[2]))
            if "aim" in op:
                bpy.context.view_layer.update()
                cur, tgt = bone_dir(p), mir(op["aim"]).normalized()
                axis = cur.cross(tgt)
                if axis.length > 1e-6:
                    apply_world_rotation(p, axis, math.degrees(cur.angle(tgt)))
            elif "flex" in op:
                bpy.context.view_layer.update()
                cur, tw = bone_dir(p), mir(op["flex"]["toward"]).normalized()
                axis = cur.cross(tw)
                if axis.length < 1e-6:
                    axis = Vector((1, 0, 0))
                apply_world_rotation(p, axis, op["flex"]["deg"])
            elif "rot" in op:
                apply_world_rotation(p, mir(op["rot"]["axis"]), op["rot"]["deg"])


def tri_volume(V, T):
    return float(np.einsum("ij,ij->i", V[T[:, 0]], np.cross(V[T[:, 1]], V[T[:, 2]])).sum() / 6.0)


def tri_areas(V, T):
    return 0.5 * np.linalg.norm(np.cross(V[T[:, 1]] - V[T[:, 0]], V[T[:, 2]] - V[T[:, 0]]), axis=1)


class LodState:
    pass


LS = []
bpy.ops.object.mode_set(mode='OBJECT') if bpy.context.object and bpy.context.object.mode != 'OBJECT' else None
reset_pose()
for li, ob in enumerate(LODS):
    s = LodState()
    s.ob = ob
    s.V0, s.T = pc.mesh_np(ob, depsgraph=True)
    Wm = weight_matrix(ob)
    s.strong = Wm.argmax(axis=1)
    s.region_of_vert = np.array([REGIONS.index(BONE_REGION[BONES[b]]) for b in s.strong])
    me = ob.data
    ed = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ed)
    s.E = ed.reshape(-1, 2)
    s.len0 = np.linalg.norm(s.V0[s.E[:, 0]] - s.V0[s.E[:, 1]], axis=1)
    s.valid_edges = s.len0 > CC["min_edge_len_m"]
    s.region_of_edge = s.region_of_vert[s.E[:, 0]]
    s.tri_region = s.region_of_vert[s.T[:, 0]]
    s.area0 = tri_areas(s.V0, s.T)
    s.vol0 = tri_volume(s.V0, s.T)
    s.pairs0 = None
    LS.append(s)


def overlap_pairs(V, T):
    bvh = BVHTree.FromPolygons([Vector(v) for v in V.tolist()], T.tolist(), epsilon=0.0)
    pr_ = np.array(bvh.overlap(bvh), dtype=np.int64).reshape(-1, 2)
    if len(pr_) == 0:
        return set(), np.zeros((0, 2), np.int64)
    pr_ = pr_[pr_[:, 0] < pr_[:, 1]]
    A_, B_ = T[pr_[:, 0]], T[pr_[:, 1]]
    shared = (A_[:, :, None] == B_[:, None, :]).any(axis=(1, 2))
    pr_ = pr_[~shared]
    return set(map(tuple, pr_.tolist())), pr_


for s in LS:
    s.pairs0, _ = overlap_pairs(s.V0, s.T)

STRESS = {}
POSE_POINTS = {}
for pose in POSES:
    apply_pose(pose)
    res = {}
    for li, s in enumerate(LS):
        V, _ = pc.mesh_np(s.ob, depsgraph=True)
        vol = tri_volume(V, s.T)
        vol_pct = (vol - s.vol0) / abs(s.vol0) * 100.0
        pairs, arr = overlap_pairs(V, s.T)
        new = [p for p in pairs if p not in s.pairs0]
        new_arr = np.array(new, dtype=np.int64).reshape(-1, 2)
        ln = np.linalg.norm(V[s.E[:, 0]] - V[s.E[:, 1]], axis=1)
        ratio = np.where(s.valid_edges, ln / np.maximum(s.len0, 1e-9), 1.0)
        stretch, compress = {}, {}
        for ri, rn in enumerate(REGIONS):
            m = (s.region_of_edge == ri) & s.valid_edges
            if m.any():
                stretch[rn] = round(float(ratio[m].max()), 3)
                compress[rn] = round(float(ratio[m].min()), 3)
        area = tri_areas(V, s.T)
        area_pct = {}
        for ri, rn in enumerate(REGIONS):
            m = s.tri_region == ri
            if m.sum() > 20:
                area_pct[rn] = round(float((area[m].sum() / max(s.area0[m].sum(), 1e-9) - 1) * 100), 1)
        worst_reg = max(stretch, key=stretch.get)
        wi = int(np.argmax(np.where(s.valid_edges, ratio, 0)))
        wloc = ((s.V0[s.E[wi, 0]] + s.V0[s.E[wi, 1]]) / 2).round(3).tolist()
        wbones = [BONES[int(s.strong[s.E[wi, 0]])], BONES[int(s.strong[s.E[wi, 1]])]]
        res["LOD%d" % li] = {
            "volume_change_pct": round(vol_pct, 2), "tris": len(s.T), "new_self_intersections": len(new), "rest_self_intersections": len(s.pairs0),
            "worst_edge_stretch": stretch[worst_reg], "worst_stretch_region": worst_reg, "worst_edge_rest_pos": wloc, "worst_edge_bones": wbones, "edge_stretch_by_region": stretch,
            "edge_compress_by_region": compress, "area_change_pct_by_region": area_pct}
        if li == 0 and len(new_arr):
            cen = (V[s.T[new_arr[:, 0]]].mean(axis=1) + V[s.T[new_arr[:, 1]]].mean(axis=1)) / 2
            POSE_POINTS[pose["id"]] = cen[:400]
        elif li == 0:
            POSE_POINTS[pose["id"]] = np.zeros((0, 3))
        pre = "stress.%s.lod%d." % (pose["id"], li)
        add(pre + "volume_change_pct", "stress", round(vol_pct, 2), [-CC["max_volume_loss_pct"], CC["max_volume_gain_pct"]], "range",
            "game-mesh signed volume vs rest")
        CHECKS[-1]["pass"] = -CC["max_volume_loss_pct"] <= vol_pct <= CC["max_volume_gain_pct"]
        per1k = round(len(new) / (len(s.T) / 1000.0), 2)
        add(pre + "new_self_intersections_per_1k_tris", "stress", per1k, lim(li, "max_self_intersections_per_1k_tris"), "<=",
            "%d BVH overlaps (neighbouring faces ignored) not present at rest (rest has %d); %d tris; limit applied: LOD%d value" % (len(new), len(s.pairs0), len(s.T), li))
        add(pre + "worst_edge_stretch", "stress", stretch[worst_reg], lim(li, "max_edge_stretch_ratio"), "<=",
            "worst region %s (edge length ratio vs rest); limit applied: LOD%d value" % (worst_reg, li))
    STRESS[pose["id"]] = res
reset_pose()

# ------------------------------------------------------------------ sheet
t_sheet = time.time()
TILE = int(CC["tile_px"])
st = pr.Stage(TILE)
bpy.context.scene.display.shading.light = 'FLAT'
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.hide_render = True
lod0.hide_render = False
arm.hide_render = True
CAM_T = (0.0, 0.0, 1.05)
VIEWS = {"front": ((0.0, -7.0, 1.05), 2.7), "side": ((7.0, 0.0, 1.05), 2.7)}
BONE_LINES = [(n, b) for n, b in ((b.name, b) for b in arm.data.bones)]


def bone_overlay(img, bones_pose=False):
    """Joint dots (colored by confidence) and bone lines from the current camera."""
    for b in arm.data.bones:
        if bones_pose:
            pbn = pb[b.name]
            h, t = pbn.head, pbn.tail
        else:
            h, t = b.head_local, b.tail_local
        p0, p1 = st.project(h), st.project(t)
        pr.line(img, p0, p1, (1.0, 0.95, 0.2), 1.5)
    for b in arm.data.bones:
        h = pb[b.name].head if bones_pose else b.head_local
        c = J["joints"][b.name]["confidence"] if b.name in J["joints"] else 1
        col = (0.2, 1.0, 0.3) if c >= 0.7 else ((1.0, 0.8, 0.1) if c >= CFG["joints"]["min_confidence"] else (1.0, 0.2, 0.2))
        x, y = st.project(h)
        pr.disk(img, x, y, 3.0, (0, 0, 0))
        pr.disk(img, x, y, 2.2, col)


# vertex colors by strongest bone on a temp copy of LOD0
wcopy = lod0.copy()
wcopy.data = lod0.data.copy()
wcopy.name = "_weightcolors"
bpy.context.scene.collection.objects.link(wcopy)
wcopy.modifiers.clear()
for vg in list(wcopy.vertex_groups):
    pass
cols = np.array([pr.color_for(BONES[b]) for b in WEIGHT_INFO["strongest"]], np.float32)
rgba = np.concatenate([cols, np.ones((len(cols), 1), np.float32)], axis=1)
ca = wcopy.data.color_attributes.new("Col", 'FLOAT_COLOR', 'POINT')
ca.data.foreach_set("color", rgba.ravel())
wcopy.data.color_attributes.active_color = ca
wcopy.parent = None
wcopy.hide_render = True
# mark far-bone vertices in white by overriding colour
far_idx = WEIGHT_INFO["far_idx"]
if len(far_idx):
    rgba2 = rgba.copy()
    rgba2[far_idx] = (1, 1, 1, 1)
    ca.data.foreach_set("color", rgba2.ravel())

# make wcopy follow the armature in pose: re-add the armature modifier (no parent -> identity)
am = wcopy.modifiers.new("Armature", 'ARMATURE')
am.object = arm

lod0.color = (0.72, 0.76, 0.86, 1)
for L in LODS:
    L.color = (0.72, 0.76, 0.86, 1)

tiles, labels = [], []


def tile(img, label, overlay=None):
    if overlay:
        overlay(img)
    tiles.append(img)
    labels.append(label)


def full_tile(view, visible, color_type, label, overlay=None):
    loc, ortho = VIEWS[view]
    img = st.render(loc, CAM_T, ortho, color_type, visible)
    tile(img, label, overlay)
    return img


def close_tile(center, view, ortho, visible, color_type, label, overlay=None):
    c = Vector(center)
    if view == "front":
        loc = c + Vector((0, -6, 0))
    elif view == "side":
        loc = c + Vector((6, 0, 0))
    else:
        loc = c + Vector((0, 0, 6))
    img = st.render(loc, c, ortho, color_type, visible)
    tile(img, label, overlay)


reset_pose()
bpy.context.scene.display.shading.light = 'STUDIO'
full_tile("front", [lod0], 'OBJECT', "FRONT JOINTS+BONES", lambda im: bone_overlay(im))
full_tile("side", [lod0], 'OBJECT', "SIDE JOINTS+BONES", lambda im: bone_overlay(im))
bpy.context.scene.display.shading.light = 'FLAT'
full_tile("front", [wcopy], 'VERTEX', "WEIGHTS FRONT (WHITE=FAR)")
full_tile("side", [wcopy], 'VERTEX', "WEIGHTS SIDE")

bpy.context.scene.display.shading.light = 'STUDIO'
HAND_V = {}
for pose in POSES:
    apply_pose(pose)
    pid = pose["id"]
    pts = POSE_POINTS.get(pid, np.zeros((0, 3)))

    def ov(im, pts=pts, pose=pose):
        for p in pts:
            x, y = st.project(p)
            pr.disk(im, x, y, 3.5, (1.0, 0.1, 0.1))
    s0 = STRESS[pid]["LOD0"]
    lab = "%s V%+.1f%% X%d S%.1f" % (pose["label"], s0["volume_change_pct"], s0["new_self_intersections"], s0["worst_edge_stretch"])
    if pose["view"] == "hand_left":
        c = pb["LeftHandEnd"].head if "LeftHandEnd" in pb else pb["LeftHand"].tail
        c = pb["LeftHand"].tail
        close_tile(tuple(c), "side", 0.30, [lod0], 'OBJECT', lab, ov)
    else:
        full_tile(pose["view"], [lod0], 'OBJECT', lab, ov)
# worst pose for LOD1 / LOD2
for li in (1, 2):
    worst = max(POSES, key=lambda p: STRESS[p["id"]]["LOD%d" % li]["worst_edge_stretch"] + 0.02 * STRESS[p["id"]]["LOD%d" % li]["new_self_intersections"])
    apply_pose(worst)
    s_ = STRESS[worst["id"]]["LOD%d" % li]
    lab = "LOD%d %s S%.1f X%d" % (li, worst["label"], s_["worst_edge_stretch"], s_["new_self_intersections"])
    if worst["view"] == "hand_left":
        close_tile(tuple(pb["LeftHand"].tail), "side", 0.30, [LODS[li]], 'OBJECT', lab)
    else:
        full_tile(worst["view"], [LODS[li]], 'OBJECT', lab)
reset_pose()

# close-ups on the rest pose, colored by strongest bone
bpy.context.scene.display.shading.light = 'FLAT'


def bone_ov(im):
    bone_overlay(im)


jp = {k: np.array(v["pos"]) for k, v in J["joints"].items()}
close_tile(jp["LeftHandEnd"], "top", 0.30, [wcopy], 'VERTEX', "HAND LEFT (TOP)", bone_ov)
close_tile(jp["RightHandEnd"], "top", 0.30, [wcopy], 'VERTEX', "HAND RIGHT (TOP)", bone_ov)
arm_c = jp["LeftUpperArm"] + np.array([0.0, 0.0, -0.10])
close_tile(arm_c, "front", 0.42, [wcopy], 'VERTEX', "ARMPIT LEFT", bone_ov)
close_tile(jp["LeftUpperArm"], "front", 0.40, [wcopy], 'VERTEX', "SHOULDER LEFT", bone_ov)
cz = J["analysis"]["crotch_z"]
close_tile(np.array([0.0, jp["Hips"][1], cz + 0.05]), "front", 0.50, [wcopy], 'VERTEX', "CROTCH", bone_ov)
close_tile(jp["LeftLowerLeg"], "side", 0.50, [wcopy], 'VERTEX', "KNEE LEFT", bone_ov)

# proof tile: body-skin faces on the rest pose, RED = removed from the game mesh (hidden under clothing), tan = kept
if RM.get("enabled") and "removed_body" in hi.data.attributes:
    hb = CFG["cleanup"]["hidden_body"]
    rm_me = hi.data.copy()
    bmr = bmesh.new()
    bmr.from_mesh(rm_me)
    lay = bmr.faces.layers.int.get("removed_body")
    mats_ = [m.name if m else "" for m in hi.data.materials]
    drop = [f for f in bmr.faces if not any(k in mats_[f.material_index] for k in hb["body_materials"])]
    bmr.faces.ensure_lookup_table()
    bmr.to_mesh(rm_me)
    bmesh.ops.delete(bmr, geom=drop, context='FACES')
    bmr.to_mesh(rm_me)
    bmr.free()
    rmo = bpy.data.objects.new("_removed_view", rm_me)
    bpy.context.scene.collection.objects.link(rmo)
    flag = np.zeros(len(rm_me.polygons), np.int32)
    rm_me.attributes["removed_body"].data.foreach_get("value", flag)
    cc = rm_me.color_attributes.new("RM", 'FLOAT_COLOR', 'CORNER')
    col = np.zeros((len(rm_me.loops), 4), np.float32)
    for poly in rm_me.polygons:
        col[poly.loop_start:poly.loop_start + poly.loop_total] = (0.95, 0.12, 0.1, 1) if flag[poly.index] else (0.85, 0.7, 0.55, 1)
    cc.data.foreach_set("color", col.ravel())
    rm_me.color_attributes.active_color = cc
    bpy.context.scene.display.shading.light = 'FLAT'
    for nm_, vw in (("FRONT", "front"), ("SIDE", "side")):
        full_tile(vw, [rmo], 'VERTEX', "REMOVED BODY %s %d FACES" % (nm_, RM.get("removed_faces", 0)))
    loc_, ortho_ = ((0.0, 7.0, 1.05), 2.7)
    tile(st.render(loc_, CAM_T, ortho_, 'VERTEX', [rmo]), "REMOVED BODY BACK")

COLS = 5
sheet = pr.assemble(tiles, COLS, labels, scale=2)
# header strip with the verdict
sheet_path = os.path.join(OUT, "sheet.png")

# ------------------------------------------------------------------ FBX reimport check
fbx = os.path.join(OUT, "export", NAME + ".fbx")
fbx_info = {"exists": os.path.exists(fbx)}
for o in list(bpy.data.objects):
    o.name = "_pre_" + o.name
before = set(bpy.data.objects)
if fbx_info["exists"]:
    try:
        bpy.ops.import_scene.fbx(filepath=fbx)
        new = [o for o in bpy.data.objects if o not in before]
        arms = [o for o in new if o.type == 'ARMATURE']
        meshes = [o for o in new if o.type == 'MESH']
        fb = {b.name for a in arms for b in a.data.bones}
        fbx_info["bone_count"] = len(fb)
        fbx_info["missing_bones"] = [b for b in sk.BONE_NAMES if b not in fb]
        fbx_info["extra_bones"] = sorted(fb - set(sk.BONE_NAMES))
        fbx_info["leaf_bones"] = [b for b in fb if b.endswith("_end") or b.endswith("_leaf")]
        fbx_info["mesh_names"] = sorted(o.name for o in meshes)
        mz = []
        for o in meshes:
            if o.name.endswith("LOD0"):
                bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
                mz = [min(v.z for v in bb), max(v.z for v in bb)]
        fbx_info["lod0_z_range_m"] = mz
        fbx_info["armature_scale"] = [round(x, 4) for x in arms[0].scale] if arms else None
        fbx_info["tris"] = {o.name: tris(o) for o in meshes}
        fbx_info["materials"] = sorted({m.name for o in meshes for m in o.data.materials if m})
    except Exception as e:  # noqa
        fbx_info["error"] = str(e)
ok_names = {NAME + "_LOD%d" % i for i in range(len(LODS))}
add("fbx.humanoid_bones_complete", "fbx", len(fbx_info.get("missing_bones", ["?"])), 0, "<=", "missing in re-imported FBX: %s" % fbx_info.get("missing_bones"))
add("fbx.no_extra_or_leaf_bones", "fbx", len(fbx_info.get("extra_bones", ["?"])) + len(fbx_info.get("leaf_bones", ["?"])), 0, "<=",
    "extra: %s leaf: %s" % (fbx_info.get("extra_bones"), fbx_info.get("leaf_bones")))
add("fbx.lod_mesh_names", "fbx", sorted(m for m in fbx_info.get("mesh_names", [])) == sorted(ok_names), True, "bool", "meshes: %s" % fbx_info.get("mesh_names"))
zr = fbx_info.get("lod0_z_range_m") or [0, 0]
add("fbx.height_m", "fbx", round(zr[1] - zr[0], 3), CFG["cleanup"]["height_range_m"], "range", "re-imported LOD0 height in meters (Unity scale 1 expected)")
CHECKS[-1]["pass"] = CFG["cleanup"]["height_range_m"][0] <= zr[1] - zr[0] <= CFG["cleanup"]["height_range_m"][1]
add("fbx.feet_on_ground_m", "fbx", round(zr[0], 4), 0.03, "<=", "lowest LOD0 vertex z")
CHECKS[-1]["pass"] = abs(zr[0]) <= 0.03

# ------------------------------------------------------------------ report
failed = [c for c in CHECKS if not c["pass"]]
worst_areas = []
for pid, res in STRESS.items():
    for lod, r in res.items():
        li_ = int(lod[3:])
        if r["worst_edge_stretch"] > lim(li_, "max_edge_stretch_ratio"):
            worst_areas.append({"kind": "edge_stretch", "pose": pid, "lod": lod, "region": r["worst_stretch_region"], "value": r["worst_edge_stretch"]})
        if r["new_self_intersections"] > lim(li_, "max_self_intersections_per_1k_tris") * r["tris"] / 1000.0:
            worst_areas.append({"kind": "self_intersection", "pose": pid, "lod": lod, "value": r["new_self_intersections"]})
        if not (-CC["max_volume_loss_pct"] <= r["volume_change_pct"] <= CC["max_volume_gain_pct"]):
            worst_areas.append({"kind": "volume", "pose": pid, "lod": lod, "value": r["volume_change_pct"]})
worst_areas.sort(key=lambda w: -abs(w["value"]))
n_pass = sum(1 for c in CHECKS if c["pass"])
report = {
    "character": NAME, "version": VERSION, "overall_pass": len(failed) == 0,
    "summary": {"checks_total": len(CHECKS), "checks_passed": n_pass, "checks_failed": len(failed),
                "failed_ids": [c["id"] for c in failed], "worst_areas": worst_areas[:12],
                "score": round(n_pass / len(CHECKS), 4)},
    "numbers": {"height_m": H, "lod_tris": {o.name.replace("_pre_", ""): tris(o) for o in LODS}, "topology": {"source": topo_hi, "game_lod0": topo_g, "proxy": topo_p},
                "fingers": finger_detail, "far_vertices": WEIGHT_INFO.get("far_detail"), "textures": imgs, "cleanup": CLJ, "fbx_reimport": fbx_info},
    "limits_applied_per_lod": LIMITS_APPLIED, "hidden_body_removal": removal_report,
    "checks": CHECKS, "stress": STRESS,
}
pc.write_json(os.path.join(OUT, "report.json"), report)

# header on the sheet
head = np.zeros((36, sheet.shape[1], 4), np.float32)
head[...] = (0.05, 0.05, 0.06, 1)
verdict = "PASS" if report["overall_pass"] else "FAIL"
pr.text(head, "%s %s %s  CHECKS %d/%d  FAILED: %s" % (NAME, VERSION, verdict, n_pass, len(CHECKS), " ".join(report["summary"]["failed_ids"][:4])),
        8, 8, scale=3, color=(0.4, 1, 0.5) if report["overall_pass"] else (1, 0.45, 0.4))
sheet = np.concatenate([head, sheet], axis=0)
pr.save_png(sheet, sheet_path)

prev = A["prev"]
if prev and os.path.exists(os.path.join(os.path.abspath(prev), "sheet.png")):
    a = pr.load_png(os.path.join(os.path.abspath(prev), "sheet.png"))
    b = sheet

    def half(x):
        h, w = (x.shape[0] // 2) * 2, (x.shape[1] // 2) * 2
        return x[:h, :w].reshape(h // 2, 2, w // 2, 2, 4).mean(axis=(1, 3))
    a2, b2 = half(a), half(b)
    hh = max(a2.shape[0], b2.shape[0])

    def padh(x):
        out = np.zeros((hh, x.shape[1], 4), np.float32)
        out[..., 3] = 1
        out[:x.shape[0]] = x
        return out
    comp = np.concatenate([padh(a2), padh(b2)], axis=1)
    pr.text(comp, "PREVIOUS: " + os.path.basename(os.path.abspath(prev)), 8, 24, scale=3, color=(1, 0.8, 0.3))
    pr.text(comp, "CURRENT: " + VERSION, a2.shape[1] + 8, 24, scale=3, color=(0.4, 1, 0.5))
    pr.save_png(comp, os.path.join(OUT, "sheet_compare.png"))

print("CHECK", VERSION, "pass" if report["overall_pass"] else "FAIL", "%d/%d" % (n_pass, len(CHECKS)))
for c in failed:
    print("  FAIL", c["id"], c["value"], c["op"], c["threshold"], "|", (c["detail"] or "")[:140])
print("sheet seconds", round(time.time() - t_sheet, 1))
print("STAGE5 DONE")
