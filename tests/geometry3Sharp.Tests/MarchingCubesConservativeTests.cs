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

        static DMesh3 Generate(bool pro, bool parallel, ImplicitFunction3d f, AxisAlignedBox3d bounds, double h, double level)
        {
            if (pro) {
                var mc = new MarchingCubesPro { Implicit = f, Bounds = bounds, CubeSize = h, IsoValue = level,
                    RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = parallel };
                mc.Generate();
                return mc.Mesh;
            } else {
                var mc = new MarchingCubes { Implicit = f, Bounds = bounds, CubeSize = h, IsoValue = level,
                    RootMode = MarchingCubes.RootfindingModes.LinearEdges, ParallelCompute = parallel };
                mc.Generate();
                return mc.Mesh;
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void LinearEdges_GenerateIsInvariant(bool pro, bool parallel)
        {
            // Generate() itself under Similarity.All(): a sphere of radius 1 on a grid of spacing 1/4 around its
            // center, at the level 0.1 (no grid node on the level; at level 0 the node (1, 0, 0) is, and the mode throws).
            DMesh3 reference = null;
            foreach (Similarity s in Similarity.All()) {
                Vector3d center = s.Apply(Vector3d.Zero);
                var sphere = new ImplicitSphere3d { Origin = center, Radius = s.Scale };
                var bounds = new AxisAlignedBox3d(center, 1.5 * s.Scale);
                DMesh3 mesh = Generate(pro, parallel, sphere, bounds, 0.25 * s.Scale, 0.1 * s.Scale);
                Assert.True(mesh.TriangleCount > 0, s.Name);
                Assert.True(mesh.IsClosed(), s.Name);
                Assert.True(mesh.CheckValidity(), s.Name);
                Assert.True(Invariance.AllFinite(mesh), s.Name);
                Assert.All(mesh.Vertices(), v => Assert.InRange((v - center).Length / s.Scale, 1.07, 1.13));
                if (reference == null) reference = mesh;
                // (the 2^k scales: the same mesh, scaled, bit for bit; the ids of the Pro parallel path follow the schedule)
                if (s.IsExact)
                    Assert.Equal("", pro && parallel ? Invariance.GeometricDifference(reference, Invariance.MappedBack(mesh, s))
                                                     : Invariance.Difference(reference, Invariance.MappedBack(mesh, s)));
                // the parallel path gives the serial result (the same ids for MarchingCubes), on every run
                DMesh3 serial = parallel ? Generate(pro, false, sphere, bounds, 0.25 * s.Scale, 0.1 * s.Scale) : mesh;
                for (int run = 0; run < 2; ++run) {
                    DMesh3 again = Generate(pro, parallel, sphere, bounds, 0.25 * s.Scale, 0.1 * s.Scale);
                    Assert.Equal("", pro && parallel ? Invariance.GeometricDifference(serial, again) : Invariance.Difference(serial, again));
                }
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void LinearEdges_NodeOnLevelThrowsInvalidOperationInEveryPath(bool pro, bool parallel)
        {
            // The grid nodes (+-1, 0, 0), (0, +-1, 0), (0, 0, +-1) lie on the level 0 of the unit sphere: their crossings
            // fall onto grid corners in several z-slabs. The parallel paths throw the serial exception type, not an
            // AggregateException of the parallel loop.
            var sphere = new ImplicitSphere3d { Origin = Vector3d.Zero, Radius = 1 };
            var error = Assert.Throws<InvalidOperationException>(
                () => Generate(pro, parallel, sphere, new AxisAlignedBox3d(Vector3d.Zero, 1.5), 0.25, 0));
            Assert.Contains("LinearEdges", error.Message);
        }

        sealed class MeetingPlane : ImplicitFunction3d
        {
            // The plane y = 1 through grid nodes: every z-slab fails. The first sample on the plane in a thread waits
            // (at most two seconds) until a second thread has one too, so two slabs fail at the same time and the
            // parallel loop collects two exceptions (with a single core the test still passes, without this check).
            int arrived;
            [ThreadStatic] static MeetingPlane met;
            public double Value(ref Vector3d p)
            {
                if (p.y == 1 && met != this) {
                    met = this;
                    System.Threading.Interlocked.Increment(ref arrived);
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (System.Threading.Volatile.Read(ref arrived) < 2 && clock.ElapsedMilliseconds < 2000)
                        System.Threading.Thread.Yield();
                }
                return p.y - 1;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinearEdges_SimultaneousSlabFailuresThrowInvalidOperation(bool pro)
        {
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 3));
            var error = Assert.Throws<InvalidOperationException>(() => Generate(pro, true, new MeetingPlane(), bounds, 1, 0));
            Assert.Contains("LinearEdges", error.Message);
        }

        sealed class Slab : ImplicitFunction3d
        {
            public double Value(ref Vector3d p) => p.y - 0.5;
        }

        sealed class AliasField : ImplicitFunction3d
        {
            // A plane below x = 65536 and one low node at x = 65540: with 16-bit corner indices its y edges alias the
            // edges at x = 4 of the plane, which silently gave an open mesh with edges 65536 long.
            public double Value(ref Vector3d p)
            {
                if (p.x < 65536) return p.y - 0.25;
                return p.x == 65540 && p.y == 1 && p.z == 1 ? -1 : 1;
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void LinearEdges_ProRejectsGridsBeyondItsHash(bool parallel, bool continuation)
        {
            // MarchingCubesPro keeps 16 bits of each corner index in its corner and edge hashes: LinearEdges refuses a
            // grid of more than 65535 cells along an axis (65534 for the continuation) before any work. (Within these
            // limits every visited grid edge has its own vertex, and by the case table no edge between two crossings is
            // used by more than two triangles, so the rejected-triangle throw is not expected to fire; it stays as a
            // guard.)
            var bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(65600, 2, 2));
            var pro = new MarchingCubesPro { Implicit = new AliasField(), Bounds = bounds, CubeSize = 1,
                RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = parallel };
            var seeds = new[] { new Vector3d(65540, 1, 1), new Vector3d(4, 0.25, 1) };
            var error = Assert.Throws<ArgumentException>(() => { if (continuation) pro.GenerateContinuation(seeds); else pro.Generate(); });
            Assert.Contains("cells per axis", error.Message);
            Assert.Equal(0, pro.Mesh.TriangleCount);
            // the legacy modes are unchanged (no check)
            var legacy = new MarchingCubesPro { Implicit = new AliasField(), Bounds = bounds, CubeSize = 1, ParallelCompute = parallel };
            legacy.Generate();
            Assert.True(legacy.Mesh.TriangleCount > 0);
        }

        sealed class AxisPlane : ImplicitFunction3d
        {
            readonly int axis;
            public AxisPlane(int axis) { this.axis = axis; }
            public double Value(ref Vector3d p) => p[axis] - 0.5;
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public void LinearEdges_ProGridLimitIsExact(int axis, bool parallel)
        {
            // Along the given axis, w / h < 65535 gives (int)(w / h) + 1 <= 65535 cells, corner indices up to 65535:
            // allowed and without aliasing (a plane across the other axes crosses 65535 x 2 cells, two triangles each).
            // One more is refused.
            Vector3d size = Vector3d.One;
            size[axis] = 65534;
            DMesh3 mesh = Generate(true, parallel, new AxisPlane((axis + 1) % 3), new AxisAlignedBox3d(Vector3d.Zero, size), 1, 0);
            Assert.Equal(2 * 65535 * 2, mesh.TriangleCount);
            Assert.True(mesh.CheckValidity());
            size[axis] = 65535;
            Assert.Throws<ArgumentException>(
                () => Generate(true, parallel, new AxisPlane((axis + 1) % 3), new AxisAlignedBox3d(Vector3d.Zero, size), 1, 0));
        }

        sealed class TwoSpheres : ImplicitFunction3d
        {
            public double Value(ref Vector3d p) => Math.Min((p - new Vector3d(0, 1, 1)).Length, (p - new Vector3d(65536, 1, 1)).Length) - 0.7;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinearEdges_ProContinuationGridLimit(bool parallel)
        {
            // GenerateContinuation also visits the cell of index n (GridBounds.Contains is inclusive), so with n = 65535
            // cells its corner index 65536 aliased to 0: the spheres at x = 0 and x = 65536 shared vertices (an open
            // mesh with edges 65536 long). The continuation allows 65534 cells.
            var seeds = new[] { new Vector3d(0.5, 0.5, 0.5), new Vector3d(65535.5, 0.5, 0.5) };
            var pro = new MarchingCubesPro { Implicit = new TwoSpheres(), Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(65534, 2, 2)),
                CubeSize = 1, RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = parallel };
            var error = Assert.Throws<ArgumentException>(() => pro.GenerateContinuation(seeds));
            Assert.Contains("65534", error.Message);
            Assert.Equal(0, pro.Mesh.TriangleCount);
            // (seeds inside the bounds: a seed outside them is visited too, also an aliasing route, not checked here)
            pro.Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(65533, 2, 2));
            pro.GenerateContinuation(new[] { new Vector3d(0.5, 0.5, 0.5), new Vector3d(65533.5, 0.5, 0.5) });
            Assert.True(pro.Mesh.TriangleCount > 0);
            Assert.True(pro.Mesh.CheckValidity());
            Assert.All(pro.Mesh.Vertices(), v => Assert.True(v.x < 2));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NaN)]
        public void LinearEdges_ProRejectsInvalidCubeSize(double h)
        {
            var pro = new MarchingCubesPro { Implicit = new Slab(), Bounds = new AxisAlignedBox3d(Vector3d.Zero, 4), CubeSize = h,
                RootMode = MarchingCubesPro.RootfindingModes.LinearEdges, ParallelCompute = false };
            Assert.Contains("cube size", Assert.Throws<ArgumentException>(() => pro.Generate()).Message);
            Assert.Contains("cube size", Assert.Throws<ArgumentException>(() => pro.GenerateContinuation(new[] { Vector3d.One })).Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LinearEdges_CollapsedTriangleThrows(bool parallel)
        {
            // A grid that is not exactly representable: at x = 2^52 (spacing of doubles 1) the nodes 2^52 + 0.75 i
            // round to 2^52 + {0, 1, 2, 2, 3, 4, ...}, so one cell has width zero and MarchingCubes (vertices identified
            // by position) merges the crossings on its two sides into one vertex. The legacy modes skip the
            // degenerate triangles; LinearEdges throws (a triangle collapsed, coincident crossings).
            double x0 = Math.Pow(2, 52);
            var bounds = new AxisAlignedBox3d(new Vector3d(x0, 0, 0), new Vector3d(x0 + 4, 1, 1));
            var error = Assert.Throws<InvalidOperationException>(() => Generate(false, parallel, new Slab(), bounds, 0.75, 0));
            Assert.Contains("collapsed", error.Message);
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
