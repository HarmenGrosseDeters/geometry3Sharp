using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// A small far part must not make SafeRounding delete the rest. Two unit spheres and a unit tetrahedron 1e8 away:
    /// the result's bounding box diagonal is 1.7e8, and with tolerances relative to it (degenerate area 1e-12 diag^2,
    /// maxLength 1e-5 diag) every sphere triangle counted as degenerate and was contracted, the rest removed as tiny:
    /// Success, RoundingClean, 0 triangles. The result must keep the spheres' union and the tetrahedron.
    /// </summary>
    public class ExactBooleanFarComponentTests
    {
        static DMesh3 Sphere(Vector3d c)
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            MeshTransforms.Translate(m, c);
            return m;
        }

        static void AddTetrahedron(DMesh3 m, Vector3d p, double size)
        {
            int v0 = m.AppendVertex(p), v1 = m.AppendVertex(p + size * Vector3d.AxisX), v2 = m.AppendVertex(p + size * Vector3d.AxisY), v3 = m.AppendVertex(p + size * Vector3d.AxisZ);
            m.AppendTriangle(v0, v2, v1); m.AppendTriangle(v0, v1, v3); m.AppendTriangle(v0, v3, v2); m.AppendTriangle(v1, v2, v3);
        }

        // (per component, relative to a point of it: far from the origin the plain sum cancels)
        static double Volume(DMesh3 m)
        {
            var comps = new MeshConnectedComponents(m);
            comps.FindConnectedT();
            double volume = 0;
            foreach (var comp in comps.Components) {
                Vector3d o = m.GetVertex(m.GetTriangle(comp.Indices[0]).a);
                foreach (int t in comp.Indices) {
                    Index3i tv = m.GetTriangle(t);
                    volume += (m.GetVertex(tv.a) - o).Dot((m.GetVertex(tv.b) - o).Cross(m.GetVertex(tv.c) - o)) / 6;
                }
            }
            return volume;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (double far in new[] { 1e4, 1e8, 1e12 })
                foreach (Ops op in new[] { Ops.Union, Ops.Intersection, Ops.Difference })
                    foreach (bool sphere in new[] { false, true })
                        yield return new object[] { far, op, sphere };
        }

        // the far part: a unit tetrahedron, or a small sphere (radius 0.1)
        static double AddFar(DMesh3 a, double far, bool sphere)
        {
            var p = new Vector3d(far, far, far);
            if (sphere == false) {
                AddTetrahedron(a, p, 1);
                return 1.0 / 6;
            }
            DMesh3 s = new Sphere3Generator_NormalizedCube() { Radius = 0.1, EdgeVertices = 8 }.Generate().MakeDMesh();
            MeshTransforms.Translate(s, p);
            double v = Volume(s);
            new MeshEditor(a).AppendMesh(s);
            return v;
        }

        static ExactMeshBoolean Run(DMesh3 a, DMesh3 b, Ops op)
        {
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), op);
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void FarPart_SpheresKept(double far, Ops op, bool sphere)
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            // the reference: the same operation without the far part
            ExactMeshBoolean near = Run(a, b, op);
            double expected = Volume(near.Result);
            // (the same far part in both: the intersection keeps it too)
            double farVolume = AddFar(a, far, sphere);
            AddFar(b, far, sphere);
            ExactMeshBoolean boolean = Run(a, b, op);
            DMesh3 r = boolean.Result;
            double total = expected + (op == Ops.Difference ? 0 : farVolume);
            Assert.True(r.TriangleCount >= near.Result.TriangleCount / 2, $"far {far} {op}: {r.TriangleCount} triangles left of {near.Result.TriangleCount}");
            Assert.True(Math.Abs(Volume(r) - total) <= 1e-6 * total + 1e-6, $"far {far} {op}: volume {Volume(r)}, expected {total}");
            Assert.True(r.IsClosed() && Invariance.AllFinite(r));
            Assert.True(boolean.RoundingClean, $"far {far} {op}: not clean");
            // the spheres' part is the same as without the far part (same tolerances: the largest component's)
            Assert.Equal(near.Rounding.MaxDrift, boolean.Rounding.MaxDrift);
        }

        // a unit sphere with a fold that a move repairs with a drift of about 1.2e-3 (above the bound 1e-5 diag = 3.5e-5,
        // below the far part's ulp term 256 ulp(1e12) = 0.03), and needles of 1e-10 on every 7th edge (degenerate by
        // the area 1e-12 diag^2 of the sphere's diagonal, not by that of the far tetrahedron's)
        static DMesh3 FoldedSphere()
        {
            DMesh3 m = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 20 }.Generate().MakeDMesh();
            int v = 0, w = m.VtxVerticesItr(v).First();
            Vector3d pv = m.GetVertex(v), pw = m.GetVertex(w);
            m.SetVertex(v, pw + 0.01 * (pw - pv).Normalized);
            foreach (int eid in m.EdgeIndices().ToArray()) {
                Index2i ev = m.GetEdgeV(eid);
                if (eid % 7 != 0 || ev.a == v || ev.b == v)
                    continue;
                Vector3d p = m.GetVertex(ev.a), q = m.GetVertex(ev.b);
                Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out DMesh3.EdgeSplitInfo info));
                m.SetVertex(info.vNew, p + 1e-10 * (q - p).Normalized);
            }
            return m;
        }

        // the part near the origin: the triangles with a vertex within 10 of it
        static DMesh3 NearPart(DMesh3 m)
        {
            DMesh3 r = new DMesh3(m);
            foreach (int t in r.TriangleIndices().ToArray())
                if (r.GetTriVertex(t, 0).Length > 10)
                    r.RemoveTriangle(t, true, false);
            return r;
        }

        [Fact]
        public void FarPart_NearToleranceKept_NoCoarserBoundFromTheFarPart()
        {
            // the far part's ulp term and diagonal are its own: the sphere is repaired as without it (the needles), the
            // fold stays (it needs more than the sphere's bound), and the drift is within 1e-5 of the sphere's diagonal.
            // The far part first, last and in random orders (the size is the largest component's, not the first's)
            DMesh3 sphere = FoldedSphere();
            double diag = sphere.GetBounds().DiagonalLength;
            DMesh3 reference = new DMesh3(sphere);
            var r0 = new MeshRoundingRepair(reference);
            r0.Apply();
            Assert.True(r0.Collapses > 0 && r0.RemainingIntersections > 0, $"collapses {r0.Collapses}, left {r0.RemainingIntersections}");
            var farFirst = new DMesh3();
            AddTetrahedron(farFirst, new Vector3d(1e12, 1e12, 1e12), 1);
            new MeshEditor(farFirst).AppendMesh(sphere);
            var farLast = new DMesh3(sphere);
            AddTetrahedron(farLast, new Vector3d(1e12, 1e12, 1e12), 1);
            var inputs = new List<(string, DMesh3)> { ("first", farFirst), ("last", farLast) };
            for (int seed = 1; seed <= 3; ++seed)
                inputs.Add(($"permuted {seed}", Invariance.Permuted(farLast, seed)));
            foreach (var (name, input) in inputs) {
                DMesh3 m = new DMesh3(input);
                var r = new MeshRoundingRepair(m);
                r.Apply();
                // (in another order the edits may differ in detail: the same defects, the same bound, the fold left)
                Assert.True(r.MaxDrift <= 1e-5 * diag, $"{name}: drift {r.MaxDrift:R}");
                Assert.Equal(r0.InitialDegenerate, r.InitialDegenerate);
                Assert.Equal(r0.InitialIntersections, r.InitialIntersections);
                Assert.Equal(r0.RemainingDegenerate, r.RemainingDegenerate);
                Assert.True(r.RemainingIntersections > 0 && r.Collapses > 0, $"{name}: left {r.RemainingIntersections}, collapses {r.Collapses}");
                Assert.Equal(m.TriangleCount - 4, NearPart(m).TriangleCount);
                if (name == "last") {
                    Assert.Equal(r0.MaxDrift, r.MaxDrift);
                    Assert.True(Invariance.GeometricDifference(reference, NearPart(m)) == "", name);
                }
            }
        }

        [Fact]
        public void NotFiniteComponent_SameInEveryOrder()
        {
            // a component at x = +infinity has a NaN diagonal (inf - inf): the size is then NaN whatever the order of the
            // components (every edit rejected, nothing removed, not clean), not the sphere's diagonal if the far part
            // comes first; an infinite size would remove the sphere as tiny
            DMesh3 sphere = FoldedSphere();
            DMesh3 inf = new DMesh3();
            int a = inf.AppendVertex(new Vector3d(double.PositiveInfinity, 0, 0)), b = inf.AppendVertex(new Vector3d(double.PositiveInfinity, 1, 0)), c = inf.AppendVertex(new Vector3d(double.PositiveInfinity, 0, 1));
            inf.AppendTriangle(a, b, c);
            DMesh3 first = new DMesh3(inf), last = new DMesh3(sphere);
            new MeshEditor(first).AppendMesh(sphere);
            new MeshEditor(last).AppendMesh(inf);
            var results = new List<MeshRoundingRepair>();
            foreach (DMesh3 input in new[] { first, last }) {
                DMesh3 m = new DMesh3(input);
                var r = new MeshRoundingRepair(m);
                Assert.False(r.Apply());
                Assert.Equal(0, r.Collapses + r.Flips + r.Moves + r.Zips);
                Assert.Equal(0, r.RemovedComponents);
                Assert.Equal(input.TriangleCount, m.TriangleCount);
                results.Add(r);
            }
            Assert.Equal(results[0].InitialDegenerate, results[1].InitialDegenerate);
            Assert.Equal(results[0].InitialIntersections, results[1].InitialIntersections);
            Assert.True(double.IsNaN(results[0].MaxLength) && double.IsNaN(results[1].MaxLength));
        }

        [Fact]
        public void FarPart_Similarities_Pow2BitIdentical()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            AddFar(a, 1e8, false);
            ExactMeshBoolean b0 = Run(a, b, Ops.Union);
            foreach (Similarity sim in Similarity.All()) {
                ExactMeshBoolean bs = Run(Invariance.Transformed(a, sim), Invariance.Transformed(b, sim), Ops.Union);
                Assert.True(bs.Result.TriangleCount >= b0.Result.TriangleCount / 2, $"{sim}: {bs.Result.TriangleCount} triangles");
                Assert.True(bs.Result.IsClosed() && Invariance.AllFinite(bs.Result), sim.ToString());
                if (sim.IsExact)
                    Assert.True(Invariance.Difference(b0.Result, Invariance.MappedBack(bs.Result, sim)) == "", sim.ToString());
            }
        }

        [Fact]
        public void FarPart_ComputeTwice_SerialEqualsParallel()
        {
            DMesh3 a = Sphere(Vector3d.Zero), b = Sphere(new Vector3d(0.5, 0.3, 0.2));
            AddFar(a, 1e8, true);
            gParallel.SerialOnThisThread = true;
            DMesh3 serial;
            try { serial = Run(a, b, Ops.Difference).Result; } finally { gParallel.SerialOnThisThread = false; }
            Assert.True(Invariance.Difference(serial, Run(a, b, Ops.Difference).Result) == "");
            var boolean = new ExactMeshBoolean(new DMesh3(a), new DMesh3(b), Ops.Difference);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(boolean.Compute(), boolean.Error);
            Assert.True(Invariance.Difference(serial, boolean.Result) == "");
        }
    }
}
