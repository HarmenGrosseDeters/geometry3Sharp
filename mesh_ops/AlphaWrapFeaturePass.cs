// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Sharp-feature pass on a raw alpha wrap (Alpha Wrap 3.0 lever 6, variant K of the sharp-feature study, in the manner of
    /// Kobbelt et al. 2001, feature-sensitive surface extraction: a feature sample where the surface's planes disagree, and
    /// edges along the feature), run between the wrap and its simplification:
    ///   1. plane snap: each vertex onto the plane of its nearest input triangle, shifted outward by one offset (the offset
    ///      surface's face there);
    ///   2. crease points: an edge whose two end planes differ by more than CreaseAngleDegrees crosses a crease; its crease point
    ///      is the point of the two planes' intersection line nearest the edge's midpoint, accepted only within MaxMiterDistance
    ///      offsets of the miter line of an input sharp edge (the edge moved onto the offset surface's crease, the shift capped at
    ///      MaxMiterShift offsets); with VarifoldCreases, accepted where the input's unoriented varifold has a crease between the
    ///      two planes there instead (VarifoldCreaseQuery: no input connectivity needed, so unwelded soups work);
    ///   3. crease-line snap: a vertex with such a crease point in the near SnapFraction of one of its edges moves onto that line;
    ///   4. the remaining crossing edges are split at their crease points (two crossing edges of a triangle then give an edge
    ///      along the crease), and an edge whose opposite vertices are both crease vertices is flipped onto the crease.
    /// Every operation is kept only if its new triangles are not degenerate, turn by at most MaxNormalTurnDegrees, keep the angle
    /// floors (smallest angle not below min(old, MinAngleFloorDegrees), largest not above max(old, MaxAngleCeilingDegrees),
    /// against the triangles before the operation), do not intersect each other or the rest of the mesh (exact,
    /// TriangleTriangleFilter as in AlphaWrapQualityPass), do not touch the input (exact, Reducer.TrianglesTouch), sweep over no
    /// input triangle (exact: no input triangle meets the tetrahedra between the old and the new surface), and keep every moved
    /// vertex at least MinDistance offsets from the input. So the pass keeps the wrap closed, 2-manifold (moves, splits and
    /// flips only), free of self-intersections and strictly enclosing the input; it changes the offset band only by moving
    /// vertices onto the offset surface's planes and creases. Serial, in a fixed order: deterministic; relative thresholds only:
    /// scaling by 2^k gives the scaled result bit for bit. Corners (three planes) are not handled.
    /// The validity statement and its proof (which checks it uses, and what it assumes of the mesh handed in):
    /// docs/design/ALPHA_WRAP_FEATURE_VALIDITY.md.
    /// </summary>
    internal sealed class AlphaWrapFeaturePass
    {
        internal DMesh3 Mesh;
        readonly DMesh3 input;
        readonly double alpha, offset;
        internal Func<bool> CancelF = () => false;

        internal double CreaseAngleDegrees = 30;
        internal double SnapFraction = 0.3;
        /// <summary> a crease point lies within this many offsets of an input sharp edge's miter line </summary>
        internal double MaxMiterDistance = 1.0;
        /// <summary> the miter line's shift from the input edge is capped at this many offsets (nearly folded creases) </summary>
        internal double MaxMiterShift = 4.0;
        /// <summary> input triangles farther than this many alphas from a vertex give it no plane </summary>
        internal double PlaneRadius = 1.5;
        /// <summary> a moved or new vertex keeps at least this many offsets from the input </summary>
        internal double MinDistance = 0.9;
        internal double MinAngleFloorDegrees = 10, MaxAngleCeilingDegrees = 150, MaxNormalTurnDegrees = 100;
        internal bool EnableFlips = true;
        /// <summary>
        /// the input's creases from its unoriented varifold instead of its edges' dihedral angles (an unwelded, duplicated,
        /// flipped or T-junctioned soup has no two-triangle edges, so the dihedral finds none there): at every crease point the
        /// pass asks VarifoldCreaseQuery whether the input's two planes there really meet at a crease of at least
        /// CreaseAngleDegrees (docs/design/VARIFOLD_CREASE_INPUT.md). Off by default; off, the pass is the one before, bit for bit.
        /// </summary>
        internal bool VarifoldCreases = false;
        /// <summary>
        /// with VarifoldCreases: a vertex's plane is the input's varifold plane at its nearest input point (the mass-weighted
        /// plane of the triangles there within 20 degrees of the nearest one's, ball VarifoldPlaneRadius alphas) instead of the
        /// nearest triangle's own plane, where that cluster is planar; 0 keeps the nearest triangle's plane. On a jittered or
        /// noisy soup the nearest triangle's tilt is noise; the fitted plane averages it out (docs/design/VARIFOLD_CREASE_INPUT.md 10)
        /// </summary>
        internal double VarifoldPlaneRadius = 1.5;
        /// <summary>
        /// the fitted plane is moved outward by this many of its rms (the envelope of a jittered sheet: the pass keeps
        /// MinDistance offsets from the outermost triangles, which the mean plane lies below)
        /// </summary>
        internal double VarifoldPlaneLift = 2;
        /// <summary>
        /// the fitted planes are used only on a noisy input: when the median noise of its sheets (VarifoldCreaseQuery.MedianNoise,
        /// 512 triangles at a fixed stride: the plane residual at the plane radius and at twice it, the part that grows neither
        /// as r^2 nor as r^4, relative to the radius) is above this; 0 uses them always. Measured (design note 10): 0 on 32 clean
        /// flat and curved tessellations (cylinders of 12-256 slices, spheres, tori, fillets of 1-3 balls, at alpha = diagonal /
        /// 60 and / 300); jitter of 0.005 h 0.0011-0.0013, 0.01 h 0.0022-0.0025, 0.025 h 0.0056-0.0062
        /// </summary>
        internal double VarifoldPlaneMinNoise = 0.0005;
        /// <summary> (tests) false: the labels are made lazily, serially, as without the fitted planes (the same bits either way) </summary>
        internal bool VarifoldPrefetch = true;
        /// <summary> (outputs) the median tilt measured, and whether the fitted planes were used </summary>
        internal double VarifoldMedianTilt, VarifoldMedianNoise;
        internal bool VarifoldPlanesUsed;
        /// <summary> the ball of the varifold crease test, in alphas </summary>
        internal double VarifoldRadius = 3.5;
        /// <summary> the varifold crease test's distance of each plane from the crease, in offsets (plus 3 rms of the plane) </summary>
        internal double VarifoldTolerance = 0.5;
        /// <summary> (tests) false: the exact sweep test is skipped (only the end state is checked) </summary>
        internal bool CheckSweep = true;

        // outputs
        /// <summary>
        /// (InputCreases: with the dihedral source the miter segments of the input's sharp edges, two per edge; with
        /// VarifoldCreases the crease points the varifold test accepted, counted per query, so a point asked in both the snap
        /// and the split pass counts twice)
        /// </summary>
        internal int PlaneMoves, CreaseSnaps, Splits, Flips, Rejected, CrossingEdges, InputCreases;
        /// <summary> (benchmarks) VarifoldCreases: the queries asked, and the Stopwatch ticks of building the measure and of the queries </summary>
        internal int VarifoldQueries;
        internal long VarifoldBuildTicks, VarifoldQueryTicks, VarifoldPlaneTicks;
        internal int VarifoldPlaneQueries, VarifoldPlanesFitted;

        DMeshAABBTree3 inputTree;
        TriangleHashGrid grid;
        SegmentGrid miters;
        VarifoldCreaseQuery varifold;
        double cosTurn, cosCrease;

        /// <summary> mesh: the raw wrap (changed in place); input: the wrap's input; alpha, offset: the wrap's used values </summary>
        internal AlphaWrapFeaturePass(DMesh3 mesh, DMesh3 input, double alpha, double offset)
        {
            Mesh = mesh; this.input = input; this.alpha = alpha; this.offset = offset;
        }

        /// <summary> (optional) a built tree of the input, read only, eg the one the simplification keeps clear of </summary>
        internal DMeshAABBTree3 InputTree { set { inputTree = value; } }

        // plane of a vertex: N . x = D (shifted outward by one offset), Kind 0 none, 1 face, 2 on a crease (also N2, D2)
        struct Label
        {
            public int Kind;
            public Vector3d N, N2;
            public double D, D2;
        }
        Label[] labels = new Label[0];
        bool[] labelled = new bool[0];

        /// <summary> runs the pass on Mesh; false if cancelled (Mesh is then partly changed, every change checked) </summary>
        internal bool Compute()
        {
            if (!Prepare())
                return true;
            return plane_pass() && crease_snap_pass() && split_pass() && (!EnableFlips || flip_pass());
        }

        /// <summary>
        /// resets the counters and builds the queries (Compute does it; for tests of TryMove); false if there is nothing to do
        /// (an empty mesh or input, or, with the dihedral source, no sharp edge in the input; the varifold source has no list
        /// of creases to look at first, so with it the pass always runs)
        /// </summary>
        internal bool Prepare()
        {
            PlaneMoves = CreaseSnaps = Splits = Flips = Rejected = CrossingEdges = InputCreases = VarifoldQueries = 0;
            VarifoldBuildTicks = VarifoldQueryTicks = VarifoldPlaneTicks = 0;
            VarifoldPlanesUsed = false;
            VarifoldMedianTilt = VarifoldMedianNoise = 0;
            VarifoldPlaneQueries = VarifoldPlanesFitted = 0;
            varifold = null;
            if (Mesh.TriangleCount == 0 || input.TriangleCount == 0 || !(alpha > 0) || !(offset > 0))
                return false;
            if (inputTree == null || inputTree.Mesh != input)
                inputTree = new DMeshAABBTree3(input, true);
            cosTurn = Math.Cos(MaxNormalTurnDegrees * MathUtil.Deg2Rad);
            cosCrease = Math.Cos(CreaseAngleDegrees * MathUtil.Deg2Rad);
            grid = build_grid(Mesh);
            labels = new Label[Mesh.MaxVertexID];
            labelled = new bool[Mesh.MaxVertexID];
            if (VarifoldCreases) {
                // (InputCreases then counts the crease points the test accepted)
                miters = null;
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                varifold = new VarifoldCreaseQuery(input, VarifoldRadius * alpha, CancelF);
                VarifoldBuildTicks = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                VarifoldMedianTilt = VarifoldMedianNoise = 0; VarifoldPlanesUsed = false;
                if (VarifoldPlaneRadius > 0) {
                    long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                    VarifoldMedianTilt = varifold.MedianTilt(input, VarifoldPlaneRadius * alpha);
                    VarifoldMedianNoise = varifold.MedianNoise(input, VarifoldPlaneRadius * alpha);
                    VarifoldPlanesUsed = VarifoldMedianNoise > VarifoldPlaneMinNoise;
                    if (VarifoldPlanesUsed && VarifoldPrefetch) prefetch_labels();
                    VarifoldPlaneTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t1;
                }
                return true;
            }
            varifold = null;
            build_miters();
            InputCreases = miters == null ? 0 : miters.Count;
            return miters != null;
        }

        /// <summary> (tests, after Prepare) moves vertex vid to x if the move passes every check </summary>
        internal bool TryMove(int vid, Vector3d x)
        {
            return try_move(vid, x);
        }

        // ---- the input's sharp edges and their miter lines ----

        void build_miters()
        {
            miters = null;
            double size = SafeDiag(input.CachedBounds), minHeight = 1e-5 * size;
            var segs = new List<Segment3d>();
            foreach (int eid in input.EdgeIndices()) {
                Index2i et = input.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID) continue;
                Vector3d n1 = tri_normal(input, et.a), n2 = tri_normal(input, et.b);
                if (n1.LengthSquared < 0.5 || n2.LengthSquared < 0.5) continue;
                Index2i ev = input.GetEdgeV(eid);
                // (both triangles in the orientation of the first: an edge traversed the same way by both is inconsistent)
                if (traverses(input.GetTriangle(et.a), ev.a, ev.b) == traverses(input.GetTriangle(et.b), ev.a, ev.b))
                    n2 = -n2;
                double c = n1.Dot(n2);
                if (c >= cosCrease) continue;
                if (height(input, et.a) < minHeight || height(input, et.b) < minHeight) continue;
                Vector3d a = input.GetVertex(ev.a), b = input.GetVertex(ev.b);
                // the miter shift delta (n1 + n2) / (1 + n1.n2), capped in dimensionless offset units before scaling by offset
                Vector3d dir = (n1 + n2) / Math.Max(1 + c, 1e-12);
                double dl = dir.Length;
                if (dl > MaxMiterShift) dir *= MaxMiterShift / dl;
                Vector3d s = dir * offset;
                segs.Add(SafeSegment(a + s, b + s));
                segs.Add(SafeSegment(a - s, b - s));
            }
            if (segs.Count > 0)
                miters = new SegmentGrid(segs, Math.Max(alpha, MinNormal), input.CachedBounds.Min);
        }

        static bool traverses(Index3i t, int a, int b)
        {
            return (t.a == a && t.b == b) || (t.b == a && t.c == b) || (t.c == a && t.a == b);
        }

        static double height(DMesh3 m, int tid)
        {
            Index3i t = m.GetTriangle(tid);
            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
            Vector3d u = b - a, v = c - a;
            double s = Math.Max(u.MaxAbs, v.MaxAbs);
            if (s >= FastScaleMin && s <= FastScaleMax) {
                double area2 = u.Cross(v).Length;
                double maxEdge = Math.Max(u.Length, Math.Max((c - b).Length, v.Length));
                return maxEdge > 0 ? area2 / maxEdge : 0;
            }
            if (!a.IsFinite || !b.IsFinite || !c.IsFinite) return 0;
            int pre = 0;
            if (!u.IsFinite || !v.IsFinite) { u = 0.5 * b - 0.5 * a; v = 0.5 * c - 0.5 * a; pre = 1; }
            s = Math.Max(u.MaxAbs, v.MaxAbs);
            if (!(s > 0)) return 0;
            int exp = Pow2Exp(s);
            Vector3d us = ScaleVec(u, -exp), vs = ScaleVec(v, -exp);
            double area2s = us.Cross(vs).Length;
            double maxEdgeS = Math.Max(us.Length, Math.Max((vs - us).Length, vs.Length));
            return maxEdgeS > 0 ? ExactPoint3.ScaleB(area2s / maxEdgeS, exp + pre) : 0;
        }

        bool near_miter(Vector3d p)
        {
            return miters.AnyWithin(p, MaxMiterDistance * offset);
        }

        // the crease point m of the vertex planes la, lb lies on an input crease: near the miter line of an input sharp edge, or
        // (VarifoldCreases) the input's two planes meet at a crease near m in the varifold sense
        bool input_crease(Label la, Label lb, Vector3d m)
        {
            if (varifold == null)
                return near_miter(m);
            // the unshifted planes (the labels are shifted outward by one offset) and their line's point nearest m
            var ia = new Label { Kind = 1, N = la.N, D = la.D - offset };
            var ib = new Label { Kind = 1, N = lb.N, D = lb.D - offset };
            if (!crease_point(ia, ib, m, out Vector3d x0)) return false;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            bool yes = varifold.IsCrease(x0, la.N, lb.N, CreaseAngleDegrees, VarifoldTolerance * offset, out double _);
            VarifoldQueryTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            VarifoldQueries++;
            if (yes) InputCreases++;
            return yes;
        }

        // ---- vertex planes ----

        Label label(int vid)
        {
            if (vid >= labels.Length) {
                Array.Resize(ref labels, Math.Max(vid + 1, 2 * labels.Length));
                Array.Resize(ref labelled, labels.Length);
            }
            if (!labelled[vid]) {
                labels[vid] = make_label(Mesh.GetVertex(vid), out int fit);
                labelled[vid] = true;
                if (fit > 0) { VarifoldPlaneQueries++; if (fit == 2) VarifoldPlanesFitted++; }
            }
            return labels[vid];
        }

        // the labels of all vertices at their current positions, in parallel (each vertex writes its own slot; no reduction across
        // threads). The passes ask a vertex's label before they move it, and nothing moves before them, so these are the labels
        // the lazy path would make: the same bits
        void prefetch_labels()
        {
            int nv = Mesh.MaxVertexID;
            var fits = new int[nv];
            // (cancellation: asked every 1024 vertices; the vertices skipped after it keep no label and get one lazily, the same)
            bool cancelled = false;
            gParallel.ForRange(nv, vid => {
                if ((vid & 1023) == 0 && CancelF()) cancelled = true;
                if (cancelled || !Mesh.IsVertex(vid)) return;
                labels[vid] = make_label(Mesh.GetVertex(vid), out fits[vid]);
                labelled[vid] = true;
            });
            for (int vid = 0; vid < nv; ++vid)
                if (fits[vid] > 0) { VarifoldPlaneQueries++; if (fits[vid] == 2) VarifoldPlanesFitted++; }
        }

        void set_label(int vid, Label l)
        {
            label(vid);
            labels[vid] = l;
        }

        // (fit: 0 no plane fit asked, 1 asked and refused, 2 the fitted plane taken; no shared state is written, so labels of
        // different vertices may be made in parallel)
        Label make_label(Vector3d v, out int fit)
        {
            fit = 0;
            double d2;
            int tid = inputTree.FindNearestTriangle(v, out d2, PlaneRadius * alpha);
            if (tid == DMesh3.InvalidID) return new Label();
            DistPoint3Triangle3 dist = MeshQueries.TriangleDistance(input, tid, v);
            Vector3d q = dist.TriangleClosest;
            Vector3d n = facing_normal(tid, q, v, dist.TriangleBaryCoords);
            if (n.LengthSquared < 0.5 || !(n.Dot(v - q) > 0)) return new Label();
            if (varifold != null && VarifoldPlanesUsed) {
                fit = 1;
                if (varifold.FitPlane(q, n, VarifoldPlaneRadius * alpha, out Vector3d nf, out Vector3d cf, out double rf) && nf.Dot(v - cf) > 0) {
                    fit = 2;
                    return new Label { Kind = 1, N = nf, D = nf.Dot(cf) + VarifoldPlaneLift * rf + offset };
                }
            }
            return new Label { Kind = 1, N = n, D = n.Dot(q) + offset };
        }

        // the unit normal of an input triangle at q, turned towards v; where q lies on an edge or a vertex of the triangle (a
        // crease), the triangle around q whose normal points most towards v (so both faces of a convex crease give planes, by
        // the side the vertex is on). Zero if none
        Vector3d facing_normal(int tid, Vector3d q, Vector3d v, Vector3d bc)
        {
            Vector3d dir = v - q;
            if (!dir.IsFinite) dir = 0.5 * v - 0.5 * q;
            double len = SafeLength(dir);
            if (!(len > 0) || double.IsInfinity(len)) return Vector3d.Zero;
            dir /= len;
            Vector3d bestN = Vector3d.Zero;
            double best = double.NegativeInfinity;
            Action<int> consider = (t) => {
                Vector3d n = tri_normal(input, t);
                if (n.LengthSquared < 0.5) return;
                double s = n.Dot(dir);
                if (s < 0) { s = -s; n = -n; }
                if (s > best) { best = s; bestN = n; }
            };
            consider(tid);
            const double eps = 1e-9;
            if (!(bc.x < eps || bc.y < eps || bc.z < eps))
                return bestN;
            Index3i tv = input.GetTriangle(tid);
            int atVertex = bc.x > 1 - eps ? tv.a : bc.y > 1 - eps ? tv.b : bc.z > 1 - eps ? tv.c : -1;
            if (atVertex >= 0) {
                foreach (int t2 in input.VtxTrianglesItr(atVertex)) consider(t2);
                return bestN;
            }
            for (int k = 0; k < 3; ++k) {
                if (!(bc[k] < eps)) continue;
                int eid = input.FindEdge(tv[(k + 1) % 3], tv[(k + 2) % 3]);
                if (eid == DMesh3.InvalidID) continue;
                Index2i et = input.GetEdgeT(eid);
                int other = et.a == tid ? et.b : et.a;
                if (other != DMesh3.InvalidID) consider(other);
            }
            return bestN;
        }

        // the point of the intersection line of the two planes nearest p
        static bool crease_point(Label a, Label b, Vector3d p, out Vector3d x)
        {
            double c = a.N.Dot(b.N), det = 1 - c * c;
            x = p;
            if (!(det > 1e-6)) return false;
            double r1 = a.D - a.N.Dot(p), r2 = b.D - b.N.Dot(p);
            double c1 = (r1 - c * r2) / det, c2 = (r2 - c * r1) / det;
            x = p + c1 * a.N + c2 * b.N;
            return x.IsFinite;
        }

        bool crossing(Label a, Label b)
        {
            return a.Kind == 1 && b.Kind == 1 && a.N.Dot(b.N) < cosCrease;
        }

        // ---- passes ----

        bool plane_pass()
        {
            int maxV = Mesh.MaxVertexID;
            for (int vid = 0; vid < maxV; ++vid) {
                if ((vid & 1023) == 0 && CancelF()) return false;
                if (!Mesh.IsVertex(vid)) continue;
                Label l = label(vid);
                if (l.Kind != 1) continue;
                Vector3d v = Mesh.GetVertex(vid);
                double off = l.D - l.N.Dot(v);
                if (Math.Abs(off) < 0.02 * offset) continue;
                if (try_move(vid, v + off * l.N)) PlaneMoves++; else Rejected++;
            }
            return true;
        }

        bool crease_snap_pass()
        {
            int maxV = Mesh.MaxVertexID;
            var nbrs = new List<int>();
            for (int vid = 0; vid < maxV; ++vid) {
                if ((vid & 1023) == 0 && CancelF()) return false;
                if (!Mesh.IsVertex(vid)) continue;
                Label la = label(vid);
                if (la.Kind != 1) continue;
                Vector3d a = Mesh.GetVertex(vid);
                double bestD = double.MaxValue;
                Vector3d bestX = a;
                Label bestOther = default(Label);
                nbrs.Clear();
                foreach (int nb in Mesh.VtxVerticesItr(vid)) nbrs.Add(nb);
                nbrs.Sort();        // (a fixed order: ties between equally near lines go to the lower vertex id)
                foreach (int nb in nbrs) {
                    Label lb = label(nb);
                    if (!crossing(la, lb)) continue;
                    Vector3d b = Mesh.GetVertex(nb), m, x;
                    if (!crease_point(la, lb, 0.5 * (a + b), out m)) continue;
                    double t = (m - a).Dot(b - a) / (b - a).LengthSquared;
                    if (t > SnapFraction || !input_crease(la, lb, m)) continue;
                    if (!crease_point(la, lb, a, out x)) continue;
                    double d = x.Distance(a);
                    if (d < bestD) { bestD = d; bestX = x; bestOther = lb; }
                }
                if (bestD == double.MaxValue) continue;
                if (try_move(vid, bestX)) {
                    CreaseSnaps++;
                    set_label(vid, new Label { Kind = 2, N = la.N, D = la.D, N2 = bestOther.N, D2 = bestOther.D });
                } else Rejected++;
            }
            return true;
        }

        bool split_pass()
        {
            var cross = new List<int>();
            foreach (int eid in Mesh.EdgeIndices()) {
                Index2i ev = Mesh.GetEdgeV(eid);
                if (crossing(label(ev.a), label(ev.b))) cross.Add(eid);
            }
            CrossingEdges = cross.Count;
            var ids = new List<Index3i>(4);
            var tris = new List<Triangle3d>(4);
            var removed = new HashSet<int>();
            int k = 0;
            foreach (int eid in cross) {
                if ((++k & 1023) == 0 && CancelF()) return false;
                if (!Mesh.IsEdge(eid) || Mesh.IsBoundaryEdge(eid)) continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                Label la = label(ev.a), lb = label(ev.b);
                if (!crossing(la, lb)) continue;
                Vector3d a = Mesh.GetVertex(ev.a), b = Mesh.GetVertex(ev.b), m;
                if (!crease_point(la, lb, 0.5 * (a + b), out m) || !input_crease(la, lb, m)) continue;
                // (a, b) in the order of triangle et.a = (a, b, c); et.b = (b, a, d)
                Index2i et = Mesh.GetEdgeT(eid);
                int va = ev.a, vb = ev.b;
                if (!traverses(Mesh.GetTriangle(et.a), va, vb)) { int s = va; va = vb; vb = s; Vector3d sp = a; a = b; b = sp; }
                int vc = opposite(et.a, va, vb), vd = opposite(et.b, va, vb);
                Vector3d c = Mesh.GetVertex(vc), d = Mesh.GetVertex(vd);
                const int NewId = int.MaxValue;
                ids.Clear(); tris.Clear(); removed.Clear();
                ids.Add(new Index3i(va, NewId, vc)); tris.Add(new Triangle3d(a, m, c));
                ids.Add(new Index3i(NewId, vb, vc)); tris.Add(new Triangle3d(m, b, c));
                ids.Add(new Index3i(vb, NewId, vd)); tris.Add(new Triangle3d(b, m, d));
                ids.Add(new Index3i(NewId, va, vd)); tris.Add(new Triangle3d(m, a, d));
                removed.Add(et.a); removed.Add(et.b);
                Triangle3d o0 = new Triangle3d(a, b, c), o1 = new Triangle3d(b, a, d);
                var olds = new[] { o0, o0, o1, o1 };
                if (!shapes_ok(tris, olds, o0, o1) || !far_enough(m)
                    || (CheckSweep && sweeps_input(new[] { new Tet(a, b, c, m), new Tet(b, a, d, m) }))
                    || !clear(ids, tris, removed)) {
                    Rejected++;
                    continue;
                }
                DMesh3.EdgeSplitInfo info;
                if (Mesh.SplitEdge(eid, out info) != MeshResult.Ok) { Rejected++; continue; }
                Mesh.SetVertex(info.vNew, m);
                set_label(info.vNew, new Label { Kind = 2, N = la.N, D = la.D, N2 = lb.N, D2 = lb.D });
                var changed = new List<int>();
                foreach (int t in Mesh.VtxTrianglesItr(info.vNew)) changed.Add(t);
                regrid(removed, changed);
                Splits++;
            }
            return true;
        }

        // an edge between two non-crease vertices whose opposite vertices are both crease vertices crosses the crease (the
        // vertices snapped in crease_snap_pass are not joined by the splits): flipped, so the edge runs along it
        bool flip_pass()
        {
            var edges = new List<int>(Mesh.EdgeIndices());
            var ids = new List<Index3i>(2);
            var tris = new List<Triangle3d>(2);
            var removed = new HashSet<int>();
            int k = 0;
            foreach (int eid in edges) {
                if ((++k & 1023) == 0 && CancelF()) return false;
                if (!Mesh.IsEdge(eid) || Mesh.IsBoundaryEdge(eid)) continue;
                Index2i ev = Mesh.GetEdgeV(eid);
                Index2i et = Mesh.GetEdgeT(eid);
                int va = ev.a, vb = ev.b;
                if (!traverses(Mesh.GetTriangle(et.a), va, vb)) { int s = va; va = vb; vb = s; }
                int vc = opposite(et.a, va, vb), vd = opposite(et.b, va, vb);
                Label la = label(va), lb = label(vb), lc = label(vc), ld = label(vd);
                if (lc.Kind < 2 || ld.Kind < 2) continue;
                if (la.Kind >= 2 && lb.Kind >= 2) continue;
                if (la.Kind == 1 && lb.Kind == 1 && la.N.Dot(lb.N) >= cosCrease) continue;
                if (Mesh.FindEdge(vc, vd) != DMesh3.InvalidID) continue;
                Vector3d a = Mesh.GetVertex(va), b = Mesh.GetVertex(vb), c = Mesh.GetVertex(vc), d = Mesh.GetVertex(vd);
                // (a, b, c), (b, a, d) -> (c, a, d), (d, b, c), the same orientation
                ids.Clear(); tris.Clear(); removed.Clear();
                ids.Add(new Index3i(vc, va, vd)); tris.Add(new Triangle3d(c, a, d));
                ids.Add(new Index3i(vd, vb, vc)); tris.Add(new Triangle3d(d, b, c));
                removed.Add(et.a); removed.Add(et.b);
                Triangle3d o0 = new Triangle3d(a, b, c), o1 = new Triangle3d(b, a, d);
                if (!shapes_ok(tris, null, o0, o1)
                    || (CheckSweep && sweeps_input(new[] { new Tet(a, b, c, d) }))
                    || !clear(ids, tris, removed)) {
                    Rejected++;
                    continue;
                }
                DMesh3.EdgeFlipInfo info;
                if (Mesh.FlipEdge(eid, out info) != MeshResult.Ok) { Rejected++; continue; }
                regrid(removed, new[] { info.t0, info.t1 });
                Flips++;
            }
            return true;
        }

        // ---- checks ----

        int opposite(int tid, int a, int b)
        {
            Index3i t = Mesh.GetTriangle(tid);
            return (t.a != a && t.a != b) ? t.a : (t.b != a && t.b != b) ? t.b : t.c;
        }

        bool far_enough(Vector3d x)
        {
            double d2;
            inputTree.FindNearestTriangle(x, out d2);
            return Math.Sqrt(d2) >= MinDistance * offset;
        }

        // the new triangles: not degenerate, turned by at most MaxNormalTurnDegrees against their old triangle (olds[i], or
        // both old triangles if olds is null), and within the angle floors of the old triangles o0, o1 (or o0 alone)
        bool shapes_ok(List<Triangle3d> tris, Triangle3d[] olds, Triangle3d o0, Triangle3d? o1)
        {
            double minOld = min_angle(o0), maxOld = max_angle(o0);
            if (o1.HasValue) { minOld = Math.Min(minOld, min_angle(o1.Value)); maxOld = Math.Max(maxOld, max_angle(o1.Value)); }
            for (int i = 0; i < tris.Count; ++i) {
                Triangle3d t = tris[i];
                Vector3d n;
                if (!NotDegenerate(t, out n)) return false;
                if (olds != null) {
                    Vector3d no;
                    if (!unit_normal(olds[i], out no) || n.Dot(no) < cosTurn) return false;
                } else {
                    Vector3d n0, n1;
                    if (!unit_normal(o0, out n0) || n.Dot(n0) < cosTurn) return false;
                    if (o1.HasValue && (!unit_normal(o1.Value, out n1) || n.Dot(n1) < cosTurn)) return false;
                }
                if (min_angle(t) < Math.Min(minOld, MinAngleFloorDegrees) - 1e-9) return false;
                if (max_angle(t) > Math.Max(maxOld, MaxAngleCeilingDegrees) + 1e-9) return false;
            }
            return true;
        }

        readonly List<int> ring = new List<int>();
        readonly List<Index3i> mIds = new List<Index3i>();
        readonly List<Triangle3d> mTris = new List<Triangle3d>();
        readonly HashSet<int> mRemoved = new HashSet<int>();

        // moves vid to x if the moved one ring passes the checks (each ring triangle against its own old shape)
        bool try_move(int vid, Vector3d x)
        {
            if (!x.IsFinite || !far_enough(x)) return false;
            ring.Clear(); mIds.Clear(); mTris.Clear(); mRemoved.Clear();
            foreach (int t in Mesh.VtxTrianglesItr(vid)) ring.Add(t);
            Vector3d p = Mesh.GetVertex(vid);
            var tets = new List<Tet>(ring.Count);
            foreach (int t in ring) {
                Index3i tv = Mesh.GetTriangle(t);
                Triangle3d o = new Triangle3d(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c));
                Triangle3d nt = new Triangle3d(tv.a == vid ? x : o.V0, tv.b == vid ? x : o.V1, tv.c == vid ? x : o.V2);
                Vector3d no, nn;
                if (!unit_normal(o, out no) || !NotDegenerate(nt, out nn) || nn.Dot(no) < cosTurn) return false;
                if (min_angle(nt) < Math.Min(min_angle(o), MinAngleFloorDegrees) - 1e-9) return false;
                if (max_angle(nt) > Math.Max(max_angle(o), MaxAngleCeilingDegrees) + 1e-9) return false;
                mIds.Add(tv); mTris.Add(nt); mRemoved.Add(t);
                tets.Add(new Tet(o.V0, o.V1, o.V2, x));
            }
            if ((CheckSweep && sweeps_input(tets)) || !clear(mIds, mTris, mRemoved))
                return false;
            Mesh.SetVertex(vid, x);
            regrid(ring, ring);
            return true;
        }

        // the new triangles touch neither the input nor each other nor the rest of the mesh (exact)
        bool clear(List<Index3i> ids, List<Triangle3d> tris, HashSet<int> removed)
        {
            if (Reducer.TrianglesTouch(inputTree, tris)) return false;
            for (int i = 0; i < tris.Count; ++i) {
                Triangle3d A = tris[i];
                Index3i ia = ids[i];
                // (for a moved fan of a closed mesh, where every link edge has a rest triangle outside the ring, and with non-degenerate
                // rest triangles, this is implied by the test against the rest: if fan triangles (x, e1) and (x, e2) cross, a
                // ray from x meets both link edges, so the farther triangle contains a point of the nearer edge, beyond the shared
                // vertices, and touches the rest triangle on that edge; it stays for splits and flips, and for a degenerate rest
                // triangle, next to which the grid test refuses the operation)
                for (int j = i + 1; j < tris.Count; ++j) {
                    Triangle3d B = tris[j];
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ids[j], ref B)) return false;
                }
                bool hit = false;
                grid.Query(box(A.V0, A.V1, A.V2), (tc) => {
                    if (hit || removed.Contains(tc) || Mesh.IsTriangle(tc) == false) return;
                    Index3i ic = Mesh.GetTriangle(tc);
                    Triangle3d C = new Triangle3d(Mesh.GetVertex(ic.a), Mesh.GetVertex(ic.b), Mesh.GetVertex(ic.c));
                    // a degenerate rest triangle (none in a raw wrap, whose triangles are Delaunay facets, and none made by
                    // this pass): the test beyond shared vertices is not defined for it, so an operation next to one is
                    // refused instead of leaving the pair untested
                    if (ExactTriangleIntersection.IsDegenerate(ref C)) { hit = true; return; }
                    if (TriangleTriangleFilter.IntersectsBeyondShared(ia, ref A, ic, ref C)) hit = true;
                });
                if (hit) return false;
            }
            return true;
        }

        struct Tet
        {
            public Vector3d A, B, C, D;
            public Tet(Vector3d a, Vector3d b, Vector3d c, Vector3d d) { A = a; B = b; C = c; D = d; }
        }

        /// <summary>
        /// does an input triangle meet the region between the old and the new surface? The region is the union of the
        /// tetrahedra (old triangle, new vertex) of the operation; an input triangle meets a tetrahedron if it intersects one of
        /// its faces or lies inside it (one vertex strictly inside by exact orientations). (The end state alone could jump over a
        /// thin input part smaller than the move, leaving it outside the wrap.)
        /// </summary>
        bool sweeps_input(IList<Tet> tets)
        {
            // (Reducer.TetrahedraMeet: the faces exactly, corners and centroids strictly inside; shared with the Reducer's and the
            // quality pass's KeepClearOf, issue #743)
            var corners = new List<Vector3d>(4 * tets.Count);
            foreach (Tet t in tets) { corners.Add(t.A); corners.Add(t.B); corners.Add(t.C); corners.Add(t.D); }
            return Reducer.TetrahedraMeet(inputTree, corners);
        }

        // ---- geometry helpers ----

        /// <summary>
        /// a new triangle of an operation: its unit normal, false if it is too thin for one in floating point or degenerate
        /// by the library's exact test (ExactTriangleIntersection.IsDegenerate: collinear corners). So "no operation creates
        /// a degenerate triangle" is decided by the same test that clear() applies to the rest triangles, and rests on that
        /// test's prefilter (pinned by IsDegenerate_PrefilterThreshold), not on a second threshold of its own. On every
        /// input found the floating-point normal alone refuses collinear corners too (the cross product of exactly parallel
        /// edge vectors carries only the rounding of the subtractions, about 1e-16 of the scale)
        /// </summary>
        internal static bool NotDegenerate(Triangle3d t, out Vector3d n)
        {
            return unit_normal(t, out n) && !ExactTriangleIntersection.IsDegenerate(ref t);
        }

        const double MinNormal = 2.2250738585072014e-308; // 2^-1022
        const double FastScaleMin = 7.888609052210118e-31; // 2^-100
        const double FastScaleMax = 1.2676506002282294e30; // 2^100

        static int Pow2Exp(double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int biased = (int)((bits >> 52) & 0x7FFL);
            if (biased != 0)
                return biased - 1023;
            long sbits = BitConverter.DoubleToInt64Bits(v * 18014398509481984.0); // 2^54
            return (int)((sbits >> 52) & 0x7FFL) - 1023 - 54;
        }

        static Vector3d ScaleVec(Vector3d v, int n)
        {
            return new Vector3d(ExactPoint3.ScaleB(v.x, n), ExactPoint3.ScaleB(v.y, n), ExactPoint3.ScaleB(v.z, n));
        }

        static double SafeLength(Vector3d d)
        {
            double d2 = d.LengthSquared;
            if (d2 >= 4 * MinNormal && !double.IsInfinity(d2))
                return Math.Sqrt(d2);
            double m = d.MaxAbs;
            if (double.IsNaN(m)) return double.NaN;
            if (m == 0) return 0;
            if (double.IsInfinity(m)) return double.PositiveInfinity;
            int e = Pow2Exp(m);
            Vector3d ds = ScaleVec(d, -e);
            return ExactPoint3.ScaleB(Math.Sqrt(ds.LengthSquared), e);
        }

        static double SafeDiag(AxisAlignedBox3d b)
        {
            Vector3d d = b.Max - b.Min;
            if (!d.IsFinite)
                return 2.0 * SafeLength(0.5 * b.Max - 0.5 * b.Min);
            return SafeLength(d);
        }

        static Vector3d tri_normal(DMesh3 m, int tid)
        {
            Vector3d n = m.GetTriNormal(tid);
            if (n.LengthSquared >= 0.5) return n;
            Index3i tv = m.GetTriangle(tid);
            return unit_normal(new Triangle3d(m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c)), out n) ? n : Vector3d.Zero;
        }

        static Segment3d SafeSegment(Vector3d p0, Vector3d p1)
        {
            Vector3d d = p1 - p0;
            double s = d.MaxAbs;
            if (s >= FastScaleMin && s <= FastScaleMax && p0.MaxAbs <= FastScaleMax && p1.MaxAbs <= FastScaleMax)
                return new Segment3d(p0, p1);
            Vector3d center = 0.5 * p0 + 0.5 * p1;
            if (!d.IsFinite) {
                d = 0.5 * p1 - 0.5 * p0;
                double halfLen = SafeLength(d);
                if (!(halfLen > 0) || double.IsInfinity(halfLen))
                    return new Segment3d(center, Vector3d.AxisX, 0);
                int hexp = Pow2Exp(d.MaxAbs);
                Vector3d hds = ScaleVec(d, -hexp);
                return new Segment3d(center, hds / Math.Sqrt(hds.LengthSquared), halfLen);
            }
            double len = SafeLength(d);
            if (!(len > 0) || double.IsInfinity(len))
                return new Segment3d(center, Vector3d.AxisX, 0);
            int exp = Pow2Exp(s);
            Vector3d ds = ScaleVec(d, -exp);
            return new Segment3d(center, ds / Math.Sqrt(ds.LengthSquared), 0.5 * len);
        }

        static bool unit_normal(Triangle3d t, out Vector3d n)
        {
            Vector3d u = t.V1 - t.V0, v = t.V2 - t.V0, w = t.V2 - t.V1;
            double s = Math.Max(u.MaxAbs, Math.Max(v.MaxAbs, w.MaxAbs));
            if (s >= FastScaleMin && s <= FastScaleMax) {
                n = u.Cross(v);
                double l = Math.Max(u.LengthSquared, Math.Max(w.LengthSquared, v.LengthSquared));
                double len = n.Length;
                if (!(len > 1e-12 * l)) return false;
                n /= len;
                return true;
            }
            n = Vector3d.Zero;
            if (!t.V0.IsFinite || !t.V1.IsFinite || !t.V2.IsFinite) return false;
            if (!u.IsFinite || !v.IsFinite || !w.IsFinite) {
                u = 0.5 * t.V1 - 0.5 * t.V0;
                v = 0.5 * t.V2 - 0.5 * t.V0;
                w = 0.5 * t.V2 - 0.5 * t.V1;
            }
            s = Math.Max(u.MaxAbs, Math.Max(v.MaxAbs, w.MaxAbs));
            if (!(s > 0)) return false;
            int exp = Pow2Exp(s);
            Vector3d us = ScaleVec(u, -exp), vs = ScaleVec(v, -exp), ws = ScaleVec(w, -exp);
            n = us.Cross(vs);
            double ls = Math.Max(us.LengthSquared, Math.Max(ws.LengthSquared, vs.LengthSquared));
            double lens = SafeLength(n);
            if (!(lens > 1e-12 * ls)) return false;
            n /= lens;
            return true;
        }

        static double angle(Vector3d u, Vector3d v)
        {
            double su = u.MaxAbs, sv = v.MaxAbs;
            if (su >= FastScaleMin && su <= FastScaleMax && sv >= FastScaleMin && sv <= FastScaleMax) {
                double lu = u.Length, lv = v.Length;
                if (!(lu > 0 && lv > 0)) return 0;
                return Math.Acos(MathUtil.Clamp(u.Dot(v) / (lu * lv), -1, 1)) * MathUtil.Rad2Deg;
            }
            if (!(su > 0 && sv > 0) || !u.IsFinite || !v.IsFinite) return 0;
            Vector3d us = ScaleVec(u, -Pow2Exp(su));
            Vector3d vs = ScaleVec(v, -Pow2Exp(sv));
            double lus = us.Length, lvs = vs.Length;
            if (!(lus > 0 && lvs > 0)) return 0;
            return Math.Acos(MathUtil.Clamp(us.Dot(vs) / (lus * lvs), -1, 1)) * MathUtil.Rad2Deg;
        }

        static double min_angle(Triangle3d t)
        {
            return Math.Min(angle(t.V1 - t.V0, t.V2 - t.V0), Math.Min(angle(t.V0 - t.V1, t.V2 - t.V1), angle(t.V0 - t.V2, t.V1 - t.V2)));
        }

        static double max_angle(Triangle3d t)
        {
            return Math.Max(angle(t.V1 - t.V0, t.V2 - t.V0), Math.Max(angle(t.V0 - t.V1, t.V2 - t.V1), angle(t.V0 - t.V2, t.V1 - t.V2)));
        }

        static AxisAlignedBox3d box(Vector3d a, Vector3d b, Vector3d c)
        {
            var bx = new AxisAlignedBox3d(a); bx.Contain(b); bx.Contain(c);
            return bx;
        }

        static TriangleHashGrid build_grid(DMesh3 mesh)
        {
            double sum = 0; int n = 0;
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                Vector3d va = mesh.GetVertex(ev.a), vb = mesh.GetVertex(ev.b);
                Vector3d d = va - vb;
                sum += d.IsFinite ? SafeLength(d) : 2.0 * SafeLength(0.5 * va - 0.5 * vb);
                n++;
            }
            var g = new TriangleHashGrid(mesh.CachedBounds.Min, Math.Max(1.5 * sum / Math.Max(1, n), MinNormal));
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                g.Insert(tid, box(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c)));
            }
            return g;
        }

        void regrid(IEnumerable<int> removed, IEnumerable<int> added)
        {
            foreach (int t in removed) grid.Remove(t);
            foreach (int t in added) {
                Index3i tv = Mesh.GetTriangle(t);
                grid.Insert(t, box(Mesh.GetVertex(tv.a), Mesh.GetVertex(tv.b), Mesh.GetVertex(tv.c)));
            }
        }

        /// <summary> segments by grid cells: is a segment within r of a point? </summary>
        sealed class SegmentGrid
        {
            const int CellLimit = 1 << 28; // safely inside int: ++x never wraps int.MaxValue
            const int MaxStepsPerSegment = 4096;
            const long MaxQueryCells = 4096;

            readonly Dictionary<Vector3i, List<int>> cells = new Dictionary<Vector3i, List<int>>();
            readonly List<int> wideSegs = new List<int>();
            readonly List<Segment3d> segs;
            readonly double h;
            readonly Vector3d origin;
            internal int Count { get { return segs.Count; } }

            // each segment is entered in the cells of its points at steps of half a cell (a segment point is then within a
            // quarter cell of a sampled one); segments exceeding MaxStepsPerSegment or CellLimit go to wideSegs
            internal SegmentGrid(List<Segment3d> s, double cell, Vector3d o)
            {
                segs = s; h = cell; origin = o;
                bool validH = h > 0 && !double.IsInfinity(h);
                for (int i = 0; i < s.Count; ++i) {
                    Vector3d p0 = s[i].P0, p1 = s[i].P1;
                    if (!validH || !FitsCell(p0) || !FitsCell(p1)) {
                        wideSegs.Add(i);
                        continue;
                    }
                    Vector3d diff = p1 - p0;
                    double len = diff.IsFinite ? SafeLength(diff) : 2.0 * SafeLength(0.5 * p1 - 0.5 * p0);
                    double stepsD = Math.Ceiling(len / (0.5 * h));
                    if (!(stepsD <= MaxStepsPerSegment)) {
                        wideSegs.Add(i);
                        continue;
                    }
                    int n = Math.Max(1, (int)stepsD);
                    for (int k = 0; k <= n; ++k) {
                        double t = (double)k / n;
                        Vector3d pt = (1.0 - t) * p0 + t * p1;
                        if (!TryKey(pt, out Vector3i c)) {
                            wideSegs.Add(i);
                            break;
                        }
                        List<int> l;
                        if (!cells.TryGetValue(c, out l)) cells[c] = l = new List<int>();
                        if (l.Count == 0 || l[l.Count - 1] != i) l.Add(i);
                    }
                }
            }

            bool FitsCell(Vector3d p)
            {
                return TryKey(p, out _);
            }

            bool TryKey(Vector3d p, out Vector3i k)
            {
                double fx = Math.Floor((p.x - origin.x) / h);
                double fy = Math.Floor((p.y - origin.y) / h);
                double fz = Math.Floor((p.z - origin.z) / h);
                if (Math.Abs(fx) <= CellLimit && Math.Abs(fy) <= CellLimit && Math.Abs(fz) <= CellLimit) {
                    k = new Vector3i((int)fx, (int)fy, (int)fz);
                    return true;
                }
                k = default(Vector3i);
                return false;
            }

            static int ClampCell(double f)
            {
                if (f <= -CellLimit) return -CellLimit;
                if (f >= CellLimit) return CellLimit;
                return (int)f;
            }

            static bool SegmentWithin(Segment3d seg, Vector3d p, double r)
            {
                if (!(r >= 0) || !p.IsFinite) return false;
                double r2 = r * r;
                if (r2 >= 4 * MinNormal && !double.IsInfinity(r2)) {
                    Vector3d a = seg.P0, b = seg.P1;
                    if (a.IsFinite && b.IsFinite
                        && (b - a).MaxAbs <= FastScaleMax
                        && (p - a).MaxAbs <= FastScaleMax) {
                        return seg.DistanceSquared(p) <= r2;
                    }
                }
                if (r == 0) {
                    return SafePointSegmentDistance(seg.P0, seg.P1, p) == 0;
                }
                return SafePointSegmentDistance(seg.P0, seg.P1, p) <= r;
            }

            static double SafePointSegmentDistance(Vector3d a, Vector3d b, Vector3d q)
            {
                if (!a.IsFinite || !b.IsFinite || !q.IsFinite) return double.NaN;
                Vector3d ab = b - a, qa = q - a, qb = q - b;
                if (!ab.IsFinite || !qa.IsFinite || !qb.IsFinite)
                    return 2.0 * SafePointSegmentDistance(0.5 * a, 0.5 * b, 0.5 * q);
                double m = ab.MaxAbs, mq = qa.MaxAbs;
                if (!(m > 0)) return SafeLength(qa);
                if (!(mq > 0)) return 0;
                int ea = Pow2Exp(m), eq = Pow2Exp(mq);
                Vector3d sab = ScaleVec(ab, -ea), sqa = ScaleVec(qa, -eq);
                double proj = sqa.Dot(sab) / sab.LengthSquared;
                if (!(proj > 0)) return SafeLength(qa);
                if (eq >= ea ? proj >= ExactPoint3.ScaleB(1.0, ea - eq) : ExactPoint3.ScaleB(proj, eq - ea) >= 1.0)
                    return SafeLength(qb);
                return ExactPoint3.ScaleB(SafeLength(sqa - proj * sab), eq);
            }

            internal bool AnyWithin(Vector3d p, double r)
            {
                if (!(r >= 0) || !p.IsFinite) return false;
                for (int w = 0; w < wideSegs.Count; ++w)
                    if (SegmentWithin(segs[wideSegs[w]], p, r)) return true;
                if (cells.Count == 0 || !(h > 0) || double.IsInfinity(h))
                    return false;
                double pad = ExactInterval.Up(r + ExactInterval.Up(0.25 * h));
                double fx0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(p.x - pad) - origin.x) / h));
                double fx1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(p.x + pad) - origin.x) / h));
                double fy0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(p.y - pad) - origin.y) / h));
                double fy1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(p.y + pad) - origin.y) / h));
                double fz0 = Math.Floor(ExactInterval.Down(ExactInterval.Down(ExactInterval.Down(p.z - pad) - origin.z) / h));
                double fz1 = Math.Floor(ExactInterval.Up(ExactInterval.Up(ExactInterval.Up(p.z + pad) - origin.z) / h));
                if (fx1 < -CellLimit || fx0 > CellLimit || fy1 < -CellLimit || fy0 > CellLimit || fz1 < -CellLimit || fz0 > CellLimit
                    || double.IsNaN(fx0) || double.IsNaN(fx1) || double.IsNaN(fy0) || double.IsNaN(fy1) || double.IsNaN(fz0) || double.IsNaN(fz1))
                    return false;
                int x0 = ClampCell(fx0), x1 = ClampCell(fx1);
                int y0 = ClampCell(fy0), y1 = ClampCell(fy1);
                int z0 = ClampCell(fz0), z1 = ClampCell(fz1);
                long nx = (long)x1 - x0 + 1, ny = (long)y1 - y0 + 1, nz = (long)z1 - z0 + 1;
                if (nx > MaxQueryCells || ny > MaxQueryCells || nz > MaxQueryCells || nx * ny > MaxQueryCells || nx * ny * nz > MaxQueryCells) {
                    for (int i = 0; i < segs.Count; ++i)
                        if (SegmentWithin(segs[i], p, r)) return true;
                    return false;
                }
                for (int z = z0; z <= z1; ++z)
                    for (int y = y0; y <= y1; ++y)
                        for (int x = x0; x <= x1; ++x) {
                            List<int> l;
                            if (!cells.TryGetValue(new Vector3i(x, y, z), out l)) continue;
                            foreach (int i in l)
                                if (SegmentWithin(segs[i], p, r)) return true;
                        }
                return false;
            }
        }
    }
}
