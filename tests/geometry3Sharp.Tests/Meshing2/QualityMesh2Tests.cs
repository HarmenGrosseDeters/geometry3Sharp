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

        /// <summary> holes, regions with and without an area bound, locked segments: every input class the class doc advertises </summary>
        static Mesh2Input FuzzDomains(int seed)
        {
            var r = new Random(1000 + seed);
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0 }, Y = new double[] { 0, 0, 10, 10 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false, 1));
            int holes = 1 + r.Next(4);
            for (int h = 0; h < holes; ++h) {
                // small triangles in disjoint cells of a 4 x 4 grid, so the rings neither cross nor touch
                double cx = 1.25 + 2.5 * ((seed + 5 * h) % 4), cy = 1.25 + 2.5 * (((seed + 5 * h) / 4 + h) % 4), s = 0.2 + r.NextDouble();
                int a = AddPoint(input, cx - s, cy - s * r.NextDouble()), b = AddPoint(input, cx + s, cy - s * r.NextDouble()), c = AddPoint(input, cx + s * (r.NextDouble() - 0.5), cy + s);
                switch (r.Next(3)) {
                    case 0: input.Rings.Add(new RingInput(new[] { a, b, c }, true, 2)); break;
                    case 1: input.Regions.Add(new RegionInput { Ring = new[] { a, b, c }, Label = 7 + h }); break;
                    default: input.Regions.Add(new RegionInput { Ring = new[] { a, b, c }, Label = 7 + h, MaxArea = 0.01 }); break;
                }
            }
            input.MinAngleDeg = 20 + r.Next(14);
            input.SteinerLimit = 20000;
            return input;
        }

        [Fact]
        public void Fuzz_HolesAndRegions_PassTheOracle()
        {
            // review of M3: a split lifts the constraint of its piece, the flips around it may cross the boundary, and a small hole or
            // region enclosed by constraints kept the tags of the slots it was rewritten into (filled holes, wrong labels)
            for (int seed = 0; seed < 80; ++seed) {
                var input = FuzzDomains(seed);
                var report = Judge(input, out var output);
                Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.FailureKind);
                Assert.True(report.Ok, "seed " + seed + ": " + report);
            }
        }

        static Mesh2Input SmallHole(bool region)
        {
            var input = new Mesh2Input { X = new double[] { 0, 10, 10, 0, 4, 6, 5, 5 }, Y = new double[] { 0, 0, 10, 10, 5, 5, 5.3, 4.7 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            if (region) input.Regions.Add(new RegionInput { Ring = new[] { 4, 5, 6 }, Label = 7 });
            else input.Rings.Add(new RingInput(new[] { 4, 5, 6 }, true));
            input.Conforming = true;
            return input;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Split_NextToASmallHoleOrRegion_KeepsItsTag(bool region)
        {
            // the review's case: the encroached hole edge 4-5 is split, its constraint lifted; the hole stays empty, the region labelled
            var input = SmallHole(region);
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            double area = 0;
            for (int t = 0; t < o.Triangles.Length; ++t) {
                var a = o.Triangles[t];
                double at = 0.5 * ((o.VX[a[1]] - o.VX[a[0]]) * (o.VY[a[2]] - o.VY[a[0]]) - (o.VY[a[1]] - o.VY[a[0]]) * (o.VX[a[2]] - o.VX[a[0]]));
                area += at;
            }
            Assert.Equal(region ? 100 : 100 - 0.3, area, 9);
            if (region) {
                double labelled = 0;
                for (int t = 0; t < o.Triangles.Length; ++t) {
                    if (o.TriangleLabels[t] != 7) continue;
                    var a = o.Triangles[t];
                    labelled += 0.5 * ((o.VX[a[1]] - o.VX[a[0]]) * (o.VY[a[2]] - o.VY[a[0]]) - (o.VY[a[1]] - o.VY[a[0]]) * (o.VX[a[2]] - o.VX[a[0]]));
                }
                Assert.Equal(0.3, labelled, 9);
            }
        }

        [Theory]
        [InlineData(-560)]
        [InlineData(-540)]
        [InlineData(-500)]
        [InlineData(-200)]
        [InlineData(200)]
        [InlineData(225)]
        public void ExtremeScales_RefineTheSameMesh(int exponent)
        {
            // review of M3: at 2^-500 the squared lengths underflowed and no triangle counted as bad (silent success)
            var input = Sample(); input.MaxArea = null;
            var scaled = Sample(); scaled.MaxArea = null;
            for (int i = 0; i < input.Count; ++i) { scaled.X[i] = ExactPoint3.ScaleB(input.X[i], exponent); scaled.Y[i] = ExactPoint3.ScaleB(input.Y[i], exponent); }
            Assert.True(Judge(input, out var a).Ok);
            var report = Judge(scaled, out var b);
            Assert.True(report.Ok, report.ToString());
            Assert.Equal(Canonical(a), Canonical(b));
            for (int i = 0; i < a.VX.Length; ++i) {
                Assert.Equal(ExactPoint3.ScaleB(a.VX[i], exponent), b.VX[i]);
                Assert.Equal(ExactPoint3.ScaleB(a.VY[i], exponent), b.VY[i]);
            }
        }

        static Mesh2Input FuzzLocked(int seed)
        {
            var input = Fuzz(seed);
            foreach (var s in input.Segments) s.Locked = s.Marker == 1;
            if (input.MinAngleDeg == 0) input.MinAngleDeg = 20;
            return input;
        }

        [Fact]
        public void Fuzz_LockedSegments_StayWholeAndValid()
        {
            // review of M3: near locked segments the bounds are not met yet (m3.locks, documented on QualityMesh2). What holds: the
            // mesh is valid, locked segments keep their vertices, and every triangle left below the angle is in the bad list
            var allowed = new HashSet<Violation> { Violation.AngleBelowBound, Violation.AreaAboveBound };
            for (int seed = 0; seed < 60; ++seed) {
                var input = FuzzLocked(seed);
                var report = Judge(input, out var output);
                Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.FailureKind);
                var other = report.Violations.Where(v => !allowed.Contains(v.Kind)).ToList();
                Assert.True(other.Count == 0, "seed " + seed + ": " + string.Join("; ", other.Select(v => v.Kind + ": " + v.Detail)));
                foreach (var s in input.Segments.Where(s => s.Locked)) {
                    int a = output.InputToVertex[s.A], b = output.InputToVertex[s.B];
                    // whole unless another segment crosses it (then split only at crossings, which are not Steiner points of refinement)
                    bool crossed = output.Subsegments.Count(q => q.Marker == s.Marker) > input.Segments.Count(q => q.Marker == s.Marker);
                    if (!crossed) Assert.Contains(output.Subsegments, q => (q.A == a && q.B == b) || (q.A == b && q.B == a));
                }
            }
        }

        /// <summary>
        /// A polygon with a narrow wedge at a small angle (0.5 to 5 degrees) between two ring edges from one vertex, at coordinates in
        /// the thousands, the wedge inside or outside the domain (corpus run: earcut's water and water-huge)
        /// </summary>
        static Mesh2Input Wedge(int seed)
        {
            var r = new Random(2000 + seed);
            double ax = 4000 + r.Next(200), ay = 3000 + r.Next(200), len = 20 + r.Next(80), angle = (0.5 + 4.5 * r.NextDouble()) * Math.PI / 180;
            double dir = r.NextDouble() * 2 * Math.PI;
            // A, then B along dir, C at the small angle from it; the rest of the ring closes around them
            var pts = new List<double[]> { new[] { ax, ay } };
            pts.Add(new[] { Math.Round(ax + len * Math.Cos(dir)), Math.Round(ay + len * Math.Sin(dir)) });
            pts.Add(new[] { Math.Round(ax + 1.3 * len * Math.Cos(dir + angle)), Math.Round(ay + 1.3 * len * Math.Sin(dir + angle)) });
            double far = 3 * len;
            pts.Add(new[] { Math.Round(ax + far * Math.Cos(dir + angle + 1.2)), Math.Round(ay + far * Math.Sin(dir + angle + 1.2)) });
            pts.Add(new[] { Math.Round(ax + far * Math.Cos(dir + Math.PI)), Math.Round(ay + far * Math.Sin(dir + Math.PI)) });
            pts.Add(new[] { Math.Round(ax + far * Math.Cos(dir - 1.2)), Math.Round(ay + far * Math.Sin(dir - 1.2)) });
            var input = new Mesh2Input { X = pts.Select(p => p[0]).ToArray(), Y = pts.Select(p => p[1]).ToArray() };
            // the wedge between A-B and A-C is outside (ring A, C, ..., B) or the domain is the wedge side (ring reversed)
            var ring = seed % 2 == 0 ? new[] { 0, 2, 3, 4, 5, 1 } : new[] { 0, 1, 5, 4, 3, 2 };
            input.Rings.Add(new RingInput(ring, false, 1));
            if (seed % 3 == 0) input.Segments.Add(new SegmentInput(0, 3, 2));
            input.MinAngleDeg = new[] { 0, 20, 30 }[seed % 3];
            input.SteinerLimit = 30000;
            return input;
        }

        /// <summary>
        /// The tag check (tags against domain membership from scratch, after every operation) on the catalogue rows. On a hull domain a
        /// hull edge whose split point rounds inwards leaves a sliver between its pieces and the old, now unconstrained hull edge: the
        /// tags put it outside the constrained hull, and so must the check (F-35 and F-3/c failed it; the check reached them only when another test had switched it on in parallel).
        /// </summary>
        [Fact]
        public void TagCheck_HoldsOnTheCoveredCatalogueRows()
        {
            QualityMesh2.Refiner.CheckTagsForTests = true;
            try {
                foreach (var row in Catalogue.Rows().Where(r => r.Case != null && r.State == CaseState.Covered && !r.Slow))
                    CandidateRegistry.For(row.Case.Milestone).Run(row.Case.Input);   // throws InvalidOperationException on a wrong tag
            } finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
        }

        /// <summary>
        /// The switch of the slow tag check is per thread: a test that turns it on must not reach meshers other tests run in parallel
        /// (master CI failed F-35 in Meshing2OracleModesTests intermittently while Fuzz_NarrowWedges had it on).
        /// </summary>
        [Fact]
        public void TagCheckSwitch_DoesNotReachOtherThreads()
        {
            QualityMesh2.Refiner.CheckTagsForTests = true;
            try {
                bool seen = true;
                var thread = new Thread(() => seen = QualityMesh2.Refiner.CheckTagsForTests);
                thread.Start();
                thread.Join();
                Assert.False(seen);
                Assert.True(QualityMesh2.Refiner.CheckTagsForTests);
            } finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
        }

        /// <summary>
        /// D13: on a hull domain the hull is the boundary, so a segment from a hull vertex at a small angle to a hull edge is a small input
        /// angle like two segments. Refinement must stop at it (Shewchuk 2002) instead of cascading into the apex until the merge tolerance
        /// stops it, leaving triangles below the bound that nothing excuses (found with CDT's issue-211 in the reference comparison: a
        /// segment at 0.096 degrees to a hull edge; 7 triangles below 20 degrees, and the Steiner limit at 30 degrees).
        /// </summary>
        [Theory]
        [InlineData(20.0, 0.0134)]   // 0.096 degrees, as in issue-211
        [InlineData(30.0, 0.0134)]
        [InlineData(20.0, 0.25)]     // 1.8 degrees
        [InlineData(20.0, -2.0)]     // 0.096 degrees, with an input point at (2, 0) on the hull edge (its hull sources are split there)
        public void SmallAngle_BetweenASegmentAndTheHull_IsAnInputAngle(double angle, double rise)
        {
            // a point set (hull domain): the segment from the hull vertex (0, 0) to (8, rise), just above the hull edge to (10, 0)
            bool collinear = rise < 0;
            if (collinear) rise = 0.0134;
            var input = new Mesh2Input { X = new[] { 0.0, 10, 10, 0, 8, 3, collinear ? 2 : 3 }, Y = new[] { 0, 0, 10, 10, rise, 6, collinear ? 0 : 6 }, MinAngleDeg = angle, SteinerLimit = 20000 };
            input.Segments.Add(new SegmentInput(0, 4));
            var report = Judge(input, out var output);
            Assert.Equal(Mesh2Status.Success, output.Status);
            Assert.False(output.SteinerLimitReached, "refinement cascaded into the apex");
            Assert.True(report.Ok, report.ToString());
        }

        public static IEnumerable<object[]> HullAngleSimilarities() => Similarity.All().Select(s => new object[] { s.Name });

        /// <summary> the same under every similarity: 2^k scales bit for bit, the others pass the oracle </summary>
        [Theory]
        [MemberData(nameof(HullAngleSimilarities))]
        public void SmallAngle_BetweenASegmentAndTheHull_Invariance(string name)
        {
            var s = Similarity.ByName(name);
            double[] x = { 0.0, 10, 10, 0, 8, 3 }, y = { 0, 0, 10, 10, 0.0134, 6 };
            Mesh2Input Make(Func<double, double, Vector3d> f)
            {
                var input = new Mesh2Input { X = new double[6], Y = new double[6], MinAngleDeg = 20, SteinerLimit = 20000 };
                for (int i = 0; i < 6; ++i) { var q = f(x[i], y[i]); input.X[i] = q.x; input.Y[i] = q.y; }
                input.Segments.Add(new SegmentInput(0, 4));
                return input;
            }
            var moved = Make((a, b) => s.Apply(new Vector3d(a, b, 0)));
            var report = Judge(moved, out var output);
            Assert.False(output.SteinerLimitReached);
            Assert.True(report.Ok, report.ToString());
            if (s.IsExact) {
                Judge(Make((a, b) => new Vector3d(a, b, 0)), out var plain);
                Assert.Equal(plain.VX.Select(v => v * s.Scale), output.VX);
                Assert.Equal(plain.VY.Select(v => v * s.Scale), output.VY);
                Assert.Equal(plain.Triangles.SelectMany(t => t), output.Triangles.SelectMany(t => t));
            }
        }

        [Fact]
        public void Fuzz_NarrowWedges_KeepTagsAndPassTheOracle()
        {
            QualityMesh2.Refiner.CheckTagsForTests = true;
            try {
                for (int seed = 0; seed < 40; ++seed) {
                    var input = Wedge(seed);
                    var report = Judge(input, out var output);
                    Assert.True(output.Status == Mesh2Status.Success, "seed " + seed + ": " + output.FailureKind);
                    Assert.True(report.Ok, "seed " + seed + ": " + report);
                }
            } finally { QualityMesh2.Refiner.CheckTagsForTests = false; }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(20.0)]
        [InlineData(30.0)]
        public void SpikeAtASmallAngle_KeepsItsApex(double angle)
        {
            // corpus run (earcut's water): a spike of 3.4 degrees; refinement split its sides down to where they are within the
            // tolerance of each other, the chains merged there (O11), and the input vertex at the tip was left in no triangle
            var input = new Mesh2Input { X = new double[] { 3027, 3044, 3060, 3060, 3051 }, Y = new double[] { 4224, 4223, 4200, 4250, 4224 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
            input.MinAngleDeg = angle; input.SteinerLimit = 50000;
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            Assert.Contains(o.Triangles, t => t.Contains(o.InputToVertex[0]));
        }

        static Mesh2Input SpikeOf084Degrees(double angle)
        {
            // corpus run at 25 degrees (earcut's water-huge, reduced to its ring around one corner): a spike of 0.84 degrees at (4097, 3013).
            // The first piece of the slanted leg (2^-22) has its split points 1.75e-9 from the vertical leg, within the mesher's tolerance
            // (2^-41 of the magnitude 4183: 1.90e-9; the oracle's tube is twice that): O11 forbids them, and conforming Delaunay and the angle
            // bound cannot be reached across the piece (oracle: O11-unsplittable pieces)
            var input = new Mesh2Input { X = new double[] { 4097, 4097, 4098, 4183, 4050 }, Y = new double[] { 2989, 3013, 2945, 4096, 3675 } };
            input.Rings.Add(new RingInput(new[] { 0, 1, 2, 3, 4 }, false));
            input.MinAngleDeg = angle;
            return input;
        }

        [Theory]
        [InlineData(20.0)]
        [InlineData(25.0)]
        [InlineData(30.0)]
        public void SpikeOf084Degrees_PassesTheOracle(double angle)
        {
            var report = Judge(SpikeOf084Degrees(angle), out _);
            Assert.True(report.Ok, report.ToString());
        }

        [Fact]
        public void SpikeOf084Degrees_At25_ReachesAnO11UnsplittablePiece()
        {
            // the case still reaches what it is for: the mesher stops at a piece it may not split, and the oracle excuses it there (a
            // hidden vertex and a triangle whose circumcentre lies behind the piece; the local test of the piece itself is not reached here)
            var report = Judge(SpikeOf084Degrees(25), out _);
            Assert.True(report.Ok, report.ToString());
            Assert.True(report.O11GlobalExcused > 0 && report.O11AngleExcused > 0,
                "excused: " + report.O11LocalExcused + " local, " + report.O11GlobalExcused + " global, " + report.O11AngleExcused + " angle");
        }

        [Theory]
        [MemberData(nameof(Similarities))]
        public void SpikeOf084Degrees_Similarity_PassesTheOracle(string name)
        {
            var s = Similarity.All().First(x => x.Name == name);
            var input = SpikeOf084Degrees(25);
            var e = s.Rotation * Vector3d.AxisX;
            double norm = Math.Sqrt(e.x * e.x + e.y * e.y), cos = e.x / norm, sin = e.y / norm;
            for (int i = 0; i < input.Count; ++i) {
                double x = input.X[i], y = input.Y[i];
                input.X[i] = s.Scale * (cos * x - sin * y) + s.Translation.x;
                input.Y[i] = s.Scale * (sin * x + cos * y) + s.Translation.y;
            }
            var report = Judge(input, out var moved);
            Assert.True(report.Ok, name + ": " + report);
            if (s.IsExact) {
                Judge(SpikeOf084Degrees(25), out var plain);
                Assert.Equal(Canonical(plain), Canonical(moved));
            }
        }

        [Theory]
        [InlineData(20.0)]
        [InlineData(30.0)]
        public void PointsUlpsInsideAHullEdge_BecomeBoundaryVertices(double angle)
        {
            // corpus run (delaunator's issue43): points a few 1e-15 inside a hull edge were encroaching it for ever, refinement split
            // it towards them down to the last bit (59 vertices from 5); now they are vertices of the boundary (O11)
            var input = new Mesh2Input {
                X = new[] { -537.7739674441619, -495.533967444162, -453.29396744416204, -411.0539674441621, -164 },
                Y = new[] { -122.26130468750004, -183.39195703125006, -244.5226093750001, -305.6532617187501, -122 } };
            input.MinAngleDeg = angle;
            var report = Judge(input, out var o);
            Assert.True(report.Ok, report.ToString());
            Assert.True(o.VX.Length < 20, o.VX.Length + " vertices");
            Assert.False(o.SteinerLimitReached);
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
            // a limit of 0 on a mesh that needs no point: nothing was refused, so no flag (review of M3)
            var square = new Mesh2Input { X = new double[] { 0, 1, 1, 0 }, Y = new double[] { 0, 0, 1, 1 }, MinAngleDeg = 30, SteinerLimit = 0 };
            square.Rings.Add(new RingInput(new[] { 0, 1, 2, 3 }, false));
            Assert.True(Judge(square, out var good).Ok);
            Assert.False(good.SteinerLimitReached);
            Assert.Equal(4, good.VX.Length);
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
