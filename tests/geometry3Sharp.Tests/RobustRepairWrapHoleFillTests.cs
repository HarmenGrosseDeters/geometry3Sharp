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
    /// #1575 / #1542 option 1: the hole fill of MeshRobustRepair before the alpha wrap (RobustRepairWrapHoleFill) and the thin-shell guard after
    /// it. Procedural inputs only: spheres (Sphere3Generator_NormalizedCube) with triangles removed, a flat plate, and the repro of the issue
    /// (RepairSoups.Issue). Internal members: these tests are not run against the base library (the public ones are: RobustRepairHollowWrapTests).
    /// </summary>
    public class RobustRepairWrapHoleFillTests
    {
        readonly ITestOutputHelper output;
        public RobustRepairWrapHoleFillTests(ITestOutputHelper output) { this.output = output; }

        static DMesh3 Sphere(int edgeVertices)
        {
            return new Sphere3Generator_NormalizedCube() { Radius = 0.5, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
        }

        // the triangles whose centroid is within radius of p are removed: a hole there
        static DMesh3 HoleAt(DMesh3 m, Vector3d p, double radius)
        {
            DMesh3 r = new DMesh3(m);
            // (at least the nearest triangle)
            int nearest = m.TriangleIndices().OrderBy(t => m.GetTriCentroid(t).Distance(p)).First();
            r.RemoveTriangle(nearest, true);
            foreach (int tid in m.TriangleIndices().ToList())
                if (m.GetTriCentroid(tid).Distance(p) <= radius)
                    r.RemoveTriangle(tid, true);
            return r;
        }

        static double LongestLoop(DMesh3 m)
        {
            var loops = new MeshBoundaryLoops(m);
            double best = 0;
            foreach (EdgeLoop l in loops) {
                double len = 0;
                for (int i = 0; i < l.VertexCount; ++i)
                    len += l.GetVertex(i).Distance(l.GetVertex((i + 1) % l.VertexCount));
                best = Math.Max(best, len);
            }
            return best;
        }

        static double MedianEdge(DMesh3 m)
        {
            var e = m.EdgeIndices().Select(eid => { Index2i v = m.GetEdgeV(eid); return m.GetVertex(v.a).Distance(m.GetVertex(v.b)); }).OrderBy(x => x).ToList();
            return e[e.Count / 2];
        }

        static ulong Hash(DMesh3 m)
        {
            ulong h = 14695981039346656037UL;
            foreach (int vid in m.VertexIndices()) {
                Vector3d v = m.GetVertex(vid);
                foreach (double d in new[] { v.x, v.y, v.z }) { h ^= (ulong)BitConverter.DoubleToInt64Bits(d); h *= 1099511628211UL; }
            }
            foreach (int tid in m.TriangleIndices()) {
                Index3i t = m.GetTriangle(tid);
                foreach (int i in new[] { t.a, t.b, t.c }) { h ^= (ulong)i; h *= 1099511628211UL; }
            }
            return h;
        }

        static DMesh3 Fill(DMesh3 m, double alpha, double k, out int filled, out int candidates)
        {
            return RobustRepairWrapHoleFill.Fill(m, alpha, k, null, out filled, out candidates);
        }

        [Fact]
        public void ClosedMesh_IsNotCopied()
        {
            DMesh3 m = Sphere(8);
            Assert.Null(Fill(m, 0.01, 64, out int filled, out int cand));
            Assert.Equal(0, filled);
            Assert.Equal(0, cand);
        }

        [Fact]
        public void SmallHoles_AreFilled_ClosedManifold_InputUnchanged()
        {
            DMesh3 m = Sphere(12);
            m = HoleAt(m, new Vector3d(0, 0, 0.5), 0.03);
            m = HoleAt(m, new Vector3d(0.5, 0, 0), 0.04);
            m = HoleAt(m, new Vector3d(0, -0.5, 0), 0.01);
            int loops = new MeshBoundaryLoops(m).Count;
            Assert.True(loops >= 3);
            ulong before = Hash(m);
            int tris = m.TriangleCount;
            DMesh3 f = Fill(m, 0.01, 64, out int filled, out int cand);
            Assert.NotNull(f);
            Assert.Equal(loops, filled);
            Assert.Equal(loops, cand);
            Assert.Equal(before, Hash(m));
            Assert.Equal(tris, m.TriangleCount);
            Assert.True(f.IsClosed());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(f, false));
        }

        [Fact]
        public void HoleOfAFewEdges_IsFilled_AlsoAtATinyAlpha()
        {
            // one missing triangle: 3 edges, far longer than 64 alphas of a tiny alpha, but a hole of the surface (MaxLoopEdges)
            DMesh3 m = Sphere(12);
            int tid = m.TriangleIndices().First();
            m.RemoveTriangle(tid, true);
            Assert.NotNull(Fill(m, 1e-6, 64, out int filled, out _));
            Assert.Equal(1, filled);
        }

        [Fact]
        public void LoopLongerThanTheBound_IsNotFilled_ByAlphaAndByEdges()
        {
            // a hole of about 20 edges: above 8 median edges, below the diagonal share
            DMesh3 m = HoleAt(Sphere(40), new Vector3d(0, 0, 0.5), 0.04);
            double len = LongestLoop(m), med = MedianEdge(m);
            output.WriteLine($"loop {len} median edge {med}");
            Assert.True(len > 8 * med, "setup: above the edge bound");
            Assert.True(len < RobustRepairWrapHoleFill.MaxLoopDiagonalShare * Math.Sqrt(3), "setup: below the diagonal share");
            // 64 alphas are a third of the loop: not filled; 64 alphas are three loops: filled
            Assert.Null(Fill(m, len / 200, 64, out int f1, out int c1));
            Assert.Equal(0, f1); Assert.Equal(0, c1);
            Assert.NotNull(Fill(m, len / 20, 64, out int f2, out _));
            Assert.Equal(1, f2);
        }

        [Fact]
        public void LoopLongerThanTheDiagonalShare_IsNeverFilled_EvenAtACoarseAlpha()
        {
            // the rim of an open bowl is as long as the part it bounds: a coarse alpha does not turn it into a hole
            DMesh3 bowl = RobustRepairHollowWrapTests.Bowl(8, 0.1);
            Assert.Null(Fill(bowl, 1, 64, out int filled, out int cand));
            Assert.Equal(0, filled);
            Assert.Equal(0, cand);
        }

        [Fact]
        public void SmallSheetApartFromASolid_IsNotFilled()
        {
            // a single triangle next to a sphere: its loop is short, but the fill would be the triangle again (a lens of twice the area)
            DMesh3 m = Sphere(8);
            int a = m.AppendVertex(new Vector3d(0.7, 0, 0)), b = m.AppendVertex(new Vector3d(0.75, 0.05, 0)), c = m.AppendVertex(new Vector3d(0.75, 0, 0.05));
            m.AppendTriangle(a, b, c);
            Assert.Null(Fill(m, 0.01, 64, out int filled, out int cand));
            Assert.Equal(0, filled);
            Assert.Equal(1, cand);
        }

        [Theory, MemberData(nameof(Names))]
        public void Invariance_Fill_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 m = HoleAt(HoleAt(Sphere(14), new Vector3d(0, 0, 0.5), 0.03), new Vector3d(0.5, 0, 0), 0.0);
            DMesh3 f0 = Fill(m, 0.01, 64, out int n0, out int c0);
            DMesh3 f1 = Fill(Invariance.Transformed(m, s), 0.01 * s.Scale, 64, out int n1, out int c1);
            Assert.NotNull(f0);
            Assert.NotNull(f1);
            Assert.Equal(n0, n1);
            Assert.Equal(c0, c1);
            Assert.Equal(f0.TriangleCount, f1.TriangleCount);
            Assert.True(f1.IsClosed());
            if (s.IsExact) {
                var a = f0.VertexIndices().ToArray();
                var b = f1.VertexIndices().ToArray();
                Assert.Equal(a.Length, b.Length);
                for (int i = 0; i < a.Length; ++i) {
                    Vector3d p = f0.GetVertex(a[i]), q = f1.GetVertex(b[i]) / s.Scale;
                    Assert.True(p.x == q.x && p.y == q.y && p.z == q.z, name + ": vertex " + i);
                }
            }
        }

        public static IEnumerable<object[]> Names() { return Similarity.Names(); }
        public static IEnumerable<object[]> NamesExceptExtremeFrame() { return Similarity.NamesExceptExtremeFrame(); }

        static MeshRobustRepair Config(DMesh3 mesh, double relativeAlpha, double fillAlphas, bool guard)
        {
            return new MeshRobustRepair(mesh) { SolidifyResolution = 80, WrapRelativeAlpha = relativeAlpha, WrapRelativeOffset = 10 * relativeAlpha,
                WrapFillLoopAlphas = fillAlphas, NoThinShellCheck = guard == false };
        }

        [Fact]
        public void IssueRepro_FillsTheHoles_AndWrapsOneSide()
        {
            DMesh3 soup = RepairSoups.MakeSoup(RepairSoupParams.Issue("sphere", 10), out _, out double known);
            MeshRobustRepair r = Config(soup, 100, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.True(r.Apply(), r.ResultReason);
            Assert.True(r.UsedAlphaWrap);
            Assert.Equal(8, r.WrapFilledLoops);
            Assert.Equal(8, r.WrapFillCandidateLoops);
            // one-sided: the raw wrap has the size of a closed solid's (13k here; the hollow two-sided wrap of the base has 72k)
            Assert.InRange(r.AlphaWrapTriangles, 1, 30000);
            Assert.InRange(RepairSoups.Area(r.Mesh), 0.9 * 4.4, 1.2 * 4.6);
        }

        [Fact]
        public void IssueRepro_Reuse_ApplyTwice_SecondIsCleanAndUnwrapped()
        {
            DMesh3 soup = RepairSoups.MakeSoup(RepairSoupParams.Issue("sphere", 10), out _, out _);
            MeshRobustRepair r = Config(soup, 100, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.True(r.Apply());
            Assert.Equal(8, r.WrapFilledLoops);
            ulong first = Hash(r.Mesh);
            Assert.True(r.Apply());
            Assert.Equal("", r.ResultReason);
            Assert.Equal(0, r.WrapFilledLoops);
            Assert.Equal(0, r.WrapFillCandidateLoops);
            Assert.True(r.Mesh.IsClosed());
            Assert.Equal(first, Hash(r.Mesh));
        }

        [Fact]
        public void OpenBowl_LongLoopsOnly_NothingFilled_ResultIdenticalToTheBaseWrap_ThenTheGuardFires()
        {
            DMesh3 bowl = RobustRepairHollowWrapTests.Bowl(8, -0.2);
            MeshRobustRepair baseLike = Config(new DMesh3(bowl), 50, 0, false);
            bool okBase = baseLike.Apply();
            MeshRobustRepair on = Config(new DMesh3(bowl), 50, RobustRepairWrapHoleFill.MaxLoopAlphas, false);
            bool okOn = on.Apply();
            Assert.True(okBase && okOn, "without the guard: the thin shell is a success, as before");
            Assert.True(baseLike.UsedAlphaWrap && on.UsedAlphaWrap);
            Assert.Equal(0, on.WrapFilledLoops);
            Assert.Equal(0, on.WrapFillCandidateLoops);
            Assert.Equal(Hash(baseLike.Mesh), Hash(on.Mesh));
            // with the guard: the same mesh, Apply() false with the reason
            MeshRobustRepair guarded = Config(new DMesh3(bowl), 50, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.False(guarded.Apply());
            Assert.Equal(MeshRobustRepair.ThinShellWrap, guarded.ResultReason);
            Assert.Equal(MeshRobustRepair.ResultPaths.AlphaWrap, guarded.ResultPath);
            Assert.Equal(Hash(on.Mesh), Hash(guarded.Mesh));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(guarded.Mesh, true));
        }

        [Fact]
        public void FlatSheet_HasNoSolid_IsNotTakenForAShell_ResultUnchanged()
        {
            // the guard compares with the solid of the input: a flat sheet has none (the repair closes it to a flat lens), and a small solid is its own wrap
            DMesh3 plate = RobustRepairHollowWrapTests.Plate(10);
            MeshRobustRepair off = Config(new DMesh3(plate), 100, RobustRepairWrapHoleFill.MaxLoopAlphas, false);
            MeshRobustRepair on = Config(new DMesh3(plate), 100, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.True(off.Apply());
            Assert.True(on.Apply(), on.ResultReason);
            Assert.Equal("", on.ResultReason);
            Assert.Equal(Hash(off.Mesh), Hash(on.Mesh));
        }

        [Fact]
        public void ClosedCleanInput_IsUnchanged_ByTheSwitches()
        {
            DMesh3 m = Sphere(10);
            var a = Config(new DMesh3(m), 100, 0, false);
            var b = Config(new DMesh3(m), 100, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.True(a.Apply());
            Assert.True(b.Apply());
            Assert.Equal(Hash(a.Mesh), Hash(b.Mesh));
            Assert.Equal(a.ResultPath, b.ResultPath);
            Assert.Equal(0, b.WrapFilledLoops);
        }

        // (without RotatedUtmMillimetre: the design is 1e-3 of a metre at UTM coordinates, near the limit of the stages before the wrap)
        [Theory, MemberData(nameof(NamesExceptExtremeFrame))]
        public void Invariance_ThinShellGuard_AllSimilarities(string name)
        {
            Similarity s = Similarity.ByName(name);
            DMesh3 plate = RobustRepairHollowWrapTests.Bowl(8, -0.2);
            var id = Config(new DMesh3(plate), 50, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            var tr = Config(Invariance.Transformed(plate, s), 50, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.False(id.Apply());
            Assert.True(id.UsedAlphaWrap);
            bool ok1 = tr.Apply();
            // (the grid of the solidify before the wrap is not invariant under a rotation or a translation: it may succeed there, and then there is no wrap
            // to judge. Where the wrap runs, the guard decides the same; the power-of-two scales run the same stages bit for bit)
            if (s.IsExact == false && tr.UsedAlphaWrap == false)
                return;
            Assert.True(tr.UsedAlphaWrap, name);
            Assert.False(ok1);
            Assert.Equal(MeshRobustRepair.ThinShellWrap, tr.ResultReason);
            Assert.Equal(id.ResultPath, tr.ResultPath);
            if (s.IsExact) {
                Assert.Equal(id.Mesh.TriangleCount, tr.Mesh.TriangleCount);
                var a = id.Mesh.VertexIndices().ToArray();
                var b = tr.Mesh.VertexIndices().ToArray();
                Assert.Equal(a.Length, b.Length);
                for (int i = 0; i < a.Length; ++i) {
                    Vector3d p = id.Mesh.GetVertex(a[i]), q = tr.Mesh.GetVertex(b[i]) / s.Scale;
                    Assert.True(p.x == q.x && p.y == q.y && p.z == q.z, name + ": vertex " + i);
                }
            }
        }

        [Fact]
        public void IssueRepro_Invariance_FillCountInTheFrames()
        {
            // the repro under a power of two scale and a translation: the same holes are filled, the same volume comes back
            DMesh3 soup = RepairSoups.MakeSoup(RepairSoupParams.Issue("sphere", 10), out _, out double known);
            Similarity s = Similarity.Pow2Large;
            MeshRobustRepair r = Config(Invariance.Transformed(soup, s), 100, RobustRepairWrapHoleFill.MaxLoopAlphas, true);
            Assert.True(r.Apply(), r.ResultReason);
            Assert.Equal(8, r.WrapFilledLoops);
            Assert.InRange(Invariance.Volume(r.Mesh) / (s.Scale * s.Scale * s.Scale), 0.97 * known, 1.1 * known);
        }
    }
}
