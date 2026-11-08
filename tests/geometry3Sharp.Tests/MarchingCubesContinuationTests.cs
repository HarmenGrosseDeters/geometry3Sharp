using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class MarchingCubesContinuationTests
    {
        sealed class Spheres : ImplicitFunction3d
        {
            readonly Vector3d a, b;
            public Spheres(int axis)
            {
                a = Rotate(new Vector3d(4, 1, 1), axis);
                b = Rotate(new Vector3d(1, 2, 1), axis);
            }
            public double Value(ref Vector3d p) => Math.Min(p.Distance(a), p.Distance(b)) - .3;
        }

        static Vector3d Rotate(Vector3d p, int axis) => axis == 0 ? p :
            axis == 1 ? new Vector3d(p.z, p.x, p.y) : new Vector3d(p.y, p.z, p.x);

        static void EqualGeometry(DMesh3 expected, DMesh3 actual)
        {
            Assert.True(actual.TriangleCount > 0);
            Assert.True(actual.CheckValidity());
            Assert.True(Invariance.AllFinite(actual));
            Assert.Equal("", Invariance.GeometricDifference(expected, actual));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void UpperCell_DoesNotAliasAnotherComponentOrRunPastGrid(int axis, bool parallel)
        {
            // #423 reviewer repro: n=3, so old done[3,0,0] aliases done[0,1,0].
            // Permuting axes also exercises the final slice, which used to throw.
            foreach (MarchingCubesPro.RootfindingModes mode in Enum.GetValues(typeof(MarchingCubesPro.RootfindingModes))) {
                var mc = new MarchingCubesPro { Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2.5, 2.5, 2.5)),
                    CubeSize = 1, Implicit = new Spheres(axis), ParallelCompute = parallel, RootMode = mode };
                // Full Generate over one extra cell matches continuation's inclusive grid.
                var reference = new MarchingCubesPro { Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3.5, 3.5, 3.5)),
                    CubeSize = 1, Implicit = mc.Implicit, ParallelCompute = false, RootMode = mode };
                reference.Generate();
                var seeds = new[] { Rotate(new Vector3d(3.1, .1, .1), axis), Rotate(new Vector3d(.1, 1.1, .1), axis) };
                // Isolate the first upper-cell visit: on the old parallel code an
                // indexing exception can leave the visited-grid lock held, blocking other seeds.
                if (parallel)
                    mc.GenerateContinuation(new[] { seeds[0] });
                mc.GenerateContinuation(seeds);
                EqualGeometry(reference.Mesh, mc.Mesh);
                // The outer component is deliberately cut at the extraction boundary;
                // the reference is therefore open too. The interior component is complete.
                var components = new MeshConnectedComponents(mc.Mesh); components.FindConnectedT();
                Assert.Equal(2, components.Count);
                mc.ParallelCompute = !parallel;
                mc.GenerateContinuation(seeds);
                EqualGeometry(reference.Mesh, mc.Mesh);
                // Generate replaces the corner cache; returning to continuation must resize it.
                mc.Generate();
                mc.GenerateContinuation(seeds);
                EqualGeometry(reference.Mesh, mc.Mesh);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyModes_IgnoreSeedsWhoseCellsAreOutsideGrid(bool parallel)
        {
            foreach (var mode in new[] { MarchingCubesPro.RootfindingModes.SingleLerp, MarchingCubesPro.RootfindingModes.LerpSteps,
                                       MarchingCubesPro.RootfindingModes.Bisection }) {
                var mc = new MarchingCubesPro { Bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2.5, 2.5, 2.5)),
                    CubeSize = 1, Implicit = new Spheres(0), ParallelCompute = parallel, RootMode = mode };
                var outside = new[] { new Vector3d(-2, 0, 0), new Vector3d(0, 4, 0), new Vector3d(0, 0, 100) };
                // One invalid seed at a time: the old parallel implementation can
                // leave its visited-grid lock held after an out-of-range exception.
                foreach (var seed in outside) {
                    mc.GenerateContinuation(new[] { seed });
                    Assert.Equal(0, mc.Mesh.TriangleCount);
                    Assert.True(mc.Mesh.CheckValidity());
                }
                mc.GenerateContinuation(new[] { outside[0], new Vector3d(.1, 1.1, .1), outside[2] });
                Assert.True(mc.Mesh.IsClosed());
                Assert.True(mc.Mesh.CheckValidity());
                Assert.True(mc.Mesh.TriangleCount > 0);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Continuation_IsInvariantAndReusable(bool parallel)
        {
            foreach (MarchingCubesPro.RootfindingModes mode in Enum.GetValues(typeof(MarchingCubesPro.RootfindingModes))) {
                DMesh3 reference = null;
                foreach (var s in Similarity.All()) {
                    var center = s.Apply(Vector3d.Zero);
                    var mc = new MarchingCubesPro { Bounds = new AxisAlignedBox3d(center, 1.5*s.Scale), CubeSize = .25*s.Scale,
                        IsoValue = .1*s.Scale, Implicit = new ImplicitSphere3d { Origin = center, Radius = s.Scale },
                        ParallelCompute = parallel, RootMode = mode };
                    var seeds = new[] { s.Apply(Vector3d.AxisX) };
                    mc.GenerateContinuation(seeds);
                    DMesh3 first = mc.Mesh;
                    Assert.True(first.IsClosed(), s.Name);
                    Assert.True(first.CheckValidity());
                    Assert.True(Invariance.AllFinite(first));
                    Assert.True(first.TriangleCount > 0);
                    Assert.All(first.Vertices(), p => Assert.InRange((p-center).Length/s.Scale, 1.06, 1.14));
                    if (reference == null) reference = first;
                    if (s.IsExact) EqualGeometry(reference, Invariance.MappedBack(first, s));
                    mc.ParallelCompute = !parallel;
                    mc.GenerateContinuation(seeds);
                    EqualGeometry(first, mc.Mesh);
                    mc.GenerateContinuation(seeds);
                    EqualGeometry(first, mc.Mesh);
                }
            }
        }
    }
}
