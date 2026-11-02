using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The displacement bound of MeshRoundingRepair (SafeRounding): the drift of every vertex stays at most
    /// MaxDisplacement times the diagonal (edits beyond it are not applied), MaxDrift reports it, and the repaired
    /// surface is the same solid as the one at the start up to MaxDrift.
    /// </summary>
    public class SafeRoundingBoundTests
    {
        static DMesh3 Sphere() => new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 6 }.Generate().MakeDMesh();

        // edges of a sphere split at the given distances from their first end (needles and caps)
        static DMesh3 SplitSphere(params double[] at)
        {
            DMesh3 m = Sphere();
            foreach (int eid in m.EdgeIndices().ToArray()) {
                if (eid % 7 != 0 || m.IsEdge(eid) == false)
                    continue;
                Index2i ev = m.GetEdgeV(eid);
                Vector3d a = m.GetVertex(ev.a), b = m.GetVertex(ev.b), dir = (b - a).Normalized;
                int e = eid;
                // the farthest split first, then the nearer ones on the edge towards a
                foreach (double s in at.OrderByDescending(x => x)) {
                    Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out DMesh3.EdgeSplitInfo info));
                    m.SetVertex(info.vNew, a + s * dir);
                    e = m.FindEdge(ev.a, info.vNew);
                }
            }
            return m;
        }

        static double Diagonal(DMesh3 m) => gs.MeshRepairMetrics.TriangleBounds(m).DiagonalLength;

        static void AssertWithinBound(DMesh3 start, DMesh3 repaired, MeshRoundingRepair r, string what)
        {
            double bound = r.MaxDisplacement * Diagonal(start);
            Assert.True(r.MaxDrift <= bound, $"{what}: drift {r.MaxDrift:G3} above {bound:G3}");
            Assert.True(r.MaxDrift > 0 || r.Collapses + r.Flips + r.Moves + r.Zips == 0, what);
            Assert.True(BooleanOracle.SameSolid(repaired, start, 2, r.MaxDrift) == "", what);
        }

        [Fact]
        public void Needles_RepairedWithinTheBound()
        {
            DMesh3 start = SplitSphere(1e-13), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.Collapses > 0);
            AssertWithinBound(start, m, r, "needles");
        }

        [Fact]
        public void BudgetBinds_ChainedContractionsStopAtTheBound()
        {
            // two needles in a row on each split edge (1e-13 and 2e-13 from its end): contracting both into the end moves
            // the surface by up to 3e-13 in total
            double diag = Diagonal(SplitSphere(1e-13, 2e-13));
            DMesh3 free = SplitSphere(1e-13, 2e-13);
            var unbounded = new MeshRoundingRepair(free);
            Assert.True(unbounded.Apply());
            Assert.True(unbounded.MaxDrift > 2.2e-13, $"drift {unbounded.MaxDrift:G3}");
            // with a bound of 2.2e-13 (MaxDisplacement relative to the diagonal) the second contraction into the same
            // end is not applied where it would exceed it
            DMesh3 start = SplitSphere(1e-13, 2e-13), m = new DMesh3(start);
            var bounded = new MeshRoundingRepair(m) { MaxDisplacement = 2.2e-13 / diag };
            bounded.Apply();
            AssertWithinBound(start, m, bounded, "bounded");
            Assert.True(bounded.MaxDrift <= 2.2e-13);
            Assert.True(bounded.Collapses > 0);
        }

        [Fact]
        public void NearlyCoincidentBoxes_SafeRoundingWithinTheBound()
        {
            DMesh3 a = new TrivialBox3Generator() { Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            DMesh3 b = new DMesh3(a);
            MeshTransforms.Rotate(b, Vector3d.Zero, new Quaterniond(new Vector3d(0.3, 0.5, 0.8).Normalized, 1e-11 * MathUtil.Rad2Deg));
            MeshTransforms.Translate(b, 1e-11 * new Vector3d(0.7, 0.2, 0.4));
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection }) {
                var raw = new ExactMeshBoolean(a, b, op) { SafeRounding = false };
                Assert.True(raw.Compute(), raw.Error);
                var safe = new ExactMeshBoolean(a, b, op);
                Assert.True(safe.Compute(), safe.Error);
                Assert.True(safe.RoundingClean);
                Assert.True(safe.Rounding.RemovedComponents == 0);
                AssertWithinBound(raw.Result, safe.Result, safe.Rounding, op.ToString());
            }
        }

        // a sphere with triangles poked at a point 1e-11 (barycentric) from an edge: caps, repaired by flips
        static DMesh3 PokedSphere()
        {
            DMesh3 m = Sphere();
            foreach (int t in m.TriangleIndices().ToArray())
                if (t % 5 == 0)
                    Assert.Equal(MeshResult.Ok, m.PokeTriangle(t, new Vector3d(0.5, 0.5 - 1e-11, 1e-11), out DMesh3.PokeTriangleInfo info));
            return m;
        }

        [Fact]
        public void Caps_FlippedWithinTheBound()
        {
            DMesh3 start = PokedSphere(), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m);
            Assert.True(r.Apply());
            Assert.True(r.Flips > 0, $"flips {r.Flips}");
            AssertWithinBound(start, m, r, "caps");
        }

        [Fact]
        public void PowerOfTwoScales_SameResultScaled()
        {
            foreach (DMesh3 start in new[] { SplitSphere(1e-13, 2e-13), PokedSphere() }) {
                DMesh3 m0 = new DMesh3(start);
                var r0 = new MeshRoundingRepair(m0);
                r0.Apply();
                foreach (Similarity sim in Similarity.All().Where(s => s.IsExact)) {
                    DMesh3 m = new DMesh3(start);
                    MeshTransforms.PerVertexTransform(m, sim.Apply);
                    var r = new MeshRoundingRepair(m);
                    r.Apply();
                    Assert.Equal(r0.MaxDrift * sim.Scale, r.MaxDrift);
                    Assert.Equal(r0.Collapses + r0.Flips + r0.Moves + r0.Zips, r.Collapses + r.Flips + r.Moves + r.Zips);
                    // the same mesh, scaled, bit for bit
                    MeshTransforms.PerVertexTransform(m, sim.Inverse);
                    Assert.True(Invariance.Difference(m0, m) == "", sim.ToString());
                }
            }
        }

        [Fact]
        public void ReusedInstance_EqualsFreshInstance()
        {
            // one instance repairs the needles, then the caps (its drifts and counts must start again), then again
            var reused = new MeshRoundingRepair(new DMesh3(SplitSphere(1e-13, 2e-13)));
            Assert.True(reused.Apply());
            Assert.True(reused.MaxDrift > 0);
            foreach (int k in new[] { 0, 1 }) {
                DMesh3 caps = PokedSphere(), fresh = PokedSphere();
                reused.Mesh = caps;
                Assert.True(reused.Apply());
                var f = new MeshRoundingRepair(fresh);
                Assert.True(f.Apply());
                Assert.Equal("", Invariance.Difference(fresh, caps));
                Assert.Equal(f.MaxDrift, reused.MaxDrift);
                Assert.Equal(f.Flips + f.Collapses, reused.Flips + reused.Collapses);
            }
        }

        // a sphere poked four times in a row near edges, resolved without SafeRounding: strips of slivers along lines,
        // which only the strip zip repairs (see SafeRoundingDegenerateInputTests)
        static DMesh3 Strips()
        {
            var raw = new ExactMeshBoolean(SafeRoundingDegenerateInputTests.Make(SafeRoundingDegenerateInputTests.Family.Cluster, 30, 2, 4),
                new DMesh3(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute(), raw.Error);
            return raw.Result;
        }

        [Fact]
        public void Strips_ZippedWithinTheBound()
        {
            DMesh3 start = Strips(), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
            Assert.True(r.Apply());
            Assert.True(r.StripZips > 0 && r.StripZips <= r.Zips, $"strip zips {r.StripZips} of {r.Zips}");
            ResultChecks.AssertClean(m);
            AssertWithinBound(start, m, r, "strips");
            // without the strip zip: not clean, or only with moves that drift farther (since the moves no longer depend on
            // the scale, 16 moves repair these strips at 5 times the drift; before, Normalized zeroed their direction)
            DMesh3 m2 = new DMesh3(start);
            var without = new MeshRoundingRepair(m2) { SharedPositionsTouch = true, MaxStripTriangles = 1 };
            bool cleanWithout = without.Apply();
            Assert.True(cleanWithout == false || without.MaxDrift > 2 * r.MaxDrift, $"drift {without.MaxDrift:G3} against {r.MaxDrift:G3}");
            Assert.Equal(0, without.StripZips);
        }

        [Fact]
        public void Strips_PowerOfTwoScales_SameResultScaled()
        {
            DMesh3 start = Strips(), m0 = new DMesh3(start);
            var r0 = new MeshRoundingRepair(m0) { SharedPositionsTouch = true };
            r0.Apply();
            Assert.True(r0.StripZips > 0);
            foreach (Similarity sim in Similarity.All().Where(s => s.IsExact)) {
                DMesh3 m = new DMesh3(start);
                MeshTransforms.PerVertexTransform(m, sim.Apply);
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
                r.Apply();
                Assert.Equal(r0.MaxDrift * sim.Scale, r.MaxDrift);
                Assert.Equal(r0.StripZips, r.StripZips);
                MeshTransforms.PerVertexTransform(m, sim.Inverse);
                Assert.True(Invariance.Difference(m0, m) == "", sim.ToString());
            }
        }

        [Fact]
        public void Strips_ReusedInstance_EqualsFreshInstance()
        {
            // the caps first, then the strips twice: counts, drifts and the per-vertex state start again
            var reused = new MeshRoundingRepair(PokedSphere()) { SharedPositionsTouch = true };
            Assert.True(reused.Apply());
            foreach (int k in new[] { 0, 1 }) {
                DMesh3 strips = Strips(), fresh = Strips();
                reused.Mesh = strips;
                Assert.True(reused.Apply());
                var f = new MeshRoundingRepair(fresh) { SharedPositionsTouch = true };
                Assert.True(f.Apply());
                Assert.Equal("", Invariance.Difference(fresh, strips));
                Assert.Equal(f.MaxDrift, reused.MaxDrift);
                Assert.Equal(f.StripZips, reused.StripZips);
            }
        }

        // ---- coordinates far from the origin: the ulp tolerance (docs/design/SAFE_ROUNDING.md section 5) ----

        // the dense clusters, folded clusters, collinear pokes and slivers of SafeRoundingDegenerateInputTests at scale
        // 1e-3, rotated, near UTM coordinates (up to 5.7e6, one ulp 9.3e-10): 114 inputs whose rounding breaks more than
        // the relative bound (3.5e-8 here, 37 ulps) can repair
        static IEnumerable<(string name, DMesh3 mesh)> UtmCases()
        {
            Similarity sim = Similarity.ByName("rotated, scale 1e-3, utm");
            foreach (var f in new[] { SafeRoundingDegenerateInputTests.Family.Cluster, SafeRoundingDegenerateInputTests.Family.FoldedCluster,
                                      SafeRoundingDegenerateInputTests.Family.Collinear, SafeRoundingDegenerateInputTests.Family.Sliver })
                foreach (int k in new[] { 0, 30, 36, 44, 52 })
                    foreach (int count in new[] { 2, 4 })
                        for (int seed = 0; seed < 3; seed++)
                            if (k != 0 || f != SafeRoundingDegenerateInputTests.Family.Sliver)
                                yield return ($"{f} k{k} count {count} seed {seed}",
                                    Invariance.Transformed(SafeRoundingDegenerateInputTests.Make(f, k, seed, count), sim));
        }

        static double Ulp(DMesh3 m) => Math.Pow(2, Math.Floor(Math.Log(m.Vertices().Max(v => v.MaxAbs), 2)) - 52);

        [Fact]
        public void Utm_CleanWithTheUlpTolerance_WithinIt()
        {
            // 108 of 114 clean with the default (UlpFactor 256); 92 with the relative bound only (UlpFactor 0, before).
            // Every result within its bound, MaxDrift <= MaxLength = 256 ulps (0.24 micrometres here)
            int clean = 0, cleanRelative = 0, removals = 0, n = 0;
            foreach (var (name, mesh) in UtmCases()) {
                var op = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union);
                Assert.True(op.Compute(), name);
                n++;
                MeshRoundingRepair r = op.Rounding;
                double ulp = Ulp(op.Result);
                Assert.True(r.MaxDrift <= r.MaxLength && r.MaxLength == 256 * ulp, $"{name}: drift {r.MaxDrift:G3}, bound {r.MaxLength:G3}");
                if (op.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(op.Result) == "")
                    clean++;
                removals += r.VertexRemovals;
                var relative = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union) { RoundingUlpFactor = 0 };
                Assert.True(relative.Compute(), name);
                Assert.Equal(0, relative.Rounding.VertexRemovals);
                if (relative.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(relative.Result) == "")
                    cleanRelative++;
            }
            Assert.Equal(114, n);
            Assert.True(clean >= 108, $"clean {clean} of {n} (relative bound only: {cleanRelative})");
            Assert.True(cleanRelative <= 92, $"relative bound only: {cleanRelative}");
            Assert.True(removals > 0, "vertex removals");
        }

        // the 2^k scales give the same result bit for bit on the dense clusters too. Before the fix, 88 of the 198 clusters of
        // SafeRoundingDegenerateInputTests differed at 2^-10 or 2^10 (eg Cluster k0 count 4 seed 5: clean only at 2^10):
        // fix_degenerate moved the apex of a cap along Normalized, fix_intersection along GetTriNormal and Normalized, and
        // those return 0 below the absolute MathUtil.Epsilon, so an offset of 1e-17 gave no move at scale 1 but one at
        // 2^10 (docs/design/SAFE_ROUNDING.md, the snap-rounding experiment)
        [Theory]
        [InlineData(SafeRoundingDegenerateInputTests.Family.Cluster, 0, 4, 5)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.Cluster, 0, 8, 4)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.Cluster, 30, 8, 1)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.Cluster, 40, 4, 0)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.Cluster, 52, 8, 3)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.FoldedCluster, 36, 8, 5)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.FoldedCluster, 44, 8, 1)]
        [InlineData(SafeRoundingDegenerateInputTests.Family.FoldedCluster, 36, 8, 2)]
        public void Clusters_PowerOfTwoScales_SameResultScaled(SafeRoundingDegenerateInputTests.Family f, int k, int count, int seed)
        {
            DMesh3 input = SafeRoundingDegenerateInputTests.Make(f, k, seed, count);
            var op0 = new ExactMeshBoolean(new DMesh3(input), new DMesh3(), Ops.Union);
            Assert.True(op0.Compute());
            // (and extreme ones, where the edges are shorter than MathUtil.Epsilon: 2^-60, or longer than its inverse)
            var scales = Similarity.All().Where(s => s.IsExact).Concat(new[] {
                new Similarity("scale 2^-60", Math.Pow(2, -60), Matrix3d.Identity, Vector3d.Zero),
                new Similarity("scale 2^60", Math.Pow(2, 60), Matrix3d.Identity, Vector3d.Zero) });
            foreach (Similarity sim in scales) {
                var op = new ExactMeshBoolean(Invariance.Transformed(input, sim), new DMesh3(), Ops.Union);
                Assert.True(op.Compute(), sim.Name);
                Assert.True(Invariance.Difference(op0.Result, Invariance.MappedBack(op.Result, sim)) == "", sim.Name);
                Assert.Equal(op0.Rounding.MaxDrift * sim.Scale, op.Rounding.MaxDrift);
                Assert.Equal(op0.RoundingClean, op.RoundingClean);
            }
        }

        // a corner exactly at the origin, 2^-60 inside another box: fix_intersection's first step for a vertex at the
        // origin was an absolute 1e-300 (times 16 per try), so the move it found did not scale with the mesh
        [Fact]
        public void CornerAtTheOrigin_PowerOfTwoScales_SameResultScaled()
        {
            DMesh3 Make(double s)
            {
                DMesh3 m = new TrivialBox3Generator() { Box = new Box3d(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One * s)) }.Generate().MakeDMesh();
                MeshEditor.Append(m, new TrivialBox3Generator() {
                    Box = new Box3d(new AxisAlignedBox3d(new Vector3d(-0.75, -0.5, -1) * s, new Vector3d(0.5, 0.75, Math.Pow(2, -60)) * s)) }.Generate().MakeDMesh());
                return m;
            }
            DMesh3 m0 = Make(1);
            Assert.Contains(Vector3d.Zero, m0.Vertices());
            var r0 = new MeshRoundingRepair(m0);
            r0.Apply();
            Assert.True(r0.Moves > 0, "moves");
            foreach (double s in new[] { Math.Pow(2, -10), Math.Pow(2, 10) }) {
                DMesh3 m = Make(s);
                var r = new MeshRoundingRepair(m);
                r.Apply();
                DMesh3 back = new DMesh3(m);
                MeshTransforms.Scale(back, 1 / s);
                Assert.True(Invariance.Difference(m0, back) == "", s.ToString());
                Assert.Equal(r0.MaxDrift * s, r.MaxDrift);
            }
        }

        [Fact]
        public void Utm_PowerOfTwoScales_SameResultScaled()
        {
            // the ulp of the largest coordinate scales by 2^k too: the same result, bit for bit
            var (name, mesh) = UtmCases().First(c => c.name == "Cluster k0 count 4 seed 0");
            var op0 = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union);
            Assert.True(op0.Compute());
            Assert.True(op0.Rounding.VertexRemovals > 0, name);
            foreach (double s in new[] { 1.0 / 1024, 1024 }) {
                DMesh3 scaled = new DMesh3(mesh);
                MeshTransforms.Scale(scaled, s);
                var op = new ExactMeshBoolean(scaled, new DMesh3(), Ops.Union);
                Assert.True(op.Compute());
                Assert.Equal(op0.Rounding.MaxLength * s, op.Rounding.MaxLength);
                Assert.Equal(op0.Rounding.MaxDrift * s, op.Rounding.MaxDrift);
                DMesh3 back = new DMesh3(op.Result);
                MeshTransforms.Scale(back, 1 / s);
                Assert.True(Invariance.Difference(op0.Result, back) == "", s.ToString());
            }
        }

        [Fact]
        public void OrdinaryMeshes_UlpToleranceChangesNothing()
        {
            // near the origin (and at UTM coordinates for meshes of ordinary size) the ulp term is far below the relative
            // bound: the same result, bit for bit, with and without it
            foreach (DMesh3 start in new[] { Strips(), SplitSphere(1e-13, 2e-13), PokedSphere(),
                                             Invariance.Transformed(PokedSphere(), Similarity.ByName("utm")) }) {
                DMesh3 a = new DMesh3(start), b = new DMesh3(start);
                var ra = new MeshRoundingRepair(a) { SharedPositionsTouch = true };
                var rb = new MeshRoundingRepair(b) { SharedPositionsTouch = true, UlpFactor = 0 };
                ra.Apply(); rb.Apply();
                Assert.Equal(rb.MaxLength, ra.MaxLength);
                Assert.Equal(0, ra.VertexRemovals);
                Assert.Equal("", Invariance.Difference(a, b));
            }
        }

        [Fact]
        public void AbsoluteTolerance_RaisesTheBound()
        {
            DMesh3 start = SplitSphere(1e-13, 2e-13), m = new DMesh3(start);
            var r = new MeshRoundingRepair(m) { AbsoluteTolerance = 1e-3 };
            Assert.True(r.Apply());
            Assert.Equal(1e-3, r.MaxLength);
            Assert.True(r.MaxDrift <= r.MaxLength);
        }

        [Fact]
        public void StrictRounding_FailsWithTheReason_InsteadOfDefects()
        {
            // a UTM case that the relative bound cannot repair: strict, Compute fails with the defects left, where, and
            // the drift the smallest rejected edit needed; not strict, it is clean
            var (name, mesh) = UtmCases().First(c => c.name == "Cluster k0 count 4 seed 0");
            var strict = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union) { StrictRounding = true };
            Assert.False(strict.Compute(), name);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.RoundingNotClean, strict.Status);
            Assert.Null(strict.Result);
            Assert.Contains("rounding not clean", strict.Error);
            Assert.Contains("smallest edit rejected by the bound needed", strict.Error);
            Assert.True(strict.Rounding.SmallestRejectedDrift > strict.Rounding.MaxLength
                && strict.Rounding.SmallestRejectedDrift < double.PositiveInfinity, strict.Error);
            Assert.True(strict.Rounding.RemainingDefectAt.Distance(mesh.GetBounds().Center) < 1, strict.Error);
            var lenient = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union);
            Assert.True(lenient.Compute());
            Assert.True(lenient.RoundingClean && gs.MeshRobustRepair.NotCleanReasons(lenient.Result) == "", name);
            // an ordinary case: strict and clean, the same result as not strict
            var ok = new ExactMeshBoolean(PokedSphere(), new DMesh3(), Ops.Union) { StrictRounding = true };
            Assert.True(ok.Compute(), ok.Error);
            Assert.Equal(ExactMeshBoolean.BooleanStatus.Success, ok.Status);
            var plain = new ExactMeshBoolean(PokedSphere(), new DMesh3(), Ops.Union);
            Assert.True(plain.Compute());
            Assert.Equal("", Invariance.Difference(plain.Result, ok.Result));
        }

        [Fact]
        public void Utm_ReusedInstance_EqualsFreshInstance()
        {
            // the coarse state (ulp term, fixed vertices ignored, contact pass) does not carry over to the next mesh
            var (name, mesh) = UtmCases().First(c => c.name == "Cluster k0 count 4 seed 0");
            var raw = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union) { SafeRounding = false };
            Assert.True(raw.Compute());
            var reused = new MeshRoundingRepair(new DMesh3(raw.Result)) { SharedPositionsTouch = true };
            reused.Apply();
            Assert.True(reused.VertexRemovals > 0, name);
            foreach (int k in new[] { 0, 1 }) {
                DMesh3 m = PokedSphere(), fresh = PokedSphere();
                reused.Mesh = m;
                reused.Apply();
                var f = new MeshRoundingRepair(fresh) { SharedPositionsTouch = true };
                f.Apply();
                Assert.Equal("", Invariance.Difference(fresh, m));
                Assert.Equal(f.MaxLength, reused.MaxLength);
                Assert.Equal(0, reused.VertexRemovals);
            }
        }

        [Fact]
        public void VertexRemoval_BoundIsTheTrueDistance_FarFromTheOrigin()
        {
            // (bounds review) a tetrahedron with 100 m edges at UTM coordinates, one face poked by a vertex u up to 0.5
            // micrometres above it, and a second tetrahedron with its apex exactly at u; with MaxDisplacement 1e-9 the ulp
            // term is active. DistPoint3Triangle3.Get loses distances below about 1e-8 of the triangle's size: a removal
            // of u counted as 0 drift. Now every removal's drift is at least the distance of u to the new triangle (in local
            // coordinates, exact by Sterbenz), and none beyond the bound is applied
            var rnd = new Random(5);
            int removals = 0;
            for (int seed = 0; seed < 40; seed++) {
                double M = 5.7e6, L = 100;
                var O = new Vector3d(M * (0.5 + 0.5 * rnd.NextDouble()), M * (0.5 + 0.5 * rnd.NextDouble()), 0.1 * M * rnd.NextDouble());
                Func<Vector3d> r = () => O + new Vector3d(L * (rnd.NextDouble() - .5), L * (rnd.NextDouble() - .5), L * (rnd.NextDouble() - .5));
                Vector3d p0 = r(), p1 = r(), p2 = r();
                Vector3d n = (p1 - p0).Cross(p2 - p0).Normalized;
                Vector3d p3 = (p0 + p1 + p2) / 3 - 30 * n;
                double h = 1e-9 + 5e-7 * rnd.NextDouble();
                double w0 = rnd.NextDouble(), w1 = rnd.NextDouble() * (1 - w0);
                Vector3d u = p0 + w0 * (p1 - p0) + w1 * (p2 - p0) + h * n;
                var m = new DMesh3();
                int a = m.AppendVertex(p0), b = m.AppendVertex(p1), c = m.AppendVertex(p2), d = m.AppendVertex(p3), vu = m.AppendVertex(u);
                m.AppendTriangle(vu, a, b); m.AppendTriangle(vu, b, c); m.AppendTriangle(vu, c, a);
                m.AppendTriangle(a, d, b); m.AppendTriangle(b, d, c); m.AppendTriangle(c, d, a);
                Vector3d q0 = u + 10 * n + new Vector3d(5, 0, 0), q1 = u + 10 * n + new Vector3d(-3, 4, 1), q2 = u + 10 * n + new Vector3d(-2, -4, -1);
                int e = m.AppendVertex(u), f0 = m.AppendVertex(q0), f1 = m.AppendVertex(q1), f2 = m.AppendVertex(q2);
                m.AppendTriangle(e, f0, f1); m.AppendTriangle(e, f1, f2); m.AppendTriangle(e, f2, f0); m.AppendTriangle(f0, f2, f1);
                var rr = new MeshRoundingRepair(m) { SharedPositionsTouch = true, MaxDisplacement = 1e-9 };
                rr.Apply();
                Assert.True(rr.MaxDrift <= rr.MaxLength, $"seed {seed}");
                if (rr.VertexRemovals == 0)
                    continue;
                removals++;
                // the distance of u to the plane of (a, b, c) in local coordinates (u projects into the face)
                Vector3d l1 = p1 - p0, l2 = p2 - p0, lu = u - p0, ln = l1.Cross(l2).Normalized;
                double dist = Math.Abs(lu.Dot(ln));
                // (up to the absolute rounding error of the bound's computation: a few ulps of the coordinates)
                Assert.True(rr.MaxDrift >= dist - 4 * Ulp(m), $"seed {seed}: drift {rr.MaxDrift:G3}, distance {dist:G3}");
            }
            // (not vacuous: some seeds are within the bound and get removed)
            Assert.True(removals > 0, "no removal");
        }

        [Fact]
        public void UnreferencedVertices_DoNotChangeTheBound()
        {
            // (code review) the largest coordinate and the diagonal both of the triangles' vertices: a stray vertex far
            // away or at NaN changes nothing
            DMesh3 start = SplitSphere(1e-13, 2e-13), m0 = new DMesh3(start);
            var r0 = new MeshRoundingRepair(m0) { SharedPositionsTouch = true };
            Assert.True(r0.Apply());
            foreach (Vector3d stray in new[] { new Vector3d(1e12, 0, 0), new Vector3d(double.NaN, 0, 0) }) {
                DMesh3 m = new DMesh3(start);
                int v = m.AppendVertex(stray);
                var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true };
                Assert.True(r.Apply(), stray.ToString());
                Assert.Equal(r0.MaxLength, r.MaxLength);
                Assert.Equal(m0.TriangleCount, m.TriangleCount);
                Assert.Equal(r0.MaxDrift, r.MaxDrift);
                Assert.Equal(r0.Collapses + r0.Flips + r0.Moves + r0.Zips, r.Collapses + r.Flips + r.Moves + r.Zips);
                m.RemoveVertex(v, false, false);
                Assert.Equal("", Invariance.GeometricDifference(m0, m));
            }
        }

        [Fact]
        public void EmptyMesh_ResetsTheReportedValues()
        {
            // (the first run rejects an edit and leaves defects, so the values to reset are set)
            DMesh3 start = SplitSphere(1e-13, 2e-13);
            var r = new MeshRoundingRepair(new DMesh3(start)) { MaxDisplacement = 1e-15 / Diagonal(start) };
            r.Apply();
            Assert.True(r.SmallestRejectedDrift < double.PositiveInfinity);
            Assert.NotEqual(Vector3d.Zero, r.RemainingDefectAt);
            r.Mesh = new DMesh3();
            Assert.True(r.Apply());
            Assert.Equal(double.PositiveInfinity, r.SmallestRejectedDrift);
            Assert.Equal(Vector3d.Zero, r.RemainingDefectAt);
            Assert.Equal(0, r.VertexRemovals);
        }

        [Fact]
        public void FixedVertices_KeptWhereCoarse_UnlessTheCallerAllowsIt()
        {
            // FixedVertexF is public: by default it applies also where the coordinates are coarse (FixedWhereCoarse);
            // ExactMeshBoolean, whose fixed vertices are the rounded input vertices, turns that off. Over the UTM cases,
            // the vertices marked fixed move less often with it on than with it off
            int movedKept = 0, movedFree = 0;
            foreach (var (name, mesh) in UtmCases().Where(c => c.name.Contains("count 4"))) {
                var raw = new ExactMeshBoolean(new DMesh3(mesh), new DMesh3(), Ops.Union) { SafeRounding = false };
                Assert.True(raw.Compute(), name);
                var start = new Dictionary<int, Vector3d>();
                foreach (int v in raw.Result.VertexIndices())
                    if (v % 2 == 0) start[v] = raw.Result.GetVertex(v);
                foreach (bool keep in new[] { true, false }) {
                    DMesh3 m = new DMesh3(raw.Result);
                    var r = new MeshRoundingRepair(m) { SharedPositionsTouch = true, FixedVertexF = (v) => start.ContainsKey(v), FixedWhereCoarse = keep };
                    r.Apply();
                    int moved = start.Count(kv => m.IsVertex(kv.Key) && m.GetVertex(kv.Key) != kv.Value);
                    if (keep) movedKept += moved; else movedFree += moved;
                }
            }
            Assert.True(movedKept < movedFree, $"fixed vertices moved: {movedKept} kept, {movedFree} free");
        }

        [Fact]
        public void HugeCoordinates_RejectEveryEdit()
        {
            // (re-review) coordinates whose diagonal overflows (infinite ones throw in the exact predicates): the bound is
            // not finite, every edit is rejected
            DMesh3 m = SplitSphere(1e-13, 2e-13);
            int far = m.AppendVertex(new Vector3d(1e300, 0, 0));
            int a = m.AppendVertex(new Vector3d(5, 0, 0)), b = m.AppendVertex(new Vector3d(5, 1, 0));
            m.AppendTriangle(far, a, b);
            int before = m.TriangleCount;
            var r = new MeshRoundingRepair(m);
            r.Apply();
            Assert.Equal(0, r.Collapses + r.Flips + r.Moves + r.Zips + r.VertexRemovals);
            Assert.True(double.IsNaN(r.MaxLength));
        }
    }
}
