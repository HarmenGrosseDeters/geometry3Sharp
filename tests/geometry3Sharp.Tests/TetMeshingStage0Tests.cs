using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary> Stage 0 of docs/design/TET_MESHING.md: the inside cells of the alpha wrap as a tetrahedral mesh, its validity and quality measures. </summary>
    public class TetMeshingStage0Tests
    {
        static DMesh3 Sphere(double r, int n) => new Sphere3Generator_NormalizedCube() { Radius = r, EdgeVertices = n }.Generate().MakeDMesh();
        static DMesh3 Box(Vector3d min, Vector3d max) =>
            new TrivialBox3Generator() { Box = new Box3d(0.5 * (min + max), 0.5 * (max - min)) }.Generate().MakeDMesh();
        static DMesh3 Cylinder() => new CappedCylinderGenerator() { BaseRadius = 0.5f, TopRadius = 0.5f, Height = 1.5f, Slices = 16 }.Generate().MakeDMesh();
        static DMesh3 TwoSpheres()
        {
            var a = Sphere(1, 5); var b = Sphere(1, 5);
            foreach (int v in b.VertexIndices()) b.SetVertex(v, b.GetVertex(v) + new Vector3d(1.2, 0.3, 0));
            var ed = new MeshEditor(a); ed.AppendMesh(b);       // a soup of two intersecting closed surfaces
            return a;
        }
        static DMesh3 Triangle() { var m = new DMesh3(); m.AppendVertex(Vector3d.Zero); m.AppendVertex(Vector3d.AxisX); m.AppendVertex(Vector3d.AxisY); m.AppendTriangle(0, 1, 2); return m; }

        public static IEnumerable<object[]> Inputs()
        {
            yield return new object[] { "sphere" };
            yield return new object[] { "box" };
            yield return new object[] { "cylinder" };
            yield return new object[] { "two spheres" };
            yield return new object[] { "open triangle" };
        }

        static DMesh3 Input(string name)
        {
            switch (name) {
                case "sphere": return Sphere(1, 5);
                case "box": return Box(Vector3d.Zero, new Vector3d(1, 0.5, 0.25));
                case "cylinder": return Cylinder();
                case "two spheres": return TwoSpheres();
                default: return Triangle();
            }
        }

        static MeshAlphaWrap Wrap(DMesh3 m, bool tri = false) =>
            tri ? new MeshAlphaWrap(m) { Alpha = 0.2, Offset = 0.02 } : new MeshAlphaWrap(m) { RelativeAlpha = 12, RelativeOffset = 200 };

        static TetMesh3 Tets(string name, out MeshAlphaWrap w)
        {
            w = Wrap(Input(name), name == "open triangle");
            Assert.True(w.Compute(), name + ": " + w.Status + " " + w.Error);
            TetMesh3 t = w.ExtractTetMesh();
            Assert.NotNull(t);
            return t;
        }

        [Theory]
        [MemberData(nameof(Inputs))]
        public void InsideCells_AreAValidTetMesh_ConformingToTheWrap(string name)
        {
            TetMesh3 t = Tets(name, out var w);
            Assert.True(t.Tets.Count > 0);
            Assert.Null(TetMeshQuality.NotValidReason(t));
            Assert.Null(TetOracle.Check(t, w.Result));              // independent exact oracle: positive, conforming, volume identity
            Assert.Equal(w.Result.TriangleCount, t.BoundaryTriangles.Count);
            DMesh3 s = t.ToSurfaceMesh();
            Assert.True(s.IsClosed());
            TetQualityStatistics q = TetMeshQuality.Measure(t);
            Assert.Equal(t.Tets.Count, q.Tets);
            Assert.Equal(0, q.Degenerate);                          // exactly positive, so no zero-volume cell
            double wrapVolume = MeshMeasurements.VolumeArea(w.Result, w.Result.TriangleIndices(), w.Result.GetVertex).x;
            Assert.Equal(wrapVolume, q.TotalVolume, 8);
        }

        [Fact]
        public void ReuseAndRepeatedRuns_GiveTheSameTets()
        {
            var w = Wrap(Sphere(1, 5));
            Assert.True(w.Compute());
            TetMesh3 a = w.ExtractTetMesh();
            Assert.True(w.Compute());                               // reuse of one instance
            TetMesh3 b = w.ExtractTetMesh();
            var w2 = Wrap(Sphere(1, 5));
            Assert.True(w2.Compute());
            TetMesh3 c = w2.ExtractTetMesh();                       // a fresh instance
            Assert.Equal(a.Vertices, b.Vertices); Assert.Equal(a.Tets, b.Tets); Assert.Equal(a.BoundaryTriangles, b.BoundaryTriangles);
            Assert.Equal(a.Vertices, c.Vertices); Assert.Equal(a.Tets, c.Tets);
        }

        [Fact]
        public void ExtractTetMesh_NullWithoutSuccess()
        {
            Assert.Null(new MeshAlphaWrap(Sphere(1, 4)).ExtractTetMesh());
        }

        [Theory]
        [MemberData(nameof(SimilarityNames))]
        public void Similarity_ValidAndPow2ScalesBitForBit(string simName)
        {
            Similarity s = Similarity.ByName(simName);
            DMesh3 box = Box(Vector3d.Zero, new Vector3d(1, 0.5, 0.25));
            var w0 = Wrap(box); Assert.True(w0.Compute());
            TetMesh3 t0 = w0.ExtractTetMesh();
            var w = Wrap(Invariance.Transformed(box, s)); Assert.True(w.Compute(), s.Name + " " + w.Status);
            TetMesh3 t = w.ExtractTetMesh();
            Assert.Null(TetMeshQuality.NotValidReason(t));
            Assert.Null(TetOracle.Check(t, w.Result));
            TetQualityStatistics q = TetMeshQuality.Measure(t);
            Assert.Equal(0, q.Degenerate);
            if (s.IsExact) {                                         // 2^k scales: the same tets, vertex by vertex, scaled
                Assert.Equal(t0.Tets, t.Tets);
                Assert.Equal(t0.BoundaryTriangles, t.BoundaryTriangles);
                for (int i = 0; i < t0.Vertices.Count; ++i)
                    Assert.Equal(s.Scale * t0.Vertices[i], t.Vertices[i]);
                TetQualityStatistics q0 = TetMeshQuality.Measure(t0);
                Assert.Equal(q0.RadiusEdge.Worst, q.RadiusEdge.Worst);
                Assert.Equal(q0.MinDihedral.Worst, q.MinDihedral.Worst);
            }
        }
        public static IEnumerable<object[]> SimilarityNames() => Similarity.Names();

        // ---- the measures ----

        [Fact]
        public void Measure_RegularAndCornerTetrahedra()
        {
            var a = new Vector3d(1, 1, 1); var b = new Vector3d(1, -1, -1); var c = new Vector3d(-1, 1, -1); var d = new Vector3d(-1, -1, 1);
            Assert.True(TetMeshQuality.Measure(a, c, b, d, out double re, out double mn, out double mx));
            Assert.Equal(Math.Sqrt(6) / 4, re, 12);
            Assert.Equal(Math.Acos(1.0 / 3) * 180 / Math.PI, mn, 9);
            Assert.Equal(mn, mx, 9);
            // the corner of a cube: three right dihedral angles at the axes, acos(1/sqrt(3)) = 54.7356 at the other edges
            Assert.True(TetMeshQuality.Measure(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, Vector3d.AxisZ, out re, out mn, out mx));
            Assert.Equal(Math.Sqrt(3) / 2, re, 12);                 // R = sqrt(3)/2, shortest edge 1
            Assert.Equal(Math.Acos(1 / Math.Sqrt(3)) * 180 / Math.PI, mn, 9);
            Assert.Equal(90, mx, 9);
        }

        [Fact]
        public void Measure_Sliver_And_FlatTetrahedron()
        {
            // four points near the equator of their circumsphere: radius-edge small, min dihedral near 0
            double e = 1e-3;
            Assert.True(TetMeshQuality.Measure(new Vector3d(1, 0, e), new Vector3d(-1, 0, e), new Vector3d(0, 1, -e), new Vector3d(0, -1, -e), out double re, out double mn, out double mx));
            Assert.True(re < 1.0, $"{re} {mn} {mx}"); Assert.InRange(mn, 0.1, 0.3); Assert.InRange(mx, 179.7, 179.9);   // about 2e radians (0.11 .. 0.23 degrees) at the near-flat edges
            Assert.False(TetMeshQuality.Measure(Vector3d.Zero, Vector3d.AxisX, Vector3d.AxisY, new Vector3d(1, 1, 0), out re, out mn, out mx));
            Assert.True(double.IsPositiveInfinity(re)); Assert.Equal(0, mn); Assert.Equal(180, mx);
        }

        // ---- the validity checks reject broken meshes (mutations of a valid one) ----

        static TetMesh3 Cube6()
        {
            var t = new TetMesh3();
            for (int i = 0; i < 8; ++i) t.Vertices.Add(new Vector3d(i & 1, (i >> 1) & 1, (i >> 2) & 1));
            int[][] tets = { new[] { 0, 1, 3, 7 }, new[] { 0, 3, 2, 7 }, new[] { 0, 2, 6, 7 }, new[] { 0, 6, 4, 7 }, new[] { 0, 4, 5, 7 }, new[] { 0, 5, 1, 7 } };
            foreach (int[] q in tets) {
                var v = q;
                if (ExactPredicates.Orient3d(t.Vertices[v[0]], t.Vertices[v[1]], t.Vertices[v[2]], t.Vertices[v[3]]) < 0) v = new[] { v[1], v[0], v[2], v[3] };
                t.Tets.Add(new Index4i(v[0], v[1], v[2], v[3]));
            }
            // boundary: facets without partner, from the oracle-independent construction by directed face counting
            var count = new Dictionary<(int, int, int), int>();
            Func<int, int, int, (int, int, int)> canon = (a, b, c) => a <= b && a <= c ? (a, b, c) : b <= a && b <= c ? (b, c, a) : (c, a, b);
            foreach (Index4i q in t.Tets)
                for (int i = 0; i < 4; ++i)
                    count[canon(q[Delaunay3.FacetIndices[i, 0]], q[Delaunay3.FacetIndices[i, 1]], q[Delaunay3.FacetIndices[i, 2]])] = 1;
            foreach (var k in count.Keys)
                if (!count.ContainsKey(canon(k.Item1, k.Item3, k.Item2))) t.BoundaryTriangles.Add(new Index3i(k.Item1, k.Item2, k.Item3));
            return t;
        }

        [Fact]
        public void ValidityChecks_AcceptACubeAndRejectMutants()
        {
            TetMesh3 ok = Cube6();
            Assert.Equal(12, ok.BoundaryTriangles.Count);
            Assert.Null(TetMeshQuality.NotValidReason(ok));
            Assert.Null(TetOracle.Check(ok, null));

            var flipped = Cube6(); flipped.Tets[2] = new Index4i(flipped.Tets[2].b, flipped.Tets[2].a, flipped.Tets[2].c, flipped.Tets[2].d);
            Assert.NotNull(TetMeshQuality.NotValidReason(flipped)); Assert.NotNull(TetOracle.Check(flipped, null));

            var missing = Cube6(); missing.Tets.RemoveAt(3);                      // a hole: facets lose their partner
            Assert.NotNull(TetMeshQuality.NotValidReason(missing)); Assert.NotNull(TetOracle.Check(missing, null));

            var dropTri = Cube6(); dropTri.BoundaryTriangles.RemoveAt(0);
            Assert.NotNull(TetMeshQuality.NotValidReason(dropTri)); Assert.NotNull(TetOracle.Check(dropTri, null));

            var wrongTri = Cube6(); var b0 = wrongTri.BoundaryTriangles[0]; wrongTri.BoundaryTriangles[0] = new Index3i(b0.a, b0.c, b0.b);
            Assert.NotNull(TetMeshQuality.NotValidReason(wrongTri)); Assert.NotNull(TetOracle.Check(wrongTri, null));

            var moved = Cube6(); moved.Vertices[7] = new Vector3d(1, 1, 1.5);   // a different but still valid mesh (all tets stay positive, facets match)
            Assert.Null(TetMeshQuality.NotValidReason(moved)); Assert.Null(TetOracle.Check(moved, null));

            var inverted = Cube6(); inverted.Vertices[7] = new Vector3d(-0.5, -0.5, -0.5); // the apex of every tet crosses the opposite facet: orientations flip
            Assert.NotNull(TetMeshQuality.NotValidReason(inverted)); Assert.NotNull(TetOracle.Check(inverted, null));

            var flat = Cube6(); flat.Vertices[7] = new Vector3d(0.5, 0.5, 0);   // tet (0,1,3,7) gets volume 0
            Assert.NotNull(TetMeshQuality.NotValidReason(flat)); Assert.NotNull(TetOracle.Check(flat, null));

            var dup = Cube6(); dup.Tets.Add(dup.Tets[0]);
            Assert.NotNull(TetMeshQuality.NotValidReason(dup)); Assert.NotNull(TetOracle.Check(dup, null));

            var range = Cube6(); range.Tets[0] = new Index4i(0, 1, 3, 99);
            Assert.NotNull(TetMeshQuality.NotValidReason(range));
        }
    }
}
