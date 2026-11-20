using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;
using Rep = geometry3Sharp.Tests.BooleanOracle.Representation;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The certifying checker of ExactMeshBoolean (Harness/BooleanCertificate.cs, docs/QA.md "Certifying checker of the
    /// exact boolean"): it certifies the results of the fixed boolean corpus (the box and tetrahedron families of
    /// ExactBooleanMetamorphicTests, their representation changes, spheres, nested and self-intersecting solids), and it
    /// has teeth: every mutation of the witness (winding numbers with a consistent selection, so that C4 does not see it
    /// and C3, which runs before C5, must), of the selection, of the culling, of the pieces and of the result is caught.
    /// </summary>
    public class BooleanCertificateTests
    {
        readonly ITestOutputHelper log;
        public BooleanCertificateTests(ITestOutputHelper log) { this.log = log; }

        static IntBox Bx(int x0, int y0, int z0, int x1, int y1, int z1) => new IntBox(x0, y0, z0, x1, y1, z1);
        static DMesh3 Box(IntBox b) => new BoxCase(b, b).MeshA();

        static DMesh3 Sphere(double r, Vector3d c, int n = 5)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Combined(params DMesh3[] parts)
        {
            var m = new DMesh3();
            foreach (DMesh3 p in parts) MeshEditor.Append(m, p);
            return m;
        }

        static DMesh3 Reversed(DMesh3 m) { var r = new DMesh3(m); r.ReverseOrientation(false); return r; }

        // runs and checks; Intersection without the early-out (the checker does not apply to culling against several meshes)
        static BooleanCertificate Certify(DMesh3 a, DMesh3 b, Ops op, out ExactMeshBoolean eb, Action<ExactMeshBoolean> configure = null)
        {
            return BooleanCertificate.Run(a, b, op, out eb, x => {
                if (op == Ops.Intersection) x.CullOutsideA = false;
                configure?.Invoke(x);
            });
        }

        static void AssertCertified(DMesh3 a, DMesh3 b, Ops op, string what, Action<ExactMeshBoolean> configure = null)
        {
            BooleanCertificate c = Certify(a, b, op, out _, configure);
            string r = c.Check();
            Assert.True(r == "", $"{what} {op}: {r}");
            Assert.True(c.Anchors >= 1 || c.PieceCorners.Length == 0, $"{what} {op}: no anchor");
            Assert.True(c.Uncovered == 0, $"{what} {op}: {c.Uncovered} triangles not covered within Delta");
        }

        // ---------------------------------------------------------------- the corpus

        public static IEnumerable<object[]> BoxFamilies() => ExactBooleanMetamorphicTests.Families.Keys.Select(k => new object[] { k });

        [Theory, MemberData(nameof(BoxFamilies))]
        public void Certifies_BoxFamilies_InExactFrames(string family)
        {
            var (a, b) = ExactBooleanMetamorphicTests.Families[family];
            foreach (BoxCase c in new[] { new BoxCase(a, b), new BoxCase(a, b, -6, new Vector3d(1024.5, -3.25, 0.0625)) })
                foreach (Ops op in BooleanOracle.AllOps)
                    AssertCertified(c.MeshA(), c.MeshB(), op, $"{family} {c}");
        }

        public static IEnumerable<object[]> TetFamilies() => ExactBooleanMetamorphicTests.TetFamilies.Keys.Select(k => new object[] { k });

        [Theory, MemberData(nameof(TetFamilies))]
        public void Certifies_TetrahedronFamilies_AndRepresentationChanges(string family)
        {
            var (ta, tb) = ExactBooleanMetamorphicTests.TetFamilies[family];
            foreach (Vector3d shift in new[] { Vector3d.Zero, new Vector3d(632117.25, 5712043.5, 113.75) }) {
                DMesh3 a = ta.Mesh(shift), b = tb.Mesh(shift);
                foreach (Ops op in BooleanOracle.AllOps)
                    AssertCertified(a, b, op, $"{family} shift {shift}");
            }
            DMesh3 a0 = ta.Mesh(Vector3d.Zero), b0 = tb.Mesh(Vector3d.Zero);
            foreach (Rep r in Enum.GetValues(typeof(Rep)).Cast<Rep>())
                foreach (Ops op in BooleanOracle.AllOps)
                    AssertCertified(BooleanOracle.Change(a0, r, 5), BooleanOracle.Change(b0, r, 6), op, $"{family} {r}");
        }

        public static IEnumerable<object[]> SolidCases() => Solids.Keys.Select(k => new object[] { k });

        static readonly Dictionary<string, Func<(DMesh3, DMesh3)>> Solids = new Dictionary<string, Func<(DMesh3, DMesh3)>> {
            ["spheres overlapping"] = () => (Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(0.7, 0.3, 0.1))),
            ["sphere nested"] = () => (Sphere(2, Vector3d.Zero), Sphere(0.5, new Vector3d(0.25, 0.125, 0))),
            // a hollow box (an inner shell reversed: winding number 0 in the cavity) and a box through the cavity
            ["hollow box"] = () => (Combined(Box(Bx(0, 0, 0, 6, 6, 6)), Reversed(Box(Bx(2, 2, 2, 4, 4, 4)))), Box(Bx(3, 1, 3, 5, 5, 5))),
            // a self-intersecting operand (two overlapping boxes in one mesh: winding number 2 in the overlap)
            ["self-intersecting"] = () => (Combined(Box(Bx(0, 0, 0, 3, 3, 3)), Box(Bx(1, 1, 1, 4, 4, 4))), Box(Bx(2, 0, 2, 5, 2, 5))),
            // a nested shell of the second operand inside the first (two components of B)
            ["two components"] = () => (Box(Bx(0, 0, 0, 6, 6, 6)), Combined(Box(Bx(1, 1, 1, 2, 2, 2)), Box(Bx(5, 5, 5, 8, 8, 8)))),
            // an operand with one triangle reversed (the boolean orients it again: ReorientedTriangles 1)
            ["one face flipped"] = () => { DMesh3 m = Box(Bx(0, 0, 0, 3, 3, 3)); m.ReverseTriOrientation(m.TriangleIndices().First()); return (m, Box(Bx(1, 1, 1, 4, 4, 4))); },
            // duplicated faces within one operand (coplanar copies of the same mesh)
            ["duplicate shell"] = () => (Combined(Box(Bx(0, 0, 0, 2, 2, 2)), Box(Bx(0, 0, 0, 2, 2, 2))), Box(Bx(1, 0, 0, 3, 2, 2))),
        };

        [Theory, MemberData(nameof(SolidCases))]
        public void Certifies_Solids(string name)
        {
            var (a, b) = Solids[name]();
            foreach (Ops op in BooleanOracle.AllOps) {
                // (AreaMajority: the case "one face flipped" certifies the reoriented operand; it changes nothing for the others)
                AssertCertified(a, b, op, name, x => x.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority);
                AssertCertified(a, b, op, name + " without SafeRounding", x => { x.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority; x.SafeRounding = false; });
                AssertCertified(a, b, op, name + " without SimplifyFlatRegions", x => { x.Orientation = ExactMeshBoolean.OrientationModes.AreaMajority; x.SimplifyFlatRegions = false; });
            }
        }

        [Fact]
        public void Certifies_WindingPredicate_SymmetricDifference()
        {
            var (a, b) = Solids["spheres overlapping"]();
            var eb = new ExactMeshBoolean(new[] { a, b }, w => (w[0] > 0) != (w[1] > 0)) { RecordCertificate = true, ComputeExactVertices = true };
            Assert.True(eb.Compute(), eb.Error);
            BooleanCertificate c = BooleanCertificate.From(eb);
            Assert.Equal("", c.Check());
        }

        [Fact]
        public void NotApplicable_ForOpenInputs_AndCullingAgainstSeveralMeshes()
        {
            DMesh3 open = Box(Bx(0, 0, 0, 2, 2, 2));
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.StartsWith("n/a: an input is open", BooleanCertificate.Run(open, Box(Bx(1, 1, 1, 3, 3, 3)), Ops.Union, out _).Check());
            // the early-out of an Intersection of closed meshes culls against each of them
            BooleanCertificate c = BooleanCertificate.Run(Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(0.7, 0.3, 0.1)), Ops.Intersection, out ExactMeshBoolean eb);
            Assert.True(eb.Arrangement.CulledCount > 0);
            Assert.StartsWith("n/a: triangles culled against several meshes", c.Check());
            // the early-out of a Difference culls against the first mesh only: checked, with the culled sheets' rule
            c = BooleanCertificate.Run(Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(0.7, 0.3, 0.1)), Ops.Difference, out eb);
            Assert.True(eb.Arrangement.CulledCount > 0);
            Assert.Equal(0, c.CullMesh);
            Assert.Equal("", c.Check());
        }

        // ---------------------------------------------------------------- similarities, reuse

        [Theory, MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Similarities_AreCertified_AndPowerOfTwoScalesGiveTheSameCertificate(string name)
        {
            Similarity s = Similarity.ByName(name);
            var (a0, b0) = Solids["spheres overlapping"]();
            DMesh3 Map(DMesh3 m) { var r = new DMesh3(m); foreach (int v in r.VertexIndices()) r.SetVertex(v, s.Apply(r.GetVertex(v))); return r; }
            DMesh3 a = Map(a0), b = Map(b0);
            foreach (Ops op in BooleanOracle.AllOps) {
                BooleanCertificate c = Certify(a, b, op, out _);
                string r = c.Check();
                Assert.True(r == "", $"{name} {op}: {r}");
                Assert.Equal(0, c.Uncovered);
                if (s.IsExact && s != Similarity.Identity) {
                    BooleanCertificate c0 = Certify(a0, b0, op, out _);
                    Assert.Equal("", c0.Check());
                    Assert.Equal(c0.PieceCorners.Length, c.PieceCorners.Length);
                    Assert.Equal(c0.Anchors, c.Anchors);
                    Assert.Equal(c0.CoverageTests, c.CoverageTests);
                    Assert.Equal(c0.Keep, c.Keep);
                    Assert.Equal(c0.Flip, c.Flip);
                }
            }
        }

        [Fact]
        public void Timing_IsLogged()
        {
            // (the last of three runs: the first includes the JIT)
            for (int run = 0; run < 3; ++run) TimeOnce(run == 2);
        }

        void TimeOnce(bool report)
        {
            var (a, b) = Solids["spheres overlapping"]();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BooleanCertificate c = Certify(a, b, Ops.Union, out var eb);
            long t0 = sw.ElapsedMilliseconds; sw.Restart();
            Assert.Equal("", c.CheckPieces()); long t1 = sw.ElapsedMilliseconds; sw.Restart();
            Assert.Equal("", c.CheckClassification()); long t2 = sw.ElapsedMilliseconds; sw.Restart();
            Assert.Equal("", c.CheckResult()); long t3 = sw.ElapsedMilliseconds;
            if (report) log.WriteLine($"pieces {c.PieceCorners.Length} boolean {t0} C1 {t1} C3/C4 {t2} C5 {t3} ms, anchors {c.Anchors}, coverage tests {c.CoverageTests}");
        }

        [Fact]
        public void Check_IsRepeatable_OnOneInstance()
        {
            var (a, b) = Solids["hollow box"]();
            BooleanCertificate c = Certify(a, b, Ops.Difference, out _);
            Assert.Equal("", c.Check());
            int anchors = c.Anchors, tests = c.CoverageTests;
            Assert.Equal("", c.Check());
            Assert.Equal(anchors, c.Anchors);
            Assert.Equal(tests, c.CoverageTests);
        }

        /// <summary>
        /// The heavier corpus (Nightly: the run is the check, about 13 s here): random sphere pairs (rotated, various
        /// resolutions), random integer tetrahedra and random small boxes (many shared planes), every operation, each
        /// certified with every triangle covered.
        /// </summary>
        [Fact]
        [Trait("Category", "Nightly")]
        public void Certifies_RandomSolids_Nightly()
        {
            var rng = new Random(20261117);
            int cases = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Vector3d Rand(double lo, double hi) => new Vector3d(lo + (hi - lo) * rng.NextDouble(), lo + (hi - lo) * rng.NextDouble(), lo + (hi - lo) * rng.NextDouble());
            void One(DMesh3 a, DMesh3 b, string what)
            {
                foreach (Ops op in BooleanOracle.AllOps) {
                    string r = BooleanCertificate.CheckBoolean(a, b, op);
                    Assert.True(r == "", $"{what}: {r}");
                    cases++;
                }
            }
            for (int k = 0; k < 40; ++k) {
                DMesh3 a = Sphere(0.5 + rng.NextDouble(), Vector3d.Zero, 4 + rng.Next(4));
                DMesh3 b = Sphere(0.5 + rng.NextDouble(), Rand(-1, 1), 4 + rng.Next(4));
                Matrix3d rot = Matrix3d.AxisAngleD(Rand(-1, 1).Normalized, 360 * rng.NextDouble());
                foreach (int v in b.VertexIndices()) b.SetVertex(v, rot * b.GetVertex(v));
                One(a, b, $"spheres {k}");
            }
            for (int k = 0; k < 60; ++k) {
                Vector3i P() => new Vector3i(rng.Next(9), rng.Next(9), rng.Next(9));
                IntTet ta, tb;
                try { ta = new IntTet(P(), P(), P(), P()); tb = new IntTet(P(), P(), P(), P()); } catch (ArgumentException) { continue; }
                One(ta.Mesh(Vector3d.Zero), tb.Mesh(Vector3d.Zero), $"tets {k} {ta} {tb}");
            }
            for (int k = 0; k < 60; ++k) {
                (int, int) I() { int lo = rng.Next(4); return (lo, lo + 1 + rng.Next(4 - lo)); }
                var (x0, x1) = I(); var (y0, y1) = I(); var (z0, z1) = I();
                var (u0, u1) = I(); var (v0, v1) = I(); var (w0, w1) = I();
                One(Box(Bx(x0, y0, z0, x1, y1, z1)), Box(Bx(u0, v0, w0, u1, v1, w1)), $"boxes {k}");
            }
            log.WriteLine($"{cases} booleans certified in {sw.ElapsedMilliseconds} ms");
        }

        // ---------------------------------------------------------------- teeth: mutations

        // pieces of one mesh joined across edges where exactly two classified pieces meet (the patches; the test's own)
        static List<List<int>> Patches(BooleanCertificate c, ExactMeshBoolean eb)
        {
            var output = eb.Arrangement.Output;
            int n = output.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            var atEdge = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < n; ++i) {
                if (c.Front[i] == null) continue;
                int[] v = { output[i].A, output[i].B, output[i].C };
                for (int k = 0; k < 3; ++k) {
                    var e = (Math.Min(v[k], v[(k + 1) % 3]), Math.Max(v[k], v[(k + 1) % 3]));
                    if (atEdge.TryGetValue(e, out var l) == false) atEdge[e] = l = new List<int>();
                    l.Add(i);
                }
            }
            foreach (var l in atEdge.Values)
                if (l.Count == 2 && output[l[0]].Mesh == output[l[1]].Mesh) parent[Find(l[0])] = Find(l[1]);
            return Enumerable.Range(0, n).Where(i => c.Front[i] != null).GroupBy(Find).Select(g => g.ToList()).ToList();
        }

        static bool IsC3Failure(string r) => r.StartsWith("C3/C4: ") && (r.Contains("wedge") || r.Contains("exact ray") || r.Contains("half-plane"));
        static bool IsC4Failure(string r) => r.StartsWith("C3/C4: ") && (r.Contains("keep") || r.Contains("flip") || r.Contains("coplanar copy"));

        // the mutation must make the check fail with a failure of the kind given
        void Caught(string what, ExactMeshBoolean eb, Action<BooleanCertificate> mutate, Func<string, bool> kind)
        {
            BooleanCertificate c = BooleanCertificate.From(eb);
            mutate(c);
            string r = c.Check();
            log.WriteLine($"{what}: {r}");
            Assert.True(kind(r), $"{what}: not caught as expected: '{r}'");
        }

        [Fact]
        public void WitnessMutations_WithConsistentSelection_AreCaughtByC3()
        {
            int mutations = 0, equivalent = 0;
            foreach (var (name, op) in new[] { ("spheres overlapping", Ops.Union), ("spheres overlapping", Ops.Difference), ("hollow box", Ops.Difference),
                ("self-intersecting", Ops.Union), ("two components", Ops.Intersection) }) {
                var (a, b) = Solids[name]();
                Certify(a, b, op, out ExactMeshBoolean eb);
                BooleanCertificate probe = BooleanCertificate.From(eb);
                Assert.Equal("", probe.Check());
                List<List<int>> patches = Patches(probe, eb);
                int nm = probe.Meshes.Count;
                for (int p = 0; p < patches.Count; ++p)
                    for (int m = 0; m < nm; ++m)
                        foreach (int delta in new[] { 1, -1 }) {
                            // (an equivalent mutant: with the early-out, the winding numbers of the other meshes do not
                            // matter where the culling mesh's is <= 0 on both sides; the selection is the same)
                            int cm = probe.CullMesh;
                            if (cm >= 0 && m != cm && patches[p].All(i => probe.Front[i][cm] <= 0 && probe.Back(i)[cm] <= 0)) {
                                equivalent++;
                                continue;
                            }
                            // one patch's winding number of one mesh off by one (the selection made to agree with it)
                            int pp = p, mm = m;
                            Caught($"{name} {op}: patch {p} mesh {m} {delta:+0;-0}", eb, c => {
                                foreach (int i in patches[pp]) c.Front[i][mm] += delta;
                                c.SelectFromWitness();
                            }, IsC3Failure);
                            mutations++;
                        }
                // two patches (consecutive in piece order, often neighbours), their winding numbers swapped (what a wrong
                // radial order in a propagation gives)
                for (int p = 0; p + 1 < patches.Count; ++p) {
                    int q = p + 1;
                    if (patches[p].Select(i => probe.Front[i]).First().SequenceEqual(probe.Front[patches[q][0]])) continue;
                    int pp = p;
                    Caught($"{name} {op}: patches {p} and {q} swapped", eb, c => {
                        int[] wp = c.Front[patches[pp][0]], wq = c.Front[patches[q][0]];
                        foreach (int i in patches[pp]) c.Front[i] = (int[])wq.Clone();
                        foreach (int i in patches[q]) c.Front[i] = (int[])wp.Clone();
                        c.SelectFromWitness();
                    }, IsC3Failure);
                    mutations++;
                }
            }
            log.WriteLine($"{mutations} witness mutations caught, {equivalent} equivalent ones skipped");
            Assert.True(mutations >= 50, $"only {mutations} mutations");
        }

        [Fact]
        public void NestedComponentWithoutItsAnchor_IsCaughtOnlyByTheAnchorRay()
        {
            // B (a box inside A, its own connected set of pieces) read as if it were outside A: every winding number of
            // A on its pieces one less. Locally consistent (the wedges of B's pieces agree), only its anchor sees it.
            var (a, b) = (Box(Bx(0, 0, 0, 6, 6, 6)), Box(Bx(1, 1, 1, 2, 2, 2)));
            foreach (Ops op in BooleanOracle.AllOps) {
                Certify(a, b, op, out ExactMeshBoolean eb);
                Assert.Equal("", BooleanCertificate.From(eb).Check());
                Caught($"nested {op}", eb, c => {
                    for (int i = 0; i < c.Front.Length; ++i)
                        if (c.Front[i] != null && c.PieceMesh[i] == 1) c.Front[i][0] -= 1;
                    c.SelectFromWitness();
                }, r => r.StartsWith("C3/C4: ") && r.Contains("exact ray"));
            }
        }

        [Fact]
        public void SelectionMutations_AreCaughtByC4()
        {
            int mutations = 0;
            foreach (var (name, op) in new[] { ("spheres overlapping", Ops.Union), ("hollow box", Ops.Difference), ("duplicate shell", Ops.Intersection) }) {
                var (a, b) = Solids[name]();
                Certify(a, b, op, out ExactMeshBoolean eb);
                BooleanCertificate probe = BooleanCertificate.From(eb);
                Assert.Equal("", probe.Check());
                int n = probe.PieceCorners.Length;
                for (int i = 0; i < n; i += Math.Max(1, n / 25)) {
                    int ii = i;
                    if (probe.IsClassified(i)) {
                        Caught($"{name} {op}: keep of {i} toggled", eb, c => c.Keep[ii] = !c.Keep[ii], IsC4Failure);
                        mutations++;
                        if (probe.Keep[i]) {
                            Caught($"{name} {op}: flip of {i} toggled", eb, c => c.Flip[ii] = !c.Flip[ii], IsC4Failure);
                            mutations++;
                        }
                    }
                }
                // a coplanar copy kept (the cases with coincident faces)
                for (int i = 0; i < n; ++i)
                    if (probe.IsClassified(i) == false) {
                        int ii = i;
                        Caught($"{name} {op}: copy {i} kept", eb, c => c.Keep[ii] = true, IsC4Failure);
                        mutations++;
                        break;
                    }
            }
            // A and B swapped in a difference: the selection of A - B judged as B - A
            {
                var (a, b) = Solids["spheres overlapping"]();
                Certify(a, b, Ops.Difference, out ExactMeshBoolean eb);
                Caught("difference with A and B swapped", eb, c => c.Predicate = w => w[1] > 0 && w[0] <= 0, IsC4Failure);
                mutations++;
            }
            log.WriteLine($"{mutations} selection mutations caught");
            Assert.True(mutations >= 20, $"only {mutations} mutations");
        }

        [Fact]
        public void PieceMutations_AreCaughtByC1()
        {
            var (a, b) = Solids["spheres overlapping"]();
            Certify(a, b, Ops.Union, out ExactMeshBoolean eb);
            BooleanCertificate probe = BooleanCertificate.From(eb);
            Assert.Equal("", probe.Check());
            int n = probe.PieceCorners.Length;
            Func<string, bool> c1 = r => r.StartsWith("C1: ");
            // a split source triangle (several pieces) and an unsplit one
            int split = Enumerable.Range(0, n).First(i => Enumerable.Range(0, n).Count(j => probe.PieceSource[j] == probe.PieceSource[i]) >= 3);
            int other = Enumerable.Range(0, n).First(j => probe.PieceSource[j] == probe.PieceSource[split] && j != split);
            Caught("piece reversed", eb, c => { var t = c.PieceCorners[split]; c.PieceCorners[split] = new[] { t[0], t[2], t[1] }; }, c1);
            Caught("piece corner moved to a neighbour's corner", eb, c => {
                ExactPoint3 x = c.PieceCorners[other].First(p => c.PieceCorners[split].Contains(p) == false);
                c.PieceCorners[split] = new[] { x, c.PieceCorners[split][1], c.PieceCorners[split][2] };
            }, c1);
            Caught("piece given to another source", eb, c => c.PieceSource[split] = Enumerable.Range(0, c.TriMesh.Length).First(t => t != c.PieceSource[split] && c.TriMesh[t] == c.PieceMesh[split]), c1);
            Caught("two pieces of a source merged into one (a corner of one replaced, the other left out)", eb, c => {
                // the second piece duplicates the first: the source's area is not tiled once
                c.PieceCorners[other] = (ExactPoint3[])c.PieceCorners[split].Clone();
            }, c1);
            Caught("corner moved off the plane", eb, c => {
                ExactPoint3 p = c.PieceCorners[split][0];
                c.PieceCorners[split][0] = ExactPoint3.Create(p.X * 1024 + 1, p.Y * 1024, p.Z * 1024, p.W * 1024);
            }, c1);
        }

        // the unit triangle (0,0,0), (4,0,0), (0,4,0) and points of its plane on the integer grid
        static ExactPoint3 G(long x, long y, long z = 0) => ExactPoint3.FromInteger(x, y, z);
        static readonly ExactPoint3[] Src = { G(0, 0), G(4, 0), G(0, 4) };

        [Fact]
        public void CheckPiece_RejectsEachCondition()
        {
            Assert.Equal("", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(2, 0), G(0, 2) }));
            Assert.Equal("", BooleanCertificate.CheckPiece(Src, Src));
            Assert.Equal("degenerate", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(1, 0), G(2, 0) }));
            // (projected into the source, but one corner above its plane)
            Assert.StartsWith("corner 2 off its source's plane", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(2, 0), G(0, 2, 1) }));
            Assert.StartsWith("corner 1 outside its source", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(5, 0), G(0, 2) }));
            Assert.StartsWith("corner 1 outside its source", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(3, 2), G(0, 2) }));
            Assert.Equal("oriented against its source", BooleanCertificate.CheckPiece(Src, new[] { G(0, 0), G(0, 2), G(2, 0) }));
        }

        [Fact]
        public void CheckTiling_RejectsEachCondition()
        {
            ExactPoint3 m1 = G(2, 0), m2 = G(2, 2), m3 = G(0, 2), c = G(1, 1);
            // the source split at the midpoints of two edges, and a fan around an inner point
            var split = new List<ExactPoint3[]> { new[] { G(0, 0), m1, G(0, 4) }, new[] { m1, G(4, 0), m2 }, new[] { m1, m2, G(0, 4) } };
            Assert.Equal("", BooleanCertificate.CheckTiling(Src, split));
            var fan = new List<ExactPoint3[]> { new[] { G(0, 0), G(4, 0), c }, new[] { G(4, 0), G(0, 4), c }, new[] { G(0, 4), G(0, 0), c } };
            Assert.Equal("", BooleanCertificate.CheckTiling(Src, fan));
            // a piece twice
            Assert.Equal("directed edge used twice", BooleanCertificate.CheckTiling(Src, split.Concat(new[] { split[0] }).ToList()));
            // the source covered twice, without a repeated directed edge: two tilings with different boundary splits
            var other = new List<ExactPoint3[]> { new[] { G(0, 0), G(4, 0), m3 }, new[] { m3, G(4, 0), G(0, 4) } };
            Assert.Equal("the pieces' areas do not add up to the source's", BooleanCertificate.CheckTiling(Src, split.Concat(other).ToList()));
            // a gap: less area
            Assert.Equal("the pieces' areas do not add up to the source's", BooleanCertificate.CheckTiling(Src, split.Take(2).ToList()));
            // a gap and an overlap of the same area: the area is right, the gap's edges are unpaired inside the source
            var overlap = new List<ExactPoint3[]> { split[0], split[1], new[] { c, G(3, 1), G(1, 3) } };
            Assert.Equal("an unpaired edge is not on the source's boundary", BooleanCertificate.CheckTiling(Src, overlap));
        }

        [Fact]
        public void WronglyCulledComponent_IsCaught()
        {
            // B inside A, Difference: as if the early-out had culled all of B (its pieces gone, the selection made to
            // agree). Locally everything is consistent (A's pieces see w_B = 0, which is true at A's surface); only the
            // check of the culled triangles sees it
            DMesh3 a = Box(Bx(0, 0, 0, 6, 6, 6)), b = Box(Bx(1, 1, 1, 2, 2, 2));
            Certify(a, b, Ops.Difference, out ExactMeshBoolean eb);
            Assert.Equal("", BooleanCertificate.From(eb).Check());
            BooleanCertificate c = BooleanCertificate.From(eb);
            int[] keep = Enumerable.Range(0, c.PieceMesh.Length).Where(i => c.PieceMesh[i] == 0).ToArray();
            c.PieceMesh = keep.Select(i => c.PieceMesh[i]).ToArray();
            c.PieceSource = keep.Select(i => c.PieceSource[i]).ToArray();
            c.PieceCorners = keep.Select(i => c.PieceCorners[i]).ToArray();
            c.Front = keep.Select(i => c.Front[i]).ToArray();
            c.Keep = new bool[keep.Length]; c.Flip = new bool[keep.Length];
            for (int t = 0; t < c.TriMesh.Length; ++t) if (c.TriMesh[t] == 1) c.TriCulled[t] = true;
            c.CullMesh = 0;
            c.SelectFromWitness();
            Assert.Equal("", c.CheckPieces());
            Assert.Equal("", c.CheckClassification());
            string r = c.Check();
            Assert.True(r.StartsWith("C0: ") && r.Contains("inside the culling mesh"), r);
            // and a culled triangle that touches the culling mesh
            var (sa, sb) = Solids["spheres overlapping"]();
            Certify(sa, sb, Ops.Difference, out eb);
            c = BooleanCertificate.From(eb);
            Assert.True(c.TriCulled.Any(x => x));
            int crossing = Enumerable.Range(0, c.TriMesh.Length).First(t => c.TriMesh[t] == 1 && c.TriCulled[t] == false
                && c.PieceSource.Count(s => s == t) > 1);
            c.TriCulled[crossing] = true;
            Assert.Contains("meets the culling mesh", c.CheckInputs());
        }

        [Fact]
        public void RemovedComponents_OnlyTinyOrThinOnesAreExempt()
        {
            // a tiny box next to a unit sphere, left out of the result as the rounding's removal of tiny components does
            // (it removes only those that keep a sliver, so the case is made by hand): the certificate holds
            DMesh3 tiny = new BoxCase(Bx(0, 0, 0, 1, 1, 1), Bx(0, 0, 0, 1, 1, 1), -17, new Vector3d(2, 0, 0)).MeshA();
            BooleanCertificate c = Certify(Sphere(1, Vector3d.Zero), tiny, Ops.Union, out ExactMeshBoolean eb);
            Assert.Equal("", c.Check());
            Assert.Equal(0, c.ExemptComponents);
            c = BooleanCertificate.From(eb);
            c.Result = new DMesh3(c.Result, false, MeshComponents.None);
            foreach (int t in c.Result.TriangleIndices().ToList())
                if (c.Result.GetTriCentroid(t).x > 1.5) c.Result.RemoveTriangle(t, true, false);
            Assert.Contains("farther than", c.Check());
            c.RemovedComponents = 1;
            Assert.Equal("", c.Check());
            Assert.Equal(1, c.ExemptComponents);
            // two spheres apart: one of them dropped from the result, with a removed component reported: still caught
            c = Certify(Sphere(1, Vector3d.Zero), Sphere(1, new Vector3d(3, 0, 0)), Ops.Union, out eb);
            Assert.Equal("", c.Check());
            c = BooleanCertificate.From(eb);
            c.Result = new DMesh3(c.Result, false, MeshComponents.None);
            foreach (int t in c.Result.TriangleIndices().ToList())
                if (c.Result.GetTriCentroid(t).x > 1.5) c.Result.RemoveTriangle(t, true, false);
            c.RemovedComponents = 1;
            string r = c.Check();
            Assert.True(r.StartsWith("C5: ") && r.Contains("farther than"), r);
        }

        [Fact]
        public void InputAndBoundMutations_AreCaughtByC0()
        {
            var (a, b) = Solids["hollow box"]();
            Certify(a, b, Ops.Union, out ExactMeshBoolean eb);
            BooleanCertificate Fresh() => BooleanCertificate.From(eb);
            Assert.Equal("", Fresh().Check());
            BooleanCertificate m = Fresh();
            m.Inputs = new List<DMesh3> { Reversed(a), b };
            Assert.Contains("reversed", m.CheckInputs());
            // the boolean's mesh with the cavity's shell turned (a consistently oriented component reversed), reported
            m = Fresh();
            DMesh3 turned = new DMesh3(m.Meshes[0]);
            foreach (int t in turned.TriangleIndices().Where(t => t >= 12)) turned.ReverseTriOrientation(t);
            m.Meshes = new List<DMesh3> { turned, m.Meshes[1] };
            m.ReorientedTriangles = 12;
            Assert.Contains("reversed in a consistently oriented component", m.CheckInputs());
            m = Fresh();
            m.TriTid[1] = m.TriTid[0];
            m.TriMesh[1] = m.TriMesh[0];
            Assert.Contains("twice in the arrangement", m.CheckInputs());
            m = Fresh();
            int last = m.TriMesh.Length - 1;
            m.TriMesh = m.TriMesh.Take(last).ToArray(); m.TriTid = m.TriTid.Take(last).ToArray(); m.TriCulled = m.TriCulled.Take(last).ToArray();
            Assert.Contains("is not in the arrangement", m.CheckInputs());
            m = Fresh();
            m.MaxLength = 2 * m.MaxLengthBound + 1;
            Assert.Contains("above the documented", m.CheckInputs());
        }

        [Fact]
        public void RecordingChangesNothing_AndIsClearedWhenOff()
        {
            var (a, b) = Solids["self-intersecting"]();
            foreach (Ops op in BooleanOracle.AllOps) {
                var plain = new ExactMeshBoolean(a, b, op);
                Assert.True(plain.Compute());
                var rec = new ExactMeshBoolean(a, b, op) { RecordCertificate = true };
                Assert.True(rec.Compute());
                Assert.Equal(plain.Result.TriangleCount, rec.Result.TriangleCount);
                Assert.Equal(plain.Result.VertexCount, rec.Result.VertexCount);
                foreach (int v in plain.Result.VertexIndices()) Assert.Equal(plain.Result.GetVertex(v), rec.Result.GetVertex(v));
                foreach (int t in plain.Result.TriangleIndices()) Assert.Equal(plain.Result.GetTriangle(t), rec.Result.GetTriangle(t));
                Assert.NotNull(rec.CertificateFront); Assert.NotNull(rec.CertificateKeep); Assert.NotNull(rec.CertificateFlip); Assert.NotNull(rec.CertificateAssembled);
                Assert.Equal(rec.Arrangement.Output.Count, rec.CertificateFront.Length);
                rec.RecordCertificate = false;
                Assert.True(rec.Compute());
                Assert.Null(rec.CertificateFront); Assert.Null(rec.CertificateKeep); Assert.Null(rec.CertificateFlip); Assert.Null(rec.CertificateAssembled);
            }
        }

        [Fact]
        public void Coverage_OfDegenerateTriangles_IsTheDistanceToTheirEdges()
        {
            ExactPoint3 o = G(0, 0), x = G(4, 0);
            Assert.True(BooleanCertificate.PointNearTriangle(G(2, 1), new[] { o, o, x }, 1.0));
            Assert.False(BooleanCertificate.PointNearTriangle(G(2, 2), new[] { o, o, x }, 1.0));
            Assert.False(BooleanCertificate.PointNearTriangle(G(2, 2), new[] { o, G(2, 0), x }, 1.0));
            Assert.True(BooleanCertificate.PointNearTriangle(G(6, 0), new[] { o, G(2, 0), x }, 2.0));
            // (beyond the end of the segments: the distance to the end point, not to the line)
            Assert.False(BooleanCertificate.PointNearTriangle(G(7, 1), new[] { o, G(2, 0), x }, 2.0));
            Assert.False(BooleanCertificate.PointNearTriangle(G(7, 0), new[] { x, x, x }, 2.0));
            Assert.True(BooleanCertificate.PointNearTriangle(G(1, 1, 1), new[] { o, x, G(0, 4) }, 1.0));
            Assert.False(BooleanCertificate.PointNearTriangle(G(1, 1, 2), new[] { o, x, G(0, 4) }, 1.0));
        }

        [Fact]
        public void ResultMutations_AreCaughtByC5()
        {
            var (a, b) = Solids["spheres overlapping"]();
            Certify(a, b, Ops.Difference, out ExactMeshBoolean eb);
            Assert.Equal("", BooleanCertificate.From(eb).Check());
            Func<string, bool> c5 = r => r.StartsWith("C5: ");
            DMesh3 Copy(DMesh3 m) => new DMesh3(m, false, MeshComponents.None);
            int t0 = eb.Result.TriangleIndices().First();
            Caught("result triangle removed", eb, c => { c.Result = Copy(c.Result); c.Result.RemoveTriangle(t0, false, false); }, c5);
            Caught("result triangle reversed", eb, c => { c.Result = Copy(c.Result); c.Result.ReverseTriOrientation(t0); }, c5);
            Caught("result vertex moved by 10 Delta", eb, c => {
                c.Result = Copy(c.Result);
                int v = c.Result.VertexIndices().First();
                c.Result.SetVertex(v, c.Result.GetVertex(v) + new Vector3d(10 * c.Delta, 0, 0));
            }, c5);
            {
                // (a triangle and its reverse on top of the result before rounding: the chains are equal, the areas are not)
                BooleanCertificate sheet = BooleanCertificate.From(eb);
                sheet.Assembled = Copy(sheet.Assembled);
                Index3i t = sheet.Assembled.GetTriangle(sheet.Assembled.TriangleIndices().First());
                // (vertex copies, so that the mesh accepts the reversed triangle next to the original)
                int va = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.a)), vb = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.b)), vc = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.c));
                int vd = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.a)), ve = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.b)), vf = sheet.Assembled.AppendVertex(sheet.Assembled.GetVertex(t.c));
                sheet.Assembled.AppendTriangle(va, vb, vc); sheet.Assembled.AppendTriangle(vd, vf, ve);
                var ex = sheet.AssembledExact.ToList();
                while (ex.Count < sheet.Assembled.MaxVertexID) ex.Add(default(ExactPoint3));
                ex[va] = ex[vd] = sheet.AssembledExact[t.a]; ex[vb] = ex[ve] = sheet.AssembledExact[t.b]; ex[vc] = ex[vf] = sheet.AssembledExact[t.c];
                sheet.AssembledExact = ex.ToArray();
                Assert.Contains("another area than the kept pieces", sheet.CheckResult());
            }
            Caught("result before rounding: a triangle removed", eb, c => {
                c.Assembled = Copy(c.Assembled); c.Assembled.RemoveTriangle(c.Assembled.TriangleIndices().First(), false, false);
            }, c5);
            Caught("result before rounding: an exact vertex moved by 2^-30", eb, c => {
                c.AssembledExact = (ExactPoint3[])c.AssembledExact.Clone();
                int v = c.Assembled.VertexIndices().First();
                ExactPoint3 p = c.AssembledExact[v];
                c.AssembledExact[v] = ExactPoint3.Create(p.X * (BigIntegerOne << 30) + p.W, p.Y * (BigIntegerOne << 30), p.Z * (BigIntegerOne << 30), p.W * (BigIntegerOne << 30));
            }, c5);
            // the chain test on its own (C4 would see this first): a kept piece left out of the selection is not E_s
            BooleanCertificate d = BooleanCertificate.From(eb);
            d.Keep[Array.IndexOf(d.Keep, true)] = false;
            Assert.Contains("differs from the kept pieces", d.CheckResult());
        }

        static readonly System.Numerics.BigInteger BigIntegerOne = System.Numerics.BigInteger.One;
    }
}
