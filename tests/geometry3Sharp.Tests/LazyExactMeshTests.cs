using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    // ExactMesh (mesh_ops/LazyExactMesh.cs): a lazy boolean expression over double meshes, computed as one n-ary
    // ExactMeshBoolean (Exact Boolean 4.0 stage 1, docs/design/EXACT_BOOLEAN_40_DESIGN.md)
    public class LazyExactMeshTests
    {
        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var m = new DMesh3();
            for (int i = 0; i < 8; ++i)
                m.AppendVertex(new Vector3d((i & 1) == 0 ? x0 : x1, (i & 2) == 0 ? y0 : y1, (i & 4) == 0 ? z0 : z1));
            int[][] quads = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (int[] q in quads) {
                m.AppendTriangle(q[0], q[1], q[2]);
                m.AppendTriangle(q[0], q[2], q[3]);
            }
            return m;
        }

        static DMesh3 Sphere(Vector3d center, double radius, int edgeVertices = 8)
        {
            DMesh3 mesh = new Sphere3Generator_NormalizedCube() { Radius = radius, EdgeVertices = edgeVertices }.Generate().MakeDMesh();
            MeshTransforms.Translate(mesh, center);
            return mesh;
        }

        static DMesh3 A() => Sphere(Vector3d.Zero, 1);
        static DMesh3 B() => Box(0.25, -0.375, -0.5, 1.5, 0.625, 0.75);
        static DMesh3 C() => Sphere(new Vector3d(0.6, 0.2, 0.1), 0.7);
        static DMesh3 D() => Box(-0.75, -0.875, -0.625, 1.125, 0.5, 0.5);

        // clean by the library's check (closed, manifold, finite, no degenerate triangles or self-intersections), or empty
        static void AssertClean(DMesh3 r, string what)
        {
            Assert.NotNull(r);
            string reason = MeshRobustRepair.NotCleanReason(r);
            Assert.True(reason == "" || reason == "empty", $"{what}: {reason}");
            Assert.True(Invariance.AllFinite(r), what);
        }

        static DMesh3 Compute(ExactMeshBoolean b)
        {
            Assert.True(b.Compute(), b.Error);
            return b.Result;
        }

        static Func<int[], bool> Manual(Func<bool[], bool> f)
        {
            return w => f(w.Select(x => x > 0).ToArray());
        }

        // ((A ∪ B) − C) ∩ D: not a built-in shape
        static ExactMesh Chain(DMesh3 a, DMesh3 b, DMesh3 c, DMesh3 d)
        {
            return ExactMesh.FromDMesh(a).Union(ExactMesh.FromDMesh(b)).Difference(ExactMesh.FromDMesh(c)).Intersection(ExactMesh.FromDMesh(d));
        }

        [Theory]
        [InlineData(Ops.Union)]
        [InlineData(Ops.Intersection)]
        [InlineData(Ops.Difference)]
        public void TwoSources_SameAsExactMeshBoolean_BitForBit(Ops op)
        {
            DMesh3 a = A(), b = B();
            ExactMesh ea = ExactMesh.FromDMesh(a), eb = ExactMesh.FromDMesh(b);
            ExactMesh e = op == Ops.Union ? ea.Union(eb) : op == Ops.Intersection ? ea.Intersection(eb) : ea.Difference(eb);
            DMesh3 lazy = e.Round(), direct = Compute(new ExactMeshBoolean(a, b, op));
            AssertClean(lazy, op.ToString());
            Assert.True(lazy.TriangleCount > 0);
            Assert.Equal("", Invariance.Difference(direct, lazy));
        }

        [Fact]
        public void BuiltinShapes_SameAsTheNaryOperations()
        {
            DMesh3 a = A(), b = B(), c = C();
            ExactMesh ea = ExactMesh.FromDMesh(a), eb = ExactMesh.FromDMesh(b), ec = ExactMesh.FromDMesh(c);
            var list = new List<DMesh3> { a, b, c };
            Assert.Equal("", Invariance.Difference(Compute(new ExactMeshBoolean(list, Ops.Union)), ea.Union(eb).Union(ec).Round()));
            Assert.Equal("", Invariance.Difference(Compute(new ExactMeshBoolean(list, Ops.Intersection)), ea.Intersection(eb.Intersection(ec)).Round()));
            // A − (B ∪ C): the n-ary difference (with its cull of the parts outside A)
            Assert.Equal("", Invariance.Difference(Compute(new ExactMeshBoolean(list, Ops.Difference)), ea.Difference(eb.Union(ec)).Round()));
        }

        [Fact]
        public void Chain_IsTheNaryBooleanOfItsPredicate_AndClean()
        {
            DMesh3 a = A(), b = B(), c = C(), d = D();
            ExactMesh e = Chain(a, b, c, d);
            Assert.Equal(4, e.SourceCount);
            DMesh3 lazy = e.Round();
            AssertClean(lazy, "chain");
            Assert.True(lazy.TriangleCount > 0);
            var manual = new ExactMeshBoolean(new List<DMesh3> { a, b, c, d }, Manual(x => (x[0] || x[1]) && !x[2] && x[3]));
            Assert.Equal("", Invariance.Difference(Compute(manual), lazy));
        }

        [Fact]
        public void BoxChain_ExactVolume()
        {
            // boxes on a grid of spacing 1/4: every volume is a small dyadic number, exact in double
            DMesh3 b1 = Box(0, 0, 0, 2, 1, 1), b2 = Box(1, 0.25, 0.25, 3, 2, 0.75), b3 = Box(0.5, -1, -1, 1.5, 0.5, 2), b4 = Box(0.25, 0, 0, 2.75, 1.5, 1);
            DMesh3 r = ExactMesh.FromDMesh(b1).Union(ExactMesh.FromDMesh(b2)).Difference(ExactMesh.FromDMesh(b3)).Intersection(ExactMesh.FromDMesh(b4)).Round();
            AssertClean(r, "boxes");
            // ((b1 ∪ b2) − b3) ∩ b4 by inclusion-exclusion on the cells of the grid
            double cell = 1.0 / 64, expected = 0;
            for (double x = -1; x < 3; x += 0.25)
                for (double y = -1; y < 2; y += 0.25)
                    for (double z = -1; z < 2; z += 0.25) {
                        Vector3d p = new Vector3d(x + 0.125, y + 0.125, z + 0.125);
                        bool in1 = inside(p, 0, 0, 0, 2, 1, 1), in2 = inside(p, 1, 0.25, 0.25, 3, 2, 0.75), in3 = inside(p, 0.5, -1, -1, 1.5, 0.5, 2), in4 = inside(p, 0.25, 0, 0, 2.75, 1.5, 1);
                        if ((in1 || in2) && !in3 && in4) expected += cell;
                    }
            Assert.Equal(expected, Invariance.Volume(r));
        }

        static bool inside(Vector3d p, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return p.x > x0 && p.x < x1 && p.y > y0 && p.y < y1 && p.z > z0 && p.z < z1;
        }

        [Fact]
        public void UnionThenIntersectionWithTheSameSource_GivesTheSourceExactly()
        {
            // (A ∪ B) ∩ A = A: no intermediate rounding, so the result is A's own surface: its vertices are exactly A's
            // (the points where B cut A's triangles lie in A's flat triangles and are removed again; the faces may be
            // triangulated differently where A's quads are planar)
            DMesh3 a = A();
            ExactMesh ea = ExactMesh.FromDMesh(a);
            ExactMesh e = ea.Union(ExactMesh.FromDMesh(B())).Intersection(ea);
            Assert.Equal(2, e.SourceCount);
            DMesh3 r = e.Round();
            AssertClean(r, "(A u B) n A");
            var va = new HashSet<Vector3d>(a.Vertices());
            var vr = new HashSet<Vector3d>(r.Vertices());
            Assert.True(vr.SetEquals(va), $"{vr.Count(v => !va.Contains(v))} vertices that are not A's, {va.Count(v => !vr.Contains(v))} of A's missing");
            Assert.Equal(a.TriangleCount, r.TriangleCount);
            double v = Invariance.Volume(a);
            Assert.True(Math.Abs(Invariance.Volume(r) - v) <= 1e-14 * v, $"volume {Invariance.Volume(r)} vs {v}");
        }

        [Fact]
        public void SameSourceTwice_IsOneSource()
        {
            ExactMesh ea = ExactMesh.FromDMesh(A());
            Assert.Equal(1, ea.Union(ea).SourceCount);
            DMesh3 empty = ea.Difference(ea).Round();
            Assert.NotNull(empty);
            Assert.Equal(0, empty.TriangleCount);
            Assert.Equal("", Invariance.GeometricDifference(ea.Round(), ea.Intersection(ea).Round()));
            // a new source of the same mesh is another source
            Assert.Equal(2, ea.Union(ExactMesh.FromDMesh(A())).SourceCount);
        }

        [Fact]
        public void Inside_IsTheExpressionOnEveryWindingVector()
        {
            ExactMesh ea = ExactMesh.FromDMesh(A()), eb = ExactMesh.FromDMesh(B()), ec = ExactMesh.FromDMesh(C());
            // (A − B) ∪ (C ∩ A): A appears twice, Sources = A, B, C
            ExactMesh e = ea.Difference(eb).Union(ec.Intersection(ea));
            Assert.Equal(3, e.SourceCount);
            Func<int[], bool> pred = e.WindingPredicate();
            for (int mask = 0; mask < 27; ++mask) {
                // winding numbers -1, 0, 1 per source: only a positive one is inside
                int[] w = { mask % 3 - 1, mask / 3 % 3 - 1, mask / 9 - 1 };
                bool a = w[0] > 0, b = w[1] > 0, c = w[2] > 0;
                bool expected = (a && !b) || (c && a);
                Assert.Equal(expected, e.Inside(w));
                Assert.Equal(expected, pred(w));
            }
            Assert.Throws<ArgumentException>(() => e.Inside(new int[2]));
            Assert.Throws<ArgumentNullException>(() => e.Inside(null));
        }

        [Fact]
        public void DeepChain_NoRecursion()
        {
            // a left-deep chain of 20000 operations on two sources: built and evaluated without recursion
            ExactMesh ea = ExactMesh.FromDMesh(Box(0, 0, 0, 1, 1, 1)), eb = ExactMesh.FromDMesh(Box(2, 0, 0, 3, 1, 1));
            ExactMesh e = ea;
            for (int i = 0; i < 20000; ++i)
                e = (i % 2 == 0) ? e.Union(eb) : e.Difference(eb);
            Assert.Equal(2, e.SourceCount);
            Assert.True(e.Inside(new[] { 1, 0 }));
            Assert.False(e.Inside(new[] { 0, 1 }));      // the last operation is a difference
        }

        [Fact]
        public void Arguments()
        {
            Assert.Throws<ArgumentNullException>(() => ExactMesh.FromDMesh(null));
            ExactMesh ea = ExactMesh.FromDMesh(A());
            Assert.Throws<ArgumentNullException>(() => ea.Union(null));
            Assert.Throws<ArgumentNullException>(() => ea.Difference(null));
            Assert.Throws<ArgumentNullException>(() => ea.Intersection(null));
            Assert.True(ea.IsSource);
            Assert.False(ea.Union(ea).IsSource);
        }

        [Fact]
        public void SourceIsACopy()
        {
            DMesh3 a = A(), b = B();
            ExactMesh e = ExactMesh.FromDMesh(a).Difference(ExactMesh.FromDMesh(b));
            DMesh3 before = e.Round();
            MeshTransforms.Translate(a, new Vector3d(5, 0, 0));
            Assert.Equal("", Invariance.Difference(before, e.Round()));
            Assert.NotSame(a, e.Sources[0]);
        }

        [Fact]
        public void ToBoolean_GivesStatusAndOptions_FailureIsNull()
        {
            ExactMesh e = Chain(A(), B(), C(), D());
            ExactMeshBoolean b = e.ToBoolean();
            b.SafeRounding = false;
            b.ComputeExactVertices = true;
            Assert.True(b.Compute(), b.Error);
            Assert.Equal(b.Result.VertexCount, b.ResultExactVertices.Length);
            // a source with a non-finite coordinate: Compute() fails, Round() returns null
            DMesh3 bad = Box(0, 0, 0, 1, 1, 1);
            bad.SetVertex(0, new Vector3d(double.NaN, 0, 0));
            ExactMesh eb = ExactMesh.FromDMesh(bad).Union(ExactMesh.FromDMesh(A())).Intersection(ExactMesh.FromDMesh(B()));
            Assert.Null(eb.Round());
            ExactMeshBoolean fb = eb.ToBoolean();
            Assert.False(fb.Compute());
            Assert.Equal(ExactMeshBoolean.BooleanStatus.InvalidInput, fb.Status);
            // configure is applied before Compute()
            bool called = false;
            Assert.NotNull(e.Round(x => { called = true; x.SafeRounding = true; }));
            Assert.True(called);
        }

        [Fact]
        public void Similarities_ChainInvariant()
        {
            DMesh3 a = A(), b = B(), c = C(), d = D();
            DMesh3 r0 = Chain(a, b, c, d).Round();
            AssertClean(r0, "identity");
            double v0 = Invariance.Volume(r0), area = Invariance.Area(r0);
            foreach (Similarity sim in Similarity.All()) {
                DMesh3 r = Chain(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Invariance.Transformed(c, sim), Invariance.Transformed(d, sim)).Round();
                AssertClean(r, sim.ToString());
                DMesh3 back = Invariance.MappedBack(r, sim);
                if (sim.IsExact)
                    Assert.True(Invariance.Difference(r0, back) == "", sim.ToString());
                else {
                    // one rounding of every vertex in the transformed frame moves the surface by about its resolution
                    double tol = 1e-12 * v0 + 10 * area * sim.Resolution(2);
                    Assert.True(Math.Abs(Invariance.Volume(back) - v0) <= tol, $"{sim}: volume {Invariance.Volume(back)} vs {v0}, tolerance {tol}");
                }
            }
        }

        [Fact]
        public void Reuse_SerialEqualsParallel_Repeated()
        {
            DMesh3 a = A(), b = B(), c = C(), d = D();
            ExactMesh e = Chain(a, b, c, d);
            DMesh3 serial;
            gParallel.SerialOnThisThread = true;
            try { serial = e.Round(); } finally { gParallel.SerialOnThisThread = false; }
            for (int run = 0; run < 3; ++run)
                Assert.Equal("", Invariance.Difference(serial, e.Round()));
            // one ExactMeshBoolean computed twice, and the same sub-expression in two expressions
            ExactMeshBoolean twice = e.ToBoolean();
            Assert.True(twice.Compute(), twice.Error);
            Assert.True(twice.Compute(), twice.Error);
            Assert.Equal("", Invariance.Difference(serial, twice.Result));
            ExactMesh ab = ExactMesh.FromDMesh(a).Union(ExactMesh.FromDMesh(b));
            DMesh3 r1 = ab.Difference(ExactMesh.FromDMesh(c)).Round(), r2 = ab.Intersection(ExactMesh.FromDMesh(d)).Round();
            AssertClean(r1, "ab - c");
            AssertClean(r2, "ab n d");
            Assert.Equal("", Invariance.Difference(r1, ab.Difference(ExactMesh.FromDMesh(c)).Round()));
        }
    }
}
