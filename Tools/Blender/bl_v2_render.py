"""Eevee preview + street-assembly rendering for architecture kit v2 (imported by make_architecture_v2.py)."""
import math, os
import bpy
import numpy as np
from mathutils import Vector, Matrix

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
TEX = os.path.join(ROOT, "Assets", "Art", "Textures")
MODELS = os.path.join(ROOT, "Assets", "Art", "Models")


# ------------------------------------------------------------------ materials
def _img(name, srgb=True):
    path = os.path.join(TEX, name)
    img = bpy.data.images.load(path, check_existing=True)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    return img


def mk(name, base=(0.5, 0.5, 0.5), albedo=None, normal=None, scale=(1, 1), tint=None, rough=0.8, metallic=0.0,
       nstrength=1.0, alpha=None, blend=False, emission=None, emit_strength=0.0, rough_img=None, albedo_alpha=False):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Base Color"].default_value = (*base, 1)
    m.diffuse_color = (*base, 1)
    uv = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (scale[0], scale[1], 1)
    nt.links.new(uv.outputs["UV"], mp.inputs["Vector"])
    last = None
    if albedo:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = _img(albedo)
        t.interpolation = 'Smart' if False else 'Linear'
        nt.links.new(mp.outputs["Vector"], t.inputs["Vector"])
        last = t.outputs["Color"]
        if tint:
            mx = nt.nodes.new("ShaderNodeMix")
            mx.data_type = 'RGBA'
            mx.blend_type = 'MULTIPLY'
            mx.inputs["Factor"].default_value = 1.0
            nt.links.new(last, mx.inputs["A"])
            mx.inputs["B"].default_value = (*tint, 1)
            last = mx.outputs["Result"]
        nt.links.new(last, bsdf.inputs["Base Color"])
        if albedo_alpha:
            nt.links.new(t.outputs["Alpha"], bsdf.inputs["Alpha"])
    if normal:
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = _img(normal, False)
        nt.links.new(mp.outputs["Vector"], tn.inputs["Vector"])
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = nstrength
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if rough_img:
        tr = nt.nodes.new("ShaderNodeTexImage")
        tr.image = _img(rough_img, False)
        nt.links.new(mp.outputs["Vector"], tr.inputs["Vector"])
        inv = nt.nodes.new("ShaderNodeMath")
        inv.operation = 'SUBTRACT'
        inv.inputs[0].default_value = 1.0
        nt.links.new(tr.outputs["Color"], inv.inputs[1])
        nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    if emission:
        te = nt.nodes.new("ShaderNodeTexImage")
        te.image = _img(emission)
        nt.links.new(mp.outputs["Vector"], te.inputs["Vector"])
        nt.links.new(te.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    if alpha is not None:
        bsdf.inputs["Alpha"].default_value = alpha
    if blend or alpha is not None or albedo_alpha:
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    return m


BRICK_SCALE = (1 / 1.5, 1 / 1.6)


def build_materials():
    mk("Wall", (.55, .25, .2), "BrickRed_Albedo.png", "BrickRed_Normal.png", BRICK_SCALE, rough=0.85, nstrength=1.0)
    mk("Wall_Red", (.55, .25, .2), "BrickRed_Albedo.png", "BrickRed_Normal.png", BRICK_SCALE, rough=0.85)
    mk("Wall_Brown", (.35, .2, .15), "BrickBrown_Albedo.png", "BrickBrown_Normal.png", BRICK_SCALE, rough=0.85)
    mk("Wall_Tan", (.6, .5, .35), "BrickTan_Albedo.png", "BrickTan_Normal.png", BRICK_SCALE, rough=0.85)
    mk("Trim", (.8, .78, .72), "StoneTrim_Albedo.png", "StoneTrim_Normal.png", (0.5, 0.5), tint=(1.0, 0.98, 0.93), rough=0.75, nstrength=0.8)
    mk("Shutter", (.4, .45, .42), "MetalShutter_Albedo.png", "MetalShutter_Normal.png", (1, 1), tint=(0.55, 0.68, 0.62),
       rough=0.55, metallic=0.35, rough_img="MetalShutter_Mask.png")
    mk("Frame", (.05, .12, .09), rough=0.35)
    mk("Metal", (.03, .03, .035), rough=0.5, metallic=0.4)
    mk("Door", (.25, .09, .07), rough=0.4)
    mk("Interior", (.008, .008, .01), rough=1.0)
    mk("Awning", (.65, .12, .1), rough=0.9)
    mk("Awning_Red", (.6, .1, .08), rough=0.9)
    mk("Awning_Green", (.1, .35, .2), rough=0.9)
    mk("Awning_Blue", (.12, .2, .42), rough=0.9)
    mk("SignPanel", (.07, .2, .13), rough=0.5)
    mk("Concrete", (.6, .6, .58), "ConcreteStained_Albedo.png", "ConcreteStained_Normal.png", (0.5, 0.5), tint=(0.8, 0.77, 0.72), rough=0.9)
    mk("Roof", (.3, .3, .32), "Roof_Albedo.png", "Roof_Normal.png", (0.25, 0.25), tint=(0.42, 0.42, 0.44), rough=0.95)
    mk("Wood", (.5, .35, .2), "Wood_Albedo.png", "Wood_Normal.png", (1, 1), tint=(0.62, 0.45, 0.30), rough=0.8)
    mk("Paint", (.5, .1, .1), rough=0.5)
    mk("WindowInterior", (.3, .3, .3), "WindowInteriors_Albedo.png", None, (1, 1), rough=1.0, emission="WindowInteriors_Emission.png", emit_strength=3.0)
    mk("SideAd", (.7, .7, .6), "SideAds_Albedo.png", None, (1, 1), rough=0.95, albedo_alpha=True)
    g = mk("Glass", (.10, .14, .18), rough=0.02, alpha=0.55)
    g.node_tree.nodes["Principled BSDF"].inputs["Specular IOR Level"].default_value = 0.9
    # glass: tint + strong fresnel-ish reflection from the sky probe
    mk("Asphalt", (.1, .1, .1), "AsphaltWorn_Albedo.png", "AsphaltWorn_Normal.png", (0.25, 0.25), tint=(0.26, 0.25, 0.24), rough=0.9)
    mk("Floor", (.22, .22, .21), rough=0.95)
    mk("Yellow", (.75, .55, .08), rough=0.7)


def setup_world(sun_az=-35, sun_el=38, strength=1.0, haze=0.0):
    sc = bpy.context.scene
    w = bpy.data.worlds.new("SkyWorld")
    w.use_nodes = True
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    sky = nt.nodes.new("ShaderNodeTexSky")
    for k, v in (("sky_type", 'MULTIPLE_SCATTERING'), ("sun_elevation", math.radians(sun_el)),
                 ("sun_rotation", math.radians(sun_az)), ("sun_disc", False), ("air_density", 1.3), ("aerosol_density", 1.6),
                 ("altitude", 20)):
        try:
            setattr(sky, k, v)
        except Exception as e:
            print("sky", k, e)
    bg.inputs["Strength"].default_value = strength
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs["Vector"])
    lt = nt.nodes.new("ShaderNodeMath")
    lt.operation = 'LESS_THAN'
    lt.inputs[1].default_value = -0.01
    nt.links.new(sep.outputs["Z"], lt.inputs[0])
    mxn = nt.nodes.new("ShaderNodeMix")
    mxn.data_type = 'RGBA'
    nt.links.new(lt.outputs["Value"], mxn.inputs["Factor"])
    nt.links.new(sky.outputs["Color"], mxn.inputs["A"])
    mxn.inputs["B"].default_value = (0.42, 0.40, 0.37, 1)
    nt.links.new(mxn.outputs["Result"], bg.inputs["Color"])
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    if haze > 0:
        vs = nt.nodes.new("ShaderNodeVolumeScatter")
        vs.inputs["Density"].default_value = haze
        vs.inputs["Color"].default_value = (1.0, 0.94, 0.85, 1)
        vs.inputs["Anisotropy"].default_value = 0.35
        nt.links.new(vs.outputs["Volume"], out.inputs["Volume"])
    sc.world = w


def add_sun(direction, energy=4.0, color=(1.0, 0.93, 0.80), angle=0.035):
    ld = bpy.data.lights.new("Sun", 'SUN')
    ld.energy = energy
    ld.color = color
    ld.angle = angle
    ob = bpy.data.objects.new("Sun", ld)
    bpy.context.scene.collection.objects.link(ob)
    ob.rotation_euler = (-Vector(direction)).to_track_quat('-Z', 'Y').to_euler()
    return ob


def setup_eevee(res, samples=64, haze=False):
    sc = bpy.context.scene
    try:
        sc.render.engine = 'BLENDER_EEVEE'
    except TypeError:
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    ev = sc.eevee
    for k, v in (("taa_render_samples", samples), ("use_raytracing", True), ("use_shadows", True),
                 ("shadow_ray_count", 3), ("shadow_step_count", 8), ("use_volumetric_shadows", True),
                 ("volumetric_end", 250.0), ("volumetric_start", 0.5), ("volumetric_tile_size", '2'),
                 ("volumetric_samples", 96)):
        try:
            setattr(ev, k, v)
        except Exception as e:
            pass
    try:
        sc.view_settings.view_transform = 'AgX'
        sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    sc.view_settings.exposure = -0.3
    sc.render.film_transparent = False


def clear_extras():
    for o in list(bpy.data.objects):
        if o.name.startswith(("Sun", "cam", "floor", "inst_", "env_")):
            bpy.data.objects.remove(o, do_unlink=True)


# ------------------------------------------------------------------ grading (post, numpy on the saved PNG)
def grade(path, warm=1.0, desat=0.10, lift=0.02, contrast=1.06, depth=None, fog=(0.90, 0.85, 0.76), k=1 / 85.0):
    img = bpy.data.images.load(path)
    W, H = img.size
    a = np.array(img.pixels[:], np.float32).reshape(H, W, 4)
    rgb = a[..., :3]
    if depth is not None:
        sky = depth[..., None] > 380
        t = np.exp(-depth * k)[..., None]
        rgb = rgb * t + np.array(fog)[None, None, :] * (1 - t)
        yy = (np.arange(H)[:, None] / H)[..., None] * np.ones((1, W, 1), np.float32)    # 0 bottom .. 1 top (image rows are bottom-up)
        up = np.clip((yy - 0.45) / 0.55, 0, 1) ** 0.8
        skycol = np.array(fog)[None, None, :] * (1 - up) + np.array([0.50, 0.68, 0.92])[None, None, :] * up
        rgb = np.where(sky, skycol, rgb)
    g = rgb.mean(-1, keepdims=True)
    rgb = g + (rgb - g) * (1 - desat)
    rgb = (rgb - 0.5) * contrast + 0.5
    rgb = rgb * np.array([1.05, 1.0, 0.93]) ** warm + lift * np.array([1.0, 0.8, 0.55])
    a[..., :3] = np.clip(rgb, 0, 1)
    img.pixels.foreach_set(a.ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


# ------------------------------------------------------------------ per-piece previews
def bounds(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


def render_pieces(objs, out_dir, only=None):
    build_materials()
    sc = bpy.context.scene
    setup_eevee((900, 900), 48)
    setup_world(-35, 38, 0.45)
    for name, ob in objs.items():
        if only and name not in only:
            continue
        clear_extras()
        for o in objs.values():
            o.hide_render = o is not ob
        mn, mx = bounds([ob])
        ctr = (mn + mx) / 2
        size = mx - mn
        rad = max(size.length / 2, 0.3)
        add_sun(Vector((0.55, -0.65, 0.55)).normalized(), 2.6)
        # ground plane
        import bmesh
        me = bpy.data.meshes.new("floor")
        b = bmesh.new()
        bmesh.ops.create_grid(b, x_segments=1, y_segments=1, size=rad * 3.0, matrix=Matrix.Translation((ctr.x, ctr.y, min(mn.z, 0.0) - 0.002)))
        b.to_mesh(me); b.free()
        me.materials.append(bpy.data.materials["Floor"])
        fl = bpy.data.objects.new("floor", me)
        sc.collection.objects.link(fl)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        cam.data.lens = 50
        cam.data.clip_end = 500
        fov = 2 * math.atan(18 / 50)
        dist = rad / math.sin(fov / 2) * 1.12
        az, el = (-28, 13) if size.z < 4.5 else (-30, 20)
        if size.x < 0.9 and size.z < 4.5 and size.y < 4.5:
            az, el = (-35, 18)
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
        cam.location = ctr + d * dist
        cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        sc.camera = cam
        sc.render.filepath = os.path.join(out_dir, name + ".png")
        bpy.ops.render.render(write_still=True)
        grade(sc.render.filepath, desat=0.05, lift=0.0, contrast=1.0)
        print("PREVIEW", name)
    for o in objs.values():
        o.hide_render = False
    clear_extras()


# ------------------------------------------------------------------ street assembly
ROW = {"flip": False, "zb": 0.0}


def _z(z):
    return ROW["zb"] + (-z if ROW["flip"] else z)


def inst(src, x, y, z, yaw=0.0, over=None):
    """Place a linked copy at Unity (x, y, z) in the current row; yaw about the vertical axis; per-slot material overrides."""
    o = bpy.data.objects.new("inst_" + src.name, src.data)
    bpy.context.scene.collection.objects.link(o)
    o.location = (x, -_z(z), y)
    o.rotation_euler = (0, 0, yaw + (math.pi if ROW["flip"] else 0.0))
    if over:
        for i, s in enumerate(o.material_slots):
            nm = src.data.materials[i].name if src.data.materials[i] else ""
            if nm in over:
                s.link = 'OBJECT'
                s.material = bpy.data.materials[over[nm]]
    return o


def box_obj(name, x0, x1, y0, y1, z0, z1, matname, row=True):
    """Unity-coordinate box (x, y up, z forward) as a Blender object with one material."""
    import bmesh
    if row:
        z0, z1 = sorted((_z(z0), _z(z1)))
    me = bpy.data.meshes.new(name)
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0, matrix=Matrix.Translation(((x0 + x1) / 2, -(z0 + z1) / 2, (y0 + y1) / 2)) @
                          Matrix.Diagonal((x1 - x0, z1 - z0, y1 - y0, 1)))
    uv = b.loops.layers.uv.verify()
    for f in b.faces:
        ax = max(range(3), key=lambda i: abs(f.normal[i]))
        for l in f.loops:
            p = l.vert.co
            l[uv].uv = ((p.x + 1.5, p.z) if ax == 1 else (p.y, p.z) if ax == 0 else (p.x, p.y))
    b.to_mesh(me); b.free()
    me.materials.append(bpy.data.materials[matname])
    o = bpy.data.objects.new("env_" + name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


WIN_NAMES = ["Flat_BlindsHalf", "Seg_CurtainsRed", "Key_CurtainsBlue", "Hood_CurtainsCream", "Flat_DarkFurniture", "Seg_ACUnit",
             "Hood_PlantGuard", "Key_LampLit", "Flat_EmptyDark", "Seg_BlindsClosedGuard", "Hood_TVRoom", "Key_GreenPlantGuard"]


def window_choice(rng):
    return "V2_Window_" + WIN_NAMES[int(rng.integers(len(WIN_NAMES)))]


def build_building(O, x0, nbays, upper, wall_mat, ground, rng, cornice="bracketed", chimneys=(), bulkhead=False,
                   tower=False, escape_bay=None, awnings=(), depth=12.0, side_left=False, belt_floor=None,
                   awning_mat="Awning", juliet_floor=None, downspout_bay=None, side_right=False):
    ov = {"Wall": wall_mat}
    W = nbays * 3.0
    H = 4.0 + 3.2 * upper
    box_obj("body%d%s" % (int(x0), "f" if ROW["flip"] else ""), x0 + (0.33 if side_left else 0.0), x0 + W - (0.33 if side_right else 0.0),
            0, H, -depth, -0.33, wall_mat)
    box_obj("roof%d%s" % (int(x0), "f" if ROW["flip"] else ""), x0, x0 + W, H - 0.02, H + 0.02, -depth, -0.3, "Roof")
    for i in range(nbays):
        cx = x0 + 1.5 + 3 * i
        inst(O[ground[i % len(ground)]], cx, 0, 0, over=ov)
        if i in awnings:
            inst(O["V2_Awning"], cx, 3.2, 0.03, over={"Awning": awning_mat})
    for f in range(upper):
        y = 4.0 + 3.2 * f
        for i in range(nbays):
            cx = x0 + 1.5 + 3 * i
            if belt_floor is not None and f == belt_floor:
                inst(O["V2_Facade_BeltCourse"], cx, y, 0, over=ov)
            elif juliet_floor is not None and f == juliet_floor and i == nbays // 2:
                inst(O["V2_Facade_Juliet"], cx, y, 0, over=ov)
            else:
                inst(O[window_choice(rng)], cx, y, 0, over=ov)
    inst(O["V2_Corner_Quoin_Ground_L"], x0, 0, 0)
    inst(O["V2_Corner_Quoin_Ground_R"], x0 + W, 0, 0)
    for f in range(upper):
        inst(O["V2_Corner_Quoin_L"], x0, 4.0 + 3.2 * f, 0)
        inst(O["V2_Corner_Quoin_R"], x0 + W, 4.0 + 3.2 * f, 0)
    if escape_bay is not None:
        cx = x0 + 1.5 + 3 * escape_bay
        for f in range(upper - 1):
            inst(O["V2_FireEscape_ZigA" if f % 2 == 0 else "V2_FireEscape_ZigB"], cx, 4.0 + 3.2 * f, 0)
        inst(O["V2_FireEscape_Top"], cx, 4.0 + 3.2 * (upper - 1), 0)
        inst(O["V2_FireEscape_DropLadder"], cx + 1.2, 4.0, 0)
    if downspout_bay is not None:
        cx = x0 + 3 * downspout_bay + 0.1
        inst(O["V2_Pipe_Downspout_Foot"], cx, 0, 0)
        for f in range(upper):
            inst(O["V2_Pipe_Downspout"], cx, 4.0 + 3.2 * f, 0)
    if cornice == "bracketed":
        for i in range(nbays):
            inst(O["V2_Cornice_Bracketed"], x0 + 1.5 + 3 * i, H, 0)
        inst(O["V2_Cornice_Bracketed_Corner_L"], x0, H, 0)
        inst(O["V2_Cornice_Bracketed_Corner_R"], x0 + W, H, 0)
        for i in range(nbays):
            inst(O["V2_Parapet_Coped"], x0 + 1.5 + 3 * i, H + 0.86, 0, over=ov)
    else:
        for i in range(nbays):
            inst(O["V2_Cornice_Corbel"], x0 + 1.5 + 3 * i, H, 0, over=ov)
    for cxp, twin in chimneys:
        inst(O["V2_Roof_Chimney_B" if twin else "V2_Roof_Chimney_A"], x0 + cxp, H, -3.0, over=ov)
    if bulkhead:
        inst(O["V2_Roof_Bulkhead"], x0 + 4.5, H, -5.5)
    if tower:
        inst(O["V2_Roof_WaterTower"], x0 + W - 2.4, H, -4.0)
    return W, H


def side_wall(O, x, wall_mat, floors, ads, zrange=(0, 4)):
    """Side wall of a building at plane x, facing -x (visible from the street), 4 bays deep."""
    for j in range(4):
        zc = -(1.5 + 3 * j)
        inst(O["V2_Side_Blank_Ground"], x, 0, zc, yaw=-math.pi / 2, over={"Wall": wall_mat})
        for f in range(floors):
            nm = ads.get((j, f), "V2_Side_Blank")
            inst(O[nm], x, 4.0 + 3.2 * f, zc, yaw=-math.pi / 2, over={"Wall": wall_mat})
        inst(O["V2_Parapet_Coped"], x, 4.0 + 3.2 * floors, zc, yaw=-math.pi / 2, over={"Wall": wall_mat})


def render_street(O, out_dir):
    build_materials()
    sc = bpy.context.scene
    rng = np.random.default_rng(7)
    clear_extras()
    for o in O.values():
        o.hide_render = True
        o.hide_viewport = True
    shops_a = ["V2_Facade_ShutterShop_Down", "V2_Facade_ShopDoorway", "V2_Facade_ShutterShop_Half", "V2_Facade_ServiceDoor"]
    # --- near side (facade plane z = 0, street at z > 0)
    ROW.update(flip=False, zb=0.0)
    build_building(O, 0.0, 3, 3, "Wall_Red", ["V2_Facade_Stoop"] * 3, rng, "bracketed", chimneys=[(1.5, False), (7.0, False)],
                   side_left=True, juliet_floor=1, downspout_bay=2)
    build_building(O, 9.0, 4, 5, "Wall_Brown", shops_a, rng, "corbel", chimneys=[(6.0, True)], bulkhead=True, escape_bay=1,
                   awnings=(1, 2), awning_mat="Awning_Red", downspout_bay=3)
    build_building(O, 21.0, 3, 3, "Wall_Tan", ["V2_Facade_ShopDoorway", "V2_Facade_ShutterShop_Half", "V2_Facade_ServiceDoor"],
                   rng, "bracketed", tower=True, awnings=(0, 1), awning_mat="Awning_Green")
    build_building(O, 30.0, 3, 4, "Wall_Red", ["V2_Facade_Stoop"] * 3, rng, "corbel", chimneys=[(1.5, True)], escape_bay=2)
    build_building(O, 39.0, 4, 6, "Wall_Tan", ["V2_Facade_ShutterShop_Half", "V2_Facade_ServiceDoor", "V2_Facade_ShopDoorway", "V2_Facade_ShutterShop_Down"],
                   rng, "bracketed", awnings=(0, 2), awning_mat="Awning_Blue", bulkhead=True)
    build_building(O, 51.0, 3, 4, "Wall_Brown", ["V2_Facade_Stoop"] * 3, rng, "bracketed", chimneys=[(5.0, False)])
    build_building(O, 60.0, 5, 3, "Wall_Red", shops_a, rng, "corbel")
    build_building(O, -12.0, 4, 1, "Wall_Brown", ["V2_Facade_ShutterShop_Half", "V2_Facade_ShopDoorway", "V2_Facade_ShutterShop_Down", "V2_Facade_ServiceDoor"],
                   rng, "corbel", awnings=(1,), awning_mat="Awning_Blue", depth=11.9, side_right=True)
    side_wall(O, 0.0, "Wall_Red", 3, {(1, 1): "V2_Side_Ad_Sterling", (2, 2): "V2_Facade_BrickedUp", (3, 0): "V2_Side_Ad_Arden", (0, 2): "V2_Side_Ad_Kestrel"})
    # --- far side across the street (flipped row)
    ROW.update(flip=True, zb=21.5)
    far = [(-12.0, 3, 5, "Wall_Brown", "bracketed"), (-3.0, 4, 4, "Wall_Red", "corbel"), (9.0, 3, 6, "Wall_Tan", "bracketed"),
           (18.0, 4, 4, "Wall_Brown", "corbel"), (30.0, 3, 5, "Wall_Red", "bracketed"), (39.0, 4, 3, "Wall_Tan", "corbel"),
           (51.0, 3, 6, "Wall_Brown", "bracketed"), (60.0, 4, 4, "Wall_Red", "corbel")]
    for k, (fx, nb, up, wm, cor) in enumerate(far):
        build_building(O, fx, nb, up, wm, shops_a if k % 2 == 0 else ["V2_Facade_Stoop"] * 3, rng, cor, escape_bay=(1 if k % 3 == 0 else None),
                       chimneys=([(2.0, False)] if k % 2 else []), awnings=((0, 2) if k % 2 == 0 else ()), awning_mat=["Awning_Red", "Awning_Green", "Awning_Blue"][k % 3],
                       tower=(k == 4), downspout_bay=(nb - 1 if k % 2 else None))
    ROW.update(flip=False, zb=0.0)
    # --- street
    box_obj("sidewalk", -45, 130, -0.6, 0.0, 0.0, 3.4, "Concrete", row=False)
    box_obj("curb", -45, 130, -0.16, 0.0, 3.4, 3.6, "Concrete", row=False)
    box_obj("road", -45, 130, -0.8, -0.16, 3.6, 17.5, "Asphalt", row=False)
    box_obj("curb2", -45, 130, -0.16, 0.0, 17.5, 17.7, "Concrete", row=False)
    box_obj("far_sidewalk", -45, 130, -0.6, 0.0, 17.7, 21.5, "Concrete", row=False)
    box_obj("cl1", -45, 130, -0.158, -0.15, 10.50, 10.58, "Yellow", row=False)
    box_obj("cl2", -45, 130, -0.158, -0.15, 10.72, 10.80, "Yellow", row=False)
    box_obj("ground", -300, 400, -1.2, -0.8, -80, 200, "Asphalt", row=False)
    import_props(sc)
    # --- render
    setup_eevee((1920, 1080), 96)
    setup_world(-35, 36, 0.55)
    add_sun(Vector((0.45, -0.7, 0.55)).normalized(), 3.0)
    shots = [("V2_Street_Assembly", (-26.0, 1.75, 8.0), (30.0, 7.0, -2.0), 28),
             ("V2_Street_Sidewalk", (-4.0, 1.7, 3.0), (22.0, 3.5, -1.0), 26),
             ("V2_Street_Facades", (17.0, 3.0, 15.0), (17.0, 7.5, 0.0), 22)]
    for name, cpos, tgt, lens in shots:
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        sc.collection.objects.link(cam)
        cam.data.lens = lens
        cam.data.clip_end = 800
        cam.location = (cpos[0], -cpos[2], cpos[1])
        d = Vector((tgt[0], -tgt[2], tgt[1])) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.camera = cam
        path = os.path.join(out_dir, name + ".png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        depth = render_depth(sc, os.path.join(bpy.app.tempdir, "depth.exr"))
        grade(path, depth=depth)
        bpy.data.objects.remove(cam, do_unlink=True)
        print("STREET", name)


def render_depth(sc, path):
    """Second pass: camera distance written as emission into a float EXR (used for aerial-perspective haze)."""
    old = (sc.render.engine, sc.render.image_settings.file_format, sc.render.image_settings.color_depth, sc.world,
           sc.view_settings.view_transform, sc.view_settings.look, sc.view_settings.exposure, sc.eevee.taa_render_samples,
           sc.render.filepath)
    m = bpy.data.materials.new("depthmat")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    cd = nt.nodes.new("ShaderNodeCameraData")
    mu = nt.nodes.new("ShaderNodeMath")
    mu.operation = 'MULTIPLY'
    mu.inputs[1].default_value = 1.0 / 400.0
    cm = nt.nodes.new("ShaderNodeCombineXYZ")
    nt.links.new(cd.outputs["View Distance"], mu.inputs[0])
    for k in ("X", "Y", "Z"):
        nt.links.new(mu.outputs["Value"], cm.inputs[k])
    nt.links.new(cm.outputs["Vector"], em.inputs["Color"])
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    w = bpy.data.worlds.new("depthworld")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (1, 1, 1, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    sc.world = w
    sc.view_layers[0].material_override = m
    sc.view_settings.view_transform = 'Standard'
    try:
        sc.view_settings.look = 'None'
    except Exception:
        pass
    sc.view_settings.exposure = 0.0
    sc.eevee.taa_render_samples = 8
    sc.render.image_settings.file_format = 'OPEN_EXR'
    sc.render.image_settings.color_depth = '32'
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    W, H = img.size
    d = np.array(img.pixels[:], np.float32).reshape(H, W, 4)[..., 0] * 400.0
    bpy.data.images.remove(img)
    sc.view_layers[0].material_override = None
    sc.render.engine, sc.render.image_settings.file_format, sc.render.image_settings.color_depth, sc.world = old[:4]
    sc.view_settings.view_transform, sc.view_settings.look, sc.view_settings.exposure, sc.eevee.taa_render_samples, sc.render.filepath = old[4:]
    bpy.data.worlds.remove(w)
    bpy.data.materials.remove(m)
    return d


def import_props(sc):
    """Parked v1 cars + lamps from the existing kit, for scale and density context only."""
    cols = [(0.45, 0.07, 0.06), (0.08, 0.2, 0.35), (0.75, 0.74, 0.7), (0.12, 0.12, 0.14), (0.7, 0.55, 0.15), (0.25, 0.3, 0.25), (0.55, 0.55, 0.58)]
    names = ["Car_Sedan", "Car_Hatchback", "Car_Van"]
    k = 0
    for side, zc, yaw in ((0, 5.3, math.pi / 2), (1, 15.8, -math.pi / 2)):
        x = -13.0 + (3 if side else 0)
        while x < 90:
            nm = names[(k * 5 + side) % 3]
            if rng_skip(k):
                x += 5.2
                k += 1
                continue
            load_car(nm, x, zc, yaw, cols[k % len(cols)])
            x += 5.6 + (k % 3) * 0.3
            k += 1
    for x in (-3.0, 24.0, 55.0):
        load_prop("StreetLamp", x, 3.3, math.pi / 2)
    for x in (4.0, 36.0):
        load_prop("Hydrant", x, 3.1, 0.0)


def rng_skip(k):
    return k % 7 == 3


def _import(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, axis_forward='-Z', axis_up='Y')
    return [o for o in bpy.data.objects if o not in before]


def load_prop(nm, x, z, yaw):
    path = os.path.join(MODELS, "Props", nm + ".fbx")
    if not os.path.exists(path):
        return
    for o in _import(path):
        if o.parent is None:
            o.matrix_world = Matrix.Translation((x, -z, 0)) @ Matrix.Rotation(yaw, 4, 'Z') @ o.matrix_world
        o.name = "env_" + o.name


def load_car(nm, x, z, yaw, col):
    path = os.path.join(MODELS, "Vehicles", nm + ".fbx")
    if not os.path.exists(path):
        return
    for o in _import(path):
        if o.parent is None:
            o.matrix_world = Matrix.Translation((x, -z, 0)) @ Matrix.Rotation(yaw, 4, 'Z') @ o.matrix_world
        o.name = "env_" + o.name
        if o.type == 'MESH':
            for s in o.material_slots:
                n = s.material.name if s.material else ""
                if n.startswith("CarPaint"):
                    m = bpy.data.materials.new("cp")
                    m.use_nodes = True
                    b = m.node_tree.nodes["Principled BSDF"]
                    b.inputs["Base Color"].default_value = (*col, 1)
                    b.inputs["Roughness"].default_value = 0.25
                    s.material = m
                elif n.startswith("Glass"):
                    m = bpy.data.materials.new("cg")
                    m.use_nodes = True
                    b = m.node_tree.nodes["Principled BSDF"]
                    b.inputs["Base Color"].default_value = (0.02, 0.03, 0.04, 1)
                    b.inputs["Roughness"].default_value = 0.05
                    s.material = m
