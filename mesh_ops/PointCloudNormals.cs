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
            AmbiguousOrientation = 5
        }

        /// <summary> the input points (not changed) </summary>
        public IList<Vector3d> Points;

        /// <summary> number of neighbours of the PCA, including the point itself; at least 8 (default 16) </summary>
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
        public double EscalationFactor = 1.6;
        public bool Parallel = true;

        // results
        public NormalsStatus Status;
        /// <summary> unit normals, or zero for a point with a degenerate neighbourhood; set also if Status is not Success (but not for InvalidInput / TooFewPoints) </summary>
        public Vector3d[] Normals;
        /// <summary> the sign of Normals[i] is certain by the probes (false: ambiguous or degenerate) </summary>
        public bool[] Confident;
        public int ConfidentCount;
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
            Normals = null; Confident = null; Wrap = null; ConfidentCount = 0; Error = "";
            Spacing = NoiseSigma = UsedAlpha = UsedOffset = 0; AlphaConditionMet = false; Escalations = 0;
            WrapStatus = MeshAlphaWrap.WrapStatus.Success;
            int n = Points == null ? 0 : Points.Count;
            int k = Math.Max(8, Neighbors);
            if (Points == null) { Status = NormalsStatus.InvalidInput; Error = "Points is null"; return false; }
            for (int i = 0; i < n; ++i) {
                Vector3d p = Points[i];
                if (double.IsNaN(p.x + p.y + p.z) || double.IsInfinity(p.x) || double.IsInfinity(p.y) || double.IsInfinity(p.z)) {
                    Status = NormalsStatus.InvalidInput; Error = "point " + i + " is not finite"; return false;
                }
            }
            if (n < k + 1) { Status = NormalsStatus.TooFewPoints; Error = n + " points, need " + (k + 1); return false; }

            Vector3d[] pts = new Vector3d[n];
            for (int i = 0; i < n; ++i) pts[i] = Points[i];

            // neighbourhoods, PCA axes, spacing and noise
            PointGrid grid = new PointGrid(pts);
            Normals = new Vector3d[n];
            Confident = new bool[n];
            double[] d6 = new double[n], lambdaMin = new double[n];
            bool[] degenerate = new bool[n];
            Action<int, Work> body = (i, work) => {
                grid.Nearest(i, k, work.Index, work.Dist2);
                d6[i] = Math.Sqrt(work.Dist2[Math.Min(6, k - 1)]);        // [0] is the point itself
                double lmin;
                Vector3d axis = PcaAxis(pts, work.Index, k, work.Solver, out lmin, out degenerate[i]);
                Normals[i] = axis; lambdaMin[i] = lmin;
            };
            if (Parallel)
                gParallel.ForRange(n, () => new Work(k), body);
            else {
                Work w = new Work(k);
                for (int i = 0; i < n; ++i) body(i, w);
            }
            Spacing = Median(d6) / Math.Sqrt(6.0 / Math.PI);
            NoiseSigma = Math.Sqrt(Math.Max(0, Median(lambdaMin)));
            for (int i = 0; i < n; ++i)
                if (degenerate[i]) Normals[i] = Vector3d.Zero;

            double condition = Math.Max(SpacingFactor * Spacing, NoiseFactor * NoiseSigma);
            if (!(Spacing > 0)) {
                // (coincident points: no scale at all)
                Status = NormalsStatus.TooFewPoints; Error = "the points have no extent"; return false;
            }

            if (!Orient) {
                for (int i = 0; i < n; ++i) Normals[i] = Normals[i] == Vector3d.Zero ? Normals[i] : CanonicalSign(Normals[i]);
                UsedAlpha = Alpha;
                Status = NormalsStatus.Success;
                return true;
            }

            Vector3d[] axes = (Vector3d[])Normals.Clone();
            UsedAlpha = Alpha > 0 ? Alpha : condition;
            for (int attempt = 0; ; ++attempt) {
                UsedOffset = Offset > 0 ? Offset : UsedAlpha / OffsetDivisor;
                AlphaConditionMet = UsedAlpha >= condition;
                MeshAlphaWrap wrap = new MeshAlphaWrap(pts) { Alpha = UsedAlpha, Offset = UsedOffset };
                bool ok = wrap.Compute();
                WrapStatus = wrap.Status;
                if (!ok && wrap.Status != MeshAlphaWrap.WrapStatus.PointLimitReached) {
                    Error = "alpha wrap: " + wrap.Status + " " + wrap.Error;
                    for (int i = 0; i < n; ++i) Normals[i] = axes[i] == Vector3d.Zero ? axes[i] : CanonicalSign(axes[i]);
                    Wrap = null;
                    Status = NormalsStatus.WrapFailed;
                    return false;
                }
                Wrap = wrap.Result;
                Array.Copy(axes, Normals, n);
                Array.Clear(Confident, 0, n);
                OrientWithWrap(pts, degenerate);
                if (ConfidentFraction >= MinConfidentFraction || Alpha > 0 || attempt >= MaxEscalations)
                    break;
                UsedAlpha *= EscalationFactor;
                Escalations++;
            }

            if (!AlphaConditionMet) Status = NormalsStatus.AlphaBelowCondition;
            else if (ConfidentFraction < MinConfidentFraction) Status = NormalsStatus.AmbiguousOrientation;
            else Status = NormalsStatus.Success;
            return Status == NormalsStatus.Success;
        }

        void OrientWithWrap(Vector3d[] pts, bool[] degenerate)
        {
            int n = pts.Length;
            double probe = ProbeFactor * (UsedAlpha + UsedOffset);
            DMeshAABBTree3 tree = new DMeshAABBTree3(Wrap, true);
            // the probes of the points with a normal axis: index 2j = outer candidate (p + d n), 2j+1 = p - d n
            int[] ids = Enumerable.Range(0, n).Where(i => !degenerate[i]).ToArray();
            Vector3d[] probes = new Vector3d[2 * ids.Length];
            for (int j = 0; j < ids.Length; ++j) {
                int i = ids[j];
                probes[2 * j] = pts[i] + probe * Normals[i];
                probes[2 * j + 1] = pts[i] - probe * Normals[i];
            }
            InsideStatus[] inside = new InsideStatus[probes.Length];
            BatchOptions options = new BatchOptions() { Parallel = Parallel };
            MeshBatchQueries.IsInsideBatch(tree, probes, inside, InsideMethod.Crossings, options);

            // ambiguous points: the direction to the nearest wrap point decides (not confident)
            List<int> ambiguous = new List<int>();
            for (int j = 0; j < ids.Length; ++j) {
                int i = ids[j];
                bool plusIn = inside[2 * j] == InsideStatus.Inside, minusIn = inside[2 * j + 1] == InsideStatus.Inside;
                if (!plusIn && minusIn) { Confident[i] = true; }
                else if (plusIn && !minusIn) { Normals[i] = -Normals[i]; Confident[i] = true; }
                else ambiguous.Add(i);
            }
            if (ambiguous.Count > 0) {
                Vector3d[] amb = ambiguous.Select(i => pts[i]).ToArray();
                NearestPointResult[] near = new NearestPointResult[amb.Length];
                MeshBatchQueries.NearestPointBatch(tree, amb, near, null, options);
                for (int a = 0; a < amb.Length; ++a) {
                    int i = ambiguous[a];
                    if (near[a].Triangle != DMesh3.InvalidID && (near[a].Point - pts[i]).Dot(Normals[i]) < 0)
                        Normals[i] = -Normals[i];
                }
            }
            for (int i = 0; i < n; ++i)
                if (degenerate[i]) Confident[i] = false;
            ConfidentCount = 0;
            for (int i = 0; i < n; ++i) if (Confident[i]) ++ConfidentCount;
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
            public double[] Matrix = new double[9];
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

        /// <summary> A uniform grid for exact k-nearest-neighbour queries (the result does not depend on the cell size). </summary>
        internal class PointGrid
        {
            readonly Vector3d[] pts;
            readonly Vector3d origin;
            readonly double cell;
            readonly int nx, ny, nz;
            readonly int[] start, items;

            public PointGrid(Vector3d[] points)
            {
                pts = points;
                AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
                foreach (Vector3d p in points) box.Contain(p);
                origin = box.Min;
                double diag = Math.Max(box.Diagonal.Length, 1e-300);
                // about 2 points per cell for a volume sampling; capped at 4M cells
                double c = diag / Math.Max(1.0, Math.Pow(points.Length / 2.0, 1.0 / 3.0));
                double ex = box.Width, ey = box.Height, ez = box.Depth;
                while (true) {
                    double cells = (Math.Floor(ex / c) + 1) * (Math.Floor(ey / c) + 1) * (Math.Floor(ez / c) + 1);
                    if (cells <= 4e6) break;
                    c *= 1.5;
                }
                cell = c > 0 ? c : 1;
                nx = (int)Math.Floor(ex / cell) + 1; ny = (int)Math.Floor(ey / cell) + 1; nz = (int)Math.Floor(ez / cell) + 1;
                int[] cellOf = new int[points.Length];
                start = new int[nx * ny * nz + 1];
                for (int i = 0; i < points.Length; ++i) {
                    cellOf[i] = CellIndex(points[i]);
                    start[cellOf[i] + 1]++;
                }
                for (int c2 = 0; c2 < nx * ny * nz; ++c2) start[c2 + 1] += start[c2];
                items = new int[points.Length];
                int[] fill = new int[nx * ny * nz];
                for (int i = 0; i < points.Length; ++i)
                    items[start[cellOf[i]] + fill[cellOf[i]]++] = i;
            }

            int Coord(double v, double o, int n) { int c = (int)Math.Floor((v - o) / cell); return c < 0 ? 0 : (c >= n ? n - 1 : c); }
            int CellIndex(Vector3d p) { return (Coord(p.z, origin.z, nz) * ny + Coord(p.y, origin.y, ny)) * nx + Coord(p.x, origin.x, nx); }

            /// <summary> the k nearest points of point i (itself first), by squared distance and then index; k results in index/dist2 </summary>
            public void Nearest(int i, int k, int[] index, double[] dist2)
            {
                Vector3d p = pts[i];
                int cx = Coord(p.x, origin.x, nx), cy = Coord(p.y, origin.y, ny), cz = Coord(p.z, origin.z, nz);
                int count = 0;
                int maxR = Math.Max(nx, Math.Max(ny, nz));
                for (int r = 0; r <= maxR; ++r) {
                    for (int z = cz - r; z <= cz + r; ++z) {
                        if (z < 0 || z >= nz) continue;
                        for (int y = cy - r; y <= cy + r; ++y) {
                            if (y < 0 || y >= ny) continue;
                            bool edgeYZ = (z == cz - r || z == cz + r || y == cy - r || y == cy + r);
                            for (int x = cx - r; x <= cx + r; ++x) {
                                if (x < 0 || x >= nx) continue;
                                if (!edgeYZ && x != cx - r && x != cx + r) continue;     // only the shell of the ring
                                int c = (z * ny + y) * nx + x;
                                for (int s = start[c]; s < start[c + 1]; ++s) {
                                    int j = items[s];
                                    double d2 = (pts[j] - p).LengthSquared;
                                    Insert(index, dist2, ref count, k, j, d2);
                                }
                            }
                        }
                    }
                    // everything not yet visited is farther than r * cell
                    if (count == k) {
                        double reach = r * cell;
                        if (dist2[k - 1] <= reach * reach) break;
                    }
                }
            }

            static void Insert(int[] index, double[] dist2, ref int count, int k, int j, double d2)
            {
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
