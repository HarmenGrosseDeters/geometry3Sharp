# stlgeo.py <stl>...: surface area / diag^2, components (shared vertex positions), bbox diag of all vs the largest component
import struct, sys, math, json
def read(fn):
    b = open(fn, 'rb').read()
    tris = []
    if len(b) >= 84:
        n = struct.unpack('<I', b[80:84])[0]
        if 84 + 50 * n == len(b):
            for i in range(n):
                v = struct.unpack('<12f', b[84 + 50*i: 84 + 50*i + 48])
                tris.append((v[3:6], v[6:9], v[9:12]))
            return tris
    pts = []
    for line in b.decode('latin1').splitlines():
        s = line.split()
        if s and s[0] == 'vertex': pts.append(tuple(map(float, s[1:4])))
    return [tuple(pts[3*i:3*i+3]) for i in range(len(pts)//3)]
def diag(ps):
    mn = [min(p[k] for p in ps) for k in range(3)]; mx = [max(p[k] for p in ps) for k in range(3)]
    return math.dist(mn, mx)
for fn in sys.argv[1:]:
    T = read(fn)
    idx = {}; par = []
    def f(x):
        while par[x] != x: par[x] = par[par[x]]; x = par[x]
        return x
    tv = []
    for t in T:
        ids = []
        for p in t:
            if p not in idx: idx[p] = len(par); par.append(len(par))
            ids.append(idx[p])
        tv.append(ids)
        a, b2, c = (f(i) for i in ids); par[b2] = a; par[f(c)] = a
    area = 0
    for t in T:
        u = [t[1][k]-t[0][k] for k in range(3)]; v = [t[2][k]-t[0][k] for k in range(3)]
        cx = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]); area += 0.5*math.sqrt(sum(x*x for x in cx))
    comp = {}
    for i, t in enumerate(tv): comp.setdefault(f(t[0]), []).append(i)
    big = max(comp.values(), key=len)
    allp = [p for t in T for p in t]; bigp = [p for i in big for p in T[i]]
    D = diag(allp); Db = diag(bigp)
    print(json.dumps({"file": fn.split('/')[-1], "tris": len(T), "components": len(comp), "area_over_diag2": area / D**2, "diag_all_over_largest": D / Db, "largest_share": len(big)/len(T)}))
