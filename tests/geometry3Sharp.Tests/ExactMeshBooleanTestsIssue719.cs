using System;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #719: the Antipodal classification of open meshes (and L1 and L2's gradient bounds) squared grid coordinates in
    /// doubles. On a grid wider than about 2^500 (an input with one coordinate of 2^-500 next to coordinates of 16) the
    /// direction to the far point and the boundary segment lengths overflowed, and the Biot-Savart terms with d * d = +inf
    /// became 0: wrong winding values with a valid status, a result that depended on the coordinate span. The norms are
    /// now safe (scaled by a power of two out of range, bit-identical in it) and the far terms computed one quotient at a
    /// time. (The class name keeps the mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsIssue719
    {
        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static DMesh3 OpenSphere(Vector3d c, double r, int ev, Vector3d dir, double cut)
        {
            DMesh3 m = Sphere(c, r, ev);
            foreach (int t in m.TriangleIndices().ToList())
                if ((m.GetTriCentroid(t) - c).Normalized.Dot(dir.Normalized) > cut)
                    m.RemoveTriangle(t, true, false);
            return m;
        }

        // (#716's case: the coordinate of smallest magnitude set to 2^exponent, which only widens the grid)
        static DMesh3 WithTinyCoordinate(DMesh3 m, int exponent)
        {
            DMesh3 r = new DMesh3(m);
            int best = -1, axis = 0;
            double smallest = double.MaxValue;
            foreach (int v in r.VertexIndices()) {
                Vector3d p = r.GetVertex(v);
                for (int k = 0; k < 3; ++k)
                    if (Math.Abs(p[k]) < smallest) {
                        smallest = Math.Abs(p[k]);
                        best = v;
                        axis = k;
                    }
            }
            Vector3d q = r.GetVertex(best);
            q[axis] = Math.Pow(2.0, exponent);
            r.SetVertex(best, q);
            return r;
        }

        static (DMesh3, DMesh3) Inputs(int exponent)
        {
            DMesh3 a = Sphere(Vector3d.Zero, 16, 5), b = OpenSphere(new Vector3d(4.8, 6.4, 8), 11.2, 6, new Vector3d(0, 0, 1), 0.7);
            return (exponent == 0 ? a : WithTinyCoordinate(a, exponent), b);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, bool traced)
        {
            var s = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true, TracedOpenWinding = traced, PerPatchOpenWinding = !traced };
            Assert.True(s.Compute());
            Assert.NotNull(s.Result);
            foreach (int v in s.Result.VertexIndices())
                Assert.True(s.Result.GetVertex(v).IsFinite);
            Assert.True(s.Result.CheckValidity(true, FailMode.ReturnOnly));
            long undecided;
            Assert.False(s.Counts.TryGetValue("antipodal_undecided", out undecided) && undecided > 0, $"{undecided} undecided");
            return s;
        }

        [Theory]
        [InlineData(-500, true)]
        [InlineData(-500, false)]
        [InlineData(-600, true)]
        [InlineData(-600, false)]
        [InlineData(-1060, true)]
        [InlineData(-1060, false)]
        public void WideGrid_SameResultAsUnitSpan(int exponent, bool traced)
        {
            (DMesh3 a, DMesh3 b) = Inputs(0);
            (DMesh3 ta, DMesh3 tb) = Inputs(exponent);
            ExactMeshBoolean plain = Computed(a, b, traced), wide = Computed(ta, tb, traced);
            // (before: 408 traced and 418 per patch triangles, pieces of the open sphere kept or dropped by the span)
            Assert.Equal(plain.Result.TriangleCount, wide.Result.TriangleCount);
            Assert.Equal(Invariance.Area(plain.Result), Invariance.Area(wide.Result), 6);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void WideGrid_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Inputs(-600);
            ExactMeshBoolean plain = Computed(a, b, true);
            ExactMeshBoolean moved = Computed(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), true);
            if (sim.IsExact)
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(moved.Result, sim)));
            else {
                // (rounded: the tiny coordinate is lost in the rotation, the same result as for the rounded unit-span input)
                (DMesh3 a0, DMesh3 b0) = Inputs(0);
                ExactMeshBoolean moved0 = Computed(Invariance.Transformed(a0, sim), Invariance.Transformed(b0, sim), true);
                Assert.Equal(moved0.Result.TriangleCount, moved.Result.TriangleCount);
            }
        }

        [Fact]
        public void WideGrid_RepeatedReusedSerialEqualsParallel()
        {
            (DMesh3 a, DMesh3 b) = Inputs(-600);
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Union) { AllowOpenMeshes = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute());
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
            }
        }
    }
}
