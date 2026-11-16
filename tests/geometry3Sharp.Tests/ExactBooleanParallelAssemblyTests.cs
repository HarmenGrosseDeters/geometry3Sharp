using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    [CollectionDefinition("EdgeGroupsHooks", DisableParallelization = true)]
    public class EdgeGroupsHooksCollection { }

    /// <summary>
    /// The parallel steps of the boolean's classification and assembly (ExactMeshEdgeGroups, ExactMeshResultBuilder,
    /// DMesh3.FromEdgeStructure; docs/perf/BOOLEAN_THREAD_SCALING.md, levers 2 and 4): every result, vertex, triangle and edge ids
    /// included, equals the serial one, for every block size and schedule. The hooks (ParallelFrom, BlockSize, Disabled) are
    /// static, so the tests of this class do not run beside other tests.
    /// </summary>
    [Collection("EdgeGroupsHooks")]
    public class ExactBooleanParallelAssemblyTests
    {
        // ---- the building blocks against a naive reference ------------------------------------------------------------------

        static void WithBlocks(int parallelFrom, int blockSize, Action body)
        {
            int f = ExactMeshEdgeGroups.ParallelFrom, b = ExactMeshEdgeGroups.BlockSize;
            ExactMeshEdgeGroups.ParallelFrom = parallelFrom;
            ExactMeshEdgeGroups.BlockSize = blockSize;
            try { body(); }
            finally { ExactMeshEdgeGroups.ParallelFrom = f; ExactMeshEdgeGroups.BlockSize = b; }
        }

        static long[] Keys(int n, int kind, int seed)
        {
            // (kind 0: edge keys of ids below 2^20, many equal; 1: all equal; 2: one high part (a fan around a vertex); 3: low parts
            // only; 4: ids up to 2^31 - 1, so the high part spans the range; 5: a few huge keys among small ones)
            var rand = new Random(seed);
            var keys = new long[n];
            for (int i = 0; i < n; ++i) {
                long a, b;
                switch (kind) {
                    case 1: a = 7; b = 9; break;
                    case 2: a = 5; b = rand.Next(1 << 20); break;
                    case 3: a = 0; b = rand.Next(1 << 22); break;
                    case 4: a = rand.Next(int.MaxValue); b = rand.Next(int.MaxValue); break;
                    case 5: a = (i % 97 == 0) ? int.MaxValue - 5 : rand.Next(50); b = rand.Next(1000); break;
                    default: a = rand.Next(1 << 18); b = rand.Next(1 << 20); break;
                }
                keys[i] = (a << 32) | b;
                if (kind == 0 && i % 4 == 3)
                    keys[i] = keys[rand.Next(i)];       // repeated edges
            }
            return keys;
        }

        [Theory]
        [InlineData(0, 70000, 0)]
        [InlineData(1, 70000, 1)]
        [InlineData(2, 70000, 2)]
        [InlineData(3, 70000, 3)]
        [InlineData(4, 70000, 4)]
        [InlineData(5, 70000, 5)]
        [InlineData(0, 1000, 6)]
        [InlineData(0, 100000, 7)]
        public void SortByKey_Parallel_EqualsStableReference(int kind, int n, int seed)
        {
            long[] keys0 = Keys(n, kind, seed);
            var expected = keys0.Select((k, i) => (k, i)).OrderBy(p => p.k).ThenBy(p => p.i).ToArray();
            // (the default threshold, and every block size down to a few entries, with the parallel path forced)
            foreach (var setting in new[] { (1 << 16, 1 << 13), (1, 1 << 13), (1, 1000), (1, 37) }) {
                WithBlocks(setting.Item1, setting.Item2, () => {
                    long[] keys = (long[])keys0.Clone();
                    int[] items = Enumerable.Range(0, n).ToArray();
                    ExactMeshEdgeGroups.SortByKey(keys, items);
                    Assert.True(expected.Select(p => p.k).SequenceEqual(keys), $"keys, setting {setting}");
                    Assert.True(expected.Select(p => p.i).SequenceEqual(items), $"items, setting {setting}");
                });
            }
        }

        [Fact]
        public void GroupEdges_Parallel_EqualsSerial()
        {
            // a triangle strip with repeated edges and a few shared ones
            var rand = new Random(3);
            var tris = new List<Index3i>();
            for (int i = 0; i < 30000; ++i) {
                int a = rand.Next(9000), b = rand.Next(9000), c = rand.Next(9000);
                tris.Add(new Index3i(a, b, c));
            }
            long[] serialKeys = null; int[] serialTri = null;
            WithBlocks(int.MaxValue, 1 << 13, () => ExactMeshEdgeGroups.GroupEdges(tris.Count, (t) => tris[t], out serialKeys, out serialTri));
            foreach (int blockSize in new[] { 1 << 13, 500, 29 }) {
                WithBlocks(1, blockSize, () => {
                    long[] keys; int[] tri;
                    ExactMeshEdgeGroups.GroupEdges(tris.Count, (t) => tris[t], out keys, out tri);
                    Assert.Equal(serialKeys, keys);
                    Assert.Equal(serialTri, tri);
                });
            }
            // (sorted, and equal edges in the order of their triangles)
            for (int k = 1; k < serialKeys.Length; ++k) {
                Assert.True(serialKeys[k - 1] <= serialKeys[k]);
                if (serialKeys[k - 1] == serialKeys[k])
                    Assert.True(serialTri[k - 1] <= serialTri[k]);
            }
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void UnionFind_RootIsSmallestMember_WhateverTheOrder(int seed, bool concurrent)
        {
            // random unions in random orders, one thread or many: the classes are the connected components of the pairs and the root of
            // each is its smallest member
            var rand = new Random(seed);
            int n = 20000;
            var pairs = new List<(int, int)>();
            for (int i = 0; i < 9000; ++i)
                pairs.Add((rand.Next(n), rand.Next(n)));
            int[] reference = Enumerable.Range(0, n).ToArray();
            Func<int, int> ref_find = null;
            ref_find = (x) => reference[x] == x ? x : (reference[x] = ref_find(reference[x]));
            foreach (var (a, b) in pairs) {
                int ra = ref_find(a), rb = ref_find(b);
                if (ra != rb) reference[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
            int[] expected = Enumerable.Range(0, n).Select(ref_find).ToArray();
            for (int order = 0; order < 4; ++order) {
                var shuffled = pairs.OrderBy(p => (p.Item1 * 31 + p.Item2 * 17 + order * 101) % 1013).ThenBy(p => order % 2 == 0 ? p.Item1 : -p.Item1).ToList();
                int[] parent = ExactMeshEdgeGroups.NewForest(n);
                if (concurrent)
                    Parallel.For(0, shuffled.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, (i) => ExactMeshEdgeGroups.Union(parent, shuffled[i].Item1, shuffled[i].Item2, true));
                else
                    foreach (var (a, b) in shuffled) ExactMeshEdgeGroups.Union(parent, a, b, false);
                for (int x = 0; x < n; ++x)
                    Assert.Equal(expected[x], ExactMeshEdgeGroups.Find(parent, x));
            }
        }

        [Fact]
        public void NumberClasses_NumbersRootsInSlotOrder()
        {
            var rand = new Random(4);
            int n = 50000;
            int[] parent = ExactMeshEdgeGroups.NewForest(n);
            for (int i = 0; i < 30000; ++i)
                ExactMeshEdgeGroups.Union(parent, rand.Next(n), rand.Next(n), false);
            // (what a serial scan numbers: a class gets the next number at its first slot)
            var number = new Dictionary<int, int>();
            var expected = new int[n];
            var roots = new List<int>();
            for (int s = 0; s < n; ++s) {
                int r = ExactMeshEdgeGroups.Find(parent, s);
                int v;
                if (number.TryGetValue(r, out v) == false) {
                    number[r] = v = number.Count;
                    roots.Add(s);
                }
                expected[s] = v;
            }
            foreach (var setting in new[] { (1 << 16, 1 << 13), (1, 1 << 13), (1, 41) }) {
                WithBlocks(setting.Item1, setting.Item2, () => {
                    int[] vids, rootSlot;
                    int count = ExactMeshEdgeGroups.NumberClasses((int[])parent.Clone(), out vids, out rootSlot);
                    Assert.Equal(number.Count, count);
                    Assert.Equal(expected, vids);
                    Assert.Equal(roots, rootSlot);
                });
            }
        }

        [Fact]
        public void UsedPoints_AreTheTrianglePointsInIdOrder()
        {
            var rand = new Random(5);
            var tris = new List<Index3i>();
            for (int i = 0; i < 4000; ++i)
                tris.Add(new Index3i(rand.Next(1, 30000), rand.Next(1, 30000), rand.Next(1, 30000)));
            var expected = tris.SelectMany(t => new[] { t.a, t.b, t.c }).Distinct().OrderBy(x => x).ToList();
            foreach (var setting in new[] { (1 << 16, 1 << 13), (1, 1 << 13), (1, 77) }) {
                WithBlocks(setting.Item1, setting.Item2, () => {
                    int[] usedIndex;
                    List<int> used = ExactMeshEdgeGroups.UsedPoints(tris, 30000, out usedIndex);
                    Assert.Equal(expected, used);
                    for (int p = 0; p < usedIndex.Length; ++p)
                        Assert.Equal(expected.BinarySearch(p) >= 0 ? expected.BinarySearch(p) : -1, usedIndex[p]);
                });
            }
        }

        // ---- the boolean ----------------------------------------------------------------------------------------------------

        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // a cylinder whose cap centres have 40 edges each (a vertex list that spills out of its block of 8)
        static DMesh3 Cylinder(Vector3d center, double radius, double height, int slices)
        {
            DMesh3 c = new CappedCylinderGenerator() { BaseRadius = (float)radius, TopRadius = (float)radius, Height = (float)height, Slices = slices }.Generate().MakeDMesh();
            MeshTransforms.Translate(c, center);
            return c;
        }

        // every id and coordinate bit of the vertices and triangles, and the edges and the order of the edges around each vertex
        static string Structure(DMesh3 m)
        {
            var sb = new StringBuilder();
            sb.Append(m.MaxVertexID).Append('/').Append(m.MaxTriangleID).Append('/').Append(m.MaxEdgeID).Append('/').Append(m.Timestamp).Append('/').Append(m.ShapeTimestamp)
                .Append('/').Append(m.VerticesRefCounts.count).Append('/').Append(m.TrianglesRefCounts.count).Append('/').Append(m.EdgesRefCounts.count).Append('|');
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.z)).Append(m.GetVertexNormal(v)).Append('r').Append(m.VerticesRefCounts.rawRefCount(v)).Append('e');
                foreach (int e in m.VtxEdgesItr(v))
                    sb.Append(e).Append(',');
                sb.Append('t');
                foreach (int t in m.VtxTrianglesItr(v))
                    sb.Append(t).Append(',');
                sb.Append(';');
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t), e = m.GetTriEdges(t);
                sb.Append(t).Append(':').Append(m.TrianglesRefCounts.rawRefCount(t)).Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append('/').Append(e.a).Append(',').Append(e.b).Append(',').Append(e.c).Append(';');
            }
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e), et = m.GetEdgeT(e);
                sb.Append(e).Append(':').Append(m.EdgesRefCounts.rawRefCount(e)).Append(ev.a).Append(',').Append(ev.b).Append('/').Append(et.a).Append(',').Append(et.b).Append(';');
            }
            return sb.ToString();
        }

        static string Counters(ExactMeshBoolean b) => $"conflict {b.ConflictEdges} nonmanifold {b.NonManifoldEdges} split {b.SplitNonManifoldEdges} nmtri {b.NonManifoldTriangles} bowtie {b.SplitBowtieVertices} patches {b.Patches}";

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op, bool exactVertices = false)
        {
            var eb = new ExactMeshBoolean(a, b, op) { ComputeExactVertices = exactVertices };
            Assert.True(eb.Compute(), eb.Error);
            Assert.True(eb.Result.CheckValidity(true, FailMode.ReturnOnly));
            return eb;
        }

        static IEnumerable<(string, Func<ExactMeshBoolean>)> Cases()
        {
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 12);
            DMesh3 cyl = Cylinder(new Vector3d(0.1, 0.1, -0.2), 0.6, 1.4, 40);
            yield return ("box-sphere diff", () => Run(box, sphere, ExactMeshBoolean.Operations.Difference));
            yield return ("box-sphere union", () => Run(box, sphere, ExactMeshBoolean.Operations.Union));
            yield return ("box-cylinder union", () => Run(box, cyl, ExactMeshBoolean.Operations.Union));
            yield return ("sphere-cylinder intersection", () => Run(sphere, cyl, ExactMeshBoolean.Operations.Intersection));
            // (solids touching along edges and at vertices: the serial path of the assembly)
            DMesh3 b1 = Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1)), b2 = Box(new Vector3d(1, 1, -0.2), new Vector3d(2, 2, 1.3));
            yield return ("touching boxes union", () => Run(b1, b2, ExactMeshBoolean.Operations.Union));
            // (an open input: generalized winding numbers, bowtie vertices)
            DMesh3 open = Sphere(new Vector3d(-0.2, 0.1, 0), 0.9, 9);
            open.RemoveTriangle(open.TriangleIndices().First());
            yield return ("open sphere - box", () => Run(open, box, ExactMeshBoolean.Operations.Intersection));
            // (the per-patch evaluation of the open mesh without the traced one: the patch joins are collected for it, in the order of the sorted edges)
            yield return ("open per-patch sphere - box", () => {
                var eb = new ExactMeshBoolean(open, box, ExactMeshBoolean.Operations.Union) { TracedOpenWinding = false };
                Assert.True(eb.Compute(), eb.Error);
                Assert.True(eb.Result.CheckValidity(true, FailMode.ReturnOnly));
                return eb;
            });
        }

        public static IEnumerable<object[]> CaseNames() => Cases().Select(c => new object[] { c.Item1 });

        static Func<ExactMeshBoolean> Case(string name) => Cases().First(c => c.Item1 == name).Item2;

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ParallelAssembly_SameIdsAsSerial_ForEveryBlockSize(string name)
        {
            var make = Case(name);
            string serial;
            gParallel.SerialOnThisThread = true;
            try { var sb = WithHooks(1 << 30, 1 << 13, true, make); serial = Structure(sb.Result) + Counters(sb); }
            finally { gParallel.SerialOnThisThread = false; }
            // (the serial path of every step; then every step parallel with blocks of 16 .. 8192 entries, so that most edge groups
            // meet a block border, the result built in one go or appended, and under the permuted orders of the environment
            // adversary on one thread)
            foreach (int blockSize in name.StartsWith("touching") || name.StartsWith("open") ? new[] { 2, 3, 5, 7, 16, 1 << 13 } : new[] { 16, 1 << 13 }) {
                foreach (bool disabled in new[] { false, true }) {
                    var pb = WithHooks(1, blockSize, disabled, make);
                    Assert.True(serial == Structure(pb.Result) + Counters(pb), $"{name}: block size {blockSize}, builder disabled {disabled}");
                    for (int seed = 1; seed <= 1; ++seed) {
                        gParallel.SerialOnThisThread = true;
                        gParallel.SerialOrderOnThisThread = seed;
                        try {
                            var ob = WithHooks(1, blockSize, disabled, make);
                            Assert.True(serial == Structure(ob.Result) + Counters(ob), $"{name}: block size {blockSize}, order {seed}, builder disabled {disabled}");
                        } finally { gParallel.SerialOrderOnThisThread = 0; gParallel.SerialOnThisThread = false; }
                    }
                }
            }
        }

        static ExactMeshBoolean WithHooks(int parallelFrom, int blockSize, bool builderDisabled, Func<ExactMeshBoolean> make)
        {
            ExactMeshBoolean eb = null;
            bool d = ExactMeshResultBuilder.Disabled;
            ExactMeshResultBuilder.Disabled = builderDisabled;
            try { WithBlocks(parallelFrom, blockSize, () => eb = make()); }
            finally { ExactMeshResultBuilder.Disabled = d; }
            return eb;
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void BulkBuilder_BuildsTheMeshThatTheAppendsBuild(string name)
        {
            // the one-go builder against AppendVertex / AppendTriangle: every id, corner, edge, edge list order (also the lists of the
            // cylinder's cap centres, past the 8 of a block) and time stamp
            var make = Case(name);
            int before = ExactMeshResultBuilder.Built;
            ExactMeshBoolean built = WithHooks(1 << 16, 1 << 13, false, make);
            int taken = ExactMeshResultBuilder.Built - before;
            ExactMeshBoolean appended = WithHooks(1 << 16, 1 << 13, true, make);
            Assert.Equal(ExactMeshResultBuilder.Built - before, taken);     // (disabled: nothing built)
            // (the one-go path is the one taken exactly where no edge has more than two pieces or two pieces running it the same way)
            Assert.Equal(built.NonManifoldEdges + built.ConflictEdges == 0 ? 1 : 0, taken);
            Assert.Equal(Structure(appended.Result), Structure(built.Result));
            ResultChecksClosed(built, name);
        }

        static void ResultChecksClosed(ExactMeshBoolean b, string name)
        {
            Assert.True(b.Result.TriangleCount > 0, name);
            Assert.True(b.Result.CheckValidity(true, FailMode.ReturnOnly), name);
            foreach (int v in b.Result.VertexIndices()) {
                Vector3d p = b.Result.GetVertex(v);
                Assert.True(!double.IsNaN(p.x + p.y + p.z) && !double.IsInfinity(p.x + p.y + p.z) && Math.Abs(p.x) < 1e300 && Math.Abs(p.y) < 1e300 && Math.Abs(p.z) < 1e300, name + ": finite");
            }
            if (name.StartsWith("open") == false && name.StartsWith("touching") == false) {
                Assert.True(b.Result.IsClosed(), name + ": closed");
                Assert.Equal("", MeshRobustRepair.NotCleanReason(b.Result));
            }
            Assert.Equal(0, b.Result.EdgeIndices().Count(e => b.Result.GetEdgeT(e).b == DMesh3.InvalidID && name.StartsWith("open") == false && name.StartsWith("touching") == false));
        }

        [Fact]
        public void BulkBuilder_IsTaken_ForTheSimpleCases_AndHasVerticesPastTheBlock()
        {
            // the cylinder's cap centres: more than 8 edges, so the spill into the linked store is exercised by the comparison above
            ExactMeshBoolean b = Run(Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1)), Cylinder(new Vector3d(0.1, 0.1, -0.2), 0.6, 1.4, 40), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Result.VertexIndices().Any(v => b.Result.GetVtxEdgeCount(v) > 8));
            Assert.Equal(0, b.NonManifoldEdges + b.ConflictEdges);
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ParallelAssembly_RepeatedRunsAndReuse_GiveTheSameMesh(string name)
        {
            var make = Case(name);
            string first = null;
            for (int run = 0; run < 6; ++run) {
                string print = Structure(WithHooks(1, 64, false, make).Result);
                if (first == null) first = print;
                else Assert.True(first == print, $"{name}: run {run} differs");
            }
            // (reuse: the same instance computed twice, and the second result is the first)
            WithBlocks(1, 64, () => {
                DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
                DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 12);
                var eb = new ExactMeshBoolean(box, sphere, ExactMeshBoolean.Operations.Difference);
                Assert.True(eb.Compute(), eb.Error);
                string a = Structure(eb.Result);
                Assert.True(eb.Compute(), eb.Error);
                Assert.Equal(a, Structure(eb.Result));
                // (and another operation on the instance, against a fresh one)
                eb.Operation = ExactMeshBoolean.Operations.Union;
                Assert.True(eb.Compute(), eb.Error);
                var fresh = new ExactMeshBoolean(box, sphere, ExactMeshBoolean.Operations.Union);
                Assert.True(fresh.Compute(), fresh.Error);
                Assert.Equal(Structure(fresh.Result), Structure(eb.Result));
            });
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void ParallelAssembly_SameResultUnderSimilarity(string similarity)
        {
            Similarity s = Similarity.All().First(x => x.Name == similarity);
            DMesh3 box = Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 sphere = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 12);
            DMesh3 cyl = Cylinder(new Vector3d(0.1, 0.1, -0.2), 0.6, 1.4, 40);
            foreach (var op in new[] { ExactMeshBoolean.Operations.Union, ExactMeshBoolean.Operations.Difference }) {
                DMesh3 reference = null, transformed = null;
                WithBlocks(1, 64, () => {
                    reference = Run(box, cyl, op).Result;
                    transformed = Run(Invariance.Transformed(box, s), Invariance.Transformed(cyl, s), op).Result;
                });
                Assert.True(transformed.IsClosed());
                Assert.True(transformed.CheckValidity(true, FailMode.ReturnOnly));
                DMesh3 mapped = Invariance.MappedBack(transformed, s);
                // (other similarities round the inputs, so flat regions and triangle counts differ: the volume agrees to the rounding)
                Assert.True(Math.Abs(Invariance.Volume(reference) - Invariance.Volume(mapped)) <= 1e-9 * Math.Abs(Invariance.Volume(reference)) + 100 * s.Resolution(2), s.Name);
                if (s.IsExact) {
                    Assert.Equal(reference.TriangleCount, transformed.TriangleCount);
                    // (a power of two: the same ids and the same bits once mapped back)
                    DMesh3 back = Invariance.MappedBack(transformed, s);
                    Assert.Equal(Structure(reference), Structure(back).Replace(back.Timestamp + "/" + back.ShapeTimestamp, reference.Timestamp + "/" + reference.ShapeTimestamp));
                }
            }
        }


        // ---- the primitives on their own ------------------------------------------------------------------------------------

        [Fact]
        public void SmallListSet_BuildFrom_EqualsInserts()
        {
            // lists of 0, 1, 7, 8, 9, 10, 17 and 30 values (the block holds 8, the rest goes to the linked store, newest first)
            int[] sizes = { 0, 1, 7, 8, 9, 10, 17, 30, 2, 0, 12 };
            var begin = new int[sizes.Length + 1];
            for (int i = 0; i < sizes.Length; ++i) begin[i + 1] = begin[i] + sizes[i];
            var values = Enumerable.Range(100, begin[sizes.Length]).Select(x => x * 3 % 1000).ToArray();
            var built = new SmallListSet();
            built.BuildFrom(sizes.Length, begin, values);
            var inserted = new SmallListSet();
            inserted.Resize(sizes.Length);
            for (int i = 0; i < sizes.Length; ++i)
                for (int k = begin[i]; k < begin[i + 1]; ++k) {
                    if (k == begin[i]) inserted.AllocateAt(i);
                    inserted.Insert(i, values[k]);
                }
            for (int pass = 0; pass < 2; ++pass) {
                Assert.Equal(inserted.Size, built.Size);
                for (int i = 0; i < sizes.Length; ++i) {
                    Assert.Equal(inserted.Count(i), built.Count(i));
                    Assert.Equal(inserted.ValueItr(i).ToList(), built.ValueItr(i).ToList());
                }
                // (then the same edits on both: removals from the block, from the chain, inserts after)
                for (int i = 0; i < sizes.Length; ++i) {
                    if (sizes[i] == 0) continue;
                    int victim = values[begin[i] + (pass == 0 ? 0 : sizes[i] - 1)];
                    Assert.Equal(inserted.Remove(i, victim), built.Remove(i, victim));
                    inserted.Insert(i, 5000 + i);
                    built.Insert(i, 5000 + i);
                }
            }
            Assert.Throws<InvalidOperationException>(() => built.BuildFrom(1, new[] { 0, 0 }, new int[0]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2047)]
        [InlineData(2048)]
        [InlineData(2049)]
        [InlineData(4096)]
        public void DVector_BulkSetters_LeaveWhatPushBackLeaves(int count)
        {
            var pushed = new DVector<int>();
            for (int i = 0; i < count; ++i) pushed.Add(i % 7);
            var bulk = new DVector<int>();
            bulk.ResizeLikePushBack(count);
            bulk.SetFirstRepeating(new[] { 0, 1, 2, 3, 4, 5, 6 }, count);
            Assert.Equal(pushed.Length, bulk.Length);
            Assert.Equal(pushed.MemoryUsageBytes, bulk.MemoryUsageBytes);
            if (count > 0) Assert.Equal(pushed.back, bulk.back);
            for (int i = 0; i < count; ++i) Assert.Equal(pushed[i], bulk[i]);
            for (int i = 0; i < 5; ++i) { pushed.Add(9); bulk.Add(9); }
            Assert.Equal(pushed.Length, bulk.Length);
            Assert.Equal(pushed.back, bulk.back);
            var copy = new DVector<int>();
            copy.ResizeLikePushBack(count);
            copy.SetFirst(Enumerable.Range(0, count).Select(i => i % 7).ToArray(), count);
            for (int i = 0; i < count; ++i) Assert.Equal(pushed[i], copy[i]);
            var refs = new RefCountVector();
            refs.AllocateBulk(count);
            Assert.Equal(count, refs.count);
            Assert.Equal(count, refs.max_index);
            if (count > 0) Assert.Throws<InvalidOperationException>(() => refs.AllocateBulk(1));
        }

        // pieces as point ids; every point is a vertex of its own (vids = the points) unless vids is given
        static DMesh3 TryBuild(int[][] pieces, int[] vids = null)
        {
            var tris = pieces.Select(p => new Index3i(p[0], p[1], p[2])).ToList();
            int points = pieces.SelectMany(p => p).Max() + 1;
            vids = vids ?? pieces.SelectMany(p => p).ToArray();
            int vertices = vids.Max() + 1;
            var vertexPoint = Enumerable.Range(0, vertices).ToList();
            var coords = Enumerable.Range(0, points).Select(i => new Vector3d(i, i * i, 0.5 * i)).ToArray();
            var usedIndex = Enumerable.Range(0, points).ToArray();
            long[] keys; int[] edgeTri;
            ExactMeshEdgeGroups.GroupEdges(tris.Count, (t) => tris[t], out keys, out edgeTri);
            return ExactMeshResultBuilder.TryBuild(tris, vids, vertexPoint, coords, usedIndex, keys, edgeTri);
        }

        [Fact]
        public void TryBuild_RefusesLayoutsThatAppendsWouldNotGiveTheSameWay()
        {
            Assert.NotNull(TryBuild(new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 } }));
            // (three pieces at one edge; two running it the same way; a vertex twice in a piece; the ends of a shared edge at different vertices)
            Assert.Null(TryBuild(new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 0, 1, 4 } }));
            Assert.Null(TryBuild(new[] { new[] { 0, 1, 2 }, new[] { 0, 1, 3 } }));
            Assert.Null(TryBuild(new[] { new[] { 0, 1, 2 } }, new[] { 0, 1, 1 }));
            Assert.Null(TryBuild(new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 } }, new[] { 0, 1, 2, 5, 4, 3 }));
            // (a piece (p, q, p): its edge key twice in one group)
            Assert.Null(TryBuild(new[] { new[] { 0, 1, 0 } }, new[] { 0, 1, 2 }));
            // (the built mesh of the valid one equals the appended one)
            DMesh3 built = TryBuild(new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 2, 3, 0 } , new[] { 3, 2, 1 } });
            var appended = new DMesh3();
            for (int i = 0; i < 4; ++i) appended.AppendVertex(new Vector3d(i, i * i, 0.5 * i));
            foreach (var t in new[] { new[] { 0, 1, 2 }, new[] { 1, 0, 3 }, new[] { 2, 3, 0 }, new[] { 3, 2, 1 } }) appended.AppendTriangle(t[0], t[1], t[2]);
            Assert.NotNull(built);
            Assert.Equal(Structure(appended), Structure(built));
        }

        [Fact]
        public void BuiltMesh_BehavesLikeTheAppendedMeshUnderEdits()
        {
            var make = Case("box-cylinder union");
            DMesh3 built = WithHooks(1 << 16, 1 << 13, false, make).Result, appended = WithHooks(1 << 16, 1 << 13, true, make).Result;
            var log = new StringBuilder();
            foreach (DMesh3 m in new[] { built, appended }) {
                log.Append('#');
                for (int i = 0; i < 60; ++i) {
                    int e = (i * 37 + 3) % m.MaxEdgeID;
                    if (m.IsEdge(e) == false) continue;
                    DMesh3.EdgeSplitInfo si; DMesh3.EdgeFlipInfo fi; DMesh3.EdgeCollapseInfo ci;
                    switch (i % 4) {
                        case 0: log.Append(m.SplitEdge(e, out si)); break;
                        case 1: log.Append(m.FlipEdge(e, out fi)); break;
                        case 2: Index2i ev = m.GetEdgeV(e); log.Append(m.CollapseEdge(ev.a, ev.b, out ci)); break;
                        default: log.Append(m.RemoveTriangle(m.GetEdgeT(e).a)); break;
                    }
                }
            }
            string text = log.ToString();
            string[] halves = text.Split('#');
            Assert.Equal(halves[1], halves[2]);
            Assert.Equal(Structure(appended), Structure(built));
        }

        [Fact]
        public void ProductionSettings_LargeBoolean_SameAsSerialAndAppended()
        {
            // above the default thresholds (65536 sorted entries, blocks of 8192): the sort, the pairing and the builder in their real configuration
            DMesh3 a = Sphere(new Vector3d(0, 0, 0), 1, 52), b = Sphere(new Vector3d(0.4, 0.3, 0.2), 0.8, 48);
            Assert.True(3 * (a.TriangleCount + b.TriangleCount) > 70000);
            Func<ExactMeshBoolean> make = () => Run(a, b, ExactMeshBoolean.Operations.Union);
            string serial;
            gParallel.SerialOnThisThread = true;
            try { var sb = WithHooks(1 << 30, 1 << 13, true, make); serial = Structure(sb.Result) + Counters(sb); }
            finally { gParallel.SerialOnThisThread = false; }
            int before = ExactMeshResultBuilder.Built;
            var pb = WithHooks(1 << 16, 1 << 13, false, make);
            Assert.True(ExactMeshResultBuilder.Built > before);
            Assert.True(serial == Structure(pb.Result) + Counters(pb));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(pb.Result));
        }

        [Fact]
        public void SubPhaseTimers_ArePresent()
        {
            // the sub-steps of the assembly and the classification are timed in microseconds ("<phase>.<step>_us")
            ExactMeshBoolean b = Run(Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1)), Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 12), ExactMeshBoolean.Operations.Union);
            foreach (string key in new[] { "assembly.gather", "assembly.simplify", "assembly.edges", "assembly.pairing", "assembly.used", "assembly.coords", "assembly.vertices", "assembly.split", "assembly.triangles", "assembly.rest",
                "classification.stacks", "classification.edges", "classification.patches", "classification.prep", "classification.firsts", "classification.winding", "classification.open", "classification.decisions" }) {
                long us;
                Assert.True(b.PhaseMilliseconds.TryGetValue(key + "_us", out us), key);
                Assert.True(us >= 0 && us < 60L * 1000 * 1000, key);
            }
            Assert.False(b.PhaseMilliseconds.ContainsKey("_us"));
        }

        // every field of the mesh by reflection: DVectors by content, reference counts, edge lists; the overflow nodes of SmallListSet
        // (linked_store, the chain heads in the blocks) are skipped, they are laid out in another order (SmallListSet.BuildFrom)
        static void Walk(object a, object b, string path, List<string> differences)
        {
            if (a == null || b == null) {
                if (a != b) differences.Add(path + ": null");
                return;
            }
            Type t = a.GetType();
            if (t.IsPrimitive || t == typeof(string) || t.IsEnum) {
                if (a.Equals(b) == false) differences.Add(path + ": " + a + " vs " + b);
                return;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(DVector<>)) {
                var ea = ((System.Collections.IEnumerable)a).Cast<object>().ToList();
                var eb = ((System.Collections.IEnumerable)b).Cast<object>().ToList();
                if (ea.Count != eb.Count) { differences.Add(path + ": length " + ea.Count + " vs " + eb.Count); return; }
                bool blocks = path.EndsWith("block_store");
                for (int i = 0; i < ea.Count; ++i) {
                    if (blocks && i % 10 == 9) continue;      // (the chain head of each block of 8 values + count + link)
                    if (ea[i].Equals(eb[i]) == false) { differences.Add(path + "[" + i + "]: " + ea[i] + " vs " + eb[i]); return; }
                }
                return;
            }
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)) {
                // (free_head_ptr points into linked_store, so it is part of the overflow layout; the free blocks and counts are compared)
                if (f.Name == "linked_store" || f.Name == "Metadata" || f.Name == "free_head_ptr") continue;
                Walk(f.GetValue(a), f.GetValue(b), path + "." + f.Name, differences);
            }
        }

        [Theory]
        [InlineData("box-cylinder union")]
        [InlineData("box-sphere diff")]
        [InlineData("open sphere - box")]
        public void BuiltMesh_EveryContainerEqualsTheAppendedOne_ExceptTheOverflowNodes(string name)
        {
            var make = Case(name);
            DMesh3 built = WithHooks(1 << 16, 1 << 13, false, make).Result, appended = WithHooks(1 << 16, 1 << 13, true, make).Result;
            var differences = new List<string>();
            Walk(built, appended, "mesh", differences);
            Assert.True(differences.Count == 0, string.Join("; ", differences.Take(8)));
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
