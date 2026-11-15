// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The slope design gives the same bits on every platform (issue 802): its angles and fan directions come from the
    /// correctly rounded CoreMath, not from the platform's libm, whose sin / cos / atan2 differ in the last bit between
    /// glibc, the Windows CRT of .NET and the one of .NET Framework. The fingerprints below are pinned: the tests run
    /// on linux net10 and on windows net48 in CI, and both must give them. (This file uses only the public API, so that
    /// it compiles against the library before the fix, where the fingerprints differ.)
    /// </summary>
    public class TerrainSlopePlatformLibmTests
    {
        // a tilted, slightly warped terrain (+ - * / only) with an irregular 7-gon pit at z 0
        static TerrainSlopeDesign Pit(SlopeSpec slope, double tolerance, bool withTerrain)
        {
            var terrain = new DMesh3();
            int nx = 23, ny = 25;
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i) {
                    double x = -60 + 120.0 * i / nx, y = -60 + 120.0 * j / ny;
                    terrain.AppendVertex(new Vector3d(x, y, 0.15 * (0.8 * x + 0.6 * y) + 0.3 + 0.0004 * x * y - 0.0002 * y * y));
                }
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                    if ((i + j) % 2 == 0) { terrain.AppendTriangle(a, b, c); terrain.AppendTriangle(a, c, d); }
                    else { terrain.AppendTriangle(a, b, d); terrain.AppendTriangle(b, c, d); }
                }
            var toe = new List<Vector3d>() {
                new Vector3d(11, 1, 0), new Vector3d(7, 8.5, 0), new Vector3d(-2, 10.75, 0), new Vector3d(-10, 5, 0),
                new Vector3d(-9.5, -5.25, 0), new Vector3d(-2.5, -11, 0), new Vector3d(6.75, -8, 0) };
            var design = new TerrainSlopeDesign() {
                Toes = { SlopeToe.Constant(toe, true, SlopeToe.Sides.Outside, slope) }, Tolerance = tolerance,
                Direction = TerrainSlopeDesign.Directions.Cut
            };
            if (withTerrain) { design.Terrain = terrain; design.BuildComposite = true; }
            else design.HeightLimit = 8;
            return design;
        }

        // FNV-1a over the bits of every vertex and the triangles, in id order
        static ulong Fingerprint(params DMesh3[] meshes)
        {
            ulong h = 14695981039346656037UL;
            Action<long> add = (v) => { for (int k = 0; k < 8; ++k) { h ^= (ulong)(v >> (8 * k)) & 0xFF; h *= 1099511628211UL; } };
            foreach (DMesh3 m in meshes) {
                add(m.VertexCount); add(m.TriangleCount);
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    add(v); add(BitConverter.DoubleToInt64Bits(p.x)); add(BitConverter.DoubleToInt64Bits(p.y)); add(BitConverter.DoubleToInt64Bits(p.z));
                }
                foreach (int t in m.TriangleIndices()) { Index3i tri = m.GetTriangle(t); add(t); add(tri.a); add(tri.b); add(tri.c); }
            }
            return h;
        }

        [Theory]
        [InlineData(0.05, "e27301b6e0374f21 angle 3fc1df46a2529d39")]
        [InlineData(0.01, "3e4254b3f11077b2 angle 3fb03f577c4b1a91")]
        [InlineData(0.004, "286f273ba6588dcd angle 3fa47d8a0d42ff43")]
        public void Pit_SlopeSurface_PinnedBits(double tolerance, string expected)
        {
            TerrainSlopeDesign d = Pit(SlopeSpec.Ratio(1.5), tolerance, false);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            Assert.True(d.SlopeSurface.TriangleCount > 20 && d.SlopeSurface.IsCompact);
            foreach (Vector3d v in d.SlopeSurface.Vertices()) Assert.True(v.IsFinite);
            Assert.Equal(expected, Fingerprint(d.SlopeSurface).ToString("x16") + " angle " + BitConverter.DoubleToInt64Bits(d.FacetAngle).ToString("x16"));
        }

        // (0.02: before the fix linux net10 gave c3d2174ee578a5d7 here, glibc's functions differing from the correctly
        // rounded ones on this input; windows net10 differed from windows net48 on the 0.05 case as well, in 20 of 15302
        // values, determinism run 43379590599)
        [Theory]
        [InlineData(0.05, "d3c0d67f20c7c464")]
        [InlineData(0.02, "4f8e3773b73bb966")]
        public void PitInTerrain_SlopeSurfaceAndComposite_PinnedBits(double tolerance, string expected)
        {
            TerrainSlopeDesign d = Pit(SlopeSpec.Ratio(1.5), tolerance, true);
            Assert.True(d.Compute(), d.Status + " " + d.Message);
            TerrainSlopeCatchLineTests.AssertCatchLines(d);
            TerrainSlopeCatchLineTests.AssertComposite(d);
            Assert.Equal(expected, Fingerprint(d.SlopeSurface, d.Composite).ToString("x16"));
        }
    }
}
