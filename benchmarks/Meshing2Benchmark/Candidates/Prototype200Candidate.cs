using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;

namespace Meshing2Acceptance
{
    /// <summary>
    /// The kernel of the abandoned PR #200 (comp_geom/DelaunayMesh2.cs at a2c0d64), copied here with only its cancellation checks removed, so that every gate has
    /// something real to judge until the production mesher exists. It stays out of the library: it is correct on all inputs tried
    /// but about quadratic in time and memory (docs/design/MESHING2D_PLAN.md, section 2). Session 2 replaces it and deletes this file.
    /// </summary>
    public sealed class Prototype200Candidate : IMeshing2Candidate
    {
        public string Name => "pr200-prototype";

        const double CoordinateLimit = 1e70;

        public Mesh2Output Run(Mesh2Input input)
        {
            var vertices = new Vector2d[input.Count];
            var mapping = new int[input.Count];
            var unique = new Dictionary<Vector2d, int>();
            var ids = new List<int>();
            for (int i = 0; i < vertices.Length; ++i) {
                double x = input.X[i], y = input.Y[i];
                if (double.IsNaN(x) || double.IsInfinity(x) || Math.Abs(x) > CoordinateLimit) throw new ArgumentException("points[" + i + "] has an invalid x coordinate.");
                if (double.IsNaN(y) || double.IsInfinity(y) || Math.Abs(y) > CoordinateLimit) throw new ArgumentException("points[" + i + "] has an invalid y coordinate.");
                var point = new Vector2d(x == 0 ? 0.0 : x, y == 0 ? 0.0 : y);
                vertices[i] = point;
                if (!unique.TryGetValue(point, out int first)) {
                    unique.Add(point, first = i);
                    ids.Add(i);
                }
                mapping[i] = first;
            }
            var builder = new Builder(vertices);
            var output = new Mesh2Output {
                VX = vertices.Select(p => p.x).ToArray(), VY = vertices.Select(p => p.y).ToArray(), InputToVertex = mapping
            };
            if (!builder.Compute(ids, out var faces)) {
                output.Status = Mesh2Status.Failed;
                output.FailureKind = "kernel";
                return output;
            }
            output.Status = faces.Length == 0 ? Mesh2Status.Empty : Mesh2Status.Success;
            output.Triangles = faces.Select(t => new[] { t.a, t.b, t.c }).ToArray();
            return output;
        }

        struct EdgeUse
        {
            internal int Triangle, Opposite, A, B, C;
            internal EdgeUse(int triangle, int opposite, int a, int b, int c) { Triangle = triangle; Opposite = opposite; A = a; B = b; C = c; }
        }

        static long EdgeKey(int a, int b)
        {
            if (a > b) { int t = a; a = b; b = t; }
            return ((long)a << 32) | (uint)b;
        }

        static SortedDictionary<long, List<EdgeUse>> BuildEdges(IReadOnlyList<Index3i> faces)
        {
            var edges = new SortedDictionary<long, List<EdgeUse>>();
            for (int ti = 0; ti < faces.Count; ++ti) {
                var face = faces[ti];
                for (int k = 0; k < 3; ++k) {
                    int a = face[(k + 1) % 3], b = face[(k + 2) % 3];
                    long key = EdgeKey(a, b);
                    if (!edges.TryGetValue(key, out var uses)) edges.Add(key, uses = new List<EdgeUse>(2));
                    uses.Add(new EdgeUse(ti, k, a, b, face[k]));
                }
            }
            return edges;
        }

        sealed class Builder
        {
            readonly Vector2d[] vertices;
            readonly List<Index3i> triangles = new List<Index3i>();

            internal Builder(Vector2d[] vertices) { this.vertices = vertices; }
            int Orient(int a, int b, int c) => ExactPredicates.Orient2d(vertices[a], vertices[b], vertices[c]);

            bool Add(int a, int b, int c)
            {
                int sign = Orient(a, b, c);
                if (sign == 0) return false;
                triangles.Add(sign > 0 ? new Index3i(a, b, c) : new Index3i(b, a, c));
                return true;
            }

            bool OnSegment(int a, int b, int p)
            {
                if (Orient(a, b, p) != 0) return false;
                var u = vertices[a]; var v = vertices[b]; var q = vertices[p];
                return Math.Min(u.x, v.x) <= q.x && q.x <= Math.Max(u.x, v.x) &&
                    Math.Min(u.y, v.y) <= q.y && q.y <= Math.Max(u.y, v.y);
            }

            internal bool Compute(List<int> ids, out Index3i[] faces)
            {
                faces = null;
                if (ids.Count < 3) { faces = new Index3i[0]; return true; }
                int third = -1;
                for (int i = 2; i < ids.Count; ++i) {
                    if (Orient(ids[0], ids[1], ids[i]) != 0) { third = i; break; }
                }
                if (third < 0) { faces = new Index3i[0]; return true; }
                Add(ids[0], ids[1], ids[third]);
                for (int i = 2; i < ids.Count; ++i) {
                    if (i == third) continue;
                    if (!Insert(ids[i])) return false;
                    if (!Legalize()) return false;
                }
                faces = triangles.Select(Canonical).ToArray();
                Array.Sort(faces, (a, b) => {
                    int c = a.a.CompareTo(b.a);
                    if (c == 0) c = a.b.CompareTo(b.b);
                    return c == 0 ? a.c.CompareTo(b.c) : c;
                });
                return true;
            }

            static Index3i Canonical(Index3i t)
            {
                if (t.b < t.a && t.b < t.c) return new Index3i(t.b, t.c, t.a);
                if (t.c < t.a && t.c < t.b) return new Index3i(t.c, t.a, t.b);
                return t;
            }

            bool Insert(int p)
            {
                var edges = BuildEdges(triangles);
                foreach (var pair in edges) {
                    var uses = pair.Value;
                    if (uses.Count == 0 || uses.Count > 2) return false;
                    var edge = uses[0];
                    if (!OnSegment(edge.A, edge.B, p)) continue;
                    if (uses.Count == 2 && (uses[0].A != uses[1].B || uses[0].B != uses[1].A)) return false;
                    var old = uses.ToArray();
                    foreach (var face in old.OrderByDescending(x => x.Triangle)) triangles.RemoveAt(face.Triangle);
                    foreach (var face in old)
                        if (!Add(face.A, p, face.C) || !Add(p, face.B, face.C)) return false;
                    return true;
                }
                for (int ti = 0; ti < triangles.Count; ++ti) {
                    var t = triangles[ti];
                    if (Orient(t.a, t.b, p) > 0 && Orient(t.b, t.c, p) > 0 && Orient(t.c, t.a, p) > 0) {
                        triangles.RemoveAt(ti);
                        return Add(t.a, t.b, p) && Add(t.b, t.c, p) && Add(t.c, t.a, p);
                    }
                }
                bool visible = false;
                foreach (var pair in edges) {
                    var uses = pair.Value;
                    if (uses.Count != 1) continue;
                    var edge = uses[0];
                    if (Orient(edge.A, edge.B, p) < 0) {
                        visible = true;
                        if (!Add(edge.B, edge.A, p)) return false;
                    }
                }
                return visible;
            }

            bool Legalize()
            {
                var seen = new HashSet<string>();
                while (true) {
                    var edges = BuildEdges(triangles);
                    var state = new StringBuilder();
                    foreach (long key in edges.Keys) state.Append(key).Append(',');
                    if (!seen.Add(state.ToString())) return false;
                    bool changed = false;
                    foreach (var pair in edges) {
                        var uses = pair.Value;
                        if (uses.Count != 2) continue;
                        var e = uses[0]; var f = uses[1];
                        int a = e.A, b = e.B, c = e.C, d = f.C;
                        if (e.A != f.B || e.B != f.A) return false;
                        if (Orient(c, d, b) <= 0 || Orient(d, c, a) <= 0) continue;
                        int incircle = ExactPredicates.InCircle(vertices[a], vertices[b], vertices[c], vertices[d]);
                        long replacement = EdgeKey(c, d);
                        if (incircle < 0 || (incircle == 0 && replacement >= pair.Key)) continue;
                        triangles.RemoveAt(Math.Max(e.Triangle, f.Triangle));
                        triangles.RemoveAt(Math.Min(e.Triangle, f.Triangle));
                        if (!Add(c, d, b) || !Add(d, c, a)) return false;
                        changed = true;
                        break;
                    }
                    if (!changed) return true;
                }
            }
        }
    }
}
