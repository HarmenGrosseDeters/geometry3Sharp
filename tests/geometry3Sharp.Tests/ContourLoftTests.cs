using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// ContourLoft stage 1 (docs/design/CONTOUR_LOFT.md): the optimal band between two contours, against an independent
    /// brute force over all tilings, closed-form bands of coaxial regular polygons, every status, the terrain slope client,
    /// invariance and reuse.
    /// </summary>
    public class ContourLoftTests
    {
        // ---- inputs ----

        static List<Vector3d> Regular(int n, double r, double z, double phase = 0, double cx = 0, double cy = 0)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double a = phase + 2 * Math.PI * i / n;
                c.Add(new Vector3d(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z));
            }
            return c;
        }

        // a random star-shaped contour around (cx, cy), counter-clockwise
        static List<Vector3d> Star(Random rand, int n, double z, double cx = 0, double cy = 0, double r = 1)
        {
            var c = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * (i + 0.6 * rand.NextDouble()) / n;
                double rr = r * (0.7 + 0.6 * rand.NextDouble());
                c.Add(new Vector3d(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a), z));
            }
            return c;
        }

        // ---- the independent oracle: every tiling of every start, explicitly ----

        struct Tri { public bool OnFirst; public int I, J; }

        sealed class BruteResult
        {
            public int MinCount = int.MaxValue;
            public double Best = double.PositiveInfinity, Second = double.PositiveInfinity;
            public List<Tri> BestBand;
        }

        // contours run the same way; triangle (edge P_i P_i+1, apex Q_j) or (edge Q_j Q_j+1, apex P_i); all starts (P_0, Q_k)
        static BruteResult Brute(IList<Vector3d> P, IList<Vector3d> Q, Func<Tri, double> cost, Func<Tri, bool> bad)
        {
            int m = P.Count, n = Q.Count;
            var res = new BruteResult();
            var band = new List<Tri>();
            void Walk(int i, int j, int k, int cnt, double c)
            {
                if (i == m && j == n) {
                    if (Turns(band) < 3) return;   // a fan over a whole contour: not a manifold band
                    if (cnt < res.MinCount || cnt == res.MinCount && c < res.Best) {
                        if (cnt == res.MinCount) res.Second = res.Best; else res.Second = double.PositiveInfinity;
                        res.MinCount = cnt; res.Best = c; res.BestBand = new List<Tri>(band);
                    } else if (cnt == res.MinCount && c < res.Second) res.Second = c;
                    return;
                }
                if (i < m) {
                    var t = new Tri { OnFirst = true, I = i % m, J = (j + k) % n };
                    band.Add(t); Walk(i + 1, j, k, cnt + (bad(t) ? 1 : 0), c + cost(t)); band.RemoveAt(band.Count - 1);
                }
                if (j < n) {
                    var t = new Tri { OnFirst = false, I = i % m, J = (j + k) % n };
                    band.Add(t); Walk(i, j + 1, k, cnt + (bad(t) ? 1 : 0), c + cost(t)); band.RemoveAt(band.Count - 1);
                }
            }
            for (int k = 0; k < n; ++k) Walk(0, 0, k, 0, 0);
            return res;
        }

        static int Turns(List<Tri> band)
        {
            int t = 0;
            for (int k = 1; k < band.Count; ++k) if (band[k].OnFirst != band[k - 1].OnFirst) t++;
            return t;
        }

        [Fact]
        public void Oracle_DegenerateFans_AreNotManifold()
        {
            // the paths the oracle leaves out (all steps on one contour in one block) do not give a manifold mesh
            var P = Regular(4, 1, 0); var Q = Regular(3, 1, 1);
            var fan = new List<Tri>();
            for (int j = 0; j < 3; ++j) fan.Add(new Tri { OnFirst = false, I = 0, J = j });
            for (int i = 0; i < 4; ++i) fan.Add(new Tri { OnFirst = true, I = i, J = 0 });
            var mesh = new DMesh3();
            foreach (var p in P) mesh.AppendVertex(p);
            foreach (var p in Q) mesh.AppendVertex(p);
            bool all = true;
            foreach (var t in fan) {
                var tri = t.OnFirst ? new Index3i(t.I, (t.I + 1) % 4, 4 + t.J) : new Index3i(t.I, 4 + (t.J + 1) % 3, 4 + t.J);
                all &= mesh.AppendTriangle(tri) >= 0;
            }
            Assert.False(all);
        }

        static (Vector3d a, Vector3d b, Vector3d c) Points(IList<Vector3d> P, IList<Vector3d> Q, Tri t)
        {
            int m = P.Count, n = Q.Count;
            return t.OnFirst ? (P[t.I], P[(t.I + 1) % m], Q[t.J]) : (Q[t.J], Q[(t.J + 1) % n], P[t.I]);
        }

        static double TriArea(Vector3d a, Vector3d b, Vector3d c) => 0.5 * (b - a).Cross(c - a).Length;

        // the band's triangles in the oracle's terms, read from the mesh (vertex i < m: P_i, else Q_{i - m})
        static List<Tri> BandOf(DMesh3 mesh, int m, int n)
        {
            var list = new List<Tri>();
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                int[] v = { t.a, t.b, t.c };
                if (v.Any(x => x >= m + n)) continue;  // a cap triangle with a Steiner point
                int onP = v.Count(x => x < m);
                if (onP == 0 || onP == 3) continue;   // a cap triangle
                if (onP == 2) {
                    int[] p = v.Where(x => x < m).ToArray(); int q = v.First(x => x >= m) - m;
                    int i = (p[0] + 1) % m == p[1] ? p[0] : p[1];
                    list.Add(new Tri { OnFirst = true, I = i, J = q });
                } else {
                    int[] q = v.Where(x => x >= m).Select(x => x - m).ToArray(); int p = v.First(x => x < m);
                    int j = (q[0] + 1) % n == q[1] ? q[0] : q[1];
                    list.Add(new Tri { OnFirst = false, I = p, J = j });
                }
            }
            return list;
        }

        static string Key(IEnumerable<Tri> band) => string.Join(" ", band.Select(t => (t.OnFirst ? "P" : "Q") + t.I + "," + t.J).OrderBy(s => s, StringComparer.Ordinal));

        // signed volume of the band closed by fan caps (independent of any cap mesh), relative to c
        static double ClosedVolume(DMesh3 band, IList<Vector3d> P, IList<Vector3d> Q)
        {
            Vector3d c = Vector3d.Zero;
            foreach (var p in P) c += p;
            foreach (var p in Q) c += p;
            c /= P.Count + Q.Count;
            double v = 0;
            foreach (int tid in band.TriangleIndices()) {
                Index3i t = band.GetTriangle(tid);
                v += (band.GetVertex(t.a) - c).Dot((band.GetVertex(t.b) - c).Cross(band.GetVertex(t.c) - c));
            }
            // caps: each contour edge is used by the band once; the cap runs it the other way
            int m = P.Count;
            for (int k = 0; k < 2; ++k) {
                IList<Vector3d> C = k == 0 ? P : Q; int off = k == 0 ? 0 : m;
                int e = band.FindEdge(off, off + 1);
                Index3i bt = band.GetTriangle(band.GetEdgeT(e).a);
                bool forward = IndexUtil.find_tri_ordered_edge(off, off + 1, ref bt) != DMesh3.InvalidID;
                for (int i = 1; i + 1 < C.Count; ++i) {
                    Vector3d a = C[0] - c, b = C[i] - c, d = C[i + 1] - c;
                    double tv = a.Dot(b.Cross(d));
                    v += forward ? -tv : tv;
                }
            }
            return v / 6;
        }

        static void AssertValidBand(ContourLoftResult r, IList<Vector3d> P, IList<Vector3d> Q, bool caps = false)
        {
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            DMesh3 mesh = r.Mesh;
            Assert.NotNull(mesh);
            Assert.True(Invariance.AllFinite(mesh));
            for (int i = 0; i < P.Count; ++i) Assert.Equal(P[i], mesh.GetVertex(i));
            for (int j = 0; j < Q.Count; ++j) Assert.Equal(Q[j], mesh.GetVertex(P.Count + j));
            Assert.Equal(P.Count + Q.Count, BandOf(mesh, P.Count, Q.Count).Count);
            Assert.Equal(0, MeshRepairMetrics.InconsistentlyOrientedEdges(mesh));
            if (caps) {
                Assert.Equal("", MeshRobustRepair.NotCleanReason(mesh));
                Assert.True(Invariance.Volume(mesh) > 0);
            } else {
                var loops = new MeshBoundaryLoops(mesh);
                Assert.Equal(2, loops.Count);
                var sets = loops.Loops.Select(l => string.Join(",", l.Vertices.OrderBy(x => x))).OrderBy(s => s.Length).ToList();
                var expect = new[] { string.Join(",", Enumerable.Range(0, P.Count)), string.Join(",", Enumerable.Range(P.Count, Q.Count)) }
                    .OrderBy(s => s.Length).ToList();
                Assert.Equal(expect.OrderBy(s => s, StringComparer.Ordinal), sets.OrderBy(s => s, StringComparer.Ordinal));
                foreach (int v in mesh.VertexIndices()) Assert.False(mesh.IsBowtieVertex(v));
                Assert.False(MeshRepairMetrics.HasSelfIntersections(mesh));
                Assert.True(ClosedVolume(mesh, P, Q) > 0);   // faces out of the solid
            }
        }

        // ---- the brute-force oracle ----

        public static IEnumerable<object[]> BruteCases()
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
                foreach (var (m, n) in new[] { (3, 3), (4, 6), (6, 4), (5, 5), (3, 7) })
                    yield return new object[] { seed, m, n };
        }

        [Theory]
        [MemberData(nameof(BruteCases))]
        public void MinimumArea_EqualsBruteForce(int seed, int m, int n)
        {
            var rand = new Random(seed * 100 + m * 10 + n);
            var P = Star(rand, m, 0); var Q = Star(rand, n, 0.8, 0.2, -0.1, 0.8);
            var brute = Brute(P, Q, t => { var (a, b, c) = Points(P, Q, t); return TriArea(a, b, c); }, t => false);
            var r = new ContourLoft { CheckSelfIntersections = false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            var band = BandOf(r.Mesh, m, n);
            double area = band.Sum(t => { var (a, b, c) = Points(P, Q, t); return TriArea(a, b, c); });
            Assert.True(Math.Abs(area - brute.Best) <= 1e-12 * brute.Best, $"{area} vs {brute.Best}");
            Assert.True(Math.Abs(r.TotalCost - brute.Best) <= 1e-12 * brute.Best);
            if (brute.Second - brute.Best > 1e-9 * brute.Best)
                Assert.Equal(Key(brute.BestBand), Key(band));
        }

        [Theory]
        [MemberData(nameof(BruteCases))]
        public void MaximumVolume_EqualsBruteForce(int seed, int m, int n)
        {
            var rand = new Random(seed * 100 + m * 10 + n + 7);
            var P = Star(rand, m, 0); var Q = Star(rand, n, 1.1, 0.1, 0.1, 0.9);
            // the oracle's cost: minus the volume of the solid, from scratch for every band (no per-triangle decomposition)
            var r = new ContourLoft { Cost = ContourLoftCost.MaximumVolume, CheckSelfIntersections = false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            double best = double.NegativeInfinity, second = double.NegativeInfinity; string bestKey = null;
            foreach (var bandList in AllBands(m, n)) {
                double v = Math.Abs(ClosedVolume(MeshOf(P, Q, bandList), P, Q));
                if (v > best) { second = best; best = v; bestKey = Key(bandList); } else if (v > second) second = v;
            }
            double got = ClosedVolume(r.Mesh, P, Q);
            Assert.True(got > 0);
            Assert.True(Math.Abs(got - best) <= 1e-12 * best, $"{got} vs {best}");
            if (best - second > 1e-9 * best)
                Assert.Equal(bestKey, Key(BandOf(r.Mesh, m, n)));
        }

        static IEnumerable<List<Tri>> AllBands(int m, int n)
        {
            var band = new List<Tri>();
            var all = new List<List<Tri>>();
            void Walk(int i, int j, int k)
            {
                if (i == m && j == n) { if (Turns(band) >= 3) all.Add(new List<Tri>(band)); return; }
                if (i < m) { band.Add(new Tri { OnFirst = true, I = i, J = (j + k) % n }); Walk(i + 1, j, k); band.RemoveAt(band.Count - 1); }
                if (j < n) { band.Add(new Tri { OnFirst = false, I = i % m, J = (j + k) % n }); Walk(i, j + 1, k); band.RemoveAt(band.Count - 1); }
            }
            for (int k = 0; k < n; ++k) Walk(0, 0, k);
            return all;
        }

        // a band as a mesh, consistently oriented (P forwards, Q backwards)
        static DMesh3 MeshOf(IList<Vector3d> P, IList<Vector3d> Q, List<Tri> band)
        {
            int m = P.Count, n = Q.Count;
            var mesh = new DMesh3();
            foreach (var p in P) mesh.AppendVertex(p);
            foreach (var p in Q) mesh.AppendVertex(p);
            foreach (var t in band) {
                var tri = t.OnFirst ? new Index3i(t.I, (t.I + 1) % m, m + t.J) : new Index3i(t.I, m + (t.J + 1) % n, m + t.J);
                Assert.True(mesh.AppendTriangle(tri) >= 0);
            }
            return mesh;
        }

        [Theory]
        [MemberData(nameof(BruteCases))]
        public void CustomCostAndAdmissibility_EqualBruteForce(int seed, int m, int n)
        {
            var rand = new Random(seed * 1000 + m * 10 + n);
            var P = Star(rand, m, 0); var Q = Star(rand, n, 1);
            // a cost that may be negative, from the indices and the points
            double Cost(bool onFirst, int i, int j, Vector3d a, Vector3d b, Vector3d c) =>
                (a - c).Length * (b - c).Length - 1.3 + 0.01 * ((i * 7 + j * 3 + (onFirst ? 1 : 0)) % 5);
            // a random mask of forbidden triangles, by their input indices
            var banned = new HashSet<string>();
            for (int i = 0; i < m; ++i) for (int j = 0; j < n; ++j) {
                if (rand.NextDouble() < 0.35) banned.Add("P" + i + "," + j);
                if (rand.NextDouble() < 0.35) banned.Add("Q" + i + "," + j);
            }
            var brute = Brute(P, Q, t => { var (a, b, c) = Points(P, Q, t); return Cost(t.OnFirst, t.I, t.J, a, b, c); },
                t => banned.Contains((t.OnFirst ? "P" : "Q") + t.I + "," + t.J));
            var loft = new ContourLoft {
                Cost = ContourLoftCost.Custom, CheckSelfIntersections = false,
                CustomCost = t => Cost(t.EdgeOnFirst, t.I, t.J, t.A, t.B, t.C),
                IsAdmissible = t => !banned.Contains((t.EdgeOnFirst ? "P" : "Q") + t.I + "," + t.J),
            };
            var r = loft.Compute(P, Q);
            if (brute.MinCount > 0) {
                Assert.Equal(ContourLoftStatus.NoAdmissibleBand, r.Status);
                Assert.Null(r.Mesh);
                Assert.Equal(brute.MinCount, r.InadmissibleTriangles);
            } else {
                Assert.Equal(ContourLoftStatus.Success, r.Status);
                Assert.Equal(0, r.InadmissibleTriangles);
                var band = BandOf(r.Mesh, m, n);
                Assert.All(band, t => Assert.DoesNotContain((t.OnFirst ? "P" : "Q") + t.I + "," + t.J, banned));
                Assert.True(Math.Abs(r.TotalCost - brute.Best) <= 1e-12 * (m + n) * 10, $"{r.TotalCost} vs {brute.Best}");
                if (brute.Second - brute.Best > 1e-9)
                    Assert.Equal(Key(brute.BestBand), Key(band));
            }
            // the cost sees the triangle as the oracle defines it
            if (r.Status == ContourLoftStatus.Success) {
                var band = BandOf(r.Mesh, m, n);
                double sum = band.Sum(t => { var (a, b, c) = Points(P, Q, t); return Cost(t.OnFirst, t.I, t.J, a, b, c); });
                Assert.True(Math.Abs(sum - r.TotalCost) <= 1e-12 * (m + n) * 10);
            }
        }

        [Fact]
        public void NoAdmissibleBand_ByCount_IsExactOverManyMasks()
        {
            // masks at the edge of feasibility: many cases with a minimum of exactly 1, and some with 0
            int zero = 0, positive = 0;
            for (int seed = 0; seed < 60; ++seed) {
                var rand = new Random(seed);
                int m = 3 + seed % 3, n = 3 + seed % 4;
                var P = Star(rand, m, 0); var Q = Star(rand, n, 1);
                var banned = new HashSet<string>();
                for (int i = 0; i < m; ++i) for (int j = 0; j < n; ++j) {
                    if (rand.NextDouble() < 0.3) banned.Add("P" + i + "," + j);
                    if (rand.NextDouble() < 0.3) banned.Add("Q" + i + "," + j);
                }
                var brute = Brute(P, Q, t => 0, t => banned.Contains((t.OnFirst ? "P" : "Q") + t.I + "," + t.J));
                var r = new ContourLoft { CheckSelfIntersections = false,
                    IsAdmissible = t => !banned.Contains((t.EdgeOnFirst ? "P" : "Q") + t.I + "," + t.J) }.Compute(P, Q);
                Assert.Equal(brute.MinCount, r.InadmissibleTriangles);
                Assert.Equal(brute.MinCount == 0 ? ContourLoftStatus.Success : ContourLoftStatus.NoAdmissibleBand, r.Status);
                if (brute.MinCount == 0) zero++; else positive++;
            }
            Assert.True(zero >= 5 && positive >= 5, $"{zero} feasible, {positive} infeasible");
        }

        [Fact]
        public void LargerInputs_EqualTheAllStartsDynamicProgram()
        {
            // the divide and conquer over the starts against a plain DP per start (all starts, no regions)
            for (int seed = 0; seed < 8; ++seed) {
                var rand = new Random(seed);
                int m = 20 + seed * 3, n = 9 + seed * 2;
                var P = Star(rand, m, 0); var Q = Star(rand, n, 0.7, 0.15, 0.05, 0.7);
                double Cost(Tri t) { var (a, b, c) = Points(P, Q, t); return TriArea(a, b, c); }
                bool Bad(Tri t) { var (a, b, c) = Points(P, Q, t); return Math.Abs(b.x - a.x) * 3 < Math.Abs(c.z - a.z) * 0.2 && t.I % 2 == 0; }
                int bestCount = int.MaxValue; double best = double.PositiveInfinity;
                for (int k = 0; k < n; ++k) {
                    // state: first direction f (0: a P step), turns t capped at 3; forward relaxation over the full grid
                    var cnt = new int[m + 1, n + 1, 2, 4]; var d = new double[m + 1, n + 1, 2, 4];
                    foreach (int i in Enumerable.Range(0, m + 1)) foreach (int j in Enumerable.Range(0, n + 1))
                        for (int f = 0; f < 2; ++f) for (int t = 0; t < 4; ++t) cnt[i, j, f, t] = int.MaxValue;
                    void Step(int i, int j, int f, int t, int dir, int c0, double d0)
                    {
                        Tri tri = dir == 0 ? new Tri { OnFirst = true, I = i, J = (j + k) % n } : new Tri { OnFirst = false, I = i % m, J = (j + k) % n };
                        int ni = dir == 0 ? i + 1 : i, nj = dir == 0 ? j : j + 1;
                        int nf = f < 0 ? dir : f, nt = f < 0 ? 0 : ((t % 2 == 0 ? f : 1 - f) == dir ? t : Math.Min(t + 1, 3));
                        Relax(ref cnt[ni, nj, nf, nt], ref d[ni, nj, nf, nt], c0 + (Bad(tri) ? 1 : 0), d0 + Cost(tri));
                    }
                    Step(0, 0, -1, 0, 0, 0, 0); Step(0, 0, -1, 0, 1, 0, 0);
                    for (int i = 0; i <= m; ++i) for (int j = 0; j <= n; ++j)
                        for (int f = 0; f < 2; ++f) for (int t = 0; t < 4; ++t) {
                            if (cnt[i, j, f, t] == int.MaxValue) continue;
                            if (i < m) Step(i, j, f, t, 0, cnt[i, j, f, t], d[i, j, f, t]);
                            if (j < n) Step(i, j, f, t, 1, cnt[i, j, f, t], d[i, j, f, t]);
                        }
                    for (int f = 0; f < 2; ++f)
                        if (cnt[m, n, f, 3] < bestCount || cnt[m, n, f, 3] == bestCount && d[m, n, f, 3] < best) { bestCount = cnt[m, n, f, 3]; best = d[m, n, f, 3]; }
                }
                var r = new ContourLoft { CheckSelfIntersections = false, IsAdmissible = t => !Bad(new Tri { OnFirst = t.EdgeOnFirst, I = t.I, J = t.J }) }.Compute(P, Q);
                Assert.Equal(bestCount, r.InadmissibleTriangles);
                Assert.True(Math.Abs(r.TotalCost - best) <= 1e-12 * best, $"seed {seed}: {r.TotalCost} vs {best}");
                Assert.Equal(0, r.FullRegionFallbacks);
            }
        }

        [Fact]
        public void CyclicRelabelling_OfEitherContour_GivesTheSameBand()
        {
            // the optimal start moves through every position of the divide and conquer over the starts; the band must not change
            for (int seed = 0; seed < 4; ++seed) {
                var rand = new Random(40 + seed);
                int m = 37 + seed * 5, n = 17 + seed * 4;
                var P = Star(rand, m, 0); var Q = Star(rand, n, 0.6, 0.2, -0.1, 0.75);
                var loft = new ContourLoft { CheckSelfIntersections = false };
                double reference = loft.Compute(P, Q).TotalCost;
                for (int r = 1; r < n; ++r) {
                    var Qr = Enumerable.Range(0, n).Select(k => Q[(k + r) % n]).ToList();
                    var res = loft.Compute(P, Qr);
                    Assert.Equal(ContourLoftStatus.Success, res.Status);
                    Assert.True(Math.Abs(res.TotalCost - reference) <= 1e-12 * reference, $"seed {seed}, shift {r}: {res.TotalCost} vs {reference}");
                    Assert.Equal(0, res.FullRegionFallbacks);   // the bounded regions always held the band (O(m n log n))
                }
                for (int r = 1; r < m; r += 3) {
                    var Pr = Enumerable.Range(0, m).Select(k => P[(k + r) % m]).ToList();
                    var res = loft.Compute(Pr, Q);
                    Assert.True(Math.Abs(res.TotalCost - reference) <= 1e-12 * reference, $"seed {seed}, P shift {r}: {res.TotalCost} vs {reference}");
                }
            }
        }

        [Theory]
        [InlineData(64, 64)]
        [InlineData(256, 256)]
        [InlineData(512, 128)]
        public void SearchWork_IsMNLogN(int m, int n)
        {
            // the divide and conquer visits each grid node O(log n) times; a search that touched whole rows per start would be
            // O(m n^2) (it did once: the per-row reset of the state arrays)
            var rand = new Random(m + n);
            var P = Star(rand, m, 0); var Q = Star(rand, n, 0.5, 0.05, 0, 0.8);
            var r = new ContourLoft { CheckSelfIntersections = false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, r.Status);
            Assert.Equal(0, r.FullRegionFallbacks);
            double bound = 4.0 * (m + 1) * (n + 1) * (Math.Log(n, 2) + 2);
            Assert.True(r.SearchWork <= bound, $"{r.SearchWork} > {bound}");
            Assert.True(r.SearchWork < (long)(m + 1) * (n + 1) * n / 4, "not below the all-starts cost");
        }

        static void Relax(ref int c, ref double d, int c2, double d2)
        {
            if (c2 < c || c2 == c && d2 < d) { c = c2; d = d2; }
        }

        // ---- closed forms ----

        [Theory]
        [InlineData(6, 2.0, 1.0, 1.5)]
        [InlineData(12, 1.0, 1.0, 0.25)]
        [InlineData(5, 3.0, 0.5, 2.0)]
        public void CoaxialRegularPolygons_MinimumArea_IsTheFrustumSide(int n, double r1, double r2, double h)
        {
            var P = Regular(n, r1, 0); var Q = Regular(n, r2, h);
            var r = new ContourLoft().Compute(P, Q);
            AssertValidBand(r, P, Q);
            Assert.Equal(0, r.StartOffset);
            double s = Math.Sin(Math.PI / n), c = Math.Cos(Math.PI / n);
            double slant = Math.Sqrt(h * h + (r1 - r2) * (r1 - r2) * c * c);
            double expected = n * (r1 + r2) * s * slant;   // n trapezoids, parallel sides 2 r sin(pi / n)
            Assert.True(Math.Abs(Invariance.Area(r.Mesh) - expected) <= 1e-12 * expected, $"{Invariance.Area(r.Mesh)} vs {expected}");
            Assert.True(Math.Abs(r.TotalCost - expected) <= 1e-12 * expected);
        }

        [Theory]
        [InlineData(6, 2.0, 1.0, 1.5)]
        [InlineData(8, 1.0, 1.5, 0.5)]
        public void CoaxialRegularPolygons_MaximumVolume_WithCaps_IsTheFrustum(int n, double r1, double r2, double h)
        {
            // rotate the second one so that the start must be found: P_0 meets Q_3
            var P = Regular(n, r1, 0); var Q = Regular(n, r2, h, -3 * 2 * Math.PI / n);
            var r = new ContourLoft { Cost = ContourLoftCost.MaximumVolume, Caps = true }.Compute(P, Q);
            AssertValidBand(r, P, Q, caps: true);
            var spanning = Spanning(r.Mesh, n);
            Assert.All(Enumerable.Range(0, n), i => Assert.Contains((i, (i + 3) % n), spanning));
            double A1 = 0.5 * n * r1 * r1 * Math.Sin(2 * Math.PI / n), A2 = 0.5 * n * r2 * r2 * Math.Sin(2 * Math.PI / n);
            double expected = h / 3 * (A1 + A2 + Math.Sqrt(A1 * A2));
            Assert.True(Math.Abs(Invariance.Volume(r.Mesh) - expected) <= 1e-12 * expected, $"{Invariance.Volume(r.Mesh)} vs {expected}");
            Assert.Equal(2 * n + 2 * (n - 2), r.Mesh.TriangleCount);   // no Steiner points in the default caps
        }

        [Fact]
        public void Caps_WithRefinement_AddInteriorPointsOnly_AndStayClean()
        {
            var P = Regular(10, 3, 0); var Q = Regular(7, 2, 2, 0.3);
            var r = new ContourLoft { Caps = true, CapMesher = new QualityMesh2 { MinAngle = 25, MaxArea = 0.2 } }.Compute(P, Q);
            AssertValidBand(r, P, Q, caps: true);
            Assert.True(r.Mesh.VertexCount > 17);
            for (int v = 17; v < r.Mesh.VertexCount; ++v) {
                double z = r.Mesh.GetVertex(v).z;
                Assert.True(z == 0 || z == 2, "a Steiner point off its cap's plane: " + z);
            }
        }

        // ---- orientation, swapped roles, normalization ----

        [Fact]
        public void ReversedSecondContour_GivesTheSameBand()
        {
            var rand = new Random(11);
            var P = Star(rand, 9, 0); var Q = Star(rand, 6, 1);
            var Qr = Enumerable.Range(0, Q.Count).Select(k => Q[(Q.Count - k) % Q.Count]).ToList();   // Qr[k] = Q[-k]
            var a = new ContourLoft().Compute(P, Q); var b = new ContourLoft().Compute(P, Qr);
            AssertValidBand(a, P, Q); AssertValidBand(b, P, Qr);
            Func<int, int> map = v => v < 9 ? v : 9 + (Q.Count - (v - 9)) % Q.Count;   // b's vertex -> a's vertex
            var ka = a.Mesh.TriangleIndices().Select(t => Canon(a.Mesh.GetTriangle(t))).OrderBy(s => s, StringComparer.Ordinal);
            var kb = b.Mesh.TriangleIndices().Select(t => { var x = b.Mesh.GetTriangle(t); return Canon(new Index3i(map(x.a), map(x.b), map(x.c))); }).OrderBy(s => s, StringComparer.Ordinal);
            Assert.Equal(ka, kb);
            Assert.Equal(a.TotalCost, b.TotalCost, 12);
        }

        [Fact]
        public void SwappedContours_GiveTheSameTriangles()
        {
            var rand = new Random(12);
            var P = Star(rand, 5, 0); var Q = Star(rand, 8, 1);
            var a = new ContourLoft().Compute(P, Q); var b = new ContourLoft().Compute(Q, P);
            AssertValidBand(a, P, Q); AssertValidBand(b, Q, P);
            Func<int, int> map = v => v < 8 ? 5 + v : v - 8;
            var ka = a.Mesh.TriangleIndices().Select(t => Canon(a.Mesh.GetTriangle(t))).OrderBy(s => s, StringComparer.Ordinal);
            var kb = b.Mesh.TriangleIndices().Select(t => { var x = b.Mesh.GetTriangle(t); return Canon(new Index3i(map(x.a), map(x.b), map(x.c))); }).OrderBy(s => s, StringComparer.Ordinal);
            Assert.Equal(ka, kb);
        }

        static string Canon(Index3i t)
        {
            // rotate so the smallest id comes first (keeps the orientation)
            if (t.b < t.a && t.b < t.c) t = new Index3i(t.b, t.c, t.a); else if (t.c < t.a && t.c < t.b) t = new Index3i(t.c, t.a, t.b);
            return t.a + "," + t.b + "," + t.c;
        }

        [Fact]
        public void NormalizeForCost_MatchesOffsetSimilarContoursLikeAlignedOnes()
        {
            // the same 12-gon, the upper one far to the side and twice as large: the normalized cost matches vertex i with i,
            // as for the aligned pair; the plain area cost does not
            var P = Regular(12, 1, 0); var Q = Regular(12, 2, 0.5, 0, 3.5, 0);
            var plain = new ContourLoft { CheckSelfIntersections = false }.Compute(P, Q);
            var norm = new ContourLoft { NormalizeForCost = true, CheckSelfIntersections = false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, norm.Status);
            Assert.Equal(0, norm.StartOffset);
            // normalized, both are the 12-gon of area 1 at unit distance: the cost is the side of that prism
            double rr = Math.Sqrt(2 / (12 * Math.Sin(2 * Math.PI / 12)));
            double prism = 12 * 2 * rr * Math.Sin(Math.PI / 12);
            Assert.True(Math.Abs(norm.TotalCost - prism) <= 1e-12 * prism, $"{norm.TotalCost} vs {prism}");
            var spanning = Spanning(norm.Mesh, 12);
            Assert.All(Enumerable.Range(0, 12), i => Assert.Contains((i, i), spanning));
            Assert.Equal(ContourLoftStatus.Success, plain.Status);
            Assert.False(Enumerable.Range(0, 12).All(i => Spanning(plain.Mesh, 12).Contains((i, i))));
        }

        static HashSet<(int, int)> Spanning(DMesh3 mesh, int m)
        {
            var s = new HashSet<(int, int)>();
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e);
                if ((ev.a < m) != (ev.b < m)) s.Add(ev.a < m ? (ev.a, ev.b - m) : (ev.b, ev.a - m));
            }
            return s;
        }

        // ---- the terrain client (TERRAIN.md 9.2) ----

        // an upper square of side 20 one unit above its offset by 5, the lower contour, which has vertices only at the middle of
        // each side (5 from the square) and in the middle of each corner arc (5 / sqrt 2 from the lines of both adjacent edges)
        static (List<Vector3d>, List<Vector3d>) Benches()
        {
            const double a = 10, d = 3.5355339059327378;   // 5 / sqrt(2)
            var upper = new List<Vector3d> { new Vector3d(-a, -a, 1), new Vector3d(a, -a, 1), new Vector3d(a, a, 1), new Vector3d(-a, a, 1) };
            var lower = new List<Vector3d> {
                new Vector3d(0, -a - 5, 0), new Vector3d(a + d, -a - d, 0), new Vector3d(a + 5, 0, 0), new Vector3d(a + d, a + d, 0),
                new Vector3d(0, a + 5, 0), new Vector3d(-a - d, a + d, 0), new Vector3d(-a - 5, 0, 0), new Vector3d(-a - d, -a - d, 0) };
            return (lower, upper);
        }

        [Fact]
        public void Terrain_SlopeOracle_RejectsTheSteepBeyondTheEdgeTriangle()
        {
            var (lower, upper) = Benches();
            SlopeSpec spec = SlopeSpec.Ratio(4);   // 1:4, needs a horizontal run of 4 per unit height; the offset is 5
            // plain minimum area: an upper edge takes an arc vertex as its apex, beyond the edge's end and 3.54 from its line: too steep
            var plain = new ContourLoft().Compute(lower, upper);
            AssertValidBand(plain, lower, upper);
            Assert.Contains(plain.Mesh.TriangleIndices(), t => !IsNotSteeper(plain.Mesh, t, spec));
            // under the oracle: every triangle complies
            var steep = new ContourLoft { IsAdmissible = ContourLoft.NotSteeperThan(spec) }.Compute(lower, upper);
            AssertValidBand(steep, lower, upper);
            Assert.All(steep.Mesh.TriangleIndices(), t => Assert.True(IsNotSteeper(steep.Mesh, t, spec)));
            Assert.True(steep.TotalCost >= plain.TotalCost);
            // the oracle's band also by brute force: the same minimum count (0) and cost
            var brute = Brute(lower, upper, t => { var (a, b, c) = Points(lower, upper, t); return TriArea(a, b, c); },
                t => { var (a, b, c) = Points(lower, upper, t); return !Slope.IsNotSteeper(a, b, c, spec); });
            Assert.Equal(0, brute.MinCount);
            Assert.True(Math.Abs(brute.Best - steep.TotalCost) <= 1e-12 * brute.Best);
        }

        [Fact]
        public void Terrain_TooSteepSpec_HasNoAdmissibleBand_WithTheExactMinimumCount()
        {
            var (lower, upper) = Benches();
            SlopeSpec spec = SlopeSpec.Ratio(40);   // a run of 40 per unit height, more than any vertex is from any edge line
            var r = new ContourLoft { IsAdmissible = ContourLoft.NotSteeperThan(spec) }.Compute(lower, upper);
            Assert.Equal(ContourLoftStatus.NoAdmissibleBand, r.Status);
            Assert.Null(r.Mesh);
            var brute = Brute(lower, upper, t => 0, t => { var (a, b, c) = Points(lower, upper, t); return !Slope.IsNotSteeper(a, b, c, spec); });
            Assert.True(brute.MinCount > 0);
            Assert.Equal(brute.MinCount, r.InadmissibleTriangles);
        }

        static bool IsNotSteeper(DMesh3 mesh, int tid, SlopeSpec spec)
        {
            Index3i t = mesh.GetTriangle(tid);
            return Slope.IsNotSteeper(mesh.GetVertex(t.a), mesh.GetVertex(t.b), mesh.GetVertex(t.c), spec);
        }

        // ---- statuses ----

        [Fact]
        public void InvalidInputs_AreReported()
        {
            var P = Regular(5, 1, 0); var Q = Regular(4, 1, 1);
            var loft = new ContourLoft();
            Assert.Equal(ContourLoftStatus.InvalidInput, loft.Compute(P.Take(2).ToList(), Q).Status);
            var nan = new List<Vector3d>(Q); nan[2] = new Vector3d(double.NaN, 0, 1);
            Assert.Equal(ContourLoftStatus.InvalidInput, loft.Compute(P, nan).Status);
            var rep = new List<Vector3d>(Q); rep.Insert(2, rep[2]);
            Assert.Equal(ContourLoftStatus.InvalidInput, loft.Compute(P, rep).Status);
            var closing = new List<Vector3d>(Q) { Q[0] };
            Assert.Equal(ContourLoftStatus.InvalidInput, loft.Compute(P, closing).Status);
            var line = new List<Vector3d> { new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(2, 0, 1), new Vector3d(1, 0, 1) };
            var rz = loft.Compute(P, line);
            Assert.Equal(ContourLoftStatus.InvalidInput, rz.Status);
            Assert.Contains("zero area", rz.Reason);
            // the same plane, and a tilted contour crossing the other's plane
            var r0 = loft.Compute(P, Regular(4, 0.5, 0));
            Assert.Equal(ContourLoftStatus.InvalidInput, r0.Status);
            Assert.Contains("separated", r0.Reason);
            var tilted = Regular(4, 1, 0).Select(p => new Vector3d(p.x, p.y, 0.1 + p.x)).ToList();
            Assert.Equal(ContourLoftStatus.InvalidInput, loft.Compute(P, tilted).Status);
            Assert.Throws<ArgumentNullException>(() => loft.Compute(null, Q));
            Assert.Throws<ArgumentException>(() => new ContourLoft { Cost = ContourLoftCost.Custom }.Compute(P, Q));
            Assert.Throws<ArgumentException>(() => new ContourLoft { MaxCells = 0 }.Compute(P, Q));
        }

        [Fact]
        public void TooLarge_IsReportedBeforeAnyWork()
        {
            var P = Regular(5, 1, 0); var Q = Regular(4, 1, 1);
            int calls = 0;
            var loft = new ContourLoft { MaxCells = 19, IsAdmissible = t => { calls++; return true; } };
            var r = loft.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.TooLarge, r.Status);
            Assert.Null(r.Mesh);
            Assert.Equal(0, calls);
            loft.MaxCells = 20;
            Assert.Equal(ContourLoftStatus.Success, loft.Compute(P, Q).Status);
        }

        [Fact]
        public void InvalidCost_IsReported()
        {
            var P = Regular(5, 1, 0); var Q = Regular(4, 1, 1);
            var nan = new ContourLoft { Cost = ContourLoftCost.Custom, CustomCost = t => t.I == 2 ? double.NaN : 1 }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.InvalidCost, nan.Status);
            Assert.Null(nan.Mesh);
            var huge = new ContourLoft { Cost = ContourLoftCost.Custom, CustomCost = t => t.EdgeOnFirst ? 1e307 : -1e307 }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.InvalidCost, huge.Status);
            Assert.Contains("too large", huge.Reason);
            var fine = new ContourLoft { Cost = ContourLoftCost.Custom, CustomCost = t => t.EdgeOnFirst ? 1e300 : -1e300, CheckSelfIntersections = false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, fine.Status);
        }

        [Fact]
        public void AllInadmissible_ReportsEveryTriangle()
        {
            var P = Regular(7, 1, 0); var Q = Regular(4, 1, 1);
            var r = new ContourLoft { IsAdmissible = t => false }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.NoAdmissibleBand, r.Status);
            Assert.Equal(11, r.InadmissibleTriangles);
            Assert.Null(r.Mesh);
        }

        [Fact]
        public void TwistedBand_IsSelfIntersecting_NotReturned()
        {
            // a cost that rewards long spanning edges twists the band half a turn: every spanning edge P_i Q_i+2 of two coaxial
            // squares runs through the axis
            var P = Regular(4, 1, 0); var Q = Regular(4, 1, 1);
            var loft = new ContourLoft { Cost = ContourLoftCost.Custom, CustomCost = t => -((t.A - t.C).Length + (t.B - t.C).Length) };
            var r = loft.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.SelfIntersecting, r.Status);
            Assert.Null(r.Mesh);
            // without the check the band is returned, and it does intersect itself
            loft.CheckSelfIntersections = false;
            var unchecked_ = loft.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.Success, unchecked_.Status);
            var spanning = Spanning(unchecked_.Mesh, 4);
            Assert.All(Enumerable.Range(0, 4), i => Assert.Contains((i, (i + 2) % 4), spanning));
            Assert.True(MeshRepairMetrics.HasSelfIntersections(unchecked_.Mesh));
        }

        [Fact]
        public void CapThatMovesAContourVertex_IsCapFailed()
        {
            var P = Regular(9, 1, 0); var Q = Regular(6, 1, 1, 0.1);
            var r = new ContourLoft { Caps = true, CapMesher = new QualityMesh2 { MinAngle = 0, SnapTolerance = 0.3 } }.Compute(P, Q);
            Assert.Equal(ContourLoftStatus.CapFailed, r.Status);
            Assert.Null(r.Mesh);
        }

        // ---- invariance and reuse ----

        static (List<Vector3d>, List<Vector3d>) InvarianceInput()
        {
            var rand = new Random(77);
            return (Star(rand, 23, 0, 0, 0, 2), Star(rand, 14, 1.3, 0.3, -0.2, 1.6));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarity_GivesTheSameBand(string name)
        {
            Similarity s = Similarity.ByName(name);
            var (P, Q) = InvarianceInput();
            foreach (var cost in new[] { ContourLoftCost.MinimumArea, ContourLoftCost.MaximumVolume })
                foreach (bool caps in new[] { false, true }) {
                    var loft = new ContourLoft { Cost = cost, Caps = caps };
                    var expected = loft.Compute(P, Q);
                    AssertValidBand(expected, P, Q, caps);
                    var Ps = P.Select(s.Apply).ToList(); var Qs = Q.Select(s.Apply).ToList();
                    var got = loft.Compute(Ps, Qs);
                    Assert.Equal(ContourLoftStatus.Success, got.Status);
                    if (s.IsExact) {
                        Assert.Equal(expected.StartOffset, got.StartOffset);
                        Assert.Equal("", Invariance.Difference(expected.Mesh, Invariance.MappedBack(got.Mesh, s)));
                        double f = cost == ContourLoftCost.MinimumArea ? s.Scale * s.Scale : s.Scale * s.Scale * s.Scale;
                        Assert.Equal(expected.TotalCost * f, got.TotalCost);
                    } else if (!caps) {
                        // the band's triangles (ids) are the same; the caps' Delaunay triangulation may flip at near-cocircular points
                        var a = expected.Mesh.TriangleIndices().Select(t => Canon(expected.Mesh.GetTriangle(t))).OrderBy(x => x, StringComparer.Ordinal);
                        var b = got.Mesh.TriangleIndices().Select(t => Canon(got.Mesh.GetTriangle(t))).OrderBy(x => x, StringComparer.Ordinal);
                        Assert.Equal(a, b);
                    } else {
                        Assert.Equal("", MeshRobustRepair.NotCleanReason(got.Mesh));
                    }
                }
        }

        [Fact]
        public void Reuse_ComputeTwice_EqualsFreshInstances()
        {
            var (P, Q) = InvarianceInput();
            var rand = new Random(5);
            var P2 = Star(rand, 8, 0); var Q2 = Star(rand, 11, 2);
            var loft = new ContourLoft { Caps = true, IsAdmissible = t => !(t.EdgeOnFirst && t.I == 1 && t.J == 0) };
            var a1 = loft.Compute(P, Q);
            var b1 = loft.Compute(P2, Q2);
            var a2 = loft.Compute(P, Q);
            var fresh = new ContourLoft { Caps = true, IsAdmissible = loft.IsAdmissible };
            var a0 = fresh.Compute(P, Q);
            var b0 = new ContourLoft { Caps = true, IsAdmissible = loft.IsAdmissible }.Compute(P2, Q2);
            Assert.Equal(ContourLoftStatus.Success, a0.Status);
            Assert.Equal("", Invariance.Difference(a0.Mesh, a1.Mesh));
            Assert.Equal("", Invariance.Difference(a0.Mesh, a2.Mesh));
            Assert.Equal(b0.Status, b1.Status);
            if (b0.Mesh != null) Assert.Equal("", Invariance.Difference(b0.Mesh, b1.Mesh));
            Assert.Equal(a0.TotalCost, a2.TotalCost);
            // results are separate objects
            Assert.NotSame(a1, a2);
        }
    }
}
