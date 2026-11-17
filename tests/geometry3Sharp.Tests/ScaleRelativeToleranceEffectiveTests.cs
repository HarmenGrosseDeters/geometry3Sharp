using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The default tolerances of MergeCoincidentEdges, RemoveDuplicateTriangles, RemoveOccludedTriangles and
    /// PlanarHoleFiller / PlanarSpansFiller are relative to the input (ROBUSTNESS_SWEEP.md S11, S12, S13, S16): the
    /// same decisions under every similarity, at scales far from 1, and an explicit (absolute) value keeps its meaning.
    /// </summary>
    // the tests that read the Effective* properties: a separate file, so that the tests of the other file compile against the base library
    public class ScaleRelativeToleranceEffectiveTests
    {
        // scales beyond Similarity.All(): where the old absolute 1e-6 / 1e-8 constants are the size of the input or
        // below the rounding of its coordinates
        public static IEnumerable<object[]> ExtremeScales()
        {
            yield return new object[] { -24 };
            yield return new object[] { -20 };
            yield return new object[] { 20 };
            yield return new object[] { 36 };
        }
        static Similarity Pow2(int log2) => new Similarity("scale 2^" + log2, Math.Pow(2, log2), Matrix3d.Identity, Vector3d.Zero);
        static Similarity Of(string name) => name.StartsWith("scale 2^")
            ? Pow2(int.Parse(name.Substring(8))) : Similarity.ByName(name);
        public static IEnumerable<object[]> AllAndExtreme() =>
            Similarity.Names().Concat(ExtremeScales().Select(a => new object[] { "scale 2^" + a[0] }));


        // a unit triangle and a copy moved by shift (separate vertices)
        static DMesh3 TrianglePair(double shift, Similarity s)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)));
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, shift)), m.AppendVertex(new Vector3d(1, 0, shift)), m.AppendVertex(new Vector3d(0, 1, shift)));
            return Invariance.Transformed(m, s);
        }
        // a unit sphere (outer shell) and a concentric shell of radius 1 - gap, both with outward normals
        static DMesh3 TwoShells(double gap, Similarity s)
        {
            DMesh3 outer = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            DMesh3 inner = new Sphere3Generator_NormalizedCube() { Radius = 1 - gap, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshEditor.Append(outer, inner);
            return Invariance.Transformed(outer, s);
        }
        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void MergeCoincidentEdges_DefaultDistance_ScalesWithTheMesh(string name)
        {
            Similarity s = Of(name);
            double Eff(Similarity sim)
            {
                var merge = new MergeCoincidentEdges(Invariance.Transformed(MeshTestUtil.CubeWithShortCrack(0.01, 3e-7), sim));
                merge.Apply();
                return merge.EffectiveMergeDistance;
            }
            double e0 = Eff(Similarity.Identity), e = Eff(s);
            Assert.Equal(Math.Sqrt(3) * (double)MathUtil.ZeroTolerancef, e0, 1e-12);      // diagonal sqrt(3) of the unit cube
            if (s.IsExact)
                Assert.Equal(s.Scale * e0, e);
            else    // a rotation changes the axis-aligned bounds, a translation adds the rounding floor: bounded, not equal
                Assert.True(e >= 0.5 * s.Scale * e0, $"{name}: {e} against {s.Scale * e0}");
        }

        [Fact]
        public void MergeCoincidentEdges_ExplicitDistance_KeepsItsAbsoluteMeaning()
        {
            DMesh3 m = MeshTestUtil.CubeWithShortCrack(0.01, 3e-7);
            var merge = new MergeCoincidentEdges(m) { MergeDistance = 1e-8 };
            merge.Apply();
            Assert.Equal(1e-8, merge.EffectiveMergeDistance);
            Assert.False(m.IsClosed());
            merge = new MergeCoincidentEdges(m) { MergeDistance = 1e-6 };
            merge.Apply();
            Assert.True(m.IsClosed());
        }

        [Fact]
        public void MergeCoincidentEdges_DefaultDistance_Reuse()
        {
            var m = MeshTestUtil.CubeWithShortCrack(0.01, 3e-7);
            var merge = new MergeCoincidentEdges(m);
            merge.Apply();
            double e = merge.EffectiveMergeDistance;
            Assert.True(m.IsClosed());
            Assert.True(merge.Apply());                 // second run on the closed mesh: nothing to do
            Assert.Equal(e, merge.EffectiveMergeDistance);
            Assert.Equal(20, m.TriangleCount);
        }

        [Fact]
        public void RemoveDuplicateTriangles_ExplicitTolerance_KeepsItsAbsoluteMeaning()
        {
            DMesh3 m = TrianglePair(3e-7, Similarity.Identity);
            var d = new RemoveDuplicateTriangles(m) { VertexTolerance = 1e-8 };
            d.Apply();
            Assert.Equal(1e-8, d.EffectiveVertexTolerance);
            Assert.Equal(0, d.Removed);
            d = new RemoveDuplicateTriangles(m) { VertexTolerance = 1e-6 };
            d.Apply();
            Assert.Equal(1, d.Removed);
        }

        [Fact]
        public void RemoveDuplicateTriangles_DefaultTolerance_ReuseAndEmptyMesh()
        {
            DMesh3 m = TrianglePair(3e-7, Similarity.Identity);
            var d = new RemoveDuplicateTriangles(m);
            d.Apply();
            double e = d.EffectiveVertexTolerance;
            Assert.Equal(1, d.Removed);
            d.Apply();
            Assert.Equal(0, d.Removed);
            Assert.Equal(e, d.EffectiveVertexTolerance, 1e-12);   // the bounds of one triangle of the pair
            Assert.True(new RemoveDuplicateTriangles(new DMesh3()).Apply());
            Assert.True(new MergeCoincidentEdges(new DMesh3()).Apply());
        }

        [Fact]
        public void DefaultTolerances_MeshAtOnePoint_FallBackToTheAbsoluteValue()
        {
            // diagonal 0: a tolerance of 0 would not match exact duplicates
            var m = new DMesh3();
            for (int k = 0; k < 2; ++k)
                m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(Vector3d.Zero), m.AppendVertex(Vector3d.Zero));
            var d = new RemoveDuplicateTriangles(m) { CheckOrientation = false };
            d.Apply();
            Assert.Equal((double)MathUtil.ZeroTolerancef, d.EffectiveVertexTolerance);
        }

        [Fact]
        public void RemoveOccludedTriangles_ExplicitOffset_KeepsItsAbsoluteMeaning_DefaultIsRelative()
        {
            DMesh3 m = TwoShells(1e-4, Similarity.Identity);
            var remove = new RemoveOccludedTriangles(m) { NormalOffset = 0.5 };      // pushes every test point out of both shells
            remove.Apply();
            Assert.Equal(0.5, remove.EffectiveNormalOffset);
            Assert.Empty(remove.RemovedT);

            var def = new RemoveOccludedTriangles(TwoShells(1e-4, Similarity.Identity));
            def.Apply();
            Assert.InRange(def.EffectiveNormalOffset, MathUtil.ZeroTolerance * 3.3, MathUtil.ZeroTolerance * 2 * Math.Sqrt(3));   // the diagonal of the sphere polyhedron inside [-1, 1]^3
            double e = def.EffectiveNormalOffset;
            def.Apply();                                                              // reuse: same offset, same result
            Assert.Equal(e, def.EffectiveNormalOffset);
            Assert.Empty(def.RemovedT);
        }
    }
}
