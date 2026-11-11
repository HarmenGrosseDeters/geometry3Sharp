using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class PointCloudNormalsTests
    {
        /// <summary> fraction of points with the sign of the true normal, and fraction with |cos| of the axis above the limit </summary>
        static void Accuracy(PointCloudNormals pn, List<Vector3d> truth, out double right, out double axisOk, double axisLimit = 0.9)
        {
            int r = 0, a = 0, n = truth.Count;
            for (int i = 0; i < n; ++i) {
                double c = pn.Normals[i].Dot(truth[i]);
                if (c > 0) r++;
                if (Math.Abs(c) >= axisLimit) a++;
            }
            right = (double)r / n; axisOk = (double)a / n;
        }

        public static IEnumerable<object[]> Samplings() => new[] {
            new object[] { "clean", 0.0, false, false, 0.99, 0.99 },
            new object[] { "noise", 0.08, false, false, 0.99, 0.85 },
            new object[] { "hole", 0.0, true, false, 0.99, 0.99 },
            new object[] { "non-uniform", 0.0, false, true, 0.99, 0.99 },
            new object[] { "noise+hole+non-uniform", 0.08, true, true, 0.97, 0.80 },
        };

        [Theory]
        [MemberData(nameof(Samplings))]
        public void Torus_AxesAndSigns(string name, double sigma, bool hole, bool nonUniform, double minRight, double minAxis)
        {
            var pts = PointClouds.Torus(4000, 7, out var truth, sigma, hole, nonUniform);
            var pn = new PointCloudNormals(pts);
            Assert.True(pn.Compute(), $"{name}: {pn.Status} confident {pn.ConfidentFraction:P1} alpha {pn.UsedAlpha} {pn.Error}");
            Assert.Equal(PointCloudNormals.NormalsStatus.Success, pn.Status);
            Accuracy(pn, truth, out double right, out double axisOk);
            Assert.True(right >= minRight, $"{name}: {right:P1} of the signs right");
            Assert.True(axisOk >= minAxis, $"{name}: {axisOk:P1} of the axes within 26 degrees");
            Assert.True(pn.AlphaConditionMet);
            foreach (Vector3d n in pn.Normals)
                if (n != Vector3d.Zero) Assert.Equal(1.0, n.Length, 9);
            // the wrap that oriented the points is kept, closed, and encloses them
            ResultChecks.AssertClosed(pn.Wrap);
            // a hole or a density gap lets the first wrap into the solid: Alpha was enlarged (and the sign still found)
            if (hole || nonUniform) Assert.True(pn.Escalations > 0, name + ": no escalation");
            else Assert.Equal(0, pn.Escalations);
        }

        [Fact]
        public void PointArea_SumsToTheAreaOfTheSurface()
        {
            // the area each point stands for (the weight of the winding number of the cloud): about the area of the torus in total
            var pts = PointClouds.Torus(4000, 7, out _);
            var pn = new PointCloudNormals(pts);
            Assert.True(pn.Compute());
            Assert.Equal(pts.Count, pn.PointArea.Length);
            double torus = 4 * Math.PI * Math.PI * PointClouds.TorusR * PointClouds.Torusr;
            Assert.InRange(pn.PointArea.Sum(), 0.85 * torus, 1.15 * torus);
            // scale: areas scale with the square of the length
            var big = new PointCloudNormals(pts.Select(p => 1024 * p).ToList());
            Assert.True(big.Compute());
            Assert.Equal(1024.0 * 1024.0 * pn.PointArea[17], big.PointArea[17]);
        }

        [Fact]
        public void Sphere_AxesAreExactEnough()
        {
            var pts = PointClouds.Sphere(2000, 3, 2.0, out var truth);
            var pn = new PointCloudNormals(pts);
            Assert.True(pn.Compute(), pn.Status + pn.Error);
            Accuracy(pn, truth, out double right, out double axisOk, 0.98);
            Assert.Equal(1.0, right);
            Assert.True(axisOk > 0.99, $"{axisOk:P1}");
            Assert.Equal(pts.Count, pn.ConfidentCount);
        }

        [Fact]
        public void OpenSheet_IsReportedNotGuessed()
        {
            // a flat sampling has no outside: both probes are outside the thin shell of the wrap
            var rnd = new Random(2);
            var pts = Enumerable.Range(0, 1500).Select(i => new Vector3d(4 * rnd.NextDouble(), 4 * rnd.NextDouble(), 0)).ToList();
            var pn = new PointCloudNormals(pts);
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.AmbiguousOrientation, pn.Status);
            Assert.True(pn.ConfidentFraction < 0.05, $"{pn.ConfidentFraction:P1} confident");
            Assert.True(pn.Escalations == pn.MaxEscalations);
            // the axes are still right
            foreach (Vector3d n in pn.Normals)
                Assert.True(Math.Abs(n.z) > 0.99);
        }

        [Fact]
        public void GivenSmallAlpha_IsReportedAsBelowTheCondition()
        {
            var pts = PointClouds.Sphere(1500, 5, 1.0, out var truth);
            var probe = new PointCloudNormals(pts) { Orient = false };
            Assert.True(probe.Compute());
            var pn = new PointCloudNormals(pts) { Alpha = 0.7 * probe.Spacing };
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.AlphaBelowCondition, pn.Status);
            Assert.False(pn.AlphaConditionMet);
            Assert.Equal(0, pn.Escalations);                 // a given Alpha is never changed
            Assert.Equal(0.7 * probe.Spacing, pn.UsedAlpha);
            Assert.NotNull(pn.Normals);
        }

        [Fact]
        public void OrientFalse_AxesOnly()
        {
            var pts = PointClouds.Torus(3000, 9, out var truth, 0.02);
            var pn = new PointCloudNormals(pts) { Orient = false };
            Assert.True(pn.Compute());
            Assert.Null(pn.Wrap);
            Accuracy(pn, truth, out double right, out double axisOk);
            Assert.True(axisOk > 0.95, $"{axisOk:P1}");
            foreach (Vector3d n in pn.Normals) {
                double m = new[] { n.x, n.y, n.z }.OrderByDescending(Math.Abs).First();
                Assert.True(m > 0, "the largest component is positive");
            }
        }

        [Fact]
        public void InvalidAndTooFewInput()
        {
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, Run(new PointCloudNormals(null)));
            var pts = PointClouds.Sphere(100, 1, 1, out _);
            pts[17] = new Vector3d(1, double.NaN, 0);
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, Run(new PointCloudNormals(pts)));
            pts[17] = new Vector3d(1, double.PositiveInfinity, 0);
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, Run(new PointCloudNormals(pts)));
            Assert.Equal(PointCloudNormals.NormalsStatus.TooFewPoints, Run(new PointCloudNormals(pts.Take(10).ToList()) { Neighbors = 16 }));
            // coincident points have no scale
            Assert.Equal(PointCloudNormals.NormalsStatus.TooFewPoints, Run(new PointCloudNormals(Enumerable.Repeat(new Vector3d(1, 2, 3), 50).ToList())));
        }

        static PointCloudNormals.NormalsStatus Run(PointCloudNormals pn) { Assert.False(pn.Compute()); return pn.Status; }

        [Fact]
        public void ExtentThatOverflows_IsInvalidInput_NotAHang()
        {
            var pts = PointClouds.Sphere(100, 1, 1.0, out _);
            pts[0] = new Vector3d(-1.5e308, 0, 0);
            pts[1] = new Vector3d(1.5e308, 0, 0);
            var pn = new PointCloudNormals(pts) { Orient = false };
            Assert.True(Timed.Finishes(() => pn.Compute(), TimeSpan.FromSeconds(10)));
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, pn.Status);
            Assert.Null(pn.Normals);
        }

        [Theory]
        [InlineData(1e-170)]
        [InlineData(1e160)]
        [InlineData(1e300)]
        public void AxesAtAnyScale(double radius)
        {
            // the squares of such coordinates overflow or underflow: the neighbourhoods are found in a unit frame
            var pts = PointClouds.Sphere(800, 3, radius, out var truth);
            var pn = new PointCloudNormals(pts) { Orient = false };
            Assert.True(pn.Compute(), pn.Status + " " + pn.Error);
            Accuracy(pn, truth, out _, out double axisOk, 0.98);
            Assert.True(axisOk > 0.98, $"{axisOk:P1}");
            Assert.InRange(pn.Spacing / radius, 0.01, 0.5);
        }

        [Fact]
        public void LargeScaleOrientsToo()
        {
            var pts = PointClouds.Sphere(1500, 3, 1e30, out var truth);
            var pn = new PointCloudNormals(pts);
            Assert.True(pn.Compute(), pn.Status + " " + pn.Error);
            Accuracy(pn, truth, out double right, out _);
            Assert.Equal(1.0, right);
        }

        [Fact]
        public void ClusteredCloudWithAnOutlier_IsFast()
        {
            // one cell of a bounding-box grid would hold every point: the k-d tree does not care
            var rnd = new Random(6);
            var pts = Enumerable.Range(0, 30000).Select(i => new Vector3d(1e-3 * rnd.NextDouble(), 1e-3 * rnd.NextDouble(), 1e-3 * rnd.NextDouble())).ToList();
            pts.Add(new Vector3d(1e3, 1e3, 1e3));
            var pn = new PointCloudNormals(pts) { Orient = false };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pn.Compute();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), sw.Elapsed.ToString());
        }

        [Fact]
        public void DuplicatedPoints_AreMergedAndShareTheNormal()
        {
            var pts = PointClouds.Sphere(500, 4, 1.0, out var truth);
            var repeated = new List<Vector3d>();
            for (int rep = 0; rep < 8; ++rep) repeated.AddRange(pts);
            var pn = new PointCloudNormals(repeated);
            Assert.True(pn.Compute(), pn.Status + " " + pn.Error);
            var single = new PointCloudNormals(pts);
            Assert.True(single.Compute());
            for (int i = 0; i < pts.Count; ++i)
                for (int rep = 0; rep < 8; ++rep)
                    Assert.Equal(single.Normals[i], pn.Normals[rep * pts.Count + i]);
            Assert.Equal(single.UsedAlpha, pn.UsedAlpha);
            Assert.Equal(repeated.Count, pn.ConfidentCount);
        }

        [Fact]
        public void HugeNeighbourCount_IsClamped()
        {
            var pts = PointClouds.Sphere(100, 1, 1.0, out _);
            var pn = new PointCloudNormals(pts) { Neighbors = int.MaxValue };
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.TooFewPoints, pn.Status);
            Assert.Null(pn.Normals);
            var big = PointClouds.Sphere(600, 1, 1.0, out _);
            Assert.True(new PointCloudNormals(big) { Neighbors = 100000, Orient = false }.Compute());
        }

        [Fact]
        public void FewDegenerateNeighbourhoods_StillSuccess_ButCounted()
        {
            // 1000 sphere points and a short line far away: Success, and DegenerateCount tells about the line
            var pts = PointClouds.Sphere(1000, 8, 1.0, out var truth);
            for (int i = 0; i < 30; ++i) pts.Add(new Vector3d(10 + i * 0.01, 0, 0));
            var pn = new PointCloudNormals(pts) { Orient = false };
            Assert.True(pn.Compute(), pn.Status + pn.Error);
            Assert.InRange(pn.DegenerateCount, 25, 30);
            Assert.Equal(pts.Count, pn.AxisCount + pn.DegenerateCount);
        }

        [Fact]
        public void PointLimitReached_CleanWrapIsUsed_AndReported()
        {
            var pts = PointClouds.Sphere(600, 2, 1.0, out var truth);
            var pn = new PointCloudNormals(pts) { MaxSteinerPoints = 30, MaxEscalations = 0 };
            pn.Compute();
            Assert.Equal(MeshAlphaWrap.WrapStatus.PointLimitReached, pn.WrapStatus);
            Assert.NotEqual(PointCloudNormals.NormalsStatus.WrapFailed, pn.Status);
            Assert.Equal("", PointCloudNormals.WrapProblem(pn.Wrap));
            Accuracy(pn, truth, out double right, out _);
            Assert.True(right > 0.9, $"{right:P1} right");
        }

        [Fact]
        public void WrapProblem_NamesWhatTheProbesCannotUse()
        {
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();
            Assert.Equal("", PointCloudNormals.WrapProblem(sphere));
            DMesh3 open = new DMesh3(sphere);
            open.RemoveTriangle(open.TriangleIndices().First(), true);
            Assert.Equal("not closed", PointCloudNormals.WrapProblem(open));
            // a closed mesh that passes through itself: a second sphere's shell merged into the first, shifted
            DMesh3 crossing = new DMesh3(sphere);
            DMesh3 other = new DMesh3(sphere);
            MeshTransforms.Translate(other, new Vector3d(0.5, 0, 0));
            new MeshEditor(crossing).AppendMesh(other);
            Assert.Equal("self-intersections", PointCloudNormals.WrapProblem(crossing));
        }

        [Fact]
        public void PointLimitReached_UncleanWrap_TriesACoarserAlpha_ThenFails()
        {
            var pts = PointClouds.Sphere(600, 2, 1.0, out _);
            // every limited wrap is reported unclean: with the class's own Alpha it is enlarged, MaxEscalations times, then WrapFailed
            var pn = new PointCloudNormals(pts) { MaxSteinerPoints = 30, MaxEscalations = 2 };
            int checks = 0;
            pn.WrapProblemF = (w) => { checks++; return "simulated defect"; };
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.WrapFailed, pn.Status);
            Assert.Contains("(not clean: simulated defect)", pn.Error);
            Assert.Equal(3, checks);
            Assert.Equal(2, pn.Escalations);
            Assert.Null(pn.Wrap);
            Assert.Equal(0, pn.ConfidentCount);
            Assert.Equal(pts.Count, pn.Normals.Length);
            // a first wrap that is used (clean, but too few confident points) and a later one that fails: no stale wrap of the first attempt
            var late = new PointCloudNormals(pts) { MaxSteinerPoints = 30, MaxEscalations = 2 };
            int lateCalls = 0;
            late.WrapProblemF = (w) => ++lateCalls == 1 ? "" : "simulated defect";
            Assert.False(late.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.WrapFailed, late.Status);
            Assert.True(lateCalls >= 2, "the first wrap was not enough: " + lateCalls);
            Assert.Null(late.Wrap);
            Assert.Equal(0, late.ConfidentCount);
            // a given Alpha is never changed: fails at once
            var given = new PointCloudNormals(pts) { MaxSteinerPoints = 30, Alpha = 0.3 };
            given.WrapProblemF = (w) => "simulated defect";
            Assert.False(given.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.WrapFailed, given.Status);
            Assert.Equal(0, given.Escalations);
            // the first limited wrap unclean, the coarser one clean: used
            var recovered = new PointCloudNormals(pts) { MaxSteinerPoints = 30, MaxEscalations = 2 };
            int calls = 0;
            recovered.WrapProblemF = (w) => ++calls == 1 ? "simulated defect" : "";
            recovered.Compute();
            Assert.True(recovered.Escalations >= 1);
            Assert.NotNull(recovered.Wrap);
            Assert.NotEqual(PointCloudNormals.NormalsStatus.WrapFailed, recovered.Status);
        }

        [Fact]
        public void NonFiniteAlpha_IsInvalidInput()
        {
            var pts = PointClouds.Sphere(100, 1, 1.0, out _);
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, Run(new PointCloudNormals(pts) { Alpha = double.PositiveInfinity }));
            Assert.Equal(PointCloudNormals.NormalsStatus.InvalidInput, Run(new PointCloudNormals(pts) { Offset = double.NaN }));
        }

        [Fact]
        public void CollinearCloud_IsDegenerateInput_AlsoWithoutOrient()
        {
            var line = Enumerable.Range(0, 100).Select(i => new Vector3d(i * 0.1, 0, 0)).ToList();
            foreach (bool orient in new[] { false, true }) {
                var pn = new PointCloudNormals(line) { Orient = orient };
                Assert.False(pn.Compute());
                Assert.Equal(PointCloudNormals.NormalsStatus.DegenerateInput, pn.Status);
                Assert.Equal(0, pn.AxisCount);
                Assert.All(pn.Normals, n => Assert.Equal(Vector3d.Zero, n));
            }
        }

        [Fact]
        public void CollinearNeighbourhoods_HaveNoNormal()
        {
            // points on a line, and a plane of points away from it: the line points get a zero normal and are never confident
            var pts = new List<Vector3d>();
            for (int i = 0; i < 40; ++i) pts.Add(new Vector3d(i * 0.1, 0, 0));
            var rnd = new Random(4);
            for (int i = 0; i < 600; ++i) pts.Add(new Vector3d(10 + 3 * rnd.NextDouble(), 3 * rnd.NextDouble(), 0));
            var pn = new PointCloudNormals(pts) { Orient = false };
            Assert.True(pn.Compute());
            for (int i = 0; i < 25; ++i)        // (the end of the line has plane points among its neighbours)
                Assert.Equal(Vector3d.Zero, pn.Normals[i]);
            for (int i = 100; i < pts.Count; ++i)
                Assert.True(Math.Abs(pn.Normals[i].z) > 0.99, "plane point " + i);
            Assert.Equal(pts.Count - pn.Normals.Count(v => v == Vector3d.Zero), pn.AxisCount);
            Assert.Equal(pts.Count - pn.AxisCount, pn.DegenerateCount);
            Assert.True(pn.DegenerateCount > 0);
        }

        [Fact]
        public void GivenAlpha_NoiseConditionIsChecked()
        {
            // a dense, noisy sphere: the noise term of the condition is larger than the spacing term, and Alpha is just below it
            var pts = PointClouds.Sphere(20000, 5, 1.0, out _);
            var rnd = new Random(3);
            Func<double> g = () => { double a = 1 - rnd.NextDouble(), b = rnd.NextDouble(); return Math.Sqrt(-2 * Math.Log(a)) * Math.Cos(2 * Math.PI * b); };
            pts = pts.Select(p => p + 0.05 * new Vector3d(g(), g(), g())).ToList();
            var probe = new PointCloudNormals(pts) { Orient = false };
            Assert.True(probe.Compute());
            Assert.True(2.5 * probe.Spacing < 6.0 * probe.NoiseSigma, $"spacing {probe.Spacing} noise {probe.NoiseSigma}");
            var pn = new PointCloudNormals(pts) { Alpha = 6.0 * probe.NoiseSigma };
            pn.Compute();
            Assert.Equal(PointCloudNormals.NormalsStatus.AlphaBelowCondition, pn.Status);
            // the class's own choice meets both
            var auto = new PointCloudNormals(pts);
            Assert.True(auto.Compute(), auto.Status + auto.Error);
            Assert.True(auto.UsedAlpha >= 6.5 * auto.NoiseSigma && auto.UsedAlpha >= 2.5 * auto.Spacing);
        }

        [Fact]
        public void AmbiguousProbes_FallBackToTheNearestWrapPoint()
        {
            // a removed patch lets the wrap into the torus; without the escalation the probes are ambiguous and the direction
            // to the nearest wrap point decides: not confident, but mostly right
            var pts = PointClouds.Torus(3000, 7, out var truth, 0, true);
            var pn = new PointCloudNormals(pts) { MaxEscalations = 0 };
            Assert.False(pn.Compute());
            Assert.Equal(PointCloudNormals.NormalsStatus.AmbiguousOrientation, pn.Status);
            Assert.True(pn.ConfidentFraction < 0.05);
            Accuracy(pn, truth, out double right, out _);
            Assert.True(right > 0.85, $"{right:P1} right");
        }

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Invariant_OverSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            var pts = PointClouds.Torus(1500, 11, out var truth, 0.03);
            var reference = new PointCloudNormals(pts);
            Assert.True(reference.Compute(), reference.Status + reference.Error);
            var moved = new PointCloudNormals(pts.Select(s.Apply).ToList());
            Assert.True(moved.Compute(), name + ": " + moved.Status + " " + moved.Error);
            for (int i = 0; i < pts.Count; ++i) {
                Vector3d expected = s.ApplyDirection(reference.Normals[i]);
                if (s.IsExact)
                    Assert.Equal(expected, moved.Normals[i]);            // bit for bit
                else
                    Assert.True(expected.Dot(moved.Normals[i]) > 0.999, $"{name}: normal {i} differs ({expected} vs {moved.Normals[i]})");
            }
            if (s.IsExact) {
                Assert.Equal(s.Scale * reference.Spacing, moved.Spacing);
                Assert.Equal(s.Scale * reference.UsedAlpha, moved.UsedAlpha);
            }
            Assert.Equal(reference.ConfidentCount, moved.ConfidentCount);
        }

        [Fact]
        public void Reuse_ComputeTwice()
        {
            var pts = PointClouds.Torus(2000, 3, out _, 0.05, true);
            var pn = new PointCloudNormals(pts);
            Assert.True(pn.Compute());
            Vector3d[] first = (Vector3d[])pn.Normals.Clone();
            int confident = pn.ConfidentCount;
            Assert.True(pn.Compute());
            Assert.Equal(first, pn.Normals);
            Assert.Equal(confident, pn.ConfidentCount);
        }

        [Fact]
        public void Determinism_ParallelEqualsSerial()
        {
            var pts = PointClouds.Torus(2500, 5, out _, 0.04, false, true);
            var parallel = new PointCloudNormals(pts) { Parallel = true };
            Assert.True(parallel.Compute());
            var serial = new PointCloudNormals(pts) { Parallel = false };
            Assert.True(serial.Compute());
            Assert.Equal(parallel.Normals, serial.Normals);
            Assert.Equal(parallel.Confident, serial.Confident);
            gParallel.SerialOnThisThread = true;
            try {
                var forced = new PointCloudNormals(pts) { Parallel = true };
                Assert.True(forced.Compute());
                Assert.Equal(parallel.Normals, forced.Normals);
            } finally { gParallel.SerialOnThisThread = false; }
            // repeated runs
            var again = new PointCloudNormals(pts);
            Assert.True(again.Compute());
            Assert.Equal(parallel.Normals, again.Normals);
        }

        [Fact]
        public void PointTree_NearestMatchesBruteForce()
        {
            // clustered points with duplicates and ties (a lattice), an outlier far away, and a flat sampling
            var rnd = new Random(8);
            var pts = new List<Vector3d>();
            for (int x = 0; x < 6; ++x) for (int y = 0; y < 6; ++y) for (int z = 0; z < 3; ++z) pts.Add(new Vector3d(x, y, z));
            for (int i = 0; i < 150; ++i) pts.Add(new Vector3d(rnd.NextDouble(), rnd.NextDouble(), 0));
            pts.Add(pts[5]); pts.Add(pts[5]);
            AssertNearest(pts.ToArray(), 12);
            // a volume sampling, and a sampling with a far outlier (one huge cell)
            var volume = Enumerable.Range(0, 700).Select(i => new Vector3d(5 * rnd.NextDouble(), 2 * rnd.NextDouble(), 9 * rnd.NextDouble())).ToList();
            AssertNearest(volume.ToArray(), 16);
            volume.Add(new Vector3d(1e4, -5e3, 300));
            AssertNearest(volume.ToArray(), 16);
            // a dense cluster and a sparse background: the k-th distance of the sparse points is several cells
            for (int seed = 0; seed < 4; ++seed) {
                var mixed = Enumerable.Range(0, 500).Select(i => new Vector3d(0.5 * rnd.NextDouble(), 0.5 * rnd.NextDouble(), 0.5 * rnd.NextDouble())).ToList();
                mixed.AddRange(Enumerable.Range(0, 60 + 20 * seed).Select(i => new Vector3d(30 * rnd.NextDouble(), 30 * rnd.NextDouble(), 30 * rnd.NextDouble())));
                AssertNearest(mixed.ToArray(), 8 + 4 * seed);
            }
        }

        static void AssertNearest(Vector3d[] p, int k)
        {
            var grid = new PointCloudNormals.PointTree(p);
            var index = new int[k]; var dist2 = new double[k];
            for (int i = 0; i < p.Length; ++i) {
                grid.Nearest(i, k, index, dist2);
                var expected = Enumerable.Range(0, p.Length).OrderBy(j => (p[j] - p[i]).LengthSquared).ThenBy(j => j).Take(k).ToArray();
                Assert.Equal(expected, index);
                for (int j = 0; j < k; ++j) Assert.Equal((p[expected[j]] - p[i]).LengthSquared, dist2[j]);
            }
        }
    }
}
