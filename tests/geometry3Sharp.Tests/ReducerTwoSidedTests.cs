using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.MaxDeviationTwoSided (docs/design/BOUNDED_SIMPLIFICATION.md): the input stays within MaxDeviation of the
    /// result (h(M, S)), on top of MaxDeviation's h(S, M). Checked with MeshHausdorffDistance's certified bounds; the
    /// one-sided reduction of the same inputs is shown to violate h(M, S), so the tests exercise the option.
    /// </summary>
    public class ReducerTwoSidedTests
    {
        // a sphere with three narrow spikes of height 0.3 (a feature thinner than the tolerance), w the cap of 1 - cos
        static DMesh3 Spiky(int n, double w = 0.01)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            var dirs = new[] { new Vector3d(1, 0.2, 0.1).Normalized, new Vector3d(-0.3, 1, 0.2).Normalized, new Vector3d(0.1, -0.4, -1).Normalized };
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v).Normalized;
                double s = 1;
                foreach (Vector3d d in dirs) { double c = p.Dot(d); if (c > 1 - w) s += 0.3 * (c - (1 - w)) / w; }
                m.SetVertex(v, p * s);
            }
            return m;
        }

        // an open heightfield on [0, 1]^2: its rim is where the one-sided reduction pulls away from the input
        static DMesh3 Terrain(int n)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    double x = (double)i / n, y = (double)j / n;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, 0.08 * Math.Sin(6 * x) * Math.Cos(5 * y) + 0.01 * Math.Sin(40 * x + 13 * y)));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        static DMesh3 Reduce(DMesh3 input, double eps, bool twoSided, out Reducer r, int target = 4)
        {
            var m = new DMesh3(input);
            r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = twoSided };
            r.ReduceToTriangleCount(target);
            return m;
        }

        static MeshHausdorffDistance Hausdorff(DMesh3 from, DMesh3 to, double tol)
        {
            var h = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = tol };
            Assert.True(h.Compute());
            return h;
        }

        // both directions within eps (certified upper bounds, to tol), and the result is valid
        static void AssertTwoSided(DMesh3 input, DMesh3 result, double eps, string what)
        {
            double tol = 1e-3 * eps;
            var back = Hausdorff(input, result, tol);
            var fwd = Hausdorff(result, input, tol);
            Assert.True(back.UpperBound <= eps + tol, $"{what}: h(input, result) <= {back.UpperBound / eps} eps");
            Assert.True(fwd.UpperBound <= eps + tol, $"{what}: h(result, input) <= {fwd.UpperBound / eps} eps");
            Assert.True(result.CheckValidity(eFailMode: FailMode.ReturnOnly), what);
            foreach (int v in result.VertexIndices()) Assert.True(result.GetVertex(v).IsFinite, what);
            Assert.Equal(input.IsClosed(), result.IsClosed());
            if (input.IsClosed())
                Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
        }

        static void AssertSame(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            foreach (int v in a.VertexIndices()) Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        /// <summary> the one-sided reduction cuts the spikes and pulls away from the terrain's rim; the two-sided one does not </summary>
        /// (the cases are ones where the one-sided reduction provably leaves the input farther than eps: 1.015-1.18 eps)
        [Theory]
        [InlineData("spiky", 16, 1e-2)]
        [InlineData("spiky", 16, 3e-3)]
        [InlineData("terrain", 14, 1e-2)]
        [InlineData("terrain", 20, 1e-3)]
        public void TwoSided_InputStaysWithinTolerance(string name, int n, double rel)
        {
            DMesh3 input = name == "spiky" ? Spiky(n) : Terrain(n);
            double eps = rel * input.CachedBounds.DiagonalLength;
            DMesh3 one = Reduce(input, eps, false, out Reducer _);
            DMesh3 two = Reduce(input, eps, true, out Reducer r);
            Assert.True(Hausdorff(input, one, 1e-3 * eps).LowerBound > eps, "the one-sided reduction keeps the input within eps: the case does not exercise the option");
            AssertTwoSided(input, two, eps, name);
            // it still reduces as far as the bound allows: few collapses are lost against the one-sided reduction
            Assert.True(two.TriangleCount < input.TriangleCount && two.TriangleCount <= 1.1 * one.TriangleCount + 8, $"{two.TriangleCount} (one-sided {one.TriangleCount}) of {input.TriangleCount} triangles");
            Assert.True(r.reverseCoverage.Refused > 0 && r.reverseCoverage.Committed > 0);
        }

        /// <summary> the invariant itself: every input triangle is within eps of the output triangles it keeps, all current </summary>
        [Fact]
        public void TwoSided_EveryInputTriangleWithinItsSet()
        {
            DMesh3 input = Terrain(14);
            double eps = 1e-2 * input.CachedBounds.DiagonalLength;
            DMesh3 two = Reduce(input, eps, true, out Reducer r);
            Assert.True(two.TriangleCount < 0.5 * input.TriangleCount);
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int t in input.TriangleIndices()) {
                int[] deps = r.reverseCoverage.DependenciesOf(t);
                Assert.True(deps.Length > 0);
                var set = new DMesh3();
                foreach (int d in deps) {
                    Assert.True(two.IsTriangle(d), $"input {t}: {d} is not a triangle of the result");
                    two.GetTriVertices(d, ref a, ref b, ref c);
                    set.AppendTriangle(set.AppendVertex(a), set.AppendVertex(b), set.AppendVertex(c));
                }
                var single = new DMesh3();
                input.GetTriVertices(t, ref a, ref b, ref c);
                single.AppendTriangle(single.AppendVertex(a), single.AppendVertex(b), single.AppendVertex(c));
                var h = Hausdorff(single, set, 1e-3 * eps);
                Assert.True(h.UpperBound <= eps * (1 + 1e-3), $"input {t}: {h.UpperBound / eps} eps from its set");
            }
        }

        /// <summary> the certificate: a corner-only test would accept a triangle whose corners are near and whose middle is far </summary>
        [Fact]
        public void Within_ChecksTheInteriorNotOnlyTheCorners()
        {
            Vector3d p0 = new Vector3d(0, 0, 0), p1 = new Vector3d(1, 0, 0), p2 = new Vector3d(0, 1, 0);
            var corners = new List<Triangle3d>();
            foreach (Vector3d p in new[] { p0, p1, p2 })
                corners.Add(new Triangle3d(p + new Vector3d(-0.01, -0.01, 0), p + new Vector3d(0.01, -0.01, 0), p + new Vector3d(0, 0.01, 0)));
            var used = new List<int>();
            Assert.False(ReducerReverseCoverage.Within(p0, p1, p2, corners, 0.05, 6, used));
            // the same triangle under a plane at 0.04: within 0.05 (one candidate, by convexity), not within 0.03
            var plane = new List<Triangle3d> { new Triangle3d(new Vector3d(-1, -1, 0.04), new Vector3d(3, -1, 0.04), new Vector3d(-1, 3, 0.04)) };
            used.Clear();
            Assert.True(ReducerReverseCoverage.Within(p0, p1, p2, plane, 0.05, 0, used));
            Assert.Equal(new[] { 0 }, used.ToArray());
            Assert.False(ReducerReverseCoverage.Within(p0, p1, p2, plane, 0.03, 6, used));
            // two halves of the plane meeting under the triangle: no single one covers it, the subdivision (or covering) does
            var halves = new List<Triangle3d> {
                new Triangle3d(new Vector3d(-1, -1, 0.04), new Vector3d(3, -1, 0.04), new Vector3d(-1, 3, 0.04)),
                new Triangle3d(new Vector3d(3, -1, 0.04), new Vector3d(3, 3, 0.04), new Vector3d(-1, 3, 0.04)) };
            // (crosses the seam x + y = 2: corner sums 1.8, 2.1, 2.1)
            var small = new[] { new Vector3d(0.9, 0.9, 0), new Vector3d(1.2, 0.9, 0), new Vector3d(0.9, 1.2, 0) };
            used.Clear();
            Assert.False(ReducerReverseCoverage.Within(small[0], small[1], small[2], new List<Triangle3d> { halves[0] }, 0.05, 6, used));
            used.Clear();
            Assert.True(ReducerReverseCoverage.Within(small[0], small[1], small[2], halves, 0.05, 6, used));
            Assert.Contains(0, used); Assert.Contains(1, used);
            // without subdivision only the covering bound accepts this one: each corner lies on its own small candidate,
            // which is 1.49 or more from the other corners, and every point is within the circumradius 0.866 of a corner
            var q = new[] { new Vector3d(0, 0, 0), new Vector3d(1.5, 0, 0), new Vector3d(0.75, 0.75 * Math.Sqrt(3), 0) };
            var dots = new List<Triangle3d>();
            foreach (Vector3d p in q)
                dots.Add(new Triangle3d(p + new Vector3d(-0.01, -0.01, 0), p + new Vector3d(0.01, -0.01, 0), p + new Vector3d(0, 0.01, 0)));
            used.Clear();
            Assert.True(ReducerReverseCoverage.Within(q[0], q[1], q[2], dots, 1, 0, used));
            Assert.Equal(new[] { 0, 1, 2 }, used.OrderBy(k => k).ToArray());
            Assert.False(ReducerReverseCoverage.Within(q[0], q[1], q[2], dots, 0.8, 0, new List<int>()));
            Assert.False(ReducerReverseCoverage.Within(p0, p1, p2, new List<Triangle3d>(), 1, 6, new List<int>()));
        }

        /// <summary> invariance: the guarantee at every similarity, and the 2^k scales give the same reduction, scaled, bit for bit </summary>
        [Fact]
        public void TwoSided_Invariance()
        {
            DMesh3 input = Terrain(16);
            double eps = 1e-2 * input.CachedBounds.DiagonalLength;
            DMesh3 baseRes = Reduce(input, eps, true, out Reducer _);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 moved = new DMesh3(input);
                foreach (int v in moved.VertexIndices()) moved.SetVertex(v, sim.Apply(moved.GetVertex(v)));
                double e = sim.ApplyLength(eps);
                DMesh3 r = Reduce(moved, e, true, out Reducer _);
                AssertTwoSided(moved, r, e, sim.Name);
                Assert.True(r.TriangleCount < 0.5 * input.TriangleCount, $"{sim.Name}: {r.TriangleCount}");
                if (sim.IsExact) {
                    Assert.Equal(baseRes.TriangleCount, r.TriangleCount);
                    foreach (int v in baseRes.VertexIndices()) Assert.Equal(sim.Apply(baseRes.GetVertex(v)), r.GetVertex(v));
                }
            }
        }

        /// <summary> reuse: repeated reductions on one Reducer stay within eps of the original, in both directions </summary>
        [Fact]
        public void TwoSided_RepeatedReductions_StayWithinToleranceOfTheOriginal()
        {
            // (a terrain whose one-sided reduction leaves its rim farther than eps, small enough to stay well under 5 s)
            DMesh3 input = Terrain(14);
            double eps = 1e-2 * input.CachedBounds.DiagonalLength;
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true };
            r.ReduceToTriangleCount(input.TriangleCount / 2);
            int first = m.TriangleCount;
            AssertTwoSided(input, m, eps, "first");
            r.ReduceToEdgeLength(0.1 * input.CachedBounds.DiagonalLength);
            r.ReduceToTriangleCount(4);
            Assert.True(m.TriangleCount < first);
            AssertTwoSided(input, m, eps, "after three reductions");
        }

        /// <summary> determinism: the per-input certificates run in parallel; serial gives the same mesh, and so do repeated runs </summary>
        [Fact]
        public void TwoSided_ParallelEqualsSerial_AndRepeatable()
        {
            DMesh3 input = Spiky(16);
            double eps = 1e-2 * input.CachedBounds.DiagonalLength;
            DMesh3 p1 = Reduce(input, eps, true, out Reducer r1), p2 = Reduce(input, eps, true, out Reducer _), serial;
            gParallel.SerialOnThisThread = true;
            try { serial = Reduce(input, eps, true, out Reducer _); } finally { gParallel.SerialOnThisThread = false; }
            AssertSame(p1, p2);
            AssertSame(p1, serial);
            // (at 8 or more affected inputs a collapse certifies them through gParallel)
            Assert.True(r1.reverseCoverage.Certified >= 8 * (r1.reverseCoverage.Committed + r1.reverseCoverage.Refused), "the parallel path is taken");
        }

        /// <summary> off by default; with MaxDeviation 0 the option does nothing </summary>
        [Fact]
        public void TwoSided_OffByDefault_AndNoEffectWithoutMaxDeviation()
        {
            Assert.False(new Reducer(new DMesh3()).MaxDeviationTwoSided);
            DMesh3 input = Terrain(16);
            var a = new DMesh3(input); new Reducer(a).ReduceToTriangleCount(50);
            var b = new DMesh3(input); var rb = new Reducer(b) { MaxDeviationTwoSided = true }; rb.ReduceToTriangleCount(50);
            AssertSame(a, b);
            Assert.Null(rb.reverseCoverage);
        }

        /// <summary> the cases where the bound cannot be kept are refused with a clear message, before any change </summary>
        [Fact]
        public void TwoSided_RefusesWhatItCannotKeep()
        {
            DMesh3 input = Terrain(10);
            double eps = 3e-3 * input.CachedBounds.DiagonalLength;
            Action<Action<Reducer>, Action<Reducer>> refused = (setup, act) => {
                var m = new DMesh3(input);
                var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true };
                setup(r);
                int stamp = m.ShapeTimestamp;
                var ex = Assert.Throws<InvalidOperationException>(() => act(r));
                Assert.Contains("MaxDeviationTwoSided", ex.Message);
                Assert.Equal(stamp, m.ShapeTimestamp);
            };
            refused(r => r.SetProjectionTarget(new MeshProjectionTarget(new DMesh3(input), null)), r => r.FastCollapsePass(0.5));
            refused(r => r.SetDeviationReference(input), r => r.ReduceToTriangleCount(4));
            refused(r => { r.ReduceToTriangleCount(100); r.SetDeviationReference(input); }, r => r.ReduceToTriangleCount(4));
            refused(r => { r.MaxDeviationTwoSided = false; r.ReduceToTriangleCount(100); r.MaxDeviationTwoSided = true; }, r => r.ReduceToTriangleCount(4));
            refused(r => { r.ReduceToTriangleCount(100); r.MaxDeviation = 2 * eps; }, r => r.ReduceToTriangleCount(4));
            refused(r => { r.ReduceToTriangleCount(100); r.Mesh.SetVertex(r.Mesh.VertexIndices().First(), Vector3d.Zero); }, r => r.ReduceToTriangleCount(4));
            refused(r => r.SetProjectionTarget(new MeshProjectionTarget(new DMesh3(input), null)), r => r.ReduceToTriangleCount(4));
            Assert.Throws<ArgumentException>(() => new PartitionedReducer { Configure = r => r.MaxDeviationTwoSided = true }.Reduce(input, 1e-3));
        }

        static DMesh3 FastPass(DMesh3 input, double eps, bool twoSided, double len)
        {
            var m = new DMesh3(input);
            new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = twoSided }.FastCollapsePass(len, 5);
            return m;
        }

        /// <summary> FastCollapsePass (midpoint collapses of short edges) keeps the bound too; one-sided it cuts the spikes and the rim </summary>
        /// (cases where the one-sided pass provably leaves the input farther than eps: 1.03 and 1.37 eps)
        [Theory]
        [InlineData("spiky", 16)]
        [InlineData("terrain", 20)]
        public void TwoSided_FastCollapsePass_InputStaysWithinTolerance(string name, int n)
        {
            DMesh3 input = name == "spiky" ? Spiky(n) : Terrain(n);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 one = FastPass(input, eps, false, 0.05 * diag), two = FastPass(input, eps, true, 0.05 * diag);
            Assert.True(Hausdorff(input, one, 1e-3 * eps).LowerBound > eps, "the one-sided pass keeps the input within eps: the case does not exercise the option");
            AssertTwoSided(input, two, eps, name);
            Assert.True(two.TriangleCount < 0.5 * input.TriangleCount && two.TriangleCount <= 1.1 * one.TriangleCount + 8, $"{two.TriangleCount} (one-sided {one.TriangleCount}) of {input.TriangleCount} triangles");
        }

        /// <summary> reuse across entry points: a fast pass, then a quadric reduction on the same Reducer, both within eps of the original </summary>
        [Fact]
        public void TwoSided_FastCollapsePassThenReduction()
        {
            DMesh3 input = Terrain(14);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true };
            r.FastCollapsePass(0.2 * diag, 5);
            int afterFast = m.TriangleCount;
            Assert.True(afterFast < input.TriangleCount);
            AssertTwoSided(input, m, eps, "after the fast pass");
            r.ReduceToTriangleCount(4);
            Assert.True(m.TriangleCount < afterFast);
            AssertTwoSided(input, m, eps, "after the reduction");
        }

        /// <summary> invariance of the fast pass: the guarantee at every similarity, 2^k scales bit for bit </summary>
        [Fact]
        public void TwoSided_FastCollapsePass_Invariance()
        {
            DMesh3 input = Terrain(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 baseRes = FastPass(input, eps, true, 0.05 * diag);
            Assert.True(baseRes.TriangleCount < 0.5 * input.TriangleCount);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 moved = new DMesh3(input);
                foreach (int v in moved.VertexIndices()) moved.SetVertex(v, sim.Apply(moved.GetVertex(v)));
                DMesh3 r = FastPass(moved, sim.ApplyLength(eps), true, sim.ApplyLength(0.05 * diag));
                AssertTwoSided(moved, r, sim.ApplyLength(eps), sim.Name);
                if (sim.IsExact) {
                    Assert.Equal(baseRes.TriangleCount, r.TriangleCount);
                    foreach (int v in baseRes.VertexIndices()) Assert.Equal(sim.Apply(baseRes.GetVertex(v)), r.GetVertex(v));
                }
            }
        }

        // a subclass that edits the mesh in OnEdgeCollapse, and one with its own pass that calls CollapseEdge directly
        sealed class EditingReducer : Reducer
        {
            int n;
            public EditingReducer(DMesh3 m) : base(m) { }
            protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo info)
            {
                if (++n == 5) mesh.SetVertex(va, mesh.GetVertex(va));
            }
        }

        sealed class OwnPassReducer : Reducer
        {
            public OwnPassReducer(DMesh3 m) : base(m) { }
            public void CollapseFirstEdge() { CollapseEdge(mesh.EdgeIndices().First(), Vector3d.Zero, out int _); }
        }

        /// <summary> edits the coverage cannot see are refused when they happen, not silently passed </summary>
        [Fact]
        public void TwoSided_RefusesEditsBySubclasses()
        {
            DMesh3 input = Terrain(10);
            double eps = 3e-3 * input.CachedBounds.DiagonalLength;
            var e = new EditingReducer(new DMesh3(input)) { MaxDeviation = eps, MaxDeviationTwoSided = true };
            var ex = Assert.Throws<InvalidOperationException>(() => e.ReduceToTriangleCount(4));
            Assert.Contains("OnEdgeCollapse", ex.Message);
            var o = new OwnPassReducer(new DMesh3(input)) { MaxDeviation = eps, MaxDeviationTwoSided = true };
            ex = Assert.Throws<InvalidOperationException>(() => o.CollapseFirstEdge());
            Assert.Contains("MaxDeviationTwoSided", ex.Message);
        }

        /// <summary> a non-finite vertex elsewhere does not stop the option: the finite part reduces, both directions within eps </summary>
        [Fact]
        public void TwoSided_InputWithANonFiniteTriangle()
        {
            DMesh3 input = Terrain(14);
            DMesh3 finite = new DMesh3(input);
            double eps = 1e-2 * input.CachedBounds.DiagonalLength;
            int a = input.AppendVertex(new Vector3d(5, 5, 0)), b = input.AppendVertex(new Vector3d(6, 5, 0)), c = input.AppendVertex(new Vector3d(5, double.NaN, 0));
            int bad = input.AppendTriangle(a, b, c);
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true };
            r.ReduceToTriangleCount(4);
            Assert.True(m.IsTriangle(bad));
            m.RemoveTriangle(bad, true, false);
            Assert.True(m.TriangleCount < 0.5 * finite.TriangleCount, $"{m.TriangleCount}");
            AssertTwoSided(finite, m, eps, "finite part");
        }
    }
}
