using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Inputs with inconsistently oriented triangles (flipped faces, as from broken files): ExactMeshBoolean
    /// orients each component consistently (the larger part by area keeps its orientation) before classifying,
    /// so the result is the consistently oriented solid, not a surface with edges that two triangles run the
    /// same way.
    /// </summary>
    public class ExactBooleanOrientationTests
    {
        static DMesh3 Box(Vector3d lo, Vector3d hi)
        {
            return new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(lo, hi)) }.Generate().MakeDMesh();
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices)
        {
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, center);
            return s;
        }

        // the mesh with the triangles flipped (b, a, c) for which flip(tid) is true
        static DMesh3 Flipped(DMesh3 m, Func<int, bool> flip)
        {
            var r = new DMesh3();
            foreach (int v in m.VertexIndices())
                r.AppendVertex(m.GetVertex(v));
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                r.AppendTriangle(flip(t) ? new Index3i(f.b, f.a, f.c) : f);
            }
            return r;
        }

        // directed edges (by position) used more than once in the same direction
        static int SameDirectionEdges(DMesh3 m)
        {
            var count = new Dictionary<(Vector3d, Vector3d), int>();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                for (int k = 0; k < 3; ++k) {
                    var e = (m.GetVertex(f[k]), m.GetVertex(f[(k + 1) % 3]));
                    count.TryGetValue(e, out int c);
                    count[e] = c + 1;
                }
            }
            return count.Values.Count(c => c > 1);
        }

        static double Volume(DMesh3 m)
        {
            return MeshMeasurements.VolumeArea(m, m.TriangleIndices(), m.GetVertex).x;
        }

        static void AssertOriented(DMesh3 r, double volume)
        {
            Assert.Equal(0, SameDirectionEdges(r));
            Assert.True(r.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(r));
            Assert.Equal(volume, Volume(r), 9);
        }

        [Fact]
        public void Resolve_BoxWithOneFlippedTriangle_IsOrientedBox()
        {
            DMesh3 m = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t == 0);
            DMesh3 r = ExactMeshBoolean.Resolve(m);
            Assert.NotNull(r);
            Assert.Equal(12, r.TriangleCount);
            AssertOriented(r, 1);
        }
    
        // the triangles as a soup: three vertices of their own each
        static DMesh3 Soup(DMesh3 m)
        {
            var r = new DMesh3();
            foreach (int t in m.TriangleIndices()) {
                Index3i f = m.GetTriangle(t);
                r.AppendTriangle(r.AppendVertex(m.GetVertex(f.a)), r.AppendVertex(m.GetVertex(f.b)), r.AppendVertex(m.GetVertex(f.c)));
            }
            return r;
        }

        [Fact]
        public void Resolve_FlippedTriangles_CallersMeshUnchanged()
        {
            DMesh3 m = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t == 0 || t == 7);
            var before = m.TriangleIndices().Select(m.GetTriangle).ToList();
            var b = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2, b.ReorientedTriangles);
            Assert.True(b.ExactWindingA);       // oriented, the box is closed in the winding sense: exact winding numbers
            AssertOriented(b.Result, 1);
            Assert.Equal(before, m.TriangleIndices().Select(m.GetTriangle).ToList());
        }

        [Fact]
        public void OpenMeshWithInvertedShell_OnlyFlippedTriangleReoriented()
        {
            // one mesh: an outer box with a flipped triangle and an inverted cavity box (consistent on its own); the
            // mesh is not closed in the winding sense, so it is reoriented: only the flipped triangle, the cavity
            // stays inverted
            DMesh3 m = Flipped(Box(Vector3d.Zero, 3 * Vector3d.One), (t) => t == 4);
            DMesh3 cavity = Box(Vector3d.One, 2 * Vector3d.One);
            cavity.ReverseOrientation();
            MeshEditor.Append(m, cavity);
            var b = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(1, b.ReorientedTriangles);
            Assert.True(b.ExactWindingA);
            AssertOriented(b.Result, 27 - 1);
        }

        [Fact]
        public void ConsistentInputs_NotReoriented()
        {
            // a consistently inverted shell (a cavity) and an open consistently oriented surface keep their orientation
            DMesh3 outer = Box(Vector3d.Zero, 3 * Vector3d.One);
            DMesh3 cavity = Box(Vector3d.One, 2 * Vector3d.One);
            cavity.ReverseOrientation();
            MeshEditor.Append(outer, cavity);       // one mesh of two shells: winding number 0 in the cavity
            var b = new ExactMeshBoolean(outer, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(0, b.ReorientedTriangles);
            AssertOriented(b.Result, 27 - 1);
            DMesh3 open = Box(Vector3d.Zero, Vector3d.One);
            open.RemoveTriangle(0);
            var c = new ExactMeshBoolean(open, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(c.Compute(), c.Error);
            Assert.Equal(0, c.ReorientedTriangles);
        }

        [Theory]
        [InlineData(ExactMeshBoolean.Operations.Union, 1 + 1 - 0.125)]
        [InlineData(ExactMeshBoolean.Operations.Intersection, 0.125)]
        [InlineData(ExactMeshBoolean.Operations.Difference, 1 - 0.125)]
        public void TwoBoxesWithFlippedTriangles_OrientedResult(ExactMeshBoolean.Operations op, double volume)
        {
            DMesh3 a = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t == 3);
            DMesh3 b = Flipped(Box(0.5 * Vector3d.One, 1.5 * Vector3d.One), (t) => t == 4 || t == 9);
            var op2 = new ExactMeshBoolean(a, b, op);
            Assert.True(op2.Compute(), op2.Error);
            Assert.Equal(3, op2.ReorientedTriangles);
            AssertOriented(op2.Result, volume);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Resolve_PartlyFlippedSphere_EqualsResolvedSphere(int seed)
        {
            DMesh3 sphere = Sphere(new Vector3d(0.3, -0.2, 0.1), 1, 8);
            var rand = new Random(seed);
            var flips = new HashSet<int>(sphere.TriangleIndices().Where(t => rand.NextDouble() < 0.3));
            DMesh3 r0 = ExactMeshBoolean.Resolve(sphere), r = ExactMeshBoolean.Resolve(Flipped(sphere, flips.Contains));
            AssertOriented(r, Volume(r0));
            Assert.Equal(r0.TriangleCount, r.TriangleCount);
        }

        [Fact]
        public void Resolve_UnweldedSoupWithFlippedFace_IsOrientedBox()
        {
            // no shared vertex ids: the components are found by vertex position
            DMesh3 m = Soup(Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t == 0 || t == 1));
            DMesh3 r = ExactMeshBoolean.Resolve(m);
            Assert.NotNull(r);
            AssertOriented(r, 1);
        }

        [Fact]
        public void Resolve_MostlyFlippedBox_IsOrientedInverted()
        {
            // the larger part keeps its orientation: 10 of 12 triangles flipped is an inverted box, whose solid is
            // outside (an empty result for Resolve)
            DMesh3 m = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t >= 2);
            var b = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(2, b.ReorientedTriangles);
            Assert.True(b.ExactWindingA);
            Assert.Equal(0, b.Result.TriangleCount);
        }
    
        [Fact]
        public void Resolve_FlippedTriangleNextToFin_IsOrientedBox()
        {
            // two fins (open triangles) on one edge of the box, through which the orientation must not propagate
            // (four triangles at the edge: no neighbour across it); one flipped triangle elsewhere
            DMesh3 box = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t == 0);
            Index3i f = box.GetTriangle(5);
            Vector3d a = box.GetVertex(f.a), b = box.GetVertex(f.b), c = box.GetVertex(f.c);
            Vector3d n = (b - a).Cross(c - a).Normalized;
            // two fins on the edge a-b of triangle 5, pointing outwards, first in the mesh (visited first)
            var m = new DMesh3();
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(0.5 * (a + b) + n));
            m.AppendTriangle(m.AppendVertex(b), m.AppendVertex(a), m.AppendVertex(0.5 * (a + b) + 1.5 * n));
            MeshEditor.Append(m, Soup(box));
            var op = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1, op.ReorientedTriangles);
            Assert.Equal(0, SameDirectionEdges(op.Result));
            Assert.True(op.Result.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void MoebiusStrip_LeftAsItIs()
        {
            // not orientable: no consistent orientation to choose, the component is not changed
            int n = 24;
            var p = new List<Vector3d>();      // 2 i + (s + 1) / 2: the point at angle 2 pi i / n, side s
            for (int i = 0; i < n; ++i) {
                double t = 2 * Math.PI * i / n;
                for (int s = -1; s <= 1; s += 2) {
                    double r = 1 + 0.3 * s * Math.Cos(t / 2);
                    p.Add(new Vector3d(r * Math.Cos(t), r * Math.Sin(t), 0.3 * s * Math.Sin(t / 2)));
                }
            }
            // after a full turn the sides are swapped (the half twist)
            Func<int, int, Vector3d> at = (i, side) => (i < n) ? p[2 * i + side] : p[1 - side];
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) {
                m.AppendTriangle(m.AppendVertex(at(i, 0)), m.AppendVertex(at(i, 1)), m.AppendVertex(at(i + 1, 1)));
                m.AppendTriangle(m.AppendVertex(at(i, 0)), m.AppendVertex(at(i + 1, 1)), m.AppendVertex(at(i + 1, 0)));
            }
            var op = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(0, op.ReorientedTriangles);
        }
    
        static DMesh3 FlippedSphere() => Flipped(Sphere(new Vector3d(0.7, 0.4, 0.3), 0.8, 8), (t) => t % 3 == 0);
        static DMesh3 FlippedBox() => Flipped(Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1)), (t) => t == 2 || t == 5);

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void FlippedInputs_SameResultUnderSimilarity(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 a = FlippedBox(), b = FlippedSphere();
            var op0 = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Difference);
            Assert.True(op0.Compute(), op0.Error);
            var op = new ExactMeshBoolean(Invariance.Transformed(a, s), Invariance.Transformed(b, s), ExactMeshBoolean.Operations.Difference);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(op0.ReorientedTriangles, op.ReorientedTriangles);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(op.Result));
            Assert.Equal(0, SameDirectionEdges(op.Result));
            DMesh3 back = Invariance.MappedBack(op.Result, s);
            double v0 = Invariance.Volume(op0.Result), v = Invariance.Volume(back);
            Assert.True(Math.Abs(v - v0) <= (1e-12 + 100 * s.Resolution(2)) * Math.Abs(v0), $"{s.Name}: volume {v} vs {v0}");
            Assert.True(Invariance.VertexDistance(op0.Result, back) <= s.DistanceResolution(2), s.Name);
            if (s.IsExact)
                Assert.Equal("", Invariance.GeometricDifference(op0.Result, back));
        }

        [Fact]
        public void ReusedInstance_ReorientedTrianglesReset()
        {
            // flipped inputs, then consistent ones on the same instance: the count and the result as from a fresh instance
            var op = new ExactMeshBoolean(FlippedBox(), FlippedSphere(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.True(op.ReorientedTriangles > 0);
            op.MeshA = Box(new Vector3d(-1, -1, -1), new Vector3d(1, 1, 1));
            op.MeshB = Sphere(new Vector3d(0.7, 0.4, 0.3), 0.8, 8);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(0, op.ReorientedTriangles);
            var fresh = new ExactMeshBoolean(op.MeshA, op.MeshB, ExactMeshBoolean.Operations.Union);
            Assert.True(fresh.Compute(), fresh.Error);
            Assert.Equal("", Invariance.Difference(fresh.Result, op.Result));
        }

        [Fact]
        public void FlippedInputs_RepeatedRuns_BitIdentical()
        {
            DMesh3 first = null;
            for (int run = 0; run < 10; ++run) {
                var op = new ExactMeshBoolean(FlippedBox(), FlippedSphere(), ExactMeshBoolean.Operations.Union);
                Assert.True(op.Compute(), op.Error);
                if (first == null)
                    first = op.Result;
                else
                    Assert.Equal("", Invariance.Difference(first, op.Result));
            }
        }
    
        [Fact]
        public void NaryConstructor_CallersListUnchanged_SameCountOnSecondCompute()
        {
            DMesh3 a = FlippedBox(), b = FlippedSphere();
            var op = new ExactMeshBoolean(new List<DMesh3>() { a, b }, ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            int count = op.ReorientedTriangles;
            Assert.True(count > 0);
            Assert.Same(a, op.Meshes[0]);
            Assert.Same(b, op.Meshes[1]);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(count, op.ReorientedTriangles);
            AssertOriented(op.Result, Volume(op.Result));
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void HalfFlippedBox_SameChoiceUnderSimilarity(string name)
        {
            // 6 of 12 triangles flipped: equal areas (up to their rounding); the first triangle keeps its orientation
            // under every transform
            Similarity s = Similarity.ByName(name);
            DMesh3 m = Flipped(Box(Vector3d.Zero, Vector3d.One), (t) => t % 2 == 1);
            var op = new ExactMeshBoolean(Invariance.Transformed(m, s), new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(6, op.ReorientedTriangles);
            Assert.True(op.ExactWindingA);
            Assert.Equal(0, SameDirectionEdges(op.Result));
            // triangle 0 is outward: the result is the box
            Assert.Equal(12, op.Result.TriangleCount);
        }

        [Fact]
        public void Resolve_SoupWithNegativeZeroAndDegenerateTriangle_IsOrientedBox()
        {
            // the box at the origin as a soup, with -0.0 in the copies of some vertices (welded with 0.0), one flipped
            // face and a zero-area triangle on an edge (corners a, b and the midpoint of a-b)
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            var m = new DMesh3();
            int k = 0;
            foreach (int t in box.TriangleIndices()) {
                Index3i f = box.GetTriangle(t);
                if (t == 6) f = new Index3i(f.b, f.a, f.c);
                Func<Vector3d, Vector3d> neg = (v) => (k++ % 2 == 0) ? new Vector3d(v.x == 0 ? -0.0 : v.x, v.y == 0 ? -0.0 : v.y, v.z == 0 ? -0.0 : v.z) : v;
                m.AppendTriangle(m.AppendVertex(neg(box.GetVertex(f.a))), m.AppendVertex(neg(box.GetVertex(f.b))), m.AppendVertex(neg(box.GetVertex(f.c))));
            }
            Index3i e = box.GetTriangle(0);
            Vector3d a = box.GetVertex(e.a), b = box.GetVertex(e.b);
            m.AppendTriangle(m.AppendVertex(a), m.AppendVertex(b), m.AppendVertex(0.5 * (a + b)));
            var op = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(1, op.ReorientedTriangles);
            AssertOriented(op.Result, 1);
        }
    
        [Fact]
        public void NearlyHalfFlippedBox_FirstTriangleKeepsOrientation()
        {
            // 6 of 12 triangles flipped, the flipped ones larger by a relative 3e-13 (a vertex moved by 1e-12): within
            // the tie band, so the first triangle keeps its orientation (outward: the box), not the larger part
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            box.SetVertex(2, box.GetVertex(2) + 1e-12 * Vector3d.AxisX);
            DMesh3 m = Flipped(box, (t) => t % 2 == 1);
            var op = new ExactMeshBoolean(m, new DMesh3(), ExactMeshBoolean.Operations.Union);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal(6, op.ReorientedTriangles);
            Assert.Equal(12, op.Result.TriangleCount);
            Assert.Equal(0, SameDirectionEdges(op.Result));
        }
    }
}
