// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// 2.5D dual contouring of terrain (height fields such as open-pit mines, dumps, DTMs). Rebuilds the top
    /// surface of a (possibly broken) input mesh as a clean height-field mesh with sharp break lines and
    /// few triangles:
    ///
    ///   - the input is sampled with vertical rays on a grid in XY (SamplesPerCell^2 rays per cell), which
    ///     sees only the top surface, so triangle soups, duplicated, overlapping or inconsistently oriented
    ///     triangles do not matter;
    ///   - each cell gets one vertex at the minimum of a QEF of the sampled planes (as in dual contouring),
    ///     so vertices sit on break lines (crest and toe of each bench) instead of rounding them off;
    ///   - cells are merged in a quadtree while the QEF error stays below Tolerance (2:1 balanced), so flat
    ///     berms and uniform slopes become a few large triangles;
    ///   - the mesh is the dual of the quadtree: one triangle or quad around each cell corner; each quad is
    ///     split along the diagonal that matches the input better;
    ///   - the outer boundary (and the rims of holes) is kept: cells on the input boundary get a vertex on
    ///     the boundary line (2D QEF of the boundary edges, so its corners are kept exactly).
    ///
    /// Overhangs and vertical walls cannot be represented by a height field; the top surface is used.
    /// All computations are relative to the grid origin, so large (eg UTM) coordinates are fine.
    /// </summary>
    public class TerrainDualContouring
    {
        public DMesh3 Mesh;

        /// <summary>
        /// Alternatively a point cloud (eg LiDAR ground points, see ClothSimulationFilter): the height and
        /// normal at a sample are fitted to the points within PointRadius (weighted least squares plane).
        /// The outer boundary is then where the points end (no boundary edges to follow).
        /// </summary>
        public IList<Vector3d> Points;
        /// <summary> neighbourhood radius for the point fit; 0 = 1.5 x the mean point spacing </summary>
        public double PointRadius = 0;

        /// <summary> height and normal of the surface at world (x, y); false outside its footprint </summary>
        public delegate bool HeightSampleFunc(double x, double y, out double z, out Vector3d normal);
        /// <summary>
        /// Alternatively any height field given by a sampling function over SampleBounds (eg TerrainOffset).
        /// The normals are the Hermite data, so break lines of the sampled surface stay sharp.
        /// </summary>
        public HeightSampleFunc SampleF;
        public AxisAlignedBox3d SampleBounds;

        /// <summary> finest cell size in XY. If zero, uses the larger XY extent / GridResolution </summary>
        public double CellSize = 0;
        public int GridResolution = 256;
        /// <summary> vertical rays per cell along each axis (finest cells) </summary>
        public int SamplesPerCell = 4;

        /// <summary> merge cells into a quadtree while the RMS distance to the sampled planes stays below Tolerance </summary>
        public bool Adaptive = true;
        /// <summary> RMS distance tolerance for merging (0 = 0.05 * CellSize) </summary>
        public double Tolerance = 0;
        /// <summary> largest merged cell, in finest cells (power of two) </summary>
        public int MaxCellSize = 32;

        /// <summary> place vertices at the QEF minimum (sharp break lines); false = mean of the samples (smooth) </summary>
        public bool UseQEF = true;
        /// <summary> eigenvalues below this fraction of the largest are ignored in the QEF solve </summary>
        public double QEFEigenTolerance = 0.1;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        public int FinestCells, LeafCells, BoundaryCells;
        /// <summary> dual triangles that were skipped because they would face down </summary>
        public int SkippedTriangles;


        public TerrainDualContouring(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public TerrainDualContouring(IList<Vector3d> points)
        {
            Points = points;
        }

        /// <summary> for SampleF </summary>
        public TerrainDualContouring()
        {
        }


        public bool Compute()
        {
            AxisAlignedBox3d bounds;
            if (SampleF != null) {
                bounds = SampleBounds;
            } else if (Points != null) {
                // non-finite points (broken records) are skipped
                points = new List<Vector3d>(Points.Count);
                bounds = AxisAlignedBox3d.Empty;
                foreach (Vector3d p in Points) {
                    if (double.IsNaN(p.x + p.y + p.z) || double.IsInfinity(p.x + p.y + p.z)) continue;
                    points.Add(p);
                    bounds.Contain(p);
                }
                if (points.Count < 3)
                    return false;
            } else {
                if (Mesh.TriangleCount == 0)
                    return false;
                // bounds of the triangles: isolated vertices (eg at 0,0,0 next to UTM data) do not count
                bounds = AxisAlignedBox3d.Empty;
                foreach (int tid in Mesh.TriangleIndices())
                    bounds.Contain(Mesh.GetTriBounds(tid));
            }
            cell = (CellSize > 0) ? CellSize : Math.Max(bounds.Width, bounds.Height) / GridResolution;
            if (!(cell > 0) || SamplesPerCell < 1 || double.IsNaN(bounds.MaxDim) || double.IsInfinity(bounds.MaxDim))
                return false;
            if (!((bounds.Width / cell + 2) * (bounds.Height / cell + 2) <= MaxCells))
                return false;
            tolerance = (Tolerance > 0) ? Tolerance : 0.05 * cell;
            if (SampleF != null)
                rayTop = bounds.Max.z;
            else if (Points != null)
                build_point_grid(bounds);
            else
                spatial = new DMeshAABBTree3(Mesh, true);
            rayTop = bounds.Max.z + 1 + 0.01 * bounds.Depth;
            rayLength = rayTop - bounds.Min.z + 1;

            // grid origin slightly below the bounds by an odd fraction of a cell, so that vertices and edges at
            // round coordinates do not land exactly on cell borders or sample rays
            origin = new Vector3d(bounds.Min.x - 0.1371 * cell, bounds.Min.y - 0.2713 * cell, bounds.Min.z);
            NX = (int)Math.Ceiling((bounds.Max.x - origin.x) / cell) + 1;
            NY = (int)Math.Ceiling((bounds.Max.y - origin.y) / cell) + 1;
            FinestCells = NX * NY;

            sample_cells();
            if (CancelF()) return false;
            if (Points == null && SampleF == null)
                add_boundary_edges();
            else
                segs = new List<BoundarySeg>[NX * NY];
            if (CancelF()) return false;
            build_quadtree();
            if (CancelF()) return false;
            if (Adaptive)
                balance();
            build_mesh();
            if (CancelF()) return false;
            // eg cells larger than the footprint: every vertex needs four sampled cells around it
            return Result != null && Result.TriangleCount > 0;
        }

        /// <summary> largest finest grid (cells); larger ones fail </summary>
        public static double MaxCells = 1 << 28;
        List<Vector3d> points;


        // QEF of planes n.(v - p) = 0, as sums so it can be merged; coordinates relative to origin
        struct Qef
        {
            public double a00, a01, a02, a11, a12, a22, b0, b1, b2, c;
            public Vector3d mass;
            public int n;
            public void Add(Vector3d p, Vector3d nrm)
            {
                double d = nrm.Dot(p);
                a00 += nrm.x * nrm.x; a01 += nrm.x * nrm.y; a02 += nrm.x * nrm.z;
                a11 += nrm.y * nrm.y; a12 += nrm.y * nrm.z; a22 += nrm.z * nrm.z;
                b0 += d * nrm.x; b1 += d * nrm.y; b2 += d * nrm.z; c += d * d;
                mass += p; n++;
            }
            public void Add(ref Qef q)
            {
                a00 += q.a00; a01 += q.a01; a02 += q.a02; a11 += q.a11; a12 += q.a12; a22 += q.a22;
                b0 += q.b0; b1 += q.b1; b2 += q.b2; c += q.c; mass += q.mass; n += q.n;
            }
            public double Error(Vector3d v)
            {
                double vav = a00 * v.x * v.x + a11 * v.y * v.y + a22 * v.z * v.z
                    + 2 * (a01 * v.x * v.y + a02 * v.x * v.z + a12 * v.y * v.z);
                return Math.Max(0, vav - 2 * (b0 * v.x + b1 * v.y + b2 * v.z) + c);
            }
        }

        struct BoundarySeg
        {
            public Vector3d A, B;      // local coordinates, clipped to the cell
            public Vector2d N;         // 2D normal of the boundary line
        }

        double cell, tolerance, rayTop, rayLength;
        Vector3d origin;
        int NX, NY;
        DMeshAABBTree3 spatial;
        Qef[] qef;
        int[] hits;
        List<BoundarySeg>[] segs;


        int cid(int i, int j) { return i + NX * j; }
        Vector3d to_world(Vector3d local) { return local + origin; }

        // top surface hit of a vertical ray at world (x,y): local point and upward normal
        bool sample(double x, double y, out Vector3d p, out Vector3d n)
        {
            if (SampleF != null) {
                double z;
                p = n = Vector3d.Zero;
                if (SampleF(x, y, out z, out n) == false)
                    return false;
                p = new Vector3d(x, y, z) - origin;
                if (n.z < 0) n = -n;
                return true;
            }
            if (Points != null)
                return sample_points(x, y, out p, out n);
            p = n = Vector3d.Zero;
            Ray3d ray = new Ray3d(new Vector3d(x, y, rayTop), -Vector3d.AxisZ);
            int tid = spatial.FindNearestHitTriangle(ray, rayLength);
            if (tid == DMesh3.InvalidID)
                return false;
            IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(Mesh, tid, ray);
            if (intr.Find() == false)
                return false;
            p = ray.PointAt(intr.RayParameter) - origin;
            n = Mesh.GetTriNormal(tid);
            if (n.z < 0)
                n = -n;     // orientation of the input does not matter
            return true;
        }

        // point cloud: bins of size pointRadius, points relative to the origin of the bins
        double pointRadius;
        Vector2d pointBinOrigin;
        int pbx, pby;
        int[] binStart, binPoints;
        Vector3d[] localPoints;

        void build_point_grid(AxisAlignedBox3d bounds)
        {
            int n = points.Count;
            double spacing = Math.Sqrt(Math.Max(bounds.Width * bounds.Height, MathUtil.Epsilon) / n);
            pointRadius = (PointRadius > 0) ? PointRadius : 1.5 * spacing;
            pointBinOrigin = bounds.Min.xy;
            pbx = (int)(bounds.Width / pointRadius) + 1;
            pby = (int)(bounds.Height / pointRadius) + 1;
            int[] bin = new int[n];
            int[] count = new int[pbx * pby + 1];
            for (int k = 0; k < n; ++k) {
                Vector3d p = points[k];
                int i = Math.Min(pbx - 1, (int)((p.x - pointBinOrigin.x) / pointRadius)), j = Math.Min(pby - 1, (int)((p.y - pointBinOrigin.y) / pointRadius));
                bin[k] = i + pbx * j;
                count[bin[k] + 1]++;
            }
            binStart = new int[pbx * pby + 1];
            for (int b = 0; b < pbx * pby; ++b)
                binStart[b + 1] = binStart[b] + count[b + 1];
            int[] fill = new int[pbx * pby];
            binPoints = new int[n];
            localPoints = new Vector3d[n];
            for (int k = 0; k < n; ++k) {
                binPoints[binStart[bin[k]] + fill[bin[k]]++] = k;
                localPoints[k] = points[k];
            }
            rayTop = bounds.Max.z;      // unused for points
        }

        // weighted least squares plane z = a + b dx + c dy through the points within pointRadius of (x, y)
        // gaps in the cloud (eg where the ground filter removed points) are bridged by retrying with a
        // larger radius, up to 4x; outside the cloud (points on one side only) there is no sample
        bool sample_points(double x, double y, out Vector3d p, out Vector3d n)
        {
            for (int k = 1; k <= 4; k *= 2)
                if (sample_points(x, y, k, out p, out n))
                    return true;
            p = n = Vector3d.Zero;
            return false;
        }

        bool sample_points(double x, double y, int radiusFactor, out Vector3d p, out Vector3d n)
        {
            p = n = Vector3d.Zero;
            double R = pointRadius * radiusFactor, R2 = R * R;
            int ci = (int)Math.Floor((x - pointBinOrigin.x) / pointRadius), cj = (int)Math.Floor((y - pointBinOrigin.y) / pointRadius);
            double s00 = 0, s01 = 0, s02 = 0, s11 = 0, s12 = 0, s22 = 0, r0 = 0, r1 = 0, r2 = 0, wsum = 0, mx = 0, my = 0;
            int count = 0;
            for (int j = cj - radiusFactor; j <= cj + radiusFactor; ++j) {
                if (j < 0 || j >= pby) continue;
                for (int i = ci - radiusFactor; i <= ci + radiusFactor; ++i) {
                    if (i < 0 || i >= pbx) continue;
                    int b = i + pbx * j;
                    for (int q = binStart[b]; q < binStart[b + 1]; ++q) {
                        Vector3d v = localPoints[binPoints[q]];
                        double dx = v.x - x, dy = v.y - y, d2 = dx * dx + dy * dy;
                        if (d2 >= R2) continue;
                        double t = 1 - d2 / R2, w = t * t;
                        double z = v.z - origin.z;
                        s00 += w; s01 += w * dx; s02 += w * dy; s11 += w * dx * dx; s12 += w * dx * dy; s22 += w * dy * dy;
                        r0 += w * z; r1 += w * dx * z; r2 += w * dy * z;
                        wsum += w; mx += w * dx; my += w * dy;
                        count++;
                    }
                }
            }
            if (count < 4)
                return false;
            // outside the cloud (only points on one side): no sample
            if (Math.Sqrt(mx * mx + my * my) / wsum > 0.5 * R)
                return false;
            double det = s00 * (s11 * s22 - s12 * s12) - s01 * (s01 * s22 - s12 * s02) + s02 * (s01 * s12 - s11 * s02);
            if (Math.Abs(det) < 1e-12 * s00 * s00 * s00 * R2 * R2)
                return false;
            double a = (r0 * (s11 * s22 - s12 * s12) - s01 * (r1 * s22 - s12 * r2) + s02 * (r1 * s12 - s11 * r2)) / det;
            double bx = (s00 * (r1 * s22 - s12 * r2) - r0 * (s01 * s22 - s12 * s02) + s02 * (s01 * r2 - r1 * s02)) / det;
            double by = (s00 * (s11 * r2 - r1 * s12) - s01 * (s01 * r2 - r1 * s02) + r0 * (s01 * s12 - s11 * s02)) / det;
            p = new Vector3d(x - origin.x, y - origin.y, a);
            n = new Vector3d(-bx, -by, 1).Normalized;
            return true;
        }

        bool is_inside_footprint(Vector2d local)
        {
            Vector3d p, n;
            return sample(local.x + origin.x, local.y + origin.y, out p, out n);
        }


        void sample_cells()
        {
            qef = new Qef[NX * NY];
            hits = new int[NX * NY];
            segs = new List<BoundarySeg>[NX * NY];
            int S = Math.Max(1, SamplesPerCell);
            gParallel.ForEach(Interval1i.Range(NY), (j) => {
                if (CancelF()) return;
                for (int i = 0; i < NX; ++i) {
                    Qef q = new Qef();
                    int h = 0;
                    for (int sj = 0; sj < S; ++sj)
                        for (int si = 0; si < S; ++si) {
                            double x = origin.x + (i + (si + 0.5) / S) * cell, y = origin.y + (j + (sj + 0.5) / S) * cell;
                            Vector3d p, n;
                            if (sample(x, y, out p, out n)) {
                                q.Add(p, n);
                                h++;
                            }
                        }
                    qef[cid(i, j)] = q;
                    hits[cid(i, j)] = h;
                }
            });
        }


        // Boundary edges of the input whose one side is covered and the other is not (for a soup every edge
        // is a boundary edge topologically; interior ones are covered on both sides). Clipped into cells.
        void add_boundary_edges()
        {
            BoundaryCells = 0;
            double eps = 1e-3 * cell;
            List<int> candidates = new List<int>();
            foreach (int eid in Mesh.BoundaryEdgeIndices())
                candidates.Add(eid);
            bool[] isBoundary = new bool[candidates.Count];
            gParallel.ForEach(Interval1i.Range(candidates.Count), (k) => {
                Index2i ev = Mesh.GetEdgeV(candidates[k]);
                Vector3d a = Mesh.GetVertex(ev.a), b = Mesh.GetVertex(ev.b);
                Vector2d d = b.xy - a.xy;
                double len = d.Length;
                if (len < 1e-9 * cell)
                    return;
                Vector2d n2 = new Vector2d(-d.y, d.x) / len;
                Vector2d m = 0.5 * (a.xy + b.xy) - origin.xy;
                isBoundary[k] = is_inside_footprint(m + eps * n2) != is_inside_footprint(m - eps * n2);
            });

            for (int k = 0; k < candidates.Count; ++k) {
                if (isBoundary[k] == false)
                    continue;
                Index2i ev = Mesh.GetEdgeV(candidates[k]);
                Vector3d a = Mesh.GetVertex(ev.a) - origin, b = Mesh.GetVertex(ev.b) - origin;
                Vector2d d = b.xy - a.xy;
                Vector2d n2 = new Vector2d(-d.y, d.x).Normalized;
                int i0 = clampi((int)Math.Floor(Math.Min(a.x, b.x) / cell), NX), i1 = clampi((int)Math.Floor(Math.Max(a.x, b.x) / cell), NX);
                int j0 = clampi((int)Math.Floor(Math.Min(a.y, b.y) / cell), NY), j1 = clampi((int)Math.Floor(Math.Max(a.y, b.y) / cell), NY);
                for (int j = j0; j <= j1; ++j)
                    for (int i = i0; i <= i1; ++i) {
                        double t0, t1;
                        if (clip(a.xy, b.xy, new Vector2d(i * cell, j * cell), new Vector2d((i + 1) * cell, (j + 1) * cell), out t0, out t1) == false)
                            continue;
                        int c = cid(i, j);
                        if (segs[c] == null) {
                            segs[c] = new List<BoundarySeg>();
                            BoundaryCells++;
                        }
                        segs[c].Add(new BoundarySeg() { A = Vector3d.Lerp(a, b, t0), B = Vector3d.Lerp(a, b, t1), N = n2 });
                    }
            }
        }
        static int clampi(int v, int n) { return Math.Max(0, Math.Min(n - 1, v)); }

        // Liang-Barsky clip of segment a-b to the box [mn, mx]
        static bool clip(Vector2d a, Vector2d b, Vector2d mn, Vector2d mx, out double t0, out double t1)
        {
            t0 = 0; t1 = 1;
            Vector2d d = b - a;
            for (int k = 0; k < 2; ++k) {
                if (Math.Abs(d[k]) < 1e-300) {
                    if (a[k] < mn[k] || a[k] > mx[k]) return false;
                    continue;
                }
                double ta = (mn[k] - a[k]) / d[k], tb = (mx[k] - a[k]) / d[k];
                if (ta > tb) { double tmp = ta; ta = tb; tb = tmp; }
                t0 = Math.Max(t0, ta); t1 = Math.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return t1 - t0 > 1e-12;
        }


        bool is_valid(int c) { return hits[c] > 0 || segs[c] != null; }
        // fully covered interior cell, may be merged
        bool is_simple(int c) { return segs[c] == null && hits[c] == SamplesPerCell * SamplesPerCell; }


        // quadtree: level l node (i,j) covers finest cells [i*2^l, (i+1)*2^l) x [j*2^l, (j+1)*2^l)
        int levels;
        bool[][] merged;
        Qef[][] nodeQef;
        struct Leaf { public int Level, I, J; public bool Alive; }
        List<Leaf> leaves;
        int[] owner;        // leaf index per finest cell, -1 = no data

        void build_quadtree()
        {
            levels = 0;
            if (Adaptive) {
                int maxSize = Math.Max(1, MaxCellSize);
                while ((1 << (levels + 1)) <= maxSize && (1 << (levels + 1)) <= Math.Max(NX, NY))
                    levels++;
            }
            merged = new bool[levels + 1][];
            nodeQef = new Qef[levels + 1][];
            merged[0] = new bool[NX * NY];
            nodeQef[0] = qef;
            for (int c = 0; c < NX * NY; ++c)
                merged[0][c] = is_simple(c);
            for (int l = 1; l <= levels; ++l) {
                int nx = level_dim(NX, l), ny = level_dim(NY, l), cnx = level_dim(NX, l - 1), cny = level_dim(NY, l - 1);
                merged[l] = new bool[nx * ny];
                nodeQef[l] = new Qef[nx * ny];
                int L = l;
                gParallel.ForEach(Interval1i.Range(ny), (j) => {
                    for (int i = 0; i < nx; ++i) {
                        Qef q = new Qef();
                        bool all = true;
                        for (int k = 0; k < 4 && all; ++k) {
                            int ci = 2 * i + (k & 1), cj = 2 * j + (k >> 1);
                            if (ci >= cnx || cj >= cny || merged[L - 1][ci + cnx * cj] == false)
                                all = false;
                            else
                                q.Add(ref nodeQef[L - 1][ci + cnx * cj]);
                        }
                        if (all == false)
                            continue;
                        int size = 1 << L;
                        Vector3d v = solve(ref q, new Vector2d(i * size * cell, j * size * cell), size * cell);
                        if (q.Error(v) <= tolerance * tolerance * q.n) {
                            merged[L][i + nx * j] = true;
                            nodeQef[L][i + nx * j] = q;
                        }
                    }
                });
            }

            leaves = new List<Leaf>();
            owner = new int[NX * NY];
            for (int c = 0; c < owner.Length; ++c)
                owner[c] = -1;
            int tnx = level_dim(NX, levels), tny = level_dim(NY, levels);
            for (int j = 0; j < tny; ++j)
                for (int i = 0; i < tnx; ++i)
                    collect_leaves(levels, i, j, false);
            LeafCells = leaves.Count;
        }
        static int level_dim(int n, int l) { return (n + (1 << l) - 1) >> l; }

        void collect_leaves(int l, int i, int j, bool parentMerged)
        {
            int nx = level_dim(NX, l);
            if (l == 0) {
                if (i < NX && j < NY && is_valid(cid(i, j)))
                    add_leaf(0, i, j);
                return;
            }
            if (i < nx && j < level_dim(NY, l) && merged[l][i + nx * j]) {
                add_leaf(l, i, j);
                return;
            }
            for (int k = 0; k < 4; ++k)
                collect_leaves(l - 1, 2 * i + (k & 1), 2 * j + (k >> 1), false);
        }

        void add_leaf(int l, int i, int j)
        {
            int idx = leaves.Count;
            leaves.Add(new Leaf() { Level = l, I = i, J = j, Alive = true });
            int size = 1 << l;
            for (int cj = j * size; cj < Math.Min(NY, (j + 1) * size); ++cj)
                for (int ci = i * size; ci < Math.Min(NX, (i + 1) * size); ++ci)
                    owner[cid(ci, cj)] = idx;
        }

        // 2:1 balance: a leaf may not border leaves less than half its size (avoids slivers)
        void balance()
        {
            bool changed = true;
            while (changed) {
                changed = false;
                int count = leaves.Count;
                for (int li = 0; li < count; ++li) {
                    Leaf leaf = leaves[li];
                    if (leaf.Alive == false || leaf.Level < 2)
                        continue;
                    if (has_small_neighbour(leaf) == false)
                        continue;
                    leaf.Alive = false;
                    leaves[li] = leaf;
                    for (int k = 0; k < 4; ++k)
                        add_leaf(leaf.Level - 1, 2 * leaf.I + (k & 1), 2 * leaf.J + (k >> 1));
                    changed = true;
                }
            }
            LeafCells = 0;
            foreach (Leaf leaf in leaves)
                if (leaf.Alive) LeafCells++;
        }

        bool has_small_neighbour(Leaf leaf)
        {
            int size = 1 << leaf.Level;
            int x0 = leaf.I * size, y0 = leaf.J * size;
            for (int k = 0; k < size; ++k) {
                int[][] nbs = {
                    new[] { x0 - 1, y0 + k }, new[] { x0 + size, y0 + k },
                    new[] { x0 + k, y0 - 1 }, new[] { x0 + k, y0 + size } };
                foreach (int[] nb in nbs) {
                    if (nb[0] < 0 || nb[1] < 0 || nb[0] >= NX || nb[1] >= NY)
                        continue;
                    int o = owner[cid(nb[0], nb[1])];
                    if (o >= 0 && leaves[o].Level < leaf.Level - 1)
                        return true;
                }
            }
            return false;
        }


        // QEF minimum with XY inside the square [min, min + size]^2. Z is free, so it is eliminated
        // (z = best height for given XY), which leaves a 2D quadratic in XY that is minimized exactly over
        // the square: inside if possible, else on its border. Just clamping the unconstrained minimum would
        // move vertices off the break line.
        Vector3d solve(ref Qef q, Vector2d min, double size)
        {
            Vector3d mass = q.mass / Math.Max(1, q.n);
            if (UseQEF == false || q.n == 0)
                return mass;
            double trace = q.a00 + q.a11 + q.a22;
            if (q.a22 < 1e-9 * trace)
                return clamp_xy(mass, min, size);
            double hb = q.b2 / q.a22, hx = q.a02 / q.a22, hy = q.a12 / q.a22;
            Func<Vector2d, double> height = (xy) => hb - hx * xy.x - hy * xy.y;

            // E(x, y, height(x,y)) = p^T M p - 2 g.p + const
            double m00 = q.a00 - q.a02 * q.a02 / q.a22, m01 = q.a01 - q.a02 * q.a12 / q.a22, m11 = q.a11 - q.a12 * q.a12 / q.a22;
            Vector2d g = new Vector2d(q.b0 - q.a02 * q.b2 / q.a22, q.b1 - q.a12 * q.b2 / q.a22);
            Vector2d c = mass.xy;
            Vector2d r = g - new Vector2d(m00 * c.x + m01 * c.y, m01 * c.x + m11 * c.y);   // relative to mass

            // eigen-decomposition of the symmetric 2x2 M; weak directions (eg along a single break line)
            // are not solved but kept near the mass point
            double tr = m00 + m11, det = m00 * m11 - m01 * m01;
            double disc = Math.Sqrt(Math.Max(0, 0.25 * tr * tr - det));
            double l1 = 0.5 * tr + disc, l2 = 0.5 * tr - disc;
            Vector2d e1 = (Math.Abs(m01) > 1e-12 * Math.Max(tr, 1e-300)) ? new Vector2d(l1 - m11, m01).Normalized : (m00 >= m11 ? Vector2d.AxisX : Vector2d.AxisY);
            Vector2d e2 = new Vector2d(-e1.y, e1.x);
            double scale = Math.Max(trace, 1e-300);
            if (l1 < 1e-9 * scale) {
                Vector2d xy0 = clamp_xy(mass, min, size).xy;
                return new Vector3d(xy0.x, xy0.y, height(xy0));
            }
            double w1 = l1, w2 = (l2 >= QEFEigenTolerance * l1) ? l2 : 1e-3 * l1;
            Vector2d best = c + (e1.Dot(r) / l1) * e1;
            if (l2 >= QEFEigenTolerance * l1)
                best += (e2.Dot(r) / l2) * e2;

            // minimize Q(p) = w1 (e1.(p - best))^2 + w2 (e2.(p - best))^2 over the square
            Vector2d bmin = min, bmax = min + size * Vector2d.One;
            if (best.x < bmin.x || best.y < bmin.y || best.x > bmax.x || best.y > bmax.y) {
                Func<Vector2d, double> Q = (p) => { Vector2d d = p - best; double u = e1.Dot(d), v = e2.Dot(d); return w1 * u * u + w2 * v * v; };
                Vector2d[] corners = { bmin, new Vector2d(bmax.x, bmin.y), bmax, new Vector2d(bmin.x, bmax.y) };
                Vector2d bestOnBorder = corners[0];
                double bestQ = double.MaxValue;
                for (int k = 0; k < 4; ++k) {
                    Vector2d a = corners[k], d = corners[(k + 1) % 4] - a;
                    // Q(a + t d) is quadratic in t: minimize on [0,1]
                    double du = e1.Dot(d), dv = e2.Dot(d);
                    Vector2d ab = a - best;
                    double au = e1.Dot(ab), av = e2.Dot(ab);
                    double qa = w1 * du * du + w2 * dv * dv, qb = w1 * au * du + w2 * av * dv;
                    double t = (qa > 0) ? MathUtil.Clamp(-qb / qa, 0, 1) : 0;
                    Vector2d p = a + t * d;
                    double qp = Q(p);
                    if (qp < bestQ) { bestQ = qp; bestOnBorder = p; }
                }
                best = bestOnBorder;
            }
            return new Vector3d(best.x, best.y, height(best));
        }

        static Vector3d clamp_xy(Vector3d v, Vector2d min, double size)
        {
            return new Vector3d(MathUtil.Clamp(v.x, min.x, min.x + size), MathUtil.Clamp(v.y, min.y, min.y + size), v.z);
        }

        // vertex of a finest cell on the input boundary: XY at the 2D QEF minimum of the boundary lines
        // (on the line, or at the corner where two lines meet), Z from the boundary edge itself
        Vector3d solve_boundary(int i, int j)
        {
            List<BoundarySeg> list = segs[cid(i, j)];
            double m00 = 0, m01 = 0, m11 = 0, r0 = 0, r1 = 0, wsum = 0;
            Vector2d mass = Vector2d.Zero;
            foreach (BoundarySeg s in list) {
                double w = Math.Max(s.A.xy.Distance(s.B.xy), 1e-9 * cell);
                Vector2d mid = 0.5 * (s.A.xy + s.B.xy);
                double d = s.N.Dot(mid);
                m00 += w * s.N.x * s.N.x; m01 += w * s.N.x * s.N.y; m11 += w * s.N.y * s.N.y;
                r0 += w * d * s.N.x; r1 += w * d * s.N.y;
                mass += w * mid; wsum += w;
            }
            mass /= wsum;
            // solve relative to the mass point; drop the weak direction (single straight line)
            double rr0 = r0 - (m00 * mass.x + m01 * mass.y), rr1 = r1 - (m01 * mass.x + m11 * mass.y);
            double tr = m00 + m11, det = m00 * m11 - m01 * m01;
            double disc = Math.Sqrt(Math.Max(0, 0.25 * tr * tr - det));
            double l1 = 0.5 * tr + disc, l2 = 0.5 * tr - disc;
            Vector2d e1 = (Math.Abs(m01) > 1e-12 * tr) ? new Vector2d(l1 - m11, m01).Normalized : (m00 >= m11 ? Vector2d.AxisX : Vector2d.AxisY);
            Vector2d e2 = new Vector2d(-e1.y, e1.x);
            Vector2d rr = new Vector2d(rr0, rr1);
            Vector2d xy = mass;
            if (l1 > MathUtil.Epsilon)
                xy += (e1.Dot(rr) / l1) * e1;
            if (l2 > 0.1 * l1)
                xy += (e2.Dot(rr) / l2) * e2;
            xy.x = MathUtil.Clamp(xy.x, i * cell, (i + 1) * cell);
            xy.y = MathUtil.Clamp(xy.y, j * cell, (j + 1) * cell);

            // height (and exact position) from the nearest point on the boundary segments
            double best = double.MaxValue;
            Vector3d result = new Vector3d(xy.x, xy.y, 0);
            foreach (BoundarySeg s in list) {
                Segment2d seg = new Segment2d(s.A.xy, s.B.xy);
                double t = seg.Project(xy);
                t = MathUtil.Clamp(t, -seg.Extent, seg.Extent);
                double d = seg.PointAt(t).DistanceSquared(xy);
                if (d < best) {
                    best = d;
                    double u = (seg.Extent > 0) ? 0.5 * (t / seg.Extent + 1) : 0.5;
                    Vector3d p = Vector3d.Lerp(s.A, s.B, u);
                    result = (d < 1e-18 * cell * cell) ? p : new Vector3d(xy.x, xy.y, p.z);
                }
            }
            return result;
        }

        Vector3d leaf_vertex(Leaf leaf)
        {
            int size = 1 << leaf.Level;
            if (leaf.Level == 0 && segs[cid(leaf.I, leaf.J)] != null)
                return solve_boundary(leaf.I, leaf.J);
            Qef q = (leaf.Level == 0) ? qef[cid(leaf.I, leaf.J)] : nodeQef[leaf.Level][leaf.I + level_dim(NX, leaf.Level) * leaf.J];
            return solve(ref q, new Vector2d(leaf.I * size * cell, leaf.J * size * cell), size * cell);
        }


        void build_mesh()
        {
            DMesh3 mesh = new DMesh3();
            int[] leafVertex = new int[leaves.Count];
            Vector3d[] pos = new Vector3d[leaves.Count];
            gParallel.ForEach(Interval1i.Range(leaves.Count), (li) => {
                if (leaves[li].Alive)
                    pos[li] = leaf_vertex(leaves[li]);
            });
            for (int li = 0; li < leaves.Count; ++li)
                leafVertex[li] = leaves[li].Alive ? mesh.AppendVertex(pos[li]) : -1;

            // corners of the leaves (finest-lattice points), each once
            HashSet<long> corners = new HashSet<long>();
            List<Vector2i> cornerList = new List<Vector2i>();
            foreach (Leaf leaf in leaves) {
                if (leaf.Alive == false) continue;
                int size = 1 << leaf.Level;
                for (int k = 0; k < 4; ++k) {
                    int I = (leaf.I + (k & 1)) * size, J = (leaf.J + (k >> 1)) * size;
                    if (corners.Add((long)I * (NY + 2) + J))
                        cornerList.Add(new Vector2i(I, J));
                }
            }
            cornerList.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            SkippedTriangles = 0;
            List<Index4i> corner3 = new List<Index4i>();
            int[] ring = new int[4];
            int[] ringLeaf = new int[4];
            foreach (Vector2i P in cornerList) {
                // the 4 finest cells around P, counter-clockwise: SW, SE, NE, NW
                int[,] around = { { P.x - 1, P.y - 1 }, { P.x, P.y - 1 }, { P.x, P.y }, { P.x - 1, P.y } };
                bool nearBoundary = false;
                int invalid = 0, invalidK = -1;
                for (int k = 0; k < 4; ++k) {
                    int ci = around[k, 0], cj = around[k, 1];
                    if (ci < 0 || cj < 0 || ci >= NX || cj >= NY || owner[cid(ci, cj)] < 0) { invalid++; invalidK = k; continue; }
                    ringLeaf[k] = owner[cid(ci, cj)];
                    if (leaves[ringLeaf[k]].Level == 0 && is_simple(cid(ci, cj)) == false)
                        nearBoundary = true;
                }
                if (invalid == 1 && nearBoundary)
                    corner3.Add(new Index4i(P.x, P.y, invalidK, 0));
                if (invalid > 0)
                    continue;
                if (nearBoundary && is_inside_footprint(new Vector2d(P.x * cell, P.y * cell)) == false)
                    continue;
                int n = 0;
                for (int k = 0; k < 4; ++k) {
                    int v = leafVertex[ringLeaf[k]];
                    if (n > 0 && ring[n - 1] == v) continue;
                    ring[n++] = v;
                }
                if (n > 1 && ring[n - 1] == ring[0]) n--;
                if (n == 3)
                    append_tri(mesh, ring[0], ring[1], ring[2]);
                else if (n == 4)
                    append_quad(mesh, ring[0], ring[1], ring[2], ring[3]);
            }

            // grid points with three valid cells, the fourth outside: at a convex corner of the boundary the
            // corner cell may have no grid point inside the footprint; the triangle of the three cells keeps
            // the corner (only if it lies inside the footprint)
            foreach (Index4i c3 in corner3) {
                int[,] around = { { c3.a - 1, c3.b - 1 }, { c3.a, c3.b - 1 }, { c3.a, c3.b }, { c3.a - 1, c3.b } };
                int n = 0;
                for (int k = 0; k < 4; ++k) {
                    if (k == c3.c) continue;
                    int v = leafVertex[owner[cid(around[k, 0], around[k, 1])]];
                    if (n > 0 && ring[n - 1] == v) continue;
                    ring[n++] = v;
                }
                if (n > 1 && ring[n - 1] == ring[0]) n--;
                if (n != 3) continue;
                Vector3d centroid = (mesh.GetVertex(ring[0]) + mesh.GetVertex(ring[1]) + mesh.GetVertex(ring[2])) / 3;
                if (is_inside_footprint(centroid.xy) && faces_up(mesh, ring[0], ring[1], ring[2]))
                    mesh.AppendTriangle(ring[0], ring[1], ring[2]);
            }
            // vertices of cells that got no triangle (eg along gaps in a point cloud)
            foreach (int vid in mesh.VertexIndices())
                if (mesh.GetVtxEdgeCount(vid) == 0)
                    mesh.RemoveVertex(vid, false, false);

            MeshTransforms.Translate(mesh, origin);
            Result = mesh;
        }

        bool faces_up(DMesh3 mesh, int a, int b, int c)
        {
            Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b), pc = mesh.GetVertex(c);
            return (pb.x - pa.x) * (pc.y - pa.y) - (pb.y - pa.y) * (pc.x - pa.x) > 1e-12 * cell * cell;
        }

        void append_tri(DMesh3 mesh, int a, int b, int c)
        {
            if (faces_up(mesh, a, b, c) == false || mesh.AppendTriangle(a, b, c) < 0)
                SkippedTriangles++;
        }

        // split along the diagonal whose midpoint is closer to the input surface (follows break lines)
        void append_quad(DMesh3 mesh, int v0, int v1, int v2, int v3)
        {
            bool okA = faces_up(mesh, v0, v1, v2) && faces_up(mesh, v0, v2, v3);
            bool okB = faces_up(mesh, v0, v1, v3) && faces_up(mesh, v1, v2, v3);
            bool useA;
            if (okA != okB)
                useA = okA;
            else {
                double errA = diagonal_error(mesh.GetVertex(v0), mesh.GetVertex(v2));
                double errB = diagonal_error(mesh.GetVertex(v1), mesh.GetVertex(v3));
                useA = errA <= errB;
            }
            if (useA) {
                append_tri(mesh, v0, v1, v2);
                append_tri(mesh, v0, v2, v3);
            } else {
                append_tri(mesh, v0, v1, v3);
                append_tri(mesh, v1, v2, v3);
            }
        }

        double diagonal_error(Vector3d a, Vector3d b)
        {
            Vector3d m = 0.5 * (a + b);
            Vector3d p, n;
            if (sample(m.x + origin.x, m.y + origin.y, out p, out n) == false)
                return double.MaxValue;
            return Math.Abs(p.z - m.z);
        }
    }
}
