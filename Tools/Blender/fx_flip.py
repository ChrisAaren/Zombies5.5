"""Mantaflow bake + Cycles render of explosion flipbook frames (helper for make_explosion_fx.py).
Runs inside Blender. Steps are separate so the shading can be re-tuned without re-baking:
  bake(kind)   -> staging/explosions/_work/<kind>.blend + VDB cache
  render(kind) -> _work/<kind>_frames/f####.exr  (premultiplied RGBA float, transparent film)
"""
import bpy, os, math, random, time, shutil
import numpy as np
from mathutils import Vector

WORK = r"E:\Unity\Zombies5.5\Tools\Blender\staging\explosions\_work"
FPS = 30
NOISE_SCALE = {"fireball": 9.0, "smoke": 14.0, "fire": 30.0}
_MAP = None

# domain box (m): centre + edge. Camera = fixed ortho camera looking along +Y at cam_c, frame width cam_w.
CFG = {
    "fireball": dict(dom_c=(0, 0, 5.3), dom_s=12.0, cam_c=(0, 0, 4.3), cam_w=10.0, res=112, frames=64, res_noise=2),
    "smoke":    dict(dom_c=(0, 0, 5.3), dom_s=12.0, cam_c=(0, 0, 4.3), cam_w=10.0, res=112, frames=64, res_noise=2),
    "fire":     dict(dom_c=(0, 0, 1.55), dom_s=3.6, cam_c=(0, 0, 1.45), cam_w=3.2, res=96, frames=64, res_noise=2, warm=48, extra=64),
}
RES_OVERRIDE = {}
SC = 1.25


def blend_path(kind): return os.path.join(WORK, kind + ".blend")
def cache_dir(kind): return os.path.join(WORK, kind + "_cache")
def frames_dir(kind): return os.path.join(WORK, kind + "_frames")


def _mesh_obj(name, loc, scale, shape="ico"):
    if shape == "ico":
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=1, location=loc)
    elif shape == "plane":
        bpy.ops.mesh.primitive_plane_add(size=1, location=loc)
    elif shape == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=1, depth=1, location=loc)
    o = bpy.context.object
    o.name = name
    o.scale = scale
    return o


def _lumpy(o, amp=.38, seed=0, freq=1.3):
    """displace an icosphere emitter with fractal noise (no clean spherical shell)"""
    from mathutils import noise as mn
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='OBJECT')
    me = o.data
    off = Vector((seed * 7.1, seed * 3.3, seed * 5.9))
    for v in me.vertices:
        n = v.co.normalized()
        k = 1 + amp * mn.fractal(n * freq + off, 1.0, 2.0, 4)
        v.co = n * k
    me.update()


_TEX = {}


def _noise_tex(scale):
    if scale not in _TEX:
        t = bpy.data.textures.new("emit_noise_%s" % scale, type='CLOUDS')
        t.noise_scale = scale; t.noise_depth = 3; t.noise_basis = 'ORIGINAL_PERLIN'
        _TEX[scale] = t
    return _TEX[scale]


def _flow(o, ftype, fuel=1.0, dens=1.0, vnormal=0.0, temp=1.0, color=(.05, .05, .05), surf=1.0, vrand=0.0, tex=0.0):
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_add(type='FLUID')
    m = o.modifiers[-1]; m.fluid_type = 'FLOW'
    f = m.flow_settings
    f.flow_type = ftype; f.flow_behavior = 'INFLOW'; f.flow_source = 'MESH'
    f.fuel_amount = fuel; f.density = dens; f.temperature = temp; f.smoke_color = color
    f.surface_distance = surf
    f.volume_density = 1.0
    if vnormal:
        f.use_initial_velocity = True; f.velocity_normal = vnormal; f.velocity_random = vrand
    if tex:
        f.use_texture = True; f.texture_map_type = 'AUTO'; f.noise_texture = _noise_tex(tex); f.texture_size = tex; f.texture_offset = 0.0
    return f


def _window(f, start, end):
    """inflow active on frames [start, end)"""
    for fr, v in ((1, start <= 1), (max(start, 1), True), (end, False)):
        f.use_inflow = v
        f.keyframe_insert("use_inflow", frame=fr)


def _ground(size):
    g = _mesh_obj("Ground", (0, 0, 0), (size, size, 1), "plane")
    bpy.context.view_layer.objects.active = g
    bpy.ops.object.modifier_add(type='FLUID'); g.modifiers[-1].fluid_type = 'EFFECTOR'


def build_sim(kind):
    c = CFG[kind]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    scn.render.fps = FPS
    scn.frame_start = 1
    total = c["frames"] + c.get("warm", 0) + c.get("extra", 0)
    scn.frame_end = total
    bpy.ops.mesh.primitive_cube_add(size=1, location=c["dom_c"])
    dom = bpy.context.object; dom.name = "Domain"; dom.scale = (c["dom_s"],) * 3
    bpy.ops.object.modifier_add(type='FLUID')
    m = dom.modifiers[-1]; m.fluid_type = 'DOMAIN'
    ds = m.domain_settings
    ds.domain_type = 'GAS'
    ds.resolution_max = RES_OVERRIDE.get(kind, c["res"])
    ds.use_noise = False
    ds.cache_type = 'ALL'
    ds.cache_data_format = 'OPENVDB'
    ds.cache_noise_format = 'OPENVDB'
    ds.cache_directory = cache_dir(kind)
    ds.cache_frame_start = 1; ds.cache_frame_end = total
    ds.use_adaptive_timesteps = True
    ds.time_scale = {"fireball": 1.25, "smoke": 1.6, "fire": 1.0}[kind]
    ds.timesteps_max = 6
    for s in ("front", "back", "left", "right", "top"):
        setattr(ds, "use_collision_border_" + s, False)
    ds.use_collision_border_bottom = True
    random.seed(7)
    if kind == "fireball":
        ds.alpha = 0.9; ds.beta = 5.5          # density / heat buoyancy
        ds.vorticity = 1.2
        ds.use_dissolve_smoke = True; ds.dissolve_speed = 24; ds.use_dissolve_smoke_log = True
        ds.burning_rate = 0.34; ds.flame_smoke = 6.0; ds.flame_vorticity = 1.2
        ds.flame_smoke_color = (.10, .09, .08); ds.flame_max_temp = 3.0; ds.flame_ignition = 1.2
        _ground(12.0)
        rg = random.Random(21)
        blobs = [((0, 0, 1.2), 1.45, 1, 3.4, 1.8)] + \
                [((rg.uniform(-.9, .9), rg.uniform(-.4, .4), .55 + .17 * k + rg.uniform(0, .3)), rg.uniform(.7, 1.0), 1 + 2 * k, 2.3 - .1 * k, 1.1) for k in range(1, 7)]
        for i, (loc, r, start, tmp_, fuel) in enumerate(blobs):
            loc = tuple(v * SC for v in loc); r *= SC
            o = _mesh_obj("Blob%d" % i, loc, (r * rg.uniform(.85, 1.15), r * rg.uniform(.85, 1.15), r * rg.uniform(.85, 1.15)))
            _lumpy(o, .38, i + 1)
            f = _flow(o, "BOTH", fuel=fuel, dens=2.0, vnormal=2.2, temp=tmp_, color=(.10, .09, .08), vrand=1.5, tex=.7)
            _window(f, start, start + 3)
    elif kind == "smoke":
        ds.alpha = 0.8; ds.beta = 6.0
        ds.vorticity = 1.6
        ds.use_dissolve_smoke = True; ds.dissolve_speed = 30; ds.use_dissolve_smoke_log = True
        ds.noise_pos_scale = 1.2; ds.noise_strength = 1.2
        _ground(12.0)
        blobs = [((0, 0, 0.9), 1.0, 1)] + [((random.uniform(-1.0, 1.0), random.uniform(-.4, .4), random.uniform(.6, 1.8)), random.uniform(.5, .8), random.randint(2, 9)) for _ in range(6)]
        for i, (loc, r, start) in enumerate(blobs):
            loc = tuple(v * 1.7 for v in loc); r *= 1.7
            o = _mesh_obj("Blob%d" % i, loc, (r, r, r))
            f = _flow(o, 'SMOKE', dens=1.3, vnormal=5.0, temp=7.0, color=(.55, .55, .55), vrand=0.6)
            _window(f, start, start + 5)
    elif kind == "fire":
        ds.alpha = 0.1; ds.beta = 2.2
        ds.vorticity = 0.35
        ds.use_dissolve_smoke = True; ds.dissolve_speed = 35; ds.use_dissolve_smoke_log = True
        ds.burning_rate = 0.9; ds.flame_smoke = 0.12; ds.flame_vorticity = 0.9
        ds.flame_smoke_color = (.04, .04, .04); ds.flame_max_temp = 3.0; ds.flame_ignition = 1.5
        ds.noise_pos_scale = 1.0; ds.noise_strength = 1.4
        ds.use_collision_border_bottom = False
        for i, (loc, r) in enumerate((((0, 0, 0.12), .42), ((.22, 0, .1), .22), ((-.25, 0, .1), .2))):
            o = _mesh_obj("Src%d" % i, loc, (r, r * .8, .12), "ico")
            _flow(o, 'FIRE', fuel=1.0, dens=1.0, temp=1.9)
    return dom, total


def bake(kind):
    t0 = time.time()
    dom, total = build_sim(kind)
    d = cache_dir(kind)
    shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path(kind))
    bpy.context.view_layer.objects.active = dom
    dom.select_set(True)
    bpy.ops.fluid.bake_data()
    t1 = time.time()
    print("BAKE-DATA %s: %.1fs" % (kind, t1 - t0))
    bpy.ops.wm.save_as_mainfile(filepath=blend_path(kind))
    print("BAKE %s total: %.1fs (noise %.1fs)" % (kind, time.time() - t0, time.time() - t1))


# ------------------------------------------------------------------ render
FIRE_RAMP = [  # flame 0..1 -> emission colour (linear), blackbody-ish
    (0.00, (0.0, 0.0, 0.0)), (0.12, (.28, .015, .0)), (0.30, (1.0, .10, .01)), (0.55, (1.0, .32, .03)),
    (0.78, (1.0, .62, .16)), (1.0, (1.0, .92, .62))]


def setup_render(kind, res, samples, fire_gain=1.0, absorb_gain=None):
    c = CFG[kind]
    if absorb_gain is None:
        absorb_gain = {"fireball": 2.0, "smoke": 1.0, "fire": 1.0}[kind]
    scn = bpy.context.scene
    scn.render.engine = 'CYCLES'
    cy = scn.cycles
    cy.device = 'CPU'; cy.samples = samples; cy.use_denoising = False
    cy.volume_bounces = 3 if kind != "fire" else 0
    cy.max_bounces = 4; cy.volume_step_rate = 0.5; cy.volume_max_steps = 512
    cy.transparent_max_bounces = 8
    scn.render.film_transparent = True
    scn.render.resolution_x = scn.render.resolution_y = res
    scn.render.resolution_percentage = 100
    scn.render.image_settings.file_format = 'OPEN_EXR'
    scn.render.image_settings.color_depth = '32'
    scn.render.image_settings.color_mode = 'RGBA'
    scn.view_settings.view_transform = 'Standard'
    for o in list(bpy.data.objects):
        if o.type in ('CAMERA', 'LIGHT'):
            bpy.data.objects.remove(o)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scn.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = c["cam_w"]; cam.data.clip_end = 100
    cc = Vector(c["cam_c"])
    cam.location = (cc.x, cc.y - 30, cc.z); cam.rotation_euler = (math.radians(90), 0, 0)
    scn.camera = cam
    for o in bpy.data.objects:
        if o.name != "Domain" and o.type == 'MESH':
            o.hide_render = True
    dom = bpy.data.objects["Domain"]
    w = bpy.data.worlds.new("W"); scn.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    if kind in ("smoke", "fireball"):
        bg.inputs[0].default_value = (.5, .52, .56, 1); bg.inputs[1].default_value = 1.0
        sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); scn.collection.objects.link(sun)
        sun.data.energy = 8.0 if kind == "smoke" else 4.0; sun.data.angle = math.radians(12)
        sun.rotation_euler = (math.radians(32), math.radians(10), math.radians(-18))
        sun.data.color = (1.0, .96, .9)
    else:
        bg.inputs[0].default_value = (.5, .46, .42, 1); bg.inputs[1].default_value = 0.6
    mt = bpy.data.materials.new("FXVol"); mt.use_nodes = True
    nt = mt.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")

    def attr(name):
        a = nt.nodes.new("ShaderNodeAttribute"); a.attribute_name = name; return a
    flame = attr("flame"); dens = attr("density"); colr = attr("color")
    L = nt.links.new
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping"); L(tc.outputs["Object"], mp.inputs["Vector"])
    nz = nt.nodes.new("ShaderNodeTexNoise"); nz.noise_dimensions = '3D'; nz.inputs["Detail"].default_value = 4.0
    nz.inputs["Roughness"].default_value = 0.62; nz.inputs["Scale"].default_value = NOISE_SCALE.get(kind, 26.0)
    L(mp.outputs[0], nz.inputs["Vector"])
    global _MAP
    _MAP = mp
    def contrast(src, lo, hi, a, b):
        mr = nt.nodes.new("ShaderNodeMapRange"); mr.clamp = True
        mr.inputs[1].default_value = lo; mr.inputs[2].default_value = hi; mr.inputs[3].default_value = a; mr.inputs[4].default_value = b
        L(nz.outputs["Fac"], mr.inputs[0]); r = nt.nodes.new("ShaderNodeMath"); r.operation = 'MULTIPLY'
        L(src, r.inputs[0]); L(mr.outputs[0], r.inputs[1]); return r.outputs[0]
    if kind in ("fireball", "fire"):
        mapr = nt.nodes.new("ShaderNodeMapRange"); mapr.clamp = True
        L(contrast(flame.outputs["Fac"], .28, .78, .55, 1.3), mapr.inputs[0])
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = FIRE_RAMP[0][0]; ramp.color_ramp.elements[0].color = (*FIRE_RAMP[0][1], 1)
        ramp.color_ramp.elements[1].position = FIRE_RAMP[-1][0]; ramp.color_ramp.elements[1].color = (*FIRE_RAMP[-1][1], 1)
        for p, cl in FIRE_RAMP[1:-1]:
            e = ramp.color_ramp.elements.new(p); e.color = (*cl, 1)
        L(mapr.outputs[0], ramp.inputs[0])
        em = nt.nodes.new("ShaderNodeEmission")
        pw = nt.nodes.new("ShaderNodeMath"); pw.operation = 'POWER'; pw.inputs[1].default_value = 1.4
        L(contrast(flame.outputs["Fac"], .28, .78, .55, 1.3), pw.inputs[0])
        mm = nt.nodes.new("ShaderNodeMath"); mm.operation = 'MULTIPLY'; mm.inputs[1].default_value = 28.0 * fire_gain
        L(pw.outputs[0], mm.inputs[0])
        L(ramp.outputs[0], em.inputs[0]); L(mm.outputs[0], em.inputs[1])
        ab = nt.nodes.new("ShaderNodeVolumeAbsorption")
        dm = nt.nodes.new("ShaderNodeMath"); dm.operation = 'MULTIPLY'; dm.inputs[1].default_value = 3.0 * absorb_gain
        L(contrast(dens.outputs["Fac"], .28, .78, .45, 1.5), dm.inputs[0])
        fm = nt.nodes.new("ShaderNodeMath"); fm.operation = 'MULTIPLY'; fm.inputs[1].default_value = 22.0
        L(contrast(flame.outputs["Fac"], .28, .78, .55, 1.3), fm.inputs[0])
        sm = nt.nodes.new("ShaderNodeMath"); sm.operation = 'ADD'
        L(dm.outputs[0], sm.inputs[0]); L(fm.outputs[0], sm.inputs[1])
        L(sm.outputs[0], ab.inputs["Density"])
        ab.inputs["Color"].default_value = (.55, .52, .5, 1)
        sc = nt.nodes.new("ShaderNodeVolumeScatter"); sc.inputs["Color"].default_value = (.24, .22, .20, 1)
        L(dm.outputs[0], sc.inputs["Density"])
        add = nt.nodes.new("ShaderNodeAddShader"); add2 = nt.nodes.new("ShaderNodeAddShader")
        L(em.outputs[0], add.inputs[0]); L(ab.outputs[0], add.inputs[1])
        L(add.outputs[0], add2.inputs[0]); L(sc.outputs[0], add2.inputs[1])
        L(add2.outputs[0], out.inputs["Volume"])
    else:
        pv = nt.nodes.new("ShaderNodeVolumePrincipled")
        pv.inputs["Color"].default_value = (.82, .82, .84, 1)
        dd = contrast(dens.outputs["Fac"], .30, .65, .25, 1.8)
        dg = nt.nodes.new("ShaderNodeMath"); dg.operation = 'MULTIPLY'; dg.inputs[1].default_value = 6.0 * absorb_gain
        L(dd, dg.inputs[0]); L(dg.outputs[0], pv.inputs["Density"])
        pv.inputs["Anisotropy"].default_value = 0.35
        L(pv.outputs[0], out.inputs["Volume"])
    dom.data.materials.clear()
    dom.data.materials.append(mt)
    dom.hide_render = False
    return cam


def render(kind, res=512, samples=64, frames=None, **kw):
    t0 = time.time()
    bpy.ops.wm.open_mainfile(filepath=blend_path(kind))
    setup_render(kind, res, samples, **kw)
    c = CFG[kind]
    fd = frames_dir(kind); os.makedirs(fd, exist_ok=True)
    rng = frames if frames is not None else range(1, c["frames"] + c.get("warm", 0) + c.get("extra", 0) + 1)
    scn = bpy.context.scene
    for fr in rng:
        if kind == "fire" and fr <= c["warm"]:
            continue
        scn.frame_set(fr)
        _MAP.inputs['Location'].default_value = (0, 0, -0.012 * fr)
        scn.render.filepath = os.path.join(fd, "f%04d.exr" % fr)
        bpy.ops.render.render(write_still=True)
    print("RENDER %s: %.1fs" % (kind, time.time() - t0))
