using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class TpmsLatticeTests
    {
        const double L = 1.0;

        // An independent copy of the five closed forms (cos 2x written as 2 cos^2 x - 1), shares no code with the library.
        static double F(TpmsType type, Vector3d q)
        {
            double a = 2 * Math.PI * q.x / L, b = 2 * Math.PI * q.y / L, c = 2 * Math.PI * q.z / L;
            double ca = Math.Cos(a), cb = Math.Cos(b), cc = Math.Cos(c), sa = Math.Sin(a), sb = Math.Sin(b), sc = Math.Sin(c);
            switch (type) {
                case TpmsType.Gyroid: return sa * cb + sb * cc + sc * ca;
                case TpmsType.SchwarzP: return ca + cb + cc;
                case TpmsType.SchwarzD: return sa * sb * sc + sa * cb * cc + ca * sb * cc + ca * cb * sc;
                case TpmsType.Neovius: return 3 * (ca + cb + cc) + 4 * ca * cb * cc;
                default: return 2 * (ca * cb + cb * cc + cc * ca) - (2 * ca * ca - 1 + 2 * cb * cb - 1 + 2 * cc * cc - 1);
            }
        }

        static double G(TpmsType type, TpmsStyle style, Vector3d q)
        {
            double f = F(type, q);
            return style == TpmsStyle.Sheet ? Math.Abs(f) : (style == TpmsStyle.SkeletalLow ? f : -f);
        }

        // first-order distance of q to the level set {G = level}: (G - level) / |grad G| by central differences
        static double Distance(TpmsType type, TpmsStyle style, double level, Vector3d q)
        {
            const double e = 1e-5;
            double gx = (G(type, style, q + new Vector3d(e, 0, 0)) - G(type, style, q - new Vector3d(e, 0, 0))) / (2 * e);
            double gy = (G(type, style, q + new Vector3d(0, e, 0)) - G(type, style, q - new Vector3d(0, e, 0))) / (2 * e);
            double gz = (G(type, style, q + new Vector3d(0, 0, e)) - G(type, style, q - new Vector3d(0, 0, e))) / (2 * e);
            double norm = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            return (G(type, style, q) - level) / Math.Max(norm, 1e-9);
        }

        static TpmsLattice Make(TpmsType type, TpmsStyle style, double fraction = 0.3, int divisions = 16, double size = 2)
        {
            return new TpmsLattice {
                Type = type, Style = style, CellSize = L, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(size, size, size))
            };
        }

        public static IEnumerable<object[]> Combinations()
        {
            foreach (TpmsType t in Enum.GetValues(typeof(TpmsType)))
                foreach (TpmsStyle s in Enum.GetValues(typeof(TpmsStyle)))
                    yield return new object[] { t, s };
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void Compute_IsCleanAndNearTheTargetFraction(TpmsType type, TpmsStyle style)
        {
            var lattice = Make(type, style, 0.3, 14);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Assert.True(Invariance.AllFinite(lattice.Mesh));
            // two whole periods: the realized fraction is the target up to the cut-edge rounding and the discretisation
            Assert.InRange(lattice.RealizedVolumeFraction, 0.3 - 0.05, 0.3 + 0.05);
            // the cut planes are the box planes: the mesh does not pass them (up to the rounding of a cut edge)
            double h = L / 14;
            DMesh3 m = lattice.Mesh;
            AxisAlignedBox3d bounds = m.GetBounds();
            for (int d = 0; d < 3; ++d) {
                Assert.True(bounds.Min[d] >= -0.05 * h, "below the box by " + bounds.Min[d]);
                Assert.True(bounds.Max[d] <= 2 + 0.05 * h, "above the box by " + (bounds.Max[d] - 2));
            }
            Assert.True(lattice.MeanWallThickness > 0);
            Assert.True(lattice.CellsEvaluated > 0);
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, 0.2)]
        [InlineData(TpmsType.SchwarzP, 0.5)]
        [InlineData(TpmsType.IWP, 0.7)]
        public void Compute_HandlesOtherFractions(TpmsType type, double fraction)
        {
            var lattice = Make(type, TpmsStyle.SkeletalLow, fraction, 16);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Assert.InRange(lattice.RealizedVolumeFraction, fraction - 0.05, fraction + 0.05);
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void QuantileTable_MatchesAFinerIndependentTable(TpmsType type, TpmsStyle style)
        {
            double[] table = TpmsLattice.QuantileTable(type, style);
            double[] fine = TpmsLattice.BuildTable(type, style, 112);
            Assert.Equal(fine.Length, table.Length);
            double range = fine[fine.Length - 1] - fine[0];
            for (int i = 0; i < table.Length; ++i) {
                if (i > 0) Assert.True(table[i] >= table[i - 1]);
                Assert.InRange(Math.Abs(table[i] - fine[i]), 0, 0.02 * range);
            }
            // the table is the distribution of the independent formula: count a random sample below each level
            var rand = new Random(5);
            const int n = 100000;
            var below = new int[3];
            double[] fractions = { 0.2, 0.5, 0.8 };
            for (int i = 0; i < n; ++i) {
                var q = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                double g = G(type, style, q);
                for (int j = 0; j < 3; ++j)
                    if (g <= TpmsLattice.LevelForFraction(table, fractions[j])) below[j]++;
            }
            for (int j = 0; j < 3; ++j)
                Assert.InRange((double)below[j] / n, fractions[j] - 0.007, fractions[j] + 0.007);
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, TpmsStyle.Sheet)]
        [InlineData(TpmsType.SchwarzD, TpmsStyle.SkeletalLow)]
        [InlineData(TpmsType.Neovius, TpmsStyle.SkeletalHigh)]
        [InlineData(TpmsType.IWP, TpmsStyle.Sheet)]
        public void Mesh_AgreesWithTheFieldAwayFromTheSurface(TpmsType type, TpmsStyle style)
        {
            var lattice = Make(type, style, 0.35, 16);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            double h = L / 16, lw = TpmsLattice.GradientBound(type) * 2 * Math.PI / L;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(type, style), 0.35);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(11);
            int checkedPoints = 0;
            for (int i = 0; i < 4000; ++i) {
                var q = new Vector3d(0.2 + 1.6 * rand.NextDouble(), 0.2 + 1.6 * rand.NextDouble(), 0.2 + 1.6 * rand.NextDouble());
                double dist = Distance(type, style, level, q);
                if (Math.Abs(dist) < 1.5 * h) continue;
                ++checkedPoints;
                Assert.Equal(dist < 0, tree.IsInside(q));
            }
            Assert.True(checkedPoints > 300, "only " + checkedPoints + " points");
        }

        [Fact]
        public void Mesh_IsPeriodic()
        {
            var a = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16);
            a.Compute();
            var b = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16);
            b.Box = new AxisAlignedBox3d(new Vector3d(1, 1, 1), new Vector3d(3, 3, 3));
            b.Compute();
            Assert.True(a.Status == TpmsStatus.Ok, a.Status + ": " + a.Message);
            Assert.True(b.Status == TpmsStatus.Ok, b.Status + ": " + b.Message);
            Assert.Equal(a.Mesh.TriangleCount, b.Mesh.TriangleCount);
            Assert.Equal(a.Mesh.VertexCount, b.Mesh.VertexCount);
            Assert.Equal(1.0, Invariance.Area(b.Mesh) / Invariance.Area(a.Mesh), 9);
            var tree = new DMeshAABBTree3(a.Mesh, true);
            double max = 0;
            foreach (Vector3d v in b.Mesh.Vertices()) {
                int tid = tree.FindNearestTriangle(v - new Vector3d(1, 1, 1), out double d2);
                max = Math.Max(max, Math.Sqrt(d2));
            }
            Assert.True(max < 1e-9, "max vertex distance " + max);
        }

        [Fact]
        public void Field_HasTheCubicSymmetries()
        {
            var rand = new Random(3);
            for (int i = 0; i < 200; ++i) {
                var q = new Vector3d(rand.NextDouble() * 3 - 1, rand.NextDouble() * 3 - 1, rand.NextDouble() * 3 - 1);
                foreach (TpmsType t in Enum.GetValues(typeof(TpmsType))) {
                    double f = F(t, q);
                    Assert.Equal(f, F(t, q + new Vector3d(L, 0, 0)), 9);
                    Assert.Equal(f, F(t, q + new Vector3d(0, L, 0)), 9);
                    Assert.Equal(f, F(t, q + new Vector3d(0, 0, L)), 9);
                    // the library's evaluation agrees with the independent one
                    double k = 2 * Math.PI / L;
                    Assert.Equal(f, TpmsLattice.Evaluate(t, k * q.x, k * q.y, k * q.z), 9);
                    // the cyclic permutation is a symmetry of all five
                    Assert.Equal(f, F(t, new Vector3d(q.y, q.z, q.x)), 9);
                    if (t == TpmsType.Gyroid) continue;   // the gyroid is chiral: no swap, no mirror about a coordinate plane
                    Assert.Equal(f, F(t, new Vector3d(q.y, q.x, q.z)), 9);
                    if (t != TpmsType.SchwarzD)
                        Assert.Equal(f, F(t, new Vector3d(-q.x, q.y, q.z)), 9);
                }
                // the gyroid is odd under the central inversion
                Assert.Equal(-F(TpmsType.Gyroid, q), F(TpmsType.Gyroid, new Vector3d(-q.x, -q.y, -q.z)), 9);
            }
        }

        [Fact]
        public void Mesh_SwapsAxesForSchwarzP()
        {
            var lattice = Make(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.4, 16);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            double h = L / 16, max = 0;
            foreach (Vector3d v in lattice.Mesh.Vertices()) {
                tree.FindNearestTriangle(new Vector3d(v.y, v.x, v.z), out double d2);
                max = Math.Max(max, Math.Sqrt(d2));
            }
            Assert.True(max < 0.25 * h, "max distance " + max);
        }

        [Fact]
        public void Compute_IsInvariantUnderSimilarities()
        {
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 12, 1.5);
                lattice.CellSize = s.Scale * L;
                lattice.Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1.5 * s.Scale, 1.5 * s.Scale, 1.5 * s.Scale));
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.GridShiftsUsed, lattice.GridShiftsUsed);
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, lattice.RealizedVolumeFraction, 9);
                DMesh3 back = Invariance.MappedBack(lattice.Mesh, s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, back));
                else {
                    double tol = 1e-6 * L;
                    foreach (int vid in back.VertexIndices())
                        Assert.True((back.GetVertex(vid) - reference.Mesh.GetVertex(vid)).Length < tol, s.Name);
                }
            }
        }

        [Fact]
        public void Compute_SnapsNodesOnTheLevelInsteadOfRetrying_AndIsInvariant()
        {
            // The first grid is the lattice grid moved down by half a cube. The box [h/2, L + h/2]^3 has its faces on the planes of
            // those nodes: samples of the box distance are exactly 0 there, a sample on the level. Such values are snapped inside
            // (the sliver guard), so the first grid works: no retry, under every similarity (the guard is 2^-8 cube, scale exact).
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.5, 16, 1);
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
        public void Compute_RetriesTheGridWhenTheFirstOneIsRejected_AndIsInvariant()
        {
            // The retry path itself (the guard makes a real on-level sample rare): an internal hook rejects the first attempt; the
            // shifts are defined in the lattice frame, so every similarity uses the same one and the 2^k scales stay bit for bit.
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.5, 16, 1);
                lattice.CellSize = s.Scale * L;
                lattice.Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale);
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.RejectAttempt = a => a < 2;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                Assert.Equal(2, lattice.GridShiftsUsed);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(lattice.Mesh, s)));
            }
        }

        [Fact]
        public void SliverGuard_GradientRatioIsTheSampledOverTheProvenBound()
        {
            // the ratio sizes the guard: in (0.2, 1] and consistent with the independent finite-difference maximum of the test copy
            foreach (TpmsType t in Enum.GetValues(typeof(TpmsType))) {
                double ratio = TpmsLattice.GradientRatio(t);
                Assert.InRange(ratio, 0.2, 1.0);
                var rand = new Random(71);
                double max = 0, e = 1e-6;
                for (int i = 0; i < 20000; ++i) {
                    var q = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                    double gx = (F(t, q + new Vector3d(e, 0, 0)) - F(t, q - new Vector3d(e, 0, 0))) / (2 * e);
                    double gy = (F(t, q + new Vector3d(0, e, 0)) - F(t, q - new Vector3d(0, e, 0))) / (2 * e);
                    double gz = (F(t, q + new Vector3d(0, 0, e)) - F(t, q - new Vector3d(0, 0, e))) / (2 * e);
                    max = Math.Max(max, Math.Sqrt(gx * gx + gy * gy + gz * gz) / (2 * Math.PI));
                }
                Assert.True(ratio * TpmsLattice.GradientBound(t) >= max, t + ": the sampled maximum " + max + " exceeds ratio * bound");
                Assert.True(ratio * TpmsLattice.GradientBound(t) <= 1.2 * max, t + ": the ratio is far above the independent sampled maximum");
            }
        }

        [Fact]
        public void SliverGuard_MovesNearLevelValuesDownAndOnlyThose()
        {
            // pins the direction (down: the solid only grows, so the enclosure survives) and the range of the guard
            const double iso = 0.25, snap = 0.01;
            Assert.Equal(iso - snap, TpmsLattice.SliverGuard(iso, iso, snap));                // on the level
            Assert.Equal(iso - snap, TpmsLattice.SliverGuard(iso + 0.5 * snap, iso, snap));   // slightly outside
            Assert.Equal(iso - snap, TpmsLattice.SliverGuard(iso - 0.5 * snap, iso, snap));   // slightly inside
            Assert.Equal(iso + 2 * snap, TpmsLattice.SliverGuard(iso + 2 * snap, iso, snap)); // far values unchanged
            Assert.Equal(iso - 2 * snap, TpmsLattice.SliverGuard(iso - 2 * snap, iso, snap));
            Assert.Equal(iso, TpmsLattice.SliverGuard(iso, iso, 0));                          // off: untouched
            foreach (double v in new[] { -1.0, 0.0, iso - 0.02, iso + 0.02, 3.0 })
                Assert.True(TpmsLattice.SliverGuard(v, iso, snap) <= v + 1.5 * snap && TpmsLattice.SliverGuard(v, iso, snap) <= Math.Max(v, iso - snap));
        }

        public static IEnumerable<object[]> CombinationsForTheGuard() => Combinations();

        [Theory]
        [MemberData(nameof(CombinationsForTheGuard))]
        public void SliverGuard_ChangesConstantLatticesNotAtAllOrByLessThanACubeFraction(TpmsType type, TpmsStyle style)
        {
            // On its own grid a constant lattice has a small discrete set of node values. The guard moves a node whose normalised
            // value (g - c) / Lw is within h / 256 of the level; Lw is a proven but loose bound, so for the types whose bound is
            // far above the true gradient (Schwarz D, Neovius: bound / true maximum about 4) that is a wider band of f values and the
            // output of the unguarded code changes. The guard band is scaled by the sampled true gradient over the proven bound (the
            // ratio), which leaves 2 of the 15 cases (Neovius skeletal) different; the other 13 are bit for bit the same.
            // 2 periods, 14 cubes per period.
            var on = Make(type, style, 0.3, 14);
            var off = Make(type, style, 0.3, 14);
            off.SliverGuardCubeFraction = 0;
            on.Compute();
            off.Compute();
            Assert.True(on.Status == TpmsStatus.Ok, on.Status + ": " + on.Message);
            Assert.True(off.Status == TpmsStatus.Ok, off.Status + ": " + off.Message);
            if (!(type == TpmsType.Neovius && style != TpmsStyle.Sheet)) {
                Assert.Equal("", Invariance.Difference(off.Mesh, on.Mesh));
                return;
            }
            // changed: by how much? Every guarded vertex stays within 5 % of a cube of the unguarded surface (and vice versa),
            // and the volume grows by less than 2 %.
            double h = L / 14;
            var treeOff = new DMeshAABBTree3(off.Mesh, true);
            var treeOn = new DMeshAABBTree3(on.Mesh, true);
            double maxOn = 0, maxOff = 0;
            foreach (Vector3d v in on.Mesh.Vertices()) { treeOff.FindNearestTriangle(v, out double d2); maxOn = Math.Max(maxOn, Math.Sqrt(d2)); }
            foreach (Vector3d v in off.Mesh.Vertices()) { treeOn.FindNearestTriangle(v, out double d2); maxOff = Math.Max(maxOff, Math.Sqrt(d2)); }
            Assert.True(maxOn <= 0.05 * h && maxOff <= 0.05 * h, "guarded " + maxOn / h + " h, unguarded " + maxOff / h + " h");
            double ratio = on.RealizedVolumeFraction / off.RealizedVolumeFraction;
            Assert.True(ratio >= 0.999 && ratio <= 1.02, "volume ratio " + ratio);   // the solid only grows
        }

        [Fact]
        public void SliverGuard_OnlyChangesTheNodesItSnaps_AndOnlyOutward()
        {
            // A box with its planes on nodes: the guard moves the on-level nodes inside (by less than 2 / 256 cube). The two meshes
            // differ only near the box planes, and the guarded solid contains the unguarded one (here: more volume, within 1 %).
            double h = L / 16;
            TpmsLattice Run(double guard)
            {
                var l = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.5, 16, 1);
                l.Box = new AxisAlignedBox3d(new Vector3d(1, 1, 1) * (h / 2), new Vector3d(1, 1, 1) * (1 + h / 2));
                l.SliverGuardCubeFraction = guard;
                l.RejectAttempt = a => false;
                l.Compute();
                return l;
            }
            TpmsLattice on = Run(1.0 / 256), off = Run(0);
            Assert.True(on.Status == TpmsStatus.Ok, on.Status + ": " + on.Message);
            // without the guard the first grid throws (a node on the level) and a shifted grid is used
            Assert.Equal(0, on.GridShiftsUsed);
            Assert.True(off.GridShiftsUsed > 0 || off.Status != TpmsStatus.Ok);
            Assert.True(on.RealizedVolumeFraction >= off.RealizedVolumeFraction - 0.01 * off.RealizedVolumeFraction - 1e-3);
        }

        [Fact]
        public void SliverGuard_GuardedPathIsDeterministicAndEnclosureStillEncloses()
        {
            // the guarded path on the large position-dependent case: parallel equals serial, and a repeated run is identical
            Func<TpmsLattice> make = () => new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = L, Divisions = 16, VolumeFraction = 0.317,
                Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(4, 4, 4)),
                VolumeFractionAt = p => 0.317 + 0.05 * Math.Sin(p.x + 2 * p.y)
            };
            TpmsLattice serial = make(), parallel = make(), again = make();
            serial.ParallelCompute = false;
            serial.Compute(); parallel.Compute(); again.Compute();
            Assert.True(serial.Status == TpmsStatus.Ok && parallel.Status == TpmsStatus.Ok && again.Status == TpmsStatus.Ok, serial.Message + parallel.Message + again.Message);
            Assert.Equal("", Invariance.Difference(serial.Mesh, parallel.Mesh));
            Assert.Equal("", Invariance.Difference(parallel.Mesh, again.Mesh));
        }

        [Fact]
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
        public void Compute_CanBeRepeatedAndReconfigured()
        {
            var lattice = Make(TpmsType.SchwarzD, TpmsStyle.Sheet, 0.3, 12);
            lattice.Compute();
            DMesh3 first = lattice.Mesh;
            lattice.Compute();
            Assert.NotSame(first, lattice.Mesh);
            Assert.Equal("", Invariance.Difference(first, lattice.Mesh));
            lattice.Style = TpmsStyle.SkeletalLow;
            lattice.VolumeFraction = 0.5;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.NotEqual("", Invariance.Difference(first, lattice.Mesh));
            // a failed run clears the mesh, a following valid one works again
            lattice.CellSize = -1;
            lattice.Compute();
            Assert.Equal(TpmsStatus.InvalidOptions, lattice.Status);
            Assert.Null(lattice.Mesh);
            lattice.CellSize = L;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
        }

        [Fact]
        public void Compute_ParallelEqualsSerial_AndRepeatedRunsAgree()
        {
            var serial = Make(TpmsType.Neovius, TpmsStyle.SkeletalLow, 0.4, 12);
            serial.ParallelCompute = false;
            serial.Compute();
            for (int run = 0; run < 3; ++run) {
                var parallel = Make(TpmsType.Neovius, TpmsStyle.SkeletalLow, 0.4, 12);
                parallel.ParallelCompute = true;
                parallel.Compute();
                Assert.Equal("", Invariance.Difference(serial.Mesh, parallel.Mesh));
            }
        }

        [Fact]
        public void Compute_ReportsTooLargeBeforeAllocating()
        {
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 512, 100);
            long before = GC.GetAllocatedBytesForCurrentThread();
            lattice.Compute();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(TpmsStatus.TooLarge, lattice.Status);
            Assert.Null(lattice.Mesh);
            Assert.True(allocated < 200_000, "allocated " + allocated);
            // a limit just below the node count: too large; just above: ok
            var small = Make(TpmsType.SchwarzP, TpmsStyle.Sheet, 0.3, 8, 1);
            small.MaxCells = 100;
            small.Compute();
            Assert.Equal(TpmsStatus.TooLarge, small.Status);
            small.MaxCells = 1 << 20;
            small.Compute();
            Assert.True(small.Status == TpmsStatus.Ok, small.Status + ": " + small.Message);
        }

        [Fact]
        public void Compute_RejectsBadOptions()
        {
            void Expect(Action<TpmsLattice> change, TpmsStatus status)
            {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.Sheet);
                change(lattice);
                lattice.Compute();
                Assert.Equal(status, lattice.Status);
                Assert.Null(lattice.Mesh);
                Assert.NotEqual("", lattice.Message);
            }
            Expect(l => l.CellSize = 0, TpmsStatus.InvalidOptions);
            Expect(l => l.CellSize = double.NaN, TpmsStatus.InvalidOptions);
            Expect(l => l.Divisions = 3, TpmsStatus.InvalidOptions);
            Expect(l => l.VolumeFraction = 1, TpmsStatus.InvalidOptions);
            Expect(l => l.VolumeFraction = double.NaN, TpmsStatus.InvalidOptions);
            Expect(l => l.Origin = new Vector3d(double.NaN, 0, 0), TpmsStatus.InvalidOptions);
            Expect(l => l.Rotation = Matrix3d.Identity * 2, TpmsStatus.InvalidOptions);
            Expect(l => { l.Style = TpmsStyle.SkeletalLow; l.WallThickness = 0.1; }, TpmsStatus.InvalidOptions);
            Expect(l => l.WallThickness = 0.9, TpmsStatus.InvalidOptions);
            Expect(l => { l.Enclosure = true; l.VolumeFractionAt = p => 0.3; }, TpmsStatus.InvalidOptions);
            Expect(l => l.Box = new AxisAlignedBox3d(Vector3d.Zero, Vector3d.Zero), TpmsStatus.InvalidDomain);
            Expect(l => l.Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, double.PositiveInfinity, 1)), TpmsStatus.InvalidDomain);
            Expect(l => { l.Enclosure = true; l.DomainMesh = Sphere(1, Vector3d.Zero); }, TpmsStatus.InvalidOptions);
            Expect(l => { l.Enclosure = true; l.Box = new AxisAlignedBox3d(new Vector3d(1e9, 0, 0), new Vector3d(1e9 + 2, 2, 2)); }, TpmsStatus.InvalidOptions);
            // enclosure needs an exact dyadic grid
            Expect(l => { l.Enclosure = true; l.CellSize = 0.3; }, TpmsStatus.InvalidOptions);
            var cancelled = Make(TpmsType.Gyroid, TpmsStyle.Sheet);
            cancelled.CancelF = () => true;
            cancelled.Compute();
            Assert.Equal(TpmsStatus.Cancelled, cancelled.Status);
        }

        static DMesh3 Sphere(double radius, Vector3d center)
        {
            var gen = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = 16 };
            DMesh3 m = gen.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, m.GetVertex(v) + center);
            return m;
        }

        [Fact]
        public void MeshDomain_ClipsToTheInsideOfAClosedMesh()
        {
            Vector3d center = new Vector3d(0.3, 0.2, 0.1);
            var lattice = new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.SkeletalLow, CellSize = L, Divisions = 20, VolumeFraction = 0.5,
                DomainMesh = Sphere(1.2, center)
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            // inside the sphere, up to the faceting and the cut-edge rounding
            double maxR = lattice.Mesh.Vertices().Max(v => (v - center).Length);
            Assert.True(maxR < 1.2 + 0.1, "max radius " + maxR);
            Assert.InRange(lattice.RealizedVolumeFraction, 0.5 - 0.08, 0.5 + 0.08);
            // the same lattice phase as a box: points far from both surfaces agree with the field
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            double h = L / 20, lw = TpmsLattice.GradientBound(TpmsType.Gyroid) * 2 * Math.PI / L;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(TpmsType.Gyroid, TpmsStyle.SkeletalLow), 0.5);
            var rand = new Random(2);
            int n = 0;
            for (int i = 0; i < 3000; ++i) {
                var q = center + new Vector3d(rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1);
                if ((q - center).Length > 1.0) continue;
                double dist = Distance(TpmsType.Gyroid, TpmsStyle.SkeletalLow, level, q);
                if (Math.Abs(dist) < 2 * h) continue;
                ++n;
                Assert.Equal(dist < 0, tree.IsInside(q));
            }
            Assert.True(n > 200, "only " + n);
            // an open mesh is not a domain
            DMesh3 open = Sphere(1, Vector3d.Zero);
            open.RemoveTriangle(0);
            var bad = new TpmsLattice { DomainMesh = open };
            bad.Compute();
            Assert.Equal(TpmsStatus.InvalidDomain, bad.Status);
        }

        [Fact]
        public void Frame_RotationMapsTheLattice()
        {
            var lattice = Make(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.4, 12, 1);
            lattice.Origin = new Vector3d(5, -3, 2);
            lattice.Rotation = Matrix3d.AxisAngleD(Vector3d.AxisZ, 30);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Vector3d c = lattice.Origin + lattice.Rotation * new Vector3d(0.5, 0.5, 0.5);
            Assert.True(lattice.Mesh.Vertices().All(v => (v - c).Length < 1.0));
        }

        [Fact]
        public void WallThickness_IsAboutTheRequestedValue()
        {
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 24, 2);
            lattice.VolumeFraction = 0.5;
            lattice.WallThickness = 0.1;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            // first-order map, so a loose bound; the report is the measured value
            Assert.InRange(lattice.MeanWallThickness, 0.07, 0.13);
            double expected = TpmsLattice.AreaRatio(TpmsType.Gyroid);
            Assert.InRange(expected, 2.9, 3.3);   // the gyroid's area per period is about 3.09 L^2
        }

        [Fact]
        public void Grading_ChangesTheLocalVolumeFraction()
        {
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 4);
            lattice.VolumeFractionAt = p => p.x < 2 ? 0.25 : 0.6;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(4);
            int low = 0, high = 0, nLow = 0, nHigh = 0;
            for (int i = 0; i < 6000; ++i) {
                var q = new Vector3d(0.3 + 3.4 * rand.NextDouble(), 0.3 + 3.4 * rand.NextDouble(), 0.3 + 3.4 * rand.NextDouble());
                bool inside = tree.IsInside(q);
                if (q.x < 1.7) { ++nLow; if (inside) ++low; }
                if (q.x > 2.3) { ++nHigh; if (inside) ++high; }
            }
            Assert.InRange((double)low / nLow, 0.2, 0.3);
            Assert.InRange((double)high / nHigh, 0.55, 0.65);
        }

        [Fact]
        public void Enclosure_ContainsTheExactLattice()
        {
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 2);
            lattice.Enclosure = true;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            AssertContainsLatticePoints(lattice, TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 4000);
            var plain = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 2);
            plain.Compute();
            Assert.True(lattice.RealizedVolumeFraction > plain.RealizedVolumeFraction, "the enclosure is larger than the target");
        }

        [Fact]
        public void Enclosure_ContainsTheExactLatticeForASheet()
        {
            var lattice = Make(TpmsType.SchwarzP, TpmsStyle.Sheet, 0.3, 16, 2);
            lattice.Enclosure = true;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            AssertContainsLatticePoints(lattice, TpmsType.SchwarzP, TpmsStyle.Sheet, 0.3, 16, 4000);
        }

        [Fact]
        public void Enclosure_LevelIsAtLeastTheHalfDiagonalPlusPad()
        {
            // Pins the size of the level pad (a half-pad mutant, sqrt(3) h / 4, must fail): the recipe of CONSERVATIVE_MC_AUDIT needs
            // sqrt(3) h / 2 plus the floating point pad 2^-26 (times the power of two above the largest coordinate, here 4).
            foreach (int divisions in new[] { 8, 16, 32 }) {
                var lattice = Make(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.3, divisions, 2);
                lattice.Enclosure = true;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                double h = L / divisions;
                Assert.True(lattice.IsoValueUsed >= Math.Sqrt(3) * h / 2 + 2 * h / 256 * TpmsLattice.GradientRatio(TpmsType.SchwarzP) + 4 * Math.Pow(2, -26), "level " + lattice.IsoValueUsed);
                Assert.True(lattice.IsoValueUsed <= Math.Sqrt(3) * h / 2 + 2 * h / 256 * TpmsLattice.GradientRatio(TpmsType.SchwarzP) + 4 * Math.Pow(2, -22), "level " + lattice.IsoValueUsed);
                var plain = Make(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.3, divisions, 2);
                plain.Compute();
                Assert.Equal(0.0, plain.IsoValueUsed);
            }
        }

        [Fact]
        public void Enclosure_HoldsAtTheBoxFaces_InAFrame()
        {
            // (an enclosure level is never exactly hit by the box distance, so no retry is needed here; the dyadic shifts are
            // used only when a sample does hit the level, which this input cannot be made to do)
            // the whole box including the cut planes must be inside the solid, in a translated frame
            double h = L / 16;
            var lattice = Make(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, 0.3, 16, 2);
            lattice.Box = new AxisAlignedBox3d(new Vector3d(h / 2, h / 2, h / 2), new Vector3d(2 + h / 2, 2 + h / 2, 2 + h / 2));
            lattice.Enclosure = true;
            lattice.Origin = new Vector3d(8, -4, 2);   // a dyadic world position
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            double lw = TpmsLattice.GradientBound(TpmsType.SchwarzP) * 2 * Math.PI / L;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(TpmsType.SchwarzP, TpmsStyle.SkeletalLow), 0.3);
            var rand = new Random(21);
            int tested = 0;
            for (int i = 0; i < 300000 && tested < 3000; ++i) {
                // anywhere in the box, including right at its faces: the cut plane belongs to the solid
                var q = new Vector3d(h / 2 + 2 * rand.NextDouble(), h / 2 + 2 * rand.NextDouble(), h / 2 + 2 * rand.NextDouble());
                double g = (G(TpmsType.SchwarzP, TpmsStyle.SkeletalLow, q) - level) / lw;
                bool nearFace = q.x < h || q.y < h || q.z < h || q.x > 2 || q.y > 2 || q.z > 2;
                if (g > 0 || (g < -0.1 * h && !nearFace)) continue;
                ++tested;
                Assert.True(tree.IsInside(q + lattice.Origin), "box point " + q + " outside (g " + g + ")");
            }
            Assert.True(tested >= 1000, "only " + tested);
        }

        [Fact]
        public void Enclosure_RetryUsesADyadicGridAndStillEncloses()
        {
            // reject the first attempts by hook: the retry shifts must be dyadic multiples of the unit s 2^-40 (s = 4 here)
            double unit = 4 * Math.Pow(2, -40);
            foreach (int rejected in new[] { 1, 2, 3 }) {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 2);
                lattice.Enclosure = true;
                lattice.RejectAttempt = a => a < rejected;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                Assert.Equal(rejected, lattice.GridShiftsUsed);
                Vector3d min = lattice.LastGridMin;
                for (int d = 0; d < 3; ++d)
                    Assert.Equal(Math.Floor(min[d] / unit), min[d] / unit);
                AssertContainsLatticePoints(lattice, TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 2000);
            }
            var all = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16, 2);
            all.Enclosure = true;
            all.RejectAttempt = a => true;
            all.Compute();
            Assert.Equal(TpmsStatus.ContouringFailed, all.Status);
            Assert.Null(all.Mesh);
        }

        static void AssertContainsLatticePoints(TpmsLattice lattice, TpmsType type, TpmsStyle style, double fraction, int divisions, int samples)
        {
            double h = L / divisions, lw = TpmsLattice.GradientBound(type) * 2 * Math.PI / L;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(type, style), fraction);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(8);
            int tested = 0;
            for (int i = 0; i < 400000 && tested < samples; ++i) {
                var q = new Vector3d(0.25 + 1.5 * rand.NextDouble(), 0.25 + 1.5 * rand.NextDouble(), 0.25 + 1.5 * rand.NextDouble());
                double g = (G(type, style, q) - level) / lw;
                // exactly the points of the exact lattice that sit just inside its surface (the hardest ones)
                if (g > 0 || g < -0.1 * h) continue;
                ++tested;
                Assert.True(tree.IsInside(q), "an exact-lattice point " + q + " is outside the enclosure (g " + g + ")");
            }
            Assert.True(tested >= samples / 4, "only " + tested + " near-surface points");
        }
    }
}
