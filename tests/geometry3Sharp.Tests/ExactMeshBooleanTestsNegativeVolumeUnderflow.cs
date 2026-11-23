using System;
using System.Collections.Generic;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1347: ExactMeshBoolean.negative_volume claimed "certainly negative" where the coordinate differences are near 2^-358:
    /// the triple products are subnormal and the bound (32 + n) u S rounded to 0. The oracle is ExactPredicates.VolumeSign (the
    /// exact sign of the volume, docs/design/EXACT_VOLUME_SIGN.md): a component of exact volume >= 0 must never be refused.
    /// </summary>
    public class ExactMeshBooleanTestsNegativeVolumeUnderflow
    {
        // the reproducer of #1347 (found by the error bound audit): the triangles (0,1,2), (1,3,4)
        static readonly double[] Repro = {
            3.4063678720065236E-108, 3.40636787200652E-108, 8.515952165671821E-109,
            -8.515919680016305E-109, -1.70318393600326E-108, -8.515919680016307E-109,
            -3.406367872018913E-108, 6.81273574401304E-108, 3.4063678720065455E-108,
            -3.4063678720065202E-108, -2.530540142149384E-108, 1.7031839360033087E-108,
            -1.7031839360032601E-108, -8.515919680016305E-109, 1.7032099245276744E-108 };

        static DMesh3 Mesh(double[] xyz, int[] tris)
        {
            var m = new DMesh3();
            for (int i = 0; i < xyz.Length; i += 3) m.AppendVertex(new Vector3d(xyz[i], xyz[i + 1], xyz[i + 2]));
            for (int i = 0; i < tris.Length; i += 3) m.AppendTriangle(tris[i], tris[i + 1], tris[i + 2]);
            return m;
        }

        // (by reflection: negative_volume is private on master)
        static readonly System.Reflection.MethodInfo NegativeVolume = typeof(ExactMeshBoolean).GetMethod("negative_volume",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        static bool Negative(DMesh3 m)
        {
            var component = new List<int>(m.TriangleIndices());
            return (bool)NegativeVolume.Invoke(null, new object[] { m, component });
        }

        // the exact sign of the volume of the triangles of the mesh (o = the first corner, as negative_volume)
        static int ExactSign(DMesh3 m)
        {
            var xyz = new List<double>();
            for (int v = 0; v < m.MaxVertexID; ++v) { Vector3d p = m.GetVertex(v); xyz.Add(p.x); xyz.Add(p.y); xyz.Add(p.z); }
            var corners = new List<int>();
            foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); corners.Add(f.a); corners.Add(f.b); corners.Add(f.c); }
            // (negative_volume takes the first vertex of the first triangle as o: the sum is the same for every o on a closed
            // surface, but not for this open pair: put o first as the library's own sum does)
            int o = m.GetTriangle(0).a;
            var c2 = new List<int> { o, o, o };
            c2.AddRange(corners);
            return ExactPredicates.VolumeSign(xyz.ToArray(), c2.ToArray());
        }

        [Fact]
        public void TheReproducer_IsNotCertainlyNegative_AsTheExactSignIsNot()
        {
            DMesh3 m = Mesh(Repro, new[] { 0, 1, 2, 1, 3, 4 });
            Assert.True(ExactSign(m) >= 0, "the exact volume sign of the reproducer is not negative");
            Assert.False(Negative(m));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void TheReproducer_UnderEverySimilarity_IsNeverRefusedWhereTheExactVolumeIsNotNegative(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = Invariance.Transformed(Mesh(Repro, new[] { 0, 1, 2, 1, 3, 4 }), s);
            if (ExactSign(m) >= 0)
                Assert.False(Negative(m), name);
        }

        [Theory]
        [InlineData(0)] [InlineData(100)] [InlineData(300)] [InlineData(358)] [InlineData(400)] [InlineData(700)]
        public void TheReproducerScaledByPowersOfTwo_IsNotRefusedAtAnyScale(int k)
        {
            double f = ExactPoint3.ScaleB(1.0, k);
            var xyz = (double[])Repro.Clone();
            for (int i = 0; i < xyz.Length; ++i) xyz[i] *= f;
            DMesh3 m = Mesh(xyz, new[] { 0, 1, 2, 1, 3, 4 });
            Assert.True(ExactSign(m) >= 0);
            Assert.False(Negative(m), "2^" + k);
        }

        // a real inside-out solid is still refused at every scale above the underflow range, a right-way one never
        [Theory]
        [InlineData(-300)] [InlineData(-200)] [InlineData(-100)] [InlineData(0)] [InlineData(100)] [InlineData(300)]
        public void ATetrahedron_IsRefusedInsideOutAtAnyScale_AndNotRightWayOut(int k)
        {
            double f = ExactPoint3.ScaleB(1.0, k);
            double[] xyz = { 0, 0, 0, f, 0, 0, 0, f, 0, 0, 0, f };
            // outward oriented (positive volume), and the same with two vertices swapped (inside out)
            DMesh3 outward = Mesh(xyz, new[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 0, 3, 2 });
            DMesh3 inward = Mesh(xyz, new[] { 0, 1, 2, 0, 3, 1, 1, 3, 2, 0, 2, 3 });
            Assert.Equal(-1, ExactSign(inward));
            Assert.Equal(1, ExactSign(outward));
            Assert.NotEqual(ExactSign(outward), ExactSign(inward));
            bool outwardNegative = Negative(outward), inwardNegative = Negative(inward);
            Assert.False(outwardNegative && ExactSign(outward) >= 0, "2^" + k + ": refused a right-way solid");
            // (exactly one of them is the negative one)
            Assert.True(inwardNegative != outwardNegative, "2^" + k + ": the inside-out solid is not told from the other");
        }

        // The decision at its exact edge (every term of the bound is pinned). Triangle 0 has the corner o first (x = 0: it adds 1
        // to the underflow sum and nothing else), triangle 1 has x = (1, xy, xz), y = (0, t, 0), z = (0, 0, -w): v6 = -(t w)
        // exactly, a multiple of 2^-1074 (t = 2^-1000). The norm term (32 + n) u S is below half of 2^-1074 for these (it rounds
        // to 0), so the bound is UnderflowUnit * (1 + 1 + 1 + |xy| + |xz|) = 2^-1066 * under. d = 0 puts v6 on the bound
        // (not refused: the test is strict), d = 1 one unit of 2^-1074 beyond it (refused), d = -1 one unit short of it (not).
        [Theory]
        [InlineData(0, 0)] [InlineData(2, 0)] [InlineData(0, 4)] [InlineData(2, 4)] [InlineData(-2, -4)] [InlineData(-2, 4)] [InlineData(2, -4)]
        public void TheBoundOfTheUnderflowTerm_IsStrict_AndSumsTheAbsoluteValuesOfAllThreeCoordinates(double xy, double xz)
        {
            double under = 3 + Math.Abs(xy) + Math.Abs(xz);
            double t = ExactPoint3.ScaleB(1.0, -1000);
            foreach (int d in new[] { -1, 0, 1 }) {
                double w = (under * 256 + d) * ExactPoint3.ScaleB(1.0, -74);       // t w = (under 256 + d) 2^-1074, exact
                Assert.Equal((under * 256 + d) * double.Epsilon, t * w);
                var xyz = new double[] { 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, xy, xz, 0, t, 0, 0, 0, -w };
                DMesh3 m = Mesh(xyz, new[] { 0, 1, 2, 3, 4, 5 });
                Assert.Equal(d > 0, Negative(m));
            }
        }
    }
}
