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
    /// #1587: a piece next to a boundary segment of an open mesh on the Antipodal path. The value there was undecided (the
    /// segment test 1 + a.b &lt;= tol is quadratic in the angle, so it fired up to about u^(1/3) of the segment's length
    /// away, and a bound above 1e-6 gave another ray, 20 times), and undecided was 0.5, read as outside: the piece was
    /// dropped and Compute() returned true. Now the test is linear in the angle (|a + b| &lt;= tol,
    /// tools/Z3Pilot/antipodal_segment.py), an undecided value is evaluated again with a wide bound that is taken only
    /// where it decides the threshold, and where that does not decide either Compute() fails (Status OpenInput, Error
    /// names the count). Procedural inputs only.
    /// </summary>
    public class ExactMeshBooleanTestsIssue1587
    {
        // an open unit box: GridBox3Generator with 4 edge vertices (12 boundary segments of length 1/3), top face (z = 1) removed
        static DMesh3 OpenUnitBox()
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)), EdgeVertices = 4 }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 1 - 1e-9)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        // a closed box of side d centred at (1 - 1.5 d, 0.5, 1 - 1.5 d): inside the open box, about 2 d from its open top edge
        // x = 1, z = 1, where the generalized winding number of the open box is about 5/8 (inside)
        static DMesh3 SmallBox(double d)
        {
            var c = new Vector3d(1 - 1.5 * d, 0.5, 1 - 1.5 * d);
            return new GridBox3Generator() { Box = new Box3d(c, new Vector3d(d / 2, d / 2, d / 2)), EdgeVertices = 2 }.Generate().MakeDMesh();
        }

        static ExactMeshBoolean New(DMesh3 a, DMesh3 b, Ops op = Ops.Intersection, int maxSegments = 20000, bool perPatch = true, bool traced = true)
        {
            return new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op) {
                AllowOpenMeshes = true, AntipodalMaxBoundarySegments = maxSegments, PerPatchOpenWinding = perPatch, TracedOpenWinding = traced
            };
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op = Ops.Intersection, int maxSegments = 20000, bool perPatch = true, bool traced = true)
        {
            ExactMeshBoolean r = New(a, b, op, maxSegments, perPatch, traced);
            r.Compute();
            return r;
        }

        // the success path: the small box is the result, closed, manifold, finite, with its volume
        static void AssertSmallBoxKept(ExactMeshBoolean r, DMesh3 b, string what)
        {
            Assert.True(r.Status == ExactMeshBoolean.BooleanStatus.Success, $"{what}: {r.Status} {r.Error}");
            Assert.True(r.Result.TriangleCount > 0, $"{what}: empty result (the small box was dropped), undecided {r.Counts["antipodal_undecided"]}");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Result, true));
            Assert.True(Invariance.AllFinite(r.Result), what);
            double v = Invariance.Volume(r.Result), expected = Invariance.Volume(b);
            Assert.True(Math.Abs(v - expected) <= 1e-6 * expected, $"{what}: volume {v:R}, expected {expected:R}");
            Assert.Equal(0L, r.Counts["antipodal_unresolved"]);
        }

        public static IEnumerable<object[]> Sides() => new[] { 1e-6, 1e-7, 3e-8, 1e-8, 1e-9, 3e-10, 1e-10, 1e-11, 1e-12 }.Select(d => new object[] { d });

        // the repro of the issue over the sides: on master every side from 1e-8 down was an empty result with Compute() true
        [Theory]
        [MemberData(nameof(Sides))]
        public void NextToTheOpenEdge_SmallBoxKept_OnTheAntipodalPath(double d)
        {
            DMesh3 a = OpenUnitBox(), b = SmallBox(d);
            ExactMeshBoolean r = Run(a, b);
            Assert.Equal(1L, r.Counts["antipodal_meshes"]);
            AssertSmallBoxKept(r, b, $"d = {d:R}");
        }

        // which route decides: the linear segment test alone down to about 1e-8 (no undecided value); below, the wide bound
        // (every undecided value decided by it). Pins that the wide evaluation is what keeps the box at 1e-10 and 1e-12
        [Theory]
        [InlineData(1e-8, false)]
        [InlineData(1e-10, true)]
        [InlineData(1e-12, true)]
        public void NextToTheOpenEdge_DecidedByTheLinearTestOrByTheWideBound(double d, bool wide)
        {
            ExactMeshBoolean r = Run(OpenUnitBox(), SmallBox(d));
            long undecided = r.Counts["antipodal_undecided"];
            Assert.Equal(wide ? undecided : 0, r.Counts["antipodal_wide"]);
            Assert.True(wide ? undecided > 0 : undecided == 0, $"undecided {undecided}");
        }

        // the per-patch and traced levers and the plain per-piece evaluation give the same result, and the fast winding
        // numbers (the other route of an open mesh, AntipodalMaxBoundarySegments = 0) too, in all three operations
        [Theory]
        [InlineData(1e-8)]
        [InlineData(1e-11)]
        public void NextToTheOpenEdge_SameResultOnEveryRoute_AndAsFastWinding(double d)
        {
            DMesh3 a = OpenUnitBox(), b = SmallBox(d);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                ExactMeshBoolean levers = Run(a, b, op), plain = Run(a, b, op, perPatch: false, traced: false), fw = Run(a, b, op, 0);
                Assert.Equal(0L, fw.Counts["antipodal_meshes"]);
                foreach (ExactMeshBoolean r in new[] { levers, plain, fw })
                    Assert.True(r.Status == ExactMeshBoolean.BooleanStatus.Success, $"{op}: {r.Status} {r.Error}");
                if (op == Ops.Intersection)
                    AssertSmallBoxKept(levers, b, "levers");
                Assert.Equal("", Invariance.GeometricDifference(levers.Result, plain.Result));
                Assert.Equal("", Invariance.GeometricDifference(levers.Result, fw.Result));
            }
        }

        // within the rounding error of the open edge (2e-14 away) no evaluation decides: Compute() fails with OpenInput and a
        // count, never an empty or partial result that reads the pieces as outside (master: Success, the box dropped)
        [Theory]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Union)]
        public void AtTheRoundingErrorOfTheOpenEdge_FailsWithOpenInput_NotSilently(Ops op)
        {
            ExactMeshBoolean r = New(OpenUnitBox(), SmallBox(1e-14), op);
            Assert.False(r.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, r.Status);
            Assert.Null(r.Result);
            long unresolved = r.Counts["antipodal_unresolved"];
            Assert.True(unresolved > 0);
            Assert.True(unresolved <= r.Counts["antipodal_undecided"]);
            Assert.Contains("undecided at " + unresolved + " evaluations", r.Error);
            Assert.Contains("antipodal_unresolved", r.Error);
        }

        static DMesh3 ClosedBox(Vector3d min, Vector3d max, int edgeVertices)
        {
            return new GridBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)), EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        // a sliver on the open rim: the box's top face (z = 1) reaches 1e-15 past the open box's wall x = 1, so the wall cuts
        // it into pieces whose centroids are about 3e-16 from the rim: on the segment within the rounding (status 1), which no
        // ray and no wider bound decides. With 4 edge vertices the slivers are reached by L2's tracing (status 1 at the exact
        // centroid of a traced piece: the piece's own evaluation, not 0.5), with 2 by their own evaluations
        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void SliverOnTheRim_OnTheSegmentWithinRounding_FailsWithOpenInput(int edgeVertices)
        {
            ExactMeshBoolean r = New(OpenUnitBox(), ClosedBox(new Vector3d(0.5, 0.4, 0.5), new Vector3d(1 + 1e-15, 0.6, 1), edgeVertices));
            Assert.False(r.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, r.Status);
            Assert.True(r.Counts["antipodal_unresolved"] > 0);
            Assert.Equal(0L, r.Counts["antipodal_wide"]);
        }

        // an open flat sheet (z = 0, the unit square, normals +z) and a box whose bottom face lies on it and runs 2e-11 short of
        // the sheet's edge x = 1 on the sheet and past it: the piece of the face on the sheet, 2e-11 wide, has the exact value
        // 1/2 in front of the sheet (a tie), but its centroid is too close to the edge for a bound of 1e-6, and the wide bound
        // is no snap: a value within it of 1/2 is undecided, never taken as inside or outside by its rounding
        [Fact]
        public void TieNextToTheEdgeOfAFlatSheet_IsNotDecidedByTheWideBound()
        {
            var sheet = new DMesh3();
            int v0 = sheet.AppendVertex(new Vector3d(0, 0, 0)), v1 = sheet.AppendVertex(new Vector3d(1, 0, 0));
            int v2 = sheet.AppendVertex(new Vector3d(1, 1, 0)), v3 = sheet.AppendVertex(new Vector3d(0, 1, 0));
            sheet.AppendTriangle(v0, v1, v2);
            sheet.AppendTriangle(v0, v2, v3);
            ExactMeshBoolean r = New(sheet, ClosedBox(new Vector3d(1 - 2e-11, 0.4, 0), new Vector3d(1.1, 0.6, 0.1), 2), Ops.Union);
            Assert.False(r.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, r.Status);
            Assert.True(r.Counts["antipodal_unresolved"] > 0);
        }

        // invariance (CLAUDE.md): in every frame of Similarity.All() whose resolution is far below the side the box is kept,
        // and at the 2^k scales the result is the same bits, mapped back
        [Theory]
        [InlineData(1e-8)]
        [InlineData(1e-11)]
        public void NextToTheOpenEdge_UnderSimilarity(double d)
        {
            DMesh3 a = OpenUnitBox(), b = SmallBox(d);
            ExactMeshBoolean plain = Run(a, b);
            AssertSmallBoxKept(plain, b, "identity");
            int frames = 0;
            foreach (Similarity sim in Similarity.All()) {
                // (the input is the same geometry only where the frame resolves the side: a UTM frame rounds a box of 1e-11 away)
                if (sim.Resolution(2) > d / 50)
                    continue;
                frames++;
                DMesh3 sb = Invariance.Transformed(b, sim);
                ExactMeshBoolean r = Run(Invariance.Transformed(a, sim), sb);
                AssertSmallBoxKept(r, sb, sim.Name);
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(r.Result, sim)));
                    foreach (string c in new[] { "antipodal_undecided", "antipodal_wide", "antipodal_unresolved" })
                        Assert.Equal(plain.Counts[c], r.Counts[c]);
                }
            }
            Assert.True(frames >= 6, $"frames {frames}");
        }

        // reuse (CLAUDE.md): Compute() twice on one instance, the same bits and counts; and serial equals parallel, repeated,
        // also for the failure (the unresolved count decides it; DeterministicApis.txt, the site of antipodal_front_winding)
        [Fact]
        public void Unresolved_SerialEqualsParallel_Repeated()
        {
            foreach (double d in new[] { 1e-11, 1e-14 }) {
                DMesh3 a = OpenUnitBox(), b = SmallBox(d);
                ExactMeshBoolean serial;
                gParallel.SerialOnThisThread = true;
                try {
                    serial = Run(a, b);
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
                ExactMeshBoolean reused = New(a, b);
                for (int run = 0; run < 2; ++run) {
                    bool ok = reused.Compute();
                    Assert.Equal(serial.Status, reused.Status);
                    Assert.Equal(d > 1e-12, ok);
                    if (ok)
                        Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                    else
                        Assert.Equal(serial.Error, reused.Error);
                    foreach (string c in new[] { "antipodal_undecided", "antipodal_wide", "antipodal_unresolved" })
                        Assert.Equal(serial.Counts[c], reused.Counts[c]);
                }
            }
        }

        // the segment test, linear in the angle (S1, S2 of tools/Z3Pilot/antipodal_segment.py): one segment from (-1, 0, 0) to
        // (1, 0, 0) and the point (0, h, 0), whose |A + B| is 2 h / sqrt(1 + h^2). Status 1 exactly where 2 h is within about
        // tol (to a factor 1.2), not up to h = sqrt(tol / 2) as with 1 + a.b <= tol
        static double Tol(double h)
        {
            double la = Math.Sqrt(1 + h * h), e = 2.3e-16 * (h + 1) / la;
            return 16 * (e + e + 1e-15);
        }

        static int Status(double h, double maxBound, out double w, out double bound)
        {
            var p = new Vector3d(0, h, 0);
            Vector3d[] segment = { new Vector3d(-1, 0, 0), new Vector3d(1, 0, 0) };
            double rb;
            return ExactMeshBoolean.antipodal_value(p, h, new Vector3d(0, 0, 1), 0, segment, out w, out bound, out rb, 0, 0, maxBound);
        }

        [Fact]
        public void SegmentTest_IsLinearInTheAngle()
        {
            double w, bound;
            Assert.Equal(1, Status(0, 1e-6, out w, out bound));
            for (double h = 1e-17; h < 1e-3; h *= 1.3) {
                double tol = Tol(h), ab = 2 * h / Math.Sqrt(1 + h * h);
                int s = Status(h, 1e-6, out w, out bound);
                if (ab <= 0.99 * tol)
                    Assert.True(s == 1, $"h {h:R}: {s}, |A + B| {ab:R} tol {tol:R}");
                if (ab >= 1.01 * tol)
                    Assert.True(s != 1, $"h {h:R}: status 1 with |A + B| {ab:R} tol {tol:R} (the old quadratic test)");
            }
            // (the old test, 1 + a.b = 2 h^2 / (1 + h^2) <= tol, would give 1 up to h about 1.3e-7)
            Assert.NotEqual(1, Status(1e-10, 1e-6, out w, out bound));
        }

        // ExactMeshBoolean.WideBound (a literal here, so that the class compiles against the library before #1587)
        const double Wide = 0.25;

        // the wide bound (T1): a term with tol above 0.4 hypot gives another ray (status 2) even where its bound 4 tol / hypot
        // would be below WideBound; at h = 1e-10 the term is well conditioned and the wide value decides, the normal one not
        [Fact]
        public void WideBound_NeedsEachTermConditioned()
        {
            double w, bound;
            // (|A + B| = 2 h = 1.5 tol: not on the segment; hypot about 2 h, so tol / hypot about 0.67, 4 tol / hypot / (4 pi) about 0.21)
            double h = 0.75 * Tol(1e-14);
            Assert.NotEqual(1, Status(h, 1e-6, out w, out bound));
            Assert.Equal(2, Status(h, Wide, out w, out bound));
            Assert.Equal(2, Status(1e-10, 1e-6, out w, out bound));
            Assert.Equal(0, Status(1e-10, Wide, out w, out bound));
            Assert.True(bound > 1e-6 && bound < 1e-3, $"bound {bound:R}");
            // (x1 = +z: 2 atan2(num, den) over 4 pi with (den, num) about (2 h^2, 2 h), a quarter of a turn)
            Assert.True(Math.Abs(w - 0.25) <= bound, $"w {w:R} bound {bound:R}");
        }
    }
}
