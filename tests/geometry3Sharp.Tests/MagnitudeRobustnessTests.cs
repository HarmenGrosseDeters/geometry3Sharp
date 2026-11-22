using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The geodesic and intrinsic classes at every power of two (#1352, #1377; docs/research/MAGNITUDE_ROBUSTNESS_2026-11.md,
    /// option (a)): the lengths are processed times 2^-k with k from the longest edge, so a mesh scaled by 2^e gives the result
    /// of the unit mesh times 2^e bit for bit, from 2^-500 to 2^500 (before: false successes and NaN beyond about 2^+-255).
    /// </summary>
    public class MagnitudeRobustnessTests
    {
        public static readonly int[] Exponents = { -500, -499, -250, -100, -37, -1, 1, 37, 100, 250, 499, 500 };

        public static IEnumerable<object[]> ExponentData() => Exponents.Select(e => new object[] { e });

        static double Sc(double x, int e) => ExactPoint3.ScaleB(x, e);
        static Vector3d Sc(Vector3d p, int e) => new Vector3d(Sc(p.x, e), Sc(p.y, e), Sc(p.z, e));

        /// <summary>The mesh times 2^e (a power of two: exact).</summary>
        public static DMesh3 Scaled(DMesh3 mesh, int e)
        {
            var c = new DMesh3(mesh);
            foreach (int v in c.VertexIndices()) c.SetVertex(v, Sc(mesh.GetVertex(v), e));
            return c;
        }

        // a bumpy sphere away from the origin: no cocircular quads, edges of different length
        static DMesh3 Sphere(int n = 5)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, new Vector3d(0.1, 0.2, 0.3) + p * (1 + 0.1 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5)));
            }
            return m;
        }

        static DMesh3 ShearedGrid(int n = 3)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(new Vector3d(i + 0.375 * j, j, 0));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
            }
            return m;
        }

        static void SameBits(double want, double got, string what)
        {
            Assert.True(BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(got) || (want == 0 && got == 0),
                $"{what}: {want:R} vs {got:R}");
        }

        static void SameBitsScaled(double unit, double got, int e, string what) => SameBits(Sc(unit, e), got, what);

        // ---------------------------------------------------------------------------------------------- IntrinsicTriangulation

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void IntrinsicTriangulation_FlipsLengthsAndAreas_ScaleExactly(int e)
        {
            DMesh3 mesh = Sphere();
            var T0 = new IntrinsicTriangulation(mesh);
            int flips0 = T0.FlipToDelaunay();
            var T = new IntrinsicTriangulation(Scaled(mesh, e));
            Assert.Equal(flips0, T.FlipToDelaunay());
            Assert.Equal(T0.HalfedgeCount, T.HalfedgeCount);
            for (int h = 0; h < T0.HalfedgeCount; ++h) {
                Assert.Equal(T0.Twin(h), T.Twin(h));
                SameBitsScaled(T0.Length(h), T.Length(h), e, "length " + h);
                SameBits(T0.Signpost(h), T.Signpost(h), "signpost " + h);
                SameBits(T0.CotanWeight(h), T.CotanWeight(h), "cotan " + h);
            }
            for (int t = 0; t < T0.TriangleCount; ++t) {
                SameBitsScaled(T0.TriangleArea(t), T.TriangleArea(t), 2 * e, "area " + t);
                T0.GetTriangleLayout(t, out Vector2d a0, out Vector2d b0, out Vector2d c0);
                T.GetTriangleLayout(t, out Vector2d a, out Vector2d b, out Vector2d c);
                SameBitsScaled(b0.x, b.x, e, "layout"); SameBitsScaled(c0.x, c.x, e, "layout"); SameBitsScaled(c0.y, c.y, e, "layout");
            }
            SameBitsScaled(T0.MeanEdgeLength, T.MeanEdgeLength, e, "mean edge length");
        }

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void IntrinsicTriangulation_DelaunayRefine_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere(4);
            var T0 = new IntrinsicTriangulation(mesh);
            var r0 = T0.DelaunayRefine(25);
            var T = new IntrinsicTriangulation(Scaled(mesh, e));
            var r = T.DelaunayRefine(25);
            Assert.Equal(r0.Status, r.Status);
            Assert.Equal(r0.InsertedVertices, r.InsertedVertices);
            Assert.Equal(r0.Flips, r.Flips);
            SameBits(r0.MinAngle, r.MinAngle, "min angle");
            Assert.Equal(T0.VertexCount, T.VertexCount);
            for (int i = 0; i < T0.VertexCount; ++i) {
                Vector3d p0 = T0.VertexPosition(i), p = T.VertexPosition(i);
                SameBitsScaled(p0.x, p.x, e, "x"); SameBitsScaled(p0.y, p.y, e, "y"); SameBitsScaled(p0.z, p.z, e, "z");
            }
        }

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void ExtractCommonSubdivision_ScalesExactly_AtEveryScale(int e)
        {
            DMesh3 mesh = ShearedGrid();
            var T0 = new IntrinsicTriangulation(mesh);
            T0.FlipToDelaunay();
            var cs0 = T0.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs0.Status);
            var T = new IntrinsicTriangulation(Scaled(mesh, e));
            T.FlipToDelaunay();
            var cs = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, cs.Status);
            Assert.Equal(cs0.Points.Count, cs.Points.Count);
            Assert.Equal(cs0.Faces.Count, cs.Faces.Count);
            for (int i = 0; i < cs.Points.Count; ++i) {
                SameBitsScaled(cs0.Points[i].x, cs.Points[i].x, e, "x"); SameBitsScaled(cs0.Points[i].y, cs.Points[i].y, e, "y");
                SameBitsScaled(cs0.Points[i].z, cs.Points[i].z, e, "z");
            }
            for (int f = 0; f < cs.Faces.Count; ++f) SameBitsScaled(cs0.Faces[f].Area, cs.Faces[f].Area, 2 * e, "face area");
        }

        [Fact]
        public void IntrinsicTriangulation_ReuseTwice_GivesTheSameResult()
        {
            var T = new IntrinsicTriangulation(Scaled(ShearedGrid(), 300));
            T.FlipToDelaunay();
            var a = T.ExtractCommonSubdivision();
            var b = T.ExtractCommonSubdivision();
            Assert.Equal(CommonSubdivisionStatus.Success, a.Status);
            Assert.Equal(a.Points.Count, b.Points.Count);
            for (int i = 0; i < a.Points.Count; ++i) Assert.Equal(a.Points[i], b.Points[i]);
        }

        [Fact]
        public void IntrinsicTriangulation_UnitScaleExponent_FollowsTheLongestEdge()
        {
            DMesh3 mesh = ShearedGrid();
            Assert.Equal(IntrinsicTriangulation.EdgeExponent(mesh) + 37, IntrinsicTriangulation.EdgeExponent(Scaled(mesh, 37)));
            Assert.Equal(new IntrinsicTriangulation(mesh).ScaleExponent + 37, new IntrinsicTriangulation(Scaled(mesh, 37)).ScaleExponent);
        }

        [Fact]
        public void IntrinsicTriangulation_LengthDisparity_IsDetected()
        {
            // edges 2^-260 of the longest one: the fourth powers of their lengths leave the double range
            Assert.False(new IntrinsicTriangulation(ShearedGrid()).HasLengthDisparity);
            Assert.True(new IntrinsicTriangulation(HalfSliver()).HasLengthDisparity);
        }

        // two triangles sharing an edge whose other edges are 2^-260 long
        static DMesh3 HalfSliver()
        {
            var m = new DMesh3();
            double s = Math.Pow(2, -260);
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, s, 0)); m.AppendVertex(new Vector3d(1, -s, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 3, 1);
            return m;
        }

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void IntrinsicTriangulation_TangentBasisAndToWorld_AreScaleInvariant(int e)
        {
            // The normal of the tangent frame weights the input corners by their angles; before #1352 the angle came from the dot
            // product of the unnormalized layout edges, so the basis depended on the scale of the mesh (and was not the angle at all).
            DMesh3 mesh = Sphere(4);
            var T0 = new IntrinsicTriangulation(mesh);
            var T = new IntrinsicTriangulation(Scaled(mesh, e));
            foreach (int vid in mesh.VertexIndices()) {
                T0.GetTangentBasis(vid, out Vector3d a1, out Vector3d a2, out Vector3d an);
                T.GetTangentBasis(vid, out Vector3d b1, out Vector3d b2, out Vector3d bn);
                for (int k = 0; k < 3; ++k) { SameBits(a1[k], b1[k], "e1"); SameBits(a2[k], b2[k], "e2"); SameBits(an[k], bn[k], "n"); }
                Vector3d w0 = T0.ToWorld(vid, new Vector2d(0.3, 0.7)), w = T.ToWorld(vid, new Vector2d(0.3, 0.7));
                for (int k = 0; k < 3; ++k) SameBits(w0[k], w[k], "ToWorld");
            }
        }

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void MeshGeodesicPath_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere();
            int target = mesh.MaxVertexID - 3;
            var f0 = new MeshGeodesicPath(mesh);
            Assert.Equal(GeodesicPathStatus.Success, f0.Compute(0, target));
            var f = new MeshGeodesicPath(Scaled(mesh, e));
            Assert.Equal(GeodesicPathStatus.Success, f.Compute(0, target));
            Assert.Equal(f0.Flips, f.Flips);
            SameBitsScaled(f0.Length, f.Length, e, "length");
            Assert.Equal(f0.Path.Count, f.Path.Count);
            for (int i = 0; i < f.Path.Count; ++i) { SameBitsScaled(f0.Path[i].x, f.Path[i].x, e, "x"); SameBitsScaled(f0.Path[i].z, f.Path[i].z, e, "z"); }
        }

        // ---------------------------------------------------------------------------------------------- MeshGeodesicDistance

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void MeshGeodesicDistance_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere();
            foreach (bool delaunay in new[] { true, false }) {
                var g0 = new MeshGeodesicDistance(mesh) { IntrinsicDelaunay = delaunay };
                Assert.True(g0.Compute(new[] { 0 }));
                var g = new MeshGeodesicDistance(Scaled(mesh, e)) { IntrinsicDelaunay = delaunay };
                Assert.True(g.Compute(new[] { 0 }), $"{e}, delaunay {delaunay}");
                foreach (int v in mesh.VertexIndices()) SameBitsScaled(g0.Distance[v], g.Distance[v], e, $"distance {v}, delaunay {delaunay}");
            }
        }

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void MeshGeodesicDistance_TracePath_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere();
            int target = mesh.MaxVertexID - 3;
            var g0 = new MeshGeodesicDistance(mesh);
            Assert.True(g0.Compute(new[] { 0 }));
            var p0 = g0.TracePath(target);
            var g = new MeshGeodesicDistance(Scaled(mesh, e));
            Assert.True(g.Compute(new[] { 0 }));
            var p = g.TracePath(target);
            Assert.True(p0.Count > 2);
            Assert.Equal(p0.Count, p.Count);
            for (int i = 0; i < p.Count; ++i) {
                SameBitsScaled(p0[i].x, p[i].x, e, "x"); SameBitsScaled(p0[i].y, p[i].y, e, "y"); SameBitsScaled(p0[i].z, p[i].z, e, "z");
            }
            // from a point beside the surface
            Vector3d q = mesh.GetVertex(target) + new Vector3d(0.01, 0.02, 0.03);
            var pq0 = g0.TracePath(q);
            var pq = g.TracePath(Sc(q, e));
            Assert.Equal(pq0.Count, pq.Count);
            for (int i = 0; i < pq.Count; ++i) SameBitsScaled(pq0[i].x, pq[i].x, e, "x of the path from a point");
        }

        [Fact]
        public void MeshGeodesicDistance_Reuse_ComputeTwice()
        {
            var g = new MeshGeodesicDistance(Scaled(Sphere(), 300));
            Assert.True(g.Compute(new[] { 0 }));
            double[] first = (double[])g.Distance.Clone();
            Assert.True(g.Compute(new[] { 0 }));
            for (int i = 0; i < first.Length; ++i) SameBits(first[i], g.Distance[i], "distance " + i);
            Assert.True(g.Compute(new[] { 5 }));
            Assert.True(g.Compute(new[] { 0 }));
            for (int i = 0; i < first.Length; ++i) SameBits(first[i], g.Distance[i], "distance after another source " + i);
        }

        [Fact]
        public void MeshGeodesicDistance_LengthDisparity_Refuses()
        {
            var g = new MeshGeodesicDistance(HalfSliver());
            Assert.False(g.Compute(new[] { 0 }));
            Assert.Null(g.Distance);
        }

        [Fact]
        public void MeshGeodesicDistance_BeyondTheScale_NeverReturnsNaNAsSuccess()
        {
            // 2^-1060 .. 2^1020: subnormal coordinates lose bits there, so the answer may be a refusal, never NaN distances
            foreach (int e in new[] { -1060, -1040, 1000, 1020 }) {
                DMesh3 mesh = Scaled(Sphere(4), e);
                var g = new MeshGeodesicDistance(mesh);
                bool ok = g.Compute(new[] { 0 });
                if (ok) foreach (int v in mesh.VertexIndices()) Assert.False(double.IsNaN(g.Distance[v]), $"e {e}, vertex {v}");
            }
        }

        // ---------------------------------------------------------------------------------------------- PolyhedralGeodesicDistance

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void PolyhedralGeodesicDistance_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere(4);
            var g0 = new PolyhedralGeodesicDistance(mesh);
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g0.Compute(new[] { 0 }));
            var g = new PolyhedralGeodesicDistance(Scaled(mesh, e));
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 0 }));
            foreach (int v in mesh.VertexIndices()) SameBitsScaled(g0.Distance[v], g.Distance[v], e, "distance " + v);
            int target = mesh.MaxVertexID - 2;
            var p0 = g0.TracePath(target);
            var p = g.TracePath(target);
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, p0.Status);
            Assert.Equal(PolyhedralGeodesicPathStatus.Ok, p.Status);
            Assert.Equal(p0.Points.Count, p.Points.Count);
            SameBitsScaled(p0.Length, p.Length, e, "path length");
            SameBitsScaled(p0.Distance, p.Distance, e, "path distance");
            for (int i = 0; i < p.Points.Count; ++i) SameBitsScaled(p0.Points[i].Position.x, p.Points[i].Position.x, e, "path x");
        }

        [Fact]
        public void PolyhedralGeodesicDistance_MaxDistance_IsInTheUnitsOfTheMesh()
        {
            DMesh3 mesh = Sphere(4);
            var g0 = new PolyhedralGeodesicDistance(mesh) { MaxDistance = 1.0 };
            g0.Compute(new[] { 0 });
            var g = new PolyhedralGeodesicDistance(Scaled(mesh, 300)) { MaxDistance = Sc(1.0, 300) };
            g.Compute(new[] { 0 });
            Assert.Equal(g0.Status, g.Status);
            foreach (int v in mesh.VertexIndices()) {
                Assert.Equal(double.IsNaN(g0.Distance[v]), double.IsNaN(g.Distance[v]));
                if (!double.IsNaN(g0.Distance[v])) SameBitsScaled(g0.Distance[v], g.Distance[v], 300, "distance " + v);
            }
        }

        [Fact]
        public void PolyhedralGeodesicDistance_Reuse_AndDisparity()
        {
            var g = new PolyhedralGeodesicDistance(Scaled(Sphere(4), 300));
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 0 }));
            double[] first = (double[])g.Distance.Clone();
            Assert.Equal(PolyhedralGeodesicStatus.Ok, g.Compute(new[] { 0 }));
            for (int i = 0; i < first.Length; ++i) SameBits(first[i], g.Distance[i], "distance " + i);
            Assert.Equal(PolyhedralGeodesicStatus.DegenerateTriangles, new PolyhedralGeodesicDistance(HalfSliver()).Compute(new[] { 0 }));
        }

        // ---------------------------------------------------------------------------------------------- VectorHeatMethod

        [Theory]
        [MemberData(nameof(ExponentData))]
        public void VectorHeatMethod_ScalesExactly(int e)
        {
            DMesh3 mesh = Sphere(4);
            var h0 = new VectorHeatMethod(mesh);
            var h = new VectorHeatMethod(Scaled(mesh, e));
            Vector2d[] t0 = h0.TransportVector(0, new Vector2d(1, 0.5)), t = h.TransportVector(0, new Vector2d(1, 0.5));
            Vector2d[] l0 = h0.ComputeLogMap(0), l = h.ComputeLogMap(0);
            Assert.Equal(t0.Length, t.Length);
            for (int i = 0; i < t0.Length; ++i) {
                SameBits(t0[i].x, t[i].x, "transport x " + i); SameBits(t0[i].y, t[i].y, "transport y " + i);
                SameBitsScaled(l0[i].x, l[i].x, e, "log x " + i); SameBitsScaled(l0[i].y, l[i].y, e, "log y " + i);
            }
            SameBitsScaled(h0.DiffusionTime, h.DiffusionTime, 2 * e, "diffusion time");
        }

        [Fact]
        public void VectorHeatMethod_Reuse_ComputeTwice()
        {
            var h = new VectorHeatMethod(Scaled(Sphere(4), 400));
            Vector2d[] a = h.TransportVector(0, new Vector2d(1, 0)), b = h.TransportVector(0, new Vector2d(1, 0));
            for (int i = 0; i < a.Length; ++i) { SameBits(a[i].x, b[i].x, "x"); SameBits(a[i].y, b[i].y, "y"); }
        }

        // ---------------------------------------------------------------------------------------------- QuadRemesher

        [Theory]
        [InlineData(-500)]
        [InlineData(-250)]
        [InlineData(-37)]
        [InlineData(37)]
        [InlineData(250)]
        [InlineData(500)]
        public void QuadRemesher_ScalesExactly(int e)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            var q0 = new QuadRemesher(mesh) { TargetEdgeLength = 0.3, Parallel = false };
            Assert.Equal(QuadRemeshStatus.Success, q0.Compute());
            var q = new QuadRemesher(Scaled(mesh, e)) { TargetEdgeLength = Sc(0.3, e), Parallel = false };
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Assert.True(q.Mesh.VertexCount == mesh.VertexCount);
            Assert.Equal(q0.Result.FaceIndices.ToArray(), q.Result.FaceIndices.ToArray());
            Assert.Equal(q0.Result.VertexCount, q.Result.VertexCount);
            for (int i = 0; i < q0.Result.VertexCoordinates.Count; ++i)
                SameBitsScaled(q0.Result.VertexCoordinates[i], q.Result.VertexCoordinates[i], e, "coordinate " + i);
        }

        [Fact]
        public void QuadRemesher_SizeFunctionIsCalledInTheUnitsOfTheMesh_AndReuse()
        {
            DMesh3 mesh = Scaled(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh(), 300);
            double seenMax = 0;
            var q = new QuadRemesher(mesh) { TargetEdgeLength = Sc(0.3, 300), Parallel = false, SizeFunction = p => { seenMax = Math.Max(seenMax, Math.Abs(p.x)); return Sc(0.3, 300); } };
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Assert.True(seenMax > Sc(0.5, 300), "the size function gets points in the units of the mesh");
            int faces = q.Result.FaceCount;
            Assert.Equal(QuadRemeshStatus.Success, q.Compute());
            Assert.Equal(faces, q.Result.FaceCount);
            Assert.True(ReferenceEquals(mesh, q.Mesh), "the mesh of the caller is restored");
        }

        [Fact]
        public void QuadRemesher_LengthDisparityAndTargetBelowTheRange_AreInvalidInput()
        {
            Assert.Equal(QuadRemeshStatus.InvalidInput, new QuadRemesher(HalfSliver()) { TargetEdgeLength = 0.1 }.Compute());
            DMesh3 big = Scaled(new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh(), 600);
            Assert.Equal(QuadRemeshStatus.InvalidInput, new QuadRemesher(big) { TargetEdgeLength = Sc(1.0, -600) }.Compute());
        }
    }
}
