// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using g3;
using gs;

// The cost of the lost-area report of MeshRobustRepair (MeasureLostArea, #1479) against the repair, on procedural inputs: Apply() with the report off
// and on, alternating, in fresh copies of the same input; medians over the repetitions (default 7, first run excluded as the warm-up).
// Usage: LostAreaBenchmark [repetitions]
static class Program
{
    static DMesh3 Sphere(int n) { return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh(); }

    static DMesh3 Box(Vector3d c, Vector3d half) { return new TrivialBox3Generator() { Box = new Box3d(c, half) }.Generate().MakeDMesh(); }

    // a sphere with a hole of about 3 % of its area (the repair closes it)
    static DMesh3 HoledSphere(int n)
    {
        DMesh3 m = Sphere(n);
        foreach (int t in m.TriangleIndices().ToList())
            if (m.GetTriCentroid(t).Normalized.Dot(Vector3d.AxisX) > 0.995)
                m.RemoveTriangle(t);
        return m;
    }

    // the unit box and a slab through it from wall to wall: the slab's two large faces are interior walls (the exact resolve drops them)
    static DMesh3 BoxWithWall()
    {
        DMesh3 m = Box(new Vector3d(.5, .5, .5), new Vector3d(.5, .5, .5));
        MeshEditor.Append(m, Box(new Vector3d(.5, .5, .5), new Vector3d(.5, .05, .5)));
        return m;
    }

    static DMesh3 OverlappingSpheres(int n)
    {
        DMesh3 m = Sphere(n);
        DMesh3 b = Sphere(n);
        MeshTransforms.Translate(b, new Vector3d(1.2, 0, 0));
        MeshEditor.Append(m, b);
        return m;
    }

    static double Median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        int reps = args.Length > 0 ? int.Parse(args[0]) : 7;
        var inputs = new List<(string, Func<DMesh3>)> {
            ("box with an interior wall, 24 triangles", BoxWithWall),
            ("sphere with a hole, 1.4k triangles", () => HoledSphere(12)),
            ("two overlapping spheres, 23k triangles", () => OverlappingSpheres(32)),
            ("sphere with a hole, 99k triangles", () => HoledSphere(92)),
        };
        Console.WriteLine("input,triangles,path,repair_ms,with_report_ms,extra_ms,extra_percent,lost_share");
        foreach (var (name, make) in inputs) {
            var off = new List<double>(); var on = new List<double>(); var extra = new List<double>();
            string path = ""; double share = double.NaN; int tris = make().TriangleCount;
            for (int i = 0; i <= reps; ++i) {
                var a = new MeshRobustRepair(make());
                var sw = Stopwatch.StartNew();
                a.Apply();
                double tOff = sw.Elapsed.TotalMilliseconds;
                var b = new MeshRobustRepair(make()) { MeasureLostArea = true };
                sw.Restart();
                b.Apply();
                double tOn = sw.Elapsed.TotalMilliseconds;
                path = b.ResultPath.ToString(); share = b.LostAreaShare;
                if (i == 0)
                    continue;       // warm-up
                off.Add(tOff); on.Add(tOn); extra.Add(tOn - tOff);
            }
            double mOff = Median(off), mOn = Median(on), mExtra = Median(extra);
            Console.WriteLine($"\"{name}\",{tris},{path},{mOff:F1},{mOn:F1},{mExtra:F1},{100 * mExtra / mOff:F1},{share:G6}");
        }
    }
}
