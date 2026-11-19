"""Stage 1: mesh cleanup. Source mesh -> ground/scale/facing fix, voxel PROXY, decimated + UV'd + baked GAME mesh.

  blender -b --factory-startup --python cleanup.py -- --character testcourier --source <fbx|blend> --out work/testcourier/v001

Writes <out>/cleanup.blend (objects HI, PROXY, <name>_LOD0), <out>/cleanup.json,
<out>/textures/<name>_Albedo.png + <name>_Normal.png.
"""
import bpy, bmesh, os, sys, math
import numpy as np
from mathutils import Vector, Matrix
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pl_common as pc

A = pc.parse_args({"character": "testcourier", "source": "", "out": ""})
OUT = os.path.abspath(A["out"])
CFG = pc.load_config(OUT)
C = CFG["cleanup"]
NAME = A["character"]
os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)


def select_only(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o


def import_source(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    elif path.lower().endswith(".obj"):
        bpy.ops.wm.obj_import(filepath=path)
    else:
        with bpy.data.libraries.load(path) as (src, dst):
            dst.objects = src.objects
        for o in dst.objects:
            bpy.context.scene.collection.objects.link(o)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in list(bpy.context.scene.objects):
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    for o in meshes:
        o.parent = None
        for mod in list(o.modifiers):
            if mod.type == 'ARMATURE':
                o.modifiers.remove(mod)
        o.vertex_groups.clear()
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def fix_placement(ob):
    """Face -Y, feet at z=0, centered on x/y of the feet, real-world scale."""
    info = {}
    yaw = float(C["source_yaw_deg"])
    if yaw:
        ob.rotation_euler.z = math.radians(yaw)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    V, _ = pc.mesh_np(ob)
    H = V[:, 2].max() - V[:, 2].min()
    z0 = V[:, 2].min()
    if C["auto_face"]:
        feet = V[V[:, 2] < z0 + 0.04 * H]
        shin = V[(V[:, 2] > z0 + 0.20 * H) & (V[:, 2] < z0 + 0.30 * H)]
        sy = shin[:, 1].mean()
        toe_fwd = sy - feet[:, 1].min()
        toe_back = feet[:, 1].max() - sy
        info["facing_toe_minus_heel"] = [float(toe_fwd), float(toe_back)]
        if toe_back > toe_fwd:
            ob.rotation_euler.z = math.pi
            bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
            info["rotated_180"] = True
            V, _ = pc.mesh_np(ob)
    lo, hi = C["height_range_m"]
    scale = 1.0
    if not (lo <= H <= hi):
        scale = C["target_height_m"] / H
        ob.scale = (scale, scale, scale)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        V, _ = pc.mesh_np(ob)
        H = V[:, 2].max() - V[:, 2].min()
    info["scale_applied"] = scale
    z0 = V[:, 2].min()
    mid = V[(V[:, 2] > z0 + 0.28 * H) & (V[:, 2] < z0 + 0.32 * H)]
    feet = V[V[:, 2] < z0 + 0.04 * H]
    cx = float(0.5 * (mid[:, 0].min() + mid[:, 0].max()))
    cy = float(0.5 * (feet[:, 1].min() + feet[:, 1].max()))
    ob.location = (-cx, -cy, -z0)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    V, _ = pc.mesh_np(ob)
    info["height"] = float(V[:, 2].max())
    info["offset"] = [-cx, -cy, -z0]
    return info


def tri_total(ob):
    me = ob.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


PROXY_STATS = {}


def make_proxy(hi):
    px = hi.copy()
    px.data = hi.data.copy()
    px.name = "PROXY"
    bpy.context.scene.collection.objects.link(px)
    select_only(px)
    px.data.materials.clear()
    # The source is made of open shells (cuffs, neck, waist). Voxel remesh needs a closed volume, so give
    # the shells thickness INWARD first (normals point outward), which keeps the outer surface exact.
    sol = px.modifiers.new("solid", 'SOLIDIFY')
    sol.thickness = float(C["proxy_shell_thickness"])
    sol.offset = float(C["proxy_shell_offset"])
    sol.use_even_offset = False
    bpy.ops.object.modifier_apply(modifier=sol.name)
    m = px.modifiers.new("vox", 'REMESH')
    m.mode = 'VOXEL'
    m.voxel_size = float(C["proxy_voxel_size"])
    m.adaptivity = 0.0
    m.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=m.name)
    px.data.name = "PROXY"
    # thickened shells leave an inner cavity surface (negative signed volume): drop those pieces
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='LOOSE')
    bpy.ops.object.mode_set(mode='OBJECT')
    parts = [o for o in bpy.context.scene.objects if o.name.startswith("PROXY")]
    kept, dropped = [], 0
    for o in parts:
        V, T = pc.mesh_np(o)
        vol = float(np.einsum("ij,ij->i", V[T[:, 0]], np.cross(V[T[:, 1]], V[T[:, 2]])).sum() / 6.0)
        if vol < 0:
            dropped += 1
            bpy.data.objects.remove(o, do_unlink=True)
        else:
            kept.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in kept:
        o.select_set(True)
    bpy.context.view_layer.objects.active = kept[0]
    if len(kept) > 1:
        bpy.ops.object.join()
    px = bpy.context.view_layer.objects.active
    px.name = px.data.name = "PROXY"
    PROXY_STATS["parts_kept"] = len(kept)
    PROXY_STATS["cavity_parts_dropped"] = dropped
    PROXY_STATS["tris_before_decimate"] = tri_total(px)
    mx = int(C["proxy_max_tris"])
    if tri_total(px) > mx:
        mod = px.modifiers.new("dec", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = mx / tri_total(px)
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.shade_smooth()
    return px


REMOVAL = {"enabled": False}


def mark_hidden_body_faces(hi):
    """Hidden-body-face removal (config cleanup.hidden_body). Marks body-skin faces of HI that are fully covered by
    clothing in the int face attribute 'removed_body' (1 = remove from the game mesh).

    Rule: a face is BODY if its material name contains one of hidden_body.body_materials, GARMENT if it contains one
    of garment_materials (everything else: hair, eyes, teeth... is never touched and never counts as cover).
    A body face is COVERED when 1 + cone_rays rays from its centre (along the face normal and tilted by
    cone_angle_deg) ALL hit a garment face within max_cover_distance_m (the garment BVH is built from garment faces only).
    Covered faces are kept when they lie within opening_band_m of a garment OPENING (a garment boundary edge: cuff,
    collar, hem, ankle), measured from the face centre and from the normal ray's hit point, so nothing shows through
    when the cloth moves. The PROXY is built from the full HI before this runs, so skin weights still see a watertight body."""
    from mathutils.bvhtree import BVHTree
    from mathutils.kdtree import KDTree
    H = C.get("hidden_body", {})
    REMOVAL["enabled"] = bool(H.get("enabled", False))
    me = hi.data
    attr = me.attributes.get("removed_body") or me.attributes.new("removed_body", 'INT', 'FACE')
    if not REMOVAL["enabled"]:
        return
    mats = [m.name if m else "" for m in me.materials]
    is_body = [any(k in n for k in H["body_materials"]) for n in mats]
    is_garm = [any(k in n for k in H["garment_materials"]) for n in mats]
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    garm = [f for f in bm.faces if is_garm[f.material_index]]
    body = [f for f in bm.faces if is_body[f.material_index]]
    verts = [v.co.copy() for v in bm.verts]
    tris = []
    for f in garm:
        vs = [v.index for v in f.verts]
        for k in range(1, len(vs) - 1):
            tris.append((vs[0], vs[k], vs[k + 1]))
    bvh = BVHTree.FromPolygons(verts, tris, epsilon=0.0)
    # garment openings: boundary edges of garment faces, sampled every <= 1 cm
    pts = []
    gset = set(f.index for f in garm)
    for e in bm.edges:
        lf = [f for f in e.link_faces if f.index in gset]
        if len(lf) == 1 and len(e.link_faces) == 1:
            a, b = e.verts[0].co, e.verts[1].co
            n = max(1, int((a - b).length / 0.01))
            for k in range(n + 1):
                pts.append(a.lerp(b, k / n))
    kd = KDTree(len(pts))
    for i, p in enumerate(pts):
        kd.insert(p, i)
    kd.balance()
    maxd = float(H["max_cover_distance_m"])
    band = float(H["opening_band_m"])
    ang = math.radians(float(H["cone_angle_deg"]))
    nray = int(H["cone_rays"])
    removed, band_kept, uncovered, removed_tris = 0, 0, 0, 0
    area_removed, area_body = 0.0, 0.0
    layer = bm.faces.layers.int.get("removed_body") or bm.faces.layers.int.new("removed_body")
    for f in body:
        c = f.calc_center_median()
        n = f.normal.normalized()
        area_body += f.calc_area()
        t1 = n.cross(Vector((0, 0, 1)))
        if t1.length < 1e-3:
            t1 = n.cross(Vector((1, 0, 0)))
        t1.normalize()
        t2 = n.cross(t1)
        dirs = [n] + [(n * math.cos(ang) + (t1 * math.cos(2 * math.pi * k / nray) + t2 * math.sin(2 * math.pi * k / nray)) * math.sin(ang)).normalized()
                      for k in range(nray)]
        o = c + n * 1e-4
        hit0, ok = None, True
        for i, d in enumerate(dirs):
            loc, nrm, idx, dist = bvh.ray_cast(o, d, maxd)
            if loc is None:
                ok = False
                break
            if i == 0:
                hit0 = loc
        if not ok:
            uncovered += 1
            continue
        near = min(kd.find(c)[2], kd.find(hit0)[2]) if len(pts) else 1e9
        if near < band:
            band_kept += 1
            continue
        f[layer] = 1
        removed += 1
        removed_tris += len(f.verts) - 2
        area_removed += f.calc_area()
    bm.to_mesh(me)
    # boundary loops of the mesh once the covered faces are gone (the expected count for the game mesh)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[layer]], context='FACES')
    loops, bedges = pc.boundary_loops(bm)
    bm.free()
    REMOVAL.update({"body_faces": len(body), "garment_faces": len(garm), "removed_faces": removed, "removed_tris": removed_tris,
                    "kept_open_band_faces": band_kept, "kept_not_covered_faces": uncovered,
                    "removed_area_m2": round(area_removed, 4), "body_area_m2": round(area_body, 4),
                    "removed_area_fraction": round(area_removed / max(area_body, 1e-9), 4),
                    "boundary_loops_after_removal": loops, "garment_opening_samples": len(pts)})


def make_game(hi, target_tris):
    g = hi.copy()
    g.data = hi.data.copy()
    g.name = g.data.name = NAME + "_LOD0"
    bpy.context.scene.collection.objects.link(g)
    select_only(g)
    bm = bmesh.new()
    bm.from_mesh(g.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    lay = bm.faces.layers.int.get("removed_body")
    if lay is not None and REMOVAL.get("enabled"):
        gone = [f for f in bm.faces if f[lay]]
        bmesh.ops.delete(bm, geom=gone, context='FACES')
        REMOVAL["faces_deleted_from_game"] = len(gone)
    bm.to_mesh(g.data)
    bm.free()
    if "removed_body" in g.data.attributes:
        g.data.attributes.remove(g.data.attributes["removed_body"])
    bpy.ops.object.shade_smooth()
    cur = tri_total(g)
    mod = g.modifiers.new("dec", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = min(1.0, target_tris / cur)
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    cur2 = tri_total(g)
    if abs(cur2 - target_tris) > 0.02 * target_tris and cur2 > target_tris:
        mod = g.modifiers.new("dec2", 'DECIMATE')
        mod.decimate_type = 'COLLAPSE'
        mod.ratio = target_tris / cur2
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.delete_loose(use_verts=True, use_edges=True, use_faces=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    while g.data.uv_layers:
        g.data.uv_layers.remove(g.data.uv_layers[0])
    g.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=float(C["uv_island_margin"]),
                             area_weight=0.6, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=float(C["uv_island_margin"]), rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    return g


def bake(hi, game, size):
    scn = bpy.context.scene
    scn.render.engine = 'CYCLES'
    scn.cycles.device = 'CPU'
    scn.cycles.samples = int(C["bake_samples"])
    scn.cycles.use_denoising = False
    res = {}
    mat = bpy.data.materials.new(NAME + "_Body")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    game.data.materials.clear()
    game.data.materials.append(mat)
    for kind in ("Albedo", "Normal"):
        img = bpy.data.images.new("%s_%s" % (NAME, kind), size, size, alpha=False)
        img.colorspace_settings.name = 'sRGB' if kind == "Albedo" else 'Non-Color'
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        node.name = "bake_" + kind
        for n in nt.nodes:
            n.select = False
        node.select = True
        nt.nodes.active = node
        bpy.ops.object.select_all(action='DESELECT')
        hi.select_set(True)
        game.select_set(True)
        bpy.context.view_layer.objects.active = game
        kw = dict(use_selected_to_active=True, cage_extrusion=float(C["bake_cage_extrusion"]),
                  max_ray_distance=float(C["bake_max_ray_distance"]), margin=int(C["bake_margin_px"]),
                  margin_type='EXTEND', use_clear=True)
        if kind == "Albedo":
            bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, **kw)
        else:
            bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', **kw)
        path = os.path.join(OUT, "textures", "%s_%s.png" % (NAME, kind))
        img.filepath_raw = path
        img.file_format = 'PNG'
        img.save()
        res[kind] = path
    a = nt.nodes["bake_Albedo"]
    n = nt.nodes["bake_Normal"]
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(a.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(n.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.7
    scn.render.engine = 'BLENDER_WORKBENCH'
    return res


def main():
    hi = import_source(os.path.abspath(A["source"]))
    hi.name = hi.data.name = "HI"
    info = fix_placement(hi)
    info["source_tris"] = tri_total(hi)
    info["source_materials"] = len(hi.data.materials)
    proxy = make_proxy(hi)
    mark_hidden_body_faces(hi)
    hi_bm = bmesh.new()
    hi_bm.from_mesh(hi.data)
    REMOVAL["boundary_loops_source"] = pc.boundary_loops(hi_bm)[0]
    hi_bm.free()
    info["proxy_tris"] = tri_total(proxy)
    info["proxy"] = PROXY_STATS
    info["proxy_voxel_size"] = C["proxy_voxel_size"]
    game = make_game(hi, CFG["budget"]["lod0_tris"])
    info["game_tris_pre_bake"] = tri_total(game)
    info["hidden_body_removal"] = REMOVAL
    info["bake"] = bake(hi, game, CFG["budget"]["texture_size"])
    info["texture_size"] = CFG["budget"]["texture_size"]
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "cleanup.blend"))
    pc.write_json(os.path.join(OUT, "cleanup.json"), info)
    print("CLEANUP", info)


main()
print("STAGE1 DONE")
