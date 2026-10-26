using System;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Every operation over the defect cases of Harness/Defects.cs (docs/QA.md 2.1, 2.3, 2.5): it must not throw (other
    /// than its documented exceptions), must leave a valid DMesh3 with finite coordinates, and must not report success
    /// for a result that misses its documented guarantees.
    /// </summary>
    public class DefectInputTests
    {
        public static System.Collections.Generic.IEnumerable<object[]> Cases() => Defects.Names();

        /// <summary>
        /// interior edges whose two triangles run through them in the same direction. DMesh3 accepts such triangles (the
        /// readers create them for inconsistently oriented files), but CheckValidity reports them.
        /// </summary>
        static int InconsistentEdges(DMesh3 mesh)
        {
            int n = 0;
            foreach (int e in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(e), ev = mesh.GetEdgeV(e);
                if (et.b == DMesh3.InvalidID) continue;
                bool fa = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, mesh.GetTriangle(et.a).array) != DMesh3.InvalidID;
                bool fb = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, mesh.GetTriangle(et.b).array) != DMesh3.InvalidID;
                if (fa == fb) ++n;
            }
            return n;
        }

        /// <summary>
        /// a valid DMesh3 with finite coordinates. Inconsistently oriented edges are accepted only if the input had them
        /// (an operation that keeps the input triangles keeps them); otherwise CheckValidity must pass.
        /// </summary>
        static void AssertValid(DMesh3 mesh, string what, bool inputInconsistent = false)
        {
            Assert.True(mesh != null, what + ": no mesh");
            int bad = InconsistentEdges(mesh);
            Assert.True(bad == 0 || inputInconsistent, what + $": {bad} inconsistently oriented edges");
            if (bad == 0)
                Assert.True(mesh.CheckValidity(true, FailMode.ReturnOnly), what + ": invalid DMesh3");
            Assert.True(Invariance.AllFinite(mesh), what + ": non-finite coordinates");
        }

        static bool Inconsistent(DMesh3 mesh) => InconsistentEdges(mesh) > 0;

        static bool HasBowtie(DMesh3 mesh) => mesh.VertexIndices().Any(v => mesh.IsBowtieVertex(v));

        // ---- repair ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshAutoRepair_DefectInput(string name)
        {
            var r = new MeshAutoRepair(Defects.ByName(name).Make());
            Assert.True(r.Apply(), name + ": Apply returned false without a cancel");
            AssertValid(r.Mesh, name);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshRobustRepair_DefectInput_CleanWhenReportedClean(string name)
        {
            DMesh3 input = Defects.ByName(name).Make();
            bool empty = input.TriangleCount == 0;
            var r = new MeshRobustRepair(input) { SolidifyResolution = 64 };
            bool ok = r.Apply();
            if (empty) {
                Assert.False(ok, name + ": an empty input is documented to fail");
                return;
            }
            Assert.True(ok, $"{name}: Apply failed ({r.ResultReason})");
            AssertValid(r.Mesh, name);
            // ResultReason "" claims a clean result: check it independently
            if (r.ResultReason == "")
                Assert.Equal("", MeshRobustRepair.NotCleanReason(r.Mesh));
        }

        // ---- boolean ----

        static bool HasInconsistentInput(string name) => Inconsistent(Defects.ByName(name).Make());

        public static System.Collections.Generic.IEnumerable<object[]> ConsistentCases() =>
            Defects.Names().Where(c => HasInconsistentInput((string)c[0]) == false);

        public static System.Collections.Generic.IEnumerable<object[]> InconsistentCases() =>
            Defects.Names().Where(c => HasInconsistentInput((string)c[0]));

        static void UnionWithBox(string name)
        {
            DMesh3 a = Defects.ByName(name).Make();
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.6, 0.4, 0.7)) }.Generate().MakeDMesh();
            var eb = new ExactMeshBoolean(a, box, ExactMeshBoolean.Operations.Union);
            bool ok = eb.Compute();
            Assert.True(ok == (eb.Status == ExactMeshBoolean.BooleanStatus.Success), name + ": status and return value disagree");
            Assert.True(ok, $"{name}: {eb.Status} {eb.Error}");
            // a new mesh: consistently oriented whatever the input
            AssertValid(eb.Result, name + " (union with a box)");
            // documented: closed and manifold for closed inputs
            if (a.IsClosed() && Inconsistent(a) == false) {
                Assert.True(eb.Result.IsClosed(), name + ": result not closed");
                Assert.False(HasBowtie(eb.Result), name + ": result has bowtie vertices");
            }
        }

        static void Resolve(string name)
        {
            DMesh3 a = Defects.ByName(name).Make();
            DMesh3 r = ExactMeshBoolean.Resolve(a);
            AssertValid(r, name + " (resolve)");
            if (a.IsClosed() && Inconsistent(a) == false) {
                // (empty for inputs without a solid: inside-out, flat)
                Assert.True(r.TriangleCount == 0 || r.IsClosed(), name + ": result not closed");
                Assert.False(HasBowtie(r), name + ": result has bowtie vertices");
            }
        }

        [Theory]
        [MemberData(nameof(ConsistentCases))]
        public void ExactMeshBoolean_UnionWithBox_DefectInput(string name) => UnionWithBox(name);

        [Theory]
        [MemberData(nameof(ConsistentCases))]
        public void ExactMeshBoolean_Resolve_DefectInput(string name) => Resolve(name);

        // Inputs with inconsistently oriented triangles (a flipped triangle): the result keeps the input triangles with
        // their input orientation where the solid's boundary runs along them, so it has inconsistently oriented edges
        // (not a valid oriented surface; DMesh3.CheckValidity fails). Minimal case: a unit box with one triangle
        // flipped; its winding number inside is about 0.83, so the result should be the box, consistently oriented.
        // Reported; enabled when fixed.
        [Theory(Skip = "ExactMeshBoolean keeps the orientation of flipped input triangles (reported)")]
        [MemberData(nameof(InconsistentCases))]
        public void ExactMeshBoolean_InconsistentInput_ConsistentResult(string name)
        {
            UnionWithBox(name);
            Resolve(name);
        }

        // ---- wrap, solidify ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshAlphaWrap_DefectInput_GuaranteesOnSuccess(string name)
        {
            DMesh3 a = Defects.ByName(name).Make();
            var w = new MeshAlphaWrap(a);
            bool ok = w.Compute();
            if (a.VertexCount == 0) {
                Assert.Equal(MeshAlphaWrap.WrapStatus.EmptyInput, w.Status);
                return;
            }
            Assert.True(ok, $"{name}: {w.Status}");
            Assert.True(w.Status == MeshAlphaWrap.WrapStatus.Success || w.Status == MeshAlphaWrap.WrapStatus.PointLimitReached, $"{name}: {w.Status}");
            AssertValid(w.Result, name);
            // documented: closed, manifold, free of self-intersections
            Assert.True(w.Result.IsClosed(), name + ": not closed");
            Assert.False(HasBowtie(w.Result), name + ": bowtie vertices");
            Assert.False(MeshRepairMetrics.HasSelfIntersections(w.Result), name + ": self-intersections");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshWindingNumberSolidify_DefectInput(string name)
        {
            DMesh3 a = Defects.ByName(name).Make();
            var s = new MeshWindingNumberSolidify(a) { GridResolution = 32 };
            bool ok = s.Compute();
            if (a.TriangleCount == 0) {
                Assert.False(ok, name + ": an empty input is documented to fail");
                return;
            }
            if (ok)
                AssertValid(s.Result, name);
        }

        // ---- remesh, reduce ----

        static double MeanEdgeLength(DMesh3 m) =>
            m.EdgeCount == 0 ? 1 : m.EdgeIndices().Average(e => { Index2i ev = m.GetEdgeV(e); return m.GetVertex(ev.a).Distance(m.GetVertex(ev.b)); });

        [Theory]
        [MemberData(nameof(Cases))]
        public void Remesher_DefectInput(string name)
        {
            DMesh3 m = Defects.ByName(name).Make();
            bool inconsistent = Inconsistent(m);
            var r = new Remesher(m);
            r.SetTargetEdgeLength(MeanEdgeLength(m));
            for (int k = 0; k < 3; ++k)
                r.BasicRemeshPass();
            AssertValid(m, name, inconsistent);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Reducer_DefectInput(string name)
        {
            DMesh3 m = Defects.ByName(name).Make();
            bool inconsistent = Inconsistent(m);
            var r = new Reducer(m);
            r.ReduceToTriangleCount(m.TriangleCount / 2);
            AssertValid(m, name, inconsistent);
        }

        // ---- queries and analysis ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void Queries_DefectInput(string name)
        {
            DMesh3 m = Defects.ByName(name).Make();
            var tree = new DMeshAABBTree3(m, true);
            Vector3d q = new Vector3d(0.3, -0.2, 0.1);
            int t = tree.FindNearestTriangle(q);
            Assert.True(t == DMesh3.InvalidID ? m.TriangleCount == 0 : m.IsTriangle(t), name + ": nearest triangle");
            double w = tree.WindingNumber(q);
            Assert.False(double.IsNaN(w) || double.IsInfinity(w), name + ": winding number " + w);
            double fw = tree.FastWindingNumber(q);
            Assert.False(double.IsNaN(fw) || double.IsInfinity(fw), name + ": fast winding number " + fw);
            tree.IsInside(q);

            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            Assert.Equal(m.TriangleCount, comps.Components.Sum(c => c.Indices.Length));

            try {
                var loops = new MeshBoundaryLoops(m);
                foreach (EdgeLoop loop in loops.Loops)
                    Assert.True(loop.CheckValidity(FailMode.ReturnOnly), name + ": invalid boundary loop");
            } catch (MeshBoundaryLoopsException) {
                // documented failure for boundaries it cannot walk (bowties, open spans)
            }
        }
    }
}
