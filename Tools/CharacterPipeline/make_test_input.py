"""Generate the pipeline test input: an UNRIGGED, STATIC adult male in an A-pose with spread fingers.

Stand-in for an external sculpt. Uses MPFB (CC0 MakeHuman assets), so run WITHOUT --factory-startup:
  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python Tools/CharacterPipeline/make_test_input.py

Output: Tools/CharacterPipeline/test_input/testcourier_source.fbx (textures live ONLY in test_input/textures/; the FBX stores bare file names and importers find them by recursive search; plus a .blend copy).
Body, clothes, hair, brows, lashes, eyes, teeth and tongue are joined into ONE mesh with 8+ materials.
"""
import bpy, os, sys, math, traceback
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(os.path.dirname(HERE), "Blender"))
import make_mpfb_characters as mpfb

OUT = os.path.join(HERE, "test_input")
TEX = os.path.join(OUT, "textures")
os.makedirs(TEX, exist_ok=True)
mpfb.TEX_DIR = TEX  # keep textures out of Assets/

# finger spread (world Y offset added to the arm direction; -Y is forward = thumb/index side)
SPREAD = {"Index": -0.30, "Middle": -0.10, "Ring": 0.11, "Pinky": 0.34}
ARM_DOWN = 0.70  # arm direction = (side, 0, -ARM_DOWN): ~35 degrees below horizontal


def pose_a(rig):
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    pb = rig.pose.bones
    sdir = mpfb._set_dir
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        p = "mixamorig:" + side
        sdir(rig, pb[p + "Shoulder"], (sx, 0.0, 0.10))
        arm = Vector((sx, 0.0, -ARM_DOWN)).normalized()
        for n in ("Arm", "ForeArm", "Hand"):
            sdir(rig, pb[p + n], arm)
        for f, k in SPREAD.items():
            d = (arm + Vector((0, k, 0))).normalized()
            for i in (1, 2, 3):
                sdir(rig, pb[p + "Hand" + f + str(i)], d)
        td = (arm + Vector((sx * 0.25, -0.75, 0.0))).normalized()
        for i in (1, 2, 3):
            sdir(rig, pb[p + "HandThumb" + str(i)], td)
    bpy.ops.object.mode_set(mode='OBJECT')


mpfb.pose_t = pose_a

SPEC = dict(mpfb.COURIER)
SPEC["name"] = "testcourier"


def main():
    rig, objs = mpfb.build(SPEC)
    meshes = list(objs.values())
    for m in meshes:
        for mod in list(m.modifiers):
            m.modifiers.remove(mod)
        m.parent = None
    bpy.data.objects.remove(rig, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes:
        m.select_set(True)
        m.location = (0, 0, 0)
    bpy.context.view_layer.objects.active = objs["body"]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = ob.data.name = "testcourier_source"
    ob.vertex_groups.clear()
    zs = [v.co.z for v in ob.data.vertices]
    print("joined: verts", len(ob.data.vertices), "tris", sum(len(p.vertices) - 2 for p in ob.data.polygons),
          "materials", len(ob.data.materials), "height", max(zs) - min(zs))
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    out = os.path.join(OUT, "testcourier_source.fbx")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', object_types={'MESH'},
        path_mode='STRIP', embed_textures=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "testcourier_source.blend"), copy=True)
    print("EXPORTED", out, os.path.getsize(out))


try:
    main()
    print("DONE")
except Exception:
    traceback.print_exc()
    print("FAILED")
