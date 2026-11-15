using System;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.KeepClearOf over the region a collapse sweeps (issue 743): testing only the new triangles let a collapse jump
    /// over a small part of the obstacle that they do not touch, leaving it outside (the wrap simplification's input).
    /// </summary>
    public class ReducerSweptClearTests
    {
        // the box [-1,1]^3 on a 13 x 13 grid per face, its top raised by up to 0.05 in a disk of radius 0.5; a small input
        // triangle just under the dome's flank: flattening the dome would leave it outside
        static DMesh3 BumpedBox()
        {
            var gen = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 13 };
            DMesh3 m = gen.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d x = m.GetVertex(v);
                if (x.z > 1 - 1e-9) {
                    double r2 = x.x * x.x + x.y * x.y;
                    m.SetVertex(v, new Vector3d(x.x, x.y, 1 + 0.05 * Math.Max(0, 1 - r2 / 0.25)));
                }
            }
            return m;
        }

        static readonly Vector3d[] Small = { new Vector3d(0.25, -0.06, 1.032), new Vector3d(0.25, 0.06, 1.032), new Vector3d(0.27, 0, 1.031) };

        static DMesh3 Obstacle(Similarity s)
        {
            var o = new DMesh3();
            o.AppendTriangle(o.AppendVertex(s.Apply(Small[0])), o.AppendVertex(s.Apply(Small[1])), o.AppendVertex(s.Apply(Small[2])));
            return o;
        }

        static DMesh3 Reduce(Similarity s, bool swept)
        {
            DMesh3 box = BumpedBox();
            MeshTransforms.PerVertexTransform(box, s.Apply);
            var r = new Reducer(box) { KeepClearOf = new DMeshAABBTree3(Obstacle(s), true), PreventSelfIntersections = true, KeepClearOfSwept = swept };
            r.ReduceToTriangleCount(12);
            return box;
        }

        static void AssertEncloses(DMesh3 m, Similarity s, string what)
        {
            Assert.True(m.IsClosed(), what + ": closed");
            var tree = new DMeshAABBTree3(m, true);
            foreach (Vector3d p in new[] { Small[0], Small[1], Small[2], (Small[0] + Small[1] + Small[2]) / 3 })
                Assert.True(Math.Abs(tree.WindingNumber(s.Apply(p)) - 1) < 0.01, $"{what}: {p} not enclosed");
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            var tris = new System.Collections.Generic.List<Triangle3d>();
            foreach (int t in m.TriangleIndices()) { m.GetTriVertices(t, ref a, ref b, ref c); tris.Add(new Triangle3d(a, b, c)); }
            Assert.False(Reducer.TrianglesTouch(new DMeshAABBTree3(Obstacle(s), true), tris), what + ": touches the obstacle");
        }

        [Fact]
        public void KeepClearOf_ACollapseCannotJumpOverTheObstacle()
        {
            Assert.True(BumpedBox().WindingNumber(Small[2]) > 0.5, "the obstacle starts inside");
            DMesh3 m = Reduce(Similarity.Identity, true);
            Assert.True(m.TriangleCount < 200, $"reduced: {m.TriangleCount}");
            AssertEncloses(m, Similarity.Identity, "swept");
            // the end state alone jumps over it (the case of issue 743)
            DMesh3 endOnly = Reduce(Similarity.Identity, false);
            Assert.True(endOnly.WindingNumber(Small[2]) < 0.5, "the end-state test alone left it outside");
        }

        // invariance over Similarity.All(): enclosed on every transformed input; the 2^k scales give the same result, scaled, bit for bit
        [Fact]
        public void KeepClearOf_Swept_Invariance()
        {
            DMesh3 b = Reduce(Similarity.Identity, true);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Reduce(s, true);
                AssertEncloses(m, s, s.Name);
                if (s.IsExact) {
                    Assert.Equal(b.TriangleCount, m.TriangleCount);
                    foreach (int v in b.VertexIndices())
                        Assert.Equal(s.Apply(b.GetVertex(v)), m.GetVertex(v));
                }
            }
        }

        // the tetrahedra test itself: a triangle inside, one crossing a face, one in a face, one outside
        [Fact]
        public void TetrahedraMeet_InsideCrossingInAFaceOutside()
        {
            var tet = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1) };
            Func<Vector3d, Vector3d, Vector3d, bool> meets = (p, q, r) => {
                var o = new DMesh3(); o.AppendTriangle(o.AppendVertex(p), o.AppendVertex(q), o.AppendVertex(r));
                return Reducer.TetrahedraMeet(new DMeshAABBTree3(o, true), tet);
            };
            Assert.True(meets(new Vector3d(0.1, 0.1, 0.1), new Vector3d(0.2, 0.1, 0.1), new Vector3d(0.1, 0.2, 0.1)), "inside");
            Assert.True(meets(new Vector3d(0.1, 0.1, -1), new Vector3d(0.2, 0.1, 1), new Vector3d(0.1, 0.2, 1)), "crossing");
            Assert.True(meets(new Vector3d(0.1, 0, 0.1), new Vector3d(0.2, 0, 0.1), new Vector3d(0.1, 0, 0.2)), "in the face y = 0");
            Assert.False(meets(new Vector3d(1, 1, 1), new Vector3d(2, 1, 1), new Vector3d(1, 2, 1)), "outside");
            Assert.False(Reducer.TetrahedraMeet(new DMeshAABBTree3(new DMesh3(), true), tet), "empty obstacle");
        }
    }
}
