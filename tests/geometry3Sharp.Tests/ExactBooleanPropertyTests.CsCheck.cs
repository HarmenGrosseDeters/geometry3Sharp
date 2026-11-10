using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CsCheck;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Rep = geometry3Sharp.Tests.BooleanOracle.Representation;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Property-based tests of ExactMeshBoolean with CsCheck (Apache-2.0, net10.0 only; the file name ends in
    /// .CsCheck.cs, which the project compiles only there). Cases are printable descriptions (boxes or tetrahedra
    /// with integer corners, a 2^k scale, a dyadic translation, a representation change), built into fresh meshes
    /// per run and checked against the oracles of BooleanOracle; the fixed corpus is ExactBooleanMetamorphicTests.
    ///
    /// Campaign: Samples cases, each from its own seed derived from a fixed campaign seed (CsCheck's own sampling
    /// draws random seeds after the first one). A failing seed is shrunk deterministically (Shrink: ShrinkIterations
    /// candidates from seeds derived from the failing seed, CsCheck's size order) and reported with the original and
    /// the shrunk case and seed; the same failing seed always gives the same shrunk seed and shrink count. Opt-in:
    ///   G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~ExactBooleanPropertyTests
    ///     a larger campaign (any number of samples per property, from the same campaign seeds);
    ///   G3_PROPERTY_CAMPAIGN="Boxes(general)" G3_PROPERTY_REPLAY=&lt;seed&gt; dotnet test ... --filter "FullyQualifiedName~ExactBooleanPropertyTests.Boxes"
    ///     one case of the named campaign, from a seed a failure reported (original or shrunk); the failure message
    ///     prints this command. Other campaigns run as usual.
    /// </summary>
    public class ExactBooleanPropertyTests
    {
        readonly ITestOutputHelper log;
        public ExactBooleanPropertyTests(ITestOutputHelper log) { this.log = log; }

        const int ShrinkIterations = 400;

        // ---------------------------------------------------------------- generators

        // a box with integer corners in [0, range]^3: small ranges give many shared planes, edges and vertices
        static Gen<IntBox> GenBox(int range) =>
            Gen.Select(GenInterval(range), GenInterval(range), GenInterval(range),
                (x, y, z) => new IntBox(x.lo, y.lo, z.lo, x.hi, y.hi, z.hi));

        static Gen<(int lo, int hi)> GenInterval(int range) =>
            Gen.Int[0, range - 1].SelectMany(lo => Gen.Int[1, range - lo].Select(ext => (lo, lo + ext)));

        // 2^k with |k| <= 8, and a translation on a 1/16 grid up to 2^12 (or none): exact world coordinates
        static readonly Gen<(int k, Vector3d t)> GenFrame =
            Gen.Select(Gen.Int[-8, 8], Gen.Bool, Gen.Int[-65536, 65536], Gen.Int[-65536, 65536], Gen.Int[-65536, 65536],
                (k, move, x, y, z) => (k, move ? new Vector3d(x / 16.0, y / 16.0, z / 16.0) : Vector3d.Zero));

        static Gen<BoxCase> GenCase(Gen<(IntBox a, IntBox b)> pair) =>
            Gen.Select(pair, GenFrame, (p, f) => new BoxCase(p.a, p.b, f.k, f.t));

        /// <summary> the box families of docs/design/BOOLEAN_PROPERTY_TESTS.md section 1, each its own campaign </summary>
        static Gen<(IntBox, IntBox)> GenFamily(string family)
        {
            switch (family) {
                case "general":
                    return Gen.Select(GenBox(4), GenBox(4));
                case "coplanar":
                    // B starts or ends on one of A's planes
                    return Gen.Select(GenBox(4), GenBox(4), Gen.Int[0, 2], Gen.Bool, (a, b, axis, atEnd) => (a, Snap(a, b, axis, atEnd)));
                case "touching":
                    // B starts where A ends on 1 to 3 axes (face, edge or vertex contact, or disjoint)
                    return Gen.Select(GenBox(3), GenBox(3), Gen.Int[1, 7], (a, b, axes) => (a, Touch(a, b, axes)));
                case "nested":
                    // B inside A, possibly on its walls or identical
                    return GenBox(5).SelectMany(a => Gen.Select(GenInterval(a.X1 - a.X0), GenInterval(a.Y1 - a.Y0), GenInterval(a.Z1 - a.Z0),
                        (x, y, z) => (a, new IntBox(a.X0 + x.lo, a.Y0 + y.lo, a.Z0 + z.lo, a.X0 + x.hi, a.Y0 + y.hi, a.Z0 + z.hi))));
                default: throw new ArgumentException(family);
            }
        }

        // Each relation is constructive, with positive bounded dimensions. Shrinking
        // coordinates, dimensions, gap, axis or frame cannot leave the chosen family.
        static Gen<(IntBox, IntBox)> GenDirectedFamily(string family) =>
            Gen.Select(GenBox(2), Gen.Int[1, 2], Gen.Int[0, 2], (box, gap, axis) => {
                int[] lo = { box.X0, box.Y0, box.Z0 }, hi = { box.X1, box.Y1, box.Z1 };
                int[] blo = (int[])lo.Clone(), bhi = (int[])hi.Clone();
                switch (family) {
                    case "disjoint":
                        blo[axis] = hi[axis] + gap;
                        bhi[axis] = blo[axis] + hi[axis] - lo[axis];
                        break;
                    case "identical": break;
                    case "strict_nested":
                        for (int j = 0; j < 3; j++) { lo[j]--; hi[j]++; }
                        break;
                    case "overlapping":
                    case "coplanar_overlap":
                        for (int j = 0; j < 3; j++) {
                            int width = hi[j] - lo[j];
                            hi[j] += width;
                            blo[j] += family == "coplanar_overlap" && j == axis ? 0 : width;
                            bhi[j] = blo[j] + 2 * width;
                        }
                        break;
                    case "face_touching":
                    case "edge_touching":
                    case "vertex_touching":
                        int dimensions = family == "face_touching" ? 1 : family == "edge_touching" ? 2 : 3;
                        for (int j = 0; j < dimensions; j++) {
                            int a = (axis + j) % 3;
                            blo[a] = hi[a]; bhi[a] = hi[a] + hi[a] - lo[a];
                        }
                        break;
                    default: throw new ArgumentException(family);
                }
                return (new IntBox(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]),
                        new IntBox(blo[0], blo[1], blo[2], bhi[0], bhi[1], bhi[2]));
            });

        // Independent interval predicates, used on initial cases and every shrink.
        static string DirectedFamilyReason(string family, BoxCase c)
        {
            int[] overlap = Enumerable.Range(0, 3).Select(j => Math.Min(c.A.Hi(j), c.B.Hi(j)) - Math.Max(c.A.Lo(j), c.B.Lo(j))).ToArray();
            bool identical = Enumerable.Range(0, 3).All(j => c.A.Lo(j) == c.B.Lo(j) && c.A.Hi(j) == c.B.Hi(j));
            bool containsA = Enumerable.Range(0, 3).All(j => c.A.Lo(j) <= c.B.Lo(j) && c.A.Hi(j) >= c.B.Hi(j));
            bool containsB = Enumerable.Range(0, 3).All(j => c.B.Lo(j) <= c.A.Lo(j) && c.B.Hi(j) >= c.A.Hi(j));
            bool valid;
            switch (family) {
                case "disjoint": valid = overlap.Any(x => x < 0); break;
                case "identical": valid = identical; break;
                case "strict_nested": valid = Enumerable.Range(0, 3).All(j => c.A.Lo(j) < c.B.Lo(j) && c.A.Hi(j) > c.B.Hi(j)); break;
                // Generic partial overlap: no shared boundary plane, which keeps it apart from coplanar_overlap.
                case "overlapping": valid = overlap.All(x => x > 0) && !containsA && !containsB &&
                    Enumerable.Range(0, 3).All(j => c.A.Lo(j) != c.B.Lo(j) && c.A.Hi(j) != c.B.Hi(j)); break;
                case "coplanar_overlap": valid = overlap.All(x => x > 0) && !containsA && !containsB &&
                    Enumerable.Range(0, 3).Any(j => c.A.Lo(j) == c.B.Lo(j) && c.A.Hi(j) == c.B.Hi(j)); break;
                default:
                    int count = family == "face_touching" ? 1 : family == "edge_touching" ? 2 : family == "vertex_touching" ? 3 : -1;
                    valid = count >= 0 && overlap.All(x => x >= 0) && overlap.Count(x => x == 0) == count;
                    break;
            }
            return valid ? "" : "generator left " + family + ": " + c;
        }

        static IntBox Snap(IntBox a, IntBox b, int axis, bool atEnd)
        {
            int[] lo = { b.X0, b.Y0, b.Z0 }, hi = { b.X1, b.Y1, b.Z1 };
            int ext = hi[axis] - lo[axis];
            if (atEnd) { hi[axis] = a.Hi(axis); lo[axis] = hi[axis] - ext; }
            else { lo[axis] = a.Lo(axis); hi[axis] = lo[axis] + ext; }
            return new IntBox(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]);
        }

        static IntBox Touch(IntBox a, IntBox b, int axes)
        {
            int[] lo = { b.X0, b.Y0, b.Z0 }, hi = { b.X1, b.Y1, b.Z1 };
            for (int axis = 0; axis < 3; ++axis)
                if ((axes >> axis & 1) != 0) { int ext = hi[axis] - lo[axis]; lo[axis] = a.Hi(axis); hi[axis] = lo[axis] + ext; }
            return new IntBox(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]);
        }

        static Gen<Vector3i> GenPoint(int range) => Gen.Select(Gen.Int[0, range], Gen.Int[0, range], Gen.Int[0, range], (x, y, z) => new Vector3i(x, y, z));

        static Gen<IntTet> GenTet(Gen<Vector3i> point) =>
            Gen.Select(point, point, point, point).Where(p => IntTet.Det(p.Item1, p.Item2, p.Item3, p.Item4) != 0)
                .Select(p => new IntTet(p.Item1, p.Item2, p.Item3, p.Item4));

        /// <summary>
        /// Two tetrahedra in [0,6]^3; each corner of B is either free or one of A's corners (shared vertices, edges
        /// and faces), and sliver tetrahedra come up whenever four corners are nearly coplanar. Translated by 0 or by
        /// UTM-like coordinates (dyadic, exact).
        /// </summary>
        static readonly Gen<(IntTet a, IntTet b, Vector3d shift)> GenTets =
            GenTet(GenPoint(6)).SelectMany(a => {
                Vector3i[] corners = { a.P0, a.P1, a.P2, a.P3 };
                Gen<Vector3i> corner = Gen.Int[0, 7].SelectMany(k => k < 4 ? Gen.Const(corners[k]) : GenPoint(6));
                return Gen.Select(GenTet(corner), Gen.Bool, (b, far) => (a, b, far ? new Vector3d(632117.25, 5712043.5, 113.75) : Vector3d.Zero));
            });

        // ---------------------------------------------------------------- campaign

        static int Samples(int normal)
        {
            string env = Environment.GetEnvironmentVariable("G3_PROPERTY_SAMPLES");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : normal;
        }

        /// <summary>
        /// Runs the property on `samples` cases, case i from seed (campaign, i): reproducible, and independent of
        /// the other cases. property returns "" or the reason it fails. A failure is shrunk and reported.
        /// </summary>
        void Campaign<T>(string name, Gen<T> gen, Func<T, string> property, int samples, uint campaign)
        {
            void Check(T t) { string r = property(t); if (r != "") throw new Exception(r); }
            var watch = Stopwatch.StartNew();
            string replay = Environment.GetEnvironmentVariable("G3_PROPERTY_CAMPAIGN") == name ? Environment.GetEnvironmentVariable("G3_PROPERTY_REPLAY") : null;
            string[] seeds = string.IsNullOrEmpty(replay)
                ? Enumerable.Range(0, Samples(samples)).Select(i => new PCG(campaign, (ulong)i).ToString()).ToArray()
                : new[] { replay };
            log.WriteLine($"{name}: CsCheck campaign {campaign}, {seeds.Length} samples");
            foreach (string seed in seeds) {
                string failure = null;
                T original = default(T);
                try { gen.Sample(t => { original = t; Check(t); }, seed: seed, iter: 1, threads: 1, print: t => t.ToString()); }
                catch (CsCheckException e) { failure = e.InnerException?.Message ?? e.Message; }
                if (failure == null) continue;
                string shrunk = Shrink(gen, property, seed, ShrinkIterations).Report;
                throw new Xunit.Sdk.XunitException($"{name} fails for seed {seed} (campaign {campaign}):\n  {original}\n  {failure}\n" +
                    $"Shrunk ({ShrinkIterations} tries, not necessarily minimal): {shrunk}\n" +
                    $"Replay: G3_PROPERTY_CAMPAIGN=\"{name}\" G3_PROPERTY_REPLAY=<seed> dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter \"FullyQualifiedName~ExactBooleanPropertyTests.{name.Split('(')[0]}\"");
            }
            log.WriteLine($"{name}: passed in {watch.ElapsedMilliseconds} ms");
        }

        /// <summary>
        /// Deterministic shrinking of a failing seed. CsCheck's Sample(seed, iter) runs the seed first and draws every
        /// further iteration from PCG.ThreadPCG, which is seeded differently in each process and shared by every test
        /// on the thread, so the shrunk seed and the shrink count changed from run to run. Here candidate k comes from
        /// a PCG whose seed is a SplitMix64 hash of (failing seed, stream, k) only. As in CsCheck, a candidate that is
        /// not smaller than the current best by CsCheck's size order is skipped unevaluated; a smaller one that fails
        /// (a non-empty reason or an exception) becomes the new best, one shrink. The reported seed replays the
        /// shrunk case through gen.Sample(seed: ..., iter: 1).
        /// </summary>
        internal static (string Seed, T Value, int Shrinks, int Skipped, string Report) Shrink<T>(Gen<T> gen, Func<T, string> property,
            string seed, int iterations)
        {
            string Fails(T t)
            {
                try { return property(t); }
                catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
            }
            PCG root = PCG.Parse(seed);
            string bestSeed = seed;
            T best = gen.Generate(PCG.Parse(seed), null, out Size bestSize);
            string bestReason = Fails(best);
            int shrinks = 0, skipped = 0;
            for (int k = 0; k < iterations; ++k) {
                var pcg = new PCG(root.Stream, SplitMix64(root.Seed ^ ((ulong)root.Stream << 32) ^ SplitMix64((ulong)k + 1)));
                string candidateSeed = pcg.ToString();
                T candidate = gen.Generate(pcg, bestSize, out Size size);
                if (!Size.IsLessThan(size, bestSize)) { skipped++; continue; }
                string reason = Fails(candidate);
                if (reason == "") continue;
                best = candidate; bestSize = size; bestSeed = candidateSeed; bestReason = reason; shrinks++;
            }
            string report = $"Set seed: \"{bestSeed}\" to reproduce ({shrinks} shrinks, {skipped} skipped, {iterations} total).\n{best}\n{bestReason}";
            return (bestSeed, best, shrinks, skipped, report);
        }

        static T One<T>(Gen<T> gen, string seed)
        {
            T value = default(T);
            gen.Sample(t => { value = t; }, seed: seed, iter: 1, threads: 1);
            return value;
        }

        static ulong SplitMix64(ulong x)
        {
            unchecked {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        // ---------------------------------------------------------------- properties

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, ref string error, bool safeRounding = true)
        {
            return Run(a, b, op, ref error, out _, safeRounding);
        }

        static DMesh3 Run(DMesh3 a, DMesh3 b, Ops op, ref string error, out double maxDrift, bool safeRounding = true)
        {
            maxDrift = 0;
            if (error != "") return null;
            DMesh3 r = BooleanOracle.Run(a, b, op, out string e, out maxDrift, safeRounding);
            if (e != "") error = op + ": " + e;
            return r;
        }

        static string First(params Func<string>[] checks)
        {
            foreach (Func<string> c in checks) { string r = c(); if (r != "") return r; }
            return "";
        }

        /// <summary>
        /// For boxes: each operation matches the analytic reference; vol(AuB) + vol(AnB) = vol A + vol B exactly;
        /// (A-B) u (AnB) is A by the reference and by SameSolid; u and n commute.
        /// </summary>
        internal static string BoxProperties(BoxCase c)
        {
            DMesh3 a = c.MeshA(), b = c.MeshB();
            string error = "";
            var r = new Dictionary<Ops, DMesh3>();
            foreach (Ops op in BooleanOracle.AllOps) r[op] = Run(a, b, op, ref error);
            DMesh3 rebuilt = Run(r[Ops.Difference], r[Ops.Intersection], Ops.Union, ref error);
            DMesh3 ba = Run(b, a, Ops.Union, ref error), bi = Run(b, a, Ops.Intersection, ref error);
            if (error != "") return error;
            var reversed = new BoxCase(c.B, c.A, c.Exponent, c.Translation);
            double size = 8 * c.Scale;
            return First(
                () => Prefix("union", BooleanOracle.CheckBoxResult(r[Ops.Union], c, Ops.Union)),
                () => Prefix("intersection", BooleanOracle.CheckBoxResult(r[Ops.Intersection], c, Ops.Intersection)),
                () => Prefix("difference", BooleanOracle.CheckBoxResult(r[Ops.Difference], c, Ops.Difference)),
                () => (BooleanOracle.ExactSixVolume(r[Ops.Union]) + BooleanOracle.ExactSixVolume(r[Ops.Intersection]))
                      .Equals(BooleanOracle.ExactSixVolume(a) + BooleanOracle.ExactSixVolume(b)) ? "" : "inclusion-exclusion",
                () => Prefix("(A-B) u (AnB)", BooleanOracle.CheckBoxResult(rebuilt, new BoxCase(c.A, c.A, c.Exponent, c.Translation), Ops.Union)),
                () => Prefix("(A-B) u (AnB) vs A", BooleanOracle.SameSolid(rebuilt, a, size)),
                () => Prefix("union B,A", BooleanOracle.CheckBoxResult(ba, reversed, Ops.Union)),
                () => Prefix("intersection B,A", BooleanOracle.CheckBoxResult(bi, reversed, Ops.Intersection)),
                () => Prefix("union A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Union], ba, size)),
                () => Prefix("intersection A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Intersection], bi, size)));
        }

        static string Prefix(string what, string reason) => reason == "" ? "" : what + ": " + reason;

        [Trait("Category", "Slow")]
        [Theory]
        [InlineData("general", 1u, 400)]
        [InlineData("coplanar", 2u, 300)]
        [InlineData("touching", 3u, 300)]
        [InlineData("nested", 4u, 300)]
        public void Boxes(string family, uint campaign, int samples) =>
            Campaign("Boxes(" + family + ")", GenCase(GenFamily(family)), BoxProperties, samples, campaign);

        [Theory]
        [InlineData("disjoint", 11u)]
        [InlineData("identical", 12u)]
        [InlineData("strict_nested", 13u)]
        [InlineData("overlapping", 14u)]
        [InlineData("coplanar_overlap", 15u)]
        [InlineData("face_touching", 16u)]
        [InlineData("edge_touching", 17u)]
        [InlineData("vertex_touching", 18u)]
        public void DirectedBoxes(string family, uint campaign) =>
            Campaign("DirectedBoxes(" + family + ")", GenCase(GenDirectedFamily(family)),
                c => First(() => DirectedFamilyReason(family, c), () => BoxProperties(c)), 32, campaign);

        [Theory]
        [InlineData("disjoint")]
        [InlineData("identical")]
        [InlineData("strict_nested")]
        [InlineData("overlapping")]
        [InlineData("coplanar_overlap")]
        [InlineData("face_touching")]
        [InlineData("edge_touching")]
        [InlineData("vertex_touching")]
        public void DirectedFamilyCheck_RejectsWrongRelations(string family)
        {
            var a = new IntBox(0, 0, 0, 1, 1, 1);
            var b = family == "identical" ? new IntBox(2, 0, 0, 3, 1, 1) : a;
            Assert.NotEqual("", DirectedFamilyReason(family, new BoxCase(a, b)));
        }

        static IntBox ParseBox(string box)
        {
            int[] v = box.Split(' ').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            return new IntBox(v[0], v[1], v[2], v[3], v[4], v[5]);
        }

        // One canonical member per family (A = [0,2]^3 unless the family needs a larger A): the classifier must
        // accept it, so the neighbour controls below cannot pass with a classifier that rejects everything.
        [Theory]
        [InlineData("disjoint", "0 0 0 2 2 2", "3 0 0 5 2 2")]
        [InlineData("identical", "0 0 0 2 2 2", "0 0 0 2 2 2")]
        [InlineData("strict_nested", "0 0 0 4 4 4", "1 1 1 3 3 3")]
        [InlineData("overlapping", "0 0 0 2 2 2", "1 1 1 3 3 3")]
        [InlineData("coplanar_overlap", "0 0 0 2 2 2", "1 0 1 3 2 3")]
        [InlineData("face_touching", "0 0 0 2 2 2", "2 0 0 4 2 2")]
        [InlineData("edge_touching", "0 0 0 2 2 2", "2 2 0 4 4 2")]
        [InlineData("vertex_touching", "0 0 0 2 2 2", "2 2 2 4 4 4")]
        public void DirectedFamilyCheck_AcceptsCanonicalCase(string family, string a, string b) =>
            Assert.Equal("", DirectedFamilyReason(family, new BoxCase(ParseBox(a), ParseBox(b))));

        // Targeted negative controls: each family against its nearest neighbours (one contact dimension more or
        // less, a gap of zero, a shared plane, a non-strict or reversed containment). Each row is a case of the
        // named neighbour relation, which the classifier of the family under test must reject.
        [Theory]
        [InlineData("disjoint", "face", "0 0 0 2 2 2", "2 0 0 4 2 2")]
        [InlineData("disjoint", "edge", "0 0 0 2 2 2", "2 2 0 4 4 2")]
        [InlineData("disjoint", "vertex", "0 0 0 2 2 2", "2 2 2 4 4 4")]
        [InlineData("identical", "strict_nested", "0 0 0 4 4 4", "1 1 1 3 3 3")]
        [InlineData("identical", "nested_sharing_faces", "0 0 0 2 2 2", "0 0 0 2 2 1")]
        [InlineData("identical", "coplanar_overlap", "0 0 0 2 2 2", "1 0 1 3 2 3")]
        [InlineData("strict_nested", "nested_sharing_a_face", "0 0 0 4 4 4", "0 1 1 2 3 3")]
        [InlineData("strict_nested", "identical", "0 0 0 2 2 2", "0 0 0 2 2 2")]
        [InlineData("strict_nested", "reversed", "1 1 1 3 3 3", "0 0 0 4 4 4")]
        [InlineData("overlapping", "coplanar_overlap", "0 0 0 2 2 2", "1 0 1 3 2 3")]
        [InlineData("overlapping", "one_shared_plane", "0 0 0 2 2 2", "0 1 1 3 3 3")]
        [InlineData("overlapping", "nested", "0 0 0 4 4 4", "1 1 1 3 3 3")]
        [InlineData("overlapping", "face", "0 0 0 2 2 2", "2 1 1 4 3 3")]
        [InlineData("coplanar_overlap", "overlapping", "0 0 0 2 2 2", "1 1 1 3 3 3")]
        [InlineData("coplanar_overlap", "one_shared_plane", "0 0 0 2 2 2", "0 1 1 3 3 3")]
        [InlineData("coplanar_overlap", "nested_sharing_planes", "0 0 0 4 4 4", "1 0 1 3 4 3")]
        [InlineData("coplanar_overlap", "identical", "0 0 0 2 2 2", "0 0 0 2 2 2")]
        [InlineData("face_touching", "edge", "0 0 0 2 2 2", "2 2 0 4 4 2")]
        [InlineData("face_touching", "vertex", "0 0 0 2 2 2", "2 2 2 4 4 4")]
        [InlineData("face_touching", "disjoint", "0 0 0 2 2 2", "3 0 0 5 2 2")]
        [InlineData("face_touching", "overlapping", "0 0 0 2 2 2", "1 1 1 3 3 3")]
        [InlineData("edge_touching", "face", "0 0 0 2 2 2", "2 0 0 4 2 2")]
        [InlineData("edge_touching", "vertex", "0 0 0 2 2 2", "2 2 2 4 4 4")]
        [InlineData("edge_touching", "disjoint", "0 0 0 2 2 2", "2 3 0 4 5 2")]
        [InlineData("vertex_touching", "edge", "0 0 0 2 2 2", "2 2 0 4 4 2")]
        [InlineData("vertex_touching", "face", "0 0 0 2 2 2", "2 0 0 4 2 2")]
        [InlineData("vertex_touching", "disjoint", "0 0 0 2 2 2", "2 2 3 4 4 5")]
        public void DirectedFamilyCheck_RejectsNeighbouringRelations(string family, string neighbour, string a, string b) =>
            Assert.True(DirectedFamilyReason(family, new BoxCase(ParseBox(a), ParseBox(b))) != "",
                family + " accepted a " + neighbour + " case");

        [Theory]
        [InlineData("disjoint")]
        [InlineData("face_touching")]
        public void DirectedCampaign_ShrinksAndReplaysWithinItsFamily(string family)
        {
            var gen = GenCase(GenDirectedFamily(family));
            var visited = new List<BoxCase>();
            string WrongOperation(BoxCase c)
            {
                Assert.Equal("", DirectedFamilyReason(family, c));
                visited.Add(c);
                DMesh3 wrong = BooleanOracle.Run(c.MeshA(), c.MeshB(), Ops.Difference, out string error);
                Assert.Equal("", error);
                return BooleanOracle.CheckBoxResult(wrong, c, Ops.Union);
            }
            var failure = Assert.Throws<Xunit.Sdk.XunitException>(() =>
                Campaign("DirectedFault(" + family + ")", gen, WrongOperation, 1, 31u));
            log.WriteLine(failure.Message);
            string shrink = failure.Message.Substring(failure.Message.IndexOf("Shrunk (", StringComparison.Ordinal));
            Match match = Regex.Match(shrink, "Set seed: \"([^\"]+)\".*\\((\\d+) shrinks");
            Assert.True(match.Success, failure.Message);
            Assert.True(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > 0, failure.Message);
            BoxCase replay = null;
            gen.Sample(c => replay = c, seed: match.Groups[1].Value, iter: 1, threads: 1);
            Assert.Contains(replay.ToString(), shrink);
            Assert.NotEqual(visited[0].ToString(), replay.ToString());
            Assert.Equal("", DirectedFamilyReason(family, replay));
            Assert.NotEqual("", WrongOperation(replay));
        }

        /// <summary>
        /// One representation change (triangle order, vertex ids, corner rotation, subdivision, slivers) of A, B or
        /// both: the result still matches the analytic reference, and the result without the change (SameSolid).
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void Boxes_RepresentationInvariance()
        {
            var gen = Gen.Select(GenCase(GenFamily("general")), Gen.Enum<Rep>(), Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[0, 1000],
                (c, rep, operand, op, seed) => new RepresentationCase(c, rep, operand, BooleanOracle.AllOps[op], seed));
            Campaign("Boxes_RepresentationInvariance", gen, x => {
                BoxCase c = x.Case;
                DMesh3 a = c.MeshA(), b = c.MeshB();
                string error = "";
                DMesh3 baseline = Run(a, b, x.Op, ref error);
                if (x.Operand != 1) a = BooleanOracle.Change(a, x.Rep, x.Seed);
                if (x.Operand != 0) b = BooleanOracle.Change(b, x.Rep, x.Seed + 1);
                DMesh3 result = Run(a, b, x.Op, ref error, out double drift);
                // slivers: the result with SafeRounding must be clean and the same solid up to its documented
                // displacement bound (it contracts pieces below its degenerate threshold even where they are exact,
                // ExactBooleanPropertyFindings); the exact geometry is checked without it
                DMesh3 plain = x.Rep == Rep.Slivers ? Run(a, b, x.Op, ref error, safeRounding: false) : result;
                if (error != "") return error;
                return First(
                    () => x.Operand != 1 ? Prefix("changed A", BooleanOracle.CheckRepresentation(c.MeshA(), a, 8 * c.Scale)) : "",
                    () => x.Operand != 0 ? Prefix("changed B", BooleanOracle.CheckRepresentation(c.MeshB(), b, 8 * c.Scale)) : "",
                    () => Prefix("SafeRounding", BooleanOracle.CleanReason(result)),
                    () => x.Rep == Rep.Slivers ? Prefix("SafeRounding vs baseline", BooleanOracle.WithinSafeRoundingBound(result, baseline, 8 * c.Scale, drift)) : "",
                    () => BooleanOracle.CheckBoxResult(plain, c, x.Op, x.Rep == Rep.Slivers),
                    () => Prefix("vs baseline", BooleanOracle.SameSolid(plain, baseline, 8 * c.Scale)));
            }, 400, 5u);
        }

        public sealed class RepresentationCase
        {
            public readonly BoxCase Case; public readonly Rep Rep; public readonly int Operand; public readonly Ops Op; public readonly int Seed;
            public RepresentationCase(BoxCase c, Rep rep, int operand, Ops op, int seed) { Case = c; Rep = rep; Operand = operand; Op = op; Seed = seed; }
            public override string ToString() => $"{Case} {Op} {Rep} of {"ABX"[Operand]} seed {Seed}";
        }

        /// <summary>
        /// Non-axis-aligned solids: results clean; inclusion-exclusion and vol(A-B) + vol(AnB) = vol A within the
        /// rounding tolerance; (A-B) u (AnB) is A as a regularized solid; u and n commute (SameSolid). A bounded
        /// numerical check, weaker than the analytic box reference.
        /// </summary>
        [Trait("Category", "Slow")]
        [Fact]
        public void Tetrahedra()
        {
            Campaign("Tetrahedra", GenTets.Select(x => new TetCase(x.a, x.b, x.shift)), x => {
                DMesh3 a = x.A.Mesh(x.Shift), b = x.B.Mesh(x.Shift);
                string error = "";
                var r = new Dictionary<Ops, DMesh3>();
                foreach (Ops op in BooleanOracle.AllOps) r[op] = Run(a, b, op, ref error);
                DMesh3 rebuilt = Run(r[Ops.Difference], r[Ops.Intersection], Ops.Union, ref error);
                DMesh3 ba = Run(b, a, Ops.Union, ref error), bi = Run(b, a, Ops.Intersection, ref error);
                if (error != "") return error;
                double tol = BooleanOracle.VolumeTolerance(r[Ops.Union], r[Ops.Intersection]) + BooleanOracle.VolumeTolerance(r[Ops.Difference], a);
                double va = x.A.SixVolume / 6.0, vb = x.B.SixVolume / 6.0;
                return First(
                    () => Prefix("union", BooleanOracle.CleanReason(r[Ops.Union])),
                    () => Prefix("intersection", BooleanOracle.CleanReason(r[Ops.Intersection])),
                    () => Prefix("difference", BooleanOracle.CleanReason(r[Ops.Difference])),
                    () => Math.Abs(BooleanOracle.Volume(r[Ops.Union]) + BooleanOracle.Volume(r[Ops.Intersection]) - va - vb) <= tol ? "" : "inclusion-exclusion",
                    () => Math.Abs(BooleanOracle.Volume(r[Ops.Difference]) + BooleanOracle.Volume(r[Ops.Intersection]) - va) <= tol ? "" : "vol(A-B) + vol(AnB) != vol A",
                    () => Prefix("(A-B) u (AnB)", BooleanOracle.CleanReason(rebuilt)),
                    () => Prefix("(A-B) u (AnB) vs A", BooleanOracle.SameRegularizedSolid(rebuilt, a, 6)),
                    () => Prefix("union A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Union], ba, 6)),
                    () => Prefix("intersection A,B vs B,A", BooleanOracle.SameSolid(r[Ops.Intersection], bi, 6)));
            }, 600, 6u);
        }

        public sealed class TetCase
        {
            public readonly IntTet A, B; public readonly Vector3d Shift;
            public TetCase(IntTet a, IntTet b, Vector3d shift) { A = a; B = b; Shift = shift; }
            public override string ToString() => $"A={A} B={B} shift={(Shift == Vector3d.Zero ? "0" : "utm")}";
        }

        // ---------------------------------------------------------------- the campaign harness itself

        /// <summary>
        /// A controlled faulty result: the union with one triangle less whenever it has more than 12 triangles.
        /// The campaign must fail and report the original seed, which gives the first failing case again, and a
        /// shrunk seed after at least one shrink (CsCheck accepts a failing case as a shrink only if it is smaller by
        /// its own size measure), which gives a different case that still fails.
        /// </summary>
        [Fact]
        public void Campaign_FindsShrinksAndReplaysAFaultyResult()
        {
            var failing = new List<BoxCase>();
            string Faulty(BoxCase c)
            {
                DMesh3 union = BooleanOracle.Run(c.MeshA(), c.MeshB(), Ops.Union, out string error);
                if (error != "") return error;
                if (union.TriangleCount > 12) union.RemoveTriangle(union.TriangleIndices().Last(), true, false);
                string r = BooleanOracle.CheckBoxResult(union, c, Ops.Union);
                if (r != "") failing.Add(c);
                return r;
            }
            var gen = GenCase(GenFamily("general"));
            var e = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Campaign("faulty union", gen, Faulty, 40, 7u));
            log.WriteLine(e.Message);
            Assert.Contains("not closed", e.Message);
            // the shrunk seed is the one after "Shrunk" (the failure text before it holds the original seed too)
            string afterShrunk = e.Message.Substring(e.Message.IndexOf("Shrunk (", StringComparison.Ordinal));
            Match m = Regex.Match(afterShrunk, "Set seed: \"([^\"]+)\".*\\((\\d+) shrinks");
            Assert.True(m.Success, "no shrunk seed in: " + e.Message);
            Assert.True(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) > 0, "no shrink: " + afterShrunk);
            BoxCase first = failing[0];
            BoxCase shrunk = null;
            gen.Sample(c => { shrunk = c; }, seed: m.Groups[1].Value, iter: 1, threads: 1);
            Assert.NotEqual(first.ToString(), shrunk.ToString());
            Assert.Contains(shrunk.ToString(), afterShrunk);
            Assert.NotEqual("", Faulty(shrunk));
            // the reported original seed gives the first failing case again
            Match original = Regex.Match(e.Message, "fails for seed (\\S+)");
            BoxCase again = null;
            gen.Sample(c => { again = c; }, seed: original.Groups[1].Value, iter: 1, threads: 1);
            Assert.Equal(first.ToString(), again.ToString());
        }

        /// <summary>
        /// The shrink of a failing seed is deterministic: twice the same shrunk seed, case and shrink count, also when
        /// CsCheck's thread-local generator (the former source of run-to-run differences) is advanced in between.
        /// </summary>
        [Fact]
        public void Shrink_IsDeterministicForAGivenSeed()
        {
            var gen = GenCase(GenFamily("general"));
            string Faulty(BoxCase c)
            {
                DMesh3 union = BooleanOracle.Run(c.MeshA(), c.MeshB(), Ops.Union, out string error);
                if (error != "") return error;
                if (union.TriangleCount > 12) union.RemoveTriangle(union.TriangleIndices().Last(), true, false);
                return BooleanOracle.CheckBoxResult(union, c, Ops.Union);
            }
            string seed = new PCG(7u, 0).ToString();
            Assert.NotEqual("", Faulty(One(gen, seed)));
            var first = Shrink(gen, Faulty, seed, ShrinkIterations);
            for (int i = 0; i < 17; ++i) PCG.ThreadPCG.Next();
            var second = Shrink(gen, Faulty, seed, ShrinkIterations);
            log.WriteLine(first.Report);
            Assert.True(first.Shrinks > 0, first.Report);
            Assert.Equal(first.Seed, second.Seed);
            Assert.Equal(first.Shrinks, second.Shrinks);
            Assert.Equal(first.Skipped, second.Skipped);
            Assert.Equal(first.Report, second.Report);
            Assert.Equal(first.Value.ToString(), One(gen, first.Seed).ToString());
        }

        [Fact]
        public void Campaign_IsReproducible()
        {
            var gen = GenCase(GenFamily("touching"));
            var x = new List<string>();
            var y = new List<string>();
            Campaign("first", gen, c => { x.Add(c.ToString()); return ""; }, 20, 3u);
            Campaign("second", gen, c => { y.Add(c.ToString()); return ""; }, 20, 3u);
            Assert.Equal(x, y);
            Assert.True(x.Distinct().Count() > 10);
        }
    }
}
