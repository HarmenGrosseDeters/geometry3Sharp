using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> gs.MeshPrincipalCurvature (Rusinkiewicz 2004): values and directions on a sphere and a torus, noise, scale. </summary>
    public class MeshPrincipalCurvatureTests
    {
        // a torus by its (u, v) grid: R around the z axis, r around the tube, outward normals
        static DMesh3 GridTorus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            var id = new int[nu, nv];
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    id[i, j] = m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int i1 = (i + 1) % nu, j1 = (j + 1) % nv;
                    m.AppendTriangle(id[i, j], id[i1, j], id[i1, j1]);
                    m.AppendTriangle(id[i, j], id[i1, j1], id[i, j1]);
                }
            return m;
        }

        [Fact]
        public void Sphere_HasCurvatureOneOverRadius_WithOutwardNormals()
        {
            DMesh3 m = CrossFieldShapes.UnitSphere();
            var c = MeshPrincipalCurvature.Compute(m);
            foreach (int v in m.VertexIndices()) {
                Assert.True(c.Valid[v]);
                Assert.InRange(c.K1[v], 0.85, 1.2);
                Assert.InRange(c.K2[v], 0.85, 1.2);
                Assert.True(c.K1[v] >= c.K2[v]);
                Assert.Equal(0, c.Dir1[v].Dot(c.Normal[v]), 9);
                Assert.Equal(1, c.Dir1[v].Length, 9);
            }
        }

        [Fact]
        public void Torus_PrincipalDirectionsFollowTubeAndBigCircle()
        {
            double R = 1, r = 0.35;
            DMesh3 m = GridTorus(R, r, 64, 32);
            var c = MeshPrincipalCurvature.Compute(m);
            // the curvature around the tube is 1 / r everywhere, along the big circle cos(v) / (R + r cos(v)); the larger one is around the tube
            foreach (int vid in m.VertexIndices()) {
                Vector3d p = m.GetVertex(vid);
                double rho = Math.Sqrt(p.x * p.x + p.y * p.y), cv = (rho - R) / r;
                double kBig = cv / (R + r * cv);
                Assert.InRange(c.K1[vid], 1 / r * 0.9, 1 / r * 1.1);
                Assert.InRange(c.K2[vid], kBig - 0.15, kBig + 0.15);
                Vector3d around = new Vector3d(-p.y, p.x, 0).Normalized;
                // K2's direction is along the big circle
                Assert.True(Math.Abs(c.Dir2[vid].Dot(around)) > 0.98, "vertex " + vid);
            }
        }

        [Fact]
        public void SmoothingRounds_ReduceTheDirectionErrorOfNoisyInput()
        {
            DMesh3 m = CrossFieldShapes.NoisyTorus();
            Func<MeshPrincipalCurvature, double> error = c => {
                double sum = 0; int n = 0;
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    Vector3d around = new Vector3d(-p.y, p.x, 0).Normalized;
                    double d = Math.Abs(c.Dir2[v].Normalized.Dot(around));   // K2 direction is along the big circle where K1 - K2 is large
                    sum += Math.Acos(Math.Min(1, d)); n++;
                }
                return sum / n;
            };
            double raw = error(MeshPrincipalCurvature.Compute(m));
            double smooth = error(MeshPrincipalCurvature.Compute(m, 2));
            Assert.True(smooth < 0.6 * raw, "mean angle error " + raw + " -> " + smooth);
            Assert.True(smooth < 0.35);
        }

        [Theory, MemberData(nameof(Frames))]
        public void Curvature_IsInvariantUnderSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = GridTorus(1, 0.35, 32, 16);
            var a = MeshPrincipalCurvature.Compute(m, 1);
            var b = MeshPrincipalCurvature.Compute(Invariance.Transformed(m, s), 1);
            foreach (int v in m.VertexIndices()) {
                if (s.IsExact) {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a.K1[v] / s.Scale), BitConverter.DoubleToInt64Bits(b.K1[v]));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(a.K2[v] / s.Scale), BitConverter.DoubleToInt64Bits(b.K2[v]));
                    Assert.Equal(a.Dir1[v].x, b.Dir1[v].x);
                } else {
                    Assert.True(Math.Abs(a.K1[v] / s.Scale - b.K1[v]) < 1e-5 * Math.Abs(b.K1[v]), name);
                    Assert.True((s.ApplyDirection(a.Dir1[v]) - b.Dir1[v]).Length < 1e-5 || (s.ApplyDirection(a.Dir1[v]) + b.Dir1[v]).Length < 1e-5);
                }
            }
        }

        public static System.Collections.Generic.IEnumerable<object[]> Frames() => Similarity.Names();

        [Fact]
        public void ParallelLoops_EqualSerialOnes_BitForBit()
        {
            DMesh3 m = CrossFieldShapes.NoisyTorus();
            var parallel = MeshPrincipalCurvature.Compute(m, 2);
            foreach (int order in new[] { 0, -1, 3 }) {
                gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
                MeshPrincipalCurvature serial;
                try { serial = MeshPrincipalCurvature.Compute(m, 2); }
                finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
                foreach (int v in m.VertexIndices()) {
                    Assert.Equal(BitConverter.DoubleToInt64Bits(parallel.K1[v]), BitConverter.DoubleToInt64Bits(serial.K1[v]));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(parallel.K2[v]), BitConverter.DoubleToInt64Bits(serial.K2[v]));
                    foreach (var (p, q) in new[] { (parallel.Dir1[v], serial.Dir1[v]), (parallel.Dir2[v], serial.Dir2[v]), (parallel.Normal[v], serial.Normal[v]) }) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.x), BitConverter.DoubleToInt64Bits(q.x));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.y), BitConverter.DoubleToInt64Bits(q.y));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(p.z), BitConverter.DoubleToInt64Bits(q.z));
                    }
                    Assert.Equal(parallel.Valid[v], serial.Valid[v]);
                }
            }
        }

        [Fact]
        public void CornerAreas_SumToTheTriangleArea()
        {
            var tris = new[] {
                new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) },          // right
                new[] { new Vector3d(0, 0, 0), new Vector3d(4, 0, 0), new Vector3d(1, 1, 0) },          // obtuse
                new[] { new Vector3d(0, 0, 0), new Vector3d(3, 0, 0), new Vector3d(1.2, 2, 0) } };      // acute, scalene
            foreach (var p in tris) {
                double area = 0.5 * (p[1] - p[0]).Cross(p[2] - p[0]).Length;
                double[] a = MeshPrincipalCurvature.CornerAreas(p, area);
                Assert.Equal(area, a.Sum(), 12);
                Assert.All(a, x => Assert.True(x > 0));
            }
            // the right triangle: 1/4 at the right angle (the Voronoi cell), 1/8 at the others
            double[] r = MeshPrincipalCurvature.CornerAreas(tris[0], 0.5);
            Assert.Equal(new[] { 0.25, 0.125, 0.125 }, r.Select(x => Math.Round(x, 12)).ToArray());
        }

        [Fact]
        public void DegenerateAndIsolatedVertices_AreNotValid()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(2, 0, 0));
            int iso = m.AppendVertex(new Vector3d(5, 5, 5));
            m.AppendTriangle(a, b, c);
            var pc = MeshPrincipalCurvature.Compute(m);
            Assert.All(new[] { a, b, c, iso }, v => Assert.False(pc.Valid[v]));
            Assert.All(new[] { a, b, c, iso }, v => Assert.Equal(0, pc.K1[v]));
        }
    }
}
