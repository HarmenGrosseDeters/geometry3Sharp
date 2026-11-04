// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The certificate of the catch points' move (docs/design/TERRAIN.md 5.5): its bound D holds what it bounds, sampled.
    /// On every composite triangle it bounded, |z - c| is at most D, where c = max(S_fill, min(T, S_cut)) is the clamp
    /// of the realized surfaces (the slope triangles of a run on their own plane). Samples within twice the move of a
    /// boundary edge of a slope surface are left out (the steps, 5.5).
    /// </summary>
    public class TerrainSlopeSnapCertificateTests
    {
        static Similarity frame(string name)
        {
            Similarity s = Similarity.ByName(name);
            return s.IsRotated ? new Similarity(s.Name + " (about z)", s.Scale, Matrix3d.AxisAngleD(Vector3d.AxisZ, 31), s.Translation) : s;
        }

        // the largest sampled |z - c| on the certified triangles, over D (0 if nothing moved)
        internal static double SampledOverBound(TerrainSlopeDesign d, out int samples)
        {
            samples = 0;
            var surfaces = new List<KeyValuePair<DMesh3, bool>>();
            int nc = d.Pieces.Count;
            if (d.Direction == TerrainSlopeDesign.Directions.Auto) {
                surfaces.Add(new KeyValuePair<DMesh3, bool>(d.AutoCut.SlopeSurface, true));
                surfaces.Add(new KeyValuePair<DMesh3, bool>(d.AutoFill.SlopeSurface, false));
                nc = d.AutoCut.Pieces.Count;
            } else surfaces.Add(new KeyValuePair<DMesh3, bool>(d.SlopeSurface, d.Direction == TerrainSlopeDesign.Directions.Cut));
            var boundary = new List<Segment2d>();
            foreach (var kv in surfaces)
                foreach (int e in kv.Key.BoundaryEdgeIndices()) {
                    Index2i ev = kv.Key.GetEdgeV(e);
                    boundary.Add(new Segment2d(kv.Key.GetVertex(ev.a).xy, kv.Key.GetVertex(ev.b).xy));
                }
            DMesh3 m = d.Composite;
            int terrainGroup = d.Pieces.Count;
            double worst = 0;
            foreach (int t in d.SnapTriangles) {
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                int g = m.GetTriangleGroup(t);
                // (the run the triangle is a slope triangle of, or -1)
                int own = g == terrainGroup ? -1 : (d.Direction == TerrainSlopeDesign.Directions.Auto && g >= nc ? 1 : 0);
                double scale = Math.Max(Math.Max(Math.Abs(a.x), Math.Abs(a.y)), 1);
                double slack = 1e-10 * (1 + Math.Abs(a.z)) + 1e-14 * scale;
                const int n = 24;
                for (int i = 0; i <= n; ++i)
                    for (int j = 0; i + j <= n; ++j) {
                        double u = (double)i / n, v = (double)j / n;
                        Vector3d p = a * (1 - u - v) + b * u + c * v;
                        Vector2d x = p.xy;
                        if (boundary.Any(sg => sg.DistanceSquared(x) <= (2 * d.SnapStep + 1e-14 * scale) * (2 * d.SnapStep + 1e-14 * scale))) continue;
                        double T = TerrainSlopeDesignTests.HeightAt(d.Terrain, x);
                        if (double.IsNaN(T)) continue;
                        double sCut = double.PositiveInfinity, sFill = double.NegativeInfinity;
                        for (int r = 0; r < surfaces.Count; ++r) {
                            double S = r == own ? p.z : TerrainSlopeDesignTests.HeightAt(surfaces[r].Key, x);
                            if (double.IsNaN(S)) continue;
                            if (surfaces[r].Value) sCut = S; else sFill = S;
                        }
                        double clamp = Math.Max(sFill, Math.Min(T, sCut));
                        samples++;
                        double dev = Math.Abs(p.z - clamp) - slack;
                        if (dev > 0) worst = Math.Max(worst, d.SnapCertificate > 0 ? dev / d.SnapCertificate : double.PositiveInfinity);
                    }
            }
            return worst;
        }

        // the clamp's |z - c| at x on the composite triangle t (corners a, b, c), the slope triangles of run own on their
        // own plane; NaN where it is not taken (off the terrain, or within 2 m of a boundary edge: the steps)
        static double deviation_at(TerrainSlopeDesign d, List<KeyValuePair<DMesh3, bool>> surfaces, List<Segment2d> boundary,
                                   Vector3d a, Vector3d b, Vector3d c, int own, Vector2d x, double scale)
        {
            if (boundary.Any(sg => sg.DistanceSquared(x) <= (2 * d.SnapStep + 1e-14 * scale) * (2 * d.SnapStep + 1e-14 * scale))) return double.NaN;
            // (z on t's plane, extended)
            Vector3d n = (b - a).Cross(c - a);
            double z = a.z - (n.x * (x.x - a.x) + n.y * (x.y - a.y)) / n.z;
            double T = TerrainSlopeDesignTests.HeightAt(d.Terrain, x);
            if (double.IsNaN(T)) return double.NaN;
            double sCut = double.PositiveInfinity, sFill = double.NegativeInfinity;
            for (int r = 0; r < surfaces.Count; ++r) {
                double S = r == own ? z : TerrainSlopeDesignTests.HeightAt(surfaces[r].Key, x);
                if (double.IsNaN(S)) continue;
                if (surfaces[r].Value) sCut = S; else sFill = S;
            }
            return Math.Abs(z - Math.Max(sFill, Math.Min(T, sCut)));
        }

        static List<KeyValuePair<DMesh3, bool>> surfaces_of(TerrainSlopeDesign d, out int nc)
        {
            var surfaces = new List<KeyValuePair<DMesh3, bool>>();
            nc = d.Pieces.Count;
            if (d.Direction == TerrainSlopeDesign.Directions.Auto) {
                surfaces.Add(new KeyValuePair<DMesh3, bool>(d.AutoCut.SlopeSurface, true));
                surfaces.Add(new KeyValuePair<DMesh3, bool>(d.AutoFill.SlopeSurface, false));
                nc = d.AutoCut.Pieces.Count;
            } else surfaces.Add(new KeyValuePair<DMesh3, bool>(d.SlopeSurface, d.Direction == TerrainSlopeDesign.Directions.Cut));
            return surfaces;
        }

        /// <summary>
        /// per certified triangle, |z - c| at the candidates of the bound (TERRAIN.md 5.5), recomputed here in floating
        /// point: the corners, the terrain's and the slope surfaces' vertices inside it, the crossings of edges of two of
        /// them and of its own edges; the largest excess over the triangle's own bound, as a fraction of it, and the
        /// count of candidates by class
        /// </summary>
        internal static double CandidatesOverBound(TerrainSlopeDesign d, int[] perClass)
        {
            int nc;
            var surfaces = surfaces_of(d, out nc);
            var boundary = new List<Segment2d>();
            foreach (var kv in surfaces)
                foreach (int e in kv.Key.BoundaryEdgeIndices()) {
                    Index2i ev = kv.Key.GetEdgeV(e);
                    boundary.Add(new Segment2d(kv.Key.GetVertex(ev.a).xy, kv.Key.GetVertex(ev.b).xy));
                }
            var meshes = new List<DMesh3> { d.Terrain };
            meshes.AddRange(surfaces.Select(kv => kv.Key));
            DMesh3 m = d.Composite;
            double worst = 0;
            for (int k = 0; k < d.SnapTriangles.Count; ++k) {
                int t = d.SnapTriangles[k];
                double bound = d.SnapTriangleBounds[k];
                Index3i tv = m.GetTriangle(t);
                Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
                int g = m.GetTriangleGroup(t);
                int own = g == d.Pieces.Count ? -1 : (d.Direction == TerrainSlopeDesign.Directions.Auto && g >= nc ? 1 : 0);
                double scale = Math.Max(Math.Max(Math.Abs(a.x), Math.Abs(a.y)), 1);
                double slack = 1e-10 * (1 + Math.Abs(a.z)) + 1e-14 * scale;
                Vector2d[] tri = { a.xy, b.xy, c.xy };
                int ot = ExactPredicates.Orient2d(tri[0], tri[1], tri[2]);
                var box = new AxisAlignedBox2d(tri[0], tri[1]); box.Contain(tri[2]);
                Func<Vector2d, bool> inside = (x) => ExactPredicates.Orient2d(tri[0], tri[1], x) * ot >= 0
                    && ExactPredicates.Orient2d(tri[1], tri[2], x) * ot >= 0 && ExactPredicates.Orient2d(tri[2], tri[0], x) * ot >= 0;
                var pts = new List<KeyValuePair<int, Vector2d>>();
                foreach (Vector2d x in tri) pts.Add(new KeyValuePair<int, Vector2d>(0, x));
                var edges = new List<List<Segment2d>>();
                for (int mi = 0; mi < meshes.Count; ++mi) {
                    DMesh3 s = meshes[mi];
                    var es = new List<Segment2d>();
                    foreach (int e in s.EdgeIndices()) {
                        Index2i ev = s.GetEdgeV(e);
                        Vector2d p = s.GetVertex(ev.a).xy, q = s.GetVertex(ev.b).xy;
                        var eb = new AxisAlignedBox2d(p, q);
                        if (eb.Intersects(box) == false) continue;
                        es.Add(new Segment2d(p, q));
                    }
                    foreach (int v in s.VertexIndices()) {
                        Vector2d p = s.GetVertex(v).xy;
                        if (box.Contains(p) && inside(p)) pts.Add(new KeyValuePair<int, Vector2d>(mi == 0 ? 1 : 2, p));
                    }
                    edges.Add(es);
                }
                edges.Add(new List<Segment2d> { new Segment2d(tri[0], tri[1]), new Segment2d(tri[1], tri[2]), new Segment2d(tri[2], tri[0]) });
                for (int i = 0; i < edges.Count; ++i)
                    for (int j = i + 1; j < edges.Count; ++j)
                        foreach (Segment2d e1 in edges[i])
                            foreach (Segment2d e2 in edges[j]) {
                                Vector2d p0 = e1.P0, p1 = e1.P1, q0 = e2.P0, q1 = e2.P1;
                                int o1 = ExactPredicates.Orient2d(p0, p1, q0), o2 = ExactPredicates.Orient2d(p0, p1, q1);
                                int o3 = ExactPredicates.Orient2d(q0, q1, p0), o4 = ExactPredicates.Orient2d(q0, q1, p1);
                                if (o1 * o2 > 0 || o3 * o4 > 0 || (o1 == 0 && o2 == 0)) continue;
                                // (relative to p0, nearly parallel pairs left out: their crossing is not located well in doubles)
                                Vector2d u = p1 - p0, w = q1 - q0, r = q0 - p0;
                                double den = u.x * w.y - u.y * w.x;
                                if (Math.Abs(den) <= 1e-6 * u.Length * w.Length) continue;
                                double tt = (r.x * w.y - r.y * w.x) / den;
                                Vector2d x = p0 + tt * u;
                                if (inside(x)) pts.Add(new KeyValuePair<int, Vector2d>(j == edges.Count - 1 ? 4 : 3, x));
                            }
                foreach (var kv in pts) {
                    double f = deviation_at(d, surfaces, boundary, a, b, c, own, kv.Value, scale);
                    if (double.IsNaN(f)) continue;
                    if (perClass != null) perClass[kv.Key]++;
                    double excess = f - slack - bound;
                    if (excess > 0) worst = Math.Max(worst, bound > 0 ? excess / bound : double.PositiveInfinity);
                }
            }
            return worst;
        }

        // every composite triangle whose interior overlaps a triangle (u, p0, p1) swept by a moved point's edge (p0 its
        // own position, p1 the composite's, u a neighbour) is among the certified ones (found here by brute force)
        internal static string UncertifiedInSweep(TerrainSlopeDesign d)
        {
            DMesh3 m = d.Composite;
            var certified = new HashSet<int>(d.SnapTriangles);
            Func<Vector2d[], Vector2d[], bool> overlap = (s, t) => {
                foreach (var pair in new[] { new[] { s, t }, new[] { t, s } }) {
                    Vector2d[] p = pair[0], q = pair[1];
                    int o = ExactPredicates.Orient2d(p[0], p[1], p[2]);
                    for (int i = 0; i < 3; ++i) {
                        bool separates = true;
                        for (int j = 0; j < 3 && separates; ++j)
                            if (ExactPredicates.Orient2d(p[i], p[(i + 1) % 3], q[j]) * o > 0) separates = false;
                        if (separates) return false;
                    }
                }
                return true;
            };
            foreach (var mv in d.SnapMoves) {
                Vector2d p0 = mv.Key, p1 = m.GetVertex(mv.Value).xy;
                foreach (int u in m.VtxVerticesItr(mv.Value)) {
                    var sw = new[] { m.GetVertex(u).xy, p0, p1 };
                    if (ExactPredicates.Orient2d(sw[0], sw[1], sw[2]) == 0) continue;
                    foreach (int t in m.TriangleIndices()) {
                        if (certified.Contains(t)) continue;
                        Index3i tv = m.GetTriangle(t);
                        var txy = new[] { m.GetVertex(tv.a).xy, m.GetVertex(tv.b).xy, m.GetVertex(tv.c).xy };
                        if (ExactPredicates.Orient2d(txy[0], txy[1], txy[2]) == 0) continue;
                        if (overlap(txy, sw)) return $"triangle {t} overlaps the sweep of vertex {mv.Value} from {p0} to {p1}";
                    }
                }
            }
            return "";
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (string f in new[] { "identity", "utm", "rotated, scale 1e-3, utm" })
                foreach (var dir in new[] { TerrainSlopeDesign.Directions.Cut, TerrainSlopeDesign.Directions.Fill, TerrainSlopeDesign.Directions.Auto })
                    yield return new object[] { f, dir };
        }

        // Mixed's designs (TerrainSlopeMixedTests.Make: terrain tilt, offset, tilt direction) in three frames, cut, fill
        // and mixed: on each Success the certificate's bound holds, sampled, on the triangles it bounded
        [Theory]
        [MemberData(nameof(Cases))]
        public void BoundHoldsSampled(string name, TerrainSlopeDesign.Directions dir)
        {
            Similarity s = frame(name);
            int certified = 0, moved = 0;
            var perClass = new int[5];
            foreach (double g in new[] { 0.07, 0.15, 0.23 })
                foreach (double c in new[] { -1.1, 0.123, 0.3, 1.7 })
                    foreach (double rot in new[] { 0.0, 3.9 }) {
                        var d = TerrainSlopeMixedTests.Make(g, c, rot, dir, s);
                        if (d.Compute() == false) continue;
                        certified++;
                        Assert.True(ExactInterval.Up(d.SnapBudget + d.SnapCertificate) <= d.Tolerance);
                        if (d.SnapTriangles.Count == 0) continue;
                        moved++;
                        int samples;
                        double w = SampledOverBound(d, out samples);
                        Assert.True(w <= 1, $"g {g} c {c} rot {rot}: sampled |z - clamp| up to {w:G4} x the bound {d.SnapCertificate:G4}");
                        string un = UncertifiedInSweep(d);
                        Assert.True(un == "", $"g {g} c {c} rot {rot}: {un}");
                        double wc = CandidatesOverBound(d, perClass);
                        Assert.True(wc == 0, $"g {g} c {c} rot {rot}: |z - clamp| at a candidate over its triangle's bound by {wc:G4} x the bound");
                    }
            // (designs to check: Successes, with moved catch points; near the origin cut and fill alone move none)
            Assert.True(certified >= 12, $"{certified} certified");
            if (name != "identity" || dir == TerrainSlopeDesign.Directions.Auto) Assert.True(moved >= 1, $"{moved} with moves");
        }
    }
}
