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
    /// SafeRounding's box query over the changed triangles (MeshRoundingRepair.near): the tree of the changed triangles
    /// is rebuilt only when the triangles changed since its build outnumber 256, those being scanned linearly, instead of
    /// after every edit (which made a repair with many edits quadratic: Thingi10K 119829/119830 union, 13000
    /// contractions of input slivers, rounding 22.9 -> 4.1 s). With MeshRoundingRepair.CheckOnThisThread every query is
    /// compared with a scan of all triangles (it throws if they differ). The meshes are grid boxes with many edges split
    /// next to an end: two slivers per split, which the repair contracts.
    /// </summary>
    public class RoundingDirtyTreeTests
    {
        // a grid box with every k-th edge split at a fraction f from its first end (two slivers per split)
        internal static DMesh3 SliverBox(int n = 24, int every = 9, double f = 1e-10)
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 0.7, 0.4)), EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int e in m.EdgeIndices().Where(e => e % every == 0).ToList()) {
                if (m.IsEdge(e) == false)
                    continue;
                Index2i ev = m.GetEdgeV(e);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                DMesh3.EdgeSplitInfo info;
                if (m.SplitEdge(e, out info) == MeshResult.Ok)
                    m.SetVertex(info.vNew, a + f * (b - a));
            }
            return m;
        }

        static MeshRoundingRepair Repair(DMesh3 m, bool check = false, bool serial = false, bool boxesFromMesh = false)
        {
            MeshRoundingRepair.CheckOnThisThread = check;
            MeshRoundingRepair.PendingBoxesFromMeshOnThisThread = boxesFromMesh;
            gParallel.SerialOnThisThread = serial;
            try {
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
                r.Apply();
                return r;
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
                MeshRoundingRepair.PendingBoxesFromMeshOnThisThread = false;
                gParallel.SerialOnThisThread = false;
            }
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
        public void ManySlivers_QueriesEqualAScan_TreeBuiltRarely()
        {
            DMesh3 m = SliverBox();
            DMesh3 plainMesh = new DMesh3(m), checkedMesh = new DMesh3(m);
            MeshRoundingRepair plain = Repair(plainMesh);
            MeshRoundingRepair checkedRepair = Repair(checkedMesh, check: true);
            Assert.True(checkedRepair.InitialDegenerate > 1000, $"{checkedRepair.InitialDegenerate} degenerate");
            Assert.True(checkedRepair.IsClean, $"{checkedRepair.RemainingDegenerate} degenerate left");
            Assert.True(checkedRepair.Collapses > 500, $"{checkedRepair.Collapses} contractions");
            Assert.True(checkedRepair.NearChecks > 1000, $"{checkedRepair.NearChecks} queries checked");
            // (before: a build after every edit; now one per 256 changed triangles, and never more than 256 scanned
            // linearly: without the threshold the scan grows to a sixteenth of the mesh, quadratic again)
            Assert.True(checkedRepair.DirtyTreeBuilds > 1 && checkedRepair.DirtyTreeBuilds * 20 < checkedRepair.Collapses,
                $"{checkedRepair.DirtyTreeBuilds} builds for {checkedRepair.Collapses} contractions");
            Assert.True(checkedRepair.MaxPendingScanned > 0 && checkedRepair.MaxPendingScanned <= 256, $"{checkedRepair.MaxPendingScanned} scanned in one query");
            Assert.Equal(Print(plainMesh), Print(checkedMesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(checkedMesh));
        }

        [Trait("Category", "Slow")]
        [Theory]
        [InlineData("utm Cluster 0 4 0")]
        [InlineData("utm Cluster 0 9 1")]
        public void Moves_QueriesEqualAScan(string name)
        {
            // repairs with vertex moves (the moved vertex's triangles pending while a move is proposed: near must see
            // the current geometry, not the proposed one), compared with a scan of the mesh in check mode
            ExactMeshBoolean b = MeshRoundingRepairIncrementalTests.Case(name)();
            MeshRoundingRepair.CheckOnThisThread = true;
            try {
                Assert.True(b.Compute(), b.Error);
            } finally {
                MeshRoundingRepair.CheckOnThisThread = false;
            }
            Assert.True(b.Rounding.Moves > 0, $"{name}: no moves");
            Assert.True(b.Rounding.NearChecks > 0, $"{name}: no query checked");
        }

        [Fact]
        public void DegenerateFamilies_QueriesEqualAScan()
        {
            long checks = 0;
            foreach (SafeRoundingDegenerateInputTests.Family f in Enum.GetValues(typeof(SafeRoundingDegenerateInputTests.Family)))
                foreach (int seed in new[] { 1, 2 }) {
                    DMesh3 m = SafeRoundingDegenerateInputTests.Make(f, 30, seed);
                    DMesh3 a = new DMesh3(m), b = new DMesh3(m);
                    Repair(a);
                    checks += Repair(b, check: true).NearChecks;
                    Assert.True(Print(a) == Print(b), f.ToString());
                }
            Assert.True(checks > 0);
        }

        // ---- the boxes of the pending triangles are kept from query to query (ROUNDING_COST_2026-11.md section 6) ----

        // a prism of height h over a regular n-gon of radius r around (cx, cy), from z0
        static DMesh3 Prism(int n, double r, double cx, double cy, double z0, double h)
        {
            var m = new DMesh3();
            for (int k = 0; k < 2; ++k)
                for (int i = 0; i < n; ++i) {
                    double t = 2 * Math.PI * i / n;
                    m.AppendVertex(new Vector3d(cx + r * Math.Cos(t), cy + r * Math.Sin(t), z0 + k * h));
                }
            int c0 = m.AppendVertex(new Vector3d(cx, cy, z0)), c1 = m.AppendVertex(new Vector3d(cx, cy, z0 + h));
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(c0, j, i); m.AppendTriangle(c1, n + i, n + j);
                m.AppendTriangle(i, j, n + j); m.AppendTriangle(i, n + j, n + i);
            }
            return m;
        }

        // a plate with holes x holes holes (the difference of a box and prisms: long thin triangles between the holes)
        // and its copy, rotated and shifted: a boolean whose rounding evaluates many edits (the `plate` case of
        // benchmarks/SnapRoundingExperiment gen)
        static Func<ExactMeshBoolean> PlateWithHoles(int holes, Ops op)
        {
            var parts = new List<DMesh3> { new GridBox3Generator() { Box = new Box3d(new Vector3d(1, 1, 0.1), new Vector3d(1, 1, 0.1)), EdgeVertices = 2 }.Generate().MakeDMesh() };
            double pitch = 2.0 / holes;
            for (int i = 0; i < holes; ++i)
                for (int j = 0; j < holes; ++j)
                    parts.Add(Prism(16, 0.3 * pitch, (i + 0.5) * pitch, (j + 0.5) * pitch, -0.4, 1));
            var plate = new ExactMeshBoolean(parts, Ops.Difference);
            Assert.True(plate.Compute(), plate.Error);
            DMesh3 a = plate.Result, b = new DMesh3(a);
            MeshTransforms.Rotate(b, new Vector3d(1, 1, 0.1), Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 20));
            MeshTransforms.Translate(b, new Vector3d(0.5, 0.5, 0.05));
            return () => new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
        }

        static ExactMeshBoolean Run(Func<ExactMeshBoolean> make, bool boxesFromMesh, bool check = false)
        {
            MeshRoundingRepair.PendingBoxesFromMeshOnThisThread = boxesFromMesh;
            MeshRoundingRepair.CheckOnThisThread = check;
            try {
                ExactMeshBoolean b = make();
                Assert.True(b.Compute(), b.Error);
                return b;
            } finally {
                MeshRoundingRepair.PendingBoxesFromMeshOnThisThread = false;
                MeshRoundingRepair.CheckOnThisThread = false;
            }
        }

        [Fact]
        public void PendingBoxes_AreKeptBetweenQueries()
        {
            DMesh3 m = SliverBox();
            MeshRoundingRepair kept = Repair(new DMesh3(m));
            // (the boxes are computed once per change of the mesh, not once per query: several queries per accepted edit)
            Assert.True(kept.PendingBoxQueries > 1000, $"{kept.PendingBoxQueries} queries read the kept boxes");
            Assert.True(kept.PendingBoxRefreshes > 0 && 2 * kept.PendingBoxRefreshes < kept.PendingBoxQueries,
                $"{kept.PendingBoxRefreshes} computations for {kept.PendingBoxQueries} queries");
            // (not more often than the mesh changes: at most once per marked triangle)
            Assert.True(kept.PendingBoxRefreshes <= 4L * (kept.Collapses + kept.Flips + kept.Moves + kept.Zips + 1),
                $"{kept.PendingBoxRefreshes} computations for {kept.Collapses + kept.Flips + kept.Moves + kept.Zips} edits");
            MeshRoundingRepair reference = Repair(new DMesh3(m), boxesFromMesh: true);
            Assert.Equal(0, reference.PendingBoxRefreshes);
            Assert.Equal(0, reference.PendingBoxQueries);
        }

        [Fact]
        public void PendingBoxes_ManySlivers_SameResultAsBoxesFromTheMesh()
        {
            // (ManySlivers_QueriesEqualAScan_TreeBuiltRarely checks the queries of the large box against a scan; here the
            // result against the reference path, and the scan check on a smaller box)
            foreach ((int n, int every, bool check) in new[] { (24, 9, false), (24, 5, false), (14, 5, true) }) {
                DMesh3 m = SliverBox(n, every);
                DMesh3 kept = new DMesh3(m), reference = new DMesh3(m), checkedMesh = new DMesh3(m);
                MeshRoundingRepair rk = Repair(kept), rr = Repair(reference, boxesFromMesh: true);
                Assert.True(rk.PendingBoxQueries > 0);
                Assert.True(Print(kept) == Print(reference), $"{n} every {every}: the result differs from the one with the boxes from the mesh");
                Assert.Equal((rr.Collapses, rr.Flips, rr.Moves, rr.Zips, rr.IsClean), (rk.Collapses, rk.Flips, rk.Moves, rk.Zips, rk.IsClean));
                // (and every query of the kept boxes equals a scan of the mesh)
                if (check) {
                    MeshRoundingRepair rc = Repair(checkedMesh, check: true);
                    Assert.True(rc.NearChecks > 0 && Print(kept) == Print(checkedMesh), $"{n} every {every}");
                }
                Assert.Equal("", MeshRobustRepair.NotCleanReason(kept));
            }
        }

        [Fact]
        public void PendingBoxes_DegenerateFamilies_SameResultAsBoxesFromTheMesh()
        {
            long queries = 0;
            foreach (SafeRoundingDegenerateInputTests.Family f in Enum.GetValues(typeof(SafeRoundingDegenerateInputTests.Family)))
                foreach (int seed in new[] { 1, 2, 3 }) {
                    DMesh3 m = SafeRoundingDegenerateInputTests.Make(f, 30, seed);
                    DMesh3 kept = new DMesh3(m), reference = new DMesh3(m);
                    queries += Repair(kept).PendingBoxQueries;
                    Repair(reference, boxesFromMesh: true);
                    Assert.True(Print(kept) == Print(reference), $"{f} {seed}");
                }
            Assert.True(queries > 0);
        }

        [Trait("Category", "Slow")]   // (booleans with their rounding, each twice and once more in check mode: 5-10 s)
        [Theory]
        [InlineData("utm Cluster 0 4 0")]
        [InlineData("utm Cluster 0 9 1")]
        [InlineData("Cluster")]
        [InlineData("FoldedCluster")]
        public void PendingBoxes_RoundingCorpusBooleans_SameResultAsBoxesFromTheMesh(string name)
        {
            Func<ExactMeshBoolean> make = MeshRoundingRepairIncrementalTests.Case(name);
            ExactMeshBoolean kept = Run(make, false), reference = Run(make, true);
            Assert.True(kept.Rounding.PendingBoxQueries > 0, $"{name}: no query read the kept boxes");
            Assert.True(Print(kept.Result) == Print(reference.Result), name);
            Assert.Equal(reference.RoundingClean, kept.RoundingClean);
            Assert.Equal(reference.Rounding.MaxDrift, kept.Rounding.MaxDrift);
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void PendingBoxes_PlateWithHoles_SameResultAsBoxesFromTheMesh(Ops op)
        {
            Func<ExactMeshBoolean> make = PlateWithHoles(8, op);
            ExactMeshBoolean kept = Run(make, false), reference = Run(make, true);
            Assert.True(Print(kept.Result) == Print(reference.Result), op.ToString());
            Assert.Equal(reference.RoundingClean, kept.RoundingClean);
            Assert.True(kept.Result.TriangleCount > 0 && Invariance.AllFinite(kept.Result), op.ToString());
            Assert.True(kept.Rounding.PendingBoxQueries > 100, $"{op}: {kept.Rounding.PendingBoxQueries} queries read the kept boxes, {kept.Result.TriangleCount} triangles");
        }

        // the frames in 4 rows (#698: 5.1-7.6 s alone in one row, up to 26.9 s in a full run); each row repairs the base too
        static readonly int[][] FrameRows = { new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };

        public static IEnumerable<object[]> FrameRowIndexes() => Enumerable.Range(0, FrameRows.Length).Select(r => new object[] { r });

        [Fact]
        public void FrameRows_AreAllTheFramesOnce() =>
            Assert.Equal(Enumerable.Range(0, Similarity.All().Count()), FrameRows.SelectMany(r => r).OrderBy(i => i));

        [Trait("Category", "Slow")]
        [Theory]
        [MemberData(nameof(FrameRowIndexes))]
        public void ManySlivers_Similarities_Pow2BitIdentical(int row)
        {
            DMesh3 m = SliverBox(16, 7);
            DMesh3 m0 = new DMesh3(m);
            MeshRoundingRepair r0 = Repair(m0);
            Assert.True(r0.IsClean);
            Similarity[] all = Similarity.All().ToArray();
            foreach (Similarity sim in FrameRows[row].Select(i => all[i])) {
                DMesh3 ms = Invariance.Transformed(m, sim);
                MeshRoundingRepair rs = Repair(ms, check: true);
                Assert.True(rs.IsClean, sim.ToString());
                Assert.True(ms.IsClosed() && Invariance.AllFinite(ms), sim.ToString());
                if (sim.IsExact) {
                    Assert.True(Invariance.Difference(m0, Invariance.MappedBack(ms, sim)) == "", sim.ToString());
                    Assert.Equal(r0.Collapses, rs.Collapses);
                }
            }
        }

        [Fact]
        public void ManySlivers_SerialEqualsParallel_Repeated()
        {
            DMesh3 m = SliverBox();
            DMesh3 serial = new DMesh3(m);
            Repair(serial, serial: true);
            string s = Print(serial);
            for (int run = 0; run < 3; ++run) {
                DMesh3 p = new DMesh3(m);
                Repair(p);
                Assert.True(s == Print(p), $"parallel run {run} differs from the serial run");
            }
        }
    }
}
