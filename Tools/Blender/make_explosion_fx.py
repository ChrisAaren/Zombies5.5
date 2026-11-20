"""Explosion FX generator -> Tools/Blender/staging/explosions/  (run inside Blender, headless)
  blender --background --factory-startup --python make_explosion_fx.py -- <steps>
Steps (see bottom for the full list) can be combined; with no step given everything runs.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
A = {}
for a in argv:
    k, _, v = a.lstrip("-").partition("=")
    A[k] = v or "1"

if "bake" in A:
    import fx_flip
    if "simres" in A: fx_flip.RES_OVERRIDE.update({k: int(A["simres"]) for k in A["bake"].split(",")})
    for k in A["bake"].split(","): fx_flip.bake(k)
if "render" in A:
    import fx_flip
    kw = {}
    if "res" in A: kw["res"] = int(A["res"])
    if "spp" in A: kw["samples"] = int(A["spp"])
    if "frames" in A:
        a_, b_ = A["frames"].split("-"); kw["frames"] = range(int(a_), int(b_) + 1, int(A.get("step", 1)))
    for k in A["render"].split(","): fx_flip.render(k, **kw)
if "debris" in A:
    import fx_debris
    only = A["debris"].split(",") if A["debris"] != "1" else None
    fx_debris.run_debris(only)
if "textures" in A:
    import fx_tex
    fx_tex.run_textures()
if "flipbook" in A:
    import fx_atlas
    for k in A["flipbook"].split(","): fx_atlas.run_flipbook(k)
if "sheets" in A:
    import fx_atlas
    fx_atlas.tex_sheet()
