using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TerrainBenchmark;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class TerrainDistanceMetricTests
    {
        static DMesh3 Mesh(params Vector3d[] vertices)
        {
            var m = new DMesh3(MeshComponents.None);
            foreach (var v in vertices) m.AppendVertex(v);
            for (int i = 0; i < vertices.Length; i += 3) m.AppendTriangle(i, i + 1, i + 2);
            return m;
        }
        static DMesh3 Plane() => Mesh(new Vector3d(-10, -10, 0), new Vector3d(30, -10, 0), new Vector3d(-10, 30, 0));
        static Dictionary<string, object> Measure(DMesh3 reference, params Vector3d[] points)
        {
            var row = new Dictionary<string, object>();
            new ClosestFields(reference).Measure(points, points.Length, 0, row);
            // The JSON serializer's default rejects non-finite numbers.
            JsonSerializer.Serialize(row);
            return row;
        }
        static double Number(Dictionary<string, object> r, string key) => Convert.ToDouble(r[key]);
        static void Near(Vector3d a, Vector3d b, double tol = 1e-10) => Assert.True((a - b).Length <= tol, $"{a} != {b}");

        [Fact]
        public void PlanesAndSlopeHaveAnalyticComponents()
        {
            var flat = Measure(Plane(), new Vector3d(0, 0, 0));
            Assert.Equal(0, Number(flat, "closest_d3_max"));
            var parallel = Measure(Plane(), new Vector3d(0, 0, 2));
            Assert.Equal(2, Number(parallel, "closest_d3_max"));
            Assert.Equal(0, Number(parallel, "closest_dh_max"));
            Assert.Equal(2, Number(parallel, "closest_dv_max"));
            var slope = Mesh(new Vector3d(-10, -10, -10), new Vector3d(30, -10, 30), new Vector3d(-10, 30, -10));
            var metric = new ClosestFields(slope);
            Assert.True(metric.TryClosest(new Vector3d(-1, 0, 1), out var q));
            Near(Vector3d.Zero, q);
            var row = Measure(slope, new Vector3d(-1, 0, 1));
            Assert.Equal(Math.Sqrt(2), Number(row, "closest_d3_mean"), 12);
            Assert.Equal(1, Number(row, "closest_dh_mean"), 12);
            Assert.Equal(1, Number(row, "closest_dv_mean"), 12); // vertical ray error would be 2
        }

        [Fact]
        public void FaceEdgesVerticesAndUnboundedSearch()
        {
            var metric = new ClosestFields(Mesh(Vector3d.Zero, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0)));
            var points = new[] { new Vector3d(.5, .5, 3), new Vector3d(1, -1, 2), new Vector3d(-1, 1, 2), new Vector3d(2, 2, 2), new Vector3d(-1, -1, 2), new Vector3d(4, -1, 2), new Vector3d(-1, 4, 2) };
            var expected = new[] { new Vector3d(.5, .5, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(1, 1, 0), Vector3d.Zero, new Vector3d(2, 0, 0), new Vector3d(0, 2, 0) };
            for (int i = 0; i < points.Length; i++) { Assert.True(metric.TryClosest(points[i], out var q)); Near(expected[i], q); }
            var far = new ClosestFields(Mesh(new Vector3d(100, 0, 0), new Vector3d(102, 0, 0), new Vector3d(100, 2, 0), new Vector3d(0, 0, 1000), new Vector3d(2, 0, 1000), new Vector3d(0, 2, 1000)));
            Assert.True(far.TryClosest(Vector3d.Zero, out var closest));
            Near(new Vector3d(100, 0, 0), closest); // outside any small XY search window
        }

        [Fact]
        public void EmptyAndInvalidAreUnavailableOrPartial()
        {
            var empty = Measure(new DMesh3(), Vector3d.Zero);
            Assert.Equal("unavailable", empty["closest_status"]);
            Assert.False(empty.ContainsKey("closest_d3_max"));
            var invalid = Measure(Plane(), new Vector3d(double.NaN, 0, 0), new Vector3d(0, 0, 2));
            Assert.Equal("partial", invalid["closest_status"]);
            Assert.Equal(1, invalid["closest_invalid"]);
            var bad = Mesh(Vector3d.Zero, Vector3d.Zero, Vector3d.Zero, new Vector3d(double.NaN, 0, 0), Vector3d.AxisX, Vector3d.AxisY);
            var row = Measure(bad, Vector3d.Zero);
            Assert.Equal(2, row["closest_invalid_reference_triangles"]);
            Assert.Equal("unavailable", row["closest_status"]);
            var noSamples = Measure(Plane());
            Assert.Equal("unavailable", noSamples["closest_status"]);
        }

        [Fact]
        public void NonconstantStatisticsUseTheSameCorrespondences()
        {
            var row = Measure(Plane(), new Vector3d(0,0,1), new Vector3d(0,0,2), new Vector3d(0,0,4));
            foreach (string component in new[] { "d3", "dv" }) {
                string p = "closest_"+component;
                Assert.Equal(3, row[p+"_n"]);
                Assert.Equal(1, Number(row,p+"_min")); Assert.Equal(1, Number(row,p+"_p01"));
                Assert.Equal(2, Number(row,p+"_p50")); Assert.Equal(2, Number(row,p+"_p99"));
                Assert.Equal(4, Number(row,p+"_max"));
                Assert.Equal(7.0/3, Number(row,p+"_mean"), 12);
                Assert.Equal(Math.Sqrt(7), Number(row,p+"_rms"), 12);
            }
            Assert.Equal(3, row["closest_dh_n"]);
            Assert.Equal(0, Number(row,"closest_dh_max"));
        }

        [Fact]
        public void ReusingARowClearsUnavailableStatistics()
        {
            var metric = new ClosestFields(Plane()); var row = new Dictionary<string, object>();
            metric.Measure(new[] { new Vector3d(0,0,2) }, 1, 0, row);
            metric.Measure(Array.Empty<Vector3d>(), 1, 0, row);
            Assert.Equal("unavailable", row["closest_status"]);
            foreach (string component in new[] { "d3", "dh", "dv" })
                foreach (string suffix in new[] { "min", "p01", "p50", "p99", "max", "mean", "rms" })
                    Assert.False(row.ContainsKey("closest_"+component+"_"+suffix));
            metric.Measure(new[] { new Vector3d(0,0,2) }, 1, 0, row);
            Assert.Equal(2, Number(row,"closest_d3_mean"));
        }

        [Fact]
        public void SimilaritiesUseTheFixedZFrameAndRepeatExactly()
        {
            var plane = Plane(); var p = new Vector3d(0, 0, 2);
            foreach (var s in Similarity.All()) {
                var transformed = Invariance.Transformed(plane, s);
                var row = Measure(transformed, s.Apply(p));
                Vector3d delta = s.Scale * s.ApplyDirection(p);
                double tolerance = Math.Max(1e-12 * s.Scale, 64 * s.Resolution(40) * s.Scale);
                Assert.InRange(Math.Abs(Number(row, "closest_d3_mean") - 2 * s.Scale), 0, tolerance);
                Assert.InRange(Math.Abs(Number(row, "closest_dh_mean") - new Vector2d(delta.x, delta.y).Length), 0, tolerance);
                Assert.InRange(Math.Abs(Number(row, "closest_dv_mean") - Math.Abs(delta.z)), 0, tolerance);
                if (s.IsExact) Assert.Equal(2 * s.Scale, Number(row, "closest_d3_mean"));
                Assert.Equal(JsonSerializer.Serialize(row), JsonSerializer.Serialize(Measure(transformed, s.Apply(p))));
            }
        }

        [Fact]
        public void SamplingIsAreaWeightedInteriorAndDeterministic()
        {
            var mesh = Mesh(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, new Vector3d(10, 0, 0), new Vector3d(12, 0, 0), new Vector3d(10, 2, 0));
            var a = ClosestFields.Samples(mesh, 1000, 0, new Dictionary<string, object>()).ToArray();
            var b = ClosestFields.Samples(mesh, 1000, 0, new Dictionary<string, object>()).ToArray();
            Assert.Equal(a, b);
            Assert.InRange(a.Count(p => p.x < 5), 199, 201);
            Assert.All(a, p => Assert.True(p.x > 0 && p.y > 0));
            var empty = new Dictionary<string, object>();
            ClosestFields.Add(Plane(), new DMesh3(), empty, 100);
            Assert.Equal("unavailable", empty["closest_status"]);
            Assert.Equal(100, empty["closest_missing"]);
        }

        // Independent oracle: plane projection with oriented half-space tests, then
        // clamped segment projections. No library point-triangle query is used.
        static Vector3d BruteClosest(Vector3d p, DMesh3 mesh)
        {
            Vector3d best = Vector3d.Zero; double distance = double.PositiveInfinity;
            foreach (int tid in mesh.TriangleIndices()) {
                var a = Vector3d.Zero; var b = a; var c = a;
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                Vector3d n = (b-a).Cross(c-a);
                Vector3d q = p - n * ((p-a).Dot(n) / n.Dot(n));
                var candidates = new List<Vector3d>();
                if ((b-a).Cross(q-a).Dot(n) >= 0 && (c-b).Cross(q-b).Dot(n) >= 0 && (a-c).Cross(q-c).Dot(n) >= 0)
                    candidates.Add(q);
                foreach (var edge in new[] { (a,b), (b,c), (c,a) }) {
                    Vector3d d = edge.Item2-edge.Item1;
                    double t = Math.Max(0, Math.Min(1, (p-edge.Item1).Dot(d) / d.Dot(d)));
                    candidates.Add(edge.Item1+t*d);
                }
                foreach (var candidate in candidates) {
                    double d = (p-candidate).LengthSquared;
                    if (d < distance) { best = candidate; distance = d; }
                }
            }
            return best;
        }

        [Fact]
        public void TreeAgreesWithIndependentBruteForceOnSteepFacets()
        {
            var mesh = Mesh(new Vector3d(0,0,0), new Vector3d(.1,0,10), new Vector3d(0,4,0),
                            new Vector3d(-10,0,3), new Vector3d(-5,0,3), new Vector3d(-10,5,3));
            var metric = new ClosestFields(mesh); var random = new Random(42);
            for (int i=0; i<200; i++) {
                var p = new Vector3d(-12+15*random.NextDouble(), -2+9*random.NextDouble(), -1+14*random.NextDouble());
                Assert.True(metric.TryClosest(p, out var q));
                Vector3d expected = BruteClosest(p, mesh);
                Near(expected, q, 1e-9);
                var row = Measure(mesh, p);
                Vector3d delta = p-expected;
                Assert.Equal(delta.Length, Number(row,"closest_d3_mean"), 9);
                Assert.Equal(new Vector2d(delta.x,delta.y).Length, Number(row,"closest_dh_mean"), 9);
                Assert.Equal(Math.Abs(delta.z), Number(row,"closest_dv_mean"), 9);
            }
        }

        [Fact]
        public void TiesAreDeterministicForAFixedTree()
        {
            var m = Mesh(new Vector3d(-10,-10,0),new Vector3d(30,-10,0),new Vector3d(-10,30,0), new Vector3d(0,-10,-10),new Vector3d(0,30,-10),new Vector3d(0,-10,30));
            var metric = new ClosestFields(m); var p = new Vector3d(1,0,1);
            Assert.True(metric.TryClosest(p, out var first));
            Assert.True(first.Distance(new Vector3d(1,0,0)) < 1e-12 || first.Distance(new Vector3d(0,0,1)) < 1e-12);
            for (int i=0;i<10;i++) { Assert.True(metric.TryClosest(p,out var q)); Assert.Equal(first,q); }
        }
    }
}
