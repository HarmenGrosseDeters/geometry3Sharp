using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;
namespace geometry3Sharp.Tests
{
    public class ScratchGridMeasure
    {
        static Mesh2Input Parcels(int seed, double angle)
        {
            // Herne-style tile: a jittered grid of parcels (corner jitter 1..4 cm on a 10..30 m lattice), snap 1 cm, slivers: the corner
            // points of neighbouring parcels are nearly collinear (0.05..3 degrees) and the hull edges run less than a pixel from inner edges
            var rnd = new Random(seed);
            int nx = 2 + rnd.Next(3), ny = 2 + rnd.Next(3);
            double cell = 10 + 20 * rnd.NextDouble();
            var xs = new List<double>(); var ys = new List<double>();
            int Id(int i, int j) => j * (nx + 1) + i;
            for (int j = 0; j <= ny; ++j) for (int i = 0; i <= nx; ++i) {
                bool inner = i > 0 && i < nx && j > 0 && j < ny;
                double jit = inner ? (rnd.NextDouble() - 0.5) * 0.08 : (rnd.NextDouble() - 0.5) * 0.03;
                xs.Add(Math.Round(371800 + i * cell + jit * (rnd.Next(2)), 3)); ys.Add(Math.Round(5710600 + j * cell + jit * (rnd.Next(2)), 3));
            }
            var input = new Mesh2Input { X = xs.ToArray(), Y = ys.ToArray(), SnapTolerance = 0.01, MinAngleDeg = angle, SteinerLimit = 50000 };
            for (int j = 0; j < ny; ++j) for (int i = 0; i < nx; ++i)
                input.Rings.Add(new RingInput(new[] { Id(i, j), Id(i + 1, j), Id(i + 1, j + 1), Id(i, j + 1) }, false));
            return input;
        }

        static IEnumerable<(string name, Func<Mesh2Input> make)> Inputs()
        {
            foreach (int s in Enumerable.Range(0, 150)) foreach (double a in new[] { 20.0, 30.0 }) { int ss = s; double aa = a; yield return ($"star{ss}/{aa}", () => { var i = StarPolygonFuzz.Make(ss); i.MinAngleDeg = aa; return i; }); }
            foreach (int s in Enumerable.Range(0, 150)) foreach (double a in new[] { 20.0, 30.0 }) { int ss = s; double aa = a; yield return ($"seg{ss}/{aa}", () => QualityMesh2PinchFreeTests.Segments(ss, aa)); }
            foreach (int s in Enumerable.Range(0, 40)) foreach (double a in new[] { 20.0, 30.0 }) { int ss = s; double aa = a; yield return ($"parcels{ss}/{aa}", () => Parcels(ss, aa)); }
            foreach (double snap in new[] { 1.0 / 256, 1.0 / 64, 1.0 / 16 }) foreach (double deg in new[] { 1.0, 2.6, 5.0, 10.0 }) foreach (double a in new[] { 20.0, 30.0 }) { double sn = snap, dg = deg, aa = a;
                yield return ($"wedge{dg}/{sn}/{aa}", () => { var i = QualityMesh2SmallAngleRuleTests.HullWedge(aa, dg, sn); i.ConstrainedQuality = false; return i; }); }
            foreach (double snap in new[] { 1.0 / 256, 1.0 / 64 }) foreach (int kind in new[] { 0, 1, 2, 3 }) foreach (double a in new[] { 20.0, 30.0 }) { double sn = snap; int k = kind; double aa = a;
                yield return ($"pair{k}/{sn}/{aa}", () => { var i = QualityMesh2ApexExemptionTests.Pair(k, aa); i.ConstrainedQuality = false; i.SnapTolerance = sn; return i; }); }
            foreach (int w in new[] { 0, 1 }) { int ww = w; yield return ($"zigzag{ww}", () => QualityMesh2CollapsedPartsTests.Zigzag(ww, 20)); }
        }

        [Fact]
        public void Measure()
        {
            var cases = Inputs().ToList();
            var rows = new List<string>();
            foreach (var (name, make) in cases) {
                var res = new List<(string v, int tri, long ms, bool lim, string rep)>();
                foreach (bool off in new[] { true, false }) {
                    QualityMesh2.Refiner.RuleCOffGridOnlyProbe = off;
                    try {
                        var input = make();
                        var sw = Stopwatch.StartNew();
                        var o = new QualityMesh2Candidate().Run(input);
                        long ms = sw.ElapsedMilliseconds;
                        if (o.Status != Mesh2Status.Success) { res.Add(("FAIL:" + o.Status, 0, ms, false, "")); continue; }
                        var r = Oracle.Check(input, o, new OracleExpect { Milestone = Milestone.M3 });
                        res.Add((r.Ok ? "ok" : "INVALID", o.Triangles.Length, ms, o.SteinerLimitReached, r.Ok ? "" : r.ToString()));
                    } finally { QualityMesh2.Refiner.RuleCOffGridOnlyProbe = false; }
                }
                Console.Error.WriteLine($"ROW\t{name}\t{res[0].v}\t{res[0].tri}\t{res[0].ms}\t{res[0].lim}\t{res[1].v}\t{res[1].tri}\t{res[1].ms}\t{res[1].lim}\t{res[1].rep.Replace('\n', ' ')}");
            }
        }
    }
}
