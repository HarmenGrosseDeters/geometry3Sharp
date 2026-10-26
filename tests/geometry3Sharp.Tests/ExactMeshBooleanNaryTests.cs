using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    // ExactMeshBoolean with more than two meshes in one arrangement: n-ary union / intersection / difference
    // and winding number predicates (Zhou et al. 2016)
    public class ExactMeshBooleanNaryTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices = 10)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }

        static ExactMeshBoolean Check(ExactMeshBoolean boolean)
        {
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.Equal(Status.Success, boolean.Status);
            DMesh3 r = boolean.Result;
            Assert.True(r.CheckValidity(true, FailMode.ReturnOnly), "invalid mesh");
            if (r.TriangleCount > 0) {
                Assert.True(r.IsClosed(), $"{MeshIterators.BoundaryEdges(r).Count()} boundary edges");
                Assert.True(ExactMeshBoolean.IsWindingClosed(r));
            }
            Assert.Equal(0, boolean.NonManifoldTriangles);
            return boolean;
        }

        static DMesh3 Run(IEnumerable<DMesh3> meshes, Ops op)
        {
            return Check(new ExactMeshBoolean(meshes, op)).Result;
        }

        static DMesh3 Run(IEnumerable<DMesh3> meshes, Func<int[], bool> predicate)
        {
            return Check(new ExactMeshBoolean(meshes, predicate)).Result;
        }

        static DMesh3 Pairwise(DMesh3 a, DMesh3 b, Ops op)
        {
            return Check(new ExactMeshBoolean(a, b, op)).Result;
        }

        // boxes with corners on the grid of spacing 1/2 in [0, 4]^3, and the volume of a predicate over them
        // counted per grid cell (exact)
        static double[] RandomBox(Random rand)
        {
            var b = new double[6];
            for (int k = 0; k < 3; ++k) {
                int lo = rand.Next(0, 6), hi = rand.Next(lo + 1, 9);
                b[k] = 0.5 * lo; b[k + 3] = 0.5 * hi;
            }
            return b;
        }

        static double CellVolume(List<double[]> boxes, Func<int[], bool> predicate)
        {
            int cells = 0;
            var w = new int[boxes.Count];
            for (int i = 0; i < 8; ++i)
                for (int j = 0; j < 8; ++j)
                    for (int k = 0; k < 8; ++k) {
                        double x = 0.5 * i + 0.25, y = 0.5 * j + 0.25, z = 0.5 * k + 0.25;
                        for (int m = 0; m < boxes.Count; ++m) {
                            double[] b = boxes[m];
                            w[m] = (x > b[0] && x < b[3] && y > b[1] && y < b[4] && z > b[2] && z < b[5]) ? 1 : 0;
                        }
                        if (predicate(w))
                            cells++;
                    }
            return cells * 0.125;
        }

        static DMesh3 BoxOf(double[] b) { return Box(b[0], b[1], b[2], b[3], b[4], b[5]); }


        [Fact]
        public void ThreeOverlappingBoxes_UnionAndIntersection_ExactVolumes()
        {
            // general position (no coplanar faces): inclusion-exclusion of box intersections
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(1, 0.5, 0.25, 3, 2.5, 1.75), c = Box(0.5, 1.25, -1, 1.75, 3, 1);
            double va = 8, vb = 2 * 2 * 1.5, vc = 1.25 * 1.75 * 2;
            double vab = 1 * 1.5 * 1.5, vac = 1.25 * 0.75 * 1, vbc = 0.75 * 1.25 * 0.75, vabc = 0.75 * 0.75 * 0.75;
            var meshes = new[] { a, b, c };
            Assert.Equal(va + vb + vc - vab - vac - vbc + vabc, Volume(Run(meshes, Ops.Union)), 12);
            Assert.Equal(vabc, Volume(Run(meshes, Ops.Intersection)), 12);
            Assert.Equal(va - vab - vac + vabc, Volume(Run(meshes, Ops.Difference)), 12);
            // the static helpers
            Assert.Equal(vabc, Volume(ExactMeshBoolean.Intersection(meshes)), 12);
            Assert.Equal(va + vb + vc - vab - vac - vbc + vabc, Volume(ExactMeshBoolean.Union(meshes)), 12);
            // at least two of three: the pairwise intersections without double counting the triple one
            Assert.Equal(vab + vac + vbc - 2 * vabc, Volume(Run(meshes, ExactMeshBoolean.AtLeast(2))), 12);
        }

        [Fact]
        public void RandomGridBoxes_Predicates_MatchCellCounts()
        {
            // boxes on a coarse grid: many coplanar faces of three and more meshes, collinear edges, touching
            // faces, edges and vertices
            var rand = new Random(17);
            for (int iter = 0; iter < 12; ++iter) {
                int n = 3 + iter % 4;
                var boxes = new List<double[]>();
                for (int m = 0; m < n; ++m)
                    boxes.Add(RandomBox(rand));
                var meshes = boxes.Select(BoxOf).ToList();
                var predicates = new List<Func<int[], bool>>() {
                    (w) => w.Any(x => x > 0),
                    (w) => w.All(x => x > 0),
                    (w) => w[0] > 0 && w.Skip(1).All(x => x <= 0),
                    ExactMeshBoolean.AtLeast(2),
                    ExactMeshBoolean.AtLeast(n - 1),
                    (w) => w.Count(x => x > 0) % 2 == 1,      // odd parity (symmetric difference)
                };
                foreach (var predicate in predicates) {
                    double expected = CellVolume(boxes, predicate);
                    Assert.Equal(expected, Volume(Run(meshes, predicate)), 9);
                }
                Assert.Equal(CellVolume(boxes, predicates[0]), Volume(Run(meshes, Ops.Union)), 9);
                Assert.Equal(CellVolume(boxes, predicates[1]), Volume(Run(meshes, Ops.Intersection)), 9);
                Assert.Equal(CellVolume(boxes, predicates[2]), Volume(Run(meshes, Ops.Difference)), 9);
            }
        }

        [Fact]
        public void AtLeastKOfN_Spheres_MatchesChainedPairwise()
        {
            // at least two of three = (A n B) u (A n C) u (B n C), with pairwise booleans
            DMesh3 a = Sphere(Vector3d.Zero, 1), b = Sphere(new Vector3d(0.9, 0.2, 0.1), 1), c = Sphere(new Vector3d(0.4, 0.8, -0.3), 1);
            DMesh3 two = Run(new[] { a, b, c }, ExactMeshBoolean.AtLeast(2));
            DMesh3 ab = Pairwise(a, b, Ops.Intersection), ac = Pairwise(a, c, Ops.Intersection), bc = Pairwise(b, c, Ops.Intersection);
            DMesh3 chained = Pairwise(Pairwise(ab, ac, Ops.Union), bc, Ops.Union);
            Assert.Equal(Volume(chained), Volume(two), 9);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(two));
            // at least one = union, at least three = intersection, at least four = empty
            Assert.Equal(Volume(Run(new[] { a, b, c }, Ops.Union)), Volume(Run(new[] { a, b, c }, ExactMeshBoolean.AtLeast(1))), 12);
            Assert.Equal(Volume(Run(new[] { a, b, c }, Ops.Intersection)), Volume(Run(new[] { a, b, c }, ExactMeshBoolean.AtLeast(3))), 12);
            Assert.Equal(0, Run(new[] { a, b, c }, ExactMeshBoolean.AtLeast(4)).TriangleCount);
        }

        [Fact]
        public void ThreeMeshes_CoplanarFaces()
        {
            // three boxes on the same floor (z = 0) whose bottom faces overlap pairwise and all three in a region,
            // and whose top faces are in the same plane z = 1 too
            DMesh3 a = Box(0, 0, 0, 2, 2, 1), b = Box(1, 0, 0, 3, 2, 1), c = Box(0.5, 1, 0, 2.5, 3, 1);
            var meshes = new[] { a, b, c };
            var boxes = new List<double[]> { new double[] { 0, 0, 0, 2, 2, 1 }, new double[] { 1, 0, 0, 3, 2, 1 }, new double[] { 0.5, 1, 0, 2.5, 3, 1 } };
            var unionOp = Check(new ExactMeshBoolean(meshes, Ops.Union));
            DMesh3 union = unionOp.Result;
            Assert.Equal(CellVolume(boxes, (w) => w.Any(x => x > 0)), Volume(union), 12);
            // the pieces of a mesh are joined into patches also across edges shared with pieces of other meshes
            // (41 patches if only edges with the pieces of one mesh in a row are joined)
            Assert.True(unionOp.Patches <= 36, $"{unionOp.Patches} patches");
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(union));
            // the union is a prism over a flat polygon: no new vertices inside the floor and top faces
            foreach (Vector3d v in union.Vertices())
                Assert.True(v.z == 0 || v.z == 1);
            Assert.Equal(CellVolume(boxes, (w) => w.All(x => x > 0)), Volume(Run(meshes, Ops.Intersection)), 12);
            Assert.Equal(CellVolume(boxes, ExactMeshBoolean.AtLeast(2)), Volume(Run(meshes, ExactMeshBoolean.AtLeast(2))), 12);
        }

        [Fact]
        public void StackOfCoincidentTriangles_ThreeIdenticalMeshes()
        {
            // every face is in a stack of three coincident triangles, one per mesh; the third copy inside out
            DMesh3 a = Box(0, 0, 0, 1, 1, 1), b = Box(0, 0, 0, 1, 1, 1), c = Box(0, 0, 0, 1, 1, 1);
            var meshes = new[] { a, b, c };
            foreach (var op in new[] { Ops.Union, Ops.Intersection }) {
                DMesh3 r = Run(meshes, op);
                Assert.Equal(12, r.TriangleCount);
                Assert.Equal(1.0, Volume(r), 12);
            }
            Assert.Equal(0, Run(meshes, Ops.Difference).TriangleCount);
            Assert.Equal(1.0, Volume(Run(meshes, ExactMeshBoolean.AtLeast(3))), 12);
            Assert.Equal(0, Run(meshes, ExactMeshBoolean.AtLeast(4)).TriangleCount);
            // winding numbers add up: with c inside out the sum is 1 inside the box
            DMesh3 inv = Box(0, 0, 0, 1, 1, 1);
            inv.ReverseOrientation();
            Assert.Equal(0, Run(new[] { a, b, inv }, Ops.Intersection).TriangleCount);
            Assert.Equal(1.0, Volume(Run(new[] { a, b, inv }, (w) => w[0] + w[1] + w[2] == 1)), 12);
            Assert.Equal(12, Run(new[] { a, b, inv }, Ops.Union).TriangleCount);
        }

        [Fact]
        public void PredicateSeesTheWindingNumberVector()
        {
            // nested boxes in one mesh (winding number 2 in the inner box) and a second box: the predicate gets
            // the winding numbers per mesh, not only inside / outside
            DMesh3 nested = Box(0, 0, 0, 3, 3, 3);
            MeshEditor.Append(nested, Box(1, 1, 1, 2, 2, 2));
            DMesh3 other = Box(1.5, 0, 0, 4, 3, 3);
            Assert.Equal(1.0, Volume(Run(new[] { nested, other }, (w) => w[0] >= 2)), 12);
            Assert.Equal(0.5, Volume(Run(new[] { nested, other }, (w) => w[0] + w[1] >= 3)), 12);
            Assert.Equal(27 - 1, Volume(Run(new[] { nested, other }, (w) => w[0] == 1)), 12);
        }

        [Fact]
        public void NegativeWindingNumbers()
        {
            // a box inside an inside-out box: winding number -1 of the outer mesh at the inner box's faces
            DMesh3 inv = Box(0, 0, 0, 3, 3, 3);
            inv.ReverseOrientation();
            DMesh3 inner = Box(1, 1, 1, 2, 2, 2), outside = Box(5, 0, 0, 6, 1, 1);
            var meshes = new[] { inv, inner, outside };
            Assert.Equal(1.0, Volume(Run(meshes, (w) => w[0] < 0 && w[1] > 0)), 12);
            Assert.Equal(1.0, Volume(Run(meshes, (w) => w[0] + w[1] == 0 && w[1] > 0)), 12);
            Assert.Equal(1.0, Volume(Run(meshes, (w) => w[2] > 0)), 12);
            // inside out at the outer box: the solid where the outer winding number is -1 (and nothing else)
            Assert.Equal(27 - 1, Volume(Run(meshes, (w) => w[0] == -1 && w[1] == 0)), 12);
        }

        [Fact]
        public void TwoMeshes_SameCountsAsBeforeTheNaryChange()
        {
            // golden values of the two-mesh boolean before the n-ary classification (commit edb069f^1)
            DMesh3 a = Sphere(Vector3d.Zero, 1), b = Sphere(new Vector3d(0.7, 0.3, 0.2), 0.8);
            var expected = new Dictionary<Ops, int[]> {
                { Ops.Union, new[] { 1660, 832 } }, { Ops.Intersection, new[] { 836, 420 } }, { Ops.Difference, new[] { 1448, 726 } } };
            foreach (var kv in expected) {
                var boolean = Check(new ExactMeshBoolean(a, b, kv.Key));
                Assert.Equal(kv.Value[0], boolean.Result.TriangleCount);
                Assert.Equal(kv.Value[1], boolean.Result.VertexCount);
                Assert.Equal(4, boolean.Patches);
                Assert.Equal(149, boolean.IntersectingPairs);
            }
        }

        [Fact]
        public void HugeCoordinates_ThreeMeshes()
        {
            // finite coordinates whose sum overflows (5e307 + 5e307 + 5e307): accepted, same result as at scale 1
            var unit = new[] { Box(0, 0, 0, 2, 2, 2), Box(1, 0.5, 0.25, 3, 2.5, 1.75), Box(0.5, 1.25, -1, 1.75, 3, 1) };
            var huge = unit.Select(m => { var c = new DMesh3(m); MeshTransforms.Scale(c, 5e307); return c; }).ToList();
            // (DMesh3.CheckValidity overflows at these coordinates already for the inputs: checked here directly)
            Func<ExactMeshBoolean, DMesh3> run = (boolean) => {
                Assert.True(boolean.Compute(), boolean.Error);
                Assert.Equal(Status.Success, boolean.Status);
                Assert.True(boolean.Result.IsClosed());
                Assert.True(ExactMeshBoolean.IsWindingClosed(boolean.Result));
                return boolean.Result;
            };
            foreach (var op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                DMesh3 r1 = Run(unit, op), r2 = run(new ExactMeshBoolean(huge, op));
                Assert.Equal(r1.TriangleCount, r2.TriangleCount);
                Assert.Equal(r1.VertexCount, r2.VertexCount);
            }
            Assert.Equal(Run(unit, ExactMeshBoolean.AtLeast(2)).TriangleCount, run(new ExactMeshBoolean(huge, ExactMeshBoolean.AtLeast(2))).TriangleCount);
        }

        [Fact]
        public void CandidateSearchInBatches_SameResult()
        {
            // the candidate pair search runs in batches of mesh pairs (300 triangles per sphere): one pair per
            // batch (1, 100), two or three pairs per batch (700, 1000) find the same pairs and give the same result;
            // the first mesh intersects itself (two spheres in one mesh), so self pairs are counted too
            var rand = new Random(9);
            var meshes = new List<DMesh3>();
            for (int k = 0; k < 6; ++k)
                meshes.Add(Sphere(new Vector3d(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()), 0.6, 6));
            MeshEditor.Append(meshes[0], Sphere(new Vector3d(0.3, 0.2, 0.1), 0.5, 6));
            var full = Check(new ExactMeshBoolean(meshes, ExactMeshBoolean.AtLeast(2)));
            Assert.True(full.SelfIntersectingPairs > 0);
            foreach (long batch in new long[] { 1, 100, 700, 1000 }) {
                var small = Check(new ExactMeshBoolean(meshes, ExactMeshBoolean.AtLeast(2)) { CandidateBatchSize = batch });
                Assert.Equal(full.IntersectingPairs, small.IntersectingPairs);
                Assert.Equal(full.SelfIntersectingPairs, small.SelfIntersectingPairs);
                Assert.Equal(full.Result.TriangleCount, small.Result.TriangleCount);
                Assert.Equal(Volume(full.Result), Volume(small.Result), 14);
            }
        }

        [Fact]
        public void Status_ResetByEveryCompute()
        {
            // a Compute() that throws (here the predicate) does not leave the Success of the previous call
            var meshes = new[] { Box(0, 0, 0, 1, 1, 1), Box(0.5, 0, 0, 1.5, 1, 1), Box(0, 0.5, 0, 1, 1.5, 1) };
            var boolean = Check(new ExactMeshBoolean(meshes, Ops.Union));
            boolean.WindingPredicate = (w) => { throw new InvalidOperationException("predicate"); };
            Assert.Throws<InvalidOperationException>(() => boolean.Compute());
            Assert.Equal(Status.NotComputed, boolean.Status);
            Assert.Null(boolean.Result);
        }

        [Fact]
        public void TwoMeshes_SameAsPairwise()
        {
            // the list API with two meshes is the two-mesh boolean
            DMesh3 a = Sphere(Vector3d.Zero, 1), b = Sphere(new Vector3d(0.7, 0.3, 0.2), 0.8);
            foreach (var op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                DMesh3 p = Pairwise(a, b, op), n = Run(new[] { a, b }, op);
                Assert.Equal(p.TriangleCount, n.TriangleCount);
                Assert.Equal(p.VertexCount, n.VertexCount);
                Assert.Equal(Volume(p), Volume(n), 14);
            }
        }

        [Fact]
        public void ManySpheres_UnionMatchesChainedPairwise()
        {
            var rand = new Random(3);
            var meshes = new List<DMesh3>();
            for (int k = 0; k < 6; ++k)
                meshes.Add(Sphere(new Vector3d(rand.NextDouble() * 2, rand.NextDouble() * 2, rand.NextDouble()), 0.6 + 0.4 * rand.NextDouble(), 8));
            DMesh3 nary = Run(meshes, Ops.Union);
            DMesh3 chained = meshes[0];
            for (int k = 1; k < meshes.Count; ++k)
                chained = Pairwise(chained, meshes[k], Ops.Union);
            Assert.Equal(Volume(chained), Volume(nary), 9);
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(nary));
            // the one-pass result has no rounded intermediate vertices: at most as many vertices as the chain
            Assert.True(nary.VertexCount <= chained.VertexCount, $"{nary.VertexCount} vs {chained.VertexCount}");

            DMesh3 inter = Run(meshes.Take(3), Ops.Intersection);
            DMesh3 chainedInter = Pairwise(Pairwise(meshes[0], meshes[1], Ops.Intersection), meshes[2], Ops.Intersection);
            Assert.Equal(Volume(chainedInter), Volume(inter), 9);

            DMesh3 diff = Run(meshes, Ops.Difference);
            DMesh3 chainedDiff = meshes[0];
            for (int k = 1; k < meshes.Count; ++k)
                chainedDiff = Pairwise(chainedDiff, meshes[k], Ops.Difference);
            Assert.Equal(Volume(chainedDiff), Volume(diff), 9);
        }

        [Fact]
        public void CubeGrid_OnePass_MatchesChained()
        {
            // the iterated cube grid of ExactPlanarSimplifyTests (exactly coplanar faces of up to eight cubes) in
            // one operation, with the voids subtracted by the predicate: inside a cube and in no void
            int n = 3;
            var cubes = new List<DMesh3>();
            var voids = new List<DMesh3>();
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    for (int k = 0; k < n; ++k) {
                        cubes.Add(Box(0.9 * i, 0.9 * j, 0.9 * k, 0.9 * i + 1, 0.9 * j + 1, 0.9 * k + 1));
                        voids.Add(Box(0.9 * i + 0.25, 0.9 * j + 0.25, 0.9 * k + 0.25, 0.9 * i + 0.75, 0.9 * j + 0.75, 0.9 * k + 0.75));
                    }
            int nc = cubes.Count;
            double side = 0.9 * (n - 1) + 1;
            DMesh3 union = Run(cubes, Ops.Union);
            Assert.Equal(side * side * side, Volume(union), 9);
            Assert.True(union.VertexCount <= 6 * 2 * n * 2 * n, $"{union.VertexCount} vertices");
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(union));

            var all = cubes.Concat(voids).ToList();
            Func<int[], bool> grid = (w) => {
                bool inCube = false;
                for (int m = 0; m < nc; ++m) inCube |= w[m] > 0;
                for (int m = nc; m < w.Length; ++m) if (w[m] > 0) return false;
                return inCube;
            };
            DMesh3 holes = Run(all, grid);
            Assert.Equal(side * side * side - nc * 0.125, Volume(holes), 9);
            var comps = new MeshConnectedComponents(holes);
            comps.FindConnectedT();
            Assert.Equal(1 + nc, comps.Count);

            DMesh3 chained = cubes[0];
            for (int c = 1; c < nc; ++c)
                chained = Pairwise(chained, cubes[c], Ops.Union);
            foreach (DMesh3 v in voids)
                chained = Pairwise(chained, v, Ops.Difference);
            Assert.Equal(Volume(chained), Volume(holes), 9);
            Assert.True(holes.TriangleCount <= chained.TriangleCount, $"{holes.TriangleCount} vs {chained.TriangleCount}");
        }

        [Fact]
        public void SolidTouchingItselfAlongAnEdge_FourMeshes()
        {
            // two cubes in a checkerboard (touching along the edge x = y = 1) between two overlapping slabs, as
            // four meshes: the contact edge is split per pair of pieces (as for one or two meshes)
            var parts = new[] { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1), Box(0, 0, -1, 2, 2, 0.25), Box(0, 0, 0.75, 2, 2, 2) };
            var boolean = Check(new ExactMeshBoolean(parts, Ops.Union));
            Assert.Equal(2 * 4 * 1.25 + 2 * 0.5, Volume(boolean.Result), 9);
            Assert.Equal(1, boolean.SplitNonManifoldEdges);
            Assert.True(boolean.Result.CheckValidity(false, FailMode.ReturnOnly));
            // touching cubes only: two sheets, the edge vertices duplicated
            var touching = Check(new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1), Box(5, 5, 5, 6, 6, 6) }, Ops.Union));
            Assert.Equal(3.0, Volume(touching.Result), 12);
            Assert.Equal(1, touching.NonManifoldEdges);
            Assert.Equal(24, touching.Result.VertexCount);
        }

        [Fact]
        public void OpenMeshes_GeneralizedWindingInPredicate()
        {
            // with generalized winding numbers for all (closed) meshes the volumes stay the same
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(1, 0.5, 0.25, 3, 2.5, 1.75), c = Box(0.5, 1.25, -1, 1.75, 3, 1);
            var meshes = new[] { a, b, c };
            foreach (var predicate in new[] { ExactMeshBoolean.AtLeast(1), ExactMeshBoolean.AtLeast(2), ExactMeshBoolean.AtLeast(3) }) {
                var exact = Check(new ExactMeshBoolean(meshes, predicate));
                var general = new ExactMeshBoolean(meshes, predicate) { ForceGeneralizedWinding = true };
                Check(general);
                Assert.DoesNotContain(true, general.ExactWinding);
                Assert.True(exact.ExactWinding.All(e => e));
                Assert.Equal(Volume(exact.Result), Volume(general.Result), 12);
            }
            // an open box (no top) among closed ones: inside at least two
            DMesh3 open = Box(0, 0, 0, 2, 2, 2);
            foreach (int tid in open.TriangleIndices().Where(t => open.GetTriCentroid(t).z == 2).ToArray())
                open.RemoveTriangle(tid, true, false);
            var mixed = new ExactMeshBoolean(new[] { Box(0.5, 0.5, -0.5, 1.5, 1.5, 0.5), open, Box(0.5, 0.5, 0.25, 1.5, 1.5, 1) }, ExactMeshBoolean.AtLeast(2));
            Assert.True(mixed.Compute(), mixed.Error);
            Assert.Equal(new[] { true, false, true }, mixed.ExactWinding);
            // inside the open box above z = 0 and in one of the others: z in [0, 1]; or in both others: z in [0.25, 0.5]
            Assert.Equal(1.0, Volume(mixed.Result), 9);
        }

        [Fact]
        public void Status_And_Errors()
        {
            var notRun = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1) }, Ops.Union);
            Assert.Equal(Status.NotComputed, notRun.Status);
            // one mesh: resolve
            Check(notRun);
            Assert.Equal(1.0, Volume(notRun.Result), 12);

            var none = new ExactMeshBoolean(new DMesh3[0], Ops.Union);
            Assert.False(none.Compute());
            Assert.Equal(Status.InvalidInput, none.Status);
            Assert.Null(none.Result);

            var withNull = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), null, Box(0, 0, 0, 1, 1, 1) }, Ops.Union);
            Assert.False(withNull.Compute());
            Assert.Equal(Status.InvalidInput, withNull.Status);

            DMesh3 nan = Box(0, 0, 0, 1, 1, 1);
            nan.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            var nonFinite = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Box(0, 0, 0, 1, 1, 1), nan }, Ops.Union);
            Assert.False(nonFinite.Compute());
            Assert.Equal(Status.InvalidInput, nonFinite.Status);

            DMesh3 open = Box(0, 0, 0, 1, 1, 1);
            open.RemoveTriangle(open.TriangleIndices().First(), true, false);
            var openOp = new ExactMeshBoolean(new[] { Box(0, 0, 0, 1, 1, 1), Box(2, 2, 2, 3, 3, 3), open }, Ops.Union) { AllowOpenMeshes = false };
            Assert.False(openOp.Compute());
            Assert.Equal(Status.OpenInput, openOp.Status);
            Assert.Contains("closed", openOp.Error);

            // the two-mesh API reports the status as well
            var pair = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), null, Ops.Union);
            Assert.False(pair.Compute());
            Assert.Equal(Status.InvalidInput, pair.Status);
            var ok = new ExactMeshBoolean(Box(0, 0, 0, 1, 1, 1), Box(0.5, 0, 0, 1.5, 1, 1), Ops.Union);
            Assert.True(ok.Compute());
            Assert.Equal(Status.Success, ok.Status);
        }

        [Fact]
        public void DisjointMeshes_FarApart()
        {
            // many meshes, most pairs far apart (the arrangement intersects only meshes with overlapping boxes)
            var meshes = new List<DMesh3>();
            for (int k = 0; k < 40; ++k)
                meshes.Add(Box(3 * k, 0, 0, 3 * k + 1, 1, 1));
            meshes.Add(Box(0.5, 0.5, 0.5, 1.5, 1.5, 1.5));
            var boolean = Check(new ExactMeshBoolean(meshes, Ops.Union));
            Assert.Equal(40 + 1 - 0.125, Volume(boolean.Result), 12);
            Assert.True(boolean.IntersectingPairs > 0);
            Assert.Equal(1.0 / 8, Volume(Run(new[] { meshes[0], meshes[40] }, Ops.Intersection)), 12);
            Assert.Equal(0, Run(meshes, Ops.Intersection).TriangleCount);
        }
    }
}
