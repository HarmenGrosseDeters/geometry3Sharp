using System;
using System.Collections.Generic;
using g3;
using Xunit;
using Xunit.Abstractions;

namespace geometry3Sharp.Tests
{
    public class ReducerQuadricBudgetTests
    {
        readonly ITestOutputHelper output;
        public ReducerQuadricBudgetTests(ITestOutputHelper output) { this.output = output; }

        sealed class RecordingReducer : Reducer
        {
            public double MinimumInitialError = double.PositiveInfinity;
            public double MinimumEndpointError = double.PositiveInfinity;
            public int RestrictToVertex = -1;
            public readonly List<double> AcceptedErrors = new List<double>();
            public RecordingReducer(DMesh3 mesh) : base(mesh) { }

            protected override void InitializeQueue()
            {
                base.InitializeQueue();
                MinimumInitialError = MinimumEndpointError = double.PositiveInfinity;
                foreach (int eid in Mesh.EdgeIndices()) {
                    QEdge edge = EdgeQuadrics[eid];
                    MinimumInitialError = Math.Min(MinimumInitialError, evaluate(ref edge.q, edge.collapse_pt));
                    Index2i ev = Mesh.GetEdgeV(eid);
                    MinimumEndpointError = Math.Min(MinimumEndpointError,
                        Math.Min(evaluate(ref edge.q, Mesh.GetVertex(ev.a)), evaluate(ref edge.q, Mesh.GetVertex(ev.b))));
                }
                if (RestrictToVertex >= 0) {
                    EdgeQueue.Clear(false);
                    foreach (int eid in Mesh.VtxEdgesItr(RestrictToVertex)) {
                        QEdge edge = EdgeQuadrics[eid];
                        EdgeQueue.Insert(eid, (float)evaluate(ref edge.q, edge.collapse_pt));
                    }
                }
            }

            protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo collapseInfo)
            {
                QEdge edge = EdgeQuadrics[edgeID];
                AcceptedErrors.Add(evaluate(ref edge.q, Mesh.GetVertex(va)));
                base.OnEdgeCollapse(edgeID, va, vb, collapseInfo);
            }
        }

        // Constant quadrics isolate queue rounding from geometric cost evaluation; collapses still use the real mesh.
        sealed class ConstantCostReducer : Reducer
        {
            readonly double firstCost, otherCost;
            public readonly List<double> AcceptedErrors = new List<double>();
            public ConstantCostReducer(DMesh3 mesh, double firstCost, double otherCost) : base(mesh)
            {
                this.firstCost = firstCost; this.otherCost = otherCost;
            }
            protected override void InitializeQueue()
            {
                EdgeQuadrics = new QEdge[Mesh.MaxEdgeID];
                EdgeQueue = new IndexPriorityQueue(Mesh.MaxEdgeID);
                // Equal priorities move up in this heap; insert the expensive half last so it is examined first.
                for (int eid = Mesh.MaxEdgeID - 1; eid >= 0; --eid) {
                    if (!Mesh.IsEdge(eid)) continue;
                    var q = new QuadricError { c = eid < Mesh.MaxEdgeID / 2 ? firstCost : otherCost };
                    Index2i ev = Mesh.GetEdgeV(eid);
                    Vector3d midpoint = (Mesh.GetVertex(ev.a) + Mesh.GetVertex(ev.b)) * 0.5;
                    EdgeQuadrics[eid] = new QEdge(eid, ref q, ref midpoint);
                    EdgeQueue.Insert(eid, (float)q.c);
                }
                Assert.Equal(firstCost, EdgeQuadrics[EdgeQueue.First].q.c);
            }
            protected override void UpdateNeighbours(int vid) { }
            protected override void OnEdgeCollapse(int edgeID, int va, int vb, DMesh3.EdgeCollapseInfo info)
            {
                AcceptedErrors.Add(evaluate(ref EdgeQuadrics[edgeID].q, Mesh.GetVertex(va)));
            }
        }

        static DMesh3 Sphere() => new Sphere3Generator_NormalizedCube { Radius = 1, EdgeVertices = 4 }.Generate().MakeDMesh();

        static void AssertClean(DMesh3 mesh)
        {
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(mesh.IsClosed());
            Assert.True(Invariance.AllFinite(mesh));
            Assert.All(mesh.TriangleIndices(), tid => Assert.True(mesh.GetTriArea(tid) > 0));
        }

        [Fact]
        public void RoundedUpPriority_StillAcceptsErrorEqualToDoubleBudget()
        {
            double cost = 1 + 3 * Math.Pow(2, -25);
            Assert.True((float)cost > cost);
            DMesh3 mesh = Sphere();
            var reducer = new ConstantCostReducer(mesh, cost, cost);
            reducer.ReduceToMaxQuadricError(cost);
            Assert.NotEmpty(reducer.AcceptedErrors);
            Assert.All(reducer.AcceptedErrors, error => Assert.Equal(cost, error));
            AssertClean(mesh);
        }

        [Fact]
        public void RoundedTie_OverBudgetCandidateDoesNotHideAffordableCandidates()
        {
            double high = 1 + Math.Pow(2, -25);
            Assert.Equal(1f, (float)high);
            DMesh3 mesh = Sphere();
            var reducer = new ConstantCostReducer(mesh, high, 1);
            reducer.ReduceToMaxQuadricError(1);
            Assert.NotEmpty(reducer.AcceptedErrors);
            Assert.All(reducer.AcceptedErrors, error => Assert.Equal(1, error));
            AssertClean(mesh);
        }

        [Fact]
        public void FixedVertices_CheckActualEndpointRatherThanQueuedOptimum()
        {
            DMesh3 mesh = Sphere();
            var reducer = new RecordingReducer(mesh) { AreaWeightedQuadrics = false };
            reducer.ReduceToTriangleCount(mesh.TriangleCount);
            Assert.True(reducer.MinimumEndpointError > reducer.MinimumInitialError);
            double budget = (reducer.MinimumInitialError + reducer.MinimumEndpointError) * 0.5;
            var constraints = new MeshConstraints();
            constraints.SetOrUpdateVertexConstraint(0, new VertexConstraint(true, 0));
            reducer.SetExternalConstraints(constraints);
            reducer.RestrictToVertex = 0;
            DMesh3 before = new DMesh3(mesh);
            reducer.ReduceToMaxQuadricError(budget);
            Assert.Empty(reducer.AcceptedErrors);
            Assert.Equal("", Invariance.Difference(before, mesh));
            AssertClean(mesh);
        }

        [Fact]
        public void BudgetRejection_IsInvariantUnderSimilarities()
        {
            DMesh3 source = Sphere();
            var inspect = new RecordingReducer(new DMesh3(source)) { AreaWeightedQuadrics = false };
            inspect.ReduceToTriangleCount(source.TriangleCount);
            // between the cheapest queued optimum and the cheapest endpoint: passes the queue, fails the check of the kept position
            double fixedBudget = (inspect.MinimumInitialError + inspect.MinimumEndpointError) * 0.5;
            DMesh3 reduced = new DMesh3(source);
            new Reducer(reduced) { AreaWeightedQuadrics = false }.ReduceToMaxQuadricError(0.05);
            Assert.True(reduced.TriangleCount < source.TriangleCount);
            foreach (Similarity s in Similarity.All()) {
                DMesh3 mesh = Invariance.Transformed(source, s);
                var reducer = new RecordingReducer(mesh) { AreaWeightedQuadrics = false };
                reducer.ReduceToMaxQuadricError(0.05 * s.Scale * s.Scale);
                Assert.NotEmpty(reducer.AcceptedErrors);
                if (s.IsExact)
                    Assert.Equal("", Invariance.Difference(reduced, Invariance.MappedBack(mesh, s)));
                AssertClean(mesh);

                mesh = Invariance.Transformed(source, s);
                DMesh3 before = new DMesh3(mesh);
                var constraints = new MeshConstraints();
                constraints.SetOrUpdateVertexConstraint(0, new VertexConstraint(true, 0));
                reducer = new RecordingReducer(mesh) { AreaWeightedQuadrics = false, RestrictToVertex = 0 };
                reducer.SetExternalConstraints(constraints);
                reducer.ReduceToMaxQuadricError(fixedBudget * s.Scale * s.Scale);
                Assert.Empty(reducer.AcceptedErrors);
                Assert.Equal("", Invariance.Difference(before, mesh));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BudgetMode_DoesNotLeakIntoOtherReductionModes(bool fast)
        {
            DMesh3 mesh = Sphere(), fresh = new DMesh3(mesh);
            var reused = new Reducer(mesh);
            // (NaN: every collapse reaches the budget check and is rejected; 0 would stop before it)
            reused.ReduceToMaxQuadricError(double.NaN);
            Assert.Equal("", Invariance.Difference(fresh, mesh));
            if (fast) {
                reused.FastCollapsePass(1);
                new Reducer(fresh).FastCollapsePass(1);
            } else {
                reused.ReduceToTriangleCount(50);
                new Reducer(fresh).ReduceToTriangleCount(50);
            }
            Assert.True(mesh.TriangleCount < Sphere().TriangleCount);
            Assert.Equal("", Invariance.Difference(fresh, mesh));
            AssertClean(mesh);
        }

        [Fact]
        public void RepeatedBudgetCalls_MatchFreshInstance_AndRepeatedRuns()
        {
            DMesh3 firstRun = null;
            for (int run = 0; run < 3; ++run) {
                DMesh3 mesh = Sphere();
                var reducer = new RecordingReducer(mesh) { AreaWeightedQuadrics = false };
                reducer.ReduceToMaxQuadricError(0.05);
                Assert.NotEmpty(reducer.AcceptedErrors);
                Assert.All(reducer.AcceptedErrors, error => Assert.True(error <= 0.05));
                DMesh3 fresh = new DMesh3(mesh);
                reducer.AcceptedErrors.Clear();
                reducer.ReduceToMaxQuadricError(0.1);
                new Reducer(fresh) { AreaWeightedQuadrics = false }.ReduceToMaxQuadricError(0.1);
                Assert.All(reducer.AcceptedErrors, error => Assert.True(error <= 0.1));
                Assert.Equal("", Invariance.Difference(fresh, mesh));
                if (firstRun != null) Assert.Equal("", Invariance.Difference(firstRun, mesh));
                firstRun = new DMesh3(mesh);
                AssertClean(mesh);
            }
        }

        sealed class ThrowOnceReducer : Reducer
        {
            bool throwOnce = true;
            public ThrowOnceReducer(DMesh3 mesh) : base(mesh) { }
            protected override ProcessResult CollapseEdge(int eid, Vector3d point, out int kept)
            {
                if (throwOnce) {
                    throwOnce = false;
                    throw new InvalidOperationException("test interruption");
                }
                return base.CollapseEdge(eid, point, out kept);
            }
        }

        [Fact]
        public void InterruptedBudgetCall_DoesNotLeakBudgetIntoFastCollapsePass()
        {
            DMesh3 mesh = Sphere(), fresh = new DMesh3(mesh);
            var reducer = new ThrowOnceReducer(mesh);
            Assert.Throws<InvalidOperationException>(() => reducer.ReduceToMaxQuadricError(double.NaN));
            reducer.FastCollapsePass(1);
            new Reducer(fresh).FastCollapsePass(1);
            Assert.True(mesh.TriangleCount < Sphere().TriangleCount);
            Assert.Equal("", Invariance.Difference(fresh, mesh));
            AssertClean(mesh);
        }

        [Fact]
        public void NaNBudget_DoesNotAcceptAnyCollapse()
        {
            DMesh3 mesh = Sphere(), before = new DMesh3(mesh);
            new Reducer(mesh).ReduceToMaxQuadricError(double.NaN);
            Assert.Equal("", Invariance.Difference(before, mesh));
            AssertClean(mesh);
        }

        [Fact]
        public void MaxQuadricError_DoubleBudgetBelowFirstError_DoesNotAcceptRoundedDownPriority()
        {
            DMesh3 witness = null;
            double minimum = 0, budget = 0;
            // Select a fixed procedural witness by the rounding condition, not by whether reduction fails.
            foreach (double radius in new[] { 1.0, 1.1, 1.3, 1.7, 2.1, 2.9, 3.7, 4.3 }) {
                var mesh = new Sphere3Generator_NormalizedCube { Radius = radius, EdgeVertices = 4 }.Generate().MakeDMesh();
                var inspect = new RecordingReducer(mesh) { AreaWeightedQuadrics = false };
                inspect.ReduceToTriangleCount(mesh.TriangleCount);
                minimum = inspect.MinimumInitialError;
                double queued = (float)minimum;
                if (queued > 0 && queued < minimum) {
                    witness = mesh;
                    budget = queued + 0.5 * (minimum - queued);
                    output.WriteLine("radius={0:R}; exactMinimum={1:R}; queuedMinimum={2:R}; budget={3:R}", radius, minimum, queued, budget);
                    break;
                }
            }
            Assert.NotNull(witness);
            int trianglesBefore = witness.TriangleCount;
            var reducer = new RecordingReducer(witness) { AreaWeightedQuadrics = false };
            reducer.ReduceToMaxQuadricError(budget);
            foreach (double error in reducer.AcceptedErrors) output.WriteLine("acceptedError={0:R}; budget={1:R}", error, budget);
            Assert.True(witness.CheckValidity(false, FailMode.ReturnOnly));
            Assert.True(witness.IsClosed());
            Assert.All(reducer.AcceptedErrors, error => Assert.True(error <= budget,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "Actual accepted error {0:R} exceeds budget {1:R}", error, budget)));
            Assert.Equal(trianglesBefore, witness.TriangleCount);
        }
    }
}
