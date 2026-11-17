using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// DualContouring.Simplify (default on, opt-out; docs/design/DC_ADAPTIVE_SIMPLIFICATION.md): off gives the uniform contour bit for bit,
    /// on gives the simplified one for the dense mode only, and the callers with their own Hermite data or sparse mode stay uniform.
    /// </summary>
    public class DualContouringSimplifyDefaultTests
    {
        class Fn : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public Fn(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d p) { return f(p); }
            public AxisAlignedBox3d Bounds() { return AxisAlignedBox3d.Empty; }
        }
        static double Box(Vector3d p, double he)
        {
            Vector3d q = new Vector3d(Math.Abs(p.x) - he, Math.Abs(p.y) - he, Math.Abs(p.z) - he);
            Vector3d o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }
        static double Cyl(double a, double b, double r) { return Math.Sqrt(a * a + b * b) - r; }
        static double Csg(Vector3d p)
        {
            double f = Math.Max(Box(p, 1), p.Length - 1.35);
            return Math.Max(f, -Math.Min(Cyl(p.y, p.z, 0.55), Math.Min(Cyl(p.x, p.z, 0.55), Cyl(p.x, p.y, 0.55))));
        }
        static double Sphere(Vector3d p) { return p.Length - 1; }
        static double Slabs(Vector3d p) { return Math.Min(Math.Max(Math.Abs(p.z - 0.35) - 0.15, Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 1), Math.Max(Math.Abs(p.z + 0.35) - 0.15, Math.Max(Math.Abs(p.x), Math.Abs(p.y)) - 1)); }
        static readonly Quaterniond Tilt = Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 23);
        static double RotBox(Vector3d p) { return Box(Tilt.Inverse() * p, 0.9); }

        const double Ext = 1.6;
        static DualContouring Make(Func<Vector3d, double> f, int n, bool simplify, bool parallel = false)
        {
            return new DualContouring() {
                Implicit = new Fn(f), InsideIsLess = true, CellSize = 2 * Ext / n, ParallelCompute = parallel, Simplify = simplify,
                Bounds = new AxisAlignedBox3d(new Vector3d(-Ext, -Ext, -Ext), new Vector3d(Ext, Ext, Ext))
            };
        }

        static string Digest(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            Action<long> add = v => { h ^= (ulong)v; h *= 1099511628211UL; };
            for (int v = 0; v < m.MaxVertexID; ++v) {
                if (!m.IsVertex(v)) { add(-1); continue; }
                Vector3d q = m.GetVertex(v);
                add(BitConverter.DoubleToInt64Bits(q.x)); add(BitConverter.DoubleToInt64Bits(q.y)); add(BitConverter.DoubleToInt64Bits(q.z));
            }
            for (int t = 0; t < m.MaxTriangleID; ++t) {
                if (!m.IsTriangle(t)) { add(-2); continue; }
                Index3i it = m.GetTriangle(t);
                add(it.a); add(it.b); add(it.c);
            }
            return $"v={m.VertexCount} t={m.TriangleCount} {h:x16}";
        }

        // digests of the uniform contour taken on master before Simplify existed (ParallelCompute false)
        [Theory]
        [InlineData("csg", 24, "v=1792 t=3600 53b0206642a2f576")]
        [InlineData("csg", 40, "v=4864 t=9744 53023f51ed4c4041")]
        [InlineData("sphere", 24, "v=1064 t=2124 96f5d9f5f96ef5c4")]
        [InlineData("sphere", 40, "v=2936 t=5868 59358cccca397ee0")]
        [InlineData("slabs", 24, "v=1144 t=2280 bd1a813296e0f767")]
        [InlineData("slabs", 40, "v=3304 t=6600 95c3c6a63eaa5a7d")]
        [InlineData("rotbox", 24, "v=1472 t=2940 8f3e7c929d082ad3")]
        [InlineData("rotbox", 40, "v=4088 t=8172 d15460da6bd65ab1")]
        public void SimplifyOff_IsTheUniformContourBitForBit(string shape, int n, string digest)
        {
            Func<Vector3d, double> f = shape == "csg" ? (Func<Vector3d, double>)Csg : shape == "sphere" ? Sphere : shape == "slabs" ? Slabs : RotBox;
            var dc = Make(f, n, false);
            dc.Generate();
            Assert.Equal(digest, Digest(dc.Mesh));
        }

        [Fact]
        public void DefaultIsOn_AndGivesFewerTrianglesThanTheUniformContourAtTheSameAccuracy()
        {
            var on = Make(Csg, 48, true); on.Generate();
            var off = Make(Csg, 48, false); off.Generate();
            Assert.True(new DualContouring().Simplify);
            Assert.True(on.Mesh.TriangleCount * 3 <= off.Mesh.TriangleCount, on.Mesh.TriangleCount + " vs " + off.Mesh.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(on.Mesh, true));
            Assert.True(on.SelfIntersectionFree);
            double worstOn = on.Mesh.Vertices().Max(v => Math.Abs(Csg(v))), worstOff = off.Mesh.Vertices().Max(v => Math.Abs(Csg(v)));
            Assert.True(worstOn <= worstOff + 0.5 * on.CellSize, worstOn + " vs " + worstOff);
        }

        [Fact]
        public void TriangleQuality_StaysUsable()
        {
            var on = Make(Csg, 64, true); on.Generate();
            int below10 = 0;
            foreach (int t in on.Mesh.TriangleIndices()) {
                Index3i it = on.Mesh.GetTriangle(t);
                Vector3d a = on.Mesh.GetVertex(it.a), b = on.Mesh.GetVertex(it.b), c = on.Mesh.GetVertex(it.c);
                double mn = Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b)));
                if (mn < 10) below10++;
            }
            Assert.True(below10 < 0.1 * on.Mesh.TriangleCount, below10 + " of " + on.Mesh.TriangleCount);
        }
        static double Angle(Vector3d p, Vector3d q, Vector3d r)
        {
            Vector3d u = q - p, v = r - p;
            double d = u.Length * v.Length;
            return d == 0 ? 0 : Math.Acos(MathUtil.Clamp(u.Dot(v) / d, -1, 1)) * 180 / Math.PI;
        }

        [Fact]
        public void OwnHermiteData_StaysUniform()
        {
            var a = Make(Csg, 24, true);
            a.HermiteF = (Vector3d ea, Vector3d eb, Vector3d crossing, out Vector3d point, out Vector3d normal) => { point = crossing; normal = Vector3d.AxisX; return false; };
            a.Generate();
            var b = Make(Csg, 24, false);
            b.HermiteF = a.HermiteF;
            b.Generate();
            Assert.Equal("", Invariance.Difference(a.Mesh, b.Mesh));
        }

        [Fact]
        public void SparseMode_StaysUniform()
        {
            Func<Vector3i, double> node = n => Sphere(new Vector3d(-Ext, -Ext, -Ext) + 2 * Ext / 24 * new Vector3d(n.x, n.y, n.z));
            DualContouring Sparse(bool simplify)
            {
                var cells = new List<Vector3i>();
                for (int i = 0; i < 24; ++i) for (int j = 0; j < 24; ++j) for (int k = 0; k < 24; ++k) cells.Add(new Vector3i(i, j, k));
                var dc = Make(Sphere, 24, simplify);
                dc.CandidateCells = cells; dc.NodeValueF = node;
                dc.Generate();
                return dc;
            }
            Assert.Equal("", Invariance.Difference(Sparse(true).Mesh, Sparse(false).Mesh));
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Invariance_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            var plain = Make(Csg, 32, true); plain.Generate();
            var corners = new List<Vector3d>();
            for (int c = 0; c < 8; ++c) corners.Add(s.Apply(new Vector3d((c & 1) == 0 ? -Ext : Ext, (c & 2) == 0 ? -Ext : Ext, (c & 4) == 0 ? -Ext : Ext)));
            var box = new AxisAlignedBox3d(corners[0]);
            foreach (var c in corners) box.Contain(c);
            double scale = s.Scale;
            var dc = new DualContouring() { Implicit = new Fn(p => scale * Csg(s.Inverse(p))), InsideIsLess = true, CellSize = plain.CellSize * scale, Bounds = box };
            dc.Generate();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(dc.Mesh, true));
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(plain.Mesh, Invariance.MappedBack(dc.Mesh, s)));
            else
                Assert.InRange((double)dc.Mesh.TriangleCount / plain.Mesh.TriangleCount, 0.5, 2.0);
        }
        public static IEnumerable<object[]> Similarities() { return Similarity.Names(); }

        [Fact]
        public void Reuse_GenerateTwice_GivesTheSameMesh()
        {
            var dc = Make(Csg, 32, true);
            dc.Generate();
            DMesh3 first = dc.Mesh;
            dc.Generate();
            Assert.False(ReferenceEquals(first, dc.Mesh));
            Assert.Equal("", Invariance.Difference(first, dc.Mesh));
        }

        [Fact]
        public void ParallelEqualsSerial_AndRepeatedRunsAreIdentical()
        {
            var serial = Make(Csg, 40, true, false); serial.Generate();
            for (int k = 0; k < 3; ++k) {
                var par = Make(Csg, 40, true, true); par.Generate();
                Assert.Equal("", Invariance.Difference(serial.Mesh, par.Mesh));
            }
        }
    }
}
