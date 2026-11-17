using System;
using g3;
using Xunit;

#pragma warning disable CS0618 // tests of the deprecated float MeshMeshCut (behaviour stays pinned)
namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Issue 856 on the old API only (it compiles and runs against the base library, where the default path throws "update_from_split: unsortable
    /// vertex?" at one scale): the refusal is switched on through reflection when the field exists.
    /// </summary>
    public class MeshMeshCutContactBaseTests
    {
        static DMesh3 Mesh(Vector3d[] v, (int, int, int)[] t)
        {
            var m = new DMesh3();
            foreach (var p in v) m.AppendVertex(p);
            foreach (var x in t) m.AppendTriangle(x.Item1, x.Item2, x.Item3);
            return m;
        }

        internal static DMesh3 Scaled(DMesh3 m, int e)
        {
            var c = new DMesh3(m);
            foreach (int v in c.VertexIndices()) { Vector3d p = c.GetVertex(v); c.SetVertex(v, new Vector3d(p.x * Math.Pow(2, e), p.y * Math.Pow(2, e), p.z * Math.Pow(2, e))); }
            return c;
        }

        internal static (DMesh3 a, DMesh3 b) Issue856()
        {
            var a = Mesh(new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),
                new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0.125, 1.4375, 7.625) },
                new[] { (0, 2, 1), (0, 3, 2), (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) });
            var b = Mesh(new[] { new Vector3d(0.01806640625, 0, 0), new Vector3d(0.01810455322265625, 2.47955322265625E-05, 1.1444091796875E-05),
                new Vector3d(0.018096923828125, 3.0517578125E-05, 0), new Vector3d(0.01806640625, 3.0517578125E-05, 0),
                new Vector3d(0.018133163452148438, 1.33514404296875E-05, 0.0001239776611328125), new Vector3d(0.018096923828125, 0, 3.0517578125E-05),
                new Vector3d(0.018096923828125, 3.0517578125E-05, 3.0517578125E-05), new Vector3d(0.01806640625, 3.0517578125E-05, 3.0517578125E-05) },
                new[] { (4, 5, 6), (4, 6, 7), (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) });
            return (a, b);
        }

        [Fact]
        public void FuzzCase_WithTheRefusalOnWhenItExists_NeverThrows()
        {
            var field = typeof(MeshMeshCut).GetField("RefuseContact");       // (absent on the base library: the default path runs, and throws at one scale)
            var (a, b) = Issue856();
            for (int e = -8; e <= 5; ++e) {
                DMesh3 target = Scaled(a, e), cut = Scaled(b, e);
                var op = new MeshMeshCut() { Target = target, CutMesh = cut };
                field?.SetValue(op, true);
                op.Compute();       // must not throw ("update_from_split: unsortable vertex?" at 2^-2 without the refusal)
            }
        }
    }
}
