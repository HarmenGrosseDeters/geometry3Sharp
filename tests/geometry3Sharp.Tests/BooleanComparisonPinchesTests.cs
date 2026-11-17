using System;
using System.Collections.Generic;
using System.Linq;
using BoolCmp;
using g3;
using gs;
using Xunit;
using Ops = g3.ExactMeshBoolean.Operations;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// The eval's pinch split of benchmarks/BooleanComparison (Pinches.cs; docs/research/BOOLEAN_TRIAGE_2026-11.md, RC2): a result
    /// welded by exact position is split at the edges and vertices where the solid touches itself, so that it is a closed manifold
    /// mesh with duplicated vertices there; touching at those positions is not a self-intersection. Before, the eval appended the
    /// triangles that did not fit at a pinch edge on their own vertices and reported the result "not closed".
    /// </summary>
    public class BooleanComparisonPinchesTests
    {
        // prism over a ccw polygon (ear clipping), z in [z0, z1], outward
        static DMesh3 Prism(double[][] poly, double z0, double z1)
        {
            int n = poly.Length;
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i][0], poly[i][1], z0));
            for (int i = 0; i < n; ++i) m.AppendVertex(new Vector3d(poly[i][0], poly[i][1], z1));
            var idx = Enumerable.Range(0, n).ToList();
            var caps = new List<Index3i>();
            Func<int, int, int, double> cross = (a, b, c) =>
                (poly[b][0] - poly[a][0]) * (poly[c][1] - poly[a][1]) - (poly[b][1] - poly[a][1]) * (poly[c][0] - poly[a][0]);
            while (idx.Count > 3)
                for (int k = 0; k < idx.Count; ++k) {
                    int a = idx[(k + idx.Count - 1) % idx.Count], b = idx[k], c = idx[(k + 1) % idx.Count];
                    if (cross(a, b, c) <= 0) continue;
                    if (idx.Any(q => q != a && q != b && q != c && cross(a, b, q) >= 0 && cross(b, c, q) >= 0 && cross(c, a, q) >= 0)) continue;
                    caps.Add(new Index3i(a, b, c));
                    idx.RemoveAt(k);
                    break;
                }
            caps.Add(new Index3i(idx[0], idx[1], idx[2]));
            foreach (Index3i t in caps) { m.AppendTriangle(t.a + n, t.b + n, t.c + n); m.AppendTriangle(t.a, t.c, t.b); }
            for (int i = 0; i < n; ++i) { int j = (i + 1) % n; m.AppendTriangle(i, j, j + n); m.AppendTriangle(i, j + n, i + n); }
            return m;
        }

        static DMesh3 Box(double x0, double y0, double z0, double x1, double y1, double z1) =>
            Prism(new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } }, z0, z1);

        static DMesh3 Boolean(DMesh3 a, DMesh3 b, Ops op)
        {
            var boolean = new ExactMeshBoolean(a, b, op);
            Assert.True(boolean.Compute(), boolean.Error);
            return boolean.Result;
        }

        static DMesh3 Soup(params DMesh3[] meshes)
        {
            var s = new DMesh3();
            foreach (DMesh3 m in meshes) new MeshEditor(s).AppendMesh(m);
            return s;
        }

        // the eval's weld by exact position (Program.cs: degenerate triangles of the weld dropped)
        static Pinches.Split SplitOf(DMesh3 result)
        {
            var ids = new Dictionary<Vector3d, int>();
            var pos = new List<Vector3d>();
            var map = new int[result.MaxVertexID];
            foreach (int v in result.VertexIndices()) {
                Vector3d p = result.GetVertex(v);
                if (ids.TryGetValue(p, out int id) == false) { ids[p] = id = pos.Count; pos.Add(p); }
                map[v] = id;
            }
            var tris = new List<Index3i>();
            foreach (int t in result.TriangleIndices()) {
                Index3i f = result.GetTriangle(t);
                var g = new Index3i(map[f.a], map[f.b], map[f.c]);
                if (g.a != g.b && g.b != g.c && g.a != g.c) tris.Add(g);
            }
            return Pinches.Build(pos, tris);
        }

        static void AssertCleanExceptPinches(Pinches.Split s, int edges, int vertices, string what)
        {
            Assert.True(s.Mesh.IsClosed(), what + ": not closed");
            Assert.Equal("", MeshRobustRepair.NotCleanReason(s.Mesh, false));
            Assert.Equal(0, Pinches.SelfIntersectionsBeyondContacts(s.Mesh));
            Assert.Equal(edges, s.PinchEdges);
            Assert.Equal(vertices, s.PinchVertices);
            Assert.Equal(0, s.UnpairedEdges);
        }

        // #994's mechanism: two L-prisms in face contact on z = 1; along y = 1, x in [1, 2] the union touches itself
        static DMesh3 LPrismsUnion() => Boolean(
            Prism(new[] { new[] { 0.0, 0 }, new[] { 2.0, 0 }, new[] { 2.0, 1 }, new[] { 1.0, 1 }, new[] { 1.0, 2 }, new[] { 0.0, 2 } }, 0, 1),
            Prism(new[] { new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 2.0, 1 }, new[] { 2.0, 2 }, new[] { 0.0, 2 } }, 1, 2), Ops.Union);

        [Fact]
        public void FaceContactUnion_EdgePinch_IsCleanExceptPinches()
        {
            DMesh3 u = LPrismsUnion();
            // (the split pinch is not clean by NotCleanReason's self-intersection test, which compares vertex ids: unchanged, owner 2026-11-17)
            Assert.Equal("self-intersections", MeshRobustRepair.NotCleanReason(u, true));
            // the pinch edge (1,1,1)-(2,1,1): the surface is connected around (1,1,1), so only (2,1,1) gets two copies
            Pinches.Split s = SplitOf(u);
            AssertCleanExceptPinches(s, 1, 1, "L-prisms union");
            Assert.Equal(u.TriangleCount, s.Mesh.TriangleCount);
        }

        [Fact]
        public void MirrorDifference_EdgePinch_IsCleanExceptPinches()
        {
            // #993's mechanism: A = two boxes, A - mirror(A) across x = 3 leaves two boxes that touch along x = 2, y = 2
            DMesh3 a = Boolean(Box(0, 0, 2, 4, 2, 4), Box(2, 2, 1, 3, 4, 4), Ops.Union);
            DMesh3 m = new DMesh3(a);
            foreach (int v in m.VertexIndices()) { Vector3d p = m.GetVertex(v); p.x = 6 - p.x; m.SetVertex(v, p); }
            m.ReverseOrientation();
            Pinches.Split s = SplitOf(Boolean(a, m, Ops.Difference));
            Assert.True(s.PinchEdges >= 1);
            AssertCleanExceptPinches(s, s.PinchEdges, s.PinchVertices, "A - mirror(A)");
        }

        [Fact]
        public void CubesTouchingAtAVertex_BowtieIsAPinchVertex()
        {
            Pinches.Split s = SplitOf(Soup(Box(0, 0, 0, 1, 1, 1), Box(1, 1, 1, 2, 2, 2)));
            AssertCleanExceptPinches(s, 0, 1, "vertex contact");
        }

        [Fact]
        public void CubesTouchingAlongAnEdge_WeldedSoup_IsSplitByAngle()
        {
            // a welded soup (as CGAL writes one vertex per position): the edge x = y = 1 has four triangles
            Pinches.Split s = SplitOf(Soup(Box(0, 0, 0, 1, 1, 1), Box(1, 1, 0, 2, 2, 1)));
            AssertCleanExceptPinches(s, 1, 2, "edge contact");
            var components = new MeshConnectedComponents(s.Mesh);
            components.FindConnectedT();
            Assert.Equal(2, components.Count);
        }

        [Fact]
        public void PlainSolid_HasNoPinches()
        {
            Pinches.Split s = SplitOf(Box(0, 0, 0, 1, 2, 3));
            AssertCleanExceptPinches(s, 0, 0, "box");
        }

        [Fact]
        public void OverlappingSoup_StillSelfIntersects()
        {
            Pinches.Split s = SplitOf(Soup(Box(0, 0, 0, 2, 2, 2), Box(1, 1, 1, 3, 3, 3)));
            Assert.True(Pinches.SelfIntersectionsBeyondContacts(s.Mesh) > 0);
            Assert.Equal(0, s.PinchVertices);
        }

        [Fact]
        public void OpenMesh_StaysOpen_AndAnOddEdgeIsUnpaired()
        {
            DMesh3 open = Box(0, 0, 0, 1, 1, 1);
            open.RemoveTriangle(open.TriangleIndices().First());
            Assert.False(SplitOf(open).Mesh.IsClosed());
            // three triangles on one edge: a box plus one fin triangle on its edge (0,0,0)-(1,0,0)
            Pinches.Split s = SplitOf(BuildWithExtra(Box(0, 0, 0, 1, 1, 1), new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0.5, -1, -1)));
            Assert.Equal(1, s.UnpairedEdges);
            Assert.Equal(0, s.PinchEdges);
            Assert.False(s.Mesh.IsClosed());
        }

        // the mesh's triangles plus one triangle (p, q, r) as a separate soup triangle
        static DMesh3 BuildWithExtra(DMesh3 m, Vector3d p, Vector3d q, Vector3d r)
        {
            var s = new DMesh3(m);
            s.AppendTriangle(s.AppendVertex(p), s.AppendVertex(q), s.AppendVertex(r));
            return s;
        }

        /// <summary> invariance (Similarity.All()): the pinch counts and the verdicts do not depend on the frame </summary>
        [Fact]
        public void PinchCounts_InvariantUnderSimilarities()
        {
            DMesh3 u = LPrismsUnion();
            Pinches.Split s0 = SplitOf(u);
            foreach (Similarity sim in Similarity.All()) {
                Pinches.Split s = SplitOf(Invariance.Transformed(u, sim));
                Assert.Equal(s0.PinchEdges, s.PinchEdges);
                Assert.Equal(s0.PinchVertices, s.PinchVertices);
                Assert.True(s.Mesh.IsClosed(), sim.ToString());
                Assert.Equal(0, Pinches.SelfIntersectionsBeyondContacts(s.Mesh));
                if (sim.IsExact)
                    Assert.Equal("", Invariance.Difference(Invariance.Transformed(s0.Mesh, sim), s.Mesh));
            }
        }
    }
}
