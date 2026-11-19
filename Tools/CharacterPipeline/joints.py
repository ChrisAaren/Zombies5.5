"""Stage 2: estimate joint positions from the shape of the PROXY mesh, write joints.json.

  blender -b --factory-startup --python joints.py -- --out work/testcourier/v001

Reads <out>/cleanup.blend (PROXY). Writes <out>/joints.json: {joint: {pos, confidence, method}} plus the
hand slice planes check.py reuses to count fingers on proxy and source.
method is "measured" (found from the shape), "narrowest" (limb cross-section minimum) or "estimated"
(standard body proportions used because the shape was ambiguous).
"""
import bpy, os, sys, math
import numpy as np
sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import pl_common as pc
import bl_skeleton as sk

A = pc.parse_args({"out": ""})
OUT = os.path.abspath(A["out"])
CFG = pc.load_config(OUT)
JC = CFG["joints"]
STEP = float(JC["slice_step_m"])

pc.open_blend(os.path.join(OUT, "cleanup.blend"))
V, T = pc.mesh_np(bpy.data.objects["PROXY"])
H = float(V[:, 2].max())
EDGES = pc.mesh_edges(T, len(V))
J = {}          # name -> dict(pos, confidence, method)
INFO = {}


def put(name, pos, conf, method):
    J[name] = {"pos": [float(x) for x in pos], "confidence": round(float(conf), 3), "method": method}


def hslice(z, minn=6):
    return [c for c in pc.slice_plane(V, T, (0, 0, z), (0, 0, 1)) if c["n"] >= minn]


def spans_center(c):
    return c["points"][:, 0].min() < -0.02 and c["points"][:, 0].max() > 0.02


def narrowest(ts, rs, default_t, lo_edge_conf=0.35):
    """Minimum of a smoothed radius profile. Returns (t, confidence, method)."""
    ts, rs = np.asarray(ts), np.asarray(rs)
    if len(rs) < 5:
        return default_t, 0.3, "estimated"
    k = np.ones(3) / 3
    sm = np.convolve(np.pad(rs, 1, mode="edge"), k, mode="valid")
    i = int(np.argmin(sm))
    clarity = (np.median(sm) - sm[i]) / max(np.median(sm), 1e-6)
    if i <= 1 or i >= len(sm) - 2 or clarity < 0.03:
        return default_t, lo_edge_conf + 0.1, "estimated"
    return float(ts[i]), min(0.95, 0.55 + 3.0 * clarity), "narrowest"


# ------------------------------------------------------------------ crotch, armpits
crotch_z = None
for z in np.arange(0.30 * H, 0.60 * H, STEP):
    if any(spans_center(c) for c in hslice(z)):
        crotch_z = float(z - STEP / 2)
        break
if crotch_z is None:
    crotch_z, crotch_conf = 0.47 * H, 0.3
else:
    crotch_conf = 0.9
INFO["crotch_z"] = crotch_z

armpit_z = None
for z in np.arange(0.80 * H, crotch_z + 0.12, -STEP):
    if len(hslice(z)) >= 3:
        armpit_z = float(z + STEP / 2)
        break
if armpit_z is None:
    armpit_z, armpit_conf = 0.74 * H, 0.3
else:
    armpit_conf = 0.9
INFO["armpit_z"] = armpit_z

# ------------------------------------------------------------------ neck
ts, rs = [], []
for z in np.arange(0.79 * H, 0.92 * H, 0.005):
    cs = hslice(z)
    c = max(cs, key=lambda q: q["n"]) if cs else None
    if c is not None:
        ts.append(z)
        rs.append(c["radius"])
neck_z, neck_conf, neck_m = narrowest(ts, rs, 0.855 * H)
INFO["neck_narrow_z"] = neck_z


def torso_y(z):
    cs = [c for c in hslice(z) if spans_center(c)]
    if cs:
        c = max(cs, key=lambda q: q["n"])
        return float(c["centroid"][1])
    return 0.0


# ------------------------------------------------------------------ legs
leg = {}
zc = crotch_z - 0.04 * H
for s, side in ((1, "Left"), (-1, "Right")):
    cs = [c for c in hslice(zc) if s * c["centroid"][0] > 0.02]
    c = max(cs, key=lambda q: q["n"]) if cs else None
    if c is None:
        leg[side] = (s * 0.09 * H / 1.8, 0.0)
    else:
        leg[side] = (float(c["centroid"][0]), float(c["centroid"][1]))

ankle = {}
KNEE_C = {}
for s, side in ((1, "Left"), (-1, "Right")):
    # follow the leg downward from the knee, nearest component to the previous centroid
    kz = 0.285 * H
    cs = [c for c in hslice(kz) if abs(c["centroid"][0] - leg[side][0]) < 0.10]
    prev = max(cs, key=lambda q: q["n"])["centroid"] if cs else np.array([leg[side][0], leg[side][1], kz])
    KNEE_C[side] = prev
    ts, rs, cens = [], [], []
    for z in np.arange(kz - 0.01, 0.02 * H, -0.004):
        cs = [c for c in hslice(z, 5) if np.linalg.norm(c["centroid"][:2] - prev[:2]) < 0.06]
        if not cs:
            continue
        c = min(cs, key=lambda q: np.linalg.norm(q["centroid"][:2] - prev[:2]))
        prev = c["centroid"]
        if 0.025 * H < z < 0.075 * H:
            ts.append(z)
            rs.append(c["radius"])
            cens.append(c["centroid"])
    ts, rs, cens = ts[::-1], rs[::-1], cens[::-1]
    az, aconf, am = narrowest(ts, rs, 0.045 * H)
    if cens:
        j = int(np.argmin(np.abs(np.array(ts) - az)))
        ac = cens[j]
    else:
        ac = np.array([leg[side][0], leg[side][1], az])
    ankle[side] = (float(ac[0]), float(ac[1]), az, aconf, am)

# ------------------------------------------------------------------ spine / head
hip_z = crotch_z + 0.045 * H
z0 = hip_z + 0.02 * H
zn = neck_z - 0.04 * H
spine_z = z0 + 0.19 * (zn - z0)
chest_z = z0 + 0.53 * (zn - z0)
put("Hips", (0, torso_y(z0), z0), crotch_conf, "measured")
put("Spine", (0, torso_y(spine_z), spine_z), 0.7, "estimated")
put("Chest", (0, torso_y(chest_z), chest_z), 0.7, "estimated")
put("Neck", (0, torso_y(zn), zn), neck_conf, neck_m)
hz = neck_z + 0.015 * H
hc = [c for c in hslice(0.95 * H) if c["n"] > 6]
hy = float(max(hc, key=lambda q: q["n"])["centroid"][1]) if hc else 0.0
put("Head", (0, torso_y(hz), hz), neck_conf, neck_m)
put("HeadTop", (0, hy, H), 0.9, "measured")

# ------------------------------------------------------------------ legs joints
for s, side in ((1, "Left"), (-1, "Right")):
    lx, ly = leg[side]
    put(side + "UpperLeg", (lx, ly, hip_z), crotch_conf * 0.9, "measured")
    kz = 0.285 * H
    kc = KNEE_C[side]
    put(side + "LowerLeg", (kc[0], kc[1], kz), 0.55, "estimated")
    ax, ay, az, aconf, am = ankle[side]
    put(side + "Foot", (ax, ay, az), aconf, am)
    # foot extents
    m = (np.abs(V[:, 0] - ax) < 0.07) & (V[:, 2] < 0.07 * H) & (V[:, 1] < ay + 0.20) & (V[:, 1] > ay - 0.40)
    fy = V[m][:, 1]
    toe_y, heel_y = float(fy.min()), float(fy.max())
    ball_y = heel_y + 0.72 * (toe_y - heel_y)
    bz = 0.014 * H
    put(side + "Toes", (ax, ball_y, bz), 0.75, "measured")
    put(side + "ToesEnd", (ax, toe_y + 0.012, bz * 0.8), 0.75, "measured")

# ------------------------------------------------------------------ arms
ARM_FRAC_ELBOW = 0.186 / 0.44
ARM_FRAC_WRIST = (0.186 + 0.146) / 0.44
FINGER_FRAC = {"Index": 0.42, "Middle": 0.45, "Ring": 0.43, "Little": 0.36}
PH_SPLIT = (0.47, 0.30, 0.23)
HAND = {}


def fit_axis(points):
    P = np.asarray(points)
    c = P.mean(axis=0)
    u, sv, vt = np.linalg.svd(P - c)
    d = vt[0]
    return c, d


for s, side in ((1, "Left"), (-1, "Right")):
    # 1. arm centroids in horizontal slices just below the armpit
    pts = []
    for z in np.arange(armpit_z - 0.04, armpit_z - 0.22, -STEP):
        cs = [c for c in hslice(z) if s * c["centroid"][0] > 0.10]
        if cs:
            pts.append(max(cs, key=lambda q: s * q["centroid"][0])["centroid"])
    c0, d = fit_axis(pts)
    if d[2] > 0:
        d = -d
    # 2. refine with perpendicular sections
    for it in range(2):
        pts = []
        for t in np.arange(0.10, 0.34, 0.02):
            p = c0 + d * (t - float(np.dot(np.array([0, 0, 0]), d)))
            # march from the fit centre along the axis
            p = c0 + d * (t - 0.17)
            cs = pc.slice_plane(V, T, p, d)
            c = pc.nearest_comp(cs, p)
            if c is not None and np.linalg.norm(c["centroid"] - p) < 0.05:
                pts.append(c["centroid"])
        if len(pts) >= 5:
            c0, d = fit_axis(pts)
            if d[2] > 0:
                d = -d
    # 3. shoulder joint: on the axis at z = armpit + 0.06 H
    zr = armpit_z + 0.06 * H
    root = c0 + d * ((zr - c0[2]) / d[2])
    rel = (V - root) @ d
    perp = np.linalg.norm((V - root) - np.outer(rel, d), axis=1)
    sel = (perp < 0.16) & (rel > 0.30)
    L = float(rel[sel].max())
    # 4. wrist = narrowest section; track the centroid along the arm
    ts, rs, cen = [], [], []
    p_prev = root + d * (0.68 * L)
    for t in np.arange(0.66 * L, 0.84 * L, 0.005):
        p = root + d * t
        cs = pc.slice_plane(V, T, p, d)
        c = pc.nearest_comp(cs, p)
        if c is not None and np.linalg.norm(c["centroid"] - p) < 0.05:
            ts.append(t)
            rs.append(c["radius"])
            cen.append(c["centroid"])
    tw, wconf, wm = narrowest(ts, rs, ARM_FRAC_WRIST * L)
    wp = root + d * tw
    cs = pc.slice_plane(V, T, wp, d)
    c = pc.nearest_comp(cs, wp)
    W = c["centroid"] if (c is not None and np.linalg.norm(c["centroid"] - wp) < 0.04) else wp
    # elbow: standard proportion (record the narrowest section as a hint)
    ts2, rs2 = [], []
    for t in np.arange(0.32 * L, 0.52 * L, 0.01):
        p = root + d * t
        c2 = pc.nearest_comp(pc.slice_plane(V, T, p, d), p)
        if c2 is not None:
            ts2.append(t)
            rs2.append(c2["radius"])
    te_n, econf, em = narrowest(ts2, rs2, ARM_FRAC_ELBOW * L)
    te = te_n if JC["snap_limb_joints_to_narrowest"] else ARM_FRAC_ELBOW * L
    elbow = root + d * te
    put(side + "UpperArm", root, 0.85, "measured")
    put(side + "LowerArm", elbow, 0.55, "estimated")
    put(side + "Hand", W, wconf, wm)
    clav = np.array([s * 0.03 * H / 1.8, root[1], root[2] + 0.01])
    put(side + "Shoulder", clav, 0.7, "measured")
    INFO[side + "_arm"] = {"axis_dir": d.tolist(), "length_root_to_tip": L, "elbow_narrowest_t": te_n,
                           "wrist_t": float(tw)}

    # 5. hand: slice across the palm/fingers (planes recorded for check.py), fingertips by geodesic maxima
    Lh = L - float(np.dot(W - root, d))
    HAND[side] = {"W": W, "d": d, "Lh": Lh, "root": root}
    planes = []
    for f in JC["hand_slice_fractions"]:
        o = W + d * (f * Lh)
        cs = [c for c in pc.slice_plane(V, T, o, d) if c["n"] >= 5 and np.linalg.norm(c["centroid"] - o) < 0.13]
        planes.append({"origin": o.tolist(), "normal": d.tolist(), "fraction": f, "count": len(cs)})
    HAND[side]["planes"] = planes
    tips, tinfo = pc.hand_tips(V, T, W, d, edges=EDGES)
    HAND[side]["tips"] = tips
    HAND[side]["tinfo"] = tinfo


for s, side in ((1, "Left"), (-1, "Right")):
    h = HAND[side]
    W, d, Lh = h["W"], h["d"], h["Lh"]
    names = ("Thumb", "Index", "Middle", "Ring", "Little")
    fingers = {}
    tips = h["tips"]
    if len(tips) == 5 and all(np.linalg.norm(t["pos"] - W) > 0.5 * Lh for t in tips):
        for nm, t in zip(names, tips):
            fingers[nm] = (t["dir"], t["pos"] - t["dir"] * 0.004, float(np.linalg.norm(t["pos"] - W)))
    conf_f, meth_f = 0.8, "measured"
    if not fingers:
        conf_f, meth_f = 0.3, "estimated"
        spread = {"Thumb": (-0.9, 0.55), "Index": (-0.25, 0.92), "Middle": (-0.05, 1.0), "Ring": (0.13, 0.95), "Little": (0.32, 0.8)}
        for nm in names:
            ang, ln = spread[nm]
            dv = d + np.array([0, ang, 0]) * 1.0
            dv = dv / np.linalg.norm(dv)
            fingers[nm] = (dv, W + dv * (Lh * ln), Lh * ln)
    ends = []
    for nm in names:
        dirv, tip, _ = fingers[nm]
        wl = np.linalg.norm(tip - W)
        if nm == "Thumb":
            base = W + (tip - W) * 0.25
            seg = (0.38, 0.34, 0.28)
            conf = conf_f * 0.9
        else:
            base = tip - dirv * (FINGER_FRAC[nm] * wl)
            seg = PH_SPLIT
            conf = conf_f
            ends.append(base)
        total = tip - base
        p1 = base + total * seg[0]
        p2 = base + total * (seg[0] + seg[1])
        put(side + nm + "Proximal", base, conf, meth_f)
        put(side + nm + "Intermediate", p1, conf, meth_f)
        put(side + nm + "Distal", p2, conf, meth_f)
        put(side + nm + "Tip", tip, conf, meth_f)
    put(side + "HandEnd", np.mean(ends, axis=0), conf_f, meth_f)
    INFO[side + "_hand"] = {"fingertips_found": len(tips), "wrist": W.tolist(), "hand_len": Lh,
                            "geodesic": h["tinfo"], "planes": h["planes"]}

missing = [n for n in sk.required_joints() if n not in J]
INFO["missing_joints"] = missing
data = {"height": H, "joints": J, "analysis": INFO,
        "hand_planes": {side: HAND[side]["planes"] for side in HAND},
        "hand_params": {side: {"wrist": HAND[side]["W"].tolist(), "axis": HAND[side]["d"].tolist(),
                               "hand_len": HAND[side]["Lh"]} for side in HAND}}
pc.write_json(os.path.join(OUT, "joints.json"), data)
print("JOINTS written", len(J), "missing", missing)
print("analysis", {k: v for k, v in INFO.items() if not isinstance(v, dict)})
for side in ("Left", "Right"):
    print(side, "hand planes", [(p["fraction"], p["count"]) for p in HAND[side]["planes"]], "tips", len(HAND[side]["tips"]))
print("STAGE2 DONE")
