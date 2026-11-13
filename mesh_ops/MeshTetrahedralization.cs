// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Tetrahedral mesh of any input (docs/design/TET_MESHING.md section 14, stage 1): the alpha wrap (MeshAlphaWrap), whose inside
    /// cells are already a valid tet mesh of the wrap surface, refined inside by Delaunay refinement (Shewchuk, "Tetrahedral mesh
    /// generation by Delaunay refinement", SoCG 1998; Cheng, Dey, Shewchuk, "Delaunay Mesh Generation", 2012): a bad inside tet
    /// (radius-edge ratio above MaxRadiusEdgeRatio, or circumradius above TargetEdgeLength) gets its circumcentre.
    ///
    /// The boundary is the wrap surface, exactly (same vertices, same triangles): a point is kept only if every cell its insertion
    /// removes is inside, otherwise the insertion is undone (Delaunay3.UndoLastInsert) and counted (refused_surface). Then no surface
    /// facet lies inside the cavity, so every surface facet stays a facet, and the new cells are inside; the test is on exact labels.
    /// Every tet is exactly positively oriented (Delaunay3). The price: a bad tet whose circumcentre lies beyond the surface, or
    /// whose cavity would reach it, stays (counted in unrefined_bad_tets); the surface's own quality bounds those tets.
    ///
    /// Termination: no point is inserted closer than MinFeatureFactor * min(Offset, Alpha - Offset) to a vertex (the floor, in units
    /// of the wrap's own vertex separation), so the vertices are a packing of a bounded region and the number of insertions is
    /// finite whatever the input's angles; tets whose point the floor refuses are counted (refused_floor, unrefined_bad_tets).
    ///
    /// Serial and deterministic; relative parameters only, so scaling the input by 2^k gives the scaled result bit for bit.
    /// </summary>
    public class MeshTetrahedralization
    {
        public DMesh3 Mesh;
        /// <summary> the wrap's parameters (MeshAlphaWrap.RelativeAlpha / RelativeOffset: the bounding box diagonal over alpha / offset) </summary>
        public double RelativeAlpha = 20, RelativeOffset = 600;
        /// <summary> radius-edge bound rho (> 2 for the termination argument without the floor) </summary>
        public double MaxRadiusEdgeRatio = 2;
        /// <summary> size bound: tets with a larger circumradius are refined; 0 = none (the mesh is graded by the surface) </summary>
        public double TargetEdgeLength = 0;
        /// <summary>
        /// sliver step (Li and Teng 2001): tets with a smallest dihedral angle below this (degrees) are refined too, and a new point
        /// is chosen among candidates near the circumcentre so that its new tets have no dihedral angle below it; 0 = off
        /// (plain circumcentres)
        /// </summary>
        public double SliverDihedralDegrees = 10;
        /// <summary> the floor in units of the wrap's vertex separation min(Offset, Alpha - Offset) </summary>
        public double MinFeatureFactor = 0.125;
        /// <summary> stop (status PointLimitReached, result still valid) after this many inserted points </summary>
        public int MaxSteinerPoints = 2000000;
        public Func<bool> CancelF = () => false;

        public enum TetStatus
        {
            Success = 0,
            /// <summary> the alpha wrap did not give a result (WrapStatus says why) </summary>
            WrapFailed = 1,
            /// <summary> MaxSteinerPoints reached: Result is valid but not fully refined </summary>
            PointLimitReached = 2,
            Cancelled = 3
        }

        public TetStatus Status;
        public MeshAlphaWrap.WrapStatus WrapStatus;
        /// <summary> the wrap of the last Compute(); its Result is the boundary of Result, triangle by triangle </summary>
        public MeshAlphaWrap Wrap;
        public TetMesh3 Result;
        /// <summary> tet_splits (inserted points), refused_floor, refused_surface (undone: the cavity reached the surface), unrefined_bad_tets </summary>
        public Dictionary<string, long> Counts = new Dictionary<string, long>();
        public double UsedAlpha, UsedOffset, UsedFloor;

        public MeshTetrahedralization(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        public bool Compute()
        {
            Result = null;
            Counts = new Dictionary<string, long>();
            var wrap = new MeshAlphaWrap(Mesh) { RelativeAlpha = RelativeAlpha, RelativeOffset = RelativeOffset, CancelF = CancelF };
            bool ok = wrap.Compute();
            Wrap = wrap;
            WrapStatus = wrap.Status;
            if (!ok || wrap.Status != MeshAlphaWrap.WrapStatus.Success) {
                Status = wrap.Status == MeshAlphaWrap.WrapStatus.Cancelled ? TetStatus.Cancelled : TetStatus.WrapFailed;
                return false;
            }
            UsedAlpha = wrap.UsedAlpha; UsedOffset = wrap.UsedOffset;
            UsedFloor = MinFeatureFactor * Math.Min(UsedOffset, UsedAlpha - UsedOffset);
            Delaunay3 dt = wrap.Triangulation.Clone();
            dt.RefinementSupport = true;
            foreach (int c in dt.CellIndices())
                dt.SetCellLabel(c, wrap.IsInsideCell(c) ? 1 : 0);
            var r = new Refiner(dt, UsedFloor, MaxRadiusEdgeRatio, TargetEdgeLength, SliverDihedralDegrees, MaxSteinerPoints, CancelF);
            TetStatus st = r.Run();
            Status = st;
            if (st == TetStatus.Cancelled) {
                foreach (var kv in r.Counts) Counts[kv.Key] = kv.Value;
                return false;
            }
            Result = r.Extract();
            Counts["unrefined_bad_tets"] = r.CountBadInside();
            foreach (var kv in r.Counts) Counts[kv.Key] = kv.Value;
            return st == TetStatus.Success;
        }

        /// <summary>
        /// The refinement of the inside cells (label 1) of a Delaunay3 whose inside / outside facets are the surface. The surface is
        /// never changed: a point is kept only if every cell its insertion removes is inside (then no surface facet is in the
        /// cavity's interior, so all of them stay facets, and the new cells are inside); otherwise the insertion is undone.
        /// Labels are exact, so this test is exact.
        /// </summary>
        internal sealed class Refiner
        {
            readonly Delaunay3 dt;
            readonly double floor, rho, size, sliver;
            readonly int maxPoints;
            readonly Func<bool> cancel;
            readonly Queue<(int cell, int stamp)> tetQueue = new Queue<(int, int)>();
            internal readonly Dictionary<string, long> Counts = new Dictionary<string, long>();
            readonly List<int> removed = new List<int>(), added = new List<int>();
            int inserted;
            readonly int firstSteiner;

            internal Refiner(Delaunay3 dt, double floor, double rho, double size, double sliver, int maxPoints, Func<bool> cancel)
            {
                this.dt = dt; this.floor = floor; this.rho = rho; this.size = size; this.sliver = sliver; this.maxPoints = maxPoints;
                this.cancel = cancel ?? (() => false);
                firstSteiner = dt.VertexCount;
                foreach (string k in new[] { "tet_splits", "refused_floor", "refused_surface", "trial_insertions", "sliver_free_choice" })
                    Counts[k] = 0;
                foreach (int c in dt.CellIndices())
                    if (dt.CellLabel(c) == 1) tetQueue.Enqueue((c, dt.CellStamp(c)));
            }

            void inc(string k) { Counts[k] = Counts[k] + 1; }

            internal TetStatus Run()
            {
                while (tetQueue.Count > 0) {
                    if (cancel()) return TetStatus.Cancelled;
                    if (inserted >= maxPoints) return TetStatus.PointLimitReached;
                    var (c, st) = tetQueue.Dequeue();
                    if (!dt.IsCell(c) || dt.CellStamp(c) != st || dt.CellLabel(c) != 1 || !is_bad(c)) continue;
                    split_tet(c);
                }
                return TetStatus.Success;
            }

            internal bool is_bad(int c)
            {
                Index4i t = dt.GetCell(c);
                Vector3d a = dt.GetVertex(t.a), b = dt.GetVertex(t.b), d = dt.GetVertex(t.c), e = dt.GetVertex(t.d);
                if (!TetMeshQuality.Measure(a, b, d, e, out double re, out double mind, out _)) return true;   // degenerate
                if (!(re <= rho)) return true;
                if (sliver > 0 && mind < sliver) return true;
                if (size > 0) {
                    Vector3d cc = Delaunay3.Circumcenter(a, b, d, e);
                    if (!(cc.DistanceSquared(a) <= size * size)) return true;
                }
                return false;
            }

            // fixed candidate directions (6 axes, 8 diagonals): no random numbers, so the result is deterministic and scales by 2^k
            static readonly Vector3d[] Directions = make_directions();
            static Vector3d[] make_directions()
            {
                var l = new List<Vector3d>();
                for (int i = 0; i < 3; ++i) for (int sgn = -1; sgn <= 1; sgn += 2) { var v = Vector3d.Zero; v[i] = sgn; l.Add(v); }
                double k = 1 / Math.Sqrt(3);
                for (int m = 0; m < 8; ++m) l.Add(new Vector3d((m & 1) != 0 ? k : -k, (m & 2) != 0 ? k : -k, (m & 4) != 0 ? k : -k));
                return l.ToArray();
            }
            static readonly double[] Rings = { 0.3, 0.6 };

            readonly List<Vector3d> candidates = new List<Vector3d>();

            // the smallest dihedral angle of the cells added by the last insertion
            double added_min_dihedral()
            {
                double q = 180;
                foreach (int nc in added) {
                    Index4i t = dt.GetCell(nc);
                    if (!TetMeshQuality.Measure(dt.GetVertex(t.a), dt.GetVertex(t.b), dt.GetVertex(t.c), dt.GetVertex(t.d), out _, out double mind, out _)) return 0;
                    q = Math.Min(q, mind);
                }
                return q;
            }

            // tentative insertion: Inserted and every removed cell inside (else undone); the new cells stay
            bool try_insert(Vector3d p, int hint, out bool surface)
            {
                surface = false;
                if (!(p.x == p.x && p.y == p.y && p.z == p.z)) return false;
                removed.Clear(); added.Clear();
                if (dt.Insert(p, hint, floor, out _, removed, added) != Delaunay3.InsertResult.Inserted) return false;
                Counts["trial_insertions"]++;
                foreach (int k in removed)        // dead, but their labels stay until the ids are reused
                    if (dt.CellLabel(k) != 1) { dt.UndoLastInsert(); surface = true; return false; }
                return true;
            }

            void split_tet(int c)
            {
                Index4i t = dt.GetCell(c);
                Vector3d cc = dt.Circumcenter(c);
                double R = cc.Distance(dt.GetVertex(t.a));
                // the circumcentre first; if it is refused by the floor or its new tets have a sliver: the rings around it (points
                // towards the centroid were tried for tets refused at the surface and made the result worse on the procedural set)
                int best = -1; double bestQ = -1; bool anySurface = false;
                candidates.Clear();
                candidates.Add(cc);
                for (int k = 0; k < candidates.Count; ++k) {
                    bool ok = try_insert(candidates[k], c, out bool surface);
                    anySurface |= surface;
                    if (k == 0) {
                        if (sliver > 0 && !surface)
                            foreach (double ring in Rings) foreach (Vector3d u in Directions) candidates.Add(cc + (ring * R) * u);
                    }
                    if (!ok) continue;
                    double q = sliver > 0 ? added_min_dihedral() : 180;
                    if (q >= sliver) { accept(k > 0); return; }
                    if (q > bestQ) { bestQ = q; best = k; }
                    dt.UndoLastInsert();
                }
                if (best >= 0 && try_insert(candidates[best], c, out _)) { accept(best > 0); return; }
                inc(anySurface ? "refused_surface" : "refused_floor");
            }

            void accept(bool moved)
            {
                inserted++;
                inc("tet_splits");
                if (moved) inc("sliver_free_choice");
                foreach (int nc in added) tetQueue.Enqueue((nc, dt.CellStamp(nc)));
            }

            internal long CountBadInside()
            {
                long n = 0;
                var byWrapVertices = new long[5];
                foreach (int c in dt.CellIndices())
                    if (dt.CellLabel(c) == 1 && is_bad(c)) {
                        n++;
                        int k = 0;
                        for (int i = 0; i < 4; ++i) if (dt.CellVertex(c, i) < firstSteiner) k++;
                        byWrapVertices[k]++;
                    }
                for (int k = 0; k <= 4; ++k) Counts["bad_with_" + k + "_wrap_vertices"] = byWrapVertices[k];
                return n;
            }

            internal TetMesh3 Extract()
            {
                var tm = new TetMesh3();
                var map = new Dictionary<int, int>();
                Func<int, int> vid = (v) => {
                    if (!map.TryGetValue(v, out int id)) { id = tm.Vertices.Count; tm.Vertices.Add(dt.GetVertex(v)); map[v] = id; }
                    return id;
                };
                foreach (int c in dt.CellIndices()) {
                    if (dt.CellLabel(c) != 1) continue;
                    Index4i q = dt.GetCell(c);
                    tm.Tets.Add(new Index4i(vid(q.a), vid(q.b), vid(q.c), vid(q.d)));
                    for (int i = 0; i < 4; ++i) {
                        int n = dt.Neighbour(c, i);
                        if (n >= 0 && dt.CellLabel(n) == 1) continue;
                        Index3i f = dt.FacetVertices(c, i);
                        tm.BoundaryTriangles.Add(new Index3i(vid(f.a), vid(f.b), vid(f.c)));
                    }
                }
                return tm;
            }
        }
    }
}
