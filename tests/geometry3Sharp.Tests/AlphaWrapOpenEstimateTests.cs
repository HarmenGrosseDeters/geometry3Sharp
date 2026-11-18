using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The size estimate of the alpha wrap counts both sides of an open input (issue #1113): MeshAlphaWrap.EstimatedTriangles against the
    /// raw wrap's triangle count on procedural open, closed and mixed inputs, so that MaxEstimatedTriangles means the same on both.
    /// </summary>
    public class AlphaWrapOpenEstimateTests
    {
        readonly ITestOutputHelper output;
        public AlphaWrapOpenEstimateTests(ITestOutputHelper output) { this.output = output; }

        // a triangle soup (three own vertices per triangle) of the triangles of the meshes, those whose centroid direction is within
        // cos 'capCos' of an axis (a cap of the sphere) dropped if capCos > 0
        static DMesh3 Soup(IEnumerable<DMesh3> meshes, double capCos = 0)
        {
            var soup = new DMesh3();
            foreach (DMesh3 m in meshes)
                foreach (int tid in m.TriangleIndices()) {
                    Index3i t = m.GetTriangle(tid);
                    Vector3d a = m.GetVertex(t.a), b = m.GetVertex(t.b), c = m.GetVertex(t.c);
                    if (capCos > 0) {
                        Vector3d d = (a + b + c).Normalized;
                        if (Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))) > capCos) continue;
                    }
                    soup.AppendTriangle(soup.AppendVertex(a), soup.AppendVertex(b), soup.AppendVertex(c));
                }
            return soup;
        }

        static DMesh3 Sphere(Vector3d center, int edgeVertices)
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = edgeVertices };
            g.Generate();
            DMesh3 m = g.MakeDMesh();
            foreach (int v in m.VertexIndices().ToList()) m.SetVertex(v, m.GetVertex(v) + center);
            return m;
        }

        // a flat n x n grid sheet of the given side in the plane z = z0, welded
        static DMesh3 Sheet(Vector3d origin, double side, int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j) for (int i = 0; i <= n; ++i) m.AppendVertex(origin + new Vector3d(i * side / n, j * side / n, 0));
            for (int j = 0; j < n; ++j) for (int i = 0; i < n; ++i) {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                m.AppendTriangle(a, b, d); m.AppendTriangle(a, d, c);
            }
            return m;
        }

        // the reproducer of #1113 (cap rule as in the issue's diagnosis): two overlapping unwelded spheres with the axis caps removed
        static DMesh3 TwoOpenSpheres(int edgeVertices = 8) => Soup(new[] { Sphere(Vector3d.Zero, edgeVertices), Sphere(new Vector3d(1.2, 0, 0.3), edgeVertices) }, 0.8776);

        // a budget that never coarsens (its only effect: the estimate counts the open components twice, the wrap is unchanged)
        const int NeverCoarsens = int.MaxValue;

        static MeshAlphaWrap Wrap(DMesh3 m, double relativeAlpha, int budget = NeverCoarsens)
        {
            var w = new MeshAlphaWrap(m) { RelativeAlpha = relativeAlpha, RelativeOffset = 10 * relativeAlpha, MaxEstimatedTriangles = budget };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        static DMesh3 Build(string name)
        {
            switch (name) {
                case "two open spheres": return TwoOpenSpheres();
                case "two closed spheres": return Soup(new[] { Sphere(Vector3d.Zero, 8), Sphere(new Vector3d(1.2, 0, 0.3), 8) });
                case "closed soup sphere": return Soup(new[] { Sphere(Vector3d.Zero, 8) });
                case "open sheet": return Sheet(Vector3d.Zero, 2, 12);
                case "mixed": return Mixed();
                case "welded sphere": return Sphere(Vector3d.Zero, 8);
                case "small hole": { DMesh3 s = Sphere(Vector3d.Zero, 16); s.RemoveTriangle(0); return s; }
                case "big hole": { DMesh3 s = Sphere(Vector3d.Zero, 8); foreach (int t in s.TriangleIndices().ToList()) { Index3i tr = s.GetTriangle(t); if ((s.GetVertex(tr.a) + s.GetVertex(tr.b) + s.GetVertex(tr.c)).Normalized.z > 0.9) s.RemoveTriangle(t); } return s; }
            }
            throw new ArgumentException(name);
        }

        static DMesh3 Mixed() => Soup(new[] { Sphere(Vector3d.Zero, 8), Sheet(new Vector3d(3, 0, 0), 4, 24), Sphere(new Vector3d(0, 4, 0), 8) });

        // (the wraps here: relative alpha 40, 0.2 to 1 s each)
        const double RelAlpha = 40;

        static double OneSided(DMesh3 m, MeshAlphaWrap w) => MeshAlphaWrap.EstimateDensity * Invariance.Area(m) / (w.UsedAlpha * w.UsedAlpha);

        // open input (the reproducer of #1113, a sheet, a sphere with a big hole, a mixed input): the estimate is within 0.75 to 1.3 of the
        // raw wrap (measured 0.94, 0.89, 1.03 and 1.02 of it for the reproducer, the sheet, the big hole and the mixed input; the one-sided
        // estimate was 0.5 of it on the first three: below 0.75)
        [Theory]
        [InlineData("two open spheres")]
        [InlineData("open sheet")]
        [InlineData("big hole")]
        [InlineData("mixed")]
        public void OpenInput_EstimateWithinFactorOfTheRawWrap(string name)
        {
            DMesh3 m = Build(name);
            MeshAlphaWrap w = Wrap(m, RelAlpha);
            double ratio = w.Result.TriangleCount / w.EstimatedTriangles;
            output.WriteLine($"{name}: estimate {w.EstimatedTriangles:F0} (one-sided {OneSided(m, w):F0}) actual {w.Result.TriangleCount} ratio {ratio:F2}");
            Assert.InRange(ratio, 0.75, 1.3);
            Assert.InRange(w.EstimatedTriangles / OneSided(m, w), 1.2, 2.0 + 1e-9);   // (counts more than one side)
        }

        // closed input and a hole below the opening width: the estimate is the one-sided one, bit for bit (the wrap is on the outside only)
        [Theory]
        [InlineData("two closed spheres")]
        [InlineData("closed soup sphere")]
        [InlineData("welded sphere")]
        [InlineData("small hole")]
        public void ClosedInput_EstimateIsTheOneSidedOne(string name)
        {
            DMesh3 m = Build(name);
            MeshAlphaWrap w = Wrap(m, RelAlpha);
            Assert.Equal(OneSided(m, w), w.EstimatedTriangles, 12);
            Assert.InRange(w.Result.TriangleCount / w.EstimatedTriangles, 0.15, 0.6);   // (about half: the wrap is on the outside only)
        }

        // the opening width: a hole with a chain diagonal of 3.0 alpha (the wrap is one-sided, measured 0.46 of the one-sided estimate) stays
        // one-sided in the estimate, a hole of 5.0 alpha (both sides, measured 1.87) counts twice (the scan of OpeningFactor, n = 24:
        // cut 0.999: 1.0 alpha, 0.46; 0.998 to 0.995: 3.0, 0.46; 0.993 to 0.98: 5.0 to 7.1, 1.87 to 1.93)
        [Fact]
        public void Opening_WidthDecidesTheSide()
        {
            foreach ((double cosCut, double chain, double factor, double ratioOfActual) in new[] { (0.995, 3.0, 1.0, 0.46), (0.993, 5.0, 2.0, 1.87) }) {
                DMesh3 m = Sphere(Vector3d.Zero, 24);
                foreach (int t in m.TriangleIndices().ToList()) {
                    Index3i tr = m.GetTriangle(t);
                    if ((m.GetVertex(tr.a) + m.GetVertex(tr.b) + m.GetVertex(tr.c)).Normalized.z > cosCut) m.RemoveTriangle(t);
                }
                MeshAlphaWrap w = Wrap(m, RelAlpha);
                Assert.InRange(AlphaWrapEstimate.LargestOpening(Corners(m), w.UsedAlpha) / w.UsedAlpha, chain - 0.5, chain + 0.5);
                Assert.Equal(factor, w.EstimatedTriangles / OneSided(m, w), 9);
                Assert.InRange(w.Result.TriangleCount / OneSided(m, w), ratioOfActual - 0.1, ratioOfActual + 0.1);
            }
        }

        static List<Vector3d> Corners(DMesh3 m)
        {
            var l = new List<Vector3d>();
            foreach (int t in m.TriangleIndices()) { Index3i tr = m.GetTriangle(t); l.Add(m.GetVertex(tr.a)); l.Add(m.GetVertex(tr.b)); l.Add(m.GetVertex(tr.c)); }
            return l;
        }

        // the guards of the estimate: no triangle, and an alpha that is not positive (zero, negative, NaN), are no open area and no opening
        // (the changed-line coverage gate of #1123 found them unhit)
        [Fact]
        public void Guards_NoTrianglesOrNoAlpha_AreNoOpenArea()
        {
            List<Vector3d> sheet = Corners(Build("open sheet"));
            Assert.True(AlphaWrapEstimate.OpenArea(sheet, 0.05) > 0);   // (the same input with a valid alpha is open: the guards, not the input, return 0)
            Assert.True(AlphaWrapEstimate.LargestOpening(sheet, 0.05) > 0);
            foreach (double alpha in new[] { 0.0, -0.05, double.NaN }) {
                Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(sheet, alpha));
                Assert.Equal(0.0, AlphaWrapEstimate.LargestOpening(sheet, alpha));
            }
            foreach (int n in new[] { 0, 1, 2 }) {
                List<Vector3d> few = sheet.GetRange(0, n);
                Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(few, 0.05));
                Assert.Equal(0.0, AlphaWrapEstimate.LargestOpening(few, 0.05));
            }
        }

        // the weld is by grid cells of alpha / 8: an unwelded soup of a closed sphere is closed (no open area), like the welded mesh; a crack
        // wider than the opening makes it open (every triangle counts); -0 equals +0
        [Fact]
        public void Weld_ByExactCoordinates()
        {
            DMesh3 welded = Sphere(Vector3d.Zero, 8);
            DMesh3 soup = Soup(new[] { welded });
            double alpha = 0.05, area = Invariance.Area(soup);
            Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(Corners(soup), alpha));
            Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(Corners(welded), alpha));
            // (a crack along a seam: one side of a cut moved apart by more than the opening width)
            // (a soup with corners off by rounding, 1e-12: still closed)
            DMesh3 noisy = Soup(new[] { welded });
            var rnd = new Random(3);
            foreach (int v in noisy.VertexIndices().ToList())
                noisy.SetVertex(v, noisy.GetVertex(v) + 1e-12 * new Vector3d(rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5, rnd.NextDouble() - 0.5));
            Assert.Equal(0.0, AlphaWrapEstimate.OpenArea(Corners(noisy), alpha));
            DMesh3 cracked = Soup(new[] { welded });
            foreach (int t in cracked.TriangleIndices().ToList()) {
                Index3i tr = cracked.GetTriangle(t);   // (own vertices per triangle: moving a triangle cuts it from its neighbours)
                if (cracked.GetTriCentroid(t).y > 0.3)
                    for (int k = 0; k < 3; ++k) cracked.SetVertex(tr[k], cracked.GetVertex(tr[k]) + new Vector3d(0, 4 * alpha, 0));
            }
            double open = AlphaWrapEstimate.OpenArea(Corners(cracked), alpha);
            Assert.True(open > 0.5 * area, $"{open} of {area}");
        }

        // the budget means the same on open and closed input: the raw wrap is about the budget on an open sheet and a budget of
        // N gives about N raw triangles, where the one-sided estimate gave about 2N; and the coarsened alpha is the one whose estimate
        // is the budget
        [Fact]
        public void Budget_MeansTheSameOnOpenInput()
        {
            DMesh3 sheet = Build("open sheet");
            double area = Invariance.Area(sheet);
            foreach (int budget in new[] { 1500, 3000 }) {
                var w = new MeshAlphaWrap(sheet) { RelativeAlpha = 100, RelativeOffset = 1000, MaxEstimatedTriangles = budget };
                Assert.True(w.Compute(), w.Error);
                Assert.Equal(Math.Sqrt(MeshAlphaWrap.EstimateDensity * 2 * area / budget), w.UsedAlpha, 12);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(w.Result));
                Assert.InRange(w.Result.TriangleCount / (double)budget, 0.75, 1.3);
            }
        }

        // the default path (no budget) is bit for bit the wrap of before the estimate counted both sides, on open and on closed input
        [Fact]
        public void Default_BitIdentical_OpenAndClosed()
        {
            var open = new MeshAlphaWrap(TwoOpenSpheres(5)) { RelativeAlpha = 30, RelativeOffset = 300 };   // (no budget: the open area is not computed)
            Assert.True(open.Compute(), open.Error);
            Assert.Equal(1.0, open.AlphaCoarsening);
            output.WriteLine($"open {open.Result.TriangleCount} {AlphaWrapManifoldStepTests.Hash(open.Result)}");
            var closed = new MeshAlphaWrap(Soup(new[] { Sphere(Vector3d.Zero, 5), Sphere(new Vector3d(1.2, 0, 0.3), 5) })) { RelativeAlpha = 30, RelativeOffset = 300 };
            Assert.True(closed.Compute(), closed.Error);
            output.WriteLine($"closed {closed.Result.TriangleCount} {AlphaWrapManifoldStepTests.Hash(closed.Result)}");
            Assert.Equal(5616, open.Result.TriangleCount);
            Assert.Equal("44823F74852D53F3", AlphaWrapManifoldStepTests.Hash(open.Result));
            Assert.Equal(1416, closed.Result.TriangleCount);
            Assert.Equal("7CC5E7C3C3ACC639", AlphaWrapManifoldStepTests.Hash(closed.Result));
        }

        // without a budget the estimate is the one-sided one (the open area is not computed) on open input
        // too, with a budget that does not bind it counts the open components twice, and the wrap is bit for bit the same in both
        [Theory]
        [InlineData("two open spheres")]
        [InlineData("open sheet")]
        [InlineData("big hole")]
        [InlineData("mixed")]
        public void NoBudget_EstimateIsOneSided_WrapUnchanged(string name)
        {
            DMesh3 m = Build(name);
            MeshAlphaWrap off = Wrap(m, RelAlpha, 0);
            MeshAlphaWrap on = Wrap(m, RelAlpha);
            Assert.Equal(OneSided(m, off), off.EstimatedTriangles, 12);
            Assert.True(on.EstimatedTriangles > 1.2 * off.EstimatedTriangles, $"{on.EstimatedTriangles} vs {off.EstimatedTriangles}");
            Assert.Equal(1.0, on.AlphaCoarsening);
            Assert.Equal(off.UsedAlpha, on.UsedAlpha);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(off.Result), AlphaWrapManifoldStepTests.Hash(on.Result));
            Assert.Equal(off.Result.TriangleCount, on.Result.TriangleCount);
            // (a negative budget is off as well)
            MeshAlphaWrap neg = Wrap(m, RelAlpha, -1);
            Assert.Equal(off.EstimatedTriangles, neg.EstimatedTriangles);
        }

        // invariance over Similarity.All() at a fixed alpha (scaled with the frame): the same open area, the estimate bit for bit in the
        // 2^k frames and within 1e-6 in the others; the budget's alpha likewise
        [Fact]
        public void Estimate_Invariance()
        {
            DMesh3 input = Mixed();
            double alpha = input.CachedBounds.DiagonalLength / RelAlpha;
            double e0 = 0, b0 = 0, o0 = 0;
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = Invariance.Transformed(input, s);
                var w = new MeshAlphaWrap(m) { Alpha = alpha * s.Scale, Offset = alpha * s.Scale / 10, MaxEstimatedTriangles = 1000 };
                Assert.True(w.Compute(), w.Error);
                // (no budget: the one-sided estimate, bit for bit in the 2^k frames as well)
                var off = new MeshAlphaWrap(m) { Alpha = alpha * s.Scale, Offset = alpha * s.Scale / 10 };
                Assert.True(off.Compute(), off.Error);
                if (s.Name == Similarity.Identity.Name) o0 = off.EstimatedTriangles;
                if (s.IsExact) Assert.Equal(o0, off.EstimatedTriangles);
                else Assert.True(Math.Abs(off.EstimatedTriangles - o0) <= 1e-6 * o0, $"{s.Name}: {off.EstimatedTriangles} vs {o0}");
                if (s.Name == Similarity.Identity.Name) { e0 = w.EstimatedTriangles; b0 = w.AlphaCoarsening; }
                if (s.IsExact) {
                    Assert.Equal(e0, w.EstimatedTriangles);
                    Assert.Equal(b0, w.AlphaCoarsening);
                } else
                    Assert.True(Math.Abs(w.EstimatedTriangles - e0) <= 1e-6 * e0, $"{s.Name}: {w.EstimatedTriangles} vs {e0}");
                Assert.True(w.AlphaCoarsening > 1.5, s.Name);
            }
        }

        // reuse: Compute() twice on one instance gives the same estimate and result; the open area is recomputed for a changed input
        [Fact]
        public void Reuse()
        {
            var w = new MeshAlphaWrap(Build("open sheet")) { RelativeAlpha = RelAlpha, RelativeOffset = 10 * RelAlpha, MaxEstimatedTriangles = NeverCoarsens };
            Assert.True(w.Compute(), w.Error);
            double e = w.EstimatedTriangles; string h = AlphaWrapManifoldStepTests.Hash(w.Result);
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(e, w.EstimatedTriangles);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(w.Result));
            w.Mesh = Build("closed soup sphere");
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(OneSided(w.Mesh, w), w.EstimatedTriangles, 12);
            w.Mesh = Build("open sheet");
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(e, w.EstimatedTriangles);
            // the budget switched on and off on the instance: the estimate follows it, the wrap does not
            w.MaxEstimatedTriangles = 0;
            Assert.True(w.Compute(), w.Error);
            Assert.Equal(OneSided(w.Mesh, w), w.EstimatedTriangles, 12);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(w.Result));
        }
    }
}
