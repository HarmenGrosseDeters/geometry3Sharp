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
            var tree = new DMeshAABBTree3(mesh, true);
            int count = 0;
            object lockObj = new object();
            int[] tris = new List<int>(mesh.TriangleIndices()).ToArray();
            gParallel.ForEach(Interval1i.Range(tris.Length), (k) => {
                int ta = tris[k];
                Triangle3d A = triangle(mesh, ta);
                if (degenerate(ref A)) return;
                AxisAlignedBox3d box = new AxisAlignedBox3d(A.V0); box.Contain(A.V1); box.Contain(A.V2);
                Index3i ia = mesh.GetTriangle(ta);
                int local = 0;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxDF = (b, depth) => b.Intersects(box),
                    NextTriangleF = (tb) => {
                        if (tb <= ta) return;
                        Triangle3d B = triangle(mesh, tb);
                        if (degenerate(ref B)) return;
                        if (intersect(ia, ref A, mesh.GetTriangle(tb), ref B))
                            local++;
                    }
                });
                if (local > 0)
                    lock (lockObj) count += local;
            });
            return count;
        }

        static Triangle3d triangle(DMesh3 mesh, int tid)
        {
            Index3i t = mesh.GetTriangle(tid);
            return new Triangle3d(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c));
        }

        // zero area: the three points are collinear (exact)
        static bool degenerate(ref Triangle3d t)
        {
            Vector3d n = (t.V1 - t.V0).Cross(t.V2 - t.V0);
            if (n.LengthSquared > 1e-24 * Math.Pow((t.V1 - t.V0).LengthSquared + (t.V2 - t.V0).LengthSquared, 2))
                return false;
            // collinear in 3D iff collinear in all three axis projections
            for (int k = 0; k < 3; ++k)
                if (ExactPredicates.Orient2d(project(t.V0, k), project(t.V1, k), project(t.V2, k)) != 0)
                    return false;
            return true;
        }

        static int dominant_axis(Vector3d n)
        {
            double x = Math.Abs(n.x), y = Math.Abs(n.y), z = Math.Abs(n.z);
            return (x >= y && x >= z) ? 0 : (y >= z ? 1 : 2);
        }

        // drop coordinate ax
        static Vector2d project(Vector3d p, int ax)
        {
            return ax == 0 ? new Vector2d(p.y, p.z) : (ax == 1 ? new Vector2d(p.z, p.x) : new Vector2d(p.x, p.y));
        }

        static bool intersect(Index3i ia, ref Triangle3d A, Index3i ib, ref Triangle3d B)
        {
            // shared vertices: corner indices in A and B
            int nShared = 0, sa0 = -1, sb0 = -1, sa1 = -1, sb1 = -1;
            for (int i = 0; i < 3; ++i)
                for (int j = 0; j < 3; ++j)
                    if (ia[i] == ib[j]) {
                        if (nShared == 0) { sa0 = i; sb0 = j; } else { sa1 = i; sb1 = j; }
                        nShared++;
                    }
            if (nShared == 0)
                return ExactTriangleIntersection.Intersects(ref A, ref B);
            if (nShared >= 2) {
                // shared edge: overlap only if coplanar with the far corners on the same side of the edge
                Vector3d a = A[sa0], b = A[sa1], c = A[3 - sa0 - sa1], d = B[3 - sb0 - sb1];
                if (ExactPredicates.Orient3d(a, b, c, d) != 0)
                    return false;
                int ax = dominant_axis((b - a).Cross(c - a));
                Vector2d a2 = project(a, ax), b2 = project(b, ax);
                return ExactPredicates.Orient2d(a2, b2, project(c, ax)) * ExactPredicates.Orient2d(a2, b2, project(d, ax)) > 0;
            }
            // one shared vertex s: they intersect elsewhere iff the edge opposite s of one triangle meets
            // the other triangle, or (coplanar) their corners at s overlap
            Vector3d s = A[sa0];
            Vector3d p = A[(sa0 + 1) % 3], q = A[(sa0 + 2) % 3], u = B[(sb0 + 1) % 3], w = B[(sb0 + 2) % 3];
            if (ExactTriangleIntersection.SegmentIntersectsTriangle(p, q, ref B) || ExactTriangleIntersection.SegmentIntersectsTriangle(u, w, ref A))
                return true;
            if (ExactPredicates.Orient3d(s, p, q, u) != 0 || ExactPredicates.Orient3d(s, p, q, w) != 0)
                return false;
            int axis = dominant_axis((p - s).Cross(q - s));
            Vector2d s2 = project(s, axis), p2 = project(p, axis), q2 = project(q, axis), u2 = project(u, axis), w2 = project(w, axis);
            if (ExactPredicates.Orient2d(s2, p2, q2) < 0) { var t = p2; p2 = q2; q2 = t; }
            if (ExactPredicates.Orient2d(s2, u2, w2) < 0) { var t = u2; u2 = w2; w2 = t; }
            return inside_corner(s2, p2, q2, u2) || inside_corner(s2, p2, q2, w2) || inside_corner(s2, u2, w2, p2) || inside_corner(s2, u2, w2, q2)
                || (same_ray(s2, p2, u2) && same_ray(s2, q2, w2));
        }

        // is the direction s->x strictly inside the (counter-clockwise, < 180 degree) corner p-s-q?
        static bool inside_corner(Vector2d s, Vector2d p, Vector2d q, Vector2d x)
        {
            return ExactPredicates.Orient2d(s, p, x) > 0 && ExactPredicates.Orient2d(s, x, q) > 0;
        }

        static bool same_ray(Vector2d s, Vector2d a, Vector2d b)
        {
            return ExactPredicates.Orient2d(s, a, b) == 0 && (a - s).Dot(b - s) > 0;
        }


        /// <summary>
        /// Intersection over union of the solids of input and result, from samples in the common bounds
        /// (deterministic): a point is inside the input where its generalized winding number is > 0.5,
        /// inside the result likewise. 1 = same solid. Also returns the parts of the input solid missing in
        /// the result (eg lost thin walls) and the result solid not in the input (eg filled cavities),
        /// relative to the input solid.
        /// </summary>
        public static double VolumeIoU(DMesh3 input, DMesh3 result, out double missing, out double extra, int samples = 100000, int seed = 1)
        {
            missing = extra = 0;
            if (input.TriangleCount == 0 || result.TriangleCount == 0)
                return 0;
            var ti = new DMeshAABBTree3(input, true);
            var tr = new DMeshAABBTree3(result, true);
            ti.FastWindingNumber(Vector3d.Zero); tr.FastWindingNumber(Vector3d.Zero);    // caches before parallel use
            AxisAlignedBox3d box = input.CachedBounds;
            box.Contain(result.CachedBounds);
            box.Expand(0.01 * box.DiagonalLength);
            var rnd = new Random(seed);
            Vector3d[] pts = new Vector3d[samples];
            for (int k = 0; k < samples; ++k)
                pts[k] = box.Min + new Vector3d(rnd.NextDouble() * box.Width, rnd.NextDouble() * box.Height, rnd.NextDouble() * box.Depth);
            int[] cls = new int[samples];
            gParallel.ForEach(Interval1i.Range(samples), (k) => {
                bool a = ti.FastWindingNumber(pts[k]) > 0.5, b = tr.FastWindingNumber(pts[k]) > 0.5;
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
            int union = both + onlyIn + onlyOut;
            return union > 0 ? (double)both / union : 1;
        }
    }
}
