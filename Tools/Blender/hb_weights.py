"""Analytic skin weights for Char_Human (rest-pose positions -> bone weights, max 4 influences)."""
import math
import numpy as np
import hb_shape as H


def ss(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


FINGERS = ["Index", "Middle", "Ring", "Little"]
FJ = {  # finger joint x positions (abs): MCP, PIP, DIP
    "Index": (0.790, 0.835, 0.863), "Middle": (0.792, 0.842, 0.874),
    "Ring": (0.789, 0.835, 0.865), "Little": (0.783, 0.819, 0.841),
}
FY = {n: H.FINGER_DEF[n.lower()][0][0][1] for n in FINGERS}
THUMB_J = [(0.725, -0.030, 1.425), (0.755, -0.058, 1.420), (0.780, -0.078, 1.418), (0.802, -0.094, 1.417), (0.820, -0.106, 1.4165)]
THUMB_MESH = H.THUMB


def _chain3(vals, joints, hw):
    """Weights for a chain of bones given a scalar position and joint positions (blend half-widths hw)."""
    return None


def weights_at(p):
    x, y, z = float(p[0]), float(p[1]), float(p[2])
    side = "Left" if x >= 0 else "Right"
    ax = abs(x)
    W = {}

    def add(name, w, s=None):
        if w > 1e-5:
            nm = name if s is None else (s + name)
            W[nm] = W.get(nm, 0.0) + w

    # ------------------------------------------------------------ trunk chain by height
    def trunk(z):
        t_hs = ss((z - 1.01) / 0.12)          # Hips -> Spine centred 1.07
        t_sc = ss((z - 1.18) / 0.14)          # Spine -> Chest centred 1.25
        t_cn = ss((z - 1.47) / 0.06)          # Chest -> Neck centred 1.50
        t_nh = ss((z - 1.555) / 0.07)         # Neck -> Head centred 1.59
        d = {}
        d["Hips"] = 1 - t_hs
        d["Spine"] = t_hs * (1 - t_sc)
        d["Chest"] = t_sc * (1 - t_cn)
        d["Neck"] = t_cn * (1 - t_nh)
        d["Head"] = t_nh
        return d

    T = trunk(z)

    # ------------------------------------------------------------ legs
    hw = 0.045 * ss((z - 0.84) / 0.06)
    fl = ss((x + hw) / (2 * hw + 1e-6)) if hw > 1e-5 else (1.0 if x > 0 else 0.0)
    f_leg = ss((1.02 - z) / 0.16)
    legW = {}
    if f_leg > 0:
        t_knee = ss((0.56 - z) / 0.10)
        t_ank = ss((0.108 - z) / 0.05)
        t_toe = ss((-y - 0.100) / 0.04) * t_ank
        up = 1 - t_knee
        low = t_knee * (1 - t_ank)
        foot = t_ank * (1 - t_toe)
        toe = t_ank * t_toe
        legW = {"UpperLeg": up, "LowerLeg": low, "Foot": foot, "Toes": toe}

    # ------------------------------------------------------------ arms
    arm = {}
    d_arm = math.hypot(max(0.17 - ax, 0.0), math.hypot(y - 0.02, z - 1.442) if ax >= 0.17 else (z - 1.442))
    a_m = (1 - ss((d_arm - 0.06) / 0.07)) * ss((ax - 0.07) / 0.07)
    f_below = max(ss((z - 1.355) / 0.06), ss((ax - 0.20) / 0.05))
    a_m *= f_below
    if ax > 0.45:
        a_m = 1.0
    if a_m > 0:
        c = 1 - ss((ax - 0.05) / 0.15)
        u = ss((ax - 0.12) / 0.14)
        s_ = max(0.0, 1 - c - u)
        t_elb = ss((ax - 0.40) / 0.10)
        t_wr = ss((ax - 0.672) / 0.050)
        arm["Chest"] = c
        arm["Shoulder"] = s_
        arm["UpperArm"] = u * (1 - t_elb)
        arm["LowerArm"] = u * t_elb * (1 - t_wr)
        hand = u * t_wr
        # thumb + fingers
        thumb_share = 0.0
        best = (1e9, 0, 0.0)
        if ax > 0.712 and hand > 0:
            tpts = np.array([[q[0], q[1], q[2]] for q in THUMB_MESH])
            pp = np.array([ax, y, z])
            for i in range(len(tpts) - 1):
                a_, b_ = tpts[i], tpts[i + 1]
                ab = b_ - a_
                t = float(np.clip(np.dot(pp - a_, ab) / np.dot(ab, ab), 0, 1))
                dd = float(np.linalg.norm(pp - (a_ + ab * t)))
                if dd < best[0]:
                    best = (dd, i, t)
            thumb_share = (1 - ss((best[0] - 0.014) / 0.014)) * ss((ax - 0.712) / 0.03)
            if y > -0.012:
                thumb_share = 0.0
        fs = 0.0
        if ax > 0.775 and hand > 0:
            ramp = ss((ax - 0.776) / 0.034)
            gs = {}
            for fn in FINGERS:
                dy = abs(y - FY[fn])
                gs[fn] = math.exp(-0.5 * (dy / 0.0088) ** 2) * ramp
            tot = sum(gs.values())
            if y < -0.050:
                gs = {k: 0.0 for k in gs}; tot = 0.0
            if tot > 1.0:
                gs = {k: v / tot for k, v in gs.items()}
            for fn, g in gs.items():
                g *= (1 - thumb_share)
                if g > 1e-4:
                    mc, pip, dip = FJ[fn]
                    t1 = ss((ax - pip) / 0.018 + 0.5)
                    t2 = ss((ax - dip) / 0.016 + 0.5)
                    arm.setdefault("_f", {})
                    arm["_f"][fn] = (g * hand, (1 - t1), t1 * (1 - t2), t1 * t2)
                    fs += g
        if thumb_share > 0:
            seg, t = best[1], best[2]
            pos = seg + t          # 0..4
            tp = 1 - ss((pos - 1.0) / 0.6 + 0.5)
            tpi = ss((pos - 1.0) / 0.6 + 0.5) * (1 - ss((pos - 2.0) / 0.6 + 0.5))
            tpd = ss((pos - 2.0) / 0.6 + 0.5)
            arm["_t"] = (thumb_share * hand, tp, tpi, tpd)
        arm["Hand"] = hand * max(0.0, 1 - min(1.0, fs + thumb_share))
    # ------------------------------------------------------------ combine
    # trunk and legs first
    base = {}
    for k, v in T.items():
        base[k] = v * (1 - f_leg)
    # crotch / hips remainder: legs take f_leg; hips keep rest of weight so the pelvis stays solid
    for k, v in legW.items():
        add(k, v * f_leg * fl, "Left")
        add(k, v * f_leg * (1 - fl), "Right")
    # when f_leg > 0 the trunk part (Hips) carries the rest
    base["Hips"] = base.get("Hips", 0.0) + 0.0
    for k, v in base.items():
        add(k, v * (1 - a_m))
    if a_m > 0:
        for k, v in arm.items():
            if k == "_f":
                for fn, (w, a, b, c3) in v.items():
                    add(fn + "Proximal", w * a * a_m, side)
                    add(fn + "Intermediate", w * b * a_m, side)
                    add(fn + "Distal", w * c3 * a_m, side)
            elif k == "_t":
                w, a, b, c3 = v
                add("ThumbProximal", w * a * a_m, side)
                add("ThumbIntermediate", w * b * a_m, side)
                add("ThumbDistal", w * c3 * a_m, side)
            elif k == "Chest":
                add("Chest", v * a_m)
            else:
                add(k, v * a_m, side)
        # a_m region of the trunk contributes remaining trunk weight (torso part) already via (1 - a_m)
    # legs contribute inside f_leg only; make sure trunk side of a_m uses leg-free trunk
    # prune to 4 and normalise
    items = sorted(W.items(), key=lambda kv: -kv[1])[:4]
    tot = sum(v for _, v in items)
    if tot < 1e-6:
        return {"Hips": 1.0}
    return {k: v / tot for k, v in items}


def prefix_fix(name):
    return name
