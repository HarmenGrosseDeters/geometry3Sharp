using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Follow-up of the IsFinite fix (#705): inline checks that tested IsNaN / IsInfinity of a sum of coordinates (which overflows for
    /// finite large values) now test each component (ClothSimulationFilter, MeshHausdorffDistance, TerrainDualContouring, PrimitiveFit3,
    /// TriangleDeviationBounds, MeshAlphaWrap). Each of these operations refuses or overflows further on at such coordinates anyway (a
    /// range check, or squares beyond about 1e154), so the results do not change; these tests pin that each gives a defined result there
    /// (false, no exception, no NaN output) at ordinary and at huge coordinates, where the sums overflow.
    /// </summary>
    public class InlineSumFiniteTests
    {
        static DMesh3 Sphere(double scale, Vector3d offset)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube { EdgeVertices = 6, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, scale * m.GetVertex(v) + offset);
            return m;
        }

        static List<Vector3d> Terrain(double scale, Vector3d offset)
        {
            var r = new Random(3);
            var l = new List<Vector3d>();
            for (int i = 0; i < 400; ++i) l.Add(offset + scale * new Vector3d(r.NextDouble(), r.NextDouble(), 0.05 * r.NextDouble()));
            return l;
        }

        public static TheoryData<double, double, double> Cases() => new TheoryData<double, double, double> {
            { 0.4e308, 1.2e308, 0 }, { 0.4e308, 1.2e308, 1.2e308 }, { 1e300, 1.2e308, 1.2e308 } };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Operations_GiveDefinedResults(double scale, double offsetXY, double offsetZ)
        {
            var off = new Vector3d(offsetXY, offsetXY, offsetZ);
            Assert.False(new ClothSimulationFilter(Terrain(scale, off)).Compute());
            Assert.False(new TerrainDualContouring(Terrain(scale, off)).Compute());
            Assert.False(new PrimitiveFit3(Sphere(scale, off).Vertices().ToList()).Compute());
            var h = new MeshHausdorffDistance(Sphere(scale, off), Sphere(scale * 1.01, off));
            Assert.False(h.Compute());
            var target = Sphere(scale, off);
            var tree = new DMeshAABBTree3(target, true);
            var bounds = new TriangleDeviationBounds(target);
            bool ok = bounds.MaxDistance(tree, target.GetVertex(0), target.GetVertex(1), target.GetVertex(7), 0, 0, 0, -1, -1, -1, scale * 1e-3, 8,
                out double lower, out double upper, out Vector3d worst);
            // (measured, or refused: a result has a finite lower bound and an upper bound not below it, +infinity where a piece cannot
            // be resolved, as MaxDistance documents)
            if (ok) {
                Assert.False(double.IsNaN(lower) || double.IsInfinity(lower), "lower " + lower);
                Assert.False(double.IsNaN(upper), "upper " + upper);
                Assert.True(upper >= lower, "upper " + upper + " below lower " + lower);
            }
            var wrap = new MeshAlphaWrap(Sphere(scale, off)) { Alpha = scale * 0.3, Offset = scale * 0.05 };
            Assert.False(wrap.Compute());
        }

        /// <summary> MeshHausdorffDistance squares lengths of the meshes' extent: from an extent of about 1e155 its nearest-triangle query
        /// found no triangle and the query threw (NullReferenceException in an AggregateException, on 1.0.324 too, without the sum check
        /// being involved). It now refuses an extent beyond 2^500; large coordinates with a small extent are still measured </summary>
        [Theory]
        [InlineData(1e160, 0.0)]
        [InlineData(1e200, 0.0)]
        [InlineData(1e300, 0.0)]
        [InlineData(1e190, 1e200)]
        [InlineData(0.4e308, 1.2e308)]
        public void Hausdorff_BeyondTheExtentLimit_Refuses(double scale, double offset)
        {
            var off = new Vector3d(offset, offset, 0);
            var h = new MeshHausdorffDistance(Sphere(scale, off), Sphere(scale * 1.01, off));
            Assert.False(h.Compute());
            Assert.True(double.IsNaN(h.LowerBound) && double.IsNaN(h.UpperBound));
        }

        [Theory]
        [InlineData(1, 1e200)]
        [InlineData(1e100, 0)]
        [InlineData(1e140, 0)]
        public void Hausdorff_WithinTheExtentLimit_Measures(double scale, double offset)
        {
            var off = new Vector3d(offset, 0, 0);
            var h = new MeshHausdorffDistance(Sphere(scale, off), Sphere(scale * 1.01, off));
            Assert.True(h.Compute());
            // the spheres' radii differ by 1 %: the distance is 0.01 scale, up to the tolerance and the face discretization
            Assert.InRange(h.LowerBound, 0.009 * scale, 0.011 * scale);
            Assert.True(h.UpperBound >= h.LowerBound);
        }
    }
}
