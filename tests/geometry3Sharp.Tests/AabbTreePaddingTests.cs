using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Scale invariance of DMeshAABBTree3 and of the Remesher on top of it (Reducer / Remesher 2.0, T5). The boxes of
    /// the tree were padded by a fixed 50 * Epsilonf, which changed the order in which the boxes of a nearest-point
    /// query are visited when the mesh was scaled (by a power of two, which is exact), and with it which triangle
    /// won a tie, so the Remesher's projection moved a vertex by one bit. The padding is now proportional to the size
    /// of the mesh.
    /// </summary>
    public class AabbTreePaddingTests
    {
        const double Old = 50.0 * MathUtil.Epsilonf;

        static double MeanEdge(DMesh3 m) => m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        // open n x n heightfield on the unit square (grid lines: many ties between the two triangles of an edge)
        static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y) + 0.004 * Math.Cos(90 * y - 31 * x)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        static DMesh3 BumpySphere(int edgeVertices)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        static DMesh3 Remesh(DMesh3 input, double target, int passes)
        {
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { SmoothSpeedT = 0.5 };
            r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            for (int k = 0; k < passes; ++k)
                r.BasicRemeshPass();
            return m;
        }

        // meshes and targets where the fixed padding broke the bit-identity at 2^-10 (found by a sweep; terrain 40 at the
        // mean edge, terrain 60 at 0.7 of it), and a closed one
        public static System.Collections.Generic.IEnumerable<object[]> Cases()
        {
            foreach (string s in Similarity.All().Select(x => x.Name))
                foreach (var c in new[] { ("terrain40", 1.0), ("terrain60", 0.7), ("sphere16", 1.0) })
                    yield return new object[] { s, c.Item1, c.Item2 };
        }

        static DMesh3 Input(string name) =>
            name == "terrain40" ? Terrain(40) : name == "terrain60" ? Terrain(60) : BumpySphere(16);

        [Theory]
        [MemberData(nameof(Cases))]
        public void Remesher_ScaledByPowerOfTwo_GivesTheSameMeshBitForBit(string similarity, string meshName, double factor)
        {
            Similarity s = Similarity.ByName(similarity);
            DMesh3 input = Input(meshName);
            double target = factor * MeanEdge(input);
            DMesh3 expected = Remesh(input, target, 5);
            DMesh3 scaled = Remesh(Invariance.Transformed(input, s), s.ApplyLength(target), 5);
            DMesh3 back = Invariance.MappedBack(scaled, s);
            Assert.True(Invariance.AllFinite(back));
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(expected, back));
        }

        [Fact]
        public void Remesher_RepeatedRun_Reuse_IsDeterministic()
        {
            DMesh3 input = Terrain(40);
            double target = MeanEdge(input);
            Assert.Equal("", Invariance.Difference(Remesh(input, target, 3), Remesh(input, target, 3)));
        }

        [Fact]
        public void BoxPadding_OfAUnitMesh_IsTheEarlierConstant()
        {
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(new Vector3d(0.5, 0.5, 0.1), new Vector3d(0.5, 0.5, 0.1)));
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, new Vector3d(0.5, 0.25, 0.1)));
        }

        [Theory]
        [InlineData(-10)]
        [InlineData(-3)]
        [InlineData(7)]
        [InlineData(12)]
        public void BoxPadding_ScalesExactlyWithPowersOfTwo(int k)
        {
            Vector3d c = new Vector3d(0.3, -0.2, 0.7), e = new Vector3d(0.9, 0.4, 0.25);
            double f = Math.Pow(2, k);
            Assert.Equal(f * DMeshAABBTree3.ComputeBoxPadding(c, e), DMeshAABBTree3.ComputeBoxPadding(f * c, f * e));
        }

        [Fact]
        public void BoxPadding_FollowsTheSize_AndCoversRoundingFarFromTheOrigin()
        {
            // proportional to the size (twice the largest half extent)
            Assert.Equal(Old * 0.25, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, new Vector3d(0.125, 0.01, 0.01)));
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, new Vector3d(0.5, 0.01, 0.01)));
            Assert.Equal(Old * 3, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, new Vector3d(1.5, 0.01, 0.01)));
            // a small mesh in UTM coordinates: the rounding of the coordinates (about 1e-9 at 6e6) dominates the size term
            double far = DMeshAABBTree3.ComputeBoxPadding(new Vector3d(632117, 5712043, 113), new Vector3d(1e-4, 1e-4, 1e-4));
            Assert.True(far >= 5712043 * 8e-16 && far < 1e-6, far.ToString());
        }

        [Fact]
        public void BoxPadding_DegenerateAndNonFiniteRoots_FallBackToTheConstant()
        {
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, Vector3d.Zero));
            // a root of zero size away from the origin (all corners at one point): the rounding term alone
            Assert.Equal(5 * Math.Pow(2, -50), DMeshAABBTree3.ComputeBoxPadding(new Vector3d(5, 0, 0), Vector3d.Zero));
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(Vector3d.Zero, new Vector3d(double.PositiveInfinity, 1, 1)));
            Assert.Equal(Old, DMeshAABBTree3.ComputeBoxPadding(new Vector3d(double.NaN, 0, 0), new Vector3d(1, 1, 1)));
        }

        [Fact]
        public void NearestTriangle_OnAFarOffAndATinyMesh_StillFindsTheTriangleOfTheQueryPoint()
        {
            foreach (Similarity s in new[] { Similarity.Utm, Similarity.Pow2Small, Similarity.RotatedUtmMillimetre }) {
                DMesh3 m = Invariance.Transformed(Terrain(20), s);
                var tree = new DMeshAABBTree3(m, true);
                foreach (int t in m.TriangleIndices().Where(t => t % 7 == 0)) {
                    Vector3d c = m.GetTriCentroid(t);
                    Assert.Equal(t, tree.FindNearestTriangle(c));
                }
            }
        }
    }
}
