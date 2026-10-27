using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using g3;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reduces a failing mesh to a small one that still shows the error (docs/QA.md 2.9), in the manner of delta
    /// debugging (DeltaDebug.Minimize). The steps, coarse to fine, repeated until nothing changes:
    /// 0. drop the vertex attributes and the triangle groups;
    /// 1. drop connected components (triangles that share a vertex);
    /// 2. ddmin over the triangles, down to single ones (the result is 1-minimal: no single triangle can go);
    /// 3. drop the input's unused vertices (vertices of dropped triangles go with them);
    /// 4. round the coordinates to coarser power-of-two grids, all together and then vertex by vertex
    ///    (MinimizeOptions.SimplifyCoordinates).
    /// The predicate gets a fresh, compact DMesh3 on every call (it may modify it) and returns true while the error is
    /// present. Runs are deterministic for a deterministic predicate (MinimizeOptions.Seed), unless calls time out.
    /// </summary>
    public static class MeshMinimizer
    {
        public enum MinimizeStatus
        {
            /// <summary> Mesh is 1-minimal for the steps above </summary>
            Minimized,
            /// <summary> stopped by the time budget or by MinimizeOptions.MaxTimeouts: Mesh fails, but may not be minimal </summary>
            Stopped,
            /// <summary> the predicate is false on the compact copy of the input (it needs the ids, or it is flaky): Mesh is that copy </summary>
            NotReproduced
        }

        public sealed class Result
        {
            public MinimizeStatus Status;
            /// <summary> the smallest failing mesh found (compact ids) </summary>
            public DMesh3 Mesh;
            public int Evaluations, Exceptions, Timeouts;
            public Exception FirstException;
            public TimeSpan Elapsed;
            /// <summary> one line per step: what it removed </summary>
            public List<string> Log = new List<string>();
            public override string ToString() =>
                $"{Status}: {Mesh.VertexCount} vertices, {Mesh.TriangleCount} triangles after {Evaluations} evaluations in {Elapsed.TotalSeconds:F1} s"
                + (Exceptions > 0 ? $", {Exceptions} threw ({FirstException?.GetType().Name})" : "") + (Timeouts > 0 ? $", {Timeouts} timed out" : "");
        }

        /// <summary> the current candidate: a subset of the input's triangles and unused vertices, with coordinates </summary>
        sealed class State
        {
            public DMesh3 Input;
            public int[] Vid;              // compact index -> input vertex id
            public Vector3d[] P;           // compact index -> position
            public Index3i[] T;            // input triangles in compact vertex indices, in id order
            public int[] Group;
            public List<int> Tris;         // indices into T
            public List<int> Unused;       // compact vertex indices without a triangle in the input
            public bool VertexAttributes, Groups;

            public State With(List<int> tris = null, List<int> unused = null, Vector3d[] p = null, bool? vertexAttributes = null, bool? groups = null)
            {
                var s = (State)MemberwiseClone();
                s.Tris = tris ?? Tris; s.Unused = unused ?? Unused; s.P = p ?? P;
                s.VertexAttributes = vertexAttributes ?? VertexAttributes; s.Groups = groups ?? Groups;
                return s;
            }

            public bool[] UsedByTriangles()
            {
                var used = new bool[P.Length];
                foreach (int t in Tris) { used[T[t].a] = true; used[T[t].b] = true; used[T[t].c] = true; }
                return used;
            }

            /// <summary> the mesh: vertices in input order, then the triangles in input order </summary>
            public DMesh3 Build()
            {
                bool[] used = UsedByTriangles();
                foreach (int v in Unused) used[v] = true;
                var m = new DMesh3(VertexAttributes && Input.HasVertexNormals, VertexAttributes && Input.HasVertexColors,
                    VertexAttributes && Input.HasVertexUVs, Groups && Input.HasTriangleGroups);
                var map = new int[P.Length];
                for (int i = 0; i < P.Length; ++i) {
                    if (used[i] == false) continue;
                    if (VertexAttributes) {
                        NewVertexInfo info = Input.GetVertexAll(Vid[i]);
                        info.v = P[i];
                        map[i] = m.AppendVertex(info);
                    } else
                        map[i] = m.AppendVertex(P[i]);
                }
                // (a subset of the triangles of a valid DMesh3 has at most two triangles per edge, in any order)
                foreach (int t in Tris)
                    if (m.AppendTriangle(new Index3i(map[T[t].a], map[T[t].b], map[T[t].c]), Groups ? Group[t] : -1) < 0)
                        throw new InvalidOperationException("MeshMinimizer: triangle " + t + " rejected");
                return m;
            }

            public bool SameAs(State o) =>
                Tris.SequenceEqual(o.Tris) && Unused.SequenceEqual(o.Unused) && P.Length == o.P.Length && P.Select((p, i) => Same(p, o.P[i])).All(x => x)
                && VertexAttributes == o.VertexAttributes && Groups == o.Groups;
        }

        /// <summary> equal bits (NaN equals NaN, -0 differs from +0) </summary>
        static bool Same(Vector3d a, Vector3d b) =>
            BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) && BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y)
            && BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z);

        const int MaxRounds = 8;

        /// <summary> minimizes mesh (not modified) while fails is true; see the class comment </summary>
        public static Result Minimize(DMesh3 mesh, Func<DMesh3, bool> fails, MinimizeOptions options = null)
        {
            options = options ?? new MinimizeOptions();
            var probe = new Probe<DMesh3>(fails, options);
            var result = new Result();

            var s = new State() { Input = mesh, VertexAttributes = true, Groups = true };
            var index = new Dictionary<int, int>();
            var vids = new List<int>();
            foreach (int v in mesh.VertexIndices()) { index[v] = vids.Count; vids.Add(v); }
            s.Vid = vids.ToArray();
            s.P = vids.Select(v => mesh.GetVertex(v)).ToArray();
            var tids = mesh.TriangleIndices().ToList();
            s.T = tids.Select(t => { Index3i tv = mesh.GetTriangle(t); return new Index3i(index[tv.a], index[tv.b], index[tv.c]); }).ToArray();
            s.Group = tids.Select(t => mesh.HasTriangleGroups ? mesh.GetTriangleGroup(t) : -1).ToArray();
            s.Tris = Enumerable.Range(0, s.T.Length).ToList();
            bool[] used = s.UsedByTriangles();
            s.Unused = Enumerable.Range(0, s.P.Length).Where(v => used[v] == false).ToList();

            bool Fails(State x) => probe.Stopped == false && probe.Fails(x.Build());

            if (probe.Fails(s.Build()) == false) {
                result.Status = MinimizeStatus.NotReproduced;
                result.Log.Add("the compact copy of the input does not fail");
            } else {
                // 0. attributes
                bool hasVertexAttributes = mesh.HasVertexNormals || mesh.HasVertexColors || mesh.HasVertexUVs;
                if (hasVertexAttributes || mesh.HasTriangleGroups) {
                    foreach (State candidate in new[] { s.With(vertexAttributes: false, groups: false), s.With(vertexAttributes: false), s.With(groups: false) }) {
                        if (candidate.SameAs(s) == false && Fails(candidate)) { s = candidate; break; }
                    }
                    result.Log.Add($"attributes: vertex attributes {(s.VertexAttributes && hasVertexAttributes ? "kept" : "dropped")}, groups {(s.Groups && mesh.HasTriangleGroups ? "kept" : "dropped")}");
                }
                bool fixpoint = false;
                // the grid of the coordinates is fixed at the first rounding (after the first triangle step): rounding
                // shrinks the extent, and a grid that followed it would never settle
                int? gridExponent = null;
                for (int round = 1; round <= MaxRounds && probe.Stopped == false && fixpoint == false; ++round) {
                    State start = s;
                    int before;

                    // 1. components
                    List<List<int>> components = Components(s);
                    if (components.Count > 1) {
                        before = components.Count;
                        List<List<int>> kept = DeltaDebug.Minimize(components, c => Fails(s.With(tris: c.SelectMany(x => x).OrderBy(t => t).ToList())),
                            () => probe.Stopped, options.Seed);
                        s = s.With(tris: kept.SelectMany(x => x).OrderBy(t => t).ToList());
                        result.Log.Add($"round {round}, components: {before} -> {kept.Count}");
                    }

                    // 2. triangles (the empty set was tried with the components, if there were several)
                    before = s.Tris.Count;
                    s = s.With(tris: DeltaDebug.Minimize(s.Tris, c => Fails(s.With(tris: c)), () => probe.Stopped, options.Seed, components.Count <= 1));
                    result.Log.Add($"round {round}, triangles: {before} -> {s.Tris.Count}");

                    // 3. unused vertices
                    if (s.Unused.Count > 0) {
                        before = s.Unused.Count;
                        s = s.With(unused: DeltaDebug.Minimize(s.Unused, c => Fails(s.With(unused: c)), () => probe.Stopped, options.Seed));
                        result.Log.Add($"round {round}, unused vertices: {before} -> {s.Unused.Count}");
                    }

                    // 4. coordinates
                    if (options.SimplifyCoordinates) {
                        int changed;
                        s = SimplifyCoordinates(s, Fails, ref gridExponent, out changed);
                        result.Log.Add($"round {round}, coordinates: {changed} roundings accepted");
                    }

                    fixpoint = s.SameAs(start);
                }
                if (fixpoint == false && probe.Stopped == false) result.Log.Add($"no fixpoint after {MaxRounds} rounds");
                result.Status = (probe.Stopped || fixpoint == false) ? MinimizeStatus.Stopped : MinimizeStatus.Minimized;
            }
            result.Mesh = s.Build();
            result.Evaluations = probe.Evaluations;
            result.Exceptions = probe.Exceptions;
            result.Timeouts = probe.Timeouts;
            result.FirstException = probe.FirstException;
            result.Elapsed = probe.Elapsed;
            return result;
        }

        /// <summary> the triangles of s grouped into vertex-connected components, in the order of their first triangle </summary>
        static List<List<int>> Components(State s)
        {
            var parent = Enumerable.Range(0, s.P.Length).ToArray();
            int Find(int v) { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
            foreach (int t in s.Tris) { Union(s.T[t].a, s.T[t].b); Union(s.T[t].a, s.T[t].c); }
            var byRoot = new Dictionary<int, List<int>>();
            var result = new List<List<int>>();
            foreach (int t in s.Tris) {
                int r = Find(s.T[t].a);
                if (byRoot.TryGetValue(r, out List<int> list) == false) { list = new List<int>(); byRoot[r] = list; result.Add(list); }
                list.Add(t);
            }
            return result;
        }

        // grid levels: a step of 2^(e - k), 2^e >= the extent of the used vertices
        static readonly int[] Levels = { 0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32, 40, 52 };

        /// <summary>
        /// Rounds the coordinates of the used vertices to the coarsest grid level that keeps the error, all vertices
        /// together first, then each vertex on its own. The grid is a power of two relative to the extent, so a
        /// power-of-two scale of the input gives the same result, scaled, bit for bit.
        /// </summary>
        static State SimplifyCoordinates(State s, Func<State, bool> fails, ref int? gridExponent, out int changed)
        {
            changed = 0;
            bool[] used = s.UsedByTriangles();
            if (s.Tris.Count == 0) foreach (int v in s.Unused) used[v] = true;
            // the extent: largest bounding box side of the finite used vertices
            double extent = 0;
            var box = AxisAlignedBox3d.Empty;
            for (int i = 0; i < s.P.Length; ++i)
                if (used[i] && s.P[i].IsFinite) box.Contain(s.P[i]);
            if (box.Max.x >= box.Min.x) extent = Math.Max(box.Width, Math.Max(box.Height, box.Depth));
            if (gridExponent == null) {
                if (!(extent > 1e-290) || double.IsInfinity(extent)) return s;
                gridExponent = Exponent(extent) + 1;
            }
            int e = gridExponent.Value;
            if (e > 1023) return s;      // (2^e would overflow)

            Vector3d[] Rounded(Vector3d[] p, int level, int only)
            {
                double step = Pow2(e - level);
                Vector3d[] q = (Vector3d[])p.Clone();
                for (int i = 0; i < q.Length; ++i)
                    if (used[i] && (only < 0 || only == i))
                        q[i] = new Vector3d(Round(q[i].x, step), Round(q[i].y, step), Round(q[i].z, step));
                return q;
            }

            foreach (int level in Levels) {
                Vector3d[] q = Rounded(s.P, level, -1);
                if (s.With(p: q).SameAs(s)) break;
                if (fails(s.With(p: q))) { changed = q.Where((p, i) => Same(p, s.P[i]) == false).Count(); s = s.With(p: q); break; }
            }
            for (int i = 0; i < s.P.Length; ++i) {
                if (used[i] == false) continue;
                foreach (int level in Levels) {
                    Vector3d[] q = Rounded(s.P, level, i);
                    if (Same(q[i], s.P[i])) break;
                    if (fails(s.With(p: q))) { ++changed; s = s.With(p: q); break; }
                }
            }
            return s;
        }

        static double Round(double x, double step)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            double q = x / step;
            if (Math.Abs(q) >= 4503599627370496.0) return x;     // 2^52: already on the grid
            return Math.Round(q) * step;
        }

        /// <summary> floor(log2(x)) for a positive normal x </summary>
        static int Exponent(double x) => (int)((BitConverter.DoubleToInt64Bits(x) >> 52) & 0x7FF) - 1023;

        /// <summary> 2^k for -1022 &lt;= k &lt;= 1023 </summary>
        static double Pow2(int k) => BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);

        // ---- output ----

        /// <summary> a double as a C# literal that parses back to the same bits </summary>
        public static string Literal(double x)
        {
            if (double.IsNaN(x)) return "double.NaN";
            if (double.IsPositiveInfinity(x)) return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(x)) return "double.NegativeInfinity";
            if (x == 0) return (1 / x < 0) ? "-0.0" : "0";
            // subnormal and near-overflow values as their bits: double.Parse on .NET Framework is not exact there (and
            // throws near MaxValue); the C# compiler would read the decimal text right, but it cannot be checked here
            double ax = Math.Abs(x);
            if (ax < 2.2250738585072014E-308 || ax > 1e308) return Bits(x);
            // else the fewest significant digits that round-trip: the same text on .NET Framework and .NET Core ("R"
            // differs between them, and does not round-trip every value on .NET Framework)
            for (int digits = 1; digits <= 17; ++digits) {
                string r = x.ToString("G" + digits, CultureInfo.InvariantCulture);
                double back;
                if (double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out back) && back == x) return r;
            }
            return Bits(x);
        }

        /// <summary> the exact bits as a C# expression </summary>
        static string Bits(double x) => "System.BitConverter.Int64BitsToDouble(" + BitConverter.DoubleToInt64Bits(x).ToString(CultureInfo.InvariantCulture) + "L)";

        /// <summary>
        /// The mesh as a C# method that builds it (vertex positions, triangles and groups; ids are compacted): a test
        /// fixture instead of a mesh file, which CLAUDE.md does not allow in the repository.
        /// </summary>
        public static string ToCSharp(DMesh3 mesh, string methodName = "Repro", string comment = null)
        {
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices()) map[v] = map.Count;
            bool groups = mesh.HasTriangleGroups;
            var sb = new StringBuilder();
            if (comment != null)
                foreach (string line in comment.Split('\n')) sb.Append("// ").Append(line.TrimEnd()).Append('\n');
            sb.Append($"static DMesh3 {methodName}()\n{{\n");
            sb.Append($"    // {mesh.VertexCount} vertices, {mesh.TriangleCount} triangles\n");
            sb.Append("    double[] v = {\n");
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                sb.Append($"        {Literal(p.x)}, {Literal(p.y)}, {Literal(p.z)},\n");
            }
            sb.Append("    };\n    int[] t = {\n");
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                sb.Append($"        {map[tv.a]}, {map[tv.b]}, {map[tv.c]},\n");
            }
            sb.Append("    };\n");
            if (groups)
                sb.Append("    int[] g = { ").Append(string.Join(", ", mesh.TriangleIndices().Select(t => mesh.GetTriangleGroup(t)))).Append(" };\n");
            if (mesh.HasVertexNormals || mesh.HasVertexColors || mesh.HasVertexUVs)
                sb.Append("    // (the mesh has vertex normals, colors or uvs, which are not written here)\n");
            // (no attributes: DMesh3() would add normals)
            sb.Append($"    var m = new DMesh3(false, false, false, {(groups ? "true" : "false")});\n");
            sb.Append("    for (int i = 0; i < v.Length; i += 3) m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));\n");
            sb.Append("    for (int i = 0; i < t.Length; i += 3)\n");
            sb.Append(groups ? "        if (m.AppendTriangle(new Index3i(t[i], t[i + 1], t[i + 2]), g[i / 3]) < 0)\n"
                             : "        if (m.AppendTriangle(t[i], t[i + 1], t[i + 2]) < 0)\n");
            sb.Append("            throw new InvalidOperationException(\"repro: triangle \" + i / 3);\n");
            sb.Append("    return m;\n}\n");
            return sb.ToString();
        }

        static string Number(double x)
        {
            if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x.ToString(CultureInfo.InvariantCulture);
            string r = Literal(x);
            return r.StartsWith("System.") ? x.ToString("G17", CultureInfo.InvariantCulture) : r;
        }

        /// <summary> the mesh as OBJ text with exact (round-trip) coordinates </summary>
        public static string ToObj(DMesh3 mesh)
        {
            var map = new Dictionary<int, int>();
            var sb = new StringBuilder();
            foreach (int v in mesh.VertexIndices()) {
                map[v] = map.Count + 1;
                Vector3d p = mesh.GetVertex(v);
                sb.Append("v ").Append(Number(p.x)).Append(' ').Append(Number(p.y)).Append(' ').Append(Number(p.z)).Append('\n');
            }
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t);
                sb.Append($"f {map[tv.a]} {map[tv.b]} {map[tv.c]}\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// The directory for minimized cases: G3_MINIMIZE_DIR, else .g3-minimized in the repository root (git-ignored),
        /// else in the temp directory
        /// </summary>
        public static string ReproDirectory()
        {
            string dir = Environment.GetEnvironmentVariable("G3_MINIMIZE_DIR");
            if (string.IsNullOrEmpty(dir)) {
                for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, "geometry3Sharp_netstandard.csproj"))) { dir = Path.Combine(d.FullName, ".g3-minimized"); break; }
                if (dir == null) dir = Path.Combine(Path.GetTempPath(), "g3-minimized");
            }
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary> a name usable as a file name and as a C# identifier part </summary>
        public static string Identifier(string name)
        {
            var sb = new StringBuilder();
            bool upper = true;
            foreach (char ch in name) {
                if (char.IsLetterOrDigit(ch) && ch < 128) { sb.Append(upper ? char.ToUpperInvariant(ch) : ch); upper = false; } else upper = true;
            }
            return sb.Length == 0 ? "Case" : sb.ToString();
        }

        /// <summary> writes name.obj and name.cs into ReproDirectory(); returns the path of the .cs file </summary>
        public static string WriteRepro(string name, DMesh3 mesh, string csharp)
        {
            string dir = ReproDirectory(), id = Identifier(name);
            File.WriteAllText(Path.Combine(dir, id + ".obj"), ToObj(mesh));
            string cs = Path.Combine(dir, id + ".cs");
            File.WriteAllText(cs, csharp);
            return cs;
        }
    }
}
