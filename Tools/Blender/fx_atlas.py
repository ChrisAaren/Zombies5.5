"""Flipbook atlas assembly + contact sheets (helper for make_explosion_fx.py, runs inside Blender's python)."""
import os, json, math
import numpy as np
import fx_tex as T

OUT = T.OUT
WORK = os.path.join(OUT, "_work")
PREV = os.path.join(OUT, "previews")

SPEC = {
    "fireball": dict(file="FX_Fireball_8x8", atlas=2048, fps=24, blend="alpha", premult=False, loop=False, first=1, tone=1.15, tail=12, retime=(3, 52), soften=1, head=0,
                     notes="Mantaflow fire+smoke explosion (side view, ground plane at ~7% above the bottom of each cell): bright core "
                           "-> rolling orange fireball -> dark smoke. RGB is straight (not premultiplied) with soft alpha; fire colours are "
                           "tone-mapped to <=1, boost with an HDR tint / emission in the shader. Last 12 frames fade to alpha 0."),
    "smoke": dict(file="FX_Smoke_8x8", atlas=2048, fps=20, blend="alpha", premult=False, loop=False, first=1, tone=0, tail=14, retime=(1, 50), head=0,
                  notes="Mantaflow hot smoke puff lit from above (sun + sky baked in), grey RGB with soft alpha, straight alpha. "
                        "Last 14 frames fade to alpha 0 so the sprite can be removed without a pop."),
    "fire": dict(file="FX_FireLoop_8x8", atlas=1024, fps=24, blend="additive", premult=True, loop=True, first=49, tone=1.0, tail=0, head=0,
                 notes="Mantaflow burning flame, seamless loop (cross-faded over a 64-frame window of a longer bake). RGB is "
                       "premultiplied by coverage (use additive blending; alpha is also provided for alpha-blend / premultiplied use). Flame base "
                       "sits ~8% above the bottom of each cell, centred horizontally."),
}


def box_down(a, f):
    if f == 1:
        return a
    h, w = a.shape[:2]
    return a.reshape(h // f, f, w // f, f, 4).mean((1, 3))


def edge_mask(n, margin):
    d = np.minimum(np.minimum(np.arange(n), n - 1 - np.arange(n)), 1e9).astype(np.float32)
    m1 = T.sstep((d - 1) / max(margin, 1))
    return (m1[:, None] * m1[None, :])


def load_frames(kind, n_frames=64):
    sp = SPEC[kind]
    fd = os.path.join(WORK, kind + "_frames")
    raw = []
    for i in range(n_frames * (2 if sp["loop"] else 1)):
        raw.append(T.load_img(os.path.join(fd, "f%04d.exr" % (sp["first"] + i))))
    return raw


def build(kind):
    sp = SPEC[kind]
    N = 64
    raw = load_frames(kind, N)
    cell = sp["atlas"] // 8
    f = raw[0].shape[0] // cell
    fr = [box_down(a, f) for a in raw]
    if sp.get("soften"):
        fr = [np.stack([T.blur(a[..., c], 1, sp["soften"]) for c in range(4)], -1) for a in fr]
    if sp["loop"]:
        out = []
        for i in range(N):
            t = i / N
            out.append(t * fr[i] + (1 - t) * fr[N + i])        # out[N] == out[0] -> seamless
        fr = out
    if not sp["loop"] and "retime" in sp:       # drop the dead first / last frames, keep motion lively
        a0, a1 = sp["retime"]
        out = []
        for i in range(N):
            x = a0 + (a1 - a0) * i / (N - 1)         # fractional source frame index (0-based)
            k = int(math.floor(x)); t = x - k
            out.append(fr[k] * (1 - t) + fr[min(k + 1, N - 1)] * t)
        fr = out
    mask = edge_mask(cell, max(2, int(cell * .045)))
    cells = []
    for i, a in enumerate(fr):
        a = a.copy()
        a *= mask[..., None]
        if sp["tail"] and i >= N - sp["tail"]:
            k = 1 - T.sstep((i - (N - sp["tail"])) / (sp["tail"] - 1))
            a *= k
        rgb = a[..., :3]; al = np.clip(a[..., 3:4], 0, 1)
        if sp["tone"]:
            rgb = 1 - np.exp(-rgb * sp["tone"])
        rgb = np.clip(rgb, 0, 1)
        if kind == "smoke":
            al = al ** .9
        if sp["premult"]:
            rgb = np.minimum(rgb, 1.0)
            out_rgb = rgb
            al = np.maximum(al, rgb.max(-1, keepdims=True))        # alpha covers the glow too
        else:
            out_rgb = np.where(al > 1e-4, rgb / np.maximum(al, 1e-4), 0)
            out_rgb = np.clip(out_rgb, 0, 1)
            # (rgb above was premultiplied radiance -> straight colour)
        cells.append(np.concatenate([out_rgb, al], -1))
    return np.stack(cells).astype(np.float32), sp


def atlas_from(cells):
    rows = [np.concatenate(list(cells[r * 8:(r + 1) * 8]), 1) for r in range(8)]
    return np.concatenate(rows, 0)


def over(arr, bg, premult=False):
    a = arr[..., 3:4]
    rgb = arr[..., :3] * (1 if premult else a)
    return np.concatenate([rgb + np.array(bg, np.float32)[None, None, :] * (1 - a), np.ones_like(a)], -1)


def sheet(atlas, premult, bg, path, size=None):
    im = over(atlas, bg, premult)
    # slight grid lines so the cells read
    n = atlas.shape[0]; c = n // 8
    im[::c, :, :3] = np.clip(im[::c, :, :3] + .08, 0, 1); im[:, ::c, :3] = np.clip(im[:, ::c, :3] + .08, 0, 1)
    T.save_png(im, path)


def checks(cells, atlas_n, sp):
    n = cells.shape[1]
    border = max(cells[:, 0, :, 3].max(), cells[:, -1, :, 3].max(), cells[:, :, 0, 3].max(), cells[:, :, -1, 3].max())
    diffs = [float(np.abs(cells[i + 1] - cells[i]).mean()) for i in range(len(cells) - 1)]
    wrap = float(np.abs(cells[0] - cells[-1]).mean())
    cov = [float((c[..., 3] > .05).mean()) for c in cells]
    return dict(max_border_alpha=float(border), mean_frame_diff=float(np.mean(diffs)), wrap_diff_last_to_first=wrap,
                peak_coverage=float(max(cov)))


def run_flipbook(kind):
    cells, sp = build(kind)
    atlas = atlas_from(cells)
    T.save_png(atlas, os.path.join(OUT, sp["file"] + ".png"))
    chk = checks(cells, sp["atlas"], sp)
    cell = sp["atlas"] // 8
    js = dict(file=sp["file"] + ".png", atlas_px=[sp["atlas"], sp["atlas"]], grid=[8, 8], frames=64, cell_px=cell,
              fps=sp["fps"], duration_s=round(64 / sp["fps"], 3), loop=sp["loop"], blend=sp["blend"],
              rgb_premultiplied=sp["premult"],
              frame_order="row-major: frame 0 = top-left cell, left->right, top->bottom (Unity UV: row r from top -> v = 1 - (r + 1) / 8)",
              edge_margin_px=int(max(2, int(cell * .045))), bg_alpha_checks=chk, notes=sp["notes"],
              suggested_use=dict(additive="rgb already premultiplied: Blend One One (or SrcAlpha One)" if sp["premult"] else "alpha only",
                                 alpha="Blend SrcAlpha OneMinusSrcAlpha" if not sp["premult"] else "Blend One OneMinusSrcAlpha (premultiplied)"))
    json.dump(js, open(os.path.join(OUT, sp["file"] + ".json"), "w"), indent=2)
    sheet(atlas, sp["premult"], (.04, .04, .045), os.path.join(PREV, kind + "_frames_dark.png"))
    sheet(atlas, sp["premult"], (.45, .46, .47), os.path.join(PREV, kind + "_frames_gray.png"))
    print("FLIPBOOK %s %s" % (kind, chk))


def tex_sheet():
    def ld(n): return T.load_img(os.path.join(OUT, n))
    def up(a, f): return np.repeat(np.repeat(a, f, 0), f, 1)
    def down(a, f): return a.reshape(a.shape[0] // f, f, a.shape[1] // f, f, 4).mean((1, 3))
    asphalt = np.ones((512, 512, 3), np.float32) * np.array([.36, .36, .35]) * (.75 + .5 * T.fbm(512, 40, 4, 99)[..., None])
    sc = down(ld("FX_Scorch.png"), 2)
    p1 = np.concatenate([sc[..., :3] * sc[..., 3:4] + asphalt * (1 - sc[..., 3:4]), np.ones((512, 512, 1), np.float32)], -1)
    scn = down(ld("FX_Scorch_Normal.png"), 2); scn[..., 3] = 1
    ca = down(ld("FX_Charred_Albedo.png"), 2); cn = down(ld("FX_Charred_Normal.png"), 2)
    row1 = np.concatenate([p1, scn, ca, cn], 1)
    ring = over(ld("FX_ShockwaveRing.png"), (.03, .03, .035))
    spk = over(up(ld("FX_SparkStreak.png"), 2), (.03, .03, .035))          # 128 x 512
    spk_box = np.zeros((512, 512, 4), np.float32) + np.array([.03, .03, .035, 1], np.float32)
    for k in range(4):
        spk_box[20 + k * 120:20 + k * 120 + 128] = spk
    bits = ld("FX_DebrisBits_4x2.png")
    bits_g = over(bits, (.42, .42, .42)); bits_d = over(bits, (.04, .04, .045))
    row2 = np.concatenate([ring, spk_box, bits_g], 1)
    T.save_png(np.concatenate([row1, row2], 0), os.path.join(PREV, "textures_sheet.png"))
    T.save_png(np.concatenate([p1, ca], 1), os.path.join(PREV, "scorch_charred.png"))
    T.save_png(np.concatenate([bits_g, bits_d], 0), os.path.join(PREV, "debris_bits.png"))
