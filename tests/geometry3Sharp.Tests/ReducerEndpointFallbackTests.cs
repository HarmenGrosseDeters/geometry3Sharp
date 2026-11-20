using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.FastCollapseEndpointFallback: an edge whose midpoint collapse is refused is tried again as a collapse onto
    /// one of its ends (the end of the larger angle defect first). Off by default and then bit for bit the old pass; on, every
    /// check applies to the end position, so the certified bounds hold; it keeps features the midpoint cuts and reaches fewer
    /// triangles.
    /// </summary>
    public class ReducerEndpointFallbackTests
    {
        // a sphere with three narrow spikes of height 0.3 (a feature thinner than the tolerance)
        static DMesh3 Spiky(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            var dirs = new[] { new Vector3d(1, 0.2, 0.1).Normalized, new Vector3d(-0.3, 1, 0.2).Normalized, new Vector3d(0.1, -0.4, -1).Normalized };
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v).Normalized;
                double s = 1;
                foreach (Vector3d d in dirs) { double c = p.Dot(d); if (c > 0.99) s += 0.3 * (c - 0.99) / 0.01; }
                m.SetVertex(v, p * s);
            }
            return m;
        }

        static DMesh3 Bumpy(int n)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = n }.Generate().MakeDMesh();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                m.SetVertex(v, p * (1 + 0.1 * Math.Sin(5 * p.x + 1) * Math.Cos(4 * p.y - 0.5) + 0.05 * Math.Sin(9 * p.z)));
            }
            return m;
        }

        // a flat n x n grid with one narrow tower (a spike of the given height) at the middle vertex
        static DMesh3 Tower(int n, double height, out int tip)
        {
            var m = new DMesh3();
            int[,] ids = new int[n + 1, n + 1];
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d((double)i / n, (double)j / n, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            tip = ids[n / 2, n / 2];
            m.SetVertex(tip, new Vector3d(0.5, 0.5, height));
            return m;
        }

        static DMesh3 Pass(DMesh3 input, double eps, bool fallback, double len, out Reducer r, Action<Reducer> configure = null, bool twoSided = true)
        {
            var m = new DMesh3(input);
            r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = twoSided, FastCollapseEndpointFallback = fallback };
            configure?.Invoke(r);
            r.FastCollapsePass(len, 5);
            return m;
        }

        static DMesh3 Pass(DMesh3 input, double eps, bool fallback, double len) { return Pass(input, eps, fallback, len, out Reducer _); }

        static void AssertBounds(DMesh3 input, DMesh3 result, double eps, string what)
        {
            double tol = 1e-3 * eps;
            var back = new MeshHausdorffDistance(input, result) { Symmetric = false, Tolerance = tol };
            var fwd = new MeshHausdorffDistance(result, input) { Symmetric = false, Tolerance = tol };
            Assert.True(back.Compute() && fwd.Compute());
            Assert.True(back.UpperBound <= eps + tol, $"{what}: h(input, result) <= {back.UpperBound / eps} eps");
            Assert.True(fwd.UpperBound <= eps + tol, $"{what}: h(result, input) <= {fwd.UpperBound / eps} eps");
            Assert.True(result.CheckValidity(eFailMode: FailMode.ReturnOnly), what);
            foreach (int v in result.VertexIndices()) Assert.True(result.GetVertex(v).IsFinite, what);
            if (input.IsClosed())
                Assert.Equal("", MeshRobustRepair.NotCleanReason(result));
        }

        static void AssertSame(DMesh3 a, DMesh3 b)
        {
            Assert.Equal(a.TriangleCount, b.TriangleCount);
            Assert.Equal(a.MaxVertexID, b.MaxVertexID);
            foreach (int v in a.VertexIndices()) Assert.Equal(a.GetVertex(v), b.GetVertex(v));
            foreach (int t in a.TriangleIndices()) Assert.Equal(a.GetTriangle(t), b.GetTriangle(t));
        }

        static string Digest(DMesh3 m)
        {
            var bytes = new List<byte>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                bytes.AddRange(BitConverter.GetBytes(v));
                bytes.AddRange(BitConverter.GetBytes(p.x)); bytes.AddRange(BitConverter.GetBytes(p.y)); bytes.AddRange(BitConverter.GetBytes(p.z));
            }
            foreach (int t in m.TriangleIndices()) {
                Index3i tv = m.GetTriangle(t);
                bytes.AddRange(BitConverter.GetBytes(t));
                bytes.AddRange(BitConverter.GetBytes(tv.a)); bytes.AddRange(BitConverter.GetBytes(tv.b)); bytes.AddRange(BitConverter.GetBytes(tv.c));
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").Substring(0, 16);
        }

        /// <summary> off (the default): the old pass bit for bit (digests of the pass before the option existed), and an explicit false is the default </summary>
        [Fact]
        public void Off_IsTheOldPass_BitForBit()
        {
            DMesh3 input = Spiky(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            var def = new DMesh3(input);
            new Reducer(def) { MaxDeviation = eps, MaxDeviationTwoSided = true }.FastCollapsePass(0.05 * diag, 5);
            Assert.False(new Reducer(new DMesh3(input)).FastCollapseEndpointFallback);
            AssertSame(def, Pass(input, eps, false, 0.05 * diag));
            Assert.Equal("@SPIKY2@", Digest(def));
            var one = new DMesh3(input);
            new Reducer(one) { MaxDeviation = eps }.FastCollapsePass(0.05 * diag, 5);
            Assert.Equal("@SPIKY1@", Digest(one));
        }

        /// <summary> the certified bounds hold in both directions on spheres with a spike / bumps, and the pass reaches fewer triangles than without </summary>
        [Theory]
        [InlineData("spiky", 16, 1e-2)]
        [InlineData("spiky", 16, 3e-3)]
        [InlineData("bumpy", 16, 1e-2)]
        public void Fallback_KeepsTheBounds_AndReachesFewerTriangles(string name, int n, double rel)
        {
            DMesh3 input = name == "spiky" ? Spiky(n) : Bumpy(n);
            double diag = input.CachedBounds.DiagonalLength, eps = rel * diag;
            DMesh3 off = Pass(input, eps, false, 0.05 * diag), on = Pass(input, eps, true, 0.05 * diag, out Reducer r);
            AssertBounds(input, on, eps, name);
            Assert.True(on.TriangleCount < off.TriangleCount, $"{on.TriangleCount} with the fallback, {off.TriangleCount} without");
            Assert.True(r.reverseCoverage.Committed > 0);
        }

        /// <summary> the fallback applies to the one-sided check as well (h(result, input) stays within eps) </summary>
        [Fact]
        public void Fallback_OneSidedDeviation()
        {
            DMesh3 input = Bumpy(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 off = Pass(input, eps, false, 0.05 * diag, out Reducer _, null, false), on = Pass(input, eps, true, 0.05 * diag, out Reducer _, null, false);
            var fwd = new MeshHausdorffDistance(on, input) { Symmetric = false, Tolerance = 1e-3 * eps };
            Assert.True(fwd.Compute());
            Assert.True(fwd.UpperBound <= eps * 1.001, $"{fwd.UpperBound / eps}");
            Assert.True(on.TriangleCount < off.TriangleCount);
            Assert.True(on.CheckValidity(eFailMode: FailMode.ReturnOnly));
        }

        /// <summary>
        /// the sharper end is tried first: on a flat grid with one tall tower the midpoint of a tower edge cuts the tip (refused), the
        /// collapse onto the base end too, the collapse onto the tip is fine. With the feature end only, the tip must survive at its
        /// place (a pass that tried the other end first would not collapse these edges, or would cut the tip)
        /// </summary>
        [Fact]
        public void FeatureEnd_IsTriedFirst_AndKeepsTheTip()
        {
            DMesh3 input = Tower(12, 0.5, out int tip);
            double diag = input.CachedBounds.DiagonalLength, eps = 0.02;
            DMesh3 off = Pass(input, eps, false, 0.5);
            DMesh3 on = Pass(input, eps, true, 0.5, out Reducer r, rr => rr.FastCollapseFallbackFeatureOnly = true);
            AssertBounds(input, on, eps, "tower, feature end only");
            Assert.True(on.GetVertex(tip) == new Vector3d(0.5, 0.5, 0.5), "the tip is kept in place");
            Assert.True(on.TriangleCount < off.TriangleCount, $"{on.TriangleCount} against {off.TriangleCount}");
            Assert.True(on.TriangleCount <= 40, $"{on.TriangleCount}");
        }

        /// <summary> a vertex fixed by a constraint is never removed or moved: a retry onto the other end must respect it </summary>
        [Fact]
        public void Fallback_RespectsVertexConstraints()
        {
            DMesh3 input = Spiky(12);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            var fixedIds = input.VertexIndices().Where(v => v % 3 == 0).ToList();
            var constraints = new MeshConstraints();
            foreach (int v in fixedIds) constraints.SetOrUpdateVertexConstraint(v, new VertexConstraint(true));
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true, FastCollapseEndpointFallback = true };
            r.SetExternalConstraints(constraints);
            r.FastCollapsePass(0.1 * diag, 5);
            foreach (int v in fixedIds) {
                Assert.True(m.IsVertex(v), $"fixed vertex {v} removed");
                Assert.Equal(input.GetVertex(v), m.GetVertex(v));
            }
            AssertBounds(input, m, eps, "constrained");
            Assert.True(m.TriangleCount < input.TriangleCount);
        }

        /// <summary> PreventSelfIntersections applies to the end position too </summary>
        [Fact]
        public void Fallback_WithPreventSelfIntersections()
        {
            DMesh3 input = Spiky(12);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 off = Pass(input, eps, false, 0.05 * diag, out Reducer _, r => r.PreventSelfIntersections = true);
            DMesh3 on = Pass(input, eps, true, 0.05 * diag, out Reducer _, r => r.PreventSelfIntersections = true);
            AssertBounds(input, on, eps, "PreventSelfIntersections");
            Assert.True(on.TriangleCount < off.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(on));
        }

        /// <summary> reuse: two passes and a quadric reduction on one Reducer stay within eps of the original </summary>
        [Fact]
        public void Fallback_Reuse_OnOneInstance()
        {
            DMesh3 input = Bumpy(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            var m = new DMesh3(input);
            var r = new Reducer(m) { MaxDeviation = eps, MaxDeviationTwoSided = true, FastCollapseEndpointFallback = true };
            r.FastCollapsePass(0.05 * diag, 5);
            int first = m.TriangleCount;
            AssertBounds(input, m, eps, "first pass");
            r.FastCollapsePass(0.1 * diag, 5);
            Assert.True(m.TriangleCount <= first);
            AssertBounds(input, m, eps, "second pass");
            r.FastCollapseEndpointFallback = false;
            r.FastCollapsePass(0.2 * diag, 5);
            r.ReduceToTriangleCount(4);
            AssertBounds(input, m, eps, "after the reduction");
        }

        /// <summary> determinism: repeated runs equal, parallel equals serial (the reverse certification of a collapse is parallel) </summary>
        [Fact]
        public void Fallback_ParallelEqualsSerial_AndRepeatable()
        {
            DMesh3 input = Spiky(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 a = Pass(input, eps, true, 0.05 * diag, out Reducer r), b = Pass(input, eps, true, 0.05 * diag), serial;
            gParallel.SerialOnThisThread = true;
            try { serial = Pass(input, eps, true, 0.05 * diag); } finally { gParallel.SerialOnThisThread = false; }
            AssertSame(a, b);
            AssertSame(a, serial);
            Assert.True(r.reverseCoverage.Certified >= 8 * (r.reverseCoverage.Committed + r.reverseCoverage.Refused), "the parallel path is taken");
        }

        /// <summary> invariance: the bounds at every similarity, the 2^k scales bit for bit </summary>
        [Fact]
        public void Fallback_Invariance()
        {
            DMesh3 input = Spiky(16);
            double diag = input.CachedBounds.DiagonalLength, eps = 1e-2 * diag;
            DMesh3 baseRes = Pass(input, eps, true, 0.05 * diag);
            Assert.True(baseRes.TriangleCount < Pass(input, eps, false, 0.05 * diag).TriangleCount);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 moved = new DMesh3(input);
                foreach (int v in moved.VertexIndices()) moved.SetVertex(v, sim.Apply(moved.GetVertex(v)));
                DMesh3 res = Pass(moved, sim.ApplyLength(eps), true, sim.ApplyLength(0.05 * diag));
                AssertBounds(moved, res, sim.ApplyLength(eps), sim.Name);
                if (sim.IsExact) {
                    Assert.Equal(baseRes.TriangleCount, res.TriangleCount);
                    foreach (int v in baseRes.VertexIndices()) Assert.Equal(sim.Apply(baseRes.GetVertex(v)), res.GetVertex(v));
                }
            }
        }
    }
}
