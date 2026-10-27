using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using g3;
using Xunit;
using Xunit.Sdk;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The test-case minimizer of the harness (Harness/MeshMinimizer.cs, DeltaDebug.cs, FailureMinimizer.cs): synthetic
    /// predicates with a known minimal case, the robustness policies (exceptions, timeouts, budget), determinism,
    /// invariance and the C# output.
    /// </summary>
    public class MeshMinimizerTests
    {
        static MinimizeOptions Inline(bool simplify = true) => new MinimizeOptions() { PredicateTimeout = TimeSpan.Zero, SimplifyCoordinates = simplify };

        /// <summary> sphere soup with the triangle `which` duplicated (on its own vertices, at the end) </summary>
        static DMesh3 SphereWithDuplicate(int which = 17, Vector3d? center = null)
        {
            DMesh3 m = Defects.Sphere(center ?? Vector3d.Zero, 1);
            Index3i t = m.GetTriangle(which);
            int a = m.AppendVertex(m.GetVertex(t.a)), b = m.AppendVertex(m.GetVertex(t.b)), c = m.AppendVertex(m.GetVertex(t.c));
            Assert.True(m.AppendTriangle(a, b, c) >= 0);
            return m;
        }

        static string Key(Vector3d p) => $"{p.x:R} {p.y:R} {p.z:R}";

        /// <summary> two triangles with the same three corner positions (in any order) </summary>
        static bool HasCoincidentPair(DMesh3 m)
        {
            var seen = new HashSet<string>();
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                string k = string.Join("|", new[] { tv.a, tv.b, tv.c }.Select(v => Key(m.GetVertex(v))).OrderBy(s => s, StringComparer.Ordinal));
                if (seen.Add(k) == false) return true;
            }
            return false;
        }

        /// <summary> removing any one triangle makes the error disappear </summary>
        static void AssertOneMinimal(DMesh3 m, Func<DMesh3, bool> fails)
        {
            Assert.True(fails(new DMesh3(m)), "the result does not fail");
            foreach (int t in m.TriangleIndices().ToList()) {
                var c = new DMesh3(m);
                c.RemoveTriangle(t, true, false);
                Assert.False(fails(c), $"triangle {t} can be removed");
            }
        }

        [Fact]
        public void CoincidentPair_ExactlyThoseTwoTrianglesRemain()
        {
            DMesh3 input = SphereWithDuplicate();
            Index3i orig = input.GetTriangle(17);
            var r = MeshMinimizer.Minimize(input, HasCoincidentPair, Inline(false));
            Assert.Equal(MeshMinimizer.MinimizeStatus.Minimized, r.Status);
            Assert.Equal(2, r.Mesh.TriangleCount);
            Assert.Equal(6, r.Mesh.VertexCount);
            var expected = new[] { orig.a, orig.b, orig.c }.Select(v => Key(input.GetVertex(v))).OrderBy(s => s).ToList();
            foreach (int t in r.Mesh.TriangleIndices()) {
                Index3i tv = r.Mesh.GetTriangle(t);
                Assert.Equal(expected, new[] { tv.a, tv.b, tv.c }.Select(v => Key(r.Mesh.GetVertex(v))).OrderBy(s => s).ToList());
            }
            Assert.True(r.Mesh.CheckValidity(true, FailMode.ReturnOnly));
            AssertOneMinimal(r.Mesh, HasCoincidentPair);
            // the C# code builds the tested mesh: same ids, coordinates, no attributes
            string code = MeshMinimizer.ToCSharp(r.Mesh);
            Assert.Contains("var m = new DMesh3(false, false, false, false);", code);
            DMesh3 back = ParseCSharp(code);
            Assert.Equal("", Invariance.Difference(r.Mesh, back));
            Assert.Equal(r.Mesh.Components, back.Components);
            Assert.Equal(MeshComponents.None, r.Mesh.Components);
            // the input is not modified
            Assert.Equal(SphereWithDuplicate().TriangleCount, input.TriangleCount);
        }

        [Fact]
        public void Components_DropsTheOthers()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            MeshEditor.Append(m, Defects.Sphere(new Vector3d(3, 0, 0), 1));
            MeshEditor.Append(m, SphereWithDuplicate(5, new Vector3d(6, 0, 0)));
            Func<DMesh3, bool> fails = x => HasCoincidentPair(x) && x.Vertices().Any(p => p.x > 2.5 && p.x < 4.5);
            var r = MeshMinimizer.Minimize(m, fails, Inline(false));
            Assert.Equal(MeshMinimizer.MinimizeStatus.Minimized, r.Status);
            Assert.Contains("components: 4 -> 3", string.Join("\n", r.Log));
            Assert.Equal(3, r.Mesh.TriangleCount);
            AssertOneMinimal(r.Mesh, fails);
        }

        [Fact]
        public void UnusedVertex_OnlyItRemains()
        {
            DMesh3 m = Defects.ByName("unused far vertex").Make();
            Func<DMesh3, bool> fails = x => x.Vertices().Any(p => p.Length > 1e5);
            var r = MeshMinimizer.Minimize(m, fails, Inline());
            Assert.Equal(0, r.Mesh.TriangleCount);
            Assert.Equal(1, r.Mesh.VertexCount);
            Assert.Equal(new Vector3d(1e6, -2e6, 5e5), r.Mesh.GetVertex(0));
            // and the error that needs a triangle with it: the unused vertices go
            Func<DMesh3, bool> needsTriangle = x => x.TriangleCount > 0 && x.TriangleIndices().Any(t => x.GetTriCentroid(t).z > 0.9);
            r = MeshMinimizer.Minimize(m, needsTriangle, Inline());
            Assert.Equal(1, r.Mesh.TriangleCount);
            Assert.Equal(3, r.Mesh.VertexCount);
        }

        // the error needs an edge longer than 0.3: the coordinates are rounded to a coarse grid and stay long enough
        [Fact]
        public void Coordinates_RoundedToACoarseGrid()
        {
            DMesh3 m = Defects.Sphere(new Vector3d(0.1234567, -0.7654321, 0.3141593), 1);
            Func<DMesh3, bool> fails = x => x.EdgeIndices().Any(e => { Index2i ev = x.GetEdgeV(e); return x.GetVertex(ev.a).Distance(x.GetVertex(ev.b)) > 0.3; });
            var plain = MeshMinimizer.Minimize(m, fails, Inline(false));
            var r = MeshMinimizer.Minimize(m, fails, Inline());
            Assert.Equal(plain.Mesh.TriangleCount, r.Mesh.TriangleCount);
            Assert.True(fails(r.Mesh));
            // the plain coordinates need many bits; the rounded ones a few (multiples of 2^-6 of the extent's power of two)
            Func<DMesh3, int> bits = x => x.Vertices().SelectMany(p => new[] { p.x, p.y, p.z }).Max(c => MantissaBits(c));
            Assert.True(bits(plain.Mesh) > 30, bits(plain.Mesh).ToString());
            Assert.True(bits(r.Mesh) <= 8, bits(r.Mesh).ToString());
        }

        // the coincident pair cannot be rounded vertex by vertex (that breaks the coincidence), only all together
        [Fact]
        public void Coordinates_RoundedTogether()
        {
            var r = MeshMinimizer.Minimize(SphereWithDuplicate(), HasCoincidentPair, Inline());
            Assert.Equal(2, r.Mesh.TriangleCount);
            Assert.True(r.Mesh.Vertices().SelectMany(p => new[] { p.x, p.y, p.z }).All(c => MantissaBits(c) <= 4));
        }

        // the steps repeat until nothing changes: here the rounding lets more triangles go
        [Fact]
        public void Rounds_RepeatUntilNothingChanges()
        {
            Func<DMesh3, bool> fails = x => x.TriangleCount >= 3
                || x.TriangleCount >= 1 && x.Vertices().All(p => new[] { p.x, p.y, p.z }.All(c => c * 8 == Math.Round(c * 8)));
            var r = MeshMinimizer.Minimize(Defects.Sphere(new Vector3d(0.1, 0.2, 0.3), 1), fails, Inline());
            Assert.Equal(1, r.Mesh.TriangleCount);
            Assert.Contains("round 2, triangles: 3 -> 1", string.Join("\n", r.Log));
        }

        // an extent near double.MaxValue: no overflow of the grid step into NaN coordinates
        [Fact]
        public void Coordinates_HugeExtent_StayFinite()
        {
            // (an extent of at least 2^1023: the step 2^1024 overflows)
            var m = new DMesh3(false);
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(new Vector3d(1.7e308, 0, 0)); m.AppendVertex(new Vector3d(0, 1.7e308, 0));
            m.AppendTriangle(0, 1, 2);
            m.AppendVertex(new Vector3d(0, 0, 1)); m.AppendVertex(new Vector3d(1, 0, 1)); m.AppendVertex(new Vector3d(0, 1, 1));
            m.AppendTriangle(3, 4, 5);
            Func<DMesh3, bool> fails = x => x.Vertices().Any(p => p.x > 1e308);
            var r = MeshMinimizer.Minimize(m, fails, Inline());
            Assert.Equal(1, r.Mesh.TriangleCount);
            Assert.True(r.Mesh.Vertices().All(p => double.IsNaN(p.x) == false && double.IsNaN(p.y) == false && double.IsNaN(p.z) == false));
        }

        // a NaN coordinate: compared by bits, so the rounds reach a fixpoint
        [Fact]
        public void Coordinates_NaN_ReachesAFixpoint()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            m.SetVertex(0, new Vector3d(double.NaN, 0.3, 0.7));
            Func<DMesh3, bool> fails = x => x.Vertices().Any(p => double.IsNaN(p.x));
            var r = MeshMinimizer.Minimize(m, fails, Inline());
            Assert.Equal(MeshMinimizer.MinimizeStatus.Minimized, r.Status);
            Assert.Equal(1, r.Mesh.TriangleCount);
            Assert.DoesNotContain(r.Log, l => l.StartsWith("round 3"));
        }

        static int MantissaBits(double x)
        {
            if (x == 0) return 0;
            long m = BitConverter.DoubleToInt64Bits(x) & 0xFFFFFFFFFFFFFL;
            int bits = 52;
            while (bits > 0 && (m & 1) == 0) { m >>= 1; --bits; }
            return bits;
        }

        // the minimized case is ready for a test: a predicate that is false on the input is reported, not minimized
        [Fact]
        public void NotReproduced_ReturnsTheInput()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            var r = MeshMinimizer.Minimize(m, HasCoincidentPair, Inline());
            Assert.Equal(MeshMinimizer.MinimizeStatus.NotReproduced, r.Status);
            Assert.Equal(m.TriangleCount, r.Mesh.TriangleCount);
            Assert.Equal(1, r.Evaluations);
        }

        // a predicate that throws is a different error: the result keeps the triangles it needs not to throw
        [Fact]
        public void Exceptions_CountAsDifferentError()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            Func<DMesh3, bool> fails = x => { if (x.TriangleCount < 5) throw new InvalidOperationException("other error"); return true; };
            var r = MeshMinimizer.Minimize(m, fails, Inline());
            Assert.Equal(5, r.Mesh.TriangleCount);
            Assert.True(r.Exceptions > 0);
            Assert.IsType<InvalidOperationException>(r.FirstException);

            r = MeshMinimizer.Minimize(m, fails, new MinimizeOptions() { PredicateTimeout = TimeSpan.Zero, ExceptionMeansFailure = true });
            Assert.Equal(0, r.Mesh.TriangleCount);
        }

        [Fact]
        public void Timeouts_CountAsDifferentError()
        {
            DMesh3 m = Defects.ByName("open sheet").Make();
            foreach (int t in m.TriangleIndices().Skip(6).ToList()) m.RemoveTriangle(t);
            Func<DMesh3, bool> fails = x => { if (x.TriangleCount < 3) Thread.Sleep(10000); return true; };
            var options = new MinimizeOptions() { PredicateTimeout = TimeSpan.FromMilliseconds(500), MaxTimeouts = 1000, SimplifyCoordinates = false };
            var r = MeshMinimizer.Minimize(m, fails, options);
            Assert.Equal(MeshMinimizer.MinimizeStatus.Minimized, r.Status);
            Assert.Equal(3, r.Mesh.TriangleCount);
            Assert.True(r.Timeouts > 0);

            // MaxTimeouts stops the run, with a failing result
            options.MaxTimeouts = 1;
            r = MeshMinimizer.Minimize(m, fails, options);
            Assert.Equal(MeshMinimizer.MinimizeStatus.Stopped, r.Status);
            Assert.Equal(1, r.Timeouts);
            Assert.True(r.Mesh.TriangleCount >= 3);
        }

        [Fact]
        public void TimeBudget_StopsWithAFailingResult()
        {
            DMesh3 m = SphereWithDuplicate();
            // (about 13 calls before the first reduction, a few dozen more for the whole run). The first calls are
            // fast, so a reduction exists before the budget even on a loaded CI runner (3 s were not enough once on
            // the net48 runner); the rest are slow, so the whole run takes far longer than the budget
            int calls = 0;
            Func<DMesh3, bool> slow = x => {
                if (Interlocked.Increment(ref calls) > 20) Thread.Sleep(1000);
                return HasCoincidentPair(x);
            };
            var budget = TimeSpan.FromSeconds(8);
            var r = MeshMinimizer.Minimize(m, slow, new MinimizeOptions() { TimeBudget = budget });
            Assert.Equal(MeshMinimizer.MinimizeStatus.Stopped, r.Status);
            Assert.True(r.Elapsed < budget + TimeSpan.FromSeconds(5), r.Elapsed.ToString());
            Assert.True(r.Mesh.TriangleCount < m.TriangleCount, $"not reduced: {r}, {calls} calls");
            Assert.True(HasCoincidentPair(r.Mesh), $"result does not fail: {r}, {calls} calls");
        }

        // an error with many minimal cases (any 3 triangles above z = 0.5): the seed picks one, reproducibly
        [Fact]
        public void Deterministic_PerSeed()
        {
            DMesh3 m = Defects.Sphere(Vector3d.Zero, 1);
            Func<DMesh3, bool> fails = x => x.TriangleIndices().Count(t => x.GetTriCentroid(t).z > 0.5) >= 3;
            var results = new List<DMesh3>();
            foreach (int seed in new[] { 0, 0, 7, 7 }) {
                var r = MeshMinimizer.Minimize(m, fails, new MinimizeOptions() { Seed = seed });
                Assert.Equal(3, r.Mesh.TriangleCount);
                AssertOneMinimal(r.Mesh, fails);
                results.Add(r.Mesh);
            }
            Assert.Equal("", Invariance.Difference(results[0], results[1]));
            Assert.Equal("", Invariance.Difference(results[2], results[3]));
            Assert.NotEqual("", Invariance.GeometricDifference(results[0], results[2]));
        }

        // the steps and the coordinate grid are relative to the input: a 2^k scale gives the same result, scaled, bit for
        // bit; every similarity gives a case of the same size
        [Fact]
        public void Invariant_UnderSimilarities()
        {
            DMesh3 input = SphereWithDuplicate();
            DMesh3 reference = MeshMinimizer.Minimize(input, HasCoincidentPair, Inline()).Mesh;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 r = MeshMinimizer.Minimize(Invariance.Transformed(input, s), HasCoincidentPair, Inline()).Mesh;
                Assert.Equal(reference.TriangleCount, r.TriangleCount);
                Assert.Equal(reference.VertexCount, r.VertexCount);
                Assert.True(HasCoincidentPair(r), s.Name);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(reference, s), r));
            }
        }

        [Fact]
        public void Attributes_DroppedWhenNotNeeded_KeptWhenNeeded()
        {
            DMesh3 m = SphereWithDuplicate();
            m.EnableTriangleGroups(3);
            m.EnableVertexColors(Vector3f.One);
            Assert.True(m.HasVertexNormals);
            var r = MeshMinimizer.Minimize(m, HasCoincidentPair, Inline());
            Assert.False(r.Mesh.HasTriangleGroups || r.Mesh.HasVertexNormals || r.Mesh.HasVertexColors);

            m.SetTriangleGroup(17, 9);
            Func<DMesh3, bool> needsGroup = x => x.HasTriangleGroups && x.TriangleIndices().Any(t => x.GetTriangleGroup(t) == 9);
            r = MeshMinimizer.Minimize(m, needsGroup, Inline());
            Assert.Equal(1, r.Mesh.TriangleCount);
            Assert.True(r.Mesh.HasTriangleGroups);
            Assert.False(r.Mesh.HasVertexNormals || r.Mesh.HasVertexColors);
            string code = MeshMinimizer.ToCSharp(r.Mesh);
            Assert.Contains("int[] g = { 9 };", code);
        }

        // ---- output ----

        /// <summary> the arrays of a ToCSharp method, parsed back into a mesh (the same construction as the code) </summary>
        static DMesh3 ParseCSharp(string code)
        {
            double[] Doubles(string name) => Array(code, name).Select(ParseLiteral).ToArray();
            double[] v = Doubles("double[] v");
            int[] t = Array(code, "int[] t").Select(int.Parse).ToArray();
            Assert.Matches(@"var m = new DMesh3\(false, false, false, (true|false)\);", code);
            var m = new DMesh3(false, false, false, false);
            for (int i = 0; i < v.Length; i += 3) m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));
            for (int i = 0; i < t.Length; i += 3) Assert.True(m.AppendTriangle(t[i], t[i + 1], t[i + 2]) >= 0);
            return m;
        }

        /// <summary> a MeshMinimizer.Literal, read without double.Parse where .NET Framework is not exact </summary>
        static double ParseLiteral(string s)
        {
            const string bits = "System.BitConverter.Int64BitsToDouble(";
            switch (s) {
                case "double.NaN": return double.NaN;
                case "double.PositiveInfinity": return double.PositiveInfinity;
                case "double.NegativeInfinity": return double.NegativeInfinity;
                // (in C# source -0.0 is the negation of 0.0; double.Parse("-0.0") is +0 on .NET Framework)
                case "-0.0": return -0.0;
            }
            if (s.StartsWith(bits))
                return BitConverter.Int64BitsToDouble(long.Parse(s.Substring(bits.Length, s.Length - bits.Length - 2), CultureInfo.InvariantCulture));
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        static string[] Array(string code, string name)
        {
            Match match = Regex.Match(code, Regex.Escape(name) + @" = \{([^}]*)\}");
            Assert.True(match.Success, name);
            return match.Groups[1].Value.Split(new[] { ',', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        [Fact]
        public void CSharp_RoundTripsBitForBit()
        {
            DMesh3 m = Invariance.Transformed(SphereWithDuplicate(), Similarity.RotatedUtmMillimetre);
            m.SetVertex(0, new Vector3d(-0.0, 1e-310, double.NaN));
            m.SetVertex(1, new Vector3d(double.PositiveInfinity, double.NegativeInfinity, 0.1));
            m.RemoveTriangle(3);
            string code = MeshMinimizer.ToCSharp(m, "Repro", "a comment\nover two lines");
            Assert.StartsWith("// a comment\n// over two lines\nstatic DMesh3 Repro()", code);
            DMesh3 back = ParseCSharp(code);
            Assert.Equal(m.TriangleCount, back.TriangleCount);
            Assert.Equal("", Invariance.GeometricDifference(m, back));
            // (the same text on .NET Framework and .NET Core)
            Assert.Contains($"-0.0, System.BitConverter.Int64BitsToDouble({BitConverter.DoubleToInt64Bits(1e-310)}L), double.NaN", code);
            Assert.Contains("double.PositiveInfinity, double.NegativeInfinity, 0.1,", code);
            Assert.Equal(1 / -0.0, 1 / back.GetVertex(0).x);
            // the special values by name, independent of the runtime's number formatting
            Assert.Equal("-0.0", MeshMinimizer.Literal(-0.0));
            Assert.Equal("0", MeshMinimizer.Literal(0.0));
            Assert.Equal("double.NaN", MeshMinimizer.Literal(double.NaN));
            Assert.Equal("double.PositiveInfinity", MeshMinimizer.Literal(double.PositiveInfinity));
            Assert.Equal("double.NegativeInfinity", MeshMinimizer.Literal(double.NegativeInfinity));
            foreach (double x in new[] { 0.1, 1.0 / 3, 632117.25, 5712043.123456789, double.Epsilon, double.MaxValue, -1e-300 })
                Assert.Equal(BitConverter.DoubleToInt64Bits(x), BitConverter.DoubleToInt64Bits(ParseLiteral(MeshMinimizer.Literal(x))));
            // exact texts, without a runtime parse
            Assert.Equal("0.1", MeshMinimizer.Literal(0.1));
            Assert.Equal("632117.25", MeshMinimizer.Literal(632117.25));
            Assert.Equal("System.BitConverter.Int64BitsToDouble(1L)", MeshMinimizer.Literal(double.Epsilon));
            Assert.Equal($"System.BitConverter.Int64BitsToDouble({BitConverter.DoubleToInt64Bits(-double.MaxValue)}L)", MeshMinimizer.Literal(-double.MaxValue));
            // obj: one line per vertex and triangle
            string obj = MeshMinimizer.ToObj(m);
            Assert.Equal(m.VertexCount, obj.Split('\n').Count(l => l.StartsWith("v ")));
            Assert.Equal(m.TriangleCount, obj.Split('\n').Count(l => l.StartsWith("f ")));
        }

        // ---- ddmin and bytes ----

        [Fact]
        public void DeltaDebug_FindsTheMinimalSubset()
        {
            var items = Enumerable.Range(0, 100).ToList();
            int calls = 0;
            List<int> r = DeltaDebug.Minimize(items, s => { ++calls; return s.Contains(13) && s.Contains(71) && s.Contains(72); }, () => false);
            Assert.Equal(new[] { 13, 71, 72 }, r);
            // ddmin needs O(k log n) tests for k needed items, far below 2^n or n^2
            Assert.True(calls < 200, calls.ToString());
            // a stop before the first test returns the input
            Assert.Equal(items, DeltaDebug.Minimize(items, s => true, () => true));
            Assert.Empty(DeltaDebug.Minimize(items, s => true, () => false));
        }

        [Fact]
        public void MinimizeBytes_TextAndBinary()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 200; ++i) sb.Append(i == 77 ? "v nan 0 0\n" : $"v {i} {i * 0.5} {i % 7}\n");
            byte[] text = Encoding.ASCII.GetBytes(sb.ToString());
            var r = DeltaDebug.MinimizeBytes(text, d => Encoding.ASCII.GetString(d).Contains("nan"), true, Inline());
            Assert.True(r.Reproduced);
            Assert.Equal("nan", Encoding.ASCII.GetString(r.Data));
            Assert.Equal("System.Text.Encoding.ASCII.GetBytes(\"nan\")", DeltaDebug.ToCSharp(r.Data));
            // a reader-like error that needs well-formed lines: lines first find the line, bytes alone take longer
            Func<byte[], bool> parses = d => {
                string[] lines = Encoding.ASCII.GetString(d).Split('\n');
                return lines.Last() == "" && lines.Take(lines.Length - 1).All(l => Regex.IsMatch(l, @"^v \S+ \S+ \S+$")) && lines.Contains("v nan 0 0");
            };
            r = DeltaDebug.MinimizeBytes(text, parses, true, Inline());
            var bytesOnly = DeltaDebug.MinimizeBytes(text, parses, false, Inline());
            Assert.Equal("v nan 0 0\n", Encoding.ASCII.GetString(r.Data));
            Assert.True(r.Evaluations < bytesOnly.Evaluations / 2, $"{r.Evaluations} vs {bytesOnly.Evaluations}");

            var rand = new Random(3);
            byte[] bin = Enumerable.Range(0, 3000).Select(_ => (byte)rand.Next(0, 200)).ToArray();
            bin[500] = 0xAB; bin[2500] = 0xCD;
            Func<byte[], bool> fails = d => { int a = System.Array.IndexOf(d, (byte)0xAB); return a >= 0 && System.Array.IndexOf(d, (byte)0xCD, a) > a; };
            r = DeltaDebug.MinimizeBytes(bin, fails, false, Inline());
            Assert.Equal(new byte[] { 0xAB, 0xCD }, r.Data);
            Assert.Equal("new byte[] {\n    0xAB, 0xCD\n}", DeltaDebug.ToCSharp(r.Data));
        }

        // ---- coupling to the tests ----

        [Fact]
        public void Signature_IgnoresNumbers()
        {
            Assert.Equal(FailureMinimizer.Signature("random edits 6: 12 edges, at -1.5e-7"), FailureMinimizer.Signature("random edits 6: 3 edges, at 2"));
            Assert.NotEqual(FailureMinimizer.Signature("3 open edges"), FailureMinimizer.Signature("3 bowtie vertices"));
            // the whole message: xunit's Assert.Equal("", problem) has the problem in its later lines, with column markers
            Exception Equal(string problem) { try { Assert.Equal("", problem); } catch (Exception e) { return e; } return null; }
            Assert.NotEqual(FailureMinimizer.Signature(Equal("triangle 3 repeats a vertex")), FailureMinimizer.Signature(Equal("edge 3 without triangles")));
            Assert.Equal(FailureMinimizer.Signature(Equal("triangle 3 repeats a vertex")), FailureMinimizer.Signature(Equal("triangle 12345 repeats a vertex")));
        }

        [Fact]
        public void FailureMinimizer_AppendsTheMinimizedRepro()
        {
            Action<DMesh3> check = m => Assert.False(HasCoincidentPair(m), $"{m.TriangleCount} triangles with a coincident pair");
            // passes: no exception, enabled or not
            FailureMinimizer.Check("clean", () => Defects.Sphere(Vector3d.Zero, 1), check, true, Inline());
            // disabled: the original exception
            Assert.Throws<FalseException>(() => FailureMinimizer.Check("dup", () => SphereWithDuplicate(), check, false, Inline()));
            // enabled: the original message, then the C# repro of the two coincident triangles
            var e = Assert.Throws<XunitException>(() => FailureMinimizer.Check("dup case", () => SphereWithDuplicate(), check, true, Inline()));
            Assert.StartsWith(SphereWithDuplicate().TriangleCount + " triangles with a coincident pair", e.Message);
            Assert.Contains("static DMesh3 MinimizedDupCase()", e.Message);
            Assert.Contains("// 6 vertices, 2 triangles", e.Message);
            DMesh3 back = ParseCSharp(e.Message.Substring(e.Message.IndexOf("static DMesh3")));
            Assert.True(HasCoincidentPair(back));
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(MeshMinimizer.ReproDirectory(), "DupCase.obj")));

            // another exception in the check is another error: not minimized away
            Action<DMesh3> crashesWhenSmall = m => {
                if (m.TriangleCount < 10) throw new IndexOutOfRangeException();
                check(m);
            };
            e = Assert.Throws<XunitException>(() => FailureMinimizer.Check("dup crash", () => SphereWithDuplicate(), crashesWhenSmall, true, Inline()));
            Assert.Matches(@"// \d+ vertices, 10 triangles", e.Message);
        }

        [Fact]
        public void BytesReport_MinimizesAReaderFailure()
        {
            byte[] data = Encoding.ASCII.GetBytes("solid x\nfacet normal 0 0 1\nouter loop\nvertex 1e400 0 0\nendloop\nendfacet\nendsolid\n");
            Func<byte[], string> check = d => Encoding.ASCII.GetString(d).Contains("1e4") ? $"Ok with non-finite coordinates ({d.Length} bytes)" : "";
            string report = FailureMinimizer.BytesReport("stl test", "stl", data, check(data), check, true, Inline());
            Assert.Contains("minimized to 3 of " + data.Length + " bytes", report);
            Assert.EndsWith("System.Text.Encoding.ASCII.GetBytes(\"1e4\")", report);
            Assert.Equal("not reproduced by the minimizer", FailureMinimizer.BytesReport("stl test", "stl", data, "other", check, true, Inline()));
        }
    }
}
