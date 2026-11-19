// P4(b), S1-F (docs/design/TET_MESHING.md 17.14): the featured wrap surface (AlphaWrapFeaturePass) as the boundary of the tet mesh.
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// A closed surface (the wrap after the feature pass) and a Delaunay3 of its vertices in which every surface triangle is a facet, so that
    /// the cells inside it (label 1) are a tet mesh of exactly that surface (docs/design/TET_MESHING.md 14.2, 17.14). The raw featured surface
    /// is not Delaunay conforming (1.3 to 3.7 % of its triangles are missing facets, section 13). They are recovered in two steps:
    /// (1) flips of surface edges that are missing in the Delaunay3 whose flipped edge is present, only across convex, (nearly, within
    /// 20 degrees) flat quads, so the surface changes by at most a small fold, which the final check against the input covers; (2) Steiner points: the midpoint of a missing edge, else the circumcentre of a missing triangle in its plane
    /// (the midpoint of its longest edge if the circumcentre is outside), then flips again. A point never moves a surface vertex and lies on
    /// the surface up to the rounding of a midpoint. The recovery has a budget (maxSteiner, rounds) and a floor (no edge shorter than
    /// 2 minEdge is split); if it does not finish, Recover returns null and the caller meshes the rounded wrap (S1-R) instead:
    /// the result is valid or absent, never half recovered. Serial and deterministic (fixed orders); relative thresholds only.
    /// </summary>
    internal sealed class TetFeatureSurface
    {
        internal DMesh3 Surface;
        internal Delaunay3 Dt;
        internal Dictionary<int, int> Ids;
        /// <summary> counts of the run: flips, Steiner points, rounds, missing triangles / edges before recovery </summary>
        internal string FailReason;
        internal int Flips, Steiner, Rounds, MissingTrianglesBefore, MissingEdgesBefore;

        /// <summary> the most flips of one round and the rounds of the recovery </summary>
        internal const int MaxRounds = 60;
        const double FlatCos = 0.9396926207859084;          // cos 20 degrees: flip only across a (nearly) flat quad
        const double MinAngle = 10 * Math.PI / 180;

        /// <summary>
        /// Recovers the surface (changed in place: flips and splits only) in a fresh Delaunay3; null if the budget or the floor stops it,
        /// the surface is not closed and manifold without self-intersection afterwards, or cancel is set.
        /// </summary>
        internal static TetFeatureSurface Recover(DMesh3 surface, double minEdge, int maxSteiner, Func<bool> cancel, DMesh3 input = null)
        {
            var r = new TetFeatureSurface { Surface = surface };
            if (surface.TriangleCount == 0) return null;
            AxisAlignedBox3d box = surface.GetBounds();
            double pad = Math.Max(box.DiagonalLength, double.Epsilon);
            r.Dt = new Delaunay3(new AxisAlignedBox3d(box.Min - new Vector3d(pad, pad, pad), box.Max + new Vector3d(pad, pad, pad))) { RefinementSupport = true };
            r.Ids = new Dictionary<int, int>();
            foreach (int v in surface.VertexIndices())
                if (!r.add_vertex(v)) return fail(r, "insert vertex");
            var missT = new List<int>(); var missE = new List<int>();
            r.collect(missT, missE);
            r.MissingTrianglesBefore = missT.Count; r.MissingEdgesBefore = missE.Count;
            double minEdgeSqr4 = 4 * minEdge * minEdge;
            for (; ; ) {
                if (cancel != null && cancel()) return null;
                r.flip_to_delaunay(cancel);
                r.collect(missT, missE);
                if (missT.Count == 0 && missE.Count == 0) break;
                if (r.Rounds >= MaxRounds || r.Steiner > maxSteiner) return fail(r, "budget rounds=" + r.Rounds + " steiner=" + r.Steiner + " missT=" + missT.Count + " missE=" + missE.Count);
                r.Rounds++;
                if (missE.Count > 0) {
                    foreach (int e in missE) {
                        if (!surface.IsEdge(e)) continue;
                        if (!r.split_edge(e, minEdgeSqr4)) return fail(r, "split edge " + r.FailReason);
                    }
                } else {
                    foreach (int t in missT) {
                        if (!r.treat_triangle(t, minEdgeSqr4)) return fail(r, "treat triangle " + r.FailReason);
                    }
                }
            }
            string nc = MeshRobustRepair.NotCleanReason(surface); if (nc != "") return fail(r, "not clean: " + nc);
            if (input != null && touches_input(surface, input)) return fail(r, "touches the input");
            r.label();
            return r;
        }

        internal static string LastFailure;
        static TetFeatureSurface fail(TetFeatureSurface r, string why) { LastFailure = why; return null; }

        // the recovered surface (flips and splits change it by at most a flat quad's fold) must not touch the input (exact)
        static bool touches_input(DMesh3 surface, DMesh3 input)
        {
            var tree = new DMeshAABBTree3(input, true);
            var tris = new List<Triangle3d>();
            foreach (int t in surface.TriangleIndices()) {
                Index3i tv = surface.GetTriangle(t);
                tris.Add(new Triangle3d(surface.GetVertex(tv.a), surface.GetVertex(tv.b), surface.GetVertex(tv.c)));
            }
            return Reducer.TrianglesTouch(tree, tris);
        }

        bool add_vertex(int v)
        {
            var ir = Dt.Insert(Surface.GetVertex(v), -1, 0, out int dv); if (ir != Delaunay3.InsertResult.Inserted) { FailReason = "insert " + ir; return false; }
            Ids[v] = dv;
            return true;
        }

        void collect(List<int> tris, List<int> edges)
        {
            tris.Clear(); edges.Clear();
            foreach (int t in Surface.TriangleIndices()) {
                Index3i tv = Surface.GetTriangle(t);
                if (!Dt.IsFacet(Ids[tv.a], Ids[tv.b], Ids[tv.c], out _, out _)) tris.Add(t);
            }
            foreach (int e in Surface.EdgeIndices()) {
                Index2i ev = Surface.GetEdgeV(e);
                if (!Dt.IsEdge(Ids[ev.a], Ids[ev.b], out _)) edges.Add(e);
            }
        }

        // the smallest angle of the triangle (radians)
        double min_angle(int t)
        {
            Index3i tv = Surface.GetTriangle(t);
            Vector3d a = Surface.GetVertex(tv.a), b = Surface.GetVertex(tv.b), c = Surface.GetVertex(tv.c);
            return Math.Min(angle(a, b, c), Math.Min(angle(b, c, a), angle(c, a, b)));
        }
        static double angle(Vector3d p, Vector3d q, Vector3d r)
        {
            Vector3d u = q - p, v = r - p;
            double lu = u.Length, lv = v.Length;
            if (!(lu > 0 && lv > 0)) return 0;
            return Math.Acos(Math.Max(-1, Math.Min(1, u.Dot(v) / (lu * lv))));
        }

        // flips of missing edges whose flipped edge is a Delaunay edge, across flat quads, keeping the angles; until a pass flips nothing
        void flip_to_delaunay(Func<bool> cancel)
        {
            var missT = new List<int>(); var missE = new List<int>();
            for (int pass = 0; pass < 30; ++pass) {
                if (cancel != null && cancel()) return;
                collect(missT, missE);
                int done = 0;
                foreach (int e in missE) {
                    if (!Surface.IsEdge(e)) continue;
                    Index2i et = Surface.GetEdgeT(e);
                    if (et.b == DMesh3.InvalidID) continue;
                    Index2i op = Surface.GetEdgeOpposingV(e);
                    if (!Dt.IsEdge(Ids[op.a], Ids[op.b], out _)) continue;
                    if (Surface.GetTriNormal(et.a).Dot(Surface.GetTriNormal(et.b)) < FlatCos) continue;
                    double before = Math.Min(min_angle(et.a), min_angle(et.b));
                    Vector3d n0 = Surface.GetTriNormal(et.a), n1 = Surface.GetTriNormal(et.b);
                    if (Surface.FlipEdge(e, out _) != MeshResult.Ok) continue;
                    // kept only if the quad was convex (the new triangles agree with both old normals: no fold) and the angles hold
                    Vector3d m0 = Surface.GetTriNormal(et.a), m1 = Surface.GetTriNormal(et.b);
                    bool ok = m0.Dot(n0) >= FlatCos && m0.Dot(n1) >= FlatCos && m1.Dot(n0) >= FlatCos && m1.Dot(n1) >= FlatCos
                        && Math.Min(min_angle(et.a), min_angle(et.b)) >= Math.Min(MinAngle, before);
                    if (!ok) { Surface.FlipEdge(e, out _); continue; }
                    done++;
                }
                Flips += done;
                if (done == 0) break;
            }
        }

        bool split_edge(int e, double minEdgeSqr4)
        {
            Index2i ev = Surface.GetEdgeV(e);
            if (Surface.GetVertex(ev.a).DistanceSquared(Surface.GetVertex(ev.b)) < minEdgeSqr4) { FailReason = "floor"; return false; }   // the floor
            if (Surface.SplitEdge(e, out DMesh3.EdgeSplitInfo info, 0.5) != MeshResult.Ok) return false;
            Steiner++;
            return add_vertex(info.vNew);
        }

        // a missing triangle whose edges are all present: its circumcentre (in its plane) if that is inside it, else its longest edge's midpoint
        bool treat_triangle(int t, double minEdgeSqr4)
        {
            if (!Surface.IsTriangle(t)) return true;
            Index3i tv = Surface.GetTriangle(t);
            if (Dt.IsFacet(Ids[tv.a], Ids[tv.b], Ids[tv.c], out _, out _)) return true;       // fixed by an earlier split of this round
            Vector3d a = Surface.GetVertex(tv.a), b = Surface.GetVertex(tv.b), c = Surface.GetVertex(tv.c);
            Vector3d ab = b - a, ac = c - a, n = ab.Cross(ac);
            double nn = n.LengthSquared;
            if (!(nn > 0)) return false;
            // circumcentre: a + ((|ac|^2 ab - |ab|^2 ac) x n) ... (standard formula), barycentric coordinates of it
            Vector3d cc = a + (n.Cross(ab) * ac.LengthSquared + ac.Cross(n) * ab.LengthSquared) / (2 * nn);
            Vector3d ap = cc - a;
            double d00 = ab.Dot(ab), d01 = ab.Dot(ac), d11 = ac.Dot(ac), d20 = ap.Dot(ab), d21 = ap.Dot(ac);
            double den = d00 * d11 - d01 * d01;
            double v = (d11 * d20 - d01 * d21) / den, w = (d00 * d21 - d01 * d20) / den, u = 1 - v - w;
            double r2 = cc.DistanceSquared(a);
            if (u > 0.05 && v > 0.05 && w > 0.05 && r2 >= minEdgeSqr4 / 4) {
                if (Surface.PokeTriangle(t, new Vector3d(u, v, w), out DMesh3.PokeTriangleInfo pi) != MeshResult.Ok) return false;
                Surface.SetVertex(pi.new_vid, cc);
                Steiner++;
                return add_vertex(pi.new_vid);
            }
            Index3i te = Surface.GetTriEdges(t);
            int best = te.a; double bl = -1;
            for (int k = 0; k < 3; ++k) {
                Index2i kv = Surface.GetEdgeV(te[k]);
                double l = Surface.GetVertex(kv.a).DistanceSquared(Surface.GetVertex(kv.b));
                if (l > bl) { bl = l; best = te[k]; }
            }
            return split_edge(best, minEdgeSqr4);
        }

        // cell labels: 0 outside = reachable from the box corners without crossing a surface triangle, 1 inside
        void label()
        {
            var surf = new HashSet<(int, int, int)>();
            foreach (int t in Surface.TriangleIndices()) {
                Index3i tv = Surface.GetTriangle(t);
                surf.Add(key(Ids[tv.a], Ids[tv.b], Ids[tv.c]));
            }
            var stack = new Stack<int>();
            foreach (int c in Dt.CellIndices()) {
                Index4i q = Dt.GetCell(c);
                bool corner = q.a < 8 || q.b < 8 || q.c < 8 || q.d < 8;
                Dt.SetCellLabel(c, corner ? 0 : 1);
                if (corner) stack.Push(c);
            }
            while (stack.Count > 0) {
                int c = stack.Pop();
                for (int i = 0; i < 4; ++i) {
                    int n = Dt.Neighbour(c, i);
                    if (n < 0 || Dt.CellLabel(n) == 0) continue;
                    Index3i f = Dt.FacetVertices(c, i);
                    if (surf.Contains(key(f.a, f.b, f.c))) continue;
                    Dt.SetCellLabel(n, 0);
                    stack.Push(n);
                }
            }
        }

        static (int, int, int) key(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return (a, b, c);
        }
    }
}
