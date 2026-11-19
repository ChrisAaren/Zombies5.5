"""Shared helpers for the V2 street-asset scripts (trees, vehicles, street furniture).

- numpy texture tools (tileable FFT noise, pure-python PNG writer, text via Blender render)
- P2: Part subclass that can keep hand-made UVs and add a 2nd UV map
- Eevee preview material styling (by slot name) + scene/world/sun setup + render helper
Run inside Blender (headless). Imports bl_common.
"""
import sys, os, math, struct, zlib, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *
import numpy as np

TEX = os.path.join(ROOT, "Assets", "Art", "Textures")
V2_NATURE, V2_VEH, V2_PROPS = "Nature/V2", "Vehicles/V2", "Props/V2"


# ------------------------------------------------------------------ png / noise
def write_png(path, a):
    """a: float 0..1 or uint8 array (H, W, 3|4), row 0 = top."""
    if a.dtype != np.uint8:
        a = (np.clip(a, 0, 1) * 255 + .5).astype(np.uint8)
    h, w, c = a.shape
    ct = 6 if c == 4 else 2
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    ensure_dir(os.path.dirname(path))
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ct, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))
    print("TEXTURE", path, a.shape)


def noise2d(n, beta=2.0, seed=0, aniso=(1.0, 1.0), m=None, sc=8.0):
    """Tileable fractal noise 0..1 (FFT filtered white noise). aniso stretches features (x, y)."""
    rng = np.random.default_rng(seed)
    m = m or n
    F = np.fft.fft2(rng.standard_normal((m, n)))
    fy = np.fft.fftfreq(m)[:, None] * m / sc * aniso[1]
    fx = np.fft.fftfreq(n)[None, :] * n / sc * aniso[0]
    r = np.sqrt(fx * fx + fy * fy)
    r[0, 0] = 1.0
    F = F / r ** (beta / 2.0 + .5)
    F[0, 0] = 0
    a = np.fft.ifft2(F).real
    a = (a - a.min()) / (a.max() - a.min() + 1e-9)
    return a


def blur(a, k=1):
    for _ in range(k):
        a = (a + np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1)) / 5.0
    return a


def normal_from_height(h, strength=2.0):
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * strength
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * strength   # row 0 = top => +gy points down
    n = np.stack([-gx, gy, np.ones_like(h)], -1)              # OpenGL: +Y up
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * .5 + .5


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-9), 0, 1)
    return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ text through Blender
_FONT = None


def text_mask(txt, w, h, fill=0.86, font="C:/Windows/Fonts/arialbd.ttf", line_spacing=1.0):
    """Render txt (may contain \n) fitted into a w x h pixel box: float mask (h, w), row 0 = top.
    Uses the current scene (call it from a texture pass, then reset_scene())."""
    global _FONT
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.render.resolution_x, scn.render.resolution_y, scn.render.resolution_percentage = w, h, 100
    scn.display.render_aa = '8'
    sh = scn.display.shading
    sh.light = 'FLAT'; sh.color_type = 'SINGLE'; sh.single_color = (1, 1, 1)
    scn.view_settings.view_transform = 'Standard'
    scn.view_settings.look = 'None'
    scn.render.image_settings.file_format = 'PNG'
    scn.render.image_settings.color_mode = 'RGBA'
    wd = bpy.data.worlds.new("tw"); wd.color = (0, 0, 0); scn.world = wd
    cu = bpy.data.curves.new("t", 'FONT')
    cu.body = txt; cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.space_line = line_spacing
    if _FONT is None:
        try:
            _FONT = bpy.data.fonts.load(font)
        except Exception:
            _FONT = False
    if _FONT:
        cu.font = cu.font_bold = cu.font_italic = cu.font_bold_italic = _FONT
    ob = bpy.data.objects.new("t", cu); scn.collection.objects.link(ob)
    bpy.context.view_layer.update()
    d = ob.dimensions
    s = min(w * fill / max(d.x, 1e-6), h * fill / max(d.y, 1e-6))
    ob.scale = (s, s, s)
    bpy.context.view_layer.update()
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    ob.location.x -= (min(p.x for p in bb) + max(p.x for p in bb)) / 2
    ob.location.y -= (min(p.y for p in bb) + max(p.y for p in bb)) / 2
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); scn.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = max(w, h)
    cam.location = (0, 0, 10); scn.camera = cam
    path = os.path.join(os.environ.get("TEMP", "."), "_txt.png")
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1, :, 0].copy()
    bpy.data.images.remove(im)
    bpy.data.objects.remove(ob); bpy.data.objects.remove(cam)
    return a


# ------------------------------------------------------------------ Part with hand UVs
class P2(Part):
    def uvl(self):
        return self.bm.loops.layers.uv.verify()

    def build2(self, name=None, smooth=35, flat=(), keep_uv=False, uvfix=None, uv2=None):
        """Like Part.build; keep_uv skips the world-meter projection (faces must carry UVs),
        uvfix(bm) runs after projection; uv2(bm, layer) fills a second UV map named UVDirt."""
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        if not keep_uv:
            project_uvs(bm)
        else:
            bm.loops.layers.uv.verify()
        if uvfix:
            uvfix(bm)
        if uv2:
            uv2(bm, bm.loops.layers.uv.new("UVDirt"))
        name = name or self.name
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for s in self.slots:
            me.materials.append(mat(s))
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        select_only(ob)
        for p in me.polygons:
            p.use_smooth = True
        if smooth:
            bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
        if flat:
            idx = {i for i, s in enumerate(self.slots) if s in flat}
            for p in me.polygons:
                if p.material_index in idx:
                    p.use_smooth = False
        return ob


def clear_objects():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)


def export2(objs, sub, name):
    export_fbx(objs, sub, name)


# ------------------------------------------------------------------ preview materials (Eevee)
_img = {}


def img(name, cs='sRGB'):
    p = os.path.join(TEX, name)
    try:
        ok = name in _img and _img[name].name in bpy.data.images
    except ReferenceError:
        ok = False
    if not ok:
        im = bpy.data.images.load(p)
        im.colorspace_settings.name = cs
        _img[name] = im
    return _img[name]


def _principled(m):
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_MATERIAL':
            nt.nodes.remove(n)
    out = nt.nodes["Material Output"]
    b = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(b.outputs[0], out.inputs[0])
    return nt, b


def _set(b, k, v):
    try:
        b.inputs[k].default_value = v
    except Exception:
        pass


def style_mat(slot, color=None, uvmap=None, cell=None):
    """Return a rich Eevee material for a contract slot (preview only; never exported)."""
    key = "PV_" + slot + ("_%s" % (color,) if color else "") + ("_%s" % (cell,) if cell else "")
    m = bpy.data.materials.get(key)
    if m:
        return m
    m = bpy.data.materials.new(key)
    nt, b = _principled(m)
    base = COLORS.get(slot, (.5, .5, .5))
    rough, metal = .6, 0.0
    if slot == "CarPaint":
        base = tuple(c ** 2.2 for c in (color or base))
        _set(b, "Coat Weight", 1.0); _set(b, "Coat Roughness", .03); rough, metal = .32, .35
    elif slot == "Glass":
        base = (.02, .028, .032); rough = .04
        _set(b, "Alpha", .3); _set(b, "Specular IOR Level", 1.0)
    elif slot == "Chrome":
        base = (.85, .86, .88); rough, metal = .1, 1.0
    elif slot == "Rubber":
        base = (.025, .025, .028); rough = .78
        m.use_backface_culling = True
    elif slot == "Interior":
        base = (.12, .105, .09); rough = .8
    elif slot == "Headlight":
        base = (.85, .85, .8); rough = .05; _set(b, "Emission Color", (1, .9, .7)); _set(b, "Emission Strength", .35)
    elif slot == "Taillight":
        base = (.25, .008, .006); rough = .3; _set(b, "Emission Color", (1, .03, .01)); _set(b, "Emission Strength", .25)
    elif slot in ("SignalRed", "SignalAmber", "SignalGreen"):
        col = {"SignalRed": (1, .05, .03), "SignalAmber": (1, .55, .05), "SignalGreen": (.1, 1, .4)}[slot]
        base = tuple(c * .25 for c in col); rough = .15; _set(b, "Emission Color", col); _set(b, "Emission Strength", .7)
    elif slot == "LampGlass":
        base = (.9, .9, .85); rough = .1; _set(b, "Emission Color", (1, .92, .7)); _set(b, "Emission Strength", .8)
    elif slot == "Metal":
        base = (.1, .11, .12); rough, metal = .45, .7
    elif slot == "Paint":
        base = (.7, .55, .05); rough = .5
    elif slot == "Hazard":
        base = (.8, .65, .05); rough = .4
    elif slot == "Concrete":
        base = (.55, .54, .5); rough = .9
    elif slot == "Wood":
        base = (.35, .24, .14)
    elif slot == "KioskScreen":
        base = (.1, .3, .2); _set(b, "Emission Color", (.3, 1, .6)); _set(b, "Emission Strength", .6)
    if slot in ("Bark", "LeafCard", "Plate", "SignFace"):
        tex = nt.nodes.new("ShaderNodeTexImage")
        uvn = nt.nodes.new("ShaderNodeUVMap")
        nt.links.new(uvn.outputs[0], tex.inputs[0])
        if slot == "Bark":
            tex.image = img("BarkV2_Albedo.png")
            mixc = nt.nodes.new("ShaderNodeMix"); mixc.data_type = 'RGBA'; mixc.blend_type = 'MULTIPLY'
            mixc.inputs[0].default_value = 1.0
            nt.links.new(tex.outputs[0], mixc.inputs[6]); mixc.inputs[7].default_value = (.82, .72, .6, 1)
            nt.links.new(mixc.outputs[2], b.inputs["Base Color"])
            nrm = nt.nodes.new("ShaderNodeNormalMap"); t2 = nt.nodes.new("ShaderNodeTexImage")
            t2.image = img("BarkV2_Normal.png", 'Non-Color'); nrm.inputs[0].default_value = 1.2
            nt.links.new(uvn.outputs[0], t2.inputs[0]); nt.links.new(t2.outputs[0], nrm.inputs[1])
            nt.links.new(nrm.outputs[0], b.inputs["Normal"])
            _set(b, "Roughness", .9)
        elif slot == "LeafCard":
            tex.image = img("LeafCards_Albedo.png")
            nt.links.new(tex.outputs[0], b.inputs["Base Color"])
            nt.links.new(tex.outputs[1], b.inputs["Alpha"])
            nt.links.new(tex.outputs[0], b.inputs["Emission Color"]); _set(b, "Emission Strength", .12)
            nrm = nt.nodes.new("ShaderNodeNormalMap"); t2 = nt.nodes.new("ShaderNodeTexImage")
            t2.image = img("LeafCards_Normal.png", 'Non-Color')
            nt.links.new(uvn.outputs[0], t2.inputs[0]); nt.links.new(t2.outputs[0], nrm.inputs[1])
            nt.links.new(nrm.outputs[0], b.inputs["Normal"])
            _set(b, "Roughness", .55)
            m.use_backface_culling = False
        elif slot == "Plate":
            tex.image = img("PlatesV2_Albedo.png"); nt.links.new(tex.outputs[0], b.inputs["Base Color"]); _set(b, "Roughness", .35)
        elif slot == "SignFace":
            tex.image = img("StreetSigns_Albedo.png"); nt.links.new(tex.outputs[0], b.inputs["Base Color"]); _set(b, "Roughness", .3)
            _set(b, "Metallic", .0)
    else:
        _set(b, "Base Color", (*base, 1)); _set(b, "Roughness", rough); _set(b, "Metallic", metal)
    if slot == "Glass":
        m.surface_render_method = 'BLENDED' if hasattr(m, "surface_render_method") else None
    return m


def style_object(ob, car_color=None, flat_slots=()):
    for i, s in enumerate(ob.data.materials):
        if s is None:
            continue
        name = s.name.split(".")[0]
        if name.startswith("PV_"):
            continue
        ob.data.materials[i] = style_mat(name, car_color if name == "CarPaint" else None)


# ------------------------------------------------------------------ world / render
def setup_render(size, samples=48, exposure=-.5, sun_elev=42, sun_az=-35, sun_strength=2.6, haze=0.0,
                 sky_tint=(1, 1, 1)):
    scn = bpy.context.scene
    for e in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scn.render.engine = e; break
        except Exception:
            pass
    scn.render.resolution_x, scn.render.resolution_y = size
    scn.render.resolution_percentage = 100
    scn.render.image_settings.file_format = 'PNG'
    ev = scn.eevee
    try: ev.taa_render_samples = samples
    except Exception: pass
    for k, v in (("use_raytracing", True), ("use_shadows", True), ("shadow_ray_count", 2), ("shadow_step_count", 8),
                 ("use_volumetric_shadows", False)):
        try: setattr(ev, k, v)
        except Exception: pass
    try:
        scn.view_settings.view_transform = 'AgX'
        scn.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        scn.view_settings.view_transform = 'Standard'
    scn.view_settings.exposure = exposure
    w = bpy.data.worlds.new("SkyW"); w.use_nodes = True
    nt = w.node_tree
    for n in list(nt.nodes):
        if n.type != 'OUTPUT_WORLD':
            nt.nodes.remove(n)
    bg = nt.nodes.new("ShaderNodeBackground")
    tc = nt.nodes.new("ShaderNodeTexCoord"); sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs[0])
    nrmz = nt.nodes.new("ShaderNodeMapRange"); nrmz.inputs[1].default_value = 0.0; nrmz.inputs[2].default_value = .9
    nrmz.clamp = True
    nt.links.new(sep.outputs[2], nrmz.inputs[0])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (.95 * sky_tint[0], .90 * sky_tint[1], .80 * sky_tint[2], 1)
    ramp.color_ramp.elements[1].color = (.36, .52, .80, 1)
    nt.links.new(nrmz.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], bg.inputs[0]); bg.inputs[1].default_value = 1.1
    nt.links.new(bg.outputs[0], nt.nodes["World Output"].inputs[0])
    scn.world = w
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN'))
    scn.collection.objects.link(sun)
    sun.data.energy = sun_strength; sun.data.angle = math.radians(1.6); sun.data.color = (1.0, .93, .82)
    el, az = math.radians(sun_elev), math.radians(sun_az)
    d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))   # toward the sun
    sun.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    return scn


def look_cam(loc, target, lens=50, name="cam", scn=None):
    scn = scn or bpy.context.scene
    cam = bpy.data.objects.new(name, bpy.data.cameras.new(name)); scn.collection.objects.link(cam)
    cam.data.lens = lens; cam.data.clip_end = 400
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    scn.camera = cam
    return cam


def textured_ground(size=30, kind="asphalt", z=0.0, center=(0, 0), tile=4.0):
    m = bpy.data.materials.new("PVG_" + kind)
    nt, b = _principled(m)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img({"asphalt": "AsphaltWorn_Albedo.png", "concrete": "ConcreteStained_Albedo.png",
                     "brick": "BrickWorn_Albedo.png"}[kind])
    nt.links.new(tex.outputs[0], b.inputs["Base Color"])
    mp = nt.nodes.new("ShaderNodeMapping"); uvn = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(uvn.outputs["UV"], mp.inputs[0]); nt.links.new(mp.outputs[0], tex.inputs[0])
    _set(b, "Roughness", .85)
    me = bpy.data.meshes.new("g"); bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=size / 2)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = (l.vert.co.x / tile, l.vert.co.y / tile)
    for v in bm.verts:
        v.co.x += center[0]; v.co.y += center[1]; v.co.z = z
    bm.to_mesh(me); bm.free(); me.materials.append(m)
    ob = bpy.data.objects.new("ground", me); bpy.context.scene.collection.objects.link(ob)
    return ob


def render_item(objs, path, az=35, el=16, size=(900, 700), car_color=None, ground="asphalt", exposure=-.5,
                fit=1.0, lens=50, sun_az=-35):
    """Eevee beauty render of the given objects on a textured ground (everything else hidden)."""
    scn = bpy.context.scene
    for o in objs:
        style_object(o, car_color)
    keep = set(objs)
    for o in scn.objects:
        o.hide_render = o not in keep
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr = (mn + mx) / 2
    rad = (mx - mn).length / 2
    setup_render(size, exposure=exposure, sun_az=sun_az)
    g = textured_ground(max(rad * 14, 600), ground, mn.z, (ctr.x, ctr.y))
    aspect = min(size) / max(size)
    fov = 2 * math.atan((36 * aspect / 2) / lens)
    dist = rad / math.sin(fov / 2) * 1.05 * fit
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))
    look_cam(ctr + d * dist, ctr, lens)
    scn.render.filepath = path
    ensure_dir(os.path.dirname(path))
    bpy.ops.render.render(write_still=True)
    for o in list(scn.objects):
        if o.name.startswith(("cam", "Sun", "ground")):
            bpy.data.objects.remove(o, do_unlink=True)
    for o in scn.objects:
        o.hide_render = False
    print("PREVIEW", path)


def grade_png(path, out=None, warm=(1.04, 1.0, .9), sat=.8, lift=.04, haze=0.0):
    """Post grade a render towards the target look: warm, desaturated, lifted shadows."""
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    rgb = a[..., :3]
    l = rgb.mean(-1, keepdims=True)
    rgb = l + (rgb - l) * sat
    rgb = rgb * np.array(warm, dtype=np.float32)
    rgb = lift + rgb * (1 - lift)
    rgb = np.clip(rgb, 0, 1)
    a[..., :3] = rgb
    ex = np.zeros((h, w, 4), dtype=np.float32)
    ex[...] = a
    write_png(out or path, ex[::-1, :, :3])
    bpy.data.images.remove(im)
