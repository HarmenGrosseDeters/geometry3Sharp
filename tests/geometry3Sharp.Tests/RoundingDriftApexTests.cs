using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Family = geometry3Sharp.Tests.SafeRoundingDegenerateInputTests.Family;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// SafeRounding charges an edit's drift to its apex, a vertex of every triangle it adds, instead of to every vertex
    /// of the triangles it removes and adds (docs/design/ROUNDING_DRIFT_APEX.md; the lemma is checked by Z3,
    /// tools/Z3Pilot/drift_apex.py). Neighbouring edits that share no apex then do not add up their drifts: needles that
    /// share only the vertices opposite their short edges are all repaired within a bound of 1.5 times one needle, where
    /// the old rule chained them through the shared vertices. The check mode asserts that the apex is in every added
    /// triangle and that its drift does not decrease.
    /// </summary>
    public class RoundingDriftApexTests
    {
        // a sphere with needles: edges split at s from one end, chosen so that no needle's end is an end or an opposite
        // vertex of another needle; neighbouring needles share opposite vertices only
        static DMesh3 Needles(double s, out int count)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            var ends = new HashSet<int>();
            var opposite = new HashSet<int>();
            count = 0;
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (m.IsEdge(eid) == false)
                    continue;
                Index2i e = m.GetEdgeV(eid), o = m.GetEdgeOpposingV(eid);
                if (ends.Contains(e.a) || ends.Contains(e.b) || opposite.Contains(e.a) || opposite.Contains(e.b) || ends.Contains(o.a) || ends.Contains(o.b))
                    continue;
                ends.Add(e.a); ends.Add(e.b); opposite.Add(o.a); opposite.Add(o.b);
                Vector3d a = m.GetVertex(e.a), b = m.GetVertex(e.b);
                Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                m.SetVertex(info.vNew, a + s * (b - a).Normalized);
                ends.Add(info.vNew);
                count++;
            }
            return m;
        }

        const double S = 1e-13;

        // the bound 1.5 needles, relative to the diagonal of the needles' sphere (so that it scales with a similarity)
        static readonly double Bound = 1.5 * S / MeshRepairMetrics.TriangleBounds(Needles(S, out _)).DiagonalLength;

        static MeshRoundingRepair Repair(DMesh3 m)
        {
            var r = new MeshRoundingRepair(m) { MaxDisplacement = Bound };
            r.Apply();
            return r;
        }

        [Fact]
        public void Needles_SharingOppositeVertices_AllRepairedWithinTheBound()
        {
            // before: 880 of the 1388 degenerate triangles left (the drift of each contraction went to the opposite
            // vertices too, and the next needle there started from it)
            DMesh3 start = Needles(S, out int count), m = new DMesh3(start);
            Assert.True(count > 600, $"{count} needles");
            MeshRoundingRepair r = Repair(m);
            Assert.Equal(2 * count, r.InitialDegenerate);
            Assert.True(r.IsClean, $"{r.RemainingDegenerate} degenerate left");
            Assert.Equal(count, r.Collapses);
            Assert.True(r.MaxDrift <= r.MaxLength, $"drift {r.MaxDrift:G3}, bound {r.MaxLength:G3}");
            Assert.True(BooleanOracle.SameSolid(m, start, 2, r.MaxDrift) == "");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        [Fact]
        public void CheckMode_EveryEditType_SameResultAsWithout()
        {
            // contractions, flips, zips, strip zips, moves and vertex removals: with the check, every apex is in every
            // triangle its edit adds, and the result is the one without the check
            int collapses = 0, flips = 0, zips = 0, strips = 0, moves = 0, removals = 0;
            var inputs = new List<(DMesh3 mesh, bool shared)> { (Needles(S, out _), false) };
            foreach (Family f in new[] { Family.Cluster, Family.FoldedCluster, Family.Collinear, Family.Sliver })
                foreach (int k in new[] { 30, 44 })
                    inputs.Add((SafeRoundingDegenerateInputTests.Make(f, k, 1), true));
            Similarity utm = Similarity.ByName("rotated, scale 1e-3, utm");
            inputs.Add((Invariance.Transformed(SafeRoundingDegenerateInputTests.Make(Family.Cluster, 0, 0, 4), utm), true));
            foreach (var (mesh, shared) in inputs) {
                DMesh3 plain = new DMesh3(mesh), checkedMesh = new DMesh3(mesh);
                var r0 = new MeshRoundingRepair(plain) { SharedPositionsTouch = shared };
                r0.Apply();
                MeshRoundingRepair.CheckOnThisThread = true;
                MeshRoundingRepair r;
                try {
                    r = new MeshRoundingRepair(checkedMesh) { SharedPositionsTouch = shared };
                    r.Apply();
                } finally {
                    MeshRoundingRepair.CheckOnThisThread = false;
                }
                Assert.Equal("", Invariance.Difference(plain, checkedMesh));
                Assert.Equal(r0.MaxDrift, r.MaxDrift);
                collapses += r.Collapses; flips += r.Flips; zips += r.Zips; strips += r.StripZips; moves += r.Moves; removals += r.VertexRemovals;
            }
            // vertex removals: where the coordinates are coarse, in the booleans of clusters at UTM coordinates
            foreach (int seed in new[] { 0, 1, 2 }) {
                DMesh3 mesh = Invariance.Transformed(SafeRoundingDegenerateInputTests.Make(Family.Cluster, 0, seed, 4), utm);
                var plain = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), ExactMeshBoolean.Operations.Union);
                Assert.True(plain.Compute(), plain.Error);
                MeshRoundingRepair.CheckOnThisThread = true;
                ExactMeshBoolean checkedOp;
                try {
                    checkedOp = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), ExactMeshBoolean.Operations.Union);
                    Assert.True(checkedOp.Compute(), checkedOp.Error);
                } finally {
                    MeshRoundingRepair.CheckOnThisThread = false;
                }
                Assert.Equal("", Invariance.Difference(plain.Result, checkedOp.Result));
                removals += checkedOp.Rounding.VertexRemovals;
            }
            Assert.True(collapses > 0 && flips > 0 && zips > 0 && strips > 0 && moves > 0 && removals > 0,
                $"collapses {collapses}, flips {flips}, zips {zips}, strips {strips}, moves {moves}, removals {removals}");
        }

        [Fact]
        public void Needles_Similarities_Pow2BitIdentical()
        {
            DMesh3 start = Needles(S, out _), m0 = new DMesh3(start);
            MeshRoundingRepair r0 = Repair(m0);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(start, sim);
                MeshRoundingRepair r = Repair(m);
                Assert.True(Invariance.AllFinite(m) && m.IsClosed(), sim.ToString());
                if (sim.IsExact) {
                    Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, sim)));
                    Assert.Equal(r0.Collapses, r.Collapses);
                }
            }
        }

        [Fact]
        public void Needles_SerialEqualsParallel_ApplyTwice()
        {
            DMesh3 start = Needles(S, out _), parallel = new DMesh3(start), serial = new DMesh3(start);
            Repair(parallel);
            gParallel.SerialOnThisThread = true;
            try {
                Repair(serial);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            Assert.Equal("", Invariance.Difference(serial, parallel));
            // the same instance on a fresh copy: the drifts start from 0 again
            var r = new MeshRoundingRepair(new DMesh3(start)) { MaxDisplacement = Bound };
            Assert.True(r.Apply());
            r.Mesh = new DMesh3(start);
            Assert.True(r.Apply());
            Assert.Equal("", Invariance.Difference(serial, r.Mesh));
        }
    }
}
