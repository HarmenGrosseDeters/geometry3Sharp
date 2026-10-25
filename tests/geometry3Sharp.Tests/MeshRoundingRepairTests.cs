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
    }
}
