using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Hard cases for mesh booleans from the issue trackers of other libraries (docs/QA.md 2.11): only the failure
    /// mode, rebuilt here from simple primitives. No code, test code or meshes of these projects are used (CGAL and the
    /// copyleft parts of libigl are GPL: only the prose of their issues was read). The issue URL is in the comment of
    /// each test.
    /// </summary>
    public class ExactBooleanIssueCasesTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var box = new Box3d(new Vector3d((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), new Vector3d((x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2));
            DMesh3 mesh = new TrivialBox3Generator() { Box = box }.Generate().MakeDMesh();
            foreach (int vid in mesh.VertexIndices().ToArray()) {
                Vector3d v = mesh.GetVertex(vid);
                mesh.SetVertex(vid, new Vector3d(v.x < box.Center.x ? x0 : x1, v.y < box.Center.y ? y0 : y1, v.z < box.Center.z ? z0 : z1));
            }
            return mesh;
        }

        // a closed prism between two rings of points (each convex and planar, the same count, bottom counter-clockwise
        // seen from the top), caps as fans
        static DMesh3 Prism(IList<Vector3d> bottom, IList<Vector3d> top)
        {
            var m = new DMesh3();
            int n = bottom.Count;
            int[] b = bottom.Select(p => m.AppendVertex(p)).ToArray(), t = top.Select(p => m.AppendVertex(p)).ToArray();
            for (int i = 0; i < n; ++i) {
                int j = (i + 1) % n;
                m.AppendTriangle(b[i], b[j], t[j]);
                m.AppendTriangle(b[i], t[j], t[i]);
            }
            for (int i = 1; i + 1 < n; ++i) {
                m.AppendTriangle(b[0], b[i + 1], b[i]);
                m.AppendTriangle(t[0], t[i], t[i + 1]);
            }
            if (ResultChecks.Volume(m) < 0)
                m.ReverseOrientation();
            return m;
        }

        static DMesh3 Tetra(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            var m = new DMesh3();
            foreach (Vector3d v in new[] { a, b, c, d }) m.AppendVertex(v);
            m.AppendTriangle(0, 2, 1); m.AppendTriangle(0, 1, 3); m.AppendTriangle(1, 2, 3); m.AppendTriangle(0, 3, 2);
            if (ResultChecks.Volume(m) < 0)
                m.ReverseOrientation();
            return m;
        }

        static DMesh3 Sphere(Vector3d c, double r, int n = 8)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op, bool safe = true)
        {
            var boolean = new ExactMeshBoolean(a, b, op) { SafeRounding = safe };
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean;
        }

        static void AssertCleanResult(ExactMeshBoolean b, double volume, double tol, string what)
        {
            ResultChecks.AssertClean(b.Result, what);
            Assert.True(b.RoundingClean, what + ": rounding not clean");
            Assert.True(Math.Abs(ResultChecks.Volume(b.Result) - volume) <= tol * Math.Max(1, Math.Abs(volume)),
                $"{what}: volume {ResultChecks.Volume(b.Result)}, expected {volume}");
        }

        static int Components(DMesh3 m)
        {
            var c = new MeshConnectedComponents(m);
            c.FindConnectedT();
            return c.Count;
        }

        [Fact]
        public void TetrahedraSharingAFace_RandomOrientations()
        {
            // https://github.com/elalish/manifold/issues/1656: two tetrahedra on the same base triangle, apexes on both
            // sides (one exactly coincident face, opposite orientations); in a few percent of random orientations the
            // union kept both copies of the shared face (two touching shells instead of one)
            var rand = new Random(1656);
            for (int it = 0; it < 60; ++it) {
                var rot = new Quaterniond(new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5).Normalized, 360 * rand.NextDouble());
                Vector3d a = rot * new Vector3d(1, 0, 0), b = rot * new Vector3d(-0.5, 0.8, 0), c = rot * new Vector3d(-0.4, -0.9, 0.1);
                Vector3d n = (b - a).Cross(c - a).Normalized;
                Vector3d g = (a + b + c) / 3;
                DMesh3 t1 = Tetra(a, b, c, g + 0.7 * n), t2 = Tetra(a, b, c, g - 0.9 * n);
                var u = Run(t1, t2, Ops.Union);
                string what = "case " + it;
                AssertCleanResult(u, ResultChecks.Volume(t1) + ResultChecks.Volume(t2), 1e-12, what);
                Assert.Equal(6, u.Result.TriangleCount);
                Assert.Equal(5, u.Result.VertexCount);
                var i = Run(t1, t2, Ops.Intersection);
                Assert.Equal(0, i.Result.TriangleCount);
            }
        }

        static (DMesh3, DMesh3) TrianglePrisms(Func<Vector3d, Vector3d> R)
        {
            DMesh3 p1 = Prism(new[] { new Vector3d(0, 0, 0), new Vector3d(10, 0, 0), new Vector3d(0, 10, 0) }.Select(R).ToList(),
                new[] { new Vector3d(0, 0, 3), new Vector3d(10, 0, 3), new Vector3d(0, 10, 3) }.Select(R).ToList());
            DMesh3 p2 = Prism(new[] { new Vector3d(10, 0, 0), new Vector3d(10, 10, 0), new Vector3d(0, 10, 0) }.Select(R).ToList(),
                new[] { new Vector3d(10, 0, 3), new Vector3d(10, 10, 3), new Vector3d(0, 10, 3) }.Select(R).ToList());
            return (p1, p2);
        }

        [Fact]
        public void TwoTriangularPrisms_FormABox_RotatedAboutX()
        {
            // https://github.com/elalish/manifold/issues/1430 (case c): two right triangles that form a 10 x 10 square,
            // extruded and rotated by 90 degrees about x, united: only part of the shared hypotenuse face merged and
            // a sliver was left. The two prisms triangulate the shared face with different diagonals. Rotated exactly
            // (90 degrees as a permutation of the coordinates, as Manifold does) the face stays planar
            var cases = new List<(string, Func<Vector3d, Vector3d>)> {
                ("unrotated", p => p),
                ("90 degrees about x, exact", p => new Vector3d(p.x, -p.z, p.y)),
                ("180 degrees about x, exact", p => new Vector3d(p.x, -p.y, -p.z)),
            };
            foreach (var (what, R) in cases) {
                var (p1, p2) = TrianglePrisms(R);
                var u = Run(p1, p2, Ops.Union);
                AssertCleanResult(u, 300, 1e-12, what);
                Assert.Equal(1, Components(u.Result));
                // the shared face is gone completely: every vertex is a corner of the box
                Assert.Equal(8, u.Result.VertexCount);
            }
        }

        [Fact]
        public void KnownLimitation_TwoTriangularPrisms_RotatedInexactly_KeepATinyVoid()
        {
            // Known limitation (pinned: a fix turns this test red, then it moves to the clean cases above), the same case
            // rotated by 90 degrees with a quaternion: the rotated corners are rounded to double (a coordinate of 6.7e-16
            // instead of 0), so the shared face is no longer planar and its two triangulations differ by a tetrahedron of
            // about 3e-15 volume between them, outside both prisms.
            // ExactMeshBoolean computes this input exactly: the union is the box with that tetrahedral void, which
            // touches the outer surface along two edges (a contact at duplicated vertices, as where solids touch,
            // see ExactBooleanByDesignTests). The volume is right; SafeRounding finds nothing to repair (contacts at
            // duplicated vertices are not intersections) and keeps the void, because it removes thin closed components
            // only where the rounding left a defect. Welded by position, the result is non-manifold at the edges.
            // A fix would remove such thin closed components that touch the rest only at duplicated vertices.
            // Pin the rounded input, not runtime-dependent Sin/Cos results: net48 and net10
            // produce different coordinates for the same quaternion and therefore different exact solids.
            var (p1, p2) = TrianglePrisms(p => new Vector3d(p.x,
                p.z == 0 ? (p.y == 0 ? 0 : 2.2204460492503131e-15)
                    : (p.y == 0 ? -3.0 : -2.9999999999999978),
                p.y == 0 ? (p.z == 0 ? 0 : 6.6613381477509392e-16) : 10.0));
            var u = Run(p1, p2, Ops.Union);
            ResultChecks.AssertClosed(u.Result);
            Assert.True(u.RoundingClean);
            Assert.Equal(300, ResultChecks.Volume(u.Result), 9);
            Assert.Equal(2, Components(u.Result));
            Assert.Equal(16, u.Result.TriangleCount);
            Assert.True(u.NonManifoldEdges > 0);
        }

        [Fact]
        public void TwoTriangularPrisms_RoundedRotationWithoutVoid_FormOneClosedSolid()
        {
            // The other observed runtime rounding lies on the opposite side of the shared face.
            var (p1, p2) = TrianglePrisms(p => new Vector3d(p.x,
                p.z == 0 ? (p.y == 0 ? 0 : -2.220446049250313e-15)
                    : (p.y == 0 ? -3.000000000000001 : -3.000000000000003),
                p.y == 0 ? (p.z == 0 ? 0 : -6.661338147750939e-16) : 10.000000000000002));
            var u = Run(p1, p2, Ops.Union);
            AssertCleanResult(u, 300, 1e-12, "pinned rounded rotation without a void");
            Assert.Equal(1, Components(u.Result));
        }

        [Fact]
        public void BoxMinusBarsFlushWithItsFaces_NoShards()
        {
            // https://github.com/elalish/manifold/issues/1430 (case a): a block minus two bars placed exactly flush
            // with its top and bottom faces; the bottom one left a shard
            DMesh3 block = Prism(Enumerable.Range(0, 8).Select(k => new Vector3d(4 * Math.Cos(k * Math.PI / 4), 4 * Math.Sin(k * Math.PI / 4), 0)).ToList(),
                Enumerable.Range(0, 8).Select(k => new Vector3d(4 * Math.Cos(k * Math.PI / 4), 4 * Math.Sin(k * Math.PI / 4), 5)).ToList());
            double v0 = ResultChecks.Volume(block);
            var d1 = Run(block, Box(-0.5, -1.5, 4, 0.5, 1.5, 5), Ops.Difference);
            var d2 = Run(d1.Result, Box(-0.5, -1.5, 0, 0.5, 1.5, 1), Ops.Difference);
            AssertCleanResult(d2, v0 - 6, 1e-12, "two flush bars");
            Assert.Equal(1, Components(d2.Result));
            // the same with the bars rotated 90 degrees about y (the shard moved with the rotation)
            var rot = Quaterniond.AxisAngleD(Vector3d.AxisY, 90);
            DMesh3 rb = new DMesh3(block);
            MeshTransforms.Rotate(rb, Vector3d.Zero, rot);
            DMesh3 bar1 = Box(-0.5, -1.5, 4, 0.5, 1.5, 5), bar2 = Box(-0.5, -1.5, 0, 0.5, 1.5, 1);
            MeshTransforms.Rotate(bar1, Vector3d.Zero, rot);
            MeshTransforms.Rotate(bar2, Vector3d.Zero, rot);
            var r2 = Run(Run(rb, bar1, Ops.Difference).Result, bar2, Ops.Difference);
            Assert.True(r2.RoundingClean);
            ResultChecks.AssertClean(r2.Result, "rotated");
            Assert.Equal(1, Components(r2.Result));
            Assert.Equal(v0 - 6, ResultChecks.Volume(r2.Result), 6);
        }

        [Fact]
        public void PrismAndSkewedPrismSharingAnEndFace()
        {
            // https://github.com/elalish/manifold/issues/1496: an octagonal prism along x, and a second, skewed prism
            // starting at the first one's end octagon; the union had duplicated vertices, interior triangles and
            // self-intersections
            Func<double, double, double, double, List<Vector3d>> ring = (x, dy, dz, turn) => Enumerable.Range(0, 8)
                .Select(k => new Vector3d(x, dy + 2 * Math.Cos(k * Math.PI / 4 + turn), dz + 2 * Math.Sin(k * Math.PI / 4 + turn))).ToList();
            DMesh3 p1 = Prism(ring(0, 0, 0, 0), ring(10, 0, 0, 0));
            DMesh3 p2 = Prism(ring(10, 0, 0, 0), ring(20, 1.3, -0.4, 0.3));
            var u = Run(p1, p2, Ops.Union);
            AssertCleanResult(u, ResultChecks.Volume(p1) + ResultChecks.Volume(p2), 1e-12, "union");
            Assert.Equal(24, u.Result.VertexCount);
            Assert.Equal(1, Components(u.Result));
        }

        [Fact]
        public void SphereMinusItsIntersectionWithAShiftedCopy()
        {
            // https://github.com/elalish/manifold/issues/1706: sphere intersected with the same sphere shifted along x,
            // then the original sphere minus that result (faces that coincide with the input): hundreds of slivers
            // below 1e-9 area
            DMesh3 s = Sphere(Vector3d.Zero, 1, 12), t = Sphere(new Vector3d(0.6, 0, 0), 1, 12);
            var lens = Run(s, t, Ops.Intersection);
            AssertCleanResult(lens, ResultChecks.Volume(Run(s, t, Ops.Intersection, false).Result), 1e-9, "lens");
            var d = Run(s, lens.Result, Ops.Difference);
            ResultChecks.AssertClean(d.Result, "difference");
            Assert.True(d.RoundingClean);
            Assert.Equal(ResultChecks.Volume(s) - ResultChecks.Volume(lens.Result), ResultChecks.Volume(d.Result), 9);
            // and the difference computed directly is the same solid
            var direct = Run(s, t, Ops.Difference);
            Assert.Equal(ResultChecks.Volume(direct.Result), ResultChecks.Volume(d.Result), 9);
        }

        [Fact]
        public void HugeCoordinates_BatchUnion_IndependentOfOrder()
        {
            // https://github.com/elalish/manifold/issues/1883: ten convex integer-coordinate pieces at coordinates up to
            // 3.6e8, united in one batch: volume 0.056 % low and stray vertices; reversing the order or uniting one at
            // a time was correct
            var rand = new Random(1883);
            var pieces = new List<DMesh3>();
            for (int k = 0; k < 10; ++k) {
                double x = 3.6e8 + rand.Next(0, 600), y = -1.2e8 + rand.Next(0, 600), z = 2e8 + rand.Next(0, 200);
                // a chamfered box: an octagonal prism with integer corners
                double w = 200 + rand.Next(0, 200), c = 40;
                var oct = new List<Vector3d> {
                    new Vector3d(x + c, y, 0), new Vector3d(x + w - c, y, 0), new Vector3d(x + w, y + c, 0), new Vector3d(x + w, y + w - c, 0),
                    new Vector3d(x + w - c, y + w, 0), new Vector3d(x + c, y + w, 0), new Vector3d(x, y + w - c, 0), new Vector3d(x, y + c, 0) };
                pieces.Add(Prism(oct.Select(p => new Vector3d(p.x, p.y, z)).ToList(), oct.Select(p => new Vector3d(p.x, p.y, z + 150 + k)).ToList()));
            }
            var batch = new ExactMeshBoolean(pieces, Ops.Union) { SafeRounding = true };
            Assert.True(batch.Compute(), batch.Error);
            var reversed = new ExactMeshBoolean(Enumerable.Reverse(pieces).ToList(), Ops.Union) { SafeRounding = true };
            Assert.True(reversed.Compute(), reversed.Error);
            DMesh3 seq = pieces[0];
            for (int k = 1; k < pieces.Count; ++k)
                seq = Run(seq, pieces[k], Ops.Union).Result;
            ResultChecks.AssertClean(batch.Result, "batch");
            ResultChecks.AssertClean(reversed.Result, "reversed");
            ResultChecks.AssertClean(seq, "sequential");
            // volumes relative to a point of the pieces (far from the origin the plain sum cancels)
            Vector3d o = pieces[0].GetVertex(0);
            Func<DMesh3, double> vol = m => m.TriangleIndices().Sum(t => {
                Index3i tv = m.GetTriangle(t);
                return (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o)) / 6;
            });
            double vb = vol(batch.Result);
            Assert.Equal(vb, vol(reversed.Result), 6);
            Assert.Equal(vb, vol(seq), 6);
            Assert.True(batch.IntersectingPairs > 0);
            // every result vertex lies on the boundary of a piece (each a convex prism: the largest signed distance to
            // its face planes is 0), relative to o so that the coordinates are small: no stray vertices
            Func<Vector3d, bool> onSomePiece = v => pieces.Any(p => {
                double worst = double.MinValue;
                foreach (int t in p.TriangleIndices()) {
                    Index3i tv = p.GetTriangle(t);
                    Vector3d a = p.GetVertex(tv.a) - o, n = (p.GetVertex(tv.b) - p.GetVertex(tv.a)).Cross(p.GetVertex(tv.c) - p.GetVertex(tv.a)).Normalized;
                    worst = Math.Max(worst, n.Dot(v - o - a));
                }
                return Math.Abs(worst) < 1e-6;
            });
            foreach (DMesh3 m in new[] { batch.Result, reversed.Result, seq })
                foreach (Vector3d v in m.Vertices())
                    Assert.True(onSomePiece(v), "stray vertex " + v);
        }

        [Fact]
        public void SameBoxTriangulatedTwoWays_UnionMinusABox()
        {
            // https://github.com/elalish/manifold/issues/1857: (a + b) - c where a and b are the same box with different
            // diagonals on its faces and c shares half of its vertices with them: slivers made an edge-swap loop (stack
            // overflow, then an endless loop)
            DMesh3 a = Box(0, 0, 0, 2, 2, 2);
            DMesh3 b = new DMesh3(a);
            // flip every diagonal of b
            foreach (int e in b.EdgeIndices().ToArray()) {
                Index2i ev = b.GetEdgeV(e);
                Vector3d d = b.GetVertex(ev.a) - b.GetVertex(ev.b);
                int nonzero = (d.x != 0 ? 1 : 0) + (d.y != 0 ? 1 : 0) + (d.z != 0 ? 1 : 0);
                if (nonzero == 2 && b.IsEdge(e))
                    Assert.Equal(MeshResult.Ok, b.FlipEdge(e, out DMesh3.EdgeFlipInfo _));
            }
            var ab = Run(a, b, Ops.Union);
            AssertCleanResult(ab, 8, 1e-12, "a + b");
            var abc = Run(ab.Result, Box(1, 0, 0, 2, 2, 2), Ops.Difference);
            AssertCleanResult(abc, 4, 1e-12, "(a + b) - c");
            Assert.Equal(8, abc.Result.VertexCount);
        }

        [Fact]
        public void RotateAndUnionSpiral_ManyLevels()
        {
            // https://github.com/elalish/manifold/issues/634: a spiral, each level the union of a unit cube at radius
            // r with the deeper result rotated about z; the precision estimate grew about 7 % per rotation, and beyond
            // 117 levels all triangles vanished. Exact arithmetic has no tolerance to grow. Here consecutive cubes
            // overlap (1.5 degrees at radius 25 to 33 is 0.65 to 0.86 apart): 130 levels stay clean and give the same solid as one
            // n-ary union of the cubes at their final places
            DMesh3 result = new DMesh3();
            var cubes = new List<DMesh3>();
            int levels = 130;
            double r = 25;
            for (int k = 0; k < levels; ++k) {
                var turn = Quaterniond.AxisAngleD(Vector3d.AxisZ, 1.5);
                MeshTransforms.Rotate(result, Vector3d.Zero, turn);
                foreach (DMesh3 c in cubes)
                    MeshTransforms.Rotate(c, Vector3d.Zero, turn);
                r *= 1.002;
                DMesh3 cube = Box(r, -0.5, -0.5 + 0.01 * k, r + 1, 0.5, 0.5 + 0.01 * k);
                cubes.Add(new DMesh3(cube));
                var u = Run(result, cube, Ops.Union);
                if (k > 0)
                    Assert.True(u.IntersectingPairs > 0, "level " + k + ": no overlap");
                result = u.Result;
            }
            ResultChecks.AssertClean(result, "spiral");
            Assert.Equal(1, Components(result));
            var onePass = new ExactMeshBoolean(cubes, Ops.Union) { SafeRounding = true };
            Assert.True(onePass.Compute(), onePass.Error);
            ResultChecks.AssertClean(onePass.Result, "one pass");
            double v = ResultChecks.Volume(result);
            Assert.True(v > 0.5 * levels && v < levels, "volume " + v);
            Assert.Equal(ResultChecks.Volume(onePass.Result), v, 6);
        }

        [Fact]
        public void TetrahedraRotatedByTinyAngles_Union()
        {
            // https://github.com/elalish/manifold/issues/970 (cases a, b): three tetrahedra, two rotated by tiny angles,
            // united (the triangulator refused a valid nearly degenerate polygon, nondeterministically), and a cube
            // rotated by tiny angles with other cubes (self-intersections)
            DMesh3 t0 = Tetra(new Vector3d(0, 0, 0), new Vector3d(2, 0, 0), new Vector3d(0, 2, 0), new Vector3d(0, 0, 2));
            DMesh3 t1 = new DMesh3(t0), t2 = new DMesh3(t0);
            MeshTransforms.Rotate(t1, new Vector3d(0.3, 0.3, 0.3), Quaterniond.AxisAngleD(new Vector3d(1, 2, 3).Normalized, 1e-7));
            MeshTransforms.Rotate(t2, new Vector3d(0.3, 0.3, 0.3), Quaterniond.AxisAngleD(new Vector3d(-2, 1, 1).Normalized, 3e-9));
            string first = null;
            for (int run = 0; run < 3; ++run) {
                var u = new ExactMeshBoolean(new[] { t0, t1, t2 }, Ops.Union) { SafeRounding = true };
                Assert.True(u.Compute(), u.Error);
                ResultChecks.AssertClean(u.Result, "tetrahedra");
                Assert.Equal(ResultChecks.Volume(t0), ResultChecks.Volume(u.Result), 6);
                string print = string.Join(";", u.Result.Vertices().Select(v => v.ToString("R")));
                first = first ?? print;
                Assert.Equal(first, print);
            }
            DMesh3 c0 = Box(0, 0, 0, 1, 1, 1), c1 = Box(0, 0, 0, 1, 1, 1), c2 = Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5);
            MeshTransforms.Rotate(c1, new Vector3d(0.5, 0.5, 0.5), Quaterniond.AxisAngleD(new Vector3d(1, 1, 0).Normalized, 1e-8));
            var cu = Run(Run(c0, c1, Ops.Union).Result, c2, Ops.Union);
            ResultChecks.AssertClean(cu.Result, "cubes");
            Assert.True(cu.RoundingClean);
            var cd = Run(Run(c0, c1, Ops.Union).Result, c2, Ops.Difference);
            ResultChecks.AssertClean(cd.Result, "cubes, difference");
            Assert.Equal(1 - 0.125, ResultChecks.Volume(cd.Result), 6);
        }

        [Fact]
        public void MirroredCubes_Difference()
        {
            // https://github.com/elalish/manifold/issues/294: a cube scaled by (1, -1, 1) minus a mirrored small cube;
            // the winding was not flipped with the mirror. Here the mirror is MeshTransforms.Scale with bFixOrientation
            // (which reverses the triangles); without it the mirrored mesh is inside out (winding number -1, an empty
            // solid)
            DMesh3 big = Box(0, 0, 0, 1, 1, 1), small = Box(0.45, 0.45, 0.45, 0.55, 0.55, 1.5);
            MeshTransforms.Scale(big, new Vector3d(1, -1, 1), Vector3d.Zero, true);
            MeshTransforms.Scale(small, new Vector3d(1, -1, 1), Vector3d.Zero, true);
            var d = Run(big, small, Ops.Difference);
            AssertCleanResult(d, 1 - 0.01 * 0.55, 1e-12, "mirrored, fixed orientation");
            // without the orientation fix both are inside out: their solids are empty, so is the difference
            DMesh3 big2 = Box(0, 0, 0, 1, 1, 1), small2 = Box(0.45, 0.45, 0.45, 0.55, 0.55, 1.5);
            MeshTransforms.Scale(big2, new Vector3d(1, -1, 1), Vector3d.Zero, false);
            MeshTransforms.Scale(small2, new Vector3d(1, -1, 1), Vector3d.Zero, false);
            Assert.True(ResultChecks.Volume(big2) < 0);
            var d2 = Run(big2, small2, Ops.Difference);
            Assert.Equal(0, d2.Result.TriangleCount);
        }

        [Fact]
        public void CubeMinusCenteredCube_ShellWithACavity()
        {
            // https://github.com/elalish/manifold/issues/745: a 3-cube minus a centred 1-cube is a shell with a cavity;
            // the cavity is a component of its own with negative volume
            var d = Run(Box(0, 0, 0, 3, 3, 3), Box(1, 1, 1, 2, 2, 2), Ops.Difference);
            AssertCleanResult(d, 26, 1e-12, "shell");
            Assert.Equal(2, Components(d.Result));
            // the inner component is the cavity: inside-out on its own
            var comps = new MeshConnectedComponents(d.Result);
            comps.FindConnectedT();
            var vols = comps.Components.Select(c => c.Indices.Sum(t => {
                Index3i tv = d.Result.GetTriangle(t);
                return d.Result.GetVertex(tv.a).Dot(d.Result.GetVertex(tv.b).Cross(d.Result.GetVertex(tv.c))) / 6;
            })).OrderBy(v => v).ToArray();
            Assert.Equal(-1, vols[0], 12);
            Assert.Equal(27, vols[1], 12);
            // and the shell united with the centred cube is the full cube again
            AssertCleanResult(Run(d.Result, Box(1, 1, 1, 2, 2, 2), Ops.Union), 27, 1e-12, "filled");
        }

        [Fact]
        public void CylinderToolSteppedDown_RepeatedDifference()
        {
            // https://github.com/CGAL/cgal/issues/6481 (prose only): a cylinder tool moved down one unit per step and
            // subtracted from the workpiece again and again; the third step failed on collinear points and the cost
            // grew with each step, which re-cut all earlier layers. https://github.com/CGAL/cgal/issues/2875 and
            // https://github.com/libigl/libigl/issues/1116: results rounded to double between iterated booleans
            // self-intersect. Here every step is rounded (SafeRounding) and used as the input of the next one
            DMesh3 work = Box(-3, -3, -10, 3, 3, 0);
            DMesh3 tool = new CappedCylinderGenerator() { BaseRadius = 1.3f, TopRadius = 1.3f, Height = 2.5f, Slices = 24 }.Generate().MakeDMesh();
            // (the generator's axis is y, from 0 to the height): along z, from -1.5 to 1
            MeshTransforms.Rotate(tool, Vector3d.Zero, Quaterniond.AxisAngleD(Vector3d.AxisX, 90));
            MeshTransforms.Translate(tool, new Vector3d(0.17, -0.11, -1.5));
            var counts = new List<int>();
            for (int step = 0; step < 6; ++step) {
                var d = Run(work, tool, Ops.Difference);
                ResultChecks.AssertClean(d.Result, "step " + step);
                Assert.True(d.RoundingClean, "step " + step);
                work = d.Result;
                counts.Add(work.TriangleCount);
                MeshTransforms.Translate(tool, new Vector3d(0, 0, -1));
            }
            // the walls of the hole are re-cut at each step, but its triangles do not multiply: the count grows by a
            // bounded amount per step
            for (int k = 2; k < counts.Count; ++k)
                Assert.True(counts[k] - counts[k - 1] <= 2 * (counts[1] - counts[0]) + 200, string.Join(", ", counts));
            Assert.Equal(1, Components(work));
            // the tool positions cover z from -6.5 to 1, so the hole is 6.5 deep (the tool is a prism: volume / height
            // is its cross-section)
            DMesh3 tool0 = new CappedCylinderGenerator() { BaseRadius = 1.3f, TopRadius = 1.3f, Height = 2.5f, Slices = 24 }.Generate().MakeDMesh();
            // (within the vertex moves of the safe rounding)
            Assert.Equal(360 - ResultChecks.Volume(tool0) / 2.5 * 6.5, ResultChecks.Volume(work), 6);
        }

        [Fact]
        public void InsideOutOperand_HasAnEmptySolid()
        {
            // https://github.com/CGAL/cgal/issues/9467 and https://github.com/libigl/libigl/issues/1998 (prose only):
            // inside-out inputs (unbounded volume, negative winding). ExactMeshBoolean treats a consistently inside-out
            // mesh as winding number -1: its solid is empty (documented at the class)
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(1, 1, 1, 3, 3, 3);
            b.ReverseOrientation();
            AssertCleanResult(Run(a, b, Ops.Union), 8, 1e-12, "union");
            Assert.Equal(0, Run(a, b, Ops.Intersection).Result.TriangleCount);
            AssertCleanResult(Run(a, b, Ops.Difference), 8, 1e-12, "difference");
        }
    }
}
