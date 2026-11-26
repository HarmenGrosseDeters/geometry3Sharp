// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using gs;

namespace g3
{
    /// <summary> The triply periodic minimal surface (nodal approximation) that makes the lattice. </summary>
    public enum TpmsType { Gyroid, SchwarzP, SchwarzD, Neovius, IWP, Lidinoid }

    /// <summary>
    /// Sheet: the solid is {|f| &lt;= t} (a wall around the surface). SkeletalLow: {f &lt;= c}. SkeletalHigh: {f &gt;= -c}
    /// (the other network of the same surface; for a volume fraction of 0.5 both fill half of the space).
    /// </summary>
    public enum TpmsStyle { Sheet, SkeletalLow, SkeletalHigh }

    /// <summary>
    /// Outcome of <see cref="TpmsLattice.Compute"/>. Mesh is set only for Ok. NotResolved (appended last; #1596): the grid is too coarse for the lattice
    /// asked for and the mesh was dropped: an Enclosure that is the solid box (its volume at least <see cref="TpmsLattice.EnclosureRefusalFraction"/> of the box grown by the margin),
    /// or a plain Sheet that realized less than <see cref="TpmsLattice.SheetRefusalRatio"/> of the requested volume fraction. RealizedVolumeFraction and
    /// MeanWallThickness of the dropped result stay set, and Message says what to change.
    /// </summary>
    public enum TpmsStatus { Ok, InvalidOptions, InvalidDomain, TooLarge, ContouringFailed, NotClean, SelfIntersecting, Cancelled, NotResolved }

    /// <summary>
    /// A graded cell size with a library-derived bound of the Jacobian of its phase map (periods per length), from
    /// <see cref="TpmsLattice.LinearGrade"/>. The bound holds for transverse coordinates within TransverseHalfExtent of the frame origin.
    /// </summary>
    public sealed class TpmsPhaseGrade
    {
        /// <summary> Local position to phase in periods. </summary>
        public Func<Vector3d, Vector3d> Map { get; }
        /// <summary> A bound of the operator norm of the Jacobian of Map within the transverse extent, periods per unit length. </summary>
        public double JacobianBound { get; }
        /// <summary> The grading axis. </summary>
        public int Axis { get; }
        /// <summary> The bound holds while the two other coordinates stay within +- this. </summary>
        public double TransverseHalfExtent { get; }
        internal TpmsPhaseGrade(Func<Vector3d, Vector3d> map, double bound, int axis, double extent)
        { Map = map; JacobianBound = bound; Axis = axis; TransverseHalfExtent = extent; }
    }

    /// <summary>
    /// The weight of a two-type hybrid lattice (<see cref="TpmsLattice.SecondType"/>): w(p) in [0, 1] in the lattice frame, 1 selecting
    /// <see cref="TpmsLattice.Type"/> and 0 the second type, with a bound of its gradient (per unit length). Made by
    /// <see cref="TpmsLattice.SigmoidBlend"/> (the library derives the bound) or from a delegate with a caller supplied bound.
    /// A delegate must be pure and thread safe (it is called from the parallel field evaluation) and its bound is trusted, not checked; the
    /// error term of <see cref="TpmsLattice.Enclosure"/> assumes a rounding error of the weight of at most 8 u + 2 u s |grad w| (u = 2^-53, s the power of two above the
    /// largest coordinate), which holds for <see cref="TpmsLattice.SigmoidBlend"/> and is the caller's responsibility for a delegate. A weight that returns NaN fails
    /// the computation with <see cref="TpmsStatus.ContouringFailed"/>.
    /// </summary>
    public sealed class TpmsBlendWeight
    {
        /// <summary> Local position to weight; values outside [0, 1] are clamped (which keeps the bound). </summary>
        public Func<Vector3d, double> Weight { get; }
        /// <summary> A bound of |grad w| per unit length; NaN when unknown (then <see cref="TpmsLattice.Enclosure"/> is refused). </summary>
        public double GradientBound { get; }
        /// <summary> A weight from a delegate. gradientBound must be an upper bound of |grad w| (NaN: unknown, no Enclosure). </summary>
        public TpmsBlendWeight(Func<Vector3d, double> weight, double gradientBound = double.NaN)
        {
            if (weight == null) throw new ArgumentNullException(nameof(weight));
            Weight = weight; GradientBound = gradientBound;
        }
    }

    /// <summary>
    /// Triply periodic minimal surface lattices (gyroid, Schwarz P and D, Neovius, I-WP) for additive manufacturing,
    /// clipped to a box or to the inside of a closed mesh. Marching cubes with <see cref="MarchingCubes.RootfindingModes.LinearEdges"/>
    /// on the field F = max((g - c) / Lw, d): g the style value of the nodal approximation, c the level for the volume
    /// fraction (the sampled quantile of g over one period: 64^3 midpoint rule, linear between samples), Lw a proven bound of the gradient of g so that F is 1-Lipschitz,
    /// d the signed distance to the domain. The solid is {F &lt;= 0}, so the cut faces belong to it and the result is closed.
    ///
    /// Guarantees (docs/design/TPMS_LATTICE.md section 5): Status Ok only for a mesh that passed MeshRobustRepair.NotCleanReason
    /// (closed, manifold, finite, oriented, no degenerate triangles) and, with VerifySelfIntersections, the exact
    /// self-intersection check of the returned coordinates. With Enclosure the solid contains the exact lattice
    /// (never a wall thinner than designed), at the price of a larger volume. No topology guarantee for walls thinner than
    /// about two cubes (CellSize / Divisions) with marching cubes; for thinner sheet walls see TwoLevelSheet.
    ///
    /// The lattice lives in a local frame (Origin, Rotation): phase zero at the origin, the Box in local coordinates,
    /// the grid anchored at multiples of the cube size. The result is mapped to world coordinates before the certificate (which so
    /// checks the returned coordinates; far from the origin the rounding of the transform can make it fail, #1569), so the
    /// operation is equivariant under similarities (bit for bit under power-of-two scales): a generator, not a field that is
    /// invariant under rotating its input.
    /// </summary>
    public class TpmsLattice
    {
        /// <summary> The surface (nodal approximation; see docs/design/TPMS_LATTICE.md for the formulas and sources). </summary>
        public TpmsType Type = TpmsType.Gyroid;
        /// <summary> Sheet or skeletal solid. </summary>
        public TpmsStyle Style = TpmsStyle.Sheet;
        /// <summary> Period of the lattice along each axis, finite and positive. </summary>
        public double CellSize = 1;
        /// <summary> Marching cubes cubes per period, 4 to 512. The cube is CellSize / Divisions; a wall should be at least 2 cubes thick. </summary>
        public int Divisions = 16;
        /// <summary> Target solid fraction of the infinite lattice, in (0, 1); clamped to [0.001, 0.999] for the level. </summary>
        public double VolumeFraction = 0.3;
        /// <summary>
        /// If &gt; 0 it replaces VolumeFraction: a wall of this thickness (Sheet only), through the first-order map
        /// fraction = (A / L^2) (w / L) with A the area of the surface per period L^3. Approximate; MeanWallThickness reports the result.
        /// </summary>
        public double WallThickness = 0;
        /// <summary> Optional grading: the volume fraction at a world position (replaces VolumeFraction; not with Enclosure). Called from several threads with ParallelCompute, so it must be thread-safe; a NaN gives ContouringFailed, a fraction outside (0, 1) is clamped to [0.001, 0.999]. </summary>
        public Func<Vector3d, double> VolumeFractionAt;
        /// <summary>
        /// Graded cell size, general form: the phase of the lattice in periods as a function of the local position (null: x / CellSize,
        /// a constant period). The field is f(2 pi PhaseMap(x)). It must be pure, thread-safe and finite on the grid. CellSize stays the
        /// nominal period that sets the cube (CellSize / Divisions): set it to the smallest local period. A general map distorts the cells
        /// (see docs/design/TPMS_GRADED_CELL.md). The map is sampled on a 9^3 set of points of the domain (and at +-h/8 around them, so it must be finite slightly outside the domain) to check that the grid resolves the smallest period: a narrow feature between the samples can be missed (the result is still certified). Not with WallThickness, and not with Enclosure (the library cannot certify the Jacobian
        /// bound of an arbitrary delegate: use <see cref="Grade"/>). Do not set both.
        /// </summary>
        public Func<Vector3d, Vector3d> PhaseMap;
        /// <summary>
        /// Graded cell size with a library-derived Jacobian bound (<see cref="LinearGrade"/>): the only graded form allowed with Enclosure.
        /// The box (or domain mesh) must lie within the grade's transverse extent. Do not set both Grade and PhaseMap.
        /// </summary>
        public TpmsPhaseGrade Grade;
        /// <summary>
        /// A second type for a hybrid lattice (docs/design/TPMS_LATTICE.md section 9), blended with <see cref="Type"/> by <see cref="Blend"/>:
        /// the solid is {h &lt;= 0} with h = w (g_A - c_A) + (1 - w) (g_B - c_B), g the style value and c the level of the target fraction of each type, so
        /// that where w is 1 or 0 the lattice is the pure A or B lattice with exactly the requested fraction (in the transition the fraction is not
        /// controlled and only reported). Same Style, CellSize, Rotation and (with a Grade or PhaseMap) phase for both. Set together with Blend; not with VolumeFractionAt.
        /// </summary>
        public TpmsType? SecondType;
        /// <summary> The weight of the hybrid (see <see cref="SecondType"/>); 1 selects <see cref="Type"/>. Set together with SecondType. </summary>
        public TpmsBlendWeight Blend;
        /// <summary> Lattice frame: the world position of the local origin (lattice phase zero). </summary>
        public Vector3d Origin = Vector3d.Zero;
        /// <summary> Lattice frame: local to world rotation (orthonormal, determinant +1). </summary>
        public Matrix3d Rotation = Matrix3d.Identity;
        /// <summary> Domain in local coordinates (used if DomainMesh is null). </summary>
        public AxisAlignedBox3d Box = new AxisAlignedBox3d(Vector3d.Zero, 1);
        /// <summary> Domain as the inside of a closed manifold mesh in world coordinates (wins over Box). Not with Enclosure. The cut lies at the mesh up to the rounding of the contour (a fraction of a cube; #1372: it used to lie up to a cube outside it near the mesh's bounds). </summary>
        public DMesh3 DomainMesh;
        /// <summary> Run the exact self-intersection check on the result (cost O(T log T), see the benchmark). </summary>
        public bool VerifySelfIntersections = true;
        /// <summary> The solid contains {F &lt;= 0} of the exact lattice (box domain, constant fraction, dyadic grid only). </summary>
        public bool Enclosure = false;
        /// <summary> Enclosure: a result whose volume is at least this part of the volume of the box grown by IsoValueUsed on every side (the extent the enclosure fills when it has no voids) is the solid box, not a lattice: Status NotResolved (#1596). Independent of the box size, unlike RealizedVolumeFraction. </summary>
        public const double EnclosureRefusalFraction = 0.99;
        /// <summary> A plain Sheet (marching cubes, constant fraction, no hybrid) that realizes less than this part of the requested volume fraction lost its wall: Status NotResolved (#1596). </summary>
        public const double SheetRefusalRatio = 0.5;
        /// <summary> <see cref="MinDivisionsForEnclosure"/> asks for an estimated enclosure fraction of at most this. </summary>
        public const double EnclosureEstimateTarget = 0.9;
        /// <summary>
        /// Sheet style only, opt-in (default off: marching cubes as before). The two sides of the wall are contoured as the two levels
        /// f = +t and f = -t of the surface function with <see cref="TwoLevelMarchingTetrahedra"/> instead of one level of |f| - t with
        /// marching cubes: a wall thinner than two cubes, down to a small fraction of a cube, keeps both sides and stays connected
        /// as the grid resolves the surface itself, and the mesh has no self-intersections by construction. About 2.5 times the
        /// triangles of marching cubes. Not with Enclosure and not with a hybrid (SecondType). See docs/design/TPMS_LATTICE.md section 10.
        /// </summary>
        public bool TwoLevelSheet = false;
        /// <summary> Slab-parallel field evaluation (id-identical to serial). </summary>
        public bool ParallelCompute = true;
        /// <summary> Upper limit of grid nodes, checked before any allocation. </summary>
        public long MaxCells = 1L << 28;
        /// <summary> If it returns true, Compute stops with Status Cancelled. </summary>
        public Func<bool> CancelF = () => false;

        // ---- outputs ----
        public TpmsStatus Status;
        /// <summary> Why the status is not Ok, or for NotResolved what to change (an Enclosure that fills at least <see cref="EnclosureRefusalFraction"/> of the box grown by the margin; a Sheet that realized less than <see cref="SheetRefusalRatio"/> of the requested fraction: a wall thinner than about two cubes, see TwoLevelSheet); empty otherwise. </summary>
        public string Message = "";
        /// <summary> The lattice mesh in world coordinates; null unless Status == Ok. </summary>
        public DMesh3 Mesh;
        /// <summary> The level c (or t) for the target fraction (not set with grading). </summary>
        public double Level;
        /// <summary> The level of the second type of a hybrid for its target fraction (0 without a hybrid). </summary>
        public double SecondLevel;
        /// <summary> Mesh volume / domain volume (box volume or domain mesh volume). </summary>
        public double RealizedVolumeFraction;
        /// <summary> 2 V / A of the result (the wall thickness of a sheet; a report, not a promise). </summary>
        public double MeanWallThickness;
        /// <summary> 0 for Ok; 1 when the exact check rejected the result (it stops at the first reason, so this is "at least one", not a count). </summary>
        public int SelfIntersectionsFound;
        /// <summary> Grid nodes of the successful (or last) attempt. </summary>
        public long CellsEvaluated;
        /// <summary> Number of grid shifts tried before the contouring succeeded (0 = the first grid; see the design note section 4). </summary>
        public int GridShiftsUsed;
        /// <summary> The marching cubes level used (0 unless Enclosure: sqrt(3) h / 2 plus the floating point pad, in length units). </summary>
        public double IsoValueUsed;
        // the sliver guard of the field, in cubes (2^-8); 0 switches it off. Internal: for the tests that compare with and without it
        internal double SliverGuardCubeFraction = 1.0 / 256;
        // test hook (#1596): return the mesh of a result that Compute refuses as NotResolved, for the tests of the exact enclosure proof that run at a coarse grid
        internal bool AllowUnresolved;
        // test hooks: reject a grid attempt as if the contouring had failed; the lowest node of the grid last used
        internal Func<int, bool> RejectAttempt;
        internal Vector3d LastGridMin;
        /// <summary> With a PhaseMap: the smallest local period seen on the sample points (1 / the largest sampled phase gradient), else 0. </summary>
        public double MinLocalCellSize;
        /// <summary>
        /// The Lipschitz bound of the lattice field in length units that normalises it (Lf 2 pi J); a report for tests and proofs. For a hybrid it is the bound of
        /// h: the larger phase part plus the weight term (TermSum and level of both types) times the gradient bound of the Blend; for a Blend whose bound is NaN
        /// (unknown, not with Enclosure) the weight term is left out and this is not a full Lipschitz bound.
        /// </summary>
        public double LipschitzBoundUsed;
        /// <summary> NotCleanReason of a rejected result. </summary>
        public string NotCleanReason = "";

        const int TableSamples = 64;
        const int TableKnots = 1024;
        const double MinFraction = 0.001, MaxFraction = 0.999;
        // fractions of a cube the grid is moved down on a retry; defined in the lattice frame, so similarity-invariant
        static readonly double[] GridShifts = { 0.5, 0.2360679774997897, 0.7639320225002103, 0.3819660112501051 };
        // with Enclosure the shifted grid must stay exactly representable (dyadic fractions of a cube)
        static readonly double[] DyadicGridShifts = { 0.5, 0.25, 0.75, 0.125 };

        // ================= the nodal approximations =================

        /// <summary> f(p) for the phase p = 2 pi x / L (radians). </summary>
        public static double Evaluate(TpmsType type, double px, double py, double pz)
        {
            double sx = Math.Sin(px), cx = Math.Cos(px), sy = Math.Sin(py), cy = Math.Cos(py), sz = Math.Sin(pz), cz = Math.Cos(pz);
            switch (type) {
                case TpmsType.Gyroid: return sx * cy + sy * cz + sz * cx;
                case TpmsType.SchwarzP: return cx + cy + cz;
                case TpmsType.SchwarzD: return sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
                case TpmsType.Neovius: return 3 * (cx + cy + cz) + 4 * cx * cy * cz;
                case TpmsType.IWP:
                    return 2 * (cx * cy + cy * cz + cz * cx) - (Math.Cos(2 * px) + Math.Cos(2 * py) + Math.Cos(2 * pz));
                case TpmsType.Lidinoid:
                    return 0.5 * (Math.Sin(2 * px) * cy * sz + Math.Sin(2 * py) * cz * sx + Math.Sin(2 * pz) * cx * sy)
                         - 0.5 * (Math.Cos(2 * px) * Math.Cos(2 * py) + Math.Cos(2 * py) * Math.Cos(2 * pz) + Math.Cos(2 * pz) * Math.Cos(2 * px))
                         + 0.15;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary> A proven bound of |grad f| in phase coordinates (triangle inequality on the closed forms; see the design note). </summary>
        internal static double GradientBound(TpmsType type)
        {
            double r3 = Math.Sqrt(3.0);
            switch (type) {
                case TpmsType.Gyroid: return 2 * r3;     // |cos x cos y - sin z sin x| <= 2 per component
                case TpmsType.SchwarzP: return r3;
                case TpmsType.SchwarzD: return 4 * r3;   // four products of three factors per component
                case TpmsType.Neovius: return 7 * r3;    // 3|sin x| + 4|sin x cos y cos z|
                case TpmsType.IWP: return 6 * r3;        // 2|sin x|(|cos y| + |cos z|) + 2|sin 2x|
                // d/dx = cos 2x cos y sin z + 0.5 sin 2y cos z cos x - 0.5 sin 2z sin x sin y + sin 2x (cos 2y + cos 2z): <= 1 + 0.5 + 0.5 + 2 = 4
                case TpmsType.Lidinoid: return 4 * r3;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        static readonly ConcurrentDictionary<TpmsType, double> gradientRatios = new ConcurrentDictionary<TpmsType, double>();

        // sampled maximum of |grad f| over one period (central differences, 24^3 points) divided by the proven bound: in (0, 1]
        internal static double GradientRatio(TpmsType type)
        {
            return gradientRatios.GetOrAdd(type, t => {
                const int n = 24;
                double e = 1e-5, max = 0, step = 2 * Math.PI / n;
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j)
                        for (int l = 0; l < n; ++l) {
                            double x = (i + 0.37) * step, y = (j + 0.61) * step, z = (l + 0.23) * step;
                            double gx = (Evaluate(t, x + e, y, z) - Evaluate(t, x - e, y, z)) / (2 * e);
                            double gy = (Evaluate(t, x, y + e, z) - Evaluate(t, x, y - e, z)) / (2 * e);
                            double gz = (Evaluate(t, x, y, z + e) - Evaluate(t, x, y, z - e)) / (2 * e);
                            max = Math.Max(max, Math.Sqrt(gx * gx + gy * gy + gz * gz));
                        }
                return Math.Min(1, 1.05 * max / GradientBound(t));
            });
        }

        // bound of the sum of the absolute values of the terms, for the rounding error of an evaluation
        internal static double TermSum(TpmsType type)
        {
            switch (type) {
                case TpmsType.Gyroid: return 3;
                case TpmsType.SchwarzP: return 3;
                case TpmsType.SchwarzD: return 4;
                case TpmsType.Neovius: return 13;
                case TpmsType.Lidinoid: return 3.15;
                default: return 9;
            }
        }

        // g = style value: the solid is {g <= c}
        static double StyleValue(TpmsStyle style, double f)
        {
            return style == TpmsStyle.Sheet ? Math.Abs(f) : (style == TpmsStyle.SkeletalLow ? f : -f);
        }

        // ================= volume fraction <-> level =================

        static readonly ConcurrentDictionary<(TpmsType, TpmsStyle), double[]> tables = new ConcurrentDictionary<(TpmsType, TpmsStyle), double[]>();
        static readonly ConcurrentDictionary<TpmsType, double> areaRatios = new ConcurrentDictionary<TpmsType, double>();

        /// <summary>
        /// The quantile table of g over one period: q[k] is the level for the fraction k / 1024 (midpoint rule on a cell-centred
        /// 64^3 grid of one period, sorted, linear interpolation). Cached per type and style. Internal, for tests.
        /// </summary>
        internal static double[] QuantileTable(TpmsType type, TpmsStyle style)
        {
            return tables.GetOrAdd((type, style), key => BuildTable(key.Item1, key.Item2, TableSamples));
        }

        /// <summary> The table for a given sampling resolution (not cached). Internal, for tests (an independent finer table). </summary>
        internal static double[] BuildTable(TpmsType type, TpmsStyle style, int n)
        {
            int total = checked(n * n * n);
            var values = new double[total];
            double step = 2 * Math.PI / n;
            int idx = 0;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    for (int k = 0; k < n; ++k)
                        values[idx++] = StyleValue(style, Evaluate(type, (i + 0.5) * step, (j + 0.5) * step, (k + 0.5) * step));
            Array.Sort(values);
            var table = new double[TableKnots + 1];
            for (int m = 0; m <= TableKnots; ++m) {
                double pos = (double)m / TableKnots * total - 0.5;
                if (pos <= 0) { table[m] = values[0]; continue; }
                if (pos >= total - 1) { table[m] = values[total - 1]; continue; }
                int lo = MathUtil.ToIntSaturating(pos);
                double t = pos - lo;
                table[m] = values[lo] * (1 - t) + values[lo + 1] * t;
            }
            return table;
        }

        /// <summary> The level for a volume fraction, from a quantile table (monotone, linear between the knots). </summary>
        internal static double LevelForFraction(double[] table, double fraction)
        {
            double fr = Math.Min(Math.Max(fraction, MinFraction), MaxFraction);
            double pos = fr * TableKnots;
            int lo = Math.Min(MathUtil.ToIntSaturating(pos), TableKnots - 1);
            double t = pos - lo;
            return table[lo] * (1 - t) + table[lo + 1] * t;
        }

        /// <summary>
        /// Area of the surface f = 0 per period, in units of L^2 (marching cubes of one period with 48 cubes per period,
        /// triangles with the centroid in the half-open period). Cached per type.
        /// </summary>
        internal static double AreaRatio(TpmsType type)
        {
            return areaRatios.GetOrAdd(type, t => {
                const int n = 48;
                double h = 1.0 / n;
                var field = new FieldFunction { Type = t, Style = TpmsStyle.SkeletalLow, K = 2 * Math.PI, Level = 0, InvLipschitz = 1, UseBox = false };
                var mc = new MarchingCubes { Implicit = field, Bounds = new AxisAlignedBox3d(new Vector3d(-2 * h, -2 * h, -2 * h), new Vector3d(1 + 2.5 * h, 1 + 2.5 * h, 1 + 2.5 * h)),
                    CubeSize = h, IsoValue = 1e-9, ParallelCompute = false };
                mc.Generate();
                double area = 0;
                foreach (int tid in mc.Mesh.TriangleIndices()) {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    mc.Mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    Vector3d ce = (a + b + c) / 3;
                    if (ce.x >= 0 && ce.x < 1 && ce.y >= 0 && ce.y < 1 && ce.z >= 0 && ce.z < 1)
                        area += 0.5 * (b - a).Cross(c - a).Length;
                }
                return area;
            });
        }

        // The sliver guard of the field (see FieldFunction.Value): a value within snap of the level iso counts as inside. Only ever down:
        // the solid becomes a superset, so the enclosure survives. Internal for the tests that pin the direction and the range.
        internal static double SliverGuard(double value, double iso, double snap)
        {
            if (snap > 0 && Math.Abs(value - iso) < snap) return iso - snap;
            return value;
        }

        // ================= the field =================

        sealed class FieldFunction : ImplicitFunction3d
        {
            public TpmsType Type; public TpmsStyle Style;
            public double K;                  // 2 pi / L
            public Func<Vector3d, Vector3d> PhaseMap;   // graded cell size: local position -> phase in periods
            public double Level;              // c (constant fraction)
            public double InvLipschitz;       // 1 / Lw
            public double Iso, Snap;          // the contouring level and the sliver guard: a value within Snap of Iso is moved to Iso - Snap
            public bool UseBox; public Vector3d Center, Half;
            public Func<Vector3d, double> LevelAt;    // grading: local position -> level
            public volatile bool WeightNaN;
            public volatile bool LevelNaN;     // VolumeFractionAt returned NaN somewhere (a NaN node counts as outside and would go unseen)
            public bool Hybrid; public TpmsType Type2; public double Level2; public Func<Vector3d, double> Weight;   // two-type hybrid (section 9)
            public DMeshAABBTree3 DomainTree; public AxisAlignedBox3d DomainBounds;

            public double Value(ref Vector3d p)
            {
                double v = Raw(ref p);
                // Sliver guard: a node whose value is within rounding of the level makes a crossing at a cube corner and so degenerate
                // triangles (found: large lattices with a position dependent level or a graded cell size). Values within Snap of the
                // level count as inside, with Snap = ratio * 2^-8 cube (ratio: see Compute): where F is 1-Lipschitz in length every crossing is
                // then at least ratio / 256 of an edge from a node. That is a heuristic size, not a proof (the clip distance d changes by h per
                // edge, and a position dependent level makes F not 1-Lipschitz); the result is certified separately. The surface moves by less than 2 Snap, and only down, which keeps the
                // solid a superset (the enclosure survives).
                return SliverGuard(v, Iso, Snap);
            }

            // The parts of the sheet field for TwoLevelSheet: F = max((|f| - c) / Lw, d) as g = f / Lw, w = c / Lw and d.
            public double SheetValue(ref Vector3d p)
            {
                double px, py, pz;
                if (PhaseMap != null) {
                    Vector3d ph = PhaseMap(p);
                    px = 2 * Math.PI * ph.x; py = 2 * Math.PI * ph.y; pz = 2 * Math.PI * ph.z;
                } else {
                    px = K * p.x; py = K * p.y; pz = K * p.z;
                }
                return Evaluate(Type, px, py, pz) * InvLipschitz;
            }

            public double SheetHalfWidth(Vector3d p) { return (LevelAt != null ? LevelAt(p) : Level) * InvLipschitz; }

            public double DomainValue(ref Vector3d p)
            {
                if (UseBox) {
                    double dx = Math.Abs(p.x - Center.x) - Half.x, dy = Math.Abs(p.y - Center.y) - Half.y, dz = Math.Abs(p.z - Center.z) - Half.z;
                    return Math.Max(dx, Math.Max(dy, dz));
                }
                return DomainDistance(p);
            }

            double Raw(ref Vector3d p)
            {
                double px, py, pz;
                if (PhaseMap != null) {
                    Vector3d ph = PhaseMap(p);
                    px = 2 * Math.PI * ph.x; py = 2 * Math.PI * ph.y; pz = 2 * Math.PI * ph.z;
                } else {
                    px = K * p.x; py = K * p.y; pz = K * p.z;
                }
                double g;
                if (Hybrid) {
                    // h = w (g_A - c_A) + (1 - w) (g_B - c_B), w clamped to [0, 1] (a clamp keeps the Lipschitz bound; NaN stays NaN)
                    double w0 = Weight(p);
                    if (double.IsNaN(w0)) WeightNaN = true;
                    double w = Math.Min(1.0, Math.Max(0.0, w0));
                    double ga = StyleValue(Style, Evaluate(Type, px, py, pz)) - Level;
                    double gb = StyleValue(Style, Evaluate(Type2, px, py, pz)) - Level2;
                    g = (w * ga + (1 - w) * gb) * InvLipschitz;
                } else {
                    double f = Evaluate(Type, px, py, pz);
                    double c = LevelAt != null ? LevelAt(p) : Level;
                    if (double.IsNaN(c)) LevelNaN = true;
                    g = (StyleValue(Style, f) - c) * InvLipschitz;
                }
                if (UseBox) {
                    double dx = Math.Abs(p.x - Center.x) - Half.x, dy = Math.Abs(p.y - Center.y) - Half.y, dz = Math.Abs(p.z - Center.z) - Half.z;
                    double d = Math.Max(dx, Math.Max(dy, dz));
                    return Math.Max(g, d);
                }
                if (DomainTree != null) {
                    double d = DomainDistance(p);
                    return Math.Max(g, d);
                }
                return g;
            }

            // signed distance to the closed domain mesh (negative inside)
            double DomainDistance(Vector3d p)
            {
                // outside the bounds the winding number is 0 (a closed mesh): the distance is the one to the nearest triangle
                if (!DomainBounds.Contains(p)) {
                    int tid0 = DomainTree.FindNearestTriangle(p, out double d20);
                    return tid0 >= 0 ? Math.Sqrt(d20) : double.MaxValue;
                }
                double dist = double.MaxValue;
                int tid = DomainTree.FindNearestTriangle(p, out double d2);
                if (tid >= 0) dist = Math.Sqrt(d2);
                bool inside = DomainTree.FastWindingNumber(p) > 0.5;
                return inside ? -dist : dist;
            }
        }

        sealed class PartFunction : ImplicitFunction3d
        {
            public delegate double Part(ref Vector3d p);
            readonly Part part;
            public PartFunction(Part part) { this.part = part; }
            public double Value(ref Vector3d p) { return part(ref p); }
        }

        // ================= Compute =================

        void Fail(TpmsStatus status, string message)
        {
            Status = status; Message = message; Mesh = null;
        }

        /// <summary> Build the lattice from the current options. Reusable: every call recomputes everything and resets the outputs. </summary>
        public void Compute()
        {
            Mesh = null; Message = ""; NotCleanReason = ""; Level = 0; RealizedVolumeFraction = 0; MeanWallThickness = 0;
            SelfIntersectionsFound = 0; CellsEvaluated = 0; GridShiftsUsed = 0; IsoValueUsed = 0; MinLocalCellSize = 0; LipschitzBoundUsed = 0; SecondLevel = 0;
            Status = TpmsStatus.InvalidOptions;

            // ---- options ----
            if (!(CellSize > 0) || double.IsInfinity(CellSize)) { Fail(TpmsStatus.InvalidOptions, "CellSize must be finite and positive"); return; }
            if (Divisions < 4 || Divisions > 512) { Fail(TpmsStatus.InvalidOptions, "Divisions must be in [4, 512]"); return; }
            if (!IsFinite(Origin)) { Fail(TpmsStatus.InvalidOptions, "Origin must be finite"); return; }
            if (!IsRotation(Rotation)) { Fail(TpmsStatus.InvalidOptions, "Rotation must be orthonormal with determinant +1"); return; }
            if (!Enum.IsDefined(typeof(TpmsType), Type) || !Enum.IsDefined(typeof(TpmsStyle), Style)) { Fail(TpmsStatus.InvalidOptions, "unknown Type or Style"); return; }
            if (VolumeFractionAt == null && WallThickness <= 0 && !(VolumeFraction > 0 && VolumeFraction < 1)) { Fail(TpmsStatus.InvalidOptions, "VolumeFraction must be in (0, 1)"); return; }
            if (double.IsNaN(WallThickness) || double.IsInfinity(WallThickness) || WallThickness < 0) { Fail(TpmsStatus.InvalidOptions, "WallThickness must be finite and not negative"); return; }
            if (WallThickness > 0 && Style != TpmsStyle.Sheet) { Fail(TpmsStatus.InvalidOptions, "WallThickness is defined for Sheet only"); return; }
            if (PhaseMap != null && WallThickness > 0) { Fail(TpmsStatus.InvalidOptions, "WallThickness assumes a constant period: not with a PhaseMap"); return; }
            if (PhaseMap != null && Grade != null) { Fail(TpmsStatus.InvalidOptions, "set either PhaseMap or Grade, not both"); return; }
            if (Grade != null && WallThickness > 0) { Fail(TpmsStatus.InvalidOptions, "WallThickness assumes a constant period: not with a Grade"); return; }
            if (TwoLevelSheet && Style != TpmsStyle.Sheet) { Fail(TpmsStatus.InvalidOptions, "TwoLevelSheet is defined for Sheet only"); return; }
            if (TwoLevelSheet && Enclosure) { Fail(TpmsStatus.InvalidOptions, "TwoLevelSheet does not give an Enclosure"); return; }
            if (TwoLevelSheet && SecondType != null) { Fail(TpmsStatus.InvalidOptions, "TwoLevelSheet is not defined for a hybrid (SecondType)"); return; }
            if (Enclosure && PhaseMap != null) { Fail(TpmsStatus.InvalidOptions, "Enclosure with a graded cell size needs a Grade (a library-derived Jacobian bound), not a general PhaseMap"); return; }
            if (Enclosure && (VolumeFractionAt != null || DomainMesh != null)) { Fail(TpmsStatus.InvalidOptions, "Enclosure needs a box domain and a constant volume fraction"); return; }
            if (MaxCells < 1) { Fail(TpmsStatus.InvalidOptions, "MaxCells must be positive"); return; }
            bool hybrid = SecondType.HasValue;
            if (hybrid != (Blend != null)) { Fail(TpmsStatus.InvalidOptions, "SecondType and Blend are set together"); return; }
            if (hybrid && !Enum.IsDefined(typeof(TpmsType), SecondType.Value)) { Fail(TpmsStatus.InvalidOptions, "unknown SecondType"); return; }
            if (hybrid && VolumeFractionAt != null) { Fail(TpmsStatus.InvalidOptions, "a hybrid (SecondType) has a constant volume fraction per type: not with VolumeFractionAt"); return; }
            if (hybrid && !double.IsNaN(Blend.GradientBound) && !(Blend.GradientBound >= 0 && !double.IsInfinity(Blend.GradientBound))) { Fail(TpmsStatus.InvalidOptions, "the gradient bound of the Blend must be finite and not negative (or NaN for unknown, not with Enclosure)"); return; }
            if (hybrid && Enclosure && double.IsNaN(Blend.GradientBound)) { Fail(TpmsStatus.InvalidOptions, "Enclosure with a hybrid needs a Blend with a finite gradient bound (SigmoidBlend derives one)"); return; }

            // ---- domain in the local frame ----
            AxisAlignedBox3d box = Box;
            DMeshAABBTree3 tree = null;
            double domainVolume;
            Matrix3d toLocal = Rotation.Transpose();
            if (DomainMesh != null) {
                if (DomainMesh.TriangleCount == 0 || !DomainMesh.IsClosed() || !DomainMesh.CheckValidity(false, FailMode.ReturnOnly)) {
                    Fail(TpmsStatus.InvalidDomain, "DomainMesh must be a valid closed mesh"); return;
                }
                if (MeshRobustRepair.NotCleanReason(DomainMesh, false) != "") {
                    Fail(TpmsStatus.InvalidDomain, "DomainMesh must be closed, manifold and oriented: " + MeshRobustRepair.NotCleanReason(DomainMesh, false)); return;
                }
                var local = new DMesh3(DomainMesh, true);
                foreach (int vid in local.VertexIndices())
                    local.SetVertex(vid, toLocal * (local.GetVertex(vid) - Origin));    // (the copy is compact: its ids are not DomainMesh's)
                Vector2d va = MeshMeasurements.VolumeArea(local, local.TriangleIndices(), local.GetVertex);
                if (!(va.x > 0)) { Fail(TpmsStatus.InvalidDomain, "DomainMesh must enclose a positive volume with outward normals"); return; }
                domainVolume = va.x;
                box = local.GetBounds();
                tree = new DMeshAABBTree3(local, true);
                tree.FastWindingNumber(box.Center);   // builds the winding cache before the field is evaluated in parallel
            } else {
                if (!IsFinite(box.Min) || !IsFinite(box.Max) || !(box.Max.x > box.Min.x) || !(box.Max.y > box.Min.y) || !(box.Max.z > box.Min.z)) {
                    Fail(TpmsStatus.InvalidDomain, "Box must be finite with positive extent"); return;
                }
                domainVolume = (box.Max.x - box.Min.x) * (box.Max.y - box.Min.y) * (box.Max.z - box.Min.z);
            }

            // ---- level ----
            double[] table = QuantileTable(Type, Style);
            double fraction = VolumeFraction;
            if (WallThickness > 0) {
                fraction = AreaRatio(Type) * (WallThickness / CellSize);
                if (fraction > 0.95) { Fail(TpmsStatus.InvalidOptions, "WallThickness is too large for the first-order map (fraction > 0.95)"); return; }
            }
            double level = LevelForFraction(table, fraction);
            double level2 = 0, fraction2 = VolumeFraction;
            if (hybrid) {
                TpmsType second = SecondType.Value;
                if (WallThickness > 0) {
                    fraction2 = AreaRatio(second) * (WallThickness / CellSize);
                    if (fraction2 > 0.95) { Fail(TpmsStatus.InvalidOptions, "WallThickness is too large for the first-order map of the second type (fraction > 0.95)"); return; }
                }
                level2 = LevelForFraction(QuantileTable(second, Style), fraction2);
            }
            double k = 2 * Math.PI / CellSize;
            double mapGradientRatio = 1;                  // sampled / bounded phase gradient (1 for the constant lattice)
            double jac = 1 / CellSize, phaseMax = 0;      // bound of the Jacobian of the phase (periods per length) and of its magnitude
            Func<Vector3d, Vector3d> map = Grade != null ? Grade.Map : PhaseMap;
            if (map != null) {
                if (Grade != null) {
                    // the derived Jacobian bound holds within the transverse extent of the grade: the domain plus the grid pad (3.5 cubes:
                    // the grid reaches 3 cubes beyond the box, a shifted grid one more)
                    double ext = Grade.TransverseHalfExtent * (1 + 1e-12), pad = 3.5 * CellSize / Divisions;
                    for (int d = 1; d <= 2; ++d) {
                        int ax = (Grade.Axis + d) % 3;
                        if (Math.Abs(box.Min[ax]) + pad > ext || Math.Abs(box.Max[ax]) + pad > ext) {
                            Fail(TpmsStatus.InvalidOptions, "the domain and the grid pad (3.5 cubes) leave the transverse extent " + Grade.TransverseHalfExtent + " of the Grade (axis " + ax + ")"); return;
                        }
                    }
                }
                if (!SamplePhase(map, box, CellSize / Divisions, out double gradMax, out phaseMax)) {
                    Fail(TpmsStatus.InvalidOptions, "the phase map must be finite on the domain"); return;
                }
                MinLocalCellSize = gradMax > 0 ? 1 / gradMax : double.PositiveInfinity;
                if (!(gradMax > 0)) { Fail(TpmsStatus.InvalidOptions, "the phase map is constant on the domain"); return; }
                if (CellSize / Divisions > MinLocalCellSize / 4) {
                    Fail(TpmsStatus.InvalidOptions, "the grid does not resolve the smallest local period " + MinLocalCellSize + ": CellSize / Divisions must be at most a quarter of it (set CellSize to the smallest period, raise Divisions)"); return;
                }
                jac = Grade != null ? Grade.JacobianBound : 1.5 * gradMax;
                mapGradientRatio = Math.Min(1, gradMax / jac);   // the guard scales with the sampled over the bounded phase gradient too
            }
            double lw = map == null ? GradientBound(Type) * k : GradientBound(Type) * 2 * Math.PI * jac;       // Lipschitz bound of g in length units
            double blendTermSum = 0, wmax = 0;
            if (hybrid) {
                // grad h = w grad g_A + (1 - w) grad g_B + (g_A - c_A - g_B + c_B) grad w (section 9.2): the phase part is the larger type bound, the
                // weight part uses |g_X - c_X| <= TermSum(X) + |c_X|
                double phaseScale = map == null ? k : 2 * Math.PI * jac;
                blendTermSum = TermSum(Type) + Math.Abs(level) + TermSum(SecondType.Value) + Math.Abs(level2);
                wmax = Blend.GradientBound;
                lw = Math.Max(GradientBound(Type), GradientBound(SecondType.Value)) * phaseScale + (double.IsNaN(wmax) ? 0 : blendTermSum * wmax);
            }
            if (!(lw > 0 && lw < double.MaxValue / 4)) { Fail(TpmsStatus.InvalidOptions, "the Lipschitz bound of the field is not finite and positive (with a hybrid: the gradient bound of the Blend is too large to normalise the field)"); return; }
            LipschitzBoundUsed = lw;
            Func<Vector3d, double> levelAt = null;
            if (VolumeFractionAt != null) {
                Func<Vector3d, double> frac = VolumeFractionAt;
                Vector3d origin = Origin; Matrix3d rot = Rotation;
                levelAt = p => {
                    double fr = frac(origin + rot * p);
                    if (double.IsNaN(fr)) return double.NaN;
                    return LevelForFraction(table, fr);
                };
            } else {
                Level = level;
            }
            SecondLevel = level2;

            // ---- grid (in the lattice frame, anchored at multiples of the cube) ----
            double h = CellSize / Divisions;
            // The sliver guard in field units: SliverGuardCubeFraction of a cube times the ratio of the true gradient scale to the proven bound
            // Lw (the bound is loose for some types: Schwarz D and Neovius by about 4). The guard band is then the same fraction of the f
            // change per edge for every type, and the crossing distance of 2^-8 of an edge holds against the sampled maximum change per
            // edge. The ratio is a sampled estimate and only sizes the guard: the result is certified separately, and the enclosure pad
            // pays 2 Snap in field units whatever Snap is.
            double ratio = hybrid ? Math.Max(GradientRatio(Type), GradientRatio(SecondType.Value)) : GradientRatio(Type);   // a hybrid: the larger ratio (a larger guard only grows the solid)
            // a hybrid's Lw includes the weight term, so the phase part of the normalised gradient is only phaseLw / lw of the single type's: the guard is scaled by it, which
            // keeps the guard band the same fraction of the f change per edge (in field units Snap * Lw is then the single type's)
            double guardScale = hybrid ? Math.Max(GradientBound(Type), GradientBound(SecondType.Value)) * (map == null ? k : 2 * Math.PI * jac) / lw : 1;
            double snap = h * SliverGuardCubeFraction * Math.Max(ratio * mapGradientRatio, 1.0 / 16) * guardScale;
            var cells = new int[3];
            var kmin = new double[3];
            double nodes = 1;
            for (int d = 0; d < 3; ++d) {
                kmin[d] = Math.Floor(box.Min[d] / h) - 3;
                double span = Math.Ceiling((box.Max[d] - h * kmin[d]) / h) + 3;
                if (double.IsNaN(span) || span > int.MaxValue / 2) { Fail(TpmsStatus.TooLarge, "the grid does not fit"); return; }
                cells[d] = MathUtil.ToIntSaturating(span);
                nodes *= cells[d] + 1.0;
            }
            if (nodes > MaxCells) { Fail(TpmsStatus.TooLarge, "the grid has " + nodes.ToString("G6") + " nodes, MaxCells is " + MaxCells); return; }

            // ---- enclosure preconditions ----
            double isoValue = 0;
            double enclosureUnit = 0;
            if (Enclosure) {
                double maxCoord = 0;
                for (int d = 0; d < 3; ++d)
                    maxCoord = Math.Max(maxCoord, Math.Max(Math.Abs(h * (kmin[d] - 1)), Math.Abs(h * (kmin[d] + cells[d] + 2))));
                if (maxCoord <= 0) maxCoord = h;
                double s = MathUtil.IsFinite(maxCoord) ? g3.CoreMathPort.CoreMath.Ldexp(1.0, MathUtil.CeilLog2(maxCoord)) : maxCoord;   // #1612: the exact ceiling
                while (s < maxCoord) s *= 2;
                double unit = s * Math.Pow(2, -40);
                enclosureUnit = unit;
                if (s / CellSize > Math.Pow(2, 20)) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure: the domain extends over too many periods for the proven phase error bound (coordinate / CellSize > 2^20)");
                    return;
                }
                if (h > s || !IsMultiple(h, unit) || !IsMultiple(h * kmin[0], unit) || !IsMultiple(h * kmin[1], unit) || !IsMultiple(h * kmin[2], unit)) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure needs an exact dyadic grid: the cube size and the grid origin must be multiples of 2^-40 of the power of two above the largest coordinate (eg a dyadic CellSize / Divisions)");
                    return;
                }
                double u = Math.Pow(2, -53);
                // evaluation error of g in normalised units: terms (64 u per unit of term sum) / (Lw s); the phase error is 4 u
                // (with a Grade the phase term is 4 u |phase| / (J s) and assumes the map is evaluated to a relative error of 4 u)
                double eNorm = hybrid
                    ? EnclosureErrorBlend(Type, SecondType.Value, lw, s, map != null, phaseMax, jac, h, blendTermSum, wmax)
                    : EnclosureError(Type, lw, s, map != null, phaseMax, jac, h);
                if (!(eNorm <= Math.Pow(2, -30))) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure: the domain is too small against the period for the proven error bound");
                    return;
                }
                // + 2 Snap: the sliver guard moves node values by less than 2 Snap (one-sided, down), a sample error e in the sense of the
                // audit's rule "level above half diagonal + e + S E"; the pad pays for it although a one-sided error would need less
                isoValue = Math.Sqrt(3.0) * h / 2 + 2 * snap + s * (Math.Pow(2, -26) + 4 * eNorm);
                // the samples of the outermost nodes are at least 1.2 cubes above the box; they must stay above the level
                if (!(isoValue < 1.2 * h)) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure: the cube is too small against the coordinates for the proven level pad (reduce the number of cubes)");
                    return;
                }
            }

            IsoValueUsed = isoValue;
            var field = new FieldFunction {
                Type = Type, Style = Style, K = k, PhaseMap = map, Level = level, InvLipschitz = 1.0 / lw, LevelAt = levelAt, Iso = isoValue, Snap = snap,
                Hybrid = hybrid, Type2 = hybrid ? SecondType.Value : Type, Level2 = level2, Weight = hybrid ? Blend.Weight : null,
                UseBox = tree == null, Center = box.Center, Half = new Vector3d(box.Width / 2, box.Height / 2, box.Depth / 2),
                DomainTree = tree, DomainBounds = box
            };
            if (tree == null) {
                // the planes of the box as written: Center +- Half must reproduce Min and Max
                field.Center = new Vector3d((box.Min.x + box.Max.x) / 2, (box.Min.y + box.Max.y) / 2, (box.Min.z + box.Max.z) / 2);
                field.Half = new Vector3d((box.Max.x - box.Min.x) / 2, (box.Max.y - box.Min.y) / 2, (box.Max.z - box.Min.z) / 2);
            }

            // ---- contouring, with grid shifts when a sample lies on the level or a sliver appears ----
            // The first grid is moved by half a cube so that the planes of an aligned box fall between nodes.
            DMesh3 result = null;
            Vector3i gridCells = Vector3i.Zero;
            Vector2d vol = Vector2d.Zero;
            string last = "";
            string reason = "";
            for (int attempt = 0; attempt < GridShifts.Length; ++attempt) {
                if (CancelF()) { Fail(TpmsStatus.Cancelled, "cancelled"); return; }
                GridShiftsUsed = attempt;
                double shift = Enclosure ? DyadicGridShifts[attempt] : GridShifts[attempt];
                var min = new Vector3d(h * (kmin[0] - shift), h * (kmin[1] - shift), h * (kmin[2] - shift));
                if (Enclosure && !(IsMultiple(min.x, enclosureUnit) && IsMultiple(min.y, enclosureUnit) && IsMultiple(min.z, enclosureUnit))) {
                    last = "the shifted grid is not exactly representable"; result = null; continue;   // never an inexact grid under Enclosure (h*0.125 is a multiple of the unit only for a cube of at least 8 units, so for tiny cubes the later shifts are skipped: harmless, ContouringFailed)
                }
                // (int)((Max - Min) / h) + 1 = cells, independent of rounding: Max is half a cube above the last full cube
                if (RejectAttempt != null && RejectAttempt(attempt)) { last = "attempt rejected"; result = null; continue; }
                LastGridMin = min;
                var max = new Vector3d(min.x + h * (cells[0] - 0.5), min.y + h * (cells[1] - 0.5), min.z + h * (cells[2] - 0.5));
                try {
                    if (TwoLevelSheet) {
                        var two = new TwoLevelMarchingTetrahedra {
                            Field = new PartFunction(field.SheetValue), HalfWidthF = field.SheetHalfWidth, Domain = new PartFunction(field.DomainValue),
                            Bounds = new AxisAlignedBox3d(min, max), CellSize = h, SliverGuard = SliverGuardCubeFraction > 0 ? 1.0 / 256 : 0,
                            ParallelCompute = ParallelCompute, CancelF = CancelF, MaxGridNodes = Math.Max(MaxCells, 1)
                        };
                        two.Generate();
                        result = two.Mesh; gridCells = two.CellDimensions;
                    } else {
                        var mc = new MarchingCubes {
                            Implicit = field, Bounds = new AxisAlignedBox3d(min, max), CubeSize = h, IsoValue = isoValue,
                            RootMode = MarchingCubes.RootfindingModes.LinearEdges, ParallelCompute = ParallelCompute, CancelF = CancelF
                        };
                        mc.Generate();
                        result = mc.Mesh; gridCells = mc.CellDimensions;
                    }
                } catch (InvalidOperationException e) {
                    last = e.Message; result = null; continue;
                } catch (ArgumentException e) {
                    Fail(TpmsStatus.ContouringFailed, e.Message); return;
                }
                if (CancelF() || result == null) { Fail(TpmsStatus.Cancelled, "cancelled"); return; }
                if (field.WeightNaN) { Fail(TpmsStatus.ContouringFailed, "the blend weight returned NaN"); return; }
                if (field.LevelNaN) { Fail(TpmsStatus.ContouringFailed, "VolumeFractionAt returned NaN"); return; }
                if (result.TriangleCount == 0) { Fail(TpmsStatus.ContouringFailed, "the lattice is empty in this domain"); return; }
                // volume and area in the lattice frame (no cancellation from a far origin), then to world coordinates
                vol = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex);
                ToWorld(result);
                // ---- certificate of the result, on the returned (world) coordinates ----
                // #1569: the rounding of Origin + Rotation * v moves a vertex by up to about an ulp of its world coordinates. Far from the
                // origin (eg 1e13 with a cube of 1/32, an ulp of 2e-3) that collapses and crosses triangles of a mesh that was clean in the
                // lattice frame. No a-priori bound can rule this out cheaply: it would need the smallest separation of non-adjacent triangles,
                // which the exact self-intersection check does not compute. So the certificate runs once, after the transform, on the mesh that
                // is returned (the same cost as before: the lattice-frame check is not repeated). With the identity transform the coordinates
                // are the lattice coordinates (0 + v = v), and wherever both frames are clean the result is the same as before.
                reason = MeshRobustRepair.NotCleanReason(result, VerifySelfIntersections);
                // a node with a field value within rounding of the level makes a sliver triangle: another grid avoids it
                // (only when degenerate triangles are the sole reason; a combined reason is reported, not retried)
                if (reason == "degenerate triangles" && attempt + 1 < GridShifts.Length) { last = reason; result = null; continue; }
                break;
            }
            if (result == null) { Fail(TpmsStatus.ContouringFailed, "no grid shift gave a valid contouring: " + last); return; }
            CellsEvaluated = MathUtil.ToLongSaturating((gridCells.x + 1.0) * (gridCells.y + 1.0) * (gridCells.z + 1.0));
            if (reason != "") {
                NotCleanReason = reason;
                if (reason.Contains("self-intersection")) {
                    SelfIntersectionsFound = 1;   // the check stops at the first reason; the count is not available here
                    Fail(TpmsStatus.SelfIntersecting, "the result self-intersects: " + reason);
                } else
                    Fail(TpmsStatus.NotClean, "the result is not clean: " + reason);
                return;
            }

            RealizedVolumeFraction = vol.x / domainVolume;
            MeanWallThickness = vol.y > 0 ? 2 * vol.x / vol.y : 0;
            // #1596: a result that is not the lattice asked for is refused (NotResolved, no mesh); the realized values stay set
            string inv(double x) { return x.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
            double grown = Enclosure ? (box.Width + 2 * isoValue) * (box.Height + 2 * isoValue) * (box.Depth + 2 * isoValue) : 0;
            double filled = Enclosure ? vol.x / grown : 0;     // the part of the box grown by the margin that the enclosure fills (a box domain only)
            if (!AllowUnresolved && Enclosure && filled >= EnclosureRefusalFraction) {
                int need = MinDivisionsForEnclosure();
                Fail(TpmsStatus.NotResolved, "Enclosure: the result fills " + inv(filled) + " of the box grown by the margin (at least " + inv(EnclosureRefusalFraction)
                    + "; RealizedVolumeFraction " + inv(RealizedVolumeFraction) + "): the error margin of the enclosure is larger than the voids of the lattice, so the result is the solid box (enlarged by the margin), not a lattice; use a smaller CellSize / more Divisions"
                    + (need > 0 ? " (MinDivisionsForEnclosure: " + need + ")" : ""));
                return;
            }
            if (!AllowUnresolved && !Enclosure && !TwoLevelSheet && Style == TpmsStyle.Sheet && !hybrid && VolumeFractionAt == null
                && fraction > 0 && RealizedVolumeFraction < SheetRefusalRatio * fraction) {
                Fail(TpmsStatus.NotResolved, "Sheet: the realized volume fraction " + inv(RealizedVolumeFraction)
                    + " is less than " + inv(SheetRefusalRatio) + " of the requested " + inv(fraction)
                    + ": the wall is thinner than about two cubes (CellSize / Divisions) and marching cubes loses it; set TwoLevelSheet = true or raise Divisions");
                return;
            }
            Mesh = result;
            Status = TpmsStatus.Ok;
        }

        /// <summary>
        /// An estimate, without contouring, of the part of the box grown by the margin that an Enclosure with the given Divisions fills: the fraction of the infinite lattice
        /// below the enclosure level (the level of the volume fraction plus the margin sqrt(3) h / 2 + 2 Snap, times the Lipschitz bound). A result at or above
        /// <see cref="EnclosureRefusalFraction"/> is refused (NotResolved); RealizedVolumeFraction is this times the growth of the box by the margin. Approximate (the table of the level has 1/1024
        /// resolution, the float pad of the proof is left out, the box is cut from a periodic lattice); NaN where it does not apply (a hybrid, a graded cell
        /// size, VolumeFractionAt, DomainMesh, or invalid options).
        /// </summary>
        public double EnclosureFractionEstimate(int divisions)
        {
            if (SecondType.HasValue || PhaseMap != null || Grade != null || VolumeFractionAt != null || DomainMesh != null)
                return double.NaN;
            if (!(CellSize > 0) || double.IsInfinity(CellSize) || divisions < 4 || divisions > 512 || !Enum.IsDefined(typeof(TpmsType), Type) || !Enum.IsDefined(typeof(TpmsStyle), Style))
                return double.NaN;
            double fraction = WallThickness > 0 ? AreaRatio(Type) * (WallThickness / CellSize) : VolumeFraction;
            if (!(fraction > 0 && fraction < 1) || WallThickness > 0 && (Style != TpmsStyle.Sheet || fraction > 0.95))
                return double.NaN;
            AxisAlignedBox3d box = Box;
            double ex = box.Max.x - box.Min.x, ey = box.Max.y - box.Min.y, ez = box.Max.z - box.Min.z;
            if (!(ex > 0 && ey > 0 && ez > 0) || double.IsInfinity(ex) || double.IsInfinity(ey) || double.IsInfinity(ez))
                return double.NaN;
            double[] table = QuantileTable(Type, Style);
            double level = LevelForFraction(table, fraction);
            double lw = GradientBound(Type) * 2 * Math.PI / CellSize, h = CellSize / divisions;
            // the enclosure needs a dyadic cube (a multiple of 2^-40 of the power of two above the largest coordinate): at least the mantissa of h has 12 trailing zero bits
            if ((BitConverter.DoubleToInt64Bits(h) & 0xFFF) != 0)
                return double.NaN;
            double iso = Math.Sqrt(3.0) * h / 2 + 2 * h * SliverGuardCubeFraction * Math.Max(GradientRatio(Type), 1.0 / 16);
            return FractionBelow(table, level + iso * lw);
        }

        /// <summary>
        /// The smallest power of two Divisions (4 to 512; the enclosure needs a dyadic cube) whose <see cref="EnclosureFractionEstimate"/> is at most
        /// <see cref="EnclosureEstimateTarget"/>, so that an Enclosure keeps the voids of the lattice; 0 when none does or the estimate does not apply.
        /// An estimate, not a proof: a Divisions at or above it is not refused on the cases tested (see the guide), a lower one may still work.
        /// </summary>
        public int MinDivisionsForEnclosure()
        {
            for (int d = 4; d <= 512; d *= 2) {
                double e = EnclosureFractionEstimate(d);
                if (double.IsNaN(e)) return 0;
                if (e <= EnclosureEstimateTarget) return d;
            }
            return 0;
        }

        // the fraction of the table's values (the quantile function of the style value over one period) at or below c
        internal static double FractionBelow(double[] table, double c)
        {
            if (!(c > table[0])) return 0;
            if (c >= table[TableKnots]) return 1;
            int lo = 0, hi = TableKnots;
            while (hi - lo > 1) {
                int mid = (lo + hi) / 2;
                if (table[mid] <= c) lo = mid; else hi = mid;
            }
            double span = table[hi] - table[lo];
            double t = span > 0 ? (c - table[lo]) / span : 0;
            return (lo + t) / TableKnots;
        }

        // the lattice frame to world coordinates, in place: Origin + Rotation * v (Origin + v for the identity rotation)
        void ToWorld(DMesh3 mesh)
        {
            bool identity = Rotation.EpsilonEqual(Matrix3d.Identity, 0);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, identity ? Origin + v : Origin + Rotation * v);
            }
        }

        // ================= helpers =================

        // Samples the phase map on a regular set of points of the box: false if a value is not finite, else the largest sampled
        // column norm of the Jacobian (central differences over an eighth of a cube) and the largest sampled phase magnitude.
        static bool SamplePhase(Func<Vector3d, Vector3d> map, AxisAlignedBox3d box, double h, out double gradMax, out double phaseMax)
        {
            gradMax = 0; phaseMax = 0;
            double e = h / 8;
            const int n = 9;
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    for (int l = 0; l < n; ++l) {
                        var p = new Vector3d(box.Min.x + (box.Max.x - box.Min.x) * i / (n - 1), box.Min.y + (box.Max.y - box.Min.y) * j / (n - 1),
                                             box.Min.z + (box.Max.z - box.Min.z) * l / (n - 1));
                        Vector3d v = map(p);
                        if (!IsFinite(v)) return false;
                        phaseMax = Math.Max(phaseMax, Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))));
                        for (int axis = 0; axis < 3; ++axis) {
                            Vector3d a = p, b = p;
                            a[axis] -= e; b[axis] += e;
                            Vector3d va = map(a), vb = map(b);
                            if (!IsFinite(va) || !IsFinite(vb)) return false;
                            gradMax = Math.Max(gradMax, (vb - va).Length / (2 * e));
                        }
                    }
            return true;
        }

        /// <summary>
        /// A graded cell size whose period varies linearly along one axis: the period is period0 at z0 and period1 at z1 (constant
        /// outside), the phase along the axis is the exact integral of 1 / period, the transverse phase is the transverse coordinate over
        /// the local period (so the cells are stretched with the period, distorted by the slope times the transverse distance from the
        /// frame origin: centre the box on the axis for the smallest distortion). The Jacobian bound is derived here (the Frobenius, hence
        /// operator, norm bound over the transverse half extent: the largest |coordinate| of the domain in the two other axes) and
        /// checked by the library against the domain. Set CellSize to the smaller period.
        /// </summary>
        public static TpmsPhaseGrade LinearGrade(int axis, double z0, double period0, double z1, double period1, double transverseHalfExtent)
        {
            if (axis < 0 || axis > 2) throw new ArgumentOutOfRangeException(nameof(axis));
            if (!(period0 > 0) || !(period1 > 0) || double.IsInfinity(period0) || double.IsInfinity(period1)) throw new ArgumentException("the periods must be finite and positive");
            if (!(z1 > z0) || double.IsInfinity(z1) || double.IsInfinity(z0)) throw new ArgumentException("z1 must be above z0");
            if (!(transverseHalfExtent >= 0) || double.IsInfinity(transverseHalfExtent)) throw new ArgumentException("the transverse extent must be finite and not negative");
            double slope = (period1 - period0) / (z1 - z0);
            double minPeriod = Math.Min(period0, period1);
            double bound = Math.Sqrt(3 / (minPeriod * minPeriod) + 2 * Math.Pow(transverseHalfExtent * Math.Abs(slope) / (minPeriod * minPeriod), 2));
            Func<double, double> period = z => period0 + (Math.Min(Math.Max(z, z0), z1) - z0) * slope;
            Func<double, double> phase = z => {
                if (z <= z0) return (z - z0) / period0;
                double top = Math.Min(z, z1);
                // the integral of 1 / period: (top - z0) / period0 * log(1 + x) / x with x = slope (top - z0) / period0, accurate to a
                // few ulps for every x (log1p by the u = 1 + x trick), so the phase has a relative error of a few u, also for gentle grades
                double len = top - z0, x = slope * len / period0;
                double inside = len / period0 * Log1pOverX(x);
                return inside + (z > z1 ? (z - z1) / period1 : 0);
            };
            Func<Vector3d, Vector3d> map = p => {
                double z = p[axis], l = period(z);
                var v = new Vector3d();
                v[axis] = phase(z);
                v[(axis + 1) % 3] = p[(axis + 1) % 3] / l;
                v[(axis + 2) % 3] = p[(axis + 2) % 3] / l;
                return v;
            };
            return new TpmsPhaseGrade(map, bound, axis, transverseHalfExtent);
        }

        // The evaluation error of the normalised field in the Enclosure proof, in units of s: the terms of f (64 u per unit of term sum, over
        // Lw s) plus the phase rounding: 4 u for the constant lattice (phase = x / L), and for a graded map 8 u |phase| / (J s) with the phase
        // magnitude taken as twice the sampled maximum plus J times the grid pad (the grid reaches 3.5 cubes beyond the box). The 8 u is the
        // assumption on the map (LinearGrade evaluates to a few ulps, see Log1pOverX); a PhaseMap is not allowed with Enclosure.
        internal static double EnclosureError(TpmsType type, double lw, double s, bool graded, double phaseMax, double jac, double h)
        {
            double u = Math.Pow(2, -53);
            double phase = graded ? 8 * u * (2 * phaseMax + 4 * jac * h) / (jac * s) : 4 * u;
            return 64 * u * TermSum(type) / (lw * s) + phase;
        }

        // The evaluation error of the hybrid h = w (g_A - c_A) + (1 - w) (g_B - c_B) in normalised units (divided by Lw s), with msum >= |g_A - c_A| + |g_B - c_B|
        // and u = 2^-53:
        //  - the type terms: the weights sum to 1, so the evaluation errors of f_A and f_B enter with at most the larger of 64 u TermSum (as for one type);
        //  - the rounding steps of the combination, each at most u times a value of at most msum: g_A - c_A, g_B - c_B, w * (.), 1 - w (an error of at most u in
        //    the weight, times |g_B - c_B|), (1 - w) * (.), and the sum: 6 u msum;
        //  - the error of the weight dw times |g_A - c_A - g_B + c_B| <= msum: dw <= 8 u + 2 u s Wmax. For SigmoidBlend, w = 0.5 (1 + tanh z) with z = 2 eta / width: eta = p - offset has a
        //    relative error u, the division another, so z has a relative error of at most 2 u (also for |offset| > s); with sech^2(z) |z| <= 0.45 the error of tanh
        //    propagated from z is at most 0.9 u, a tanh within 2 ulp adds 2 u, the sum 1 + tanh and the product by 0.5 (exact) add u: dw <= 0.5 (0.9 u + 2 u + 2 u) = 2.5 u < 8 u. A
        //    delegate weight is covered if its coordinate rounding is at most 2 u s (the second term) and its own evaluation error at most 8 u;
        //  - the phase as in EnclosureError.
        internal static double EnclosureErrorBlend(TpmsType a, TpmsType b, double lw, double s, bool graded, double phaseMax, double jac, double h, double msum, double wmax)
        {
            double u = Math.Pow(2, -53);
            double phase = graded ? 8 * u * (2 * phaseMax + 4 * jac * h) / (jac * s) : 4 * u;
            double epsW = 8 * u + 2 * u * s * wmax;
            return (64 * u * Math.Max(TermSum(a), TermSum(b)) + (6 * u + epsW) * msum) / (lw * s) + phase;
        }

        /// <summary>
        /// The sigmoid weight of the in-plane hybrid design of Fang et al. (Sci. Rep. 16, 17947, 2026, eq. 6), for a single plane: w = 1 / (1 + exp(-k eta)) with eta the signed
        /// distance to the plane {p[axis] = offset} in lattice-frame length units (positive on the side that selects <see cref="TpmsLattice.Type"/>) and
        /// k = 4 / width, so that the slope at the plane is 1 / width and the 10 % to 90 % zone is about 1.1 width wide. The library derives the gradient bound 1 / width.
        /// Evaluated as 0.5 (1 + tanh(k eta / 2)), which does not overflow. A power-of-two scale of offset and width gives the same weights bit for bit.
        /// </summary>
        public static TpmsBlendWeight SigmoidBlend(int axis, double offset, double width)
        {
            if (axis < 0 || axis > 2) throw new ArgumentOutOfRangeException(nameof(axis));
            if (double.IsNaN(offset) || double.IsInfinity(offset)) throw new ArgumentException("the offset must be finite");
            if (!(width > 0) || double.IsInfinity(width)) throw new ArgumentException("the width must be finite and positive");
            return new TpmsBlendWeight(p => 0.5 * (1 + Math.Tanh(2 * (p[axis] - offset) / width)), 1 / width);
        }

        // log(1 + x) / x to a few ulps for all x > -1 (x == 0 gives 1): log1p by the trick log(u) x / (u - 1) with u = 1 + x
        internal static double Log1pOverX(double x)
        {
            if (x == 0) return 1;
            double u = 1 + x;
            if (u == 1) return 1 - x / 2;          // |x| below 2^-53: the series is exact to rounding
            return Math.Log(u) / (u - 1);
        }

        static bool IsFinite(Vector3d v)
        {
            return !(double.IsNaN(v.x) || double.IsInfinity(v.x) || double.IsNaN(v.y) || double.IsInfinity(v.y) || double.IsNaN(v.z) || double.IsInfinity(v.z));
        }

        static bool IsRotation(Matrix3d m)
        {
            for (int i = 0; i < 9; ++i)
                if (double.IsNaN(m[i / 3, i % 3]) || double.IsInfinity(m[i / 3, i % 3])) return false;
            Matrix3d p = m * m.Transpose();
            return p.EpsilonEqual(Matrix3d.Identity, 1e-9) && Math.Abs(m.Determinant - 1) < 1e-9;
        }

        static bool IsMultiple(double x, double unit)
        {
            double q = x / unit;
            return q == Math.Floor(q) && Math.Abs(q) < 9007199254740992.0;
        }
    }
}
