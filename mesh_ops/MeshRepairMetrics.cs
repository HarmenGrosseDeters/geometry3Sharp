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
    /// </summary>
    public static class MeshRepairMetrics
    {
        /// <summary>
        /// Number of triangle pairs that intersect, exactly (ExactTriangleIntersection). Pairs that share a
        /// vertex count if they intersect anywhere else; pairs that share an edge count if they overlap
        /// (coplanar on the same side). Degenerate (zero area) triangles are skipped.
        /// </summary>
        public static int SelfIntersections(DMesh3 mesh)
        {
            return SelfIntersectionPairs(mesh).Count;
        }

        /// <summary> the intersecting triangle pairs (see SelfIntersections), each once, sorted </summary>
        public static List<Index2i> SelfIntersectionPairs(DMesh3 mesh)
        {
            var tree = new DMeshAABBTree3(mesh, true);
            var pairs = new List<Index2i>();
            object lockObj = new object();
            int[] tris = new List<int>(mesh.TriangleIndices()).ToArray();
            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                int ta = tris[k];
                Triangle3d A = triangle(mesh, ta);
                if (ExactTriangleIntersection.IsDegenerate(ref A)) return;
                AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                Index3i ia = mesh.GetTriangle(ta);
                List<Index2i> local = null;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => b.Intersects(box),
                    NextTriangleF = (tb) => {
                        if (tb <= ta) return;
                        Triangle3d B = triangle(mesh, tb);
                        if (ExactTriangleIntersection.IsDegenerate(ref B)) return;
                        if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, mesh.GetTriangle(tb), ref B)) {
                            if (local == null) local = new List<Index2i>();
                            local.Add(new Index2i(ta, tb));
                        }
                    }
                });
                if (local != null)
                    lock (lockObj) pairs.AddRange(local);
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
        /// (deterministic): a point is inside the input where its generalized winding number is > 0.5,
        /// inside the result likewise. 1 = same solid. Also returns the parts of the input solid missing in
        /// the result (eg lost thin walls) and the result solid not in the input (eg filled cavities),
        /// relative to the input solid. NaN if the input encloses no volume (eg a single open sheet).
        /// </summary>
        public static double VolumeIoU(DMesh3 input, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            missing = extra = 0;
            if (input.TriangleCount == 0)
                return 0;
            var ti = new DMeshAABBTree3(input, true);
            ti.FastWindingNumber(Vector3d.Zero);    // cache before parallel use
            AxisAlignedBox3d box = input.CachedBounds;
            box.Contain(result.CachedBounds);
            return VolumeIoU((p) => ti.FastWindingNumber(p) > 0.5, box, result, out missing, out extra, samples, seed);
        }

        /// <summary>
        /// As above, against a reference solid given by its inside test (eg the exact boolean of the
        /// inputs' winding numbers), sampled in box (expanded by 1%).
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
