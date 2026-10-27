using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Invariance tests for the core operations (docs/QA.md): the same input translated to UTM coordinates, scaled to
    /// millimetres or kilometres, rotated, or with its vertices and triangles permuted must give the same result
    /// (mapped back), compared with the result on the original, not with absolute bounds.
    /// </summary>
    public class InvarianceTests
    {
        static DMesh3 Box(Vector3d min, Vector3d max) =>
            new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();

        static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static void AssertRelative(double expected, double actual, double relTol, string what)
        {
            double err = Math.Abs(expected - actual) / Math.Max(Math.Abs(expected), 1e-300);
            Assert.True(err <= relTol, $"{what}: expected {expected:R}, got {actual:R} (relative error {err:E2})");
        }

        // ---- ExactMeshBoolean ----

        static DMesh3 ExactBoolean(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
        {
            var eb = new ExactMeshBoolean(a, b, op);
            Assert.True(eb.Compute(), eb.Error);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, eb.Status);
            return eb.Result;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NotCleanReason_InconsistentOrientation_SameUnderSimilarity(string name)
        {
            // a closed sphere with some triangles reversed: the same reason and the same count of inconsistently
            // oriented edges in any placement, and clean once the reversed triangles are flipped back
            Similarity s = Similarity.ByName(name);
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            var flipped = m.TriangleIndices().Where(t => t % 37 == 0).ToList();
            foreach (int t in flipped)
                m.ReverseTriOrientation(t);
            int count = MeshRepairMetrics.InconsistentlyOrientedEdges(m);
            Assert.True(count > 0);
            DMesh3 x = Invariance.Transformed(m, s);
            Assert.Equal("inconsistent orientation", MeshRobustRepair.NotCleanReason(x));
            Assert.Equal(count, MeshRepairMetrics.InconsistentlyOrientedEdges(x));
            foreach (int t in flipped)
                x.ReverseTriOrientation(t);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(x));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void ExactBoolean_Difference_SameVolumeUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 a = Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            DMesh3 b = Sphere(new Vector3d(0.7, 0.4, 0.3), 0.8, 8);
            DMesh3 r0 = ExactBoolean(a, b, ExactMeshBoolean.Operations.Difference);

            DMesh3 r = ExactBoolean(Invariance.Transformed(a, s), Invariance.Transformed(b, s), ExactMeshBoolean.Operations.Difference);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            DMesh3 back = Invariance.MappedBack(r, s);
            // exact arrangement, output rounded once: the result moves only by the rounding of its coordinates
            AssertRelative(Invariance.Volume(r0), Invariance.Volume(back), 1e-12 + 100 * s.Resolution(2), s.Name);
            double dist = Invariance.VertexDistance(r0, back);
            Assert.True(dist <= s.DistanceResolution(2), $"{s.Name}: results {dist} apart");
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(r0, back));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void ExactBoolean_Union_SameResultUnderPermutation(int seed)
        {
            DMesh3 a = Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            DMesh3 b = Sphere(new Vector3d(0.7, 0.4, 0.3), 0.8, 8);
            DMesh3 reference = ExactBoolean(a, b, ExactMeshBoolean.Operations.Union);
            DMesh3 r = ExactBoolean(Invariance.Permuted(a, seed), Invariance.Permuted(b, seed + 100), ExactMeshBoolean.Operations.Union);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.Equal(reference.TriangleCount, r.TriangleCount);
            AssertRelative(Invariance.Volume(reference), Invariance.Volume(r), 1e-12, "volume");
            Assert.True(Invariance.VertexDistance(reference, r) < 1e-12);
        }

        /// <summary>A box without its top face (two triangles): open, so the boolean uses generalized winding numbers.</summary>
        static DMesh3 OpenBox(Vector3d min, Vector3d max)
        {
            DMesh3 m = Box(min, max);
            foreach (int t in m.TriangleIndices().ToList())
                if (m.GetTriNormal(t).z > 0.9) m.RemoveTriangle(t);
            return m;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExactBoolean_ReusedInstance_EqualsFreshInstance(bool open)
        {
            // the first box is away from the second sphere, the second box contains most of it: a winding number
            // (or a tree for it) kept from the first call classifies the sphere as outside
            DMesh3 a = open ? OpenBox(new Vector3d(1, -1, -1), new Vector3d(3, 1, 1)) : Box(new Vector3d(1, -1, -1), new Vector3d(3, 1, 1));
            DMesh3 a2 = open ? OpenBox(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1)) : Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1));
            DMesh3 b = Sphere(new Vector3d(1.7, 0.4, 0.3), 0.8, 8);
            DMesh3 c = Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6);
            var eb = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference);
            Assert.True(eb.Compute());
            // new inputs and a new operation on the same instance
            eb.MeshA = a2;
            eb.MeshB = c;
            eb.Operation = ExactMeshBoolean.Operations.Intersection;
            Assert.True(eb.Compute());
            var fresh = new ExactMeshBoolean(a2, c, ExactMeshBoolean.Operations.Intersection);
            Assert.True(fresh.Compute());
            Assert.Equal(fresh.Status, eb.Status);
            Assert.True(fresh.Result.TriangleCount > 0);
            Assert.Equal("", Invariance.GeometricDifference(fresh.Result, eb.Result));
        }

        static DMesh3 BoxIntersectSphere() => ExactBoolean(Box(new Vector3d(-1.2, -1.1, -0.9), new Vector3d(0.3, 0.6, 1.1)),
            Sphere(new Vector3d(-0.6, -0.5, 0.2), 0.7, 6), ExactMeshBoolean.Operations.Intersection);

        [Fact]
        public void ExactBoolean_RepeatedRuns_SameTriangles()
        {
            DMesh3 first = BoxIntersectSphere();
            for (int k = 0; k < 8; ++k)
                Assert.Equal("", Invariance.GeometricDifference(first, BoxIntersectSphere()));
        }

        // The same triangles in the same order (ids), although the arrangement assigns point ids in parallel: output
        // files and downstream steps with ties are reproducible (see also ExactBooleanDeterminismTests).
        [Fact]
        public void ExactBoolean_RepeatedRuns_BitIdentical()
        {
            DMesh3 first = BoxIntersectSphere();
            for (int k = 0; k < 8; ++k)
                Assert.Equal("", Invariance.Difference(first, BoxIntersectSphere()));
        }

        // ---- MeshAlphaWrap ----

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void AlphaWrap_CleanAndSameVolumeUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 input = Box(new Vector3d(-1, -1, -0.5), new Vector3d(1, 1, 0.5));
            input.AppendTriangle(input.AppendVertex(new Vector3d(-1.5, 0, 0)), input.AppendVertex(new Vector3d(1.5, 0.1, 0)),
                input.AppendVertex(new Vector3d(0, 1.5, 0.2)));     // a stray triangle through the box
            var w0 = new MeshAlphaWrap(input) { RelativeAlpha = 15, RelativeOffset = 300 };
            Assert.True(w0.Compute(), w0.Error);
            double reference = Invariance.Volume(w0.Result);

            // the relative alpha and offset come from the axis-aligned bounds, which change under a rotation: then
            // the wrap gets the same absolute values
            var w = new MeshAlphaWrap(Invariance.Transformed(input, s)) { RelativeAlpha = 15, RelativeOffset = 300 };
            if (s.IsRotated) {
                w.Alpha = s.ApplyLength(w0.UsedAlpha);
                w.Offset = s.ApplyLength(w0.UsedOffset);
            }
            Assert.True(w.Compute(), w.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
            AssertRelative(w0.UsedAlpha, w.UsedAlpha / s.Scale, 1e-9, "alpha");
            AssertRelative(w0.UsedOffset, w.UsedOffset / s.Scale, 1e-9, "offset");
            DMesh3 back = Invariance.MappedBack(w.Result, s);
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(w0.Result, back));
            // the wrap surface lies at the offset around the input: a wrap that ignored or doubled the offset (about 1%
            // of the volume here) moves by an offset; rounding changes the Delaunay refinement only locally
            double dist = Invariance.VertexDistance(w0.Result, back);
            Assert.True(dist < 0.6 * w0.UsedAlpha, $"{s.Name}: wrap surfaces {dist} apart, alpha {w0.UsedAlpha}");
            // (a rotation changes the axis-aligned box the wrap starts from, and with it the refinement)
            AssertRelative(reference, Invariance.Volume(back), s.IsRotated ? 1e-2 : 2e-3, s.Name);
        }

        [Fact]
        public void AlphaWrap_RepeatedRuns_BitIdentical()
        {
            DMesh3 input = Sphere(Vector3d.Zero, 1, 10);
            var w0 = new MeshAlphaWrap(input) { RelativeAlpha = 10 };
            Assert.True(w0.Compute());
            for (int k = 0; k < 2; ++k) {
                var w = new MeshAlphaWrap(input) { RelativeAlpha = 10 };
                Assert.True(w.Compute());
                Assert.Equal("", Invariance.Difference(w0.Result, w.Result));
            }
        }

        // ---- MeshRobustRepair ----

        /// <summary>Two overlapping spheres with a hole and a flipped triangle.</summary>
        static DMesh3 Defective()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, 8);
            MeshEditor.Append(m, Sphere(new Vector3d(1.1, 0.2, 0.1), 0.9, 8));
            int t0 = m.TriangleIndices().First();
            m.RemoveTriangle(t0);
            int t1 = m.TriangleIndices().Skip(40).First();
            m.ReverseTriOrientation(t1);
            return m;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RobustRepair_CleanAndSameVolumeUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            var r0 = new MeshRobustRepair(Defective());
            Assert.True(r0.Apply());
            double reference = Invariance.Volume(r0.Mesh);

            var r = new MeshRobustRepair(Invariance.Transformed(Defective(), s));
            Assert.True(r.Apply(), r.ResultReason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
            Assert.Equal(r0.UsedSolidify, r.UsedSolidify);
            Assert.Equal(r0.UsedAlphaWrap, r.UsedAlphaWrap);
            Assert.Equal(r0.UsedExactResolve, r.UsedExactResolve);
            DMesh3 back = Invariance.MappedBack(r.Mesh, s);
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(r0.Mesh, back));
            AssertRelative(reference, Invariance.Volume(back), 1e-6 + 100 * s.Resolution(2), s.Name);
            double dist = Invariance.VertexDistance(r0.Mesh, back);
            Assert.True(dist <= 1e-6 + s.DistanceResolution(4), $"{s.Name}: repaired surfaces {dist} apart");
        }

        // ---- MeshWindingNumberSolidify (winding number octree with propagated node values) ----

        static DMesh3 Solidify(DMesh3 mesh)
        {
            var sol = new MeshWindingNumberSolidify(mesh) { GridResolution = 48, Adaptive = true };
            Assert.True(sol.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(sol.Result));
            return sol.Result;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Solidify_SameResultUnderSimilarity(string name)
        {
            // two overlapping closed spheres: closed and oriented, so node values are propagated (and rounded)
            Similarity s = Similarity.ByName(name);
            DMesh3 Input() { DMesh3 m = BumpySphere(); MeshEditor.Append(m, Sphere(new Vector3d(0.9, 0.2, 0.1), 0.7, 10)); return m; }
            DMesh3 m0 = Solidify(Input());
            DMesh3 back = Invariance.MappedBack(Solidify(Invariance.Transformed(Input(), s)), s);
            // bit for bit under 2^k scales; otherwise the grid lies differently on the shape: the same solid up to
            // about a cell (diagonal 3.4 / 48)
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            AssertRelative(Invariance.Volume(m0), Invariance.Volume(back), 0.01, s.Name);
            double dist = Invariance.VertexDistance(m0, back);
            Assert.True(dist < 0.1, $"{s.Name}: solidified surfaces {dist} apart");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WindingOctree_NodeValuesUnderSimilarity(string name)
        {
            // node values along lines through a closed mesh (propagated and rounded where far from the surface):
            // bit for bit under exact similarities; otherwise the grid moves relative to the shape, and the nodes
            // away from the surface still have exactly integer values
            Similarity s = Similarity.ByName(name);
            List<double> Values(DMesh3 m)
            {
                var o = new WindingNumberOctree(m, new DMeshAABBTree3(m, true)) { MaxDepth = 6, RegionF = (min, size) => false };
                o.Build();
                int n = o.LeafCount;
                var v = new List<double>();
                for (int i = 0; i < n; ++i)
                    for (int j = n / 4; j <= 3 * n / 4; j += n / 8)
                        v.Add(o.NodeValue(new Vector3i(i, j, n / 2)));
                return v;
            }
            List<double> v0 = Values(BumpySphere()), v1 = Values(Invariance.Transformed(BumpySphere(), s));
            Assert.Contains(v1, w => w == 1.0);
            Assert.All(v1, w => Assert.True(Math.Abs(w - 0.5) <= 0.3 || w == Math.Round(w), $"{s.Name}: {w:R}"));
            if (s.IsExact)
                Assert.Equal(v0, v1);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Solidify_UnusedFarVertex_IgnoredForTheGrid(bool adaptive)
        {
            // the grid is sized from the triangles: an unused vertex far away (CachedBounds includes it) used to
            // make the cells so large that Compute() returned true with an empty result
            DMesh3 Input(bool far)
            {
                DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
                if (far)
                    m.AppendVertex(new Vector3d(1e6, -2e6, 5e5));
                return m;
            }
            DMesh3 Run(DMesh3 m)
            {
                var sol = new MeshWindingNumberSolidify(m) { GridResolution = 32, Adaptive = adaptive };
                Assert.True(sol.Compute());
                return sol.Result;
            }
            DMesh3 withFar = Run(Input(true));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(withFar));
            Assert.Equal("", Invariance.GeometricDifference(Run(Input(false)), withFar));
        }

        [Fact]
        public void WindingBoolean_UnusedFarVertex_IgnoredForTheGrid()
        {
            DMesh3 Sphere1(bool far)
            {
                DMesh3 m = Sphere(Vector3d.Zero, 1, 8);
                if (far)
                    m.AppendVertex(new Vector3d(1e6, -2e6, 5e5));
                return m;
            }
            DMesh3 Run(bool far)
            {
                var b = new MeshWindingNumberBoolean(Sphere1(far), Sphere(new Vector3d(0.8, 0, 0), 0.7, 8), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
                Assert.True(b.Compute());
                return b.Result;
            }
            DMesh3 withFar = Run(true);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(withFar));
            Assert.Equal("", Invariance.GeometricDifference(Run(false), withFar));
        }

        [Fact]
        public void Solidify_ReusedInstance_EqualsFreshInstance()
        {
            DMesh3 a = BumpySphere(), b = Sphere(new Vector3d(0.3, 0, 0), 0.8, 12);
            var sol = new MeshWindingNumberSolidify(a) { GridResolution = 48, Adaptive = true };
            Assert.True(sol.Compute());
            sol.Mesh = b;
            Assert.True(sol.Compute());
            Assert.Equal("", Invariance.Difference(Solidify(b), sol.Result));
            sol.Mesh = a;
            Assert.True(sol.Compute());
            Assert.Equal("", Invariance.Difference(Solidify(a), sol.Result));
        }

        // ---- Reducer, Remesher ----

        /// <summary>A sphere with a smooth bump, so that the reducer has few ties between equal collapse costs.</summary>
        static DMesh3 BumpySphere()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, 16);
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.15 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5) + 0.05 * p.z));
            }
            return m;
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Reducer_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m0 = BumpySphere();
            new Reducer(m0).ReduceToTriangleCount(300);
            DMesh3 m = Invariance.Transformed(BumpySphere(), s);
            new Reducer(m).ReduceToTriangleCount(300);
            Assert.Equal(m0.TriangleCount, m.TriangleCount);
            Assert.True(Invariance.AllFinite(m));
            DMesh3 back = Invariance.MappedBack(m, s);
            if (s.IsExact) {
                // no absolute constants: the same collapses, the same positions
                Assert.Equal("", Invariance.Difference(m0, back));
            } else {
                // rounding can reorder collapses of almost equal cost; the approximation error stays the same
                double e0 = Invariance.VertexDistance(BumpySphere(), m0), e = Invariance.VertexDistance(BumpySphere(), back);
                Assert.True(Math.Abs(e - e0) < 0.05 * e0, $"{s.Name}: approximation error {e} vs {e0}");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Remesher_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 Remesh(DMesh3 mesh, double edge)
            {
                var r = new Remesher(mesh) { EnableParallelSmooth = false, EnableParallelProjection = false };
                r.SetTargetEdgeLength(edge);
                for (int k = 0; k < 5; ++k) r.BasicRemeshPass();
                Assert.True(Invariance.AllFinite(mesh));
                return mesh;
            }
            DMesh3 m0 = Remesh(BumpySphere(), 0.15);
            DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(BumpySphere(), s), s.ApplyLength(0.15)), s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            AssertRelative(m0.TriangleCount, back.TriangleCount, 0.03, s.Name);
            // both approximate the same surface: apart by about the sagitta of an edge, not by an edge
            double dist = Invariance.VertexDistance(m0, back);
            Assert.True(dist < 0.02, $"{s.Name}: remeshed surfaces {dist} apart");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RemesherPro_SameResultUnderSimilarity(string name)
        {
            // queued passes with projection (and face-aligned projection): bit for bit under 2^k scales (the
            // 'vertex did not move' and weight tests used absolute tolerances)
            Similarity s = Similarity.ByName(name);
            DMesh3 Remesh(DMesh3 mesh, DMesh3 target, double edge, bool faceAligned)
            {
                var r = new RemesherPro(mesh) { UseFaceAlignedProjection = faceAligned };
                r.SetProjectionTarget(MeshProjectionTarget.Auto(target));
                r.SetTargetEdgeLength(edge);
                r.FastestRemesh();
                Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh, false));
                return mesh;
            }
            foreach (bool faceAligned in new[] { false, true }) {
                DMesh3 m0 = Remesh(BumpySphere(), BumpySphere(), 0.1, faceAligned);
                DMesh3 back = Invariance.MappedBack(Remesh(Invariance.Transformed(BumpySphere(), s), Invariance.Transformed(BumpySphere(), s),
                    s.ApplyLength(0.1), faceAligned), s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(m0, back));
                AssertRelative(m0.TriangleCount, back.TriangleCount, 0.05, s.Name);
                double dist = Invariance.VertexDistance(m0, back);
                Assert.True(dist < 0.02, $"{s.Name}: remeshed surfaces {dist} apart");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void MergeCoincidentEdges_ShortCrack_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m0 = MeshTestUtil.CubeWithShortCrack(0.01, 0.0003);
            new MergeCoincidentEdges(m0) { MergeDistance = 0.001 }.Apply();
            Assert.True(m0.IsClosed());
            DMesh3 m = Invariance.Transformed(MeshTestUtil.CubeWithShortCrack(0.01, 0.0003), s);
            new MergeCoincidentEdges(m) { MergeDistance = s.ApplyLength(0.001) }.Apply();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal(m0.VertexCount, m.VertexCount);
            Assert.Equal(m0.TriangleCount, m.TriangleCount);
            DMesh3 back = Invariance.MappedBack(m, s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            double dist = Invariance.VertexDistance(m0, back);
            Assert.True(dist <= s.DistanceResolution(1), $"{s.Name}: results {dist} apart");
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void RemoveDuplicateTriangles_SmallDuplicates_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m0 = MeshTestUtil.DuplicatedSmallTriangle(0.01, 3, false);
            var d0 = new RemoveDuplicateTriangles(m0) { VertexTolerance = 0.001 };
            d0.Apply();
            Assert.Equal(2, d0.Removed);
            DMesh3 m = Invariance.Transformed(MeshTestUtil.DuplicatedSmallTriangle(0.01, 3, false), s);
            var d = new RemoveDuplicateTriangles(m) { VertexTolerance = s.ApplyLength(0.001) };
            d.Apply();
            Assert.Equal(d0.Removed, d.Removed);
            Assert.Equal(m0.TriangleCount, m.TriangleCount);
            Assert.Equal(m0.VertexCount, m.VertexCount);
            DMesh3 back = Invariance.MappedBack(m, s);
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(m0, back));
            double dist = Invariance.VertexDistance(m0, back);
            Assert.True(dist <= s.DistanceResolution(0.01), $"{s.Name}: results {dist} apart");
        }

        [Theory]
        [InlineData(-20)]
        [InlineData(20)]
        public void ReducerAndRemesher_TinyAndHugeScales_SameResult(int log2)
        {
            // far below and above the scales of Similarity.All(): the quality and normal-flip tests have no
            // absolute thresholds (a triangle quality of 0 below summed squared edges of 1e-8 and a normal
            // test that treated cross products below 2.2e-16 as zero used to change the decisions). Below about
            // 2^-25 the plain reducer still differs (another absolute constant, not in these tests).
            var s = new Similarity("scale 2^" + log2, Math.Pow(2, log2), Matrix3d.Identity, Vector3d.Zero);
            DMesh3 Reduce(DMesh3 m)
            {
                var r = new Reducer(m) { EdgeFlipTolerance = Math.Cos(60 * MathUtil.Deg2Rad), MinCollapseTriangleQuality = 0.05 };
                r.ReduceToTriangleCount(400);
                return m;
            }
            Assert.Equal("", Invariance.Difference(Reduce(BumpySphere()), Invariance.MappedBack(Reduce(Invariance.Transformed(BumpySphere(), s)), s)));
            DMesh3 Remesh(DMesh3 mesh, double edge)
            {
                var r = new Remesher(mesh) { EnableParallelSmooth = false, EnableParallelProjection = false, PreventNormalFlips = true };
                r.SetTargetEdgeLength(edge);
                for (int k = 0; k < 5; ++k) r.BasicRemeshPass();
                return mesh;
            }
            Assert.Equal("", Invariance.Difference(Remesh(BumpySphere(), 0.15),
                Invariance.MappedBack(Remesh(Invariance.Transformed(BumpySphere(), s), s.ApplyLength(0.15)), s)));
        }

        // ---- analysis ----

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WallThickness_ScalesWithTheInput(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 shell = Sphere(Vector3d.Zero, 1, 10);
            DMesh3 inner = Sphere(Vector3d.Zero, 0.8, 10);
            inner.ReverseOrientation();
            MeshEditor.Append(shell, inner);
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere }) {
                var w0 = new MeshWallThickness(shell) { Method = method };
                Assert.True(w0.Compute());
                var w = new MeshWallThickness(Invariance.Transformed(shell, s)) { Method = method };
                Assert.True(w.Compute());
                double tol = s.IsExact ? 0 : 1e-12 + 64 * s.Resolution(2);
                AssertRelative(w0.Statistics.Mean, w.Statistics.Mean / s.Scale, s.IsExact ? 1e-15 : 1e-12 + 64 * s.Resolution(2), method + " " + s.Name);
                foreach (int vid in shell.VertexIndices())
                    Assert.True(Math.Abs(w0.VertexThickness[vid] - w.VertexThickness[vid] / s.Scale) <= tol,
                        $"{method} {s.Name} vertex {vid}: {w0.VertexThickness[vid]} vs {w.VertexThickness[vid] / s.Scale}");
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Deviation_ScalesWithTheInput(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 mesh = Sphere(Vector3d.Zero, 1.05, 12);
            DMesh3 reference = Sphere(Vector3d.Zero, 1, 20);
            var d0 = new MeshDeviation(mesh, reference);
            Assert.True(d0.Compute());
            var d = new MeshDeviation(Invariance.Transformed(mesh, s), Invariance.Transformed(reference, s));
            Assert.True(d.Compute());
            double tol = s.IsExact ? 1e-15 : 1e-9 + 64 * s.Resolution(2);
            foreach (int vid in mesh.VertexIndices())
                Assert.True(Math.Abs(d0.VertexDeviation[vid] - d.VertexDeviation[vid] / s.Scale) < tol,
                    $"{s.Name} vertex {vid}: {d0.VertexDeviation[vid]} vs {d.VertexDeviation[vid] / s.Scale}");
        }

        // (on the bumpy sphere: the cube sphere has many exactly cocircular quads, where the intrinsic Delaunay flips
        // are ties that a perturbation of 1e-9 decides differently, changing the distances by about 1%, the
        // discretization error; away from such ties the distances follow a perturbation one to one)
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void GeodesicDistance_ScalesWithTheInput(string name) => GeodesicDistanceCase(name);

        static void GeodesicDistanceCase(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 mesh = BumpySphere();
            int src = mesh.VertexIndices().First();
            var g0 = new MeshGeodesicDistance(mesh);
            Assert.True(g0.Compute(new[] { src }));
            var g = new MeshGeodesicDistance(Invariance.Transformed(mesh, s));
            Assert.True(g.Compute(new[] { src }));
            // 2^k scales: the same bits (the heat source scales with the mesh, so its log does not round differently)
            double tol = s.IsExact ? 0 : 1e-9 + 64 * s.Resolution(2);
            foreach (int vid in mesh.VertexIndices())
                Assert.True(s.IsExact ? g0.Distance[vid] == g.Distance[vid] / s.Scale : Math.Abs(g0.Distance[vid] - g.Distance[vid] / s.Scale) < tol,
                    $"{s.Name} vertex {vid}: {g0.Distance[vid]} vs {g.Distance[vid] / s.Scale}");
        }
    }
}
