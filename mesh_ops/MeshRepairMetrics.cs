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
        /// Is VolumeIoU(input, result) at least minIoU? The same samples as VolumeIoU, but only as many as the
        /// answer needs: they are classified in rounds (samples / 32, then doubling), and after each round
        /// the share of the union that is in only one of the solids gets a confidence interval (Wilson score,
        /// z standard deviations); the answer is given as soon as the interval lies on one side of
        /// 1 - minIoU and some sample is inside the input. With all samples (results close to minIoU) the
        /// answer and iou are those of VolumeIoU. An early answer differs from the one with all samples only
        /// by chance, about the probability of a z-sigma deviation (z = 4: about 3e-5, times the number of
        /// rounds). iou is the estimate from the samples used; NaN (and true) if the input encloses no volume,
        /// as VolumeIoU (no solid to compare with); 0 (and false) if its solid is too small to be hit by the samples.
        /// Typically 10-30x cheaper than VolumeIoU for results that clearly agree with the input.
        /// </summary>
        public static bool VolumeIoUAtLeast(DMesh3 input, DMesh3 result, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            return VolumeIoUAtLeast(new SolidReference(input), result, SolidInside(result), minIoU, out iou, samples, seed, z);
        }

        /// <summary>
        /// The input's solid as the metrics see it (its oriented copy and a winding number tree), built once for several
        /// results (eg the stages of MeshRobustRepair)
        /// </summary>
        internal sealed class SolidReference
        {
            internal readonly DMesh3 Mesh;
            internal readonly Func<Vector3d, bool> Inside;
            List<AxisAlignedBox3d> componentBoxes;
            /// <summary> boxes of the connected components of Mesh (computed once) </summary>
            internal List<AxisAlignedBox3d> ComponentBoxes()
            {
                if (componentBoxes == null) {
                    var comps = new MeshConnectedComponents(Mesh);
                    comps.FindConnectedT();
                    componentBoxes = comps.Components.Select(c => {
                        var box = AxisAlignedBox3d.Empty;
                        foreach (int tid in c.Indices) box.Contain(Mesh.GetTriBounds(tid));
                        return box;
                    }).ToList();
                }
                return componentBoxes;
            }

            internal SolidReference(DMesh3 input)
            {
                Mesh = input.TriangleCount > 0 ? WindingContouring.OrientedCopy(input) : input;
                Inside = SolidInside(Mesh);
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

        internal static bool VolumeIoUAtLeast(SolidReference input, DMesh3 result, Func<Vector3d, bool> resultInside, double minIoU, out double iou, int samples = 100000, int seed = 1, double z = 4)
        {
            iou = 0;
            if (input.Mesh.TriangleCount == 0)
                return iou >= minIoU;
            bool atLeast = volume_iou_at_least(input.Inside, sample_region(input.Mesh, result), resultInside, minIoU, out iou, samples, seed, z);
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
            return volume_iou_at_least(referenceInside, new SampleRegion(box), SolidInside(result), minIoU, out iou, samples, seed, z);
        }

        static bool volume_iou_at_least(Func<Vector3d, bool> referenceInside, SampleRegion region, Func<Vector3d, bool> resultInside, double minIoU, out double iou, int samples, int seed, double z)
        {
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            int[] cls = new int[samples];
            int done = 0, both = 0, onlyOne = 0, inputSolid = 0;
            double maxShare = 1 - minIoU;
            int round = Math.Max(1, samples / 32);
            while (true) {
                int end = Math.Min(samples, done + round);
                for (int k = done; k < end; ++k)        // same sequence as VolumeIoU
                    pts[k] = region.Next(rnd);
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
                if (done == samples)
                    break;
                // no sample in the input's solid yet: it may be small rather than absent, which only all samples
                // (and has_solid) can tell
                if (union == 0 || inputSolid == 0)
                    continue;
                // Wilson score interval of the share p of the union in only one solid (1 - IoU)
                double n = union, ph = onlyOne / n, z2 = z * z;
                double center = (ph + z2 / (2 * n)) / (1 + z2 / n);
                double half = z * Math.Sqrt(ph * (1 - ph) / n + z2 / (4 * n * n)) / (1 + z2 / n);
                if (center + half < maxShare || center - half > maxShare)
                    break;
            }
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
            box.Expand(0.01 * box.DiagonalLength);
            return volume_iou(referenceInside, new SampleRegion(box), result, out missing, out extra, samples, seed);
        }

        static double volume_iou(Func<Vector3d, bool> referenceInside, SampleRegion region, DMesh3 result, out double missing, out double extra, int samples, int seed)
        {
            missing = extra = 0;
            var tr = new DMeshAABBTree3(result, true);
            tr.FastWindingNumber(Vector3d.Zero);
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            for (int k = 0; k < samples; ++k)
                pts[k] = region.Next(rnd);
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

        static Func<Vector3d, bool> winding_inside(DMesh3 mesh)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            tree.FastWindingNumber(Vector3d.Zero);    // cache before parallel use
            return (p) => tree.FastWindingNumber(p) > 0.5;
        }

        // Does the input (with no sample of the IoU in its solid) enclose a volume after all, too small to be hit?
        // Sampled only near the input itself.
        static bool has_solid(Func<Vector3d, bool> inside, DMesh3 input, int seed)
        {
            SampleRegion region = sample_region(input, null);
            var rnd = new Random(seed);
            for (int k = 0; k < 4096; ++k)
                if (inside(region.Next(rnd)))
                    return true;
            return false;
        }

        /// <summary>
        /// Connected components of the input whose solid the result lost (the volume IoU does not notice a part below
        /// 1 - IoU of the volume, eg a screw of an assembly or a small separate shell). For each component, points are
        /// sampled in its box (up to 1024, 256 if none of them lies in the solid) until 32 lie in the input's solid (winding number of its oriented copy
        /// &gt; 0.5, as VolumeIoU); the component is lost if fewer than minCoverage of them lie in the result's solid.
        /// Components with fewer than 8 such points (open sheets, soups, walls thinner than about 1/100 of their box)
        /// are not judged, nor components whose box diagonal is below MinComponentDiagonal of the input's (specks that
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

        internal static int LostComponents(SolidReference reference, Func<Vector3d, bool> inResult, double minCoverage = 0.5, int maxComponents = 1000, int seed = 1)
        {
            DMesh3 input = reference.Mesh;
            if (input.TriangleCount == 0)
                return 0;
            Func<Vector3d, bool> inInput = reference.Inside;
            double minDiagonal = MinComponentDiagonal * TriangleBounds(input).DiagonalLength;
            var boxes = reference.ComponentBoxes().Where(b => b.Volume > 0 && b.DiagonalLength >= minDiagonal)
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
