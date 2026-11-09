using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Refinement on the grid of an explicit tolerance (D14, MESHING2D_SNAP.md section 3g) on hull domains (D13): the hull edges are
    /// snap-rounded with the segments and split in their envelopes (owner decision 2026-11-02, option C of MESHING2D_HULL_GRID.md).
    /// Found with the Herne tiles of the reference comparison (OffGrid vertices at snap 0.01); the acceptance test is the fuzz of
    /// section 5 there: input points a few millimetres inside a sloped hull edge.
    /// </summary>
    public class QualityMesh2HullGridTests
    {
        static OracleReport Judge(Mesh2Input input, out Mesh2Output output)
        {
            output = new QualityMesh2Candidate().Run(input);
            return Oracle.Check(input, output, new OracleExpect { Milestone = Milestone.M3 });
        }

        /// <summary>
        /// The fuzz input of MESHING2D_HULL_GRID.md section 5: a hull edge of 64 m at one of six slopes, a third hull point, and 3 to 8
        /// input points 2 to 9 mm inside the edge on the grid of 0.01; 20 or 30 degrees, snap 0.01.
        /// </summary>
        internal static Mesh2Input Fuzz(int seed)
        {
            var rnd = new Random(seed);
            double[] slopes = { 0.005, 0.0073, 0.011, 0.0037, 0.13, 0.41 };
            double slope = slopes[rnd.Next(slopes.Length)];
            double R(double v) => Math.Round(v * 100) / 100;
            var x = new List<double> { 0, 64, 32 };
            var y = new List<double> { 0, R(64 * slope), R(32 * slope + 3 + rnd.NextDouble() * 17) };
            int extra = 3 + rnd.Next(6);
            for (int i = 0; i < extra; ++i) {
                double px = R(2 + rnd.NextDouble() * 60), py = R(px * slope + 0.002 + rnd.NextDouble() * 0.007);
                if (py <= px * slope) py = R(py + 0.01);
                x.Add(px); y.Add(py);
            }
            return new Mesh2Input { X = x.ToArray(), Y = y.ToArray(), MinAngleDeg = rnd.Next(2) == 0 ? 20 : 30, SnapTolerance = 0.01, SteinerLimit = 200000 };
        }

        internal static Mesh2Input Moved(Mesh2Input input, Similarity s)
        {
            var moved = new Mesh2Input { Segments = input.Segments, SnapTolerance = input.SnapTolerance * s.Scale, MinAngleDeg = input.MinAngleDeg,
                SteinerLimit = input.SteinerLimit, X = new double[input.Count], Y = new double[input.Count] };
            for (int i = 0; i < input.Count; ++i) { var q = s.Apply(new Vector3d(input.X[i], input.Y[i], 0)); moved.X[i] = q.x; moved.Y[i] = q.y; }
            return moved;
        }

        /// <summary>
        /// A polyline of two segments along a hull edge at a small angle (reduced from herne_381000_5710400 at snap 0.01, moved by
        /// (-380000, -5710000)): a grid split of a hull piece checked its new pieces against the triangles near it starting from the
        /// ghost side of the edge, found none, and its pieces crossed the segment's chain at an unrounded vertex (OffGrid, the
        /// reported finding).
        /// </summary>
        [Theory]
        [InlineData(20.0)]
        [InlineData(30.0)]
        public void HullSplit_ChecksItsPiecesFromTheRealSide_StaysOnTheGrid(double angle)
        {
            var input = new Mesh2Input { X = new[] { 881.998, 882.826, 885.111 }, Y = new[] { 780.666, 775.308, 756.895 }, MinAngleDeg = angle, SnapTolerance = 0.01, SteinerLimit = 200000 };
            input.Segments.Add(new SegmentInput(0, 1));
            input.Segments.Add(new SegmentInput(1, 2));
            var report = Judge(input, out var output);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> Similarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary>
        /// The fuzz in every similarity frame (the tolerance scaled alike): every mesh passes the oracle, and the 2^k scales give the
        /// mesh of the identity frame, bit for bit. Before the fix about four in ten failed (OffGrid, AngleBelowBound,
        /// BoundaryNotConstrained, TriangleOutsideDomain).
        /// </summary>
        [Theory]
        [MemberData(nameof(Similarities))]
        public void Fuzz_PointsInsideASlopedHullEdge_PassTheOracle(string name)
        {
            var s = Similarity.ByName(name);
            var failures = new List<string>();
            for (int seed = 0; seed < 24; ++seed) {
                var input = Fuzz(seed);
                var report = Judge(Moved(input, s), out var output);
                if (output.Status != Mesh2Status.Success || !report.Ok) { failures.Add("seed " + seed + ": " + output.FailureKind + " " + report); continue; }
                if (s.IsExact) {
                    Judge(input, out var plain);
                    if (!plain.VX.Select(v => v * s.Scale).SequenceEqual(output.VX) || !plain.VY.Select(v => v * s.Scale).SequenceEqual(output.VY)
                        || !plain.Triangles.SelectMany(t => t).SequenceEqual(output.Triangles.SelectMany(t => t)))
                        failures.Add("seed " + seed + ": not the identity frame's mesh, scaled");
                }
            }
            Assert.True(failures.Count == 0, name + ": " + failures.Count + " of 24 fail\n" + string.Join("\n", failures.Take(5)));
        }

        /// <summary>
        /// A hull tip narrower than a pixel may collapse (owner decision 2026-11-02, Q-H4): the hull triangle (0, 0), (10, 0), (10, 0.6)
        /// with the point (6, 0.2), pixel 1: both hull edges pass the point's pixel (6, 0), so the rounding of the input routes both
        /// chains through it and the tip (0, 0) - (6, 0) has no area; the oracle accepts the corner in no triangle, and the mesher reports
        /// it. (Was fuzz seed 11 in the rotated frame, whose tip refinement collapsed by rerouting one hull chain onto the other through
        /// a grid point: no longer, since the refinement keeps what the rounding of the input keeps, Q17.)
        /// </summary>
        [Fact]
        public void CollapsedHullTip_IsReported()
        {
            var input = new Mesh2Input { X = new[] { 0.0, 10, 10, 6 }, Y = new[] { 0.0, 0, 0.6, 0.2 }, MinAngleDeg = 20, SnapTolerance = 1, SteinerLimit = 200000 };
            var report = Judge(input, out var output);
            Assert.True(report.Ok, report.ToString());
            // the same run as the judged one: the M3 candidate is conforming (O2); without it, lenses (Conforming = false) give another mesh
            var result = new QualityMesh2 { MinAngle = input.MinAngleDeg.Value, SnapTolerance = input.SnapTolerance, SteinerLimit = input.SteinerLimit, Conforming = true }
                .Compute(ConstrainedDelaunayMesh2Candidate.Convert(input));
            Assert.Contains(result.Diagnostics, d => d.Contains("vertices of the hull chains in no triangle"));
        }

        /// <summary>
        /// A collapsed tip takes the input points on its shared chain along (review of the rule): a hull triangle of 7.1 degrees at the
        /// corner (0, 0), pixel 1/4, with input points (0.5, 0.03) and (0.76, 0.05) next to the corner; the oracle accepts them in no
        /// triangle only because both hull edges pass their pixels
        /// </summary>
        [Fact]
        public void CollapsedHullTip_TakesThePointsOnItsChainAlong()
        {
            var input = new Mesh2Input { X = new[] { 0.0, 10, 10, 0.76, 0.5 }, Y = new[] { 0, 0, 1.25, 0.05, 0.03 }, MinAngleDeg = 20, SnapTolerance = 0.25, SteinerLimit = 200000 };
            var report = Judge(input, out var output);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.True(report.Ok, report.ToString());
        }

        /// <summary> the plain square-ish hull domain of these tests, with a hole ring of four points and a point in the hole </summary>
        static ConstrainedMesh2Input WithHole(double holeSize)
        {
            var input = new ConstrainedMesh2Input();
            foreach (var (x, y) in new[] { (0.0, 0.0), (8.0, 0.13), (8.0, 6.0), (0.0, 5.5) }) input.Points.Add(new Vector2d(x, y));
            int h = input.Points.Count;
            foreach (var (x, y) in new[] { (4.0, 3.0), (4.0 + holeSize, 3.0), (4.0 + holeSize, 3.0 + holeSize), (4.0, 3.0 + holeSize) }) input.Points.Add(new Vector2d(x, y));
            input.Points.Add(new Vector2d(4.0 + holeSize / 2, 3.0 + holeSize / 2));
            input.Rings.Add(new Mesh2Ring(new[] { h, h + 1, h + 2, h + 3 }, true));
            return input;
        }

        /// <summary>
        /// A hole ring that snapping collapses (smaller than a pixel) becomes segments behind the hull chains; they must not be taken
        /// for hull chains (the end of the hull segments' range; review). And a point in a hole lies in no triangle without being a
        /// vertex of a hull chain: the diagnostic counts only those.
        /// </summary>
        [Theory]
        [InlineData(0.004)]   // collapses at pixel 0.01
        [InlineData(1.0)]     // stays a hole, with a point inside
        public void HoleRing_InAHullDomainOnTheGrid(double holeSize)
        {
            var result = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 200000 }.Compute(WithHole(holeSize));
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.DoesNotContain(result.Diagnostics, d => d.Contains("vertices of the hull chains in no triangle"));
            var mesh = result.Mesh;
            // the hull's corners and the snapped hole are in the mesh; every subsegment on the hull has marker 0
            foreach (var corner in new[] { new Vector2d(0, 0), new Vector2d(8, 0.13), new Vector2d(8, 6), new Vector2d(0, 5.5) })
                Assert.Contains(mesh.Triangles, t => mesh.Vertices[t.a] == corner || mesh.Vertices[t.b] == corner || mesh.Vertices[t.c] == corner);
            if (holeSize > 0.01) {
                var inside = new Vector2d(4.5, 3.5);
                bool Covers(Index3i t)
                {
                    Vector2d a = mesh.Vertices[t.a], b = mesh.Vertices[t.b], c = mesh.Vertices[t.c];
                    double o1 = (b - a).Perp.Dot(inside - a), o2 = (c - b).Perp.Dot(inside - b), o3 = (a - c).Perp.Dot(inside - c);
                    return (o1 >= 0 && o2 >= 0 && o3 >= 0) || (o1 <= 0 && o2 <= 0 && o3 <= 0);
                }
                Assert.DoesNotContain(mesh.Triangles, Covers);
            }
        }

        /// <summary>
        /// Nothing to refine: no hull chains are added, and the mesh is that of ConstrainedDelaunayMesh2 at the same tolerance (the
        /// hull segments only where refinement runs; review)
        /// </summary>
        [Fact]
        public void NothingToRefine_SameMeshAsTheConstrainedMesher()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(5));
            var quality = new QualityMesh2 { MinAngle = 0, SnapTolerance = 0.01 }.Compute(input);
            var constrained = new ConstrainedDelaunayMesh2 { SnapTolerance = 0.01 }.Compute(input);
            Assert.Equal(constrained.Mesh.Vertices, quality.Mesh.Vertices);
            Assert.Equal(constrained.Mesh.Triangles, quality.Mesh.Triangles);
            Assert.Equal(constrained.Mesh.Subsegments.Count, quality.Mesh.Subsegments.Count);
        }

        /// <summary>
        /// An input segment along a hull edge keeps its marker and its lock where the hull chain shares its pieces, and heights are
        /// interpolated as on every path (the input is extended by the hull segments, not replaced; review)
        /// </summary>
        [Fact]
        public void SegmentOnAHullEdge_KeepsMarkerLockAndHeights()
        {
            var input = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(2));
            input.Segments.Add(new Mesh2Segment(0, 1, 5, true));
            input.Heights = input.Points.Select(p => 0.5 * p.x + 2 * p.y).ToList();
            int segments = input.Segments.Count;
            var result = new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.01, SteinerLimit = 200000 }.Compute(input);
            Assert.Equal(Triangulation2Status.Success, result.Status);
            Assert.Equal(segments, input.Segments.Count);   // the caller's input is not changed
            var mesh = result.Mesh;
            var onEdge = mesh.Subsegments.Where(g => g.Marker == 5).ToList();
            Assert.NotEmpty(onEdge);
            Assert.All(onEdge, g => Assert.True(g.Locked));
            Assert.NotNull(mesh.Heights);
            // (a plane: a vertex snapping moved carries the height of where it came from, at most half a pixel's diagonal away, so
            // within |gradient| 0.0071 of the plane at its own position)
            for (int i = 0; i < mesh.Vertices.Count; ++i)
                Assert.True(Math.Abs(0.5 * mesh.Vertices[i].x + 2 * mesh.Vertices[i].y - mesh.Heights[i]) <= 2.07 * 0.0071, "vertex " + i);
        }

        [Fact]
        public void HullOnTheGrid_Reuse_GivesTheSameMesh()
        {
            var a = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(3));
            var b = ConstrainedDelaunayMesh2Candidate.Convert(Fuzz(4));
            var mesher = new QualityMesh2 { SnapTolerance = 0.01, MinAngle = 20, SteinerLimit = 200000 };
            var first = mesher.Compute(a);
            var other = mesher.Compute(b);
            var again = mesher.Compute(a);
            var fresh = new QualityMesh2 { SnapTolerance = 0.01, MinAngle = 20, SteinerLimit = 200000 }.Compute(b);
            Assert.Equal(Triangulation2Status.Success, first.Status);
            Assert.Equal(first.Mesh.Vertices, again.Mesh.Vertices);
            Assert.Equal(first.Mesh.Triangles, again.Mesh.Triangles);
            Assert.Equal(fresh.Mesh.Vertices, other.Mesh.Vertices);
            Assert.Equal(fresh.Mesh.Triangles, other.Mesh.Triangles);
        }

        /// <summary>
        /// The Herne tiles themselves (polylines of ALKIS parcels and buildings; not committed): with G3_HERNE_DIR set to a folder of
        /// herne_*.txt files ("L x,y x,y ..." per polyline), each tile at 20 and 30 degrees with snap 0.01 must pass the oracle.
        /// Takes some minutes.
        /// </summary>
        [Fact]
        public void HerneTiles_OnTheGrid_PassTheOracle()
        {
            string dir = Environment.GetEnvironmentVariable("G3_HERNE_DIR");
            if (dir == null || !Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "herne_*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();
            Assert.NotEmpty(files);
            foreach (var file in files)
                foreach (double angle in new[] { 20.0, 30.0 }) {
                    var input = ReadTile(file);
                    input.MinAngleDeg = angle; input.SnapTolerance = 0.01; input.SteinerLimit = 200000;
                    var report = Judge(input, out var output);
                    Assert.True(output.Status == Mesh2Status.Success, Path.GetFileName(file) + " " + angle + ": " + output.FailureKind);
                    Assert.True(report.Ok, Path.GetFileName(file) + " " + angle + ": " + report);
                }
        }

        /// <summary> the polylines of a tile as segments between their points, each point once, each segment once </summary>
        static Mesh2Input ReadTile(string path)
        {
            var index = new Dictionary<(string, string), int>();
            var x = new List<double>(); var y = new List<double>();
            var segments = new HashSet<(int, int)>();
            var input = new Mesh2Input();
            foreach (var line in File.ReadLines(path)) {
                if (!line.StartsWith("L", StringComparison.Ordinal)) continue;
                int previous = -1;
                foreach (var token in line.Substring(1).Split((char[])null, StringSplitOptions.RemoveEmptyEntries)) {
                    var p = token.Split(',');
                    if (!index.TryGetValue((p[0], p[1]), out int id)) {
                        id = x.Count; index.Add((p[0], p[1]), id);
                        x.Add(double.Parse(p[0], CultureInfo.InvariantCulture)); y.Add(double.Parse(p[1], CultureInfo.InvariantCulture));
                    }
                    if (previous >= 0 && previous != id && segments.Add((Math.Min(previous, id), Math.Max(previous, id)))) input.Segments.Add(new SegmentInput(previous, id));
                    previous = id;
                }
            }
            input.X = x.ToArray(); input.Y = y.ToArray();
            return input;
        }
    }
}
