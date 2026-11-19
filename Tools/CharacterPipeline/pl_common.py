"""Shared helpers for the character pipeline: config, paths, mesh -> numpy, plane slicing."""
import bpy, os, sys, json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.append(os.path.join(os.path.dirname(HERE), "Blender"))


def argv():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def parse_args(spec):
    """spec: {name: default}. Accepts --name value pairs."""
    a = argv()
    out = dict(spec)
    i = 0
    while i < len(a):
        if a[i].startswith("--") and a[i][2:] in out:
            out[a[i][2:]] = a[i + 1]
            i += 2
        else:
            i += 1
    return out


def load_config(vdir=None):
    p = os.path.join(vdir, "config_used.json") if vdir else None
    if not p or not os.path.exists(p):
        p = os.path.join(HERE, "pipeline_config.json")
    with open(p, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    t = cfg["tiers"][cfg["tier"]]
    cfg["budget"] = t
    return cfg


def read_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def write_json(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def open_blend(path):
    bpy.ops.wm.open_mainfile(filepath=path)


def mesh_np(obj, world=True, depsgraph=False):
    """(V (n,3) float64, T (m,3) int64) of the object (evaluated with modifiers if depsgraph)."""
    if depsgraph:
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        me = ev.to_mesh()
    else:
        ev = obj
        me = obj.data
    n = len(me.vertices)
    V = np.empty(n * 3, np.float64)
    me.vertices.foreach_get("co", V)
    V = V.reshape(n, 3)
    me.calc_loop_triangles()
    m = len(me.loop_triangles)
    T = np.empty(m * 3, np.int64)
    me.loop_triangles.foreach_get("vertices", T)
    T = T.reshape(m, 3)
    if world:
        M = np.array(obj.matrix_world)
        V = V @ M[:3, :3].T + M[:3, 3]
    if depsgraph:
        ev.to_mesh_clear()
    return V, T


def slice_plane(V, T, origin, normal, tri_mask=None, drop_nested=True):
    """Cross-section of a triangle mesh with a plane. Returns a list of components
    {points (k,3) (edge crossings), centroid (3,), radius (mean distance to centroid), n}."""
    origin = np.asarray(origin, float)
    normal = np.asarray(normal, float)
    normal = normal / np.linalg.norm(normal)
    d = (V - origin) @ normal
    s = d > 0
    st = s[T]
    cross = ~((st[:, 0] == st[:, 1]) & (st[:, 1] == st[:, 2]))
    if tri_mask is not None:
        cross &= tri_mask
    ti = np.nonzero(cross)[0]
    if len(ti) == 0:
        return []
    keys, pts, owner = [], [], []
    N = len(V)
    for a, b in ((0, 1), (1, 2), (2, 0)):
        va, vb = T[ti, a], T[ti, b]
        m = s[va] != s[vb]
        va, vb, tt = va[m], vb[m], ti[m]
        da, db = d[va], d[vb]
        t = da / (da - db)
        p = V[va] + (V[vb] - V[va]) * t[:, None]
        lo, hi = np.minimum(va, vb), np.maximum(va, vb)
        keys.append(lo * N + hi)
        pts.append(p)
        owner.append(tt)
    keys = np.concatenate(keys)
    pts = np.concatenate(pts)
    owner = np.concatenate(owner)
    order = np.argsort(owner, kind="stable")
    keys, pts, owner = keys[order], pts[order], owner[order]
    uk, inv = np.unique(keys, return_inverse=True)
    # each crossing triangle contributes exactly two consecutive entries
    ia, ib = inv[0::2], inv[1::2]
    if len(ia) != len(ib):
        ia, ib = ia[:len(ib)], ib[:len(ib)]
    parent = list(range(len(uk)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for a, b in zip(ia.tolist(), ib.tolist()):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    node_pt = np.zeros((len(uk), 3))
    node_pt[inv] = pts
    roots = np.array([find(i) for i in range(len(uk))])
    comps = []
    for r in np.unique(roots):
        P = node_pt[roots == r]
        c = P.mean(axis=0)
        comps.append({"points": P, "centroid": c, "radius": float(np.linalg.norm(P - c, axis=1).mean()), "n": len(P),
                      "lo": P.min(axis=0), "hi": P.max(axis=0)})
    if drop_nested and len(comps) > 1:
        # inner loops of hollow garments: a loop whose bounding box lies inside another loop's box
        keep = []
        for i, a in enumerate(comps):
            inside = False
            for j, b in enumerate(comps):
                if i != j and np.any(a["hi"] - a["lo"] < b["hi"] - b["lo"] - 1e-3) and np.all(a["lo"] >= b["lo"] - 1e-4) and np.all(a["hi"] <= b["hi"] + 1e-4):
                    inside = True
                    break
            if not inside:
                keep.append(a)
        comps = keep
    return comps


def nearest_comp(comps, point, min_n=6):
    best, bd = None, 1e9
    for c in comps:
        if c["n"] < min_n:
            continue
        dd = np.linalg.norm(c["centroid"] - point)
        if dd < bd:
            best, bd = c, dd
    return best


def region_of_bone(name):
    """Coarse body region for a bone name (used for per-region reporting)."""
    n = name
    for k in ("Thumb", "Index", "Middle", "Ring", "Little"):
        if k in n:
            return "hand_" + ("L" if n.startswith("Left") else "R")
    if "Hand" in n:
        return "hand_" + ("L" if n.startswith("Left") else "R")
    side = "L" if n.startswith("Left") else ("R" if n.startswith("Right") else "")
    for k, r in (("LowerArm", "arm"), ("UpperArm", "arm"), ("Shoulder", "shoulder"), ("UpperLeg", "leg"),
                 ("LowerLeg", "leg"), ("Foot", "foot"), ("Toes", "foot")):
        if k in n:
            return r + ("_" + side if side else "")
    if n in ("Head", "Neck"):
        return "head_neck"
    return "torso"


# ------------------------------------------------------------------ hands: geodesic fingertips
def mesh_edges(T, n):
    E = np.concatenate([T[:, [0, 1]], T[:, [1, 2]], T[:, [2, 0]]])
    E = np.sort(E, axis=1)
    k = np.unique(E[:, 0] * n + E[:, 1])
    return np.stack([k // n, k % n], axis=1)


def _dijkstra(n, ea, eb, ew, sources, limit=1e9):
    import heapq
    adj = [[] for _ in range(n)]
    for a, b, w in zip(ea.tolist(), eb.tolist(), ew.tolist()):
        adj[a].append((b, w))
        adj[b].append((a, w))
    dist = np.full(n, np.inf)
    pred = np.full(n, -1, np.int64)
    h = []
    for s in sources:
        dist[s] = 0.0
        h.append((0.0, int(s)))
    heapq.heapify(h)
    while h:
        d0, u = heapq.heappop(h)
        if d0 > dist[u] or d0 > limit:
            continue
        for v, w in adj[u]:
            nd = d0 + w
            if nd < dist[v]:
                dist[v] = nd
                pred[v] = u
                heapq.heappush(h, (nd, v))
    return dist, pred


def hand_tips(V, T, W, d, perp_max=0.13, max_r=0.30, min_frac=0.45, merge=0.02, rho=0.05, max_tips=5, edges=None):
    """Fingertips of the hand beyond wrist point W (arm direction d): local maxima of the geodesic distance
    from the wrist plane over the mesh surface. Returns (tips, info); tips = list of dicts
    {pos, dist, dir} sorted by lateral position (thumb first when the thumb faces -Y)."""
    W = np.asarray(W, float)
    d = np.asarray(d, float) / np.linalg.norm(d)
    rel = (V - W) @ d
    perp = np.linalg.norm((V - W) - np.outer(rel, d), axis=1)
    reg = (rel > -0.004) & (perp < perp_max) & (rel < max_r)
    idx = np.nonzero(reg)[0]
    if len(idx) < 50:
        return [], {"region_verts": int(len(idx))}
    remap = -np.ones(len(V), np.int64)
    remap[idx] = np.arange(len(idx))
    E = edges if edges is not None else mesh_edges(T, len(V))
    keep = reg[E[:, 0]] & reg[E[:, 1]]
    E = E[keep]
    ea, eb = remap[E[:, 0]], remap[E[:, 1]]
    ew = np.linalg.norm(V[E[:, 0]] - V[E[:, 1]], axis=1)
    src = np.nonzero(rel[idx] < 0.003)[0]
    dist, pred = _dijkstra(len(idx), ea, eb, ew, src)
    fin = np.isfinite(dist)
    ismax = fin.copy()
    lo_a = dist[ea] < dist[eb]
    ismax[ea[lo_a]] = False
    lo_b = dist[eb] < dist[ea]
    ismax[eb[lo_b]] = False
    maxd = dist[fin].max() if fin.any() else 0.0
    cand = np.nonzero(ismax & (dist >= min_frac * maxd))[0]
    cand = cand[np.argsort(-dist[cand])]
    tips = []
    P = V[idx]
    for c in cand:
        # a real tip has no farther vertex within `rho` along the surface (rejects web/knuckle bumps)
        dl, _ = _dijkstra(len(idx), ea, eb, ew, [c], limit=rho)
        near = np.isfinite(dl) & (dl <= rho)
        if (dist[near] > dist[c] + 1e-6).any():
            continue
        if all(np.linalg.norm(P[c] - t["pos"]) > merge for t in tips):
            v = c
            while pred[v] >= 0 and dist[c] - dist[v] < 0.05:
                v = pred[v]
            dv = P[c] - P[v]
            dv = dv / max(np.linalg.norm(dv), 1e-9)
            tips.append({"pos": P[c].copy(), "dist": float(dist[c]), "dir": dv})
    tips = tips[:max_tips]  # sorted by geodesic distance already: keep the 5 farthest (web bumps are shorter)
    lat = np.cross(d, np.cross(np.array([0.0, 1.0, 0.0]), d))
    lat = lat / max(np.linalg.norm(lat), 1e-9)
    tips.sort(key=lambda t: float(np.dot(t["pos"], lat)))
    return tips, {"region_verts": int(len(idx)), "max_geodesic": float(maxd), "tips_found": len(tips)}


def boundary_loops(bm):
    """Number of connected boundary-edge components of a bmesh (same definition check.py uses for game/source)."""
    parent = {}

    def find(x):
        while parent.setdefault(x, x) != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    n = 0
    for e in bm.edges:
        if len(e.link_faces) == 1:
            n += 1
            a, b = find(e.verts[0].index), find(e.verts[1].index)
            if a != b:
                parent[a] = b
    return len({find(v) for v in list(parent)}), n
