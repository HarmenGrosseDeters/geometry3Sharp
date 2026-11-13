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
            Assert.Null(TetOracle.Check(t, tm.Wrap.Result));       // exact: positive tets, the boundary = the wrap's triangles, volume identity
            Assert.False(MeshRepairMetrics.HasSelfIntersections(t.ToSurfaceMesh()));
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

        [Fact]
        public void Sphere_EveryTetMeetsRadiusEdge2AndDihedral10()
        {
            var tm = new MeshTetrahedralization(Sphere(1, 16));
            Assert.True(tm.Compute());
            Assert.Equal(0, tm.Counts["unrefined_bad_tets"]);
            TetQualityStatistics q = TetMeshQuality.Measure(tm.Result);
            Assert.True(q.MinDihedral.Worst >= 10, q.MinDihedral.Worst.ToString());
            Assert.True(q.RadiusEdge.Worst <= 2, q.RadiusEdge.Worst.ToString());
            Assert.Null(TetOracle.Check(tm.Result, tm.Wrap.Result));
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
            Assert.Null(TetOracle.Check(b.Result, b.Wrap.Result));
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
            Assert.Null(TetOracle.Check(lim.Result, lim.Wrap.Result));      // valid, only less refined
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
            Assert.Null(TetOracle.Check(b.Result, b.Wrap.Result));
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
