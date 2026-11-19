"""Workbench tile rendering + tiny numpy drawing/labeling helpers (no Pillow).

All tiles are float32 arrays (h, w, 4), row 0 = top. Cameras are orthographic and fixed (framing is given
by the caller), so two versions of the same character can be compared tile for tile.
"""
import bpy, os, math, hashlib
import numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

_FONT = {
    "A": "01110 10001 10001 11111 10001 10001 10001", "B": "11110 10001 10001 11110 10001 10001 11110",
    "C": "01110 10001 10000 10000 10000 10001 01110", "D": "11110 10001 10001 10001 10001 10001 11110",
    "E": "11111 10000 10000 11110 10000 10000 11111", "F": "11111 10000 10000 11110 10000 10000 10000",
    "G": "01110 10001 10000 10111 10001 10001 01111", "H": "10001 10001 10001 11111 10001 10001 10001",
    "I": "01110 00100 00100 00100 00100 00100 01110", "J": "00111 00010 00010 00010 00010 10010 01100",
    "K": "10001 10010 10100 11000 10100 10010 10001", "L": "10000 10000 10000 10000 10000 10000 11111",
    "M": "10001 11011 10101 10101 10001 10001 10001", "N": "10001 11001 10101 10011 10001 10001 10001",
    "O": "01110 10001 10001 10001 10001 10001 01110", "P": "11110 10001 10001 11110 10000 10000 10000",
    "Q": "01110 10001 10001 10001 10101 10010 01101", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 10000 01110 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    "U": "10001 10001 10001 10001 10001 10001 01110", "V": "10001 10001 10001 10001 10001 01010 00100",
    "W": "10001 10001 10001 10101 10101 11011 10001", "X": "10001 10001 01010 00100 01010 10001 10001",
    "Y": "10001 10001 01010 00100 00100 00100 00100", "Z": "11111 00001 00010 00100 01000 10000 11111",
    "0": "01110 10001 10011 10101 11001 10001 01110", "1": "00100 01100 00100 00100 00100 00100 01110",
    "2": "01110 10001 00001 00010 00100 01000 11111", "3": "11110 00001 00001 01110 00001 00001 11110",
    "4": "00010 00110 01010 10010 11111 00010 00010", "5": "11111 10000 11110 00001 00001 10001 01110",
    "6": "00110 01000 10000 11110 10001 10001 01110", "7": "11111 00001 00010 00100 01000 01000 01000",
    "8": "01110 10001 10001 01110 10001 10001 01110", "9": "01110 10001 10001 01111 00001 00010 01100",
    "-": "00000 00000 00000 11111 00000 00000 00000", "_": "00000 00000 00000 00000 00000 00000 11111",
    ".": "00000 00000 00000 00000 00000 01100 01100", ":": "00000 01100 01100 00000 01100 01100 00000",
    "/": "00001 00010 00010 00100 01000 01000 10000", "+": "00000 00100 00100 11111 00100 00100 00000",
    "%": "11001 11010 00010 00100 01000 01011 10011", "(": "00010 00100 01000 01000 01000 00100 00010",
    ")": "01000 00100 00010 00010 00010 00100 01000", " ": "00000 00000 00000 00000 00000 00000 00000",
}
_GLYPH = {k: np.array([[int(c) for c in row] for row in v.split()], np.float32) for k, v in _FONT.items()}


def text(img, s, x, y, scale=2, color=(1, 1, 1), shadow=True):
    """Draw text on img (top-left origin) at pixel x,y."""
    H, W = img.shape[:2]
    cx = x
    for ch in s.upper():
        g = _GLYPH.get(ch, _GLYPH[" "])
        big = np.kron(g, np.ones((scale, scale), np.float32))
        h, w = big.shape
        for (dx, dy, col) in (((1, 1, (0, 0, 0)),) if shadow else ()) + ((0, 0, color),):
            xs, ys = cx + dx, y + dy
            x0, y0, x1, y1 = max(xs, 0), max(ys, 0), min(xs + w, W), min(ys + h, H)
            if x1 <= x0 or y1 <= y0:
                continue
            m = big[y0 - ys:y1 - ys, x0 - xs:x1 - xs] > 0
            reg = img[y0:y1, x0:x1]
            for c in range(3):
                reg[..., c][m] = col[c]
        cx += (5 + 1) * scale


def disk(img, x, y, r, color):
    H, W = img.shape[:2]
    x0, x1, y0, y1 = int(max(x - r - 1, 0)), int(min(x + r + 2, W)), int(max(y - r - 1, 0)), int(min(y + r + 2, H))
    if x1 <= x0 or y1 <= y0:
        return
    yy, xx = np.mgrid[y0:y1, x0:x1]
    m = (xx - x) ** 2 + (yy - y) ** 2 <= r * r
    for c in range(3):
        img[y0:y1, x0:x1, c][m] = color[c]


def line(img, p0, p1, color, width=1):
    n = int(max(abs(p1[0] - p0[0]), abs(p1[1] - p0[1]))) + 1
    for t in np.linspace(0, 1, n):
        disk(img, p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t, width * 0.5, color)


def color_for(name):
    h = hashlib.md5(name.encode()).digest()
    import colorsys
    r, g, b = colorsys.hsv_to_rgb(h[0] / 255.0, 0.55 + 0.4 * h[1] / 255.0, 0.65 + 0.35 * h[2] / 255.0)
    return (r, g, b)


class Stage:
    def __init__(self, res=512, bg=(0.23, 0.24, 0.27)):
        self.res = res
        scn = bpy.context.scene
        self.scn = scn
        scn.render.engine = 'BLENDER_WORKBENCH'
        scn.render.resolution_x = scn.render.resolution_y = res
        scn.render.resolution_percentage = 100
        scn.render.image_settings.file_format = 'PNG'
        scn.render.film_transparent = False
        scn.view_settings.view_transform = 'Standard'
        scn.view_settings.look = 'None'
        scn.display.shading.light = 'FLAT'
        scn.display.shading.show_object_outline = True
        scn.display.shading.object_outline_color = (0, 0, 0)
        scn.display.render_aa = '8'
        w = bpy.data.worlds.new("pl_world")
        w.color = bg
        scn.world = w
        cd = bpy.data.cameras.new("pl_cam")
        cd.type = 'ORTHO'
        cd.clip_start, cd.clip_end = 0.01, 50
        self.cam = bpy.data.objects.new("pl_cam", cd)
        scn.collection.objects.link(self.cam)
        scn.camera = self.cam
        self.tmp = os.path.join(bpy.app.tempdir or os.getcwd(), "pl_tile.png")

    def set_camera(self, loc, target, ortho):
        self.cam.location = Vector(loc)
        self.cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
        self.cam.data.ortho_scale = ortho
        bpy.context.view_layer.update()

    def render(self, loc, target, ortho, color_type='OBJECT', visible=None):
        """visible: list of objects to show (all other meshes hidden); None = leave as is."""
        scn = self.scn
        if visible is not None:
            vs = set(visible)
            for o in bpy.data.objects:
                if o.type in ('MESH', 'ARMATURE'):
                    o.hide_render = o not in vs
        scn.display.shading.color_type = color_type
        self.set_camera(loc, target, ortho)
        scn.render.filepath = self.tmp
        bpy.ops.render.render(write_still=True)
        im = bpy.data.images.load(self.tmp, check_existing=False)
        w, h = im.size
        a = np.empty(w * h * 4, np.float32)
        im.pixels.foreach_get(a)
        bpy.data.images.remove(im)
        return np.flipud(a.reshape(h, w, 4)).copy()

    def project(self, p):
        """World point -> pixel (x, y) in the last-set camera, top-left origin."""
        co = world_to_camera_view(self.scn, self.cam, Vector(p))
        return co.x * self.res, (1.0 - co.y) * self.res


def save_png(img, path):
    h, w = img.shape[:2]
    im = bpy.data.images.new("pl_out", w, h, alpha=True)
    im.pixels.foreach_set(np.flipud(np.clip(img, 0, 1)).astype(np.float32).ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


def load_png(path):
    im = bpy.data.images.load(path, check_existing=False)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return np.flipud(a.reshape(h, w, 4)).copy()


def assemble(tiles, cols, labels=None, pad=4, bg=(0.1, 0.1, 0.11), scale=2):
    """tiles: list of (h,w,4) arrays; labels: list of str drawn top-left on each."""
    th, tw = tiles[0].shape[:2]
    rows = (len(tiles) + cols - 1) // cols
    sheet = np.empty((rows * (th + pad) + pad, cols * (tw + pad) + pad, 4), np.float32)
    sheet[...] = (*bg, 1.0)
    for i, t in enumerate(tiles):
        t = t.copy()
        if labels and labels[i]:
            text(t, labels[i], 6, 6, scale=scale)
        r, c = divmod(i, cols)
        y, x = pad + r * (th + pad), pad + c * (tw + pad)
        sheet[y:y + th, x:x + tw] = t
    return sheet
