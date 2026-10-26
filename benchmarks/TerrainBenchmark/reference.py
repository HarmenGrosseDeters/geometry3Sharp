# Independent reference volumes (numpy) for the volume cases of the benchmark.
#   exact:   both grids on the same nodes -> both TINs (cells split from node (i,j) to (i+1,j+1), as in
#            Grid.BuildMesh) have the same triangles; the difference is linear on each triangle and its
#            positive and negative parts are integrated in closed form.
#   sampled: grids on different nodes (DGM1 1 m vs bDOM50 0.5 m, offset by 0.25 m): both TINs evaluated
#            on a fine sample grid (midpoint rule), restricted to the common footprint.
#   raster:  the plain DEM of difference: sum of (after - before) x cell area over the nodes (what a GIS
#            raster calculator gives), for comparison.
import json, sys
import numpy as np


def load(data, name, win=None):
    m = json.load(open(f"{data}/{name}.json"))
    z = np.fromfile(f"{data}/{name}.f32", dtype="<f4").reshape(m["ny"], m["nx"]).astype(np.float64)
    x0, y0, dx = m["x0"], m["y0"], m["dx"]
    if win:
        # same node selection as Grid.Window
        i0 = max(0, int(np.ceil((win[0] - x0) / dx - 1e-9))); j0 = max(0, int(np.ceil((win[1] - y0) / dx - 1e-9)))
        i1 = min(m["nx"] - 1, int(np.floor((win[2] - x0) / dx + 1e-9))); j1 = min(m["ny"] - 1, int(np.floor((win[3] - y0) / dx + 1e-9)))
        z = z[j0:j1 + 1, i0:i1 + 1]
        x0, y0 = x0 + i0 * dx, y0 + j0 * dx
    return z, x0, y0, dx


def positive_part(s, area):
    """integral and area of max(f, 0) over triangles with vertex values s (n x 3), f linear"""
    s = -np.sort(-s, axis=1)        # descending
    s1, s2, s3 = s[:, 0], s[:, 1], s[:, 2]
    integ = np.zeros(len(s)); ar = np.zeros(len(s))
    with np.errstate(divide="ignore", invalid="ignore"):
        allpos = s3 >= 0
        integ[allpos] = area * (s1 + s2 + s3)[allpos] / 3
        ar[allpos & (s1 > 0)] = area
        one = (s1 > 0) & (s2 <= 0) & ~allpos
        integ[one] = (area * s1 ** 3 / (3 * (s1 - s2) * (s1 - s3)))[one]
        ar[one] = (area * s1 ** 2 / ((s1 - s2) * (s1 - s3)))[one]
        two = (s2 > 0) & (s3 < 0)
        integ[two] = (area * (s1 + s2 + s3) / 3 + area * (-s3) ** 3 / (3 * (s1 - s3) * (s2 - s3)))[two]
        ar[two] = (area - area * s3 ** 2 / ((s1 - s3) * (s2 - s3)))[two]
    return integ.sum(), ar.sum()


def exact(zb, za, dx):
    d = za - zb
    a = 0.5 * dx * dx
    d00, d10, d11, d01 = d[:-1, :-1].ravel(), d[:-1, 1:].ravel(), d[1:, 1:].ravel(), d[1:, :-1].ravel()
    t = np.concatenate([np.stack([d00, d10, d11], 1), np.stack([d00, d11, d01], 1)])
    t = t[np.isfinite(t).all(axis=1)]
    fill, fill_area = positive_part(t, a)
    cut, cut_area = positive_part(-t, a)
    return {"cut": cut, "fill": fill, "net": fill - cut, "cut_area": cut_area, "fill_area": fill_area,
            "overlap_area": len(t) * a}


def tin_height(z, x0, y0, dx, x, y):
    fx = (x - x0) / dx; fy = (y - y0) / dx
    i = np.clip(np.floor(fx).astype(np.int64), 0, z.shape[1] - 2); j = np.clip(np.floor(fy).astype(np.int64), 0, z.shape[0] - 2)
    fx -= i; fy -= j
    z00, z10, z01, z11 = z[j, i], z[j, i + 1], z[j + 1, i], z[j + 1, i + 1]
    return np.where(fx >= fy, z00 + fx * (z10 - z00) + fy * (z11 - z10), z00 + fy * (z01 - z00) + fx * (z11 - z01))


def sampled(b, a, h):
    (zb, xb, yb, db), (za, xa, ya, da) = b, a
    x0 = max(xb, xa); y0 = max(yb, ya)
    x1 = min(xb + db * (zb.shape[1] - 1), xa + da * (za.shape[1] - 1)); y1 = min(yb + db * (zb.shape[0] - 1), ya + da * (za.shape[0] - 1))
    nx = int(round((x1 - x0) / h)); ny = int(round((y1 - y0) / h))
    hx, hy = (x1 - x0) / nx, (y1 - y0) / ny
    xs = x0 + hx * (np.arange(nx) + 0.5)
    cut = fill = cut_area = fill_area = 0.0
    for j in range(ny):
        y = np.full(nx, y0 + hy * (j + 0.5))
        d = tin_height(za, xa, ya, da, xs, y) - tin_height(zb, xb, yb, db, xs, y)
        fill += d[d > 0].sum(); cut -= d[d < 0].sum()
        fill_area += (d > 0).sum(); cut_area += (d < 0).sum()
    c = hx * hy
    return {"cut": cut * c, "fill": fill * c, "net": (fill - cut) * c, "cut_area": cut_area * c, "fill_area": fill_area * c,
            "overlap_area": (x1 - x0) * (y1 - y0), "h": h}


def raster(zb, za, dx):
    d = za - zb
    return {"net": np.nansum(d) * dx * dx, "cut": -np.nansum(d[d < 0]) * dx * dx, "fill": np.nansum(d[d > 0]) * dx * dx}


def reference(data, before, after, win, h=0.125):
    b = load(data, before, win); a = load(data, after, win)
    out = {}
    if b[1:] == a[1:] and b[0].shape == a[0].shape:
        out["exact"] = exact(b[0], a[0], b[3])
        out["raster"] = raster(b[0], a[0], b[3])
    else:
        out["sampled"] = sampled(b, a, h)
    return out


if __name__ == "__main__":
    # reference.py <data dir> <before> <after> x0,y0,x1,y1 [h]
    win = [float(v) for v in sys.argv[4].split(",")]
    print(json.dumps(reference(sys.argv[1], sys.argv[2], sys.argv[3], win, *(float(v) for v in sys.argv[5:]))))
