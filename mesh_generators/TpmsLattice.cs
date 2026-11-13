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

    /// <summary> Outcome of <see cref="TpmsLattice.Compute"/>. Mesh is set only for Ok. </summary>
    public enum TpmsStatus { Ok, InvalidOptions, InvalidDomain, TooLarge, ContouringFailed, NotClean, SelfIntersecting, Cancelled }

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
    /// about two cubes (CellSize / Divisions).
    ///
    /// The lattice lives in a local frame (Origin, Rotation): phase zero at the origin, the Box in local coordinates,
    /// the grid anchored at multiples of the cube size. The result is mapped to world coordinates at the end, so the
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
        /// <summary> Lattice frame: the world position of the local origin (lattice phase zero). </summary>
        public Vector3d Origin = Vector3d.Zero;
        /// <summary> Lattice frame: local to world rotation (orthonormal, determinant +1). </summary>
        public Matrix3d Rotation = Matrix3d.Identity;
        /// <summary> Domain in local coordinates (used if DomainMesh is null). </summary>
        public AxisAlignedBox3d Box = new AxisAlignedBox3d(Vector3d.Zero, 1);
        /// <summary> Domain as the inside of a closed manifold mesh in world coordinates (wins over Box). Not with Enclosure. </summary>
        public DMesh3 DomainMesh;
        /// <summary> Run the exact self-intersection check on the result (cost O(T log T), see the benchmark). </summary>
        public bool VerifySelfIntersections = true;
        /// <summary> The solid contains {F &lt;= 0} of the exact lattice (box domain, constant fraction, dyadic grid only). </summary>
        public bool Enclosure = false;
        /// <summary> Slab-parallel field evaluation (id-identical to serial). </summary>
        public bool ParallelCompute = true;
        /// <summary> Upper limit of grid nodes, checked before any allocation. </summary>
        public long MaxCells = 1L << 28;
        /// <summary> If it returns true, Compute stops with Status Cancelled. </summary>
        public Func<bool> CancelF = () => false;

        // ---- outputs ----
        public TpmsStatus Status;
        public string Message = "";
        /// <summary> The lattice mesh in world coordinates; null unless Status == Ok. </summary>
        public DMesh3 Mesh;
        /// <summary> The level c (or t) for the target fraction (not set with grading). </summary>
        public double Level;
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
        // test hooks: reject a grid attempt as if the contouring had failed; the lowest node of the grid last used
        internal Func<int, bool> RejectAttempt;
        internal Vector3d LastGridMin;
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
                int lo = (int)pos;
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
            int lo = Math.Min((int)pos, TableKnots - 1);
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

        // ================= the field =================

        sealed class FieldFunction : ImplicitFunction3d
        {
            public TpmsType Type; public TpmsStyle Style;
            public double K;                  // 2 pi / L
            public double Level;              // c (constant fraction)
            public double InvLipschitz;       // 1 / Lw
            public bool UseBox; public Vector3d Center, Half;
            public Func<Vector3d, double> LevelAt;    // grading: local position -> level
            public DMeshAABBTree3 DomainTree; public AxisAlignedBox3d DomainBounds;

            public double Value(ref Vector3d p)
            {
                double f = Evaluate(Type, K * p.x, K * p.y, K * p.z);
                double c = LevelAt != null ? LevelAt(p) : Level;
                double g = (StyleValue(Style, f) - c) * InvLipschitz;
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

            // signed distance to the closed domain mesh (negative inside); outside the bounds a lower bound (the box distance)
            double DomainDistance(Vector3d p)
            {
                double ox = Math.Max(Math.Max(DomainBounds.Min.x - p.x, p.x - DomainBounds.Max.x), 0);
                double oy = Math.Max(Math.Max(DomainBounds.Min.y - p.y, p.y - DomainBounds.Max.y), 0);
                double oz = Math.Max(Math.Max(DomainBounds.Min.z - p.z, p.z - DomainBounds.Max.z), 0);
                if (ox > 0 || oy > 0 || oz > 0) return Math.Sqrt(ox * ox + oy * oy + oz * oz);
                double dist = double.MaxValue;
                int tid = DomainTree.FindNearestTriangle(p, out double d2);
                if (tid >= 0) dist = Math.Sqrt(d2);
                bool inside = DomainTree.FastWindingNumber(p) > 0.5;
                return inside ? -dist : dist;
            }
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
            SelfIntersectionsFound = 0; CellsEvaluated = 0; GridShiftsUsed = 0; IsoValueUsed = 0;
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
            if (Enclosure && (VolumeFractionAt != null || DomainMesh != null)) { Fail(TpmsStatus.InvalidOptions, "Enclosure needs a box domain and a constant volume fraction"); return; }
            if (MaxCells < 1) { Fail(TpmsStatus.InvalidOptions, "MaxCells must be positive"); return; }

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
                    local.SetVertex(vid, toLocal * (DomainMesh.GetVertex(vid) - Origin));
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
            double k = 2 * Math.PI / CellSize;
            double lw = GradientBound(Type) * k;       // Lipschitz bound of g in length units
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

            // ---- grid (in the lattice frame, anchored at multiples of the cube) ----
            double h = CellSize / Divisions;
            var cells = new int[3];
            var kmin = new double[3];
            double nodes = 1;
            for (int d = 0; d < 3; ++d) {
                kmin[d] = Math.Floor(box.Min[d] / h) - 3;
                double span = Math.Ceiling((box.Max[d] - h * kmin[d]) / h) + 3;
                if (double.IsNaN(span) || span > int.MaxValue / 2) { Fail(TpmsStatus.TooLarge, "the grid does not fit"); return; }
                cells[d] = (int)span;
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
                double s = Math.Pow(2, Math.Ceiling(Math.Log(maxCoord, 2)));
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
                double eNorm = 64 * u * TermSum(Type) / (lw * s) + 4 * u;
                if (!(eNorm <= Math.Pow(2, -30))) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure: the domain is too small against the period for the proven error bound");
                    return;
                }
                isoValue = Math.Sqrt(3.0) * h / 2 + s * (Math.Pow(2, -26) + 4 * eNorm);
                // the samples of the outermost nodes are at least 1.2 cubes above the box; they must stay above the level
                if (!(isoValue < 1.2 * h)) {
                    Fail(TpmsStatus.InvalidOptions, "Enclosure: the cube is too small against the coordinates for the proven level pad (reduce the number of cubes)");
                    return;
                }
            }

            IsoValueUsed = isoValue;
            var field = new FieldFunction {
                Type = Type, Style = Style, K = k, Level = level, InvLipschitz = 1.0 / lw, LevelAt = levelAt,
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
            MarchingCubes mc = null;
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
                mc = new MarchingCubes {
                    Implicit = field, Bounds = new AxisAlignedBox3d(min, max), CubeSize = h, IsoValue = isoValue,
                    RootMode = MarchingCubes.RootfindingModes.LinearEdges, ParallelCompute = ParallelCompute, CancelF = CancelF
                };
                try {
                    mc.Generate();
                } catch (InvalidOperationException e) {
                    last = e.Message; result = null; continue;
                } catch (ArgumentException e) {
                    Fail(TpmsStatus.ContouringFailed, e.Message); return;
                }
                if (CancelF()) { Fail(TpmsStatus.Cancelled, "cancelled"); return; }
                result = mc.Mesh;
                if (result.TriangleCount == 0) { Fail(TpmsStatus.ContouringFailed, "the lattice is empty in this domain"); return; }
                // ---- certificate of the result ----
                reason = MeshRobustRepair.NotCleanReason(result, VerifySelfIntersections);
                // a node with a field value within rounding of the level makes a sliver triangle: another grid avoids it
                // (only when degenerate triangles are the sole reason; a combined reason is reported, not retried)
                if (reason == "degenerate triangles" && attempt + 1 < GridShifts.Length) { last = reason; result = null; continue; }
                break;
            }
            if (result == null) { Fail(TpmsStatus.ContouringFailed, "no grid shift gave a valid contouring: " + last); return; }
            CellsEvaluated = (long)((mc.CellDimensions.x + 1.0) * (mc.CellDimensions.y + 1.0) * (mc.CellDimensions.z + 1.0));
            if (reason != "") {
                NotCleanReason = reason;
                if (reason.Contains("self-intersection")) {
                    SelfIntersectionsFound = 1;   // the check stops at the first reason; the count is not available here
                    Fail(TpmsStatus.SelfIntersecting, "the result self-intersects: " + reason);
                } else
                    Fail(TpmsStatus.NotClean, "the result is not clean: " + reason);
                return;
            }

            // volume and area in the lattice frame (no cancellation from a far origin), then to world coordinates
            Vector2d vol = MeshMeasurements.VolumeArea(result, result.TriangleIndices(), result.GetVertex);
            RealizedVolumeFraction = vol.x / domainVolume;
            MeanWallThickness = vol.y > 0 ? 2 * vol.x / vol.y : 0;
            bool identity = Rotation.EpsilonEqual(Matrix3d.Identity, 0);
            foreach (int vid in result.VertexIndices()) {
                Vector3d v = result.GetVertex(vid);
                result.SetVertex(vid, identity ? Origin + v : Origin + Rotation * v);
            }
            Mesh = result;
            Status = TpmsStatus.Ok;
        }

        // ================= helpers =================

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
