using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Reducer.PairCoincidentVertices: the refusals of the option (unsupported settings, the structure checks of a pair, the entry
    /// check of the mesh) and the self-checks the tests use (QueueQuadricDifference, TriangleGridDifference), each with an input that reaches it.
    /// </summary>
    public class ReducerPairedContactsGuardTests
    {
        static DMesh3 Boxes() { return ReducerPairedContactsTests.TouchingBoxes(4, new Vector3d(2, 2, 0)); }

        static HashSet<int> ContactVertices(DMesh3 m)
        {
            var at = new Dictionary<Vector3d, List<int>>();
            foreach (int v in m.VertexIndices()) {
                Vector3d p = m.GetVertex(v);
                if (!at.TryGetValue(p, out var l)) at[p] = l = new List<int>();
                l.Add(v);
            }
            return new HashSet<int>(at.Values.Where(l => l.Count > 1).SelectMany(l => l));
        }

        sealed class StopProbe : Exception { }

        // a Reducer that looks at itself before its first collapse
        sealed class Probe : Reducer
        {
            public Probe(DMesh3 m) : base(m) { }
            public int ConstrainedAtContact;
            public HashSet<int> Contacts;
            public List<string> Diffs = new List<string>();
            public bool Tamper;

            public void Prepare() { Precompute(false); }

            protected override ProcessResult CollapseEdge(int edgeID, Vector3d vNewPos, out int collapseToV)
            {
                if (Tamper) {
                    Tamper = false;
                    Tampered();
                    throw new StopProbe();
                }
                ProcessResult r = base.CollapseEdge(edgeID, vNewPos, out collapseToV);
                if (Contacts != null && r == ProcessResult.Ignored_Constrained && edgeID <= mesh.MaxEdgeID && mesh.IsEdge(edgeID)) {
                    Index2i ev = mesh.GetEdgeV(edgeID);
                    if (Contacts.Contains(ev.a) != Contacts.Contains(ev.b))
                        ConstrainedAtContact++;
                }
                return r;
            }

            void Tampered()
            {
                // a stale quadric
                int eid = mesh.EdgeIndices().First(e => EdgeQueue.Contains(e));
                QuadricError saved = EdgeQuadrics[eid].q;
                EdgeQuadrics[eid].q.Axx += 1.0;
                Diffs.Add(QueueQuadricDifference());
                EdgeQuadrics[eid].q = saved;
                Diffs.Add(QueueQuadricDifference());

                Diffs.Add(TriangleGridDifference());
                // a stale box
                int v = mesh.VertexIndices().First();
                Vector3d p = mesh.GetVertex(v);
                mesh.SetVertex(v, p + new Vector3d(10, 10, 10));
                Diffs.Add(TriangleGridDifference());
                mesh.SetVertex(v, p);
                // one triangle more in the mesh than in the grid
                int a = mesh.AppendVertex(new Vector3d(50, 0, 0)), b = mesh.AppendVertex(new Vector3d(51, 0, 0)), c = mesh.AppendVertex(new Vector3d(50, 1, 0));
                int t = mesh.AppendTriangle(a, b, c);
                Diffs.Add(TriangleGridDifference());
                mesh.RemoveTriangle(t); mesh.RemoveVertex(a); mesh.RemoveVertex(b); mesh.RemoveVertex(c);
                Diffs.Add(TriangleGridDifference());
                // as many in the grid as in the mesh, but one of the mesh is not in the grid
                var grid = (TriangleHashGrid)typeof(Reducer).GetField("triangleGrid", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(this);
                int tid = mesh.TriangleIndices().First();
                grid.Remove(tid);
                grid.Insert(mesh.MaxTriangleID + 7, new AxisAlignedBox3d(0, 0, 0, 1, 1, 1));
                Diffs.Add(TriangleGridDifference());
            }
        }

        [Fact]
        public void KeepClearOf_IsRefused()
        {
            DMesh3 m = Boxes();
            var other = new DMesh3(m);
            var r = new Reducer(m) { PairCoincidentVertices = true, KeepClearOf = new DMeshAABBTree3(other, true) };
            var e = Assert.Throws<InvalidOperationException>(() => r.ReduceToTriangleCount(10));
            Assert.Contains("KeepClearOf", e.Message);
        }

        [Fact]
        public void BoundaryEnd_NextToAContactVertex_IsNotMovedOntoTheContact()
        {
            // an open mesh: a boundary vertex next to a contact vertex (which has a twin) is not moved (it is a boundary end, forced to stay),
            // the collapse of that edge is refused, not done with a position that moves the contact vertex
            DMesh3 start = Boxes();
            var contacts = ContactVertices(start);
            int found = 0;
            foreach (int t in start.TriangleIndices().ToList()) {
                Index3i tv = start.GetTriangle(t);
                if (contacts.Contains(tv.a) || contacts.Contains(tv.b) || contacts.Contains(tv.c))
                    continue;
                bool nextTo = false;
                for (int k = 0; k < 3 && !nextTo; ++k)
                    foreach (int nb in start.VtxVerticesItr(tv[k]))
                        nextTo |= contacts.Contains(nb);
                if (!nextTo)
                    continue;
                var m = new DMesh3(start);
                m.RemoveTriangle(t);
                var r = new Probe(m) { PairCoincidentVertices = true, PreventSelfIntersections = true, MinCollapseTriangleQuality = 0.02, Contacts = contacts };
                r.ReduceToTriangleCount(10);
                found += r.ConstrainedAtContact;
                Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
                if (found > 0)
                    break;
            }
            Assert.True(found > 0, "no edge between a boundary vertex and a contact vertex was refused");
        }

        [Fact]
        public void MeshAcceptsCollapse_RefusesWhatTheMeshRefuses()
        {
            var m = new DMesh3();
            int a = m.AppendVertex(new Vector3d(0, 0, 0)), b = m.AppendVertex(new Vector3d(1, 0, 0)), c = m.AppendVertex(new Vector3d(0, 1, 0)), d = m.AppendVertex(new Vector3d(5, 5, 5));
            m.AppendTriangle(a, b, c); m.AppendTriangle(b, a, c);      // a pillow: the edge (a, b) has the same third vertex on both sides
            Assert.False(Reducer.MeshAcceptsCollapse(m, a, b));
            Assert.False(Reducer.MeshAcceptsCollapse(m, a, 77));       // not a vertex
            Assert.False(Reducer.MeshAcceptsCollapse(m, 77, a));
            Assert.False(Reducer.MeshAcceptsCollapse(m, a, d));        // no edge
            DMesh3 box = ReducerPairedContactsTests.Box(Vector3d.Zero, 4);
            foreach (int e in box.EdgeIndices().Take(20)) {
                Index2i ev = box.GetEdgeV(e);
                var copy = new DMesh3(box);
                Assert.Equal(copy.CollapseEdge(ev.b, ev.a, out _) == MeshResult.Ok, Reducer.MeshAcceptsCollapse(box, ev.b, ev.a));
            }
        }

        [Fact]
        public void PairStructure_RefusesBoundaryEdges_BoundaryEnds_AndBowtieTwins()
        {
            // the contact edge of the boxes, with the triangle that holds it removed: the edge is a boundary edge; its twin edge is not
            DMesh3 start = Boxes();
            var contacts = ContactVertices(start);
            int edge = start.EdgeIndices().First(e => contacts.Contains(start.GetEdgeV(e).a) && contacts.Contains(start.GetEdgeV(e).b));
            Index2i et = start.GetEdgeT(edge);
            var open = new DMesh3(start);
            open.RemoveTriangle(et.a);
            var ro = new Probe(open) { PairCoincidentVertices = true };
            Assert.True(open.IsBoundaryEdge(edge));
            Assert.False(ro.PairStructureAllows(edge));                       // a boundary edge is not paired
            Index2i ev = open.GetEdgeV(edge);
            var posToVerts = open.VertexIndices().GroupBy(v => open.GetVertex(v)).ToDictionary(g => g.Key, g => g.ToList());
            int ta = posToVerts[open.GetVertex(ev.a)].First(v => v != ev.a), tb = posToVerts[open.GetVertex(ev.b)].First(v => v != ev.b);
            int twinEdge = open.FindEdge(ta, tb);
            Assert.NotEqual(DMesh3.InvalidID, twinEdge);
            Assert.False(open.IsBoundaryEdge(twinEdge));
            Assert.False(ro.PairStructureAllows(twinEdge));                   // its twin edge is a boundary edge

            // a triangle at one end of the edge removed: that end is a boundary vertex, with the boundary known (Precompute), the pair is refused
            var open2 = new DMesh3(start);
            // (a triangle that holds the end ev.a but not the edge)
            int victim = open2.VtxTrianglesItr(ev.a).First(t => t != et.a && t != et.b);
            open2.RemoveTriangle(victim);
            var r2 = new Probe(open2) { PairCoincidentVertices = true };
            Assert.True(r2.PairStructureAllows(edge));                         // (boundary not known yet: ensure_vertex_pairs only)
            r2.Prepare();
            Assert.False(r2.PairStructureAllows(edge));

            // three tetrahedra: P and R each hold one of two coincident edges, S is a third one at the vertex ta of R: ta is a bowtie
            var m = new DMesh3();
            int[] P = Tet(m, new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
            int[] R = Tet(m, new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, -1));
            var rn = new Probe(m) { PairCoincidentVertices = true };
            Assert.False(rn.PairStructureAllows(m.FindEdge(P[0], P[1])));      // (two tetrahedra: the mesh does not collapse them)
            AppendTetAt(m, R[0], new Vector3d(-1, 0, 0), new Vector3d(-1, 1, 0), new Vector3d(-1, 0, 1));
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            var rb = new Probe(m) { PairCoincidentVertices = true };
            Assert.False(rb.PairStructureAllows(m.FindEdge(P[0], P[1])));      // R[0] is a bowtie now
        }

        static int[] Tet(DMesh3 m, Vector3d p0, Vector3d p1, Vector3d p2, Vector3d p3)
        {
            int[] v = { m.AppendVertex(p0), m.AppendVertex(p1), m.AppendVertex(p2), m.AppendVertex(p3) };
            AppendFaces(m, v);
            return v;
        }

        static void AppendTetAt(DMesh3 m, int v0, Vector3d p1, Vector3d p2, Vector3d p3)
        {
            AppendFaces(m, new[] { v0, m.AppendVertex(p1), m.AppendVertex(p2), m.AppendVertex(p3) });
        }

        static void AppendFaces(DMesh3 m, int[] v)
        {
            m.AppendTriangle(v[0], v[1], v[2]); m.AppendTriangle(v[0], v[3], v[1]); m.AppendTriangle(v[1], v[3], v[2]); m.AppendTriangle(v[2], v[3], v[0]);
        }

        [Fact]
        public void SelfChecks_ReportWhatTheyFind()
        {
            DMesh3 m = Boxes();
            Assert.Equal("no grid", new Reducer(m) { PairCoincidentVertices = true }.TriangleGridDifference());
            var r = new Probe(m) { PairCoincidentVertices = true, PreventSelfIntersections = true, Tamper = true };
            Assert.Throws<StopProbe>(() => r.ReduceToTriangleCount(10));
            Assert.Equal(7, r.Diffs.Count);
            Assert.Contains("stale quadric", r.Diffs[0]);
            Assert.Equal("", r.Diffs[1]);
            Assert.Equal("", r.Diffs[2]);
            Assert.Contains("stale box", r.Diffs[3]);
            Assert.Contains("the grid holds", r.Diffs[4]);
            Assert.Equal("", r.Diffs[5]);
            Assert.Contains("is not in the grid", r.Diffs[6]);
        }
    }
}
