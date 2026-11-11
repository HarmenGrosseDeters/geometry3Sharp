// Exact Boolean 3.0 pre-study, topic 7 (docs/design/EXACT_BOOLEAN_30_PRESTUDY.md section 9): one body against many.
//   prestudy-many-a <terrain|cad> <n> <seed> [--size <k>]   T3a: A - (B1 u ... u Bn)
//       variants: chained (n differences), onepass (one ExactMeshBoolean over all, predicate w_A > 0 and no w_i > 0),
//       twostep (n-ary union of the Bi, then A - U), tree (balanced pairwise unions of the Bi, each level in parallel,
//       then A - U); plus a pruning probe: the arrangement of all inputs, the share of the Bi's pieces outside A (the
//       part an operation-aware pruning drops), and the arrangement of the Bi alone (their mutual intersections).
//   prestudy-many-b <terrain|cad> <n> <seed> [--size <k>]   T3b: A n Bi for every i, separate results
//       variants: serial (n booleans one after the other), parallel (the n booleans in parallel over i, results in an
//       array by i); the fixed cost of A alone (A n a small box outside A's box, times n); per-body volumes and hashes
//       of both variants must be identical.
// Workloads are procedural (nothing read from files):
//   terrain: A = a TIN heightfield over 1 km x 1 km (grid of size x size vertices, interior jittered) extruded to a
//            closed body; Bi = construction bodies: pits and foundations (rectangular / polygonal prisms and frusta
//            from above the terrain to below it, over rounded rectangles or polygons), trenches (a narrow rectangle swept
//            along a curve), road bodies (a trapezoid swept along a gently curved polyline, following the terrain);
//            about 70-800 triangles each.
//   cad:     A = a block 100 x 60 x 40 with its faces subdivided (GridBox3Generator, size edge vertices); Bi = drills
//            (32-sided prisms, vertical, a fifth horizontal, blind or through) and pockets (rounded rectangles, 40
//            vertices, from the top).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using g3;
using gs;

static class PrestudyMany
{
    // ------------------------------------------------------------------ generators

    static void FixOrientation(DMesh3 m)
    {
        if (MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x < 0)
            m.ReverseOrientation(true);
    }

    // prism (or frustum) over a convex polygon footprint (counter-clockwise in xy), from z0 to z1; the top polygon is the
    // footprint scaled by topScale about its centroid
    static DMesh3 Prism(List<Vector2d> poly, double z0, double z1, double topScale = 1)
    {
        var m = new DMesh3();
        int k = poly.Count;
        Vector2d c = Vector2d.Zero;
        foreach (var p in poly) c += p;
        c /= k;
        for (int i = 0; i < k; ++i) m.AppendVertex(new Vector3d(poly[i].x, poly[i].y, z0));
        for (int i = 0; i < k; ++i) { Vector2d p = c + (poly[i] - c) * topScale; m.AppendVertex(new Vector3d(p.x, p.y, z1)); }
        int cb = m.AppendVertex(new Vector3d(c.x, c.y, z0)), ct = m.AppendVertex(new Vector3d(c.x, c.y, z1));
        for (int i = 0; i < k; ++i) {
            int j = (i + 1) % k;
            m.AppendTriangle(cb, j, i);
            m.AppendTriangle(ct, k + i, k + j);
            m.AppendTriangle(i, j, k + j);
            m.AppendTriangle(i, k + j, k + i);
        }
        FixOrientation(m);
        return m;
    }

    static List<Vector2d> Rect(Vector2d c, double w, double l, double angle)
    {
        var d = new Vector2d(Math.Cos(angle), Math.Sin(angle));
        var e = new Vector2d(-d.y, d.x);
        return new List<Vector2d> { c - d * w / 2 - e * l / 2, c + d * w / 2 - e * l / 2, c + d * w / 2 + e * l / 2, c - d * w / 2 + e * l / 2 };
    }

    static List<Vector2d> NGon(Vector2d c, double r, int k, double phase)
    {
        var p = new List<Vector2d>();
        for (int i = 0; i < k; ++i) {
            double a = phase + 2 * Math.PI * i / k;
            p.Add(c + new Vector2d(Math.Cos(a), Math.Sin(a)) * r);
        }
        return p;
    }

    static List<Vector2d> RoundedRect(Vector2d c, double w, double l, double r, int perCorner, double angle)
    {
        var p = new List<Vector2d>();
        var d = new Vector2d(Math.Cos(angle), Math.Sin(angle));
        var e = new Vector2d(-d.y, d.x);
        double[,] corners = { { 1, 1 }, { -1, 1 }, { -1, -1 }, { 1, -1 } };
        for (int q = 0; q < 4; ++q) {
            double a0 = q * Math.PI / 2;
            Vector2d cc = c + d * corners[q, 0] * (w / 2 - r) + e * corners[q, 1] * (l / 2 - r);
            for (int i = 0; i < perCorner; ++i) {
                double a = a0 + (Math.PI / 2) * i / (perCorner - 1);
                p.Add(cc + (d * Math.Cos(a) + e * Math.Sin(a)) * r);
            }
        }
        return p;
    }

    static double Height(double x, double y)
    {
        return 20 * Math.Sin(x / 170) * Math.Cos(y / 130) + 8 * Math.Sin(x / 53 + y / 71) + 3 * Math.Sin(x / 17) * Math.Cos(y / 23);
    }

    const double Bottom = -60;

    static DMesh3 Terrain(int g, Random r)
    {
        var m = new DMesh3();
        double h = 1000.0 / (g - 1);
        var id = new int[g, g];
        for (int j = 0; j < g; ++j)
            for (int i = 0; i < g; ++i) {
                double x = i * h, y = j * h;
                bool border = i == 0 || j == 0 || i == g - 1 || j == g - 1;
                if (border == false) { x += (r.NextDouble() - 0.5) * 0.6 * h; y += (r.NextDouble() - 0.5) * 0.6 * h; }
                id[i, j] = m.AppendVertex(new Vector3d(x, y, Height(x, y)));
            }
        for (int j = 0; j + 1 < g; ++j)
            for (int i = 0; i + 1 < g; ++i) {
                m.AppendTriangle(id[i, j], id[i + 1, j], id[i + 1, j + 1]);
                m.AppendTriangle(id[i, j], id[i + 1, j + 1], id[i, j + 1]);
            }
        // the boundary loop counter-clockwise, its copy at the bottom, walls and a bottom fan
        var loop = new List<int>();
        for (int i = 0; i < g - 1; ++i) loop.Add(id[i, 0]);
        for (int j = 0; j < g - 1; ++j) loop.Add(id[g - 1, j]);
        for (int i = g - 1; i > 0; --i) loop.Add(id[i, g - 1]);
        for (int j = g - 1; j > 0; --j) loop.Add(id[0, j]);
        var low = loop.Select(v => { Vector3d p = m.GetVertex(v); return m.AppendVertex(new Vector3d(p.x, p.y, Bottom)); }).ToList();
        int center = m.AppendVertex(new Vector3d(500, 500, Bottom));
        for (int k = 0; k < loop.Count; ++k) {
            int k1 = (k + 1) % loop.Count;
            m.AppendTriangle(loop[k1], loop[k], low[k]);
            m.AppendTriangle(loop[k1], low[k], low[k1]);
            m.AppendTriangle(center, low[k1], low[k]);
        }
        FixOrientation(m);
        return m;
    }

    static DMesh3 Road(Random r) { return Sweep(r, 20 + r.Next(81), 14 + 6 * r.NextDouble(), 8 + 2 * r.NextDouble(), 3, 1); }

    // a body swept along a gently curved polyline of segs segments 10 m apart: a trapezoid cross-section of bottom
    // width wb at below under the terrain and top width wt at above over it
    static DMesh3 Sweep(Random r, int segs, double wb, double wt, double below, double above)
    {
        double x = 100 + 800 * r.NextDouble(), y = 100 + 800 * r.NextDouble(), dir = 2 * Math.PI * r.NextDouble(), turn = (r.NextDouble() - 0.5) * 0.04;
        var pts = new List<Vector2d>();
        for (int k = 0; k <= segs; ++k) {
            pts.Add(new Vector2d(x, y));
            x += 10 * Math.Cos(dir); y += 10 * Math.Sin(dir); dir += turn;
        }
        var m = new DMesh3();
        var sec = new List<int[]>();
        for (int k = 0; k <= segs; ++k) {
            Vector2d t = (k < segs ? pts[k + 1] - pts[k] : pts[k] - pts[k - 1]).Normalized;
            if (k > 0 && k < segs) t = (pts[k + 1] - pts[k - 1]).Normalized;
            var nrm = new Vector2d(-t.y, t.x);
            double z = Height(pts[k].x, pts[k].y);
            Vector2d bl = pts[k] + nrm * wb / 2, br = pts[k] - nrm * wb / 2, tl = pts[k] + nrm * wt / 2, tr = pts[k] - nrm * wt / 2;
            sec.Add(new[] {
                m.AppendVertex(new Vector3d(bl.x, bl.y, z - below)), m.AppendVertex(new Vector3d(br.x, br.y, z - below)),
                m.AppendVertex(new Vector3d(tr.x, tr.y, z + above)), m.AppendVertex(new Vector3d(tl.x, tl.y, z + above)) });
        }
        for (int k = 0; k < segs; ++k)
            for (int s = 0; s < 4; ++s) {
                int s1 = (s + 1) % 4;
                m.AppendTriangle(sec[k][s], sec[k][s1], sec[k + 1][s1]);
                m.AppendTriangle(sec[k][s], sec[k + 1][s1], sec[k + 1][s]);
            }
        m.AppendTriangle(sec[0][0], sec[0][2], sec[0][1]); m.AppendTriangle(sec[0][0], sec[0][3], sec[0][2]);
        m.AppendTriangle(sec[segs][0], sec[segs][1], sec[segs][2]); m.AppendTriangle(sec[segs][0], sec[segs][2], sec[segs][3]);
        FixOrientation(m);
        return m;
    }

    static List<DMesh3> ConstructionBodies(int n, Random r)
    {
        var list = new List<DMesh3>();
        for (int i = 0; i < n; ++i) {
            double kind = r.NextDouble();
            var c = new Vector2d(60 + 880 * r.NextDouble(), 60 + 880 * r.NextDouble());
            double zt = Height(c.x, c.y);
            if (kind < 0.3) {          // pit: a sloped frustum over a rounded rectangle (32-100 corners)
                double w = 10 + 30 * r.NextDouble(), l = 10 + 30 * r.NextDouble();
                list.Add(Prism(RoundedRect(c, w, l, 2 + 3 * r.NextDouble(), 8 + r.Next(18), Math.PI * r.NextDouble()), zt - 2 - 6 * r.NextDouble(), zt + 30, 1.6));
            } else if (kind < 0.5) {   // foundation: a prism over a rounded rectangle (16-56 corners)
                double w = 8 + 20 * r.NextDouble(), l = 8 + 20 * r.NextDouble();
                list.Add(Prism(RoundedRect(c, w, l, 1 + 2 * r.NextDouble(), 4 + r.Next(11), Math.PI * r.NextDouble()), zt - 1.5 - 3 * r.NextDouble(), zt + 30));
            } else if (kind < 0.65) {  // shaft / round pit: a polygonal prism with 32-200 sides
                list.Add(Prism(NGon(c, 3 + 12 * r.NextDouble(), 32 + r.Next(169), r.NextDouble()), zt - 3 - 10 * r.NextDouble(), zt + 30));
            } else if (kind < 0.85) {  // trench: a narrow rectangle swept along a curve (10-60 segments)
                double w = 1 + 2 * r.NextDouble();
                list.Add(Sweep(r, 10 + r.Next(51), w, w, 1 + 2 * r.NextDouble(), 25));
            } else {
                list.Add(Road(r));
            }
        }
        return list;
    }

    static DMesh3 Block(int k)
    {
        return new GridBox3Generator() { Box = new Box3d(new Vector3d(50, 30, 20), new Vector3d(50, 30, 20)), EdgeVertices = k }.Generate().MakeDMesh();
    }

    static List<DMesh3> Tools(int n, Random r)
    {
        var list = new List<DMesh3>();
        for (int i = 0; i < n; ++i) {
            double kind = r.NextDouble();
            var c = new Vector2d(5 + 90 * r.NextDouble(), 5 + 50 * r.NextDouble());
            if (kind < 0.6) {          // vertical drill, blind or through
                double depth = r.NextDouble() < 0.3 ? 50 : 5 + 30 * r.NextDouble();
                list.Add(Prism(NGon(c, 0.5 + 3 * r.NextDouble(), 32, r.NextDouble()), 40 - depth, 45));
            } else if (kind < 0.8) {   // horizontal drill along x
                double hz = 5 + 30 * r.NextDouble();
                var p = Prism(NGon(new Vector2d(hz, c.y), 0.5 + 2 * r.NextDouble(), 32, r.NextDouble()), -5, 30 + 75 * r.NextDouble());
                // (the prism runs along z: rotated about y by 90 degrees it runs along x from -5, its axis at z = -hz,
                // then lifted to z = hz)
                MeshTransforms.Rotate(p, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisY, 90));
                MeshTransforms.Translate(p, new Vector3d(0, 0, 2 * hz));
                list.Add(p);
            } else {                   // pocket from the top
                double w = 4 + 16 * r.NextDouble(), l = 4 + 16 * r.NextDouble();
                list.Add(Prism(RoundedRect(c, w, l, 1 + 0.3 * Math.Min(w, l), 10, Math.PI * r.NextDouble()), 40 - 3 - 7 * r.NextDouble(), 45));
            }
        }
        return list;
    }

    static void Workload(string kind, int n, int seed, int size, out DMesh3 a, out List<DMesh3> bs)
    {
        var r = new Random(seed);
        if (kind == "terrain") { a = Terrain(size > 0 ? size : 50, r); bs = ConstructionBodies(n, r); }
        else { a = Block(size > 0 ? size : 30); bs = Tools(n, r); }
    }

    // ------------------------------------------------------------------ helpers

    static double Volume(DMesh3 m) { return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x; }

    static string Hash(DMesh3 m)
    {
        ulong h = 14695981039346656037UL;
        void add(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        foreach (int v in m.VertexIndices()) {
            Vector3d p = m.GetVertex(v);
            add(v); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
        }
        foreach (int t in m.TriangleIndices()) { Index3i f = m.GetTriangle(t); add(t); add(f.a); add(f.b); add(f.c); }
        return h.ToString("X16");
    }

    static DMesh3 Op(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations o, ref long roundingMs, ref int notClean)
    {
        var op = new ExactMeshBoolean(a, b, o);
        if (op.Compute() == false) throw new Exception(op.Error);
        roundingMs += op.PhaseMilliseconds.TryGetValue("rounding", out long rm) ? rm : 0;
        if (op.RoundingClean == false) notClean++;
        return op.Result;
    }

    static void Warm()
    {
        DMesh3 w1 = new Sphere3Generator_NormalizedCube() { EdgeVertices = 6 }.Generate().MakeDMesh();
        DMesh3 w2 = new DMesh3(w1);
        MeshTransforms.Translate(w2, new Vector3d(0.3, 0.2, 0.1));
        for (int k = 0; k < 3; ++k) {
            new ExactMeshBoolean(w1, w2, ExactMeshBoolean.Operations.Difference).Compute();
            new ExactMeshBoolean(new[] { w1, w2 }, ExactMeshBoolean.Operations.Union).Compute();
        }
    }

    static void Gc() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    // ------------------------------------------------------------------ T3a

    // T3a step 1 (upper-bound check): the share of the Bi triangles that a triangle-level early-out drops (box meets no
    // box of A, and a vertex outside the closed A) and an upper bound of the time it saves: a one pass without those
    // triangles (its Bi are no longer closed, so only its arrangement phases are used) and the full one pass's
    // classification scaled by the share of the pieces that remain.
    //   t3a-cull-share <terrain|cad> <n> <seed> [--size k]
    public static int RunCullShare(string[] args)
    {
        string kind = args[0]; int n = int.Parse(args[1]), seed = int.Parse(args[2]), size = 0;
        for (int i = 3; i < args.Length; ++i)
            if (args[i] == "--size") size = int.Parse(args[++i]);
        DMesh3 a; List<DMesh3> bs;
        Workload(kind, n, seed, size, out a, out bs);
        Warm();
        var aBoxes = new List<AxisAlignedBox3d>();
        foreach (int t in a.TriangleIndices()) {
            Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero, p2 = Vector3d.Zero;
            a.GetTriVertices(t, ref p0, ref p1, ref p2);
            var bx = new AxisAlignedBox3d(p0); bx.Contain(p1); bx.Contain(p2);
            aBoxes.Add(bx);
        }
        var aTree = new TriangleBoxTree(aBoxes);
        var wTree = new DMeshAABBTree3(a, true);
        long total = 0, culled = 0, noOverlapInside = 0;
        var kept = new List<DMesh3>();
        foreach (DMesh3 b in bs) {
            var keep = new DMesh3(b);
            foreach (int t in b.TriangleIndices()) {
                total++;
                Vector3d p0 = Vector3d.Zero, p1 = Vector3d.Zero, p2 = Vector3d.Zero;
                b.GetTriVertices(t, ref p0, ref p1, ref p2);
                var bx = new AxisAlignedBox3d(p0); bx.Contain(p1); bx.Contain(p2);
                bool hit = false;
                aTree.Query(bx, (k) => hit = true);
                if (hit) continue;
                if (wTree.FastWindingNumber(p0) > 0.5) { noOverlapInside++; continue; }
                culled++;
                keep.RemoveTriangle(t, true, false);
            }
            kept.Add(keep);
        }
        var row = new Dictionary<string, object> { ["case"] = $"{kind}_{size}_{n}_{seed}", ["a_tris"] = a.TriangleCount, ["b_tris"] = total,
            ["culled"] = culled, ["culled_share"] = (double)culled / total, ["no_overlap_inside"] = noOverlapInside };
        Func<List<DMesh3>, ExactMeshBoolean> run = (list) => {
            var meshes = new List<DMesh3> { a };
            meshes.AddRange(list);
            Gc();
            var op = new ExactMeshBoolean(meshes, ExactMeshBoolean.Operations.Difference);
            var sw = Stopwatch.StartNew();
            op.Compute();
            op.PhaseMilliseconds["total"] = sw.ElapsedMilliseconds;
            return op;
        };
        ExactMeshBoolean full = run(bs), cut = run(kept);
        long T(ExactMeshBoolean o, string k) { long v; return o.PhaseMilliseconds.TryGetValue(k, out v) ? v : 0; }
        // (the arrangement's top-level phases; their sub-phases, "intersections.pairs" etc., are parts of them)
        string[] arrPhases = { "setup", "intersections", "merge", "triangulation" };
        long arrFull = arrPhases.Sum(k => T(full, k)), arrCut = arrPhases.Sum(k => T(cut, k));
        double pieceShare = (double)cut.Arrangement.Output.Count / full.Arrangement.Output.Count;
        double saved = (arrFull - arrCut) + T(full, "classification") * (1 - pieceShare);
        row["total_ms"] = T(full, "total");
        row["phases_full"] = full.PhaseMilliseconds;
        row["phases_cut"] = cut.PhaseMilliseconds;
        row["pieces_full"] = full.Arrangement.Output.Count;
        row["pieces_cut"] = cut.Arrangement.Output.Count;
        row["saved_upper_ms"] = (long)saved;
        row["saved_upper_share"] = saved / T(full, "total");
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    // T3a A/B: the one-pass Difference with and without the early-out (ExactMeshBoolean.CullOutsideA): times, culled
    // triangles, whether the results are bit-identical, volumes.
    //   t3a-ab <terrain|cad> <n> <seed> [--size k] [--op difference|intersection]
    public static int RunCullAB(string[] args)
    {
        string kind = args[0]; int n = int.Parse(args[1]), seed = int.Parse(args[2]), size = 0;
        var op = ExactMeshBoolean.Operations.Difference;
        int repeat = 1;
        bool serialCheck = false;
        for (int i = 3; i < args.Length; ++i) {
            if (args[i] == "--size") size = int.Parse(args[++i]);
            else if (args[i] == "--repeat") repeat = int.Parse(args[++i]);
            else if (args[i] == "--serial") serialCheck = true;
            else if (args[i] == "--op") op = args[++i] == "intersection" ? ExactMeshBoolean.Operations.Intersection : ExactMeshBoolean.Operations.Difference;
        }
        DMesh3 a; List<DMesh3> bs;
        if (kind == "spheres") {
            // a plain binary case: two overlapping spheres (n edge vertices), the second shifted by seed / 10
            a = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            DMesh3 b = new Sphere3Generator_NormalizedCube() { Radius = 0.8, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(b, new Vector3d(0.1 * seed, 0.3, 0.2));
            bs = new List<DMesh3> { b };
        } else
            Workload(kind, n, seed, size, out a, out bs);
        Warm();
        var meshes = new List<DMesh3> { a };
        meshes.AddRange(bs);
        var row = new Dictionary<string, object> { ["case"] = $"{kind}_{size}_{n}_{seed}", ["op"] = op.ToString(), ["a_tris"] = a.TriangleCount,
            ["b_tris"] = bs.Sum(b => b.TriangleCount) };
        var results = new Dictionary<bool, DMesh3>();
        var times = new Dictionary<bool, List<double>> { [false] = new List<double>(), [true] = new List<double>() };
        // (determinism of each mode on its own: the distinct result hashes over the repeats, and with --serial one serial run)
        var hashes = new Dictionary<bool, HashSet<string>> { [false] = new HashSet<string>(), [true] = new HashSet<string>() };
        // (alternating, so that drift hits both alike; the last run of each is kept for the checks)
        for (int rep = 0; rep < repeat; ++rep)
        foreach (bool cull in new[] { false, true }) {
            Gc();
            var o = new ExactMeshBoolean(meshes, op) { CullOutsideA = cull };
            var sw = Stopwatch.StartNew();
            o.Compute();
            string k = cull ? "cull" : "base";
            times[cull].Add(sw.Elapsed.TotalMilliseconds);
            hashes[cull].Add(Hash(o.Result));
            var sorted = times[cull].OrderBy(x => x).ToList();
            row[k + "_ms"] = sorted[sorted.Count / 2];
            row[k + "_all_ms"] = times[cull].ToArray();
            row[k + "_phases"] = new Dictionary<string, long>(o.PhaseMilliseconds);
            row[k + "_ok"] = o.Status == ExactMeshBoolean.BooleanStatus.Success;
            row[k + "_pieces"] = o.Arrangement.Output.Count;
            row[k + "_tris"] = o.Result.TriangleCount;
            row[k + "_volume"] = Volume(o.Result);
            row[k + "_clean"] = MeshRobustRepair.NotCleanReason(o.Result);
            if (cull) row["culled"] = o.Counts["t3a_culled"];
            results[cull] = o.Result;
        }
        row["equal"] = Hash(results[false]) == Hash(results[true]);
        row["base_distinct_hashes"] = hashes[false].Count;
        row["cull_distinct_hashes"] = hashes[true].Count;
        if (serialCheck)
            foreach (bool cull in new[] { false, true }) {
                gParallel.SerialOnThisThread = true;
                try {
                    var o = new ExactMeshBoolean(meshes, op) { CullOutsideA = cull };
                    o.Compute();
                    row[(cull ? "cull" : "base") + "_serial_equal"] = Hash(o.Result) == Hash(results[cull]);
                } finally {
                    gParallel.SerialOnThisThread = false;
                }
            }
        if ((bool)row["equal"] == false) {
            // what differs: vertices and triangles (by exact position) in one result only, and their areas
            Func<DMesh3, HashSet<Vector3d>> verts = (m) => new HashSet<Vector3d>(m.VertexIndices().Select(v => m.GetVertex(v)));
            Func<DMesh3, Dictionary<string, double>> tris = (m) => {
                var d = new Dictionary<string, double>();
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    var p = new[] { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) };
                    // (rotation-invariant key: start at the smallest corner, keep the orientation)
                    int k0 = 0;
                    for (int k = 1; k < 3; ++k) if (string.CompareOrdinal(p[k].ToString("R"), p[k0].ToString("R")) < 0) k0 = k;
                    string key = string.Join("|", Enumerable.Range(0, 3).Select(k => { var v = p[(k0 + k) % 3]; return $"{v.x:R},{v.y:R},{v.z:R}"; }));
                    d[key] = m.GetTriArea(t);
                }
                return d;
            };
            var vb = verts(results[false]); var vc = verts(results[true]);
            var tb = tris(results[false]); var tc = tris(results[true]);
            row["verts_only_base"] = vb.Count(v => vc.Contains(v) == false);
            row["verts_only_cull"] = vc.Count(v => vb.Contains(v) == false);
            row["tris_only_base"] = tb.Keys.Count(k => tc.ContainsKey(k) == false);
            row["tris_only_cull"] = tc.Keys.Count(k => tb.ContainsKey(k) == false);
            row["area_only_base"] = tb.Where(kv => tc.ContainsKey(kv.Key) == false).Sum(kv => kv.Value);
            row["area_only_cull"] = tc.Where(kv => tb.ContainsKey(kv.Key) == false).Sum(kv => kv.Value);
        }
        Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    public static int RunA(string[] args)
    {
        string kind = args[0]; int n = int.Parse(args[1]), seed = int.Parse(args[2]), size = 0;
        var only = new HashSet<string>();
        for (int i = 3; i < args.Length; ++i) {
            if (args[i] == "--size") size = int.Parse(args[++i]);
            else if (args[i] == "--variants") only = new HashSet<string>(args[++i].Split(','));
        }
        DMesh3 a; List<DMesh3> bs;
        Workload(kind, n, seed, size, out a, out bs);
        Warm();
        string name = $"{kind}_{size}_{n}_{seed}";
        var info = new Dictionary<string, object> { ["case"] = name, ["a_tris"] = a.TriangleCount, ["b_tris"] = bs.Sum(b => b.TriangleCount), ["n"] = n,
            ["cores"] = Environment.ProcessorCount };
        Func<int[], bool> pred = (w) => { if (w[0] <= 0) return false; for (int i = 1; i < w.Length; ++i) if (w[i] > 0) return false; return true; };
        double refVolume = double.NaN;
        foreach (string variant in new[] { "onepass", "chained", "twostep", "tree" }) {
            if (only.Count > 0 && only.Contains(variant) == false) continue;
            Gc();
            var row = new Dictionary<string, object>(info) { ["variant"] = variant };
            long roundingMs = 0; int notClean = 0, roundings = 0;
            var sw = Stopwatch.StartNew();
            DMesh3 result = null;
            try {
                if (variant == "onepass") {
                    var op = new ExactMeshBoolean(new[] { a }.Concat(bs), pred);
                    if (op.Compute() == false) throw new Exception(op.Error);
                    result = op.Result; roundings = 1;
                    roundingMs = op.PhaseMilliseconds.TryGetValue("rounding", out long rm) ? rm : 0;
                    if (op.RoundingClean == false) notClean++;
                    row["phases_ms"] = op.PhaseMilliseconds;
                    row["pieces"] = op.Counts["pieces"];
                    row["patches"] = op.Patches;
                } else if (variant == "chained") {
                    result = a;
                    foreach (DMesh3 b in bs) { result = Op(result, b, ExactMeshBoolean.Operations.Difference, ref roundingMs, ref notClean); roundings++; }
                } else if (variant == "twostep") {
                    var u = new ExactMeshBoolean(bs, ExactMeshBoolean.Operations.Union);
                    if (u.Compute() == false) throw new Exception(u.Error);
                    if (u.RoundingClean == false) notClean++;
                    roundingMs += u.PhaseMilliseconds.TryGetValue("rounding", out long rm) ? rm : 0;
                    result = Op(a, u.Result, ExactMeshBoolean.Operations.Difference, ref roundingMs, ref notClean); roundings = 2;
                } else {
                    // balanced pairwise unions, each level in parallel (results by index: deterministic), then A - U
                    var level = new List<DMesh3>(bs);
                    while (level.Count > 1) {
                        var next = new DMesh3[(level.Count + 1) / 2];
                        var rms = new long[next.Length]; var ncs = new int[next.Length];
                        var cur = level;
                        Parallel.For(0, next.Length, (k) => {
                            if (2 * k + 1 >= cur.Count) { next[k] = cur[2 * k]; return; }
                            next[k] = Op(cur[2 * k], cur[2 * k + 1], ExactMeshBoolean.Operations.Union, ref rms[k], ref ncs[k]);
                        });
                        roundingMs += rms.Sum(); notClean += ncs.Sum(); roundings += cur.Count / 2;
                        level = next.ToList();
                    }
                    result = Op(a, level[0], ExactMeshBoolean.Operations.Difference, ref roundingMs, ref notClean); roundings++;
                }
            } catch (Exception e) {
                row["error"] = e.GetType().Name + ": " + e.Message;
            }
            row["ms"] = sw.ElapsedMilliseconds;
            if (result != null) {
                row["roundings"] = roundings; row["rounding_ms"] = roundingMs; row["steps_not_clean"] = notClean;
                row["tris"] = result.TriangleCount;
                row["not_clean_reason"] = MeshRobustRepair.NotCleanReason(result);
                double v = Volume(result);
                row["volume"] = v;
                if (variant == "onepass") refVolume = v;
                if (double.IsNaN(refVolume) == false) row["volume_rel_diff_vs_onepass"] = Math.Abs(v - refVolume) / Math.Abs(refVolume);
            }
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        if (only.Count == 0 || only.Contains("probe")) {
            // pruning probe: the pieces of the Bi outside A (dropped by an operation-aware pruning before triangulation
            // and classification), and the cost of the Bi's mutual intersections alone
            Gc();
            var row = new Dictionary<string, object>(info) { ["variant"] = "probe" };
            var sw = Stopwatch.StartNew();
            var arr = new ExactMeshArrangement(new[] { a }.Concat(bs).ToList()) { SelfIntersections = true };
            arr.Compute();
            row["arr_ms"] = sw.ElapsedMilliseconds;
            row["arr_phases_ms"] = arr.PhaseMilliseconds;
            var tree = new DMeshAABBTree3(a, true);
            long aPieces = 0, bPieces = 0, bOutside = 0;
            var outside = new bool[arr.Output.Count];
            gParallelFor(arr.Output.Count, (i) => {
                var ot = arr.Output[i];
                if (ot.Mesh == 0) return;
                Vector3d p = (arr.PointToVector(ot.A) + arr.PointToVector(ot.B) + arr.PointToVector(ot.C)) / 3;
                outside[i] = tree.FastWindingNumber(p) < 0.5;
            });
            for (int i = 0; i < arr.Output.Count; ++i) {
                if (arr.Output[i].Mesh == 0) aPieces++;
                else { bPieces++; if (outside[i]) bOutside++; }
            }
            row["a_pieces"] = aPieces; row["b_pieces"] = bPieces; row["b_pieces_outside_a"] = bOutside;
            Gc();
            sw.Restart();
            var arrB = new ExactMeshArrangement(bs) { SelfIntersections = true };
            arrB.Compute();
            row["arr_b_only_ms"] = sw.ElapsedMilliseconds;
            row["arr_b_only_phases_ms"] = arrB.PhaseMilliseconds;
            row["arr_b_only_pieces"] = arrB.Output.Count;
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }

    static void gParallelFor(int n, Action<int> body) { Parallel.For(0, n, body); }

    // ------------------------------------------------------------------ T3b

    public static int RunB(string[] args)
    {
        string kind = args[0]; int n = int.Parse(args[1]), seed = int.Parse(args[2]), size = 0;
        for (int i = 3; i < args.Length; ++i)
            if (args[i] == "--size") size = int.Parse(args[++i]);
        DMesh3 a; List<DMesh3> bs;
        Workload(kind, n, seed, size, out a, out bs);
        Warm();
        var info = new Dictionary<string, object> { ["case"] = $"{kind}_{size}_{n}_{seed}", ["a_tris"] = a.TriangleCount,
            ["b_tris"] = bs.Sum(b => b.TriangleCount), ["n"] = n, ["cores"] = Environment.ProcessorCount };
        string[] serialHash = new string[n], parallelHash = new string[n];
        double[] serialVol = new double[n];
        foreach (string variant in new[] { "serial", "parallel" }) {
            Gc();
            var row = new Dictionary<string, object>(info) { ["variant"] = variant };
            var results = new DMesh3[n];
            var phases = new Dictionary<string, long>[n];
            var notClean = new int[n];
            var sw = Stopwatch.StartNew();
            Action<int> one = (i) => {
                var op = new ExactMeshBoolean(a, bs[i], ExactMeshBoolean.Operations.Intersection);
                if (op.Compute() == false) throw new Exception(op.Error);
                results[i] = op.Result; phases[i] = op.PhaseMilliseconds; notClean[i] = op.RoundingClean ? 0 : 1;
            };
            if (variant == "serial") for (int i = 0; i < n; ++i) one(i);
            else Parallel.For(0, n, one);
            row["ms"] = sw.ElapsedMilliseconds;
            var sum = new Dictionary<string, long>();
            foreach (var p in phases) foreach (var kv in p) sum[kv.Key] = (sum.TryGetValue(kv.Key, out long s) ? s : 0) + kv.Value;
            row["phases_ms_sum"] = sum;
            row["not_clean"] = notClean.Sum();
            row["unclean_reason"] = results.Select(m => MeshRobustRepair.NotCleanReason(m)).Count(s => s != "" && s != "empty");
            row["empty"] = results.Count(m => m.TriangleCount == 0);
            row["result_tris"] = results.Sum(m => m.TriangleCount);
            for (int i = 0; i < n; ++i) {
                if (variant == "serial") { serialHash[i] = Hash(results[i]); serialVol[i] = Volume(results[i]); }
                else parallelHash[i] = Hash(results[i]);
            }
            if (variant == "parallel")
                row["hashes_equal_serial"] = Enumerable.Range(0, n).All(i => serialHash[i] == parallelHash[i]);
            row["volume_sum"] = serialVol.Sum();
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        {
            // the fixed cost of A alone: A n a small box outside A's box (setup, trees, orientation and closedness of A)
            Gc();
            AxisAlignedBox3d box = a.CachedBounds;
            var far = new TrivialBox3Generator() { Box = new Box3d(box.Max + new Vector3d(10, 10, 10), new Vector3d(1, 1, 1)) }.Generate().MakeDMesh();
            var row = new Dictionary<string, object>(info) { ["variant"] = "a_alone" };
            var times = new List<long>();
            Dictionary<string, long> ph = null;
            for (int k = 0; k < 5; ++k) {
                var sw = Stopwatch.StartNew();
                var op = new ExactMeshBoolean(a, far, ExactMeshBoolean.Operations.Intersection);
                op.Compute();
                times.Add(sw.ElapsedMilliseconds); ph = op.PhaseMilliseconds;
            }
            row["ms_runs"] = times; row["ms"] = times.Min(); row["phases_ms"] = ph;
            Console.WriteLine(JsonSerializer.Serialize(row));
        }
        return 0;
    }
}
