// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using g3;

namespace g3cli
{
    /// <summary>
    /// The .node / .poly / .ele files of Shewchuk's Triangle, the common exchange format of 2D meshers (Triangle, Triangle.NET,
    /// CDT, MeshPy), written from the published file format description only (https://www.cs.cmu.edu/~quake/triangle.node.html,
    /// .poly.html, .ele.html; docs/SOURCES.md), no code of Triangle. Shared by g3cli (mesh2d) and benchmarks/Meshing2Benchmark (mesh),
    /// which compiles this file; it uses only library types. Every failure of a reader is a FormatException.
    /// </summary>
    /// <remarks>
    /// <para>Files: '#' starts a comment, blank lines are ignored, fields are separated by blanks. Vertices are numbered from 0 or 1,
    /// decided by the first vertex (as Triangle does); segments, holes and triangles refer to that numbering.</para>
    /// <para>.node: "&lt;n&gt; &lt;dimension 2&gt; &lt;attributes&gt; &lt;markers 0|1&gt;", then "&lt;i&gt; &lt;x&gt; &lt;y&gt; [attributes] [marker]".
    /// .poly: a .node header and its vertices (n = 0: the vertices are in a separate .node file), then "&lt;m&gt; &lt;markers 0|1&gt;" and
    /// "&lt;i&gt; &lt;a&gt; &lt;b&gt; [marker]", then "&lt;h&gt;" and "&lt;i&gt; &lt;x&gt; &lt;y&gt;" hole points, then optionally "&lt;r&gt;" and
    /// "&lt;i&gt; &lt;x&gt; &lt;y&gt; &lt;attribute&gt; &lt;max area&gt;" regions (a negative area: none). .ele: "&lt;t&gt; &lt;3&gt; &lt;attributes&gt;", then
    /// "&lt;i&gt; &lt;a&gt; &lt;b&gt; &lt;c&gt; [attributes]", counterclockwise.</para>
    /// <para>The domain: Triangle meshes the convex hull and, with its -p switch, eats triangles from the hull inwards until segments
    /// stop it, then from each hole point. Ours meshes the convex hull unless rings are given. So that the same .poly gives the same
    /// domain, every connected group of segments, without its bridges and dangling chains, adds the boundary of its outer face as an
    /// outer ring: a point is then in the domain iff some group encloses it (nested loops, chords, loops sharing a vertex included),
    /// and hole points remove what they reach (HoleSeeds). Limit: a group whose segments cross, touch or overlap other than at shared
    /// end points is no plane graph here (Triangle would split it at the crossings); it stays constraints only, and without any ring
    /// the domain is the convex hull (Triangle -c). <see cref="PolyInfo.Notes"/> says so in both cases.</para>
    /// </remarks>
    public static class TrianglePolyFormat
    {
        /// <summary> What a reader found besides the input. </summary>
        public sealed class PolyInfo
        {
            /// <summary> 0 or 1: the number of the first vertex </summary>
            public int FirstIndex;
            /// <summary> vertex attributes per vertex in the file (the first one is taken as the height if asked) </summary>
            public int VertexAttributes;
            /// <summary> outer rings made from groups of segments (see the class remarks) </summary>
            public int Loops;
            public List<string> Notes = new List<string>();
        }

        sealed class Lines
        {
            readonly List<string[]> lines = new List<string[]>();
            readonly List<int> numbers = new List<int>();
            int at;
            public readonly string What;

            public Lines(string text, string what)
            {
                What = what;
                string[] raw = text.Replace("\r", "").Split('\n');
                for (int i = 0; i < raw.Length; ++i) {
                    string l = raw[i];
                    int h = l.IndexOf('#');
                    if (h >= 0) l = l.Substring(0, h);
                    var f = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length > 0) { lines.Add(f); numbers.Add(i + 1); }
                }
            }

            public bool AtEnd => at >= lines.Count;

            public FormatException Fail(string message) =>
                new FormatException($"{What} line {(at > 0 && at <= numbers.Count ? numbers[at - 1] : 0)}: {message}");

            public string[] Next(int min, int max, string expected)
            {
                if (at >= lines.Count) throw new FormatException($"{What}: unexpected end of the file, expected {expected}");
                var f = lines[at++];
                if (f.Length < min || f.Length > max) throw Fail($"expected {expected}, got {f.Length} values");
                return f;
            }

            public int Int(string s, string name, int min = int.MinValue, int max = int.MaxValue)
            {
                // Triangle writes integers; a value like "3.0" is accepted if it is integral
                if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)
                    || (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue && (v = (int)d) == d)) {
                    if (v < min || v > max) throw Fail($"{name} must be in {min}..{max}, got {s}");
                    return v;
                }
                throw Fail($"{name} is not an integer: {s}");
            }

            public double Number(string s, string name)
            {
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
                throw Fail($"{name} is not a finite number: {s}");
            }
        }

        /// <summary> The points (and heights, the first vertex attribute, if heights) of a .node file, or of the vertex part of a .poly. </summary>
        static void ReadVertices(Lines r, ConstrainedMesh2Input input, PolyInfo info, bool heights, int count, int attributes, int markers)
        {
            if (heights && attributes > 0) input.Heights = new List<double>();      // (no capacity from the header: a count may be huge in a broken file)
            for (int i = 0; i < count; ++i) {
                var f = r.Next(3, 3 + attributes + markers, $"vertex: <number> <x> <y>{(attributes > 0 ? " <" + attributes + " attributes>" : "")}{(markers > 0 ? " [marker]" : "")}");
                if (f.Length < 3 + attributes) throw r.Fail($"vertex has fewer than {attributes} attributes");
                int number = r.Int(f[0], "vertex number");
                if (i == 0) {
                    if (number != 0 && number != 1) throw r.Fail("the first vertex is numbered 0 or 1, got " + number);
                    info.FirstIndex = number;
                } else if (number != i + info.FirstIndex) {
                    throw r.Fail($"vertex {number} out of order, expected {i + info.FirstIndex}");
                }
                input.Points.Add(new Vector2d(r.Number(f[1], "x"), r.Number(f[2], "y")));
                for (int a = 0; a < attributes; ++a) {
                    double v = r.Number(f[3 + a], "attribute");
                    if (a == 0 && input.Heights != null) input.Heights.Add(v);
                }
            }
            info.VertexAttributes = attributes;
        }

        static (int count, int attributes, int markers) NodeHeader(Lines r)
        {
            var h = r.Next(1, 4, "<vertices> [2] [attributes] [markers]");
            int count = r.Int(h[0], "number of vertices", 0, 100000000);
            if (h.Length > 1 && r.Int(h[1], "dimension") != 2) throw r.Fail("the dimension must be 2");
            int attributes = h.Length > 2 ? r.Int(h[2], "number of attributes", 0, 1000) : 0;
            int markers = h.Length > 3 ? r.Int(h[3], "number of boundary markers", 0, 1) : 0;
            return (count, attributes, markers);
        }

        /// <summary> A .node file: the points, and the first vertex attribute as heights if heights. </summary>
        public static ConstrainedMesh2Input ReadNode(string text, bool heights, out PolyInfo info)
        {
            var r = new Lines(text, ".node");
            info = new PolyInfo();
            var input = new ConstrainedMesh2Input();
            var (count, attributes, markers) = NodeHeader(r);
            ReadVertices(r, input, info, heights, count, attributes, markers);
            if (!r.AtEnd) throw r.Fail("text after the last vertex");
            return input;
        }

        /// <summary>
        /// A .poly file. If it lists no vertices, nodeText (the .node file of the same name) gives them; null then fails. With heights,
        /// the first vertex attribute is the height of each point.
        /// </summary>
        public static ConstrainedMesh2Input ReadPoly(string text, Func<string> nodeText, bool heights, out PolyInfo info)
        {
            var r = new Lines(text, ".poly");
            var (count, attributes, markers) = NodeHeader(r);
            ConstrainedMesh2Input input;
            if (count == 0) {
                string node = nodeText?.Invoke() ?? throw new FormatException(".poly: it has no vertices and there is no .node file of the same name");
                input = ReadNode(node, heights, out info);
            } else {
                info = new PolyInfo();
                input = new ConstrainedMesh2Input();
                ReadVertices(r, input, info, heights, count, attributes, markers);
            }
            int n = input.Points.Count, first = info.FirstIndex;

            var sh = r.Next(1, 2, "<segments> [markers]");
            int segments = r.Int(sh[0], "number of segments", 0, 100000000);
            int segMarkers = sh.Length > 1 ? r.Int(sh[1], "number of boundary markers", 0, 1) : 0;
            for (int i = 0; i < segments; ++i) {
                var f = r.Next(3, 3 + segMarkers, "segment: <number> <a> <b>" + (segMarkers > 0 ? " [marker]" : ""));
                r.Int(f[0], "segment number");
                int a = r.Int(f[1], "end point", first, first + n - 1) - first, b = r.Int(f[2], "end point", first, first + n - 1) - first;
                int marker = f.Length > 3 ? r.Int(f[3], "marker") : 0;
                input.Segments.Add(new Mesh2Segment(a, b, marker));
            }

            var hh = r.Next(1, 1, "<holes>");
            int holes = r.Int(hh[0], "number of holes", 0, 100000000);
            for (int i = 0; i < holes; ++i) {
                var f = r.Next(3, 3, "hole: <number> <x> <y>");
                r.Int(f[0], "hole number");
                input.HoleSeeds.Add(new Vector2d(r.Number(f[1], "x"), r.Number(f[2], "y")));
            }

            if (!r.AtEnd) {
                var rh = r.Next(1, 1, "<regions>");
                int regions = r.Int(rh[0], "number of regions", 0, 100000000);
                for (int i = 0; i < regions; ++i) {
                    var f = r.Next(4, 5, "region: <number> <x> <y> <attribute> [max area]");
                    r.Int(f[0], "region number");
                    var seed = new Vector2d(r.Number(f[1], "x"), r.Number(f[2], "y"));
                    // the attribute is a label here (an integer); Triangle's attribute is a real number
                    int label = r.Int(f[3], "regional attribute (an integer label)");
                    double area = f.Length > 4 ? r.Number(f[4], "maximum area") : -1;
                    input.Regions.Add(Mesh2Region.BySeed(seed, label, area > 0 ? area : (double?)null));
                }
            }
            if (!r.AtEnd) throw r.Fail("text after the regions");

            info.Loops = AddLoops(input, info);
            return input;
        }

        /// <summary>
        /// The domain as Triangle -p gives it, as outer rings: for each connected group of segments, without its bridges and dangling
        /// chains (they enclose nothing; they stay constraints), the boundary of its outer face becomes an outer ring. A point is then
        /// in the domain iff some group encloses it, which is what eating triangles from the convex hull until a segment stops it
        /// leaves; regions inside a group (between chords, nested loops) stay, and hole points remove what they reach. A group whose
        /// segments cross or touch other than at shared end points is no plane graph: it gives no ring, and Notes says so.
        /// </summary>
        static int AddLoops(ConstrainedMesh2Input input, PolyInfo info)
        {
            var pts = input.Points;
            // one vertex per position (exact duplicates get the first index)
            var canonical = new Dictionary<(double, double), int>();
            int Canon(int i)
            {
                var key = (pts[i].x == 0 ? 0.0 : pts[i].x, pts[i].y == 0 ? 0.0 : pts[i].y);
                if (!canonical.TryGetValue(key, out int c)) canonical[key] = c = i;
                return c;
            }
            var edges = new List<Index2i>();
            var seen = new HashSet<(int, int)>();
            foreach (var s in input.Segments) {
                int a = Canon(s.A), b = Canon(s.B);
                if (a == b) continue;
                var key = a < b ? (a, b) : (b, a);
                if (seen.Add(key)) edges.Add(new Index2i(key.Item1, key.Item2));      // a repeated segment counts once
            }
            if (edges.Count == 0) return 0;

            // segments that meet other than at a shared end point: their groups are no plane graph
            var bad = new bool[edges.Count];
            var tree = new SegmentQuadtree2d(pts.Select(p => new Vector3d(p.x, p.y, 0)).ToList(), edges, false);
            var visitor = new MeetVisitor { Pts = pts, Edges = edges, Bad = bad };
            for (int e = 0; e < edges.Count; ++e) {
                visitor.E = e;
                visitor.Box = new AxisAlignedBox2d(pts[edges[e].a], pts[edges[e].b]);
                tree.Traverse(ref visitor);
            }

            var adj = new Dictionary<int, List<(int to, int edge)>>();
            for (int e = 0; e < edges.Count; ++e) {
                foreach (var (u, v) in new[] { (edges[e].a, edges[e].b), (edges[e].b, edges[e].a) }) {
                    if (!adj.TryGetValue(u, out var l)) adj[u] = l = new List<(int, int)>();
                    l.Add((v, e));
                }
            }
            // groups with a bad segment give no ring
            var group = new Dictionary<int, int>();
            var badGroup = new List<bool>();
            foreach (int start in adj.Keys.OrderBy(k => k)) {
                if (group.ContainsKey(start)) continue;
                int id = badGroup.Count; badGroup.Add(false);
                var stack = new Stack<int>(); stack.Push(start); group[start] = id;
                while (stack.Count > 0) {
                    int v = stack.Pop();
                    foreach (var (w, e) in adj[v]) {
                        if (bad[e]) badGroup[id] = true;
                        if (!group.ContainsKey(w)) { group[w] = id; stack.Push(w); }
                    }
                }
            }

            // bridges (Tarjan, iterative): they and the dangling chains enclose nothing
            var bridge = new bool[edges.Count];
            var disc = new Dictionary<int, int>(); var low = new Dictionary<int, int>();
            int time = 0;
            foreach (int root in adj.Keys.OrderBy(k => k)) {
                if (disc.ContainsKey(root)) continue;
                var stack = new Stack<(int v, int parentEdge, int next)>();
                disc[root] = low[root] = time++;
                stack.Push((root, -1, 0));
                while (stack.Count > 0) {
                    var (v, pe, next) = stack.Pop();
                    if (next < adj[v].Count) {
                        stack.Push((v, pe, next + 1));
                        var (w, e) = adj[v][next];
                        if (e == pe) continue;
                        if (!disc.ContainsKey(w)) {
                            disc[w] = low[w] = time++;
                            stack.Push((w, e, 0));
                        } else {
                            low[v] = Math.Min(low[v], disc[w]);
                        }
                    } else if (pe >= 0) {
                        int parent = edges[pe].a == v ? edges[pe].b : edges[pe].a;
                        low[parent] = Math.Min(low[parent], low[v]);
                        if (low[v] > disc[parent]) bridge[pe] = true;
                    }
                }
            }

            // the remaining edges around each vertex, counterclockwise from the direction +x, exactly (half-plane, then orientation;
            // no atan2, whose values tie for different directions at large coordinates)
            var around = new Dictionary<int, List<int>>();
            var slot = new Dictionary<(int, int), int>();      // (v, u): the position of u around v
            foreach (var kv in adj) {
                var l = kv.Value.Where(x => !bridge[x.edge] && !badGroup[group[kv.Key]]).Select(x => x.to).ToList();
                if (l.Count == 0) continue;
                Vector2d p = pts[kv.Key];
                l.Sort((x, y) => CompareAround(p, pts[x], pts[y]));
                around[kv.Key] = l;
                for (int i = 0; i < l.Count; ++i) slot[(kv.Key, l[i])] = i;
            }

            // per connected part, its outer face: at the lowest (then leftmost) vertex every neighbour lies above or to the right,
            // so the region below it is the outer face, on the left of the edge to its last neighbour counterclockwise; trace that
            // face (at each vertex, the next edge clockwise from the way back), exactly, without areas
            var part = new HashSet<int>();
            var rings = new List<int[]>();
            foreach (int start in around.Keys.OrderBy(k => k)) {
                if (part.Contains(start)) continue;
                int lowest = start;
                var stack = new Stack<int>(); stack.Push(start); part.Add(start);
                while (stack.Count > 0) {
                    int v = stack.Pop();
                    if (pts[v].y < pts[lowest].y || (pts[v].y == pts[lowest].y && pts[v].x < pts[lowest].x)) lowest = v;
                    foreach (int w in around[v]) if (part.Add(w)) stack.Push(w);
                }
                var ring = new List<int>();
                int u = lowest, n = around[lowest][around[lowest].Count - 1], guard = 0;
                do {
                    ring.Add(u);
                    var l = around[n];
                    int w = l[(slot[(n, u)] - 1 + l.Count) % l.Count];
                    u = n; n = w;
                    if (++guard > 2 * edges.Count + 8) throw new FormatException(".poly: the segment graph could not be traced");
                } while (u != lowest || n != around[lowest][around[lowest].Count - 1]);
                if (ring.Count >= 3) rings.Add(ring.ToArray());
            }
            int loops = 0;
            foreach (var ring in rings) {
                input.Rings.Add(new Mesh2Ring(ring));
                ++loops;
            }

            int badCount = badGroup.Count(b => b);
            if (badCount > 0)
                info.Notes.Add(badCount + " group(s) of segments cross or touch other than at shared end points: they are constraints only and do not bound the domain");
            if (loops == 0)
                info.Notes.Add("no group of segments encloses a region: the domain is the convex hull of the points minus the holes (as Triangle's -c)");
            return loops;
        }

        /// <summary>
        /// Marks segment E and every later segment that meets it other than at one shared end point. Segments that share an end
        /// point are told apart by their indices before any exact test, so a vertex with many segments costs comparisons, not
        /// orientation tests; each pair is decided once (only later segments).
        /// </summary>
        struct MeetVisitor : IQuadtreeVisitor2d
        {
            public IReadOnlyList<Vector2d> Pts;
            public List<Index2i> Edges;
            public bool[] Bad;
            public int E;
            public AxisAlignedBox2d Box;

            public bool TestRegion(AxisAlignedBox2d region) => region.Intersects(Box);

            public bool Visit(int f)
            {
                if (f <= E) return false;
                Index2i e = Edges[E], g = Edges[f];
                bool shares = g.a == e.a || g.a == e.b || g.b == e.a || g.b == e.b;
                if (shares) {
                    // they touch at the shared point; improper only if they also overlap along one line
                    if (!Collinear(Pts, e, g) || !Improper(Pts, e, g)) return false;
                } else {
                    if (!new AxisAlignedBox2d(Pts[g.a], Pts[g.b]).Intersects(Box) || !Meet(Pts[e.a], Pts[e.b], Pts[g.a], Pts[g.b])) return false;
                }
                Bad[E] = Bad[f] = true;
                return false;
            }
        }

        /// <summary> The closed segments ab and cd have a point in common (exact). </summary>
        static bool Meet(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            int o1 = ExactPredicates.Orient2d(a, b, c), o2 = ExactPredicates.Orient2d(a, b, d);
            int o3 = ExactPredicates.Orient2d(c, d, a), o4 = ExactPredicates.Orient2d(c, d, b);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;       // a proper crossing
            if (o1 == 0 && OnSegment(a, b, c)) return true;
            if (o2 == 0 && OnSegment(a, b, d)) return true;
            if (o3 == 0 && OnSegment(c, d, a)) return true;
            if (o4 == 0 && OnSegment(c, d, b)) return true;
            return false;
        }

        /// <summary> p, collinear with ab, lies on the closed segment ab (coordinate comparisons, exact) </summary>
        static bool OnSegment(Vector2d a, Vector2d b, Vector2d p) =>
            Math.Min(a.x, b.x) <= p.x && p.x <= Math.Max(a.x, b.x) && Math.Min(a.y, b.y) <= p.y && p.y <= Math.Max(a.y, b.y);

        static bool Collinear(IReadOnlyList<Vector2d> pts, Index2i e, Index2i f) =>
            ExactPredicates.Orient2d(pts[e.a], pts[e.b], pts[f.a]) == 0 && ExactPredicates.Orient2d(pts[e.a], pts[e.b], pts[f.b]) == 0;

        /// <summary> Counterclockwise order of directions from p, starting at +x: the half-plane first, then the orientation (exact). </summary>
        public static int CompareAround(Vector2d p, Vector2d a, Vector2d b)
        {
            int ha = a.y > p.y || (a.y == p.y && a.x > p.x) ? 0 : 1, hb = b.y > p.y || (b.y == p.y && b.x > p.x) ? 0 : 1;
            if (ha != hb) return ha.CompareTo(hb);
            return -ExactPredicates.Orient2d(p, a, b);      // a before b if b is counterclockwise of a
        }

        /// <summary> Two segments that the quadtree reports as meeting meet other than at one shared end point (a crossing, a touch, an overlap). </summary>
        static bool Improper(IReadOnlyList<Vector2d> pts, Index2i e, Index2i f)
        {
            int p = e.a == f.a || e.a == f.b ? e.a : e.b == f.a || e.b == f.b ? e.b : -1;
            if (p < 0) return true;
            // one shared end point p: improper only if they overlap, ie run the same way along one line
            int q = e.a == p ? e.b : e.a, r = f.a == p ? f.b : f.a;
            Vector2d u = pts[q] - pts[p], v = pts[r] - pts[p];
            return ExactPredicates.Orient2d(pts[p], pts[q], pts[r]) == 0 && u.x * v.x + u.y * v.y > 0;
        }

        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// A .node file: the vertices numbered from firstIndex, with the heights as one attribute if given, and boundary markers: the
        /// marker of a subsegment at the vertex (the largest, 0 if none), 1 if only unmarked subsegments meet there (as Triangle marks
        /// boundary vertices).
        /// </summary>
        public static string WriteNode(IReadOnlyList<Vector2d> vertices, IReadOnlyList<double> heights, IReadOnlyList<(int a, int b, int marker)> subsegments, int firstIndex = 1)
        {
            var marker = new int[vertices.Count];
            if (subsegments != null)
                foreach (var s in subsegments)
                    foreach (int v in new[] { s.a, s.b })
                        marker[v] = Math.Max(marker[v], s.marker != 0 ? s.marker : 1);
            var sb = new StringBuilder();
            sb.Append(vertices.Count).Append(" 2 ").Append(heights != null ? 1 : 0).Append(' ').Append(subsegments != null ? 1 : 0).Append('\n');
            for (int i = 0; i < vertices.Count; ++i) {
                sb.Append(i + firstIndex).Append(' ').Append(N(vertices[i].x)).Append(' ').Append(N(vertices[i].y));
                if (heights != null) sb.Append(' ').Append(N(heights[i]));
                if (subsegments != null) sb.Append(' ').Append(marker[i]);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary> An .ele file: the triangles (counterclockwise), with the labels as one attribute if given. </summary>
        public static string WriteEle(IReadOnlyList<Index3i> triangles, IReadOnlyList<int> labels, int firstIndex = 1)
        {
            var sb = new StringBuilder();
            sb.Append(triangles.Count).Append(" 3 ").Append(labels != null ? 1 : 0).Append('\n');
            for (int t = 0; t < triangles.Count; ++t) {
                sb.Append(t + firstIndex).Append(' ').Append(triangles[t].a + firstIndex).Append(' ').Append(triangles[t].b + firstIndex).Append(' ').Append(triangles[t].c + firstIndex);
                if (labels != null) sb.Append(' ').Append(labels[t].ToString(CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// A .poly file of a result: no vertices (they are in the .node file), the subsegments with markers, and the hole points and
        /// seed regions of the input (regions given by ring have no form here and are left out), so that the file meshed again gives
        /// the same domain, within the limits of <see cref="ReadPoly"/>.
        /// </summary>
        public static string WritePolyOfSubsegments(IReadOnlyList<(int a, int b, int marker)> subsegments, IReadOnlyList<Vector2d> holes = null,
            IReadOnlyList<Mesh2Region> regions = null, int firstIndex = 1)
        {
            var sb = new StringBuilder();
            sb.Append("0 2 0 1\n");
            sb.Append(subsegments.Count).Append(" 1\n");
            for (int i = 0; i < subsegments.Count; ++i)
                sb.Append(i + firstIndex).Append(' ').Append(subsegments[i].a + firstIndex).Append(' ').Append(subsegments[i].b + firstIndex).Append(' ').Append(subsegments[i].marker.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendHolesAndRegions(sb, holes ?? new Vector2d[0], (regions ?? new Mesh2Region[0]).Where(g => g.Ring == null).ToList(), firstIndex);
            return sb.ToString();
        }

        static void AppendHolesAndRegions(StringBuilder sb, IReadOnlyList<Vector2d> holes, IReadOnlyList<Mesh2Region> regions, int firstIndex)
        {
            sb.Append(holes.Count).Append('\n');
            for (int i = 0; i < holes.Count; ++i)
                sb.Append(i + firstIndex).Append(' ').Append(N(holes[i].x)).Append(' ').Append(N(holes[i].y)).Append('\n');
            sb.Append(regions.Count).Append('\n');
            for (int i = 0; i < regions.Count; ++i) {
                var g = regions[i];
                sb.Append(i + firstIndex).Append(' ').Append(N(g.Seed.x)).Append(' ').Append(N(g.Seed.y)).Append(' ').Append(g.Label.ToString(CultureInfo.InvariantCulture))
                    .Append(' ').Append(g.MaxArea.HasValue ? N(g.MaxArea.Value) : "-1").Append('\n');
            }
        }

        /// <summary>
        /// A .poly file of an input: its points (numbered from firstIndex, heights as an attribute), segments with markers, ring
        /// edges as segments, hole seeds and seed regions. Fails (ArgumentException) for what the format cannot hold: hole rings and
        /// ring regions (they need a point inside, which the input does not give).
        /// </summary>
        public static string WritePolyOfInput(ConstrainedMesh2Input input, int firstIndex = 1)
        {
            if (input.Rings.Any(r => r.Hole)) throw new ArgumentException("a hole ring has no .poly form (a hole needs a point inside it)");
            if (input.Regions.Any(r => r.Ring != null)) throw new ArgumentException("a region by ring has no .poly form (a region needs a seed point)");
            var segments = input.Segments.Select(s => (s.A, s.B, s.Marker)).ToList();
            foreach (var ring in input.Rings) {
                var v = ring.Vertices;
                int count = v.Length > 1 && v[v.Length - 1] == v[0] ? v.Length - 1 : v.Length;
                for (int i = 0; i < count; ++i) segments.Add((v[i], v[(i + 1) % count], ring.Marker));
            }
            var sb = new StringBuilder();
            bool hasHeights = input.Heights != null;
            sb.Append(input.Points.Count).Append(" 2 ").Append(hasHeights ? 1 : 0).Append(" 0\n");
            for (int i = 0; i < input.Points.Count; ++i) {
                sb.Append(i + firstIndex).Append(' ').Append(N(input.Points[i].x)).Append(' ').Append(N(input.Points[i].y));
                if (hasHeights) sb.Append(' ').Append(N(input.Heights[i]));
                sb.Append('\n');
            }
            sb.Append(segments.Count).Append(" 1\n");
            for (int i = 0; i < segments.Count; ++i)
                sb.Append(i + firstIndex).Append(' ').Append(segments[i].Item1 + firstIndex).Append(' ').Append(segments[i].Item2 + firstIndex).Append(' ').Append(segments[i].Item3.ToString(CultureInfo.InvariantCulture)).Append('\n');
            AppendHolesAndRegions(sb, input.HoleSeeds, input.Regions, firstIndex);
            return sb.ToString();
        }

        /// <summary> The triangles of an .ele file, 0-based, against a vertex count; firstIndex as in its .node file. </summary>
        public static List<Index3i> ReadEle(string text, int vertexCount, int firstIndex, out List<double> firstAttribute)
        {
            var r = new Lines(text, ".ele");
            var h = r.Next(1, 3, "<triangles> [nodes per triangle] [attributes]");
            int count = r.Int(h[0], "number of triangles", 0, 100000000);
            int corners = h.Length > 1 ? r.Int(h[1], "nodes per triangle") : 3;
            if (corners != 3) throw r.Fail("only 3 nodes per triangle are read (no second-order triangles)");
            int attributes = h.Length > 2 ? r.Int(h[2], "number of attributes", 0, 1000) : 0;
            var triangles = new List<Index3i>();
            firstAttribute = attributes > 0 ? new List<double>() : null;
            for (int i = 0; i < count; ++i) {
                var f = r.Next(4 + attributes, 4 + attributes, "triangle: <number> <a> <b> <c>" + (attributes > 0 ? " <" + attributes + " attributes>" : ""));
                r.Int(f[0], "triangle number");
                int a = r.Int(f[1], "corner", firstIndex, firstIndex + vertexCount - 1), b = r.Int(f[2], "corner", firstIndex, firstIndex + vertexCount - 1),
                    c = r.Int(f[3], "corner", firstIndex, firstIndex + vertexCount - 1);
                triangles.Add(new Index3i(a - firstIndex, b - firstIndex, c - firstIndex));
                if (firstAttribute != null) firstAttribute.Add(r.Number(f[4], "attribute"));
            }
            if (!r.AtEnd) throw r.Fail("text after the last triangle");
            return triangles;
        }
    }
}
