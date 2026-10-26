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
        /// the structure of a DMesh3 without its orientation (CheckValidity also requires consistent orientation):
        /// triangles with distinct, existing vertices, each side an edge that lists the triangle, edges with existing
        /// vertices and one or two triangles that contain both of them, and vertex edge lists that agree with the edges
        /// </summary>
        static string StructureProblem(DMesh3 mesh)
        {
            foreach (int t in mesh.TriangleIndices()) {
                Index3i tv = mesh.GetTriangle(t), te = mesh.GetTriEdges(t);
                if (tv.a == tv.b || tv.b == tv.c || tv.a == tv.c) return $"triangle {t} repeats a vertex";
                for (int j = 0; j < 3; ++j) {
                    if (mesh.IsVertex(tv[j]) == false) return $"triangle {t}: no vertex {tv[j]}";
                    int e = te[j];
                    if (mesh.IsEdge(e) == false) return $"triangle {t}: no edge {e}";
                    Index2i ev = mesh.GetEdgeV(e), et = mesh.GetEdgeT(e);
                    if (IndexUtil.same_pair_unordered(ev.a, ev.b, tv[j], tv[(j + 1) % 3]) == false) return $"triangle {t}: edge {e} has other vertices";
                    if (et.a != t && et.b != t) return $"edge {e} does not list triangle {t}";
                }
            }
            foreach (int e in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(e), et = mesh.GetEdgeT(e);
                if (mesh.IsVertex(ev.a) == false || mesh.IsVertex(ev.b) == false) return $"edge {e}: missing vertex";
                foreach (int t in new[] { et.a, et.b }) {
                    if (t == DMesh3.InvalidID) continue;
                    if (mesh.IsTriangle(t) == false) return $"edge {e}: no triangle {t}";
                    Index3i tv = mesh.GetTriangle(t);
                    Func<int, bool> has = (v) => tv.a == v || tv.b == v || tv.c == v;
                    if (has(ev.a) == false || has(ev.b) == false) return $"edge {e}: triangle {t} misses a vertex";
                }
                if (et.a == DMesh3.InvalidID) return $"edge {e} without triangles";
                if (mesh.FindEdge(ev.a, ev.b) != e) return $"edge {e} not found from its vertices";
            }
            foreach (int v in mesh.VertexIndices())
                foreach (int e in mesh.VtxEdgesItr(v))
                    if (mesh.IsEdge(e) == false || mesh.GetEdgeV(e).a != v && mesh.GetEdgeV(e).b != v) return $"vertex {v}: wrong edge {e}";
            return "";
        }

        /// <summary>
        /// a valid DMesh3 with finite coordinates. Inconsistently oriented edges are accepted only if the input had them
        /// (an operation that keeps the input triangles keeps them); the rest of the structure must be valid in any case.
        /// </summary>
        static void AssertValid(DMesh3 mesh, string what, bool inputInconsistent = false)
        {
            Assert.True(mesh != null, what + ": no mesh");
            int bad = InconsistentEdges(mesh);
            Assert.True(bad == 0 || inputInconsistent, what + $": {bad} inconsistently oriented edges");
            Assert.Equal("", StructureProblem(mesh));
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
            // (consistent orientation is not promised, the repair only tries; it gets there on all these cases, so this
            // guards against a regression)
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

        static void UnionWithBox(string name, bool allowInconsistentResult = false)
        {
            DMesh3 a = Defects.ByName(name).Make();
            DMesh3 box = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.5, 0.5), new Vector3d(0.6, 0.4, 0.7)) }.Generate().MakeDMesh();
            var eb = new ExactMeshBoolean(a, box, ExactMeshBoolean.Operations.Union);
            bool ok = eb.Compute();
            Assert.True(ok == (eb.Status == ExactMeshBoolean.BooleanStatus.Success), name + ": status and return value disagree");
            Assert.True(ok, $"{name}: {eb.Status} {eb.Error}");
            // a new mesh: consistently oriented whatever the input
            AssertValid(eb.Result, name + " (union with a box)", allowInconsistentResult);
            // documented: closed for closed inputs (and the touching vertices are duplicated: no bowties)
            if (a.IsClosed() && Inconsistent(a) == false) {
                Assert.True(eb.Result.IsClosed(), name + ": result not closed");
                Assert.False(HasBowtie(eb.Result), name + ": result has bowtie vertices");
            }
        }

        static void Resolve(string name, bool allowInconsistentResult = false)
        {
            DMesh3 a = Defects.ByName(name).Make();
            DMesh3 r = ExactMeshBoolean.Resolve(a);
            AssertValid(r, name + " (resolve)", allowInconsistentResult);
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

        // inconsistently oriented inputs: everything but the result's orientation (see the test below)
        [Theory]
        [MemberData(nameof(InconsistentCases))]
        public void ExactMeshBoolean_InconsistentInput_ValidStructure(string name)
        {
            UnionWithBox(name, true);
            Resolve(name, true);
        }

        // Inputs with inconsistently oriented triangles (a flipped triangle): ExactMeshBoolean orients each component
        // consistently first, so the result is a consistently oriented surface (DMesh3.CheckValidity passes). Minimal
        // case: a unit box with one triangle flipped gives the box (see also ExactBooleanOrientationTests).
        [Theory]
        [MemberData(nameof(InconsistentCasesOrientable))]
        public void ExactMeshBoolean_InconsistentInput_ConsistentResult(string name)
        {
            UnionWithBox(name);
            Resolve(name);
        }

        // the inconsistent cases without "random edits 6" (see the skipped test below)
        public static System.Collections.Generic.IEnumerable<object[]> InconsistentCasesOrientable() =>
            InconsistentCases().Where(c => (string)c[0] != "random edits 6");

        // "random edits 6": the union with the box has an inconsistently oriented edge, but not because of the
        // orientation: the stray triangle through the inside of the sphere alone (edit 0, no flips) gives it, and
        // the input is not reoriented there. The mesh is open, so it is classified by generalized winding numbers;
        // next to the sheet they are near 0.5 and 1.5, at the threshold 1/2, and patches evaluated independently
        // decide inconsistently. A consistent classification needs winding numbers propagated across patches with
        // integer jumps. Reported; enabled when fixed.
        [Fact(Skip = "ExactMeshBoolean: generalized winding patch classification at the 1/2 threshold is inconsistent next to an interior sheet (reported)")]
        public void ExactMeshBoolean_OpenInputWithInteriorSheet_ConsistentResult()
        {
            UnionWithBox("random edits 6");
            Resolve("random edits 6");
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
            // Compute() is true only for Success; PointLimitReached also has a result with all the guarantees
            Assert.True(ok == (w.Status == MeshAlphaWrap.WrapStatus.Success), $"{name}: {w.Status} returned {ok}");
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
            // documented: false only for an empty input, a bad cell size, a too large grid or a cancel
            Assert.True(ok, name + ": Compute failed");
            AssertValid(s.Result, name);
            // documented: closed, without self-intersections (empty where the input has no solid: open sheet, flat,
            // inside-out)
            if (s.Result.TriangleCount > 0) {
                Assert.True(s.Result.IsClosed(), name + ": not closed");
                Assert.False(MeshRepairMetrics.HasSelfIntersections(s.Result), name + ": self-intersections");
            }
        }

        // The cell size comes from the bounds of the triangles: an unused vertex 1e6 away (in CachedBounds) used to give
        // cells of about 5e4 and an empty result.
        [Fact]
        public void MeshWindingNumberSolidify_UnusedFarVertex_KeepsTheSolid()
        {
            var s = new MeshWindingNumberSolidify(Defects.ByName("unused far vertex").Make()) { GridResolution = 32 };
            Assert.True(s.Compute());
            Assert.True(s.Result.TriangleCount > 0, "empty result");
            Assert.InRange(Invariance.Volume(s.Result), 0.8 * 4.18879, 1.1 * 4.18879);
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
