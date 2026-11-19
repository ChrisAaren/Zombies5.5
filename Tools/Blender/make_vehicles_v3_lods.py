"""Vehicles V3 LODs -> Assets/Art/Models/Vehicles/V3/CarV3_<Name>_L1.fbx and _L2.fbx
Run: blender --background --factory-startup --python make_vehicles_v3_lods.py [-- --only=Sedan,Cab]

Reads the finished LOD0 FBX, splits it per material slot, decimates each slot with its own ratio (so the silhouette parts keep
more triangles than the small detail parts), and re-joins with the SAME slot list, pivot and UV sets (UV0 + UVDirt).
  LOD1 ~7k tris: interior reduced to a few hundred tris (seat blocks + dash), the rest decimated.
  LOD2 ~2k tris: no interior faces (the slot stays, empty), glass kept as a plain shell (Unity gives it an opaque dark tint),
                 wheels simplified.
"""
import sys, os
import bpy, bmesh

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
V3 = os.path.join(ROOT, "Assets", "Art", "Models", "Vehicles", "V3")
NAMES = ["Sedan", "Compact", "Hatch", "Minivan", "SUV", "Pickup", "Van", "Cab"]

# relative priority of each slot when the triangle budget is shared out (higher = keeps more of its triangles)
WEIGHT = {"CarPaint": 1.0, "Glass": 0.9, "Rubber": 0.5, "Chrome": 0.45, "Metal": 0.45, "Headlight": 0.5, "Taillight": 0.5,
          "Paint": 0.5, "LampGlass": 0.8}
LODS = {1: dict(target=7000, interior=700), 2: dict(target=2000, interior=0)}


def import_car(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(V3, "CarV3_%s.fbx" % name), axis_forward='-Z', axis_up='Y')
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    assert len(meshes) == 1, meshes
    ob = meshes[0]
    for o in list(bpy.context.scene.objects):
        if o is not ob:
            bpy.data.objects.remove(o)
    ob.parent = None
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def tris(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def split_slots(ob):
    """one mesh datablock per slot index, every one keeping the full slot list"""
    me = ob.data
    out = {}
    for si, slot in enumerate(ob.material_slots):
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != si], context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        n = bpy.data.meshes.new("slot_%s" % slot.name)
        bm.to_mesh(n)
        bm.free()
        for m in me.materials:
            n.materials.append(m)
        out[si] = n
    return out


def decimate(me, ratio):
    if ratio >= 0.999 or not me.polygons:
        return me
    ob = bpy.data.objects.new("tmp", me)
    bpy.context.scene.collection.objects.link(ob)
    md = ob.modifiers.new("d", 'DECIMATE')
    md.decimate_type = 'COLLAPSE'
    md.ratio = max(ratio, 0.005)
    md.use_collapse_triangulate = True
    ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    new = bpy.data.meshes.new_from_object(ev)
    bpy.data.objects.remove(ob)
    return new


def build_lod(src, lod):
    cfg = LODS[lod]
    slots = split_slots(src)
    names = [s.name.split(".")[0] for s in src.material_slots]
    counts = {si: tris(m) for si, m in slots.items()}
    keep = {}     # slot index -> final mesh for slots with a fixed treatment
    fixed = 0
    for si, nm in enumerate(names):
        if nm == "Interior":
            if cfg["interior"]:
                keep[si] = decimate(slots[si], min(1.0, cfg["interior"] / max(counts[si], 1)))
                fixed += tris(keep[si])
            else:
                keep[si] = None
        elif nm == "Plate":
            keep[si] = slots[si]
            fixed += counts[si]
    free = [si for si in range(len(names)) if si not in keep]
    budget = cfg["target"] - fixed
    scale = 1.0
    done = {}
    for _ in range(4):
        lo, hi = 0.0, 1000.0
        for _i in range(60):
            k = (lo + hi) / 2
            t = sum(min(1.0, k * WEIGHT.get(names[si], .5) * scale) * counts[si] for si in free)
            lo, hi = (k, hi) if t < budget else (lo, k)
        done = {si: decimate(slots[si], min(1.0, lo * WEIGHT.get(names[si], .5) * scale)) for si in free}
        got = sum(tris(m) for m in done.values()) + fixed
        if abs(got - cfg["target"]) < cfg["target"] * .05:
            break
        scale *= cfg["target"] / max(got, 1)
    bm = bmesh.new()
    for si in range(len(names)):
        m = done[si] if si in done else keep[si]
        if m is not None:
            bm.from_mesh(m)
    out = bpy.data.meshes.new(src.data.name + "_LOD%d" % lod)
    bm.to_mesh(out)
    bm.free()
    for m in src.data.materials:
        out.materials.append(m)
    for p in out.polygons:
        p.use_smooth = True
    return out


def per_slot(me):
    per = {}
    for p in me.polygons:
        n = me.materials[p.material_index].name.split(".")[0]
        per[n] = per.get(n, 0) + len(p.vertices) - 2
    return per


def export(ob, path):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                             object_types={'MESH', 'EMPTY'})


def bounds(me):
    vs = [v.co for v in me.vertices]
    return tuple(round(c, 3) for c in (min(v.x for v in vs), max(v.x for v in vs), min(v.y for v in vs), max(v.y for v in vs),
                                       min(v.z for v in vs), max(v.z for v in vs)))


def main():
    only = None
    for a in sys.argv:
        if a.startswith("--only="):
            only = a.split("=")[1].split(",")
    summary = []
    for nm in NAMES:
        if only and nm not in only:
            continue
        src = import_car(nm)
        base = src.data
        print("LOD0", nm, tris(base), bounds(base), [uv.name for uv in base.uv_layers], per_slot(base))
        row = [nm, tris(base)]
        for lod in (1, 2):
            me = build_lod(src, lod)
            ob = bpy.data.objects.new(src.name, me)
            bpy.context.scene.collection.objects.link(ob)
            print("LOD%d" % lod, nm, tris(me), bounds(me), [uv.name for uv in me.uv_layers], per_slot(me))
            export(ob, os.path.join(V3, "CarV3_%s_L%d.fbx" % (nm, lod)))
            row.append(tris(me))
            bpy.data.objects.remove(ob)
        summary.append(row)
    print("LODSUMMARY", summary)


main()
