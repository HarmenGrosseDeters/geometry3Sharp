// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace g3
{
    /// <summary> One run of <see cref="ContourLoftStack"/> (docs/design/CONTOUR_LOFT.md section 10). </summary>
    internal sealed class ContourLoftStackRun
    {
        sealed class FailException : Exception
        {
            public readonly ContourLoftStackStatus Status;
            public FailException(ContourLoftStackStatus status, string reason) : base(reason) { Status = status; }
        }

        sealed class Contour
        {
            public int Slice, Index;            // slice, index in the input slice
            public Vector3d[] P3;               // the input positions, in the internal (counter-clockwise) order
            public int[] Ids;                   // mesh vertex of each internal position
            public Vector2d[] P2;               // the working-frame 2D positions
            public int Parent = -2;             // global contour id, -1: top level, -2: not known yet
            public int Depth;
            public List<int> Children = new List<int>();
            public bool TermUp, TermDown;       // no counterpart in the next / previous slice (or the stack ends)
            public bool FreeUp, FreeDown;       // the stack ends there
        }

        // a closed loop of an intermediate or a composite contour
        sealed class Loop
        {
            public Vector2d[] P2; public Vector3d[] P3; public int[] Ids;
            public int Count => Ids.Length;
        }

        readonly ContourLoftStack o;
        readonly IList<IList<IList<Vector3d>>> input;
        readonly CancellationToken ct;
        readonly List<ContourLoftGroupInfo> groups = new List<ContourLoftGroupInfo>();

        readonly List<Contour> contours = new List<Contour>();
        readonly List<List<int>> sliceContours = new List<List<int>>();
        readonly List<Vector3d> vertexPos = new List<Vector3d>();     // mesh vertices: input first, then intermediate, then cap points
        readonly List<Index3i> tris = new List<Index3i>();

        int e;                          // working frame: input scaled by 2^-e
        Vector3d fu, fv, fn;            // the frame (unit, orthogonal), fn = the stacking direction
        double cx, cy;                  // 2D centre
        double[] sliceHeight;           // per slice: mean height along fn (working frame)
        bool anyCap;

        public ContourLoftStackRun(ContourLoftStack options, IList<IList<IList<Vector3d>>> input, CancellationToken ct)
        {
            o = options; this.input = input; this.ct = ct;
        }

        ContourLoftStackResult Failed(ContourLoftStackStatus s, string why) =>
            new ContourLoftStackResult { Status = s, Reason = why, Mesh = null, Groups = groups.ToArray() };

        public ContourLoftStackResult Execute()
        {
            try {
                return Run();
            } catch (FailException f) {
                return Failed(f.Status, f.Message);
            }
        }

        static FailException Fail(ContourLoftStackStatus s, string why) => new FailException(s, why);

        // ---------------------------------------------------------------- frame and input

        ContourLoftStackResult Run()
        {
            int N = input.Count;
            if (N < 2) throw Fail(ContourLoftStackStatus.InvalidInput, "a stack needs at least two slices");
            ReadInput();
            BuildFrame();
            for (int s = 0; s < N; ++s) AnalyseSlice(s);
            for (int s = 0; s + 1 < N; ++s) { ct.ThrowIfCancellationRequested(); Slab(s); }
            MarkEnds();
            CheckTerminals();
            Caps();
            return Finish();
        }

        void ReadInput()
        {
            int id = 0;
            for (int s = 0; s < input.Count; ++s) {
                var list = new List<int>();
                sliceContours.Add(list);
                for (int c = 0; c < input[s].Count; ++c) {
                    var pts = input[s][c];
                    if (pts.Count < 3) throw Fail(ContourLoftStackStatus.InvalidInput, "contour " + c + " of slice " + s + " has fewer than 3 vertices");
                    for (int i = 0; i < pts.Count; ++i) {
                        if (!pts[i].IsFinite) throw Fail(ContourLoftStackStatus.InvalidInput, "contour " + c + " of slice " + s + " has a vertex that is not finite (" + i + ")");
                        if (pts[i] == pts[(i + 1) % pts.Count]) throw Fail(ContourLoftStackStatus.InvalidInput, "contour " + c + " of slice " + s + " repeats a vertex (" + i + ")");
                    }
                    var k = new Contour { Slice = s, Index = c, Ids = new int[pts.Count], P3 = new Vector3d[pts.Count] };
                    for (int i = 0; i < pts.Count; ++i) { vertexPos.Add(pts[i]); k.Ids[i] = id++; k.P3[i] = pts[i]; }
                    list.Add(contours.Count);
                    contours.Add(k);
                }
            }
        }

        static int MaxExponent(IEnumerable<Vector3d> pts)
        {
            double big = 0;
            foreach (var p in pts) big = Math.Max(big, Math.Max(Math.Abs(p.x), Math.Max(Math.Abs(p.y), Math.Abs(p.z))));
            if (big == 0) return 0;
            int shift = 0;
            if (big < 2.2250738585072014E-308) { big *= 18446744073709551616.0; shift = 64; }
            long bits = BitConverter.DoubleToInt64Bits(big);
            return (int)((bits >> 52) & 0x7FF) - 1023 - shift;
        }

        Vector3d Work(Vector3d p) => new Vector3d(ExactPoint3.ScaleB(p.x, -e), ExactPoint3.ScaleB(p.y, -e), ExactPoint3.ScaleB(p.z, -e));
        Vector3d Unwork(Vector3d p) => new Vector3d(ExactPoint3.ScaleB(p.x, e), ExactPoint3.ScaleB(p.y, e), ExactPoint3.ScaleB(p.z, e));
        Vector3d Lift(Vector2d p, double h) => Unwork(fu * (p.x + cx) + fv * (p.y + cy) + fn * h);

        void BuildFrame()
        {
            e = MaxExponent(vertexPos);
            Contour first = contours.FirstOrDefault();
            if (first == null) throw Fail(ContourLoftStackStatus.InvalidInput, "the stack has no contour");
            // the stacking normal: the Newell normal of the first contour, signed so that the slices' mean heights increase
            var w0 = first.P3.Select(Work).ToArray();
            Vector3d cen = Vector3d.Zero;
            foreach (var p in w0) cen += p;
            cen /= w0.Length;
            Vector3d nw = Vector3d.Zero;
            for (int i = 0; i < w0.Length; ++i) nw += (w0[i] - cen).Cross(w0[(i + 1) % w0.Length] - cen);
            if (nw == Vector3d.Zero || !nw.IsFinite) throw Fail(ContourLoftStackStatus.InvalidInput, "the first contour has zero area");
            Vector3d n0 = nw / nw.Length;
            sliceHeight = new double[input.Count];
            var nonEmpty = new List<int>();
            for (int s = 0; s < input.Count; ++s) if (sliceContours[s].Count > 0) nonEmpty.Add(s);
            double Mean(int s, Vector3d n)
            {
                double sum = 0; int cnt = 0;
                foreach (int c in sliceContours[s]) foreach (var p in contours[c].P3) { sum += Work(p).Dot(n); cnt++; }
                return sum / cnt;
            }
            if (nonEmpty.Count > 1 && Mean(nonEmpty[0], n0) > Mean(nonEmpty[nonEmpty.Count - 1], n0)) n0 = -n0;
            fn = n0;
            Vector3d axis = Math.Abs(fn.x) <= Math.Abs(fn.y) && Math.Abs(fn.x) <= Math.Abs(fn.z) ? Vector3d.AxisX : Math.Abs(fn.y) <= Math.Abs(fn.z) ? Vector3d.AxisY : Vector3d.AxisZ;
            Vector3d u = fn.Cross(axis);
            fu = u / u.Length;
            fv = fn.Cross(fu);
            double prev = double.NegativeInfinity;
            foreach (int s in nonEmpty) {
                sliceHeight[s] = Mean(s, fn);
                if (!(sliceHeight[s] > prev)) throw Fail(ContourLoftStackStatus.InvalidInput, "the slice planes are not increasing along the stacking direction (slice " + s + ")");
                prev = sliceHeight[s];
            }
            // 2D coordinates around the centre of their box
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var k in contours)
                foreach (var p in k.P3) {
                    Vector3d w = Work(p);
                    double x = w.Dot(fu), y = w.Dot(fv);
                    x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                }
            cx = 0.5 * (x0 + x1); cy = 0.5 * (y0 + y1);
            // orientation: every contour counter-clockwise in (fu, fv)
            foreach (var k in contours) {
                int m = k.P3.Length;
                var p2 = new Vector2d[m];
                for (int i = 0; i < m; ++i) { Vector3d w = Work(k.P3[i]); p2[i] = new Vector2d(w.Dot(fu) - cx, w.Dot(fv) - cy); }
                double area = 0;
                for (int i = 0; i < m; ++i) { var a = p2[i]; var b = p2[(i + 1) % m]; area += a.x * b.y - b.x * a.y; }
                if (area == 0 || double.IsNaN(area)) throw Fail(ContourLoftStackStatus.InvalidInput, "contour " + k.Index + " of slice " + k.Slice + " has zero area");
                if (area < 0) {
                    Array.Reverse(p2); Array.Reverse(k.P3); Array.Reverse(k.Ids);
                }
                k.P2 = p2;
            }
        }

        // ---------------------------------------------------------------- nesting of a slice

        // the triangulation of a set of contours (no crossings allowed), with each segment marked by its position in the list + 1
        ConstrainedMesh2 Triangulate(List<Contour> list, bool resolveCrossings, out int[] contourBase)
        {
            var inp = new ConstrainedMesh2Input();
            contourBase = new int[list.Count + 1];
            for (int c = 0; c < list.Count; ++c) {
                contourBase[c] = inp.Points.Count;
                var k = list[c];
                for (int i = 0; i < k.P2.Length; ++i) inp.Points.Add(k.P2[i]);
            }
            contourBase[list.Count] = inp.Points.Count;
            int seg = 1;
            segmentOwner.Clear();
            for (int c = 0; c < list.Count; ++c) {
                int m = list[c].P2.Length;
                for (int i = 0; i < m; ++i) {
                    inp.Segments.Add(new Mesh2Segment(contourBase[c] + i, contourBase[c] + (i + 1) % m, seg++));
                    segmentOwner.Add(c);
                }
            }
            ConstrainedTriangulation2Result r;
            try {
                r = new ConstrainedDelaunayMesh2 { ResolveCrossings = resolveCrossings }.Compute(inp, ct);
            } catch (ArgumentException ex) {
                throw Fail(ContourLoftStackStatus.InvalidInput, "the triangulation rejected the contours: " + ex.Message);
            }
            if (r.Status != Triangulation2Status.Success) {
                if (r.FailureKind == Triangulation2FailureKind.CrossingConstraints)
                    throw Fail(ContourLoftStackStatus.CrossingContours, "two contours of a slice cross");
                throw Fail(ContourLoftStackStatus.InvalidInput, "the contours could not be triangulated: " + r.Status + " " + r.FailureKind + " " + r.FailureDetail);
            }
            return r.Mesh;
        }
        readonly List<int> segmentOwner = new List<int>();     // per segment marker - 1: the contour (position in the triangulated list)

        static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        void AnalyseSlice(int s)
        {
            var ids = sliceContours[s];
            if (ids.Count == 0) return;
            var list = ids.Select(i => contours[i]).ToList();
            ConstrainedMesh2 mesh = Triangulate(list, false, out int[] baseIdx);
            int totalEdges = list.Sum(k => k.P2.Length);
            if (mesh.Subsegments.Count != totalEdges) throw Fail(ContourLoftStackStatus.CrossingContours, "contours of slice " + s + " touch (a vertex lies on another contour's edge)");
            for (int v = 0; v < mesh.InputCount; ++v)
                if (mesh.InputToVertex[v] != v) throw Fail(ContourLoftStackStatus.CrossingContours, "contours of slice " + s + " touch (two vertices coincide)");
            var constrained = new Dictionary<long, int>();       // edge -> contour (position in list)
            foreach (var sub in mesh.Subsegments) constrained[EdgeKey(sub.A, sub.B)] = segmentOwner[sub.Marker - 1];

            int T = mesh.TriangleCount;
            var inner = new int[T];
            var seen = new bool[T];
            int start = -1, startEdgeOwner = -1;
            for (int t = 0; t < T && start < 0; ++t)
                for (int k = 0; k < 3; ++k)
                    if (mesh.Neighbours[t][k] < 0) {
                        start = t;
                        var tr = mesh.Triangles[t];
                        if (constrained.TryGetValue(EdgeKey(tr[(k + 1) % 3], tr[(k + 2) % 3]), out int own)) startEdgeOwner = own;
                        break;
                    }
            if (start < 0) throw Fail(ContourLoftStackStatus.InvalidInput, "slice " + s + " has no triangulated hull");
            var parent = new int[list.Count];
            for (int i = 0; i < parent.Length; ++i) parent[i] = -2;
            if (startEdgeOwner >= 0) parent[startEdgeOwner] = -1;
            inner[start] = startEdgeOwner; seen[start] = true;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0) {
                int t = queue.Dequeue();
                var tr = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k) {
                    int nb = mesh.Neighbours[t][k];
                    if (nb < 0) continue;
                    int cur = inner[t], next = cur;
                    if (constrained.TryGetValue(EdgeKey(tr[(k + 1) % 3], tr[(k + 2) % 3]), out int c)) {
                        if (cur == c) {
                            if (parent[c] == -2) throw Fail(ContourLoftStackStatus.InvalidInput, "inconsistent nesting in slice " + s);
                            next = parent[c];
                        } else {
                            if (parent[c] == -2) parent[c] = cur;
                            else if (parent[c] != cur) throw Fail(ContourLoftStackStatus.InvalidInput, "inconsistent nesting in slice " + s);
                            next = c;
                        }
                    }
                    if (seen[nb]) {
                        if (inner[nb] != next) throw Fail(ContourLoftStackStatus.InvalidInput, "inconsistent nesting in slice " + s + " (a contour is not closed or crosses)");
                    } else { seen[nb] = true; inner[nb] = next; queue.Enqueue(nb); }
                }
            }
            for (int c = 0; c < list.Count; ++c) {
                if (parent[c] == -2) throw Fail(ContourLoftStackStatus.InvalidInput, "contour " + list[c].Index + " of slice " + s + " is not reached");
                list[c].Parent = parent[c] < 0 ? -1 : ids[parent[c]];
            }
            foreach (int i in ids) {
                var k = contours[i];
                k.Depth = 0;
                for (int p = k.Parent; p >= 0; p = contours[p].Parent) k.Depth++;
                if (k.Parent >= 0) contours[k.Parent].Children.Add(i);
            }
        }

        // ---------------------------------------------------------------- a slab: overlay, groups, bands

        sealed class Uf
        {
            readonly int[] p;
            public Uf(int n) { p = new int[n]; for (int i = 0; i < n; ++i) p[i] = i; }
            public int Find(int x) { while (p[x] != x) { p[x] = p[p[x]]; x = p[x]; } return x; }
            public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) p[Math.Max(a, b)] = Math.Min(a, b); }
        }

        sealed class Lobe
        {
            public int Lower, Upper;            // global contour ids
            public int[] Loop;                  // overlay vertex ids, counter-clockwise, canonical start
            public Loop Inter;                  // the intermediate contour (general case)
            public int[] StartKey;
        }

        void Slab(int s)
        {
            var lo = sliceContours[s]; var hi = sliceContours[s + 1];
            if (lo.Count == 0 || hi.Count == 0) {
                foreach (int c in lo) { contours[c].TermUp = true; AddGroup(s, ContourLoftGroupKind.Vanishing, new[] { contours[c].Index }, new int[0]); }
                foreach (int c in hi) { contours[c].TermDown = true; AddGroup(s, ContourLoftGroupKind.Appearing, new int[0], new[] { contours[c].Index }); }
                return;
            }
            var list = new List<Contour>();
            foreach (int c in lo) list.Add(contours[c]);
            foreach (int c in hi) list.Add(contours[c]);
            ConstrainedMesh2 mesh = Triangulate(list, true, out int[] baseIdx);
            int nlo = lo.Count;
            Func<int, int> globalOf = pos => pos < nlo ? lo[pos] : hi[pos - nlo];
            // segment marker -> position in list
            var constrained = new Dictionary<long, int[]>();
            foreach (var sub in mesh.Subsegments) constrained[EdgeKey(sub.A, sub.B)] = sub.Markers;

            int T = mesh.TriangleCount;
            var innerLo = new int[T]; var innerHi = new int[T];
            var seen = new bool[T];
            int start = -1; int[] startMarkers = null;
            for (int t = 0; t < T && start < 0; ++t)
                for (int k = 0; k < 3; ++k)
                    if (mesh.Neighbours[t][k] < 0) {
                        start = t;
                        var tr = mesh.Triangles[t];
                        constrained.TryGetValue(EdgeKey(tr[(k + 1) % 3], tr[(k + 2) % 3]), out startMarkers);
                        break;
                    }
            if (start < 0) throw Fail(ContourLoftStackStatus.InvalidInput, "slab " + s + " has no triangulated hull");
            innerLo[start] = -1; innerHi[start] = -1;
            if (startMarkers != null)
                foreach (int mk in startMarkers) { int pos = segmentOwner[mk - 1]; if (pos < nlo) innerLo[start] = lo[pos]; else innerHi[start] = hi[pos - nlo]; }
            seen[start] = true;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0) {
                int t = queue.Dequeue();
                var tr = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k) {
                    int nb = mesh.Neighbours[t][k];
                    if (nb < 0) continue;
                    int a = innerLo[t], b = innerHi[t];
                    if (constrained.TryGetValue(EdgeKey(tr[(k + 1) % 3], tr[(k + 2) % 3]), out int[] marks))
                        foreach (int mk in marks) {
                            int g = globalOf(segmentOwner[mk - 1]);
                            if (segmentOwner[mk - 1] < nlo) a = a == g ? contours[g].Parent : g;
                            else b = b == g ? contours[g].Parent : g;
                        }
                    if (seen[nb]) {
                        if (innerLo[nb] != a || innerHi[nb] != b) throw Fail(ContourLoftStackStatus.InvalidInput, "inconsistent overlay in slab " + s);
                    } else { seen[nb] = true; innerLo[nb] = a; innerHi[nb] = b; queue.Enqueue(nb); }
                }
            }

            // related pairs: equal depth parity, a triangle labelled with both
            var pairTriangles = new SortedDictionary<long, List<int>>();
            for (int t = 0; t < T; ++t) {
                int a = innerLo[t], b = innerHi[t];
                if (a < 0 || b < 0) continue;
                if ((contours[a].Depth & 1) != (contours[b].Depth & 1)) continue;
                long key = ((long)a << 32) | (uint)b;
                if (!pairTriangles.TryGetValue(key, out var l)) pairTriangles.Add(key, l = new List<int>());
                l.Add(t);
            }
            var idx = new Dictionary<int, int>();
            for (int i = 0; i < lo.Count; ++i) idx[lo[i]] = i;
            for (int i = 0; i < hi.Count; ++i) idx[hi[i]] = lo.Count + i;
            var uf = new Uf(lo.Count + hi.Count);
            var related = new bool[lo.Count + hi.Count];
            foreach (var kv in pairTriangles) {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                uf.Union(idx[a], idx[b]); related[idx[a]] = true; related[idx[b]] = true;
            }
            // the groups in a fixed order: by their smallest member (lower slice first)
            var members = new SortedDictionary<int, List<int>>();
            for (int i = 0; i < related.Length; ++i) {
                int g = globalOf(i);
                if (!related[i]) {
                    if (i < nlo) { contours[g].TermUp = true; AddGroup(s, ContourLoftGroupKind.Vanishing, new[] { contours[g].Index }, new int[0]); }
                    else { contours[g].TermDown = true; AddGroup(s, ContourLoftGroupKind.Appearing, new int[0], new[] { contours[g].Index }); }
                    continue;
                }
                int root = uf.Find(i);
                if (!members.TryGetValue(root, out var l)) members.Add(root, l = new List<int>());
                l.Add(g);
            }
            var general = new List<(List<int> lower, List<int> upper)>();
            foreach (var kv in members) {
                var lower = kv.Value.Where(c => contours[c].Slice == s).ToList();
                var upper = kv.Value.Where(c => contours[c].Slice == s + 1).ToList();
                ContourLoftGroupKind kind = lower.Count == 1 && upper.Count == 1 ? ContourLoftGroupKind.Band
                    : lower.Count == 1 ? ContourLoftGroupKind.Branch : upper.Count == 1 ? ContourLoftGroupKind.Merge : ContourLoftGroupKind.Complex;
                AddGroup(s, kind, lower.Select(c => contours[c].Index).ToArray(), upper.Select(c => contours[c].Index).ToArray());
                if (kind == ContourLoftGroupKind.Band) {
                    Band(contours[lower[0]].P3, contours[lower[0]].Ids, contours[upper[0]].P3, contours[upper[0]].Ids, contours[lower[0]].Depth);
                } else {
                    foreach (int c in lower.Concat(upper))
                        if (contours[c].Children.Count > 0)
                            throw Fail(ContourLoftStackStatus.Unsupported, "a contour of a branching or merging group has holes (contour " + contours[c].Index + " of slice " + contours[c].Slice + ")");
                    general.Add((lower, upper));
                }
            }
            if (general.Count == 0) return;
            Overlay ov = new Overlay(mesh, innerLo, innerHi, constrained);
            for (int i = 0; i < list.Count; ++i) ov.Base[globalOf(i)] = baseIdx[i];
            foreach (var (lower, upper) in general) GeneralGroup(s, ov, lower, upper);
        }

        void AddGroup(int slab, ContourLoftGroupKind kind, int[] lower, int[] upper) =>
            groups.Add(new ContourLoftGroupInfo { Slab = slab, Kind = kind, Lower = lower, Upper = upper });

        sealed class Overlay
        {
            public readonly ConstrainedMesh2 Mesh; public readonly int[] InnerLo, InnerHi; public readonly Dictionary<long, int[]> Constrained;
            public readonly Dictionary<int, int[]> CrossMarkers = new Dictionary<int, int[]>();
            public readonly Dictionary<int, int> Base = new Dictionary<int, int>();
            public Overlay(ConstrainedMesh2 m, int[] lo, int[] hi, Dictionary<long, int[]> c)
            {
                Mesh = m; InnerLo = lo; InnerHi = hi; Constrained = c;
                var sets = new Dictionary<int, SortedSet<int>>();
                foreach (var sub in m.Subsegments) {
                    foreach (int v in new[] { sub.A, sub.B }) {
                        if (v < m.InputCount) continue;
                        if (!sets.TryGetValue(v, out var set)) sets.Add(v, set = new SortedSet<int>());
                        foreach (int mk in sub.Markers) set.Add(mk);
                    }
                }
                foreach (var kv in sets) CrossMarkers[kv.Key] = kv.Value.ToArray();
            }
            // canonical key of an overlay vertex: an input point by its index, a crossing by the markers of its segments
            public int[] Key(int v)
            {
                if (v < Mesh.InputCount) return new[] { 0, v };
                var m = CrossMarkers[v];
                var k = new int[m.Length + 1]; k[0] = 1; Array.Copy(m, 0, k, 1, m.Length);
                return k;
            }
        }

        static int CompareKeys(int[] a, int[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); ++i) if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return a.Length.CompareTo(b.Length);
        }

        // ---------------------------------------------------------------- bands

        void Band(IList<Vector3d> first, int[] idsFirst, IList<Vector3d> second, int[] idsSecond, int depth)
        {
            var loft = new ContourLoft {
                Cost = o.Cost, CustomCost = o.CustomCost, IsAdmissible = o.IsAdmissible, NormalizeForCost = o.NormalizeForCost,
                CheckSelfIntersections = false, MaxCells = o.MaxCells
            };
            ContourLoftBandTriangles b = loft.ComputeBand(first, second, ct);
            switch (b.Status) {
                case ContourLoftStatus.Success: break;
                case ContourLoftStatus.NoAdmissibleBand: throw Fail(ContourLoftStackStatus.NoAdmissibleBand, b.Reason);
                case ContourLoftStatus.InvalidCost: throw Fail(ContourLoftStackStatus.InvalidCost, b.Reason);
                case ContourLoftStatus.TooLarge: throw Fail(ContourLoftStackStatus.TooLarge, b.Reason);
                default: throw Fail(ContourLoftStackStatus.BandFailed, "a band failed: " + b.Status + ": " + b.Reason);
            }
            int m = first.Count;
            foreach (var t in b.Triangles) {
                int a = t.a < m ? idsFirst[t.a] : idsSecond[t.a - m];
                int bb = t.b < m ? idsFirst[t.b] : idsSecond[t.b - m];
                int c = t.c < m ? idsFirst[t.c] : idsSecond[t.c - m];
                // stage 1 faces out of the prism between the contours; the surface of a hole (odd depth) is the same prism seen from inside
                tris.Add((depth & 1) == 0 ? new Index3i(a, bb, c) : new Index3i(a, c, bb));
            }
        }

        // ---------------------------------------------------------------- branching and merging

        // the bridges placed so far in a plane (a slice, or the intermediate plane of a slab as -1 - slab): later bridges keep clear of them
        readonly Dictionary<int, List<(Vector2d, Vector2d)>> planeBridges = new Dictionary<int, List<(Vector2d, Vector2d)>>();

        void GeneralGroup(int s, Overlay ov, List<int> lower, List<int> upper)
        {
            var mesh = ov.Mesh;
            var lowerSet = new HashSet<int>(lower); var upperSet = new HashSet<int>(upper);
            // the triangles of every (lower, upper) label, split into connected pieces
            var labelled = new SortedDictionary<long, List<int>>();
            for (int t = 0; t < mesh.TriangleCount; ++t) {
                int a = ov.InnerLo[t], b = ov.InnerHi[t];
                if (a < 0 || b < 0 || !lowerSet.Contains(a) || !upperSet.Contains(b)) continue;
                long key = ((long)a << 32) | (uint)b;
                if (!labelled.TryGetValue(key, out var l)) labelled.Add(key, l = new List<int>());
                l.Add(t);
            }
            var lobes = new List<Lobe>();
            var visited = new HashSet<int>();
            foreach (var kv in labelled) {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                foreach (int t0 in kv.Value) {
                    if (visited.Contains(t0)) continue;
                    var comp = new List<int>();
                    var q = new Queue<int>(); q.Enqueue(t0); visited.Add(t0);
                    while (q.Count > 0) {
                        int t = q.Dequeue(); comp.Add(t);
                        for (int k = 0; k < 3; ++k) {
                            int nb = mesh.Neighbours[t][k];
                            if (nb >= 0 && ov.InnerLo[nb] == a && ov.InnerHi[nb] == b && visited.Add(nb)) q.Enqueue(nb);
                        }
                    }
                    lobes.Add(TraceLobe(ov, comp, a, b));
                }
            }
            lobes.Sort((x, y) => x.Lower != y.Lower ? x.Lower.CompareTo(y.Lower) : x.Upper != y.Upper ? x.Upper.CompareTo(y.Upper) : CompareKeys(x.StartKey, y.StartKey));

            // does every piece equal its lower contour (merge of contained contours) or its upper contour (branch into contained ones)?
            bool bottomOnly = lobes.All(l => SameLoop(ov, l, contours[l.Lower])), topOnly = lobes.All(l => SameLoop(ov, l, contours[l.Upper]));
            int nlo = sliceContours[s].Count;
            int depth = contours[lower[0]].Depth;
            double hLo = sliceHeight[s], hHi = sliceHeight[s + 1];
            if (bottomOnly || topOnly) {
                // the pieces are the contours of one side themselves: no intermediate plane
                if (bottomOnly) {
                    foreach (int d in upper) {
                        var set = lobes.Where(l => l.Upper == d).Select(l => AsLoop(contours[l.Lower])).ToList();
                        Loop comp = Compose(set, contours[d].P2, SliceEdges(s), s, "merge");
                        Band(comp.P3, comp.Ids, contours[d].P3, contours[d].Ids, depth);
                    }
                } else {
                    foreach (int c in lower) {
                        var set = lobes.Where(l => l.Lower == c).Select(l => AsLoop(contours[l.Upper])).ToList();
                        Loop comp = Compose(set, contours[c].P2, SliceEdges(s + 1), s + 1, "branch");
                        Band(contours[c].P3, contours[c].Ids, comp.P3, comp.Ids, depth);
                    }
                }
                return;
            }
            // general: intermediate contours at the fraction of the slab
            double hs = hLo + o.BranchFraction * (hHi - hLo);
            foreach (var l in lobes) {
                int n = l.Loop.Length;
                var p2 = new Vector2d[n]; var p3 = new Vector3d[n]; var ids = new int[n];
                for (int i = 0; i < n; ++i) {
                    p2[i] = mesh.Vertices[l.Loop[i]];
                    p3[i] = Lift(p2[i], hs);
                    ids[i] = vertexPos.Count; vertexPos.Add(p3[i]);
                }
                l.Inter = new Loop { P2 = p2, P3 = p3, Ids = ids };
            }
            var interEdges = new List<Loop>(lobes.Select(l => l.Inter));
            foreach (int c in lower) {
                var set = lobes.Where(l => l.Lower == c).Select(l => l.Inter).ToList();
                Loop comp = Compose(set, contours[c].P2, interEdges, -1 - s, "branch");
                Band(contours[c].P3, contours[c].Ids, comp.P3, comp.Ids, depth);
            }
            foreach (int d in upper) {
                var set = lobes.Where(l => l.Upper == d).Select(l => l.Inter).ToList();
                Loop comp = Compose(set, contours[d].P2, interEdges, -1 - s, "merge");
                Band(comp.P3, comp.Ids, contours[d].P3, contours[d].Ids, depth);
            }
        }

        static Loop AsLoop(Contour k) => new Loop { P2 = k.P2, P3 = k.P3, Ids = k.Ids };

        List<Loop> SliceEdges(int slice) => sliceContours[slice].Select(c => AsLoop(contours[c])).ToList();

        // The lobe's loop is the contour: the contour's vertices in their cyclic order, plus, between them, vertices of other contours that
        // split its edges (overlay vertices, T-junctions). The lobe lies in the contour's face and all of the contour's vertices are on its boundary, so it is the face.
        bool SameLoop(Overlay ov, Lobe l, Contour k)
        {
            var loop = l.Loop;
            int n = k.P2.Length;
            if (loop.Length < n) return false;
            int baseIdx = ov.Base[GlobalOf(k)];
            var position = new Dictionary<int, int>();
            for (int i = 0; i < n; ++i) position[ov.Mesh.InputToVertex[baseIdx + i]] = i;
            if (position.Count != n) return false;
            int first = -1;
            for (int i = 0; i < loop.Length; ++i) if (position.ContainsKey(loop[i]) && position[loop[i]] == 0) { first = i; break; }
            if (first < 0) return false;
            int expect = 0;
            for (int i = 0; i < loop.Length; ++i) {
                int v = loop[(first + i) % loop.Length];
                if (!position.TryGetValue(v, out int pos)) continue;
                if (pos != expect) return false;
                expect++;
            }
            if (expect != n) return false;
            return true;
        }

        int GlobalOf(Contour k) => sliceContours[k.Slice][k.Index];

        Lobe TraceLobe(Overlay ov, List<int> comp, int a, int b)
        {
            var mesh = ov.Mesh;
            var inSet = new HashSet<int>(comp);
            var next = new Dictionary<int, int>();
            foreach (int t in comp) {
                var tr = mesh.Triangles[t];
                for (int k = 0; k < 3; ++k) {
                    int nb = mesh.Neighbours[t][k];
                    if (nb >= 0 && inSet.Contains(nb)) continue;
                    // edge opposite vertex k, counter-clockwise: tr[k+1] -> tr[k+2]
                    int u = tr[(k + 1) % 3], v = tr[(k + 2) % 3];
                    if (next.ContainsKey(u)) throw Fail(ContourLoftStackStatus.Unsupported, "the overlap of two contours touches itself at a vertex");
                    next.Add(u, v);
                }
            }
            // the canonical start: the smallest key
            int startV = -1; int[] startKey = null;
            foreach (int u in next.Keys) {
                var key = ov.Key(u);
                if (startKey == null || CompareKeys(key, startKey) < 0) { startKey = key; startV = u; }
            }
            var loop = new List<int>();
            int cur = startV;
            do {
                loop.Add(cur);
                if (!next.TryGetValue(cur, out int nx)) throw Fail(ContourLoftStackStatus.Unsupported, "the overlap region of two contours is not a closed curve");
                cur = nx;
            } while (cur != startV && loop.Count <= next.Count);
            if (loop.Count != next.Count) throw Fail(ContourLoftStackStatus.Unsupported, "the overlap of two contours has several boundary curves");
            return new Lobe { Lower = a, Upper = b, Loop = loop.ToArray(), StartKey = startKey };
        }

        // ---------------------------------------------------------------- bridges

        static bool OnSegment(Vector2d p, Vector2d q, Vector2d r) =>
            Math.Min(p.x, q.x) <= r.x && r.x <= Math.Max(p.x, q.x) && Math.Min(p.y, q.y) <= r.y && r.y <= Math.Max(p.y, q.y);

        // do the closed segments pq and ab share a point?
        static bool Touch(Vector2d p, Vector2d q, Vector2d a, Vector2d b)
        {
            int o1 = ExactPredicates.Orient2d(p, q, a), o2 = ExactPredicates.Orient2d(p, q, b);
            int o3 = ExactPredicates.Orient2d(a, b, p), o4 = ExactPredicates.Orient2d(a, b, q);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            if (o1 == 0 && OnSegment(p, q, a)) return true;
            if (o2 == 0 && OnSegment(p, q, b)) return true;
            if (o3 == 0 && OnSegment(a, b, p)) return true;
            if (o4 == 0 && OnSegment(a, b, q)) return true;
            return false;
        }

        // is the point inside the polygon (strictly)? false on the boundary
        static bool Inside(Vector2d[] poly, Vector2d pt)
        {
            int wn = 0, n = poly.Length;
            for (int i = 0; i < n; ++i) {
                Vector2d a = poly[i], b = poly[(i + 1) % n];
                int o = ExactPredicates.Orient2d(a, b, pt);
                if (o == 0 && OnSegment(a, b, pt)) return false;
                if (a.y <= pt.y) { if (b.y > pt.y && o > 0) wn++; }
                else if (b.y <= pt.y && o < 0) wn--;
            }
            return wn != 0;
        }

        // Joins the loops (all counter-clockwise, disjoint, inside the container) into one composite contour by zero-width bridges: Prim's
        // tree of the shortest visible vertex pairs (ties by position). The bridges avoid every edge of 'obstacles' (and each other).
        Loop Compose(List<Loop> loops, Vector2d[] container, List<Loop> obstacles, int planeKey, string what)
        {
            if (loops.Count == 1) return loops[0];
            int r = loops.Count;
            var connected = new List<int> { 0 };
            var done = new bool[r]; done[0] = true;
            var bridges = new List<(int la, int va, int lb, int vb)>();
            if (!planeBridges.TryGetValue(planeKey, out var chosen)) planeBridges.Add(planeKey, chosen = new List<(Vector2d, Vector2d)>());
            while (connected.Count < r) {
                ct.ThrowIfCancellationRequested();
                // candidates: every vertex of a connected loop against every vertex of an unconnected one
                var cand = new List<(double d2, int la, int va, int lb, int vb)>();
                foreach (int la in connected)
                    for (int lb = 0; lb < r; ++lb) {
                        if (done[lb]) continue;
                        for (int va = 0; va < loops[la].Count; ++va)
                            for (int vb = 0; vb < loops[lb].Count; ++vb) {
                                var d = loops[la].P2[va] - loops[lb].P2[vb];
                                cand.Add((d.x * d.x + d.y * d.y, la, va, lb, vb));
                            }
                    }
                cand.Sort((x, y) => {
                    int c = x.d2.CompareTo(y.d2); if (c != 0) return c;
                    c = x.la.CompareTo(y.la); if (c != 0) return c;
                    c = x.lb.CompareTo(y.lb); if (c != 0) return c;
                    c = x.va.CompareTo(y.va); return c != 0 ? c : x.vb.CompareTo(y.vb);
                });
                bool found = false;
                foreach (var cd in cand) {
                    if (Visible(loops, cd.la, cd.va, cd.lb, cd.vb, container, obstacles, chosen)) {
                        bridges.Add((cd.la, cd.va, cd.lb, cd.vb));
                        chosen.Add((loops[cd.la].P2[cd.va], loops[cd.lb].P2[cd.vb]));
                        connected.Add(cd.lb); done[cd.lb] = true;
                        found = true; break;
                    }
                }
                if (!found) throw Fail(ContourLoftStackStatus.BridgeNotFound, "no bridge between the contours of a " + what + " is visible");
            }
            // the composite sequence
            var at = new Dictionary<(int, int), List<(int lb, int vb)>>();
            foreach (var b in bridges) {
                if (!at.TryGetValue((b.la, b.va), out var l)) at.Add((b.la, b.va), l = new List<(int, int)>());
                l.Add((b.lb, b.vb));
            }
            var seq = new List<(int loop, int v)>();
            Emit(0, 0, loops, at, seq);
            return new Loop {
                P2 = seq.Select(x => loops[x.loop].P2[x.v]).ToArray(), P3 = seq.Select(x => loops[x.loop].P3[x.v]).ToArray(),
                Ids = seq.Select(x => loops[x.loop].Ids[x.v]).ToArray()
            };
        }

        static void Emit(int loop, int start, List<Loop> loops, Dictionary<(int, int), List<(int lb, int vb)>> at, List<(int, int)> seq)
        {
            int n = loops[loop].Count;
            for (int k = 0; k < n; ++k) {
                int v = (start + k) % n;
                seq.Add((loop, v));
                if (at.TryGetValue((loop, v), out var l))
                    foreach (var (lb, vb) in l) {
                        Emit(lb, vb, loops, at, seq);
                        seq.Add((lb, vb));
                        seq.Add((loop, v));
                    }
            }
        }

        bool Visible(List<Loop> loops, int la, int va, int lb, int vb, Vector2d[] container, List<Loop> obstacles, List<(Vector2d, Vector2d)> chosen)
        {
            Loop A = loops[la], B = loops[lb];
            Vector2d p = A.P2[va], q = B.P2[vb];
            if (!Inside(container, new Vector2d(0.5 * (p.x + q.x), 0.5 * (p.y + q.y)))) return false;
            foreach (var ob in obstacles)
                for (int i = 0; i < ob.Count; ++i) {
                    Vector2d a = ob.P2[i], b = ob.P2[(i + 1) % ob.Count];
                    if (!SegmentClear(p, q, a, b)) return false;
                }
            foreach (var (a, b) in chosen) if (!SegmentClear(p, q, a, b)) return false;
            return true;
        }

        // pq may meet ab only in a common end point (and not overlap it)
        static bool SegmentClear(Vector2d p, Vector2d q, Vector2d a, Vector2d b)
        {
            bool ap = a == p, aq = a == q, bp = b == p, bq = b == q;
            if ((ap && bq) || (aq && bp)) return false;          // the same segment
            if (ap || aq || bp || bq) {
                // a shared end point: the other end of ab must not lie on pq, and the other end of pq not on ab
                Vector2d far = ap || aq ? b : a;
                Vector2d pFar = ap || bp ? q : p;
                Vector2d shared = ap || bp ? p : q;
                if (ExactPredicates.Orient2d(shared, pFar, far) == 0) {
                    // collinear: overlap if they point the same way
                    double dot = (pFar.x - shared.x) * (far.x - shared.x) + (pFar.y - shared.y) * (far.y - shared.y);
                    if (dot > 0) return false;
                }
                return true;
            }
            return !Touch(p, q, a, b);
        }

        // ---------------------------------------------------------------- terminal contours and caps

        void MarkEnds()
        {
            foreach (int c in sliceContours[0]) { contours[c].TermDown = true; contours[c].FreeDown = true; }
            foreach (int c in sliceContours[input.Count - 1]) { contours[c].TermUp = true; contours[c].FreeUp = true; }
        }

        void CheckTerminals()
        {
            foreach (var k in contours) {
                foreach (int ch in k.Children) {
                    if (k.TermUp && !contours[ch].TermUp || k.TermDown && !contours[ch].TermDown)
                        throw Fail(ContourLoftStackStatus.Unsupported, "a contour that ends has a child that continues (contour " + k.Index + " of slice " + k.Slice + ")");
                }
            }
        }

        void Caps()
        {
            foreach (var k in contours)
                for (int side = 0; side < 2; ++side) {
                    bool up = side == 1;
                    bool term = up ? k.TermUp : k.TermDown, free = up ? k.FreeUp : k.FreeDown;
                    if (!term || free && !o.CapEnds) continue;
                    // a child that ends is part of its parent's cap
                    if (k.Parent >= 0 && (up ? contours[k.Parent].TermUp : contours[k.Parent].TermDown)) continue;
                    AddCap(k, up);
                }
        }

        void AddCap(Contour root, bool up)
        {
            anyCap = true;
            var rings = new List<Contour> { root };
            foreach (int ch in root.Children) rings.Add(contours[ch]);
            var inp = new ConstrainedMesh2Input { Heights = new List<double>() };
            var baseIdx = new int[rings.Count];
            var pos = new List<int>();     // mesh vertex of each input point
            for (int r = 0; r < rings.Count; ++r) {
                baseIdx[r] = inp.Points.Count;
                var k = rings[r];
                for (int i = 0; i < k.P2.Length; ++i) {
                    inp.Points.Add(k.P2[i]);
                    inp.Heights.Add(Work(k.P3[i]).Dot(fn));
                    pos.Add(k.Ids[i]);
                }
            }
            for (int r = 0; r < rings.Count; ++r) {
                int m = rings[r].P2.Length;
                var ring = new int[m];
                for (int i = 0; i < m; ++i) { ring[i] = baseIdx[r] + i; inp.Segments.Add(new Mesh2Segment(baseIdx[r] + i, baseIdx[r] + (i + 1) % m, 0, true)); }
                inp.Rings.Add(new Mesh2Ring(ring, r > 0));
            }
            ConstrainedTriangulation2Result res;
            try {
                res = (o.CapMesher ?? new QualityMesh2 { MinAngle = 0 }).Compute(inp, ct);
            } catch (ArgumentException ex) {
                throw Fail(ContourLoftStackStatus.CapFailed, "the cap mesher rejected a contour: " + ex.Message);
            }
            if (res.Status != Triangulation2Status.Success)
                throw Fail(ContourLoftStackStatus.CapFailed, "the cap mesher failed: " + res.Status + " " + res.FailureKind + " " + res.FailureDetail);
            ConstrainedMesh2 cm = res.Mesh;
            var map = new int[cm.Vertices.Count];
            for (int v = 0; v < map.Length; ++v) map[v] = -1;
            for (int v = 0; v < inp.Points.Count; ++v) {
                int q = cm.InputToVertex[v];
                if (q < 0 || map[q] >= 0 || cm.Vertices[q] != inp.Points[v] || cm.Heights[q] != inp.Heights[v])
                    throw Fail(ContourLoftStackStatus.CapFailed, "the cap moved or merged a contour vertex");
                map[q] = pos[v];
            }
            for (int q = 0; q < map.Length; ++q) {
                if (map[q] >= 0) continue;
                map[q] = vertexPos.Count;
                vertexPos.Add(Lift(cm.Vertices[q], cm.Heights[q]));
            }
            // the cap's normal: up (+fn) for the end of a solid facing up (even depth) or of an air pocket facing down
            bool faceUp = up == ((root.Depth & 1) == 0);
            foreach (var t in cm.Triangles) {
                int a = map[t.a], b = map[t.b], c = map[t.c];
                tris.Add(faceUp ? new Index3i(a, b, c) : new Index3i(a, c, b));
            }
        }

        // ---------------------------------------------------------------- result

        ContourLoftStackResult Finish()
        {
            var mesh = new DMesh3();
            foreach (var p in vertexPos) mesh.AppendVertex(p);
            foreach (var t in tris)
                if (mesh.AppendTriangle(t) < 0)
                    throw Fail(ContourLoftStackStatus.BandFailed, "the pieces do not form a manifold (a triangle does not fit)");
            foreach (var k in contours)
                for (int i = 0; i < k.Ids.Length; ++i)
                    if (mesh.FindEdge(k.Ids[i], k.Ids[(i + 1) % k.Ids.Length]) < 0)
                        throw Fail(ContourLoftStackStatus.BandFailed, "an edge of contour " + k.Index + " of slice " + k.Slice + " is not in the mesh");
            foreach (int v in mesh.VertexIndices())
                if (mesh.IsBowtieVertex(v)) throw Fail(ContourLoftStackStatus.BandFailed, "the mesh has a non-manifold vertex");
            if (gs.MeshRepairMetrics.InconsistentlyOrientedEdges(mesh) > 0) throw Fail(ContourLoftStackStatus.BandFailed, "the mesh is not consistently oriented");
            if (o.CapEnds) {
                string clean = gs.MeshRobustRepair.NotCleanReason(mesh, o.CheckSelfIntersections);
                if (clean == "self-intersections") throw Fail(ContourLoftStackStatus.SelfIntersecting, "the lofted solid intersects itself");
                if (clean != "") throw Fail(anyCap ? ContourLoftStackStatus.CapFailed : ContourLoftStackStatus.BandFailed, "the lofted solid is not clean: " + clean);
                if (SignedVolume(mesh) <= 0) throw Fail(ContourLoftStackStatus.BandFailed, "the lofted solid is oriented inwards (internal error)");
            } else if (o.CheckSelfIntersections && gs.MeshRepairMetrics.HasSelfIntersections(mesh))
                throw Fail(ContourLoftStackStatus.SelfIntersecting, "the lofted surface intersects itself");
            return new ContourLoftStackResult { Status = ContourLoftStackStatus.Success, Reason = "", Mesh = mesh, Groups = groups.ToArray() };
        }

        static double SignedVolume(DMesh3 mesh)
        {
            Vector3d c = Vector3d.Zero; int n = 0;
            foreach (int v in mesh.VertexIndices()) { c += mesh.GetVertex(v); n++; }
            c /= Math.Max(n, 1);
            double vol = 0;
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tr = mesh.GetTriangle(t);
                vol += (mesh.GetVertex(tr.a) - c).Dot((mesh.GetVertex(tr.b) - c).Cross(mesh.GetVertex(tr.c) - c)) / 6;
            }
            return vol;
        }
    }
}
