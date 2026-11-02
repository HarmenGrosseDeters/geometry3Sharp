using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for alpha wrapping (MeshAlphaWrap) from the issue trackers of other libraries (docs/QA.md 2.11): only
    /// the failure mode, rebuilt here from simple primitives. CGAL is GPL and MeshLib source-available: only the prose of
    /// their issues was read, no code, test code or meshes. The issue URL is in the comment of each test.
    ///
    /// Every case checks the wrap's guarantees with AlphaWrapTests.CheckWrap (closed, manifold, outward, no
    /// self-intersections, vertices on the offset surface, every sampled point within offset + 2 alpha, the input
    /// enclosed) and case-specific results (components, distances). No case fails today; a failing one would be listed
    /// in KnownFailures with its finding (strict: a listed case that passes fails).
    /// </summary>
    public class AlphaWrapIssueCasesTests
    {
        /// <summary> case name -> finding; a listed case must still fail </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
        };

        static void Expect(string name, Action check)
        {
            string failure = "";
            try { check(); } catch (Exception e) when (e is Xunit.Sdk.XunitException) { failure = e.Message; }
            if (KnownFailures.TryGetValue(name, out string finding)) {
                if (!IssueCases.Strict) return;
                Assert.True(failure != "", $"{name} passes now: remove its entry ({finding}) from KnownFailures and docs/QA.md 2.11");
            }
            else
                Assert.True(failure == "", name + ": " + failure);
        }

        static DMesh3 Box(Vector3d lo, Vector3d hi) => MeshRepairIssueCasesTests.Box(lo, hi);

        static DMesh3 Union(params DMesh3[] meshes)
        {
            var m = new DMesh3();
            foreach (DMesh3 x in meshes) new MeshEditor(m).AppendMesh(x);
            return m;
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        static MeshAlphaWrap Wrap(DMesh3 mesh, double relAlpha, double relOffset)
        {
            var w = new MeshAlphaWrap(mesh) { RelativeAlpha = relAlpha, RelativeOffset = relOffset };
            w.Compute();
            return w;
        }

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(AlphaWrapIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                Assert.Contains(kv.Key, names);
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            }
        }

        [Theory]
        [InlineData(4000)]
        [InlineData(4050)]
        public void SeparateComponents_GiveSeparateShells(double relOffset)
        {
            // https://github.com/CGAL/cgal/issues/7625: several separate components wrapped with a relative alpha of
            // 1000 and offset of 4000: the step that makes the result manifold marked outside cells inside, which gave
            // stray triangles bridging the components (offset 4050 worked). Rebuilt with a coarser alpha (100, the same
            // ratio of offset to alpha): 6 small boxes in a row, their gaps many alphas wide. Correct: 6 shells, each
            // around its own box (its vertices at the offset from it).
            var boxes = Enumerable.Range(0, 6).Select(i => Box(new Vector3d(3 * i, 0, 0), new Vector3d(3 * i + 1, 1 + 0.1 * i, 1))).ToArray();
            DMesh3 input = Union(boxes);
            Expect(nameof(SeparateComponents_GiveSeparateShells), () => {
                MeshAlphaWrap w = Wrap(input, 100, relOffset / 10);
                AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(input));
                Assert.Equal(6, Components(w.Result));
            });
        }

        [Fact]
        public void OverlappingClosedInputs_OneShell()
        {
            // https://github.com/CGAL/cgal/issues/8725: overlapping closed inputs wrapped one by one gave overlapping
            // shells that a mesher downstream hung on. Wrapping them together must give one closed shell around their
            // union: two boxes crossing each other, a sphere through both.
            DMesh3 a = Box(new Vector3d(0, 0, 0), new Vector3d(4, 1, 1)), b = Box(new Vector3d(1.5, -1.5, -0.5), new Vector3d(2.5, 2.5, 1.5));
            DMesh3 s = MeshRepairIssueCasesTests.Sphere(new Vector3d(2, 0.5, 0.5), 0.9);
            DMesh3 input = Union(a, b, s);
            Expect(nameof(OverlappingClosedInputs_OneShell), () => {
                MeshAlphaWrap w = Wrap(input, 40, 1200);
                AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(input));
                Assert.Equal(1, Components(w.Result));
            });
        }

        [Fact]
        public void NestedShells_OnlyTheOuterOneIsWrapped()
        {
            // https://github.com/CGAL/cgal/issues/8725 (nested shells from wrapping parts separately): a closed sphere
            // inside a closed box, wrapped together. The wrap comes from outside, so the inner shell is not reached:
            // one shell, outside the box.
            DMesh3 outer = Box(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)), inner = MeshRepairIssueCasesTests.Sphere(Vector3d.Zero, 1);
            DMesh3 input = Union(outer, inner);
            Expect(nameof(NestedShells_OnlyTheOuterOneIsWrapped), () => {
                MeshAlphaWrap w = Wrap(input, 30, 900);
                AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(input));
                Assert.Equal(1, Components(w.Result));
                foreach (Vector3d v in w.Result.Vertices())
                    Assert.True(Math.Max(Math.Abs(v.x), Math.Max(Math.Abs(v.y), Math.Abs(v.z))) >= 2, $"vertex {v} inside the box");
            });
        }

        [Fact]
        public void CubeWithLargeAlpha_StaysNearTheCorners()
        {
            // https://github.com/CGAL/cgal/issues/8152: large flat panels and sharp edges at a coarse alpha (relative
            // 600 / offset 1000 there): the edges were rounded off and the flat parts over-refined. Correct, as
            // documented: every point of the result within offset + 2 alpha of the cube, each corner within offset +
            // 2 alpha of the result (not rounded off further), and flat faces with no more triangles than alpha allows.
            DMesh3 cube = Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            foreach (double relAlpha in new[] { 4.0, 15.0 }) {
                Expect(nameof(CubeWithLargeAlpha_StaysNearTheCorners), () => {
                    MeshAlphaWrap w = Wrap(cube, relAlpha, 10 * relAlpha);
                    AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(cube));
                    // the corners are kept: each within offset + 2 alpha of the result (a rounded-off corner is farther)
                    var tree = new DMeshAABBTree3(w.Result, true);
                    foreach (Vector3d c in AlphaWrapTests.Vertices(cube)) {
                        double d = Math.Sqrt(MeshQueries.TriDistanceSqr(w.Result, tree.FindNearestTriangle(c), c));
                        Assert.True(d <= w.UsedOffset + 2 * w.UsedAlpha, $"corner {c} at {d} from the wrap (offset {w.UsedOffset}, alpha {w.UsedAlpha})");
                    }
                    // flat faces not over-refined: triangles about alpha in size, so their count is bounded by the area
                    // of the offset surface over the area of a triangle with circumradius alpha / 2
                    // (measured: 48 and 732 triangles, bounds 116 and 1450)
                    double area = 6 * Math.Pow(2 + 2 * w.UsedOffset, 2);
                    double small = 3 * Math.Sqrt(3) / 4 * Math.Pow(w.UsedAlpha / 2, 2);
                    Assert.True(w.Result.TriangleCount <= area / small, $"{w.Result.TriangleCount} triangles at alpha {w.UsedAlpha}");
                });
            }
        }

        [Fact]
        public void LatticePointCloud_CosphericalPoints()
        {
            // https://github.com/isl-org/Open3D/issues/1829: point sets with degenerate (zero-volume) Delaunay
            // tetrahedra, eg lattice points (many cospherical quadruples), made the whole alpha shape fail. Rebuilt: a
            // 6 x 6 x 6 lattice and a planar 10 x 10 lattice as point clouds. Correct: a closed wrap around all points.
            var cube = new List<Vector3d>();
            for (int i = 0; i < 6; ++i) for (int j = 0; j < 6; ++j) for (int k = 0; k < 6; ++k) cube.Add(new Vector3d(i, j, k));
            var plane = new List<Vector3d>();
            for (int i = 0; i < 10; ++i) for (int j = 0; j < 10; ++j) plane.Add(new Vector3d(i, j, 0));
            foreach (var pts in new[] { cube, plane }) {
                Expect(nameof(LatticePointCloud_CosphericalPoints), () => {
                    var w = new MeshAlphaWrap(pts) { RelativeAlpha = 10, RelativeOffset = 100 };
                    w.Compute();
                    AlphaWrapTests.CheckWrap(w, pts);
                });
            }
        }

        [Fact]
        public void NearlyCosphericalPoints_FarFromTheOrigin()
        {
            // https://github.com/isl-org/Open3D/issues/1659: about 12000 points voxel-downsampled from a sphere (so
            // nearly cospherical) at coordinates of about 2.8e6: the convex hull / Delaunay step aborted on a precision
            // error. Rebuilt: points of a sphere of radius 5 snapped to a 0.25 grid, at (2.8e6, -1.9e6, 4.1e5), and a
            // copy at the origin; both wraps alike.
            var pts = new List<Vector3d>();
            var seen = new HashSet<Vector3d>();
            var rand = new Random(1659);
            for (int i = 0; i < 4000; ++i) {
                Vector3d d = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5);
                if (d.Length < 1e-3) continue;
                Vector3d p = 5 * d.Normalized;
                p = new Vector3d(Math.Round(4 * p.x) / 4, Math.Round(4 * p.y) / 4, Math.Round(4 * p.z) / 4);
                if (seen.Add(p)) pts.Add(p);
            }
            var shift = new Vector3d(2.8e6, -1.9e6, 4.1e5);
            var counts = new List<int>();
            foreach (Vector3d t in new[] { Vector3d.Zero, shift }) {
                List<Vector3d> input = pts.Select(p => p + t).ToList();
                Expect(nameof(NearlyCosphericalPoints_FarFromTheOrigin), () => {
                    var w = new MeshAlphaWrap(input) { RelativeAlpha = 15, RelativeOffset = 300 };
                    w.Compute();
                    AlphaWrapTests.CheckWrap(w, input);
                    Assert.Equal(1, Components(w.Result));
                    counts.Add(w.Result.TriangleCount);
                });
            }
            // the same wrap up to rounding: triangle counts within a few percent
            if (counts.Count == 2)
                Assert.True(Math.Abs(counts[0] - counts[1]) <= 0.05 * counts[0], $"{counts[0]} vs {counts[1]} triangles");
        }

        [Fact]
        public void TooSmallAlpha_StopsAtThePointLimit()
        {
            // https://github.com/MeshInspector/MeshLib/issues/5528: with a too fine voxel (here: alpha) for the model
            // size, a rebuild ran for hours instead of minutes. Correct: the point limit stops the refinement in bounded
            // time, with a closed result that still encloses the input (PointLimitReached, documented).
            DMesh3 input = MeshRepairIssueCasesTests.Sphere(Vector3d.Zero, 1, 6);
            Expect(nameof(TooSmallAlpha_StopsAtThePointLimit), () => {
                var sw = Stopwatch.StartNew();
                var w = new MeshAlphaWrap(input) { RelativeAlpha = 1e5, RelativeOffset = 1e6, MaxSteinerPoints = 20000 };
                w.Compute();
                Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, w.Status);
                Assert.True(sw.Elapsed.TotalSeconds < 60, $"{sw.Elapsed.TotalSeconds} s");
                // (with the limit reached, box corners can be vertices: no distance bound, only the topology and enclosure)
                ResultChecks.AssertClosed(w.Result);
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(w.Result));
                var tree = new DMeshAABBTree3(w.Result, true);
                foreach (Vector3d p in AlphaWrapTests.Vertices(input))
                    Assert.True(tree.WindingNumber(p) > 0.99, "input point outside of the wrap");
            });
        }

        [Fact]
        public void TwoSidedSheet_ZeroThickness_Wrapped()
        {
            // https://github.com/elalish/manifold/issues/560: a zero-thickness solid (a surface and its flipped copy
            // stitched together) whose cleanup deleted the valid but zero-volume parts. A wrap must enclose such a
            // sheet: a 10 x 10 grid and its reversed copy on the same vertices positions.
            var grid = new DMesh3();
            int n = 10;
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) grid.AppendVertex(new Vector3d(i, j, 0.05 * Math.Sin(i + j)));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1;
                grid.AppendTriangle(a, b, c); grid.AppendTriangle(a, c, d);
            }
            DMesh3 back = new DMesh3(grid);
            back.ReverseOrientation();
            DMesh3 input = Union(grid, back);
            Expect(nameof(TwoSidedSheet_ZeroThickness_Wrapped), () => {
                MeshAlphaWrap w = Wrap(input, 20, 400);
                AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(input));
                Assert.Equal(1, Components(w.Result));
                // a thin shell: its volume about twice the offset times the sheet's area
                double vol = ResultChecks.Volume(w.Result);
                Assert.True(vol < 2 * (w.UsedOffset + 2 * w.UsedAlpha) * 1.2 * n * n + 1e-9, $"volume {vol}");
            });
        }

        [Fact]
        public void ComponentsCloserThanAlpha_Bridged()
        {
            // the other side of https://github.com/CGAL/cgal/issues/7625: components closer than about 2 alpha are
            // wrapped together (documented: gaps narrower than about 2 alpha are wrapped over). Two boxes 0.1 alpha apart:
            // one shell.
            DMesh3 a = Box(new Vector3d(0, 0, 0), new Vector3d(1, 1, 1));
            DMesh3 input = null;
            Expect(nameof(ComponentsCloserThanAlpha_Bridged), () => {
                var probe = Wrap(a, 20, 600);
                double gap = 0.1 * probe.UsedAlpha;
                input = Union(a, Box(new Vector3d(1 + gap, 0, 0), new Vector3d(2 + gap, 1, 1)));
                // the same absolute alpha and offset for the pair (relative values follow the larger bounds)
                var w = new MeshAlphaWrap(input) { Alpha = probe.UsedAlpha, Offset = probe.UsedOffset };
                w.Compute();
                AlphaWrapTests.CheckWrap(w, AlphaWrapTests.Vertices(input));
                Assert.Equal(1, Components(w.Result));
            });
        }
    }
}
