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
    /// Hard cases for geodesics and intrinsic triangulations (MeshGeodesicDistance, MeshGeodesicPath, VectorHeatMethod,
    /// IntrinsicTriangulation and its Delaunay refinement) from the issue trackers of other libraries (docs/QA.md 2.11):
    /// only the failure mode, rebuilt here from simple procedural meshes. No code, test code or meshes of these projects
    /// are used. The issue URL is in the comment of each test.
    ///
    /// Every case checks the result itself: finite, non-negative distances with 0 at the source, distances against the
    /// exact ones where the geometry gives them (flat: |p - s|; MeshGeodesicPath is exact in the intrinsic geometry),
    /// the heat method within its documented accuracy (2% plus one mean edge length near boundaries), paths on the
    /// surface and ending at their source. A case we fail is listed in KnownFailures with its finding (strict: a listed
    /// case that passes fails, so a fix removes the entry).
    /// </summary>
    public class GeodesicsIssueCasesTests
    {
        /// <summary> case name -> finding; a listed case must still fail </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
            [nameof(TinyAreaTriangles_VectorTransportFinite) + "_CoincidentVertices"] =
                "finding G2: tangent frames of vertices with a coincident neighbour (zero-length input edge) are arbitrary",
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

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(GeodesicsIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                Assert.True(names.Any(n => kv.Key == n || kv.Key.StartsWith(n + "_")), kv.Key + ": no such case");
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            }
        }

        // ---- meshes ----

        /// <summary>
        /// nx x ny cells over [0, sx] x [0, sy], z = h(x, y); all diagonals the same way (alternate: alternating), ids[i, j]
        /// </summary>
        static DMesh3 Grid(int nx, int ny, double sx, double sy, Func<double, double, double> h, bool alternate, out int[,] ids)
        {
            var m = new DMesh3();
            ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    double x = sx * i / nx, y = sy * j / ny;
                    ids[i, j] = m.AppendVertex(new Vector3d(x, y, h(x, y)));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = ids[i, j], b = ids[i + 1, j], c = ids[i + 1, j + 1], d = ids[i, j + 1];
                    if (alternate && (i + j) % 2 == 1) {
                        m.AppendTriangle(a, b, d);
                        m.AppendTriangle(b, c, d);
                    } else {
                        m.AppendTriangle(a, b, c);
                        m.AppendTriangle(a, c, d);
                    }
                }
            return m;
        }

        static readonly Func<double, double, double> Flat = (x, y) => 0;

        /// <summary> flat polar disc of radius R: a center vertex, rings of 6k vertices </summary>
        static DMesh3 Disc(double R, int rings, out int center, out List<int> boundary)
        {
            var m = new DMesh3();
            center = m.AppendVertex(Vector3d.Zero);
            var prev = new List<int> { center };
            for (int r = 1; r <= rings; ++r) {
                int n = 6 * r;
                var ring = new List<int>();
                for (int k = 0; k < n; ++k) {
                    double a = 2 * Math.PI * k / n;
                    ring.Add(m.AppendVertex(new Vector3d(R * r / rings * Math.Cos(a), R * r / rings * Math.Sin(a), 0)));
                }
                // stitch prev (6(r-1) vertices) to ring (6r): per sector of 60 degrees
                if (r == 1) {
                    for (int k = 0; k < 6; ++k) m.AppendTriangle(center, ring[k], ring[(k + 1) % 6]);
                } else {
                    int np = prev.Count;
                    int pi = 0, ri = 0;
                    // walk both rings by angle
                    while (pi < np || ri < n) {
                        double ap = 2 * Math.PI * (pi + 1) / np, ar = 2 * Math.PI * (ri + 1) / n;
                        if (ri < n && (pi >= np || ar <= ap + 1e-12)) {
                            m.AppendTriangle(prev[pi % np], ring[ri], ring[(ri + 1) % n]);
                            ri++;
                        } else {
                            m.AppendTriangle(prev[pi % np], ring[ri % n], prev[(pi + 1) % np]);
                            pi++;
                        }
                    }
                }
                prev = ring;
            }
            boundary = prev;
            return m;
        }

        /// <summary> a triangle at the edge a-b </summary>
        static int EdgeTriangle(DMesh3 m, int a, int b) => m.GetEdgeT(m.FindEdge(a, b)).a;

        static double MeanEdge(DMesh3 m) => m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        static double PolylineLength(List<Vector3d> p)
        {
            double l = 0;
            for (int k = 1; k < p.Count; ++k) l += p[k].Distance(p[k - 1]);
            return l;
        }

        /// <summary> heat distances: finite and non-negative at every vertex with triangles, 0 at the source </summary>
        static double[] HeatDistances(DMesh3 m, int source, MeshGeodesicDistance geo = null)
        {
            geo = geo ?? new MeshGeodesicDistance(m);
            Assert.True(geo.Compute(new[] { source }), "Compute false");
            foreach (int v in m.VertexIndices()) {
                if (m.GetVtxEdgeCount(v) == 0) continue;
                double d = geo.Distance[v];
                Assert.True(d >= 0 && !double.IsInfinity(d), $"distance {d} at vertex {v}");
            }
            Assert.Equal(0, geo.Distance[source]);
            return geo.Distance;
        }

        /// <summary> exact geodesic (FlipOut) between two vertices: Success, a path from s to t, length = polyline length </summary>
        static double ExactPath(DMesh3 m, int s, int t, out MeshGeodesicPath g)
        {
            g = new MeshGeodesicPath(m);
            GeodesicPathStatus st = g.Compute(s, t);
            Assert.True(st == GeodesicPathStatus.Success, $"path {s} -> {t}: {st}");
            Assert.True(g.Path.All(p => p.IsFinite));
            Assert.Equal(m.GetVertex(s), g.Path[0]);
            Assert.Equal(m.GetVertex(t), g.Path[g.Path.Count - 1]);
            double tol = 1e-9 * g.Length + 64 * g.Path.Count * g.Triangulation.MollificationDelta;
            Assert.True(Math.Abs(PolylineLength(g.Path) - g.Length) <= tol, $"polyline length {PolylineLength(g.Path):R} differs from Length {g.Length:R}");
            return g.Length;
        }

        /// <summary> the polyline has no zero-length segment (a repeated point gives NaN tangents downstream) </summary>
        static void AssertNoRepeatedPoints(MeshGeodesicPath g)
        {
            for (int k = 1; k < g.Path.Count; ++k)
                Assert.True(g.Path[k].Distance(g.Path[k - 1]) > 1e-9 * g.Length, $"path point {k} repeats point {k - 1}: {g.Path[k]}");
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a1: unreachable targets

        [Fact]
        public void TwoDisjointTriangles_TargetIsUnreachable()
        {
            // https://github.com/libigl/libigl/issues/2554: two triangles that share nothing, the source in one and the
            // target in the other: an assert or a distance 0. The heat distances and paths of components without a source
            // are covered by GeodesicDistanceTests.ComponentWithoutSource_IsNaN_AndPathIsTheStartPoint; new here: the
            // vector heat method on two single triangles. Correct (documented): NaN on the other triangle, finite on the
            // source's, the extended value of the source there.
            Expect(nameof(TwoDisjointTriangles_TargetIsUnreachable), () => {
                var m = new DMesh3();
                int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(0, 1, 0));
                int d = m.AppendVertex(new Vector3d(3, 0, 0.5)), e = m.AppendVertex(new Vector3d(4, 0, 0.5)), f = m.AppendVertex(new Vector3d(3, 1, 0.5));
                m.AppendTriangle(a, b, c);
                m.AppendTriangle(d, e, f);
                var vh = new VectorHeatMethod(m);
                Vector2d[] tr = vh.TransportVector(a, new Vector2d(1, 0));
                Vector2d[] log = vh.ComputeLogMap(a);
                double[] ext = vh.ExtendScalar(new[] { new KeyValuePair<int, double>(a, 7) });
                foreach (int v in new[] { a, b, c }) {
                    Assert.True(tr[v].IsFinite && log[v].IsFinite, $"vertex {v}");
                    Assert.Equal(7, ext[v], 9);
                }
                Assert.True(log[b].Length > 0.5 && log[b].Length < 1.5, $"log map length at b: {log[b].Length}");
                foreach (int v in new[] { d, e, f })
                    Assert.True(double.IsNaN(tr[v].x) && double.IsNaN(log[v].x) && double.IsNaN(ext[v]), $"unreachable vertex {v}");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a3: zero-length intervals: a source inside a face, a box with one hole, a boundary-vertex source

        [Fact]
        public void FaceSource_DistancesFiniteAndEuclidean()
        {
            // https://github.com/libigl/libigl/issues/2535: a source inside a face gave a zero-length interval and an assert.
            // Our sources are vertices: the face point is inserted (PokeTriangle), once at the centroid and once close
            // to an edge and a corner (barycentric 1e-9). Correct: on a flat grid |p - s| (exact paths to rounding, the heat
            // method within 2% plus half an edge length: an interior source).
            Expect(nameof(FaceSource_DistancesFiniteAndEuclidean), () => {
                foreach (Vector3d bary in new[] { new Vector3d(1.0 / 3, 1.0 / 3, 1.0 / 3), new Vector3d(1 - 2e-9, 1e-9, 1e-9), new Vector3d(0.5, 0.5 - 1e-9, 1e-9) }) {
                    DMesh3 m = Grid(12, 12, 1, 1, Flat, true, out int[,] ids);
                    int tid = EdgeTriangle(m, ids[6, 6], ids[7, 6]);
                    Assert.Equal(MeshResult.Ok, m.PokeTriangle(tid, bary, out DMesh3.PokeTriangleInfo info));
                    int s = info.new_vid;
                    Vector3d ps = m.GetVertex(s);
                    double h = MeanEdge(m);
                    double[] d = HeatDistances(m, s);
                    foreach (int v in m.VertexIndices()) {
                        double ex = m.GetVertex(v).Distance(ps);
                        Assert.True(Math.Abs(d[v] - ex) <= 0.02 * ex + h, $"bary {bary}, vertex {v}: heat {d[v]}, exact {ex}");
                    }
                    foreach (int t in new[] { ids[0, 0], ids[12, 0], ids[12, 12], ids[0, 12], ids[6, 0], ids[12, 7] }) {
                        double len = ExactPath(m, s, t, out MeshGeodesicPath g);
                        double ex = m.GetVertex(t).Distance(ps);
                        // the slivers at a point 1e-9 from an edge are mollified (edge lengths + delta, about 1e-6 h): that
                        // lifts the point off the edge intrinsically by about sqrt(2 delta h), and the distances by as much
                        double delta = g.Triangulation.MollificationDelta;
                        Assert.True(delta <= 1e-5 * h, $"bary {bary}: mollification {delta}");
                        Assert.True(Math.Abs(len - ex) <= 1e-12 * ex + 2 * Math.Sqrt(2 * delta * h), $"bary {bary}, path to {t}: {len:R} vs {ex:R} (delta {delta})");
                    }
                }
            });
        }

        /// <summary> the cube [0,1]^3 without its top face, n x n cells per face, welded </summary>
        static DMesh3 OpenBox(int n)
        {
            var m = new DMesh3();
            var map = new Dictionary<Vector3i, int>();
            Func<Vector3i, int> vid = (k) => {
                if (!map.TryGetValue(k, out int v)) { v = m.AppendVertex(new Vector3d(k.x, k.y, k.z) / n); map[k] = v; }
                return v;
            };
            // faces: origin, two axes (counter-clockwise seen from outside)
            var faces = new List<(Vector3i o, Vector3i u, Vector3i w)> {
                (new Vector3i(0, 0, 0), new Vector3i(0, 1, 0), new Vector3i(1, 0, 0)),     // bottom z = 0, normal -z
                (new Vector3i(0, 0, 0), new Vector3i(1, 0, 0), new Vector3i(0, 0, 1)),     // y = 0, normal -y
                (new Vector3i(n, 0, 0), new Vector3i(0, 1, 0), new Vector3i(0, 0, 1)),     // x = 1, normal +x
                (new Vector3i(n, n, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 0, 1)),    // y = 1, normal +y
                (new Vector3i(0, n, 0), new Vector3i(0, -1, 0), new Vector3i(0, 0, 1)),    // x = 0, normal -x
            };
            foreach (var f in faces)
                for (int i = 0; i < n; ++i)
                    for (int j = 0; j < n; ++j) {
                        int a = vid(f.o + f.u * i + f.w * j), b = vid(f.o + f.u * (i + 1) + f.w * j);
                        int c = vid(f.o + f.u * (i + 1) + f.w * (j + 1)), d = vid(f.o + f.u * i + f.w * (j + 1));
                        Assert.True(m.AppendTriangle(a, b, c) >= 0);
                        Assert.True(m.AppendTriangle(a, c, d) >= 0);
                    }
            return m;
        }

        [Fact]
        public void BoxWithOneHole_BoundarySources_FiniteDistances()
        {
            // https://github.com/libigl/libigl/issues/1013 (a box with one hole) and
            // https://github.com/nmwsharp/geometry-central/issues/212 (a source on a boundary vertex): zero-length
            // intervals and asserts. Rebuilt: the unit cube without its top, sources at a rim corner and a rim midpoint
            // (exact paths on a closed cube: MeshGeodesicPathTests.Cube_OppositeCorners_SqrtFive). Correct: finite
            // distances; heat distances at least the straight distance less an edge length, and at most 2% plus an edge
            // length above the geodesic from the Dijkstra start (an upper bound: it is not always the shortest one).
            Expect(nameof(BoxWithOneHole_BoundarySources_FiniteDistances), () => {
                int n = 8;
                DMesh3 m = OpenBox(n);
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                Func<double, double, double, int> at = (x, y, z) => m.VertexIndices().First(v => m.GetVertex(v).Distance(new Vector3d(x, y, z)) < 1e-9);
                double h = MeanEdge(m);
                foreach (int s in new[] { at(0, 0, 1), at(0.5, 0, 1) }) {
                    Assert.True(m.IsBoundaryVertex(s));
                    double[] d = HeatDistances(m, s);
                    var targets = new[] { at(1, 1, 0), at(1, 0, 0), at(0, 1, 0), at(1, 1, 1), at(0.5, 1, 1), at(0.5, 0.5, 0), at(1, 0.5, 0.5) };
                    foreach (int t in targets) {
                        double len = ExactPath(m, s, t, out _);
                        Assert.True(len >= m.GetVertex(t).Distance(m.GetVertex(s)) * (1 - 1e-12), $"path {s} -> {t} shorter than the straight line");
                        // the path from the Dijkstra start is a geodesic, not always the shortest one: an upper bound
                        double straight = m.GetVertex(t).Distance(m.GetVertex(s));
                        Assert.True(d[t] <= 1.02 * len + h && d[t] >= straight - h, $"source {s}, target {t}: heat {d[t]}, path {len}, straight {straight}");
                    }
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a4: disc with a boundary

        [Fact]
        public void Disc_BoundarySource_TerminatesWithChordDistances()
        {
            // https://github.com/nmwsharp/geometry-central/issues/119: on a disc with a boundary the propagation never
            // ended. Correct: it terminates with finite distances; the disc is a convex polygon, so the geodesics from a
            // boundary vertex are its chords (exact paths to rounding; heat within 2% plus one edge length) and from the
            // center the radius.
            Expect(nameof(Disc_BoundarySource_TerminatesWithChordDistances), () => {
                var sw = Stopwatch.StartNew();
                DMesh3 m = Disc(1, 10, out int center, out List<int> boundary);
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                Assert.Equal(1, m.TriangleIndices().Count() - m.EdgeIndices().Count() + m.VertexIndices().Count());
                double h = MeanEdge(m);
                int s = boundary[0];
                double[] d = HeatDistances(m, s);
                for (int k = 1; k < boundary.Count; k += 2) {
                    int t = boundary[k];
                    double chord = m.GetVertex(t).Distance(m.GetVertex(s));
                    double len = ExactPath(m, s, t, out _);
                    Assert.True(Math.Abs(len - chord) <= 1e-12, $"path to boundary vertex {k}: {len:R} vs chord {chord:R}");
                    Assert.True(Math.Abs(d[t] - chord) <= 0.02 * chord + h, $"heat to boundary vertex {k}: {d[t]} vs chord {chord}");
                }
                double[] dc = HeatDistances(m, center);
                foreach (int t in boundary)
                    Assert.True(Math.Abs(dc[t] - 1) <= 0.02 + h, $"heat from the center: {dc[t]}");
                Assert.True(sw.Elapsed.TotalSeconds < 10, $"{sw.Elapsed}");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a5: height field, traceback from face points

        [Fact]
        public void HeightFieldBump_TracePathFromFacePoints()
        {
            // https://github.com/nmwsharp/geometry-central/issues/133: an open height-field grid with a Gaussian bump, the
            // source a point inside a face: the traceback asserted. Rebuilt: the source a poked face point beside the bump,
            // paths traced from the centroid of every 7th triangle. Correct: every path is finite, on the surface, starts at
            // its point, ends at the source, is at least the straight distance and at most the heat distance plus two edge
            // lengths.
            Expect(nameof(HeightFieldBump_TracePathFromFacePoints), () => {
                Func<double, double, double> bump = (x, y) => 0.6 * Math.Exp(-((x - 1.1) * (x - 1.1) + (y - 1) * (y - 1)) / 0.15);
                DMesh3 m = Grid(20, 20, 2, 2, bump, false, out int[,] ids);
                int tid = EdgeTriangle(m, ids[4, 10], ids[5, 10]);
                Assert.Equal(MeshResult.Ok, m.PokeTriangle(tid, new Vector3d(0.2, 0.3, 0.5), out DMesh3.PokeTriangleInfo info));
                int s = info.new_vid;
                var geo = new MeshGeodesicDistance(m);
                double[] d = HeatDistances(m, s, geo);
                double h = MeanEdge(m);
                var spatial = new DMeshAABBTree3(m, true);
                int tested = 0;
                foreach (int t in m.TriangleIndices()) {
                    if (t % 7 != 0) continue;
                    Vector3d p = m.GetTriCentroid(t);
                    List<Vector3d> path = geo.TracePath(p);
                    Assert.True(path.Count >= 2, $"triangle {t}: path of {path.Count} points");
                    Assert.True(path.All(q => q.IsFinite), $"triangle {t}: non-finite path");
                    Assert.True(path[0].Distance(p) < 1e-12, $"triangle {t}: starts at {path[0]}");
                    Assert.Equal(m.GetVertex(s), path[path.Count - 1]);
                    foreach (Vector3d q in path) {
                        int nt = spatial.FindNearestTriangle(q);
                        // (the closest point, not DistanceSquared: that cancels to about 1e-18 at distance 0)
                        double off = q.Distance(MeshQueries.TriangleDistance(m, nt, q).TriangleClosest);
                        Assert.True(off < 1e-12, $"triangle {t}: path point {q} off the surface by {off}");
                    }
                    double len = PolylineLength(path), straight = p.Distance(m.GetVertex(s));
                    Index3i tv = m.GetTriangle(t);
                    double dp = (d[tv.a] + d[tv.b] + d[tv.c]) / 3;
                    Assert.True(len >= straight * (1 - 1e-12), $"triangle {t}: path {len} shorter than the straight line {straight}");
                    Assert.True(len <= dp * 1.02 + 2 * h, $"triangle {t}: path {len}, heat distance {dp}");
                    tested++;
                }
                Assert.True(tested > 100);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a6: heat underflow far from the source

        [Fact]
        public void LongTerrain_FarFromTheSource_AllFinite()
        {
            // https://github.com/nmwsharp/geometry-central/issues/36: a large terrain, the source near one end: the heat
            // underflowed to 0 far away and normalizing it gave NaN. Flat strips are covered by
            // GeodesicDistanceTests.LongStrip_NoUnderflowFarFromTheSource and IntrinsicGeodesicsTests.VectorHeat_LongerDiffusionTime...;
            // new here: a hilly strip 1500 edge lengths long with the vector heat method and ExtendScalar from both
            // ends. Correct: transported unit vectors, the log map and extended values finite everywhere, the log map
            // length the heat distance, each half the value of its end.
            Expect(nameof(LongTerrain_FarFromTheSource_AllFinite), () => {
                int nx = 1500;
                Func<double, double, double> hills = (x, y) => 0.15 * Math.Sin(0.7 * x) * Math.Cos(1.3 * y) + 0.1 * Math.Sin(0.23 * x + 0.5);
                DMesh3 m = Grid(nx, 2, nx, 2, hills, true, out int[,] ids);
                int s = ids[0, 1];
                double h = MeanEdge(m);
                double[] d = HeatDistances(m, s);
                var vh = new VectorHeatMethod(m);
                Vector2d[] tr = vh.TransportVector(s, new Vector2d(0, 1));
                Vector2d[] log = vh.ComputeLogMap(s);
                double[] ext = vh.ExtendScalar(new[] { new KeyValuePair<int, double>(s, 2.5), new KeyValuePair<int, double>(ids[nx, 1], -1) });
                foreach (int v in m.VertexIndices()) {
                    Assert.True(tr[v].IsFinite && Math.Abs(tr[v].Length - 1) < 1e-6, $"transport at {v}: {tr[v]}");
                    Assert.True(log[v].IsFinite, $"log map at {v}: {log[v]}");
                    Assert.True(ext[v] >= -1 - 1e-9 && ext[v] <= 2.5 + 1e-9, $"extended value at {v}: {ext[v]}");
                }
                for (int i = 100; i <= nx; i += 100) {
                    int v = ids[i, 1];
                    Assert.True(Math.Abs(log[v].Length - d[v]) <= 0.02 * d[v] + 2 * h, $"x = {i}: log map length {log[v].Length}, heat distance {d[v]}");
                    Assert.Equal(i < nx / 2 ? 2.5 : -1, ext[v], 6);
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a7: coarse meshes

        /// <summary> the triangles of a planar mesh split k x k times (each into k^2 triangles), welded </summary>
        static DMesh3 Subdivide(DMesh3 coarse, int k, Dictionary<int, int> vmap)
        {
            var m = new DMesh3();
            var map = new Dictionary<(long, long), int>();
            Func<Vector3d, int> vid = (p) => {
                var key = ((long)Math.Round(p.x * 1e9), (long)Math.Round(p.y * 1e9));
                if (!map.TryGetValue(key, out int v)) { v = m.AppendVertex(p); map[key] = v; }
                return v;
            };
            foreach (int t in coarse.TriangleIndices()) {
                Index3i tv = coarse.GetTriangle(t);
                Vector3d a = coarse.GetVertex(tv.a), b = coarse.GetVertex(tv.b), c = coarse.GetVertex(tv.c);
                Func<int, int, int> P = (i, j) => vid(a + (b - a) * ((double)i / k) + (c - a) * ((double)j / k));
                for (int i = 0; i < k; ++i)
                    for (int j = 0; i + j < k; ++j) {
                        m.AppendTriangle(P(i, j), P(i + 1, j), P(i, j + 1));
                        if (i + j + 1 < k) m.AppendTriangle(P(i + 1, j), P(i + 1, j + 1), P(i, j + 1));
                    }
            }
            foreach (int v in coarse.VertexIndices()) vmap[v] = vid(coarse.GetVertex(v));
            return m;
        }

        [Fact]
        public void CoarseSquareAndKite_ExactPathsAndHeatConverges()
        {
            // https://github.com/nmwsharp/geometry-central/issues/111 (a square of size 100 made of 2 triangles: 70.7
            // instead of 100) and https://github.com/nmwsharp/potpourri3d/issues/30 (the kite (0,0), (1,-3), (2,0),
            // (1,3) from (2,0): 1.51 instead of 2). The heat method has a discretization error on such meshes; correct:
            // the exact method (MeshGeodesicPath) gives 100 and 2, and the heat distances approach them as the same
            // geometry is refined (error below 2% plus one edge length, and shrinking).
            Expect(nameof(CoarseSquareAndKite_ExactPathsAndHeatConverges), () => {
                var sq = new DMesh3();
                int s0 = sq.AppendVertex(new Vector3d(0, 0, 0)), s1 = sq.AppendVertex(new Vector3d(100, 0, 0));
                int s2 = sq.AppendVertex(new Vector3d(100, 100, 0)), s3 = sq.AppendVertex(new Vector3d(0, 100, 0));
                sq.AppendTriangle(s0, s1, s2); sq.AppendTriangle(s0, s2, s3);
                var kite = new DMesh3();
                int k0 = kite.AppendVertex(new Vector3d(0, 0, 0)), k1 = kite.AppendVertex(new Vector3d(1, -3, 0));
                int k2 = kite.AppendVertex(new Vector3d(2, 0, 0)), k3 = kite.AppendVertex(new Vector3d(1, 3, 0));
                kite.AppendTriangle(k0, k1, k3); kite.AppendTriangle(k1, k2, k3);   // the diagonal (1,-3)-(1,3)

                Assert.Equal(100, ExactPath(sq, s0, s1, out _), 12);
                Assert.Equal(100, ExactPath(sq, s1, s2, out _), 12);
                Assert.Equal(100 * Math.Sqrt(2), ExactPath(sq, s1, s3, out _), 11);
                Assert.Equal(2, ExactPath(kite, k2, k0, out var gk), 14);
                Assert.True(gk.Flips > 0 || gk.InitialLength > 2, "the kite's path needs the flip across the diagonal");

                // relative errors (k = 1 ... 64) when written: edge 0.016 0.098 0.105 0.073 0.053 0.041 0.035, diagonals
                // below 0.03, kite 0.12 0.19 0.19 0.096 0.040 0.015 0.005. The edge from a boundary corner converges
                // slowly (the documented one edge length near boundaries), the kite fast.
                var cases = new[] {
                    ("edge", sq, s0, s1, 100.0, true, 0.04), ("diagonal", sq, s0, s2, 100 * Math.Sqrt(2), false, 0.02),
                    ("other diagonal", sq, s1, s3, 100 * Math.Sqrt(2), false, 0.02), ("kite", kite, k2, k0, 2.0, true, 0.01) };
                foreach (var (name, coarse, src, dst, exact, decreasing, finest) in cases) {
                    var errs = new List<double>();
                    foreach (int k in new[] { 1, 2, 4, 8, 16, 32, 64 }) {
                        var vmap = new Dictionary<int, int>();
                        DMesh3 m = Subdivide(coarse, k, vmap);
                        double[] d = HeatDistances(m, vmap[src]);
                        double err = Math.Abs(d[vmap[dst]] - exact);
                        Assert.True(err <= 0.02 * exact + MeanEdge(m), $"{name}, k = {k}: heat {d[vmap[dst]]}, exact {exact}");
                        errs.Add(err / exact);
                    }
                    string all = string.Join(" ", errs.Select(e => e.ToString("F4")));
                    if (decreasing)
                        for (int i = 3; i < errs.Count; ++i) Assert.True(errs[i] < errs[i - 1], $"{name}: errors {all}");
                    Assert.True(errs[errs.Count - 1] < finest, $"{name}: errors {all}");
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a9: reversed input path

        [Fact]
        public void ReversedEdgePath_SameGeodesicReversed()
        {
            // https://github.com/nmwsharp/geometry-central/issues/258: reversing the input path gave a different geodesic.
            // Rebuilt: a staircase edge path over a bumpy grid, shortened forwards and backwards, open and closed.
            // Correct: the same length (to rounding) and the same polyline, reversed.
            Expect(nameof(ReversedEdgePath_SameGeodesicReversed), () => {
                Func<double, double, double> bumps = (x, y) => 0.12 * Math.Sin(3 * x) * Math.Sin(2.5 * y);
                DMesh3 m = Grid(12, 12, 3, 3, bumps, true, out int[,] ids);
                // a hole in the middle, so that a loop around it does not contract
                foreach (int t in m.TriangleIndices().ToList()) {
                    Vector3d c = m.GetTriCentroid(t);
                    if (c.x > 1.25 && c.x < 1.75 && c.y > 0.75 && c.y < 1.25) m.RemoveTriangle(t, true, false);
                }
                var stair = new List<int>();
                int i = 1, j = 1;
                stair.Add(ids[i, j]);
                while (i < 10 || j < 9) {
                    if (i < 10) stair.Add(ids[++i, j]);
                    if (j < 9) stair.Add(ids[i, ++j]);
                }
                foreach (bool closed in new[] { false, true }) {
                    List<int> fwd = closed ? Loop(ids) : stair;
                    var a = new MeshGeodesicPath(m);
                    var b = new MeshGeodesicPath(m);
                    GeodesicPathStatus sa = a.Shorten(fwd, closed, closed ? new[] { fwd[0] } : null);
                    var rev = new List<int>(fwd); rev.Reverse();
                    GeodesicPathStatus sb = b.Shorten(rev, closed, closed ? new[] { fwd[0] } : null);
                    Assert.True(sa == GeodesicPathStatus.Success && sb == GeodesicPathStatus.Success, $"closed {closed}: {sa}, {sb}");
                    Assert.True(Math.Abs(a.Length - b.Length) <= 1e-12 * a.Length, $"closed {closed}: {a.Length:R} vs {b.Length:R}");
                    Assert.True(a.Length < a.InitialLength);
                    // the same polyline reversed (repeated points, finding G1, are dropped relative to the length, so
                    // this compares the geodesics; PathPolyline_NoRepeatedPoints checks the repeats); a closed path is
                    // compared as a cycle (its start need not be the fixed vertex)
                    List<Vector3d> pa = Distinct(a.Path, closed, a.Length), pb = Distinct(b.Path, closed, b.Length);
                    pb.Reverse();
                    Assert.True(pa.Count == pb.Count, $"closed {closed}: {pa.Count} vs {pb.Count} points");
                    double tol = 1e-9 * a.Length;
                    int shift = closed ? pb.FindIndex(q => q.Distance(pa[0]) < tol) : 0;
                    Assert.True(shift >= 0, $"closed {closed}: the start of one path is not on the other");
                    for (int k = 0; k < pb.Count; ++k)
                        Assert.True(pa[k].Distance(pb[(k + shift) % pb.Count]) < tol, $"closed {closed}, point {k}: {pa[k]} vs {pb[(k + shift) % pb.Count]}");
                }
            });
        }

        [Fact]
        public void PathPolyline_NoRepeatedPoints()
        {
            // Found with ReversedEdgePath_SameGeodesicReversed (https://github.com/nmwsharp/geometry-central/issues/258):
            // MeshGeodesicPath.Path repeated its end point when the traced edge ran exactly through a mesh vertex (the
            // walk records its crossings there, then the exact vertex). A zero-length segment gives NaN tangents
            // downstream. Rebuilt: the staircase on the bumpy grid in both directions, and paths from a corner of a flat
            // grid along its diagonal and to its far sides. Correct: no two consecutive points closer than 1e-9 of the
            // length. Finding G1 (fixed: MeshGeodesicPath.build_output drops repeats).
            Expect(nameof(PathPolyline_NoRepeatedPoints), () => {
                Func<double, double, double> bumps = (x, y) => 0.12 * Math.Sin(3 * x) * Math.Sin(2.5 * y);
                DMesh3 m = Grid(12, 12, 3, 3, bumps, true, out int[,] ids);
                var stair = new List<int> { ids[1, 1] };
                for (int i = 1, j = 1; i < 10 || j < 9;) {
                    if (i < 10) stair.Add(ids[++i, j]);
                    if (j < 9) stair.Add(ids[i, ++j]);
                }
                var paths = new List<MeshGeodesicPath>();
                var a = new MeshGeodesicPath(m); Assert.Equal(GeodesicPathStatus.Success, a.Shorten(stair)); paths.Add(a);
                stair.Reverse();
                var b = new MeshGeodesicPath(m); Assert.Equal(GeodesicPathStatus.Success, b.Shorten(stair)); paths.Add(b);
                DMesh3 flat = Grid(8, 8, 1, 1, Flat, false, out int[,] fid);
                foreach (int t in new[] { fid[8, 8], fid[4, 4], fid[8, 3], fid[2, 8] }) {
                    ExactPath(flat, fid[0, 0], t, out MeshGeodesicPath g);
                    paths.Add(g);
                }
                foreach (MeshGeodesicPath g in paths) AssertNoRepeatedPoints(g);
            });
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PathPolyline_NoRepeatedPoints_InvariantUnderSimilarities(string name)
        {
            // the paths of PathPolyline_NoRepeatedPoints that run exactly through vertices (along the diagonal of the
            // flat grid), moved: no repeated points. The grid is cocircular (docs/QA.md 2.1), which is fine here: a
            // 2^k scale keeps every tie exactly, so the same vertices and points bit for bit; the other similarities may
            // decide ties differently, so only the length is compared. The last path passes the diagonal vertices at
            // 1e-7 to 5e-7 (its far corner moved by 1e-6): real segments of about 1e-7, 1e-10 at a scale of 2^-10, which
            // a repeat threshold not relative to the edge length would drop there (a different point count)
            Similarity sim = Similarity.ByName(name);
            DMesh3 flat = Grid(8, 8, 1, 1, Flat, false, out int[,] fid);
            DMesh3 near = Grid(8, 8, 1, 1, Flat, false, out int[,] nid);
            near.SetVertex(nid[8, 8], new Vector3d(1, 1 + 1e-6, 0));
            var cases = new[] { (flat, fid[0, 0], fid[8, 8]), (flat, fid[0, 0], fid[4, 4]), (flat, fid[0, 0], fid[8, 3]), (near, nid[0, 0], nid[8, 8]) };
            string caseName = nameof(PathPolyline_NoRepeatedPoints_InvariantUnderSimilarities) + "_" + sim.Name.Replace(", ", "_").Replace(" ", "_");
            Expect(caseName, () => {
                foreach (var (mesh, s0, t) in cases) {
                    var g0 = new MeshGeodesicPath(mesh);
                    var g = new MeshGeodesicPath(Invariance.Transformed(mesh, sim));
                    Assert.Equal(GeodesicPathStatus.Success, g0.Compute(s0, t));
                    if (mesh == near) {
                        double shortest = Enumerable.Range(1, g0.Path.Count - 1).Min(k => g0.Path[k].Distance(g0.Path[k - 1]));
                        Assert.True(shortest > 1e-9 && shortest < 1e-6, $"near-diagonal path: shortest segment {shortest}");
                    }
                    Assert.Equal(GeodesicPathStatus.Success, g.Compute(s0, t));
                    AssertNoRepeatedPoints(g);
                    if (sim.IsExact) {
                        Assert.Equal(g0.PathVertices, g.PathVertices);
                        Assert.Equal(g0.Path.Count, g.Path.Count);
                        for (int k = 0; k < g0.Path.Count; ++k) Assert.Equal(g0.Path[k], g.Path[k] / sim.Scale);
                    } else
                        Assert.True(Math.Abs(g0.Length - g.Length / sim.Scale) < 1e-9 * g0.Length + 64 * sim.Resolution(1.5), $"{g0.Length} vs {g.Length / sim.Scale}");
                }
            });
        }

        /// <summary> the points without repeats (within 1e-9 of the length); closed: without the closing point </summary>
        static List<Vector3d> Distinct(List<Vector3d> path, bool closed, double length)
        {
            double tol = 1e-9 * length;
            var r = new List<Vector3d>();
            foreach (Vector3d p in path)
                if (r.Count == 0 || r[r.Count - 1].Distance(p) > tol) r.Add(p);
            if (closed && r.Count > 1 && r[r.Count - 1].Distance(r[0]) <= tol) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary> a loop of grid vertices around the hole: the boundary of [2, 9] x [2, 9], counter-clockwise </summary>
        static List<int> Loop(int[,] ids)
        {
            var l = new List<int>();
            for (int i = 2; i < 9; ++i) l.Add(ids[i, 2]);
            for (int j = 2; j < 9; ++j) l.Add(ids[9, j]);
            for (int i = 9; i > 2; --i) l.Add(ids[i, 9]);
            for (int j = 9; j > 2; --j) l.Add(ids[2, j]);
            return l;
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a11: refinement at a tiny angle between constraint edges

        [Fact]
        public void Refinement_TinyAngleBetweenBoundaryEdges_NoCoincidentVertices()
        {
            // https://github.com/nmwsharp/geometry-central/issues/246: intrinsic Delaunay refinement with an angle of 1.69
            // degrees between two constraint edges inserted coincident vertices and a 0 degree triangle. Our constraint
            // edges are boundary edges (wedges of 5 to 55 degrees: IntrinsicRefinementTests.Refine_Wedges_AllSucceed;
            // new here: 1.69 degrees and the coincident-vertex check): a 1.69 degree wedge, and a needle (an isosceles
            // triangle domain with two 1.69 degree corners, the third one 176.6 degrees). Correct: terminates, no two
            // vertices closer than 1e-9 of the size, no zero angle, consistent signposts, no negative cotan weight.
            Expect(nameof(Refinement_TinyAngleBetweenBoundaryEdges_NoCoincidentVertices), () => {
                double ang = 1.69 * Math.PI / 180;
                var wedge = new DMesh3();
                int o = wedge.AppendVertex(Vector3d.Zero);
                int rings = 6;
                int[] p = new int[rings + 1], q = new int[rings + 1];
                p[0] = q[0] = o;
                for (int r = 1; r <= rings; ++r) {
                    p[r] = wedge.AppendVertex(new Vector3d(r, 0, 0));
                    q[r] = wedge.AppendVertex(new Vector3d(r * Math.Cos(ang), r * Math.Sin(ang), 0));
                }
                wedge.AppendTriangle(o, p[1], q[1]);
                for (int r = 1; r < rings; ++r) {
                    wedge.AppendTriangle(p[r], p[r + 1], q[r + 1]);
                    wedge.AppendTriangle(p[r], q[r + 1], q[r]);
                }
                var needle = new DMesh3();
                double L = 10, H = 5 * Math.Tan(ang);
                int n0 = needle.AppendVertex(new Vector3d(0, 0, 0)), n1 = needle.AppendVertex(new Vector3d(L, 0, 0));
                int n2 = needle.AppendVertex(new Vector3d(L / 2, H, 0)), nm = needle.AppendVertex(new Vector3d(L / 2, 0, 0));
                needle.AppendTriangle(n0, nm, n2); needle.AppendTriangle(nm, n1, n2);

                foreach (var (name, mesh, size) in new[] { ("wedge", wedge, (double)rings), ("needle", needle, L) })
                    foreach (double minAngle in new[] { 0.0, 20, 30 }) {
                        var T = new IntrinsicTriangulation(mesh);
                        var sw = Stopwatch.StartNew();
                        IntrinsicRefinementResult res = T.DelaunayRefine(minAngle, 5000);
                        Assert.True(sw.Elapsed.TotalSeconds < 5, $"{name} {minAngle}: {sw.Elapsed}");
                        Assert.True(res.Status == IntrinsicRefinementStatus.Success, $"{name} {minAngle}: {res}");
                        Assert.Equal(0, res.NegativeWeights);
                        Assert.True(res.MinAngle > 0.5, $"{name} {minAngle}: min angle {res.MinAngle}");
                        Assert.True(T.CheckConsistency(out string err), $"{name} {minAngle}: {err}");
                        var pos = Enumerable.Range(0, T.VertexCount).Select(i => T.VertexPosition(i)).ToList();
                        Assert.True(pos.All(x => x.IsFinite && Math.Abs(x.z) < 1e-12));
                        for (int i = 0; i < pos.Count; ++i)
                            for (int k = i + 1; k < pos.Count; ++k)
                                Assert.True(pos[i].Distance(pos[k]) > 1e-9 * size, $"{name} {minAngle}: vertices {i} and {k} coincide at {pos[i]}");
                        for (int h = 0; h < T.HalfedgeCount; ++h)
                            Assert.True(T.Length(h) > 1e-9 * size && T.CornerAngle(h) > 0, $"{name} {minAngle}: halfedge {h}");
                    }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a12: tiny triangles and boundary corners in vector transport

        [Fact]
        public void TinyAreaTriangles_VectorTransportFinite()
        {
            // https://github.com/nmwsharp/potpourri3d/issues/29 (triangles of area below 1e-8 gave NaN frames in the vector
            // transport) and https://github.com/nmwsharp/geometry-central/issues/167 (at boundary corners). Rebuilt: a flat
            // grid whose middle column is squeezed to slivers of area 1e-12 and 1e-16 (and 0), the source at a boundary
            // corner. Correct: transported vectors, log map and frames finite at every vertex; on the flat grid the
            // transport is the same vector everywhere (here within 2 degrees).
            foreach (double eps in new[] { 1e-6, 1e-10, 1e-14, 0.0 }) {
                string name = nameof(TinyAreaTriangles_VectorTransportFinite) + (eps == 0 ? "_CoincidentVertices" : "");
                int n = 10;
                DMesh3 m = Grid(n, n, 1, 1, Flat, true, out int[,] ids);
                // squeeze column 5 onto column 4: the cells between are slivers of width eps (eps 0: coincident vertices)
                for (int j = 0; j <= n; ++j) {
                    Vector3d p = m.GetVertex(ids[5, j]);
                    m.SetVertex(ids[5, j], new Vector3d(0.4 + eps, p.y, 0));
                }
                int s = ids[0, 0];
                var vh = new VectorHeatMethod(m);
                Vector3d dir = new Vector3d(1, 1, 0).Normalized;
                Vector2d[] tr = null, log = null;
                var off = new List<string>();
                var offColumns = new HashSet<int>();
                Expect(name, () => {
                    Vector2d v0 = vh.Triangulation.FromWorld(s, dir);
                    tr = vh.TransportVector(s, v0);
                    log = vh.ComputeLogMap(s);
                    for (int i = 0; i <= n; ++i)
                        for (int j = 0; j <= n; ++j) {
                            int v = ids[i, j];
                            Assert.True(tr[v].IsFinite && log[v].IsFinite, $"eps {eps}, vertex {v}: transport {tr[v]}, log {log[v]}");
                            vh.Triangulation.GetTangentBasis(v, out Vector3d e1, out Vector3d e2, out Vector3d nn);
                            Assert.True(e1.IsFinite && e2.IsFinite && nn.IsFinite, $"eps {eps}, vertex {v}: frame");
                            Vector3d w = vh.Triangulation.ToWorld(v, tr[v]);
                            Assert.True(w.IsFinite, $"eps {eps}, vertex {v}: ToWorld");
                            double angle = Vector3d.AngleD(w.Normalized, dir);
                            if (angle >= 2) { off.Add($"({i},{j}) {angle:F1}"); offColumns.Add(i); }
                        }
                    Assert.True(off.Count == 0, $"eps {eps}: transported vectors off by 2 degrees or more at {string.Join(", ", off)}");
                });
                if (IssueCases.Strict && KnownFailures.ContainsKey(name)) {
                    // finding G2: finite everywhere, wrong (45 degrees) only at the coincident vertices of columns 4 and 5
                    Assert.True(tr != null && m.VertexIndices().All(v => tr[v].IsFinite && log[v].IsFinite), "G2: non-finite output");
                    Assert.True(offColumns.Count > 0 && offColumns.All(c => c == 4 || c == 5), $"G2: off at {string.Join(", ", off)}");
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------
        // a13: inconsistent winding

        [Fact]
        public void InconsistentWinding_ClearFailureOrCorrect()
        {
            // https://github.com/nmwsharp/potpourri3d/issues/21 (inconsistently wound faces) and /issues/18 (a non-manifold
            // edge): asserts. DMesh3 does not store a non-manifold edge (AppendTriangle returns NonManifoldID), so only the
            // winding is rebuilt: a flat grid with one triangle reversed. Correct: a clear failure (an ArgumentException
            // from the intrinsic classes, InvalidInput for paths, false from Compute) or a correct result (distances |p - s|
            // within the heat method's accuracy), never an assert, NaN or a wrong value.
            Expect(nameof(InconsistentWinding_ClearFailureOrCorrect), () => {
                int n = 8;
                DMesh3 m = Grid(n, n, 1, 1, Flat, true, out int[,] ids);
                int tid = EdgeTriangle(m, ids[4, 4], ids[5, 4]);
                Index3i tv = m.GetTriangle(tid);
                m.RemoveTriangle(tid, false, false);
                int nt = m.AppendTriangle(tv.a, tv.c, tv.b);
                Assert.True(nt >= 0, $"reversed triangle: {nt}");
                int s = ids[1, 1];
                double h = MeanEdge(m);

                bool intrinsicRejects = false;
                try { new IntrinsicTriangulation(m); } catch (ArgumentException) { intrinsicRejects = true; }

                var geo = new MeshGeodesicDistance(m);
                if (geo.Compute(new[] { s }))
                    foreach (int v in m.VertexIndices()) {
                        double ex = m.GetVertex(v).Distance(m.GetVertex(s));
                        Assert.True(Math.Abs(geo.Distance[v] - ex) <= 0.02 * ex + h, $"heat at {v}: {geo.Distance[v]}, exact {ex}");
                    }

                var g = new MeshGeodesicPath(m);
                GeodesicPathStatus st = g.Compute(s, ids[7, 7]);
                if (st == GeodesicPathStatus.Success) {
                    double ex = m.GetVertex(ids[7, 7]).Distance(m.GetVertex(s));
                    Assert.True(Math.Abs(g.Length - ex) < 1e-12, $"path {g.Length:R}, exact {ex:R}");
                } else
                    Assert.True(st == GeodesicPathStatus.InvalidInput, $"path: {st}");
                Assert.True(intrinsicRejects == (st == GeodesicPathStatus.InvalidInput), $"triangulation rejects {intrinsicRejects}, path {st}");

                try {
                    var vh = new VectorHeatMethod(m);
                    Vector2d[] tr = vh.TransportVector(s, new Vector2d(1, 0));
                    Assert.False(intrinsicRejects, "vector heat accepted what the triangulation rejects");
                    foreach (int v in m.VertexIndices()) Assert.True(tr[v].IsFinite, $"transport at {v}");
                } catch (ArgumentException) {
                    Assert.True(intrinsicRejects, "vector heat rejects what the triangulation accepts");
                }
            });
        }
    }
}
