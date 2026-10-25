// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
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

        /// <summary> the intersecting triangle pairs (see SelfIntersections), each once, sorted </summary>
        public static List<Index2i> SelfIntersectionPairs(DMesh3 mesh)
        {
            return self_intersection_pairs(mesh, false);
        }

        /// <summary>
        /// SelfIntersections(mesh) &gt; 0, but stops at the first intersecting pair (cheaper for meshes with
        /// self-intersections; the same work for meshes without)
        /// </summary>
        public static bool HasSelfIntersections(DMesh3 mesh)
        {
            return self_intersection_pairs(mesh, true).Count > 0;
        }

        // firstOnly: stop after the first pair found (then at least one pair is returned if there is any)
        static List<Index2i> self_intersection_pairs(DMesh3 mesh, bool firstOnly)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            var pairs = new List<Index2i>();
            object lockObj = new object();
            int found = 0;
            int[] tris = new List<int>(mesh.TriangleIndices()).ToArray();
            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                if (firstOnly && System.Threading.Volatile.Read(ref found) != 0) return;
                int ta = tris[k];
                Triangle3d A = triangle(mesh, ta);
                if (ExactTriangleIntersection.IsDegenerate(ref A)) return;
                AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                Index3i ia = mesh.GetTriangle(ta);
                List<Index2i> local = null;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => b.Intersects(box) && (firstOnly == false || local == null),
                    NextTriangleF = (tb) => {
                        if (tb <= ta || (firstOnly && local != null)) return;
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
                    System.Threading.Volatile.Write(ref found, 1);
                }
            });
            pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
            return pairs;
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
        /// Intersection over union of the solids of input and result, from samples in the common bounds
        /// (deterministic): a point is inside the input where the generalized winding number of its oriented
        /// copy (WindingContouring.OrientedCopy: inside-out parts flipped, as the solidify fallback does) is > 0.5,
        /// inside the result likewise. 1 = same solid. Also returns the parts of the input solid missing in
        /// the result (eg lost thin walls) and the result solid not in the input (eg filled cavities),
        /// relative to the input solid. NaN if the input encloses no volume (eg a single open sheet), 0 if the
        /// input has no triangles. Estimated from samples random points (fixed seed, so repeatable); small
        /// differences are sampling noise.
        /// </summary>
        public static double VolumeIoU(DMesh3 input, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            missing = extra = 0;
            if (input.TriangleCount == 0)
                return 0;
            // the solid the solidify fallback sees: inside-out inputs (winding number -1 inside) count as
            // their solid, not as none
            input = WindingContouring.OrientedCopy(input);
            var ti = new DMeshAABBTree3(input, true);
            ti.FastWindingNumber(Vector3d.Zero);    // cache before parallel use
            AxisAlignedBox3d box = input.CachedBounds;
            box.Contain(result.CachedBounds);
            return VolumeIoU((p) => ti.FastWindingNumber(p) > 0.5, box, result, out missing, out extra, samples, seed);
        }

        /// <summary>
        /// Is VolumeIoU(input, result) at least minIoU? The same samples as VolumeIoU, but only as many as the
        /// answer needs: they are classified in rounds (samples / 32, then doubling), and after each round
        /// the share of the union that is in only one of the solids gets a confidence interval (Wilson score,
        /// z standard deviations); the answer is given as soon as the interval lies on one side of
        /// 1 - minIoU. With all samples (results close to minIoU) the answer and iou are those of VolumeIoU.
        /// An early answer differs from the one with all samples only by chance, about the probability
        /// of a z-sigma deviation (z = 4: about 3e-5). iou is the estimate from the samples used; NaN (and
        /// true) if no sample is inside the input, as VolumeIoU (no solid to compare with).
        /// Typically 10-30x cheaper than VolumeIoU for results that clearly agree with the input.
        /// </summary>
        public static bool VolumeIoUAtLeast(DMesh3 input, DMesh3 result, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            iou = 0;
            if (input.TriangleCount == 0)
                return iou >= minIoU;
            input = WindingContouring.OrientedCopy(input);
            var ti = new DMeshAABBTree3(input, true);
            ti.FastWindingNumber(Vector3d.Zero);    // cache before parallel use
            AxisAlignedBox3d box = input.CachedBounds;
            box.Contain(result.CachedBounds);
            return VolumeIoUAtLeast((p) => ti.FastWindingNumber(p) > 0.5, box, result, minIoU, out iou, samples, seed, z);
        }

        /// <summary> as above, against a reference solid given by its inside test (see VolumeIoU) </summary>
        public static bool VolumeIoUAtLeast(Func<Vector3d, bool> referenceInside, AxisAlignedBox3d box, DMesh3 result, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            var tr = new DMeshAABBTree3(result, true);
            tr.FastWindingNumber(Vector3d.Zero);
            box.Expand(0.01 * box.DiagonalLength);
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            int[] cls = new int[samples];
            int done = 0, both = 0, onlyOne = 0;
            double maxShare = 1 - minIoU;
            int round = Math.Max(1, samples / 32);
            while (true) {
                int end = Math.Min(samples, done + round);
                for (int k = done; k < end; ++k)        // same sequence as VolumeIoU
                    pts[k] = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
                gParallel.ForEach(Interval1i.FromToInclusive(done, end - 1), (k) => {
                    bool a = referenceInside(pts[k]), b = result.TriangleCount > 0 && tr.FastWindingNumber(pts[k]) > 0.5;
                    cls[k] = (a ? 1 : 0) | (b ? 2 : 0);
                });
                for (int k = done; k < end; ++k) {
                    if (cls[k] == 3) both++; else if (cls[k] != 0) onlyOne++;
                }
                done = end;
                round = done;
                int union = both + onlyOne;
                if (done == samples)
                    break;
                if (union == 0)
                    continue;
                // Wilson score interval of the share p of the union in only one solid (1 - IoU)
                double n = union, ph = onlyOne / n, z2 = z * z;
                double center = (ph + z2 / (2 * n)) / (1 + z2 / n);
                double half = z * Math.Sqrt(ph * (1 - ph) / n + z2 / (4 * n * n)) / (1 + z2 / n);
                if (center + half < maxShare || center - half > maxShare)
                    break;
            }
            int inputSolid = 0;
            for (int k = 0; k < done; ++k)
                if ((cls[k] & 1) != 0) inputSolid++;
            if (inputSolid == 0) {
                iou = double.NaN;       // no reference solid (eg a single open sheet): not defined
                return true;
            }
            iou = (double)both / (both + onlyOne);
            return iou >= minIoU;
        }

        /// <summary>
        /// As above, against a reference solid given by its inside test (eg the exact boolean of the
        /// inputs' winding numbers), sampled in box (expanded by 1%). NaN if no sample is inside the reference.
        /// referenceInside is called in parallel and must be thread-safe.
        /// </summary>
        public static double VolumeIoU(Func<Vector3d, bool> referenceInside, AxisAlignedBox3d box, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            missing = extra = 0;
            var tr = new DMeshAABBTree3(result, true);
            tr.FastWindingNumber(Vector3d.Zero);
            box.Expand(0.01 * box.DiagonalLength);
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            for (int k = 0; k < samples; ++k)
                pts[k] = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
            int[] cls = new int[samples];
            gParallel.ForEach(Interval1i.Range(samples), (k) => {
                bool a = referenceInside(pts[k]), b = result.TriangleCount > 0 && tr.FastWindingNumber(pts[k]) > 0.5;
                cls[k] = (a ? 1 : 0) | (b ? 2 : 0);
            });
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
    }
}
