using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;
using Status = g3.ExactMeshBoolean.BooleanStatus;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// L1 of the tail case (docs/design/EXACT_BOOLEAN_TAIL_55928.md, section 2): the open meshes on the Antipodal path are
    /// decided per patch where the Biot-Savart bound proves that every piece of the patch has the threshold of its first
    /// piece. The results must be bit-identical to the per-piece classification, and every piece decided per patch must
    /// have the threshold its own evaluation gives (the verify mode counts the differences). (The class name keeps the
    /// mutation shards' test filter, FullyQualifiedName~ExactMeshBooleanTests.)
    /// </summary>
    public class ExactMeshBooleanTestsPerPatch
    {
        static DMesh3 Sphere(Vector3d c, double r, int ev)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = ev }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        // a sphere with a cap removed (an open mesh with one boundary loop), the hole facing dir
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
                yield return new object[] { "open and closed", op };
            }
        }

        static (DMesh3, DMesh3) Inputs(string name)
        {
            switch (name) {
                case "knot": return ExactMeshBooleanTestsBudget.KnotPair(120, 6, 10, 0.35, 5);
                // (two open spheres, the holes far from the other one and near it)
                case "open spheres": return (OpenSphere(Vector3d.Zero, 1, 14, new Vector3d(-1, 0, 0), 0.9), OpenSphere(new Vector3d(0.7, 0.2, 0.1), 0.8, 12, new Vector3d(-1, 0.2, 0), 0.8));
                case "open and closed": return (OpenSphere(Vector3d.Zero, 1, 14, new Vector3d(0, 0, 1), 0.7), Sphere(new Vector3d(0.3, 0.4, 0.5), 0.7, 10));
            }
            throw new ArgumentException(name);
        }

        static ExactMeshBoolean Computed(DMesh3 a, DMesh3 b, Ops op, bool perPatch, bool verify = false)
        {
            var s = new ExactMeshBoolean(a, b, op) { AllowOpenMeshes = true, PerPatchOpenWinding = perPatch, VerifyPerPatchOpenWinding = verify, RecordOpenWinding = true };
            Assert.True(s.Compute(), s.Error);
            return s;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void PerPatch_SameResultAsPerPiece_AndEveryDecidedPieceAgrees(string name, Ops op)
        {
            (DMesh3 a, DMesh3 b) = Inputs(name);
            ExactMeshBoolean piece = Computed(a, b, op, false), patch = Computed(a, b, op, true, verify: true);
            Assert.Equal("", Invariance.Difference(piece.Result, patch.Result));
            Assert.Equal(0, patch.PerPatchMismatches);
            // the recorded open winding numbers per piece are the same
            Assert.Equal(piece.OpenWindingPerPiece.Length, patch.OpenWindingPerPiece.Length);
            for (int i = 0; i < piece.OpenWindingPerPiece.Length; ++i)
                Assert.Equal(piece.OpenWindingPerPiece[i], patch.OpenWindingPerPiece[i]);
            // L1 decides patches here (it is not a no-op), and saves evaluations
            Assert.True(patch.Counts["l1_patch_decided"] > 0, name);
            Assert.True(patch.Counts["l1_evaluations_saved"] > 0, name);
            Assert.Equal(0, piece.Counts["l1_patch_decided"]);
            Assert.True(Invariance.AllFinite(patch.Result));
        }

        [Fact]
        public void PerPatch_NearTheBoundary_LeftPerPiece()
        {
            // a patch next to a hole of the open mesh: its box comes within 1e-3 of the size of a boundary segment, or the
            // bound is too large, so it is evaluated per piece; the far patches are decided. A sphere through the hole of
            // an open sphere
            DMesh3 a = OpenSphere(Vector3d.Zero, 1, 16, new Vector3d(1, 0, 0), 0.6);
            DMesh3 b = Sphere(new Vector3d(1, 0, 0), 0.5, 10);
            ExactMeshBoolean patch = Computed(a, b, Ops.Union, true, verify: true);
            ExactMeshBoolean piece = Computed(a, b, Ops.Union, false);
            Assert.Equal("", Invariance.Difference(piece.Result, patch.Result));
            Assert.Equal(0, patch.PerPatchMismatches);
            // not every evaluation is saved: some patches stay per piece
            long open = piece.OpenWindingPerPiece.Count(w => w != null) * 1L;
            Assert.True(patch.Counts["l1_evaluations_saved"] < open, $"{patch.Counts["l1_evaluations_saved"]} of {open}");
        }

        [Theory]
        [InlineData(0.3, 0.3)]
        [InlineData(0.2, 0.35)]
        public void PerPatch_PatchThroughAHole_ThresholdChangesWithinThePatch(double cut, double radius)
        {
            // a small sphere straddling the plane of a large hole of an open sphere: its patches reach from inside the open
            // sphere (w near 1) through the hole to outside it (w near 0) without crossing it, so the threshold changes
            // within a patch, away from the boundary; only pieces whose bound keeps them from the tie take the first
            // piece's value
            Vector3d dir = new Vector3d(0, 0, 1);
            DMesh3 a = OpenSphere(Vector3d.Zero, 1, 20, dir, cut);
            DMesh3 b = Sphere(cut * dir, radius, 14);
            ExactMeshBoolean piece = Computed(a, b, Ops.Union, false);
            ExactMeshBoolean patch = Computed(a, b, Ops.Union, true, verify: true);
            Assert.Equal("", Invariance.Difference(piece.Result, patch.Result));
            Assert.Equal(0, patch.PerPatchMismatches);
            // (the case is what it claims: B's open winding thresholds for A differ among B's pieces of one patch)
            int ia = 0;
            var values = new HashSet<int>();
            for (int i = 0; i < piece.OpenWindingPerPiece.Length; ++i) {
                int[] w = piece.OpenWindingPerPiece[i];
                if (w == null || piece.Arrangement.Output[i].Mesh != 1)
                    continue;
                int v = 0;
                for (int k = 0; k < w.Length; k += 2)
                    if (w[k] == ia) v = w[k + 1];
                values.Add(v);
            }
            Assert.True(values.Count > 1, "thresholds of B's pieces: " + string.Join(",", values));
            Assert.True(patch.Counts["l1_evaluations_saved"] > 0);
        }

        [Theory]
        [MemberData(nameof(Similarity.Names), MemberType = typeof(Similarity))]
        public void PerPatch_SameAsPerPiece_UnderSimilarity(string name)
        {
            Similarity sim = Similarity.ByName(name);
            (DMesh3 a, DMesh3 b) = Inputs("open spheres");
            DMesh3 ta = Invariance.Transformed(a, sim), tb = Invariance.Transformed(b, sim);
            ExactMeshBoolean piece = Computed(ta, tb, Ops.Union, false), patch = Computed(ta, tb, Ops.Union, true, verify: true);
            Assert.Equal("", Invariance.Difference(piece.Result, patch.Result));
            Assert.Equal(0, patch.PerPatchMismatches);
            if (sim.IsExact) {
                ExactMeshBoolean plain = Computed(a, b, Ops.Union, true);
                Assert.Equal("", Invariance.Difference(plain.Result, Invariance.MappedBack(patch.Result, sim)));
            }
        }

        [Fact]
        public void PerPatch_SerialEqualsParallel_RepeatedAndReused()
        {
            (DMesh3 a, DMesh3 b) = Inputs("knot");
            ExactMeshBoolean serial;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Computed(a, b, Ops.Difference, true);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            var reused = new ExactMeshBoolean(a, b, Ops.Difference) { AllowOpenMeshes = true };
            for (int run = 0; run < 2; ++run) {
                Assert.True(reused.Compute());
                Assert.Equal("", Invariance.Difference(serial.Result, reused.Result));
                Assert.Equal(serial.Counts["l1_patch_decided"], reused.Counts["l1_patch_decided"]);
                Assert.Equal(serial.Counts["l1_evaluations_saved"], reused.Counts["l1_evaluations_saved"]);
            }
            // pinned: how much L1 decides on this knot (the margin rule, the bound and the search all change it; the
            // results do not depend on it, the verify mode checks each decision)
            Assert.Equal(3640, serial.Counts["l1_patch_decided"]);
            Assert.Equal(22887, serial.Counts["l1_evaluations_saved"]);
        }
    }
}
