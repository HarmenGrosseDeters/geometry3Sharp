using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// #1596 / #1597: results that are Ok (or Empty) but not what was asked for. TpmsLattice refuses it as NotResolved (no mesh) with the advice in Message (#1596);
    /// MeshOffset.Status.Empty is documented as "nothing resolved at this grid" (the repro below pins the behaviour the documentation describes).
    /// </summary>
    public class ThinPartDiagnosticsTests
    {
        static TpmsLattice Sheet(double fraction, int divisions = 16, bool enclosure = false, bool twoLevel = false, bool parallel = true)
        {
            return new TpmsLattice {
                Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, CellSize = 1, Divisions = divisions, VolumeFraction = fraction,
                Box = new AxisAlignedBox3d(Vector3d.Zero, 1), Enclosure = enclosure, TwoLevelSheet = twoLevel, ParallelCompute = parallel
            };
        }

        [Fact]
        public void EnclosureOfAThinSheet_ReportsThatItIsTheSolidBox()
        {
            // the #1596 repro: the realized fraction is above 1 (the box enlarged by the margin): refused
            var t = Sheet(0.3, enclosure: true);
            t.Compute();
            Assert.Equal(TpmsStatus.NotResolved, t.Status);
            Assert.Null(t.Mesh);
            Assert.True(t.RealizedVolumeFraction > 1, "fraction " + t.RealizedVolumeFraction);
            Assert.Contains("solid box", t.Message);
        }

        [Fact]
        public void ThinSheetWall_ReportsTheLostMaterial_AndTwoLevelSheetDoesNot()
        {
            var thin = Sheet(0.1);
            thin.Compute();
            Assert.Equal(TpmsStatus.NotResolved, thin.Status);
            Assert.Null(thin.Mesh);
            Assert.True(thin.RealizedVolumeFraction < 0.05, "fraction " + thin.RealizedVolumeFraction);
            Assert.Contains("TwoLevelSheet", thin.Message);

            var two = Sheet(0.1, twoLevel: true);
            two.Compute();
            Assert.Equal(TpmsStatus.Ok, two.Status);
            Assert.Equal("", two.Message);
            Assert.InRange(two.RealizedVolumeFraction, 0.05, 0.2);
        }

        [Fact]
        public void ResolvedSheet_HasNoMessage()
        {
            var t = Sheet(0.3);
            t.Compute();
            Assert.Equal(TpmsStatus.Ok, t.Status);
            Assert.Equal("", t.Message);
        }

        [Fact]
        public void Message_IsReusable_AndParallelEqualsSerial()
        {
            var t = Sheet(0.1);
            t.Compute();
            string first = t.Message;
            Assert.NotEqual("", first);
            t.Compute();
            Assert.Equal(first, t.Message);
            t.VolumeFraction = 0.3;   // the Message of the previous run must not stay
            t.Compute();
            Assert.Equal("", t.Message);
            var serial = Sheet(0.1, parallel: false);
            serial.Compute();
            Assert.Equal(first, serial.Message);
        }

        [Fact]
        public void Message_UnderSimilarities()
        {
            string reference = null;
            foreach (Similarity s in Similarity.All()) {
                var t = new TpmsLattice {
                    Type = TpmsType.Gyroid, Style = TpmsStyle.Sheet, VolumeFraction = 0.1, CellSize = s.Scale, Divisions = 12,
                    Box = new AxisAlignedBox3d(Vector3d.Zero, new Vector3d(1, 1, 1) * s.Scale),
                    Origin = s.Apply(Vector3d.Zero), Rotation = s.Rotation
                };
                t.Compute();
                Assert.True(t.Status == TpmsStatus.NotResolved, s.Name + ": " + t.Status + ": " + t.Message);
                if (reference == null) { reference = t.Message; Assert.NotEqual("", reference); continue; }
                if (s.IsExact) Assert.Equal(reference, t.Message);
                else Assert.NotEqual("", t.Message);
            }
        }

        [Fact]
        public void ThinInset_EmptyAtTheDefaultCell_ButThePartExists()
        {
            // the #1597 repro: the box is 1 x 0.7 x 0.5, an inset by 0.235 leaves 0.03 x 0.23 x 0.47 > 0 (the inset is shallower than half the thickness)
            DMesh3 box = new TrivialBox3Generator { Box = new Box3d(Vector3d.Zero, new Vector3d(0.5, 0.35, 0.25)) }.Generate().MakeDMesh();
            // #1597 fixed: at the default cell the nodes cannot exclude the part, so the cell is refined (MaxCellRefinements, default 2) and the part is found;
            // without refinements the answer is NotResolved (it was Empty: "nothing resolved at this grid"), never Empty for a part that exists
            var coarse = new MeshOffset(box, -0.235) { RelativeCellSize = 0.25 };
            coarse.Compute();
            Assert.Equal(MeshOffset.Status.Success, coarse.ResultStatus);
            Assert.InRange(MeshMeasure(coarse.Result), 0.0036, 0.00372);
            var first = new MeshOffset(box, -0.235) { RelativeCellSize = 0.25, MaxCellRefinements = 0 };
            first.Compute();
            Assert.Equal(MeshOffset.Status.NotResolved, first.ResultStatus);

            var fine = new MeshOffset(box, -0.235) { RelativeCellSize = 0.05 };
            fine.Compute();
            Assert.Equal(MeshOffset.Status.Success, fine.ResultStatus);
            Assert.Equal("", MeshRobustRepair.NotCleanReasons(fine.Result));
            // the exact remainder (1 - 0.47) x (0.7 - 0.47) x (0.5 - 0.47) = 0.53 x 0.23 x 0.03
            Assert.InRange(MeshMeasure(fine.Result), 0.0036, 0.00372);
        }

        static double MeshMeasure(DMesh3 m)
        {
            double v = 0;
            foreach (int tid in m.TriangleIndices()) {
                m.GetTriVertices(tid, ref a_, ref b_, ref c_);
                v += a_.Dot(b_.Cross(c_)) / 6;
            }
            return v;
        }
        [System.ThreadStatic] static Vector3d a_, b_, c_;
    }
}
