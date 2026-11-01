using System;
using System.Collections.Generic;
using System.Numerics;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The triangle planes of ExactMeshArrangement in fixed width (FixedArith.TrianglePlanes, docs/design/
    /// CHEAPER_CONSTRUCTIONS.md section 4.5) against the BigInteger code: the same plane, axis, sign and the same 16
    /// approximations, for corners of every size up to the FixedPoint3 limit; the limb -> double conversion against
    /// (double)BigInteger.
    /// </summary>
    public unsafe class ExactPlanesFixedWidthTests
    {
        static BigInteger RandomValue(Random r, int bits)
        {
            if (bits == 0) return BigInteger.Zero;
            var b = new byte[bits / 8 + 2];
            r.NextBytes(b);
            b[b.Length - 1] = 0;
            BigInteger v = new BigInteger(b) >> (8 * b.Length - 8 - bits);
            return (r.Next(2) == 0) ? -v : v;
        }

        static FixedInt Load(BigInteger v, ulong* buf, int limbs)
        {
            int len;
            bool neg;
            Assert.True(FixedPoint3.LoadN(v, buf, limbs, out len, out neg));
            return new FixedInt(buf, len, neg);
        }

        [Fact]
        public void ToDouble_SameAsBigIntegerConversion()
        {
            var r = new Random(1);
            ulong* buf = stackalloc ulong[8];
            for (int k = 0; k < 200000; ++k) {
                BigInteger v = RandomValue(r, r.Next(0, 8 * 64 - 1));
                if (k % 7 == 0) v = (BigInteger.One << r.Next(0, 510)) * (r.Next(2) == 0 ? 1 : -1) - r.Next(-2, 3);
                if (BigInteger.Abs(v) >= BigInteger.One << 511) continue;
                double got = FixedArith.ToDouble(Load(v, buf, 8));
                double expected = (double)v;
#if NET
                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(got));
#else
                // (.NET Framework's conversion may round differently: the filters need a relative error below 2^-52)
                Assert.True(Math.Abs(got - expected) <= Math.Abs(expected) * Math.Pow(2, -52));
#endif
            }
        }

        [Fact]
        public void ToDouble_BeyondTheDoubleRange_Infinite()
        {
            // 17-limb values (the edge planes' F of 4-limb corners reach 1027 bits)
            var r = new Random(2);
            ulong* buf = stackalloc ulong[20];
            foreach (int bits in new[] { 1023, 1024, 1025, 1030, 1087 }) {
                for (int k = 0; k < 100; ++k) {
                    BigInteger v = (BigInteger.One << (bits - 1)) + BigInteger.Abs(RandomValue(r, bits - 2));
                    if (k % 2 == 1) v = -v;
                    int limbs = 17;
                    // (LoadN takes at most 8 limbs: the magnitude by hand)
                    BigInteger m = BigInteger.Abs(v);
                    int n = 0;
                    while (m.IsZero == false) { buf[n++] = (ulong)(m & ulong.MaxValue); m >>= 64; }
                    Assert.True(n <= limbs);
                    double got = FixedArith.ToDouble(new FixedInt(buf, n, v.Sign < 0));
#if NET
                    Assert.Equal(BitConverter.DoubleToInt64Bits((double)v), BitConverter.DoubleToInt64Bits(got));
#else
                    double e = (double)v;
                    Assert.True(double.IsInfinity(e) == double.IsInfinity(got) && (double.IsInfinity(e) || Math.Abs(got - e) <= Math.Abs(e) * Math.Pow(2, -52)));
#endif
                }
            }
        }

        [Fact]
        public void LeadingZerosPortable_Matches()
        {
            var r = new Random(3);
            for (int k = 0; k < 64; ++k)
                Assert.Equal(63 - k, FixedArith.LeadingZerosPortable(1UL << k));
            for (int k = 0; k < 100000; ++k) {
                ulong v = ((ulong)(uint)r.Next() << 32 | (uint)r.Next()) >> r.Next(64);
                if (v == 0) continue;
                int expected = 0;
                while ((v >> (63 - expected)) == 0) expected++;
                Assert.Equal(expected, FixedArith.LeadingZerosPortable(v));
            }
        }

        // compute_planes's BigInteger code (the reference)
        static void Reference(ExactPoint3 a, ExactPoint3 b, ExactPoint3 c, double[] pa, out BigInteger[] plane, out int axis, out int sign)
        {
            BigInteger ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
            BigInteger vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
            BigInteger nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            plane = new[] { nx, ny, nz, nx * a.X + ny * a.Y + nz * a.Z };
            BigInteger ax = BigInteger.Abs(nx), ay = BigInteger.Abs(ny), az = BigInteger.Abs(nz);
            axis = (ax >= ay && ax >= az) ? 0 : (ay >= az) ? 1 : 2;
            sign = ((axis == 0) ? nx : (axis == 1) ? ny : nz).Sign;
            var corners = new[] { a, b, c };
            for (int k = 0; k < 4; ++k) pa[k] = (double)plane[k];
            for (int k = 0; k < 3; ++k) {
                ExactPoint3 p = corners[k], q = corners[(k + 1) % 3];
                BigInteger dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                BigInteger ex = dy * nz - dz * ny, ey = dz * nx - dx * nz, ez = dx * ny - dy * nx;
                BigInteger f = ex * p.X + ey * p.Y + ez * p.Z;
                pa[4 * k + 4] = (double)ex; pa[4 * k + 5] = (double)ey; pa[4 * k + 6] = (double)ez; pa[4 * k + 7] = (double)f;
            }
        }

        [Fact]
        public void TrianglePlanes_SameAsBigInteger()
        {
            var r = new Random(4);
            BigInteger max = (BigInteger.One << 255) - 1;
            for (int k = 0; k < 20000; ++k) {
                int bits = (k % 5 == 0) ? 255 : r.Next(0, 256);
                var pts = new ExactPoint3[3];
                for (int i = 0; i < 3; ++i) {
                    if (k % 97 == 0)
                        pts[i] = ExactPoint3.FromInteger(r.Next(2) == 0 ? max : -max, r.Next(2) == 0 ? max : -max, r.Next(2) == 0 ? max : -max);
                    else
                        // (the corners of one triangle with different lengths too: 1-limb next to 4-limb ones)
                        pts[i] = ExactPoint3.FromInteger(RandomValue(r, bits), RandomValue(r, r.Next(0, bits + 1)), RandomValue(r, (k % 3 == 0) ? r.Next(0, 256) : bits));
                }
                if (k % 11 == 0) pts[2] = pts[0];      // a degenerate triple: N = 0
                FixedPoint3 fa, fb, fc;
                Assert.True(FixedPoint3.TryCreate(pts[0], out fa));
                Assert.True(FixedPoint3.TryCreate(pts[1], out fb));
                Assert.True(FixedPoint3.TryCreate(pts[2], out fc));
                var got = new double[16];
                BigInteger nx, ny, nz, d;
                int axis, sign;
                FixedArith.TrianglePlanes(ref fa, ref fb, ref fc, got, out nx, out ny, out nz, out d, out axis, out sign);
                var expected = new double[16];
                BigInteger[] plane;
                int eaxis, esign;
                Reference(pts[0], pts[1], pts[2], expected, out plane, out eaxis, out esign);
                Assert.Equal(plane[0], nx); Assert.Equal(plane[1], ny); Assert.Equal(plane[2], nz); Assert.Equal(plane[3], d);
                Assert.Equal(eaxis, axis); Assert.Equal(esign, sign);
                for (int i = 0; i < 16; ++i) {
#if NET
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(got[i]));
#else
                    Assert.True(double.IsInfinity(expected[i]) == double.IsInfinity(got[i]) && (double.IsInfinity(got[i]) || Math.Abs(got[i] - expected[i]) <= Math.Abs(expected[i]) * Math.Pow(2, -52)));
#endif
                }
            }
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // the planes of every triangle of the arrangement of two meshes, as a string (exact plane, axis, sign, approximations)
        static string Planes(List<DMesh3> meshes, bool fixedWidth)
        {
            var arr = new ExactMeshArrangement(meshes) { FixedWidthPoints = fixedWidth };
            arr.Compute();
            var sb = new System.Text.StringBuilder();
            foreach (var t in arr.Triangles) {
                if (t.Degenerate) continue;
                var p = t.P;
                sb.Append(p.A).Append(',').Append(p.B).Append(',').Append(p.C).Append(',').Append(p.D).Append(';').Append(t.Axis).Append(t.Sign);
                foreach (double x in t.PlaneApprox) sb.Append(',').Append(BitConverter.DoubleToInt64Bits(x));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [Fact]
        public void Arrangement_TakesTheFixedWidthPath()
        {
            // every non-degenerate input triangle (integer corners, all fixed width) gets its plane by FixedArith
            var arr = new ExactMeshArrangement(new List<DMesh3> { Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 5), Sphere(new Vector3d(0.7, 0.4, 0.1), 0.8, 4) });
            arr.Compute();
            int n = 0;
            foreach (var t in arr.Triangles)
                if (t.Degenerate == false) {
                    Assert.NotNull(t.compute_planes_fixed());
                    n++;
                }
            Assert.True(n > 100);
            // and with fixed width off, never
            var off = new ExactMeshArrangement(new List<DMesh3> { Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 5) }) { FixedWidthPoints = false };
            off.Compute();
            foreach (var t in off.Triangles)
                if (t.Degenerate == false)
                    Assert.Null(t.compute_planes_fixed());
        }

        [Fact]
        public void Arrangement_SamePlanesWithoutFixedWidth()
        {
            var meshes = new List<DMesh3> { Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 6), Sphere(new Vector3d(0.7, 0.4, 0.1), 0.8, 5) };
            Assert.Equal(Planes(meshes, false), Planes(meshes, true));
        }

        [Fact]
        public void Invariance_Pow2ScalesGiveTheSamePlanes()
        {
            // the grid integers do not change under 2^k scales, so neither do the planes; under the other
            // similarities the planes are those of the transformed input, compared with the BigInteger code
            var baseMeshes = new List<DMesh3> { Sphere(new Vector3d(0.1, 0.2, 0.3), 1, 5), Sphere(new Vector3d(0.7, 0.4, 0.1), 0.8, 4) };
            string reference = Planes(baseMeshes, true);
            foreach (Similarity sim in Similarity.All()) {
                var meshes = new List<DMesh3>();
                foreach (DMesh3 m0 in baseMeshes) {
                    var m = new DMesh3(m0);
                    foreach (int vid in m.VertexIndices())
                        m.SetVertex(vid, sim.Apply(m.GetVertex(vid)));
                    meshes.Add(m);
                }
                string planes = Planes(meshes, true);
                Assert.Equal(Planes(meshes, false), planes);
                if (sim.IsExact)
                    Assert.Equal(reference, planes);
            }
        }
    }
}
