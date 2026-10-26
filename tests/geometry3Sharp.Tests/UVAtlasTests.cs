using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Seams (MeshDiscCut), chart segmentation (MeshChartSegmentation), BFF with cone singularities
    /// (ConeFlattening), the exact UV overlap test (UVOverlap) and the complete pipeline (UVAtlasGenerator).
    /// All test meshes are generated.
    /// </summary>
    public class UVAtlasTests
    {
        // ---- test meshes ----

        internal static DMesh3 Sphere(int edgeVertices = 10)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        /// <summary> grid over (s,t) in [0,1]^2 with optional periodic directions </summary>
        static DMesh3 Grid(int ns, int nt, bool wrapS, bool wrapT, Func<double, double, Vector3d> pos)
        {
            var mesh = new DMesh3();
            int cs = wrapS ? ns : ns + 1, ct = wrapT ? nt : nt + 1;
            for (int i = 0; i < cs; ++i)
                for (int j = 0; j < ct; ++j)
                    mesh.AppendVertex(pos((double)i / ns, (double)j / nt));
            for (int i = 0; i < ns; ++i)
                for (int j = 0; j < nt; ++j) {
                    int i1 = (i + 1) % cs, j1 = (j + 1) % ct;
                    int a = i * ct + j, b = i1 * ct + j, c = i1 * ct + j1, d = i * ct + j1;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        /// <summary> torus with radii R, r </summary>
        internal static DMesh3 Torus(double R = 2, double r = 0.7, int nu = 48, int nv = 20)
        {
            return Grid(nu, nv, true, true, (s, t) => {
                double u = 2 * Math.PI * s, v = 2 * Math.PI * t;
                return new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v));
            });
        }

        /// <summary> open cylinder (annulus: genus 0, two boundary loops) </summary>
        static DMesh3 OpenCylinder()
        {
            return Grid(40, 10, true, false, (s, t) => new Vector3d(Math.Cos(2 * Math.PI * s), Math.Sin(2 * Math.PI * s), 2 * t));
        }

        /// <summary> hemisphere z &gt;= 0 (a disc) </summary>
        static DMesh3 Hemisphere()
        {
            DMesh3 mesh = Grid(40, 10, true, false, (s, t) => {
                double phi = 2 * Math.PI * s, theta = Math.PI / 2 * (1 - t);
                return new Vector3d(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta));
            });
            // weld the ring at the pole into one vertex
            var result = new DMesh3();
            var map = new Dictionary<int, int>();
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                map[v] = p.Distance(Vector3d.AxisZ) < 1e-9 ? -1 : result.AppendVertex(p);
            }
            int rp = result.AppendVertex(Vector3d.AxisZ);
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tri = mesh.GetTriangle(t);
                int a = map[tri.a] < 0 ? rp : map[tri.a], b = map[tri.b] < 0 ? rp : map[tri.b], c = map[tri.c] < 0 ? rp : map[tri.c];
                if (a != b && b != c && c != a) result.AppendTriangle(a, b, c);
            }
            return result;
        }

        class DoubleTorus : ImplicitFunction3d
        {
            // two tori side by side, blended: genus 2
            public double Value(ref Vector3d p)
            {
                double f1 = torus(p - new Vector3d(-1, 0, 0)), f2 = torus(p - new Vector3d(1, 0, 0));
                double k = 0.15, h = Math.Max(k - Math.Abs(f1 - f2), 0) / k;
                return Math.Min(f1, f2) - h * h * k * 0.25;
            }
            static double torus(Vector3d p)
            {
                double q = Math.Sqrt(p.x * p.x + p.y * p.y) - 1;
                return Math.Sqrt(q * q + p.z * p.z) - 0.4;
            }
        }

        /// <summary> genus 2 surface from marching cubes (irregular triangles and slivers) </summary>
        internal static DMesh3 GenusTwo(double cell = 0.1)
        {
            var mc = new MarchingCubes() {
                Implicit = new DoubleTorus(), IsoValue = 0, CubeSize = cell, ParallelCompute = false,
                Bounds = new AxisAlignedBox3d(new Vector3d(-2.6, -1.6, -0.7), new Vector3d(2.6, 1.6, 0.7)),
                RootMode = MarchingCubes.RootfindingModes.Bisection,
            };
            mc.Generate();
            return mc.Mesh;
        }

        /// <summary> axis aligned box [-1,1]^3 with n x n quads per face </summary>
        internal static DMesh3 Cube(int n = 8)
        {
            return new GridBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One), EdgeVertices = n + 1 }.Generate().MakeDMesh();
        }

        /// <summary> the cube with a few caps: triangles poked at an edge midpoint (one collinear triangle each) </summary>
        static DMesh3 CubeWithCaps(int n = 4)
        {
            DMesh3 cube = Cube(n);
            foreach (int tid in new[] { 3, 20, 41, 77 }) {
                DMesh3.PokeTriangleInfo info;
                Assert.Equal(MeshResult.Ok, cube.PokeTriangle(tid, new Vector3d(0.5, 0.5, 0), out info));
            }
            return cube;
        }

        static DMesh3 Append(params DMesh3[] meshes)
        {
            var all = new DMesh3();
            double x = 0;
            foreach (DMesh3 m in meshes) {
                DMesh3 c = new DMesh3(m);
                MeshTransforms.Translate(c, new Vector3d(x - c.CachedBounds.Min.x, 0, 0));
                x += c.CachedBounds.Width + 1;
                MeshEditor.Append(all, c);
            }
            return all;
        }

        internal static int EulerCharacteristic(DMesh3 mesh)
        {
            return mesh.VertexCount - mesh.EdgeCount + mesh.TriangleCount;
        }

        internal static int BoundaryLoopCount(DMesh3 mesh)
        {
            return new MeshBoundaryLoops(mesh).Count;
        }

        static int ComponentCount(DMesh3 mesh)
        {
            var cc = new MeshConnectedComponents(mesh);
            cc.FindConnectedT();
            return cc.Count;
        }

        static void AssertDisc(DMesh3 cut)
        {
            Assert.True(cut.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(1, EulerCharacteristic(cut));
            Assert.Equal(1, BoundaryLoopCount(cut));
            Assert.Equal(1, ComponentCount(cut));
            Assert.Empty(cut.VertexIndices().Where(v => cut.IsBowtieVertex(v)));
        }

        /// <summary> the cut mesh has the input's triangles (each once), on copies of its vertices </summary>
        static void AssertSameTriangles(DMesh3 input, DMesh3 cut, int[] toV, int[] toT)
        {
            Assert.Equal(input.TriangleCount, cut.TriangleCount);
            Assert.Equal(input.TriangleCount, toT.Distinct().Count());
            foreach (int tid in cut.TriangleIndices()) {
                Index3i t = cut.GetTriangle(tid), s = input.GetTriangle(toT[tid]);
                for (int j = 0; j < 3; ++j) {
                    Assert.Equal(s[j], toV[t[j]]);
                    Assert.Equal(input.GetVertex(s[j]), cut.GetVertex(t[j]));
                }
            }
        }


        // ---- seams ----

        [Fact]
        public void Meshes_HaveTheExpectedTopology()
        {
            Assert.Equal(2, EulerCharacteristic(Sphere()));
            Assert.Equal(0, EulerCharacteristic(Torus()));
            DMesh3 g2 = GenusTwo();
            Assert.Equal(-2, EulerCharacteristic(g2));
            Assert.Equal(0, BoundaryLoopCount(g2));
            Assert.Equal(1, ComponentCount(g2));
            Assert.Equal(2, EulerCharacteristic(Cube()));
            Assert.Equal(0, EulerCharacteristic(OpenCylinder()));
            Assert.Equal(2, BoundaryLoopCount(OpenCylinder()));
            Assert.Equal(1, EulerCharacteristic(Hemisphere()));
            Assert.Equal(1, BoundaryLoopCount(Hemisphere()));
        }

        [Fact]
        public void DiscCut_ClosedSurfaces_BecomeDiscs()
        {
            var cases = new (string name, DMesh3 mesh, int genus)[] {
                ("sphere", Sphere(), 0), ("torus", Torus(), 1), ("genus2", GenusTwo(), 2), ("cube", Cube(4), 0)
            };
            foreach (var c in cases) {
                var cut = new MeshDiscCut(c.mesh);
                Assert.True(cut.Compute(), c.name + ": " + cut.Error);
                Assert.Equal(c.genus, cut.Genus);
                Assert.Equal(1, cut.Components);
                Assert.Equal(0, cut.InputBoundaryLoops);
                AssertDisc(cut.CutMesh);
                AssertSameTriangles(c.mesh, cut.CutMesh, cut.CutToInputVertex, cut.CutToInputTriangle);
                Assert.True(MeshDiscCut.IsDiscs(cut.CutMesh, out string why), why);
                // the seams: 2g loops through the root (a path for genus 0), V - E = 1 - 2g, no dangling ends
                var seamVerts = new HashSet<int>(cut.SeamEdges.SelectMany(e => new[] { e.a, e.b }));
                Assert.Equal(1 - 2 * c.genus, seamVerts.Count - cut.SeamEdges.Count);
                var degree = seamVerts.ToDictionary(v => v, v => cut.SeamEdges.Count(e => e.a == v || e.b == v));
                Assert.Equal(c.genus == 0 ? 2 : 0, degree.Values.Count(d => d == 1));
                // each seam edge becomes two boundary edges
                Assert.Equal(2 * cut.SeamEdges.Count, cut.CutMesh.BoundaryEdgeIndices().Count());
                double len = cut.SeamEdges.Sum(e => c.mesh.GetVertex(e.a).Distance(c.mesh.GetVertex(e.b)));
                Assert.Equal(len, cut.SeamLength, 9);
            }
        }

        [Fact]
        public void DiscCut_TorusLoops_AreShort()
        {
            // the greedy shortest loops of a torus are a meridian (2 pi r) and a longitude, through the root
            var cut = new MeshDiscCut(Torus(2, 0.5, 48, 16));
            Assert.True(cut.Compute(), cut.Error);
            double meridian = 2 * Math.PI * 0.5, longitude = 2 * Math.PI * 1.5;
            Assert.InRange(cut.SeamLength, meridian + longitude - 1e-6, 1.15 * (meridian + 2 * Math.PI * 2.5));
        }

        [Fact]
        public void DiscCut_OpenSurfaces_Components_Bowties()
        {
            // a disc is not cut
            var disc = new MeshDiscCut(Hemisphere());
            Assert.True(disc.Compute(), disc.Error);
            Assert.Empty(disc.SeamEdges);
            Assert.Equal(1, disc.InputBoundaryLoops);
            AssertDisc(disc.CutMesh);

            // an annulus gets one seam between its boundaries
            DMesh3 cyl = OpenCylinder();
            var annulus = new MeshDiscCut(cyl);
            Assert.True(annulus.Compute(), annulus.Error);
            Assert.Equal(0, annulus.Genus);
            Assert.Equal(2, annulus.InputBoundaryLoops);
            AssertDisc(annulus.CutMesh);
            Assert.InRange(annulus.SeamLength, 2 - 1e-9, 2.5);   // the height of the cylinder

            // a torus with a hole: genus 1, one boundary loop
            DMesh3 holed = Torus();
            foreach (int tid in new[] { 0, 1, 2, 3 })
                holed.RemoveTriangle(tid);
            var h = new MeshDiscCut(holed);
            Assert.True(h.Compute(), h.Error);
            Assert.Equal(1, h.Genus);
            Assert.Equal(1, h.InputBoundaryLoops);
            AssertDisc(h.CutMesh);

            // several components: one disc each
            DMesh3 several = Append(Sphere(6), Torus(), OpenCylinder(), GenusTwo(0.15));
            var s = new MeshDiscCut(several);
            Assert.True(s.Compute(), s.Error);
            Assert.Equal(4, s.Components);
            Assert.Equal(3, s.Genus);
            Assert.Equal(4, ComponentCount(s.CutMesh));
            Assert.True(MeshDiscCut.IsDiscs(s.CutMesh, out string why), why);
            AssertSameTriangles(several, s.CutMesh, s.CutToInputVertex, s.CutToInputTriangle);

            // two closed boxes sharing one vertex (bowtie): split, then cut
            DMesh3 bow = Cube(2);
            int corner = bow.VertexIndices().First(v => bow.GetVertex(v) == new Vector3d(1, 1, 1));
            DMesh3 other = Cube(2);
            MeshTransforms.Translate(other, new Vector3d(2, 2, 2));
            int otherCorner = other.VertexIndices().First(v => other.GetVertex(v) == new Vector3d(1, 1, 1));
            var map = new Dictionary<int, int>();
            foreach (int v in other.VertexIndices())
                map[v] = v == otherCorner ? corner : bow.AppendVertex(other.GetVertex(v));
            foreach (int t in other.TriangleIndices()) {
                Index3i tri = other.GetTriangle(t);
                Assert.True(bow.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]) >= 0);
            }
            Assert.True(bow.IsBowtieVertex(corner));
            var b = new MeshDiscCut(bow);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2, b.Components);
            Assert.True(MeshDiscCut.IsDiscs(b.CutMesh, out why), why);
        }

        [Fact]
        public void DiscCut_Tetrahedron_SeamOfTwoEdges()
        {
            // every vertex is adjacent to the root: the path to the farthest vertex is a single edge, which can
            // not be split, so the seam is extended by one edge
            var tet = new DMesh3();
            tet.AppendVertex(new Vector3d(1, 1, 1)); tet.AppendVertex(new Vector3d(1, -1, -1));
            tet.AppendVertex(new Vector3d(-1, 1, -1)); tet.AppendVertex(new Vector3d(-1, -1, 1));
            tet.AppendTriangle(0, 1, 2); tet.AppendTriangle(0, 3, 1); tet.AppendTriangle(0, 2, 3); tet.AppendTriangle(1, 3, 2);
            var cut = new MeshDiscCut(tet);
            Assert.True(cut.Compute(), cut.Error);
            Assert.Equal(2, cut.SeamEdges.Count);
            AssertDisc(cut.CutMesh);
            Assert.Equal(5, cut.CutMesh.VertexCount);
        }

        [Fact]
        public void DiscCut_RequiredVertices_AreOnTheSeams()
        {
            DMesh3 sphere = Sphere(8);
            var rnd = new Random(3);
            int[] verts = sphere.VertexIndices().ToArray();
            var required = Enumerable.Range(0, 6).Select(i => verts[rnd.Next(verts.Length)]).Distinct().ToList();
            var cut = new MeshDiscCut(sphere) { RequiredVertices = required };
            Assert.True(cut.Compute(), cut.Error);
            AssertDisc(cut.CutMesh);
            foreach (int r in required)
                Assert.Contains(cut.CutMesh.VertexIndices(), v => cut.CutToInputVertex[v] == r && cut.CutMesh.IsBoundaryVertex(v));
            // seam ends (degree 1) only at required vertices: a tree spanning them
            var seamVerts = new HashSet<int>(cut.SeamEdges.SelectMany(e => new[] { e.a, e.b }));
            foreach (int v in seamVerts)
                if (cut.SeamEdges.Count(e => e.a == v || e.b == v) == 1)
                    Assert.Contains(v, required);
            Assert.Equal(1, seamVerts.Count - cut.SeamEdges.Count);

            Assert.False(new MeshDiscCut(sphere) { RequiredVertices = new[] { sphere.MaxVertexID + 5 } }.Compute());
            Assert.False(new MeshDiscCut(new DMesh3()).Compute());
        }

        [Fact]
        public void DiscCut_FeatureWeight_PutsSeamsOnSharpEdges()
        {
            DMesh3 cube = Cube(4);
            var corners = cube.VertexIndices().Where(v => {
                Vector3d p = cube.GetVertex(v);
                return Math.Abs(p.x) == 1 && Math.Abs(p.y) == 1 && Math.Abs(p.z) == 1;
            }).ToList();
            Func<Index2i, bool> sharp = (e) => {
                Vector3d a = cube.GetVertex(e.a), b = cube.GetVertex(e.b);
                // on a cube edge: two coordinates at the same +-1 for both ends
                int n = 0;
                for (int k = 0; k < 3; ++k) if (Math.Abs(a[k]) == 1 && a[k] == b[k]) n++;
                return n >= 2;
            };
            var plain = new MeshDiscCut(cube) { RequiredVertices = corners };
            Assert.True(plain.Compute(), plain.Error);
            var feature = new MeshDiscCut(cube) { RequiredVertices = corners, FeatureWeight = 10 };
            Assert.True(feature.Compute(), feature.Error);
            Assert.Contains(plain.SeamEdges, e => !sharp(e));          // shortest paths cut across faces
            Assert.All(feature.SeamEdges, e => Assert.True(sharp(e)));  // feature weighted paths follow the cube edges
            Assert.Equal(14, feature.SeamLength, 9);                    // a spanning tree of the corners: 7 cube edges
        }

        [Fact]
        public void SplitAlongEdges_NoCut_IsACopy_AndRejectsSingleEdgeSlit()
        {
            DMesh3 sphere = Sphere(6);
            int[] toV, toT;
            DMesh3 copy = MeshDiscCut.SplitAlongEdges(sphere, null, out toV, out toT);
            Assert.Equal(sphere.VertexCount, copy.VertexCount);
            AssertSameTriangles(sphere, copy, toV, toT);
            // a closed surface cut along a single edge can not be represented (the edge would be doubled)
            int eid = sphere.EdgeIndices().First();
            Assert.Null(MeshDiscCut.SplitAlongEdges(sphere, e => e == eid, out toV, out toT));
            // two adjacent edges: a slit, a disc
            int v0 = sphere.GetEdgeV(eid).b;
            int eid2 = sphere.VtxEdgesItr(v0).First(e => e != eid);
            DMesh3 slit = MeshDiscCut.SplitAlongEdges(sphere, e => e == eid || e == eid2, out toV, out toT);
            AssertDisc(slit);
            Assert.Equal(sphere.VertexCount + 1, slit.VertexCount);
        }


        // ---- exact overlap test ----

        static Vector2d V(double x, double y) { return new Vector2d(x, y); }

        static DMesh3 TriangleSoup(List<Vector2d[]> tris, out Vector2d[] uv)
        {
            var mesh = new DMesh3();
            var list = new List<Vector2d>();
            foreach (Vector2d[] t in tris) {
                int a = mesh.AppendVertex(new Vector3d(t[0].x, t[0].y, 0));
                int b = mesh.AppendVertex(new Vector3d(t[1].x, t[1].y, 0));
                int c = mesh.AppendVertex(new Vector3d(t[2].x, t[2].y, 0));
                mesh.AppendTriangle(a, b, c);
                list.AddRange(t);
            }
            uv = list.ToArray();
            return mesh;
        }

        [Fact]
        public void UVOverlap_ExactCases()
        {
            Vector2d[] A = { V(0, 0), V(1, 0), V(0, 1) };
            Func<Vector2d[], Vector2d[], int> pairs = (a, b) => {
                Vector2d[] uv;
                DMesh3 m = TriangleSoup(new List<Vector2d[]>() { a, b }, out uv);
                return UVOverlap.Compute(m, uv).OverlappingPairs;
            };
            Assert.Equal(0, pairs(A, new[] { V(1, 0), V(1, 1), V(0, 1) }));                  // shared edge
            Assert.Equal(0, pairs(A, new[] { V(0, 0), V(-1, 0), V(0, -1) }));                // shared vertex, opposite
            Assert.Equal(0, pairs(A, new[] { V(0.5, 0.5), V(1.5, 0.5), V(0.5, 1.5) }));      // touches the hypotenuse
            Assert.Equal(0, pairs(A, new[] { V(2, 0), V(3, 0), V(2, 1) }));                   // apart
            Assert.Equal(1, pairs(A, new[] { V(0.1, 0.1), V(0.3, 0.1), V(0.1, 0.3) }));      // contained
            Assert.Equal(1, pairs(A, A));                                                     // identical
            Assert.Equal(1, pairs(A, new[] { V(0.5, -0.5), V(0.5, 1), V(-0.5, 0.2) }));      // crossing, no vertex inside
            Assert.Equal(1, pairs(A, new[] { V(0, 1), V(0, 0), V(1, 1) }));                  // shared edge, same side (clockwise input)
            // the corner just across the hypotenuse is exactly decided
            double eps = Math.Pow(2, -40);
            Assert.Equal(1, pairs(A, new[] { V(0.5 - eps, 0.5), V(1.5, 0.5), V(0.5, 1.5) }));

            // clockwise triangles are tested like their counter-clockwise versions
            Vector2d[] Acw = { V(0, 0), V(0, 1), V(1, 0) };
            Assert.Equal(1, pairs(Acw, new[] { V(0.1, 0.3), V(0.3, 0.1), V(0.1, 0.1) }));
            Assert.Equal(1, pairs(Acw, new[] { V(0.1, 0.1), V(0.3, 0.1), V(0.1, 0.3) }));
            Assert.Equal(0, pairs(Acw, new[] { V(1, 1), V(1, 0), V(0, 1) }));
            Assert.Equal(0, pairs(Acw, new[] { V(2, 0), V(2, 1), V(3, 0) }));

            // inverted and degenerate triangles
            Vector2d[] uv2;
            DMesh3 m2 = TriangleSoup(new List<Vector2d[]>() { new[] { V(0, 0), V(0, 1), V(1, 0) }, new[] { V(5, 5), V(6, 6), V(7, 7) } }, out uv2);
            UVOverlap o = UVOverlap.Compute(m2, uv2);
            Assert.Equal(1, o.InvertedTriangles);
            Assert.Equal(1, o.DegenerateTriangles);
            Assert.False(o.IsInjective);
        }

        [Fact]
        public void CollapsedTriangles_ArePerTriangle()
        {
            // triangle 0: a good surface triangle with collinear UVs (collapsed); triangle 1: a degenerate surface
            // triangle (collinear) with a good UV triangle. Comparing counts would cancel them out.
            var mesh = new DMesh3();
            for (int i = 0; i < 3; ++i) mesh.AppendVertex(new Vector3d(i == 1 ? 1 : 0, i == 2 ? 1 : 0, 0));
            for (int i = 0; i < 3; ++i) mesh.AppendVertex(new Vector3d(5 + i, 0, 0));
            mesh.AppendTriangle(0, 1, 2);
            mesh.AppendTriangle(3, 4, 5);
            Vector2d[] uv = { V(0, 0), V(1, 1), V(2, 2), V(5, 0), V(6, 0), V(5, 1) };
            UVOverlap o = UVOverlap.Compute(mesh, uv);
            Assert.Equal(new[] { 0 }, o.DegenerateTriangleIds);
            Assert.Equal(1, UVAtlasGenerator.CollapsedTriangles(mesh, o));
            // a degenerate surface triangle may be degenerate in UV too
            uv[5] = V(7, 0);
            uv[2] = V(0, 1);
            o = UVOverlap.Compute(mesh, uv);
            Assert.Equal(new[] { 1 }, o.DegenerateTriangleIds);
            Assert.Equal(0, UVAtlasGenerator.CollapsedTriangles(mesh, o));
        }

        [Fact]
        public void UVOverlap_Grid_MatchesBruteForce()
        {
            var rnd = new Random(7);
            for (int round = 0; round < 7; ++round) {
                var tris = new List<Vector2d[]>();
                int n = 300;
                for (int i = 0; i < n; ++i) {
                    Vector2d c = V(rnd.NextDouble() * 10, rnd.NextDouble() * 10);
                    // round 4: large triangles spanning many cells; round 5: a few huge ones among small ones
                    // round 6: three size classes, most triangles tiny (several grid levels)
                    double s = round == 4 ? 3 : (round == 5 && i % 50 == 0 ? 12 : (round == 6 ? (i % 3 == 0 ? 2 : (i % 3 == 1 ? 0.2 : 0.01)) : 0.3));
                    Vector2d[] t = { c, c + V(rnd.NextDouble() * s, rnd.NextDouble() * s * 0.2), c + V(rnd.NextDouble() * s * 0.2, rnd.NextDouble() * s) };
                    if (ExactPredicates.Orient2d(t[0], t[1], t[2]) < 0) { Vector2d tmp = t[1]; t[1] = t[2]; t[2] = tmp; }
                    tris.Add(t);
                }
                // some exactly touching and some duplicated triangles
                for (int i = 0; i < 20; ++i) {
                    Vector2d[] t = tris[i];
                    tris.Add(new[] { t[1], t[0], t[0] + t[1] - t[2] });   // mirrored across an edge: touches
                    if (i % 4 == 0) tris.Add(new[] { t[0], t[1], t[2] });
                }
                Vector2d[] uv;
                DMesh3 mesh = TriangleSoup(tris, out uv);
                UVOverlap o = UVOverlap.Compute(mesh, uv);
                int brute = 0;
                for (int i = 0; i < tris.Count; ++i)
                    for (int j = i + 1; j < tris.Count; ++j) {
                        Vector2d[] a = tris[i], b = tris[j];
                        if (ExactPredicates.Orient2d(a[0], a[1], a[2]) <= 0 || ExactPredicates.Orient2d(b[0], b[1], b[2]) <= 0) continue;
                        if (UVOverlap.InteriorsOverlap(a[0], a[1], a[2], b[0], b[1], b[2])) brute++;
                    }
                Assert.Equal(brute, o.OverlappingPairs);
                Assert.True(brute > 0);
                Assert.Equal(0, o.InvertedTriangles);
            }
        }

        [Fact]
        public void UVOverlap_MixedSizes_NotQuadratic()
        {
            // 120k tiny and 40k larger triangles: with one grid level the larger ones were tested against all
            // triangles (about a minute); the levels make it near linear
            var rnd = new Random(5);
            var tris = new List<Vector2d[]>();
            for (int i = 0; i < 160200; ++i) {
                // and 200 triangles the size of the domain, which one grid level would put in millions of cells
                double s = i < 120000 ? 1e-4 : (i < 160000 ? 3e-3 : 0.4);
                Vector2d c = V(rnd.NextDouble() * 0.4, rnd.NextDouble() * 0.4);
                tris.Add(new[] { c, c + V(s, 0), c + V(0, s) });
            }
            Vector2d[] uv;
            DMesh3 mesh = TriangleSoup(tris, out uv);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            UVOverlap o = UVOverlap.Compute(mesh, uv);
            Assert.True(sw.Elapsed.TotalSeconds < 15, "UVOverlap took " + sw.Elapsed.TotalSeconds + " s");
            Assert.True(o.OverlappingPairs > 0);
        }

        [Fact]
        public void UVOverlap_AgreesWithClippedArea()
        {
            // independent check: area of the intersection polygon (Sutherland-Hodgman) is clearly positive or zero
            var rnd = new Random(11);
            int overlapping = 0, apart = 0;
            for (int k = 0; k < 4000; ++k) {
                Vector2d[] a = RandomTriangle(rnd), b = RandomTriangle(rnd);
                double area = ClippedArea(a, b);
                bool exact = UVOverlap.InteriorsOverlap(a[0], a[1], a[2], b[0], b[1], b[2]);
                if (area > 1e-9) { Assert.True(exact); overlapping++; }
                else if (area == 0 && SeparatedBy(a, b, 1e-9)) { Assert.False(exact); apart++; }
            }
            Assert.True(overlapping > 500 && apart > 500, overlapping + " " + apart);
        }

        static Vector2d[] RandomTriangle(Random rnd)
        {
            Vector2d[] t = { V(rnd.NextDouble(), rnd.NextDouble()), V(rnd.NextDouble(), rnd.NextDouble()), V(rnd.NextDouble(), rnd.NextDouble()) };
            if ((t[1] - t[0]).Cross(t[2] - t[0]) < 0) { Vector2d tmp = t[1]; t[1] = t[2]; t[2] = tmp; }
            return t;
        }

        static bool SeparatedBy(Vector2d[] a, Vector2d[] b, double gap)
        {
            foreach (var (p, q) in new[] { (a, b), (b, a) })
                for (int i = 0; i < 3; ++i) {
                    Vector2d e = p[(i + 1) % 3] - p[i];
                    Vector2d nrm = V(e.y, -e.x).Normalized;
                    if (q.All(x => (x - p[i]).Dot(nrm) > gap)) return true;
                }
            return false;
        }

        static double ClippedArea(Vector2d[] a, Vector2d[] b)
        {
            var poly = new List<Vector2d>(b);
            for (int i = 0; i < 3 && poly.Count > 0; ++i) {
                Vector2d p = a[i], q = a[(i + 1) % 3];
                var next = new List<Vector2d>();
                for (int k = 0; k < poly.Count; ++k) {
                    Vector2d s = poly[k], e = poly[(k + 1) % poly.Count];
                    double ds = (q - p).Cross(s - p), de = (q - p).Cross(e - p);
                    if (ds >= 0) next.Add(s);
                    if ((ds >= 0) != (de >= 0)) next.Add(s + (e - s) * (ds / (ds - de)));
                }
                poly = next;
            }
            double area = 0;
            for (int k = 0; k < poly.Count; ++k) area += poly[k].Cross(poly[(k + 1) % poly.Count]);
            return Math.Max(0, 0.5 * area);
        }


        // ---- chart segmentation ----

        static bool ChartsConnected(DMesh3 mesh, int[] chartOf, int charts)
        {
            for (int c = 0; c < charts; ++c) {
                var tris = mesh.TriangleIndices().Where(t => chartOf[t] == c).ToList();
                if (tris.Count == 0) return false;
                var seen = new HashSet<int>() { tris[0] };
                var stack = new Stack<int>(seen);
                while (stack.Count > 0) {
                    int t = stack.Pop();
                    Index3i nb = mesh.GetTriNeighbourTris(t);
                    for (int j = 0; j < 3; ++j)
                        if (nb[j] >= 0 && chartOf[nb[j]] == c && seen.Add(nb[j])) stack.Push(nb[j]);
                }
                if (seen.Count != tris.Count) return false;
            }
            return true;
        }

        [Fact]
        public void Segmentation_Cube_OneChartPerFace()
        {
            DMesh3 cube = Cube(6);
            var seg = new MeshChartSegmentation(cube) { MaxNormalAngle = 30 };
            Assert.True(seg.Compute());
            Assert.Equal(6, seg.ChartCount);
            Assert.True(seg.MaxAngle < 1e-6, "max angle " + seg.MaxAngle);
            Assert.True(ChartsConnected(cube, seg.TriangleChart, seg.ChartCount));
            foreach (int t in cube.TriangleIndices())
                foreach (int u in cube.TriangleIndices())
                    if (seg.TriangleChart[t] == seg.TriangleChart[u])
                        Assert.True(cube.GetTriNormal(t).Dot(cube.GetTriNormal(u)) > 0.999);
        }

        [Fact]
        public void Segmentation_Sphere_BoundedNormalDeviation()
        {
            DMesh3 sphere = Sphere(16);
            foreach (double angle in new[] { 45.0, 25.0 }) {
                var seg = new MeshChartSegmentation(sphere) { MaxNormalAngle = angle, MaxCharts = 200 };
                Assert.True(seg.Compute());
                Assert.True(seg.ChartCount < 200);
                Assert.True(seg.MaxAngle <= angle, $"max angle {seg.MaxAngle} > {angle}");
                Assert.True(ChartsConnected(sphere, seg.TriangleChart, seg.ChartCount));
            }
            // limited number of charts
            var few = new MeshChartSegmentation(sphere) { MaxNormalAngle = 1, MaxCharts = 5 };
            Assert.True(few.Compute());
            Assert.Equal(5, few.ChartCount);
            Assert.True(few.MaxAngle > 1);
        }

        [Fact]
        public void Segmentation_SplitInTwo_FlatAndCurved()
        {
            DMesh3 flat = Grid(20, 4, false, false, (s, t) => new Vector3d(10 * s, 2 * t, 0));
            DMesh3 cyl = OpenCylinder();
            foreach (DMesh3 m in new[] { flat, cyl }) {
                var tris = m.TriangleIndices().ToList();
                int[] labels;
                Assert.Equal(2, MeshChartSegmentation.SplitInTwo(m, tris, 0.1, out labels));
                int[] chartOf = new int[m.MaxTriangleID];
                for (int i = 0; i < tris.Count; ++i) chartOf[tris[i]] = labels[i];
                Assert.True(ChartsConnected(m, chartOf, 2));
                int n0 = labels.Count(l => l == 0);
                Assert.InRange(n0, tris.Count / 5, 4 * tris.Count / 5);
            }
            // a flat strip with a cap at its far end: the second chart is not seeded at the cap (it would keep
            // a zero normal and stay a chart of one degenerate triangle)
            DMesh3 capped = Grid(20, 4, false, false, (s, t) => new Vector3d(10 * s, 2 * t, 0));
            int farT = capped.TriangleIndices().OrderByDescending(t => capped.GetTriCentroid(t).x).First();
            Index3i ft3 = capped.GetTriangle(farT);
            Vector3d bary = Vector3d.Zero;
            for (int j = 0; j < 3; ++j) if (capped.GetVertex(ft3[j]).x == 10) bary[j] = 0.5;
            Assert.Equal(1, bary.x + bary.y + bary.z, 12);
            DMesh3.PokeTriangleInfo pi;
            Assert.Equal(MeshResult.Ok, capped.PokeTriangle(farT, bary, out pi));
            var ct = capped.TriangleIndices().ToList();
            int[] cl;
            Assert.Equal(2, MeshChartSegmentation.SplitInTwo(capped, ct, 0.1, out cl));
            for (int k = 0; k < 2; ++k)
                Assert.Contains(Enumerable.Range(0, ct.Count), i => cl[i] == k && capped.GetTriArea(ct[i]) > 1e-6);

            // a flat strip is split across its length
            var ft = flat.TriangleIndices().ToList();
            int[] fl;
            MeshChartSegmentation.SplitInTwo(flat, ft, 0.1, out fl);
            double x0 = ft.Where((t, i) => fl[i] == 0).Average(t => flat.GetTriCentroid(t).x);
            double x1 = ft.Where((t, i) => fl[i] == 1).Average(t => flat.GetTriCentroid(t).x);
            Assert.True(Math.Abs(x0 - x1) > 3);
        }


        // ---- BFF with cones ----

        [Fact]
        public void Cones_Cube_CornersBecomeCones_NoDistortion()
        {
            DMesh3 cube = Cube(8);
            var cf = new ConeFlattening(cube);
            ParameterizationResult r = cf.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(8, cf.ConeVertices.Count);
            foreach (int c in cf.ConeVertices) {
                Vector3d p = cube.GetVertex(c);
                Assert.Equal(1, Math.Abs(p.x), 9); Assert.Equal(1, Math.Abs(p.y), 9); Assert.Equal(1, Math.Abs(p.z), 9);
                Assert.Equal(Math.PI / 2, cf.ConeCurvature[c], 6);
            }
            Assert.True(cf.MaxAbsScaleFactor < 1e-6, "max |u| " + cf.MaxAbsScaleFactor);
            AssertDisc(cf.CutMesh);
            Assert.True(r.Distortion.MaxAreaDistortion < 1e-6, "area distortion " + r.Distortion.MaxAreaDistortion);
            Assert.True(r.Distortion.MaxConformal < 1 + 1e-6);
            Assert.Equal(1, r.Distortion.AreaScale, 6);
            var ov = UVOverlap.Compute(cf.CutMesh, r.UV);
            Assert.True(ov.IsInjective, $"overlaps {ov.OverlappingPairs} inverted {ov.InvertedTriangles}");
            // without cones (a single cut, plain BFF) the cube is strongly distorted
            var single = new MeshDiscCut(cube);
            Assert.True(single.Compute());
            var plain = new BoundaryFirstFlattening(single.CutMesh).Compute();
            Assert.True(plain.Distortion.MeanAreaDistortion > 0.5);
        }

        [Fact]
        public void Cones_CapTriangles_DoNotBecomeCones()
        {
            // a cap has an angle of pi at the poked vertex; skipping it would give that vertex curvature pi
            DMesh3 cube = CubeWithCaps();
            Assert.Equal(4, cube.TriangleIndices().Count(t => cube.GetTriArea(t) < 1e-12));
            var cf = new ConeFlattening(cube);
            ParameterizationResult r = cf.Compute();
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(8, cf.ConeVertices.Count);
            foreach (int c in cf.ConeVertices) {
                Vector3d p = cube.GetVertex(c);
                Assert.Equal(1, Math.Abs(p.x) * Math.Abs(p.y) * Math.Abs(p.z), 9);
            }
            Assert.True(cf.MaxAbsScaleFactor < 1e-6);
            Assert.Equal(new Vector3d(0, 0, Math.PI), ConeFlattening.DegenerateAngles(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(1, 0, 0)));
            Assert.Equal(new Vector3d(Math.PI / 2, Math.PI / 2, 0), ConeFlattening.DegenerateAngles(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0), new Vector3d(1, 0, 0)));
        }

        [Fact]
        public void Cones_Sphere_LowAreaDistortion()
        {
            DMesh3 sphere = Sphere(12);
            var cf = new ConeFlattening(sphere);
            ParameterizationResult r = cf.Compute();
            Assert.True(r.Succeeded, r.Error);
            // Gauss-Bonnet: the cones carry all the curvature
            Assert.Equal(4 * Math.PI, cf.ConeCurvature.Values.Sum(), 6);
            Assert.InRange(cf.ConeVertices.Count, 4, cf.MaxCones);
            Assert.True(cf.MaxAbsScaleFactor <= cf.MaxScaleFactor);
            foreach (int c in cf.ConeVertices) Assert.Equal(0, cf.ScaleFactors[c]);
            Assert.True(r.Distortion.MeanAreaDistortion < 0.1, "area " + r.Distortion.MeanAreaDistortion);
            Assert.True(r.Distortion.MeanConformal < 1.05);
            Assert.Equal(0, r.Distortion.FlippedTriangles);
            Assert.True(UVOverlap.Compute(cf.CutMesh, r.UV).IsInjective);
            // the seams match: around every vertex that is not a cone the UV angles of its copies sum to about
            // 2 pi, around a cone to 2 pi minus its curvature
            var angleSum = new Dictionary<int, double>();
            foreach (int t in cf.CutMesh.TriangleIndices()) {
                Index3i tri = cf.CutMesh.GetTriangle(t);
                for (int j = 0; j < 3; ++j) {
                    Vector2d e1 = r.UV[tri[(j + 1) % 3]] - r.UV[tri[j]], e2 = r.UV[tri[(j + 2) % 3]] - r.UV[tri[j]];
                    int iv = cf.Cut.CutToInputVertex[tri[j]];
                    angleSum.TryGetValue(iv, out double a);
                    angleSum[iv] = a + Math.Atan2(Math.Abs(e1.Cross(e2)), e1.Dot(e2));
                }
            }
            double meanDev = 0;
            foreach (var kv in angleSum) {
                double expected = 2 * Math.PI - (cf.ConeCurvature.TryGetValue(kv.Key, out double k) ? k : 0);
                meanDev += Math.Abs(kv.Value - expected) / angleSum.Count;
            }
            // (the conformal extension does not reproduce the boundary curve exactly: 0.025 without the scale factors on the cut)
            Assert.True(meanDev < 0.012, "mean angle sum deviation " + meanDev);
            // more cones allowed: less distortion
            var fine = new ConeFlattening(sphere) { MaxScaleFactor = 0.1 };
            ParameterizationResult rf = fine.Compute();
            Assert.True(fine.ConeVertices.Count > cf.ConeVertices.Count);
            Assert.True(rf.Distortion.MeanAreaDistortion < r.Distortion.MeanAreaDistortion);
        }

        [Fact]
        public void Cones_GivenCones_OpenMesh_InvalidInput()
        {
            // given cones: the cube corners
            DMesh3 cube = Cube(4);
            var corners = cube.VertexIndices().Where(v => {
                Vector3d p = cube.GetVertex(v);
                return Math.Abs(p.x) == 1 && Math.Abs(p.y) == 1 && Math.Abs(p.z) == 1;
            }).ToList();
            var cf = new ConeFlattening(cube) { Cones = corners };
            Assert.True(cf.Compute().Succeeded);
            Assert.Equal(corners.OrderBy(v => v), cf.ConeVertices.OrderBy(v => v));
            Assert.True(cf.Result.Distortion.MaxAreaDistortion < 1e-6);

            // open mesh: u = 0 on the boundary, cones inside
            DMesh3 hemi = Hemisphere();
            var ho = new ConeFlattening(hemi) { MaxScaleFactor = 0.05 };
            ParameterizationResult hr = ho.Compute();
            Assert.True(hr.Succeeded, hr.Error);
            Assert.True(ho.ConeVertices.Count >= 1);
            Assert.All(ho.ConeVertices, v => Assert.False(hemi.IsBoundaryVertex(v)));
            Assert.True(hr.Distortion.MeanAreaDistortion < new BoundaryFirstFlattening(hemi).Compute().Distortion.MeanAreaDistortion);
            Assert.True(UVOverlap.Compute(ho.CutMesh, hr.UV).IsInjective);

            // impossible cone sets fail clearly: a closed sphere without cones, one cone of curvature 4 pi
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ConeFlattening(Sphere(6)) { Cones = new int[0] }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ConeFlattening(Sphere(6)) { MaxCones = 1 }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ConeFlattening(Sphere(6)) { Cones = new[] { 0 } }.Compute().Status);
            ParameterizationResult noCones = new ConeFlattening(Sphere(6)) { MaxCones = 0 }.Compute();
            Assert.Equal(ParameterizationStatus.InvalidConstraints, noCones.Status);
            Assert.Contains("MaxCones", noCones.Error);
            // a torus needs no cones: MaxCones = 0 flattens it without any
            var torus0 = new ConeFlattening(Torus(2, 0.7, 24, 12)) { MaxCones = 0 };
            Assert.True(torus0.Compute().Succeeded, torus0.Result.Error);
            Assert.Empty(torus0.ConeVertices);
            // a torus can be flattened without cones
            var flatTorus = new ConeFlattening(Torus()) { Cones = new int[0] };
            Assert.True(flatTorus.Compute().Succeeded, flatTorus.Result.Error);
            Assert.Empty(flatTorus.ConeVertices);

            int bv = hemi.VertexIndices().First(v => hemi.IsBoundaryVertex(v));
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ConeFlattening(hemi) { Cones = new[] { bv } }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidConstraints, new ConeFlattening(hemi) { Cones = new[] { -1 } }.Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidTopology, new ConeFlattening(Append(Sphere(4), Sphere(4))).Compute().Status);
            Assert.Equal(ParameterizationStatus.InvalidMesh, new ConeFlattening(new DMesh3()).Compute().Status);
        }


        // ---- pipeline ----

        static void AssertAtlas(DMesh3 input, UVAtlasGenerator atlas, double maxArea, double maxConformal)
        {
            DMesh3 m = atlas.AtlasMesh;
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            AssertSameTriangles(input, m, atlas.AtlasToInputVertex, atlas.AtlasToInputTriangle);
            Assert.True(MeshDiscCut.IsDiscs(m, out string why), why);
            Assert.Equal(atlas.ChartCount, ComponentCount(m));
            // no overlaps: exact test of the final UVs, and the UVs in the unit square
            UVOverlap ov = UVOverlap.Compute(m, atlas.UV);
            Assert.Equal(0, ov.OverlappingPairs);
            Assert.Equal(0, ov.InvertedTriangles);
            Assert.Equal(0, atlas.Overlap.OverlappingPairs);
            Assert.Equal(0, atlas.Distortion.FlippedTriangles);
            Assert.Equal(0, atlas.CollapsedTriangleCount);
            // the float UVs as stored: the reported test agrees with a test of the mesh's UVs
            UVOverlap stored = UVOverlap.Compute(m);
            Assert.Equal(atlas.StoredOverlap.OverlappingPairs, stored.OverlappingPairs);
            Assert.Equal(atlas.StoredOverlap.InvertedTriangles, stored.InvertedTriangles);
            foreach (int v in m.VertexIndices()) {
                Assert.InRange(atlas.UV[v].x, 0, 1);
                Assert.InRange(atlas.UV[v].y, 0, 1);
                Assert.Equal((Vector2f)atlas.UV[v], m.GetVertexUV(v));
            }
            // distortion below the thresholds, overall and per chart
            Assert.True(atlas.Distortion.MeanAreaDistortion < maxArea, "mean area distortion " + atlas.Distortion.MeanAreaDistortion);
            Assert.True(atlas.Distortion.MeanConformal < maxConformal, "mean conformal distortion " + atlas.Distortion.MeanConformal);
            foreach (var c in atlas.Charts) {
                Assert.True(c.WithinLimits);
                Assert.True(c.Distortion.MeanAreaDistortion <= atlas.MaxAreaDistortion);
                Assert.True(c.Distortion.MeanConformal <= atlas.MaxConformalDistortion);
            }
            Assert.True(atlas.Coverage > 0.2, "coverage " + atlas.Coverage);
            // uniform texel density: UV area / surface area is the same for every chart
            double[] uvA = new double[atlas.ChartCount], a3 = new double[atlas.ChartCount];
            foreach (int t in m.TriangleIndices()) {
                Index3i tri = m.GetTriangle(t);
                uvA[atlas.TriangleChart[t]] += 0.5 * (atlas.UV[tri.b] - atlas.UV[tri.a]).Cross(atlas.UV[tri.c] - atlas.UV[tri.a]);
                a3[atlas.TriangleChart[t]] += m.GetTriArea(t);
            }
            for (int c = 1; c < atlas.ChartCount; ++c)
                Assert.Equal(1, (uvA[c] / a3[c]) / (uvA[0] / a3[0]), 6);
        }

        [Fact]
        public void Atlas_ClosedSurfaces_NoOverlap_BoundedDistortion()
        {
            var cases = new (string name, DMesh3 mesh)[] { ("sphere", Sphere()), ("torus", Torus()), ("genus2", GenusTwo()), ("cube", Cube(6)) };
            foreach (var c in cases)
                foreach (var method in new[] { UVAtlasGenerator.Methods.Auto, UVAtlasGenerator.Methods.Charts }) {
                    var atlas = new UVAtlasGenerator(c.mesh) { Method = method };
                    Assert.True(atlas.Compute(), $"{c.name} {method}: {atlas.Status} {atlas.Error}");
                    Assert.Equal(UVAtlasStatus.Success, atlas.Status);
                    AssertAtlas(c.mesh, atlas, 0.15, 1.1);
                    Assert.True(atlas.SeamLength > 0);
                    // the marching cubes mesh has vertices 1e-7 apart, which float UVs can not separate: reported,
                    // not failed; the regular meshes survive the rounding exactly
                    if (c.name == "genus2")
                        Assert.True(atlas.FloatRoundingDefects > 0);
                    else
                        Assert.True(atlas.FloatRoundingDefects == 0 && atlas.StoredOverlap.OverlappingPairs == 0 && atlas.StoredOverlap.InvertedTriangles == 0);
                }
        }

        [Fact]
        public void Atlas_Auto_UsesConesWhereTheyWork()
        {
            DMesh3 cube = Cube(6);
            var atlas = new UVAtlasGenerator(cube);
            Assert.True(atlas.Compute(), atlas.Error);
            Assert.Equal(1, atlas.ComponentCount);
            Assert.Equal(1, atlas.ConeComponents);
            Assert.Equal(8, atlas.ConeCount);
            Assert.Equal(1, atlas.ChartCount);
            Assert.Equal("cones", atlas.Charts[0].Method);
            Assert.True(atlas.Distortion.MaxAreaDistortion < 1e-6);
            Assert.Equal(14, atlas.SeamLength, 6);
            // on a torus the cone flattening overlaps itself: the component falls back to charts
            var torusCf = new ConeFlattening(Torus());
            ParameterizationResult tr = torusCf.Compute();
            Assert.True(tr.Succeeded, tr.Error);
            Assert.True(UVOverlap.Compute(torusCf.CutMesh, tr.UV).OverlappingPairs > 0);
            var torus = new UVAtlasGenerator(Torus()) { Method = UVAtlasGenerator.Methods.Cones };
            Assert.True(torus.Compute(), torus.Error);
            Assert.Equal(0, torus.ConeComponents);
            Assert.True(torus.ChartCount > 1);
            Assert.Equal(0, torus.Overlap.OverlappingPairs);
            // the sphere too
            var sphere = new UVAtlasGenerator(Sphere()) { Method = UVAtlasGenerator.Methods.Cones };
            Assert.True(sphere.Compute(), sphere.Error);
            Assert.Equal(1, sphere.ChartCount);
            Assert.True(sphere.ConeCount >= 4);
        }

        [Fact]
        public void Atlas_OpenMeshes_SeveralComponents()
        {
            DMesh3 mesh = Append(Hemisphere(), OpenCylinder(), Torus(1.5, 0.5, 32, 12), Cube(3));
            var atlas = new UVAtlasGenerator(mesh);
            Assert.True(atlas.Compute(), atlas.Error);
            AssertAtlas(mesh, atlas, 0.15, 1.1);
            Assert.True(atlas.ChartCount >= 4);
            Assert.Equal(4, atlas.ComponentCount);
            Assert.True(atlas.ConeComponents <= atlas.ComponentCount);
            // charts do not span components
            var compOf = new Dictionary<int, int>();
            var cc = new MeshConnectedComponents(mesh);
            cc.FindConnectedT();
            for (int i = 0; i < cc.Count; ++i) foreach (int t in cc.Components[i].Indices) compOf[t] = i;
            foreach (var g in atlas.AtlasMesh.TriangleIndices().GroupBy(t => atlas.TriangleChart[t]))
                Assert.Single(g.Select(t => compOf[atlas.AtlasToInputTriangle[t]]).Distinct());
        }

        [Fact]
        public void Atlas_ComponentCount_SplitsBowties()
        {
            // two triangle fans that share only their center vertex: one component of the input by vertices, two
            // after the bowtie vertex is split
            DMesh3 mesh = new DMesh3();
            int c = mesh.AppendVertex(Vector3d.Zero);
            for (int side = 0; side < 2; ++side) {
                int prev = -1;
                for (int k = 0; k < 4; ++k) {
                    double a = (side == 0 ? 0.2 : Math.PI + 0.2) + 0.5 * k;
                    int v = mesh.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0.1 * k));
                    if (prev >= 0) Assert.True(mesh.AppendTriangle(c, prev, v) >= 0);
                    prev = v;
                }
            }
            var atlas = new UVAtlasGenerator(mesh);
            atlas.Compute();
            Assert.NotEqual(UVAtlasStatus.InvalidMesh, atlas.Status);
            Assert.Equal(2, atlas.ComponentCount);
            Assert.True(atlas.ChartCount >= 2);
        }

        [Fact]
        public void Atlas_StatusCodes()
        {
            // a single chart of a sphere can not meet the limits: reported, but still overlap-free
            DMesh3 sphere = Sphere();
            var single = new UVAtlasGenerator(sphere) { Method = UVAtlasGenerator.Methods.SingleChart };
            Assert.False(single.Compute());
            Assert.Equal(UVAtlasStatus.DistortionLimitExceeded, single.Status);
            Assert.Equal(1, single.ChartCount);
            Assert.Equal(1, single.ChartsOverLimit);
            Assert.NotNull(single.AtlasMesh);
            Assert.Equal(0, single.Overlap.OverlappingPairs);
            Assert.True(single.Distortion.MeanAreaDistortion > 0.25);

            // too few charts allowed
            var few = new UVAtlasGenerator(GenusTwo()) { Method = UVAtlasGenerator.Methods.Charts, MaxCharts = 3 };
            Assert.False(few.Compute());
            Assert.Equal(UVAtlasStatus.DistortionLimitExceeded, few.Status);
            Assert.True(few.ChartCount <= 3);
            Assert.True(few.ChartsOverLimit > 0);
            Assert.Equal(0, few.Overlap.OverlappingPairs);
            Assert.Equal(0, few.Overlap.InvertedTriangles);
            Assert.True(MeshDiscCut.IsDiscs(few.AtlasMesh, out string why), why);

            // invalid input
            Assert.Equal(UVAtlasStatus.InvalidMesh, run(new UVAtlasGenerator(new DMesh3())));
            DMesh3 nan = Sphere(4);
            nan.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            Assert.Equal(UVAtlasStatus.InvalidMesh, run(new UVAtlasGenerator(nan)));
        }

        static UVAtlasStatus run(UVAtlasGenerator g) { g.Compute(); return g.Status; }

        [Fact]
        public void Atlas_StrayDegenerateTriangle_DoesNotFailTheAtlas()
        {
            // a separate collinear triangle: nothing to flatten, its chart is a point
            DMesh3 mesh = Sphere(6);
            int a = mesh.AppendVertex(new Vector3d(3, 0, 0)), b = mesh.AppendVertex(new Vector3d(4, 0, 0)), c = mesh.AppendVertex(new Vector3d(5, 0, 0));
            mesh.AppendTriangle(a, b, c);
            foreach (var method in new[] { UVAtlasGenerator.Methods.Auto, UVAtlasGenerator.Methods.Charts, UVAtlasGenerator.Methods.Cones, UVAtlasGenerator.Methods.SingleChart }) {
                var atlas = new UVAtlasGenerator(mesh) { Method = method };
                atlas.Compute();
                Assert.NotEqual(UVAtlasStatus.Failed, atlas.Status);
                Assert.Contains(atlas.Charts, ch => ch.Method == "point");
                Assert.Equal(0, atlas.Overlap.OverlappingPairs);
                Assert.Equal(0, atlas.CollapsedTriangleCount);
            }
        }

        [Fact]
        public void Atlas_LongDegenerateTriangle_DoesNotShrinkTheAtlas()
        {
            DMesh3 cube = Cube(3);
            var plain = new UVAtlasGenerator(cube);
            Assert.True(plain.Compute(), plain.Error);
            DMesh3 mesh = Cube(3);
            int a = mesh.AppendVertex(new Vector3d(2, 0, 0)), b = mesh.AppendVertex(new Vector3d(500, 0, 0)), c = mesh.AppendVertex(new Vector3d(1000, 0, 0));
            mesh.AppendTriangle(a, b, c);
            var atlas = new UVAtlasGenerator(mesh);
            Assert.True(atlas.Compute(), atlas.Error);
            Assert.True(atlas.Coverage > 0.8 * plain.Coverage, $"coverage {atlas.Coverage} vs {plain.Coverage}");
        }

        [Fact]
        public void Atlas_ManyCaps_ChartsAreNotSeededAtCaps()
        {
            // caps (zero-area triangles from poking an edge point) all over a sphere
            for (int seed = 0; seed < 6; ++seed) {
                DMesh3 mesh = Sphere(12);
                var rnd = new Random(seed);
                foreach (int tid in mesh.TriangleIndices().Where(t => t % 4 == 0).ToList()) {
                    double t = 0.2 + 0.6 * rnd.NextDouble();
                    DMesh3.PokeTriangleInfo info;
                    mesh.PokeTriangle(tid, new Vector3d(t, 1 - t, 0), out info);
                }
                foreach (var method in new[] { UVAtlasGenerator.Methods.Auto, UVAtlasGenerator.Methods.Charts }) {
                    var atlas = new UVAtlasGenerator(mesh) { Method = method };
                    Assert.True(atlas.Compute(), $"seed {seed} {method}: {atlas.Status} {atlas.Error}");
                }
            }
            // a flat square with one cap, split as far as the limit asks
            var sq = new DMesh3();
            sq.AppendVertex(new Vector3d(0, 0, 0)); sq.AppendVertex(new Vector3d(1, 0, 0));
            sq.AppendVertex(new Vector3d(1, 1, 0)); sq.AppendVertex(new Vector3d(0, 1, 0));
            sq.AppendTriangle(0, 1, 2); sq.AppendTriangle(0, 2, 3);
            DMesh3.PokeTriangleInfo pinfo;
            sq.PokeTriangle(0, new Vector3d(0.5, 0.5, 0), out pinfo);
            var tiny = new UVAtlasGenerator(sq) { Method = UVAtlasGenerator.Methods.Charts, MaxAreaDistortion = 1e-9 };
            tiny.Compute();
            Assert.NotEqual(UVAtlasStatus.Failed, tiny.Status);
        }

        [Fact]
        public void Atlas_NoPacking_ReportsAtlasTriangleIds()
        {
            // a cube and a separate collinear triangle (its chart is a point), not packed
            DMesh3 mesh = Cube(3);
            int a = mesh.AppendVertex(new Vector3d(3, 0, 0)), b = mesh.AppendVertex(new Vector3d(4, 0, 0)), c = mesh.AppendVertex(new Vector3d(5, 0, 0));
            int stray = mesh.AppendTriangle(a, b, c);
            var atlas = new UVAtlasGenerator(mesh) { Method = UVAtlasGenerator.Methods.Charts, Pack = false };
            atlas.Compute();
            Assert.NotEqual(UVAtlasStatus.Failed, atlas.Status);
            // the degenerate triangle is reported with its AtlasMesh triangle id
            Assert.True(atlas.Overlap.DegenerateTriangles > 0);
            Assert.Equal(atlas.Overlap.DegenerateTriangles, atlas.Overlap.DegenerateTriangleIds.Count);
            foreach (int t in atlas.Overlap.DegenerateTriangleIds) {
                Assert.True(atlas.AtlasMesh.GetTriArea(t) < 1e-12);
                Assert.Equal(stray, atlas.AtlasToInputTriangle[t]);
            }
            Assert.Equal(atlas.StoredOverlap.DegenerateTriangles, atlas.StoredOverlap.DegenerateTriangleIds.Count);
        }

        [Fact]
        public void Atlas_DegenerateSurfaceTriangles()
        {
            DMesh3 caps = CubeWithCaps();
            foreach (var method in new[] { UVAtlasGenerator.Methods.Auto, UVAtlasGenerator.Methods.Charts }) {
                var atlas = new UVAtlasGenerator(caps) { Method = method };
                Assert.True(atlas.Compute(), method + ": " + atlas.Error);
                Assert.Equal(0, atlas.CollapsedTriangleCount);
                AssertAtlas(caps, atlas, 0.15, 1.1);
            }
        }

        [Fact]
        public void Atlas_KeepsVertexAttributesAndGroups()
        {
            DMesh3 mesh = Torus(2, 0.7, 24, 10);
            mesh.EnableVertexNormals(Vector3f.AxisZ);
            mesh.EnableTriangleGroups();
            foreach (int v in mesh.VertexIndices()) mesh.SetVertexNormal(v, (Vector3f)mesh.GetVertex(v).Normalized);
            foreach (int t in mesh.TriangleIndices()) mesh.SetTriangleGroup(t, t % 5);
            var atlas = new UVAtlasGenerator(mesh);
            Assert.True(atlas.Compute(), atlas.Error);
            DMesh3 m = atlas.AtlasMesh;
            Assert.True(m.HasVertexNormals && m.HasTriangleGroups && m.HasVertexUVs);
            foreach (int v in m.VertexIndices())
                Assert.Equal(mesh.GetVertexNormal(atlas.AtlasToInputVertex[v]), m.GetVertexNormal(v));
            foreach (int t in m.TriangleIndices())
                Assert.Equal(mesh.GetTriangleGroup(atlas.AtlasToInputTriangle[t]), m.GetTriangleGroup(t));
        }
    }
}
