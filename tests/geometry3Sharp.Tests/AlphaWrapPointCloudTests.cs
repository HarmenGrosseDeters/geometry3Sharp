using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshAlphaWrap on point clouds (docs/design/POINT_CLOUD_RECONSTRUCTION.md, option A): procedural samplings of a
    /// torus (genus 1) with noise, a hole and a non-uniform density. The wrap of a point set is closed, manifold and
    /// encloses every point (AlphaWrapTests.CheckWrap), also in other units and coordinates, and it does not depend on
    /// the order of the points or on a reused instance.
    /// </summary>
    public class AlphaWrapPointCloudTests
    {
        const double R = 3, r = 1;      // torus radii

        /// <summary> n points on the torus, area-weighted; sigma = Gaussian noise; hole = a removed patch; ratio = density ratio along the ring </summary>
        internal static List<Vector3d> Torus(int n, int seed, double sigma = 0, bool hole = false, bool nonUniform = false)
        {
            var rnd = new Random(seed);
            var pts = new List<Vector3d>();
            double Gauss() { double a = 1 - rnd.NextDouble(), b = rnd.NextDouble(); return Math.Sqrt(-2 * Math.Log(a)) * Math.Cos(2 * Math.PI * b); }
            while (pts.Count < n) {
                double u = rnd.NextDouble() * 2 * Math.PI, v = rnd.NextDouble() * 2 * Math.PI;
                if (rnd.NextDouble() > (R + r * Math.Cos(v)) / (R + r)) continue;
                if (nonUniform && rnd.NextDouble() > 0.1 + 0.9 * (Math.Cos(u) + 1) / 2) continue;
                if (hole && Math.Abs(u - 1) < 0.5 && Math.Abs(v - 0.5) < 0.6) continue;
                var p = new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v));
                pts.Add(p + new Vector3d(Gauss(), Gauss(), Gauss()) * sigma);
            }
            return pts;
        }

        public static IEnumerable<object[]> Samplings() => new[] {
            new object[] { "clean", 0.0, false, false },
            new object[] { "noise", 0.08, false, false },
            new object[] { "hole", 0.0, true, false },
            new object[] { "non-uniform", 0.0, false, true },
            new object[] { "noise+hole+non-uniform", 0.08, true, true },
        };

        [Theory]
        [MemberData(nameof(Samplings))]
        public void PointCloudWrap_Torus_ClosedManifoldAndEnclosing(string name, double sigma, bool hole, bool nonUniform)
        {
            List<Vector3d> pts = Torus(4000, 7, sigma, hole, nonUniform);
            var w = new MeshAlphaWrap(pts) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(w.Compute(), name + ": " + w.Status + " " + w.Error);
            AlphaWrapTests.CheckWrap(w, pts);
            // genus 1 for a sampling dense against alpha (alpha = 0.37, spacing about 0.17): the wrap does not close the ring or the tube
            if (sigma == 0 && !hole && !nonUniform) {
                var genus = MeshMeasurements.Genus(w.Result);
                Assert.True(genus.Valid);
                Assert.Equal(1, genus.Genus);
            }
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void PointCloudWrap_Torus_InvariantOverSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            List<Vector3d> pts = Torus(1500, 11);
            var reference = new MeshAlphaWrap(pts) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(reference.Compute());
            var moved = new MeshAlphaWrap(pts.Select(s.Apply)) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(moved.Compute(), name + ": " + moved.Status + " " + moved.Error);
            var moved_pts = pts.Select(s.Apply).ToList();
            if (s.Name == Similarity.RotatedUtmMillimetre.Name) {
                // the offset (2e-5) is below the rounding unit of the coordinates (1e-10 at 6e5) by only 5 orders: the distance
                // checks of CheckWrap (1e-6 of the offset) are finer than the oracle's own rounding, so check the structure
                ResultChecks.AssertClosed(moved.Result);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(moved.Result));
                var tree = new DMeshAABBTree3(moved.Result, true);
                foreach (Vector3d p in moved_pts)
                    Assert.True(tree.WindingNumber(p) > 0.99, "input point outside of the wrap");
            } else
                AlphaWrapTests.CheckWrap(moved, moved_pts);
            if (s.IsExact) {
                // a power of two scale is exact: the same wrap, scaled, bit for bit
                Assert.Equal(reference.Result.VertexCount, moved.Result.VertexCount);
                Assert.Equal(reference.Result.TriangleCount, moved.Result.TriangleCount);
                foreach (int vid in reference.Result.VertexIndices())
                    Assert.Equal(s.Apply(reference.Result.GetVertex(vid)), moved.Result.GetVertex(vid));
            } else {
                // the wrap adapts to the units: the same size of result (triangle count within 5 %)
                Assert.InRange(moved.Result.TriangleCount, 0.95 * reference.Result.TriangleCount, 1.05 * reference.Result.TriangleCount);
            }
        }

        [Fact]
        public void PointCloudWrap_RepeatedAndReusedRunsGiveTheSameResult()
        {
            List<Vector3d> pts = Torus(2000, 3, 0.05, true, true);
            var a = new MeshAlphaWrap(pts) { RelativeAlpha = 20, RelativeOffset = 600 };
            var b = new MeshAlphaWrap(pts) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(a.Compute());
            Assert.True(b.Compute());
            AssertSameMesh(a.Result, b.Result);
            DMesh3 first = new DMesh3(a.Result);
            Assert.True(a.Compute());       // reuse of the instance
            AssertSameMesh(first, a.Result);
        }

        static void AssertSameMesh(DMesh3 x, DMesh3 y)
        {
            Assert.Equal(x.VertexCount, y.VertexCount);
            Assert.Equal(x.TriangleCount, y.TriangleCount);
            foreach (int vid in x.VertexIndices())
                Assert.Equal(x.GetVertex(vid), y.GetVertex(vid));
            foreach (int tid in x.TriangleIndices())
                Assert.Equal(x.GetTriangle(tid), y.GetTriangle(tid));
        }

        [Fact]
        public void PointCloudWrap_OrderOfThePointsDoesNotBreakTheGuarantees()
        {
            List<Vector3d> pts = Torus(2000, 5, 0.05);
            var rnd = new Random(1);
            List<Vector3d> shuffled = pts.OrderBy(p => rnd.Next()).ToList();
            var w = new MeshAlphaWrap(shuffled) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(w.Compute());
            AlphaWrapTests.CheckWrap(w, pts);
        }

        [Fact]
        public void PointCloudWrap_CoplanarPoints_ThinClosedShell()
        {
            // a flat sampling (no volume): the wrap is a thin closed shell around the sheet
            var rnd = new Random(2);
            var pts = Enumerable.Range(0, 800).Select(i => new Vector3d(4 * rnd.NextDouble(), 4 * rnd.NextDouble(), 0)).ToList();
            var w = new MeshAlphaWrap(pts) { RelativeAlpha = 20, RelativeOffset = 600 };
            Assert.True(w.Compute(), w.Status + " " + w.Error);
            AlphaWrapTests.CheckWrap(w, pts);
        }
    }
}
