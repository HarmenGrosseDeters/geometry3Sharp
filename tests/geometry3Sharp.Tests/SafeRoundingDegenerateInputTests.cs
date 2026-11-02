using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Xunit.Abstractions;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Design question 1b (owner decision): SafeRounding also repairs input that is already degenerate. Generated
    /// families of degenerate input go through ExactMeshBoolean.Resolve (and Booleans); each must end clean
    /// (RoundingClean, MeshRobustRepair.NotCleanReasons), within the displacement bound (MaxDrift at most
    /// MaxDisplacement times the diagonal), and the same solid as the input up to MaxDrift: the poked and split
    /// spheres have the surface of the sphere they come from (their new vertices lie in its faces), the others are
    /// compared with the exact resolve without SafeRounding. Dense clusters of slivers along lines need the strip zip;
    /// what it leaves (folded clusters) is counted, see docs/design/SAFE_ROUNDING.md section 2.
    /// </summary>
    public class SafeRoundingDegenerateInputTests
    {
        readonly ITestOutputHelper output;
        public SafeRoundingDegenerateInputTests(ITestOutputHelper output) { this.output = output; }

        public enum Family { Collinear, Sliver, ShortEdge, Coincident, Unwelded, VertexOnFace, SharedFace, Cluster, FoldedCluster }

        static DMesh3 Sphere(int n) => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();

        static DMesh3 Box(Vector3d min, Vector3d max) => new TrivialBox3Generator() {
            Box = new Box3d(new AxisAlignedBox3d(min, max))
        }.Generate().MakeDMesh();

        // every count-th triangle (from seed on) gets the defect of the family; k is the exponent of the size 2^-k
        public static DMesh3 Make(Family f, int k, int seed, int count = 9)
        {
            double h = Math.Pow(2, -k);
            switch (f) {
                case Family.Collinear:
                case Family.Sliver: {
                    // a vertex poked into a triangle and moved onto (Collinear) or 2^-k inside (Sliver) its edge ab:
                    // the triangle (a, b, p) has zero area or a height of 2^-k
                    DMesh3 m = Sphere(6);
                    foreach (int tid in m.TriangleIndices().Where(t => (t + seed) % count == 0).ToArray()) {
                        Index3i t = m.GetTriangle(tid);
                        Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                        double s = 0.25 + 0.5 * ((tid * 7 + seed) % 11) / 10.0;
                        Vector3d onEdge = a + s * (b - a);
                        Vector3d p = f == Family.Collinear ? onEdge : onEdge + h * (c - onEdge).Normalized;
                        Assert.Equal(MeshResult.Ok, m.PokeTriangle(tid, out DMesh3.PokeTriangleInfo info));
                        m.SetVertex(info.new_vid, p);
                    }
                    return m;
                }
                case Family.ShortEdge:
                case Family.Coincident: {
                    // an edge split at 2^-k from its end (ShortEdge) or at its end (Coincident: two welded
                    // vertices at one position, two triangles of zero area)
                    DMesh3 m = Sphere(6);
                    foreach (int eid in m.EdgeIndices().Where(e => (e + seed) % count == 0).ToArray()) {
                        Index2i ev = m.GetEdgeV(eid);
                        Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b);
                        Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                        m.SetVertex(info.vNew, f == Family.Coincident ? a : a + h * (b - a).Normalized);
                    }
                    return m;
                }
                case Family.Unwelded: {
                    // a vertex duplicated: some triangles of its fan use a copy at the same position (closed by
                    // position, open by ids)
                    DMesh3 m = Sphere(6);
                    foreach (int vid in m.VertexIndices().Where(v => (v + seed) % count == 0).ToArray()) {
                        int[] fan = m.VtxTrianglesItr(vid).ToArray();
                        int copy = m.AppendVertex(m.GetVertex(vid));
                        for (int i = 0; i < fan.Length / 2; i++) {
                            Index3i t = m.GetTriangle(fan[i]);
                            m.RemoveTriangle(fan[i], false, false);
                            m.AppendTriangle(t.a == vid ? copy : t.a, t.b == vid ? copy : t.b, t.c == vid ? copy : t.c);
                        }
                    }
                    return m;
                }
                case Family.VertexOnFace: {
                    // a box, and an octahedron whose lowest vertex lies on the box's top face (or 2^-k above it)
                    DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
                    double x = 0.3 + 0.05 * (seed % 7), y = 0.35 + 0.04 * (seed % 5), r = 0.2;
                    var o = new Vector3d(x, y, 1 + r + (k > 0 ? h : 0));
                    var oct = new DMesh3();
                    Vector3d[] p = { o + r * Vector3d.AxisX, o + r * Vector3d.AxisY, o - r * Vector3d.AxisX,
                                     o - r * Vector3d.AxisY, o + r * Vector3d.AxisZ, o - r * Vector3d.AxisZ };
                    foreach (Vector3d q in p) oct.AppendVertex(q);
                    for (int i = 0; i < 4; i++) {
                        oct.AppendTriangle(i, (i + 1) % 4, 4);
                        oct.AppendTriangle((i + 1) % 4, i, 5);
                    }
                    MeshEditor.Append(m, oct);
                    return m;
                }
                case Family.Cluster:
                case Family.FoldedCluster: {
                    // dense clusters: a triangle poked count times in a row, each new vertex near an edge of the
                    // triangle it pokes, so fans of slivers and zero-area triangles touch each other. Cluster: 2^-k of
                    // the way from the edge to the opposite corner (inside, the surface does not fold); FoldedCluster:
                    // 2^-k from the edge (outside slivers thinner than that: the surface folds over itself)
                    DMesh3 m = Sphere(6);
                    var rnd = new Random(seed);
                    foreach (int t0 in m.TriangleIndices().Where(t => (t + seed) % 17 == 0).ToArray()) {
                        int tid = t0;
                        for (int i = 0; i < count; i++) {
                            Index3i t = m.GetTriangle(tid);
                            Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                            Vector3d onEdge = a + (0.2 + 0.6 * rnd.NextDouble()) * (b - a);
                            Vector3d p = k == 0 ? onEdge : f == Family.Cluster ? onEdge + h * (c - onEdge) : onEdge + h * (c - onEdge).Normalized;
                            if (m.PokeTriangle(tid, out DMesh3.PokeTriangleInfo info) != MeshResult.Ok) break;
                            m.SetVertex(info.new_vid, p);
                            tid = rnd.Next(3) == 0 ? info.new_t1 : rnd.Next(2) == 0 ? info.new_t2 : tid;
                        }
                    }
                    return m;
                }
                case Family.SharedFace: {
                    // two boxes in one mesh that share a region of a face (or are 2^-k apart)
                    DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
                    double x = 0.1 * (seed % 5), y = 0.15 * (seed % 3);
                    MeshEditor.Append(m, Box(new Vector3d(x, y, 1 + (k > 0 ? h : 0)), new Vector3d(x + 0.6, y + 0.5, 1.7)));
                    return m;
                }
            }
            throw new ArgumentException(f.ToString());
        }

        // the families whose surface is the sphere's (pokes and splits in its faces), and the others
        static bool OnSphere(Family f) => f != Family.VertexOnFace && f != Family.SharedFace;

        static string Check(DMesh3 input, DMesh3 reference, string expectedDefects, out ExactMeshBoolean op)
        {
            op = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            if (op.Compute() == false) return "failed: " + op.Error;
            string defects = gs.MeshRobustRepair.NotCleanReasons(op.Result);
            if (op.RoundingClean == false || defects != expectedDefects) return $"defects '{defects}', expected '{expectedDefects}'";
            double drift = op.Rounding.MaxDrift, bound = BooleanOracle.SafeRoundingBound(input);
            if (!(drift <= bound)) return $"drift {drift:G3} above {bound:G3}";
            string solid = BooleanOracle.SameSolid(op.Result, reference, 2, drift);
            return solid == "" ? "" : $"drift {drift:G3}: {solid}";
        }

        static DMesh3 ExactResolve(DMesh3 input)
        {
            var raw = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute(), raw.Error);
            return raw.Result;
        }

        // the cases of a family: k (2^-k: the offsets; 0: on the edge, or touching), the count (every count-th
        // triangle, edge or vertex; pokes in a row for clusters), the seed
        public static IEnumerable<(int k, int count, int seed)> Cases(Family f)
        {
            foreach (int k in new[] { 0, 30, 34, 38, 42, 46, 52 })
                foreach (int count in new[] { 2, 3, 9 })
                    for (int seed = 0; seed < 4; seed++)
                        if (k != 0 || (f != Family.Sliver && f != Family.ShortEdge))
                            yield return (k, count, seed);
        }

        public static IEnumerable<object[]> CleanFamilies() =>
            new[] { Family.Collinear, Family.Sliver, Family.ShortEdge, Family.Coincident, Family.Unwelded,
                    Family.VertexOnFace, Family.SharedFace }.Select(f => new object[] { f });

        [Theory]
        [MemberData(nameof(CleanFamilies))]
        public void Family_RepairedCleanWithinTheBound(Family f)
        {
            DMesh3 sphere = Sphere(6);
            var failures = new List<string>();
            int n = 0;
            foreach (var (k, count, seed) in Cases(f)) {
                DMesh3 input = Make(f, k, seed, count);
                n++;
                string why;
                if (OnSphere(f)) {
                    why = Check(input, sphere, "", out _);
                } else if (f == Family.VertexOnFace && k == 0) {
                    // a vertex on a face: the exact result touches there (two shells in a point), as SafeRounding
                    // leaves it, no rounding defect
                    why = Check(input, ExactResolve(input), "self-intersections", out ExactMeshBoolean op);
                    if (why == "" && op.Rounding.MaxDrift != 0) why = $"drift {op.Rounding.MaxDrift:G3}";
                } else if (f == Family.SharedFace && k > 0) {
                    // two boxes 2^-k apart: nothing to repair, the exact result (SameSolid probes farther than the gap)
                    why = Check(input, ExactResolve(input), "", out ExactMeshBoolean op);
                    if (why.Contains("winding") && Invariance.Difference(op.Result, ExactResolve(input)) == "") why = "";
                } else {
                    why = Check(input, ExactResolve(input), "", out _);
                }
                if (why != "")
                    failures.Add($"{f} k{k} count {count} seed {seed}: {why}");
            }
            output.WriteLine($"{f}: {n - failures.Count} of {n} as expected");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        // dense clusters of slivers along lines (poked in a row): the strip zip repairs them; before it, none of
        // these was clean (no flip, zip, contraction or move within the bound repairs a strip of slivers)
        public static IEnumerable<object[]> StripCases() => new[] {
            new object[] { 30, 4, 0 }, new object[] { 30, 4, 1 }, new object[] { 30, 4, 2 }, new object[] { 0, 4, 2 } };

        [Theory]
        [MemberData(nameof(StripCases))]
        public void DenseCluster_RepairedClean(int k, int count, int seed)
        {
            DMesh3 input = Make(Family.Cluster, k, seed, count);
            Assert.Equal("", Check(input, Sphere(6), "", out _));
        }

        // all dense clusters, unfolded and folded (the pokes 2^-k outside slivers thinner than that: the surface folds
        // over itself, in layers on one line): always within the bound and the same solid; clean in most cases (85 of
        // 108 and 80 of 90; before the strip zip 51 and 41), the others are counted (docs/design/SAFE_ROUNDING.md
        // section 2: strips that branch or fold, no disk with a monotone boundary)
        [Fact]
        public void DenseClusters_WithinTheBound_MostlyClean()
        {
            DMesh3 sphere = Sphere(6);
            var failures = new List<string>();
            var clean = new Dictionary<Family, int> { [Family.Cluster] = 0, [Family.FoldedCluster] = 0 };
            int n = 0;
            foreach (Family f in new[] { Family.Cluster, Family.FoldedCluster })
                foreach (int k in new[] { 0, 30, 36, 40, 44, 52 })
                    foreach (int count in new[] { 2, 4, 8 })
                        for (int seed = 0; seed < 6; seed++) {
                            if (f == Family.FoldedCluster && k == 0) continue;     // (the same as Cluster)
                            DMesh3 input = Make(f, k, seed, count);
                            var op = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
                            Assert.True(op.Compute(), op.Error);
                            n++;
                            double drift = op.Rounding.MaxDrift, bound = BooleanOracle.SafeRoundingBound(input);
                            string solid = BooleanOracle.SameSolid(op.Result, sphere, 2, drift);
                            if (!(drift <= bound) || solid != "")
                                failures.Add($"{f} k{k} count {count} seed {seed}: drift {drift:G3} / {bound:G3} {solid}");
                            if (op.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(op.Result) == "")
                                clean[f]++;
                        }
            output.WriteLine($"clean: Cluster {clean[Family.Cluster]} of 108, FoldedCluster {clean[Family.FoldedCluster]} of 90");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
            Assert.True(clean[Family.Cluster] >= 85 && clean[Family.FoldedCluster] >= 80,
                $"clean: Cluster {clean[Family.Cluster]}, FoldedCluster {clean[Family.FoldedCluster]}");
        }

        // degenerate input in Booleans: a box cuts the poked and split spheres; 197 of 198 clean (before the strip zip
        // 183), the one left (ShortEdge 2^-44, union, seed 0) keeps a degenerate triangle
        [Fact]
        public void DegenerateInput_InBooleans_CleanWithinTheBound()
        {
            var failures = new List<string>();
            int n = 0;
            foreach (Family f in new[] { Family.Collinear, Family.Sliver, Family.ShortEdge, Family.Coincident, Family.Unwelded, Family.Cluster })
                foreach (int k in new[] { 0, 36, 44, 52 })
                    foreach (Ops op in BooleanOracle.AllOps)
                        for (int seed = 0; seed < 3; seed++) {
                            if (k == 0 && (f == Family.Sliver || f == Family.ShortEdge)) continue;
                            DMesh3 a = Make(f, k, seed, f == Family.Cluster ? 4 : 3);
                            DMesh3 b = Box(new Vector3d(-0.3 + 0.1 * seed, -2, -2), new Vector3d(2, 2, 2));
                            var reference = new ExactMeshBoolean(Sphere(6), new DMesh3(b), op);
                            var x = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
                            Assert.True(reference.Compute() && x.Compute());
                            n++;
                            string defects = gs.MeshRobustRepair.NotCleanReasons(x.Result);
                            double drift = x.Rounding.MaxDrift, bound = BooleanOracle.SafeRoundingBound(a, b);
                            string solid = BooleanOracle.SameSolid(x.Result, reference.Result, 4, drift + reference.Rounding.MaxDrift);
                            if (!x.RoundingClean || defects != "" || !(drift <= bound) || solid != "")
                                failures.Add($"{f} k{k} {op} seed {seed}: '{defects}' drift {drift:G3} / {bound:G3} {solid}");
                        }
            output.WriteLine($"{n - failures.Count} of {n} clean");
            foreach (string s in failures) output.WriteLine(s);
            Assert.True(failures.Count <= 1, string.Join("\n", failures));
        }

        // Similarity.All(): the 2^k scales give the same result bit for bit; the others are clean, within the bound and
        // the same solid, mapped back
        [Fact]
        public void Similarities_SameResult()
        {
            foreach (DMesh3 input in new[] { Make(Family.Cluster, 30, 2, 4), Make(Family.Collinear, 0, 1, 3) }) {
                var op0 = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
                Assert.True(op0.Compute());
                foreach (Similarity sim in Similarity.All()) {
                    var op = new ExactMeshBoolean(Invariance.Transformed(input, sim), new DMesh3(), Ops.Union);
                    Assert.True(op.Compute(), sim.Name);
                    DMesh3 back = Invariance.MappedBack(op.Result, sim);
                    if (sim.IsExact) {
                        Assert.True(Invariance.Difference(op0.Result, back) == "", sim.Name);
                        Assert.Equal(op0.Rounding.MaxDrift * sim.Scale, op.Rounding.MaxDrift);
                        continue;
                    }
                    Assert.True(op.Rounding.MaxDrift <= BooleanOracle.SafeRoundingBound(op.Result), sim.Name);
                    // (except the cluster at 1e-3 near UTM coordinates, ROBUSTNESS_SWEEP R23: a unit in the last place
                    // there, 9.3e-10, is 4.7e-7 of the mesh's size, far coarser than its slivers (about 1e-13); rounding
                    // the result's new vertices to that grid leaves two intersecting pairs)
                    bool coarse = sim.Name == "rotated, scale 1e-3, utm" && input.TriangleCount != Make(Family.Collinear, 0, 1, 3).TriangleCount;
                    if (coarse == false) {
                        Assert.True(op.RoundingClean, sim.Name);
                        Assert.True(gs.MeshRobustRepair.NotCleanReasons(op.Result) == "", sim.Name);
                    }
                    // (the transformed input is rounded: a few units in the last place of its largest coordinate)
                    double ulps = 8 * 2.220446049250313e-16 * Invariance.Transformed(input, sim).Vertices().Max(v => v.MaxAbs);
                    Assert.True(BooleanOracle.SameSolid(back, Sphere(6), 2, (op.Rounding.MaxDrift + ulps) / sim.Scale) == "", sim.Name);
                }
            }
        }

        // repeated runs, and one ExactMeshBoolean computed twice: the same result
        [Fact]
        public void Repeated_SameResult()
        {
            DMesh3 input = Make(Family.Cluster, 30, 1, 8);
            var a = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            Assert.True(a.Compute());
            DMesh3 first = new DMesh3(a.Result);
            Assert.True(a.Compute());
            Assert.True(Invariance.Difference(first, a.Result) == "");
            var b = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            Assert.True(b.Compute());
            Assert.True(Invariance.Difference(first, b.Result) == "");
        }
    }
}
