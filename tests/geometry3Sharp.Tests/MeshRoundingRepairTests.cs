using System;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    // Rounding the exact output of ExactMeshBoolean to double without new self-intersections or degenerate
    // slivers (ExactMeshBoolean.SafeRounding, MeshRoundingRepair), and the exact output coordinates.
    public class MeshRoundingRepairTests
    {
        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        // [-1, 1]^3 and a copy rotated about axis by angle (radians) and shifted by shift
        static DMesh3[] NearlyCoincident(DMesh3 a, Vector3d axis, double angle, Vector3d shift)
        {
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(axis.Normalized, angle * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, shift);
            return new[] { a, b };
        }

        static DMesh3 Cube()
        {
            return new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(int edgeVertices)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        static int Degenerate(DMesh3 m)
        {
            double diag = m.CachedBounds.DiagonalLength;
            return m.TriangleIndices().Count(t => m.GetTriArea(t) < 1e-12 * diag * diag);
        }

        static ExactMeshBoolean Run(DMesh3[] ab, Ops op, bool safe)
        {
            var b = new ExactMeshBoolean(ab[0], ab[1], op) { SafeRounding = safe };
            Assert.True(b.Compute(), b.Error);
            Assert.True(b.Result.CheckValidity(true, FailMode.ReturnOnly));
            return b;
        }

        // the result is clean (closed, no bowties, no degenerate triangles, no inside-out components, no
        // self-intersections), checked independently of MeshRoundingRepair's own counts
        static void AssertClean(ExactMeshBoolean b)
        {
            Assert.True(b.RoundingClean, $"remaining {b.Rounding.RemainingIntersections} intersections, {b.Rounding.RemainingDegenerate} degenerate");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result, true));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(b.Result));
            Assert.Equal(0, Degenerate(b.Result));
        }


        [Fact]
        public void NearlyCoincidentBoxes_Intersection_RoundingMadeSelfIntersections()
        {
            // the cube and a copy turned by 1e-10: intersection points 1e-10 apart and closer
            var ab = NearlyCoincident(Cube(), new Vector3d(0.19758121236114823, 0.3649866608274107, -0.30151001680712686), 1e-10,
                1e-10 * new Vector3d(0.5604903733173806, 0.1805782146661441, 0.2501971587772468));
            var plain = Run(ab, Ops.Intersection, false);
            // without the repair: rounding left self-intersections and slivers
            Assert.True(MeshRepairMetrics.SelfIntersections(plain.Result) > 0);
            Assert.True(Degenerate(plain.Result) > 0);
            Assert.Null(plain.Rounding);

            var safe = Run(ab, Ops.Intersection, true);
            AssertClean(safe);
            Assert.True(safe.Rounding.InitialIntersections > 0);
            Assert.Equal(8.0, Volume(safe.Result), 6);
        }

        [Theory]
        [InlineData(1e-9)]
        [InlineData(1e-11)]
        [InlineData(1e-13)]
        public void NearlyCoincidentBoxes_UnionIntersection_NoSlivers(double eps)
        {
            var ab = NearlyCoincident(Cube(), new Vector3d(0.3, 0.5, 0.8), eps, eps * new Vector3d(0.7, 0.2, 0.4));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                Assert.True(Degenerate(Run(ab, op, false).Result) > 0);
                var safe = Run(ab, op, true);
                AssertClean(safe);
                Assert.Equal(8.0, Volume(safe.Result), 6);
            }
        }

        [Fact]
        public void NearlyCoincidentSpheres_UnionIntersection_Clean()
        {
            // intersection curves that cross each other everywhere, at 1e-14
            var ab = NearlyCoincident(Sphere(4), new Vector3d(-0.14007338678467712, -0.23429536387990013, 0.3179190721446271), 1e-14,
                1e-14 * new Vector3d(0.5500672480790257, 0.4904806467194486, 0.45565309583007035));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                var plain = Run(ab, op, false);
                Assert.True(MeshRepairMetrics.SelfIntersections(plain.Result) > 0);
                var safe = Run(ab, op, true);
                AssertClean(safe);
                Assert.Equal(Volume(plain.Result), Volume(safe.Result), 6);
            }
        }

        [Fact]
        public void NearlyCoincidentSpheres_Difference_ThinSliversRemoved()
        {
            // the difference of nearly coincident solids is a set of slivers thinner than 1e-10: removed as
            // components thinner than MaxDisplacement (their volume is below 1e-9)
            foreach (double eps in new[] { 1e-8, 1e-10, 1e-12 }) {
                var ab = NearlyCoincident(Sphere(10), new Vector3d(0.6, 0.3, 0.1), eps, eps * new Vector3d(0.2, 0.9, 0.5));
                var plain = Run(ab, Ops.Difference, false);
                Assert.True(Degenerate(plain.Result) > 0);
                Assert.True(Math.Abs(Volume(plain.Result)) < 1e-6);
                var safe = Run(ab, Ops.Difference, true);
                Assert.True(safe.RoundingClean);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(safe.Result));
                Assert.Equal(0, Degenerate(safe.Result));
                Assert.True(safe.Result.TriangleCount == 0 || safe.Result.IsClosed());
                Assert.True(Math.Abs(Volume(safe.Result)) < 1e-6);
            }
        }

        [Fact]
        public void GeneralPosition_SafeRoundingChangesNothing()
        {
            // nothing to repair: the same result as without
            DMesh3 a = Sphere(8), b = Sphere(8);
            MeshTransforms.Translate(b, new Vector3d(0.6, 0.3, 0.2));
            var plain = Run(new[] { a, b }, Ops.Union, false);
            var safe = Run(new[] { a, b }, Ops.Union, true);
            AssertClean(safe);
            Assert.Equal(0, safe.Rounding.Collapses + safe.Rounding.Flips + safe.Rounding.Moves + safe.Rounding.Zips + safe.Rounding.RemovedTriangles);
            Assert.Equal(plain.Result.VertexCount, safe.Result.VertexCount);
            foreach (int vid in plain.Result.VertexIndices())
                Assert.Equal(plain.Result.GetVertex(vid), safe.Result.GetVertex(vid));
        }

        [Fact]
        public void TouchingSolids_SharedPositionsStillTouch()
        {
            // two cubes that touch along an edge: the result has duplicated vertices on the edge (manifold sheets
            // that touch); with SharedPositionsTouch they are not intersections and stay where they are
            DMesh3 a = Cube(), b = Cube();
            MeshTransforms.Translate(b, new Vector3d(2, 2, 0));
            var plain = Run(new[] { a, b }, Ops.Union, false);
            var safe = Run(new[] { a, b }, Ops.Union, true);
            Assert.True(safe.RoundingClean);
            Assert.Equal(0, safe.Rounding.Moves + safe.Rounding.Collapses);
            Assert.Equal(16.0, Volume(safe.Result), 12);
            Assert.Equal(plain.Result.VertexCount, safe.Result.VertexCount);
            // strictly (by vertex ids) the touching sheets intersect
            var strict = new MeshRoundingRepair(new DMesh3(plain.Result)) { MaxPasses = 0 };
            strict.Apply();
            Assert.True(strict.InitialIntersections > 0);
        }

        // positions where more than one vertex is
        static System.Collections.Generic.HashSet<Vector3d> SharedPositions(DMesh3 m)
        {
            return new System.Collections.Generic.HashSet<Vector3d>(m.VertexIndices().GroupBy(v => m.GetVertex(v)).Where(g => g.Count() > 1).Select(g => g.Key));
        }

        [Fact]
        public void TouchingSolids_NeedleAtTheContact_ContactStaysAPoint()
        {
            // two cubes touching at a corner; at the corner of the first, an edge is split 1e-13 from the corner (a
            // needle and a cap at the contact): the repair contracts it into the corner, and the sheets still touch
            // only there
            DMesh3 a = Cube(), b = Cube();
            MeshTransforms.Translate(b, new Vector3d(2, 2, 2));
            Vector3d corner = new Vector3d(1, 1, 1);
            int vc = a.VertexIndices().First(v => a.GetVertex(v) == corner);
            int eid = a.VtxEdgesItr(vc).First();
            Index2i ev = a.GetEdgeV(eid);
            Vector3d other = a.GetVertex(ev.a == vc ? ev.b : ev.a);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, a.SplitEdge(eid, out split));
            a.SetVertex(split.vNew, corner + 1e-13 * (other - corner).Normalized);

            var plain = Run(new[] { a, b }, Ops.Union, false);
            Assert.True(Degenerate(plain.Result) > 0);
            Assert.Equal(new[] { corner }, SharedPositions(plain.Result).ToArray());
            var safe = Run(new[] { a, b }, Ops.Union, true);
            Assert.True(safe.RoundingClean);
            Assert.True(safe.Rounding.Collapses > 0);
            Assert.Equal(0, Degenerate(safe.Result));
            Assert.Equal(new[] { corner }, SharedPositions(safe.Result).ToArray());
            Assert.Equal(16.0, Volume(safe.Result), 9);
            // no intersections beyond the contact point
            var check = new MeshRoundingRepair(new DMesh3(safe.Result)) { SharedPositionsTouch = true, MaxPasses = 0 };
            check.Apply();
            Assert.Equal(0, check.InitialIntersections);
        }

        [Fact]
        public void Resolve_SafeRounding()
        {
            // a self-intersecting mesh: two nearly coincident cubes in one mesh
            var ab = NearlyCoincident(Cube(), new Vector3d(0.3, 0.5, 0.8), 1e-11, 1e-11 * new Vector3d(0.7, 0.2, 0.4));
            DMesh3 both = new DMesh3(ab[0]);
            new MeshEditor(both).AppendMesh(ab[1]);
            DMesh3 plain = ExactMeshBoolean.Resolve(both);
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(plain, false));
            DMesh3 safe = ExactMeshBoolean.Resolve(both, true);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(safe, true));
            Assert.Equal(8.0, Volume(safe), 6);
        }

        [Fact]
        public void RobustRepair_UsesTheExactStage()
        {
            // two nearly coincident spheres in one mesh: the exact resolve gives slivers (before CollapseSlivers the
            // solidify was used here); with SafeRounding the first resolve is clean, verified exactly
            var ab = NearlyCoincident(Sphere(6), new Vector3d(0.6, 0.3, 0.1), 1e-10, 1e-10 * new Vector3d(0.2, 0.9, 0.5));
            DMesh3 both = new DMesh3(ab[0]);
            new MeshEditor(both).AppendMesh(ab[1]);
            Assert.Equal("degenerate triangles", MeshRobustRepair.NotCleanReason(ExactMeshBoolean.Resolve(both), false));
            var robust = new MeshRobustRepair(new DMesh3(both)) { AlphaWrapFallback = false };
            Assert.True(robust.Apply());
            Assert.True(robust.UsedExactResolve, $"exact: {robust.ExactResolveReason}");
            Assert.Equal(1, robust.ExactResolveRoundsUsed);
            Assert.Single(robust.ExactRoundingEdits);
            Assert.True(robust.ExactRoundingEdits[0].a > 0);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(robust.Mesh, true));
            Assert.Equal(Volume(ab[0]), Volume(robust.Mesh), 6);
        }


        [Fact]
        public void Repair_Needles_Collapsed()
        {
            // edges of a sphere split 1e-13 from their end: needles and caps, repaired back to a clean sphere
            DMesh3 m = Sphere(6);
            double v0 = Volume(m);
            int splits = 0;
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 7 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) != MeshResult.Ok)
                    continue;
                m.SetVertex(info.vNew, a + 1e-13 * (b - a).Normalized);
                splits++;
            }
            Assert.True(splits > 5);
            Assert.True(Degenerate(m) > 0);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.True(repair.Collapses >= splits);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, true));
            Assert.Equal(v0, Volume(m), 9);
        }

        [Fact]
        public void Repair_EvaluationLimit_StopsWithAValidMeshAndExactCounts()
        {
            DMesh3 m = Sphere(6);
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 7 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) == MeshResult.Ok)
                    m.SetVertex(info.vNew, a + 1e-13 * (b - a).Normalized);
            }
            int before = Degenerate(m);
            var repair = new MeshRoundingRepair(m) { MaxPairTests = 50 };
            Assert.False(repair.Apply());
            Assert.True(repair.EvaluationLimitReached);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(m.IsClosed());
            Assert.Equal(Degenerate(m), repair.RemainingDegenerate);
            Assert.True(repair.RemainingDegenerate > 0 && repair.RemainingDegenerate < before);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
        }

        [Fact]
        public void Repair_TinyOpenComponent_Kept()
        {
            // a sphere and, apart from it, a single tiny degenerate triangle: an open component, which may close
            // other parts by position (sheets touching along an edge), so it is not removed; a closed tiny one is
            DMesh3 m = Sphere(6);
            int n = m.TriangleCount;
            Vector3d p = new Vector3d(3, 0, 0);
            int a = m.AppendVertex(p), b = m.AppendVertex(p + new Vector3d(1e-7, 0, 0)), c = m.AppendVertex(p + new Vector3d(2e-7, 1e-20, 0));
            m.AppendTriangle(a, b, c);
            var repair = new MeshRoundingRepair(m);
            Assert.False(repair.Apply());
            Assert.Equal(0, repair.RemovedComponents);
            Assert.Equal(n + 1, m.TriangleCount);
            Assert.Equal(1, repair.RemainingDegenerate);

            // the same as a closed flat tetrahedron: removed
            DMesh3 t = Sphere(6);
            int[] v = { t.AppendVertex(p), t.AppendVertex(p + new Vector3d(1e-7, 0, 0)), t.AppendVertex(p + new Vector3d(0, 1e-7, 0)), t.AppendVertex(p + new Vector3d(3e-8, 3e-8, 1e-20)) };
            t.AppendTriangle(v[0], v[2], v[1]); t.AppendTriangle(v[0], v[1], v[3]); t.AppendTriangle(v[1], v[2], v[3]); t.AppendTriangle(v[2], v[0], v[3]);
            var repair2 = new MeshRoundingRepair(t);
            Assert.True(repair2.Apply());
            Assert.Equal(1, repair2.RemovedComponents);
            Assert.Equal(n, t.TriangleCount);
            Assert.True(t.IsClosed());
        }

        [Fact]
        public void Repair_TinyClosedComponent_Removed()
        {
            // a sphere and, apart from it, a closed cube of edge 1.9e-4 with a needle: tiny (area below 1e4 times the
            // degenerate area of 2.4e-11) but not thin (thickness 2 V / A = edge / 3 = 6e-5 above 1e-5 of the
            // diagonal, 4.9e-5): removed as tiny once the needle is contracted (an edited component)
            DMesh3 m = Sphere(6);
            int n = m.TriangleCount;
            DMesh3 cube = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(3, 0, 0), 0.95e-4 * Vector3d.One) }.Generate().MakeDMesh();
            int eid = cube.EdgeIndices().First();
            Index2i ev = cube.GetEdgeV(eid);
            Vector3d a = cube.GetVertex(ev.a), b = cube.GetVertex(ev.b);
            DMesh3.EdgeSplitInfo split;
            Assert.Equal(MeshResult.Ok, cube.SplitEdge(eid, out split));
            cube.SetVertex(split.vNew, a + 1e-12 * (b - a).Normalized);
            new MeshEditor(m).AppendMesh(cube);
            double diag = m.CachedBounds.DiagonalLength, areaEps = 1e-12 * diag * diag;
            Assert.True(6 * 1.9e-4 * 1.9e-4 < 1e4 * areaEps);
            Assert.True(1.9e-4 / 3 > 1e-5 * diag);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(1, repair.RemovedComponents);
            Assert.Equal(n, m.TriangleCount);
            Assert.True(m.IsClosed());
        }

        [Fact]
        public void Repair_VertexThroughThinWall_Moved()
        {
            // a slab 2e-4 thick whose top face has a vertex 1e-9 below the bottom face: separated by moving it
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1e-4)) }.Generate().MakeDMesh();
            int top = m.TriangleIndices().First(t => m.GetTriNormal(t).z > 0.5);
            DMesh3.PokeTriangleInfo poke;
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(top, out poke));
            Vector3d c = m.GetVertex(poke.new_vid);
            m.SetVertex(poke.new_vid, new Vector3d(c.x, c.y, -1e-4 - 1e-9));
            Assert.True(MeshRepairMetrics.SelfIntersections(m) > 0);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.True(repair.Moves > 0);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, true));
            // moved only a little
            Assert.True(m.GetVertex(poke.new_vid).z < -1e-4 + 1e-5);
        }

        [Fact]
        public void Repair_FixedVerticesKeepTheirPosition()
        {
            DMesh3 m = Sphere(6);
            var original = m.VertexIndices().ToDictionary(v => v, v => m.GetVertex(v));
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 5 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) == MeshResult.Ok)
                    m.SetVertex(info.vNew, a + 1e-12 * (b - a).Normalized);
            }
            var repair = new MeshRoundingRepair(m) { FixedVertexF = (v) => original.ContainsKey(v) };
            Assert.True(repair.Apply());
            Assert.True(repair.Collapses > 0);
            // the original vertices are kept, where they were; the new ones are contracted into them
            foreach (var kv in original) {
                Assert.True(m.IsVertex(kv.Key));
                Assert.Equal(kv.Value, m.GetVertex(kv.Key));
            }
            Assert.Equal(original.Count, m.VertexCount);
        }

        [Fact]
        public void Repair_FlipThatWouldFold_NotTaken()
        {
            // a cap T = (a, b, c) with c 1e-13 off the line through a and b, beyond b, and U = (b, a, d) across
            // (a, b): flipping (a, b) gives two triangles that fold onto each other, nearly coplanar, which no
            // intersection test sees (they share an edge and are not exactly coplanar); the apex of T is moved instead
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0));
            int c = m.AppendVertex(new Vector3d(2, 1e-13, 0)), d = m.AppendVertex(new Vector3d(2, -0.2, 0.3));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, d);
            Assert.Equal(1, Degenerate(m));
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(0, repair.Flips);
            Assert.Empty(MeshRepairMetrics.FoldedEdges(m));
            Assert.Equal(0, Degenerate(m));
        }


        [Fact]
        public void ExactVertices_RoundToTheResult()
        {
            var ab = NearlyCoincident(Cube(), new Vector3d(0.3, 0.5, 0.8), 1e-3, new Vector3d(0.1, 0.2, 0.05));
            var b = new ExactMeshBoolean(ab[0], ab[1], Ops.Intersection) { ComputeExactVertices = true };
            Assert.True(b.Compute());
            DMesh3 r = b.Result;
            ResultChecks.AssertClosed(r);
            Assert.NotNull(b.ResultExactVertices);
            Assert.Equal(r.MaxVertexID, b.ResultExactVertices.Length);
            int onFace = 0;
            foreach (int vid in r.VertexIndices()) {
                ExactPoint3 p = b.ResultExactVertices[vid];
                Assert.Equal(r.GetVertex(vid), p.ToVector3d());
                // exactly inside the cube [-1, 1]^3 (the intersection), many exactly on its faces
                for (int k = 0; k < 3; ++k) {
                    Assert.True(p[k] >= -p.W && p[k] <= p.W);
                    if (p[k] == p.W || p[k] == -p.W)
                        onFace++;
                }
            }
            Assert.True(onFace > 0);
            // not computed unless asked for
            var plain = new ExactMeshBoolean(ab[0], ab[1], Ops.Intersection);
            plain.Compute();
            Assert.Null(plain.ResultExactVertices);
        }

        [Fact]
        public void ExactVertices_AgreeWithTheArrangementsRounding()
        {
            // every point of the arrangement (input vertices and intersection points), mapped exactly to input
            // coordinates and rounded, is the double the arrangement gives for it
            DMesh3 a = Sphere(5), c = Sphere(5);
            MeshTransforms.Scale(a, 0.001);
            MeshTransforms.Scale(c, 0.001);
            MeshTransforms.Translate(a, new Vector3d(500000.25, -3.5, 7));
            MeshTransforms.Translate(c, new Vector3d(500000.2505, -3.4996, 7.0003));
            var arr = new ExactMeshArrangement(new System.Collections.Generic.List<DMesh3>() { a, c }) { SelfIntersections = true };
            arr.Compute();
            Assert.True(arr.PointCount > 100);
            for (int id = 0; id < arr.PointCount; ++id)
                Assert.Equal(arr.PointToVector(id), arr.ToInputExact(arr.Exact(id)).ToVector3d());
        }

        [Fact]
        public void NearlyCoincidentSpheres_Difference_RemovedComponentsCounted()
        {
            var ab = NearlyCoincident(Sphere(10), new Vector3d(0.6, 0.3, 0.1), 1e-10, 1e-10 * new Vector3d(0.2, 0.9, 0.5));
            var safe = Run(ab, Ops.Difference, true);
            Assert.True(safe.Rounding.RemovedComponents > 0);
            Assert.True(safe.Rounding.RemovedTriangles >= safe.Rounding.RemovedComponents);
            Assert.Equal(safe.Rounding.RemovedComponents, safe.Counts["rounding_removed_components"]);
        }

        [Fact]
        public void ExactVertices_ScaledAndShiftedInput()
        {
            // input coordinates far from the origin and not integers: the grid's scale and center are undone
            DMesh3 a = Sphere(5), c = Sphere(5);
            MeshTransforms.Scale(a, 0.001);
            MeshTransforms.Scale(c, 0.001);
            MeshTransforms.Translate(a, new Vector3d(500000.25, -3.5, 7));
            MeshTransforms.Translate(c, new Vector3d(500000.2505, -3.4996, 7.0003));
            var b = new ExactMeshBoolean(a, c, Ops.Union) { ComputeExactVertices = true };
            Assert.True(b.Compute());
            // far from the origin: a closed result, larger than one sphere and smaller than both
            ResultChecks.AssertClosed(b.Result);
            double one = ResultChecks.Volume(a);
            Assert.InRange(ResultChecks.Volume(b.Result), one, 2 * one);
            foreach (int vid in b.Result.VertexIndices())
                Assert.Equal(b.Result.GetVertex(vid), b.ResultExactVertices[vid].ToVector3d());
            // input vertices are exact doubles
            var inputs = new System.Collections.Generic.HashSet<Vector3d>(a.Vertices().Concat(c.Vertices()));
            int kept = 0;
            foreach (int vid in b.Result.VertexIndices()) {
                if (inputs.Contains(b.Result.GetVertex(vid)) == false) continue;
                ExactPoint3 p = b.ResultExactVertices[vid];
                Vector3d v = b.Result.GetVertex(vid);
                for (int k = 0; k < 3; ++k) {
                    System.Numerics.BigInteger m; int e;
                    ExactPoint3.Decompose(v[k], out m, out e);
                    // v[k] = m 2^e = p[k] / W
                    var lhs = p[k] * (e >= 0 ? System.Numerics.BigInteger.One : System.Numerics.BigInteger.Pow(2, -e));
                    var rhs = m * p.W * (e >= 0 ? System.Numerics.BigInteger.Pow(2, e) : System.Numerics.BigInteger.One);
                    Assert.Equal(rhs, lhs);
                }
                kept++;
            }
            Assert.True(kept > 10);
        }


        // ---- open meshes: edits along the boundary curve ----

        // the sphere without its cap above z = 0.3: open, one boundary loop
        static DMesh3 OpenSphere()
        {
            DMesh3 m = Sphere(6);
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > 0.3)
                    m.RemoveTriangle(t, true, false);
            Assert.Single(new MeshBoundaryLoops(m).Loops);
            return m;
        }

        static double DistanceToBoundary(DMesh3 m, Vector3d p)
        {
            return m.BoundaryEdgeIndices().Min(e => {
                Index2i ev = m.GetEdgeV(e);
                return Math.Sqrt(new Segment3d(m.GetVertex(ev.a), m.GetVertex(ev.b)).DistanceSquared(p));
            });
        }

        // the boundary of the repaired mesh m is that of before (a copy of m before the repair): as many loops, no
        // bowties, every boundary vertex was one before and is where it was or on the old curve; returns the number
        // of boundary vertices moved along the curve
        static int AssertBoundaryKept(DMesh3 before, DMesh3 m)
        {
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
            Assert.Equal(new MeshBoundaryLoops(before).Count, new MeshBoundaryLoops(m).Count);
            Assert.Empty(MeshIterators.BowtieVertices(m));
            int moved = 0;
            foreach (int v in m.VertexIndices().Where(v => m.IsBoundaryVertex(v))) {
                Assert.True(before.IsVertex(v) && before.IsBoundaryVertex(v), $"new boundary vertex {v}");
                if (m.GetVertex(v) == before.GetVertex(v))
                    continue;
                Assert.True(DistanceToBoundary(before, m.GetVertex(v)) < 1e-14, $"vertex {v} off the boundary");
                moved++;
            }
            return moved;
        }

        // splits the edges chosen by pick 1e-13 from their vertex end(edge vertices); returns the number of splits
        static int SplitNear(DMesh3 m, Func<int, bool> pick, Func<Index2i, int> end)
        {
            int splits = 0;
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (m.IsEdge(eid) == false || pick(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                int a = end(ev), b = (a == ev.a) ? ev.b : ev.a;
                Vector3d pa = m.GetVertex(a), pb = m.GetVertex(b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) != MeshResult.Ok)
                    continue;
                m.SetVertex(info.vNew, pa + 1e-13 * (pb - pa).Normalized);
                splits++;
            }
            return splits;
        }

        [Fact]
        public void Repair_NeedlesOnTheBoundary_ContractedAlongIt()
        {
            // boundary edges of an open mesh split 1e-13 from their end: needles with a short boundary edge, contracted
            // into the old boundary vertex, so that the boundary is as before
            DMesh3 m = OpenSphere();
            DMesh3 original = new DMesh3(m);
            int splits = SplitNear(m, e => m.IsBoundaryEdge(e) && e % 2 == 0, ev => ev.a);
            Assert.True(splits > 5);
            Assert.True(Degenerate(m) > 0);
            DMesh3 before = new DMesh3(m);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(0, Degenerate(m));
            Assert.True(repair.Collapses >= splits);
            Assert.Equal(0, AssertBoundaryKept(before, m));
            // the old mesh again, its vertices where they were
            Assert.Equal(original.VertexCount, m.VertexCount);
            foreach (int v in original.VertexIndices()) {
                Assert.True(m.IsVertex(v));
                Assert.Equal(original.GetVertex(v), m.GetVertex(v));
            }
        }

        // m with the vertex ids in reverse order (the edges' vertices in the other order)
        static DMesh3 ReversedIds(DMesh3 m)
        {
            var r = new DMesh3();
            var map = new System.Collections.Generic.Dictionary<int, int>();
            foreach (int v in m.VertexIndices().Reverse())
                map[v] = r.AppendVertex(m.GetVertex(v));
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                r.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]);
            }
            return r;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Repair_NeedlesAtTheBoundary_ContractedIntoTheBoundaryVertex(bool reversed)
        {
            // interior edges from a boundary vertex split 1e-13 from it: the new interior vertex is contracted into the
            // boundary vertex, which stays where it is (with the ids reversed, the boundary vertex is the edge's other end)
            DMesh3 m = reversed ? ReversedIds(OpenSphere()) : OpenSphere();
            DMesh3 original = new DMesh3(m);
            int splits = SplitNear(m, e => e % 3 == 0 && m.IsBoundaryVertex(m.GetEdgeV(e).a) != m.IsBoundaryVertex(m.GetEdgeV(e).b),
                ev => m.IsBoundaryVertex(ev.a) ? ev.a : ev.b);
            Assert.True(splits > 5);
            Assert.True(Degenerate(m) > 0);
            DMesh3 before = new DMesh3(m);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(0, Degenerate(m));
            Assert.True(repair.Collapses >= splits);
            Assert.Equal(0, AssertBoundaryKept(before, m));
            Assert.Equal(original.VertexCount, m.VertexCount);
            foreach (int v in original.VertexIndices()) {
                Assert.True(m.IsVertex(v));
                Assert.Equal(original.GetVertex(v), m.GetVertex(v));
            }
        }

        [Fact]
        public void Repair_BoundaryVertexThroughASheet_SlidAlongTheBoundary()
        {
            // a vertical open sheet (x-z plane, z in [1e-3, 1]) above a horizontal one at z = 0, one vertex of its lower
            // boundary 1e-9 below the horizontal sheet: moved along one of its boundary edges (which rise to the
            // neighbours at z = 1e-3) until it is above, not straight up off the boundary
            var m = new DMesh3();
            int w0 = m.AppendVertex(new Vector3d(-1, -1, 0)), w1 = m.AppendVertex(new Vector3d(2, -1, 0));
            int w2 = m.AppendVertex(new Vector3d(2, 1, 0)), w3 = m.AppendVertex(new Vector3d(-1, 1, 0));
            m.AppendTriangle(w0, w1, w2);
            m.AppendTriangle(w0, w2, w3);
            const int n = 10;
            var bottom = new int[n + 1];
            var top = new int[n + 1];
            for (int i = 0; i <= n; ++i) {
                bottom[i] = m.AppendVertex(new Vector3d(i / (double)n, 0, 1e-3));
                top[i] = m.AppendVertex(new Vector3d(i / (double)n, 0, 1));
            }
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(bottom[i], bottom[i + 1], top[i + 1]);
                m.AppendTriangle(bottom[i], top[i + 1], top[i]);
            }
            int v = bottom[n / 2];
            m.SetVertex(v, new Vector3d(0.5, 0, -1e-9));
            Assert.True(MeshRepairMetrics.SelfIntersections(m) > 0);
            DMesh3 before = new DMesh3(m);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            Assert.Equal(0, Degenerate(m));
            Assert.True(repair.Moves > 0);
            Assert.True(AssertBoundaryKept(before, m) > 0);
            Assert.True(m.GetVertex(v).z > 0 && m.GetVertex(v).Distance(before.GetVertex(v)) < 1e-5);
        }

        [Fact]
        public void Repair_StripPinchedToANeedle_NotContractedAcross()
        {
            // a strip of quads whose top boundary dips to 1e-13 above the bottom one at x = 2: the short edge between
            // the two boundaries is not contracted (it would pinch the strip into a bowtie; DMesh3.CollapseEdge refuses
            // it too); the top vertex is moved along the top boundary instead
            var m = new DMesh3();
            const int n = 4;
            var b = new int[n + 1];
            var t = new int[n + 1];
            for (int i = 0; i <= n; ++i) {
                b[i] = m.AppendVertex(new Vector3d(i, 0, 0));
                t[i] = m.AppendVertex(new Vector3d(i, (i == 2) ? 1e-13 : 1, 0));
            }
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(b[i], b[i + 1], t[i + 1]);
                m.AppendTriangle(b[i], t[i + 1], t[i]);
            }
            Assert.True(Degenerate(m) > 0);
            DMesh3 before = new DMesh3(m);
            var repair = new MeshRoundingRepair(m);
            Assert.True(repair.Apply());
            Assert.Equal(0, Degenerate(m));
            Assert.Equal(0, repair.Collapses);
            Assert.Equal(1, AssertBoundaryKept(before, m));
            Assert.Equal(before.VertexCount, m.VertexCount);
        }

        // ---- the repair after MeshRobustRepair.SeparateTouchingVertices ----

        // a box [-1, 1]^2 x [-2, 0] whose top is a dent down to (0, 0, -0.5), and a pyramid pointing down into it
        // with its tip on the bottom of the dent (base of half width 0.3 at height height above the tip), in one mesh
        static DMesh3 PyramidInADent(double height)
        {
            var m = new DMesh3();
            int[] top = { m.AppendVertex(new Vector3d(-1, -1, 0)), m.AppendVertex(new Vector3d(1, -1, 0)), m.AppendVertex(new Vector3d(1, 1, 0)), m.AppendVertex(new Vector3d(-1, 1, 0)) };
            int[] bottom = { m.AppendVertex(new Vector3d(-1, -1, -2)), m.AppendVertex(new Vector3d(1, -1, -2)), m.AppendVertex(new Vector3d(1, 1, -2)), m.AppendVertex(new Vector3d(-1, 1, -2)) };
            Vector3d dent = new Vector3d(0, 0, -0.5);
            int p = m.AppendVertex(dent);
            for (int i = 0; i < 4; ++i) {
                int j = (i + 1) % 4;
                m.AppendTriangle(p, top[i], top[j]);
                m.AppendTriangle(top[i], bottom[i], bottom[j]);
                m.AppendTriangle(top[i], bottom[j], top[j]);
            }
            m.AppendTriangle(bottom[0], bottom[2], bottom[1]);
            m.AppendTriangle(bottom[0], bottom[3], bottom[2]);
            int tip = m.AppendVertex(dent);
            int[] b = new int[4];
            for (int i = 0; i < 4; ++i)
                b[i] = m.AppendVertex(dent + new Vector3d((i == 0 || i == 3) ? -0.3 : 0.3, (i < 2) ? -0.3 : 0.3, height));
            for (int i = 0; i < 4; ++i)
                m.AppendTriangle(tip, b[(i + 1) % 4], b[i]);
            m.AppendTriangle(b[0], b[1], b[2]);
            m.AppendTriangle(b[0], b[2], b[3]);
            return m;
        }

        [Fact]
        public void RobustRepair_SeparationMadeIntersections_RepairedAgain()
        {
            // the solids touch at the bottom of the dent; the resolve keeps two vertices there, which
            // SeparateTouchingVertices moves by 1e-4 of the way to the centroid of their neighbours: the box's up by
            // 5e-5, the pyramid's up by 4.9e-5, so the pyramid's tip ends up below the dent's surface. The repair
            // after the separation moves them apart; without it the result would have self-intersections (the
            // rounding of the resolve had nothing to do, so its count, 0, would be taken)
            DMesh3 m = PyramidInADent(0.49);
            Assert.True(m.IsClosed());
            DMesh3 resolved = ExactMeshBoolean.Resolve(m, true);
            Assert.Equal(2, MeshRobustRepair.SeparateTouchingVertices(resolved, 1e-4));
            var separated = new MeshRoundingRepair(resolved) { MaxPasses = 0 };
            Assert.False(separated.Apply());
            Assert.True(separated.InitialIntersections > 0);

            var robust = new MeshRobustRepair(new DMesh3(m)) { AlphaWrapFallback = false };
            Assert.True(robust.Apply());
            Assert.True(robust.UsedExactResolve, $"exact: {robust.ExactResolveReason}");
            Assert.Equal(1, robust.ExactResolveRoundsUsed);
            Assert.Single(robust.ExactRoundingEdits);
            Assert.Equal(0, robust.ExactRoundingEdits[0].a);
            Assert.True(robust.ExactRoundingEdits[0].b > 0);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(robust.Mesh, true));
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(robust.Mesh));
            // (the separation sweeps a volume of about 6e-5)
            Assert.Equal(Volume(m), Volume(robust.Mesh), 3);
        }

        // a strip of 12 quads (randomly split into triangles), its top boundary dipping to 1e-13 .. 1e-10 above or
        // below the bottom one at random vertices, with edges split 1e-9 .. 1e-14 from an end and a few vertices
        // moved by up to 5e-7 across
        static DMesh3 PinchedStrip(int seed)
        {
            var r = new Random(seed);
            var m = new DMesh3();
            const int n = 12;
            var b = new int[n + 1];
            var t = new int[n + 1];
            for (int i = 0; i <= n; ++i) {
                double y = (r.NextDouble() < 0.4) ? Math.Pow(10, -13 + r.Next(4)) * ((r.Next(2) == 0) ? 1 : -0.3) : 1;
                b[i] = m.AppendVertex(new Vector3d(i + (r.NextDouble() - 0.5) * 0.3, 0, 0));
                t[i] = m.AppendVertex(new Vector3d(i + (r.NextDouble() - 0.5) * 0.6, y, (r.NextDouble() - 0.5) * 1e-9));
            }
            for (int i = 0; i < n; ++i) {
                if (r.Next(2) == 0) {
                    m.AppendTriangle(b[i], b[i + 1], t[i + 1]);
                    m.AppendTriangle(b[i], t[i + 1], t[i]);
                } else {
                    m.AppendTriangle(b[i], b[i + 1], t[i]);
                    m.AppendTriangle(b[i + 1], t[i + 1], t[i]);
                }
            }
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (m.IsEdge(eid) == false || r.NextDouble() > 0.35)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                int a = (r.Next(2) == 0) ? ev.a : ev.b, c = (a == ev.a) ? ev.b : ev.a;
                Vector3d pa = m.GetVertex(a), pc = m.GetVertex(c);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(eid, out info) != MeshResult.Ok)
                    continue;
                Vector3d p = pa + Math.Pow(10, -9 - r.Next(6)) * (pc - pa).Normalized;
                if (r.Next(3) == 0)
                    p += new Vector3d(r.NextDouble() - 0.5, r.NextDouble() - 0.5, r.NextDouble() - 0.5) * 1e-12;
                m.SetVertex(info.vNew, p);
            }
            foreach (int v in m.VertexIndices().ToArray())
                if (r.NextDouble() < 0.05)
                    m.SetVertex(v, m.GetVertex(v) + new Vector3d(0, 0, (r.NextDouble() - 0.5) * 1e-6));
            return m;
        }

        [Theory]
        [InlineData(14)]
        [InlineData(92)]
        [InlineData(215)]
        [InlineData(320)]
        public void Repair_PinchedStrips_BoundaryStaysOnTheInputCurve(int seed)
        {
            // many edits along the boundary: a vertex slid after its neighbour on the boundary was contracted away
            // (along the new, shorter edge) or slid back the other way would leave the input curve; it stays on it
            DMesh3 m = PinchedStrip(seed);
            DMesh3 before = new DMesh3(m);
            int intersections = MeshRepairMetrics.SelfIntersections(m), degenerate = Degenerate(m);
            var repair = new MeshRoundingRepair(m) { RemoveTinyComponents = false };
            repair.Apply();
            Assert.True(repair.Collapses > 5);
            AssertBoundaryKept(before, m);
            Assert.True(MeshRepairMetrics.SelfIntersections(m) <= intersections && Degenerate(m) <= degenerate);
            Assert.Equal(MeshRepairMetrics.SelfIntersections(m), repair.RemainingIntersections);
        }
    }
}
