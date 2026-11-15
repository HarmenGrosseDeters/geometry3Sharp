using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    // (its own file: it needs the internal Reducer.TrianglesTouch, so the other new tests still compile against the base library)
    public class ReducerTrianglesTouchTests
    {
        [Fact]
        public void TrianglesTouch_FindsContactInAnAxisPlaneAndAtASharedPoint()
        {
            // the obstacle lies in the plane x = 0: its boxes are flat, which an open box test never overlaps
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(new Vector3d(0, 0, 0)), m.AppendVertex(new Vector3d(0, 1, 0)), m.AppendVertex(new Vector3d(0, 0, 1)));
            var tree = new DMeshAABBTree3(m, true);
            // coplanar and overlapping
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(0, 0.1, 0.1), new Vector3d(0, 0.3, 0.1), new Vector3d(0, 0.1, 0.3)) }));
            // only a corner in common, the rest on one side of the plane
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(0, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(-1, -1, 0)) }));
            // crossing the plane
            Assert.True(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(-0.5, 0.2, 0.2), new Vector3d(0.5, 0.2, 0.2), new Vector3d(0, 0.4, 0.4)) }));
            // parallel, apart
            Assert.False(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(1e-3, 0.1, 0.1), new Vector3d(1e-3, 0.3, 0.1), new Vector3d(1e-3, 0.1, 0.3)) }));
            Assert.False(Reducer.TrianglesTouch(tree, new[] { new Triangle3d(new Vector3d(2, 2, 2), new Vector3d(3, 2, 2), new Vector3d(2, 3, 2)) }));
        }

        // degenerate obstacle triangles (collinear corners: a needle, or all three at one point) are segments or points
        // of the obstacle; the test used to skip them, so a wrap simplification (KeepClearOf = the input) could cut a
        // needle of the input and leave part of it outside
        [Fact]
        public void TrianglesTouch_FindsDegenerateObstacleTriangles()
        {
            Assert.True(Reducer.TrianglesTouch(Needle(new Vector3d(0.2, 0.2, -1), new Vector3d(0.2, 0.2, 1)), new[] { UnitXY() }), "needle through the triangle");
            Assert.True(Reducer.TrianglesTouch(Needle(new Vector3d(0.2, 0.2, 0), new Vector3d(0.2, 0.2, 1)), new[] { UnitXY() }), "needle ending on the triangle");
            Assert.True(Reducer.TrianglesTouch(Needle(new Vector3d(-1, 0.2, 0), new Vector3d(1, 0.2, 0)), new[] { UnitXY() }), "needle in the triangle's plane");
            Assert.True(Reducer.TrianglesTouch(Needle(new Vector3d(0.2, 0.2, 0), new Vector3d(0.2, 0.2, 0)), new[] { UnitXY() }), "point on the triangle");
            Assert.False(Reducer.TrianglesTouch(Needle(new Vector3d(0.2, 0.2, 1e-9), new Vector3d(0.2, 0.2, 1)), new[] { UnitXY() }), "needle above the triangle");
            Assert.False(Reducer.TrianglesTouch(Needle(new Vector3d(0.7, 0.7, -1), new Vector3d(0.7, 0.7, 1)), new[] { UnitXY() }), "needle beside the triangle");
        }

        // the same answers on the transformed inputs (exact predicates: every similarity, not only the 2^k scales)
        [Fact]
        public void TrianglesTouch_DegenerateObstacle_Invariance()
        {
            var cases = new (Vector3d, Vector3d, bool)[] {
                (new Vector3d(0.2, 0.2, -1), new Vector3d(0.2, 0.2, 1), true),
                (new Vector3d(-1, 0.25, 0), new Vector3d(1, 0.25, 0), true),
                (new Vector3d(0.2, 0.2, 0.5), new Vector3d(0.2, 0.2, 1), false),
                (new Vector3d(0.7, 0.7, -1), new Vector3d(0.7, 0.7, 1), false) };
            foreach (Similarity sim in Similarity.All()) {
                Triangle3d t = UnitXY();
                var tt = new Triangle3d(sim.Apply(t.V0), sim.Apply(t.V1), sim.Apply(t.V2));
                foreach (var (p, q, expected) in cases) {
                    // the needle's middle corner computed from the transformed ends, so it stays (close to) collinear
                    bool hit = Reducer.TrianglesTouch(Needle(sim.Apply(p), sim.Apply(q)), new[] { tt });
                    // an in-plane needle after a rotation is generally not exactly in the plane any more: only crossing/apart cases there
                    if (sim.IsExact || p.z != q.z || expected == false) Assert.True(expected == hit, $"{sim}: {p} {q}");
                }
            }
        }

        // the wrap simplification's case: a bump on the top of a box with a needle of the input just under its crest;
        // flattening the bump would leave the needle outside. With KeepClearOf the reduction must keep it enclosed.
        [Fact]
        public void Reducer_KeepClearOf_KeepsADegenerateInputTriangleEnclosed()
        {
            DMesh3 box = BumpedBox();
            Vector3d p0 = new Vector3d(0.25, 0, 0), p1 = new Vector3d(0.25, 0, 1.032);
            Assert.True(box.WindingNumber(p0) > 0.5 && box.WindingNumber(p1) > 0.5, "the needle starts inside");
            var r = new Reducer(box) { KeepClearOf = Needle(p0, p1), PreventSelfIntersections = true };
            r.ReduceToTriangleCount(12);
            Assert.True(box.TriangleCount < 400, $"reduced: {box.TriangleCount}");
            Assert.True(box.IsClosed(), "closed");
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in box.TriangleIndices()) {
                box.GetTriVertices(t, ref a, ref b, ref c);
                var tri = new Triangle3d(a, b, c);
                Assert.False(ExactTriangleIntersection.SegmentIntersectsTriangle(p0, p1, ref tri), $"triangle {t} cuts the needle");
            }
            for (int k = 0; k <= 4; ++k)
                Assert.True(box.WindingNumber(Vector3d.Lerp(p0, p1, k / 4.0)) > 0.5, $"needle point {k}/4 outside");
        }

        static Triangle3d UnitXY() => new Triangle3d(Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0));

        static DMeshAABBTree3 Needle(Vector3d p, Vector3d q)
        {
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(p), m.AppendVertex(q), m.AppendVertex(0.5 * p + 0.5 * q));
            return new DMeshAABBTree3(m, true);
        }

        // the box [-1,1]^3 on a 21 x 21 grid per face, its top raised by up to 0.05 in a disk of radius 0.5
        static DMesh3 BumpedBox()
        {
            var gen = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = 21 };
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
    }
}
