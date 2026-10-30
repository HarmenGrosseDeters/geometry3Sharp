using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MeshBoundaryLoopsTests
    {
        static DMesh3 LoadTestFile(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "test_files", name);
            DMesh3 mesh = StandardMeshReader.ReadMesh(path);
            Assert.NotNull(mesh);
            return mesh;
        }

        // open box: box without its top face
        static DMesh3 OpenBox()
        {
            DMesh3 mesh = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            foreach (int tid in mesh.TriangleIndices().ToArray())
                if (mesh.GetTriCentroid(tid).y > 0.99)
                    mesh.RemoveTriangle(tid, true, false);
            return mesh;
        }

        // 6x6 grid with two cells removed that touch diagonally at one vertex (a bowtie on the boundary)
        static DMesh3 GridWithBowtieHoles()
        {
            DMesh3 mesh = new GriddedRectGenerator() { Width = 6, Height = 6, EdgeVertices = 7 }.Generate().MakeDMesh();
            foreach (int tid in mesh.TriangleIndices().ToArray()) {
                Vector3d c = mesh.GetTriCentroid(tid);
                bool cellA = c.x > -1 && c.x < 0 && c.z > -1 && c.z < 0;
                bool cellB = c.x > 0 && c.x < 1 && c.z > 0 && c.z < 1;
                if (cellA || cellB)
                    mesh.RemoveTriangle(tid, true, false);
            }
            return mesh;
        }

        // checks the invariants every boundary loop decomposition must have
        static void Validate(DMesh3 mesh, MeshBoundaryLoops loops, bool expectOriented)
        {
            Assert.Equal(0, loops.SpanCount);
            var boundaryEdges = new HashSet<int>(MeshIterators.BoundaryEdges(mesh));
            var seen = new HashSet<int>();
            foreach (EdgeLoop loop in loops) {
                int N = loop.Vertices.Length;
                Assert.True(N >= 3, $"loop with {N} vertices");
                Assert.Equal(N, loop.Edges.Length);
                Assert.Equal(N, loop.Vertices.Distinct().Count());     // simple loop
                for (int i = 0; i < N; ++i) {
                    int eid = loop.Edges[i];
                    Assert.Contains(eid, boundaryEdges);
                    Assert.True(seen.Add(eid), $"edge {eid} in more than one loop");
                    Index2i ev = mesh.GetEdgeV(eid);
                    int a = loop.Vertices[i], b = loop.Vertices[(i + 1) % N];
                    Assert.True((ev.a == a && ev.b == b) || (ev.a == b && ev.b == a), "edge does not connect consecutive loop vertices");
                    if (expectOriented) {
                        Index2i oriented = mesh.GetOrientedBoundaryEdgeV(eid);
                        Assert.True(oriented.a == a && oriented.b == b, "loop does not follow boundary orientation");
                    }
                }
            }
            Assert.Equal(boundaryEdges.Count, seen.Count);
        }


        [Fact]
        public void OpenBox_OneOrientedLoop()
        {
            DMesh3 mesh = OpenBox();
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(1, loops.Count);
            Assert.Equal(4, loops[0].VertexCount);
            Validate(mesh, loops, true);
        }

        [Fact]
        public void BowtieHoles_SeparateSimpleLoops_AndFillable()
        {
            DMesh3 mesh = GridWithBowtieHoles();
            Assert.Single(MeshIterators.BoundaryEdges(mesh).Select(e => mesh.GetEdgeV(e)).SelectMany(ev => new[] { ev.a, ev.b })
                .GroupBy(v => v).Where(g => g.Count() > 2));
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(3, loops.Count);       // outer border + two holes
            Validate(mesh, loops, true);

            foreach (EdgeLoop loop in loops.Where(l => l.VertexCount == 4))
                Assert.True(new SimpleHoleFiller(mesh, loop).Fill());
            Assert.Single(new MeshBoundaryLoops(mesh).Loops);   // only the outer border is left
            // the fills close the holes: no bowtie vertex left, and no fan triangle of zero area
            ResultChecks.AssertValid(mesh);
            Assert.True(mesh.TriangleIndices().All(t => mesh.GetTriArea(t) > 0));
        }

        [Fact]
        public void InconsistentOrientation_StillOneLoop()
        {
            DMesh3 mesh = OpenBox();
            // flip one triangle on the opening: its boundary edge now runs against the others
            int tid = MeshIterators.BoundaryEdges(mesh).Select(e => mesh.GetEdgeT(e).a).First();
            mesh.ReverseTriOrientation(tid);
            var loops = new MeshBoundaryLoops(mesh);
            Assert.Equal(1, loops.Count);
            Assert.Equal(4, loops[0].VertexCount);
            Validate(mesh, loops, false);
            Assert.True(loops.SawInconsistentOrientation);
            Assert.False(loops.IsConsistentlyOriented(loops[0]));
        }

        [Fact]
        public void ConsistentLoops_AreReportedAsOriented()
        {
            DMesh3 mesh = GridWithBowtieHoles();
            var loops = new MeshBoundaryLoops(mesh);
            Assert.False(loops.SawInconsistentOrientation);
            Assert.All(loops.Loops, l => Assert.True(loops.IsConsistentlyOriented(l)));
        }

        sealed class BoundaryFixture
        {
            public DMesh3 Mesh;
            public List<string> Partitions = new List<string>();
        }

        static string PointKey(Vector3d p) => string.Join(",", new[] { p.x, p.y, p.z }
            .Select(x => (x + 0.0).ToString("R", CultureInfo.InvariantCulture)));
        static string EdgeKey(Vector3d a, Vector3d b)
        {
            string x = PointKey(a), y = PointKey(b);
            return string.CompareOrdinal(x, y) <= 0 ? x + "/" + y : y + "/" + x;
        }
        static string CycleKey(IEnumerable<string> edges) => string.Join(";", edges.OrderBy(e => e, StringComparer.Ordinal));
        static string CycleKey(params Vector3d[] vertices) => CycleKey(Enumerable.Range(0, vertices.Length)
            .Select(i => EdgeKey(vertices[i], vertices[(i + 1) % vertices.Length])));
        static List<string> PartitionKeys(DMesh3 mesh, MeshBoundaryLoops loops) => loops.Loops
            .Select(l => CycleKey(l.Edges.Select(e => {
                Index2i v = mesh.GetEdgeV(e);
                return EdgeKey(mesh.GetVertex(v.a), mesh.GetVertex(v.b));
            }))).OrderBy(s => s, StringComparer.Ordinal).ToList();

        static BoundaryFixture Fans(int count)
        {
            var f = new BoundaryFixture { Mesh = new DMesh3() };
            int center = f.Mesh.AppendVertex(Vector3d.Zero);
            for (int i = 0; i < count; ++i) {
                double angle = 2 * Math.PI * i / count;
                Func<double, Vector3d> point = t => new Vector3d(2 * Math.Cos(t), 0, 2 * Math.Sin(t));
                Vector3d a = point(angle), b = point(angle + .3), c = point(angle + .6);
                int va = f.Mesh.AppendVertex(a), vb = f.Mesh.AppendVertex(b), vc = f.Mesh.AppendVertex(c);
                Assert.True(f.Mesh.AppendTriangle(center, va, vb) >= 0);
                Assert.True(f.Mesh.AppendTriangle(center, vb, vc) >= 0);
                f.Partitions.Add(CycleKey(Vector3d.Zero, a, b, c));
            }
            Assert.True(f.Mesh.IsBowtieVertex(center));
            f.Partitions.Sort(StringComparer.Ordinal);
            return f;
        }

        // 7x7 grid with three cells removed that touch diagonally: two bowties in a row
        static BoundaryFixture HoleChain()
        {
            const int n = 7;
            var f = new BoundaryFixture { Mesh = new DMesh3() };
            Func<int, int, Vector3d> p = (x, z) => new Vector3d(x, 0, z);
            var ids = new int[n + 1, n + 1];
            for (int z = 0; z <= n; ++z)
                for (int x = 0; x <= n; ++x) ids[x, z] = f.Mesh.AppendVertex(p(x, z));
            for (int z = 0; z < n; ++z)
                for (int x = 0; x < n; ++x) {
                    if (x == z && x >= 2 && x <= 4) continue;
                    Assert.True(f.Mesh.AppendTriangle(ids[x, z], ids[x + 1, z], ids[x + 1, z + 1]) >= 0);
                    Assert.True(f.Mesh.AppendTriangle(ids[x, z], ids[x + 1, z + 1], ids[x, z + 1]) >= 0);
                }
            for (int h = 2; h <= 4; ++h)
                f.Partitions.Add(CycleKey(p(h, h), p(h + 1, h), p(h + 1, h + 1), p(h, h + 1)));
            var outer = new List<Vector3d>();
            for (int x = 0; x < n; ++x) outer.Add(p(x, 0));
            for (int z = 0; z < n; ++z) outer.Add(p(n, z));
            for (int x = n; x > 0; --x) outer.Add(p(x, n));
            for (int z = n; z > 0; --z) outer.Add(p(0, z));
            f.Partitions.Add(CycleKey(outer.ToArray()));
            f.Partitions.Sort(StringComparer.Ordinal);
            Assert.Contains(f.Mesh.VertexIndices(), v => f.Mesh.IsBowtieVertex(v));
            return f;
        }

        // Two discs that share only the bowtie vertices A and B. These coordinates make the left-turn rule of
        // the filtered walk go x B y A z B w A, an interleaving without a simple sub-loop, which is the only way
        // to reach FellBackToSpansOnFailure and FailureBowties without seeding them.
        static void AppendInterleavedBowties(DMesh3 mesh)
        {
            Func<double, double, double, int> add = (x, y, z) => mesh.AppendVertex(new Vector3d(x, y, z));
            int A = add(2, -2, 2), B = add(-1, 2, 3), p = add(0, 2, 1), q = add(-3, 3, 2), r = add(3, -3, 0), s = add(0, -2, 3);
            int c1 = add(1, -1, 0), c2 = add(1, 1, 0);
            foreach (Index3i t in new[] { new Index3i(c1, p, B), new Index3i(c1, B, q), new Index3i(c1, q, A), new Index3i(c1, A, p),
                new Index3i(c2, r, B), new Index3i(c2, B, s), new Index3i(c2, s, A), new Index3i(c2, A, r) })
                Assert.True(mesh.AppendTriangle(t) >= 0);
        }

        static DMesh3 InterleavedBowties()
        {
            var mesh = new DMesh3();
            AppendInterleavedBowties(mesh);
            return mesh;
        }

        static void AssertSnapshot(DMesh3 before, DMesh3 after, int timestamp, int shapeTimestamp)
        {
            Assert.Equal("", Invariance.Difference(before, after));
            Assert.Equal(before.EdgeIndices().ToArray(), after.EdgeIndices().ToArray());
            foreach (int e in before.EdgeIndices()) {
                Assert.Equal(before.GetEdgeV(e), after.GetEdgeV(e));
                Assert.Equal(before.GetEdgeT(e), after.GetEdgeT(e));
            }
            Assert.Equal(timestamp, after.Timestamp);
            Assert.Equal(shapeTimestamp, after.ShapeTimestamp);
        }

        static void AssertSuccessfulCycles(DMesh3 mesh, MeshBoundaryLoops loops)
        {
            Validate(mesh, loops, true);
            Assert.False(loops.SawOpenSpans);
            Assert.False(loops.FellBackToSpansOnFailure);
            Assert.True(loops.FailureBowties == null || loops.FailureBowties.Count == 0);
            Assert.False(loops.SawInconsistentOrientation);
        }

        // Fans(9) has 18 boundary edges at one vertex, more than the walk's initial buffer of 16.
        [Theory]
        [InlineData(9, 19)]
        [InlineData(9, 43)]
        [InlineData(9, 101)]
        [InlineData(0, 19)]
        [InlineData(0, 43)]
        [InlineData(0, 101)]
        public void Bowties_HandBuiltPartition_SurvivesInsertionOrderAndRecompute(int fans, int seed)
        {
            BoundaryFixture f = fans > 0 ? Fans(fans) : HoleChain();
            DMesh3 mesh = Invariance.Permuted(f.Mesh, seed);
            var before = new DMesh3(mesh);
            int stamp = mesh.Timestamp, shape = mesh.ShapeTimestamp;
            var loops = new MeshBoundaryLoops(mesh);
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(f.Partitions, PartitionKeys(mesh, loops));
            AssertSnapshot(before, mesh, stamp, shape);
            Assert.True(loops.Compute());
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(f.Partitions, PartitionKeys(mesh, loops));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void BowtieAndChain_AllSimilaritiesPreserveDiscreteEdgePartition(string name)
        {
            Similarity s = Similarity.ByName(name);
            // Only the interleaved fixture depends on the turn rule; the others have one simple-loop partition.
            foreach (DMesh3 original in new[] { Fans(2).Mesh, HoleChain().Mesh, InterleavedBowties() }) {
                var baseline = new MeshBoundaryLoops(original);
                DMesh3 mesh = Invariance.Transformed(original, s);
                var loops = new MeshBoundaryLoops(mesh);
                AssertSuccessfulCycles(mesh, loops);
                Func<MeshBoundaryLoops, string[]> ids = x => x.Loops.Select(l => string.Join(",", l.Edges.OrderBy(e => e)))
                    .OrderBy(k => k, StringComparer.Ordinal).ToArray();
                Assert.Equal(ids(baseline), ids(loops));
            }
        }

        // A first Compute that really produces the diagnostic; asserts that it did, so the reset test cannot be vacuous.
        static MeshBoundaryLoops ComputedWithDiagnostic(string diagnostic)
        {
            MeshBoundaryLoops loops;
            switch (diagnostic) {
                case nameof(MeshBoundaryLoops.SawOpenSpans):
                    DMesh3 box = OpenBox();
                    int removed = MeshIterators.BoundaryEdges(box).First();
                    loops = new MeshBoundaryLoops(box, false) { EdgeFilterF = e => e != removed };
                    loops.Compute();
                    Assert.True(loops.SawOpenSpans);
                    break;
                case nameof(MeshBoundaryLoops.FellBackToSpansOnFailure):
                    loops = new MeshBoundaryLoops(InterleavedBowties(), false) { EdgeFilterF = e => true };
                    loops.Compute();
                    Assert.True(loops.FellBackToSpansOnFailure);
                    break;
                case nameof(MeshBoundaryLoops.FailureBowties):
                    loops = new MeshBoundaryLoops(InterleavedBowties(), false) {
                        EdgeFilterF = e => true, FailureBehavior = MeshBoundaryLoops.FailureBehaviors.ThrowException };
                    Assert.Throws<MeshBoundaryLoopsException>(() => loops.Compute());
                    Assert.NotEmpty(loops.FailureBowties);
                    break;
                case nameof(MeshBoundaryLoops.SawInconsistentOrientation):
                    DMesh3 flipped = OpenBox();
                    flipped.ReverseTriOrientation(MeshIterators.BoundaryEdges(flipped).Select(e => flipped.GetEdgeT(e).a).First());
                    loops = new MeshBoundaryLoops(flipped);
                    Assert.True(loops.SawInconsistentOrientation);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(diagnostic));
            }
            return loops;
        }

        public static IEnumerable<object[]> ReuseCases()
        {
            foreach (string diagnostic in new[] { nameof(MeshBoundaryLoops.SawOpenSpans), nameof(MeshBoundaryLoops.FellBackToSpansOnFailure),
                nameof(MeshBoundaryLoops.FailureBowties), nameof(MeshBoundaryLoops.SawInconsistentOrientation) })
                foreach (bool closed in new[] { false, true })
                    foreach (bool filtered in new[] { false, true })
                        yield return new object[] { diagnostic, closed, filtered };
        }

        [Theory]
        [MemberData(nameof(ReuseCases))]
        public void Reuse_RealDiagnosticFromFirstRun_IsClearedOnEveryPathOfTheNextRun(string diagnostic, bool closed, bool filtered)
        {
            MeshBoundaryLoops reused = ComputedWithDiagnostic(diagnostic);
            DMesh3 mesh = closed ? new TrivialBox3Generator().Generate().MakeDMesh() : OpenBox();
            Func<int, bool> filter = filtered ? (Func<int, bool>)(e => true) : null;
            reused.Mesh = mesh;
            reused.EdgeFilterF = filter;
            reused.FailureBehavior = MeshBoundaryLoops.FailureBehaviors.ConvertToOpenSpan;
            Assert.True(reused.Compute());
            AssertSuccessfulCycles(mesh, reused);
            Assert.Null(reused.FailureBowties);
            var fresh = new MeshBoundaryLoops(mesh, false) { EdgeFilterF = filter };
            Assert.True(fresh.Compute());
            Assert.Equal(closed ? 0 : 1, reused.Count);
            Assert.Equal(PartitionKeys(mesh, fresh), PartitionKeys(mesh, reused));
        }

        [Fact]
        public void Reuse_RunThatThrowsBeforeAnyFlag_DoesNotKeepTheFlagOfThePreviousRun()
        {
            MeshBoundaryLoops loops = ComputedWithDiagnostic(nameof(MeshBoundaryLoops.SawOpenSpans));
            loops.SpanBehavior = MeshBoundaryLoops.SpanBehaviors.ThrowException;
            Assert.Throws<MeshBoundaryLoopsException>(() => loops.Compute());
            Assert.False(loops.SawOpenSpans);
        }

        [Fact]
        public void Reuse_AfterExceptionMidWalk_NextRunIsClean()
        {
            // The filter opens the triangle first (SawOpenSpans), then the interleaved bowties throw (FailureBowties).
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(new Vector3d(20, 0, 0)), b = mesh.AppendVertex(new Vector3d(21, 0, 0)), c = mesh.AppendVertex(new Vector3d(20, 0, 1));
            Assert.True(mesh.AppendTriangle(a, b, c) >= 0);
            AppendInterleavedBowties(mesh);
            int filteredOut = mesh.FindEdge(a, b);
            var loops = new MeshBoundaryLoops(mesh, false) {
                EdgeFilterF = e => e != filteredOut, FailureBehavior = MeshBoundaryLoops.FailureBehaviors.ThrowException };
            Assert.Throws<MeshBoundaryLoopsException>(() => loops.Compute());
            Assert.True(loops.SawOpenSpans);
            Assert.NotEmpty(loops.FailureBowties);

            loops.EdgeFilterF = null;
            Assert.True(loops.Compute());
            AssertSuccessfulCycles(mesh, loops);
            Assert.Null(loops.FailureBowties);
            Assert.Equal(3, loops.Count);
            Assert.Equal(PartitionKeys(mesh, new MeshBoundaryLoops(mesh)), PartitionKeys(mesh, loops));
        }

        [Theory]
        [InlineData(MeshBoundaryLoops.SpanBehaviors.Compute)]
        [InlineData(MeshBoundaryLoops.SpanBehaviors.Ignore)]
        public void FilteredRun_SpanPolicy_ReportsTheSpanAndNextRunClearsTheFlag(MeshBoundaryLoops.SpanBehaviors policy)
        {
            DMesh3 mesh = OpenBox();
            int removed = MeshIterators.BoundaryEdges(mesh).OrderBy(e => e).First();
            var loops = new MeshBoundaryLoops(mesh, false) { SpanBehavior = policy, EdgeFilterF = e => e != removed };
            var before = new DMesh3(mesh);
            int stamp = mesh.Timestamp, shape = mesh.ShapeTimestamp;
            Assert.True(loops.Compute());
            Assert.Empty(loops.Loops);
            Assert.True(loops.SawOpenSpans);
            if (policy == MeshBoundaryLoops.SpanBehaviors.Compute) {
                Assert.Single(loops.Spans);
                int[] expected = MeshIterators.BoundaryEdges(mesh).Where(e => e != removed).OrderBy(e => e).ToArray();
                Assert.Equal(expected, loops.Spans.SelectMany(s => s.Edges).OrderBy(e => e).ToArray());
            } else Assert.Empty(loops.Spans);
            AssertSnapshot(before, mesh, stamp, shape);
            loops.EdgeFilterF = null;
            Assert.True(loops.Compute());
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(PartitionKeys(mesh, new MeshBoundaryLoops(mesh)), PartitionKeys(mesh, loops));
        }

        [Fact]
        public void EmptyMeshAndSingleTriangle_HaveContractBoundaryCounts()
        {
            var empty = new MeshBoundaryLoops(new DMesh3());
            Assert.Equal(0, empty.Count);
            Assert.Equal(0, empty.SpanCount);
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(Vector3d.Zero), mesh.AppendVertex(Vector3d.AxisX), mesh.AppendVertex(Vector3d.AxisY));
            var loops = new MeshBoundaryLoops(mesh);
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(3, loops[0].VertexCount);
        }

        [Theory]
        [InlineData("crazy_boundary.obj")]
        [InlineData("bunny_bowties.obj")]
        [InlineData("crazyhole.obj")]
        public void UpstreamProblemMeshes_DecomposeIntoSimpleLoops(string file)
        {
            DMesh3 mesh = LoadTestFile(file);
            var loops = new MeshBoundaryLoops(mesh);
            Validate(mesh, loops, false);
        }
    }
}
