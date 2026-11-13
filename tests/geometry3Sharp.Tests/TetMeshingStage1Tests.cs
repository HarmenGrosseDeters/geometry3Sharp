using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Stage 1 of docs/design/TET_MESHING.md (section 14): MeshTetrahedralization refines the inside of the alpha wrap. The result is
    /// valid (independent exact oracle), its boundary is the wrap surface triangle by triangle, every inside tet not counted in
    /// unrefined_bad_tets meets the radius-edge and dihedral bounds, and the run is deterministic, reusable and scales by 2^k bit for bit.
    /// </summary>
    public class TetMeshingStage1Tests
    {
        static DMesh3 Sphere(double r, int n) => new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        static DMesh3 Box() => new TrivialBox3Generator() { Box = new Box3d(new Vector3d(0.5, 0.25, 0.125), new Vector3d(0.5, 0.25, 0.125)) }.Generate().MakeDMesh();
        static DMesh3 TwoSpheres()
        {
            var a = Sphere(1, 5); var b = Sphere(1, 5);
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(1.2, 0.3, 0));
            new MeshEditor(a).AppendMesh(b);                        // a soup of two intersecting closed surfaces
            return a;
        }

        static DMesh3 Input(string name)
        {
            switch (name) {
                case "sphere": return Sphere(1, 5);
                case "box": return Box();
                default: return TwoSpheres();
            }
        }

        public static IEnumerable<object[]> Inputs() => new[] { "sphere", "box", "two spheres" }.Select(n => new object[] { n });

        static MeshTetrahedralization Make(DMesh3 m) => new MeshTetrahedralization(m) { RelativeAlpha = 12, RelativeOffset = 200 };

        // bad by the refiner's own criteria: radius-edge above rho or smallest dihedral below the sliver bound, or degenerate
        static int Bad(TetMesh3 t, double rho, double sliver)
        {
            int n = 0;
            foreach (Index4i q in t.Tets)
                if (!TetMeshQuality.Measure(t.Vertices[q.a], t.Vertices[q.b], t.Vertices[q.c], t.Vertices[q.d], out double re, out double mind, out _)
                    || !(re <= rho) || mind < sliver) n++;
            return n;
        }

        [Theory]
        [MemberData(nameof(Inputs))]
        public void Result_IsValid_BoundedByTheWrap_AndMeetsTheBoundsOutsideTheCountedTets(string name)
        {
            var tm = Make(Input(name));
            Assert.True(tm.Compute(), name + ": " + tm.Status);
            TetMesh3 t = tm.Result;
            Assert.Null(TetMeshQuality.NotValidReason(t));
            Assert.Null(TetOracle.Check(t, null));                   // exact: positive tets, matched facets, volume identity
            DMesh3 boundary = t.ToSurfaceMesh();
            Assert.False(MeshRepairMetrics.HasSelfIntersections(boundary));
            BoundaryFromWrapAroundInput(tm, t);
            NothingPeelableLeft(tm);
            TetMesh3 raw = tm.Wrap.ExtractTetMesh();
            Assert.True(t.Tets.Count > raw.Tets.Count && tm.Counts["tet_splits"] > 0);
            // the bounds hold for every tet except the ones the result counts
            int bad = Bad(t, tm.MaxRadiusEdgeRatio, tm.SliverDihedralDegrees);
            Assert.Equal(tm.Counts["unrefined_bad_tets"], bad);
            int rawBad = Bad(raw, tm.MaxRadiusEdgeRatio, tm.SliverDihedralDegrees);
            Assert.True(bad < 0.7 * rawBad && bad <= 0.06 * t.Tets.Count, bad + " of " + t.Tets.Count + ", raw " + rawBad + " of " + raw.Tets.Count);
            // all counted ones touch the surface with at least two wrap vertices (the surface is fixed, section 14): no bad tet is
            // left inside
            Assert.Equal(0, tm.Counts["bad_with_0_wrap_vertices"] + tm.Counts["bad_with_1_wrap_vertices"]);
        }

        // the boundary after peeling: only wrap vertices, closed (NotValidReason), and no boundary triangle touches the input (exact)
        static void BoundaryFromWrapAroundInput(MeshTetrahedralization tm, TetMesh3 t)
        {
            var wrapVertices = new HashSet<Vector3d>(tm.Wrap.Result.Vertices());
            foreach (Index3i f in t.BoundaryTriangles)
                for (int j = 0; j < 3; ++j) Assert.Contains(t.Vertices[f[j]], wrapVertices);
            var tree = new DMeshAABBTree3(tm.Mesh, true);
            var tris = t.BoundaryTriangles.Select(f => new Triangle3d(t.Vertices[f.a], t.Vertices[f.b], t.Vertices[f.c])).ToList();
            Assert.False(Reducer.TrianglesTouch(tree, tris));
            // unpeeled: the wrap exactly; peeled: differs from it
            Assert.Equal(tm.Counts["peeled"] == 0, TetOracle.Check(t, tm.Wrap.Result) == null);
        }

        [Theory]
        [MemberData(nameof(Inputs))]
        public void WithoutPeeling_TheBoundaryIsTheWrapExactly(string name)
        {
            var tm = Make(Input(name)); tm.PeelBoundarySlivers = false;
            Assert.True(tm.Compute());
            Assert.Null(TetOracle.Check(tm.Result, tm.Wrap.Result));   // the boundary = the wrap's triangles, exactly
            Assert.Equal(0, tm.Counts["peeled"]);
        }

        [Fact]
        public void Peeling_LiftsTheBoxToTenDegrees()
        {
            var box = new MeshTetrahedralization(Box());
            Assert.True(box.Compute());
            Assert.True(box.Counts["peeled"] > 0);
            Assert.True(TetMeshQuality.Measure(box.Result).MinDihedral.Worst >= 10, TetMeshQuality.Measure(box.Result).MinDihedral.Worst.ToString());
            BoundaryFromWrapAroundInput(box, box.Result);
            var plain = new MeshTetrahedralization(Box()) { PeelBoundarySlivers = false };
            Assert.True(plain.Compute());
            Assert.True(TetMeshQuality.Measure(plain.Result).MinDihedral.Worst < 5);
        }

        static DMesh3 Torus(int major, int minor, double R, double r)
        {
            var m = new DMesh3();
            for (int i = 0; i < major; ++i)
                for (int j = 0; j < minor; ++j) {
                    double u = 2 * Math.PI * i / major, v = 2 * Math.PI * j / minor;
                    m.AppendVertex(new Vector3d((R + r * Math.Cos(v)) * Math.Cos(u), (R + r * Math.Cos(v)) * Math.Sin(u), r * Math.Sin(v)));
                }
            for (int i = 0; i < major; ++i)
                for (int j = 0; j < minor; ++j) {
                    int a = i * minor + j, b = ((i + 1) % major) * minor + j, c = ((i + 1) % major) * minor + (j + 1) % minor, d = i * minor + (j + 1) % minor;
                    m.AppendTriangle(a, b, c); m.AppendTriangle(a, c, d);
                }
            return m;
        }

        [Fact]
        public void Peeling_NeverExposesTheInput()
        {
            // a thin torus at the default offset: many boundary slivers would cut the input if peeled (measured: 157 refused)
            var tm = new MeshTetrahedralization(Torus(64, 16, 1, 0.1));
            Assert.True(tm.Compute());
            Assert.True(tm.Counts["peel_refused_input"] > 20 && tm.Counts["peeled"] > 10, tm.Counts["peel_refused_input"] + " / " + tm.Counts["peeled"]);
            Assert.Null(TetOracle.Check(tm.Result, null));
            BoundaryFromWrapAroundInput(tm, tm.Result);
            NothingPeelableLeft(tm);
        }

        // after Compute: no bad tet with exactly two boundary facets is left that could be peeled (the final sweep)
        static void NothingPeelableLeft(MeshTetrahedralization tm)
        {
            TetMesh3 t = tm.Result;
            var bfaces = new HashSet<(int, int, int)>(t.BoundaryTriangles.Select(f => Key(f.a, f.b, f.c)));
            var bedges = new HashSet<(int, int)>();
            foreach (Index3i f in t.BoundaryTriangles) { bedges.Add(E(f.a, f.b)); bedges.Add(E(f.b, f.c)); bedges.Add(E(f.c, f.a)); }
            var tree = new DMeshAABBTree3(tm.Mesh, true);
            int checkedTets = 0;
            foreach (Index4i q in t.Tets) {
                if (TetMeshQuality.Measure(t.Vertices[q.a], t.Vertices[q.b], t.Vertices[q.c], t.Vertices[q.d], out double re, out double mind, out _)
                    && re <= tm.MaxRadiusEdgeRatio && mind >= tm.SliverDihedralDegrees) continue;
                var opp = new List<int>();
                for (int i = 0; i < 4; ++i) {
                    var f = Enumerable.Range(0, 4).Where(j => j != i).Select(j => q[j]).ToArray();
                    if (bfaces.Contains(Key(f[0], f[1], f[2]))) opp.Add(q[i]);
                }
                if (opp.Count != 2) continue;
                checkedTets++;
                bool edgeOnSurface = bedges.Contains(E(opp[0], opp[1]));
                bool touches = MeshTetrahedralization.Refiner.TetTouchesMesh(tree, t.Vertices[q.a], t.Vertices[q.b], t.Vertices[q.c], t.Vertices[q.d]);
                Assert.True(edgeOnSurface || touches, "a peelable bad tet is left");
            }
        }
        static (int, int, int) Key(int a, int b, int c) { var x = new[] { a, b, c }; Array.Sort(x); return (x[0], x[1], x[2]); }
        static (int, int) E(int a, int b) => a < b ? (a, b) : (b, a);

        [Fact]
        public void FinalSweep_PeelsWhatEarlierPeelsExposed()
        {
            // two intersecting spheres (a soup) at the default parameters: a peel exposes a neighbour that the final sweep peels
            var a = Sphere(1, 12); var b = Sphere(1, 12);
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(1.2, 0.3, 0));
            new MeshEditor(a).AppendMesh(b);
            var tm = new MeshTetrahedralization(a);
            Assert.True(tm.Compute());
            Assert.True(tm.Counts["peeled_in_sweep"] > 0, tm.Counts["peeled_in_sweep"] + " of " + tm.Counts["peeled"]);
            NothingPeelableLeft(tm);
            BoundaryFromWrapAroundInput(tm, tm.Result);
        }

        [Fact]
        public void TryPeel_RefusesWhenTheNewEdgeIsOnTheSurface()
        {
            // labels at random on a random Delaunay3 (the surface need not be manifold here: only the local rule is tested),
            // against a brute-force test of "(p, q) is on a facet between differently labelled cells other than c"
            var rnd = new Random(31);
            var dt = new Delaunay3(new AxisAlignedBox3d(Vector3d.Zero, Vector3d.One)) { RefinementSupport = true };
            for (int i = 0; i < 300; ++i) dt.Insert(new Vector3d(0.05 + 0.9 * rnd.NextDouble(), 0.05 + 0.9 * rnd.NextDouble(), 0.05 + 0.9 * rnd.NextDouble()), -1, 0, out _);
            foreach (int c in dt.CellIndices().ToList()) dt.SetCellLabel(c, rnd.Next(3) == 0 ? 0 : 1);
            var r = new MeshTetrahedralization.Refiner(dt, 0, 2, 0, 10, 1000, null) { Input = new DMesh3() };
            int refused = 0, peeled = 0;
            foreach (int c in dt.CellIndices().ToList()) {
                if (dt.CellLabel(c) != 1) continue;
                var surf = Enumerable.Range(0, 4).Where(i => dt.Neighbour(c, i) < 0 || dt.CellLabel(dt.Neighbour(c, i)) != 1).ToList();
                if (surf.Count != 2) continue;
                int p = dt.CellVertex(c, surf[0]), q = dt.CellVertex(c, surf[1]);
                bool onSurface = false;
                foreach (int k in dt.CellIndices()) {
                    if (k == c || dt.VertexIndexIn(k, p) < 0 || dt.VertexIndexIn(k, q) < 0) continue;
                    for (int i = 0; i < 4; ++i) {
                        int w = dt.CellVertex(k, i);
                        if (w == p || w == q) continue;
                        int n = dt.Neighbour(k, i);
                        if (n == c) continue;
                        if (dt.CellLabel(k) != (n < 0 ? 0 : dt.CellLabel(n))) onSurface = true;
                    }
                }
                bool done = r.TryPeel(c);
                Assert.Equal(!onSurface, done);
                if (done) { peeled++; dt.SetCellLabel(c, 1); } else refused++;   // restore, so the next cases see the same labels
            }
            Assert.True(refused > 10 && peeled > 10, refused + " / " + peeled);
        }

        [Fact]
        public void TetTouchesMesh_ExactCases()
        {
            Vector3d a = Vector3d.Zero, b = Vector3d.AxisX, c = Vector3d.AxisY, d = Vector3d.AxisZ;
            bool Touch(params Vector3d[] tri)
            {
                var m = new DMesh3(); foreach (var v in tri) m.AppendVertex(v); m.AppendTriangle(0, 1, 2);
                var tree = new DMeshAABBTree3(m, true);
                return MeshTetrahedralization.Refiner.TetTouchesMesh(tree, a, b, c, d);
            }
            Assert.True(Touch(new Vector3d(0.1, 0.1, 0.1), new Vector3d(0.2, 0.1, 0.1), new Vector3d(0.1, 0.2, 0.1)));     // inside
            Assert.True(Touch(new Vector3d(-1, 0.1, 0.1), new Vector3d(2, 0.1, 0.1), new Vector3d(0.1, 0.1, 3)));           // crosses
            Assert.True(Touch(new Vector3d(0.5, 0.5, 0), new Vector3d(1, 1, -1), new Vector3d(1, 0.5, -1)));                // touches an edge point
            Assert.False(Touch(new Vector3d(0.5, 0.5, 0.01), new Vector3d(1, 1, 1), new Vector3d(1, 0.5, 1)));             // just beyond face bcd
            Assert.False(Touch(new Vector3d(2, 2, 2), new Vector3d(3, 2, 2), new Vector3d(2, 3, 2)));                      // far
            Assert.True(Touch(new Vector3d(0.1, 0.1, 0.1), new Vector3d(0.2, 0.2, 0.2), new Vector3d(0.3, 0.3, 0.3)));     // degenerate, inside
            Assert.True(Touch(new Vector3d(0.2, 0.2, -0.1), new Vector3d(0.25, 0.2, 0.02), new Vector3d(0.2, 0.25, -0.1))); // through face abc only
        }

        [Fact]
        public void Sphere_EveryTetMeetsRadiusEdge2AndDihedral10()
        {
            var tm = new MeshTetrahedralization(Sphere(1, 16));
            Assert.True(tm.Compute());
            Assert.Equal(0, tm.Counts["unrefined_bad_tets"]);
            TetQualityStatistics q = TetMeshQuality.Measure(tm.Result);
            Assert.True(q.MinDihedral.Worst >= 10, q.MinDihedral.Worst.ToString());
            Assert.True(q.RadiusEdge.Worst <= 2, q.RadiusEdge.Worst.ToString());
            BoundaryFromWrapAroundInput(tm, tm.Result);
        }

        [Fact]
        public void WithoutTheSliverStep_SliversRemain()
        {
            var plain = new MeshTetrahedralization(Sphere(1, 16)) { SliverDihedralDegrees = 0 };
            Assert.True(plain.Compute());
            Assert.Null(TetMeshQuality.NotValidReason(plain.Result));
            Assert.True(TetMeshQuality.Measure(plain.Result).MinDihedral.Worst < 5);   // radius-edge alone leaves slivers
            Assert.True(TetMeshQuality.Measure(plain.Result).RadiusEdge.Worst <= 2);
        }

        [Fact]
        public void SizeBound_GivesMoreAndSmallerTets()
        {
            var a = Make(Sphere(1, 5)); Assert.True(a.Compute());
            var b = Make(Sphere(1, 5)); b.TargetEdgeLength = 0.15; Assert.True(b.Compute());
            Assert.True(b.Result.Tets.Count > 2 * a.Result.Tets.Count, a.Result.Tets.Count + " / " + b.Result.Tets.Count);
            Assert.Null(TetOracle.Check(b.Result, null));
            int large = 0;
            foreach (Index4i q in b.Result.Tets) {
                Vector3d cc = Delaunay3.Circumcenter(b.Result.Vertices[q.a], b.Result.Vertices[q.b], b.Result.Vertices[q.c], b.Result.Vertices[q.d]);
                if (cc.Distance(b.Result.Vertices[q.a]) > 0.15) large++;
            }
            Assert.True(large <= b.Counts["unrefined_bad_tets"], large + " > " + b.Counts["unrefined_bad_tets"]);
        }

        [Fact]
        public void ReuseAndRepeatedRuns_GiveTheSameTets()
        {
            var a = Make(Box()); Assert.True(a.Compute());
            TetMesh3 r1 = a.Result;
            Assert.True(a.Compute());                               // reuse of one instance
            var b = Make(Box()); Assert.True(b.Compute());          // a fresh instance
            foreach (TetMesh3 r in new[] { a.Result, b.Result }) {
                Assert.Equal(r1.Vertices, r.Vertices); Assert.Equal(r1.Tets, r.Tets); Assert.Equal(r1.BoundaryTriangles, r.BoundaryTriangles);
            }
            Assert.Equal(a.Counts, b.Counts);
        }

        [Fact]
        public void PointLimitAndCancel()
        {
            var lim = Make(Sphere(1, 5)); lim.MaxSteinerPoints = 5;
            Assert.False(lim.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.PointLimitReached, lim.Status);
            Assert.Null(TetOracle.Check(lim.Result, null));                  // valid, only less refined
            Assert.Equal(5, lim.Counts["tet_splits"]);
            int calls = 0;
            var cancel = Make(Sphere(1, 5)); cancel.CancelF = () => ++calls > 200;
            Assert.False(cancel.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.Cancelled, cancel.Status);
            Assert.Null(cancel.Result);
            var empty = new MeshTetrahedralization(new DMesh3());
            Assert.False(empty.Compute());
            Assert.Equal(MeshTetrahedralization.TetStatus.WrapFailed, empty.Status);
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 box = Box();
            var a = Make(box); Assert.True(a.Compute());
            var b = Make(Invariance.Transformed(box, s)); Assert.True(b.Compute(), s.Name + " " + b.Status);
            Assert.Null(TetOracle.Check(b.Result, null));
            BoundaryFromWrapAroundInput(b, b.Result);
            if (s.IsExact) {                                         // 2^k scales: the same tets, vertex by vertex, scaled
                Assert.Equal(a.Result.Tets, b.Result.Tets);
                Assert.Equal(a.Result.BoundaryTriangles, b.Result.BoundaryTriangles);
                for (int i = 0; i < a.Result.Vertices.Count; ++i)
                    Assert.Equal(s.Scale * a.Result.Vertices[i], b.Result.Vertices[i]);
                Assert.Equal(a.Counts, b.Counts);
            } else {
                // rotated / translated: rounding changes the wrap, so compare sizes and the share of counted tets
                // (measured on this box: 562 .. 955 tets against 891, 24 .. 56 counted against 48; a rotated box wraps differently)
                Assert.InRange(b.Result.Tets.Count, 0.5 * a.Result.Tets.Count, 2.0 * a.Result.Tets.Count);
                Assert.True(b.Counts["unrefined_bad_tets"] <= 0.1 * b.Result.Tets.Count, b.Counts["unrefined_bad_tets"] + " of " + b.Result.Tets.Count);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();
    }
}
