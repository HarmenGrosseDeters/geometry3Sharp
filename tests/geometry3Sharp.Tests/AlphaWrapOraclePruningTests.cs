using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// AlphaWrapOracle's pruning (Alpha Wrap 3.0 lever 1): the tetrahedron query skips BVH nodes that a face plane of the
    /// tetrahedron separates from it, and the segment query tests each triangle's vertex balls once. Both must give
    /// exactly the result of the unpruned query (the wrap's output stays bit-identical), so these tests compare with
    /// the plain traversal (FacePruning off) and with a brute force over all primitives, on inputs where the face planes
    /// and the offset are near the input: flat patches with tetrahedra just above them, a soup, far from the origin.
    /// </summary>
    public class AlphaWrapOraclePruningTests
    {
        // a flat patch (many triangles in z = 0, mixed orientations), a sphere soup and a few points and segments
        static List<Vector3d> Input(out List<Vector3d> points)
        {
            var rnd = new Random(11);
            var tris = new List<Vector3d>();
            for (int i = 0; i < 16; ++i)
                for (int j = 0; j < 16; ++j) {
                    Vector3d p = new Vector3d(0.1 * i, 0.1 * j, 0), q = p + 0.1 * Vector3d.AxisX, w = p + 0.1 * Vector3d.AxisY;
                    if (rnd.Next(2) == 0) { tris.Add(p); tris.Add(q); tris.Add(w); } else { tris.Add(p); tris.Add(w); tris.Add(q); }
                    Vector3d s = p + new Vector3d(0.1, 0.1, 0);
                    tris.Add(q); tris.Add(s); tris.Add(w);
                }
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 0.4, EdgeVertices = 8 }.Generate().MakeDMesh();
            foreach (int tid in sphere.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                sphere.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d o = new Vector3d(0.8, 0.8, 0.6);
                tris.Add(a + o); tris.Add(b + o); tris.Add(c + o);
            }
            // a degenerate triangle (a segment)
            tris.Add(new Vector3d(1.7, 0.2, 0.3)); tris.Add(new Vector3d(1.8, 0.2, 0.3)); tris.Add(new Vector3d(1.9, 0.2, 0.3));
            points = new List<Vector3d> { new Vector3d(1.7, 1.5, 0.2), new Vector3d(0.3, 1.7, 0.05) };
            return tris;
        }

        // tetrahedra near the input: a face parallel to the patch at heights around the margin, and random ones
        static List<Vector3d[]> Tetrahedra(double margin, int count, int seed)
        {
            var rnd = new Random(seed);
            var list = new List<Vector3d[]>();
            double[] heights = { 0.5 * margin, margin * (1 - 1e-9), margin, margin * (1 + 1e-9), 1.5 * margin, 3 * margin, 0.02 };
            for (int k = 0; k < count; ++k) {
                Vector3d[] v;
                if (k % 2 == 0) {
                    // above the patch: one face nearly parallel to it, at height h (tilted a little, or not at all)
                    double h = heights[rnd.Next(heights.Length)];
                    Vector3d c = new Vector3d(0.05 + 1.5 * rnd.NextDouble(), 0.05 + 1.5 * rnd.NextDouble(), h);
                    double s = 0.05 + 0.2 * rnd.NextDouble(), tilt = rnd.Next(3) == 0 ? 0 : 1e-3 * (rnd.NextDouble() - 0.5);
                    v = new[] { c, c + new Vector3d(s, 0, tilt), c + new Vector3d(0, s, tilt * rnd.NextDouble()), c + new Vector3d(0.3 * s, 0.3 * s, s) };
                    if (rnd.Next(2) == 0) v[3] = c + new Vector3d(0.3 * s, 0.3 * s, -s - h);       // reaching through the patch
                } else {
                    Vector3d c = new Vector3d(2 * rnd.NextDouble() - 0.1, 2 * rnd.NextDouble() - 0.1, 1.2 * rnd.NextDouble() - 0.1);
                    double s = 0.02 + 0.3 * rnd.NextDouble();
                    v = Enumerable.Range(0, 4).Select(_ => c + s * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5)).ToArray();
                }
                if (ExactPredicates.Orient3d(v[0], v[1], v[2], v[3]) == 0) continue;
                if (rnd.Next(2) == 0) { var t = v[0]; v[0] = v[1]; v[1] = t; }       // both orientations
                list.Add(v);
            }
            return list;
        }

        [Fact]
        public void TetrahedronTouches_FacePruning_MatchesUnpruned()
        {
            var tris = Input(out var points);
            var o = new AlphaWrapOracle(tris, points);
            int touching = 0, apart = 0;
            foreach (double margin in new[] { 1e-6, 1e-3, 0.03 }) {
                foreach (var v in Tetrahedra(margin, 1500, 3)) {
                    o.FacePruning = true;
                    bool pruned = o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin);
                    o.FacePruning = false;
                    bool plain = o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin);
                    Assert.Equal(plain, pruned);
                    if (pruned) touching++; else apart++;
                }
            }
            Assert.True(touching > 300 && apart > 300, $"{touching} touching, {apart} apart");
        }

        // where the face test has no slack: a triangle vertex at the corner of its node box that is lowest along the face
        // normal (all components of one sign), so the box's projection is computed from the vertex's own coordinates, and the
        // tetrahedron's face just within the margin of it (or just beyond), far from the origin (rounding near 1e-9). A face
        // test that adds even 1e-8 to the box's projection skips the touching triangle
        [Fact]
        public void TetrahedronTouches_FacePruning_ExactAtTheSupportCorner()
        {
            Vector3d o0 = new Vector3d(632117.25, 5712043.5, 113.75);
            var tris = new List<Vector3d> { o0, o0 + new Vector3d(1, 0.1, 0.2), o0 + new Vector3d(0.1, 1, 0.3) };
            // (more triangles elsewhere: a hierarchy, not a single leaf)
            for (int k = 0; k < 12; ++k) {
                Vector3d p = o0 + new Vector3d(3 + 0.5 * k, 2, 1);
                tris.Add(p); tris.Add(p + new Vector3d(0.4, 0, 0)); tris.Add(p + new Vector3d(0, 0.4, 0.1));
            }
            var o = new AlphaWrapOracle(tris, new List<Vector3d>());
            var rnd = new Random(21);
            double margin = 1e-3;
            Vector3d u = new Vector3d(1, 1, 1).Normalized, e1 = new Vector3d(1, -1, 0).Normalized, e2 = u.Cross(e1);
            int touching = 0, apart = 0;
            for (int k = 0; k < 2000; ++k) {
                // the face at distance h from the corner vertex, h within 1e-9 .. 1e-7 of the margin
                double dh = margin * Math.Pow(10, -6 + 2 * rnd.NextDouble());
                double h = k % 2 == 0 ? margin - dh : margin + dh, s = 0.5 * (1 + rnd.NextDouble());
                double turn = 2 * Math.PI * rnd.NextDouble();
                Vector3d f1 = Math.Cos(turn) * e1 + Math.Sin(turn) * e2, f2 = u.Cross(f1);
                Vector3d c = o0 - h * u;
                Vector3d[] v = { c + s * f1, c - 0.5 * s * f1 + 0.87 * s * f2, c - 0.5 * s * f1 - 0.87 * s * f2, c - s * u };
                if (rnd.Next(2) == 0) { var t = v[0]; v[0] = v[1]; v[1] = t; }
                o.FacePruning = false;
                bool plain = o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin);
                o.FacePruning = true;
                Assert.Equal(plain, o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin));
                if (plain) touching++; else apart++;
            }
            Assert.True(touching > 500 && apart > 500, $"{touching} touching, {apart} apart");
        }

        // the face planes reject the nodes under the tetrahedron: fewer primitive tests than without them, for tetrahedra
        // just outside the sphere with a face towards it (the curved input, where the node slabs are thick)
        [Fact]
        public void TetrahedronTouches_FacePruning_SkipsPrimitives()
        {
            var tris = Input(out var points);
            var o = new AlphaWrapOracle(tris, points);
            double margin = 1e-6;
            var rnd = new Random(5);
            var tets = new List<Vector3d[]>();
            Vector3d center = new Vector3d(0.8, 0.8, 0.6);
            for (int k = 0; k < 400; ++k) {
                Vector3d u = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, 0.2 + rnd.NextDouble()).Normalized;
                Vector3d e1 = u.Cross(Vector3d.AxisX).Normalized, e2 = u.Cross(e1);
                double s = 0.15, h = 0.37 + 0.07 * rnd.NextDouble();
                Vector3d c = center + h * u;
                tets.Add(new[] { c + s * e1, c - 0.5 * s * e1 + 0.87 * s * e2, c - 0.5 * s * e1 - 0.87 * s * e2, c + s * u });
            }
            long[] tested = new long[2];
            int touching = 0;
            for (int p = 0; p < 2; ++p) {
                o.FacePruning = p == 1;
                touching = 0;
                foreach (var v in tets) {
                    touching += o.TetrahedronTouches(v[0], v[1], v[2], v[3], margin, out int n) ? 1 : 0;
                    tested[p] += n;
                }
            }
            Assert.InRange(touching, 1, tets.Count - 1);
            Assert.True(tested[1] < 0.9 * tested[0], $"primitive tests {tested[0]} without, {tested[1]} with the face planes");
        }

        [Fact]
        public void FirstEntry_MatchesBruteForce()
        {
            var tris = Input(out var points);
            var o = new AlphaWrapOracle(tris, points);
            var rnd = new Random(7);
            int hits = 0, misses = 0;
            for (int k = 0; k < 4000; ++k) {
                double r = k % 3 == 0 ? 1e-4 : 0.005 + 0.05 * rnd.NextDouble();
                Vector3d a = new Vector3d(2 * rnd.NextDouble() - 0.1, 2 * rnd.NextDouble() - 0.1, 1.2 * rnd.NextDouble() - 0.1), b;
                if (k % 4 == 0) {
                    // grazing the patch at height r, or starting within the offset
                    a.z = r * (0.5 + rnd.NextDouble());
                    b = a + new Vector3d(0.5 * (rnd.NextDouble() - 0.5), 0.5 * (rnd.NextDouble() - 0.5), r * (rnd.NextDouble() - 0.7));
                } else {
                    b = a + 0.6 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                }
                double t = o.FirstEntry(a, b, r);
                Assert.Equal(o.FirstEntryBruteForce(a, b, r), t);
                if (t >= 0) hits++; else misses++;
            }
            Assert.True(hits > 500 && misses > 500, $"{hits} hits, {misses} misses");
        }

        // per primitive (no minimum over neighbours that share a vertex): the entry with each vertex ball once equals the one
        // by the three capsules, for segments aimed at a corner from outside the triangle's plane and beyond its edges (where
        // the corner's ball is the first part entered), at its edges and through its face, with tmax below 1 and 0 too
        [Fact]
        public void PrimitiveEntry_BallsOnce_MatchesCapsules()
        {
            var rnd = new Random(31);
            var tris = new List<Vector3d>();
            for (int k = 0; k < 40; ++k) {
                Vector3d o = new Vector3d(3 * k, 0, 0);
                Func<Vector3d> j = () => new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                tris.Add(o + j()); tris.Add(o + j() + Vector3d.AxisX); tris.Add(o + j() + Vector3d.AxisY);
            }
            var o3 = new AlphaWrapOracle(tris, new List<Vector3d>());
            Assert.Equal(40, o3.Triangles);
            int[] byCorner = new int[3];
            int hits = 0;
            for (int k = 0; k < 40; ++k) {
                Vector3d[] v = { tris[3 * k], tris[3 * k + 1], tris[3 * k + 2] };
                Vector3d c = (v[0] + v[1] + v[2]) / 3;
                for (int s = 0; s < 150; ++s) {
                    double r = 0.02 + 0.2 * rnd.NextDouble();
                    int corner = s % 3;
                    Vector3d target = s % 5 == 4 ? c + 0.3 * (v[corner] - c) : v[corner] + 0.3 * r * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                    // from beyond the corner, away from the triangle (outside both edge cylinders' spans)
                    Vector3d away = (v[corner] - c).Normalized + new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, 2 * rnd.NextDouble() - 1);
                    Vector3d a = target + (0.5 + rnd.NextDouble()) * away, b = target - (0.2 + rnd.NextDouble()) * away;
                    foreach (double tmax in new[] { 1.0, 0.6, 0.0 }) {
                        double once = o3.PrimitiveEntry(k, a, b, r, tmax, true), caps = o3.PrimitiveEntry(k, a, b, r, tmax, false);
                        Assert.True(caps == once, $"triangle {k}, corner {corner}, tmax {tmax}: {once} vs {caps}");
                        if (tmax == 1 && caps > 0) {
                            hits++;
                            // the corner's ball is the part entered first: the entry point is within r of that vertex
                            Vector3d p = a + caps * (b - a);
                            if (Math.Abs(p.Distance(v[corner]) - r) < 1e-9 * r) byCorner[corner]++;
                        }
                    }
                }
            }
            Assert.True(hits > 3000, $"{hits} entries");
            Assert.True(byCorner.All(n => n > 200), $"entries through the corner balls: {string.Join(", ", byCorner)}");
        }

        // r = 0 and a point: the expanded box is the point itself, so the segment's clip interval is a single parameter
        // (t0 == t1, a closed interval); the entry is exact (0.5)
        [Fact]
        public void FirstEntry_ZeroRadius_GrazesTheBoxExactly()
        {
            var o = new AlphaWrapOracle(new List<Vector3d>(), new List<Vector3d> { new Vector3d(1, 0, 0) });
            Assert.Equal(0.5, o.FirstEntry(Vector3d.Zero, new Vector3d(2, 0, 0), 0));
            Assert.Equal(0.25, o.FirstEntry(new Vector3d(-1, -2, 0), new Vector3d(7, 6, 0), 0));
            Assert.Equal(-1, o.FirstEntry(new Vector3d(0, 1e-3, 0), new Vector3d(2, 1e-3, 0), 0));
        }

        // invariance over Similarity.All(): pruned equals unpruned (and the brute force) on every transformed input; on the
        // 2^k scales every answer, and every entry parameter bit for bit, equals the untransformed one
        [Fact]
        public void Pruning_Invariance()
        {
            var tris = Input(out var points);
            double margin = 1e-3;
            var tets = Tetrahedra(margin, 300, 9);
            var rnd = new Random(13);
            var segs = Enumerable.Range(0, 300).Select(_ => {
                Vector3d a = new Vector3d(2 * rnd.NextDouble(), 2 * rnd.NextDouble(), 0.01 + 0.5 * rnd.NextDouble());
                return new[] { a, a + 0.6 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.9) };
            }).ToList();
            bool[] touch0 = null; double[] entry0 = null;
            foreach (Similarity s in Similarity.All()) {
                var o = new AlphaWrapOracle(tris.Select(s.Apply).ToList(), points.Select(s.Apply).ToList());
                double m = s.ApplyLength(margin), r = s.ApplyLength(0.01);
                var touch = new bool[tets.Count];
                for (int k = 0; k < tets.Count; ++k) {
                    Vector3d[] v = tets[k].Select(s.Apply).ToArray();
                    o.FacePruning = false;
                    bool plain = o.TetrahedronTouches(v[0], v[1], v[2], v[3], m);
                    o.FacePruning = true;
                    touch[k] = o.TetrahedronTouches(v[0], v[1], v[2], v[3], m);
                    Assert.True(plain == touch[k], $"{s.Name}: tetrahedron {k}");
                }
                var entry = segs.Select(g => o.FirstEntry(s.Apply(g[0]), s.Apply(g[1]), r)).ToArray();
                for (int k = 0; k < segs.Count; ++k)
                    Assert.True(o.FirstEntryBruteForce(s.Apply(segs[k][0]), s.Apply(segs[k][1]), r) == entry[k], $"{s.Name}: segment {k}");
                if (touch0 == null) {
                    touch0 = touch; entry0 = entry;
                    Assert.Contains(true, touch); Assert.Contains(false, touch);
                    Assert.Contains(entry, e => e > 0); Assert.Contains(entry, e => e < 0);
                } else if (s.IsExact) {
                    Assert.Equal(touch0, touch);
                    Assert.Equal(entry0, entry);
                }
            }
        }
    }
}
