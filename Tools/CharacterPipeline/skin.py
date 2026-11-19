"""Stage 4: skin weights + LODs + FBX export.

  blender -b --factory-startup --python skin.py -- --character testcourier --out work/testcourier/v001

1. PROXY is parented to the armature with Automatic Weights (watertight, so bone heat works).
2. Weights are copied to the game mesh (Data Transfer, vertex groups, nearest face interpolated) and applied.
3. 4 influences max per vertex, normalized. Optional smoothing of listed groups (config skin.smooth_groups).
4. LOD1 / LOD2 = Decimate on the skinned LOD0 (vertex groups kept), re-limited and re-normalized.
5. FBX for Unity: <out>/export/<name>.fbx + textures (deform bones only, no leaf bones, transforms applied).
Writes <out>/skin.blend, <out>/skin.json.
"""
import bpy, os, sys, shutil, time
import numpy as np
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pl_common as pc
import bl_skeleton as sk

A = pc.parse_args({"character": "testcourier", "out": ""})
OUT = os.path.abspath(A["out"])
NAME = A["character"]
CFG = pc.load_config(OUT)
SC = CFG["skin"]
pc.open_blend(os.path.join(OUT, "skeleton.blend"))
arm = bpy.data.objects["Armature"]
proxy = bpy.data.objects["PROXY"]
hi = bpy.data.objects["HI"]
lod0 = bpy.data.objects[NAME + "_LOD0"]
info = {}


def select_only(*objs, active=None):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objs[-1]


def tri_total(ob):
    ob.data.calc_loop_triangles()
    return len(ob.data.loop_triangles)


def repair_far_weights(ob):
    """Vertices whose strongest bone is farther than 0.85 * (far_bone_fraction * height) get envelope-style
    weights instead: inverse-square distance to their 3 nearest bone segments (the heat solve put them on a bone
    on the far side of the body, e.g. jacket-hem vertices on Spine)."""
    names = {vg.index: vg.name for vg in ob.vertex_groups}
    bn = [b.name for b in arm.data.bones]
    HA = np.array([b.head_local[:] for b in arm.data.bones])
    TA = np.array([b.tail_local[:] for b in arm.data.bones])
    P = np.array([v.co[:] for v in ob.data.vertices])
    lim = 0.85 * CFG["check"]["far_bone_fraction_of_height"] * P[:, 2].max()
    fixed = 0
    for v in ob.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda x: x.weight)
        p = P[v.index]
        ds = []
        for i in range(len(bn)):
            ab = TA[i] - HA[i]
            t = np.clip((p - HA[i]) @ ab / max(float(ab @ ab), 1e-12), 0, 1)
            ds.append(np.linalg.norm(p - (HA[i] + ab * t)))
        ds = np.array(ds)
        if ds[bn.index(names[g.group])] <= lim:
            continue
        near = np.argsort(ds)[:3]
        w = 1.0 / np.maximum(ds[near], 1e-3) ** 2
        w /= w.sum()
        for vg in ob.vertex_groups:
            try:
                vg.remove([v.index])
            except RuntimeError:
                pass
        for i, wi in zip(near, w):
            ob.vertex_groups[bn[i]].add([v.index], float(wi), 'REPLACE')
        fixed += 1
    return fixed


def hem_fix(ob, cfg):
    """Local correction after smoothing. Vertices whose strongest bone is farther than the far-bone limit mark a
    problem spot (jacket-hem verts that heat weighting tied to Spine). Within cfg.radius_m of those vertices the
    weight on cfg.from_bone is moved to cfg.to_bone, fading out linearly with distance, so neighbours stay consistent
    (v011 changed the two vertices alone and the squat/twist stretched)."""
    bn = [b.name for b in arm.data.bones]
    HA = np.array([b.head_local[:] for b in arm.data.bones])
    TA = np.array([b.tail_local[:] for b in arm.data.bones])
    names = {vg.index: vg.name for vg in ob.vertex_groups}
    P = np.array([v.co[:] for v in ob.data.vertices])
    lim = CFG["check"]["far_bone_fraction_of_height"] * P[:, 2].max()
    seeds = []
    for v in ob.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda x: x.weight)
        i = bn.index(names[g.group])
        ab = TA[i] - HA[i]
        t = np.clip((P[v.index] - HA[i]) @ ab / max(float(ab @ ab), 1e-12), 0, 1)
        if np.linalg.norm(P[v.index] - (HA[i] + ab * t)) > lim:
            seeds.append(v.index)
    if not seeds:
        return 0
    r = float(cfg["radius_m"])
    d = np.min(np.linalg.norm(P[:, None, :] - P[seeds][None], axis=2), axis=1)
    src, dst = ob.vertex_groups[cfg["from_bone"]], ob.vertex_groups[cfg["to_bone"]]
    moved = 0
    for vi in np.nonzero(d < r)[0].tolist():
        try:
            w = src.weight(vi)
        except RuntimeError:
            continue
        k = float(cfg["fraction"]) * (1.0 - d[vi] / r)
        mv = w * k
        src.add([vi], w - mv, 'REPLACE')
        dst.add([vi], mv, 'ADD')
        moved += 1
    return moved


def smooth_weights(ob, patterns, passes, factor, mask=None):
    """Laplacian smoothing of the listed groups (substring match) over the mesh edges. The total weight held by
    the smoothed groups at each vertex is preserved, so all other groups (fingers!) are untouched."""
    me = ob.data
    n = len(me.vertices)
    sel = [vg for vg in ob.vertex_groups if any(p in vg.name for p in patterns)]
    idx = {vg.index: k for k, vg in enumerate(sel)}
    Wm = np.zeros((n, len(sel)))
    for v in me.vertices:
        for g in v.groups:
            k = idx.get(g.group)
            if k is not None:
                Wm[v.index, k] = g.weight
    ed = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ed)
    ed = ed.reshape(-1, 2)
    deg = np.bincount(ed.ravel(), minlength=n).astype(float)
    mass0 = Wm.sum(axis=1)
    W2 = Wm.copy()
    for _ in range(passes):
        acc = np.zeros_like(W2)
        np.add.at(acc, ed[:, 0], W2[ed[:, 1]])
        np.add.at(acc, ed[:, 1], W2[ed[:, 0]])
        avg = acc / np.maximum(deg, 1)[:, None]
        upd = (deg > 0) if mask is None else ((deg > 0) & mask)
        W2 = np.where(upd[:, None], (1 - factor) * W2 + factor * avg, W2)
    m1 = W2.sum(axis=1)
    W2 *= (mass0 / np.maximum(m1, 1e-9))[:, None]
    changed = np.nonzero(np.abs(W2 - Wm).max(axis=1) > 1e-5)[0]
    for vi in changed.tolist():
        for k, vg in enumerate(sel):
            w = float(W2[vi, k])
            if w > 1e-4:
                vg.add([vi], w, 'REPLACE')
            elif Wm[vi, k] > 0:
                vg.remove([vi])


def hand_cleanup(ob):
    """Finger-bone weight on the wrist/palm base (closer to the wrist than skin.hand_cleanup_fraction of the hand
    length) is moved to the Hand bone, so the fingers' proximal bones cannot drag the wrist or cuff."""
    J = pc.read_json(os.path.join(OUT, "joints.json"))["hand_params"]
    me = ob.data
    P = np.array([v.co[:] for v in me.vertices])
    fingers = ("Thumb", "Index", "Middle", "Ring", "Little")
    moved = 0
    for side in ("Left", "Right"):
        hp = J[side]
        W, ax = np.array(hp["wrist"]), np.array(hp["axis"])
        zone = np.nonzero(((P - W) @ ax < SC["hand_cleanup_fraction"] * hp["hand_len"]) &
                          (np.linalg.norm(P - W, axis=1) < 0.35))[0]
        gidx = {vg.index: vg for vg in ob.vertex_groups}
        hand = ob.vertex_groups[side + "Hand"]
        fg = {vg.index for vg in ob.vertex_groups if vg.name.startswith(side) and any(f in vg.name for f in fingers)}
        for vi in zone.tolist():
            acc = 0.0
            for g in me.vertices[vi].groups:
                if g.group in fg:
                    acc += g.weight
            if acc > 0:
                for gi in fg:
                    try:
                        gidx[gi].remove([vi])
                    except RuntimeError:
                        pass
                hand.add([vi], acc, 'ADD')
                moved += 1
    return moved


def finish_weights(ob, is_lod=False):
    # LODs inherit LOD0's already cleaned/smoothed weights through the Decimate; skin.lods_skip_resmooth skips a second pass
    skip = is_lod and SC.get("lods_skip_resmooth")
    select_only(ob)
    bpy.ops.object.vertex_group_clean(group_select_mode='ALL', limit=float(SC["clean_below"]), keep_single=True)
    if SC.get("hand_cleanup") and not skip:
        info.setdefault("hand_cleanup_verts", {})[ob.name] = hand_cleanup(ob)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=int(SC["max_influences"]))
    if SC.get("repair_far_weights"):
        info.setdefault("far_repaired", {})[ob.name] = repair_far_weights(ob)
    if SC["smooth_passes"] and SC["smooth_groups"] and not skip:
        smooth_weights(ob, SC["smooth_groups"], int(SC["smooth_passes"]), float(SC["smooth_factor"]))
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=int(SC["max_influences"]))
    if SC.get("local_smooth") and not skip:
        ls = SC["local_smooth"]
        P_ = np.array([v.co[:] for v in ob.data.vertices])
        mask = np.zeros(len(P_), bool)
        for side in ("Left", "Right", ""):
            bone = arm.data.bones.get(side + ls["center_bone"])
            if bone is None:
                continue
            c = np.array(bone.head_local[:])
            mask |= np.linalg.norm(P_ - c, axis=1) < float(ls["radius_m"])
        smooth_weights(ob, SC["smooth_groups"], int(ls["passes"]), float(SC["smooth_factor"]), mask)
        bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=int(SC["max_influences"]))
        info.setdefault("local_smooth_verts", {})[ob.name] = int(mask.sum())
    if SC.get("hem_fix") and not skip:
        info.setdefault("hem_fixed", {})[ob.name] = hem_fix(ob, SC["hem_fix"])
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)


# ---- 1. automatic weights on the proxy
t0 = time.time()
select_only(proxy, arm, active=arm)
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
info["auto_weights_seconds"] = round(time.time() - t0, 1)
bones = [b.name for b in arm.data.bones]
info["proxy_groups"] = len(proxy.vertex_groups)
info["proxy_groups_missing"] = [b for b in bones if b not in proxy.vertex_groups]
if len(proxy.vertex_groups) == 0:
    raise RuntimeError("automatic weights produced no vertex groups")

# ---- 2. transfer to the game mesh
lod0.vertex_groups.clear()
for b in bones:
    lod0.vertex_groups.new(name=b)
select_only(lod0)
dt = lod0.modifiers.new("WeightTransfer", 'DATA_TRANSFER')
dt.object = proxy
dt.use_vert_data = True
dt.data_types_verts = {'VGROUP_WEIGHTS'}
dt.vert_mapping = 'POLYINTERP_NEAREST'
dt.layers_vgroup_select_src = 'ALL'
dt.layers_vgroup_select_dst = 'NAME'
bpy.ops.object.modifier_apply(modifier=dt.name)
finish_weights(lod0)

# ---- armature modifier + parenting
for o in (lod0,):
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    mod = o.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    o.parent = arm
    o.matrix_parent_inverse = arm.matrix_world.inverted()

# ---- 4. LODs
ratios = CFG["lod_ratios"]
lods = [lod0]
for i, r in enumerate(ratios[1:], start=1):
    o = lod0.copy()
    o.data = lod0.data.copy()
    o.name = o.data.name = "%s_LOD%d" % (NAME, i)
    bpy.context.scene.collection.objects.link(o)
    select_only(o)
    # decimate before the armature modifier is evaluated
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    mod = o.modifiers.new("Decimate", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = float(r)
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    finish_weights(o, True)
    am = o.modifiers.new("Armature", 'ARMATURE')
    am.object = arm
    o.parent = arm
    o.matrix_parent_inverse = arm.matrix_world.inverted()
    lods.append(o)
info["lod_tris"] = {o.name: tri_total(o) for o in lods}

# rest pose, hide helpers
arm.data.pose_position = 'REST'
for o in (proxy, hi):
    o.hide_set(True)
    o.hide_render = True

# ---- 5. FBX
exp = os.path.join(OUT, "export")
os.makedirs(exp, exist_ok=True)
tex_src = os.path.join(OUT, "textures")
for f in os.listdir(tex_src):
    shutil.copy2(os.path.join(tex_src, f), os.path.join(exp, f))
fbx = os.path.join(exp, NAME + ".fbx")
select_only(arm, *lods, active=arm)
arm.select_set(True)
for o in lods:
    o.select_set(True)
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False,
    use_armature_deform_only=True, object_types={'MESH', 'ARMATURE'}, path_mode='STRIP', bake_anim=False,
    use_mesh_modifiers=True, primary_bone_axis='Y', secondary_bone_axis='X')
info["fbx"] = fbx
info["fbx_bytes"] = os.path.getsize(fbx)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "skin.blend"))
pc.write_json(os.path.join(OUT, "skin.json"), info)
print("SKIN", info)
print("STAGE4 DONE")
