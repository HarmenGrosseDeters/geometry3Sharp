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
