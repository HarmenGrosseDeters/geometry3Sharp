using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Family = geometry3Sharp.Tests.SafeRoundingDegenerateInputTests.Family;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The position search of MeshRoundingRepair (docs/design/SAFE_ROUNDING.md section 9): after the passes, a corner of
    /// each defect that is left is moved to one of a fixed set of positions (26 directions, the room of the vertex over
    /// 2^j), each move accepted by the same exact check as every edit. With SearchWithoutPasses the search is the only repair,
    /// which is how the rules are tested one by one.
    /// </summary>
    public class MeshRoundingRepairTestsPositionSearch
    {
        readonly Xunit.Abstractions.ITestOutputHelper output;
        public MeshRoundingRepairTestsPositionSearch(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        // a sphere with one cap (Collinear, every 200th triangle): one degenerate triangle, no intersecting pair
        static DMesh3 OneCap() => SafeRoundingDegenerateInputTests.Make(Family.Collinear, 0, 1, 200);

        static MeshRoundingRepair SearchOnly(DMesh3 m, bool check = true)
        {
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, SearchWithoutPasses = true };
            MeshRoundingRepair.CheckOnThisThread = check;
            try {
                r.Apply();
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            return r;
        }

        static List<int> Moved(DMesh3 before, DMesh3 after) =>
            before.VertexIndices().Where(v => after.IsVertex(v) == false || after.GetVertex(v) != before.GetVertex(v)).ToList();

        // the one triangle below the degenerate area (1e-12 of the squared diagonal)
        static int TheDegenerateTriangle(DMesh3 m)
        {
            double diag = MeshRepairMetrics.TriangleBounds(m).DiagonalLength;
            return Assert.Single(m.TriangleIndices().Where(t => m.GetTriArea(t) < 1e-12 * diag * diag));
        }

        static string Print(DMesh3 m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                sb.Append(v).Append(':').Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',')
                    .Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            foreach (int t in m.TriangleIndices())
                sb.Append(t).Append(':').Append(m.GetTriangle(t)).Append(';');
            return sb.ToString();
        }

        [Fact]
        public void Directions_The26UnitVectors_InOrder()
        {
            Vector3d[] d = MeshRoundingRepair.SearchDirections;
            Assert.Equal(26, d.Length);
            int i = 0;
            for (int a = -1; a <= 1; ++a)
                for (int b = -1; b <= 1; ++b)
                    for (int c = -1; c <= 1; ++c) {
                        if (a == 0 && b == 0 && c == 0)
                            continue;
                        double l = Math.Sqrt(a * a + b * b + c * c);
                        Assert.Equal(a / l, d[i].x, 15); Assert.Equal(b / l, d[i].y, 15); Assert.Equal(c / l, d[i].z, 15);
                        Assert.Equal(1, d[i].Length, 15);
                        // (the axes exactly, and with every direction its opposite)
                        if (Math.Abs(a) + Math.Abs(b) + Math.Abs(c) == 1)
                            Assert.Equal(new Vector3d(a, b, c), d[i]);
                        Assert.Equal(-d[i], d[25 - i]);
                        i++;
                    }
        }

        [Fact]
        public void OneCap_SearchAlone_OneMoveWithinTheBound_Clean()
        {
            DMesh3 start = OneCap(), m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            Assert.Equal(1, r.InitialDegenerate);
            Assert.Equal(0, r.InitialIntersections);
            Assert.True(r.IsClean);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed() && Invariance.AllFinite(m));
            Assert.Equal(1, r.PositionSearchMoves);
            // (counted into Moves, and nothing else was applied)
            Assert.Equal(1, r.Moves);
            Assert.Equal(0, r.Collapses + r.Flips + r.Zips + r.VertexRemovals + r.RemovedComponents);
            List<int> moved = Moved(start, m);
            int v = Assert.Single(moved);
            Index3i tv = start.GetTriangle(TheDegenerateTriangle(start));
            Assert.Contains(v, new[] { tv.a, tv.b, tv.c });
            double far = m.GetVertex(v).Distance(start.GetVertex(v));
            Assert.True(far > 0 && far <= r.MaxLength, $"moved by {far:R}, bound {r.MaxLength:R}");
            Assert.True(r.MaxDrift <= r.MaxLength);
            Assert.Equal(far, r.MaxDrift);
            // the smallest size first, the room (here the whole bound) over 2^7, in the first direction; the position
            // is the rounded sum, so the distance is that size up to the rounding of the coordinates
            Assert.Equal(1, r.PositionSearchProposals);
            Assert.Equal(r.MaxLength / 128, far, 12);
            Assert.Equal(start.GetVertex(v) + r.MaxLength / 128 * MeshRoundingRepair.SearchDirections[0], m.GetVertex(v));
            // the room that is left at the moved vertex is the bound minus what it used; elsewhere the whole bound
            Assert.Equal(r.MaxLength - far, r.room_at(v));
            int elsewhere = m.VertexIndices().First(u => u != v && m.VtxVerticesItr(u).Contains(v) == false);
            Assert.Equal(r.MaxLength, r.room_at(elsewhere));
            Assert.Equal(double.PositiveInfinity, r.SmallestRejectedDrift);
            // the same solid within the drift
            Assert.Equal("", BooleanOracle.SameSolid(m, start, 2, r.MaxDrift));
        }

        [Theory]
        [InlineData(Family.FoldedCluster, 40, 6, 1)]
        public void Rounds_UntilASearchAcceptsNothing(Family f, int k, int count, int seed)
        {
            // the passes after a search open the way for another search: one round leaves defects, the rounds none
            var raw = new ExactMeshBoolean(SafeRoundingDegenerateInputTests.Make(f, k, seed, count), new DMesh3(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute(), raw.Error);
            DMesh3 one = new DMesh3(raw.Result), all = new DMesh3(raw.Result);
            var r1 = new MeshRoundingRepair(one) { SharedPositionsTouch = true, MaxSearchRounds = 1 };
            Assert.False(r1.Apply());
            Assert.True(r1.RemainingDegenerate + r1.RemainingIntersections > 0 && r1.PositionSearchMoves > 0);
            var r = new MeshRoundingRepair(all) { SharedPositionsTouch = true };
            Assert.True(r.Apply());
            Assert.True(r.PositionSearchMoves > r1.PositionSearchMoves);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(all));
            Assert.True(r.MaxDrift <= r.MaxLength);
        }

        [Fact]
        public void SmallestRejectedDrift_NotFromTheSearch()
        {
            // a candidate of the search is within the bound by its size; where the rounding of its position puts it an
            // ulp beyond, that is not reported as an edit the bound rejected: here (strict: the relative bound only)
            // the passes had an edit rejected that needed 3 % more than the bound, and that stays the smallest
            var strict = new ExactMeshBoolean(Leftover("utm FoldedCluster 36 4 0"), new DMesh3(), Ops.Union) { StrictRounding = true };
            Assert.False(strict.Compute());
            MeshRoundingRepair r = strict.Rounding;
            Assert.True(r.PositionSearchProposals > 0);
            Assert.InRange(r.SmallestRejectedDrift / r.MaxLength, 1.01, 1.1);
        }

        [Fact]
        public void MaxPassesZero_NoSearch()
        {
            // MaxPasses 0 repairs nothing (callers use it to count the defects): no search either
            DMesh3 start = OneCap(), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, MaxPasses = 0 };
            Assert.False(r.Apply());
            Assert.Equal(1, r.RemainingDegenerate);
            Assert.Equal(0, r.PositionSearchProposals);
            Assert.Equal(Print(start), Print(m));
        }

        [Fact]
        public void NoDefect_NoProposal()
        {
            DMesh3 start = DegenerateFamilies.Sphere(6), m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            Assert.True(r.IsClean);
            Assert.Equal(0, r.PositionSearchProposals);
            Assert.Equal(0, r.PositionSearchMoves);
            Assert.Equal(Print(start), Print(m));
        }

        [Fact]
        public void FixedCorner_TriedLast()
        {
            // the corner the search moves when none is fixed: fixed, another corner is moved and that one stays
            DMesh3 start = OneCap(), free = new DMesh3(start);
            SearchOnly(free);
            int first = Assert.Single(Moved(start, free));
            DMesh3 m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, SearchWithoutPasses = true, FixedVertexF = v => v == first };
            Assert.True(r.Apply());
            int other = Assert.Single(Moved(start, m));
            Assert.NotEqual(first, other);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            // all three fixed: the fixed corners are still tried (a preference), in their order
            DMesh3 all = new DMesh3(start);
            var ra = new MeshRoundingRepair(all) { SharedPositionsTouch = true, SearchWithoutPasses = true, FixedVertexF = v => true };
            Assert.True(ra.Apply());
            Assert.Equal(Print(free), Print(all));
        }

        [Fact]
        public void BoundaryCorners_NotMoved()
        {
            // every corner of the degenerate triangle on the boundary (a triangle removed at each): nothing is proposed
            DMesh3 start = OneCap();
            int t = TheDegenerateTriangle(start);
            Index3i tv = start.GetTriangle(t);
            // (one hole across an edge of the triangle, one at the corner opposite, at a triangle with no other corner)
            int far = -1, kc = -1;
            for (int k = 0; k < 3 && far < 0; ++k) {
                int c = tv[k], a = tv[(k + 1) % 3], b = tv[(k + 2) % 3];
                foreach (int x in start.VtxTrianglesItr(c)) {
                    Index3i xv = start.GetTriangle(x);
                    if (xv.a != a && xv.b != a && xv.c != a && xv.a != b && xv.b != b && xv.c != b) { far = x; kc = k; break; }
                }
            }
            Assert.True(far >= 0);
            Index2i et = start.GetEdgeT(start.FindEdge(tv[(kc + 1) % 3], tv[(kc + 2) % 3]));
            start.RemoveTriangle(et.a == t ? et.b : et.a, false, false);
            start.RemoveTriangle(far, false, false);
            for (int k = 0; k < 3; ++k)
                Assert.True(start.IsBoundaryVertex(tv[k]) && start.IsBowtieVertex(tv[k]) == false);
            DMesh3 m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            Assert.Equal(1, r.RemainingDegenerate);
            Assert.False(r.IsClean);
            Assert.Equal(0, r.PositionSearchProposals);
            Assert.Equal(Print(start), Print(m));
        }

        [Fact]
        public void OpenMesh_InteriorCornerMoved_BoundaryKept()
        {
            // a hole next to the degenerate triangle (a triangle removed at one of its corners): the search moves an
            // interior corner, every boundary vertex stays where it was, and the boundary loop is the same
            DMesh3 start = OneCap();
            int t = TheDegenerateTriangle(start);
            Index3i tv = start.GetTriangle(t);
            int far = -1;
            for (int k = 0; k < 3 && far < 0; ++k)
                foreach (int x in start.VtxTrianglesItr(tv[k])) {
                    Index3i xv = start.GetTriangle(x);
                    int a = tv[(k + 1) % 3], b = tv[(k + 2) % 3];
                    if (xv.a != a && xv.b != a && xv.c != a && xv.a != b && xv.b != b && xv.c != b) { far = x; break; }
                }
            Assert.True(far >= 0);
            start.RemoveTriangle(far, false, false);
            List<int> boundary = start.VertexIndices().Where(v => start.IsBoundaryVertex(v)).ToList();
            Assert.Equal(3, boundary.Count);
            DMesh3 m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            Assert.True(r.IsClean);
            Assert.Equal(1, r.PositionSearchMoves);
            int moved = Assert.Single(Moved(start, m));
            Assert.DoesNotContain(moved, boundary);
            Assert.Equal(boundary, m.VertexIndices().Where(v => m.IsBoundaryVertex(v)).ToList());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            Assert.True(r.MaxDrift <= r.MaxLength);
        }

        [Fact]
        public void SharedCorner_SearchedOncePerMeshState()
        {
            // the apex pokes deeper than the bound: no position repairs the pairs. Their first triangles share the
            // apex and the base corners, and each vertex is searched once, not once per pair
            DMesh3 deep = PokedBox(1e-3);
            MeshRoundingRepair r = SearchOnly(deep);
            Assert.True(r.RemainingIntersections >= 2);
            Assert.Equal(0, r.PositionSearchMoves);
            // (the four vertices of the tetrahedron at most: the box's corners are not corners of a first triangle)
            Assert.InRange(r.PositionSearchProposals, 26 * 8, 4 * 26 * 8);
        }

        [Fact]
        public void BowtieCorners_NotMoved()
        {
            // every corner of the degenerate triangle a bowtie (a tetrahedron hung on each): none is moved
            DMesh3 start = OneCap();
            int t = TheDegenerateTriangle(start);
            Index3i tv = start.GetTriangle(t);
            for (int k = 0; k < 3; ++k) {
                Vector3d p = start.GetVertex(tv[k]);
                // (outside the sphere, away from the others)
                int a = start.AppendVertex(p * 1.3 + new Vector3d(0.02, 0, 0)), b = start.AppendVertex(p * 1.3 + new Vector3d(0, 0.02, 0)), c = start.AppendVertex(p * 1.3 + new Vector3d(0, 0, 0.02));
                int v = tv[k];
                Assert.True(start.AppendTriangle(v, a, b) >= 0 && start.AppendTriangle(v, b, c) >= 0 && start.AppendTriangle(v, c, a) >= 0 && start.AppendTriangle(a, c, b) >= 0);
                Assert.True(start.IsBowtieVertex(v) && start.IsBoundaryVertex(v) == false);
            }
            DMesh3 m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            // (the hung tetrahedra touch the sphere's triangles at the corners; whatever the search does about those
            // pairs, it does not move a bowtie: the corners stay, and with them the degenerate triangle)
            Assert.Equal(1, r.RemainingDegenerate);
            for (int k = 0; k < 3; ++k)
                Assert.Equal(start.GetVertex(tv[k]), m.GetVertex(tv[k]));
        }

        // a box, and a tetrahedron whose apex pokes through the box's face x = 1 by depth: intersecting pairs, no
        // degenerate triangle (the tetrahedron first: its triangles are the first of the pairs)
        static DMesh3 PokedBox(double depth)
        {
            var m = new DMesh3();
            int apex = m.AppendVertex(new Vector3d(1 - depth, 0.3, 0.6));
            int a = m.AppendVertex(new Vector3d(2, 0.1, 0.4)), b = m.AppendVertex(new Vector3d(2, 0.5, 0.4)), c = m.AppendVertex(new Vector3d(2, 0.3, 0.9));
            m.AppendTriangle(apex, a, b); m.AppendTriangle(apex, b, c); m.AppendTriangle(apex, c, a); m.AppendTriangle(a, c, b);
            MeshEditor.Append(m, DegenerateFamilies.Box(Vector3d.Zero, Vector3d.One));
            return m;
        }

        [Fact]
        public void Sizes_SmallestThatSeparates_UpToTheRoom()
        {
            // the apex pokes through by a share of the bound: the move is the smallest size room / 2^j that separates
            // (the first directions point back into the box; the apex leaves along +x, alone or with y, z), and the
            // room itself is the last size
            double bound = SearchOnly(PokedBox(1e-3)).MaxLength;
            double[] share = { 0.001, 0.05, 0.3, 0.8 };
            int last = 8;
            for (int i = 0; i < share.Length; ++i) {
                DMesh3 start = PokedBox(share[i] * bound), m = new DMesh3(start);
                MeshRoundingRepair r = SearchOnly(m);
                Assert.Equal(bound, r.MaxLength);
                Assert.True(r.IsClean, $"share {share[i]}: {r.RemainingIntersections} pairs left");
                Assert.Equal(1, r.PositionSearchMoves);
                int v = Assert.Single(Moved(start, m));
                Assert.Equal(0, v);
                double far = m.GetVertex(v).Distance(start.GetVertex(v));
                // (the size is the room over 2^j: which j)
                int j = (int)Math.Round(Math.Log(bound / far, 2));
                output.WriteLine($"share {share[i]}: j = {j}, {r.PositionSearchProposals} proposals");
                Assert.InRange(j, 0, 7);
                Assert.Equal(bound / (1 << j), far, 12);
                // a deeper apex needs a larger size, and the move is longer than half the depth
                Assert.True(j <= last);
                Assert.True(far > share[i] * bound * 0.5);
                last = j;
                Assert.True(far <= bound && r.MaxDrift <= bound);
                Assert.Equal(double.PositiveInfinity, r.SmallestRejectedDrift);
                if (i == 0)
                    Assert.Equal(7, j);
                if (i == share.Length - 1)
                    Assert.Equal(0, j);
            }
        }

        [Fact]
        public void IntersectingPair_SearchAlone_Separated()
        {
            DMesh3 start = PokedBox(1e-7), m = new DMesh3(start);
            MeshRoundingRepair r = SearchOnly(m);
            Assert.Equal(0, r.InitialDegenerate);
            Assert.True(r.InitialIntersections > 0);
            Assert.True(r.IsClean, $"{r.RemainingIntersections} pairs left");
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(m));
            Assert.True(r.PositionSearchMoves >= 1);
            Assert.True(r.MaxDrift <= r.MaxLength);
            foreach (int v in Moved(start, m))
                Assert.True(m.GetVertex(v).Distance(start.GetVertex(v)) <= r.MaxLength);
            // deeper than the bound: no position within it separates the two, the mesh is left as it is
            DMesh3 deep = PokedBox(1e-3), deepStart = new DMesh3(deep);
            MeshRoundingRepair rd = SearchOnly(deep);
            Assert.True(rd.RemainingIntersections > 0 && rd.PositionSearchProposals > 0);
            Assert.Equal(0, rd.PositionSearchMoves);
            Assert.Equal(Print(deepStart), Print(deep));
        }

        [Fact]
        public void AtTheLimit_TheSearchStops()
        {
            // the effort limit is reached inside the search: no proposal after it, and the mesh is the one of a search
            // that proposes on (every proposal after the limit is rejected)
            DMesh3 input = SafeRoundingDegenerateInputTests.Make(Family.Collinear, 0, 1, 9);
            DMesh3 stop = new DMesh3(input), on = new DMesh3(input), free = new DMesh3(input);
            var rs = new MeshRoundingRepair(stop) { SharedPositionsTouch = true, SearchWithoutPasses = true, MaxPairTests = 300 };
            rs.Apply();
            var ro = new MeshRoundingRepair(on) { SharedPositionsTouch = true, SearchWithoutPasses = true, MaxPairTests = 300, ProposePastTheLimit = true };
            ro.Apply();
            var rf = new MeshRoundingRepair(free) { SharedPositionsTouch = true, SearchWithoutPasses = true };
            rf.Apply();
            Assert.True(rs.EvaluationLimitReached && ro.EvaluationLimitReached);
            Assert.False(rf.EvaluationLimitReached);
            Assert.True(rs.PositionSearchMoves > 0 && rs.PositionSearchMoves < rf.PositionSearchMoves, $"{rs.PositionSearchMoves} of {rf.PositionSearchMoves} moves");
            Assert.Equal(Print(on), Print(stop));
            Assert.Equal(ro.PositionSearchMoves, rs.PositionSearchMoves);
            Assert.Equal(0, rs.ProposalsPastTheLimit);
            Assert.True(ro.ProposalsPastTheLimit > 0);
            Assert.True(rs.PositionSearchProposals < ro.PositionSearchProposals, $"{rs.PositionSearchProposals} proposals with the stop, {ro.PositionSearchProposals} without");
            // (every defect searched gave a move, but the one the limit was reached at: no further defect after it)
            Assert.True(rs.PositionSearchProposals <= 26 * 8 * 3 * (rs.PositionSearchMoves + 1), $"{rs.PositionSearchProposals} proposals, {rs.PositionSearchMoves} moves");
            // the limit reached before the search (in the passes): the search proposes nothing
            DMesh3 early = SafeRoundingDegenerateInputTests.Make(Family.Cluster, 36, 1);
            var re = new MeshRoundingRepair(early) { SharedPositionsTouch = true, MaxPairTests = 200 };
            re.Apply();
            Assert.True(re.EvaluationLimitReached);
            Assert.Equal(0, re.PositionSearchProposals);
            Assert.Equal(0, re.PositionSearchMoves);
        }

        // generated leftovers (SAFE_ROUNDING.md section 6.1: dense clusters with 8 pokes in a row, and UTM cases) that the
        // passes leave unclean and the search cleans: family, k, count, seed
        public static IEnumerable<object[]> Leftovers()
        {
            yield return new object[] { "Cluster 0 8 5" };
            yield return new object[] { "Cluster 40 8 1" };
            yield return new object[] { "Cluster 44 8 5" };
            yield return new object[] { "Cluster 52 8 4" };
            yield return new object[] { "FoldedCluster 30 8 3" };
            yield return new object[] { "FoldedCluster 44 8 0" };
            yield return new object[] { "FoldedCluster 44 8 3" };
            yield return new object[] { "FoldedCluster 52 8 4" };
            yield return new object[] { "utm Cluster 30 4 1" };
            yield return new object[] { "utm FoldedCluster 36 4 0" };
        }

        static DMesh3 Leftover(string name)
        {
            string[] w = name.Split(' ');
            bool utm = w[0] == "utm";
            int o = utm ? 1 : 0;
            DMesh3 m = SafeRoundingDegenerateInputTests.Make((Family)Enum.Parse(typeof(Family), w[o]), int.Parse(w[o + 1]), int.Parse(w[o + 3]), int.Parse(w[o + 2]));
            return utm ? Invariance.Transformed(m, Similarity.ByName("rotated, scale 1e-3, utm")) : m;
        }

        [Theory]
        [MemberData(nameof(Leftovers))]
        public void Leftover_CleanWithTheSearch_WithinTheBound(string name)
        {
            DMesh3 input = Leftover(name);
            var op = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            Assert.True(op.Compute(), op.Error);
            MeshRoundingRepair r = op.Rounding;
            output.WriteLine($"{name}: {r.PositionSearchMoves} search moves of {r.Moves}, {r.PositionSearchProposals} proposals, drift {r.MaxDrift / r.MaxLength:G3} of the bound");
            Assert.True(op.RoundingClean, $"{r.RemainingDegenerate} degenerate, {r.RemainingIntersections} pairs left");
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(op.Result));
            Assert.True(op.Result.IsClosed() && Invariance.AllFinite(op.Result));
            Assert.True(r.PositionSearchMoves > 0 && r.PositionSearchMoves <= r.Moves);
            Assert.True(r.MaxDrift <= r.MaxLength);
            Assert.False(r.EvaluationLimitReached);
            // the surface is the sphere's that the pokes were made in, within the drift
            if (name.StartsWith("utm") == false)
                Assert.Equal("", BooleanOracle.SameSolid(op.Result, DegenerateFamilies.Sphere(6), 2, r.MaxDrift));
        }

        [Fact]
        public void Leftover_Checked_Invariant_Deterministic()
        {
            // through the Boolean, with the passes: the checked run (every search and bound compared with its slow
            // reference) gives the same mesh; the 2^k scales bit for bit; serial equals parallel, repeated
            DMesh3 input = Leftover("Cluster 52 8 4");
            Func<DMesh3, bool, bool, ExactMeshBoolean> run = (mesh, check, serial) => {
                MeshRoundingRepair.CheckOnThisThread = check;
                gParallel.SerialOnThisThread = serial;
                try {
                    var b = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union);
                    Assert.True(b.Compute(), b.Error);
                    return b;
                } finally {
                    MeshRoundingRepair.CheckOnThisThread = false;
                    gParallel.SerialOnThisThread = false;
                }
            };
            ExactMeshBoolean b0 = run(input, false, false);
            Assert.True(b0.RoundingClean && b0.Rounding.PositionSearchMoves > 0);
            string expected = Print(b0.Result);
            Assert.Equal(expected, Print(run(input, true, false).Result));
            Assert.Equal(expected, Print(run(input, false, true).Result));
            Assert.Equal(expected, Print(run(input, false, false).Result));
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean b = run(Invariance.Transformed(input, sim), false, false);
                Assert.True(b.Rounding.MaxDrift <= b.Rounding.MaxLength, sim.ToString());
                if (sim.IsExact == false)
                    continue;
                Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(b.Result, sim)) == "", sim.ToString());
                Assert.Equal(b0.Rounding.PositionSearchMoves, b.Rounding.PositionSearchMoves);
                Assert.Equal(b0.Rounding.MaxDrift * sim.Scale, b.Rounding.MaxDrift);
            }
        }

        [Fact]
        public void SearchAlone_Invariant_Deterministic_Reused()
        {
            // many caps and pairs, the search alone: the 2^k scales bit for bit, every similarity clean with the same
            // counts; serial equals parallel, repeated; a reused instance equals a fresh one
            DMesh3 input = SafeRoundingDegenerateInputTests.Make(Family.Collinear, 0, 1, 9);
            Func<DMesh3, bool, MeshRoundingRepair> repair = (mesh, serial) => {
                var r = new MeshRoundingRepair(mesh) { SharedPositionsTouch = true, SearchWithoutPasses = true };
                bool was = gParallel.SerialOnThisThread;
                gParallel.SerialOnThisThread = serial;
                try { r.Apply(); } finally { gParallel.SerialOnThisThread = was; }
                return r;
            };
            DMesh3 m0 = new DMesh3(input);
            MeshRoundingRepair r0 = repair(m0, false);
            Assert.True(r0.IsClean && r0.PositionSearchMoves > 1);
            output.WriteLine($"{r0.PositionSearchMoves} moves, {r0.PositionSearchProposals} proposals, drift {r0.MaxDrift / r0.MaxLength:R}, smallest rejected {r0.SmallestRejectedDrift / r0.MaxLength:R}");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m0));
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 ms = Invariance.Transformed(input, sim);
                MeshRoundingRepair rs = repair(ms, false);
                Assert.True(rs.MaxDrift <= rs.MaxLength, sim.ToString());
                if (sim.IsExact == false)
                    continue;
                Assert.True(Invariance.Difference(m0, Invariance.MappedBack(ms, sim)) == "", sim.ToString());
                Assert.Equal(r0.PositionSearchMoves, rs.PositionSearchMoves);
                Assert.Equal(r0.PositionSearchProposals, rs.PositionSearchProposals);
                Assert.Equal(r0.MaxDrift * sim.Scale, rs.MaxDrift);
            }
            string expected = Print(m0);
            for (int run = 0; run < 3; ++run) {
                DMesh3 again = new DMesh3(input);
                repair(again, run == 0);
                Assert.True(expected == Print(again), $"run {run} differs");
            }
            // reuse: after another mesh, the same instance gives the same result and counts
            var reused = new MeshRoundingRepair(OneCap()) { SharedPositionsTouch = true, SearchWithoutPasses = true };
            reused.Apply();
            Assert.Equal(1, reused.PositionSearchMoves);
            DMesh3 second = new DMesh3(input);
            reused.Mesh = second;
            reused.Apply();
            Assert.Equal(expected, Print(second));
            Assert.Equal(r0.PositionSearchMoves, reused.PositionSearchMoves);
            Assert.Equal(r0.PositionSearchProposals, reused.PositionSearchProposals);
            Assert.Equal(r0.Moves, reused.Moves);
        }
    }
}
