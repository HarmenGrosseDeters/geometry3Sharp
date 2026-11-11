using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// the supported input range of the rounding (docs/QA.md 2.5a): MeshRoundingRepair reports OutsideRange and leaves the mesh;
    /// ExactMeshBoolean keeps its exact result (the arrangement has no range) but RoundingClean is false, and StrictRounding fails
    /// </summary>
    public class RoundingInputRangeTests
    {
        static DMesh3 Cube(Vector3d c, double h, double scale)
        {
            DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(h, h, h)) }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, scale * m.GetVertex(v));
            return m;
        }

        static ExactMeshBoolean Union(double scale, bool strict = false)
        {
            var op = new ExactMeshBoolean(Cube(Vector3d.Zero, 1, scale), Cube(new Vector3d(0.5, 0.25, 0.125), 1, scale), ExactMeshBoolean.Operations.Union) { StrictRounding = strict };
            op.Compute();
            return op;
        }

        [Theory]
        [InlineData(-1000)]
        [InlineData(-500)]
        [InlineData(-300)]
        [InlineData(-280)]
        [InlineData(-210)]
        [InlineData(210)]
        [InlineData(700)]
        public void Rounding_OutsideRange_NotTouched_NotClean(int e)
        {
            DMesh3 m = Cube(Vector3d.Zero, 1, Math.Pow(2, e));
            DMesh3 before = new DMesh3(m);
            var r = new MeshRoundingRepair(m);
            Assert.False(r.Apply());
            Assert.True(r.OutsideRange);
            Assert.False(r.IsClean);
            Assert.Equal("", Invariance.Difference(before, m));
        }

        [Fact]
        public void Rounding_Subnormal_OutsideRange()
        {
            var r = new MeshRoundingRepair(Cube(Vector3d.Zero, 1, 1e-320));
            Assert.False(r.Apply());
            Assert.True(r.OutsideRange);
        }

        // before: Success with an empty mesh and RoundingClean true below 2^-280 (a union of two cubes)
        [Theory]
        [InlineData(-500)]
        [InlineData(-300)]
        [InlineData(-280)]
        public void Boolean_TinyScale_KeepsTheResult_NotReportedClean(int e)
        {
            var op = Union(Math.Pow(2, e));
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            Assert.Equal(Union(Math.Pow(2, -100)).Result.TriangleCount, op.Result.TriangleCount);
            Assert.True(op.Result.IsClosed());
            Assert.False(op.RoundingClean);
        }

        [Fact]
        public void Boolean_TinyScale_Strict_Fails()
        {
            var op = Union(Math.Pow(2, -300), true);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, op.Status);
            Assert.Null(op.Result);
        }

        // the range is the largest coordinate, not each one: a result coordinate that cancels toward 0 (here 2^-80 of the scale) is in range
        [Fact]
        public void Rounding_OneTinyCoordinate_InRange()
        {
            DMesh3 m = Cube(Vector3d.Zero, 1, Math.Pow(2, -180));
            int v = m.VertexIndices().First();
            Vector3d p = m.GetVertex(v);
            m.SetVertex(v, new Vector3d(Math.Pow(2, -260), p.y, p.z));
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.False(r.OutsideRange);
        }

        [Theory]
        [InlineData(1.0000000000000002)]       // the first double above 2^200
        [InlineData(0.5)]                      // 2^199 is in range
        public void Rounding_UpperEdge(double factor)
        {
            DMesh3 m = Cube(Vector3d.Zero, 1, Math.Pow(2, 200) * factor);
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(factor > 1, r.OutsideRange);
        }

        // the failure of a strict boolean names the range
        [Fact]
        public void Boolean_TinyScale_Strict_ErrorNamesTheRange()
        {
            var op = Union(Math.Pow(2, -300), true);
            Assert.Contains("supported range", op.Error);
            Assert.Equal(1, op.Counts["rounding_outside_range"]);
        }

        [Theory]
        [InlineData(-190)]
        [InlineData(-100)]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(190)]
        public void InsideRange_Clean_Pow2BitIdentical(int e)
        {
            double s = Math.Pow(2, e);
            var op0 = Union(1);
            var op = Union(s);
            Assert.True(op.RoundingClean);
            Assert.True(op.Result.IsClosed());
            var back = new DMesh3(op.Result);
            foreach (int v in back.VertexIndices()) back.SetVertex(v, back.GetVertex(v) / s);
            Assert.Equal("", Invariance.Difference(op0.Result, back));
        }
    }
}
