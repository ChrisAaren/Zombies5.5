"""UV layout for Char_Human.

Skin (head, neck, hands)  : Blender smart-project islands, packed into 0..1 (single 2K Skin_Albedo / Skin_Normal).
Top / Pants / Shoe        : world-space box projection, 1 UV unit = TILE metres, offset into separate integer tiles
                            (Top u+1, Pants u+2, Shoe u+3) so the tiling cloth textures never overlap the skin layout.
EyeWhite / Iris / Brow    : EyeWhite tiny constant, Iris planar onto Eye_Albedo (iris disc radius = IRIS_R), Brow v-strip.
"""
import math
import bpy, bmesh
from mathutils import Vector

TILE = 0.25
OFFS = {"Top": 1.0, "Pants": 2.0, "Shoe": 3.0}


def unwrap_skin(obj, skin_slot):
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(me)
    for f in bm.faces:
        f.select_set(False)
    for f in bm.faces:
        if f.material_index == skin_slot:
            f.select_set(True)
    bmesh.update_edit_mesh(me)
    bpy.ops.uv.smart_project(angle_limit=math.radians(70), island_margin=0.004, area_weight=0.6,
                             correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def box_uvs(obj, slot_names, iris_info):
    me = obj.data
    names = list(slot_names)
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.active
    for f in bm.faces:
        nm = names[f.material_index]
        if nm in OFFS:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                if ax == 1:
                    u, v = p.x, p.z
                elif ax == 0:
                    u, v = p.y, p.z
                else:
                    u, v = p.x, p.y
                l[uv].uv = (u / TILE + OFFS[nm], v / TILE)
        elif nm == "Iris":
            c = iris_info(f.calc_center_median())
            for l in f.loops:
                p = l.vert.co
                l[uv].uv = (0.5 + (p.x - c.x) / (2 * 0.0062), 0.5 + (p.z - c.z) / (2 * 0.0062))
        elif nm == "EyeWhite":
            for l in f.loops:
                l[uv].uv = (0.02, 0.02)
        elif nm == "Brow":
            for l in f.loops:
                p = l.vert.co
                l[uv].uv = (abs(p.x) * 20.0, p.z * 40.0 % 1.0)
    bm.to_mesh(me)
    bm.free()


def report(obj, skin_slot):
    me = obj.data
    uv = me.uv_layers.active
    xs, ys = [], []
    for p in me.polygons:
        if p.material_index == skin_slot:
            for li in p.loop_indices:
                u = uv.data[li].uv
                xs.append(u[0]); ys.append(u[1])
    return (min(xs), max(xs), min(ys), max(ys)) if xs else None
