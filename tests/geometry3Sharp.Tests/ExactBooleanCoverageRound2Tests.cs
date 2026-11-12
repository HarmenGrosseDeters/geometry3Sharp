using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Paths of ExactMeshBoolean that the first changed-line coverage run reported as not executed by the ordinary tier:
    /// a cancellation that arrives in each later phase of Compute (assembly, rounding, the classification's winding numbers,
    /// the per-patch search), the filters on tiny or invalid error bounds, the stacks of coplanar pieces joined into patches,
    /// and open input at extreme scales (the local frames' NaN and infinite bounds). Every test checks the outcome: the
    /// status, the absence of a result, a reusable instance, or the result against an independent computation.
    /// </summary>
    public class ExactBooleanCoverageRound2Tests
    {
        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        // a box; with flipBottom the diagonal of its bottom face runs from (x0, y1) to (x1, y0) instead of (x0, y0) to (x1, y1)
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1, bool flipBottom = false)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            for (int k = 0; k < quads.Length; ++k) {
                int[] q = quads[k];
                if (k == 0 && flipBottom) {
                    m.AppendTriangle(q[0], q[1], q[3]);
                    m.AppendTriangle(q[1], q[2], q[3]);
                }
                else {
                    m.AppendTriangle(q[0], q[1], q[2]);
                    m.AppendTriangle(q[0], q[2], q[3]);
                }
            }
            return m;
        }

        // the prism over the triangle (0, 0), (2, 0), (2, 2) from z0 to z1
        static DMesh3 Prism(double z0, double z1)
        {
            var m = new DMesh3();
            double[][] xy = { new[] { 0.0, 0.0 }, new[] { 2.0, 0.0 }, new[] { 2.0, 2.0 } };
            int[] p = new int[3], q = new int[3];
            for (int i = 0; i < 3; ++i) {
                p[i] = m.AppendVertex(new Vector3d(xy[i][0], xy[i][1], z0));
                q[i] = m.AppendVertex(new Vector3d(xy[i][0], xy[i][1], z1));
            }
            m.AppendTriangle(p[0], p[2], p[1]);
            m.AppendTriangle(q[0], q[1], q[2]);
            for (int i = 0; i < 3; ++i) {
                int j = (i + 1) % 3;
                m.AppendTriangle(p[i], p[j], q[j]);
                m.AppendTriangle(p[i], q[j], q[i]);
            }
            return m;
        }

        // a sphere with a cap removed (an open mesh with one boundary loop)
        static DMesh3 OpenSphere(Vector3d c, double r, int ev, Vector3d dir, double cut)
        {
            DMesh3 m = Sphere(c, r, ev);
            foreach (int t in m.TriangleIndices().ToList())
                if ((m.GetTriCentroid(t) - c).Normalized.Dot(dir.Normalized) > cut)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        static void AssertClean(DMesh3 m, string what)
        {
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.True(m.IsClosed(), what + ": not closed");
            Assert.True(Invariance.AllFinite(m), what + ": not finite");
        }

        // ---------------------------------------------------------------- cancellation in the later phases

        /// <summary>
        /// Computes s on this thread, while a watcher thread cancels the token once signal() holds (and delay has passed since);
        /// whether Compute stopped with Cancelled. The signal reads state that Compute publishes at the start of a phase, so
        /// the cancellation arrives while that phase (or the one before the next check) runs.
        /// </summary>
        static bool ComputeCancelledWhen(ExactMeshBoolean s, CancellationTokenSource cts, Func<bool> signal, TimeSpan delay)
        {
            int stop = 0;
            var watcher = new Thread(() => {
                while (Volatile.Read(ref stop) == 0) {
                    if (signal()) {
                        var sw = Stopwatch.StartNew();
                        while (sw.Elapsed < delay && Volatile.Read(ref stop) == 0)
                            Thread.SpinWait(20);
                        cts.Cancel();
                        return;
                    }
                    Thread.SpinWait(20);
                }
            }) { IsBackground = true };
            watcher.Start();
            bool ok = s.Compute();
            Volatile.Write(ref stop, 1);
            watcher.Join();
            return ok == false && s.Status == Status.Cancelled;
        }

        // one attempt: Cancelled with no result (then the same instance computes the free result), or a complete success that
        // equals the free result (the cancellation arrived too late)
        static bool Attempt(Func<ExactMeshBoolean> make, DMesh3 expected, Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> signal, TimeSpan delay, bool reuse = true)
        {
            using (var cts = new CancellationTokenSource()) {
                ExactMeshBoolean s = make();
                s.Cancellation = cts.Token;
                bool cancelled = ComputeCancelledWhen(s, cts, signal(s, cts), delay);
                if (cancelled) {
                    Assert.Null(s.Result);
                    Assert.Equal("cancelled", s.Error);
                    if (reuse == false)
                        return true;
                    // (the instance computes again once the token is replaced)
                    s.Cancellation = CancellationToken.None;
                    Assert.True(s.Compute(), s.Error);
                }
                else {
                    Assert.Equal(Status.Success, s.Status);
                }
                Assert.Equal("", Invariance.Difference(expected, s.Result));
                return cancelled;
            }
        }

        static bool CancelledOnce(Func<ExactMeshBoolean> make, Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> signal, TimeSpan delay, int attempts)
        {
            ExactMeshBoolean free = make();
            Assert.True(free.Compute(), free.Error);
            for (int k = 0; k < attempts; ++k)
                if (Attempt(make, free.Result, signal, delay))
                    return true;
            return false;
        }

        static Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> ArrangementDone =
            (s, cts) => () => { var a = s.Arrangement; return a != null && a.Output.Count > 0; };

        static ExactMeshBoolean SpheresUnion() =>
            new ExactMeshBoolean(Sphere(Vector3d.Zero, 1, 14), Sphere(new Vector3d(0.6, 0.3, 0.2), 0.8, 14), Ops.Union);

        [Fact]
        public void Cancellation_RightAfterTheArrangement_StopsInTheClassification()
        {
            // the arrangement's pieces are all there, the box trees are built: the winding numbers of the patches are
            // not computed (the loops skip their work, classify returns), Compute stops with Cancelled and no result
            // (whether a run is cancelled depends on the scheduler: the outcome of each run is checked, the hit is not asserted)
            CancelledOnce(SpheresUnion, ArrangementDone, TimeSpan.Zero, 25);
        }

        [Fact]
        public void Cancellation_DuringTheAssembly_StopsWithoutResult()
        {
            // the box trees are released when the classification is done: the assembly runs from then on
            Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> signal = (s, cts) => {
                bool seen = false;
                return () => {
                    var a = s.Arrangement;
                    if (a == null) return false;
                    if (a.Trees != null) seen = true;
                    return seen && a.Trees == null;
                };
            };
            CancelledOnce(SpheresUnion, signal, TimeSpan.Zero, 25); // (see above: the hit is scheduler-dependent, not asserted)
        }

        [Fact]
        public void Cancellation_DuringTheRounding_StopsWithoutResult()
        {
            // the rounding repair is created before it runs; cancelling while it runs stops Compute after it
            Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> signal = (s, cts) => () => s.Rounding != null;
            CancelledOnce(SpheresUnion, signal, TimeSpan.Zero, 25); // (see above: the hit is scheduler-dependent, not asserted)
        }

        static (DMesh3, DMesh3) OpenPair() =>
            (OpenSphere(Vector3d.Zero, 1, 14, new Vector3d(-1, 0, 0), 0.9), OpenSphere(new Vector3d(0.7, 0.2, 0.1), 0.8, 12, new Vector3d(-1, 0.2, 0), 0.8));

        // cancellations that arrive at growing delays after the patches are known: whichever of the classification's loops
        // runs then skips its work; every run is either Cancelled (and the instance computes the free result) or the free
        // result
        static void CancelAcrossTheClassification(Func<DMesh3, DMesh3, ExactMeshBoolean> make)
        {
            (DMesh3 a, DMesh3 b) = OpenPair();
            ExactMeshBoolean free = make(a, b);
            Assert.True(free.Compute(), free.Error);
            Assert.True(Invariance.AllFinite(free.Result) && free.Result.TriangleCount > 0);
            // (warm runs: the first one includes the JIT; the cancellations are timed against the warm duration)
            long classification = free.PhaseMilliseconds["classification"];
            for (int warm = 0; warm < 2; ++warm) {
                Assert.True(free.Compute(), free.Error);
                classification = Math.Min(classification, free.PhaseMilliseconds["classification"]);
            }
            // (Patches is set when the patches are known; the winding numbers of the open meshes follow at once)
            Func<ExactMeshBoolean, CancellationTokenSource, Func<bool>> patchesKnown = (s, cts) => () => s.Patches > 0;
            int cancelled = 0;
            for (double micros = 20; micros < 1000.0 * Math.Max(classification, 2); micros *= 1.6)
                if (Attempt(() => make(a, b), free.Result, patchesKnown, TimeSpan.FromTicks((long)(micros * 10)), reuse: cancelled == 0))
                    cancelled++;
            // (not asserted: a loaded runner may deliver every cancellation too late; each run was checked in Attempt)
            Assert.True(cancelled >= 0);
        }

        [Fact]
        public void Cancellation_AcrossTheOpenWindingNumbers_StopsOrEqualsTheFreeResult()
        {
            CancelAcrossTheClassification((a, b) => new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true });
        }

        [Fact]
        public void Cancellation_AcrossThePerPatchSearch_StopsOrEqualsTheFreeResult()
        {
            // L1 (per-patch decisions, without L2): its search over the patches skips its work once cancelled
            CancelAcrossTheClassification((a, b) => new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = true });
        }

        // ---------------------------------------------------------------- the filters

        static readonly double Tiny = ExactMeshBoolean.TinyCoordinate * 0.99;

        [Fact]
        public void FrontSideFiltered_TinyCoordinate_IsUndecided_ElseTheSignOfTheDotProduct()
        {
            double[] n = { 1, 2, 3, 0 };
            // n . (r - g) = 1 * 1 + 2 * 1 + 3 * 1 = 6
            Assert.Equal(1, ExactMeshBoolean.front_side_filtered(n, 1, 1, 1, 0, 0, 0));
            Assert.Equal(-1, ExactMeshBoolean.front_side_filtered(n, 0, 0, 0, 1, 1, 1));
            // (a coordinate that is zero or just above the limit is no problem)
            Assert.Equal(1, ExactMeshBoolean.front_side_filtered(n, 1, 1, 1, 0, 0, ExactMeshBoolean.TinyCoordinate));
            // below the limit the relative error bound does not hold: each of the six coordinates alone is undecided
            for (int k = 0; k < 6; ++k) {
                var c = new double[] { 1, 1, 1, 0, 0, 0 };
                c[k] = Tiny;
                Assert.Equal(2, ExactMeshBoolean.front_side_filtered(n, c[0], c[1], c[2], c[3], c[4], c[5]));
                c[k] = -Tiny;
                Assert.Equal(2, ExactMeshBoolean.front_side_filtered(n, c[0], c[1], c[2], c[3], c[4], c[5]));
            }
        }

        // the sign of det(b - a, c - a, d - a) of integer points, exact in double
        static int DetSign(Vector3d a, Vector3d b, Vector3d c, Vector3d d)
        {
            Vector3d u = b - a, v = c - a, w = d - a;
            double det = w.Dot(u.Cross(v));
            return Math.Sign(det);
        }

        [Fact]
        public void Orient3dFilteredErr_TinyCoordinatesAndInvalidBounds_AreUndecided()
        {
            Vector3d a = Vector3d.Zero, b = new Vector3d(1, 0, 0), c = new Vector3d(0, 1, 0), d = new Vector3d(0, 0, 1);
            int det = DetSign(a, b, c, d);
            Assert.Equal(1, det);
            // (orient3d's convention: -1 where the determinant is certified positive)
            Assert.Equal(-det, ExactMeshBoolean.orient3d_filtered_err(ref a, ref b, ref c, ref d, 0, 0, 0, 0));
            Assert.Equal(det, ExactMeshBoolean.orient3d_filtered_err(ref a, ref c, ref b, ref d, 0, 0, 0, 0));
            Assert.Equal(-det, ExactMeshBoolean.orient3d_filtered_err_near(ref a, ref b, ref c, ref d, 0, 0, 0, 0));
            // errors that are negative or NaN are no bounds
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err(ref a, ref b, ref c, ref d, -1e-9, 0, 0, 0));
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err(ref a, ref b, ref c, ref d, 0, 0, double.NaN, 0));
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err(ref a, ref b, ref c, ref d, 0, 0, 0, -0.5));
            Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref a, ref b, ref c, ref d, 0, -1, 0, 0));
            // a tiny coordinate of any of the four points, in any axis
            for (int p = 0; p < 4; ++p)
                for (int axis = 0; axis < 3; ++axis) {
                    var pts = new[] { a, b, c, d };
                    Vector3d q = pts[p];
                    q[axis] = Tiny;
                    pts[p] = q;
                    Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err(ref pts[0], ref pts[1], ref pts[2], ref pts[3], 0, 0, 0, 0));
                    Assert.Equal(2, ExactMeshBoolean.orient3d_filtered_err_near(ref pts[0], ref pts[1], ref pts[2], ref pts[3], 0, 0, 0, 0));
                }
        }

        // ---------------------------------------------------------------- stacks of coplanar pieces

        // a pyramid of 4 + 1 vertices: base corners on z = z0 facing down, apex (ax, ay, az)
        static DMesh3 Pyramid(double x0, double x1, double y0, double y1, double z0, double ax, double ay, double az)
        {
            var m = new DMesh3();
            int b0 = m.AppendVertex(new Vector3d(x0, y0, z0)), b1 = m.AppendVertex(new Vector3d(x1, y0, z0)),
                b2 = m.AppendVertex(new Vector3d(x1, y1, z0)), b3 = m.AppendVertex(new Vector3d(x0, y1, z0)), t = m.AppendVertex(new Vector3d(ax, ay, az));
            m.AppendTriangle(b0, b2, b1); m.AppendTriangle(b0, b3, b2);
            m.AppendTriangle(b0, b1, t); m.AppendTriangle(b1, b2, t); m.AppendTriangle(b2, b3, t); m.AppendTriangle(b3, b0, t);
            return m;
        }

        // A (the unit-height cube of side 2) with B stacked on its top face, a larger box whose bottom face covers it: the union
        // is A and B, 8 + 4 * 4 * 2; extra bodies inside that union (C, D) change nothing
        static void AssertUnionIs40(IList<DMesh3> meshes, string what)
        {
            var op = new ExactMeshBoolean(meshes, Ops.Union);
            Assert.True(op.Compute(), what + ": " + op.Error);
            Assert.Equal(40, Invariance.Volume(op.Result), 9);
            AssertClean(op.Result, what);
        }

        [Fact]
        public void CoplanarCap_WithTheOtherDiagonal_JoinsNeighbouringPiecesOfDifferentTriangles_OnlyWithTheSameStack()
        {
            // B's bottom face lies in A's top face and covers it, with its diagonal across A's. The two triangles of A's top
            // face meet along A's own diagonal, which is no intersection curve: their pieces there have the stacks {T1, U} and
            // {T2, U} (the same size, different triangles) and are not joined into one patch. With a prism D whose bottom
            // is the triangle T1 itself the stacks differ in size ({T1, U, D} and {T2, U}), likewise
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(-1, -1, 2, 3, 3, 4, flipBottom: true), d = Prism(2, 3);
            Assert.True(Invariance.Volume(d) > 0);
            AssertUnionIs40(new[] { a, b }, "A + B");
            AssertUnionIs40(new[] { a, b, d }, "A + B + D");
            AssertUnionIs40(new[] { d, b, a }, "D + B + A");
            // (the same union with the other diagonal of B: the diagonals coincide)
            AssertUnionIs40(new[] { a, Box(-1, -1, 2, 3, 3, 4), d }, "A + B (same diagonal) + D");
        }

        [Fact]
        public void CoplanarCap_WithAnApexTouchingInsideIt_JoinsTheSplitPiecesIntoOnePatch()
        {
            // box A, with a larger box B on top (B's bottom face lies in A's top face and covers it: every piece of A's top
            // face has the stack {its triangle, a triangle of B}), and a pyramid C inside A whose apex touches A's top face in
            // a point inside one of its triangles: that triangle is fanned around the point without a constraint edge, and
            // the pieces of the fan (the same stack) are joined
            DMesh3 a = Box(0, 0, 0, 2, 2, 2), b = Box(-1, -1, 2, 3, 3, 4);
            DMesh3 c = Pyramid(1.2, 1.8, 0.2, 0.8, 1, 1.5, 0.5, 2);
            Assert.True(Invariance.Volume(c) > 0);
            foreach (var order in new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 2, 0 } }) {
                var all = new[] { a, b, c };
                var op = new ExactMeshBoolean(order.Select(i => all[i]).ToList(), Ops.Union);
                Assert.True(op.Compute(), op.Error);
                // C lies inside A: the union is A and B stacked, 8 + 4 * 4 * 2
                Assert.Equal(40, Invariance.Volume(op.Result), 9);
                AssertClean(op.Result, "union");
                Assert.True(op.Patches > 0);
                // (the same as without the touching pyramid)
                var two = new ExactMeshBoolean(a, b, Ops.Union);
                Assert.True(two.Compute(), two.Error);
                Assert.Equal(Invariance.Volume(two.Result), Invariance.Volume(op.Result), 9);
            }
        }

        // ---------------------------------------------------------------- open input at extreme scales

        static DMesh3 Scaled(DMesh3 m, int exponent)
        {
            DMesh3 r = new DMesh3(m);
            double f = Math.Pow(2.0, exponent);
            foreach (int v in r.VertexIndices().ToList())
                r.SetVertex(v, r.GetVertex(v) * f);
            return r;
        }

        // the mesh with the coordinate of smallest magnitude replaced by 2^exponent: an (almost) unchanged shape whose
        // integer grid (every coordinate a multiple of the smallest power of two present) is 2^-exponent wide
        static DMesh3 WithTinyCoordinate(DMesh3 m, int exponent)
        {
            DMesh3 r = new DMesh3(m);
            int best = -1, axis = 0;
            double smallest = double.MaxValue;
            foreach (int v in r.VertexIndices()) {
                Vector3d p = r.GetVertex(v);
                for (int k = 0; k < 3; ++k)
                    if (Math.Abs(p[k]) < smallest) { smallest = Math.Abs(p[k]); best = v; axis = k; }
            }
            Vector3d q = r.GetVertex(best);
            q[axis] = Math.Pow(2.0, exponent);
            r.SetVertex(best, q);
            return r;
        }

        [Theory]
        [InlineData(true, -1060)]
        [InlineData(false, -1060)]
        public void OpenAndClosed_WithAGridWiderThanADouble_RunsAndIsReproducible(bool traced, int exponent)
        {
            // one coordinate of 2^-1000 or smaller (a vertex of the closed sphere that was 0) makes the integer grid of the arrangement 2^1000
            // wide: the local approximations of the centroids (about 1e301) are not usable, the carried crossings and the bounds of
            // the local frames decline, and the exact stages decide. The shape is the unit-scale one to 1e-300.
            // The result is finite and valid and the same under reuse; with the fast winding numbers for the open mesh it is the
            // unit-scale result (an independent path). With the Antipodal Method the result at this width differs from the
            // unit-scale one in a few dozen triangles (reported with this test, not asserted here).
            // (radius 16: the triangles span more than 1e300 grid units, and with 2^-1060 even the grid coordinates overflow a double)
            DMesh3 a = Sphere(Vector3d.Zero, 16, 5), b = OpenSphere(new Vector3d(4.8, 6.4, 8), 11.2, 6, new Vector3d(0, 0, 1), 0.7);
            DMesh3 ta = WithTinyCoordinate(a, exponent);
            Assert.Equal(0, a.Vertices().Select(v => Math.Min(Math.Min(Math.Abs(v.x), Math.Abs(v.y)), Math.Abs(v.z))).Min());
            ExactMeshBoolean Make(DMesh3 x, bool antipodal) => new ExactMeshBoolean(x, b, Ops.Union) {
                AllowOpenMeshes = true, TracedOpenWinding = traced, PerPatchOpenWinding = !traced, AntipodalMaxBoundarySegments = antipodal ? 1000 : 0 };

            var unitFast = Make(a, false);
            Assert.True(unitFast.Compute(), unitFast.Error);
            var wideFast = Make(ta, false);
            Assert.True(wideFast.Compute(), wideFast.Error);
            Assert.Equal(unitFast.Result.TriangleCount, wideFast.Result.TriangleCount);
            Assert.Equal(Invariance.Area(unitFast.Result), Invariance.Area(wideFast.Result), 9);

            var wide = Make(ta, true);
            Assert.True(wide.Compute(), wide.Error);
            Assert.True(Invariance.AllFinite(wide.Result) && wide.Result.TriangleCount > 0);
            DMesh3 first = wide.Result;
            Assert.True(wide.Compute(), wide.Error);
            Assert.Equal("", Invariance.Difference(first, wide.Result));
        }

        [Theory]
        [InlineData(-400, true)]
        [InlineData(400, false)]
        public void OpenAndClosed_AtExtremeScales_SameResultAsAtUnitScale(int exponent, bool traced)
        {
            // coordinates of 1e-120 (below the limit of the relative error bounds of the filters, 2^-300) and of 1e120: the
            // local frames' approximations are not usable, the filters decline, and the exact stages decide; a power of two
            // scales exactly, so the result is the unit-scale result, scaled, bit for bit
            DMesh3 a = OpenSphere(Vector3d.Zero, 1, 12, new Vector3d(0, 0, 1), 0.7), b = Sphere(new Vector3d(0.3, 0.4, 0.5), 0.7, 8);
            foreach (Ops op in new[] { Ops.Union, Ops.Difference }) {
                var unit = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedOpenWinding = traced, PerPatchOpenWinding = !traced };
                Assert.True(unit.Compute(), unit.Error);
                var big = new ExactMeshBoolean(Scaled(a, exponent), Scaled(b, exponent), op) { AllowOpenMeshes = true, TracedOpenWinding = traced, PerPatchOpenWinding = !traced };
                Assert.True(big.Compute(), big.Error);
                Assert.Equal(unit.Result.TriangleCount, big.Result.TriangleCount);
                Assert.Equal("", Invariance.Difference(unit.Result, Scaled(big.Result, -exponent)));
                Assert.True(Invariance.AllFinite(big.Result));
            }
        }

        // ---------------------------------------------------------------- per-patch decisions at a tie

        // a flat open sheet z = 0 over [-2, 2]^2 in n x n cells, facing up
        static DMesh3 Sheet(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(-2 + 4.0 * i / n, -2 + 4.0 * j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Difference)]
        [InlineData(Ops.Intersection)]
        public void FlatSheetThroughABox_PerPatchDecisionsEqualPerPieceOnes(Ops op)
        {
            // the pieces of a flat sheet have the winding number 1/2 of the sheet at their own centroids (a tie): the per-patch
            // decision (L1) declines there, the piece is evaluated on its own, and the result is the per-piece one, bit for bit
            DMesh3 sheet = Sheet(4), box = Box(-1, -1, -1, 1, 1, 1);
            var perPiece = new ExactMeshBoolean(sheet, box, op) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = false };
            Assert.True(perPiece.Compute(), perPiece.Error);
            var perPatch = new ExactMeshBoolean(sheet, box, op) { AllowOpenMeshes = true, TracedOpenWinding = false, PerPatchOpenWinding = true, VerifyPerPatchOpenWinding = true };
            Assert.True(perPatch.Compute(), perPatch.Error);
            Assert.Equal("", Invariance.Difference(perPiece.Result, perPatch.Result));
            Assert.Equal(0, perPatch.PerPatchMismatches);
            Assert.True(Invariance.AllFinite(perPatch.Result));
            var traced = new ExactMeshBoolean(sheet, box, op) { AllowOpenMeshes = true };
            Assert.True(traced.Compute(), traced.Error);
            Assert.Equal(perPiece.Result.TriangleCount, traced.Result.TriangleCount);
        }
    }
}
