// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace FilletDetection
{
    /// <summary>
    /// Sharp-edge reconstruction on detected fillets, the smallest version that can show whether the detection is good enough
    /// to rebuild the design edge. It keeps the two ideas of DeFillet's removal step (Jiang et al. 2025, section 4.3: every
    /// fillet triangle gets the normal of the nearest face outside the fillet, and the vertices move so that the triangles
    /// get those normals) and replaces the iterated quadratic program by one closed-form step:
    ///   - a fillet triangle gets the plane of the nearest tangent support triangle (a triangle outside the fillet next to it
    ///     across a fold below 50 degrees; nearest along the edges of the fillet triangles, Dijkstra from the shared edges, and a
    ///     triangle takes the support of its nearest vertex);
    ///   - a vertex of a fillet triangle moves to the least squares point of the planes of its triangles (the target planes
    ///     of its fillet triangles, the own planes of the others), held at its position where the planes leave it free
    ///     (the quadric error point of Garland and Heckbert 1997 / Ju et al. 2002 with the vertex as mass point).
    /// A vertex between triangles of one support lands on its plane, between two supports on their intersection line (the
    /// design edge), between three on the corner. The connectivity is unchanged; triangles that end with all vertices on one
    /// line are degenerate and are counted, not removed. Supports are taken as planes: a curved support gets its tangent plane.
    /// </summary>
    public static class Defillet
    {
        public static DMesh3 Reconstruct(DMesh3 source, bool[] fillet, out int moved)
        {
            var mesh = new DMesh3(source, false);
            int n = mesh.MaxTriangleID;
            // nearest tangent support per vertex: Dijkstra over the edges of the fillet triangles, from the vertices of the
            // edges shared with a tangent support
            int nv = mesh.MaxVertexID;
            var vN = new Vector3d[nv]; var vP = new Vector3d[nv]; var vDist = new double[nv];
            for (int i = 0; i < nv; ++i) vDist[i] = double.MaxValue;
            double cos50 = Math.Cos(50 * MathUtil.Deg2Rad);
            var queue = new PriorityQueue<int, (double, int)>();
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID || fillet[et.a] == fillet[et.b]) continue;
                int s = fillet[et.a] ? et.b : et.a;
                if (mesh.GetTriNormal(et.a).Dot(mesh.GetTriNormal(et.b)) < cos50) continue;
                Index2i ev = mesh.GetEdgeV(eid);
                for (int j = 0; j < 2; ++j) {
                    if (vDist[ev[j]] == 0) continue;
                    vDist[ev[j]] = 0; vN[ev[j]] = mesh.GetTriNormal(s); vP[ev[j]] = mesh.GetTriCentroid(s);
                    queue.Enqueue(ev[j], (0, ev[j]));
                }
            }
            while (queue.TryDequeue(out int vid, out (double, int) key)) {
                if (key.Item1 > vDist[vid]) continue;
                foreach (int eid in mesh.VtxEdgesItr(vid)) {
                    Index2i et = mesh.GetEdgeT(eid);
                    if (!fillet[et.a] && (et.b == DMesh3.InvalidID || !fillet[et.b])) continue;
                    Index2i ev = mesh.GetEdgeV(eid);
                    int o = ev.a == vid ? ev.b : ev.a;
                    double d = vDist[vid] + mesh.GetVertex(o).Distance(mesh.GetVertex(vid));
                    if (d < vDist[o]) { vDist[o] = d; vN[o] = vN[vid]; vP[o] = vP[vid]; queue.Enqueue(o, (d, o)); }
                }
            }
            // a fillet triangle takes the support of its nearest vertex
            var planeN = new Vector3d[n]; var planeP = new Vector3d[n]; var dist = new double[n];
            for (int i = 0; i < n; ++i) dist[i] = double.MaxValue;
            foreach (int tid in mesh.TriangleIndices()) {
                if (!fillet[tid]) continue;
                Index3i tv = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j)
                    if (vDist[tv[j]] < dist[tid]) { dist[tid] = vDist[tv[j]]; planeN[tid] = vN[tv[j]]; planeP[tid] = vP[tv[j]]; }
            }

            moved = 0;
            var position = new Dictionary<int, Vector3d>();
            foreach (int vid in mesh.VertexIndices()) {
                bool any = false;
                foreach (int tid in mesh.VtxTrianglesItr(vid)) if (fillet[tid] && dist[tid] < double.MaxValue) any = true;
                if (!any) continue;
                Vector3d x0 = mesh.GetVertex(vid);
                var seen = new HashSet<(long, long, long, long)>();
                // (A + mu I) x = b
                const double mu = 1e-6;
                double axx = mu, ayy = mu, azz = mu, axy = 0, axz = 0, ayz = 0;
                Vector3d b = mu * x0;
                foreach (int tid in mesh.VtxTrianglesItr(vid)) {
                    Vector3d nrm, pt;
                    if (fillet[tid]) { if (dist[tid] == double.MaxValue) continue; nrm = planeN[tid]; pt = planeP[tid]; }
                    else { nrm = mesh.GetTriNormal(tid); pt = x0; }
                    double off = nrm.Dot(pt);
                    if (!seen.Add(((long)Math.Round(nrm.x * 1e6), (long)Math.Round(nrm.y * 1e6), (long)Math.Round(nrm.z * 1e6), (long)Math.Round(off * 1e6)))) continue;
                    axx += nrm.x * nrm.x; ayy += nrm.y * nrm.y; azz += nrm.z * nrm.z;
                    axy += nrm.x * nrm.y; axz += nrm.x * nrm.z; ayz += nrm.y * nrm.z;
                    b += off * nrm;
                }
                var m = new Matrix3d(axx, axy, axz, axy, ayy, ayz, axz, ayz, azz);
                Vector3d x = m.Inverse() * b;
                if (x.Distance(x0) > 1e-12) { position[vid] = x; ++moved; }
            }
            foreach (var kv in position) mesh.SetVertex(kv.Key, kv.Value);
            return mesh;
        }

        /// <summary> largest distance from the vertices, edge midpoints and centroids of a to the surface of b </summary>
        static double OneSided(DMesh3 a, DMesh3 b)
        {
            var tree = new DMeshAABBTree3(b, true);
            double worst = 0;
            Action<Vector3d> probe = p => {
                int t = tree.FindNearestTriangle(p);
                Index3i tv = b.GetTriangle(t);
                var tri = new Triangle3d(b.GetVertex(tv.a), b.GetVertex(tv.b), b.GetVertex(tv.c));
                worst = Math.Max(worst, Math.Sqrt(DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out Vector3d c, out Vector3d bary)));
            };
            foreach (int tid in a.TriangleIndices()) {
                Vector3d p = Vector3d.Zero, q = Vector3d.Zero, r = Vector3d.Zero;
                a.GetTriVertices(tid, ref p, ref q, ref r);
                probe(p); probe(q); probe(r); probe(0.5 * (p + q)); probe(0.5 * (q + r)); probe(0.5 * (r + p)); probe((p + q + r) / 3);
            }
            return worst;
        }

        public static double Hausdorff(DMesh3 a, DMesh3 b) { return Math.Max(OneSided(a, b), OneSided(b, a)); }

        public static int Run(string[] filters)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<double, string> f = v => v.ToString("0.0000", inv);
            Console.WriteLine("| solid | fillets from | Hausdorff to the design, before | after | crease length (30 deg) before | after | design | vertices moved | degenerate triangles | flipped |");
            Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (Solid s in Program.Cases(filters)) {
                if (s.Name.StartsWith("rod")) continue;
                var labels = new Dictionary<string, bool[]>();
                var truth = new bool[s.Mesh.MaxTriangleID];
                foreach (int tid in s.Mesh.TriangleIndices()) truth[tid] = s.IsFillet(tid);
                labels["known answer"] = truth;
                labels["medial ball"] = new MedialDetector { MaxRadius = Program.RadiusBound * s.MaxFilletRadius }.Run(s.Mesh).Fillet;
                double before = Hausdorff(s.Mesh, s.Sharp);
                foreach (var kv in labels) {
                    DMesh3 result = Reconstruct(s.Mesh, kv.Value, out int moved);
                    int degenerate = 0, flipped = 0;
                    double scale = s.Mesh.CachedBounds.DiagonalLength;
                    foreach (int tid in result.TriangleIndices()) {
                        if (result.GetTriArea(tid) < 1e-12 * scale * scale) ++degenerate;
                        else if (result.GetTriNormal(tid).Dot(s.Mesh.GetTriNormal(tid)) < 0) ++flipped;
                    }
                    Console.WriteLine($"| {s.Name} | {kv.Key} | {f(before)} | {f(Hausdorff(result, s.Sharp))} | {f(Baselines.CreaseLength(s.Mesh, 30))} | {f(Baselines.CreaseLength(result, 30))} | {f(Baselines.CreaseLength(s.Sharp, 30))} | {moved} | {degenerate} | {flipped} |");
                }
            }
            return 0;
        }
    }
}
