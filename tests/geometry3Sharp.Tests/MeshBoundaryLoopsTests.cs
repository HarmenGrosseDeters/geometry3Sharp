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

        static BoundaryFixture Fans(int count, bool tilted, bool aligned)
        {
            var f = new BoundaryFixture { Mesh = new DMesh3() };
            int center = f.Mesh.AppendVertex(Vector3d.Zero);
            for (int i = 0; i < count; ++i) {
                double angle = 2 * Math.PI * i / count;
                double width = aligned ? Math.Pow(2, -24) : .3;
                Func<double, Vector3d> point = t => new Vector3d(2 * Math.Cos(t),
                    tilted ? .25 * Math.Sin(3 * t) : 0, 2 * Math.Sin(t));
                Vector3d a = point(angle), b = point(angle + width), c = point(angle + 2 * width);
                int va = f.Mesh.AppendVertex(a), vb = f.Mesh.AppendVertex(b), vc = f.Mesh.AppendVertex(c);
                Assert.True(f.Mesh.AppendTriangle(center, va, vb) >= 0);
                Assert.True(f.Mesh.AppendTriangle(center, vb, vc) >= 0);
                f.Partitions.Add(CycleKey(Vector3d.Zero, a, b, c));
            }
            Assert.True(f.Mesh.IsBowtieVertex(center));
            f.Partitions.Sort(StringComparer.Ordinal);
            return f;
        }

        static BoundaryFixture HoleChain(bool touchesBorder, bool sparse)
        {
            const int n = 7;
            var f = new BoundaryFixture { Mesh = new DMesh3() };
            int dummy = -1;
            if (sparse) {
                int a = f.Mesh.AppendVertex(new Vector3d(-10, 0, -10));
                int b = f.Mesh.AppendVertex(new Vector3d(-9, 0, -10));
                int c = f.Mesh.AppendVertex(new Vector3d(-10, 0, -9));
                dummy = f.Mesh.AppendTriangle(a, b, c);
            }
            Func<int, int, Vector3d> p = (x, z) => new Vector3d(x, 0, z);
            var ids = new int[n + 1, n + 1];
            for (int z = 0; z <= n; ++z)
                for (int x = 0; x <= n; ++x) ids[x, z] = f.Mesh.AppendVertex(p(x, z));
            var holes = touchesBorder ? new[] { new Index2i(0, 0), new Index2i(1, 1) }
                : new[] { new Index2i(2, 2), new Index2i(3, 3), new Index2i(4, 4) };
            for (int z = 0; z < n; ++z)
                for (int x = 0; x < n; ++x) {
                    if (holes.Any(h => h.a == x && h.b == z)) continue;
                    Assert.True(f.Mesh.AppendTriangle(ids[x, z], ids[x + 1, z], ids[x + 1, z + 1]) >= 0);
                    Assert.True(f.Mesh.AppendTriangle(ids[x, z], ids[x + 1, z + 1], ids[x, z + 1]) >= 0);
                }
            foreach (Index2i h in holes.Where(h => h.a > 0))
                f.Partitions.Add(CycleKey(p(h.a, h.b), p(h.a + 1, h.b), p(h.a + 1, h.b + 1), p(h.a, h.b + 1)));
            var outer = new List<Vector3d>();
            for (int x = touchesBorder ? 1 : 0; x < n; ++x) outer.Add(p(x, 0));
            for (int z = 0; z < n; ++z) outer.Add(p(n, z));
            for (int x = n; x > 0; --x) outer.Add(p(x, n));
            for (int z = n; z > (touchesBorder ? 1 : 0); --z) outer.Add(p(0, z));
            if (touchesBorder) { outer.Add(p(0, 1)); outer.Add(p(1, 1)); }
            f.Partitions.Add(CycleKey(outer.ToArray()));
            f.Partitions.Sort(StringComparer.Ordinal);
            if (sparse) Assert.Equal(MeshResult.Ok, f.Mesh.RemoveTriangle(dummy, true, false));
            foreach (int v in f.Mesh.VertexIndices().ToArray())
                if (f.Mesh.GetVtxTriangleCount(v) == 0) Assert.Equal(MeshResult.Ok, f.Mesh.RemoveVertex(v));
            Assert.Contains(f.Mesh.VertexIndices(), v => f.Mesh.IsBowtieVertex(v));
            return f;
        }

        static BoundaryFixture NamedBoundaryFixture(string name)
        {
            switch (name) {
                case "two-fans": return Fans(2, false, false);
                case "many-fans": return Fans(5, false, false);
                case "tilted-fans": return Fans(3, true, false);
                case "aligned-fans": return Fans(3, false, true);
                case "hole-chain": return HoleChain(false, false);
                case "border-touch": return HoleChain(true, false);
                case "sparse-chain": return HoleChain(false, true);
                default: {
                    DMesh3 mesh = GridWithBowtieHoles();
                    var f = new BoundaryFixture { Mesh = mesh };
                    Func<double, double, Vector3d> p = (x, z) => new Vector3d(x, 0, z);
                    f.Partitions.Add(CycleKey(p(-1, -1), p(0, -1), p(0, 0), p(-1, 0)));
                    f.Partitions.Add(CycleKey(p(0, 0), p(1, 0), p(1, 1), p(0, 1)));
                    var outer = new List<Vector3d>();
                    for (int x = -3; x < 3; ++x) outer.Add(p(x, -3));
                    for (int z = -3; z < 3; ++z) outer.Add(p(3, z));
                    for (int x = 3; x > -3; --x) outer.Add(p(x, 3));
                    for (int z = 3; z > -3; --z) outer.Add(p(-3, z));
                    f.Partitions.Add(CycleKey(outer.ToArray()));
                    f.Partitions.Sort(StringComparer.Ordinal);
                    return f;
                }
            }
        }

        public static IEnumerable<object[]> AdversarialBoundaryCases()
        {
            foreach (string name in new[] { "two-fans", "many-fans", "tilted-fans", "aligned-fans", "hole-chain", "border-touch", "sparse-chain", "diagonal-holes" })
                foreach (int seed in new[] { 7, 19, 43, 101, 307, 701, 1237, 4093 })
                    yield return new object[] { name, seed };
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

        [Theory]
        [MemberData(nameof(AdversarialBoundaryCases))]
        public void AdversarialBowties_ExpectedPartitionsSurviveInsertionOrderAndSparseIds(string name, int seed)
        {
            BoundaryFixture f = NamedBoundaryFixture(name);
            DMesh3 mesh = seed == 7 ? f.Mesh : Invariance.Permuted(f.Mesh, seed);
            // The intended shared vertices are bowties; only vertex manifoldness is relaxed.
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(Invariance.AllFinite(mesh));
            Assert.Contains(mesh.VertexIndices(), v => mesh.IsBowtieVertex(v));
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
            foreach (string fixture in new[] { "two-fans", "hole-chain" }) {
                DMesh3 original = NamedBoundaryFixture(fixture).Mesh;
                var baseline = new MeshBoundaryLoops(original);
                DMesh3 mesh = Invariance.Transformed(original, s);
                var loops = new MeshBoundaryLoops(mesh);
                AssertSuccessfulCycles(mesh, loops);
                Func<MeshBoundaryLoops, string[]> ids = x => x.Loops.Select(l => string.Join(",", l.Edges.OrderBy(e => e)))
                    .OrderBy(k => k, StringComparer.Ordinal).ToArray();
                Assert.Equal(ids(baseline), ids(loops));
            }
        }

        [Fact]
        public void Reuse_BowtieClosedBowtieMatchesFreshStatusAndPartitions()
        {
            DMesh3 a = HoleChain(false, false).Mesh;
            DMesh3 b = new TrivialBox3Generator().Generate().MakeDMesh();
            var reused = new MeshBoundaryLoops(a, false);
            foreach (DMesh3 mesh in new[] { a, b, a }) {
                reused.Mesh = mesh;
                Assert.True(reused.Compute());
                var fresh = new MeshBoundaryLoops(mesh);
                AssertSuccessfulCycles(mesh, reused);
                Assert.Equal(PartitionKeys(mesh, fresh), PartitionKeys(mesh, reused));
                Assert.Equal(fresh.SawInconsistentOrientation, reused.SawInconsistentOrientation);
                Assert.Equal(fresh.SawOpenSpans, reused.SawOpenSpans);
                Assert.Equal(fresh.FellBackToSpansOnFailure, reused.FellBackToSpansOnFailure);
            }
        }

        [Theory]
        [InlineData(MeshBoundaryLoops.SpanBehaviors.Compute)]
        [InlineData(MeshBoundaryLoops.SpanBehaviors.Ignore)]
        [InlineData(MeshBoundaryLoops.SpanBehaviors.ThrowException)]
        public void FilterPolicies_OneMissingEdgeAndResetDoNotRetainSpanFlags(MeshBoundaryLoops.SpanBehaviors policy)
        {
            DMesh3 mesh = OpenBox();
            int removed = MeshIterators.BoundaryEdges(mesh).OrderBy(e => e).First();
            var loops = new MeshBoundaryLoops(mesh, false) { SpanBehavior = policy, EdgeFilterF = e => e != removed };
            var before = new DMesh3(mesh);
            int stamp = mesh.Timestamp, shape = mesh.ShapeTimestamp;
            if (policy == MeshBoundaryLoops.SpanBehaviors.ThrowException) Assert.Throws<MeshBoundaryLoopsException>(() => loops.Compute());
            else {
                Assert.True(loops.Compute());
                Assert.Empty(loops.Loops);
                Assert.True(loops.SawOpenSpans);
                if (policy == MeshBoundaryLoops.SpanBehaviors.Compute) {
                    Assert.Single(loops.Spans);
                    int[] expected = MeshIterators.BoundaryEdges(mesh).Where(e => e != removed).OrderBy(e => e).ToArray();
                    Assert.Equal(expected, loops.Spans.SelectMany(s => s.Edges).OrderBy(e => e).ToArray());
                } else Assert.Empty(loops.Spans);
            }
            AssertSnapshot(before, mesh, stamp, shape);
            loops.EdgeFilterF = null;
            Assert.True(loops.Compute());
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(PartitionKeys(mesh, new MeshBoundaryLoops(mesh)), PartitionKeys(mesh, loops));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Reuse_AllDiagnosticStateIsResetBeforeEitherComputePath(bool closed, bool filtered)
        {
            DMesh3 mesh = closed ? new TrivialBox3Generator().Generate().MakeDMesh() : OpenBox();
            var reused = new MeshBoundaryLoops(mesh, false) {
                SawOpenSpans = true,
                FellBackToSpansOnFailure = true,
                SawInconsistentOrientation = true,
                FailureBowties = new List<int> { 0 },
                EdgeFilterF = filtered ? (Func<int, bool>)(e => true) : null
            };
            Assert.True(reused.Compute());
            AssertSuccessfulCycles(mesh, reused);
            Assert.Null(reused.FailureBowties);
            var fresh = new MeshBoundaryLoops(mesh, false) { EdgeFilterF = reused.EdgeFilterF };
            Assert.True(fresh.Compute());
            Assert.Equal(PartitionKeys(mesh, fresh), PartitionKeys(mesh, reused));
            Assert.Equal(fresh.SawInconsistentOrientation, reused.SawInconsistentOrientation);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WholeOrientationReversal_PreservesPartitionsAndDirectedEdges(bool reverse)
        {
            BoundaryFixture f = HoleChain(false, false);
            if (reverse) foreach (int t in f.Mesh.TriangleIndices()) f.Mesh.ReverseTriOrientation(t);
            var loops = new MeshBoundaryLoops(f.Mesh);
            AssertSuccessfulCycles(f.Mesh, loops);
            Assert.Equal(f.Partitions, PartitionKeys(f.Mesh, loops));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void EmptyClosedAndSingleTriangle_HaveContractBoundaryCounts(int kind)
        {
            DMesh3 mesh = kind == 1 ? new TrivialBox3Generator().Generate().MakeDMesh() : new DMesh3();
            if (kind == 2) {
                int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(Vector3d.AxisY);
                mesh.AppendTriangle(a, b, c);
            }
            var loops = new MeshBoundaryLoops(mesh);
            AssertSuccessfulCycles(mesh, loops);
            Assert.Equal(kind == 2 ? 1 : 0, loops.Count);
            if (kind == 2) Assert.Equal(3, loops[0].VertexCount);
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
