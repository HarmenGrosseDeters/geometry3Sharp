using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The certifying checker on fans and fine coplanar faces: the cost of C2 and its stacks (#1632), the edge forms, the groups of
    /// equal pieces and the parallel loops against the direct computations, and the verdicts that the cost work must not change.
    /// </summary>
    public class BooleanCertificateFanTests
    {
        readonly ITestOutputHelper log;
        public BooleanCertificateFanTests(ITestOutputHelper log) { this.log = log; }

        static DMesh3 Cylinder(int slices) =>
            new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1f, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();

        // the cylinder shifted along x by `ulps` ulps of its x extent (the contact of Thingi10K that did not finish, as a procedural input)
        static DMesh3 Shifted(DMesh3 m, int ulps)
        {
            var r = new DMesh3(m);
            MeshTransforms.Translate(r, new Vector3d(ulps * Math.Pow(2, -53), 0, 0));
            return r;
        }

        static BooleanCertificate Run(DMesh3 a, DMesh3 b, Ops op) =>
            BooleanCertificate.Run(a, b, op, out _, x => { x.RegionClipping = false; if (op == Ops.Intersection) x.CullOutsideA = false; });

        static DMesh3 FineBox(Vector3d center, double half, int edgeVertices) =>
            new GridBox3Generator() { Box = new Box3d(center, new Vector3d(half, half, half)), EdgeVertices = edgeVertices }.Generate().MakeDMesh();

        // ---------------------------------------------------------------- the forms against the direct computations

        /// <summary>
        /// An edge form gives the sign of Dot(Cross(b - a, p - a), n) of the direct computation, for homogeneous points of
        /// different W (also where p is on the line, an end of the edge, or the normal is not the triangle's own).
        /// </summary>
        [Fact]
        public void EdgeForm_EqualsTheDirectSide_OnRandomHomogeneousPoints()
        {
            var rng = new Random(20261126);
            BigInteger Big() => new BigInteger(rng.NextDouble() * 2e6 - 1e6) * (rng.Next(3) == 0 ? BigInteger.Pow(10, rng.Next(0, 30)) : BigInteger.One);
            ExactPoint3 Pt() => ExactPoint3.Create(Big(), Big(), Big(), new BigInteger(rng.Next(1, 1000)) * (rng.Next(4) == 0 ? BigInteger.Pow(7, rng.Next(0, 20)) : BigInteger.One));
            int zero = 0, tests = 0;
            for (int k = 0; k < 300; ++k) {
                ExactPoint3 a = Pt(), b = Pt(), c = Pt();
                BigInteger[] n = BooleanCertificate.NormalOf(a, b, c);
                var form = new BooleanCertificate.EdgeForm(a, b, n);
                // (c itself, the ends, a point on the line a b by a combination, and random points)
                var points = new List<ExactPoint3> { a, b, c, Pt(), Pt() };
                points.Add(ExactPoint3.Create(a.X * b.W + b.X * a.W, a.Y * b.W + b.Y * a.W, a.Z * b.W + b.Z * a.W, 2 * a.W * b.W));       // the midpoint of a b
                foreach (ExactPoint3 p in points) {
                    int direct = BooleanCertificate.DirectSide(a, b, p, n);
                    Assert.Equal(direct, form.Side(p));
                    if (direct == 0) zero++;
                    tests++;
                }
                // (a normal that is not this triangle's: the form of another plane's orientation)
                BigInteger[] other = { Big(), Big(), Big() };
                var form2 = new BooleanCertificate.EdgeForm(a, b, other);
                foreach (ExactPoint3 p in points) Assert.Equal(BooleanCertificate.DirectSide(a, b, p, other), form2.Side(p));
            }
            Assert.True(zero >= 600, $"{zero} of {tests} on the line: the zero case is covered");
        }

        /// <summary>
        /// The stacks (with their groups of equal pieces), the "inside" tests, the straddle tests of C2 and the pair tests of
        /// the coplanar pieces, by the forms and the parallel loops, equal the direct computations on fine faces that are
        /// triangulated differently (the coplanar copies, the box tree) for every operation.
        /// </summary>
        [Fact]
        public void FormsGroupsAndLoops_EqualTheDirectTests_OnFineFaceContacts()
        {
            var a = FineBox(Vector3d.Zero, 1, 4);
            var b = FineBox(new Vector3d(2, 0.25, 0.125), 1, 5);
            long stack = 0, straddle = 0, pairs = 0;
            foreach (Ops op in BooleanOracle.AllOps) {
                BooleanCertificate c = Run(a, b, op);
                Assert.Equal("", c.CheckPieces());
                Assert.Equal("", c.AuditInPlaneForms(8000, out int s, out int t, out int p));
                Assert.Equal("", c.CheckEmbedding());
                stack += s; straddle += t; pairs += p;
            }
            log.WriteLine($"{stack} stack tests, {straddle} straddle tests, {pairs} pair tests");
            Assert.True(stack > 1000 && straddle > 1000 && pairs > 1000, $"{stack} / {straddle} / {pairs}");
        }

        /// <summary> The same on a fan: the cylinder and its 2-ulp shift (coplanar copies in the caps; every piece is in the plane of a cap or a side). </summary>
        [Theory]
        [InlineData(2)]
        [InlineData(24)]
        public void FormsGroupsAndLoops_EqualTheDirectTests_OnTheShiftedFan(int ulps)
        {
            DMesh3 a = Cylinder(8);
            BooleanCertificate c = Run(a, Shifted(a, ulps), Ops.Union);
            Assert.Equal("", c.CheckPieces());
            Assert.Equal("", c.AuditInPlaneForms(4000, out int s, out int t, out int p));
            Assert.Equal("", c.CheckEmbedding());
            Assert.True(s > 50 && t > 50 && p > 50, $"{s} / {t} / {p}");
            // (the groups of equal pieces are in use: pieces that are copies of one another)
            Assert.True(c.EmbeddingCopies > 0, "no coplanar copies");
        }

        // ---------------------------------------------------------------- the fan (#1632)

        /// <summary>
        /// The repro of #1632 at a size that the 5 s limit allows: the capped cylinder and its 2-ulp shift certify (every clause, all triangles
        /// covered), with the counts of pairs of the exact tests (the cost itself is in the Nightly run and the table of the PR).
        /// </summary>
        [Fact]
        public void ShiftedFan_Certifies_WithTheKnownPairCounts()
        {
            DMesh3 a = Cylinder(30);
            var sw = Stopwatch.StartNew();
            BooleanCertificate c = Run(a, Shifted(a, 2), Ops.Union);
            long boolean = sw.ElapsedMilliseconds;
            Assert.Equal("", c.CheckInputs());
            Assert.Equal("", c.CheckPieces());
            sw.Restart();
            Assert.Equal("", c.CheckEmbedding());
            long c2 = sw.ElapsedMilliseconds;
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.Uncovered);
            log.WriteLine($"boolean {boolean} ms, C2 {c2} ms, {c.EmbeddingPairs} pairs ({c.EmbeddingCoplanarPairs} coplanar), {c.EmbeddingPieces} classified and {c.EmbeddingCopies} copies");
                        // (the pair counts are those of the exact tests, as before the work on the cost: the cost itself is in the Nightly run and the PR's table)
            Assert.Equal(15935, c.EmbeddingPairs);
            Assert.Equal(9406, c.EmbeddingCoplanarPairs);
        }

        /// <summary> Two runs of the checker give the same verdict and counts (the loops are parallel: the lowest piece or pair decides, the counts are up to it). </summary>
        [Fact]
        public void ParallelLoops_AreDeterministic()
        {
            DMesh3 a = Cylinder(16);
            BooleanCertificate c1 = Run(a, Shifted(a, 2), Ops.Union), c2 = Run(a, Shifted(a, 2), Ops.Union);
            Assert.Equal(c1.Check(), c2.Check());
            Assert.Equal(c1.EmbeddingPairs, c2.EmbeddingPairs);
            Assert.Equal(c1.EmbeddingCoplanarPairs, c2.EmbeddingCoplanarPairs);
            Assert.Equal(c1.EmbeddingPieces, c2.EmbeddingPieces);
            for (int r = 0; r < 3; ++r) {
                // a piece moved by one grid unit crosses its neighbours: the same piece and pair in the message every time
                BooleanCertificate m = Run(a, Shifted(a, 2), Ops.Union);
                Assert.Equal("", m.CheckPieces());
                var p = m.PieceCorners[m.PieceCorners.Length / 2];
                p[0] = ExactPoint3.Create(p[0].X * 4 + p[0].W, p[0].Y * 4, p[0].Z * 4, p[0].W * 4);
                m.CheckPieces();
                string first = m.CheckEmbedding();
                Assert.NotEqual("", first);
                BooleanCertificate m2 = Run(a, Shifted(a, 2), Ops.Union);
                m2.CheckPieces();
                var p2 = m2.PieceCorners[m2.PieceCorners.Length / 2];
                p2[0] = ExactPoint3.Create(p2[0].X * 4 + p2[0].W, p2[0].Y * 4, p2[0].Z * 4, p2[0].W * 4);
                m2.CheckPieces();
                Assert.Equal(first, m2.CheckEmbedding());
            }
        }


        // one corner of piece k moved by num / 4 grid units along x (the pieces are then prepared again); the original corner
        static ExactPoint3 Move(BooleanCertificate m, int k, BigInteger num)
        {
            ExactPoint3[] p = m.PieceCorners[k];
            ExactPoint3 old = p[0];
            p[0] = ExactPoint3.Create(old.X * 4 + old.W * num, old.Y * 4, old.Z * 4, old.W * 4);
            return old;
        }

        // the key of a failure message in the order of the loops: the piece of a straddle, the pair of two pieces
        static (int, int) Key(string m)
        {
            int[] f = System.Text.RegularExpressions.Regex.Matches(m, @"\d+").Cast<System.Text.RegularExpressions.Match>().Select(x => int.Parse(x.Value)).ToArray();
            return m.StartsWith("pieces ") ? (f[0], f[2]) : (f[0], 0);
        }

        /// <summary>
        /// With broken pieces the verdict is the one of the lowest failing pair (or, with no failing pair, the lowest straddling piece),
        /// as the sequential loops gave. The loops are parallel and the verdict is picked in order afterwards: the message with three
        /// pieces broken at once is the lowest of the three messages with one broken. Once with corners that cross their neighbours
        /// (C2's pair test) and once with corners that leave their triangles by a quarter of a grid unit (its straddle test).
        /// </summary>
        [Theory]
        [InlineData("4000000000000000000000", "pieces ")]
        [InlineData("1", "piece ")]
        public void BrokenPieces_ReportTheLowestFailure_NotTheFirstThatFinishes(string numeratorText, string startsWith)
        {
            BigInteger num = BigInteger.Parse(numeratorText);
            DMesh3 a = Cylinder(16);
            BooleanCertificate m = Run(a, Shifted(a, 2), Ops.Union);
            Assert.Equal("", m.CheckPieces());
            // (the single breaks that give this kind of message, spread over the pieces)
            var found = new List<(int, string)>();
            foreach (int k in Enumerable.Range(0, m.PieceCorners.Length).Where(m.IsClassified).Where((_, idx) => idx % 11 == 0)) {
                ExactPoint3 old = Move(m, k, num);
                m.CheckPieces();
                string r = m.CheckEmbedding();
                m.PieceCorners[k][0] = old;
                if (r.StartsWith(startsWith) && found.All(f => Key(f.Item2) != Key(r))) found.Add((k, r));
                if (found.Count >= 4) break;
            }
            Assert.True(found.Count >= 3, $"{found.Count} breaks of this kind");
            var three = new[] { found[0], found[1], found[found.Count - 1] };
            foreach (var (k, _) in three) Move(m, k, num);
            m.CheckPieces();
            string both = m.CheckEmbedding();
            log.WriteLine(string.Join(" | ", three.Select(t => t.Item2)) + "  =>  " + both);
            Assert.Equal(three.Select(t => t.Item2).OrderBy(Key).First(), both);
        }


        // the point k0 a0 + k1 a1 + k2 a2 (the k's in eighths, summing to 8) of three exact points
        static ExactPoint3 Combination(ExactPoint3[] a, int k0, int k1, int k2)
        {
            BigInteger w = a[0].W * a[1].W * a[2].W;
            BigInteger[] k = { k0, k1, k2 };
            BigInteger x = 0, y = 0, z = 0;
            for (int i = 0; i < 3; ++i) {
                BigInteger f = k[i] * (w / a[i].W);
                x += f * a[i].X; y += f * a[i].Y; z += f * a[i].Z;
            }
            return ExactPoint3.Create(x, y, z, 8 * w);
        }

        /// <summary>
        /// A piece whose centroid is on the edge of a coplanar input triangle cannot be told inside or outside it: the stacks refuse it, and
        /// name the piece and the triangle (the lowest piece, the lowest triangle, whatever the order the parallel loops finish in).
        /// </summary>
        [Fact]
        public void ACentroidOnTheEdgeOfACoplanarTriangle_IsRefused()
        {
            var a = FineBox(Vector3d.Zero, 1, 4);
            var b = FineBox(new Vector3d(2, 0.25, 0.125), 1, 5);
            BooleanCertificate c = Run(a, b, Ops.Union);
            Assert.Equal("", c.CheckPieces());
            var candidates = Enumerable.Range(0, c.PieceCorners.Length).Where(i => c.CoplanarSources(c.PieceSource[i]).Any(u => u != c.PieceSource[i])).ToArray();
            int piece = candidates[0], second = candidates[candidates.Length / 2];
            Assert.NotEqual(piece, second);
            foreach (int k in new[] { second, piece }) {
                int src = c.PieceSource[k];
                int u = c.CoplanarSources(src).First(x => x != src);
                ExactPoint3[] g = c.SourceCornersOnGrid(u);
                // corners with the centroid (a0 + a1) / 2 on u's edge a0 a1, the piece not degenerate
                c.PieceCorners[k] = new[] { Combination(g, 2, 6, 0), Combination(g, 4, 2, 2), Combination(g, 6, 4, -2) };
            }
            c.CheckPieces();
            string r = c.CheckEmbedding();
            Assert.Contains("its centroid is on the edge of coplanar triangle", r);
            Assert.StartsWith($"piece {piece}: ", r);
        }

        /// <summary>
        /// The straddle test through the edge forms assumes that the piece is in the plane of its source; a piece that is not (a corner moved
        /// off the plane) takes the direct test, and the two agree for every coplanar input triangle.
        /// </summary>
        [Fact]
        public void APieceOutOfItsSourcePlane_TakesTheDirectStraddleTest()
        {
            var a = FineBox(Vector3d.Zero, 1, 4);
            var b = FineBox(new Vector3d(2, 0.25, 0.125), 1, 5);
            BooleanCertificate c = Run(a, b, Ops.Union);
            Assert.Equal("", c.CheckPieces());
            int tried = 0;
            foreach (int k in Enumerable.Range(0, c.PieceCorners.Length).Where(i => c.CoplanarSources(c.PieceSource[i]).Length > 4).Where((_, i) => i % 40 == 0).Take(6)) {
                ExactPoint3 p = c.PieceCorners[k][0];
                c.PieceCorners[k][0] = ExactPoint3.Create(p.X * 2 + p.W * 3, p.Y * 3 + p.W, p.Z * 5 + p.W * 5, p.W);       // (far off the plane, the piece tilted)
                tried++;
            }
            Assert.True(tried >= 4);
            c.CheckPieces();
            Assert.Equal("", c.AuditInPlaneForms(2000, out int s, out int t, out _));
            Assert.True(t > 100, $"{t} straddle tests");
        }

        /// <summary>
        /// A culled input triangle (the early-out: it meets no triangle of the culling mesh) is not in any stack, even where a piece of another
        /// triangle of its own mesh lies inside it: B has a triangle u in the plane of its top face, over most of it, away from A. The
        /// stacks (one table per group of equal pieces) equal the direct ones, which skip culled triangles.
        /// </summary>
        [Fact]
        public void ACulledTriangleOverlappingAPiece_IsNotInTheStack()
        {
            DMesh3 a = new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, new Vector3d(1, 1, 1)), EdgeVertices = 2 }.Generate().MakeDMesh();
            DMesh3 b = new GridBox3Generator() { Box = new Box3d(new Vector3d(3.5, 0, 0), new Vector3d(3, 1, 1)), EdgeVertices = 2 }.Generate().MakeDMesh();
            int p0 = b.AppendVertex(new Vector3d(1.5, 1, -0.9)), p1 = b.AppendVertex(new Vector3d(6, 1, -0.9)), p2 = b.AppendVertex(new Vector3d(6, 1, 0.9));
            b.AppendTriangle(p0, p2, p1);
            BooleanCertificate c = Run(a, b, Ops.Difference);
            Assert.True(c.TriCulled.Count(x => x) >= 1, "no culled triangle");
            Assert.Equal("", c.CheckPieces());
            Assert.Equal("", c.AuditInPlaneForms(2000, out int stackTests, out _, out _));
            // (a piece of B's top face inside u: the stack of such a piece would hold u if culled triangles were kept)
            int culledU = Enumerable.Range(0, c.TriCulled.Length).First(t => c.TriCulled[t] && c.CoplanarSources(t).Any(x => x != t && c.TriCulled[x] == false));
            Assert.True(Enumerable.Range(0, c.PieceCorners.Length).Any(i => c.CoplanarSources(c.PieceSource[i]).Contains(culledU)), "no piece in the plane of the culled triangle");
        }

        /// <summary>
        /// A piece that straddles the boundary of several coplanar input triangles is reported with the lowest of them (the triangles are
        /// tried in order, the loop over the pieces is parallel): a source triangle put back unsplit on fine faces cut by many others.
        /// </summary>
        [Fact]
        public void APieceStraddlingSeveralTriangles_NamesTheLowest()
        {
            var a = FineBox(Vector3d.Zero, 1, 4);
            var b = FineBox(new Vector3d(2, 0.25, 0.125), 1, 5);
            BooleanCertificate probe = Run(a, b, Ops.Union);
            Assert.Equal("", probe.CheckPieces());
            int checkedSources = 0;
            foreach (int source in probe.PieceSource.Distinct().OrderBy(x => x)) {
                BooleanCertificate c = Run(a, b, Ops.Union);
                int[] keep = Enumerable.Range(0, c.PieceSource.Length).Where(i => c.PieceSource[i] != source).ToArray();
                int mesh = c.PieceMesh[Array.IndexOf(c.PieceSource, source)];
                ExactPoint3[] whole = c.SourceCornersOnGrid(source);
                // (the source whole, as the only piece of its source)
                c.PieceMesh = keep.Select(i => c.PieceMesh[i]).Append(mesh).ToArray();
                c.PieceCorners = keep.Select(i => c.PieceCorners[i]).Append(whole).ToArray();
                c.PieceSource = keep.Select(i => c.PieceSource[i]).Append(source).ToArray();
                int[] straddled = c.CoplanarSources(source).Where(u => u != source && BooleanCertificate.StraddlesInPlane(whole, c.SourceCornersOnGrid(u))).OrderBy(u => u).ToArray();
                if (straddled.Length < 2 || c.CoplanarSources(source).Length <= 8) continue;
                c.CheckPieces();
                int last = c.PieceSource.Length - 1;
                if (c.IsClassified(last) == false) continue;
                // (alone: with the others, the unsplit piece also meets their pieces, which the pair test reports first)
                c.PieceMesh = new[] { c.PieceMesh[last] }; c.PieceCorners = new[] { c.PieceCorners[last] }; c.PieceSource = new[] { c.PieceSource[last] };
                c.CheckPieces();
                string r = c.CheckEmbedding();
                Assert.Equal($"piece 0 (source {source}) straddles the boundary of the coplanar input triangle {straddled[0]}", r);
                if (++checkedSources >= 3) break;
            }
            Assert.True(checkedSources >= 2, $"{checkedSources} sources with a piece straddling several triangles");
        }

        /// <summary>
        /// The scaling of #1632: 120 slices (480 input triangles each, 15878 classified pieces; C2 took 32 s before, 4.8 s after). Nightly: the
        /// boolean, C0 and C1 take a few seconds on their own, and the run is what is checked (the cost, not a verdict).
        /// </summary>
        [Fact]
        [Trait("Category", "Nightly")]
        public void ShiftedFan_120Slices_C2Finishes_Nightly()
        {
            DMesh3 a = Cylinder(120);
            BooleanCertificate c = Run(a, Shifted(a, 2), Ops.Union);
            Assert.Equal("", c.CheckInputs());
            Assert.Equal("", c.CheckPieces());
            var sw = Stopwatch.StartNew();
            Assert.Equal("", c.CheckEmbedding());
            log.WriteLine($"C2 {sw.ElapsedMilliseconds} ms, {c.EmbeddingPairs} pairs");
            Assert.True(sw.ElapsedMilliseconds < 20000, $"C2 {sw.ElapsedMilliseconds} ms");
        }

        // ---------------------------------------------------------------- C5 (#1683)

        /// <summary> the balanced sum of fractions equals the running sum, for every count (0, 1, odd, even, powers of two) and unreduced denominators </summary>
        [Fact]
        public void SumFractions_EqualsRunningSum()
        {
            var rng = new Random(1683);
            for (int k = 0; k <= 37; ++k) {
                var t = new List<(BigInteger n, BigInteger d)>();
                BigInteger rn = BigInteger.Zero, rd = BigInteger.One;
                for (int j = 0; j < k; ++j) {
                    BigInteger n = rng.Next(1, 1000) * BigInteger.Pow(3, rng.Next(0, 40)), d = rng.Next(1, 50) * BigInteger.Pow(2, rng.Next(0, 90));
                    t.Add((n, d));
                    rn = rn * d + n * rd; rd *= d;
                }
                var (sn, sd) = BooleanCertificate.SumFractions(t, 0, t.Count);
                Assert.True(sn * rd == rn * sd, $"{k} terms");
                if (k > 2) {
                    // a sub-range
                    var (pn, pd) = BooleanCertificate.SumFractions(t, 1, k - 1);
                    BigInteger qn = BigInteger.Zero, qd = BigInteger.One;
                    for (int j = 1; j < k - 1; ++j) { qn = qn * t[j].d + t[j].n * qd; qd *= t[j].d; }
                    Assert.True(pn * qd == qn * pd, $"{k} terms, 1..{k - 1}");
                }
            }
        }

        /// <summary>
        /// The cost of C5 on the sliced cylinders (#1683): the area sums of CheckSameChain were 97 % of it (running sums with unreduced
        /// denominators, 4.4 s at 60 slices, 66 s at 120); the verdict is "" and the time of C5 alone stays far below the old one.
        /// </summary>
        [Fact]
        [Trait("Category", "Slow")] // (whole test 3.2 s locally: cylinders, boolean, C0/C1 and C5 at 60 slices; a smaller count does not separate the sums by cost)
        public void ShiftedFan_60Slices_C5IsCheap()
        {
            var total = Stopwatch.StartNew();
#if NETFRAMEWORK
            // (net48: its BigInteger multiplies by the schoolbook method, C5 took 12.7 s at 60 slices there: a smaller case, the verdict only;
            // the cost bound is checked on .NET 10, where the time rule is judged)
            DMesh3 a = Cylinder(30);
#else
            DMesh3 a = Cylinder(60);
#endif
            BooleanCertificate c = Run(a, Shifted(a, 2), Ops.Union);
            Assert.Equal("", c.CheckInputs());
            Assert.Equal("", c.CheckPieces());
            var sw = Stopwatch.StartNew();
            Assert.Equal("", c.CheckResult());
            long c5 = sw.ElapsedMilliseconds;
            // (the count, independent of the speed of the runner: no plane of the fan has both orientations, so no exact area sum is taken)
            Assert.Equal(0, c.AreaPlanesExact);
            Assert.True(c.AreaPlanesSkipped > 0);
            log.WriteLine($"C5 {c5} ms, whole test {total.ElapsedMilliseconds} ms");
            // (the running sums took 4.4 s here and C5 takes 1.0-1.2 s now: the bound is half of the old time, so the quadratic sums fail it
            // on a machine of the same speed and a runner up to 1.8x slower still passes; 120 slices took 66 s before and 5 s now, in the PR)
#if !NETFRAMEWORK
            Assert.True(c5 < 2200, $"C5 {c5} ms (4400 before)");
#endif
        }

        /// <summary> the skip of the exact area sums gives the verdict and the plane counts of the exact path, and takes no exact sum on a fan </summary>
        [Fact]
        public void AreaSkip_Fan_SameVerdict_NoExactSums()
        {
            foreach (int n in new[] { 12, 30 }) {
                DMesh3 a = Cylinder(n), b = Shifted(a, 2);
                BooleanCertificate cs = Run(a, b, Ops.Union), ce = Run(a, b, Ops.Union);
                Assert.Equal("", cs.CheckInputs()); Assert.Equal("", cs.CheckPieces());
                Assert.Equal("", ce.CheckInputs()); Assert.Equal("", ce.CheckPieces());
                ce.ForceExactAreas = true;
                Assert.Equal(ce.CheckResult(), cs.CheckResult());
                Assert.Equal(0, cs.AreaPlanesExact);
                Assert.True(cs.AreaPlanesSkipped > 0);
                Assert.Equal(ce.AreaPlanesExact, cs.AreaPlanesSkipped);
            }
        }
    }
}
