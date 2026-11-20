using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SolidBooleanPipeline: the cheap input check, the repair of an open input only on demand, the exact boolean with
    /// AllowOpenMeshes = false and StrictRounding = true, the reduction with the self-intersection guard, and the final check.
    /// </summary>
    public class SolidBooleanPipelineTests
    {
        const ExactMeshBoolean.Operations U = ExactMeshBoolean.Operations.Union, I = ExactMeshBoolean.Operations.Intersection, D = ExactMeshBoolean.Operations.Difference;

        // ---- procedural inputs; Soup() unwelds them like an STL read ----
        static DMesh3 Soup(DMesh3 m)
        {
            var s = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                s.AppendTriangle(s.AppendVertex(m.GetVertex(tv.a)), s.AppendVertex(m.GetVertex(tv.b)), s.AppendVertex(m.GetVertex(tv.c)));
            }
            return s;
        }

        static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Box(Vector3d c, double half, int n)
        {
            return new GridBox3Generator() { Box = new Box3d(c, new Vector3d(half, half, half)), EdgeVertices = n }.Generate().MakeDMesh();
        }

        static DMesh3 Cylinder(Vector3d c, double r, double h, int slices)
        {
            var m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Torus(Vector3d c, double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(c + new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), r * Math.Sin(v), (R + r * Math.Cos(v)) * Math.Sin(u)));
                }
            Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = id(i, j), b = id(i + 1, j), cc = id(i + 1, j + 1), d = id(i, j + 1);
                    m.AppendTriangle(a, d, cc); m.AppendTriangle(a, cc, b);
                }
            return m;
        }

        // two spheres (soups) that overlap
        static List<DMesh3> TwoSpheres() { return new List<DMesh3> { Soup(Sphere(Vector3d.Zero, 1, 12)), Soup(Sphere(new Vector3d(1.2, 0.1, 0.05), 1, 12)) }; }

        static void AssertSolid(SolidBooleanPipeline p)
        {
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, p.Status);
            DMesh3 r = p.Result;
            Assert.NotNull(r);
            Assert.True(r.TriangleCount > 0);
            Assert.True(Invariance.AllFinite(r));
            Assert.True(r.IsClosed());
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(r.TriangleCount, p.Report.ResultTriangles);
            Assert.True(r.IsCompact);       // (the reduction leaves holes in the id spaces; the result is the compacted copy)
        }

        // positions with two or more vertices
        static HashSet<(double, double, double)> ContactPositions(DMesh3 m)
        {
            var seen = new HashSet<(double, double, double)>();
            var multiple = new HashSet<(double, double, double)>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                var key = (p.x == 0 ? 0 : p.x, p.y == 0 ? 0 : p.y, p.z == 0 ? 0 : p.z);
                if (!seen.Add(key)) multiple.Add(key);
            }
            return multiple;
        }

        // ---- success paths ----

        [Fact]
        public void Union_OfSoups_IsCleanClosedAndReachesTheTarget()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.25 };
            Assert.True(p.Compute());
            AssertSolid(p);
            SolidBooleanReport r = p.Report;
            Assert.Equal("", r.NotCleanReason);
            Assert.True(r.Clean);
            Assert.Equal(SolidBooleanStopReason.TargetReached, r.StopReason);
            Assert.True(r.ResultTriangles <= r.TargetTriangles);
            Assert.Equal((int)Math.Ceiling(0.25 * r.BooleanTriangles), r.TargetTriangles);
            Assert.True(r.ResultTriangles < r.BooleanTriangles / 2, "the boolean result was not reduced");
            Assert.All(r.InputClosed, c => Assert.True(c));
            Assert.Empty(r.RepairedInputs);
            Assert.Equal(new[] { Inputs0(p), Inputs1(p) }, r.InputTriangles);
            // the volume stays close to the boolean's (the reduction moves the surface a little)
            var boolean = new ExactMeshBoolean(TwoSpheres(), U) { AllowOpenMeshes = false, StrictRounding = true };
            Assert.True(boolean.Compute());
            Assert.Equal(boolean.Result.TriangleCount, r.BooleanTriangles);
            double v0 = Invariance.Volume(boolean.Result), v1 = Invariance.Volume(p.Result);
            Assert.InRange(v1 / v0, 0.9, 1.1);
        }
        static int Inputs0(SolidBooleanPipeline p) { return p.Inputs[0].TriangleCount; }
        static int Inputs1(SolidBooleanPipeline p) { return p.Inputs[1].TriangleCount; }

        [Fact]
        public void IntersectionAndDifference_AndThreeInputs_AreClean()
        {
            DMesh3 a = Soup(Sphere(Vector3d.Zero, 1, 12)), b = Soup(Sphere(new Vector3d(1.0, 0.1, 0.05), 0.6, 12)), c = Soup(Sphere(new Vector3d(0.5, 0.9, 0.1), 0.8, 12));
            foreach (var (op, inputs) in new[] { (I, new[] { a, b }), (D, new[] { a, b }), (U, new[] { a, b, c }), (D, new[] { a, b, c }) }) {
                var p = new SolidBooleanPipeline(inputs, op) { TargetFraction = 0.5 };
                Assert.True(p.Compute(), op + " " + inputs.Length + ": " + p.Error);
                AssertSolid(p);
                Assert.Equal("", p.Report.NotCleanReason);
                Assert.Equal(SolidBooleanStopReason.TargetReached, p.Report.StopReason);
            }
        }

        [Fact]
        public void BoxCylinderTorus_Soups_AreClean()
        {
            var cases = new List<(string, ExactMeshBoolean.Operations, DMesh3[])> {
                ("box - cylinder", D, new[] { Soup(Box(Vector3d.Zero, 1, 6)), Soup(Cylinder(new Vector3d(0.2, -1.5, 0.1), 0.4, 3, 32)) }),
                ("torus + cylinder", U, new[] { Soup(Torus(Vector3d.Zero, 1, 0.3, 40, 16)), Soup(Cylinder(new Vector3d(1, -0.8, 0), 0.25, 1.6, 32)) }),
                ("linked tori", U, new[] { Soup(Torus(Vector3d.Zero, 1, 0.3, 40, 16)), Soup(Torus(new Vector3d(1, 0, 0), 1, 0.3, 40, 16)) }),
                ("box + box overlap, plate faces", U, new[] { Soup(Box(Vector3d.Zero, 1, 8)), Soup(Box(new Vector3d(0.7, 0.4, 0.2), 1, 8)) }),
            };
            foreach (var (name, op, inputs) in cases) {
                var p = new SolidBooleanPipeline(inputs, op) { TargetFraction = 0.25 };
                Assert.True(p.Compute(), name + ": " + p.Error);
                AssertSolid(p);
                Assert.True(p.Report.Clean, name + ": " + p.Report.NotCleanReason);
                Assert.Equal(SolidBooleanStopReason.TargetReached, p.Report.StopReason);
            }
        }

        [Fact]
        public void RepeatedBooleans_StayCleanAndDoNotGrow()
        {
            DMesh3 acc = Sphere(Vector3d.Zero, 1, 10);
            int first = 0;
            for (int k = 1; k <= 4; ++k) {
                var p = new SolidBooleanPipeline(acc, Soup(Sphere(new Vector3d(0.9 * k, 0.15 * k, 0.05 * k), 1, 10)), U) { TargetFraction = 0.5 };
                Assert.True(p.Compute(), "step " + k + ": " + p.Error);
                AssertSolid(p);
                Assert.True(p.Report.Clean, "step " + k + ": " + p.Report.NotCleanReason);
                acc = p.Result;
                if (k == 1) first = acc.TriangleCount;
                Assert.True(acc.TriangleCount <= 2 * first, "step " + k + ": " + acc.TriangleCount + " triangles after " + first);
            }
        }

        // ---- inputs that are not closed ----

        static DMesh3 HoledSphere()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, 10);
            foreach (int t in m.TriangleIndices().Take(3).ToArray())
                m.RemoveTriangle(t);
            return m;
        }

        [Fact]
        public void OpenInput_IsRefusedWithRefuse()
        {
            DMesh3 holed = HoledSphere();
            int triangles = holed.TriangleCount;
            var p = new SolidBooleanPipeline(holed, Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10)), U) { InputRepair = SolidBooleanPipeline.InputRepairModes.Refuse };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Null(p.Result);
            Assert.NotEmpty(p.Error);
            Assert.False(p.Report.InputClosed[0]);
            Assert.True(p.Report.InputClosed[1]);
            Assert.Empty(p.Report.RepairedInputs);
            Assert.Equal(triangles, holed.TriangleCount);
        }

        [Fact]
        public void OpenInput_IsRepairedOnACopy_AndTheReportNamesIt()
        {
            DMesh3 holed = HoledSphere();
            DMesh3 before = new DMesh3(holed);
            DMesh3 other = Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10));
            foreach (var mode in new[] { SolidBooleanPipeline.InputRepairModes.AutoRepair, SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust }) {
                var p = new SolidBooleanPipeline(holed, other, U) { InputRepair = mode, TargetFraction = 0.5 };
                Assert.True(p.Compute(), mode + ": " + p.Error);
                AssertSolid(p);
                Assert.True(p.Report.Clean, p.Report.NotCleanReason);
                SolidBooleanInputRepair rep = Assert.Single(p.Report.RepairedInputs);
                Assert.Equal(0, rep.Index);
                // the cheapest repair that works is the one used, also with the fallback allowed
                Assert.Equal("MeshAutoRepair", rep.Method);
                Assert.Equal(before.TriangleCount, rep.TrianglesBefore);
                Assert.True(rep.TrianglesAfter >= rep.TrianglesBefore);
                Assert.Equal("", Invariance.Difference(before, holed));
                Assert.Equal("", Invariance.Difference(other, new DMesh3(other)));
                Assert.Same(holed, p.Inputs[0]);
            }
        }

        // one triangle: MeshAutoRepair removes it (nothing to close), MeshRobustRepair solidifies it into a closed blob
        static DMesh3 SingleTriangle()
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)));
            return m;
        }

        [Fact]
        public void RobustRepair_IsOnlyAnExplicitFallback()
        {
            DMesh3 triangle = SingleTriangle();
            DMesh3 other = Sphere(new Vector3d(0.3, 0.3, 0), 0.6, 10);

            var auto = new SolidBooleanPipeline(triangle, other, U) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair };
            Assert.False(auto.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, auto.Status);
            Assert.Contains("input 0", auto.Error);
            Assert.Null(auto.Result);
            Assert.Empty(auto.Report.RepairedInputs);

            var robust = new SolidBooleanPipeline(triangle, other, U) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust, TargetFraction = 0.5 };
            Assert.True(robust.Compute(), robust.Error);
            AssertSolid(robust);
            SolidBooleanInputRepair rep = Assert.Single(robust.Report.RepairedInputs);
            Assert.Equal(0, rep.Index);
            Assert.Equal("MeshRobustRepair", rep.Method);
            Assert.Equal(1, rep.TrianglesBefore);
            Assert.True(rep.TrianglesAfter > 4);
            Assert.True(robust.Report.Clean, robust.Report.NotCleanReason);
            Assert.Equal(1, triangle.TriangleCount);
        }

        [Fact]
        public void ClosedInputs_AreNeverRepaired_AlsoWithAFlippedTriangle()
        {
            // with Orientation = AreaMajority the boolean reorients flipped triangles itself: the input counts as open for
            // IsWindingClosed but needs no repair
            DMesh3 flipped = Sphere(Vector3d.Zero, 1, 10);
            flipped.ReverseTriOrientation(flipped.TriangleIndices().First());
            Assert.False(ExactMeshBoolean.IsWindingClosed(flipped));
            var p = new SolidBooleanPipeline(flipped, Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10)), U) { TargetFraction = 0.5, Orientation = ExactMeshBoolean.OrientationModes.AreaMajority };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.False(p.Report.InputClosed[0]);
            Assert.Empty(p.Report.RepairedInputs);
        }

        [Fact]
        public void ConfigureBoolean_CannotSwitchOffTheStrictSettings()
        {
            DMesh3 holed = HoledSphere();
            var p = new SolidBooleanPipeline(holed, Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10)), U) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.Refuse,
                ConfigureBoolean = op => { op.AllowOpenMeshes = true; op.StrictRounding = false; }
            };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
        }

        [Fact]
        public void TheBooleanRunsWithTheStrictSettings()
        {
            ExactMeshBoolean seen = null;
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { ConfigureBoolean = op => { op.AllowOpenMeshes = true; op.StrictRounding = false; op.MaxPieces = 1000000; seen = op; } };
            Assert.True(p.Compute());
            Assert.NotNull(seen);
            Assert.False(seen.AllowOpenMeshes);
            Assert.True(seen.StrictRounding);
            Assert.Equal(1000000, seen.MaxPieces);      // the rest of the configuration is kept
        }

        // ---- contacts ----

        [Fact]
        public void TouchingBoxes_ContactsAreKeptOrRemoved_NeverCreated()
        {
            // two boxes that share one edge: their solids touch along it (duplicated vertices, a non-manifold edge split)
            var inputs = new[] { Soup(Box(Vector3d.Zero, 1, 8)), Soup(Box(new Vector3d(2, 2, 0), 1, 8)) };
            var boolean = new ExactMeshBoolean(inputs, U) { AllowOpenMeshes = false, StrictRounding = true };
            Assert.True(boolean.Compute());
            Assert.True(boolean.Contacts > 0, "the test does not exercise contacts");
            var contactsBefore = ContactPositions(boolean.Result);

            var p = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.3 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            SolidBooleanReport r = p.Report;
            Assert.Equal(boolean.Contacts, r.ContactsBoolean);
            Assert.Equal(SolidBooleanStopReason.TargetReached, r.StopReason);
            Assert.True(r.ResultTriangles < r.BooleanTriangles / 2);
            Assert.True(r.ContactsResult <= r.ContactsBoolean);
            Assert.True(r.ContactsResult > 0, "the contacts along the edge were all removed");
            // every contact of the result is at a contact of the boolean result: the guard creates no new touching
            Assert.True(ContactPositions(p.Result).IsSubsetOf(contactsBefore));
            // NotCleanReason sees the touching points as self-intersections and nothing else
            Assert.Equal("self-intersections", r.NotCleanReason);
            Assert.False(r.Clean);
        }

        [Fact]
        public void TouchingBoxes_ContactsLimitHowFarTheReductionGoes()
        {
            // measured limit (benchmarks/SolidBooleanBenchmark README): two subdivided boxes that touch along an edge stop at 96 of 1176
            // triangles when asked for 4, the same boxes apart reduce to 8: the sheets along a contact line cannot follow the collapses next to it
            var apart = new SolidBooleanPipeline(new[] { Soup(Box(Vector3d.Zero, 1, 8)), Soup(Box(new Vector3d(2.5, 2.5, 0), 1, 8)) }, U) { TargetTriangleCount = 4 };
            Assert.True(apart.Compute(), apart.Error);
            Assert.True(apart.Report.ResultTriangles <= 12, apart.Report.ResultTriangles.ToString());

            var touching = new SolidBooleanPipeline(new[] { Soup(Box(Vector3d.Zero, 1, 8)), Soup(Box(new Vector3d(2, 2, 0), 1, 8)) }, U) { TargetTriangleCount = 4 };
            Assert.True(touching.Compute(), touching.Error);
            AssertSolid(touching);
            SolidBooleanReport r = touching.Report;
            Assert.True(r.ResultTriangles > 4);
            Assert.True(r.ResultTriangles > 4 * apart.Report.ResultTriangles);
            Assert.Equal(SolidBooleanStopReason.Contacts, r.StopReason);
            Assert.True(r.BlockedByGuard > 0);
            Assert.True(r.ContactsResult > 0);
            Assert.True(r.BlockedAtContacts > 0);
            Assert.Contains(r.ContactsResult + " contacts left", r.StopDetail);
            Assert.Equal(0, apart.Report.ContactsResult);
        }

        [Fact]
        public void PoleTouch_ContactMayBeRemoved_ResultIsThenClean()
        {
            var inputs = new[] { Soup(Box(Vector3d.Zero, 1, 8)), Soup(Sphere(new Vector3d(0, 2, 0), 1, 13)) };
            var p = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.25 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.True(p.Report.ContactsBoolean >= 1);
            Assert.True(p.Report.ContactsResult <= p.Report.ContactsBoolean);
            Assert.Equal(p.Report.ContactsResult == 0, p.Report.Clean);
        }

        // ---- stop reasons ----

        [Fact]
        public void NoCriterion_ReturnsTheBooleanResult()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U);
            Assert.True(p.Compute());
            AssertSolid(p);
            Assert.Equal(SolidBooleanStopReason.NotReduced, p.Report.StopReason);
            Assert.Equal(p.Report.BooleanTriangles, p.Report.ResultTriangles);
            Assert.True(p.Report.Clean);
        }

        [Fact]
        public void DisjointIntersection_IsEmpty()
        {
            var p = new SolidBooleanPipeline(Sphere(Vector3d.Zero, 1, 8), Sphere(new Vector3d(5, 0, 0), 1, 8), I) { TargetFraction = 0.5 };
            Assert.True(p.Compute());
            Assert.Equal(0, p.Result.TriangleCount);
            Assert.Equal(SolidBooleanStopReason.EmptyResult, p.Report.StopReason);
            Assert.Equal("empty", p.Report.NotCleanReason);
            Assert.False(p.Report.Clean);
        }

        [Fact]
        public void TinyMaxDeviation_StopsWithTheDeviationBound()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.1, MaxDeviation = 1e-5 };
            Assert.True(p.Compute());
            AssertSolid(p);
            SolidBooleanReport r = p.Report;
            Assert.True(r.ResultTriangles > r.TargetTriangles);
            Assert.Equal(SolidBooleanStopReason.DeviationBound, r.StopReason);
            Assert.True(r.BlockedByDeviation > 0);
            Assert.Contains("MaxDeviation", r.StopDetail);
            Assert.True(r.Clean, r.NotCleanReason);
        }

        [Fact]
        public void MinimalTouchingBoxes_StopWithTheSelfIntersectionGuard()
        {
            // 24 triangles are the least that two boxes touching along an edge need; the target 6 cannot be reached
            var inputs = new[] { Soup(Box(Vector3d.Zero, 1, 2)), Soup(Box(new Vector3d(2, 2, 0), 1, 2)) };
            var p = new SolidBooleanPipeline(inputs, U) { TargetTriangleCount = 6 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal(24, p.Report.ResultTriangles);
            Assert.True(p.Report.StopReason == SolidBooleanStopReason.SelfIntersectionGuard || p.Report.StopReason == SolidBooleanStopReason.Contacts, p.Report.StopReason.ToString());
            Assert.True(p.Report.BlockedByGuard > 0);
            Assert.True(p.Report.BlockedAtContacts > 0);
            Assert.Equal(6, p.Report.TargetTriangles);
        }

        [Fact]
        public void EdgeLengthAndQuadricCriteria_Reduce()
        {
            var byLength = new SolidBooleanPipeline(TwoSpheres(), U) { MinEdgeLength = 0.3 };
            Assert.True(byLength.Compute());
            AssertSolid(byLength);
            Assert.True(byLength.Report.ResultTriangles < byLength.Report.BooleanTriangles);
            Assert.True(byLength.Report.Clean, byLength.Report.NotCleanReason);

            var byError = new SolidBooleanPipeline(TwoSpheres(), U) { MaxQuadricError = 1e-6 };
            Assert.True(byError.Compute());
            AssertSolid(byError);
            Assert.True(byError.Report.ResultTriangles < byError.Report.BooleanTriangles);
            Assert.Equal(SolidBooleanStopReason.TargetReached, byError.Report.StopReason);
        }

        [Fact]
        public void FlatFaces_ReduceWithoutDegenerateTriangles_ByTheDefaultQuality()
        {
            // the Reducer alone leaves zero-area triangles on the creases of a subdivided box (NotCleanReason "degenerate triangles")
            DMesh3 plain = Box(Vector3d.Zero, 1, 12);
            new Reducer(plain) { PreventSelfIntersections = true }.ReduceToTriangleCount(plain.TriangleCount / 4);
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(plain));

            var p = new SolidBooleanPipeline(new[] { Soup(Box(Vector3d.Zero, 1, 12)), Soup(Box(new Vector3d(5, 0, 0), 1, 3)) }, U) { TargetFraction = 0.25 };
            Assert.True(p.Compute(), p.Error);
            AssertSolid(p);
            Assert.Equal("", p.Report.NotCleanReason);
            Assert.Equal(SolidBooleanStopReason.TargetReached, p.Report.StopReason);
        }

        // ---- invalid use, cancellation, report ----

        [Fact]
        public void InvalidSettings_AreRefused()
        {
            foreach (Action<SolidBooleanPipeline> bad2 in new Action<SolidBooleanPipeline>[] { q => q.MinTriangleQuality = double.NaN, q => q.MinTriangleQuality = 2, q => q.MaxDeviation = double.PositiveInfinity }) {
                var q2 = new SolidBooleanPipeline(TwoSpheres(), U);
                bad2(q2);
                Assert.False(q2.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, q2.Status);
            }
            var two = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.5, TargetTriangleCount = 100 };
            Assert.False(two.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, two.Status);

            foreach (Action<SolidBooleanPipeline> bad in new Action<SolidBooleanPipeline>[] {
                q => q.TargetFraction = 1.5, q => q.TargetFraction = -0.1, q => q.MinEdgeLength = double.NaN, q => q.MaxDeviation = -1, q => q.TargetTriangleCount = -4 }) {
                var q = new SolidBooleanPipeline(TwoSpheres(), U);
                bad(q);
                Assert.False(q.Compute());
                Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, q.Status);
                Assert.Null(q.Result);
            }

            var none = new SolidBooleanPipeline(new DMesh3[0], U);
            Assert.False(none.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, none.Status);

            var nullInput = new SolidBooleanPipeline(new DMesh3[] { Sphere(Vector3d.Zero, 1, 6), null }, U);
            Assert.False(nullInput.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, nullInput.Status);

            var nan = Sphere(Vector3d.Zero, 1, 6);
            nan.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var nanInput = new SolidBooleanPipeline(nan, Sphere(Vector3d.Zero, 1, 6), U);
            Assert.False(nanInput.Compute());
            Assert.NotEqual(SolidBooleanPipeline.PipelineStatus.Success, nanInput.Status);
            Assert.Null(nanInput.Result);
        }

        [Fact]
        public void CancelledToken_StopsWithNoResult()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { Cancellation = cts.Token, TargetFraction = 0.5 };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
        }

        [Fact]
        public void CancelledDuringTheBoolean_StopsWithNoResult()
        {
            var cts = new CancellationTokenSource();
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { Cancellation = cts.Token, TargetFraction = 0.5, ConfigureBoolean = op => cts.Cancel() };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
        }

        [Fact]
        public void CancelledBeforeTheReduction_StopsWithNoResult()
        {
            var cts = new CancellationTokenSource();
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { Cancellation = cts.Token, TargetFraction = 0.5, BeforeReduction = () => cts.Cancel() };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Null(p.Result);
        }

        [Fact]
        public void MinimalMesh_IsExhausted_NotTheGuard()
        {
            var p = new SolidBooleanPipeline(new[] { Box(Vector3d.Zero, 1, 2), Box(new Vector3d(5, 0, 0), 1, 2) }, U) { TargetTriangleCount = 2 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(8, p.Report.ResultTriangles);      // two tetrahedra
            Assert.Equal(SolidBooleanStopReason.Exhausted, p.Report.StopReason);
        }

        [Fact]
        public void BooleanStatus_IsSetAfterABooleanFailure()
        {
            var p = new SolidBooleanPipeline(HoledSphere(), Sphere(new Vector3d(1.2, 0, 0), 1, 10), U) { InputRepair = SolidBooleanPipeline.InputRepairModes.Refuse };
            Assert.False(p.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, p.BooleanStatus);
        }

        [Fact]
        public void Report_HasTheStageTimes()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.5 };
            Assert.True(p.Compute());
            SolidBooleanReport r = p.Report;
            long stages = r.InputCheckMilliseconds + r.RepairMilliseconds + r.BooleanMilliseconds + r.ReductionMilliseconds + r.FinalCheckMilliseconds;
            Assert.True(r.TotalMilliseconds >= stages - 3, "total " + r.TotalMilliseconds + " < stages " + stages);
            Assert.True(r.BooleanMilliseconds >= 0 && r.ReductionMilliseconds >= 0);
            Assert.Equal(0, r.RepairMilliseconds);
        }

        // ---- reuse, determinism, invariance ----

        [Fact]
        public void Compute_Twice_OnOneInstance_GivesTheSameResult_AndFollowsChangedSettings()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.25 };
            Assert.True(p.Compute());
            DMesh3 first = p.Result;
            SolidBooleanReport firstReport = p.Report;
            Assert.True(p.Compute());
            Assert.NotSame(first, p.Result);
            Assert.NotSame(firstReport, p.Report);
            Assert.Equal("", Invariance.Difference(first, p.Result));

            p.TargetFraction = 0.5;
            Assert.True(p.Compute());
            Assert.True(p.Result.TriangleCount > first.TriangleCount);
            Assert.True(p.Result.TriangleCount <= p.Report.TargetTriangles);

            // a failed run does not leave the earlier result behind
            p.TargetFraction = 2;
            Assert.False(p.Compute());
            Assert.Null(p.Result);
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.InvalidInput, p.Status);
            p.TargetFraction = 0.25;
            Assert.True(p.Compute());
            Assert.Equal("", Invariance.Difference(first, p.Result));
        }

        [Fact]
        public void Repeated_Runs_AreBitIdentical_AndSwappedInputsAreClean()
        {
            DMesh3 reference = null;
            for (int run = 0; run < 3; ++run) {
                var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.25 };
                Assert.True(p.Compute());
                if (reference == null) reference = p.Result;
                else Assert.Equal("", Invariance.Difference(reference, p.Result));
            }
            // union is symmetric: the swapped inputs also give a clean solid at the target (not the same triangles: the reduction order follows the ids)
            var swapped = new SolidBooleanPipeline(TwoSpheres().AsEnumerable().Reverse(), U) { TargetFraction = 0.25 };
            Assert.True(swapped.Compute());
            AssertSolid(swapped);
            Assert.True(swapped.Report.Clean);
        }

        [Theory]
        [MemberData(nameof(FrameNames))]
        public void Result_IsInvariantUnderSimilarities(string frame)
        {
            Similarity s = Similarity.ByName(frame);
            var inputs = TwoSpheres();
            var reference = new SolidBooleanPipeline(inputs, U) { TargetFraction = 0.25 };
            Assert.True(reference.Compute());

            var moved = inputs.Select(m => Invariance.Transformed(m, s)).ToList();
            var p = new SolidBooleanPipeline(moved, U) { TargetFraction = 0.25 };
            Assert.True(p.Compute(), s.Name + ": " + p.Error);
            AssertSolid(p);
            Assert.True(p.Report.Clean, s.Name + ": " + p.Report.NotCleanReason);
            Assert.True(p.Result.TriangleCount <= p.Report.TargetTriangles, s.Name);
            // (the other frames round the new vertices differently: the same solid, not the same triangles)
            if (s.IsExact) {
                Assert.Equal(reference.Report.BooleanTriangles, p.Report.BooleanTriangles);
                Assert.Equal("", Invariance.Difference(reference.Result, Invariance.MappedBack(p.Result, s)));
            }
        }

        // ---- failure paths and stop reasons ----

        [Fact]
        public void BooleanBudgetExceeded_IsBooleanFailed_WithItsStatus()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.5, ConfigureBoolean = op => op.MaxPieces = 1 };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.BooleanFailed, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.BudgetExceeded, p.BooleanStatus);
            Assert.Contains("BudgetExceeded", p.Error);
            Assert.Null(p.Result);
        }

        [Fact]
        public void CancelledAfterTheOpenInputWasSeen_StopsInTheRepair()
        {
            var cts = new CancellationTokenSource();
            var p = new SolidBooleanPipeline(HoledSphere(), Soup(Sphere(new Vector3d(1.2, 0, 0), 1, 10)), U) { Cancellation = cts.Token, TargetFraction = 0.5, ConfigureBoolean = op => cts.Cancel() };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Cancelled, p.Status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.OpenInput, p.BooleanStatus);      // (the boolean had refused the input: the repair was entered)
            Assert.Null(p.Result);
        }

        [Fact]
        public void RobustRepair_ThatDoesNotCloseTheInput_FailsWithOpenInput()
        {
            var tiny = new DMesh3();
            tiny.AppendTriangle(tiny.AppendVertex(new Vector3d(0, 0, 0)), tiny.AppendVertex(new Vector3d(1e-300, 0, 0)), tiny.AppendVertex(new Vector3d(0, 1e-300, 0)));
            var p = new SolidBooleanPipeline(tiny, Sphere(new Vector3d(5, 5, 5), 1, 6), U) { InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepairThenRobust };
            Assert.False(p.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.OpenInput, p.Status);
            Assert.Contains("no allowed repair", p.Error);
            Assert.Empty(p.Report.RepairedInputs);
        }

        [Fact]
        public void TriangleQualityBound_ThatNothingMeets_StopsWithTriangleQuality()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { TargetFraction = 0.1, MinTriangleQuality = 1 };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanStopReason.TriangleQuality, p.Report.StopReason);
            Assert.True(p.Report.ResultTriangles > p.Report.TargetTriangles);
            Assert.True(p.Report.BlockedByQuality > 0);
            AssertSolid(p);
        }

        [Fact]
        public void PairedContacts_ThatTheStructureRefuses_StopWithTopology()
        {
            var inputs = new[] { new Vector3d(0, 0, 0), new Vector3d(2, 2, 0), new Vector3d(4, 0, 0), new Vector3d(2, -2, 0) }.Select(c => Soup(Box(c, 1, 3))).ToList();
            var p = new SolidBooleanPipeline(inputs, U) { TargetTriangleCount = 4, PairContacts = true };
            Assert.True(p.Compute(), p.Error);
            Assert.Equal(SolidBooleanStopReason.Topology, p.Report.StopReason);
            Assert.True(p.Report.BlockedByTopology > p.Report.BlockedByQuality && p.Report.BlockedByTopology > p.Report.BlockedByGuard);
            Assert.True(p.Report.ResultTriangles > 4);
            Assert.True(p.Result.IsClosed());
        }

        [Fact]
        public void EdgeLength_ThatCannotBeReached_ReportsTheStop()
        {
            var p = new SolidBooleanPipeline(TwoSpheres(), U) { MinEdgeLength = 100 };
            Assert.True(p.Compute(), p.Error);
            Assert.NotEqual(SolidBooleanStopReason.TargetReached, p.Report.StopReason);      // (the last edges of a solid cannot be collapsed: a tetrahedron is the minimum)
            Assert.Equal(4, p.Report.ResultTriangles);
        }

        public static IEnumerable<object[]> FrameNames() { return Similarity.Names(); }
    }
}
