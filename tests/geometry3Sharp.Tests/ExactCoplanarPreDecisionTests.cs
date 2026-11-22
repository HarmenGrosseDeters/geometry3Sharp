using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The pre-decision of coplanar pairs (ExactMeshArrangement.meets_only_at_shared, PreDecideCoplanar; docs/perf/BOOLEAN_CHAINS_PROBE_2026-11.md):
    /// two coplanar triangles that meet only in their shared corners or shared edge are not clipped, since the clips would add only corners and
    /// boundary edges. The decision is checked against the clips themselves (the arrangement with the pre-decision off), and the result of booleans
    /// that use it against the result without it, bit for bit.
    /// </summary>
    public class ExactCoplanarPreDecisionTests
    {
        static DMesh3 OneTriangle(Vector3d a, Vector3d b, Vector3d c)
        {
            var m = new DMesh3();
            int va = m.AppendVertex(a), vb = m.AppendVertex(b), vc = m.AppendVertex(c);
            m.AppendTriangle(va, vb, vc);
            return m;
        }

        // grid point (x, y) of the plane z = ax + by (exact on small integers, also scaled by a power of two)
        static Vector3d P(int x, int y, int mode, double scale)
        {
            double z = mode == 0 ? 0 : mode == 1 ? x + y : 3 * x - 2 * y;
            return new Vector3d(x * scale, y * scale, z * scale);
        }

        // what the clips left behind (PreDecideCoplanar = false): nothing but corners of the triangle and boundary edges between two of its corners
        static bool OnlyCornersAndBoundary(ExactMeshArrangement arr, ExactMeshArrangement.InputTriangle t)
        {
            Index3i c = t.Corners;
            bool corner(int p) { p = arr.Canonical(p); return p == arr.Canonical(c.a) || p == arr.Canonical(c.b) || p == arr.Canonical(c.c); }
            foreach (int p in t.Points)
                if (corner(p) == false)
                    return false;
            foreach (var s in t.Segments)
                if (corner(s.P) == false || corner(s.Q) == false)
                    return false;
            return true;
        }

        static ExactMeshArrangement Arrange(DMesh3 a, DMesh3 b, bool preDecide)
        {
            var arr = new ExactMeshArrangement(new List<DMesh3> { a, b }) { PreDecideCoplanar = preDecide };
            arr.Compute();
            return arr;
        }

        [Theory]
        [InlineData(0, 1.0)]
        [InlineData(1, 1.0)]
        [InlineData(2, 0.125)]
        [InlineData(1, 1024.0)]
        public void Decision_EqualsTheClips_OnAllGridPairsThatShareCorners(int mode, double scale)
        {
            // every triangle of the 3 x 3 grid points (non-degenerate, both orientations), in pairs that share 1, 2 or 3 corners
            var pts = new List<(int, int)>();
            for (int x = 0; x < 3; ++x) for (int y = 0; y < 3; ++y) pts.Add((x, y));
            var tris = new List<(int, int, int)>();
            for (int i = 0; i < pts.Count; ++i)
                for (int j = 0; j < pts.Count; ++j)
                    for (int k = 0; k < pts.Count; ++k) {
                        if (i == j || j == k || i == k) continue;
                        long cross = (long)(pts[j].Item1 - pts[i].Item1) * (pts[k].Item2 - pts[i].Item2) - (long)(pts[j].Item2 - pts[i].Item2) * (pts[k].Item1 - pts[i].Item1);
                        if (cross != 0 && i < j && j < k) tris.Add((i, j, k));
                    }
            var rnd = new Random(17 + mode);
            var seen = new int[4, 2];
            int checkedPairs = 0;
            for (int n = 0; n < 700; ++n) {
                var t = tris[rnd.Next(tris.Count)];
                var u = tris[rnd.Next(tris.Count)];
                var ta = new[] { t.Item1, t.Item2, t.Item3 };
                var ua = new[] { u.Item1, u.Item2, u.Item3 };
                if (rnd.Next(2) == 0) Array.Reverse(ua);
                int shared = ua.Count(q => ta.Contains(q));
                if (shared == 0) continue;
                DMesh3 mt = OneTriangle(P(pts[ta[0]].Item1, pts[ta[0]].Item2, mode, scale), P(pts[ta[1]].Item1, pts[ta[1]].Item2, mode, scale), P(pts[ta[2]].Item1, pts[ta[2]].Item2, mode, scale));
                DMesh3 mu = OneTriangle(P(pts[ua[0]].Item1, pts[ua[0]].Item2, mode, scale), P(pts[ua[1]].Item1, pts[ua[1]].Item2, mode, scale), P(pts[ua[2]].Item1, pts[ua[2]].Item2, mode, scale));
                var clipped = Arrange(mt, mu, false);
                var T = clipped.Triangles[0]; var U = clipped.Triangles[1];
                bool left = OnlyCornersAndBoundary(clipped, T) && OnlyCornersAndBoundary(clipped, U);
                bool decided = clipped.meets_only_at_shared(T, U);
                Assert.True(decided == left, $"shared {shared}, triangles {string.Join(",", ta)} / {string.Join(",", ua)}: decision {decided}, clips left only corners and boundary edges: {left}");
                seen[shared, decided ? 1 : 0]++;
                checkedPairs++;
            }
            Assert.True(checkedPairs > 300);
            // every branch and both answers of each were exercised
            for (int shared = 1; shared <= 3; ++shared)
                Assert.True(seen[shared, 1] > 0, $"no pair with {shared} shared corners was decided");
            Assert.True(seen[1, 0] > 0 && seen[2, 0] > 0, "no undecided pair with 1 or 2 shared corners");
        }

        [Fact]
        public void Defaults_PreDecisionIsOn_InTheArrangementAndTheBoolean()
        {
            // kills a mutant of either default: nothing else leaves the flag unset (ExactMeshBoolean always passes its own to the arrangement)
            Func<int, int, Vector3d> p = (x, y) => new Vector3d(x, y, 0);
            var arr = new ExactMeshArrangement(new List<DMesh3> {
                OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 2), p(2, 0), p(0, 0)) });
            Assert.True(arr.PreDecideCoplanar);
            arr.Compute();
            Assert.Equal(1, arr.PreDecidedPairs);   // the same triangle twice: three shared corners, decided without clipping

            Assert.True(new ExactMeshBoolean(OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 0), p(2, 0), p(0, 2)),
                ExactMeshBoolean.Operations.Union).ArrangementPreDecideCoplanar);
        }

        [Fact]
        public void Decision_AllCasesOfTheThreeBranches()
        {
            Func<DMesh3, DMesh3, bool> decide = (a, b) => { var arr = Arrange(a, b, false); return arr.meets_only_at_shared(arr.Triangles[0], arr.Triangles[1]); };
            Func<int, int, Vector3d> p = (x, y) => new Vector3d(x, y, 0);
            // 3 shared corners: the same triangle, also with the other orientation
            Assert.True(decide(OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 0), p(2, 0), p(0, 2))));
            Assert.True(decide(OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 2), p(2, 0), p(0, 0))));
            // 2 shared corners (edge (0,0)-(2,0)): third corners on opposite sides, on the same side (overlap, one inside the other)
            Assert.True(decide(OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 0), p(2, 0), p(1, -1))));
            Assert.False(decide(OneTriangle(p(0, 0), p(2, 0), p(0, 2)), OneTriangle(p(0, 0), p(2, 0), p(2, 2))));
            Assert.False(decide(OneTriangle(p(0, 0), p(2, 0), p(0, 4)), OneTriangle(p(0, 0), p(2, 0), p(1, 1))));
            // 1 shared corner (0,0): wedges apart, the same wedge side by side, nested, overlapping, sharing a ray, and opposite (vertical) wedges
            Assert.True(decide(OneTriangle(p(0, 0), p(2, 0), p(2, 1)), OneTriangle(p(0, 0), p(-2, 1), p(-1, 2))));
            Assert.True(decide(OneTriangle(p(0, 0), p(2, 0), p(2, 1)), OneTriangle(p(0, 0), p(-2, 0), p(-2, -1))));
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(0, 4)), OneTriangle(p(0, 0), p(2, 1), p(1, 2))));
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(0, 4)), OneTriangle(p(0, 0), p(3, 3), p(-1, 3))));
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(4, 1)), OneTriangle(p(0, 0), p(2, 0), p(2, -1))));
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(0, 4)), OneTriangle(p(0, 0), p(1, 3), p(-3, 1))));
            // wedges that share their first ray, and one that starts on the last ray of the other
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(4, 1)), OneTriangle(p(0, 0), p(2, 0), p(2, 1))));
            Assert.False(decide(OneTriangle(p(0, 0), p(4, 0), p(4, 4)), OneTriangle(p(0, 0), p(2, 2), p(0, 3))));
            Assert.False(decide(OneTriangle(p(0, 0), p(2, 2), p(0, 3)), OneTriangle(p(0, 0), p(4, 0), p(4, 4))));
            // a wedge that ends on the ray where the other starts: with the corner (4,4) on both it is the shared-edge case (meets only in the edge)
            Assert.True(decide(OneTriangle(p(0, 0), p(4, 0), p(4, 4)), OneTriangle(p(0, 0), p(4, 4), p(0, 4))));
            // no shared corner: never decided (the clips decide)
            Assert.False(decide(OneTriangle(p(0, 0), p(1, 0), p(0, 1)), OneTriangle(p(5, 5), p(6, 5), p(5, 6))));
        }

        [Fact]
        public void DecidedPair_AddsNothing_ButIsStillACoplanarPair()
        {
            // two triangles of one flat face, side by side (the shared edge (2,0)-(0,2)), one from each mesh
            DMesh3 a = OneTriangle(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0));
            DMesh3 b = OneTriangle(new Vector3d(2, 0, 0), new Vector3d(2, 2, 0), new Vector3d(0, 2, 0));
            var on = Arrange(a, b, true);
            var off = Arrange(a, b, false);
            Assert.Equal(off.IntersectingPairs, on.IntersectingPairs);
            Assert.Equal(1, on.IntersectingPairs);
            Assert.Equal(off.Triangles[0].Coplanar, on.Triangles[0].Coplanar);
            Assert.Equal(off.Triangles[1].Coplanar, on.Triangles[1].Coplanar);
            Assert.Equal(new[] { 1 }, on.Triangles[0].Coplanar.ToArray());
            Assert.Empty(on.Triangles[0].Segments);
            Assert.Empty(on.Triangles[1].Segments);
            Assert.NotEmpty(off.Triangles[0].Segments);       // the clips left the shared boundary edge
            Assert.Equal(off.Output.Count, on.Output.Count);
        }

        // ---------------------------------------------------------------- booleans on flat-faced chains

        static DMesh3 FlatBox(Similarity s, double cx, double cy, double cz, double hx, double hy, double hz, int n)
        {
            var g = new GridBox3Generator { Box = new Box3d(new Vector3d(cx, cy, cz), new Vector3d(hx, hy, hz)), EdgeVertices = n };
            DMesh3 m = g.Generate().MakeDMesh();
            foreach (int vid in m.VertexIndices().ToArray())
                m.SetVertex(vid, s.Apply(m.GetVertex(vid)));
            return m;
        }

        static string Hash(DMesh3 mesh)
        {
            var sb = new StringBuilder();
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                sb.Append(vid).Append(':').Append(BitConverter.DoubleToInt64Bits(v.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(v.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(v.z)).Append(';');
            }
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                sb.Append(tid).Append(':').Append(t.a).Append(',').Append(t.b).Append(',').Append(t.c).Append(';');
            }
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        // step 3 of a chain: A, R = A + B (rounded, as the chain does), then R op A: R holds A's surface again (the case the decision is for)
        static (ExactMeshBoolean b, DMesh3 result) Step3(Similarity s, ExactMeshBoolean.Operations op, bool preDecide, bool serial = false)
        {
            DMesh3 a = FlatBox(s, 0, 0, 0, 1, 1, 1, 5);
            DMesh3 b = FlatBox(s, 0.5, 0.25, 1, 0.75, 0.5, 0.5, 4);
            var first = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union) { ArrangementPreDecideCoplanar = preDecide };
            Assert.True(first.Compute());
            DMesh3 r = first.Result;
            var step = new ExactMeshBoolean(r, a, op) { ArrangementPreDecideCoplanar = preDecide };
            bool wasSerial = gParallel.SerialOnThisThread;
            gParallel.SerialOnThisThread = serial;
            try { Assert.True(step.Compute()); } finally { gParallel.SerialOnThisThread = wasSerial; }
            return (step, step.Result);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Step3OfAChain_IsTheSameWithAndWithoutThePreDecision(string name)
        {
            Similarity s = Similarity.ByName(name);
            foreach (var op in new[] { ExactMeshBoolean.Operations.Intersection, ExactMeshBoolean.Operations.Difference, ExactMeshBoolean.Operations.Union }) {
                var on = Step3(s, op, true);
                var off = Step3(s, op, false);
                Assert.Equal(Hash(off.result), Hash(on.result));
                // the flag reaches the arrangement and the decision fires in step 3 (a vacuous A/B would pass the equalities alone)
                Assert.Equal(0, off.b.ArrangementPreDecidedPairs);
                Assert.True(on.b.ArrangementPreDecidedPairs > 0);
                Assert.Equal(off.b.Counts["intersecting_pairs"], on.b.Counts["intersecting_pairs"]);
                Assert.Equal(off.b.Counts["pieces"], on.b.Counts["pieces"]);
                Assert.Equal(off.b.RoundingClean, on.b.RoundingClean);
                Assert.True(on.result.TriangleCount > 0);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(on.result));
            }
        }

        [Fact]
        public void Step3OfAChain_PowerOfTwoScalesGiveTheSameResultBitForBit()
        {
            var one = Step3(Similarity.Identity, ExactMeshBoolean.Operations.Intersection, true).result;
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var scaled = Step3(s, ExactMeshBoolean.Operations.Intersection, true).result;
                Assert.Equal(one.TriangleCount, scaled.TriangleCount);
                var undone = new DMesh3(scaled);
                foreach (int vid in undone.VertexIndices().ToArray())
                    undone.SetVertex(vid, s.Inverse(undone.GetVertex(vid)));
                Assert.Equal(Hash(one), Hash(undone));
            }
        }

        [Fact]
        public void Step3OfAChain_ReuseOfOneInstance_GivesTheSameResult()
        {
            DMesh3 a = FlatBox(Similarity.Identity, 0, 0, 0, 1, 1, 1, 5);
            DMesh3 b = FlatBox(Similarity.Identity, 0.5, 0.25, 1, 0.75, 0.5, 0.5, 4);
            var first = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(first.Compute());
            var step = new ExactMeshBoolean(first.Result, a, ExactMeshBoolean.Operations.Intersection);
            Assert.True(step.Compute());
            string h1 = Hash(step.Result);
            Assert.True(step.Compute());
            Assert.Equal(h1, Hash(step.Result));
        }

        [Fact]
        public void Step3OfAChain_ReuseOfOneInstance_ToggledFlagGivesTheSameResult()
        {
            DMesh3 a = FlatBox(Similarity.Identity, 0, 0, 0, 1, 1, 1, 5);
            DMesh3 b = FlatBox(Similarity.Identity, 0.5, 0.25, 1, 0.75, 0.5, 0.5, 4);
            var first = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(first.Compute());
            var step = new ExactMeshBoolean(first.Result, a, ExactMeshBoolean.Operations.Difference);
            Assert.True(step.Compute());
            string h1 = Hash(step.Result);
            int decided = step.ArrangementPreDecidedPairs;
            Assert.True(decided > 0);
            step.ArrangementPreDecideCoplanar = false;
            Assert.True(step.Compute());
            Assert.Equal(0, step.ArrangementPreDecidedPairs);
            Assert.Equal(h1, Hash(step.Result));
            step.ArrangementPreDecideCoplanar = true;
            Assert.True(step.Compute());
            Assert.Equal(decided, step.ArrangementPreDecidedPairs);
            Assert.Equal(h1, Hash(step.Result));
        }

        [Fact]
        public void Step3OfAChain_ParallelEqualsSerial_AndRepeatedRunsAgree()
        {
            string reference = Hash(Step3(Similarity.Identity, ExactMeshBoolean.Operations.Difference, true, serial: true).result);
            for (int i = 0; i < 3; ++i) {
                Assert.Equal(reference, Hash(Step3(Similarity.Identity, ExactMeshBoolean.Operations.Difference, true, serial: false).result));
                Assert.Equal(reference, Hash(Step3(Similarity.Identity, ExactMeshBoolean.Operations.Difference, true, serial: true).result));
            }
        }
    }
}
