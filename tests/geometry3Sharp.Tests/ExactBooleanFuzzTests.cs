using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Groups = geometry3Sharp.Tests.BooleanFuzzCheck.Groups;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Metamorphic and differential fuzzing of ExactMeshBoolean (and ExactMeshArrangement under it; SolidBooleanPipeline as a
    /// consumer): docs/research/EXACT_BOOLEAN_FUZZ_2026-11.md. Generators: Harness/BooleanFuzzCases.cs (coplanar faces, shared
    /// edges and vertices, offsets of a unit in the last place, tilted thin slabs, nested shells, all of them rotated, open
    /// meshes). Relations: Harness/BooleanFuzzCheck.cs, exact where they can be (SurfaceFingerprint).
    ///
    /// Tiers: the smoke tests run a few seeds per family in the PR tier; the campaigns run more seeds in the Nightly tier.
    /// A failure prints the case as code (FuzzCase.ToCode); it is reduced by hand to a procedural repro, never stored as a file.
    /// To run other seeds locally: FUZZ_FROM=5000 FUZZ_COUNT=2000 dotnet test ... --filter "FullyQualifiedName~ExactBooleanFuzzTests.Campaign".
    /// </summary>
    public class ExactBooleanFuzzTests
    {
        readonly ITestOutputHelper output;
        public ExactBooleanFuzzTests(ITestOutputHelper output) { this.output = output; }

        /// <summary> checks the seeds [from, from + count) of a family; returns the report of the failures ("" if none) </summary>
        string RunSeeds(string family, int from, int count)
        {
            Assert.True(count > 0, "no seeds to run");
            var report = new StringBuilder();
            int failed = 0, booleans = 0, relations = 0, notClean = 0, inexact = 0, uncovered = 0, refused = 0, thinInsideOut = 0, samples = 0;
            int ncIntersections = 0, ncDegenerate = 0, ncContacts = 0, ncDisparity = 0;
            for (int seed = from; seed < from + count; ++seed) {
                FuzzCase c = BooleanFuzzCases.Make(family, seed);
                var check = new BooleanFuzzCheck(c);
                List<string> failures = check.Check();
                booleans += check.Booleans; relations += check.Relations; notClean += check.ReportedNotClean; inexact += check.ComplementInexact;
                uncovered += check.CertificateUncovered; refused += check.PipelineRefused; thinInsideOut += check.ThinInsideOut; samples += check.SamplesUsed;
                ncIntersections += check.NotCleanIntersections; ncDegenerate += check.NotCleanDegenerate; ncContacts += check.NotCleanFalseContacts; ncDisparity += check.NotCleanScaleDisparity;
                if (failures.Count == 0) continue;
                if (++failed > 3) continue;
                report.AppendLine($"{c}: {failures.Count} relations fail");
                foreach (string f in failures.Take(5)) report.AppendLine("  " + f);
                report.AppendLine(c.ToCode());
            }
            output.WriteLine($"{family}: seeds {from}..{from + count - 1}, {failed} failed; {booleans} Booleans, {relations} relations; documented exceptions: " +
                $"{notClean} results reported not clean (intersections {ncIntersections}, degenerate {ncDegenerate}, false contacts {ncContacts}, scale disparity {ncDisparity}), {thinInsideOut} thin inside-out components, {inexact} complement steps not representable, " +
                $"{uncovered} triangles the certificate could not cover, {refused} pipeline refusals; {samples} winding samples");
            return failed == 0 ? "" : $"{failed} of {count} cases of '{family}' fail:\n" + report;
        }

        static int Env(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;

        // ------------------------------------------------------------------ PR tier: a seeded smoke per family

        // (the counts keep each family at about 2 s or less in a loaded run here: the limit is 5 s on the CI runner)
        [Theory]
        [InlineData("coplanar", 8)]
        [InlineData("shared", 16)]
        [InlineData("ulp", 5)]
        [InlineData("slab", 6)]
        [InlineData("shells", 6)]
        [InlineData("generic", 6)]
        [InlineData("rotated", 3)]
        [InlineData("open", 24)]
        public void Smoke(string family, int count)
        {
            string report = RunSeeds(family, 0, count);
            Assert.True(report == "", report);
        }

        // ------------------------------------------------------------------ Nightly: the campaigns

        /// <summary>
        /// Nightly: the run is the check (a fuzzing campaign finds by volume: about 80 Boolean runs per case; 30 to 150 s per
        /// family here, about 10 minutes for all on 4 cores, of the nightly job's 90). The seeds are fixed, so a failure
        /// reproduces; FUZZ_FROM and FUZZ_COUNT run others.
        /// </summary>
        [Theory, Trait("Category", "Nightly")]
        [InlineData("coplanar", 400)]
        [InlineData("shared", 800)]
        [InlineData("ulp", 400)]
        [InlineData("slab", 400)]
        [InlineData("shells", 400)]
        [InlineData("generic", 400)]
        [InlineData("rotated", 250)]
        [InlineData("open", 2000)]
        public void Campaign(string family, int count)
        {
            string report = RunSeeds(family, Env("FUZZ_FROM", 1000), Env("FUZZ_COUNT", count));
            Assert.True(report == "", report);
        }

        // ------------------------------------------------------------------ findings, reduced to code

        /// <summary>
        /// Found by the campaign (ulp #147): the certificate's coverage grid ran out of memory, after 100 s, on an intersection
        /// that is a sliver of 4 triangles, thinner than the certificate's Delta in every direction (a harness defect, in
        /// BooleanCertificate.Cover: the grid was sized by the unpadded extent). Two tetrahedra; a vertex of B lies on an edge
        /// of A but for 3 units in the last place of one coordinate.
        /// </summary>
        [Fact]
        public void Finding_CertificateOfASliverIntersection_Finishes()
        {
            DMesh3 a = BooleanFuzzShapes.Tet(new Vector3d(3, 4, 0), new Vector3d(4, 4, 0), new Vector3d(0, 4, 4), new Vector3d(3, 3, 3));
            double e = 3 * MathUtil.Epsilon;
            DMesh3 b = BooleanFuzzShapes.Tet(new Vector3d(3.5, 4, e), new Vector3d(3, 1, BooleanFuzzShapes.Ulps(2, 3)), new Vector3d(3, 1, BooleanFuzzShapes.Ulps(4, 3)), new Vector3d(3, 2, BooleanFuzzShapes.Ulps(3, 3)));
            BooleanCertificate c = BooleanCertificate.Run(a, b, Ops.Intersection, out ExactMeshBoolean boolean, x => x.CullOutsideA = false);
            Assert.True(boolean.Result.TriangleCount > 0 && boolean.Result.CachedBounds.MaxDim < 1e-12, "the intersection is a sliver: " + boolean.Result.CachedBounds.MaxDim);
            Assert.True(Timed.Finishes(() => Assert.Equal("", c.Check()), TimeSpan.FromSeconds(4)), "the certificate's check did not finish in 4 s of CPU time");
        }

        // ------------------------------------------------------------------ the generators

        [Fact]
        public void Generators_AreDeterministic_AndGiveWhatTheyDeclare()
        {
            foreach (string family in BooleanFuzzCases.ClosedFamilies.Append(BooleanFuzzCases.OpenFamily))
                for (int seed = 0; seed < 40; ++seed) {
                    FuzzCase c = BooleanFuzzCases.Make(family, seed), again = BooleanFuzzCases.Make(family, seed);
                    Assert.True(BooleanFuzzCheck.SameBits(c.A, again.A) == "" && BooleanFuzzCheck.SameBits(c.B, again.B) == "", $"{c}: not deterministic");
                    Assert.Equal(family == BooleanFuzzCases.OpenFamily, c.Open);
                    foreach (var (m, simple, closed) in new[] { (c.A, c.SimpleA, true), (c.B, c.SimpleB, !c.Open) }) {
                        Assert.True(m.TriangleCount > 0 && m.Vertices().All(v => v.IsFinite), c.ToString());
                        Assert.Equal(closed, BooleanFuzzCheck.ClosedOriented(m) == "");
                        // a mesh declared simple bounds a solid: its exact volume is positive
                        if (closed && simple) Assert.True(SurfaceFingerprint.Of(m).SixVolume().Sign > 0, c.ToString());
                    }
                }
            // neighbouring seeds differ: no two of 40 cases of a family are the same pair of meshes
            foreach (string family in BooleanFuzzCases.ClosedFamilies) {
                var seen = new HashSet<string>();
                for (int seed = 0; seed < 40; ++seed) seen.Add(BooleanFuzzCases.Make(family, seed).ToCode().Split(new[] { '\n' }, 2)[1]);
                // (shells draws from 5 x 5 fixed kinds, a fifth of them with a random box: its cases repeat by design)
                Assert.True(seen.Count >= (family == "shells" ? 20 : 36), family + ": only " + seen.Count + " different cases of 40");
            }
        }

        [Fact]
        public void Generators_HitTheHardCasesTheyAreFor()
        {
            int coplanarPairs = 0, sharedVertices = 0, nearCoincident = 0, thin = 0, winding2 = 0;
            for (int seed = 0; seed < 40; ++seed) {
                FuzzCase c = BooleanFuzzCases.Make("coplanar", seed);
                var planes = new HashSet<string>(SurfaceFingerprint.Of(c.A).Planes.Keys);
                if (SurfaceFingerprint.Of(c.B).Planes.Keys.Any(planes.Contains)) ++coplanarPairs;

                FuzzCase s = BooleanFuzzCases.Make("shared", seed);
                var positions = new HashSet<Vector3d>(s.A.Vertices());
                if (s.B.Vertices().Any(positions.Contains)) ++sharedVertices;

                FuzzCase u = BooleanFuzzCases.Make("ulp", seed);
                // a coordinate of B within 4 units in the last place of a coordinate of A, and not equal to it
                var coordinates = u.A.Vertices().SelectMany(v => new[] { v.x, v.y, v.z }).Distinct().ToArray();
                if (u.B.Vertices().SelectMany(v => new[] { v.x, v.y, v.z }).Any(x => coordinates.Any(y => x != y && Math.Abs(x - y) <= 4 * Math.Max(Math.Abs(y), 1) * MathUtil.Epsilon))) ++nearCoincident;

                FuzzCase t = BooleanFuzzCases.Make("slab", seed);
                if (t.B.CachedBounds.Depth < 1e-6) ++thin;

                FuzzCase n = BooleanFuzzCases.Make("shells", seed);
                if (!n.SimpleA || !n.SimpleB) ++winding2;
            }
            output.WriteLine($"of 40: coplanar {coplanarPairs}, shared {sharedVertices}, near-coincident {nearCoincident}, thin slabs {thin}, winding 2 {winding2}");
            Assert.True(coplanarPairs >= 30, "coplanar faces: " + coplanarPairs);
            Assert.True(sharedVertices >= 20, "shared vertices: " + sharedVertices);
            Assert.True(nearCoincident >= 30, "near-coincident coordinates: " + nearCoincident);
            Assert.True(thin >= 15, "thin slabs: " + thin);
            Assert.True(winding2 >= 8, "shells of winding number 2: " + winding2);
        }

        [Fact]
        public void Ulps_MovesByUnitsInTheLastPlace()
        {
            Assert.Equal(1.0, BooleanFuzzShapes.Ulps(1.0, 0));
            Assert.Equal(1.0 + MathUtil.Epsilon, BooleanFuzzShapes.Ulps(1.0, 1));
            Assert.Equal(1.0 - MathUtil.Epsilon / 2, BooleanFuzzShapes.Ulps(1.0, -1));
            Assert.Equal(-(1.0 + 3 * MathUtil.Epsilon), BooleanFuzzShapes.Ulps(-1.0, 3));
            Assert.Equal(-2 * MathUtil.Epsilon, BooleanFuzzShapes.Ulps(0.0, -2));
            Assert.Equal(4.0 + 8 * MathUtil.Epsilon, BooleanFuzzShapes.Ulps(4.0, 2));
        }

        [Fact]
        public void ToCode_RoundTripsTheMeshes()
        {
            // a case with negative coordinates: their bit patterns do not fit a long literal, so the code casts them
            FuzzCase c = BooleanFuzzCases.Make("slab", 0);
            c.A = BooleanFuzzShapes.Map(c.A, p => p - new Vector3d(0.5, 0.25, 7));
            Assert.Contains(c.A.Vertices(), v => v.x < 0 && v.y < 0 && v.z < 0);
            string code = c.ToCode();
            Assert.Contains("BooleanFuzzShapes.FromBits", code);
            Assert.DoesNotContain(" 0x", code);
            Assert.Contains("unchecked((long)0xBF", code);
            // the first array of the code line of A, parsed back
            string line = code.Split('\n').First(l => l.StartsWith("DMesh3 a", StringComparison.Ordinal));
            string[] arrays = line.Split(new[] { "{ ", " }" }, StringSplitOptions.None);
            long[] bits = arrays[1].Split(new[] { ", " }, StringSplitOptions.None)
                .Select(x => { Assert.StartsWith("unchecked((long)0x", x); return Convert.ToInt64(x.Substring(18, 16), 16); }).ToArray();
            int[] triangles = arrays[3].Split(new[] { ", " }, StringSplitOptions.None).Select(int.Parse).ToArray();
            Assert.Equal("", BooleanFuzzCheck.SameBits(new DMesh3(c.A, true), BooleanFuzzShapes.FromBits(bits, triangles)));
        }

        // ------------------------------------------------------------------ the fingerprint

        [Fact]
        public void Fingerprint_IgnoresTheTriangulation_AndSeesTheSurface()
        {
            DMesh3 box = BooleanFuzzShapes.Box(0, 0, 0, 2, 1, 3);
            SurfaceFingerprint f = SurfaceFingerprint.Of(box);
            Assert.Equal(6, f.Planes.Count);
            Assert.Equal(new Rat(36, 1), f.SixVolume());
            Assert.Equal("", SurfaceFingerprint.Difference(f, SurfaceFingerprint.Of(BooleanOracle.Subdivided(box, 2))));
            Assert.Equal("", SurfaceFingerprint.Difference(f, SurfaceFingerprint.Of(Invariance.Permuted(box, 5))));
            // another box, the box reversed, and the box translated (the same volume)
            Assert.NotEqual("", SurfaceFingerprint.Difference(f, SurfaceFingerprint.Of(BooleanFuzzShapes.Box(0, 0, 0, 2, 1, 3.5))));
            Assert.NotEqual("", SurfaceFingerprint.Difference(f, SurfaceFingerprint.Of(BooleanFuzzShapes.Reversed(box))));
            Assert.Equal(new Rat(-36, 1), SurfaceFingerprint.Of(BooleanFuzzShapes.Reversed(box)).SixVolume());
            DMesh3 moved = BooleanFuzzShapes.Map(box, p => p + new Vector3d(0.25, 0, 0));
            SurfaceFingerprint g = SurfaceFingerprint.Of(moved);
            Assert.Equal(f.SixVolume(), g.SixVolume());
            Assert.NotEqual("", SurfaceFingerprint.Difference(f, g));
            // the first moment alone: a triangle and its translate within its plane have the same plane and the same area
            DMesh3 One(double shift)
            {
                var m = new DMesh3();
                m.AppendTriangle(m.AppendVertex(new Vector3d(shift, 0, 1)), m.AppendVertex(new Vector3d(shift + 1, 0, 1)), m.AppendVertex(new Vector3d(shift, 1, 1)));
                return m;
            }
            SurfaceFingerprint t0 = SurfaceFingerprint.Of(One(0)), t1 = SurfaceFingerprint.Of(One(0.5));
            Assert.Equal(t0.Planes.Keys.Single(), t1.Planes.Keys.Single());
            Assert.Equal(t0.Planes.Values.Single()[0], t1.Planes.Values.Single()[0]);
            Assert.NotEqual("", SurfaceFingerprint.Difference(t0, t1));
            Assert.Equal("", SurfaceFingerprint.Difference(t0, SurfaceFingerprint.Of(One(0))));
            // a difference of one unit in the last place of one coordinate shows
            DMesh3 nudged = new DMesh3(box);
            nudged.SetVertex(7, new Vector3d(2, 1, BooleanFuzzShapes.Ulps(3, 1)));
            Assert.NotEqual("", SurfaceFingerprint.Difference(f, SurfaceFingerprint.Of(nudged)));
        }

        [Fact]
        public void Fingerprint_IsAdditive_AndCancelsTwoSidedSheets()
        {
            // two boxes that share a face: the sum of their boundaries is the boundary of their union (the shared face cancels)
            SurfaceFingerprint left = SurfaceFingerprint.Of(BooleanFuzzShapes.Box(0, 0, 0, 1, 1, 1)), right = SurfaceFingerprint.Of(BooleanFuzzShapes.Box(1, 0, 0, 3, 1, 1));
            SurfaceFingerprint whole = SurfaceFingerprint.Of(BooleanFuzzShapes.Box(0, 0, 0, 3, 1, 1));
            Assert.Equal("", SurfaceFingerprint.Difference(left.Plus(right), whole));
            Assert.Equal("", SurfaceFingerprint.Difference(whole.Minus(right), left));
            Assert.NotEqual("", SurfaceFingerprint.Difference(whole.Minus(left), left));
            // a mesh and its reverse: nothing is left
            DMesh3 tet = BooleanFuzzShapes.Tet(new Vector3d(0.1, 0.2, 0.3), new Vector3d(1.7, 0.1, 0), new Vector3d(0, 2.3, 0.2), new Vector3d(0.3, 0.1, 1.9));
            Assert.True(SurfaceFingerprint.Of(BooleanFuzzShapes.Join(tet, BooleanFuzzShapes.Reversed(tet))).IsEmpty);
            Assert.True(SurfaceFingerprint.Of(tet).SixVolume().Sign > 0);
            // exact vertices give the same fingerprint as the doubles they are
            ExactPoint3[] exact = tet.VertexIndices().Select(v => ExactOf(tet.GetVertex(v))).ToArray();
            Assert.Equal("", SurfaceFingerprint.Difference(SurfaceFingerprint.Of(tet, exact), SurfaceFingerprint.Of(tet)));
        }

        static ExactPoint3 ExactOf(Vector3d v)
        {
            Rat x = Rats.Of(v.x), y = Rats.Of(v.y), z = Rats.Of(v.z);
            BigInteger w = x.D * y.D * z.D;
            return ExactPoint3.Create(x.N * (w / x.D), y.N * (w / y.D), z.N * (w / z.D), w);
        }

        [Fact]
        public void Rats_AreExact()
        {
            Assert.Equal(new Rat(1, 10), new Rat(3, 30));
            Assert.Equal(new Rat(3, 8), Rats.Of(0.375));
            Assert.Equal(new Rat(-5, 1), Rats.Of(-5.0));
            Assert.NotEqual(new Rat(1, 10), Rats.Of(0.1));
            Assert.Equal(0.1, Rats.Of(0.1).ToDouble());
            Assert.Equal(BigInteger.Pow(2, 1074), Rats.Of(double.Epsilon).D);
            Assert.True(Rats.Of(0.0).IsZero() && !Rats.Of(1e-300).IsZero());
            Assert.Equal(new Rat(1, 3), new Rat(1, 2) - new Rat(1, 6));
        }

        // ------------------------------------------------------------------ the checker: it accepts good runs and sees wrong ones

        static FuzzCase Boxes() => new FuzzCase {
            Family = "test", A = BooleanFuzzShapes.Box(0, 0, 0, 2, 2, 2), B = BooleanFuzzShapes.Box(1, 1, 0, 3, 3, 2), Description = "two boxes with coplanar faces" };

        [Fact]
        public void Checker_AcceptsACase_AndCountsItsWork()
        {
            var check = new BooleanFuzzCheck(Boxes());
            Assert.Empty(check.Check());
            Assert.True(check.Booleans >= 60 && check.Relations >= 100, $"{check.Booleans} Booleans, {check.Relations} relations");
            Assert.True(check.SamplesUsed > 50, "winding samples used: " + check.SamplesUsed);
            Assert.Equal(0, check.ComplementInexact);
        }

        static void MoveVertex(ExactMeshBoolean b, Vector3d by)
        {
            int v = b.Result.VertexIndices().First();
            Vector3d p = b.Result.GetVertex(v) + by;
            b.Result.SetVertex(v, p);
            if (b.ResultExactVertices != null) b.ResultExactVertices[v] = ExactOf(p);
        }

        /// <summary>
        /// Mutation check of the checker: a wrong result of one run (a moved vertex, a reversed surface, a missing triangle,
        /// an empty result) makes the relations of its group fail, and only a faulty run does.
        /// </summary>
        [Theory]
        [InlineData("A Union B", "move", Groups.Algebra, "boundary(A u B) + boundary(A n B)")]
        [InlineData("A Intersection B", "move", Groups.Algebra, "A n B = B n A")]
        [InlineData("A Difference B", "move", Groups.Algebra, "boundary(A - B) = boundary(A) - boundary(A n B)")]
        [InlineData("B Union A", "move", Groups.Algebra, "A u B = B u A")]
        [InlineData("B Difference A", "reverse", Groups.Algebra, "boundary(B - A)")]
        [InlineData("A Union A", "move", Groups.Algebra, "A u A = A")]
        [InlineData("A Intersection A", "move", Groups.Algebra, "A n A = A u A")]
        [InlineData("A Difference A", "box", Groups.Algebra, "A - A is empty")]
        [InlineData("B Intersection B", "move", Groups.Algebra, "B n B = B u B")]
        [InlineData("B Union B", "move", Groups.Algebra, "B u B = B")]
        [InlineData("A Intersection B", "reverse", Groups.Algebra, "volumes are ordered")]
        [InlineData("A Union B", "winding", Groups.None, "exact winding numbers")]
        [InlineData("A Union B", "open", Groups.None, "exact result")]
        [InlineData("A Union B", "inexact", Groups.None, "exact vertices")]
        [InlineData("Union reordered", "move", Groups.Representation, "reordered")]
        [InlineData("Intersection seed", "move", Groups.Representation, "with Seed")]
        [InlineData("Difference salt", "move", Groups.Representation, "hash salt")]
        [InlineData("Union no shortcuts", "move", Groups.Representation, "pre-decision")]
        [InlineData("Union not simplified", "move", Groups.Representation, "SimplifyFlatRegions")]
        [InlineData("Difference predicate", "move", Groups.Representation, "winding predicate")]
        [InlineData("Intersection repeated", "empty", Groups.Representation, "repeated operand")]
        [InlineData("universe predicate", "move", Groups.Complement, "U as a third operand")]
        [InlineData("A n (U - B)", "move", Groups.Complement, "A n (U - B) = A - B")]
        [InlineData("(U - A) n (U - B)", "move", Groups.Complement, "(U - A) n (U - B) = U - (A u B)")]
        [InlineData("U - ((U - A) n (U - B))", "move", Groups.Complement, "= A u B")]
        [InlineData("default Union", "open", Groups.DefaultMode, "is a closed surface")]
        [InlineData("default Union", "open", Groups.DefaultMode, "valid, closed DMesh3")]
        [InlineData("default Union", "cross", Groups.DefaultMode, "clean where RoundingClean says so")]
        [InlineData("default Intersection", "far", Groups.DefaultMode, "within the rounding bound")]
        [InlineData("default Difference scaled 2^10", "move", Groups.DefaultMode, "bit for bit")]
        [InlineData("default Union again", "move", Groups.DefaultMode, "second Compute()")]
        [InlineData("default Union", "reverse", Groups.Differential, "winding numbers")]
        [InlineData("default Difference", "empty", Groups.Differential, "winding numbers")]
        [InlineData("A Union B, rotated", "move", Groups.Similarities, "inclusion-exclusion, rotated")]
        [InlineData("A Intersection B, utm", "far", Groups.Similarities, "volume, utm")]
        [InlineData("default, rotated", "open", Groups.Similarities, "rotated is a closed surface")]
        [InlineData("default, scale 1e3", "cross", Groups.Similarities, "scale 1e3 is clean where RoundingClean says so")]
        public void Checker_SeesAWrongResult(string run, string fault, Groups group, string expected)
        {
            var check = new BooleanFuzzCheck(Boxes()) { Run = group };
            int tampered = 0;
            check.Tamper = (name, b) => {
                if (name != run) return;
                ++tampered;
                switch (fault) {
                    case "move": MoveVertex(b, new Vector3d(0.125, 0, 0)); break;
                    case "far": foreach (int v in b.Result.VertexIndices().ToList()) { Vector3d p = b.Result.GetVertex(v); p.x *= 1.5; b.Result.SetVertex(v, p); if (b.ResultExactVertices != null) b.ResultExactVertices[v] = ExactOf(p); } break;
                    case "inexact": { int v = b.Result.VertexIndices().First(); b.Result.SetVertex(v, b.Result.GetVertex(v) + new Vector3d(1e-9, 0, 0)); break; }
                    case "reverse": b.Result = BooleanFuzzShapes.Reversed(b.Result); break;
                    case "winding": b.ExactWinding[1] = false; break;
                    case "cross": { Vector3d c0 = b.Result.CachedBounds.Min, d = b.Result.CachedBounds.Diagonal; b.Result = BooleanFuzzShapes.Join(b.Result, BooleanFuzzShapes.Box(c0 - 0.1 * d, c0 + 0.2 * d)); break; }
                    case "open": b.Result.RemoveTriangle(b.Result.TriangleIndices().First(), false, false); break;
                    case "empty": b.Result = new DMesh3(); b.ResultExactVertices = new ExactPoint3[0]; break;
                    case "box": b.Result = BooleanFuzzShapes.Box(0, 0, 0, 1, 1, 1); b.ResultExactVertices = b.Result.VertexIndices().Select(v => ExactOf(b.Result.GetVertex(v))).ToArray(); break;
                    default: throw new ArgumentException(fault);
                }
            };
            List<string> failures = check.Check();
            Assert.True(tampered >= 1, "the run '" + run + "' did not happen");
            Assert.True(failures.Any(f => f.Contains(expected)), "expected a failure with '" + expected + "', got: " + string.Join(" | ", failures));

        }

        [Fact]
        public void Checker_WithoutAFault_EveryGroupHolds()
        {
            foreach (Groups group in new[] { Groups.None, Groups.Algebra, Groups.Representation, Groups.Complement, Groups.DefaultMode, Groups.Differential, Groups.Similarities })
                Assert.Empty(new BooleanFuzzCheck(Boxes()) { Run = group }.Check());
        }

        /// <summary> the pipeline relations: a wrong pipeline result (open, or another solid) fails them </summary>
        [Theory]
        [InlineData("pipeline Union", "open", "pipeline Union is a closed surface")]
        [InlineData("pipeline Difference", "box", "pipeline Difference has the volume of the Boolean")]
        public void Checker_SeesAWrongPipelineResult(string run, string fault, string expected)
        {
            var check = new BooleanFuzzCheck(Boxes()) { Run = Groups.Differential };
            int tampered = 0;
            check.TamperPipeline = (name, p) => {
                if (name != run) return;
                ++tampered;
                if (fault == "open") p.Result.RemoveTriangle(p.Result.TriangleIndices().First(), false, false);
                else p.Result = BooleanFuzzShapes.Box(0, 0, 0, 3, 3, 2);
            };
            List<string> failures = check.Check();
            Assert.Equal(1, tampered);
            Assert.True(failures.Any(f => f.Contains(expected)), "expected '" + expected + "', got: " + string.Join(" | ", failures));
        }

        /// <summary> the relations of the open case, each with a fault in the run it reads </summary>
        [Theory]
        [InlineData("A Union B", "nan", "is finite and valid")]
        [InlineData("B Difference A", "winding", "reports the open input")]
        [InlineData("A Intersection B", "windingBoth", "reports the open input")]
        [InlineData("A Difference B again", "move", "second Compute()")]
        [InlineData("B Union A scaled", "move", "bit for bit")]
        [InlineData("A Difference B seed", "move", "is the same surface")]
        public void Checker_OpenCase_SeesAWrongResult(string run, string fault, string expected)
        {
            // a box and a box without a face that cross: every operation has a result with triangles
            var c = new FuzzCase { Family = "open", Open = true, A = BooleanFuzzShapes.Box(0, 0, 0, 2, 2, 2), B = BooleanFuzzShapes.Without(BooleanFuzzShapes.Box(1, 1, 1, 3, 3, 3), 2, 3) };
            Assert.Empty(new BooleanFuzzCheck(c).Check());
            var check = new BooleanFuzzCheck(c);
            int tampered = 0;
            check.Tamper = (name, b) => {
                if (name != run) return;
                ++tampered;
                Assert.True(b.Result.VertexCount > 0, "the run '" + run + "' has an empty result");
                switch (fault) {
                    case "move": MoveVertex(b, new Vector3d(0.125, 0, 0)); break;
                    case "nan": b.Result.SetVertex(b.Result.VertexIndices().First(), new Vector3d(double.NaN, 0, 0)); break;
                    // the open input reported as exact, and the closed one as open
                    case "winding": Array.Reverse(b.ExactWinding); break;
                    case "windingBoth": b.ExactWinding[0] = b.ExactWinding[1] = false; break;
                    default: throw new ArgumentException(fault);
                }
            };
            List<string> failures = check.Check();
            Assert.Equal(1, tampered);
            Assert.True(failures.Any(f => f.Contains(run.Split(' ')[0] + " " + run.Split(' ')[1]) && f.Contains(expected)), "expected '" + expected + "', got: " + string.Join(" | ", failures));
        }

        [Fact]
        public void Checker_SeesAFailedRun()
        {
            // a non-finite coordinate: Compute() fails, and the checker reports the run instead of going on
            FuzzCase c = Boxes();
            c.B.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            List<string> failures = new BooleanFuzzCheck(c).Check();
            Assert.Contains(failures, f => f.Contains("status InvalidInput"));
        }

        [Fact]
        public void Checker_OpenCase_HoldsAndSeesADifferentScaledResult()
        {
            FuzzCase c = BooleanFuzzCases.Make("open", 1);
            var check = new BooleanFuzzCheck(c);
            Assert.Empty(check.Check());
            Assert.True(check.Booleans >= 24 && check.Relations >= 30, $"{check.Booleans} Booleans, {check.Relations} relations");
            // SameBits, the comparison behind the scale and reuse relations
            DMesh3 box = BooleanFuzzShapes.Box(0, 0, 0, 1, 1, 1), scaled = BooleanFuzzShapes.Map(box, p => p * 1024);
            Assert.Equal("", BooleanFuzzCheck.SameBits(box, scaled, 1024));
            Assert.NotEqual("", BooleanFuzzCheck.SameBits(box, scaled, 512));
            Assert.NotEqual("", BooleanFuzzCheck.SameBits(box, BooleanFuzzShapes.Reversed(box)));
            Assert.NotEqual("", BooleanFuzzCheck.SameBits(box, BooleanFuzzShapes.Without(box, 3)));
        }

        /// <summary>
        /// The one exemption of the clean check (a thin inside-out component, MeshRoundingRepair #1067) is as narrow as the rule:
        /// a reversed plate thinner than the rounding's bound passes and is counted, the same plate a little thicker than
        /// the bound fails, and so do a reversed box and any other defect next to a thin plate.
        /// </summary>
        [Fact]
        public void CleanCheck_ExemptsOnlyThinInsideOutComponents()
        {
            DMesh3 box = BooleanFuzzShapes.Box(0, 0, 0, 4, 4, 4);
            double diag = BooleanFuzzShapes.Join(box, BooleanFuzzShapes.Box(6, 0, 0, 8, 2, 1)).CachedBounds.DiagonalLength, bound = 1e-5 * diag;
            DMesh3 Plate(double thickness) => BooleanFuzzShapes.Reversed(BooleanFuzzShapes.Box(6, 0, 0, 8, 2, thickness));
            Assert.Equal("", BooleanFuzzCheck.CleanExceptThinInsideOut(box, 1e-5, 256, false, out int thin));
            Assert.Equal(0, thin);
            // thin by the bound: 2 |V| / area is about the thickness (the plate has two large faces)
            Assert.Equal("", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(box, Plate(0.5 * bound)), 1e-5, 256, true, out thin));
            Assert.Equal(1, thin);
            Assert.Contains("not thin", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(box, Plate(4 * bound)), 1e-5, 256, true, out thin));
            Assert.Contains("not thin", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(box, Plate(1)), 1e-5, 256, true, out thin));
            Assert.Contains("not thin", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Reversed(box), 1e-5, 256, true, out thin));
            // another defect next to a thin plate is still reported: an open box, and a box that crosses the first
            Assert.NotEqual("", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(BooleanFuzzShapes.Without(box, 0), Plate(0.5 * bound)), 1e-5, 256, true, out thin));
            Assert.Contains("self-intersections", BooleanFuzzCheck.CleanExceptThinInsideOut(
                BooleanFuzzShapes.Join(box, BooleanFuzzShapes.Box(3, 1, 1, 5, 2, 2), Plate(0.5 * bound)), 1e-5, 256, true, out thin));
            // without the allowance for slivers, the thin plate's triangles are not degenerate (they are large), a needle is
            DMesh3 needle = BooleanFuzzShapes.Tet(new Vector3d(6, 0, 0), new Vector3d(8, 0, 0), new Vector3d(7, 1e-12, 0), new Vector3d(7, 0, 1e-12));
            Assert.Contains("degenerate", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(box, needle), 1e-5, 256, false, out thin));
            Assert.Equal("", BooleanFuzzCheck.CleanExceptThinInsideOut(BooleanFuzzShapes.Join(box, needle), 1e-5, 256, true, out thin));
        }

        [Fact]
        public void PipelineRefusals_OnlyTheDocumentedOnesPass()
        {
            foreach (gs.SolidBooleanPipeline.PipelineStatus status in Enum.GetValues(typeof(gs.SolidBooleanPipeline.PipelineStatus)))
                foreach (ExactMeshBoolean.BooleanStatus b in Enum.GetValues(typeof(ExactMeshBoolean.BooleanStatus))) {
                    bool refusal = status == gs.SolidBooleanPipeline.PipelineStatus.ThinInput || status == gs.SolidBooleanPipeline.PipelineStatus.InsideOutInput
                        || status == gs.SolidBooleanPipeline.PipelineStatus.InconsistentOrientation;
                    bool strict = status == gs.SolidBooleanPipeline.PipelineStatus.BooleanFailed && b == ExactMeshBoolean.BooleanStatus.RoundingNotClean;
                    Assert.Equal(refusal || strict, BooleanFuzzCheck.PipelineRefusalIsDocumented(status, b));
                }
            Assert.False(BooleanFuzzCheck.PipelineRefusalIsDocumented(gs.SolidBooleanPipeline.PipelineStatus.Success, ExactMeshBoolean.BooleanStatus.Success));
            Assert.False(BooleanFuzzCheck.PipelineRefusalIsDocumented(gs.SolidBooleanPipeline.PipelineStatus.BooleanFailed, ExactMeshBoolean.BooleanStatus.BudgetExceeded));
            Assert.False(BooleanFuzzCheck.PipelineRefusalIsDocumented(gs.SolidBooleanPipeline.PipelineStatus.OpenInput, ExactMeshBoolean.BooleanStatus.NotComputed));
        }

        [Fact]
        public void ClosedOriented_SeesHolesFlipsAndDoubledFaces()
        {
            DMesh3 box = BooleanFuzzShapes.Box(0, 0, 0, 1, 1, 1);
            Assert.Equal("", BooleanFuzzCheck.ClosedOriented(box));
            Assert.Equal("", BooleanFuzzCheck.ClosedOriented(new DMesh3()));
            Assert.Contains("no opposite", BooleanFuzzCheck.ClosedOriented(BooleanFuzzShapes.Without(box, 0)));
            var flipped = new DMesh3(box);
            flipped.ReverseTriOrientation(4);
            Assert.Contains("twice", BooleanFuzzCheck.ClosedOriented(flipped));
            var nan = new DMesh3(box);
            nan.SetVertex(2, new Vector3d(0, double.PositiveInfinity, 0));
            Assert.Contains("not finite", BooleanFuzzCheck.ClosedOriented(nan));
        }
    }
}
