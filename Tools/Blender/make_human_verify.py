"""Re-import Char_Human.fbx and compare bone names / head positions against bl_skeleton; report mesh facts."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy, math
from mathutils import Vector
import bl_skeleton

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r"E:\Unity\Zombies5.5\Assets\Art\Models\Characters\Char_Human.fbx")
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
print("[verify] armature", rig.name, "bones", len(rig.data.bones), "mesh", mesh.name, "mats", [m.name for m in mesh.data.materials])
print("[verify] rig matrix_world scale", tuple(round(v, 3) for v in rig.matrix_world.to_scale()), "euler", tuple(round(math.degrees(v), 1) for v in rig.matrix_world.to_euler()))
table = {b[0]: b for b in bl_skeleton.bone_table()}
missing = [n for n in table if n not in rig.data.bones]
extra = [b.name for b in rig.data.bones if b.name not in table]
print("[verify] missing bones", missing, "extra bones", extra)
worst = 0.0; worst_n = ""
for b in rig.data.bones:
    if b.name in table:
        h = rig.matrix_world @ b.head_local
        d = (h - Vector(table[b.name][1])).length
        if d > worst:
            worst, worst_n = d, b.name
print("[verify] max bone-head error vs bl_skeleton: %.5f m (%s)" % (worst, worst_n))
parents_ok = all((b.parent.name if b.parent else None) == table[b.name][3] for b in rig.data.bones if b.name in table)
print("[verify] parent hierarchy matches:", parents_ok)
me = mesh.data
import bmesh
print("[verify] mesh verts %d tris %d uv layers %d" % (len(me.vertices), sum(len(p.vertices) - 2 for p in me.polygons), len(me.uv_layers)))
vg = {g.name for g in mesh.vertex_groups}
print("[verify] vertex groups %d, all bones present: %s" % (len(vg), set(table) <= vg))
mx = max(len([g for g in v.groups if g.weight > 0]) for v in me.vertices)
print("[verify] max influences", mx)
zs = [(mesh.matrix_world @ v.co).z for v in me.vertices]
print("[verify] height %.3f..%.3f" % (min(zs), max(zs)))
