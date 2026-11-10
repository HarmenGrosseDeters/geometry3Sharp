using System;
using System.Linq;
using g3;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Open operands are classified by their generalized winding number at every piece (the solid where it is above
    /// 1/2), not once per patch. Before, the value of a patch's first piece was used for all of it; a patch of one mesh
    /// can reach from inside an open mesh to outside it through that mesh's hole (where the hole cuts off the end of an
    /// intersection curve), and so two overlapping open spheres had an empty intersection, the union kept everything
    /// and the difference all of A (found while testing Lévy classification phase 2).
    /// </summary>
    public class ExactBooleanOpenPerPieceTests
    {
        // a unit sphere with the cap above capZ (relative to its centre) removed; capZ above 1 keeps it closed
        static DMesh3 Sphere(Vector3d c, double capZ)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            foreach (int t in m.TriangleIndices().ToArray())
                if (m.GetTriCentroid(t).z > capZ)
                    m.RemoveTriangle(t, true, false);
            MeshTransforms.Translate(m, c);
            return m;
        }

        static readonly Vector3d CenterB = new Vector3d(0.6, 0.3, 0.1);

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op)
        {
            var r = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(r.Compute(), r.Error);
            return r;
        }

        // per result triangle, the generalized winding numbers of the inputs at its centroid (independent of the
        // boolean: trees of the inputs); a piece of A lies on A (wA about 1/2), so only the other input decides
        internal static void AssertPieces(DMesh3 a, DMesh3 b, ExactMeshBoolean r, Ops op, string what)
        {
            var ta = new DMeshAABBTree3(a, true);
            var tb = new DMeshAABBTree3(b, true);
            DMesh3 m = r.Result;
            int bad = 0;
            foreach (int t in m.TriangleIndices()) {
                Vector3d c = m.GetTriCentroid(t);
                double wa = ta.FastWindingNumber(c), wb = tb.FastWindingNumber(c);
                // on A or on B: the nearer input surface; skipped near the curve where they cross
                double da = Math.Sqrt(MeshQueries.TriDistanceSqr(a, ta.FindNearestTriangle(c), c)), db = Math.Sqrt(MeshQueries.TriDistanceSqr(b, tb.FindNearestTriangle(c), c));
                // (both distances within the rounding of the coordinates, about 1e-16 of their magnitude: on both or
                // near their crossing, undecided)
                double resolution = 1e-14 * (Math.Max(Math.Abs(c.x), Math.Max(Math.Abs(c.y), Math.Abs(c.z))) + 1);
                if (Math.Abs(da - db) <= Math.Max(1e-3 * (da + db), resolution))
                    continue;
                bool onA = da < db;
                double other = onA ? wb : wa;
                if (Math.Abs(other - 0.5) < 0.1)
                    continue;
                bool inOther = other > 0.5;
                bool ok = op == Ops.Intersection ? inOther : op == Ops.Union ? !inOther : (onA ? !inOther : inOther);
                if (!ok)
                    bad++;
            }
            Assert.True(bad == 0, $"{what}: {bad} of {m.TriangleCount} result triangles on the wrong side of the other input");
        }

        [Fact]
        public void TwoOpenSpheres_IntersectionNotEmpty_PiecesOnTheRightSide()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 0.5);
            Assert.False(a.IsClosed() || b.IsClosed());
            // the setup: B's generalized winding number is well above 1/2 at A's centre
            Assert.True(new DMeshAABBTree3(b, true).FastWindingNumber(Vector3d.Zero) > 0.8);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                ExactMeshBoolean r = Run(a, b, op);
                Assert.True(r.Result.TriangleCount > 0, $"{op}: empty");
                AssertPieces(a, b, r, op, op.ToString());
            }
            // the oracle is not blind: the intersection's pieces are on the wrong side for a union
            ExactMeshBoolean inter = Run(a, b, Ops.Intersection);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertPieces(a, b, inter, Ops.Union, "control"));
            // before, the union kept the pieces of both inputs, also those inside the other (7592 triangles for 6726
            // input triangles here), and the intersection was empty
            int all = a.TriangleCount + b.TriangleCount;
            Assert.True(Run(a, b, Ops.Union).Result.TriangleCount < 0.8 * all);
        }

        [Fact]
        public void OpenAClosedB_PatchValueRight_PerPieceSame()
        {
            // A open, B closed: A's patch reaches no further than the closed intersection curve, so the per-patch value
            // was right here; the per-piece evaluation gives the same classification
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 2);
            Assert.True(b.IsClosed() && a.IsClosed() == false);
            foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference }) {
                ExactMeshBoolean r = Run(a, b, op);
                Assert.True(r.Result.TriangleCount > 0, $"{op}: empty");
                AssertPieces(a, b, r, op, op.ToString());
            }
        }

        [Fact]
        public void TwoOpenSpheres_Similarities()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 0.5);
            ExactMeshBoolean r0 = Run(a, b, Ops.Intersection);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 sa = Invariance.Transformed(a, sim), sb = Invariance.Transformed(b, sim);
                ExactMeshBoolean r = Run(sa, sb, Ops.Intersection);
                Assert.True(Invariance.AllFinite(r.Result), sim.ToString());
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(r0.Result, Invariance.MappedBack(r.Result, sim)));
                else
                    AssertPieces(sa, sb, r, Ops.Intersection, sim.ToString());
            }
        }

        [Fact]
        public void ReusedInstance_OpenThenClosedThenOpen_AsFresh()
        {
            // open inputs (per-piece winding, bowtie split), then closed ones (per patch) on the same instance, then the
            // open ones again: each result as from a fresh instance
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 0.5), ca = Sphere(Vector3d.Zero, 2), cb = Sphere(CenterB, 2);
            ExactMeshBoolean open = Run(a, b, Ops.Intersection), closed = Run(ca, cb, Ops.Intersection);
            var op = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Intersection);
            Assert.True(op.Compute(), op.Error);
            Assert.Equal("", Invariance.Difference(open.Result, op.Result));
            op.MeshA = new DMesh3(ca);
            op.MeshB = new DMesh3(cb);
            Assert.True(op.Compute(), op.Error);
            Assert.True(op.ExactWindingA && op.ExactWindingB);
            Assert.Equal("", Invariance.Difference(closed.Result, op.Result));
            Assert.Equal("", gs.MeshRobustRepair.NotCleanReason(op.Result));
            op.MeshA = new DMesh3(a);
            op.MeshB = new DMesh3(b);
            Assert.True(op.Compute(), op.Error);
            Assert.False(op.ExactWindingA || op.ExactWindingB);
            Assert.Equal("", Invariance.Difference(open.Result, op.Result));
        }

        [Fact]
        public void SplitBowties_HandBuilt_EachFanItsOwnVertex()
        {
            // vertex 0 shared by two closed fans (the apexes of two tetrahedra) and an open fan of two triangles; vertex
            // 1 is not a bowtie. The fan with the smallest triangle id keeps vertex 0, the others get a copy each, in the
            // order of their smallest triangle id, at the same position and with the same point id
            var m = new DMesh3();
            var o = new Vector3d(0.25, -0.5, 1.5);
            m.AppendVertex(o);
            Vector3d[] p = { new Vector3d(1, 0, 0), new Vector3d(1, 1, 0), new Vector3d(1, 0, 1), new Vector3d(-1, 0, 0), new Vector3d(-1, 0, 1), new Vector3d(-1, 1, 0),
                new Vector3d(0, -1, 2), new Vector3d(0.5, -1, 2), new Vector3d(1, -1, 2) };
            foreach (Vector3d q in p)
                m.AppendVertex(o + q);
            void tet(int b, int c, int d) { m.AppendTriangle(0, c, b); m.AppendTriangle(0, b, d); m.AppendTriangle(0, d, c); m.AppendTriangle(b, c, d); }
            tet(1, 2, 3);
            tet(4, 5, 6);
            m.AppendTriangle(0, 7, 8);
            m.AppendTriangle(0, 8, 9);
            Assert.True(m.IsBowtieVertex(0));
            var vertexPoint = Enumerable.Range(0, m.MaxVertexID).Select(v => 100 + v).ToList();
            Assert.Equal(1, ExactMeshBoolean.split_bowties(m, vertexPoint));
            Assert.Equal(12, m.VertexCount);
            Assert.Equal(new[] { 100, 100 }, vertexPoint.Skip(10));
            Assert.Equal(o, m.GetVertex(10));
            Assert.Equal(o, m.GetVertex(11));
            Assert.Empty(MeshIterators.BowtieVertices(m));
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            // the first tetrahedron (triangles 0-2) keeps vertex 0, the second (4-6) gets 10, the open fan (8, 9) 11
            int[] owner = { 0, 0, 0, -1, 10, 10, 10, -1, 11, 11 };
            for (int t = 0; t < owner.Length; ++t)
                if (owner[t] >= 0)
                    Assert.True(m.GetTriangle(t).a == owner[t], "triangle " + t);
            Assert.Equal(new[] { 1, 2, 3 }, m.GetTriangle(3).array.OrderBy(x => x));
            // nothing to split now
            Assert.Equal(0, ExactMeshBoolean.split_bowties(m, vertexPoint));
            Assert.Equal(12, vertexPoint.Count);
        }

        [Fact]
        public void TwoOpenSpheres_SerialEqualsParallel_ComputeTwice()
        {
            DMesh3 a = Sphere(Vector3d.Zero, 0.6), b = Sphere(CenterB, 0.5);
            ExactMeshBoolean serial = null;
            gParallel.SerialOnThisThread = true;
            try {
                serial = Run(a, b, Ops.Intersection);
            } finally {
                gParallel.SerialOnThisThread = false;
            }
            for (int run = 0; run < 2; ++run)
                Assert.Equal("", Invariance.Difference(serial.Result, Run(a, b, Ops.Intersection).Result));
            var again = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Intersection);
            Assert.True(again.Compute() && again.Compute());
            Assert.Equal("", Invariance.Difference(serial.Result, again.Result));
        }
    }
}
