"""Trees -> Assets/Art/Models/Nature/{Tree_A,Tree_B}.fbx  (pivot bottom center)
Run: blender --background --factory-startup --python make_nature.py
Tree_A: broad rounded canopy on a branching trunk. Tree_B: tall, slim, tiered oval crown.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bl_common import *


def trunk(p, pts, radii, n=9):
    p.tube([V(*q) for q in pts], radii, "Bark", n)


def tree_a():
    p = Part("Tree_A")
    # tapered, slightly leaning trunk with a root flare
    trunk(p, [(0, 0, -.05), (0.02, 0, .3), (.08, .04, 1.3), (-.02, .1, 2.4), (.05, .05, 3.4)], [.46, .3, .24, .19, .14])
    # branches (start, end, radius) reaching toward the foliage clumps
    for a, b, r in (((.0, .08, 2.5), (1.7, .5, 4.2), .11), ((.0, .08, 2.7), (-1.6, .6, 4.3), .1),
                    ((.05, .05, 3.0), (.3, -1.6, 4.4), .09), ((.05, .05, 3.2), (-.2, 1.3, 5.2), .08),
                    ((.05, .05, 3.4), (.2, 0, 5.2), .1)):
        mid = tuple((x + y) / 2 + (.12 if i == 2 else 0) for i, (x, y) in enumerate(zip(a, b)))
        trunk(p, [a, mid, b], [r, r * .8, r * .45], 7)
    # foliage clumps: (x, f, z), radii, seed
    for c, r, s in (((0, 0, 5.1), (2.0, 2.0, 1.45), 1), ((1.8, .4, 4.4), (1.4, 1.3, 1.0), 2), ((-1.7, .6, 4.5), (1.35, 1.3, 1.0), 3),
                    ((.3, -1.7, 4.55), (1.3, 1.2, 1.0), 4), ((-.2, 1.5, 5.3), (1.3, 1.2, 1.05), 5), ((.6, .3, 6.0), (1.35, 1.3, .9), 6),
                    ((-.9, -.8, 5.7), (1.1, 1.1, .85), 7)):
        noise_blob(p, V(*c), r, "Leaf", s, .32, 1.3, 2, flat_below=c[2] - r[2] * .45)
    return p.build(smooth=40, flat=("Leaf",))


def tree_b():
    p = Part("Tree_B")
    trunk(p, [(0, 0, -.05), (0, 0, .3), (.04, 0, 1.4), (0, 0, 2.6), (0, 0, 4.2)], [.36, .22, .17, .13, .07])
    for a, b, r in (((0, 0, 1.9), (.9, .3, 3.0), .06), ((0, 0, 2.3), (-.8, -.4, 3.6), .06), ((0, 0, 3.0), (.6, -.5, 4.5), .05)):
        trunk(p, [a, tuple((x + y) / 2 for x, y in zip(a, b)), b], [r, r * .8, r * .5], 6)
    # stacked, tapering crown tiers with a slight wobble
    for c, r, s in (((0, 0, 2.9), (1.55, 1.5, .95), 11), ((.1, .05, 3.85), (1.3, 1.3, 1.0), 12), ((-.05, 0, 4.8), (1.1, 1.05, .95), 13),
                    ((.05, -.05, 5.7), (.8, .8, .85), 14), ((0, 0, 6.45), (.5, .5, .6), 15),
                    ((.9, .4, 3.4), (.7, .7, .6), 16), ((-.8, -.5, 4.1), (.65, .65, .55), 17)):
        noise_blob(p, V(*c), r, "Leaf", s, .3, 1.4, 2, flat_below=c[2] - r[2] * .6)
    return p.build(smooth=40, flat=("Leaf",))


def main():
    for name, fn in (("Tree_A", tree_a), ("Tree_B", tree_b)):
        reset_scene()
        ob = fn()
        report(ob, (None, 6.5 if name == "Tree_A" else 7.0, None), 1.0)
        export_fbx([ob], "Nature", name)
        render_preview([ob], os.path.join(PREVIEWS, name + ".png"), az=35, el=12, size=(700, 900))


main()
