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
    /// Metamorphic relations, round 2 (docs/qa/testing-and-invariance.md item 10), on the procedural solids of CurvedSolids:
    /// the winding-number Boolean and solidify against the exact pipeline, the alpha wrap contains the input within its
    /// documented band, bounded simplification keeps its deviation certificate, and the TPMS lattice is periodic.
    /// Every tolerance is derived in the test that uses it.
    /// </summary>
    public class MetamorphicRound2Tests
    {
        static double Area(DMesh3 m) => Invariance.Area(m);
        static double Vol(DMesh3 m) => m == null || m.TriangleCount == 0 ? 0 : BooleanOracle.Volume(m);

        static DMesh3 ExactResult(DMesh3 a, DMesh3 b, Ops op)
        {
            var p = new SolidBooleanPipeline(a, b, op);
            Assert.True(p.Compute(), "pipeline: " + p.Status + " " + p.Error);
            return p.Result;
        }

        static DMesh3 Soup(params DMesh3[] meshes)
        {
            var r = new DMesh3();
            foreach (DMesh3 m in meshes) {
                var map = new Dictionary<int, int>();
                foreach (int v in m.VertexIndices()) map[v] = r.AppendVertex(m.GetVertex(v));
                foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); r.AppendTriangle(map[f.a], map[f.b], map[f.c]); }
            }
            return r;
        }

        public static readonly string[] GeneralPairs = { "sphere overlap", "sphere same radius shifted", "box rotated box", "box sphere", "torus sphere in hole", "torus box", "sphere nested" };
        public static IEnumerable<object[]> PairOps() =>
            GeneralPairs.SelectMany(n => new[] { Ops.Union, Ops.Intersection, Ops.Difference }.Select(o => new object[] { n, (int)o }));

        // ---------------------------------------------------------------- winding-number Boolean and solidify vs the exact pipeline

        /// <summary>
        /// The winding-number Boolean extracts the 0.5 iso-surface on a grid of UsedCellSize h; each vertex of the result lies in a
        /// cell, so within the cell diagonal sqrt(3) h of the exact surface, and the volumes differ by at most that distance times the larger of the
        /// two areas (a thin feature below h may be lost, none of the pairs here has one).
        /// </summary>
        [Theory, MemberData(nameof(PairOps))]
        public void WindingBoolean_MatchesTheExactPipeline_WithinTheGrid(string name, int opValue)
        {
            var op = (Ops)opValue;
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            DMesh3 exact = ExactResult(p.A, p.B, op);
            var w = new MeshWindingNumberBoolean(p.A, p.B, (MeshWindingNumberBoolean.Operations)opValue) { GridResolution = 64 };
            Assert.True(w.Compute(), $"{name} {op}: winding Boolean failed");
            Assert.NotNull(w.Result);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result, true));
            double tol = Math.Sqrt(3) * w.UsedCellSize * Math.Max(Area(exact), Area(w.Result));
            Assert.True(Math.Abs(Vol(exact) - Vol(w.Result)) <= tol, $"{name} {op}: exact {Vol(exact):G8}, winding {Vol(w.Result):G8}, tolerance {tol:G3} (cell {w.UsedCellSize:G3})");
        }

        /// <summary> the solidified soup of A and B (overlap has winding 2, above the iso 0.5) is the union, within the same grid bound </summary>
        [Theory]
        [InlineData("sphere overlap")]
        [InlineData("box rotated box")]
        [InlineData("torus sphere in hole")]
        public void WindingSolidify_OfTheSoupOfTwoSolids_IsTheirUnion(string name)
        {
            CurvedSolids.Pair p = CurvedSolids.Pairs[name];
            DMesh3 exact = ExactResult(p.A, p.B, Ops.Union);
            var s = new MeshWindingNumberSolidify(Soup(p.A, p.B)) { GridResolution = 64 };
            Assert.True(s.Compute(), name + ": solidify failed");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(s.Result, true));
            double tol = Math.Sqrt(3) * s.UsedCellSize * Math.Max(Area(exact), Area(s.Result));
            Assert.True(Math.Abs(Vol(exact) - Vol(s.Result)) <= tol, $"{name}: union {Vol(exact):G8}, solidify {Vol(s.Result):G8}, tolerance {tol:G3}");
        }

        /// <summary> solidify of a closed solid keeps its volume (same bound), and is idempotent: solidify(solidify(A)) has the volume of solidify(A) </summary>
        [Theory]
        [InlineData("sphere overlap", 0)]
        [InlineData("torus sphere in hole", 0)]
        [InlineData("box rotated box", 1)]
        public void WindingSolidify_OfAClosedSolid_KeepsItsVolume_AndIsIdempotent(string name, int which)
        {
            DMesh3 a = which == 0 ? CurvedSolids.Pairs[name].A : CurvedSolids.Pairs[name].B;
            var s = new MeshWindingNumberSolidify(a) { GridResolution = 64 };
            Assert.True(s.Compute());
            double tol = Math.Sqrt(3) * s.UsedCellSize * Math.Max(Area(a), Area(s.Result));
            Assert.True(Math.Abs(Vol(a) - Vol(s.Result)) <= tol, $"{name}: input {Vol(a):G8}, solidify {Vol(s.Result):G8}, tolerance {tol:G3}");
            // the second pass runs on the grid of the first result's own bounds: the same bound again (two passes: twice)
            var s2 = new MeshWindingNumberSolidify(s.Result) { GridResolution = 64 };
            Assert.True(s2.Compute());
            double tol2 = tol + Math.Sqrt(3) * s2.UsedCellSize * Math.Max(Area(s.Result), Area(s2.Result));
            Assert.True(Math.Abs(Vol(s.Result) - Vol(s2.Result)) <= tol2, $"{name}: solidify {Vol(s.Result):G8}, twice {Vol(s2.Result):G8}, tolerance {tol2:G3}");
        }

        // ---------------------------------------------------------------- alpha wrap

        static DMesh3 WrapInput(string name)
        {
            switch (name) {
                case "sphere": return CurvedSolids.Sphere(1, Vector3d.Zero, 7);
                case "torus": return CurvedSolids.Torus(1, 0.35, Vector3d.Zero);
                case "union": { var p = CurvedSolids.Pairs["sphere overlap"]; return ExactResult(p.A, p.B, Ops.Union); }
                default: return Soup(CurvedSolids.Pairs["box sphere"].A, CurvedSolids.Pairs["box sphere"].B);   // overlapping shells as a soup
            }
        }
        public static IEnumerable<object[]> WrapInputs()
        {
            foreach (string n in new[] { "sphere", "torus", "union", "soup" })
                foreach (var (a, o) in new[] { (20.0, 600.0), (10.0, 100.0), (40.0, 2000.0) }) yield return new object[] { n, a, o };
        }

        /// <summary>
        /// The documented guarantees (class comment of MeshAlphaWrap): closed, manifold, no self-intersections, the input strictly inside
        /// (here: every input vertex and every input triangle centroid has winding number above 0.5), vertices at least Offset from the input
        /// (minus the contact margin 1e-6 Offset), and the result within Offset + Alpha of the input where the facets come from carving; the band is
        /// asserted for the vertices of the result with the measured factor 1.1 of the class comment (RefineNonManifold on).
        /// </summary>
        [Theory, MemberData(nameof(WrapInputs))]
        public void AlphaWrap_ContainsTheInput_WithinItsBand(string name, double alphaDivisor, double offsetDivisor)
        {
            DMesh3 input = WrapInput(name);
            double diag = input.CachedBounds.DiagonalLength;
            var w = new MeshAlphaWrap(input) { Alpha = diag / alphaDivisor, Offset = diag / offsetDivisor };
            Assert.True(w.Compute(), $"{name}: wrap {w.Status} {w.Error}");
            DMesh3 r = w.Result;
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
            var wrapTree = new DMeshAABBTree3(r, true);
            wrapTree.WindingNumber(Vector3d.Zero);
            foreach (int v in input.VertexIndices()) {
                double wn = wrapTree.WindingNumber(input.GetVertex(v));
                Assert.True(wn > 0.5, $"{name}: input vertex {v} outside the wrap (winding {wn:G4})");
            }
            foreach (int t in input.TriangleIndices().Where((x, i) => i % 3 == 0)) {
                double wn = wrapTree.WindingNumber(input.GetTriCentroid(t));
                Assert.True(wn > 0.5, $"{name}: input triangle {t} outside the wrap (winding {wn:G4})");
            }
            var inputTree = new DMeshAABBTree3(input, true);
            double minD = double.MaxValue, maxD = 0;
            foreach (int v in r.VertexIndices()) {
                inputTree.FindNearestTriangle(r.GetVertex(v), out double dsq);
                double d = Math.Sqrt(dsq);
                minD = Math.Min(minD, d); maxD = Math.Max(maxD, d);
            }
            Assert.True(minD >= w.UsedOffset * (1 - 1e-5), $"{name}: a wrap vertex at {minD:G6} from the input, offset {w.UsedOffset:G6}");
            Assert.True(maxD <= 1.1 * (w.UsedOffset + w.UsedAlpha), $"{name}: a wrap vertex at {maxD:G6} from the input, offset + alpha {w.UsedOffset + w.UsedAlpha:G6}");
            if (name != "soup") Assert.True(Vol(r) > Vol(input), $"{name}: wrap volume {Vol(r):G6} not above input {Vol(input):G6}");
        }

        // ---------------------------------------------------------------- bounded simplification

        /// <summary>
        /// Reducer with MaxDeviation D and MaxDeviationTwoSided: every point of the result is within D of the input and every point of
        /// the input within D of the result (a certificate, docs/design/BOUNDED_SIMPLIFICATION.md). Checked on the vertices and the triangle centroids of
        /// both meshes (a sample of the certificate). The volume then differs by at most D times the larger area. Closed and clean with PreventSelfIntersections.
        /// </summary>
        [Theory]
        [InlineData("sphere", 0.02)]
        [InlineData("torus", 0.02)]
        [InlineData("union", 0.03)]
        [InlineData("union", 0.01)]
        public void Simplification_TwoSidedDeviation_BoundsVolumeAndDistance(string name, double deviation)
        {
            DMesh3 input = WrapInput(name), m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = deviation, MaxDeviationTwoSided = true, PreventSelfIntersections = true };
            r.ReduceToTriangleCount(Math.Max(20, input.TriangleCount / 8));
            Assert.True(m.TriangleCount < input.TriangleCount, $"{name}: nothing was reduced");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, true));
            double slack = 1e-9;
            AssertWithin(m, input, deviation + slack, name + " result in the input band");
            AssertWithin(input, m, deviation + slack, name + " input in the result band");
            double tol = deviation * Math.Max(Area(input), Area(m));
            Assert.True(Math.Abs(Vol(input) - Vol(m)) <= tol, $"{name}: volume {Vol(input):G8} -> {Vol(m):G8}, tolerance {tol:G3}");
        }

        static void AssertWithin(DMesh3 from, DMesh3 to, double d, string what)
        {
            var tree = new DMeshAABBTree3(to, true);
            foreach (Vector3d p in from.VertexIndices().Select(from.GetVertex).Concat(from.TriangleIndices().Select(from.GetTriCentroid))) {
                tree.FindNearestTriangle(p, out double dsq);
                Assert.True(Math.Sqrt(dsq) <= d, $"{what}: a point at distance {Math.Sqrt(dsq):G6}, bound {d}");
            }
        }

        // ---------------------------------------------------------------- TPMS periodic shift

        static TpmsLattice Lattice(TpmsType type, TpmsStyle style, Vector3d origin, AxisAlignedBox3d box) =>
            new TpmsLattice { Type = type, Style = style, CellSize = 1, Divisions = 16, VolumeFraction = 0.3, Origin = origin, Box = box };

        public static IEnumerable<object[]> LatticeKinds()
        {
            foreach (TpmsType t in new[] { TpmsType.Gyroid, TpmsType.SchwarzP, TpmsType.SchwarzD, TpmsType.Neovius, TpmsType.IWP, TpmsType.Lidinoid })
                foreach (TpmsStyle s in new[] { TpmsStyle.Sheet, TpmsStyle.SkeletalLow, TpmsStyle.SkeletalHigh })
                    yield return new object[] { (int)t, (int)s };
        }

        /// <summary>
        /// The lattice is periodic with CellSize: moving Origin by whole periods leaves the solid unchanged, and a box moved by whole periods
        /// (with the origin fixed) gives the translated solid. The grid is aligned to the box, so the fields agree up to the rounding of
        /// the phase 2 pi (p - Origin) / L (relative 1e-14 at these magnitudes); the vertices move by that over the field gradient, which is
        /// bounded below away from the critical points of a nodal field, so volume and area are compared with the relative tolerance 1e-7.
        /// </summary>
        [Theory, MemberData(nameof(LatticeKinds))]
        public void TpmsLattice_IsPeriodic_OriginAndBoxShiftsByWholePeriods(int typeValue, int styleValue)
        {
            var type = (TpmsType)typeValue; var style = (TpmsStyle)styleValue;
            var box = new AxisAlignedBox3d(new Vector3d(0.1, 0.2, 0.3), new Vector3d(1.6, 1.4, 1.7));
            var baseLattice = Lattice(type, style, Vector3d.Zero, box); baseLattice.Compute();
            Assert.Equal(TpmsStatus.Ok, baseLattice.Status);
            double v0 = Vol(baseLattice.Mesh), a0 = Area(baseLattice.Mesh);
            foreach (Vector3d shift in new[] { new Vector3d(1, 0, 0), new Vector3d(-2, 1, 3), new Vector3d(0, 0, 4) }) {
                var o = Lattice(type, style, shift, box); o.Compute();
                Assert.Equal(TpmsStatus.Ok, o.Status);
                Assert.True(Math.Abs(Vol(o.Mesh) - v0) <= 1e-7 * v0 && Math.Abs(Area(o.Mesh) - a0) <= 1e-7 * a0,
                    $"{type} {style} origin {shift}: volume {v0:R} vs {Vol(o.Mesh):R}, area {a0:R} vs {Area(o.Mesh):R}");
                var movedBox = new AxisAlignedBox3d(box.Min + shift, box.Max + shift);
                var b = Lattice(type, style, Vector3d.Zero, movedBox); b.Compute();
                Assert.Equal(TpmsStatus.Ok, b.Status);
                Assert.True(Math.Abs(Vol(b.Mesh) - v0) <= 1e-7 * v0 && Math.Abs(Area(b.Mesh) - a0) <= 1e-7 * a0,
                    $"{type} {style} box shift {shift}: volume {v0:R} vs {Vol(b.Mesh):R}, area {a0:R} vs {Area(b.Mesh):R}");
            }
        }
    }
}
