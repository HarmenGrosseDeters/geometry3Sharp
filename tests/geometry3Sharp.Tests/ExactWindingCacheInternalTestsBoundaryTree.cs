using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The clauses of the BoundaryTree bound of ExactWindingCache, one test each (docs/REVIEW_CHECKLIST.md 10a): the decision
    /// margin, the guards of the node query, the terms of the node's error against their formulas, the data a node stores
    /// against its build formulas, the gates of the build, and the fallbacks. The class name starts with
    /// ExactWindingCacheInternalTests so that the mutation shard's filter runs it.
    /// </summary>
    public class ExactWindingCacheInternalTestsBoundaryTree
    {
        static double Up(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + (x >= 0 ? 1 : -1)); }
        static double Down(double x) { return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) - (x > 0 ? 1 : -1)); }

        static ExactWindingCache Cache(DMesh3 mesh, int minThreshold = -1)
        {
            ExactWindingCache cache = new DMeshAABBTree3(mesh, true).GetExactWindingCache();
            if (minThreshold >= 0) cache.BoundaryTreeMinThreshold = minThreshold;
            cache.PrepareThresholdQueries();
            return cache;
        }

        static DMesh3 Grid(int n) { return ExactWindingAdoptionTests.LargeBoundaryOpenGrid(n); }

        // a convex polygon with the given number of sides, triangulated as a fan: sides boundary edges, one closed loop
        static void AppendPolygon(DMesh3 m, int sides, Vector3d center, double radius)
        {
            int c = m.AppendVertex(center);
            var ring = new int[sides];
            for (int i = 0; i < sides; ++i) {
                double a = 2 * Math.PI * i / sides;
                ring[i] = m.AppendVertex(center + new Vector3d(radius * Math.Cos(a), radius * Math.Sin(a), 0.1 * Math.Sin(3 * a)));
            }
            for (int i = 0; i < sides; ++i)
                m.AppendTriangle(c, ring[i], ring[(i + 1) % sides]);
        }

        // ---- the decision margin and its two-sided comparisons ----

        [Fact]
        public void TreeMargin_EqualsItsFormula_AndCoversItsBoundAndTheRounding()
        {
            // exact expected doubles, with the operands of every clause nonzero and of different size or sign
            double[] bErrs = { 1e-4, 0.0, 2e-7 };
            int[] chis = { -3, 0, 5 };
            double[] sums = { -0.25, 0.0, 0.75 };
            for (int i = 0; i < 3; ++i) {
                double expected = 1.01 * (bErrs[i] + 1e-9 + 4e-15 * (Math.Abs((double)chis[i]) + Math.Abs(sums[i])));
                double margin = ExactWindingCache.tree_margin(bErrs[i], chis[i], sums[i]);
                Assert.Equal(expected, margin);
                Assert.True(margin >= bErrs[i] + 1e-9 + 4e-15 * (Math.Abs((double)chis[i]) + Math.Abs(sums[i])));   // (the factor 1.01 only adds)
            }
            // the three clauses are each present
            double basis = ExactWindingCache.tree_margin(1e-4, -3, -0.25);
            Assert.True(ExactWindingCache.tree_margin(2e-4, -3, -0.25) > basis);       // bErr
            Assert.True(ExactWindingCache.tree_margin(1e-4, -4, -0.25) > basis);       // |chi|
            Assert.True(ExactWindingCache.tree_margin(1e-4, -3, -0.5) > basis);        // |bSum|
            Assert.True(ExactWindingCache.tree_margin(0.0, 0, 0.0) >= 1e-9);           // the absolute slack
        }

        [Fact]
        public void DecideAbove_BothSidesOfTheMargin()
        {
            double iso = 1.0, m = 0.25;
            Assert.Equal(0, ExactWindingCache.decide_above(1.25, iso, m));             // w - iso == margin: not decided
            Assert.Equal(1, ExactWindingCache.decide_above(Up(1.25), iso, m));
            Assert.Equal(0, ExactWindingCache.decide_above(0.75, iso, m));             // iso - w == margin
            Assert.Equal(-1, ExactWindingCache.decide_above(Down(0.75), iso, m));
            Assert.Equal(0, ExactWindingCache.decide_above(iso, iso, m));
            Assert.Equal(0, ExactWindingCache.decide_above(1.1, iso, m));              // above iso inside the margin
            Assert.Equal(0, ExactWindingCache.decide_above(0.9, iso, m));              // below iso inside the margin
            Assert.Equal(1, ExactWindingCache.decide_above(3.0, iso, m));
            Assert.Equal(-1, ExactWindingCache.decide_above(-3.0, iso, m));
            // a negative iso (EvaluateAbove(p, -0.25)) and a margin of 0
            Assert.Equal(1, ExactWindingCache.decide_above(-0.125, -0.25, 0.0));
            Assert.Equal(-1, ExactWindingCache.decide_above(-0.5, -0.25, 0.0));
            Assert.Equal(0, ExactWindingCache.decide_above(-0.25, -0.25, 0.0));
        }

        [Fact]
        public void NodeQueryGuards_AtTheirBoundaries()
        {
            Assert.Equal(0, ExactWindingCache.squared_distance_range(1.0));
            Assert.Equal(0, ExactWindingCache.squared_distance_range(1e140));
            Assert.Equal(-1, ExactWindingCache.squared_distance_range(Up(1e140)));
            Assert.Equal(0, ExactWindingCache.squared_distance_range(1e-140));
            Assert.Equal(1, ExactWindingCache.squared_distance_range(Down(1e-140)));
            Assert.Equal(1, ExactWindingCache.squared_distance_range(0.0));

            Assert.False(ExactWindingCache.ball_is_small(2.0, 8.0));                   // r == d / 4
            Assert.True(ExactWindingCache.ball_is_small(Down(2.0), 8.0));
            Assert.False(ExactWindingCache.ball_is_small(3.0, 8.0));

            Assert.False(ExactWindingCache.coord_eps_small(1e-6));
            Assert.True(ExactWindingCache.coord_eps_small(Down(1e-6)));
            Assert.False(ExactWindingCache.coord_eps_small(1e-3));

            Assert.False(ExactWindingCache.ray_outside_cap(0.5, 0.75, 0.25));          // minusX1U == cosA - eps
            Assert.True(ExactWindingCache.ray_outside_cap(Down(0.5), 0.75, 0.25));
            Assert.False(ExactWindingCache.ray_outside_cap(0.75, 0.75, 0.25));
            Assert.True(ExactWindingCache.ray_outside_cap(-0.5, 0.75, 0.25));
        }

        // ---- the terms of the node's error against their formulas ----

        [Fact]
        public void NodeTerms_EqualTheirFormulas()
        {
            ExactWindingCache cache = Cache(Grid(160));
            var rnd = new Random(41);
            int accepted = 0, withChord = 0;
            for (int k = 0; k < 12; ++k) {
                for (int t = 0; t < 40; ++t) {
                    Vector3d probe = new Vector3d(0.3, -0.2, 0.1);
                    ExactWindingCache.NodeInfo node = cache.DescribeBoundaryNode(k, probe);
                    if (node == null) break;
                    double dist = (8 + 40 * rnd.NextDouble()) * Math.Max(node.R, 1e-3);
                    Vector3d dir = new Vector3d(rnd.NextDouble() - 0.7, rnd.NextDouble() - 0.4, rnd.NextDouble() - 0.5);
                    Vector3d p = node.Center + dist * dir.Normalized + new Vector3d(0.013, -0.021, 0.034);
                    ExactWindingCache.NodeInfo info = cache.DescribeBoundaryNode(k, p);
                    if (info.Status != 1) continue;
                    ++accepted;
                    AssertNodeTerms(info, p, ref withChord);
                }
            }
            Assert.True(accepted > 40, $"only {accepted} accepted nodes");
            Assert.True(withChord > 20, $"only {withChord} with a chord of {accepted}");
        }

        static void AssertNodeTerms(ExactWindingCache.NodeInfo info, Vector3d p, ref int withChord)
        {
            double pm = Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z);
            double dx = info.Center.x - p.x, dy = info.Center.y - p.y, dz = info.Center.z - p.z;
            double dd = dx * dx + dy * dy + dz * dz;
            double dn = Math.Sqrt(dd);
            double d = dn * (1.0 - 1e-15);
            double r = info.R;
            double dr = d - r;
            double coordEps = 2.3e-16 * (pm + info.M1) / dr;
            double rd = r / d;
            double cosA = Math.Sqrt(1.0 - rd * rd);
            double minusX1U = dx / dn;
            double eps = 1e-9 + 16.0 * coordEps;
            ExactWindingCache.NodeQuery q = info.Query;
            Assert.Equal(d, q.D);
            Assert.Equal(dr, q.Dr);
            Assert.Equal(coordEps, q.CoordEps);
            Assert.Equal(cosA, q.CosA);
            Assert.Equal(minusX1U, q.MinusX1U);
            Assert.Equal(eps, q.Eps);
            Vector3d a0 = info.Vertices[0], ak = info.Vertices[info.Vertices.Length - 1];
            double chord = (a0.x == ak.x && a0.y == ak.y && a0.z == ak.z) ? 0.0 : ExactWindingCache.boundary_edge_angle_x(p, a0, ak);
            if (chord != 0.0) ++withChord;
            Assert.Equal(chord, q.Chord);
            double invD3 = 1.0 / (dd * dn);
            double vx = info.HalfVa.x, vy = info.HalfVa.y, vz = info.HalfVa.z;
            Assert.Equal(0.5 * (vx * dx + vy * dy + vz * dz) * invD3, q.HalfOmega);
            double absDx = Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
            double rem = info.AAbs * r / (dr * dr * dr);
            double vaErrTerm = 0.5 * info.VaErr * absDx * invD3;
            double dipScale = 0.5 * (Math.Abs(vx) + Math.Abs(vy) + Math.Abs(vz)) * absDx * invD3;
            Assert.Equal(rem, q.Rem);
            Assert.Equal(vaErrTerm, q.VaErrTerm);
            Assert.Equal(dipScale, q.DipScale);
            double nodeErr = rem + vaErrTerm + (1e-12 + 16.0 * coordEps) * dipScale + 1e-11 + 16.0 * coordEps + 1e-12 * (double)(info.Hi - info.Lo);
            Assert.Equal(nodeErr, q.NodeErr);
        }

        // ---- the data a node stores against its build formulas, and the shape of the tree ----

        [Fact]
        public void NodeData_EqualsItsBuildFormulas_AndTheTreeIsBalancedWithLeavesOfAtMostFourEdges()
        {
            // 128 x 128 grid: one closed boundary loop of 512 edges: nodes of 512, 256, ... 8 edges, and leaves of exactly 4
            ExactWindingCache cache = Cache(Grid(128));
            int count = cache.BoundaryNodeCount;
            Assert.True(count > 200);
            int leaves = 0, leafOfFour = 0;
            for (int k = 0; k < count; ++k) {
                ExactWindingCache.NodeInfo node = cache.DescribeBoundaryNode(k, new Vector3d(5, 6, 7));
                int edges = node.Hi - node.Lo;
                Assert.Equal(edges + 1, node.Vertices.Length);
                // leaf exactly when the chain has at most 4 edges; otherwise split at lo + (edges >> 1)
                if (edges <= 4) {
                    Assert.True(node.Left < 0 && node.Right < 0, $"node {k} with {edges} edges is not a leaf");
                    ++leaves;
                    if (edges == 4) ++leafOfFour;
                } else {
                    Assert.True(node.Left > k && node.Right > k, $"node {k} with {edges} edges is a leaf");
                    ExactWindingCache.NodeInfo l = cache.DescribeBoundaryNode(node.Left, Vector3d.Zero), r = cache.DescribeBoundaryNode(node.Right, Vector3d.Zero);
                    int mid = node.Lo + (edges >> 1);
                    Assert.Equal(node.Lo, l.Lo); Assert.Equal(mid, l.Hi);
                    Assert.Equal(mid, r.Lo); Assert.Equal(node.Hi, r.Hi);
                }
                if (k % 7 == 0 || edges <= 5)
                    AssertBuildFormulas(node);
            }
            Assert.True(leaves > 100 && leafOfFour > 100, $"{leaves} leaves, {leafOfFour} of four edges");
        }

        static void AssertBuildFormulas(ExactWindingCache.NodeInfo node)
        {
            Vector3d[] v = node.Vertices;
            int a = 0, b = v.Length - 1;
            int count = b - a + 1;
            Vector3d sum = Vector3d.Zero;
            for (int q = a; q <= b; ++q) sum += v[q];
            Vector3d c = sum / count;
            double r = 0.0, l1 = 0.0, abs = 0.0, prods = 0.0;
            Vector3d va = Vector3d.Zero;
            for (int q = a; q <= b; ++q) {
                Vector3d vq = v[q];
                Vector3d w = (q < b) ? v[q + 1] : v[a];
                Vector3d dv = vq - c, dw = w - c;
                double lv = dv.Length, lw = dw.Length;
                if (lv > r) r = lv;
                double vm1 = Math.Abs(vq.x) + Math.Abs(vq.y) + Math.Abs(vq.z);
                if (vm1 > l1) l1 = vm1;
                Vector3d cr = dv.Cross(dw);
                va += cr;
                abs += cr.Length;
                prods += lv * lw;
            }
            double vaError = 0.5 * prods * (count + 8) * 4.5e-16;
            Assert.Equal(c, node.Center);
            Assert.Equal(r * (1.0 + 1e-14), node.R);
            Assert.Equal(l1, node.M1);
            Assert.Equal(0.5 * abs + vaError, node.AAbs);
            Assert.Equal(vaError, node.VaErr);
            Assert.Equal(new Vector3d(0.5 * va.x, 0.5 * va.y, 0.5 * va.z), node.HalfVa);
        }

        // ---- the hook's guards ----

        [Fact]
        public void DescribeBoundaryNode_Guards()
        {
            ExactWindingCache cache = Cache(Grid(160));
            int count = cache.BoundaryNodeCount;
            Assert.True(count > 0);
            Assert.Null(cache.DescribeBoundaryNode(-1, Vector3d.Zero));
            Assert.Null(cache.DescribeBoundaryNode(count, Vector3d.Zero));
            Assert.NotNull(cache.DescribeBoundaryNode(count - 1, Vector3d.Zero));
            Assert.NotNull(cache.DescribeBoundaryNode(0, Vector3d.Zero));
            // a cache without a tree (closed mesh)
            ExactWindingCache closed = Cache(ExactWindingAdoptionTests.Sphere(10, 1.0));
            Assert.Null(closed.DescribeBoundaryNode(0, Vector3d.Zero));
            Assert.Equal(0, closed.BoundaryNodeCount);
            // the coordinate size pm of the hook is the L1 norm: a point with all three coordinates changes coordEps
            ExactWindingCache.NodeInfo n = cache.DescribeBoundaryNode(0, new Vector3d(100, 100, 100));
            Vector3d p = n.Center + new Vector3d(30, 40, 50);
            ExactWindingCache.NodeInfo i1 = cache.DescribeBoundaryNode(0, p);
            Assert.Equal(2.3e-16 * ((Math.Abs(p.x) + Math.Abs(p.y) + Math.Abs(p.z)) + n.M1) / i1.Query.Dr, i1.Query.CoordEps);
        }

        // ---- the full traversal against an independent one (the accumulations of sum and err, the leaves, the heavy edges) ----

        // the grid with a second sheet over some rim edges: unwelded triangles with the same directed rim edge (weight 2: heavy)
        static DMesh3 GridWithHeavyEdges(int n, int count)
        {
            DMesh3 m = Grid(n);
            var rim = new List<Index2i>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    int a = tri[j], b = tri[(j + 1) % 3];
                    int e = m.FindEdge(a, b);
                    if (m.IsBoundaryEdge(e)) rim.Add(new Index2i(a, b));
                }
            }
            for (int i = 0; i < count; ++i) {
                Index2i ab = rim[(i * 37) % rim.Count];
                Vector3d a = m.GetVertex(ab.a), b = m.GetVertex(ab.b);
                int va = m.AppendVertex(a), vb = m.AppendVertex(b), vc = m.AppendVertex(0.5 * (a + b) + new Vector3d(0.01 * (i + 1), 0.02, 0.3));
                m.AppendTriangle(va, vb, vc);      // (a unwelded sheet: its directed edge a -> b doubles the rim edge's weight)
            }
            return m;
        }

        static void ReferenceTraversal(ExactWindingCache cache, Vector3d p, out double bSum, out double bErr)
        {
            double sum = 0.0, err = 0.0;
            foreach (ExactWindingCache.BoundaryEdge e in cache.BoundaryTreeHeavy) {
                double wAbs = Math.Abs((double)e.Weight);
                sum += e.Weight * ExactWindingCache.boundary_edge_angle_x(p, e.A, e.B);
                err += 1e-12 * wAbs;
            }
            var stack = new Stack<int>();
            foreach (int root in cache.BoundaryTreeRoots) {
                stack.Push(root);
                while (stack.Count > 0) {
                    int k = stack.Pop();
                    ExactWindingCache.NodeInfo node = cache.DescribeBoundaryNode(k, p);
                    if (node.Left >= 0) {
                        Assert.True(node.Status >= 0, "the reference traversal needs queries inside the certified range");
                        if (node.Status > 0) {
                            sum += node.Query.Chord + node.Query.HalfOmega;
                            err += node.Query.NodeErr;
                            continue;
                        }
                        stack.Push(node.Right);
                        stack.Push(node.Left);
                    } else {
                        for (int s = 0; s < node.Vertices.Length - 1; ++s) {
                            sum += ExactWindingCache.boundary_edge_angle_x(p, node.Vertices[s], node.Vertices[s + 1]);
                            err += 1e-12;
                        }
                    }
                }
            }
            double sumOrderErr = 7.5e-16 * (double)cache.BoundaryCount * cache.BoundaryTreeTotalWeight;
            bSum = sum / MathUtil.TwoPI;
            bErr = 1.01 * (err + sumOrderErr) / MathUtil.TwoPI;
        }

        [Fact]
        public void TreeBoundarySum_EqualsAnIndependentTraversal_WithHeavyEdgesLeavesAndNodes()
        {
            foreach (DMesh3 mesh in new[] { Grid(160), GridWithHeavyEdges(160, 5) }) {
                ExactWindingCache cache = Cache(mesh);
                Assert.True(cache.HasBoundaryTree);
                bool heavy = cache.BoundaryTreeHeavy.Length > 0;
                Assert.Equal(mesh.TriangleCount > 51200 ? true : false, heavy);
                var rnd = new Random(12);
                int compared = 0, nearRim = 0;
                for (int i = 0; i < 80; ++i) {
                    // points far away and points close to the rim (leaves are visited near it)
                    Vector3d p = i % 2 == 0
                        ? new Vector3d(rnd.NextDouble() * 6 - 3, rnd.NextDouble() * 6 - 3, rnd.NextDouble() * 6 - 3)
                        : new Vector3d(rnd.NextDouble() * 2.2 - 1.1, (rnd.NextDouble() < 0.5 ? -1 : 1) * (1.0 + 0.01 * rnd.NextDouble()), 0.2 * rnd.NextDouble());
                    if (i % 2 == 1) ++nearRim;
                    if (!cache.TryEvaluateTreeBoundarySumX(p, out double bSum, out double bErr)) continue;
                    ReferenceTraversal(cache, p, out double rSum, out double rErr);
                    Assert.Equal(rSum, bSum);
                    Assert.Equal(rErr, bErr);
                    ++compared;
                }
                Assert.True(compared > 60 && nearRim > 30, $"only {compared} points compared");
            }
        }

        // ---- the fallbacks and the guards of the two threshold queries ----

        [Fact]
        public void ThresholdQueries_AtTheirOwnValue_AreNotAbove()
        {
            // iso == the exact value: the full sum (Evaluate) is the same expression as the final fallback, so w > iso is false
            // and a mutant with >= is true; the tree cannot decide inside its margin and falls back
            ExactWindingCache cache = Cache(Grid(160));
            ExactWindingCache small = Cache(ExactWindingAdoptionTests.Sphere(10, 1.0));    // no tree: Evaluate(p) > iso
            var rnd = new Random(2);
            int checkedPoints = 0;
            for (int i = 0; i < 60; ++i) {
                var p = new Vector3d(rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5, rnd.NextDouble() * 3 - 1.5);
                foreach (ExactWindingCache c in new[] { cache, small }) {
                    double w = c.Evaluate(p);
                    Assert.False(c.EvaluateAbove(p, w), $"EvaluateAbove at its own value {w:R}");
                    Assert.False(c.EvaluateAbsAbove(p, Math.Abs(w)), $"EvaluateAbsAbove at its own value {w:R}");
                    ++checkedPoints;
                }
            }
            Assert.Equal(120, checkedPoints);
        }

        [Fact]
        public void ThresholdQueries_EmptyNonFiniteAndSoup()
        {
            // an empty tree and a point that is not finite: 0 > iso
            var empty = new DMeshAABBTree3(new DMesh3(), true).GetExactWindingCache();
            ExactWindingCache grid = Cache(Grid(160));
            foreach (double iso in new[] { -1.0, 0.0, 1.0 }) {
                Assert.Equal(0.0 > iso, empty.EvaluateAbove(new Vector3d(0.5, 0.5, 0.5), iso));
                Assert.Equal(0.0 > iso, empty.EvaluateAbsAbove(new Vector3d(0.5, 0.5, 0.5), iso));
                Assert.Equal(0.0 > iso, grid.EvaluateAbove(new Vector3d(double.NaN, 0, 0), iso));
                Assert.Equal(0.0 > iso, grid.EvaluateAbsAbove(new Vector3d(0, double.PositiveInfinity, 0), iso));
            }
            // a true triangle soup (boundary edges >= triangles, more than 64 triangles): the direct sum, with its own thresholds
            var soup = new DMesh3();
            for (int i = 0; i < 70; ++i) {
                var o = new Vector3d(3 * (i % 10), 3 * (i / 10), 0.1 * i);
                soup.AppendTriangle(soup.AppendVertex(o), soup.AppendVertex(o + new Vector3d(1, 0, 0.2)), soup.AppendVertex(o + new Vector3d(0.1, 1, 0.1)));
            }
            ExactWindingCache sc = Cache(soup);
            Assert.True(sc.UseDirectSum);
            Assert.False(sc.HasBoundaryTree);
            var rnd = new Random(6);
            for (int i = 0; i < 40; ++i) {
                var p = new Vector3d(rnd.NextDouble() * 30, rnd.NextDouble() * 21, rnd.NextDouble() * 7);
                double w = sc.Evaluate(p);
                foreach (double iso in new[] { w, Down(Math.Abs(w)) , 0.0, 0.5, -0.5, w - 1e-3, w + 1e-3 }) {
                    Assert.Equal(w > iso, sc.EvaluateAbove(p, iso));
                    Assert.Equal(Math.Abs(w) > iso, sc.EvaluateAbsAbove(p, iso));
                }
                Assert.False(sc.EvaluateAbove(p, w));
                Assert.False(sc.EvaluateAbsAbove(p, Math.Abs(w)));
            }
            // iso that is not finite: the exact value decides
            Assert.True(grid.EvaluateAbove(new Vector3d(0.1, 0.2, 0.3), double.NegativeInfinity));
            Assert.False(grid.EvaluateAbove(new Vector3d(0.1, 0.2, 0.3), double.PositiveInfinity));
            Assert.True(grid.EvaluateAbsAbove(new Vector3d(0.1, 0.2, 0.3), double.NegativeInfinity));
            Assert.False(grid.EvaluateAbsAbove(new Vector3d(0.1, 0.2, 0.3), double.PositiveInfinity));
            Assert.False(grid.EvaluateAbove(new Vector3d(0.1, 0.2, 0.3), double.NaN));
        }

        // ---- the range guards of the query ----

        [Fact]
        public void TreeQuery_RangeGuards_ByTheSizeOfThePointAndOfTheMesh()
        {
            // a grid in the corner x in [0.5e65, 1e65] (the largest coordinate the build accepts): every node centre is within 1e70 of (1e70, 0, 0)
            DMesh3 big = Grid(160);
            foreach (int v in big.VertexIndices()) {
                Vector3d q = big.GetVertex(v);
                big.SetVertex(v, new Vector3d((0.75 + 0.25 * q.x) * 1e65, 0.25e65 * q.y, 0.25e65 * q.z));
            }
            ExactWindingCache cache = Cache(big);
            Assert.True(cache.HasBoundaryTree);
            // pm == 1e70 is inside the range: the tree is used
            Assert.True(cache.TryEvaluateTreeBoundarySumX(new Vector3d(1e70, 0, 0), out _, out _));
            // pm above 1e70 (the three components add up): outside; a little below: inside
            Assert.False(cache.TryEvaluateTreeBoundarySumX(new Vector3d(4e69, 4e69, 2.1e69), out _, out _));
            Assert.True(cache.TryEvaluateTreeBoundarySumX(new Vector3d(4e69, 4e69, 1.9e69), out _, out _));
            Assert.False(cache.TryEvaluateTreeBoundarySumX(new Vector3d(3e70, 0, 0), out _, out _));
            // pm == 1e70 but every node centre more than 1e70 away (dd above 1e140): the query leaves the certified range
            DMesh3 near = Grid(160);
            foreach (int v in near.VertexIndices()) {
                Vector3d q = near.GetVertex(v);
                near.SetVertex(v, new Vector3d((1.0 + 0.5 * q.x) * 1e64, 1e64 * q.y, 1e64 * q.z));
            }
            ExactWindingCache c2 = Cache(near);
            Assert.True(c2.HasBoundaryTree);
            Assert.False(c2.TryEvaluateTreeBoundarySumX(new Vector3d(-1e70, 0, 0), out _, out _));
            Assert.True(c2.TryEvaluateTreeBoundarySumX(new Vector3d(1e70, 0, 0), out _, out _));
        }

        // ---- the gates of the build ----

        [Fact]
        public void TreeBuild_Gates()
        {
            // the number of boundary edges against the threshold: a tree at exactly the threshold, none below it
            ExactWindingCache g16 = Cache(Grid(4), 16);
            Assert.Equal(16, g16.BoundaryCount);
            Assert.True(g16.HasBoundaryTree);                     // n == 16: enough edges
            Assert.False(Cache(Grid(4), 17).HasBoundaryTree);      // fewer than the threshold
            Assert.False(Cache(Grid(3), 12).HasBoundaryTree);      // 12 edges: below the build's own minimum of 16
            // a soup (direct sum) never builds a tree, whatever the threshold
            var soup = new DMesh3();
            for (int i = 0; i < 70; ++i) {
                var o = new Vector3d(3 * (i % 10), 3 * (i / 10), 0.1 * i);
                soup.AppendTriangle(soup.AppendVertex(o), soup.AppendVertex(o + new Vector3d(1, 0, 0.2)), soup.AppendVertex(o + new Vector3d(0.1, 1, 0.1)));
            }
            Assert.False(Cache(soup, 0).HasBoundaryTree);
            // coordinates: the largest 1e65 and the smallest 1e-65 are inside, beyond them there is no tree
            foreach (double scale in new[] { 1e65, 1e-65 }) {
                DMesh3 m = Grid(8);
                foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) * scale);
                Assert.True(Cache(m, 32).HasBoundaryTree, $"scale {scale}");
            }
            DMesh3 tooBig = Grid(8);
            foreach (int v in tooBig.VertexIndices()) tooBig.SetVertex(v, tooBig.GetVertex(v) * Up(1e65));
            Assert.False(Cache(tooBig, 32).HasBoundaryTree);
            DMesh3 tooSmall = Grid(8);
            foreach (int v in tooSmall.VertexIndices()) tooSmall.SetVertex(v, tooSmall.GetVertex(v) * Down(1e-65));
            Assert.False(Cache(tooSmall, 32).HasBoundaryTree);
            // one huge coordinate in each axis alone (flat grids at 2e65): no tree
            for (int axis = 0; axis < 3; ++axis) {
                DMesh3 m = Grid(8);
                foreach (int v in m.VertexIndices()) {
                    Vector3d q = m.GetVertex(v);
                    m.SetVertex(v, axis == 0 ? new Vector3d(2e65, q.x, q.y) : axis == 1 ? new Vector3d(q.x, 2e65, q.y) : new Vector3d(q.x, q.y, 2e65));
                }
                Assert.False(Cache(m, 32).HasBoundaryTree, $"axis {axis}");
            }
        }

        [Fact]
        public void TreeBuild_ShortChainsAreNotWorthATree()
        {
            // many small loops: the tree is skipped when the average chain has at most 4 edges (chains * 5 > edges)
            DMesh3 tri = new DMesh3();       // 6 triangular loops of 3 edges: 6 * 5 = 30 > 18
            for (int i = 0; i < 6; ++i) AppendPolygon(tri, 3, new Vector3d(3 * i, 0, 0), 1.0);
            Assert.False(Cache(tri, 0).HasBoundaryTree);
            DMesh3 pent = new DMesh3();      // 4 pentagonal loops of 5 edges: 4 * 5 == 20, not more: a tree
            for (int i = 0; i < 4; ++i) AppendPolygon(pent, 5, new Vector3d(3 * i, 0, 0), 1.0);
            ExactWindingCache pc = Cache(pent, 0);
            Assert.Equal(20, pc.BoundaryCount);
            Assert.True(pc.HasBoundaryTree);
        }
    }
}
