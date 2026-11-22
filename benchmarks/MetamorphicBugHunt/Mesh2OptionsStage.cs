using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// QualityMesh2 with its options (hunt 1 ran MinAngle and MaxArea): Conforming (every triangle has an empty circumcircle,
    /// checked with a brute-force in-circle test in double with a margin), the default (locally Delaunay across unconstrained
    /// edges), SizeFunction (the longest edge at most h at the centroid), a small SteinerLimit (a valid mesh that says so),
    /// SnapTolerance. Structure: Neighbours are mutual, the subsegments are edges of the mesh and cover the rings. Relations: a
    /// second Compute() on the instance, a far copy of the domain in one input (twice the area, the same bounds), duplicated
    /// input points, the points in another order, 2^k (the same triangles).
    /// </summary>
    public static class Mesh2OptionsStage
    {
        const string S = "mesh2d2";

        sealed class Dom { public string Name; public List<Vector2d> Outer; public List<List<Vector2d>> Holes = new List<List<Vector2d>>(); public List<Vector2d> Free = new List<Vector2d>(); }

        static IEnumerable<Dom> Domains()
        {
            yield return new Dom { Name = "square", Outer = Shapes.Ngon(4, 1, 0.3) };
            yield return new Dom { Name = "star", Outer = Shapes.Star(7, 1.2, 0.5, 0.1) };
            yield return new Dom { Name = "ngon with hole and points", Outer = Shapes.Ngon(17, 2, 0.05), Holes = { Shapes.Ngon(5, 0.7, 0.3) }, Free = { new Vector2d(1.3, 0.2), new Vector2d(-1.2, -0.7), new Vector2d(0.1, 1.5) } };
            yield return new Dom { Name = "L", Outer = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2, 1), new Vector2d(1, 1), new Vector2d(1, 2), new Vector2d(0, 2) } };
            yield return new Dom { Name = "thin wedge", Outer = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(5, 0), new Vector2d(5, 0.3) } };
        }

        static double Area(IList<Vector2d> r) { double s = 0; for (int i = 0; i < r.Count; ++i) { Vector2d a = r[i] - r[0], b = r[(i + 1) % r.Count] - r[0]; s += a.x * b.y - a.y * b.x; } return s / 2; }

        static ConstrainedMesh2Input Input(Dom d, Func<Vector2d, Vector2d> map, bool farCopy = false, bool duplicates = false, int shuffle = 0)
        {
            var pts = new List<Vector2d>(); var rings = new List<(int[], bool)>();
            void Add(Func<Vector2d, Vector2d> f)
            {
                void Ring(List<Vector2d> r, bool hole) { int first = pts.Count; foreach (Vector2d p in r) pts.Add(f(p)); rings.Add((Enumerable.Range(first, r.Count).ToArray(), hole)); }
                Ring(d.Outer, false);
                foreach (var h in d.Holes) Ring(h, true);
                foreach (Vector2d p in d.Free) pts.Add(f(p));
            }
            Add(map);
            if (farCopy) Add(p => map(p + new Vector2d(64, 0)));
            if (duplicates) { pts.Add(pts[0]); pts.Add(pts[rings[0].Item1[1]]); foreach (Vector2d p in d.Free.Take(1)) pts.Add(map(p)); }
            // another order of the points
            int[] perm = Enumerable.Range(0, pts.Count).ToArray();
            if (shuffle != 0) { var rng = new Random(shuffle); for (int i = perm.Length - 1; i > 0; --i) { int j = rng.Next(i + 1); int t = perm[i]; perm[i] = perm[j]; perm[j] = t; } }
            var input = new ConstrainedMesh2Input();
            var placed = new Vector2d[pts.Count];
            for (int i = 0; i < pts.Count; ++i) placed[perm[i]] = pts[i];
            input.Points.AddRange(placed);
            foreach (var (ids, hole) in rings) input.Rings.Add(new Mesh2Ring(ids.Select(i => perm[i]).ToArray(), hole));
            return input;
        }

        static double InCircle(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
        {
            double ax = a.x - d.x, ay = a.y - d.y, bx = b.x - d.x, by = b.y - d.y, cx = c.x - d.x, cy = c.y - d.y;
            return (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay) + (cx * cx + cy * cy) * (ax * by - bx * ay);
        }

        static string Structure(ConstrainedMesh2 m, ConstrainedMesh2Input input, double area, double tolerance)
        {
            double sum = 0;
            var dir = new Dictionary<(int, int), int>();
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                if (!(a.IsFinite && b.IsFinite && c.IsFinite)) return $"triangle {t} is not finite";
                double ar = ((b - a).x * (c - a).y - (b - a).y * (c - a).x) / 2;
                if (!(ar > 0)) return $"triangle {t} has the area {ar:R}";
                sum += ar;
                if (tv.a > tv.b || tv.a > tv.c) return $"triangle {t} does not start at its smallest id";
                for (int k = 0; k < 3; ++k) { var e = (tv[k], tv[(k + 1) % 3]); if (dir.ContainsKey(e)) return $"directed edge {e} in two triangles"; dir[e] = t; }
            }
            if (Math.Abs(sum - area) > tolerance * Math.Abs(area)) return $"area {sum:R} vs the domain's {area:R}";
            if (m.Neighbours.Count != m.Triangles.Count) return "Neighbours count";
            for (int t = 0; t < m.Triangles.Count; ++t) for (int k = 0; k < 3; ++k) {
                Index3i tv = m.Triangles[t];
                int u = tv[(k + 1) % 3], v = tv[(k + 2) % 3];   // the edge opposite corner k
                int nb = m.Neighbours[t][k];
                dir.TryGetValue((v, u), out int other); if (!dir.ContainsKey((v, u))) other = -1;
                if (nb != other) return $"Neighbours[{t}][{k}] = {nb}, the triangle across ({u}, {v}) is {other}";
            }
            foreach (Mesh2Subsegment s in m.Subsegments) if (!dir.ContainsKey((s.A, s.B)) && !dir.ContainsKey((s.B, s.A))) return $"subsegment ({s.A}, {s.B}) is not an edge of the mesh";
            for (int i = 0; i < input.Points.Count; ++i) { int v = m.InputToVertex[i]; if (v < 0 || v >= m.Vertices.Count || m.Vertices[v] != input.Points[i]) return $"input point {i} is not a vertex at its position"; }
            // the ring edges are covered by subsegments: every boundary edge of the mesh is a subsegment
            var sub = new HashSet<(int, int)>(); foreach (Mesh2Subsegment s in m.Subsegments) { sub.Add((s.A, s.B)); sub.Add((s.B, s.A)); }
            foreach (var kv in dir) if (!dir.ContainsKey((kv.Key.Item2, kv.Key.Item1)) && !sub.Contains(kv.Key)) return $"boundary edge {kv.Key} is not a subsegment";
            return "";
        }

        static string Delaunay(ConstrainedMesh2 m, bool conforming)
        {
            var sub = new HashSet<(int, int)>(); foreach (Mesh2Subsegment s in m.Subsegments) { sub.Add((s.A, s.B)); sub.Add((s.B, s.A)); }
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                double scale = Math.Max((b - a).LengthSquared, (c - a).LengthSquared); double eps = 1e-9 * scale * scale;
                for (int k = 0; k < 3; ++k) {
                    int nb = m.Neighbours[t][k]; if (nb < 0) continue;
                    int u = tv[(k + 1) % 3], v = tv[(k + 2) % 3];
                    if (!conforming && sub.Contains((u, v))) continue;
                    Index3i nv = m.Triangles[nb];
                    int opp = nv.a != u && nv.a != v ? nv.a : nv.b != u && nv.b != v ? nv.b : nv.c;
                    if (InCircle(a, b, c, m.Vertices[opp]) > eps) return $"vertex {opp} is inside the circumcircle of triangle {t} (across edge ({u}, {v}){(sub.Contains((u, v)) ? ", a subsegment" : "")})";
                }
            }
            return "";
        }

        static string Bounds(ConstrainedMesh2 m, double angle, double? maxArea, Func<double, double, double> size)
        {
            var bad = new HashSet<int>(m.BadTriangles);
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                double A(Vector2d p, Vector2d q, Vector2d r) { Vector2d u = q - p, v = r - p; return Math.Atan2(Math.Abs(u.x * v.y - u.y * v.x), u.Dot(v)) * 180 / Math.PI; }
                double min = Math.Min(A(a, b, c), Math.Min(A(b, c, a), A(c, a, b)));
                if (!bad.Contains(t) && min < angle - 1e-9) return $"triangle {t} outside BadTriangles has a minimum angle of {min:R}";
                if (bad.Contains(t) && min >= angle + 1e-9) return $"triangle {t} is in BadTriangles with a minimum angle of {min:R}";
                double ar = ((b - a).x * (c - a).y - (b - a).y * (c - a).x) / 2;
                if (maxArea.HasValue && ar > maxArea.Value * (1 + 1e-12)) return $"triangle {t} has the area {ar:R}, MaxArea {maxArea.Value:R}";
                if (size != null) { Vector2d g = (a + b + c) / 3; double h = size(g.x, g.y), longest = Math.Max((a - b).Length, Math.Max((b - c).Length, (c - a).Length)); if (longest > h * (1 + 1e-12)) return $"triangle {t} has a longest edge of {longest:R}, the size function gives {h:R}"; }
            }
            return "";
        }

        public static void Run()
        {
            foreach (Dom d in Domains()) {
                if (Area(d.Outer) < 0) d.Outer.Reverse();
                double area = Area(d.Outer) - d.Holes.Sum(h => Math.Abs(Area(h)));
                Func<double, double, double> sizeF = (x, y) => 0.15 + 0.1 * Math.Abs(x);
                var options = new (string, Func<double, QualityMesh2>, bool conforming, bool size)[] {
                    ("default 20", s => new QualityMesh2(), false, false),
                    ("conforming", s => new QualityMesh2 { Conforming = true, MinAngle = 0 }, true, false),
                    ("conforming 25", s => new QualityMesh2 { Conforming = true, MinAngle = 25, MaxArea = area * s * s / 100 }, true, false),
                    ("angle 33", s => new QualityMesh2 { MinAngle = 33 }, false, false),
                    ("size function", s => new QualityMesh2 { SizeFunction = (x, y) => s * sizeF(x / s, y / s) }, false, true),
                    ("steiner limit 5", s => new QualityMesh2 { MinAngle = 30, MaxArea = area * s * s / 500, SteinerLimit = 5 }, false, false),
                    ("no angle", s => new QualityMesh2 { MinAngle = 0 }, false, false),
                    ("snap", s => new QualityMesh2 { SnapTolerance = 1e-3 * s }, false, false),
                };
                foreach (var (on, make, conforming, size) in options) {
                    string c = d.Name + " | " + on;
                    try {
                        ConstrainedMesh2Input input = Input(d, p => p);
                        QualityMesh2 q = make(1);
                        ConstrainedTriangulation2Result r = Program.Timed(() => q.Compute(input), out long ms);
                        Program.Report(S, c + " | time", "", ms);
                        if (r.Status != Triangulation2Status.Success) { Program.Report(S, c + " | status", $"{r.Status} {r.FailureKind} {r.FailureDetail}"); continue; }
                        ConstrainedMesh2 m = r.Mesh;
                        bool snap = on == "snap";
                        bool limit = m.SteinerLimitReached;
                        if (limit) Program.Info(S, c, "Steiner limit reached");
                        if (on == "steiner limit 5") Program.Report(S, c + " | the limit is reported", limit ? "" : "a limit of 5 Steiner points was not reached, or not reported");
                        if (on == "steiner limit 5") Program.Report(S, c + " | at most 5 Steiner points", m.Vertices.Count - m.InputCount <= 5 ? "" : $"{m.Vertices.Count - m.InputCount} vertices beyond the input");
                        if (!snap) Program.Report(S, c + " | structure", Structure(m, input, area, 1e-9));
                        else { string st = Structure(m, input, area, 1e-2); if (st.StartsWith("input point")) st = ""; Program.Report(S, c + " | structure (snapped)", st); }
                        if (!snap) Program.Report(S, c + " | Delaunay", Delaunay(m, conforming));
                        if (!limit && !snap) Program.Report(S, c + " | bounds", Bounds(m, q.MinAngle, q.MaxArea, size ? sizeF : null));
                        if (m.BadTriangles.Count > 0) Program.Info(S, c, $"{m.BadTriangles.Count} bad triangles of {m.Triangles.Count}");
                        // reuse
                        ConstrainedTriangulation2Result again = q.Compute(input);
                        Program.Report(S, c + " | reuse", again.Status == r.Status && again.Mesh.Triangles.SequenceEqual(m.Triangles) && again.Mesh.Vertices.SequenceEqual(m.Vertices) ? "" : "another mesh from the second Compute()");
                        // 2^k
                        foreach (double s in new[] { 1.0 / 1024, 1024.0 }) {
                            ConstrainedTriangulation2Result rs = make(s).Compute(Input(d, p => p * s));
                            Program.Report(S, c + " | scale " + s, rs.Status == r.Status && rs.Mesh.Triangles.SequenceEqual(m.Triangles) && rs.Mesh.Vertices.Select(p => p / s).SequenceEqual(m.Vertices) ? "" : rs.Status != r.Status ? "status " + rs.Status : $"{rs.Mesh.Triangles.Count} vs {m.Triangles.Count} triangles, or other vertices");
                        }
                        if (snap) continue;
                        // a far copy of the domain in the same input
                        {
                            ConstrainedMesh2Input two = Input(d, p => p, farCopy: true);
                            QualityMesh2 q2 = size ? new QualityMesh2 { SizeFunction = (x, y) => sizeF(x > 32 ? x - 64 : x, y) } : make(1);
                            ConstrainedTriangulation2Result r2 = q2.Compute(two);
                            if (r2.Status != r.Status) Program.Report(S, c + " | far copy", $"status {r2.Status} {r2.FailureKind} {r2.FailureDetail}");
                            else {
                                Program.Report(S, c + " | far copy structure", Structure(r2.Mesh, two, 2 * area, 1e-9));
                                Program.Report(S, c + " | far copy Delaunay", Delaunay(r2.Mesh, conforming));
                                if (!limit && !r2.Mesh.SteinerLimitReached) Program.Report(S, c + " | far copy bounds", Bounds(r2.Mesh, q.MinAngle, q.MaxArea, size ? (Func<double, double, double>)((x, y) => sizeF(x > 32 ? x - 64 : x, y)) : null));
                                if (!limit) { double ratio = r2.Mesh.Triangles.Count / (2.0 * m.Triangles.Count); if (ratio > 1.5 || ratio < 0.67) Program.Report(S, c + " | far copy size", $"{r2.Mesh.Triangles.Count} triangles vs 2 x {m.Triangles.Count}"); }
                            }
                        }
                        // duplicated input points
                        {
                            ConstrainedMesh2Input dup = Input(d, p => p, duplicates: true);
                            ConstrainedTriangulation2Result rd = make(1).Compute(dup);
                            if (rd.Status != r.Status) Program.Report(S, c + " | duplicated points", $"status {rd.Status} {rd.FailureKind} {rd.FailureDetail}");
                            else {
                                Program.Report(S, c + " | duplicated points structure", Structure(rd.Mesh, dup, area, 1e-9));
                                // (the duplicates shift the ids of the Steiner points, and with them the order of ties: about the same mesh, not the same)
                                Program.Report(S, c + " | duplicated points Delaunay", Delaunay(rd.Mesh, conforming));
                                if (!limit && !rd.Mesh.SteinerLimitReached) Program.Report(S, c + " | duplicated points bounds", Bounds(rd.Mesh, q.MinAngle, q.MaxArea, size ? sizeF : null));
                                if (rd.Mesh.Triangles.Count != m.Triangles.Count && !limit) Program.Info(S, c + " | duplicated points", $"{rd.Mesh.Triangles.Count} vs {m.Triangles.Count} triangles");
                            }
                        }
                        // the points in another order
                        {
                            ConstrainedMesh2Input sh = Input(d, p => p, shuffle: 7);
                            ConstrainedTriangulation2Result rp = make(1).Compute(sh);
                            if (rp.Status != r.Status) Program.Report(S, c + " | shuffled points", $"status {rp.Status} {rp.FailureKind} {rp.FailureDetail}");
                            else {
                                Program.Report(S, c + " | shuffled points structure", Structure(rp.Mesh, sh, area, 1e-9));
                                Program.Report(S, c + " | shuffled points Delaunay", Delaunay(rp.Mesh, conforming));
                                if (!limit && !rp.Mesh.SteinerLimitReached) Program.Report(S, c + " | shuffled points bounds", Bounds(rp.Mesh, q.MinAngle, q.MaxArea, size ? sizeF : null));
                                if (rp.Mesh.Triangles.Count != m.Triangles.Count) Program.Info(S, c + " | shuffled points", $"{rp.Mesh.Triangles.Count} vs {m.Triangles.Count} triangles");
                            }
                        }
                    }
                    catch (Exception e) { Program.Report(S, c, "threw " + e.GetType().Name + ": " + e.Message + " at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
                }
            }
        }
    }
}
