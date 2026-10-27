using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// MeshFidelityMetrics (F-score, normal consistency, Edge Chamfer), TriangleQualityStatistics and
    /// MeshTopologySummary: analytic cases, and the cases Hausdorff distance and volume IoU do not see
    /// (lifted half, reversed patch, bevelled edge, closed handle).
    /// </summary>
    public class MeshFidelityMetricsTests
    {
        // n x n grid of quads in [x0, x1] x [0, 1] at height z, normal +z
        static DMesh3 Plate(double x0, double x1, double z, int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(x0 + (x1 - x0) * i / n, (double)j / n, z));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static DMesh3 Sphere(double r, int edgeVertices = 30)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c);
                    m.AppendTriangle(a, c, d);
                }
            return m;
        }

        // convex polyhedron from its faces (vertex indices, counter-clockwise seen from outside), fan triangulated
        static DMesh3 Polyhedron(Vector3d[] verts, int[][] faces)
        {
            var m = new DMesh3();
            foreach (var v in verts) m.AppendVertex(v);
            foreach (var f in faces)
                for (int k = 1; k + 1 < f.Length; ++k)
                    Assert.True(m.AppendTriangle(f[0], f[k], f[k + 1]) >= 0);
            return m;
        }

        static DMesh3 Cube()
        {
            var v = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
                            new Vector3d(0, 0, 1), new Vector3d(1, 0, 1), new Vector3d(1, 1, 1), new Vector3d(0, 1, 1) };
            return Polyhedron(v, new[] {
                new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } });
        }

        // the unit cube with its edge at y = 1, z = 1 bevelled: cut by the plane y + z = 2 - c
        static DMesh3 BevelledCube(double c)
        {
            var v = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(0, 1, 0),
                            new Vector3d(0, 0, 1), new Vector3d(1, 0, 1),
                            new Vector3d(0, 1, 1 - c), new Vector3d(1, 1, 1 - c), new Vector3d(0, 1 - c, 1), new Vector3d(1, 1 - c, 1) };
            return Polyhedron(v, new[] {
                new[] { 0, 3, 2, 1 },               // z = 0
                new[] { 0, 1, 5, 4 },               // y = 0
                new[] { 3, 6, 7, 2 },               // y = 1
                new[] { 4, 5, 9, 8 },               // z = 1
                new[] { 8, 9, 7, 6 },               // bevel
                new[] { 0, 4, 8, 6, 3 },            // x = 0
                new[] { 1, 2, 7, 9, 5 } });         // x = 1
        }

        // all outputs as numbers, for bit-exact comparisons
        static List<double> Values(MeshFidelityMetrics f)
        {
            var v = new List<double> { f.Size, f.DistanceMeshToReference, f.DistanceReferenceToMesh, f.Chamfer, f.NormalConsistency,
                f.SharpEdgeLengthReference, f.SharpEdgeLengthMesh, f.EdgeDistanceMeshToReference, f.EdgeDistanceReferenceToMesh, f.EdgeChamfer,
                (double)f.TopologyChanges };
            foreach (var a in new[] { f.Precision, f.Recall, f.FScore, f.EdgePrecision, f.EdgeRecall, f.EdgeFScore })
                v.AddRange(a);
            foreach (var n in new[] { f.NormalsMeshToReference, f.NormalsReferenceToMesh })
                v.AddRange(new[] { n.Samples, n.MeanAbsCos, n.MeanAngle, n.MatchedSamples, n.ReversedFraction });
            foreach (var q in new[] { f.ReferenceQuality, f.MeshQuality }) {
                v.AddRange(new double[] { q.Triangles, q.Degenerate, q.MinAngleBelow30, q.MinAngleBelow10, q.MinAngleBelow1, q.MaxAngleAbove90,
                    q.MaxAngleAbove160, q.AspectRatioAbove13, q.ScaledJacobianBelow05, q.ConditionAbove13 });
                foreach (var d in new[] { q.MinAngle, q.MaxAngle, q.AspectRatio, q.ScaledJacobian, q.Condition })
                    v.AddRange(new[] { d.Worst, d.Worst1Percent, d.Worst5Percent, d.Median });
            }
            foreach (var t in new[] { f.ReferenceTopology, f.MeshTopology })
                v.AddRange(new double[] { t.Components, t.Vertices, t.Edges, t.Triangles, t.BoundaryEdges, t.BoundaryLoops, t.BowtieVertices,
                    t.ClosedComponents, t.EulerCharacteristic, t.Genus });
            v.AddRange(new[] { f.MaxDistanceMeshToReference, f.MaxDistanceReferenceToMesh, f.OuterShare, f.OuterDistanceReferenceToMesh, f.OuterMaxDistanceReferenceToMesh });
            v.AddRange(f.OuterRecall);
            v.AddRange(f.OuterFScore);
            v.AddRange(f.OuterEdgeRecall);
            v.Add(f.OuterEdgeShare);
            v.AddRange(new[] { f.VolumeIoU, f.VolumeMissing, f.VolumeExtra, f.OuterVolumeIoU, f.OuterVolumeMissing, f.OuterVolumeExtra, f.OuterFaceShare });
            foreach (var c in new[] { f.CurvatureMeshToReference, f.CurvatureReferenceToMesh })
                v.AddRange(new[] { c.Samples, c.MeanError, c.GaussianError, c.MeanAbs, c.GaussianAbs });
            v.AddRange(new[] { f.MeanCurvatureError, f.GaussianCurvatureError, f.MeanCurvatureDistance, f.GaussianCurvatureDistance,
                f.TotalAbsMeanCurvatureReference, f.TotalAbsMeanCurvatureMesh, f.TotalAbsGaussianCurvatureReference, f.TotalAbsGaussianCurvatureMesh });
            return v;
        }

        static void AssertBitEqual(List<double> a, List<double> b)
        {
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; ++i)
                Assert.True(a[i].Equals(b[i]), $"value {i}: {a[i]:R} vs {b[i]:R}");
        }

        static MeshFidelityMetrics Run(DMesh3 reference, DMesh3 mesh, params double[] thresholds)
        {
            var f = new MeshFidelityMetrics(reference, mesh);
            if (thresholds.Length > 0) f.RelativeThresholds = thresholds;
            Assert.True(f.Compute());
            return f;
        }


        [Fact]
        public void IdenticalMeshes_PerfectScores()
        {
            DMesh3 m = BevelledCube(0.1);
            var f = Run(m, new DMesh3(m));
            for (int k = 0; k < f.FScore.Length; ++k) {
                Assert.Equal(1.0, f.FScore[k]);
                Assert.Equal(1.0, f.EdgeFScore[k]);
            }
            // (distances from squared distances: rounding noise about 1e-8 of the size)
            Assert.True(f.Chamfer < 1e-7, $"{f.Chamfer}");
            Assert.True(f.EdgeChamfer < 1e-7, $"{f.EdgeChamfer}");
            Assert.True(f.NormalConsistency > 1 - 1e-9, $"{f.NormalConsistency}");
            Assert.Equal(0.0, f.NormalsMeshToReference.ReversedFraction);
            Assert.Equal(MeshTopologySummary.Changes.None, f.TopologyChanges);
        }

        [Fact]
        public void LiftedHalf_FScoreAnalytic()
        {
            // reference: the unit square; mesh: its left half, and the right half lifted by delta. Within tau < delta:
            // precision = 1/2 (the lifted half is delta away), recall = 1/2 + tau (the reference's strip of width
            // tau right of x = 1/2 is within tau of the left half's edge). Hausdorff is only delta.
            const double delta = 0.02, tau = 0.01;
            DMesh3 reference = Plate(0, 1, 0, 40);
            DMesh3 mesh = Plate(0, 0.5, 0, 20);
            MeshEditor.Append(mesh, Plate(0.5, 1, delta, 20));
            double diag = Math.Sqrt(2);
            var f = Run(reference, mesh, tau / diag, 2 * delta / diag);
            Assert.Equal(diag, f.Size, 12);
            Assert.InRange(f.Precision[0], 0.5 - 2e-3, 0.5 + 2e-3);
            Assert.InRange(f.Recall[0], 0.5 + tau - 2e-3, 0.5 + tau + 2e-3);
            double expectedF = 2 * 0.5 * (0.5 + tau) / (1 + tau);
            Assert.InRange(f.FScore[0], expectedF - 2e-3, expectedF + 2e-3);
            Assert.Equal(1.0, f.FScore[1]);
            // mean distance of the mesh: delta on half of it
            Assert.InRange(f.DistanceMeshToReference * diag, 0.5 * delta - 1e-4, 0.5 * delta + 1e-4);
        }

        [Fact]
        public void ConcentricSpheres_FScoreStepsAtTheGap()
        {
            // radii 1 and 1.02: every distance is 0.02 up to the tessellation (sag below 1e-3)
            DMesh3 a = Sphere(1, 40), b = Sphere(1.02, 40);
            Vector3d centroid;
            double diag = MeshFidelityMetrics.SizeOf(a, out centroid);
            var f = Run(a, b, 0.015 / diag, 0.025 / diag);
            Assert.Equal(0.0, f.FScore[0]);
            Assert.Equal(1.0, f.FScore[1]);
            Assert.InRange(f.Chamfer * diag, 0.019, 0.021);
            Assert.True(f.NormalConsistency > 0.999);
            Assert.Equal(0.0, f.EdgeChamfer);     // no sharp edges on either
            Assert.Equal(0.0, f.SharpEdgeLengthMesh);
            Assert.True(double.IsNaN(f.EdgeRecall[0]));
        }

        [Fact]
        public void ReversedPatch_SharedAsArea_HausdorffBlind()
        {
            DMesh3 reference = Sphere(1, 30);
            DMesh3 mesh = new DMesh3(reference);
            double flipped = 0, total = 0;
            foreach (int tid in mesh.TriangleIndices().ToArray()) {
                double area = mesh.GetTriArea(tid);
                total += area;
                if (mesh.GetTriCentroid(tid).z > 0.5) {
                    mesh.ReverseTriOrientation(tid);
                    flipped += area;
                }
            }
            double share = flipped / total;
            Assert.InRange(share, 0.2, 0.3);
            var f = Run(reference, mesh);
            Assert.InRange(f.NormalsMeshToReference.ReversedFraction, share - 0.01, share + 0.01);
            Assert.InRange(f.NormalsReferenceToMesh.ReversedFraction, share - 0.01, share + 0.01);
            // the surfaces are the same: Hausdorff 0, F-score 1, |cos| 1
            Assert.Equal(1.0, f.FScore[0]);
            Assert.True(f.NormalConsistency > 1 - 1e-6, $"{f.NormalConsistency}");
            var h = new MeshHausdorffDistance(reference, mesh);
            Assert.True(h.Compute());
            Assert.True(h.LowerBound < 1e-6 && h.UpperBound < 1e-3, $"{h.UpperBound}");
            // the patch's border is not a crease: no sharp edges from the orientation flip
            Assert.Equal(0.0, f.SharpEdgeLengthMesh);
        }

        [Fact]
        public void TiltedFacets_NormalAngleAnalytic_NotReversed()
        {
            // a zigzag over the unit square, facets tilted by 70 degrees, amplitude within the largest threshold: every
            // sample of the zigzag sees the plate's normal at 70 degrees (cos 0.342), none is reversed
            const double p = 0.005, angle = 70;
            double h = 0.5 * p * Math.Tan(angle * MathUtil.Deg2Rad);
            int n = (int)Math.Round(2 / p);
            var zig = new DMesh3();
            for (int i = 0; i <= n; ++i) {
                double x = (double)i / n, z = (i % 2 == 0) ? 0 : h;
                zig.AppendVertex(new Vector3d(x, 0, z));
                zig.AppendVertex(new Vector3d(x, 1, z));
            }
            for (int i = 0; i < n; ++i) {
                int a = 2 * i, b = 2 * i + 2, c = 2 * i + 3, d = 2 * i + 1;
                zig.AppendTriangle(a, b, c);
                zig.AppendTriangle(a, c, d);
            }
            DMesh3 plate = Plate(0, 1, 0, 10);
            var f = Run(plate, zig, 0.005, 0.02);
            Assert.Equal(f.NormalsMeshToReference.Samples, f.NormalsMeshToReference.MatchedSamples);
            Assert.Equal(0.0, f.NormalsMeshToReference.ReversedFraction);
            Assert.Equal(Math.Cos(angle * MathUtil.Deg2Rad), f.NormalsMeshToReference.MeanAbsCos, 6);
            Assert.Equal(angle, f.NormalsMeshToReference.MeanAngle, 4);
            Assert.Equal(0.0, f.NormalsReferenceToMesh.ReversedFraction);
        }

        [Fact]
        public void SegmentTree_EqualsBruteForce()
        {
            // random segments of mixed lengths, and query points inside and outside their bounds
            var rnd = new Random(3);
            var segments = new List<Vector3d>();
            for (int i = 0; i < 3000; ++i) {
                var a = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble());
                double len = (i % 10 == 0) ? 0.5 : 0.01;
                segments.Add(a);
                segments.Add(a + len * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5));
            }
            var tree = new SegmentTree3(segments);
            for (int k = 0; k < 2000; ++k) {
                var q = new Vector3d(1.6 * rnd.NextDouble() - 0.3, 1.6 * rnd.NextDouble() - 0.3, 1.6 * rnd.NextDouble() - 0.3);
                double best = double.MaxValue;
                for (int j = 0; j < segments.Count; j += 2)
                    best = Math.Min(best, SegmentTree3.segment_distance_sqr(q, segments[j], segments[j + 1]));
                Assert.Equal(best, tree.NearestDistanceSqr(q));
            }
            Assert.True(double.IsPositiveInfinity(new SegmentTree3(new List<Vector3d>()).NearestDistanceSqr(Vector3d.Zero)));
        }

        [Fact]
        public void SharpEdges_CubeAndBevelledCube_Lengths()
        {
            double degenerate = 1e-24;
            List<Vector3d> cube = MeshFidelityMetrics.SharpEdges(Cube(), 30, degenerate);
            Assert.Equal(24, cube.Count);
            Assert.Equal(12, Length(cube), 12);
            const double c = 0.1;
            List<Vector3d> bevel = MeshFidelityMetrics.SharpEdges(BevelledCube(c), 30, degenerate);
            // 7 unchanged edges, 4 shortened by c, the 2 long bevel edges and the 2 end edges of length c sqrt(2)
            Assert.Equal(15 * 2, bevel.Count);
            Assert.Equal(13 - 4 * c + 2 * Math.Sqrt(2) * c, Length(bevel), 12);
            // boundary edges are not sharp: an open plate has none
            Assert.Empty(MeshFidelityMetrics.SharpEdges(Plate(0, 1, 0, 4), 30, degenerate));
            // the bevel's 45 degree edges are not sharp at a 50 degree threshold
            Assert.Equal(13 * 2, MeshFidelityMetrics.SharpEdges(BevelledCube(c), 50, degenerate).Count);
            // a triangle soup (no shared vertices) has the same sharp edges
            DMesh3 soup = new DMesh3();
            DMesh3 welded = BevelledCube(c);
            foreach (int tid in welded.TriangleIndices()) {
                Index3i t = welded.GetTriangle(tid);
                int a = soup.AppendVertex(welded.GetVertex(t.a)), b = soup.AppendVertex(welded.GetVertex(t.b)), d = soup.AppendVertex(welded.GetVertex(t.c));
                soup.AppendTriangle(a, b, d);
            }
            Assert.Equal(bevel, MeshFidelityMetrics.SharpEdges(soup, 30, degenerate));
            // with an inconsistently oriented face the edges stay the same (the flip is not a crease)
            DMesh3 flippedFace = BevelledCube(c);
            flippedFace.ReverseTriOrientation(flippedFace.TriangleIndices().First());
            Assert.Equal(Length(bevel), Length(MeshFidelityMetrics.SharpEdges(flippedFace, 30, degenerate)), 12);
        }

        [Fact]
        public void SharpEdges_NotAtSlivers()
        {
            // a flat triangle and a needle hinged on its edge, tilted by 45 degrees: sharp, unless the needle is
            // narrower than the minimum height (its normal then comes from rounding, not from a feature)
            foreach (double h in new[] { 1e-2, 1e-7 }) {
                var m = new DMesh3();
                int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0));
                int c = m.AppendVertex(new Vector3d(0.5, -1, 0));
                int d = m.AppendVertex(new Vector3d(0.5, h * Math.Sqrt(0.5), h * Math.Sqrt(0.5)));
                m.AppendTriangle(a, b, c);
                m.AppendTriangle(b, a, d);
                List<Vector3d> e = MeshFidelityMetrics.SharpEdges(m, 30, 1e-5);
                Assert.Equal(h > 1e-5 ? 2 : 0, e.Count);
            }
        }

        static double Length(List<Vector3d> e)
        {
            double s = 0;
            for (int i = 0; i < e.Count; i += 2) s += e[i].Distance(e[i + 1]);
            return s;
        }

        // the Edge Chamfer distance by brute force: midpoint quadrature over the segments, exact distances to all
        // segments of the other set
        static double BruteEdgeDistance(List<Vector3d> from, List<Vector3d> to, int perUnit)
        {
            double sum = 0, length = 0;
            for (int i = 0; i < from.Count; i += 2) {
                double l = from[i].Distance(from[i + 1]);
                int n = Math.Max(1, (int)(l * perUnit));
                for (int k = 0; k < n; ++k) {
                    Vector3d p = from[i] + ((k + 0.5) / n) * (from[i + 1] - from[i]);
                    double best = double.MaxValue;
                    for (int j = 0; j < to.Count; j += 2)
                        best = Math.Min(best, SegmentTree3.segment_distance_sqr(p, to[j], to[j + 1]));
                    sum += Math.Sqrt(best) * (l / n);
                }
                length += l;
            }
            return sum / length;
        }

        [Fact]
        public void BevelledEdge_EdgeChamferMatchesBruteForce_HausdorffAndIoUBlind()
        {
            // a bevel of 0.02 on one of the 12 edges: Hausdorff 0.014 (0.8% of the diagonal) and IoU 0.9998 pass the
            // benchmark criterion, but the edge is gone: the Edge Chamfer distance and edge recall see it
            const double c = 0.02;
            DMesh3 cube = Cube(), bevel = BevelledCube(c);
            double diag = Math.Sqrt(3);
            var f = new MeshFidelityMetrics(cube, bevel) { EdgeSamples = 40000, RelativeThresholds = new[] { 0.001, 0.005, 0.01 } };
            Assert.True(f.Compute());
            List<Vector3d> ec = MeshFidelityMetrics.SharpEdges(cube, 30, 1e-24), eb = MeshFidelityMetrics.SharpEdges(bevel, 30, 1e-24);
            double toBevel = BruteEdgeDistance(ec, eb, 20000), toCube = BruteEdgeDistance(eb, ec, 20000);
            Assert.InRange(f.EdgeDistanceReferenceToMesh * diag, toBevel * 0.98, toBevel * 1.02);
            Assert.InRange(f.EdgeDistanceMeshToReference * diag, toCube * 0.98, toCube * 1.02);
            Assert.InRange(f.EdgeChamfer, 0.5 * (toBevel + toCube) / diag * 0.98, 0.5 * (toBevel + toCube) / diag * 1.02);
            // the lost edge is 1/12 of the cube's edges, its points are at least c / sqrt(2) from the bevel's edges
            Assert.InRange(1 - f.EdgeRecall[0], 1.0 / 12 * 0.9, 1.0 / 12 * 1.1);
            Assert.True(f.FScore[2] > 0.99);
            double missing, extra;
            Assert.True(MeshRepairMetrics.VolumeIoU(cube, bevel, out missing, out extra) > 0.99);
            var h = new MeshHausdorffDistance(cube, bevel);
            Assert.True(h.Compute());
            Assert.True(h.UpperBound / diag < 0.01);
        }

        [Fact]
        public void EdgesLost_EdgeChamferInfinite()
        {
            // the sphere has no sharp edges: all of the cube's are lost
            DMesh3 cube = Cube();
            DMesh3 sphere = Sphere(0.5, 20);
            MeshTransforms.Translate(sphere, new Vector3d(0.5, 0.5, 0.5));
            var f = Run(cube, sphere);
            Assert.True(double.IsPositiveInfinity(f.EdgeChamfer));
            Assert.True(double.IsPositiveInfinity(f.EdgeDistanceReferenceToMesh));
            Assert.True(double.IsNaN(f.EdgeDistanceMeshToReference));
            Assert.Equal(0.0, f.EdgeRecall[2]);
            Assert.True(double.IsNaN(f.EdgePrecision[2]));
        }

        [Fact]
        public void TriangleQuality_VerdictValues()
        {
            double minA, maxA, ar, sj, cond;
            // equilateral: all metrics at their optimum
            TriangleQualityStatistics.Measure(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, Math.Sqrt(3) / 2, 0), out minA, out maxA, out ar, out sj, out cond);
            Assert.Equal(60, minA, 10); Assert.Equal(60, maxA, 10);
            Assert.Equal(1, ar, 12); Assert.Equal(1, sj, 12); Assert.Equal(1, cond, 12);
            // right isosceles: aspect (sqrt 2 + 1) / sqrt 3, condition 2 / sqrt 3, scaled Jacobian 2 / sqrt 3 sin 45
            TriangleQualityStatistics.Measure(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), out minA, out maxA, out ar, out sj, out cond);
            Assert.Equal(45, minA, 10); Assert.Equal(90, maxA, 10);
            Assert.Equal((Math.Sqrt(2) + 1) / Math.Sqrt(3), ar, 12);
            Assert.Equal(2 / Math.Sqrt(3), cond, 12);
            Assert.Equal(2 / Math.Sqrt(3) * Math.Sqrt(0.5), sj, 12);
            // collinear: angles 0, 0, 180
            TriangleQualityStatistics.Measure(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(2, 0, 0), out minA, out maxA, out ar, out sj, out cond);
            Assert.Equal(0, minA); Assert.Equal(180, maxA, 10);
            Assert.True(double.IsPositiveInfinity(ar)); Assert.True(double.IsPositiveInfinity(cond)); Assert.Equal(0, sj);
            // coincident corners
            TriangleQualityStatistics.Measure(Vector3d.Zero, Vector3d.Zero, new Vector3d(1, 0, 0), out minA, out maxA, out ar, out sj, out cond);
            Assert.Equal(0, minA); Assert.Equal(180, maxA); Assert.Equal(0, sj);
        }

        [Fact]
        public void TriangleQuality_DistributionAndCounts()
        {
            // a plate of right isosceles triangles plus one sliver and one degenerate triangle
            DMesh3 m = Plate(0, 1, 0, 10);
            int a = m.AppendVertex(new Vector3d(3, 0, 0)), b = m.AppendVertex(new Vector3d(4, 0, 0)), c = m.AppendVertex(new Vector3d(3.5, 0.001, 0));
            m.AppendTriangle(a, b, c);
            int d = m.AppendVertex(new Vector3d(5, 0, 0)), e = m.AppendVertex(new Vector3d(6, 0, 0)), g = m.AppendVertex(new Vector3d(7, 0, 0));
            m.AppendTriangle(d, e, g);
            var q = TriangleQualityStatistics.Compute(m);
            Assert.Equal(202, q.Triangles);
            Assert.Equal(1, q.Degenerate);
            Assert.Equal(0, q.MinAngle.Worst);
            Assert.Equal(45, q.MinAngle.Median, 10);
            Assert.Equal(45, q.MinAngle.Worst5Percent, 10);
            Assert.True(q.AspectRatio.Worst == double.PositiveInfinity);
            Assert.Equal((Math.Sqrt(2) + 1) / Math.Sqrt(3), q.AspectRatio.Median, 12);
            Assert.Equal(2, q.MinAngleBelow1);
            Assert.Equal(2, q.MinAngleBelow10);
            Assert.Equal(2, q.MinAngleBelow30);
            Assert.Equal(2, q.MaxAngleAbove160);
            Assert.Equal(202, q.AspectRatioAbove13);
            Assert.Equal(2, q.ScaledJacobianBelow05);
        }

        [Fact]
        public void Topology_GenusLoopsComponents()
        {
            var sphere = MeshTopologySummary.Compute(Sphere(1, 10));
            Assert.Equal(1, sphere.Components); Assert.Equal(2, sphere.EulerCharacteristic); Assert.Equal(0, sphere.Genus);
            Assert.Equal(1, sphere.ClosedComponents); Assert.Equal(0, sphere.BoundaryLoops);
            var torus = MeshTopologySummary.Compute(Torus(2, 0.5, 24, 12));
            Assert.Equal(0, torus.EulerCharacteristic); Assert.Equal(1, torus.Genus);
            var cyl = MeshTopologySummary.Compute(new OpenCylinderGenerator() { Slices = 16 }.Generate().MakeDMesh());
            Assert.Equal(2, cyl.BoundaryLoops); Assert.Equal(0, cyl.Genus); Assert.Equal(0, cyl.ClosedComponents);
            DMesh3 two = Torus(2, 0.5, 48, 24);
            DMesh3 s = Sphere(1, 10);
            MeshTransforms.Translate(s, new Vector3d(10, 0, 0));
            MeshEditor.Append(two, s);
            var both = MeshTopologySummary.Compute(two);
            Assert.Equal(2, both.Components); Assert.Equal(1, both.Genus);
            Assert.Equal(48 * 24 * 2, both.ComponentList[0].Triangles);       // largest first
            Assert.Equal(1, both.ComponentList[0].Genus);
            Assert.Equal(0, both.ComponentList[1].Genus);
            // changes
            Assert.Equal(MeshTopologySummary.Changes.HandlesClosed, MeshTopologySummary.Compare(torus, sphere));
            Assert.Equal(MeshTopologySummary.Changes.HandlesCreated, MeshTopologySummary.Compare(sphere, torus));
            Assert.Equal(MeshTopologySummary.Changes.ComponentsLost | MeshTopologySummary.Changes.HandlesClosed, MeshTopologySummary.Compare(both, sphere));
            Assert.Equal(MeshTopologySummary.Changes.BoundaryLoopsRemoved, MeshTopologySummary.Compare(cyl, sphere));
            // two boundary loops through one bowtie vertex count as one (documented)
            DMesh3 bow = new DMesh3();
            int c0 = bow.AppendVertex(Vector3d.Zero);
            int[] v = new int[4];
            v[0] = bow.AppendVertex(new Vector3d(1, 0, 0)); v[1] = bow.AppendVertex(new Vector3d(1, 1, 0));
            v[2] = bow.AppendVertex(new Vector3d(-1, 0, 0)); v[3] = bow.AppendVertex(new Vector3d(-1, -1, 0));
            bow.AppendTriangle(c0, v[0], v[1]); bow.AppendTriangle(c0, v[2], v[3]);
            var bt = MeshTopologySummary.Compute(bow);
            Assert.Equal(2, bt.Components); Assert.Equal(2, bt.BoundaryLoops); Assert.Equal(1, bt.BowtieVertices);
            // in one component: a 3 x 3 grid without its center and corner quad, the hole touching the outer boundary
            // at a bowtie vertex: one boundary loop counted (two when split there), genus 1/2 (documented)
            DMesh3 ring = new DMesh3();
            for (int j = 0; j <= 3; ++j)
                for (int i = 0; i <= 3; ++i)
                    ring.AppendVertex(new Vector3d(i, j, 0));
            for (int j = 0; j < 3; ++j)
                for (int i = 0; i < 3; ++i) {
                    if ((i == 1 && j == 1) || (i == 0 && j == 0)) continue;
                    int a = j * 4 + i;
                    ring.AppendTriangle(a, a + 1, a + 5);
                    ring.AppendTriangle(a, a + 5, a + 4);
                }
            var rt = MeshTopologySummary.Compute(ring);
            Assert.Equal(1, rt.Components); Assert.Equal(1, rt.BowtieVertices); Assert.Equal(1, rt.BoundaryLoops);
            Assert.Equal(0.5, rt.Genus);
            // a handle moved between components (two tori to a sphere and a genus 2 part): same total genus,
            // other genera of the components, both flags
            Func<double[], MeshTopologySummary> summary = (genera) => {
                var t = new MeshTopologySummary() { Components = genera.Length };
                foreach (double g in genera) {
                    t.Genus += g;
                    t.ComponentList.Add(new MeshTopologySummary.ComponentTopology() { Genus = g });
                }
                return t;
            };
            Assert.Equal(MeshTopologySummary.Changes.HandlesClosed | MeshTopologySummary.Changes.HandlesCreated,
                MeshTopologySummary.Compare(summary(new double[] { 1, 1 }), summary(new double[] { 2, 0 })));
            Assert.Equal(MeshTopologySummary.Changes.None, MeshTopologySummary.Compare(summary(new double[] { 1, 0 }), summary(new double[] { 0, 1 })));
        }

        [Fact]
        public void TorusVsSphere_HandleClosedFlagged()
        {
            // a torus with a tiny hole against a sphere (eg a solidify at coarse resolution that fills the hole)
            DMesh3 torus = Torus(1, 0.97, 64, 48);
            DMesh3 sphere = Sphere(1.97, 40);
            var f = Run(torus, sphere);
            Assert.True((f.TopologyChanges & MeshTopologySummary.Changes.HandlesClosed) != 0);
            Assert.Equal(1, f.ReferenceTopology.Genus);
            Assert.Equal(0, f.MeshTopology.Genus);
        }

        static DMesh3 Box(Vector3d min, Vector3d max)
        {
            return new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        }

        // quad in the plane given by origin and two edge vectors, normal u x v (flip: reversed)
        static DMesh3 Quad(Vector3d o, Vector3d u, Vector3d v, int n, bool flip)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(o + ((double)i / n) * u + ((double)j / n) * v);
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    if (flip) { m.AppendTriangle(a, d, b); m.AppendTriangle(a, c, d); }
                    else { m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c); }
                }
            return m;
        }

        [Fact]
        public void OuterSurface_LeavesOutInteriorFacesAndDoubleSidedFins()
        {
            // reference: a box with a single face inside it, a nested shell inside it, and a double-sided fin outside
            // (two coincident opposite quads); the repair to the solid drops all three: recall and the sampled
            // Hausdorff distance see them as lost, the outer surface measures do not
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 reference = new DMesh3(box);
            DMesh3 inner = Quad(new Vector3d(0.2, 0.2, 0.3), new Vector3d(0.6, 0, 0), new Vector3d(0, 0.6, 0), 6, false);
            DMesh3 shell = Box(new Vector3d(0.3, 0.3, 0.55), new Vector3d(0.7, 0.7, 0.85));
            DMesh3 finA = Quad(new Vector3d(1, 0.5, 0), new Vector3d(0.5, 0, 0), new Vector3d(0, 0, 1), 5, false);
            DMesh3 finB = Quad(new Vector3d(1, 0.5, 0), new Vector3d(0.5, 0, 0), new Vector3d(0, 0, 1), 5, true);
            foreach (var part in new[] { inner, shell, finA, finB }) MeshEditor.Append(reference, part);
            // inner face 0.6 x 0.6; shell 0.4 x 0.4 top and bottom, 0.4 x 0.3 sides; fin 2 x 0.5 x 1
            double boxArea = 6, otherArea = 0.36 + (2 * 0.16 + 4 * 0.12) + 1.0;
            var f = Run(reference, box);
            Assert.True(f.Recall[2] < 0.9);
            Assert.True(f.MaxDistanceReferenceToMesh * f.Size > 0.2);
            Assert.InRange(f.OuterShare, boxArea / (boxArea + otherArea) - 0.01, boxArea / (boxArea + otherArea) + 0.01);
            for (int k = 0; k < f.OuterRecall.Length; ++k) {
                Assert.Equal(1.0, f.OuterRecall[k]);
                Assert.Equal(1.0, f.OuterFScore[k]);
            }
            Assert.True(f.OuterMaxDistanceReferenceToMesh < 1e-6);
            // an open sheet and a cavity wall are outer
            DMesh3 sheet = Plate(0, 1, 0, 10);
            var fs = Run(sheet, new DMesh3(sheet));
            Assert.Equal(1.0, fs.OuterShare);
            DMesh3 cavity = new DMesh3(box);
            DMesh3 hole = Box(new Vector3d(0.3, 0.3, 0.3), new Vector3d(0.7, 0.7, 0.7));
            foreach (int tid in hole.TriangleIndices().ToArray()) hole.ReverseTriOrientation(tid);
            MeshEditor.Append(cavity, hole);
            var fc = Run(cavity, new DMesh3(cavity));
            Assert.InRange(fc.OuterShare, 0.995, 1.0);
            // off: the outer measures stay NaN
            var off = new MeshFidelityMetrics(reference, box) { OuterSurface = false };
            Assert.True(off.Compute());
            Assert.True(double.IsNaN(off.OuterShare) && double.IsNaN(off.OuterRecall[0]) && double.IsNaN(off.OuterFScore[0]));
        }

        [Fact]
        public void OuterEdges_LeaveOutEdgesInsideTheSolid()
        {
            // two overlapping boxes against their union: the edges of the second box inside the first are gone, but
            // not lost (inside the solid); 4 of the 20 units of edge length are inside
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One), b = Box(new Vector3d(0.5, 0.25, 0.25), new Vector3d(1.5, 0.75, 0.75));
            var union = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(union.Compute());
            DMesh3 reference = new DMesh3(a);
            MeshEditor.Append(reference, b);
            var f = new MeshFidelityMetrics(reference, union.Result) { EdgeSamples = 20000 };
            Assert.True(f.Compute());
            Assert.InRange(f.OuterEdgeShare, 0.8 - 0.01, 0.8 + 0.01);
            Assert.True(f.EdgeRecall[2] < 0.9);
            for (int k = 0; k < f.OuterEdgeRecall.Length; ++k)
                Assert.Equal(1.0, f.OuterEdgeRecall[k]);
            var off = new MeshFidelityMetrics(reference, union.Result) { OuterSurface = false };
            Assert.True(off.Compute());
            Assert.True(double.IsNaN(off.OuterEdgeShare) && double.IsNaN(off.OuterEdgeRecall[0]));
        }

        [Fact]
        public void OuterSurface_DuplicateShells()
        {
            // a box appended twice (coincident, same orientation; winding number 0 outside, 2 inside): all outer
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 twice = new DMesh3(box);
            MeshEditor.Append(twice, box);
            var f = Run(twice, box);
            Assert.Equal(1.0, f.OuterShare);
            Assert.Equal(1.0, f.OuterEdgeShare);
            Assert.Equal(1.0, f.OuterRecall[0]);
        }

        [Fact]
        public void OuterVolumeIoU_InteriorShellAndLayers_EqualsShellFree()
        {
            // a closed box with an interior shell (same orientation, winding number 2 inside it) and two stacked interior
            // layers facing down (below both, their winding numbers add to about -0.8, so the box's solid gets a hole
            // there), against the box itself (the repair to the solid): the whole reference's solid misses the region
            // under the layers, the solid of its outer faces is the box's, sample for sample
            DMesh3 box = Box(Vector3d.Zero, Vector3d.One);
            DMesh3 reference = new DMesh3(box);
            MeshEditor.Append(reference, Box(new Vector3d(0.6, 0.6, 0.6), new Vector3d(0.9, 0.9, 0.9)));
            MeshEditor.Append(reference, Quad(new Vector3d(0.1, 0.1, 0.5), new Vector3d(0, 0.8, 0), new Vector3d(0.8, 0, 0), 4, false));
            MeshEditor.Append(reference, Quad(new Vector3d(0.1, 0.1, 0.4), new Vector3d(0, 0.8, 0), new Vector3d(0.8, 0, 0), 4, false));
            Assert.True(MeshAnalysisNormals.TriNormal(reference, reference.TriangleCount - 1).z < -0.99);
            var withInterior = Run(reference, box);
            var shellFree = Run(new DMesh3(box), box);
            Assert.Equal(1.0, shellFree.VolumeIoU);
            Assert.Equal(1.0, shellFree.OuterVolumeIoU);
            Assert.Equal(1.0, shellFree.OuterFaceShare);
            // the interior distorts the winding number solid: about 2 % of the box missing
            Assert.InRange(withInterior.VolumeIoU, 0.9, 0.995);
            Assert.Equal(0.0, withInterior.VolumeMissing);
            Assert.InRange(withInterior.VolumeExtra, 0.005, 0.1);
            Assert.Equal(shellFree.OuterVolumeIoU, withInterior.OuterVolumeIoU);
            Assert.Equal(0.0, withInterior.OuterVolumeMissing);
            Assert.Equal(0.0, withInterior.OuterVolumeExtra);
            // outer faces: the box (area 6 of 6 + 0.54 + 2 * 0.64). The shell and the upper layer go in the first round; the
            // part of the lower layer under the upper one passes the rule of OuterSurface then (the upper layer lowers its
            // mean winding number below 0.75), and goes in the second
            Assert.Equal(6 / (6 + 0.54 + 1.28), withInterior.OuterFaceShare, 9);

            // the same with the box open (a quad of its top missing): the solids of the open box are the same with and
            // without the interior, the whole one is not
            DMesh3 open = Quad(Vector3d.Zero, new Vector3d(0, 1, 0), new Vector3d(1, 0, 0), 4, false);
            foreach (var side in new[] {
                Quad(Vector3d.Zero, new Vector3d(1, 0, 0), new Vector3d(0, 0, 1), 4, false),
                Quad(new Vector3d(0, 1, 0), new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), 4, false),
                Quad(Vector3d.Zero, new Vector3d(0, 0, 1), new Vector3d(0, 1, 0), 4, false),
                Quad(new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1), 4, false) })
                MeshEditor.Append(open, side);
            DMesh3 top = Quad(new Vector3d(0, 0, 1), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), 4, false);
            foreach (int tid in top.TriangleIndices().ToArray())
                if (top.GetTriCentroid(tid).x > 0.5 && top.GetTriCentroid(tid).x < 0.75 && top.GetTriCentroid(tid).y > 0.5 && top.GetTriCentroid(tid).y < 0.75)
                    top.RemoveTriangle(tid);
            MeshEditor.Append(open, top);
            DMesh3 openInterior = new DMesh3(open);
            MeshEditor.Append(openInterior, Box(new Vector3d(0.6, 0.6, 0.6), new Vector3d(0.9, 0.9, 0.9)));
            MeshEditor.Append(openInterior, Quad(new Vector3d(0.1, 0.1, 0.5), new Vector3d(0, 0.8, 0), new Vector3d(0.8, 0, 0), 4, false));
            MeshEditor.Append(openInterior, Quad(new Vector3d(0.1, 0.1, 0.4), new Vector3d(0, 0.8, 0), new Vector3d(0.8, 0, 0), 4, false));
            var o1 = Run(openInterior, box);
            var o0 = Run(open, box);
            Assert.Equal(o0.OuterVolumeIoU, o1.OuterVolumeIoU);
            Assert.True(o1.VolumeIoU < o0.VolumeIoU - 0.005, $"{o1.VolumeIoU} vs {o0.VolumeIoU}");
            Assert.InRange(o0.OuterVolumeIoU, 0.99, 1.0);
        }

        [Fact]
        public void OuterVolumeIoU_OverlappingPartsAndSheets()
        {
            // overlapping closed boxes against their union: the faces inside the other box are left out, the ones that
            // cross it are kept, and the outer faces still enclose the union
            DMesh3 a = Box(Vector3d.Zero, Vector3d.One), b = Box(new Vector3d(0.5, 0.25, 0.25), new Vector3d(1.5, 0.75, 0.75));
            var union = new ExactMeshBoolean(a, b, ExactMeshBoolean.Operations.Union);
            Assert.True(union.Compute());
            DMesh3 reference = new DMesh3(a);
            MeshEditor.Append(reference, b);
            var f = Run(reference, union.Result);
            Assert.True(f.VolumeIoU > 0.995, $"{f.VolumeIoU}");
            Assert.True(f.OuterVolumeIoU > 0.99, $"{f.OuterVolumeIoU}");
            Assert.True(f.OuterFaceShare < 1, $"{f.OuterFaceShare}");
            // a double-sided fin on the box adds no volume, its faces are not outer
            DMesh3 finned = Box(Vector3d.Zero, Vector3d.One);
            MeshEditor.Append(finned, Quad(new Vector3d(1, 0.5, 0), new Vector3d(0.5, 0, 0), new Vector3d(0, 0, 1), 5, false));
            MeshEditor.Append(finned, Quad(new Vector3d(1, 0.5, 0), new Vector3d(0.5, 0, 0), new Vector3d(0, 0, 1), 5, true));
            var ff = Run(finned, Box(Vector3d.Zero, Vector3d.One));
            Assert.Equal(1.0, ff.OuterVolumeIoU);
            Assert.Equal(6.0 / 7.0, ff.OuterFaceShare, 9);
            // an open sheet encloses nothing: NaN, also for the outer faces (all of them)
            DMesh3 sheet = Plate(0, 1, 0, 10);
            var fs = Run(sheet, new DMesh3(sheet));
            Assert.True(double.IsNaN(fs.VolumeIoU) && double.IsNaN(fs.VolumeMissing) && double.IsNaN(fs.OuterVolumeIoU));
            Assert.Equal(1.0, fs.OuterFaceShare);
            // a solid that the mesh misses entirely, and off switches
            DMesh3 far = Box(new Vector3d(5, 5, 5), new Vector3d(6, 6, 6));
            var fm = Run(a, far, 0.01);
            Assert.Equal(0.0, fm.VolumeIoU);
            Assert.Equal(1.0, fm.VolumeMissing);
            Assert.InRange(fm.VolumeExtra, 0.95, 1.05);       // the same volume, sampled
            Assert.Equal(0.0, fm.OuterVolumeIoU);
            var off = new MeshFidelityMetrics(reference, union.Result) { VolumeSamples = 0 };
            Assert.True(off.Compute());
            Assert.True(double.IsNaN(off.VolumeIoU) && double.IsNaN(off.OuterVolumeIoU) && double.IsNaN(off.OuterFaceShare));
            var noOuter = new MeshFidelityMetrics(reference, union.Result) { OuterSurface = false };
            Assert.True(noOuter.Compute());
            Assert.Equal(f.VolumeIoU, noOuter.VolumeIoU);
            Assert.True(double.IsNaN(noOuter.OuterVolumeIoU) && double.IsNaN(noOuter.OuterFaceShare));
        }

        [Fact]
        public void VolumeIoU_SameAsMeshRepairMetrics()
        {
            // the same samples as MeshRepairMetrics.VolumeIoU (a mesh at the origin, so the local copies are the meshes)
            DMesh3 reference = Sphere(1, 16), mesh = Sphere(1.05, 12);
            Vector3d center;
            MeshFidelityMetrics.SizeOf(reference, out center);
            MeshTransforms.Translate(reference, -center);
            MeshTransforms.Translate(mesh, -center);
            var f = Run(reference, mesh);
            double missing, extra;
            double iou = MeshRepairMetrics.VolumeIoU(reference, mesh, out missing, out extra);
            Assert.Equal(iou, f.VolumeIoU);
            Assert.Equal(missing, f.VolumeMissing);
            Assert.Equal(extra, f.VolumeExtra);
            // about (1 / 1.05)^3
            Assert.InRange(f.VolumeIoU, 0.85, 0.88);
        }

        [Fact]
        public void Curvature_IdenticalAndRefinedSpheres()
        {
            DMesh3 sphere = MeshCurvatureTests.Icosphere(1, 3);
            var same = Run(sphere, new DMesh3(sphere));
            Assert.True(same.MeanCurvatureError < 1e-9 && same.GaussianCurvatureError < 1e-9, $"{same.MeanCurvatureError} {same.GaussianCurvatureError}");
            // (other samples on the same surface)
            Assert.True(same.MeanCurvatureDistance < 1e-6 && same.GaussianCurvatureDistance < 1e-5, $"{same.MeanCurvatureDistance} {same.GaussianCurvatureDistance}");
            Assert.Equal(same.SurfaceSamples, same.CurvatureMeshToReference.Samples);
            // H * Size = 2, K * Size^2 = 4 on a sphere (Size = its diameter)
            Assert.Equal(2, same.CurvatureMeshToReference.MeanAbs, 3);
            Assert.InRange(same.CurvatureMeshToReference.GaussianAbs, 3.96, 4.04);
            // total |H| A_M / Size = 4 pi r^2 / r / 2r = 2 pi; total |deficit| = 4 pi
            // (of the polyhedron's area, H = 1 within 3e-5)
            double area = sphere.TriangleIndices().Sum(t => sphere.GetTriArea(t));
            Assert.InRange(area / 2, 2 * Math.PI * 0.99, 2 * Math.PI);
            Assert.Equal(area / 2, same.TotalAbsMeanCurvatureReference, 3);
            Assert.Equal(4 * Math.PI, same.TotalAbsGaussianCurvatureReference, 9);
            // a sphere against its refinement: small differences (the discretization error of both), shrinking with the
            // refinement by about 4 (second order)
            var r4 = Run(sphere, MeshCurvatureTests.Icosphere(1, 4));
            var r45 = Run(MeshCurvatureTests.Icosphere(1, 4), MeshCurvatureTests.Icosphere(1, 5));
            Assert.True(r4.MeanCurvatureError < 2e-3 && r4.GaussianCurvatureError < 0.05, $"{r4.MeanCurvatureError} {r4.GaussianCurvatureError}");
            Assert.True(r45.GaussianCurvatureError < r4.GaussianCurvatureError / 3, $"{r45.GaussianCurvatureError} {r4.GaussianCurvatureError}");
            Assert.True(r45.GaussianCurvatureDistance < r4.GaussianCurvatureDistance / 3, $"{r45.GaussianCurvatureDistance} {r4.GaussianCurvatureDistance}");
            Assert.True(r45.MeanCurvatureError < r4.MeanCurvatureError / 3, $"{r45.MeanCurvatureError} {r4.MeanCurvatureError}");
            // a larger sphere (radius 1.1): H * Size 2 / 1.1, K * Size^2 4 / 1.21, everywhere (threshold 10 %, so the
            // samples are matched)
            DMesh3 large = MeshCurvatureTests.Icosphere(1.1, 4);
            var fl = Run(MeshCurvatureTests.Icosphere(1, 4), large, 0.1);
            double dh = 2 - 2 / 1.1, dk = 4 - 4 / 1.21;
            // (within the discretization error of K at 2562 vertices, 0.15 %)
            Assert.InRange(fl.MeanCurvatureDistance, dh * 0.999, dh * 1.001);
            Assert.InRange(fl.MeanCurvatureError, dh * 0.999, dh * 1.001);
            Assert.InRange(fl.GaussianCurvatureDistance, dk * 0.99, dk * 1.01);
            Assert.InRange(fl.GaussianCurvatureError, dk * 0.99, dk * 1.01);
            // an ellipsoid (z * 1.3) differs far more than the refinement
            DMesh3 ellipsoid = MeshCurvatureTests.Icosphere(1, 4);
            MeshTransforms.Scale(ellipsoid, new Vector3d(1, 1, 1.3), Vector3d.Zero);
            var fe = Run(sphere, ellipsoid, 0.2);
            Assert.True(fe.MeanCurvatureError > 50 * r4.MeanCurvatureError, $"{fe.MeanCurvatureError} vs {r4.MeanCurvatureError}");
            Assert.True(fe.GaussianCurvatureDistance > 10 * r4.GaussianCurvatureDistance, $"{fe.GaussianCurvatureDistance} vs {r4.GaussianCurvatureDistance}");
        }

        [Fact]
        public void Curvature_PlaneAndCylinder_OpenAndDegenerate()
        {
            // a plane remeshed: H = K = 0 on both; only the interior vertices count (boundary not Valid)
            var plane = Run(Plate(0, 1, 0, 10), Plate(0, 1, 0, 7));
            Assert.True(plane.MeanCurvatureError < 1e-9 && plane.GaussianCurvatureError < 1e-9);
            Assert.True(plane.MeanCurvatureDistance < 1e-9 && plane.GaussianCurvatureDistance < 1e-9);
            Assert.True(plane.CurvatureMeshToReference.Samples > 0.9 * plane.SurfaceSamples);
            Assert.Equal(0, plane.TotalAbsGaussianCurvatureReference, 9);
            // a cylinder of radius 1 and height 3 at two resolutions: K = 0, H = 1 / 2 (H * Size = Size / 2)
            DMesh3 c1 = MeshCurvatureTests.Cylinder(1, 3, 64, 32), c2 = MeshCurvatureTests.Cylinder(1, 3, 48, 40);
            var fc = Run(c1, c2);
            Assert.True(fc.GaussianCurvatureError < 1e-6, $"{fc.GaussianCurvatureError}");
            Assert.True(fc.MeanCurvatureError < 2e-3 * fc.CurvatureMeshToReference.MeanAbs, $"{fc.MeanCurvatureError}");
            Assert.Equal(0.5 * fc.Size, fc.CurvatureMeshToReference.MeanAbs, 2);
            // a mesh made only of degenerate triangles has no surface to sample (false), one with a few degenerate
            // triangles gives finite values
            DMesh3 withSliver = new DMesh3(c1);
            int eid = withSliver.EdgeIndices().First(e => withSliver.IsBoundaryEdge(e) == false);
            Index2i ev = withSliver.GetEdgeV(eid);
            DMesh3.EdgeSplitInfo info;
            Assert.Equal(MeshResult.Ok, withSliver.SplitEdge(eid, out info));
            withSliver.SetVertex(info.vNew, withSliver.GetVertex(ev.a));
            var fd = Run(c1, withSliver);
            Assert.True(MathUtil.IsFinite(fd.MeanCurvatureError) && MathUtil.IsFinite(fd.GaussianCurvatureError)
                && MathUtil.IsFinite(fd.MeanCurvatureDistance) && MathUtil.IsFinite(fd.GaussianCurvatureDistance));
            // off: NaN
            var off = new MeshFidelityMetrics(c1, c2) { Curvature = false };
            Assert.True(off.Compute());
            Assert.True(double.IsNaN(off.MeanCurvatureError) && double.IsNaN(off.TotalAbsMeanCurvatureMesh) && off.CurvatureMeshToReference == null);
        }

        [Fact]
        public void Curvature_OnlyMatchedSamplesCompared()
        {
            // the reference has a flat plate far from the sphere that the mesh lost: its samples have no counterpart
            // (the F-score shows the loss) and are not compared, so the errors stay those of the sphere; the
            // distributions see the plate's zeros
            DMesh3 sphere = MeshCurvatureTests.Icosphere(1, 3);
            DMesh3 reference = new DMesh3(sphere);
            MeshEditor.Append(reference, Plate(4, 6, 0, 12));
            var f = Run(reference, new DMesh3(sphere));
            // (plate area 2 of 2 + 12.5)
            Assert.InRange(f.CurvatureReferenceToMesh.Samples, 0.82 * f.SurfaceSamples, 0.9 * f.SurfaceSamples);
            Assert.Equal(f.SurfaceSamples, f.CurvatureMeshToReference.Samples);
            Assert.True(f.MeanCurvatureError < 1e-9 && f.GaussianCurvatureError < 1e-9, $"{f.MeanCurvatureError} {f.GaussianCurvatureError}");
            Assert.True(f.MeanCurvatureDistance > 0.1, $"{f.MeanCurvatureDistance}");
        }

        [Fact]
        public void Wasserstein1_Analytic()
        {
            Assert.Equal(0.0, MeshFidelityMetrics.Wasserstein1(new[] { 1.0, 2, 3 }, new[] { 3.0, 1, 2 }));
            Assert.Equal(0.5, MeshFidelityMetrics.Wasserstein1(new[] { 0.0, 1 }, new[] { 0.5, 1.5 }), 15);
            // a point mass against a uniform pair: |0 - 1| / 2 + |2 - 1| / 2
            Assert.Equal(1.0, MeshFidelityMetrics.Wasserstein1(new[] { 1.0 }, new[] { 0.0, 2 }), 15);
            Assert.True(double.IsNaN(MeshFidelityMetrics.Wasserstein1(new double[0], new[] { 1.0 })));
        }

        [Fact]
        public void NothingToSample_ReturnsFalse()
        {
            Assert.False(new MeshFidelityMetrics(Cube(), Cube()) { SurfaceSamples = 0 }.Compute());
            Assert.False(new MeshFidelityMetrics(Cube(), Cube()) { EdgeSamples = 0 }.Compute());
            // the mesh has only triangles below 1e-12 of the reference's size squared
            DMesh3 tiny = Cube();
            MeshTransforms.Scale(tiny, 1e-7);
            var f = new MeshFidelityMetrics(Cube(), tiny);
            Assert.False(f.Compute());
            Assert.Null(f.FScore);
            Assert.Null(f.MeshTopology);
        }

        [Fact]
        public void Cancel_AtAnyPoint_ClearsAllOutputs()
        {
            DMesh3 reference, mesh;
            Pair(out reference, out mesh);
            int total = 0;
            var counting = new MeshFidelityMetrics(reference, mesh) { CancelF = () => { total++; return false; } };
            Assert.True(counting.Compute());
            Assert.True(total >= 4);
            for (int stop = 1; stop <= total; ++stop) {
                int calls = 0;
                var f = new MeshFidelityMetrics(reference, mesh) { CancelF = () => ++calls >= stop };
                Assert.False(f.Compute());
                Assert.True(double.IsNaN(f.Size) && double.IsNaN(f.Chamfer) && double.IsNaN(f.EdgeChamfer) && double.IsNaN(f.OuterShare));
                Assert.True(f.FScore == null && f.OuterRecall == null && f.EdgeRecall == null && f.OuterEdgeRecall == null && f.NormalsMeshToReference == null);
                Assert.True(double.IsNaN(f.OuterEdgeShare));
                Assert.True(f.MeshQuality == null && f.ReferenceQuality == null && f.MeshTopology == null && f.ReferenceTopology == null);
                Assert.Equal(MeshTopologySummary.Changes.None, f.TopologyChanges);
            }
        }

        [Fact]
        public void Soup_SameSurfaceMeasuresAsWelded()
        {
            // documented: weld soups first; the surface measures stay close, the topology does not
            DMesh3 welded = Sphere(1, 20);
            DMesh3 soup = new DMesh3();
            foreach (int tid in welded.TriangleIndices()) {
                Index3i t = welded.GetTriangle(tid);
                soup.AppendTriangle(soup.AppendVertex(welded.GetVertex(t.a)), soup.AppendVertex(welded.GetVertex(t.b)), soup.AppendVertex(welded.GetVertex(t.c)));
            }
            DMesh3 other = Sphere(1.01, 24);
            var a = Run(welded, other);
            var b = Run(soup, other);
            Assert.Equal(a.FScore[1], b.FScore[1]);
            Assert.InRange(b.NormalConsistency, a.NormalConsistency - 1e-3, a.NormalConsistency + 1e-3);
            Assert.Equal(a.SharpEdgeLengthReference, b.SharpEdgeLengthReference);
            Assert.Equal(welded.TriangleCount, b.ReferenceTopology.Components);
        }

        [Fact]
        public void EmptyOrNonFinite_ReturnsFalse()
        {
            var f = new MeshFidelityMetrics(Cube(), new DMesh3());
            Assert.False(f.Compute());
            Assert.True(double.IsNaN(f.Chamfer));
            Assert.Null(f.FScore);
            DMesh3 bad = Cube();
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            f = new MeshFidelityMetrics(bad, Cube());
            Assert.False(f.Compute());
            // a reference without extent
            DMesh3 point = new DMesh3();
            int a = point.AppendVertex(Vector3d.One), b = point.AppendVertex(Vector3d.One), c = point.AppendVertex(Vector3d.One);
            point.AppendTriangle(a, b, c);
            f = new MeshFidelityMetrics(point, Cube());
            Assert.False(f.Compute());
            Assert.True(double.IsNaN(f.Size));
        }

        // a mesh with features, curvature, a reversed patch and a different tessellation than the reference
        static void Pair(out DMesh3 reference, out DMesh3 mesh)
        {
            reference = BevelledCube(0.1);
            DMesh3 s1 = Sphere(0.3, 12);
            MeshTransforms.Translate(s1, new Vector3d(2, 0.5, 0.5));
            MeshEditor.Append(reference, s1);
            // inner geometry, so that the outer surface and edge classification is part of the comparisons: a face
            // inside the bevelled cube and a box overlapping it (its part inside is inner)
            MeshEditor.Append(reference, Quad(new Vector3d(0.2, 0.2, 0.4), new Vector3d(0.6, 0, 0), new Vector3d(0, 0.6, 0), 4, false));
            MeshEditor.Append(reference, Box(new Vector3d(0.7, 0.3, 0.3), new Vector3d(1.3, 0.6, 0.6)));
            mesh = Cube();
            DMesh3 s2 = Sphere(0.31, 16);
            MeshTransforms.Translate(s2, new Vector3d(2, 0.5, 0.52));
            foreach (int tid in s2.TriangleIndices().ToArray())
                if (s2.GetTriCentroid(tid).z > 0.7) s2.ReverseTriOrientation(tid);
            MeshEditor.Append(mesh, s2);
        }

        [Fact]
        public void Invariance_SimilarityAll()
        {
            DMesh3 reference, mesh;
            Pair(out reference, out mesh);
            var baseF = new MeshFidelityMetrics(reference, mesh) { SurfaceSamples = 20000, EdgeSamples = 5000 };
            Assert.True(baseF.Compute());
            List<double> expected = Values(baseF);
            Assert.True(baseF.NormalsMeshToReference.ReversedFraction > 0.01);
            Assert.InRange(baseF.OuterShare, 0.5, 0.95);
            Assert.InRange(baseF.OuterEdgeShare, 0.5, 0.95);
            foreach (Similarity s in Similarity.All()) {
                var f = new MeshFidelityMetrics(Invariance.Transformed(reference, s), Invariance.Transformed(mesh, s)) { SurfaceSamples = 20000, EdgeSamples = 5000 };
                Assert.True(f.Compute(), s.Name);
                List<double> got = Values(f);
                // the size is in mesh units
                got[0] /= s.Scale;
                if (s.IsExact) {
                    AssertBitEqual(expected, got);
                    continue;
                }
                Assert.Equal(expected.Count, got.Count);
                // the normal agreement block of Values: Samples, MeanAbsCos, MeanAngle, MatchedSamples, ReversedFraction
                int normals = 11 + 6 * baseF.RelativeThresholds.Length;
                for (int i = 0; i < expected.Count; ++i) {
                    double x = expected[i], y = got[i];
                    if (double.IsNaN(x) || double.IsInfinity(x)) { Assert.True(x.Equals(y), $"{s}: value {i}: {x} vs {y}"); continue; }
                    // shares can move by a few samples whose distance is at a threshold; counts by a few elements
                    double tol = 2e-3 * Math.Max(1, Math.Abs(x));
                    // the normal at a closest point jumps where a sample is equally close to two faces (here: samples on
                    // the cube's faces in the planes of the bevelled cube's faces, next to the bevel): about 0.1% of
                    // the samples get another normal by rounding, up to 90 degrees
                    if (i == normals + 2 || i == normals + 7) tol = 0.2;
                    if (i == normals + 1 || i == normals + 6) tol = 4e-3;
                    Assert.True(Math.Abs(x - y) <= tol, $"{s}: value {i}: {x:R} vs {y:R}");
                }
            }
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial_RepeatedRuns()
        {
            DMesh3 reference, mesh;
            Pair(out reference, out mesh);
            var p = new MeshFidelityMetrics(reference, mesh);
            Assert.True(p.Compute());
            var s = new MeshFidelityMetrics(reference, mesh) { Parallel = false };
            Assert.True(s.Compute());
            AssertBitEqual(Values(p), Values(s));
            for (int k = 0; k < 3; ++k) {
                var r = new MeshFidelityMetrics(reference, mesh);
                Assert.True(r.Compute());
                AssertBitEqual(Values(p), Values(r));
            }
            // another seed gives other samples, but about the same values
            var other = new MeshFidelityMetrics(reference, mesh) { Seed = 7 };
            Assert.True(other.Compute());
            Assert.NotEqual(p.Chamfer, other.Chamfer);
            Assert.InRange(other.Chamfer, p.Chamfer * 0.95, p.Chamfer * 1.05);
        }

        [Fact]
        public void Reuse_SecondComputeEqualsFreshInstance()
        {
            DMesh3 reference, mesh;
            Pair(out reference, out mesh);
            DMesh3 other = BevelledCube(0.05);
            var f = new MeshFidelityMetrics(reference, mesh);
            Assert.True(f.Compute());
            List<double> first = Values(f);
            Assert.True(f.Compute());
            AssertBitEqual(first, Values(f));
            f.Mesh = other;
            Assert.True(f.Compute());
            var fresh = new MeshFidelityMetrics(reference, other);
            Assert.True(fresh.Compute());
            AssertBitEqual(Values(fresh), Values(f));
            // a failing compute clears the outputs of the previous one
            f.Mesh = new DMesh3();
            Assert.False(f.Compute());
            Assert.Null(f.FScore);
            Assert.Null(f.MeshTopology);
            Assert.True(double.IsNaN(f.EdgeChamfer));
        }
    }
}
