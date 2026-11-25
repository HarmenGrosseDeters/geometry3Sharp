// Idea 1: the valuation identity V(A u B) + V(A n B) = V(A) + V(B) (and chi likewise) as a tolerance-free check of
// the exact (pre-rounding) result of ExactMeshBoolean. Research probe: not library code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;

static class Valuation
{
    // an exact rational, reduced
    struct Q
    {
        public BigInteger N, D;
        public Q(BigInteger n, BigInteger d) { if (d.Sign < 0) { n = -n; d = -d; } N = n; D = d; }
        public static Q Zero => new Q(BigInteger.Zero, BigInteger.One);
        public static Q operator +(Q a, Q b)
        {
            if (a.D == b.D) return Red(a.N + b.N, a.D);
            return Red(a.N * b.D + b.N * a.D, a.D * b.D);
        }
        public static Q operator -(Q a) => new Q(-a.N, a.D);
        static Q Red(BigInteger n, BigInteger d)
        {
            if (n.IsZero) return Zero;
            BigInteger g = BigInteger.GreatestCommonDivisor(n, d);
            return g.IsOne ? new Q(n, d) : new Q(n / g, d / g);
        }
        public bool IsZero => N.IsZero;
        public double ToDouble() => (double)N / (double)D; // probe only (small values)
    }

    // the apex of the divergence theorem: a generic dyadic point, so that no input or result face plane contains it
    // (with the origin as apex, faces in a plane through the origin contribute 0 and their loss goes unseen)
    public static Vector3d Apex = new Vector3d(0.3125 + Math.Pow(2, -20), 0.4375 + Math.Pow(2, -21), 0.5625 + Math.Pow(2, -22));
    public static bool NaiveSum = false;

    static int TwoPart(BigInteger w) { int j = 0; while (w.IsEven) { w >>= 1; ++j; } return j; }

    // 6 x signed volume of a triangle list over exact points (divergence theorem, apex Apex), exact
    static Q Volume6(ExactPoint3[] p, IEnumerable<Index3i> tris)
    {
        ExactPoint3 o = FromDoubles(Apex);
        // every point as (numerator relative to the apex) / (2^L * odd): one power of two for all, so that the
        // denominator products differ only in their odd parts (1 for the input's vertices)
        int L = TwoPart(o.W);
        foreach (ExactPoint3 q in p) L = Math.Max(L, TwoPart(q.W));
        var rel = new BigInteger[p.Length][]; var odd = new BigInteger[p.Length];
        int jo = TwoPart(o.W);
        for (int i = 0; i < p.Length; ++i) {
            ExactPoint3 q = p[i];
            int j = TwoPart(q.W);
            BigInteger od = q.W >> j;
            odd[i] = od;
            var r = new BigInteger[3];
            for (int k = 0; k < 3; ++k) r[k] = (q[k] << (L - j)) - ((o[k] << (L - jo)) * od);
            rel[i] = r;
        }
        var byDen = new Dictionary<BigInteger, BigInteger>();
        foreach (Index3i t in tris) {
            BigInteger[] a = rel[t.a], b = rel[t.b], c = rel[t.c];
            BigInteger det = ExactPoint3.Det3(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2]);
            if (det.IsZero) continue;
            BigInteger den = odd[t.a] * odd[t.b] * odd[t.c];
            byDen.TryGetValue(den, out BigInteger s);
            byDen[den] = s + det;
        }
        // balanced (tree) summation of the groups: the denominators grow in a tree, not in a chain
        var list = byDen.Select(kv => new Q(kv.Value, kv.Key)).ToList();
        if (NaiveSum) { Q s = Q.Zero; foreach (Q q in list) s = s + q; return Scale(s, 3 * L); }
        if (list.Count == 0) return Q.Zero;
        while (list.Count > 1) {
            var next = new List<Q>((list.Count + 1) / 2);
            for (int i = 0; i + 1 < list.Count; i += 2) next.Add(list[i] + list[i + 1]);
            if ((list.Count & 1) == 1) next.Add(list[list.Count - 1]);
            list = next;
        }
        return Scale(list[0], 3 * L);
    }

    static Q Scale(Q q, int twoPow) => q.IsZero ? q : new Q(q.N, q.D << twoPow) + Q.Zero;

    static ExactPoint3 FromDoubles(Vector3d v)
    {
        // dyadic exact point: common power of two
        BigInteger[] m = new BigInteger[3]; int[] e = new int[3];
        for (int i = 0; i < 3; ++i) ExactPoint3.Decompose(v[i], out m[i], out e[i]);
        int e0 = Math.Min(e[0], Math.Min(e[1], e[2]));
        BigInteger[] x = new BigInteger[3];
        for (int i = 0; i < 3; ++i) x[i] = m[i].IsZero ? BigInteger.Zero : m[i] << (e[i] - e0);
        return e0 >= 0 ? ExactPoint3.Create(x[0] << e0, x[1] << e0, x[2] << e0, BigInteger.One)
                       : ExactPoint3.Create(x[0], x[1], x[2], BigInteger.One << -e0);
    }

    static (ExactPoint3[] p, List<Index3i> t) ExactOf(DMesh3 m)
    {
        var map = new int[m.MaxVertexID];
        var p = new List<ExactPoint3>();
        foreach (int v in m.VertexIndices()) { map[v] = p.Count; p.Add(FromDoubles(m.GetVertex(v))); }
        var t = new List<Index3i>();
        foreach (int tid in m.TriangleIndices()) { Index3i q = m.GetTriangle(tid); t.Add(new Index3i(map[q.a], map[q.b], map[q.c])); }
        return (p.ToArray(), t);
    }

    class Exact
    {
        public ExactPoint3[] P; public List<Index3i> T; public DMesh3 Mesh; public long Ms;
        public int Chi() => Mesh.VertexCount - Mesh.EdgeCount + Mesh.TriangleCount;
    }

    static Exact Run(DMesh3 a, DMesh3 b, ExactMeshBoolean.Operations op)
    {
        var sw = Stopwatch.StartNew();
        var bo = new ExactMeshBoolean(a, b, op) { ComputeExactVertices = true, SafeRounding = false };
        if (!bo.Compute()) throw new Exception(op + ": " + bo.Error);
        sw.Stop();
        var r = bo.Result;
        var t = new List<Index3i>();
        foreach (int tid in r.TriangleIndices()) t.Add(r.GetTriangle(tid));
        return new Exact { P = bo.ResultExactVertices, T = t, Mesh = r, Ms = sw.ElapsedMilliseconds };
    }

    static DMesh3 Sphere(int n, double r, Vector3d c)
    {
        var m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        MeshTransforms.Translate(m, c);
        return m;
    }
    static DMesh3 Box(Vector3d min, Vector3d max) =>
        new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(min, max)) }.Generate().MakeDMesh();
    static DMesh3 Rotated(DMesh3 m, Vector3d axis, double deg)
    {
        var q = new Quaterniond(axis.Normalized, deg);
        MeshTransforms.Rotate(m, Vector3d.Zero, q);
        return m;
    }
    static DMesh3 Merge(params DMesh3[] ms) { var r = new DMesh3(); foreach (var m in ms) MeshEditor.Append(r, m); return r; }

    public static void RunAll()
    {
        var cases = new List<(string name, DMesh3 a, DMesh3 b)> {
            ("spheres n=24", Sphere(24, 1, Vector3d.Zero), Sphere(24, 1, new Vector3d(0.7, 0.3, 0.1))),
            ("spheres n=64", Sphere(64, 1, Vector3d.Zero), Sphere(64, 1, new Vector3d(0.7, 0.3, 0.1))),
            ("box x rotated box", Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), Rotated(Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), new Vector3d(1, 2, 3), 33)),
            ("coplanar boxes (shared faces)", Box(new Vector3d(0, 0, 0), new Vector3d(2, 2, 1)), Box(new Vector3d(1, 1, 0), new Vector3d(3, 3, 1))),
            ("two spheres + box, one sphere swallowed", Merge(Sphere(16, 0.5, Vector3d.Zero), Sphere(16, 0.3, new Vector3d(3, 0, 0))), Box(new Vector3d(0.2, -1, -1), new Vector3d(4, 1, 1))),
        };
        Console.WriteLine("| case | tris A+B | union ms | intersection ms | exact volumes ms | V identity | chi U + chi I vs chi A + chi B |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        var defectRows = new List<string>();
        foreach (var (name, a, b) in cases) {
            Exact u = Run(a, b, ExactMeshBoolean.Operations.Union);
            Exact i = Run(a, b, ExactMeshBoolean.Operations.Intersection);
            var sw = Stopwatch.StartNew();
            var (pa, ta) = ExactOf(a); var (pb, tb) = ExactOf(b);
            Q vU = Volume6(u.P, u.T), vI = Volume6(i.P, i.T), vA = Volume6(pa, ta), vB = Volume6(pb, tb);
            Q diff = vU + vI + (-vA) + (-vB);
            sw.Stop();
            int chiA = a.VertexCount - a.EdgeCount + a.TriangleCount, chiB = b.VertexCount - b.EdgeCount + b.TriangleCount;
            Console.WriteLine($"| {name} | {a.TriangleCount + b.TriangleCount} | {u.Ms} | {i.Ms} | {sw.ElapsedMilliseconds} | {(diff.IsZero ? "exact 0" : "NONZERO " + diff.ToDouble())} | {u.Chi()} + {i.Chi()} vs {chiA} + {chiB} |");

            // injected defects on the exact union (the intersection kept): which does the identity catch?
            Func<List<Index3i>, ExactPoint3[], string> check = (tu, pu) => {
                Q d = Volume6(pu, tu) + vI + (-vA) + (-vB);
                return d.IsZero ? "missed" : "caught";
            };
            var rows = new List<string>();
            // D1: one triangle dropped
            rows.Add("drop 1 triangle: " + check(u.T.Skip(1).ToList(), u.P));
            // D2: one triangle reversed
            var t2 = u.T.ToList(); t2[0] = new Index3i(t2[0].a, t2[0].c, t2[0].b); rows.Add("reverse 1 triangle: " + check(t2, u.P));
            // D3: one triangle mislabelled: moved from U to I (I gets it, U loses it): sum unchanged by construction
            {
                Q dU = Volume6(u.P, u.T.Skip(1)), dI = vI + Volume6(u.P, u.T.Take(1));
                rows.Add("move 1 triangle U->I: " + ((dU + dI + (-vA) + (-vB)).IsZero ? "missed" : "caught"));
            }
            // D4: one exact vertex moved by 2^-40 along x (normal-ish) and within the plane of a flat fan (if any)
            {
                var p4 = (ExactPoint3[])u.P.Clone(); int v = u.T[0].a;
                ExactPoint3 q = p4[v];
                p4[v] = ExactPoint3.Create(q.X * (BigInteger.One << 40) + q.W, q.Y * (BigInteger.One << 40), q.Z * (BigInteger.One << 40), q.W * (BigInteger.One << 40));
                rows.Add("move 1 exact vertex by 2^-40 in x: " + check(u.T, p4));
                // tangential: find a vertex whose incident triangles are all coplanar, move it inside that plane
                rows.Add("move 1 vertex inside its flat fan: " + TangentialMove(u, vI, vA, vB));
            }
            // D5: the rounding step (the identity on the rounded doubles of both results)
            {
                var (ru, rtu) = ExactOf(u.Mesh); var (ri, rti) = ExactOf(i.Mesh);
                Q dr = Volume6(ru, rtu) + Volume6(ri, rti) + (-vA) + (-vB);
                rows.Add("rounded results: " + (dr.IsZero ? "exact 0" : $"drift {dr.ToDouble() / 6:E2}"));
            }
            // D6: a whole closed component mislabelled (U gets an inner shell, I loses it)
            rows.Add("whole component U<->I: " + ComponentSwap(u, i, vA, vB));
            defectRows.Add($"| {name} | " + string.Join(" | ", rows) + " |");
        }
        Console.WriteLine();
        Console.WriteLine("| case | drop 1 | reverse 1 | move 1 U->I | vertex 2^-40 | vertex in flat fan | rounded | component U<->I |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (string r in defectRows) Console.WriteLine(r.Replace("drop 1 triangle: ", "").Replace("reverse 1 triangle: ", "").Replace("move 1 triangle U->I: ", "")
            .Replace("move 1 exact vertex by 2^-40 in x: ", "").Replace("move 1 vertex inside its flat fan: ", "").Replace("rounded results: ", "").Replace("whole component U<->I: ", ""));
    }

    static string TangentialMove(Exact u, Q vI, Q vA, Q vB)
    {
        // a vertex with all incident triangles in one axis plane (the box cases): move it inside the plane
        var r = u.Mesh;
        foreach (int v in r.VertexIndices()) {
            var tris = r.VtxTrianglesItr(v).ToList();
            if (tris.Count < 3) continue;
            Vector3d n0 = r.GetTriNormal(tris[0]);
            int ax = Math.Abs(n0.x) > 0.999999 ? 0 : Math.Abs(n0.y) > 0.999999 ? 1 : Math.Abs(n0.z) > 0.999999 ? 2 : -1;
            if (ax < 0 || tris.Any(t => r.GetTriNormal(t).Dot(n0) < 0.999999)) continue;
            // exact coplanarity of the fan along the axis: all its vertices have the same exact coordinate ax
            var fanV = tris.SelectMany(t => { var q = r.GetTriangle(t); return new[] { q.a, q.b, q.c }; }).Distinct().ToList();
            ExactPoint3 p0 = u.P[v];
            if (fanV.Any(w => u.P[w][ax] * p0.W != p0[ax] * u.P[w].W)) continue;
            int d = (ax + 1) % 3;
            var p = (ExactPoint3[])u.P.Clone();
            BigInteger s = BigInteger.One << 50;
            BigInteger[] c = { p0.X * s, p0.Y * s, p0.Z * s };
            c[d] += p0.W;
            p[v] = ExactPoint3.Create(c[0], c[1], c[2], p0.W * s);
            Q dd = Volume6(p, u.T) + vI + (-vA) + (-vB);
            return dd.IsZero ? "missed" : "caught";
        }
        return "n/a (no flat fan)";
    }

    static string ComponentSwap(Exact u, Exact i, Q vA, Q vB)
    {
        // a closed component of I that lies inside the other input: moved to U (as a nested shell), removed from I
        var comps = new MeshConnectedComponents(i.Mesh); comps.FindConnectedT();
        if (comps.Count < 2) return "n/a (one component)";
        var c = comps.Components.OrderBy(x => x.Indices.Length).First();
        var set = new HashSet<int>(c.Indices);
        var tI = new List<Index3i>(); var tC = new List<Index3i>();
        foreach (int tid in i.Mesh.TriangleIndices()) (set.Contains(tid) ? tC : tI).Add(i.Mesh.GetTriangle(tid));
        Q d = Volume6(u.P, u.T) + Volume6(i.P, tC) + Volume6(i.P, tI) + (-vA) + (-vB);
        // the swapped results stay closed: closedness does not catch it either
        return d.IsZero ? "missed (closed too)" : "caught";
    }
}
