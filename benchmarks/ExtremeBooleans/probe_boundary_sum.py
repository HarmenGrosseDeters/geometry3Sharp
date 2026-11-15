# Probe of lever O2 of docs/perf/EXTREME_RUNTIME_BOOLEANS.md: a hierarchical evaluation of the Antipodal boundary sum.
# The open winding number at p is chi(x0) + (1/4 pi) sum over the boundary segments (a, b) of
#     2 atan2(A . (B x x1), 1 + A.B + A.x1 + B.x1),  A, B the unit vectors from p to a, b, x1 = -x0
# (EXACT_BOOLEAN_30_PRESTUDY.md section 5.1); today every evaluation sums all B segments. Here the segments are ordered
# into chains (head to tail), a binary tree is built over each chain, and a cluster (a_0 .. a_k) whose bounding sphere
# is far from p (radius / distance < theta) is replaced by the term of its chord (a_0, a_k) plus the dipole
# approximation of the solid angle of the closed polygon a_0 .. a_k (its vector area dotted with (c - p) / |c - p|^3,
# Barill et al. 2018, first order), if also the ray x1 misses the cluster's cone (otherwise the chord identity is off by a
# whole turn, the integer part being the ray's). Reports, over sampled query points, the mean number of terms per evaluation
# (exact: B), the largest and 99th-percentile error of the sum against the exact one modulo whole turns, in
# winding-number units, and the share of evaluations off by a whole turn (the integer bookkeeping of the chord identity,
# which this probe does not settle: a design item, see the doc).
#   python3 -I probe_boundary_sum.py <mesh.off> <queries.off> [samples] [theta...]
# The queries are the centroids of sampled triangles of <queries.off> (eg the rotated copy B of a case), the ray
# directions random; the boundary is that of <mesh.off> (vertices welded by exact position).
import sys, numpy as np
def read_off(path):
    L = open(path).read().split("\n"); nv, nt = map(int, L[1].split()[:2])
    V = np.array([[float(x) for x in L[2 + i].split()[:3]] for i in range(nv)])
    T = np.array([[int(x) for x in L[2 + nv + i].split()[1:4]] for i in range(nt)])
    return V, T
V, T = read_off(sys.argv[1]); Q, QT = read_off(sys.argv[2])
samples = int(sys.argv[3]) if len(sys.argv) > 3 else 400
thetas = [float(x) for x in sys.argv[4:]] or [0.25, 0.5]
# weld by exact position
_, inv = np.unique(V, axis=0, return_inverse=True); inv = inv.ravel(); W = np.unique(V, axis=0)
T = inv[T]
from collections import Counter, defaultdict
cnt = Counter()
for a, b, c in T:
    for u, v in ((a, b), (b, c), (c, a)):
        if u != v: cnt[(u, v)] += 1
segs = []
for (u, v), k in cnt.items():
    d = k - cnt.get((v, u), 0)
    segs += [(u, v)] * max(d, 0)
B = len(segs)
# chains: follow head to tail
out = defaultdict(list)
for i, (u, v) in enumerate(segs): out[u].append(i)
used = np.zeros(B, bool); chains = []
for i in range(B):
    if used[i]: continue
    ch = [i]; used[i] = True; v = segs[i][1]
    while True:
        nxt = [j for j in out[v] if not used[j]]
        if not nxt: break
        j = nxt[0]; used[j] = True; ch.append(j); v = segs[j][1]
    chains.append(ch)
print("boundary segments", B, "chains", len(chains), "longest", max(len(c) for c in chains))
P = W[np.array([s[0] for s in segs])]; R = W[np.array([s[1] for s in segs])]
def term(p, x1, a, b):
    A = a - p; Bv = b - p
    A /= np.linalg.norm(A, axis=-1, keepdims=True); Bv /= np.linalg.norm(Bv, axis=-1, keepdims=True)
    num = np.einsum("...i,...i", A, np.cross(Bv, x1)); den = 1 + np.einsum("...i,...i", A, Bv) + A @ x1 + Bv @ x1
    return 2 * np.arctan2(num, den)
# tree nodes over each chain: (start, end) ranges of the chain's segment list
nodes = []   # (chain id, lo, hi, center, radius, a0, ak, vector area)
def build(ci, lo, hi):
    idx = chains[ci][lo:hi]; pts = np.vstack([P[idx], R[idx][-1:]])
    c = pts.mean(0); r = np.max(np.linalg.norm(pts - c, axis=1))
    poly = pts; va = 0.5 * np.sum(np.cross(poly - c, np.roll(poly, -1, axis=0) - c), axis=0)
    me = len(nodes); nodes.append([ci, lo, hi, c, r, pts[0], pts[-1], va, -1, -1])
    if hi - lo > 1:
        m = (lo + hi) // 2; l = build(ci, lo, m); rr = build(ci, m, hi); nodes[me][8] = l; nodes[me][9] = rr
    return me
roots = [build(ci, 0, len(c)) for ci, c in enumerate(chains)]
rng = np.random.default_rng(1)
qi = rng.choice(len(QT), size=min(samples, len(QT)), replace=False)
for theta in thetas:
    terms, errs = [], []
    for q in qi:
        p = Q[QT[q]].mean(0); x0 = rng.normal(size=3); x0 /= np.linalg.norm(x0); x1 = -x0
        exact = term(p, x1, P.copy(), R.copy()).sum() / (4 * np.pi)
        s, n, stack = 0.0, 0, list(roots)
        while stack:
            k = stack.pop(); ci, lo, hi, c, r, a0, ak, va, l, rr = nodes[k]
            d = np.linalg.norm(c - p)
            if hi - lo == 1:
                s += term(p, x1, P[chains[ci][lo]][None].copy(), R[chains[ci][lo]][None].copy())[0]; n += 1
            elif r < theta * d and np.dot(x1, (c - p) / d) < np.cos(np.arcsin(min(1.0, r / d)) + 1e-3):
                # the chord term plus the dipole of the closed polygon (sign: the fan from x1 over the chain minus the chord)
                s += term(p, x1, a0[None].copy(), ak[None].copy())[0] + np.dot(va, c - p) / d ** 3; n += 1
            else:
                stack += [l, rr]
        terms.append(n); errs.append(s / (4 * np.pi) - exact)
    raw = np.abs(np.array(errs)); wraps = np.mean(raw > 0.5)
    errs = np.abs((np.array(errs) + 0.5) % 1.0 - 0.5)       # modulo whole turns (the integer part is the ray's)
    print("theta %.2f: terms per evaluation %.0f of %d (%.1f %%); error modulo whole turns max %.2e, p99 %.2e, share > 1e-3: %.1f %%; whole-turn jumps %.1f %%"
          % (theta, np.mean(terms), B, 100 * np.mean(terms) / B, errs.max(), np.percentile(errs, 99), 100 * np.mean(errs > 1e-3), 100 * wraps))
