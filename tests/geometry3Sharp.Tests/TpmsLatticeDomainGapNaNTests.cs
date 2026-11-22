using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// TpmsLattice: #1375 a DomainMesh with vertex-id gaps is the same domain as its compact copy (the lattice is
    /// the compact copy's result bit for bit); #1373 a VolumeFractionAt that returns NaN fails with ContouringFailed.
    /// </summary>
    public class TpmsLatticeDomainGapNaNTests
    {
        const double CellSize = 1.0;

        static DMesh3 Box(double half, Vector3d center)
        {
            DMesh3 m = new TrivialBox3Generator { Box = new Box3d(center, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, new Vector3d(half, half, half)) }.Generate().MakeDMesh();
            return m;
        }

        /// <summary> The same mesh with ids shifted by gaps: dummy vertices (and their ids) first, in the middle, then removed. </summary>
        static DMesh3 WithGaps(DMesh3 compact, Vector3d dummy, int gapsFirst, int gapsMiddle)
        {
            var m = new DMesh3();
            var dummies = new System.Collections.Generic.List<int>();
            for (int i = 0; i < gapsFirst; ++i) dummies.Add(m.AppendVertex(dummy));
            var map = new int[compact.MaxVertexID];
            int n = 0;
            foreach (int v in compact.VertexIndices()) {
                if (n++ == 4) for (int i = 0; i < gapsMiddle; ++i) dummies.Add(m.AppendVertex(dummy));
                map[v] = m.AppendVertex(compact.GetVertex(v));
            }
            foreach (int t in compact.TriangleIndices()) {
                Index3i tri = compact.GetTriangle(t);
                m.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
            }
            foreach (int v in dummies) m.RemoveVertex(v);
            return m;
        }

        static TpmsLattice Make(DMesh3 domain)
        {
            return new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.SkeletalLow, CellSize = CellSize, Divisions = 8, VolumeFraction = 0.5,
                DomainMesh = domain
            };
        }

        static void AssertSameMesh(DMesh3 expected, DMesh3 actual)
        {
            Assert.Equal(expected.VertexCount, actual.VertexCount);
            Assert.Equal(expected.TriangleCount, actual.TriangleCount);
            Assert.Equal("", Invariance.Difference(expected, actual));
        }

        [Theory]
        [InlineData(1, 0, 0, 0, 0)]       // dummy at the origin: removed vertex inside the domain
        [InlineData(1, 0, 3, 0, 0)]       // dummy outside the domain (the lattice used to reach x = 2.84)
        [InlineData(2, 3, -2, 5, 1)]
        public void DomainMeshWithIdGaps_IsItsCompactCopy(int gapsFirst, int gapsMiddle, double dx, double dy, double dz)
        {
            DMesh3 compact = Box(1, Vector3d.Zero);
            DMesh3 gaps = WithGaps(compact, new Vector3d(dx, dy, dz), gapsFirst, gapsMiddle);
            Assert.False(gaps.IsCompact);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(gaps));
            var a = Make(compact); a.Compute();
            var b = Make(gaps); b.Compute();
            Assert.True(a.Status == TpmsStatus.Ok, a.Status + ": " + a.Message);
            Assert.True(b.Status == TpmsStatus.Ok, b.Status + ": " + b.Message);
            AssertSameMesh(a.Mesh, b.Mesh);
            Assert.Equal(a.RealizedVolumeFraction, b.RealizedVolumeFraction);
        }

        [Fact]
        public void DomainMeshWithIdGaps_IsInvariantUnderSimilarities_AndReusable()
        {
            DMesh3 gaps = WithGaps(Box(1, Vector3d.Zero), new Vector3d(3, 0, 0), 1, 2);
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = Make(Invariance.Transformed(gaps, s));
                lattice.CellSize = s.Scale * CellSize;
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, s.Name + " " + lattice.Status + ": " + lattice.Message);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                DMesh3 back = Invariance.MappedBack(lattice.Mesh, s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, back));
                else
                    foreach (int vid in back.VertexIndices())
                        Assert.True((back.GetVertex(vid) - reference.Mesh.GetVertex(vid)).Length < 1e-6 * CellSize, s.Name);
            }
            // reuse: a second Compute on one instance gives the same mesh
            var again = Make(gaps); again.Compute();
            DMesh3 first = again.Mesh;
            again.Compute();
            AssertSameMesh(first, again.Mesh);
        }

        [Theory]
        [InlineData(0)]     // NaN in half the box
        [InlineData(1)]     // NaN everywhere
        [InlineData(2)]     // NaN in a small region
        public void VolumeFractionAtReturningNaN_FailsWithContouringFailed(int variant)
        {
            Func<Vector3d, double> frac = variant == 0 ? (p => p.x < 1 ? 0.3 : double.NaN)
                : variant == 1 ? (p => double.NaN)
                : (Func<Vector3d, double>)(p => (p - new Vector3d(1, 1, 1)).Length < 0.4 ? double.NaN : 0.3);
            var lattice = new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.SkeletalLow, CellSize = 1.0, Divisions = 8, VolumeFractionAt = frac,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2))
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.ContouringFailed, "variant " + variant + ": " + lattice.Status + ": " + lattice.Message);
            Assert.Null(lattice.Mesh);
            // reuse: a finite fraction on the same instance succeeds, and then NaN fails again
            lattice.VolumeFractionAt = p => 0.3;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            lattice.VolumeFractionAt = frac;
            lattice.Compute();
            Assert.Equal(TpmsStatus.ContouringFailed, lattice.Status);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VolumeFractionAtReturningNaN_AtOneInteriorNode_FailsEvenWhenItsNeighboursAreVoid(bool parallel)
        {
            // the NaN node counts as outside, so no edge to it crosses and marching cubes never sees it: master returned Ok
            // (found by probing: the interior node (1/8, 1/8, 1/8) of this grid is such a node)
            var node = new Vector3d(0.125, 0.125, 0.125);
            Func<Vector3d, double> frac = p => (p - node).Length < 1e-9 ? double.NaN : 0.3;
            var lattice = new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.SkeletalLow, CellSize = 1.0, Divisions = 4, ParallelCompute = parallel,
                VolumeFractionAt = frac, Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1))
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.ContouringFailed, lattice.Status + ": " + lattice.Message);
            Assert.Null(lattice.Mesh);
            // the same grid without the NaN is Ok (the node is on the grid), and the instance is reusable
            lattice.VolumeFractionAt = p => 0.3;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void VolumeFractionAtReturningNaN_FailsSerialAndParallel(bool parallel)
        {
            var lattice = new TpmsLattice {
                Type = TpmsType.SchwarzP, Style = TpmsStyle.Sheet, CellSize = 1.0, Divisions = 8, ParallelCompute = parallel,
                VolumeFractionAt = p => p.z > 1.5 ? double.NaN : 0.4,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(2, 2, 2))
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.ContouringFailed, lattice.Status + ": " + lattice.Message);
        }
    }
}
