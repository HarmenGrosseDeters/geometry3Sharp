// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The default resolution of MeshTetrahedralization (docs/design/TET_MESHING.md section 17.5, stage P1): RelativeAlpha chosen by the
    /// runner sample. The result keeps the library's rules at the default: 2^k scales bit for bit, every MaxThreads the same result
    /// (TetMeshingStage2Tests runs at the default: the reuse test, and every tet >= 10 degrees with the invariants on the procedural set).
    /// </summary>
    public class TetMeshingDefaultTests
    {
        internal const double P1Alpha = 40;

        [Fact]
        public void Default_RelativeAlpha_IsTheValueOfTheRunnerSample()
        {
            var tm = new MeshTetrahedralization(new DMesh3());
            Assert.Equal(P1Alpha, tm.RelativeAlpha);
            Assert.Equal(600, tm.RelativeOffset);
        }

        [Fact]
        public void Default_EveryMaxThreads_GivesTheSameTets()
        {
            DMesh3 input = TetMeshingStage2Tests.Input("torus thin");
            var serial = new MeshTetrahedralization(input) { MaxThreads = 1 };
            Assert.True(serial.Compute());
            foreach (int threads in new[] { 0, 2, 4 }) {
                var tm = new MeshTetrahedralization(input) { MaxThreads = threads };
                Assert.True(tm.Compute());
                Assert.Equal(serial.Result.Vertices, tm.Result.Vertices);
                Assert.Equal(serial.Result.Tets, tm.Result.Tets);
                Assert.Equal(serial.Result.BoundaryTriangles, tm.Result.BoundaryTriangles);
                Assert.Equal(serial.Counts, tm.Counts);
            }
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Default_Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = TetMeshingStage2Tests.Input("torus thin");
            MeshTetrahedralization a = Reference.Value;
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)); Assert.True(b.Compute());
            TetMeshingStage2Tests.Invariants(b);
            Assert.True(a.Counts["improve_edge_removals"] > 0 && a.Counts["improve_boundary_moves"] > 0);   // stage 2 does work here
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else
                Assert.True(TetMeshQuality.Measure(b.Result).MinDihedral.Worst >= 10 || TetMeshQuality.Measure(b.Result).MinDihedral.Worst >=
                    0.5 * TetMeshQuality.Measure(a.Result).MinDihedral.Worst);
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // the untransformed run, shared by the cases (deterministic, so computing it once is the same as each time)
        static readonly System.Lazy<MeshTetrahedralization> Reference = new System.Lazy<MeshTetrahedralization>(() => {
            var tm = new MeshTetrahedralization(TetMeshingStage2Tests.Input("torus thin")); Assert.True(tm.Compute()); return tm;
        });
    }
}
