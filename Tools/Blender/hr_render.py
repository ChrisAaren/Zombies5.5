"""Eevee render helpers for the Char_Human proof renders (soft key, rim light, neutral backdrop)."""
import bpy, math, os
from mathutils import Vector


def setup_scene(size=(900, 900), samples=24, bg=(0.46, 0.48, 0.50)):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE'
    try:
        scn.eevee.taa_render_samples = samples
    except Exception:
        pass
    scn.render.resolution_x, scn.render.resolution_y = size
    scn.render.image_settings.file_format = 'PNG'
    scn.view_settings.view_transform = 'Standard'
    try:
        scn.view_settings.look = 'None'
    except Exception:
        pass
    w = bpy.data.worlds.new("HW")
    w.use_nodes = True
    nt = w.node_tree
    bgn = nt.nodes.get("Background")
    bgn.inputs[0].default_value = (*bg, 1)
    bgn.inputs[1].default_value = 0.9
    scn.world = w
    return scn


def clay(obj, color=(0.55, 0.52, 0.50)):
    m = bpy.data.materials.new("clay")
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = 0.55
    obj.data.materials.clear()
    obj.data.materials.append(m)


def clear_extras():
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA') or o.name.startswith("_floor"):
            bpy.data.objects.remove(o, do_unlink=True)


def add_floor(size=8.0, z=0.0, color=(0.30, 0.31, 0.32)):
    m = bpy.data.meshes.new("_floor")
    m.from_pydata([(-size, -size, z), (size, -size, z), (size, size, z), (-size, size, z)], [], [(0, 1, 2, 3)])
    o = bpy.data.objects.new("_floor", m)
    bpy.context.scene.collection.objects.link(o)
    mt = bpy.data.materials.new("_floorm")
    mt.use_nodes = True
    mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    mt.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
    m.materials.append(mt)
    return o


def lights(target, scale=1.0, az=0.0):
    """Key (soft, upper left of camera), fill, and rim from behind. az: camera azimuth (deg) so lights follow."""
    t = Vector(target)
    a = math.radians(az)

    def rot(v):
        c, s = math.cos(a), math.sin(a)
        return Vector((v[0] * c - v[1] * s, v[0] * s + v[1] * c, v[2]))
    specs = [((-2.2, -2.6, 2.4), 140, 2.2, (1.0, .96, .90)),      # key
             ((2.8, -1.8, 0.8), 40, 3.0, (.8, .88, 1.0)),          # fill
             ((1.6, 2.8, 2.2), 160, 1.2, (1.0, .97, .92))]         # rim
    for loc, e, sz, col in specs:
        d = bpy.data.lights.new("L", 'AREA')
        d.energy = e * scale * scale
        d.size = sz * scale
        d.color = col
        o = bpy.data.objects.new("L", d)
        bpy.context.scene.collection.objects.link(o)
        o.location = t + rot(Vector(loc)) * scale
        o.rotation_euler = (t - o.location).to_track_quat('-Z', 'Y').to_euler()


def camera(loc, target, lens=70, ortho=None):
    cd = bpy.data.cameras.new("C")
    cd.lens = lens
    cd.clip_start, cd.clip_end = 0.05, 100
    if ortho:
        cd.type = 'ORTHO'
        cd.ortho_scale = ortho
    o = bpy.data.objects.new("C", cd)
    bpy.context.scene.collection.objects.link(o)
    o.location = Vector(loc)
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = o
    return o


def shot(path, az, el, dist, target, lens=70, size=(900, 900), floor=True, light_scale=1.0, hide=()):
    """az 0 = camera in front of the character (camera at -Y), 90 = camera at the character's left (+X)."""
    clear_extras()
    scn = setup_scene(size)
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    t = Vector(target)
    camera(t + d * dist, t, lens)
    lights(t, light_scale * max(0.5, dist / 4.5) ** 0.5, az)
    fl = add_floor() if floor else None
    for o in hide:
        o.hide_render = True
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in hide:
        o.hide_render = False
    clear_extras()
    if fl:
        pass


def wire_copy(obj, thickness=0.0006, color=(0.02, 0.02, 0.02)):
    """A wireframe overlay object (for topology proof renders)."""
    o2 = obj.copy(); o2.data = obj.data.copy()
    bpy.context.scene.collection.objects.link(o2)
    m = o2.modifiers.new("w", 'WIREFRAME'); m.thickness = thickness; m.use_replace = True
    mt = bpy.data.materials.new("wm"); mt.use_nodes = True
    mt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*color, 1)
    o2.data.materials.clear(); o2.data.materials.append(mt)
    return o2
