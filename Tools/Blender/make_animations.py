"""Zombies 5.5 humanoid animation clips, authored procedurally in Blender (headless).

Run:  blender --background --factory-startup --python make_animations.py -- [ClipName ...] [--nopreview] [--noexport]

Approach
  * The armature comes only from bl_skeleton.build_armature() (T-pose rest).
  * Every clip is a function frame -> channel dict (hips offset, spine/neck/head eulers, leg IK
    ankle targets or FK angles, arm IK wrist targets or FK angles, finger curl).
  * solve() turns channels into an absolute world rotation per bone (rotation delta from the rest
    pose), with forward kinematics for joint positions, two-bone IK for legs/arms, head stabilisation.
  * Those are converted to pose-bone quaternions (+ Hips location), keyed every frame (Bezier,
    auto-clamped handles), baked and exported one clip per FBX.
  * Locomotion uses a real foot model: heel strike -> flat -> heel-off -> toe-off roll with the
    contact point planted on the ground (ground slides at the clip speed), Hermite swing with
    clearance, and the pelvis height solved from leg reach, so there is no foot sliding.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Quaternion, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bl_skeleton as SK

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Art", "Animations")
PREV = os.path.join(HERE, "previews")
FPS = 30
TAU = 2 * math.pi
rad = math.radians


# ============================================================================ math helpers
def qx(d): return Quaternion((1, 0, 0), rad(d))
def qy(d): return Quaternion((0, 1, 0), rad(d))
def qz(d): return Quaternion((0, 0, 1), rad(d))
def eul(x=0.0, y=0.0, z=0.0): return qz(z) @ qy(y) @ qx(x)   # x first, then y, then z (world axes)
def lerp(a, b, t): return a + (b - a) * t
def clamp(x, a, b): return a if x < a else b if x > b else x
def smooth(x):
    x = clamp(x, 0.0, 1.0)
    return x * x * x * (x * (x * 6 - 15) + 10)


def orth(v, ref):
    """v made perpendicular to ref (ref unit), normalized."""
    w = v - ref * v.dot(ref)
    if w.length < 1e-6:
        w = Vector((1, 0, 0)) - ref * ref.x
        if w.length < 1e-6:
            w = Vector((0, 1, 0)) - ref * ref.y
    return w.normalized()


def frame_rot(u1, u2, v1, v2):
    """World rotation taking rest direction u1 -> v1 and (the part of) u2 perpendicular to u1 -> that of v2."""
    u1 = u1.normalized(); v1 = v1.normalized()
    u2 = orth(u2, u1); v2 = orth(v2, v1)
    U = Matrix((u1, u2, u1.cross(u2))).transposed()
    V = Matrix((v1, v2, v1.cross(v2))).transposed()
    return (V @ U.transposed()).to_quaternion()


def herm(y0, y1, m0, m1, w):
    w2 = w * w; w3 = w2 * w
    return (2 * w3 - 3 * w2 + 1) * y0 + (w3 - 2 * w2 + w) * m0 + (-2 * w3 + 3 * w2) * y1 + (w3 - w2) * m1


def pchip_eval(ts, ys, t, periodic=False):
    """Monotone cubic (Fritsch-Carlson) interpolation; periodic wraps with period ts[-1]-ts[0]."""
    n = len(ts)
    if periodic:
        per = ts[-1] - ts[0]
        t = ts[0] + ((t - ts[0]) % per)
        ts = [ts[-2] - per] + list(ts) + [ts[1] + per]
        ys = [ys[-2]] + list(ys) + [ys[1]]
        n += 2
    else:
        if t <= ts[0]: return ys[0]
        if t >= ts[-1]: return ys[-1]
    h = [ts[i + 1] - ts[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0.0
        else:
            w1 = 2 * h[i] + h[i - 1]; w2 = h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
    if not periodic:
        m[0] = 0.0; m[-1] = 0.0
    else:
        m[0] = m[1]; m[-1] = m[-2]
    i = 0
    while i < n - 2 and t > ts[i + 1]:
        i += 1
    w = (t - ts[i]) / h[i]
    return herm(ys[i], ys[i + 1], m[i] * h[i], m[i + 1] * h[i], w)


# ============================================================================ skeleton data
TABLE = SK.bone_table()
HEAD = {b[0]: Vector(b[1]) for b in TABLE}
TAIL = {b[0]: Vector(b[2]) for b in TABLE}
PAR = {b[0]: b[3] for b in TABLE}
NAMES = [b[0] for b in TABLE]
REST_DIR = {n: (TAIL[n] - HEAD[n]).normalized() for n in NAMES}
LEN = {n: (TAIL[n] - HEAD[n]).length for n in NAMES}
SIDE = {'L': ("Left", 1.0), 'R': ("Right", -1.0)}
FINGERS = ["Index", "Middle", "Ring", "Little"]
JOINTS = ["Proximal", "Intermediate", "Distal"]
RESTQ = {}      # filled from the real armature
ARM = None


def setup_scene():
    global ARM
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ARM = SK.build_armature("CharacterRig")
    for b in ARM.data.bones:
        RESTQ[b.name] = b.matrix_local.to_3x3().to_quaternion()
    ARM.rotation_mode = 'QUATERNION'
    for pb in ARM.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.render.fps_base = 1.0
    return ARM


# ============================================================================ channels
def default_channels():
    c = {}
    for n in ("hip_tx", "hip_ty", "hip_tz", "hip_rx", "hip_ry", "hip_rz",
              "sp_rx", "sp_ry", "sp_rz", "ch_rx", "ch_ry", "ch_rz",
              "nk_rx", "nk_ry", "nk_rz", "hd_rx", "hd_ry", "hd_rz",
              "stab", "look_rx", "look_ry", "look_rz"):
        c[n] = 0.0
    for s in "LR":
        for n in ("shu", "shf",                       # shoulder (clavicle) up / forward
                  "LAx", "LAy", "LAz",                # not used (placeholder replaced below)
                  ):
            pass
        c[s + "shu"] = 0.0; c[s + "shf"] = 0.0
        # leg IK
        for n in ("Ax", "Ay", "Az", "Fp", "Fy", "Fr", "T"):
            c[s + n] = 0.0
        c[s + "Ax"] = 0.095 * SIDE[s][1]; c[s + "Ay"] = 0.02; c[s + "Az"] = 0.085
        # knee pole direction (world); default is the standing knee-forward pole
        c[s + "Px"] = 0.03 * SIDE[s][1]; c[s + "Py"] = -1.0; c[s + "Pz"] = 0.0
        # knee-plant (get-up clips only, see gu_resolve): weight, knee ground xy, shin pitch
        c[s + "Kw"] = 0.0; c[s + "Kx"] = 0.0; c[s + "Ky"] = 0.0; c[s + "Ka"] = 0.0
        # leg FK
        for n in ("hf", "ha", "hr", "k", "ap", "t"):
            c[s + n] = 0.0
        # arm FK
        c[s + "af"] = 0.0; c[s + "aa"] = 8.0; c[s + "at"] = 0.0; c[s + "ae"] = 12.0
        c[s + "w1"] = 0.0; c[s + "w2"] = 0.0; c[s + "w3"] = 0.0
        # arm IK
        for n in ("Wx", "Wy", "Wz", "Hfx", "Hfy", "Hfz", "Hdx", "Hdy", "Hdz", "Ex", "Ey", "Ez"):
            c[s + n] = 0.0
        c[s + "Hfx"] = SIDE[s][1]; c[s + "Hdz"] = 1.0
        c[s + "Ex"] = 0.3 * SIDE[s][1]; c[s + "Ey"] = 0.6; c[s + "Ez"] = -1.0
        # fingers
        c[s + "c"] = 0.3; c[s + "cth"] = 0.2; c[s + "spr"] = 0.0
    return c


DEFAULT = default_channels()


def new_ch(**kw):
    c = dict(DEFAULT)
    for k, v in kw.items():
        if k not in c:
            raise KeyError(k)
        c[k] = v
    return c


# ============================================================================ solver
# Lateral share of the knee pole: the knee heads over the foot (a few degrees of toe-out), not outward.
KNEE_POLE_OUT = 0.03


def two_bone(H, A, L1, L2, pole):
    d = A - H
    dist = d.length
    dirv = d / max(dist, 1e-6)
    dist = clamp(dist, abs(L1 - L2) + 1e-3, (L1 + L2) * 0.9995)
    a = (L1 * L1 - L2 * L2 + dist * dist) / (2 * dist)
    h = math.sqrt(max(L1 * L1 - a * a, 0.0))
    n = orth(pole, dirv)
    Aeff = H + dirv * dist
    return H + dirv * a + n * h, Aeff, n


def hips_xform(c):
    D = eul(c["hip_rx"], c["hip_ry"], c["hip_rz"])
    P = HEAD["Hips"] + Vector((c["hip_tx"], c["hip_ty"], c["hip_tz"]))
    return D, P


FING_CURL = {"Proximal": 62.0, "Intermediate": 88.0, "Distal": 55.0}
FING_K = {"Index": 0.85, "Middle": 1.0, "Ring": 1.05, "Little": 1.12}
FING_RELAX = {"Index": 0.8, "Middle": 1.0, "Ring": 1.15, "Little": 1.3}


def solve(c, cfg):
    """channels -> (D, P): absolute world rotation delta and head position of every bone."""
    D = {}; P = {}

    def put(name, d):
        p = PAR[name]
        P[name] = P[p] + D[p] @ (HEAD[name] - HEAD[p])
        D[name] = d

    D["Hips"], P["Hips"] = hips_xform(c)
    put("Spine", D["Hips"] @ eul(c["sp_rx"], c["sp_ry"], c["sp_rz"]))
    put("Chest", D["Spine"] @ eul(c["ch_rx"], c["ch_ry"], c["ch_rz"]))
    k = c["stab"]
    Dt = eul(c["look_rx"], c["look_ry"], c["look_rz"])
    put("Neck", D["Chest"].slerp(Dt, k * 0.35) @ eul(c["nk_rx"], c["nk_ry"], c["nk_rz"]))
    put("Head", D["Neck"].slerp(Dt, k * 0.8) @ eul(c["hd_rx"], c["hd_ry"], c["hd_rz"]))

    space = cfg.get("space", "chest")

    def to_world(v, is_dir=False):
        if space == "world":
            return v.copy()
        base = "Chest" if space == "chest" else "Hips"
        return D[base] @ v if is_dir else P[base] + D[base] @ v

    # ---------------- arms
    for sd in "LR":
        nm, s = SIDE[sd]
        put(nm + "Shoulder", D["Chest"] @ qz(-s * c[sd + "shf"]) @ qy(-s * c[sd + "shu"]))
        UA, LA, HA = nm + "UpperArm", nm + "LowerArm", nm + "Hand"
        put(UA, D[nm + "Shoulder"])        # temporary, orientation replaced below
        L1, L2 = LEN[UA], LEN[LA]
        apex_rest = Vector((0, 1, 0))
        mode = cfg.get("arms", {}).get(sd, "fk")
        if mode == "fk":
            f = rad(c[sd + "af"]); ab = rad(c[sd + "aa"])
            Dr = D["Chest"] if cfg.get("arm_rel") else Quaternion()
            u = Dr @ Vector((s * math.sin(ab), -math.sin(f) * math.cos(ab), -math.cos(f) * math.cos(ab))).normalized()
            fold = Dr @ Vector((-s * 0.35, -1.0, 0.0)).normalized()
            w = orth(fold, u)
            el = rad(c[sd + "ae"])
            fv = (u * math.cos(el) + w * math.sin(el)).normalized()
            tw = Quaternion(u, rad(c[sd + "at"]))
            apexU = tw @ (-w)
            wf = orth(fold, fv)
            apexF = Quaternion(fv, rad(c[sd + "at"])) @ (-wf)
            D[UA] = frame_rot(REST_DIR[UA], apex_rest, u, apexU)
            put(LA, frame_rot(REST_DIR[LA], apex_rest, fv, apexF))
            put(HA, D[LA] @ qz(c[sd + "w3"]) @ qy(s * c[sd + "w2"]) @ qx(s * c[sd + "w1"]))
        else:
            S = P[UA]
            Wt = to_world(Vector((c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"])))
            pole = to_world(Vector((c[sd + "Ex"], c[sd + "Ey"], c[sd + "Ez"])), True)
            K, Wd, n = two_bone(S, Wt, L1, L2, pole)
            D[UA] = frame_rot(REST_DIR[UA], apex_rest, K - S, n)
            put(LA, frame_rot(REST_DIR[LA], apex_rest, Wd - K, n))
            fdir = to_world(Vector((c[sd + "Hfx"], c[sd + "Hfy"], c[sd + "Hfz"])), True)
            ddir = to_world(Vector((c[sd + "Hdx"], c[sd + "Hdy"], c[sd + "Hdz"])), True)
            put(HA, frame_rot(REST_DIR[HA], Vector((0, 0, 1)), fdir, ddir))
        # fingers
        cc = c[sd + "c"]; ct = c[sd + "cth"]; spr = c[sd + "spr"]
        for fi, fn in enumerate(FINGERS):
            for jn in JOINTS:
                bn = nm + fn + jn
                r = qy(s * cc * FING_CURL[jn] * FING_K[fn])
                if jn == "Proximal":
                    r = qz(s * spr * (fi - 1.5) * -4.0) @ r
                put(bn, D[PAR[bn]] @ r)
        for ji, jn in enumerate(JOINTS):
            bn = nm + "Thumb" + jn
            r = qy(s * ct * (30 + 25 * ji)) @ qz(s * ct * (28 if ji == 0 else 8))
            put(bn, D[PAR[bn]] @ r)

    # ---------------- legs
    for sd in "LR":
        nm, s = SIDE[sd]
        UL, LL, FT, TO = nm + "UpperLeg", nm + "LowerLeg", nm + "Foot", nm + "Toes"
        put(UL, D["Hips"])
        apex_rest = Vector((0, -1, 0))
        if cfg.get("legs", "ik") == "ik":
            H = P[UL]
            A = Vector((c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"]))
            pole = Vector((c[sd + "Px"], c[sd + "Py"], c[sd + "Pz"]))
            K, Ae, n = two_bone(H, A, LEN[UL], LEN[LL], pole)
            D[UL] = frame_rot(REST_DIR[UL], apex_rest, K - H, n)
            put(LL, frame_rot(REST_DIR[LL], apex_rest, Ae - K, n))
            put(FT, qz(s * c[sd + "Fy"]) @ qy(c[sd + "Fr"]) @ qx(-c[sd + "Fp"]))
            put(TO, D[FT] @ qx(-c[sd + "T"]))
        else:
            D[UL] = D["Hips"] @ qz(s * c[sd + "hr"]) @ qy(-s * c[sd + "ha"]) @ qx(-c[sd + "hf"])
            put(LL, D[UL] @ qx(c[sd + "k"]))
            put(FT, D[LL] @ qx(-c[sd + "ap"]))
            put(TO, D[FT] @ qx(-c[sd + "t"]))
    return D, P


_last_q = {}


def basis_from(D, c):
    """absolute deltas -> {bone: (quat, loc)} in pose-bone space."""
    out = {}
    for n in NAMES:
        p = PAR[n]
        r = D[n] if p is None else D[p].inverted() @ D[n]
        rq = RESTQ[n]
        q = rq.inverted() @ r @ rq
        out[n] = q
    t = Vector((c["hip_tx"], c["hip_ty"], c["hip_tz"]))
    loc = RESTQ["Hips"].inverted() @ t
    return out, loc


def key_pose(c, cfg, frame, cont):
    D, P = solve(c, cfg)
    qs, loc = basis_from(D, c)
    for n in NAMES:
        q = qs[n]
        prev = cont.get(n)
        if prev is not None and prev.dot(q) < 0:
            q = Quaternion((-q.w, -q.x, -q.y, -q.z))
        cont[n] = q
        pb = ARM.pose.bones[n]
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=frame)
    pb = ARM.pose.bones["Hips"]
    pb.location = loc
    pb.keyframe_insert("location", frame=frame)
    return D, P


def set_pose_only(c, cfg):
    D, P = solve(c, cfg)
    qs, loc = basis_from(D, c)
    for n in NAMES:
        ARM.pose.bones[n].rotation_quaternion = qs[n]
    ARM.pose.bones["Hips"].location = loc
    return D, P


# ============================================================================ foot cycle model
HEEL = (-0.06, -0.085)
BALL = (0.125, -0.085)
FWD2 = Vector((0.0, -1.0))


def rot2(pt, phi_deg):
    a = rad(phi_deg); cs, sn = math.cos(a), math.sin(a)
    return (pt[0] * cs - pt[1] * sn, pt[0] * sn + pt[1] * cs)


class FootCycle:
    """Ankle trajectory (body frame, metres) over one gait cycle for one foot.

    u in [0,1): stance is [0, beta]: roll through (pivot, phi0 -> phi1) phases, with the contact point
    planted on the ground, which slides at `g` (per cycle) in the body frame. Swing (beta,1) is a
    Hermite blend with matching velocity at both ends plus clearance."""

    def __init__(self, side, beta, g, center, phases, clear=0.10, peak=0.5, swing_pitch=0.0,
                 arc=0.0, cross=0.0, toe_swing=-6.0, yaw=0.0):
        self.s = SIDE[side][1]
        a_ = rad(yaw)
        self.fw = Vector((math.sin(a_), -math.cos(a_)))
        self.beta = beta; self.g = Vector(g); self.clear = clear; self.peak = peak
        self.swing_pitch = swing_pitch; self.arc = arc; self.cross = cross; self.toe_swing = toe_swing
        self.phases = phases        # list of (pivot 'heel'|'ball', phi0, phi1, u0, u1)
        self.off = Vector((0.0, 0.0))
        self.off = Vector(center) - self._stance_raw(beta * 0.5)[0]
        self.k = math.log(0.5) / math.log(peak)

    def _stance_raw(self, u):
        pos = Vector((0.0, 0.0))        # pivot world position at start of first phase
        for i, (piv, p0, p1, u0, u1) in enumerate(self.phases):
            prel = HEEL if piv == 'heel' else BALL
            if u <= u1 + 1e-12 or i == len(self.phases) - 1:
                uu = min(max(u, u0), u1) if u >= u0 else u0
                t = (uu - u0) / max(u1 - u0, 1e-9)
                phi = lerp(p0, p1, smooth(t))
                piv_w = pos + self.g * (uu - u0)
                rv = rot2((-prel[0], -prel[1]), phi)
                ank = piv_w + self.fw * rv[0]
                toe = max(0.0, -phi) if piv == 'ball' else 0.0
                return ank, rv[1], phi, toe
            # advance pivot to start of next phase (phi == 0 at switch): ankle -> new pivot
            t = 1.0
            piv_w = pos + self.g * (u1 - u0)
            rv = rot2((-prel[0], -prel[1]), p1)
            ank = piv_w + self.fw * rv[0]
            nxt = self.phases[i + 1][0]
            nrel = HEEL if nxt == 'heel' else BALL
            pos = ank + self.fw * nrel[0]
        raise RuntimeError

    def stance(self, u):
        a, z, phi, toe = self._stance_raw(u)
        return a + self.off, z, phi, toe

    def at(self, u):
        u = u % 1.0
        if u <= self.beta:
            a, z, phi, toe = self.stance(u)
            return Vector((a.x, a.y, z)), phi, toe
        w = (u - self.beta) / (1.0 - self.beta)
        dur = 1.0 - self.beta
        e = 1e-4
        a0, z0, ph0, t0 = self.stance(self.beta)
        a1, z1, ph1, t1 = self.stance(0.0)
        a0b, z0b, ph0b, _ = self.stance(self.beta - e)
        a1b, z1b, ph1b, _ = self.stance(e)
        d0 = (a0 - a0b) / e * dur; d1 = (a1b - a1) / e * dur
        dz0 = (z0 - z0b) / e * dur; dz1 = (z1b - z1) / e * dur
        x = herm(a0.x, a1.x, d0.x, d1.x, w)
        y = herm(a0.y, a1.y, d0.y, d1.y, w)
        z = herm(z0, z1, dz0, dz1, w)
        bump = math.sin(math.pi * (w ** self.k)) ** 2
        z += self.clear * bump
        x += self.s * self.arc * math.sin(math.pi * w) ** 2
        y += self.cross * math.sin(math.pi * w) ** 2
        # pitch: end of stance -> swing_pitch (mid) -> strike
        dp0 = (ph0 - ph0b) / e * dur; dp1 = (ph1b - ph1) / e * dur
        if w < 0.5:
            pitch = herm(ph0, self.swing_pitch, dp0, 0.0, w * 2)
        else:
            pitch = herm(self.swing_pitch, ph1, 0.0, dp1, (w - 0.5) * 2)
        toe = lerp(t0, 0.0, smooth(w / 0.35)) + self.toe_swing * math.sin(math.pi * clamp(w * 1.2, 0, 1)) ** 2 * (1 - smooth(w))
        return Vector((x, y, z)), pitch, toe


def make_phases(beta, strike, u1, u2, push, back=False, fore=False):
    """Stance roll phases. u1: heel phase end; u2: heel-off start (fractions of the cycle)."""
    if fore:
        return [('ball', -strike, -push, 0.0, beta)]
    if not back:
        return [('heel', strike, 0.0, 0.0, u1), ('heel', 0.0, 0.0, u1, u2), ('ball', 0.0, -push, u2, beta)]
    return [('ball', -strike, 0.0, 0.0, u1), ('ball', 0.0, 0.0, u1, u2), ('heel', 0.0, push, u2, beta)]


class Loco:
    """Parameter bag + channel generator for a walking/running style."""

    def __init__(self, **kw):
        d = dict(T=28, speed=1.6, dir=(0, -1), beta=0.62, betaL=None, betaR=None, offR=0.5,
                 strike=18.0, u1=0.08, u2=0.40, push=28.0, clear=0.10, peak=0.5, swing_pitch=4.0,
                 width=0.10, arc=0.015, cross=0.0, cy=0.03, toeout=6.0, back=False,
                 bobA=0.02, bob_peak=None, sway=0.025, yaw=5.0, yaw_sign=1.0, tilt=3.0, lean=3.0, cr=0.8,
                 reach=0.995, drop=0.0, stab=0.8, look=0.0, neck=0.0, head=0.0,
                 swing_f=18.0, swing_b=22.0, elbow0=24.0, elbow1=8.0, ab0=7.0, xab=4.0, armlag=0.04,
                 curl=0.3, thumb=0.2, wrist=6.0, post=None, toe_swing=-6.0, armmode="swing",
                 lagS=0.03, lagC=0.06, extra_yaw=0.0, fore=False,
                 clearL=None, clearR=None, swing_pitchL=None, swing_pitchR=None, toe_swingL=None, toe_swingR=None,
                 push_back_frac=None)
        d.update(kw)
        self.__dict__.update(d)
        self.dur = self.T / FPS
        gv = -Vector(self.dir) * self.speed * self.dur
        self.g = gv
        bl = self.betaL if self.betaL else self.beta
        br = self.betaR if self.betaR else self.beta
        self.feet = {}
        for sd, b in (('L', bl), ('R', br)):
            s = SIDE[sd][1]
            ph = make_phases(b, self.strike, self.u1 * b / 0.62 if self.u1 < 0.2 else self.u1, self.u2 * b / 0.62 if False else self.u2, self.push, self.back, self.fore)
            ov = lambda base, a, bb: (a if sd == 'L' else bb) if (a if sd == 'L' else bb) is not None else base
            self.feet[sd] = FootCycle(sd, b, gv, (s * self.width, self.cy), ph,
                                      clear=ov(self.clear, self.clearL, self.clearR), peak=self.peak,
                                      swing_pitch=ov(self.swing_pitch, self.swing_pitchL, self.swing_pitchR),
                                      arc=self.arc, cross=self.cross,
                                      toe_swing=ov(self.toe_swing, self.toe_swingL, self.toe_swingR),
                                      yaw=s * self.toeout)
        if self.bob_peak is None:
            self.bob_peak = self.beta / 2 if self.beta > 0.5 else self.beta / 2 + 0.25
        self.z0 = None
        self.prepare()

    # foot phase of each side at global phase p
    def fph(self, sd, p):
        return p if sd == 'L' else (p + self.offR) % 1.0

    def body(self, p):
        """Everything except hips height."""
        c = new_ch()
        t = TAU
        b = self.beta
        c["hip_tx"] = self.sway * math.cos(t * (p - b / 2))
        yaw = -self.yaw_sign * self.yaw * math.cos(t * p) + self.extra_yaw
        c["hip_rz"] = yaw
        tilt = self.tilt * math.cos(t * (p - 0.5 * (1 + b)))
        c["hip_ry"] = tilt
        L = self.lean
        c["hip_rx"] = 0.35 * L
        c["sp_rx"] = 0.30 * L
        c["ch_rx"] = 0.35 * L
        yS = -self.yaw_sign * self.yaw * math.cos(t * (p - self.lagS))
        yC = -self.yaw_sign * self.yaw * math.cos(t * (p - self.lagC))
        c["sp_rz"] = -(1 + self.cr) * 0.4 * yS
        c["ch_rz"] = -(1 + self.cr) * 0.6 * yC
        c["sp_ry"] = -0.5 * self.tilt * math.cos(t * (p - 0.5 * (1 + b) - self.lagS)) - 0.4 * self.sway / 0.025
        c["ch_ry"] = -0.5 * self.tilt * math.cos(t * (p - 0.5 * (1 + b) - self.lagC)) + 0.2 * self.sway / 0.025
        c["stab"] = self.stab
        c["look_rx"] = self.look
        c["nk_rx"] = self.neck; c["hd_rx"] = self.head
        # arms
        for sd in "LR":
            ph = self.fph(sd, p)
            a = ph - 0.5 - self.armlag
            m = (self.swing_f - self.swing_b) / 2; amp = (self.swing_f + self.swing_b) / 2
            cs = math.cos(t * a)
            c[sd + "af"] = m + amp * cs
            c[sd + "ae"] = self.elbow0 + self.elbow1 * math.cos(t * (a - 0.06))
            c[sd + "aa"] = self.ab0 - self.xab * cs
            c[sd + "w2"] = self.wrist * math.cos(t * (a - 0.12))
            c[sd + "c"] = self.curl; c[sd + "cth"] = self.thumb
            c[sd + "shu"] = 1.5 * math.cos(t * (a - 0.1))
            c[sd + "shf"] = -0.5 * (c["ch_rz"])  # shoulders follow chest counter-yaw a bit
        # legs
        for sd in "LR":
            s = SIDE[sd][1]
            A, pitch, toe = self.feet[sd].at(self.fph(sd, p))
            c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"] = A.x, A.y, A.z
            c[sd + "Fp"] = pitch; c[sd + "T"] = toe
            c[sd + "Fy"] = self.toeout
        return c

    def bob(self, p):
        return self.bobA * math.cos(TAU * 2 * (p - self.bob_peak))

    def prepare(self):
        """Solve the pelvis height offset so planted legs never overreach."""
        L = LEN["LeftUpperLeg"] + LEN["LeftLowerLeg"]
        zmax = 1e9
        n = 240
        for i in range(n):
            p = i / n
            c = self.body(p)
            c["hip_tz"] = self.bob(p)
            if self.post:
                self.post(self, c, p)
            Dh, Ph = hips_xform(c)
            for sd in "LR":
                u = self.fph(sd, p)
                b = self.feet[sd].beta
                if u > b:
                    continue
                nm = SIDE[sd][0]
                hj = Ph + Dh @ (HEAD[nm + "UpperLeg"] - HEAD["Hips"])
                A = Vector((c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"]))
                dh2 = (hj.x - A.x) ** 2 + (hj.y - A.y) ** 2
                cap = self.reach * L
                if dh2 >= cap * cap:
                    continue
                zhj = A.z + math.sqrt(cap * cap - dh2)
                zmax = min(zmax, zhj - hj.z)    # hips tz offset (relative to rest) such that reach == cap
        self.z0 = zmax - self.drop

    def channels(self, f):
        p = (f / self.T) % 1.0
        c = self.body(p)
        c["hip_tz"] = self.z0 + self.bob(p)
        if self.post:
            self.post(self, c, p)
        return c


def slide_max(lo, cfg=None):
    """Max planted-point slide (m/s) at integer frames, heel/ball points with z < 4 mm in two consecutive frames."""
    cfg = cfg or {"legs": "ik"}
    exp = Vector((lo.g.x, lo.g.y, 0.0)) / lo.T
    prev = None; worst = 0.0
    for fr in range(lo.T + 1):
        c = lo.channels(float(fr % lo.T))
        D, P = solve(c, cfg)
        cur = {}
        for sd in "LR":
            nm = SIDE[sd][0]; ft = nm + "Foot"; x = HEAD[ft].x
            for pn, y in (("h", 0.08), ("b", -0.105)):
                cur[sd + pn] = P[ft] + D[ft] @ (Vector((x, y, 0.0)) - HEAD[ft])
        if prev:
            for k in cur:
                if cur[k].z < 0.004 and prev[k].z < 0.004:
                    d = cur[k] - prev[k]
                    worst = max(worst, (Vector((d.x, d.y, 0)) - exp).length * FPS)
        prev = cur
    return worst


def tuned(**kw):
    """Loco with stance fraction nudged (within +-2%) so no frame sits on a marginal lift-off (foot slide <= 0.04 m/s)."""
    best = None
    b0 = kw.get("beta", 0.62)
    bl, br = kw.get("betaL"), kw.get("betaR")
    for off in (0.0, 0.004, -0.004, 0.008, -0.008, 0.012, -0.012, 0.016, -0.016, 0.02, -0.02):
        k = dict(kw); k["beta"] = b0 + off
        if bl: k["betaL"] = bl + off
        if br: k["betaR"] = br + off
        lo = Loco(**k)
        sl = slide_max(lo, {"legs": "ik"})
        if best is None or sl < best[0]:
            best = (sl, lo)
        if sl <= 0.04:
            return lo
    if bl and br:
        grid = [i * 0.006 for i in range(-5, 6)]
        for oa in grid:
            for ob in grid:
                k = dict(kw); k["betaL"] = bl + oa; k["betaR"] = br + ob
                lo = Loco(**k)
                sl = slide_max(lo, {"legs": "ik"})
                if sl < best[0]:
                    best = (sl, lo)
                if sl <= 0.04:
                    return lo
    return best[1]


def knee_stats(loco, cfg=None):
    cfg = {"legs": "ik"}
    mn = 1e9; mx = -1e9
    mids = {}
    for i in range(0, loco.T * 2):
        c = loco.channels(i / 2.0)
        D, P = solve(c, cfg)
        for sd in "LR":
            nm = SIDE[sd][0]
            a = (P[nm + "LowerLeg"] - P[nm + "UpperLeg"]).normalized()
            b = (P[nm + "Foot"] - P[nm + "LowerLeg"]).normalized()
            fl = math.degrees(math.acos(clamp(a.dot(b), -1, 1)))
            mn = min(mn, fl); mx = max(mx, fl)
    return mn, mx


# ============================================================================ clip registry
CLIPS = {}


def loco_clip(name, loco, **extra):
    CLIPS[name] = dict(name=name, frames=loco.T, loop=True, cfg={"legs": "ik"}, fn=loco.channels,
                       speed=loco.speed, loco=loco, gun=None, **extra)


def add_clip(name, frames, loop, fn, cfg=None, speed=None, **extra):
    CLIPS[name] = dict(name=name, frames=frames, loop=loop, cfg=cfg or {"legs": "ik"}, fn=fn, speed=speed)
    CLIPS[name].update(extra)


class Keys:
    """Keyframed pose sequence: key(t_sec, **channels); PCHIP per channel; unspecified = base."""

    def __init__(self, base=None, periodic=False):
        self.base = base or new_ch()
        self.keys = []
        self.periodic = periodic

    def key(self, t, pose=None, **kw):
        c = dict(pose if pose is not None else self.base)
        for k, v in kw.items():
            if k not in c:
                raise KeyError(k)
            c[k] = v
        self.keys.append((t, c))
        return c

    def eval(self, t):
        ts = [k[0] for k in self.keys]
        out = {}
        for name in self.keys[0][1]:
            out[name] = pchip_eval(ts, [k[1][name] for k in self.keys], t, self.periodic)
        return out

    def fn(self):
        return lambda f: self.eval(f / FPS)


# ============================================================================ proxy body + previews
PROXY = []
PROXY_STATIC = []
CAMS = {}


def _mk(name, verts, faces, color):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.color = (*color, 1.0)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def _frame_of(a, b):
    d = (b - a).normalized()
    t = Vector((1, 0, 0)) if abs(d.x) < 0.9 else Vector((0, 1, 0))
    u = d.cross(t).normalized(); v = d.cross(u).normalized()
    return d, u, v


def cyl_geo(a, b, r1, r2, seg=10, sx=1.0, sy=1.0):
    d, u, v = _frame_of(a, b)
    verts = []; faces = []
    for p, r in ((a, r1), (b, r2)):
        for i in range(seg):
            an = TAU * i / seg
            verts.append(p + u * (math.cos(an) * r * sx) + v * (math.sin(an) * r * sy))
    for i in range(seg):
        j = (i + 1) % seg
        faces.append((i, j, seg + j, seg + i))
    verts += [a, b]
    for i in range(seg):
        j = (i + 1) % seg
        faces.append((seg * 2, j, i)); faces.append((seg * 2 + 1, seg + i, seg + j))
    return verts, faces


def box_geo(lo, hi):
    x0, y0, z0 = lo; x1, y1, z1 = hi
    v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return v, f


def sphere_geo(c, rx, ry, rz, seg=12, rings=8):
    verts = []; faces = []
    for r in range(rings + 1):
        th = math.pi * r / rings
        for i in range(seg):
            ph = TAU * i / seg
            verts.append(c + Vector((rx * math.sin(th) * math.cos(ph), ry * math.sin(th) * math.sin(ph), rz * math.cos(th))))
    for r in range(rings):
        for i in range(seg):
            j = (i + 1) % seg
            faces.append((r * seg + i, r * seg + j, (r + 1) * seg + j, (r + 1) * seg + i))
    return verts, faces


def bone_parent(ob, bone):
    pb = ARM.pose.bones[bone]
    ob.parent = ARM
    ob.parent_type = 'BONE'
    ob.parent_bone = bone
    bpy.context.view_layer.update()
    m = ARM.matrix_world @ pb.matrix @ Matrix.Translation((0, pb.bone.length, 0))
    ob.matrix_parent_inverse = m.inverted()


COL_L = (0.3, 0.5, 0.95); COL_R = (0.95, 0.5, 0.25); COL_C = (0.75, 0.72, 0.65)
RADII = {"Hips": (0.10, 0.11), "Spine": (0.105, 0.115), "Chest": (0.125, 0.13), "Neck": (0.045, 0.04),
         "UpperArm": (0.05, 0.042), "LowerArm": (0.042, 0.032), "Hand": (0.03, 0.025),
         "UpperLeg": (0.082, 0.062), "LowerLeg": (0.058, 0.042)}


def build_proxy():
    for ob in PROXY:
        bpy.data.objects.remove(ob, do_unlink=True)
    PROXY.clear()
    for pb in ARM.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    for n in NAMES:
        base = n.replace("Left", "").replace("Right", "")
        col = COL_L if n.startswith("Left") else COL_R if n.startswith("Right") else COL_C
        if n == "Head":
            ob = _mk(n, *sphere_geo(Vector((0, 0.005, 1.695)), 0.085, 0.10, 0.108), (0.85, 0.65, 0.52))
            PROXY.append(ob); bone_parent(ob, n)
            nose = _mk(n + "_nose", *cyl_geo(Vector((0, -0.09, 1.69)), Vector((0, -0.14, 1.675)), 0.02, 0.004, 6), (0.95, 0.3, 0.3))
            PROXY.append(nose); bone_parent(nose, n)
            continue
        if "Thumb" in n or any(f in n for f in FINGERS):
            r = 0.0085 if "Distal" not in n else 0.007
            v, f = cyl_geo(HEAD[n], TAIL[n], r, r * 0.9, 6)
            ob = _mk(n, v, f, tuple(x * 0.8 for x in col))
        elif base == "Foot":
            x = HEAD[n].x
            v, f = box_geo((x - 0.045, -0.115, 0.0), (x + 0.045, 0.085, 0.07))
            ob = _mk(n, v, f, col)
        elif base == "Toes":
            x = HEAD[n].x
            v, f = box_geo((x - 0.045, -0.19, 0.0), (x + 0.045, -0.105, 0.04))
            ob = _mk(n, v, f, col)
        elif base == "Hand":
            v, f = cyl_geo(HEAD[n], TAIL[n], 0.035, 0.03, 8, 1.0, 0.45)
            ob = _mk(n, v, f, col)
        elif base == "Shoulder":
            continue
        else:
            r1, r2 = RADII.get(base, (0.03, 0.03))
            chestlike = base in ("Hips", "Spine", "Chest")
            v, f = cyl_geo(HEAD[n], TAIL[n], r1, r2, 12, 1.25 if chestlike else 1.0, 0.78 if chestlike else 1.0)
            ob = _mk(n, v, f, col)
        PROXY.append(ob)
        bone_parent(ob, n)


GUN_OBJS = []


def clear_guns():
    for ob in GUN_OBJS:
        bpy.data.objects.remove(ob, do_unlink=True)
    GUN_OBJS.clear()


def make_gun(spec):
    """spec: list of (lo, hi) boxes in WORLD coords at frame 0 of the clip; parented to RightHand."""
    clear_guns()
    bpy.context.scene.frame_set(0)
    for (o, a, u, rs, rw, rh) in spec:
        r = a.cross(u)
        v = []
        for i in (0, 1):
            for j in (0, 1):
                for k in (0, 1):
                    v.append(o + a * rs[i] + r * rw[j] + u * rh[k])
        _, f = box_geo((0, 0, 0), (1, 1, 1))
        # box_geo vertex order: (x0y0z0, x1y0z0, x1y1z0, x0y1z0, x0y0z1 ...) -> remap from i,j,k loop
        idx = {(0, 0, 0): 0, (1, 0, 0): 1, (1, 1, 0): 2, (0, 1, 0): 3, (0, 0, 1): 4, (1, 0, 1): 5, (1, 1, 1): 6, (0, 1, 1): 7}
        vv = [None] * 8
        n = 0
        for i in (0, 1):
            for j in (0, 1):
                for k in (0, 1):
                    vv[idx[(i, j, k)]] = v[n]; n += 1
        ob = _mk("gun", vv, f, (0.1, 0.1, 0.12))
        GUN_OBJS.append(ob)
        bone_parent(ob, "RightHand")


def setup_render():
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'OBJECT'; sh.show_cavity = False
    sh.show_shadows = True; sh.shadow_intensity = 0.3
    sc.render.resolution_x = 300; sc.render.resolution_y = 380
    w = bpy.data.worlds.new("w"); w.color = (0.55, 0.58, 0.62); sc.world = w
    sc.render.image_settings.file_format = 'PNG'
    sc.render.use_stamp = True
    for a in ("use_stamp_date", "use_stamp_time", "use_stamp_render_time", "use_stamp_camera", "use_stamp_scene",
              "use_stamp_filename", "use_stamp_lens", "use_stamp_marker", "use_stamp_memory", "use_stamp_hostname",
              "use_stamp_sequencer_strip", "use_stamp_note", "use_stamp_frame_range"):
        if hasattr(sc.render, a):
            setattr(sc.render, a, False)
    sc.render.use_stamp_frame = True
    sc.render.stamp_font_size = 22
    for i in range(-10, 11):
        v, f = box_geo((-1.5, i * 0.25 - 0.006, -0.004), (1.5, i * 0.25 + 0.006, 0.0))
        PROXY_STATIC.append(_mk("stripe", v, f, (0.25, 0.25, 0.28) if i % 4 else (0.9, 0.3, 0.3)))
    v, f = box_geo((-1.5, -2.6, -0.02), (1.5, 2.6, -0.004))
    PROXY_STATIC.append(_mk("floor", v, f, (0.42, 0.45, 0.42)))
    for nm, loc, rot in (("side", (-6, 0, 0.95), (math.pi / 2, 0, -math.pi / 2)),
                         ("front", (0, -6, 0.95), (math.pi / 2, 0, 0))):
        cam = bpy.data.cameras.new(nm); cam.type = 'ORTHO'; cam.ortho_scale = 2.3
        co = bpy.data.objects.new(nm, cam); co.location = loc; co.rotation_euler = rot
        bpy.context.scene.collection.objects.link(co)
        CAMS[nm] = co


def render_sheet(name, frames, scale=2.3, cam_z=0.95):
    import numpy as np
    sc = bpy.context.scene
    tmp = os.path.join(PREV, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    rows = []
    for cn in ("side", "front"):
        CAMS[cn].data.ortho_scale = scale
        CAMS[cn].location.z = cam_z
        sc.camera = CAMS[cn]
        tiles = []
        for f in frames:
            sc.frame_set(int(round(f)))
            sc.render.filepath = os.path.join(tmp, "t.png")
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(sc.render.filepath)
            arr = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
            tiles.append(arr)
            bpy.data.images.remove(img)
        rows.append(np.concatenate(tiles, axis=1))
    sheet = np.concatenate(rows[::-1], axis=0)   # image rows are bottom-up: put side on top
    h, w = sheet.shape[:2]
    out = bpy.data.images.new("sheet", w, h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = os.path.join(PREV, "Anim_%s.png" % name)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


# ============================================================================ bake / export / verify
def author(cl):
    name = cl["name"]
    N = cl["frames"]
    sc = bpy.context.scene
    if ARM.animation_data:
        ARM.animation_data_clear()
    for pb in ARM.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    ARM.animation_data_create()
    act = bpy.data.actions.new("Anim_" + name)
    ARM.animation_data.action = act
    fn = cl["fn"]
    cont = {}
    pad = 3 if cl["loop"] else 0
    for fr in range(-pad, N + 1 + pad):
        if cl["loop"]:
            c = fn(float(fr) if fr in (N,) else float(fr % N))
        else:
            c = fn(float(fr))
        key_pose(c, cl["cfg"], fr, cont)
    sc.frame_start = 0; sc.frame_end = N
    sc.frame_set(0)
    return act


def export_fbx(name):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    ARM.select_set(True)
    bpy.context.view_layer.objects.active = ARM
    bpy.context.scene.name = "Anim_" + name
    path = os.path.join(OUT, "Anim_%s.fbx" % name)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_unit_scale=True,
                             object_types={'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
                             bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                             bake_anim_simplify_factor=0.0)
    return path


def pose_snapshot(frame):
    bpy.context.scene.frame_set(frame)
    return {n: ARM.pose.bones[n].matrix.copy() for n in NAMES}


def verify_loop(cl):
    N = cl["frames"]
    a = pose_snapshot(0); b = pose_snapshot(N)
    posd = max((a[n].translation - b[n].translation).length for n in NAMES)
    rotd = 0.0
    for n in NAMES:
        qa = a[n].to_quaternion(); qb = b[n].to_quaternion()
        rotd = max(rotd, math.degrees(2 * math.acos(clamp(abs(qa.dot(qb)), 0, 1))))
    a1 = pose_snapshot(1); bm = pose_snapshot(N - 1)
    sd0 = max((a1[n].translation - 2 * a[n].translation + bm[n].translation).length for n in NAMES)
    sdm = 0.0
    prev = None
    snaps = [pose_snapshot(i) for i in range(0, N + 1)]
    for i in range(1, N):
        sdm = max(sdm, max((snaps[i + 1][n].translation - 2 * snaps[i][n].translation + snaps[i - 1][n].translation).length for n in NAMES))
    return round(posd, 7), round(rotd, 3), round(sd0, 5), round(sdm, 5)


def measure_slide(cl):
    """Planted sole points (heel/ball) vs expected ground speed: (max, mean slide m/s, samples)."""
    lo = cl.get("loco")
    if not lo:
        return None
    sc = bpy.context.scene
    N = cl["frames"]
    errs = []
    prev = None
    for fr in range(0, N + 1):
        sc.frame_set(fr)
        cur = {}
        for sd in "LR":
            nm = SIDE[sd][0]
            m = ARM.pose.bones[nm + "Foot"].matrix
            inv = ARM.data.bones[nm + "Foot"].matrix_local.inverted()
            x = HEAD[nm + "Foot"].x
            for pn, y in (("heel", 0.08), ("ball", -0.105)):
                cur[sd + pn] = m @ (inv @ Vector((x, y, 0.0)))
        if prev:
            for k in cur:
                if cur[k].z < 0.004 and prev[k].z < 0.004:
                    d = cur[k] - prev[k]
                    exp = Vector((lo.g.x, lo.g.y, 0.0)) / lo.T
                    errs.append((Vector((d.x, d.y, 0)) - exp).length * FPS)
        prev = cur
    if not errs:
        return (0, 0, 0, 0)
    es = sorted(errs)
    return (round(es[-1], 3), round(es[int(len(es) * 0.9)], 3), round(sum(es) / len(es), 3), len(es))


def feet_min_dist(cl):
    sc = bpy.context.scene
    md = 9
    for fr in range(cl["frames"]):
        sc.frame_set(fr)
        a = ARM.pose.bones["LeftFoot"].head; b = ARM.pose.bones["RightFoot"].head
        md = min(md, math.hypot(a.x - b.x, a.y - b.y))
    return round(md, 3)


def check_fk(cl):
    fr = 3 if cl["frames"] > 3 else 0
    c = cl["fn"](float(fr))
    D, P = solve(c, cl["cfg"])
    bpy.context.scene.frame_set(fr)
    return round(max((ARM.pose.bones[n].head - P[n]).length for n in NAMES), 6)


def loco_metrics(cl):
    """Cadence, step, stance %, knee/hip ranges, bob, pelvis, from the solved pose (no bake needed)."""
    lo = cl["loco"]; cfg = cl["cfg"]; N = lo.T
    sub = 8
    S = N * sub
    dirv = Vector((lo.dir[0], lo.dir[1]))
    samples = []
    for i in range(S):
        f = i / sub
        c = lo.channels(f)
        D, P = solve(c, cfg)
        rec = {"c": c, "hipz": P["Hips"].z, "f": f}
        for sd in "LR":
            nm = SIDE[sd][0]
            ft = nm + "Foot"; x = HEAD[ft].x
            pts = [P[ft] + D[ft] @ (Vector((x, y, 0.0)) - HEAD[ft]) for y in (0.08, -0.105)]
            zmin = min(p.z for p in pts)
            lowp = min(pts, key=lambda p: p.z)
            hip = P[nm + "UpperLeg"]; kn = P[nm + "LowerLeg"]; an = P[ft]
            a = (kn - hip).normalized(); b = (an - kn).normalized()
            knee = math.degrees(math.acos(clamp(a.dot(b), -1, 1)))
            hipfl = math.degrees(math.atan2(hip.y - kn.y, hip.z - kn.z))
            rec[sd] = dict(contact=zmin < 0.003, knee=knee, hip=hipfl, pt=lowp, ank=an)
        samples.append(rec)
    out = {}
    out["cadence_spm"] = round(3600.0 / N, 1)
    st = {}
    for sd in "LR":
        st[sd] = sum(1 for r in samples if r[sd]["contact"]) / S * 100
    out["stance_pct"] = (round(st["L"], 1), round(st["R"], 1))
    # touchdown positions in the ground frame
    td = {}
    for sd in "LR":
        for i in range(S):
            if samples[i][sd]["contact"] and not samples[i - 1][sd]["contact"]:
                t = samples[i]["f"] / FPS
                p = samples[i][sd]["pt"]
                w = Vector((p.x, p.y)) + dirv * lo.speed * t
                td[sd] = (t, w)
                break
    if len(td) == 2:
        # step: touchdown of R after L, projected on travel dir; compensate the other foot's own movement
        (tl, wl), (tr, wr) = td["L"], td["R"]
        d = (wr - wl).dot(dirv)
        out["step_m"] = round(abs(d) if abs(d) < lo.speed * N / FPS * 0.9 else lo.speed * N / FPS - abs(d), 3)
    else:
        out["step_m"] = None
    out["step_nominal_m"] = round(lo.speed * N / FPS / 2.0, 3)
    sw = [r for r in samples]
    kn_swing = max(r[sd]["knee"] for r in samples for sd in "LR" if not r[sd]["contact"])
    kn_stance = [r[sd]["knee"] for r in samples for sd in "LR" if r[sd]["contact"]]
    out["knee_swing_peak"] = round(kn_swing, 1)
    out["knee_stance_peak"] = round(max(kn_stance), 1) if kn_stance else None
    mids = []; tds = []
    for sd in "LR":
        idx = [i for i in range(S) if samples[i][sd]["contact"]]
        if idx:
            # contiguous stance run containing the first touchdown
            st0 = next(i for i in range(S) if samples[i][sd]["contact"] and not samples[i - 1][sd]["contact"])
            run_ = [st0]
            j = (st0 + 1) % S
            while samples[j][sd]["contact"] and len(run_) < S:
                run_.append(j); j = (j + 1) % S
            tds.append(samples[st0][sd]["knee"])
            mids.append(samples[run_[len(run_) // 2]][sd]["knee"])
    out["knee_touchdown"] = round(sum(tds) / len(tds), 1) if tds else None
    out["knee_midstance"] = round(sum(mids) / len(mids), 1) if mids else None
    out["knee_min"] = round(min(r[sd]["knee"] for r in samples for sd in "LR"), 1)
    out["hip_flex_range"] = (round(min(r[sd]["hip"] for r in samples for sd in "LR"), 1),
                             round(max(r[sd]["hip"] for r in samples for sd in "LR"), 1))
    hz = [r["hipz"] for r in samples]
    out["bob_pp_cm"] = round((max(hz) - min(hz)) * 100, 1)
    rz = [r["c"]["hip_rz"] for r in samples]; ry = [r["c"]["hip_ry"] for r in samples]
    out["pelvis_rot_pp_deg"] = round(max(rz) - min(rz), 1)
    out["pelvis_obl_pp_deg"] = round(max(ry) - min(ry), 1)
    flight = sum(1 for r in samples if not r["L"]["contact"] and not r["R"]["contact"]) / S * 100
    out["flight_pct"] = round(flight, 1)
    out["double_support_pct"] = round(sum(1 for r in samples if r["L"]["contact"] and r["R"]["contact"]) / S * 100, 1)
    return out


def run(names, preview=True, export=True):
    setup_scene()
    if preview:
        build_proxy(); setup_render()
        if any(CLIPS[n].get("sheetfn") for n in names):
            setup_gu_cams()
    res = {}
    for nm in names:
        cl = CLIPS[nm]
        author(cl)
        info = dict(frames=cl["frames"], loop=cl["loop"], fkerr=check_fk(cl))
        if cl["loop"]:
            info["loop_check"] = verify_loop(cl)
        sl = measure_slide(cl)
        if sl:
            info["slide"] = sl; info["feet_dist"] = feet_min_dist(cl)
            info["metrics"] = loco_metrics(cl)
        if cl.get("gunframe"):
            report_hands(cl)
            miss = 0.0
            for fr_ in range(cl["frames"] + 1):
                c_ = cl["fn"](float(fr_ % cl["frames"]) if cl["loop"] else float(fr_))
                D_, P_ = solve(c_, cl["cfg"])
                for sd_ in "LR":
                    nm_ = SIDE[sd_][0] + "Hand"
                    miss = max(miss, (P_[nm_] - Vector((c_[sd_ + "Wx"], c_[sd_ + "Wy"], c_[sd_ + "Wz"]))).length)
            info["wrist_ik_miss_max_m"] = round(miss, 4)
        if export:
            info["fbx"] = export_fbx(nm)
        if preview:
            if cl.get("gun"):
                make_gun(cl["gun"])
            else:
                clear_guns()
            n = cl["frames"]
            fr = [n * i / 8.0 for i in range(8)] if cl["loop"] else [n * i / 7.0 for i in range(8)]
            if cl.get("sheetfn"):
                ns = int(os.environ.get("GU_N", "8"))
                cl["sheetfn"](nm, [n * i / float(ns - 1) for i in range(ns)])
            else:
                render_sheet(nm, fr, scale=cl.get("scale", 2.3), cam_z=cl.get("camz", 0.95))
        if cl.get("verify"):
            info["verify"] = cl["verify"](cl)
        res[nm] = info
        print("CLIP", nm, info)
    return res


# ============================================================================ CLIPS
V3 = Vector


def unit(*a):
    return V3(a).normalized()


def ball_ank(x, yball, phi):
    rv = rot2((-BALL[0], -BALL[1]), phi)
    return (x, yball - rv[0], rv[1])


def set_leg(c, sd, ank, pitch=0.0, toe=0.0, yaw=None):
    s = SIDE[sd][1]
    c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"] = ank
    c[sd + "Fp"] = pitch; c[sd + "T"] = toe
    c[sd + "Fy"] = 6.0 * 1 if yaw is None else yaw


def stand_feet(c, xl=0.09, yl=0.03, xr=0.09, yr=0.03, yawl=6.0, yawr=6.0):
    set_leg(c, 'L', (xl, yl, 0.085), yaw=yawl)
    set_leg(c, 'R', (-xr, yr, 0.085), yaw=yawr)


def lerp_ch(a, b, t):
    return {k: lerp(a[k], b[k], t) for k in a}


# ---------------------------------------------------------------- locomotion
JOG = tuned(T=20, speed=3.6, beta=0.34, strike=8, u1=0.04, u2=0.17, push=34, clear=0.14, peak=0.5, swing_pitch=-8,
           width=0.06, arc=0.0, cy=0.05, toeout=3, bobA=0.034, sway=0.02, yaw=9, tilt=2, lean=9, cr=0.9,
           swing_f=24, swing_b=26, elbow0=88, elbow1=12, ab0=9, xab=7, curl=0.7, thumb=0.5, wrist=0,
           reach=0.985, stab=0.75, look=4, toe_swing=-12, armlag=0.02)
WALKB = tuned(T=30, speed=1.4, dir=(0, 1), back=True, beta=0.64, strike=14, u1=0.08, u2=0.42, push=14, clear=0.08,
             peak=0.5, swing_pitch=-4, width=0.07, arc=0.01, cy=0.02, bobA=0.03, sway=0.03, yaw=4, yaw_sign=-1, tilt=3,
             lean=-1, swing_f=12, swing_b=10, elbow0=20, elbow1=5, reach=0.995, stab=0.8, look=-1, toe_swing=-10)


def strafe(dirx):
    return tuned(T=18, speed=1.6, dir=(dirx, 0), beta=0.58, strike=0, u1=0.05, u2=0.42, push=14, clear=0.07, peak=0.5,
                swing_pitch=0, width=0.08, arc=0.0, cross=0.16, cy=0.04, toeout=4, bobA=0.022, sway=0.025, yaw=2.5,
                tilt=3, lean=3, swing_f=8, swing_b=8, elbow0=30, elbow1=4, ab0=9, xab=2, reach=0.99, stab=0.8,
                look=1, toe_swing=-4)


loco_clip("Walk", tuned(T=27, speed=1.6, beta=0.62, strike=16, u1=0.08, u2=0.40, push=26, clear=0.10, peak=0.45,
                       swing_pitch=3, width=0.07, bobA=0.025, sway=0.03, yaw=4, tilt=4, lean=3, swing_f=11,
                       swing_b=14, elbow0=24, elbow1=9, reach=0.998, stab=0.8, look=2))
loco_clip("Jog", JOG)
loco_clip("WalkBack", WALKB)
loco_clip("StrafeLeft", strafe(1))
loco_clip("StrafeRight", strafe(-1))


# ---------------------------------------------------------------- contract v2 locomotion
RUN = tuned(T=19, speed=5.0, beta=0.32, strike=8, u1=0.04, u2=0.16, push=40, clear=0.11, peak=0.5, swing_pitch=-8,
           width=0.055, arc=0.0, cy=0.08, toeout=3, bobA=0.018, sway=0.02, yaw=8, tilt=2, lean=11, cr=0.9,
           swing_f=26, swing_b=28, elbow0=88, elbow1=12, ab0=9, xab=7, curl=0.7, thumb=0.5, wrist=0,
           reach=1.0, stab=0.8, look=5, toe_swing=-12, armlag=0.02)
SPRINT = tuned(T=14, speed=8.5, beta=0.22, strike=12, push=45, clear=0.28, peak=0.55, swing_pitch=-18, fore=True,
              width=0.04, arc=0.0, cy=0.20, toeout=2, bobA=0.04, sway=0.015, yaw=12, tilt=1.5, lean=16, cr=1.0,
              swing_f=38, swing_b=42, elbow0=90, elbow1=10, ab0=6, xab=6, curl=0.85, thumb=0.6, wrist=0,
              reach=1.0, stab=0.8, look=10, toe_swing=-20, armlag=0.02, neck=-4)
loco_clip("Run", RUN)
loco_clip("Sprint", SPRINT)

AIM = dict(speed=2.0, beta=0.58, strike=14, u1=0.08, u2=0.42, push=30, clear=0.065, peak=0.5, swing_pitch=-2,
           width=0.065, arc=0.01, cy=0.08, toeout=5, bobA=0.012, bob_peak=0.32, sway=0.02, yaw=3, tilt=2.5, lean=4,
           cr=0.8, swing_f=6, swing_b=6, elbow0=55, elbow1=4, ab0=9, xab=2, curl=0.7, thumb=0.5, wrist=0, reach=0.995,
           drop=0.0, stab=0.95, look=0, toe_swing=-6)


def aim_loco(T=22, **kw):
    d = dict(AIM); d["T"] = T; d.update(kw)
    return tuned(**d)


def diag(dx, dy, twist):
    back = dy > 0
    lo = aim_loco(T=22, dir=(dx, dy), extra_yaw=twist, cross=0.10, width=0.08, yaw_sign=(-1.0 if back else 1.0),
                  lean=(2 if back else 4), back=back, strike=(12 if back else 14), push=(28 if back else 30),
                  cy=(-0.02 if back else 0.08), swing_pitch=(-4 if back else -2), post=lambda S, c, p, tw=twist: c.update(
                      sp_rz=c["sp_rz"] - 0.35 * tw, ch_rz=c["ch_rz"] - 0.5 * tw))
    return lo


_h = 0.7071
loco_clip("AimWalk", aim_loco(22, dir=(0, -1)))
loco_clip("AimWalkBack", aim_loco(22, dir=(0, 1), back=True, yaw_sign=-1.0, lean=2, strike=12, push=28, cy=-0.02, swing_pitch=-4,
                                   u2=0.42, toe_swing=-10, look=-1))
for _nm, _dir in (("AimStrafeLeft", 1), ("AimStrafeRight", -1)):
    loco_clip(_nm, aim_loco(22, dir=(_dir, 0), beta=0.54, cy=0.04, strike=0, u1=0.05, u2=0.42, push=12, cross=0.24, swing_pitch=0,
                            yaw=2.5, lean=3, width=0.065, toe_swing=-4))
loco_clip("AimWalkFL", diag(_h, -_h, 18))
loco_clip("AimWalkFR", diag(-_h, -_h, -18))
loco_clip("AimWalkBL", diag(_h, _h, -18))
loco_clip("AimWalkBR", diag(-_h, _h, 18))


# ---------------------------------------------------------------- idle
def idle_pose(f, N=120):
    p = f / N
    c = new_ch()
    sh = math.sin(TAU * p)
    br = math.sin(TAU * 2 * p - 0.6)
    c["hip_tx"] = 0.032 * sh
    c["hip_tz"] = -0.012 - 0.007 * math.cos(TAU * p) - 0.004 * abs(sh)
    c["hip_ry"] = -2.6 * sh
    c["hip_rz"] = 1.2 * math.sin(TAU * p + 0.8)
    c["sp_ry"] = 1.9 * sh; c["ch_ry"] = 1.5 * sh
    c["sp_rz"] = -0.5 * math.sin(TAU * p + 0.8 - 0.3)
    c["ch_rx"] = 1.1 * br + 1.0; c["sp_rx"] = 0.5 * br + 0.5
    c["hd_ry"] = 1.5 * math.sin(TAU * p + 0.4)
    c["nk_rz"] = 2.0 * math.sin(TAU * p + 1.4)
    c["hd_rx"] = 0.6 * math.sin(TAU * 2 * p + 1.0)
    c["stab"] = 0.6
    for sd in "LR":
        s = SIDE[sd][1]
        c[sd + "shu"] = 1.3 * br
        c[sd + "af"] = 2.0 + 1.5 * math.sin(TAU * p + 0.9 - 0.4 * s)
        c[sd + "aa"] = 8.0 + 0.8 * br
        c[sd + "ae"] = 13 + 1.2 * math.sin(TAU * p + 1.4)
        c[sd + "w2"] = 3 * math.sin(TAU * p + 2.0)
        c[sd + "c"] = 0.30 + 0.03 * math.sin(TAU * p + 1.0 + s)
    stand_feet(c, 0.09, 0.03, 0.09, 0.03)
    return c


add_clip("Idle", 120, True, idle_pose)
CLIPS["Idle"]["cfg"] = {"legs": "ik"}


# ---------------------------------------------------------------- jump / fall / land
def jump_keys():
    """Push-off only (no pre-crouch): starts already in the drive, ~0.17 s."""
    b = idle_pose(0)
    k = Keys(b)
    dr = dict(b)
    dr.update(hip_tz=-0.09, hip_ty=0.045, hip_rx=10, sp_rx=7, ch_rx=5, hd_rx=-8, stab=0.7,
              Laf=30, Raf=30, Laa=14, Raa=14, Lae=20, Rae=20, Lc=0.35, Rc=0.35)
    stand_feet(dr, 0.115, 0.04, 0.115, 0.04, 8, 8)
    k.keys.append((0.0, dr))
    ex = dict(b)
    ex.update(hip_tz=0.0, hip_ty=-0.01, hip_rx=4, sp_rx=-2, ch_rx=-4, hd_rx=-4, stab=0.7,
              Laf=70, Raf=70, Laa=18, Raa=18, Lae=18, Rae=18)
    for sd in "LR":
        s_ = SIDE[sd][1]
        a = ball_ank(0.09 * s_, -0.10, -42)
        ex[sd + "Ax"], ex[sd + "Ay"], ex[sd + "Az"] = a
        ex[sd + "Fp"] = -42; ex[sd + "T"] = 36
    k.keys.append((0.07, ex))
    k.keys.append((5 / 30.0, fall_pose(0)))
    return k


def fall_pose(f, N=30):
    p = f / N
    c = new_ch()
    fl = math.sin(TAU * p); fl2 = math.sin(TAU * p + 1.3)
    c["hip_tz"] = 0.02 + 0.006 * fl
    c["hip_rx"] = 5 + 1.0 * fl2; c["sp_rx"] = 3; c["ch_rx"] = 2
    c["hip_ry"] = 1.5 * fl2; c["hip_rz"] = 2 * fl
    c["stab"] = 0.6; c["hd_rx"] = -4
    for sd, (x, y, z), ph in (('L', (0.13, -0.17, 0.27), 0.0), ('R', (-0.12, 0.09, 0.19), 1.9)):
        c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"] = x, y + 0.025 * math.sin(TAU * p + ph), z + 0.015 * math.cos(TAU * p + ph)
        c[sd + "Fp"] = -26 + 4 * math.sin(TAU * p + ph); c[sd + "T"] = 12
    for sd in "LR":
        s = SIDE[sd][1]
        c[sd + "af"] = 38 + 6 * math.sin(TAU * p + 0.7 * s)
        c[sd + "aa"] = 58 + 7 * math.sin(TAU * p + 2.0 + s)
        c[sd + "ae"] = 34 + 6 * math.sin(TAU * p + 1.0 - s)
        c[sd + "w2"] = -8 + 6 * math.sin(TAU * p + 1.5)
        c[sd + "c"] = 0.15 + 0.1 * math.sin(TAU * p); c[sd + "spr"] = 0.8
        c[sd + "shu"] = 5
    return c


def land_keys():
    k = Keys(idle_pose(0))
    b = idle_pose(0)
    k.keys.append((0.0, fall_pose(18)))
    # first toes touch (feet below hips, still arms out)
    t1 = dict(fall_pose(24))
    for sd in "LR":
        s = SIDE[sd][1]
        a = ball_ank(0.12 * s, -0.08 + (-0.03 if sd == 'L' else 0.03), -20)
        t1[sd + "Ax"], t1[sd + "Ay"], t1[sd + "Az"] = a
        t1[sd + "Fp"] = -20; t1[sd + "T"] = 14
    t1.update(hip_tz=0.0, hip_rx=6)
    k.keys.append((0.07, t1))
    # deep absorb: heels down, hips back and down, torso folds, arms forward
    ab = dict(b)
    ab.update(hip_tz=-0.22, hip_ty=0.10, hip_rx=18, sp_rx=12, ch_rx=9, hd_rx=-14, stab=0.7,
              Laf=42, Raf=42, Laa=30, Raa=30, Lae=34, Rae=34, Lc=0.2, Rc=0.2)
    stand_feet(ab, 0.15, 0.04, 0.15, 0.04, 12, 12)
    k.keys.append((0.15, ab))
    mid = dict(b)
    mid.update(hip_tz=-0.12, hip_ty=0.05, hip_rx=10, sp_rx=7, ch_rx=5, hd_rx=-6, Laf=18, Raf=18, Laa=18, Raa=18, Lae=24, Rae=24)
    stand_feet(mid, 0.125, 0.035, 0.125, 0.035, 9, 9)
    k.keys.append((0.28, mid))
    end = idle_pose(0)
    k.keys.append((0.40, end))
    k.keys.append((0.50, end))
    return k


_jk = jump_keys()
add_clip("JumpStart", 5, False, _jk.fn())
add_clip("Fall", 30, True, fall_pose)
_lk = land_keys()
add_clip("Land", 15, False, _lk.fn())

# ---------------------------------------------------------------- weapon handling
def put_hand(c, sd, palm, f, d, curl=0.85, thumb=0.7, pole=None):
    s = SIDE[sd][1]
    f = f.normalized(); d = d.normalized()
    w = palm - f * 0.05
    c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"] = w
    c[sd + "Hfx"], c[sd + "Hfy"], c[sd + "Hfz"] = f
    c[sd + "Hdx"], c[sd + "Hdy"], c[sd + "Hdz"] = d
    pl = pole if pole is not None else V3((s * 0.5, 0.35, -1.0))
    c[sd + "Ex"], c[sd + "Ey"], c[sd + "Ez"] = pl
    c[sd + "c"] = curl; c[sd + "cth"] = thumb


def pistol_hands(c, O, a, u, left=True):
    r = a.cross(u)
    put_hand(c, 'R', O + r * 0.034, a * 0.9 - u * 0.15, r * 0.85 + u * 0.5, 0.88, 0.7)
    if left:
        put_hand(c, 'L', O - r * 0.032 - u * 0.012, a * 0.9 - u * 0.2, -r * 0.85 + u * 0.5, 0.88, 0.7)


def rifle_right(c, B, a, u):
    r = a.cross(u)
    put_hand(c, 'R', B + a * 0.265 - u * 0.085, a * 0.9 - u * 0.45, r * 0.9 + u * 0.35, 0.9, 0.75)


def rifle_left(c, B, a, u, s=0.62):
    r = a.cross(u)
    put_hand(c, 'L', B + a * s - u * 0.045, a * 0.5 + r * 0.7, -r * 0.5 - u * 0.8, 0.85, 0.7)


def pistol_spec(O, a, u):
    return [(O, a, u, (-0.045, 0.02), (-0.016, 0.016), (-0.075, 0.03)),
            (O, a, u, (-0.06, 0.17), (-0.016, 0.016), (0.03, 0.07)),
            (O, a, u, (0.17, 0.20), (-0.008, 0.008), (0.035, 0.055))]


def rifle_spec(B, a, u):
    return [(B, a, u, (0.0, 0.20), (-0.025, 0.025), (-0.07, 0.04)),
            (B, a, u, (0.20, 0.46), (-0.03, 0.03), (-0.045, 0.055)),
            (B, a, u, (0.30, 0.37), (-0.022, 0.022), (-0.17, -0.045)),
            (B, a, u, (0.24, 0.29), (-0.015, 0.015), (-0.14, -0.045)),
            (B, a, u, (0.46, 0.74), (-0.032, 0.032), (-0.04, 0.04)),
            (B, a, u, (0.74, 1.0), (-0.01, 0.01), (-0.01, 0.01)),
            (B, a, u, (0.26, 0.31), (-0.008, 0.008), (0.055, 0.085))]


def torso(c, tz=0.0, ty=0.0, lean=0.0, yaw=0.0, head=0.0, roll=0.0):
    c["hip_tz"] = tz; c["hip_ty"] = ty
    c["hip_rx"] = 0.35 * lean; c["sp_rx"] = 0.3 * lean; c["ch_rx"] = 0.35 * lean
    c["hip_rz"] = 0.34 * yaw; c["sp_rz"] = 0.28 * yaw; c["ch_rz"] = 0.38 * yaw
    c["hd_rx"] = head; c["stab"] = 0.9; c["hd_ry"] = roll


PISTOL0 = (V3((0, -0.47, 1.60)), unit(0, -1, 0), unit(0, 0, 1))
RIFLE0 = (V3((-0.10, 0.02, 1.51)), unit(0.03, -1, 0), unit(0, 0, 1))
_lowa = unit(0.0, -0.78, -0.62)
RIFLELOW0 = (V3((-0.10, -0.02, 1.38)), _lowa, orth(V3((0, 0, 1)), _lowa))
RIFLELOW0 = (RIFLELOW0[0], RIFLELOW0[1], RIFLELOW0[2])


def aim_pistol(f, N=90):
    p = f / N
    br = math.sin(TAU * p)
    c = new_ch()
    torso(c, tz=-0.025 + 0.002 * br, ty=0.03, lean=7 + 0.4 * br, yaw=0, head=-6)
    c["hd_ry"] = 0
    O, a, u = PISTOL0
    O = O + V3((0.002 * math.sin(TAU * 2 * p), 0.0, 0.003 * math.sin(TAU * p + 1.0)))
    pistol_hands(c, O, a, u)
    set_leg(c, 'L', (0.12, -0.03, 0.085), yaw=8)
    set_leg(c, 'R', (-0.12, 0.08, 0.085), yaw=10)
    c["Lshu"] = 1 * br; c["Rshu"] = 1 * br
    return c


def aim_rifle(f, N=90):
    p = f / N
    br = math.sin(TAU * p)
    c = new_ch()
    torso(c, tz=-0.03 + 0.002 * br, ty=0.02, lean=7 + 0.4 * br, yaw=-35, head=10, roll=-9)
    B, a, u = RIFLE0
    B = B + V3((0.0015 * math.sin(TAU * 2 * p), 0.0, 0.003 * math.sin(TAU * p + 1.0)))
    rifle_right(c, B, a, u)
    rifle_left(c, B, a, u)
    set_leg(c, 'L', (0.11, -0.20, 0.085), yaw=-18)
    set_leg(c, 'R', (-0.17, 0.14, 0.085), yaw=-45)
    return c


def hold_low(f, N=90):
    p = f / N
    br = math.sin(TAU * p)
    c = new_ch()
    torso(c, tz=-0.03 + 0.002 * br, ty=0.0, lean=5 + 0.5 * br, yaw=-15, head=0)
    B, a, u = RIFLELOW0
    B = B + V3((0.002 * math.sin(TAU * 2 * p), 0.0, 0.004 * math.sin(TAU * p + 1.0)))
    rifle_right(c, B, a, u)
    rifle_left(c, B, a, u, 0.42)
    set_leg(c, 'L', (0.11, -0.10, 0.085), yaw=2)
    set_leg(c, 'R', (-0.12, 0.08, 0.085), yaw=-20)
    return c


W_CFG = {"legs": "ik", "arms": {"L": "ik", "R": "ik"}, "space": "world"}
add_clip("AimPistol", 90, True, aim_pistol, cfg=W_CFG, gun=pistol_spec(*PISTOL0), gunframe=PISTOL0)
add_clip("AimRifle", 90, True, aim_rifle, cfg=W_CFG, gun=rifle_spec(*RIFLE0), gunframe=RIFLE0)
add_clip("HoldRifleLow", 90, True, hold_low, cfg=W_CFG, gun=rifle_spec(*RIFLELOW0), gunframe=RIFLELOW0)


# ---------------------------------------------------------------- reloads
def reload_pistol():
    k = Keys(aim_pistol(0))
    base = aim_pistol(0)
    O0, a0, u0 = PISTOL0
    hip = V3((0.24, 0.03, 1.02))

    def st(t, O, a, u, tz, lean, head, left=None, lc=0.88):
        c = dict(base)
        torso(c, tz=tz, ty=0.03, lean=lean, yaw=0, head=head)
        pistol_hands(c, O, a, u, left=left is None)
        if left is not None:
            lp, lf, ld = left
            put_hand(c, 'L', lp, lf, ld, lc, 0.5)
        k.keys.append((t, c))

    a1 = unit(0, -0.94, 0.34); u1 = orth(V3((0, 0, 1)), a1)
    O1 = V3((-0.05, -0.30, 1.40))
    hf = unit(-0.2, -0.4, -0.9); hd = unit(0.9, 0.1, -0.1)
    k.keys.append((0.0, aim_pistol(0)))
    st(0.16, O1, a1, u1, -0.08, 10, -2, left=(O1 + V3((0.10, -0.02, -0.08)), unit(0.4, -0.8, -0.2), unit(0.8, 0, 0.5)), lc=0.4)
    st(0.40, O1, a1, u1, -0.10, 12, 1, left=(hip, hf, hd), lc=0.35)
    st(0.52, O1, a1, u1, -0.10, 12, 2, left=(hip, hf, hd), lc=0.85)
    mag = O1 - u1 * 0.12 + a1 * 0.01
    lf_up = unit(0.1, -0.3, 0.9)
    st(0.80, O1, a1, u1, -0.10, 12, 0, left=(mag - u1 * 0.06, lf_up, unit(0.8, 0, 0.3)), lc=0.85)
    st(0.92, O1, a1, u1, -0.10, 12, 0, left=(mag + u1 * 0.012, lf_up, unit(0.8, 0, 0.3)), lc=0.9)
    st(1.00, O1, a1, u1, -0.09, 11, -1, left=(mag + u1 * 0.012, lf_up, unit(0.8, 0, 0.3)), lc=0.5)
    r1 = a1.cross(u1)
    sl = O1 + a1 * (-0.015) + u1 * 0.062 - r1 * 0.030          # rear of the slide, support side
    st(1.07, O1, a1, u1, -0.09, 11, -1, left=(sl, a1 * 0.9 + u1 * 0.1, u1), lc=0.8)
    st(1.12, O1, a1, u1, -0.09, 11, -1, left=(sl - a1 * 0.05, a1 * 0.9 + u1 * 0.1, u1), lc=0.9)   # slide release
    a2 = unit(0, -0.99, 0.12); u2 = orth(V3((0, 0, 1)), a2)
    st(1.24, V3((-0.02, -0.40, 1.52)), a2, u2, -0.07, 8, -2)
    k.keys.append((1.40, aim_pistol(0)))
    return k


def reload_rifle():
    k = Keys(aim_rifle(0))
    base = aim_rifle(0)
    B0, a0, u0 = RIFLE0
    hip = V3((0.22, 0.02, 1.00))
    hf = unit(-0.2, -0.5, -0.9); hd = unit(0.9, 0.1, -0.1)
    aL = unit(0.0, -0.85, -0.53); uL = orth(V3((0, 0, 1)), aL)
    BL = V3((-0.12, 0.0, 1.38))

    def st(t, B, a, u, tz, lean, yaw, head, left=None, lc=0.85, roll=0.0):
        c = dict(base)
        torso(c, tz=tz, ty=0.02, lean=lean, yaw=yaw, head=head, roll=roll)
        rifle_right(c, B, a, u)
        if left is None:
            rifle_left(c, B, a, u)
        else:
            lp, lf, ld = left
            put_hand(c, 'L', lp, lf, ld, lc, 0.6)
        k.keys.append((t, c))

    mw = BL + aL * 0.335 - uL * 0.14      # magwell
    magf = unit(0.1, -0.2, 0.9); magd = unit(0.9, 0.0, 0.2)
    k.keys.append((0.0, aim_rifle(0)))
    st(0.35, BL, aL, uL, -0.07, 12, -25, 7)
    st(0.62, BL, aL, uL, -0.08, 14, -22, 9, left=(mw, magf, magd), lc=0.9)
    st(0.80, BL, aL, uL, -0.08, 14, -22, 9, left=(mw - uL * 0.07, magf, magd), lc=0.9)
    st(1.05, BL, aL, uL, -0.10, 16, -22, 8, left=(hip + V3((0, -0.02, 0.05)), hf, hd), lc=0.4)
    st(1.25, BL, aL, uL, -0.10, 16, -22, 8, left=(hip, hf, hd), lc=0.9)
    st(1.60, BL, aL, uL, -0.09, 15, -22, 9, left=(mw - uL * 0.08, magf, magd), lc=0.9)
    st(1.74, BL, aL, uL, -0.09, 15, -22, 9, left=(mw + uL * 0.015, magf, magd), lc=0.95)
    chp = BL + aL * 0.26 + V3((0.045, 0, 0.055))
    cf = unit(-0.3, -0.5, 0.1); cd = unit(0.2, 0.3, 0.9)
    st(1.92, BL, aL, uL, -0.08, 14, -24, 8, left=(chp, cf, cd), lc=0.8)
    st(2.02, BL, aL, uL, -0.08, 14, -24, 8, left=(chp + V3((0, 0.07, 0.0)), cf, cd), lc=0.8)
    st(2.10, BL, aL, uL, -0.08, 13, -26, 7, left=(chp, cf, cd), lc=0.3)
    k.keys.append((2.30, aim_rifle(0)))
    return k


_k = reload_pistol()
add_clip("ReloadPistol", 42, False, _k.fn(), cfg=W_CFG, gun=pistol_spec(*PISTOL0), gunframe=PISTOL0)
_k = reload_rifle()
add_clip("ReloadRifle", 69, False, _k.fn(), cfg=W_CFG, gun=rifle_spec(*RIFLE0), gunframe=RIFLE0)


# ---------------------------------------------------------------- contract v2 weapon clips
def lerp_v(a, b, t):
    return a + (b - a) * t


def pistol_carry(f, N=60):
    """Compressed ready: both hands, elbows in, gun close to the chest, muzzle down-forward."""
    p = f / N
    br = math.sin(TAU * p)
    c = new_ch()
    torso(c, tz=-0.025 + 0.002 * br, ty=0.02, lean=5 + 0.3 * br, yaw=0, head=-2)
    O, a, u = PISTOLCARRY0
    O = O + V3((0.0015 * math.sin(TAU * 2 * p), 0.0, 0.003 * math.sin(TAU * p + 1.0)))
    pistol_hands(c, O, a, u)
    for sd in "LR":
        c[sd + "Ex"] = SIDE[sd][1] * 0.15; c[sd + "Ey"] = 0.7; c[sd + "Ez"] = -1.0
    set_leg(c, 'L', (0.115, -0.04, 0.085), yaw=7)
    set_leg(c, 'R', (-0.115, 0.07, 0.085), yaw=8)
    c["Lshu"] = 1 * br; c["Rshu"] = 1 * br
    return c


_ca = unit(0, -0.95, -0.28)
PISTOLCARRY0 = (V3((-0.015, -0.30, 1.30)), _ca, orth(V3((0, 0, 1)), _ca))
add_clip("PistolCarry", 60, True, pistol_carry, cfg=W_CFG, gun=pistol_spec(*PISTOLCARRY0), gunframe=PISTOLCARRY0)


def gun_state(base, B, a, u, tz, lean, yaw, head, left=None, lc=0.85, roll=0.0, ty=0.02, lspec=0.62):
    c = dict(base)
    torso(c, tz=tz, ty=ty, lean=lean, yaw=yaw, head=head, roll=roll)
    rifle_right(c, B, a, u)
    if left is None:
        rifle_left(c, B, a, u, lspec)
    else:
        lp, lf, ld = left
        put_hand(c, 'L', lp, lf, ld, lc, 0.6)
    return c


def weapon_swap():
    base = hold_low(0)
    k = Keys(base)
    k.keys.append((0.0, hold_low(0)))
    # frames 0-6: gun dropped down and across to the hip, support hand goes to the belt
    a1 = unit(0.20, -0.62, -0.76); u1 = orth(V3((0, 0, 1)), a1)
    k.keys.append((3 / 30.0, gun_state(base, V3((-0.11, 0.02, 1.26)), a1, u1, -0.06, 8, -12, 2, lspec=0.45)))
    a2 = unit(0.12, -0.30, -0.95); u2 = orth(V3((0, 0, 1)), a2)
    k.keys.append((6 / 30.0, gun_state(base, V3((-0.20, 0.05, 1.08)), a2, u2, -0.10, 10, -10, 3,
                                       left=(V3((0.10, -0.20, 1.06)), unit(0.1, -0.5, -0.8), unit(0.9, 0.0, 0.2)), lc=0.4)))   # model switch here
    # frames 6-14: the new weapon rises across the body into the low-ready
    a3 = unit(0.10, -0.55, -0.82); u3 = orth(V3((0, 0, 1)), a3)
    k.keys.append((9 / 30.0, gun_state(base, V3((-0.14, 0.02, 1.26)), a3, u3, -0.07, 7, -14, 1, lspec=0.40)))
    k.keys.append((13 / 30.0, hold_low(0)))
    return k


_k = weapon_swap()
add_clip("WeaponSwap", 14, False, _k.fn(), cfg=W_CFG, gun=rifle_spec(*RIFLELOW0), gunframe=RIFLELOW0)
CLIPS["WeaponSwap"]["switch_frame"] = 6

# shotgun: tube loading from below, gun held up across the chest, eyes up
_sa = unit(0.02, -0.72, 0.69)
SG_LOAD = (V3((-0.14, 0.03, 1.10)), _sa, orth(V3((0, 0, 1)), _sa))
SG_BELT = V3((0.24, 0.0, 1.00))
SG_YAW = -22


def sg_port():
    B, a, u = SG_LOAD
    return B + a * 0.30 - u * 0.075


def sg_state(left_pos, lc, tz=-0.07, head=2, lf=None, ld=None):
    base = aim_rifle(0)
    Bq, aq, uq = SG_LOAD
    return gun_state(base, Bq, aq, uq, tz, 11, SG_YAW, head,
                     left=(left_pos, lf if lf is not None else unit(0.15, -0.25, 0.95),
                           ld if ld is not None else unit(0.9, 0.1, 0.1)), lc=lc)


def sg_hover(lc=0.5):
    return sg_state(sg_port() - SG_LOAD[2] * 0.055 + V3((0, 0.0, -0.02)), lc)


def shotgun_start():
    base = aim_rifle(0)
    k = Keys(base)
    k.keys.append((0.0, aim_rifle(0)))
    am = unit(0.01, -0.86, 0.38); um = orth(V3((0, 0, 1)), am)
    mid = gun_state(base, V3((-0.13, 0.02, 1.28)), am, um, -0.05, 9, -30, 4, lspec=0.55)
    k.keys.append((0.14, mid))
    k.keys.append((11 / 30.0, sg_hover()))
    return k


def shotgun_shell():
    k = Keys(sg_hover(), periodic=True)
    port = sg_port(); u = SG_LOAD[2]
    k.keys.append((0.0, sg_hover(0.5)))
    k.keys.append((0.18, sg_state(lerp_v(port - u * 0.055, SG_BELT, 0.45), 0.35)))
    k.keys.append((0.30, sg_state(SG_BELT, 0.30, lf=unit(-0.2, -0.5, -0.8), ld=unit(0.9, 0.1, 0.2))))
    k.keys.append((0.37, sg_state(SG_BELT, 0.85, lf=unit(-0.2, -0.5, -0.8), ld=unit(0.9, 0.1, 0.2))))
    k.keys.append((0.51, sg_state(lerp_v(port - u * 0.055, SG_BELT, 0.40), 0.85)))
    k.keys.append((0.62, sg_state(port - u * 0.03, 0.85, tz=-0.075)))
    k.keys.append((0.70, sg_hover(0.5)))
    return k


def shotgun_end():
    base = aim_rifle(0)
    k = Keys(base)
    B, a, u = SG_LOAD
    k.keys.append((0.0, sg_hover()))
    pump_pos = B + a * 0.50 - u * 0.045
    k.keys.append((0.08, sg_state(pump_pos, 0.8, lf=unit(0.1, -0.9, -0.2), ld=unit(0.8, 0.0, 0.5))))
    am = unit(0.01, -0.86, 0.38); um = orth(V3((0, 0, 1)), am)
    Bm = V3((-0.12, 0.01, 1.30))
    pm = Bm + am * 0.42 - um * 0.045
    k.keys.append((0.18, gun_state(base, Bm, am, um, -0.06, 9, -28, 3, left=(pm, unit(0.1, -0.9, -0.2), unit(0.8, 0.0, 0.5)), lc=0.8)))   # rack back
    ah = unit(0.02, -0.96, 0.25)
    k.keys.append((0.30, gun_state(base, V3((-0.11, 0.02, 1.42)), ah, orth(V3((0, 0, 1)), ah),
                                   -0.04, 8, -32, 6, left=(V3((-0.12, -0.33, 1.38)), unit(0.1, -0.9, -0.2), unit(0.8, 0.0, 0.5)), lc=0.8)))   # rack forward
    k.keys.append((14 / 30.0, aim_rifle(0)))
    return k


_k = shotgun_start()
add_clip("ShotgunLoadStart", 11, False, _k.fn(), cfg=W_CFG, gun=rifle_spec(*RIFLE0), gunframe=RIFLE0)
_k = shotgun_shell()
add_clip("ShotgunLoadShell", 21, True, _k.fn(), cfg=W_CFG, gun=rifle_spec(*SG_LOAD), gunframe=SG_LOAD)
_k = shotgun_end()
add_clip("ShotgunLoadEnd", 14, False, _k.fn(), cfg=W_CFG, gun=rifle_spec(*SG_LOAD), gunframe=SG_LOAD)


# ---------------------------------------------------------------- ground fit (for collapses)
GROUND_PTS = []


def lowest_point(c, cfg):
    D, P = solve(c, cfg)
    m = 1e9
    for sd in "LR":
        nm = SIDE[sd][0]
        ft = nm + "Foot"; x = HEAD[ft].x
        for y in (0.08, -0.105):
            pt = P[ft] + D[ft] @ (Vector((x, y, 0.0)) - HEAD[ft])
            m = min(m, pt.z)
        for bn, r in ((nm + "LowerLeg", 0.06), (nm + "UpperLeg", 0.085), (nm + "LowerArm", 0.045),
                      (nm + "Hand", 0.04), (nm + "UpperArm", 0.05)):
            m = min(m, P[bn].z - r)
        t = P[nm + "Hand"] + D[nm + "Hand"] @ (TAIL[nm + "Hand"] - HEAD[nm + "Hand"])
        m = min(m, t.z - 0.035)
    m = min(m, P["Hips"].z - 0.10, P["Chest"].z - 0.12)
    ct = P["Chest"] + D["Chest"] @ (TAIL["Chest"] - HEAD["Chest"])
    m = min(m, ct.z - 0.12)
    hc = P["Head"] + D["Head"] @ (Vector((0, 0.005, 1.695)) - HEAD["Head"])
    m = min(m, hc.z - 0.10)
    return m


def grounded(fn, cfg):
    def g(f):
        c = fn(f)
        c["hip_tz"] -= lowest_point(c, cfg)
        return c
    return g


def legs_fk(c, sd, hf, k, ha=0.0, hr=0.0, flat=True, t=0.0):
    c[sd + "hf"] = hf; c[sd + "k"] = k; c[sd + "ha"] = ha; c[sd + "hr"] = hr
    c[sd + "ap"] = (k - hf) if flat else 20.0
    c[sd + "t"] = t


def arms_fk(c, af, aa, ae, afr=None, aar=None, aer=None, curl=0.3, curlr=None):
    for sd, (a, b, e, cu) in (('L', (af, aa, ae, curl)), ('R', (af if afr is None else afr, aa if aar is None else aar,
                                                               ae if aer is None else aer, curl if curlr is None else curlr))):
        c[sd + "af"] = a; c[sd + "aa"] = b; c[sd + "ae"] = e; c[sd + "c"] = cu


# ---------------------------------------------------------------- player death
def death_keys():
    base = new_ch()
    k = Keys(base)
    c0 = dict(base); c0.update(hip_tz=-0.01, stab=0.5)
    k.keys.append((0.0, c0))
    c1 = dict(c0); c1.update(hip_rx=-4, sp_rx=-5, ch_rx=-9, hd_rx=-14, nk_rx=-4, stab=0.0)
    arms_fk(c1, 22, 40, 28); legs_fk(c1, 'L', 6, 8); legs_fk(c1, 'R', -4, 6)
    k.keys.append((0.10, c1))
    c2 = dict(c1); c2.update(hip_rx=-6, sp_rx=-4, ch_rx=-4, hd_rx=-8, hip_ty=0.06, hip_rz=6, ch_rz=-4)
    arms_fk(c2, 12, 25, 30, curl=0.3); legs_fk(c2, 'L', 42, 78); legs_fk(c2, 'R', 34, 70)
    k.keys.append((0.36, c2))
    c3 = dict(c2); c3.update(hip_rx=-34, sp_rx=-6, ch_rx=-12, hd_rx=-12, hip_ty=0.16, hip_rz=10, ch_rz=-8)
    arms_fk(c3, 55, 62, 18, afr=40, aar=70, aer=22, curl=0.2); legs_fk(c3, 'L', 62, 112, flat=False); legs_fk(c3, 'R', 48, 96, ha=8, flat=False)
    c3["Lap"] = 30; c3["Rap"] = 30
    k.keys.append((0.62, c3))
    c4 = dict(c3); c4.update(hip_rx=-78, sp_rx=-4, ch_rx=-6, hd_rx=-14, hip_ty=0.28, hip_rz=14, ch_rz=-6)
    arms_fk(c4, 105, 55, 12, afr=88, aar=70, aer=16, curl=0.15); legs_fk(c4, 'L', 26, 52, ha=4, flat=False); legs_fk(c4, 'R', 18, 40, ha=10, flat=False)
    c4["Lap"] = 28; c4["Rap"] = 28
    k.keys.append((0.98, c4))
    c5 = dict(c4); c5.update(hip_rx=-90, sp_rx=1, ch_rx=2, hd_rx=-4, hd_rz=16, hip_ty=0.32, hip_rz=8, ch_rz=-3)
    arms_fk(c5, 30, 62, 10, afr=18, aar=58, aer=12, curl=0.2); legs_fk(c5, 'L', 6, 14, ha=7, flat=False); legs_fk(c5, 'R', 2, 24, ha=11, hr=-8, flat=False)
    c5["Lap"] = 20; c5["Rap"] = 28
    k.keys.append((1.28, c5))
    c6 = dict(c5); c6.update(hip_rx=-88, ch_rx=4, hd_rx=-1, hd_rz=18)
    arms_fk(c6, 24, 58, 12, afr=14, aar=56, aer=12, curl=0.22)
    k.keys.append((1.50, c6))
    c7 = dict(c6)
    k.keys.append((1.80, c7))
    return k


DEATH_CFG = {"legs": "fk", "arms": {"L": "fk", "R": "fk"}, "arm_rel": True}
_k = death_keys()
add_clip("Death", 54, False, grounded(_k.fn(), DEATH_CFG), cfg=DEATH_CFG, scale=2.6, camz=0.8)


# ---------------------------------------------------------------- enemies ("Hollows")
def enemy_idle(f, N=96):
    p = f / N
    c = new_ch()
    sw = math.sin(TAU * p)
    sw2 = math.sin(TAU * 2 * p + 0.7)
    br = math.sin(TAU * 3 * p - 0.5)
    c["hip_tz"] = -0.06 - 0.004 * math.cos(TAU * 2 * p)
    c["hip_ty"] = 0.04
    c["hip_tx"] = 0.035 * sw + 0.008 * sw2
    c["hip_ry"] = -2.5 * sw + 1.0 * sw2
    c["hip_rz"] = 2.5 * math.sin(TAU * p + 0.6)
    c["hip_rx"] = 5
    c["sp_rx"] = 10 + 0.8 * br; c["ch_rx"] = 9 + 1.0 * br
    c["sp_ry"] = 3.5 * sw; c["ch_ry"] = -6 + 2.0 * sw2
    c["sp_rz"] = -2.5 * math.sin(TAU * p + 0.6 - 0.35)
    c["nk_rx"] = 16 + 1.5 * math.sin(TAU * 2 * p + 1.1); c["hd_rx"] = 7
    c["nk_rz"] = 4 * math.sin(TAU * p + 1.9); c["hd_ry"] = 7 + 2 * sw2
    c["hd_rz"] = 3 * math.sin(TAU * 2 * p + 2.4) + (4 if (p % 0.5) > 0.44 else 0) * 0
    c["stab"] = 0.1
    c["Rshu"] = -7 + 0.8 * br; c["Lshu"] = 2 + 0.8 * br
    for sd in "LR":
        s = SIDE[sd][1]
        c[sd + "af"] = 24 + 5 * math.sin(TAU * p + 0.8 * s) + (4 if sd == 'R' else 0)
        c[sd + "aa"] = 16 + 4 * math.sin(TAU * 2 * p + s)
        c[sd + "ae"] = 42 + 6 * math.sin(TAU * p + 1.5 + s)
        c[sd + "w2"] = 14 + 6 * math.sin(TAU * 2 * p + 2.0 + s)
        c[sd + "c"] = 0.5 + 0.08 * math.sin(TAU * 2 * p + 1.0 * s); c[sd + "cth"] = 0.1; c[sd + "spr"] = 0.6
    stand_feet(c, 0.12, 0.0, 0.12, 0.09, 12, 16)
    return c


EN_CFG = {"legs": "ik", "arm_rel": True}
add_clip("EnemyIdle", 96, True, enemy_idle, cfg=EN_CFG)


def shamble_post(S, c, p):
    # one shoulder dropped, head lolling, arms reaching with unequal swing, uneven pelvis
    c["Rshu"] = -8; c["Lshu"] = 3
    c["ch_ry"] += -7; c["hd_ry"] = 9 + 3 * math.sin(TAU * (p + 0.2))
    c["hd_rz"] = 5 * math.sin(TAU * (p - 0.1))
    c["Raf"] += 6; c["Lae"] += 8
    c["Laf"] = c["Laf"] * 0.8
    c["Rc"] = 0.6; c["Lc"] = 0.45
    c["Lspr"] = 0.7; c["Rspr"] = 0.7
    c["hip_rz"] += -3
    c["nk_rz"] = 6 * math.sin(TAU * (p + 0.35))


def enemy_loco(**kw):
    d = dict(armmode="swing", post=shamble_post, arm_rel_dummy=None)
    d.pop("arm_rel_dummy")
    d.update(kw)
    return tuned(**d)


SHAMBLE = enemy_loco(T=34, speed=1.6, betaL=0.63, betaR=0.55, offR=0.465, strike=8, u1=0.07, u2=0.45, push=12,
                     clear=0.07, clearL=0.07, clearR=0.07, swing_pitchL=-12, toe_swingL=-4, peak=0.5, swing_pitch=-4, width=0.065, arc=0.02, cy=0.05, toeout=12, bobA=0.028,
                     sway=0.05, yaw=7, tilt=5, lean=14, cr=0.3, swing_f=52, swing_b=-18, elbow0=62, elbow1=7,
                     ab0=15, xab=1, curl=0.5, thumb=0.1, wrist=12, stab=0.15, neck=20, head=6, reach=0.99,
                     toe_swing=-10, beta=0.6)
ENJOG = enemy_loco(T=20, speed=3.2, beta=0.36, strike=6, u1=0.04, u2=0.17, push=32, clear=0.14, peak=0.5,
                   swing_pitch=-6, width=0.06, cy=0.07, toeout=6, bobA=0.034, sway=0.03, yaw=10, tilt=3, lean=16, cr=0.5,
                   swing_f=45, swing_b=-5, elbow0=70, elbow1=14, ab0=14, xab=5, curl=0.5, thumb=0.1, wrist=10,
                   stab=0.2, neck=18, head=6, reach=0.985, toe_swing=-10)
ENSPRINT = enemy_loco(T=17, speed=5.0, beta=0.27, strike=4, u1=0.03, u2=0.12, push=40, clear=0.2, peak=0.5,
                      swing_pitch=-10, width=0.05, cy=0.12, toeout=5, bobA=0.04, sway=0.03, yaw=12, tilt=3, lean=26, cr=0.4,
                      swing_f=68, swing_b=12, elbow0=50, elbow1=12, ab0=18, xab=6, curl=0.5, thumb=0.1, wrist=10,
                      stab=0.2, neck=20, head=8, reach=1.0, toe_swing=-14)
for nm, lo in (("Shamble", SHAMBLE), ("EnemyJog", ENJOG), ("EnemySprint", ENSPRINT)):
    loco_clip(nm, lo)
    CLIPS[nm]["cfg"] = {"legs": "ik", "arm_rel": True}


# attack / hit: keyed from the hunched idle stance
def en_state(t, **kw):
    c = enemy_idle(0)
    for k_, v in kw.items():
        c[k_] = v
    return c


def attack_keys():
    k = Keys(enemy_idle(0))
    e0 = enemy_idle(0)
    k.keys.append((0.0, e0))
    # wind up: weight back, both arms raised wide and back
    w = dict(e0)
    w.update(hip_ty=0.14, hip_tz=-0.10, hip_rx=-1, sp_rx=-2, ch_rx=-8, nk_rx=-2, hd_rx=-12, hip_rz=14, ch_rz=-22, sp_rz=-8,
             hip_tx=0.0, hip_ry=0, ch_ry=-4, stab=0.0, Rshu=0, Lshu=0)
    arms_fk(w, 128, 55, 80, curl=0.3)
    w.update(Rc=0.25, Lc=0.25, Lspr=1, Rspr=1)
    w.update(Lw2=-20, Rw2=-20)
    set_leg(w, 'L', (0.13, -0.06, 0.085), yaw=14)
    set_leg(w, 'R', (-0.13, 0.17, 0.085), yaw=20)
    k.keys.append((0.14, w))
    w2 = dict(w)
    w2.update(hip_ty=0.17, hip_tz=-0.12, ch_rx=-10, hip_rz=16, ch_rz=-26, Laf=140, Raf=140, hd_rx=-14)
    wm = lerp_ch(w, w2, 0.55)
    k.keys.append((0.24, wm))
    k.keys.append((0.33, w2))
    # contact: lunge forward, arms swiped down and across, claws open
    ct = dict(e0)
    ct.update(hip_ty=-0.26, hip_tz=-0.16, hip_rx=14, sp_rx=14, ch_rx=14, nk_rx=24, hd_rx=4, hip_rz=-12, ch_rz=14, sp_rz=6,
              hip_ry=0, ch_ry=3, hip_tx=0.0, stab=0.0, Rshu=0, Lshu=0)
    arms_fk(ct, 78, 22, 22, afr=82, aar=26, aer=28, curl=0.2)
    ct.update(Lspr=1, Rspr=1, Lw2=10, Rw2=10)
    set_leg(ct, 'L', (0.13, -0.42, 0.085), yaw=10)
    set_leg(ct, 'R', (-0.12, 0.10, 0.085), yaw=20)
    k.keys.append((0.45, ct))
    ft = dict(ct)
    ft.update(hip_ty=-0.28, hip_tz=-0.18, hip_rx=18, sp_rx=18, ch_rx=16, nk_rx=26, hip_rz=-16, ch_rz=18,
              Laf=42, Raf=34, Laa=8, Raa=12, Lae=26, Rae=30)
    k.keys.append((0.58, ft))
    rc = dict(e0)
    rc.update(hip_ty=-0.10, hip_tz=-0.09, hip_rx=6, sp_rx=12, ch_rx=10)
    arms_fk(rc, 30, 18, 48, curl=0.5)
    set_leg(rc, 'L', (0.13, -0.22, 0.085), yaw=12)
    set_leg(rc, 'R', (-0.12, 0.10, 0.085), yaw=16)
    k.keys.append((0.82, rc))
    k.keys.append((1.1, enemy_idle(0)))
    return k


def hit_keys():
    k = Keys(enemy_idle(0))
    e0 = enemy_idle(0)
    k.keys.append((0.0, e0))
    h = dict(e0)
    h.update(hip_rx=-3, sp_rx=-2, ch_rx=-8, nk_rx=-6, hd_rx=-18, hd_rz=18, hip_rz=10, ch_rz=-14, hip_tz=-0.09, hip_ty=0.07,
             hip_tx=-0.03, ch_ry=-10, stab=0.0)
    arms_fk(h, 58, 52, 34, afr=46, aar=60, aer=38)
    k.keys.append((0.08, h))
    h2 = dict(e0)
    h2.update(hip_rx=8, sp_rx=14, ch_rx=12, nk_rx=24, hd_rx=10, hd_rz=-6, hip_rz=-4, ch_rz=5, hip_tz=-0.08, hip_ty=0.05)
    arms_fk(h2, 36, 30, 46, curl=0.45)
    k.keys.append((0.2, h2))
    k.keys.append((11 / 30.0, enemy_idle(0)))
    return k


_k = attack_keys()
add_clip("EnemyAttack", 33, False, _k.fn(), cfg=EN_CFG, contact=0.45)
_k = hit_keys()
add_clip("EnemyHit", 11, False, _k.fn(), cfg=EN_CFG)


# enemy death: twisting fall onto the front
def edeath_keys():
    base = new_ch()
    k = Keys(base)
    c0 = dict(base)
    c0.update(hip_tz=-0.03, hip_rx=5, sp_rx=10, ch_rx=9, nk_rx=16, hd_rx=7, stab=0.0, Rshu=-7)
    arms_fk(c0, 26, 16, 42, curl=0.5); legs_fk(c0, 'L', 6, 14); legs_fk(c0, 'R', 3, 10)
    k.keys.append((0.0, c0))
    c1 = dict(c0)
    c1.update(hip_rx=-2, sp_rx=-4, ch_rx=-8, hd_rx=-16, nk_rx=-2, hip_rz=14, ch_rz=-20, ch_ry=-8)
    arms_fk(c1, 50, 45, 30, afr=40, aar=55, aer=34); legs_fk(c1, 'L', 8, 16); legs_fk(c1, 'R', 2, 10)
    k.keys.append((0.14, c1))
    c2 = dict(c1)
    c2.update(hip_rx=10, sp_rx=12, ch_rx=10, nk_rx=14, hd_rx=4, hip_rz=-18, ch_rz=16, sp_rz=-6, hip_ty=-0.04, hd_rz=-10)
    arms_fk(c2, 38, 25, 70, curl=0.5); legs_fk(c2, 'L', 48, 92); legs_fk(c2, 'R', 22, 60)
    k.keys.append((0.50, c2))
    c3 = dict(c2)
    c3.update(hip_rx=44, sp_rx=10, ch_rx=8, nk_rx=10, hip_rz=-48, ch_rz=-12, hip_ry=14, hip_ty=-0.14)
    arms_fk(c3, 88, 55, 30, afr=70, aar=40, aer=50, curl=0.3); legs_fk(c3, 'L', 62, 118, flat=False); legs_fk(c3, 'R', 12, 40, ha=10, flat=False)
    c3["Lap"] = 40; c3["Rap"] = 25
    k.keys.append((0.86, c3))
    c4 = dict(c3)
    c4.update(hip_rx=86, sp_rx=-4, ch_rx=-2, nk_rx=-8, hd_rx=-6, hip_rz=-52, ch_rz=-6, hip_ry=10, hip_ty=-0.30, hd_rz=-48)
    arms_fk(c4, 140, 60, 18, afr=120, aar=48, aer=30, curl=0.2); legs_fk(c4, 'L', -4, 28, ha=10, flat=False); legs_fk(c4, 'R', 6, 34, ha=8, hr=-10, flat=False)
    c4["Lap"] = 10; c4["Rap"] = 14
    k.keys.append((1.28, c4))
    c5 = dict(c4)
    c5.update(hip_rx=84, hip_ry=8, hd_rz=-52, ch_rx=2, hip_ty=-0.31)
    arms_fk(c5, 132, 58, 22, afr=116, aar=46, aer=34, curl=0.25)
    k.keys.append((1.55, c5))
    k.keys.append((1.9, dict(c5)))
    return k


_k = edeath_keys()
add_clip("EnemyDeath", 57, False, grounded(_k.fn(), DEATH_CFG), cfg=DEATH_CFG, scale=2.6, camz=0.8)


# ============================================================================ hand report
def U(v):
    """Blender character axes (x left, y back, z up) -> Unity (x right, y up, z forward)."""
    return (round(-v.x, 3), round(v.z, 3), round(-v.y, 3))


def report_hands(cl):
    sc = bpy.context.scene
    sc.frame_set(0)
    hips = ARM.pose.bones["Hips"]
    hh = hips.head.copy()
    hq = hips.matrix.to_quaternion()
    out = []
    for sd in "LR":
        nm = SIDE[sd][0] + "Hand"
        pb = ARM.pose.bones[nm]
        m = pb.matrix
        w = pb.head - hh
        fd = m.col[1].to_3d().normalized(); dd = m.col[2].to_3d().normalized()
        palm = w + fd * 0.045
        rel = hq.inverted() @ m.to_quaternion()
        out.append("  %s hand wrist(rel Hips, Blender xyz)=(%.3f %.3f %.3f) Unity xyz=%s | palm centre Unity=%s | fingers dir Unity=%s dorsal Unity=%s | quat(rel Hips, bone frame, wxyz)=(%.3f %.3f %.3f %.3f)" % (
            sd, w.x, w.y, w.z, U(w), U(palm), U(fd), U(dd), rel.w, rel.x, rel.y, rel.z))
    gf = cl.get("gunframe")
    if gf:
        O, a, u = gf
        pb = ARM.pose.bones["RightHand"]
        inv = pb.matrix.inverted()
        lo = inv @ O
        la = inv.to_3x3() @ a; lu = inv.to_3x3() @ u
        out.append("  gun origin in RightHand bone-local (x,y,z)=(%.3f %.3f %.3f) barrel axis local=(%.3f %.3f %.3f) up local=(%.3f %.3f %.3f)" % (lo.x, lo.y, lo.z, la.x, la.y, la.z, lu.x, lu.y, lu.z))
        if cl["name"] != "HoldRifleLow":
            pass
        lh = ARM.pose.bones["LeftHand"].head; rh = ARM.pose.bones["RightHand"].head
        out.append("  hands distance (wrist to wrist)=%.3f m; gun origin rel Hips Unity=%s" % ((lh - rh).length, U(O - hh)))
        if cl["name"] == "AimPistol":
            head = ARM.pose.bones["Head"].head
            out.append("  gun centre to face: %.3f m (forward)" % abs((O + a * 0.055).y - (head.y - 0.09)))
    for l in out:
        print("HANDS", cl["name"], l)
    # IK reach check
    c = cl["fn"](0.0)
    D, P = solve(c, cl["cfg"])
    for sd in "LR":
        nm = SIDE[sd][0] + "Hand"
        tgt = Vector((c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"]))
        print("HANDS", cl["name"], "  %s wrist IK miss=%.4f m" % (sd, (P[nm] - tgt).length))
# ============================================================================ get-up clips
# GetUpBack (from supine) and GetUpFront (from prone). Authored in WORLD space with IK for hands and feet:
# a planted hand/foot holds one identical world target over consecutive keys (zero slip by construction).
# Frame 0 is a ragdoll-like lying pose with the hips at (0, GU_Y0, ~0.11); the last frame is Idle frame 0
# (hips at the origin), so the root is fixed and the body travels GU_Y0 m toward the character's front.
GU_CFG = {"legs": "ik", "arms": {"L": "ik", "R": "ik"}, "space": "world"}
GU_Y0 = 0.42                      # lying hips offset along +Y (behind the standing spot), Blender metres
KNEE_Z = 0.06                     # knee-joint height when a knee rests on the ground
L_UL, L_LL = LEN["LeftUpperLeg"], LEN["LeftLowerLeg"]
FOOT_BOX = [(x, y, z) for x in (-0.045, 0.045) for y in (-0.115, 0.085) for z in (0.0, 0.07)]
TOE_BOX = [(x, y, z) for x in (-0.045, 0.045) for y in (-0.19, -0.105) for z in (0.0, 0.04)]


def foot_corners(c, sd, ank=None):
    """World positions of the foot and toe box corners (proxy extents) for the leg IK targets in c."""
    nm, s = SIDE[sd]
    Df = qz(s * c[sd + "Fy"]) @ qy(c[sd + "Fr"]) @ qx(-c[sd + "Fp"])
    Dt = Df @ qx(-c[sd + "T"])
    A = Vector(ank) if ank is not None else Vector((c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"]))
    hx = HEAD[nm + "Foot"].x
    hf = HEAD[nm + "Foot"]
    ht = HEAD[nm + "Toes"]
    pt = A + Df @ (ht - hf)
    out = [A + Df @ (Vector((hx + x, y, z)) - hf) for (x, y, z) in FOOT_BOX]
    out += [pt + Dt @ (Vector((hx + x, y, z)) - ht) for (x, y, z) in TOE_BOX]
    return out


def gu_hips(c, x, y, z, rx=0.0, ry=0.0, rz=0.0):
    c["hip_tx"] = x; c["hip_ty"] = y; c["hip_tz"] = z - HEAD["Hips"].z
    c["hip_rx"] = rx; c["hip_ry"] = ry; c["hip_rz"] = rz


def gu_hand(c, sd, pos, f=(0, -1, 0), d=(0, 0, 1), pole=None, curl=0.2):
    s = SIDE[sd][1]
    c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"] = pos
    c[sd + "Hfx"], c[sd + "Hfy"], c[sd + "Hfz"] = unit(*f)
    c[sd + "Hdx"], c[sd + "Hdy"], c[sd + "Hdz"] = unit(*d)
    c[sd + "Ex"], c[sd + "Ey"], c[sd + "Ez"] = pole if pole else (0.6 * s, 0.8, 0.0)
    c[sd + "c"] = curl


def gu_foot(c, sd, pos, fp=0.0, fr=0.0, fy=6.0, toe=0.0, pole=None, z=None):
    """Leg IK target. z=None: lower the ankle so the lowest foot corner just touches the ground."""
    c[sd + "Fp"] = fp; c[sd + "Fr"] = fr; c[sd + "Fy"] = fy; c[sd + "T"] = toe
    if z is None:
        low = min(p.z for p in foot_corners(c, sd, (0.0, 0.0, 0.0)))
        z = -low + 0.001
    c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"] = pos[0], pos[1], z
    if pole:
        c[sd + "Px"], c[sd + "Py"], c[sd + "Pz"] = pole


def tuck_fp(c, sd, z, fr=0.0, fy=0.0):
    """Foot pitch (toes tucked, ball on the ground) for an ankle at height z."""
    lo, hi = -100.0, 20.0
    for _ in range(40):
        mid = 0.5 * (lo + hi)
        c[sd + "Fp"] = mid; c[sd + "Fr"] = fr; c[sd + "Fy"] = fy; c[sd + "T"] = 0.0
        low = min(p.z for p in foot_corners(c, sd, (0.0, 0.0, z)))
        if low > 0.0:
            hi = mid
        else:
            lo = mid
    return 0.5 * (lo + hi)


def gu_resolve(c):
    """Knee-plant: with weight Kw the planted knee (Kx, Ky, KNEE_Z) fixes the hip height and the ankle."""
    c = dict(c)
    for sd in "LR":
        w = c[sd + "Kw"]
        if w <= 1e-5:
            continue
        nm = SIDE[sd][0]
        Dh, Ph = hips_xform(c)
        hj = Ph + Dh @ (HEAD[nm + "UpperLeg"] - HEAD["Hips"])
        dxy2 = (hj.x - c[sd + "Kx"]) ** 2 + (hj.y - c[sd + "Ky"]) ** 2
        if dxy2 < L_UL * L_UL:
            zt = KNEE_Z + math.sqrt(L_UL * L_UL - dxy2)
            c["hip_tz"] += w * (zt - hj.z)
        a = rad(c[sd + "Ka"])
        A = (c[sd + "Kx"], c[sd + "Ky"] + math.cos(a) * L_LL, KNEE_Z + math.sin(a) * L_LL)
        for i, k in enumerate("xyz"):
            c[sd + "A" + k] = lerp(c[sd + "A" + k], A[i], w)
        # knee pole: from the hip-ankle line toward the planted knee, so the IK knee lands on it
        Av = Vector(A); Kv = Vector((c[sd + "Kx"], c[sd + "Ky"], KNEE_Z))
        Dh, Ph = hips_xform(c)
        hj = Ph + Dh @ (HEAD[nm + "UpperLeg"] - HEAD["Hips"])
        ax = (Av - hj).normalized()
        pk = (Kv - hj) - ax * (Kv - hj).dot(ax)
        if pk.length > 1e-4:
            pk.normalize()
            cur = Vector((c[sd + "Px"], c[sd + "Py"], c[sd + "Pz"])).normalized()
            pm = cur.lerp(pk, w)
            c[sd + "Px"], c[sd + "Py"], c[sd + "Pz"] = pm
    return c


def arm_rel(c, sd, off, f=(0, -1, 0), d=(0, 0, 1), pole=None, curl=0.3):
    """Free arm: wrist at (current shoulder joint + off) given the hips/spine channels already in c."""
    cfg = {"legs": "fk", "arms": {"L": "fk", "R": "fk"}}
    D, P = solve(c, cfg)
    S = P[SIDE[sd][0] + "UpperArm"]
    gu_hand(c, sd, (S.x + off[0], S.y + off[1], S.z + off[2]), f, d, pole, curl)


def arms_idle(c, sd_list="LR", pole_out=0.8):
    """Free arms hanging as in Idle, placed relative to the current shoulder joints."""
    ci = idle_pose(0)
    D, P = solve(ci, {"legs": "ik"})
    for sd in sd_list:
        nm = SIDE[sd][0]
        off = P[nm + "Hand"] - P[nm + "UpperArm"]
        arm_rel(c, sd, off, (0.3 * SIDE[sd][1], -1, 0), (0, 0, 1), (pole_out * SIDE[sd][1], 0.6, -0.3), 0.3)


def idle_ik():
    """Idle frame 0 expressed in the get-up (world IK) channel set."""
    c = idle_pose(0)
    D, P = solve(c, {"legs": "ik"})
    for sd in "LR":
        nm = SIDE[sd][0]
        S = P[nm + "UpperArm"]; E = P[nm + "LowerArm"]; W = P[nm + "Hand"]
        ax = (W - S).normalized()
        pole = (E - S) - ax * (E - S).dot(ax)
        fv = D[nm + "Hand"] @ REST_DIR[nm + "Hand"]
        dv = D[nm + "Hand"] @ Vector((0, 0, 1))
        c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"] = W
        c[sd + "Hfx"], c[sd + "Hfy"], c[sd + "Hfz"] = fv
        c[sd + "Hdx"], c[sd + "Hdy"], c[sd + "Hdz"] = dv
        c[sd + "Ex"], c[sd + "Ey"], c[sd + "Ez"] = pole.normalized()
    return c


def gu_points(c, cfg):
    """Contact candidates for slip checks: {name: world point}."""
    D, P = solve(c, cfg)
    pts = {}
    for sd in "LR":
        nm = SIDE[sd][0]
        pts[sd + "knee"] = P[nm + "LowerLeg"]
        for i, p in enumerate(foot_corners(c, sd)):
            pts["%sfoot%d" % (sd, i)] = p
        h = nm + "Hand"
        pts[sd + "hand0"] = P[h]
        pts[sd + "hand1"] = P[h] + D[h] @ (REST_DIR[h] * 0.05)
        pts[sd + "hand2"] = P[h] + D[h] @ (REST_DIR[h] * 0.09)
    return pts


def gu_clearance(c, cfg):
    """(min clearance m, name) over proxy body capsules sampled along the bones."""
    D, P = solve(c, cfg)
    worst = [9.0, ""]

    def chk(p, r, nm):
        if p.z - r < worst[0]:
            worst[0] = p.z - r; worst[1] = nm

    def seg(a, b, ra, rb, nm, n=4):
        for i in range(n + 1):
            t = i / n
            chk(a.lerp(b, t), lerp(ra, rb, t), nm)
    for bn in ("Hips", "Spine", "Chest"):
        seg(P[bn], P[bn] + D[bn] @ (TAIL[bn] - HEAD[bn]), 0.10, 0.10, bn)
    seg(P["Neck"], P["Head"], 0.04, 0.04, "Neck", 2)
    chk(P["Head"] + D["Head"] @ (Vector((0, 0.005, 1.695)) - HEAD["Head"]), 0.10, "Head")
    for sd in "LR":
        nm = SIDE[sd][0]
        ual, lal, hnd = nm + "UpperArm", nm + "LowerArm", nm + "Hand"
        seg(P[ual], P[lal], 0.05, 0.04, ual); seg(P[lal], P[hnd], 0.04, 0.03, lal)
        seg(P[hnd], P[hnd] + D[hnd] @ (TAIL[hnd] - HEAD[hnd]), 0.015, 0.012, hnd, 2)
        ull, lll, ft = nm + "UpperLeg", nm + "LowerLeg", nm + "Foot"
        seg(P[ull], P[lll], 0.082, 0.062, ull); seg(P[lll], P[ft], 0.058, 0.042, lll)
        for p in foot_corners(c, sd):
            chk(p, 0.0, ft)
    return tuple(worst)


def gu_verify(cl, hold_thresh=0.012):
    """Per-frame numbers: contact slip of hands/knees/feet, ground penetration, IK reach, frame-0 hips, last vs Idle."""
    cfg = cl["cfg"]; N = cl["frames"]
    prev = None
    slips = []; worst_clear = (9.0, "", 0); reach = 0.0; reach_f = 0
    slip_by = {}
    thr = {"knee": 0.085, "foot": hold_thresh, "hand": 0.045}
    for fr in range(N + 1):
        c = cl["fn"](float(fr))
        pts = gu_points(c, cfg)
        cm = gu_clearance(c, cfg)
        if cm[0] < worst_clear[0]:
            worst_clear = (cm[0], cm[1], fr)
        D, P = solve(c, cfg)
        for sd in "LR":
            nm = SIDE[sd][0]
            A = Vector((c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"]))
            e = (P[nm + "Foot"] - A).length
            tw = Vector((c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"]))
            e2 = (P[nm + "Hand"] - tw).length
            if max(e, e2) > reach:
                reach = max(e, e2); reach_f = fr
        if prev:
            for k, p in pts.items():
                kind = "knee" if "knee" in k else "foot" if "foot" in k else "hand"
                if p.z < thr[kind] and prev[k].z < thr[kind]:
                    d = p - prev[k]
                    v = math.hypot(d.x, d.y) * FPS
                    slips.append(v)
                    key = k[0] + kind
                    if v > slip_by.get(key, (0.0, 0))[0]:
                        slip_by[key] = (v, fr)
        prev = pts
    slips.sort()
    c0 = cl["fn"](0.0); cN = cl["fn"](float(N))
    D0, P0 = solve(c0, cfg)
    ci = idle_pose(0); Di, Pi = solve(ci, {"legs": "ik"})
    Dn, Pn = solve(cN, cfg)
    end_err = max((Pn[n] - Pi[n]).length for n in NAMES)
    return dict(
        slip_max=round(slips[-1], 3) if slips else 0, slip_p90=round(slips[int(len(slips) * 0.9)], 3) if slips else 0,
        slip_mean=round(sum(slips) / len(slips), 3) if slips else 0, contact_samples=len(slips),
        slip_by_part={k: (round(v[0], 3), 'f%d' % v[1]) for k, v in sorted(slip_by.items())},
        min_clearance_m=round(worst_clear[0], 4), min_clearance_part=worst_clear[1], min_clearance_frame=worst_clear[2],
        ik_reach_err_max_m=round(reach, 4), ik_reach_err_frame=reach_f,
        hips0_world=tuple(round(v, 4) for v in P0["Hips"]),
        hips0_euler_xyz_deg=(round(c0["hip_rx"], 2), round(c0["hip_ry"], 2), round(c0["hip_rz"], 2)),
        hips_last_world=tuple(round(v, 4) for v in Pn["Hips"]),
        last_vs_idle_joint_err_m=round(end_err, 4))


def gu_dump(cl, step=3):
    """Debug table: hips, clearance and IK reach error per frame."""
    cfg = cl["cfg"]
    for fr in range(0, cl["frames"] + 1, step):
        c = cl["fn"](float(fr))
        D, P = solve(c, cfg)
        cm = gu_clearance(c, cfg)
        reach = []
        for sd in "LR":
            nm = SIDE[sd][0]
            reach.append(round((P[nm + "Foot"] - Vector((c[sd + "Ax"], c[sd + "Ay"], c[sd + "Az"]))).length, 3))
            reach.append(round((P[nm + "Hand"] - Vector((c[sd + "Wx"], c[sd + "Wy"], c[sd + "Wz"]))).length, 3))
        print("GU %3d t=%.2f hips=(%.2f %.2f %.2f) clr=%.3f %s reach(Lft,Lh,Rft,Rh)=%s" % (
            fr, fr / FPS, P["Hips"].x, P["Hips"].y, P["Hips"].z, cm[0], cm[1], reach))


GU_CAMS = {}


def setup_gu_cams():
    sc = bpy.context.scene
    sc.render.resolution_x = 360; sc.render.resolution_y = 270
    for i in range(-6, 7):
        v, f = box_geo((i * 0.25 - 0.006, -2.6, -0.004), (i * 0.25 + 0.006, 2.6, 0.0))
        PROXY_STATIC.append(_mk("gline", v, f, (0.25, 0.25, 0.28) if i % 4 else (0.3, 0.3, 0.9)))
    for nm, d in (("side", (-1.0, 0.0, 0.0)), ("q34", (-0.62, -0.62, 0.42))):
        cam = bpy.data.cameras.new("gu_" + nm); cam.type = 'ORTHO'; cam.ortho_scale = 2.75
        cam.clip_start = 0.1; cam.clip_end = 40
        co = bpy.data.objects.new("gu_" + nm, cam)
        tgt = Vector((0.0, 0.28, 0.86))
        co.location = tgt + Vector(d).normalized() * 8.0
        co.rotation_euler = (tgt - co.location).to_track_quat('-Z', 'Y').to_euler()
        bpy.context.scene.collection.objects.link(co)
        GU_CAMS[nm] = co


def render_sheet_gu(name, frames):
    import numpy as np
    sc = bpy.context.scene
    tmp = os.path.join(PREV, "_tmp")
    os.makedirs(tmp, exist_ok=True)
    rows = []
    for cn in ("side", "q34"):
        sc.camera = GU_CAMS[cn]
        tiles = []
        for f in frames:
            sc.frame_set(int(round(f)))
            sc.render.filepath = os.path.join(tmp, "t.png")
            bpy.ops.render.render(write_still=True)
            img = bpy.data.images.load(sc.render.filepath)
            arr = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4)
            tiles.append(arr)
            bpy.data.images.remove(img)
        rows.append(np.concatenate(tiles, axis=1))
    sheet = np.concatenate(rows[::-1], axis=0)
    h, w = sheet.shape[:2]
    out = bpy.data.images.new("sheet", w, h, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = os.path.join(PREV, "Anim_%s.png" % name)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    try:
        os.remove(os.path.join(tmp, "t.png")); os.rmdir(tmp)
    except OSError:
        pass


def gu_clip(name, keys, T):
    CLIPS[name] = dict(name=name, frames=T, loop=False, cfg=GU_CFG, fn=lambda f: gu_resolve(keys.eval(f / FPS)),
                       speed=None, gun=None, sheetfn=render_sheet_gu, verify=gu_verify)


# ---------------------------------------------------------------- GetUpBack (supine)
# Head toward +Y (behind the standing character), face up, legs toward -Y. Left hand posts behind-left, the
# right foot is drawn in flat (it stays planted until the end), the body sits up, bridges, the left leg sweeps
# under and back to a toes-tucked knee (hand + knee + foot tripod), then half-kneel, lunge, stand.
def getup_back_keys():
    Y0 = GU_Y0
    HP = (0.34, 0.60, 0.03)                      # posted left hand
    FR = (-0.09, 0.03)                            # front (right) foot at the Idle foot spot
    base = new_ch()
    base["stab"] = 0.0
    k = Keys(base)
    POLE_AR = (-0.8, 0.5, -0.3)
    POLE_AL = (0.8, 0.6, -0.3)

    c = dict(base)
    gu_hips(c, 0.0, Y0, 0.12, rx=-90, ry=3)
    c.update(sp_rx=0, ch_rx=-2, sp_rz=2, nk_rz=5, hd_rz=12, hd_rx=-4)
    gu_hand(c, 'L', HP, (0.6, -0.8, 0), (0, 0, 1), (1.0, 0.3, 0.2), 0.12)
    gu_hand(c, 'R', (-0.31, 0.50, 0.03), (-0.3, -1, 0), (0, 0, 1), (-1.0, 0.2, 0.3), 0.25)
    gu_foot(c, 'L', (0.22, -0.43), fp=84, fr=14, fy=0, pole=(0.1, 0.0, 1.0))
    gu_foot(c, 'R', (-0.20, -0.43), fp=80, fr=-16, fy=0, pole=(-0.2, 0.0, 1.0))
    k.keys.append((0.0, c))
    c = dict(c)
    gu_foot(c, 'R', (-0.15, -0.05, 0.20), fp=40, fr=-8, fy=3, pole=(-0.2, 0.0, 1.0), z=0.20)
    c.update(nk_rx=6)
    k.keys.append((0.12, c))
    c = dict(c)
    gu_foot(c, 'R', (FR[0], FR[1], 0.11), fp=6, fr=0, fy=6, pole=(-0.15, -0.2, 1.0), z=0.11)
    k.keys.append((0.20, c))
    c = dict(c)
    gu_foot(c, 'R', FR, fp=0, fr=0, fy=6, pole=(-0.15, -0.2, 1.0), z=0.085)
    c.update(nk_rx=14, sp_rx=6, ch_rx=10, hd_rz=6, hd_rx=-2)
    gu_hips(c, 0.0, Y0, 0.115, rx=-86, ry=2)
    k.keys.append((0.26, c))
    c = dict(c)
    gu_hips(c, 0.0, Y0, 0.12, rx=-62, ry=0)
    c.update(sp_rx=4, ch_rx=4, nk_rx=14, hd_rx=-2, hd_rz=0)
    arm_rel(c, 'R', (-0.03, -0.40, 0.18), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((0.50, c))
    c = dict(c)
    gu_hips(c, 0.0, Y0, 0.125, rx=-32, ry=0)
    c.update(sp_rx=-4, ch_rx=-4, nk_rx=18, hd_rx=-10)
    arm_rel(c, 'R', (-0.03, -0.46, 0.04), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((0.72, c))
    c = dict(c)
    gu_hips(c, 0.0, Y0 - 0.03, 0.17, rx=-40, ry=0)
    k.keys.append((0.80, c))
    # bridge: hips lift, left foot leaves the ground
    c = dict(c)
    gu_hips(c, 0.0, Y0 - 0.10, 0.28, rx=-50, ry=0)
    c.update(sp_rx=-6, ch_rx=-6, nk_rx=16)
    gu_foot(c, 'L', (0.20, -0.15, 0.24), fp=50, fr=0, fy=0, pole=(0.5, -1.0, 0.6), z=0.24)
    arm_rel(c, 'R', (-0.03, -0.45, -0.02), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((0.92, c))
    # sweep: knee bent and swung outward-up, foot passing low under the hips
    c = dict(c)
    gu_hips(c, 0.0, Y0 - 0.20, 0.32, rx=-52, ry=0)
    gu_foot(c, 'L', (0.17, 0.22, 0.20), fp=10, fr=0, fy=0, pole=(1.0, -0.3, 0.6), z=0.20)
    k.keys.append((1.02, c))
    # landing pose: left knee down behind the front foot, toes tucked. The pose is resolved once (hip height,
    # ankle, knee pole) and keyed explicitly, so the later Kw hold does not move anything.
    c = dict(c)
    gu_hips(c, 0.0, 0.17, 0.30, rx=-52, ry=0)
    ka = 12.0
    zank = KNEE_Z + math.sin(rad(ka)) * L_LL
    c["LKw"] = 1.0; c["LKx"] = 0.20; c["LKy"] = 0.50; c["LKa"] = ka
    gu_foot(c, 'L', (0.20, 0.50 + math.cos(rad(ka)) * L_LL), fp=0, fr=0, fy=0, pole=(0.1, -1.0, 0.0), z=zank)
    c["LFp"] = tuck_fp(c, 'L', zank)
    r = gu_resolve(c)
    for n in ("hip_tz", "LAx", "LAy", "LAz", "LPx", "LPy", "LPz"):
        c[n] = r[n]
    A_L = (c["LAx"], c["LAy"], c["LAz"]); FP_L = c["LFp"]
    c2 = dict(c); c2["LKw"] = 0.0
    c3 = dict(c2)      # hover: the same pose 9 cm higher, so the knee comes down vertically
    c3["hip_tz"] += 0.09; c3["LAz"] += 0.09
    k.keys.append((1.12, c3))
    k.keys.append((1.22, c2))
    k.keys.append((1.26, c))
    # hands move onto the thighs and push; torso comes up
    c = dict(c)
    gu_hips(c, 0.0, 0.26, 0.40, rx=-36, ry=0)
    c.update(sp_rx=-6, ch_rx=-6, nk_rx=8, hd_rx=-6)
    gu_hand(c, 'L', (0.20, 0.30, 0.40), (0, -1, 0), (0, 0, 1), (0.8, 0.8, 0.2), 0.2)
    arm_rel(c, 'R', (-0.08, -0.30, -0.36), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((1.46, c))
    # hand leaves the ground
    c = dict(c)
    gu_hips(c, 0.0, 0.36, 0.45, rx=-14, ry=0)
    c.update(sp_rx=-2, ch_rx=-2, nk_rx=2, hd_rx=-2, stab=0.5)
    arm_rel(c, 'L', (0.16, -0.12, -0.42), (0.3, -1, 0), (0, 0, 1), POLE_AL)
    arm_rel(c, 'R', (-0.08, -0.30, -0.28), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((1.58, c))
    # half-kneel
    c = dict(c)
    gu_hips(c, 0.0, 0.44, 0.52, rx=-3, ry=0)
    c.update(sp_rx=0, ch_rx=0, nk_rx=0, hd_rx=0, stab=0.6)
    arm_rel(c, 'L', (0.12, -0.08, -0.46), (0.3, -1, 0), (0, 0, 1), POLE_AL)
    arm_rel(c, 'R', (-0.08, -0.26, -0.34), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((1.70, c))
    # weight to the front foot, knee leaves the ground, rear toes stay planted
    c = dict(c)
    c["LKw"] = 0.0
    gu_hips(c, 0.0, 0.33, 0.66, rx=8, ry=0)
    c.update(sp_rx=4, ch_rx=4)
    c["LAx"], c["LAy"], c["LAz"] = A_L; c["LFp"] = FP_L
    c["LPx"], c["LPy"], c["LPz"] = 0.05, -1.0, -0.2
    arm_rel(c, 'L', (0.10, -0.12, -0.44), (0.3, -1, 0), (0, 0, 1), POLE_AL)
    arm_rel(c, 'R', (-0.08, -0.22, -0.40), (0, -1, 0), (0, 0, 1), POLE_AR)
    k.keys.append((1.82, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.14, 0.82, rx=8, ry=0)
    c.update(sp_rx=3, ch_rx=3)
    gu_foot(c, 'L', (0.11, 0.52, 0.26), fp=-25, fr=0, fy=3, pole=(0.05, -1.0, 0.0), z=0.26)
    arms_idle(c)
    k.keys.append((1.96, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.05, 0.92, rx=5, ry=0)
    c.update(sp_rx=2, ch_rx=2)
    gu_foot(c, 'L', (0.10, 0.20, 0.13), fp=-6, fr=0, fy=5, pole=(0.05, -1.0, 0.0), z=0.13)
    arms_idle(c)
    k.keys.append((2.10, c))
    c = dict(c)
    gu_foot(c, 'L', (0.09, 0.03, 0.105), fp=-2, fr=0, fy=6, pole=(0.03, -1.0, 0.0), z=0.105)
    gu_hips(c, 0.0, 0.01, 0.94, rx=3, ry=0)
    arms_idle(c)
    k.keys.append((2.16, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.0, 0.945, rx=2, ry=0)
    c.update(sp_rx=1, ch_rx=1)
    gu_foot(c, 'L', (0.09, 0.03), fp=0, fr=0, fy=6, pole=(0.03, -1.0, 0.0), z=0.085)
    arms_idle(c)
    k.keys.append((2.22, c))
    c = dict(c)
    gu_hips(c, 0.0, -0.006, 0.962, rx=-1.5, ry=0)
    c.update(sp_rx=-0.5, ch_rx=-1.0, hd_rx=1.0)
    arms_idle(c)
    k.keys.append((2.38, c))
    k.keys.append((2.55, idle_ik()))
    return k


gu_clip("GetUpBack", getup_back_keys(), 77)


# ---------------------------------------------------------------- GetUpFront (prone)
# Head toward -Y (the character's front), face down, legs toward +Y. Hands re-plant under the shoulders, push-up
# to a plank on the toes, the right foot steps up beside the hands (crouch), the left foot follows, hands come
# off the ground, rise and step the left foot through to the Idle stance.
def getup_front_keys():
    Y0 = GU_Y0
    FR = (-0.09, 0.03)
    HX = 0.21
    HY = -0.01
    base = new_ch()
    base["stab"] = 0.0
    k = Keys(base)
    PL = (0.7, 0.9, 0.9)      # elbows point back and up in the push-up
    ka = 12.0
    zt = KNEE_Z + math.sin(rad(ka)) * L_LL

    def lying_feet(c):
        gu_foot(c, 'L', (0.20, 1.29), fp=24.8, fr=180, fy=180, pole=(0.1, 0.0, 0.3))
        gu_foot(c, 'R', (-0.18, 1.26), fp=24.8, fr=180, fy=180, pole=(-0.1, 0.0, 0.3))

    def tuck(c, sd, x, y, z=0.15):
        gu_foot(c, sd, (x, y), fp=0, fr=0, fy=0, z=z)
        c[sd + "Fp"] = tuck_fp(c, sd, z)

    def hands_planted(c):
        gu_hand(c, 'L', (HX, HY, 0.03), (0, -1, 0), (0, 0, 1), (PL[0], PL[1], PL[2]), 0.1)
        gu_hand(c, 'R', (-HX, HY, 0.03), (0, -1, 0), (0, 0, 1), (-PL[0], PL[1], PL[2]), 0.1)

    c = dict(base)
    gu_hips(c, 0.0, Y0, 0.12, rx=90, ry=-3)
    c.update(sp_rx=0, ch_rx=2, sp_rz=-2, nk_rz=6, hd_rz=20, hd_rx=-4)
    gu_hand(c, 'L', (0.48, -0.28, 0.03), (0.3, -1, 0), (0, 0, 1), (1.0, 0.5, 0.6), 0.25)
    gu_hand(c, 'R', (-0.38, -0.22, 0.03), (-0.3, -1, 0), (0, 0, 1), (-1.0, 0.5, 0.6), 0.25)
    lying_feet(c)
    k.keys.append((0.0, c))
    # hands lift, feet lift (flip to toes)
    c = dict(c)
    gu_hand(c, 'L', (0.24, -0.04, 0.09), (0.1, -1, 0), (0, 0, 1), (PL[0], PL[1], PL[2]), 0.15)
    gu_hand(c, 'R', (-0.24, -0.04, 0.09), (-0.1, -1, 0), (0, 0, 1), (-PL[0], PL[1], PL[2]), 0.15)
    gu_foot(c, 'L', (0.10, 1.26, 0.16), fp=10, fr=90, fy=90, z=0.16)
    gu_foot(c, 'R', (-0.10, 1.26, 0.16), fp=10, fr=90, fy=90, z=0.16)
    c.update(hd_rz=10, hd_rx=-12, nk_rx=-6)
    k.keys.append((0.10, c))
    c = dict(c)
    hands_planted(c)
    tuck(c, 'L', 0.10, 1.26); tuck(c, 'R', -0.10, 1.26)
    c["LAz"] += 0.06; c["RAz"] += 0.06     # hovering, same foot pitch as the landing
    c.update(hd_rz=4, hd_rx=-18, nk_rx=-10)
    k.keys.append((0.22, c))
    c = dict(c)
    tuck(c, 'L', 0.10, 1.26); tuck(c, 'R', -0.10, 1.26)
    LT = {n: c[n] for n in ("LAx", "LAy", "LAz", "LFp", "LFr", "LFy", "LT")}
    RT = {n: c[n] for n in ("RAx", "RAy", "RAz", "RFp", "RFr", "RFy", "RT")}
    c["LPx"], c["LPy"], c["LPz"] = 0.05, -1.0, 0.1
    c["RPx"], c["RPy"], c["RPz"] = -0.05, -1.0, 0.1
    c.update(hd_rz=0, hd_rx=-20, nk_rx=-12)
    k.keys.append((0.30, c))
    # half-way up the push-up
    c = dict(c)
    gu_hips(c, 0.0, Y0, 0.22, rx=78, ry=0)
    k.keys.append((0.55, c))
    # plank
    c = dict(c)
    gu_hips(c, 0.0, Y0, 0.345, rx=65, ry=0)
    c.update(sp_rx=0, ch_rx=0)
    k.keys.append((0.74, c))
    PLANK = dict(c)
    # right foot swings up beside the right hand
    c = dict(c)
    gu_hips(c, 0.0, 0.40, 0.355, rx=66, ry=0)
    gu_foot(c, 'R', (-0.13, 0.55, 0.34), fp=-20, fr=0, fy=8, pole=(-0.8, -0.5, 0.7), z=0.34)
    k.keys.append((0.90, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.39, 0.365, rx=67, ry=0)
    gu_foot(c, 'R', (-0.10, 0.09, 0.17), fp=-8, fr=0, fy=7, pole=(-0.5, -1.0, 0.5), z=0.19)
    k.keys.append((0.96, c))
    # crouch: right foot flat at the Idle front-foot spot
    c = dict(c)
    gu_hips(c, 0.0, 0.38, 0.375, rx=68, ry=0)
    gu_foot(c, 'R', (-0.09, 0.03, 0.105), fp=-3, fr=0, fy=6, pole=(-0.15, -1.0, 0.4), z=0.105)
    k.keys.append((1.04, c))
    c = dict(c)
    gu_foot(c, 'R', FR, fp=0, fr=0, fy=6, pole=(-0.15, -1.0, 0.4), z=0.085)
    k.keys.append((1.09, c))
    # left foot steps in
    c = dict(c)
    gu_hips(c, 0.0, 0.36, 0.385, rx=68, ry=0)
    gu_foot(c, 'L', (0.11, 1.02, 0.27), fp=-30, fr=0, fy=0, pole=(0.3, -1.0, 0.6), z=0.27)
    k.keys.append((1.17, c))
    c = dict(c)
    tuck(c, 'L', 0.10, 0.80, 0.15)
    c["LAz"] += 0.07
    gu_hips(c, 0.0, 0.345, 0.42, rx=72, ry=0)
    k.keys.append((1.25, c))
    c = dict(c)
    tuck(c, 'L', 0.10, 0.80, 0.15)
    c["LPx"], c["LPy"], c["LPz"] = 0.4, -1.0, 1.0
    gu_hips(c, 0.0, 0.34, 0.42, rx=72, ry=0)
    LT2 = {n: c[n] for n in ("LAx", "LAy", "LAz", "LFp", "LFr", "LFy", "LT")}
    k.keys.append((1.32, c))
    # hands leave the ground (onto the thighs), torso comes up
    c = dict(c)
    gu_hips(c, 0.0, 0.27, 0.56, rx=40, ry=0)
    c.update(sp_rx=2, ch_rx=2, nk_rx=-6, hd_rx=-8, stab=0.4)
    gu_hand(c, 'L', (0.19, 0.20, 0.60), (0, -1, 0), (0, 0, 1), (0.8, 0.8, 0.2), 0.25)
    gu_hand(c, 'R', (-0.19, 0.04, 0.50), (0, -1, 0), (0, 0, 1), (-0.8, 0.8, 0.2), 0.25)
    k.keys.append((1.44, c))
    # rising; rear foot starts to swing
    c = dict(c)
    gu_hips(c, 0.0, 0.14, 0.80, rx=14, ry=0)
    c.update(sp_rx=3, ch_rx=3, nk_rx=0, hd_rx=0, stab=0.6)
    gu_foot(c, 'L', (0.11, 0.46, 0.24), fp=-25, fr=0, fy=3, pole=(0.05, -1.0, 0.0), z=0.24)
    arms_idle(c)
    k.keys.append((1.57, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.05, 0.92, rx=5, ry=0)
    c.update(sp_rx=2, ch_rx=2)
    gu_foot(c, 'L', (0.10, 0.19, 0.13), fp=-6, fr=0, fy=5, pole=(0.05, -1.0, 0.0), z=0.13)
    arms_idle(c)
    k.keys.append((1.69, c))
    c = dict(c)
    gu_foot(c, 'L', (0.09, 0.03, 0.105), fp=-2, fr=0, fy=6, pole=(0.03, -1.0, 0.0), z=0.105)
    gu_hips(c, 0.0, 0.01, 0.94, rx=3, ry=0)
    arms_idle(c)
    k.keys.append((1.77, c))
    c = dict(c)
    gu_hips(c, 0.0, 0.0, 0.945, rx=2, ry=0)
    c.update(sp_rx=1, ch_rx=1)
    gu_foot(c, 'L', (0.09, 0.03), fp=0, fr=0, fy=6, pole=(0.03, -1.0, 0.0), z=0.085)
    arms_idle(c)
    k.keys.append((1.83, c))
    c = dict(c)
    gu_hips(c, 0.0, -0.006, 0.962, rx=-1.5, ry=0)
    c.update(sp_rx=-0.5, ch_rx=-1.0, hd_rx=1.0)
    arms_idle(c)
    k.keys.append((1.99, c))
    k.keys.append((2.15, idle_ik()))
    # the rear foot stays planted until 1.50 s, then lifts straight up (no toe drag)
    ce = k.eval(1.50)
    for n_ in LT2:
        ce[n_] = LT2[n_]
    ce["LPx"], ce["LPy"], ce["LPz"] = 0.4, -1.0, 1.0
    k.keys.append((1.50, ce))
    k.keys.sort(key=lambda kk: kk[0])
    return k


gu_clip("GetUpFront", getup_front_keys(), 65)

# CLIPS_END


# ============================================================================ main
if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--out" in argv:
        # optional export folder (FBX + previews/); the default stays Assets/Art/Animations
        _i = argv.index("--out")
        OUT = os.path.abspath(argv[_i + 1])
        PREV = os.path.join(OUT, "previews")
        os.makedirs(PREV, exist_ok=True)
        del argv[_i:_i + 2]
    flags = [a for a in argv if a.startswith("--")]
    names = [a for a in argv if not a.startswith("--")] or list(CLIPS)
    run(names, preview="--nopreview" not in flags, export="--noexport" not in flags)
