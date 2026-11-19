"""Char_Body: one smooth skinned adult + 6-bone armature -> Assets/Art/Models/Characters/Char_Body.fbx
Run: blender --background --factory-startup --python make_body.py
Also renders previews (standing + deformation pose sheets) to Tools/Blender/previews/.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from bl_human import *

POSES = {
    "rest": {},
    "legs_fwd_back": {"LegR": (-40, 0, 0), "LegL": (40, 0, 0)},
    "legs_back_fwd": {"LegR": (40, 0, 0), "LegL": (-40, 0, 0)},
    "arms_swing": {"ArmR": (-35, 0, 0), "ArmL": (35, 0, 0)},
    "arms_raised_fwd": {"ArmR": (-80, 0, 0), "ArmL": (-80, 0, 0)},
    "arms_out_side": {"ArmR": (0, 75, 0), "ArmL": (0, -75, 0)},
    "head_yaw": {"Head": (0, 0, 45)},
    "combined": {"LegR": (-40, 0, 0), "LegL": (30, 0, 0), "ArmR": (-80, 0, 0), "ArmL": (35, 0, 0), "Head": (0, 0, -45)},
}


def export_body(body, rig):
    d = os.path.join(C.MODELS, "Characters")
    C.ensure_dir(d)
    pose(rig, {})
    select_only(body, rig)
    bpy.ops.export_scene.fbx(filepath=os.path.join(d, "Char_Body.fbx"), use_selection=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE',
                             add_leaf_bones=False, object_types={'MESH', 'ARMATURE'}, use_armature_deform_only=True)


def pose_sheet(body, rig, names, path, az=0, el=6, spacing=.95):
    insts = []
    n = len(names)
    rx, ry = math.cos(math.radians(az)), math.sin(math.radians(az))   # screen-right direction
    for i, nm in enumerate(names):
        o = (i - (n - 1) / 2) * spacing
        b2, r2 = instance(body, rig, (rx * o, ry * o, 0))
        pose(r2, POSES[nm])
        insts.append(b2)
    render_view(insts, path, az=az, el=el, size=(420 * n, 840), target=(0, 0, .95), scale=n * spacing + .2, floor_size=5)
    for b2 in insts:
        r2 = b2.parent
        bpy.data.objects.remove(b2, do_unlink=True); bpy.data.objects.remove(r2, do_unlink=True)


def main():
    C.reset_scene()
    body, rig = build_body()
    # remove the helper head_ref leftovers (joined already), report
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o is not body:
            bpy.data.objects.remove(o, do_unlink=True)
    C.report(body, (None, 1.80, None))
    mx = max(len(v.groups) for v in body.data.vertices)
    un = sum(1 for v in body.data.vertices if len(v.groups) == 0)
    print("max influences", mx, "unweighted", un, "slots", [m.name for m in body.data.materials])
    export_body(body, rig)
    P = C.PREVIEWS
    for az, nm in ((0, "front"), (90, "side"), (155, "back"), (35, "3q")):
        render_view([body], os.path.join(P, "Char_Body_%s.png" % nm), az=az, el=5, size=(600, 900), scale=1.95, target=(0, 0, .93))
    render_view([body], os.path.join(P, "Char_Body.png"), az=30, el=8, size=(720, 900), scale=1.95, target=(0, 0, .93))
    render_view([body], os.path.join(P, "Char_Body_head.png"), az=25, el=3, size=(900, 900), scale=.5, target=(0, 0, 1.66), floor=False)
    render_view([body], os.path.join(P, "Char_Body_head_side.png"), az=90, el=3, size=(900, 900), scale=.5, target=(0, 0, 1.66), floor=False)
    pose_sheet(body, rig, ["rest", "legs_fwd_back", "legs_back_fwd", "arms_swing"], os.path.join(P, "Pose_A_legs_arms_front.png"))
    pose_sheet(body, rig, ["legs_fwd_back", "legs_back_fwd", "arms_swing"], os.path.join(P, "Pose_A_legs_arms_side.png"), az=90)
    pose_sheet(body, rig, ["arms_raised_fwd", "arms_out_side", "head_yaw", "combined"], os.path.join(P, "Pose_B_raise_yaw_front.png"))
    pose_sheet(body, rig, ["arms_raised_fwd", "head_yaw", "combined"], os.path.join(P, "Pose_B_raise_yaw_3q.png"), az=50)
    pose_sheet(body, rig, ["arms_raised_fwd", "combined"], os.path.join(P, "Pose_B_raise_side.png"), az=90)


if __name__ == "__main__":
    main()
