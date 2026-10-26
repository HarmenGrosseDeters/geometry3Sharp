// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Intrinsic Delaunay refinement: accuracy of the heat method distance and the vector heat log map, minimum
// angles and negative cotan weights, on procedurally generated poor meshes (no mesh files). Compares the
// mesh triangles (extrinsic), the intrinsic Delaunay triangulation (flips) and its refinement.
//
//   dotnet run -c Release --project benchmarks/IntrinsicBenchmark
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Program
{
    // median time of runs of f after one untimed warm-up run (JIT); returns the result of the last run
    static T Timed<T>(Func<T> f, out long ms, int runs = 3)
    {
        T r = f();
        var times = new List<long>();
        for (int k = 0; k < runs; ++k) {
            var sw = Stopwatch.StartNew();
            r = f();
            times.Add(sw.ElapsedMilliseconds);
        }
        times.Sort();
        ms = times[runs / 2];
        return r;
    }

    static void Main()
    {
        Console.WriteLine("| mesh | method | vertices (+inserted) | min angle | negative weights | mean error | max error | time |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");

        // plane of 169 degree triangles (rows 0.05 apart, shifted by half a column), 20 x 20
        DMesh3 plane = ObtusePlane(20, 400, 1.0, 0.05);
        int ps = Nearest(plane, new Vector3d(10, 10, 0));
        Vector3d pp = plane.GetVertex(ps);
        Func<int, bool> inner = v => plane.GetVertex(v).x >= 1 && plane.GetVertex(v).x <= 19;
        Distances("plane 169°", plane, ps, v => plane.GetVertex(v).Distance(pp), inner);

        // sphere from shifted latitude rings: nearly all triangles very obtuse
        DMesh3 sphere = RingSphere(1, 160, 24);
        int ss = Nearest(sphere, new Vector3d(1, 0, 0));
        Vector3d sp = sphere.GetVertex(ss);
        Distances("ring sphere", sphere, ss, v => sphere.GetVertex(v).Normalized.AngleR(sp.Normalized), v => true);

        // log map on the plane: mean distance of the log map vector from the true offset
        Console.WriteLine();
        Console.WriteLine("| mesh | method | log map mean error | log map max error | time |");
        Console.WriteLine("|---|---|---|---|---|");
        DMesh3 lp = ObtusePlane(10, 200, 1.0, 0.05);
        int ls = Nearest(lp, new Vector3d(5, 5, 0));
        foreach (bool refine in new[] { false, true }) {
            long ms;
            var vhm = Timed(() => { var m = new VectorHeatMethod(lp) { IntrinsicRefine = refine }; m.ComputeLogMap(ls); return m; }, out ms);
            Vector2d[] log = vhm.ComputeLogMap(ls);
            double sum = 0, max = 0; int n = 0;
            Vector3d c = lp.GetVertex(ls);
            foreach (int v in lp.VertexIndices()) {
                Vector3d p = lp.GetVertex(v);
                if (p.x < 1 || p.x > 9 || p.y < 1 || p.y > 9) continue;
                double e = (vhm.Triangulation.ToWorld(ls, log[v]) - (p - c)).Length;
                sum += e; max = Math.Max(max, e); n++;
            }
            Console.WriteLine($"| plane 169° 10 x 10 | {(refine ? "refined 25°" : "flips")} | {sum / n:F4} | {max:F4} | {ms} ms |");
        }
    }

    static void Distances(string name, DMesh3 mesh, int src, Func<int, double> exact, Func<int, bool> use)
    {
        var configs = new (string, bool, bool, double)[] {
            ("mesh triangles", false, false, 0), ("intrinsic flips", true, false, 0),
            ("refined 0° (boundary only)", true, true, 0), ("refined 25°", true, true, 25) };
        foreach (var (label, intrinsic, refine, angle) in configs) {
            long ms;
            var geo = Timed(() => {
                var g = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = intrinsic, IntrinsicRefine = refine, RefineMinAngle = angle };
                g.Compute(new[] { src });
                return g;
            }, out ms);
            bool ok = geo.Distance != null;
            double mean = 0, max = 0; int n = 0;
            if (ok)
                foreach (int v in mesh.VertexIndices()) {
                    if (use(v) == false) continue;
                    double e = Math.Abs(geo.Distance[v] - exact(v));
                    mean += e; max = Math.Max(max, e); n++;
                }
            // angles and weights of the same kind of triangulation, built by IntrinsicTriangulation (for the flips
            // MeshGeodesicDistance uses its own flip code, which ends in an intrinsic Delaunay triangulation too)
            IntrinsicRefinementResult r;
            var T = intrinsic ? IntrinsicTriangulation.BuildDelaunay(mesh, refine, out r, angle) : new IntrinsicTriangulation(mesh);
            int neg = 0;
            for (int h = 0; h < T.HalfedgeCount; ++h)
                if ((T.Twin(h) < 0 || T.Twin(h) > h) && T.CotanWeight(h) < -1e-9) neg++;
            string verts = T.VertexCount + (T.VertexCount > T.InputVertexCount ? $" (+{T.VertexCount - T.InputVertexCount})" : "");
            Console.WriteLine($"| {name} | {label} | {verts} | {T.MinAngle() * 180 / Math.PI:F1}° | {neg} | " +
                (ok ? $"{mean / n:F4} | {max:F4}" : "failed | failed") + $" | {ms} ms |");
        }
    }

    static int Nearest(DMesh3 m, Vector3d p) { return m.VertexIndices().OrderBy(v => m.GetVertex(v).DistanceSquared(p)).First(); }

    static DMesh3 ObtusePlane(int nx, int ny, double h, double dy)
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

    static DMesh3 RingSphere(double R, int rings, int perRing)
    {
        DMesh3 mesh = new DMesh3();
        int north = mesh.AppendVertex(new Vector3d(0, 0, R));
        var ring = new List<int[]>();
        for (int k = 1; k < rings; ++k) {
            double th = Math.PI * k / rings;
            int[] r = new int[perRing];
            for (int i = 0; i < perRing; ++i) {
                double ph = 2 * Math.PI * (i + ((k % 2 == 1) ? 0.5 : 0)) / perRing;
                r[i] = mesh.AppendVertex(new Vector3d(R * Math.Sin(th) * Math.Cos(ph), R * Math.Sin(th) * Math.Sin(ph), R * Math.Cos(th)));
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
}
