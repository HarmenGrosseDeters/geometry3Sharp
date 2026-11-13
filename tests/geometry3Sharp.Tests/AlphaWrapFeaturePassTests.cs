using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// AlphaWrapFeaturePass (Alpha Wrap 3.0 lever 6, the sharp-feature pass on the raw wrap): the concave crease of an L-shaped
    /// block, which the wrap rounds off, comes back on the offset surface's miter line; the wrap's guarantees stay (closed,
    /// manifold, no self-intersections, no contact with the input, vertices at least 0.9 offset from it); the pass is
    /// deterministic, scale invariant (2^k bit for bit), reusable, cancellable, and does not sweep over input it skips.
    /// </summary>
    public class AlphaWrapFeaturePassTests
    {
        const double RelAlpha = 60, RelOffset = 600;

        // solid unit cells of an nx x ny x nz grid, every boundary cell face as two outward triangles
        internal static DMesh3 PolyCube(int nx, int ny, int nz, Func<int, int, int, bool> solid)
        {
            Func<int, int, int, bool> s = (x, y, z) => x >= 0 && y >= 0 && z >= 0 && x < nx && y < ny && z < nz && solid(x, y, z);
            var mesh = new DMesh3();
            var ids = new Dictionary<(int, int, int), int>();
            Func<int, int, int, int> vid = (x, y, z) => {
                if (!ids.TryGetValue((x, y, z), out int id)) { id = mesh.AppendVertex(new Vector3d(x, y, z)); ids[(x, y, z)] = id; }
                return id;
            };
            int[][] dirs = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
            for (int x = 0; x < nx; ++x) for (int y = 0; y < ny; ++y) for (int z = 0; z < nz; ++z) {
                if (!s(x, y, z)) continue;
                foreach (int[] d in dirs) {
                    if (s(x + d[0], y + d[1], z + d[2])) continue;
                    int ax = d[0] != 0 ? 0 : (d[1] != 0 ? 1 : 2), u = (ax + 1) % 3, v = (ax + 2) % 3;
                    int[] c = { x, y, z };
                    if (d[ax] > 0) c[ax] += 1;
                    var q = new int[4][];
                    for (int k = 0; k < 4; ++k) {
                        q[k] = new[] { c[0], c[1], c[2] };
                        if (k == 1 || k == 2) q[k][u] += 1;
                        if (k == 2 || k == 3) q[k][v] += 1;
                    }
                    int i0 = vid(q[0][0], q[0][1], q[0][2]), i1 = vid(q[1][0], q[1][1], q[1][2]), i2 = vid(q[2][0], q[2][1], q[2][2]), i3 = vid(q[3][0], q[3][1], q[3][2]);
                    if (d[ax] > 0) { mesh.AppendTriangle(i0, i1, i2); mesh.AppendTriangle(i0, i2, i3); }
                    else { mesh.AppendTriangle(i0, i2, i1); mesh.AppendTriangle(i0, i3, i2); }
                }
            }
            return mesh;
        }

        // an L-shaped block 10 x 10 x 4 without the quadrant x, y >= 5: a re-entrant (concave) edge along x = y = 5
        internal static DMesh3 LShape() { return PolyCube(10, 10, 4, (x, y, z) => !(x >= 5 && y >= 5)); }

        static MeshAlphaWrap Wrap(DMesh3 input, double relAlpha = RelAlpha)
        {
            var w = new MeshAlphaWrap(input) { RelativeAlpha = relAlpha, RelativeOffset = 10 * relAlpha };
            Assert.True(w.Compute(), w.Error);
            return w;
        }

        // share of points on the concave crease's miter line (x = y = 5 + offset, z in [0.5, 3.5], mapped by s) within one offset of a
        // sharp edge (dihedral above 60 degrees) of m
        static double ConcaveCoverage(DMesh3 m, double offset, Similarity s)
        {
            var sharp = new List<Segment3d>();
            foreach (int eid in m.EdgeIndices()) {
                Index2i et = m.GetEdgeT(eid);
                if (et.b < 0) continue;
                if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) > Math.Cos(60 * MathUtil.Deg2Rad)) continue;
                Index2i ev = m.GetEdgeV(eid);
                sharp.Add(new Segment3d(m.GetVertex(ev.a), m.GetVertex(ev.b)));
            }
            double o = offset / s.Scale;
            int n = 40, hit = 0;
            for (int k = 0; k < n; ++k) {
                Vector3d p = s.Apply(new Vector3d(5 + o, 5 + o, 0.5 + 3.0 * k / (n - 1)));
                if (sharp.Any(g => g.DistanceSquared(p) <= offset * offset)) hit++;
            }
            return (double)hit / n;
        }

        // the wrap's guarantees on m: clean (closed, manifold, no self-intersections), no contact with the input (exact), every
        // vertex at least 0.9 offset (less the rounding) from the input, the input enclosed (winding number 1)
        static void AssertGuarantees(DMesh3 m, DMesh3 input, double offset, string what)
        {
            Assert.True(MeshRobustRepair.NotCleanReason(m) == "", what + ": " + MeshRobustRepair.NotCleanReason(m));
            var tree = new DMeshAABBTree3(input, true);
            var tris = m.TriangleIndices().Select(t => { Index3i v = m.GetTriangle(t); return new Triangle3d(m.GetVertex(v.a), m.GetVertex(v.b), m.GetVertex(v.c)); }).ToList();
            Assert.False(Reducer.TrianglesTouch(tree, tris), what + ": touches the input");
            double dmin = m.VertexIndices().Min(v => { tree.FindNearestTriangle(m.GetVertex(v), out double d2); return Math.Sqrt(d2); });
            Assert.True(dmin >= (0.9 - 1e-9) * offset, $"{what}: vertex {dmin / offset} offsets from the input");
            // enclosure as such: every input vertex and triangle centroid has winding number 1 with respect to m
            var wt = new DMeshAABBTree3(m, true);
            foreach (int v in input.VertexIndices())
                Assert.True(Math.Abs(wt.WindingNumber(input.GetVertex(v)) - 1) < 0.01, $"{what}: input vertex {v} not enclosed");
            foreach (int t in input.TriangleIndices())
                Assert.True(Math.Abs(wt.WindingNumber(input.GetTriCentroid(t)) - 1) < 0.01, $"{what}: input triangle {t} not enclosed");
        }

        [Fact]
        public void LShape_RecoversTheConcaveCrease_KeepsTheGuarantees()
        {
            DMesh3 input = LShape();
            MeshAlphaWrap w = Wrap(input);
            double before = ConcaveCoverage(w.Result, w.UsedOffset, Similarity.Identity);
            var pass = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(pass.Compute());
            double after = ConcaveCoverage(pass.Mesh, w.UsedOffset, Similarity.Identity);
            Assert.True(before < 0.3 && after > 0.8, $"concave crease coverage {before} -> {after}");
            Assert.True(pass.PlaneMoves > 0 && pass.CreaseSnaps > 0 && pass.InputCreases > 0);
            AssertGuarantees(pass.Mesh, input, w.UsedOffset, "after the pass");
            // the angle floors hold per operation, so also globally: min angle >= min(the wrap's, 10), max <= max(the wrap's, 150)
            double minBefore, maxBefore, minAfter, maxAfter, s1;
            MeshRobustRepair.angle_stats(w.Result, out minBefore, out maxBefore, out s1);
            MeshRobustRepair.angle_stats(pass.Mesh, out minAfter, out maxAfter, out s1);
            Assert.True(minAfter >= Math.Min(minBefore, 10) - 1e-6 && maxAfter <= Math.Max(maxBefore, 150) + 1e-6,
                $"angles {minBefore} / {maxBefore} -> {minAfter} / {maxAfter}");
            // and through the simplification, as the pipeline runs it
            DMesh3 simplified = MeshRobustRepair.SimplifyWithin(pass.Mesh, 0.5 * w.UsedOffset, 0.5 * w.UsedOffset, true, () => false, out int _,
                MeshRobustRepair.WrapSimplifyMinQuality, true, MeshRobustRepair.WrapSimplifySlabs, 0, null, input);
            Assert.NotNull(simplified);
            Assert.True(ConcaveCoverage(simplified, w.UsedOffset, Similarity.Identity) > 0.8);
            Assert.True(MeshRobustRepair.NotCleanReason(simplified) == "");
        }

        // the repair's opt-in switch (off by default) runs the pass between the wrap and the simplification
        [Fact]
        public void RobustRepair_SharpFeatures_OffByDefault_RunsWhenOn()
        {
            Assert.False(new MeshRobustRepair(new DMesh3()).AlphaWrapSharpFeatures);
            Func<bool, MeshRobustRepair> run = (on) => {
                var r = new MeshRobustRepair(MeshRobustRepairTests.ThinPlates(6)) {
                    RepairF = (m) => new DMesh3(m), ExactResolve = false, SolidifyResolution = 32, WrapRelativeAlpha = 40, WrapRelativeOffset = 400,
                    AlphaWrapSharpFeatures = on
                };
                // (a not-clean solidify, so the wrap is used: MeshRobustRepairTests.WithUnrepairedContour)
                Assert.True(MeshRobustRepairTests.WithUnrepairedContour(() => r.Apply()));
                Assert.True(r.UsedAlphaWrap && r.AlphaWrapSimplified);
                return r;
            };
            var off = run(false);
            Assert.False(off.AlphaWrapSharpFeaturesApplied);
            Assert.False(off.PhaseMilliseconds.ContainsKey("wrap.features"));
            var on = run(true);
            Assert.True(on.AlphaWrapSharpFeaturesApplied);
            Assert.True(on.PhaseMilliseconds.ContainsKey("wrap.features"));
            Assert.Equal("", on.ResultReason);
            AssertGuarantees(on.Mesh, MeshRobustRepairTests.ThinPlates(6), 0, "repair with sharp features");      // (the simplified wrap: no distance floor)
            Assert.NotEqual(AlphaWrapManifoldStepTests.Hash(off.Mesh), AlphaWrapManifoldStepTests.Hash(on.Mesh));
        }

        // invariance over Similarity.All(): clean and the crease recovered on every transformed input; the 2^k scales give the
        // result scaled bit for bit
        [Fact]
        public void FeaturePass_Invariance()
        {
            DMesh3 input = LShape();
            // (a coarser wrap: nine wraps within the test's time)
            MeshAlphaWrap w0 = Wrap(input, 30);
            var p0 = new AlphaWrapFeaturePass(new DMesh3(w0.Result, true), input, w0.UsedAlpha, w0.UsedOffset);
            Assert.True(p0.Compute());
            foreach (Similarity s in Similarity.All()) {
                DMesh3 m = new DMesh3(input);
                MeshTransforms.PerVertexTransform(m, s.Apply);
                MeshAlphaWrap w = Wrap(m, 30);
                var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), m, w.UsedAlpha, w.UsedOffset);
                Assert.True(p.Compute(), s.Name);
                AssertGuarantees(p.Mesh, m, w.UsedOffset, s.Name);
                Assert.True(ConcaveCoverage(p.Mesh, w.UsedOffset, s) > 0.8, s.Name);
                if (s.IsExact) {
                    Assert.Equal(p0.Mesh.TriangleCount, p.Mesh.TriangleCount);
                    Assert.True(p0.Mesh.VertexIndices().All(v => p.Mesh.GetVertex(v) == s.Apply(p0.Mesh.GetVertex(v))), s.Name);
                    Assert.True(p0.Mesh.TriangleIndices().All(t => p.Mesh.GetTriangle(t) == p0.Mesh.GetTriangle(t)), s.Name);
                    Assert.Equal(p0.Splits, p.Splits);
                }
            }
        }

        // deterministic (two runs, the same mesh bit for bit) and reusable (Compute again on the same instance resets the counters
        // and gives what a new instance on the same mesh gives)
        [Fact]
        public void FeaturePass_Deterministic_AndReusable()
        {
            DMesh3 input = LShape();
            MeshAlphaWrap w = Wrap(input);
            var a = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            var b = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(a.Compute()); Assert.True(b.Compute());
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(a.Mesh), AlphaWrapManifoldStepTests.Hash(b.Mesh));
            int firstMoves = a.PlaneMoves;
            var again = new AlphaWrapFeaturePass(new DMesh3(a.Mesh, false), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(again.Compute());
            Assert.True(a.Compute());
            Assert.True(a.PlaneMoves < firstMoves, $"plane moves {firstMoves}, then {a.PlaneMoves}");
            Assert.Equal(again.PlaneMoves, a.PlaneMoves);
            Assert.Equal(again.Splits, a.Splits);
            Assert.Equal(AlphaWrapManifoldStepTests.Hash(new DMesh3(again.Mesh, true)), AlphaWrapManifoldStepTests.Hash(new DMesh3(a.Mesh, true)));
            AssertGuarantees(a.Mesh, input, w.UsedOffset, "second run");
        }

        [Fact]
        public void FeaturePass_Cancel_LeavesACleanMesh()
        {
            DMesh3 input = LShape();
            MeshAlphaWrap w = Wrap(input);
            var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset) { CancelF = () => true };
            Assert.False(p.Compute());
            AssertGuarantees(p.Mesh, input, w.UsedOffset, "cancelled");
        }

        // no sharp edge in the input (a sphere): nothing to do
        [Fact]
        public void FeaturePass_SmoothInput_NoChange()
        {
            DMesh3 input = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 12 }.Generate().MakeDMesh();
            MeshAlphaWrap w = Wrap(input);
            string h = AlphaWrapManifoldStepTests.Hash(w.Result);
            var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(p.Compute());
            Assert.Equal(0, p.InputCreases);
            Assert.Equal(h, AlphaWrapManifoldStepTests.Hash(p.Mesh));
        }

        // a move whose end state is clean but whose sweep contains an input triangle (a small one strictly inside the tetrahedron
        // between a ring triangle and the new position) is refused: the end state alone would leave that part outside the wrap
        [Fact]
        public void TryMove_RefusesToSweepOverInput()
        {
            DMesh3 box = PolyCube(10, 6, 4, (x, y, z) => true);
            MeshAlphaWrap w = Wrap(box);
            DMesh3 raw = w.Result;
            double delta = w.UsedOffset;
            // a vertex on the top face, away from the edges
            int v = raw.VertexIndices().Where(i => { Vector3d p = raw.GetVertex(i); return p.z > 4 && p.x > 3 && p.x < 7 && p.y > 2 && p.y < 4; }).First();
            Vector3d pv = raw.GetVertex(v), x = pv + delta * Vector3d.AxisZ;
            int t0 = raw.VtxTrianglesItr(v).First();
            Index3i tv = raw.GetTriangle(t0);
            Vector3d c0 = (raw.GetVertex(tv.a) + raw.GetVertex(tv.b) + raw.GetVertex(tv.c)) / 3;
            Vector3d inside = 0.45 * c0 + 0.25 * pv + 0.3 * x;
            double e = 1e-3 * delta;
            DMesh3 input = new DMesh3(box);
            int a = input.AppendVertex(inside + new Vector3d(e, 0, 0)), b = input.AppendVertex(inside + new Vector3d(-e, e, 0)), c = input.AppendVertex(inside + new Vector3d(-e, -e, 0));
            input.AppendTriangle(a, b, c);
            foreach (bool sweep in new[] { true, false }) {
                var p = new AlphaWrapFeaturePass(new DMesh3(raw, true), input, w.UsedAlpha, w.UsedOffset) { CheckSweep = sweep, MinDistance = 0 };
                Assert.True(p.Prepare());
                Assert.Equal(!sweep, p.TryMove(v, x));
            }
            // (and without the extra triangle the move is fine)
            var q = new AlphaWrapFeaturePass(new DMesh3(raw, true), box, w.UsedAlpha, w.UsedOffset);
            Assert.True(q.Prepare());
            Assert.True(q.TryMove(v, x));
        }

        // the end state: a move whose new triangle cuts an input triangle (a small one through the moved ring triangle's centroid,
        // given to the pass only, several offsets from every vertex) is refused, and so is one that brings the vertex closer than
        // MinDistance; the sweep and shape checks are off, to test these two alone
        [Fact]
        public void TryMove_RefusesToCutTheInput_OrComeTooClose()
        {
            DMesh3 box = PolyCube(10, 6, 4, (x, y, z) => true);
            MeshAlphaWrap w = Wrap(box);
            DMesh3 raw = w.Result;
            double delta = w.UsedOffset;
            int v = raw.VertexIndices().Where(i => { Vector3d p = raw.GetVertex(i); return p.z > 4 && p.x > 3 && p.x < 7 && p.y > 2 && p.y < 4; }).First();
            Vector3d pv = raw.GetVertex(v), x = pv + delta * Vector3d.AxisZ;
            Index3i tv = raw.GetTriangle(raw.VtxTrianglesItr(v).First());
            Func<int, Vector3d> at = (k) => k == v ? x : raw.GetVertex(k);
            Vector3d c = (at(tv.a) + at(tv.b) + at(tv.c)) / 3;
            double e = 0.05 * delta;
            DMesh3 input = new DMesh3(box);
            int a = input.AppendVertex(c + new Vector3d(-e, 0, -e)), b = input.AppendVertex(c + new Vector3d(e, 0, -e)), d = input.AppendVertex(c + new Vector3d(0, 0, e));
            input.AppendTriangle(a, b, d);
            Func<DMesh3, AlphaWrapFeaturePass> make = (inp) => {
                var p = new AlphaWrapFeaturePass(new DMesh3(raw, true), inp, w.UsedAlpha, w.UsedOffset) {
                    CheckSweep = false, MinAngleFloorDegrees = 0, MaxAngleCeilingDegrees = 180, MaxNormalTurnDegrees = 180 };
                Assert.True(p.Prepare());
                return p;
            };
            Assert.False(make(input).TryMove(v, x));
            Assert.True(make(box).TryMove(v, x));
            Assert.False(make(box).TryMove(v, new Vector3d(pv.x, pv.y, 4 + 0.5 * delta)));
        }

        // a degenerate input triangle (a needle, collinear corners) just under the flat top of the wrap: a move that cuts it must be
        // refused (review of #736, finding 1; Reducer.TrianglesTouch used to skip degenerate obstacle triangles, #746)
        [Fact]
        public void TryMove_RefusesToCutADegenerateInputTriangle()
        {
            DMesh3 box = PolyCube(10, 6, 4, (x, y, z) => true);
            MeshAlphaWrap w = Wrap(box);
            DMesh3 raw = w.Result;
            double delta = w.UsedOffset, al = w.UsedAlpha;
            int v = raw.VertexIndices().Where(i => { Vector3d p = raw.GetVertex(i); return p.z > 4 && p.x > 3 && p.x < 7 && p.y > 2 && p.y < 4; }).First();
            Vector3d pv = raw.GetVertex(v), x = pv - 0.05 * delta * Vector3d.AxisZ;
            // the needle: long, horizontal, between the old and the new height of the vertex, through its x
            Vector3d n0 = new Vector3d(pv.x - 3 * al, pv.y, pv.z - 0.02 * delta), n1 = new Vector3d(pv.x + 3 * al, pv.y, pv.z - 0.02 * delta);
            Func<bool, DMesh3> input = (degenerate) => {
                DMesh3 m = new DMesh3(box);
                Vector3d c = degenerate ? 0.5 * n0 + 0.5 * n1 : 0.5 * n0 + 0.5 * n1 + new Vector3d(0, 1e-3 * delta, 0);
                m.AppendTriangle(m.AppendVertex(n0), m.AppendVertex(n1), m.AppendVertex(c));
                return m;
            };
            Func<DMesh3, bool, bool> tryMove = (inp, sweep) => {
                var p = new AlphaWrapFeaturePass(new DMesh3(raw, true), inp, al, delta) {
                    CheckSweep = sweep, MinAngleFloorDegrees = 0, MaxAngleCeilingDegrees = 180, MaxNormalTurnDegrees = 180, MinDistance = 0 };
                Assert.True(p.Prepare());
                return p.TryMove(v, x);
            };
            Assert.True(tryMove(box, true), "without the needle the move is accepted");
            // the moved fan crosses the needle: refused by the end state alone, degenerate or not
            Assert.False(tryMove(input(true), false), "degenerate needle");
            Assert.False(tryMove(input(false), false), "the same as a sliver");
            Assert.False(tryMove(input(true), true));
        }

        // the sweep's face test: an input triangle that lies in a side face (p, u, x) of the swept tetrahedra has no point
        // strictly inside one, and the end state does not see it (it touches no new triangle), but the move leaves it outside.
        // (Where the triangle is not in a face, the winding number of its points changes only across the old or the new fan,
        // which the end state and the input's distance to the old surface cover; inside the region, a corner or the centroid
        // is strictly inside a tetrahedron.) A bipyramid over a zigzag link, the face in the plane y = 0 exactly.
        [Fact]
        public void TryMove_SweepFaceTest_FindsAnInputTriangleInASideFace()
        {
            DMesh3 m = new DMesh3();
            int top = m.AppendVertex(new Vector3d(0, 0, 1));
            int[] l = { m.AppendVertex(new Vector3d(1, 0, 0.5)), m.AppendVertex(new Vector3d(0, 1, -0.5)),
                m.AppendVertex(new Vector3d(-1, 0, 0.5)), m.AppendVertex(new Vector3d(0, -1, -0.5)) };
            int bot = m.AppendVertex(new Vector3d(0, 0, -1.5));
            for (int k = 0; k < 4; ++k) { m.AppendTriangle(top, l[k], l[(k + 1) % 4]); m.AppendTriangle(bot, l[(k + 1) % 4], l[k]); }
            Assert.True(m.IsClosed());
            Vector3d x = new Vector3d(0.25, 0, 0.6);
            // in the face (top, l[0], x), away from its edges: inside the bipyramid, outside it after the move
            Vector3d c0 = new Vector3d(0.375, 0, 0.75), c1 = new Vector3d(0.4375, 0, 0.75), c2 = new Vector3d(0.375, 0, 0.8125);
            DMesh3 input = PolyCube(1, 1, 1, (i, j, k) => true);
            MeshTransforms.Scale(input, 0.125); MeshTransforms.Translate(input, new Vector3d(-0.0625, -0.0625, -0.75));
            int a = input.AppendVertex(c0), b = input.AppendVertex(c1), d = input.AppendVertex(c2);
            input.AppendTriangle(a, b, d);
            Vector3d cc = (c0 + c1 + c2) / 3;
            Assert.True(m.WindingNumber(cc) > 0.5, "inside before");
            DMesh3 moved = new DMesh3(m); moved.SetVertex(top, x);
            Assert.True(moved.WindingNumber(cc) < 0.5, "outside after");
            Func<bool, bool> tryMove = (sweep) => {
                var p = new AlphaWrapFeaturePass(new DMesh3(m), input, 0.5, 0.01) {
                    CheckSweep = sweep, MinAngleFloorDegrees = 0, MaxAngleCeilingDegrees = 180, MaxNormalTurnDegrees = 180, MinDistance = 0 };
                Assert.True(p.Prepare());
                return p.TryMove(top, x);
            };
            Assert.True(tryMove(false), "the end state alone accepts it");
            Assert.False(tryMove(true), "the sweep test refuses it");
        }

        // the normal-turn limit: a spike (the vertex lifted by one alpha) turns its triangles by about 45 degrees: refused at 30
        [Fact]
        public void TryMove_NormalTurnLimit()
        {
            DMesh3 box = PolyCube(10, 6, 4, (x, y, z) => true);
            MeshAlphaWrap w = Wrap(box);
            DMesh3 raw = w.Result;
            int v = raw.VertexIndices().Where(i => { Vector3d p = raw.GetVertex(i); return p.z > 4 && p.x > 3 && p.x < 7 && p.y > 2 && p.y < 4; }).First();
            Vector3d x = raw.GetVertex(v) + w.UsedAlpha * Vector3d.AxisZ;
            foreach (double turn in new[] { 30.0, 100.0 }) {
                var p = new AlphaWrapFeaturePass(new DMesh3(raw, true), box, w.UsedAlpha, w.UsedOffset) {
                    MinAngleFloorDegrees = 0, MaxAngleCeilingDegrees = 180, MaxNormalTurnDegrees = turn };
                Assert.True(p.Prepare());
                Assert.Equal(turn > 50, p.TryMove(v, x));
            }
        }

        // the input's orientation does not matter (a soup can come in any): the L-shape reversed, and with every other triangle
        // reversed, gives the same pass on the same wrap, bit for bit (both miter sides, faces by their consistent orientation)
        [Fact]
        public void FeaturePass_InputOrientation_DoesNotMatter()
        {
            DMesh3 input = LShape();
            MeshAlphaWrap w = Wrap(input);
            var p0 = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(p0.Compute());
            string h0 = AlphaWrapManifoldStepTests.Hash(p0.Mesh);
            DMesh3 reversed = new DMesh3(input);
            reversed.ReverseOrientation();
            DMesh3 mixed = new DMesh3();
            foreach (int v in input.VertexIndices()) mixed.AppendVertex(input.GetVertex(v));
            foreach (int t in input.TriangleIndices()) {
                Index3i tv = input.GetTriangle(t);
                mixed.AppendTriangle(t % 2 == 1 ? new Index3i(tv.a, tv.c, tv.b) : tv);
            }
            foreach (DMesh3 m in new[] { reversed, mixed }) {
                var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), m, w.UsedAlpha, w.UsedOffset);
                Assert.True(p.Compute());
                Assert.Equal(h0, AlphaWrapManifoldStepTests.Hash(p.Mesh));
            }
        }

        // crease points only near the input's sharp edges: a thin cylinder next to a box (its neighbouring vertex planes differ by
        // more than 30 degrees, but it has no sharp edge) gets no crease, while the box's edges are recovered
        [Fact]
        public void FeaturePass_NoCreasesOnSmoothParts()
        {
            DMesh3 input = PolyCube(10, 6, 4, (x, y, z) => true);
            DMesh3 cyl = new CappedCylinderGenerator() { BaseRadius = 0.25f, TopRadius = 0.25f, Height = 4, Slices = 64 }.Generate().MakeDMesh();
            // (the caps' rims are sharp edges: the part away from them is checked)
            MeshTransforms.Translate(cyl, new Vector3d(14, 0, 3));
            AxisAlignedBox3d cb = cyl.CachedBounds;
            MeshEditor.Append(input, cyl);
            MeshAlphaWrap w = Wrap(input);
            var p = new AlphaWrapFeaturePass(new DMesh3(w.Result, true), input, w.UsedAlpha, w.UsedOffset);
            Assert.True(p.Compute());
            Assert.True(p.CreaseSnaps > 0);
            Func<DMesh3, int> sharpNearCylinder = (m) => {
                int n = 0;
                foreach (int eid in m.EdgeIndices()) {
                    Index2i et = m.GetEdgeT(eid);
                    if (m.GetTriNormal(et.a).Dot(m.GetTriNormal(et.b)) > Math.Cos(45 * MathUtil.Deg2Rad)) continue;
                    Index2i ev = m.GetEdgeV(eid);
                    Vector3d mid = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
                    // (the middle of the cylinder, along its axis y, away from the caps)
                    if (mid.x > 12 && mid.y > cb.Min.y + 1 && mid.y < cb.Max.y - 1) n++;
                }
                return n;
            };
            // (the wrap itself may have a few; the pass adds none there)
            Assert.Equal(sharpNearCylinder(w.Result), sharpNearCylinder(p.Mesh));
        }
    }
}
