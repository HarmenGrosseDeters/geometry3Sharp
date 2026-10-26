using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Determinism of the operations with parallel paths (docs/QA.md): the parallel result equals the serial one where
    /// there is a switch, and repeated runs give bit-identical output. Each check runs several times, since a race shows
    /// only in some schedules. See also RemesherDeterminismTests and ThreadingFixesTests.
    /// </summary>
    public class DeterminismTests
    {
        const int Repeats = 4;

        static BoundedImplicitFunction3d Blob() => new ImplicitSmoothUnion3d() {
            A = new ImplicitSphere3d() { Origin = new Vector3d(0.1, -0.2, 0.05), Radius = 0.9 },
            B = new ImplicitSphere3d() { Origin = new Vector3d(0.8, 0.5, 0.3), Radius = 0.6 }
        };

        static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        /// <summary>Two spheres with a bump, so that there are few ties.</summary>
        static DMesh3 Bumpy()
        {
            DMesh3 m = Sphere(Vector3d.Zero, 1, 14);
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                m.SetVertex(vid, p * (1 + 0.15 * Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.y - 0.5)));
            }
            return m;
        }

        [Fact]
        public void MarchingCubes_ParallelEqualsSerial()
        {
            DMesh3 Run(bool parallel)
            {
                var mc = new MarchingCubes() { Implicit = Blob(), Bounds = Blob().Bounds().Expanded(0.2), CubeSize = 0.05, ParallelCompute = parallel };
                mc.Generate();
                return mc.Mesh;
            }
            DMesh3 serial = Run(false);
            Assert.True(serial.TriangleCount > 1000);
            for (int k = 0; k < Repeats; ++k)
                Assert.Equal("", Invariance.GeometricDifference(serial, Run(true)));
        }

        [Fact]
        public void DualContouring_ParallelEqualsSerial()
        {
            DMesh3 Run(bool parallel)
            {
                var dc = new DualContouring() {
                    Implicit = Blob(), IsoValue = 0, InsideIsLess = true, CellSize = 0.05,
                    Bounds = Blob().Bounds().Expanded(0.2), ParallelCompute = parallel
                };
                dc.Generate();
                return dc.Mesh;
            }
            DMesh3 serial = Run(false);
            Assert.True(serial.TriangleCount > 1000);
            for (int k = 0; k < Repeats; ++k)
                Assert.Equal("", Invariance.Difference(serial, Run(true)));
        }

        [Fact]
        public void WallThickness_RepeatedRuns_BitIdentical()
        {
            DMesh3 shell = Bumpy();
            DMesh3 inner = Sphere(new Vector3d(0.05, 0, 0), 0.7, 10);
            inner.ReverseOrientation();
            MeshEditor.Append(shell, inner);
            foreach (var method in new[] { MeshWallThickness.Methods.Ray, MeshWallThickness.Methods.InscribedSphere }) {
                var w0 = new MeshWallThickness(shell) { Method = method };
                Assert.True(w0.Compute());
                for (int k = 0; k < Repeats; ++k) {
                    var w = new MeshWallThickness(shell) { Method = method };
                    Assert.True(w.Compute());
                    Assert.True(w0.VertexThickness.SequenceEqual(w.VertexThickness), method.ToString());
                    Assert.True(w0.TriangleThickness.SequenceEqual(w.TriangleThickness), method.ToString());
                }
            }
        }

        [Fact]
        public void Deviation_And_Hausdorff_RepeatedRuns_BitIdentical()
        {
            DMesh3 mesh = Bumpy(), reference = Sphere(Vector3d.Zero, 1, 20);
            var d0 = new MeshDeviation(mesh, reference);
            Assert.True(d0.Compute());
            var h0 = new MeshHausdorffDistance(mesh, reference);
            Assert.True(h0.Compute());
            for (int k = 0; k < Repeats; ++k) {
                var d = new MeshDeviation(mesh, reference);
                Assert.True(d.Compute());
                Assert.True(d0.VertexDeviation.SequenceEqual(d.VertexDeviation));
                var h = new MeshHausdorffDistance(mesh, reference);
                Assert.True(h.Compute());
                Assert.Equal(h0.LowerBound, h.LowerBound);
                Assert.Equal(h0.UpperBound, h.UpperBound);
            }
        }

        [Fact]
        public void RobustRepair_RepeatedRuns_SameTriangles()
        {
            DMesh3 Input()
            {
                DMesh3 m = Bumpy();
                MeshEditor.Append(m, Sphere(new Vector3d(1.0, 0.3, 0.1), 0.8, 10));
                return m;
            }
            var r0 = new MeshRobustRepair(Input());
            Assert.True(r0.Apply());
            Assert.True(r0.UsedExactResolve, r0.ExactResolveReason);
            for (int k = 0; k < 2; ++k) {
                var r = new MeshRobustRepair(Input());
                Assert.True(r.Apply());
                // (the exact resolve uses ExactMeshBoolean, whose triangle order depends on the thread schedule,
                // see InvarianceTests.ExactBoolean_RepeatedRuns_BitIdentical)
                Assert.Equal("", Invariance.GeometricDifference(r0.Mesh, r.Mesh));
            }
        }

        [Fact]
        public void Reducer_RepeatedRuns_BitIdentical()
        {
            DMesh3 m0 = Bumpy();
            new Reducer(m0).ReduceToTriangleCount(400);
            for (int k = 0; k < Repeats; ++k) {
                DMesh3 m = Bumpy();
                new Reducer(m).ReduceToTriangleCount(400);
                Assert.Equal("", Invariance.Difference(m0, m));
            }
        }
    }
}
