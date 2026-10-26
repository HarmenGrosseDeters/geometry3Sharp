// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary> outcome of IntrinsicTriangulation.ExtractCommonSubdivision </summary>
    public enum CommonSubdivisionStatus
    {
        /// <summary> the faces tile every input triangle and every intrinsic triangle (areas checked) </summary>
        Success,
        /// <summary>
        /// the traced intrinsic edges are not consistent (eg two crossings of an input edge in the wrong
        /// order, or a trace ending in the wrong input triangle); Error says where. The faces are not reliable.
        /// </summary>
        Inconsistent
    }

    /// <summary>
    /// Common subdivision of an intrinsic triangulation and its input mesh: the polygons in which the
    /// intrinsic triangles cut the input triangles. Each face lies in one input triangle and one intrinsic
    /// triangle, and is convex (both are convex in the flat input triangle). The face areas sum to the
    /// input triangle areas and to the intrinsic triangle areas.
    /// </summary>
    public class IntrinsicCommonSubdivision
    {
        public class Face
        {
            /// <summary> mesh triangle id the face lies in </summary>
            public int InputTriangle;
            /// <summary> triangle index of the intrinsic triangulation the face lies in </summary>
            public int IntrinsicTriangle;
            /// <summary> corners counter-clockwise, ids into Points </summary>
            public int[] Corners;
            /// <summary> corners as barycentric coordinates in the input triangle </summary>
            public Vector3d[] Barycentric;
            /// <summary> area (in the input triangle; with mollification in its mollified geometry) </summary>
            public double Area;
        }

        public CommonSubdivisionStatus Status;
        /// <summary> null on success; otherwise the first inconsistency found </summary>
        public string Error;
        public List<Face> Faces = new List<Face>();
        /// <summary> positions of the subdivision vertices: the triangulation vertices first (by vertex index), then the edge crossings </summary>
        public List<Vector3d> Points = new List<Vector3d>();
        /// <summary> largest relative difference between an input triangle area and the sum of its faces </summary>
        public double MaxInputAreaError;
        /// <summary> largest relative difference between an intrinsic triangle area and the sum of its faces </summary>
        public double MaxIntrinsicAreaError;

        /// <summary>
        /// the subdivision as a triangle mesh (each face fanned from its first corner); the triangle group
        /// is the intrinsic triangle index
        /// </summary>
        public DMesh3 ToMesh()
        {
            var m = new DMesh3(MeshComponents.FaceGroups);
            foreach (Vector3d p in Points) m.AppendVertex(p);
            foreach (Face f in Faces)
                for (int k = 1; k + 1 < f.Corners.Length; ++k)
                    m.AppendTriangle(new Index3i(f.Corners[0], f.Corners[k], f.Corners[k + 1]), f.IntrinsicTriangle);
            return m;
        }
    }


    // Common subdivision by tracing every intrinsic edge over the input mesh (signposts; Sharp, Soliman and
    // Crane 2019, section 3.3) and an arrangement of the traced segments per input triangle.
    public partial class IntrinsicTriangulation
    {
        // a subdivision vertex inside or on input triangle t
        struct SubPoint
        {
            public int Id;
            public double U;            // parameter along the canonical input halfedge (for edge points)
        }

        // labelled piece of an arrangement edge: intrinsic halfedge h whose trace runs the same way
        // (Same) or the opposite way
        struct Label
        {
            public int H;
            public bool Same;
        }

        /// <summary>
        /// The common subdivision of this triangulation and the input mesh (see IntrinsicCommonSubdivision):
        /// every intrinsic edge is traced over the input (TraceEdge); within each input triangle the traced
        /// segments, which cannot cross, cut it into convex faces, and each face is assigned the intrinsic
        /// triangle on its side of the segments. The result is checked: every input triangle and every
        /// intrinsic triangle must be tiled by its faces (areas to a relative 1e-6); otherwise Status is
        /// Inconsistent. Integer (normal) coordinates (Gillespie, Sharp and Crane 2021) would make the
        /// combinatorics exact; here they come from the traces, which are exact up to rounding.
        /// </summary>
        public IntrinsicCommonSubdivision ExtractCommonSubdivision()
        {
            var R = new IntrinsicCommonSubdivision();
            for (int v = 0; v < N; ++v) R.Points.Add(VertexPosition(v));
            var edgePts = new Dictionary<int, List<SubPoint>>();        // canonical input halfedge -> points on it
            var interior = new List<int>[F0];
            var chords = new List<int[]>[F0];                            // per input triangle: {a, b, h} (trace a -> b)
            var edgeLabels = new Dictionary<(int, int, int), Label>();   // (canonical halfedge, point a, point b) -> label
            Func<string, IntrinsicCommonSubdivision> fail = (msg) => {
                if (R.Status == CommonSubdivisionStatus.Success) { R.Status = CommonSubdivisionStatus.Inconsistent; R.Error = msg; }
                return R;
            };
            Func<int, int> canonical = (ih) => (inTwin[ih] >= 0 && inTwin[ih] < ih) ? inTwin[ih] : ih;
            Func<int, double, double> toCanonical = (ih, u) => canonical(ih) == ih ? u : 1 - u;
            Action<int, int, double> addEdgePoint = (ih, id, u) => {
                int c = canonical(ih);
                List<SubPoint> l;
                if (edgePts.TryGetValue(c, out l) == false) edgePts[c] = l = new List<SubPoint>();
                l.Add(new SubPoint() { Id = id, U = toCanonical(ih, u) });
            };

            // where each vertex lies: corner, edge (input halfedge, parameter) or inside a triangle
            int[] onEdge = new int[N];
            double[] onEdgeU = new double[N];
            int[] inTri = new int[N];
            const double snap = 1e-9;
            for (int v = 0; v < N; ++v) {
                onEdge[v] = -1; inTri[v] = -1;
                if (v < N0) continue;
                if (vInEdge[v] >= 0) { onEdge[v] = vInEdge[v]; onEdgeU[v] = vInEdgeT[v]; }
                else {
                    int t = vInTri[v];
                    Vector3d b = barycentric(t, vInPos[v]);
                    int small = 0, k0 = -1;
                    for (int k = 0; k < 3; ++k) if (b[k] < snap) { small++; k0 = k; }
                    if (small >= 2) return fail("inserted vertex " + v + " lies on an input vertex");
                    if (small == 1) {
                        // on the edge opposite corner k0: halfedge from corner k0+1 to k0+2
                        int ih = 3 * t + (k0 + 1) % 3;
                        double sum = b[(k0 + 1) % 3] + b[(k0 + 2) % 3];
                        onEdge[v] = ih; onEdgeU[v] = b[(k0 + 2) % 3] / sum;
                    } else
                        inTri[v] = t;
                }
                if (onEdge[v] >= 0) addEdgePoint(onEdge[v], v, onEdgeU[v]);
                else {
                    int t = inTri[v];
                    if (interior[t] == null) interior[t] = new List<int>();
                    interior[t].Add(v);
                }
            }

            // trace every edge, split into segments per input triangle
            var crossings = new List<WalkCrossing>();
            var segs = new List<int[]>();       // {triangle, a, b}
            for (int h = 0; h < 3 * F; ++h) {
                if (hTwin[h] >= 0 && hTwin[h] < h) continue;
                int vs = hV[h], ve = Head(h);
                int t; Vector2d p, d;
                start_walk(vs, hAngle[h], out t, out p, out d);
                crossings.Clear();
                if (walk_input(ref t, ref p, ref d, hLen[h], crossings) == false)
                    return fail("trace of intrinsic edge " + h + " does not reach its head (boundary or step limit)");
                segs.Clear();
                int cur = vs;
                bool ended = false;
                for (int ci = 0; ci < crossings.Count && ended == false; ++ci) {
                    WalkCrossing c = crossings[ci];
                    int corner = -1;
                    if (c.U < snap) corner = input_tail(c.Halfedge);
                    else if (c.U > 1 - snap) corner = input_head(c.Halfedge);
                    if (corner < 0) {
                        // at an inserted vertex on this input edge
                        int ce = canonical(c.Halfedge);
                        double cu = toCanonical(c.Halfedge, c.U);
                        if (onEdge[vs] >= 0 && canonical(onEdge[vs]) == ce && Math.Abs(toCanonical(onEdge[vs], onEdgeU[vs]) - cu) < snap) corner = vs;
                        else if (onEdge[ve] >= 0 && canonical(onEdge[ve]) == ce && Math.Abs(toCanonical(onEdge[ve], onEdgeU[ve]) - cu) < snap) corner = ve;
                        else {
                            // another inserted vertex on this input edge at the crossing
                            List<SubPoint> l;
                            if (edgePts.TryGetValue(ce, out l))
                                foreach (SubPoint sp in l)
                                    if (sp.Id < N && Math.Abs(sp.U - cu) < snap) { corner = sp.Id; break; }
                        }
                    }
                    if (corner >= 0) {
                        if (corner == vs && cur == vs) continue;            // still leaving the start vertex
                        if (corner == ve) { segs.Add(new[] { c.Halfedge / 3, cur, ve }); ended = true; continue; }
                        return fail("intrinsic edge " + h + " passes through vertex " + corner);
                    }
                    int id = R.Points.Count;
                    R.Points.Add(to_world(c.Halfedge / 3, c.Point));
                    addEdgePoint(c.Halfedge, id, c.U);
                    segs.Add(new[] { c.Halfedge / 3, cur, id });
                    cur = id;
                }
                if (ended == false) segs.Add(new[] { t, cur, ve });
                foreach (int[] s in segs) {
                    if (s[1] == s[2]) continue;
                    if (chords[s[0]] == null) chords[s[0]] = new List<int[]>();
                    chords[s[0]].Add(new[] { s[1], s[2], h });
                }
            }

            foreach (var l in edgePts.Values) l.Sort((x, y) => x.U.CompareTo(y.U));

            // per input triangle: nodes, arrangement edges, faces
            var faceTri = new List<int>();                  // per face: intrinsic triangle (-1 unknown)
            var faceArea = new List<double>();
            var pieceFaces = new Dictionary<(int, int, int), List<int>>();     // unlabelled boundary piece -> faces beside it
            Func<int, int, int, (int, int, int)> pieceKey = (c, a, b) => (c, Math.Min(a, b), Math.Max(a, b));
            var noPiece = (-1, -1, -1);
            long P = R.Points.Count + 1;
            var faceCorners = new List<int[]>();
            var faceInput = new List<int>();
            var facePos = new List<Vector2d[]>();
            // first pass, all input triangles: nodes, and the labels of the edge pieces a trace runs along (a
            // trace along an input edge is recorded on one side only; both sides read the label below)
            var posOf = new Dictionary<int, Vector2d>[F0];
            var edgeSeq = new List<int>[F0][];
            var chordsOf = new List<int[]>[F0];
            for (int t = 0; t < F0; ++t) {
                // node positions in the layout of t
                var pos = new Dictionary<int, Vector2d>();
                int[] cornerId = new int[3];
                Index3i tri = Mesh.GetTriangle(inTid[t]);
                for (int k = 0; k < 3; ++k) { cornerId[k] = toIndex[tri[k]]; pos[cornerId[k]] = inLayout[3 * t + k]; }
                // boundary: the points along each edge, counter-clockwise
                var onThisEdge = new List<int>[3];
                for (int k = 0; k < 3; ++k) {
                    int ih = 3 * t + k, c = canonical(ih);
                    Vector2d A = inLayout[ih], B = inLayout[3 * t + (k + 1) % 3];
                    var seq = new List<int>() { cornerId[k] };
                    List<SubPoint> l;
                    if (edgePts.TryGetValue(c, out l)) {
                        if (c == ih) foreach (var sp in l) { seq.Add(sp.Id); pos[sp.Id] = A + sp.U * (B - A); }
                        else for (int m = l.Count - 1; m >= 0; --m) { seq.Add(l[m].Id); pos[l[m].Id] = A + (1 - l[m].U) * (B - A); }
                    }
                    seq.Add(cornerId[(k + 1) % 3]);
                    onThisEdge[k] = seq;
                }
                if (interior[t] != null)
                    foreach (int v in interior[t]) pos[v] = vInPos[v];
                // which edges (closures) of t each node is on, as a bit mask
                var edgeMask = new Dictionary<int, int>();
                for (int k = 0; k < 3; ++k)
                    foreach (int id in onThisEdge[k]) { int mm; edgeMask.TryGetValue(id, out mm); edgeMask[id] = mm | (1 << k); }
                Func<int, int, bool> onEdgeK = (id, k) => { int mm; return edgeMask.TryGetValue(id, out mm) && (mm & (1 << k)) != 0; };
                // chords along an edge of t label its pieces; the others become arrangement edges
                var chordList = new List<int[]>();
                posOf[t] = pos; edgeSeq[t] = onThisEdge; chordsOf[t] = chordList;
                if (chords[t] != null)
                    foreach (int[] ch in chords[t]) {
                        if (pos.ContainsKey(ch[0]) == false || pos.ContainsKey(ch[1]) == false)
                            return fail("trace of intrinsic edge " + ch[2] + " leaves input triangle " + inTid[t] + " at a point not on it");
                        int along = -1;
                        for (int k = 0; k < 3 && along < 0; ++k)
                            if (onEdgeK(ch[0], k) && onEdgeK(ch[1], k)) along = k;
                        if (along < 0) { chordList.Add(ch); continue; }
                        var seq = onThisEdge[along];
                        int ia = seq.IndexOf(ch[0]), ib = seq.IndexOf(ch[1]);
                        int c = canonical(3 * t + along);
                        for (int m = Math.Min(ia, ib); m < Math.Max(ia, ib); ++m) {
                            // the canonical direction of this piece runs from seq[m] to seq[m+1] if c == 3t+along
                            bool pieceForward = (c == 3 * t + along);
                            int a = pieceForward ? seq[m] : seq[m + 1], b = pieceForward ? seq[m + 1] : seq[m];
                            bool traceForward = ib > ia;     // trace runs along 3t+along
                            bool same = (traceForward == pieceForward);
                            edgeLabels[pieceKey(c, a, b)] = new Label() { H = ch[2], Same = same };      // relative to canonical direction
                        }
                    }
            }

            // second pass: arrangement and faces per input triangle
            for (int t = 0; t < F0; ++t) {
                var pos = posOf[t];
                var onThisEdge = edgeSeq[t];
                var chordList = chordsOf[t];
                var darts = new Dictionary<int, List<int>>();       // node -> outgoing dart ids
                var dFrom = new List<int>(); var dTo = new List<int>();
                var dLabel = new List<Label?>(); var dPiece = new List<(int, int, int)>(); var dFwd = new List<bool>();
                Action<int, int, Label?, (int, int, int), bool> addDart = (a, b, lab, piece, fwdc) => {
                    int id = dFrom.Count;
                    dFrom.Add(a); dTo.Add(b); dLabel.Add(lab); dPiece.Add(piece); dFwd.Add(fwdc);
                    List<int> l;
                    if (darts.TryGetValue(a, out l) == false) darts[a] = l = new List<int>();
                    l.Add(id);
                };
                // boundary darts (counter-clockwise, face inside t on their left)
                for (int k = 0; k < 3; ++k) {
                    int c = canonical(3 * t + k);
                    bool fwd = (c == 3 * t + k);
                    var seq = onThisEdge[k];
                    for (int m = 0; m + 1 < seq.Count; ++m) {
                        var key = pieceKey(c, seq[m], seq[m + 1]);
                        addDart(seq[m], seq[m + 1], null, key, fwd);
                        addDart(seq[m + 1], seq[m], null, noPiece, false);       // outside
                    }
                }
                foreach (int[] ch in chordList) {
                    addDart(ch[0], ch[1], new Label() { H = ch[2], Same = true }, noPiece, false);
                    addDart(ch[1], ch[0], new Label() { H = ch[2], Same = false }, noPiece, false);
                }
                // sort darts around nodes counter-clockwise; index of each dart in its node list
                int nd = dFrom.Count;
                int[] rank = new int[nd];
                foreach (var kv in darts) {
                    Vector2d o = pos[kv.Key];
                    kv.Value.Sort((x, y) => {
                        Vector2d ex = pos[dTo[x]] - o, ey = pos[dTo[y]] - o;
                        return Math.Atan2(ex.y, ex.x).CompareTo(Math.Atan2(ey.y, ey.x));
                    });
                    for (int m = 0; m < kv.Value.Count; ++m) rank[kv.Value[m]] = m;
                }
                // twin dart: the reverse
                var twinOf = new Dictionary<long, int>();
                for (int dd = 0; dd < nd; ++dd) twinOf[(long)dFrom[dd] * P + dTo[dd]] = dd;
                bool[] used = new bool[nd];
                int outer = 0;
                double triArea = 0.5 * Math.Abs(cross(inLayout[3 * t + 1] - inLayout[3 * t], inLayout[3 * t + 2] - inLayout[3 * t]));
                double sumArea = 0;
                for (int d0 = 0; d0 < nd; ++d0) {
                    if (used[d0]) continue;
                    var cyc = new List<int>();
                    int dd = d0, guard = 0;
                    while (used[dd] == false && guard++ < nd + 1) {
                        used[dd] = true;
                        cyc.Add(dd);
                        int rev;
                        if (twinOf.TryGetValue((long)dTo[dd] * P + dFrom[dd], out rev) == false) return fail("arrangement error in input triangle " + inTid[t]);
                        var l = darts[dTo[dd]];
                        dd = l[(rank[rev] - 1 + l.Count) % l.Count];       // next clockwise after the reverse
                    }
                    double area = 0;
                    var poly = new Vector2d[cyc.Count];
                    for (int m = 0; m < cyc.Count; ++m) poly[m] = pos[dFrom[cyc[m]]];
                    for (int m = 0; m < poly.Length; ++m) area += 0.5 * cross(poly[m], poly[(m + 1) % poly.Length]);
                    if (area <= 0) { outer++; continue; }
                    int fid = faceTri.Count;
                    int ft = -1;
                    foreach (int x in cyc) {
                        Label? lab = dLabel[x];
                        if (dPiece[x].Item1 >= 0) {
                            Label el;
                            if (edgeLabels.TryGetValue(dPiece[x], out el)) {
                                // label relative to the canonical direction, the dart runs along it or not
                                lab = new Label() { H = el.H, Same = (el.Same == dFwd[x]) };
                            } else {
                                List<int> pf;
                                if (pieceFaces.TryGetValue(dPiece[x], out pf) == false) pieceFaces[dPiece[x]] = pf = new List<int>();
                                pf.Add(fid);
                            }
                        }
                        if (lab.HasValue) {
                            int it = lab.Value.Same ? lab.Value.H / 3 : (hTwin[lab.Value.H] >= 0 ? hTwin[lab.Value.H] / 3 : -2);
                            if (it == -2) return fail("face of input triangle " + inTid[t] + " outside a boundary edge");
                            if (ft >= 0 && ft != it) return fail("face of input triangle " + inTid[t] + " between different intrinsic triangles (crossing traces)");
                            ft = it;
                        }
                    }
                    faceTri.Add(ft);
                    faceArea.Add(area);
                    sumArea += area;
                    var ids = new int[cyc.Count];
                    for (int m = 0; m < cyc.Count; ++m) ids[m] = dFrom[cyc[m]];
                    faceCorners.Add(ids);
                    faceInput.Add(t);
                    facePos.Add(poly);
                }
                if (outer != 1) return fail("input triangle " + inTid[t] + " is cut into " + outer + " outer cycles (crossing traces)");
                double err = Math.Abs(sumArea - triArea) / Math.Max(triArea, 1e-300);
                R.MaxInputAreaError = Math.Max(R.MaxInputAreaError, err);
                if (err > 1e-6) return fail("faces of input triangle " + inTid[t] + " do not sum to its area (crossing traces)");
            }
            // faces without an intrinsic edge on their border: across unlabelled input edge pieces the
            // intrinsic triangle does not change
            bool changedAny = true;
            while (changedAny) {
                changedAny = false;
                foreach (var pf in pieceFaces.Values) {
                    int known = -1;
                    foreach (int f in pf) if (faceTri[f] >= 0) known = faceTri[f];
                    if (known < 0) continue;
                    foreach (int f in pf)
                        if (faceTri[f] < 0) { faceTri[f] = known; changedAny = true; }
                        else if (faceTri[f] != known) return fail("faces beside an input edge in different intrinsic triangles");
                }
            }
            double[] itArea = new double[F];
            for (int f = 0; f < faceTri.Count; ++f) {
                if (faceTri[f] < 0) return fail("face in input triangle " + inTid[faceInput[f]] + " without intrinsic triangle");
                itArea[faceTri[f]] += faceArea[f];
                var face = new IntrinsicCommonSubdivision.Face() {
                    InputTriangle = inTid[faceInput[f]], IntrinsicTriangle = faceTri[f], Corners = faceCorners[f], Area = faceArea[f],
                    Barycentric = new Vector3d[faceCorners[f].Length]
                };
                for (int m = 0; m < face.Barycentric.Length; ++m) face.Barycentric[m] = barycentric(faceInput[f], facePos[f][m]);
                R.Faces.Add(face);
            }
            for (int it = 0; it < F; ++it) {
                double a = TriangleArea(it);
                double err = Math.Abs(itArea[it] - a) / Math.Max(a, 1e-300);
                R.MaxIntrinsicAreaError = Math.Max(R.MaxIntrinsicAreaError, err);
            }
            if (R.MaxIntrinsicAreaError > 1e-6) return fail("faces do not sum to the intrinsic triangle areas (relative error " + R.MaxIntrinsicAreaError + ")");
            return R;
        }

        static double cross(Vector2d a, Vector2d b) { return a.x * b.y - a.y * b.x; }
    }
}
