using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// QuadRemesher.BuildFaces splits a vertex whose kept faces form more than one fan into one copy per fan, so that the polygon mesh is manifold at every
    /// vertex (docs/design/QUAD_REMESHING.md 4.2, "narrow strip"). The root cause, a degenerate star of the extraction on strips whose width is not a
    /// multiple of the lattice spacing, is not fixed by it: the repair keeps the faces and the positions.
    /// </summary>
    public class QuadNonManifoldVertexTests
    {
        // an open strip of 1 x width with a smooth height, nx x ny cells
        static DMesh3 Strip(int nx, int ny, double width)
        {
            var m = new DMesh3();
            int[,] ids = new int[nx + 1, ny + 1];
            for (int j = 0; j <= ny; ++j)
                for (int i = 0; i <= nx; ++i)
                    ids[i, j] = m.AppendVertex(new Vector3d((double)i / nx, width * j / ny, 0.1 * Math.Sin(3.0 * i / nx)));
            for (int j = 0; j < ny; ++j)
                for (int i = 0; i < nx; ++i) {
                    m.AppendTriangle(ids[i, j], ids[i + 1, j], ids[i + 1, j + 1]);
                    m.AppendTriangle(ids[i, j], ids[i + 1, j + 1], ids[i, j + 1]);
                }
            return m;
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (double w in new[] { 0.1, 0.2, 0.3 })
                foreach (double s in new[] { 0.04, 0.06 })
                    yield return new object[] { w, s };
        }

        static QuadRemesher RunStrip(double w, double s, DMesh3 m = null, double sizeScale = 1)
        {
            m = m ?? Strip(100, (int)Math.Round(w / 0.0125), w);
            var cf = new CrossField(m);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(m) { Field = cf, TargetEdgeLength = s * sizeScale };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            return qr;
        }

        [Theory, MemberData(nameof(Cases))]
        public void Strip_ResultIsManifold_AndFaithful(double w, double s)
        {
            QuadRemesher qr = RunStrip(w, s);
            Assert.True(qr.Result.Validate().IsValid);
            for (int v = 0; v < qr.Result.VertexCount; ++v) Assert.True(qr.Result.GetVertex(v).IsFinite);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(m.IsManifold, "non-manifold: edges " + m.NonManifoldEdges + ", inconsistent " + m.InconsistentEdges + ", vertices " + m.NonManifoldVertices);
            Assert.True(m.Faces > 10 && m.MaxSides <= 6);
            // the copies sit on the vertex: the mesh stays near the input
            int[] map; DMesh3 tri = qr.Result.ToDMesh3(out map);
            var hd = new MeshHausdorffDistance(tri, Strip(100, (int)Math.Round(w / 0.0125), w)) { Symmetric = false, RelativeTolerance = 1e-3 };
            hd.Compute();
            // the ragged lattice of a strip is up to about 2.3 s off the input (0.09 for s = 0.04), with or without the split: only a sanity bound
            Assert.True(hd.UpperBound <= 3 * s, "Hausdorff " + hd.UpperBound);
        }

        [Fact]
        public void Strip_ResultIsTheSameRepeatedSerialAndParallel()
        {
            QuadRemesher a = RunStrip(0.1, 0.04);
            QuadRemesher b = RunStrip(0.1, 0.04);
            var cf = new CrossField(Strip(100, 8, 0.1)); cf.Compute();
            var c = new QuadRemesher(Strip(100, 8, 0.1)) { Field = cf, TargetEdgeLength = 0.04, Parallel = false };
            Assert.Equal(QuadRemeshStatus.Success, c.Compute());
            foreach (DPolyMesh r in new[] { b.Result, c.Result }) {
                Assert.Equal(a.Result.VertexCount, r.VertexCount);
                Assert.Equal(a.Result.FaceIndices.ToArray(), r.FaceIndices.ToArray());
                Assert.Equal(a.Result.FaceOffsets.ToArray(), r.FaceOffsets.ToArray());
                for (int v = 0; v < r.VertexCount; ++v) Assert.Equal(a.Result.GetVertex(v), r.GetVertex(v));
            }
        }

        public static IEnumerable<object[]> Frames() => Similarity.Names();

        [Theory, MemberData(nameof(Frames))]
        public void Strip_IsInvariantUnderSimilarities(string name)
        {
            Similarity sim = Similarity.ByName(name);
            DMesh3 m = Strip(100, 8, 0.1);
            DPolyMesh baseResult = RunStrip(0.1, 0.04, m).Result;
            DMesh3 t = Invariance.Transformed(m, sim);
            var cf = new CrossField(t);
            Assert.Equal(CrossFieldStatus.Success, cf.Compute());
            var qr = new QuadRemesher(t) { Field = cf, TargetEdgeLength = sim.Scale * 0.04 };
            Assert.Equal(QuadRemeshStatus.Success, qr.Compute());
            PolyMeshMetrics pm = PolyMeshMetrics.Compute(qr.Result);
            Assert.True(pm.IsManifold, name);
            if (sim.IsExact) {
                Assert.Equal(baseResult.VertexCount, qr.Result.VertexCount);
                Assert.Equal(baseResult.FaceIndices.ToArray(), qr.Result.FaceIndices.ToArray());
                for (int v = 0; v < baseResult.VertexCount; ++v) {
                    Vector3d p = baseResult.GetVertex(v), q = qr.Result.GetVertex(v);
                    Assert.Equal(BitConverter.DoubleToInt64Bits(p.x * sim.Scale), BitConverter.DoubleToInt64Bits(q.x));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(p.y * sim.Scale), BitConverter.DoubleToInt64Bits(q.y));
                    Assert.Equal(BitConverter.DoubleToInt64Bits(p.z * sim.Scale), BitConverter.DoubleToInt64Bits(q.z));
                }
            } else {
                // rounding changes the degenerate lattice of a strip a lot (the extraction there is not stable): only the kind of mesh is checked
                Assert.InRange(pm.Faces, 0.5 * baseResult.FaceCount, 2.0 * baseResult.FaceCount);
            }
        }

        // ---------------------------------------------------------------- BuildFaces on small graphs

        static QuadRemesher.PolyGraph Graph(IEnumerable<(double x, double y)> pts, IEnumerable<(int a, int b)> edges)
        {
            var g = new QuadRemesher.PolyGraph();
            foreach (var p in pts) g.Add(new Vector3d(p.x, p.y, 0), Vector3d.AxisZ, 0);
            foreach (var e in edges) g.Link(e.a, e.b);
            return g;
        }

        [Fact]
        public void BuildFaces_SplitsAVertexWhereTwoQuadsTouch()
        {
            // two unit squares that share only the corner (0, 0): a vertex of two fans
            var pts = new List<(double, double)> { (0, 0), (1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1) };
            var edges = new List<(int, int)> { (0, 1), (1, 2), (2, 3), (3, 0), (0, 4), (4, 5), (5, 6), (6, 0) };
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(Graph(pts, edges), 6, out dropped);
            Assert.Equal(2, r.FaceCount);
            Assert.Equal(8, r.VertexCount);          // 7 vertices and one copy of the shared corner
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold);
            Assert.Equal(2, m.Quads);
            int atOrigin = 0;
            for (int v = 0; v < r.VertexCount; ++v) if (r.GetVertex(v).LengthSquared == 0) atOrigin++;
            Assert.Equal(2, atOrigin);
            // the copies are numbered by the smallest face index of the fan: the same result however often it is built
            DPolyMesh r2 = QuadRemesher.BuildFaces(Graph(pts, edges), 6, out dropped);
            Assert.Equal(r.FaceIndices.ToArray(), r2.FaceIndices.ToArray());
            Assert.Equal(r.FaceOffsets.ToArray(), r2.FaceOffsets.ToArray());
        }

        [Fact]
        public void BuildFaces_SplitsAnInteriorVertexOfValence4WhoseOppositeFacesAreDropped()
        {
            // the vertex (0, 0) has four neighbours; the unit squares in the quadrants 1 and 3 are kept, the faces in the quadrants 2 and 4 have 8 sides
            // (extra vertices on their outer edges) and are dropped: the interior vertex is left with two fans
            var pts = new List<(double, double)> { (0, 0), (1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1),
                                                   (-1, 0.33), (-1, 0.66), (-1, 1), (-0.66, 1), (-0.33, 1),      // quadrant 2 (with (0, 1) and (-1, 0))
                                                   (1, -0.33), (1, -0.66), (1, -1), (0.66, -1), (0.33, -1) };     // quadrant 4 (with (0, -1) and (1, 0))
            var edges = new List<(int, int)> { (0, 1), (1, 2), (2, 3), (3, 0), (0, 4), (4, 5), (5, 6), (6, 0),
                                               (4, 7), (7, 8), (8, 9), (9, 10), (10, 11), (11, 3),
                                               (1, 12), (12, 13), (13, 14), (14, 15), (15, 16), (16, 6) };
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(Graph(pts, edges), 6, out dropped);
            Assert.True(dropped >= 3, "dropped " + dropped);                // two holes and the outer loop
            Assert.Equal(2, r.FaceCount);
            PolyMeshMetrics m = PolyMeshMetrics.Compute(r);
            Assert.True(m.IsManifold, "non-manifold vertices " + m.NonManifoldVertices);
            int atOrigin = 0;
            for (int v = 0; v < r.VertexCount; ++v) if (r.GetVertex(v).LengthSquared == 0) atOrigin++;
            Assert.Equal(2, atOrigin);
        }

        [Fact]
        public void BuildFaces_ManifoldGraphsAreBuiltAsBefore()
        {
            var g = new QuadRemesher.PolyGraph();
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) g.Add(new Vector3d(i, j, 0), Vector3d.AxisZ, 0);
            for (int j = 0; j < 4; ++j) for (int i = 0; i < 4; ++i) { if (i + 1 < 4) g.Link(j * 4 + i, j * 4 + i + 1); if (j + 1 < 4) g.Link(j * 4 + i, (j + 1) * 4 + i); }
            int dropped;
            DPolyMesh r = QuadRemesher.BuildFaces(g, 6, out dropped);
            Assert.Equal(16, r.VertexCount);         // no copy
            Assert.Equal(9, r.FaceCount);
            Assert.True(PolyMeshMetrics.Compute(r).IsManifold);
        }
    }
}
