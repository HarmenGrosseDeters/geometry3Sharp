using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue #1578: with PreventNormalFlips an unconverged remesh (a box of 12 triangles refined to 1 % of its diagonal, 10 passes)
    /// held 807 triangles that face against the input surface (944 at pass 11) and self-intersections. Where they came from, measured
    /// pass by pass on a 4 % target (max 11 per pass): collapses that left a flat triangle (its normal is rounding noise: it passed the
    /// flip test, which compares a triangle with itself), and smoothing and projection moves, which the option did not check at all
    /// (a triangle in a flat face turned inside out by the tangential smoothing). Now PreventNormalFlips also refuses a collapse that
    /// creates a flat triangle and, as MinTriangleQuality did, the smoothing and projection moves that turn a triangle (reverted per
    /// triangle, in the buffered passes and in RemesherPro's tracked pass; a shorter projection step). Inverted = a triangle (not
    /// flat: quality above 1e-6) whose normal points against the normal of the nearest input triangle at its centroid.
    /// Each test runs the pass loop of the guide on the box of the issue and checks every pass, not only the last.
    /// </summary>
    public class RemesherPreventNormalFlipsTests
    {
        readonly ITestOutputHelper output;
        public RemesherPreventNormalFlipsTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Box(Similarity s)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
            return Invariance.Transformed(box, s);
        }

        public enum Stage { Full, EdgeOpsOnly, NoProjection, NoSmoothing, InlineProjection }

        static Remesher Setup(DMesh3 mesh, DMesh3 input, double fraction, Stage stage, bool pro = false)
        {
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SetTargetEdgeLength(fraction * input.GetBounds().DiagonalLength);
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 40);
            MeshConstraintUtil.PinSharpVertices(r, 75);
            r.PreventNormalFlips = true;
            if (stage == Stage.EdgeOpsOnly || stage == Stage.NoSmoothing) r.EnableSmoothing = false;
            if (stage == Stage.EdgeOpsOnly || stage == Stage.NoProjection) r.ProjectionMode = Remesher.TargetProjectionMode.NoProjection;
            if (stage == Stage.InlineProjection) r.ProjectionMode = Remesher.TargetProjectionMode.Inline;
            return r;
        }

        static double Quality(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double e = (a - b).LengthSquared + (b - c).LengthSquared + (c - a).LengthSquared;
            return 4 * Math.Sqrt(3) * m.GetTriArea(t) / e;
        }

        static int Inverted(DMesh3 mesh, DMesh3 input, out int flat)
        {
            var tree = new DMeshAABBTree3(input, true);
            int n = 0; flat = 0;
            foreach (int t in mesh.TriangleIndices()) {
                if (Quality(mesh, t) < 1e-6) { flat++; continue; }
                int nearest = tree.FindNearestTriangle(mesh.GetTriCentroid(t));
                if (mesh.GetTriNormal(t).Dot(input.GetTriNormal(nearest)) < 0)
                    n++;
            }
            return n;
        }

        // the passes of the guide's loop; the inverted and flat triangles after each
        List<(int tris, int inverted, int flat)> Run(DMesh3 mesh, DMesh3 input, Remesher r, int passes, Action<int> each = null)
        {
            var rec = new List<(int, int, int)>();
            for (int i = 0; i < passes; ++i) {
                if (r is RemesherPro pro) pro.RemeshIteration(); else r.BasicRemeshPass();
                int inv = Inverted(mesh, input, out int flat);
                rec.Add((mesh.TriangleCount, inv, flat));
                each?.Invoke(i);
            }
            output.WriteLine(string.Join(" ", rec.Select((x, i) => i + ":" + x.Item2 + "/" + x.Item3)));
            return rec;
        }

        // 4 % target: the passes 3 to 7 held up to 11 inverted triangles (and self-intersections) before; 14 passes reach the target
        [Theory]
        [InlineData(Stage.Full)]
        [InlineData(Stage.EdgeOpsOnly)]       // flat triangles from collapses (1 to 3 inverted at passes 6 to 8)
        [InlineData(Stage.NoProjection)]      // the smoothing moves (the same counts as Full: the box is flat)
        [InlineData(Stage.NoSmoothing)]       // the projection moves
        [InlineData(Stage.InlineProjection)]  // the inline projection of smoothed and collapsed vertices (up to 8)
        public void NoPass_HoldsAnInvertedTriangle(Stage stage)
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.04, stage);
            var rec = Run(mesh, input, r, 14);
            Assert.All(rec, p => Assert.Equal(0, p.inverted));
            Assert.Equal(0, rec.Sum(p => p.flat));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
            Assert.Equal(0, r.InvertingPassesRejected);
        }

        // the case of the issue: 1 % of the diagonal, 10 passes (807 inverted triangles of 103946 and self-intersections before)
        // PreventSelfIntersections moves the vertices in rounds with its own revert logic, a different code path that runs the flip
        // checks of the moves too (this 4 % box passes on master as well: it pins that the path stays free; the failing case is the 1 % one above)
        [Fact]
        public void NoPass_HoldsAnInvertedTriangle_WithPreventSelfIntersections()
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.04, Stage.Full);
            r.PreventSelfIntersections = true;
            var rec = Run(mesh, input, r, 14);
            Assert.All(rec, p => Assert.Equal(0, p.inverted));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
        }

        [Fact]
        public void TheIssueCase_OnePercentTenPasses_HasNoInvertedTriangle()
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.01, Stage.Full);
            for (int i = 0; i < 10; ++i) r.BasicRemeshPass();
            int inv = Inverted(mesh, input, out int flat);
            output.WriteLine($"{mesh.TriangleCount} triangles, inverted {inv}, flat {flat}, '{MeshRobustRepair.NotCleanReason(mesh)}'");
            Assert.Equal(0, inv);
            Assert.True(Invariance.AllFinite(mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NoPass_HoldsAnInvertedTriangle_UnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Box(s);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.04, Stage.Full);
            var rec = Run(mesh, input, r, 14);
            Assert.All(rec, p => Assert.Equal(0, p.inverted));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
            Assert.True(Invariance.AllFinite(mesh));
        }

        // scaling by 2^k is exact, so every decision of the guards (signs of cross products, comparisons) and every position scales with it: the mesh mapped back is the same bit for bit
        [Fact]
        public void Result_IsTheSameBitForBit_AtScales2ToTheK()
        {
            DMesh3 reference = null;
            foreach (Similarity s in new[] { Similarity.Identity, Similarity.Pow2Small, Similarity.Pow2Large }) {
                DMesh3 input = Box(s);
                DMesh3 mesh = new DMesh3(input);
                Remesher r = Setup(mesh, input, 0.04, Stage.Full);
                for (int i = 0; i < 10; ++i) r.BasicRemeshPass();
                DMesh3 back = Invariance.MappedBack(mesh, s);
                if (reference == null) { reference = back; continue; }
                Assert.Equal("", Invariance.Difference(reference, back));
            }
        }

        // the buffered smoothing and projection passes revert moves per triangle, in triangle order: the parallel passes give the serial result
        [Fact]
        public void ParallelPasses_GiveTheSerialResult_AndRepeatedRunsTheSame()
        {
            DMesh3 serial = null;
            foreach (bool parallel in new[] { false, true, true, false }) {
                DMesh3 input = Box(Similarity.Identity);
                DMesh3 mesh = new DMesh3(input);
                Remesher r = Setup(mesh, input, 0.04, Stage.Full);
                r.EnableParallelSmooth = parallel; r.EnableParallelProjection = parallel;
                for (int i = 0; i < 12; ++i) r.BasicRemeshPass();
                Assert.Equal(0, Inverted(mesh, input, out _));
                if (serial == null) { serial = mesh; continue; }
                Assert.Equal("", Invariance.Difference(serial, mesh));
            }
        }

        // more passes on a converged remesher: stays free of inverted triangles
        [Fact]
        public void MorePassesOnTheSameInstance_StayFree()
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.04, Stage.Full);
            Run(mesh, input, r, 12);
            var more = Run(mesh, input, r, 6);
            Assert.All(more, p => Assert.Equal(0, p.inverted));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
        }

        // RemesherPro: its queued iteration and tracked projection pass use the same guards
        [Fact]
        public void RemesherPro_HoldsNoInvertedTriangle_AtAnyIteration()
        {
            DMesh3 input = Box(Similarity.Identity);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Setup(mesh, input, 0.04, Stage.Full, pro: true);
            var rec = Run(mesh, input, r, 14);
            Assert.All(rec, p => Assert.Equal(0, p.inverted));
            Assert.True(Invariance.AllFinite(mesh));
        }

        // ---- one move at a time: a fan of six triangles around vertex 0, which a smoothing or projection move would turn inside out

        // the centre at (0, 0, 0), the ring at radius 1 (vertices 1 to 6), all triangles facing +z
        static DMesh3 Fan()
        {
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero);
            for (int k = 0; k < 6; ++k) m.AppendVertex(new Vector3d(Math.Cos(k * Math.PI / 3), Math.Sin(k * Math.PI / 3), 0));
            for (int k = 0; k < 6; ++k) m.AppendTriangle(0, 1 + k, 1 + (k + 1) % 6);
            return m;
        }

        static int FlippedTriangles(DMesh3 m) => m.TriangleIndices().Count(t => m.GetTriNormal(t).z < 0.5);

        sealed class MoveTarget : IProjectionTarget
        {
            public Vector3d To;
            public Vector3d Project(Vector3d p, int id = -1) => id == 0 ? To : p;
        }

        // only the centre may move; no edge operation
        static Remesher MoveOnly(DMesh3 mesh, bool pro = false)
        {
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.EnableCollapses = r.EnableFlips = r.EnableSplits = false;
            r.MinEdgeLength = 1e-6; r.MaxEdgeLength = 10;
            r.PreventNormalFlips = true;
            r.VertexControlF = id => id == 0 ? Remesher.VertexControl.AllowAll : (Remesher.VertexControl.NoSmooth | Remesher.VertexControl.NoProject);
            return r;
        }

        [Theory]
        [InlineData(false)]       // buffered: reverted with the other moves of the pass (revert_flipping_moves)
        [InlineData(true)]        // in place: the vertex is checked as it moves (ComputeSmoothedVertexPos)
        public void SmoothingMove_ThatTurnsTrianglesInsideOut_IsRefused(bool inPlace)
        {
            DMesh3 mesh = Fan();
            Remesher r = MoveOnly(mesh);
            r.EnableSmoothInPlace = inPlace; r.EnableParallelSmooth = false;
            r.SmoothSpeedT = 1.0;
            r.CustomSmoothF = (m, vid, t) => vid == 0 ? new Vector3d(2.5, 0, 0) : m.GetVertex(vid);
            r.BasicRemeshPass();
            Assert.Equal(0, FlippedTriangles(mesh));
            Assert.Equal(Vector3d.Zero, mesh.GetVertex(0));
        }

        // a projection that would turn the fan inside out moves the vertex a shorter way: the largest of the half, quarter and eighth of the step that flips nothing
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]         // PreventSelfIntersections: the moves of the pass are applied in rounds with their own check
        [InlineData(false, true)]         // RemesherPro: the tracked projection pass
        public void ProjectionMove_ThatTurnsTrianglesInsideOut_TakesAShorterStep(bool preventIntersections, bool pro)
        {
            DMesh3 mesh = Fan();
            Remesher r = MoveOnly(mesh, pro);
            r.EnableSmoothing = false;
            r.PreventSelfIntersections = preventIntersections;
            r.SetProjectionTarget(new MoveTarget() { To = new Vector3d(2.5, 0, 0) });
            if (pro) ((RemesherPro)r).RemeshIteration(); else r.BasicRemeshPass();
            Assert.Equal(0, FlippedTriangles(mesh));
            // (the full step 2.5 and the half 1.25 pass the ring vertex at x = 1, where the triangles turn over; the quarter, 0.625, does not)
            Assert.True(mesh.GetVertex(0).EpsilonEqual(new Vector3d(0.625, 0, 0), 1e-12), mesh.GetVertex(0).ToString());
        }

        [Fact]
        public void ProjectionMove_ThatFlipsNothing_IsTaken()
        {
            DMesh3 mesh = Fan();
            Remesher r = MoveOnly(mesh);
            r.EnableSmoothing = false;
            r.SetProjectionTarget(new MoveTarget() { To = new Vector3d(0.25, 0.1, 0) });
            r.BasicRemeshPass();
            Assert.True(mesh.GetVertex(0).EpsilonEqual(new Vector3d(0.25, 0.1, 0), 1e-12));
            Assert.Equal(0, FlippedTriangles(mesh));
        }

        sealed class MovesTarget : IProjectionTarget
        {
            public Dictionary<int, Vector3d> To = new Dictionary<int, Vector3d>();
            public Vector3d Project(Vector3d p, int id = -1) => To.TryGetValue(id, out Vector3d q) ? q : p;
        }

        /// <summary>
        /// PreventSelfIntersections applies the moves of a pass in rounds and reverts a vertex whose new triangles hit another triangle.
        /// Reverts by an intersection leave no triangle turned over. Each move is checked against the old positions of its neighbours, and
        /// all together; once some vertices are reverted, flipped_against_before checks the mix around them.
        /// Random patches (a 5 x 5 grid in the plane z = 0): a vertex and most of its neighbours move along one vector, and a vertical
        /// triangle stands across the middle of an edge from a moved vertex to a neighbour (at the new positions), so that the moves of the
        /// triangles at that edge are reverted. No triangle of the patch may face down afterwards.
        /// Limit: the mix check fires in about 2 of 4000 trials, and no trial found (80000 tried) turns a triangle over without it, so
        /// this test does not kill the mutation that drops that check for PreventNormalFlips alone.
        /// </summary>
        [Fact]
        public void IntersectionReverts_LeaveNoTriangleTurnedOver()
        {
            var rnd = new Random(1578);
            int trials = 0, reverted = 0;
            for (int trial = 0; trial < 1500; ++trial) {
                var mesh = new DMesh3();
                int n = 5;
                for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) mesh.AppendVertex(new Vector3d(i, j, 0));
                for (int j = 0; j + 1 < n; ++j)
                    for (int i = 0; i + 1 < n; ++i) {
                        int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                        mesh.AppendTriangle(a, b, d); mesh.AppendTriangle(a, d, c);
                    }
                int patchTriangles = mesh.TriangleCount;
                var target = new MovesTarget();
                var moved = new HashSet<int>();
                // a cluster (a vertex and its neighbours) moves along one random vector, so that the triangles inside it keep their shape
                int centre = 1 + rnd.Next(n - 2) + n * (1 + rnd.Next(n - 2));
                moved.Add(centre);
                foreach (int nb in mesh.VtxVerticesItr(centre)) if (rnd.Next(4) > 0) moved.Add(nb);
                {
                    double a = rnd.NextDouble() * 2 * Math.PI, r = 0.3 + 1.7 * rnd.NextDouble();
                    Vector3d shift = new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0);
                    foreach (int v in moved) {
                        double b = rnd.NextDouble() * 2 * Math.PI, q = 0.25 * rnd.NextDouble();
                        target.To[v] = mesh.GetVertex(v) + shift + new Vector3d(q * Math.Cos(b), q * Math.Sin(b), 0);
                    }
                }
                // a vertical triangle across the middle of an edge from a moved vertex to a neighbour (at the new position of both)
                int hit = moved.ElementAt(rnd.Next(moved.Count));
                int[] nbrs = mesh.VtxVerticesItr(hit).ToArray();
                int w = nbrs[rnd.Next(nbrs.Length)];
                Vector3d pv = target.To[hit], pw = target.To.TryGetValue(w, out Vector3d tw) ? tw : mesh.GetVertex(w);
                Vector3d mid = 0.5 * (pv + pw), dir = (pw - pv).Normalized, side = new Vector3d(-dir.y, dir.x, 0);
                int o0 = mesh.AppendVertex(mid - 0.4 * side + new Vector3d(0, 0, -0.5)), o1 = mesh.AppendVertex(mid + 0.4 * side + new Vector3d(0, 0, -0.5)), o2 = mesh.AppendVertex(mid + new Vector3d(0, 0, 0.5));
                mesh.AppendTriangle(o0, o1, o2);
                Remesher r2 = new Remesher(mesh);
                r2.EnableCollapses = r2.EnableFlips = r2.EnableSplits = false; r2.EnableSmoothing = false;
                r2.MinEdgeLength = 1e-6; r2.MaxEdgeLength = 100;
                r2.PreventNormalFlips = true; r2.PreventSelfIntersections = true;
                r2.EnableParallelProjection = false;
                r2.VertexControlF = id => moved.Contains(id) ? Remesher.VertexControl.AllowAll : (Remesher.VertexControl.NoSmooth | Remesher.VertexControl.NoProject);
                r2.SetProjectionTarget(target);
                r2.BasicRemeshPass();
                if (r2.SelfIntersectionsRejected > 0) reverted++;
                int flipped = 0;
                for (int t = 0; t < patchTriangles; ++t) if (mesh.GetTriNormal(t).z < 0.5) flipped++;
                Assert.True(flipped == 0, "trial " + trial + ": " + flipped + " triangles turned over");
                trials++;
            }
            Assert.True(reverted > 100, "moves reverted by an intersection in only " + reverted + " of " + trials + " trials");
        }
    }
}
