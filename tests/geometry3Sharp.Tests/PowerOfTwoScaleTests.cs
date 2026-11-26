using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1612: operations that read a binary exponent with Floor / Ceiling(Math.Log(x, 2)) were off by one at exact powers of two (a platform libm
    /// value), so a copy of a problem scaled by 2^k was a different problem. These tests use only the public API (no internal helper), so they
    /// compile and fail against the base branch.
    /// </summary>
    public class PowerOfTwoScaleTests
    {
        // ---- FoldoverFreeParameterization: PowerScale of the UV extent (Compute) and of the surface extent (PrepareMesh) ----

        static ParameterizationResult RunFoldover(int k, bool scaleMesh = false)
        {
            int n = 8; double s = Math.Pow(2, k);
            var mesh = new DMesh3();
            for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++)
                mesh.AppendVertex(new Vector3d(i / (double)n, j / (double)n, 0) * (scaleMesh ? s : 1));
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i, b = a + 1, c = a + n + 2, d = a + n + 1; mesh.AppendTriangle(a, b, c); mesh.AppendTriangle(a, c, d); }
            var uv = new List<Vector2d>();
            for (int v = 0; v < mesh.VertexCount; v++) { var p = mesh.GetVertex(v) * (scaleMesh ? 1 / s : 1); uv.Add(new Vector2d(p.x, p.y)); }
            int mid = 4 * (n + 1) + 4; var q = uv[mid];
            uv[mid] = new Vector2d(q.x + 0.3 / n, q.y + 0.45 / n);
            uv[mid + 1] = new Vector2d(uv[mid - 1].x - 0.2 / n, uv[mid + 1].y);
            for (int v = 0; v < uv.Count; v++) uv[v] = uv[v] * s;
            return new FoldoverFreeParameterization(mesh, uv).Compute();
        }

        static void AssertScaled(ParameterizationResult reference, ParameterizationResult r, double factor, string what)
        {
            Assert.True(reference.Succeeded && r.Succeeded, what + ": " + r.Error);
            Assert.Equal(reference.Iterations, r.Iterations);
            Assert.Equal(reference.Energy, r.Energy);
            Assert.Equal(reference.UV.Length, r.UV.Length);
            for (int i = 0; i < r.UV.Length; ++i) {
                Assert.Equal(reference.UV[i].x * factor, r.UV[i].x);
                Assert.Equal(reference.UV[i].y * factor, r.UV[i].y);
            }
        }

        [Theory]
        [InlineData(-29)]
        [InlineData(-31)]
        [InlineData(-39)]
        [InlineData(-59)]
        [InlineData(0)]
        [InlineData(29)]
        [InlineData(-3)]
        public void FoldoverFree_UvScaledByAPowerOfTwo_GivesTheScaledResultBitForBit(int k)
        {
            AssertScaled(RunFoldover(-28), RunFoldover(k), Math.Pow(2, k + 28), "uv 2^" + k);
        }

        [Fact]
        public void FoldoverFree_UnderSimilarities_UvAndMeshScaledByThePowerOfTwo_GiveTheScaledResultBitForBit()
        {
            // every exact similarity of the harness: the UVs and, for the surface path, the mesh positions are scaled by 2^k
            ParameterizationResult reference = RunFoldover(0);
            ParameterizationResult referenceMesh = RunFoldover(0, scaleMesh: true);
            foreach (Similarity s in Similarity.All()) {
                if (!s.IsExact) continue;
                int k = (int)Math.Round(Math.Log(s.Scale, 2));
                Assert.Equal(s.Scale, Math.Pow(2, k));
                AssertScaled(reference, RunFoldover(k), s.Scale, s.Name + " uv");
                AssertScaled(referenceMesh, RunFoldover(k, scaleMesh: true), s.Scale, s.Name + " uv and mesh");
            }
        }

        [Theory]
        [InlineData(-29)]
        [InlineData(-31)]
        [InlineData(-39)]
        [InlineData(29)]
        public void FoldoverFree_MeshAndUvScaledByAPowerOfTwo_GivesTheScaledResultBitForBit(int k)
        {
            AssertScaled(RunFoldover(-28, scaleMesh: true), RunFoldover(k, scaleMesh: true), Math.Pow(2, k + 28), "mesh and uv 2^" + k);
        }

        // ---- TpmsLattice Enclosure: the dyadic level pad s = 2^ceil(log2 maxCoord) ----

        static TpmsLattice Enclosure(double scale)
        {
            // the domain sits at the far end of x: with h = CellSize / 4 the largest grid coordinate is h (ceil(Max / h) + 5) = 2^20 CellSize exactly,
            // the limit of the proven phase bound (coordinate / CellSize <= 2^20). With scale 2^k that coordinate is the power of two 2^(20 + k).
            double xmax = (Math.Pow(2, 22) - 5) / 4 * scale;
            return new TpmsLattice {
                Type = TpmsType.SchwarzP, Style = TpmsStyle.Sheet, VolumeFraction = 0.3, Enclosure = true, CellSize = scale, Divisions = 4,
                Box = new AxisAlignedBox3d(new Vector3d(xmax - 2 * scale, -scale, -scale), new Vector3d(xmax, scale, scale))
            };
        }

        [Fact]
        public void TpmsEnclosure_PowerOfTwoScales_AreAllAcceptedAndGiveTheScaledMesh()
        {
            // S1: the same problem scaled by 2^k was refused with "coordinate / CellSize > 2^20" at the largest coordinates 2^29 and 2^31
            // (and accepted at 2^28, 2^30, 2^32), because Ceiling(Log(2^k, 2)) is k + 1 there. Scan every k that crosses those.
            var reference = Enclosure(1);
            reference.Compute();
            Assert.True(reference.Status == TpmsStatus.Ok, reference.Status + ": " + reference.Message);
            for (int k = -40; k <= 40; ++k) {
                var t = Enclosure(Math.Pow(2, k));
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, "2^" + k + ": " + t.Status + ": " + t.Message);
                Assert.Equal(reference.Mesh.TriangleCount, t.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, t.RealizedVolumeFraction);
                Assert.Equal(reference.IsoValueUsed * Math.Pow(2, k), t.IsoValueUsed);
            }
        }

        [Fact]
        public void TpmsEnclosure_UnderSimilarities()
        {
            TpmsLattice reference = null;
            foreach (Similarity s in Similarity.All()) {
                if (!s.IsExact) continue;   // Enclosure needs a dyadic grid: only the 2^k scales (and the identity) are valid inputs, by design
                var t = new TpmsLattice {
                    Type = TpmsType.SchwarzP, Style = TpmsStyle.Sheet, VolumeFraction = 0.3, Enclosure = true, CellSize = s.Scale, Divisions = 4,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                t.Compute();
                Assert.True(t.Status == TpmsStatus.Ok, s.Name + ": " + t.Status + ": " + t.Message);
                if (reference == null) { reference = t; continue; }
                Assert.Equal(reference.Mesh.TriangleCount, t.Mesh.TriangleCount);
                Assert.Equal(reference.RealizedVolumeFraction, t.RealizedVolumeFraction);
                Assert.Equal("", Invariance.Difference(reference.Mesh, Invariance.MappedBack(t.Mesh, s)));
            }
        }

        // ---- WindingNumberOctree.RequiredDepth: ceil(log2 n) of n = maxDim / cell + 2 padding ----

        [Fact]
        public void RequiredDepth_ForAGridOf2PowKCells_IsK()
        {
            // bounds (0,0,0)-(2^k - 4, 1, 1), cell 1, padding 2: n = 2^k exactly; Ceiling(Log(2^29, 2)) is 30 on glibc
            for (int k = 3; k <= 29; ++k) {
                var bounds = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(Math.Pow(2, k) - 4, 1, 1));
                Assert.Equal(k, WindingNumberOctree.RequiredDepth(bounds, 1.0));
                // one cell more than 2^k: k + 1
                var bigger = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(Math.Pow(2, k) - 3, 1, 1));
                Assert.Equal(k + 1, WindingNumberOctree.RequiredDepth(bigger, 1.0));
            }
        }
    }
}
