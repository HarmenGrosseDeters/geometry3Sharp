// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Certified restricted Delaunay refinement, stage 1b (mesh_ops/ExactRestrictedVoronoi.cs, mesh_ops/RestrictedDelaunayRefinement.cs;
    /// docs/design/CERTIFIED_REMESHING.md sections 8, 14.8 and 14.9). Procedural inputs only.
    /// </summary>
    public class RestrictedDelaunayRefinementTests
    {
        // ---- inputs ----

        static DMesh3 Sphere(int n, Func<Vector3d, Vector3d> f)
        {
            var m = new Sphere3Generator_NormalizedCube() { EdgeVertices = n, Radius = 1 }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) m.SetVertex(v, f(m.GetVertex(v).Normalized));
            return m;
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    int a = i * nv + j, b = ((i + 1) % nu) * nv + j, c = ((i + 1) % nu) * nv + (j + 1) % nv, d = i * nv + (j + 1) % nv;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        // two spherical caps glued at their rim: the interior crease angle is psi
        static DMesh3 Lens(double psiDeg, int nu, int nphi)
        {
            double theta = psiDeg * Math.PI / 360;
            var m = new DMesh3();
            int top = m.AppendVertex(new Vector3d(0, 0, 1 - Math.Cos(theta)));
            var rings = new List<int[]>();
            for (int k = 1; k <= 2 * nphi - 1; ++k) {
                double phi = theta * (k <= nphi ? k : 2 * nphi - k) / nphi;
                double z = (Math.Cos(phi) - Math.Cos(theta)) * (k <= nphi ? 1 : -1);
                var ring = new int[nu];
                for (int i = 0; i < nu; ++i) {
                    double u = 2 * Math.PI * (i + 0.5 * (k % 2)) / nu;
                    ring[i] = m.AppendVertex(new Vector3d(Math.Sin(phi) * Math.Cos(u), Math.Sin(phi) * Math.Sin(u), z));
                }
                rings.Add(ring);
            }
            int bottom = m.AppendVertex(new Vector3d(0, 0, -(1 - Math.Cos(theta))));
            for (int i = 0; i < nu; ++i) m.AppendTriangle(top, rings[0][i], rings[0][(i + 1) % nu]);
            for (int k = 0; k + 1 < rings.Count; ++k) {
                int[] r0 = rings[k], r1 = rings[k + 1];
                bool odd = (k + 1) % 2 == 1;
                for (int i = 0; i < nu; ++i) {
                    int i1 = (i + 1) % nu;
                    if (!odd) { m.AppendTriangle(r0[i], r1[i], r0[i1]); m.AppendTriangle(r0[i1], r1[i], r1[i1]); }
                    else { m.AppendTriangle(r0[i], r1[(i + nu - 1) % nu], r1[i]); m.AppendTriangle(r0[i], r1[i], r0[i1]); }
                }
            }
            int[] last = rings[rings.Count - 1];
            for (int i = 0; i < nu; ++i) m.AppendTriangle(bottom, last[(i + 1) % nu], last[i]);
            return m;
        }

        static DMesh3 Cube(int n)
        {
            var m = new GridBox3Generator() { EdgeVertices = n, Box = new Box3d(Vector3d.Zero, Vector3d.One) }.Generate().MakeDMesh();
            MeshEditor.RemoveFinTriangles(m);
            return m;
        }

        static DMesh3 Rotated(DMesh3 m)
        {
            var q = new Quaterniond(new Vector3d(0.3, 0.7, 0.2).Normalized, 23.7);
            foreach (int v in m.VertexIndices()) m.SetVertex(v, q * m.GetVertex(v));
            return m;
        }

        static DMesh3 Make(string name)
        {
            switch (name) {
                case "sphere": return Rotated(Sphere(12, p => p));
                case "ellipsoid": return Rotated(Sphere(12, p => new Vector3d(p.x, 0.6 * p.y, 0.4 * p.z)));
                case "bumpy": return Rotated(Sphere(16, p => p * (1 + 0.12 * Math.Sin(6 * p.x) * Math.Sin(6 * p.y) * Math.Sin(6 * p.z + 0.3))));
                case "torus": return Rotated(Torus(1, 0.3, 48, 16));
                case "lens90": return Rotated(Lens(90, 64, 10));
                case "lens270": return Rotated(Lens(270, 64, 10));
                case "lens15": return Rotated(Lens(15, 64, 10));
                case "cube": return Rotated(Cube(6));
                default: throw new ArgumentException(name);
            }
        }

        static RestrictedDelaunayRefinement Run(DMesh3 input, double rmaxOverDiag, bool check = false)
        {
            var rdr = new RestrictedDelaunayRefinement { RMax = rmaxOverDiag * input.CachedBounds.Diagonal.Length, CheckEachInsertion = check };
            rdr.Compute(input);
            return rdr;
        }

        static int Genus(DMesh3 m) => (2 - (m.VertexCount - m.EdgeCount + m.TriangleCount)) / 2;
        static int Components(DMesh3 m) { var c = new MeshConnectedComponents(m); c.FindConnectedT(); return c.Count; }

        static double MinAngleDeg(DMesh3 m)
        {
            double best = 180;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
                best = Math.Min(best, Math.Min(A, Math.Min(B, 180 - A - B)));
            }
            return best;
        }

        /// <summary> the success-path checks of section 8: closed, manifold, clean, homeomorphic, the band both ways, the angles </summary>
        static void AssertCertifiedResult(DMesh3 input, RestrictedDelaunayRefinement rdr)
        {
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.Certified, rdr.Status);
            DMesh3 D = rdr.Output;
            Assert.True(D.TriangleCount > 0);
            Assert.True(D.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(D) ?? "");
            Assert.Equal(Components(input), Components(D));
            Assert.Equal(Genus(input), Genus(D));
            Assert.True(D.VertexIndices().All(v => D.GetVertex(v).IsFinite));
            var hd = new MeshHausdorffDistance(input, D) { Tolerance = 0.01 * rdr.RMax };
            hd.Compute();
            Assert.True(hd.UpperBound <= rdr.RMax, $"Hausdorff {hd.UpperBound} > r_max {rdr.RMax}");
            Assert.Equal(0, rdr.ExemptFacets);
            Assert.True(MinAngleDeg(D) >= 30 - 1e-9, $"min angle {MinAngleDeg(D)}");
            Assert.True(rdr.Diagram.Side2Calls > 0 && rdr.Diagram.Side3Calls > 0);
            Assert.Equal(rdr.RestrictedFacets, D.TriangleCount);
        }

        // ---- the success paths ----

        [Theory]
        [InlineData("sphere")]
        [InlineData("ellipsoid")]
        [InlineData("bumpy")]
        [InlineData("torus")]
        public void SmoothSet_IsCertified_AndTheResultChecksHold(string name)
        {
            DMesh3 input = Make(name);
            AssertCertifiedResult(input, Run(input, 1.0 / 20));
        }

        [Theory]
        [InlineData("lens90")]
        [InlineData("lens270")]
        [InlineData("cube")]
        public void CreasesAtLeast60Degrees_AreCertified_AndTheResultChecksHold(string name)
        {
            DMesh3 input = Make(name);
            AssertCertifiedResult(input, Run(input, 1.0 / 12));
        }

        [Fact]
        public void SharpLens_IsNotCertified()
        {
            // a 15 degree crease: the points pile up at it until h_min refuses them (no feature protection in stage 1)
            DMesh3 input = Make("lens15");
            var rdr = Run(input, 1.0 / 12);
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.Uncertified, rdr.Status);
            // every clause is violated somewhere at the end
            Assert.True(rdr.BadEdges > 0 && rdr.BadArcs > 0 && rdr.BadCells > 0, $"E' {rdr.BadEdges}, F' {rdr.BadArcs}, C' {rdr.BadCells}");
            Assert.True(rdr.Refused > 0);
        }

        // ---- the incremental cells (14.9 Lemma U) ----

        [Theory]
        [InlineData("sphere")]
        [InlineData("torus")]
        [InlineData("cube")]
        public void IncrementalCells_EqualAFullRecompute(string name)
        {
            // after every insertion: the cells kept by the cut update equal the cells computed from scratch (same pieces, same
            // symbolic points and labels, same (E') counts)
            DMesh3 input = Make(name);
            var rdr = Run(input, name == "cube" ? 1.0 / 6 : 1.0 / 8, check: true);
            Assert.True(rdr.CheckedInsertions > 20, rdr.CheckedInsertions.ToString());
            Assert.Equal(0, rdr.CheckMismatches);
        }

        [Theory]
        [InlineData("sphere")]
        [InlineData("cube")]
        public void Pieces_PartitionTheSurface(string name)
        {
            // every triangle is covered once by the pieces of its cells (no piece lost or doubled by the skip radius or the cut),
            // and every kind-3 point appears in the cells of all three of its sites (14.9 Lemma F)
            DMesh3 input = Make(name);
            var rdr = Run(input, 1.0 / 10);
            var rvd = rdr.Diagram;
            var area = new Dictionary<int, double>();
            var keys = new Dictionary<(int, int, int, int), int>();
            for (int i = 0; i < rvd.SiteCount; ++i)
                foreach (var pc in rvd.Cell(i)) {
                    double a = 0;
                    for (int k = 1; k + 1 < pc.V.Length; ++k) a += 0.5 * (pc.V[k].P - pc.V[0].P).Cross(pc.V[k + 1].P - pc.V[0].P).Length;
                    area[pc.T] = area.GetValueOrDefault(pc.T) + a;
                    foreach (var q in pc.V)
                        if (q.Kind == 3) {
                            int[] s = new[] { i, q.A, q.B }.OrderBy(x => x).ToArray();
                            var key = (pc.T, s[0], s[1], s[2]);
                            keys[key] = keys.GetValueOrDefault(key) + 1;
                        }
                }
            foreach (int t in input.TriangleIndices())
                Assert.True(Math.Abs(area.GetValueOrDefault(t) - input.GetTriArea(t)) <= 1e-9 * input.GetTriArea(t) + 1e-15, $"triangle {t}: {area.GetValueOrDefault(t)} of {input.GetTriArea(t)}");
            Assert.NotEmpty(keys);
            Assert.All(keys.Values, n => Assert.Equal(3, n));
        }

        // ---- the TBP counterexamples, one clause each (Lemma 1 and 14.9 Lemma K) ----

        static ExactRestrictedVoronoi Diagram(DMesh3 S, params Vector3d[] sites)
        {
            var box = S.CachedBounds; box.Expand(box.Diagonal.Length);
            foreach (var p in sites) box.Contain(p);
            var dt = new Delaunay3(box) { SymbolicPerturbation = true, RefinementSupport = true };
            foreach (var p in sites) Assert.Equal(Delaunay3.InsertResult.Inserted, dt.Insert(p, -1, 0, out _));
            var rvd = new ExactRestrictedVoronoi(S, dt, 100);
            rvd.ComputeAll();
            return rvd;
        }

        [Fact]
        public void EdgeClause_AVoronoiEdgeCrossingAThinSlabTwice_IsCounted()
        {
            // the Voronoi edge of three sites in the plane z = 0 is the z axis; it crosses the slab's top and bottom faces
            var slab = new GridBox3Generator() { EdgeVertices = 4, Box = new Box3d(Vector3d.Zero, new Vector3d(2, 2, 0.05)) }.Generate().MakeDMesh();
            MeshEditor.RemoveFinTriangles(slab);
            var rvd = Diagram(slab, new Vector3d(1, 0.1, 0), new Vector3d(-1, 0.2, 0), new Vector3d(0.1, 1, 0), new Vector3d(0, -1.1, 0.01));
            int worst = 0;
            for (int a = 8; a < rvd.SiteCount; ++a)
                for (int b = a + 1; b < rvd.SiteCount; ++b)
                    for (int c = b + 1; c < rvd.SiteCount; ++c) worst = Math.Max(worst, rvd.FacetPointCount(a, b, c));
            Assert.Equal(2, worst);
        }

        [Fact]
        public void ArcClause_AFacetMeetingTheSurfaceInACircle_IsRefused()
        {
            // two sites at the poles of a sphere: each cell is a hemisphere (a disk), their facet meets the sphere in the equator
            DMesh3 S = Rotated(Sphere(8, p => p));
            var rvd = Diagram(S, new Vector3d(0, 0, 0.5), new Vector3d(0, 0, -0.5));
            var s8 = rvd.Summary(8); var s9 = rvd.Summary(9);
            Assert.True(s8.Disk && s9.Disk);
            Assert.False(rvd.IsArc(8, 9));
            Assert.Equal(1, s8.BadArcs);
            Assert.Equal(1, s9.BadArcs);
        }

        [Fact]
        public void ArcClause_OneArc_IsAccepted()
        {
            // the same sphere with a third site: the facets of the three cells meet the sphere in arcs
            DMesh3 S = Rotated(Sphere(8, p => p));
            var rvd = Diagram(S, new Vector3d(0, 0, 0.5), new Vector3d(0.45, 0, -0.25), new Vector3d(-0.4, 0.2, -0.25));
            for (int i = 8; i < 11; ++i) {
                var s = rvd.Summary(i);
                Assert.True(s.Disk, $"site {i}: chi {s.Chi}, components {s.Components}");
                Assert.Equal(0, s.BadArcs);
            }
        }

        [Fact]
        public void CellClause_ACellHoldingTwoComponents_IsRefused()
        {
            // two spheres, one site between them: its cell holds both (two components, chi 4); the far site's cell is empty
            DMesh3 S = Sphere(8, p => p + new Vector3d(-2, 0, 0));
            new MeshEditor(S).AppendMesh(Sphere(8, p => p + new Vector3d(2, 0, 0)));
            var rvd = Diagram(S, Vector3d.Zero, new Vector3d(0, 6, 0));
            var s = rvd.Summary(8);
            Assert.Equal(2, s.Components);
            Assert.Equal(4, s.Chi);
            Assert.False(s.Disk);
            Assert.Empty(rvd.Cell(9));
        }

        [Fact]
        public void CellClause_ADiskAndAnAnnulus_IsRefused()
        {
            // a sphere and a torus in the plane z = 0; two sites above and below the sphere's centre: the upper cell holds the
            // upper hemisphere (a disk, chi 1) and the upper half of the torus (an annulus, chi 0): chi 1, but two components
            DMesh3 S = Sphere(8, p => p + new Vector3d(-3, 0, 0));
            DMesh3 T = Torus(1, 0.3, 36, 12);
            MeshTransforms.Translate(T, new Vector3d(3, 0, 0));
            new MeshEditor(S).AppendMesh(T);
            var rvd = Diagram(S, new Vector3d(-3, 0.01, 0.5), new Vector3d(-3, -0.01, -0.5));
            foreach (int i in new[] { 8, 9 }) {
                var s = rvd.Summary(i);
                Assert.Equal(1, s.Chi);
                Assert.Equal(2, s.Components);
                Assert.False(s.Disk);
            }
        }

        [Fact]
        public void FacetRules_LeaveNoEdgeClauseViolation()
        {
            // the facet rules alone ((E'), R2 of the balls, R3) already remove every Voronoi edge that meets S twice
            // (on a sphere: its initial sample already has restricted facets; a torus's first cells can be bands without any)
            DMesh3 input = Make("sphere");
            var rdr = new RestrictedDelaunayRefinement { RMax = input.CachedBounds.Diagonal.Length / 12, FacetRulesOnly = true };
            rdr.Compute(input);
            Assert.True(rdr.RestrictedFacets > 0);
            Assert.Equal(0, rdr.BadEdges);
            Assert.Equal(0, rdr.FarBalls);
            Assert.Equal(0, rdr.ExemptFacets);
        }

        [Fact]
        public void RadiusEdgeRule_GivesTheAngleBound_WhenTheSizeRuleDoesNot()
        {
            // a thin ellipsoid at a coarse r_max: the farthest-point rules leave a facet below 30 degrees that R3 fixes (without it,
            // rho huge, 29.0 degrees; mostly R2's farthest points already give the angles, so R3 rarely acts)
            DMesh3 input = Rotated(Sphere(12, p => new Vector3d(p.x, 0.3 * p.y, 0.15 * p.z)));
            double rmax = input.CachedBounds.Diagonal.Length / 4;
            var good = new RestrictedDelaunayRefinement { RMax = rmax };
            good.Compute(input);
            var loose = new RestrictedDelaunayRefinement { RMax = rmax, Rho = 1e6 };
            loose.Compute(input);
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.Certified, good.Status);
            Assert.Equal(0, good.ExemptFacets);
            Assert.True(MinAngleDeg(good.Output) >= 30 - 1e-9, MinAngleDeg(good.Output).ToString());
            Assert.True(MinAngleDeg(loose.Output) < 30, MinAngleDeg(loose.Output).ToString());
        }

        [Fact]
        public void CellClause_AnAnnulus_IsRefused()
        {
            // a torus with three sites on its centre circle: each cell is a tube sector (one component, chi 0)
            DMesh3 S = Torus(1, 0.3, 36, 12);
            var rvd = Diagram(S, Enumerable.Range(0, 3).Select(k => new Vector3d(Math.Cos(2 * Math.PI * k / 3 + 0.1), Math.Sin(2 * Math.PI * k / 3 + 0.1), 0)).ToArray());
            for (int i = 8; i < 11; ++i) {
                var s = rvd.Summary(i);
                Assert.Equal(1, s.Components);
                Assert.Equal(0, s.Chi);
                Assert.False(s.Disk);
            }
        }

        [Fact]
        public void CornerClause_ACornerCellMeetingTheSurface_IsCounted()
        {
            // no sample yet: the box corners' cells cover the surface (R0); one sample at the centre takes it all
            DMesh3 S = Rotated(Sphere(6, p => p));
            var rvd = Diagram(S);
            Assert.Equal(8, Enumerable.Range(0, 8).Count(c => rvd.Cell(c).Count > 0));
            var one = Diagram(S, new Vector3d(0.01, 0.02, 0.03));
            Assert.Equal(0, Enumerable.Range(0, 8).Count(c => one.Cell(c).Count > 0));
            Assert.True(one.Summary(8).Chi == 2 && one.Summary(8).Components == 1);
        }

        [Fact]
        public void FarClause_PointsBeyondRMax_AreFlagged()
        {
            DMesh3 S = Rotated(Sphere(6, p => p));
            var box = S.CachedBounds; box.Expand(box.Diagonal.Length);
            var dt = new Delaunay3(box) { SymbolicPerturbation = true, RefinementSupport = true };
            dt.Insert(new Vector3d(0, 0, 0.9), -1, 0, out _);
            dt.Insert(new Vector3d(0, 0, -0.9), -1, 0, out _);
            foreach (double rmax in new[] { 0.5, 3.0 }) {
                var rvd = new ExactRestrictedVoronoi(S, dt, rmax);
                rvd.ComputeAll();
                Assert.Equal(rmax < 1, rvd.Summary(8).AnyFar);
            }
        }

        // ---- invariance, reuse, determinism ----

        static (Vector3d[] v, Index3i[] t) Bits(DMesh3 m) =>
            (m.VertexIndices().Select(m.GetVertex).ToArray(), m.TriangleIndices().Select(m.GetTriangle).ToArray());

        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Invariance_OverSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 input = Make("torus");
            var baseRun = Run(input, 1.0 / 10);
            DMesh3 moved = new DMesh3(input);
            foreach (int v in moved.VertexIndices()) moved.SetVertex(v, sim.Apply(moved.GetVertex(v)));
            var rdr = new RestrictedDelaunayRefinement { RMax = sim.ApplyLength(baseRun.RMax) };
            rdr.Compute(moved);
            Assert.Equal(baseRun.Status, rdr.Status);
            Assert.Equal(Genus(baseRun.Output), Genus(rdr.Output));
            if (sim.IsExact) {
                // the 2^k scales: every decision is an exact homogeneous sign, every construction scales exactly
                var (bv, bt) = Bits(baseRun.Output);
                var (mv, mt) = Bits(rdr.Output);
                Assert.Equal(bt, mt);
                Assert.Equal(bv.Select(sim.Apply).ToArray(), mv);
            } else {
                Assert.InRange(rdr.Output.VertexCount, 0.8 * baseRun.Output.VertexCount, 1.25 * baseRun.Output.VertexCount);
            }
        }

        [Fact]
        public void Reuse_ComputeTwice_SameBits()
        {
            DMesh3 input = Make("sphere");
            var rdr = new RestrictedDelaunayRefinement { RMax = input.CachedBounds.Diagonal.Length / 10 };
            rdr.Compute(input);
            var first = Bits(rdr.Output);
            int inserted = rdr.Inserted;
            rdr.Compute(input);
            var second = Bits(rdr.Output);
            Assert.Equal(first.v, second.v);
            Assert.Equal(first.t, second.t);
            Assert.Equal(inserted, rdr.Inserted);
        }

        [Fact]
        public void RepeatedRuns_BitIdentical()
        {
            DMesh3 input = Make("bumpy");
            var a = Bits(Run(input, 1.0 / 12).Output);
            for (int k = 0; k < 2; ++k) {
                var b = Bits(Run(input, 1.0 / 12).Output);
                Assert.Equal(a.v, b.v);
                Assert.Equal(a.t, b.t);
            }
        }

        // ---- input checks ----

        [Fact]
        public void InvalidInputs_AreRefused()
        {
            DMesh3 open = Rotated(Sphere(6, p => p));
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.InvalidInput, Run(open, 0.1).Status);
            DMesh3 good = Rotated(Sphere(6, p => p));
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.InvalidInput, new RestrictedDelaunayRefinement { RMax = 0 }.Let(r => { r.Compute(good); return r.Status; }));
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.InvalidInput, new RestrictedDelaunayRefinement { RMax = 0.1, Rho = -1 }.Let(r => { r.Compute(good); return r.Status; }));
            Assert.Throws<ArgumentException>(() => new ExactRestrictedVoronoi(good, new Delaunay3(good.CachedBounds), 1));
        }

        [Fact]
        public void SampleLimit_IsReported()
        {
            DMesh3 input = Make("sphere");
            var rdr = new RestrictedDelaunayRefinement { RMax = input.CachedBounds.Diagonal.Length / 20, MaxSamples = 40 };
            rdr.Compute(input);
            Assert.Equal(RestrictedDelaunayRefinement.ResultStatus.LimitReached, rdr.Status);
        }
    }
}
