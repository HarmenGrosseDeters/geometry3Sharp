using System;
using System.Diagnostics;
using g3;

// The ray, line and segment against box classes (IntrExactBox3) and their callers: DMeshAABBTree3 ray queries (the tree culls with
// the conservative form IntrExactBox3.RayAabbMay; FindRayIntersectT is the exact form of the public class) and DCurve3BoxTree ray
// queries (IntrRay3Box3.Intersects).
// Run: dotnet run -c Release (best with DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 for a stable number); the numbers are ns per call, the minimum of 9 repetitions.
class Program
{
    static double MinNs(Action f, int n, int reps = 9)
    {
        f();
        double best = double.MaxValue;
        for (int r = 0; r < reps; r++) { var sw = Stopwatch.StartNew(); f(); best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / n); }
        return best;
    }

    static void Main()
    {
        var r = new Random(3);
        int N = 200000;
        var rays = new Ray3d[N]; var boxes = new AxisAlignedBox3d[N]; var obox = new Box3d[N];
        for (int i = 0; i < N; i++) {
            Vector3d c = new Vector3d(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);
            Vector3d e = new Vector3d(0.05 + 0.2 * r.NextDouble(), 0.05 + 0.2 * r.NextDouble(), 0.05 + 0.2 * r.NextDouble());
            boxes[i] = new AxisAlignedBox3d(ref c, e.x, e.y, e.z); obox[i] = new Box3d(c, e);
            Vector3d o = new Vector3d(r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2, r.NextDouble() * 4 - 2);
            Vector3d d = new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5); d.Normalize();
            if (i % 4 == 0) d = new Vector3d(0, 0, 1);         // every fourth ray axis-aligned (parallel to two slabs)
            rays[i] = new Ray3d(o, d);
        }
        int h = 0; double t = 0;
        Console.WriteLine($"IntrRay3AxisAlignedBox3.FindRayIntersectT: {MinNs(() => { h = 0; for (int i = 0; i < N; i++) if (IntrRay3AxisAlignedBox3.FindRayIntersectT(ref rays[i], ref boxes[i], out t)) h++; }, N):F1} ns (hits {h})");
        Console.WriteLine($"IntrRay3AxisAlignedBox3.Intersects: {MinNs(() => { h = 0; for (int i = 0; i < N; i++) if (IntrRay3AxisAlignedBox3.Intersects(ref rays[i], ref boxes[i])) h++; }, N):F1} ns (hits {h})");
        Console.WriteLine($"IntrRay3Box3.Intersects (expand 0.01): {MinNs(() => { h = 0; for (int i = 0; i < N; i++) if (IntrRay3Box3.Intersects(ref rays[i], ref obox[i], 0.01)) h++; }, N):F1} ns (hits {h})");
        Console.WriteLine($"IntrRay3Box3.Find: {MinNs(() => { h = 0; for (int i = 0; i < N; i++) if (new IntrRay3Box3(rays[i], obox[i]).Find()) h++; }, N):F1} ns (hits {h})");
        Console.WriteLine($"IntrRay3AxisAlignedBox3.Find: {MinNs(() => { h = 0; for (int i = 0; i < N; i++) if (new IntrRay3AxisAlignedBox3(rays[i], boxes[i]).Find()) h++; }, N):F1} ns (hits {h})");

        // the callers
        var sph = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 150 }.Generate().MakeDMesh();
        var tree = new DMeshAABBTree3(sph, true);
        var rr = new Random(9); int M = 100000; var rs = new Ray3d[M];
        for (int i = 0; i < M; i++) { Vector3d o = new Vector3d(rr.NextDouble() * 4 - 2, rr.NextDouble() * 4 - 2, 3); Vector3d d = new Vector3d(rr.NextDouble() * 0.2 - 0.1, rr.NextDouble() * 0.2 - 0.1, -1); d.Normalize(); rs[i] = new Ray3d(o, d); }
        int hh = 0;
        Console.WriteLine($"DMeshAABBTree3.FindNearestHitTriangle ({sph.TriangleCount} triangles): {MinNs(() => { hh = 0; for (int i = 0; i < M; i++) if (tree.FindNearestHitTriangle(rs[i]) != DMesh3.InvalidID) hh++; }, M):F0} ns (hits {hh})");

        var pts = new System.Collections.Generic.List<Vector3d>(); var cr = new Random(11);
        for (int i = 0; i < 4000; i++) { double a = i * 0.05; pts.Add(new Vector3d(Math.Cos(a) * (1 + 0.1 * Math.Sin(a * 3)), Math.Sin(a) * (1 + 0.1 * Math.Sin(a * 3)), a * 0.01 + 0.05 * Math.Sin(a * 7))); }
        var curve = new DCurve3(pts, false); var ct = new DCurve3BoxTree(curve);
        int Q = 20000; var qr = new Ray3d[Q];
        for (int i = 0; i < Q; i++) { Vector3d o = new Vector3d(cr.NextDouble() * 4 - 2, cr.NextDouble() * 4 - 2, cr.NextDouble() * 2); Vector3d dd = new Vector3d(cr.NextDouble() - .5, cr.NextDouble() - .5, cr.NextDouble() - .5); dd.Normalize(); qr[i] = new Ray3d(o, dd); }
        double acc = 0; int ch = 0;
        Console.WriteLine($"DCurve3BoxTree.SquaredDistance(ray): {MinNs(() => { acc = 0; for (int i = 0; i < Q; i++) { int sg; double st, rt; acc += ct.SquaredDistance(qr[i], out sg, out st, out rt); } }, Q):F0} ns");
        Console.WriteLine($"DCurve3BoxTree.FindClosestRayIntersction: {MinNs(() => { ch = 0; for (int i = 0; i < Q; i++) { int sg; double rt; if (ct.FindClosestRayIntersction(qr[i], 0.05, out sg, out rt)) ch++; } }, Q):F0} ns (hits {ch})");
    }
}
