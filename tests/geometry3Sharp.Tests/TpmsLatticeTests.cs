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

        // An independent copy of the six closed forms (sin 2x written as 2 sin x cos x and cos 2x as 2 cos^2 x - 1), shares no code with the library.
        static double F(TpmsType type, Vector3d q)
        {
            double a = 2 * Math.PI * q.x / L, b = 2 * Math.PI * q.y / L, c = 2 * Math.PI * q.z / L;
            double ca = Math.Cos(a), cb = Math.Cos(b), cc = Math.Cos(c), sa = Math.Sin(a), sb = Math.Sin(b), sc = Math.Sin(c);
            switch (type) {
                case TpmsType.Gyroid: return sa * cb + sb * cc + sc * ca;
                case TpmsType.SchwarzP: return ca + cb + cc;
                case TpmsType.SchwarzD: return sa * sb * sc + sa * cb * cc + ca * sb * cc + ca * cb * sc;
                case TpmsType.Neovius: return 3 * (ca + cb + cc) + 4 * ca * cb * cc;
                case TpmsType.Lidinoid: {
                    double s2a = 2 * sa * ca, s2b = 2 * sb * cb, s2c = 2 * sc * cc, c2a = 2 * ca * ca - 1, c2b = 2 * cb * cb - 1, c2c = 2 * cc * cc - 1;
                    return 0.5 * (s2a * cb * sc + s2b * cc * sa + s2c * ca * sb) - 0.5 * (c2a * c2b + c2b * c2c + c2c * c2a) + 0.15;
                }
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
            // the Lidinoid has a large area per period: it needs more cubes per period to resolve its wall
            double target = type == TpmsType.Lidinoid && style == TpmsStyle.Sheet ? 0.5 : 0.3;   // a thicker wall for the thin Lidinoid sheet
            var lattice = Make(type, style, target, type == TpmsType.Lidinoid ? 22 : 14);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Assert.True(Invariance.AllFinite(lattice.Mesh));
            // two whole periods: the realized fraction is the target up to the cut-edge rounding and the discretisation
            Assert.InRange(lattice.RealizedVolumeFraction, target - 0.05, target + 0.05);
            // the cut planes are the box planes: the mesh does not pass them (up to the rounding of a cut edge)
            double h = L / (type == TpmsType.Lidinoid ? 22 : 14);
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
        [InlineData(TpmsType.Lidinoid, TpmsStyle.SkeletalLow)]
        [InlineData(TpmsType.Lidinoid, TpmsStyle.Sheet)]
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
                    // the cyclic permutation is a symmetry of all six
                    Assert.Equal(f, F(t, new Vector3d(q.y, q.z, q.x)), 9);
                    if (t == TpmsType.Gyroid || t == TpmsType.Lidinoid) continue;   // chiral: no swap, no mirror about a coordinate plane
                    Assert.Equal(f, F(t, new Vector3d(q.y, q.x, q.z)), 9);
                    if (t != TpmsType.SchwarzD)
                        Assert.Equal(f, F(t, new Vector3d(-q.x, q.y, q.z)), 9);
                }
                // the gyroid is odd under the central inversion, the Lidinoid even
                Assert.Equal(-F(TpmsType.Gyroid, q), F(TpmsType.Gyroid, new Vector3d(-q.x, -q.y, -q.z)), 9);
                Assert.Equal(F(TpmsType.Lidinoid, q), F(TpmsType.Lidinoid, new Vector3d(-q.x, -q.y, -q.z)), 9);
            }
        }

        [Fact]
        public void GradientBound_BoundsTheNumericalGradient()
        {
            // the proven bound (triangle inequality) must dominate the largest gradient found on a fine sample; it is loose, so
            // also check it is not absurdly loose (within a factor 5 of the sampled maximum) to catch a wrong-type bound
            var rand = new Random(17);
            foreach (TpmsType t in Enum.GetValues(typeof(TpmsType))) {
                double max = 0, k = 2 * Math.PI, e = 1e-6;
                for (int i = 0; i < 20000; ++i) {
                    var q = new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble());
                    double gx = (F(t, q + new Vector3d(e, 0, 0)) - F(t, q - new Vector3d(e, 0, 0))) / (2 * e);
                    double gy = (F(t, q + new Vector3d(0, e, 0)) - F(t, q - new Vector3d(0, e, 0))) / (2 * e);
                    double gz = (F(t, q + new Vector3d(0, 0, e)) - F(t, q - new Vector3d(0, 0, e))) / (2 * e);
                    max = Math.Max(max, Math.Sqrt(gx * gx + gy * gy + gz * gz) / k);   // in phase coordinates
                }
                double bound = TpmsLattice.GradientBound(t);
                Assert.True(max <= bound, t + ": sampled gradient " + max + " above the bound " + bound);
                Assert.True(bound <= 5 * max, t + ": bound " + bound + " is more than 5 times the sampled maximum " + max);
            }
        }

        [Fact]
        public void ProvenBoundsFollowTheHandDerivation()
        {
            // Pins the hand-derived constants of the Enclosure proof (a sampled maximum cannot: the bounds are loose by design).
            // Gradient: the largest per-component sum of absolute values of the partial derivative, times sqrt 3 for the norm.
            //   gyroid 2 (cos x cos y, sin z sin x), P 1, D 4, Neovius 3 + 4 = 7, I-WP 2 + 2 + 2 = 6,
            //   Lidinoid 1 + 0.5 + 0.5 + 2 = 4 (cos 2x cos y sin z, 0.5 sin 2y cos z cos x, 0.5 sin 2z sin x sin y, sin 2x (cos 2y + cos 2z)).
            // Terms: the sum of the largest absolute values of the terms of f (the rounding error of one evaluation scales with it).
            var gradient = new Dictionary<TpmsType, double> {
                { TpmsType.Gyroid, 2 }, { TpmsType.SchwarzP, 1 }, { TpmsType.SchwarzD, 4 }, { TpmsType.Neovius, 7 }, { TpmsType.IWP, 6 }, { TpmsType.Lidinoid, 4 } };
            var terms = new Dictionary<TpmsType, double> {
                { TpmsType.Gyroid, 3 }, { TpmsType.SchwarzP, 3 }, { TpmsType.SchwarzD, 4 }, { TpmsType.Neovius, 13 }, { TpmsType.IWP, 9 }, { TpmsType.Lidinoid, 3.15 } };
            foreach (TpmsType t in Enum.GetValues(typeof(TpmsType))) {
                Assert.Equal(gradient[t] * Math.Sqrt(3), TpmsLattice.GradientBound(t), 12);
                Assert.Equal(terms[t], TpmsLattice.TermSum(t), 12);
            }
        }

        [Fact]
        public void Enclosure_ContainsTheExactLidinoid()
        {
            foreach (TpmsStyle style in new[] { TpmsStyle.Sheet, TpmsStyle.SkeletalLow }) {
                var lattice = Make(TpmsType.Lidinoid, style, 0.3, 16, 2);
                lattice.Enclosure = true;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, style + " " + lattice.Status + ": " + lattice.Message);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                AssertContainsLatticePoints(lattice, TpmsType.Lidinoid, style, 0.3, 16, 3000);
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
        public void Compute_RetriesTheGridWhenASampleLiesOnTheLevel_AndIsInvariant()
        {
            // The first grid is the lattice grid moved down by half a cube. The box [h/2, L + h/2]^3 has its faces on the planes of
            // those nodes: samples of the box distance are exactly 0 there (a sample on the level or a sliver), so the first
            // grid must be rejected and a later shift succeed.
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.5, 16, 1);
                lattice.CellSize = s.Scale * L;
                lattice.Box = new AxisAlignedBox3d(new Vector3d(1, 1, 1) * (s.Scale / 32), new Vector3d(1, 1, 1) * (s.Scale * (1 + 1.0 / 32)));
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
                Assert.True(lattice.GridShiftsUsed > 0, "the on-level grid must have been rejected: " + s.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.GridShiftsUsed, lattice.GridShiftsUsed);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(lattice.Mesh, s)));
            }
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

        // ---- graded cell size (#816) ----

        // An independent phase map in periods: the period 1 at z = 0 growing linearly to 2 at z = 3 (constant outside), the phase
        // along z the integral of 1 / period (3 ln(period) inside, linear outside), x and y over the local period.
        static Vector3d GradedPhase(Vector3d q)
        {
            double zc = Math.Min(Math.Max(q.z, 0), 3), period = 1 + zc / 3;
            double pz = q.z <= 0 ? q.z : (q.z <= 3 ? 3 * Math.Log(period) : 3 * Math.Log(2) + (q.z - 3) / 2);
            return new Vector3d(q.x / period, q.y / period, pz);
        }

        static double GradedG(TpmsType type, TpmsStyle style, Vector3d q) => G(type, style, GradedPhase(q));

        static TpmsLattice MakeGraded(TpmsType type, TpmsStyle style, double fraction = 0.3, int divisions = 16)
        {
            var lattice = new TpmsLattice {
                Type = type, Style = style, CellSize = L, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 3))
            };
            lattice.Grade = TpmsLattice.LinearGrade(2, 0, 1, 3, 2, 1.5);
            return lattice;
        }

        [Theory]
        [MemberData(nameof(Combinations))]
        public void Graded_IsCleanForEveryTypeAndStyle(TpmsType type, TpmsStyle style)
        {
            var lattice = MakeGraded(type, style, 0.35, 16);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            Assert.True(Invariance.AllFinite(lattice.Mesh));
            Assert.InRange(lattice.MinLocalCellSize, 0.5, 1.0);
        }

        [Theory]
        [InlineData(TpmsType.Gyroid, TpmsStyle.SkeletalLow)]
        [InlineData(TpmsType.SchwarzP, TpmsStyle.Sheet)]
        [InlineData(TpmsType.Neovius, TpmsStyle.SkeletalHigh)]
        public void Graded_MeshAgreesWithTheIndependentGradedField(TpmsType type, TpmsStyle style)
        {
            var lattice = MakeGraded(type, style, 0.35, 16);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            double h = L / 16;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(type, style), 0.35);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(31);
            int tested = 0;
            for (int i = 0; i < 20000 && tested < 1500; ++i) {
                var q = new Vector3d(-0.8 + 1.6 * rand.NextDouble(), -0.8 + 1.6 * rand.NextDouble(), 0.2 + 2.6 * rand.NextDouble());
                // first-order distance to the graded level set by central differences of the composite field
                const double e = 1e-5;
                double gx = (GradedG(type, style, q + new Vector3d(e, 0, 0)) - GradedG(type, style, q - new Vector3d(e, 0, 0))) / (2 * e);
                double gy = (GradedG(type, style, q + new Vector3d(0, e, 0)) - GradedG(type, style, q - new Vector3d(0, e, 0))) / (2 * e);
                double gz = (GradedG(type, style, q + new Vector3d(0, 0, e)) - GradedG(type, style, q - new Vector3d(0, 0, e))) / (2 * e);
                double dist = (GradedG(type, style, q) - level) / Math.Max(Math.Sqrt(gx * gx + gy * gy + gz * gz), 1e-9);
                if (Math.Abs(dist) < 2.5 * h) continue;
                ++tested;
                Assert.Equal(dist < 0, tree.IsInside(q));
            }
            Assert.True(tested >= 300, "only " + tested + " points");
        }

        [Fact]
        public void Graded_LocalPeriodFollowsTheMap()
        {
            // The number of surface crossings of a line through the lattice grows where the period is small: along z the period
            // is 1 at the bottom and 2 at the top, so a sheet has about twice the crossings per length near z = 0 as near z = 3.
            var lattice = MakeGraded(TpmsType.SchwarzP, TpmsStyle.Sheet, 0.3, 24);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            int low = 0, high = 0;
            for (int i = 0; i < 60; ++i) {
                double x = -0.7 + 1.4 * i / 59, y = 0.3 - 0.5 * x;
                bool prev = false; bool first = true;
                for (int k = 0; k <= 600; ++k) {
                    double z = 3.0 * k / 600;
                    bool inside = tree.IsInside(new Vector3d(x, y, z));
                    if (!first && inside != prev) { if (z < 1.5) ++low; else ++high; }
                    prev = inside; first = false;
                }
            }
            Assert.True(low > 0 && high > 0);
            double ratio = (double)low / high;   // z in [0, 1.5] has period 1 to 1.5, z in [1.5, 3] has 1.5 to 2: about 1.4 to 1.7 times more
            Assert.InRange(ratio, 1.15, 2.0);
        }

        [Fact]
        public void Graded_IsInvariantUnderSimilarities_Reusable_AndDeterministic()
        {
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                var lattice = MakeGraded(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 12);
                lattice.CellSize = s.Scale * L;
                lattice.Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0) * s.Scale, new Vector3d(1, 1, 3) * s.Scale);
                lattice.Grade = TpmsLattice.LinearGrade(2, 0, s.Scale, 3 * s.Scale, 2 * s.Scale, 1.5 * s.Scale);
                lattice.Origin = s.Apply(Vector3d.Zero);
                lattice.Rotation = s.Rotation;
                lattice.Compute();
                Assert.True(lattice.Status == TpmsStatus.Ok, s.Name + " " + lattice.Status + ": " + lattice.Message);
                if (reference == null) { reference = lattice; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, lattice.Mesh.TriangleCount);
                DMesh3 back = Invariance.MappedBack(lattice.Mesh, s);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reference.Mesh, back));
                else
                    foreach (int vid in back.VertexIndices())
                        Assert.True((back.GetVertex(vid) - reference.Mesh.GetVertex(vid)).Length < 1e-6 * L, s.Name);
            }
            // reuse and parallel = serial
            var a = MakeGraded(TpmsType.SchwarzD, TpmsStyle.SkeletalLow, 0.4, 12);
            a.ParallelCompute = false;
            a.Compute();
            DMesh3 first = a.Mesh;
            a.Compute();
            Assert.Equal("", Invariance.Difference(first, a.Mesh));
            var b = MakeGraded(TpmsType.SchwarzD, TpmsStyle.SkeletalLow, 0.4, 12);
            b.ParallelCompute = true;
            b.Compute();
            Assert.Equal("", Invariance.Difference(first, b.Mesh));
        }

        [Fact]
        public void Graded_RejectsBadOptions()
        {
            void Expect(Action<TpmsLattice> change, TpmsStatus status)
            {
                var lattice = MakeGraded(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16);
                change(lattice);
                lattice.Compute();
                Assert.Equal(status, lattice.Status);
                Assert.Null(lattice.Mesh);
                Assert.NotEqual("", lattice.Message);
            }
            Expect(l => l.WallThickness = 0.05, TpmsStatus.InvalidOptions);                       // with a Grade
            Expect(l => l.PhaseMap = l.Grade.Map, TpmsStatus.InvalidOptions);                     // both PhaseMap and Grade
            Expect(l => { var g = l.Grade; l.Grade = null; l.PhaseMap = g.Map; l.Enclosure = true; }, TpmsStatus.InvalidOptions);   // Enclosure needs a Grade
            Expect(l => { var g = l.Grade; l.Grade = null; l.PhaseMap = g.Map; l.WallThickness = 0.05; }, TpmsStatus.InvalidOptions);
            Expect(l => l.Box = new AxisAlignedBox3d(new Vector3d(-1.45, -1, 0), new Vector3d(1, 1, 3)), TpmsStatus.InvalidOptions);  // leaves the extent with the grid pad
            Expect(l => { l.Grade = null; l.PhaseMap = p => new Vector3d(double.NaN, 0, 0); }, TpmsStatus.InvalidOptions);
            Expect(l => { l.Grade = null; l.PhaseMap = p => new Vector3d(1, 2, 3); }, TpmsStatus.InvalidOptions);   // constant
            Expect(l => l.Divisions = 4, TpmsStatus.InvalidOptions);                     // the cube is not a quarter of the smallest period
            Expect(l => l.CellSize = 8, TpmsStatus.InvalidOptions);
        }

        [Fact]
        public void Graded_GeneralPhaseMapWorksWithoutEnclosure()
        {
            // the same grade as a plain delegate: a clean lattice (the Jacobian is only estimated for the units of the clip)
            var lattice = MakeGraded(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.35, 16);
            var map = lattice.Grade.Map;
            lattice.Grade = null;
            lattice.PhaseMap = map;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
        }

        [Fact]
        public void Graded_LinearGradeBoundDominatesTheSampledJacobian()
        {
            // The library derives the bound: check it against the Jacobian sampled by central differences, in random directions, within
            // the transverse extent (the sampled operator norm can only refute a too small bound, it is the test of the derivation).
            foreach (var (p0, p1, ext) in new[] { (1.0, 2.0, 1.0), (2.0, 1.0, 3.0), (1.0, 1.0, 5.0), (1.0, 4.0, 0.5), (1.0, 4.0, 3.0), (3.0, 1.0, 3.0) }) {
                TpmsPhaseGrade grade = TpmsLattice.LinearGrade(1, -0.5, p0, 2.5, p1, ext);
                var rand = new Random(53);
                double max = 0;
                const double e = 1e-6;
                for (int i = 0; i < 4000; ++i) {
                    var q = new Vector3d((rand.NextDouble() * 2 - 1) * ext, -1 + 4 * rand.NextDouble(), (rand.NextDouble() * 2 - 1) * ext);
                    var v = new Vector3d(rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1, rand.NextDouble() * 2 - 1).Normalized;
                    max = Math.Max(max, (grade.Map(q + e * v) - grade.Map(q - e * v)).Length / (2 * e));
                }
                Assert.True(max <= grade.JacobianBound, "sampled Jacobian " + max + " above the bound " + grade.JacobianBound);
                Assert.True(grade.JacobianBound <= 2.5 * max, "bound " + grade.JacobianBound + " far above the sampled " + max);
            }
        }

        [Fact]
        public void Graded_LinearGradeIsAccurateForGentleGrades()
        {
            // The phase along the axis is the integral of 1 / period. For a gentle grade the naive log(period / period0) / slope loses
            // digits (the quotient of two nearly equal numbers); the log1p form keeps a few ulps. Reference: the series
            // len / p0 * (1 - x/2 + x^2/3 - x^3/4 + x^4/5) with x = slope len / p0, exact to double precision for |x| < 1e-3.
            foreach (var (p0, p1) in new[] { (1.0, 1.0 + 1e-9), (1.0, 1.0 + 1e-6), (2.0, 2.0 - 1e-7), (1.0, 1.0 + 1e-3) }) {
                TpmsPhaseGrade grade = TpmsLattice.LinearGrade(2, 0, p0, 1, p1, 1);
                double slope = (p1 - p0) / 1.0;
                foreach (double z in new[] { 0.1, 0.5, 0.9, 1.0 }) {
                    double x = slope * z / p0;
                    double reference = z / p0 * (1 - x / 2 + x * x / 3 - x * x * x / 4 + x * x * x * x / 5);
                    double actual = grade.Map(new Vector3d(0, 0, z)).z;
                    Assert.True(Math.Abs(actual - reference) <= 4e-16 * Math.Max(1, Math.Abs(reference)), p0 + " " + p1 + " z " + z + ": " + actual + " vs " + reference);
                }
            }
            // the helper itself: log(1 + x) / x near 0, at 0 and for a large and a negative argument
            Assert.Equal(1.0, TpmsLattice.Log1pOverX(0));
            Assert.Equal(1 - 1e-17 / 2, TpmsLattice.Log1pOverX(1e-17), 15);
            Assert.Equal(Math.Log(3) / 2, TpmsLattice.Log1pOverX(2), 14);
            Assert.Equal(Math.Log(0.5) / -0.5, TpmsLattice.Log1pOverX(-0.5), 14);
        }

        [Fact]
        public void Graded_EnclosureErrorTermFollowsTheDerivation_AndTheConstantLatticeIsUnchanged()
        {
            // pins the phase part of the Enclosure error model: constant lattice 4 u; graded 8 u (2 phaseMax + 4 J h) / (J s)
            double u = Math.Pow(2, -53), lw = 10, s = 4;
            double terms = 64 * u * TpmsLattice.TermSum(TpmsType.Gyroid) / (lw * s);
            Assert.Equal(terms + 4 * u, TpmsLattice.EnclosureError(TpmsType.Gyroid, lw, s, false, 123, 2, 0.1), 15);
            double graded = TpmsLattice.EnclosureError(TpmsType.Gyroid, lw, s, true, 50, 2, 0.1);
            Assert.Equal(terms + 8 * u * (2 * 50 + 4 * 2 * 0.1) / (2 * s), graded, 15);
            Assert.True(graded > terms + 4 * u);
            // the constant lattice normalises with exactly Lf 2 pi / L as before the graded cells existed (bit for bit)
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 12);
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Message);
            Assert.Equal(TpmsLattice.GradientBound(TpmsType.Gyroid) * (2 * Math.PI / L), lattice.LipschitzBoundUsed);
        }

        [Fact]
        public void Graded_LocalFractionIsTheTargetWhereTheMapIsASimilarity()
        {
            // The period is constant (2) above z = 3: there the phase map is a similarity and the local volume fraction is the
            // target (elsewhere the cells are distorted, see the design note). One period of 2 in x, y and z is sampled.
            var lattice = new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.SkeletalLow, CellSize = L, Divisions = 16, VolumeFraction = 0.35,
                Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 6)),
                Grade = TpmsLattice.LinearGrade(2, 0, 1, 3, 2, 1.5)
            };
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(61);
            int inside = 0, n = 6000;
            for (int i = 0; i < n; ++i) {
                var q = new Vector3d(-0.98 + 1.96 * rand.NextDouble(), -0.98 + 1.96 * rand.NextDouble(), 3.5 + 2.0 * rand.NextDouble());
                if (tree.IsInside(q)) ++inside;
            }
            Assert.InRange((double)inside / n, 0.35 - 0.06, 0.35 + 0.06);
        }

        [Fact]
        public void Graded_EnclosureUsesTheDerivedBoundAndContainsTheExactLattice()
        {
            var lattice = MakeGraded(TpmsType.Gyroid, TpmsStyle.SkeletalLow, 0.3, 16);
            lattice.Box = new AxisAlignedBox3d(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 2));
            lattice.Grade = TpmsLattice.LinearGrade(2, 0, 1, 3, 2, 1.5);
            double bound = lattice.Grade.JacobianBound;
            lattice.Enclosure = true;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(lattice.Mesh));
            // the normalising Lipschitz bound is exactly Lf 2 pi J with the derived J
            Assert.Equal(TpmsLattice.GradientBound(TpmsType.Gyroid) * 2 * Math.PI * bound, lattice.LipschitzBoundUsed, 9);
            double h = L / 16, lw = lattice.LipschitzBoundUsed;
            double level = TpmsLattice.LevelForFraction(TpmsLattice.QuantileTable(TpmsType.Gyroid, TpmsStyle.SkeletalLow), 0.3);
            var tree = new DMeshAABBTree3(lattice.Mesh, true);
            var rand = new Random(41);
            int tested = 0;
            for (int i = 0; i < 400000 && tested < 3000; ++i) {
                var q = new Vector3d(-0.8 + 1.6 * rand.NextDouble(), -0.8 + 1.6 * rand.NextDouble(), 0.2 + 1.6 * rand.NextDouble());
                double g = (GradedG(TpmsType.Gyroid, TpmsStyle.SkeletalLow, q) - level) / lw;
                if (g > 0 || g < -0.1 * h) continue;
                ++tested;
                Assert.True(tree.IsInside(q), "an exact graded-lattice point " + q + " is outside the enclosure (g " + g + ")");
            }
            Assert.True(tested >= 750, "only " + tested);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Compute_CancelsInTheMiddleOfTheContouring(bool parallel)
        {
            // CancelF is polled by the contouring itself: it answers false for the first calls and then true. The status is
            // Cancelled, no mesh is exposed, and the contouring was asked many times (it ran, it did not stop before it started).
            int calls = 0;
            var lattice = Make(TpmsType.Gyroid, TpmsStyle.Sheet, 0.3, 16, 2);
            lattice.ParallelCompute = parallel;
            lattice.CancelF = () => System.Threading.Interlocked.Increment(ref calls) > 40;
            lattice.Compute();
            Assert.Equal(TpmsStatus.Cancelled, lattice.Status);
            Assert.Null(lattice.Mesh);
            Assert.True(calls > 40, "the contouring did not poll CancelF: " + calls);
            // the same lattice without cancelling is fine (state is not left behind)
            lattice.CancelF = () => false;
            lattice.Compute();
            Assert.True(lattice.Status == TpmsStatus.Ok, lattice.Status + ": " + lattice.Message);
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
                Assert.True(lattice.IsoValueUsed >= Math.Sqrt(3) * h / 2 + 4 * Math.Pow(2, -26), "level " + lattice.IsoValueUsed);
                Assert.True(lattice.IsoValueUsed <= Math.Sqrt(3) * h / 2 + 4 * Math.Pow(2, -22), "level " + lattice.IsoValueUsed);
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
