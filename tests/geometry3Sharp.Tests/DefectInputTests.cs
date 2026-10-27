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

        /// <summary> runs check on the case; with G3_MINIMIZE=1 a failure is minimized (Harness/FailureMinimizer.cs) </summary>
        static void Check(string name, Action<DMesh3> check, [System.Runtime.CompilerServices.CallerMemberName] string test = "") =>
            FailureMinimizer.Check(test + " " + name, () => Defects.ByName(name).Make(), check);

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
        public void MeshAutoRepair_DefectInput(string name) => Check(name, input => {
            var r = new MeshAutoRepair(input);
            Assert.True(r.Apply(), name + ": Apply returned false without a cancel");
            // (consistent orientation is not promised, the repair only tries; it gets there on all these cases, so this
            // guards against a regression)
            AssertValid(r.Mesh, name);
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshRobustRepair_DefectInput_CleanWhenReportedClean(string name) => Check(name, input => {
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
        });

        // ---- boolean ----

        static bool HasInconsistentInput(string name) => Inconsistent(Defects.ByName(name).Make());

        public static System.Collections.Generic.IEnumerable<object[]> ConsistentCases() =>
            Defects.Names().Where(c => HasInconsistentInput((string)c[0]) == false);

        public static System.Collections.Generic.IEnumerable<object[]> InconsistentCases() =>
            Defects.Names().Where(c => HasInconsistentInput((string)c[0]));

        static void UnionWithBox(DMesh3 a, string name, bool allowInconsistentResult = false)
        {
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

        static void Resolve(DMesh3 a, string name, bool allowInconsistentResult = false)
        {
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
        public void ExactMeshBoolean_UnionWithBox_DefectInput(string name) => Check(name, a => UnionWithBox(a, name));

        [Theory]
        [MemberData(nameof(ConsistentCases))]
        public void ExactMeshBoolean_Resolve_DefectInput(string name) => Check(name, a => Resolve(a, name));

        // inconsistently oriented inputs: everything but the result's orientation (see the test below)
        [Theory]
        [MemberData(nameof(InconsistentCases))]
        public void ExactMeshBoolean_InconsistentInput_ValidStructure(string name)
            => Check(name, a => { UnionWithBox(new DMesh3(a), name, true); Resolve(a, name, true); });

        // Inputs with inconsistently oriented triangles (a flipped triangle): ExactMeshBoolean orients each component
        // consistently first, so the result is a consistently oriented surface (DMesh3.CheckValidity passes). Minimal
        // case: a unit box with one triangle flipped gives the box (see also ExactBooleanOrientationTests).
        [Theory]
        [MemberData(nameof(InconsistentCases))]
        public void ExactMeshBoolean_InconsistentInput_ConsistentResult(string name)
            => Check(name, a => { UnionWithBox(new DMesh3(a), name); Resolve(a, name); });

        // "random edits 6" is open (removed triangles) and has a stray triangle through the inside of the sphere whose
        // edge lies on the sphere (an edge with three triangles). It is classified by generalized winding numbers, and
        // around such an edge the integer jumps across the pieces do not add up to zero (an open boundary of the input
        // lies on it): no inside/outside choice for the regions around the edge agrees with all pieces, so the union
        // keeps three pieces there, or two that run the edge the same way. Assembled like the other pieces, they gave
        // an inconsistently oriented edge; now pieces that run an edge the same way are not joined there
        // (ExactMeshBoolean.ConflictEdges: the result is open along the edge).
        [Fact]
        public void ExactMeshBoolean_OpenInputWithInteriorSheet_ConsistentResult()
            => Check("random edits 6", a => { UnionWithBox(new DMesh3(a), "random edits 6"); Resolve(a, "random edits 6"); });

        // "random edits 6" minimized by MeshMinimizer (G3_MINIMIZE=1, docs/QA.md 2.9; 301 -> 5 triangles, 1158
        // evaluations, 10 s), with the coordinates rounded. Five open triangles: three are coplanar (two of them
        // coincide with opposite orientations, so they cancel), and the open edge of a fourth lies inside the coplanar
        // ones, which is the same kind of edge as above. Vertices that coincide differ in the sign of zero.
        internal static DMesh3 MinimizedRandomEdits6()
        {
            // 8 vertices, 5 triangles
            double[] v = {
                0.5, 0.5, -0.5,
                0, -0.0, 0,
                1, -0.0, -0.0,
                1, 0, 0,
                1, 0.125, -0.125,
                1, 0, -0.5,
                1, 0, -0.0,
                0, 0, -0.0,
            };
            int[] t = {
                1, 4, 3,
                2, 5, 4,
                4, 5, 7,
                4, 7, 6,
                6, 7, 0,
            };
            return Build(v, t);
        }

        // the same without the rounding (G3_MINIMIZE_ROUND=0; 1115 evaluations, 8 s): 34 triangles of the sphere next
        // to the box and the stray triangle (the last one, its two vertices on the sphere unwelded), whose edge on the
        // sphere has three triangles. Here the inconsistent edge appeared only with SimplifyFlatRegions (which
        // retriangulates the flat region next to that edge).
        internal static DMesh3 MinimizedRandomEdits6Unrounded()
        {
            // 35 vertices, 34 triangles
            double[] v = {
                0.66332495807108, -0.34641016151377557, 0.66332495807108,
                0.7023769168568493, -0.11547005383792515, 0.7023769168568493,
                0.7023769168568493, 0.11547005383792515, 0.7023769168568493,
                0.11547005383792515, 0.7023769168568493, 0.7023769168568493,
                -0.11547005383792515, 0.7023769168568493, 0.7023769168568493,
                0.66332495807108, 0.66332495807108, -0.34641016151377557,
                0.7023769168568493, 0.7023769168568493, -0.11547005383792515,
                -0.1404753833713698, -0.1404753833713698, 0.9800680248499761,
                0.1404753833713698, -0.1404753833713698, 0.9800680248499761,
                0.4214261501141096, -0.13266499161421597, 0.8971064596802322,
                -0.1404753833713698, 0.1404753833713698, 0.9800680248499761,
                0.1404753833713698, 0.1404753833713698, 0.9800680248499761,
                0.4214261501141096, 0.13266499161421597, 0.8971064596802322,
                -0.13266499161421597, 0.4214261501141096, 0.8971064596802322,
                0.13266499161421597, 0.4214261501141096, 0.8971064596802322,
                0.8265591327908731, -0.3979949748426481, 0.3979949748426481,
                0.8971064596802322, -0.13266499161421597, 0.4214261501141096,
                0.9800680248499761, -0.1404753833713698, 0.1404753833713698,
                0.9800680248499761, -0.1404753833713698, -0.1404753833713698,
                0.8971064596802322, 0.13266499161421597, 0.4214261501141096,
                0.9800680248499761, 0.1404753833713698, 0.1404753833713698,
                0.9800680248499761, 0.1404753833713698, -0.1404753833713698,
                0.8971064596802322, 0.13266499161421597, -0.4214261501141096,
                0.8971064596802322, 0.4214261501141096, -0.13266499161421597,
                0.8265591327908731, 0.3979949748426481, -0.3979949748426481,
                -0.13266499161421597, 0.8971064596802322, 0.4214261501141096,
                0.13266499161421597, 0.8971064596802322, 0.4214261501141096,
                0.1404753833713698, 0.9800680248499761, 0.1404753833713698,
                0.4214261501141096, 0.8971064596802322, 0.13266499161421597,
                0.1404753833713698, 0.9800680248499761, -0.1404753833713698,
                0.4214261501141096, 0.8971064596802322, -0.13266499161421597,
                0.3979949748426481, 0.8265591327908731, -0.3979949748426481,
                0.8971064596802322, -0.13266499161421597, 0.4214261501141096,
                -0.3979949748426481, 0.8265591327908731, 0.3979949748426481,
                0.8971064596802322, 0.13266499161421597, 0.4214261501141096,
            };
            int[] t = {
                7, 8, 11,
                7, 11, 10,
                8, 9, 12,
                8, 12, 11,
                9, 1, 2,
                9, 2, 12,
                10, 11, 14,
                10, 14, 13,
                13, 14, 3,
                13, 3, 4,
                0, 15, 16,
                0, 16, 1,
                15, 17, 16,
                1, 16, 19,
                1, 19, 2,
                16, 17, 20,
                16, 20, 19,
                17, 18, 21,
                17, 21, 20,
                18, 22, 21,
                21, 22, 24,
                21, 24, 23,
                23, 24, 5,
                23, 5, 6,
                4, 3, 26,
                4, 26, 25,
                25, 26, 27,
                26, 28, 27,
                27, 28, 30,
                27, 30, 29,
                29, 30, 31,
                30, 6, 5,
                30, 5, 31,
                32, 33, 34,
            };
            return Build(v, t);
        }

        static DMesh3 Build(double[] v, int[] t)
        {
            var m = new DMesh3(false, false, false, false);
            for (int i = 0; i < v.Length; i += 3) m.AppendVertex(new Vector3d(v[i], v[i + 1], v[i + 2]));
            for (int i = 0; i < t.Length; i += 3)
                if (m.AppendTriangle(t[i], t[i + 1], t[i + 2]) < 0)
                    throw new InvalidOperationException("repro: triangle " + i / 3);
            return m;
        }

        [Fact]
        public void ExactMeshBoolean_MinimizedRandomEdits6_ConsistentResult() => UnionWithBox(MinimizedRandomEdits6(), "random edits 6, minimized");

        [Fact]
        public void ExactMeshBoolean_MinimizedRandomEdits6Unrounded_ConsistentResult() => UnionWithBox(MinimizedRandomEdits6Unrounded(), "random edits 6, minimized without rounding");

        // ---- wrap, solidify ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshAlphaWrap_DefectInput_GuaranteesOnSuccess(string name) => Check(name, a => {
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
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void MeshWindingNumberSolidify_DefectInput(string name) => Check(name, a => {
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
        });

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
        public void Remesher_DefectInput(string name) => Check(name, m => {
            bool inconsistent = Inconsistent(m);
            var r = new Remesher(m);
            r.SetTargetEdgeLength(MeanEdgeLength(m));
            for (int k = 0; k < 3; ++k)
                r.BasicRemeshPass();
            AssertValid(m, name, inconsistent);
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void Reducer_DefectInput(string name) => Check(name, m => {
            bool inconsistent = Inconsistent(m);
            var r = new Reducer(m);
            r.ReduceToTriangleCount(m.TriangleCount / 2);
            AssertValid(m, name, inconsistent);
        });

        // ---- queries and analysis ----

        [Theory]
        [MemberData(nameof(Cases))]
        public void Queries_DefectInput(string name) => Check(name, m => {
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
        });
    }
}
