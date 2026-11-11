using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Normals of an unoriented point cloud (docs/design/POINT_CLOUD_RECONSTRUCTION.md, step B): the normal axis of every
    /// point from a PCA of its nearest neighbours (the tangent plane of Hoppe et al. 1992), and the sign from an alpha wrap
    /// of the cloud: of the two probe points p + d n and p - d n the one outside the wrap is on the outer side, and a point
    /// whose probes do not tell them apart (both outside: a thin wall or an open sheet; both inside: a point buried
    /// in the wrap) is reported, not guessed. The wrap is closed and free of self-intersections (MeshAlphaWrap), so the
    /// inside test is exact (the parity of the crossings of a ray).
    ///
    /// Alpha is chosen from the cloud when it is not given: the larger of SpacingFactor (2.5) times the estimated sampling
    /// spacing and NoiseFactor (6.5) times the estimated noise, which is about 4 noise sigmas (measured on procedural torus samplings: orientation was right
    /// for 97-100 % of the points above this, 56-93 % below). A given Alpha below that condition is not changed; the result
    /// says so (Status AlphaBelowCondition, AlphaConditionMet). Spacing is the median distance to the 6th nearest neighbour
    /// divided by sqrt(6/pi) (the value for a uniform sampling of a surface); NoiseSigma is the square root of the median of
    /// the smallest eigenvalues of the neighbourhood covariances (sum of squares about the fitted plane over k - 3), so it also
    /// contains the curvature and the sampling gaps. It underestimates the true noise: about 0.6 of it on a noisy sphere (sigma 0.02-0.05,
    /// k = 16), because the fitted plane absorbs part of the noise; NoiseFactor makes up for that.
    ///
    /// A hole or a gap in the sampling larger than Alpha lets the wrap into the solid, so that both probes of every point are
    /// outside. If Alpha was chosen by the class and fewer than MinConfidentFraction of the points are confident, the wrap is
    /// redone with a larger Alpha (EscalationFactor, up to MaxEscalations times): orientation needs no detail. Measured on the
    /// torus samplings: without it 0 % confident (and 91-94 % right by the nearest wrap point alone) with a removed patch or a
    /// 10:1 density ratio, with it 99-100 % confident and right.
    ///
    /// Exact duplicates are merged before everything else (repeated scanner samples); their normals are those of the point they
    /// repeat. The neighbourhoods are found in a frame scaled by a power of two to extent 1, so coordinates of any
    /// finite magnitude work for the axes (the wrap itself has its own supported range, see MeshAlphaWrap); a finite cloud
    /// whose extent overflows, or is below 2.3e-308, is InvalidInput. The neighbours come from a k-d tree: exact, O(n log n),
    /// not sensitive to clusters or outliers.
    ///
    /// A wrap that reaches MaxSteinerPoints (WrapStatus PointLimitReached) is used only if it is closed, valid, without bowtie vertices and without
    /// self-intersections; otherwise a coarser Alpha is tried (Alpha chosen by the class, up to MaxEscalations times), and then the status is WrapFailed. DegenerateCount tells how many
    /// points have a zero normal also when the run is a Success.
    ///
    /// Limits: a surface that is not the boundary of a solid has no outside (an open sheet, a wall thinner than about
    /// 2 * (Alpha + Offset) * ProbeFactor); its points are reported as not confident. Holes larger than Alpha that the wrap enters
    /// make the points near them not confident. Normals of points whose neighbourhood is degenerate (coincident or collinear
    /// points) are zero.
    ///
    /// Invariance: the result is the same, scaled and rotated, for a similarity of the input (bit for bit for a power of two
    /// scale). With Orient false the sign is arbitrary (the largest component is positive), which depends on the rotation.
    /// Parallel and serial runs give the same result. Compute() can be called again.
    /// </summary>
    public class PointCloudNormals
    {
        public enum NormalsStatus
        {
            /// <summary> axes oriented, the alpha condition met, at least MinConfidentFraction of the points confident (or Orient false) </summary>
            Success = 0,
            /// <summary> a point is not finite, or Points is null </summary>
            InvalidInput = 1,
            /// <summary> fewer than Neighbors + 1 points </summary>
            TooFewPoints = 2,
            /// <summary> the wrap did not succeed (see WrapStatus); the axes are set, the signs are not reliable </summary>
            WrapFailed = 3,
            /// <summary> a given Alpha is below the condition (SpacingFactor * Spacing, NoiseFactor * NoiseSigma): the signs are a heuristic with a lower success rate </summary>
            AlphaBelowCondition = 4,
            /// <summary> fewer than MinConfidentFraction of the points have probes that tell the sides apart (an open sheet, thin walls, holes) </summary>
            AmbiguousOrientation = 5,
            /// <summary> fewer than MinConfidentFraction of the points have a normal axis (collinear or coincident neighbourhoods, eg a line, or a few points repeated); the axes that exist are set </summary>
            DegenerateInput = 6
        }

        /// <summary> the input points (not changed) </summary>
        public IList<Vector3d> Points;

        /// <summary> number of neighbours of the PCA, including the point itself; clamped to [8, 256] (default 16) </summary>
        public int Neighbors = 16;
        /// <summary> orient the axes with the wrap (default); false: axes only, no wrap </summary>
        public bool Orient = true;
        /// <summary> the ball radius of the wrap; not positive: chosen from the cloud (see the class comment) </summary>
        public double Alpha = 0;
        /// <summary> the wrap's offset; not positive: Alpha / OffsetDivisor </summary>
        public double Offset = 0;
        public double OffsetDivisor = 30;
        public double SpacingFactor = 2.5;
        /// <summary> NoiseSigma is about 0.6 of the true noise (measured on a noisy sphere, sigma 0.02-0.05, k = 16): 6.5 times it is about 4 sigma </summary>
        public double NoiseFactor = 6.5;
        /// <summary> probe distance in units of (Alpha + Offset); the outer probe must lie beyond the wrap, the inner one inside the solid </summary>
        public double ProbeFactor = 1.5;
        /// <summary> Success needs at least this fraction of confident points </summary>
        public double MinConfidentFraction = 0.9;
        /// <summary>
        /// Alpha chosen by the class (Alpha not positive): if fewer than MinConfidentFraction of the points are confident (a
        /// hole or a gap in the sampling larger than Alpha lets the wrap into the solid, so that both probes are outside),
        /// the wrap is redone with Alpha times EscalationFactor, at most this many times. Orientation needs no detail, so a
        /// coarser wrap is fine. 0 = never. A given Alpha is never changed.
        /// </summary>
        public int MaxEscalations = 3;
        /// <summary> stop refining the wrap after this many Steiner points (MeshAlphaWrap.MaxSteinerPoints); a wrap that reaches it is used if it is clean (WrapStatus PointLimitReached tells) </summary>
        public int MaxSteinerPoints = 2000000;
        public double EscalationFactor = 1.6;
        public bool Parallel = true;

        // results
        public NormalsStatus Status;
        /// <summary> unit normals, or zero for a point with a degenerate neighbourhood; set also if Status is not Success, except for InvalidInput and TooFewPoints (null) </summary>
        public Vector3d[] Normals;
        /// <summary> the sign of Normals[i] is certain by the probes (false: ambiguous or degenerate) </summary>
        public bool[] Confident;
        public int ConfidentCount;
        /// <summary> number of points (duplicates counted) that have a normal axis (the neighbourhood is not collinear or coincident) </summary>
        public int AxisCount;
        /// <summary>
        /// the area that each input point stands for, in the units of the input: pi * d^2 / (j + 1), d the distance to its j-th
        /// nearest neighbour (j = 8, or the largest the neighbour count allows); the area weight of the fast winding number of
        /// an oriented point cloud. Set together with Normals (also for the failure statuses that set them); null otherwise.
        /// </summary>
        public double[] PointArea;
        /// <summary> number of points (duplicates counted) without a normal axis, whose normal is zero; also set if Status is Success (a few degenerate neighbourhoods do not fail the run) </summary>
        public int DegenerateCount { get { return Normals == null ? 0 : Normals.Length - AxisCount; } }
        public double ConfidentFraction { get { return Points == null || Points.Count == 0 ? 0 : (double)ConfidentCount / Points.Count; } }
        /// <summary> estimated sampling spacing and noise (upper bound), see the class comment </summary>
        public double Spacing, NoiseSigma;
        /// <summary> Alpha and Offset used, whether Alpha meets the condition </summary>
        public double UsedAlpha, UsedOffset;
        public bool AlphaConditionMet;
        /// <summary> how often Alpha was enlarged (MaxEscalations) </summary>
        public int Escalations;
        /// <summary> the wrap of the points (Orient true), for further use; null otherwise </summary>
        public DMesh3 Wrap;
        public MeshAlphaWrap.WrapStatus WrapStatus;
        public string Error = "";

        public PointCloudNormals(IList<Vector3d> points)
        {
            Points = points;
        }

        public bool Compute()
        {
            Normals = null; Confident = null; Wrap = null; ConfidentCount = 0; AxisCount = 0; PointArea = null; Error = "";
            Spacing = NoiseSigma = UsedAlpha = UsedOffset = 0; AlphaConditionMet = false; Escalations = 0;
            WrapStatus = MeshAlphaWrap.WrapStatus.Success;
            if (Points == null) { Status = NormalsStatus.InvalidInput; Error = "Points is null"; return false; }
            if (double.IsNaN(Alpha) || double.IsInfinity(Alpha) || double.IsNaN(Offset) || double.IsInfinity(Offset)) {
                Status = NormalsStatus.InvalidInput; Error = "Alpha or Offset is not finite"; return false;
            }
            int n = Points.Count;
            int k = Math.Min(256, Math.Max(8, Neighbors));
            for (int i = 0; i < n; ++i) {
                Vector3d p = Points[i];
                if (double.IsNaN(p.x + p.y + p.z) || double.IsInfinity(p.x) || double.IsInfinity(p.y) || double.IsInfinity(p.z)) {
                    Status = NormalsStatus.InvalidInput; Error = "point " + i + " is not finite"; return false;
                }
            }

            // merge exact duplicates: unique points U, map[i] = index in U
            int[] order = new int[n];
            for (int i = 0; i < n; ++i) order[i] = i;
            Vector3d[] all = new Vector3d[n];
            for (int i = 0; i < n; ++i) all[i] = Points[i];
            Array.Sort(order, (a, b) => {
                int c = all[a].x.CompareTo(all[b].x);
                if (c == 0) c = all[a].y.CompareTo(all[b].y);
                if (c == 0) c = all[a].z.CompareTo(all[b].z);
                return c != 0 ? c : a.CompareTo(b);
            });
            int[] map = new int[n];
            List<Vector3d> unique = new List<Vector3d>();
            for (int j = 0; j < n; ++j) {
                int i = order[j];
                if (j == 0 || all[i].x != all[order[j - 1]].x || all[i].y != all[order[j - 1]].y || all[i].z != all[order[j - 1]].z)
                    unique.Add(all[i]);
                map[i] = unique.Count - 1;
            }
            Vector3d[] pts = unique.ToArray();
            int m = pts.Length;
            if (m <= k) { Status = NormalsStatus.TooFewPoints; Error = m + " different points, need " + (k + 1); return false; }

            // the frame of the neighbourhoods: centred, scaled by a power of two to an extent in [1, 2)
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            foreach (Vector3d p in pts) box.Contain(p);
            double ex = box.Max.x - box.Min.x, ey = box.Max.y - box.Min.y, ez = box.Max.z - box.Min.z;
            double extent = Math.Max(ex, Math.Max(ey, ez));
            if (double.IsInfinity(extent) || double.IsNaN(extent)) { Status = NormalsStatus.InvalidInput; Error = "the extent of the points overflows"; return false; }
            if (!(extent >= 2.3e-308)) { Status = NormalsStatus.InvalidInput; Error = "the extent of the points is below the supported range"; return false; }
            int exponent = (int)((BitConverter.DoubleToInt64Bits(extent) >> 52) & 0x7FF) - 1023;
            double toUnit = PowerOfTwo(-exponent), fromUnit = PowerOfTwo(exponent);
            Vector3d center = new Vector3d(box.Min.x + 0.5 * ex, box.Min.y + 0.5 * ey, box.Min.z + 0.5 * ez);
            Vector3d[] unit = new Vector3d[m];
            for (int i = 0; i < m; ++i) unit[i] = (pts[i] - center) * toUnit;

            // neighbourhoods, PCA axes, spacing and noise
            PointTree tree = new PointTree(unit);
            Vector3d[] axes = new Vector3d[m];
            double[] d6 = new double[m], lambdaMin = new double[m], areaUnit = new double[m];
            bool[] degenerate = new bool[m];
            Action<int, Work> body = (i, work) => {
                tree.Nearest(i, k, work.Index, work.Dist2);
                d6[i] = Math.Sqrt(work.Dist2[Math.Min(6, k - 1)]);        // [0] is the point itself
                int jn = Math.Min(8, k - 1);
                areaUnit[i] = Math.PI * work.Dist2[jn] / (jn + 1);
                double lmin;
                axes[i] = PcaAxis(unit, work.Index, k, work.Solver, out lmin, out degenerate[i]);
                lambdaMin[i] = lmin;
            };
            if (Parallel)
                gParallel.ForRange(m, () => new Work(k), body);
            else {
                Work w = new Work(k);
                for (int i = 0; i < m; ++i) body(i, w);
            }
            List<double> d6Valid = new List<double>(), lambdaValid = new List<double>();
            int validUnique = 0;
            for (int i = 0; i < m; ++i) {
                if (degenerate[i]) { axes[i] = Vector3d.Zero; continue; }
                validUnique++;
                d6Valid.Add(d6[i]); lambdaValid.Add(lambdaMin[i]);
            }
            Normals = new Vector3d[n];
            Confident = new bool[n];
            double areaScale = fromUnit * fromUnit;
            PointArea = new double[n];
            for (int i = 0; i < n; ++i) PointArea[i] = areaUnit[map[i]] * areaScale;
            Action<Vector3d[]> expand = (u) => { for (int i = 0; i < n; ++i) Normals[i] = u[map[i]]; };
            for (int i = 0; i < n; ++i) if (!degenerate[map[i]]) AxisCount++;
            if (validUnique == 0 || (double)AxisCount / n < MinConfidentFraction) {
                Vector3d[] sign = new Vector3d[m];
                for (int i = 0; i < m; ++i) sign[i] = CanonicalSign(axes[i]);
                expand(sign);
                if (validUnique > 0) {
                    Spacing = Median(d6Valid.ToArray()) / Math.Sqrt(6.0 / Math.PI) * fromUnit;
                    NoiseSigma = Math.Sqrt(Math.Max(0, Median(lambdaValid.ToArray()))) * fromUnit;
                }
                Status = NormalsStatus.DegenerateInput;
                Error = AxisCount + " of " + n + " points have a normal axis";
                return false;
            }
            Spacing = Median(d6Valid.ToArray()) / Math.Sqrt(6.0 / Math.PI) * fromUnit;
            NoiseSigma = Math.Sqrt(Math.Max(0, Median(lambdaValid.ToArray()))) * fromUnit;
            double condition = Math.Max(SpacingFactor * Spacing, NoiseFactor * NoiseSigma);

            if (!Orient) {
                Vector3d[] sign = new Vector3d[m];
                for (int i = 0; i < m; ++i) sign[i] = CanonicalSign(axes[i]);
                expand(sign);
                UsedAlpha = Alpha;
                Status = NormalsStatus.Success;
                return true;
            }

            Vector3d[] oriented = new Vector3d[m];
            bool[] confident = new bool[m];
            UsedAlpha = Alpha > 0 ? Alpha : condition;
            for (int attempt = 0; ; ++attempt) {
                UsedOffset = Offset > 0 ? Offset : UsedAlpha / OffsetDivisor;
                AlphaConditionMet = UsedAlpha >= condition;
                MeshAlphaWrap wrap = new MeshAlphaWrap(pts) { Alpha = UsedAlpha, Offset = UsedOffset, MaxSteinerPoints = MaxSteinerPoints };
                bool ok = wrap.Compute();
                WrapStatus = wrap.Status;
                bool limited = wrap.Status == MeshAlphaWrap.WrapStatus.PointLimitReached;
                bool usable = (ok || limited) && wrap.Result != null && wrap.Result.IsClosed();
                string notClean = "";
                if (usable && limited) {
                    // a wrap that stopped early keeps its guarantees (class comment of MeshAlphaWrap), but the exact parity test of the
                    // probes needs a closed, manifold wrap without self-intersections: check it, it is the rare case
                    notClean = WrapProblemF(wrap.Result);
                    usable = notClean == "";
                    if (!usable && Alpha <= 0 && attempt < MaxEscalations) {
                        // a coarser wrap needs fewer Steiner points: try it before giving up
                        UsedAlpha *= EscalationFactor;
                        Escalations++;
                        continue;
                    }
                }
                if (!usable) {
                    Error = "alpha wrap: " + wrap.Status + " " + wrap.Error + (notClean == "" ? "" : " (not clean: " + notClean + ")");
                    Vector3d[] sign = new Vector3d[m];
                    for (int i = 0; i < m; ++i) sign[i] = CanonicalSign(axes[i]);
                    expand(sign);
                    Array.Clear(Confident, 0, n);
                    ConfidentCount = 0;
                    Wrap = null;             // (an earlier attempt of the escalation may have set it)
                    Status = NormalsStatus.WrapFailed;
                    return false;
                }
                Wrap = wrap.Result;
                Array.Copy(axes, oriented, m);
                Array.Clear(confident, 0, m);
                OrientWithWrap(pts, degenerate, oriented, confident);
                int count = 0;
                for (int i = 0; i < n; ++i) if (confident[map[i]]) ++count;
                ConfidentCount = count;
                if (ConfidentFraction >= MinConfidentFraction || Alpha > 0 || attempt >= MaxEscalations)
                    break;
                UsedAlpha *= EscalationFactor;
                Escalations++;
            }
            expand(oriented);
            for (int i = 0; i < n; ++i) Confident[i] = confident[map[i]];

            if (!AlphaConditionMet) Status = NormalsStatus.AlphaBelowCondition;
            else if (ConfidentFraction < MinConfidentFraction) Status = NormalsStatus.AmbiguousOrientation;
            else Status = NormalsStatus.Success;
            return Status == NormalsStatus.Success;
        }

        /// <summary> 2^e as a double, exact, for e in [-1023, 1023] </summary>
        static double PowerOfTwo(int e)
        {
            if (e == -1023) return BitConverter.Int64BitsToDouble(1L << 51);
            return BitConverter.Int64BitsToDouble((long)(e + 1023) << 52);
        }

        /// <summary> for tests: what is checked of a wrap that stopped at MaxSteinerPoints (replaceable to simulate an unclean one) </summary>
        internal Func<DMesh3, string> WrapProblemF = WrapProblem;

        /// <summary> "" if the wrap is closed, valid, without bowtie vertices and without self-intersections (what the parity test of the probes needs), else the reason </summary>
        internal static string WrapProblem(DMesh3 wrap)
        {
            if (!wrap.IsClosed()) return "not closed";
            if (!wrap.CheckValidity(false, FailMode.ReturnOnly)) return "invalid mesh";
            if (MeshIterators.BowtieVertices(wrap).Any()) return "bowtie vertices";
            if (MeshRepairMetrics.HasSelfIntersections(wrap)) return "self-intersections";       // (stops at the first pair)
            return "";
        }

        void OrientWithWrap(Vector3d[] pts, bool[] degenerate, Vector3d[] normals, bool[] confident)
        {
            int n = pts.Length;
            double probe = ProbeFactor * (UsedAlpha + UsedOffset);
            DMeshAABBTree3 tree = new DMeshAABBTree3(Wrap, true);
            // the probes of the points with a normal axis: index 2j = outer candidate (p + d n), 2j+1 = p - d n
            int[] ids = Enumerable.Range(0, n).Where(i => !degenerate[i]).ToArray();
            Vector3d[] probes = new Vector3d[2 * ids.Length];
            for (int j = 0; j < ids.Length; ++j) {
                int i = ids[j];
                probes[2 * j] = pts[i] + probe * normals[i];
                probes[2 * j + 1] = pts[i] - probe * normals[i];
            }
            InsideStatus[] inside = new InsideStatus[probes.Length];
            BatchOptions options = new BatchOptions() { Parallel = Parallel };
            MeshBatchQueries.IsInsideBatch(tree, probes, inside, InsideMethod.Crossings, options);

            // ambiguous points (both probes on the same side, or a probe that is not finite): the direction to the
            // nearest wrap point decides (not confident)
            List<int> ambiguous = new List<int>();
            for (int j = 0; j < ids.Length; ++j) {
                int i = ids[j];
                InsideStatus plus = inside[2 * j], minus = inside[2 * j + 1];
                if (plus == InsideStatus.Outside && minus == InsideStatus.Inside) confident[i] = true;
                else if (plus == InsideStatus.Inside && minus == InsideStatus.Outside) { normals[i] = -normals[i]; confident[i] = true; }
                else ambiguous.Add(i);
            }
            if (ambiguous.Count > 0) {
                Vector3d[] amb = ambiguous.Select(i => pts[i]).ToArray();
                NearestPointResult[] near = new NearestPointResult[amb.Length];
                MeshBatchQueries.NearestPointBatch(tree, amb, near, null, options);
                for (int a = 0; a < amb.Length; ++a) {
                    int i = ambiguous[a];
                    if (near[a].Triangle != DMesh3.InvalidID && (near[a].Point - pts[i]).Dot(normals[i]) < 0)
                        normals[i] = -normals[i];
                }
            }
        }

        static Vector3d CanonicalSign(Vector3d v)
        {
            double ax = Math.Abs(v.x), ay = Math.Abs(v.y), az = Math.Abs(v.z);
            double m = ax >= ay && ax >= az ? v.x : (ay >= az ? v.y : v.z);
            return m < 0 ? -v : v;
        }

        static double Median(double[] values)
        {
            double[] sorted = (double[])values.Clone();
            Array.Sort(sorted);
            int n = sorted.Length;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        class Work
        {
            public int[] Index; public double[] Dist2; public SymmetricEigenSolver Solver = new SymmetricEigenSolver(3, 4096);
            public Work(int k) { Index = new int[k]; Dist2 = new double[k]; }
        }

        /// <summary>
        /// The unit eigenvector of the smallest eigenvalue of the covariance of the neighbours (the covariance is divided
        /// by its trace for the solver, so the matrix of a power of two scale is bit for bit the same). lambdaMin is the
        /// smallest eigenvalue per point in length^2. degenerate: coincident points, or collinear ones (the second
        /// eigenvalue below 1e-9 of the largest)
        /// </summary>
        static Vector3d PcaAxis(Vector3d[] pts, int[] index, int k, SymmetricEigenSolver solver, out double lambdaMin, out bool degenerate)
        {
            Vector3d mean = Vector3d.Zero;
            for (int j = 0; j < k; ++j) mean += pts[index[j]];
            mean /= k;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            for (int j = 0; j < k; ++j) {
                Vector3d d = pts[index[j]] - mean;
                xx += d.x * d.x; xy += d.x * d.y; xz += d.x * d.z; yy += d.y * d.y; yz += d.y * d.z; zz += d.z * d.z;
            }
            double trace = xx + yy + zz;
            lambdaMin = 0; degenerate = true;
            if (!(trace > 0) || double.IsInfinity(trace)) return Vector3d.Zero;
            double inv = 1.0 / trace;
            double[] m = { xx * inv, xy * inv, xz * inv, xy * inv, yy * inv, yz * inv, xz * inv, yz * inv, zz * inv };
            if (solver.Solve(m, SymmetricEigenSolver.SortType.Increasing) == SymmetricEigenSolver.NO_CONVERGENCE)
                return Vector3d.Zero;
            double[] ev = solver.GetEigenvalues();       // increasing
            double[] vec = new double[3];
            solver.GetEigenvector(0, vec);
            lambdaMin = Math.Max(0, ev[0]) * trace / (k - 3);       // (k points, the fitted plane takes 3 degrees of freedom)
            degenerate = !(ev[1] > 1e-9 * ev[2]);
            Vector3d axis = new Vector3d(vec[0], vec[1], vec[2]);
            double len = axis.Length;
            return len > 0 ? axis / len : Vector3d.Zero;
        }

        /// <summary>
        /// A k-d tree for exact k-nearest-neighbour queries (ties by index): median splits along the widest axis of the
        /// node, leaves of up to 16 points; a subtree is skipped only if its splitting plane is strictly farther than
        /// the k-th distance found, so equal distances are all visited.
        /// </summary>
        internal class PointTree
        {
            const int LeafSize = 16;
            readonly Vector3d[] pts;
            readonly int[] idx;
            readonly byte[] axisAt;

            public PointTree(Vector3d[] points)
            {
                pts = points;
                idx = new int[points.Length];
                for (int i = 0; i < idx.Length; ++i) idx[i] = i;
                axisAt = new byte[points.Length];
                Build(0, points.Length);
            }

            double Coord(int i, int axis) { return axis == 0 ? pts[i].x : (axis == 1 ? pts[i].y : pts[i].z); }

            // (coordinate, index) order: a total order, so the splits do not depend on the algorithm's internals
            bool Less(int a, int b, int axis)
            {
                double ca = Coord(a, axis), cb = Coord(b, axis);
                return ca < cb || (ca == cb && a < b);
            }

            void Build(int lo, int hi)
            {
                if (hi - lo <= LeafSize) return;
                double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue, maxx = -double.MaxValue, maxy = -double.MaxValue, maxz = -double.MaxValue;
                for (int s = lo; s < hi; ++s) {
                    Vector3d p = pts[idx[s]];
                    if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x;
                    if (p.y < miny) miny = p.y; if (p.y > maxy) maxy = p.y;
                    if (p.z < minz) minz = p.z; if (p.z > maxz) maxz = p.z;
                }
                double wx = maxx - minx, wy = maxy - miny, wz = maxz - minz;
                int axis = wx >= wy && wx >= wz ? 0 : (wy >= wz ? 1 : 2);
                int mid = (lo + hi) / 2;
                Select(lo, hi - 1, mid, axis);
                axisAt[mid] = (byte)axis;
                Build(lo, mid);
                Build(mid + 1, hi);
            }

            // quickselect: afterwards idx[mid] is the element of rank mid in idx[lo..hi], smaller ones before, larger after
            void Select(int lo, int hi, int mid, int axis)
            {
                while (lo < hi) {
                    if (hi - lo == 1) {
                        if (Less(idx[hi], idx[lo], axis)) Swap(lo, hi);
                        return;
                    }
                    int mi = lo + (hi - lo) / 2;
                    // median of three as the pivot
                    if (Less(idx[mi], idx[lo], axis)) Swap(mi, lo);
                    if (Less(idx[hi], idx[lo], axis)) Swap(hi, lo);
                    if (Less(idx[hi], idx[mi], axis)) Swap(hi, mi);
                    int pivot = idx[mi];
                    Swap(mi, hi - 1);
                    int i = lo, j = hi - 1;
                    while (true) {
                        while (Less(idx[++i], pivot, axis)) { }
                        while (Less(pivot, idx[--j], axis)) { }
                        if (i >= j) break;
                        Swap(i, j);
                    }
                    Swap(i, hi - 1);
                    if (mid == i) return;
                    if (mid < i) hi = i - 1; else lo = i + 1;
                }
            }

            void Swap(int a, int b) { int t = idx[a]; idx[a] = idx[b]; idx[b] = t; }

            /// <summary> the k nearest points of point i (itself first), by squared distance and then index; k results in index/dist2 </summary>
            public void Nearest(int i, int k, int[] index, double[] dist2)
            {
                int count = 0;
                Search(0, pts.Length, pts[i], k, index, dist2, ref count);
            }

            void Search(int lo, int hi, Vector3d q, int k, int[] index, double[] dist2, ref int count)
            {
                if (hi - lo <= LeafSize) {
                    for (int s = lo; s < hi; ++s) Consider(idx[s], q, k, index, dist2, ref count);
                    return;
                }
                int mid = (lo + hi) / 2;
                int axis = axisAt[mid];
                int m = idx[mid];
                Consider(m, q, k, index, dist2, ref count);
                double diff = (axis == 0 ? q.x : (axis == 1 ? q.y : q.z)) - Coord(m, axis);
                bool nearLeft = diff < 0;
                if (nearLeft) Search(lo, mid, q, k, index, dist2, ref count); else Search(mid + 1, hi, q, k, index, dist2, ref count);
                if (count < k || diff * diff <= dist2[k - 1]) {
                    if (nearLeft) Search(mid + 1, hi, q, k, index, dist2, ref count); else Search(lo, mid, q, k, index, dist2, ref count);
                }
            }

            void Consider(int j, Vector3d q, int k, int[] index, double[] dist2, ref int count)
            {
                double d2 = (pts[j] - q).LengthSquared;
                int pos = count;
                if (count == k) {
                    if (d2 > dist2[k - 1] || (d2 == dist2[k - 1] && j > index[k - 1])) return;
                    pos = k - 1;
                } else count++;
                while (pos > 0 && (dist2[pos - 1] > d2 || (dist2[pos - 1] == d2 && index[pos - 1] > j))) {
                    dist2[pos] = dist2[pos - 1]; index[pos] = index[pos - 1]; --pos;
                }
                dist2[pos] = d2; index[pos] = j;
            }
        }
    }
}
