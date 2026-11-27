// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Concurrent;
using g3.CoreMathPort;

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

    // The field part of TpmsLattice: the nodal approximations, the node tables, the level tables, the field, the grade and the blend.
    // In the determinism proof scope (DeterministicApis.txt, docs/design/DETERMINISM_PROOF.md); the contouring and the certificate are in TpmsLattice.cs.
    public partial class TpmsLattice
    {
        const int TableSamples = 64;
        const int TableKnots = 1024;
        const double MinFraction = 0.001, MaxFraction = 0.999;

        // ================= the nodal approximations =================

        /// <summary>
        /// f(p) for the phase p = 2 pi x / L (radians). The same bits on every runtime (#1577): the sines and cosines are the correctly rounded
        /// CoreMath ones, not the platform libm (Math.Sin / Math.Cos give other last bits in WebAssembly and on Windows, #1590), and the rest is
        /// + - * of doubles in a fixed order.
        /// </summary>
        public static double Evaluate(TpmsType type, double px, double py, double pz)
        {
            bool two = NeedsDoubleAngle(type);
            return EvaluateTrig(type, MakeTrig(px, two), MakeTrig(py, two), MakeTrig(pz, two));
        }

        // sin and cos of one phase, and of twice the phase for the types that use it
        internal struct AxisTrig { public double S, C, S2, C2; }

        static bool NeedsDoubleAngle(TpmsType type) { return type == TpmsType.IWP || type == TpmsType.Lidinoid; }

        static AxisTrig MakeTrig(double p, bool two)
        {
            var t = new AxisTrig { S = CoreMath.Sin(p), C = CoreMath.Cos(p) };
            if (two) { double p2 = 2 * p; t.S2 = CoreMath.Sin(p2); t.C2 = CoreMath.Cos(p2); }
            return t;
        }

        // f from the sines and cosines of the three phases: the same arithmetic as Evaluate, so a table of AxisTrig gives its bits
        static double EvaluateTrig(TpmsType type, in AxisTrig X, in AxisTrig Y, in AxisTrig Z)
        {
            double sx = X.S, cx = X.C, sy = Y.S, cy = Y.C, sz = Z.S, cz = Z.C;
            switch (type) {
                case TpmsType.Gyroid: return sx * cy + sy * cz + sz * cx;
                case TpmsType.SchwarzP: return cx + cy + cz;
                case TpmsType.SchwarzD: return sx * sy * sz + sx * cy * cz + cx * sy * cz + cx * cy * sz;
                case TpmsType.Neovius: return 3 * (cx + cy + cz) + 4 * cx * cy * cz;
                case TpmsType.IWP:
                    return 2 * (cx * cy + cy * cz + cz * cx) - (X.C2 + Y.C2 + Z.C2);
                case TpmsType.Lidinoid:
                    return 0.5 * (X.S2 * cy * sz + Y.S2 * cz * sx + Z.S2 * cx * sy)
                         - 0.5 * (X.C2 * Y.C2 + Y.C2 * Z.C2 + Z.C2 * X.C2)
                         + 0.15;
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary> Test hook: with true the grid nodes of Compute and the sampling tables use the direct Evaluate (the result is the same bit for bit; only slower). </summary>
        internal static bool DisableAxisTables;
        // Test hook: count the table lookups of the field and those that hit a node (a node-heavy contouring must hit almost always)
        // A graded lattice (PhaseMap) has no per-axis tables; its node values are kept instead (8 bytes a node, 32 MB at most, per grid attempt) up to this many nodes:
        // a node is evaluated by up to eight cells. A larger grid evaluates every node of every cell directly (the same bits, slower).
        const double NodeValueCacheMaxNodes = 1 << 22;
        // Test hooks (global statics, not thread safe to toggle while another thread computes a lattice; production code never sets them). DisableAxisTables: no
        // node tables and no node values (the direct CoreMath path); DisableNodeValues: no node values of a graded lattice; CountTableLookups: count the lookups below.
        internal static bool DisableNodeValues;
        internal static bool CountTableLookups;
        static long tableLookups, tableHits;
        internal static long TableLookups { get { return System.Threading.Interlocked.Read(ref tableLookups); } }
        internal static long TableHits { get { return System.Threading.Interlocked.Read(ref tableHits); } }

        // The sines and cosines at the nodes of a regular grid, per axis (the field is a function of three separable phases): node i of axis d is at
        // Min[d] + h * i, the coordinate that MarchingCubes computes. Find returns the entry only for a point that is exactly a node, so a point
        // anywhere else (an edge root, a user delegate) takes the direct path and the result never depends on the table.
        internal sealed class GridTrig
        {
            readonly double[][] coord = new double[3][];
            readonly AxisTrig[][] trig = new AxisTrig[3][];
            readonly double min0, min1, min2, h;
            readonly int n0, n1, n2;
            // the value of the field at each node (Raw, before the sliver guard), computed on the first request. Threads may compute the same node at the same
            // time: both store the same bits (the field is a pure function of p). Empty: bits of a quiet NaN that the field never returns
            const long Empty = 0x7FF8157700000001L;
            long[] values;

            internal GridTrig(Vector3d min, double h, int nx, int ny, int nz, double k, bool two, bool withTrig, bool withValues)
            {
                this.h = h; min0 = min.x; min1 = min.y; min2 = min.z; n0 = nx; n1 = ny; n2 = nz;
                int[] n = { nx, ny, nz };
                for (int d = 0; d < 3; ++d) {
                    coord[d] = new double[n[d]];
                    if (withTrig) trig[d] = new AxisTrig[n[d]];
                    for (int i = 0; i < n[d]; ++i) {
                        coord[d][i] = min[d] + h * i;
                        if (withTrig) trig[d][i] = MakeTrig(k * coord[d][i], two);
                    }
                }
                if (withValues) {
                    values = new long[(long)nx * ny * nz];
                    for (long i = 0; i < values.Length; ++i) values[i] = Empty;
                }
            }

            int find(int d, double min, double x)
            {
                double q = (x - min) / h + 0.5;
                if (q >= 0 && q < coord[d].Length) {
                    int i = (int)q;
                    if (coord[d][i] == x) return i;
                }
                return -1;
            }

            public bool Find(ref Vector3d p, out AxisTrig x, out AxisTrig y, out AxisTrig z)
            {
                x = y = z = default(AxisTrig);
                int i = find(0, min0, p.x), j = i >= 0 ? find(1, min1, p.y) : -1, k = j >= 0 ? find(2, min2, p.z) : -1;
                bool hit = k >= 0;
                if (hit) { x = trig[0][i]; y = trig[1][j]; z = trig[2][k]; }
                if (CountTableLookups) { System.Threading.Interlocked.Increment(ref tableLookups); if (hit) System.Threading.Interlocked.Increment(ref tableHits); }
                return hit;
            }

            // the node index of p, or -1
            public long Node(ref Vector3d p)
            {
                int i = find(0, min0, p.x); if (i < 0) return -1;
                int j = find(1, min1, p.y); if (j < 0) return -1;
                int k = find(2, min2, p.z); if (k < 0) return -1;
                return ((long)i * n1 + j) * n2 + k;
            }

            public bool HasValues { get { return values != null; } }

            // the bits are read and written as one long (atomic also on a 32-bit runtime, where a double store may not be)
            public bool TryGetValue(long node, out double v)
            {
                long bits = System.Threading.Volatile.Read(ref values[node]);
                v = BitConverter.Int64BitsToDouble(bits);
                return bits != Empty;
            }

            public void SetValue(long node, double v) { System.Threading.Volatile.Write(ref values[node], BitConverter.DoubleToInt64Bits(v)); }
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
                bool two = NeedsDoubleAngle(t);
                // the phases of one axis: x, x + e and x - e for the 24 samples (the same on the three axes up to the offset of the sample)
                AxisTrig[][] c0 = new AxisTrig[3][], cp = new AxisTrig[3][], cm = new AxisTrig[3][];
                double[] off = { 0.37, 0.61, 0.23 };
                for (int d = 0; d < 3; ++d) {
                    c0[d] = new AxisTrig[n]; cp[d] = new AxisTrig[n]; cm[d] = new AxisTrig[n];
                    for (int i = 0; i < n; ++i) {
                        double x = (i + off[d]) * step;
                        c0[d][i] = MakeTrig(x, two); cp[d][i] = MakeTrig(x + e, two); cm[d][i] = MakeTrig(x - e, two);
                    }
                }
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j)
                        for (int l = 0; l < n; ++l) {
                            double gx = (EvaluateTrig(t, cp[0][i], c0[1][j], c0[2][l]) - EvaluateTrig(t, cm[0][i], c0[1][j], c0[2][l])) / (2 * e);
                            double gy = (EvaluateTrig(t, c0[0][i], cp[1][j], c0[2][l]) - EvaluateTrig(t, c0[0][i], cm[1][j], c0[2][l])) / (2 * e);
                            double gz = (EvaluateTrig(t, c0[0][i], c0[1][j], cp[2][l]) - EvaluateTrig(t, c0[0][i], c0[1][j], cm[2][l])) / (2 * e);
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
            if (DisableAxisTables) {
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j)
                        for (int k = 0; k < n; ++k)
                            values[idx++] = StyleValue(style, Evaluate(type, (i + 0.5) * step, (j + 0.5) * step, (k + 0.5) * step));
            } else {
                bool two = NeedsDoubleAngle(type);
                var axis = new AxisTrig[n];
                for (int i = 0; i < n; ++i) axis[i] = MakeTrig((i + 0.5) * step, two);
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j)
                        for (int k = 0; k < n; ++k)
                            values[idx++] = StyleValue(style, EvaluateTrig(type, axis[i], axis[j], axis[k]));
            }
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
            public GridTrig Grid;             // the sines and cosines at the nodes of the current grid (PhaseMap == null): see GridTrig

            public double Value(ref Vector3d p)
            {
                double v;
                if (Grid != null && Grid.HasValues) {
                    // a node is evaluated by up to eight cells: the value of the field there is computed once (the same bits as every time)
                    long node = Grid.Node(ref p);
                    if (node >= 0) {
                        if (!Grid.TryGetValue(node, out v)) { v = Raw(ref p); Grid.SetValue(node, v); }
                    } else v = Raw(ref p);
                } else
                    v = Raw(ref p);
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
                AxisTrig tx, ty, tz;
                bool hit = Lookup(ref p, out tx, out ty, out tz);
                return Eval(Type, hit, tx, ty, tz, px, py, pz) * InvLipschitz;
            }

            // f at the phases (px, py, pz) of the point p: from the node table x, y, z when p is a grid node (hit), else directly (the same bits)
            static double Eval(TpmsType type, bool hit, in AxisTrig x, in AxisTrig y, in AxisTrig z, double px, double py, double pz)
            {
                return hit ? EvaluateTrig(type, x, y, z) : Evaluate(type, px, py, pz);
            }

            bool Lookup(ref Vector3d p, out AxisTrig x, out AxisTrig y, out AxisTrig z)
            {
                x = y = z = default(AxisTrig);
                return Grid != null && PhaseMap == null && Grid.Find(ref p, out x, out y, out z);
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
                AxisTrig tx, ty, tz;
                bool hit = Lookup(ref p, out tx, out ty, out tz);
                double g;
                if (Hybrid) {
                    // h = w (g_A - c_A) + (1 - w) (g_B - c_B), w clamped to [0, 1] (a clamp keeps the Lipschitz bound; NaN stays NaN)
                    double w0 = Weight(p);
                    if (double.IsNaN(w0)) WeightNaN = true;
                    double w = Math.Min(1.0, Math.Max(0.0, w0));
                    double ga = StyleValue(Style, Eval(Type, hit, tx, ty, tz, px, py, pz)) - Level;
                    double gb = StyleValue(Style, Eval(Type2, hit, tx, ty, tz, px, py, pz)) - Level2;
                    g = (w * ga + (1 - w) * gb) * InvLipschitz;
                } else {
                    double f = Eval(Type, hit, tx, ty, tz, px, py, pz);
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
    }
}
