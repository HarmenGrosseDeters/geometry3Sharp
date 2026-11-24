using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshing2Acceptance;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Verifies that the linear-time cavity earcut recovery path (Livesu et al. 2022) produces the exact same
    /// constrained Delaunay triangulation as Sloan's flip recovery on generic and degenerate cavities (#1401).
    /// </summary>
    public class ConstrainedDelaunayMesh2CavityTests
    {
        static ConstrainedTriangulation2Result ComputeWithThreshold(ConstrainedMesh2Input input, int threshold)
        {
            DelaunayKernel2.CavityEarcutThresholdForTests = threshold;
            try { return new ConstrainedDelaunayMesh2().Compute(input); }
            finally { DelaunayKernel2.CavityEarcutThresholdForTests = 0; }
        }

        static (HashSet<string> tris, HashSet<string> subs) CanonicalGeometry(ConstrainedMesh2 m)
        {
            string PtKey(int i) =>
                BitConverter.DoubleToInt64Bits(m.Vertices[i].x) + "," + BitConverter.DoubleToInt64Bits(m.Vertices[i].y);
            var tris = new HashSet<string>();
            foreach (var t in m.Triangles) {
                string[] ks = { PtKey(t.a), PtKey(t.b), PtKey(t.c) };
                Array.Sort(ks, StringComparer.Ordinal);
                tris.Add(ks[0] + "|" + ks[1] + "|" + ks[2]);
            }
            var subs = new HashSet<string>();
            foreach (var s in m.Subsegments) {
                string ka = PtKey(s.A), kb = PtKey(s.B);
                if (string.CompareOrdinal(ka, kb) > 0) (ka, kb) = (kb, ka);
                subs.Add(ka + "|" + kb + "|" + s.Marker);
            }
            return (tris, subs);
        }

        [Fact]
        public void ForcedCavityEarcut_MatchesForcedSloan_OnBundleAndDeepReflexCavity()
        {
            var inputs = new[] {
                ConstrainedDelaunayMesh2BundleTests.Bundle(80),
                ConstrainedDelaunayMesh2BundleTests.DeepReflexCavityWithHangingSpurs(100)
            };
            foreach (var input in inputs) {
                var earcut = ComputeWithThreshold(input, -1);         // force cavity earcut for all k >= 1
                var sloan = ComputeWithThreshold(input, int.MaxValue); // force half-edge Sloan for all k
                Assert.Equal(Triangulation2Status.Success, earcut.Status);
                Assert.Equal(Triangulation2Status.Success, sloan.Status);
                var (et, es) = CanonicalGeometry(earcut.Mesh);
                var (st, ss) = CanonicalGeometry(sloan.Mesh);
                Assert.Equal(st, et);
                Assert.Equal(ss, es);
            }
        }
    }
}
