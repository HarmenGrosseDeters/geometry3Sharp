// Variant C: a post-pass on a wrap that moves each vertex toward the quadric-error-function (QEF) point of the planes of the
// input triangles around it (the Hermite data of dual contouring, Ju et al. 2002: planes through the input's surface points
// with the input's normals; the minimiser with the small singular values cut off, relative to the vertex as mass point), but
// only while exact checks hold: the ring triangles do not intersect each other or the rest of the wrap (exact predicates, the
// check of AlphaWrapQualityPass), do not intersect the input, keep their orientation and the angle floors, and the vertex
// keeps a minimum distance from the input (the offset band). Deterministic: fixed vertex order, serial.
using System;
using System.Collections.Generic;
using g3;
using gs;

class SnapOptions
{
    /// <summary> planes are moved by Shift * offset toward the vertex's side (1: the offset surface's miter, 0: the input's own crease) </summary>
    public double Shift = 1;
    /// <summary> the moved vertex keeps at least this distance from the input, in offsets </summary>
    public double MinDistance = 0.9;
    /// <summary> input triangles within Radius * alpha of the vertex give planes </summary>
    public double Radius = 1.0;
    /// <summary> longest move, in alphas </summary>
    public double MaxMove = 1.0;
    /// <summary> singular values below this share of the largest are cut off (DualContouring.QEFEigenTolerance) </summary>
    public double EigenTolerance = 0.1;
    public int Rounds = 2;
    /// <summary> a ring triangle's min angle stays above min(its old min angle, this) </summary>
    public double MinAngleFloor = 10;
    /// <summary> and its max angle below max(its old max angle, this) </summary>
    public double MaxAngleCeiling = 150;
    /// <summary> ring triangle normals turn by at most this (degrees) </summary>
    public double MaxNormalTurn = 60;
}

class SnapStats
{
    public int Attempts, Moved, RejDistance, RejAngle, RejNormal, RejInput, RejSelf, NoPlanes, Tiny;
    public double Ms;
    public long TPlanes, TDist, TInput, TSelf, TOther;
}

static class Snap
{
    public static DMesh3 Run(DMesh3 wrap, DMesh3 input, DMeshAABBTree3 inputTree, TriangleHashGrid inputGrid, double alpha, double delta,
        SnapOptions o, SnapStats st)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        DMesh3 mesh = new DMesh3(wrap, true);
        TriangleHashGrid grid = BuildGrid(mesh);
        double R = o.Radius * alpha, maxMove = o.MaxMove * alpha;
        double cosTurn = Math.Cos(o.MaxNormalTurn * Math.PI / 180);
        var ring = new List<int>();
        var oldTris = new List<Triangle3d>();
        var newTris = new List<Triangle3d>();
        var ids = new List<Index3i>();
        var removed = new HashSet<int>();
        var planes = new List<(Vector3d n, double d, double w)>();
        for (int round = 0; round < o.Rounds; ++round) {
            int movedThisRound = 0;
            for (int vid = 0; vid < mesh.MaxVertexID; ++vid) {
                if (!mesh.IsVertex(vid)) continue;
                Vector3d v = mesh.GetVertex(vid);
                // the planes of the input around v (only triangles that face v: the far side of a thin wall is no data)
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                planes.Clear();
                var bx = new AxisAlignedBox3d(v - R * Vector3d.One, v + R * Vector3d.One);
                inputGrid.Query(bx, (tid) => {
                    Triangle3d t = new Triangle3d(input.GetVertex(input.GetTriangle(tid).a), input.GetVertex(input.GetTriangle(tid).b), input.GetVertex(input.GetTriangle(tid).c));
                    var dist = new DistPoint3Triangle3(v, t);
                    double d2 = dist.GetSquared();
                    if (d2 > R * R) return;
                    Vector3d n = t.Normal;
                    double area = 0.5 * (t.V1 - t.V0).Cross(t.V2 - t.V0).Length;
                    if (!(area > 1e-14 * alpha * alpha)) return;
                    Vector3d q = dist.TriangleClosest;
                    if (n.Dot(v - q) < -1e-9 * alpha) return;
                    // plane n.x = n.q + Shift * delta (toward v's side)
                    planes.Add((n, n.Dot(q) + o.Shift * delta, Math.Min(area, R * R)));
                });
                st.TPlanes += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                if (planes.Count == 0) { st.NoPlanes++; continue; }
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                Vector3d d = Qef(planes, v, o.EigenTolerance);
                st.TOther += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
                double len = d.Length;
                if (len < 1e-3 * delta) { st.Tiny++; continue; }
                if (len > maxMove) d *= maxMove / len;
                ++st.Attempts;
                ring.Clear();
                mesh.GetVtxTriangles(vid, ring, true);
                oldTris.Clear();
                foreach (int tid in ring) oldTris.Add(Tri(mesh, tid));
                bool done = false;
                int reason = 0;
                foreach (double s in new[] { 1.0, 0.5, 0.25 }) {
                    Vector3d x = v + s * d;
                    reason = Check(mesh, vid, ring, oldTris, x, input, inputTree, delta, o, cosTurn, grid, removed, newTris, ids, st);
                    if (reason == 0) {
                        // regrid and move
                        foreach (int tid in ring) grid.Remove(tid);
                        mesh.SetVertex(vid, x);
                        foreach (int tid in ring) grid.Insert(tid, Box(Tri(mesh, tid)));
                        ++st.Moved; ++movedThisRound;
                        done = true;
                        break;
                    }
                }
                if (!done) {
                    switch (reason) {
                        case 1: st.RejDistance++; break;
                        case 2: st.RejNormal++; break;
                        case 3: st.RejAngle++; break;
                        case 4: st.RejInput++; break;
                        default: st.RejSelf++; break;
                    }
                }
            }
            if (movedThisRound == 0) break;
        }
        st.Ms = sw.Elapsed.TotalMilliseconds;
        return mesh;
    }

    // 0: ok, 1: distance to the input, 2: normal turns / degenerate, 3: angle floors, 4: hits the input, 5: self-intersection
    static int Check(DMesh3 mesh, int vid, List<int> ring, List<Triangle3d> oldTris, Vector3d x, DMesh3 input, DMeshAABBTree3 inputTree, double delta,
        SnapOptions o, double cosTurn, TriangleHashGrid grid, HashSet<int> removed, List<Triangle3d> newTris, List<Index3i> ids, SnapStats st)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        double dist2;
        inputTree.FindNearestTriangle(x, out dist2);
        st.TDist += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        if (Math.Sqrt(dist2) < o.MinDistance * delta) return 1;
        newTris.Clear(); ids.Clear(); removed.Clear();
        for (int i = 0; i < ring.Count; ++i) {
            Index3i tv = mesh.GetTriangle(ring[i]);
            Triangle3d t = oldTris[i];
            Vector3d a = tv.a == vid ? x : t.V0, b = tv.b == vid ? x : t.V1, c = tv.c == vid ? x : t.V2;
            Vector3d nOld = (t.V1 - t.V0).Cross(t.V2 - t.V0), nNew = (b - a).Cross(c - a);
            double lo = nOld.Length, ln = nNew.Length;
            if (!(ln > 1e-12 * (b - a).LengthSquared) || !(ln > 1e-9 * lo)) return 2;
            if (nOld.Dot(nNew) / (lo * ln) < cosTurn) return 2;
            double minOld = MinAngle(t.V0, t.V1, t.V2), minNew = MinAngle(a, b, c);
            double maxOld = MaxAngle(t.V0, t.V1, t.V2), maxNew = MaxAngle(a, b, c);
            if (minNew < Math.Min(minOld, o.MinAngleFloor) - 1e-9 || maxNew > Math.Max(maxOld, o.MaxAngleCeiling) + 1e-9) return 3;
            newTris.Add(new Triangle3d(a, b, c));
            ids.Add(tv);
            removed.Add(ring[i]);
        }
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // (the simplified wrap itself can graze the input at a convex crease: a ring triangle that hit it before may hit it again)
        for (int i = 0; i < newTris.Count; ++i)
            if (inputTree.TestIntersection(newTris[i]) && !inputTree.TestIntersection(oldTris[i])) { st.TInput += System.Diagnostics.Stopwatch.GetTimestamp() - t0; return 4; }
        st.TInput += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < newTris.Count; ++i) {
            Triangle3d A = newTris[i];
            Index3i ia = ids[i];
            for (int j = i + 1; j < newTris.Count; ++j) {
                Triangle3d B = newTris[j];
                if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) { st.TSelf += System.Diagnostics.Stopwatch.GetTimestamp() - t0; return 5; }
            }
            bool hit = false;
            grid.Query(Box(A), (tc) => {
                if (hit || removed.Contains(tc) || mesh.IsTriangle(tc) == false) return;
                Index3i ic = mesh.GetTriangle(tc);
                Triangle3d C = new Triangle3d(mesh.GetVertex(ic.a), mesh.GetVertex(ic.b), mesh.GetVertex(ic.c));
                if (ExactTriangleIntersection.IsDegenerate(ref C)) return;
                if (ExactTriangleIntersection.IntersectsBeyondShared(ia, ref A, ic, ref C)) hit = true;
            });
            if (hit) { st.TSelf += System.Diagnostics.Stopwatch.GetTimestamp() - t0; return 5; }
        }
        st.TSelf += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        return 0;
    }

    // displacement from v to the minimiser of sum w (n.x - d)^2, singular values below tol * largest cut off
    static Vector3d Qef(List<(Vector3d n, double d, double w)> planes, Vector3d v, double tol)
    {
        double[,] A = new double[3, 3];
        double[] b = new double[3];
        double wsum = 0;
        foreach (var p in planes) wsum += p.w;
        foreach (var p in planes) {
            double w = p.w / wsum;
            double r = p.d - p.n.Dot(v);
            for (int i = 0; i < 3; ++i) {
                b[i] += w * p.n[i] * r;
                for (int j = 0; j < 3; ++j) A[i, j] += w * p.n[i] * p.n[j];
            }
        }
        double[] ev; double[,] vec;
        Jacobi(A, out ev, out vec);
        double max = Math.Max(ev[0], Math.Max(ev[1], ev[2]));
        Vector3d d = Vector3d.Zero;
        for (int k = 0; k < 3; ++k) {
            if (ev[k] < tol * max || ev[k] <= 0) continue;
            double proj = vec[0, k] * b[0] + vec[1, k] * b[1] + vec[2, k] * b[2];
            d += (proj / ev[k]) * new Vector3d(vec[0, k], vec[1, k], vec[2, k]);
        }
        return d;
    }

    static void Jacobi(double[,] a0, out double[] ev, out double[,] v)
    {
        double[,] a = (double[,])a0.Clone();
        v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; ++sweep) {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (off < 1e-15) break;
            for (int p = 0; p < 2; ++p) for (int q = p + 1; q < 3; ++q) {
                if (Math.Abs(a[p, q]) < 1e-18) continue;
                double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                for (int k = 0; k < 3; ++k) {
                    double akp = a[k, p], akq = a[k, q];
                    a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                }
                for (int k = 0; k < 3; ++k) {
                    double apk = a[p, k], aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                }
                for (int k = 0; k < 3; ++k) {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq; v[k, q] = s * vkp + c * vkq;
                }
            }
        }
        ev = new[] { a[0, 0], a[1, 1], a[2, 2] };
    }

    public static TriangleHashGrid BuildGrid(DMesh3 mesh)
    {
        double sum = 0; int n = 0;
        foreach (int eid in mesh.EdgeIndices()) {
            Index2i ev = mesh.GetEdgeV(eid);
            sum += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)); n++;
        }
        var g = new TriangleHashGrid(mesh.CachedBounds.Min, Math.Max(1.5 * sum / Math.Max(1, n), MathUtil.ZeroTolerance));
        foreach (int tid in mesh.TriangleIndices()) g.Insert(tid, Box(Tri(mesh, tid)));
        return g;
    }

    public static Triangle3d Tri(DMesh3 m, int tid)
    {
        Index3i t = m.GetTriangle(tid);
        return new Triangle3d(m.GetVertex(t.a), m.GetVertex(t.b), m.GetVertex(t.c));
    }

    public static AxisAlignedBox3d Box(Triangle3d t)
    {
        var bx = new AxisAlignedBox3d(t.V0); bx.Contain(t.V1); bx.Contain(t.V2);
        return bx;
    }

    static double Angle(Vector3d u, Vector3d v)
    {
        double lu = u.Length, lv = v.Length;
        if (!(lu > 0 && lv > 0)) return 0;
        return Math.Acos(MathUtil.Clamp(u.Dot(v) / (lu * lv), -1, 1)) * MathUtil.Rad2Deg;
    }
    public static double MinAngle(Vector3d a, Vector3d b, Vector3d c) { return Math.Min(Angle(b - a, c - a), Math.Min(Angle(a - b, c - b), Angle(a - c, b - c))); }
    public static double MaxAngle(Vector3d a, Vector3d b, Vector3d c) { return Math.Max(Angle(b - a, c - a), Math.Max(Angle(a - b, c - b), Angle(a - c, b - c))); }
}
