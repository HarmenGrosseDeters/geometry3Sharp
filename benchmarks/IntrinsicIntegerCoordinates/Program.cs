// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// Study: integer (normal) coordinates for intrinsic triangulations (docs/research/INTRINSIC_INTEGER_COORDINATES_2026-11.md).
//
//   dotnet run -c Release --project benchmarks/IntrinsicIntegerCoordinates [-- signpost | integer | cost] [--verbose]
//
// signpost: how often the library's traced common subdivision fails on adversarial procedural meshes (stage 1,
// the upper bound of what integers can gain). integer: the prototype layer (NormalCoordinates.cs) on the same
// triangulations, checked by its own invariants and against the traced result. cost: times.
// The exit code is 1 if a check of the prototype fails.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;

static class Program
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "all";
        if (mode == "signpost" || mode == "all") Signpost(args.Contains("--verbose"));
        if (mode == "integer" || mode == "all") Integer(args.Contains("--verbose"));
        if (mode == "cost" || mode == "all") Cost();
        return failures == 0 ? 0 : 1;
    }

    static string Classify(string error)
    {
        if (error == null) return "ok";
        if (error.Contains("passes through vertex")) return "through vertex";
        if (error.Contains("does not reach its head")) return "trace lost";
        if (error.Contains("leaves input triangle")) return "trace off triangle";
        if (error.Contains("sum to")) return "area";
        if (error.Contains("crossing traces") || error.Contains("different intrinsic")) return "crossing traces";
        if (error.Contains("lies on an input vertex")) return "inserted on vertex";
        return "other";
    }

    // Stage 1 (upper bound): how often does today's traced common subdivision fail?
    static void Signpost(bool verbose)
    {
        Console.WriteLine("| mesh | V | flips | inserted | flips only: failed transforms | refined 25°: failed transforms | reasons |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        int total = 0, failFlip = 0, failRefine = 0, meshFlip = 0, meshRefine = 0, n = 0, okFlip = 0, okRefine = 0, falseFlip = 0, falseRefine = 0;
        var falseValences = new SortedDictionary<int, int>();
        foreach (var c in Meshes.All()) {
            DMesh3 m0 = c.Make();
            int ff = 0, fr = 0, flips = 0, inserted = 0;
            var reasons = new SortedDictionary<string, int>();
            for (int k = 0; k < Meshes.TransformNames.Length; ++k) {
                DMesh3 m = Meshes.Transformed(m0, k);
                var T = new IntrinsicTriangulation(m);
                int f = T.FlipToDelaunay();
                if (k == 0) flips = f;
                var cs = T.ExtractCommonSubdivision();
                if (cs.Status != CommonSubdivisionStatus.Success) { ff++; Count(reasons, "flips: " + Classify(cs.Error)); }
                else { okFlip++; if (OddCrossings(cs.Faces.Select(x => x.Corners), T.VertexCount) > 0) falseFlip++; }
                if (verbose && cs.Error != null) Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] flips ({f} flips): {cs.Error}");
                var R = new IntrinsicTriangulation(m);
                var rr = R.DelaunayRefine(25);
                if (k == 0) inserted = rr.InsertedVertices;
                var cr = R.ExtractCommonSubdivision();
                if (cr.Status != CommonSubdivisionStatus.Success) { fr++; Count(reasons, "refined: " + Classify(cr.Error)); }
                else { okRefine++; if (OddCrossings(cr.Faces.Select(x => x.Corners), R.VertexCount, falseValences) > 0) falseRefine++; }
                if (verbose && cr.Error != null) Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] refined: {cr.Error}");
                total++;
            }
            failFlip += ff; failRefine += fr; n++;
            if (ff > 0) meshFlip++;
            if (fr > 0) meshRefine++;
            Console.WriteLine($"| {c.Name} | {m0.VertexCount} | {flips} | {inserted} | {ff} / 4 | {fr} / 4 | " +
                string.Join(", ", reasons.Select(kv => kv.Key + " x" + kv.Value)) + " |");
        }
        Console.WriteLine();
        Console.WriteLine($"flips only: {failFlip} of {total} runs failed ({meshFlip} of {n} meshes); refined 25°: {failRefine} of {total} runs failed ({meshRefine} of {n} meshes)");
        Console.WriteLine($"Success results with crossing vertices that do not have four edges: flips only {falseFlip} of {okFlip}; refined 25° {falseRefine} of {okRefine}" +
            (falseValences.Count > 0 ? " (valences: " + string.Join(", ", falseValences.Select(kv => kv.Value + " x valence " + kv.Key)) + ")" : ""));
    }

    static int failures;
    static readonly string[] StepNames = { "flips", "flips + insertions", "flips + insertions near edges" };

    // one random point strictly inside a triangle of T (in its layout); with nearEdge every fifth at a barycentric
    // distance of 1e-7 from an edge
    static Vector2d RandomPoint(IntrinsicTriangulation T, Random rng, bool nearEdge, out int t)
    {
        t = rng.Next(T.TriangleCount);
        double u = 0.05 + 0.9 * rng.NextDouble(), v = 0.05 + 0.9 * rng.NextDouble();
        if (u + v > 1) { u = 1 - u; v = 1 - v; }
        double w = 1 - u - v;
        if (nearEdge && rng.Next(5) == 0) { double d = w - 1e-7; w = 1e-7; u += d / 2; v += d / 2; }
        Vector2d q0, q1, q2;
        T.GetTriangleLayout(t, out q0, out q1, out q2);
        return u * q0 + v * q1 + w * q2;
    }

    // flips, then N0 / 2 random insertions and flips again; the library executes them, the layer follows
    static NormalCoordinates Build(DMesh3 m, int step, out int flips, out int inserted, out int refused)
    {
        var T = new IntrinsicTriangulation(m);
        var NC = new NormalCoordinates(T);
        flips = NC.FlipToDelaunay();
        inserted = refused = 0;
        if (step > 0) {
            var rng = new Random(7);
            int K = T.InputVertexCount / 2;
            for (int k = 0; k < K; ++k) {
                int t;
                Vector2d p = RandomPoint(T, rng, step == 2, out t);
                if (NC.Insert(t, p) >= 0) inserted++; else refused++;
            }
            flips += NC.FlipToDelaunay();
        }
        return NC;
    }

    // Stage 2: the integer layer on the same triangulations: is its subdivision valid, and does it agree with the
    // traced one where that succeeds?
    static void Integer(bool verbose)
    {
        Console.WriteLine();
        Console.WriteLine("| mesh | step | flips | crossings | faces | max n | longest curve | integer valid | traced ok | agree (where traced ok) | out of order | same over transforms |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        int valid = 0, sameAll = 0, groups = 0, scaleSame = 0, refusedAll = 0;
        int[] runs = new int[3], tracedOk = new int[3], silent = new int[3];
        double[] maxArea = new double[3]; double maxDist = 0;
        var falseValences = new SortedDictionary<int, int>();
        foreach (var c in Meshes.All()) {
            DMesh3 m0 = c.Make();
            for (int step = 0; step < 3; ++step) {
                bool insert = step > 0;
                int v = 0, ok = 0, ag = 0, ooo = 0, flips0 = 0;
                long maxN = 0; int longest = 0;
                var counts = new List<(int, int)>();
                for (int k = 0; k < Meshes.TransformNames.Length; ++k) {
                    DMesh3 m = Meshes.Transformed(m0, k);
                    int flips, inserted, refused;
                    var NC = Build(m, step, out flips, out inserted, out refused);
                    refusedAll += refused;
                    if (k == 0) flips0 = flips;
                    if (insert == false) {
                        // the layer's flip loop is the library's
                        var L = new IntrinsicTriangulation(m);
                        if (L.FlipToDelaunay() != flips) { failures++; Console.Error.WriteLine($"{c.Name}: flip counts differ"); }
                    }
                    var S = NC.Extract();
                    int chi0 = Euler(m, out int b0);
                    // the integer polygons must be a conforming complex of the same topology; its boundary edges are
                    // the input boundary edges, cut at the inserted boundary vertices (none here)
                    if (S.Valid && (Euler(S.Polygons, out int bi) != chi0 || bi != b0)) { S.Valid = false; S.Error = "polygons are not a conforming complex"; }
                    if (S.Valid && OddCrossings(S.Polygons, NC.T.VertexCount) != 0) { S.Valid = false; S.Error = "crossing that is not 4-valent"; }
                    // geometry: the polygons tile the surface (wrong regions, positions or unfolding change the area);
                    // the tolerance is that of the library's own area check
                    double areaErr = S.Valid ? AreaError(m, S.Points, S.Polygons) : 0;
                    maxArea[step] = Math.Max(maxArea[step], areaErr);
                    if (S.Valid && !(areaErr <= 1e-6)) { S.Valid = false; S.Error = "polygon areas differ from the mesh area by " + areaErr; }
                    if (S.Valid) v++;
                    else { failures++; Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] integer: {S.Error}"); }
                    var cs = NC.T.ExtractCommonSubdivision();
                    if (cs.Status == CommonSubdivisionStatus.Success) {
                        ok++;
                        bool same = cs.Points.Count == NC.T.VertexCount + S.Crossings && cs.Faces.Count == S.Faces;
                        bool conforming = Euler(cs.Faces.Select(f => f.Corners), out int bt) == chi0 && bt == b0;
                        if (conforming == false) { failures++; Console.Error.WriteLine($"{c.Name}: traced Success is not a conforming complex"); }
                        // a crossing of an intrinsic and an input edge has four edges; a traced "crossing" with three is a
                        // trace that crosses an input edge of its head vertex and is then taken as running along it
                        int odd = OddCrossings(cs.Faces.Select(f => f.Corners), NC.T.VertexCount, falseValences);
                        if (same && S.Valid) {
                            // the same crossing points, up to rounding
                            double diag = m.CachedBounds.DiagonalLength;
                            double d = Math.Max(PointSetDistance(cs.Points, S.Points, NC.T.VertexCount, diag), PointSetDistance(S.Points, cs.Points, NC.T.VertexCount, diag));
                            maxDist = Math.Max(maxDist, d);
                            if (!(d <= 1e-7)) { failures++; Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}]: crossing positions differ by {d} of the diagonal"); }
                        }
                        if (odd > 0) silent[step]++;
                        if (same != (odd == 0)) { failures++; Console.Error.WriteLine($"{c.Name}: counts agree = {same} but {odd} traced crossings are not 4-valent"); }
                        if (verbose && odd > 0) Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] insert={insert}: traced Success with {odd} false crossings");
                        if (same) ag++;
                        else if (verbose) Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] insert={insert}: traced {cs.Points.Count - NC.T.VertexCount} crossings / {cs.Faces.Count} faces, integer {S.Crossings} / {S.Faces}");
                    } else if (verbose) Console.Error.WriteLine($"{c.Name} [{Meshes.TransformNames[k]}] insert={insert} traced: {cs.Error}");
                    ooo += S.OutOfOrder;
                    maxN = Math.Max(maxN, NC.MaxN); longest = Math.Max(longest, S.LongestCurve);
                    counts.Add((S.Crossings, S.Faces));
                }
                runs[step] += 4; valid += v; tracedOk[step] += ok; groups++;
                bool all = counts.All(x => x == counts[0]);
                if (all) sameAll++;
                if (counts[2] == counts[0]) scaleSame++;
                Console.WriteLine($"| {c.Name} | {StepNames[step]} | {flips0} | {counts[0].Item1} | {counts[0].Item2} | {maxN} | {longest} | {v} / 4 | {ok} / 4 | {ag} / {ok} | {ooo} | {(all ? "yes" : counts[2] == counts[0] ? "scale only" : "no")} |");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"integer layer valid: {valid} of {runs.Sum()} runs; same counts over all transforms: {sameAll} of {groups} " +
            $"(scaled 2^-30 same as identity: {scaleSame} of {groups}); insertions refused by the library: {refusedAll}");
        for (int step = 0; step < 3; ++step)
            Console.WriteLine($"{StepNames[step]}: traced Success in {tracedOk[step]} of {runs[step]} runs, of these {silent[step]} with false crossings; integer polygons: largest relative area error {maxArea[step]:E1}");
        Console.WriteLine($"false crossings of the traced Success results: " + string.Join(", ", falseValences.Select(kv => kv.Value + " x valence " + kv.Key)) +
            $"; largest distance between traced and integer crossing positions where the counts agree: {maxDist:E1} of the bounding box diagonal");
    }

    // Euler characteristic of a polygon complex (vertices that are used, undirected edges, faces) and the number of
    // edges that lie on one polygon only (-1: an edge with more than two polygons, or polygons that are not
    // consistently oriented)
    static int Euler(IEnumerable<int[]> polys, out int oneSided)
    {
        var edges = new Dictionary<(int, int), int>();
        var directed = new HashSet<(int, int)>();
        var verts = new HashSet<int>();
        int faces = 0;
        bool oriented = true;
        foreach (int[] p in polys) {
            faces++;
            for (int k = 0; k < p.Length; ++k) {
                int a = p[k], b = p[(k + 1) % p.Length];
                if (directed.Add((a, b)) == false) oriented = false;        // two polygons run along an edge the same way
                verts.Add(a);
                var key = (Math.Min(a, b), Math.Max(a, b));
                int cnt; edges.TryGetValue(key, out cnt); edges[key] = cnt + 1;
            }
        }
        oneSided = 0;
        foreach (int cnt in edges.Values) { if (cnt == 1) oneSided++; else if (cnt != 2) { oneSided = -1; break; } }
        if (oriented == false) oneSided = -1;
        return verts.Count - edges.Count + faces;
    }

    // subdivision vertices from firstCrossing on that do not have exactly four edges; valences: how many of each valence
    static int OddCrossings(IEnumerable<int[]> polys, int firstCrossing, SortedDictionary<int, int> valences = null)
    {
        var edges = new HashSet<(int, int)>();
        var valence = new Dictionary<int, int>();
        foreach (int[] p in polys)
            for (int k = 0; k < p.Length; ++k) {
                int a = p[k], b = p[(k + 1) % p.Length];
                if (edges.Add((Math.Min(a, b), Math.Max(a, b))) == false) continue;
                int c; valence.TryGetValue(a, out c); valence[a] = c + 1;
                valence.TryGetValue(b, out c); valence[b] = c + 1;
            }
        int odd = 0;
        foreach (var kv in valence)
            if (kv.Key >= firstCrossing && kv.Value != 4) { odd++; if (valences != null) Count(valences, kv.Value); }
        return odd;
    }
    static void Count(SortedDictionary<int, int> d, int k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }

    // area of the polygons (each is planar: it lies in one input triangle), relative difference to the mesh area
    static double AreaError(DMesh3 m, List<Vector3d> pts, List<int[]> polys)
    {
        double sum = 0, area = 0;
        foreach (int[] p in polys) {
            Vector3d a = Vector3d.Zero;
            for (int k = 1; k + 1 < p.Length; ++k) a += (pts[p[k]] - pts[p[0]]).Cross(pts[p[k + 1]] - pts[p[0]]);
            sum += 0.5 * a.Length;
        }
        foreach (int t in m.TriangleIndices()) area += m.GetTriArea(t);
        return Math.Abs(sum - area) / area;
    }

    // largest distance from a point of a (from index first on) to the nearest point of b, relative to the bounding box diagonal
    static double PointSetDistance(List<Vector3d> a, List<Vector3d> b, int first, double diag)
    {
        double cell = 1e-6 * diag;
        var grid = new Dictionary<(long, long, long), List<int>>();
        Func<Vector3d, (long, long, long)> key = p => ((long)Math.Floor(p.x / cell), (long)Math.Floor(p.y / cell), (long)Math.Floor(p.z / cell));
        for (int i = first; i < b.Count; ++i) {
            var k = key(b[i]);
            List<int> l;
            if (grid.TryGetValue(k, out l) == false) grid[k] = l = new List<int>();
            l.Add(i);
        }
        double worst = 0;
        for (int i = first; i < a.Count; ++i) {
            var k = key(a[i]);
            double best = double.MaxValue;
            for (long dx = -1; dx <= 1; ++dx) for (long dy = -1; dy <= 1; ++dy) for (long dz = -1; dz <= 1; ++dz) {
                List<int> l;
                if (grid.TryGetValue((k.Item1 + dx, k.Item2 + dy, k.Item3 + dz), out l))
                    foreach (int j in l) best = Math.Min(best, a[i].Distance(b[j]));
            }
            worst = Math.Max(worst, best);
        }
        return worst / diag;
    }

    static int Euler(DMesh3 m, out int boundaryEdges)
    {
        boundaryEdges = 0;
        foreach (int e in m.EdgeIndices()) if (m.IsBoundaryEdge(e)) boundaryEdges++;
        return m.VertexCount - m.EdgeCount + m.TriangleCount;
    }

    static double Median(Func<double> f, int runs = 5)
    {
        f();
        var l = new List<double>();
        for (int k = 0; k < runs; ++k) l.Add(f());
        l.Sort();
        return l[runs / 2];
    }

    static double Ms(Action a) { var sw = Stopwatch.StartNew(); a(); return sw.Elapsed.TotalMilliseconds; }

    // Stage 3: cost of the integer bookkeeping against the signpost path (identity transform)
    static void Cost()
    {
        Console.WriteLine();
        Console.WriteLine("| mesh | V | flips | flips: library | flips: + integers | V/2 insertions: library | V/2 insertions: + integers | subdivision: traced (library) | subdivision: integers, connectivity | subdivision: integers, positions and polygons | crossings | max n |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
        var big = new List<Meshes.Case>(Meshes.All().Where(c => c.Name.StartsWith("sheared torus") || c.Name == "ring sphere 160x24" || c.Name == "obtuse plane dy=0.05"));
        big.Add(new Meshes.Case() { Name = "sheared torus s=9, 200 x 160", Make = () => Meshes.Sheared(200, 160, 45, true) });
        big.Add(new Meshes.Case() { Name = "obtuse plane 40 x 800", Make = () => Meshes.ObtusePlane(40, 800, 1.0, 0.05) });
        foreach (var c in big) {
            DMesh3 m = c.Make();
            int flips = 0;
            double lib = Median(() => { var T = new IntrinsicTriangulation(m); return Ms(() => flips = T.FlipToDelaunay()); });
            double withInt = Median(() => { var T = new IntrinsicTriangulation(m); var NC = new NormalCoordinates(T); return Ms(() => NC.FlipToDelaunay()); });
            int f, ins, refd;
            var N = Build(m, 0, out f, out ins, out refd);
            string status = "";
            double traced = Median(() => Ms(() => { var cs = N.T.ExtractCommonSubdivision(); status = cs.Status == CommonSubdivisionStatus.Success ? "" : " (failed)"; }), 3);
            NormalCoordinates.Subdivision S = null;
            double conn = Median(() => Ms(() => S = N.Extract(false)), 3);
            double full = Median(() => Ms(() => S = N.Extract(true)), 3);
            // insertions (N0 / 2 random points, the same in both), on the Delaunay triangulation
            int queries = 0;
            double insLib = Median(() => {
                var T = new IntrinsicTriangulation(m); T.FlipToDelaunay();
                var rng = new Random(7); int K = T.InputVertexCount / 2;
                return Ms(() => { for (int k = 0; k < K; ++k) { int t; Vector2d p = RandomPoint(T, rng, false, out t); T.InsertVertex(t, p); } });
            }, 3);
            double insInt = Median(() => {
                var T = new IntrinsicTriangulation(m); var NC = new NormalCoordinates(T); NC.FlipToDelaunay();
                var rng = new Random(7); int K = T.InputVertexCount / 2;
                double ms = Ms(() => { for (int k = 0; k < K; ++k) { int t; Vector2d p = RandomPoint(T, rng, false, out t); NC.Insert(t, p); } });
                queries = NC.ChordQueries;
                return ms;
            }, 3);
            Console.WriteLine($"| {c.Name} | {m.VertexCount} | {flips} | {lib:F1} ms | {withInt:F1} ms | {insLib:F1} ms | {insInt:F1} ms ({queries} curves traced) | {traced:F1} ms{status} | {conn:F1} ms | {full:F1} ms | {S.Crossings} | {N.MaxN} |");
        }
    }

    static void Count(SortedDictionary<string, int> d, string k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }
}
