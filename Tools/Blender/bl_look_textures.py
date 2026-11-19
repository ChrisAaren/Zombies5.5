"""Look-development textures (numpy; Blender only writes the PNGs):
FacadeGrime_Mask  - R drips / soot streaks that start abruptly and fade downward (sills, cornices), G blotches, B rust bleed
SkinPore_Normal   - tiling pore + micro wrinkle normal map
SkinZones_Mask    - face-UV zone map of the MPFB skin atlas: R shine (forehead, nose, cheeks), G cavity, B eye sockets
Run:  blender -b -P bl_look_textures.py"""
import os, sys, math
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bl_v2_textures as T
from bl_v2_textures import noise, sstep, blur, normal_from_height, save


def facade_grime(N=1024, seed=700):
    r = np.random.default_rng(seed)
    drips = np.zeros((N, N), np.float32)
    rust = np.zeros((N, N), np.float32)
    yy = np.arange(N)[:, None]
    xx = np.arange(N)[None, :]
    for k in range(70):
        cx = r.integers(0, N)
        w = r.uniform(3, 14)
        y0 = r.integers(0, N)
        ln = r.uniform(120, 620)
        dy = (yy - y0) % N                                     # distance below the start (wraps)
        fall = np.clip(1 - dy / ln, 0, 1) ** 1.4
        start = sstep(dy.astype(np.float32), 0, 6)
        dx = np.minimum(np.abs(xx - cx), N - np.abs(xx - cx))
        prof = np.exp(-(dx / w) ** 2)
        wob = 1 + 0.35 * noise(N, N, 2.0, 6, 120, kx=1.0, ky=4.0, seed=seed + k)
        a = fall * prof * wob * r.uniform(0.5, 1.0)
        drips = np.maximum(drips, a)
        if k % 6 == 0:
            rust = np.maximum(rust, np.clip(a * 1.6, 0, 1))
    streak_n = noise(N, N, 2.8, 2, 60, kx=2.0, ky=0.4, seed=seed + 100)
    drips = np.clip(drips + 0.25 * sstep(streak_n, 0.2, 0.7), 0, 1)
    blot = sstep(noise(N, N, 2.6, 2, 40, seed=seed + 101), -0.1, 0.55)
    speck = sstep(noise(N, N, 0.5, 30, 500, seed=seed + 102), 0.0, 0.5)
    g = np.clip(blot * (0.7 + 0.3 * speck), 0, 1)
    rust = np.clip(rust * (0.5 + 0.5 * sstep(noise(N, N, 1.6, 4, 90, seed=seed + 103), -0.2, 0.4)), 0, 1)
    save("FacadeGrime", "Mask", np.stack([drips, g, rust], -1))


def skin_pore(N=512, seed=800):
    r = np.random.default_rng(seed)
    h = np.zeros((N, N), np.float32)
    pts = r.integers(0, N, (2200, 2))
    for (px, py) in pts:
        rad = r.uniform(1.0, 2.2)
        ys = (np.arange(-4, 5) + py) % N
        xs = (np.arange(-4, 5) + px) % N
        d = np.sqrt(np.add.outer(np.arange(-4, 5) ** 2, np.arange(-4, 5) ** 2))
        h[np.ix_(ys, xs)] -= np.exp(-(d / rad) ** 2) * r.uniform(0.4, 1.0)
    h += 0.5 * noise(N, N, 0.6, 20, 200, seed=seed + 1)
    # fine crossed micro-wrinkles
    for i in range(2):
        w = noise(N, N, 2.0, 30, 160, kx=1.0 if i == 0 else 3.0, ky=3.0 if i == 0 else 1.0, seed=seed + 2 + i)
        h -= 0.45 * np.abs(w) ** 0.5
    save("SkinPore", "Normal", normal_from_height(h, 1.1))


def skin_zones(N=1024, seed=900):
    # coordinates in fractions of the 2048 atlas (u from left, v from top); the face is rotated 90 degrees in the atlas
    u = (np.arange(N)[None, :] + 0.5) / N
    v = (np.arange(N)[:, None] + 0.5) / N     # v from top
    def blob(cu, cv, su, sv):
        return np.exp(-(((u - cu) / su) ** 2 + ((v - cv) / sv) ** 2))
    shine = 0.25 + 0.75 * np.clip(
        blob(0.875, 0.512, 0.020, 0.012) +                       # nose
        blob(0.768, 0.512, 0.040, 0.050) +                       # forehead
        blob(0.880, 0.455, 0.025, 0.022) + blob(0.880, 0.570, 0.025, 0.022) +   # cheeks
        0.6 * blob(0.905, 0.512, 0.012, 0.010), 0, 1)           # chin / lip
    cav = np.ones((N, N), np.float32)
    for cv in (0.480, 0.548):                                    # eye corners
        cav -= 0.7 * blob(0.836, cv, 0.014, 0.014)
    cav -= 0.6 * blob(0.870, 0.512, 0.008, 0.010)                # nostrils
    cav -= 0.6 * blob(0.908, 0.512, 0.018, 0.006)                # mouth line
    cav -= 0.5 * blob(0.838, 0.376, 0.016, 0.014) + 0.5 * blob(0.838, 0.650, 0.016, 0.014)   # ears
    sock = np.clip(blob(0.836, 0.478, 0.022, 0.020) + blob(0.836, 0.548, 0.022, 0.020), 0, 1)
    # A: lips (the mouth ellipse), so the shader can pull them toward the skin tone and take the gloss off
    # centred on the pink lip patch of Courier_Skin_Albedo (u 0.910, v 0.517 from the top), feathered edge
    rr = np.sqrt(((u - 0.910) / 0.0165) ** 2 + ((v - 0.517) / 0.0225) ** 2)
    lips = 1.0 - sstep(rr, 0.7, 1.2)
    save("SkinZones", "Mask", np.stack([np.clip(shine, 0, 1), np.clip(cav, 0, 1), sock], -1), alpha=lips)


def skin_zones2(N=1024, seed=950):
    """R stubble / beard shadow (jaw, chin, upper lip, sideburns), G hairline transition (forehead top, temples), B brow ridge."""
    u = (np.arange(N)[None, :] + 0.5) / N
    v = (np.arange(N)[:, None] + 0.5) / N
    def blob(cu, cv, su, sv):
        return np.exp(-(((u - cu) / su) ** 2 + ((v - cv) / sv) ** 2))
    beard = (blob(0.918, 0.512, 0.030, 0.085) + blob(0.903, 0.425, 0.026, 0.040) + blob(0.903, 0.600, 0.026, 0.040)
             + blob(0.895, 0.470, 0.022, 0.030) + blob(0.895, 0.555, 0.022, 0.030) + 0.8 * blob(0.892, 0.512, 0.012, 0.045)
             + 0.6 * blob(0.86, 0.385, 0.03, 0.02) + 0.6 * blob(0.86, 0.640, 0.03, 0.02)
             + 0.55 * blob(0.880, 0.450, 0.030, 0.034) + 0.55 * blob(0.880, 0.575, 0.030, 0.034))   # cheeks
    beard = np.clip(beard, 0, 1)
    beard *= 1.0 - 0.9 * blob(0.910, 0.517, 0.013, 0.022)       # lips stay clean
    beard *= 1.0 - 0.9 * blob(0.870, 0.512, 0.025, 0.020)         # nose
    grain = 0.55 + 0.45 * sstep(noise(N, N, 0.4, 40, 500, seed=seed), -0.2, 0.5)
    hair = np.clip(blob(0.750, 0.512, 0.016, 0.075) + 0.8 * blob(0.775, 0.42, 0.022, 0.03) + 0.8 * blob(0.775, 0.605, 0.022, 0.03), 0, 1)
    brow = np.clip(blob(0.812, 0.478, 0.013, 0.032) + blob(0.812, 0.548, 0.013, 0.032), 0, 1)
    save("SkinZones2", "Mask", np.stack([np.clip(beard * grain, 0, 1), hair, brow], -1))


if __name__ == "__main__":
    facade_grime()
    skin_pore()
    skin_zones()
    skin_zones2()
