using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The triangle quality pass on a simplified alpha wrap (MeshRobustRepair.AlphaWrapQualityPass,
    /// AlphaWrapQualityPass): fewer slivers, every changed triangle within the tolerance of the unsimplified wrap,
    /// no self-intersections, the topology kept; deterministic and scale invariant.
    /// </summary>
    public class AlphaWrapQualityPassTests
    {
        // an alpha wrap of a box (flat faces: the simplification leaves slivers) and its simplification
        static void BoxWrap(Box3d b, out DMesh3 wrap, out DMesh3 simplified, out double tol)
        {
            DMesh3 box = new TrivialBox3Generator() { Box = b, NoSharedVertices = false }.Generate().MakeDMesh();
            var w = new MeshAlphaWrap(box) { RelativeAlpha = 60, RelativeOffset = 600 };
            Assert.True(w.Compute());
            wrap = w.Result;
            tol = 0.5 * w.UsedOffset;
            simplified = MeshRobustRepair.SimplifyWithin(wrap, tol, tol, true, () => false);
            Assert.NotNull(simplified);
        }

        static Box3d DefaultBox => new Box3d(Vector3d.Zero, new Vector3d(1, 0.6, 0.3));

        static double MinAngle(DMesh3 m, int t)
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            m.GetTriVertices(t, ref a, ref b, ref c);
            double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
            return Math.Min(A, Math.Min(B, 180 - A - B));
        }

        // the 1 %-worst minimum angle
        static double P01(DMesh3 m)
        {
            var a = m.TriangleIndices().Select(t => MinAngle(m, t)).OrderBy(x => x).ToArray();
            return a[a.Length / 100];
        }

        static double HausdorffUpper(DMesh3 from, DMesh3 to, double tol)
        {
            var hd = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = 0.01 * tol };
            Assert.True(hd.Compute() && hd.Converged);
            return hd.UpperBound;
        }

        [Fact]
        public void ImproveWrapQuality_FewerSlivers_WithinTolerance_Clean_SameTopology()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            DMesh3 copy = new DMesh3(simp);
            DMesh3 q = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            Assert.NotNull(q);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(q, true));
            // flips and moves only: the same vertices, triangles and genus
            Assert.Equal(simp.VertexCount, q.VertexCount);
            Assert.Equal(simp.TriangleCount, q.TriangleCount);
            Assert.Equal(MeshTopologySummary.Compute(simp).Genus, MeshTopologySummary.Compute(q).Genus);
            // better: the 1 %-worst and the worst minimum angle
            double before = P01(simp), after = P01(q);
            Assert.True(after > before + 2, $"1 %-worst min angle {before} -> {after}");
            Assert.True(q.TriangleIndices().Min(t => MinAngle(q, t)) >= simp.TriangleIndices().Min(t => MinAngle(simp, t)));
            // within the tolerance of the unsimplified wrap (certified by the bounds without the patch bound)
            Assert.True(HausdorffUpper(q, wrap, tol) <= tol);
            // the input is not changed
            Assert.Equal("", Invariance.Difference(copy, simp));
        }

        [Fact]
        public void Pass_KeepsEveryTriangleWithinItsTolerance_AndCountsOperations()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            // a tolerance below the simplification's own deviation: the pass may only make changes that stay within it
            foreach (double t in new[] { tol, 0.3 * tol }) {
                var pass = new AlphaWrapQualityPass(new DMesh3(simp), wrap, t);
                Assert.True(pass.Compute());
                Assert.True(pass.Flips + pass.Moves > 0);
                Assert.True(pass.RejectedByDeviation > 0, "the tolerance must reject some operations");
                // every triangle that changed is within t; the unchanged ones keep the simplification's deviation
                var tree = new DMeshAABBTree3(wrap, true);
                var bounds = new TriangleDeviationBounds(wrap);
                var before = new HashSet<string>(simp.TriangleIndices().Select(k => key(simp, k)));
                int changed = 0;
                foreach (int k in pass.Mesh.TriangleIndices()) {
                    if (before.Contains(key(pass.Mesh, k))) continue;
                    changed++;
                    Index3i tv = pass.Mesh.GetTriangle(k);
                    Assert.True(bounds.IsWithin(tree, pass.Mesh.GetVertex(tv.a), pass.Mesh.GetVertex(tv.b), pass.Mesh.GetVertex(tv.c), t * 1.000001, 12),
                        $"tolerance {t}: changed triangle {k} not within it");
                }
                Assert.True(changed > 0);
            }
        }

        // a triangle by its corner positions (independent of ids)
        static string key(DMesh3 m, int t)
        {
            Index3i tv = m.GetTriangle(t);
            var p = new[] { m.GetVertex(tv.a), m.GetVertex(tv.b), m.GetVertex(tv.c) }.Select(v => $"{v.x:R},{v.y:R},{v.z:R}").OrderBy(x => x);
            return string.Join(";", p);
        }

        [Fact]
        public void Accept_RejectsIntersectingAndFarTriangles()
        {
            // two parallel grids 0.01 apart; reference = the mesh itself
            DMesh3 m = new DMesh3();
            Func<double, int[,]> grid = (z) => {
                var ids = new int[6, 6];
                for (int j = 0; j < 6; ++j) for (int i = 0; i < 6; ++i) ids[i, j] = m.AppendVertex(new Vector3d(i * 0.2, j * 0.2, z));
                for (int j = 0; j < 5; ++j) for (int i = 0; i < 5; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
                return ids;
            };
            grid(0); grid(0.01);
            var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 0.05);
            pass.Prepare();
            // a new triangle through the other sheet: rejected by the intersection check (it is within the tolerance)
            var through = new Triangle3d(new Vector3d(0.3, 0.3, -0.004), new Vector3d(0.5, 0.3, 0.014), new Vector3d(0.4, 0.5, 0.005));
            Assert.False(pass.accept(new List<Index3i> { new Index3i(1000, 1001, 1002) }, new List<Triangle3d> { through }, new HashSet<int>()));
            Assert.Equal(1, pass.RejectedByIntersection);
            // between the sheets, touching neither: accepted
            var between = new Triangle3d(new Vector3d(0.3, 0.3, 0.005), new Vector3d(0.5, 0.3, 0.005), new Vector3d(0.4, 0.5, 0.005));
            Assert.True(pass.accept(new List<Index3i> { new Index3i(1000, 1001, 1002) }, new List<Triangle3d> { between }, new HashSet<int>()));
            // far above: rejected by the tolerance
            var far = new Triangle3d(new Vector3d(0.3, 0.3, 0.2), new Vector3d(0.5, 0.3, 0.2), new Vector3d(0.4, 0.5, 0.2));
            Assert.False(pass.accept(new List<Index3i> { new Index3i(1000, 1001, 1002) }, new List<Triangle3d> { far }, new HashSet<int>()));
            Assert.Equal(1, pass.RejectedByDeviation);
            // two new triangles that cross each other
            var a = new Triangle3d(new Vector3d(0.3, 0.3, 0.003), new Vector3d(0.5, 0.3, 0.003), new Vector3d(0.4, 0.5, 0.003));
            var b = new Triangle3d(new Vector3d(0.4, 0.25, 0.001), new Vector3d(0.4, 0.45, 0.001), new Vector3d(0.42, 0.35, 0.007));
            Assert.False(pass.accept(new List<Index3i> { new Index3i(1000, 1001, 1002), new Index3i(1003, 1004, 1005) },
                new List<Triangle3d> { a, b }, new HashSet<int>()));
            Assert.Equal(2, pass.RejectedByIntersection);
        }

        [Fact]
        public void NormalGuard_NoFlipAcrossASharpEdge()
        {
            // two slivers folded over their shared edge ab at 90 degrees: the flip to cd would give fatter triangles,
            // but they turn by about 45 degrees; the tolerance allows it, only the normal guard rejects it
            Func<double, AlphaWrapQualityPass> run = (turn) => {
                var m = new DMesh3();
                int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)),
                    c = m.AppendVertex(new Vector3d(0.5, 0.06, -0.06)), d = m.AppendVertex(new Vector3d(0.5, -0.06, -0.06));
                m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, d);
                var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 100) { EnableMoves = false, MaxNormalTurnDegrees = turn };
                Assert.True(pass.Compute());
                return pass;
            };
            Assert.Equal(0, run(30).Flips);
            Assert.Equal(1, run(180).Flips);      // without the guard the flip is made
            // the crease switch alone also keeps it (the two triangles meet at 90 degrees)
            var m2 = new DMesh3();
            int a2 = m2.AppendVertex(new Vector3d(0, 0, 0)), b2 = m2.AppendVertex(new Vector3d(1, 0, 0)),
                c2 = m2.AppendVertex(new Vector3d(0.5, 0.06, -0.06)), d2 = m2.AppendVertex(new Vector3d(0.5, -0.06, -0.06));
            m2.AppendTriangle(a2, b2, c2); m2.AppendTriangle(b2, a2, d2);
            var crease = new AlphaWrapQualityPass(m2, new DMesh3(m2), 100) { EnableMoves = false, MaxNormalTurnDegrees = 180, MaxFlipDihedralDegrees = 20 };
            Assert.True(crease.Compute());
            Assert.Equal(0, crease.Flips);
        }

        [Fact]
        public void FollowReference_MovesKeepTheirOffsetFromACurvedReference()
        {
            // a sphere as the reference, the mesh its copy pushed out by 0.01 with jittered vertices: with
            // FollowReference a moved vertex keeps its distance to the reference (to the curvature's second order),
            // without it a tangential move leaves the offset surface
            DMesh3 sphere = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 16 }.Generate().MakeDMesh();
            var tree = new DMeshAABBTree3(sphere, true);
            Func<Vector3d, double> dist = (p) => Math.Sqrt(MeshQueries.TriDistanceSqr(sphere, tree.FindNearestTriangle(p), p));
            var rand = new Random(2);
            DMesh3 start = new DMesh3(sphere);
            foreach (int v in start.VertexIndices()) {
                Vector3d p = start.GetVertex(v);
                Vector3d t = new Vector3d(rand.NextDouble() - 0.5, rand.NextDouble() - 0.5, rand.NextDouble() - 0.5) * 0.04;
                start.SetVertex(v, (p + t).Normalized * 1.01);
            }
            Func<bool, double> worst = (follow) => {
                var pass = new AlphaWrapQualityPass(new DMesh3(start), sphere, 0.05) { EnableFlips = false, FollowReference = follow, MaxPasses = 1, TargetAngleDegrees = 60 };
                Assert.True(pass.Compute());
                Assert.True(pass.Moves > 0);
                // the largest change of a vertex's distance to the reference
                return pass.Mesh.VertexIndices().Max(v => Math.Abs(dist(pass.Mesh.GetVertex(v)) - dist(start.GetVertex(v))));
            };
            double with = worst(true), without = worst(false);
            Assert.True(with < 0.5 * without, $"offset error with {with}, without {without}");
        }

        [Fact]
        public void NormalGuard_NoMoveThatTiltsASteepFan()
        {
            // a low pyramid with its apex far off the centre of its base: moving the apex over the centroid would tilt faces by more than
            // 30 degrees
            Func<double, AlphaWrapQualityPass> run = (turn) => {
                var m = new DMesh3();
                int apex = m.AppendVertex(new Vector3d(0.2, 0.2, 0.4));
                var b = new[] { new Vector3d(0, 0, 0), new Vector3d(1.6, 0, 0), new Vector3d(1.6, 1.2, 0), new Vector3d(0, 1.2, 0) }
                    .Select(p => m.AppendVertex(p)).ToArray();
                for (int k = 0; k < 4; ++k) m.AppendTriangle(apex, b[k], b[(k + 1) % 4]);
                // (the base vertices are boundary vertices and stay; the apex is interior)
                var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 100) { EnableFlips = false, MaxNormalTurnDegrees = turn, TargetAngleDegrees = 60 };
                Assert.True(pass.Compute());
                return pass;
            };
            Assert.Equal(0, run(30).Moves);
            Assert.True(run(180).Moves > 0);
        }

        [Fact]
        public void Moves_NeverLowerTheWorstAngle_OnAJitteredGrid()
        {
            // a planar grid with jittered vertices and the boundary fixed: every kept move raises the smallest angle
            // of its one ring (and every flip that of its two triangles), so the worst angle of the mesh cannot drop
            var rand = new Random(5);
            int n = 12;
            var m = new DMesh3();
            var ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i) {
                    bool inner = i > 0 && j > 0 && i < n && j < n;
                    ids[i, j] = m.AppendVertex(new Vector3d(i + (inner ? 0.45 * (rand.NextDouble() - 0.5) : 0), j + (inner ? 0.45 * (rand.NextDouble() - 0.5) : 0), 0));
                }
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            DMesh3 before = new DMesh3(m);
            var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 0.01) { MaxPasses = 1, EnableFlips = false };
            Assert.True(pass.Compute());
            Assert.True(pass.Moves > 0);
            Assert.True(pass.Mesh.TriangleIndices().Min(t => MinAngle(pass.Mesh, t)) >= before.TriangleIndices().Min(t => MinAngle(before, t)) - 1e-9);
        }

        [Fact]
        public void NoMoveWithoutGain()
        {
            // a planar fan whose centroid lies close to an edge of its ring: moving the vertex there lowers the
            // smallest angle (6.2 -> 2.8 degrees), so it is not moved
            var ring = new[] { new Vector2d(0.11, 1.44), new Vector2d(-0.04, 1.23), new Vector2d(-1.24, 0.35), new Vector2d(-1.18, -0.11),
                new Vector2d(-0.81, -0.46), new Vector2d(0.78, -0.11) };
            var m = new DMesh3();
            int v = m.AppendVertex(Vector3d.Zero);
            int[] r = ring.Select(p => m.AppendVertex(new Vector3d(p.x, p.y, 0))).ToArray();
            for (int k = 0; k < r.Length; ++k) Assert.True(m.AppendTriangle(v, r[k], r[(k + 1) % r.Length]) >= 0);
            var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 1) { EnableFlips = false };
            Assert.True(pass.Compute());
            Assert.Equal(0, pass.Moves);
            Assert.Equal(Vector3d.Zero, pass.Mesh.GetVertex(v));
        }

        [Fact]
        public void Regrid_TheIntersectionCheckSeesMovedTriangles()
        {
            // two parallel grids; a vertex of the lower one is moved up through the space of the upper one's
            // neighbour region: after regrid, a new triangle at the moved position is found intersecting
            DMesh3 m = new DMesh3();
            var lower = new int[6, 6];
            for (int j = 0; j < 6; ++j) for (int i = 0; i < 6; ++i) lower[i, j] = m.AppendVertex(new Vector3d(i * 0.2, j * 0.2, 0));
            for (int j = 0; j < 5; ++j) for (int i = 0; i < 5; ++i) {
                m.AppendTriangle(lower[i, j], lower[i + 1, j], lower[i + 1, j + 1]);
                m.AppendTriangle(lower[i, j], lower[i + 1, j + 1], lower[i, j + 1]);
            }
            var pass = new AlphaWrapQualityPass(m, new DMesh3(m), 10);
            pass.Prepare();
            // move vertex (2, 2) far up; the grid still has its triangles' old (flat) boxes
            int v = lower[2, 2];
            var ring = m.VtxTrianglesItr(v).ToList();
            m.SetVertex(v, new Vector3d(0.4, 0.4, 3));
            pass.regrid(ring, ring);
            // a triangle near the moved vertex, high above the plane: it crosses the moved triangles
            var probe = new Triangle3d(new Vector3d(0.25, 0.4, 2.0), new Vector3d(0.55, 0.4, 2.0), new Vector3d(0.4, 0.6, 2.2));
            Assert.False(pass.accept(new List<Index3i> { new Index3i(1000, 1001, 1002) }, new List<Triangle3d> { probe }, new HashSet<int>()));
            Assert.Equal(1, pass.RejectedByIntersection);
        }

        [Fact]
        public void WorstFirst_OtherOrder_StillWithinAndClean_Deterministic()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            Func<bool, DMesh3> run = (worst) => MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, (p) => p.WorstFirst = worst);
            DMesh3 byId = run(false), a = run(true), b = run(true);
            Assert.NotNull(a);
            Assert.Equal("", Invariance.Difference(a, b));          // deterministic
            Assert.NotEqual("", Invariance.Difference(byId, a));    // another order, another result
            Assert.Equal("", MeshRobustRepair.NotCleanReason(a, true));
            Assert.True(HausdorffUpper(a, wrap, tol) <= tol);
            Assert.True(P01(a) > P01(simp) + 2);
        }

        // area-weighted mean angle between m's normals and the wrap's (at the wrap triangle nearest each centroid)
        static double NormalDeviation(DMesh3 m, DMesh3 wrap)
        {
            var tree = new DMeshAABBTree3(wrap, true);
            double s = 0, a = 0;
            foreach (int t in m.TriangleIndices()) {
                double ar = m.GetTriArea(t);
                s += ar * Vector3d.AngleD(m.GetTriNormal(t), wrap.GetTriNormal(tree.FindNearestTriangle(m.GetTriCentroid(t))));
                a += ar;
            }
            return s / a;
        }

        [Fact]
        public void KeepNormalDeviation_NormalsStayAsCloseToTheWrap()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            DMesh3 free = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            DMesh3 kept = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, (p) => p.KeepNormalDeviation = true);
            DMesh3 slack = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false,
                (p) => { p.KeepNormalDeviation = true; p.NormalDeviationSlackDegrees = 0.5; });
            Assert.NotNull(free); Assert.NotNull(kept); Assert.NotNull(slack);
            double d0 = NormalDeviation(simp, wrap), dFree = NormalDeviation(free, wrap), dKept = NormalDeviation(kept, wrap), dSlack = NormalDeviation(slack, wrap);
            // (every operation keeps the mean of its own triangles; the measure here samples each triangle once, so
            // it may differ a little from the per-operation means)
            Assert.True(dKept <= d0 * 1.05, $"kept {dKept} vs simplified {d0}");
            Assert.True(dFree > dKept, $"free {dFree} vs kept {dKept}");
            Assert.True(dSlack >= dKept && dSlack <= dFree * 1.0001, $"slack {dSlack} between {dKept} and {dFree}");
            // still better angles than the simplification
            Assert.True(P01(kept) > P01(simp), $"{P01(simp)} -> {P01(kept)}");
        }

        // share of triangles with a largest angle above deg
        static double CapShare(DMesh3 m, double deg)
        {
            int n = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                m.GetTriVertices(t, ref a, ref b, ref c);
                double A = Vector3d.AngleD((b - a).Normalized, (c - a).Normalized), B = Vector3d.AngleD((a - b).Normalized, (c - b).Normalized);
                if (Math.Max(A, Math.Max(B, 180 - A - B)) > deg) n++;
            }
            return (double)n / m.TriangleCount;
        }

        [Fact]
        public void SplitCaps_FewerCaps_WithinTheBudget_SameSurface()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            DMesh3 q = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, (p) => { p.SplitCapsAboveDegrees = 150; p.EnableFlips = p.EnableMoves = false; });
            Assert.NotNull(q);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(q, true));
            Assert.True(q.TriangleCount > simp.TriangleCount && q.TriangleCount <= Math.Floor(simp.TriangleCount * 1.05), $"{simp.TriangleCount} -> {q.TriangleCount}");
            Assert.True(CapShare(q, 150) < CapShare(simp, 150), $"caps {CapShare(simp, 150)} -> {CapShare(q, 150)}");
            // splits only: the surface is the same (every new vertex lies on an old edge), so it is as close to the wrap
            Assert.True(HausdorffUpper(q, wrap, tol) <= HausdorffUpper(simp, wrap, tol) + 0.01 * tol);
            Assert.Equal(MeshTopologySummary.Compute(simp).Genus, MeshTopologySummary.Compute(q).Genus);
            // no budget: no split
            Assert.Null(MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false,
                (p) => { p.SplitCapsAboveDegrees = 150; p.MaxTriangleGrowth = 0; p.EnableFlips = p.EnableMoves = false; }));
        }

        [Fact]
        public void CapFlips_FewerCapsUnderTheNormalGuard()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            Action<AlphaWrapQualityPass> d = (p) => { p.KeepNormalDeviation = true; p.NormalDeviationSlackDegrees = 0.1; };
            DMesh3 without = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, d);
            DMesh3 with = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, (p) => { d(p); p.CapAngleDegrees = 150; });
            Assert.NotNull(without); Assert.NotNull(with);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(with, true));
            Assert.True(HausdorffUpper(with, wrap, tol) <= tol);
            Assert.True(CapShare(with, 150) < CapShare(without, 150), $"caps {CapShare(without, 150)} -> {CapShare(with, 150)}");
            Assert.Equal(simp.TriangleCount, with.TriangleCount);       // flips and moves only
            // the two parts: the exemption from the normal guard on nearly flat pairs (none: CapFlipMaxDihedralDegrees 0) ...
            DMesh3 noExemption = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false,
                (p) => { d(p); p.CapAngleDegrees = 150; p.CapFlipMaxDihedralDegrees = 0; });
            Assert.True(CapShare(with, 150) < CapShare(noExemption, 150), $"exemption: {CapShare(noExemption, 150)} -> {CapShare(with, 150)}");
            // ... and the flips without a minimum-angle gain (no normal guard here)
            DMesh3 plain = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            DMesh3 plainCaps = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false, (p) => p.CapAngleDegrees = 150);
            Assert.NotEqual("", Invariance.Difference(plain, plainCaps));
            Assert.True(CapShare(plainCaps, 150) <= CapShare(plain, 150), $"no gain needed: {CapShare(plain, 150)} -> {CapShare(plainCaps, 150)}");
        }

        [Fact]
        public void InvariantUnderSimilarities()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            DMesh3 q0 = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            Assert.NotNull(q0);
            double p0 = P01(q0);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 w = new DMesh3(wrap), m = new DMesh3(simp);
                MeshTransforms.PerVertexTransform(w, s.Apply);
                MeshTransforms.PerVertexTransform(m, s.Apply);
                DMesh3 q = MeshRobustRepair.ImproveWrapQuality(m, w, s.ApplyLength(tol), true, () => false);
                Assert.True(q != null, s.Name);
                Assert.Equal("", MeshRobustRepair.NotCleanReason(q, true));
                if (s.IsExact) {
                    // the same operations: the same mesh, scaled
                    Assert.Equal(q0.TriangleCount, q.TriangleCount);
                    Assert.True(q0.VertexIndices().All(v => q.GetVertex(v) == s.Apply(q0.GetVertex(v))), s.Name);
                    Assert.True(q0.TriangleIndices().All(t => q.GetTriangle(t) == q0.GetTriangle(t)), s.Name);
                } else {
                    Assert.True(Math.Abs(P01(q) - p0) < 3, $"{s.Name}: 1 %-worst min angle {P01(q)} vs {p0}");
                }
            }
        }

        [Fact]
        public void Deterministic_AndComputeTwiceOnOneInstance()
        {
            DMesh3 wrap, simp; double tol;
            BoxWrap(DefaultBox, out wrap, out simp, out tol);
            DMesh3 a = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            DMesh3 b = MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => false);
            Assert.Equal("", Invariance.Difference(a, b));
            // a second Compute on the same instance continues from its result: counters reset, still within the tolerance
            // (0.95 of the tolerance, as ImproveWrapQuality: the final check resolves to 1 % of it)
            var pass = new AlphaWrapQualityPass(new DMesh3(simp), wrap, 0.95 * tol);
            Assert.True(pass.Compute());
            int first = pass.Flips + pass.Moves;
            Assert.True(first > 0);
            Assert.True(pass.Compute());
            Assert.True(pass.Flips + pass.Moves < first, $"{pass.Flips + pass.Moves} after {first}");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(new DMesh3(pass.Mesh, true), true));
            Assert.True(HausdorffUpper(pass.Mesh, wrap, tol) <= tol);
            // cancelled: null
            Assert.Null(MeshRobustRepair.ImproveWrapQuality(simp, wrap, tol, true, () => true));
        }

        [Fact]
        public void RobustRepair_QualityPass_OffByDefault_OnImprovesTheWrap()
        {
            Func<bool, MeshRobustRepair> run = (on) => {
                var r = new MeshRobustRepair(MeshRobustRepairTests.ThinPlates(6)) {
                    RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 64, AlphaWrapQualityPass = on
                };
                Assert.True(r.Apply());
                Assert.True(r.UsedAlphaWrap && r.AlphaWrapSimplified);
                return r;
            };
            Assert.False(new MeshRobustRepair(new DMesh3()).AlphaWrapQualityPass);
            var off = run(false);
            Assert.False(off.AlphaWrapQualityImproved);
            Assert.False(off.PhaseMilliseconds.ContainsKey("wrap.quality"));
            var on = run(true);
            Assert.True(on.AlphaWrapQualityImproved);
            Assert.True(on.PhaseMilliseconds.ContainsKey("wrap.quality"));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(on.Mesh, true));
            Assert.Equal("", on.ResultReason);
            Assert.True(P01(on.Mesh) > P01(off.Mesh), $"{P01(off.Mesh)} -> {P01(on.Mesh)}");
            Assert.True(HausdorffUpper(on.Mesh, on.AlphaWrap.Result, on.AlphaWrap.UsedOffset) <= 0.5 * on.AlphaWrap.UsedOffset);
            // the simplification is the same with and without the pass
            Assert.Equal(off.AlphaWrapTriangles, on.AlphaWrapTriangles);
            Assert.Equal(off.Mesh.TriangleCount, on.Mesh.TriangleCount);
        }
    }
}
