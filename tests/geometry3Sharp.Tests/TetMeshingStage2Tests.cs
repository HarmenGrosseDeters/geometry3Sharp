using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 2 of docs/design/TET_MESHING.md: TetMeshImprover (edge removal, face removal, smoothing after Klingner and Shewchuk 2007)
    /// keeps the mesh exactly valid and its boundary unchanged, and only ever raises the smallest dihedral angle of what it replaces.
    /// </summary>
    public class TetMeshingStage2Tests
    {
        static TetMesh3 FromTets(List<Vector3d> v, List<Index4i> t)
        {
            var m = new TetMesh3();
            m.Vertices.AddRange(v); m.Tets.AddRange(t);
            var faces = new Dictionary<(int, int, int), Index3i>();
            Func<int, int, int, (int, int, int)> canon = (a, b, c) => a <= b && a <= c ? (a, b, c) : b <= a && b <= c ? (b, c, a) : (c, a, b);
            var all = new HashSet<(int, int, int)>();
            foreach (Index4i q in t)
                for (int i = 0; i < 4; ++i) all.Add(canon(q[Delaunay3.FacetIndices[i, 0]], q[Delaunay3.FacetIndices[i, 1]], q[Delaunay3.FacetIndices[i, 2]]));
            foreach (var f in all)
                if (!all.Contains(canon(f.Item1, f.Item3, f.Item2))) m.BoundaryTriangles.Add(new Index3i(f.Item1, f.Item2, f.Item3));
            return m;
        }

        static DMesh3 Sphere(double r, int n) => new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        static DMesh3 Box() => new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.25, 0.125), new Vector3d(0.5, 0.25, 0.125)) }.Generate().MakeDMesh();
        static DMesh3 Torus(int major, int minor, double R, double r)
        {
            var m = new DMesh3();
            for (int i = 0; i < major; ++i)
                for (int j = 0; j < minor; ++j) {
                    double u = 2 * Math.PI * i / major, v = 2 * Math.PI * j / minor;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < major; ++i)
                for (int j = 0; j < minor; ++j) {
                    int a = i * minor + j, b = ((i + 1) % major) * minor + j, c = ((i + 1) % major) * minor + (j + 1) % minor, d = i * minor + (j + 1) % minor;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        static DMesh3 Input(string name)
        {
            switch (name) {
                case "sphere": return Sphere(1, 16);
                case "box": return Box();
                case "cylinder": return new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 32 }.Generate().MakeDMesh();
                default: return Torus(64, 16, 1, 0.1);
            }
        }
        public static IEnumerable<object[]> Inputs() => new[] { "sphere", "box", "cylinder", "torus thin" }.Select(n => new object[] { n });

        // the stage-2 invariants: exactly valid, boundary closed and free of self-intersections, input strictly inside (no boundary
        // triangle touches it, exact), every boundary vertex within BoundarySlideFactor offsets of the wrap surface
        static void Invariants(MeshTetrahedralization tm)
        {
            TetMesh3 t = tm.Result;
            Assert.Null(TetMeshQuality.NotValidReason(t));
            Assert.Null(TetOracle.Check(t, null));
            DMesh3 boundary = t.ToSurfaceMesh();
            Assert.False(MeshRepairMetrics.HasSelfIntersections(boundary));
            var tree = new DMeshAABBTree3(tm.Mesh, true);
            var tris = t.BoundaryTriangles.Select(f => new Triangle3d(t.Vertices[f.a], t.Vertices[f.b], t.Vertices[f.c])).ToList();
            Assert.False(Reducer.TrianglesTouch(tree, tris));
            Assert.True(MeshWindingNumberInside(tm.Mesh, boundary), "the input is not inside the boundary");
            var wrapTree = new DMeshAABBTree3(tm.Wrap.Result, true);
            foreach (int v in boundary.VertexIndices()) {
                Vector3d p = boundary.GetVertex(v);
                int tid = wrapTree.FindNearestTriangle(p);
                double dist = Math.Sqrt(MeshQueries.TriDistanceSqr(tm.Wrap.Result, tid, p));
                Assert.True(dist <= tm.BoundarySlideFactor * tm.UsedOffset * (1 + 1e-9), dist / tm.UsedOffset + " offsets");
            }
        }

        // every input vertex is inside the closed boundary (winding number of the boundary at the input vertices, about 1)
        static bool MeshWindingNumberInside(DMesh3 input, DMesh3 boundary)
        {
            foreach (int v in input.VertexIndices())
                if (Math.Abs(boundary.WindingNumber(input.GetVertex(v))) < 0.5) return false;
            return true;
        }

        [Theory]
        [MemberData(nameof(Inputs))]
        public void Improved_KeepsTheInvariants_AndRaisesTheWorstAngle(string name)
        {
            var plain = new MeshTetrahedralization(Input(name)) { Improve = false }; Assert.True(plain.Compute());
            var tm = new MeshTetrahedralization(Input(name)); Assert.True(tm.Compute(), tm.Status.ToString());
            Invariants(tm);
            double before = TetMeshQuality.Measure(plain.Result).MinDihedral.Worst, after = TetMeshQuality.Measure(tm.Result).MinDihedral.Worst;
            Assert.True(after > before || before >= 10 && after >= before, after + " vs " + before);
            // measured (TET_MESHING.md 15): sphere, box, cylinder >= 10 degrees on every tet; the thin torus 9.4
            Assert.True(after >= (name == "torus thin" ? 9 : 10), name + ": " + after);
            Assert.True(TetMeshQuality.Measure(tm.Result).MinDihedral.Worst1Percent >= 10);
            Assert.True(tm.Counts["improve_edge_removals"] + tm.Counts["improve_smooth_moves"] > 0 || before >= 10);
        }

        [Fact]
        public void BoundaryMoves_AreUsedAndRefusedWhereTheyWouldTouchTheInput()
        {
            var tm = new MeshTetrahedralization(Input("torus thin"));
            Assert.True(tm.Compute());
            // (refusals: the moves the input test stops on this fixture; 3 with the round-based carving of Alpha Wrap 3.0 lever (B),
            // #774, whose torus wrap differs, more with the serial carving: the bound only shows that the refusal path is taken)
            Assert.True(tm.Counts["improve_boundary_moves"] > 50 && tm.Counts["improve_boundary_refused_input"] >= 2,
                tm.Counts["improve_boundary_moves"] + " / " + tm.Counts["improve_boundary_refused_input"]);
            Invariants(tm);
            var fixedBoundary = new MeshTetrahedralization(Input("torus thin")) { BoundarySlideFactor = 0 };
            Assert.True(fixedBoundary.Compute());
            Assert.Equal(0, fixedBoundary.Counts["improve_boundary_moves"]);
            Assert.True(TetMeshQuality.Measure(tm.Result).MinDihedral.Worst > TetMeshQuality.Measure(fixedBoundary.Result).MinDihedral.Worst);
            var tangent = new MeshTetrahedralization(Input("torus thin")) { BoundaryMovesTangentOnly = true };
            Assert.True(tangent.Compute());
            Invariants(tangent);
            Assert.NotEqual(tm.Result.Vertices, tangent.Result.Vertices);   // the switch changes the moves
        }

        [Fact]
        public void BoundaryDisplacement_IsCumulativelyCapped_FromTheOriginalWrapPosition()
        {
            // many passes, so that repeated moves of one vertex would add up if the cap were per move: every boundary vertex must
            // still be within BoundarySlideFactor offsets of a wrap vertex (its original position; wrap vertices are at least one
            // offset apart, so the nearest one within half an offset is that vertex)
            var tm = new MeshTetrahedralization(Input("torus thin"));
            Assert.True(tm.Compute());
            Assert.True(tm.Counts["improve_passes"] >= 3 && tm.Counts["improve_boundary_moves"] > 50);
            double cap = tm.BoundarySlideFactor * tm.UsedOffset;
            var wrapVerts = tm.Wrap.Result.Vertices().ToList();
            var boundaryVerts = new HashSet<int>(tm.Result.BoundaryTriangles.SelectMany(f => new[] { f.a, f.b, f.c }));
            int moved = 0;
            foreach (int v in boundaryVerts) {
                Vector3d p = tm.Result.Vertices[v];
                double d = wrapVerts.Min(w => w.Distance(p));
                Assert.True(d <= cap * (1 + 1e-12), d / tm.UsedOffset + " offsets");
                if (d > 0.25 * cap) moved++;
            }
            Assert.True(moved > 10, moved.ToString());            // moves do use the budget
        }

        [Fact]
        public void ReuseAndRepeatedRuns_GiveTheSameTets()
        {
            var a = new MeshTetrahedralization(Input("torus thin")); Assert.True(a.Compute());
            TetMesh3 r1 = a.Result;
            Assert.True(a.Compute());
            var b = new MeshTetrahedralization(Input("torus thin")); Assert.True(b.Compute());
            foreach (TetMesh3 r in new[] { a.Result, b.Result }) {
                Assert.Equal(r1.Vertices, r.Vertices); Assert.Equal(r1.Tets, r.Tets); Assert.Equal(r1.BoundaryTriangles, r.BoundaryTriangles);
            }
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 input = Torus(48, 12, 1, 0.15);
            var a = new MeshTetrahedralization(input) { RelativeAlpha = 12, RelativeOffset = 200 }; Assert.True(a.Compute());
            var b = new MeshTetrahedralization(Invariance.Transformed(input, s)) { RelativeAlpha = 12, RelativeOffset = 200 }; Assert.True(b.Compute());
            Invariants(b);
            Assert.True(a.Counts["improve_edge_removals"] > 0 && a.Counts["improve_boundary_moves"] > 0,
                a.Counts["improve_edge_removals"] + " / " + a.Counts["improve_smooth_moves"] + " / " + a.Counts["improve_boundary_moves"]);   // stage 2 does work here
            if (s.IsExact) {
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                for (int i = 0; i < a.Result.Vertices.Count; ++i) Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else
                Assert.InRange(TetMeshQuality.Measure(b.Result).MinDihedral.Worst, 0.5 * TetMeshQuality.Measure(a.Result).MinDihedral.Worst, 90);
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Fact]
        public void EdgeRemoval_ThreeTetsAroundAnEdgeBecomeTwo()
        {
            // the inverse of the face-removal case: three tets around the edge (d, e) through an equilateral triangle, with d and e
            // at 0.6 of its side from it: 38.7 degrees; the 3-2 flip (edge removal, m = 3) gives two tets of 64.3 degrees
            double s = Math.Sqrt(3) / 2;
            var v = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, s, 0),
                                         new Vector3d(0.5, s / 3, -0.6), new Vector3d(0.5, s / 3, 0.6) };
            var t = new List<Index4i> { new Index4i(1, 0, 4, 3), new Index4i(2, 1, 4, 3), new Index4i(0, 2, 4, 3) };
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            double before = TetMeshQuality.Measure(m).MinDihedral.Worst;
            var imp = new TetMeshImprover(m) { TargetDegrees = 60 };
            imp.Run();
            TetMesh3 r = imp.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Equal(1, imp.Counts["edge_removals"]);
            Assert.Equal(2, r.Tets.Count);
            Assert.True(TetMeshQuality.Measure(r).MinDihedral.Worst > before + 20);
            Assert.Equal(m.BoundaryTriangles.Count, r.BoundaryTriangles.Count);
        }

        [Fact]
        public void Smoothing_MovesAnInteriorVertexToABetterPlace()
        {
            // a regular octahedron split at an interior vertex placed off-centre: smoothing moves it towards the centre
            var v = new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0),
                new Vector3d(0, 0, 1), new Vector3d(0, 0, -1), new Vector3d(0.6, 0.1, 0.05) };
            int[][] faces = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            var t = faces.Select(f => new Index4i(f[0], f[2], f[1], 6)).ToList();   // the faces are outward, the centre behind them
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            double before = TetMeshQuality.Measure(m).MinDihedral.Worst;
            var imp = new TetMeshImprover(m) { TargetDegrees = 60 };
            imp.Run();
            TetMesh3 r = imp.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.True(imp.Counts["smooth_moves"] > 0);
            Assert.True(TetMeshQuality.Measure(r).MinDihedral.Worst > before + 5, before + " -> " + TetMeshQuality.Measure(r).MinDihedral.Worst);
            for (int i = 0; i < 6; ++i) Assert.Contains(v[i], r.Vertices);            // boundary vertices did not move (no input tree)
        }

        [Fact]
        public void Smoothing_LeavesAnOptimalVertexAlone()
        {
            // the octahedron split at its centre: no move can raise the worst angle, so nothing moves
            var v = new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0),
                new Vector3d(0, 0, 1), new Vector3d(0, 0, -1), Vector3d.Zero };
            int[][] faces = { new[] { 0, 2, 4 }, new[] { 2, 1, 4 }, new[] { 1, 3, 4 }, new[] { 3, 0, 4 }, new[] { 2, 0, 5 }, new[] { 1, 2, 5 }, new[] { 3, 1, 5 }, new[] { 0, 3, 5 } };
            TetMesh3 m = FromTets(v, faces.Select(f => new Index4i(f[0], f[2], f[1], 6)).ToList());
            var imp = new TetMeshImprover(m) { TargetDegrees = 80 };
            imp.Run();
            Assert.Equal(0, imp.Counts["smooth_moves"]);
            Assert.True(new HashSet<Vector3d>(m.Vertices).SetEquals(imp.Result().Vertices));   // (Result renumbers the vertices)
        }

        [Fact]
        public void BoundaryMove_IntoAnotherPartOfTheBoundary_IsRefused()
        {
            // two separate tets 0.1 apart (two boundary components) and a far-away input: moving a vertex of the first through the
            // face of the second cuts the second's boundary; a small move away from it is allowed
            var v = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1),
                new Vector3d(1.1, 0, 0), new Vector3d(2.1, 0, 0), new Vector3d(1.1, 1, 0), new Vector3d(1.1, 0, 1) };
            var t = new List<Index4i> { new Index4i(0, 1, 2, 3), new Index4i(4, 5, 6, 7) };
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            var far = new DMesh3(); far.AppendVertex(new Vector3d(10, 10, 10)); far.AppendVertex(new Vector3d(11, 10, 10)); far.AppendVertex(new Vector3d(10, 11, 10));
            far.AppendTriangle(0, 1, 2);
            var imp = new TetMeshImprover(m) { InputTree = new DMeshAABBTree3(far, true), BoundarySlideLimit = 10 };
            Assert.False(imp.BoundaryMoveAllowed(1, new Vector3d(1.3, 0.05, 0.05)));     // vertex 1 into the second tet
            Assert.True(imp.BoundaryMoveAllowed(1, new Vector3d(0.95, 0.0, 0.0)));
            // and a move whose sweep crosses the input is refused for the input
            var near = new DMesh3(); near.AppendVertex(new Vector3d(0.5, -1, 0.2)); near.AppendVertex(new Vector3d(0.5, 1, 0.2)); near.AppendVertex(new Vector3d(0.5, 0, -1));
            near.AppendTriangle(0, 1, 2);
            var imp2 = new TetMeshImprover(m) { InputTree = new DMeshAABBTree3(near, true), BoundarySlideLimit = 10 };
            Assert.False(imp2.BoundaryMoveAllowed(0, new Vector3d(0.6, 0.1, 0.1)));
        }

        [Fact]
        public void EdgeRemoval_RingOfFour_FindsTheBetterTriangulation()
        {
            // four tets around an interior edge (a, b) whose worst tet has 12.0 degrees; the best triangulation of the ring gives
            // four tets of 42.6 degrees (found by a search over random rings; the DP over m = 4 with its split and recursion)
            var v = new List<Vector3d> { new Vector3d(0.2, 0.3, 0.96), new Vector3d(0.29, 0.27, -0.73),
                new Vector3d(0.62, 0, 0.06), new Vector3d(0, -0.76, -0.07), new Vector3d(-0.66, 0, -0.03), new Vector3d(0, 0.7, -0.02) };
            var t = new List<Index4i>();
            for (int k = 0; k < 4; ++k) t.Add(new Index4i(0, 1, 2 + k, 2 + (k + 1) % 4));
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            double before = TetMeshQuality.Measure(m).MinDihedral.Worst;
            Assert.InRange(before, 11.9, 12.1);
            var imp = new TetMeshImprover(m) { TargetDegrees = 40 };
            imp.Run();
            TetMesh3 r = imp.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.True(imp.Counts["edge_removals"] >= 1);
            Assert.True(TetMeshQuality.Measure(r).MinDihedral.Worst > 40, TetMeshQuality.Measure(r).MinDihedral.Worst.ToString());
            Assert.True(new HashSet<(Vector3d, Vector3d, Vector3d)>(m.BoundaryTriangles.Select(f => Key(m.Vertices, f))).SetEquals(r.BoundaryTriangles.Select(f => Key(r.Vertices, f))));
        }

        // a boundary triangle by its corner positions, independent of the vertex numbering
        static (Vector3d, Vector3d, Vector3d) Key(List<Vector3d> vs, Index3i f)
        {
            var p = new[] { vs[f.a], vs[f.b], vs[f.c] }.OrderBy(x => x.x).ThenBy(x => x.y).ThenBy(x => x.z).ToArray();
            return (p[0], p[1], p[2]);
        }

        [Fact]
        public void EdgeRemoval_NeverOnAnOpenRing()
        {
            // three of the four tets of the case above: (a, b) is a boundary edge with an open ring; removing it would change the
            // boundary, so it is never removed, whatever it would gain
            var v = new List<Vector3d> { new Vector3d(0.2, 0.3, 0.96), new Vector3d(0.29, 0.27, -0.73),
                new Vector3d(0.62, 0, 0.06), new Vector3d(0, -0.76, -0.07), new Vector3d(-0.66, 0, -0.03), new Vector3d(0, 0.7, -0.02) };
            var t = new List<Index4i>();
            for (int k = 0; k < 3; ++k) t.Add(new Index4i(0, 1, 2 + k, 2 + (k + 1) % 4));
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            var imp = new TetMeshImprover(m) { TargetDegrees = 80 };
            imp.Run();
            TetMesh3 r = imp.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Equal(0, imp.Counts["edge_removals"]);
            Assert.True(new HashSet<(Vector3d, Vector3d, Vector3d)>(m.BoundaryTriangles.Select(f => Key(m.Vertices, f))).SetEquals(r.BoundaryTriangles.Select(f => Key(r.Vertices, f))));
        }

        [Fact]
        public void BoundaryMoveAllowed_AgreesWithABruteForceSelfIntersectionTest()
        {
            // random moves of boundary vertices of a refined sphere (input far away): the improver's grid-based exact test must
            // say "allowed" exactly when the moved boundary has no self-intersection (brute force over all boundary pairs)
            var tm = new MeshTetrahedralization(Sphere(1, 6)) { Improve = false, RelativeAlpha = 6, RelativeOffset = 60 };
            Assert.True(tm.Compute());
            TetMesh3 t = tm.Result;
            var far = new DMesh3(); far.AppendVertex(new Vector3d(50, 50, 50)); far.AppendVertex(new Vector3d(51, 50, 50)); far.AppendVertex(new Vector3d(50, 51, 50));
            far.AppendTriangle(0, 1, 2);
            double edge = t.BoundaryTriangles.Average(f => t.Vertices[f.a].Distance(t.Vertices[f.b]));
            double limit = 1.5 * edge;
            // small grid cells, no always-checked list: the grid alone must find every pair (a stale box would miss some)
            var imp = new TetMeshImprover(t) { InputTree = new DMeshAABBTree3(far, true), BoundarySlideLimit = limit, GridCell = 0.25 * edge, MaxCellsPerFace = int.MaxValue };
            var bverts = t.BoundaryTriangles.SelectMany(f => new[] { f.a, f.b, f.c }).Distinct().OrderBy(x => x).ToList();
            var cur = new List<Vector3d>(t.Vertices);
            var rnd = new Random(41);
            int refused = 0, allowed = 0;
            for (int k = 0; k < 1500; ++k) {
                // a move within the cap of the vertex's FIRST position; allowed moves are kept, so later checks run on a boundary
                // whose vertices are displaced (where a grid built on old positions would be stale)
                int v = bverts[rnd.Next(bverts.Count)];
                Vector3d d = new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5);
                Vector3d p = t.Vertices[v] + (limit * rnd.NextDouble() / d.Length) * d;
                var moved = new TetMesh3();
                moved.Vertices.AddRange(cur); moved.Vertices[v] = p; moved.BoundaryTriangles.AddRange(t.BoundaryTriangles);
                bool self = MeshRepairMetrics.HasSelfIntersections(moved.ToSurfaceMesh());
                Assert.Equal(!self, imp.BoundaryMoveAllowed(v, p));
                if (self) refused++;
                else { allowed++; cur[v] = p; imp.SetPosition(v, p); }
            }
            Assert.True(refused > 50 && allowed > 100, refused + " / " + allowed);
        }

        [Fact]
        public void BoundaryDisplacement_NeverExceedsTheCap_FromTheFirstPosition()
        {
            // the improver itself, many passes, a small cap: no boundary vertex ever gets farther than the cap from its first
            // position (a per-move cap would let repeated moves add up)
            var tm = new MeshTetrahedralization(Input("torus thin")) { Improve = false };
            Assert.True(tm.Compute());
            double cap = 0.05 * tm.UsedOffset;
            var imp = new TetMeshImprover(tm.Result) { InputTree = new DMeshAABBTree3(tm.Mesh, true), BoundarySlideLimit = cap, MaxPasses = 30 };
            imp.Run();
            Assert.True(imp.Counts["boundary_moves"] > 50 && imp.Counts["passes"] >= 3, imp.Counts["boundary_moves"] + " / " + imp.Counts["passes"]);
            double d = imp.MaxDisplacement();
            Assert.True(d <= cap * (1 + 1e-12), d / cap + " caps");
            Assert.True(d > 0.75 * cap, d / cap + " caps");                 // the budget is used
            Assert.Null(TetMeshQuality.NotValidReason(imp.Result()));
        }

        [Fact]
        public void FaceRemoval_TwoFlatTetsBecomeThree()
        {
            // an equilateral facet with its two apexes at 0.2 of its side: 34.7 degrees; the 2-3 flip gives three tets of 60.5 degrees
            double s = Math.Sqrt(3) / 2;
            var v = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, s, 0),
                                         new Vector3d(0.5, s / 3, -0.2), new Vector3d(0.5, s / 3, 0.2) };
            // facet (0, 1, 2) has normal +z; e = 3 behind, d = 4 in front: tets (0, 2, 1, 3) and (0, 1, 2, 4) are positive
            var t = new List<Index4i> { new Index4i(0, 2, 1, 3), new Index4i(0, 1, 2, 4) };
            TetMesh3 m = FromTets(v, t);
            Assert.Null(TetMeshQuality.NotValidReason(m));
            double before = TetMeshQuality.Measure(m).MinDihedral.Worst;
            var imp = new TetMeshImprover(m) { TargetDegrees = 60 };
            imp.Run();
            TetMesh3 r = imp.Result();
            Assert.Null(TetMeshQuality.NotValidReason(r));
            Assert.Equal(1, imp.Counts["face_removals"]);
            Assert.Equal(3, r.Tets.Count);
            Assert.True(TetMeshQuality.Measure(r).MinDihedral.Worst > before);
            Assert.Equal(m.BoundaryTriangles.Count, r.BoundaryTriangles.Count);
        }
    }
}
