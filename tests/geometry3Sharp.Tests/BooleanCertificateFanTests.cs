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
            BooleanCertificate.Run(a, b, op, out _, x => { if (op == Ops.Intersection) x.CullOutsideA = false; });

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
            DMesh3 a = Cylinder(12);
            BooleanCertificate c = Run(a, Shifted(a, ulps), Ops.Union);
            Assert.Equal("", c.CheckPieces());
            Assert.Equal("", c.AuditInPlaneForms(20000, out int s, out int t, out int p));
            Assert.Equal("", c.CheckEmbedding());
            Assert.True(s > 100 && t > 100 && p > 100, $"{s} / {t} / {p}");
            // (the groups of equal pieces are in use: pieces that are copies of one another)
            Assert.True(c.EmbeddingCopies > 0, "no coplanar copies");
        }

        // ---------------------------------------------------------------- the fan (#1632)

        /// <summary>
        /// The repro of #1632 at a size that the 5 s limit allows: the capped cylinder and its 2-ulp shift certify (every clause, all triangles
        /// covered), and C2 takes a part of the boolean's time, not many times it (0.5 s here against 2.2 s at 30 slices before
        /// the work on its cost; the bound is loose, the cost is in the measurements of the PR).
        /// </summary>
        [Fact]
        public void ShiftedFan_Certifies_AndC2IsBoundedByTheBooleansTime()
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
            Assert.True(c.EmbeddingPairs > 10000);
            // (before: C2 2.2 s against a boolean of 0.8 s; the bound allows a loaded machine and the JIT)
            Assert.True(c2 < Math.Max(3 * boolean, 1500), $"C2 {c2} ms, boolean {boolean} ms");
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
    }
}
