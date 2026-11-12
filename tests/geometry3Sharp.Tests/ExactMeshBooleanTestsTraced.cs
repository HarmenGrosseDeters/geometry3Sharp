using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// L2 of the tail case (docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 3): within each input triangle the open meshes
    /// on the Antipodal path are evaluated with one ray at the first piece and with that ray's far point at the others,
    /// their crossing counts carried across the pieces by exact tests. Every traced value is an Antipodal evaluation with
    /// a different ray, so its threshold can differ from the piece's own evaluation only where the value lies within the
    /// error bound of a half-integer; the A/B (verify mode) counts the differences and their margins. (The class name keeps
    /// the mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsTraced
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

        public static IEnumerable<object[]> Cases()
        {
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                yield return new object[] { "knot", op };
                yield return new object[] { "open spheres", op };
                yield return new object[] { "near a hole", op };
                yield return new object[] { "duplicate triangles", op };
            }
        }

        internal static (DMesh3, DMesh3) Inputs(string name)
        {
            switch (name) {
                case "knot": return ExactMeshBooleanTestsBudget.KnotPair(120, 6, 10, 0.35, 5);
                case "open spheres": return (OpenSphere(Vector3d.Zero, 1, 14, new Vector3d(-1, 0, 0), 0.9), OpenSphere(new Vector3d(0.7, 0.2, 0.1), 0.8, 12, new Vector3d(-1, 0.2, 0), 0.8));
                // (a closed sphere cutting the open one next to its hole: the traced triangles see the boundary near)
                case "near a hole": return (OpenSphere(Vector3d.Zero, 1, 20, new Vector3d(0, 0, 1), 0.3), Sphere(new Vector3d(0.75, 0, 0.45), 0.35, 14));
                case "duplicate triangles": {
                    // triangles of the open sphere doubled (copies on their own vertices): a triangle of the other sphere
                    // crossed by both copies keeps one of their two equal segments (the unique segments), so candidates
                    // taken from its recorded segments would miss a copy
                    DMesh3 a = OpenSphere(Vector3d.Zero, 1, 12, new Vector3d(-1, 0, 0), 0.6);
                    foreach (int t in a.TriangleIndices().Where(t => a.GetTriCentroid(t).x > 0.5).ToList()) {
                        Index3i f = a.GetTriangle(t);
                        int v0 = a.AppendVertex(a.GetVertex(f.a)), v1 = a.AppendVertex(a.GetVertex(f.b)), v2 = a.AppendVertex(a.GetVertex(f.c));
                        a.AppendTriangle(v0, v1, v2);
                    }
                    return (a, Sphere(new Vector3d(0.9, 0.1, 0.05), 0.35, 10));
                }
            }
            throw new ArgumentException(name);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, bool traced, bool perPatch, bool verify = false)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, TracedOpenWinding = traced, PerPatchOpenWinding = perPatch,
                VerifyTracedOpenWinding = verify, RecordOpenWinding = true };
            Assert.True(s.Compute(), s.Error);
            return s;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Traced_SameThresholdsAsOwnRays_ExceptWithinTheBoundOfAHalfInteger(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean own = Computed(a, b, op, false, false);
            ExactMeshBoolean traced = Computed(a, b, op, true, false, verify: true);
            // (every traced value against the piece's own: a difference only within 2e-6 of a half-integer, none here)
            // (-1: the piece's own evaluation undecided or snapped)
            Assert.All(traced.TracedMismatchMargins, (m) => Assert.True(m <= 2e-6, $"margin {m}"));
            Assert.Equal(0, traced.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, traced.Result));
            for (int i = 0; i < own.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(own.OpenWindingPerPiece[i], traced.OpenWindingPerPiece[i]);
            // it traces (most pieces are not their triangle's first one)
            Assert.True(traced.Counts["l2_traced"] + traced.Counts["l2_carried"] > traced.Counts["l2_direct"], $"{traced.Counts["l2_traced"]} traced, {traced.Counts["l2_carried"]} carried, {traced.Counts["l2_direct"]} direct");
            // with L1 as well: the same result (not on the knot, the longest case, to keep the test within its time;
            // the other three cases cover it)
            if (name != "knot") {
                ExactMeshBoolean both = Computed(a, b, op, true, true, verify: true);
                Assert.Equal(0, both.TracedMismatches);
                Assert.Equal("", Invariance.Difference(own.Result, both.Result));
            }
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void Traced_SameAsOwnRays_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Inputs("open spheres");
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean own = Computed(ta, tb, Ops.Union, false, false), traced = Computed(ta, tb, Ops.Union, true, false, verify: true);
            Assert.Equal(0, traced.TracedMismatches);
            Assert.Equal("", Invariance.Difference(own.Result, traced.Result));
            if (sim.IsExact) {
                ExactMeshBoolean plain = Computed(a, b, Ops.Union, true, false);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(traced.Result, sim)));
            }
        }

        [Fact]
        public void Traced_SerialEqualsParallel_RepeatedAndReused()
        {
            (DMesh3 a, DMesh3 b) = Inputs("knot");
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, Ops.Difference, true, false);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute());
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                foreach (string c in new[] { "l2_traced", "l2_carried", "l2_direct" })
                    Assert.Equal(serial.Counts[c], reused.Counts[c]);
            }
            // pinned: how much L2 traces, carries and evaluates directly on this knot (the tracing order, the carry bound and
            // the fallbacks change it; the results do not depend on it, the verify mode checks each value)
            Assert.Equal(14179, serial.Counts["l2_traced"]);
            Assert.Equal(55097, serial.Counts["l2_carried"]);
            Assert.Equal(5054, serial.Counts["l2_direct"]);
        }
    }
}
