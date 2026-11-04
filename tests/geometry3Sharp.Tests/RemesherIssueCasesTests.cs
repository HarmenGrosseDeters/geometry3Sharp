using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for remeshing (Remesher, RemesherPro, RegionRemesher with MeshConstraintUtil constraints) from the
    /// issue trackers of other libraries (docs/QA.md 2.11): only the failure mode, rebuilt here from simple primitives.
    /// No code, test code or meshes of these projects are used (CGAL is GPL and MeshLib source-available: only the prose
    /// of their issues was read). The issue URL is in the comment of each test.
    ///
    /// Every case checks the result itself (valid DMesh3, finite, closed where the input is, orientation, distances to
    /// the input, bounds, constrained vertices). A case we fail is listed in KnownFailures with its finding (strict: a
    /// listed case that passes fails; while listed, the test also checks which failure it is).
    /// </summary>
    public class RemesherIssueCasesTests
    {
        /// <summary>
        /// case name -> (finding, the exact failure while listed: the problems joined by "; ", in the order the test
        /// reports them)
        /// </summary>
        public static readonly Dictionary<string, (string Finding, string Pinned)> KnownFailures = new Dictionary<string, (string, string)> {
            // without the opt-in MinTriangleQuality guard (default 0) smoothing moves are not checked for flips: new
            // vertices on the interior edges of a non-convex fan are smoothed across their neighbours
            [nameof(FlatOpenFan_BoundaryFixed_NoFlippedTriangle) + "_0_basic"] = (FindingSmoothFlip, "10 triangles not facing +z; area 1.7581, input 1.7338"),
            [nameof(FlatOpenFan_BoundaryFixed_NoFlippedTriangle) + "_0_pro"] = (FindingSmoothFlip, "10 triangles not facing +z; area 1.8199, input 1.7338"),
            // the same without the guard on a long thin part: a flat triangle on a crease of the long box
            [nameof(LongThinSharpParts_NoInvertedTriangles) + "_0"] = (FindingSmoothFlip, "box: 1 inverted triangles, 3 self-intersecting pairs"),
            // without feature constraints, flips and collapses across the 90 degree creases of a coarse L prism cut its
            // corners off: volume 3 -> 0.73, a leg of the L gone after 15 passes
            [nameof(CreasedPart_BoundsKept) + "_none"] = ("finding S3: without feature constraints the remesher flips and collapses across creases", "bounding box shifted by 1.000; distance to the input 1.226"),
        };
        const string FindingSmoothFlip = "finding S2: with MinTriangleQuality = 0 (default) smoothing and projection moves can flip triangles";

        static void Expect(string name, List<string> problems)
        {
            string failure = string.Join("; ", problems);
            if (KnownFailures.TryGetValue(name, out var known)) {
                if (!IssueCases.Strict) return;
                Assert.True(failure != "", $"{name} passes now: remove its entry ({known.Finding}) from KnownFailures and docs/QA.md 2.11");
                Assert.True(failure == known.Pinned, $"{name} fails differently than pinned ('{known.Pinned}'): {failure}");
            } else
                Assert.True(failure == "", name + ": " + failure);
        }

        [Theory]
        [InlineData("de-DE", "basic")]
        [InlineData("de-DE", "pro")]
        [InlineData("de-DE", "creased")]
        [InlineData("en-US", "basic")]
        [InlineData("en-US", "pro")]
        [InlineData("en-US", "creased")]
        public void PinnedFailures_AreCultureIndependent(string cultureName, string variant)
        {
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            try {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                if (variant == "creased") CreasedPart_BoundsKept(false);
                else FlatOpenFan_BoundaryFixed_NoFlippedTriangle(0, variant == "pro");
            } finally {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
            }
        }

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(RemesherIssueCasesTests).GetMethods().Select(m => m.Name));
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

        // ---- inputs ----

        static DMesh3 GridBox(Vector3d lo, Vector3d hi, int edgeVertices)
        {
            DMesh3 m = new GridBox3Generator() { Box = new Box3d(0.5 * (lo + hi), 0.5 * (hi - lo)), EdgeVertices = edgeVertices, NoSharedVertices = false }.Generate().MakeDMesh();
            Assert.True(m.IsClosed() && ResultChecks.Volume(m) > 0);
            return m;
        }

        static DMesh3 Cylinder(double r, double h, int slices)
        {
            DMesh3 m = new CappedCylinderGenerator() { BaseRadius = (float)r, TopRadius = (float)r, Height = (float)h, Slices = slices, NoSharedVertices = false }.Generate().MakeDMesh();
            Assert.True(m.IsClosed() && ResultChecks.Volume(m) > 0);
            return m;
        }

        /// <summary> a closed cylinder along z from 0 to h, rim points rounded (R), fan caps, outward </summary>
        static DMesh3 RoundedCylinder(double r, double h, int slices)
        {
            var m = new DMesh3();
            int cb = m.AppendVertex(Vector3d.Zero), ct = m.AppendVertex(new Vector3d(0, 0, h));
            Vector2d[] ring = Enumerable.Range(0, slices).Select(i => new Vector2d(R(r * Math.Cos(2 * Math.PI * i / slices)), R(r * Math.Sin(2 * Math.PI * i / slices)))).ToArray();
            int[] b = ring.Select(q => m.AppendVertex(new Vector3d(q.x, q.y, 0))).ToArray();
            int[] t = ring.Select(q => m.AppendVertex(new Vector3d(q.x, q.y, h))).ToArray();
            for (int i = 0; i < slices; ++i) {
                int j = (i + 1) % slices;
                m.AppendTriangle(cb, b[j], b[i]); m.AppendTriangle(ct, t[i], t[j]);
                m.AppendTriangle(b[i], b[j], t[j]); m.AppendTriangle(b[i], t[j], t[i]);
            }
            Assert.True(m.IsClosed() && ResultChecks.Volume(m) > 0);
            return m;
        }

        // ---- remeshing and checks ----

        /// <summary> options as g3cli remesh: smoothing 0.5, projection onto a copy of the input, creases at 30 degrees </summary>
        static Remesher Remesher(DMesh3 mesh, DMesh3 input, double edge, bool features = true, double minQuality = 0, bool pro = false)
        {
            Remesher r = pro ? new RemesherPro(mesh) : new Remesher(mesh);
            r.SmoothSpeedT = 0.5;
            r.MinTriangleQuality = minQuality;
            if (input != null)
                r.SetProjectionTarget(MeshProjectionTarget.Auto(new DMesh3(input)));
            if (features)
                MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(edge);
            return r;
        }

        static void Passes(Remesher r, int n)
        {
            for (int k = 0; k < n; ++k) {
                if (r is RemesherPro pro) pro.RemeshIteration();
                else r.BasicRemeshPass();
            }
        }

        static void CheckBasic(DMesh3 result, DMesh3 input, List<string> r, bool allowBowties = false)
        {
            if (!result.CheckValidity(allowBowties, FailMode.ReturnOnly)) r.Add("invalid DMesh3");
            if (!result.Vertices().All(v => v.IsFinite)) r.Add("non-finite coordinates");
            if (result.TriangleCount == 0) r.Add("empty result");
            if (input.IsClosed() && !result.IsClosed()) r.Add("not closed");
            if (input.IsClosed() && !(ResultChecks.Volume(result) > 0)) r.Add($"volume {ResultChecks.Volume(result)}");
        }

        /// <summary> triangles whose normal points away from the input's nearest triangle (dot &lt;= 0): inverted </summary>
        static int InvertedTriangles(DMesh3 result, DMesh3 input)
        {
            var tree = new DMeshAABBTree3(input, true);
            int n = 0;
            foreach (int t in result.TriangleIndices()) {
                int nt = tree.FindNearestTriangle(result.GetTriCentroid(t));
                if (!(result.GetTriNormal(t).Dot(input.GetTriNormal(nt)) > 0)) n++;
            }
            return n;
        }

        static AxisAlignedBox3d Bounds(DMesh3 m)
        {
            var b = AxisAlignedBox3d.Empty;
            foreach (Vector3d v in m.Vertices()) b.Contain(v);
            return b;
        }

        static double BoundsShift(AxisAlignedBox3d a, AxisAlignedBox3d b) =>
            Math.Max((a.Min - b.Min).MaxAbs, (a.Max - b.Max).MaxAbs);

        // ---------------------------------------------------------------------------------------------------------------
        // flat and open surfaces

        [Theory]
        [InlineData(0.0, false)]
        [InlineData(0.2, false)]
        [InlineData(0.0, true)]
        public void FlatOpenFan_BoundaryFixed_NoFlippedTriangle(double minQuality, bool pro)
        {
            // https://github.com/pmp-library/pmp-library/issues/158: a flat open fan at z = 0 (9 triangles, 11 vertices)
            // with its boundary fixed: tangential smoothing left a flipped triangle. Rebuilt: a centre and 10 rim points
            // over 270 degrees with radii between 0.4 and 1.6 (a non-convex fan), boundary edges fixed
            // (FixAllBoundaryEdges), remeshed to edges of 0.2. Correct: every normal is +z (z of the unit normal > 0.999),
            // every vertex at z = 0, the area unchanged, the boundary vertices unmoved.
            var m = new DMesh3();
            int c = m.AppendVertex(Vector3d.Zero);
            double[] radii = { 1.0, 1.6, 0.4, 1.3, 0.5, 1.5, 0.45, 1.2, 0.6, 1.4 };
            int[] rim = Enumerable.Range(0, 10).Select(i => {
                double a = 1.5 * Math.PI * i / 9;
                return m.AppendVertex(new Vector3d(R(radii[i] * Math.Cos(a)), R(radii[i] * Math.Sin(a)), 0));
            }).ToArray();
            for (int i = 0; i + 1 < 10; ++i)
                m.AppendTriangle(c, rim[i], rim[i + 1]);
            Assert.Equal(11, m.VertexCount);
            Assert.Equal(9, m.TriangleCount);
            DMesh3 input = new DMesh3(m);
            double area0 = Invariance.Area(input);
            Remesher r = Remesher(m, null, 0.2, features: false, minQuality: minQuality, pro: pro);
            MeshConstraintUtil.FixAllBoundaryEdges(r);
            Passes(r, 15);
            var p = new List<string>();
            CheckBasic(m, input, p);
            if (m.TriangleCount <= input.TriangleCount) p.Add("not refined");
            int bad = m.TriangleIndices().Count(t => !(m.GetTriNormal(t).z > 0.999));
            if (bad > 0) p.Add($"{bad} triangles not facing +z");
            if (m.Vertices().Any(v => v.z != 0)) p.Add("a vertex left z = 0");
            if (Math.Abs(Invariance.Area(m) - area0) > 1e-9 * area0) p.Add(FormattableString.Invariant($"area {Invariance.Area(m):F4}, input {area0:F4}"));
            foreach (int v in input.VertexIndices())
                if (!m.IsVertex(v) || m.GetVertex(v) != input.GetVertex(v)) { p.Add($"boundary vertex {v} moved"); break; }
            Expect(nameof(FlatOpenFan_BoundaryFixed_NoFlippedTriangle) + $"_{minQuality}_{(pro ? "pro" : "basic")}", p);
        }

        /// <summary> a spherical cap of the unit sphere above z = zMin: polar grid of rings x segments around the pole </summary>
        static DMesh3 SphericalCap(double zMin, int rings, int segments)
        {
            var m = new DMesh3();
            int pole = m.AppendVertex(new Vector3d(0, 0, 1));
            double phiMax = Math.Acos(zMin);
            var ids = new int[rings + 1, segments];
            for (int k = 1; k <= rings; ++k)
                for (int i = 0; i < segments; ++i) {
                    double phi = phiMax * k / rings, a = 2 * Math.PI * i / segments;
                    ids[k, i] = m.AppendVertex(new Vector3d(Math.Sin(phi) * Math.Cos(a), Math.Sin(phi) * Math.Sin(a), Math.Cos(phi)));
                }
            for (int i = 0; i < segments; ++i) {
                int j = (i + 1) % segments;
                m.AppendTriangle(pole, ids[1, i], ids[1, j]);
                for (int k = 1; k < rings; ++k) {
                    m.AppendTriangle(ids[k, i], ids[k + 1, i], ids[k + 1, j]);
                    m.AppendTriangle(ids[k, i], ids[k + 1, j], ids[k, j]);
                }
            }
            return m;
        }

        /// <summary> distance from p to the closed polyline of the given loop of input boundary points </summary>
        static double PolylineDistance(List<Vector3d> loop, Vector3d p)
        {
            double d = double.MaxValue;
            for (int i = 0; i < loop.Count; ++i)
                d = Math.Min(d, new Segment3d(loop[i], loop[(i + 1) % loop.Count]).DistanceSquared(p));
            return Math.Sqrt(d);
        }

        [Theory]
        [InlineData(0.1, false)]
        [InlineData(0.4, false)]
        [InlineData(0.1, true)]
        public void OpenSurface_BoundaryDoesNotShrink(double edge, bool pro)
        {
            // https://github.com/BrunoLevy/geogram/issues/29: an open surface remeshed: its boundary shrank inward.
            // Rebuilt: a spherical cap (z >= 0, 64 boundary segments) and a wavy square sheet with a straight
            // border and four corners, remeshed with the boundary constraints the documentation recommends
            // (MeshConstraintUtil.PreserveBoundaryAndFeatures) to finer and coarser edges. Correct: one boundary loop,
            // every result boundary vertex on the input's boundary polyline (1e-9), every input boundary vertex within
            // the sagitta of an edge of 1.33 target length from the result's boundary (the cap) or on it (the sheet),
            // the area within 2% or (1.33 target)^2 / 4 of the input's, relative (the chordal loss on the unit sphere).
            DMesh3 cap = SphericalCap(0, 12, 64);
            DMesh3 sheet = ReducerIssueCasesTests.Grid(16, 16, (i, j) => 0);
            foreach (int v in sheet.VertexIndices().ToArray()) {
                Vector3d q = sheet.GetVertex(v) / 8 - new Vector3d(1, 1, 0);
                sheet.SetVertex(v, new Vector3d(q.x, q.y, 0.2 * Math.Sin(Math.PI * q.x) * Math.Sin(2 * Math.PI * q.y)));   // 0 on the border
            }
            var p = new List<string>();
            foreach ((string what, DMesh3 input) in new[] { ("cap", cap), ("sheet", sheet) }) {
                var inLoops = new MeshBoundaryLoops(input);
                Assert.Single(inLoops.Loops);
                List<Vector3d> inLoop = inLoops.Loops[0].Vertices.Select(input.GetVertex).ToList();
                DMesh3 m = new DMesh3(input);
                Remesher r = Remesher(m, input, edge, pro: pro);
                Passes(r, 20);
                CheckBasic(m, input, p);
                var outLoops = new MeshBoundaryLoops(m);
                if (outLoops.Loops.Count != 1) { p.Add($"{what}: {outLoops.Loops.Count} boundary loops"); continue; }
                List<Vector3d> outLoop = outLoops.Loops[0].Vertices.Select(m.GetVertex).ToList();
                double off = outLoop.Max(v => PolylineDistance(inLoop, v));
                if (off > 1e-9) p.Add($"{what}: a boundary vertex {off} off the input boundary");
                double maxEdge = 1.33 * edge, sagitta = maxEdge * maxEdge / 8;   // the cap's boundary circle has radius 1
                double shrink = inLoop.Max(v => PolylineDistance(outLoop, v));
                if (what == "cap" && shrink > sagitta) p.Add($"{what}: the boundary moved inward by {shrink}, more than the sagitta {sagitta}");
                if (what == "sheet" && shrink > 1e-9) p.Add($"{what}: the boundary moved inward by {shrink}");
                double a0 = Invariance.Area(input), a = Invariance.Area(m);
                if (Math.Abs(a - a0) > Math.Max(0.02, maxEdge * maxEdge / 4) * a0) p.Add($"{what}: area {a}, input {a0}");
            }
            Expect(nameof(OpenSurface_BoundaryDoesNotShrink) + $"_{edge}_{(pro ? "pro" : "basic")}", p);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // nearby parts

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NearlyTouchingSheets_PreventSelfIntersections_NoneCreated(bool pro)
        {
            // https://github.com/CGAL/cgal/issues/8134: a vertex very close to another part of the surface: smoothing
            // pushed it through, a self-intersection. Rebuilt: a thin slab 2 x 2 x 0.01 (two sheets 0.01 apart) and a
            // box with a dent whose bottom is 0.002 above the box's lower sheet, remeshed to edges of 0.1 (10 to 50
            // times the gap) with PreventSelfIntersections. Correct: no self-intersecting pair, closed, valid, positive
            // volume (not inside out).
            DMesh3 slab = GridBox(new Vector3d(-1, -1, -0.005), new Vector3d(1, 1, 0.005), 9);
            DMesh3 dent = GridBox(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 0.5), 13);
            foreach (int v in dent.VertexIndices().ToArray()) {
                Vector3d q = dent.GetVertex(v);
                double rr = Math.Sqrt(q.x * q.x + q.y * q.y);
                if (q.z > 0.5 - 1e-9 && rr < 0.6)
                    dent.SetVertex(v, new Vector3d(q.x, q.y, 0.002 + 0.498 * (rr / 0.6) * (rr / 0.6)));
            }
            var p = new List<string>();
            foreach ((string what, DMesh3 input) in new[] { ("slab", slab), ("dent", dent) }) {
                Assert.Equal(0, MeshRepairMetrics.SelfIntersections(input));
                DMesh3 m = new DMesh3(input);
                Remesher r = Remesher(m, input, 0.1, pro: pro);
                r.PreventSelfIntersections = true;
                Passes(r, 10);
                CheckBasic(m, input, p);
                int si = MeshRepairMetrics.SelfIntersections(m);
                if (si > 0) p.Add($"{what}: {si} self-intersecting pairs");
                if (m.TriangleCount == input.TriangleCount) p.Add($"{what}: not remeshed");
            }
            Expect(nameof(NearlyTouchingSheets_PreventSelfIntersections_NoneCreated) + (pro ? "_pro" : "_basic"), p);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // constrained vertices

        [Fact]
        public void PartialRemesh_LockedVertices_BitExact()
        {
            // (the whole-mesh half overlaps the corner checks of FeaturePreservationTests, which keep corners through
            // feature constraints; new here: the region remesh with BackPropropagate and plain Pinned constraints)
            // https://github.com/CGAL/cgal/issues/8388: a partial remesh with locked corner vertices: the locked vertices
            // moved. Rebuilt: a 2 x 2 x 1 box (no features marked), its top face and the upper half of its sides as the
            // region (RegionRemesher, projection onto the input); the four top corners, inside the region, pinned
            // (VertexConstraint.Pinned), remeshed to edges of 0.15 and written back. And the whole box remeshed
            // (Remesher, RemesherPro) with its 8 corners pinned. Correct: each locked position is still a vertex, bit
            // for bit; the result is valid and closed.
            DMesh3 input = GridBox(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 1), 9);
            var corners = new List<int>();
            foreach (int v in input.VertexIndices()) {
                Vector3d q = input.GetVertex(v);
                if (Math.Abs(Math.Abs(q.x) - 1) < 1e-12 && Math.Abs(Math.Abs(q.y) - 1) < 1e-12 && (q.z == 0 || q.z == 1)) corners.Add(v);
            }
            Assert.Equal(8, corners.Count);
            var p = new List<string>();
            Action<DMesh3, IEnumerable<Vector3d>, string> checkLocked = (m, locked, what) => {
                var positions = new HashSet<Vector3d>(m.Vertices());
                foreach (Vector3d c in locked)
                    if (!positions.Contains(c)) { p.Add($"{what}: locked vertex {c} moved"); break; }
                CheckBasic(m, input, p);
            };

            // partial: RegionRemesher
            DMesh3 mesh = new DMesh3(input);
            int[] region = mesh.TriangleIndices().Where(t => mesh.GetTriCentroid(t).z > 0.5).ToArray();
            var rr = new RegionRemesher(mesh, region);
            rr.SetProjectionTarget(MeshProjectionTarget.Auto(new DMesh3(input)));
            rr.SmoothSpeedT = 0.5;
            rr.SetTargetEdgeLength(0.15);
            Vector3d[] top = corners.Select(input.GetVertex).Where(c => c.z == 1).ToArray();
            foreach (Vector3d c in top) {
                int bv = corners.First(v => input.GetVertex(v) == c);
                int sv = rr.Region.MapVertexToSubmesh(bv);
                Assert.True(sv >= 0);
                rr.Constraints.SetOrUpdateVertexConstraint(sv, VertexConstraint.Pinned);
            }
            for (int k = 0; k < 10; ++k) rr.BasicRemeshPass();
            if (!rr.BackPropropagate()) p.Add("region: BackPropropagate false");
            checkLocked(mesh, top, "region");
            if (mesh.TriangleCount == input.TriangleCount) p.Add("region: not remeshed");

            // whole: Remesher and RemesherPro
            foreach (bool pro in new[] { false, true }) {
                DMesh3 m = new DMesh3(input);
                Remesher r = Remesher(m, input, 0.15, features: false, pro: pro);
                r.SetExternalConstraints(new MeshConstraints());
                foreach (int v in corners) r.Constraints.SetOrUpdateVertexConstraint(v, VertexConstraint.Pinned);
                Passes(r, 10);
                checkLocked(m, corners.Select(input.GetVertex), pro ? "pro" : "basic");
            }
            Expect(nameof(PartialRemesh_LockedVertices_BitExact), p);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // degenerate input

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroAreaTrianglesAndKinks_FiniteAndBounded(bool pro)
        {
            // https://github.com/pmp-library/pmp-library/issues/90 and /issues/89: zero-area triangles plus kinks gave
            // NaN, a crash or a near-endless loop. Rebuilt: a box whose grid vertices are, in turn, moved onto a
            // neighbour (zero-length edges), onto the midpoint of two neighbours (collinear: zero-area triangles), or
            // pushed out or in by 0.3 (kinks), remeshed with projection onto that input. Correct: within 10 s, valid,
            // closed, finite, positive volume, every vertex within 0.3 of the input.
            DMesh3 input = GridBox(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1), 7);
            var rnd = new Random(5);
            foreach (int v in input.VertexIndices().ToArray()) {
                int[] nbrs = input.VtxVerticesItr(v).ToArray();
                Vector3d q = input.GetVertex(v);
                switch (v % 5) {
                    case 0: input.SetVertex(v, input.GetVertex(nbrs[0])); break;
                    case 1: if (nbrs.Length >= 2) input.SetVertex(v, 0.5 * (input.GetVertex(nbrs[0]) + input.GetVertex(nbrs[1]))); break;
                    case 2: input.SetVertex(v, q * (1 + 0.3 * (rnd.NextDouble() - 0.5))); break;
                }
            }
            int degenerate = input.TriangleIndices().Count(t => input.GetTriArea(t) == 0);
            Assert.True(degenerate > 10);
            var p = new List<string>();
            DMesh3 m = new DMesh3(input);
            bool done = Timed.Finishes(() => Passes(Remesher(m, input, 0.25, pro: pro), 10), TimeSpan.FromSeconds(10));
            if (!done) p.Add("did not finish within 10 s");
            else {
                CheckBasic(m, input, p);
                if (m.Vertices().All(v => v.IsFinite)) {
                    double d = Invariance.VertexDistance(m, input);
                    if (!(d < 0.3)) p.Add($"distance to the input {d}");
                }
            }
            Expect(nameof(ZeroAreaTrianglesAndKinks_FiniteAndBounded) + (pro ? "_pro" : "_basic"), p);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SliversAndNeedles_Terminates(bool pro)
        {
            // https://github.com/CGAL/cgal/issues/8412: the repair of degenerate faces looped forever. Rebuilt: a capped
            // cylinder of 256 slices and one segment (needles in the fan caps and the sides), and a sheet of slivers
            // (a 30 x 30 grid whose odd columns sit 1e-9 beside the even ones), remeshed to their mean edge length,
            // with and without MinTriangleQuality. Correct: within 10 s each, valid, finite, closed (the cylinder), one
            // boundary loop (the sheet), area within 1% (the sheet).
            DMesh3 cyl = Cylinder(1, 2, 256);
            DMesh3 sheet = ReducerIssueCasesTests.Grid(30, 30, (i, j) => 0);
            foreach (int v in sheet.VertexIndices().ToArray()) {
                Vector3d q = sheet.GetVertex(v);
                int col = (int)Math.Round(q.x);
                sheet.SetVertex(v, new Vector3d(col / 2 + (col % 2) * 1e-9, q.y, 0));
            }
            Assert.True(sheet.TriangleIndices().Count(t => sheet.GetTriArea(t) < 1e-8) > 400);
            var p = new List<string>();
            foreach ((string what, DMesh3 input) in new[] { ("cylinder", cyl), ("sheet", sheet) })
                foreach (double q in new[] { 0.0, 0.2 }) {
                    double mean = input.EdgeIndices().Average(e => input.GetEdgePoint(e, 0).Distance(input.GetEdgePoint(e, 1)));
                    DMesh3 m = new DMesh3(input);
                    bool done = Timed.Finishes(() => Passes(Remesher(m, input, Math.Max(mean, 0.2), minQuality: q, pro: pro), 10), TimeSpan.FromSeconds(10));
                    if (!done) { p.Add($"{what} q{q}: did not finish within 10 s"); continue; }
                    CheckBasic(m, input, p);
                    if (what == "sheet") {
                        if (new MeshBoundaryLoops(m).Loops.Count != 1) p.Add($"{what} q{q}: not one boundary loop");
                        double a0 = Invariance.Area(input), a = Invariance.Area(m);
                        if (Math.Abs(a - a0) > 0.01 * a0) p.Add($"{what} q{q}: area {a}, input {a0}");
                    }
                }
            Expect(nameof(SliversAndNeedles_Terminates) + (pro ? "_pro" : "_basic"), p);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DegenerateFaceOnTheBorder_BoundaryConsistent(bool features)
        {
            // https://github.com/CGAL/cgal/issues/8836: a degenerate face on the border made the border status
            // inconsistent, then a crash. Rebuilt: a flat 6 x 6 grid with an extra boundary vertex exactly on the
            // midpoint of a bottom boundary edge (a zero-area triangle with two boundary edges), and one with two
            // coincident boundary vertices (a zero-length boundary edge), remeshed to edges of 0.5 with fixed boundary
            // edges or with PreserveBoundaryAndFeatures. Correct: valid, finite, one boundary loop with every vertex on
            // the input's outline (the square [0,6]^2 at z = 0), area 36.
            var p = new List<string>();
            foreach (bool coincident in new[] { false, true }) {
                DMesh3 input = ReducerIssueCasesTests.Grid(6, 6, (i, j) => 0);
                // the bottom boundary edge a -> b of triangle (a, b, x): the triangle (a, mid, b) across it makes ab
                // interior and leaves the border a -> mid -> b with a zero-area face on it
                int a = 2, b = 3;
                int mid = input.AppendVertex(coincident ? input.GetVertex(b) : 0.5 * (input.GetVertex(a) + input.GetVertex(b)));
                Assert.True(input.AppendTriangle(a, mid, b) >= 0);
                Assert.True(input.CheckValidity(false, FailMode.ReturnOnly));
                Assert.Equal(1, input.TriangleIndices().Count(tt => input.GetTriArea(tt) == 0));
                DMesh3 m = new DMesh3(input);
                Remesher r = Remesher(m, input, 0.5, features: features);
                if (!features) MeshConstraintUtil.FixAllBoundaryEdges(r);
                try { Passes(r, 10); } catch (Exception ex) { p.Add($"{ex.GetType().Name}: {ex.Message}"); continue; }
                CheckBasic(m, input, p);
                var loops = new MeshBoundaryLoops(m);
                if (loops.Loops.Count != 1) p.Add($"{loops.Loops.Count} boundary loops");
                foreach (int v in m.VertexIndices().Where(m.IsBoundaryVertex)) {
                    Vector3d q = m.GetVertex(v);
                    bool onOutline = q.z == 0 && (q.x == 0 || q.x == 6 || q.y == 0 || q.y == 6) && q.x >= 0 && q.x <= 6 && q.y >= 0 && q.y <= 6;
                    if (!onOutline) { p.Add($"boundary vertex {q} off the outline"); break; }
                }
                if (Math.Abs(Invariance.Area(m) - 36) > 1e-9) p.Add($"area {Invariance.Area(m)}");
            }
            Expect(nameof(DegenerateFaceOnTheBorder_BoundaryConsistent) + (features ? "_features" : "_fixed"), p);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwoConesTipToTip_ValidOutput(bool pro)
        {
            // https://github.com/CGAL/cgal/issues/8359: two cones tip to tip (a non-manifold vertex). Rebuilt: two closed
            // cones of 32 slices sharing their apex (ReducerIssueCasesTests.DoubleCone), remeshed to edges of 0.2 with
            // projection onto the input and its features kept. Correct: no exception, valid (the bowtie allowed),
            // closed, finite, the cones separate (no edge from above z = 0 to below it) and each of positive volume,
            // the shared apex still at the origin.
            DMesh3 input = ReducerIssueCasesTests.DoubleCone(32, 4);
            var p = new List<string>();
            DMesh3 m = new DMesh3(input);
            try { Passes(Remesher(m, input, 0.2, pro: pro), 10); } catch (Exception ex) { p.Add($"{ex.GetType().Name}: {ex.Message}"); }
            CheckBasic(m, input, p, allowBowties: true);
            foreach (int e in m.EdgeIndices()) {
                Index2i ev = m.GetEdgeV(e);
                if (m.GetVertex(ev.a).z * m.GetVertex(ev.b).z < 0) { p.Add("an edge joins the two cones"); break; }
            }
            double up = 0, down = 0;
            foreach (int t in m.TriangleIndices()) {
                double v = MeshMeasurements.VolumeArea(m, new[] { t }, m.GetVertex).x;
                if (m.GetTriCentroid(t).z > 0) up += v; else down += v;
            }
            if (!(up > 0 && down > 0)) p.Add($"cone volumes {up}, {down}");
            if (!m.Vertices().Any(v => v == Vector3d.Zero)) p.Add("the apex moved");
            Expect(nameof(TwoConesTipToTip_ValidOutput) + (pro ? "_pro" : "_basic"), p);
        }

        // ---------------------------------------------------------------------------------------------------------------
        // sharp, anisotropic and creased input

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.2)]
        public void LongThinSharpParts_NoInvertedTriangles(double minQuality)
        {
            // https://github.com/BrunoLevy/geogram/issues/357: a strongly anisotropic input with sharp edges remeshed:
            // many folded triangles. Rebuilt: a 20 x 0.5 x 0.5 box and a cylinder of radius 0.1 and length 10 (16
            // slices, rim rounded), long triangles along them, remeshed with their features kept to edges of 0.25 and 0.1.
            // Correct: closed, valid, positive volume, no triangle whose normal opposes the input's nearest triangle.
            var p = new List<string>();
            foreach ((string what, DMesh3 input, double edge) in new[] { ("box", GridBox(new Vector3d(-10, -0.25, -0.25), new Vector3d(10, 0.25, 0.25), 3), 0.25),
                ("cylinder", RoundedCylinder(0.1, 10, 16), 0.1) }) {
                DMesh3 m = new DMesh3(input);
                Passes(Remesher(m, input, edge, minQuality: minQuality), 20);
                CheckBasic(m, input, p);
                int inv = InvertedTriangles(m, input);
                if (inv > 0) p.Add($"{what}: {inv} inverted triangles, {MeshRepairMetrics.SelfIntersections(m)} self-intersecting pairs");
                if (m.TriangleCount < 2 * input.TriangleCount) p.Add($"{what}: not refined");
            }
            Expect(nameof(LongThinSharpParts_NoInvertedTriangles) + "_" + minQuality, p);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CreasedPart_BoundsKept(bool features)
        {
            // (the features half overlaps FeaturePreservationTests.Remesher_Box_WithFeatureConstraints_KeepsEdgesAndCorners;
            // new here: a 270 degree crease, the bounds and the unconstrained case)
            // https://github.com/MeshInspector/MeshLib/issues/4148 (prose only): remeshing creased parts shifted the
            // bounding box. Rebuilt: an L-shaped prism (creases of 90 and 270 degrees) remeshed with projection onto the
            // input to edges of 0.2. Correct: with the creases kept (PreserveBoundaryAndFeatures) the bounding box is the
            // input's (1e-9) and every vertex is within 1e-6 of the input and vice versa; without them the bounding box
            // moves by less than one target edge (the corners are rounded within an edge, not shifted).
            // the L: [0,2]x[0,1] union [0,1]x[0,2], extruded to z in [0,1]
            Vector2d[] outline = { new Vector2d(0, 0), new Vector2d(2, 0), new Vector2d(2, 1), new Vector2d(1, 1), new Vector2d(1, 2), new Vector2d(0, 2) };
            DMesh3 input = Prism(outline, 1);
            Assert.Equal(3.0, ResultChecks.Volume(input), 12);
            DMesh3 m = new DMesh3(input);
            Passes(Remesher(m, input, 0.2, features: features), 15);
            var p = new List<string>();
            CheckBasic(m, input, p);
            double shift = BoundsShift(Bounds(m), Bounds(input));
            double d = Invariance.VertexDistance(m, input);
            if (features) {
                if (shift > 1e-9) p.Add($"bounding box shifted by {shift}");
                if (d > 1e-6) p.Add($"distance to the input {d}");
                if (Math.Abs(ResultChecks.Volume(m) - 3) > 1e-6) p.Add($"volume {ResultChecks.Volume(m)}");
            } else {
                if (shift > 0.2) p.Add(FormattableString.Invariant($"bounding box shifted by {shift:F3}"));
                if (d > 0.2) p.Add(FormattableString.Invariant($"distance to the input {d:F3}"));
            }
            Expect(nameof(CreasedPart_BoundsKept) + (features ? "_features" : "_none"), p);
        }

        /// <summary> a closed prism over a simple counter-clockwise polygon, z in [0,h], caps as fans from its first point (the polygon must be star-shaped from there) </summary>
        static DMesh3 Prism(Vector2d[] poly, double h)
        {
            var m = new DMesh3();
            int n = poly.Length;
            int[] b = poly.Select(q => m.AppendVertex(new Vector3d(q.x, q.y, 0))).ToArray();
            int[] t = poly.Select(q => m.AppendVertex(new Vector3d(q.x, q.y, h))).ToArray();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(b[i], b[j], t[j]);
                m.AppendTriangle(b[i], t[j], t[i]);
            }
            for (int i = 1; i + 1 < n; ++i) {
                m.AppendTriangle(b[0], b[i + 1], b[i]);
                m.AppendTriangle(t[0], t[i], t[i + 1]);
            }
            Assert.True(m.IsClosed());
            return m;
        }
    
        public static IEnumerable<object[]> Similarities() => Similarity.Names();

        [Theory]
        [MemberData(nameof(Similarities))]
        public void TwoConesTipToTip_UnderSimilarity(string similarity)
        {
            // the bowtie pin of PreserveBoundaryAndFeatures (TwoConesTipToTip_ValidOutput) under Similarity.All(): the
            // apex is pinned (exactly the transformed origin), the cones stay separate; bit for bit the same as
            // untransformed under 2^k scales
            Similarity s = Similarity.ByName(similarity);
            DMesh3 source = ReducerIssueCasesTests.DoubleCone(32, 4);
            DMesh3 reference = new DMesh3(source);
            Passes(Remesher(reference, source, 0.2), 5);
            DMesh3 input = Invariance.Transformed(source, s);
            DMesh3 mesh = new DMesh3(input);
            Remesher r = Remesher(mesh, input, s.ApplyLength(0.2));
            Assert.True(r.Constraints.GetVertexConstraint(0).Fixed);
            Passes(r, 5);
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
        }
    }
}
