using System;
using System.Text;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshWindingNumberBoolean outside an extent of [2^-32, 2^32]: the winding-number field overflows (the extent to the 4th power), so
    /// Compute() succeeded with an EMPTY mesh from about 1e79 to 1e103 and above 1e153 (silent data loss), and degraded below about 1e-12.
    /// It now runs on a copy scaled by a power of two to an extent in [1, 2), as MeshWindingNumberSolidify does.
    /// </summary>
    public class WindingBooleanScaleTests
    {
        static DMesh3 Octahedron(Vector3d c, double r)
        {
            var m = new DMesh3();
            Vector3d[] v = { c + new Vector3d(r, 0, 0), c + new Vector3d(-r, 0, 0), c + new Vector3d(0, r, 0), c + new Vector3d(0, -r, 0), c + new Vector3d(0, 0, r), c + new Vector3d(0, 0, -r) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[][] t = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            foreach (int[] f in t) m.AppendTriangle(f[0], f[1], f[2]);
            return m;
        }

        static MeshWindingNumberBoolean Make(double r, MeshWindingNumberBoolean.Operations op, double cell = 0, int resolution = 128)
        {
            return new MeshWindingNumberBoolean(Octahedron(Vector3d.Zero, r), Octahedron(new Vector3d(0.5 * r, 0, 0), r), op) { CellSize = cell, GridResolution = resolution };
        }

        static string Signature(DMesh3 m, double inverseScale)
        {
            var sb = new StringBuilder();
            foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';'); }
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v) * inverseScale;
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            return sb.ToString();
        }

        static double Volume(DMesh3 m, double r)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                v += (a / r).Dot((b / r).Cross(c / r)) / 6.0;
            }
            return v;
        }

        [Theory]
        [InlineData(1e-100)]
        [InlineData(1e-20)]
        [InlineData(1e80)]
        [InlineData(1e90)]
        [InlineData(1e100)]
        [InlineData(1e154)]
        [InlineData(1e200)]
        [InlineData(1e300)]
        public void Union_AtAnyScale_IsTheUnion_NotAnEmptyMesh(double r)
        {
            var boolean = Make(r, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result, false));
            Assert.InRange(Volume(boolean.Result, r), 2.08, 2.13);        // the unit-scale union of the two octahedra: 2.1042
        }

        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference)]
        public void Boolean_OfA2kScaledCopy_IsTheScaledResult(MeshWindingNumberBoolean.Operations op)
        {
            var reference = Make(1, op, 0, 40);
            Assert.True(reference.Compute());
            string expected = Signature(reference.Result, 1);
            // (including the two sides of the edge of the band: the octahedra have extents of 2.5 r: 2^e is inside, 2^(e+1) outside for e = 30)
            foreach (int e in new[] { -300, -35, -30, 29, 31, 40, 300, 900 }) {
                double s = Math.Pow(2, e);
                var scaled = Make(s, op, 0, 40);
                Assert.True(scaled.Compute());
                Assert.Equal(expected, Signature(scaled.Result, 1.0 / s));
                Assert.Equal(reference.UsedCellSize, scaled.UsedCellSize / s);
            }
        }

        [Fact]
        public void CellSize_IsInTheUnitsOfTheCaller_AtEveryScale()
        {
            var reference = Make(1, MeshWindingNumberBoolean.Operations.Union, 0.05);
            Assert.True(reference.Compute());
            Assert.Equal(0.05, reference.UsedCellSize, 12);
            string expected = Signature(reference.Result, 1);
            foreach (int e in new[] { -100, -40, 40, 100, 300 }) {
                double s = Math.Pow(2, e);
                var scaled = Make(s, MeshWindingNumberBoolean.Operations.Union, 0.05 * s);
                Assert.True(scaled.Compute());
                Assert.Equal(expected, Signature(scaled.Result, 1.0 / s));
                Assert.Equal(0.05 * s, scaled.UsedCellSize);
            }
        }

        [Fact]
        public void Compute_CanBeRepeated_OnOneInstance_AtALargeScale()
        {
            var boolean = Make(1e100, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            string first = Signature(boolean.Result, 1);
            Assert.True(boolean.Compute());
            Assert.Equal(first, Signature(boolean.Result, 1));
        }

        [Fact]
        public void Compute_OfAnEmptyAndAHugeMesh_StillWorks()
        {
            var boolean = new MeshWindingNumberBoolean(new DMesh3(), Octahedron(Vector3d.Zero, 1e100), MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result, false));
        }
    }
}
