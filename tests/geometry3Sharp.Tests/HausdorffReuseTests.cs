using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1614: MeshHausdorffDistance stored the trees of its first Compute() in SpatialA / SpatialB and took them as the caller's
    /// next time, so a MeshA / MeshB that was replaced (or edited in place) was measured against the tree of the old mesh, with
    /// Compute() true and Converged. A tree is now used only while it belongs to the current mesh and is valid; a tree the class built
    /// itself is replaced, a tree the caller set stays the caller's.
    /// </summary>
    public class HausdorffReuseTests
    {
        static DMesh3 Box(Vector3d c, double h)
        {
            var g = new TrivialBox3Generator { Box = new Box3d(c, new Vector3d(h, h, h)), NoSharedVertices = false };
            g.Generate();
            return g.MakeDMesh();
        }

        static DMesh3 Sphere(double radius, Vector3d offset)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, radius * m.GetVertex(v) + offset);
            return m;
        }

        static void AssertSame(MeshHausdorffDistance expected, MeshHausdorffDistance actual, string what)
        {
            Assert.True(actual.Converged, what);
            Assert.Equal(expected.LowerBound, actual.LowerBound);
            Assert.Equal(expected.UpperBound, actual.UpperBound);
            Assert.Equal(expected.Subdivisions, actual.Subdivisions);
        }

        static MeshHausdorffDistance Fresh(DMesh3 a, DMesh3 b, bool symmetric)
        {
            var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
            Assert.True(h.Compute());
            return h;
        }

        /// <summary> the repro of #1614: after MeshA or MeshB is replaced, a reused instance equals a fresh one, bit for bit </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReplacedMeshes_GiveTheResultOfAFreshInstance(bool symmetric)
        {
            DMesh3 a1 = Box(new Vector3d(1, 1, 1), 1.0), a2 = Box(new Vector3d(5, 1, 1), 0.5);
            DMesh3 b1 = Box(new Vector3d(1, 1, 1), 1.1), b2 = Box(new Vector3d(9, 9, 9), 1.0);
            var h = new MeshHausdorffDistance(a1, b1) { Symmetric = symmetric };
            Assert.True(h.Compute());
            h.MeshA = a2;
            Assert.True(h.Compute());
            AssertSame(Fresh(a2, b1, symmetric), h, "MeshA replaced");
            h.MeshA = a1; h.MeshB = b2;
            Assert.True(h.Compute());
            AssertSame(Fresh(a1, b2, symmetric), h, "MeshA and MeshB replaced");
            h.MeshB = b1;
            Assert.True(h.Compute());
            AssertSame(Fresh(a1, b1, symmetric), h, "MeshB replaced");
        }

        /// <summary> a mesh edited in place after the first Compute(): the tree of the old shape is not used (it would throw or answer for the old shape) </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EditedInPlace_GivesTheResultOfAFreshInstance(bool symmetric)
        {
            DMesh3 a = Box(new Vector3d(1, 1, 1), 1.0), b = Box(new Vector3d(1, 1, 1), 1.1);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
            Assert.True(h.Compute());
            foreach (int v in a.VertexIndices().ToList()) a.SetVertex(v, a.GetVertex(v) + new Vector3d(3, 0, 0));
            foreach (int v in b.VertexIndices().ToList()) b.SetVertex(v, 0.5 * b.GetVertex(v));
            Assert.True(h.Compute());
            AssertSame(Fresh(new DMesh3(a), new DMesh3(b), symmetric), h, "edited in place");
        }

        /// <summary> the same meshes twice: the same bits, and the trees of the first call are kept (no rebuild) </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SameMeshes_ReuseTheTrees_BitForBit(bool symmetric)
        {
            DMesh3 a = Sphere(1, Vector3d.Zero), b = Sphere(1.01, Vector3d.Zero);
            var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
            Assert.True(h.Compute());
            double lo = h.LowerBound, up = h.UpperBound; int subs = h.Subdivisions;
            DMeshAABBTree3 ta = h.SpatialA, tb = h.SpatialB;
            Assert.NotNull(tb);
            Assert.True(h.Compute());
            Assert.Equal(lo, h.LowerBound); Assert.Equal(up, h.UpperBound); Assert.Equal(subs, h.Subdivisions);
            Assert.Same(ta, h.SpatialA); Assert.Same(tb, h.SpatialB);
        }

        /// <summary> a tree the class built is replaced when its mesh is replaced; a valid tree of the caller is used and kept; a tree of another mesh is ignored and kept </summary>
        [Fact]
        public void TreesOfTheCaller_StayTheCallers()
        {
            DMesh3 a = Box(new Vector3d(1, 1, 1), 1.0), b = Box(new Vector3d(1, 1, 1), 1.1), other = Box(new Vector3d(9, 9, 9), 1.0);
            var own = new MeshHausdorffDistance(a, b) { Symmetric = true };
            Assert.True(own.Compute());
            DMeshAABBTree3 first = own.SpatialB;
            own.MeshB = other;
            Assert.True(own.Compute());
            Assert.NotSame(first, own.SpatialB);
            Assert.Same(other, own.SpatialB.Mesh);

            var tb = new DMeshAABBTree3(b, true);
            var given = new MeshHausdorffDistance(a, b) { Symmetric = false, SpatialB = tb };
            Assert.True(given.Compute());
            Assert.Same(tb, given.SpatialB);
            // the mesh is replaced: the caller's tree does not belong to it any more, it is ignored and stays the caller's
            given.MeshB = other;
            Assert.True(given.Compute());
            AssertSame(Fresh(a, other, false), given, "the caller's tree of the replaced mesh is ignored");
            Assert.Same(tb, given.SpatialB);
            given.MeshB = b;
            DMeshAABBTree3 wrong = new DMeshAABBTree3(other, true);
            var mismatch = new MeshHausdorffDistance(a, b) { Symmetric = false, SpatialB = wrong };
            Assert.True(mismatch.Compute());
            AssertSame(Fresh(a, b, false), mismatch, "a tree of another mesh is ignored");
            Assert.Same(wrong, mismatch.SpatialB);
            // (the same for the tree of A, which only a symmetric run uses)
            var wrongA = new DMeshAABBTree3(other, true);
            var mismatchA = new MeshHausdorffDistance(a, b) { Symmetric = true, SpatialA = wrongA };
            Assert.True(mismatchA.Compute());
            AssertSame(Fresh(a, b, true), mismatchA, "a tree of another mesh is ignored (A)");
            Assert.Same(wrongA, mismatchA.SpatialA);
        }

        /// <summary> the roles swapped on one instance, the same object as both meshes, and a caller's tree that went stale by an edit in place: each equals a fresh instance, bit for bit </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SwappedMeshes_SameObject_StaleCallerTree_EqualAFreshInstance(bool symmetric)
        {
            DMesh3 a = Box(new Vector3d(1, 1, 1), 1.0), b = Sphere(1.3, new Vector3d(1, 1.2, 1));
            var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
            Assert.True(h.Compute());
            h.MeshA = b; h.MeshB = a;                       // swapped: the tree built for B is now the tree for the other role
            Assert.True(h.Compute());
            AssertSame(Fresh(b, a, symmetric), h, "swapped");
            h.MeshA = a; h.MeshB = a;                       // the same object in both roles
            Assert.True(h.Compute());
            AssertSame(Fresh(a, a, symmetric), h, "MeshA = MeshB");
            Assert.Equal(0.0, h.LowerBound);
            h.MeshA = b; h.MeshB = a;
            Assert.True(h.Compute());
            AssertSame(Fresh(b, a, symmetric), h, "swapped again");

            // a caller's tree, valid at the first call, stale after an edit in place: ignored, the result is that of a fresh instance
            DMesh3 c = new DMesh3(a), d = new DMesh3(b);
            var tc = new DMeshAABBTree3(d, true);
            var given = new MeshHausdorffDistance(c, d) { Symmetric = symmetric, SpatialB = tc };
            Assert.True(given.Compute());
            foreach (int v in d.VertexIndices().ToList()) d.SetVertex(v, d.GetVertex(v) + new Vector3d(0.5, 0, 0));
            Assert.False(tc.IsValid);
            Assert.True(given.Compute());
            AssertSame(Fresh(new DMesh3(c), new DMesh3(d), symmetric), given, "the caller's tree is stale after an edit in place");
            Assert.Same(tc, given.SpatialB);
        }

        /// <summary> under every similarity: an instance that measured the untransformed pair, then the transformed one, equals a fresh instance on the transformed pair </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReuseAcrossMeshes_UnderEverySimilarity(bool symmetric)
        {
            DMesh3 a = Sphere(1, Vector3d.Zero), b = Sphere(1.01, new Vector3d(0.02, 0, 0));
            foreach (Similarity s in Similarity.All()) {
                DMesh3 ta = Invariance.Transformed(a, s), tb = Invariance.Transformed(b, s);
                var h = new MeshHausdorffDistance(a, b) { Symmetric = symmetric };
                Assert.True(h.Compute());
                h.MeshA = ta; h.MeshB = tb;
                Assert.True(h.Compute());
                AssertSame(Fresh(ta, tb, symmetric), h, s.Name);
            }
        }
    }
}
