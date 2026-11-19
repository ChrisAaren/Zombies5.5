"""Analytic clothed-courier body as a smooth-union signed distance field (numpy).

The skin-modifier cage supplies quad topology; every vertex is then projected onto this exact shape.
All dims in meters, Blender coords (z up, character faces -Y, left = +X).
"""
import numpy as np

EPS = 0.0025


def _pchip(xs, ys):
    xs = np.asarray(xs, float); ys = np.asarray(ys, float)
    h = np.diff(xs); d = np.diff(ys) / h
    m = np.zeros(len(xs))
    m[0] = d[0]; m[-1] = d[-1]
    for i in range(1, len(xs) - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0
        else:
            w1 = 2 * h[i] + h[i - 1]; w2 = h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    def f(x):
        x = np.clip(np.asarray(x, float), xs[0], xs[-1])
        i = np.clip(np.searchsorted(xs, x) - 1, 0, len(xs) - 2)
        t = (x - xs[i]) / h[i]
        return ((2 * t**3 - 3 * t**2 + 1) * ys[i] + (t**3 - 2 * t**2 + t) * h[i] * m[i]
                + (-2 * t**3 + 3 * t**2) * ys[i + 1] + (t**3 - t**2) * h[i] * m[i + 1])
    return f


class Chain:
    """Elliptical-section capsule chain. knots: [(x, y, z, r1, r2)]; r1 along `hint`, r2 along the other axis."""

    def __init__(self, name, knots, hint, step=0.012):
        self.name = name
        k = np.array(knots, float)
        seg = np.linalg.norm(np.diff(k[:, :3], axis=0), axis=1)
        s = np.concatenate([[0], np.cumsum(seg)])
        self.length = s[-1]
        self.s_knots = s
        n = max(2, int(np.ceil(s[-1] / step)))
        ss = np.linspace(0, s[-1], n + 1)
        P = np.stack([_pchip(s, k[:, i])(ss) for i in range(3)], 1)
        R1 = _pchip(s, k[:, 3])(ss); R2 = _pchip(s, k[:, 4])(ss)
        self.A = P[:-1]; self.B = P[1:]
        ab = self.B - self.A
        self.L = np.linalg.norm(ab, axis=1)
        self.a = ab / self.L[:, None]
        h = np.asarray(hint, float)
        u = h[None, :] - (self.a @ h)[:, None] * self.a
        self.u = u / np.linalg.norm(u, axis=1)[:, None]
        self.v = np.cross(self.a, self.u)
        self.r1a, self.r1b, self.r2a, self.r2b = R1[:-1], R1[1:], R2[:-1], R2[1:]
        rr = max(R1.max(), R2.max()) * 1.3 + 0.1
        self.lo = P.min(0) - rr
        self.hi = P.max(0) + rr
        self.knots = k
        self._f = [_pchip(s, k[:, i]) for i in range(5)]

    def at(self, s):
        return np.array([f(s) for f in self._f]).T

    def sdf(self, P):
        out = np.full(len(P), 1e3)
        m = np.all((P > self.lo) & (P < self.hi), axis=1)
        if not m.any():
            return out
        Q = P[m]
        d = Q[:, None, :] - self.A[None, :, :]                  # N,M,3
        t = np.clip(np.einsum('nmk,mk->nm', d, self.a) / self.L[None, :], 0, 1)
        c = d - t[:, :, None] * (self.L[None, :, None] * self.a[None, :, :])
        ax = np.einsum('nmk,mk->nm', c, self.a)
        du = np.einsum('nmk,mk->nm', c, self.u)
        dv = np.einsum('nmk,mk->nm', c, self.v)
        r1 = self.r1a[None, :] + t * (self.r1b - self.r1a)[None, :]
        r2 = self.r2a[None, :] + t * (self.r2b - self.r2a)[None, :]
        rm = np.minimum(r1, r2)
        q = np.sqrt((du / r1) ** 2 + (dv / r2) ** 2 + (ax / rm) ** 2)
        out[m] = ((q - 1.0) * rm).min(axis=1)
        return out


class Ellipsoid:
    def __init__(self, name, c, r):
        self.name = name; self.c = np.array(c, float); self.r = np.array(r, float)

    def sdf(self, P):
        q = np.linalg.norm((P - self.c) / self.r, axis=1)
        return (q - 1.0) * self.r.min()


def smin(a, b, k):
    if k <= 0:
        return np.minimum(a, b)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def smax(a, b, k):
    return -smin(-a, -b, k)


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)

# ------------------------------------------------------------------ shape tables
TORSO = [  # z-ordered, hint X: r1 = half-width, r2 = half-depth (jacket volumes)
    (0, .010, .955, .166, .110),
    (0, .010, 1.005, .176, .114),     # jacket hem
    (0, .011, 1.10, .156, .104),      # waist
    (0, .013, 1.225, .172, .110),
    (0, .016, 1.335, .196, .127),     # chest
    (0, .016, 1.405, .186, .118),
    (0, .016, 1.452, .150, .094),
    (0, .014, 1.476, .094, .064),
]
NECK = [(0, .012, 1.470, .070, .072), (0, .012, 1.520, .056, .058), (0, .010, 1.575, .050, .054),
        (0, .006, 1.620, .050, .056)]
SKULL = ((0, .010, 1.705), (.078, .102, .094))
JAW = ((0, -.020, 1.642), (.060, .068, .052))


def arm_knots(sx):
    # r1 = vertical (z) half, r2 = depth (y) half; hint Z
    k = [(.060, .018, 1.440, .074, .072),
         (.130, .018, 1.442, .070, .070),
         (.190, .020, 1.442, .066, .066),    # deltoid
         (.270, .020, 1.442, .058, .056),
         (.365, .020, 1.442, .053, .051),
         (.450, .020, 1.442, .049, .048),    # elbow
         (.520, .016, 1.442, .047, .046),
         (.610, .012, 1.442, .042, .043),
         (.676, .010, 1.442, .0415, .0425),  # sleeve end / cuff
         (.696, .010, 1.442, .0415, .0425),
         (.704, .010, 1.442, .0345, .0355),  # cuff lip
         (.716, .010, 1.442, .0285, .033),   # wrist
         (.745, .006, 1.4425, .0195, .042),  # palm
         (.775, -.0, 1.4425, .0150, .0435),
         (.800, -.0, 1.4425, .0125, .0430),
         (.826, .0, 1.4425, .0085, .0380)]
    return [(sx * p[0],) + p[1:] for p in k]


FINGER_DEF = {  # joints (x, y, z): MCP, PIP, DIP, tip bone end, tip; base radius. y slightly splayed vs the bones
    "index": ([(.793, -.037, 1.442), (.835, -.038, 1.442), (.863, -.039, 1.442), (.885, -.039, 1.442),
               (.900, -.039, 1.4415)], .0092),
    "middle": ([(.793, -.0125, 1.443), (.842, -.0125, 1.443), (.874, -.0125, 1.443), (.898, -.0125, 1.443),
                (.9135, -.0125, 1.4425)], .0094),
    "ring": ([(.793, .0105, 1.442), (.835, .0115, 1.442), (.865, .0125, 1.442), (.887, .0125, 1.442),
              (.900, .0125, 1.4415)], .0088),
    "little": ([(.793, .0300, 1.440), (.819, .0320, 1.440), (.841, .0335, 1.440), (.860, .0340, 1.440),
                (.871, .0340, 1.4395)], .0078),
}
THUMB = [(.735, -.030, 1.434), (.757, -.058, 1.431), (.782, -.078, 1.4245), (.803, -.094, 1.4195), (.817, -.104, 1.4170)]


def finger_knots(sx):
    out = {}
    for n, (pts, r) in FINGER_DEF.items():
        kn = []
        for i, p in enumerate(pts):
            rr = r * (1.0 - 0.07 * i) * (0.72 / 0.72 if i == len(pts) - 1 else 1.0)
            kn.append((sx * p[0], p[1], p[2], rr, rr * 1.04))
        out[n] = kn
    kn = []
    for i, p in enumerate(THUMB):
        rr = (.0135 - .0014 * i) * (0.72 if i == 4 else 1)
        kn.append((sx * p[0], p[1], p[2], rr, rr))
    out["thumb"] = kn
    return out


def leg_knots(sx):
    # r1 = half-width (x), r2 = half-depth (y); hint X ; jeans
    k = [(.100, .004, .965, .090, .102),
         (.100, .000, .84, .086, .098),
         (.098, -.004, .70, .074, .082),
         (.097, -.008, .52, .0615, .068),      # knee
         (.096, -.004, .39, .0605, .066),      # calf
         (.096, .006, .25, .0505, .056),
         (.096, .014, .140, .0470, .0505),
         (.096, .018, .098, .0515, .0535),     # bunched hem
         (.096, .019, .084, .0475, .050)]
    return [(sx * p[0],) + p[1:] for p in k]


def shoe_knots(sx):
    # axis along -Y; hint X: r1 = half-width, r2 = half-height (sneaker: tall heel collar, lower toe box)
    k = [(.096, .082, .052, .031, .047),
         (.096, .055, .058, .037, .056),
         (.096, .012, .052, .041, .052),
         (.096, -.040, .046, .047, .046),
         (.096, -.100, .040, .0515, .0400),
         (.096, -.160, .034, .046, .0335),
         (.096, -.210, .031, .032, .030)]
    return [(sx * p[0],) + p[1:] for p in k]


class Body:
    def __init__(self):
        self.torso = Chain("torso", TORSO, X, .015)
        self.neck = Chain("neck", NECK, X, .012)
        self.skull = Ellipsoid("skull", *SKULL)
        self.jaw = Ellipsoid("jaw", *JAW)
        self.arms = [Chain("arm%d" % s, arm_knots(s), Z, .012) for s in (1, -1)]
        self.legs = [Chain("leg%d" % s, leg_knots(s), X, .015) for s in (1, -1)]
        self.shoes = [Chain("shoe%d" % s, shoe_knots(s), X, .012) for s in (1, -1)]
        self.fingers = []
        self.thumbs = []
        for s in (1, -1):
            for n, kn in finger_knots(s).items():
                (self.thumbs if n == "thumb" else self.fingers).append(Chain("f%d%s" % (s, n), kn, Z, .005))
        self.thenar = [Ellipsoid("thenar%d" % s, (s * .750, -.031, 1.430), (.036, .024, .0175)) for s in (1, -1)]

    def sdf(self, P):
        P = np.asarray(P, float)
        T = self.torso.sdf(P)
        T = smin(T, self.neck.sdf(P), .03)
        H = smin(self.skull.sdf(P), self.jaw.sdf(P), .045)
        T = smin(T, H, .035)
        for a in self.arms:
            T = smin(T, a.sdf(P), .045)
        for l in self.legs:
            T = smin(T, l.sdf(P), .05)
        for s in self.shoes:
            T = smin(T, s.sdf(P), .014)
        for t in self.thenar:
            T = smin(T, t.sdf(P), .02)
        for t in self.thumbs:
            T = smin(T, t.sdf(P), .022)
        F = np.full(len(P), 1e3)
        for f in self.fingers:
            F = np.minimum(F, f.sdf(P))
        D = smin(T, F, .009)
        D = smax(D, -P[:, 2], .006)       # flat sole
        return D

    def project(self, P, iters=6, maxstep=0.04):
        P = np.array(P, float)
        for it in range(iters):
            d = self.sdf(P)
            g = np.zeros_like(P)
            for k in range(3):
                Q = P.copy(); Q[:, k] += EPS
                g[:, k] = (self.sdf(Q) - d) / EPS
            n2 = np.maximum((g * g).sum(1), 1e-8)
            step = g * (d / n2)[:, None]
            L = np.linalg.norm(step, axis=1)
            sc = np.minimum(1.0, maxstep / np.maximum(L, 1e-9))
            P = P - step * sc[:, None]
        return P


def relax_project(body, P, nbrs, mask, rounds=4, lam=0.5, iters=3):
    """Tangential relaxation for the masked vertices: average neighbours, then re-project onto the surface."""
    P = np.array(P, float)
    idx = np.nonzero(mask)[0]
    for r in range(rounds):
        Q = P.copy()
        for i in idx:
            nb = nbrs[i]
            Q[i] = P[i] + lam * (P[nb].mean(0) - P[i])
        P[idx] = body.project(Q[idx], iters=iters, maxstep=0.02)
    return P
