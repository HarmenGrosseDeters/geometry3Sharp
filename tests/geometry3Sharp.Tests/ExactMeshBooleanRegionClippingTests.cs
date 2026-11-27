using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 2 of docs/design/COPLANAR_REGION_CLIPPING.md (#1630): with RegionClipping the flat edges of a region cut nothing and a piece is output from the stack's
    /// region of the lowest rank. The result is the same surface as without it (the exact difference is empty both ways) with another triangulation of coplanar clusters.
    /// </summary>
    public class ExactMeshBooleanRegionClippingTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(new Vector3d(x0, y0, z0), new Vector3d(x1, y1, z1))) }.Generate().MakeDMesh();
        }

        static DMesh3 FineBox(double x0, double y0, double z0, double x1, double y1, double z1, int n)
        {
            return new GridBox3Generator() { Box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2)), EdgeVertices = n }.Generate().MakeDMesh();
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                yield return new object[] { "cylinders 2 ulps", op };
                yield return new object[] { "cylinders 0.25", op };
                yield return new object[] { "cylinders 1e-9", op };
                yield return new object[] { "plate and box", op };
                yield return new object[] { "identical fine boxes", op };
                yield return new object[] { "fine boxes offset", op };
                yield return new object[] { "fine box inside a fine box face", op };
                yield return new object[] { "box on a box, the flat diagonal ends on its edges", op };
                yield return new object[] { "box on a box, the other way", op };
            }
        }

        static List<DMesh3> Inputs(string what)
        {
            DMesh3 c = ExactMeshBooleanCoplanarWalkTests.Cylinder(6);
            switch (what) {
                case "cylinders 2 ulps": return new List<DMesh3> { c, ExactMeshBooleanCoplanarWalkTests.Shifted(c, 0) };
                case "cylinders 0.25": return new List<DMesh3> { c, ExactMeshBooleanCoplanarWalkTests.Shifted(c, 0.25) };
                case "cylinders 1e-9": return new List<DMesh3> { c, ExactMeshBooleanCoplanarWalkTests.Shifted(c, 1e-9) };
                case "plate and box": return new List<DMesh3> { Box(0, 0, 0, 4, 4, 1), Box(0.7, 0.9, 0.5, 2.1, 2.3, 1) };
                case "identical fine boxes": return new List<DMesh3> { FineBox(0, 0, 0, 1, 1, 1, 4), FineBox(0, 0, 0, 1, 1, 1, 4) };
                case "fine boxes offset": return new List<DMesh3> { FineBox(0, 0, 0, 1, 1, 1, 5), FineBox(0.25, 0.25, 0.5, 1.25, 1.25, 1, 4) };
                case "box on a box, the flat diagonal ends on its edges": return new List<DMesh3> { Box(0, 0, 0, 2, 2, 1), Box(0.3, 0.6, 1, 1.3, 1.6, 2) };
                case "box on a box, the other way": return new List<DMesh3> { Box(0.3, 0.6, 1, 1.3, 1.6, 2), Box(0, 0, 0, 2, 2, 1) };
                default: return new List<DMesh3> { FineBox(0, 0, 0, 2, 2, 1, 5), FineBox(0.5, 0.5, 0.5, 1.5, 1.5, 1, 3) };
            }
        }

        static ExactMeshBoolean Compute(List<DMesh3> m, Ops op, bool regions)
        {
            var b = new ExactMeshBoolean(m, op) { RegionClipping = regions, RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(b.Compute(), b.Error);
            return b;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void RegionClipping_GivesTheSameSurface_AsTheOldRule(string what, Ops op)
        {
            List<DMesh3> m = Inputs(what);
            var old = Compute(m, op, false);
            var neu = Compute(m, op, true);
            if (old.Result.TriangleCount == 0) {
                Assert.Equal(0, neu.Result.TriangleCount);
                return;
            }
            Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
            // the same surface: the exact difference of the two results is empty both ways
            foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                Assert.True(d.Compute(), d.Error);
                Assert.Equal(0, d.Result.TriangleCount);
            }
            // the certifying checker certifies the decisions of the region rule
            BooleanCertificate c = BooleanCertificate.Run(m[0], m[1], op, out _, x => { x.RegionClipping = true; if (op == Ops.Intersection) x.CullOutsideA = false; });
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
        }

        // ---- three or more coplanar layers, and regions that overlap themselves (review of #1725)

        static DMesh3 Appended(DMesh3 a, DMesh3 b)
        {
            var r = new DMesh3(a);
            new MeshEditor(r).AppendMesh(b);
            return r;
        }

        /// <summary> a bent bar: an annular sector of 450 degrees in steps of 90 (so it overlaps itself exactly over the last step), extruded from z 0 to 1; the faces of the repeated step coincide with the first </summary>
        static DMesh3 Spiral()
        {
            var m = new DMesh3();
            double[] cx = { 1, 0, -1, 0, 1, 0 }, cy = { 0, 1, 0, -1, 0, 1 };
            int[] pb = new int[6], pt = new int[6], qb = new int[6], qt = new int[6];
            for (int j = 0; j < 6; ++j) {
                pb[j] = m.AppendVertex(new Vector3d(cx[j], cy[j], 0)); pt[j] = m.AppendVertex(new Vector3d(cx[j], cy[j], 1));
                qb[j] = m.AppendVertex(new Vector3d(2 * cx[j], 2 * cy[j], 0)); qt[j] = m.AppendVertex(new Vector3d(2 * cx[j], 2 * cy[j], 1));
            }
            void Quad(int a, int b, int c, int d) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
            for (int j = 0; j < 5; ++j) {
                Quad(pt[j], qt[j], qt[j + 1], pt[j + 1]);          // top (up)
                Quad(pb[j], pb[j + 1], qb[j + 1], qb[j]);          // bottom (down)
                Quad(qb[j], qb[j + 1], qt[j + 1], qt[j]);          // outer wall
                Quad(pb[j], pt[j], pt[j + 1], pb[j + 1]);          // inner wall
            }
            Quad(pb[0], qb[0], qt[0], pt[0]);                      // the end at the start
            Quad(pb[5], pt[5], qt[5], qb[5]);                      // the end at the finish
            return m;
        }

        public static IEnumerable<object[]> ReviewCases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                yield return new object[] { "three boxes (n-ary) A", op };
                yield return new object[] { "three boxes (n-ary) B", op };
                yield return new object[] { "two appended into one mesh, plus the third", op };
                yield return new object[] { "three fine boxes", op };
                yield return new object[] { "two fine boxes plus one", op };
                yield return new object[] { "self-overlapping spiral and a coplanar box", op };
            }
        }

        static List<DMesh3> ReviewInputs(string what)
        {
            switch (what) {
                case "three boxes (n-ary) A": return new List<DMesh3> { Box(0, 0, 0, 2, 2, 1), Box(0.5, 0.5, 0.5, 1.5, 1.5, 1), Box(0.2, 0.7, 0.4, 1.7, 1.3, 1) };
                case "three boxes (n-ary) B": return new List<DMesh3> { Box(0, 0, 0, 1, 1, 1), Box(0.25, 0.1, 0.5, 1.25, 1.2, 1), Box(-0.3, 0.2, 0.2, 0.6, 0.9, 1) };
                case "two appended into one mesh, plus the third": return new List<DMesh3> { Appended(Box(0, 0, 0, 1, 1, 1), Box(0.25, 0.1, 0.5, 1.25, 1.2, 1)), Box(-0.3, 0.2, 0.2, 0.6, 0.9, 1) };
                case "three fine boxes": return new List<DMesh3> { FineBox(0, 0, 0, 1, 1, 1, 4), FineBox(0.25, 0.1, 0.5, 1.25, 1.2, 1, 4), FineBox(-0.3, 0.2, 0.2, 0.6, 0.9, 1, 3) };
                case "two fine boxes plus one": return new List<DMesh3> { Appended(FineBox(0, 0, 0, 1, 1, 1, 4), FineBox(0.25, 0.1, 0.5, 1.25, 1.2, 1, 3)), FineBox(0.1, 0.3, 0.2, 0.9, 0.8, 1, 3) };
                default: return new List<DMesh3> { Spiral(), Box(-1.5, -0.5, 0.5, 0.5, 1.5, 1) };
            }
        }

        [Theory]
        [MemberData(nameof(ReviewCases))]
        public void RegionClipping_IsClosedAndCertified_WithThreeCoplanarLayersAndOverlappingRegions(string what, Ops op)
        {
            List<DMesh3> m = ReviewInputs(what);
            var old = new ExactMeshBoolean(m, op) { AllowOpenMeshes = true };
            Assert.True(old.Compute(), old.Error);
            var neu = new ExactMeshBoolean(m, op) { RegionClipping = true, AllowOpenMeshes = true, RecordCertificate = true, ComputeExactVertices = true, CullOutsideA = op != Ops.Intersection };
            Assert.True(neu.Compute(), neu.Error);
            Assert.Equal(old.Result.TriangleCount == 0, neu.Result.TriangleCount == 0);
            if (neu.Result.TriangleCount == 0) return;
            // closed and manifold (the first assert: the review's open results), and the reason is the old rule's
            Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result, false) ?? "");
            Assert.Equal(MeshRobustRepair.NotCleanReason(old.Result) ?? "", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
            BooleanCertificate c = BooleanCertificate.From(neu);
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
            if ((MeshRobustRepair.NotCleanReason(neu.Result) ?? "") != "") return;      // (a result with self-intersections is no input of the difference)
            foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                Assert.True(d.Compute(), d.Error);
                Assert.Equal(0, d.Result.TriangleCount);
            }
        }

        [Fact]
        public void ARegionThatOverlapsItself_IsCountedPerTriangle_ByAPredicateThatReadsTheExactWinding()
        {
            // the spiral has winding number 2 where it overlaps itself: the region of its top face (the coplanar box on it is a second mesh) must give 2, not 1
            var m = new List<DMesh3> { Spiral(), Box(-1.5, -0.5, 0.5, 0.5, 1.5, 1) };
            Func<int[], bool> twice = w => w[0] == 2;
            var old = new ExactMeshBoolean(m, Ops.Union) { WindingPredicate = twice, AllowOpenMeshes = true };
            var neu = new ExactMeshBoolean(m, Ops.Union) { WindingPredicate = twice, AllowOpenMeshes = true, RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(old.Compute(), old.Error);
            Assert.True(neu.Compute(), neu.Error);
            Assert.True(neu.Result.TriangleCount > 0);
            Assert.Equal(MeshRobustRepair.NotCleanReason(old.Result, false) ?? "", MeshRobustRepair.NotCleanReason(neu.Result, false) ?? "");
            BooleanCertificate c = BooleanCertificate.From(neu);
            Assert.Equal("", c.Check());
            foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                if (d.Compute()) Assert.Equal(0, d.Result.TriangleCount);
            }
        }

        [Fact]
        public void ARegionWithACulledTriangle_IsClippedAsBefore_UnderTheDefaultCulling()
        {
            // (review of #1725) the early-out of the Difference culls triangles of the first mesh: their boundary edges are never paired
            var m = new List<DMesh3> { FineBox(1, 1, 0.2, 2.5, 2.25, 1, 3), FineBox(1, 0.5, 0.2, 1.5, 1, 1, 5), FineBox(1, 0.5, 1, 2.5, 1.5, 1.5, 3) };
            var old = new ExactMeshBoolean(m, Ops.Difference);
            var neu = new ExactMeshBoolean(m, Ops.Difference) { RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(old.Compute(), old.Error);
            Assert.True(neu.Compute(), neu.Error);
            Assert.True(neu.Arrangement.CulledCount > 0, "the case must cull");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
            BooleanCertificate c = BooleanCertificate.From(neu);
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
            foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                Assert.True(d.Compute(), d.Error);
                Assert.Equal(0, d.Result.TriangleCount);
            }
        }

        [Fact]
        public void AFlatEdgeEndingOnAConstraint_GivesTheTriangleThatPoint()
        {
            // (review of #1725, rule (b) of the ghosts) the end of a flat clip on the interior of a constraint segment of another layer
            var m = new List<DMesh3> { Box(0, 0, 0, 3, 3, 1), Box(0, 0, 0.5, 2, 2, 1), FineBox(1, 0, 0.2, 3, 2, 1, 3) };
            var old = new ExactMeshBoolean(m, Ops.Intersection) { CullOutsideA = false };
            var neu = new ExactMeshBoolean(m, Ops.Intersection) { CullOutsideA = false, RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(old.Compute(), old.Error);
            Assert.True(neu.Compute(), neu.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
            BooleanCertificate c = BooleanCertificate.From(neu);
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
            foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                Assert.True(d.Compute(), d.Error);
                Assert.Equal(0, d.Result.TriangleCount);
            }
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void ARegionThatOverlapsItself_GetsTheGhostPointsOfAnotherMeshsEmbeddedRegion(Ops op)
        {
            // the spiral's top faces overlap themselves (identical triangles: not embedded, not culled); a fine box on its top gives it ghosts, its flat edges crossing the spiral's sides
            var m = new List<DMesh3> { Spiral(), FineBox(-1.5, -0.5, 0.5, 0.5, 1.5, 1, 4) };
            var old = new ExactMeshBoolean(m, op) { AllowOpenMeshes = true, CullOutsideA = false, RecordCertificate = true, ComputeExactVertices = true };
            var neu = new ExactMeshBoolean(m, op) { AllowOpenMeshes = true, CullOutsideA = false, RegionClipping = true, RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(old.Compute(), old.Error);
            Assert.True(neu.Compute(), neu.Error);
            Assert.Equal(old.Result.TriangleCount == 0, neu.Result.TriangleCount == 0);
            if (neu.Result.TriangleCount == 0) return;
            // (the rounding of the result may remove a part or leave triangles outside the delta, as in the old rule: the same reason and the same count)
            Assert.Equal(MeshRobustRepair.NotCleanReason(old.Result, false) ?? "", MeshRobustRepair.NotCleanReason(neu.Result, false) ?? "");
            BooleanCertificate co = BooleanCertificate.From(old), c = BooleanCertificate.From(neu);
            Assert.Equal(co.Check(), c.Check());
            Assert.Equal(co.Uncovered, c.Uncovered);
        }

        // ---- the pieces grow linearly

        static int PiecesOf(int K, double dx, bool regions)
        {
            DMesh3 a = ExactMeshBooleanCoplanarWalkTests.Cylinder(K);
            var b = new ExactMeshBoolean(new List<DMesh3> { a, ExactMeshBooleanCoplanarWalkTests.Shifted(a, dx) }, Ops.Union) { RegionClipping = regions };
            Assert.True(b.Compute(), b.Error);
            return b.Arrangement.Output.Count;
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.25)]
        public void RegionClipping_MakesThePiecesOfShiftedFansGrowLinearly(double dx)
        {
            int p8 = PiecesOf(8, dx, true), p16 = PiecesOf(16, dx, true), p32 = PiecesOf(32, dx, true);
            Assert.True(p16 <= 2.3 * p8 && p32 <= 2.3 * p16, "pieces " + p8 + ", " + p16 + ", " + p32);
            Assert.True(PiecesOf(16, dx, false) > 4 * p16, "the old rule: " + PiecesOf(16, dx, false) + ", the region rule " + p16);
        }

        // ---- a centroid on a flat edge

        [Theory]
        [InlineData(Ops.Union, false)]
        [InlineData(Ops.Union, true)]
        [InlineData(Ops.Difference, false)]
        [InlineData(Ops.Difference, true)]
        [InlineData(Ops.Intersection, false)]
        public void ACentroidOnAFlatEdge_IsOneCrossingOfTheRegion(Ops op, bool swap)
        {
            // a box whose top face is two triangles with the flat diagonal x = y, and a smaller box on it whose bottom face is two triangles with the other diagonal: the
            // bottom triangle with the corner (0.5, 0.5) is symmetric about x = y, which cuts it from that corner to the middle of its flat edge (a vertex of neither
            // piece structure), so its only piece has its centroid on the diagonal, in both triangles of the top face
            DMesh3 box = Box(0, 0, 0, 2, 2, 1);
            DMesh3 small = Box(0.5, 0.5, 1, 1.5, 1.5, 2);
            bool onDiagonal = false;
            foreach (int e in small.EdgeIndices()) {
                Index2i ev = small.GetEdgeV(e);
                Vector3d p = small.GetVertex(ev.a), q = small.GetVertex(ev.b);
                if (p.z == 1 && q.z == 1 && p.x == 0.5 && p.y == 0.5 && q.x == 1.5 && q.y == 1.5 || p.z == 1 && q.z == 1 && q.x == 0.5 && q.y == 0.5 && p.x == 1.5 && p.y == 1.5) onDiagonal = true;
            }
            if (onDiagonal)
                foreach (int v in small.VertexIndices().ToList()) { Vector3d p = small.GetVertex(v); small.SetVertex(v, new Vector3d(2 - p.y, p.x, p.z)); }       // (a quarter turn about the middle: the other diagonal)
            var m = swap ? new List<DMesh3> { small, box } : new List<DMesh3> { box, small };
            ExactMeshBoolean old = Compute(m, op, false), neu = Compute(m, op, true);
            Assert.Equal(old.Result.TriangleCount == 0, neu.Result.TriangleCount == 0);
            if (old.Result.TriangleCount > 0) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
                foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                    Assert.True(d.Compute(), d.Error);
                    Assert.Equal(0, d.Result.TriangleCount);
                }
            }
            BooleanCertificate c = BooleanCertificate.Run(m[0], m[1], op, out _, x => { x.RegionClipping = true; if (op == Ops.Intersection) x.CullOutsideA = false; });
            Assert.Equal("", c.Check());
        }

        [Fact]
        public void ACentroidOnAFlatEdge_IsCountedOnce_ByAPredicateThatReadsTheExactWinding()
        {
            // the box top under the small box's bottom face: its centroid is on the flat edge of the bottom region, in both its triangles; the predicate
            // "inside the first and with winding number exactly 0 in the second" tells a crossing counted twice (-1 behind it) from one
            DMesh3 box = Box(0, 0, 0, 2, 2, 1), small = Box(0.5, 0.5, 1, 1.5, 1.5, 2);
            foreach (bool swap in new[] { false, true }) {
                var m = swap ? new List<DMesh3> { small, box } : new List<DMesh3> { box, small };
                Func<int[], bool> inBoxOnly = swap ? (w => w[0] == 0 && w[1] > 0) : (Func<int[], bool>)(w => w[1] == 0 && w[0] > 0);
                var old = new ExactMeshBoolean(m, Ops.Union) { WindingPredicate = inBoxOnly };
                var neu = new ExactMeshBoolean(m, Ops.Union) { WindingPredicate = inBoxOnly, RegionClipping = true };
                Assert.True(old.Compute(), old.Error);
                Assert.True(neu.Compute(), neu.Error);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
                foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                    Assert.True(d.Compute(), d.Error);
                    Assert.Equal(0, d.Result.TriangleCount);
                }
            }
        }

        // ---- invariance, reuse, determinism

        static DMesh3 Moved(DMesh3 m, Similarity sim)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices().ToList()) c.SetVertex(v, sim.Apply(c.GetVertex(v)));
            return c;
        }

        static string Pieces(ExactMeshBoolean b, double scale = 1)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ot in b.Arrangement.Output) sb.Append(ot.Mesh).Append(',').Append(ot.Source).Append(',').Append(ot.A).Append(',').Append(ot.B).Append(',').Append(ot.C).Append(';');
            sb.Append('|');
            foreach (List<int> st in b.StacksPerPiece) sb.Append(st == null ? "-" : string.Join(",", st)).Append(';');
            sb.Append('|');
            for (int i = 0; i < b.CertificateKeep.Length; ++i) sb.Append(b.CertificateKeep[i] ? 'k' : '_').Append(b.CertificateFlip[i] ? 'f' : '_');
            sb.Append('|');
            foreach (int v in b.Result.VertexIndices()) {
                Vector3d p = b.Result.GetVertex(v);
                sb.Append(BitConverter.DoubleToInt64Bits(p.x / scale)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y / scale)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z / scale)).Append(';');
            }
            foreach (int t in b.Result.TriangleIndices()) { Index3i f = b.Result.GetTriangle(t); sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';'); }
            return sb.ToString();
        }

        static ExactMeshBoolean Run(List<DMesh3> m, Ops op, bool serial)
        {
            var b = new ExactMeshBoolean(m, op) { RegionClipping = true, RecordStacks = true, RecordCertificate = true };
            gParallel.SerialOnThisThread = serial;
            try { Assert.True(b.Compute(), b.Error); } finally { gParallel.SerialOnThisThread = false; }
            return b;
        }

        [Theory]
        [InlineData("cylinders 2 ulps", Ops.Union)]
        [InlineData("cylinders 0.25", Ops.Difference)]
        [InlineData("fine boxes offset", Ops.Union)]
        public void RegionClipping_IsInvariantUnderTheSimilarities(string what, Ops op)
        {
            List<DMesh3> m = Inputs(what);
            string identity = Pieces(Run(m, op, true));
            foreach (Similarity sim in Similarity.All()) {
                List<DMesh3> t = m.Select(x => Moved(x, sim)).ToList();
                if (sim.IsExact) {
                    // the 2^k scales: the same pieces, stacks, decisions and triangles, the coordinates scaled bit for bit
                    Assert.True(identity == Pieces(Run(t, op, true), sim.Scale), sim.Name);
                    continue;
                }
                // the others round the inputs: the result is a clean mesh of the same surface as the old rule gives for the same inputs
                var neu = Run(t, op, true);
                var old = new ExactMeshBoolean(t, op);
                Assert.True(old.Compute(), old.Error);
                Assert.Equal(old.Result.TriangleCount == 0, neu.Result.TriangleCount == 0);
                if (neu.Result.TriangleCount == 0) continue;
                // (the rounded result is as clean as the old rule's: the reason, if any, is the same)
                Assert.Equal(MeshRobustRepair.NotCleanReason(old.Result) ?? "", MeshRobustRepair.NotCleanReason(neu.Result) ?? "");
                foreach (var pair in new[] { new[] { old.Result, neu.Result }, new[] { neu.Result, old.Result } }) {
                    var d = new ExactMeshBoolean(new List<DMesh3> { new DMesh3(pair[0]), new DMesh3(pair[1]) }, Ops.Difference);
                    if (d.Compute() == false) continue;      // (a result with self-intersections as input)
                    Assert.Equal(0, d.Result.TriangleCount);
                }
            }
        }

        [Theory]
        [InlineData("cylinders 2 ulps")]
        [InlineData("plate and box")]
        public void RegionClipping_ReusesAnInstance_AndIsTheSameInParallelAndSerial_AndWhenRepeated(string what)
        {
            List<DMesh3> m = Inputs(what);
            string serial = Pieces(Run(m, Ops.Union, true));
            // (the result's vertices and triangles; the ids of the points inside follow the thread order)
            for (int rep = 0; rep < 3; ++rep) {
                string par = Pieces(Run(m, Ops.Union, false));
                Assert.True(serial.Substring(serial.LastIndexOf('|')) == par.Substring(par.LastIndexOf('|')), "parallel run " + rep);
            }
            var b = new ExactMeshBoolean(m, Ops.Union) { RegionClipping = true, RecordStacks = true, RecordCertificate = true };
            gParallel.SerialOnThisThread = true;
            try {
                Assert.True(b.Compute(), b.Error);
                string first = Pieces(b);
                Assert.True(b.Compute(), b.Error);
                Assert.True(first == Pieces(b), "the second Compute of one instance differs");
                Assert.True(first == serial);
            } finally { gParallel.SerialOnThisThread = false; }
        }
    }
}
