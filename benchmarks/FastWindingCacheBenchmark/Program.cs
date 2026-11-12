// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Diagnostics;
using System.Linq;
using g3;

// Build time of the fast-winding cache of DMeshAABBTree3 (README.md): the cache is built by the first FastWindingNumber
// call on a tree; the tree itself is built before the clock starts. Median of the rounds, serial (gParallel.SerialOnThisThread, by reflection) and with the parallel top levels. The checksum (sum of the bits of 2000 winding numbers) must
// agree between two library builds whose caches agree bit for bit.
static class Program
{
    static DMesh3 Bumpy(int edgeVertices)
    {
        DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        var rnd = new Random(1);
        foreach (int v in m.VertexIndices().ToArray())
            m.SetVertex(v, m.GetVertex(v) * (1 + 0.1 * (rnd.NextDouble() - 0.5)));
        foreach (int t in m.TriangleIndices().ToArray())
            if (m.GetTriCentroid(t).z > 0.7) m.RemoveTriangle(t, true, false);   // open: the cache is what classifies
        return m;
    }

    // gParallel.SerialOnThisThread is internal: set through reflection (a [ThreadStatic] field of this thread)
    static readonly System.Reflection.FieldInfo SerialField = typeof(gParallel).GetField("SerialOnThisThread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    static void SetSerial(bool v) => SerialField.SetValue(null, v);

    static void Main(string[] args)
    {
        int rounds = args.Length > 0 ? int.Parse(args[0]) : 7;
        Console.WriteLine("triangles,mode,cache_build_ms_median,checksum");
        foreach (int n in new[] { 60, 130, 290 }) {
            DMesh3 m = Bumpy(n);
            var rnd = new Random(2);
            var pts = Enumerable.Range(0, 2000).Select(_ => new Vector3d(3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5, 3 * rnd.NextDouble() - 1.5)).ToArray();
            foreach (bool serial in new[] { true, false }) {
                var times = new double[rounds];
                long checksum = 0;
                for (int r = -1; r < rounds; ++r) {      // round -1: warm-up
                    SetSerial(serial);
                    var tree = new DMeshAABBTree3(m, true);
                    GC.Collect();
                    var sw = Stopwatch.StartNew();
                    tree.FastWindingNumber(pts[0]);
                    sw.Stop();
                    SetSerial(false);
                    if (r >= 0) times[r] = sw.Elapsed.TotalMilliseconds;
                    if (r == 0) checksum = pts.Aggregate(0L, (a, p) => unchecked(a * 31 + BitConverter.DoubleToInt64Bits(tree.FastWindingNumber(p))));
                }
                Array.Sort(times);
                Console.WriteLine($"{m.TriangleCount},{(serial ? "serial" : "parallel")},{times[rounds / 2]:F1},{checksum}");
            }
        }
    }
}
