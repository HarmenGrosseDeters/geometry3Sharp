using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Remesher.MaxDeviation (the band guard of docs/design/CERTIFIED_REMESHING.md section 10, item 1) and RemeshCertificate
    /// (item 2). Procedural inputs only.
    /// </summary>
    public class RemesherBandTests
    {
        // a coarse capped cylinder (16 slices): remeshing it finely with smoothing and no projection pulls the facets inward,
        // the band keeps them
        static DMesh3 Cylinder() => new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1, Slices = 16, NoSharedVertices = false }.Generate().MakeDMesh();

        static DMesh3 Remesh(DMesh3 input, double target, double band, int passes = 8, bool parallel = true, bool project = false, Action<Remesher> set = null)
        {
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { SmoothSpeedT = 0.5, EnableParallelSmooth = parallel, EnableParallelProjection = parallel, MaxDeviation = band };
            if (project) r.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(target);
            set?.Invoke(r);
            for (int k = 0; k < passes; ++k) r.BasicRemeshPass();
            lastRejected = r.DeviationRejected;
            return m;
        }

        [ThreadStatic] static int lastRejected;

        static double OneSided(DMesh3 from, DMesh3 to, double tol)
        {
            var hd = new MeshHausdorffDistance(from, to) { Symmetric = false, Tolerance = tol, CertifiedUpper = true };
            Assert.True(hd.Compute() && hd.Converged);
            return hd.UpperBound;
        }

        /// <summary>
        /// without the band the smoothing (no projection) pulls the remeshed cylinder off its input by more than the band;
        /// with it every triangle stays within the band (an independent certified Hausdorff distance), the result is closed
        /// and valid, and refusals were counted
        /// </summary>
        [Fact]
        public void Band_KeepsTheResultWithinIt()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 free = Remesh(input, 0.1, 0);
            Assert.True(OneSided(free, input, 0.01 * delta) > 2 * delta, "precondition: without the band the result leaves it");
            DMesh3 m = Remesh(input, 0.1, delta);
            Assert.True(lastRejected > 0);
            Assert.True(OneSided(m, input, 0.01 * delta) <= delta * (1 + 1e-9));
            Assert.True(m.IsClosed() && m.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(m.TriangleCount > 4 * input.TriangleCount, "it still refines");
        }

        /// <summary> with projection too (the projection moves are guarded like the smoothing moves) </summary>
        [Fact]
        public void Band_WithProjectionAndSafePreset()
        {
            DMesh3 input = Cylinder();
            double delta = 1e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = Remesh(input, 0.08, delta, 6, true, true, r => r.ApplySafePreset());
            Assert.True(OneSided(m, input, 0.01 * delta) <= delta * (1 + 1e-9));
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
        }

        /// <summary> the reference can be set: the band is measured from it, not from the start mesh </summary>
        [Fact]
        public void SetDeviationReference_IsTheSurfaceTheBandIsMeasuredFrom()
        {
            DMesh3 input = Cylinder();
            // a reference moved by at least 3 delta: the start mesh lies outside its band, so every collapse, flip and move is refused;
            // only splits are made (they are not checked: their halves lie in the old triangles), so the result is the input's
            // surface, refined
            double delta = 2e-3 * input.CachedBounds.DiagonalLength, size = input.CachedBounds.DiagonalLength;
            // (scaled about the centre: every point of the cylinder is at least 0.5 from it, so it moves by at least 3 delta)
            DMesh3 shifted = new DMesh3(input);
            Vector3d ctr = input.CachedBounds.Center;
            foreach (int v in shifted.VertexIndices()) shifted.SetVertex(v, ctr + (1 + 6 * delta) * (shifted.GetVertex(v) - ctr));
            DMesh3 m = Remesh(input, 0.1, delta, 3, true, false, r => r.SetDeviationReference(shifted));
            Assert.True(lastRejected > 0);
            Assert.True(m.TriangleCount > input.TriangleCount);
            Assert.True(OneSided(m, input, 1e-6 * size) <= 1e-6 * size, "the input's surface, only split");
            // and null goes back to the start mesh: the smoothing then moves the surface (within the band)
            DMesh3 back = Remesh(input, 0.1, delta, 3, true, false, r => { r.SetDeviationReference(shifted); r.SetDeviationReference(null); });
            double d = OneSided(back, input, 1e-3 * delta);
            Assert.True(d > 1e-6 * size && d <= delta * (1 + 1e-9), $"{d / delta}");
        }

        /// <summary>
        /// a split's new vertex that its constraint's projection put outside the band goes back to the edge midpoint (and is
        /// counted); one inside the band stays
        /// </summary>
        [Fact]
        public void SplitVertex_ProjectedOutOfTheBand_GoesBack()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { MaxDeviation = delta };
            r.SetTargetEdgeLength(0.1);
            r.SetDeviationReference(input);     // (else the reference is the mesh at the first check, taken below after the move)
            int e = m.EdgeIndices().First(x => !m.IsBoundaryEdge(x));
            Index2i ev = m.GetEdgeV(e);
            Vector3d mid = 0.5 * (m.GetVertex(ev.a) + m.GetVertex(ev.b));
            DMesh3.EdgeSplitInfo info;
            Assert.Equal(MeshResult.Ok, m.SplitEdge(e, out info));
            Vector3d nrm = m.GetTriNormal(m.VtxTrianglesItr(info.vNew).First());
            m.SetVertex(info.vNew, m.GetVertex(info.vNew) + 5 * delta * nrm);     // (as a projection onto an offset curve would)
            r.check_split_vertex_band(info.vNew, mid);
            Assert.Equal(mid, m.GetVertex(info.vNew));
            Assert.Equal(1, r.DeviationRejected);
            m.SetVertex(info.vNew, mid + 0.1 * delta * nrm);
            r.check_split_vertex_band(info.vNew, mid);
            Assert.Equal(mid + 0.1 * delta * nrm, m.GetVertex(info.vNew));
            Assert.Equal(1, r.DeviationRejected);
        }

        // a projection target that pushes every point off by a fixed vector (as a constraint curve beside the surface would)
        sealed class OffsetTarget : IProjectionTarget
        {
            readonly Vector3d off;
            public OffsetTarget(Vector3d off) { this.off = off; }
            public Vector3d Project(Vector3d p, int id = -1) => p + off;
        }

        /// <summary>
        /// the wiring: in a split pass, edges whose constraint projects the new vertex 5 delta off the surface keep it at the
        /// midpoint (the result stays in the band); without the band the vertices go off
        /// </summary>
        [Fact]
        public void SplitPass_ProjectionOffTheBand_KeptAtTheMidpoint()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 run(double band)
            {
                DMesh3 m = new DMesh3(input);
                var r = new Remesher(m) { MaxDeviation = band, EnableCollapses = false, EnableFlips = false, EnableSmoothing = false };
                var c = new MeshConstraints();
                var target = new OffsetTarget(new Vector3d(0, 0, 5 * delta));
                foreach (int e in m.EdgeIndices()) c.SetOrUpdateEdgeConstraint(e, new EdgeConstraint(EdgeRefineFlags.NoFlip, target));
                // (the new vertex is projected only if both ends share the edge's target)
                foreach (int v in m.VertexIndices()) c.SetOrUpdateVertexConstraint(v, new VertexConstraint(target));
                r.SetExternalConstraints(c);
                r.SetTargetEdgeLength(0.15);
                r.BasicRemeshPass();
                return m;
            }
            DMesh3 free = run(0);
            Assert.True(OneSided(free, input, 0.01 * delta) > 2 * delta, "precondition: the projection moves the new vertices off");
            DMesh3 banded = run(delta);
            Assert.True(banded.TriangleCount > input.TriangleCount);
            Assert.True(OneSided(banded, input, 0.01 * delta) <= delta * (1 + 1e-9));
        }

        /// <summary>
        /// the projection pass is guarded with the band alone (no preset, no other guard flag): a projection target 5 delta
        /// off the surface moves no vertex out of the band; without the band it moves them all
        /// </summary>
        [Fact]
        public void ProjectionPass_BandAlone_IsGuarded()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 run(double band)
            {
                DMesh3 m = new DMesh3(input);
                var r = new Remesher(m) { MaxDeviation = band, EnableSmoothing = false };
                r.SetProjectionTarget(new OffsetTarget(new Vector3d(0, 0, 5 * delta)));
                r.SetTargetEdgeLength(0.15);
                r.BasicRemeshPass();
                lastRejected = r.DeviationRejected;
                return m;
            }
            Assert.True(OneSided(run(0), input, 0.01 * delta) > 2 * delta, "precondition: the projection moves the mesh off");
            DMesh3 banded = run(delta);
            Assert.True(lastRejected > 0);
            Assert.True(OneSided(banded, input, 0.01 * delta) <= delta * (1 + 1e-9));
            Assert.True(banded.IsClosed() && banded.CheckValidity(false, FailMode.ReturnOnly));
        }

        /// <summary> EnableSmoothInPlace is ignored with the band (the smoothing is buffered, every move checked) </summary>
        [Fact]
        public void SmoothInPlace_WithBand_StaysWithin()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 free = Remesh(input, 0.1, 0, 8, false, false, r => r.EnableSmoothInPlace = true);
            Assert.True(OneSided(free, input, 0.01 * delta) > 2 * delta, "precondition: the in-place smoothing leaves the band");
            DMesh3 m = Remesh(input, 0.1, delta, 8, false, false, r => r.EnableSmoothInPlace = true);
            Assert.True(OneSided(m, input, 0.01 * delta) <= delta * (1 + 1e-9));
            Assert.Equal("", Invariance.Difference(Remesh(input, 0.1, delta, 8, false), m));
        }

        /// <summary>
        /// the default reference is the mesh before the first operation: in a pass whose first change is a split of one cap
        /// edge whose constraint projects the new vertex 5 delta off (every other edge is only split), that vertex stays at
        /// the midpoint (a reference taken after the move would contain it)
        /// </summary>
        [Fact]
        public void DefaultReference_TakenBeforeTheFirstOperation()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { MaxDeviation = delta, EnableCollapses = false, EnableFlips = false, EnableSmoothing = false };
            var c = new MeshConstraints();
            // (along the cap's normal, the axis: a move within the cap's plane would stay in the band)
            var target = new OffsetTarget(new Vector3d(0, 5 * delta, 0));
            // a cap edge (both triangles in the cap) longer than the target: constrained to the offset target
            int e0 = m.EdgeIndices().First(e => {
                Index2i ev = m.GetEdgeV(e), et = m.GetEdgeT(e);
                return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)) > 0.3
                    && Math.Abs(m.GetTriNormal(et.a).y) > 0.9 && Math.Abs(m.GetTriNormal(et.b).y) > 0.9;
            });
            Index2i v0 = m.GetEdgeV(e0);
            c.SetOrUpdateEdgeConstraint(e0, new EdgeConstraint(EdgeRefineFlags.NoFlip, target));
            c.SetOrUpdateVertexConstraint(v0.a, new VertexConstraint(target));
            c.SetOrUpdateVertexConstraint(v0.b, new VertexConstraint(target));
            r.SetExternalConstraints(c);
            r.SetTargetEdgeLength(0.15);
            r.BasicRemeshPass();
            Assert.True(r.DeviationRejected > 0);
            Assert.True(OneSided(m, input, 0.01 * delta) <= delta * (1 + 1e-9));
        }

        /// <summary> off by default: the remesher's results are the same as before, bit for bit, and nothing is counted </summary>
        [Fact]
        public void Off_ByDefault_NoChange()
        {
            DMesh3 input = Cylinder();
            DMesh3 a = Remesh(input, 0.1, 0);
            Assert.Equal(0, lastRejected);
            DMesh3 b = new DMesh3(input);
            var r = new Remesher(b) { SmoothSpeedT = 0.5 };
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(0.1);
            for (int k = 0; k < 8; ++k) r.BasicRemeshPass();
            Assert.Equal("", Invariance.Difference(a, b));
            Assert.Equal(0.0, new Remesher(new DMesh3(input)).MaxDeviation);
        }

        // the vertices (bits of the coordinates) and triangles of a mesh, in id order, hashed
        static string Fingerprint(DMesh3 m)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) {
                var bytes = new List<byte>();
                foreach (int v in m.VertexIndices()) {
                    Vector3d p = m.GetVertex(v);
                    bytes.AddRange(BitConverter.GetBytes(v));
                    bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(p.x)));
                    bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(p.y)));
                    bytes.AddRange(BitConverter.GetBytes(BitConverter.DoubleToInt64Bits(p.z)));
                }
                foreach (int t in m.TriangleIndices()) {
                    Index3i tv = m.GetTriangle(t);
                    bytes.AddRange(BitConverter.GetBytes(t)); bytes.AddRange(BitConverter.GetBytes(tv.a));
                    bytes.AddRange(BitConverter.GetBytes(tv.b)); bytes.AddRange(BitConverter.GetBytes(tv.c));
                }
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
            }
        }

        /// <summary>
        /// the off path (MaxDeviation = 0, the default) is master's, bit for bit: pinned fingerprints of three remeshes (plain;
        /// projection and the safe preset; RemesherPro), the same on master and on this branch
        /// </summary>
        [Fact]
        public void OffPath_Fingerprints_AsOnMaster()
        {
#if NETFRAMEWORK
            // (pinned on net10.0, where they were taken on master; .NET Framework's Math may round differently in the
            // feature detection and the projection, and the off path is the same code on every target)
            return;
#else
            DMesh3 input = Cylinder();
            Assert.Equal("BB7EAD3227870DAE", Fingerprint(Remesh(input, 0.1, 0, 6)));
            Assert.Equal("43677C0567C712C6", Fingerprint(Remesh(input, 0.1, 0, 6, true, true, r => r.ApplySafePreset())));
            DMesh3 m = new DMesh3(input);
            var pro = new RemesherPro(m) { SmoothSpeedT = 0.5 };
            pro.SetProjectionTarget(MeshProjectionTarget.Auto(input));
            MeshConstraintUtil.PreserveBoundaryAndFeatures(pro, 30);
            pro.SetTargetEdgeLength(0.1);
            pro.FastestRemesh(10);
            Assert.Equal("51F2225920DC73BB", Fingerprint(m));
#endif
        }

        /// <summary> the parallel buffered moves give the serial result, and repeated runs the same </summary>
        [Fact]
        public void Determinism_ParallelEqualsSerial_Repeated()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 par = Remesh(input, 0.1, delta, 6, true);
            DMesh3 ser = Remesh(input, 0.1, delta, 6, false);
            Assert.Equal("", Invariance.Difference(par, ser));
            Assert.Equal("", Invariance.Difference(par, Remesh(input, 0.1, delta, 6, true)));
        }

        /// <summary>
        /// invariance over Similarity.All(): at the 2^k scales the result is the same bit for bit (mapped back); in every frame it
        /// is within the band and closed
        /// </summary>
        [Fact]
        public void Invariance_OverSimilarities()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m0 = Remesh(input, 0.1, delta, 4);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 inS = Invariance.Transformed(input, s);
                DMesh3 m = Remesh(inS, s.ApplyLength(0.1), s.ApplyLength(delta), 4);
                if (s.IsExact) Assert.Equal("", Invariance.Difference(m0, Invariance.MappedBack(m, s)));
                Assert.True(m.IsClosed() && Invariance.AllFinite(m), s.Name);
                Assert.True(OneSided(m, inS, s.ApplyLength(0.01 * delta)) <= s.ApplyLength(delta) * (1 + 1e-6), s.Name);
            }
        }

        /// <summary>
        /// a second run of passes on the same remesher keeps the band of the first reference (the mesh at the first pass), and
        /// changing MaxDeviation takes effect
        /// </summary>
        [Fact]
        public void Reuse_SecondRunKeepsTheReference()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = new DMesh3(input);
            var r = new Remesher(m) { SmoothSpeedT = 0.5, MaxDeviation = delta };
            MeshConstraintUtil.PreserveBoundaryAndFeatures(r, 30);
            r.SetTargetEdgeLength(0.1);
            for (int k = 0; k < 4; ++k) r.BasicRemeshPass();
            for (int k = 0; k < 4; ++k) r.BasicRemeshPass();
            Assert.True(OneSided(m, input, 0.01 * delta) <= delta * (1 + 1e-9), "measured from the input, not from the mesh after the first run");
            // (the buffered rounds keep no state across the runs: 4 + 4 passes on one instance are 8 passes on a fresh one)
            Assert.Equal("", Invariance.Difference(Remesh(input, 0.1, delta, 8), m));
            r.MaxDeviation = 0.5 * delta;
            int before = r.DeviationRejected;
            for (int k = 0; k < 2; ++k) r.BasicRemeshPass();
            Assert.True(r.DeviationRejected > before);
        }

        // ---------------- RemeshCertificate ----------------

        static DMesh3 Box(Vector3d c, double e, bool inward = false)
        {
            DMesh3 b = new TrivialBox3Generator() { Box = new Box3d(c, new Vector3d(e, e, e)), NoSharedVertices = false }.Generate().MakeDMesh();
            if (inward) b.ReverseOrientation(false);
            return b;
        }

        static DMesh3 Torus(double R, double r, int nu, int nv)
        {
            var m = new DMesh3();
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    double u = 2 * Math.PI * i / nu, v = 2 * Math.PI * j / nv;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            int id(int i, int j) => ((i % nu) * nv) + (j % nv);
            for (int i = 0; i < nu; ++i)
                for (int j = 0; j < nv; ++j) {
                    m.AppendTriangle(id(i, j), id(i + 1, j), id(i + 1, j + 1));
                    m.AppendTriangle(id(i, j), id(i + 1, j + 1), id(i, j + 1));
                }
            return m;
        }

        static DMesh3 Union(params DMesh3[] parts)
        {
            var m = new DMesh3();
            var ed = new MeshEditor(m);
            foreach (DMesh3 p in parts) ed.AppendMesh(p);
            return m;
        }

        /// <summary> a clean remesh inside the band passes, every field as expected </summary>
        [Fact]
        public void Certificate_PassesOnACleanRemesh()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = Remesh(input, 0.1, delta, 6, true, true, r => r.ApplySafePreset());
            var c = RemeshCertificate.Compute(input, m, 2 * delta);
            Assert.True(c.Passed, $"{c.NotCleanReason} {c.SelfIntersectingPairs} {c.HausdorffResultToInput / delta} {c.HausdorffInputToResult / delta} {c.TopologyChanges} {c.NestingPreserved}");
            Assert.True(c.Closed && c.Manifold && c.WithinBand && c.NestingPreserved && c.HausdorffConverged);
            Assert.Equal(0, c.SelfIntersectingPairs);
            Assert.Equal(1, c.ComponentsBefore); Assert.Equal(1, c.ComponentsAfter);
            Assert.Equal(0, c.GenusAfter);
            Assert.True(c.HausdorffResultToInput <= delta * (1 + 1e-9));
            // without a band: Passed without WithinBand
            var nb = RemeshCertificate.Compute(input, m);
            Assert.True(nb.Passed && !nb.WithinBand);
        }

        /// <summary> each defect fails the certificate in its own field </summary>
        [Fact]
        public void Certificate_FailsOnEachDefect()
        {
            DMesh3 a = Box(Vector3d.Zero, 1), b = Box(new Vector3d(3, 0, 0), 1);
            DMesh3 two = Union(a, b);
            // a band exceeded: the same mesh moved by 0.1
            DMesh3 moved = new DMesh3(two);
            MeshTransforms.Translate(moved, new Vector3d(0.1, 0, 0));
            var cm = RemeshCertificate.Compute(two, moved, 0.05);
            Assert.False(cm.WithinBand); Assert.False(cm.Passed);
            Assert.True(RemeshCertificate.Compute(two, moved, 0.2).Passed);
            // a component lost
            var cl = RemeshCertificate.Compute(two, Union(a), 10);
            Assert.False(cl.Passed);
            // (and the input to result direction is out of the band, though the result lies on the input)
            var cl2 = RemeshCertificate.Compute(two, Union(a), 2);
            Assert.True(cl2.HausdorffResultToInput < 0.01 && cl2.HausdorffInputToResult > 2, $"{cl2.HausdorffResultToInput} {cl2.HausdorffInputToResult}");
            Assert.False(cl2.WithinBand);
            Assert.True(cl.TopologyChanges.HasFlag(MeshTopologySummary.Changes.ComponentsLost));
            // a hole: not closed
            DMesh3 open = new DMesh3(a);
            open.RemoveTriangle(open.TriangleIndices().First());
            var co = RemeshCertificate.Compute(a, open, 10);
            Assert.False(co.Closed); Assert.False(co.Passed);
            Assert.True(co.TopologyChanges.HasFlag(MeshTopologySummary.Changes.BoundaryLoopsAdded));
            // a self-intersection: the second box pushed into the first
            DMesh3 crossing = Union(a, Box(new Vector3d(1.5, 0, 0), 1));
            var cx = RemeshCertificate.Compute(two, crossing, 10);
            Assert.True(cx.SelfIntersectingPairs > 0); Assert.False(cx.Passed);
            // the nesting: a cavity (an inward box inside a box) turned outward
            DMesh3 withCavity = Union(Box(Vector3d.Zero, 2), Box(Vector3d.Zero, 1, true));
            DMesh3 flipped = Union(Box(Vector3d.Zero, 2), Box(Vector3d.Zero, 1));
            var cn = RemeshCertificate.Compute(withCavity, flipped, 10);
            Assert.False(cn.NestingPreserved); Assert.False(cn.Passed);
            Assert.True(RemeshCertificate.Compute(withCavity, withCavity, 10).Passed);
            // the nesting: a shell moved out of the other (same components, same signs, other depth)
            var cd = RemeshCertificate.Compute(Union(Box(Vector3d.Zero, 2), Box(Vector3d.Zero, 0.5)), Union(Box(Vector3d.Zero, 2), Box(new Vector3d(5, 0, 0), 0.5)), 10);
            Assert.False(cd.NestingPreserved);
            // a handle closed: a torus against a sphere-like shell of the same size
            DMesh3 torus = Torus(1, 0.3, 24, 12);
            DMesh3 disc = new Sphere3Generator_NormalizedCube() { Radius = 1.3, EdgeVertices = 6 }.Generate().MakeDMesh();
            var ch = RemeshCertificate.Compute(torus, disc, 10);
            Assert.True(ch.TopologyChanges.HasFlag(MeshTopologySummary.Changes.HandlesClosed)); Assert.False(ch.Passed);
            Assert.Equal(1, ch.GenusBefore); Assert.Equal(0, ch.GenusAfter);
            // a degenerate triangle (closed, manifold, no crossing, on the surface): a poked face's new vertex moved next to an
            // edge (1e-14 inside: area below NotCleanReason's 1e-12 diagonal^2, not zero)
            DMesh3 poked = new DMesh3(a);
            int pt = poked.TriangleIndices().First();
            Index3i ptv = poked.GetTriangle(pt);
            Vector3d pc = poked.GetVertex(ptv.c), pm = 0.5 * (poked.GetVertex(ptv.a) + poked.GetVertex(ptv.b));
            Assert.Equal(MeshResult.Ok, poked.PokeTriangle(pt, out DMesh3.PokeTriangleInfo pinfo));
            poked.SetVertex(pinfo.new_vid, pm + 1e-14 * (pc - pm));
            var cg = RemeshCertificate.Compute(a, poked, 10);
            Assert.True(cg.Closed && cg.Manifold && cg.SelfIntersectingPairs == 0 && cg.WithinBand, cg.NotCleanReason);
            Assert.True(cg.Defects.HasFlag(MeshRobustRepair.Defects.DegenerateTriangles)); Assert.False(cg.Passed);
        }

        /// <summary>
        /// an open input: its remesh keeps the boundary, and the certificate passes (NotClosed of the result is masked,
        /// the boundary loops are compared); a closed result of an open input fails, and an empty one
        /// </summary>
        [Fact]
        public void Certificate_OpenInput()
        {
            DMesh3 input = new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1, Slices = 16, NoSharedVertices = false }.Generate().MakeDMesh();
            // remove the top cap: an open tube with one boundary loop at the top... and the bottom cap kept
            foreach (int t in input.TriangleIndices().ToList()) {
                Vector3d n = input.GetTriNormal(t);
                if (n.y > 0.9) input.RemoveTriangle(t);
            }
            input = new DMesh3(input, true);
            Assert.False(input.IsClosed());
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = Remesh(input, 0.1, delta, 6, true, true, r => r.ApplySafePreset());
            var c = RemeshCertificate.Compute(input, m, 2 * delta);
            Assert.True(c.Passed, $"{c.NotCleanReason} {c.TopologyChanges} {c.HausdorffResultToInput / delta} {c.HausdorffInputToResult / delta}");
            Assert.False(c.Closed);
            Assert.Equal(MeshRobustRepair.Defects.None, c.Defects);
            Assert.Equal(1, c.BoundaryLoopsBefore); Assert.Equal(1, c.BoundaryLoopsAfter);
            // a closed result of the open input: the boundary loop lost
            var cc = RemeshCertificate.Compute(input, Cylinder(), 10);
            Assert.False(cc.Passed);
            // empty: fails
            var ce = RemeshCertificate.Compute(input, new DMesh3(), 10);
            Assert.False(ce.Passed); Assert.Equal(MeshRobustRepair.Defects.Empty, ce.Defects);
            // and a closed input still reports a hole of its result
            Assert.True(RemeshCertificate.Compute(Cylinder(), Cylinder(), 1).Passed);
            DMesh3 cyl = Cylinder();
            cyl.RemoveTriangle(cyl.TriangleIndices().First());
            var ch = RemeshCertificate.Compute(Cylinder(), cyl, 1);
            Assert.True(ch.Defects.HasFlag(MeshRobustRepair.Defects.NotClosed)); Assert.False(ch.Passed);
        }

        /// <summary> the certificate is the same at the 2^k scales (the band scaled), bit for bit </summary>
        [Fact]
        public void Certificate_ScaleInvariant()
        {
            DMesh3 input = Cylinder();
            double delta = 2e-3 * input.CachedBounds.DiagonalLength;
            DMesh3 m = Remesh(input, 0.1, delta, 4);
            var c0 = RemeshCertificate.Compute(input, m, 2 * delta);
            foreach (Similarity s in Similarity.All().Where(x => x.IsExact)) {
                var c = RemeshCertificate.Compute(Invariance.Transformed(input, s), Invariance.Transformed(m, s), s.ApplyLength(2 * delta));
                Assert.Equal(c0.Passed, c.Passed);
                Assert.Equal(c0.HausdorffResultToInput * s.Scale, c.HausdorffResultToInput);
                Assert.Equal(c0.HausdorffInputToResult * s.Scale, c.HausdorffInputToResult);
                Assert.Equal(c0.SelfIntersectingPairs, c.SelfIntersectingPairs);
                Assert.Equal(c0.NestingPreserved, c.NestingPreserved);
            }
        }
    }
}
