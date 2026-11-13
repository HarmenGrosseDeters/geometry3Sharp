using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.flip_creates_feature_fold (MinTriangleQuality, issue #621) where the flip would give a degenerate triangle: it
    /// has no orientation to test folds against (the metric skips such a triangle, collapse_creates_flat rejects new ones),
    /// so it counts no fold and the flip is not rejected by this check.
    /// </summary>
    public class RemesherCoverageRound2Tests
    {
        // the quad a, b, c, d (a-b the diagonal, triangles [a, b, c] and [b, a, d]); every vertex a feature
        static (Remesher, DMesh3, int, int, int, int) Quad(Vector3d pa, Vector3d pb, Vector3d pc, Vector3d pd)
        {
            var m = new DMesh3();
            int a = m.AppendVertex(pa), b = m.AppendVertex(pb), c = m.AppendVertex(pc), d = m.AppendVertex(pd);
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, a, d);
            var r = new Remesher(m) { MinTriangleQuality = 0.2 };
            var cons = new MeshConstraints();
            var rim = MeshProjectionTarget.Auto(new DMesh3(m));
            foreach (int v in new[] { a, b, c, d })
                cons.SetOrUpdateVertexConstraint(v, new VertexConstraint(rim));
            r.SetExternalConstraints(cons);
            return (r, m, a, b, c, d);
        }

        static bool Degenerate(Vector3d p, Vector3d q, Vector3d s)
        {
            var t = new Triangle3d(p, q, s);
            return ExactTriangleIntersection.IsDegenerate(ref t);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void FlipToACollinearTriangle_CountsNoFold_AndIsNotRejectedByTheFoldCheck(int collinearWith)
        {
            // the flip of a-b replaces the triangles by [c, d, b] and [d, c, a]; with d on the line through c and b (variant 0),
            // or through c and a (variant 1), one of them is a segment
            Vector3d pa = new Vector3d(0, 0, 0), pb = new Vector3d(1, 0, 0), pc = new Vector3d(0, 1, 0);
            // (variant 0: c = (0, 1), b = (1, 0), d = (2, -1) on one line; variant 1: c, a, d on the line x = 0)
            Vector3d pd = collinearWith == 0 ? new Vector3d(2, -1, 0) : new Vector3d(0, -1, 0);
            var (r, m, a, b, c, d) = Quad(pa, pb, pc, pd);
            bool degenerateNew = collinearWith == 0 ? Degenerate(m.GetVertex(c), m.GetVertex(d), m.GetVertex(b)) : Degenerate(m.GetVertex(d), m.GetVertex(c), m.GetVertex(a));
            Assert.True(degenerateNew, "the flip must give a degenerate triangle");
            // (the old triangles are fine)
            Assert.False(Degenerate(m.GetVertex(a), m.GetVertex(b), m.GetVertex(c)));
            Assert.False(Degenerate(m.GetVertex(b), m.GetVertex(a), m.GetVertex(d)));

            int e = m.FindEdge(a, b);
            Index2i ev = m.GetEdgeV(e), ov = m.GetEdgeOpposingV(e), et = m.GetEdgeT(e);
            Assert.False(r.flip_creates_feature_fold(ev.a, ev.b, ov.a, ov.b, et.a, et.b, 10));
            // (the check leaves the mesh alone)
            Assert.Equal(2, m.TriangleCount);
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly));
        }
    }
}
