using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The M3 mesher <see cref="QualityMesh2"/>. Its catalogue rows (m3.refine and the M3 rows of the M2 features) run through the
    /// oracle in Meshing2CatalogueTests; these tests add fuzzing, the CLAUDE.md requirements and the API.
    /// </summary>
    public class QualityMesh2Tests
    {
        static OracleReport Judge(Mesh2Input input, out Mesh2Output output)
        {
            output = new QualityMesh2Candidate().Run(input);
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
        }

        static int AddPoint(Mesh2Input input, double x, double y)
        {
            input.X = input.X.Concat(new[] { x }).ToArray();
            input.Y = input.Y.Concat(new[] { y }).ToArray();
            return input.Count - 1;
        }

        static Mesh2Input Fuzz(int seed)
        {
            var r = new Random(seed);
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0 }, Y = new double[] { 0, 0, 10, 10 } };
            if (seed % 3 != 0) input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            int m = 2 + r.Next(6);
            for (int i = 0; i < m; ++i)
                input.Segments.Add(new SegmentInput(AddPoint(input, r.Next(11), r.Next(11)), AddPoint(input, r.Next(11), r.Next(11)), r.Next(3)));
            for (int i = 0; i < r.Next(5); ++i) AddPoint(input, r.NextDouble() * 10, r.NextDouble() * 10);
            input.MinAngleDeg = new[] { 0, 20, 25, 30 }[seed % 4];
            if (seed % 5 == 0) input.MaxArea = 0.5;
            input.SteinerLimit = 20000;
            return input;
        }

        [Fact]
        public void Fuzz_EveryResultPassesTheOracle()
        {
            for (int seed = 0; seed < 60; ++seed) {
                var input = Fuzz(seed);
                var report = Judge(input, out var output);
                Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.FailureKind);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
        }

        [Theory]
        [InlineData(-1.0)]
        [InlineData(60.0)]
        [InlineData(70.0)]
        [InlineData(double.NaN)]
        public void InvalidMinAngle_IsAnArgumentError(double angle)
        {
            // D12: an invalid angle is rejected, so it cannot silently disable the other bounds (F-30)
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(10, 0), new Vector2d(10, 10), new Vector2d(0, 10) } };
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { MinAngle = angle, MaxArea = 0.5 }.Compute(input));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { MaxArea = -1 }.Compute(input));
            // a valid angle with the same area works and meets the area
            var mesh = new QualityMesh2 { MinAngle = 20, MaxArea = 0.5 }.Compute(input).Mesh;
            Assert.All(mesh.Triangles, t => Assert.True(Area(mesh, t) <= 0.5));
        }

        static double Area(ConstrainedMesh2 m, Index3i t)
        {
            var a = m.Vertices[t.a]; var b = m.Vertices[t.b]; var c = m.Vertices[t.c];
            return 0.5 * ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        }

        static Mesh2Input Sample()
        {
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 2, 8, 3, 3 }, Y = new double[] { 0, 0, 10, 10, 2, 7, 8, 1 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            input.Segments.Add(new SegmentInput(4, 5, 2)); input.Segments.Add(new SegmentInput(6, 7, 3));
            input.MinAngleDeg = 25; input.MaxArea = 2;
            return input;
        }

        static string[] Canonical(Mesh2Output o) => o.Triangles.Select(t => string.Join(" ", t)).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        [Theory]
        [MemberData(nameof(Similarities))]
        public void Similarity_KeepsAValidResult(string name)
        {
            var s = Similarity.All().First(x => x.Name == name);
            var input = Sample();
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            var moved = Sample();
            for (int i = 0; i < input.Count; ++i) {
                moved.X[i] = s.Scale * (cos * input.X[i] - sin * input.Y[i]) + s.Translation.x;
                moved.Y[i] = s.Scale * (sin * input.X[i] + cos * input.Y[i]) + s.Translation.y;
            }
            moved.MaxArea = input.MaxArea * s.Scale * s.Scale;
            Assert.True(Judge(input, out var a).Ok);
            var report = Judge(moved, out var b);
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                // 2^k: bit for bit the same mesh, Steiner points scaled exactly
                Assert.Equal(Canonical(a), Canonical(b));
                for (int i = 0; i < a.VX.Length; ++i) { Assert.Equal(a.VX[i] * s.Scale, b.VX[i]); Assert.Equal(a.VY[i] * s.Scale, b.VY[i]); }
            }
        }

        [Fact]
        public void Reuse_And_Parallel_RunsAreIdentical()
        {
            var mesher = new QualityMesh2 { MinAngle = 30, MaxArea = 1 };
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Sample());
            var first = mesher.Compute(input).Mesh;
            var results = new ConstrainedMesh2[8];
            Parallel.For(0, results.Length, i => results[i] = mesher.Compute(input).Mesh);
            foreach (var m in results.Concat(new[] { mesher.Compute(input).Mesh })) {
                Assert.Equal(first.Vertices, m.Vertices);
                Assert.Equal(first.Triangles, m.Triangles);
                Assert.Equal(first.Neighbours, m.Neighbours);
                Assert.Equal(first.BadTriangles, m.BadTriangles);
            }
        }

        [Fact]
        public void SteinerLimit_StopsWithAValidMesh_AndTheFlag()
        {
            var input = Sample();
            input.MinAngleDeg = 33; input.MaxArea = 0.001; input.SteinerLimit = 500;
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            Assert.True(o.SteinerLimitReached);
            Assert.True(o.VX.Length - input.Count <= 500);
            // without a limit problem the flag stays off
            Assert.True(Judge(Sample(), out var free).Ok);
            Assert.False(free.SteinerLimitReached);
        }

        [Fact]
        public void PointSet_KeepsItsHull()
        {
            // F-35: a point set refined: the convex hull is the boundary, no vertex outside it
            var r = new Random(35);
            var input = new Mesh2Input { X = Enumerable.Range(0, 40).Select(_ => r.NextDouble() * 10).ToArray(), Y = Enumerable.Range(0, 40).Select(_ => r.NextDouble() * 10).ToArray() };
            input.MinAngleDeg = 30;
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            var hull = new DelaunayMesh2().Compute(Enumerable.Range(0, 40).Select(i => new Vector2d(input.X[i], input.Y[i])).ToArray()).Mesh;
            // a split of a hull edge may round a unit in the last place outwards (O2), never farther
            var hullEdges = hull.Edges().Where(e => hull.IncidentTriangles(e.a).Intersect(hull.IncidentTriangles(e.b)).Count() == 1).ToList();
            for (int v = 0; v < o.VX.Length; ++v) {
                var q = new Vector2d(o.VX[v], o.VY[v]);
                if (hull.TryLocate(q, out _)) continue;
                double d = hullEdges.Min(e => new Segment2d(hull.Vertices[e.a], hull.Vertices[e.b]).DistanceSquared(q));
                Assert.True(d < 1e-24, "vertex " + v + " is " + Math.Sqrt(d) + " outside the hull");
            }
        }

        [Fact]
        public void Heights_OfSteinerPoints_ComeFromTheReferenceMesh()
        {
            // a plane z = x + 2y: linear interpolation on any triangulation reproduces it exactly enough
            var input = new ConstrainedMesh2Input { Points = { new Vector2d(0, 0), new Vector2d(8, 0), new Vector2d(8, 8), new Vector2d(0, 8), new Vector2d(3, 5) } };
            input.Heights = input.Points.Select(p => p.x + 2 * p.y).ToList();
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }));
            var m = new QualityMesh2 { MinAngle = 30, MaxArea = 0.5 }.Compute(input).Mesh;
            Assert.True(m.Vertices.Count > 50);
            for (int v = 0; v < m.Vertices.Count; ++v) Assert.Equal(m.Vertices[v].x + 2 * m.Vertices[v].y, m.Heights[v], 9);
        }

        [Fact]
        public void LockedSegments_AreNotSplit()
        {
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 1, 9 }, Y = new double[] { 0, 0, 10, 10, 5, 5.5 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            input.Segments.Add(new SegmentInput(4, 5, 7, locked: true));
            input.MinAngleDeg = 25; input.MaxArea = 1;
            var o = new QualityMesh2Candidate().Run(input);
            var locked = o.Subsegments.Where(s => s.Marker == 7).ToList();
            Assert.Single(locked);
            Assert.True((locked[0].A == 4 && locked[0].B == 5) || (locked[0].A == 5 && locked[0].B == 4));
        }

        [Fact]
        public void Cancellation_Throws()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Sample());
            Assert.Throws<OperationCanceledException>(() => new QualityMesh2 { MaxArea = 0.0001 }.Compute(input, new CancellationToken(true)));
        }
    }
}
