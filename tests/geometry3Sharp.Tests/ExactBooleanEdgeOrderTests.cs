using System;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ExactMeshBoolean pairs the triangles around an edge where solids touch by the order of their half-planes
    /// around the edge. Half-planes that differ by less than the rounding of the points must still be ordered
    /// correctly, or the result is not closed.
    /// </summary>
    public class ExactBooleanEdgeOrderTests
    {
        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static DMesh3 Tetra(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            DMesh3 m = new DMesh3();
            foreach (Vector3d v in new[] { a, b, c, d }) m.AppendVertex(v);
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(0, 3, 2);
            if (Volume(m) < 0)
                m.ReverseOrientation();
            return m;
        }

        // the next double towards +infinity (s = 1) or -infinity (s = -1), for x != 0 (Math.BitIncrement is not in net48)
        static double Ulp(double x, int s)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return BitConverter.Int64BitsToDouble(bits + ((x > 0) == (s > 0) ? 1 : -1));
        }

        [Fact]
        public void TetrahedraTouchingAlongAnEdge_NearlyCoincidentFaces()
        {
            // tetrahedra (u, v, a, b) and (u, v, c, d) touching only along the oblique edge u-v, with c a few ulps
            // outside the face (u, v, b): the faces (u, v, b) and (u, v, c) differ by about 1e-16 radians
            var rand = new Random(1);
            int cases = 0;
            for (int it = 0; it < 300; ++it) {
                Vector3d u = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                Vector3d v = u + new Vector3d(1 + rand.NextDouble(), 0.7 + rand.NextDouble(), 0.3 + rand.NextDouble());
                Vector3d a = new Vector3d(3 * rand.NextDouble(), 3 * rand.NextDouble(), 3 * rand.NextDouble());
                Vector3d b = new Vector3d(3 * rand.NextDouble(), 3 * rand.NextDouble(), 3 * rand.NextDouble());
                int sa = ExactPredicates.Orient3d(u, v, b, a);
                if (sa == 0)
                    continue;
                int k = rand.Next(3);
                Vector3d c = b;
                for (int dir = 0; dir < 2 && ExactPredicates.Orient3d(u, v, b, c) != -sa; ++dir) {
                    c = b;
                    for (int step = 0; step < 30 && ExactPredicates.Orient3d(u, v, b, c) != -sa; ++step)
                        c[k] = Ulp(c[k], (dir == 0) ? 1 : -1);
                }
                if (ExactPredicates.Orient3d(u, v, b, c) != -sa)
                    continue;
                // d well on the other side of both planes (u, v, b) and (u, v, c)
                Vector3d n = (v - u).Cross(b - u).Normalized;
                Vector3d d = 0.5 * (u + v) + n * 2.0 * ((ExactPredicates.Orient3d(u, v, b, u + n) == -sa) ? 1 : -1);
                int sc = ExactPredicates.Orient3d(u, v, c, a);
                if (sc == 0 || ExactPredicates.Orient3d(u, v, b, d) != -sa || ExactPredicates.Orient3d(u, v, c, d) != -sc)
                    continue;
                DMesh3 t1 = Tetra(u, v, a, b), t2 = Tetra(u, v, c, d);
                var boolean = new ExactMeshBoolean(t1, t2, Ops.Union);
                Assert.True(boolean.Compute(), boolean.Error);
                DMesh3 r = boolean.Result;
                Assert.True(r.IsClosed(), "case " + it + ": not closed");
                Assert.Equal(0, boolean.NonManifoldTriangles);
                Assert.Equal(1, boolean.NonManifoldEdges);
                Assert.Equal(8, r.TriangleCount);
                Assert.Equal(Volume(t1) + Volume(t2), Volume(r), 12);
                cases++;
            }
            Assert.True(cases > 100, cases + " cases");
        }
    }
}
