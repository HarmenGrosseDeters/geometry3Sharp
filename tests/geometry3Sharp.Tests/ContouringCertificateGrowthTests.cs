using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using static geometry3Sharp.Tests.ContouringTopologyCertificateTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The growth of the block test (remark 3 of Theorem 6, docs/design/CONTOURING_TOPOLOGY.md section 7.12): a cluster of
    /// uncertified cells that fails as it is (not a ball, or a count) is grown by layers of the cells around it, taking in
    /// the clusters and blocks it reaches, and tried again. Report only: the contour does not change.
    /// </summary>
    public class ContouringCertificateGrowthTests
    {
        static readonly Matrix3d Tilt = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 11);

        static MeshWindingNumberSolidify Solidify(DMesh3 mesh, int res, int growRounds, double cellSize = 0, int refineLevels = 2,
            bool parallel = true)
        {
            var s = new MeshWindingNumberSolidify(mesh) {
                Adaptive = true, GridResolution = res, CellSize = cellSize, ComputeTopologyCertificate = true, CleanupResult = false,
                ReduceTolerance = 0, MaxRefineLevels = refineLevels, CertificateGrowRounds = growRounds, CertificateParallel = parallel,
                CertificateMinChunk = 64
            };
            // (the invariants of Theorem 6 on the blocks: disjoint, clusters whole, condition 2; a violation throws)
            WindingContouring.CheckBlocksForTests = true;
            int checkedBefore = WindingContouring.BlocksChecked;
            try { Assert.True(s.Compute()); } finally { WindingContouring.CheckBlocksForTests = false; }
            if (s.CertificateBlocksCertified > 0) Assert.True(WindingContouring.BlocksChecked > checkedBefore);
            return s;
        }

        [Fact]
        public void Growth_IsWhatCertifiesTheClustersThatAreNotBalls()
        {
            foreach (var (name, m, res) in ContouringCertificateBlockTests.NotBallClusters()) {
                MeshWindingNumberSolidify g0 = Solidify(m, res, 0);
                Assert.Equal(ContouringCertificateStatus.CellsUncertified, g0.TopologyCertificateStatus);
                Assert.Equal(0, g0.CertificateBlocksGrown + g0.CertificateGrownCells);
                // left by the block test as it is: clusters that are not balls, only
                Assert.True(g0.CertificateBlockLeft[2] > 0, name);
                Assert.Equal(g0.UncertifiedCells.Count, g0.CertificateBlockLeft.Sum());
                MeshWindingNumberSolidify g = Solidify(m, res, 2);
                Assert.Equal(ContouringCertificateStatus.Certified, g.TopologyCertificateStatus);
                Assert.True(g.CertificateBlocksGrown > 0 && g.CertificateGrownCells > 0, name);
                Assert.Equal(0, g.CertificateBlockLeft.Sum());
                Assert.Equal(g0.UncertifiedCells.Count + g0.CertificateBlockCells, g.CertificateBlockCells);
                Assert.Equal(WindingContouringCertificateTests.Truth(m), WindingContouringCertificateTests.Topology(g.Result));
                ContouringCertificateBlockTests.AssertClosedManifold(g.Result);
                // report only: the same contour, bit for bit
                Assert.Equal(g0.Result.TriangleCount, g.Result.TriangleCount);
                foreach (int v in g0.Result.VertexIndices()) Assert.Equal(g0.Result.GetVertex(v), g.Result.GetVertex(v));
                foreach (int t in g0.Result.TriangleIndices()) Assert.Equal(g0.Result.GetTriangle(t), g.Result.GetTriangle(t));
            }
        }

        [Fact]
        public void Growth_OneRoundIsNotAlwaysEnough()
        {
            // a tilted box at / 20 needs its second layer: one round leaves 15 cells uncertified, two certify it
            Matrix3d T51 = Matrix3d.AxisAngleD(Vector3d.AxisZ, 51) * Matrix3d.AxisAngleD(Vector3d.AxisX, 28.5);
            DMesh3 m = Box(new Vector3d(0.01, 0.02, 0.03), new Vector3d(0.6, 0.4, 0.3), T51);
            MeshWindingNumberSolidify g1 = Solidify(m, 20, 1), g2 = Solidify(m, 20, 2);
            Assert.Equal(ContouringCertificateStatus.CellsUncertified, g1.TopologyCertificateStatus);
            Assert.Equal(15, g1.UncertifiedCells.Count);
            Assert.Equal(ContouringCertificateStatus.Certified, g2.TopologyCertificateStatus);
            Assert.True(g2.CertificateBlocksGrown > g1.CertificateBlocksGrown);
            // the second layer reaches two other clusters and two certified blocks, which the grown block takes in
            Assert.Equal(2, g2.CertificateClustersTakenIn);
            Assert.Equal(2, g2.CertificateBlocksTakenIn);
            Assert.Equal(WindingContouringCertificateTests.Truth(m), WindingContouringCertificateTests.Topology(g2.Result));
            ContouringCertificateBlockTests.AssertClosedManifold(g2.Result);
        }

        [Fact]
        public void Growth_NeverCertifiesAWrongContour()
        {
            // thin features (half a cell) the contour gets wrong: grown blocks are tried in these runs and never certify one;
            // each wrong run is checked on its own
            const double cell = 0.06;
            double w = 0.5 * cell;
            Vector3d c = new Vector3d(0.0123, 0.0217, 0.0311);
            var cases = new (string, DMesh3)[] {
                ("torus", Torus(c, 0.6, w / 2, 96, 8, Tilt)),
                ("gap", Union(Sphere(c - new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24), Sphere(c + new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24))),
                ("shell", Union(Sphere(c, 0.8, 32), Reversed(Sphere(c, 0.8 - w, 32)))),
                ("tunnel", Torus(c, 0.4 + 0.25 * w, 0.4 - 0.25 * w, 64, 32, Tilt)),
                ("thin box", Box(c, new Vector3d(0.6, 0.4, 0.25 * cell), Tilt)),
            };
            int wrong = 0, grownTries = 0;
            foreach (var (name, m) in cases) {
                MeshWindingNumberSolidify s = Solidify(m, 0, 2, cell, 0);
                string truth = WindingContouringCertificateTests.Truth(m), got = WindingContouringCertificateTests.Topology(s.Result);
                if (got == truth) continue;
                wrong++;
                Assert.False(s.ContourTopologyCertified, name + ": certified, but " + got + " != " + truth);
                Assert.False(s.TopologyCertified);
                Assert.NotEmpty(s.UncertifiedCells);
                grownTries += s.CertificateBlocksTried - Solidify(m, 0, 0, cell, 0).CertificateBlocksTried;
            }
            Assert.True(wrong >= 3, "too few wrong results: " + wrong);
            Assert.True(grownTries > 0, "no grown block tried in the wrong runs");
        }

        [Fact]
        public void Invariance_Similarities()
        {
            // the 2^k scales: the same status, blocks, grown cells and uncertified cells, bit for bit; the others: certified
            // implies the right topology
            foreach (var (name, m, res) in ContouringCertificateBlockTests.NotBallClusters()) {
                MeshWindingNumberSolidify b = Solidify(m, res, 2);
                Assert.True(b.CertificateBlocksGrown > 0);
                string truth = WindingContouringCertificateTests.Truth(m);
                foreach (Similarity sim in Similarity.All()) {
                    DMesh3 mm = new DMesh3(m);
                    MeshTransforms.PerVertexTransform(mm, sim.Apply);
                    MeshWindingNumberSolidify s = Solidify(mm, res, 2);
                    if (s.ContourTopologyCertified) Assert.Equal(truth, WindingContouringCertificateTests.Topology(s.Result));
                    if (!sim.IsExact) continue;
                    Assert.Equal(b.TopologyCertificateStatus, s.TopologyCertificateStatus);
                    Assert.Equal(b.CertificateBlocksTried, s.CertificateBlocksTried);
                    Assert.Equal(b.CertificateBlocksCertified, s.CertificateBlocksCertified);
                    Assert.Equal(b.CertificateBlocksGrown, s.CertificateBlocksGrown);
                    Assert.Equal(b.CertificateGrownCells, s.CertificateGrownCells);
                    Assert.Equal(b.CertificateBlockCells, s.CertificateBlockCells);
                    Assert.Equal(b.UncertifiedCells.Count, s.UncertifiedCells.Count);
                }
            }
        }

        [Fact]
        public void Compute_Twice_GivesTheSameGrowth()
        {
            var (name, m, res) = ContouringCertificateBlockTests.NotBallClusters().First();
            var s = new MeshWindingNumberSolidify(m) { Adaptive = true, GridResolution = res, ComputeTopologyCertificate = true, CleanupResult = false, ReduceTolerance = 0 };
            Assert.True(s.Compute());
            int tried = s.CertificateBlocksTried, grown = s.CertificateBlocksGrown, cells = s.CertificateGrownCells;
            Assert.True(grown > 0);
            Assert.Equal(ContouringCertificateStatus.Certified, s.TopologyCertificateStatus);
            Assert.True(s.Compute());
            Assert.Equal(tried, s.CertificateBlocksTried);
            Assert.Equal(grown, s.CertificateBlocksGrown);
            Assert.Equal(cells, s.CertificateGrownCells);
            Assert.Equal(ContouringCertificateStatus.Certified, s.TopologyCertificateStatus);
            // switched off: no stale counts
            s.ComputeTopologyCertificate = false;
            Assert.True(s.Compute());
            Assert.Equal(0, s.CertificateBlocksGrown + s.CertificateGrownCells + s.CertificateBlockLeft.Sum());
        }

        [Fact]
        public void Parallel_EqualsSerial_Repeated()
        {
            // the cell tests run in parallel, the blocks and their growth serially on their result: the same in every run
            foreach (var (name, m, res) in ContouringCertificateBlockTests.NotBallClusters()) {
                MeshWindingNumberSolidify reference = null;
                for (int rep = 0; rep < 2; ++rep)
                    foreach (bool parallel in new[] { false, true }) {
                        MeshWindingNumberSolidify s = Solidify(m, res, 2, 0, 2, parallel);
                        if (reference == null) { reference = s; continue; }
                        Assert.Equal(reference.TopologyCertificateStatus, s.TopologyCertificateStatus);
                        Assert.Equal(reference.CertificateBlocksTried, s.CertificateBlocksTried);
                        Assert.Equal(reference.CertificateBlocksGrown, s.CertificateBlocksGrown);
                        Assert.Equal(reference.CertificateGrownCells, s.CertificateGrownCells);
                        Assert.Equal(reference.UncertifiedCells.Count, s.UncertifiedCells.Count);
                    }
            }
        }
    }
}
