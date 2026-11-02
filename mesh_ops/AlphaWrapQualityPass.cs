// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Triangle quality pass for a simplified alpha wrap (MeshRobustRepair.AlphaWrapQualityPass): edge flips and
    /// tangential vertex moves around triangles with a small minimum angle, each kept only if
    ///   - it raises the smallest angle of the changed triangles by at least MinGainDegrees,
    ///   - no changed triangle turns by more than MaxNormalTurnDegrees or becomes degenerate,
    ///   - every changed triangle lies within Tolerance of the reference (the unsimplified wrap), certified by
    ///     TriangleDeviationBounds.IsWithin (with the patch bound),
    ///   - and, with CheckSelfIntersections, no changed triangle intersects another one (exact test, as the Reducer's
    ///     PreventSelfIntersections).
    /// So the result stays within Tolerance of the reference wherever it changed, and keeps the topology (flips and
    /// moves only). Serial and in a fixed order: deterministic. Scale invariant (angles and relative thresholds only).
    /// </summary>
    internal sealed class AlphaWrapQualityPass
    {
        internal DMesh3 Mesh;
        internal readonly DMesh3 Reference;
        internal double Tolerance;
        internal bool CheckSelfIntersections = true;
        internal Func<bool> CancelF = () => false;

        /// <summary> triangles with a smaller minimum angle are improved (their edges flipped, their vertices moved) </summary>
        internal double TargetAngleDegrees = 30;
        internal double MinGainDegrees = 0.5;
        internal double MaxNormalTurnDegrees = 30;
        internal int MaxPasses = 8;
        /// <summary> (tests) the two kinds of operations </summary>
        internal bool EnableFlips = true, EnableMoves = true;
        /// <summary> move along the reference: the tangential target is moved onto the reference at the vertex's own offset from it </summary>
        internal bool FollowReference = false;
        /// <summary> no flip of an edge whose two triangles meet at a larger angle (a crease) </summary>
        internal double MaxFlipDihedralDegrees = 180;
        /// <summary> visit the edges and vertices in each pass by increasing smallest angle (worst first), not by id </summary>
        internal bool WorstFirst = false;

        // outputs
        internal int Flips, Moves, RejectedByDeviation, RejectedByIntersection, Passes;

        DMeshAABBTree3 referenceTree;
        TriangleDeviationBounds bounds;
        TriangleHashGrid grid;
        double cosTurn;

        internal AlphaWrapQualityPass(DMesh3 mesh, DMesh3 reference, double tolerance)
        {
            Mesh = mesh; Reference = reference; Tolerance = tolerance;
        }

        /// <summary> improves Mesh in place; false if cancelled (Mesh is then partly improved, still within Tolerance) </summary>
        internal bool Compute()
        {
            Prepare();
            for (int pass = 0; pass < MaxPasses; ++pass) {
                int before = Flips + Moves;
                Passes++;
                if ((EnableFlips && flip_pass() == false) || (EnableMoves && move_pass() == false))
                    return false;
                if (Flips + Moves == before)
                    break;
            }
            return true;
        }

        /// <summary> resets the counters and builds the queries (Compute does it; for tests of accept) </summary>
        internal void Prepare()
        {
            Flips = Moves = RejectedByDeviation = RejectedByIntersection = Passes = 0;
            referenceTree = new DMeshAABBTree3(Reference, true);
            bounds = new TriangleDeviationBounds(Reference) { PatchBound = true };
            cosTurn = Math.Cos(MaxNormalTurnDegrees * MathUtil.Deg2Rad);
            grid = CheckSelfIntersections ? build_grid() : null;
        }

        TriangleHashGrid build_grid()
        {
            double sum = 0; int n = 0;
            foreach (int eid in Mesh.EdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(eid);
                sum += Mesh.GetVertex(ev.a).Distance(Mesh.GetVertex(ev.b)); n++;
            }
            var g = new TriangleHashGrid(Mesh.CachedBounds.Min, Math.Max(1.5 * sum / Math.Max(1, n), MathUtil.ZeroTolerance));
            foreach (int tid in Mesh.TriangleIndices())
                g.Insert(tid, box(Mesh.GetVertex(Mesh.GetTriangle(tid).a), Mesh.GetVertex(Mesh.GetTriangle(tid).b), Mesh.GetVertex(Mesh.GetTriangle(tid).c)));
            return g;
        }

        static AxisAlignedBox3d box(Vector3d a, Vector3d b, Vector3d c)
        {
            var bx = new AxisAlignedBox3d(a); bx.Contain(b); bx.Contain(c);
            return bx;
        }

        static double min_angle(Vector3d a, Vector3d b, Vector3d c)
        {
            double A = angle(b - a, c - a), B = angle(a - b, c - b), C = angle(a - c, b - c);
            return Math.Min(A, Math.Min(B, C));
        }

        // angle between u and v in degrees (0 for a zero vector)
        static double angle(Vector3d u, Vector3d v)
        {
            double lu = u.Length, lv = v.Length;
            if (!(lu > 0 && lv > 0)) return 0;
            return Math.Acos(MathUtil.Clamp(u.Dot(v) / (lu * lv), -1, 1)) * MathUtil.Rad2Deg;
        }

        double min_angle(int tid)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return min_angle(Mesh.GetVertex(t.a), Mesh.GetVertex(t.b), Mesh.GetVertex(t.c));
        }

        // the unit normal of the triangle, or false if it is degenerate (relative to its size)
        static bool unit_normal(Vector3d a, Vector3d b, Vector3d c, out Vector3d n)
        {
            n = (b - a).Cross(c - a);
            double l = Math.Max((b - a).LengthSquared, Math.Max((c - b).LengthSquared, (a - c).LengthSquared));
            double len = n.Length;
            if (!(len > 1e-12 * l)) return false;
            n /= len;
            return true;
        }

        // new triangles (vertex ids, positions) replacing the triangles `removed`: within the tolerance and, if
        // checked, free of intersections with each other and with the rest of the mesh
        internal bool accept(List<Index3i> ids, List<Triangle3d> tris, HashSet<int> removed)
        {
            foreach (Triangle3d t in tris)
                if (!bounds.IsWithin(referenceTree, t.V0, t.V1, t.V2, Tolerance)) {
                    RejectedByDeviation++;
                    return false;
                }
            if (grid == null)
                return true;
            for (int i = 0; i < tris.Count; ++i) {
                Triangle3d A = tris[i];
                Index3i ia = ids[i];
                for (int j = i + 1; j < tris.Count; ++j) {
                    Triangle3d B = tris[j];
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) {
                        RejectedByIntersection++;
                        return false;
                    }
                }
                bool hit = false;
                grid.Query(box(A.V0, A.V1, A.V2), (tc) => {
                    if (hit || removed.Contains(tc) || Mesh.IsTriangle(tc) == false) return;
                    Index3i ic = Mesh.GetTriangle(tc);
                    Triangle3d C = new Triangle3d(Mesh.GetVertex(ic.a), Mesh.GetVertex(ic.b), Mesh.GetVertex(ic.c));
                    if (ExactTriangleIntersection.IsDegenerate(ref C)) return;
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ic, ref C))
                        hit = true;
                });
                if (hit) {
                    RejectedByIntersection++;
                    return false;
                }
            }
            return true;
        }

        internal void regrid(IEnumerable<int> removed, IEnumerable<int> added)
        {
            if (grid == null) return;
            foreach (int t in removed) grid.Remove(t);
            foreach (int t in added) {
                Index3i tv = Mesh.GetTriangle(t);
                grid.Insert(t, box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
            }
        }

        bool flip_pass()
        {
            var ids = new List<Index3i>(2);
            var tris = new List<Triangle3d>(2);
            var removed = new HashSet<int>();
            foreach (int eid in visit_order(Mesh.MaxEdgeID, true)) {
                if (CancelF()) return false;
                if (Mesh.IsEdge(eid) == false || Mesh.IsBoundaryEdge(eid)) continue;
                Index2i et = Mesh.GetEdgeT(eid);
                double cur = Math.Min(min_angle(et.a), min_angle(et.b));
                if (!(cur < TargetAngleDegrees)) continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                Index3i ta = Mesh.GetTriangle(et.a);
                // (a, b) in the order of triangle et.a
                if ((ta.a == ev.b && ta.b == ev.a) || (ta.b == ev.b && ta.c == ev.a) || (ta.c == ev.b && ta.a == ev.a))
                    ev = new Index2i(ev.b, ev.a);
                Index2i ov = Mesh.GetEdgeOpposingV(eid);
                if (Mesh.FindEdge(ov.a, ov.b) != DMesh3.InvalidID) continue;
                // the triangle et.a is (a, b, c), with c = its opposite vertex; et.b has the opposite vertex d
                int a = ev.a, b = ev.b;
                int c = opposite(et.a, a, b), d = opposite(et.b, a, b);
                Vector3d pa = Mesh.GetVertex(a), pb = Mesh.GetVertex(b), pc = Mesh.GetVertex(c), pd = Mesh.GetVertex(d);
                // after the flip: (c, a, d) and (d, b, c), the same orientation
                if (!(Math.Min(min_angle(pc, pa, pd), min_angle(pd, pb, pc)) >= cur + MinGainDegrees)) continue;
                Vector3d n0, n1, m0, m1;
                if (!unit_normal(pa, pb, pc, out n0) || !unit_normal(pb, pa, pd, out n1)) continue;
                if (!unit_normal(pc, pa, pd, out m0) || !unit_normal(pd, pb, pc, out m1)) continue;
                if (m0.Dot(n0) < cosTurn || m0.Dot(n1) < cosTurn || m1.Dot(n0) < cosTurn || m1.Dot(n1) < cosTurn) continue;
                if (n0.Dot(n1) < Math.Cos(MaxFlipDihedralDegrees * MathUtil.Deg2Rad)) continue;
                ids.Clear(); tris.Clear(); removed.Clear();
                ids.Add(new Index3i(c, a, d)); tris.Add(new Triangle3d(pc, pa, pd));
                ids.Add(new Index3i(d, b, c)); tris.Add(new Triangle3d(pd, pb, pc));
                removed.Add(et.a); removed.Add(et.b);
                if (accept(ids, tris, removed) == false) continue;
                DMesh3.EdgeFlipInfo info;
                if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok) continue;
                regrid(new[] { info.t0, info.t1 }, new[] { info.t0, info.t1 });
                Flips++;
            }
            return true;
        }

        Vector3d closest_on_reference(Vector3d p)
        {
            int t = referenceTree.FindNearestTriangle(p);
            return MeshQueries.TriangleDistance(Reference, t, p).TriangleClosest;
        }

        // the edges (or vertices) of this pass: by id, or with WorstFirst by the smallest angle of their triangles
        // (ties by id; deterministic)
        IEnumerable<int> visit_order(int maxId, bool edges)
        {
            if (WorstFirst == false) {
                for (int id = 0; id < maxId; ++id) yield return id;
                yield break;
            }
            var keyed = new List<KeyValuePair<double, int>>();
            for (int id = 0; id < maxId; ++id) {
                double a = double.MaxValue;
                if (edges) {
                    if (Mesh.IsEdge(id) == false) continue;
                    Index2i et = Mesh.GetEdgeT(id);
                    a = min_angle(et.a);
                    if (et.b >= 0) a = Math.Min(a, min_angle(et.b));
                } else {
                    if (Mesh.IsVertex(id) == false) continue;
                    foreach (int t in Mesh.VtxTrianglesItr(id)) a = Math.Min(a, min_angle(t));
                }
                if (a < TargetAngleDegrees) keyed.Add(new KeyValuePair<double, int>(a, id));
            }
            keyed.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : x.Value.CompareTo(y.Value));
            foreach (var kv in keyed) yield return kv.Value;
        }

        int opposite(int tid, int a, int b)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return (t.a != a && t.a != b) ? t.a : (t.b != a && t.b != b) ? t.b : t.c;
        }

        bool move_pass()
        {
            var ring = new List<int>();
            var ids = new List<Index3i>();
            var tris = new List<Triangle3d>();
            var removed = new HashSet<int>();
            foreach (int v in visit_order(Mesh.MaxVertexID, false)) {
                if (CancelF()) return false;
                if (Mesh.IsVertex(v) == false || Mesh.IsBoundaryVertex(v)) continue;
                ring.Clear();
                foreach (int t in Mesh.VtxTrianglesItr(v)) ring.Add(t);
                if (ring.Count < 3) continue;
                double cur = double.MaxValue;
                foreach (int t in ring) cur = Math.Min(cur, min_angle(t));
                if (!(cur < TargetAngleDegrees)) continue;
                // target: the centroid of the one ring's vertices, moved into the tangent plane at v
                Vector3d p = Mesh.GetVertex(v), cen = Vector3d.Zero, nrm = Vector3d.Zero;
                int k = 0;
                foreach (int u in Mesh.VtxVerticesItr(v)) { cen += Mesh.GetVertex(u); k++; }
                cen /= k;
                foreach (int t in ring) {
                    Index3i tv = Mesh.GetTriangle(t);
                    nrm += (Mesh.GetVertex(tv.b) - Mesh.GetVertex(tv.a)).Cross(Mesh.GetVertex(tv.c) - Mesh.GetVertex(tv.a));
                }
                if (!(nrm.Length > 0)) continue;
                nrm.Normalize();
                Vector3d q = cen - (cen - p).Dot(nrm) * nrm;
                if (FollowReference) {
                    // the closest point of the reference to q, plus the vertex's own offset from the reference
                    Vector3d cp = closest_on_reference(p), cq = closest_on_reference(q);
                    q = cq + (p - cp);
                }
                if (q == p) continue;
                double nw = double.MaxValue;
                bool ok = true;
                ids.Clear(); tris.Clear(); removed.Clear();
                foreach (int t in ring) {
                    Index3i tv = Mesh.GetTriangle(t);
                    Vector3d A = Mesh.GetVertex(tv.a), B = Mesh.GetVertex(tv.b), C = Mesh.GetVertex(tv.c);
                    Vector3d n0, n1;
                    if (!unit_normal(A, B, C, out n0)) { ok = false; break; }
                    if (tv.a == v) A = q; else if (tv.b == v) B = q; else C = q;
                    if (!unit_normal(A, B, C, out n1) || n1.Dot(n0) < cosTurn) { ok = false; break; }
                    nw = Math.Min(nw, min_angle(A, B, C));
                    ids.Add(tv); tris.Add(new Triangle3d(A, B, C)); removed.Add(t);
                }
                if (!ok || !(nw >= cur + MinGainDegrees)) continue;
                if (accept(ids, tris, removed) == false) continue;
                Mesh.SetVertex(v, q);
                regrid(ring, ring);
                Moves++;
            }
            return true;
        }
    }
}
