using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.PreventSelfIntersections (opt-in): collapses, flips, the curve projection of split vertices and the
    /// buffered smoothing and projection moves are rejected if they make triangles intersect (exactly, as
    /// MeshRepairMetrics.SelfIntersections counts). With the g3cli remesh settings the remesher otherwise leaves
    /// self-intersecting pairs on primitives (at the rim where a flat cap meets the side), gears and Thingi10K meshes
    /// (benchmarks/RemeshQualityBenchmark).
    /// </summary>
    public class RemesherSelfIntersectionTests
    {
        static double MeanEdge(DMesh3 m)
        {
            return m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });
        }

        static DMesh3 Cone(int slices) =>
            new ConeGenerator() { BaseRadius = 1f, Height = 2f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
        static DMesh3 Taper(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 1f, TopRadius = 0.3f, Height = 1f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();

        // an extruded gear with fan caps and collinear points on the flanks (as in RemesherQualityTests)
        static DMesh3 Gear(int teeth, double height, int pointsPerSide)
        {
            var poly = new List<Vector2d>();
            for (int k = 0; k < teeth; ++k) {
                double a0 = 2 * Math.PI * k / teeth, da = 2 * Math.PI / teeth;
                foreach (var (f, r) in new[] { (0.0, 0.8), (0.2, 1.0), (0.5, 1.0), (0.7, 0.8) })
                    poly.Add(r * new Vector2d(Math.Cos(a0 + f * da), Math.Sin(a0 + f * da)));
            }
            var outline = new List<Vector2d>();
            for (int i = 0; i < poly.Count; ++i)
                for (int k = 0; k < pointsPerSide; ++k)
                    outline.Add(Vector2d.Lerp(poly[i], poly[(i + 1) % poly.Count], (double)k / pointsPerSide));
            var m = new DMesh3();
            int n = outline.Count;
            int cb = m.AppendVertex(new Vector3d(0, 0, 0)), ct = m.AppendVertex(new Vector3d(0, 0, height));
            int[] b = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
            int[] t = outline.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, height))).ToArray();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(cb, b[j], b[i]); m.AppendTriangle(ct, t[i], t[j]);
                m.AppendTriangle(b[i], b[j], t[j]); m.AppendTriangle(b[i], t[j], t[i]);
            }
            return m;
        }

        // as g3cli remesh (20 passes, smoothing 0.5, projection onto the input, 30 degree creases)
        static DMesh3 Remesh(DMesh3 input, double edge, double minQuality, bool prevent, bool parallel = true, bool pro = false, Remesher[] used = null)
        {
            DMesh3 mesh = new DMesh3(input);
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            r.PreventSelfIntersections = prevent;
            r.EnableParallelSmooth = r.EnableParallelProjection = parallel;
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(edge);
            for (int k = 0; k < 20; ++k) {
                if (pro) ((RemesherPro)r).RemeshIteration();
                else r.BasicRemeshPass();
            }
            if (used != null) used[0] = r;
            return mesh;
        }

        [Fact]
        public void Default_IsOff()
        {
            Assert.False(new Remesher(new DMesh3()).PreventSelfIntersections);
        }

        // cases of benchmarks/RemeshQualityBenchmark with self-intersecting pairs in the result: name, input, target / mean edge, quality
        static IEnumerable<(string, DMesh3, double, double)> Cases()
        {
            yield return ("taper16_x0.1 q0.2", Taper(16), 0.1, 0.2);
            yield return ("taper10_x0.15 q0", Taper(10), 0.15, 0);
            yield return ("cone24_x0.6 q0.2", Cone(24), 0.6, 0.2);
            yield return ("cone12_x0.15 q0", Cone(12), 0.15, 0);
            yield return ("gear24_3_x1 q0", Gear(24, 0.2, 3), 1.0, 0);
            yield return ("gear40_1_x1 q0", Gear(40, 0.25, 1), 1.0, 0);
        }

        [Fact]
        public void Primitives_NoSelfIntersections()
        {
            int pairsWithout = 0;
            foreach (var (name, input, f, q) in Cases()) {
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
                double edge = f * MeanEdge(input);
                var used = new Remesher[1];
                DMesh3 mesh = Remesh(input, edge, q, true, used: used);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
                Assert.True(mesh.IsClosed(), name);
                Assert.Empty(MeshIterators.BowtieVertices(mesh));
                Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
                Assert.Empty(MeshPostRepairCleanup.FindInvertedComponents(mesh));
                Assert.True(mesh.Vertices().All(v => v.IsFinite), name);
                if (q > 0)      // with the quality guard no degenerate triangles either: clean
                    Assert.Equal("", MeshRobustRepair.NotCleanReasons(mesh, true));
                Assert.True(used[0].SelfIntersectionsRejected > 0, name);
                // still a remesh of the input: refined or coarsened towards the target, near the input
                Assert.True(mesh.TriangleCount != input.TriangleCount, name);
                var hd = new MeshHausdorffDistance(input, mesh) { Tolerance = 0.01 * edge };
                Assert.True(hd.Compute());
                Assert.True(hd.UpperBound < (name.StartsWith("cone") ? 3.5 : 0.5) * edge, $"{name}: {hd.UpperBound / edge} target from the input");
                pairsWithout += MeshRepairMetrics.SelfIntersections(Remesh(input, edge, q, false));
            }
            // without the option these results had self-intersecting pairs
            Assert.True(pairsWithout > 0, $"{pairsWithout} pairs without the option");
        }

        [Fact]
        public void RemesherPro_NoSelfIntersections()
        {
            foreach (var (name, input, f, q) in Cases().Take(4)) {
                DMesh3 mesh = Remesh(input, f * MeanEdge(input), q, true, pro: true);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
                Assert.True(mesh.IsClosed(), name);
            }
        }

        [Fact]
        public void MeshChangedBetweenPasses_GridRebuilt()
        {
            // the triangle grid is kept between the edge operations (and passes without smoothing); a change of the
            // mesh outside the remesher (here: the mesh moved far away) must not leave it stale: a stale grid finds no
            // neighbours, so the collapses and flips of the following passes would go unchecked
            DMesh3 input = Gear(24, 0.2, 3);
            foreach (bool prevent in new[] { true, false }) {
                DMesh3 mesh = new DMesh3(input);
                var r = new Remesher(mesh) { EnableSmoothing = false, PreventSelfIntersections = prevent };
                MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
                r.SetTargetEdgeLength(2 * MeanEdge(input));
                r.BasicRemeshPass();
                Vector3d shift = new Vector3d(1000, 0, 0);
                foreach (int vid in mesh.VertexIndices())
                    mesh.SetVertex(vid, mesh.GetVertex(vid) + shift);
                for (int k = 0; k < 19; ++k)
                    r.BasicRemeshPass();
                int pairs = MeshRepairMetrics.SelfIntersections(mesh);
                if (prevent) {
                    Assert.Equal(0, pairs);
                    Assert.True(mesh.IsClosed());
                } else
                    Assert.True(pairs > 0, "without the option these passes create self-intersections");
            }
        }

        // two triangles [a,b,c], [b,a,d] folded like a tent over the edge ab, and a wall triangle (in the plane x = 0)
        // standing between c and d: the flipped edge cd cuts through the wall, and so does the third triangle at a
        // when ab is collapsed to a point above it
        static DMesh3 TentAndWall(bool wall, out int a, out int b, out int c, out int d)
        {
            var m = new DMesh3();
            a = m.AppendVertex(new Vector3d(0, -1, 0)); b = m.AppendVertex(new Vector3d(0, 1, 0));
            c = m.AppendVertex(new Vector3d(-1, 0, 0.5)); d = m.AppendVertex(new Vector3d(1, 0, 0.5));
            m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, d);
            // a triangle at a, flat on z = 0 (below the wall): after collapsing ab to (0, 0, 1) it rises through the wall
            int x = m.AppendVertex(new Vector3d(-1, -2, 0)), y = m.AppendVertex(new Vector3d(1, -2, 0));
            m.AppendTriangle(a, x, y);
            if (wall) {
                int w0 = m.AppendVertex(new Vector3d(0, -0.2, 0.2)), w1 = m.AppendVertex(new Vector3d(0, 0.2, 0.2)), w2 = m.AppendVertex(new Vector3d(0, 0, 2));
                m.AppendTriangle(w0, w1, w2);
            }
            return m;
        }

        class Probe : Remesher
        {
            public Probe(DMesh3 m) : base(m) { PreventSelfIntersections = true; }
            public bool Flip(int a, int b, int c, int d)
            {
                ensure_triangle_grid();
                Index2i et = Mesh.GetEdgeT(Mesh.FindEdge(a, b));
                return flip_creates_intersection(a, b, c, d, et.a, et.b);
            }
            public bool Collapse(int a, int b, Vector3d p)
            {
                ensure_triangle_grid();
                return collapse_creates_intersection(a, b, b, p);
            }
            public bool Split(int a, int b, int c, int d)
            {
                ensure_triangle_grid();
                Index2i et = Mesh.GetEdgeT(Mesh.FindEdge(a, b));
                return split_creates_intersection(a, b, c, d, et.a, et.b);
            }
            // one buffered move of vertex vid (as a smoothing pass would make it); true if it was applied
            public bool Move(int vid, Vector3d p, bool parallel)
            {
                Vector3d before = Mesh.GetVertex(vid);
                InitializeVertexBufferForPass();
                vModifiedV[vid] = true;
                vBufferV[vid] = p;
                ApplyVertexBuffer(parallel);
                return Mesh.GetVertex(vid) != before;
            }
            public string Process(int eid) => ProcessEdge(eid).ToString();
        }

        [Fact]
        public void FlipAndCollapseThroughAWall_Detected()
        {
            foreach (bool wall in new[] { false, true }) {
                DMesh3 m = TentAndWall(wall, out int a, out int b, out int c, out int d);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
                var probe = new Probe(m);
                Assert.Equal(wall, probe.Flip(a, b, c, d));
                // the edge ab collapsed to a point above it: [p,x,y] crosses the wall
                Assert.Equal(wall, probe.Collapse(a, b, new Vector3d(0, 0, 1)));
                // collapsed to its midpoint: [p,x,y] stays on z = 0, below the wall
                Assert.False(probe.Collapse(a, b, new Vector3d(0, 0, 0)));
            }
        }

        [Fact]
        public void PairTest_SameAsExactTriangleIntersection()
        {
            // the shortcut of the pair test (neighbours sharing a vertex, the other corners of one on one side of the
            // other's plane) against ExactTriangleIntersection.IntersectsBeyondShared on lattice pairs that share a
            // vertex or an edge: many collinear and coplanar configurations, in the axis plane z = 0 and tilted
            var rnd = new Random(7);
            int coplanar = 0, hits = 0;
            for (int n = 0; n < 200000; ++n) {
                bool flat = n % 2 == 0;
                Vector3d P() => new Vector3d(rnd.Next(-3, 4), rnd.Next(-3, 4), flat ? 0 : rnd.Next(-1, 2));
                Vector3d s = P(), p = P(), q = P(), u = P(), w = P();
                bool edge = n % 3 == 0;
                var A = new Triangle3d(s, p, q);
                var B = edge ? new Triangle3d(p, s, u) : new Triangle3d(s, u, w);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B))
                    continue;
                // distinct points get distinct ids, so the ids say which corners are shared
                var ids = new Dictionary<Vector3d, int>();
                int id(Vector3d x) { if (!ids.TryGetValue(x, out int i)) { i = ids.Count; ids[x] = i; } return i; }
                var ia = new Index3i(id(A.V0), id(A.V1), id(A.V2));
                var ib = new Index3i(id(B.V0), id(B.V1), id(B.V2));
                bool expected = ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ib, ref B);
                Assert.True(expected == Remesher.pair_intersects(ia, ref A, ib, ref B), $"{A.V0} {A.V1} {A.V2} / {B.V0} {B.V1} {B.V2}");
                Assert.True(expected == Remesher.pair_intersects(ib, ref B, ia, ref A), $"swapped: {A.V0} {A.V1} {A.V2} / {B.V0} {B.V1} {B.V2}");
                if (flat) coplanar++;
                if (expected) hits++;
            }
            Assert.True(coplanar > 10000 && hits > 1000, $"{coplanar} coplanar pairs, {hits} intersecting");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MoveThroughAWall_Reverted(bool parallel)
        {
            // c moved into the plane of the wall (x = 0), onto it: [a,b,c] then overlaps the wall, which shares no
            // vertex with it (a moved triangle against one that did not move)
            foreach (bool wall in new[] { false, true }) {
                DMesh3 m = TentAndWall(wall, out int a, out int b, out int c, out int d);
                var probe = new Probe(m);
                bool moved = probe.Move(c, new Vector3d(0, 0, 1), parallel);
                Assert.Equal(!wall, moved);
                Assert.Equal(wall ? 1 : 0, probe.SelfIntersectionsRejected);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            }
        }

        [Fact]
        public void SplitOfAnEdgeThroughAWall_Detected()
        {
            // a wall that pierces [a,b,c] (an intersection the mesh already has): the halves after a split of ab
            // intersect it too, so the split is not made
            foreach (bool wall in new[] { false, true }) {
                DMesh3 m = TentAndWall(false, out int a, out int b, out int c, out int d);
                if (wall) {
                    int w0 = m.AppendVertex(new Vector3d(-0.4, -0.1, -1)), w1 = m.AppendVertex(new Vector3d(-0.4, 0.1, -1)), w2 = m.AppendVertex(new Vector3d(-0.4, 0, 2));
                    m.AppendTriangle(w0, w1, w2);
                }
                Assert.Equal(wall, MeshRepairMetrics.SelfIntersections(m) > 0);
                Assert.Equal(wall, new Probe(m).Split(a, b, c, d));
            }
        }

        // projects every point to the same one: a "curve" that pulls the vertex of a split onto it
        class PointTarget : IProjectionTarget
        {
            public Vector3d P;
            public Vector3d Project(Vector3d v, int id = -1) => P;
        }

        [Fact]
        public void SplitVertexProjectedThroughAWall_StaysAtTheMidpoint()
        {
            // edge ab constrained to a target that pulls the new vertex of a split to (0, 0, 1): in the plane of the
            // wall and on it, so with the wall it stays at the midpoint of ab
            foreach (bool wall in new[] { false, true }) {
                DMesh3 m = TentAndWall(wall, out int a, out int b, out int c, out int d);
                var target = new PointTarget() { P = new Vector3d(0, 0, 1) };
                var probe = new Probe(m) { EnableFlips = false, EnableCollapses = false };
                var cons = new MeshConstraints();
                int eid = m.FindEdge(a, b);
                cons.SetOrUpdateEdgeConstraint(eid, new EdgeConstraint(EdgeRefineFlags.NoFlip, target));
                cons.SetOrUpdateVertexConstraint(a, new VertexConstraint(target));
                cons.SetOrUpdateVertexConstraint(b, new VertexConstraint(target));
                probe.SetExternalConstraints(cons);
                probe.SetTargetEdgeLength(0.5);
                int vNew = m.MaxVertexID;
                Assert.Equal("Ok_Split", probe.Process(eid));
                Assert.Equal(wall ? Vector3d.Zero : target.P, m.GetVertex(vNew));
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            }
        }

        // counts the triangles that a buffered pass flips (not flat before, normal turned by 90 degrees or more)
        class FlipCounting : Remesher
        {
            public FlipCounting(DMesh3 m) : base(m) { }
            public int Flipped;
            protected override void ApplyVertexBuffer(bool bParallel)
            {
                var before = new DMesh3(Mesh);
                base.ApplyVertexBuffer(bParallel);
                foreach (int tid in Mesh.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    before.GetTriVertices(tid, ref a, ref b, ref c);
                    if (MeshRefinerBase.TriangleQuality(ref a, ref b, ref c) < 1e-6) continue;
                    Vector3d n0 = (b - a).Cross(c - a);
                    Mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    if (!((b - a).Cross(c - a).Dot(n0) > 0)) Flipped++;
                }
            }
        }

        [Fact]
        public void WithMinTriangleQuality_RevertedMovesFlipNoTriangle()
        {
            // MinTriangleQuality checks the buffered moves together; the moves the intersection check reverts change
            // that set, so the mix is checked again (review of #240: cone16_x0.1 and cone32_x0.1 had a flipped triangle)
            int flipped = 0;
            foreach (int slices in new[] { 16, 32 }) {
                DMesh3 input = Cone(slices);
                DMesh3 mesh = new DMesh3(input);
                var r = new FlipCounting(mesh) { SmoothSpeedT = 0.5, MinTriangleQuality = 0.2, PreventSelfIntersections = true };
                r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
                MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
                r.SetTargetEdgeLength(0.1 * MeanEdge(input));
                for (int k = 0; k < 20; ++k) r.BasicRemeshPass();
                flipped += r.Flipped;
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));
            }
            Assert.Equal(0, flipped);
        }

        [Fact]
        public void MeshChangedOutsideTheRemesher_GridRebuilt()
        {
            // the grid is built with the wall far away; the wall is then moved between c and d from outside the
            // remesher: a stale grid would still have it far away and miss the flip through it
            DMesh3 m = TentAndWall(true, out int a, out int b, out int c, out int d);
            var wall = new[] { m.MaxVertexID - 3, m.MaxVertexID - 2, m.MaxVertexID - 1 };
            Vector3d away = new Vector3d(0, 0, 100);
            foreach (int v in wall) m.SetVertex(v, m.GetVertex(v) + away);
            var probe = new Probe(m);
            Assert.False(probe.Flip(a, b, c, d));
            foreach (int v in wall) m.SetVertex(v, m.GetVertex(v) - away);
            Assert.True(probe.Flip(a, b, c, d));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Taper(16);
            double edge = 0.1 * MeanEdge(input);
            DMesh3 m0 = Remesh(input, edge, 0.2, true, false);
            DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(input, s), s.ApplyLength(edge), 0.2, true, false), s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            Assert.True(Math.Abs(back.TriangleCount - m0.TriangleCount) <= 0.03 * m0.TriangleCount, $"{s.Name}: {back.TriangleCount} vs {m0.TriangleCount} triangles");
            Assert.True(Invariance.VertexDistance(m0, back) < 0.25 * edge, s.Name);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(back));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ParallelEqualsSerial_RepeatedRuns(bool pro)
        {
            // the moves are checked after the buffered pass, serially in triangle id order
            foreach (var (name, input, f, q) in Cases().Take(3)) {
                double edge = f * MeanEdge(input);
                DMesh3 serial = Remesh(input, edge, q, true, false, pro);
                for (int k = 0; k < 2; ++k)
                    Assert.Equal("", Invariance.Difference(serial, Remesh(input, edge, q, true, true, pro)));
            }
        }
    }
}
