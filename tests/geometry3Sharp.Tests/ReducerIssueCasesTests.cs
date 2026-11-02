using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for simplification (Reducer) from the issue trackers of other libraries (docs/QA.md 2.11): only the
    /// failure mode, rebuilt here from simple primitives. No code, test code or meshes of these projects are used (CGAL
    /// is GPL: only the prose of its issues was read). The issue URL is in the comment of each test.
    ///
    /// Every case checks the result itself (valid DMesh3, finite, closed where the input is, no folds, bounds, volume,
    /// topology). A case we fail is listed in KnownFailures with its finding (strict: a listed case that passes fails,
    /// so a fix removes the entry; while listed, the test also checks which failure it is).
    /// </summary>
    public class ReducerIssueCasesTests
    {
        /// <summary>
        /// case name -> (finding, the exact failure while listed: the problems joined by "; ", in the order the test
        /// reports them)
        /// </summary>
        public static readonly Dictionary<string, (string Finding, string Pinned)> KnownFailures = new Dictionary<string, (string, string)> {
            // The flip test of a collapse compares each new triangle normal with the triangle's current normal (dot > 0,
            // EdgeFlipTolerance), not with the input surface: a chain of collapses turns a normal by up to 90 degrees per
            // step. On a noisy terrain, interior triangles end up facing down, and at the open border an interior vertex
            // collapsed onto a boundary vertex leaves a triangle on three consecutive boundary vertices standing vertical
            // (it passes: its new normal is tilted towards the old one). EdgeFlipTolerance 0.7 still leaves 0, 1, 2 (seeds 1-3).
            [nameof(NoisyTerrain_ReducedTo20Percent_NoFoldOver) + "_1"] = (FindingFold, "1 folded triangles"),
            [nameof(NoisyTerrain_ReducedTo20Percent_NoFoldOver) + "_2"] = (FindingFold, "4 folded triangles"),
            [nameof(NoisyTerrain_ReducedTo20Percent_NoFoldOver) + "_3"] = (FindingFold, "2 folded triangles"),
            [nameof(SmallPatch_ReducedTo10_NoDuplicateTriangles) + "_2"] = (FindingFold, "1 folded triangles"),
        };
        const string FindingFold = "finding S1: collapse chains fold the surface (flip test against the current, not the input normals)";

        static void Expect(string name, List<string> problems)
        {
            string failure = string.Join("; ", problems);
            if (KnownFailures.TryGetValue(name, out var known)) {
                Assert.True(failure != "", $"{name} passes now: remove its entry ({known.Finding}) from KnownFailures and docs/QA.md 2.11");
                Assert.True(failure == known.Pinned, $"{name} fails differently than pinned ('{known.Pinned}'): {failure}");
            } else
                Assert.True(failure == "", name + ": " + failure);
        }

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(ReducerIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                Assert.True(names.Any(n => kv.Key == n || kv.Key.StartsWith(n + "_")), kv.Key + ": no such case");
                Assert.False(string.IsNullOrWhiteSpace(kv.Value.Finding));
                Assert.False(string.IsNullOrWhiteSpace(kv.Value.Pinned));
            }
        }

        /// <summary>
        /// x rounded to a multiple of 2^-30: inputs built with Math.Sin / Cos (whose last bit may differ between net48 and
        /// net10) are then the same on both, so a pinned failure is the same too
        /// </summary>
        internal static double R(double x) => Math.Round(x * (1 << 30)) / (1 << 30);

        // ---- inputs (procedural) ----

        /// <summary> an open height field over [0,nx] x [0,ny] (unit cells, two triangles each, normals +z) </summary>
        internal static DMesh3 Grid(int nx, int ny, Func<int, int, double> z, Vector3d offset = default)
        {
            var m = new DMesh3();
            int[,] vid = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    vid[i, j] = m.AppendVertex(offset + new Vector3d(i, j, z(i, j)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            return m;
        }

        /// <summary> a closed torus around the z axis: major radius R, minor r, nu x nv segments, outward </summary>
        internal static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            int[,] vid = new int[nu, nv];
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    vid[i, j] = m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int i1 = (i + 1) % nu, j1 = (j + 1) % nv;
                    m.AppendTriangle(vid[i, j], vid[i1, j], vid[i1, j1]);
                    m.AppendTriangle(vid[i, j], vid[i1, j1], vid[i, j1]);
                }
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        /// <summary> an axis-aligned box, 12 triangles, outward </summary>
        internal static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        internal static DMesh3 Sphere(Vector3d c, double r, int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 GridBox(Vector3d lo, Vector3d hi, int edgeVertices)
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(0.5 * (lo + hi), 0.5 * (hi - lo)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();
            Assert.True(m.IsClosed() && ResultChecks.Volume(m) > 0);
            return m;
        }

        static DMesh3 Cylinder(Vector3d c, double r, double h, int slices)
        {
            DMesh3 m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 Append(params DMesh3[] meshes)
        {
            var m = new DMesh3();
            foreach (DMesh3 x in meshes) new MeshEditor(m).AppendMesh(x);
            return m;
        }

        // ---- checks ----

        static DMesh3 Reduced(DMesh3 input, Action<Reducer> reduce)
        {
            DMesh3 m = new DMesh3(input);
            reduce(new Reducer(m));
            return m;
        }

        /// <summary> valid (bowties allowed if allowBowties), finite, closed if the input was </summary>
        static void CheckBasic(DMesh3 result, DMesh3 input, List<string> r, bool allowBowties = false)
        {
            if (!result.CheckValidity(allowBowties, FailMode.ReturnOnly)) r.Add("invalid DMesh3");
            if (!result.Vertices().All(v => v.IsFinite)) r.Add("non-finite coordinates");
            if (result.TriangleCount == 0) r.Add("empty result");
            if (input.IsClosed() && !result.IsClosed()) r.Add("not closed");
            int dup = DuplicateTriangles(result);
            if (dup > 0) r.Add($"{dup} duplicate triangles");
        }

        /// <summary> pairs of triangles on the same three vertices (either winding) </summary>
        static int DuplicateTriangles(DMesh3 m)
        {
            var seen = new HashSet<(int, int, int)>();
            int n = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                int[] s = { tv.a, tv.b, tv.c };
                Array.Sort(s);
                if (!seen.Add((s[0], s[1], s[2]))) n++;
            }
            return n;
        }

        /// <summary> triangles of a height field whose normal does not point up (folded over, or flat on edge) </summary>
        static int DownwardTriangles(DMesh3 m) => m.TriangleIndices().Count(t => !(m.GetTriNormal(t).z > 0));

        static void CheckInBounds(DMesh3 result, AxisAlignedBox3d b, double grow, List<string> r)
        {
            double g = grow * b.DiagonalLength;
            foreach (Vector3d v in result.Vertices())
                if (!(v.x >= b.Min.x - g && v.y >= b.Min.y - g && v.z >= b.Min.z - g && v.x <= b.Max.x + g && v.y <= b.Max.y + g && v.z <= b.Max.z + g)) {
                    r.Add($"vertex {v} outside the input bounds {b}");
                    return;
                }
        }

        static int EulerCharacteristic(DMesh3 m) => m.VertexCount - m.EdgeCount + m.TriangleCount;

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // folds and flips

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void NoisyTerrain_ReducedTo20Percent_NoFoldOver(int seed)
        {
            // https://github.com/zeux/meshoptimizer/issues/346: a noisy grid terrain reduced to 20%: a chain of
            // collapses flipped triangles although each one passed a 90 degree normal test (each compares with the
            // current neighbours only, so the normal can turn by up to 90 degrees per step). Rebuilt: a 40 x 40 unit
            // grid with a smooth hill and noise of +-0.4. Correct: the reduced height field has no triangle whose normal
            // points down or lies flat (a fold-over relative to the original, whose normals all point up); vertices
            // within the bounds grown by 1% of the diagonal.
            var rnd = new Random(seed);
            DMesh3 input = Grid(40, 40, (i, j) => R(3 * Math.Sin(0.2 * i) * Math.Cos(0.15 * j) + 0.8 * (rnd.NextDouble() - 0.5)));
            Assert.Equal(0, DownwardTriangles(input));
            DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(input.TriangleCount / 5));
            var r = new List<string>();
            CheckBasic(result, input, r);
            if (result.TriangleCount > input.TriangleCount / 5 + 2) r.Add($"{result.TriangleCount} triangles, target {input.TriangleCount / 5}");
            int down = DownwardTriangles(result);
            if (down > 0) r.Add($"{down} folded triangles");
            CheckInBounds(result, input.CachedBounds, 0.01, r);
            Expect(nameof(NoisyTerrain_ReducedTo20Percent_NoFoldOver) + "_" + seed, r);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void SmallPatch_ReducedTo10_NoDuplicateTriangles(int seed)
        {
            // https://github.com/zeux/meshoptimizer/issues/699: a 24-triangle patch reduced to 10 triangles gave two
            // copies of a triangle with opposite winding (a non-manifold edge). Rebuilt: a 4 x 3 grid (24 triangles),
            // flat (seed 0) or with random heights. Correct: valid, no two triangles on the same three vertices, every
            // normal still up, the four corners kept, the area of a flat patch exact.
            var rnd = new Random(seed);
            DMesh3 input = Grid(4, 3, (i, j) => seed == 0 ? 0 : 0.3 * rnd.NextDouble());
            Assert.Equal(24, input.TriangleCount);
            DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(10));
            var r = new List<string>();
            CheckBasic(result, input, r);
            if (result.TriangleCount > 10) r.Add($"{result.TriangleCount} triangles, target 10");
            int down = DownwardTriangles(result);
            if (down > 0) r.Add($"{down} folded triangles");
            if (Components(result) != 1) r.Add($"{Components(result)} components");
            foreach (int corner in new[] { 0, 4, 15, 19 })
                if (!result.IsVertex(corner) || result.GetVertex(corner) != input.GetVertex(corner)) r.Add($"corner {corner} lost");
            if (seed == 0 && Math.Abs(Invariance.Area(result) - 12) > 1e-12) r.Add($"area {Invariance.Area(result)}, expected 12");
            Expect(nameof(SmallPatch_ReducedTo10_NoDuplicateTriangles) + "_" + seed, r);
        }

        [Theory]
        [InlineData(1e-6)]
        [InlineData(1e-12)]
        [InlineData(0)]
        public void AlmostFlatGrid_SingularQuadric_NoFlipNoEscape(double zRange)
        {
            // (beyond ReducerRobustnessTests.NoisyFlatSurface_NoSpikes, an 81 x 81 grid checked for spikes: tiny grids
            // reduced as far as possible, z ranges down to exactly 0, and the fold check)
            // https://github.com/libigl/libigl/issues/738: an almost flat 12-triangle grid (z in a tiny range): the
            // quadric is nearly singular in the plane, the optimal point slid past the ends of the edge and a triangle
            // flipped. Rebuilt: a 3 x 2 grid (12 triangles) and a 6 x 6 grid with random z in [0, zRange], reduced as far
            // as possible. Correct: every normal up, every vertex within the input bounds (up to 1e-9 of the diagonal).
            foreach ((int nx, int ny) in new[] { (3, 2), (6, 6) }) {
                var rnd = new Random(nx);
                DMesh3 input = Grid(nx, ny, (i, j) => zRange * rnd.NextDouble());
                DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(1));
                var r = new List<string>();
                CheckBasic(result, input, r);
                int down = DownwardTriangles(result);
                if (down > 0) r.Add($"{down} folded triangles");
                CheckInBounds(result, input.CachedBounds, 1e-9, r);
                if (result.TriangleCount >= input.TriangleCount) r.Add("nothing collapsed");
                Expect(nameof(AlmostFlatGrid_SingularQuadric_NoFlipNoEscape) + $"_{nx}x{ny}", r);
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // translation

        [Fact]
        public void FlatGridAtAnOffset_SameReduction()
        {
            // (beyond ReducerRobustnessTests.UtmCoordinates_SameQualityAsAtOrigin and InvarianceTests.Reducer_SameResult
            // UnderSimilarity, which compare quality or counts off the 2^k scales: here the same triangle ids under a
            // non-exact translation, for an open flat and curved grid)
            // https://github.com/zeux/meshoptimizer/issues/1065: the same flat grid at z = 0 and at z = 900 simplified
            // differently (also with UTM coordinates): the result depended on the offset. Rebuilt: a flat 12 x 12 grid
            // and a gently curved one, at the origin, at z = 900 and at a UTM offset, reduced to 40 triangles. Correct:
            // the same triangles (vertex ids) and the same positions shifted by the offset (within 1e-9 of the size).
            foreach (bool curved in new[] { false, true }) {
                Func<int, int, double> z = (i, j) => curved ? 0.02 * ((i - 6) * (i - 6) + 0.5 * (j - 6) * (j - 6)) : 0;
                DMesh3 at0 = Reduced(Grid(12, 12, z), rd => rd.ReduceToTriangleCount(40));
                foreach (Vector3d offset in new[] { new Vector3d(0, 0, 900), new Vector3d(632117.25, 5712043.5, 113.75) }) {
                    DMesh3 input = Grid(12, 12, z, offset);
                    DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(40));
                    var r = new List<string>();
                    CheckBasic(result, input, r);
                    if (result.TriangleCount != at0.TriangleCount) r.Add($"{result.TriangleCount} triangles, {at0.TriangleCount} at the origin");
                    else {
                        foreach (int t in at0.TriangleIndices())
                            if (!result.IsTriangle(t) || result.GetTriangle(t) != at0.GetTriangle(t)) { r.Add($"triangle {t} differs"); break; }
                        foreach (int v in at0.VertexIndices())
                            if (!result.IsVertex(v) || (result.GetVertex(v) - offset).Distance(at0.GetVertex(v)) > 1e-9 * 17) { r.Add($"vertex {v} differs"); break; }
                    }
                    Expect(nameof(FlatGridAtAnOffset_SameReduction) + (curved ? "_curved" : "_flat") + $"_{offset.z}", r);
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // closed solids reduced hard: thin parts, genus, volume, cracks

        [Theory]
        [InlineData(100)]
        [InlineData(20)]
        [InlineData(1)]
        public void ThinTrunk_ReducedHard_NotEmptyAndClosed(int target)
        {
            // https://github.com/zeux/meshoptimizer/issues/967: a 4 x 1 x 1 body with a 0.25 wide trunk, reduced hard:
            // the thin part vanished and the result had 0 triangles. Rebuilt: a 4 x 1 x 1 grid box whose middle (|x| < 1)
            // is pinched to 0.25 x 0.25, reduced to 100, 20 and 1 triangles. Correct: not empty, valid, closed, one
            // component, a positive volume, within the input bounds grown by 5% of the diagonal (the plane quadrics
            // of a coarse result place corners slightly outside).
            DMesh3 input = GridBox(new Vector3d(-2, -0.5, -0.5), new Vector3d(2, 0.5, 0.5), 9);
            foreach (int v in input.VertexIndices().ToArray()) {
                Vector3d p = input.GetVertex(v);
                double s = Math.Abs(p.x) < 1.0 - 1e-9 ? 0.25 : 1;
                input.SetVertex(v, new Vector3d(p.x, s * p.y, s * p.z));
            }
            Assert.True(input.IsClosed() && ResultChecks.Volume(input) > 0);
            DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(target));
            var r = new List<string>();
            CheckBasic(result, input, r);
            double vol = ResultChecks.Volume(result);
            if (!(vol > 0)) r.Add($"volume {vol}");
            if (Components(result) != 1) r.Add($"{Components(result)} components");
            CheckInBounds(result, input.CachedBounds, 0.05, r);
            Expect(nameof(ThinTrunk_ReducedHard_NotEmptyAndClosed) + "_" + target, r);
        }

        [Theory]
        [InlineData(400)]
        [InlineData(60)]
        [InlineData(1)]
        public void Torus_ReducedCoarsely_KeepsItsHole(int target)
        {
            // https://github.com/elalish/manifold/issues/1239: a torus R = 10, r = 1 with 100 x 20 segments simplified
            // coarsely: the hole closed (the genus changed). Rebuilt as described, reduced to 400, 60 and 1 triangles.
            // Correct: valid, closed, one component, Euler characteristic 0 (genus 1), positive volume.
            DMesh3 input = Torus(10, 1, 100, 20);
            Assert.Equal(0, EulerCharacteristic(input));
            DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(target));
            var r = new List<string>();
            CheckBasic(result, input, r);
            if (EulerCharacteristic(result) != 0) r.Add($"Euler characteristic {EulerCharacteristic(result)}");
            if (Components(result) != 1) r.Add($"{Components(result)} components");
            if (!(ResultChecks.Volume(result) > 0)) r.Add($"volume {ResultChecks.Volume(result)}");
            Expect(nameof(Torus_ReducedCoarsely_KeepsItsHole) + "_" + target, r);
        }

        [Theory]
        [InlineData(1e-7)]
        [InlineData(1e-12)]
        public void PyramidWithASliverInTheBase_VolumeKept(double off)
        {
            // https://github.com/elalish/manifold/issues/1744: a closed square pyramid with one base vertex just off the
            // diagonal (a sliver), reduced a little: the volume changed. Rebuilt: a pyramid over [0,1]^2 (apex at height
            // 1) whose base has a fifth vertex just off the diagonal from (0,0) to (1,1), so the base consists of two
            // slivers and two halves; reduced by 2 triangles. Correct: closed, valid, and since the base stays planar
            // and the sides are planes through the apex, the volume 1/3 within 1e-12.
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0));
            int c = m.AppendVertex(new Vector3d(1, 1, 0)), d = m.AppendVertex(new Vector3d(0, 1, 0));
            int e = m.AppendVertex(new Vector3d(0.5 + off, 0.5 - off, 0)), apex = m.AppendVertex(new Vector3d(0.5, 0.5, 1));
            m.AppendTriangle(a, b, apex); m.AppendTriangle(b, c, apex); m.AppendTriangle(c, d, apex); m.AppendTriangle(d, a, apex);
            m.AppendTriangle(a, e, b); m.AppendTriangle(b, e, c); m.AppendTriangle(c, e, d); m.AppendTriangle(d, e, a);
            Assert.True(m.IsClosed());
            Assert.Equal(1.0 / 3, ResultChecks.Volume(m), 12);
            DMesh3 result = Reduced(m, rd => rd.ReduceToTriangleCount(6));
            var r = new List<string>();
            CheckBasic(result, m, r);
            if (result.TriangleCount != 6) r.Add($"{result.TriangleCount} triangles, target 6");
            double vol = ResultChecks.Volume(result);
            if (!(Math.Abs(vol - 1.0 / 3) <= 1e-12)) r.Add($"volume {vol:R}, expected 1/3");
            CheckInBounds(result, m.CachedBounds, 1e-12, r);
            Expect(nameof(PyramidWithASliverInTheBase_VolumeKept) + "_" + off, r);
        }

        [Fact]
        public void CylinderOnBox_RotatedAlmost90Degrees_StaysClosed()
        {
            // https://github.com/elalish/manifold/issues/1262: a cylinder joined with a box, refined, rotated by
            // -89.999999999999 degrees: simplification opened cracks. Rebuilt: the exact union of a box and a cylinder
            // through its top, split to edges of about 0.1 (Remesher splits only), rotated by -89.999999999999 degrees
            // about x, reduced to 10%. Correct: valid, closed, one component, volume within 5% of the input's.
            DMesh3 box = Box(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 1));
            DMesh3 cyl = Cylinder(new Vector3d(0, 0, 0.5), 0.5, 1.5, 24);
            DMesh3 input = ExactMeshBoolean.Union(new[] { box, cyl });
            Assert.True(input.IsClosed());
            var refine = new Remesher(input) { EnableCollapses = false, EnableFlips = false, EnableSmoothing = false };
            refine.MaxEdgeLength = 0.1;
            refine.MinEdgeLength = 0;
            for (int k = 0; k < 6; ++k) refine.BasicRemeshPass();
            Assert.True(input.TriangleCount > 2000 && input.IsClosed());
            Quaterniond q = new Quaterniond(Vector3d.AxisX, -89.999999999999);
            foreach (int v in input.VertexIndices().ToArray())
                input.SetVertex(v, q * input.GetVertex(v));
            double vol0 = ResultChecks.Volume(input);
            DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(input.TriangleCount / 10));
            var r = new List<string>();
            CheckBasic(result, input, r);
            if (Components(result) != 1) r.Add($"{Components(result)} components");
            double vol = ResultChecks.Volume(result);
            if (!(Math.Abs(vol - vol0) <= 0.05 * vol0)) r.Add($"volume {vol}, input {vol0}");
            Expect(nameof(CylinderOnBox_RotatedAlmost90Degrees_StaysClosed), r);
        }

        [Fact]
        public void ManyTinyCylinders_TargetUnreachable_TerminatesAndKeepsEachComponent()
        {
            // https://github.com/zeux/meshoptimizer/issues/764: a large mesh plus many tiny separate cylinders: the
            // target could not be reached. Rebuilt: a sphere of radius 10 and 50 closed cylinders of radius 0.01, each
            // a separate component, reduced to 10 triangles. Correct: it terminates quickly; every component stays a
            // valid closed one with a positive volume (a closed component needs at least 4 triangles, so 51 components
            // cannot reach 10 triangles: the reduction stops at the smallest closed shapes).
            DMesh3 sphere = Sphere(Vector3d.Zero, 10, 16);
            var parts = new List<DMesh3> { sphere };
            for (int k = 0; k < 50; ++k)
                parts.Add(Cylinder(new Vector3d(12 + 0.05 * k, 0.03 * (k % 7), 0.02 * k), 0.01, 0.02, 8));
            DMesh3 input = Append(parts.ToArray());
            DMesh3 result = null;
            bool finished = Timed.Finishes(() => result = Reduced(input, rd => rd.ReduceToTriangleCount(10)), TimeSpan.FromSeconds(10));
            var r = new List<string>();
            if (!finished) r.Add("did not finish within 10 s");
            else {
                CheckBasic(result, input, r);
                var comps = new MeshConnectedComponents(result);
                comps.FindConnectedT();
                if (comps.Count != 51) r.Add($"{comps.Count} components, expected 51");
                foreach (var comp in comps.Components) {
                    if (comp.Indices.Length < 4) { r.Add($"a component of {comp.Indices.Length} triangles"); break; }
                    double v = MeshMeasurements.VolumeArea(result, comp.Indices, result.GetVertex).x;
                    if (!(v > 0)) { r.Add($"a component of volume {v}"); break; }
                }
            }
            Expect(nameof(ManyTinyCylinders_TargetUnreachable_TerminatesAndKeepsEachComponent), r);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // unwelded and non-manifold input

        [Fact]
        public void UnweldedQuad_TargetOneTriangle_ValidResult()
        {
            // https://github.com/libigl/libigl/issues/2191: a flat quad as two triangles with duplicated (unwelded)
            // vertices, target 1 triangle: the simplification returned false. Rebuilt: two triangles over [0,1]^2 that
            // share no vertex (6 vertices). Correct: no exception, valid, finite, area 1 and the 4 corner positions kept
            // (each triangle is its own component with only boundary edges: nothing can be collapsed without changing
            // the outline), no degenerate triangle.
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(1, 1, 0));
            int a2 = m.AppendVertex(new Vector3d(0, 0, 0)), c2 = m.AppendVertex(new Vector3d(1, 1, 0)), d = m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(a2, c2, d);
            var r = new List<string>();
            foreach (bool preserveBoundary in new[] { true, false }) {
                DMesh3 result = null;
                try {
                    result = Reduced(m, rd => { rd.PreserveBoundaryShape = preserveBoundary; rd.ReduceToTriangleCount(1); });
                } catch (Exception ex) { r.Add($"{ex.GetType().Name}: {ex.Message}"); continue; }
                CheckBasic(result, m, r);
                if (result.TriangleIndices().Any(t => !(result.GetTriArea(t) > 0))) r.Add("degenerate triangle");
                double area = Invariance.Area(result);
                if (preserveBoundary && Math.Abs(area - 1) > 1e-15) r.Add($"area {area}, expected 1");
                if (!(area <= 1 + 1e-15)) r.Add($"area {area} above 1");
            }
            Expect(nameof(UnweldedQuad_TargetOneTriangle_ValidResult), r);
        }

        [Theory]
        [InlineData(200)]
        [InlineData(1)]
        public void TwoConesJoinedAtTheTip_NotMerged(int target)
        {
            // https://github.com/libigl/libigl/issues/2043: a non-manifold vertex, two cones joined at the tip:
            // simplification crashed. Rebuilt: two closed cones (32 slices, side rings at 4 heights) with a shared
            // apex at the origin, one above and one below (a bowtie vertex). Correct: no exception, valid (bowtie
            // allowed), closed, finite; the two cones stay separate (no edge from a vertex above z = 0 to one below it)
            // and each keeps a positive volume.
            DMesh3 input = DoubleCone(32, 4);
            Assert.True(input.IsBowtieVertex(0));
            var r = new List<string>();
            DMesh3 result = null;
            try {
                result = Reduced(input, rd => rd.ReduceToTriangleCount(target));
            } catch (Exception ex) { r.Add($"{ex.GetType().Name}: {ex.Message}"); }
            if (result != null) {
                CheckBasic(result, input, r, allowBowties: true);
                foreach (int e in result.EdgeIndices()) {
                    Index2i ev = result.GetEdgeV(e);
                    if (result.GetVertex(ev.a).z * result.GetVertex(ev.b).z < 0) { r.Add("an edge joins the two cones"); break; }
                }
                double up = 0, down = 0;
                foreach (int t in result.TriangleIndices()) {
                    Vector3d cen = result.GetTriCentroid(t);
                    double v = MeshMeasurements.VolumeArea(result, new[] { t }, result.GetVertex).x;
                    if (cen.z > 0) up += v; else down += v;
                }
                if (!(up > 0 && down > 0)) r.Add($"cone volumes {up}, {down}");
            }
            Expect(nameof(TwoConesJoinedAtTheTip_NotMerged) + "_" + target, r);
        }

        /// <summary>
        /// two closed cones of height 1 and radius 1 sharing their apex (vertex 0) at the origin, one opening up, one
        /// down: side rings at the given number of heights, fan bases; vertex 0 is a bowtie
        /// </summary>
        internal static DMesh3 DoubleCone(int slices, int rings)
        {
            var m = new DMesh3();
            int apex = m.AppendVertex(Vector3d.Zero);
            foreach (int sign in new[] { 1, -1 }) {
                var ringIds = new List<int[]>();
                for (int k = 1; k <= rings; ++k) {
                    double s = (double)k / rings;
                    ringIds.Add(Enumerable.Range(0, slices).Select(i => m.AppendVertex(new Vector3d(s * Math.Cos(2 * Math.PI * i / slices), s * Math.Sin(2 * Math.PI * i / slices), sign * s))).ToArray());
                }
                int baseC = m.AppendVertex(new Vector3d(0, 0, sign));
                Action<int, int, int> tri = (x, y, z) => { if (sign > 0) m.AppendTriangle(x, y, z); else m.AppendTriangle(x, z, y); };
                for (int i = 0; i < slices; ++i) {
                    int j = (i + 1) % slices;
                    tri(apex, ringIds[0][j], ringIds[0][i]);
                    for (int k = 0; k + 1 < rings; ++k) {
                        tri(ringIds[k][i], ringIds[k][j], ringIds[k + 1][j]);
                        tri(ringIds[k][i], ringIds[k + 1][j], ringIds[k + 1][i]);
                    }
                    tri(baseC, ringIds[rings - 1][i], ringIds[rings - 1][j]);
                }
            }
            return m;
        }

        [Fact]
        public void SelfIntersectingInput_ReducedHard_StaysInTheBounds()
        {
            // https://github.com/CGAL/cgal/issues/8213: a self-intersecting input reduced hard: some vertices were placed
            // very far away. Rebuilt: three overlapping spheres appended into one mesh, and a sheet folded through
            // itself, reduced to 1% (the spheres) and 8 triangles (the sheet). Correct: valid, finite, every vertex
            // within the input's bounds grown by 5% of the diagonal (a coarse polyhedron around a sphere sticks out of
            // it: the plane quadrics place its vertices outside, by 2.6% of the diagonal here).
            DMesh3 spheres = Append(Sphere(Vector3d.Zero, 1, 12), Sphere(new Vector3d(0.7, 0, 0), 1, 12),
                Sphere(new Vector3d(0.3, 0.6, 0.1), 0.8, 12));
            // a strip whose section is a loop (a curtate trochoid) that crosses itself at x = 0
            DMesh3 sheet = Grid(40, 4, (i, j) => 0);
            foreach (int v in sheet.VertexIndices().ToArray()) {
                Vector3d p = sheet.GetVertex(v);
                double t = p.x / 40;
                sheet.SetVertex(v, new Vector3d(6 * (t - 0.5) + 2.5 * Math.Sin(2 * Math.PI * t), p.y, 2 - 2 * Math.Cos(2 * Math.PI * t)));
            }
            Assert.True(MeshRepairMetrics.SelfIntersections(spheres) > 0 && MeshRepairMetrics.SelfIntersections(sheet) > 0);
            var r = new List<string>();
            foreach ((DMesh3 input, int target) in new[] { (spheres, spheres.TriangleCount / 100), (sheet, 8) }) {
                DMesh3 result = Reduced(input, rd => rd.ReduceToTriangleCount(target));
                CheckBasic(result, input, r);
                CheckInBounds(result, input.CachedBounds, 0.05, r);
            }
            Expect(nameof(SelfIntersectingInput_ReducedHard_StaysInTheBounds), r);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // stop test

        [Fact]
        public void TargetAlreadyMet_NoCollapse()
        {
            // https://github.com/libigl/libigl/issues/816: a 6 x 6 x 6 box with a centre vertex on its top and bottom
            // faces (10 vertices, 16 faces), all collapse costs about 0, the target equal to the current count: one
            // collapse ran before the stop test. Correct: with the target already met (triangle count 16, or vertex
            // count 10), no collapse: the mesh is unchanged bit for bit; a target of 15 gives exactly 14 (one collapse
            // removes two triangles).
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? -3 : 3, (i & 2) == 0 ? -3 : 3, (i & 4) == 0 ? -3 : 3));
            int bot = m.AppendVertex(new Vector3d(0, 0, -3)), top = m.AppendVertex(new Vector3d(0, 0, 3));
            // sides as two triangles each
            m.AppendTriangle(0, 1, 5); m.AppendTriangle(0, 5, 4);
            m.AppendTriangle(1, 3, 7); m.AppendTriangle(1, 7, 5);
            m.AppendTriangle(3, 2, 6); m.AppendTriangle(3, 6, 7);
            m.AppendTriangle(2, 0, 4); m.AppendTriangle(2, 4, 6);
            // bottom (z = -3, normal -z) and top (z = 3, normal +z) as fans around their centres
            m.AppendTriangle(bot, 1, 0); m.AppendTriangle(bot, 3, 1); m.AppendTriangle(bot, 2, 3); m.AppendTriangle(bot, 0, 2);
            m.AppendTriangle(top, 4, 5); m.AppendTriangle(top, 5, 7); m.AppendTriangle(top, 7, 6); m.AppendTriangle(top, 6, 4);
            Assert.True(m.IsClosed());
            Assert.Equal(10, m.VertexCount);
            Assert.Equal(16, m.TriangleCount);
            Assert.Equal(216, ResultChecks.Volume(m), 9);
            var r = new List<string>();
            string d = Invariance.Difference(Reduced(m, rd => rd.ReduceToTriangleCount(16)), m);
            if (d != "") r.Add("triangle target met: " + d);
            d = Invariance.Difference(Reduced(m, rd => rd.ReduceToVertexCount(10)), m);
            if (d != "") r.Add("vertex target met: " + d);
            DMesh3 one = Reduced(m, rd => rd.ReduceToTriangleCount(15));
            CheckBasic(one, m, r);
            if (one.TriangleCount != 14) r.Add($"{one.TriangleCount} triangles after reducing to 15");
            if (Math.Abs(ResultChecks.Volume(one) - 216) > 1e-9) r.Add($"volume {ResultChecks.Volume(one)}");
            Expect(nameof(TargetAlreadyMet_NoCollapse), r);
        }
    
        [Theory]
        [InlineData(40, true)]
        [InlineData(1, true)]
        [InlineData(40, false)]
        [InlineData(1, false)]
        public void TwoSheetsTouchingAtACorner_NotJoined(int target, bool preserveBoundary)
        {
            // the boundary case of TwoConesJoinedAtTheTip_NotMerged (libigl#2043): two flat open 4 x 4 grids, [0,4]^2 and
            // [4,8]^2 at z = 0, sharing only the corner vertex (4,4) (a bowtie vertex on the boundary of both), reduced
            // with and without PreserveBoundaryShape (with it the boundary rule alone already keeps the corner; without
            // it only the bowtie guard does). Correct: valid (the bowtie allowed), the corner still a vertex at
            // (4,4), no edge from one sheet to the other, every normal +z, both sheets of positive area, the area 32 exactly at 40 triangles with PreserveBoundaryShape (outline kept).
            DMesh3 m = Grid(4, 4, (i, j) => 0);
            int corner = 24;   // (4,4) of the first grid
            Assert.Equal(new Vector3d(4, 4, 0), m.GetVertex(corner));
            int[,] vid = new int[5, 5];
            for (int j = 0; j <= 4; ++j)
                for (int i = 0; i <= 4; ++i)
                    vid[i, j] = (i == 0 && j == 0) ? corner : m.AppendVertex(new Vector3d(4 + i, 4 + j, 0));
            for (int j = 0; j < 4; ++j)
                for (int i = 0; i < 4; ++i) {
                    m.AppendTriangle(vid[i, j], vid[i + 1, j], vid[i + 1, j + 1]);
                    m.AppendTriangle(vid[i, j], vid[i + 1, j + 1], vid[i, j + 1]);
                }
            Assert.True(m.IsBowtieVertex(corner));
            DMesh3 result = Reduced(m, rd => { rd.PreserveBoundaryShape = preserveBoundary; rd.ReduceToTriangleCount(target); });
            var r = new List<string>();
            CheckBasic(result, m, r, allowBowties: true);
            if (!result.IsVertex(corner) || result.GetVertex(corner) != new Vector3d(4, 4, 0)) r.Add("the shared corner moved or was removed");
            foreach (int e in result.EdgeIndices()) {
                Index2i ev = result.GetEdgeV(e);
                Vector3d p = result.GetVertex(ev.a), q = result.GetVertex(ev.b);
                if ((p.x < 4 || p.y < 4) && (q.x > 4 || q.y > 4) || (q.x < 4 || q.y < 4) && (p.x > 4 || p.y > 4)) { r.Add("an edge joins the two sheets"); break; }
            }
            int down = DownwardTriangles(result);
            if (down > 0) r.Add($"{down} folded triangles");
            double lower = result.TriangleIndices().Where(t => result.GetTriCentroid(t).x < 4).Sum(t => result.GetTriArea(t));
            double upper = Invariance.Area(result) - lower;
            if (!(lower > 0 && upper > 0)) r.Add($"sheet areas {lower}, {upper}");
            // (at 40 triangles the outline is kept; reduced to 1, its corners are collapsed too, as far as possible)
            if (target == 40 && preserveBoundary && Math.Abs(lower + upper - 32) > 1e-12) r.Add($"area {lower + upper}, expected 32");
            if (result.TriangleCount >= m.TriangleCount) r.Add("nothing collapsed");
            Expect(nameof(TwoSheetsTouchingAtACorner_NotJoined) + $"_{target}_{preserveBoundary}", r);
        }

        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void TwoConesJoinedAtTheTip_UnderSimilarity(string similarity)
        {
            // the bowtie guard of TwoConesJoinedAtTheTip_NotMerged under Similarity.All(): the shared apex stays where it
            // is (it is the transformed origin), the cones stay separate; bit for bit the same as untransformed under 2^k
            // scales, and about the same triangle count otherwise
            Similarity s = Similarity.ByName(similarity);
            DMesh3 source = DoubleCone(32, 4);
            DMesh3 reference = Reduced(source, rd => rd.ReduceToTriangleCount(60));
            DMesh3 mesh = Reduced(Invariance.Transformed(source, s), rd => rd.ReduceToTriangleCount(60));
            Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly));
            Assert.True(mesh.IsClosed() && Invariance.AllFinite(mesh));
            Assert.True(mesh.IsVertex(0) && mesh.IsBowtieVertex(0));
            Assert.Equal(s.Apply(Vector3d.Zero), mesh.GetVertex(0));
            DMesh3 back = Invariance.MappedBack(mesh, s);
            foreach (int e in back.EdgeIndices()) {
                Index2i ev = back.GetEdgeV(e);
                Assert.False(back.GetVertex(ev.a).z * back.GetVertex(ev.b).z < -1e-6, "an edge joins the two cones");
            }
            if (s.IsExact)
                Assert.Equal("", Invariance.Difference(reference, back));
            else
                Assert.InRange(mesh.TriangleCount, reference.TriangleCount - 8, reference.TriangleCount + 8);
        }
    }
}
