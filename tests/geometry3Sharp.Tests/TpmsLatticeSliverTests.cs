using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Regression tests of the TpmsLattice sliver guard that use only the public API (so they compile, and fail, against the library
    /// before the guard): a large lattice with a position dependent level used to return NotClean.
    /// </summary>
    public class TpmsLatticeSliverTests
    {
        const double L = 1.0;

        static TpmsLattice MakeBox(TpmsType type, TpmsStyle style, double fraction, int divisions, double size)
        {
            return new TpmsLattice {
                Type = type, Style = style, CellSize = L, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(size, size, size))
            };
        }

        [Fact]
        [Trait("Category", "Slow")]   // a lattice of 360 k nodes is needed to hit nodes within rounding of a varying level (about 2 s locally)
        public void Compute_LargeLatticeWithAPositionDependentLevelHasNoSlivers()
        {
            // Found by a large graded lattice: with a level that varies continuously the node values are generic, and on a 4^3 box at
            // 16 cubes per period (360 k nodes) a few nodes lie within rounding of the level. Their crossings sit at cube corners and
            // make degenerate triangles; no grid shift avoids all of them. The sliver guard does. This exact case failed with
            // "degenerate triangles" on all four grids before.
            var lattice = new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = L, Divisions = 16, VolumeFraction = 0.317,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(4, 4, 4)),
                VolumeFractionAt = p => 0.317 + 0.05 * Math.Sin(p.x + 2 * p.y)
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
        }

        [Fact]
        public void Compute_SnapsNodesOnTheLevelInsteadOfRetrying_AndIsInvariant()
        {
            // The first grid is the lattice grid moved down by half a cube. The box [h/2, L + h/2]^3 has its faces on the planes of
            // those nodes: samples of the box distance are exactly 0 there, a sample on the level. Such values are snapped inside
            // (the sliver guard), so the first grid works: no retry, under every similarity (the guard is 2^-8 cube, scale exact).
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = MakeBox(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.5, 16, 1);
                lattice.CellSize = s.Scale * L;
                lattice.Box = new AxisAlignedBox3d(new Vector3d(1, 1, 1) * (s.Scale / 32), new Vector3d(1, 1, 1) * (s.Scale * (1 + 1.0 / 32)));
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                Assert.Equal(0, lattice.GridShiftsUsed);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(lattice.Mesh, s)));
            }
        }

        [Fact]
        public void Compute_GuardedPathIsDeterministic()
        {
            // the guarded path on the large position-dependent case: parallel equals serial, and a repeated run is identical
            Func<TpmsLattice> make = () => new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = L, Divisions = 12, VolumeFraction = 0.317,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(3, 3, 3)),
                VolumeFractionAt = p => 0.317 + 0.05 * Math.Sin(p.x + 2 * p.y)
            };
            TpmsLattice serial = make(), parallel = make(), again = make();
            serial.ParallelCompute = false;
            serial.Compute(); parallel.Compute(); again.Compute();
            Assert.True(serial.Status == TpmsStatus.Ok && parallel.Status == TpmsStatus.Ok && again.Status == TpmsStatus.Ok, serial.Message + parallel.Message + again.Message);
            Assert.Equal("", Invariance.Difference(serial.Mesh, parallel.Mesh));
            Assert.Equal("", Invariance.Difference(parallel.Mesh, again.Mesh));
        }

    }
}
