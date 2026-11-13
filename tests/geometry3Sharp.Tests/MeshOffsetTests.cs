using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshOffset (stage A1 of docs/design/MESH_OFFSET.md): offset and inset of closed meshes by dual contouring of the
    /// distance field, with the band certificate. Oracles: the Steiner formula for the volume of the offset of a box (and
    /// the shrunken box for the inset), the signed distance of the result's vertices recomputed independently, the
    /// components of a hollow part and of two boxes across a gap, the sharp crease of an L-shape's offset; plus invariance
    /// over Similarity.All(), reuse, determinism and mutants of the certificate.
    /// </summary>
    public class MeshOffsetTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max) => DegenerateFamilies.Box(min, max);

        static DMesh3 Hollow()
        {
            DMesh3 m = Box(new Vector3d(0, 0, 0), new Vector3d(4, 4, 4));
            DMesh3 inner = Box(new Vector3d(1, 1, 1), new Vector3d(3, 3, 3));
            inner.ReverseOrientation();
            new MeshEditor(m).AppendMesh(inner);
            return m;
        }

        // the L-shape of benchmarks/SharpFeatureProbe: 10 x 10 x 4 minus the quarter x >= 5, y >= 5 (re-entrant edge at x = y = 5)
        static DMesh3 LShape()
        {
            var poly = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 5), new Vector2d(5, 5), new Vector2d(5, 10), new Vector2d(0, 10) };
            var m = new DMesh3();
            int n = poly.Count;
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 0));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, 4));
            // bottom (facing -z) and top (+z): fans of the two rectangles of the L
            int[][] quads = { new[] { 0, 1, 2, 3 }, new[] { 0, 3, 4, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[2], q[1]); m.AppendTriangle(q[0], q[3], q[2]);
                m.AppendTriangle(n + q[0], n + q[1], n + q[2]); m.AppendTriangle(n + q[0], n + q[2], n + q[3]);
            }
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(i, j, n + j); m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        // Steiner: the volume of a box a x b x c grown by r
        static double BoxOffsetVolume(double a, double b, double c, double r) =>
            a * b * c + 2 * (a * b + b * c + c * a) * r + Math.PI * (a + b + c) * r * r + 4.0 / 3.0 * Math.PI * r * r * r;

        static MeshOffset Run(DMesh3 mesh, double d, Action<MeshOffset> configure = null)
        {
            var o = new MeshOffset(mesh, d);
            configure?.Invoke(o);
            o.Compute();
            return o;
        }

        static void AssertGood(MeshOffset o)
        {
            Assert.Equal(MeshOffset.Status.Success, o.ResultStatus);
            Assert.True(o.SelfIntersectionFree);
            Assert.True(o.BandCertified, $"band {o.BandDeviation}, uncertified {o.UncertifiedTriangles}");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(o.Result));
        }

        // signed distance to a closed mesh by an independent path (the nearest triangle and the exact crossing parity)
        static double SignedDistance(DMeshAABBTree3 tree, Vector3d p)
        {
            int t = tree.FindNearestTriangle(p);
            double u = Math.Sqrt(MeshQueries.TriangleDistance(tree.Mesh, t, p).DistanceSquared);
            return tree.IsInside(p) ? -u : u;
        }

        static int Components(DMesh3 m) { var c = new MeshConnectedComponents(m); c.FindConnectedT(); return c.Count; }

        [Theory]
        [InlineData(0.5)]
        [InlineData(0.3)]
        public void Offset_Box_VolumeOfTheSteinerFormula_WithinTheCertifiedBand(double d)
        {
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(2, 3, 1.5));
            MeshOffset o = Run(box, d);
            AssertGood(o);
            double dev = o.BandDeviation;
            Assert.True(dev <= 0.5 * o.UsedCellSize + 1e-12, $"band {dev}");
            // the result lies in the band (proven), so with the box's topology (measured here) its volume is between those of the
            // box's offsets by d - dev and d + dev
            double v = Volume(o.Result);
            Assert.InRange(v, BoxOffsetVolume(2, 3, 1.5, d - dev), BoxOffsetVolume(2, 3, 1.5, d + dev));
            // and the measured deviation of the volume is far inside that (the chords of the round parts)
            Assert.True(Math.Abs(v - BoxOffsetVolume(2, 3, 1.5, d)) < 0.01 * BoxOffsetVolume(2, 3, 1.5, d), $"volume {v}");
        }

        [Fact]
        public void Inset_Box_IsTheShrunkenBox_WithSharpCreases()
        {
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(2, 3, 1.5));
            double r = 0.25;
            MeshOffset o = Run(box, -r);
            AssertGood(o);
            double exact = (2 - 2 * r) * (3 - 2 * r) * (1.5 - 2 * r);
            Assert.True(Math.Abs(Volume(o.Result) - exact) < 1e-3 * exact, $"volume {Volume(o.Result)} vs {exact}");
            // the corners of the shrunken box are reproduced (QEF vertices at the corners, not chamfered)
            AxisAlignedBox3d b = o.Result.CachedBounds;
            Assert.True(b.Min.Distance(new Vector3d(r, r, r)) < 1e-6 && b.Max.Distance(new Vector3d(2 - r, 3 - r, 1.5 - r)) < 1e-6, b.ToString());
        }

        [Fact]
        public void Offset_VerticesHaveTheSignedDistance_WithinTheProvenBound()
        {
            foreach (double d in new[] { 0.6, -0.6 }) {
                DMesh3 m = LShape();
                MeshOffset o = Run(m, d, x => x.RelativeCellSize = 0.5);
                AssertGood(o);
                var tree = new DMeshAABBTree3(m, true);
                foreach (int v in o.Result.VertexIndices())
                    Assert.True(Math.Abs(SignedDistance(tree, o.Result.GetVertex(v)) - d) <= o.BandDeviation, $"d {d}, vertex {v}");
            }
        }

        [Fact]
        public void Offset_LShape_ConcaveCreaseIsSharp()
        {
            // the offset sheets of the faces x = 5 and y = 5 meet in the line x = y = 5 + d: dual contouring puts vertices on it
            double d = 0.6;
            MeshOffset o = Run(LShape(), d);
            AssertGood(o);
            int onCrease = o.Result.VertexIndices().Count(v => {
                Vector3d p = o.Result.GetVertex(v);
                return p.z > 0.5 && p.z < 3.5 && new Vector2d(p.x - (5 + d), p.y - (5 + d)).Length < 0.02 * o.UsedCellSize;
            });
            Assert.True(onCrease >= 10, $"{onCrease} vertices on the crease");
        }

        [Fact]
        public void Offset_HollowPart_KeepsTheInnerShell_InsetKeepsTheWall()
        {
            MeshOffset o = Run(Hollow(), 0.25, x => x.RelativeCellSize = 0.5);
            AssertGood(o);
            Assert.Equal(2, Components(o.Result));
            // outer shell: the 4-cube grown by 0.25; the void: the 2-cube shrunk by 0.25 (sharp, it is an inset of the void)
            double expected = BoxOffsetVolume(4, 4, 4, 0.25) - 1.5 * 1.5 * 1.5;
            Assert.True(Math.Abs(Volume(o.Result) - expected) < 0.01 * expected, $"{Volume(o.Result)} vs {expected}");

            MeshOffset i = Run(Hollow(), -0.25, x => x.RelativeCellSize = 0.5);
            AssertGood(i);
            Assert.Equal(2, Components(i.Result));
            double inner = BoxOffsetVolume(2, 2, 2, 0.25);
            Assert.True(Math.Abs(Volume(i.Result) - (3.5 * 3.5 * 3.5 - inner)) < 0.01 * inner, $"{Volume(i.Result)}");
        }

        [Theory]
        [InlineData(0.15, 2)]
        [InlineData(0.3, 1)]
        public void Offset_TwoBoxesAcrossAGap_MergeOnlyAboveHalfTheGap(double d, int components)
        {
            DMesh3 m = Box(Vector3d.Zero, new Vector3d(1, 1, 1));
            new MeshEditor(m).AppendMesh(Box(new Vector3d(1.4, 0, 0), new Vector3d(2.4, 1, 1)));
            MeshOffset o = Run(m, d);
            AssertGood(o);
            Assert.Equal(components, Components(o.Result));
        }

        [Fact]
        public void Offset_Similarities_Pow2BitIdentical_OthersWithinTheBand()
        {
            DMesh3 m = LShape();
            double d = 0.6;
            Action<MeshOffset> coarse = x => x.RelativeCellSize = 0.5;
            MeshOffset r0 = Run(new DMesh3(m), d, coarse);
            AssertGood(r0);
            double v0 = Volume(r0.Result);
            foreach (Similarity sim in Similarity.All()) {
                MeshOffset r = Run(Invariance.Transformed(m, sim), sim.ApplyLength(d), coarse);
                AssertGood(r);
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(r0.Result, Invariance.MappedBack(r.Result, sim)));
                    Assert.Equal(r0.BandDeviation, r.BandDeviation / sim.Scale);
                } else {
                    // another grid: the same surface within the band (a volume within area x band)
                    double v = Volume(r.Result) / (sim.Scale * sim.Scale * sim.Scale);
                    double area = MeshMeasurements.AreaT(r0.Result, r0.Result.TriangleIndices());
                    Assert.True(Math.Abs(v - v0) <= area * (r0.BandDeviation + r.BandDeviation / sim.Scale), $"{sim}: {v} vs {v0}");
                }
            }
        }

        [Fact]
        public void Offset_ScaleShiftSweep_BitIdentical()
        {
            DMesh3 m = Hollow();
            Action<MeshOffset> coarse = x => x.RelativeCellSize = 0.5;
            MeshOffset r0 = Run(new DMesh3(m), -0.25, coarse);
            foreach (int shift in new[] { -300, -1, 37, 300 }) {
                var c = new DMesh3(m);
                double s = Math.Pow(2, shift);
                foreach (int v in c.VertexIndices()) c.SetVertex(v, s * c.GetVertex(v));
                MeshOffset r = Run(c, -0.25 * s, coarse);
                Assert.Equal(MeshOffset.Status.Success, r.ResultStatus);
                var back = new DMesh3(r.Result);
                foreach (int v in back.VertexIndices()) back.SetVertex(v, back.GetVertex(v) / s);
                Assert.Equal("", Invariance.Difference(r0.Result, back));
            }
        }

        [Fact]
        public void Offset_Reuse_ComputeTwice_AndAfterChangingTheDistance()
        {
            DMesh3 m = LShape();
            var o = new MeshOffset(m, 0.6);
            Assert.True(o.Compute());
            DMesh3 first = o.Result;
            Assert.True(o.Compute());
            Assert.Equal("", Invariance.Difference(first, o.Result));
            o.Distance = -0.6;
            Assert.True(o.Compute());
            Assert.Equal("", Invariance.Difference(Run(m, -0.6).Result, o.Result));
            Assert.True(o.BandCertified);
        }

        [Fact]
        public void Offset_ParallelEqualsSerial_AndRepeats()
        {
            DMesh3 m = Hollow();
            MeshOffset serial = Run(m, 0.25, x => { x.UseParallel = false; x.RelativeCellSize = 0.5; });
            for (int rep = 0; rep < 2; ++rep) {
                MeshOffset par = Run(m, 0.25, x => x.RelativeCellSize = 0.5);
                Assert.Equal("", Invariance.Difference(serial.Result, par.Result));
                Assert.Equal(serial.BandDeviation, par.BandDeviation);
            }
        }

        [Fact]
        public void CertifyBand_RejectsAMovedVertex_AndAWrongDistance()
        {
            DMesh3 m = LShape();
            MeshOffset o = Run(m, 0.6);
            AssertGood(o);
            double tol = 0.5 * o.UsedCellSize, dev; int unc;
            Assert.True(MeshOffset.CertifyBand(m, o.Result, 0.6, tol, out dev, out unc));
            Assert.True(dev <= tol && unc == 0);
            // a vertex pushed outward by twice the tolerance
            DMesh3 moved = new DMesh3(o.Result);
            int v0 = moved.VertexIndices().First();
            Vector3d n = moved.GetVertex(v0) - new Vector3d(2.5, 2.5, 2);
            moved.SetVertex(v0, moved.GetVertex(v0) + 2 * tol * n.Normalized);
            Assert.False(MeshOffset.CertifyBand(m, moved, 0.6, tol, out dev, out unc));
            Assert.True(unc > 0 && dev > tol);
            // the same surface against another distance (or the other side: the inset sign)
            Assert.False(MeshOffset.CertifyBand(m, o.Result, 0.6 + 3 * tol, tol, out dev, out unc));
            Assert.False(MeshOffset.CertifyBand(m, o.Result, -0.6, tol, out dev, out unc));
        }

        [Fact]
        public void CertifyBand_OfAnOtherSurface_TheSphereOfRadiusRPlusD()
        {
            // an analytic check of the certificate alone: a fine sphere mesh around a coarse closed sphere's offset is in the band
            DMesh3 input = Box(new Vector3d(-0.01, -0.01, -0.01), new Vector3d(0.01, 0.01, 0.01));
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 24 }.Generate().MakeDMesh();
            double dev; int unc;
            // the box is within 0.01 * sqrt 3 of its centre: sd on the unit sphere in [1 - 0.0174, 1]; chords add the sagitta
            Assert.True(MeshOffset.CertifyBand(input, sphere, 0.99, 0.04, out dev, out unc), $"{dev}");
            Assert.False(MeshOffset.CertifyBand(input, sphere, 0.9, 0.04, out dev, out unc));
        }

        [Fact]
        public void CertifyBand_RadiusCoversEveryCorner()
        {
            // one triangle whose first corner is near its centroid: the bound must see the far corners (sd = z above the box)
            DMesh3 input = Box(new Vector3d(-5, -5, -1), new Vector3d(5, 5, 0));
            var tri = new DMesh3();
            tri.AppendTriangle(tri.AppendVertex(new Vector3d(0, 0, 1)), tri.AppendVertex(new Vector3d(1, 0, 1.5)), tri.AppendVertex(new Vector3d(-1, 0.01, 0.5)));
            double dev; int unc;
            Assert.False(MeshOffset.CertifyBand(input, tri, 1, 0.3, out dev, out unc, maxDepth: 0));
            Assert.True(dev >= 0.5, $"{dev}");
            Assert.True(MeshOffset.CertifyBand(input, tri, 1, 0.6, out dev, out unc, maxDepth: 8), $"{dev}");
            Assert.InRange(dev, 0.5, 0.6);
        }

        [Fact]
        public void DistanceBounds_EncloseTheDistance_Tightly()
        {
            // the interval bounds of the certificate against the double distance, on random points and triangles
            var rnd = new Random(7);
            Func<Vector3d> r = () => new Vector3d(rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1, rnd.NextDouble() * 2 - 1);
            for (int i = 0; i < 2000; ++i) {
                var m = new DMesh3();
                m.AppendTriangle(m.AppendVertex(r()), m.AppendVertex(r()), m.AppendVertex(r()));
                Vector3d p = 3 * r();
                double dist = Math.Sqrt(MeshQueries.TriangleDistance(m, 0, p).DistanceSquared);
                double lo = MeshOffset.triangle_lower(m, 0, p), hi = MeshOffset.triangle_upper(m, 0, p);
                Assert.True(lo <= hi, $"{i}");
                Assert.True(Math.Abs(lo - dist) <= 1e-9 * (1 + dist) && Math.Abs(hi - dist) <= 1e-9 * (1 + dist), $"{i}: {lo} {dist} {hi}");
                AxisAlignedBox3d box = m.CachedBounds;
                double boxSq = MeshOffset.box_lower_sq(box, p);
                Assert.True(boxSq <= lo * lo * (1 + 1e-12), $"{i}");
                Assert.True(boxSq >= box.DistanceSquared(p) * (1 - 1e-9) - 1e-15, $"{i}");
            }
        }

        [Fact]
        public void InvalidInputs_AreRefused()
        {
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            Assert.Equal(MeshOffset.Status.InvalidInput, Run(new DMesh3(), 0.1).ResultStatus);
            Assert.Equal(MeshOffset.Status.InvalidParameters, Run(box, 0).ResultStatus);
            Assert.Equal(MeshOffset.Status.InvalidParameters, Run(box, double.NaN).ResultStatus);
            Assert.Equal(MeshOffset.Status.InvalidParameters, Run(box, 0.1, o => o.RelativeCellSize = 0.6).ResultStatus);
            Assert.Equal(MeshOffset.Status.InvalidParameters, Run(box, 0.1, o => o.CellSize = 0.06).ResultStatus);
            Assert.Equal(MeshOffset.Status.GridTooLarge, Run(box, 0.1, o => o.MaxGridNodes = 1000).ResultStatus);
            var open = new DMesh3(box);
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.Equal(MeshOffset.Status.NotClosed, Run(open, 0.1).ResultStatus);
            var flipped = new DMesh3(box);
            flipped.ReverseOrientation();
            Assert.Equal(MeshOffset.Status.InsideOut, Run(flipped, 0.1).ResultStatus);
            // an inset deeper than half the thickness leaves nothing
            MeshOffset empty = Run(box, -0.6);
            Assert.Equal(MeshOffset.Status.Empty, empty.ResultStatus);
            Assert.Null(empty.Result);
        }
    }
}
