using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The triangle frames and the bowtie test of ParamMesh run in parallel, each element into its own slot. The result
    /// is the bits of the serial loop (gParallel.SerialOnThisThread), the surface area included (summed in triangle order).
    /// </summary>
    public class ParamMeshParallelTests
    {
        static DMesh3 Torus(int nu, int nv, double jitter, int seed)
        {
            var rnd = new Random(seed);
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv, r = 0.35 * (1 + jitter * (rnd.NextDouble() - 0.5));
                    m.AppendVertex(new Vector3d((1 + r * Math.Cos(v)) * Math.Cos(u), (1 + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            Func<int, int, int> id = (i, j) => (i % nu) * nv + (j % nv);
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    m.AppendTriangle(id(i, j), id(i + 1, j), id(i + 1, j + 1));
                    m.AppendTriangle(id(i, j), id(i + 1, j + 1), id(i, j + 1));
                }
            return m;
        }

        /// <summary> a grid with some collapsed (degenerate) triangles </summary>
        static DMesh3 DegenerateGrid()
        {
            var m = new DMesh3();
            int n = 40;
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i, j, (i * 7 + j * 3) % 5 == 0 ? 0.3 : 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            m.SetVertex(5, m.GetVertex(6));   // a zero length edge
            return m;
        }

        static long B(double d) => BitConverter.DoubleToInt64Bits(d);

        static long[] Bits(ParamMesh pm)
        {
            var l = new List<long> { B(pm.SurfaceArea), pm.Frames.Count };
            foreach (TriangleFrame f in pm.Frames) {
                l.Add(f.Degenerate ? 1 : 0); l.Add(B(f.Area)); l.Add(B(f.P1.x)); l.Add(B(f.P1.y)); l.Add(B(f.P2.x)); l.Add(B(f.P2.y));
                l.Add(B(f.Dx.x)); l.Add(B(f.Dx.y)); l.Add(B(f.Dx.z)); l.Add(B(f.Dy.x)); l.Add(B(f.Dy.y)); l.Add(B(f.Dy.z));
                l.Add(B(f.Cot.x)); l.Add(B(f.Cot.y)); l.Add(B(f.Cot.z));
            }
            return l.ToArray();
        }

        static ParamMesh Serial(DMesh3 m)
        {
            gParallel.SerialOnThisThread = true;
            try { return new ParamMesh(m); } finally { gParallel.SerialOnThisThread = false; }
        }

        public static IEnumerable<object[]> Shapes()
        {
            yield return new object[] { 0 };   // torus 60 x 40
            yield return new object[] { 1 };   // noisy torus
            yield return new object[] { 2 };   // degenerate triangles
            yield return new object[] { 3 };   // scaled by 2^20
        }

        static DMesh3 Make(int k)
        {
            switch (k) {
                case 0: return Torus(60, 40, 0, 1);
                case 1: return Torus(50, 30, 0.6, 2);
                case 2: return DegenerateGrid();
                default: { DMesh3 m = Torus(40, 30, 0.2, 3); MeshTransforms.Scale(m, 1048576.0); return m; }
            }
        }

        [Theory, MemberData(nameof(Shapes))]
        public void FramesAndArea_EqualTheSerialLoopBitForBit_AndRepeat(int shape)
        {
            DMesh3 m = Make(shape);
            long[] reference = Bits(Serial(m));
            Assert.True(reference.Length > 100);
            for (int rep = 0; rep < 5; ++rep) {
                ParamMesh pm = new ParamMesh(m);
                Assert.True(pm.Valid || shape == 2, pm.Error);
                Assert.Equal(reference, Bits(pm));
                double area = 0;   // the serial sum in triangle order, independent of the code under test
                for (int ti = 0; ti < pm.Tris.Count; ++ti) {
                    Index3i t = pm.Tris[ti];
                    var f = new TriangleFrame(pm.Pos[t.a], pm.Pos[t.b], pm.Pos[t.c]);
                    if (!f.Degenerate) area += f.Area;
                }
                Assert.Equal(B(area), B(pm.SurfaceArea));
            }
            foreach (int order in new[] { -1, 7, 99 }) {   // other schedules of the loops (the environment adversary)
                gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
                try { Assert.Equal(reference, Bits(new ParamMesh(m))); }
                finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
            }
            if (shape == 2) Assert.Contains(Serial(m).Frames, f => f.Degenerate);
        }

        /// <summary> two interior bowties: the error names the first in vertex index order, parallel or not </summary>
        [Fact]
        public void Bowtie_NamesTheFirstVertex_AsTheSerialLoop()
        {
            var mesh = new DMesh3();
            var centers = new List<int>();
            for (int pair = 0; pair < 3; ++pair)
                for (int rep = 0; rep < 40; ++rep) {   // many bowties, so that several threads find one
                    int c = mesh.AppendVertex(new Vector3d(10 * rep, 10 * pair, 0));
                    if (rep == 0) centers.Add(c);
                    for (int d = 0; d < 2; ++d) {
                        var rim = new List<int>();
                        for (int k = 0; k < 6; ++k) {
                            double a = k * Math.PI / 3;
                            rim.Add(mesh.AppendVertex(new Vector3d(10 * rep + Math.Cos(a), 10 * pair + (d == 0 ? Math.Sin(a) : 0.3), d == 0 ? 0 : Math.Sin(a))));
                        }
                        for (int k = 0; k < 6; ++k)
                            Assert.True(mesh.AppendTriangle(c, rim[k], rim[(k + 1) % 6]) >= 0);
                    }
                }
            Assert.True(mesh.IsBowtieVertex(centers[0]));
            ParamMesh serial = Serial(mesh);
            Assert.False(serial.Valid);
            Assert.Contains("bowtie", serial.Error);
            Assert.Contains("vertex " + centers[0] + " ", serial.Error);
            foreach (int order in new[] { -1, 7 }) {
                gParallel.SerialOnThisThread = true; gParallel.SerialOrderOnThisThread = order;
                try { Assert.Contains("vertex " + centers[0] + " ", new ParamMesh(mesh).Error); }
                finally { gParallel.SerialOnThisThread = false; gParallel.SerialOrderOnThisThread = 0; }
            }
            for (int rep = 0; rep < 5; ++rep) {
                ParamMesh pm = new ParamMesh(mesh);
                Assert.Equal(ParameterizationStatus.InvalidMesh, pm.Status);
                Assert.Equal(serial.Error, pm.Error);
                Assert.Contains("vertex " + centers[0] + " ", pm.Error);
            }
        }

        [Fact]
        public void ManifoldMesh_HasNoBowtie_Valid()
        {
            Assert.True(new ParamMesh(Torus(30, 20, 0, 1)).Valid);
        }
    }
}
