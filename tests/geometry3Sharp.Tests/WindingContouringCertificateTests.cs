using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using static geometry3Sharp.Tests.ContouringTopologyCertificateTests;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The contouring's topology certificate end to end: MeshWindingNumberSolidify.ComputeTopologyCertificate on clean
    /// closed meshes of known topology. The acceptance criterion: never certified on a wrong result (docs/design/
    /// CONTOURING_TOPOLOGY.md, sections 2.1 and 4.6); certified results of generic inputs; the outputs and the statuses.
    /// </summary>
    public class WindingContouringCertificateTests
    {
        static readonly Matrix3d Tilt = Matrix3d.AxisAngleD(Vector3d.AxisZ, 23) * Matrix3d.AxisAngleD(Vector3d.AxisX, 11);
        static readonly Vector3d P0 = new Vector3d(-0.6113, -0.5871, -0.4129);

        static MeshWindingNumberSolidify Solidify(DMesh3 mesh, int res, bool post = false, int refineLevels = 2, double cellSize = 0)
        {
            var s = new MeshWindingNumberSolidify(mesh) {
                Adaptive = true, GridResolution = res, CellSize = cellSize, ComputeTopologyCertificate = true, CleanupResult = post,
                ReduceTolerance = post ? 0.05 : 0, MaxRefineLevels = refineLevels
            };
            Assert.True(s.Compute());
            return s;
        }

        // components and the sorted genus of each
        internal static string Topology(DMesh3 m)
        {
            var t = MeshTopologySummary.Compute(m);
            return t.Components + ":" + string.Join(",", t.ComponentList.Select(c => c.Genus).OrderBy(g => g));
        }

        // the truth of a clean input: the topology of its level-set components
        internal static string Truth(DMesh3 mesh)
        {
            DMesh3 L = ContouringLevelSet.CleanCopy(mesh);
            int comps, lcomps;
            bool[] inL = ContouringLevelSet.Classify(L, out comps, out lcomps);
            var level = new DMesh3();
            var cc = new MeshConnectedComponents(L); cc.FindConnectedT();
            foreach (var comp in cc.Components)
                if (inL[comp.Indices[0]]) new MeshEditor(level).AppendMesh(new DSubmesh3(L, comp.Indices).SubMesh);
            return Topology(level);
        }

        static void AssertCertifiedIsRight(MeshWindingNumberSolidify s, string truth, string what)
        {
            if (s.ContourTopologyCertified)
                Assert.True(Topology(s.Result) == truth, what + ": certified, but " + Topology(s.Result) + " != " + truth);
            Assert.Equal(s.ContourTopologyCertified, s.UncertifiedCells.Count == 0 && s.TopologyCertificateStatus == ContouringCertificateStatus.Certified);
        }

        [Fact]
        public void Polycubes_AreCertified()
        {
            // the CAD-like solids: certified, and the result is the contour
            foreach (DMesh3 m in new[] {
                PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0),
                PolyCube(5, 5, 2, (x, y, z) => !(x >= 1 && x <= 3 && y >= 1 && y <= 3), 0.25, P0),
                PolyCube(5, 5, 3, (x, y, z) => !(x >= 2 && y >= 2), 0.25, P0) }) {
                MeshWindingNumberSolidify s = Solidify(m, 48);
                Assert.Equal(ContouringCertificateStatus.Certified, s.TopologyCertificateStatus);
                Assert.True(s.TopologyCertified);
                Assert.True(s.ContourTopologyCertified);
                Assert.Empty(s.UncertifiedCells);
                Assert.Equal(Truth(m), Topology(s.Result));
                Assert.Equal("", MeshRobustRepair.NotCleanReason(s.Result));
            }
        }

        [Fact]
        public void PostProcessing_IsNotCovered()
        {
            // with the cleanup and the reduction the contour is certified, the result not
            DMesh3 ring = PolyCube(5, 5, 2, (x, y, z) => !(x >= 1 && x <= 3 && y >= 1 && y <= 3), 0.25, P0);
            MeshWindingNumberSolidify s = Solidify(ring, 48, true);
            Assert.True(s.ContourTopologyCertified);
            Assert.False(s.TopologyCertified);
            Assert.Equal(ContouringCertificateStatus.PostProcessing, s.TopologyCertificateStatus);
            Assert.Empty(s.UncertifiedCells);
        }

        [Fact]
        public void Statuses_NotComputed_NotSupported_NotClean()
        {
            DMesh3 box = PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0);
            var off = new MeshWindingNumberSolidify(box) { Adaptive = true, GridResolution = 32 };
            Assert.True(off.Compute());
            Assert.Equal(ContouringCertificateStatus.NotComputed, off.TopologyCertificateStatus);
            Assert.False(off.TopologyCertified);
            // the dense grid and marching cubes are not covered
            foreach (var m in new[] { MeshWindingNumberSolidify.Methods.DualContouring, MeshWindingNumberSolidify.Methods.MarchingCubes }) {
                var dense = new MeshWindingNumberSolidify(box) { Adaptive = false, Method = m, GridResolution = 32, ComputeTopologyCertificate = true };
                Assert.True(dense.Compute());
                Assert.Equal(ContouringCertificateStatus.NotSupported, dense.TopologyCertificateStatus);
                Assert.False(dense.TopologyCertified);
            }
            // an open input: its level set is not the input's surface
            var open = new DMesh3(box); open.RemoveTriangle(0, false, false);
            MeshWindingNumberSolidify o = Solidify(open, 32);
            Assert.Equal(ContouringCertificateStatus.InputNotClean, o.TopologyCertificateStatus);
            Assert.False(o.TopologyCertified || o.ContourTopologyCertified);
        }

        // the feature-width families of the design note's section 2.1 as closed meshes of known topology: a torus tube, a
        // gap between two balls, a hollow ball's wall, the width given in cells of the grid (diagonal / res)
        static IEnumerable<(string name, DMesh3 mesh)> Families(double cell, double width, int seed)
        {
            var rnd = new Random(seed);
            Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisY, rnd.NextDouble() * 360);
            Vector3d c = new Vector3d(rnd.NextDouble(), rnd.NextDouble(), rnd.NextDouble()) * 0.01;
            double w = width * cell;
            yield return ("torus", Torus(c, 0.6, w / 2, 96, Math.Max(8, (int)(24 * w / 0.2)), R));
            yield return ("gap", Union(Sphere(c - new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24), Sphere(c + new Vector3d(0.45 + w / 2, 0, 0), 0.45, 24)));
            yield return ("shell", Union(Sphere(c, 0.8, 32), Reversed(Sphere(c, 0.8 - w, 32))));
        }

        [Fact]
        public void FeatureFamilies_NeverCertifiedWrong()
        {
            // thin features break the topology (section 2.1); the certificate must not certify a wrong result. Without the
            // refinement for hidden gaps (that is stage 2), cell 0.06
            int wrong = 0;
            const double cell = 0.06;
            foreach (double width in new[] { 0.5, 1, 3 })
                for (int seed = 0; seed < 1; ++seed) {
                    foreach (var f in Families(cell, width, seed)) {
                        string truth = Truth(f.mesh);
                        MeshWindingNumberSolidify s = Solidify(f.mesh, 0, false, 0, cell);
                        string got = Topology(s.Result);
                        if (got != truth) wrong++;
                        // a thick feature: right, and refused only where a grid edge is nearly tangent to the surface (a few
                        // cells at the extreme points of the curved shapes; refining them is stage 2)
                        if (width >= 3) { Assert.Equal(truth, got); Assert.InRange(s.UncertifiedCells.Count, 0, 40); }
                        AssertCertifiedIsRight(s, truth, f.name + " width " + width + " seed " + seed);
                    }
                }
            Assert.True(wrong >= 3, "too few wrong results: the test does not exercise the certificate");
        }

        [Fact]
        public void Fuzz_CertifiedMeansTheRightTopology()
        {
            // random scenes of disjoint closed parts (balls, tori, polycubes, nested shells, inside-out parts) at random
            // resolutions: certified implies the components and genus of the level set
            var rnd = new Random(11);
            int certified = 0, runs = 0;
            for (int it = 0; it < 8; ++it) {
                var parts = new List<DMesh3>();
                int n = 1 + rnd.Next(3);
                for (int k = 0; k < n; ++k) {
                    Vector3d c = new Vector3d(1.3 * k + 0.01 * rnd.NextDouble(), 0.01 * rnd.NextDouble(), 0.01 * rnd.NextDouble());
                    Matrix3d R = Matrix3d.AxisAngleD(Vector3d.AxisX, rnd.NextDouble() * 360) * Matrix3d.AxisAngleD(Vector3d.AxisZ, rnd.NextDouble() * 360);
                    switch (rnd.Next(5)) {
                        case 0: parts.Add(Sphere(c, 0.3 + 0.2 * rnd.NextDouble(), 16)); break;
                        case 1: parts.Add(Torus(c, 0.4, 0.05 + 0.1 * rnd.NextDouble(), 48, 16, R)); break;
                        case 2: parts.Add(PolyCube(3, 3, 2, (x, y, z) => !(x == 1 && y == 1), 0.3, c - new Vector3d(0.45, 0.45, 0.3))); break;
                        case 3: parts.Add(Union(Sphere(c, 0.5, 20), Reversed(Sphere(c, 0.5 - 0.02 - 0.1 * rnd.NextDouble(), 16)))); break;
                        default: parts.Add(Reversed(Sphere(c, 0.3, 12))); break;     // inside out: not part of the level set
                    }
                }
                DMesh3 m = Union(parts.ToArray());
                if (ContouringLevelSet.CleanCopy(m) == null) continue;
                string truth = Truth(m);
                MeshWindingNumberSolidify s = Solidify(m, 24 + rnd.Next(24), false, rnd.Next(2) * 2);
                runs++;
                if (s.ContourTopologyCertified) certified++;
                AssertCertifiedIsRight(s, truth, "scene " + it);
            }
            Assert.True(runs >= 6);
            Assert.True(certified > 0);
        }

        [Fact]
        public void UncertifiedCells_AreInTheInputsFrame()
        {
            // the cells (boxes in the input's coordinates) cover the place of the thin feature: a torus tube half a cell wide
            DMesh3 t = Torus(Vector3d.Zero, 0.6, 0.0125, 96, 8, Tilt);
            MeshWindingNumberSolidify s = Solidify(t, 40);
            Assert.False(s.ContourTopologyCertified);
            Assert.NotEmpty(s.UncertifiedCells);
            double cell = t.CachedBounds.DiagonalLength / 40;
            foreach (ContouringUncertifiedCell c in s.UncertifiedCells) {
                Assert.InRange(c.Box.MaxDim, 0.99 * s.UsedCellSize, 1.01 * cell);
                // within a cell or two of the tube's centre circle
                Vector3d q = Tilt.Transpose() * c.Box.Center;
                Assert.True(Math.Abs(Math.Sqrt(q.x * q.x + q.y * q.y) - 0.6) < 3 * cell && Math.Abs(q.z) < 3 * cell);
            }
        }

        [Fact]
        public void MeshSide_RefusalsReachTheOutput()
        {
            // two slabs 0.0117 apart (a hidden gap): refined around the gap, a two-level grid with face vertices at the
            // transitions; certified as it is, and with the face vertices forced to fail (DualContouring's test seam) the
            // cells of the polygons that needed them are reported as MeshSide
            DMesh3 m = Union(PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0), PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0 + new Vector3d(0.0031, 0.0047, 0.2617)));
            MeshWindingNumberSolidify ok = Solidify(m, 40);
            Assert.True(ok.RefineFactor > 1);
            Assert.Equal(ContouringCertificateStatus.Certified, ok.TopologyCertificateStatus);
            Assert.Equal(Truth(m), Topology(ok.Result));
            int calls = 0;
            DualContouring.FailFaceVertexForTests = () => { calls++; return calls % 2 == 0; };
            MeshWindingNumberSolidify s;
            try { s = Solidify(m, 40); } finally { DualContouring.FailFaceVertexForTests = null; }
            Assert.True(calls > 0);
            Assert.False(s.ContourTopologyCertified);
            Assert.Contains(s.UncertifiedCells, c => c.Reason == ContouringCertificateReason.MeshSide);
            // the whole contour, where the self-intersections were not resolved (DualContouring's other test seam)
            DualContouring.SkipResolveForTests = true;
            try { s = Solidify(m, 40); } finally { DualContouring.SkipResolveForTests = false; }
            Assert.NotEmpty(s.UncertifiedCells);
            Assert.All(s.UncertifiedCells.Where(c => c.Reason != ContouringCertificateReason.MeshSide), c => Assert.NotEqual(ContouringCertificateReason.None, c.Reason));
            Assert.True(s.UncertifiedCells.Count(c => c.Reason == ContouringCertificateReason.MeshSide) > 100);
        }

        [Fact]
        public void Bubbles_TheContourMisses_AreRefused()
        {
            // closed parts thinner than a cell that no grid edge crosses: the contour misses them (a wrong result), the
            // certificate finds them through the cells their triangles meet. Away from the contour (an unrefined cell), and
            // inside a refined region (a cavity in a slab next to a hidden gap: the fine cells of a refined bin)
            DMesh3 box = PolyCube(4, 3, 2, (x, y, z) => true, 0.25, P0);
            DMesh3 m = Union(box, Sphere(P0 + new Vector3d(1.4013, 0.3711, 0.2517), 0.004, 6));
            MeshWindingNumberSolidify s = Solidify(m, 40, false, 0);
            Assert.NotEqual(Truth(m), Topology(s.Result));
            Assert.False(s.ContourTopologyCertified);
            Assert.Contains(s.UncertifiedCells, c => c.Reason == ContouringCertificateReason.Cell);

            DMesh3 slabs = Union(PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0), PolyCube(4, 4, 1, (x, y, z) => true, 0.25, P0 + new Vector3d(0.0031, 0.0047, 0.2617)),
                Reversed(Sphere(P0 + new Vector3d(0.5013, 0.4987, 0.2441), 0.0015, 6)));
            Assert.NotNull(ContouringLevelSet.CleanCopy(slabs));
            MeshWindingNumberSolidify r = Solidify(slabs, 40);
            Assert.True(r.RefineFactor > 1);
            Assert.NotEqual(Truth(slabs), Topology(r.Result));
            Assert.False(r.ContourTopologyCertified);
            // the cavity's cells are fine ones (it lies across a fine face: a closed curve there)
            Assert.Contains(r.UncertifiedCells, c => (c.Reason == ContouringCertificateReason.Cell || c.Reason == ContouringCertificateReason.Face)
                && c.Box.MaxDim < 0.5 * r.UsedCellSize * r.RefineFactor);
        }

        [Fact]
        public void Invariance_Similarities()
        {
            // the 2^k scales: the same status and cells, scaled, bit for bit (the certificate is combinatorial and the
            // solidify runs in a frame scaled to unit size); the others: certified implies the right topology
            DMesh3 ring = PolyCube(5, 5, 2, (x, y, z) => !(x >= 1 && x <= 3 && y >= 1 && y <= 3), 0.25, P0);
            DMesh3 thin = Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.02, 96, 8, Tilt);
            foreach (DMesh3 m in new[] { ring, thin }) {
                MeshWindingNumberSolidify b = Solidify(m, 32);
                string truth = Truth(m);
                foreach (Similarity sim in Similarity.All()) {
                    DMesh3 mm = new DMesh3(m);
                    MeshTransforms.PerVertexTransform(mm, sim.Apply);
                    MeshWindingNumberSolidify s = Solidify(mm, 32);
                    AssertCertifiedIsRight(s, truth, sim.Name);
                    if (!sim.IsExact) continue;
                    Assert.Equal(b.TopologyCertificateStatus, s.TopologyCertificateStatus);
                    Assert.Equal(b.UncertifiedCells.Count, s.UncertifiedCells.Count);
                    for (int i = 0; i < b.UncertifiedCells.Count; ++i) {
                        Assert.Equal(b.UncertifiedCells[i].Reason, s.UncertifiedCells[i].Reason);
                        Assert.Equal(sim.Scale * b.UncertifiedCells[i].Box.Min, s.UncertifiedCells[i].Box.Min);
                        Assert.Equal(sim.Scale * b.UncertifiedCells[i].Box.Max, s.UncertifiedCells[i].Box.Max);
                    }
                }
            }
        }

        [Fact]
        public void Compute_Twice_GivesTheSameCertificate()
        {
            DMesh3 thin = Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.02, 96, 8, Tilt);
            var s = new MeshWindingNumberSolidify(thin) { Adaptive = true, GridResolution = 40, ComputeTopologyCertificate = true, CleanupResult = false, ReduceTolerance = 0 };
            Assert.True(s.Compute());
            var status = s.TopologyCertificateStatus;
            var cells = s.UncertifiedCells.ToList();
            Assert.NotEmpty(cells);
            Assert.True(s.Compute());
            Assert.Equal(status, s.TopologyCertificateStatus);
            Assert.Equal(cells.Count, s.UncertifiedCells.Count);
            for (int i = 0; i < cells.Count; ++i) {
                Assert.Equal(cells[i].Reason, s.UncertifiedCells[i].Reason);
                Assert.Equal(cells[i].Box.Min, s.UncertifiedCells[i].Box.Min);
            }
            // switched off: no stale result
            s.ComputeTopologyCertificate = false;
            Assert.True(s.Compute());
            Assert.Equal(ContouringCertificateStatus.NotComputed, s.TopologyCertificateStatus);
            Assert.Empty(s.UncertifiedCells);
        }

        [Fact]
        public void Certificate_DoesNotChangeTheResult()
        {
            DMesh3 thin = Torus(new Vector3d(0.01, 0.02, 0.03), 0.6, 0.05, 96, 12, Tilt);
            var a = new MeshWindingNumberSolidify(thin) { Adaptive = true, GridResolution = 40 };
            var b = new MeshWindingNumberSolidify(thin) { Adaptive = true, GridResolution = 40, ComputeTopologyCertificate = true };
            Assert.True(a.Compute()); Assert.True(b.Compute());
            Assert.Equal(a.Result.VertexCount, b.Result.VertexCount);
            Assert.Equal(a.Result.TriangleCount, b.Result.TriangleCount);
            foreach (int v in a.Result.VertexIndices()) Assert.Equal(a.Result.GetVertex(v), b.Result.GetVertex(v));
        }
    }
}
