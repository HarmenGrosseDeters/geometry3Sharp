// "Snap back": after the wrap, every vertex moves onto the input surface (its nearest point there).
//   Naive: all vertices at once, no check (the upper bound of what a projection can remove of the offset error).
//   Safe:  a vertex moves only while exact checks hold for its ring; otherwise it moves a fraction of the way (1/2, 1/4, 1/8) or stays.
// The checks are those of the wrap's own post-passes (benchmarks/SharpFeatureProbe Snap.Check, AlphaWrapQualityPass): the ring triangles
// keep their orientation (no flip), are not degenerate (area >= 1e-12 diagonal^2, the threshold of MeshRobustRepair.NotCleanReason, and
// not exactly collinear), and do not intersect each other or any other triangle beyond the shared vertices/edge, tested exactly with
// ExactTriangleIntersection.IntersectsBeyondShared against a TriangleHashGrid of the current mesh. Deterministic (fixed vertex order, serial).
// What is NOT checked, unlike Snap.Check: the distance to the input and the intersection with the input (the result is meant to touch it).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;
using gs;

class BackStats
{
    public int Vertices, Full, Partial, None, Rounds;
    public int RejFlip, RejDegenerate, RejSelf;       // rejected trial moves (a vertex has up to 4 trials per round)
    public double MaxMoveRel;                        // longest full move / diagonal
    public double Ms;
}

static class SnapBack
{
    public static Vector3d Nearest(DMeshAABBTree3 tree, DMesh3 input, Vector3d p)
    {
        int tid = tree.FindNearestTriangle(p);
        Index3i t = input.GetTriangle(tid);
        var tri = new Triangle3d(input.GetVertex(t.a), input.GetVertex(t.b), input.GetVertex(t.c));
        var d = new DistPoint3Triangle3(p, tri);
        d.GetSquared();
        return d.TriangleClosest;
    }

    public static DMesh3 Naive(DMesh3 wrap, DMesh3 input, DMeshAABBTree3 tree)
    {
        var m = new DMesh3(wrap, true);
        var target = new Vector3d[m.MaxVertexID];
        foreach (int vid in m.VertexIndices()) target[vid] = Nearest(tree, input, m.GetVertex(vid));
        foreach (int vid in m.VertexIndices()) m.SetVertex(vid, target[vid]);
        return m;
    }

    // switches for the self-test (mutations of the check)
    public class Options
    {
        public bool CheckFlip = true, CheckDegenerate = true, CheckSelf = true;
        public int Rounds = 4;
        public double[] Fractions = { 1.0, 0.5, 0.25, 0.125 };
    }

    public static DMesh3 Safe(DMesh3 wrap, DMesh3 input, DMeshAABBTree3 tree, double diag, Options o, BackStats st)
    {
        var sw = Stopwatch.StartNew();
        var mesh = new DMesh3(wrap, true);
        TriangleHashGrid grid = Snap.BuildGrid(mesh);
        double minArea = 1e-12 * diag * diag;
        int nv = mesh.MaxVertexID;
        var target = new Vector3d[nv];
        var done = new bool[nv];        // reached its target exactly
        st.Vertices = mesh.VertexCount;
        foreach (int vid in mesh.VertexIndices()) target[vid] = Nearest(tree, input, mesh.GetVertex(vid));
        var ring = new List<int>(); var oldTris = new List<Triangle3d>(); var newTris = new List<Triangle3d>();
        var ids = new List<Index3i>(); var removed = new HashSet<int>();
        for (int round = 0; round < o.Rounds; ++round) {
            int moved = 0;
            foreach (int vid in mesh.VertexIndices()) {
                if (done[vid]) continue;
                Vector3d v = mesh.GetVertex(vid), goal = target[vid];
                if (v.Distance(goal) == 0) { done[vid] = true; continue; }
                ring.Clear(); mesh.GetVtxTriangles(vid, ring, true);
                oldTris.Clear();
                foreach (int tid in ring) oldTris.Add(Snap.Tri(mesh, tid));
                foreach (double s in o.Fractions) {
                    Vector3d x = s == 1.0 ? goal : v + s * (goal - v);
                    int reason = Check(mesh, vid, ring, oldTris, x, grid, minArea, o, removed, newTris, ids);
                    if (reason == 0) {
                        foreach (int tid in ring) grid.Remove(tid);
                        mesh.SetVertex(vid, x);
                        foreach (int tid in ring) grid.Insert(tid, Snap.Box(Snap.Tri(mesh, tid)));
                        ++moved;
                        if (s == 1.0) done[vid] = true;
                        break;
                    }
                    if (reason == 1) st.RejFlip++; else if (reason == 2) st.RejDegenerate++; else st.RejSelf++;
                }
            }
            st.Rounds = round + 1;
            if (moved == 0) break;
        }
        foreach (int vid in mesh.VertexIndices()) {
            Vector3d p = mesh.GetVertex(vid), w = wrap.GetVertex(vid);
            if (done[vid]) { st.Full++; st.MaxMoveRel = Math.Max(st.MaxMoveRel, w.Distance(p) / diag); }
            else if (p.Distance(w) > 0) st.Partial++; else st.None++;
        }
        st.Ms = sw.Elapsed.TotalMilliseconds;
        return mesh;
    }

    // for the self-test: the verdict of the ring check for moving vertex vid of mesh to x (0 ok, 1 flip, 2 degenerate, 3 self-intersection)
    public static int TestMove(DMesh3 mesh, int vid, Vector3d x, double diag, Options o)
    {
        TriangleHashGrid grid = Snap.BuildGrid(mesh);
        var ring = new List<int>(); mesh.GetVtxTriangles(vid, ring, true);
        var oldTris = new List<Triangle3d>();
        foreach (int tid in ring) oldTris.Add(Snap.Tri(mesh, tid));
        return Check(mesh, vid, ring, oldTris, x, grid, 1e-12 * diag * diag, o, new HashSet<int>(), new List<Triangle3d>(), new List<Index3i>());
    }

    // 0 ok, 1 flip, 2 degenerate, 3 self-intersection
    static int Check(DMesh3 mesh, int vid, List<int> ring, List<Triangle3d> oldTris, Vector3d x, TriangleHashGrid grid, double minArea,
        Options o, HashSet<int> removed, List<Triangle3d> newTris, List<Index3i> ids)
    {
        newTris.Clear(); ids.Clear(); removed.Clear();
        for (int i = 0; i < ring.Count; ++i) {
            Index3i tv = mesh.GetTriangle(ring[i]);
            Triangle3d t = oldTris[i];
            Vector3d a = tv.a == vid ? x : t.V0, b = tv.b == vid ? x : t.V1, c = tv.c == vid ? x : t.V2;
            Vector3d nOld = (t.V1 - t.V0).Cross(t.V2 - t.V0), nNew = (b - a).Cross(c - a);
            var tn = new Triangle3d(a, b, c);
            if (o.CheckDegenerate && (!(0.5 * nNew.Length >= minArea) || ExactTriangleIntersection.IsDegenerate(ref tn))) return 2;
            if (o.CheckFlip && !(nOld.Dot(nNew) > 0)) return 1;
            newTris.Add(tn); ids.Add(tv); removed.Add(ring[i]);
        }
        if (!o.CheckSelf) return 0;
        for (int i = 0; i < newTris.Count; ++i) {
            Triangle3d A = newTris[i];
            Index3i ia = ids[i];
            for (int j = i + 1; j < newTris.Count; ++j) {
                Triangle3d B = newTris[j];
                if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) return 3;
            }
            bool hit = false;
            grid.Query(Snap.Box(A), (tc) => {
                if (hit || removed.Contains(tc) || !mesh.IsTriangle(tc)) return;
                Index3i ic = mesh.GetTriangle(tc);
                Triangle3d C = new Triangle3d(mesh.GetVertex(ic.a), mesh.GetVertex(ic.b), mesh.GetVertex(ic.c));
                if (ExactTriangleIntersection.IsDegenerate(ref C)) return;
                if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ic, ref C)) hit = true;
            });
            if (hit) return 3;
        }
        return 0;
    }

    // statistics of a naive result against the wrap it came from: flipped / collapsed triangles and their area share (of the wrap's area)
    public static void Damage(DMesh3 wrap, DMesh3 snapped, double diag, out int flipped, out int degenerate, out double areaShare)
    {
        flipped = 0; degenerate = 0; double bad = 0, total = 0; double minArea = 1e-12 * diag * diag;
        foreach (int tid in wrap.TriangleIndices()) {
            Index3i t = wrap.GetTriangle(tid);
            Vector3d a = wrap.GetVertex(t.a), b = wrap.GetVertex(t.b), c = wrap.GetVertex(t.c);
            Vector3d a2 = snapped.GetVertex(t.a), b2 = snapped.GetVertex(t.b), c2 = snapped.GetVertex(t.c);
            Vector3d nOld = (b - a).Cross(c - a), nNew = (b2 - a2).Cross(c2 - a2);
            double area = 0.5 * nOld.Length; total += area;
            if (!(0.5 * nNew.Length >= minArea)) { degenerate++; bad += area; }
            else if (!(nOld.Dot(nNew) > 0)) { flipped++; bad += area; }
        }
        areaShare = total > 0 ? bad / total : 0;
    }
}
