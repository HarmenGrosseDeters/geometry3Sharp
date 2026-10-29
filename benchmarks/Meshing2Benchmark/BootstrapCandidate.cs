using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;

namespace Meshing2Benchmark
{
        internal sealed class BootstrapCandidate
        {
            readonly Vector2d[] points;
            readonly List<Index3i> triangles = new List<Index3i>();
            int flips;
            readonly CancellationToken token;
            readonly Action onInsertion;
            public BootstrapCandidate(Vector2d[] points, CancellationToken token = default(CancellationToken), Action onInsertion = null) { this.points = points; this.token = token; this.onInsertion = onInsertion; }
            int Orient(int a, int b, int c) => ExactPredicates.Orient2d(points[a], points[b], points[c]);
            static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
            void Add(int a, int b, int c)
            {
                int sign = Orient(a, b, c);
                if (sign == 0) throw new InvalidOperationException("flat face");
                triangles.Add(sign > 0 ? new Index3i(a, b, c) : new Index3i(b, a, c));
            }
            Dictionary<(int, int), List<(int triangle, int a, int b, int opposite)>> Edges()
            {
                var result = new Dictionary<(int, int), List<(int, int, int, int)>>();
                for (int ti = 0; ti < triangles.Count; ++ti) {
                    var t = triangles[ti];
                    for (int i = 0; i < 3; ++i) {
                        int a = t[i], b = t[(i + 1) % 3];
                        var key = Key(a, b);
                        if (!result.TryGetValue(key, out var list)) result.Add(key, list = new List<(int, int, int, int)>());
                        list.Add((ti, a, b, t[(i + 2) % 3]));
                    }
                }
                return result;
            }
            public Index3i[] Run()
            {
                token.ThrowIfCancellationRequested();
                if (points.Length < 3) return new Index3i[0];
                int third = -1;
                for (int i = 2; i < points.Length; ++i)
                    if (Orient(0, 1, i) != 0) { third = i; break; }
                if (third < 0) return new Index3i[0];
                Add(0, 1, third);
                for (int p = 2; p < points.Length; ++p) {
                    token.ThrowIfCancellationRequested();
                    onInsertion?.Invoke();
                    token.ThrowIfCancellationRequested();
                    if (p == third) continue;
                    bool inserted = false;
                    var edges = Edges();
                    foreach (var pair in edges.OrderBy(e => e.Key.Item1).ThenBy(e => e.Key.Item2)) {
                        var edge = pair.Value[0];
                        if (Orient(edge.a, edge.b, p) != 0) continue;
                        var u = points[edge.a]; var v = points[edge.b]; var q = points[p];
                        if (q.x < Math.Min(u.x, v.x) || q.x > Math.Max(u.x, v.x) || q.y < Math.Min(u.y, v.y) || q.y > Math.Max(u.y, v.y)) continue;
                        var old = pair.Value.ToArray();
                        foreach (var face in old.OrderByDescending(f => f.triangle)) triangles.RemoveAt(face.triangle);
                        foreach (var face in old) { Add(face.a, p, face.opposite); Add(p, face.b, face.opposite); }
                        inserted = true; break;
                    }
                    if (!inserted) {
                        for (int ti = 0; ti < triangles.Count; ++ti) {
                            var t = triangles[ti];
                            if (Orient(t.a, t.b, p) > 0 && Orient(t.b, t.c, p) > 0 && Orient(t.c, t.a, p) > 0) {
                                triangles.RemoveAt(ti); Add(t.a, t.b, p); Add(t.b, t.c, p); Add(t.c, t.a, p);
                                inserted = true; break;
                            }
                        }
                    }
                    if (!inserted) {
                        var visible = edges.Values.Where(e => e.Count == 1 && Orient(e[0].a, e[0].b, p) < 0).Select(e => e[0]).ToArray();
                        if (visible.Length == 0) throw new InvalidOperationException("no visible edge");
                        foreach (var edge in visible) Add(edge.b, edge.a, p);
                    }
                    Legalize();
                }
                return triangles.ToArray();
            }
            void Legalize()
            {
                while (true) {
                    token.ThrowIfCancellationRequested();
                    bool changed = false;
                    foreach (var pair in Edges().OrderBy(e => e.Key.Item1).ThenBy(e => e.Key.Item2)) {
                        if (pair.Value.Count != 2) continue;
                        var e = pair.Value[0]; var f = pair.Value[1];
                        int a = e.a, b = e.b, c = e.opposite, d = f.opposite;
                        if (Orient(c, d, b) <= 0 || Orient(d, c, a) <= 0) continue;
                        int incircle = ExactPredicates.InCircle(points[a], points[b], points[c], points[d]);
                        var replacement = Key(c, d);
                        bool smaller = replacement.Item1 < pair.Key.Item1 || (replacement.Item1 == pair.Key.Item1 && replacement.Item2 < pair.Key.Item2);
                        if (incircle < 0 || (incircle == 0 && !smaller)) continue;
                        if (++flips > 100000) throw new InvalidOperationException("flip guard");
                        triangles.RemoveAt(Math.Max(e.triangle, f.triangle)); triangles.RemoveAt(Math.Min(e.triangle, f.triangle));
                        Add(c, d, b); Add(d, c, a); changed = true; break;
                    }
                    if (!changed) return;
                }
            }
        }

}
