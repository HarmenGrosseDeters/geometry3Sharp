using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using g3;
using g3.Examples;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Runs the examples in docs/examples (linked into this project) and checks the behaviour that
    /// docs/OPERATIONS.md and the examples' comments promise. Each example class needs a test here whose
    /// name starts with the class name, and a link from docs/OPERATIONS.md (see EveryExample_IsTestedAndLinked).
    /// </summary>
    public class DocExamplesTests
    {
        [Fact]
        public void EveryExample_IsTestedAndLinked()
        {
            Type[] examples = typeof(MeshIOExample).Assembly.GetTypes()
                .Where(t => t.Namespace == "g3.Examples" && t.IsClass && t.IsAbstract && t.IsSealed && t.IsPublic).ToArray();
            Assert.True(examples.Length >= 10, $"only {examples.Length} examples found");
            string[] tests = typeof(DocExamplesTests).GetMethods()
                .Where(m => m.GetCustomAttribute<FactAttribute>() != null).Select(m => m.Name).ToArray();
            string docsDir = FindDocsDir();
            string overview = File.ReadAllText(Path.Combine(docsDir, "OPERATIONS.md"));
            string[] files = Directory.GetFiles(Path.Combine(docsDir, "examples"), "*.cs").Select(Path.GetFileNameWithoutExtension).ToArray();
            foreach (Type t in examples) {
                Assert.True(tests.Any(n => n.StartsWith(t.Name + "_")), $"no test for {t.Name}");
                Assert.True(overview.Contains("examples/" + t.Name + ".cs"), $"docs/OPERATIONS.md does not link {t.Name}");
                Assert.Contains(t.Name, files);
            }
            Assert.Equal(files.Length, examples.Length);
        }

        [Fact]
        public void TerrainSlopeExample_PitWithCatchLineAndComposite()
        {
            var d = TerrainSlopeExample.Run();
            Assert.Equal(gs.TerrainSlopeDesign.Statuses.Success, d.Status);
            Assert.True(d.IsComplete);
            Assert.Single(d.CatchLines);
            Assert.True(d.CatchLines[0].Closed);
            Assert.True(d.CutVolume > 0 && d.FillVolume == 0);
            Assert.True(d.Composite.IsClosed() == false && d.Composite.TriangleCount > 800);
            Assert.True(d.MaxSlopeRatio <= 1);
            // the same at UTM in a local frame: local outputs, the origin near the pit, the same catch line count
            var u = TerrainSlopeExample.Run(atUtm: true);
            Assert.Equal(gs.TerrainSlopeDesign.Statuses.Success, u.Status);
            Assert.False(u.LocalFrameFallback);
            Assert.True(u.UsedOrigin.Distance(new Vector2d(632117.25, 5712043.5)) < 1, u.UsedOrigin.ToString());
            Assert.True(u.Composite.GetBounds().Max.xy.Length < 100);
            Assert.True(u.UsedMinFeatureSize < 1e-3 * 1.5e-2);
            Assert.Single(u.CatchLines);
        }

        [Fact]
        public void QuadtreeQueriesExample_ReturnsInterpolatedHeight()
        {
            Assert.Equal(3.0, QuadtreeQueriesExample.Run(), 12);
        }

        static string FindDocsDir()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
                string docs = Path.Combine(dir.FullName, "docs");
                if (File.Exists(Path.Combine(docs, "OPERATIONS.md")))
                    return docs;
            }
            throw new DirectoryNotFoundException("docs/OPERATIONS.md not found above " + AppContext.BaseDirectory);
        }

        static DMesh3 Sphere(double radius, Vector3d center, int edgeVertices = 12)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        /// <summary> flat n x n grid of quads over [0,size]^2 at z = 0, with an open boundary </summary>
        static DMesh3 Grid(int n, double size)
        {
            var mesh = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    mesh.AppendVertex(new Vector3d(size * i / n, size * j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    mesh.AppendTriangle(a, b, d);
                    mesh.AppendTriangle(a, d, c);
                }
            return mesh;
        }

        static double AverageEdgeLength(DMesh3 mesh)
        {
            double min, max, avg;
            MeshQueries.EdgeLengthStats(mesh, out min, out max, out avg);
            return avg;
        }

        static double Volume(DMesh3 mesh)
        {
            return MeshMeasurements.VolumeArea(mesh, mesh.TriangleIndices(), mesh.GetVertex).x;
        }


        [Fact]
        public void MeshConstructionExample_BuildsClosedMeshes()
        {
            DMesh3 tet = MeshConstructionExample.Tetrahedron();
            Assert.Equal(4, tet.TriangleCount);
            Assert.True(tet.IsClosed());
            Assert.True(tet.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(1.0 / 6.0, Volume(tet), 12);   // positive: outward orientation

            DMesh3 box = MeshConstructionExample.Box();
            Assert.True(box.IsClosed());
            Assert.Equal(6 * 4 * 4 * 2, box.TriangleCount);
            Assert.Equal(8.0, Volume(box), 9);
        }

        [Fact]
        public void MeshIOExample_RoundTripsPlyThreeMfGlb()
        {
            string dir = MeshFormatTestUtil.TempDir();
            try {
                Dictionary<string, DMesh3> meshes = MeshIOExample.Run(dir);
                DMesh3 reference = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
                MeshTransforms.Translate(reference, new Vector3d(500000, 5000000, 30));
                foreach (string ext in new[] { "ply", "3mf", "glb" }) {
                    DMesh3 m = meshes[ext];
                    Assert.Equal(reference.TriangleCount, m.TriangleCount);
                    Assert.Equal(reference.VertexCount, m.VertexCount);
                    Assert.True(m.IsClosed(), ext);
                    // the large offset survives: PLY stores doubles, GLB stores float offsets from the node translation
                    double maxErr = 0;
                    AxisAlignedBox3d b0 = reference.CachedBounds, b1 = m.CachedBounds;
                    maxErr = Math.Max((b0.Min - b1.Min).Length, (b0.Max - b1.Max).Length);
                    Assert.True(maxErr < 1e-3, $"{ext}: bounds differ by {maxErr}");
                }
                Assert.True(meshes["ply"].HasVertexNormals);
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void MeshIOExample_Float32RoundTrip_RecentringKeepsPrecision()
        {
            string dir = MeshFormatTestUtil.TempDir();
            try {
                var (direct, recentred) = MeshIOExample.Float32RoundTrip(dir);
                // at 5e6 the float32 spacing is 0.5, so the direct round trip is off by far more than a recentred one
                Assert.True(direct > 0.1, $"direct error {direct}");
                Assert.True(recentred < 1e-5, $"recentred error {recentred}");
            } finally {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void LazyExactMeshExample_ExactVolumeAndClean()
        {
            // the cube minus the part of the hole inside it: 1 - 0.25 * 0.25 * 1
            string error;
            DMesh3 result = LazyExactMeshExample.Run(out error);
            Assert.True(result != null, error);
            Assert.Null(error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
            Assert.Equal(1 - 0.0625, Volume(result), 12);
        }

        [Fact]
        public void BooleanVolumeGradientExample_RealizedAndOtherOneSidedDerivative()
        {
            double[] r = g3.Examples.BooleanVolumeGradientExample.TouchingBoxes(out string error);
            Assert.True(r != null, error);
            Assert.Equal(1.25, r[0], 12);     // volume of the union
            Assert.Equal(0.25, r[1], 12);     // the realized branch: the union merges the touching boxes
            Assert.Equal(0.0, r[2], 12);      // the other branch: separated
        }

        [Fact]
        public void BooleanVolumeOptimizerExample_GrowsTheUnionToTheTargetVolume()
        {
            // (the optimizer's convergence, invariance, determinism and failures: BooleanVolumeOptimizerTests)
            var r = g3.Examples.BooleanVolumeOptimizerExample.GrowUnion();
            Assert.True(r.Reason == g3.Examples.BooleanVolumeOptimizerExample.StopReason.Converged, r.Error);
            Assert.Equal(1.3, r.Iterations[r.Iterations.Count - 1].Volume, 8);
            Assert.True(r.Final.IsClosed());
            Assert.Equal(1.3, Volume(r.Final), 6);
        }

        [Fact]
        public void ExactMeshBooleanExample_ExactVolumesAndClosedResults()
        {
            // cube 1, bar 0.5 x 0.5 x 2 of which 0.25 lies inside the cube
            var expected = new Dictionary<ExactMeshBoolean.Operations, double> {
                { ExactMeshBoolean.Operations.Union, 1 + 0.5 - 0.25 },
                { ExactMeshBoolean.Operations.Intersection, 0.25 },
                { ExactMeshBoolean.Operations.Difference, 0.75 },
            };
            foreach (var kv in expected) {
                string error;
                DMesh3 result = ExactMeshBooleanExample.Run(kv.Key, out error);
                Assert.True(result != null, error);
                Assert.Null(error);
                Assert.True(result.IsClosed(), kv.Key.ToString());
                Assert.True(result.CheckValidity(true, FailMode.ReturnOnly));
                Assert.Equal(kv.Value, Volume(result), 12);
            }
        }

        [Fact]
        public void ExactMeshBooleanExample_NearlyCoincidentUnionIsClean()
        {
            bool clean;
            DMesh3 result = ExactMeshBooleanExample.NearlyCoincidentUnion(out clean);
            Assert.True(clean);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
            Assert.Equal(1.0, Volume(result), 9);
        }

        [Fact]
        public void ExactMeshBooleanExample_AtLeastTwoOfThree()
        {
            ExactMeshBoolean.BooleanStatus status;
            DMesh3 result = ExactMeshBooleanExample.AtLeastTwoOfThree(out status);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, status);
            Assert.True(result.IsClosed());
            // x in [0.5, 1.5] is inside two of the boxes
            Assert.Equal(1.0, Volume(result), 12);
        }

        [Fact]
        public void UnionTreeExample_TreeAndOneCallAgreeInVolume()
        {
            List<DMesh3> balls = UnionTreeExample.MakeBalls();
            DMesh3 one = UnionTreeExample.UnionOneCall(balls);
            DMesh3 tree = UnionTreeExample.UnionTree(balls);
            Assert.NotNull(one);
            Assert.NotNull(tree);
            foreach (DMesh3 m in new[] { one, tree }) {
                Assert.True(m.IsClosed());
                Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            }
            Assert.True(Volume(one) > Volume(balls[0]) && Volume(one) < 8 * Volume(balls[0]));
            Assert.Equal(Volume(one), Volume(tree), 9);
            // 8 balls: the boxes of all overlap all 7 others, but a ball overlaps only 6 (the opposite corner is 1.3 * sqrt(3) = 2.25 > 2
            // away): the box degree over-counts
            Assert.Equal(7.0, UnionTreeExample.MeanBoxOverlapDegree(balls), 12);
            int ballPairs = 0;
            for (int i = 0; i < balls.Count; ++i)
                for (int j = i + 1; j < balls.Count; ++j)
                    if (balls[i].GetBounds().Center.Distance(balls[j].GetBounds().Center) < 2.0)
                        ++ballPairs;
            Assert.Equal(6.0, 2.0 * ballPairs / balls.Count, 12);
        }

        [Fact]
        public void WindingNumberSolidifyExample_ClosesAndUnitesBrokenInput()
        {
            DMesh3 result = WindingNumberSolidifyExample.Run();
            Assert.NotNull(result);
            Assert.True(result.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
            // the union of the two unit spheres, minus a few percent for the grid
            double h = (2 - 1.2) / 2, cap = Math.PI * h * h * (3 - h) / 3;
            double union = 2 * 4.0 / 3 * Math.PI - 2 * cap;
            Assert.InRange(Volume(result), 0.95 * union, 1.03 * union);
        }

        [Fact]
        public void MeshRepairExample_AutoRepairClosesHoles()
        {
            DMesh3 input = Sphere(1, Vector3d.Zero);
            foreach (int tid in input.TriangleIndices().Take(6).ToArray())
                input.RemoveTriangle(tid);
            int inputTriangles = input.TriangleCount;
            Assert.False(input.IsClosed());

            DMesh3 repaired = MeshRepairExample.AutoRepair(input);
            Assert.True(repaired.IsClosed());
            Assert.Equal(inputTriangles, input.TriangleCount);      // the example repairs a copy
            Assert.True(repaired.HasVertexNormals);
            Assert.InRange(Volume(repaired), 0.9 * 4.0 / 3 * Math.PI, 4.0 / 3 * Math.PI);
        }

        [Fact]
        public void MeshRepairExample_RobustRepairKeepsCleanInputAndResolvesOverlaps()
        {
            bool usedExact, usedSolidify;
            string reason;
            DMesh3 clean = Sphere(1, Vector3d.Zero);
            DMesh3 result = MeshRepairExample.RobustRepair(clean, out usedExact, out usedSolidify, out reason);
            Assert.False(usedExact);
            Assert.False(usedSolidify);
            Assert.Equal("", reason);
            Assert.Equal(clean.TriangleCount, result.TriangleCount);

            // overlapping spheres: self-intersecting, resolved exactly (no remeshing)
            DMesh3 overlapping = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(overlapping, Sphere(1, new Vector3d(1.2, 0, 0)));
            result = MeshRepairExample.RobustRepair(overlapping, out usedExact, out usedSolidify, out reason);
            Assert.True(usedExact);
            Assert.False(usedSolidify);
            Assert.Equal("self-intersections", reason);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
        }

        [Fact]
        public void HoleFillExample_FillsAllHoles()
        {
            DMesh3 mesh = Sphere(1, Vector3d.Zero);
            // two separate holes
            mesh.RemoveTriangle(mesh.TriangleIndices().First());
            Vector3d far = new Vector3d(0, 0, -1);
            mesh.RemoveTriangle(mesh.TriangleIndices().OrderBy(t => mesh.GetTriCentroid(t).Distance(far)).First());
            Assert.Equal(2, new MeshBoundaryLoops(mesh).Count);

            Assert.Equal(0, HoleFillExample.Run(mesh));
            Assert.True(mesh.IsClosed());
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
        }

        [Fact]
        public void RepairMetricsExample_ReportsDefects()
        {
            RepairMetricsExample.Report clean = RepairMetricsExample.Check(Sphere(1, Vector3d.Zero));
            Assert.True(clean.Closed);
            Assert.Equal(0, clean.BoundaryEdges);
            Assert.Equal(0, clean.SelfIntersections);
            Assert.Equal(0, clean.FoldedEdges);
            Assert.Equal("", clean.NotCleanReason);

            DMesh3 overlapping = Sphere(1, Vector3d.Zero);
            MeshEditor.Append(overlapping, Sphere(1, new Vector3d(1.2, 0, 0)));
            RepairMetricsExample.Report bad = RepairMetricsExample.Check(overlapping);
            Assert.True(bad.Closed);
            Assert.True(bad.SelfIntersections > 0);
            Assert.NotEqual("", bad.NotCleanReason);

            DMesh3 open = Grid(4, 1);
            Assert.Equal(16, RepairMetricsExample.Check(open).BoundaryEdges);
            Assert.False(RepairMetricsExample.Check(open).Closed);

            DMesh3 s = Sphere(1, Vector3d.Zero);
            Assert.InRange(RepairMetricsExample.VolumeIoU(s, new DMesh3(s)), 0.999, 1.0);
            Assert.InRange(RepairMetricsExample.VolumeIoU(s, Sphere(0.5, Vector3d.Zero)), 0.1, 0.14);   // (0.5)^3 = 0.125
        }

        [Fact]
        public void FidelityMetricsExample_SeesWhatHausdorffMisses()
        {
            // the same sphere with a reversed cap: Hausdorff 0, but a share of reversed normals
            DMesh3 s = Sphere(1, Vector3d.Zero);
            DMesh3 flipped = new DMesh3(s);
            foreach (int tid in flipped.TriangleIndices().ToArray())
                if (flipped.GetTriCentroid(tid).z > 0.5) flipped.ReverseTriOrientation(tid);
            FidelityMetricsExample.Report r = FidelityMetricsExample.Compare(s, flipped);
            Assert.Equal(1.0, r.FScore);
            Assert.InRange(r.Reversed, 0.15, 0.35);
            Assert.Equal(MeshTopologySummary.Changes.None, r.TopologyChanges);
            Assert.True(r.MinAngle1Percent > 20);
            // the reversed cap encloses nothing of its own: the solid is the sphere's less the cap; its mean curvature
            // has the other sign there (H * size -2 instead of 2 on about a quarter of the surface)
            Assert.InRange(r.OuterVolumeIoU, 0.5, 0.99);
            Assert.InRange(r.MeanCurvatureError, 0.5, 1.5);
            Assert.True(FidelityMetricsExample.Compare(s, new DMesh3(s)).MeanCurvatureError < 1e-9);
            // a box against a sphere: its sharp edges are lost
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, 0.6 * Vector3d.One) }.Generate().MakeDMesh();
            FidelityMetricsExample.Report lost = FidelityMetricsExample.Compare(box, s);
            Assert.True(double.IsPositiveInfinity(lost.EdgeChamfer));
            Assert.Equal(0.0, lost.EdgeRecall);
            Assert.True(lost.FScore < 0.5);
            Assert.InRange(lost.OuterVolumeIoU, 0.3, 0.9);
            Assert.Null(FidelityMetricsExample.Compare(box, new DMesh3()));
        }

        [Fact]
        public void RemeshExample_ReachesTargetEdgeLengthAndKeepsBoundary()
        {
            DMesh3 mesh = Grid(10, 10);     // edge length 1 (diagonals 1.41)
            var boundary = mesh.VertexIndices().Where(mesh.IsBoundaryVertex).ToDictionary(v => v, v => mesh.GetVertex(v));
            RemeshExample.Run(mesh, 0.5);
            Assert.InRange(AverageEdgeLength(mesh), 0.4, 0.6);
            Assert.True(mesh.TriangleCount > 2 * 200);
            foreach (var kv in boundary)
                Assert.Equal(kv.Value, mesh.GetVertex(kv.Key));     // fixed boundary edges: unchanged
            Assert.All(mesh.VertexIndices(), v => Assert.Equal(0, mesh.GetVertex(v).z, 9));   // projected onto the plane

            DMesh3 sphere = Sphere(1, Vector3d.Zero);
            RemeshExample.Run(sphere, 0.1);
            Assert.True(sphere.IsClosed());
            Assert.InRange(AverageEdgeLength(sphere), 0.08, 0.12);
            Assert.All(sphere.VertexIndices(), v => Assert.InRange(sphere.GetVertex(v).Length, 0.97, 1.01));
        }

        [Fact]
        public void SimplificationExample_ReducesToCountAndRespectsMaxDeviation()
        {
            DMesh3 mesh = Sphere(1, Vector3d.Zero, 20);
            SimplificationExample.ToTriangleCount(mesh, 500);
            Assert.True(mesh.TriangleCount <= 500);
            Assert.True(mesh.TriangleCount > 400);
            Assert.True(mesh.IsClosed());
            Assert.Equal(0, MeshRepairMetrics.SelfIntersections(mesh));

            DMesh3 original = Sphere(1, Vector3d.Zero, 20);
            DMesh3 reduced = new DMesh3(original);
            SimplificationExample.WithMaxDeviation(reduced, 0.01);
            Assert.True(reduced.TriangleCount < original.TriangleCount / 2);
            var h = new MeshHausdorffDistance(reduced, original) { Symmetric = false, Tolerance = 1e-5 };
            Assert.True(h.Compute());
            Assert.True(h.LowerBound <= 0.01 + 1e-9, $"deviation {h.LowerBound}");
        }

        [Fact]
        public void SmoothingExample_RemovesNoiseAndKeepsBoundary()
        {
            foreach (bool laplacian in new[] { false, true }) {
                DMesh3 mesh = Grid(20, 1);
                var rand = new Random(1);
                foreach (int vid in mesh.VertexIndices().ToArray())
                    if (!mesh.IsBoundaryVertex(vid))
                        mesh.SetVertex(vid, mesh.GetVertex(vid) + new Vector3d(0, 0, 0.02 * (rand.NextDouble() - 0.5)));
                var boundary = mesh.VertexIndices().Where(mesh.IsBoundaryVertex).ToDictionary(v => v, v => mesh.GetVertex(v));
                double before = mesh.VertexIndices().Max(v => Math.Abs(mesh.GetVertex(v).z));

                if (laplacian)
                    Assert.True(SmoothingExample.Laplacian(mesh, 0.01));
                else
                    SmoothingExample.Iterative(mesh);

                double after = mesh.VertexIndices().Max(v => Math.Abs(mesh.GetVertex(v).z));
                Assert.True(after < 0.5 * before, $"laplacian={laplacian}: {before} -> {after}");
                foreach (var kv in boundary)
                    Assert.Equal(kv.Value, mesh.GetVertex(kv.Key));
            }
        }

        // z = height on [x0, x1] x [y0, y1], nx x ny cells, normals up; cells with keep == false are left out
        static DMesh3 SweptCutSheet(double x0, double y0, double x1, double y1, int nx, int ny, double height, Func<double, double, bool> keep = null)
        {
            var m = new DMesh3();
            var id = new Dictionary<(int, int), int>();
            int vertex(int i, int j)
            {
                if (id.TryGetValue((i, j), out int v) == false)
                    id[(i, j)] = v = m.AppendVertex(new Vector3d(x0 + (x1 - x0) * i / nx, y0 + (y1 - y0) * j / ny, height));
                return v;
            }
            for (int i = 0; i < nx; ++i)
                for (int j = 0; j < ny; ++j) {
                    if (keep != null && keep(x0 + (x1 - x0) * (i + 0.5) / nx, y0 + (y1 - y0) * (j + 0.5) / ny) == false)
                        continue;
                    int a = vertex(i, j), b = vertex(i + 1, j), c = vertex(i + 1, j + 1), d = vertex(i, j + 1);
                    m.AppendTriangle(a, b, c);
                    m.AppendTriangle(a, c, d);
                }
            return m;
        }

        static DMesh3 SweptCutCube()
        {
            return new TrivialBox3Generator() { Box = new Box3d(new Vector3d(2, 2, 2), new Vector3d(2, 2, 2)), NoSharedVertices = false }.Generate().MakeDMesh();
        }

        static void AssertSweptCut(ExactMeshBoolean op, double volume)
        {
            Assert.NotNull(op);
            Assert.True(op.Status == ExactMeshBoolean.BooleanStatus.Success, op.Status + " " + op.Error);
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(op.Result));
            Assert.Equal(volume, Volume(op.Result), 9);
        }

        [Fact]
        public void SweptCutExample_FullCrossing_Step_Pillar_AndPocket()
        {
            Vector3d down = -Vector3d.AxisZ;
            // a sheet that crosses fully: the lower half is removed, the upper half stays (not the mirror image)
            ExactMeshBoolean half = SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-1, -1, 5, 5, 1, 1, 1), down);
            AssertSweptCut(half, 48);
            Assert.All(half.Result.VertexIndices(), v => Assert.True(half.Result.GetVertex(v).z >= 1));
            AssertSweptCut(SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-1, -1, 5, 5, 1, 1, 2), down), 32);     // the guide's first row
            // a sheet that ends inside (x up to 2): a step
            AssertSweptCut(SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-1, -1, 2, 5, 1, 1, 2), down), 48);
            // a sheet with a hole [1,3]^2 inside the cube: a pillar under the hole stays
            ExactMeshBoolean pillar = SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-1, -1, 5, 5, 6, 6, 2, (x, y) => !(x > 1 && x < 3 && y > 1 && y < 3)), down);
            AssertSweptCut(pillar, 40);
            // a disc wholly inside: a blind pocket from the bottom
            AssertSweptCut(SweptCutExample.Run(SweptCutCube(), SweptCutSheet(1, 1, 3, 3, 1, 1, 2), down), 56);
            // the inputs are not modified
            DMesh3 cube = SweptCutCube(), sheet = SweptCutSheet(-1, -1, 5, 5, 1, 1, 2);
            SweptCutExample.Run(cube, sheet, down);
            Assert.Equal(12, cube.TriangleCount);
            Assert.Equal(2, sheet.TriangleCount);
        }

        [Fact]
        public void SweptCutExample_ASheetThatDoesNotFaceAwayFromTheDirection_IsRefused()
        {
            DMesh3 sheet = SweptCutSheet(-1, -1, 5, 5, 1, 1, 2);
            Assert.Null(SweptCutExample.Run(SweptCutCube(), sheet, Vector3d.AxisZ));        // the direction of the normals
            Assert.Null(SweptCutExample.Run(SweptCutCube(), sheet, Vector3d.AxisX));        // parallel to the sheet
            // one triangle flipped
            var flipped = new DMesh3(sheet);
            flipped.ReverseTriOrientation(0);
            Assert.Null(SweptCutExample.Run(SweptCutCube(), flipped, -Vector3d.AxisZ));
            // an oblique direction is fine: the tool leans, the cut is the same where the sheet crosses fully
            AssertSweptCut(SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-8, -8, 12, 12, 1, 1, 2), new Vector3d(1, 0, -1)), 32);
        }

        [Fact]
        public void SweptCutExample_StackedParts_AreVisibleInSelfIntersectingPairs()
        {
            // z = 1 everywhere and a second part z = 3 over [1,3]^2: both face away, their prisms overlap
            DMesh3 sheet = SweptCutSheet(-1, -1, 5, 5, 1, 1, 1);
            MeshEditor.Append(sheet, SweptCutSheet(1, 1, 3, 3, 1, 1, 3));
            ExactMeshBoolean op = SweptCutExample.Run(SweptCutCube(), sheet, -Vector3d.AxisZ);
            Assert.True(op.SelfIntersectingPairs > 0);
            AssertSweptCut(op, 64 - 16 - 8);        // the union of the two prisms
            // a single sheet: none
            Assert.Equal(0, SweptCutExample.Run(SweptCutCube(), SweptCutSheet(-1, -1, 5, 5, 1, 1, 1), -Vector3d.AxisZ).SelfIntersectingPairs);
        }

        [Fact]
        public void SweptCutExample_Invariance_OverSimilarities()
        {
            DMesh3 cube = SweptCutCube(), sheet = SweptCutSheet(-1, -1, 2, 5, 3, 3, 2);
            ExactMeshBoolean r0 = SweptCutExample.Run(cube, sheet, -Vector3d.AxisZ);
            AssertSweptCut(r0, 48);
            foreach (Similarity s in Similarity.All()) {
                ExactMeshBoolean r = SweptCutExample.Run(Invariance.Transformed(cube, s), Invariance.Transformed(sheet, s), s.ApplyDirection(-Vector3d.AxisZ));
                Assert.True(r != null && r.Status == ExactMeshBoolean.BooleanStatus.Success, s.Name);
                Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(r.Result));
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(r0.Result, s), r.Result));
                else {
                    double expected = 48 * s.Scale * s.Scale * s.Scale;
                    Assert.True(Math.Abs(Volume(r.Result) - expected) <= 1e-6 * expected, s.Name + ": the volume " + Volume(r.Result) + " of " + expected);
                }
            }
        }

        [Fact]
        public void PlaneCutExample_CutsAndCaps()
        {
            DMesh3 mesh = Sphere(1, Vector3d.Zero);
            MeshPlaneCut cut = PlaneCutExample.Run(mesh, Vector3d.Zero, Vector3d.AxisZ);
            Assert.Single(cut.CutLoops);
            Assert.False(cut.CutLoopsFailed);
            Assert.All(mesh.VertexIndices(), v => Assert.True(mesh.GetVertex(v).z < 1e-8));   // +Z side removed
            Assert.True(mesh.IsClosed());
            Assert.InRange(Volume(mesh), 0.95 * 2.0 / 3 * Math.PI, 1.0 * 2.0 / 3 * Math.PI);  // half sphere
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var mesh = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    mesh.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    mesh.AppendTriangle(a, b, c);
                    mesh.AppendTriangle(a, c, d);
                }
            return mesh;
        }

        [Fact]
        public void UVAtlasExample_TorusAtlas_DiscCut_CubeCones()
        {
            // the atlas of a torus: overlap-free (exact test), charts within the limits, UVs in the unit square
            DMesh3 torus = Torus(2, 0.7, 48, 20);
            UVAtlasGenerator atlas = UVAtlasExample.Run(torus);
            Assert.Equal(UVAtlasStatus.Success, atlas.Status);
            Assert.Equal(torus.TriangleCount, atlas.AtlasMesh.TriangleCount);
            Assert.True(atlas.AtlasMesh.HasVertexUVs);
            Assert.True(UVOverlap.Compute(atlas.AtlasMesh).IsInjective);
            Assert.True(atlas.Distortion.MeanAreaDistortion < 0.25);
            Assert.True(MeshDiscCut.IsDiscs(atlas.AtlasMesh, out string why), why);
            foreach (int v in atlas.AtlasMesh.VertexIndices()) {
                Vector2f uv = atlas.AtlasMesh.GetVertexUV(v);
                Assert.InRange(uv.x, 0, 1);
                Assert.InRange(uv.y, 0, 1);
            }

            // the torus cut into one disc: Euler characteristic 1, one boundary loop
            DMesh3 disc;
            ParameterizationResult r = UVAtlasExample.CutAndFlatten(torus, out disc);
            Assert.True(r.Succeeded, r.Error);
            Assert.Equal(1, disc.VertexCount - disc.EdgeCount + disc.TriangleCount);
            Assert.Equal(1, new MeshBoundaryLoops(disc).Count);

            // the cube: eight cones at the corners, unfolded without distortion
            DMesh3 cube = new GridBox3Generator() { EdgeVertices = 5 }.Generate().MakeDMesh();
            ConeFlattening cones = UVAtlasExample.Cones(cube);
            Assert.True(cones.Result.Succeeded, cones.Result.Error);
            Assert.Equal(8, cones.ConeVertices.Count);
            Assert.True(cones.Result.Distortion.MaxAreaDistortion < 1e-6);
            Assert.True(UVOverlap.Compute(cones.CutMesh, cones.Result.UV).IsInjective);
        }

        [Fact]
        public void MeasurementsExample_MeasuresBoxAndTorusGenus()
        {
            MeasurementsExample.Result box = MeasurementsExample.Run(MeshConstructionExample.Box());
            Assert.Equal(8.0, box.Volume, 9);
            Assert.Equal(24.0, box.Area, 9);
            Assert.Equal(new Vector3d(-1, -1, -1), box.Bounds.Min);
            Assert.Equal(new Vector3d(1, 1, 1), box.Bounds.Max);
            Assert.Equal(0.5, box.MinEdge, 9);
            Assert.Equal(0, box.Genus);

            DMesh3 open = Grid(2, 1);
            Assert.Equal(-1, MeasurementsExample.Run(open).Genus);
        }

        [Fact]
        public void SpatialQueriesExample_NearestRayInside()
        {
            DMesh3 sphere = Sphere(1, Vector3d.Zero, 20);
            SpatialQueriesExample.Result r = SpatialQueriesExample.Run(sphere, new Vector3d(3, 0, 0));
            Assert.Equal(2.0, r.Distance, 2);
            Assert.True(r.NearestPoint.Distance(new Vector3d(1, 0, 0)) < 0.01);
            Assert.True(r.RayHit.Distance(new Vector3d(1, 0, 0)) < 0.01);
            Assert.True(r.CenterInside);
            Assert.False(r.FarPointInside);
            Assert.Equal(1.0, r.CenterWinding, 6);

            // the winding number still says "inside" for a mesh with a hole
            DMesh3 open = Sphere(1, Vector3d.Zero, 20);
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.InRange(SpatialQueriesExample.Run(open, new Vector3d(3, 0, 0)).CenterWinding, 0.95, 1.0);
        }

        [Fact]
        public void PointQueriesExample_FindsNearest()
        {
            var points = new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 2, 0) };
            Assert.Equal(1, PointQueriesExample.NearestInRadius(points, new Vector3d(0.9, 0.1, 0), 0.5));
            Assert.Equal(-1, PointQueriesExample.NearestInRadius(points, new Vector3d(5, 5, 5), 0.5));

            DMesh3 box = MeshConstructionExample.Box();
            int vid = PointQueriesExample.NearestVertex(box, new Vector3d(1.2, 1.1, 1.3));
            Assert.Equal(new Vector3d(1, 1, 1), box.GetVertex(vid));
        }

        [Fact]
        public void OffsetSurfaceExample_OffsetsSphere()
        {
            DMesh3 sphere = Sphere(1, Vector3d.Zero, 20);
            DMesh3 offset = OffsetSurfaceExample.Run(sphere, 0.25, 0.05);
            Assert.True(offset.IsClosed());
            Assert.All(offset.VertexIndices(), v => Assert.InRange(offset.GetVertex(v).Length, 1.25 - 0.03, 1.25 + 0.03));
            Assert.True(Volume(offset) > 0);

            DMesh3 inset = OffsetSurfaceExample.Run(sphere, -0.25, 0.05);
            Assert.All(inset.VertexIndices(), v => Assert.InRange(inset.GetVertex(v).Length, 0.75 - 0.03, 0.75 + 0.03));
        }

        [Fact]
        public void DistancesExample_HausdorffAndGeodesic()
        {
            double lower, upper;
            Assert.True(DistancesExample.Hausdorff(Sphere(1, Vector3d.Zero), Sphere(1.5, Vector3d.Zero), out lower, out upper));
            Assert.True(lower <= upper && upper - lower <= 1e-4 + 1e-12);
            Assert.InRange(lower, 0.49, 0.51);       // concentric spheres: about r2 - r1

            DMesh3 plane = Grid(40, 10);
            int source = 20 * 41 + 20;                // center vertex (5,5,0); near an open boundary the heat method is less accurate
            Vector3d c = plane.GetVertex(source);
            List<Vector3d> path;
            double[] d = DistancesExample.Geodesic(plane, source, out path);
            Assert.NotNull(d);
            Assert.Equal(0, d[source], 9);
            // on a plane the geodesic distance is the euclidean one: about 2% plus half the mean edge length,
            // near the open boundary 2% plus one mean edge length
            double h = AverageEdgeLength(plane);
            foreach (int vid in plane.VertexIndices()) {
                Vector3d p = plane.GetVertex(vid);
                bool nearBoundary = Math.Min(Math.Min(p.x, p.y), Math.Min(10 - p.x, 10 - p.y)) < 1;
                double exact = p.Distance(c);
                Assert.True(Math.Abs(d[vid] - exact) < 0.02 * exact + (nearBoundary ? h : 0.5 * h), $"{p}: {d[vid]} vs {exact}");
            }
            Assert.True(path.Count >= 2);
            Assert.True(path[path.Count - 1].Distance(c) < 1e-9);    // the path ends at the source vertex
            Assert.InRange(path[0].Distance(c), 7.0, 7.08);          // and starts at the farthest vertex, a corner
        }

        [Fact]
        public void IntrinsicTriangulationExample_RefinedLaplacianGeodesicAndOverlay()
        {
            // 169 degree triangles, boundary edges opposite them
            DMesh3 mesh = IntrinsicGeodesicsTests.ObtusePlane(10, 100, 1.0, 0.05);
            IntrinsicTriangulation T;
            IntrinsicRefinementResult result;
            SymmetricSparseMatrix L = IntrinsicTriangulationExample.RefinedLaplacian(mesh, out T, out result);
            Assert.Equal(IntrinsicRefinementStatus.Success, result.Status);
            Assert.True(T.VertexCount > mesh.VertexCount);
            foreach (var kv in L.NonZeros())
                if (kv.Key.a != kv.Key.b) Assert.True(kv.Value <= 1e-9);     // no negative cotan weights

            int source = IntrinsicGeodesicsTests.NearestVertex(mesh, new Vector3d(5, 2.5, 0));
            Vector3d c = mesh.GetVertex(source);
            double[] d = IntrinsicTriangulationExample.RefinedGeodesic(mesh, source, out result);
            Assert.Equal(IntrinsicRefinementStatus.Success, result.Status);
            foreach (int vid in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(vid);
                if (p.x >= 1 && p.x <= 9) Assert.True(Math.Abs(d[vid] - p.Distance(c)) < 0.15, $"{p}: {d[vid]}");
            }

            CommonSubdivisionStatus status;
            DMesh3 overlay = IntrinsicTriangulationExample.CommonSubdivision(T, out status);
            Assert.Equal(CommonSubdivisionStatus.Success, status);
            Assert.Equal(MeshMeasurements.AreaT(mesh, mesh.TriangleIndices()), MeshMeasurements.AreaT(overlay, overlay.TriangleIndices()), 9);

            // from integer coordinates: flips and boundary splits only
            DMesh3 exact = IntrinsicTriangulationExample.DelaunayCommonSubdivision(mesh, out status);
            Assert.Equal(CommonSubdivisionStatus.Success, status);
            Assert.True(exact.TriangleCount > mesh.TriangleCount);
            Assert.Equal(MeshMeasurements.AreaT(mesh, mesh.TriangleIndices()), MeshMeasurements.AreaT(exact, exact.TriangleIndices()), 9);
        }

        [Fact]
        public void IntrinsicTriangulationExample_GeodesicPathIsStraightOnAPlane()
        {
            // on a plane of 169 degree triangles the geodesic is the straight segment
            DMesh3 mesh = IntrinsicGeodesicsTests.ObtusePlane(10, 100, 1.0, 0.05);
            int a = IntrinsicGeodesicsTests.NearestVertex(mesh, new Vector3d(1, 0.5, 0));
            int b = IntrinsicGeodesicsTests.NearestVertex(mesh, new Vector3d(9, 4.5, 0));
            double length;
            List<Vector3d> path = IntrinsicTriangulationExample.GeodesicPath(mesh, a, b, out length);
            Assert.NotNull(path);
            Vector3d pa = mesh.GetVertex(a), pb = mesh.GetVertex(b);
            Assert.True(Math.Abs(length - pa.Distance(pb)) < 1e-9, $"{length} vs {pa.Distance(pb)}");
            var line = new Segment3d(pa, pb);
            foreach (Vector3d p in path) Assert.True(line.DistanceSquared(p) < 1e-18);
        }

        [Fact]
        public void AlphaWrapExample_WrapsOpenSoupAndKeepsPassageOpen()
        {
            MeshAlphaWrap.WrapStatus status;
            DMesh3 wrapped = AlphaWrapExample.WrapOpenSoup(out status);
            Assert.Equal(MeshAlphaWrap.WrapStatus.Success, status);
            Assert.NotNull(wrapped);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(wrapped));
            Assert.True(Volume(wrapped) > 0);

            PassageWrap pw = AlphaWrapExample.WrapWithOpenPassage();
            Assert.Equal(PassageWrap.PassageStatus.Success, pw.Status);
            Assert.True(pw.AllPassed);
            Assert.NotNull(pw.Result);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(pw.Result));
            Assert.Single(pw.PinResults);
            Assert.True(pw.PinResults[0].ClearanceLow >= 0.35);
        }

        [Fact]
        public void SolidBooleanPipelineExample_CutsWithAnOpenSheet()
        {
            SolidBooleanPipeline pipeline = SolidBooleanPipelineExample.CutWithAnOpenSheet();
            Assert.True(pipeline.Status == SolidBooleanPipeline.PipelineStatus.Success, pipeline.Status + " " + pipeline.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(pipeline.Result));
            Assert.Equal(0.5, MeshMeasurements.VolumeArea(pipeline.Result, pipeline.Result.TriangleIndices(), pipeline.Result.GetVertex).x, 12);
            Assert.Single(pipeline.Report.Route);
            Assert.Equal("SheetRole", pipeline.Report.Route[0].Name);
        }

        [Fact]
        public void SolidBooleanPipelineExample_CutsWithAFlushSheetOnTheGrid()
        {
            SolidBooleanPipeline pipeline = SolidBooleanPipelineExample.CutWithAFlushSheetOnTheGrid();
            Assert.True(pipeline.Status == SolidBooleanPipeline.PipelineStatus.Success, pipeline.Status + " " + pipeline.Error);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(pipeline.Result));
            Assert.True(pipeline.Result.IsClosed());
            Assert.Equal(0.5, MeshMeasurements.VolumeArea(pipeline.Result, pipeline.Result.TriangleIndices(), pipeline.Result.GetVertex).x, 12);
            Assert.Equal(new[] { "SheetRole", "Grid", "SheetRole" }, pipeline.Report.Route.Select(s => s.Name).ToArray());
            Assert.Equal(1.0 / (1L << 40), pipeline.Report.Route[1].Grid);
        }

        [Fact]
        public void SolidBooleanPipelineExample_UnionsAndImprintsCleanly()
        {
            SolidBooleanPipeline pipeline = SolidBooleanPipelineExample.UnionAndReduce();
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipeline.Status);
            Assert.NotNull(pipeline.Result);
            Assert.Equal("", pipeline.Report.NotCleanReason);
            Assert.True(pipeline.Result.TriangleCount < pipeline.Report.BooleanTriangles);
            // two 2x2x2 cubes (volume 8 each) shifted by 0.8 along X -> overlap 1.2x2x2 = 4.8 -> union 11.2
            Assert.Equal(11.2, Volume(pipeline.Result), 3);

            ExactMeshImprint imprint = SolidBooleanPipelineExample.ImprintBox();
            Assert.Equal(ExactMeshImprint.ImprintStatus.Success, imprint.Status);
            Assert.True(imprint.RoundingClean);
            Assert.NotNull(imprint.Result);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(imprint.Result));
            Assert.NotEmpty(imprint.CurveEdges);
            Assert.Equal(8.0, Volume(imprint.Result), 9);
        }

        [Fact]
        public void TetMeshExample_BuildsValidTetMeshWithMarkersAndGmshExport()
        {
            TetMeshMarkers markers;
            string mshText;
            MeshTetrahedralization tet = TetMeshExample.Run(out markers, out mshText);
            Assert.Equal(MeshTetrahedralization.TetStatus.Success, tet.Status);
            Assert.NotNull(tet.Result);
            Assert.True(tet.Result.Tets.Count > 0);
            Assert.Null(TetMeshQuality.NotValidReason(tet.Result));
            Assert.NotNull(markers);
            Assert.Equal(1, markers.RegionCount);
            Assert.Contains("$MeshFormat", mshText);
            Assert.Contains("$Elements", mshText);
        }

        [Fact]
        public void QuadRemeshExample_ProducesClosedQuadDominantPolyMeshAndRoundTripsObj()
        {
            DPolyMesh roundTripped;
            QuadRemesher qr = QuadRemeshExample.Run(out roundTripped);
            Assert.Equal(QuadRemeshStatus.Success, qr.Status);
            Assert.NotNull(qr.Result);
            Assert.True(qr.Result.FaceCount > 0);
            Assert.NotNull(roundTripped);
            Assert.Equal(qr.Result.FaceCount, roundTripped.FaceCount);
            Assert.Equal(qr.Result.VertexCount, roundTripped.VertexCount);
            PolyMeshMetrics metrics = PolyMeshMetrics.Compute(roundTripped);
            Assert.True(metrics.IsManifold);
            Assert.Equal(0, metrics.BoundaryVertices);
            for (int v = 0; v < roundTripped.VertexCount; ++v) {
                Vector3d p = roundTripped.GetVertex(v);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
            }
            int quadCount = 0;
            for (int f = 0; f < roundTripped.FaceCount; ++f)
                if (roundTripped.FaceVertexCount(f) == 4) ++quadCount;
            Assert.True(quadCount > roundTripped.FaceCount / 2);
        }

        [Fact]
        public void Meshing2DExample_TriangulatesAndRefinesDomainWithHoleAndRegions()
        {
            Triangulation2Result plain = Meshing2DExample.DelaunayPoints();
            Assert.Equal(Triangulation2Status.Success, plain.Status);
            Assert.Equal(4, plain.Mesh.TriangleCount);

            DMesh3 mesh3d;
            ConstrainedTriangulation2Result refined = Meshing2DExample.QualityDomainWithHole(out mesh3d);
            Assert.Equal(Triangulation2Status.Success, refined.Status);
            Assert.NotNull(mesh3d);
            Assert.True(mesh3d.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(mesh3d.HasTriangleGroups);
            var groups = new HashSet<int>();
            // 10x6 rectangle (60) minus 2x2 square hole (4) = 56 in XY projection
            double xyArea = 0;
            foreach (int tid in mesh3d.TriangleIndices()) {
                groups.Add(mesh3d.GetTriangleGroup(tid));
                Index3i t = mesh3d.GetTriangle(tid);
                Vector3d a = mesh3d.GetVertex(t.a), b = mesh3d.GetVertex(t.b), c = mesh3d.GetVertex(t.c);
                xyArea += 0.5 * Math.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            }
            Assert.Contains(1, groups);
            Assert.Contains(2, groups);
            Assert.Equal(56.0, xyArea, 6);
            foreach (int vid in mesh3d.VertexIndices()) {
                Vector3d p = mesh3d.GetVertex(vid);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
                Assert.Equal(p.x + p.y, p.z, 9);
            }
        }

        [Fact]
        public void DualContouringAndOffsetExample_ContoursSharpBoxAndCertifiesOffset()
        {
            DualContouring dc = DualContouringAndOffsetExample.ContourSharpBox();
            Assert.NotNull(dc.Mesh);
            Assert.True(dc.Mesh.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(dc.Mesh));
            Assert.InRange(Volume(dc.Mesh), 7.5, 8.5);

            MeshOffset offset = DualContouringAndOffsetExample.CertifiedOffset(0.25);
            Assert.Equal(MeshOffset.Status.Success, offset.ResultStatus);
            Assert.True(offset.BandCertified);
            Assert.True(offset.SelfIntersectionFree);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(offset.Result));
            Assert.True(Volume(offset.Result) > 8.0);
        }

        [Fact]
        public void ContourLoftExample_LoftsCappedFrustumAndBranchingStack()
        {
            ContourLoftResult frustum = ContourLoftExample.LoftFrustum();
            Assert.Equal(ContourLoftStatus.Success, frustum.Status);
            Assert.NotNull(frustum.Mesh);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(frustum.Mesh));
            // frustum of radii 2 and 1, height 2 -> volume = pi*h/3*(R^2 + R*r + r^2) = 14*pi/3 ~ 14.66
            Assert.InRange(Volume(frustum.Mesh), 13.5, 15.0);

            ContourLoftStackResult stack = ContourLoftExample.LoftBranchingStack();
            Assert.Equal(ContourLoftStackStatus.Success, stack.Status);
            Assert.NotNull(stack.Mesh);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(stack.Mesh));
            Assert.True(Volume(stack.Mesh) > 0);
        }

        [Fact]
        public void PointCloudExample_EstimatesOutwardNormalsAndReconstructsClosedSphere()
        {
            var points = PointCloudExample.SampleUnitSphere(400);
            PointCloudNormals normals = PointCloudExample.EstimateNormals(points);
            Assert.Equal(PointCloudNormals.NormalsStatus.Success, normals.Status);
            Assert.True(normals.ConfidentFraction > 0.95);
            int outward = 0;
            for (int i = 0; i < points.Count; ++i)
                if (normals.Normals[i].Dot(points[i]) > 0.8) ++outward;
            Assert.True(outward > (int)(0.95 * points.Count));

            PointCloudSurface surf = PointCloudExample.ReconstructSurface(points);
            Assert.Equal(PointCloudSurface.SurfaceStatus.Success, surf.Status);
            Assert.NotNull(surf.Result);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(surf.Result));
            Assert.InRange(Volume(surf.Result), 3.0, 5.0);
        }

        [Fact]
        public void DeformationExample_MeetsHandlesWithArapAndBiharmonic()
        {
            ArapMeshDeformer.ResultInfo arap;
            BiharmonicMeshDeformer.ResultInfo biharmonic;
            DeformationExample.DeformStrip(out arap, out biharmonic);

            Assert.Equal(ArapMeshDeformer.Status.Success, arap.Status);
            Assert.Equal(BiharmonicMeshDeformer.Status.Success, biharmonic.Status);
            Assert.NotNull(arap.Mesh);
            Assert.NotNull(biharmonic.Mesh);
            Assert.True(arap.Mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(biharmonic.Mesh.CheckValidity(false, FailMode.ReturnOnly));
            foreach (int vid in arap.Mesh.VertexIndices()) {
                Vector3d pa = arap.Mesh.GetVertex(vid);
                Vector3d pb = biharmonic.Mesh.GetVertex(vid);
                Assert.True(MathUtil.IsFinite(pa.x) && MathUtil.IsFinite(pa.y) && MathUtil.IsFinite(pa.z));
                Assert.True(MathUtil.IsFinite(pb.x) && MathUtil.IsFinite(pb.y) && MathUtil.IsFinite(pb.z));
            }
            // verify all pinned handles at x = 0 stay at z = 0 and all lifted handles at x = 6 reach z = 1
            foreach (int pinned in new[] { 0, 7, 14 }) {
                Assert.InRange(arap.Mesh.GetVertex(pinned).z, -1e-6, 1e-6);
                Assert.InRange(biharmonic.Mesh.GetVertex(pinned).z, -1e-6, 1e-6);
            }
            foreach (int lifted in new[] { 6, 13, 20 }) {
                Assert.InRange(arap.Mesh.GetVertex(lifted).z, 0.999999, 1.000001);
                Assert.InRange(biharmonic.Mesh.GetVertex(lifted).z, 0.999999, 1.000001);
            }
        }

        [Fact]
        public void AnalysisBuildingBlocksExample_ComputesThicknessDeviationAndSphereFit()
        {
            MeshWallThickness wt = AnalysisBuildingBlocksExample.MeasureSlabThickness();
            Assert.NotNull(wt.Statistics);
            Assert.InRange(wt.Statistics.Median, 0.39, 0.41);

            MeshDeviation dev = AnalysisBuildingBlocksExample.MeasureSphereDeviation();
            Assert.NotNull(dev.VertexStatistics);
            Assert.InRange(dev.VertexStatistics.Mean, 0.09, 0.11);
            Assert.True(dev.HausdorffLowerBound <= dev.HausdorffUpperBound);
            Assert.InRange(dev.HausdorffUpperBound, 0.09, 0.12);

            PrimitiveFit3 fit = AnalysisBuildingBlocksExample.FitSphere();
            Assert.Equal(PrimitiveFit3.Results.Success, fit.Result);
            Assert.True(fit.SphereCenter.Distance(new Vector3d(1, 2, 3)) < 1e-4);
            Assert.InRange(fit.SphereRadius, 1.999, 2.001);
        }

        [Fact]
        public void BatchQueriesAndCcdExample_AnswersBatchQueriesAndExactToi()
        {
            RayHit[] hits;
            InsideStatus[] inside;
            BatchStatus bs = BatchQueriesAndCcdExample.RunBatchQueries(out hits, out inside);
            Assert.Equal(BatchStatus.Ok, bs);
            Assert.NotEqual(DMesh3.InvalidID, hits[0].Triangle);
            Assert.Equal(4.0, hits[0].T, 9);
            Assert.Equal(DMesh3.InvalidID, hits[1].Triangle);
            Assert.Equal(InsideStatus.Inside, inside[0]);
            Assert.Equal(InsideStatus.Outside, inside[1]);

            CcdResult ccd = BatchQueriesAndCcdExample.VertexCrossingTriangle();
            Assert.Equal(CcdStatus.Collision, ccd.Status);
            Assert.InRange(ccd.TimeOfImpact, 0.5 - 1e-5, 0.5 + 1e-5);
        }

        [Fact]
        public void TwoLevelMarchingTetrahedraExample_BuildsACleanThinBowl()
        {
            DMesh3 bowl = TwoLevelMarchingTetrahedraExample.ThinBowl();
            Assert.Equal("", MeshRobustRepair.NotCleanReason(bowl));
            // one piece (inner and outer side joined by the rim), up to the cut plane, the volume of the wall
            Assert.Equal(1, MeshTopologySummary.Compute(bowl).Components);
            Assert.InRange(bowl.CachedBounds.Max.z, 0.49, 0.51);
            double exact = 2 * Math.PI * 1.5 * 0.025;      // area of the unit sphere below z = 0.5 (2 pi r h, h = 1.5) times the wall
            Assert.InRange(Invariance.Volume(bowl) / exact, 0.95, 1.05);
        }

        [Fact]
        public void TpmsLatticeExample_BuildsCertifiedCleanGyroidSheet()
        {
            TpmsLattice tpms = TpmsLatticeExample.GyroidSheet();
            Assert.Equal(TpmsStatus.Ok, tpms.Status);
            Assert.NotNull(tpms.Mesh);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(tpms.Mesh));
            Assert.InRange(tpms.RealizedVolumeFraction, 0.25, 0.35);
        }

        [Fact]
        public void TerrainToolsExample_CertifiesTinComputesCellVolumesAndFiltersGround()
        {
            TerrainGridTin tin = TerrainToolsExample.GridToCertifiedTin(0.05);
            Assert.Equal(TerrainGridTinStatus.Ok, tin.Status);
            Assert.NotNull(tin.Result);
            Assert.True(tin.Result.CheckValidity(false, FailMode.ReturnOnly));
            foreach (int vid in tin.Result.VertexIndices()) {
                Vector3d p = tin.Result.GetVertex(vid);
                Assert.True(MathUtil.IsFinite(p.x) && MathUtil.IsFinite(p.y) && MathUtil.IsFinite(p.z));
            }
            Assert.True(tin.SurfaceError <= 0.05 + 1e-12);
            Assert.True(tin.Result.TriangleCount < 16 * 16 * 2);

            TerrainCellVolumes cv = TerrainToolsExample.CellCutFillVolumes();
            Assert.Equal(50.0, cv.TotalCutVolume, 9);
            Assert.Equal(50.0, cv.TotalFillVolume, 9);
            Assert.NotNull(cv.CutVolume);
            Assert.NotNull(cv.FillVolume);
            Assert.Equal(0.0, cv.OutsideCutVolume, 9);
            Assert.Equal(0.0, cv.OutsideFillVolume, 9);

            TerrainCellVolumes blocks = TerrainToolsExample.CellBlockVolumes();
            Assert.Equal(2, blocks.BandCount);
            Assert.Equal(50.0, blocks.BlockCutVolume.Take(4).Sum(), 9);          // band [8, 10]: all the cut
            Assert.Equal(0.0, blocks.BlockFillVolume.Take(4).Sum(), 9);
            Assert.Equal(50.0, blocks.BlockFillVolume.Skip(4).Sum(), 9);         // band [10, 12]: all the fill
            Assert.Equal(0.0, blocks.BlockCutVolume.Skip(4).Sum(), 9);

            int groundCount;
            ClothSimulationFilter csf = TerrainToolsExample.ClassifyGround(out groundCount);
            Assert.NotNull(csf.IsGround);
            for (int i = 0; i < groundCount; ++i)
                Assert.True(csf.IsGround[i]);
            for (int i = groundCount; i < csf.IsGround.Length; ++i)
                Assert.False(csf.IsGround[i]);
        }
    }
}
