using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Meshing2Acceptance;

namespace Meshing2HardProblems
{
    /// <summary> What the mesher would hand over with its result: for every subsegment, the input constraints it is a piece of
    /// (ids: the input's segments, then the edges of its rings, in input order). </summary>
    public sealed class Certificate
    {
        public int[][] SubsegmentSources;
    }

    /// <summary>
    /// HP 7: a prototype of the checker of a certifying design, for inputs whose domain is given by rings, without a snap grid.
    /// It trusts nothing of the certificate: every claim in it is tested, and the conclusions (a planar triangulation that covers
    /// the domain, constrained Delaunay) follow from the tests by the arguments of the research note. It references nothing of
    /// the library; the exact fallback of its two predicates is the oracle's. The excuses of the specification (small input
    /// angles, merge zones, O11, the snap grid and Q17) are NOT modelled: the checker counts what would need one. Every pass is
    /// linear in the mesh, except one sort of each source's pieces.
    /// </summary>
    public sealed class Hp7Checker
    {
        public string Reject;
        public readonly List<(string step, double ms)> Laps = new List<(string, double)>();
        public int ExactOrient, ExactInCircle, TubeUndecided, ConformingSubsegmentHits, BelowBound, AngleUndecided;

        readonly Mesh2Input input; readonly Mesh2Output o; readonly Certificate cert; readonly double tube; readonly bool conforming;
        double[] X, Y;

        public Hp7Checker(Mesh2Input input, Mesh2Output o, Certificate cert, double tube, bool conforming)
        {
            this.input = input; this.o = o; this.cert = cert; this.tube = tube; this.conforming = conforming;
        }

        int Orient(int a, int b, int c)
        {
            double l = (X[a] - X[c]) * (Y[b] - Y[c]), r = (Y[a] - Y[c]) * (X[b] - X[c]), det = l - r;
            double err = 3.3306690738754716e-16 * (Math.Abs(l) + Math.Abs(r));   // Shewchuk 1997, the stage A bound of orient2d
            if (err > 1e-280) { if (det > err) return 1; if (det < -err) return -1; }
            ++ExactOrient;
            return Exact.Orient(Pt.FromDoubles(X[a], Y[a]), Pt.FromDoubles(X[b], Y[b]), Pt.FromDoubles(X[c], Y[c]));
        }

        int InCircle(int a, int b, int c, int d)
        {
            double adx = X[a] - X[d], ady = Y[a] - Y[d], bdx = X[b] - X[d], bdy = Y[b] - Y[d], cdx = X[c] - X[d], cdy = Y[c] - Y[d];
            double bdxcdy = bdx * cdy, cdxbdy = cdx * bdy, alift = adx * adx + ady * ady;
            double cdxady = cdx * ady, adxcdy = adx * cdy, blift = bdx * bdx + bdy * bdy;
            double adxbdy = adx * bdy, bdxady = bdx * ady, clift = cdx * cdx + cdy * cdy;
            double det = alift * (bdxcdy - cdxbdy) + blift * (cdxady - adxcdy) + clift * (adxbdy - bdxady);
            double permanent = (Math.Abs(bdxcdy) + Math.Abs(cdxbdy)) * alift + (Math.Abs(cdxady) + Math.Abs(adxcdy)) * blift + (Math.Abs(adxbdy) + Math.Abs(bdxady)) * clift;
            double err = 1.1102230246251577e-15 * permanent;   // Shewchuk 1997, the stage A bound of incircle
            if (err > 1e-250) { if (det > err) return 1; if (det < -err) return -1; }
            ++ExactInCircle;
            return Exact.InCircle(Pt.FromDoubles(X[a], Y[a]), Pt.FromDoubles(X[b], Y[b]), Pt.FromDoubles(X[c], Y[c]), Pt.FromDoubles(X[d], Y[d]));
        }

        public bool Run()
        {
            var watch = Stopwatch.StartNew();
            void Lap(string step) { Laps.Add((step, watch.Elapsed.TotalMilliseconds)); watch.Restart(); }
            bool Fail(string why) { Reject = why; return false; }

            // 0. the data is well formed, and input point i is vertex i (Q8)
            X = o.VX; Y = o.VY;
            int n = X.Length, tn = o.Triangles.Length;
            if (o.Status != Mesh2Status.Success || Y.Length != n) return Fail("status or vertex arrays");
            for (int i = 0; i < n; ++i) if (double.IsNaN(X[i] + Y[i]) || double.IsInfinity(X[i] + Y[i])) return Fail("a vertex is not finite");
            if (o.InputToVertex == null || o.InputToVertex.Length != input.Count) return Fail("no input mapping");
            double tube2 = tube * tube;
            for (int i = 0; i < input.Count; ++i) {
                int v = o.InputToVertex[i];
                if (v < 0 || v >= n) return Fail("input mapping out of range");
                double dx = X[v] - input.X[i], dy = Y[v] - input.Y[i];
                if (dx * dx + dy * dy > tube2) return Fail("input point " + i + " moved beyond the tube");
            }
            foreach (var t in o.Triangles) if (t.Length != 3 || (uint)t[0] >= n || (uint)t[1] >= n || (uint)t[2] >= n) return Fail("a triangle index out of range");
            Lap("0 data");

            // 1. every triangle is positive
            for (int i = 0; i < tn; ++i) { var t = o.Triangles[i]; if (Orient(t[0], t[1], t[2]) <= 0) return Fail("triangle " + i + " is not positive"); }
            Lap("1 orientation");

            // 2. no directed edge twice: the directed edges in buckets by their first vertex (counting sort, no hashing)
            var start = new int[n + 1];
            foreach (var t in o.Triangles) { ++start[t[0] + 1]; ++start[t[1] + 1]; ++start[t[2] + 1]; }
            for (int i = 0; i < n; ++i) start[i + 1] += start[i];
            int en = 3 * tn;
            var to = new int[en]; var tri = new int[en]; var apex = new int[en]; var tag = new int[en]; var fill = (int[])start.Clone();
            for (int i = 0; i < tn; ++i) {
                var t = o.Triangles[i];
                for (int k = 0; k < 3; ++k) { int e = fill[t[k]]++; to[e] = t[(k + 1) % 3]; tri[e] = i; apex[e] = t[(k + 2) % 3]; }
            }
            for (int a = 0; a < n; ++a)
                for (int e = start[a]; e < start[a + 1]; ++e)
                    for (int f = e + 1; f < start[a + 1]; ++f) if (to[e] == to[f]) return Fail("the directed edge " + a + "-" + to[e] + " is in two triangles");
            int Find(int a, int b) { for (int e = start[a]; e < start[a + 1]; ++e) if (to[e] == b) return e; return -1; }
            Lap("2 edges");

            // 3. the certificate: every subsegment is an edge, lies in the tube of each source it names, and the pieces of a source
            //    are one chain from its first end to its last
            var subs = o.Subsegments;
            if (subs == null || cert?.SubsegmentSources == null || cert.SubsegmentSources.Length != subs.Count) return Fail("no subsegments or no certificate");
            var srcA = new List<int>(); var srcB = new List<int>(); var ringSign = new List<int>();
            foreach (var s in input.Segments) { srcA.Add(s.A); srcB.Add(s.B); ringSign.Add(0); }
            foreach (var ring in input.Rings) {
                var rv = ring.Vertices; int m = rv.Length > 1 && rv[0] == rv[rv.Length - 1] ? rv.Length - 1 : rv.Length;
                double area2 = 0;   // (a prototype: the orientation of a ring in doubles; the real checker takes the exact sign)
                for (int k = 0; k < m; ++k) { int u = rv[k], w = rv[(k + 1) % m]; area2 += input.X[u] * input.Y[w] - input.X[w] * input.Y[u]; }
                int sign = (area2 > 0) == !ring.Hole ? 1 : -1;   // +1: the domain is on the left of the ring's edges as given
                for (int k = 0; k < m; ++k) { srcA.Add(rv[k]); srcB.Add(rv[(k + 1) % m]); ringSign.Add(sign); }
            }
            int sn = srcA.Count;
            var count = new int[sn + 1];
            for (int i = 0; i < subs.Count; ++i) foreach (int s in cert.SubsegmentSources[i]) { if ((uint)s >= sn) return Fail("a source id out of range"); ++count[s + 1]; }
            for (int s = 0; s < sn; ++s) count[s + 1] += count[s];
            var pieceT = new double[count[sn]]; var pieceLo = new int[count[sn]]; var pieceHi = new int[count[sn]]; var at = (int[])count.Clone();
            var net = new int[subs.Count];   // rings through the piece in its direction A to B, minus rings against it
            for (int i = 0; i < subs.Count; ++i) {
                int a = subs[i].A, b = subs[i].B;
                if ((uint)a >= n || (uint)b >= n || a == b) return Fail("a subsegment index");
                int e = Find(a, b), f = Find(b, a);
                if (e < 0 && f < 0) return Fail("subsegment " + i + " is no edge of the mesh");
                if (e >= 0) { if (tag[e] != 0) return Fail("a subsegment twice"); tag[e] = i + 1; }
                if (f >= 0) { if (tag[f] != 0) return Fail("a subsegment twice"); tag[f] = -(i + 1); }
                foreach (int s in cert.SubsegmentSources[i]) {
                    double ax = input.X[srcA[s]], ay = input.Y[srcA[s]], dx = input.X[srcB[s]] - ax, dy = input.Y[srcB[s]] - ay, len2 = dx * dx + dy * dy;
                    double ta = ((X[a] - ax) * dx + (Y[a] - ay) * dy) / len2, tb = ((X[b] - ax) * dx + (Y[b] - ay) * dy) / len2;
                    foreach (int v in new[] { a, b }) {
                        double cross = (X[v] - ax) * dy - (Y[v] - ay) * dx, d2 = cross * cross / len2;
                        if (d2 > tube2 * 1.0000001) return Fail("subsegment " + i + " leaves the tube of source " + s);
                        if (d2 > tube2 * 0.9999999) ++TubeUndecided;   // (the real checker decides these exactly)
                    }
                    if (ta == tb) return Fail("a piece without length along its source");
                    int z = at[s]++;
                    pieceT[z] = Math.Min(ta, tb); pieceLo[z] = ta < tb ? a : b; pieceHi[z] = ta < tb ? b : a;
                    if (ringSign[s] != 0) net[i] += (ta < tb ? 1 : -1) * ringSign[s];
                }
            }
            for (int s = 0; s < sn; ++s) {
                int lo = count[s], hi = count[s + 1];
                if (hi == lo) return Fail("source " + s + " has no piece");
                var order = Enumerable.Range(lo, hi - lo).ToArray();
                Array.Sort(order, (p, q) => pieceT[p].CompareTo(pieceT[q]));
                if (pieceLo[order[0]] != o.InputToVertex[srcA[s]] || pieceHi[order[order.Length - 1]] != o.InputToVertex[srcB[s]]) return Fail("the chain of source " + s + " does not join its ends");
                for (int k = 0; k + 1 < order.Length; ++k) if (pieceHi[order[k]] != pieceLo[order[k + 1]]) return Fail("the chain of source " + s + " is broken");
            }
            Lap("3 certificate: chains");

            // 4. the boundary of the mesh is the boundary of the domain: an edge with one triangle is a piece that the rings cross
            //    once with the domain on its left, an edge with two triangles is no boundary of the rings
            for (int a = 0; a < n; ++a)
                for (int e = start[a]; e < start[a + 1]; ++e) {
                    bool twin = Find(to[e], a) >= 0;
                    int ringsAlong = tag[e] > 0 ? net[tag[e] - 1] : tag[e] < 0 ? -net[-tag[e] - 1] : 0;
                    if (!twin && tag[e] == 0) return Fail("the boundary edge " + a + "-" + to[e] + " is no subsegment");
                    if (ringsAlong != (twin ? 0 : 1)) return Fail("the rings do not bound the mesh at the edge " + a + "-" + to[e]);
                }
            Lap("4 boundary");

            // 5. every edge with two triangles that is no subsegment is locally Delaunay (conforming: the subsegments too, counted)
            for (int a = 0; a < n; ++a)
                for (int e = start[a]; e < start[a + 1]; ++e) {
                    int b = to[e];
                    if (a > b || (tag[e] != 0 && !conforming)) continue;
                    int f = Find(b, a);
                    if (f < 0) continue;
                    if (InCircle(a, b, apex[e], apex[f]) > 0) {
                        if (tag[e] != 0) ++ConformingSubsegmentHits;
                        else return Fail("the edge " + a + "-" + b + " is not locally Delaunay");
                    }
                }
            Lap("5 local Delaunay");

            // 6. every triangle below the angle is on the mesher's list (the excuses themselves are not modelled)
            if (input.MinAngleDeg != null) {
                double sin = Math.Sin(input.MinAngleDeg.Value * Math.PI / 180), bound = sin * sin;
                var listed = new bool[tn];
                if (o.BadTriangles != null) foreach (int b in o.BadTriangles) { if ((uint)b >= tn) return Fail("a bad triangle index"); listed[b] = true; }
                for (int i = 0; i < tn; ++i) {
                    var t = o.Triangles[i];
                    double ax = X[t[0]], ay = Y[t[0]], bx = X[t[1]], by = Y[t[1]], cx = X[t[2]], cy = Y[t[2]];
                    double ab = (bx - ax) * (bx - ax) + (by - ay) * (by - ay), bc = (cx - bx) * (cx - bx) + (cy - by) * (cy - by), ca = (ax - cx) * (ax - cx) + (ay - cy) * (ay - cy);
                    double cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax), shortest = Math.Min(ab, Math.Min(bc, ca));
                    // sin^2 of the smallest angle = cross^2 / (product of the two longer edges squared)
                    double sin2 = cross * cross * shortest / (ab * bc * ca);
                    if (sin2 < bound * (1 - 1e-9)) { ++BelowBound; if (!listed[i]) return Fail("triangle " + i + " is below the angle and not listed"); }
                    else if (sin2 < bound * (1 + 1e-9)) ++AngleUndecided;   // (the real checker decides these exactly)
                }
            }
            Lap("6 angles");
            return true;
        }

        /// <summary>
        /// The certificate as the mesher would emit it, derived here from the geometry (idealised: not timed, and the library's own
        /// record, ConstrainedMesh2.SubsegmentSources, is internal). A source is named when both ends of the piece lie within the
        /// tube of its segment.
        /// </summary>
        public static Certificate Derive(Mesh2Input input, Mesh2Output o, double tube)
        {
            var a = new List<int>(); var b = new List<int>();
            foreach (var s in input.Segments) { a.Add(s.A); b.Add(s.B); }
            foreach (var ring in input.Rings) {
                var rv = ring.Vertices; int m = rv.Length > 1 && rv[0] == rv[rv.Length - 1] ? rv.Length - 1 : rv.Length;
                for (int k = 0; k < m; ++k) { a.Add(rv[k]); b.Add(rv[(k + 1) % m]); }
            }
            double x0 = input.X.Min(), y0 = input.Y.Min(), w = Math.Max(input.X.Max() - x0, input.Y.Max() - y0);
            int cells = Math.Max(1, (int)Math.Sqrt(a.Count)); double h = w / cells;
            int Cell(double c, double c0) => h == 0 ? 0 : Math.Max(0, Math.Min(cells - 1, (int)((c - c0) / h)));
            var grid = new List<int>[cells * cells];
            for (int s = 0; s < a.Count; ++s) {
                int i0 = Cell(Math.Min(input.X[a[s]], input.X[b[s]]) - tube, x0), i1 = Cell(Math.Max(input.X[a[s]], input.X[b[s]]) + tube, x0);
                int j0 = Cell(Math.Min(input.Y[a[s]], input.Y[b[s]]) - tube, y0), j1 = Cell(Math.Max(input.Y[a[s]], input.Y[b[s]]) + tube, y0);
                for (int i = i0; i <= i1; ++i) for (int j = j0; j <= j1; ++j) (grid[i * cells + j] ?? (grid[i * cells + j] = new List<int>())).Add(s);
            }
            bool Near(int s, double px, double py)
            {
                double ax = input.X[a[s]], ay = input.Y[a[s]], dx = input.X[b[s]] - ax, dy = input.Y[b[s]] - ay, len2 = dx * dx + dy * dy;
                double t = ((px - ax) * dx + (py - ay) * dy) / len2, cross = (px - ax) * dy - (py - ay) * dx, slack = tube / Math.Sqrt(len2);
                return t >= -slack && t <= 1 + slack && cross * cross / len2 <= tube * tube;
            }
            var result = new int[o.Subsegments.Count][];
            for (int i = 0; i < result.Length; ++i) {
                double px = o.VX[o.Subsegments[i].A], py = o.VY[o.Subsegments[i].A], qx = o.VX[o.Subsegments[i].B], qy = o.VY[o.Subsegments[i].B];
                var list = grid[Cell((px + qx) / 2, x0) * cells + Cell((py + qy) / 2, y0)];
                result[i] = list == null ? new int[0] : list.Where(s => Near(s, px, py) && Near(s, qx, qy)).ToArray();
            }
            return new Certificate { SubsegmentSources = result };
        }
    }
}
