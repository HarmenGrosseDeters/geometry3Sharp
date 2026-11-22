using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace MetamorphicBugHunt
{
    /// <summary>
    /// QualityMesh2: a Success is a triangulation of the domain (area, orientation, edge manifold, every input point kept, the
    /// angle bound outside BadTriangles), and it is the same domain under 2^k scale (bit for bit), similarities of the plane, a
    /// rotation of the ring's start vertex, a reversal of the ring, a mirror, and a split of the domain by a segment.
    /// </summary>
    public static class Mesh2Stage
    {
        const string S = "mesh2d";

        sealed class Domain { public string Name; public List<Vector2d> Outer; public List<List<Vector2d>> Holes = new List<List<Vector2d>>(); }

        static IEnumerable<Domain> Domains()
        {
            yield return new Domain { Name = "square", Outer = Shapes.Ngon(4, 1, Math.PI / 4) };
            yield return new Domain { Name = "star", Outer = Shapes.Star(7, 1.2, 0.5) };
            yield return new Domain { Name = "sharp star", Outer = Shapes.Star(9, 1.5, 0.2) };
            yield return new Domain { Name = "ngon with hole", Outer = Shapes.Ngon(17, 2), Holes = { Shapes.Ngon(5, 0.7, 0.3) } };
            yield return new Domain { Name = "star with two holes", Outer = Shapes.Star(5, 3, 1.6), Holes = { Shapes.Ngon(6, 0.4, 0.1).Select(p => p + new Vector2d(0.5, 0.2)).ToList(), Shapes.Ngon(3, 0.3).Select(p => p + new Vector2d(-0.6, -0.3)).ToList() } };
            yield return new Domain { Name = "thin rectangle", Outer = new List<Vector2d> { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 0.01), new Vector2d(0, 0.01) } };
            yield return new Domain { Name = "hole near the boundary", Outer = Shapes.Ngon(4, 1, Math.PI / 4), Holes = { Shapes.Ngon(8, 0.2).Select(p => p + new Vector2d(0.49, 0)).ToList() } };
            var comb = new List<Vector2d>();
            for (int i = 0; i < 8; ++i) { comb.Add(new Vector2d(i, 0)); comb.Add(new Vector2d(i + 0.5, 0)); comb.Add(new Vector2d(i + 0.5, 3)); comb.Add(new Vector2d(i + 0.55, 3)); comb.Add(new Vector2d(i + 0.55, 0)); }
            comb.Add(new Vector2d(8, 0)); comb.Add(new Vector2d(8, -1)); comb.Add(new Vector2d(0, -1));
            yield return new Domain { Name = "comb", Outer = comb };
        }

        static double Area(IList<Vector2d> ring)
        {
            double s = 0; Vector2d o = ring[0];
            for (int i = 0; i < ring.Count; ++i) { Vector2d a = ring[i] - o, b = ring[(i + 1) % ring.Count] - o; s += a.x * b.y - a.y * b.x; }
            return s / 2;
        }

        static ConstrainedMesh2Input Input(Domain d, Func<Vector2d, Vector2d> map, int rotate = 0, bool reverse = false, bool split = false)
        {
            var input = new ConstrainedMesh2Input();
            void Ring(List<Vector2d> pts, bool hole)
            {
                var r = Enumerable.Range(0, pts.Count).Select(i => pts[(i + rotate) % pts.Count]).ToList();
                if (reverse) r.Reverse();
                int first = input.Points.Count;
                foreach (Vector2d p in r) input.Points.Add(map(p));
                input.Rings.Add(new Mesh2Ring(Enumerable.Range(first, r.Count).ToArray(), hole));
            }
            Ring(d.Outer, false);
            foreach (var h in d.Holes) Ring(h, true);
            if (split) {
                // a segment between two outer vertices that sees no hole is not assumed: a free segment well inside the domain
                Vector2d c = Vector2d.Zero; foreach (Vector2d p in d.Outer) c += p; c /= d.Outer.Count;
                Vector2d a = c + 0.02 * (d.Outer[0] - c), b = c + 0.02 * (d.Outer[d.Outer.Count / 2] - c) + new Vector2d(0, 1e-3);
                int ia = input.Points.Count; input.Points.Add(map(a)); input.Points.Add(map(b));
                input.Segments.Add(new Mesh2Segment(ia, ia + 1));
            }
            return input;
        }

        static string Check(ConstrainedMesh2 m, ConstrainedMesh2Input input, double area, double minAngle, out double meshArea, out double worstAngle)
        {
            meshArea = 0; worstAngle = 180;
            var bad = new HashSet<int>(m.BadTriangles);
            var dir = new HashSet<(int, int)>();
            for (int t = 0; t < m.Triangles.Count; ++t) {
                Index3i tv = m.Triangles[t];
                Vector2d a = m.Vertices[tv.a], b = m.Vertices[tv.b], c = m.Vertices[tv.c];
                if (!(a.IsFinite && b.IsFinite && c.IsFinite)) return $"triangle {t} is not finite";
                double ar = ((b - a).x * (c - a).y - (b - a).y * (c - a).x) / 2;
                if (!(ar > 0)) return $"triangle {t} has area {ar:R} (not counter-clockwise)";
                meshArea += ar;
                for (int k = 0; k < 3; ++k) if (!dir.Add((tv[k], tv[(k + 1) % 3]))) return $"directed edge ({tv[k]}, {tv[(k + 1) % 3]}) in two triangles";
                double ang = MinAngle(a, b, c);
                if (!bad.Contains(t)) worstAngle = Math.Min(worstAngle, ang);
            }
            if (Math.Abs(meshArea - area) > 1e-9 * Math.Abs(area)) return $"area {meshArea:R} vs the domain's {area:R}";
            for (int i = 0; i < input.Points.Count; ++i)
                if (m.InputToVertex[i] < 0 || (m.Vertices[m.InputToVertex[i]] - input.Points[i]).Length > 0) return $"input point {i} is not a vertex of the mesh at its position";
            return "";
        }

        static double MinAngle(Vector2d a, Vector2d b, Vector2d c)
        {
            double A(Vector2d p, Vector2d q, Vector2d r) { Vector2d u = q - p, v = r - p; return Math.Atan2(Math.Abs(u.x * v.y - u.y * v.x), u.Dot(v)) * 180 / Math.PI; }
            return Math.Min(A(a, b, c), Math.Min(A(b, c, a), A(c, a, b)));
        }

        static ConstrainedTriangulation2Result Mesh(string name, ConstrainedMesh2Input input, double minAngle, double? maxArea)
        {
            try {
                var r = Program.Timed(() => new QualityMesh2 { MinAngle = minAngle, MaxArea = maxArea }.Compute(input), out long ms);
                if (ms >= Program.SlowMs) Program.Report(S, name, "", ms);
                return r;
            } catch (Exception ex) { Program.Report(S, name, "threw " + ex.GetType().Name + ": " + ex.Message); return null; }
        }

        public static void Run()
        {
            Matrix2d R(double deg) => new Matrix2d(deg * Math.PI / 180);
            foreach (Domain d in Domains()) {
                if (Area(d.Outer) < 0) d.Outer.Reverse();
                double area = Area(d.Outer) - d.Holes.Sum(h => Math.Abs(Area(h)));
                foreach (double angle in new[] { 20.0, 30.0 }) {
                    string tag = $"{d.Name} {angle}";
                    double? maxArea = area / 200;
                    ConstrainedMesh2Input input = Input(d, p => p);
                    ConstrainedTriangulation2Result r = Mesh(tag, input, angle, maxArea);
                    if (r == null) continue;
                    if (r.Status != Triangulation2Status.Success) { Program.Info(S, tag, $"status {r.Status} {r.FailureKind} {r.FailureDetail}"); continue; }
                    string c = Check(r.Mesh, input, area, angle, out double meshArea, out double worst);
                    Program.Report(S, tag + " valid", c);
                    Program.Report(S, tag + " angle bound", worst >= angle - 1e-9 || r.Mesh.SteinerLimitReached ? "" : $"a triangle outside BadTriangles has a min angle of {worst:R} degrees (bound {angle})");
                    if (r.Mesh.BadTriangles.Count > 0) Program.Info(S, tag, $"{r.Mesh.BadTriangles.Count} bad triangles reported of {r.Mesh.Triangles.Count}");
                    if (r.Mesh.SteinerLimitReached) Program.Info(S, tag, "Steiner limit reached");
                    double biggest = 0;
                    foreach (Index3i tv in r.Mesh.Triangles) { Vector2d a = r.Mesh.Vertices[tv.a], b = r.Mesh.Vertices[tv.b], cc = r.Mesh.Vertices[tv.c]; biggest = Math.Max(biggest, ((b - a).x * (cc - a).y - (b - a).y * (cc - a).x) / 2); }
                    Program.Report(S, tag + " area bound", biggest <= maxArea.Value * (1 + 1e-12) || r.Mesh.SteinerLimitReached ? "" : $"a triangle of area {biggest:R}, MaxArea {maxArea.Value:R}");
                    int count = r.Mesh.Triangles.Count;

                    var variants = new List<(string, ConstrainedMesh2Input, double areaFactor, bool exact)>();
                    variants.Add(("2^-10", Input(d, p => p / 1024), 1.0 / 1024 / 1024, true));
                    variants.Add(("2^10", Input(d, p => p * 1024), 1024.0 * 1024, true));
                    variants.Add(("rotated 31", Input(d, p => R(31) * p), 1, false));
                    variants.Add(("utm", Input(d, p => p + new Vector2d(632117.25, 5712043.5)), 1, false));
                    variants.Add(("scale 1e-3", Input(d, p => p * 1e-3), 1e-6, false));
                    variants.Add(("mirrored", Input(d, p => new Vector2d(-p.x, p.y), 0, true), 1, false));
                    variants.Add(("start vertex +1", Input(d, p => p, 1), 1, false));
                    variants.Add(("ring reversed", Input(d, p => p, 0, true), 1, false));
                    variants.Add(("split by a segment", Input(d, p => p, 0, false, true), 1, false));
                    foreach (var (vn, vi, af, exact) in variants) {
                        ConstrainedTriangulation2Result v = Mesh(tag + " " + vn, vi, angle, maxArea * af);
                        if (v == null) continue;
                        if (v.Status != Triangulation2Status.Success) { Program.Report(S, tag + " " + vn + " status", $"{v.Status} {v.FailureKind} {v.FailureDetail} where the identity run succeeds"); continue; }
                        // (the tolerance of the area far from the origin: the input points are rounded there)
                        string cv = Check(v.Mesh, vi, area * af, angle, out double va, out double vw);
                        if (vn == "utm" && cv.StartsWith("area")) cv = Math.Abs(va - area) <= 1e-6 * area ? "" : cv;
                        Program.Report(S, tag + " " + vn + " valid", cv);
                        Program.Report(S, tag + " " + vn + " angle bound", vw >= angle - 1e-9 || v.Mesh.SteinerLimitReached ? "" : $"a triangle outside BadTriangles has a min angle of {vw:R} degrees (bound {angle})");
                        if (exact) Program.Report(S, tag + " " + vn + " same triangles", v.Mesh.Triangles.Count == count && v.Mesh.Triangles.SequenceEqual(r.Mesh.Triangles) ? "" : $"{count} triangles vs {v.Mesh.Triangles.Count}");
                        else {
                            // a cost relation: the same domain needs about the same number of triangles
                            double ratio = (double)v.Mesh.Triangles.Count / count;
                            if (ratio > 2 || ratio < 0.5) Program.Report(S, tag + " " + vn + " triangle count", $"{v.Mesh.Triangles.Count} vs {count} triangles");
                            if (v.Mesh.SteinerLimitReached != r.Mesh.SteinerLimitReached) Program.Report(S, tag + " " + vn + " Steiner limit", $"reached {v.Mesh.SteinerLimitReached} vs {r.Mesh.SteinerLimitReached}");
                        }
                    }
                }
            }
        }
    }
}
