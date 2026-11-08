using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MarchingCubesConservativeTests
    {
        static Vector3d Edge(object mc, Vector3d a, Vector3d b, double fa, double fb)
        {
            object[] args = { a, b, fa, fb, Vector3d.Zero };
            mc.GetType().GetMethod("find_iso", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mc, args);
            return (Vector3d)args[4];
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinearEdges_BypassesNudgeAndIsInvariant(bool pro)
        {
            foreach (var s in Similarity.All()) {
                // This near-endpoint crossing took the legacy nudge branch.
                double fa = (1 - 5e-7) * s.Scale, fb = 1.1 * s.Scale;
                object mc = pro ? (object)new MarchingCubesPro { IsoValue = s.Scale, RootMode = MarchingCubesPro.RootfindingModes.LinearEdges }
                                : new MarchingCubes { IsoValue = s.Scale, RootMode = MarchingCubes.RootfindingModes.LinearEdges };
                Vector3d a = s.Apply(Vector3d.Zero), b = s.Apply(Vector3d.AxisX);
                Vector3d result = Edge(mc, a, b, fa, fb);
                Vector3d reverse = Edge(mc, b, a, fb, fa);
                Assert.Equal(result, reverse);
                double t = (1 - (1 - 5e-7)) / (1.1 - (1 - 5e-7));
                Vector3d expected = s.Apply(new Vector3d(t, 0, 0));
                if (s.IsExact)
                    Assert.Equal(expected, result);
                else
                    Assert.True(result.Distance(expected) <= 64 * s.Resolution(1) + 1e-14*s.Scale, $"{s}: error {result.Distance(expected):R}, expected {expected}, actual {result}");
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinearEdges_RejectsEndpointCollapse(bool pro)
        {
            object mc = pro ? (object)new MarchingCubesPro { IsoValue = 1, RootMode = MarchingCubesPro.RootfindingModes.LinearEdges }
                            : new MarchingCubes { IsoValue = 1, RootMode = MarchingCubes.RootfindingModes.LinearEdges };
            var error = Assert.Throws<TargetInvocationException>(() => Edge(mc, Vector3d.Zero, Vector3d.AxisX, 0, 1));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }

        [Fact]
        public void BelowThreshold_CellCenteredSphereIsMissed()
        {
            const double h = .25;
            var sphere = new ImplicitSphere3d { Origin = new Vector3d(h/2, h/2, h/2), Radius = h/2000 };
            var mc = new MarchingCubes { Implicit = sphere, Bounds = new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One), CubeSize = h,
                IsoValue = Math.Sqrt(3)*h/2-h/1000, RootMode = MarchingCubes.RootfindingModes.LinearEdges, ParallelCompute = false };
            mc.Generate(); Assert.Equal(0, mc.Mesh.TriangleCount);
            var pro = new MarchingCubesPro { Implicit = sphere, Bounds = mc.Bounds, CubeSize = h, IsoValue = mc.IsoValue,
                RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = false };
            pro.Generate(); Assert.Equal(0, pro.Mesh.TriangleCount);
            Vector3d exactZero = sphere.Origin + sphere.Radius*Vector3d.AxisX;
            Assert.True(Math.Abs(sphere.Value(ref exactZero)) < 1e-16);
        }

        sealed class Field : ImplicitFunction3d
        {
            readonly int kind;
            public Field(int kind) { this.kind = kind; }
            public double Value(ref Vector3d p)
            {
                if (kind == 0) return (p - new Vector3d(.02, -.03, .01)).Length - .19;
                if (kind == 1) {
                    Vector3d d = new Vector3d(Math.Abs(p.x)-.18, Math.Abs(p.y)-.14, Math.Abs(p.z)-.12);
                    return new Vector3d(Math.Max(d.x, 0), Math.Max(d.y, 0), Math.Max(d.z, 0)).Length
                        + Math.Min(Math.Max(d.x, Math.Max(d.y, d.z)), 0);
                }
                Vector3d q = Rotate(p, -0.41);
                double radial = Math.Sqrt(q.x*q.x+q.z*q.z)-.20;
                return Math.Sqrt(radial*radial+q.y*q.y)-.06;
            }
        }

        static Vector3d Rotate(Vector3d p, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new Vector3d(p.x, c*p.y-s*p.z, s*p.y+c*p.z);
        }

        static IEnumerable<Vector3d> Surface(int kind)
        {
            for (int i = 0; i < 24; ++i) for (int j = 0; j <= 12; ++j) {
                double a = i * 2*Math.PI/24, b = j*Math.PI/12;
                if (kind == 0) yield return new Vector3d(.02, -.03, .01) + .19 * new Vector3d(Math.Sin(b)*Math.Cos(a), Math.Cos(b), Math.Sin(b)*Math.Sin(a));
                else if (kind == 2) {
                    b *= 2;
                    yield return Rotate(new Vector3d((.20+.06*Math.Cos(b))*Math.Cos(a), .06*Math.Sin(b), (.20+.06*Math.Cos(b))*Math.Sin(a)), .41);
                }
            }
            if (kind == 1) for (int axis = 0; axis < 3; ++axis) for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i <= 10; ++i) for (int j = 0; j <= 10; ++j) {
                    var p = new Vector3d();
                    var half = new Vector3d(.18, .14, .12);
                    p[axis] = side*half[axis];
                    p[(axis+1)%3] = (i/5.0-1)*half[(axis+1)%3];
                    p[(axis+2)%3] = (j/5.0-1)*half[(axis+2)%3];
                    yield return p;
                }
        }

        static void Valid(DMesh3 mesh, int kind)
        {
            Assert.True(mesh.TriangleCount > 0);
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity());
            foreach (var p in mesh.Vertices())
                for (int d = 0; d < 3; ++d) Assert.True(!double.IsNaN(p[d]) && !double.IsInfinity(p[d]));
            var tree = new DMeshAABBTree3(mesh, true);
            foreach (var p in Surface(kind)) Assert.True(tree.WindingNumber(p) > .99, $"outside: {p}");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void LinearEdges_EnclosesProceduralSurfacesAndReuses(int kind)
        {
            const double h = 1.0/16;
            // Normalized exact dyadic grid, away from equality and endpoint collapse.
            double sigma = Math.Sqrt(3)*h/2 + Math.Pow(2, -26);
            var mc = new MarchingCubes { Implicit = new Field(kind), Bounds = new AxisAlignedBox3d(Vector3d.Zero, .5), CubeSize = h,
                IsoValue = sigma, RootMode = MarchingCubes.RootfindingModes.LinearEdges, ParallelCompute = false };
            mc.Generate(); Valid(mc.Mesh, kind);
            var baseline = mc.Mesh.Vertices().ToArray();
            var triangles = mc.Mesh.Triangles().ToArray();
            mc.Generate(); Assert.Equal(baseline, mc.Mesh.Vertices().ToArray());
            mc.ParallelCompute = true; mc.Generate(); Valid(mc.Mesh, kind);
            Assert.Equal(baseline, mc.Mesh.Vertices().ToArray()); Assert.Equal(triangles, mc.Mesh.Triangles().ToArray());
            var pro = new MarchingCubesPro { Implicit = new Field(kind), Bounds = mc.Bounds, CubeSize = h,
                IsoValue = sigma, RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = false };
            pro.Generate(); Valid(pro.Mesh, kind);
            var points = pro.Mesh.Vertices().OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToArray();
            Assert.Equal(baseline.OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToArray(), points);
            pro.Generate(); Assert.Equal(points, pro.Mesh.Vertices().OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToArray());
            pro.ParallelCompute = true; pro.Generate(); Valid(pro.Mesh, kind);
            Assert.Equal(points, pro.Mesh.Vertices().OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).ToArray());
        }
    }
}
