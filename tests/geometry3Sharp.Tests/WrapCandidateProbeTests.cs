using System;
using System.Collections.Generic;
using g3;
using MeshRepairBenchmark;
using Xunit;

namespace geometry3Sharp.Tests
{
    public class WrapCandidateProbeTests
    {
        static DMesh3 Triangle(double z)
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(0, 0, z));
            mesh.AppendVertex(new Vector3d(1, 0, z));
            mesh.AppendVertex(new Vector3d(0, 1, z));
            mesh.AppendTriangle(0, 1, 2);
            return mesh;
        }

        [Fact]
        public void Record_DoesNotInheritActualResultOrAcceptanceFields()
        {
            var actual = new Dictionary<string, object> {
                ["file"] = "procedural", ["repair_ms"] = 123L, ["status"] = "closed",
                ["apply_returned"] = true, ["after_tris"] = 7, ["fm_ok"] = true,
                ["robust_used_wrap"] = true, ["wrap_certificate_hi_over_offset"] = 0.1
            };
            var probe = WrapCandidateProbe.CreateRecord(actual);
            Assert.Equal("wrap_candidate_probe", probe["record_type"]);
            Assert.Equal(true, probe["diagnostic_only"]);
            Assert.Equal("procedural", probe["file"]);
            Assert.Equal(123L, probe["actual_repair_ms"]);
            foreach (string key in actual.Keys)
                if (key != "file") Assert.False(probe.ContainsKey(key), key);
            probe["file"] = "changed";
            Assert.Equal("procedural", actual["file"]);
        }

        [Theory]
        [InlineData(0.0, "passed", true)]
        [InlineData(0.5, "exceeded", false)]
        public void Envelope_UsesFixedDirectedBudgetAndPreservesInputs(double displacement, string expected, bool certified)
        {
            var original = Triangle(0);
            var candidate = Triangle(displacement);
            foreach (Similarity sim in Similarity.All()) {
                var reference = Invariance.Transformed(original, sim);
                var measured = Invariance.Transformed(candidate, sim);
                var referenceCopy = new DMesh3(reference);
                var measuredCopy = new DMesh3(measured);
                var first = new Dictionary<string, object>();
                var second = new Dictionary<string, object>();
                double offset = sim.ApplyLength(0.25);
                var progress = new List<string>();
                WrapCandidateProbe.MeasureEnvelope(reference, measured, offset, first);
                WrapCandidateProbe.MeasureEnvelope(reference, measured, offset, second, progress.Add);
                Assert.Equal(new[] { "candidate.triangle_certificate.begin", "candidate.triangle_certificate.end",
                    "candidate.fine_certificate.begin", "candidate.fine_certificate.end" }, progress);
                Assert.Equal(expected, first["candidate_fine_status"]);
                Assert.Equal(certified, first["candidate_triangle_cert_all"]);
                Assert.Equal(0.5 * offset, first["candidate_distance_budget"]);
                Assert.Equal(0.001 * 0.5 * offset, first["candidate_fine_tolerance"]);
                Assert.Equal(true, first["candidate_fine_computed"]);
                foreach (var entry in first) Assert.Equal(entry.Value, second[entry.Key]);
                Assert.Equal("", Invariance.Difference(referenceCopy, reference));
                Assert.Equal("", Invariance.Difference(measuredCopy, measured));
                Assert.True(reference.CheckValidity());
                Assert.True(measured.CheckValidity());
            }
        }

        [Fact]
        public void Envelope_EmptyCandidateIsNotCertified()
        {
            var result = new Dictionary<string, object>();
            WrapCandidateProbe.MeasureEnvelope(Triangle(0), new DMesh3(), 0.25, result);
            Assert.Equal(false, result["candidate_triangle_cert_all"]);
            Assert.Equal("incomplete", result["candidate_fine_status"]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Envelope_RejectsInvalidOffset(double offset)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => WrapCandidateProbe.MeasureEnvelope(
                Triangle(0), Triangle(0), offset, new Dictionary<string, object>()));
        }
    }
}
