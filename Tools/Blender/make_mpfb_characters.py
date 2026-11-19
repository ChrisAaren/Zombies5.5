"""Zombies 5.5: MPFB (MakeHuman) characters, the Courier (hero) and Hollow (enemy base).

Run (do NOT use --factory-startup, the MPFB extension would not load):
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" --background --python Tools/Blender/make_mpfb_characters.py
Optional args after "--": courier | hollow | all (default all), noexport, nopreview.

Writes:
  Assets/Art/Models/Characters/MPFB_Courier.fbx, MPFB_Hollow.fbx
  Assets/Art/Textures/Characters/MPFB/*.png
  Tools/Blender/previews/MPFB_*.png
All source assets are CC0 MakeHuman system assets (see MPFB_CREDITS.txt).
"""
import bpy, bmesh, os, sys, math, json, traceback
import numpy as np
from mathutils import Vector, Matrix, Quaternion

from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.locationservice import LocationService
from bl_ext.blender_org.mpfb.services.targetservice import TargetService

ROOT = r"E:\Unity\Zombies5.5"
FBX_DIR = os.path.join(ROOT, "Assets", "Art", "Models", "Characters")
TEX_DIR = os.path.join(ROOT, "Assets", "Art", "Textures", "Characters", "MPFB")
PREV_DIR = os.path.join(ROOT, "Tools", "Blender", "previews")
for d in (FBX_DIR, TEX_DIR, PREV_DIR):
    os.makedirs(d, exist_ok=True)

DATA = LocationService.get_user_data("")


def D(*parts):
    return os.path.join(DATA, *parts)


# ----------------------------------------------------------------------------- specs
def srgb(hexs):
    h = hexs.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


COURIER = dict(
    name="Courier",
    macro=dict(gender=1.0, age=0.54, muscle=0.62, weight=0.52, proportions=0.50, height=0.5,
               cupsize=0.5, firmness=0.5, race=dict(asian=0.0, caucasian=1.0, african=0.0)),
    target_height=1.80,
    # (target name, value). "-incr/-decr" names carry the sign already; values are 0..1
    face=[
        # round 12: a grounded ~30 year old, wider and shorter through the face (round 11 read long and narrow)
        # skull: wider, a touch shorter, squarer
        ("head-scale-horiz-incr", 0.30), ("head-scale-vert-decr", 0.22), ("head-square", 0.25), ("head-fat-incr", 0.0),
        # jaw and chin: wide, square, a little shorter, jaw closed
        ("chin-width-incr", 0.45), ("chin-bones-incr", 0.45), ("chin-prominent-incr", 0.15),
        ("chin-cleft-incr", 0.12), ("chin-height-decr", 0.20), ("chin-jaw-drop-decr", 0.45),
        # cheeks: fuller, not hollow
        ("l-cheek-bones-incr", 0.30), ("r-cheek-bones-incr", 0.30),
        ("l-cheek-volume-incr", 0.10), ("r-cheek-volume-incr", 0.10),
        ("l-cheek-inner-incr", 0.10), ("r-cheek-inner-incr", 0.10),
        # nose: shorter, a little broader, soft bump
        ("nose-scale-vert-decr", 0.35), ("nose-width1-incr", 0.15), ("nose-width3-incr", 0.10),
        ("nose-hump-incr", 0.20), ("nose-scale-depth-decr", 0.10), ("nose-point-down", 0.05),
        # brow ridge and forehead: fuller brow, shorter forehead, heavier brows
        ("forehead-nubian-incr", 0.40), ("forehead-scale-vert-decr", 0.25), ("eyebrows-trans-down", 0.25),
        ("eyebrows-angle-down", 0.08), ("forehead-temple-incr", 0.10), ("forehead-trans-forward", 0.10),
        # eyes: a little narrower and hooded, tired
        ("l-eye-scale-decr", 0.10), ("r-eye-scale-decr", 0.10),
        ("l-eye-eyefold-down", 0.12), ("r-eye-eyefold-down", 0.12),
        ("l-eye-bag-incr", 0.15), ("r-eye-bag-incr", 0.15),
        # mouth: closed and relaxed, shorter mid-face (mouth sits higher), flat corners
        ("mouth-scale-vert-decr", 0.35), ("mouth-trans-up", 0.30), ("mouth-scale-horiz-incr", 0.10),
        ("mouth-lowerlip-volume-incr", 0.05), ("mouth-upperlip-height-decr", 0.20), ("mouth-lowerlip-height-decr", 0.15),
        ("mouth-lowerlip-middle-up", 0.20),
        # ears and neck: thicker neck
        ("l-ear-scale-incr", 0.10), ("r-ear-scale-incr", 0.05), ("l-ear-wing-incr", 0.25),
        ("neck-scale-horiz-incr", 0.30),
        # slight asymmetry so the face reads as a real person
        ("asym-jaw-1-l", 0.35), ("asym-nose-1-r", 0.30), ("asym-eye-2-l", 0.25),
        ("asym-brown-1-r", 0.30), ("asym-mouth-1-l", 0.20), ("asym-cheek-1-r", 0.20),
        ("asym-ear-1-l", 0.30),
    ],
    skin=("young_caucasian_male", "young_lightskinned_male_diffuse.png"),
    skin_grade=dict(mul=(0.93, 0.84, 0.76), sat=1.1, gain=1.0),
    eyes=("brownlight", "brownlight_eye.png"),
    body_proxy=None,
    brows=("eyebrow001", "eyebrow001.png"),
    lashes=("eyelashes02", "eyelashes02.png"),
    hair=("short02", "short02_diffuse.png"),
    hair_tint=dict(mul=(0.62, 0.42, 0.30), gain=0.95),
    suit=("male_casualsuit05", "male_casualsuit05"),
    top_tint=srgb("#0a4749"),     # teal jacket
    pants_tint=srgb("#23262e"),   # dark jeans
    shoes=("shoes06", "shoes06_diffuse.png"),
    shoe_tint=dict(mul=(0.30, 0.31, 0.34), gain=1.0),
    teeth=("teeth_base", "teeth.png"),
    tongue=True,
    delete_under_clothes=True,
)

HOLLOW = dict(
    name="Hollow",
    macro=dict(gender=1.0, age=0.60, muscle=0.30, weight=0.14, proportions=0.45, height=0.5,
               cupsize=0.5, firmness=0.5, race=dict(asian=0.0, caucasian=1.0, african=0.0)),
    target_height=1.78,
    face=[
        ("head-oval", 0.30), ("chin-prominent-incr", 0.25), ("chin-width-decr", 0.25),
        ("l-cheek-volume-decr", 0.80), ("r-cheek-volume-decr", 0.80),
        ("l-cheek-bones-incr", 0.50), ("r-cheek-bones-incr", 0.50),
        ("l-cheek-inner-decr", 0.40), ("r-cheek-inner-decr", 0.40),
        ("nose-scale-vert-incr", 0.30), ("nose-width1-decr", 0.30), ("nose-hump-incr", 0.30),
        ("eyebrows-trans-down", 0.45), ("forehead-temple-decr", 0.50),
        ("l-eye-bag-incr", 0.80), ("r-eye-bag-incr", 0.80),
        ("l-eye-push1-in", 0.40), ("r-eye-push1-in", 0.40),
        ("l-eye-scale-incr", 0.15), ("r-eye-scale-incr", 0.15),
        ("mouth-scale-horiz-incr", 0.25), ("mouth-angles-down", 0.40),
        ("mouth-upperlip-volume-decr", 0.40), ("mouth-lowerlip-volume-decr", 0.30),
        ("l-ear-scale-incr", 0.25), ("r-ear-scale-incr", 0.25),
        ("neck-scale-horiz-decr", 0.30),
        ("asym-jaw-2-r", 0.50), ("asym-eye-3-l", 0.40), ("asym-mouth-2-r", 0.40),
    ],
    skin=("middleage_caucasian_male", "middleage_lightskinned_male_diffuse.png"),
    skin_grade=dict(mul=(0.66, 0.72, 0.68), sat=0.45, gain=0.85),
    eyes=("grey", "grey_eye.png"),
    eyes_asset="low-poly",
    body_proxy="male1591",
    brows=("eyebrow007", "eyebrow007.png"),
    lashes=None,
    hair=("short04", "short04_diffuse.png"),
    hair_tint=dict(mul=(0.55, 0.55, 0.55), gain=0.9),
    suit=("male_casualsuit03", "male_casualsuit03"),
    flatten=0.75,
    top_tint=srgb("#34332f"),
    pants_tint=srgb("#1c1d21"),
    shoes=None,
    teeth=None,
    tongue=False,
    delete_under_clothes=True,
)

MAT_PREFIX = {}


# ----------------------------------------------------------------------------- image helpers
def load_np(path):
    im = bpy.data.images.load(path, check_existing=False)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)


def save_np(arr, name):
    h, w = arr.shape[:2]
    out = os.path.join(TEX_DIR, name)
    im = bpy.data.images.new("tmp_" + name, w, h, alpha=True)
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = out
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)
    return out


def luma(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def grade(a, mul=(1, 1, 1), sat=1.0, gain=1.0):
    o = a.copy()
    l = luma(a)[..., None]
    rgb = l + (a[..., :3] - l) * sat
    o[..., :3] = rgb * np.array(mul, np.float32) * gain
    return o


def colorize(a, color, mix=0.92, ref=None, flatten=0.0):
    """Replace the hue of a diffuse map with `color` but keep its luminance detail (folds, seams, stitches)."""
    l = luma(a)
    if ref is None:
        m = a[..., 3] > 0.5
        ref = float(np.mean(l[m])) if m.any() else 0.4
    k = np.clip(l / max(ref, 1e-3), 0, 1.7)[..., None]
    k = 1.0 + (k - 1.0) * (1.0 - flatten)
    tinted = np.array(color, np.float32)[None, None, :] * k
    o = a.copy()
    o[..., :3] = a[..., :3] * (1 - mix) + tinted * mix
    return o


def downscale(a, size):
    h, w = a.shape[:2]
    if w <= size:
        return a
    f = w // size
    return a[:h // f * f, :w // f * f].reshape(size, f, size, f, 4).mean(axis=(1, 3))


# ----------------------------------------------------------------------------- materials
def new_material(name, base_png, normal_png=None, alpha=None, rough=0.6, spec=0.3, ao_png=None):
    """Plain image-texture Principled material: exactly what Unity URP Lit will rebuild."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = rough
    for key in ("Specular IOR Level", "Specular"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = spec
            break
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(base_png)
    tex.image.colorspace_settings.name = 'sRGB'
    tex.image.alpha_mode = 'STRAIGHT'
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if alpha:
        nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
        mat.surface_render_method = 'DITHERED'
        mat.use_backface_culling = False
    if normal_png:
        nim = nt.nodes.new("ShaderNodeTexImage")
        nim.image = bpy.data.images.load(normal_png)
        nim.image.colorspace_settings.name = 'Non-Color'
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = 0.8
        nt.links.new(nim.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    mat.diffuse_color = (0.6, 0.6, 0.6, 1)
    return mat


def set_single_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.material_index = 0


# ----------------------------------------------------------------------------- build
def measure_height(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    zs = [v.co.z for v in ev.data.vertices]
    return max(zs) - min(zs)


def find_object_by_suffix(parent, suffix):
    for o in bpy.data.objects:
        if o.name.endswith(suffix) and o.type == 'MESH':
            return o
    return None


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.armatures, bpy.data.cameras, bpy.data.lights):
        for x in list(coll):
            coll.remove(x)


def make_human(spec):
    macro = {k: v for k, v in spec["macro"].items()}
    base = HumanService.create_human(macro_detail_dict=macro)
    # fit the height macro to the target body height
    from bl_ext.blender_org.mpfb.entities.objectproperties import HumanObjectProperties
    lo, hi = 0.0, 1.0
    for _ in range(9):
        mid = 0.5 * (lo + hi)
        HumanObjectProperties.set_value("height", mid, entity_reference=base)
        TargetService.reapply_macro_details(base)
        h = measure_height_body(base)
        if h < spec["target_height"]:
            lo = mid
        else:
            hi = mid
    spec["height_macro"] = round(0.5 * (lo + hi), 3)
    HumanObjectProperties.set_value("height", 0.5 * (lo + hi), entity_reference=base)
    TargetService.reapply_macro_details(base)
    return base


def measure_height_body(base):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = base.evaluated_get(dg)
    body = base.vertex_groups["body"].index
    zs = []
    for v, ov in zip(ev.data.vertices, base.data.vertices):
        if any(g.group == body and g.weight > 0.5 for g in ov.groups):
            zs.append(v.co.z)
    return max(zs) - min(zs)


def add_face_targets(base, face):
    stack = [dict(target=n, value=v) for n, v in face]
    missing = [n for n, _ in face if not TargetService.target_full_path(n)]
    if missing:
        print("WARN missing targets", missing)
    TargetService.bulk_load_targets(base, [s for s in stack if s["target"] not in missing])


def add_asset(base, path, atype):
    o = HumanService.add_mhclo_asset(path, base, asset_type=atype, subdiv_levels=0, material_type="NONE")
    return o


def delete_verts_by_groups(obj, prefix, thr=0.5):
    groups = [g.index for g in obj.vertex_groups if g.name.startswith(prefix)]
    if not groups:
        return 0
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    dl = bm.verts.layers.deform.verify()
    kill = [v for v in bm.verts if any(v[dl].get(g, 0.0) > thr for g in groups)]
    n = len(kill)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    return n


def keep_only_group(obj, gname):
    gi = obj.vertex_groups[gname].index
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    dl = bm.verts.layers.deform.verify()
    kill = [v for v in bm.verts if v[dl].get(gi, 0.0) <= 0.0]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


def select_only(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


# ----------------------------------------------------------------------------- T-pose
def _set_dir(rig, pb, target_dir):
    """Rotate the pose bone about its head so its +Y axis points along target_dir (world/armature space)."""
    bpy.context.view_layer.update()
    m = pb.matrix.copy()
    cur = m.to_3x3() @ Vector((0, 1, 0))
    d = cur.rotation_difference(Vector(target_dir).normalized())
    m3 = d.to_matrix() @ m.to_3x3()
    nm = m3.to_4x4()
    nm.translation = m.translation
    pb.matrix = nm
    bpy.context.view_layer.update()


def pose_t(rig):
    """Arms horizontal along +/-X, forearms/hands/fingers straight. Thumbs angle forward."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    pb = rig.pose.bones
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        p = "mixamorig:" + side
        _set_dir(rig, pb[p + "Shoulder"], (sx, 0.0, 0.12))
        for n in ("Arm", "ForeArm", "Hand"):
            _set_dir(rig, pb[p + n], (sx, 0.0, 0.0))
        for f in ("Index", "Middle", "Ring", "Pinky"):
            for i in (1, 2, 3):
                _set_dir(rig, pb[p + "Hand" + f + str(i)], (sx, 0.0, 0.0))
        for i in (1, 2, 3):
            _set_dir(rig, pb[p + "HandThumb" + str(i)], (sx * 0.8, -0.55, 0.0))
    bpy.ops.object.mode_set(mode='OBJECT')


def bake_pose_as_rest(rig, meshes):
    """Apply the Armature modifier to every mesh (shape-preserving, shape keys already baked),
    then apply the pose as the new rest pose and re-attach the armature modifier."""
    for m in meshes:
        select_only(m)
        for mod in list(m.modifiers):
            if mod.type == 'ARMATURE':
                bpy.ops.object.modifier_apply(modifier=mod.name)
            else:
                m.modifiers.remove(mod)
    select_only(rig)
    bpy.ops.object.mode_set(mode='POSE')
    bpy.ops.pose.select_all(action='SELECT')
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    for m in meshes:
        mod = m.modifiers.new("Armature", 'ARMATURE')
        mod.object = rig
        m.parent = rig
        m.matrix_parent_inverse = Matrix.Identity(4)


# ----------------------------------------------------------------------------- per-character build
def build(spec):
    name = spec["name"]
    clear_scene()
    base = make_human(spec)
    add_face_targets(base, spec["face"])

    skin_mhmat = D("skins", spec["skin"][0], spec["skin"][0] + ".mhmat")
    HumanService.set_character_skin(skin_mhmat, base, skin_type="MAKESKIN", material_instances=False)
    rig = HumanService.add_builtin_rig(base, "mixamo_unity")
    rig.name = rig.data.name = name + "_Armature"

    objs = {}
    ex = spec.get("eyes_asset", "high-poly")
    objs["eyes"] = add_asset(base, D("eyes", ex, ex + ".mhclo"), "Eyes")
    objs["brows"] = add_asset(base, D("eyebrows", spec["brows"][0], spec["brows"][0] + ".mhclo"), "Eyebrows")
    if spec["lashes"]:
        objs["lashes"] = add_asset(base, D("eyelashes", spec["lashes"][0], spec["lashes"][0] + ".mhclo"), "Eyelashes")
    if spec["hair"]:
        objs["hair"] = add_asset(base, D("hair", spec["hair"][0], spec["hair"][0] + ".mhclo"), "Hair")
    s = spec["suit"][0]
    objs["suit"] = add_asset(base, D("clothes", s, s + ".mhclo"), "Clothes")
    if spec["shoes"]:
        objs["shoes"] = add_asset(base, D("clothes", spec["shoes"][0], spec["shoes"][0] + ".mhclo"), "Clothes")
    if spec["teeth"]:
        objs["teeth"] = add_asset(base, D("teeth", spec["teeth"][0], spec["teeth"][0] + ".mhclo"), "Teeth")
    if spec["tongue"]:
        objs["tongue"] = add_asset(base, D("tongue", "tongue01", "tongue01.mhclo"), "Tongue")

    if spec["body_proxy"]:
        p = spec["body_proxy"]
        body = add_asset(base, D("proxymeshes", p, p + ".proxy"), "Proxymeshes")
    else:
        body = base

    # ---- bake MPFB targets on the basemesh (shape keys) so apply-armature is shape-preserving
    if base.data.shape_keys:
        for m in list(base.modifiers):
            if m.type == 'MASK':
                base.modifiers.remove(m)
        TargetService.bake_targets(base)

    # ---- remove helper geometry, geometry under clothes
    if body is base:
        keep_only_group(base, "body")
    if spec["delete_under_clothes"]:
        n = delete_verts_by_groups(body, "Delete.")
        print("deleted verts under clothes", n)
    for m in list(body.modifiers):
        if m.type == 'MASK':
            body.modifiers.remove(m)
    if body is not base:
        bpy.data.objects.remove(base, do_unlink=True)
        for a in list(bpy.data.objects):
            pass

    objs["body"] = body
    for k, o in objs.items():
        for m in list(o.modifiers):
            if m.type == 'SUBSURF':
                o.modifiers.remove(m)
        o.name = o.data.name = "%s_%s" % (name, {"body": "Body", "eyes": "Eyes", "brows": "Brows", "lashes": "Lashes",
                                                 "hair": "Hair", "suit": "Clothes", "shoes": "Shoes",
                                                 "teeth": "Teeth", "tongue": "Tongue"}[k])
    meshes = list(objs.values())

    # ---- T-pose
    bpy.context.view_layer.update()
    pose_t(rig)
    bake_pose_as_rest(rig, meshes)
    # clean smooth shading
    for o in meshes:
        select_only(o)
        bpy.ops.object.shade_smooth()

    make_materials(spec, objs)
    return rig, objs


def make_materials(spec, objs):
    name = spec["name"]
    # ---- skin
    skin_src = D("skins", spec["skin"][0], spec["skin"][1])
    sk = grade(load_np(skin_src), **spec["skin_grade"])
    save_np(sk, name + "_Skin_Albedo.png")
    set_single_material(objs["body"], new_material(name + "_Skin", os.path.join(TEX_DIR, name + "_Skin_Albedo.png"), rough=0.55, spec=0.3))

    # ---- eyes
    e = load_np(D("eyes", "materials", spec["eyes"][1]))
    # the MakeHuman eye mesh has a transparent cornea shell (UVs in the clear corner of the texture):
    # drop those faces so the eye needs no alpha in Unity and the iris stays visible.
    eo = objs["eyes"]
    eh, ew = e.shape[:2]
    euv = eo.data.uv_layers.active.data
    bm = bmesh.new(); bm.from_mesh(eo.data)
    uvl = bm.loops.layers.uv.active
    kill = []
    for f in bm.faces:
        u = sum(l[uvl].uv[0] for l in f.loops) / len(f.loops)
        v = sum(l[uvl].uv[1] for l in f.loops) / len(f.loops)
        if e[min(eh - 1, int(v * eh)), min(ew - 1, int(u * ew)), 3] < 0.5:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(eo.data); bm.free()
    e[..., 3] = 1.0
    save_np(e, name + "_Eyes_Albedo.png")
    set_single_material(objs["eyes"], new_material(name + "_Eyes", os.path.join(TEX_DIR, name + "_Eyes_Albedo.png"), rough=0.3, spec=0.35))

    # ---- brows / lashes (alpha) : brow colour follows the hair colour
    b = load_np(D("eyebrows", spec["brows"][0], spec["brows"][1]))
    hairmul = spec["hair_tint"]["mul"]
    b = grade(b, mul=tuple(min(1.0, x * 0.85) for x in hairmul), gain=1.0)
    save_np(b, name + "_Brows_Albedo.png")
    set_single_material(objs["brows"], new_material(name + "_Brows", os.path.join(TEX_DIR, name + "_Brows_Albedo.png"), alpha=True, rough=0.7, spec=0.1))
    if "lashes" in objs:
        l = load_np(D("eyelashes", spec["lashes"][0], spec["lashes"][1]))
        l = grade(l, mul=(0.35, 0.3, 0.28))
        save_np(l, name + "_Lashes_Albedo.png")
        set_single_material(objs["lashes"], new_material(name + "_Lashes", os.path.join(TEX_DIR, name + "_Lashes_Albedo.png"), alpha=True, rough=0.7, spec=0.1))

    # ---- hair
    h = load_np(D("hair", spec["hair"][0], spec["hair"][1]))
    h = grade(h, **spec["hair_tint"])
    save_np(h, name + "_Hair_Albedo.png")
    hn = D("hair", spec["hair"][0], spec["hair"][0] + "_normal.png")
    hnp = None
    if os.path.exists(hn):
        save_np(load_np(hn), name + "_Hair_Normal.png")
        hnp = os.path.join(TEX_DIR, name + "_Hair_Normal.png")
    set_single_material(objs["hair"], new_material(name + "_Hair", os.path.join(TEX_DIR, name + "_Hair_Albedo.png"), normal_png=hnp, alpha=True, rough=0.7, spec=0.12))

    # ---- suit: Top + Pants (split faces by the colour of the source texture under each face)
    sdir = D("clothes", spec["suit"][0])
    src = load_np(os.path.join(sdir, spec["suit"][1] + "_diffuse.png"))
    top = colorize(src, spec["top_tint"], flatten=spec.get("flatten", 0.0))
    pants = colorize(src, spec["pants_tint"], mix=0.88, ref=0.42, flatten=spec.get("flatten", 0.0))
    save_np(top, name + "_Top_Albedo.png")
    save_np(pants, name + "_Pants_Albedo.png")
    nrm = load_np(os.path.join(sdir, spec["suit"][1] + "_normal.png"))
    nrm = downscale(nrm, 2048)
    save_np(nrm, name + "_Clothes_Normal.png")
    ao = load_np(os.path.join(sdir, spec["suit"][1] + "_ao.png"))
    save_np(ao, name + "_Clothes_AO.png")
    nrmp = os.path.join(TEX_DIR, name + "_Clothes_Normal.png")
    mt = new_material(name + "_Top", os.path.join(TEX_DIR, name + "_Top_Albedo.png"), normal_png=nrmp, rough=0.85, spec=0.2)
    mp = new_material(name + "_Pants", os.path.join(TEX_DIR, name + "_Pants_Albedo.png"), normal_png=nrmp, rough=0.9, spec=0.15)
    suit = objs["suit"]
    suit.data.materials.clear()
    suit.data.materials.append(mt)
    suit.data.materials.append(mp)
    uv = suit.data.uv_layers.active.data
    H, W = src.shape[:2]
    npoly = 0
    for p in suit.data.polygons:
        pts = [uv[i].uv for i in p.loop_indices]
        c = np.zeros(3)
        for q in pts:
            x = min(W - 1, max(0, int(q[0] * W))); y = min(H - 1, max(0, int(q[1] * H)))
            c += src[y, x, :3]
        c /= len(pts)
        # blue-dominant denim -> Pants, everything else -> Top
        is_pants = (c[2] - c[0]) > 0.10 and c[2] >= c[1]
        p.material_index = 1 if is_pants else 0
        npoly += is_pants
    print("suit faces pants/top", npoly, len(suit.data.polygons) - npoly)

    # ---- shoes
    if "shoes" in objs:
        sh = D("clothes", spec["shoes"][0])
        s = load_np(os.path.join(sh, spec["shoes"][1]))
        s = grade(s, **spec["shoe_tint"])
        save_np(s, name + "_Shoes_Albedo.png")
        sn = os.path.join(sh, spec["shoes"][0] + "_normal.png")
        snp = None
        if os.path.exists(sn):
            save_np(load_np(sn), name + "_Shoes_Normal.png")
            snp = os.path.join(TEX_DIR, name + "_Shoes_Normal.png")
        set_single_material(objs["shoes"], new_material(name + "_Shoes", os.path.join(TEX_DIR, name + "_Shoes_Albedo.png"), normal_png=snp, rough=0.7, spec=0.3))

    # ---- teeth / tongue
    if "teeth" in objs:
        t = load_np(D("teeth", spec["teeth"][0], spec["teeth"][1]))
        save_np(t, name + "_Teeth_Albedo.png")
        set_single_material(objs["teeth"], new_material(name + "_Teeth", os.path.join(TEX_DIR, name + "_Teeth_Albedo.png"), rough=0.3, spec=0.5))
    if "tongue" in objs:
        # plain flat colour map (the source tongue has no texture)
        flat = np.zeros((64, 64, 4), np.float32); flat[...] = (0.62, 0.28, 0.30, 1)
        save_np(flat, name + "_Tongue_Albedo.png")
        set_single_material(objs["tongue"], new_material(name + "_Tongue", os.path.join(TEX_DIR, name + "_Tongue_Albedo.png"), rough=0.4, spec=0.4))


# ----------------------------------------------------------------------------- reports / export
def tri_count(o):
    dg = bpy.context.evaluated_depsgraph_get()
    me = o.evaluated_get(dg).to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    o.evaluated_get(dg).to_mesh_clear()
    return n


def report(spec, rig, objs):
    print("=== REPORT", spec["name"], "height_macro", spec.get("height_macro"))
    tot = 0
    for k, o in objs.items():
        t = tri_count(o)
        tot += t
        print("  tris %-9s %6d  mats=%s" % (k, t, [s.material.name for s in o.material_slots]))
    print("  TOTAL tris", tot)
    zs = [(o.matrix_world @ v.co).z for o in objs.values() for v in o.data.vertices]
    xs = [(o.matrix_world @ v.co).x for o in objs.values() for v in o.data.vertices]
    print("  bbox z %.3f..%.3f  x %.3f..%.3f  (span x %.3f)" % (min(zs), max(zs), min(xs), max(xs), max(xs) - min(xs)))
    bodyz = [v.co.z for v in objs["body"].data.vertices]
    print("  body height %.3f" % (max(bodyz) - min(bodyz)))
    deform = [b.name for b in rig.data.bones if b.use_deform]
    print("  deform bones (%d): %s" % (len(deform), deform))
    spec["_tris"] = tot


def export_fbx(spec, rig, objs):
    out = os.path.join(FBX_DIR, "MPFB_%s.fbx" % spec["name"])
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    for o in objs.values():
        o.select_set(True)
        o.location = (0, 0, 0)
    bpy.context.view_layer.objects.active = rig
    # transforms applied (armature at origin, scale 1)
    rig.location = (0, 0, 0)
    rig.rotation_euler = (0, 0, 0)
    rig.scale = (1, 1, 1)
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False,
        use_armature_deform_only=True, object_types={'MESH', 'ARMATURE', 'EMPTY'},
        path_mode='STRIP', bake_anim=False, use_mesh_modifiers=True, primary_bone_axis='Y', secondary_bone_axis='X')
    print("EXPORTED", out, os.path.getsize(out))


# ----------------------------------------------------------------------------- rendering
def engine_name():
    ids = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    return 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in ids else 'BLENDER_EEVEE_NEXT'


def setup_stage():
    sc = bpy.context.scene
    sc.render.engine = engine_name()
    try:
        sc.eevee.taa_render_samples = 64
    except Exception:
        pass
    sc.render.film_transparent = False
    w = bpy.data.worlds.new("w") if not sc.world else sc.world
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.20, 0.205, 0.21, 1)
    bg.inputs["Strength"].default_value = 0.9
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'None'
    # floor-less neutral backdrop: lights
    def light(name, kind, loc, target, energy, size, color=(1, 1, 1)):
        ld = bpy.data.lights.new(name, kind)
        ld.energy = energy
        ld.color = color
        if kind == 'AREA':
            ld.size = size
        lo = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(lo)
        lo.location = loc
        d = Vector(target) - Vector(loc)
        lo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        return lo
    return light


def render_set(spec, rig, objs, tag_views):
    sc = bpy.context.scene
    light = setup_stage()
    # remove previous lights/cams
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'):
            bpy.data.objects.remove(o, do_unlink=True)
    L = []
    L.append(light("key", 'AREA', (-2.2, -3.0, 2.6), (0, 0, 1.1), 420, 2.5, (1.0, 0.95, 0.9)))
    L.append(light("fill", 'AREA', (3.0, -2.5, 1.2), (0, 0, 1.2), 160, 3.0, (0.85, 0.9, 1.0)))
    L.append(light("rim", 'AREA', (1.5, 3.0, 2.4), (0, 0, 1.3), 700, 1.5, (1.0, 1.0, 1.0)))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    for tag, (loc, target, lens, res) in tag_views.items():
        cam.data.lens = lens
        cam.data.clip_start = 0.05
        cam.location = loc
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        sc.render.resolution_x, sc.render.resolution_y = res
        sc.render.filepath = os.path.join(PREV_DIR, "MPFB_%s_%s.png" % (spec["name"], tag))
        bpy.ops.render.render(write_still=True)
        print("RENDER", sc.render.filepath)


def views_for(kind):
    def orbit(az_deg, dist, z, tz):
        a = math.radians(az_deg)
        return (math.sin(a) * dist, -math.cos(a) * dist, z), (0, 0, tz)
    v = {}
    for tag, az in (("Front", 0), ("ThreeQuarter", 38), ("Side", 90)):
        loc, tgt = orbit(az, 4.1, 0.95, 0.90)
        v["Full" + tag] = (loc, tgt, 50, (700, 1000))
    for tag, az in (("Front", 0), ("ThreeQuarter", 32)):
        loc, tgt = orbit(az, 1.3, 1.66, 1.65)
        v["Face" + tag] = (loc, tgt, 85, (900, 900))
    v["EyeCloseup"] = ((0.0, -0.55, 1.70), (0.0, 0.0, 1.70), 85, (900, 500))
    return v


def apply_pose_for_preview(rig):
    """Quick relaxed pose: arms lowered from T, slightly forward; one knee forward. Preview only."""
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    pb = rig.pose.bones
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        p = "mixamorig:" + side
        _set_dir(rig, pb[p + "Arm"], (sx * 0.30, -0.18, -0.94))
        _set_dir(rig, pb[p + "ForeArm"], (sx * 0.22, -0.55, -0.80))
        _set_dir(rig, pb[p + "Hand"], (sx * 0.20, -0.55, -0.80))
    _set_dir(rig, pb["mixamorig:LeftUpLeg"], (0.12, -0.35, -0.93))
    _set_dir(rig, pb["mixamorig:LeftLeg"], (0.12, -0.05, -1.0))
    bpy.ops.object.mode_set(mode='OBJECT')


# ----------------------------------------------------------------------------- main
def run(which, do_export=True, do_preview=True):
    specs = {"courier": COURIER, "hollow": HOLLOW}
    for key in which:
        spec = specs[key]
        print("##### BUILD", spec["name"])
        rig, objs = build(spec)
        report(spec, rig, objs)
        if do_export:
            export_fbx(spec, rig, objs)
        if do_preview:
            v = views_for(key)
            if key == "hollow":
                v = {"Full": v["FullFront"], "FaceFront": v["FaceFront"]}
                v["FullThreeQuarter"] = views_for(key)["FullThreeQuarter"]
            render_set(spec, rig, objs, v)
            if key == "courier":
                apply_pose_for_preview(rig)
                loc, tgt = (math.sin(math.radians(30)) * 4.1, -math.cos(math.radians(30)) * 4.1, 0.95), (0, 0, 0.90)
                render_set(spec, rig, objs, {"Posed": (loc, tgt, 50, (700, 1000))})


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    which = ["courier", "hollow"]
    for a in argv:
        if a in ("courier", "hollow"):
            which = [a]
    try:
        run(which, "noexport" not in argv, "nopreview" not in argv)
        print("DONE")
    except Exception:
        traceback.print_exc()
        print("FAILED")
