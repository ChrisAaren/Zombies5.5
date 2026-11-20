"""Retarget 100STYLE BVH clips onto the Zombies 5.5 skeleton and compare against the procedural clips.

Run (Blender 5.2, headless):
  blender --background --factory-startup --python retarget_mocap.py -- [ClipName ...] [--nopreview] [--noexport] [--nometrics]

Method (BVH rest pose is an un-rotated T-pose, so every joint's world rotation IS its delta from rest):
  1. Parse the BVH as data (own parser; nothing from the file is executed), forward kinematics at 60 fps,
     convert Y-up/+Z-forward (cm) to Blender Z-up/-Y-forward (m).
  2. Pick one clean gait cycle from the Frame_Cuts.csv window: left-foot touchdowns (foot furthest ahead of the
     pelvis along the travel direction), choose the stride whose start/end pose and speed match best.
  3. Measure real-world speed from the original pelvis travel over the cycle.
  4. Align heading to -Y, mirror if requested, remove net travel (linear drift only; sway and bob stay),
     scale pelvis translation by the leg-length ratio, make a seamless loop (cross-blend the tail with the
     previous stride), resample to 30 fps.
  5. World delta per source joint -> world delta per our bone, converted to pose-bone quaternions exactly as
     make_animations.basis_from does (rest orientation conjugation). A vertical offset puts the lowest sole
     points on z = 0.
  6. Export Anim_Mocap_<Name>.fbx with the same exporter settings as make_animations.py (to mocap/out).
  7. Render side/front contact sheets with make_animations' proxy body and cameras, mocap vs procedural.
"""
import bpy, sys, os, math, json, csv
from mathutils import Vector, Quaternion, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.abspath(os.path.join(HERE, ".."))
sys.path.insert(0, BLENDER_DIR)
import make_animations as MA          # imported as a module: only its CLIPS / proxy / render helpers are used
import bl_skeleton as SK

SRC_DIR = os.path.join(BLENDER_DIR, "references", "mocap", "100style")
OUT_DIR = os.path.join(HERE, "out")
SHEET_TMP = os.path.join(HERE, "_sheets")
PREV_DIR = os.path.join(BLENDER_DIR, "previews")
FPS = 30
CMU_DIR = os.path.join(BLENDER_DIR, "references", "mocap", "cmu")
CUTS_FILE = os.path.join(SRC_DIR, "Frame_Cuts.csv")

# source (100STYLE) joint -> our bone
MAP = {"Hips": "Hips", "Chest2": "Spine", "Chest4": "Chest", "Neck": "Neck", "Head": "Head",
       "LeftCollar": "LeftShoulder", "LeftShoulder": "LeftUpperArm", "LeftElbow": "LeftLowerArm", "LeftWrist": "LeftHand",
       "RightCollar": "RightShoulder", "RightShoulder": "RightUpperArm", "RightElbow": "RightLowerArm", "RightWrist": "RightHand",
       "LeftHip": "LeftUpperLeg", "LeftKnee": "LeftLowerLeg", "LeftAnkle": "LeftFoot", "LeftToe": "LeftToes",
       "RightHip": "RightUpperLeg", "RightKnee": "RightLowerLeg", "RightAnkle": "RightFoot", "RightToe": "RightToes"}
MAP_CMU = {"Hips": "Hips", "Spine": "Spine", "Spine1": "Chest", "Neck": "Neck", "Head": "Head",
           "LeftShoulder": "LeftShoulder", "LeftArm": "LeftUpperArm", "LeftForeArm": "LeftLowerArm", "LeftHand": "LeftHand",
           "RightShoulder": "RightShoulder", "RightArm": "RightUpperArm", "RightForeArm": "RightLowerArm", "RightHand": "RightHand",
           "LeftUpLeg": "LeftUpperLeg", "LeftLeg": "LeftLowerLeg", "LeftFoot": "LeftFoot", "LeftToeBase": "LeftToes",
           "RightUpLeg": "RightUpperLeg", "RightLeg": "RightLowerLeg", "RightFoot": "RightFoot", "RightToeBase": "RightToes"}
# per source set: unit scale (m per BVH unit), capture fps, rest = T-pose in frame 0 (CMU/cgspeed) or zero rotations
SRCSET = {
    "100style": dict(dir=SRC_DIR, scale=0.01, fps=60.0, rest0=False, map=MAP,
                     hips="Hips", lhip="LeftHip", rhip="RightHip", lank="LeftAnkle", rank="RightAnkle",
                     ltoe="LeftToe", rtoe="RightToe", knee="LeftKnee"),
    "cmu": dict(dir=CMU_DIR, scale=0.056444, fps=120.0, rest0=True, map=MAP_CMU,
                hips="Hips", lhip="LeftUpLeg", rhip="RightUpLeg", lank="LeftFoot", rank="RightFoot",
                ltoe="LeftToeBase", rtoe="RightToeBase", knee="LeftLeg"),
}
CUR = {}      # active source set (filled per clip)

# output name -> (bvh file, movement code, mirror, procedural clip to compare, note).
# bvh file "NN_NN" = CMU Graphics Lab (cgspeed/una-dinosauria BVH), "Style_XX" = 100STYLE.
CLIP_DEFS = {
    "Walk":          ("35_01",  "CM", False, "Walk",     "CMU 35_01 walk"),
    "BriskWalk":     ("07_12",  "CM", False, "Jog",      "CMU 07_12 brisk walk"),
    "Jog":           ("35_17",  "CM", False, "Jog",      "CMU 35_17 run/jog"),
    "Run":           ("09_04",  "CM", False, "Run",      "CMU 09_04 run"),
    "Sprint":        ("16_46",  "CM", False, "Sprint",   "CMU 16_46 run (fastest in set)"),
    "SprintAlt":     ("16_45",  "CM", False, None,       "CMU 16_45 run"),
    "WalkBack":      ("136_25", "CM", False, "WalkBack", "CMU 136_25 normal walk backwards"),
    "CrouchWalk":    ("136_09", "CM", False, None,       "CMU 136_09 walk crouched"),
    "StrafeLeft":    ("Neutral_SW",   "SW", "left",  "StrafeLeft",  "100STYLE side-step walk, to character left"),
    "StrafeRight":   ("Neutral_SW",   "SW", "right", "StrafeRight", "100STYLE side-step walk, to character right"),
    "StrafeRunLeft": ("Neutral_SR",   "SR", "left",  None,          "100STYLE side-step run, to character left"),
    "Idle":          ("Neutral_ID",   "ID", False, "Idle",     "100STYLE idle"),
    "Shamble":       ("Zombie_FW",    "FW", False, "Shamble",  "100STYLE zombie walk"),
    "ShambleCmu":    ("104_41", "CM", False, "Shamble",  "CMU 104_41 ZombieWalk"),
    "ZombieRun":     ("Zombie_FR",    "FR", False, "EnemyJog", "100STYLE zombie run"),
    "ZombieStrafe":  ("Zombie_SW",    "SW", "left",  None,     "100STYLE zombie side-step"),
    "ZombieIdle":    ("Zombie_ID",    "ID", False, None,       "100STYLE zombie idle"),
    "OldWalk":       ("Old_FW",       "FW", False, None,       "100STYLE old person walk"),
    "LimpLeft":      ("LimpLeft_FW",  "FW", False, None,       "100STYLE limp, injured left"),
    "LimpRight":     ("LimpRight_FW", "FW", False, None,       "100STYLE limp, injured right"),
    "LimpCmu77":     ("77_19",  "CM", False, None,       "CMU 77_19 limping, hurt right leg"),
    "LimpCmu91":     ("91_16",  "CM", False, None,       "CMU 91_16 limp"),
}
# the six comparisons the task names; AimWalk compares the mocap Walk legs to the procedural aim walk
EXTRA_SHEETS = {"AimWalk": ("Walk", "AimWalk")}


# ============================================================================ BVH data parser (no execution)
class Bvh:
    pass


def parse_bvh(path, f0, f1):
    """Parse hierarchy + motion frames f0..f1 (inclusive, 0-based). Returns Bvh with joints and frames."""
    with open(path, "r") as fh:
        lines = fh.read().split("\n")
    b = Bvh()
    b.joints = []          # dict(name, parent, offset, chans)
    stack = []
    i = 0
    cur = None
    while i < len(lines):
        t = lines[i].split()
        i += 1
        if not t:
            continue
        if t[0] in ("ROOT", "JOINT"):
            cur = dict(name=t[1], parent=stack[-1] if stack else None, offset=(0, 0, 0), chans=[])
            b.joints.append(cur)
        elif t[0] == "End":
            cur = None
        elif t[0] == "OFFSET" and cur is not None:
            cur["offset"] = tuple(float(x) for x in t[1:4])
        elif t[0] == "CHANNELS" and cur is not None:
            cur["chans"] = t[2:2 + int(t[1])]
        elif t[0] == "{":
            stack.append(cur["name"] if cur is not None else None)
        elif t[0] == "}":
            stack.pop()
        elif t[0] == "MOTION":
            break
    b.nframes = int(lines[i].split()[1]); i += 1
    b.dt = float(lines[i].split()[2]); i += 1
    f1 = min(f1, b.nframes - 1)
    b.f0 = f0
    b.rest_row = [float(x) for x in lines[i].split()]
    b.rows = []
    for fr in range(f0, f1 + 1):
        b.rows.append([float(x) for x in lines[i + fr].split()])
    return b


C = Matrix(((1, 0, 0), (0, 0, -1), (0, 1, 0)))     # BVH (x, y up, z fwd) -> Blender (x, -z, y)
CT = C.transposed()


def _axrot(ch, deg):
    a = math.radians(deg)
    return Matrix.Rotation(a, 3, ch[0])


def fk_frames(b, keep_pos, scale, rest0):
    """Per frame: {joint: world rotation Matrix3 (Blender frame)}, and world positions (m) for keep_pos joints."""
    names = [j["name"] for j in b.joints]
    rots, poss = [], []
    rest = None
    rows = ([b.rest_row] if rest0 else []) + b.rows
    for ri, row in enumerate(rows):
        pos = {}; rot = {}
        c = 0
        for j in b.joints:
            vals = row[c:c + len(j["chans"])]; c += len(j["chans"])
            tr = Vector(j["offset"]); R = Matrix.Identity(3)
            for ch, v in zip(j["chans"], vals):
                if ch.endswith("position"):
                    tr[{"X": 0, "Y": 1, "Z": 2}[ch[0]]] = v
                else:
                    R = R @ _axrot(ch, v)
            p = j["parent"]
            if p is None:
                rot[j["name"]] = R; pos[j["name"]] = tr
            else:
                rot[j["name"]] = rot[p] @ R
                pos[j["name"]] = pos[p] + rot[p] @ tr
        if rest0 and ri == 0:
            rest = {n: (C @ rot[n] @ CT).to_quaternion() for n in names}
            continue
        q = {n: (C @ rot[n] @ CT).to_quaternion() for n in names}
        if rest0:
            q = {n: q[n] @ rest[n].inverted() for n in names}
        rots.append(q)
        poss.append({n: (C @ pos[n]) * scale for n in keep_pos})
    return rots, poss


# ============================================================================ cycle selection
def cuts_window(style, code):
    with open(CUTS_FILE) as fh:
        for row in csv.DictReader(fh):
            if row["STYLE_NAME"] == style:
                return int(row[code + "_START"]), int(row[code + "_STOP"])
    raise KeyError(style)


def qdiff_deg(a, b):
    return math.degrees(2 * math.acos(min(1.0, abs(a.dot(b)))))


def pose_mismatch(rots, poss, i, j):
    s = 0.0
    for sj in CUR["map"]:
        s += qdiff_deg(rots[i][sj], rots[j][sj])
    s /= len(CUR["map"])
    hn = CUR["hips"]
    return s + abs(poss[i][hn].z - poss[j][hn].z) * 300.0


def find_peaks(sig, minsep, lo, hw):
    pk = []
    for i in range(hw, len(sig) - hw):
        if sig[i] >= lo and sig[i] == max(sig[i - hw:i + hw + 1]):
            if not pk or i - pk[-1] >= minsep:
                pk.append(i)
    return pk


def pick_cycle(rots, poss, moving, W):
    """Return (s, e) window indices of the best loop; s >= W so there is a lead-in for the loop blend."""
    n = len(rots)
    if moving:
        fps = CUR["fps"]; hn = CUR["hips"]
        H0 = poss[0][hn]; H1 = poss[-1][hn]
        d = Vector((H1.x - H0.x, H1.y - H0.y, 0)).normalized()
        sig = []
        for p in poss:
            sig.append((p[CUR["lank"]] - p[hn]).dot(d))
        sm = [sum(sig[max(0, k - 2):k + 3]) / len(sig[max(0, k - 2):k + 3]) for k in range(n)]
        mean = sum(sm) / n
        pk = find_peaks(sm, int(0.3 * fps), mean, int(0.1 * fps))
        cands = []
        for a, bb in zip(pk, pk[1:]):
            L = bb - a
            if 0.4 * fps <= L <= 2.5 * fps and (a >= W + 2 or bb + W + 2 < n):
                dur = L / fps
                sp = (poss[bb][hn] - poss[a][hn]).xy.length / dur
                cands.append((a, bb, sp))
        if not cands:
            # short clip (one or two strides): pose-matching brute force, L in 0.5..1.6 s
            best = None
            for L in range(int(0.5 * fps), int(1.6 * fps) + 1, 2):
                for a_ in range(0, n - L - 1, 2):
                    if not (a_ >= W + 2 or a_ + L + W + 2 < n):
                        continue
                    m = pose_mismatch(rots, poss, a_, a_ + L) + 4.0 * abs(L / fps - 0.7)
                    if best is None or m < best[0]:
                        best = (m, a_, a_ + L)
            if best is None:
                raise RuntimeError("no gait cycle found (n=%d)" % n)
            mode = "tail" if best[1] >= W + 2 else "head"
            return best[1], best[2], mode
        vs = sorted(c[2] for c in cands); vmed = vs[len(vs) // 2]
        lens = sorted(c[1] - c[0] for c in cands); lmed = lens[len(lens) // 2]
        best = min(cands, key=lambda c: pose_mismatch(rots, poss, c[0], c[1])
                   + 60.0 * abs(c[2] / max(vmed, 1e-6) - 1.0) + 40.0 * abs((c[1] - c[0]) / lmed - 1.0))
        mode = "tail" if best[0] >= W + 2 else "head"
        return best[0], best[1], mode
    # idle: brute-force best loop of 3..6 s
    best = None
    fps = CUR["fps"]
    for L in range(int(3 * fps), min(int(6 * fps) + 1, n - W - 4), int(0.33 * fps)):
        for a in range(W + 2, n - L - 2, int(0.25 * fps)):
            m = pose_mismatch(rots, poss, a, a + L)
            if best is None or m < best[0]:
                best = (m, a, a + L)
    return best[1], best[2], "tail"


# ============================================================================ retarget a clip
def smooth01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


def build_clip(name):
    fname, code, mirror, proc, note = CLIP_DEFS[name]
    is_cmu = fname[0].isdigit()
    CUR.clear(); CUR.update(SRCSET["cmu" if is_cmu else "100style"])
    SRC_FPS = CUR["fps"]
    if is_cmu:
        a, b = 1, 1 + int(40 * SRC_FPS)
    else:
        a, b = cuts_window(fname.split("_")[0], code)
        b = min(b, a + int(40 * SRC_FPS))
    bvh = parse_bvh(os.path.join(CUR["dir"], fname + ".bvh"), a, b)
    HN, LH, RH, LA, RA, LT, RT = (CUR[k] for k in ("hips", "lhip", "rhip", "lank", "rank", "ltoe", "rtoe"))
    keep = [HN, LH, RH, LA, RA, LT, RT]
    rots, poss = fk_frames(bvh, keep, CUR["scale"], CUR["rest0"])
    MAPC = CUR["map"]

    # mirror: the source "left" travel clips; decide direction after alignment for strafes
    def do_mirror(r, p):
        def mq(q): return Quaternion((q.w, q.x, -q.y, -q.z))
        sw = lambda n: n.replace("Left", "@").replace("Right", "Left").replace("@", "Right")
        r2 = [{sw(n): mq(q) for n, q in fr.items()} for fr in r]
        p2 = [{sw(n): Vector((-v.x, v.y, v.z)) for n, v in fr.items()} for fr in p]
        return r2, p2

    moving = code != "ID"
    W = int(0.20 * SRC_FPS)
    s, e, mode = pick_cycle(rots, poss, moving, W)

    # heading alignment: mean facing of the pelvis over the cycle -> -Y
    def facing_yaw(i):
        v = rots[i][HN] @ Vector((0, -1, 0))
        return math.atan2(v.y, v.x)
    fx = sum(math.cos(facing_yaw(i)) for i in range(s, e + 1)); fy = sum(math.sin(facing_yaw(i)) for i in range(s, e + 1))
    yaw = math.atan2(fy, fx)
    Rz = Matrix.Rotation(-math.pi / 2 - yaw, 3, 'Z')
    Rzq = Rz.to_quaternion()
    rots = [{n: Rzq @ q for n, q in fr.items()} for fr in rots]
    poss = [{n: Rz @ v for n, v in fr.items()} for fr in poss]

    if mirror == "left":
        # travel direction should be +X (character left); otherwise mirror
        tr = poss[e][HN] - poss[s][HN]
        if tr.x < 0:
            rots, poss = do_mirror(rots, poss)
    elif mirror == "right":
        tr = poss[e][HN] - poss[s][HN]
        if tr.x > 0:
            rots, poss = do_mirror(rots, poss)

    dur = (e - s) / SRC_FPS
    travel = poss[e][HN] - poss[s][HN]
    travel.z = 0
    speed_src = travel.length / dur
    dirv = travel.normalized() if travel.length > 1e-6 and speed_src > 0.15 else Vector((0, 0, 0))
    if speed_src <= 0.15:
        speed_src = 0.0
        travel = Vector((0, 0, 0))

    # leg-length ratio (hip joint -> ankle path)
    jn = {j["name"]: j for j in bvh.joints}
    src_leg = sum(Vector(jn[k]["offset"]).length for k in (CUR["knee"], CUR["lank"])) * CUR["scale"]
    our_leg = MA.LEN["LeftUpperLeg"] + MA.LEN["LeftLowerLeg"]
    ratio = our_leg / src_leg

    def raw_q(f, sj):
        i0 = int(math.floor(f)); t = f - i0
        i0 = max(0, min(len(rots) - 1, i0)); i1 = min(len(rots) - 1, i0 + 1)
        return rots[i0][sj].slerp(rots[i1][sj], t) if t > 1e-9 else rots[i0][sj].copy()

    def raw_p(f, jn_):
        i0 = int(math.floor(f)); t = f - i0
        i0 = max(0, min(len(poss) - 1, i0)); i1 = min(len(poss) - 1, i0 + 1)
        return poss[i0][jn_].lerp(poss[i1][jn_], t)

    L = float(e - s)

    def at(f):
        """Looping, in-place source pose at fractional source frame f in [s, e]: (quats, pos dict)."""
        def one(ff):
            q = {sj: raw_q(ff, sj) for sj in MAPC}
            drift = travel * ((ff - s) / L)
            p = {k_: raw_p(ff, k_) - drift for k_ in keep}
            return q, p
        q, p = one(f)
        if mode == "tail":
            u = smooth01((f - (e - W)) / W); shift = -L
        else:      # no lead-in available: blend the head toward the end of the stride (f + L)
            u = 1.0 - smooth01((f - s) / W); shift = L
        if u > 0:
            q2, p2 = one(f + shift)
            q = {sj: q[sj].slerp(q2[sj], u) for sj in q}
            p = {k_: p[k_].lerp(p2[k_], u) for k_ in p}
        return q, p

    N = max(8, int(round(dur * FPS)))
    # ground height of the source: lowest toe joints over the cycle
    toe_lows = sorted(min(poss[i][LT].z, poss[i][RT].z) for i in range(s, e + 1))
    ground_src = toe_lows[int(len(toe_lows) * 0.03)] - 0.025 / ratio
    samples = []
    for k in range(N + 1):
        samples.append(at(s + (k % N) * L / N) if k < N else at(float(e)))
    # mean in-place xy of the hip mid-point -> origin
    mids = [((p[LH] + p[RH]) * 0.5) for _, p in samples[:N]]
    cx = sum(m.x for m in mids) / N; cy = sum(m.y for m in mids) / N
    info = dict(mode=mode, name=name, src=fname, window=(a, b), cycle_src=(a + s, a + e), cycle_len_60=int(L), frames=N,
                speed_src_mps=round(speed_src, 3), speed_game_mps=round(speed_src * ratio, 3),
                ratio=round(ratio, 4), dir=(round(dirv.x, 3), round(dirv.y, 3)), note=note, proc=proc,
                heading_deg=round(math.degrees(yaw), 1))
    return dict(N=N, samples=samples, mids=(cx, cy), ground=ground_src, ratio=ratio, info=info,
                speed=speed_src * ratio, dir=dirv)


def INV_MAP():
    return {v: k for k, v in CUR["map"].items()}


def target_D(q_src):
    """src joint quats -> {our bone: world delta quat}, filling unmapped bones from the parent."""
    D = {}
    for n in MA.NAMES:
        if n in INV_MAP():
            D[n] = q_src[INV_MAP()[n]].copy()
        else:
            D[n] = D[MA.PAR[n]].copy() if MA.PAR[n] else Quaternion()
    return D


HIP_OFF = Vector((0, 0, 0.93 - 0.97))      # hip-joint midpoint relative to the Hips bone head (rest)


def pose_from(clip, k, dz, cont):
    """Set pose-bone rotations + hips location for output frame k. Returns hip-joint midpoint ok."""
    q_src, p = clip["samples"][k]
    D = target_D(q_src)
    cx, cy = clip["mids"]; ratio = clip["ratio"]
    mid = (p[CUR["lhip"]] + p[CUR["rhip"]]) * 0.5
    tgt = Vector(((mid.x - cx) * ratio, (mid.y - cy) * ratio, (mid.z - clip["ground"]) * ratio + dz))
    head = tgt - (D["Hips"] @ HIP_OFF)
    disp = head - MA.HEAD["Hips"]
    for n in MA.NAMES:
        par = MA.PAR[n]
        r = D[n] if par is None else D[par].inverted() @ D[n]
        rq = MA.RESTQ[n]
        q = rq.inverted() @ r @ rq
        pq = cont.get(n)
        if pq is not None and pq.dot(q) < 0:
            q = Quaternion((-q.w, -q.x, -q.y, -q.z))
        cont[n] = q
        MA.ARM.pose.bones[n].rotation_quaternion = q
    MA.ARM.pose.bones["Hips"].location = MA.RESTQ["Hips"].inverted() @ disp


def sole_min_z():
    ARM = MA.ARM
    zs = []
    for sd in ("Left", "Right"):
        nm = sd + "Foot"
        m = ARM.pose.bones[nm].matrix
        inv = ARM.data.bones[nm].matrix_local.inverted()
        x = MA.HEAD[nm].x
        for y in (0.08, -0.105):
            zs.append((m @ (inv @ Vector((x, y, 0.0)))).z)
    return min(zs)


def author_mocap(clip, name):
    ARM = MA.ARM
    sc = bpy.context.scene
    if ARM.animation_data:
        ARM.animation_data_clear()
    for pb in ARM.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    N = clip["N"]
    # pass 1: lowest sole points over the cycle (3rd percentile) -> vertical offset
    lows = []
    cont = {}
    for k in range(N):
        pose_from(clip, k, 0.0, cont)
        bpy.context.view_layer.update()
        lows.append(sole_min_z())
    lows.sort()
    dz = -lows[int(len(lows) * 0.08)]
    clip["dz"] = dz
    ARM.animation_data_create()
    act = bpy.data.actions.new("Anim_Mocap_" + name)
    ARM.animation_data.action = act
    cont = {}
    for k in range(N + 1):
        pose_from(clip, k, dz, cont)
        for n in MA.NAMES:
            ARM.pose.bones[n].keyframe_insert("rotation_quaternion", frame=k)
        ARM.pose.bones["Hips"].keyframe_insert("location", frame=k)
    sc.frame_start = 0; sc.frame_end = N
    sc.frame_set(0)
    return act


def export_fbx(name):
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    MA.ARM.select_set(True)
    bpy.context.view_layer.objects.active = MA.ARM
    bpy.context.scene.name = "Anim_Mocap_" + name
    path = os.path.join(OUT_DIR, "Anim_Mocap_%s.fbx" % name)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_unit_scale=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                             bake_anim_simplify_factor=0.0)
    return path


# ============================================================================ metrics (same code for both sets)
def measure(N, speed, dirv):
    """Gait metrics from the baked pose over frames 0..N (frame N == 0)."""
    ARM = MA.ARM
    sc = bpy.context.scene
    pbs = ARM.pose.bones
    rec = []
    for fr in range(N + 1):
        sc.frame_set(fr)
        r = {}
        hips = pbs["Hips"].head.copy()
        r["hips"] = hips
        for sd, sg in (("Left", 1.0), ("Right", -1.0)):
            nm = sd + "Foot"
            m = pbs[nm].matrix; inv = ARM.data.bones[nm].matrix_local.inverted()
            x = MA.HEAD[nm].x
            pts = [m @ (inv @ Vector((x, y, 0.0))) for y in (0.08, -0.105)]
            low = min(range(2), key=lambda i: pts[i].z)
            hip = pbs[sd + "UpperLeg"].head; kn = pbs[sd + "LowerLeg"].head; an = pbs[sd + "Foot"].head
            ln = (an - hip)
            t = (kn - hip).dot(ln) / ln.dot(ln)
            lat = ((kn - (hip + ln * t)).x) * sg
            r[sd] = dict(contact=pts[low].z < 0.02, pt=pts[low], ptype=low, lat=lat, ank=an.copy(),
                         knee=math.degrees(math.acos(max(-1, min(1, (kn - hip).normalized().dot((an - kn).normalized()))))),
                         hand=pbs[sd + "Hand"].head.copy())
        hb = pbs["Hips"]
        D = hb.matrix.to_3x3() @ ARM.data.bones["Hips"].matrix_local.to_3x3().inverted()
        v = D @ Vector((1, 0, 0))
        r["yaw"] = math.degrees(math.atan2(v.y, v.x)); r["obl"] = math.degrees(math.asin(max(-1, min(1, v.z))))
        rec.append(r)
    out = {}
    out["cadence_spm"] = round(3600.0 / N, 1)
    out["speed_mps"] = round(speed, 2)
    out["step_len_m"] = round(speed * N / FPS / 2.0, 3)
    sp = [abs(r["Left"]["ank"].x - r["Right"]["ank"].x) for r in rec[:N]]
    out["step_width_m"] = round(sum(sp) / N, 3)
    out["step_width_min_m"] = round(min(sp), 3)
    sw = [r[sd]["lat"] for r in rec[:N] for sd in ("Left", "Right") if not r[sd]["contact"]]
    st = [r[sd]["lat"] for r in rec[:N] for sd in ("Left", "Right") if r[sd]["contact"]]
    out["knee_lat_swing_max_cm"] = round(max(sw) * 100, 1) if sw else None
    out["knee_lat_stance_max_cm"] = round(max(st) * 100, 1) if st else None
    out["knee_lat_mean_cm"] = round(sum(sw + st) / len(sw + st) * 100, 1)
    yw = [r["yaw"] for r in rec[:N]]; ob = [r["obl"] for r in rec[:N]]
    out["pelvis_rot_pp_deg"] = round(max(yw) - min(yw), 1)
    out["pelvis_obl_pp_deg"] = round(max(ob) - min(ob), 1)
    hz = [r["hips"].z for r in rec[:N]]
    out["bob_pp_cm"] = round((max(hz) - min(hz)) * 100, 1)
    asw = []
    for sd in ("Left", "Right"):
        ys = [r[sd]["hand"].y - r["hips"].y for r in rec[:N]]
        asw.append(max(ys) - min(ys))
    out["arm_swing_pp_cm"] = round(sum(asw) / 2 * 100, 1)
    out["knee_flex_peak_deg"] = round(max(r[sd]["knee"] for r in rec[:N] for sd in ("Left", "Right")), 1)
    out["stance_pct"] = round(100.0 * sum(1 for r in rec[:N] for sd in ("Left", "Right") if r[sd]["contact"]) / (2 * N), 1)
    # foot slide in place at the clip speed: planted sole point vs expected -dir*speed
    errs = []
    exp = Vector((-dirv[0], -dirv[1], 0.0)) * speed
    for i in range(N):
        for sd in ("Left", "Right"):
            a, b = rec[i][sd], rec[i + 1][sd]
            if a["contact"] and b["contact"] and a["ptype"] == b["ptype"] and abs(b["pt"].z - a["pt"].z) < 0.004:
                d = (b["pt"] - a["pt"]) * FPS
                errs.append((Vector((d.x, d.y, 0)) - exp).length)
    if errs:
        es = sorted(errs)
        out["slide_mean_mps"] = round(sum(es) / len(es), 3)
        out["slide_p90_mps"] = round(es[int(len(es) * 0.9)], 3)
        out["slide_median_mps"] = round(es[len(es) // 2], 3)
        out["slide_pct_of_speed"] = round(100 * sum(es) / len(es) / max(speed, 0.1), 1)
    else:
        out["slide_mean_mps"] = out["slide_p90_mps"] = out["slide_pct_of_speed"] = out["slide_median_mps"] = None
    return out


# ============================================================================ sheets
def compose(pa, pb, outp):
    import numpy as np
    ims = []
    for p in (pa, pb):
        img = bpy.data.images.load(p)
        ims.append(np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4))
        bpy.data.images.remove(img)
    w = min(i.shape[1] for i in ims)
    sep = np.ones((8, w, 4), dtype=np.float32) * np.array([0.9, 0.2, 0.2, 1.0], dtype=np.float32)
    # image rows are bottom-up: mocap (bottom block) first, then separator, then procedural on top
    sheet = np.concatenate([ims[1][:, :w], sep, ims[0][:, :w]], axis=0)
    h = sheet.shape[0]
    out = bpy.data.images.new("cmp", w, h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = outp
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def sheet_for(label, N, loop=True):
    fr = [N * i / 8.0 for i in range(8)]
    MA.render_sheet(label, fr)
    return os.path.join(SHEET_TMP, "Anim_%s.png" % label)


# ============================================================================ main
def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    flags = [a for a in argv if a.startswith("--")]
    names = [a for a in argv if not a.startswith("--")] or list(CLIP_DEFS)
    do_prev = "--nopreview" not in flags; do_exp = "--noexport" not in flags
    os.makedirs(SHEET_TMP, exist_ok=True); os.makedirs(OUT_DIR, exist_ok=True)
    MA.PREV = SHEET_TMP
    MA.setup_scene()
    if do_prev:
        MA.build_proxy(); MA.setup_render()
    results = {}; proc_cache = {}; sheets = {}

    def proc_measure(pn):
        if pn in proc_cache:
            return proc_cache[pn]
        cl = MA.CLIPS[pn]
        MA.author(cl)
        lo = cl.get("loco")
        m = None
        if lo is not None:
            m = measure(cl["frames"], lo.speed, (lo.dir[0], lo.dir[1]))
        sh = None
        if do_prev:
            sh = sheet_for("Proc_" + pn, cl["frames"])
        proc_cache[pn] = (m, sh, cl["frames"])
        return proc_cache[pn]

    mocap_sheet = {}
    for nm in names:
        try:
            clip = build_clip(nm)
        except RuntimeError as ex:
            print("FAILED", nm, ex); continue
        author_mocap(clip, nm)
        info = clip["info"]; info["dz"] = round(clip["dz"], 3)
        if nm not in ("Idle", "ZombieIdle"):
            if "--nometrics" not in flags:
                info["metrics"] = measure(clip["N"], clip["speed"], (clip["dir"].x, clip["dir"].y))
        if do_exp:
            info["fbx"] = export_fbx(nm)
        if do_prev:
            mocap_sheet[nm] = sheet_for("Mocap_" + nm, clip["N"])
        results[nm] = info
        print("MOCAP", nm, json.dumps(info))
    if "--nometrics" not in flags:
        pass
    # comparisons: procedural measurements + side-by-side sheets
    pairs = []
    for nm in [n_ for n_ in names if n_ in results]:
        pn = CLIP_DEFS[nm][3]
        if pn and pn in MA.CLIPS:
            pairs.append((nm, pn, nm))
    for label, (mn, pn) in EXTRA_SHEETS.items():
        if mn in results:
            pairs.append((mn, pn, label))
    for mn, pn, label in pairs:
        m, sh, n_ = proc_measure(pn)
        results[mn].setdefault("proc_compare", {})[pn] = m
        if do_prev and sh:
            outp = os.path.join(PREV_DIR, "Mocap_vs_Procedural_%s.png" % label)
            compose(sh, mocap_sheet[mn], outp)
            results[mn].setdefault("sheets", []).append(outp)
            print("SHEET", outp)
    with open(os.path.join(OUT_DIR, "results.json"), "w") as fh:
        json.dump(results, fh, indent=1, default=str)
    print("DONE", len(results))


main()
