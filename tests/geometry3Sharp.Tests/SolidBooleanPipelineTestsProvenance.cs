using System;
using System.Collections.Generic;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    public partial class SolidBooleanPipelineTests
    {
        static DMesh3 TaggedCube(double x0, double y0, double z0, double x1, double y1, double z1, int groupId)
        {
            var g = new TrivialBox3Generator {
                Box = new Box3d(new Vector3d(0.5 * (x0 + x1), 0.5 * (y0 + y1), 0.5 * (z0 + z1)),
                    new Vector3d(0.5 * (x1 - x0), 0.5 * (y1 - y0), 0.5 * (z1 - z0))),
                NoSharedVertices = false
            };
            DMesh3 m = g.Generate().MakeDMesh();
            m.EnableTriangleGroups(groupId);
            foreach (int tid in m.TriangleIndices()) m.SetTriangleGroup(tid, groupId);
            return m;
        }

        static DMesh3 SubdividedTaggedSphere(Vector3d center, double radius, int subdiv, int groupId)
        {
            var g = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = subdiv };
            DMesh3 m = g.Generate().MakeDMesh();
            MeshTransforms.Translate(m, center);
            m.EnableTriangleGroups(groupId);
            foreach (int tid in m.TriangleIndices()) m.SetTriangleGroup(tid, groupId);
            return m;
        }

        [Fact]
        public void PreserveTriangleGroups_And_TrackFaceProvenance_ForwardedAndMapped()
        {
            DMesh3 a = TaggedCube(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedCube(1, 0.5, 0.5, 3, 1.5, 1.5, 20);
            DMesh3 c = TaggedCube(1.5, 0.5, 0.5, 2.5, 2.5, 1.5, 30);

            var pipe = new SolidBooleanPipeline(new[] { a, b, c }, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(pipe.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipe.Status);
            Assert.True(pipe.Report.Clean);
            Assert.True(pipe.Result.HasTriangleGroups);
            Assert.NotNull(pipe.ResultProvenance);
            Assert.Equal(pipe.Result.MaxTriangleID, pipe.ResultProvenance.Length);

            var inputs = new[] { a, b, c };
            var seenMeshes = new HashSet<int>();
            foreach (int tid in pipe.Result.TriangleIndices()) {
                TriangleProvenance tp = pipe.ResultProvenance[tid];
                Assert.True(tp.IsValid);
                Assert.InRange(tp.MeshIndex, 0, 2);
                Assert.True(inputs[tp.MeshIndex].IsTriangle(tp.SourceTriangle));
                Assert.False(tp.Reversed);
                int expectedGroup = (tp.MeshIndex + 1) * 10;
                Assert.Equal(expectedGroup, pipe.Result.GetTriangleGroup(tid));
                seenMeshes.Add(tp.MeshIndex);
            }
            Assert.Equal(new HashSet<int> { 0, 1, 2 }, seenMeshes);
        }

        [Fact]
        public void PreserveTriangleGroups_WithGroupOffsets_AndThinInputSkipped_MapsActiveIndices()
        {
            DMesh3 a = TaggedCube(0, 0, 0, 2, 2, 2, 1);
            DMesh3 thin = TaggedCube(5, 5, 5, 5.001, 6, 6, 1); // dropped by MinThickness / ThinInputs = TreatAsEmpty
            DMesh3 b = TaggedCube(1, 0, 0, 3, 2, 2, 1);

            var pipe = new SolidBooleanPipeline(new[] { a, thin, b }, ExactMeshBoolean.Operations.Union) {
                MinThickness = 0.05,
                ThinInputs = SolidBooleanPipeline.ThinInputModes.TreatAsEmpty,
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true,
                GroupOffsets = new[] { 10, 20, 30 }
            };
            Assert.True(pipe.Compute());
            Assert.True(pipe.Report.Clean);
            Assert.NotNull(pipe.ResultProvenance);

            var seenMeshes = new HashSet<int>();
            foreach (int tid in pipe.Result.TriangleIndices()) {
                TriangleProvenance tp = pipe.ResultProvenance[tid];
                Assert.True(tp.MeshIndex == 0 || tp.MeshIndex == 2, "Expected active mesh indices 0 or 2, got " + tp.MeshIndex);
                seenMeshes.Add(tp.MeshIndex);
                int expectedGroup = (tp.MeshIndex == 0 ? 10 : 30) + 1;
                Assert.Equal(expectedGroup, pipe.Result.GetTriangleGroup(tid));
            }
            Assert.Equal(new HashSet<int> { 0, 2 }, seenMeshes);
        }

        [Fact]
        public void PreserveTriangleGroups_WithReducer_PinsGroupBoundariesAndClearsProvenance()
        {
            DMesh3 s1 = SubdividedTaggedSphere(Vector3d.Zero, 1.0, 8, 100);
            DMesh3 s2 = SubdividedTaggedSphere(new Vector3d(1.1, 0, 0), 1.0, 8, 200);

            var unreduced = new SolidBooleanPipeline(s1, s2, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true
            };
            Assert.True(unreduced.Compute());
            double area100Before = 0, area200Before = 0;
            foreach (int tid in unreduced.Result.TriangleIndices()) {
                if (unreduced.Result.GetTriangleGroup(tid) == 100) area100Before += unreduced.Result.GetTriArea(tid);
                else if (unreduced.Result.GetTriangleGroup(tid) == 200) area200Before += unreduced.Result.GetTriArea(tid);
            }

            var reduced = new SolidBooleanPipeline(s1, s2, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true,
                TargetFraction = 0.5,
                PairContacts = true
            };
            Assert.True(reduced.Compute());
            Assert.True(reduced.Report.Clean);
            Assert.True(reduced.Result.HasTriangleGroups);
            Assert.True(reduced.Result.TriangleCount < unreduced.Result.TriangleCount,
                $"Expected reduction: {reduced.Result.TriangleCount} < {unreduced.Result.TriangleCount}");
            // Reduction modifies connectivity so ResultProvenance is honestly cleared.
            Assert.Null(reduced.ResultProvenance);

            double area100After = 0, area200After = 0;
            int groupBoundaryEdges = 0;
            foreach (int tid in reduced.Result.TriangleIndices()) {
                int g = reduced.Result.GetTriangleGroup(tid);
                Assert.True(g == 100 || g == 200);
                if (g == 100) area100After += reduced.Result.GetTriArea(tid);
                else area200After += reduced.Result.GetTriArea(tid);
            }
            foreach (int eid in reduced.Result.EdgeIndices())
                if (reduced.Result.IsGroupBoundaryEdge(eid))
                    groupBoundaryEdges++;
            Assert.True(groupBoundaryEdges > 0);
            Assert.InRange(area100After, area100Before * 0.98, area100Before * 1.02);
            Assert.InRange(area200After, area200Before * 0.98, area200Before * 1.02);
        }

        [Fact]
        public void PreserveTriangleGroups_WithContactsAndGroupBoundaries_ReducerDoesNotThrowAndSkipsWastedAutoPass()
        {
            // Edge-touching boxes where box1 has TWO face groups (creating real IsGroupBoundaryEdge edges!)
            // plus contacts along the shared edge (x=1, y=1). Exercises groupCons != null with PairContacts = true & Auto.
            DMesh3 box1 = TaggedCube(0, 0, 0, 1, 1, 1, 10);
            foreach (int tid in box1.TriangleIndices())
                if (box1.GetTriNormal(tid).z > 0.5)
                    box1.SetTriangleGroup(tid, 11);
            DMesh3 box2 = TaggedCube(1, 1, 0, 2, 2, 1, 20);

            var pipeOn = new SolidBooleanPipeline(box1, box2, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TargetFraction = 0.5,
                PairContacts = true
            };
            Assert.True(pipeOn.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipeOn.Status);
            Assert.True(pipeOn.Report.Clean || pipeOn.Report.OnlyContacts, pipeOn.Report.NotCleanReason);
            Assert.True(pipeOn.Result.HasTriangleGroups);

            var pipeAuto = new SolidBooleanPipeline(box1, box2, ExactMeshBoolean.Operations.Union) {
                PreserveTriangleGroups = true,
                TargetFraction = 0.5,
                PairContactsMode = SolidBooleanPipeline.PairContactsModes.Auto
            };
            Assert.True(pipeAuto.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipeAuto.Status);
            Assert.False(pipeAuto.Report.PairPassRan, "Auto pair pass must not run a duplicate reduction when group constraints disable PairCoincidentVertices.");
        }

        [Fact]
        public void TrackFaceProvenance_InputRepair_EmitsNoneForRepairedInputAndValidForCleanInput()
        {
            DMesh3 cleanWorkpiece = TaggedCube(0, 0, 0, 2, 2, 2, 10);
            DMesh3 openTool = TaggedCube(1, 1, 1, 3, 3, 3, 20);
            openTool.RemoveTriangle(0, true, false); // forces InputRepair on input 1

            var pipe = new SolidBooleanPipeline(cleanWorkpiece, openTool, ExactMeshBoolean.Operations.Difference) {
                InputRepair = SolidBooleanPipeline.InputRepairModes.AutoRepair,
                TrackFaceProvenance = true,
                PreserveTriangleGroups = true
            };
            Assert.True(pipe.Compute());
            Assert.Equal(SolidBooleanPipeline.PipelineStatus.Success, pipe.Status);
            Assert.Contains(pipe.Report.RepairedInputs, r => r.Index == 1);
            Assert.NotNull(pipe.ResultProvenance);

            int fromClean = 0, fromRepairedNone = 0;
            foreach (int tid in pipe.Result.TriangleIndices()) {
                TriangleProvenance tp = pipe.ResultProvenance[tid];
                if (tp.IsValid) {
                    Assert.Equal(0, tp.MeshIndex);
                    Assert.True(cleanWorkpiece.IsTriangle(tp.SourceTriangle));
                    fromClean++;
                } else {
                    Assert.Equal(TriangleProvenance.None, tp);
                    fromRepairedNone++;
                }
            }
            Assert.True(fromClean > 0 && fromRepairedNone > 0);
        }

        [Fact]
        public void TrackFaceProvenance_EmptyAndMinThicknessPartialRemoval_CompactsProvenance()
        {
            // 1. Disjoint intersection => empty ResultProvenance and groups enabled
            DMesh3 a = TaggedCube(0, 0, 0, 1, 1, 1, 10);
            DMesh3 b = TaggedCube(5, 5, 5, 6, 6, 6, 20);
            var pipeEmpty = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Intersection) {
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(pipeEmpty.Compute());
            Assert.Equal(0, pipeEmpty.Result.TriangleCount);
            Assert.True(pipeEmpty.Result.HasTriangleGroups);
            Assert.NotNull(pipeEmpty.ResultProvenance);
            Assert.Empty(pipeEmpty.ResultProvenance);

            // 2. MinThickness removes thin component (leaving non-compact mesh before CompactCopy)
            DMesh3 thick = TaggedCube(0, 0, 0, 2, 2, 2, 10);
            DMesh3 thin = TaggedCube(5, 5, 5, 6, 6, 5.01, 20);
            var pipeMinThick = new SolidBooleanPipeline(thick, thin, ExactMeshBoolean.Operations.Union) {
                MinThickness = 0.05,
                PreserveTriangleGroups = true,
                TrackFaceProvenance = true
            };
            Assert.True(pipeMinThick.Compute());
            Assert.Single(pipeMinThick.Report.RemovedThinSolids);
            Assert.NotNull(pipeMinThick.ResultProvenance);
            Assert.Equal(pipeMinThick.Result.MaxTriangleID, pipeMinThick.ResultProvenance.Length);
            Assert.True(pipeMinThick.Result.TriangleCount > 0);
            foreach (int tid in pipeMinThick.Result.TriangleIndices()) {
                TriangleProvenance tp = pipeMinThick.ResultProvenance[tid];
                Assert.True(tp.IsValid);
                Assert.Equal(0, tp.MeshIndex);
                Assert.True(thick.IsTriangle(tp.SourceTriangle));
                Assert.Equal(10, pipeMinThick.Result.GetTriangleGroup(tid));
            }
        }

        [Fact]
        public void DefaultFlagsOff_PipelineBitIdentical_AndReuseResetsProvenance()
        {
            DMesh3 a = TaggedCube(0, 0, 0, 2, 2, 2, 10);
            DMesh3 b = TaggedCube(1, 1, 1, 3, 3, 3, 20);

            var pipe = new SolidBooleanPipeline(a, b, ExactMeshBoolean.Operations.Difference);
            Assert.True(pipe.Compute());
            DMesh3 baseline = new DMesh3(pipe.Result);
            Assert.False(pipe.Result.HasTriangleGroups);
            Assert.Null(pipe.ResultProvenance);

            pipe.PreserveTriangleGroups = true;
            pipe.TrackFaceProvenance = true;
            Assert.True(pipe.Compute());
            Assert.True(pipe.Result.HasTriangleGroups);
            Assert.NotNull(pipe.ResultProvenance);

            pipe.PreserveTriangleGroups = false;
            pipe.TrackFaceProvenance = false;
            Assert.True(pipe.Compute());
            Assert.False(pipe.Result.HasTriangleGroups);
            Assert.Null(pipe.ResultProvenance);
            Assert.Equal("", Invariance.Difference(baseline, pipe.Result));
        }
    }
}
