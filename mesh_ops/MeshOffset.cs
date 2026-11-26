// Copyright (c) geometry3Sharp contributors. Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Offset (Distance &gt; 0) and inset (Distance &lt; 0) of a closed mesh by the Euclidean distance |Distance|: the boundary
    /// of the points whose signed distance sd to the input (positive outside) is at most Distance. Stage A1 of
    /// docs/design/MESH_OFFSET.md: dual contouring of the field F = Distance - sd on a regular grid, with Hermite data from the
    /// exact geometry of the input (the point of the offset surface on the grid edge and the direction to the nearest input
    /// point), so that the creases where two offset sheets meet stay sharp; the convex parts are round, as the offset surface is.
    ///
    /// Guarantees and reports (see the design note, section 6):
    ///   - closed and 2-manifold (manifold dual contouring with face vertices, DualContouring), checked: Status Failed otherwise;
    ///   - SelfIntersectionFree (reported, like BandCertified): true if the result has no self-intersections, decided exactly
    ///     by DualContouring (options A and B of docs/design/DC_INTERSECTION_FREE.md and its exact check of the remaining
    ///     triangles); false if its repair could not remove every intersection (not seen in the tests and the benchmark), with
    ///     Status still Success;
    ///   - the band (one side, proven): every point x of the result has |sd(x) - Distance| &lt;= BandDeviation. The bound comes from
    ///     the band certificate (CertifyBand): sd is 1-Lipschitz, so on a triangle within r of a point c it is within r of sd(c),
    ///     and sd(c) is enclosed by range-guarded hardware-double certified bounds (MO-1..MO-4, CTB-1/CTB-2) falling back to
    ///     interval arithmetic (ExactInterval) outside the normal double range; triangles whose bound exceeds the tolerance are
    ///     split for the check (the result is not changed). BandCertified says BandDeviation &lt;= BandTolerance.
    ///     The other side (every point of the offset surface near the result) and the topology are measured, not proven, in this
    ///     stage (stage A2: the topology certificate);
    ///   - scale-free: the input is processed as a copy scaled by a power of two to an extent in [1, 2); grid and tolerances are
    ///     relative to |Distance|, so 2^k times the input and the distance gives 2^k times the result, bit for bit. At the ends
    ///     of the supported extent [2^-900, 2^900] the scaling can round coordinates that are tiny against the extent into the
    ///     subnormal range (by at most 2^-1075 of the extent, about 2^-175 absolute at the limit), the input on the way in and
    ///     the result on the way out; the band and the bit identity then hold for that perturbed mesh.
    ///
    /// Only the vertices of triangles are used: isolated vertices (in bounds, finiteness and the grid) are ignored.
    ///
    /// Input: a closed, consistently outward oriented mesh without self-intersections (the sign of sd comes from the exact
    /// ray-crossing parity of DMeshAABBTree3.IsInside, which is only meaningful for such meshes; repair others first, eg with
    /// MeshRobustRepair, or wrap them with MeshAlphaWrap). Open meshes (NotClosed) and inside-out ones (negative volume,
    /// InsideOut) are refused; self-intersections are not checked here.
    ///
    /// Not promised: triangle quality. Degenerate (zero-area) triangles were measured in 1 of 32 benchmark cases (the chamfer inset
    /// at a cell of |Distance| / 8: three dual contouring vertices on a crease line in one triangle) until DualContouring's quad
    /// split avoided them (#800; 0 of 32 since); a quad whose both splits are degenerate can still give one. They do not affect
    /// the guarantees above, but MeshRobustRepair.NotCleanReason reports them.
    ///
    /// Features of the offset surface narrower than about a cell (RelativeCellSize * |Distance|) can be lost or merged, as in any
    /// grid contouring; an inset deeper than half the thickness of a part removes it. Status Empty means that nothing is left at this grid: it is also
    /// returned for a part thinner than about half a cell (RelativeCellSize * |Distance|) after the inset, which still exists (a finer grid, ie a smaller
    /// RelativeCellSize, finds it; #1597), so Empty does not prove that the part is gone.
    /// Usage: set the fields, call Compute(), read Result and the reports. Compute() can be called again after changes.
    /// </summary>
    public class MeshOffset
    {
        public enum Status
        {
            /// <summary> Result is set: closed and manifold; see SelfIntersectionFree and BandCertified </summary>
            Success,
            /// <summary> null or empty mesh, non-finite coordinates, or an extent outside [2^-900, 2^900] </summary>
            InvalidInput,
            /// <summary> the mesh has boundary edges </summary>
            NotClosed,
            /// <summary> the closed mesh has a negative volume (its triangles face inward) </summary>
            InsideOut,
            /// <summary> Distance zero or not finite, RelativeCellSize / CellSize out of range, BandTolerance not finite </summary>
            InvalidParameters,
            /// <summary> the grid would have more than MaxGridNodes nodes </summary>
            GridTooLarge,
            /// <summary> nothing resolved at this grid: the inset is beyond the thickness of the part, or the part left is thinner than about half a cell and a smaller RelativeCellSize can still find it </summary>
            Empty,
            /// <summary> the contour is not closed and manifold (left-out polygons, NonManifoldTriangles) or not finite; Result kept for diagnostics </summary>
            Failed,
            /// <summary> CancelF returned true </summary>
            Cancelled
        }

        /// <summary> the input: closed, outward, free of self-intersections (see the class comment) </summary>
        public DMesh3 Mesh;
        /// <summary> the offset distance, positive outward, negative inward </summary>
        public double Distance;
        /// <summary> grid cell in mesh units; if not positive, RelativeCellSize * |Distance|. Must be at most |Distance| / 2 </summary>
        public double CellSize = 0;
        /// <summary> grid cell as a fraction of |Distance| when CellSize is not set, in (0, 0.5] (default 1/4) </summary>
        public double RelativeCellSize = 0.25;
        /// <summary> the band the certificate tries to reach, in mesh units; if not positive, half the cell size </summary>
        public double BandTolerance = 0;
        /// <summary> how often a result triangle is split (into 4) for the band certificate before its bound is taken as it is </summary>
        public int MaxBandSplitDepth = 6;
        /// <summary>
        /// refuse a grid with more nodes than this (GridTooLarge). About 17 bytes per node are alive together (distance, field,
        /// side) plus the contouring's cells: about 1.1 GB at the default 64 million. Grids above 2^28 nodes (2 GB of doubles, the
        /// array limit of net48 without gcAllowVeryLargeObjects) or above 2^31 nodes along an axis are always refused.
        /// </summary>
        public long MaxGridNodes = 64000000;
        public bool UseParallel = true;
        public Func<bool> CancelF = () => false;

        /// <summary> the offset surface (a new mesh), null unless Success or Failed </summary>
        public DMesh3 Result;
        public Status ResultStatus;
        /// <summary> no two triangles of Result intersect except in shared vertices and edges (decided exactly) </summary>
        public bool SelfIntersectionFree;
        /// <summary> BandDeviation &lt;= BandTolerance </summary>
        public bool BandCertified;
        /// <summary> proven: |sd(x) - Distance| &lt;= BandDeviation for every point x of Result, in mesh units </summary>
        public double BandDeviation = double.PositiveInfinity;
        /// <summary> result triangles whose band bound, after MaxBandSplitDepth splits, is above BandTolerance </summary>
        public int UncertifiedTriangles;
        /// <summary> the cell size used, in mesh units </summary>
        public double UsedCellSize;
        /// <summary> polygons the contouring left out (Result then has holes and the status is Failed) </summary>
        public int NonManifoldTriangles;
        /// <summary> ms per phase of the last Compute() </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        public MeshOffset(DMesh3 mesh, double distance)
        {
            Mesh = mesh; Distance = distance;
        }

        // an odd fraction of a cell, so that faces at round coordinates (CAD data) do not land on grid nodes (as WindingContouring)
        static readonly Vector3d GridShift = new Vector3d(0.1371, 0.2713, 0.3891);

        public bool Compute()
        {
            ResultStatus = Status.Failed;   // until a check below says otherwise (also if a call throws)
            Result = null; SelfIntersectionFree = false; BandCertified = false; BandDeviation = double.PositiveInfinity;
            UncertifiedTriangles = 0; UsedCellSize = 0; NonManifoldTriangles = 0; PhaseMilliseconds = new Dictionary<string, long>();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Action<string> phase = (name) => { PhaseMilliseconds[name] = timer.ElapsedMilliseconds; timer.Restart(); };

            if (Mesh == null || Mesh.TriangleCount == 0)
                return fail(Status.InvalidInput);
            double d = Distance;
            if (!(d != 0) || double.IsInfinity(d) || double.IsNaN(d))
                return fail(Status.InvalidParameters);
            if (!(BandTolerance >= 0 || BandTolerance < 0) || double.IsInfinity(BandTolerance))
                return fail(Status.InvalidParameters);
            if (CellSize > 0) {
                if (double.IsInfinity(CellSize) || !(CellSize <= 0.5 * Math.Abs(d)))
                    return fail(Status.InvalidParameters);
            } else if (!(RelativeCellSize > 0 && RelativeCellSize <= 0.5))
                return fail(Status.InvalidParameters);
            // the vertices of triangles only (isolated vertices are ignored)
            AxisAlignedBox3d box0 = AxisAlignedBox3d.Empty;
            foreach (int t in Mesh.TriangleIndices()) {
                Index3i tv = Mesh.GetTriangle(t);
                for (int q = 0; q < 3; ++q) {
                    Vector3d p = Mesh.GetVertex(tv[q]);
                    if (!MathUtil.IsFinite(p.x) || !MathUtil.IsFinite(p.y) || !MathUtil.IsFinite(p.z))
                        return fail(Status.InvalidInput);
                    box0.Contain(p);
                }
            }
            if (Mesh.IsClosed() == false)
                return fail(Status.NotClosed);
            double extent = Math.Max(box0.MaxDim, Math.Abs(d));
            if (!(extent >= Math.Pow(2, -900) && extent <= Math.Pow(2, 900)))
                return fail(Status.InvalidInput);

            // the scaled copy: extent in [1, 2)
            int k = -Exponent(extent);
            double s = Pow2(k), sInv = Pow2(-k);
            DMesh3 mesh = new DMesh3(Mesh, false, MeshComponents.None);
            foreach (int v in mesh.VertexIndices().Where(v => mesh.GetVtxEdgeCount(v) == 0).ToList())
                mesh.RemoveVertex(v);   // isolated: not used (it may be far away or not finite)
            foreach (int v in mesh.VertexIndices())
                mesh.SetVertex(v, s * mesh.GetVertex(v));
            d *= s;
            double ad = Math.Abs(d);
            double h = (CellSize > 0) ? CellSize * s : RelativeCellSize * ad;
            double tol = (BandTolerance > 0) ? BandTolerance * s : 0.5 * h;
            UsedCellSize = h * sInv;
            if (MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x < 0)
                return fail(Status.InsideOut);

            var tree = new DMeshAABBTree3(mesh, true);
            phase("setup");
            if (CancelF()) return fail(Status.Cancelled);

            // the grid: the box of the input, the offset and two cells, shifted by an odd fraction of a cell
            AxisAlignedBox3d box = new AxisAlignedBox3d(s * box0.Min, s * box0.Max);   // exact: a power of two
            double margin = Math.Max(d, 0) + 2 * h;
            Vector3d origin = box.Min - margin * Vector3d.One - h * GridShift;
            Vector3d far = box.Max + margin * Vector3d.One;
            double nxd = Math.Ceiling((far.x - origin.x) / h) + 1, nyd = Math.Ceiling((far.y - origin.y) / h) + 1, nzd = Math.Ceiling((far.z - origin.z) / h) + 1;
            if (!(nxd * nyd * nzd <= MaxGridNodes) || !(nxd * nyd * nzd <= (1 << 28)) || !(Math.Max(nxd, Math.Max(nyd, nzd)) <= int.MaxValue))
                return fail(Status.GridTooLarge);
            int nx = (int)nxd, ny = (int)nyd, nz = (int)nzd;
            long nodeCount = (long)nx * ny * nz;
            Func<int, int, int, long> idx = (i, j, l) => i + (long)nx * (j + (long)ny * l);
            Func<int, int, int, Vector3d> nodePos = (i, j, l) => new Vector3d(origin.x + h * i, origin.y + h * j, origin.z + h * l);

            // unsigned distance at the nodes, up to |d| + 2h (farther nodes keep that value: only their sign matters)
            double cap = ad + 2 * h;
            double[] u = new double[nodeCount];
            Action<int> slice = (l) => {
                for (int j = 0; j < ny; ++j)
                    for (int i = 0; i < nx; ++i) {
                        double dsq;
                        int t = tree.FindNearestTriangle(nodePos(i, j, l), out dsq, cap);
                        u[idx(i, j, l)] = (t == DMesh3.InvalidID) ? cap : Math.Min(Math.Sqrt(dsq), cap);
                    }
            };
            if (UseParallel) gParallel.ForEach(Interval1i.Range(nz), slice);
            else for (int l = 0; l < nz; ++l) slice(l);
            phase("distance");
            if (CancelF()) return fail(Status.Cancelled);

            // signs: a node farther than 1.5 h from the input shares the side of the input with its neighbours (the grid edge to
            // them stays off the input), so one exact inside test per connected set of such nodes; nodes within 1.5 h < |d| of
            // the input do not need a sign (F = d - sd has the sign of d for any sign of sd there)
            sbyte[] side = new sbyte[nodeCount];
            double reach = 1.5 * h;
            var queue = new Queue<long>();
            for (long n0 = 0; n0 < nodeCount; ++n0) {
                if (side[n0] != 0 || !(u[n0] > reach)) continue;
                int i0 = (int)(n0 % nx), j0 = (int)((n0 / nx) % ny), l0 = (int)(n0 / ((long)nx * ny));
                side[n0] = tree.IsInside(nodePos(i0, j0, l0)) ? (sbyte)-1 : (sbyte)1;
                queue.Enqueue(n0);
                while (queue.Count > 0) {
                    long n = queue.Dequeue();
                    int i = (int)(n % nx), j = (int)((n / nx) % ny), l = (int)(n / ((long)nx * ny));
                    for (int q = 0; q < 6; ++q) {
                        int a = i + (q == 0 ? 1 : q == 1 ? -1 : 0), b = j + (q == 2 ? 1 : q == 3 ? -1 : 0), c = l + (q == 4 ? 1 : q == 5 ? -1 : 0);
                        if (a < 0 || b < 0 || c < 0 || a >= nx || b >= ny || c >= nz) continue;
                        long m = idx(a, b, c);
                        if (side[m] != 0) continue;
                        side[m] = side[n];
                        if (u[m] > reach) queue.Enqueue(m);
                    }
                }
            }
            // F = d - sd, inside (of the offset solid) where F > 0
            double[] f = new double[nodeCount];
            for (long n = 0; n < nodeCount; ++n)
                f[n] = d - (side[n] < 0 ? -u[n] : u[n]);
            u = null; side = null;
            phase("signs");
            if (CancelF()) return fail(Status.Cancelled);

            // candidate cells: a sign change among the corners
            var cells = new List<Vector3i>();
            for (int l = 0; l + 1 < nz; ++l)
                for (int j = 0; j + 1 < ny; ++j)
                    for (int i = 0; i + 1 < nx; ++i) {
                        bool any = false, all = true;
                        for (int q = 0; q < 8; ++q) {
                            bool ins = f[idx(i + (q & 1), j + ((q >> 1) & 1), l + ((q >> 2) & 1))] > 0;
                            any |= ins; all &= ins;
                        }
                        if (any && !all) cells.Add(new Vector3i(i, j, l));
                    }
            if (cells.Count == 0)
                return fail(Status.Empty);
            phase("cells");

            // the side of the input along a sign-changing grid edge: every point of it is within h < |d| / 2 of a node whose
            // field is beyond the offset, so the edge stays off the input: outside it for an offset, inside for an inset
            double edgeSide = d > 0 ? 1 : -1;
            var dc = new DualContouring() {
                Implicit = new FieldImplicit() { Owner = this, Tree = tree, D = d },
                IsoValue = 0, CellSize = h, CancelF = CancelF, ParallelCompute = UseParallel,
                QEFEigenTolerance = DualContouring.LegacyQEFEigenTolerance,   // pinned: the default changed with ExactHermite, this caller has its own HermiteF
                Simplify = false,   // pinned: uniform output
                Bounds = new AxisAlignedBox3d(origin, origin + new Vector3d((nx - 1) * h, (ny - 1) * h, (nz - 1) * h)),
                CandidateCells = cells,
                NodeValueF = (Vector3i n) => (n.x < 0 || n.y < 0 || n.z < 0 || n.x >= nx || n.y >= ny || n.z >= nz) ? -cap : f[idx(n.x, n.y, n.z)]
            };
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                hermite(tree, d, edgeSide, a, b, out point, out normal);
            dc.Generate();
            phase("contour");
            if (CancelF()) return fail(Status.Cancelled);
            DMesh3 result = dc.Mesh;
            if (result == null || result.TriangleCount == 0)
                return fail(Status.Empty);
            NonManifoldTriangles = dc.NonManifoldTriangles;
            SelfIntersectionFree = dc.SelfIntersectionFree;

            // the band certificate, on the scaled copy
            int unc;
            double dev = band_deviation(mesh, tree, result, d, tol, MaxBandSplitDepth, UseParallel, out unc);
            phase("band");

            foreach (int v in result.VertexIndices())
                result.SetVertex(v, sInv * result.GetVertex(v));
            Result = result;
            BandDeviation = dev * sInv;
            UncertifiedTriangles = unc;
            BandCertified = dev <= tol;
            bool finite = result.VertexIndices().All(v => {
                Vector3d p = result.GetVertex(v);
                return MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z);
            });
            ResultStatus = (NonManifoldTriangles == 0 && finite && result.IsClosed() && !result.VertexIndices().Any(v => result.IsBowtieVertex(v))) ? Status.Success : Status.Failed;
            return ResultStatus == Status.Success;
        }

        bool fail(Status status)
        {
            ResultStatus = status;
            return false;
        }

        // the field at any point (only for DualContouring's fallbacks; the grid values and the Hermite data are computed above)
        class FieldImplicit : ImplicitFunction3d
        {
            public MeshOffset Owner; public DMeshAABBTree3 Tree; public double D;
            public double Value(ref Vector3d pt)
            {
                double dsq;
                int t = Tree.FindNearestTriangle(pt, out dsq);
                double ud = (t == DMesh3.InvalidID) ? 0 : Math.Sqrt(dsq);
                return D - (Tree.IsInside(pt) ? -ud : ud);
            }
        }

        // the point of the offset surface on the grid edge from a (F > 0) to b (F < 0), by bracketed regula falsi with the
        // Illinois rule on F = d - side * u (u the unsigned distance; side is constant along the edge, see Compute), and the
        // outward normal of the offset solid there: side times the unit vector from the nearest input point
        static bool hermite(DMeshAABBTree3 tree, double d, double side, Vector3d a, Vector3d b, out Vector3d point, out Vector3d normal)
        {
            DMesh3 mesh = tree.Mesh;
            Func<Vector3d, double> field = (x) => {
                double dsq;
                tree.FindNearestTriangle(x, out dsq);
                return d - side * Math.Sqrt(dsq);
            };
            double t0 = 0, t1 = 1, f0 = field(a), f1 = field(b);
            double len = (b - a).Length;
            double t = 0.5;
            if (f0 > 0 && f1 < 0) {
                int keep = 0;
                for (int it = 0; it < 40; ++it) {
                    t = (f0 - f1 != 0) ? t0 + (t1 - t0) * f0 / (f0 - f1) : 0.5 * (t0 + t1);
                    if (!(t > t0 && t < t1)) t = 0.5 * (t0 + t1);
                    double ft = field(Vector3d.Lerp(a, b, t));
                    if (ft == 0 || (t1 - t0) * len <= 1e-9 * len) break;
                    if (ft > 0) { t0 = t; f0 = ft; if (keep == 1) f1 *= 0.5; keep = 1; }
                    else { t1 = t; f1 = ft; if (keep == -1) f0 *= 0.5; keep = -1; }
                    if (Math.Abs(ft) <= 1e-12 * Math.Abs(d)) break;
                }
            }
            point = Vector3d.Lerp(a, b, t);
            double dsq2;
            int tid = tree.FindNearestTriangle(point, out dsq2);
            normal = Vector3d.Zero;
            if (tid == DMesh3.InvalidID) return false;
            DistPoint3Triangle3 dist = MeshQueries.TriangleDistance(mesh, tid, point);
            Vector3d dir = point - dist.TriangleClosest;
            double l = dir.Length;
            if (!(l > 0)) return false;
            normal = (side / l) * dir;
            return true;
        }

        /// <summary>
        /// The band certificate of docs/design/MESH_OFFSET.md (section 4, option A item 3) for any surface: an upper bound B,
        /// proven, of |sd(x) - distance| over every point x of the triangles of surface, sd the signed distance to input
        /// (positive outside; input closed, outward, without self-intersections, as for MeshOffset). Triangles whose bound
        /// exceeds tolerance are split into 4 for the check, up to maxDepth times. Returns false for invalid arguments
        /// (deviation infinite); otherwise deviation is B and the result says whether B &lt;= tolerance. Not scale-normalized:
        /// the bound uses range-guarded hardware-double certified bounds (MO-1..MO-4, CTB-1/CTB-2) falling back to outward-rounded
        /// intervals outside the normal double range, so it is valid at any scale, while its value depends on the rounding there.
        /// </summary>
        public static bool CertifyBand(DMesh3 input, DMesh3 surface, double distance, double tolerance, out double deviation,
            out int uncertifiedTriangles, int maxDepth = 6, bool parallel = true)
        {
            deviation = double.PositiveInfinity; uncertifiedTriangles = 0;
            if (input == null || surface == null || input.TriangleCount == 0 || double.IsNaN(distance) || double.IsInfinity(distance)
                || !(tolerance >= 0) || maxDepth < 0)
                return false;
            var tree = new DMeshAABBTree3(input, true);
            deviation = band_deviation(input, tree, surface, distance, tolerance, maxDepth, parallel, out uncertifiedTriangles);
            return deviation <= tolerance;
        }

        // Range guards for the hardware-double certified fast paths in CertifyBand (MO-1..MO-4 in bounds.py)
        const double NormalSqLo = 1e-290;
        const double NormalSqHi = 1e290;
        const double NormalLinLo = 1e-140;
        const double NormalLinHi = 1e140;

        // Per-thread reusable traversal state for signed_distance_bounds (zero heap allocations per query; completely reset per call)
        sealed class BandQueryState
        {
            public readonly DMesh3 Mesh;
            public Vector3d C;
            public double ULo, ULoSq;
            public readonly DMeshAABBTree3.TreeTraversal Trav;

            public BandQueryState(DMesh3 mesh)
            {
                Mesh = mesh;
                Trav = new DMeshAABBTree3.TreeTraversal {
                    NextBoxDF = (box, depth) => box_lower_sq(box, C) <= ULoSq,
                    NextTriangleF = (t) => {
                        double lo = triangle_lower_pruned(Mesh, t, C, ULoSq);
                        if (lo < ULo) { ULo = lo; ULoSq = ExactInterval.Up(lo * lo); }
                    }
                };
            }
        }

        static double band_deviation(DMesh3 input, DMeshAABBTree3 tree, DMesh3 surface, double d, double tol, int maxDepth, bool parallel,
            out int uncertified)
        {
            int[] tris = surface.TriangleIndices().ToArray();
            double[] dev = new double[tris.Length];
            bool[] bad = new bool[tris.Length];
            Action<int, BandQueryState> one = (ti, st) => {
                Index3i t = surface.GetTriangle(tris[ti]);
                Vector3d v0 = surface.GetVertex(t.a), v1 = surface.GetVertex(t.b), v2 = surface.GetVertex(t.c);
                bool ok;
                dev[ti] = triangle_deviation(st, tree, v0, v1, v2, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),
                    d, tol, maxDepth, Vector3d.Zero, 0, 0, out ok);
                bad[ti] = !ok;
            };
            if (parallel) {
                gParallel.ForRange(tris.Length, () => new BandQueryState(input), one);
            } else {
                var st = new BandQueryState(input);
                for (int i = 0; i < tris.Length; ++i) one(i, st);
            }
            double m = 0;
            uncertified = 0;
            bool nan = false;
            for (int i = 0; i < tris.Length; ++i) {
                if (double.IsNaN(dev[i])) nan = true;      // (not expected: triangle_deviation maps NaN to infinity)
                else if (dev[i] > m) m = dev[i];
                if (bad[i]) uncertified++;
            }
            return nan ? double.PositiveInfinity : m;
        }

        // the exact point sum_i l_i v_i (barycentric l exact dyadic doubles summing to 1) as an interval box
        static void box_of(Vector3d v0, Vector3d v1, Vector3d v2, Vector3d l, out ExactInterval x, out ExactInterval y, out ExactInterval z)
        {
            ExactInterval l0 = ExactInterval.Of(l.x), l1 = ExactInterval.Of(l.y), l2 = ExactInterval.Of(l.z);
            x = l0 * ExactInterval.Of(v0.x) + l1 * ExactInterval.Of(v1.x) + l2 * ExactInterval.Of(v2.x);
            y = l0 * ExactInterval.Of(v0.y) + l1 * ExactInterval.Of(v1.y) + l2 * ExactInterval.Of(v2.y);
            z = l0 * ExactInterval.Of(v0.z) + l1 * ExactInterval.Of(v1.z) + l2 * ExactInterval.Of(v2.z);
        }

        // upper bound of the distance from c to any point of the box (x, y, z)
        static double box_radius(Vector3d c, ExactInterval x, ExactInterval y, ExactInterval z)
        {
            ExactInterval dx = x - ExactInterval.Of(c.x), dy = y - ExactInterval.Of(c.y), dz = z - ExactInterval.Of(c.z);
            ExactInterval sq = ExactInterval.Square(dx) + ExactInterval.Square(dy) + ExactInterval.Square(dz);
            return sqrt_up(sq.Hi);
        }

        // certified upper bound of |c - (l.x v0 + l.y v1 + l.z v2)| (MO-3 in bounds.py):
        // uses CTB-1 (LengthUpper) at exact corners, range-guarded hardware-double fast path inside (NormalLinLo, NormalLinHi),
        // and falls back to outward-rounded box_of + box_radius outside
        internal static double bary_radius_upper(Vector3d c, Vector3d v0, Vector3d v1, Vector3d v2, Vector3d l)
        {
            if (l.x == 1.0 && l.y == 0.0 && l.z == 0.0) return CertifiedTriangleBounds.LengthUpper(c, v0);
            if (l.x == 0.0 && l.y == 1.0 && l.z == 0.0) return CertifiedTriangleBounds.LengthUpper(c, v1);
            if (l.x == 0.0 && l.y == 0.0 && l.z == 1.0) return CertifiedTriangleBounds.LengthUpper(c, v2);
            double a0x = l.x * v0.x, a1x = l.y * v1.x, a2x = l.z * v2.x, qx = a0x + a1x + a2x;
            double a0y = l.x * v0.y, a1y = l.y * v1.y, a2y = l.z * v2.y, qy = a0y + a1y + a2y;
            double a0z = l.x * v0.z, a1z = l.y * v1.z, a2z = l.z * v2.z, qz = a0z + a1z + a2z;
            double e = (Math.Abs(a0x) + Math.Abs(a1x) + Math.Abs(a2x))
                     + (Math.Abs(a0y) + Math.Abs(a1y) + Math.Abs(a2y))
                     + (Math.Abs(a0z) + Math.Abs(a1z) + Math.Abs(a2z));
            double rx = c.x - qx, ry = c.y - qy, rz = c.z - qz;
            double r2 = rx * rx + ry * ry + rz * rz;
            if (e > NormalLinLo && e < NormalLinHi && r2 < NormalSqHi)
                return Math.Sqrt(r2) * (1.0 + 1e-15) + 5e-16 * e;
            ExactInterval bx, by, bz;
            box_of(v0, v1, v2, l, out bx, out by, out bz);
            return box_radius(c, bx, by, bz);
        }

        static double sqrt_up(double x) { return (x <= 0) ? 0 : ExactInterval.Up(Math.Sqrt(x)); }

        // bound of |sd - d| on the sub-triangle with barycentric corners l0, l1, l2 of (v0, v1, v2); ok if it is <= tol.
        // parentC / parentULo / parentSign: when parentSign != 0 and LengthUpper(c, parentC) < parentULo, the closed segment
        // [parentC, c] lies inside the open ball B(parentC, parentULo) disjoint from input, so IsInside(c) == (parentSign < 0).
        static double triangle_deviation(BandQueryState st, DMeshAABBTree3 tree, Vector3d v0, Vector3d v1, Vector3d v2,
            Vector3d l0, Vector3d l1, Vector3d l2, double d, double tol, int depth,
            Vector3d parentC, double parentULo, int parentSign, out bool ok)
        {
            // any finite point serves as the centre: the rounded barycentric centroid (falling back to box_of midpoints if non-finite)
            Vector3d p0 = l0.x * v0 + l0.y * v1 + l0.z * v2;
            Vector3d p1 = l1.x * v0 + l1.y * v1 + l1.z * v2;
            Vector3d p2 = l2.x * v0 + l2.y * v1 + l2.z * v2;
            Vector3d c = (1.0 / 3.0) * (p0 + p1 + p2);
            if (!MathUtil.IsFinite(c.x) || !MathUtil.IsFinite(c.y) || !MathUtil.IsFinite(c.z)) {
                ExactInterval ax, ay, az, bx, by, bz, cx, cy, cz;
                box_of(v0, v1, v2, l0, out ax, out ay, out az);
                box_of(v0, v1, v2, l1, out bx, out by, out bz);
                box_of(v0, v1, v2, l2, out cx, out cy, out cz);
                c = (1.0 / 3.0) * new Vector3d(0.5 * (ax.Lo + ax.Hi) + 0.5 * (bx.Lo + bx.Hi) + 0.5 * (cx.Lo + cx.Hi),
                    0.5 * (ay.Lo + ay.Hi) + 0.5 * (by.Lo + by.Hi) + 0.5 * (cy.Lo + cy.Hi),
                    0.5 * (az.Lo + az.Hi) + 0.5 * (bz.Lo + bz.Hi) + 0.5 * (cz.Lo + cz.Hi));
            }
            // every point of the sub-triangle is a convex combination of its exact corners: within r of c
            double r = Math.Max(bary_radius_upper(c, v0, v1, v2, l0),
                       Math.Max(bary_radius_upper(c, v0, v1, v2, l1),
                                bary_radius_upper(c, v0, v1, v2, l2)));
            int knownSign = (parentSign != 0 && parentULo < double.PositiveInfinity && CertifiedTriangleBounds.LengthUpper(c, parentC) < parentULo) ? parentSign : 0;
            double sLo, sHi, uLoOut;
            int signOut;
            signed_distance_bounds(st, tree, c, knownSign, out sLo, out sHi, out uLoOut, out signOut);
            // sd is 1-Lipschitz: sd(x) in [sLo - r, sHi + r] on the sub-triangle
            ExactInterval R = ExactInterval.Of(r), D = ExactInterval.Of(d);
            double dev = Math.Max((ExactInterval.Of(sHi) + R - D).Hi, (D - ExactInterval.Of(sLo) + R).Hi);
            if (double.IsNaN(dev)) dev = double.PositiveInfinity;
            // stop where the bound holds, at the depth limit, or where the centre alone (r = 0) is already beyond the
            // tolerance: splitting cannot bring the bound below it there
            double centre = Math.Max((ExactInterval.Of(sHi) - D).Hi, (D - ExactInterval.Of(sLo)).Hi);
            if (dev <= tol || depth == 0 || !(centre <= tol)) {
                ok = dev <= tol;
                return dev;
            }
            Vector3d m01 = 0.5 * (l0 + l1), m12 = 0.5 * (l1 + l2), m20 = 0.5 * (l2 + l0);   // exact: halves of dyadic numbers
            Vector3d nextC = signOut != 0 ? c : parentC;
            double nextULo = signOut != 0 ? uLoOut : parentULo;
            int nextSign = signOut != 0 ? signOut : parentSign;
            bool o0, o1, o2, o3;
            double e0 = triangle_deviation(st, tree, v0, v1, v2, l0, m01, m20, d, tol, depth - 1, nextC, nextULo, nextSign, out o0);
            double e1 = triangle_deviation(st, tree, v0, v1, v2, m01, l1, m12, d, tol, depth - 1, nextC, nextULo, nextSign, out o1);
            double e2 = triangle_deviation(st, tree, v0, v1, v2, m20, m12, l2, d, tol, depth - 1, nextC, nextULo, nextSign, out o2);
            double e3 = triangle_deviation(st, tree, v0, v1, v2, m01, m12, m20, d, tol, depth - 1, nextC, nextULo, nextSign, out o3);
            ok = o0 && o1 && o2 && o3;
            return Math.Max(Math.Max(e0, e1), Math.Max(e2, e3));
        }

        /// <summary>
        /// Proven bounds sLo &lt;= sd(c) &lt;= sHi of the signed distance from c to the closed mesh (tree over it): the distance to an
        /// explicit point of the nearest triangle from above; from below, for every triangle whose box can be that close, the
        /// support bound w . (c - x) / |w| &lt;= |c - x| over its corners x (w from c towards the triangle's nearest point); the sign
        /// from the exact crossing parity where c is provably off the mesh (else [-sHi, sHi]).
        /// </summary>
        internal static void signed_distance_bounds(DMesh3 mesh, DMeshAABBTree3 tree, Vector3d c, out double sLo, out double sHi)
        {
            var st = new BandQueryState(mesh);
            signed_distance_bounds(st, tree, c, 0, out sLo, out sHi, out _, out _);
        }

        static void signed_distance_bounds(BandQueryState st, DMeshAABBTree3 tree, Vector3d c, int knownSign,
            out double sLo, out double sHi, out double uLoOut, out int signOut)
        {
            double dsq;
            int tid = tree.FindNearestTriangle(c, out dsq);
            if (tid == DMesh3.InvalidID) { sLo = double.NegativeInfinity; sHi = double.PositiveInfinity; uLoOut = 0; signOut = 0; return; }
            double uHi = triangle_upper(st.Mesh, tid, c);
            st.C = c;
            st.ULo = uHi;
            st.ULoSq = ExactInterval.Up(uHi * uHi);
            tree.DoTraversal(st.Trav);
            double uLo = st.ULo;
            uLoOut = uLo;
            if (!(uLo > 0)) { sLo = -uHi; sHi = uHi; signOut = 0; return; }
            bool inside = (knownSign != 0) ? (knownSign < 0) : tree.IsInside(c);
            signOut = inside ? -1 : 1;
            if (inside) { sLo = -uHi; sHi = -uLo; }
            else { sLo = uLo; sHi = uHi; }
        }

        // lower bound of the squared distance from c to the box (MO-1 in bounds.py):
        // range-guarded hardware-double fast path with 3e-15*mag slack (>= 8 ulp(mag) + diff/sub roundings),
        // falling back to ExactInterval outside (NormalSqLo, NormalSqHi)
        internal static double box_lower_sq(AxisAlignedBox3d box, Vector3d c)
        {
            double magx = Math.Max(Math.Abs(c.x), Math.Max(Math.Abs(box.Min.x), Math.Abs(box.Max.x)));
            double gx = (c.x < box.Min.x) ? (box.Min.x - c.x) : ((c.x > box.Max.x) ? (c.x - box.Max.x) : 0);
            gx -= 3e-15 * magx;
            if (!(gx > 0)) gx = 0;

            double magy = Math.Max(Math.Abs(c.y), Math.Max(Math.Abs(box.Min.y), Math.Abs(box.Max.y)));
            double gy = (c.y < box.Min.y) ? (box.Min.y - c.y) : ((c.y > box.Max.y) ? (c.y - box.Max.y) : 0);
            gy -= 3e-15 * magy;
            if (!(gy > 0)) gy = 0;

            double magz = Math.Max(Math.Abs(c.z), Math.Max(Math.Abs(box.Min.z), Math.Abs(box.Max.z)));
            double gz = (c.z < box.Min.z) ? (box.Min.z - c.z) : ((c.z > box.Max.z) ? (c.z - box.Max.z) : 0);
            gz -= 3e-15 * magz;
            if (!(gz > 0)) gz = 0;

            if (magx < double.PositiveInfinity && magy < double.PositiveInfinity && magz < double.PositiveInfinity) {
                double s = gx * gx + gy * gy + gz * gz;
                if (s > NormalSqLo && s < NormalSqHi)
                    return s * (1.0 - 1e-15);
                if (s == 0)
                    return 0;
            }
            return box_lower_sq_interval(box, c);
        }

        static double box_lower_sq_interval(AxisAlignedBox3d box, Vector3d c)
        {
            // the tree's boxes are padded by 2^-40 of the root's magnitude against the rounding of their centre / extent form;
            // a further slack of 8 units in the last place of the coordinates covers that form here as well
            double s = 0;
            for (int a = 0; a < 3; ++a) {
                double mag = Math.Max(Math.Abs(c[a]), Math.Max(Math.Abs(box.Min[a]), Math.Abs(box.Max[a])));
                if (!(mag < double.PositiveInfinity)) return 0;
                double slack = 8 * (ExactInterval.Up(mag) - mag);
                double lo = Math.Max(0, Math.Max((ExactInterval.Of(box.Min[a]) - ExactInterval.Of(c[a])).Lo, (ExactInterval.Of(c[a]) - ExactInterval.Of(box.Max[a])).Lo));
                lo = Math.Max(0, ExactInterval.Down(lo - slack));
                s = ExactInterval.Down(s + ExactInterval.Down(lo * lo));
            }
            return (s > 0) ? s : 0;
        }

        // upper bound of the distance from c to the triangle: delegates to CertifiedTriangleBounds.PointTriangleUpper (CTB-2)
        internal static double triangle_upper(DMesh3 mesh, int tid, Vector3d c)
        {
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            Vector3d q, bary;
            DistPoint3Triangle3.DistanceSqr(ref c, ref tri, out q, out bary);
            return CertifiedTriangleBounds.PointTriangleUpper(c, tri.V0, tri.V1, tri.V2, bary.y, bary.z);
        }

        // certified lower bound of squared distance from p to the AABB of (a, b, c) (MO-4 in bounds.py; returns 0 outside normal range or on NaN)
        internal static double tri_aabb_lower_sq(Vector3d a, Vector3d b, Vector3d c, Vector3d p)
        {
            double lox = Math.Min(a.x, Math.Min(b.x, c.x)), hix = Math.Max(a.x, Math.Max(b.x, c.x));
            double gx = (p.x < lox) ? (lox - p.x) : ((p.x > hix) ? (p.x - hix) : 0);
            double loy = Math.Min(a.y, Math.Min(b.y, c.y)), hiy = Math.Max(a.y, Math.Max(b.y, c.y));
            double gy = (p.y < loy) ? (loy - p.y) : ((p.y > hiy) ? (p.y - hiy) : 0);
            double loz = Math.Min(a.z, Math.Min(b.z, c.z)), hiz = Math.Max(a.z, Math.Max(b.z, c.z));
            double gz = (p.z < loz) ? (loz - p.z) : ((p.z > hiz) ? (p.z - hiz) : 0);
            double s = gx * gx + gy * gy + gz * gz;
            if (s > NormalSqLo && s < NormalSqHi
                && !double.IsNaN(lox) && !double.IsNaN(loy) && !double.IsNaN(loz)
                && !double.IsNaN(p.x) && !double.IsNaN(p.y) && !double.IsNaN(p.z))
                return s * (1.0 - 1e-15);
            return 0;
        }

        static double triangle_lower_pruned(DMesh3 mesh, int tid, Vector3d c, double uLoSq)
        {
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            if (tri_aabb_lower_sq(tri.V0, tri.V1, tri.V2, c) >= uLoSq)
                return double.PositiveInfinity;
            return triangle_lower_tri(ref tri, c);
        }

        // lower bound of the distance from c to the triangle: min over its corners x of w . (c - x), divided by |w| (MO-2 in bounds.py)
        internal static double triangle_lower(DMesh3 mesh, int tid, Vector3d c)
        {
            Triangle3d tri = new Triangle3d();
            mesh.GetTriVertices(tid, ref tri.V0, ref tri.V1, ref tri.V2);
            return triangle_lower_tri(ref tri, c);
        }

        static double triangle_lower_tri(ref Triangle3d tri, Vector3d c)
        {
            Vector3d q, bary;
            DistPoint3Triangle3.DistanceSqr(ref c, ref tri, out q, out bary);
            double wx = c.x - q.x, wy = c.y - q.y, wz = c.z - q.z;
            double w2 = wx * wx + wy * wy + wz * wz;
            if (w2 > NormalSqLo && w2 < NormalSqHi) {
                double awx = Math.Abs(wx), awy = Math.Abs(wy), awz = Math.Abs(wz);
                double minNum = double.PositiveInfinity;
                for (int i = 0; i < 3; ++i) {
                    Vector3d x = tri[i];
                    double dx = c.x - x.x, dy = c.y - x.y, dz = c.z - x.z;
                    double dot = wx * dx + wy * dy + wz * dz;
                    double absDot = awx * Math.Abs(dx) + awy * Math.Abs(dy) + awz * Math.Abs(dz);
                    double lo = dot - 1e-15 * absDot;
                    if (!(lo > NormalSqLo && lo < NormalSqHi)) { minNum = 0; break; }
                    if (lo < minNum) minNum = lo;
                }
                if (minNum > NormalSqLo && minNum < NormalSqHi) {
                    double d = minNum / Math.Sqrt(w2);
                    if (d > NormalSqLo && d < NormalSqHi)
                        return d * (1.0 - 1e-15);
                }
            }
            return triangle_lower_interval(ref tri, c, new Vector3d(wx, wy, wz));
        }

        static double triangle_lower_interval(ref Triangle3d tri, Vector3d c, Vector3d w)
        {
            if (!(w.LengthSquared > 0)) return 0;
            ExactInterval wx = ExactInterval.Of(w.x), wy = ExactInterval.Of(w.y), wz = ExactInterval.Of(w.z);
            double num = double.PositiveInfinity;
            for (int i = 0; i < 3; ++i) {
                Vector3d x = tri[i];
                ExactInterval dot = wx * (ExactInterval.Of(c.x) - ExactInterval.Of(x.x)) + wy * (ExactInterval.Of(c.y) - ExactInterval.Of(x.y))
                    + wz * (ExactInterval.Of(c.z) - ExactInterval.Of(x.z));
                num = Math.Min(num, dot.Lo);
            }
            if (!(num > 0)) return 0;
            double norm = sqrt_up((ExactInterval.Square(wx) + ExactInterval.Square(wy) + ExactInterval.Square(wz)).Hi);
            if (!(norm > 0) || double.IsInfinity(norm)) return 0;
            return Math.Max(0, ExactInterval.Down(num / norm));
        }

        // floor(log2 x) for a positive normal double
        static int Exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return (int)((bits >> 52) & 0x7FF) - 1023;
        }

        // 2^k for |k| <= 1022
        static double Pow2(int k)
        {
            return BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
        }
    }
}
