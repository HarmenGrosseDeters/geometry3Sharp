using System;
using System.Collections.Generic;
using System.Linq;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The witness of the oracle's global Delaunay pass (MESHING2D_FAST_ORACLE.md section 6): the oracle's own Delaunay triangulation of
    /// the used vertices, certified, so that the pass runs only on the output triangles that are not its triangles. A witness that is
    /// wrong in any way the certificate tests is refused, and the pass runs in full with the same verdicts; the reduction gives the
    /// full pass's reports and counts (also compared in check mode by every run of Meshing2OracleFastPathCheckTests).
    /// </summary>
    public class Meshing2OracleDelaunayWitnessTests
    {
        static readonly OracleExpect M1 = new OracleExpect { Milestone = Milestone.M1 };
        static readonly OracleExpect M3 = new OracleExpect { Milestone = Milestone.M3 };

        static Mesh2Input Points(IList<(double x, double y)> p) =>
            new Mesh2Input { X = p.Select(q => q.x).ToArray(), Y = p.Select(q => q.y).ToArray() };

        static Mesh2Input RandomPoints(int seed, int n)
        {
            var rnd = new Random(seed);
            var p = new List<(double, double)>();
            for (int i = 0; i < n; ++i) p.Add((rnd.Next(0, 1 << 20) / 1024.0, rnd.Next(0, 1 << 20) / 1024.0));
            return Points(p.Distinct().ToList());
        }

        static (OracleReport report, string pass) Run(Mesh2Input input, Mesh2Output o, OracleExpect e, Func<List<int[]>, List<int[]>> corrupt = null,
            bool skipCertificate = false, bool failWitness = false)
        {
            Oracle.CorruptWitnessForTests = corrupt;
            Oracle.SkipWitnessCertificateForTests = skipCertificate;
            Oracle.FailWitnessForTests = failWitness;
            Oracle.LastGlobalPassForTests = null;
            try {
                var report = Oracle.Check(input, o, e);
                return (report, Oracle.LastGlobalPassForTests);
            } finally {
                Oracle.CorruptWitnessForTests = null;
                Oracle.SkipWitnessCertificateForTests = false;
                Oracle.FailWitnessForTests = false;
            }
        }

        static string Summary(OracleReport r) => r + " | " + r.MergeZoneExcused + " " + r.O11GlobalExcused + " " + r.O11LocalExcused + " " + r.TieEdges;

        static Mesh2Output WithTriangles(Mesh2Output o, int[][] tris) => new Mesh2Output {
            Status = o.Status, VX = o.VX, VY = o.VY, InputToVertex = o.InputToVertex, Triangles = tris, Neighbours = SelfCheck.NeighboursOf(tris),
            BadTriangles = o.BadTriangles, Subsegments = o.Subsegments };

        /// <summary> an interior edge whose flip gives two positive triangles, not a tie: the flipped triangulation is not Delaunay </summary>
        static int[][] Flip(Mesh2Output o)
        {
            var t = o.Triangles;
            Q X(int i) => Q.FromDouble(o.VX[i]);
            Q Y(int i) => Q.FromDouble(o.VY[i]);
            int Orient(int a, int b, int c) => ((X(b) - X(a)) * (Y(c) - Y(a)) - (Y(b) - Y(a)) * (X(c) - X(a))).Sign;
            int InCircle(int a, int b, int c, int d)
            {
                Q ax = X(a) - X(d), ay = Y(a) - Y(d), bx = X(b) - X(d), by = Y(b) - Y(d), cx = X(c) - X(d), cy = Y(c) - Y(d);
                return ((ax * ax + ay * ay) * (bx * cy - cx * by) + (bx * bx + by * by) * (cx * ay - ax * cy) + (cx * cx + cy * cy) * (ax * by - bx * ay)).Sign;
            }
            for (int i = 0; i < t.Length; ++i)
                for (int k = 0; k < 3; ++k) {
                    int a = t[i][k], b = t[i][(k + 1) % 3], c = t[i][(k + 2) % 3];
                    for (int j = i + 1; j < t.Length; ++j)
                        for (int m = 0; m < 3; ++m) {
                            if (t[j][m] != b || t[j][(m + 1) % 3] != a) continue;
                            int d = t[j][(m + 2) % 3];
                            if (Orient(c, a, d) <= 0 || Orient(d, b, c) <= 0) continue;
                            var copy = t.Select(x => (int[])x.Clone()).ToArray();
                            copy[i] = new[] { c, a, d };
                            copy[j] = new[] { d, b, c };
                            // the flipped edge c-d is not locally Delaunay: b strictly inside the circumcircle of (c, a, d)
                            if (InCircle(c, a, d, b) > 0) return copy;
                        }
                }
            throw new InvalidOperationException("no edge to flip");
        }

        /// <summary> on random points (Delaunay and with a flipped edge) and the lens fuzz in the conforming mode (holes, merge zones)
        /// the witness is certified and used whenever the global pass runs (no fallback), and the full pass (the witness failed) gives the same reports and counts </summary>
        [Fact]
        public void Witness_IsUsed_AndTheFallbackGivesTheSameReports()
        {
            int used = 0;
            void Compare(Mesh2Input input, Mesh2Output o, OracleExpect e, string what)
            {
                var (withWitness, pass) = Run(input, o, e);
                if (!withWitness.GlobalDelaunayChecked) return;
                Assert.True(pass == "witness", what + ": " + pass);
                ++used;
                var (full, fallback) = Run(input, o, e, failWitness: true);
                Assert.Equal("fallback: failed for tests", fallback);
                Assert.Equal(Summary(full), Summary(withWitness));
            }
            for (int seed = 0; seed < 10; ++seed) {
                var input = RandomPoints(seed, 50 + 40 * seed);
                var o = CandidateRegistry.For(Milestone.M1).Run(input);
                Compare(input, o, M1, "points " + seed);
                Compare(input, WithTriangles(o, Flip(o)), M1, "flipped points " + seed);
            }
            for (int seed = 0; seed < 12; ++seed) {
                var input = QualityMesh2LensDeletionTests.Fuzz(7000 + seed, new[] { 20.0, 25, 30 }[seed % 3], new[] { 0, 0.25, 0.125 }[seed % 3]);
                input.ConstrainedQuality = false;   // (the conforming mode: the global pass runs, with its excuses)
                var o = new QualityMesh2Candidate().Run(input);
                Compare(input, o, M3, "fuzz " + seed);
            }
            Assert.True(used >= 25, "the witness was used " + used + " times");
        }

        /// <summary> a non-Delaunay output (one flipped edge in a Delaunay triangulation of random points) is still found: the two
        /// flipped triangles are not triangles of the witness, and the pass runs on them </summary>
        [Fact]
        public void FlippedEdge_IsReported_WithTheWitness()
        {
            for (int seed = 0; seed < 5; ++seed) {
                var input = RandomPoints(100 + seed, 200);
                var o = CandidateRegistry.For(Milestone.M1).Run(input);
                var (good, gp) = Run(input, o, M1);
                Assert.True(good.Ok && gp == "witness", good + " " + gp);
                var (bad, bp) = Run(input, WithTriangles(o, Flip(o)), M1);
                Assert.Equal("witness", bp);
                Assert.Equal(2, bad.Violations.Count(v => v.Kind == Violation.NotDelaunayGlobal));
            }
        }

        /// <summary>
        /// The case the local test alone misses: a thin hole [2,8] x [1,1.01] in [0,10] x [-1,2] with a vertex E (5,0) below it. The
        /// constrained Delaunay triangulation (the mesher without conforming) has the triangle P Q E under the hole's lower side, and the
        /// hole's upper corners lie inside its circumcircle (centre (5,5), radius 5), across the hole: every edge is locally (constrained)
        /// Delaunay, the mesh is not conforming Delaunay. Judged as conforming, the global pass with the witness reports it, as the
        /// reference path does
        /// </summary>
        [Fact]
        public void AcrossAHole_TheGlobalPassReports_WithTheWitness()
        {
            var input = Points(new List<(double, double)> { (0, -1), (10, -1), (10, 2), (0, 2), (2, 1), (8, 1), (8, 1.01), (2, 1.01), (5, 0) });
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Rings.Add(new RingInput(new[] { 4, 5, 6, 7 }, true));
            var o = CandidateRegistry.For(Milestone.M2).Run(input);
            Assert.Equal(Mesh2Status.Success, o.Status);
            Assert.Equal(9, o.VX.Length);
            int e = 8;
            Assert.Contains(o.Triangles, t => t.Contains(4) && t.Contains(5) && t.Contains(e));
            var m2 = new OracleExpect { Milestone = Milestone.M2 };
            var (cdt, _) = Run(input, o, m2);
            Assert.True(cdt.Ok, cdt.ToString());
            input.Conforming = true;
            var (fast, pass) = Run(input, o, m2);
            Assert.Equal("witness", pass);
            Assert.True(fast.Has(Violation.NotDelaunayGlobal), fast.ToString());
            Assert.False(fast.Has(Violation.NotDelaunayLocal), fast.ToString());
            var reference = Oracle.Check(input, o, m2.WithReference());
            Assert.Equal(fast.ToString(), reference.ToString());
        }

        /// <summary>
        /// The global test's hidden vertex (HiddenByO11Unsplittable, through Behind) on the fast path asks the subsegment grid for the
        /// pieces near the box of the triangle and the vertex. The layout of the oracle's self-check 7b: legs A (4, 8)-(12, 8) and
        /// B (4, 8)-(12, 8.375) at a small angle, B's first piece ending 3/64 from A, the flat triangle (4.25, 8.125), (4.75, 8.125),
        /// (4.5, 8.25) above it, and the vertex (4.5, 8) inside its circumcircle, hidden by the piece, which lies below the triangle's
        /// box. With 10000 short subsegments elsewhere the grid's cells are 0.08 wide, so a query box of the triangle alone misses the
        /// piece: the vertex is then reported. With the vertex in the box it is excused, as on the reference path (and the check mode
        /// compares the grid's answer with the scan of all subsegments)
        /// </summary>
        [Fact]
        public void HiddenVertex_BelowTheTrianglesBox_IsFoundThroughTheGrid()
        {
            var xy = new List<double> { 0, 0, 16, 0, 16, 16, 0, 16, 4, 8, 12, 8, 12, 8.375, 5, 8.046875, 4.25, 8.125, 4.75, 8.125, 4.5, 8.25, 4.5, 8, 4, 7.5, 5, 7.5 };
            var input = new Mesh2Input { X = xy.Where((_, i) => i % 2 == 0).ToArray(), Y = xy.Where((_, i) => i % 2 == 1).ToArray() };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Segments.Add(new SegmentInput(4, 5));
            input.Segments.Add(new SegmentInput(4, 6));
            input.MinAngleDeg = 20;
            var vx = input.X.ToList(); var vy = input.Y.ToList();
            var subs = new List<SubsegmentOutput> { new SubsegmentOutput(4, 7), new SubsegmentOutput(7, 6), new SubsegmentOutput(4, 11), new SubsegmentOutput(11, 5) };
            // 10000 short pieces on a lattice over [4, 12] x [9, 16]: they only make the grid fine
            for (int i = 0; i < 100; ++i)
                for (int j = 0; j < 100; ++j) {
                    vx.Add(4 + 0.08 * i); vy.Add(9 + 0.07 * j); vx.Add(4 + 0.08 * i + 0.01); vy.Add(9 + 0.07 * j);
                    subs.Add(new SubsegmentOutput(vx.Count - 2, vx.Count - 1));
                }
            var tris = new[] { new[] { 8, 9, 10 }, new[] { 11, 12, 13 } };
            var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vx.ToArray(), VY = vy.ToArray(), InputToVertex = Enumerable.Range(0, input.Count).ToArray(),
                Triangles = tris, Neighbours = SelfCheck.NeighboursOf(tris), BadTriangles = new int[0], Subsegments = subs };
            var e = new OracleExpect { Milestone = Milestone.M3, Tube = new Q(1, 16) };
            Oracle.CheckFastPathForTests = true;
            OracleReport fast;
            try { fast = Oracle.Check(input, o, e); } finally { Oracle.CheckFastPathForTests = false; }
            Assert.True(fast.GlobalDelaunayChecked, fast.ToString());
            Assert.False(fast.ToString().Contains("vertex 11 inside the circumcircle of 8 9 10"), fast.ToString());
            Assert.True(fast.O11GlobalExcused > 0, fast.ToString());
            var reference = Oracle.Check(input, o, e.WithReference());
            Assert.Equal(fast.O11GlobalExcused, reference.O11GlobalExcused);
        }

        /// <summary> two used vertices at the same point (different ids): no witness, the full pass, with its verdicts </summary>
        [Fact]
        public void DuplicateCoordinates_FallBackToTheFullPass()
        {
            var input = Points(new List<(double, double)> { (0, 0), (4, 0), (4, 4), (0, 4), (1, 1) });
            var vx = new[] { 0.0, 4, 4, 0, 1, 0 }; var vy = new[] { 0.0, 0, 4, 4, 1, 0 };
            var tris = new[] { new[] { 0, 1, 4 }, new[] { 1, 2, 4 }, new[] { 2, 3, 4 }, new[] { 3, 5, 4 } };
            var o = new Mesh2Output { Status = Mesh2Status.Success, VX = vx, VY = vy, InputToVertex = Enumerable.Range(0, 5).ToArray(),
                Triangles = tris, Neighbours = SelfCheck.NeighboursOf(tris), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            input.Conforming = true;
            var m2 = new OracleExpect { Milestone = Milestone.M2 };
            var (got, pass) = Run(input, o, m2);
            Assert.True(pass == "fallback: two vertices coincide", got + " | " + pass);
            var (full, _) = Run(input, o, m2, failWitness: true);
            Assert.Equal(Summary(full), Summary(got));
        }

        /// <summary> a 7 x 7 lattice: every hull side has five points between its corners (the ghosts' on-the-edge case of the
        /// conflict test, boundary edges between points that are no hull corners), every cell four cocircular points; and the same
        /// lattice with every second point of the bottom row dropped </summary>
        [Fact]
        public void Lattice_CollinearHullPoints_GiveACertifiedWitness()
        {
            for (int variant = 0; variant < 2; ++variant) {
                var p = new List<(double, double)>();
                for (int i = 0; i < 7; ++i)
                    for (int j = 0; j < 7; ++j)
                        if (variant == 0 || j != 0 || i % 2 == 0) p.Add((i, j));
                var input = Points(p);
                var o = CandidateRegistry.For(Milestone.M1).Run(input);
                var (r, pass) = Run(input, o, M1);
                Assert.Equal("witness", pass);
                Assert.True(r.Ok, r.ToString());
                var (full, _) = Run(input, o, M1, failWitness: true);
                Assert.Equal(Summary(full), Summary(r));
            }
        }

        static int[][] Reversed(int[][] w, int i) => w.Select((t, j) => j == i ? new[] { t[0], t[2], t[1] } : t).ToArray();

        public static IEnumerable<object[]> Corruptions() => new[] {
            new object[] { "reversed", "a witness triangle is not positive" },
            new object[] { "duplicated", "a directed edge in two witness triangles" },
            new object[] { "vertex dropped", "a used vertex is not in the witness" },
            new object[] { "triangle dropped", "a witness boundary edge is not on the hull" },
            new object[] { "edge flipped", "a witness edge is not locally Delaunay" },
        };

        /// <summary> a witness made wrong in each way the certificate tests is refused for that reason, and the verdicts stay those of
        /// the full pass (on a Delaunay output and on one with a flipped edge) </summary>
        [Theory]
        [MemberData(nameof(Corruptions))]
        public void CorruptWitness_IsRefused(string how, string why)
        {
            var input = RandomPoints(77, 300);
            var o = CandidateRegistry.For(Milestone.M1).Run(input);
            var flipped = WithTriangles(o, Flip(o));
            List<int[]> Corrupt(List<int[]> w)
            {
                switch (how) {
                    case "reversed": return Reversed(w.ToArray(), w.Count / 2).ToList();
                    case "duplicated": return w.Concat(new[] { w[w.Count / 2] }).ToList();
                    case "vertex dropped": {
                        // an interior vertex: all triangles around it removed
                        var count = new Dictionary<int, int>();
                        foreach (var t in w) foreach (int x in t) count[x] = count.TryGetValue(x, out int c) ? c + 1 : 1;
                        var hull = new HashSet<int>(BoundaryVertices(w));
                        int v = count.Keys.Where(x => !hull.Contains(x)).OrderBy(x => x).First();
                        return w.Where(t => !t.Contains(v)).ToList();
                    }
                    case "triangle dropped": {
                        // a triangle with no boundary vertex
                        var hull = new HashSet<int>(BoundaryVertices(w));
                        int i = w.FindIndex(t => t.All(x => !hull.Contains(x)));
                        return w.Where((t, j) => j != i).ToList();
                    }
                    case "edge flipped": {
                        var fo = new Mesh2Output { VX = o.VX, VY = o.VY, Triangles = w.ToArray() };
                        return Flip(fo).ToList();
                    }
                }
                throw new ArgumentException(how);
            }
            foreach (var output in new[] { o, flipped }) {
                var (full, _) = Run(input, output, M1, failWitness: true);
                var (got, pass) = Run(input, output, M1, Corrupt);
                Assert.Equal("fallback: " + why, pass);
                Assert.Equal(Summary(full), Summary(got));
            }
        }

        static IEnumerable<int> BoundaryVertices(List<int[]> w)
        {
            var directed = new HashSet<(int, int)>();
            foreach (var t in w) for (int k = 0; k < 3; ++k) directed.Add((t[k], t[(k + 1) % 3]));
            foreach (var (a, b) in directed) if (!directed.Contains((b, a))) { yield return a; yield return b; }
        }

        /// <summary>
        /// The winding check alone: the square [0,4]^2 with the midpoints of its sides and its centre, triangulated as a fan around the
        /// centre (Delaunay, all ties), and a witness that is that fan plus a second layer, the square's two triangles over the corners
        /// only. Every triangle is positive, no directed edge repeats (the fan's boundary edges are the half sides, the second layer's the
        /// whole sides), every vertex is in it, every boundary edge on the hull, every interior edge a tie: only the boundary's cross sum,
        /// twice the hull's, shows that it covers the square twice
        /// </summary>
        [Fact]
        public void DoubleCover_IsRefusedByTheWinding()
        {
            // 0 A(0,0) 1 E(2,0) 2 B(4,0) 3 F(4,2) 4 C(4,4) 5 G(2,4) 6 D(0,4) 7 H(0,2) 8 M(2,2)
            var input = Points(new List<(double, double)> { (0, 0), (2, 0), (4, 0), (4, 2), (4, 4), (2, 4), (0, 4), (0, 2), (2, 2) });
            var fan = Enumerable.Range(0, 8).Select(i => new[] { i, (i + 1) % 8, 8 }).ToArray();
            var o = new Mesh2Output { Status = Mesh2Status.Success, VX = input.X, VY = input.Y, InputToVertex = Enumerable.Range(0, 9).ToArray(),
                Triangles = fan, Neighbours = SelfCheck.NeighboursOf(fan), BadTriangles = new int[0], Subsegments = new List<SubsegmentOutput>() };
            var (plain, plainPass) = Run(input, o, M1);
            Assert.True(plain.Ok, plain.ToString());
            var (got, pass) = Run(input, o, M1, w => fan.Concat(new[] { new[] { 0, 2, 4 }, new[] { 0, 4, 6 } }).ToList());
            Assert.Equal("fallback: the witness boundary does not wind once around the hull", pass);
            Assert.True(got.Ok, got.ToString());
        }

        /// <summary> the certificate is what the reduction rests on: with it skipped, a witness that holds the output's flipped
        /// triangles hides their violation; with it, the witness is refused and the violation found </summary>
        [Fact]
        public void WithoutTheCertificate_AWrongWitnessHidesAViolation()
        {
            var input = RandomPoints(5, 120);
            var o = CandidateRegistry.For(Milestone.M1).Run(input);
            var bad = WithTriangles(o, Flip(o));
            List<int[]> Output(List<int[]> w) => bad.Triangles.ToList();
            var (hidden, hp) = Run(input, bad, M1, Output, skipCertificate: true);
            Assert.Equal("witness", hp);
            Assert.False(hidden.Has(Violation.NotDelaunayGlobal), hidden.ToString());
            var (found, fp) = Run(input, bad, M1, Output);
            Assert.Equal("fallback: a witness edge is not locally Delaunay", fp);
            Assert.True(found.Has(Violation.NotDelaunayGlobal), found.ToString());
        }

        /// <summary> many cocircular points (ties everywhere, the walk and the cavity meet in-circle signs of 0) still give a
        /// certified witness </summary>
        [Fact]
        public void CocircularPoints_GiveACertifiedWitness()
        {
            var p = new List<(double, double)>();
            // the points of a circle of radius 5 at integer coordinates (12 of them), and the centre
            foreach (var (x, y) in new[] { (5, 0), (4, 3), (3, 4), (0, 5) })
                foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (-1, -1), (1, -1) }) p.Add((sx * x, sy * y));
            p.Add((0, 0));
            var input = Points(p.Distinct().ToList());
            var o = CandidateRegistry.For(Milestone.M1).Run(input);
            var (r, pass) = Run(input, o, M1);
            Assert.Equal("witness", pass);
            Assert.True(r.Ok, r.ToString());
        }
    }
}
