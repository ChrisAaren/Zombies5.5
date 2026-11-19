"""Stage 3: build the deform-only Unity Humanoid armature from joints.json (left/right mirrored).

  blender -b --factory-startup --python skeleton.py -- --out work/testcourier/v001

Reads <out>/cleanup.blend + <out>/joints.json, writes <out>/skeleton.blend (adds object "Armature") and
<out>/skeleton.json (bone head/tail table, mirror error). Bone names, parenting and rolls come from
Tools/Blender/bl_skeleton.py (build_armature_from_joints), the same scheme as the project's other characters.
"""
import bpy, os, sys
import numpy as np
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pl_common as pc
import bl_skeleton as sk

A = pc.parse_args({"out": ""})
OUT = os.path.abspath(A["out"])
CFG = pc.load_config(OUT)
data = pc.read_json(os.path.join(OUT, "joints.json"))
J = {k: v for k, v in data["joints"].items()}

pos = {}
conf = {}
for name, v in J.items():
    pos[name] = np.array(v["pos"], float)
    conf[name] = v["confidence"]

# mirror: average each Left joint with its (mirrored) Right counterpart; centre joints sit on x = 0
sym = {}
for name in list(pos):
    if name.startswith("Left"):
        r = "Right" + name[4:]
        l, rr = pos[name], pos[r]
        m = np.array([(l[0] - rr[0]) / 2, (l[1] + rr[1]) / 2, (l[2] + rr[2]) / 2])
        sym[name] = m
        sym[r] = np.array([-m[0], m[1], m[2]])
    elif not name.startswith("Right"):
        p = pos[name].copy()
        p[0] = 0.0
        sym[name] = p

pc.open_blend(os.path.join(OUT, "cleanup.blend"))
arm = sk.build_armature_from_joints({k: tuple(v) for k, v in sym.items()}, "Armature")
arm.name = "Armature"
arm.data.name = "Armature"
arm.show_in_front = True

# bone table + mirror error of the built armature
table = {}
for b in arm.data.bones:
    table[b.name] = {"head": list(b.head_local), "tail": list(b.tail_local), "parent": b.parent.name if b.parent else None,
                     "length": float(b.length), "deform": bool(b.use_deform)}
mirror_err = 0.0
worst = None
for name, t in table.items():
    if name.startswith("Left"):
        o = table["Right" + name[4:]]
        for k in ("head", "tail"):
            e = max(abs(t[k][0] + o[k][0]), abs(t[k][1] - o[k][1]), abs(t[k][2] - o[k][2]))
            if e > mirror_err:
                mirror_err, worst = e, name
pc.write_json(os.path.join(OUT, "skeleton.json"), {
    "bones": table, "bone_count": len(table), "mirror_max_error_m": mirror_err, "mirror_worst_bone": worst,
    "joint_confidence": conf, "required_bones": sk.BONE_NAMES})
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "skeleton.blend"))
print("SKELETON bones", len(table), "mirror err", mirror_err)
print("STAGE3 DONE")
