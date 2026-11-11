using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The preparation of the input meshes of ExactMeshBoolean (closedness in the winding number sense, consistent
    /// orientation per component): checked against a reference implementation with the dictionaries of the original
    /// code, on meshes with unwelded vertices, flipped, missing, degenerate and non-manifold triangles; and the
    /// preparation of several meshes in parallel.
    /// </summary>
    public class ExactBooleanInputPreparationTests
    {
        // ---- reference: the original dictionary implementation ----

        static Dictionary<Vector3d, int> RefIds(DMesh3 mesh, int[] pos)
        {
            var ids = new Dictionary<Vector3d, int>();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                if (v.x == 0) v.x = 0;
                if (v.y == 0) v.y = 0;
                if (v.z == 0) v.z = 0;
                int k;
                if (ids.TryGetValue(v, out k) == false) {
                    k = ids.Count;
                    ids[v] = k;
                }
                pos[vid] = k;
            }
            return ids;
        }

        static bool RefClosed(DMesh3 mesh)
        {
            var pos = new int[mesh.MaxVertexID];
            RefIds(mesh, pos);
            var count = new Dictionary<long, int>();
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                int[] v = { pos[t.a], pos[t.b], pos[t.c] };
                for (int k = 0; k < 3; ++k) {
                    int x = v[k], y = v[(k + 1) % 3];
                    if (x == y) continue;
                    long key = (x < y) ? (((long)x << 32) | (uint)y) : (((long)y << 32) | (uint)x);
                    count.TryGetValue(key, out int c0);
                    count[key] = c0 + ((x < y) ? 1 : -1);
                }
            }
            return count.Values.All(c => c == 0);
        }

        static bool Runs(Index3i t, int u, int v)
        {
            return (t.a == u && t.b == v) || (t.b == u && t.c == v) || (t.c == u && t.a == v);
        }

        // the triangles flipped by the original oriented(), in order of discovery (null: none)
        static List<int> RefToFlip(DMesh3 mesh)
        {
            var pos = new int[mesh.MaxVertexID];
            RefIds(mesh, pos);
            Func<int, Index3i> tri = (t) => { Index3i f = mesh.GetTriangle(t); return new Index3i(pos[f.a], pos[f.b], pos[f.c]); };
            var edgeTris = new Dictionary<long, List<int>>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i f = tri(t);
                for (int k = 0; k < 3; ++k) {
                    int u = f[k], v = f[(k + 1) % 3];
                    if (u == v) continue;
                    long key = ExactMeshArrangement.EdgeKey(u, v);
                    if (edgeTris.TryGetValue(key, out List<int> l) == false)
                        edgeTris[key] = l = new List<int>(2);
                    l.Add(t);
                }
            }
            int maxT = mesh.MaxTriangleID;
            var flipT = new bool[maxT];
            var seen = new bool[maxT];
            var component = new List<int>();
            var stack = new Stack<int>();
            var toFlip = new List<int>();
            foreach (int root in mesh.TriangleIndices()) {
                if (seen[root]) continue;
                component.Clear();
                bool orientable = true;
                seen[root] = true;
                stack.Push(root);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    component.Add(t);
                    Index3i f = tri(t);
                    for (int k = 0; k < 3; ++k) {
                        int u = f[k], v = f[(k + 1) % 3];
                        if (u == v) continue;
                        List<int> l = edgeTris[ExactMeshArrangement.EdgeKey(u, v)];
                        if (l.Count != 2) continue;
                        int o = (l[0] == t) ? l[1] : l[0];
                        if (o == t) continue;
                        bool want = flipT[t] ^ Runs(tri(o), u, v);
                        if (seen[o]) {
                            if (flipT[o] != want) orientable = false;
                            continue;
                        }
                        seen[o] = true;
                        flipT[o] = want;
                        stack.Push(o);
                    }
                }
                if (orientable == false) continue;
                double flippedArea = 0, keptArea = 0;
                foreach (int t in component) {
                    if (flipT[t]) flippedArea += mesh.GetTriArea(t); else keptArea += mesh.GetTriArea(t);
                }
                bool invert = flippedArea - keptArea > 1e-9 * (flippedArea + keptArea);
                foreach (int t in component)
                    if (flipT[t] != invert) toFlip.Add(t);
            }
            return toFlip;
        }

        // ---- meshes ----

        static DMesh3 Sphere(int edgeVertices, Vector3d shift)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, shift);
            return s;
        }

        // triangle soup of the mesh (every corner its own vertex), a triangle flipped / dropped when the predicates say so
        static DMesh3 Soup(DMesh3 m, Func<int, bool> flip, Func<int, bool> drop)
        {
            var s = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                if (drop(t)) continue;
                Index3i f = m.GetTriangle(t);
                int a = s.AppendVertex(m.GetVertex(f.a)), b = s.AppendVertex(m.GetVertex(f.b)), c = s.AppendVertex(m.GetVertex(f.c));
                s.AppendTriangle(flip(t) ? new Index3i(a, c, b) : new Index3i(a, b, c));
            }
            return s;
        }

        public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 6).Select(i => new object[] { i });

        // soups of two spheres with random flips and holes, plus a degenerate triangle, a -0 vertex and a non-manifold fin
        static DMesh3 Case(int seed)
        {
            var rand = new Random(seed);
            DMesh3 m = Sphere(5, Vector3d.Zero);
            MeshEditor.Append(m, Sphere(4, new Vector3d(5, 0, 0)));
            double pFlip = seed % 3 == 0 ? 0 : 0.15, pDrop = seed % 2 == 0 ? 0 : 0.03;
            var flip = new HashSet<int>(m.TriangleIndices().Where(t => rand.NextDouble() < pFlip));
            var drop = new HashSet<int>(m.TriangleIndices().Where(t => rand.NextDouble() < pDrop));
            DMesh3 s = Soup(m, flip.Contains, drop.Contains);
            if (seed >= 4) {
                // a vertex at -0.0 and one at +0.0, a degenerate triangle (two equal positions), a fin on a shared edge
                int a = s.AppendVertex(new Vector3d(-0.0, 7, 7)), b = s.AppendVertex(new Vector3d(0.0, 7, 7)), c = s.AppendVertex(new Vector3d(1, 7, 7));
                s.AppendTriangle(a, b, c);
                int t0 = s.TriangleIndices().First();
                Index3i f = s.GetTriangle(t0);
                int d = s.AppendVertex(new Vector3d(9, 9, 9));
                s.AppendTriangle(f.a, f.b, d);
            }
            return s;
        }

        [Theory]
        [MemberData(nameof(Seeds))]
        public void PrepareInput_SameAsDictionaryReference(int seed)
        {
            DMesh3 m = Case(seed);
            var p = ExactMeshBoolean.prepare_input(m);
            List<int> expected = RefToFlip(m);
            Assert.Equal(expected.Count, p.Reoriented);
            DMesh3 reference = m;
            if (expected.Count > 0) {
                reference = new DMesh3(m, false, MeshComponents.None);
                foreach (int t in expected) reference.ReverseTriOrientation(t);
            }
            Assert.Equal(expected.Count > 0, p.Input != m);
            foreach (int t in m.TriangleIndices())
                Assert.Equal(reference.GetTriangle(t), p.Input.GetTriangle(t));
            Assert.Equal(RefClosed(reference), p.Closed);
            Assert.Equal(RefClosed(m), ExactMeshBoolean.IsWindingClosed(m));
        }

        [Fact]
        public void PrepareInput_FlippedSoupOfSphere_BecomesClosedAndOriented()
        {
            DMesh3 m = Soup(Sphere(6, Vector3d.Zero), t => t % 5 == 0, t => false);
            var p = ExactMeshBoolean.prepare_input(m);
            Assert.True(p.Closed);
            Assert.True(p.Reoriented > 0);
            Assert.False(ExactMeshBoolean.IsWindingClosed(m));
            Assert.True(ExactMeshBoolean.IsWindingClosed(p.Input));
            Assert.True(Invariance.Volume(p.Input) > 0);
        }

        [Fact]
        public void PrepareInput_MoebiusStrip_IsLeftAsItIs()
        {
            // a strip of 6 quads closed with a twist cannot be oriented
            var m = new DMesh3();
            int n = 6;
            var top = new int[n]; var bot = new int[n];
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n;
                top[i] = m.AppendVertex(new Vector3d(Math.Cos(a) * 3, Math.Sin(a) * 3, 0.5 * Math.Sin(a / 2)));
                bot[i] = m.AppendVertex(new Vector3d(Math.Cos(a) * 3.5, Math.Sin(a) * 3.5, -0.5 * Math.Sin(a / 2)));
            }
            for (int i = 0; i + 1 < n; ++i) {
                m.AppendTriangle(top[i], bot[i], bot[i + 1]);
                m.AppendTriangle(top[i], bot[i + 1], top[i + 1]);
            }
            // the closing quad joins top[n-1] to bot[0] and bot[n-1] to top[0] (the twist)
            m.AppendTriangle(top[n - 1], bot[n - 1], top[0]);
            m.AppendTriangle(top[n - 1], top[0], bot[0]);
            var p = ExactMeshBoolean.prepare_input(m);
            List<int> expected = RefToFlip(m);
            Assert.Equal(expected.Count, p.Reoriented);
            Assert.False(p.Closed);
        }

        // ---- Compute ----

        [Fact]
        public void PrepareInput_EdgeWithThreeTriangles_IsNotUsedToOrient()
        {
            // three pages at the edge a-b, two of them running it the same way: not an edge of two triangles, so no
            // neighbour, no flip
            // (separate vertices at the same positions: a DMesh3 does not hold three triangles at an edge)
            var m = new DMesh3();
            Action<Vector3d, Vector3d, Vector3d> tri = (p, q, r) => m.AppendTriangle(m.AppendVertex(p), m.AppendVertex(q), m.AppendVertex(r));
            Vector3d a = new Vector3d(0, 0, 0), b = new Vector3d(1, 0, 0);
            tri(a, b, new Vector3d(0, 1, 0));
            tri(a, b, new Vector3d(0, 0, 1));
            tri(b, a, new Vector3d(0, -1, 0));
            Assert.Empty(RefToFlip(m));
            var p = ExactMeshBoolean.prepare_input(m);
            Assert.Equal(0, p.Reoriented);
            Assert.Same(m, p.Input);
        }

        [Fact]
        public void Compute_PreparedFirstOfAnotherMesh_IsNotUsed()
        {
            DMesh3 a = Soup(Sphere(5, Vector3d.Zero), t => t % 7 == 0, t => false);
            DMesh3 b = Sphere(5, new Vector3d(0.3, 0.2, 0.1));
            DMesh3 other = Soup(Sphere(5, Vector3d.Zero), t => false, t => false);
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true, PreparedFirst = ExactMeshBoolean.prepare_input(other) };
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(ExactMeshBoolean.prepare_input(a).Reoriented, op.ReorientedTriangles);
            Assert.True(op.ReorientedTriangles > 0);
        }


        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b)
        {
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { AllowOpenMeshes = true };
            Assert.True(op.Compute(), op.Error);
            return op;
        }

        [Fact]
        public void Compute_FlippedSoups_ReorientedCountIsSumOfTheMeshes_RepeatedRunsAndReuseEqual()
        {
            DMesh3 a = Soup(Sphere(6, Vector3d.Zero), t => t % 7 == 0, t => false);
            DMesh3 b = Soup(Sphere(6, new Vector3d(0.3, 0.2, 0.1)), t => t % 11 == 0, t => false);
            int expected = ExactMeshBoolean.prepare_input(a).Reoriented + ExactMeshBoolean.prepare_input(b).Reoriented;
            Assert.True(expected > 0);
            var first = Run(a, b);
            Assert.Equal(expected, first.ReorientedTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(first.Result));
            // repeated runs, and the same instance computed twice
            for (int i = 0; i < 3; ++i) {
                var again = Run(a, b);
                Assert.Equal("", Invariance.Difference(first.Result, again.Result));
                Assert.Equal(expected, again.ReorientedTriangles);
            }
            Assert.True(first.Compute());
            Assert.Equal(expected, first.ReorientedTriangles);
            DMesh3 r1 = new DMesh3(first.Result);
            Assert.True(first.Compute());
            Assert.Equal("", Invariance.Difference(r1, first.Result));
        }

        [Fact]
        public void Compute_SameMeshObjectTwice_PreparedInParallelWithoutInterference()
        {
            DMesh3 a = Soup(Sphere(6, Vector3d.Zero), t => t % 7 == 0, t => false);
            int one = ExactMeshBoolean.prepare_input(a).Reoriented;
            for (int i = 0; i < 5; ++i) {
                var op = new ExactMeshBoolean(a, a, ExactMeshBoolean.Operations.Union);
                Assert.True(op.Compute(), op.Error);
                Assert.Equal(2 * one, op.ReorientedTriangles);
            }
        }

        [Fact]
        public void Compute_NaryInputs_EachMeshPrepared()
        {
            var meshes = new List<DMesh3>();
            int expected = 0;
            for (int i = 0; i < 4; ++i) {
                DMesh3 m = Soup(Sphere(4, new Vector3d(0.4 * i, 0.1 * i, 0)), t => t % (3 + i) == 0, t => false);
                expected += ExactMeshBoolean.prepare_input(m).Reoriented;
                meshes.Add(m);
            }
            var op = new ExactMeshBoolean(meshes, w => w.Any(x => x > 0));
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(expected, op.ReorientedTriangles);
        }

        [Fact]
        public void PrepareInputs_SerialOnThisThread_RunsOnTheCallingThread_SameResultAsParallel()
        {
            DMesh3 a = Soup(Sphere(5, Vector3d.Zero), t => t % 7 == 0, t => false);
            DMesh3 b = Soup(Sphere(5, new Vector3d(0.3, 0.2, 0.1)), t => t % 5 == 0, t => false);
            var inputs = new List<DMesh3> { a, b, a, b };
            var threads = new System.Collections.Concurrent.ConcurrentBag<int>();
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { PrepareObserverForTests = (m) => threads.Add(Environment.CurrentManagedThreadId) };
            gParallel.SerialOnThisThread = true;
            ExactMeshBoolean.PreparedInput[] serial;
            try { serial = op.prepare_inputs(inputs); } finally { gParallel.SerialOnThisThread = false; }
            Assert.Equal(4, threads.Count);
            Assert.Equal(new[] { Environment.CurrentManagedThreadId }, threads.Distinct().ToArray());
            ExactMeshBoolean.PreparedInput[] parallel = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union).prepare_inputs(inputs);
            for (int i = 0; i < inputs.Count; ++i) {
                Assert.Equal(parallel[i].Reoriented, serial[i].Reoriented);
                Assert.Equal(parallel[i].Closed, serial[i].Closed);
                foreach (int t in inputs[i].TriangleIndices())
                    Assert.Equal(parallel[i].Input.GetTriangle(t), serial[i].Input.GetTriangle(t));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PrepareInputs_ExceptionOfAPreparation_IsRethrownAsItIs(bool serial)
        {
            // (a null input: Compute rejects it before, the preparation itself throws a NullReferenceException)
            DMesh3 a = Sphere(4, Vector3d.Zero);
            var op = new ExactMeshBoolean(a, a, ExactMeshBoolean.Operations.Union);
            gParallel.SerialOnThisThread = serial;
            try { Assert.Throws<NullReferenceException>(() => op.prepare_inputs(new List<DMesh3> { a, null })); }
            finally { gParallel.SerialOnThisThread = false; }
        }

        [Fact]
        public void Compute_NonFiniteSecondMesh_Fails()
        {
            DMesh3 a = Sphere(4, Vector3d.Zero);
            DMesh3 b = Sphere(4, Vector3d.Zero);
            b.SetVertex(3, new Vector3d(double.NaN, 0, 0));
            var op = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.False(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InvalidInput, op.Status);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PrepareInput_SameUnderSimilarity(string name)
        {
            // the same flipped components, closedness and reoriented count in any placement; under the scales 2^k the
            // reoriented mesh is the same bit for bit (after mapping back)
            Similarity s = Similarity.ByName(name);
            DMesh3 m = Soup(Sphere(6, Vector3d.Zero), t => t % 7 == 0, t => t % 53 == 0);
            DMesh3 x = Invariance.Transformed(m, s);
            var p0 = ExactMeshBoolean.prepare_input(m);
            var p = ExactMeshBoolean.prepare_input(x);
            Assert.Equal(p0.Closed, p.Closed);
            Assert.Equal(p0.Reoriented, p.Reoriented);
            if (s.IsExact)
                foreach (int t in m.TriangleIndices())
                    Assert.Equal(p0.Input.GetTriangle(t), p.Input.GetTriangle(t));
        }
    }
}
