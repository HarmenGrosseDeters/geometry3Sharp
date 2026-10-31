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
    }
}
