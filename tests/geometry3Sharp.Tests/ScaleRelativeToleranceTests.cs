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
    public class ScaleRelativeToleranceTests
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

        // ---- S13: MergeCoincidentEdges ----

        static bool CrackCloses(double gap, Similarity s, out DMesh3 mesh, out double effective)
        {
            mesh = Invariance.Transformed(MeshTestUtil.CubeWithShortCrack(0.01, gap), s);
            var merge = new MergeCoincidentEdges(mesh);       // the default distance
            merge.Apply();
            effective = merge.EffectiveMergeDistance;
            return mesh.IsClosed();
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void MergeCoincidentEdges_DefaultDistance_ClosesCrackFarBelowItAtEveryScale(string name)
        {
            // a crack of 3e-7 of the size (the old absolute 1e-6 only closed it at scale 1 and below)
            Similarity s = Of(name);
            Assert.True(CrackCloses(3e-7, s, out DMesh3 m, out double eff), name + ": not closed, distance " + eff);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal(12, m.VertexCount);
            Assert.Equal(20, m.TriangleCount);
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void MergeCoincidentEdges_DefaultDistance_KeepsWideCrackOpenAtEveryScale(string name)
        {
            // a crack of 1e-4 of the size is a distinct edge: the old absolute 1e-6 welded it below scale 1e-2
            Similarity s = Of(name);
            Assert.False(CrackCloses(1e-4, s, out DMesh3 m, out double eff), name + ": welded, distance " + eff);
            Assert.Equal(16, m.VertexCount);
            Assert.Equal(20, m.TriangleCount);
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void MergeCoincidentEdges_DefaultDistance_ScalesWithTheMesh(string name)
        {
            Similarity s = Of(name);
            CrackCloses(3e-7, Similarity.Identity, out DMesh3 m0, out double e0);
            CrackCloses(3e-7, s, out DMesh3 m, out double e);
            Assert.Equal(Math.Sqrt(3) * (double)MathUtil.ZeroTolerancef, e0, 1e-12);      // diagonal sqrt(3) of the unit cube
            if (s.IsExact)
                Assert.Equal(s.Scale * e0, e);
            else    // a rotation changes the axis-aligned bounds, a translation adds the rounding floor: bounded, not equal
                Assert.True(e >= 0.5 * s.Scale * e0, $"{name}: {e} against {s.Scale * e0}");
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, s)));
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

        // ---- S13: RemoveDuplicateTriangles ----

        // a unit triangle and a copy moved by shift (separate vertices)
        static DMesh3 TrianglePair(double shift, Similarity s)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(1, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)));
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, shift)), m.AppendVertex(new Vector3d(1, 0, shift)), m.AppendVertex(new Vector3d(0, 1, shift)));
            return Invariance.Transformed(m, s);
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void RemoveDuplicateTriangles_DefaultTolerance_RemovesNearDuplicateAtEveryScale(string name)
        {
            Similarity s = Of(name);
            DMesh3 m = TrianglePair(3e-7, s);
            var d = new RemoveDuplicateTriangles(m);
            d.Apply();
            Assert.Equal(1, d.Removed);
            Assert.Equal(1, m.TriangleCount);
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void RemoveDuplicateTriangles_DefaultTolerance_KeepsDistinctTrianglesAtEveryScale(string name)
        {
            // 1e-4 of the size apart: distinct (the old absolute 1e-6 deleted one of them below scale 1e-2)
            Similarity s = Of(name);
            DMesh3 m = TrianglePair(1e-4, s);
            var d = new RemoveDuplicateTriangles(m);
            d.Apply();
            Assert.Equal(0, d.Removed);
            Assert.Equal(2, m.TriangleCount);
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

        // ---- S11, S12: PlanarHoleFiller ----

        // an open cylinder (axis y, radius 1, 16 slices) under s, its top loop, and the plane of that loop
        static (DMesh3 mesh, EdgeLoop top, Vector3d origin, Vector3d normal) Cylinder(Similarity s)
        {
            DMesh3 m = Invariance.Transformed(new OpenCylinderGenerator() { Slices = 16, Height = 1 }.Generate().MakeDMesh(), s);
            var loops = new MeshBoundaryLoops(m);
            Vector3d topCentre = s.Apply(new Vector3d(0, 1, 0));
            EdgeLoop top = loops.Loops.First(l => m.GetVertex(l.Vertices[0]).Distance(topCentre) < s.Scale * 1.5 &&
                (m.GetVertex(l.Vertices[0]) - topCentre).Dot(s.ApplyDirection(Vector3d.AxisY)) > -0.5 * s.Scale * 0.01 &&
                Math.Abs((m.GetVertex(l.Vertices[0]) - topCentre).Dot(s.ApplyDirection(Vector3d.AxisY))) < 0.5 * s.Scale);
            return (m, top, topCentre, s.ApplyDirection(Vector3d.AxisY));
        }

        [Theory]
        [MemberData(nameof(AllAndExtreme))]
        public void PlanarHoleFiller_FillsLoopAtEveryScale(string name)
        {
            Similarity s = Of(name);
            var (m0, top0, o0, n0) = Cylinder(Similarity.Identity);
            var f0 = new PlanarHoleFiller(m0);
            f0.SetPlane(o0, n0);
            f0.AddFillLoop(top0);
            Assert.True(f0.Fill());

            var (m, top, o, n) = Cylinder(s);
            var fill = new PlanarHoleFiller(m);
            fill.SetPlane(o, n);
            fill.AddFillLoop(top);
            bool ok = fill.Fill();
            Assert.True(ok, $"{name}: fill failed, insertions {fill.FailedInsertions}, merges {fill.FailedMerges}");
            Assert.False(fill.OutputHasCracks);
            Assert.Equal(m0.VertexCount, m.VertexCount);
            Assert.Equal(m0.TriangleCount, m.TriangleCount);
            Assert.Equal(16, MeshTestUtil.BoundaryEdgeCount(m));   // only the bottom loop is left
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Theory]
        [InlineData(-24)]
        [InlineData(-20)]
        [InlineData(0)]
        public void PlanarSpansFiller_DegenerateTolerance_IsRelativeToTheSpans(int log2)
        {
            DMesh3 Run(Similarity s)
            {
                var (m, top, o, n) = Cylinder(s);
                int[] verts = top.Vertices;
                var span = EdgeSpan.FromVertices(m, verts.Take(verts.Length - 1).ToArray());
                var fill = new PlanarSpansFiller(m, new List<EdgeSpan> { span });
                fill.SetPlane(o, n);
                Assert.True(fill.Fill(), $"scale 2^{log2}");
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                return m;
            }
            DMesh3 m0 = Run(Similarity.Identity), m1 = Run(Pow2(log2));
            Assert.Equal(m0.VertexCount, m1.VertexCount);
            Assert.Equal(m0.TriangleCount, m1.TriangleCount);
        }

        [Fact]
        public void PlanarHoleFiller_SmallHoleFarFromTheOrigin_Merges()
        {
            // a hole of size 1e-2 at UTM coordinates: the merge distance 1e-8 is within ulps of the rounding there
            var s = new Similarity("small utm", 1e-2, Matrix3d.Identity, new Vector3d(632117.25, 5712043.5, 113.75));
            var (m, top, o, n) = Cylinder(s);
            var fill = new PlanarHoleFiller(m);
            fill.SetPlane(o, n);
            fill.AddFillLoop(top);
            Assert.True(fill.Fill(), $"insertions {fill.FailedInsertions}, merges {fill.FailedMerges}");
            Assert.Equal(16, MeshTestUtil.BoundaryEdgeCount(m));
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

        // ---- S16: RemoveOccludedTriangles ----

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
        public void RemoveOccludedTriangles_DefaultOffset_RemovesInnerShellAtEveryScale(string name)
        {
            Similarity s = Of(name);
            foreach (var mode in new[] { RemoveOccludedTriangles.CalculationMode.RayParity, RemoveOccludedTriangles.CalculationMode.FastWindingNumber }) {
                DMesh3 m = TwoShells(1e-4, s);
                int nOuter = m.TriangleCount / 2;
                var remove = new RemoveOccludedTriangles(m) { InsideMode = mode };
                Assert.True(remove.Apply());
                Assert.Equal(nOuter, remove.RemovedT.Count);
                Assert.True(remove.RemovedT.All(t => t >= nOuter), $"{name} {mode}: an outer triangle was removed");
            }
        }

        [Theory]
        [InlineData("scale 2^-24")]
        [InlineData("scale 2^36")]
        [InlineData("utm")]
        public void RemoveOccludedTriangles_DefaultOffset_PerVertexAndOtherModes(string name)
        {
            Similarity s = Of(name);
            foreach (var mode in new[] { RemoveOccludedTriangles.CalculationMode.FastWindingNumber, RemoveOccludedTriangles.CalculationMode.AnalyticWindingNumber }) {
                DMesh3 m = TwoShells(1e-4, s);
                int nOuter = m.TriangleCount / 2;
                var remove = new RemoveOccludedTriangles(m) { InsideMode = mode, PerVertex = true };
                Assert.True(remove.Apply());
                Assert.Equal(nOuter, remove.RemovedT.Count);
                Assert.True(remove.RemovedT.All(t => t >= nOuter), $"{name} {mode}");
            }
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
