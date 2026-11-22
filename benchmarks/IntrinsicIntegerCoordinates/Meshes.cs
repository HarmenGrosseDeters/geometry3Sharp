// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Procedural adversarial meshes for the intrinsic triangulation study (no mesh files).
using System;
using System.Collections.Generic;
using g3;

static class Meshes
{
    public class Case
    {
        public string Name;
        public Func<DMesh3> Make;
    }

    public static List<Case> All()
    {
        var l = new List<Case>();
        Action<string, Func<DMesh3>> add = (n, f) => l.Add(new Case() { Name = n, Make = f });
        // rows shifted by half a column, dy apart: every triangle has an angle near 180 degrees
        foreach (double dy in new[] { 0.05, 0.005, 0.0005 })
            add($"obtuse plane dy={dy:G}", () => ObtusePlane(12, 120, 1.0, dy));
        add("ring sphere 80x16", () => RingSphere(1, 80, 16, 0, 0));
        add("ring sphere 160x24", () => RingSphere(1, 160, 24, 0, 0));
        // every other vertex pushed outwards: cone vertices of both signs
        add("spiky ring sphere", () => RingSphere(1, 60, 16, 0.3, 0));
        // vertices of each ring moved along the ring: needles between nearly coincident vertices
        foreach (double j in new[] { 0.3, 0.49, 0.499999 })
            add($"clustered ring sphere j={j:G}", () => RingSphere(1, 40, 24, 0, j));
        // open cylinder / closed torus, ring k joined to ring k+1 turned by 'shear' vertices
        foreach (int s in new[] { 3, 9, 15 })
            add($"sheared cylinder s={s}", () => Sheared(40, 32, s, false));
        foreach (int s in new[] { 3, 9, 15 })
            add($"sheared torus s={s}", () => Sheared(40, 32, s, true));
        // grid stretched in x (rectangles: no flips, the identity triangulation), and also sheared (flips)
        foreach (double a in new[] { 1e2, 1e4, 1e6 })
            add($"stretched grid a={a:G}", () => StretchedGrid(30, a, 0, 0));
        foreach (double a in new[] { 1e2, 1e4, 1e6 })
            add($"sheared stretched grid a={a:G}", () => StretchedGrid(30, a, 0, 0.37));
        // unit grid, cocircular quads, vertices moved by a few ulps: near ties of the Delaunay test
        add("grid with ties (1e-13 noise)", () => StretchedGrid(40, 1, 1e-13, 0));
        // a vertex at distance eps from the middle of every cell diagonal: caps (180 degree angles)
        foreach (double e in new[] { 1e-3, 1e-6, 1e-9, 1e-12 })
            add($"cap grid eps={e:G}", () => CapGrid(24, e));
        // pleated strip (accordion): flat intrinsically, 3D folds of nearly 180 degrees
        add("accordion", () => Accordion(60, 20, 0.01));
        return l;
    }

    /// <summary> rotation, scale and translation applied to a copy of the mesh </summary>
    public static DMesh3 Transformed(DMesh3 m, int k)
    {
        var r = new DMesh3(m);
        if (k == 0) return r;
        Quaterniond q = new Quaterniond(new Vector3d(0.3, -0.5, 0.81).Normalized, 37.3);
        double s = k == 2 ? Math.Pow(2, -30) : 1;
        Vector3d t = k == 3 ? new Vector3d(1e6, -2e6, 3e6) : Vector3d.Zero;
        foreach (int v in r.VertexIndices()) {
            Vector3d p = r.GetVertex(v);
            if (k != 2) p = q * p;
            r.SetVertex(v, s * p + t);
        }
        return r;
    }
    public static readonly string[] TransformNames = { "identity", "rotated", "scaled 2^-30", "rotated + offset 1e6" };

    public static DMesh3 ObtusePlane(int nx, int ny, double h, double dy)
    {
        DMesh3 mesh = new DMesh3();
        var rows = new List<int[]>();
        for (int j = 0; j <= ny; ++j) {
            bool odd = (j % 2) == 1;
            int count = odd ? nx : nx + 1;
            int[] row = new int[count];
            for (int i = 0; i < count; ++i)
                row[i] = mesh.AppendVertex(new Vector3d(i * h + (odd ? 0.5 * h : 0), j * dy, 0));
            rows.Add(row);
        }
        for (int j = 0; j < ny; ++j) {
            int[] A = rows[j], B = rows[j + 1];
            if (j % 2 == 0) {
                for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i]);
                for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i + 1], B[i + 1], B[i]);
            } else {
                for (int i = 0; i < nx; ++i) mesh.AppendTriangle(A[i], B[i + 1], B[i]);
                for (int i = 0; i + 1 < nx; ++i) mesh.AppendTriangle(A[i], A[i + 1], B[i + 1]);
            }
        }
        return mesh;
    }

    // latitude rings, alternately shifted by half a step; spike: every other vertex scaled by 1 + spike;
    // jitter: every other vertex of a ring moved by jitter steps towards its neighbour
    public static DMesh3 RingSphere(double R, int rings, int perRing, double spike, double jitter)
    {
        DMesh3 mesh = new DMesh3();
        int north = mesh.AppendVertex(new Vector3d(0, 0, R));
        var ring = new List<int[]>();
        for (int k = 1; k < rings; ++k) {
            double th = Math.PI * k / rings;
            int[] r = new int[perRing];
            for (int i = 0; i < perRing; ++i) {
                double ph = 2 * Math.PI * (i + ((k % 2 == 1) ? 0.5 : 0) + ((i % 2 == 1) ? jitter : -jitter)) / perRing;
                double rr = R * (((i + k) % 2 == 0) ? 1 + spike : 1);
                r[i] = mesh.AppendVertex(new Vector3d(rr * Math.Sin(th) * Math.Cos(ph), rr * Math.Sin(th) * Math.Sin(ph), rr * Math.Cos(th)));
            }
            ring.Add(r);
        }
        int south = mesh.AppendVertex(new Vector3d(0, 0, -R));
        Action<int, int, int> add = (a, b, c) => {
            Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b), pc = mesh.GetVertex(c);
            if ((pb - pa).Cross(pc - pa).Dot(pa + pb + pc) < 0) { int t = b; b = c; c = t; }
            mesh.AppendTriangle(a, b, c);
        };
        for (int i = 0; i < perRing; ++i) {
            add(north, ring[0][i], ring[0][(i + 1) % perRing]);
            add(south, ring[rings - 2][i], ring[rings - 2][(i + 1) % perRing]);
        }
        for (int k = 0; k + 1 < ring.Count; ++k) {
            int[] A = ring[k], B = ring[k + 1];
            bool shifted = ((k + 2) % 2 == 1);
            for (int i = 0; i < perRing; ++i) {
                int i1 = (i + 1) % perRing;
                if (shifted) { add(A[i], A[i1], B[i]); add(A[i1], B[i1], B[i]); }
                else { add(B[i], B[i1], A[i]); add(B[i1], A[i1], A[i]); }
            }
        }
        return mesh;
    }

    // rings of 'per' vertices; ring k is joined to ring k+1 turned by 'shear' vertices. torus: closed in both
    // directions (major radius 3, minor 1), else a cylinder of radius 1 and height 4
    public static DMesh3 Sheared(int rings, int per, int shear, bool torus)
    {
        DMesh3 mesh = new DMesh3();
        int[,] vid = new int[rings, per];
        for (int k = 0; k < rings; ++k)
            for (int i = 0; i < per; ++i) {
                double ph = 2 * Math.PI * i / per;
                if (torus) {
                    double th = 2 * Math.PI * k / rings;
                    double rr = 3 + Math.Cos(ph);
                    vid[k, i] = mesh.AppendVertex(new Vector3d(rr * Math.Cos(th), rr * Math.Sin(th), Math.Sin(ph)));
                } else
                    vid[k, i] = mesh.AppendVertex(new Vector3d(Math.Cos(ph), Math.Sin(ph), 4.0 * k / (rings - 1)));
            }
        int last = torus ? rings : rings - 1;
        for (int k = 0; k < last; ++k) {
            int k1 = (k + 1) % rings;
            for (int i = 0; i < per; ++i) {
                int i1 = (i + 1) % per, j = (i + shear) % per, j1 = (i + shear + 1) % per;
                if (torus) {
                    mesh.AppendTriangle(vid[k, i], vid[k1, j], vid[k, i1]);
                    mesh.AppendTriangle(vid[k, i1], vid[k1, j], vid[k1, j1]);
                } else {
                    mesh.AppendTriangle(vid[k, i], vid[k, i1], vid[k1, j]);
                    mesh.AppendTriangle(vid[k, i1], vid[k1, j1], vid[k1, j]);
                }
            }
        }
        return mesh;
    }

    // n x n unit grid, x scaled by a, every row moved by shear * a against the one below; noise: each coordinate
    // changed by a deterministic pseudo-random amount up to noise
    public static DMesh3 StretchedGrid(int n, double a, double noise, double shear)
    {
        DMesh3 mesh = new DMesh3();
        var rng = new Random(12345);
        int[,] vid = new int[n + 1, n + 1];
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i)
                vid[i, j] = mesh.AppendVertex(new Vector3d(a * (i + shear * j) + noise * (2 * rng.NextDouble() - 1), j + noise * (2 * rng.NextDouble() - 1), 0));
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
            }
        return mesh;
    }

    // n x n unit grid; every cell has a fifth vertex at distance eps from the middle of its diagonal, joined so
    // that one triangle is a cap (a, c, m) with an angle of nearly 180 degrees at m
    public static DMesh3 CapGrid(int n, double eps)
    {
        DMesh3 mesh = new DMesh3();
        int[,] vid = new int[n + 1, n + 1];
        for (int j = 0; j <= n; ++j)
            for (int i = 0; i <= n; ++i)
                vid[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
        double o = eps / Math.Sqrt(2.0);
        for (int j = 0; j < n; ++j)
            for (int i = 0; i < n; ++i) {
                int a = vid[i, j], b = vid[i + 1, j], c = vid[i + 1, j + 1], d = vid[i, j + 1];
                int m = mesh.AppendVertex(new Vector3d(i + 0.5 + o, j + 0.5 - o, 0));     // towards b
                mesh.AppendTriangle(a, c, d);
                mesh.AppendTriangle(a, m, c);       // the cap
                mesh.AppendTriangle(a, b, m);
                mesh.AppendTriangle(b, c, m);
            }
        return mesh;
    }

    // strip of nx x ny cells folded along every column line, alternately up and down, to a height h of
    // nearly zero: developable (intrinsically a flat grid with alternating diagonals)
    public static DMesh3 Accordion(int nx, int ny, double gap)
    {
        DMesh3 mesh = new DMesh3();
        int[,] vid = new int[nx + 1, ny + 1];
        double dx = gap, dz = Math.Sqrt(1 - gap * gap);
        for (int j = 0; j <= ny; ++j)
            for (int i = 0; i <= nx; ++i)
                vid[i, j] = mesh.AppendVertex(new Vector3d(i * dx, j, (i % 2) * dz));
        for (int j = 0; j < ny; ++j)
            for (int i = 0; i < nx; ++i) {
                if ((i + j) % 2 == 0) {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                } else {
                    mesh.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i, j + 1]);
                    mesh.AppendTriangle(vid[i + 1, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            }
        return mesh;
    }
}
