using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    // Rare paths of DualContouring that ordinary grids never reach: they need cells that are a few ulps wide, where
    // a grid edge has no double strictly inside, a face has one interior point or none, and the vertices cannot be
    // kept off the cell borders. The fields are procedural (a hash of the grid node gives its value, trilinear in
    // between, so ambiguous faces and saddles are everywhere; + - * / only, so the signs do not depend on the
    // platform's math library), the grids are pinned (found by a search over seeds, origins and cell sizes) and
    // small. The results are not "nice" at this scale (polygons that cannot be placed without a non-manifold
    // edge are left out and counted), so the tests check what must hold anyway: a valid, finite mesh inside
    // the bounds, every auxiliary vertex used, and the counters agree with the mesh.
    //
    // A mutable static of DualContouring (GenerateFinished) is set by one test: the collection runs without
    // other tests in parallel.
    [Collection("DualContouringDiagnostics")]
    public class DualContouringCoverageTests
    {
        class Fn : ImplicitFunction3d
        {
            public Func<Vector3d, double> F;
            public double Value(ref Vector3d p) { return F(p); }
        }

        // pseudo random value in [-1, 1] of a grid node
        static double NodeHash(int i, int j, int k, int seed)
        {
            unchecked {
                uint h = (uint)(i * 73856093) ^ (uint)(j * 19349663) ^ (uint)(k * 83492791) ^ (uint)(seed * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; h *= 0x27d4eb2d; h ^= h >> 16;
                return (h % 20001) / 10000.0 - 1.0;
            }
        }

        // trilinear interpolation of the node values, in cell units
        static Func<Vector3d, double> Noise(int seed)
        {
            return u => {
                int i = (int)Math.Floor(u.x), j = (int)Math.Floor(u.y), k = (int)Math.Floor(u.z);
                double fx = u.x - i, fy = u.y - j, fz = u.z - k;
                double s = 0;
                for (int c = 0; c < 8; ++c) {
                    int a = c & 1, b = (c >> 1) & 1, d = (c >> 2) & 1;
                    s += NodeHash(i + a, j + b, k + d, seed) * (a == 1 ? fx : 1 - fx) * (b == 1 ? fy : 1 - fy) * (d == 1 ? fz : 1 - fz);
                }
                return s;
            };
        }

        // the noise inside a ball (radius in grid sizes), a closed surface: a sparse grid follows the surface wherever it goes
        static Func<Vector3d, double> Bounded(Func<Vector3d, double> noise, int res, double radius)
        {
            return u => Math.Max(noise(u), (u - 0.5 * res * Vector3d.One).Length - radius * res);
        }

        // the spacing of the doubles above x (x > 0)
        static double Ulp(double x)
        {
            return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(x) + 1) - x;
        }

        // Res^3 cells of 2^exp ulps (of the origin) at (org, org, org): the grid nodes are doubles 2^exp apart, so
        // an edge of 1 ulp (exp 0) has no double strictly inside. No repair of self-intersections (not under test)
        internal static DualContouring Uniform(int seed, double org, int exp, int res)
        {
            double cs = Ulp(org) * (1 << exp);
            Func<Vector3d, double> g = Bounded(Noise(seed), res, 0.45);
            Vector3d o = new Vector3d(org, org, org);
            var dc = new DualContouring() {
                Implicit = new Fn() { F = p => g((p - o) / cs) }, InsideIsLess = true, CellSize = cs, ParallelCompute = false,
                ResolveSelfIntersections = false, Bounds = new AxisAlignedBox3d(o, o + res * cs * Vector3d.One)
            };
            dc.Generate();
            return dc;
        }

        // the same with refinement: coarse cells of factor * 2^exp ulps, a pseudo random share `share` of them
        // replaced by factor^3 fine cells (so fine cells of 2^exp ulps next to coarse ones: the transitions)
        internal static DualContouring Refined(int seed, double org, int exp, int factor, double share, int res)
        {
            double cs = factor * Ulp(org) * (1 << exp);
            Func<Vector3d, double> g = Bounded(Noise(seed), res, 0.4);
            Vector3d o = new Vector3d(org, org, org);
            var all = new List<Vector3i>();
            var refined = new List<Vector3i>();
            var fine = new List<Vector3i>();
            for (int k = 0; k < res; ++k)
                for (int j = 0; j < res; ++j)
                    for (int i = 0; i < res; ++i) {
                        var c = new Vector3i(i, j, k);
                        all.Add(c);
                        if (new Random(unchecked(seed * 1000003 + i * 10007 + j * 101 + k)).NextDouble() >= share)
                            continue;
                        refined.Add(c);
                        for (int a = 0; a < factor; ++a)
                            for (int b = 0; b < factor; ++b)
                                for (int d = 0; d < factor; ++d)
                                    fine.Add(factor * c + new Vector3i(a, b, d));
                    }
            var dc = new DualContouring() {
                Implicit = new Fn() { F = p => g((p - o) / cs) }, InsideIsLess = true, CellSize = cs, ParallelCompute = false,
                ResolveSelfIntersections = false, Bounds = new AxisAlignedBox3d(o, o + res * cs * Vector3d.One),
                CandidateCells = all, NodeValueF = v => g(new Vector3d(v.x, v.y, v.z))
            };
            dc.SetRefinement(factor, refined, fine, v => g(new Vector3d(v.x, v.y, v.z) / factor));
            dc.Generate();
            return dc;
        }

        // what holds whatever the polygons that could not be placed: a valid mesh of finite vertices inside the
        // bounds, no vertex left without triangles, every edge vertex the center of a complete fan, the counters
        // of the auxiliary vertices equal to the vertices present, and a closed mesh unless polygons were left out
        internal static void AssertSane(DualContouring dc, string what)
        {
            DMesh3 m = dc.Mesh;
            Assert.True(m.CheckValidity(true, FailMode.ReturnOnly), what + ": invalid mesh");
            Assert.True(m.TriangleCount > 0, what);
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                Assert.True(p.IsFinite, what + ": non-finite vertex");
                for (int k = 0; k < 3; ++k)
                    Assert.InRange(p[k], dc.Bounds.Min[k], dc.Bounds.Max[k]);
                Assert.True(m.GetVtxEdgeCount(v) > 0, what + ": vertex " + v + " without triangles");
            }
            Assert.Equal(dc.EdgeVertices, dc.edgeVertexIds.Count);
            Assert.Equal(dc.FaceVertices, dc.faceVertexIds.Count);
            foreach (int v in dc.edgeVertexIds)
                Assert.True(m.IsVertex(v) && m.GetVtxEdgeCount(v) >= 3 && !m.IsBoundaryVertex(v), what + ": edge vertex " + v + " without a complete fan");
            foreach (int v in dc.faceVertexIds)
                Assert.True(m.IsVertex(v), what + ": face vertex " + v + " removed but counted");
            // every auxiliary vertex (no patches) in the mesh is a counted face or edge vertex: none left from a rolled back fan
            foreach (int v in m.VertexIndices())
                if (dc.genPatches[v] == 0)
                    Assert.True(dc.edgeVertexIds.Contains(v) || dc.faceVertexIds.Contains(v), what + ": stray auxiliary vertex " + v);
            if (dc.NonManifoldTriangles == 0) {
                Assert.True(m.IsClosed(), what + ": not closed");
                Assert.Empty(dc.NonManifoldCells);
            } else {
                Assert.False(m.IsClosed(), what + ": polygons left out but closed");
                Assert.NotEmpty(dc.NonManifoldCells);
            }
        }

        [Fact]
        public void GenerateFinished_IsCalledOncePerGenerate_WithTheFinishedMesh()
        {
            var seen = new List<DualContouring>();
            var triangles = new List<int>();
            var phases = new List<bool>();
            Action<DualContouring> hook = (d) => { seen.Add(d); triangles.Add(d.Mesh.TriangleCount); phases.Add(d.PhaseMilliseconds.ContainsKey("mesh")); };
            DualContouring.GenerateFinished = hook;
            try {
                var dc = new DualContouring() {
                    Implicit = new Fn() { F = p => p.Length - 1 }, InsideIsLess = true, CellSize = 0.25, ParallelCompute = false,
                    Bounds = new AxisAlignedBox3d(Vector3d.Zero, 1.3)
                };
                dc.Generate();
                Assert.Single(seen);
                Assert.Same(dc, seen[0]);
                Assert.True(dc.Mesh.TriangleCount > 0);
                Assert.Equal(dc.Mesh.TriangleCount, triangles[0]);     // called after the mesh is final
                Assert.True(phases[0]);
                Assert.True(dc.Mesh.IsClosed());
                dc.Generate();                                          // once more per call (also a reused instance)
                Assert.Equal(2, seen.Count);
                Assert.Same(dc, seen[1]);
            } finally {
                DualContouring.GenerateFinished = null;
            }
        }


        // grid edges with different signs at their ends (the sign of the float the grid keeps, as the contour sees it)
        static int CountCrossings(int seed, int res, double radius)
        {
            Func<Vector3d, double> g = Bounded(Noise(seed), res, radius);
            Func<int, int, int, bool> inside = (i, j, k) => (float)g(new Vector3d(i, j, k)) < 0;
            int n = 0;
            for (int k = 0; k <= res; ++k)
                for (int j = 0; j <= res; ++j)
                    for (int i = 0; i <= res; ++i) {
                        bool a = inside(i, j, k);
                        if (i < res && a != inside(i + 1, j, k)) n++;
                        if (j < res && a != inside(i, j + 1, k)) n++;
                        if (k < res && a != inside(i, j, k + 1)) n++;
                    }
            return n;
        }

        // the face vertices' offsets from the origin in ulps (all in the binade of the origin), summed per axis
        static long[] FaceVertexUlps(DualContouring dc, double org)
        {
            long[] sum = new long[3];
            double ulp = Ulp(org);
            foreach (int v in dc.faceVertexIds) {
                Vector3d p = dc.Mesh.GetVertex(v);
                for (int k = 0; k < 3; ++k)
                    sum[k] += (long)Math.Round((p[k] - org) / ulp);
            }
            return sum;
        }

        // ---- cells of a few ulps on a uniform grid ----

        [Fact]
        public void Quads_ThatCannotBeAdded_AreTakenBackAndLeftOut()
        {
            // 2 ulp cells: a cell or face has one interior point at most, no face vertex fits, so quads of one
            // ambiguous face share an edge and some cannot be added with either diagonal: the first triangle of a
            // split is taken back when the second does not fit (no flap), the quad is a hole
            DualContouring dc = Uniform(759, 0.1, 1, 6);
            AssertSane(dc, "759");
            Assert.Equal(0, dc.FaceVertices + dc.EdgeVertices);                      // only quads
            Assert.Equal(8, dc.NonManifoldTriangles);                                // 4 quads, 2 triangles each
            Assert.Equal(2 * CountCrossings(759, 6, 0.45) - dc.NonManifoldTriangles, dc.Mesh.TriangleCount);
            Assert.Equal(12, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(12, dc.NonManifoldCells.Count);
        }

        [Fact]
        public void QuadOfSingleVertices_ThatCannotBeAdded_IsLeftOutAndItsCellsAreListed()
        {
            // the same where Wang's test finds the quad inside its envelope (four single patch vertices strictly inside
            // their cells): the quad is not added with either diagonal, counted, and its cells are listed
            DualContouring dc = Uniform(3614, 9.5367431640625E-07, 1, 6);
            AssertSane(dc, "3614");
            Assert.Equal(0, dc.FaceVertices + dc.EdgeVertices);
            Assert.Equal(4, dc.NonManifoldTriangles);
            Assert.Equal(2 * CountCrossings(3614, 6, 0.45) - dc.NonManifoldTriangles, dc.Mesh.TriangleCount);
            Assert.Equal(6, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(6, dc.NonManifoldCells.Count);
        }

        [Fact]
        public void EdgeVertexFan_WhoseTriangleCannotBeAdded_IsTakenBackAndTheQuadAddedPlain()
        {
            // 4 ulp cells (found by a search over seeds 0..3999, origins 0.1, 2^-20, 1024, 3.7 and 1 to 8 ulp cells: one hit): the
            // fan around an edge vertex of a quad runs into an edge that is used up. The triangles added so far and the edge
            // vertex are taken back (no stray auxiliary vertex: AssertSane), and the quad is added with a diagonal instead
            DualContouring dc = Uniform(3313, 0.1, 2, 6);
            AssertSane(dc, "3313");
            Assert.Equal(4, dc.NonManifoldTriangles);
            Assert.Equal(4, dc.EdgeVertices);
            Assert.Equal(2, dc.FaceVertices);
            Assert.Equal(312, dc.Mesh.TriangleCount);
            Assert.Equal(6, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(6, dc.NonManifoldCells.Count);
        }

        [Fact]
        public void EdgeVertex_OnAnEdgeOfFewUlps_IsStrictlyInsideTheEdge()
        {
            // 4 ulp cells: the margin from the end nodes (1 % of the edge) is below half an ulp, the clamped point is a
            // node, and the midpoint of the edge is used instead. The result is closed, with edge vertices strictly
            // inside their grid edges (exactly on them: two coordinates are nodes')
            DualContouring dc = Uniform(1952, 9.5367431640625E-07, 2, 6);
            AssertSane(dc, "1952");
            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.Equal(4, dc.EdgeVertices);
            Assert.Equal(2, dc.FaceVertices);
            double org = 9.5367431640625E-07, cs = 4 * Ulp(org);
            foreach (int v in dc.edgeVertexIds) {
                Vector3d p = dc.Mesh.GetVertex(v);
                int offNode = 0;
                for (int k = 0; k < 3; ++k) {
                    double u = (p[k] - org) / cs;
                    if (u != Math.Floor(u)) offNode++;
                }
                Assert.Equal(1, offNode);
            }
        }

        [Fact]
        public void FanThatCannotBeAdded_IsRolledBack_NoVertexOrTriangleLeft()
        {
            // an edge vertex whose fan does not fit (a face vertex was not placed, the edge is shared with another
            // polygon) is taken back together with the triangles of the fan; the polygon is a hole, counted
            DualContouring dc = Uniform(7233, 0.1, 2, 6);
            AssertSane(dc, "7233");
            Assert.Equal(7, dc.NonManifoldTriangles);
            Assert.Equal(3, dc.EdgeVertices);
            Assert.Equal(2, dc.FaceVertices);
            Assert.Equal(7, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(6, dc.NonManifoldCells.Count);
        }

        // ---- transitions between cells of a few ulps ----

        [Fact]
        public void FaceVertex_WhereTheQefPointAndItsClampAreNotAllowed_IsBisectedTowardsTheMassPoint()
        {
            // faces of 8 ulp next to coarse ones of 16 ulp: the clamp of the QEF point into the face (by the cell
            // margin, below half an ulp) is on the face border; the vertex then goes from there towards the mass
            // point of the segment's surface points, as far as that stays allowed
            DualContouring dc = Refined(9496, 3.7, 3, 2, 0.30000000000000004, 4);
            AssertSane(dc, "9496");
            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.Equal(17, dc.FaceVertices);
            Assert.Equal(6, dc.ClampedFaceVertices);
            Assert.Equal(17, dc.EdgeVertices);
            Assert.Equal(new long[] { 421, 411, 593 }, FaceVertexUlps(dc, 3.7));
        }

        [Fact]
        public void FineEdgeOfOneUlp_OnACoarseFace_FansFromTheFirstVertex()
        {
            // a fine edge of 1 ulp cannot get an edge vertex, its polygon (between cells of different size, with
            // face vertices) becomes a fan from its first vertex: the surface stays closed. Where no face vertex
            // fits the face of 1 ulp either, the last placement (center of the face or of its allowed triangle) is tried
            DualContouring dc = Refined(5935, 1024, 0, 4, 0.2, 4);
            AssertSane(dc, "5935");
            Assert.Equal(0, dc.NonManifoldTriangles);
            Assert.Equal(2, dc.FaceVertices);
            Assert.Equal(3, dc.EdgeVertices);
            Assert.Equal(new long[] { 16, 12, 12 }, FaceVertexUlps(dc, 1024));
        }

        [Fact]
        public void FineEdgeOfOneUlp_FanThatCannotBeAdded_IsRolledBack()
        {
            // the fan from the first vertex does not fit (a polygon edge is used): the triangles added so far are
            // taken back, the polygon is a hole counted with its triangles
            DualContouring dc = Refined(10371, 0.1, 0, 4, 0.30000000000000004, 4);
            AssertSane(dc, "10371");
            Assert.Equal(3, dc.NonManifoldTriangles);
            Assert.Equal(5, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(3, dc.NonManifoldCells.Count);
            Assert.Equal(2, dc.FaceVertices);
            Assert.Equal(1, dc.EdgeVertices);
        }

        [Fact]
        public void FaceVertex_WhoseFansAllFailed_IsRemovedAndNotCounted()
        {
            // the only fans around the face vertex did not fit: it has no triangles, is removed from the mesh and
            // from the counter
            DualContouring dc = Refined(10749, 0.1, 0, 4, 0.6000000000000001, 4);
            AssertSane(dc, "10749");
            Assert.Equal(6, dc.NonManifoldTriangles);
            Assert.Equal(1, dc.FaceVertices);
            Assert.Equal(0, dc.EdgeVertices);
            Assert.Equal(6, dc.Mesh.BoundaryEdgeIndices().Count());
            Assert.Equal(3, dc.NonManifoldCells.Count);
            // all auxiliary vertices that are left are the counted ones
            int aux = dc.Mesh.VertexIndices().Count(v => dc.genPatches[v] == 0);
            Assert.Equal(dc.FaceVertices + dc.EdgeVertices, aux);
        }
    }
}
