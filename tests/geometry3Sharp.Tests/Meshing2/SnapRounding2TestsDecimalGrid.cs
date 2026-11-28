using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Tests for the decimal output grid (m4.grid, spec O12, F-65).
    /// Class name matches <c>FullyQualifiedName~SnapRounding2Tests</c> in <c>stryker-mesh-snaprounding.json</c>.
    /// </summary>
    public class SnapRounding2TestsDecimalGrid
    {
        static void AssertExactDecimalGrid(ConstrainedMesh2 mesh, int decimalPlaces)
        {
            string fmt = "F" + decimalPlaces.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < mesh.Vertices.Count; ++i) {
                var p = mesh.Vertices[i];
                // Alias slots of merged inputs keep original input coordinates by D17; only check surviving mesh vertices.
                if (i < mesh.InputCount && mesh.InputToVertex[i] != i) continue;
                Assert.False(double.IsNaN(p.x) || double.IsInfinity(p.x));
                Assert.False(double.IsNaN(p.y) || double.IsInfinity(p.y));
                double rx = Math.Round(p.x, decimalPlaces, MidpointRounding.ToEven);
                double ry = Math.Round(p.y, decimalPlaces, MidpointRounding.ToEven);
                Assert.Equal(BitConverter.DoubleToInt64Bits(rx), BitConverter.DoubleToInt64Bits(p.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(ry), BitConverter.DoubleToInt64Bits(p.y));
                double px = double.Parse(p.x.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                double py = double.Parse(p.y.ToString(fmt, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                Assert.Equal(BitConverter.DoubleToInt64Bits(px), BitConverter.DoubleToInt64Bits(p.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(py), BitConverter.DoubleToInt64Bits(p.y));
            }
            for (int t = 0; t < mesh.TriangleCount; ++t) {
                var tri = mesh.Triangles[t];
                var a = mesh.Vertices[tri.a];
                var b = mesh.Vertices[tri.b];
                var c = mesh.Vertices[tri.c];
                Assert.True(ExactPredicates.Orient2d(a, b, c) > 0, $"Triangle {t} is not strictly CCW");
                // After decimal rounding, orientation stays strictly positive (0 flipped triangles).
                var ra = new Vector2d(Math.Round(a.x, decimalPlaces), Math.Round(a.y, decimalPlaces));
                var rb = new Vector2d(Math.Round(b.x, decimalPlaces), Math.Round(b.y, decimalPlaces));
                var rc = new Vector2d(Math.Round(c.x, decimalPlaces), Math.Round(c.y, decimalPlaces));
                Assert.True(ExactPredicates.Orient2d(ra, rb, rc) > 0, $"Triangle {t} flipped after decimal rounding");
            }
        }

        static ConstrainedMesh2Input BuildF65Decimal(double ox = 0, double oy = 0)
        {
            var input = new ConstrainedMesh2Input();
            input.Points.AddRange(new[] {
                new Vector2d(ox, oy), new Vector2d(ox + 10, oy),
                new Vector2d(ox + 10, oy + 10), new Vector2d(ox, oy + 10)
            });
            input.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false));
            for (int i = 0; i < 8; ++i) {
                double y = oy + 2.0 + 0.7 * i;
                int p0 = input.Points.Count;
                input.Points.Add(new Vector2d(ox + 0.5, y));
                input.Points.Add(new Vector2d(ox + 9.5, y + 0.004 * (i + 1)));
                input.Points.Add(new Vector2d(ox + 0.5, y + 0.003));
                input.Points.Add(new Vector2d(ox + 9.5, y + 0.002));
                input.Segments.Add(new Mesh2Segment(p0, p0 + 1));
                input.Segments.Add(new Mesh2Segment(p0 + 2, p0 + 3));
            }
            return input;
        }

        [Fact]
        public void SnapGrid_DecimalCentres_MatchExactDecimalRounding_WithoutUlpDrift()
        {
            // Demonstrate the exact binary64 1-ULP difference between k * 0.01 and k / 100.0
            long k57 = 57;
            Assert.NotEqual(0.57, k57 * 0.01);
            var grid2 = new SnapGrid(0.01, decimalPlaces: 2);
            Assert.Equal(2, grid2.DecimalPlaces);
            Assert.Equal(100.0, grid2.DecimalScale);
            Assert.Equal(BitConverter.DoubleToInt64Bits(0.57), BitConverter.DoubleToInt64Bits(grid2.Centre(57)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.57), BitConverter.DoubleToInt64Bits(grid2.Centre(-57)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(grid2.Centre(0)));

            // Default (decimalPlaces = -1) preserves exact k * W for power-of-two invariance
            var binGrid = new SnapGrid(0.01);
            Assert.Equal(-1, binGrid.DecimalPlaces);
            Assert.Equal(0.0, binGrid.DecimalScale);
            Assert.Equal(BitConverter.DoubleToInt64Bits(k57 * 0.01), BitConverter.DoubleToInt64Bits(binGrid.Centre(57)));
            Assert.Equal(BitConverter.DoubleToInt64Bits(0.0), BitConverter.DoubleToInt64Bits(binGrid.Centre(0)));

            // Check all d in 0..15 across negative, zero, positive, and large UTM-like indices
            long[] ks = { -571204329L, -57L, -1L, 0L, 1L, 29L, 57L, 571204329L };
            for (int d = 0; d <= 15; ++d) {
                double scale = SnapGrid.Pow10(d);
                double step = 1.0 / scale;
                var g = new SnapGrid(step, d);
                Assert.Equal(d, g.DecimalPlaces);
                Assert.Equal(scale, g.DecimalScale);
                foreach (long k in ks) {
                    double c = g.Centre(k);
                    double expected = k == 0 ? 0.0 : k / scale;
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(c));
                    Assert.Equal(k, g.Pixel(c));
                    Assert.True(g.OnGrid(new Vector2d(c, c)));
                    if (d <= 6 && Math.Abs(k) <= 1000) {
                        Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(c, d, MidpointRounding.ToEven)), BitConverter.DoubleToInt64Bits(c));
                    }
                }
            }
        }

        [Fact]
        public void SnapRounding2_DecimalGrid_ChainPointsAndVertices_AreExactDecimal()
        {
            var input = new ConstrainedMesh2Input();
            input.Points.AddRange(new[] {
                new Vector2d(0.574, 0.289),
                new Vector2d(5.731, 0.571),
                new Vector2d(5.734, 4.172),
                new Vector2d(0.288, 4.169)
            });
            input.Segments.Add(new Mesh2Segment(0, 2));
            input.Segments.Add(new Mesh2Segment(1, 3));

            var snap = SnapRounding2.Apply(input, 0.01, decimalPlaces: 2);
            foreach (var p in snap.Result.Points) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(p.x, 2, MidpointRounding.ToEven)), BitConverter.DoubleToInt64Bits(p.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(p.y, 2, MidpointRounding.ToEven)), BitConverter.DoubleToInt64Bits(p.y));
            }
            var chain = snap.ChainPoints(0, 2);
            Assert.True(chain.Count >= 2);
            foreach (var p in chain) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(p.x, 2, MidpointRounding.ToEven)), BitConverter.DoubleToInt64Bits(p.x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(p.y, 2, MidpointRounding.ToEven)), BitConverter.DoubleToInt64Bits(p.y));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void F65_Decimal001Grid_ZeroFlippedTriangles_ValidOracle_AndNoLargeInteriorBadTriangles(bool conforming)
        {
            var input = BuildF65Decimal();
            var mesher = new QualityMesh2 { MinAngle = 30, OutputDecimalPlaces = 2, Conforming = conforming };
            var res = mesher.Compute(input);
            Assert.Equal(Triangulation2Status.Success, res.Status);
            Assert.NotNull(res.Mesh);
            Assert.True(res.Mesh.TriangleCount > 0);
            AssertExactDecimalGrid(res.Mesh, 2);

            // Verify against exact oracle first (checks all angles, grid compliance, unsplittable exemptions, and Delaunay)
            var m2In = ToOracleInput(input, snapTol: 0.01, minAngle: 30, conforming: conforming);
            var m2Out = ConstrainedDelaunayMesh2Candidate.Output(res);
            m2Out.BadTriangles = res.Mesh.BadTriangles.ToArray();
            m2Out.SteinerLimitReached = res.Mesh.SteinerLimitReached;
            var report = Oracle.Check(m2In, m2Out, new OracleExpect { Milestone = Milestone.M3, StatedArea = 100, StatedAreaTolerance = new Q(1, 16), MayMoveVertices = true });
            Assert.True(report.Ok, "Oracle failed on decimal F-65: " + report);

            // Check F-65 second expectation: no bad triangle (< 30 deg) away from segments has shortest edge > 20 grid cells (0.20).
            double Dist2ToSeg(Vector2d p, Vector2d u, Vector2d w)
            {
                double dx = w.x - u.x, dy = w.y - u.y, l2 = dx * dx + dy * dy;
                double t = l2 > 0 ? Math.Max(0, Math.Min(1, ((p.x - u.x) * dx + (p.y - u.y) * dy) / l2)) : 0;
                double qx = u.x + t * dx - p.x, qy = u.y + t * dy - p.y;
                return qx * qx + qy * qy;
            }
            foreach (int ti in res.Mesh.BadTriangles) {
                var tri = res.Mesh.Triangles[ti];
                var a = res.Mesh.Vertices[tri.a];
                var b = res.Mesh.Vertices[tri.b];
                var c = res.Mesh.Vertices[tri.c];
                double minSegDist2 = double.PositiveInfinity;
                foreach (var s in res.Mesh.Subsegments) {
                    var u = res.Mesh.Vertices[s.A];
                    var w = res.Mesh.Vertices[s.B];
                    minSegDist2 = Math.Min(minSegDist2, Dist2ToSeg(a, u, w));
                    minSegDist2 = Math.Min(minSegDist2, Dist2ToSeg(b, u, w));
                    minSegDist2 = Math.Min(minSegDist2, Dist2ToSeg(c, u, w));
                }
                if (minSegDist2 <= 0.05 * 0.05) continue; // within 5 grid cells of a segment
                double minEdge = Math.Sqrt(Math.Min(a.DistanceSquared(b), Math.Min(b.DistanceSquared(c), c.DistanceSquared(a))));
                Assert.True(minEdge <= 20.0 * 0.01 + 1e-12,
                    $"Interior bad triangle {ti} ({a}, {b}, {c}) away from segments (dist={Math.Sqrt(minSegDist2)}) has shortest edge {minEdge} > 20 grid cells");
            }
        }

        [Fact]
        public void UtmScale_AndMultipleDecimalPrecisions_CdtAndQuality_AreExactDecimal()
        {
            // UTM scale (370000, 5710000) with OutputDecimalPlaces = 2 (1 cm production grid)
            var utmInput = BuildF65Decimal(370000.0, 5710000.0);
            var cdtRes = new ConstrainedDelaunayMesh2 { OutputDecimalPlaces = 2 }.Compute(utmInput);
            Assert.Equal(Triangulation2Status.Success, cdtRes.Status);
            AssertExactDecimalGrid(cdtRes.Mesh, 2);

            var qualRes = new QualityMesh2 { MinAngle = 25, OutputDecimalPlaces = 2 }.Compute(utmInput);
            Assert.Equal(Triangulation2Status.Success, qualRes.Status);
            AssertExactDecimalGrid(qualRes.Mesh, 2);

            // Hull domain (point set without outer ring) on decimal grid
            var hullInput = new ConstrainedMesh2Input();
            hullInput.Points.AddRange(new[] {
                new Vector2d(0.113, 0.294), new Vector2d(10.289, 0.291),
                new Vector2d(10.294, 8.712), new Vector2d(0.112, 8.714),
                new Vector2d(3.291, 4.142), new Vector2d(6.734, 2.861)
            });
            var hullRes = new QualityMesh2 { MinAngle = 20, OutputDecimalPlaces = 2 }.Compute(hullInput);
            Assert.Equal(Triangulation2Status.Success, hullRes.Status);
            AssertExactDecimalGrid(hullRes.Mesh, 2);

            // Integer grid (d = 0), 1 decimal place (d = 1), and 3 decimal places (d = 3)
            foreach (int d in new[] { 0, 1, 3 }) {
                var sq = new ConstrainedMesh2Input();
                sq.Points.AddRange(new[] {
                    new Vector2d(0.1234, 0.2891), new Vector2d(20.2912, 0.2894),
                    new Vector2d(20.2918, 20.7311), new Vector2d(0.1231, 20.7319),
                    new Vector2d(5.291, 5.292), new Vector2d(14.713, 12.294)
                });
                sq.Rings.Add(new Mesh2Ring(new[] { 0, 1, 2, 3 }, hole: false));
                sq.Segments.Add(new Mesh2Segment(4, 5));
                var r = new QualityMesh2 { MinAngle = 20, OutputDecimalPlaces = d }.Compute(sq);
                Assert.Equal(Triangulation2Status.Success, r.Status);
                AssertExactDecimalGrid(r.Mesh, d);
            }
        }

        [Fact]
        public void Validation_Reuse_Determinism_AndSimilarityInvariance()
        {
            var input = BuildF65Decimal();

            // Invalid OutputDecimalPlaces
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstrainedDelaunayMesh2 { OutputDecimalPlaces = -1 }.Compute(input));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstrainedDelaunayMesh2 { OutputDecimalPlaces = 16 }.Compute(input));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityMesh2 { MinAngle = 20, OutputDecimalPlaces = -1 }.Compute(input));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QualityMesh2 { MinAngle = 20, OutputDecimalPlaces = 16 }.Compute(input));

            // Conflicting SnapTolerance and OutputDecimalPlaces
            Assert.Throws<ArgumentException>(() => new ConstrainedDelaunayMesh2 { SnapTolerance = 0.05, OutputDecimalPlaces = 2 }.Compute(input));
            Assert.Throws<ArgumentException>(() => new QualityMesh2 { MinAngle = 20, SnapTolerance = 0.05, OutputDecimalPlaces = 2 }.Compute(input));

            // Matching SnapTolerance = 0.01 and OutputDecimalPlaces = 2 equals SnapTolerance = 0, OutputDecimalPlaces = 2 bit-for-bit
            var mA = new QualityMesh2 { MinAngle = 25, OutputDecimalPlaces = 2 };
            var mB = new QualityMesh2 { MinAngle = 25, SnapTolerance = 0.01, OutputDecimalPlaces = 2 };
            var rA1 = mA.Compute(input);
            var rA2 = mA.Compute(input); // reuse on same instance
            var rB = mB.Compute(input);
            Assert.Equal(Triangulation2Status.Success, rA1.Status);
            AssertMeshesBitIdentical(rA1.Mesh, rA2.Mesh);
            AssertMeshesBitIdentical(rA1.Mesh, rB.Mesh);

            // Parallel determinism
            var parallel = new ConstrainedMesh2[8];
            Parallel.For(0, parallel.Length, idx => {
                var r = new QualityMesh2 { MinAngle = 25, OutputDecimalPlaces = 2 }.Compute(input);
                Assert.Equal(Triangulation2Status.Success, r.Status);
                parallel[idx] = r.Mesh;
            });
            for (int i = 0; i < parallel.Length; ++i) AssertMeshesBitIdentical(rA1.Mesh, parallel[i]);

            // Similarity.All2D() invariance: rotated / UTM translated copies stay on exact decimal grid with 0 flips
            foreach (var sim in new[] { Similarity.Identity, Similarity.Utm, Similarity.RotatedZ }) {
                var moved = new ConstrainedMesh2Input {
                    Points = input.Points.Select(p => sim.Apply2d(p)).ToList(),
                    Segments = new List<Mesh2Segment>(input.Segments),
                    Rings = new List<Mesh2Ring>(input.Rings)
                };
                var rm = new QualityMesh2 { MinAngle = 25, OutputDecimalPlaces = 2 }.Compute(moved);
                Assert.Equal(Triangulation2Status.Success, rm.Status);
                AssertExactDecimalGrid(rm.Mesh, 2);
            }
        }

        static void AssertMeshesBitIdentical(ConstrainedMesh2 a, ConstrainedMesh2 b)
        {
            Assert.Equal(a.Vertices.Count, b.Vertices.Count);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            for (int i = 0; i < a.Vertices.Count; ++i) {
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].x), BitConverter.DoubleToInt64Bits(b.Vertices[i].x));
                Assert.Equal(BitConverter.DoubleToInt64Bits(a.Vertices[i].y), BitConverter.DoubleToInt64Bits(b.Vertices[i].y));
            }
            for (int t = 0; t < a.TriangleCount; ++t) Assert.Equal(a.Triangles[t], b.Triangles[t]);
        }

        static Mesh2Input ToOracleInput(ConstrainedMesh2Input src, double snapTol, double minAngle, bool conforming)
        {
            var m = new Mesh2Input {
                X = src.Points.Select(p => p.x).ToArray(),
                Y = src.Points.Select(p => p.y).ToArray(),
                SnapTolerance = snapTol,
                MinAngleDeg = minAngle,
                Conforming = conforming,
                ConstrainedQuality = !conforming
            };
            foreach (var s in src.Segments) m.Segments.Add(new SegmentInput(s.A, s.B, s.Marker, s.Locked));
            foreach (var r in src.Rings) m.Rings.Add(new RingInput(r.Vertices, r.Hole, r.Marker));
            return m;
        }
    }
}
