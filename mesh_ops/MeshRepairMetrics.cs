// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// Metrics for judging a repaired (or remeshed, solidified, boolean) mesh against its input, where no
    /// reference result exists (eg Thingi10K):
    ///   - SelfIntersections: exact count of intersecting triangle pairs, including pairs that share a
    ///     vertex or an edge and intersect beyond it (folds lying flat on each other);
    ///   - VolumeIoU: agreement of inside/outside with the input, where "inside" of the input is its
    ///     generalized winding number > 0.5 (Jacobson et al. 2013; also defined for soups and open meshes);
    ///   - certified Hausdorff distances: see MeshHausdorffDistance.
    /// All functions are read-only (the meshes are not modified) and deterministic.
    /// </summary>
    public static class MeshRepairMetrics
    {
        /// <summary>
        /// Number of triangle pairs that intersect, exactly (ExactTriangleIntersection). Pairs that share a
        /// vertex count if they intersect anywhere else; pairs that share an edge count if they overlap
        /// (coplanar on the same side). Degenerate (zero area) triangles are skipped.
        /// Builds an AABB tree and runs in parallel; 0 for a mesh without self-intersections.
        /// </summary>
        public static int SelfIntersections(DMesh3 mesh)
        {
            return SelfIntersectionPairs(mesh).Count;
        }

        /// <summary>
        /// SelfIntersections(mesh), but stops counting once more than limit pairs are found (then returns a value
        /// &gt; limit, not the full count): cheap test whether a mesh has more self-intersections than a limit
        /// </summary>
        public static int SelfIntersectionsAtMost(DMesh3 mesh, int limit)
        {
            return self_intersection_pairs(mesh, limit >= int.MaxValue - 1 ? int.MaxValue : Math.Max(limit, 0) + 1).Count;
        }

        /// <summary> the intersecting triangle pairs (see SelfIntersections), each once, sorted </summary>
        public static List<Index2i> SelfIntersectionPairs(DMesh3 mesh)
        {
            return self_intersection_pairs(mesh, int.MaxValue);
        }

        /// <summary>
        /// SelfIntersections(mesh) &gt; 0, but stops at the first intersecting pair (cheaper for meshes with
        /// self-intersections; the same work for meshes without)
        /// </summary>
        public static bool HasSelfIntersections(DMesh3 mesh)
        {
            return self_intersection_pairs(mesh, 1).Count > 0;
        }

        // stop once maxPairs pairs are found (then at least maxPairs pairs are returned if there are as many)
        static List<Index2i> self_intersection_pairs(DMesh3 mesh, int maxPairs)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            var pairs = new List<Index2i>();
            object lockObj = new object();
            int found = 0;
            int[] tris = new List<int>(mesh.TriangleIndices()).ToArray();
            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                if (System.Threading.Volatile.Read(ref found) >= maxPairs) return;
                int ta = tris[k];
                Triangle3d A = triangle(mesh, ta);
                if (ExactTriangleIntersection.IsDegenerate(ref A)) return;
                AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                Index3i ia = mesh.GetTriangle(ta);
                List<Index2i> local = null;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => b.Intersects(box) && (local == null || local.Count < maxPairs),
                    NextTriangleF = (tb) => {
                        if (tb <= ta || (local != null && local.Count >= maxPairs)) return;
                        Triangle3d B = triangle(mesh, tb);
                        if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                        if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, mesh.GetTriangle(tb), ref B)) {
                            if (local == null) local = new List<Index2i>();
                            local.Add(new Index2i(ta, tb));
                        }
                    }
                });
                if (local != null) {
                    lock (lockObj) pairs.AddRange(local);
                    System.Threading.Interlocked.Add(ref found, local.Count);
                }
            });
            pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return pairs;
        }

        /// <summary>
        /// interior edges whose two triangles run through them in the same direction (the orientation is not
        /// consistent there; 0 for a consistently oriented mesh)
        /// </summary>
        public static int InconsistentlyOrientedEdges(DMesh3 mesh)
        {
            int n = 0;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID)
                    continue;
                Index2i ev = mesh.GetEdgeV(eid);
                Index3i ta = mesh.GetTriangle(et.a), tb = mesh.GetTriangle(et.b);
                if ((IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref ta) != DMesh3.InvalidID)
                    == (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref tb) != DMesh3.InvalidID))
                    n++;
            }
            return n;
        }

        /// <summary> interior edges whose triangles fold over by more than maxAngle degrees (non-degenerate triangles) </summary>
        public static List<int> FoldedEdges(DMesh3 mesh, double maxAngle = 175)
        {
            double cos = Math.Cos(maxAngle * MathUtil.Deg2Rad);
            var result = new List<int>();
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b < 0) continue;
                Triangle3d A = triangle(mesh, et.a), B = triangle(mesh, et.b);
                if (ExactTriangleIntersection.IsDegenerate(ref A) || ExactTriangleIntersection.IsDegenerate(ref B)) continue;
                if (mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b)) < cos)
                    result.Add(eid);
            }
            return result;
        }

        static Triangle3d triangle(DMesh3 mesh, int tid)
        {
            Index3i t = mesh.GetTriangle(tid);
            return new Triangle3d(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c));
        }

        /// <summary>
        /// Intersection over union of the solids of input and result, from samples (deterministic): a point is
        /// inside the input where the generalized winding number of its oriented copy (WindingContouring.OrientedCopy:
        /// inside-out parts flipped, as the solidify fallback does) is > 0.5, inside the result likewise. 1 = same
        /// solid. Also returns the parts of the input solid missing in the result (eg lost thin walls) and the result
        /// solid not in the input (eg filled cavities), relative to the input solid. NaN if the input encloses no
        /// volume (eg a single open sheet), 0 if the input has no triangles, and 0 (missing and extra NaN) if the
        /// input's solid is too small to be hit by the samples at all (then the result's solid is far larger).
        /// Estimated from samples random points (fixed seed, so repeatable) in the bounds of the triangles of both
        /// meshes, or, for meshes with up to MaxSampledComponents components, only near the components (where the
        /// winding number can reach 0.5), so components far apart are sampled as densely as a single one; small
        /// differences are sampling noise.
        /// </summary>
        public static double VolumeIoU(DMesh3 input, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            missing = extra = 0;
            if (input.TriangleCount == 0)
                return 0;
            double scale = normalizing_scale(input, result);
            if (scale != 1)
                return VolumeIoU(scaled(input, scale), scaled(result, scale), out missing, out extra, samples, seed);
            // the solid the solidify fallback sees: inside-out inputs (winding number -1 inside) count as
            // their solid, not as none
            input = WindingContouring.OrientedCopy(input);
            Func<Vector3d, bool> inside = winding_inside(input);
            double iou = volume_iou(inside, sample_region(input, result), result, out missing, out extra, samples, seed);
            if (double.IsNaN(iou) && has_solid(inside, input, seed)) {
                missing = extra = double.NaN;
                return 0;
            }
            return iou;
        }

        /// <summary>
        /// Is VolumeIoU(input, result) at least minIoU? Samples are classified in rounds (samples / 32, then
        /// doubling), and after each round the share of the union that is in only one of the solids gets a
        /// confidence interval (Wilson score, z standard deviations); the answer is given as soon as the interval
        /// lies on one side of 1 - minIoU and the input's solid has been found. An early answer differs from the one
        /// with all samples only by chance, about the probability of a z-sigma deviation (z = 4: about 3e-5, times the
        /// number of rounds; with cells, two intervals share it).
        /// If the oriented input and the result are both winding-closed (ExactMeshBoolean.IsWindingClosed), the
        /// samples are drawn only from the cells of an octree that the surfaces touch (VolumeSampleCells); the cells
        /// no triangle touches lie inside or outside each solid entirely and count with their exact volume. This
        /// estimates the same IoU with less variance and fewer samples (docs/design/VOLUME_CHECK_SAMPLING.md).
        /// Otherwise (open meshes) the samples are those of VolumeIoU: with all of them (results close to minIoU) the
        /// answer and iou are those of VolumeIoU.
        /// iou is the estimate from the samples used; NaN (and true) if the input encloses no volume,
        /// as VolumeIoU (no solid to compare with); 0 (and false) if its solid is too small to be hit by the samples.
        /// Typically 10-30x cheaper than VolumeIoU for results that clearly agree with the input.
        /// </summary>
        public static bool VolumeIoUAtLeast(DMesh3 input, DMesh3 result, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            return VolumeIoUAtLeast(new SolidReference(input), SolidReference.Unoriented(result), minIoU, out iou, samples, seed, z);
        }

        /// <summary>
        /// The input's solid as the metrics see it (its oriented copy and a winding number tree), built once for several
        /// results (eg the stages of MeshRobustRepair)
        /// </summary>
        internal sealed class SolidReference
        {
            internal readonly DMesh3 Mesh;
            internal readonly Func<Vector3d, bool> Inside;
            /// <summary> the winding number tree behind Inside (null: none, eg for no triangles or a given inside test) </summary>
            internal readonly DMeshAABBTree3 Tree;
            int windingClosed = -1;
            /// <summary> ExactMeshBoolean.IsWindingClosed(Mesh), computed once; false without a tree </summary>
            internal bool WindingClosed
            {
                get {
                    if (windingClosed < 0)
                        windingClosed = Tree != null && Mesh.TriangleCount > 0 && ExactMeshBoolean.IsWindingClosed(Mesh) ? 1 : 0;
                    return windingClosed == 1;
                }
            }
            List<AxisAlignedBox3d> componentBoxes;
            /// <summary> boxes of the closed connected components of Mesh (computed once) </summary>
            internal List<AxisAlignedBox3d> ComponentBoxes()
            {
                if (componentBoxes == null) {
                    var comps = new MeshConnectedComponents(Mesh);
                    comps.FindConnectedT();
                    componentBoxes = comps.Components.Where(c => c.Indices.All(t => {
                        Index3i te = Mesh.GetTriEdges(t);
                        return !Mesh.IsBoundaryEdge(te.a) && !Mesh.IsBoundaryEdge(te.b) && !Mesh.IsBoundaryEdge(te.c);
                    })).Select(c => {
                        var box = AxisAlignedBox3d.Empty;
                        foreach (int tid in c.Indices) box.Contain(Mesh.GetTriBounds(tid));
                        return box;
                    }).ToList();
                }
                return componentBoxes;
            }

            internal SolidReference(DMesh3 input) : this(input, true) { }

            SolidReference(DMesh3 input, bool orient)
            {
                Mesh = input.TriangleCount > 0 && orient ? WindingContouring.OrientedCopy(input) : input;
                if (Mesh.TriangleCount > 0)
                    Inside = winding_inside(Mesh, out Tree);
                else
                    Inside = (p) => false;
            }

            /// <summary> the solid of mesh as it is (not its oriented copy), as SolidInside sees it: for results </summary>
            internal static SolidReference Unoriented(DMesh3 mesh)
            {
                return new SolidReference(mesh, false);
            }
            /// <summary> mesh (its components) with a given inside test (for tests) </summary>
            internal SolidReference(DMesh3 mesh, Func<Vector3d, bool> inside)
            {
                Mesh = mesh;
                Inside = inside;
            }
        }

        /// <summary> inside test of a mesh's solid (generalized winding number &gt; 0.5), thread-safe; false everywhere for no triangles </summary>
        internal static Func<Vector3d, bool> SolidInside(DMesh3 mesh)
        {
            return mesh.TriangleCount > 0 ? winding_inside(mesh) : (p) => false;
        }

        /// <summary> what one VolumeIoUAtLeast did (for tests and the benchmark) </summary>
        internal sealed class VolumeCheckStats
        {
            /// <summary> the cells were used (both meshes winding-closed); otherwise the region of sample_region </summary>
            internal bool Cells;
            /// <summary> the cells were built but sampled more volume than the region, which was used instead </summary>
            internal bool CellsRejected;
            internal int Samples, UnionSamples, SampledCells, ChildTests, Depth, Unclassified, WindingEvaluations;
            /// <summary> volume of the sampled region, and of the cells known to be in the union (exactly) </summary>
            internal double SampledVolume, KnownUnion, KnownBoth;
            /// <summary> confidence interval of the IoU when the samples stopped (z standard deviations) </summary>
            internal double IoULow, IoUHigh;
            internal long BuildTicks, SampleTicks;
        }

        /// <summary>
        /// The power of two that brings the extent of the triangles of a and b into [2^-300, 2^300] when it lies outside,
        /// otherwise 1. The volume checks compute volumes (the cube of the extent overflows above about 5e102 and
        /// underflows below about 1e-103) and far points at a few times the extent. Outside that range their IoU was
        /// NaN, which passed as "no solid" (a result that lost most of a solid of size 1e160 was accepted), and near
        /// 1e308 the far points were infinite and the exact predicates threw. Scaling by a power of two is exact (the
        /// same inside decisions, the same IoU as at the scaled size; the checks give the same IoU for 2^k scales),
        /// except for coordinates that become subnormal (below about 2^-1322 times the extent). 1 if a bound is not
        /// finite (a NaN coordinate may not show in the bounds; it stays NaN), or if scaling up would make the
        /// coordinates not finite (a tiny extent far from the origin).
        /// </summary>
        internal static double normalizing_scale(DMesh3 a, DMesh3 b)
        {
            AxisAlignedBox3d box = TriangleBounds(a);
            if (b != null && b.TriangleCount > 0)
                box.Contain(TriangleBounds(b));
            double largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
            if (!(largest <= double.MaxValue))
                return 1;
            double ext = box.MaxDim;
            double low = BitConverter.Int64BitsToDouble((long)(1023 - 300) << 52), high = BitConverter.Int64BitsToDouble((long)(1023 + 300) << 52);
            if (ext >= low && ext <= high)
                return 1;
            // (an extent that overflows: the largest coordinate's exponent)
            double r = ext > 0 && ext <= double.MaxValue ? ext : largest;
            if (!(r > 0))
                return 1;
            // as little as needed: the extent into [2^299, 2^300) or [2^-300, 2^-299), so small parts far from the large
            // ones keep their coordinates normal (scaled to [1, 2), a unit sphere next to a part of size 1e308 became
            // subnormal and lost its shape)
            int k = exponent(r);
            int e = k > 0 ? 299 - k : -300 - k;
            double s = BitConverter.Int64BitsToDouble((long)(1023 + e) << 52);
            // (scaling up a tiny extent far from the origin, eg x = 2^1000 with an extent of 2^-400 in y, would overflow the
            // coordinates; scaling down never does, also for a mesh far from the origin; the margin of 2^6 keeps the far
            // points of the exact winding finite)
            if (s > 1 && !(largest * s <= double.MaxValue / 64))
                return 1;
            return s;
        }

        /// <summary>
        /// The power of two (a normal double) that brings the largest extent of box (or, if that is not finite, its
        /// largest coordinate) to about 1 (into [1, 4), or below 1 for an extent below 2^-1022) when the extent lies
        /// outside [2^lowExp, 2^highExp], otherwise 1 (also for an empty box or one with a coordinate that is not
        /// finite). Exact scaling (see normalizing_scale). The scaled coordinates themselves can overflow (a tiny
        /// extent far from the origin): scale differences of coordinates, not coordinates
        /// </summary>
        internal static double unit_scale(AxisAlignedBox3d box, int lowExp, int highExp)
        {
            if (!(box.Max.x >= box.Min.x && box.Max.y >= box.Min.y && box.Max.z >= box.Min.z))
                return 1;
            double largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
            if (!(largest <= double.MaxValue))
                return 1;
            double ext = box.MaxDim;
            if (ext >= BitConverter.Int64BitsToDouble((long)(1023 + lowExp) << 52) && ext <= BitConverter.Int64BitsToDouble((long)(1023 + highExp) << 52))
                return 1;
            double r = ext > 0 && ext <= double.MaxValue ? ext : largest;
            if (!(r > 0))
                return 1;
            // (2^e normal: e in [-1022, 1023]; an extent of 2^1023 or more is scaled into [2, 4))
            int e = Math.Max(-1022, Math.Min(1023, -exponent(r)));
            return BitConverter.Int64BitsToDouble((long)(1023 + e) << 52);
        }

        /// <summary>
        /// The power of two that brings the extent of the triangles of mesh into [1, 4) when it lies outside
        /// [2^-100, 2^100] (where the squares of lengths and the products of areas overflow or underflow, and an
        /// absolute threshold like 1e-12 * diagonal^2 is infinite or 0), else 1. Also 1 if scaling the coordinates would
        /// overflow or leave the normal range (a tiny extent far from the origin, subnormal coordinates): then the
        /// caller works on the mesh as it is. A 2^k scale of the vertices is exact, so an operation run on the scaled
        /// mesh and scaled back (ScaleVertices) gives the unit-scale result, times 2^k, bit for bit.
        /// </summary>
        internal static double unit_scale_for_mesh(DMesh3 mesh)
        {
            AxisAlignedBox3d box = TriangleBounds(mesh);
            double s = unit_scale(box, -100, 100);
            if (s == 1)
                return 1;
            double largest = Math.Max(box.Min.MaxAbs, box.Max.MaxAbs);
            if (!(largest * s <= 1e300))
                return 1;
            // (exact both ways only if no non-zero coordinate is subnormal before or after scaling)
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                for (int k = 0; k < 3; ++k) {
                    double x = Math.Abs(p[k]);
                    if (x != 0 && (x < 2.2250738585072014e-308 || x * s < 2.2250738585072014e-308))
                        return 1;
                }
            }
            return s;
        }

        /// <summary> the vertices of mesh times s (a power of two from unit_scale_for_mesh: exact) </summary>
        internal static void ScaleVertices(DMesh3 mesh, double s)
        {
            foreach (int v in mesh.VertexIndices())
                mesh.SetVertex(v, s * mesh.GetVertex(v));
        }

        // exponent of x > 0 (finite): x in [2^e, 2^(e+1))
        static int exponent(double x)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            if (e != 0)
                return e - 1023;
            bits = BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0);    // 2^64 (subnormal: exact)
            return (int)((bits >> 52) & 0x7FF) - 1023 - 64;
        }

        // a copy with every coordinate times s (a power of two)
        static DMesh3 scaled(DMesh3 mesh, double s)
        {
            if (mesh == null)
                return null;
            var m = new DMesh3(mesh);
            foreach (int v in m.VertexIndices())
                m.SetVertex(v, s * m.GetVertex(v));
            return m;
        }

        /// <summary>
        /// VolumeIoUAtLeast of the solids of input and result (result: SolidReference.Unoriented, as SolidInside). If
        /// both meshes are winding-closed, the samples are drawn only from the cells near the surfaces
        /// (VolumeSampleCells), and the cells known to be inside a solid count with their exact volume; otherwise
        /// from sample_region. useCells = false: always sample_region (for benchmarks, an A/B in one build, and tests).
        /// </summary>
        internal static bool VolumeIoUAtLeast(SolidReference input, SolidReference result, double minIoU, out double iou, int samples = 100000,
            int seed = 1, double z = 4, VolumeCheckStats stats = null, bool useCells = true, int childBudget = VolumeSampleCells.DefaultChildBudget)
        {
            iou = 0;
            if (input.Mesh.TriangleCount == 0)
                return iou >= minIoU;
            double scale = normalizing_scale(input.Mesh, result.Mesh);
            if (scale != 1)
                return VolumeIoUAtLeast(SolidReference.Unoriented(scaled(input.Mesh, scale)), SolidReference.Unoriented(scaled(result.Mesh, scale)),
                    minIoU, out iou, samples, seed, z, stats, useCells, childBudget);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            bool atLeast;
            if (useCells && result.Mesh.TriangleCount > 0 && input.WindingClosed && result.WindingClosed) {
                AxisAlignedBox3d root = TriangleBounds(input.Mesh);
                root.Contain(TriangleBounds(result.Mesh));
                root.Expand(0.01 * root.DiagonalLength);
                var cells = new VolumeSampleCells(input.Mesh, input.Tree, result.Mesh, result.Tree, root, childBudget);
                // the cells are kept only if they sample less volume than the region of the components' boxes: with small
                // components far apart, the cells at the depth the budget allows can be far larger
                SampleRegion region = sample_region(input.Mesh, result.Mesh);
                if (cells.SampledVolume > region.Volume()) {
                    if (stats != null) {
                        stats.CellsRejected = true;
                        stats.BuildTicks = timer.ElapsedTicks;
                    }
                    timer.Restart();
                    atLeast = volume_iou_at_least(input.Inside, region, null, result.Inside, minIoU, out iou, samples, seed, z, stats);
                    goto done;
                }
                if (stats != null) {
                    stats.Cells = true;
                    stats.SampledCells = cells.Sampled.Count;
                    stats.ChildTests = cells.ChildTests;
                    stats.Depth = cells.Depth;
                    stats.Unclassified = cells.Unclassified;
                    stats.WindingEvaluations = cells.WindingEvaluations;
                    stats.SampledVolume = cells.SampledVolume;
                    stats.KnownUnion = cells.KnownUnion;
                    stats.KnownBoth = cells.KnownBoth;
                    stats.BuildTicks = timer.ElapsedTicks;
                }
                timer.Restart();
                atLeast = volume_iou_at_least(input.Inside, null, cells, result.Inside, minIoU, out iou, samples, seed, z, stats);
            } else {
                SampleRegion region = sample_region(input.Mesh, result.Mesh);
                if (stats != null)
                    stats.BuildTicks = timer.ElapsedTicks;
                timer.Restart();
                atLeast = volume_iou_at_least(input.Inside, region, null, result.Inside, minIoU, out iou, samples, seed, z, stats);
            }
        done:
            if (stats != null)
                stats.SampleTicks = timer.ElapsedTicks;
            if (double.IsNaN(iou) && has_solid(input.Inside, input.Mesh, seed)) {
                iou = 0;
                return iou >= minIoU;
            }
            return atLeast;
        }

        /// <summary> as above, against a reference solid given by its inside test (see VolumeIoU), sampled in box (expanded by 1%) </summary>
        public static bool VolumeIoUAtLeast(Func<Vector3d, bool> referenceInside, AxisAlignedBox3d box, DMesh3 result, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            box.Expand(0.01 * box.DiagonalLength);
            return volume_iou_at_least(referenceInside, new SampleRegion(box), null, SolidInside(result), minIoU, out iou, samples, seed, z, null);
        }

        // erfc(x) for x >= 0, fractional error below 1.2e-7 (Chebyshev fit; W. H. Press et al., Numerical Recipes, 6.2)
        static double erfc(double x)
        {
            double t = 1 / (1 + 0.5 * x);
            return t * Math.Exp(-x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806
                + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
        }

        // the z of each of two intervals that together miss at most as often as one interval at z (Bonferroni: each gets
        // half of the two-sided tail probability erfc(z / sqrt 2)); z = 4 gives about 4.17
        internal static double BonferroniZ(double z)
        {
            double target = 0.5 * erfc(z / Math.Sqrt(2));
            double lo = z, hi = z + 4;
            for (int i = 0; i < 100; ++i) {
                double mid = 0.5 * (lo + hi);
                if (erfc(mid / Math.Sqrt(2)) > target) lo = mid; else hi = mid;
            }
            return hi;
        }

        // Wilson score interval of a share k / n, z standard deviations
        internal static void wilson(int k, int count, double z, out double lo, out double hi)
        {
            double n = count, ph = k / n, z2 = z * z;
            double center = (ph + z2 / (2 * n)) / (1 + z2 / n);
            double half = z * Math.Sqrt(ph * (1 - ph) / n + z2 / (4 * n * n)) / (1 + z2 / n);
            lo = center - half;
            hi = center + half;
        }

        // The samples come from region, or from the sampled cells of cells (then the known cells count with their
        // volume). With cells, the share of the union in only one solid is
        //   p = (Ko + S u q) / (K + S u),
        // K the known volume of the union, Ko its part in only one solid, S the sampled volume, u the share of the
        // samples in the union and q the share of those in only one solid. u and q get Wilson intervals; p is
        // monotone in q (for u > 0) and in u (for fixed q), so its bounds are at the corners of the two intervals. The
        // answer is given when they lie on one side of 1 - minIoU. Both intervals use BonferroniZ(z): together they miss
        // at most as often as one interval at z, the rule of the region path. Without known volume (K = 0), p = q:
        // only q's interval is used, at z, exactly as on the region path.
        // the interval of p = 1 - IoU after done samples, union of them in the union, onlyOne in only one solid; K, Ko the
        // known volume of the union and of its part in only one solid, S the sampled volume (see below); z for one
        // interval, BonferroniZ(z) for each of the two when K > 0
        internal static void share_interval(int done, int union, int onlyOne, double K, double Ko, double S, double z, out double pLow, out double pHigh)
        {
            double zEach = K > 0 ? BonferroniZ(z) : z;
            double qLow = 0, qHigh = 1;
            if (union > 0)
                wilson(onlyOne, union, zEach, out qLow, out qHigh);
            if (K == 0) {
                // Wilson score interval of the share p of the union in only one solid (1 - IoU)
                pLow = qLow;
                pHigh = qHigh;
                return;
            }
            double uLow, uHigh;
            wilson(union, done, zEach, out uLow, out uHigh);
            uLow = Math.Max(uLow, 0); uHigh = Math.Min(uHigh, 1);
            qLow = Math.Max(qLow, 0); qHigh = Math.Min(qHigh, 1);
            pLow = double.MaxValue; pHigh = double.MinValue;
            foreach (double uu in new[] { uLow, uHigh })
                foreach (double qq in new[] { qLow, qHigh }) {
                    double pc = (Ko + S * uu * qq) / (K + S * uu);
                    pLow = Math.Min(pLow, pc);
                    pHigh = Math.Max(pHigh, pc);
                }
        }

        static bool volume_iou_at_least(Func<Vector3d, bool> referenceInside, SampleRegion region, VolumeSampleCells cells, Func<Vector3d, bool> resultInside,
            double minIoU, out double iou, int samples, int seed, double z, VolumeCheckStats stats)
        {
            double K = 0, Ko = 0, Kb = 0, S = 0, knownInput = 0;
            if (cells != null) {
                K = cells.KnownUnion;
                Kb = cells.KnownBoth;
                Ko = cells.KnownOnlyA + cells.KnownOnlyB;
                knownInput = cells.KnownBoth + cells.KnownOnlyA;
                S = cells.SampledVolume;
                if (cells.Sampled.Count == 0)
                    samples = 0;
            }
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            int[] cls = new int[samples];
            int done = 0, both = 0, onlyOne = 0, inputSolid = 0;
            double maxShare = 1 - minIoU;
            int round = Math.Max(1, samples / 32);
            double pLow = 0, pHigh = 1;
            while (samples > 0) {
                int end = Math.Min(samples, done + round);
                for (int k = done; k < end; ++k)        // same sequence as VolumeIoU (on the region path)
                    pts[k] = cells != null ? cells.Next(rnd) : region.Next(rnd);
                gParallel.ForEach(Interval1i.FromToInclusive(done, end - 1), (k) => {
                    bool a = referenceInside(pts[k]), b = resultInside(pts[k]);
                    cls[k] = (a ? 1 : 0) | (b ? 2 : 0);
                });
                for (int k = done; k < end; ++k) {
                    if (cls[k] == 3) both++; else if (cls[k] != 0) onlyOne++;
                    if ((cls[k] & 1) != 0) inputSolid++;
                }
                done = end;
                round = done;
                int union = both + onlyOne;
                // no sample (or known cell) in the input's solid yet: it may be small rather than absent, which only
                // all samples (and has_solid) can tell
                bool decidable = (union > 0 || K > 0) && (inputSolid > 0 || knownInput > 0);
                if (decidable)
                    share_interval(done, union, onlyOne, K, Ko, S, z, out pLow, out pHigh);
                if (done == samples)
                    break;
                if (decidable && (pHigh < maxShare || pLow > maxShare))
                    break;
            }
            if (stats != null) {
                stats.Samples = done;
                stats.UnionSamples = both + onlyOne;
                stats.IoULow = 1 - pHigh;
                stats.IoUHigh = 1 - pLow;
            }
            if (inputSolid == 0 && knownInput == 0) {
                iou = double.NaN;       // no reference solid (eg a single open sheet): not defined
                return true;
            }
            if (cells == null)
                iou = (double)both / (both + onlyOne);
            else
                iou = (Kb + (done > 0 ? S * both / done : 0)) / (K + (done > 0 ? S * (both + onlyOne) / done : 0));
            return iou >= minIoU;
        }

        /// <summary>
        /// As above, against a reference solid given by its inside test (eg the exact boolean of the
        /// inputs' winding numbers), sampled in box (expanded by 1%). NaN if no sample is inside the reference.
        /// referenceInside is called in parallel and must be thread-safe.
        /// </summary>
        public static double VolumeIoU(Func<Vector3d, bool> referenceInside, AxisAlignedBox3d box, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            box.Expand(0.01 * box.DiagonalLength);
            return volume_iou(referenceInside, new SampleRegion(box), result, out missing, out extra, samples, seed);
        }

        static double volume_iou(Func<Vector3d, bool> referenceInside, SampleRegion region, DMesh3 result, out double missing, out double extra, int samples, int seed)
        {
            return volume_iou(referenceInside, region, SolidInside(result), out missing, out extra, samples, seed, false);
        }

        // the IoU of the solids given by both inside tests, from samples points of region (Random(seed)); serial: no
        // parallel loop (the same result)
        internal static double volume_iou(Func<Vector3d, bool> referenceInside, SampleRegion region, Func<Vector3d, bool> resultInside,
            out double missing, out double extra, int samples, int seed, bool serial)
        {
            missing = extra = 0;
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            for (int k = 0; k < samples; ++k)
                pts[k] = region.Next(rnd);
            int[] cls = new int[samples];
            gParallel.BlockStartEnd(0, samples - 1, (start, end) => {
                for (int k = start; k <= end; ++k) {
                    bool a = referenceInside(pts[k]), b = resultInside(pts[k]);
                    cls[k] = (a ? 1 : 0) | (b ? 2 : 0);
                }
            }, 256, serial);
            int both = 0, onlyIn = 0, onlyOut = 0;
            foreach (int c in cls) {
                if (c == 3) both++; else if (c == 1) onlyIn++; else if (c == 2) onlyOut++;
            }
            int inputSolid = both + onlyIn;
            if (inputSolid > 0) {
                missing = (double)onlyIn / inputSolid;
                extra = (double)onlyOut / inputSolid;
            }
            if (inputSolid == 0)
                return double.NaN;      // no reference solid (eg a single open sheet): not defined
            int union = both + onlyIn + onlyOut;
            return (double)both / union;
        }

        static Func<Vector3d, bool> winding_inside(DMesh3 mesh)
        {
            DMeshAABBTree3 tree;
            return winding_inside(mesh, out tree);
        }

        static Func<Vector3d, bool> winding_inside(DMesh3 mesh, out DMeshAABBTree3 tree)
        {
            var t = new DMeshAABBTree3(mesh, true);
            t.FastWindingNumber(Vector3d.Zero);    // cache before parallel use
            tree = t;
            return (p) => t.FastWindingNumber(p) > 0.5;
        }

        // Does the input (with no sample of the IoU in its solid) enclose a volume after all, too small to be hit?
        // Sampled only near the input itself.
        internal static bool has_solid(Func<Vector3d, bool> inside, DMesh3 input, int seed)
        {
            SampleRegion region = sample_region(input, null);
            var rnd = new Random(seed);
            for (int k = 0; k < 4096; ++k)
                if (inside(region.Next(rnd)))
                    return true;
            return false;
        }

        /// <summary>
        /// Closed connected components of the input whose solid the result lost (the volume IoU does not notice a part below
        /// 1 - IoU of the volume, eg a screw of an assembly or a small separate shell). For each component, points are
        /// sampled in its box (up to 1024, 256 if none of them lies in the solid) until 32 lie in the input's solid (winding number of its oriented copy
        /// &gt; 0.5, as VolumeIoU); the component is lost if fewer than minCoverage of them lie in the result's solid.
        /// Open components (sheets, soup triangles: near them the input's solid is that of the parts around, which
        /// gave false alarms on Thingi10K) and components with fewer than 8 such points (walls thinner than about
        /// 1/100 of their box) are not judged, nor components whose box diagonal is below MinComponentDiagonal of the input's (specks that
        /// the repair removes on purpose, eg below MeshAutoRepair.MinEdgeLengthTol, and that a fallback would lose as
        /// well). At most maxComponents components, the largest boxes first (beyond that the smallest are not judged).
        /// "In the input's solid" is the solid of the whole input, so a box that holds other parts as well is judged by
        /// them too (a lost part can then count as kept). Deterministic (seed).
        /// </summary>
        /// <summary> LostComponents does not judge components whose box diagonal is below this fraction of the input's </summary>
        public const double MinComponentDiagonal = 1e-3;

        public static int LostComponents(DMesh3 input, DMesh3 result, double minCoverage = 0.5, int maxComponents = 1000, int seed = 1)
        {
            return LostComponents(new SolidReference(input), SolidInside(result), minCoverage, maxComponents, seed);
        }

        internal static int LostComponents(SolidReference reference, Func<Vector3d, bool> inResult, double minCoverage = 0.5, int maxComponents = 1000, int seed = 1,
            bool relativeToLargest = false)
        {
            DMesh3 input = reference.Mesh;
            if (input.TriangleCount == 0)
                return 0;
            Func<Vector3d, bool> inInput = reference.Inside;
            // (relativeToLargest: relative to the largest connected component (open ones too), not to all triangles, whose diagonal grows with the
            // distance between components: a part far from the rest would make every near part too small to be judged)
            List<AxisAlignedBox3d> all = reference.ComponentBoxes();
            double minDiagonal = MinComponentDiagonal * (relativeToLargest ? LargestComponentDiagonal(input) : TriangleBounds(input).DiagonalLength);
            var boxes = all.Where(b => b.Volume > 0 && b.DiagonalLength >= minDiagonal)
                .OrderByDescending(b => b.Volume).Take(maxComponents).ToList();
            int[] lost = new int[boxes.Count];
            gParallel.ForEach(Interval1i.Range(boxes.Count), (k) => {
                var rnd = new Random(seed + 7919 * k);
                AxisAlignedBox3d box = boxes[k];
                int inside = 0, covered = 0;
                for (int tries = 0; tries < 1024 && inside < 32 && (tries < 256 || inside > 0); ++tries) {
                    Vector3d p = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
                    if (inInput(p) == false) continue;
                    inside++;
                    if (inResult(p)) covered++;
                }
                if (inside >= 8 && covered < minCoverage * inside)
                    lost[k] = 1;
            });
            return lost.Sum();
        }

        /// <summary>
        /// VolumeIoU and VolumeIoUAtLeast sample only near the components if each mesh has at most this many
        /// connected components (more: in the bounds of all triangles)
        /// </summary>
        public const int MaxSampledComponents = 64;

        // The region the samples are taken from: the bounds of the triangles (not the vertices: unused ones far away
        // would leave the solid without samples) of both meshes, expanded by 1%. For meshes with at most
        // MaxSampledComponents connected components, only the part of it near the components: around a component of
        // area A, its box expanded by r with A / (4 pi r^2) = 0.4 / N for N components. The winding number of a
        // surface of area A at distance at least r from a point is at most A / (4 pi r^2) (solid angle <= area /
        // distance^2), so outside all expanded boxes the winding number of the mesh is below 0.4 (margin for the
        // approximation of the fast winding number): no point there is in either solid, and leaving it out does not
        // change the IoU, only concentrates the samples (components far apart, eg parts of an assembly or buildings
        // of a city model, are then hit as often as a single one).
        internal static SampleRegion sample_region(DMesh3 input, DMesh3 result)
        {
            AxisAlignedBox3d clip = TriangleBounds(input);
            if (result != null && result.TriangleCount > 0)
                clip.Contain(TriangleBounds(result));
            clip.Expand(0.01 * clip.DiagonalLength);
            var boxes = new List<AxisAlignedBox3d>();
            if (component_cover(input, clip, boxes) == false || (result != null && component_cover(result, clip, boxes) == false))
                return new SampleRegion(clip);
            // boxes inside others add nothing to the union (and identical ones would only lower the acceptance rate)
            var kept = new List<AxisAlignedBox3d>();
            for (int i = 0; i < boxes.Count; ++i) {
                bool inside = false;
                for (int j = 0; j < boxes.Count && inside == false; ++j)
                    inside = j != i && boxes[j].Contains(boxes[i]) && (boxes[i].Contains(boxes[j]) == false || j < i);
                if (inside == false)
                    kept.Add(boxes[i]);
            }
            return kept.Count == 0 ? new SampleRegion(clip) : new SampleRegion(kept);
        }

        // adds the boxes around the components of mesh (see sample_region), clipped; false if it has too many components
        static bool component_cover(DMesh3 mesh, AxisAlignedBox3d clip, List<AxisAlignedBox3d> boxes)
        {
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            int n = comps.Count;
            if (n > MaxSampledComponents)
                return false;
            foreach (var comp in comps.Components) {
                double area = 0;
                var box = AxisAlignedBox3d.Empty;
                foreach (int tid in comp.Indices) {
                    area += mesh.GetTriArea(tid);
                    box.Contain(mesh.GetTriBounds(tid));
                }
                if (!(area > 0))
                    continue;       // encloses nothing
                box.Expand(Math.Sqrt(n * area / (1.6 * Math.PI)));
                box = box.Intersect(clip);
                if (box.Volume > 0)
                    boxes.Add(box);
            }
            return true;
        }

        /// <summary>
        /// Bounds of the vertices of the triangles (CachedBounds includes unused vertices, which can lie far away and
        /// make thresholds relative to the diagonal meaningless); empty for a mesh without triangles
        /// </summary>
        public static AxisAlignedBox3d TriangleBounds(DMesh3 mesh)
        {
            var box = AxisAlignedBox3d.Empty;
            foreach (int tid in mesh.TriangleIndices())
                box.Contain(mesh.GetTriBounds(tid));
            return box;
        }

        // the connected components (vertices joined by triangles, union-find; the smaller root wins, so the same mesh
        // gives the same components) and the bounds of each one's triangles, in the order of their first triangle;
        // componentOf (if not null) per vertex id its component, 0 for a vertex in no triangle
        internal static List<AxisAlignedBox3d> ComponentBoxes(DMesh3 mesh, out int[] componentOf)
        {
            int nv = mesh.MaxVertexID;
            var parent = new int[nv];
            for (int v = 0; v < nv; ++v) parent[v] = v;
            Func<int, int> find = (v) => { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; };
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                int ra = find(tv.a), rb = find(tv.b), rc = find(tv.c);
                int r = Math.Min(ra, Math.Min(rb, rc));
                parent[ra] = r; parent[rb] = r; parent[rc] = r;
            }
            componentOf = new int[nv];
            var index = new Dictionary<int, int>();
            var boxes = new List<AxisAlignedBox3d>();
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                int r = find(tv.a), c;
                if (index.TryGetValue(r, out c) == false) {
                    index[r] = c = boxes.Count;
                    boxes.Add(AxisAlignedBox3d.Empty);
                }
                AxisAlignedBox3d b = boxes[c];
                b.Contain(mesh.GetVertex(tv.a)); b.Contain(mesh.GetVertex(tv.b)); b.Contain(mesh.GetVertex(tv.c));
                boxes[c] = b;
            }
            for (int v = 0; v < nv; ++v) {
                int c;
                componentOf[v] = index.TryGetValue(find(v), out c) ? c : 0;
            }
            return boxes;
        }

        // the largest diagonal of the boxes (0 for none); NaN if one is not finite (coordinates that are not, or an
        // extent that overflows), whatever the order: tolerances relative to it are then NaN
        internal static double LargestDiagonal(List<AxisAlignedBox3d> boxes)
        {
            double size = 0;
            foreach (AxisAlignedBox3d bx in boxes) {
                double d = bx.DiagonalLength;
                if (!(d < double.PositiveInfinity)) size = double.NaN;
                else if (d > size) size = d;
            }
            return size;
        }

        // the diagonal of the largest connected component (see ComponentBoxes, LargestDiagonal): the scale of tolerances
        // that a small part far from the rest must not widen (SafeRounding, the cleanups of MeshRobustRepair)
        internal static double LargestComponentDiagonal(DMesh3 mesh)
        {
            int[] componentOf;
            return LargestDiagonal(ComponentBoxes(mesh, out componentOf));
        }

        // uniform random points in a union of boxes: a box picked by volume, a point in it, kept if no earlier box
        // contains it (so every point of the union has the same density); one box: a point in it, as before
        internal sealed class SampleRegion
        {
            readonly List<AxisAlignedBox3d> boxes;
            readonly double[] cumulative;

            public SampleRegion(AxisAlignedBox3d box) : this(new List<AxisAlignedBox3d>() { box }) { }

            public SampleRegion(List<AxisAlignedBox3d> boxes)
            {
                this.boxes = boxes;
                cumulative = new double[boxes.Count];
                double sum = 0;
                for (int i = 0; i < boxes.Count; ++i)
                    cumulative[i] = sum += boxes[i].Volume;
            }

            /// <summary>
            /// the volume of the union of the boxes, exactly up to rounding (coordinate compression in x and y, the z
            /// intervals merged per cell)
            /// </summary>
            public double Volume()
            {
                if (boxes.Count == 1)
                    return boxes[0].Volume;
                var xs = boxes.SelectMany(b => new[] { b.Min.x, b.Max.x }).Distinct().OrderBy(v => v).ToArray();
                var ys = boxes.SelectMany(b => new[] { b.Min.y, b.Max.y }).Distinct().OrderBy(v => v).ToArray();
                double volume = 0;
                var zs = new List<Interval1d>();
                for (int i = 0; i + 1 < xs.Length; ++i) {
                    double xm = 0.5 * (xs[i] + xs[i + 1]);
                    for (int j = 0; j + 1 < ys.Length; ++j) {
                        double ym = 0.5 * (ys[j] + ys[j + 1]);
                        zs.Clear();
                        foreach (var b in boxes)
                            if (b.Min.x <= xm && xm <= b.Max.x && b.Min.y <= ym && ym <= b.Max.y)
                                zs.Add(new Interval1d(b.Min.z, b.Max.z));
                        if (zs.Count == 0) continue;
                        zs.Sort((u, v) => u.a.CompareTo(v.a));
                        double len = 0, lo = zs[0].a, hi = zs[0].b;
                        for (int k = 1; k < zs.Count; ++k) {
                            if (zs[k].a > hi) { len += hi - lo; lo = zs[k].a; hi = zs[k].b; }
                            else hi = Math.Max(hi, zs[k].b);
                        }
                        len += hi - lo;
                        volume += (xs[i + 1] - xs[i]) * (ys[j + 1] - ys[j]) * len;
                    }
                }
                return volume;
            }

            public bool Contains(Vector3d p)
            {
                foreach (var box in boxes)
                    if (box.Contains(p)) return true;
                return false;
            }

            public Vector3d Next(Random rnd)
            {
                if (boxes.Count == 1)
                    return point(boxes[0], rnd);
                while (true) {
                    double u = rnd.NextDouble() * cumulative[cumulative.Length - 1];
                    int k = Array.BinarySearch(cumulative, u);
                    k = k < 0 ? ~k : k + 1;
                    k = Math.Min(k, boxes.Count - 1);
                    Vector3d p = point(boxes[k], rnd);
                    bool earlier = false;
                    for (int j = 0; j < k && earlier == false; ++j)
                        earlier = boxes[j].Contains(p);
                    if (earlier == false)
                        return p;
                }
            }

            static Vector3d point(AxisAlignedBox3d box, Random rnd)
            {
                return box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
            }
        }
    }
}
