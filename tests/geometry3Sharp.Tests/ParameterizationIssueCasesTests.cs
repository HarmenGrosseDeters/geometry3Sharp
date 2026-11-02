using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for parameterization (LSCM, ARAP, SLIM, Tutte, BFF, UVAtlasGenerator, UVPacker) from the issue trackers
    /// of other libraries (docs/QA.md 2.11): only the failure mode, rebuilt here from simple procedural meshes. No code,
    /// test code or meshes of these projects are used. The issue URL is in the comment of each test.
    ///
    /// Every case checks the result itself: a success has finite UVs for every used vertex, the orientation of the
    /// surface (positive total signed UV area), no flipped triangle where the method promises it (Tutte, SLIM; and on
    /// easy inputs for all), atlas charts that are discs with UV area; unsupported input gets a failure status
    /// (InvalidTopology, InvalidMesh), never NaN or a crash. A case we fail is listed in KnownFailures with its finding
    /// (strict: a listed case that passes fails, so a fix removes the entry).
    /// </summary>
    public class ParameterizationIssueCasesTests
    {
        /// <summary> case name -> finding; a listed case must still fail </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
            [nameof(LowPolyCylinderWithoutSeam_StatusOrBoundedDistortion) + "_LscmAndArap"] =
                "finding P1: LSCM on a low-poly cylinder (annulus) collapses the unpinned boundary ring and reports Success",
            [nameof(LongThinTriangleInPlaneX0_ValidChart) + "_BffDiskTwoTriangles"] =
                "finding P2: BFF Disk on two needles sharing their long edge (negative cotan weight -5e4) gives a flipped sliver",
            [nameof(TwoIdenticalRectangularHoles_SymmetricResult) + "_LscmAutomaticPins"] =
                "finding P3: LSCM with automatic pins is not symmetric on a symmetric patch (pin dependence, 1e-4 relative)",
        };

        static void Expect(string name, Action check)
        {
            string failure = "";
            try { check(); } catch (Exception e) when (e is Xunit.Sdk.XunitException) { failure = e.Message; }
            if (KnownFailures.TryGetValue(name, out string finding))
                Assert.True(failure != "", $"{name} passes now: remove its entry ({finding}) from KnownFailures and docs/QA.md 2.11");
            else
                Assert.True(failure == "", name + ": " + failure);
        }

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(ParameterizationIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                Assert.True(names.Any(n => kv.Key == n || kv.Key.StartsWith(n + "_")), kv.Key + ": no such case");
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            }
        }

        // ---- methods and checks ----

        /// <summary> the parameterization methods; flipFree: the method promises no flipped triangle on a disc </summary>
        static readonly (string name, bool flipFree, Func<DMesh3, ParameterizationResult> run)[] Methods = {
            ("lscm", false, m => new LSCMParameterization(m).Compute()),
            ("arap", false, m => new ARAPParameterization(m).Compute()),
            ("slim", true, m => new SLIMParameterization(m).Compute()),
            ("tutte", true, m => new TutteEmbedding(m).Compute()),
            ("bff", false, m => new BoundaryFirstFlattening(m).Compute()),
            ("bff disk", false, m => new BoundaryFirstFlattening(m) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk }.Compute()),
        };

        static double SignedArea(Vector2d a, Vector2d b, Vector2d c) => 0.5 * (b - a).Cross(c - a);

        /// <summary>
        /// "" if r is a valid success: UVs for every vertex id, finite at every vertex of a triangle, (0,0) at unused
        /// vertices, a distortion, positive total signed UV area (the surface's orientation), and with flipFree no
        /// flipped or zero-area UV triangle on a non-degenerate surface triangle; otherwise the reasons
        /// </summary>
        static string Valid(DMesh3 m, ParameterizationResult r, bool flipFree)
        {
            var e = new List<string>();
            if (!r.Succeeded) return $"status {r.Status} ({r.Error})";
            if (r.UV == null || r.UV.Length < m.MaxVertexID) return "no UVs";
            var used = new HashSet<int>();
            foreach (int t in m.TriangleIndices()) { Index3i tv = m.GetTriangle(t); used.Add(tv.a); used.Add(tv.b); used.Add(tv.c); }
            int nonFinite = used.Count(v => !r.UV[v].IsFinite);
            if (nonFinite > 0) e.Add($"{nonFinite} of {used.Count} UVs not finite");
            foreach (int v in m.VertexIndices())
                if (!used.Contains(v) && r.UV[v] != Vector2d.Zero) { e.Add($"unused vertex {v} has UV {r.UV[v]}"); break; }
            if (r.Distortion == null) e.Add("no distortion");
            double total = 0; int flipped = 0;
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                double a = SignedArea(r.UV[tv.a], r.UV[tv.b], r.UV[tv.c]);
                total += a;
                if (!(a > 0) && m.GetTriArea(t) > 1e-12 * m.CachedBounds.DiagonalLength * m.CachedBounds.DiagonalLength) flipped++;
            }
            if (!(total > 0)) e.Add($"total signed UV area {total} (mirrored)");
            if (flipFree && flipped > 0) e.Add($"{flipped} flipped UV triangles");
            return string.Join("; ", e);
        }

        static int Flipped(DMesh3 m, ParameterizationResult r) =>
            m.TriangleIndices().Count(t => { Index3i tv = m.GetTriangle(t); return !(SignedArea(r.UV[tv.a], r.UV[tv.b], r.UV[tv.c]) > 0); });

        // ---- meshes ----

        /// <summary> polar disc: center, rings x sectors, pos(r, angle) </summary>
        static DMesh3 Polar(int rings, int sectors, Func<double, double, Vector3d> pos)
        {
            var mesh = new DMesh3();
            int center = mesh.AppendVertex(pos(0, 0));
            int[,] vid = new int[rings + 1, sectors];
            for (int i = 1; i <= rings; ++i)
                for (int j = 0; j < sectors; ++j)
                    vid[i, j] = mesh.AppendVertex(pos((double)i / rings, 2 * Math.PI * j / sectors));
            for (int j = 0; j < sectors; ++j)
                mesh.AppendTriangle(center, vid[1, j], vid[1, (j + 1) % sectors]);
            for (int i = 1; i < rings; ++i)
                for (int j = 0; j < sectors; ++j) {
                    int a = vid[i, j], b = vid[i, (j + 1) % sectors], c = vid[i + 1, j], d = vid[i + 1, (j + 1) % sectors];
                    if ((i + j) % 2 == 0) { mesh.AppendTriangle(a, c, d); mesh.AppendTriangle(a, d, b); }
                    else { mesh.AppendTriangle(a, c, b); mesh.AppendTriangle(b, c, d); }
                }
            return mesh;
        }

        static DMesh3 FlatDisc() => Polar(8, 24, (r, a) => new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0));

        /// <summary> grid over [0,nx] x [0,ny] cells of size h, z = f(x, y), diagonals mirrored at x = cx (symmetric under x -> 2 cx - x) </summary>
        static DMesh3 Grid(int nx, int ny, double h, Func<double, double, double> f, out int[,] ids, Func<int, int, bool> keepCell = null)
        {
            var m = new DMesh3();
            ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d(i * h, j * h, f(i * h, j * h)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    if (keepCell != null && !keepCell(i, j)) continue;
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    bool left = 2 * i + 1 < nx;
                    if (left == ((j % 2) == 0)) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(b, c, d); }
                }
            // drop vertices of removed cells
            foreach (int v in m.VertexIndices().ToList())
                if (m.GetVtxEdgeCount(v) == 0) m.RemoveVertex(v);
            return m;
        }

        static DMesh3 Icosphere(int subdivisions)
        {
            double p = (1 + Math.Sqrt(5)) / 2;
            var pts = new List<Vector3d> {
                new Vector3d(-1, p, 0), new Vector3d(1, p, 0), new Vector3d(-1, -p, 0), new Vector3d(1, -p, 0),
                new Vector3d(0, -1, p), new Vector3d(0, 1, p), new Vector3d(0, -1, -p), new Vector3d(0, 1, -p),
                new Vector3d(p, 0, -1), new Vector3d(p, 0, 1), new Vector3d(-p, 0, -1), new Vector3d(-p, 0, 1) };
            var tris = new List<Index3i> {
                new Index3i(0, 11, 5), new Index3i(0, 5, 1), new Index3i(0, 1, 7), new Index3i(0, 7, 10), new Index3i(0, 10, 11),
                new Index3i(1, 5, 9), new Index3i(5, 11, 4), new Index3i(11, 10, 2), new Index3i(10, 7, 6), new Index3i(7, 1, 8),
                new Index3i(3, 9, 4), new Index3i(3, 4, 2), new Index3i(3, 2, 6), new Index3i(3, 6, 8), new Index3i(3, 8, 9),
                new Index3i(4, 9, 5), new Index3i(2, 4, 11), new Index3i(6, 2, 10), new Index3i(8, 6, 7), new Index3i(9, 8, 1) };
            for (int s = 0; s < subdivisions; ++s) {
                var mid = new Dictionary<long, int>();
                Func<int, int, int> M = (a, b) => {
                    long key = Math.Min(a, b) * 100000L + Math.Max(a, b);
                    if (!mid.TryGetValue(key, out int v)) { v = pts.Count; pts.Add((pts[a] + pts[b]) * 0.5); mid[key] = v; }
                    return v;
                };
                var next = new List<Index3i>();
                foreach (Index3i t in tris) {
                    int ab = M(t.a, t.b), bc = M(t.b, t.c), ca = M(t.c, t.a);
                    next.Add(new Index3i(t.a, ab, ca)); next.Add(new Index3i(t.b, bc, ab));
                    next.Add(new Index3i(t.c, ca, bc)); next.Add(new Index3i(ab, bc, ca));
                }
                tris = next;
            }
            var m = new DMesh3();
            foreach (Vector3d q in pts) m.AppendVertex(q.Normalized);
            foreach (Index3i t in tris) m.AppendTriangle(t.a, t.b, t.c);
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        /// <summary> an axis-aligned box with n x n quads per face, welded, outward </summary>
        static DMesh3 Box(Vector3d size, int n)
        {
            var m = new DMesh3();
            var map = new Dictionary<Vector3i, int>();
            Func<Vector3i, int> vid = k => {
                if (!map.TryGetValue(k, out int v)) { v = m.AppendVertex(new Vector3d(size.x * k.x / n, size.y * k.y / n, size.z * k.z / n)); map[k] = v; }
                return v;
            };
            for (int axis = 0; axis < 3; ++axis)
                for (int side = 0; side < 2; ++side) {
                    Vector3i o = Vector3i.Zero, u = Vector3i.Zero, w = Vector3i.Zero;
                    o[axis] = side * n;
                    u[(axis + 1) % 3] = 1; w[(axis + 2) % 3] = 1;
                    for (int i = 0; i < n; ++i)
                        for (int j = 0; j < n; ++j) {
                            int a = vid(o + u * i + w * j), b = vid(o + u * (i + 1) + w * j), c = vid(o + u * (i + 1) + w * (j + 1)), d = vid(o + u * i + w * (j + 1));
                            if (side == 1) { m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d); }
                            else { m.AppendTriangle(a, c, b); m.AppendTriangle(a, d, c); }
                        }
                }
            Assert.True(ResultChecks.Volume(m) > 0);
            return m;
        }

        /// <summary>
        /// "" if every chart of the atlas is a disc (connected, Euler characteristic 1, so one boundary loop) with positive
        /// UV area, the atlas overlap-free and its UVs finite in [0,1]^2
        /// </summary>
        static string AtlasCharts(UVAtlasGenerator g, out List<(double uvArea, double area)> charts)
        {
            charts = new List<(double, double)>();
            var e = new List<string>();
            if (!(g.Status == UVAtlasStatus.Success || g.Status == UVAtlasStatus.DistortionLimitExceeded)) return $"status {g.Status} ({g.Error})";
            DMesh3 a = g.AtlasMesh;
            if (a == null || g.UV == null) return "no atlas";
            if (!a.CheckValidity(false, FailMode.ReturnOnly)) e.Add("invalid atlas mesh");
            if (g.Overlap == null || !g.Overlap.IsInjective) e.Add($"atlas overlaps: {g.Overlap?.OverlappingPairs} pairs, {g.Overlap?.InvertedTriangles} inverted, {g.Overlap?.DegenerateTriangles} degenerate");
            foreach (int v in a.VertexIndices())
                if (!(g.UV[v].IsFinite && g.UV[v].x >= 0 && g.UV[v].x <= 1 && g.UV[v].y >= 0 && g.UV[v].y <= 1)) { e.Add($"UV {g.UV[v]} at {v}"); break; }
            var byChart = new Dictionary<int, List<int>>();
            foreach (int t in a.TriangleIndices()) {
                int c = g.TriangleChart[t];
                if (!byChart.TryGetValue(c, out var l)) byChart[c] = l = new List<int>();
                l.Add(t);
            }
            if (byChart.Count != g.ChartCount) e.Add($"{byChart.Count} charts with triangles, ChartCount {g.ChartCount}");
            foreach (var kv in byChart) {
                var verts = new HashSet<int>(); var edges = new HashSet<long>();
                double uvArea = 0, area = 0;
                int[] parent = new int[a.MaxVertexID];
                for (int i = 0; i < parent.Length; ++i) parent[i] = i;
                Func<int, int> find = null; find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
                foreach (int t in kv.Value) {
                    Index3i tv = a.GetTriangle(t);
                    for (int k = 0; k < 3; ++k) {
                        int p = tv[k], q = tv[(k + 1) % 3];
                        verts.Add(p);
                        edges.Add(Math.Min(p, q) * (long)a.MaxVertexID + Math.Max(p, q));
                        parent[find(p)] = find(q);
                    }
                    uvArea += SignedArea(g.UV[tv.a], g.UV[tv.b], g.UV[tv.c]);
                    area += a.GetTriArea(t);
                }
                int chi = verts.Count - edges.Count + kv.Value.Count;
                int comps = verts.Select(v => find(v)).Distinct().Count();
                if (comps != 1 || chi != 1) e.Add($"chart {kv.Key}: {comps} components, Euler characteristic {chi}");
                if (!(uvArea > 0)) e.Add($"chart {kv.Key}: UV area {uvArea}");
                charts.Add((uvArea, area));
            }
            return string.Join("; ", e);
        }

        static UVAtlasGenerator Atlas(DMesh3 m, UVAtlasGenerator.Methods method)
        {
            var g = new UVAtlasGenerator(m) { Method = method };
            g.Compute();
            return g;
        }

        static readonly UVAtlasGenerator.Methods[] AtlasMethods = {
            UVAtlasGenerator.Methods.Auto, UVAtlasGenerator.Methods.Charts, UVAtlasGenerator.Methods.Cones, UVAtlasGenerator.Methods.SingleChart };

        // ---------------------------------------------------------------------------------------------------------------
        // b1: closed surfaces, several components, holes

        [Fact]
        public void ClosedSphere_ComponentsAndHoles_StatusOrValidMap()
        {
            // https://github.com/libigl/libigl/issues/1407 and /issues/1401: a closed sphere (no boundary), meshes with
            // several components or holes: crashes and numerical failures. Correct: a closed surface gets InvalidTopology
            // from every single-chart method; two discs and an annulus either InvalidTopology or a valid map; the atlas
            // cuts all of them into disc charts.
            Expect(nameof(ClosedSphere_ComponentsAndHoles_StatusOrValidMap), () => {
                DMesh3 sphere = Icosphere(2);
                DMesh3 two = FlatDisc();
                DMesh3 other = FlatDisc();
                MeshTransforms.Translate(other, new Vector3d(5, 0, 1));
                new MeshEditor(two).AppendMesh(other);
                DMesh3 annulus = Polar(6, 24, (r, a) => new Vector3d((1 + r) * Math.Cos(a), (1 + r) * Math.Sin(a), 0));
                annulus.RemoveVertex(0, true, false);       // the center: a hole of 24 sides
                foreach (var (name, flipFree, run) in Methods) {
                    ParameterizationResult r = run(sphere);
                    Assert.True(r.Status == ParameterizationStatus.InvalidTopology && r.UV == null, $"{name}, sphere: {r.Status}");
                    foreach (var (what, mesh) in new[] { ("two discs", two), ("annulus", annulus) }) {
                        r = run(mesh);
                        if (r.Status != ParameterizationStatus.InvalidTopology) {
                            string v = Valid(mesh, r, false);
                            Assert.True(v == "", $"{name}, {what}: {v}");
                        }
                    }
                }
                foreach (var (what, mesh) in new[] { ("sphere", sphere), ("two discs", two), ("annulus", annulus) })
                    foreach (var method in AtlasMethods) {
                        string v = AtlasCharts(Atlas(mesh, method), out _);
                        Assert.True(v == "", $"atlas {method}, {what}: {v}");
                    }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b2: unused vertices

        [Fact]
        public void UnusedVertices_AreIgnored()
        {
            // https://github.com/libigl/libigl/issues/343: four vertices that no face uses made the solve fail. Rebuilt: a
            // curved disc with four unused vertices, near it and far away (1e7). Correct: the same UVs as without them
            // (to 1e-9 of the chart size) and (0,0) at the unused ones.
            Expect(nameof(UnusedVertices_AreIgnored), () => {
                Func<DMesh3> cap = () => Polar(8, 24, (r, a) => new Vector3d(r * Math.Cos(a), r * Math.Sin(a), 0.4 * (1 - r * r)));
                foreach (double far in new[] { 0.5, 1e7 }) {
                    DMesh3 clean = cap(), extra = cap();
                    extra.AppendVertex(new Vector3d(far, 0, 0)); extra.AppendVertex(new Vector3d(0, -far, 0));
                    extra.AppendVertex(new Vector3d(far, far, far)); extra.AppendVertex(new Vector3d(-far, 0, far));
                    foreach (var (name, flipFree, run) in Methods) {
                        ParameterizationResult r0 = run(clean), r = run(extra);
                        string v = Valid(extra, r, flipFree);
                        Assert.True(v == "", $"{name}, far {far}: {v}");
                        double size = clean.VertexIndices().Max(i => r0.UV[i].Length);
                        double diff = clean.VertexIndices().Max(i => r0.UV[i].Distance(r.UV[i]));
                        Assert.True(diff <= 1e-9 * size, $"{name}, far {far}: UVs differ by {diff} (size {size})");
                    }
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b3: success with NaN UVs

        [Fact]
        public void DegeneratePatches_SuccessImpliesFiniteUVs()
        {
            // https://github.com/libigl/libigl/issues/2439: success was reported although most UVs were NaN. Rebuilt:
            // patches with degenerate parts: a strip of collinear (zero-area) triangles along a disc's diameter, coincident
            // vertices (a duplicated ring), a needle fan, and a patch of only collinear triangles. Correct: success
            // implies finite UVs (and the orientation); a patch without area gets InvalidMesh.
            Expect(nameof(DegeneratePatches_SuccessImpliesFiniteUVs), () => {
                // a disc whose ring 4 is duplicated at the same positions (zero-area quads between ring 4 and its copy)
                DMesh3 doubled = Polar(8, 24, (r, a) => {
                    double rr = r <= 0.5 ? r : r - 1.0 / 8;          // rings 4 and 5 at the same radius
                    return new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), 0.2 * rr * rr);
                });
                // a disc poked on many triangles at an edge midpoint (collinear triangles)
                DMesh3 poked = FlatDisc();
                foreach (int t in poked.TriangleIndices().Where(t => t % 5 == 0).ToList()) {
                    Index3i tv = poked.GetTriangle(t);
                    poked.PokeTriangle(t, new Vector3d(0.5, 0.5, 0), out _);
                }
                // a fan of needles: angles of 1e-9 at the center
                DMesh3 needles = Polar(4, 24, (r, a) => {
                    int j = (int)Math.Round(a / (2 * Math.PI / 24));
                    double aa = (j % 2 == 0 ? j : j - 1 + 1e-9) * 2 * Math.PI / 24;
                    return new Vector3d(r * Math.Cos(aa), r * Math.Sin(aa), 0);
                });
                var collinear = new DMesh3();
                for (int i = 0; i < 6; ++i) collinear.AppendVertex(new Vector3d(i, 0, 0));
                collinear.AppendTriangle(0, 1, 2); collinear.AppendTriangle(0, 2, 3); collinear.AppendTriangle(0, 3, 4);
                foreach (var (what, mesh) in new[] { ("doubled ring", doubled), ("poked", poked), ("needles", needles) })
                    foreach (var (name, flipFree, run) in Methods) {
                        ParameterizationResult r = run(mesh);
                        if (r.Succeeded) {
                            string v = Valid(mesh, r, false);
                            Assert.True(v == "", $"{name}, {what}: {v}");
                        } else
                            Assert.True(r.UV == null && r.Error != null, $"{name}, {what}: {r.Status} with UVs");
                        // the flip-free promise covers triangles that are degenerate on the surface too (SLIM)
                        if (name == "slim") Assert.True(r.Succeeded && Flipped(mesh, r) == 0, $"slim, {what}: {r.Status}, {(r.Succeeded ? Flipped(mesh, r) : -1)} flipped");
                    }
                foreach (var (name, flipFree, run) in Methods) {
                    ParameterizationResult r = run(collinear);
                    Assert.True(r.Status == ParameterizationStatus.InvalidMesh && r.UV == null, $"{name}, collinear: {r.Status}");
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b4: orientation

        [Fact]
        public void Orientation_FollowsTheSurface()
        {
            // https://github.com/libigl/libigl/issues/91: u and v swapped, so the map was mirrored. Correct: every UV
            // triangle has the orientation of its surface triangle (positive signed area): a flat disc facing +z, the
            // same disc with reversed triangles (facing -z, so mirrored in xy), a disc in the plane x = 0 and a
            // hemisphere, and LSCM with given pins.
            Expect(nameof(Orientation_FollowsTheSurface), () => {
                DMesh3 up = FlatDisc();
                DMesh3 down = new DMesh3(up);
                down.ReverseOrientation();
                DMesh3 side = Polar(8, 24, (r, a) => new Vector3d(0, r * Math.Cos(a), r * Math.Sin(a)));
                DMesh3 hemi = Polar(8, 24, (r, a) => {
                    double th = r * Math.PI / 2;
                    return new Vector3d(Math.Sin(th) * Math.Cos(a), Math.Sin(th) * Math.Sin(a), Math.Cos(th));
                });
                foreach (var (what, mesh) in new[] { ("up", up), ("down", down), ("side", side), ("hemisphere", hemi) }) {
                    foreach (var (name, flipFree, run) in Methods) {
                        ParameterizationResult r = run(mesh);
                        string v = Valid(mesh, r, true);
                        Assert.True(v == "", $"{name}, {what}: {v}");
                    }
                    // pins that would fit the mirror image too: the result must still keep the orientation
                    var pins = new Dictionary<int, Vector2d> { [1] = new Vector2d(0, 0), [mesh.MaxVertexID - 1] = new Vector2d(1, 0) };
                    ParameterizationResult rp = new LSCMParameterization(mesh) { PinnedVertices = pins }.Compute();
                    string vp = Valid(mesh, rp, true);
                    Assert.True(vp == "", $"lscm pinned, {what}: {vp}");
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b5: terrain with three peaks

        [Fact]
        public void ThreePeakTerrain_SlimAndTutteFlipFree()
        {
            // https://github.com/libigl/libigl/issues/680: a terrain with three peaks: the LSCM start had flips, SLIM failed,
            // ARAP overlapped. Rebuilt: a square grid with three tall narrow Gaussian peaks. Correct (as documented): SLIM
            // and Tutte succeed without flipped or degenerate UV triangles, Tutte also without overlaps; LSCM, ARAP and BFF
            // give finite UVs with the surface's orientation.
            Expect(nameof(ThreePeakTerrain_SlimAndTutteFlipFree), () => {
                Func<double, double, double> peaks = (x, y) => {
                    double z = 0;
                    foreach (var c in new[] { new Vector2d(0.3, 0.3), new Vector2d(0.7, 0.35), new Vector2d(0.5, 0.75) })
                        z += 0.6 * Math.Exp(-((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y)) / 0.004);
                    return z;
                };
                DMesh3 m = Grid(30, 30, 1.0 / 30, peaks, out _);
                foreach (var (name, flipFree, run) in Methods) {
                    ParameterizationResult r = run(m);
                    string v = Valid(m, r, flipFree);
                    Assert.True(v == "", $"{name}: {v}");
                    if (flipFree) {
                        UVOverlap o = UVOverlap.Compute(m, r.UV);
                        Assert.True(o.InvertedTriangles == 0 && o.DegenerateTriangles == 0, $"{name}: {o.InvertedTriangles} inverted, {o.DegenerateTriangles} degenerate");
                        if (name == "tutte") Assert.True(o.IsInjective, $"tutte: {o.OverlappingPairs} overlapping pairs");
                    }
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b6: cylinder without a seam

        [Fact]
        public void LowPolyCylinderWithoutSeam_StatusOrBoundedDistortion()
        {
            // https://github.com/GeometryCollective/boundary-first-flattening/issues/97: a low-poly cylinder (an annulus)
            // without a seam was flattened badly distorted. Rebuilt: an open cylinder of 8 sides and 3 rings. Correct: a
            // method for discs says InvalidTopology; a method that takes it (LSCM: annuli) gives a valid flip-free map of
            // bounded distortion (a cylinder maps conformally onto a planar annulus); the atlas cuts it into discs.
            var m = new DMesh3();
            int sides = 8, rings = 3;
            for (int j = 0; j <= rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    double a = 2 * Math.PI * i / sides;
                    m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0.6 * j));
                }
            for (int j = 0; j < rings; ++j)
                for (int i = 0; i < sides; ++i) {
                    int a = j * sides + i, b = j * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                }
            Action<string, ParameterizationResult> check = (name, r) => {
                if (r.Status == ParameterizationStatus.InvalidTopology) { Assert.Null(r.UV); return; }
                string v = Valid(m, r, true);
                Assert.True(v == "", $"{name}: {v}");
                Assert.True(r.Distortion.MeanConformal < 1.5, $"{name}: mean conformal distortion {r.Distortion.MeanConformal}");
            };
            Expect(nameof(LowPolyCylinderWithoutSeam_StatusOrBoundedDistortion), () => {
                foreach (var (name, flipFree, run) in Methods)
                    if (name != "lscm" && name != "arap") check(name, run(m));
                foreach (var method in AtlasMethods) {
                    UVAtlasGenerator g = Atlas(m, method);
                    string v = AtlasCharts(g, out _);
                    Assert.True(v == "", $"atlas {method}: {v}");
                    Assert.True(g.Distortion.MeanConformal < 1.5, $"atlas {method}: mean conformal {g.Distortion.MeanConformal}");
                }
            });
            string lscmCase = nameof(LowPolyCylinderWithoutSeam_StatusOrBoundedDistortion) + "_LscmAndArap";
            ParameterizationResult rl = new LSCMParameterization(m).Compute(), ra = new ARAPParameterization(m).Compute();
            Expect(lscmCase, () => { check("lscm", rl); check("arap", ra); });
            if (KnownFailures.ContainsKey(lscmCase)) {
                // finding P1: Success, but the boundary ring without the pins collapses onto a segment (all its v = 0);
                // ARAP, started from that map, keeps flipped triangles
                Assert.True(rl.Succeeded && Flipped(m, rl) > 0 && rl.Distortion.MaxConformal > 1e6, $"P1: {rl.Status}, {(rl.Succeeded ? Flipped(m, rl) : -1)} flipped");
                Assert.True(Enumerable.Range(rings * sides, sides).All(i => Math.Abs(rl.UV[i].y) < 1e-9 * rl.UV[0].Length), "P1: the far ring is not collapsed");
                Assert.True(ra.Succeeded && Flipped(m, ra) > 0, $"P1: arap {ra.Status}");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b7: two identical holes

        [Fact]
        public void TwoIdenticalRectangularHoles_SymmetricResult()
        {
            // https://github.com/GeometryCollective/boundary-first-flattening/issues/89: a patch with two identical
            // rectangular holes gave zero-area triangles and an asymmetric result. Rebuilt: a gently curved plate,
            // mirror-symmetric (also its triangulation) about x = 1.5, with two 2 x 3 cell holes at mirrored positions.
            // Correct: methods that take several boundaries give finite UVs without zero-area triangles, and the map is
            // symmetric: distances between UVs of mirrored vertex pairs agree (to 1e-6 relative); LSCM also with pins at a
            // mirrored pair. The atlas is valid.
            int nx = 18, ny = 12;
            double h = 3.0 / nx;
            Func<int, int, bool> keep = (i, j) => !((i >= 3 && i < 5 || i >= 13 && i < 15) && j >= 4 && j < 7);
            DMesh3 m = Grid(nx, ny, h, (x, y) => 0.3 * Math.Sin(y) + 0.1 * (x - 1.5) * (x - 1.5), out int[,] ids, keep);
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            var pairs = new List<(int, int)>();
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    if (m.IsVertex(ids[i, j]) && m.IsVertex(ids[nx - i, j])) pairs.Add((ids[i, j], ids[nx - i, j]));
            // the largest difference of UV distances between mirrored pairs, relative to the UV size
            Func<string, ParameterizationResult, double> asymmetry = (name, r) => {
                string v = Valid(m, r, true);
                Assert.True(v == "", $"{name}: {v}");
                UVOverlap o = UVOverlap.Compute(m, r.UV);
                Assert.True(o.DegenerateTriangles == 0, $"{name}: {o.DegenerateTriangles} zero-area UV triangles");
                var box = new AxisAlignedBox2d(r.UV[m.VertexIndices().First()]);
                foreach (int a in m.VertexIndices()) box.Contain(r.UV[a]);
                var rnd = new Random(5);
                double worst = 0;
                for (int k = 0; k < 400; ++k) {
                    var (a, ma) = pairs[rnd.Next(pairs.Count)];
                    var (b, mb) = pairs[rnd.Next(pairs.Count)];
                    worst = Math.Max(worst, Math.Abs(r.UV[a].Distance(r.UV[b]) - r.UV[ma].Distance(r.UV[mb])));
                }
                return worst / box.DiagonalLength;
            };
            Expect(nameof(TwoIdenticalRectangularHoles_SymmetricResult), () => {
                int accepted = 0;
                foreach (var (name, flipFree, run) in Methods) {
                    if (name == "lscm") continue;
                    ParameterizationResult r = run(m);
                    if (r.Status == ParameterizationStatus.InvalidTopology) { Assert.Null(r.UV); continue; }
                    accepted++;
                    double asym = asymmetry(name, r);
                    Assert.True(asym <= 1e-6, $"{name}: mirrored distances differ by {asym} of the size");
                }
                Assert.True(accepted > 0, "no method takes a patch with holes");
                var pins = new Dictionary<int, Vector2d> { [ids[0, 0]] = Vector2d.Zero, [ids[nx, 0]] = new Vector2d(3, 0) };
                double asymPinned = asymmetry("lscm, mirrored pins", new LSCMParameterization(m) { PinnedVertices = pins }.Compute());
                Assert.True(asymPinned <= 1e-9, $"lscm, mirrored pins: mirrored distances differ by {asymPinned} of the size");
                string va = AtlasCharts(Atlas(m, UVAtlasGenerator.Methods.Auto), out _);
                Assert.True(va == "", $"atlas: {va}");
            });
            string lscmCase = nameof(TwoIdenticalRectangularHoles_SymmetricResult) + "_LscmAutomaticPins";
            double asymAuto = double.NaN;
            Expect(lscmCase, () => {
                asymAuto = asymmetry("lscm", new LSCMParameterization(m).Compute());
                Assert.True(asymAuto <= 1e-6, $"lscm: mirrored distances differ by {asymAuto} of the size");
            });
            if (KnownFailures.ContainsKey(lscmCase))
                // finding P3: the automatic pins are not a mirrored pair, and the LSCM minimizer depends on the pins
                Assert.True(asymAuto > 1e-6 && asymAuto < 1e-3, $"P3: asymmetry {asymAuto}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b8: saddles

        [Fact]
        public void StrongSaddles_LocallyInjective()
        {
            // https://github.com/GeometryCollective/boundary-first-flattening/issues/81 and /issues/61: strong negative
            // curvature (saddles) gave UV overlaps. Rebuilt: a monkey saddle disc z = 2 (x^3 - 3 x y^2) and a saddle
            // z = 1.5 (x^2 - y^2). Correct: SLIM and Tutte locally injective (no flipped or degenerate triangle), Tutte also
            // globally (it promises a bijection); the others finite with the orientation of the surface. The atlas is
            // overlap-free (it promises that).
            Expect(nameof(StrongSaddles_LocallyInjective), () => {
                var surfaces = new (string, Func<double, double, double>)[] {
                    ("monkey", (x, y) => 2 * (x * x * x - 3 * x * y * y)), ("saddle", (x, y) => 1.5 * (x * x - y * y)) };
                foreach (var (what, f) in surfaces) {
                    DMesh3 m = Polar(12, 36, (r, a) => { double x = r * Math.Cos(a), y = r * Math.Sin(a); return new Vector3d(x, y, f(x, y)); });
                    foreach (var (name, flipFree, run) in Methods) {
                        ParameterizationResult r = run(m);
                        string v = Valid(m, r, flipFree);
                        Assert.True(v == "", $"{name}, {what}: {v}");
                        if (flipFree) {
                            UVOverlap o = UVOverlap.Compute(m, r.UV);
                            Assert.True(o.InvertedTriangles == 0 && o.DegenerateTriangles == 0, $"{name}, {what}: {o.InvertedTriangles} inverted, {o.DegenerateTriangles} degenerate");
                            if (name == "tutte") Assert.True(o.IsInjective, $"tutte, {what}: {o.OverlappingPairs} overlapping pairs");
                        }
                    }
                    string va = AtlasCharts(Atlas(m, UVAtlasGenerator.Methods.Auto), out _);
                    Assert.True(va == "", $"atlas, {what}: {va}");
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b9: closed icosphere atlas

        [Fact]
        public void ClosedIcosphere_EveryChartIsADisc()
        {
            // https://github.com/jpcy/xatlas/issues/146: on a closed icosphere one chart had no boundary, which crashed the
            // flattening. Correct: every chart is a disc with one boundary loop and UV area, for every atlas method and
            // for a coarse (20 triangles) and a finer icosphere.
            Expect(nameof(ClosedIcosphere_EveryChartIsADisc), () => {
                foreach (int sub in new[] { 0, 1, 3 }) {
                    DMesh3 m = Icosphere(sub);
                    foreach (var method in AtlasMethods) {
                        UVAtlasGenerator g = Atlas(m, method);
                        string v = AtlasCharts(g, out _);
                        Assert.True(v == "", $"icosphere {sub}, {method}: {v}");
                        Assert.Equal(m.TriangleCount, g.AtlasMesh.TriangleCount);
                    }
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b10: long thin triangle in the plane x = 0

        [Fact]
        public void LongThinTriangleInPlaneX0_ValidChart()
        {
            // https://github.com/jpcy/xatlas/issues/130: a long thin triangle in the plane x = 0: the sign of its normal
            // flipped and the atlas crashed. Rebuilt: one triangle (0,0,0), (0,1000,0), (0,500,0.01) (aspect 1e5), and two
            // such triangles sharing the long edge. Correct: every method gives a valid map with the orientation of the
            // triangles, the atlas a valid chart with UV area.
            var one = new DMesh3();
            one.AppendVertex(new Vector3d(0, 0, 0)); one.AppendVertex(new Vector3d(0, 1000, 0)); one.AppendVertex(new Vector3d(0, 500, 0.01));
            one.AppendTriangle(0, 1, 2);
            var two = new DMesh3(one);
            two.AppendVertex(new Vector3d(0, 500, -0.01));
            two.AppendTriangle(1, 0, 3);
            Expect(nameof(LongThinTriangleInPlaneX0_ValidChart), () => {
                foreach (var (what, m) in new[] { ("one", one), ("two", two) }) {
                    foreach (var (name, flipFree, run) in Methods) {
                        if (name == "bff disk" && what == "two") continue;
                        string v = Valid(m, run(m), true);
                        Assert.True(v == "", $"{name}, {what}: {v}");
                    }
                    foreach (var method in AtlasMethods) {
                        string v = AtlasCharts(Atlas(m, method), out _);
                        Assert.True(v == "", $"atlas {method}, {what}: {v}");
                    }
                }
            });
            string diskCase = nameof(LongThinTriangleInPlaneX0_ValidChart) + "_BffDiskTwoTriangles";
            ParameterizationResult rd = new BoundaryFirstFlattening(two) { BoundaryMode = BoundaryFirstFlattening.BoundaryModes.Disk }.Compute();
            Expect(diskCase, () => {
                string v = Valid(two, rd, true);
                Assert.True(v == "", $"bff disk, two: {v}");
            });
            if (KnownFailures.ContainsKey(diskCase)) {
                // finding P2: Success, one triangle flipped, and the four UVs a sliver about 1.5e6 from the origin
                // instead of a quadrilateral inscribed in a circle
                Assert.True(rd.Succeeded && Flipped(two, rd) == 1, $"P2: {rd.Status}");
                Assert.True(rd.UV.Take(4).All(q => q.Length > 1e5), "P2: the UVs are no longer far away");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b11: packing keeps the winding

        [Fact]
        public void Packing_RotatedChartsKeepTheirWinding()
        {
            // https://github.com/jpcy/xatlas/issues/121: the packing swapped x and y of some charts, which mirrors them.
            // Rebuilt: 12 charts of different aspect ratios (tall and wide strips, which pack best rotated by 90 degrees),
            // each with a given flip-free UV layout. Correct: after packing every UV triangle still has positive signed
            // area, and each chart is its input up to a rotation, translation and scale (distances in ratio).
            Expect(nameof(Packing_RotatedChartsKeepTheirWinding), () => {
                var m = new DMesh3();
                var uv = new List<Vector2d>();
                var rnd = new Random(3);
                var chartVerts = new List<List<int>>();
                for (int c = 0; c < 12; ++c) {
                    double w = c % 2 == 0 ? 0.1 + 0.05 * c : 1.5, hh = c % 2 == 0 ? 1.5 : 0.1 + 0.05 * c;
                    int nx = 4, ny = 3;
                    var ids = new int[nx + 1, ny + 1];
                    var verts = new List<int>();
                    for (int j = 0; j <= ny; ++j)
                        for (int i = 0; i <= nx; ++i) {
                            double x = w * i / nx + 0.01 * rnd.NextDouble() * w, y = hh * j / ny + 0.01 * rnd.NextDouble() * hh;
                            ids[i, j] = m.AppendVertex(new Vector3d(x + 10 * c, y, 0.05 * Math.Sin(x + y)));
                            uv.Add(new Vector2d(x, y));
                            verts.Add(ids[i, j]);
                        }
                    for (int j = 0; j < ny; ++j)
                        for (int i = 0; i < nx; ++i) {
                            m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                            m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                        }
                    chartVerts.Add(verts);
                }
                Assert.True(m.TriangleIndices().All(t => { Index3i tv = m.GetTriangle(t); return SignedArea(uv[tv.a], uv[tv.b], uv[tv.c]) > 0; }));
                var p = new UVPacker(m) { UV = uv, AllowRotation = true };
                ParameterizationResult r = p.Compute();
                Assert.True(r.Succeeded, $"{r.Status} {r.Error}");
                Assert.True(p.ChartCount == 12, $"{p.ChartCount} charts");
                int flipped = Flipped(m, r);
                Assert.True(flipped == 0, $"{flipped} UV triangles mirrored by the packing");
                foreach (var verts in chartVerts) {
                    double ratio = r.UV[verts[0]].Distance(r.UV[verts[verts.Count - 1]]) / uv[verts[0]].Distance(uv[verts[verts.Count - 1]]);
                    for (int k = 1; k < verts.Count; ++k) {
                        double d0 = uv[verts[0]].Distance(uv[verts[k]]), d = r.UV[verts[0]].Distance(r.UV[verts[k]]);
                        Assert.True(Math.Abs(d - ratio * d0) <= 1e-9 * ratio * d0 + 1e-12, $"chart is not a similar copy: {d} vs {ratio * d0}");
                    }
                }
                Assert.True(r.UV.All(q => q.IsFinite && q.x >= 0 && q.x <= 1 && q.y >= 0 && q.y <= 1));
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // b12: thin tall box

        [Fact]
        public void ThinTallBox_CapChartsKeepTheirArea()
        {
            // https://github.com/jpcy/xatlas/issues/145: a thin tall box: the charts of its small caps collapsed to a
            // point. Rebuilt: boxes 0.01 x 0.01 x 10 (12 triangles, and 4 x 4 quads per face). Correct: every chart has UV
            // area, and the texel density is uniform: UV area / surface area per chart within a factor 2 of the
            // atlas' (the caps are 1e-4 of the surface: collapsed or shrunk caps fail).
            Expect(nameof(ThinTallBox_CapChartsKeepTheirArea), () => {
                foreach (int n in new[] { 1, 4 }) {
                    DMesh3 m = Box(new Vector3d(0.01, 0.01, 10), n);
                    foreach (var method in AtlasMethods) {
                        UVAtlasGenerator g = Atlas(m, method);
                        string v = AtlasCharts(g, out var charts);
                        Assert.True(v == "", $"n {n}, {method}: {v}");
                        double density = charts.Sum(c => c.uvArea) / charts.Sum(c => c.area);
                        foreach (var c in charts)
                            Assert.True(c.uvArea / c.area > 0.5 * density && c.uvArea / c.area < 2 * density,
                                $"n {n}, {method}: chart of area {c.area} has density {c.uvArea / c.area}, atlas {density}");
                    }
                }
            });
        }
    }
}
