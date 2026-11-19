using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Failure and fallback branches of ContourLoftStack (ContourLoftStackRun.cs) that no other test reaches: a slice that cannot be triangulated,
    /// a contour that ends with a hole that continues, the roof of a contained group rejected by its mesher or failing the assembly, caps the
    /// mesher fails or moves, and the tie of two overlap pieces that start at the same vertex.
    /// </summary>
    public class CoverageGate2BContourLoftStackTests
    {
        static List<Vector3d> Disc(int n, double r, double z, double cx = 0, double cy = 0) => ContourLoftStackHolesTests.Disc(n, r, z, cx, cy);
        static IList<IList<IList<Vector3d>>> Slices(params IList<Vector3d>[][] s) => ContourLoftStackHolesTests.Slices(s);
        static IList<Vector3d>[] Sl(params IList<Vector3d>[] c) => c;

        static List<Vector3d> Poly(double z, params double[] xy)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < xy.Length; i += 2) c.Add(new Vector3d(xy[i], xy[i + 1], z));
            return c;
        }

        static ContourLoftStackResult Run(ContourLoftStack options, IList<IList<IList<Vector3d>>> slices, bool roofs) =>
            new ContourLoftStackRun(options, slices, CancellationToken.None, roofs).Execute();

        // ---- the slice triangulation (ContourLoftStackRun.Triangulate) ----

        [Fact]
        public void ASliceOfASliverContour_IsNotTriangulated_AndReportedAsInvalidInput()
        {
            // 1 x 1e-15: the contour has an area in the working frame, but its two long sides are within the automatic merge tolerance
            // (2^-41 of the extent) of each other, the points merge and the triangulation is empty
            var sliver = Poly(0, 0, 0, 1, 0, 1, 1e-15, 0, 1e-15);
            var above = sliver.Select(p => new Vector3d(p.x, p.y, 1)).ToList();
            var r = new ContourLoftStack().Compute(Slices(Sl(sliver), Sl(above)));
            Assert.Equal(ContourLoftStackStatus.InvalidInput, r.Status);
            Assert.StartsWith("the contours could not be triangulated: Empty", r.Reason);
            Assert.Null(r.Mesh);
            Assert.Empty(r.Groups);
        }

        // ---- a contour that ends (CheckTerminals) ----

        [Fact]
        public void AContourThatEnds_WithAHoleThatContinues_IsUnsupported()
        {
            // the ring (outer 3, hole 1) has no counterpart above (the disc 0.9 with its hole 0.3 lies inside its hole), but its hole continues
            // into the hole of that disc
            var slices = Slices(Sl(Disc(16, 3, 0), Disc(16, 1, 0)), Sl(Disc(16, 0.9, 1), Disc(12, 0.3, 1)));
            foreach (var s in new[] { slices, Slices(Sl(Disc(16, 3, 0), Disc(16, 1, 0)), Sl(Disc(16, 0.9, 1), Disc(12, 0.3, 1)), Sl(Disc(16, 0.9, 2), Disc(12, 0.3, 2))) }) {
                var r = new ContourLoftStack { CapEnds = false }.Compute(s);
                Assert.Equal(ContourLoftStackStatus.Unsupported, r.Status);
                Assert.Equal("a contour that ends has a child that continues (contour 0 of slice 0)", r.Reason);
                Assert.Null(r.Mesh);
            }
            // with caps the ring is reported first, as a contour without thickness
            var capped = new ContourLoftStack { CapEnds = true }.Compute(slices);
            Assert.Equal(ContourLoftStackStatus.Unsupported, capped.Status);
            Assert.Contains("zero thickness", capped.Reason);
        }

        [Fact]
        public void ADiscThatStarts_WithAHoleThatContinuesDown_IsUnsupported()
        {
            // the mirror image: the ring is on top
            var slices = Slices(Sl(Disc(16, 0.9, 0), Disc(12, 0.3, 0)), Sl(Disc(16, 3, 1), Disc(16, 1, 1)));
            var r = new ContourLoftStack { CapEnds = false }.Compute(slices);
            Assert.Equal(ContourLoftStackStatus.Unsupported, r.Status);
            Assert.Equal("a contour that ends has a child that continues (contour 0 of slice 0)", r.Reason);
        }

        // ---- the caps (AddCap) ----

        [Fact]
        public void ACapMesherThatCollapsesTheCap_GivesCapFailed()
        {
            // a snap tolerance far above the size of the disc: the cap has no area left
            var slices = Slices(Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 1)));
            var r = new ContourLoftStack { CapEnds = true, CapMesher = new QualityMesh2 { MinAngle = 0, SnapTolerance = 10 } }.Compute(slices);
            Assert.Equal(ContourLoftStackStatus.CapFailed, r.Status);
            Assert.StartsWith("the cap mesher failed: Empty", r.Reason);
            Assert.Null(r.Mesh);
        }

        [Fact]
        public void ACapMesherThatMovesAContourVertex_GivesCapFailed()
        {
            // the vertices of the octagon are rounded to the grid of 0.3 and move
            var slices = Slices(Sl(Disc(8, 1, 0)), Sl(Disc(8, 1, 1)));
            var r = new ContourLoftStack { CapEnds = true, CapMesher = new QualityMesh2 { MinAngle = 0, SnapTolerance = 0.3 } }.Compute(slices);
            Assert.Equal(ContourLoftStackStatus.CapFailed, r.Status);
            Assert.Equal("the cap moved or merged a contour vertex", r.Reason);
            Assert.Null(r.Mesh);
        }

        // ---- the roofs of LoweredSaddle ----

        static IList<IList<IList<Vector3d>>> OneToTwo() => Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2)));
        static IList<IList<IList<Vector3d>>> OneToTwoToOne() => Slices(Sl(Disc(48, 4, 0)), Sl(Disc(24, 1.6, 1, -2.2), Disc(24, 1.6, 1, 2.2)), Sl(Disc(48, 4, 2)));

        [Fact]
        public void ARoofMesherThatRejectsItsInput_LeavesTheGroupToTheBridgedComposite()
        {
            // a minimum angle of 75 degrees is an invalid option of the mesher (ArgumentException): the roof is not built, in the same run
            var bad = new ContourLoftStack { CapEnds = true, LoweredSaddle = true, SaddleMesher = new QualityMesh2 { MinAngle = 75 } };
            var withRoof = Run(new ContourLoftStack { CapEnds = true, LoweredSaddle = true }, OneToTwo(), roofs: true);
            var rejected = Run(bad, OneToTwo(), roofs: true);
            var composite = Run(new ContourLoftStack { CapEnds = true }, OneToTwo(), roofs: false);
            Assert.Equal(ContourLoftStackStatus.Success, withRoof.Status);
            Assert.Equal(ContourLoftStackStatus.Success, rejected.Status);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(rejected.Mesh));
            Assert.Equal(ContourLoftStackHolesTests.Digest(composite.Mesh), ContourLoftStackHolesTests.Digest(rejected.Mesh));
            Assert.NotEqual(ContourLoftStackHolesTests.Digest(composite.Mesh), ContourLoftStackHolesTests.Digest(withRoof.Mesh));
            // the public operation gives the same
            Assert.Equal(ContourLoftStackHolesTests.Digest(composite.Mesh), ContourLoftStackHolesTests.Digest(bad.Compute(OneToTwo()).Mesh));
        }

        [Fact]
        public void TwoRoofsThatShareAChord_DoNotFormAManifold_AndTheStackIsBuiltWithoutThem()
        {
            // the roofs of the two slabs hold the same chord of the middle slice's gap: the second triangle does not fit
            var options = new ContourLoftStack { CapEnds = true, LoweredSaddle = true, SaddleMesher = new QualityMesh2 { MinAngle = 0 } };
            var roofs = Run(options, OneToTwoToOne(), roofs: true);
            Assert.Equal(ContourLoftStackStatus.BandFailed, roofs.Status);
            Assert.Equal("the pieces do not form a manifold (a triangle does not fit)", roofs.Reason);
            Assert.Null(roofs.Mesh);
            // the caller builds the stack again without them
            var r = options.Compute(OneToTwoToOne());
            var plain = new ContourLoftStack { CapEnds = true }.Compute(OneToTwoToOne());
            Assert.Equal(ContourLoftStackStatus.Success, r.Status);
            Assert.Equal(ContourLoftStackHolesTests.Digest(plain.Mesh), ContourLoftStackHolesTests.Digest(r.Mesh));
        }

        // ---- two pieces of one overlap that start at the same vertex (the order of equal keys) ----

        [Fact]
        public void TwoOverlapPiecesOfOnePair_ThatStartAtTheSameVertex_KeepTheirOrder()
        {
            // the L-shaped lower contour has its reflex vertex p = (1, 1); the upper quadrilateral has a vertex at p whose wedge holds the missing
            // quadrant: the overlap of the two is two triangles that meet at p, both with the smallest key {input vertex 0}. The tie keeps the
            // order in which the pieces are found. The surface touches itself at p (the pieces lie in one intermediate plane), so the check is off
            var lower = Poly(0, 1, 1, 1, 2, 0, 2, 0, 0, 2, 0, 2, 1);
            var upperA = Poly(1, 1, 1, 1.8, 0.5, 1.9, 1.9, 0.5, 1.8);
            var upperB = Poly(1, 0.2, 0.2, 0.7, 0.2, 0.2, 0.7);
            var upperC = Poly(1, 0.1, 1.3, 0.4, 1.3, 0.1, 1.6);
            var pieceX = new[] { new Vector3d(1, 1, 0.5), new Vector3d(1, 1.0 + 0.8357142857142857, 0.5), new Vector3d(0.5, 1.8, 0.5) };       // the piece in x < 1
            var pieceY = new[] { new Vector3d(1, 1, 0.5), new Vector3d(1.8, 0.5, 0.5), new Vector3d(1.0 + 0.8357142857142857, 1, 0.5) };       // the piece in y < 1
            var overlapB = new[] { new Vector3d(0.2, 0.2, 0.5), new Vector3d(0.7, 0.2, 0.5), new Vector3d(0.2, 0.7, 0.5) };
            var overlapC = new[] { new Vector3d(0.1, 1.3, 0.5), new Vector3d(0.4, 1.3, 0.5), new Vector3d(0.1, 1.6, 0.5) };
            // two upper contours besides the first: three pieces (the sort of three elements swaps a pair that compares greater), and three: four
            // pieces (an insertion sort moves an element that compares less); a wrong tie in either direction changes the order
            foreach (bool withC in new[] { false, true }) {
                var slices = withC ? Slices(Sl(lower), Sl(upperA, upperB, upperC)) : Slices(Sl(lower), Sl(upperA, upperB));
                var r = new ContourLoftStack { CapEnds = false, CheckSelfIntersections = false }.Compute(slices);
                Assert.Equal(ContourLoftStackStatus.Success, r.Status);
                Assert.Equal(new[] { ContourLoftGroupKind.Branch }, r.Groups.Select(g => g.Kind).ToArray());
                int input = 6 + 4 + 3 + (withC ? 3 : 0);
                var expected = pieceX.Concat(pieceY).Concat(overlapB).Concat(withC ? overlapC : new Vector3d[0]).ToArray();
                Assert.Equal(input + expected.Length, r.Mesh.VertexCount);
                for (int i = 0; i < expected.Length; ++i) {
                    Vector3d v = r.Mesh.GetVertex(input + i);
                    Assert.True((v - expected[i]).Length < 1e-12, "withC " + withC + ": vertex " + (input + i) + " is " + v + ", expected " + expected[i]);
                }
            }
            // with the check on, the touching pieces are reported
            var checkedRun = new ContourLoftStack { CapEnds = false }.Compute(Slices(Sl(lower), Sl(upperA, upperB)));
            Assert.Equal(ContourLoftStackStatus.SelfIntersecting, checkedRun.Status);
        }
    }
}
