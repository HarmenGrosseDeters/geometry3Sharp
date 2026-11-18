using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The rescue phase of stage 2 (docs/design/TET_MESHING.md section 17): if tets below the target are left after the main passes,
    /// more passes with smoothing also along the active-set direction (Freitag, Jones, Plassmann 1995). A mesh that reached the
    /// target is not changed by it.
    /// </summary>
    public class TetMeshingStage2RescueTests
    {
        [Fact]
        public void MinNormInHull_KnownCases()
        {
            Vector3d x = TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(0, 1, 0) });
            Assert.True(x.Distance(new Vector3d(0.5, 0.5, 0)) < 1e-9, x.ToString());
            // the segment from (1, 0) to (-1, 1): (1 - 2t, t) is shortest at t = 0.4
            x = TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(-1, 1, 0) });
            Assert.True(x.Distance(new Vector3d(0.2, 0.4, 0)) < 1e-9, x.ToString());
            // a single gradient is its own direction; opposite ones leave none
            Assert.Equal(new Vector3d(0, 0, 2), TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(0, 0, 2) }));
            Assert.True(TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0) }).Length < 1e-12);
            Assert.Equal(Vector3d.Zero, TetMeshImprover.MinNormInHull(new List<Vector3d>()));
            // the origin inside a triangle of gradients: the iteration only approaches it, the cut makes it exactly no direction
            Assert.Equal(Vector3d.Zero, TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(1, 0.01, 0), new Vector3d(-1, 1, 0), new Vector3d(-1, -1.3, 0) }));
        }

        [Fact]
        public void MinNormInHull_NaNGradient_GivesNoDirection()
        {
            // a NaN quality of a degenerate neighbour must not produce a step
            Vector3d x = TetMeshImprover.MinNormInHull(new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(double.NaN, 0, 0) });
            Assert.True(x == Vector3d.Zero || (!double.IsNaN(x.x) && !double.IsNaN(x.y) && !double.IsNaN(x.z)), x.ToString());
        }

        [Fact]
        public void ActiveSetSmoothing_RaisesAStarWhereTheWorstTetsGradientIsStuck()
        {
            // an octahedron with jittered corners and an interior vertex (found by a seeded search): smoothing along the worst
            // tet's gradient converges at 34.9 degrees; the active-set direction raises the star to 36.8 degrees
            var v = new List<Vector3d> { new Vector3d(0.7, -0.35, 0.08), new Vector3d(-0.63, 0.17, 0.05), new Vector3d(0.26, 0.72, -0.22),
                new Vector3d(0.3, -0.83, 0.33), new Vector3d(0.08, 0.25, 1.16), new Vector3d(-0.25, 0.13, -1.13), new Vector3d(0.09, -0.195, -0.01) };
            int[][] faces = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            TetMesh3 m = TetMeshingStage2Tests.FromTets(v, faces.Select(f => new Index4i(f[0], f[2], f[1], 6)).ToList());
            Assert.Null(TetMeshQuality.NotValidReason(m));
            var plain = new TetMeshImprover(m) { TargetDegrees = 89, MaxPasses = 200 };
            plain.Run();
            var rescued = new TetMeshImprover(m) { TargetDegrees = 89, MaxPasses = 200, RescuePasses = 200 };
            rescued.Run();
            double qa = TetMeshQuality.Measure(plain.Result()).MinDihedral.Worst, qb = TetMeshQuality.Measure(rescued.Result()).MinDihedral.Worst;
            Assert.True(plain.Counts["passes"] < 200, plain.Counts["passes"].ToString());          // converged, not cut off
            Assert.True(qb > qa + 1, qa + " -> " + qb);
            Assert.True(rescued.Counts["rescue_passes"] > 0);
            Assert.Null(TetMeshQuality.NotValidReason(rescued.Result()));
            for (int i = 0; i < 6; ++i) Assert.Contains(v[i], rescued.Result().Vertices);          // boundary vertices did not move (no input tree)
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("box")]
        public void Rescue_LeavesAMeshThatReachedTheTarget_BitForBit(string name)
        {
            var a = new MeshTetrahedralization(TetMeshingStage2Tests.Input(name)) { RescuePasses = 0 }; Assert.True(a.Compute());
            var b = new MeshTetrahedralization(TetMeshingStage2Tests.Input(name)); Assert.True(b.Compute());
            Assert.True(TetMeshQuality.Measure(b.Result).MinDihedral.Worst >= 10);
            Assert.Equal(a.Result.Vertices, b.Result.Vertices); Assert.Equal(a.Result.Tets, b.Result.Tets);
            Assert.False(b.Counts.ContainsKey("improve_rescue_passes"));
        }

        // a short main phase, so that the rescue phase has bad tets to work on
        static MeshTetrahedralization Short(DMesh3 input, int rescue) => new MeshTetrahedralization(input) {
            RelativeAlpha = 12, RelativeOffset = 200, SplitEdges = false, ContractEdges = false, ImprovePasses = 3, RescuePasses = rescue };

        [Fact]
        public void RescuePasses_IsOnByDefault()
        {
            // owner decision 2026-11-18 (#1072): the rescue phase is part of the default path
            Assert.Equal(150, new MeshTetrahedralization(TetMeshingStage2Tests.Input("sphere")).RescuePasses);
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var d = new MeshTetrahedralization(input) { RelativeAlpha = 12, RelativeOffset = 200, SplitEdges = false, ContractEdges = false, ImprovePasses = 3 };
            Assert.True(d.Compute());
            Assert.True(d.Counts["improve_rescue_passes"] > 0);
            var e = Short(input, 150); Assert.True(e.Compute());
            Assert.Equal(e.Result.Tets, d.Result.Tets); Assert.Equal(e.Result.Vertices, d.Result.Vertices);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Rescue_KeepsTheInvariants_AndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Torus(48, 12, 1, 0.15);
            var a = Short(input, 150); Assert.True(a.Compute());
            var b = Short(Invariance.Transformed(input, s), 150); Assert.True(b.Compute());
            TetMeshingStage2Tests.Invariants(b);
            Assert.True(a.Counts["improve_rescue_passes"] > 0);
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            }
            if (simName == "identity") {
                var none = Short(input, 0); Assert.True(none.Compute());
                Assert.True(TetMeshQuality.Measure(a.Result).MinDihedral.Worst > TetMeshQuality.Measure(none.Result).MinDihedral.Worst);
                TetMesh3 first = a.Result;
                Assert.True(a.Compute());                                                // reuse: the same instance again
                Assert.Equal(first.Vertices, a.Result.Vertices); Assert.Equal(first.Tets, a.Result.Tets);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
