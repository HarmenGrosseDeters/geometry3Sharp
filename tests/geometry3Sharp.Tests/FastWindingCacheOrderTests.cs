using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The triangle lists the fast-winding cache of DMeshAABBTree3 sums over are Lists in tree order. The sums depend on the order
    /// at the last bits, so every box coefficient must equal, bit for bit, the one the former per-box HashSet build gave
    /// (the reference below is that build, copied).
    /// </summary>
    public class FastWindingCacheOrderTests
    {
        sealed class Reference : DMeshAABBTree3
        {
            public Reference(DMesh3 m) : base(m, true) { }

            public Dictionary<int, (Vector3d c, double r, Vector3d o1, Matrix3d o2)> Build()
            {
                var result = new Dictionary<int, (Vector3d, double, Vector3d, Matrix3d)>();
                var triCache = new MeshTriInfoCache(Mesh);
                Rec(root_index, 0, out _, triCache, result);
                return result;
            }

            void Collect(int iBox, HashSet<int> triangles)
            {
                int idx = box_to_index[iBox];
                if (idx < triangles_end) {
                    int n = index_list[idx];
                    for (int i = 1; i <= n; ++i) triangles.Add(index_list[idx + i]);
                } else {
                    int c1 = index_list[idx];
                    if (c1 < 0) Collect(-c1 - 1, triangles);
                    else { Collect(c1 - 1, triangles); Collect(index_list[idx + 1] - 1, triangles); }
                }
            }

            int Rec(int iBox, int depth, out HashSet<int> hash, MeshTriInfoCache triCache, Dictionary<int, (Vector3d, double, Vector3d, Matrix3d)> result)
            {
                hash = null;
                int idx = box_to_index[iBox];
                if (idx < triangles_end) return index_list[idx];
                int iChild1 = index_list[idx];
                if (iChild1 < 0) return Rec(-iChild1 - 1, depth + 1, out hash, triCache, result);
                iChild1 -= 1;
                int iChild2 = index_list[idx + 1] - 1;
                int n1 = Rec(iChild1, depth + 1, out hash, triCache, result);
                int n2 = Rec(iChild2, depth + 1, out HashSet<int> hash2, triCache, result);
                if (depth == 0) return n1 + n2;
                if (hash == null && hash2 != null) { Collect(iChild1, hash2); hash = hash2; }
                else {
                    if (hash == null) { hash = new HashSet<int>(); Collect(iChild1, hash); }
                    if (hash2 == null) Collect(iChild2, hash); else hash.UnionWith(hash2);
                }
                Vector3d c = Vector3d.Zero, o1 = Vector3d.Zero; Matrix3d o2 = Matrix3d.Zero; double r = 0;
                FastTriWinding.ComputeCoeffs(Mesh, hash, ref c, ref r, ref o1, ref o2, triCache);
                result[iBox] = (c, r, o1, o2);
                return n1 + n2;
            }

            public void EnsureCache() => FastWindingNumber(new Vector3d(5, 5, 5));
        }

        static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
        static bool Same(Vector3d a, Vector3d b) => Bits(a.x) == Bits(b.x) && Bits(a.y) == Bits(b.y) && Bits(a.z) == Bits(b.z);

        static DMesh3 Bumpy(int n, bool open, int seed)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            var rnd = new Random(seed);
            foreach (int v in m.VertexIndices().ToArray()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.15 * (rnd.NextDouble() - 0.5)) + new Vector3d(0.001, 0.002, 0.003) * rnd.NextDouble());
            }
            if (open)
                foreach (int t in m.TriangleIndices().ToArray())
                    if (m.GetTriCentroid(t).z > 0.5 || (t % 97 == 0)) m.RemoveTriangle(t, true, false);
            return m;
        }

        public static IEnumerable<object[]> Cases() => new[] { new object[] { 6, true, 1 }, new object[] { 20, false, 2 }, new object[] { 30, true, 3 } };

        [Theory, MemberData(nameof(Cases))]
        public void EveryBoxCoefficient_EqualsTheHashSetBuild_BitForBit(int n, bool open, int seed)
        {
            DMesh3 m = Bumpy(n, open, seed);
            var tree = new Reference(m);
            tree.EnsureCache();
            var expected = tree.Build();
            Assert.True(expected.Count > 10);
            int compared = 0;
            for (int b = 0; b < tree.BoxCountForTests; ++b) {
                bool has = tree.TryGetFastWindingCoefficients(b, out var c, out var r, out var o1, out var o2);
                if (!expected.TryGetValue(b, out var e)) { Assert.False(has, "box " + b); continue; }
                Assert.True(has, "box " + b);
                Assert.True(Same(c, e.c) && Bits(r) == Bits(e.r) && Same(o1, e.o1), "box " + b);
                for (int i = 0; i < 9; ++i) Assert.True(Bits(o2[i]) == Bits(e.o2[i]), $"box {b} order2[{i}]");
                ++compared;
            }
            Assert.Equal(expected.Count, compared);
        }

        [Fact]
        public void Cache_IsTheSameInSerialAndParallel_AndOnReuse()
        {
            DMesh3 m = Bumpy(30, true, 5);
            var a = new Reference(m); a.EnsureCache();
            Reference b = null;
            gParallel.SerialOnThisThread = true;
            try { b = new Reference(m); b.EnsureCache(); } finally { gParallel.SerialOnThisThread = false; }
            for (int box = 0; box < a.BoxCountForTests; ++box) {
                bool ha = a.TryGetFastWindingCoefficients(box, out var c1, out var r1, out var p1, out var q1);
                bool hb = b.TryGetFastWindingCoefficients(box, out var c2, out var r2, out var p2, out var q2);
                Assert.Equal(ha, hb);
                Assert.True(Same(c1, c2) && Bits(r1) == Bits(r2) && Same(p1, p2), "box " + box);
                for (int i = 0; i < 9; ++i) Assert.True(Bits(q1[i]) == Bits(q2[i]), "box " + box);
            }
            // a second evaluation on the same tree (cache reused) gives the same value
            var pt = new Vector3d(0.3, 0.2, 2);
            Assert.Equal(Bits(a.FastWindingNumber(pt)), Bits(a.FastWindingNumber(pt)));
        }
    }
}
