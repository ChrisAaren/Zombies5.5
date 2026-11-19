"""Stage 1: cage (skin modifier + hand cages) -> Catmull-Clark -> SDF fit. Returns a bmesh of the bare shape."""
import bpy, bmesh, numpy as np
import hb_graph, hb_shape


def fit_mesh(ob, body, relax=True):
    me = ob.data
    P = np.array([v.co[:] for v in me.vertices])
    P2 = body.project(P)
    if relax:
        nbrs = [[] for _ in P]
        for e in me.edges:
            nbrs[e.vertices[0]].append(e.vertices[1]); nbrs[e.vertices[1]].append(e.vertices[0])
        ax = np.abs(P2[:, 0])
        P2 = hb_shape.relax_project(body, P2, nbrs, (ax > .70) & (ax < .85), rounds=3, lam=.4)
        thm = (ax > .72) & (ax < .84) & (P2[:, 1] < -.02)
        P2 = hb_shape.relax_project(body, P2, nbrs, thm, rounds=5, lam=.5)
    for v, p in zip(me.vertices, P2):
        v.co = p
    return ob


def base_bmesh():
    body = hb_shape.Body()
    ob = hb_graph.make_skin_mesh()
    fit_mesh(ob, body)
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bpy.data.objects.remove(ob, do_unlink=True)
    return bm, body
