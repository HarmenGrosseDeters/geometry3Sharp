using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #800: DualContouring's quad split must not keep a degenerate triangle when the other diagonal has none. On a crease
    /// line the QEF puts consecutive vertices exactly on the line, three of a quad can be collinear, and the normal of their
    /// triangle is rounding noise that could win the normal-agreement choice. Input: a box with a 45 degree chamfer, as the
    /// minimum of plane distances, with exact Hermite data (the crossing by bisection, the plane's normal).
    /// </summary>
    public class DualContouringDegenerateSplitTests
    {
        readonly ITestOutputHelper output;
        public DualContouringDegenerateSplitTests(ITestOutputHelper o) { output = o; }

        // inside where F > 0: F = min over the planes of (offset - n . x), n unit outward normals
        sealed class Planes : ImplicitFunction3d
        {
            public Vector3d[] N; public double[] D;
            public double Value(ref Vector3d p) { double f = double.MaxValue; for (int i = 0; i < N.Length; ++i) f = Math.Min(f, D[i] - N[i].Dot(p)); return f; }
            public int Active(Vector3d p) { int a = 0; double f = double.MaxValue; for (int i = 0; i < N.Length; ++i) { double v = D[i] - N[i].Dot(p); if (v < f) { f = v; a = i; } } return a; }
        }

        // the box [0, 4]^3 with the corner x + y < c cut off by a 45 degree chamfer, under the similarity sim (planes and grid
        // moved with it: normals rotated, offsets scaled and shifted, the grid box the bounds of the moved box, cell scaled)
        static DMesh3 Contour(double c, Similarity sim, bool parallel)
        {
            double r = Math.Sqrt(0.5);
            Vector3d[] n = { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(0, 0, -1), new Vector3d(-r, -r, 0) };
            double[] d = { 4, 0, 4, 0, 4, 0, -c * r };
            var f = new Planes() { N = new Vector3d[n.Length], D = new double[n.Length] };
            for (int i = 0; i < n.Length; ++i) {
                f.N[i] = sim.ApplyDirection(n[i]);
                f.D[i] = sim.ApplyLength(d[i]) + f.N[i].Dot(sim.Translation);
            }
            var box = new AxisAlignedBox3d(new Vector3d(-0.6, -0.55, -0.65), new Vector3d(4.6, 4.65, 4.55));
            var grid = AxisAlignedBox3d.Empty;
            for (int q = 0; q < 8; ++q) grid.Contain(sim.Apply(box.Corner(q)));
            var dc = new DualContouring() { Implicit = f, IsoValue = 0, CellSize = sim.ApplyLength(0.25), ParallelCompute = parallel, Bounds = grid };
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) => {
                Vector3d lo = a, hi = b;   // a inside
                for (int i = 0; i < 80; ++i) { Vector3d m = 0.5 * (lo + hi); if (f.Value(ref m) > 0) lo = m; else hi = m; }
                point = 0.5 * (lo + hi);
                normal = f.N[f.Active(point)];
                return true;
            };
            dc.Generate();
            Assert.Equal(0, dc.NonManifoldTriangles);
            return dc.Mesh;
        }

        static int Degenerate(DMesh3 m)
        {
            int k = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                double l2 = Math.Max((b - a).LengthSquared, Math.Max((c - b).LengthSquared, (a - c).LengthSquared));
                if (!((b - a).Cross(c - a).Length > 2e-12 * l2)) k++;
            }
            return k;
        }

        // chamfer offsets for which the master before #800 kept 1-4 degenerate triangles (measured over 40 offsets: 11+ cases)
        public static readonly double[] Offsets = { 1.0 + 0.0371 * 14, 1.0 + 0.0371 * 22, 1.0 + 0.0371 * 27 };

        [Fact]
        public void ChamferCrease_NoDegenerateTriangles()
        {
            foreach (double c in Offsets) {
                DMesh3 m = Contour(c, Similarity.Identity, true);
                Assert.True(m.IsClosed(), $"c {c}");
                Assert.Equal(0, Degenerate(m));
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            }
        }

        [Fact]
        public void ChamferCrease_Similarities_Pow2BitIdentical_AllClean()
        {
            double c = Offsets[1];
            DMesh3 r0 = Contour(c, Similarity.Identity, true);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 r = Contour(c, sim, true);
                Assert.True(r.IsClosed(), sim.Name);
                Assert.Equal(0, Degenerate(r));
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(r0, Invariance.MappedBack(r, sim)));
            }
        }

        [Fact]
        public void ChamferCrease_ParallelEqualsSerial_AndRepeats()
        {
            foreach (double c in Offsets) {
                DMesh3 serial = Contour(c, Similarity.Identity, false);
                for (int rep = 0; rep < 2; ++rep)
                    Assert.Equal("", Invariance.Difference(serial, Contour(c, Similarity.Identity, true)));
            }
        }
    }
}
