using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The D1 mechanism of SafeRounding, reproduced procedurally, and an audit of the drift certificate
    /// (docs/design/SAFE_ROUNDING_D1_DRIFT.md). A dense patch of tiny, well-shaped triangles on a unit sphere (as
    /// RoundingDenseTessellationTests, here repaired directly, so the surface at the start is the input) is degenerate
    /// by area and contracted. Under the apex rule (before Boolean 2.1) each contraction charged its apex the largest
    /// drift among its vertices plus its own bound, so neighbouring contractions chained, and the budget was used up
    /// (0.96 of it, degenerate triangles left) although the surface had moved by less than a thousandth of it. The
    /// reference rule measures each triangle's displacement from its reference corners instead: the patch is repaired
    /// with a drift of a fraction of the bound. The tests check that, that the wider bound certifies its result within
    /// the original one, and for every similarity that the reported drift bounds the measured distance, which holds for
    /// any sound accounting.
    /// </summary>
    public class RoundingDriftClusterTests
    {
        const double Diagonal = 3.4641016151377544; // 2 sqrt(3), the unit sphere's box

        // a unit sphere refined around one point by longest-edge splits, the new vertices on the sphere
        static DMesh3 DensePatchSphere(double radius, double edge)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { EdgeVertices = 16, Radius = 1 }.Generate().MakeDMesh();
            Vector3d p = new Vector3d(-1, -1, -1).Normalized;
            for (int pass = 0; pass < 200; ++pass) {
                var split = new List<(int e, double len)>();
                foreach (int e in m.EdgeIndices()) {
                    Index2i ev = m.GetEdgeV(e);
                    Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                    double len = a.Distance(b);
                    if (len > edge && ((a + b) * 0.5).Distance(p) < radius + len)
                        split.Add((e, len));
                }
                if (split.Count == 0)
                    break;
                foreach (var (e, _) in split.OrderByDescending(x => x.len).ThenBy(x => x.e)) {
                    if (!m.IsEdge(e))
                        continue;
                    Index2i ev = m.GetEdgeV(e);
                    if (m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) <= edge)
                        continue;
                    if (m.SplitEdge(e, out DMesh3.EdgeSplitInfo s) == MeshResult.Ok)
                        m.SetVertex(s.vNew, m.GetVertex(s.vNew).Normalized);
                }
            }
            return m;
        }

        static DMesh3 Patch() => DensePatchSphere(2e-5 * Diagonal, 2.5e-6 * Diagonal);

        // the largest distance of the corners, edge midpoints and centroids of from's triangles to the surface to: a
        // lower estimate of the one-sided Hausdorff distance (nearest points, not DistPoint3Triangle3.Get, whose
        // closed form loses distances far below the triangle's size)
        static double Measured(DMesh3 from, DMesh3 to)
        {
            var tree = new DMeshAABBTree3(to, true);
            double worst = 0;
            foreach (int t in from.TriangleIndices()) {
                Index3i tv = from.GetTriangle(t);
                Vector3d a = from.GetVertex(tv.a), b = from.GetVertex(tv.b), c = from.GetVertex(tv.c);
                foreach (Vector3d p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (c + a) / 2, (a + b + c) / 3 }) {
                    Index3i nv = to.GetTriangle(tree.FindNearestTriangle(p));
                    var d = new DistPoint3Triangle3(p, new Triangle3d(to.GetVertex(nv.a), to.GetVertex(nv.b), to.GetVertex(nv.c)));
                    d.GetSquared();
                    worst = Math.Max(worst, p.Distance(d.TriangleClosest));
                }
            }
            return worst;
        }

        static double Measured2(DMesh3 x, DMesh3 y) => Math.Max(Measured(x, y), Measured(y, x));

        [Fact]
        public void DensePatch_IsDegenerateByAreaOnly()
        {
            DMesh3 m = Patch();
            double eps = 1e-12 * Diagonal * Diagonal;
            int tiny = 0;
            foreach (int t in m.TriangleIndices()) {
                if (m.GetTriArea(t) >= eps)
                    continue;
                tiny++;
                Index3i f = m.GetTriangle(t);
                Vector3d p0 = m.GetVertex(f.a), p1 = m.GetVertex(f.b), p2 = m.GetVertex(f.c);
                double longest = Math.Max(p0.Distance(p1), Math.Max(p1.Distance(p2), p2.Distance(p0)));
                // well-shaped: twice the area over the longest edge squared (1 for a right isosceles triangle)
                Assert.True(2 * m.GetTriArea(t) / (longest * longest) > 0.1, $"triangle {t} is not well-shaped");
                Assert.True(longest < 1e-5 * Diagonal);
            }
            Assert.True(tiny > 1000, $"{tiny} tiny triangles");
        }

        [Fact]
        public void DensePatch_CleanWithAFractionOfTheBound()
        {
            DMesh3 start = Patch(), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply(), $"{r.RemainingDegenerate} degenerate left, drift {r.MaxDrift / r.MaxLength} of the bound");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal(0, r.RemainingDegenerate);
            Assert.False(r.EvaluationLimitReached);
            Assert.True(r.InitialDegenerate > 1000, $"initial degenerate {r.InitialDegenerate}");
            Assert.True(r.Collapses > 300, $"{r.Collapses} contractions");
            // the certificate holds, and the surface has moved by less than a thousandth of the bound
            double distance = Measured2(m, start);
            Assert.True(distance <= r.MaxDrift);
            Assert.True(distance < 1e-3 * r.MaxLength, $"measured distance {distance / r.MaxLength} of the bound");
            // the D1 fix: the drift is a fraction of the bound (0.14 measured; the apex rule reached 0.96 and left
            // degenerate triangles, the smallest rejected edit needing just over the bound)
            Assert.True(r.MaxDrift < 0.5 * r.MaxLength, $"drift {r.MaxDrift / r.MaxLength} of the bound");
        }

        [Fact]
        public void DensePatch_WiderBound_CleanWithinTheOriginalBound()
        {
            // an upper bound for any accounting: with four times the bound the same edits repair the patch, and the
            // result lies within a thousandth of the original bound of the start
            DMesh3 start = Patch(), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { MaxDisplacement = 4e-5 };
            Assert.True(r.Apply(), $"{r.RemainingDegenerate} degenerate left");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
            double original = r.MaxLength / 4;
            double distance = Measured2(m, start);
            Assert.True(distance < 1e-3 * original, $"measured distance {distance / original} of the original bound");
            Assert.Equal("", BooleanOracle.SameSolid(m, start, 2, original));
            // the D1 fix: the result is certified within half the original bound (the apex rule certified it only
            // beyond the original bound)
            Assert.True(r.MaxDrift < 0.5 * original, $"certified {r.MaxDrift / original} times the original bound");
        }

        [Fact]
        public void ReportedDrift_BoundsTheMeasuredDistance_AllSimilarities()
        {
            // (a smaller patch, 392 degenerate triangles: nine repairs within the test time)
            DMesh3 start = DensePatchSphere(1e-5 * Diagonal, 2.5e-6 * Diagonal), m0 = new DMesh3(start);
            var r0 = new MeshRoundingRepair(m0);
            r0.Apply();
            Assert.True(r0.Collapses > 100, $"{r0.Collapses} contractions");
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 s = Invariance.Transformed(start, sim), m = new DMesh3(s);
                var r = new MeshRoundingRepair(m);
                r.Apply();
                Assert.True(m.IsClosed() && Invariance.AllFinite(m), sim.ToString());
                Assert.Equal(0, r.RemainingIntersections);
                Assert.True(r.MaxDrift <= r.MaxLength, sim.ToString());
                // (the measured distance carries the rounding of the coordinates)
                double slack = 16 * sim.ApplyLength(sim.Resolution(2));
                Assert.True(Measured2(m, s) <= r.MaxDrift + slack, sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, sim)));
                    Assert.Equal(r0.Collapses, r.Collapses);
                    Assert.Equal(r0.RemainingDegenerate, r.RemainingDegenerate);
                    Assert.Equal(r0.MaxDrift, r.MaxDrift / sim.Scale);
                }
            }
        }

        [Fact]
        public void DensePatch_SameResultTwiceAndSerial()
        {
            DMesh3 a = Patch(), b = Patch();
            var ra = new MeshRoundingRepair(a);
            var rb = new MeshRoundingRepair(b);
            ra.Apply(); rb.Apply();
            Assert.Equal(MeshMinimizer.ToObj(a), MeshMinimizer.ToObj(b));
            Assert.Equal(ra.MaxDrift, rb.MaxDrift);
            // serial equals parallel (the search is parallel, the accounting is not)
            DMesh3 c = Patch();
            var rc = new MeshRoundingRepair(c);
            gParallel.SerialOnThisThread = true;
            try {
                rc.Apply();
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            Assert.Equal("", Invariance.Difference(a, c));
            Assert.Equal(ra.MaxDrift, rc.MaxDrift);
            // a second repair of the result starts with a fresh budget (docs/design/SAFE_ROUNDING.md section 1)
            var again = new MeshRoundingRepair(a);
            again.Apply();
            Assert.True(again.MaxDrift <= again.MaxLength);
        }
    }
}
