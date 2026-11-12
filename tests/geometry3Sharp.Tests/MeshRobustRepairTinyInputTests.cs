using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> the 62-byte input of #636 (MeshFuzzPilot dump) and the checks of a repaired result; base API only </summary>
    internal static class TinyRepairInputs
    {
        internal static readonly double[][] Oct = {
            new[] { 1.0, 0, 0 }, new[] { -1.0, 0, 0 }, new[] { 0.0, 1, 0 }, new[] { 0.0, 1, 0 },   // vertex 3 on vertex 2: two faces coincide, no volume
            new[] { 0.0, 0, 1 }, new[] { 0.0, 0, -1 } };
        internal static readonly int[][] OctTris = {
            new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };

        // the input of the issue (MeshFuzzPilot dump): the octahedron above at 2.407412430484045E-35
        internal static DMesh3 Mesh(double[][] v, int[][] t)
        {
            var m = new DMesh3();
            foreach (var p in v) m.AppendVertex(new Vector3d(p[0], p[1], p[2]));
            foreach (var x in t) m.AppendTriangle(x[0], x[1], x[2]);
            return m;
        }

        internal static DMesh3 Input(double scale = 1)
        {
            var m = new DMesh3();
            foreach (var p in Oct) m.AppendVertex(new Vector3d(p[0] * scale, p[1] * scale, p[2] * scale));
            foreach (var t in OctTris) m.AppendTriangle(t[0], t[1], t[2]);
            return m;
        }

        internal const double TinyScale = 2.407412430484045E-35;

        // a power of two that brings the diagonal into [1, 2): the thresholds of NotCleanReason (1e-12 of a squared diagonal) underflow far below
        internal static DMesh3 Normalized(DMesh3 mesh)
        {
            double d = mesh.GetBounds().DiagonalLength;
            double k = Math.Pow(2, -Math.Floor(Math.Log(d, 2)));
            var copy = new DMesh3(mesh);
            foreach (int v in copy.VertexIndices()) copy.SetVertex(v, copy.GetVertex(v) * k);
            return copy;
        }

        internal static void AssertRepaired(MeshRobustRepair r, bool ok)
        {
            Assert.True(ok, "Apply() " + r.ResultReason);
            Assert.Equal("", r.ResultReason);
            Assert.True(r.UsedAlphaWrap, "the case is about the wrap fallback");
            Assert.True(r.Mesh.IsClosed());
            Assert.True(AllFinite(r.Mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(Normalized(r.Mesh)));
        }

        internal static bool AllFinite(DMesh3 m) => m.VertexIndices().All(v => m.GetVertex(v).IsFinite);
    }

    /// <summary>
    /// #636, base API only (so this file compiles against the library before the fix, and fails there): the degenerate octahedron of 8
    /// triangles at 2.4e-35 falls through to the alpha wrap, which used to take 9 to 12 s for about 200 000 wrap triangles.
    /// </summary>
    public class MeshRobustRepairTinyInputTests
    {
        [Fact]
        public void TinyDegenerateOctahedron_IsRepairedWithAFewHundredWrapTriangles_NotTwoHundredThousand()
        {
            DMesh3 input = TinyRepairInputs.Input(TinyRepairInputs.TinyScale);
            var r = new MeshRobustRepair(new DMesh3(input));
            bool ok = r.Apply();
            TinyRepairInputs.AssertRepaired(r, ok);
            Assert.True(r.AlphaWrapTriangles < 5000, "wrap triangles " + r.AlphaWrapTriangles);   // master: 204 222
            Assert.True(r.Mesh.TriangleCount < 2000, "result triangles " + r.Mesh.TriangleCount);  // master: 11 884
        }

        // #712 (fuzz #624, group b): a tetrahedron flattened to 2^-46 of its other extents (MeshFuzzPilot dump), repaired of A alone;
        // master: 6.5 s, 136 018 wrap triangles
        [Fact]
        public void FlattenedTetrahedron_IsRepairedWithAFewHundredWrapTriangles()
        {
            double a = -1.1210387714598537E-44, b = 7.888609052210118E-31;
            var m = TinyRepairInputs.Mesh(new[] { new[] { a, 0.0, 0.0 }, new[] { 0.0, 0.0, 0.0 }, new[] { a, b, 0.0 }, new[] { a, 0.0, b } },
                new[] { new[] { 0, 2, 1 }, new[] { 0, 1, 3 }, new[] { 0, 3, 2 }, new[] { 1, 2, 3 } });
            var r = new MeshRobustRepair(m);
            bool ok = r.Apply();
            TinyRepairInputs.AssertRepaired(r, ok);
            Assert.True(r.AlphaWrapTriangles < 5000, "wrap triangles " + r.AlphaWrapTriangles);   // master: 136 018
        }

        // #712: two open cubes at about 1e-23 sharing corners (robust-repair-soup of the dump: A and B appended, 20 triangles);
        // master: 50 s, 869 654 wrap triangles
        [Fact]
        public void TwoOpenCubesAt1e23_AreRepairedWithAFewHundredWrapTriangles()
        {
            double lo = 9.512577044359818E-24, hi = 1.2821299494571929E-23;
            double[][] av = { new[] { lo, lo, lo }, new[] { hi, lo, lo }, new[] { hi, hi, lo }, new[] { lo, hi, lo }, new[] { lo, lo, hi }, new[] { hi, lo, hi }, new[] { hi, hi, hi }, new[] { lo, hi, hi } };
            var bv = av.Select(p => p.ToArray()).ToArray();
            bv[3] = new[] { 1.0546552810051103E-23, 1.3855275260263214E-23, 1.0546552810051103E-23 };
            int[][] t = { new[] { 4, 5, 6 }, new[] { 4, 6, 7 }, new[] { 0, 1, 5 }, new[] { 0, 5, 4 }, new[] { 1, 2, 6 }, new[] { 1, 6, 5 }, new[] { 2, 3, 7 }, new[] { 2, 7, 6 }, new[] { 3, 0, 4 }, new[] { 3, 4, 7 } };
            DMesh3 m = TinyRepairInputs.Mesh(av, t);
            new MeshEditor(m).AppendMesh(TinyRepairInputs.Mesh(bv, t));
            Assert.Equal(20, m.TriangleCount);
            var r = new MeshRobustRepair(m);
            bool ok = r.Apply();
            TinyRepairInputs.AssertRepaired(r, ok);
            Assert.True(r.AlphaWrapTriangles < 5000, "wrap triangles " + r.AlphaWrapTriangles);   // master: 869 654
        }
    }
}
