using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for point / ray queries and spatial structures (DMeshAABBTree3 ray casts, IsInside, WindingNumber,
    /// FastWindingNumber, MeshSignedDistanceGrid, CachingMeshSDF, Polygon2d.Contains) from the issue trackers of other
    /// libraries (docs/QA.md 2.11): only the failure mode, rebuilt here from simple primitives. No code, test code or
    /// meshes of these projects are used. The issue URL is in the comment of each test.
    ///
    /// Ray casts are compared with a brute-force loop over every triangle with the same leaf predicate
    /// (IntrRay3Triangle3.Intersects), so that a difference can only come from the tree (its boxes, slab test and
    /// traversal). Inside tests are compared with the geometric truth of the procedural solid. A case we fail is listed
    /// in KnownFailures with its finding (strict: a listed case that passes fails, so a fix removes the entry).
    /// </summary>
    public class SpatialQueryIssueCasesTests
    {
        /// <summary> case name -> finding; a listed case must still fail </summary>
        public static readonly Dictionary<string, string> KnownFailures = new Dictionary<string, string> {
            [nameof(RayOntoTheSharedDiagonalOrVertexOfASplitSquare_Hits)] =
                "finding Q2 (docs/ISSUE_CASE_FINDINGS.md): IntrRay3Triangle3.Intersects is not watertight, a ray onto a shared edge can miss both triangles at 1e-3 / 1e-7",
            [nameof(ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight)] =
                "finding Q3: IsInside counts a vertex hit 0 times and an edge hit twice, so the parity is wrong",
        };

        static void Expect(string name, string failure)
        {
            if (KnownFailures.TryGetValue(name, out string finding))
                Assert.True(failure != "", $"{name} passes now: remove its entry ({finding}) from KnownFailures and docs/QA.md 2.11");
            else
                Assert.True(failure == "", name + ": " + failure);
        }

        [Fact]
        public void KnownFailures_NameExistingCases()
        {
            var names = new HashSet<string>(typeof(SpatialQueryIssueCasesTests).GetMethods().Select(m => m.Name));
            foreach (var kv in KnownFailures) {
                Assert.True(names.Any(n => kv.Key == n || kv.Key.StartsWith(n + "_")), kv.Key + ": no such case");
                Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            }
        }

        // ---- primitives ----

        static bool Fin(double x) => !double.IsNaN(x) && !double.IsInfinity(x);     // (net48 has no double.IsFinite)

        static DMesh3 Mesh(Vector3d[] v, int[] t)
        {
            var m = new DMesh3();
            foreach (Vector3d p in v) m.AppendVertex(p);
            for (int i = 0; i < t.Length; i += 3)
                Assert.True(m.AppendTriangle(t[i], t[i + 1], t[i + 2]) >= 0);
            return m;
        }

        /// <summary> an axis-aligned box, 12 triangles, outward </summary>
        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? lo.x : hi.x, (i & 2) == 0 ? lo.y : hi.y, (i & 4) == 0 ? lo.z : hi.z));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            Assert.True(m.IsClosed() && ResultChecks.Volume(m) > 0);
            return m;
        }

        static DMesh3 Append(params DMesh3[] meshes)
        {
            var r = new DMesh3();
            foreach (DMesh3 m in meshes) {
                var map = new Dictionary<int, int>();
                foreach (int v in m.VertexIndices()) map[v] = r.AppendVertex(m.GetVertex(v));
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    Assert.True(r.AppendTriangle(map[tv.a], map[tv.b], map[tv.c]) >= 0);
                }
            }
            return r;
        }

        static DMesh3 Reversed(DMesh3 m)
        {
            var r = new DMesh3(m);
            r.ReverseOrientation();
            return r;
        }

        /// <summary> a height field over [0,n]^2 with dyadic heights, two triangles per unit cell </summary>
        static DMesh3 HeightField(int n, bool alternateDiagonals)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i, j, ((i * 7 + j * 3) % 5) / 4.0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    if (alternateDiagonals && (i + j) % 2 == 1) {
                        m.AppendTriangle(a, b, c); m.AppendTriangle(b, d, c);
                    } else {
                        m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
                    }
                }
            return m;
        }

        static DMesh3 Transformed(DMesh3 m, Func<Vector3d, Vector3d> f)
        {
            var r = new DMesh3(m);
            foreach (int v in r.VertexIndices()) r.SetVertex(v, f(r.GetVertex(v)));
            return r;
        }

        static IEnumerable<DMeshAABBTree3> Trees(DMesh3 mesh)
        {
            foreach (var (s, leaf) in new[] {
                (DMeshAABBTree3.BuildStrategy.TopDownMidpoint, 1), (DMeshAABBTree3.BuildStrategy.TopDownMidpoint, 2),
                (DMeshAABBTree3.BuildStrategy.TopDownMidpoint, 10), (DMeshAABBTree3.BuildStrategy.TopDownMedian, 2),
                (DMeshAABBTree3.BuildStrategy.BottomUpFromOneRings, 4) }) {
                var tree = new DMeshAABBTree3(mesh) { TopDownLeafMaxTriCount = leaf };
                tree.Build(s);
                yield return tree;
            }
        }

        /// <summary> every triangle the ray hits, with its ray parameter, by a loop over all triangles </summary>
        static List<(int tid, double t)> BruteHits(DMesh3 mesh, Ray3d ray)
        {
            var hits = new List<(int, double)>();
            foreach (int ti in mesh.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                mesh.GetTriVertices(ti, ref a, ref b, ref c);
                if (IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double t))
                    hits.Add((ti, t));
            }
            return hits;
        }

        static double HitT(DMesh3 mesh, int ti, Ray3d ray)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            mesh.GetTriVertices(ti, ref a, ref b, ref c);
            Assert.True(IntrRay3Triangle3.Intersects(ref ray, ref a, ref b, ref c, out double t));
            return t;
        }

        /// <summary> "" if the tree's nearest hit and all hits equal the brute force ones, else the difference </summary>
        static string TreeVsBrute(DMesh3 mesh, DMeshAABBTree3 tree, Ray3d ray)
        {
            List<(int tid, double t)> brute = BruteHits(mesh, ray);
            int near = tree.FindNearestHitTriangle(ray);
            var all = new List<int>();
            int count = tree.FindAllHitTriangles(ray, all);
            string where = $"ray {ray.Origin} {ray.Direction}, leaf {tree.TopDownLeafMaxTriCount}";
            if (brute.Count == 0)
                return near == DMesh3.InvalidID && count == 0 && all.Count == 0 ? "" : $"{where}: brute no hit, tree {near} / {count}";
            if (near == DMesh3.InvalidID)
                return $"{where}: brute {brute.Count} hits, tree no nearest hit";
            double tMin = brute.Min(h => h.t);
            if (HitT(mesh, near, ray) != tMin)
                return $"{where}: nearest t {HitT(mesh, near, ray):R}, brute {tMin:R}";
            var bs = brute.Select(h => h.tid).OrderBy(x => x).ToArray();
            if (count != all.Count || !bs.SequenceEqual(all.OrderBy(x => x)))
                return $"{where}: all hits [{string.Join(",", all)}], brute [{string.Join(",", bs)}]";
            return "";
        }

        static readonly int[][] Permutations = {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };

        static Vector3d Permute(Vector3d p, int[] perm, int signs) =>
            new Vector3d(((signs & 1) != 0 ? -1 : 1) * p[perm[0]], ((signs & 2) != 0 ? -1 : 1) * p[perm[1]], ((signs & 4) != 0 ? -1 : 1) * p[perm[2]]);

        // ---------------------------------------------------------------------------------------------------------------
        // ray casts: tree against brute force

        [Fact]
        public void FanAroundRayAxis_AllAxisPermutations_TreeEqualsBruteForce()
        {
            // https://github.com/libigl/libigl/issues/2232 and https://github.com/libigl/libigl/issues/2568: a fan of 4
            // triangles around (0,0,0) in z = 0, a ray from (0,0,1) along (0,0,-1): the slab test of the flat box computed
            // 0*inf = NaN and the BVH reported 0 hits where brute force found 4; after a fix the case rotated into the YZ
            // and XZ planes failed again. Rebuilt under all 48 axis permutations and sign flips, rays through the shared
            // vertex, a shared edge and a face interior, from both sides. Correct: the tree equals brute force, and the
            // ray through the vertex hits all 4 triangles.
            var verts = new[] { Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0) };
            int[] tris = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1 };
            var rays = new[] {
                (new Vector3d(0, 0, 1), new Vector3d(0, 0, -1), 4), (new Vector3d(0, 0, -1), new Vector3d(0, 0, 1), 4),
                (new Vector3d(0.5, 0, 1), new Vector3d(0, 0, -1), 2), (new Vector3d(0, -0.25, -3), new Vector3d(0, 0, 1), 2),
                (new Vector3d(0.25, 0.25, 1), new Vector3d(0, 0, -1), 1), (new Vector3d(2, 0, 1), new Vector3d(0, 0, -1), 0),
            };
            var fails = new List<string>();
            foreach (int[] perm in Permutations)
                for (int signs = 0; signs < 8; ++signs) {
                    DMesh3 mesh = Mesh(verts.Select(v => Permute(v, perm, signs)).ToArray(), tris);
                    foreach (DMeshAABBTree3 tree in Trees(mesh))
                        foreach (var (o, d, expected) in rays) {
                            var ray = new Ray3d(Permute(o, perm, signs), Permute(d, perm, signs));
                            string f = TreeVsBrute(mesh, tree, ray);
                            if (f == "" && tree.FindAllHitTriangles(ray) != expected)
                                f = $"ray {ray.Origin}: {tree.FindAllHitTriangles(ray)} hits, expected {expected}";
                            if (f != "") fails.Add(f);
                        }
                }
            Expect(nameof(FanAroundRayAxis_AllAxisPermutations_TreeEqualsBruteForce), string.Join("; ", fails.Take(5)));
        }

        [Fact]
        public void GrazingRays_AlongBoxFacesAndEdges_TreeEqualsBruteForce()
        {
            // https://github.com/libigl/libigl/issues/2270: rounding in the slab test missed a box that the ray only
            // grazes (a ray along a box face or through a box edge), so a hit on the boundary triangle was lost. Rebuilt:
            // a height field with leaves of 1 to 10 triangles, so many box faces lie on the grid lines x = i, y = j, and
            // rays that run in those planes (vertical, oblique) or along the lines x = i, y = j, at unit size, at 2^20
            // (where the box padding of the tree is negligible) and at UTM coordinates. Correct: the tree equals brute
            // force for every ray.
            var fails = new List<string>();
            foreach (var (name, f) in new (string, Func<Vector3d, Vector3d>)[] {
                ("unit", p => p), ("2^20", p => p * 1048576.0), ("utm", p => p + new Vector3d(632117.25, 5712043.5, 113.75)) }) {
                DMesh3 mesh = Transformed(HeightField(6, true), f);
                Vector3d o0 = f(Vector3d.Zero);
                double s = f(Vector3d.AxisX).x - o0.x;
                var rays = new List<Ray3d>();
                for (int i = 0; i <= 6; ++i)
                    for (int h = 0; h <= 12; ++h) {
                        double x = i, y = h / 2.0;
                        rays.Add(new Ray3d(f(new Vector3d(x, y, 3)), -Vector3d.AxisZ));                         // vertical, on x = i
                        rays.Add(new Ray3d(f(new Vector3d(y, x, 3)), -Vector3d.AxisZ));                         // vertical, on y = i
                        rays.Add(new Ray3d(f(new Vector3d(x, y - 3, 3)), new Vector3d(0, 0.6, -0.8)));          // oblique in x = i
                        rays.Add(new Ray3d(f(new Vector3d(y - 3, x, 3)), new Vector3d(0.6, 0, -0.8)));          // oblique in y = i
                        rays.Add(new Ray3d(f(new Vector3d(x, -1, h / 4.0)), Vector3d.AxisY));                  // along x = i at height
                        rays.Add(new Ray3d(f(new Vector3d(-1, x, h / 4.0)), Vector3d.AxisX));
                    }
                foreach (DMeshAABBTree3 tree in Trees(mesh))
                    foreach (Ray3d r in rays) {
                        string d = TreeVsBrute(mesh, tree, r);
                        if (d != "") fails.Add(name + ": " + d);
                    }
            }
            Expect(nameof(GrazingRays_AlongBoxFacesAndEdges_TreeEqualsBruteForce), string.Join("; ", fails.Take(5)));
        }

        [Fact]
        public void VerticalRaysOnAScaledMesh_HitIndependentlyOfTheScale()
        {
            // https://github.com/libigl/libigl/issues/2408: vertical rays onto a uniformly scaled mesh stopped hitting below
            // a scale of about 1e-2 (an absolute epsilon). Rebuilt: a height field and a sphere scaled by 2^k (k from -40
            // to 40) and by 1e-2 ... 1e-6, rays from above on a grid that includes vertices and shared edges. Correct: the
            // same hits at every scale; at 2^k the nearest triangle and t / 2^k are the same bit for bit.
            // Fixed (Q1): at 2^-25 and below FindNearestHitTriangle returned another triangle of the same t where the ray hits
            // a shared edge or vertex: the traversal order, which the absolute box padding changes, picked among ties.
            DMesh3 field = HeightField(5, true);
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 2, EdgeVertices = 6 }.Generate().MakeDMesh();
            MeshTransforms.Translate(sphere, new Vector3d(2.5, 2.5, 0));
            var fails = new List<string>();
            foreach (var (mname, mesh) in new[] { ("field", field), ("sphere", sphere) }) {
                var points = new List<Vector2d>();
                for (int j = 0; j <= 20; ++j)
                    for (int i = 0; i <= 20; ++i) points.Add(new Vector2d(i / 4.0, j / 4.0));
                // per ray: (the nearest triangle, "t / scale of the nearest hit : all hit triangles", hit count)
                List<(int near, string hits, int count)> Signature(double scale)
                {
                    DMesh3 m = Transformed(mesh, p => p * scale);
                    var tree = new DMeshAABBTree3(m, true);
                    var sig = new List<(int, string, int)>();
                    foreach (Vector2d q in points) {
                        var ray = new Ray3d(new Vector3d(q.x, q.y, 10) * scale, -Vector3d.AxisZ);
                        int t = tree.FindNearestHitTriangle(ray);
                        var all = new List<int>();
                        tree.FindAllHitTriangles(ray, all);
                        sig.Add((t, t < 0 ? "-" : $"{HitT(m, t, ray) / scale:R}:{string.Join(",", all.OrderBy(x => x))}", all.Count));
                    }
                    return sig;
                }
                // at the decimal scales a ray onto an edge may legitimately hit one or both triangles (and, by finding Q2, at
                // times neither): compare the hit counts of the rays inside the faces, where the count is defined
                bool[] interior = points.Select(q => {
                    int i = (int)(4 * q.x), j = (int)(4 * q.y);
                    return i % 4 != 0 && j % 4 != 0 && (i - j) % 4 != 0 && (i + j) % 4 != 0;
                }).ToArray();
                var baseline = Signature(1);
                Assert.Contains(baseline, x => x.near >= 0);
                for (int k = -40; k <= 40; k += 5) {
                    if (k == 0) continue;
                    var sig = Signature(Math.Pow(2, k));
                    Assert.Equal(baseline.Select(x => x.hits), sig.Select(x => x.hits));
                    if (!baseline.Select(x => x.near).SequenceEqual(sig.Select(x => x.near))) fails.Add($"{mname} 2^{k}");
                }
                foreach (double s in new[] { 1e-2, 1e-3, 1e-4, 1e-6 }) {
                    var sig = Signature(s);
                    for (int i = 0; i < points.Count; ++i)
                        if (interior[i] && sig[i].count != baseline[i].count) fails.Add($"{mname} {s} at {points[i]}: {sig[i].count} hits, {baseline[i].count} at scale 1");
                }
            }
            Expect(nameof(VerticalRaysOnAScaledMesh_HitIndependentlyOfTheScale), string.Join("; ", fails));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void NearestHit_AllSimilarities_MapBackToTheBaseline(string name)
        {
            // invariance of FindNearestHitTriangle (changed by Q1: ties go to the lowest id): rays from above onto the
            // vertices, the edges and the faces of a height field. At 2^k the same triangle and t / 2^k bit for bit; under the
            // other similarities the same hit, and the returned triangle's t equals the baseline's within the rounding of
            // the transformed coordinates, for rays off the edges (onto an edge a rounded ray may miss, finding Q2).
            Similarity sim = Similarity.ByName(name);
            DMesh3 mesh = HeightField(5, true), moved = Invariance.Transformed(mesh, sim);
            var tree = new DMeshAABBTree3(mesh, true);
            var movedTree = new DMeshAABBTree3(moved, true);
            double tol = 64 * sim.Resolution(10);
            for (int j = 0; j <= 20; ++j)
                for (int i = 0; i <= 20; ++i) {
                    var q = sim.IsExact ? new Vector3d(i / 4.0, j / 4.0, 10) : new Vector3d(i / 4.0 + 0.03, j / 4.0 + 0.07, 10);
                    if (q.x > 5 || q.y > 5) continue;
                    var ray = new Ray3d(q, -Vector3d.AxisZ);
                    var mray = new Ray3d(sim.Apply(ray.Origin), sim.ApplyDirection(ray.Direction).Normalized);
                    int t0 = tree.FindNearestHitTriangle(ray), t1 = movedTree.FindNearestHitTriangle(mray);
                    Assert.True(t0 >= 0, $"no hit at {ray.Origin}");
                    Assert.True(t1 >= 0, $"{name}: no hit at {ray.Origin}");
                    double d0 = HitT(mesh, t0, ray), d1 = HitT(moved, t1, mray) / sim.Scale;
                    if (sim.IsExact) {
                        Assert.Equal(t0, t1);
                        Assert.Equal(d0, d1);
                    } else
                        Assert.True(Math.Abs(d0 - d1) <= tol, $"{name}: t {d1:R}, baseline {d0:R} at {ray.Origin}");
                    Assert.Equal(t0, tree.FindNearestHitTriangle(ray));          // repeated query
                    Assert.Equal(t0, MeshQueries.FindHitTriangle_LinearSearch(mesh, ray));    // the same tie rule
                }
        }

        [Fact]
        public void RayOntoTheSharedDiagonalOrVertexOfASplitSquare_Hits()
        {
            // https://github.com/mikedh/trimesh/issues/1766: a unit square split along its diagonal, a ray straight down onto
            // a point of the shared diagonal edge (and onto a shared vertex) after rescaling gave 0 hits. Rebuilt for both
            // diagonals, 97 points along the diagonal and the 4 corners, at 2^k, 1e-3, 1e3, 1e-7 and UTM. Correct: at least
            // one hit (the two triangles cover the closed square), from the tree and from brute force.
            // Known failure Q2: at 1e-3 and 1e-7 some rays onto the anti-diagonal miss both triangles in the leaf predicate
            // IntrRay3Triangle3.Intersects (not watertight); pinned: the tree always equals brute force, and the misses
            // only occur at the inexact scales.
            var fails = new List<string>();
            foreach (bool other in new[] { false, true })
                foreach (var (name, exact, f) in new (string, bool, Func<Vector3d, Vector3d>)[] {
                    ("unit", true, p => p), ("2^-30", true, p => p * Math.Pow(2, -30)), ("2^30", true, p => p * Math.Pow(2, 30)),
                    ("1e-3", false, p => p * 1e-3), ("1e3", false, p => p * 1e3), ("1e-7", false, p => p * 1e-7),
                    ("utm", false, p => p + new Vector3d(632117.25, 5712043.5, 113.75)),
                    ("utm 1e-3", false, p => p * 1e-3 + new Vector3d(632117.25, 5712043.5, 113.75)) }) {
                    var v = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0) };
                    int[] t = other ? new[] { 0, 1, 3, 1, 2, 3 } : new[] { 0, 1, 2, 0, 2, 3 };
                    DMesh3 mesh = Mesh(v.Select(f).ToArray(), t);
                    AxisAlignedBox3d square = mesh.CachedBounds;
                    var tree = new DMeshAABBTree3(mesh, true);
                    var targets = new List<Vector3d>(v);
                    for (int k = 1; k < 98; ++k) {
                        double u = k / 98.0;
                        targets.Add(other ? new Vector3d(u, 1 - u, 0) : new Vector3d(u, u, 0));
                    }
                    foreach (Vector3d q in targets) {
                        Vector3d p = f(q), top = f(q + new Vector3d(0, 0, 1));
                        Assert.True(p.x >= square.Min.x && p.x <= square.Max.x && p.y >= square.Min.y && p.y <= square.Max.y);
                        var ray = new Ray3d(new Vector3d(p.x, p.y, top.z), -Vector3d.AxisZ);
                        Assert.Equal("", TreeVsBrute(mesh, tree, ray));
                        if (BruteHits(mesh, ray).Count == 0) {
                            Assert.False(exact, $"{name}: a miss at an exact scale, target {q}");
                            fails.Add($"{name} {(other ? "anti" : "main")} diagonal, target {q}");
                        }
                    }
                }
            Expect(nameof(RayOntoTheSharedDiagonalOrVertexOfASplitSquare_Hits), string.Join("; ", fails.Take(6)) + (fails.Count > 6 ? $" ({fails.Count} in all)" : ""));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // inside tests through edges and vertices

        // the fixed direction of DMeshAABBTree3.IsInside's parity ray (its source), so that the procedural solids below
        // put an edge or a vertex exactly on that ray
        static readonly Vector3d InsideRay = new Vector3d(0.331960519038825, 0.462531727525156, 0.822111072077288);

        /// <summary>
        /// a square pyramid with its apex exactly at apex and its base square around baseCenter, perpendicular to the
        /// parity ray; the square is shifted off the axis so that the ray crosses the base inside a triangle, not on its diagonal
        /// </summary>
        static DMesh3 Pyramid(Vector3d apex, Vector3d baseCenter, double r)
        {
            Vector3d axis = (baseCenter - apex).Normalized;
            Vector3d u = axis.Cross(Vector3d.AxisZ).Normalized, w = axis.Cross(u).Normalized;
            double c = Math.Cos(0.3), s = Math.Sin(0.3);
            Vector3d u2 = c * u + s * w, w2 = -s * u + c * w;
            Vector3d bc = baseCenter + 0.15 * r * w;      // off the ray, which so crosses a base triangle, not its diagonal
            var v = new[] { apex, bc + r * u2, bc + r * w2, bc - r * u2, bc - r * w2 };
            DMesh3 m = Mesh(v, new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 1, 1, 4, 3, 1, 3, 2 });
            if (ResultChecks.Volume(m) < 0) m.ReverseOrientation();
            Assert.True(m.IsClosed());
            return m;
        }

        /// <summary> a tetrahedron whose edge ab has its midpoint exactly at mid, with c and d further along the parity ray </summary>
        static DMesh3 RidgeTetrahedron(Vector3d mid, double far)
        {
            var e = new Vector3d(0.125, 0, 0);
            Vector3d a = mid - e, b = mid + e;
            Assert.Equal(mid, (a + b) * 0.5);
            Vector3d m2 = mid + far * InsideRay;
            DMesh3 m = Mesh(new[] { a, b, m2 + new Vector3d(0.05, 0.3, -0.1), m2 + new Vector3d(-0.05, -0.2, 0.15) }, new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2 });
            if (ResultChecks.Volume(m) < 0) m.ReverseOrientation();
            Assert.True(m.IsClosed());
            return m;
        }

        static double BruteWinding(DMesh3 m, Vector3d p) => m.WindingNumber(p);

        [Fact]
        public void ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight()
        {
            // https://github.com/isl-org/Open3D/issues/5105: unit cube, point (-1,-1,0), a parity ray along (1,1,1) touches
            // only an edge and gives the wrong sign. https://github.com/mikedh/trimesh/issues/85: a parity ray hits one
            // edge (several triangles), outside is counted as inside. https://github.com/mikedh/trimesh/issues/2317: edge
            // hits gave 0-2 hits, vertex hits 0-n. Rebuilt on IsInside's own parity ray: a pyramid whose apex (4 triangles)
            // lies exactly on the ray from an outside point and from an inside point, and a tetrahedron whose edge
            // (2 triangles) the ray crosses exactly at its midpoint, from outside and from inside; also at 2^-20 and 2^20.
            // Correct: IsInside equals the winding number test (which these points are far enough from the surface for).
            // Known failure Q3: IsInside is wrong in 3 of the 4 cases at every scale (the apex gives 0 hits instead of 1, the
            // edge 2 instead of 1). Pinned: the winding number is right everywhere (asserted always), and the wrong
            // IsInside cases are exactly these three.
            var fails = new List<string>();
            foreach (double s in new[] { 1.0, Math.Pow(2, -20), Math.Pow(2, 20) }) {
                var cases = new List<(string, DMesh3, Vector3d)> {
                    ("apex entry, outside", Pyramid(InsideRay * s, InsideRay * (3 * s), s), Vector3d.Zero),
                    ("apex exit, inside", Pyramid(InsideRay * (2 * s), Vector3d.Zero, s), InsideRay * s),
                    ("ridge entry, outside", Transformed(RidgeTetrahedron(InsideRay, 1), p => p * s), Vector3d.Zero),
                    ("ridge exit, inside", Transformed(RidgeTetrahedron(InsideRay * 2, -1.5), p => p * s), InsideRay * s),
                };
                foreach (var (name, mesh, p) in cases) {
                    double w = BruteWinding(mesh, p);
                    Assert.True(Math.Abs(w) < 0.01 || Math.Abs(w - 1) < 0.01, $"{name}: winding {w}");
                    bool truth = w > 0.5;
                    var tree = new DMeshAABBTree3(mesh, true);
                    if (tree.IsInside(p) != truth) fails.Add($"{name} at {s}: IsInside {!truth}, {tree.FindAllHitTriangles(new Ray3d(p, InsideRay))} hits");
                    Assert.True((tree.WindingNumber(p) > 0.5) == truth, $"{name} at {s}: WindingNumber {tree.WindingNumber(p)}");
                }
            }
            if (KnownFailures.ContainsKey(nameof(ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight)))
                Assert.Equal(9, fails.Count(f => !f.StartsWith("ridge entry")));
            Expect(nameof(ParityRayThroughAVertexOrAnEdge_InsideTestStaysRight), string.Join("; ", fails));
        }


        // ---------------------------------------------------------------------------------------------------------------
        // signed distance grids and winding numbers

        /// <summary> signed distance to the axis-aligned box [lo,hi] (negative inside) </summary>
        static double BoxDistance(Vector3d p, Vector3d lo, Vector3d hi)
        {
            Vector3d c = (lo + hi) * 0.5, e = (hi - lo) * 0.5;
            Vector3d q = new Vector3d(Math.Abs(p.x - c.x) - e.x, Math.Abs(p.y - c.y) - e.y, Math.Abs(p.z - c.z) - e.z);
            var o = new Vector3d(Math.Max(q.x, 0), Math.Max(q.y, 0), Math.Max(q.z, 0));
            return o.Length + Math.Min(Math.Max(q.x, Math.Max(q.y, q.z)), 0);
        }

        static IEnumerable<(string, MeshSignedDistanceGrid)> SdfVariants(DMesh3 mesh, double cell)
        {
            foreach (var mode in new[] { MeshSignedDistanceGrid.InsideModes.ParityCount, MeshSignedDistanceGrid.InsideModes.CrossingCount })
                foreach (bool parallel in new[] { false, true })
                    foreach (bool spatial in new[] { false, true }) {
                        if (spatial && !parallel) continue;
                        yield return ($"{mode} parallel {parallel} spatial {spatial}",
                            new MeshSignedDistanceGrid(mesh, cell, spatial ? new DMeshAABBTree3(mesh, true) : null) { InsideMode = mode, UseParallel = parallel });
                    }
        }

        /// <summary>
        /// "" if every grid sample is finite and has the sign of truth (negative inside) wherever |truth| exceeds minDist,
        /// else the first wrong samples
        /// </summary>
        static string SignErrors(Vector3d origin, double cell, Vector3i dims, Func<int, int, int, float> value, Func<Vector3d, double> truth, double minDist)
        {
            var wrong = new List<string>();
            int count = 0;
            for (int k = 0; k < dims.z; ++k)
                for (int j = 0; j < dims.y; ++j)
                    for (int i = 0; i < dims.x; ++i) {
                        Vector3d p = origin + cell * new Vector3d(i, j, k);
                        float v = value(i, j, k);
                        double d = truth(p);
                        if (!Fin(v) || (Math.Abs(d) > minDist && Math.Sign(v) != Math.Sign(d))) {
                            if (wrong.Count < 4) wrong.Add($"{p}: {v}, truth {d:G4}");
                            count++;
                        }
                    }
            return count == 0 ? "" : $"{count} wrong signs, eg {string.Join(", ", wrong)}";
        }

        [Fact]
        public void SdfGridSign_RaysThroughCubeEdgesAndVertices_Right()
        {
            // https://github.com/isl-org/Open3D/issues/5105: a point whose parity ray touches only an edge of the unit cube
            // got the wrong sign. Rebuilt on the grids' own rays: MeshSignedDistanceGrid casts its sign rays along x through
            // the sample rows, and with the cube [0,1]^3 on a grid of spacing 1/16 the rows y, z in {0, 1} run along the
            // cube's edges and through its vertices (and the faces x = 0, 1 contain samples). Both inside modes, serial,
            // parallel and with a tree; also CachingMeshSDF. Correct: every sample off the surface has the right sign.
            Vector3d lo = Vector3d.Zero, hi = Vector3d.One;
            DMesh3 cube = Box(lo, hi);
            double cell = 1.0 / 16;
            var fails = new List<string>();
            foreach (var (name, sdf) in SdfVariants(cube, cell)) {
                sdf.Compute();
                Assert.Equal(-2 * cell, sdf.GridOriginD.x);          // samples on the cube's faces, edges and vertices
                string e = SignErrors(sdf.GridOriginD, cell, sdf.Dimensions, (i, j, k) => sdf[i, j, k], p => BoxDistance(p, lo, hi), 1e-9);
                if (e != "") fails.Add(name + ": " + e);
            }
            var caching = new CachingMeshSDF(cube, cell, new DMeshAABBTree3(cube, true));
            caching.Initialize();
            string ce = SignErrors(caching.GridOriginD, cell, caching.Dimensions, (i, j, k) => caching.GetValue(new Vector3i(i, j, k)),
                p => BoxDistance(p, lo, hi), 1e-9);
            if (ce != "") fails.Add("CachingMeshSDF: " + ce);
            Expect(nameof(SdfGridSign_RaysThroughCubeEdgesAndVertices_Right), string.Join("; ", fails));
        }

        [Fact]
        public void SdfGridSign_SphereOn64Grid_NoArtifacts()
        {
            // https://github.com/isl-org/Open3D/issues/5669: a sphere of radius 0.8 sampled on a 64^3 grid showed sign
            // artifacts (rows of wrong signs where the rays pass through edges and vertices). Rebuilt: a cube sphere of
            // radius 0.8 centred at the origin (its vertices and edges lie on the symmetric sample rows), grid spacing 1/32.
            // Correct: outside the ball every sample is positive, inside the mesh's inscribed radius negative, and in the
            // shell between them the sign of the brute-force winding number (for samples at least 1e-6 off the surface).
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 0.8, EdgeVertices = 12 }.Generate().MakeDMesh();
            var tree = new DMeshAABBTree3(sphere, true);
            tree.FindNearestTriangle(Vector3d.Zero, out double inner2);
            double inner = Math.Sqrt(inner2), cell = 1.0 / 32;
            var shellTruth = new Dictionary<Vector3d, double>();
            double Truth(Vector3d p)
            {
                double r = p.Length;
                if (r > 0.8 + 1e-9) return 1;
                if (r < inner - 1e-9) return -1;
                if (!shellTruth.TryGetValue(p, out double t)) {
                    // the winding number is exactly 0 or 1 off the surface; samples within 1e-6 of it are not checked
                    // (the grid stores floats, and some samples lie within 1e-16 of a triangle)
                    double dist = MeshQueries.NearestPointDistance(sphere, tree, p);
                    t = dist < 1e-6 ? 0 : (sphere.WindingNumber(p) > 0.5 ? -1 : 1);
                    shellTruth[p] = t;
                }
                return t;
            }
            var fails = new List<string>();
            foreach (var (name, sdf) in SdfVariants(sphere, cell)) {
                sdf.Compute();
                string e = SignErrors(sdf.GridOriginD, cell, sdf.Dimensions, (i, j, k) => sdf[i, j, k], Truth, 0.5);
                if (e != "") fails.Add(name + ": " + e);
            }
            Assert.True(shellTruth.Count > 100);
            Expect(nameof(SdfGridSign_SphereOn64Grid_NoArtifacts), string.Join("; ", fails));
        }

        [Fact]
        public void QueryExactlyOnAFace_DistanceZero_SignsOppositeOneMicronOff()
        {
            // https://github.com/libigl/libigl/issues/1204: unit cube, a query (0, 0.5, 0.5) exactly on a face gave NaN from
            // a sqrd <= 0 check. Rebuilt with the face point, a point on an edge and a vertex. Correct: distance exactly 0
            // and finite everywhere; the winding number is 1/2 on the face, and points 1e-6 inside / outside the face get
            // opposite signs from the winding number and from the interpolated signed distance grids.
            DMesh3 cube = Box(Vector3d.Zero, Vector3d.One);
            var tree = new DMeshAABBTree3(cube, true);
            foreach (Vector3d q in new[] { new Vector3d(0, 0.5, 0.5), new Vector3d(0, 0, 0.5), Vector3d.Zero, new Vector3d(1, 0.25, 0.75) }) {
                int t = tree.FindNearestTriangle(q, out double d2);
                Assert.True(t >= 0);
                Assert.Equal(0.0, d2);
                Assert.Equal(0.0, MeshQueries.NearestPointDistance(cube, tree, q));
                Frame3f f = MeshQueries.NearestPointFrame(cube, tree, q);
                Assert.True(f.Origin.IsFinite && f.Z.IsFinite && Math.Abs(f.Z.Length - 1) < 1e-6);
                Assert.True(Fin(tree.WindingNumber(q)));
            }
            var face = new Vector3d(0, 0.5, 0.5);
            Assert.Equal(0.5, tree.WindingNumber(face), 12);
            Vector3d inside = face + new Vector3d(1e-6, 0, 0), outside = face - new Vector3d(1e-6, 0, 0);
            Assert.True(tree.WindingNumber(inside) > 0.99 && Math.Abs(tree.WindingNumber(outside)) < 0.01);
            Assert.True(tree.FastWindingNumber(inside) > 0.5 && tree.FastWindingNumber(outside) < 0.5);
            Assert.True(tree.IsInside(inside) && !tree.IsInside(outside));

            var sdf = new MeshSignedDistanceGrid(cube, 1.0 / 16) { ComputeMode = MeshSignedDistanceGrid.ComputeModes.FullGrid };
            sdf.Compute();
            var implicitSdf = new DenseGridTrilinearImplicit(sdf.Grid, sdf.GridOriginD, sdf.CellSize);
            Assert.True(Math.Abs(implicitSdf.Value(ref face)) < 1e-6);
            Assert.True(implicitSdf.Value(ref inside) < 0 && implicitSdf.Value(ref outside) > 0,
                $"grid {implicitSdf.Value(ref inside)} / {implicitSdf.Value(ref outside)}");
            var caching = new CachingMeshSDF(cube, 1.0 / 16, tree);
            caching.Initialize();
            var cachingImplicit = new CachingMeshSDFImplicit(caching);
            Assert.True(Math.Abs(cachingImplicit.Value(ref face)) < 1e-6);
            Assert.True(cachingImplicit.Value(ref inside) < 0 && cachingImplicit.Value(ref outside) > 0,
                $"caching {cachingImplicit.Value(ref inside)} / {cachingImplicit.Value(ref outside)}");
        }

        [Fact]
        public void DuplicatedAndOverlappingShells_GeneralizedWindingNumberCounts()
        {
            // https://github.com/libigl/libigl/issues/644: duplicated faces with opposite orientation cancel the winding
            // number. https://github.com/isl-org/Open3D/issues/6919: a doubled closed shell gives even parity, so everything
            // is outside. https://github.com/mikedh/trimesh/issues/1416: two overlapping cubes [-0.5,0.5]^3 and [0,1]^3 in one
            // mesh without a union gave inconsistent inside tests. Correct: the generalized winding number is 2 in the
            // overlap and 1 in the rest of the union, 2 inside a doubled shell, 0 everywhere for a shell plus its reversed
            // copy; the same value from repeated queries and from a fresh tree (WindingNumber exact to 1e-9,
            // FastWindingNumber within 0.1). The parity-mode signed distance grid is negative in the overlap too.
            DMesh3 a = Box(new Vector3d(-0.5, -0.5, -0.5), new Vector3d(0.5, 0.5, 0.5)), b = Box(Vector3d.Zero, Vector3d.One);
            var cases = new (string, DMesh3, (Vector3d p, double w)[])[] {
                ("overlapping cubes", Append(a, b), new[] { (new Vector3d(0.25, 0.25, 0.25), 2.0), (new Vector3d(-0.25, -0.25, -0.25), 1.0),
                    (new Vector3d(0.75, 0.75, 0.75), 1.0), (new Vector3d(0.25, 0.25, -0.25), 1.0), (new Vector3d(2, 0.3, 0.1), 0.0), (new Vector3d(-0.4, 0.8, 0.1), 0.0) }),
                ("doubled shell", Append(b, b), new[] { (new Vector3d(0.5, 0.5, 0.5), 2.0), (new Vector3d(0.1, 0.9, 0.3), 2.0), (new Vector3d(1.5, 0.5, 0.5), 0.0) }),
                ("shell and reversed copy", Append(b, Reversed(b)), new[] { (new Vector3d(0.5, 0.5, 0.5), 0.0), (new Vector3d(1.5, 0.5, 0.5), 0.0) }),
            };
            foreach (var (name, mesh, points) in cases) {
                var tree = new DMeshAABBTree3(mesh, true);
                var fresh = new DMeshAABBTree3(new DMesh3(mesh), true);
                foreach (var (p, w) in points) {
                    double wn = tree.WindingNumber(p), fwn = tree.FastWindingNumber(p);
                    Assert.True(Math.Abs(wn - w) < 1e-9, $"{name} at {p}: WindingNumber {wn}, expected {w}");
                    Assert.True(Math.Abs(fwn - w) < 0.1, $"{name} at {p}: FastWindingNumber {fwn}, expected {w}");
                    Assert.True(Math.Abs(mesh.WindingNumber(p) - w) < 1e-9, $"{name} at {p}: DMesh3.WindingNumber {mesh.WindingNumber(p)}");
                    Assert.Equal(wn, tree.WindingNumber(p));
                    Assert.Equal(fwn, tree.FastWindingNumber(p));
                    Assert.Equal(wn, fresh.WindingNumber(p));
                    Assert.Equal(fwn, fresh.FastWindingNumber(p));
                }
            }
            DMesh3 overlap = cases[0].Item2;
            double cell = 1.0 / 8;
            foreach (bool parallel in new[] { false, true }) {
                var sdf = new MeshSignedDistanceGrid(overlap, cell) { InsideMode = MeshSignedDistanceGrid.InsideModes.ParityCount, UseParallel = parallel };
                sdf.Compute();
                Vector3d o = sdf.GridOriginD;
                var at = new Vector3i((int)Math.Round((0.25 - o.x) / cell), (int)Math.Round((0.25 - o.y) / cell), (int)Math.Round((0.25 - o.z) / cell));
                Assert.Equal(new Vector3d(0.25, 0.25, 0.25), o + cell * new Vector3d(at.x, at.y, at.z));
                Assert.True(sdf[at] < 0, $"parity grid at the overlap: {sdf[at]}");
            }
        }

        /// <summary>
        /// the unit cube with degenerate triangles: one edge split at its endpoint (a zero-length edge and two zero-area
        /// triangles) and a face poked at a point 1e-9 from one of its edges (needles)
        /// </summary>
        static DMesh3 DegenerateCube()
        {
            DMesh3 m = Box(Vector3d.Zero, Vector3d.One);
            int eid = m.FindEdge(0, 1);
            Assert.True(eid >= 0);
            Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
            m.SetVertex(info.vNew, m.GetVertex(0));                 // zero-length edge 0 - vNew
            Index3i tv = m.GetTriangle(2);                          // a face of the cube
            Vector3d a = m.GetVertex(tv.a), b = m.GetVertex(tv.b), c = m.GetVertex(tv.c);
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(2, out DMesh3.PokeTriangleInfo poke));
            m.SetVertex(poke.new_vid, a + 0.5 * (b - a) + 1e-9 * ((c - a) - (c - a).Dot((b - a).Normalized) * (b - a).Normalized).Normalized);
            Assert.True(m.IsClosed() && m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Contains(m.TriangleIndices(), t => m.GetTriArea(t) == 0);
            return m;
        }

        [Fact]
        public void DegenerateTriangles_NoException_SignsRight()
        {
            // https://github.com/libigl/libigl/issues/905: a zero-length edge tripped an assert in the pseudonormal sign
            // computation; https://github.com/libigl/libigl/issues/906: bad triangles gave bumps in the sign field. Rebuilt:
            // the unit cube with a zero-length edge (two zero-area triangles) and a face poked 1e-9 from an edge (needles).
            // Correct: no exception, finite values, every grid sample off the surface with the cube's sign (all inside
            // modes, serial, parallel, with a tree, CachingMeshSDF), the winding number 1 / 0 next to the bad triangles,
            // and the nearest point on them at distance 0.
            DMesh3 m = DegenerateCube();
            Vector3d lo = Vector3d.Zero, hi = Vector3d.One;
            double cell = 1.0 / 16;
            var fails = new List<string>();
            foreach (var (name, sdf) in SdfVariants(m, cell)) {
                sdf.Compute();
                string e = SignErrors(sdf.GridOriginD, cell, sdf.Dimensions, (i, j, k) => sdf[i, j, k], p => BoxDistance(p, lo, hi), 1e-6);
                if (e != "") fails.Add(name + ": " + e);
            }
            var tree = new DMeshAABBTree3(m, true);
            var caching = new CachingMeshSDF(m, cell, tree);
            caching.Initialize();
            string ce = SignErrors(caching.GridOriginD, cell, caching.Dimensions, (i, j, k) => caching.GetValue(new Vector3i(i, j, k)), p => BoxDistance(p, lo, hi), 1e-6);
            if (ce != "") fails.Add("CachingMeshSDF: " + ce);
            foreach (int t in m.TriangleIndices().Where(t => m.GetTriArea(t) < 1e-8)) {
                Vector3d c = m.GetTriCentroid(t);
                Assert.Equal(0.0, MeshQueries.NearestPointDistance(m, tree, c), 15);
                Vector3d n = c - new Vector3d(0.5, 0.5, 0.5);
                Vector3d axis = Math.Abs(n.x) >= Math.Max(Math.Abs(n.y), Math.Abs(n.z)) ? new Vector3d(Math.Sign(n.x), 0, 0)
                    : Math.Abs(n.y) >= Math.Abs(n.z) ? new Vector3d(0, Math.Sign(n.y), 0) : new Vector3d(0, 0, Math.Sign(n.z));
                foreach (Vector3d p in new[] { c - 1e-3 * axis - 1e-3 * n.Normalized, c + 1e-3 * axis }) {
                    bool inside = BoxDistance(p, lo, hi) < 0;
                    double w = tree.WindingNumber(p);
                    if (Math.Abs(w - (inside ? 1 : 0)) > 1e-6) fails.Add($"winding {w} at {p}");
                    if (tree.IsInside(p) != inside) fails.Add($"IsInside at {p}");
                }
            }
            Expect(nameof(DegenerateTriangles_NoException_SignsRight), string.Join("; ", fails));
        }

        [Fact]
        public void MeshWithoutTriangles_QueriesAndGridsGiveTheirDocumentedResults()
        {
            // https://github.com/libigl/libigl/issues/2089: signed distance and queries on an empty mesh crashed. The empty
            // mesh itself is covered (AABBTreeDifferentialTests, BoundsAndMirrorFixesTests); here the variant with
            // vertices but no triangles (eg after removing every triangle), whose bounds are not empty. Correct: the tree's
            // queries return their no-hit sentinels and the grids either throw an ArgumentException or give a finite,
            // unsigned-positive grid (nothing is inside).
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.One); m.AppendVertex(new Vector3d(1, 0, 0));
            var tree = new DMeshAABBTree3(m, true);
            var q = new Vector3d(0.5, 0.5, 0.5);
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestTriangle(q));
            Assert.Equal(DMesh3.InvalidID, tree.FindNearestHitTriangle(new Ray3d(q, Vector3d.AxisX)));
            Assert.Equal(0, tree.FindAllHitTriangles(new Ray3d(q, Vector3d.AxisX)));
            Assert.Equal(double.MaxValue, MeshQueries.NearestPointDistance(m, tree, q));
            Assert.Equal(0, tree.WindingNumber(q));
            Assert.Equal(0, tree.FastWindingNumber(q));
            Assert.False(tree.IsInside(q));
            Assert.Equal(DMesh3.InvalidID, MeshQueries.FindHitTriangle_LinearSearch(m, new Ray3d(q, Vector3d.AxisX)));

            var fails = new List<string>();
            void Grid(string name, Action compute, Func<Vector3i> dims, Func<int, int, int, float> value)
            {
                try {
                    compute();
                } catch (ArgumentException) {
                    return;
                } catch (Exception ex) {
                    fails.Add($"{name}: {ex.GetType().Name} {ex.Message}");
                    return;
                }
                Vector3i d = dims();
                for (int k = 0; k < d.z; ++k)
                    for (int j = 0; j < d.y; ++j)
                        for (int i = 0; i < d.x; ++i) {
                            float v = value(i, j, k);
                            if (!(v > 0) || !Fin(v)) { fails.Add($"{name}: value {v} at {i},{j},{k}"); return; }
                        }
            }
            foreach (var (name, sdf) in SdfVariants(m, 0.25))
                Grid(name, sdf.Compute, () => sdf.Dimensions, (i, j, k) => sdf[i, j, k]);
            var caching = new CachingMeshSDF(m, 0.25, tree);
            Grid("CachingMeshSDF", caching.Initialize, () => caching.Dimensions, (i, j, k) => caching.GetValue(new Vector3i(i, j, k)));
            var wn = new MeshWindingNumberGrid(m, tree, 0.25);
            Grid("MeshWindingNumberGrid", wn.Compute, () => wn.Dimensions, (i, j, k) => 1 - wn[i, j, k]);
            Expect(nameof(MeshWithoutTriangles_QueriesAndGridsGiveTheirDocumentedResults), string.Join("; ", fails));
        }

        // ---------------------------------------------------------------------------------------------------------------
        // point in polygon

        /// <summary> Polygon2d.Contains's crossing rule (Sunday's winding number) with every orientation decided exactly </summary>
        static bool ExactContains(Polygon2d poly, Vector2d p)
        {
            int wn = 0, n = poly.VertexCount;
            for (int i = 0; i < n; ++i) {
                Vector2d a = poly[i], b = poly[(i + 1) % n];
                if (a.y <= p.y) {
                    if (b.y > p.y && ExactPredicates.Orient2d(a, b, p) > 0) ++wn;
                } else if (b.y <= p.y && ExactPredicates.Orient2d(a, b, p) < 0) --wn;
            }
            return wn != 0;
        }

        [Fact]
        public void PointInPolygon_LargeCoordinates_RightAndOnTheBoundaryByItsRule()
        {
            // https://github.com/libigl/libigl/issues/1783: point in polygon with coordinates about 4e5 and integer vertices
            // overflowed and gave winding 0 instead of 1. Rebuilt: an integer star polygon around (4e5, 4e5), the same at UTM
            // coordinates (632117.25, 5712043.5) with a 0.001 m scale, and points inside, outside, within 1e-9 relative of
            // an edge, on edges and on vertices. Correct: Contains equals its own crossing rule decided exactly for every
            // point (so also on the boundary), and WindingIntegral is 1 inside and 0 outside away from the boundary.
            var fails = new List<string>();
            var rnd = new Random(1783);
            foreach (var (name, origin, scale) in new[] { ("4e5", new Vector2d(4e5, 4e5), 1.0), ("2^26", new Vector2d(67108864, 67108864), 1.0),
                ("utm mm", new Vector2d(632117.25, 5712043.5), 1e-3), ("utm", new Vector2d(632117.25, 5712043.5), 1.0) }) {
                var verts = new List<Vector2d>();
                for (int i = 0; i < 14; ++i) {
                    double r = (i % 2 == 0) ? 1000 : 377, a = 2 * Math.PI * i / 14;
                    verts.Add(origin + scale * new Vector2d(Math.Round(r * Math.Cos(a)), Math.Round(r * Math.Sin(a))));
                }
                var poly = new Polygon2d(verts);
                var pts = new List<Vector2d> { origin };
                for (int i = 0; i < 14; ++i) {
                    Vector2d a = poly[i], b = poly[(i + 1) % 14];
                    pts.Add(a);
                    pts.Add(a + 0.5 * (b - a));
                    for (int k = 0; k < 40; ++k) {
                        double t = rnd.NextDouble();
                        Vector2d e = a + t * (b - a), nrm = (b - a).Perp.Normalized;
                        pts.Add(e);
                        pts.Add(e + nrm * (1e-9 * e.Length) * (k % 2 == 0 ? 1 : -1));
                        pts.Add(e + nrm * (scale * 0.25) * (k % 2 == 0 ? 1 : -1));
                    }
                }
                foreach (Vector2d p in pts) {
                    bool exact = ExactContains(poly, p);
                    if (poly.Contains(p) != exact) fails.Add($"{name}: Contains({p.x:R}, {p.y:R}) {!exact}");
                    double distEdge = Enumerable.Range(0, 14).Min(i => new Segment2d(poly[i], poly[(i + 1) % 14]).DistanceSquared(p));
                    if (distEdge > (0.1 * scale) * (0.1 * scale)) {
                        double w = poly.WindingIntegral(p);
                        if (Math.Abs(w - (exact ? 1 : 0)) > 1e-6) fails.Add($"{name}: WindingIntegral {w} at ({p.x:R}, {p.y:R})");
                    }
                }
                Assert.True(poly.Contains(origin) && Math.Abs(poly.WindingIntegral(origin) - 1) < 1e-9);
            }
            Expect(nameof(PointInPolygon_LargeCoordinates_RightAndOnTheBoundaryByItsRule), string.Join("; ", fails.Take(6)) + (fails.Count > 6 ? $" ({fails.Count} in all)" : ""));
        }
    }
}
