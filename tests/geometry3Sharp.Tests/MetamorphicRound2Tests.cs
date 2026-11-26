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

        public static readonly string[] GeneralPairs = { "sphere overlap", "sphere same radius shifted", "box rotated box", "box sphere", "torus sphere in hole", "torus box", "sphere nested", "sphere asymmetric" };
        public static IEnumerable<object[]> PairOps() =>
            GeneralPairs.SelectMany(n => new[] { Ops.Union, Ops.Intersection, Ops.Difference }.Select(o => new object[] { n, (int)o }));

        static IEnumerable<Vector3d> Samples(DMesh3 m) =>
            m.VertexIndices().Select(m.GetVertex).Concat(m.TriangleIndices().Select(m.GetTriCentroid));

        static double MaxDistance(DMesh3 from, DMesh3 to)
        {
            var tree = new DMeshAABBTree3(to, true);
            double max = 0;
            foreach (Vector3d p in Samples(from)) { tree.FindNearestTriangle(p, out double dsq); max = Math.Max(max, Math.Sqrt(dsq)); }
            return max;
        }

        /// <summary> measured on master: |volume difference| / (cell size x larger area) was at most 0.032 over all pairs; the sharp check allows 0.1 </summary>
        const double MeasuredVolumeSlack = 0.1;

        // rows whose exact result has a sliver or crescent thinner than the cell (the tip of A - B): the winding result loses it (documented: features
        // smaller than CellSize are lost), so the exact surface is farther than a cell from the result there. Measured 9.9 and 2.3 cell sizes.
        static bool ThinResult(string name, Ops op) => op == Ops.Difference && (name == "box rotated box" || name == "sphere same radius shifted");

        static void AssertWindingMatches(DMesh3 exact, DMesh3 result, double h, string what, bool thin)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result, true));
            double cell = Math.Sqrt(3) * h * (1 + 1e-9);   // a vertex lies in its cell: within the cell diagonal of the exact surface
            Assert.True(MaxDistance(result, exact) <= cell, $"{what}: a result sample is {MaxDistance(result, exact) / h:G3} cells from the exact surface");
            double back = MaxDistance(exact, result);
            Assert.True(back <= (thin ? 12 * h : cell), $"{what}: an exact sample is {back / h:G3} cells from the result");
            double area = Math.Max(Area(exact), Area(result));
            double dv = Math.Abs(Vol(exact) - Vol(result));
            Assert.True(dv <= Math.Sqrt(3) * h * area, $"{what}: volume {Vol(exact):G8} vs {Vol(result):G8}");
            Assert.True(dv <= MeasuredVolumeSlack * h * area, $"{what}: volume {Vol(exact):G8} vs {Vol(result):G8}, {dv / (h * area):G3} of cell size x area (measured at most 0.032)");
        }

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
            AssertWindingMatches(exact, w.Result, w.UsedCellSize, $"{name} {op} (cell {w.UsedCellSize:G3})", ThinResult(name, op));
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
            AssertWindingMatches(exact, s.Result, s.UsedCellSize, name + " solidify of the soup", false);
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
            AssertWindingMatches(a, s.Result, s.UsedCellSize, name + " solidify of the closed solid", false);
            double tol = MeasuredVolumeSlack * s.UsedCellSize * Math.Max(Area(a), Area(s.Result));
            // the second pass runs on the grid of the first result's own bounds: the same bound again (two passes: twice)
            var s2 = new MeshWindingNumberSolidify(s.Result) { GridResolution = 64 };
            Assert.True(s2.Compute());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(s2.Result, true));
            double tol2 = tol + MeasuredVolumeSlack * s2.UsedCellSize * Math.Max(Area(s.Result), Area(s2.Result));
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
        /// asserted for the vertices of the result with the measured factor 1.1 of the class comment (RefineNonManifold on), checked at the vertices and at the triangle centroids (the vertices lie on the offset surface, so only the centroids show the band).
        /// </summary>
        [Theory, MemberData(nameof(WrapInputs))]
        public void AlphaWrap_ContainsTheInput_WithinItsBand(string name, double alphaDivisor, double offsetDivisor)
        {
            DMesh3 input = WrapInput(name);
            double diag = input.CachedBounds.DiagonalLength;
            var w = new MeshAlphaWrap(input) { Alpha = diag / alphaDivisor, Offset = diag / offsetDivisor };
            Assert.True(w.Compute(), $"{name}: wrap {w.Status} {w.Error}");
            DMesh3 r = w.Result;
            Assert.Equal(1.0, w.AlphaCoarsening);   // nothing coarsened: the requested values are the ones used
            Assert.Equal(diag / alphaDivisor, w.UsedAlpha);
            Assert.Equal(diag / offsetDivisor, w.UsedOffset);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r, true));
            var wrapTree = new DMeshAABBTree3(r, true);
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
            // at the vertices the distance is the offset (they lie on the offset surface); the band is visible between them, at the triangle centroids
            foreach (int t in r.TriangleIndices()) {
                inputTree.FindNearestTriangle(r.GetTriCentroid(t), out double dsq);
                maxD = Math.Max(maxD, Math.Sqrt(dsq));
            }
            Assert.True(maxD <= 1.1 * (w.UsedOffset + w.UsedAlpha), $"{name}: a wrap vertex or triangle centroid at {maxD:G6} from the input, offset + alpha {w.UsedOffset + w.UsedAlpha:G6}");
            if (name != "soup") Assert.True(Vol(r) > Vol(input), $"{name}: wrap volume {Vol(r):G6} not above input {Vol(input):G6}");
        }

        // ---------------------------------------------------------------- bounded simplification

        /// <summary>
        /// Reducer with MaxDeviation D and MaxDeviationTwoSided: every point of the result is within D of the input and every point of
        /// the input within D of the result (a certificate, docs/design/BOUNDED_SIMPLIFICATION.md). Checked on the vertices and the triangle centroids of
        /// both meshes (a sample of the certificate). The volume then differs by at most D times the larger area. Closed and clean with PreventSelfIntersections.
        /// reduced = false rows: the coarse input has no collapse that stays within D of its own surface (the certified bound refuses every one), so
        /// the mesh must stay as it is (and the bands hold trivially).
        /// </summary>
        [Theory]
        [InlineData("sphere", 0.02, true)]
        [InlineData("sphere", 0.005, false)]
        [InlineData("torus", 0.02, true)]
        [InlineData("torus", 0.01, false)]
        [InlineData("union", 0.03, true)]
        [InlineData("union", 0.01, true)]
        public void Simplification_TwoSidedDeviation_BoundsVolumeAndDistance(string name, double deviation, bool reduced)
        {
            DMesh3 input = WrapInput(name), m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = deviation, MaxDeviationTwoSided = true, PreventSelfIntersections = true };
            r.ReduceToTriangleCount(Math.Max(20, input.TriangleCount / 8));
            if (reduced) Assert.True(m.TriangleCount < input.TriangleCount, $"{name}: nothing was reduced");
            else Assert.True(m.TriangleCount == input.TriangleCount, $"{name}: {input.TriangleCount} -> {m.TriangleCount} triangles; expected the bound to refuse every collapse");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m, true));
            double slack = 1e-9;
            AssertWithin(m, input, deviation + slack, name + " result in the input band");
            AssertWithin(input, m, deviation + slack, name + " input in the result band");
            double tol = deviation * Math.Max(Area(input), Area(m));
            Assert.True(Math.Abs(Vol(input) - Vol(m)) <= tol, $"{name}: volume {Vol(input):G8} -> {Vol(m):G8}, tolerance {tol:G3}");
        }

        /// <summary>
        /// Separates the two-sided option from the one-sided one: a sphere with one vertex pulled out into a spike of height 0.6 whose width is below D. The one-sided bound only
        /// requires the result to stay within D of the input, which cutting the spike satisfies, and the spike is cut (the input's tip is then
        /// farther than D from the result); the two-sided bound refuses those collapses and keeps the tip within D.
        /// </summary>
        [Fact]
        public void Simplification_TwoSided_KeepsASpike_OneSidedCutsIt()
        {
            DMesh3 input = CurvedSolids.Sphere(1, Vector3d.Zero, 7);
            int tip = input.VertexIndices().First();
            input.SetVertex(tip, input.GetVertex(tip).Normalized * 1.6);
            const double D = 0.25;
            double oneSided = ReduceAndMeasureInputToResult(input, D, false), twoSided = ReduceAndMeasureInputToResult(input, D, true);
            Assert.True(oneSided > D, $"one-sided: the input is {oneSided:G4} from the result; expected the spike to be cut (more than {D}; the spike is thinner than the bound, which the one-sided option documents as cut off)");
            Assert.True(twoSided <= D + 1e-9, $"two-sided: the input is {twoSided:G4} from the result, bound {D}");
        }

        static double ReduceAndMeasureInputToResult(DMesh3 input, double d, bool twoSided)
        {
            DMesh3 m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = d, MaxDeviationTwoSided = twoSided, PreventSelfIntersections = true };
            r.ReduceToTriangleCount(24);
            Assert.True(m.TriangleCount < input.TriangleCount);
            return MaxDistance(input, m);
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
        /// (1) Origin only places the lattice frame in the world (the mesh is the lattice-frame mesh mapped by Origin + Rotation v), so a mesh computed with
        /// Origin = s is the base mesh translated by s: same vertex count, each vertex within 1e-9 of base + s (the sum rounds once).
        /// (2) The lattice is periodic with CellSize: a box moved by whole periods (Origin fixed) gives the translated solid. The grid is aligned to the
        /// box, so the fields agree up to the rounding of the phase 2 pi p / L (relative 1e-14 at these magnitudes); the vertices move by that over the
        /// field gradient, bounded below away from the critical points of a nodal field, so volume and area are compared with the relative tolerance 1e-7.
        /// A wrong period (CellSize used as 1.01) fails (2): the shifted box sees another phase.
        /// </summary>
        [Theory, MemberData(nameof(LatticeKinds))]
        public void TpmsLattice_OriginTranslates_AndTheLatticeIsPeriodicUnderWholePeriodBoxShifts(int typeValue, int styleValue)
        {
            var type = (TpmsType)typeValue; var style = (TpmsStyle)styleValue;
            var box = new AxisAlignedBox3d(new Vector3d(0.1, 0.2, 0.3), new Vector3d(1.6, 1.4, 1.7));
            var baseLattice = Lattice(type, style, Vector3d.Zero, box); baseLattice.Compute();
            Assert.Equal(TpmsStatus.Ok, baseLattice.Status);
            double v0 = Vol(baseLattice.Mesh), a0 = Area(baseLattice.Mesh);
            foreach (Vector3d shift in new[] { new Vector3d(1, 0, 0), new Vector3d(-2, 1, 3), new Vector3d(0.3, -0.7, 5.5) }) {
                var o = Lattice(type, style, shift, box); o.Compute();
                Assert.Equal(TpmsStatus.Ok, o.Status);
                Assert.Equal(baseLattice.Mesh.VertexCount, o.Mesh.VertexCount);
                Assert.Equal(baseLattice.Mesh.TriangleCount, o.Mesh.TriangleCount);
                foreach (int v in baseLattice.Mesh.VertexIndices())
                    Assert.True((o.Mesh.GetVertex(v) - (baseLattice.Mesh.GetVertex(v) + shift)).Length <= 1e-9, $"{type} {style} origin {shift}: vertex {v} is not the translated one");
            }
            foreach (Vector3d shift in new[] { new Vector3d(1, 0, 0), new Vector3d(-2, 1, 3), new Vector3d(0, 0, 4) }) {
                var movedBox = new AxisAlignedBox3d(box.Min + shift, box.Max + shift);
                var b = Lattice(type, style, Vector3d.Zero, movedBox); b.Compute();
                Assert.Equal(TpmsStatus.Ok, b.Status);
                Assert.True(Math.Abs(Vol(b.Mesh) - v0) <= 1e-7 * v0 && Math.Abs(Area(b.Mesh) - a0) <= 1e-7 * a0,
                    $"{type} {style} box shift {shift}: volume {v0:R} vs {Vol(b.Mesh):R}, area {a0:R} vs {Area(b.Mesh):R}");
            }
        }
    }
}
