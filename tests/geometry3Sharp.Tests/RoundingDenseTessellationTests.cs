using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A dense patch of tiny, well-shaped triangles (Exact Boolean 2.0 item L3, Thingi10K 119829/119830). Those
    /// models carry about 13.4k such triangles each: shortest/longest edge about 0.15, longest edge about 3.3e-6 of the
    /// diagonal. Their area is just under DegenerateArea times the result's squared diagonal, so SafeRounding contracts
    /// them, as decided (D', docs/design/SAFE_ROUNDING.md section 1). The drift accumulates in the patch, and the last few
    /// contractions would need slightly more than the bound. So the result is valid and closed but not clean, and no
    /// effort limit is reached: an honest "not fully repaired", not a hopeless repair.
    /// The patch is built procedurally: a unit sphere, refined around one point by longest-edge splits (new vertices
    /// on the sphere), with B rotated and translated as in the BooleanComparison harness. The patch lies outside B, so
    /// only the union keeps it. In A alone the patch is not degenerate; in the union, whose diagonal is larger, it is.
    /// </summary>
    public class RoundingDenseTessellationTests
    {
        const double Diagonal = 3.4641016151377544; // 2 sqrt(3), the unit sphere's box

        static DMesh3 DensePatchSphere(double radius, double edge)
        {
            var g = new Sphere3Generator_NormalizedCube() { EdgeVertices = 16, Radius = 1 };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            Vector3d p = new Vector3d(-1, -1, -1).Normalized;
            for (int pass = 0; pass < 200; ++pass) {
                var split = new List<(int e, double len)>();
                foreach (int e in m.EdgeIndices()) {
                    Index2i ev = m.GetEdgeV(e);
                    Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                    double len = a.Distance(b);
                    if (len > edge && ((a + b) * 0.5).Distance(p) < radius + len)
                        split.Add((e, len));
                }
                if (split.Count == 0)
                    break;
                foreach (var (e, _) in split.OrderByDescending(x => x.len).ThenBy(x => x.e)) {
                    if (!m.IsEdge(e))
                        continue;
                    Index2i ev = m.GetEdgeV(e);
                    if (m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) <= edge)
                        continue;
                    if (m.SplitEdge(e, out DMesh3.EdgeSplitInfo s) == MeshResult.Ok)
                        m.SetVertex(s.vNew, m.GetVertex(s.vNew).Normalized);
                }
            }
            return m;
        }

        static (DMesh3 a, DMesh3 b) Case()
        {
            DMesh3 a = DensePatchSphere(3e-5 * Diagonal, 3.3e-6 * Diagonal);
            DMesh3 b = new DMesh3(a);
            AxisAlignedBox3d box = a.CachedBounds;
            MeshTransforms.Rotate(b, box.Center, Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, 0.25 * new Vector3d(box.Width, box.Height, box.Depth));
            return (a, b);
        }

        [Fact]
        public void DensePatch_IsWellShapedAndNotDegenerateInTheInput()
        {
            var (a, _) = Case();
            Assert.True(a.IsClosed());
            double eps = 1e-12 * Diagonal * Diagonal;
            int tiny = 0;
            foreach (int t in a.TriangleIndices()) {
                Index3i f = a.GetTriangle(t);
                Vector3d p0 = a.GetVertex(f.a), p1 = a.GetVertex(f.b), p2 = a.GetVertex(f.c);
                double l0 = p1.Distance(p2), l1 = p2.Distance(p0), l2 = p0.Distance(p1);
                double longest = Math.Max(l0, Math.Max(l1, l2));
                if (longest < 1e-5 * Diagonal) {
                    ++tiny;
                    // well-shaped: twice the area over the longest edge squared (1 for a right isosceles triangle)
                    Assert.True(2 * a.GetTriArea(t) / (longest * longest) > 0.1, $"triangle {t} is not well-shaped");
                }
                Assert.True(a.GetTriArea(t) >= eps, $"triangle {t} is degenerate in the input");
            }
            Assert.True(tiny > 1000, $"{tiny} tiny triangles");
        }

        [Fact]
        public void DensePatch_Union_ValidNotCleanWithinTheBound()
        {
            var (a, b) = Case();
            var op = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, op.Status);
            ResultChecks.AssertClosed(op.Result);
            Assert.True(Invariance.AllFinite(op.Result));
            MeshRoundingRepair r = op.Rounding;
            // the patch counts as degenerate in the union, and most of it is contracted
            Assert.True(r.InitialDegenerate > 500, $"initial degenerate {r.InitialDegenerate}");
            Assert.InRange(r.RemainingDegenerate, 1, r.InitialDegenerate / 10);
            Assert.Equal(0, r.RemainingIntersections);
            Assert.Equal(0, r.RemainingFalseContacts);
            // not hopeless: no effort limit, and the edits left need just over the bound
            Assert.False(r.EvaluationLimitReached);
            Assert.True(r.SmallestRejectedDrift > r.MaxLength && r.SmallestRejectedDrift < 1.2 * r.MaxLength,
                $"smallest rejected drift {r.SmallestRejectedDrift / r.MaxLength} times the bound");
            Assert.True(r.MaxDrift <= r.MaxLength);
            Assert.False(op.RoundingClean);
            Assert.NotEqual("", MeshRobustRepair.NotCleanReason(op.Result));
        }

        [Fact]
        public void DensePatch_Union_StrictRounding_RoundingNotClean()
        {
            var (a, b) = Case();
            var op = new ExactMeshBoolean(a, b, Ops.Union) { StrictRounding = true };
            Assert.False(op.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, op.Status);
            Assert.Null(op.Result);
            Assert.True(op.Rounding.RemainingDegenerate > 0);
        }

        [Fact]
        public void DensePatch_IntersectionAndDifference_Clean()
        {
            // without the patch, or where the result's diagonal is A's, nothing is degenerate
            var (a, b) = Case();
            foreach (Ops o in new[] { Ops.Intersection, Ops.Difference }) {
                var op = new ExactMeshBoolean(a, b, o);
                Assert.True(op.Compute(), o.ToString());
                Assert.True(op.RoundingClean, o.ToString());
                ResultChecks.AssertClean(op.Result, o.ToString());
            }
        }

        [Fact]
        public void DensePatch_Union_SameResultTwiceAndOnOneInstance()
        {
            var (a, b) = Case();
            var op = new ExactMeshBoolean(a, b, Ops.Union);
            Assert.True(op.Compute());
            string first = MeshMinimizer.ToObj(op.Result);
            int remaining = op.Rounding.RemainingDegenerate;
            Assert.True(op.Compute());
            Assert.Equal(first, MeshMinimizer.ToObj(op.Result));
            Assert.Equal(remaining, op.Rounding.RemainingDegenerate);
            var (a2, b2) = Case();
            var again = new ExactMeshBoolean(a2, b2, Ops.Union);
            Assert.True(again.Compute());
            Assert.Equal(first, MeshMinimizer.ToObj(again.Result));
        }
    }
}
