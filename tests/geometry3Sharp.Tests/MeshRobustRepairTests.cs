using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Repair with the solidify fallback: clean inputs keep their triangles, unclean results (also only
    /// self-intersecting ones) become a clean solid.
    /// </summary>
    public class MeshRobustRepairTests
    {
        static DMesh3 Sphere(double r, Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        [Fact]
        public void CleanInput_KeepsItsTriangles()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            int n = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify);
            Assert.Equal("", r.FallbackReason);
            Assert.Equal("", r.ResultReason);
            Assert.Equal(n, r.Mesh.TriangleCount);
        }

        static double Volume(DMesh3 m)
        {
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
        }

        // two overlapping spheres: closed and manifold, but self-intersecting (the repair keeps that)
        static DMesh3 OverlappingSpheres()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0)));
            return m;
        }

        [Fact]
        public void SelfIntersectingInput_ResolvedExactly()
        {
            DMesh3 m = OverlappingSpheres();
            DMesh3 a = Sphere(1, Vector3d.Zero), b = Sphere(1, new Vector3d(1.2, 0, 0));
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal("", r.ResultReason);
            Assert.False(r.UsedSolidify);
            Assert.Equal("self-intersections", r.FallbackReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            // the exact union of the two triangulated spheres: their volumes minus the overlap, which is the
            // exact intersection (volume identity)
            var inter = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Intersection);
            Assert.True(inter.Compute());
            Assert.Equal(Volume(a) + Volume(b) - Volume(inter.Result), Volume(r.Mesh), 6);
            // the input vertices are kept
            var input = new HashSet<Vector3d>(m.Vertices());
            Assert.True(r.Mesh.Vertices().Count(v => input.Contains(v)) > 0.8 * r.Mesh.VertexCount);
        }

        [Fact]
        public void EmptyRepairResult_InputResolvedOnly()
        {
            // an empty repair result is not resolved: the exact stage starts from the input right away, with as
            // many rounds as for the input on its own
            var own = new MeshRobustRepair(OverlappingSpheres()) { RepairF = (m) => new DMesh3(m) };
            Assert.True(own.Apply());
            var r = new MeshRobustRepair(OverlappingSpheres()) { RepairF = (m) => new DMesh3() };
            Assert.True(r.Apply());
            Assert.Equal("empty", r.FallbackReason);
            Assert.True(r.UsedInput);
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal("", r.ExactResolveReason);
            Assert.Equal(own.ExactResolveRoundsUsed, r.ExactResolveRoundsUsed);
        }

        [Fact]
        public void TooManySelfIntersections_SolidifiedInsteadOfResolved()
        {
            // the exact resolve takes too long for heavily overlapping meshes: above ExactResolveMaxPairs the
            // repaired mesh is solidified right away
            var r = new MeshRobustRepair(OverlappingSpheres()) { ExactResolveMaxPairs = 10 };
            Assert.True(r.Apply());
            Assert.False(r.UsedExactResolve);
            Assert.Equal("too many self-intersections", r.ExactResolveReason);
            Assert.True(r.UsedSolidify);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        [Fact]
        public void SelfIntersectingInput_WithoutExactResolve_SolidifiedClean()
        {
            var r = new MeshRobustRepair(OverlappingSpheres()) { ExactResolve = false };
            Assert.True(r.Apply());
            Assert.True(r.UsedSolidify);
            Assert.False(r.UsedExactResolve);
            Assert.Equal("self-intersections", r.FallbackReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            // the union of the spheres
            double v = Volume(r.Mesh);
            double h = (2 - 1.2) / 2, cap = Math.PI * h * h * (3 - h) / 3;   // each sphere's cap inside the other
            double expected = 2 * 4.0 / 3 * Math.PI - 2 * cap;
            Assert.InRange(v, 0.97 * expected, 1.03 * expected);
        }

        [Fact]
        public void OpenSelfIntersectingInput_NotResolvedExactly()
        {
            // the repair cannot close it (RepairF keeps it open): no exact winding numbers, so not resolved exactly
            DMesh3 m = OverlappingSpheres();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 0.9) m.RemoveTriangle(t);
            var r = new MeshRobustRepair(m) { RepairF = (x) => new DMesh3(x) };
            Assert.True(r.Apply());
            Assert.False(r.UsedExactResolve);
            Assert.Equal("not closed (winding number)", r.ExactResolveReason);
            Assert.True(r.UsedSolidify);
        }

        [Fact]
        public void WithoutTheSelfIntersectionCheck_KeptAsRepaired()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(m, Sphere(1, new Vector3d(1.2, 0, 0)));
            var r = new MeshRobustRepair(m) { CheckSelfIntersections = false };
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify);
        }

        [Fact]
        public void OpenMesh_ClosedBySolidifyOrRepair()
        {
            DMesh3 m = Sphere(1, Vector3d.Zero);
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 0.9) m.RemoveTriangle(t);
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        [Fact]
        public void CleanButVolumeLost_InputResolvedOrSolidified()
        {
            // input: two overlapping boxes (a region enclosed twice); a repair stage that returns only one of
            // them gives a clean mesh that lost a third of the solid: the input is used instead, resolved
            // exactly (or solidified without ExactResolve)
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1, 0, 0), Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 input = new DMesh3(a);
            MeshEditor.Append(input, b);
            foreach (bool exact in new[] { true, false }) {
                var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(a), ExactResolve = exact };
                Assert.True(r.Apply());
                Assert.Equal(exact, r.UsedExactResolve);
                Assert.Equal(!exact, r.UsedSolidify);
                Assert.Equal("volume differs from the input", r.FallbackReason);
                double missing, extra;
                Assert.True(MeshRepairMetrics.VolumeIoU(input, r.Mesh, out missing, out extra) > 0.99);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
                if (exact)
                    Assert.Equal(12.0, Volume(r.Mesh), 9);     // the union of the boxes (3 x 2 x 2), exactly
            }
            // the same repair returning the whole solid is kept
            DMesh3 union = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0, 0), new Vector3d(1.5, 1, 1)) }.Generate().MakeDMesh();
            var r2 = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(union) };
            Assert.True(r2.Apply());
            Assert.False(r2.UsedSolidify);
            Assert.False(r2.UsedExactResolve);
            Assert.True(r2.RepairVolumeIoU > 0.99);
        }

        [Fact]
        public void CleanButAFarComponentLost_InputUsed()
        {
            // two spheres 1000 apart: the volume check sampled the common bounds, where no sample hit them, so a
            // repair that lost one of them was accepted ("no solid to compare with")
            DMesh3 input = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(input, Sphere(1, new Vector3d(1000, 0, 0)));
            var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => Sphere(1, Vector3d.Zero) };
            Assert.True(r.Apply());
            Assert.Equal("volume differs from the input", r.FallbackReason);
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal(2 * Volume(Sphere(1, Vector3d.Zero)), Volume(r.Mesh), 6);
        }

        [Fact]
        public void UncleanResultThatLostAPart_InputUsed()
        {
            // the repair loses a sphere and leaves the other one open (eg erosion of an open region): the exact
            // stage cannot use it (not closed), and the solidify of it closed the remaining sphere only, without
            // a volume check; now the input is used, its solid agrees with the result's less than MinVolumeIoU
            DMesh3 a = Sphere(1, Vector3d.Zero), b = Sphere(1, new Vector3d(3, 0, 0));
            DMesh3 input = new DMesh3(a);
            MeshEditor.Append(input, b);
            Func<DMesh3, DMesh3> openA = (m) => {
                DMesh3 c = new DMesh3(a);
                foreach (int t in c.TriangleIndices().ToArray())
                    if (c.GetTriCentroid(t).z > 0.95) c.RemoveTriangle(t);
                return c;
            };
            foreach (bool exact in new[] { true, false }) {
                var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = openA, ExactResolve = exact };
                Assert.True(r.Apply());
                Assert.Equal("not closed", r.FallbackReason);
                Assert.True(r.UsedInput);
                Assert.Equal(exact, r.UsedExactResolve);
                Assert.Equal(!exact, r.UsedSolidify);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
                double missing, extra;
                Assert.True(MeshRepairMetrics.VolumeIoU(input, r.Mesh, out missing, out extra) > 0.97);
            }
            // an unclean result that keeps the solid is still the one solidified
            var kept = new MeshRobustRepair(new DMesh3(a)) { RepairF = openA, ExactResolve = false };
            Assert.True(kept.Apply());
            Assert.False(kept.UsedInput);
            Assert.True(kept.UsedSolidify);
        }

        [Fact]
        public void EmptyRepairResult_InputUsed()
        {
            // a repair stage that removes everything: Apply() returned false ("the mesh to solidify is empty")
            DMesh3 input = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(input, Sphere(1, new Vector3d(1.2, 0, 0)));
            foreach (double minIoU in new[] { 0.99, 0 }) {
                var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(), MinVolumeIoU = minIoU };
                Assert.True(r.Apply());
                Assert.Equal("empty", r.FallbackReason);
                Assert.True(r.UsedInput);
                Assert.True(r.UsedExactResolve, r.ExactResolveReason);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            }
        }

        [Fact]
        public void UncleanResultThatLosesVolume_ExactResolveOfTheInput()
        {
            // the repair drops a part (box c) and leaves two overlapping boxes: resolving them exactly is clean,
            // but the solid differs from the input's (before: the exact result was rejected and the result without
            // box c solidified); the input is resolved exactly instead
            Func<Vector3d, DMesh3> box = (c) => new TrivialBox3Generator() { Box = new Box3d(c, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 overlapping = box(Vector3d.Zero);
            MeshEditor.Append(overlapping, box(new Vector3d(1, 0, 0)));
            DMesh3 input = new DMesh3(overlapping);
            MeshEditor.Append(input, box(new Vector3d(10, 0, 0)));
            var r = new MeshRobustRepair(input) { RepairF = (m) => new DMesh3(overlapping) };
            Assert.True(r.Apply());
            Assert.Equal("self-intersections", r.FallbackReason);
            Assert.True(r.UsedInput);
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal(3 * 2 * 2 + 8, Volume(r.Mesh), 9);     // the union of the overlapping boxes and box c
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2),
                new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2)) }.Generate().MakeDMesh();
        }

        // a unit box and a small sphere (radius 0.02: triangles of about 1e-5 area, not degenerate for the box's
        // diagonal), and an unused vertex far away (eg from an OBJ file)
        static DMesh3 BoxSmallSphereAndFarUnusedVertex()
        {
            DMesh3 m = Box(-1, -1, -1, 1, 1, 1);
            MeshEditor.Append(m, Sphere(0.02, new Vector3d(3, 0, 0)));
            m.AppendVertex(new Vector3d(1e4, 1e4, 1e4));
            return m;
        }

        [Fact]
        public void UnusedVertexFarAway_SmallPartKept()
        {
            // the unused vertex made the diagonal 1e4 times larger: the sphere's triangles were "degenerate", the
            // cleanup collapsed the sphere, and the exact resolve, whose volume check does not notice a part this
            // small, returned the box alone
            DMesh3 m = BoxSmallSphereAndFarUnusedVertex();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            int n = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.Equal("", r.FallbackReason);
            Assert.Equal(n, r.Mesh.TriangleCount);
            Assert.True(r.Mesh.CachedBounds.Max.x < 3.1);       // the unused vertex is gone
        }

        [Fact]
        public void UnusedVertexFarAway_SliversAndRoundingRepairKeepSmallTriangles()
        {
            // the same thresholds (relative to the triangles' bounds) in the sliver collapse and the rounding repair
            DMesh3 m = BoxSmallSphereAndFarUnusedVertex();
            int n = m.TriangleCount;
            Assert.Equal(0, MeshRobustRepair.CollapseSlivers(m));
            var rounding = new MeshRoundingRepair(m);
            rounding.Apply();
            Assert.Equal(0, rounding.InitialDegenerate);
            Assert.Equal(n, m.TriangleCount);
        }

        [Fact]
        public void CleanButASmallPartLost_InputUsed()
        {
            // a unit-radius sphere and a small separate sphere (0.15: 0.3 % of the volume, within the IoU of 0.99): a
            // repair that drops the small one passed the volume check; the lost component is noticed now
            DMesh3 big = Sphere(1, Vector3d.Zero), small = Sphere(0.15, new Vector3d(3, 0, 0));
            DMesh3 input = new DMesh3(big);
            MeshEditor.Append(input, small);
            Assert.Equal(1, MeshRepairMetrics.LostComponents(input, big));
            Assert.Equal(0, MeshRepairMetrics.LostComponents(input, new DMesh3(input)));
            var r = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(big), CheckLostComponents = true };
            Assert.True(r.Apply());
            Assert.Equal("parts of the input lost", r.FallbackReason);
            Assert.True(r.UsedInput);
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal(Volume(big) + Volume(small), Volume(r.Mesh), 6);
            // without the check (the default): accepted as before; MinVolumeIoU = 0 turns off all volume checks
            var off = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(big) };
            Assert.True(off.Apply());
            Assert.Equal("", off.FallbackReason);
            var noVolume = new MeshRobustRepair(new DMesh3(input)) { RepairF = (m) => new DMesh3(big), MinVolumeIoU = 0, CheckLostComponents = true };
            Assert.True(noVolume.Apply());
            Assert.Equal("", noVolume.FallbackReason);
        }

        [Fact]
        public void LostComponents_SpecksBelowAThousandthOfTheDiagonalNotJudged()
        {
            // a closed speck (1e-4 across, as MeshAutoRepair's MinEdgeLengthTol removes) next to a unit sphere: losing it
            // is not a lost component (a fallback would lose it as well); a part 1e-2 across is one
            DMesh3 big = Sphere(1, Vector3d.Zero);
            DMesh3 withSpeck = new DMesh3(big);
            MeshEditor.Append(withSpeck, Box(3, 0, 0, 3.0001, 0.0001, 0.0001));
            Assert.Equal(0, MeshRepairMetrics.LostComponents(withSpeck, big));
            var r = new MeshRobustRepair(new DMesh3(withSpeck)) { RepairF = (m) => new DMesh3(big), CheckLostComponents = true };
            Assert.True(r.Apply());
            Assert.Equal("", r.FallbackReason);
            DMesh3 withPart = new DMesh3(big);
            MeshEditor.Append(withPart, Box(3, 0, 0, 3.01, 0.01, 0.01));
            Assert.Equal(1, MeshRepairMetrics.LostComponents(withPart, big));
        }

        [Fact]
        public void LostComponents_RepairsThatKeepTheSolidLoseNothing()
        {
            // overlapping spheres resolved into one shell, an inside-out sphere flipped, a cavity filled partly: the
            // components' solids are kept, nothing counts as lost; a hollow sphere whose cavity is filled loses nothing
            // either (the cavity component's solid is the shell material, which is kept)
            DMesh3 overlapping = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(overlapping, Sphere(1, new Vector3d(1.2, 0, 0)));
            var union = new ExactMeshBoolean(Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(1.2, 0, 0)), ExactMeshBoolean.Operations.Union);
            Assert.True(union.Compute());
            Assert.Equal(0, MeshRepairMetrics.LostComponents(overlapping, union.Result));
            DMesh3 inverted = Sphere(1, Vector3d.Zero);
            inverted.ReverseOrientation();
            Assert.Equal(0, MeshRepairMetrics.LostComponents(inverted, Sphere(1, Vector3d.Zero)));
            DMesh3 hollow = Sphere(1, Vector3d.Zero);
            DMesh3 cavity = Sphere(0.5, Vector3d.Zero);
            cavity.ReverseOrientation();
            MeshEditor.Append(hollow, cavity);
            Assert.Equal(0, MeshRepairMetrics.LostComponents(hollow, Sphere(1, Vector3d.Zero)));
            // a thin open sheet has no solid to lose
            DMesh3 sheet = new DMesh3();
            sheet.AppendTriangle(sheet.AppendVertex(Vector3d.Zero), sheet.AppendVertex(Vector3d.AxisX), sheet.AppendVertex(Vector3d.AxisY));
            Assert.Equal(0, MeshRepairMetrics.LostComponents(sheet, new DMesh3()));
            // everything lost
            Assert.Equal(2, MeshRepairMetrics.LostComponents(overlapping, new DMesh3()));
            // an inside-out part counts as its solid (oriented copy), so losing it is noticed
            DMesh3 withInverted = Sphere(1, Vector3d.Zero);
            DMesh3 smallInverted = Sphere(0.15, new Vector3d(3, 0, 0));
            smallInverted.ReverseOrientation();
            MeshEditor.Append(withInverted, smallInverted);
            Assert.Equal(1, MeshRepairMetrics.LostComponents(withInverted, Sphere(1, Vector3d.Zero)));
            // a sphere as a soup of single triangles: open components, not judged (near each, the solid is that of the
            // triangles around it; on Thingi10K such fragments gave false alarms)
            DMesh3 soup = new DMesh3();
            DMesh3 sphere = Sphere(1, Vector3d.Zero);
            foreach (int t in sphere.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                sphere.GetTriVertices(t, ref a, ref b, ref c);
                soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
            }
            Assert.Equal(0, MeshRepairMetrics.LostComponents(soup, new DMesh3()));
            // a component whose box holds only a few points of the solid (a thin slab of it, 0.3 %: 5 of 1024 samples): too few to judge
            // it, not counted as lost; a thicker slab (3 %) is judged
            Func<double, int> slab = (t) => MeshRepairMetrics.LostComponents(
                new MeshRepairMetrics.SolidReference(Box(0, 0, 0, 1, 1, 1), (p) => p.x < t), (p) => false);
            Assert.Equal(0, slab(0.003));
            Assert.Equal(1, slab(0.03));
        }

        [Fact]
        public void TouchingSolids_SeparatedAfterTheExactResolve()
        {
            // boxes touching along an edge, one of them overlapped by a third box: the resolve splits the touching
            // edge into two manifold sheets with duplicated vertices, which touch (a self-intersection by the exact
            // check); they are separated minimally, so the exact result is used
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            MeshEditor.Append(m, Box(1, 1, 0, 2, 2, 1));
            MeshEditor.Append(m, Box(0.5, 0.5, 0.25, 0.75, 0.75, 1.5));
            var r = new MeshRobustRepair(m) { RepairF = (x) => new DMesh3(x) };
            Assert.True(r.Apply());
            Assert.Equal("self-intersections", r.FallbackReason);
            Assert.True(r.UsedExactResolve, r.ExactResolveReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            double v = 2 + 0.25 * 0.25 * 0.5;       // two unit boxes and the part of the third above the first
            Assert.Equal(v, Volume(r.Mesh), 3);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1e-6)]
        public void SeparateTouchingVertices_AtUtmCoordinates(double size)
        {
            // the move (1e-4 of the distance to the neighbours, 1e-10 for a 1 micrometre part) is below the rounding of
            // y = 5.8e6 (9e-10), but it has x and z components, which are resolved finer: the contact is removed
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            MeshEditor.Append(m, Box(1, 1, 0, 2, 2, 1));
            MeshTransforms.Scale(m, size);
            MeshTransforms.Translate(m, new Vector3d(512345.678, 5812345.678, 312.5));
            DMesh3 res = ExactMeshBoolean.Resolve(m);
            Assert.True(MeshRepairMetrics.SelfIntersections(res) > 0);
            Assert.Equal(4, MeshRobustRepair.SeparateTouchingVertices(res, 1e-4));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(res));
        }

        [Fact]
        public void SeparateTouchingVertices_RemovesTheContact()
        {
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            MeshEditor.Append(m, Box(1, 1, 0, 2, 2, 1));
            DMesh3 res = ExactMeshBoolean.Resolve(m);
            Assert.True(MeshRepairMetrics.SelfIntersections(res) > 0);
            var before = res.Vertices().ToList();
            Assert.Equal(4, MeshRobustRepair.SeparateTouchingVertices(res, 1e-4));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(res));
            // moved by a tiny amount only
            var after = res.Vertices().ToList();
            Assert.True(before.Zip(after, (a, b) => a.Distance(b)).Max() < 1e-3);
            Assert.True(before.Zip(after, (a, b) => a.Distance(b)).Max() > 0);
            // nothing to do on a mesh without duplicated vertices
            Assert.Equal(0, MeshRobustRepair.SeparateTouchingVertices(Box(0, 0, 0, 1, 1, 1), 1e-4));
        }

        static int Degenerate(DMesh3 m)
        {
            double d = m.CachedBounds.DiagonalLength;
            return m.TriangleIndices().Count(t => m.GetTriArea(t) < 1e-12 * d * d);
        }

        [Fact]
        public void CollapseSlivers_RemovesNeedles()
        {
            // a box edge split at a point 1e-13 from its end: two needles (area below 1e-12 * diagonal^2)
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            int eid = m.EdgeIndices().First(e => m.IsBoundaryEdge(e) == false);
            Index2i ev = m.GetEdgeV(eid);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out split));
            Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
            m.SetVertex(split.vNew, a + 1e-13 * (b - a));
            Assert.Equal(2, Degenerate(m));
            Assert.True(MeshRobustRepair.CollapseSlivers(m) > 0);
            Assert.Equal(0, Degenerate(m));
            Assert.True(m.IsClosed());
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(1.0, Volume(m), 9);
            // nothing to do on a clean mesh
            Assert.Equal(0, MeshRobustRepair.CollapseSlivers(Box(0, 0, 0, 1, 1, 1)));
        }

        [Fact]
        public void CollapseSlivers_FlipsCaps()
        {
            // a face triangle poked at a point 1e-13 off its edge, in the face's plane: a cap (no short edge, so no
            // collapse that would move the box) that a flip of its long edge removes
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            int tid = m.TriangleIndices().First();
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
            DMesh3.PokeTriangleInfo poke;
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(tid, out poke));
            m.SetVertex(poke.new_vid, 0.5 * (a + b) + 1e-13 * (c - 0.5 * (a + b)));
            Assert.Equal(1, Degenerate(m));
            var before = m.Vertices().ToList();
            Assert.True(MeshRobustRepair.CollapseSlivers(m) > 0);
            Assert.Equal(0, Degenerate(m));
            Assert.True(m.IsClosed());
            Assert.Equal(1.0, Volume(m), 12);
            Assert.Equal(before, m.Vertices().ToList());     // no vertex moved
        }

        [Fact]
        public void CollapseSlivers_RemovesTinyComponents()
        {
            // a flat tetrahedron of size 1e-7 next to a box (as where intersection points nearly coincide): its
            // edges cannot be collapsed, the whole component is removed
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            int n = m.TriangleCount;
            Vector3d o = new Vector3d(2, 0, 0);
            int v0 = m.AppendVertex(o), v1 = m.AppendVertex(o + new Vector3d(1e-7, 0, 0)),
                v2 = m.AppendVertex(o + new Vector3d(0, 1e-7, 0)), v3 = m.AppendVertex(o + new Vector3d(3e-8, 3e-8, 1e-15));
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v1, v2, v3); m.AppendTriangle(v2, v0, v3);
            Assert.True(Degenerate(m) > 0);
            Assert.True(MeshRobustRepair.CollapseSlivers(m) > 0);
            Assert.Equal(0, Degenerate(m));
            Assert.Equal(n, m.TriangleCount);
            Assert.True(m.IsClosed());
            Assert.Equal(1.0, Volume(m), 9);
        }

        [Fact]
        public void CollapseSlivers_KeepsTinyOpenComponents()
        {
            // a tiny open sliver sheet next to a box: it may close another part by position, so it is not removed
            // (only closed tiny components are)
            DMesh3 m = Box(0, 0, 0, 1, 1, 1);
            int n = m.TriangleCount;
            Vector3d o = new Vector3d(2, 0, 0);
            int v0 = m.AppendVertex(o), v1 = m.AppendVertex(o + new Vector3d(1e-7, 0, 0)),
                v2 = m.AppendVertex(o + new Vector3d(5e-8, 1e-15, 0)), v3 = m.AppendVertex(o + new Vector3d(5e-8, -1e-7, 0));
            m.AppendTriangle(v0, v1, v2); m.AppendTriangle(v0, v3, v1);
            Assert.True(Degenerate(m) > 0);
            MeshRobustRepair.CollapseSlivers(m);
            Assert.True(m.TriangleCount > n);
            Assert.True(m.IsVertex(v3));
        }

        [Fact]
        public void InsideOutInput_RepairKeptNotSolidified()
        {
            // two disjoint spheres, one inside out: the repair flips it, the result is the solid of both
            // (with the raw winding number as reference only one sphere was the input solid: IoU 0.5, and
            // the good repair was replaced by a solidify)
            DMesh3 m = Sphere(1, Vector3d.Zero);
            DMesh3 inv = Sphere(1, new Vector3d(3, 0, 0));
            inv.ReverseOrientation(false);
            MeshEditor.Append(m, inv);
            int n = m.TriangleCount;
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.False(r.UsedSolidify, r.FallbackReason);
            Assert.True(r.RepairVolumeIoU > 0.99, $"IoU {r.RepairVolumeIoU}");
            Assert.Equal(n, r.Mesh.TriangleCount);
            Assert.True(MeshMeasurements.VolumeArea(r.Mesh, r.Mesh.TriangleIndices(), r.Mesh.GetVertex).x > 0);
        }

        [Fact]
        public void NestedPartAsSoup_ResolvedAsSolidNotCavity()
        {
            // a sphere inside a bigger one, given as unwelded triangles: its winding number adds to the outer
            // sphere's (the input's solid is the outer ball). The repair welds it into a closed sphere, which
            // the cleanup takes for a cavity (nested in another component); oriented by the input's solid
            // before the exact resolve, it is an inner shell instead, and the resolve gives the outer ball.
            // The outer sphere has a small hole, so the input itself is not closed and cannot be resolved
            DMesh3 m = Sphere(1, Vector3d.Zero);
            m.RemoveTriangle(m.TriangleIndices().First());
            DMesh3 inner = Sphere(0.6, new Vector3d(0.1, 0, 0));
            foreach (int tid in inner.TriangleIndices()) {
                Index3i t = inner.GetTriangle(tid);
                m.AppendTriangle(m.AppendVertex(inner.GetVertex(t.a)), m.AppendVertex(inner.GetVertex(t.b)), m.AppendVertex(inner.GetVertex(t.c)));
            }
            double outer = Volume(Sphere(1, Vector3d.Zero));
            var r = new MeshRobustRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.UsedExactResolve, r.FallbackReason + " / " + r.ExactResolveReason);
            Assert.False(r.UsedSolidify);
            Assert.False(r.UsedInput);      // the repair result was resolved, not the input
            // the clean result whose volume differs is not resolved as it is (that gives its own solid again), only
            // oriented by the input: one resolve
            Assert.Equal(1, r.ExactResolveRoundsUsed);
            Assert.Equal(outer, Volume(r.Mesh), 6);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        // thin rotated plates (about a cell thick at resolution 64), overlapping: the solidify leaves
        // self-intersections where the sheets meet in few cells
        static DMesh3 ThinPlates(int seed)
        {
            var rnd = new Random(seed);
            DMesh3 m = new DMesh3();
            for (int i = 0; i < 12; ++i) {
                var c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                var e = new Vector3d(0.3 + 0.2 * rnd.NextDouble(), 0.02 + 0.03 * rnd.NextDouble(), 0.3);
                var b = new TrivialBox3Generator() { Box = new Box3d(c, e) }.Generate().MakeDMesh();
                var axis = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5).Normalized;
                MeshTransforms.Rotate(b, c, Quaterniond.AxisAngleD(axis, 360 * rnd.NextDouble()));
                MeshEditor.Append(m, b);
            }
            return m;
        }

        [Fact]
        public void SolidifyNotClean_AlphaWrapped()
        {
            // the repair keeps the self-intersections, the exact stage is off: the solidify result still
            // self-intersects; the alpha wrap is clean and encloses the input
            Func<bool, MeshRobustRepair> run = (wrap) => {
                var r = new MeshRobustRepair(ThinPlates(6)) { RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64, AlphaWrapFallback = wrap };
                Assert.True(r.Apply());
                return r;
            };
            var without = run(false);
            Assert.True(without.UsedSolidify);
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(without.Mesh));
            Assert.Equal("self-intersections", without.ResultReason);      // returned all the same: said so

            var with = run(true);
            Assert.Equal("self-intersections", with.SolidifyReason);
            // stopped before the post-processing of the discarded solidify
            Assert.True(with.Solidify.StoppedOnSelfIntersections);
            Assert.False(with.PhaseMilliseconds.ContainsKey("solidify.reduce"));
            Assert.True(with.UsedAlphaWrap);
            Assert.False(with.UsedSolidify);
            Assert.Equal("", with.WrapReason);
            Assert.Equal("", with.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(with.Mesh));
            double missing, extra;
            double iou = MeshRepairMetrics.VolumeIoU(ThinPlates(6), with.Mesh, out missing, out extra);
            Assert.True(missing < 0.01, $"missing {missing}");     // the wrap encloses the input
        }

        static bool SameMesh(DMesh3 a, DMesh3 b)
        {
            return a.VertexCount == b.VertexCount && a.TriangleCount == b.TriangleCount
                && a.Vertices().Zip(b.Vertices(), (p, q) => p == q).All(x => x)
                && a.TriangleIndices().All(t => a.GetTriangle(t) == b.GetTriangle(t));
        }

        [Fact]
        public void Solidify_StopsOnSelfIntersectingContour_FinishGivesTheFullResult()
        {
            // the contour of the overlapping thin plates self-intersects: no cleanup and reduction, the raw
            // contour is the result; FinishPostProcess post-processes it as Compute() does without stopping
            // (compared on the same contour: the contouring is not bit-for-bit deterministic across runs)
            Func<bool, MeshWindingNumberSolidify> run = (stop) => {
                var s = new MeshWindingNumberSolidify(ThinPlates(6)) { GridResolution = 64, Adaptive = true, StopIfSelfIntersecting = stop };
                Assert.True(s.Compute());
                return s;
            };
            var full = run(false);
            Assert.False(full.StoppedOnSelfIntersections);
            Assert.True(MeshRepairMetrics.HasSelfIntersections(full.Result));

            var stopped = run(true);
            Assert.True(stopped.StoppedOnSelfIntersections);
            Assert.True(MeshRepairMetrics.HasSelfIntersections(stopped.Result));
            Assert.False(stopped.PhaseMilliseconds.ContainsKey("reduce"));
            Assert.False(stopped.PhaseMilliseconds.ContainsKey("cleanup"));
            Assert.True(stopped.Result.TriangleCount > full.Result.TriangleCount);   // not reduced

            var expected = WindingContouring.PostProcess(new DMesh3(stopped.Result), stopped.CleanupResult, stopped.ReduceTolerance,
                stopped.UsedCellSize, () => false, (name) => { });
            stopped.FinishPostProcess();
            Assert.False(stopped.StoppedOnSelfIntersections);
            Assert.True(stopped.PhaseMilliseconds.ContainsKey("reduce"));
            Assert.True(SameMesh(expected, stopped.Result));
            Assert.InRange(stopped.Result.TriangleCount, 0.9 * full.Result.TriangleCount, 1.1 * full.Result.TriangleCount);

            // reused: a Compute() that fails early does not leave the flag of the previous one
            var again = run(true);
            Assert.True(again.StoppedOnSelfIntersections);
            again.CellSize = 1e-12;     // too deep an octree
            Assert.False(again.Compute());
            Assert.False(again.StoppedOnSelfIntersections);
        }

        [Fact]
        public void Solidify_CleanContour_NotStoppedAndNotRechecked()
        {
            // the dual contouring verified the contour: no extra check ("selfint"), the same result
            Func<bool, MeshWindingNumberSolidify> run = (stop) => {
                var s = new MeshWindingNumberSolidify(Sphere(1, Vector3d.Zero)) { GridResolution = 64, Adaptive = true, StopIfSelfIntersecting = stop };
                Assert.True(s.Compute());
                return s;
            };
            var stopped = run(true);
            Assert.False(stopped.StoppedOnSelfIntersections);
            Assert.False(stopped.PhaseMilliseconds.ContainsKey("selfint"));
            Assert.True(stopped.PhaseMilliseconds.ContainsKey("reduce"));
            int full = run(false).Result.TriangleCount;
            Assert.InRange(stopped.Result.TriangleCount, 0.9 * full, 1.1 * full);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(stopped.Result));
        }

        [Fact]
        public void SolidifyNotClean_WrapFails_PostProcessedSolidifyUsed()
        {
            // the solidify is stopped on its self-intersections for the wrap; if the wrap fails (here: invalid
            // alpha), the solidify result is used as before: post-processed (reduced to about the triangles of
            // the run without the wrap; not bit-for-bit, the contouring is not deterministic across runs)
            Func<bool, MeshRobustRepair> run = (wrap) => {
                var r = new MeshRobustRepair(ThinPlates(6)) {
                    RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64,
                    AlphaWrapFallback = wrap, WrapRelativeAlpha = -1
                };
                Assert.True(r.Apply());
                return r;
            };
            var without = run(false);
            Assert.True(without.UsedSolidify);
            Assert.False(without.Solidify.StoppedOnSelfIntersections);

            var failed = run(true);
            Assert.Equal(MeshAlphaWrap.WrapStatus.InvalidParameters, failed.AlphaWrap.Status);
            Assert.False(failed.UsedAlphaWrap);
            Assert.True(failed.UsedSolidify);
            Assert.Equal("self-intersections", failed.SolidifyReason);
            Assert.True(failed.PhaseMilliseconds.ContainsKey("solidify.finish"));
            Assert.True(failed.PhaseMilliseconds.ContainsKey("solidify.reduce"));     // the finish's phases reported
            Assert.False(failed.Solidify.StoppedOnSelfIntersections);
            Assert.Same(failed.Solidify.Result, failed.Mesh);
            Assert.InRange(failed.Mesh.TriangleCount, 0.9 * without.Mesh.TriangleCount, 1.1 * without.Mesh.TriangleCount);
        }

        [Fact]
        public void CancelledDuringWrap_NoFinish()
        {
            // cancelled once the wrap has started: Apply() returns false without post-processing the solidify
            MeshRobustRepair r = null;
            r = new MeshRobustRepair(ThinPlates(6)) {
                RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64,
                CancelF = () => r.AlphaWrap != null
            };
            Assert.False(r.Apply());
            Assert.Equal(MeshAlphaWrap.WrapStatus.Cancelled, r.AlphaWrap.Status);
            Assert.True(r.Solidify.StoppedOnSelfIntersections);
            Assert.False(r.PhaseMilliseconds.ContainsKey("solidify.finish"));
        }

        [Fact]
        public void WrapFailed_ReasonReported()
        {
            // the wrap fails (alpha 0: invalid parameters): the unclean solidify result is used, and why the wrap was
            // not is said
            var r = new MeshRobustRepair(ThinPlates(6)) { RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64, WrapRelativeAlpha = 0 };
            Assert.True(r.Apply());
            Assert.True(r.UsedSolidify);
            Assert.False(r.UsedAlphaWrap);
            Assert.StartsWith("InvalidParameters", r.WrapReason);
        }

        [Fact]
        public void CleanSolidify_NotWrapped()
        {
            var r = new MeshRobustRepair(Sphere(1, Vector3d.Zero)) { RepairF = (m) => { var c = new DMesh3(m); MeshEditor.Append(c, Sphere(1, new Vector3d(1.2, 0, 0))); return c; }, ExactResolve = false };
            Assert.True(r.Apply());
            Assert.True(r.UsedSolidify);
            Assert.False(r.UsedAlphaWrap);
            Assert.Equal("", r.SolidifyReason);
            Assert.Equal("", r.ResultReason);
            Assert.Null(r.AlphaWrap);
        }
    }
}
