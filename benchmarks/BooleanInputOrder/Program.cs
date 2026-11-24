// The exact boolean, its rounding repair and SolidBooleanPipeline under reorderings of the input (the same triangles in
// another order, the same vertices with other ids), stage by stage: docs/research/BOOLEAN_INPUT_ORDER_2026-11.md.
// A measurement harness: no library change. The inputs are generated here (two nearly coincident curved plates).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using g3;
using gs;

static partial class Program
{
    const ExactMeshBoolean.Operations D = ExactMeshBoolean.Operations.Difference, I = ExactMeshBoolean.Operations.Intersection;

    // ---- inputs ----

    static double Bowl(double x, double y) => 0.3 * ((x - 0.5) * (x - 0.5) + (y - 0.5) * (y - 0.5));
    static double Wave(double x, double y) => 0.05 * Math.Sin(3 * x + 1) * Math.Cos(2 * y);
    static double Dome(double x, double y) => Math.Sqrt(4 - (x - 0.5) * (x - 0.5) - (y - 0.5) * (y - 0.5));
    static readonly Vector3d C = new Vector3d(0.5, 0.5, 0);

    // a curved plate: z = h(x, y) over the unit square on an n by n grid, thickness t along z, closed by side walls
    static DMesh3 Plate(int n, double t, Func<double, double, double> h, Func<Vector3d, Vector3d> map = null)
    {
        var m = new DMesh3();
        int N = n + 1;
        var top = new int[N, N];
        var bot = new int[N, N];
        for (int i = 0; i < N; ++i)
            for (int j = 0; j < N; ++j) {
                double x = (double)i / n, y = (double)j / n, z = h(x, y);
                Vector3d a = new Vector3d(x, y, z + t / 2), b = new Vector3d(x, y, z - t / 2);
                top[i, j] = m.AppendVertex(map != null ? map(a) : a);
                bot[i, j] = m.AppendVertex(map != null ? map(b) : b);
            }
        for (int i = 0; i < n; ++i)
            for (int j = 0; j < n; ++j) {
                m.AppendTriangle(top[i, j], top[i + 1, j], top[i + 1, j + 1]);
                m.AppendTriangle(top[i, j], top[i + 1, j + 1], top[i, j + 1]);
                m.AppendTriangle(bot[i, j], bot[i + 1, j + 1], bot[i + 1, j]);
                m.AppendTriangle(bot[i, j], bot[i, j + 1], bot[i + 1, j + 1]);
            }
        Action<int, int, int, int> wall = (a0, a1, b0, b1) => { m.AppendTriangle(a0, b0, b1); m.AppendTriangle(a0, b1, a1); };
        for (int i = 0; i < n; ++i) {
            wall(top[i, 0], top[i + 1, 0], bot[i, 0], bot[i + 1, 0]);
            wall(top[i + 1, n], top[i, n], bot[i + 1, n], bot[i, n]);
            wall(top[0, i + 1], top[0, i], bot[0, i + 1], bot[0, i]);
            wall(top[n, i], top[n, i + 1], bot[n, i], bot[n, i + 1]);
        }
        return m;
    }

    static Vector3d Rot(Vector3d p, double ax, double ay)
    {
        Vector3d q = p - C;
        double co = Math.Cos(ax), si = Math.Sin(ax);
        q = new Vector3d(q.x, co * q.y - si * q.z, si * q.y + co * q.z);
        co = Math.Cos(ay);
        si = Math.Sin(ay);
        q = new Vector3d(co * q.x + si * q.z, q.y, -si * q.x + co * q.z);
        return C + q;
    }

    sealed class Case
    {
        public string Name;
        public Func<DMesh3> A, B;
        public ExactMeshBoolean.Operations Op;
    }

    static readonly (string, Func<double, double, double>)[] Surfaces = { ("bowl", Bowl), ("wave", Wave), ("dome", Dome) };

    // family "plates": a plate against a slightly shrunk, turned, turned and shifted or retessellated copy (Difference),
    // and against a copy lifted by nearly its thickness (Intersection: a thin strip)
    static List<Case> Plates()
    {
        var cases = new List<Case>();
        foreach (int n in new[] { 6, 10, 14 })
            foreach (double t in new[] { 0.02, 0.002 })
                foreach (var (hn, h) in Surfaces) {
                    string pre = string.Format(CultureInfo.InvariantCulture, "{0} n{1} t{2:G1} ", hn, n, t);
                    Func<DMesh3> a = () => Plate(n, t, h);
                    foreach (double e in new[] { 1e-4, 1e-6, 1e-8, 1e-10 }) {
                        string es = e.ToString("G1", CultureInfo.InvariantCulture);
                        cases.Add(new Case { Name = pre + "shrunk " + es + " D", A = a, B = () => Plate(n, t, h, p => C + (1 - e) * (p - C)), Op = D });
                        cases.Add(new Case { Name = pre + "rot " + es + " D", A = a, B = () => Plate(n, t, h, p => Rot(p, e, 0.7 * e)), Op = D });
                        cases.Add(new Case { Name = pre + "rotshift " + es + " D", A = a, B = () => Plate(n, t, h, p => Rot(p, e, 0.7 * e) + e * new Vector3d(0.3, -0.2, 0.1)), Op = D });
                        cases.Add(new Case { Name = pre + "grid+1 rot " + es + " D", A = a, B = () => Plate(n + 1, t, h, p => Rot(p, e, 0.7 * e)), Op = D });
                        cases.Add(new Case { Name = pre + "rot " + es + " I-strip", A = a, B = () => Plate(n, t, h, p => Rot(p, e, 0.7 * e) + new Vector3d(0, 0, t * (1 - 16 * e))), Op = I });
                    }
                    cases.Add(new Case { Name = pre + "grid+1 D", A = a, B = () => Plate(n + 1, t, h), Op = D });
                    cases.Add(new Case { Name = pre + "grid+2 D", A = a, B = () => Plate(n + 2, t, h), Op = D });
                }
        return cases;
    }

    // family "tessellations": two tessellations of one plate, one turned by 1e-5 .. 1e-8, both ways round
    static List<Case> Tessellations()
    {
        var cases = new List<Case>();
        const double t = 0.02;
        foreach (int n in new[] { 5, 6, 7, 8 })
            foreach (var (hn, h) in Surfaces)
                foreach (int dn in new[] { 1, 2 })
                    for (int k = 0; k <= 12; ++k) {
                        double e = Math.Pow(10, -5 - k * 0.25);
                        string name = string.Format(CultureInfo.InvariantCulture, "{0} n{1} grid+{2} rot {3:G3} ", hn, n, dn, e);
                        Func<DMesh3> coarse = () => Plate(n, t, h), fine = () => Plate(n + dn, t, h, p => Rot(p, e, 0.7 * e));
                        cases.Add(new Case { Name = name + "D", A = coarse, B = fine, Op = D });
                        cases.Add(new Case { Name = name + "Drev", A = fine, B = coarse, Op = D });
                    }
        return cases;
    }

    // ---- reorderings: 0 as given, -1 reversed, any other value the seed of a shuffle ----

    static int[] Ordered(IEnumerable<int> ids, int order)
    {
        int[] a = ids.ToArray();
        if (order == -1)
            Array.Reverse(a);
        else if (order != 0) {
            ulong s = (ulong)order * 0x9E3779B97F4A7C15UL + 1;
            for (int i = a.Length - 1; i > 0; --i) {
                s = s * 6364136223846793005UL + 1442695040888963407UL;
                int j = (int)((s >> 33) % (ulong)(i + 1));
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
        return a;
    }

    static DMesh3 Reordered(DMesh3 mesh, int triangleOrder, int vertexOrder)
    {
        var map = new Dictionary<int, int>();
        var r = new DMesh3();
        foreach (int v in Ordered(mesh.VertexIndices(), vertexOrder))
            map[v] = r.AppendVertex(mesh.GetVertex(v));
        foreach (int t in Ordered(mesh.TriangleIndices(), triangleOrder)) {
            Index3i f = mesh.GetTriangle(t);
            r.AppendTriangle(map[f.a], map[f.b], map[f.c]);
        }
        return r;
    }

    // ---- what is compared ----

    static int Cmp(Vector3d a, Vector3d b)
    {
        int c = a.x.CompareTo(b.x);
        if (c != 0) return c;
        c = a.y.CompareTo(b.y);
        return c != 0 ? c : a.z.CompareTo(b.z);
    }

    static string Bits(Vector3d p) => BitConverter.DoubleToInt64Bits(p.x).ToString("x16") + BitConverter.DoubleToInt64Bits(p.y).ToString("x16") + BitConverter.DoubleToInt64Bits(p.z).ToString("x16");

    // the triangles as a set of positions: a count and a hash; the same for two meshes with the same oriented triangles
    static string TriangleSet(DMesh3 m)
    {
        if (m == null)
            return "none";
        var keys = new List<string>();
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            Vector3d[] p = { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
            int k = 0;
            for (int i = 1; i < 3; ++i)
                if (Cmp(p[i], p[k]) < 0) k = i;
            keys.Add(Bits(p[k]) + Bits(p[(k + 1) % 3]) + Bits(p[(k + 2) % 3]));
        }
        keys.Sort(string.CompareOrdinal);
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return m.TriangleCount + ":" + Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.ASCII.GetBytes(string.Join("", keys)))).Substring(0, 10);
    }

    // the volume, summed in an order that does not depend on the ids
    static double Volume(DMesh3 m)
    {
        if (m == null)
            return double.NaN;
        var terms = new List<double>();
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            Vector3d[] p = { m.GetVertex(f.a), m.GetVertex(f.b), m.GetVertex(f.c) };
            int k = 0;
            for (int i = 1; i < 3; ++i)
                if (Cmp(p[i], p[k]) < 0) k = i;
            terms.Add(p[k].Dot(p[(k + 1) % 3].Cross(p[(k + 2) % 3])));
        }
        terms.Sort();
        double v = 0;
        foreach (double x in terms) v += x;
        return v / 6;
    }

    static string Edits(MeshRoundingRepair r)
    {
        return r == null ? "-" : string.Format(CultureInfo.InvariantCulture,
            "defects {0}/{1}/{2} left {3}/{4}/{5} collapses {6} flips {7} moves {8} zips {9} strips {10} search {11} removed {12} resolvable {13} limit {14} {15}",
            r.InitialIntersections, r.InitialDegenerate, r.InitialFalseContacts, r.RemainingIntersections, r.RemainingDegenerate, r.RemainingFalseContacts,
            r.Collapses, r.Flips, r.Moves, r.Zips, r.StripZips, r.PositionSearchMoves, r.RemovedComponents, r.RemovedResolvable, r.EvaluationLimitReached, r.IsClean ? "clean" : "NOT CLEAN");
    }

    // ---- the repair on the exact result numbered by its geometry (the upper bound of option A) ----

    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    // the vertices sorted by position (those at one position by the positions of their triangles), each triangle's corners
    // rotated to start at its smallest vertex, the triangles sorted; ties: vertices the geometry does not tell apart
    static DMesh3 NumberedByGeometry(DMesh3 m, out int[] newToOld, ref int ties)
    {
        var ring = new Dictionary<int, string>();
        foreach (int v in m.VertexIndices()) {
            var l = new List<string>();
            foreach (int t in m.VtxTrianglesItr(v)) {
                Index3i f = m.GetTriangle(t);
                int k = f.a == v ? 0 : f.b == v ? 1 : 2;
                l.Add(Bits(m.GetVertex(f[(k + 1) % 3])) + Bits(m.GetVertex(f[(k + 2) % 3])));
            }
            l.Sort(string.CompareOrdinal);
            ring[v] = string.Join("|", l);
        }
        int[] vs = m.VertexIndices().ToArray();
        Comparison<int> cmp = (x, y) => { int c = Cmp(m.GetVertex(x), m.GetVertex(y)); return c != 0 ? c : string.CompareOrdinal(ring[x], ring[y]); };
        Array.Sort(vs, cmp);
        for (int j = 1; j < vs.Length; ++j)
            if (cmp(vs[j - 1], vs[j]) == 0) ties++;
        var map = new Dictionary<int, int>();
        var r = new DMesh3();
        foreach (int v in vs)
            map[v] = r.AppendVertex(m.GetVertex(v));
        var tris = new List<Index3i>();
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            int[] c = { map[f.a], map[f.b], map[f.c] };
            int k = 0;
            for (int j = 1; j < 3; ++j)
                if (c[j] < c[k]) k = j;
            tris.Add(new Index3i(c[k], c[(k + 1) % 3], c[(k + 2) % 3]));
        }
        tris.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b != y.b ? x.b.CompareTo(y.b) : x.c.CompareTo(y.c));
        foreach (Index3i f in tris)
            if (r.AppendTriangle(f) < 0) throw new InvalidOperationException("a triangle could not be appended");
        newToOld = vs;
        return r;
    }

    // the repair exactly as ExactMeshBoolean.Compute configures it with StrictRounding (as the pipeline runs it), on the
    // result of a run without SafeRounding; the boolean's private per-vertex data and the repair's internal hooks by reflection
    static MeshRoundingRepair Repair(ExactMeshBoolean exact, DMesh3 a, DMesh3 b, bool numbered, ref int ties, out DMesh3 result)
    {
        bool[] fixedV = (bool[])typeof(ExactMeshBoolean).GetField("resultFixed", Hidden).GetValue(exact);
        int[] pointId = (int[])typeof(ExactMeshBoolean).GetField("resultPointId", Hidden).GetValue(exact);
        DMesh3 m = new DMesh3(exact.Result);
        Func<int, int> old = v => v;
        if (numbered) {
            int[] newToOld;
            m = NumberedByGeometry(m, out newToOld, ref ties);
            old = v => v < newToOld.Length ? newToOld[v] : int.MaxValue;
        }
        Func<int, bool> isFixed = v => old(v) < fixedV.Length && fixedV[old(v)];
        var r = new MeshRoundingRepair(m) {
            SharedPositionsTouch = true, FixedVertexF = isFixed, FixedWhereCoarse = false, UlpFactor = 0, AbsoluteTolerance = 0,
            MaxDisplacement = exact.RoundingMaxDisplacement, MaxPairTests = exact.RoundingMaxPairTests,
            PairTestsDefectScale = exact.RoundingPairTestsDefectScale, PairTestsMaxGrowth = exact.RoundingPairTestsMaxGrowth
        };
        Type type = typeof(MeshRoundingRepair);
        type.GetField("PointIdF", Hidden).SetValue(r, (Func<int, int>)(v => old(v) < pointId.Length ? pointId[old(v)] : -1));
        type.GetField("ExactVertexF", Hidden).SetValue(r, isFixed);
        var thin = type.GetMethod("IsThinOrTiny", BindingFlags.NonPublic | BindingFlags.Static);
        bool keep = (bool)thin.Invoke(null, new object[] { a, exact.RoundingMaxDisplacement, 1e-12 }) || (bool)thin.Invoke(null, new object[] { b, exact.RoundingMaxDisplacement, 1e-12 });
        type.GetField("KeepWhenEmptied", Hidden).SetValue(r, keep);
        r.Apply();
        result = m;
        return r;
    }

    // the cost of a numbering by position alone (a sort of the vertices and one of the triangles), in ticks
    static long SortTicks(DMesh3 m)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int[] vs = m.VertexIndices().ToArray();
        var pos = new Vector3d[m.MaxVertexID];
        foreach (int v in vs) pos[v] = m.GetVertex(v);
        Array.Sort(vs, (x, y) => Cmp(pos[x], pos[y]));
        var rank = new int[m.MaxVertexID];
        for (int i = 0; i < vs.Length; ++i) rank[vs[i]] = i;
        var tris = new List<Index3i>(m.TriangleCount);
        foreach (int t in m.TriangleIndices()) {
            Index3i f = m.GetTriangle(t);
            tris.Add(new Index3i(rank[f.a], rank[f.b], rank[f.c]));
        }
        tris.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b != y.b ? x.b.CompareTo(y.b) : x.c.CompareTo(y.c));
        return sw.ElapsedTicks;
    }

    // ---- timing: the default-mode boolean on larger inputs, for a before / after of a library change ----

    // a box [0, w]^2 x [0, h] with every face on an n by n grid (flat regions with many points on common circles)
    static DMesh3 GridBox(int n, double w, double h, Vector3d at)
    {
        var m = new DMesh3();
        var ids = new Dictionary<(long, long, long), int>();
        Func<double, double, double, int> vertex = (x, y, z) => {
            var key = ((long)Math.Round(x * n), (long)Math.Round(y * n), (long)Math.Round(z * n));
            if (ids.TryGetValue(key, out int id) == false)
                ids[key] = id = m.AppendVertex(at + new Vector3d(x * w, y * w, z * h));
            return id;
        };
        // (u, v) over a face; outward orientation by the flag
        Action<Func<double, double, int>, bool> face = (corner, turn) => {
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j) {
                    double u0 = (double)i / n, u1 = (double)(i + 1) / n, v0 = (double)j / n, v1 = (double)(j + 1) / n;
                    int a = corner(u0, v0), b = corner(u1, v0), c = corner(u1, v1), d = corner(u0, v1);
                    if (turn) { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                    else { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                }
        };
        face((u, v) => vertex(u, v, 0), true);
        face((u, v) => vertex(u, v, 1), false);
        face((u, v) => vertex(u, 0, v), false);
        face((u, v) => vertex(u, 1, v), true);
        face((u, v) => vertex(0, u, v), true);
        face((u, v) => vertex(1, u, v), false);
        return m;
    }

    static DMesh3 Sphere(int edgeVertices, Vector3d at)
    {
        DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        MeshTransforms.Translate(s, at);
        return s;
    }

    static int Timing(int repeats)
    {
        var U = ExactMeshBoolean.Operations.Union;
        var cases = new List<Case> {
            new Case { Name = "spheres 40, union", A = () => Sphere(40, Vector3d.Zero), B = () => Sphere(40, new Vector3d(0.7, 0.3, 0.2)), Op = U },
            new Case { Name = "spheres 40, difference", A = () => Sphere(40, Vector3d.Zero), B = () => Sphere(40, new Vector3d(0.7, 0.3, 0.2)), Op = D },
            new Case { Name = "grid boxes 24, coplanar faces, union", A = () => GridBox(24, 1, 1, Vector3d.Zero), B = () => GridBox(24, 1, 1, new Vector3d(0.5, 0.25, 0)), Op = U },
            new Case { Name = "grid boxes 24 and 20, coplanar faces, difference", A = () => GridBox(24, 1, 1, Vector3d.Zero), B = () => GridBox(20, 1, 0.5, new Vector3d(0.3, 0.3, 0.5)), Op = D },
            new Case { Name = "grid boxes 24, shifted, intersection", A = () => GridBox(24, 1, 1, Vector3d.Zero), B = () => GridBox(24, 1, 1, new Vector3d(0.31, 0.17, 0.23)), Op = I },
            new Case { Name = "dome plates 40, turned 1e-6, difference", A = () => Plate(40, 0.02, Dome), B = () => Plate(40, 0.02, Dome, p => Rot(p, 1e-6, 0.7e-6)), Op = D },
            new Case { Name = "wave plates 40 and 41, difference", A = () => Plate(40, 0.02, Wave), B = () => Plate(41, 0.02, Wave), Op = D },
            new Case { Name = "bowl plates 40, strip, intersection", A = () => Plate(40, 0.02, Bowl), B = () => Plate(40, 0.02, Bowl, p => Rot(p, 1e-6, 0.7e-6) + new Vector3d(0, 0, 0.02 * (1 - 16e-6))), Op = I },
        };
        Func<List<double>, double> median = l => { l.Sort(); return l[l.Count / 2]; };
        Console.WriteLine("case | input triangles | result triangles | removed vertices | total ms | simplify ms | order ms | rounding ms | result");
        double sumTotal = 0, sumOrder = 0, sumSimplify = 0;
        foreach (Case cs in cases) {
            DMesh3 a = cs.A(), b = cs.B();
            var total = new List<double>();
            var simplify = new List<double>();
            var order = new List<double>();
            var rounding = new List<double>();
            ExactMeshBoolean last = null;
            for (int r = 0; r <= repeats; ++r) {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                last = new ExactMeshBoolean(a, b, cs.Op);
                last.Compute();
                double ms = sw.ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (r == 0)
                    continue;       // (the first run warms up)
                total.Add(ms);
                simplify.Add(last.PhaseMilliseconds.TryGetValue("assembly.simplify_us", out long su) ? su / 1000.0 : 0);
                order.Add(last.PhaseMilliseconds.TryGetValue("assembly.order_us", out long ou) ? ou / 1000.0 : 0);
                rounding.Add(last.PhaseMilliseconds.TryGetValue("rounding", out long ro) ? ro : 0);
            }
            double t = median(total), s = median(simplify), o = median(order);
            sumTotal += t;
            sumOrder += o;
            sumSimplify += s;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1}+{2} | {3} | {4} | {5:F1} | {6:F2} | {7:F2} | {8:F0} | {9} {10}",
                cs.Name, a.TriangleCount, b.TriangleCount, last.Result != null ? last.Result.TriangleCount : 0, last.RemovedVertices, t, s, o, median(rounding),
                last.Status, TriangleSet(last.Result)));
            Console.Out.Flush();
        }
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "sum of medians: total {0:F1} ms, simplify {1:F2} ms, order {2:F2} ms ({3:P2} of the total)", sumTotal, sumSimplify, sumOrder, sumOrder / sumTotal));
        return 0;
    }

    // ---- corpus: how many results of the generated degenerate families are clean (a before / after of a library change) ----

    static int Corpus()
    {
        var U = ExactMeshBoolean.Operations.Union;
        int allClean = 0, allStrict = 0, all = 0;
        foreach (string frame in new[] { "identity", "rotated, scale 1e-3, utm", "rotated" }) {
            var sim = geometry3Sharp.Tests.Similarity.ByName(frame);
            int n = 0, clean = 0, strict = 0;
            foreach (var f in new[] { geometry3Sharp.Tests.DegenerateFamilies.Family.Cluster, geometry3Sharp.Tests.DegenerateFamilies.Family.FoldedCluster,
                                      geometry3Sharp.Tests.DegenerateFamilies.Family.Collinear, geometry3Sharp.Tests.DegenerateFamilies.Family.Sliver })
                foreach (int k in new[] { 0, 30, 36, 40, 44, 48, 52 })
                    foreach (int count in new[] { 2, 4, 8 })
                        for (int seed = 0; seed < 6; seed++) {
                            if (k == 0 && f == geometry3Sharp.Tests.DegenerateFamilies.Family.Sliver)
                                continue;
                            DMesh3 mesh = geometry3Sharp.Tests.Invariance.Transformed(geometry3Sharp.Tests.DegenerateFamilies.Make(f, k, seed, count), sim);
                            n++;
                            var op = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), U);
                            if (op.Compute() && op.RoundingClean && MeshRobustRepair.NotCleanReasons(op.Result) == "") clean++;
                            var st = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), U) { StrictRounding = true };
                            if (st.Compute()) strict++;
                        }
            Console.WriteLine($"{frame}: cases {n}, default mode clean {clean}, StrictRounding succeeds {strict}");
            Console.Out.Flush();
            all += n;
            allClean += clean;
            allStrict += strict;
        }
        Console.WriteLine($"all frames: cases {all}, default mode clean {allClean}, StrictRounding succeeds {allStrict}");
        return 0;
    }

    // ---- the run ----

    static void Add(SortedDictionary<string, int> d, string key) { d[key] = d.TryGetValue(key, out int n) ? n + 1 : 1; }


    // ---- search: a seeded sample of a wide procedural space for results that leave only degenerate triangles ----

    // the space: surface, grid n, grid offset, thickness, rotation e (1e-5 .. 1e-9) and its axis ratio, a shift of both inputs
    // away from the origin, the operation; per case the state of the repair of the default mode (ExactMeshBoolean, SafeRounding)
    static int Search(string[] args)
    {
        int count = 2000, seed = 1;
        bool tiny = false;
        int only = -1;
        string oneCase = null;
        int forcedN = 0;
        bool pipeline = false, verify = false;
        int controls = 0;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--pipeline") pipeline = true;
            else if (args[i] == "--verify") { verify = true; pipeline = true; }
            else if (args[i] == "--controls") controls = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--count") count = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--seed") seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--tiny") tiny = true;
            else if (args[i] == "--case") oneCase = args[++i];
            else if (args[i] == "--n") forcedN = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--only") only = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else { Console.WriteLine("unknown argument " + args[i]); return 2; }
        }
        var rnd = new Random(seed);
        var classes = new SortedDictionary<string, int>();
        int smallest = int.MaxValue;
        var pipeTally = new SortedDictionary<string, int>();
        double[] maxVolDiff = { 0, 0 };
        for (int c = 0; c < count; ++c) {
            var (hn, h) = Surfaces[rnd.Next(Surfaces.Length)];
            int n = tiny ? rnd.Next(1, 4) : rnd.Next(3, 9), dn = rnd.Next(1, 4);
            double t = new[] { 0.02, 0.002, 0.2 }[rnd.Next(3)];
            double e = Math.Pow(10, -5 - 4 * rnd.NextDouble());
            double ratio = new[] { 0.7, 0.3, 1.9, -1.1 }[rnd.Next(4)];
            double shift = new[] { 0.0, 0.0, 100.0, 1e4 }[rnd.Next(4)];
            bool diff = rnd.Next(2) == 0;
            if (forcedN > 0) n = forcedN;
            if (oneCase != null) {
                // "surface,n,dn,t,e,ratio,shift,D|Drev"
                string[] f = oneCase.Split(',');
                (hn, h) = Surfaces.First(x => x.Item1 == f[0]);
                n = int.Parse(f[1], CultureInfo.InvariantCulture); dn = int.Parse(f[2], CultureInfo.InvariantCulture);
                t = double.Parse(f[3], CultureInfo.InvariantCulture); e = double.Parse(f[4], CultureInfo.InvariantCulture);
                ratio = double.Parse(f[5], CultureInfo.InvariantCulture); shift = double.Parse(f[6], CultureInfo.InvariantCulture);
                diff = f[7] == "D";
                if (c > 0) break;
            }
            Func<Vector3d, Vector3d> sh = p => p + new Vector3d(shift, shift * 0.5, -shift * 0.25);
            Func<DMesh3> coarse = () => Plate(n, t, h, sh), fine = () => Plate(n + dn, t, h, p => sh(Rot(p, e, ratio * e)));
            DMesh3 a = diff ? coarse() : fine(), b = diff ? fine() : coarse();
            string name = string.Format(CultureInfo.InvariantCulture, "{0} n{1} grid+{2} t{3:G2} rot {4:G3} ratio {5} shift {6} {7} --case {0},{1},{2},{3:R},{4:R},{5:R},{6:R},{7}", hn, n, dn, t, e, ratio, shift, diff ? "D" : "Drev");
            if (only >= 0 && only != c) continue;
            var plain = new ExactMeshBoolean(a, b, D);
            string cls;
            try {
                plain.Compute();
                MeshRoundingRepair r = plain.Rounding;
                if (r == null) cls = "no repair";
                else if (r.IsClean) cls = "clean";
                else {
                    var parts = new List<string>();
                    if (r.RemainingDegenerate > 0) parts.Add("degenerate");
                    if (r.RemainingIntersections > 0) parts.Add("intersections");
                    if (r.RemainingFalseContacts > 0) parts.Add("contacts");
                    if (r.RemovedResolvable > 0) parts.Add("removed-resolvable");
                    if (r.EvaluationLimitReached) parts.Add("limit");
                    cls = parts.Count == 0 ? "other" : string.Join("+", parts);
                    if (cls == "degenerate") {
                        int tris = a.TriangleCount + b.TriangleCount;
                        if (tris < smallest) smallest = tris;
                        // (the same input in other orders: the state does not depend on them since #1464)
                        int same = 0;
                        foreach (var (to, vo) in new[] { (-1, 0), (0, -1), (-1, -1), (101, 102), (103, 104) }) {
                            var other = new ExactMeshBoolean(Reordered(a, to, vo), Reordered(b, to, vo), D);
                            other.Compute();
                            if (other.Rounding != null && other.Rounding.RemainingDegenerate == r.RemainingDegenerate && TriangleSet(other.Result) == TriangleSet(plain.Result)) same++;
                        }
                        Add(classes, "  hit in 5 other orders: same result " + same);
                        Console.WriteLine($"HIT #{c} {name} tris {a.TriangleCount}+{b.TriangleCount} left deg {r.RemainingDegenerate} rejected drift {r.SmallestRejectedDrift:G3} | {Edits(r)}");
                    }
                }
            } catch (Exception ex) { cls = "exception " + ex.GetType().Name; }
            if (pipeline && (cls.Contains("degenerate") || cls == "removed-resolvable")) {
                // the pipeline on the same inputs: without the automatic steps, and with the default
                string group = cls.Contains("degenerate") ? "degenerate" : "removed-resolvable";
                double vPlain = Volume(plain.Result), vInput = Math.Abs(Volume(a));
                foreach (bool dflt in new[] { false, true }) {
                    var pipe = new SolidBooleanPipeline(a, b, D);
                    if (!dflt) pipe.AutomaticSteps = SolidBooleanPipeline.AutomaticStepModes.None;
                    pipe.Compute();
                    var rep = pipe.Report;
                    string routeText = string.Join(">", rep.Route.Select(x => x.Name));
                    SolidBooleanRouteStep grid = rep.Route.FirstOrDefault(x => x.Name == "Grid");
                    string outcome;
                    string reason = pipe.Result != null ? MeshRobustRepair.NotCleanReason(pipe.Result) : "no result";
                    double relDiff = double.NaN;
                    if (pipe.Status == SolidBooleanPipeline.PipelineStatus.Success) {
                        relDiff = Math.Abs(Volume(pipe.Result) - vPlain) / Math.Max(Math.Abs(vPlain), 1e-300);
                        outcome = (grid != null ? "success via Grid" : "success without Grid") + (rep.Clean ? ", clean" : rep.OnlyContacts ? ", only contacts" : ", not clean");
                        if (grid != null) maxVolDiff[1] = Math.Max(maxVolDiff[1], relDiff);
                        else if (dflt) maxVolDiff[0] = Math.Max(maxVolDiff[0], relDiff);
                    }
                    else if (grid != null && grid.Change != null && grid.Change.Contains("1/64")) outcome = "stopped by the size guard";
                    else if (grid != null && grid.Change != null && grid.Change.Contains("already")) outcome = "no retry: inputs on the cell";
                    else if (grid != null && grid.Change != null && grid.Change.Contains("it failed too")) outcome = "retry failed too";
                    else if (grid != null && grid.Change != null && grid.Change.Contains("its result is empty")) outcome = "retry result empty, rejected";
                    else if (grid != null) outcome = "grid step, other (" + pipe.Status + "/" + pipe.BooleanStatus + ")";
                    else outcome = pipe.Status + "/" + pipe.BooleanStatus;
                    Add(pipeTally, group + " | " + (dflt ? "default" : "None") + " | " + outcome);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "PIPE #{0} {1} | {2} {3}/{4} | route {5} | grid {6} | clean {7} onlyContacts {8} | reason '{9}' | rel volume diff {10:G3} | volume diff of input 0 {12:G3} (non-strict result {13:G3}) | {11}",
                        c, group, dflt ? "default" : "None", pipe.Status, pipe.BooleanStatus, routeText, grid != null ? grid.Grid.ToString("R", CultureInfo.InvariantCulture) : "-",
                        pipe.Result != null && rep.Clean, rep.OnlyContacts, reason, relDiff, grid != null ? grid.Change : "",
                        pipe.Result != null ? Math.Abs(Volume(pipe.Result) - vPlain) / vInput : double.NaN, vPlain));
                }
            }
            if (verify && (cls.Contains("degenerate") || cls == "removed-resolvable"))
                VerifyCase(c, name, cls.Contains("degenerate") ? "degenerate" : "removed", a, b, D);
            else if (verify && cls == "clean" && controls > 0) {
                controls--;
                VerifyCase(c, name, "control", a, b, D);
            }
            if (oneCase != null) {
                // the error text of StrictRounding (what SolidBooleanPipeline reports as RoundingNotClean)
                var strict = new ExactMeshBoolean(a, b, D) { StrictRounding = true };
                bool ok = strict.Compute();
                Console.WriteLine($"StrictRounding: {(ok ? "Success" : strict.Status.ToString())} {strict.Error}");
            }
            Add(classes, cls);
        }
        Console.WriteLine($"search: cases {count}, seed {seed}, tiny {tiny}");
        foreach (var k in classes) Console.WriteLine($"   {k.Key}: {k.Value}");
        if (pipeline) {
            Console.WriteLine("pipeline (group | mode | outcome):");
            foreach (var k in pipeTally) Console.WriteLine($"   {k.Key}: {k.Value}");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   largest relative volume difference of a default success via Grid: {0:G3}; of a default success without Grid: {1:G3}", maxVolDiff[1], maxVolDiff[0]));
        }
        return 0;
    }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "corpus")
            return Corpus();
        if (args.Length > 0 && args[0] == "families")
            return Families();
        if (args.Length > 0 && args[0] == "search")
            return Search(args);
        if (args.Length > 0 && args[0] == "timing")
            return Timing(args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5);
        if (args.Length == 0 || (args[0] != "plates" && args[0] != "tessellations")) {
            Console.WriteLine("usage: BooleanInputOrder search [--count N] [--seed S] [--tiny] [--n N] [--only I] [--case spec] [--pipeline]\n       BooleanInputOrder plates|tessellations [--orders N] [--filter text] [--stages] [--numbered]\n       BooleanInputOrder timing [repeats]\n       BooleanInputOrder corpus");
            return 2;
        }
        int nOrders = 8;
        string filter = "";
        bool stages = false, numbered = false;
        for (int i = 1; i < args.Length; ++i) {
            if (args[i] == "--orders") nOrders = int.Parse(args[++i], CultureInfo.InvariantCulture);
            else if (args[i] == "--filter") filter = args[++i];
            else if (args[i] == "--stages") stages = true;
            else if (args[i] == "--numbered") numbered = true;
            else { Console.WriteLine("unknown argument " + args[i]); return 2; }
        }
        // (triangle order, vertex order): as given, each reversed, both reversed, then shuffles
        var orders = new List<(int, int)> { (0, 0), (-1, 0), (0, -1), (-1, -1) };
        for (int k = 0; orders.Count < nOrders; ++k)
            orders.Add((101 + 2 * k, 102 + 2 * k));
        if (orders.Count > nOrders)
            orders.RemoveRange(nOrders, orders.Count - nOrders);

        int cases = 0, piecesDiffer = 0, exactDiffer = 0, roundedDiffer = 0, statusDiffer = 0, cleanDiffer = 0, spread4 = 0, spread2 = 0, emptied = 0;
        int numberedCases = 0, numberedMany = 0, mismatches = 0, tiesTotal = 0;
        long booleanTicks = 0, sortTicks = 0;
        var transitions = new SortedDictionary<string, int>();
        foreach (Case cs in args[0] == "plates" ? Plates() : Tessellations()) {
            if (filter != "" && cs.Name.Contains(filter) == false)
                continue;
            DMesh3 a = cs.A(), b = cs.B();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var pieces = new HashSet<string>();
            var exactSets = new HashSet<string>();
            var rounded = new HashSet<string>();
            var numberedSets = new SortedDictionary<string, int>();
            var status = new SortedDictionary<string, int>();
            var edits = new SortedDictionary<string, int>();
            double vmin = double.MaxValue, vmax = double.MinValue, vexact = 0;
            int clean = 0, strictClean = 0, ties = 0;
            foreach (var (to, vo) in orders) {
                DMesh3 pa = Reordered(a, to, vo), pb = Reordered(b, to, vo);
                var exact = new ExactMeshBoolean(pa, pb, cs.Op) { SafeRounding = false };
                exact.Compute();
                exactSets.Add(TriangleSet(exact.Result));
                vexact = Volume(exact.Result);
                if (stages) {
                    var arr = new ExactMeshBoolean(pa, pb, cs.Op) { SafeRounding = false, SimplifyFlatRegions = false };
                    arr.Compute();
                    pieces.Add(TriangleSet(arr.Result));
                    Console.WriteLine($"      order ({to},{vo}): pieces {TriangleSet(arr.Result)} exact {TriangleSet(exact.Result)} removed vertices {exact.RemovedVertices}");
                }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var plain = new ExactMeshBoolean(pa, pb, cs.Op);
                plain.Compute();
                booleanTicks += clock.ElapsedTicks;
                sortTicks += SortTicks(exact.Result);
                rounded.Add(TriangleSet(plain.Result));
                Add(edits, Edits(plain.Rounding));
                if (plain.RoundingClean) clean++;
                double v = Volume(plain.Result);
                vmin = Math.Min(vmin, v);
                vmax = Math.Max(vmax, v);
                var pipe = new SolidBooleanPipeline(pa, pb, cs.Op);
                pipe.Compute();
                Add(status, pipe.Status + "/" + pipe.BooleanStatus);
                if (numbered) {
                    var strict = new ExactMeshBoolean(pa, pb, cs.Op) { StrictRounding = true };
                    bool ok = strict.Compute();
                    int unused = 0;
                    MeshRoundingRepair today = Repair(exact, pa, pb, false, ref unused, out DMesh3 todayMesh);
                    // (the emulation must be the library's repair: the same verdict and, where there is a result, the same mesh)
                    if (today.IsClean != ok || (ok && TriangleSet(todayMesh) != TriangleSet(strict.Result))) mismatches++;
                    if (ok) strictClean++;
                    MeshRoundingRepair r = Repair(exact, pa, pb, true, ref ties, out DMesh3 numberedMesh);
                    Add(numberedSets, (r.IsClean ? "clean " : "not-clean ") + TriangleSet(numberedMesh));
                }
            }
            cases++;
            double spread = vmax > vmin ? (vmax - vmin) / Math.Abs(vmax) : 0;
            if (pieces.Count > 1) piecesDiffer++;
            if (exactSets.Count > 1) exactDiffer++;
            if (rounded.Count > 1) roundedDiffer++;
            if (status.Count > 1) statusDiffer++;
            if (clean != 0 && clean != orders.Count) cleanDiffer++;
            if (spread > 1e-4) spread4++;
            if (spread > 1e-2) spread2++;
            if (vmin == 0 && vmax != 0) emptied++;
            string flag = status.Count > 1 ? "STATUS" : rounded.Count > 1 ? "MESH" : "same";
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0,-6} {1,-36} tris {2}+{3} {4,4} ms/order | exact results {5} volume {6:G6} | pipeline {7} | default mode: clean {8}/{9}, results {10}, volume spread {11:G3}",
                flag, cs.Name, a.TriangleCount, b.TriangleCount, sw.ElapsedMilliseconds / orders.Count, exactSets.Count, vexact,
                string.Join(", ", status.Select(k => k.Key + " x" + k.Value)), clean, orders.Count, rounded.Count, spread));
            if (status.Count > 1 || edits.Count > 1 || clean != orders.Count)
                foreach (var k in edits)
                    Console.WriteLine("         x" + k.Value + " " + k.Key);
            if (numbered) {
                numberedCases++;
                tiesTotal += ties;
                if (numberedSets.Count > 1) numberedMany++;
                bool numberedClean = numberedSets.Keys.First().StartsWith("clean");
                string today = strictClean == orders.Count ? "clean in every order" : strictClean == 0 ? "not clean in any order" : "clean in some orders";
                Add(transitions, today + " -> " + (numberedSets.Count > 1 ? "several results" : numberedClean ? "clean" : "not clean"));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "   numbered: results {0} ({1}) | today clean {2}/{3} | ties {4}",
                    numberedSets.Count, numberedClean ? "clean" : "not clean", strictClean, orders.Count, ties));
            }
            Console.Out.Flush();
        }
        Console.WriteLine();
        Console.WriteLine($"cases {cases}, orders {orders.Count}");
        if (stages) Console.WriteLine($"kept pieces differ between orders: {piecesDiffer}");
        Console.WriteLine($"exact result (triangle set) differs between orders: {exactDiffer}");
        Console.WriteLine($"default-mode result (triangle set) differs between orders: {roundedDiffer}");
        Console.WriteLine($"default-mode volume differs by more than 1e-4 relative: {spread4}, by more than 1e-2: {spread2}; empty in some orders only: {emptied}");
        Console.WriteLine($"RoundingClean differs between orders: {cleanDiffer}");
        Console.WriteLine($"pipeline status differs between orders: {statusDiffer}");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "a sort of the exact result's vertices and triangles by position: {0:F1} ms in total, the default-mode booleans {1:F0} ms ({2:P2})",
            sortTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, booleanTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency, (double)sortTicks / booleanTicks));
        if (numbered) {
            Console.WriteLine($"numbered by geometry: cases {numberedCases}, with more than one result {numberedMany}, vertices not told apart {tiesTotal}, runs where the emulation is not the library's repair {mismatches}");
            foreach (var k in transitions)
                Console.WriteLine($"   {k.Key}: {k.Value}");
        }
        return 0;
    }
}
