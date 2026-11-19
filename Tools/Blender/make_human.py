"""Char_Human: the courier. One continuous skinned quad mesh in a T-pose matching bl_skeleton.

Pipeline (see the report in the task): skin-modifier cage + hand cages -> Catmull-Clark -> exact projection onto an analytic
clothed-body signed distance field -> head refinement + face sculpt, eye fissures with lids, ears -> clothing relief
(collar, hem, cuffs, folds) + stitched seam / zip / pocket / lace ribbons -> region materials -> analytic skin weights
-> UVs -> procedural textures -> FBX.

Run: blender --background --factory-startup --python make_human.py
"""
import sys, os, time, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import bl_skeleton
import hb_body, hb_face, hb_cloth, hb_weights, hb_uv, hb_tex

ROOT = r"E:\Unity\Zombies5.5"
OUT_FBX = os.path.join(ROOT, "Assets", "Art", "Models", "Characters", "Char_Human.fbx")
SLOTS = ["Skin", "EyeWhite", "Iris", "Brow", "Top", "Pants", "Shoe"]      # slot index == face tag
COLORS = {"Skin": (0.80, 0.60, 0.48), "EyeWhite": (0.92, 0.92, 0.90), "Iris": (0.45, 0.32, 0.18), "Brow": (0.09, 0.06, 0.04),
          "Top": (0.40, 0.36, 0.24), "Pants": (0.45, 0.55, 0.78), "Shoe": (0.80, 0.78, 0.74)}
SKIN_SLOT = 0


def cut_plane(bm, z, pred):
    """Bisect the faces accepted by pred(face centre) with the plane height z (gives a clean material boundary loop)."""
    faces = [f for f in bm.faces if pred(f.calc_center_median())]
    geom = set(faces)
    for f in faces:
        geom.update(f.edges); geom.update(f.verts)
    bmesh.ops.bisect_plane(bm, geom=list(geom), dist=1e-6, plane_co=(0, 0, z), plane_no=(0, 0, 1))


def cut_boundaries(bm):
    cut_plane(bm, hb_cloth.HEM_Z, lambda c: abs(c.x) < 0.25 and 0.9 < c.z < 1.12)
    cut_plane(bm, 1.533, lambda c: abs(c.x) < 0.11 and 1.47 < c.z < 1.60 and c.y > -0.075)
    for sx in (1, -1):
        cut_plane(bm, hb_cloth.JEAN_HEM, lambda c, sx=sx: abs(c.x - sx * 0.096) < 0.075 and c.y > -0.040 and 0.05 < c.z < 0.14)


def build_bmesh():
    t0 = time.time()
    bm, body = hb_body.base_bmesh()
    cut_boundaries(bm)
    hb_face.refine_head(bm, body)
    hb_face.refine_features(bm, body)
    hb_face.sculpt_face(bm)
    hb_face.wrap_eyes(bm)
    hb_face.ear_relief(bm)
    tag = bm.faces.layers.int.new('tag')
    for f in bm.faces:               # material regions are decided on the undisplaced mesh (clean boundary loops)
        f.material_index = int(hb_cloth.region_of(np.array([f.calc_center_median()[:]]))[0])
    hb_cloth.displace(bm)
    bm.normal_update()
    head_bvh = BVHTree.FromBMesh(bm)
    body_faces = set(bm.faces)
    hb_cloth.decorate(bm, tag)
    hb_face.add_eyeballs(bm, tag)
    hb_face.add_brows(bm, tag, head_bvh)
    bm.normal_update()
    # ribbon islands: make sure they face away from the body surface
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        if f[tag] in (3, 4, 5, 6) and f not in body_faces:
            loc, n, _, _ = head_bvh.find_nearest(f.calc_center_median())
            if n is not None and f.normal.dot(n) < 0:
                f.normal_flip()
    bm.normal_update()
    for f in bm.faces:
        if f[tag] != 0:
            f.material_index = f[tag]
    for v in bm.verts:
        if v.co.z < 0:
            v.co.z = 0.0
    print("[build_bmesh] %.1fs, tris %d" % (time.time() - t0, sum(len(f.verts) - 2 for f in bm.faces)))
    return bm


def make_materials(use_textures=True):
    mats = {}
    T = hb_tex.TEX_DIR
    for nm in SLOTS:
        m = bpy.data.materials.new(nm)
        m.use_nodes = True
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        col = COLORS[nm]
        m.diffuse_color = (*col, 1.0)
        b.inputs["Base Color"].default_value = (*col, 1.0)
        rough = {"Skin": 0.52, "EyeWhite": 0.15, "Iris": 0.12, "Brow": 0.7, "Top": 0.85, "Pants": 0.9, "Shoe": 0.7}[nm]
        b.inputs["Roughness"].default_value = rough
        if use_textures and nm in ("Skin", "Top", "Pants", "Shoe", "Iris"):
            tex = {"Skin": "Skin", "Top": "Cloth", "Pants": "Denim", "Shoe": "Leather", "Iris": "Eye"}[nm]
            ti = nt.nodes.new("ShaderNodeTexImage")
            p = os.path.join(T, tex + "_Albedo.png")
            if os.path.exists(p):
                ti.image = bpy.data.images.load(p)
                ti.image.colorspace_settings.name = 'sRGB'
                if nm == "Iris":
                    ti.extension = 'EXTEND'
                if nm in ("Top", "Pants", "Shoe"):
                    mix = nt.nodes.new("ShaderNodeMix")
                    mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
                    mix.inputs[0].default_value = 1.0
                    mix.inputs[7].default_value = (*col, 1.0)
                    nt.links.new(ti.outputs["Color"], mix.inputs[6])
                    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
                else:
                    nt.links.new(ti.outputs["Color"], b.inputs["Base Color"])
            pn = os.path.join(T, tex + "_Normal.png")
            if os.path.exists(pn) and nm != "Iris":
                tn = nt.nodes.new("ShaderNodeTexImage")
                tn.image = bpy.data.images.load(pn)
                tn.image.colorspace_settings.name = 'Non-Color'
                nm_node = nt.nodes.new("ShaderNodeNormalMap")
                nm_node.inputs["Strength"].default_value = 1.0
                nt.links.new(tn.outputs["Color"], nm_node.inputs["Color"])
                nt.links.new(nm_node.outputs["Normal"], b.inputs["Normal"])
        if nm == "Skin":
            try:
                b.inputs["Subsurface Weight"].default_value = 0.12
                b.inputs["Subsurface Radius"].default_value = (0.9, 0.35, 0.2)
                b.inputs["Subsurface Scale"].default_value = 0.01
            except Exception:
                pass
        mats[nm] = m
    return mats


def apply_weights(obj, rig):
    me = obj.data
    bones = {b.name for b in rig.data.bones}
    groups = {}
    for v in me.vertices:
        w = hb_weights.weights_at(v.co)
        for k, val in w.items():
            if k not in bones:
                print("WARN unknown bone", k)
                continue
            groups.setdefault(k, []).append((v.index, val))
    for name in [b[0] for b in bl_skeleton.bone_table()]:
        vg = obj.vertex_groups.new(name=name)
        for i, val in groups.get(name, []):
            vg.add([i], val, 'REPLACE')
    return groups


def build_character(bake_textures=True, export=True, name="Char_Human"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bm = build_bmesh()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.attributes.remove(me.attributes["tag"]) if "tag" in me.attributes else None
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for p in me.polygons:
        p.use_smooth = True
    # UVs (material_index already holds the slot number)
    hb_uv.unwrap_skin(obj, SKIN_SLOT)

    def iris_info(c):
        return Vector((np.sign(c.x) * hb_face.EYE_X, hb_face.EYE_CY, hb_face.EYE_Z))
    hb_uv.box_uvs(obj, SLOTS, iris_info)
    print("[uv] skin island bounds", hb_uv.report(obj, SKIN_SLOT))
    if bake_textures:
        t0 = time.time()
        me.update()
        hb_tex.make_cloth(); hb_tex.make_denim(); hb_tex.make_leather(); hb_tex.make_eye()
        hb_tex.bake_skin(me, me.uv_layers.active, SKIN_SLOT, 2048)
        print("[tex] %.1fs" % (time.time() - t0))
    mats = make_materials(use_textures=True)
    for nm in SLOTS:
        me.materials.append(mats[nm])
    # armature + weights
    rig = bl_skeleton.build_armature("CharacterRig")
    apply_weights(obj, rig)
    obj.parent = rig
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    bpy.context.view_layer.update()
    if export:
        export_fbx(obj, rig)
    return obj, rig


def export_fbx(obj, rig, path=OUT_FBX):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(False)
    obj.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_unit_scale=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False, object_types={'MESH', 'ARMATURE'},
                             use_armature_deform_only=True)
    print("[export]", path)


def stats(obj):
    me = obj.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    mx = max(len(v.groups) for v in me.vertices)
    unw = sum(1 for v in me.vertices if len(v.groups) == 0)
    quads = sum(1 for p in me.polygons if len(p.vertices) == 4)
    ngon = sum(1 for p in me.polygons if len(p.vertices) > 4)
    trif = sum(1 for p in me.polygons if len(p.vertices) == 3)
    print("[stats] verts %d faces %d (quads %d, tris %d, ngons %d) triangulated %d max-influences %d unweighted %d" %
          (len(me.vertices), len(me.polygons), quads, trif, ngon, tris, mx, unw))
    zs = [v.co.z for v in me.vertices]
    print("[stats] height %.3f..%.3f" % (min(zs), max(zs)))
    # watertight check on non-island body (boundary edges)
    bm = bmesh.new(); bm.from_mesh(me)
    b = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary)
    bm.free()
    print("[stats] boundary edges %d, non-manifold %d" % (b, nm))
    return tris


if __name__ == "__main__":
    ob, rig = build_character()
    stats(ob)
