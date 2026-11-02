// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

/// <summary>
/// `envelope [rounds]`: PolyhedralEnvelope.ContainsSegment on spheres, and the share of its Orient3dLPI calls
/// (the upper bound of a prepare / evaluate split, docs/design/ENVELOPE.md section 6, 1b): the C2 loop replayed with
/// the same candidates, crossings and early exits, timing only the Orient3dLPI calls.
/// </summary>
static class EnvelopeBench
{
    public static int Run(string[] args)
    {
        int rounds = args.Length > 0 ? int.Parse(args[0]) : 5;
        Console.WriteLine("mesh            eps      segments  contained  us/segment  LPI calls/seg  LPI us/seg  LPI share");
        foreach ((int n, double epsRel) in new[] { (10, 0.5), (20, 0.5), (20, 0.2), (40, 0.5) }) {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            double edge = 0;
            foreach (int e in mesh.EdgeIndices()) { Index2i ev = mesh.GetEdgeV(e); edge += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); }
            edge /= mesh.EdgeCount;
            double eps = epsRel * edge;
            var env = new PolyhedralEnvelope(mesh, eps);
            var rand = new Random(1);
            var segs = new List<(Vector3d, Vector3d)>();
            var tids = mesh.TriangleIndices().ToList();
            for (int i = 0; i < 2000; ++i) {
                int t = tids[rand.Next(tids.Count)];
                Vector3d c = mesh.GetTriCentroid(t), nn = mesh.GetTriNormal(t);
                Vector3d d = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                d -= d.Dot(nn) * nn;
                Vector3d a = c + (rand.NextDouble() - 0.5) * eps * nn, b = a + 2 * edge * d.Normalized + (rand.NextDouble() - 0.5) * eps * nn;
                segs.Add((a, b));
            }
            double best = double.MaxValue, bestLpi = double.MaxValue;
            long lpiCalls = 0; int contained = 0;
            for (int r = 0; r < rounds; ++r) {
                var sw = Stopwatch.StartNew();
                int yes = 0;
                foreach ((Vector3d a, Vector3d b) in segs) if (env.ContainsSegment(a, b)) yes++;
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000 / segs.Count);
                contained = yes;
                // the replay
                long calls = 0;
                var sl = new Stopwatch();
                foreach ((Vector3d a, Vector3d b) in segs) {
                    List<int> cand = env.candidates(a, b);
                    bool ia = cand.Any(p => inside(env, p, a)), ib = cand.Any(p => inside(env, p, b));
                    if (!ia || !ib) continue;
                    bool ok = true;
                    foreach (int p in cand) {
                        for (int f = 0; f < env.FacetCount(p) && ok; ++f) {
                            env.GetFacet(p, f, out Vector3d r0, out Vector3d s0, out Vector3d t0);
                            if (ExactPredicates.Orient3d(r0, s0, t0, a) * ExactPredicates.Orient3d(r0, s0, t0, b) >= 0) continue;
                            bool covered = false;
                            foreach (int p2 in cand) {
                                if (p2 == p) continue;
                                bool inP2 = true;
                                for (int g = 0; g < env.FacetCount(p2); ++g) {
                                    env.GetFacet(p2, g, out Vector3d g0, out Vector3d g1, out Vector3d g2);
                                    sl.Start();
                                    int sgn = IndirectPredicates.Orient3dLPI(a, b, r0, s0, t0, g0, g1, g2);
                                    sl.Stop();
                                    calls++;
                                    if (sgn >= 0) { inP2 = false; break; }
                                }
                                if (inP2) { covered = true; break; }
                            }
                            if (!covered) ok = false;
                        }
                        if (!ok) break;
                    }
                }
                lpiCalls = calls;
                bestLpi = Math.Min(bestLpi, sl.Elapsed.TotalMilliseconds * 1000 / segs.Count);
            }
            Console.WriteLine($"sphere {n,-3} {epsRel,4} edge  {segs.Count,8}  {contained,9}  {best,10:F1}  {lpiCalls / (double)segs.Count,13:F0}  {bestLpi,10:F1}  {100 * bestLpi / best,8:F1} %");
        }
        return 0;
    }

    static bool inside(PolyhedralEnvelope env, int p, Vector3d x)
    {
        for (int f = 0; f < env.FacetCount(p); ++f) {
            env.GetFacet(p, f, out Vector3d q0, out Vector3d q1, out Vector3d q2);
            if (ExactPredicates.Orient3d(q0, q1, q2, x) >= 0) return false;
        }
        return true;
    }
}
