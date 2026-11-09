// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// A catch line: where the slope surface meets the terrain (TERRAIN.md 5.5). Closed, or open with the reason at
    /// each end. The points lie on the slope surface.
    /// </summary>
    public class CatchLine
    {
        public enum EndReasons
        {
            None,
            /// <summary> the line ends at the toe: the terrain meets the toe there </summary>
            Toe,
            /// <summary> the line ends at a lateral limit of an open toe </summary>
            LateralLimit,
            /// <summary> the slope does not daylight: the region reaches the boundary of the terrain </summary>
            FootprintBoundary,
            /// <summary> the slope does not daylight below the given HeightLimit </summary>
            HeightLimit
        }

        public List<Vector3d> Points = new List<Vector3d>();
        public bool Closed;
        public EndReasons StartReason, EndReason;
        /// <summary>
        /// per segment (from Points[i] to the next, the last one back to the start if Closed; TERRAIN.md 5.5): the
        /// horizontal gradient |∇(S - T)| of the faces it crosses, the horizontal uncertainty of its position
        /// (TerrainSlopeDesign: (Tolerance + TerrainVerticalUncertainty) / gradient + MinFeatureSize, a local linearized
        /// estimate, not a proven bound), and whether the slope and the terrain coincide there (a tangential or planar
        /// contact: the line is a convention of the exact rule, not a measurement)
        /// </summary>
        public List<double> Gradient = new List<double>(), Uncertainty = new List<double>();
        public List<bool> Tangential = new List<bool>();
    }

    /// <summary>
    /// The topological overlay of the slope surface A (S̃) with the terrain B (TERRAIN.md 5.5, TERRAIN_1B_PLAN.md):
    /// overlay vertices by identity (a vertex of A in a triangle of B, a vertex of B in a triangle of A, the crossing of
    /// an edge of A with an edge of B), all decisions by exact Orient2d with Simulation of Simplicity (B shifted by
    /// (ε, ε²)), the sign of D = S̃ − T (cut; T − S̃ for fill) exact at each overlay vertex (0 counts as no work), the
    /// region of faces with D &lt; 0 connected to the toe, and the catch lines as chains of catch points.
    /// </summary>
    internal sealed class SlopeTerrainOverlay
    {
        // ---------------------------------------------------------------- inputs
        readonly DMesh3 A, B;
        readonly int dir;
        // the kind of each boundary edge of A: 1 toe, 2 lateral limit, 3 other (the height limit)
        readonly Dictionary<int, int> aBoundaryKind;
        readonly Func<bool> cancel;

        public const int KindToe = 1, KindLateral = 2, KindOther = 3;

        // B triangles counter-clockwise: vertices and edges (edge j between vertex j and j + 1)
        public readonly Dictionary<int, int[]> bTriV = new Dictionary<int, int[]>(), bTriE = new Dictionary<int, int[]>();
        /// <summary> the terrain triangles of bTriV in a quadtree (item i is TerrainTreeTris[i]), for point queries </summary>
        internal LooseQuadtree2d TerrainTree;
        internal List<int> TerrainTreeTris;
        public DMesh3 Slope { get { return A; } }
        /// <summary> +1 cut, -1 fill </summary>
        public int Dir { get { return dir; } }
        public DMesh3 Terrain { get { return B; } }
        /// <summary> the kind of a boundary edge of A (KindToe, KindLateral, KindOther), 0 for an interior edge </summary>
        public int BoundaryKind(int ea) { int k; return aBoundaryKind.TryGetValue(ea, out k) ? k : 0; }

        public SlopeTerrainOverlay(DMesh3 slope, DMesh3 terrain, bool fill, Dictionary<int, int> slopeBoundaryKind, Func<bool> cancel)
        {
            A = slope; B = terrain; dir = fill ? -1 : 1;
            aBoundaryKind = slopeBoundaryKind;
            this.cancel = cancel;
        }

        // ---------------------------------------------------------------- outputs

        public sealed class Face
        {
            public int TA, TB;
            public long C0 = -1, C1 = -1;        // its catch points (overlay edge keys), or -1
            public bool Negative;                // a vertex with D < 0
            public bool Seed;                    // on a toe edge, with a negative end
            public bool Region;
            public List<OEdge> Edges = new List<OEdge>();
        }

        /// <summary> the part of an edge of one surface inside a triangle of the other, from P to Q along the edge </summary>
        public struct OEdge
        {
            public long Key;                     // ekey(0, edge of A, triangle of B) or ekey(1, edge of B, triangle of A)
            public long P, Q;                    // overlay vertex keys
        }

        public readonly List<Face> Faces = new List<Face>();
        public readonly Dictionary<long, int> FaceOf = new Dictionary<long, int>(EdgeKeyComparer.Instance);     // (ta, tb) -> face
        public readonly Dictionary<long, sbyte> Sign = new Dictionary<long, sbyte>(EdgeKeyComparer.Instance);   // overlay vertex -> sign of D (+1 or -1)
        /// <summary> per overlay edge, its faces (the second is -1 on the boundary of A or B) </summary>
        public readonly Dictionary<long, Index2i> EdgeFaces = new Dictionary<long, Index2i>(EdgeKeyComparer.Instance);
        public readonly List<CatchLine> CatchLines = new List<CatchLine>();
        /// <summary> per catch line, the catch points (overlay edge keys) in order </summary>
        public readonly List<List<long>> CatchKeys = new List<List<long>>();
        /// <summary> per catch line, the region face of each segment (as CatchLine.Gradient) </summary>
        public readonly List<List<int>> CatchFaces = new List<List<int>>();
        /// <summary> the region touches the boundary of the terrain or the height limit: the slope does not daylight </summary>
        public bool NoDaylight;
        /// <summary> the region reaches the boundary of the slope surface at its height limit (the cap) </summary>
        public bool ReachedHeightLimit;
        public string Problem = "";

        // ---------------------------------------------------------------- keys

        public static long vkeyA(int v) { return v; }
        public static long vkeyB(int w) { return (1L << 62) | (uint)w; }
        public static long vkeyX(int ea, int eb) { return unchecked((long)(2UL << 62)) | ((long)ea << 31) | (uint)eb; }
        public static int vkind(long k) { return (int)((ulong)k >> 62); }
        public static int vfirst(long k) { return vkind(k) == 2 ? (int)((k >> 31) & 0x7FFFFFFF) : (int)(k & 0x7FFFFFFF); }
        public static int vsecond(long k) { return (int)(k & 0x7FFFFFFF); }
        public static long ekey(int kind, int e, int t) { return ((long)kind << 62) | ((long)e << 31) | (uint)t; }
        public static int ekind(long k) { return (int)((ulong)k >> 62); }
        public static int eedge(long k) { return (int)((k >> 31) & 0x7FFFFFFF); }
        public static int etri(long k) { return (int)(k & 0x7FFFFFFF); }
        static long fkey(int ta, int tb) { return ((long)ta << 32) | (uint)tb; }

        // ---------------------------------------------------------------- predicates with SoS

        /// <summary> orient(a0, a1, b) for a0, a1 of A and b of B, B shifted by (ε, ε²): never 0 for a0 != a1 </summary>
        public static int OrientAAB(Vector2d a0, Vector2d a1, Vector2d b)
        {
            int o = ExactPredicates.Orient2d(a0, a1, b);
            if (o != 0) return o;
            // the shift adds -(a1y - a0y) ε + (a1x - a0x) ε²
            if (a1.y != a0.y) return a1.y < a0.y ? 1 : -1;
            return a1.x > a0.x ? 1 : -1;
        }

        /// <summary> orient(b0, b1, a) for b0, b1 of B and a of A: shifting b0, b1 by e is shifting a by -e </summary>
        public static int OrientBBA(Vector2d b0, Vector2d b1, Vector2d a)
        {
            int o = ExactPredicates.Orient2d(b0, b1, a);
            if (o != 0) return o;
            // the shift adds (b1y - b0y) ε - (b1x - b0x) ε²
            if (b1.y != b0.y) return b1.y > b0.y ? 1 : -1;
            return b1.x < b0.x ? 1 : -1;
        }

        static sbyte no_work_if_zero(int s) { return (sbyte)(s >= 0 ? 1 : -1); }

        /// <summary> sign of D at a vertex v of A over the counter-clockwise triangle (t0, t1, t2) of B </summary>
        public static sbyte SignAV(Vector3d v, Vector3d t0, Vector3d t1, Vector3d t2, int dir)
        {
            // Orient3d of a counter-clockwise triangle is +1 above: v above T means S̃ - T > 0
            return no_work_if_zero(dir * ExactPredicates.Orient3d(t0, t1, t2, v));
        }

        /// <summary> sign of D at a vertex w of B over the counter-clockwise triangle (a0, a1, a2) of A </summary>
        public static sbyte SignBV(Vector3d w, Vector3d a0, Vector3d a1, Vector3d a2, int dir)
        {
            return no_work_if_zero(-dir * ExactPredicates.Orient3d(a0, a1, a2, w));
        }

        /// <summary> sign of D at the crossing of the edge (a0, a1) of A with the edge (b0, b1) of B </summary>
        public static sbyte SignX(Vector3d a0, Vector3d a1, Vector3d b0, Vector3d b1, int dir)
        {
            // sign(z_b - z_a) = Orient3d(a0, a1, b0, b1) * orient(a0, a1, b0) (TERRAIN.md 5.5); D = z_a - z_b for cut
            int s = ExactPredicates.Orient3d(a0, a1, b0, b1) * OrientAAB(a0.xy, a1.xy, b0.xy);
            return no_work_if_zero(-dir * s);
        }

        // ---------------------------------------------------------------- build

        /// <summary> false if the overlay is inconsistent (Problem); throws OperationCanceledException on cancel </summary>
        /// <summary> Compute: the terrain triangles whose XY box meets NearBox (a box around A), in order </summary>
        public readonly List<int> NearTerrain = new List<int>();
        public AxisAlignedBox2d NearBox = AxisAlignedBox2d.Empty;

        public bool Compute()
        {
            // (a second call starts from scratch)
            Faces.Clear(); FaceOf.Clear(); Sign.Clear(); EdgeFaces.Clear(); CatchLines.Clear(); CatchKeys.Clear(); CatchFaces.Clear(); facesAtVertex = null;
            containerOf.Clear(); catchPos.Clear(); crossPos.Clear(); bTriV.Clear(); bTriE.Clear();
            NoDaylight = false; ReachedHeightLimit = false; NotHeightField = false; Problem = ""; PhaseSeconds.Clear();
            NearTerrain.Clear(); NearBox = AxisAlignedBox2d.Empty;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // the B triangles under A (their boxes meet A's box), counter-clockwise, and an index over them: the
            // terrain elsewhere does not take part (a large terrain costs one pass over its triangles)
            var aAll = AxisAlignedBox2d.Empty;
            foreach (Vector3d v in A.Vertices()) aAll.Contain(v.xy);
            double am = Math.Max(Math.Max(Math.Abs(aAll.Min.x), Math.Abs(aAll.Max.x)), Math.Max(Math.Abs(aAll.Min.y), Math.Abs(aAll.Max.y)));
            aAll.Expand(1e-9 * aAll.MaxDim + 4 * ulp(am));
            var bTris = new List<int>();
            var boxes = new List<AxisAlignedBox2d>();
            // (and the terrain triangles within a quarter of A's size of it, for the composite's overlap check)
            NearBox = aAll; NearBox.Expand(0.25 * aAll.MaxDim);
            NearTerrain.Clear();
            foreach (int tb in B.TriangleIndices()) {
                Index3i t = B.GetTriangle(tb);
                Vector2d p0 = B.GetVertex(t.a).xy, p1 = B.GetVertex(t.b).xy, p2 = B.GetVertex(t.c).xy;
                var box = new AxisAlignedBox2d(p0);
                box.Contain(p1); box.Contain(p2);
                if (box.Max.x < NearBox.Min.x || box.Min.x > NearBox.Max.x || box.Max.y < NearBox.Min.y || box.Min.y > NearBox.Max.y) continue;
                NearTerrain.Add(tb);
                if (box.Max.x < aAll.Min.x || box.Min.x > aAll.Max.x || box.Max.y < aAll.Min.y || box.Min.y > aAll.Max.y) continue;
                int o = ExactPredicates.Orient2d(p0, p1, p2);
                if (o == 0) { Problem = $"terrain triangle {tb} is degenerate in XY"; NotHeightField = true; return false; }
                Index3i e = B.GetTriEdges(tb);
                bTriV[tb] = o > 0 ? new[] { t.a, t.b, t.c } : new[] { t.a, t.c, t.b };
                bTriE[tb] = o > 0 ? new[] { e.a, e.b, e.c } : new[] { e.c, e.b, e.a };
                bTris.Add(tb); boxes.Add(box);
            }
            var qt = new LooseQuadtree2d(boxes, false);
            TerrainTree = qt; TerrainTreeTris = bTris;
            // the terrain must be a height field where the overlay works: no two of these triangles overlap (exact)
            {
                var near = new List<int>();
                int overlaps = 0;
                for (int i = 0; i < bTris.Count; ++i) {
                    near.Clear();
                    qt.QueryRectangle(boxes[i], near);
                    foreach (int j in near) {
                        if (j <= i) continue;
                        if (separated(bTriV[bTris[i]], bTriV[bTris[j]]) == false && separated(bTriV[bTris[j]], bTriV[bTris[i]]) == false) overlaps++;
                    }
                }
                if (overlaps > 0) { Problem = $"{overlaps} pairs of terrain triangles overlap in XY"; NotHeightField = true; return false; }
            }
            PhaseSeconds["index"] = sw.Elapsed.TotalSeconds; sw.Restart();
            var hits = new List<int>();
            var uf = new List<int>();
            Func<int, int> find = null;
            find = (x) => { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; };

            var av = new int[3]; var ae = new int[3];
            var ap = new Vector3d[3]; var bp = new Vector3d[3];
            var vInB = new bool[3]; var wInA = new bool[3];
            var cr = new bool[3, 3];
            foreach (int ta in A.TriangleIndices()) {
                if (cancel != null && cancel()) throw new OperationCanceledException();
                Index3i t = A.GetTriangle(ta);
                Index3i te = A.GetTriEdges(ta);
                av[0] = t.a; av[1] = t.b; av[2] = t.c; ae[0] = te.a; ae[1] = te.b; ae[2] = te.c;
                for (int i = 0; i < 3; ++i) ap[i] = A.GetVertex(av[i]);
                var abox = new AxisAlignedBox2d(ap[0].xy);
                abox.Contain(ap[1].xy); abox.Contain(ap[2].xy);
                double m = Math.Max(Math.Max(Math.Abs(abox.Min.x), Math.Abs(abox.Max.x)), Math.Max(Math.Abs(abox.Min.y), Math.Abs(abox.Max.y)));
                // (a superset: the shift of SoS is infinitesimal, so touching boxes must be found)
                abox.Expand(1e-9 * abox.MaxDim + 4 * ulp(m));
                hits.Clear();
                qt.QueryRectangle(abox, hits);
                hits.Sort();
                foreach (int hi in hits) {
                    int tb = bTris[hi];
                    int[] bv = bTriV[tb], be = bTriE[tb];
                    for (int j = 0; j < 3; ++j) bp[j] = B.GetVertex(bv[j]);
                    bool any = false;
                    for (int i = 0; i < 3; ++i) {
                        vInB[i] = OrientBBA(bp[0].xy, bp[1].xy, ap[i].xy) > 0 && OrientBBA(bp[1].xy, bp[2].xy, ap[i].xy) > 0
                               && OrientBBA(bp[2].xy, bp[0].xy, ap[i].xy) > 0;
                        wInA[i] = OrientAAB(ap[0].xy, ap[1].xy, bp[i].xy) > 0 && OrientAAB(ap[1].xy, ap[2].xy, bp[i].xy) > 0
                               && OrientAAB(ap[2].xy, ap[0].xy, bp[i].xy) > 0;
                        any |= vInB[i] | wInA[i];
                    }
                    for (int i = 0; i < 3; ++i)
                        for (int j = 0; j < 3; ++j) {
                            Vector2d a0 = ap[i].xy, a1 = ap[(i + 1) % 3].xy, b0 = bp[j].xy, b1 = bp[(j + 1) % 3].xy;
                            cr[i, j] = OrientAAB(a0, a1, b0) != OrientAAB(a0, a1, b1) && OrientBBA(b0, b1, a0) != OrientBBA(b0, b1, a1);
                            any |= cr[i, j];
                        }
                    if (any == false) continue;

                    var f = new Face() { TA = ta, TB = tb };
                    int fi = Faces.Count;
                    Faces.Add(f); uf.Add(fi);
                    FaceOf[fkey(ta, tb)] = fi;
                    // the parts of the edges of ta inside tb
                    for (int i = 0; i < 3; ++i) {
                        int i1 = (i + 1) % 3;
                        long P = -1, Q = -1;
                        if (vInB[i]) P = vkeyA(av[i]);
                        if (vInB[i1]) Q = vkeyA(av[i1]);
                        for (int j = 0; j < 3; ++j) {
                            if (cr[i, j] == false) continue;
                            long x = vkeyX(ae[i], be[j]);
                            // entering tb if ap[i] is outside edge j
                            bool entry = OrientBBA(bp[j].xy, bp[(j + 1) % 3].xy, ap[i].xy) < 0;
                            if (entry) P = x; else Q = x;
                        }
                        if (P == -1 || Q == -1) continue;
                        f.Edges.Add(new OEdge() { Key = ekey(0, ae[i], tb), P = P, Q = Q });
                    }
                    // the parts of the edges of tb inside ta
                    for (int j = 0; j < 3; ++j) {
                        int j1 = (j + 1) % 3;
                        long P = -1, Q = -1;
                        if (wInA[j]) P = vkeyB(bv[j]);
                        if (wInA[j1]) Q = vkeyB(bv[j1]);
                        for (int i = 0; i < 3; ++i) {
                            if (cr[i, j] == false) continue;
                            long x = vkeyX(ae[i], be[j]);
                            bool entry = OrientAAB(ap[i].xy, ap[(i + 1) % 3].xy, bp[j].xy) < 0;
                            if (entry) P = x; else Q = x;
                        }
                        if (P == -1 || Q == -1) continue;
                        f.Edges.Add(new OEdge() { Key = ekey(1, be[j], ta), P = P, Q = Q });
                    }
                    // signs at the overlay vertices
                    // (with SoS a vertex lies in exactly one triangle of the other surface)
                    for (int i = 0; i < 3; ++i)
                        if (vInB[i] && Sign.ContainsKey(vkeyA(av[i])) == false) {
                            Sign[vkeyA(av[i])] = SignAV(ap[i], bp[0], bp[1], bp[2], dir);
                            containerOf[vkeyA(av[i])] = tb;
                        }
                    for (int j = 0; j < 3; ++j)
                        if (wInA[j] && Sign.ContainsKey(vkeyB(bv[j])) == false) {
                            Sign[vkeyB(bv[j])] = SignBV(bp[j], ap[0], ap[1], ap[2], dir);
                            containerOf[vkeyB(bv[j])] = ta;
                        }
                    foreach (OEdge oe in f.Edges)
                        foreach (long k in new[] { oe.P, oe.Q })
                            if (vkind(k) == 2 && Sign.ContainsKey(k) == false)
                                Sign[k] = sign_x(vfirst(k), vsecond(k));
                    // catch points: the edges where the sign changes (0 or 2 on a convex face, D linear)
                    int changes = 0;
                    foreach (OEdge oe in f.Edges) {
                        int sp = Sign[oe.P], sq = Sign[oe.Q];
                        if (sp < 0 || sq < 0) f.Negative = true;
                        if (sp != sq) {
                            if (changes == 0) f.C0 = oe.Key; else f.C1 = oe.Key;
                            changes++;
                        }
                        if (ekind(oe.Key) == 0 && (sp < 0 || sq < 0)) {
                            int kind;
                            if (aBoundaryKind.TryGetValue(eedge(oe.Key), out kind) && kind == KindToe) f.Seed = true;
                        }
                        // faces next to each other through an edge with a negative end are one region
                        Index2i ef;
                        if (EdgeFaces.TryGetValue(oe.Key, out ef)) {
                            if (ef.b != -1) { Problem = "an overlay edge with more than two faces"; return false; }
                            EdgeFaces[oe.Key] = new Index2i(ef.a, fi);
                            if (sp < 0 || sq < 0) {
                                int r0 = find(ef.a), r1 = find(fi);
                                if (r0 != r1) uf[Math.Max(r0, r1)] = Math.Min(r0, r1);
                            }
                        } else EdgeFaces[oe.Key] = new Index2i(fi, -1);
                    }
                    if (changes != 0 && changes != 2) { Problem = $"overlay face ({ta}, {tb}) has {changes} sign changes"; return false; }
                }
            }

            // the region: the components with a seed
            var seeded = new HashSet<int>();
            for (int i = 0; i < Faces.Count; ++i)
                if (Faces[i].Seed) seeded.Add(find(i));
            for (int i = 0; i < Faces.Count; ++i)
                Faces[i].Region = Faces[i].Negative && seeded.Contains(find(i));

            // does the region reach the terrain's boundary or the height limit?
            foreach (var kv in EdgeFaces) {
                if (kv.Value.b != -1 || Faces[kv.Value.a].Region == false) continue;
                Face f = Faces[kv.Value.a];
                foreach (OEdge oe in f.Edges) {
                    if (oe.Key != kv.Key) continue;
                    if (Sign[oe.P] > 0 && Sign[oe.Q] > 0) continue;
                    int reason = end_reason(oe.Key);
                    if (reason == (int)CatchLine.EndReasons.FootprintBoundary || reason == (int)CatchLine.EndReasons.HeightLimit)
                        NoDaylight = true;
                    if (reason == (int)CatchLine.EndReasons.HeightLimit)
                        ReachedHeightLimit = true;
                }
            }
            PhaseSeconds["faces"] = sw.Elapsed.TotalSeconds; sw.Restart();
            bool ok = chain();
            PhaseSeconds["lines"] = sw.Elapsed.TotalSeconds;
            return ok;
        }

        // does an edge line of the counter-clockwise triangle T (vertices of B) have all of U on its closed outer side?
        bool separated(int[] T, int[] U)
        {
            for (int e = 0; e < 3; ++e) {
                Vector2d p = B.GetVertex(T[e]).xy, q = B.GetVertex(T[(e + 1) % 3]).xy;
                if (ExactPredicates.Orient2d(p, q, B.GetVertex(U[0]).xy) <= 0 && ExactPredicates.Orient2d(p, q, B.GetVertex(U[1]).xy) <= 0
                    && ExactPredicates.Orient2d(p, q, B.GetVertex(U[2]).xy) <= 0)
                    return true;
            }
            return false;
        }

        /// <summary> the terrain is not a height field under the slope surface (degenerate or overlapping triangles) </summary>
        public bool NotHeightField;

        /// <summary> diagnostics: seconds per step (index of the terrain, overlay faces and region, catch lines) </summary>
        public readonly Dictionary<string, double> PhaseSeconds = new Dictionary<string, double>();

        int end_reason(long ek)
        {
            if (ekind(ek) == 1) return (int)CatchLine.EndReasons.FootprintBoundary;
            int kind;
            if (aBoundaryKind.TryGetValue(eedge(ek), out kind) == false) return (int)CatchLine.EndReasons.None;
            if (kind == KindToe) return (int)CatchLine.EndReasons.Toe;
            if (kind == KindLateral) return (int)CatchLine.EndReasons.LateralLimit;
            return (int)CatchLine.EndReasons.HeightLimit;
        }

        sbyte sign_x(int ea, int eb)
        {
            Index2i va = A.GetEdgeV(ea), vb = B.GetEdgeV(eb);
            return SignX(A.GetVertex(va.a), A.GetVertex(va.b), B.GetVertex(vb.a), B.GetVertex(vb.b), dir);
        }

        // ---------------------------------------------------------------- catch lines

        bool chain()
        {
            // the links of the catch points through the region faces
            var links = new Dictionary<long, List<long>>(EdgeKeyComparer.Instance);
            var order = new List<long>();
            var segFace = new Dictionary<(long, long), int>();
            for (int fi = 0; fi < Faces.Count; ++fi) {
                Face f = Faces[fi];
                if (f.Region == false || f.C0 == -1) continue;
                segFace[(f.C0, f.C1)] = fi; segFace[(f.C1, f.C0)] = fi;
                foreach (long c in new[] { f.C0, f.C1 }) {
                    List<long> l;
                    if (links.TryGetValue(c, out l) == false) { l = new List<long>(); links[c] = l; order.Add(c); }
                    l.Add(c == f.C0 ? f.C1 : f.C0);
                }
            }
            var done = new HashSet<long>(EdgeKeyComparer.Instance);
            // open chains first (from their ends, in the order the ends were found), then the closed ones
            for (int pass = 0; pass < 2; ++pass) {
                foreach (long start in order) {
                    if (done.Contains(start)) continue;
                    if (pass == 0 && links[start].Count != 1) continue;
                    var keys = new List<long>();
                    long prev = -1, cur = start;
                    while (true) {
                        keys.Add(cur);
                        done.Add(cur);
                        long next = -1;
                        foreach (long n in links[cur])
                            if (n != prev && done.Contains(n) == false) { next = n; break; }
                        if (next == -1) break;
                        prev = cur; cur = next;
                    }
                    var line = new CatchLine();
                    bool closed = pass == 1;
                    if (closed && (links[cur].Count != 2 || links[cur].Contains(start) == false)) { Problem = "a catch line does not close"; return false; }
                    line.Closed = closed;
                    if (closed == false) {
                        line.StartReason = (CatchLine.EndReasons)end_reason(keys[0]);
                        line.EndReason = (CatchLine.EndReasons)end_reason(keys[keys.Count - 1]);
                        if (links[cur].Count != 1) { Problem = "an open catch line ends inside the overlay"; return false; }
                    }
                    foreach (long k in keys) line.Points.Add(CatchPoint(k));
                    var faces = new List<int>();
                    int segs = closed ? keys.Count : keys.Count - 1;
                    for (int i = 0; i < segs; ++i) faces.Add(segFace[(keys[i], keys[(i + 1) % keys.Count])]);
                    CatchLines.Add(line);
                    CatchKeys.Add(keys);
                    CatchFaces.Add(faces);
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- volumes (TERRAIN.md 5.7)

        /// <summary>
        /// per piece (the group of the slope triangle): the volume between the terrain and the slope over the region (cut:
        /// the terrain above the slope; fill: the slope above the terrain), its plan area and its area on the slope
        /// surface. On each region face (a convex polygon, the XY intersection of its two triangles) the difference is
        /// linear: split at 0 and integrated exactly in doubles (area times the mean of the corners, fans), relative to
        /// the centre of the slope surface's box. Faces that the catch line crosses count with their part on the region
        /// side.
        /// </summary>
        public void Integrate(int pieces, out double[] volume, out double[] planArea, out double[] slopeArea)
        {
            volume = new double[pieces]; planArea = new double[pieces]; slopeArea = new double[pieces];
            var box = AxisAlignedBox2d.Empty;
            foreach (Vector3d v in A.Vertices()) box.Contain(v.xy);
            Vector2d o = box.Center;
            var ta = new Vector2d[3]; var tb = new Vector2d[3];
            foreach (Face f in Faces) {
                if (f.Region == false) continue;
                int piece = A.GetTriangleGroup(f.TA);
                if (piece < 0 || piece >= pieces) continue;
                Index3i av = A.GetTriangle(f.TA), bv = B.GetTriangle(f.TB);
                Vector3d a0 = A.GetVertex(av.a), a1 = A.GetVertex(av.b), a2 = A.GetVertex(av.c);
                Vector3d b0 = B.GetVertex(bv.a), b1 = B.GetVertex(bv.b), b2 = B.GetVertex(bv.c);
                LinearZ s, t;
                if (LinearZ.Of(a0, a1, a2, o, out s) == false || LinearZ.Of(b0, b1, b2, o, out t) == false) continue;
                ta[0] = a0.xy - o; ta[1] = a1.xy - o; ta[2] = a2.xy - o;
                tb[0] = b0.xy - o; tb[1] = b1.xy - o; tb[2] = b2.xy - o;
                List<Vector2d> poly = clip_convex(ccw(ta), ccw(tb));
                if (poly.Count < 3) continue;
                // the region side: dir (T - S) > 0
                var d = new List<double>(poly.Count);
                foreach (Vector2d p in poly) d.Add(dir * (t.Z(p) - s.Z(p)));
                var part = new List<Vector2d>(); var pd = new List<double>();
                for (int i = 0; i < poly.Count; ++i) {
                    int j = (i + 1) % poly.Count;
                    if (d[i] >= 0) { part.Add(poly[i]); pd.Add(d[i]); }
                    if ((d[i] > 0 && d[j] < 0) || (d[i] < 0 && d[j] > 0)) {
                        double u = d[i] / (d[i] - d[j]);
                        part.Add(poly[i] + u * (poly[j] - poly[i])); pd.Add(0);
                    }
                }
                double ar = 0, integral = 0;
                for (int i = 1; i + 1 < part.Count; ++i) {
                    double tri = 0.5 * ((part[i].x - part[0].x) * (part[i + 1].y - part[0].y) - (part[i].y - part[0].y) * (part[i + 1].x - part[0].x));
                    ar += tri;
                    integral += tri * (pd[0] + pd[i] + pd[i + 1]) / 3;
                }
                volume[piece] += integral;
                planArea[piece] += ar;
                slopeArea[piece] += ar * Math.Sqrt(1 + s.Cx * s.Cx + s.Cy * s.Cy);
            }
        }

        /// <summary>
        /// a face's horizontal gradient |∇(S - T)| (doubles) and whether the two planes coincide (exact: every terrain
        /// corner on the plane of the slope triangle)
        /// </summary>
        public void FaceGradient(int face, out double gradient, out bool coplanar)
        {
            face_gradient(face, out gradient, out coplanar);
        }

        /// <summary>
        /// a catch-line segment on this face: its gradient, and whether it touches a planar contact. The segment's own
        /// face cannot be planar (a region face has D &lt; 0 somewhere); a face where the slope and the terrain coincide
        /// counts as no work (an exact 0), so the region ends at it, and the catch line runs along its border: a coplanar
        /// face that shares an overlay vertex with the segment's face marks it (TERRAIN.md 5.5, TangentialContact)
        /// </summary>
        public void SegmentContact(int face, out double gradient, out bool tangential)
        {
            face_gradient(face, out gradient, out tangential);
            if (tangential) return;
            if (facesAtVertex == null) {
                facesAtVertex = new Dictionary<long, List<int>>(EdgeKeyComparer.Instance);
                for (int fi = 0; fi < Faces.Count; ++fi)
                    foreach (var oe in Faces[fi].Edges)
                        foreach (long k in new[] { oe.P, oe.Q }) {
                            List<int> l;
                            if (facesAtVertex.TryGetValue(k, out l) == false) { l = new List<int>(); facesAtVertex[k] = l; }
                            if (l.Contains(fi) == false) l.Add(fi);
                        }
            }
            // (a neighbour across an edge or at a corner: the catch line may touch the contact at a corner only)
            foreach (var oe in Faces[face].Edges)
                foreach (long k in new[] { oe.P, oe.Q }) {
                    List<int> l;
                    if (facesAtVertex.TryGetValue(k, out l) == false) continue;
                    foreach (int g in l) {
                        if (g == face) continue;
                        double gg; bool cp;
                        face_gradient(g, out gg, out cp);
                        if (cp) { tangential = true; return; }
                    }
                }
        }

        Dictionary<long, List<int>> facesAtVertex;

        void face_gradient(int face, out double gradient, out bool coplanar)
        {
            Face f = Faces[face];
            Index3i av = A.GetTriangle(f.TA), bv = B.GetTriangle(f.TB);
            Vector3d a0 = A.GetVertex(av.a), a1 = A.GetVertex(av.b), a2 = A.GetVertex(av.c);
            Vector3d b0 = B.GetVertex(bv.a), b1 = B.GetVertex(bv.b), b2 = B.GetVertex(bv.c);
            coplanar = ExactPredicates.Orient3d(a0, a1, a2, b0) == 0 && ExactPredicates.Orient3d(a0, a1, a2, b1) == 0
                    && ExactPredicates.Orient3d(a0, a1, a2, b2) == 0;
            LinearZ s, t;
            if (LinearZ.Of(a0, a1, a2, a0.xy, out s) == false || LinearZ.Of(b0, b1, b2, a0.xy, out t) == false) { gradient = 0; return; }
            double gx = s.Cx - t.Cx, gy = s.Cy - t.Cy;
            gradient = coplanar ? 0 : Math.Sqrt(gx * gx + gy * gy);
        }

        /// <summary> z = C0 + Cx x + Cy y of the plane through three points, in coordinates relative to an origin </summary>
        internal struct LinearZ
        {
            public double C0, Cx, Cy;
            public double Z(Vector2d p) { return C0 + Cx * p.x + Cy * p.y; }
            public static bool Of(Vector3d a, Vector3d b, Vector3d c, Vector2d origin, out LinearZ z)
            {
                z = new LinearZ();
                Vector2d pa = a.xy - origin, pb = b.xy - origin, pc = c.xy - origin;
                double ux = pb.x - pa.x, uy = pb.y - pa.y, uz = b.z - a.z, vx = pc.x - pa.x, vy = pc.y - pa.y, vz = c.z - a.z;
                double nz = ux * vy - uy * vx;
                if (nz == 0) return false;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz;
                z.Cx = -nx / nz; z.Cy = -ny / nz;
                z.C0 = a.z - z.Cx * pa.x - z.Cy * pa.y;
                return true;
            }
        }

        static Vector2d[] ccw(Vector2d[] t)
        {
            double cr = (t[1].x - t[0].x) * (t[2].y - t[0].y) - (t[1].y - t[0].y) * (t[2].x - t[0].x);
            return cr >= 0 ? new[] { t[0], t[1], t[2] } : new[] { t[0], t[2], t[1] };
        }

        // the intersection of two counter-clockwise convex polygons (Sutherland-Hodgman, approximate)
        static List<Vector2d> clip_convex(Vector2d[] subject, Vector2d[] clipper)
        {
            var poly = new List<Vector2d>(subject);
            for (int e = 0; e < clipper.Length && poly.Count > 0; ++e) {
                Vector2d c0 = clipper[e], c1 = clipper[(e + 1) % clipper.Length];
                Func<Vector2d, double> side = (p) => (c1.x - c0.x) * (p.y - c0.y) - (c1.y - c0.y) * (p.x - c0.x);
                var next = new List<Vector2d>(poly.Count + 2);
                for (int i = 0; i < poly.Count; ++i) {
                    Vector2d p = poly[i], q = poly[(i + 1) % poly.Count];
                    double sp = side(p), sq = side(q);
                    if (sp >= 0) next.Add(p);
                    if ((sp > 0 && sq < 0) || (sp < 0 && sq > 0)) next.Add(p + (sp / (sp - sq)) * (q - p));
                }
                poly = next;
            }
            return poly;
        }

        // ---------------------------------------------------------------- positions (approximate; 1c reports their accuracy)

        readonly Dictionary<long, Vector3d> catchPos = new Dictionary<long, Vector3d>(EdgeKeyComparer.Instance);
        readonly Dictionary<long, Vector2d> crossPos = new Dictionary<long, Vector2d>(EdgeKeyComparer.Instance);

        /// <summary> the XY of an overlay vertex: exact for vertices, the rounded crossing otherwise (canonical) </summary>
        public Vector2d VertexXY(long k)
        {
            int kind = vkind(k);
            if (kind == 0) return A.GetVertex(vfirst(k)).xy;
            if (kind == 1) return B.GetVertex(vfirst(k)).xy;
            Vector2d r;
            if (crossPos.TryGetValue(k, out r)) return r;
            double ta, tb;
            Vector2d at;
            crossing(vfirst(k), vsecond(k), out ta, out tb, out at);
            Index2i va = A.GetEdgeV(vfirst(k));
            Vector2d a0 = A.GetVertex(va.a).xy, a1 = A.GetVertex(va.b).xy;
            r = double.IsNaN(at.x) ? new Vector2d(a0.x + ta * (a1.x - a0.x), a0.y + ta * (a1.y - a0.y)) : at;
            crossPos[k] = r;
            return r;
        }

        // the parameters of the crossing on the A edge and on the B edge (in the stored vertex order of each edge)
        void crossing(int ea, int eb, out double ta, out double tb)
        {
            Vector2d at;
            crossing(ea, eb, out ta, out tb, out at);
        }

        // (a crossing that SoS decided, with a vertex of one edge exactly on the line of the other, is that vertex: 'at'
        // is it, exactly, and its parameter on its own edge is 0 or 1; otherwise 'at' is NaN)
        void crossing(int ea, int eb, out double ta, out double tb, out Vector2d at)
        {
            Index2i va = A.GetEdgeV(ea), vb = B.GetEdgeV(eb);
            Vector2d a0 = A.GetVertex(va.a).xy, a1 = A.GetVertex(va.b).xy, b0 = B.GetVertex(vb.a).xy, b1 = B.GetVertex(vb.b).xy;
            Vector2d da = a1 - a0, db = b1 - b0, d0 = b0 - a0;
            at = new Vector2d(double.NaN, double.NaN);
            if (ExactPredicates.Orient2d(a0, a1, b0) == 0) { at = b0; tb = 0; ta = clamp01((b0 - a0).Dot(da) / da.LengthSquared); return; }
            if (ExactPredicates.Orient2d(a0, a1, b1) == 0) { at = b1; tb = 1; ta = clamp01((b1 - a0).Dot(da) / da.LengthSquared); return; }
            if (ExactPredicates.Orient2d(b0, b1, a0) == 0) { at = a0; ta = 0; tb = clamp01((a0 - b0).Dot(db) / db.LengthSquared); return; }
            if (ExactPredicates.Orient2d(b0, b1, a1) == 0) { at = a1; ta = 1; tb = clamp01((a1 - b0).Dot(db) / db.LengthSquared); return; }
            double den = da.x * db.y - da.y * db.x;
            ta = (d0.x * db.y - d0.y * db.x) / den;
            tb = (d0.x * da.y - d0.y * da.x) / den;
            ta = clamp01(ta); tb = clamp01(tb);
        }

        static double clamp01(double t) { return (t >= 0) ? (t <= 1 ? t : 1) : 0; }

        /// <summary> z of S̃ and of the terrain at an overlay vertex (approximate) </summary>
        public void Heights(long k, out double zs, out double zt)
        {
            int kind = vkind(k);
            if (kind == 0) {
                Vector3d v = A.GetVertex(vfirst(k));
                zs = v.z;
                zt = terrain_z_at_a_vertex(k);
            } else if (kind == 1) {
                Vector3d w = B.GetVertex(vfirst(k));
                zt = w.z;
                zs = slope_z_at_b_vertex(k);
            } else {
                double ta, tb;
                crossing(vfirst(k), vsecond(k), out ta, out tb);
                Index2i va = A.GetEdgeV(vfirst(k)), vb = B.GetEdgeV(vsecond(k));
                double za0 = A.GetVertex(va.a).z, za1 = A.GetVertex(va.b).z, zb0 = B.GetVertex(vb.a).z, zb1 = B.GetVertex(vb.b).z;
                zs = za0 + ta * (za1 - za0);
                zt = zb0 + tb * (zb1 - zb0);
            }
        }

        readonly Dictionary<long, int> containerOf = new Dictionary<long, int>();

        double terrain_z_at_a_vertex(long k)
        {
            int tb = Container(k);
            int[] bv = bTriV[tb];
            return PlaneZ(B.GetVertex(bv[0]), B.GetVertex(bv[1]), B.GetVertex(bv[2]), A.GetVertex(vfirst(k)).xy);
        }

        double slope_z_at_b_vertex(long k)
        {
            int ta = Container(k);
            Index3i t = A.GetTriangle(ta);
            return PlaneZ(A.GetVertex(t.a), A.GetVertex(t.b), A.GetVertex(t.c), B.GetVertex(vfirst(k)).xy);
        }

        /// <summary> the triangle of the other surface that contains a vertex of A or B (-1 if none) </summary>
        public int Container(long k)
        {
            int c;
            return containerOf.TryGetValue(k, out c) ? c : -1;
        }

        /// <summary> the height of the plane through a, b, c at p (approximate) </summary>
        public static double PlaneZ(Vector3d a, Vector3d b, Vector3d c, Vector2d p)
        {
            Vector3d u = b - a, v = c - a;
            double nx = u.y * v.z - u.z * v.y, ny = u.z * v.x - u.x * v.z, nz = u.x * v.y - u.y * v.x;
            return a.z - (nx * (p.x - a.x) + ny * (p.y - a.y)) / nz;
        }

        /// <summary> the position of a catch point: on its overlay edge where the interpolated D is 0, z on S̃ </summary>
        public Vector3d CatchPoint(long ek)
        {
            Vector3d r;
            if (catchPos.TryGetValue(ek, out r)) return r;
            Face f = Faces[EdgeFaces[ek].a];
            OEdge oe = default(OEdge);
            foreach (OEdge e in f.Edges) if (e.Key == ek) oe = e;
            double zsP, ztP, zsQ, ztQ;
            Heights(oe.P, out zsP, out ztP);
            Heights(oe.Q, out zsQ, out ztQ);
            double dP = forced(dir * (zsP - ztP), Sign[oe.P]), dQ = forced(dir * (zsQ - ztQ), Sign[oe.Q]);
            double t = clamp01(dP / (dP - dQ));
            Vector2d p = VertexXY(oe.P), q = VertexXY(oe.Q);
            r = new Vector3d(p.x + t * (q.x - p.x), p.y + t * (q.y - p.y), zsP + t * (zsQ - zsP));
            catchPos[ek] = r;
            return r;
        }

        // a double value with its sign forced to the exact one (s = +1 means D >= 0, "no work")
        static double forced(double d, int s)
        {
            if (s > 0) return (d >= 0 && MathUtil.IsFinite(d)) ? d : 0;
            return (d < 0 && MathUtil.IsFinite(d)) ? d : -double.Epsilon;
        }

        static double ulp(double x)
        {
            x = Math.Abs(x);
            return ExactInterval.Up(x) - x;
        }
    }
}
