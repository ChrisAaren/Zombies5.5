"""Proof render: assembles the exported V2 FBX assets into a street scene (Eevee) -> previews/V2s_StreetScene*.png
Run: blender --background --factory-startup --python make_street_scene_v2.py
Reads Assets/Art/Models/{Vehicles,Props,Nature}/V2/*.fbx (round-trips the exports), existing world textures
(AsphaltWorn, ConcreteStained, BrickWorn) and the new RoadMarkingsV2 atlas. Nothing is written to Assets.
"""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_street2 import *

SCN = None


def fbx(sub, name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(MODELS, sub, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in bpy.data.objects:
        if o not in before and o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    ob = new[0]
    ob.parent = None
    ob.name = name
    select_only(ob)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def place(sub, name, x, y, rz=0, z=0, color=None, scale=1.0):
    ob = fbx(sub, name)
    ob.rotation_euler = (0, 0, math.radians(rz))
    ob.location = (x, y, z)
    ob.scale = (scale,) * 3
    style_object(ob, color)
    return ob


def tinted_surface(kind, tile, tint=(1, 1, 1), rough=.85, name=None):
    m = bpy.data.materials.new(name or ("S_" + kind))
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(b.outputs[0], nt.nodes["Material Output"].inputs[0])
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = img({"asphalt": "AsphaltWorn_Albedo.png", "concrete": "ConcreteStained_Albedo.png", "brick": "BrickWorn_Albedo.png",
                   "markings": "RoadMarkingsV2_Albedo.png"}[kind])
    mx = nt.nodes.new("ShaderNodeMix"); mx.data_type = 'RGBA'; mx.blend_type = 'MULTIPLY'; mx.inputs[0].default_value = 1
    mx.inputs[7].default_value = (*tint, 1)
    nt.links.new(t.outputs[0], mx.inputs[6]); nt.links.new(mx.outputs[2], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    mp = nt.nodes.new("ShaderNodeMapping"); tc = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(tc.outputs["UV"], mp.inputs[0]); nt.links.new(mp.outputs[0], t.inputs[0])
    mp.inputs["Scale"].default_value = (1 / tile, 1 / tile, 1)
    if kind == "markings":
        nt.links.new(t.outputs[1], b.inputs["Alpha"])
        mx.inputs[7].default_value = (1, 1, 1, 1)
        b.inputs["Roughness"].default_value = .6
        mp.inputs["Scale"].default_value = (1, 1, 1)
    return m


def quad(name, pts, uvs, material):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.verify()
    for l, u in zip(f.loops, uvs):
        l[uv].uv = u
    bm.normal_update()
    if f.normal.z < 0 and abs(f.normal.z) > .5:
        f.normal_flip()
    bm.to_mesh(me); bm.free()
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def slab(name, x0, x1, y0, y1, z0, z1, material, tile=1.0):
    """Box with world-meter UVs."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=xf(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0)))
    project_uvs(bm)
    bm.to_mesh(me); bm.free()
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def facade(side_x, y0, y1, height, face_dir, tint, floors0=4.2, seed=1, mat_tile=2.0, depth=12):
    """A brick wall with windows, sills and a cornice. face_dir = +1 faces +X, -1 faces -X."""
    rng = random.Random(seed)
    wall = tinted_surface("brick", mat_tile, tint, .9, "Brick%d" % seed)
    x0, x1 = (side_x - depth, side_x) if face_dir > 0 else (side_x, side_x + depth)
    # split into 3-4 buildings of different heights
    y = y0
    gl = bpy.data.materials.new("WinGlass%d" % seed); gl.use_nodes = True
    gb = gl.node_tree.nodes["Principled BSDF"]
    gb.inputs["Base Color"].default_value = (.06, .08, .1, 1); gb.inputs["Roughness"].default_value = .05
    gb.inputs["Metallic"].default_value = .6
    trim = bpy.data.materials.new("Trim%d" % seed); trim.diffuse_color = (.62, .58, .5, 1); trim.use_nodes = True
    trim.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.62, .58, .5, 1)
    frame = bpy.data.materials.new("Frm%d" % seed); frame.use_nodes = True
    frame.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.1, .13, .1, 1)
    shop = bpy.data.materials.new("Shop%d" % seed); shop.use_nodes = True
    shop.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.03, .03, .035, 1)
    shop.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = .1
    while y < y1:
        w = rng.choice((12, 15, 18, 21))
        h = rng.choice((4.2 + 3.2 * 3, 4.2 + 3.2 * 4, 4.2 + 3.2 * 5)) if height is None else height
        ya, yb = y, min(y + w, y1)
        slab("bld", x0, x1, ya, yb, 0, h, wall)
        nx = int((yb - ya) / 3.0)
        for k in range(nx):
            yc = ya + 1.5 + k * 3.0 + (yb - ya - nx * 3.0) / 2
            # storefront
            fx = side_x + face_dir * .02
            slab("shop", fx - (0 if face_dir > 0 else 0), fx + face_dir * .08, yc - 1.2, yc + 1.2, .5, 3.4, shop) if False else None
            sx0, sx1 = (side_x, side_x + .06) if face_dir > 0 else (side_x - .06, side_x)
            slab("shop", sx0, sx1, yc - 1.25, yc + 1.25, .5, 3.5, shop)
            slab("shopf", sx0, sx1 + (.03 if face_dir > 0 else -.03), yc - 1.4, yc - 1.25, .4, 3.6, frame)
            floors = int((h - 4.2) / 3.2)
            for fl in range(floors):
                zc = 4.2 + 1.6 + fl * 3.2
                a0, a1 = (side_x, side_x + .05) if face_dir > 0 else (side_x - .05, side_x)
                lit = rng.random() < .1
                slab("win", a0, a1, yc - .65, yc + .65, zc - .8, zc + .8, gl)
                fa0, fa1 = (side_x, side_x + .1) if face_dir > 0 else (side_x - .1, side_x)
                slab("sill", fa0, fa1 + (.06 if face_dir > 0 else -.06), yc - .8, yc + .8, zc - .95, zc - .8, trim)
                slab("lintel", fa0, fa1, yc - .8, yc + .8, zc + .8, zc + .95, trim)
                slab("fr", a0, a1 + (.025 if face_dir > 0 else -.025), yc - .04, yc + .04, zc - .8, zc + .8, frame)
        c0, c1 = (side_x, side_x + .45) if face_dir > 0 else (side_x - .45, side_x)
        slab("cornice", c0, c1, ya, yb, h - .5, h, trim)
        slab("belt", (side_x, side_x + .08)[::1] if face_dir > 0 else (side_x - .08, side_x), (ya, yb)[0], (ya, yb)[1], 3.7, 4.0, trim) if False else None
        y = yb


def road_marking_quad(cell, x0, x1, y0, y1, mat, z=.012):
    cx, cy = cell % 4, cell // 4
    u0, u1 = cx * .25, cx * .25 + .25
    v1, v0 = 1 - cy * .25, 1 - cy * .25 - .25
    # (x, y) rect, UV u along +x, v along +y
    return quad("mark", [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)], [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], mat)


def set_haze(density=.004):
    w = bpy.context.scene.world
    nt = w.node_tree
    vol = nt.nodes.new("ShaderNodeVolumeScatter")
    vol.inputs["Density"].default_value = density
    vol.inputs["Color"].default_value = (.95, .9, .82, 1)
    vol.inputs["Anisotropy"].default_value = .35
    nt.links.new(vol.outputs[0], nt.nodes["World Output"].inputs["Volume"])
    try:
        ev = bpy.context.scene.eevee
        ev.volumetric_end = 260
        ev.volumetric_tile_size = '2'
        ev.volumetric_samples = 48
    except Exception as e:
        print("volumetric settings", e)


def build_scene():
    reset_scene()
    scn = setup_render((1600, 900), samples=64, exposure=-.2, sun_elev=40, sun_az=-55, sun_strength=3.0)
    conc = tinted_surface("concrete", 2.0, (1.05, 1.0, .95), .9, "SidewalkMat")
    asph = tinted_surface("asphalt", 4.0, (1, 1, 1), .92, "RoadMat")
    mark = tinted_surface("markings", 1, (1, 1, 1), .6, "MarkMat")
    soil = bpy.data.materials.new("Soil"); soil.use_nodes = True
    soil.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.04, .03, .025, 1)
    # ground
    slab("road", -4.15, 14, -140, 40, -.3, 0, asph)
    slab("walk", -12, -4.15, -140, 40, -.3, .15, conc)
    slab("walkL", 14, 16, -140, 40, -.3, .15, conc)
    curb = bpy.data.materials.new("Curb"); curb.use_nodes = True
    curb.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (.5, .48, .44, 1)
    slab("curb", -4.4, -4.15, -140, 40, 0, .16, curb)
    # markings: double yellow every 2 m (mixed clean / worn / cracked), white lane edge, stop bar + crosswalk
    rng = random.Random(4)
    y = 40
    while y > -140:
        cell = rng.choice((0, 0, 1, 1, 1, 2))
        road_marking_quad(cell, -1.0, 1.0, y - 2, y, mark)
        for cx0 in (-1.98 - 1.0,):
            road_marking_quad(rng.choice((3, 5, 5, 6)), cx0, cx0 + 2.0, y - 2, y, mark)
        y -= 2
    for k in range(3):
        road_marking_quad(rng.choice((14, 15)), -4 + k * 4, k * 4, 12 - k * 26, 14 - k * 26, mark, .008) if False else None
    for xx in range(0, 12, 2):
        road_marking_quad(8 if xx % 4 == 0 else 9, xx - 0.0, xx + 2, -44, -42, mark)
    for xx in range(-4, 14, 2):
        for yy in range(-52, -46, 2):
            road_marking_quad(10 if (xx + yy) % 4 else 11, xx, xx + 2, yy - 2 if False else yy, yy + 2, mark)
    road_marking_quad(12, 3, 5, 16, 18, mark) if False else None
    road_marking_quad(12, 3.5, 5.5, -8, -6, mark)
    # crack/patch decals
    road_marking_quad(14, -2.5, -.5, 10, 12, mark, .01)
    road_marking_quad(13, 1, 3, -16, -14, mark, .01)
    road_marking_quad(15, 1, 3, -2, 0, mark, .01)
    # buildings
    facade(-12, -140, 40, None, +1, (1.0, .72, .55), seed=11)
    facade(16, -140, 40, None, -1, (.85, .7, .58), seed=23)
    # sidewalk details: tree pit soil patch
    # parked cars (nose to tail, facing -Y), curb side
    cars = [("CarV2_SUV", (.10, .11, .13)), ("CarV2_Sedan", (.30, .36, .45)), ("CarV2_Minivan", (.62, .63, .62)), ("CarV2_Wagon", (.22, .30, .25))]
    lens_ = {"CarV2_SUV": 4.67, "CarV2_Sedan": 4.57, "CarV2_Minivan": 4.94, "CarV2_Wagon": 4.77}
    y = 3.0
    rr = random.Random(2)
    for nm, col in cars:
        L = lens_[nm]
        yc = y - L / 2
        place(V2_VEH, nm, -3.15 + rr.uniform(-.1, .08), yc, rr.uniform(-1.2, 1.2), 0, col)
        y -= L + rr.uniform(.45, .8)
    place(V2_VEH, "CarV2_Cab", 2.1, -9, 0, 0, (.80, .66, .18))
    place(V2_VEH, "CarV2_Pickup", 5.2, -44, 180, 0, (.50, .45, .36))
    place(V2_VEH, "CarV2_Hatch", -3.1, -27, 0, 0, (.55, .56, .58))
    place(V2_VEH, "CarV2_Coupe", -3.1, -32.2, -.5, 0, (.42, .12, .12))
    # trees, grates, lamps, signals, signs
    soilp = quad("pit", [(-5.8, -12.6, .152), (-4.6, -12.6, .152), (-4.6, -11.4, .152), (-5.8, -11.4, .152)], [(0, 0)] * 4, soil)
    soilp.hide_render = False
    place(V2_NATURE, "TreePitGrate", -5.2, -12, 0, .152)
    place(V2_NATURE, "StreetTree_A", -5.2, -12, 20, .15)
    soilp2 = quad("pit2", [(-5.8, -34.6, .152), (-4.6, -34.6, .152), (-4.6, -33.4, .152), (-5.8, -33.4, .152)], [(0, 0)] * 4, soil)
    place(V2_NATURE, "TreePitGrate", -5.2, -34, 0, .152)
    place(V2_NATURE, "StreetTree_B", -5.2, -34, 70, .15)
    place(V2_NATURE, "StreetTree_C", 14.4, -22, 10, .15)
    place(V2_PROPS, "StreetLamp_V2", -4.75, -2, 0, .15)
    place(V2_PROPS, "StreetLamp_V2", -4.75, -26, 0, .15)
    place(V2_PROPS, "StreetLamp_V2", 14.5, -14, 180, .15)
    place(V2_PROPS, "TrafficLight_Pole", 14.2, -40, 180, .15)
    place(V2_PROPS, "TrafficLight_Post", -4.75, -41, 180, .15)
    place(V2_PROPS, "StreetSign_Post", -4.85, -39, 180, .15)
    place(V2_PROPS, "StopSign", 14.5, -38, 180, .15)
    place(V2_PROPS, "ParkingMeter", -4.75, -7.6, 180, .15)
    place(V2_PROPS, "ParkingMeter", -4.75, -14.2, 180, .15)
    place(V2_PROPS, "NoParkingSign", -4.8, -21, 180, .15)
    place(V2_PROPS, "BusStopSign", -4.8, -57, 180, .15)
    place(V2_PROPS, "Bollard_V2", -4.75, -44.5, 0, .15)
    place(V2_PROPS, "Bollard_V2", -6.2, -44.5, 0, .15)
    place(V2_PROPS, "CurbDrain", -3.95, -23, 90, 0)
    place(V2_PROPS, "PhoneBooth", -8.4, -42, 270, .15)
    if "--haze" in sys.argv:
        set_haze(.0035)
    return scn


def shot(path, loc, tgt, lens=35, size=(1600, 900)):
    scn = bpy.context.scene
    scn.render.resolution_x, scn.render.resolution_y = size
    for o in [o for o in scn.objects if o.type == 'CAMERA']:
        bpy.data.objects.remove(o, do_unlink=True)
    look_cam(loc, tgt, lens)
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    grade_png(path)
    print("PREVIEW", path)


def main():
    build_scene()
    shot(os.path.join(PREVIEWS, "V2s_StreetScene.png"), (-1.0, 12.0, 1.7), (-2.8, -30, 2.6), 33)
    shot(os.path.join(PREVIEWS, "V2s_StreetScene_Sidewalk.png"), (-6.6, 5.0, 1.65), (-3.2, -18, 1.4), 30)
    shot(os.path.join(PREVIEWS, "V2s_StreetScene_Corner.png"), (-1.5, -16, 1.8), (8, -46, 3.0), 34)


main()
