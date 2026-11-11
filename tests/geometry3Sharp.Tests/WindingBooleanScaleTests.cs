using System;
using System.Text;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshWindingNumberBoolean outside an extent of [2^-32, 2^32]: the winding-number field overflows (the extent to the 4th power), so
    /// Compute() succeeded with an EMPTY mesh from about 1e79 to 1e103 and above 1e153 (silent data loss), and degraded below about 1e-12.
    /// It now runs on a copy scaled by a power of two to an extent in [1, 2), as MeshWindingNumberSolidify does.
    /// </summary>
    public class WindingBooleanScaleTests
    {
        static DMesh3 Octahedron(Vector3d c, double r)
        {
            var m = new DMesh3();
            Vector3d[] v = { c + new Vector3d(r, 0, 0), c + new Vector3d(-r, 0, 0), c + new Vector3d(0, r, 0), c + new Vector3d(0, -r, 0), c + new Vector3d(0, 0, r), c + new Vector3d(0, 0, -r) };
            foreach (Vector3d p in v) m.AppendVertex(p);
            int[][] t = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            foreach (int[] f in t) m.AppendTriangle(f[0], f[1], f[2]);
            return m;
        }

        static MeshWindingNumberBoolean Make(double r, MeshWindingNumberBoolean.Operations op, double cell = 0, int resolution = 128)
        {
            return new MeshWindingNumberBoolean(Octahedron(Vector3d.Zero, r), Octahedron(new Vector3d(0.5 * r, 0, 0), r), op) { CellSize = cell, GridResolution = resolution };
        }

        static string Signature(DMesh3 m, double inverseScale)
        {
            var sb = new StringBuilder();
            foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); sb.Append(f.a).Append(',').Append(f.b).Append(',').Append(f.c).Append(';'); }
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v) * inverseScale;
                sb.Append(BitConverter.DoubleToInt64Bits(p.x)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.y)).Append(',').Append(BitConverter.DoubleToInt64Bits(p.z)).Append(';');
            }
            return sb.ToString();
        }

        static double Volume(DMesh3 m, double r)
        {
            double v = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                v += (a / r).Dot((b / r).Cross(c / r)) / 6.0;
            }
            return v;
        }

        [Theory]
        [InlineData(1e-100)]
        [InlineData(1e-20)]
        [InlineData(1e80)]
        [InlineData(1e90)]
        [InlineData(1e100)]
        [InlineData(1e154)]
        [InlineData(1e200)]
        [InlineData(1e300)]
        public void Union_AtAnyScale_IsTheUnion_NotAnEmptyMesh(double r)
        {
            var boolean = Make(r, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result, false));
            Assert.InRange(Volume(boolean.Result, r), 2.08, 2.13);        // the unit-scale union of the two octahedra: 2.1042
        }

        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference)]
        public void Boolean_OfA2kScaledCopy_IsTheScaledResult(MeshWindingNumberBoolean.Operations op)
        {
            var reference = Make(1, op, 0, 40);
            Assert.True(reference.Compute());
            string expected = Signature(reference.Result, 1);
            // (including the two sides of the edge of the band: the octahedra have extents of 2.5 r: 2^e is inside, 2^(e+1) outside for e = 30)
            foreach (int e in new[] { -300, -35, -30, 29, 31, 40, 300, 900 }) {
                double s = Math.Pow(2, e);
                var scaled = Make(s, op, 0, 40);
                Assert.True(scaled.Compute());
                Assert.Equal(expected, Signature(scaled.Result, 1.0 / s));
                Assert.Equal(reference.UsedCellSize, scaled.UsedCellSize / s);
            }
        }

        [Fact]
        public void CellSize_IsInTheUnitsOfTheCaller_AtEveryScale()
        {
            var reference = Make(1, MeshWindingNumberBoolean.Operations.Union, 0.05);
            Assert.True(reference.Compute());
            Assert.Equal(0.05, reference.UsedCellSize, 12);
            string expected = Signature(reference.Result, 1);
            foreach (int e in new[] { -100, -40, 40, 100, 300 }) {
                double s = Math.Pow(2, e);
                var scaled = Make(s, MeshWindingNumberBoolean.Operations.Union, 0.05 * s);
                Assert.True(scaled.Compute());
                Assert.Equal(expected, Signature(scaled.Result, 1.0 / s));
                Assert.Equal(0.05 * s, scaled.UsedCellSize);
            }
        }

        [Fact]
        public void Compute_CanBeRepeated_OnOneInstance_AtALargeScale()
        {
            var boolean = Make(1e100, MeshWindingNumberBoolean.Operations.Union);
            Assert.True(boolean.Compute());
            string first = Signature(boolean.Result, 1);
            Assert.True(boolean.Compute());
            Assert.Equal(first, Signature(boolean.Result, 1));
        }

        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference)]
        public void Compute_OfAnEmptyAndAHugeMesh_IsRightForEveryOperation(MeshWindingNumberBoolean.Operations op)
        {
            // an empty A: the union is B, the intersection and the difference are empty; an empty B: the union and the difference are A
            foreach (bool emptyA in new[] { true, false }) {
                DMesh3 huge = Octahedron(Vector3d.Zero, 1e100);
                var boolean = emptyA ? new MeshWindingNumberBoolean(new DMesh3(), huge, op) : new MeshWindingNumberBoolean(huge, new DMesh3(), op);
                Assert.True(boolean.Compute());
                bool expectSolid = emptyA ? op == MeshWindingNumberBoolean.Operations.Union : op != MeshWindingNumberBoolean.Operations.Intersection;
                if (expectSolid) {
                    Assert.True(boolean.Result.TriangleCount > 0);
                    Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result, false));
                    Assert.InRange(Math.Abs(Volume(boolean.Result, 1e100)), 1.2, 1.5);        // the octahedron: 4/3 r^3
                } else
                    Assert.Equal(0, boolean.Result.TriangleCount);
            }
        }

        // the hidden gaps of the result are reported in the units of the caller
        [Fact]
        public void HiddenSamples_AreInTheUnitsOfTheCaller()
        {
            Func<double, MeshWindingNumberBoolean> make = s => {
                DMesh3 a = Block(new Vector3d(-10, -5, -2) * s, new Vector3d(10, 5, 2) * s);
                DMesh3 b = Block(new Vector3d(2, 5.15, -1) * s, new Vector3d(5, 7, 1.5) * s);
                return new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { CellSize = 0.5 * s, MaxRefineLevels = 0, ReduceTolerance = 0 };
            };
            var reference = make(1);
            Assert.True(reference.Compute());
            Assert.True(reference.HiddenSamples.Count > 0, "the gap of 0.3 cells is hidden without refinement");
            foreach (int e in new[] { -60, 60, 300 }) {
                double s = Math.Pow(2, e);
                var scaled = make(s);
                Assert.True(scaled.Compute());
                Assert.Equal(reference.HiddenSamples.Count, scaled.HiddenSamples.Count);
                for (int i = 0; i < reference.HiddenSamples.Count; i++) {
                    Assert.Equal(reference.HiddenSamples[i].Key, scaled.HiddenSamples[i].Key * (1 / s));
                    Assert.Equal(reference.HiddenSamples[i].Value, scaled.HiddenSamples[i].Value / s);
                }
                Assert.Equal(reference.HiddenGapEdges, scaled.HiddenGapEdges);
            }
        }

        static DMesh3 Block(Vector3d lo, Vector3d hi)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; i++)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
            int[][] q = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] f in q) { m.AppendTriangle(f[0], f[1], f[2]); m.AppendTriangle(f[0], f[2], f[3]); }
            return m;
        }

        [Fact]
        public void CellSizeThatDoesNotExistInTheScaledFrame_IsReported_NotReplacedByTheAutomaticOne()
        {
            // a huge mesh (scaled by 2^-330) with a caller cell size of 1e-300: 0 in the scaled frame; and a NaN / infinite size
            var tooSmall = Make(1e100, MeshWindingNumberBoolean.Operations.Union, 1e-300, 24);
            Assert.False(tooSmall.Compute());
            Assert.Null(tooSmall.Result);
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity }) {
                var b = Make(1e100, MeshWindingNumberBoolean.Operations.Union, bad, 24);
                Assert.False(b.Compute());
                Assert.Null(b.Result);
            }
        }

        [Fact]
        public void OneInstance_ScaledThenInBand_HasNoStaleOutputs()
        {
            var boolean = Make(1e100, MeshWindingNumberBoolean.Operations.Union, 0, 24);
            Assert.True(boolean.Compute());
            Assert.True(boolean.HiddenSamples != null && boolean.UsedCellSize > 1e90);
            boolean.MeshA = Octahedron(Vector3d.Zero, 1);
            boolean.MeshB = Octahedron(new Vector3d(0.5, 0, 0), 1);
            Assert.True(boolean.Compute());
            Assert.InRange(boolean.UsedCellSize, 1e-3, 1.0);
            Assert.InRange(Volume(boolean.Result, 1), 2.0, 2.2);
            boolean.CancelF = () => true;
            Assert.False(boolean.Compute());
            Assert.Null(boolean.Result);
            // a failure before the computation also drops the earlier result
            boolean.CancelF = () => false;
            boolean.MeshA = Octahedron(Vector3d.Zero, 1e100);
            boolean.MeshB = Octahedron(new Vector3d(0.5e100, 0, 0), 1e100);
            Assert.True(boolean.Compute());
            boolean.CellSize = 1e-300;
            Assert.False(boolean.Compute());
            Assert.Null(boolean.Result);
            Assert.Equal(0, boolean.UsedCellSize);
        }

        [Fact]
        public void AScaleThatIsNotExact_IsReported_NotComputedOnTheOverflowingField()
        {
            // a tiny mesh (scaled by 2^330) with an unused vertex at 1e250: the scaled coordinate would exceed 1e300
            DMesh3 a = Octahedron(Vector3d.Zero, 1e-100);
            a.AppendVertex(new Vector3d(1e250, 0, 0));
            var boolean = new MeshWindingNumberBoolean(a, Octahedron(new Vector3d(0.5e-100, 0, 0), 1e-100), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 24 };
            Assert.False(boolean.Compute());
            Assert.Null(boolean.Result);
        }

        /// <summary>
        /// The invariant at every scale (coarse sweep, also at the ends of the double range): Compute() is false, or the result is the
        /// right solid: clean, non-empty, finite, with the volume of the unit-scale result; never a success with an empty or wrong mesh
        /// </summary>
        [Theory]
        [InlineData(MeshWindingNumberBoolean.Operations.Union)]
        [InlineData(MeshWindingNumberBoolean.Operations.Intersection)]
        [InlineData(MeshWindingNumberBoolean.Operations.Difference)]
        public void Sweep_AtEveryScale_IsFalseOrTheRightSolid(MeshWindingNumberBoolean.Operations op)
        {
            var reference = Make(1, op, 0, 24);
            Assert.True(reference.Compute());
            double expected = Volume(reference.Result, 1);
            Assert.True(expected > 0.1);
            var outcomes = new System.Text.StringBuilder();
            foreach (int e in new[] { -1070, -1040, -1000, -700, -300, -100, -40, -20, 20, 40, 100, 300, 600, 900, 1000, 1020 }) {
                double r = Math.Pow(2, e);
                var b = Make(r, op, 0, 24);
                if (!b.Compute()) { outcomes.Append(e).Append(":false "); continue; }
                Assert.True(b.Result != null, "2^" + e + ": true without a result");
                foreach (int v in b.Result.VertexIndices())
                    Assert.True(b.Result.GetVertex(v).IsFinite, "2^" + e + ": non-finite vertex");
                Assert.True("" == MeshRobustRepair.NotCleanReason(b.Result, false), "2^" + e + ": " + MeshRobustRepair.NotCleanReason(b.Result, false) + " (" + b.Result.TriangleCount + " triangles)");
                Assert.InRange(Volume(b.Result, r) / expected, 0.97, 1.03);
            }
        }

        // the in-band path is the old code: results pinned (hashes of the result computed with the library of master)
        [Theory]
        [InlineData(1.0)]
        [InlineData(1024.0)]
        [InlineData(1e-3)]
        [InlineData(1e3)]
        public void InBand_ResultIsThePinnedResultOfMaster(double r)
        {
            var b = Make(r, MeshWindingNumberBoolean.Operations.Union, 0, 24);
            Assert.True(b.Compute());
            Assert.Equal(PinnedHash(r), Hash(Signature(b.Result, 1)));
        }

        static string Hash(string s)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 16);
        }

        static string PinnedHash(double r)
        {
            if (r == 1.0) return "E11A9AD2309B84CA";
            if (r == 1024.0) return "4A29ADD10CC2DFF7";
            if (r == 1e-3) return "70560E784F621B16";
            return "57C9263C123D117F";
        }

        // invariance under the similarities of the harness, also at a size outside the band
        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Union_IsInvariantUnderSimilarity_AtAnySize(string name)
        {
            Similarity sim = Similarity.ByName(name);
            foreach (double size in new[] { 1.0, 1e100, 1e-100 }) {
                DMesh3 a = Invariance.Transformed(Octahedron(Vector3d.Zero, size), sim);
                DMesh3 b = Invariance.Transformed(Octahedron(new Vector3d(0.5 * size, 0, 0), size), sim);
                var boolean = new MeshWindingNumberBoolean(a, b, MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
                if (size == 1e-100 && sim.Translation != Vector3d.Zero) {
                    // (a mesh of 1e-100 at a translation of 1e5: the grid nodes cannot be separate doubles, #587: reported)
                    Assert.False(boolean.Compute());
                    Assert.Null(boolean.Result);
                    continue;
                }
                Assert.True(boolean.Compute());
                DMesh3 back = Invariance.MappedBack(boolean.Result, sim);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(boolean.Result, false));
                Assert.InRange(Invariance.Volume(back) / (size * size * size) / 2.1042, 0.97, 1.03);
                if (sim.IsExact) {
                    // a power-of-two scale: the unit-scale result times 2^k, bit for bit
                    var plain = new MeshWindingNumberBoolean(Octahedron(Vector3d.Zero, size), Octahedron(new Vector3d(0.5 * size, 0, 0), size), MeshWindingNumberBoolean.Operations.Union) { GridResolution = 32 };
                    Assert.True(plain.Compute());
                    Assert.Equal(Signature(plain.Result, 1), Signature(boolean.Result, 1 / sim.Scale));
                }
            }
        }
    }
}
