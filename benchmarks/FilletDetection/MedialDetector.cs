// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace FilletDetection
{
    /// <summary>
    /// Fillet detection with medial balls (an own construction on the idea of DeFillet, Jiang et al. 2025: the rolling ball
    /// of a fillet is an osculating ball). Instead of the Voronoi diagram of all samples it computes, per triangle and
    /// side, the largest empty ball that touches the triangle (the shrinking ball of Ma et al. 2012 with the angle
    /// condition of Dey and Zhao 2004, as MeshWallThickness.InscribedSphere does, here on both sides and bounded by
    /// MaxRadius), and then tests that ball on the mesh:
    ///   - radius at most MaxRadius;
    ///   - the triangles around that touch the ball (within Tolerance, connected over shared vertices) turn away from the
    ///     triangle's normal to two opposite sides, each by at least MinTurnDegrees and at most MaxStepDegrees: the ball
    ///     rolls in a groove. A ball in the wedge of a sharp edge touches to one side only, a ball between parallel walls
    ///     to none, a ball on the flat bottom of a slot or a blind hole touches walls that are 90 degrees away;
    ///   - they turn by at most MaxSpanDegrees: a ball inside a full cylinder or sphere is no fillet.
    /// The ball of a planar facet (triangles connected without a fold, eg a strip of a tessellated fillet) is the largest
    /// of the balls at the centroid, near the edge midpoints and near the vertices of its triangles: the ball is largest
    /// on the middle line of the facet, and the centroid of a strip triangle is off it. All triangles of the facet get
    /// that ball and its verdict. Per facet, no global state: parallel and
    /// deterministic. The radius found is the ball's.
    /// </summary>
    public class MedialDetector
    {
        public double MaxRadius;
        public double SeparationDegrees = 10;
        public double Tolerance = 0.1;
        public double MinTurnDegrees = 0.5;
        public double MaxSpanDegrees = 135;
        public double MaxStepDegrees = 50;

        DMesh3 mesh;
        DMeshAABBTree3 tree;
        Vector3d[] normal;
        double tiny;

        public Detection Run(DMesh3 m)
        {
            var sw = Stopwatch.StartNew();
            mesh = m;
            tree = new DMeshAABBTree3(mesh, true);
            tiny = 1e-9 * mesh.CachedBounds.DiagonalLength;
            int n = mesh.MaxTriangleID;
            normal = new Vector3d[n];
            foreach (int tid in mesh.TriangleIndices()) normal[tid] = mesh.GetTriNormal(tid);
            var result = new Detection(n);

            // facets: triangles connected over edges without a fold
            var facet = new int[n];
            foreach (int tid in mesh.TriangleIndices()) facet[tid] = tid;
            Func<int, int> find = null;
            find = x => { while (facet[x] != x) { facet[x] = facet[facet[x]]; x = facet[x]; } return x; };
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || normal[et.a].Dot(normal[et.b]) < 1 - 1e-9) continue;
                int ra = find(et.a), rb = find(et.b);
                if (ra != rb) facet[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
            foreach (int tid in mesh.TriangleIndices()) facet[tid] = find(tid);

            // per side: the ball of every triangle, the largest one per facet, and its test
            var radius = new double[2][]; var center = new Vector3d[2][];
            for (int side = 0; side < 2; ++side) {
                int sd = side;
                radius[sd] = new double[n]; center[sd] = new Vector3d[n];
                gParallel.ForEach(mesh.TriangleIndices(), tid => {
                    radius[sd][tid] = FacetBall(tid, sd == 0 ? -normal[tid] : normal[tid], out center[sd][tid]);
                });
                var best = new int[n];
                foreach (int tid in mesh.TriangleIndices()) best[tid] = -1;
                foreach (int tid in mesh.TriangleIndices()) {
                    int f = facet[tid];
                    if (best[f] < 0 || radius[sd][tid] > radius[sd][best[f]]) best[f] = tid;
                }
                var accepted = new bool[n];
                gParallel.ForEach(mesh.TriangleIndices(), f => {
                    if (facet[f] != f) return;
                    int tid = best[f];
                    double r = radius[sd][tid];
                    accepted[f] = r <= MaxRadius && RollsInGroove(tid, center[sd][tid], r, sd == 0 ? -normal[tid] : normal[tid]);
                });
                foreach (int tid in mesh.TriangleIndices()) {
                    int f = facet[tid];
                    double r = radius[sd][best[f]];
                    if (accepted[f] && (!result.Fillet[tid] || r < result.Radius[tid])) { result.Fillet[tid] = true; result.Radius[tid] = r; }
                }
            }
            result.Seconds = sw.Elapsed.TotalSeconds;
            return result;
        }

        // the largest of the balls at the sample points of the triangle; infinite if one of them has no ball within the bound
        double FacetBall(int tid, Vector3d dir, out Vector3d center)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(tid, ref a, ref b, ref c);
            Vector3d g = (a + b + c) / 3;
            double best = 0; center = g;
            for (int k = 0; k < 7; ++k) {
                Vector3d p = g;
                if (k == 1) p = 0.49 * (a + b) + 0.02 * g;
                if (k == 2) p = 0.49 * (b + c) + 0.02 * g;
                if (k == 3) p = 0.49 * (c + a) + 0.02 * g;
                if (k >= 4) p = 0.98 * (k == 4 ? a : k == 5 ? b : c) + 0.02 * g;
                double r = Ball(p, dir);
                if (double.IsInfinity(r)) return r;
                if (r > best) { best = r; center = p + r * dir; }
            }
            return best;
        }

        // radius of the largest empty ball that touches the surface at p with its centre on p + r dir, at most 1.01 MaxRadius
        // (infinite if that ball is still empty)
        double Ball(Vector3d p, Vector3d dir)
        {
            double r = 1.01 * MaxRadius;
            double cosSep = Math.Cos(SeparationDegrees * MathUtil.Deg2Rad);
            bool any = false;
            for (int iter = 0; iter < 100; ++iter) {
                Vector3d c = p + r * dir;
                double bestSqr = (r - tiny) * (r - tiny);
                Vector3d q = Vector3d.Zero; bool found = false;
                tree.DoTraversal(new DMeshAABBTree3.TreeTraversal() {
                    NextBoxF = (box, depth) => BoxDistanceSqr(box, c) < bestSqr,
                    NextTriangleF = ti => {
                        Index3i tv = mesh.GetTriangle(ti);
                        var tri = new Triangle3d(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c));
                        double dsqr = DistPoint3Triangle3.DistanceSqr(ref c, ref tri, out Vector3d closest, out Vector3d bary);
                        if (!(dsqr < bestSqr)) return;
                        Vector3d w = closest - c;
                        double wl = w.Length;
                        if (wl > 0 && -dir.Dot(w) > cosSep * wl) return;    // seen close to p from the centre: the facets around p
                        bestSqr = dsqr; q = closest; found = true;
                    }
                });
                if (!found) break;
                any = true;
                Vector3d d = q - p;
                double dn = d.Dot(dir);
                if (dn <= tiny) break;
                double rnew = d.LengthSquared / (2 * dn);
                if (!(rnew < r * (1 - 1e-12))) break;
                r = rnew;
            }
            return any ? r : double.PositiveInfinity;
        }

        static double BoxDistanceSqr(AxisAlignedBox3f box, Vector3d p)
        {
            const double pad = 1e-5;
            double dx = Math.Max(0, Math.Max(box.Min.x - pad - p.x, p.x - box.Max.x - pad));
            double dy = Math.Max(0, Math.Max(box.Min.y - pad - p.y, p.y - box.Max.y - pad));
            double dz = Math.Max(0, Math.Max(box.Min.z - pad - p.z, p.z - box.Max.z - pad));
            return dx * dx + dy * dy + dz * dz;
        }

        // the triangles that touch the ball, connected to tid over shared vertices, turn to two opposite sides and not too far
        bool RollsInGroove(int tid, Vector3d center, double r, Vector3d dir)
        {
            double reach = r * (1 + Tolerance), reachSqr = reach * reach;
            double minOffset = 2 * Math.Sin(0.5 * MinTurnDegrees * MathUtil.Deg2Rad);
            double maxOffset = 2 * Math.Sin(0.5 * MaxStepDegrees * MathUtil.Deg2Rad);
            double cosSpan = Math.Cos(MaxSpanDegrees * MathUtil.Deg2Rad);
            Vector3d n0 = normal[tid];
            var seen = new HashSet<int> { tid };
            var queue = new Queue<int>(); queue.Enqueue(tid);
            var offsets = new List<Vector3d>(); var distinct = new HashSet<(int, int, int)>();
            while (queue.Count > 0 && seen.Count < 20000) {
                int t = queue.Dequeue();
                Index3i tv = mesh.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    foreach (int nb in mesh.VtxTrianglesItr(tv[j])) {
                        if (!seen.Add(nb)) continue;
                        Index3i nv = mesh.GetTriangle(nb);
                        var tri = new Triangle3d(mesh.GetVertex(nv.a), mesh.GetVertex(nv.b), mesh.GetVertex(nv.c));
                        double dsqr = DistPoint3Triangle3.DistanceSqr(ref center, ref tri, out Vector3d closest, out Vector3d bary);
                        if (dsqr > reachSqr) continue;
                        // tangent: the contact is seen along the triangle's normal (towards the ball's side of the surface)
                        Vector3d w = (center - closest).Normalized;
                        double side = dir.Dot(n0) < 0 ? -1 : 1;
                        if (side * w.Dot(normal[nb]) < 0.95) continue;
                        if (normal[nb].Dot(n0) < cosSpan) return false;
                        queue.Enqueue(nb);
                        Vector3d off = normal[nb] - n0;
                        // (one entry per direction: the triangles of a facet share theirs)
                        if (off.Length >= minOffset && off.Length <= maxOffset && distinct.Add(((int)Math.Round(off.x * 500), (int)Math.Round(off.y * 500), (int)Math.Round(off.z * 500)))) offsets.Add(off);
                    }
                }
            }
            for (int i = 0; i < offsets.Count; ++i)
                for (int j = i + 1; j < offsets.Count; ++j)
                    if (offsets[i].Dot(offsets[j]) < 0) return true;
            return false;
        }
    }
}
