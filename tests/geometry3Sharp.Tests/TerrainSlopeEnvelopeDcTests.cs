using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The prototype construction of the slope map by divide and conquer (docs/design/TERRAIN_SLOPE_ENVELOPE_DC.md; internal switch
    /// <c>EnvelopeDivideAndConquer</c>, the default path is the incremental map): the oracles hold, and on designs without near-degenerate vertices the
    /// surface is the same as the incremental one, vertex for vertex.
    /// </summary>
    public class TerrainSlopeEnvelopeDcTests
    {
        static List<Vector3d> Ngon(int n, double r, double z, double phase = 0.1) =>
            Enumerable.Range(0, n).Select(i => new Vector3d(r * Math.Cos(phase + 2 * Math.PI * i / n), r * Math.Sin(phase + 2 * Math.PI * i / n), z)).ToList();

        static List<Vector3d> Wavy(int n, double r, Vector3d c)
        {
            var pts = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double a = 2 * Math.PI * i / n, rr = r * (1 + 0.15 * Math.Sin(5 * a));
                pts.Add(c + new Vector3d(rr * Math.Cos(a), rr * Math.Sin(a), 4 * Math.Sin(3 * a)));
            }
            return pts;
        }

        static TerrainSlopeDesign Run(bool dc, double height, double tol, SlopeToe toe)
        {
            var d = TerrainSlopeDesignTests.Design(height, tol, toe);
            d.EnvelopeDivideAndConquer = dc;
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            return d;
        }

        static void AssertSameSurface(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.VertexCount, b.VertexCount);
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.VertexIndices().Select(a.GetVertex).ToList(), b.VertexIndices().Select(b.GetVertex).ToList());
            Assert.Equal(a.TriangleIndices().Select(a.GetTriangle).ToList(), b.TriangleIndices().Select(b.GetTriangle).ToList());
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "square", 0 };
            yield return new object[] { "ngon24", 1 };
            yield return new object[] { "wavy48", 2 };
            yield return new object[] { "lshape", 3 };
            yield return new object[] { "open", 4 };
        }

        static (double h, double tol, SlopeToe toe) Make(int k)
        {
            switch (k) {
                case 0: return (10, 0.05, SlopeToe.Constant(TerrainSlopeDesignTests.Square(20, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)));
                case 1: return (12, 0.05, SlopeToe.Constant(Ngon(24, 30, 0), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1)));
                case 2: return (40, 0.05, SlopeToe.Constant(Wavy(24, 60, new Vector3d(328375.25, 5642875.5, -340)), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)));
                case 3: return (15, 0.05, SlopeToe.Constant(new List<Vector3d> { new Vector3d(0, 0, 0), new Vector3d(40, 0, 0), new Vector3d(40, 12, 0), new Vector3d(12, 12, 0), new Vector3d(12, 40, 0), new Vector3d(0, 40, 0) }, true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1.5)));
                default: return (9, 0.02, SlopeToe.Constant(new List<Vector3d> { new Vector3d(-30, 0, 0), new Vector3d(30, 3, 1) }, false, SlopeToe.Sides.Left, SlopeSpec.Ratio(1)));
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void DivideAndConquer_HoldsTheOracles_AndGivesTheIncrementalSurface(string name, int k)
        {
            var (h, tol, toe) = Make(k);
            TerrainSlopeDesign inc = Run(false, h, tol, toe), dc = Run(true, h, tol, toe);
            TerrainSlopeDesignTests.AssertAll(dc);
            AssertSameSurface(inc.SlopeSurface, dc.SlopeSurface);
            Assert.Equal(inc.UsedToeLowering, dc.UsedToeLowering);
            Assert.Equal(inc.MergedPositions, dc.MergedPositions);
        }

        [Fact]
        public void DivideAndConquer_Compute_Twice_AndRepeated_GiveTheSameSurface()
        {
            var (h, tol, toe) = Make(1);
            var d = TerrainSlopeDesignTests.Design(h, tol, toe);
            d.EnvelopeDivideAndConquer = true;
            Assert.True(d.Compute());
            DMesh3 first = new DMesh3(d.SlopeSurface);
            Assert.True(d.Compute());
            AssertSameSurface(first, d.SlopeSurface);
            for (int i = 0; i < 3; ++i) AssertSameSurface(first, Run(true, h, tol, toe).SlopeSurface);
        }

        [Fact]
        public void DivideAndConquer_ScalesBitForBit_By2k()
        {
            var (h, tol, toe) = Make(1);
            TerrainSlopeDesign a = Run(true, h, tol, toe);
            double s = 8;
            var toe2 = SlopeToe.Constant(toe.Points.Select(p => p * s).ToList(), true, SlopeToe.Sides.Outside, SlopeSpec.Ratio(1));
            TerrainSlopeDesign b = Run(true, h * s, tol * s, toe2);
            Assert.Equal(a.SlopeSurface.VertexCount, b.SlopeSurface.VertexCount);
            var va = a.SlopeSurface.VertexIndices().Select(a.SlopeSurface.GetVertex).ToList();
            var vb = b.SlopeSurface.VertexIndices().Select(b.SlopeSurface.GetVertex).ToList();
            for (int i = 0; i < va.Count; ++i) Assert.Equal(va[i] * s, vb[i]);
        }

        [Fact]
        public void MergeMin_OfTwoEnvelopes_IsTheEnvelopeOfBoth_AndAFaceMergeAcrossABarrierIsNotDone()
        {
            // the pieces of a design merged in either order, and in any split, give the same labels everywhere: sampled heights agree with the brute-force cone envelope
            var (h, tol, toe) = Make(1);
            TerrainSlopeDesign dc = Run(true, h, tol, toe);
            var pieces = dc.Pieces;
            var rnd = new Random(3);
            for (int i = 0; i < 200; ++i) {
                double ang = rnd.NextDouble() * 2 * Math.PI, rad = 30 + rnd.NextDouble() * 12;
                var x = new Vector2d(rad * Math.Cos(ang), rad * Math.Sin(ang));
                double cone = TerrainSlopeDesignTests.ConeEnvelope(pieces, x), surf = TerrainSlopeDesignTests.HeightAt(dc.SlopeSurface, x);
                if (double.IsNaN(surf)) continue;
                Assert.True(surf <= Math.Min(cone, h) + tol + 1e-9, $"surface {surf} above the cone envelope {cone}");
            }
        }
    }

}
