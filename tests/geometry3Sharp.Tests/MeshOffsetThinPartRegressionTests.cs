using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// An inset that leaves a part thinner than about a cell is not "empty" (#1597): the grid nodes (the only place the field is sampled)
    /// can all lie outside the thin part. MeshOffset refines the cell where the distances at the nodes cannot exclude such a part, and only there.
    /// Uses only the API of the library before the change (this file compiles against it, so that its tests count as regressions there).
    /// </summary>
    public class MeshOffsetThinPartRegressionTests
    {
        // the box 1 x 0.7 x 0.5 of the issue: an inset by d leaves (1 + 2d) x (0.7 + 2d) x (0.5 + 2d)
        static DMesh3 Box() => new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
        static double Exact(double d) => (1 + 2 * d) * (0.7 + 2 * d) * (0.5 + 2 * d);
        static double Volume(DMesh3 m) => MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;

        static string Digest(DMesh3 m)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { for (int i = 0; i < 8; ++i) { h ^= (byte)(x >> (8 * i)); h *= 1099511628211UL; } } }
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); Mix(BitConverter.DoubleToInt64Bits(p.x)); Mix(BitConverter.DoubleToInt64Bits(p.y)); Mix(BitConverter.DoubleToInt64Bits(p.z)); }
            foreach (int t in m.TriangleIndices()) { Index3i i = m.GetTriangle(t); Mix(i.a); Mix(i.b); Mix(i.c); }
            return m.TriangleCount + " " + h.ToString("X16");
        }

        static void AssertSolid(MeshOffset op, string what)
        {
            Assert.Equal("Success", op.ResultStatus.ToString());
            DMesh3 r = op.Result;
            Assert.True(r.TriangleCount > 0, what + ": empty");
            Assert.True(r.IsClosed(), what + ": not closed");
            Assert.Equal(0, op.NonManifoldTriangles);
            Assert.True(Invariance.AllFinite(r), what + ": not finite");
            Assert.Empty(MeshIterators.BowtieVertices(r));
        }

        // the cases of the issue at the default cell: the part is thin (0.03, 0.02, 0.01), exists, and a finer grid finds it exactly
        [Theory]
        [InlineData(-0.22)]
        [InlineData(-0.235)]
        [InlineData(-0.24)]
        [InlineData(-0.245)]
        public void ThinPartLeft_IsFound_AtTheDefaultCell(double d)
        {
            var op = new MeshOffset(Box(), d);
            Assert.True(op.Compute(), op.ResultStatus.ToString());
            AssertSolid(op, "d = " + d);
            Assert.InRange(Volume(op.Result), Exact(d) * 0.99, Exact(d) * 1.01);
        }

        // an inset deeper than the half thickness leaves nothing, and is proven from the distances at the nodes: no refinement, Empty
        [Theory]
        [InlineData(-0.5)]
        [InlineData(-1.0)]
        public void InsetBeyondTheThickness_IsEmpty(double d)
        {
            var op = new MeshOffset(Box(), d);
            Assert.False(op.Compute());
            Assert.Equal("Empty", op.ResultStatus.ToString());
            Assert.Null(op.Result);
            Assert.Equal(0.25 * Math.Abs(d), op.UsedCellSize, 12);
        }

        // inputs that were not Empty before keep their mesh bit for bit (pinned on master), and pay nothing: the same cell
        [Theory]
        [InlineData("box", 0.1, 0.25, 0.0, "16156 97284B07F8495B7D", 0.025)]
        [InlineData("box", -0.1, 0.25, 0.0, "5056 F683297DA1AB0359", 0.025)]
        [InlineData("box", -0.22, 0.25, 0.0, "216 E7B58B7532EDF583", 0.055)]
        [InlineData("box", -0.1, 0.25, 0.0125, "20224 54FB7C717EAF26F5", 0.0125)]
        [InlineData("sphere", 0.1, 0.5, 0.0, "5044 00D6975BC3943AC2", 0.05)]
        [InlineData("sphere", -0.1, 0.5, 0.0, "2132 35749A3C05C44EC2", 0.05)]
        [InlineData("box", 0.3, 0.5, 0.0, "824 C5B2B2A7DC89DF61", 0.15)]
        public void InputsThatWereNotEmpty_KeepTheirMeshBitForBit(string shape, double d, double rel, double cell, string digest, double usedCell)
        {
            DMesh3 m = Box();
            if (shape == "sphere") {
                var g = new Sphere3Generator_NormalizedCube { Radius = 0.5, EdgeVertices = 4 };
                g.Generate();
                m = g.MakeDMesh();
            }
            var op = new MeshOffset(m, d) { RelativeCellSize = rel, CellSize = cell, UseParallel = false };
            Assert.True(op.Compute());
            Assert.Equal(digest, Digest(op.Result));
            Assert.Equal(usedCell, op.UsedCellSize, 12);
        }

        // invariance over Similarity.All(): the 2^k frames give the mesh of the unit frame scaled, bit for bit, with the same refinement (the same
        // cell relative to the distance); the other frames find the part as well or, the grid being another one, report that they did not, never Empty
        [Fact]
        public void Invariance_OverSimilarities()
        {
            DMesh3 input = Box();
            double d = -0.24;
            var unit = new MeshOffset(input, d);
            Assert.True(unit.Compute());
            string unitDigest = Digest(unit.Result);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(input, s);
                var op = new MeshOffset(m, d * s.Scale);
                op.Compute();
                string status = op.ResultStatus.ToString();
                Assert.NotEqual("Empty", status);
                if (s.IsExact) {
                    Assert.Equal("Success", status);
                    Assert.Equal(unit.UsedCellSize * s.Scale, op.UsedCellSize);
                    Assert.Equal(unitDigest, Digest(Invariance.MappedBack(op.Result, s)));
                } else if (status == "Success") {
                    // (a rotated slab of 0.02 on the grid of a cell of 0.03 is contoured only roughly: a closed solid of a volume of the right order)
                    AssertSolid(op, s.Name);
                    Assert.InRange(Volume(op.Result) / (s.Scale * s.Scale * s.Scale), Exact(d) * 0.05, Exact(d) * 1.5);
                }
            }
        }

        // reuse: Compute() again on one instance, and with the distance changed (Success after refinement, Empty, Success again)
        [Fact]
        public void Reuse_SameResult_AndTheReportsFollowTheLastCall()
        {
            var op = new MeshOffset(Box(), -0.24);
            Assert.True(op.Compute());
            string first = Digest(op.Result);
            double used = op.UsedCellSize;
            Assert.True(op.Compute());
            Assert.Equal(first, Digest(op.Result));
            Assert.Equal(used, op.UsedCellSize);
            op.Distance = -0.5;
            Assert.False(op.Compute());
            Assert.Equal("Empty", op.ResultStatus.ToString());
            Assert.Null(op.Result);
            Assert.Equal(0.125, op.UsedCellSize, 12);
            op.Distance = -0.24;
            Assert.True(op.Compute());
            Assert.Equal(first, Digest(op.Result));
        }

        // parallel and serial give the same mesh on the refined path, repeated
        [Fact]
        public void Parallel_EqualsSerial_OnTheRefinedPath()
        {
            var serial = new MeshOffset(Box(), -0.235) { UseParallel = false };
            Assert.True(serial.Compute());
            string expected = Digest(serial.Result);
            for (int i = 0; i < 2; ++i) {
                var par = new MeshOffset(Box(), -0.235) { UseParallel = true };
                Assert.True(par.Compute());
                Assert.Equal(expected, Digest(par.Result));
            }
        }
    }
}
