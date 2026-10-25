using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in mesh/ from the class-by-class review (docs/Analyse-2026-10.md, section 2.2).
    /// </summary>
    public class MeshFixesTests
    {
        static DMesh3 Sphere(int edgeVertices = 6) { return new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = edgeVertices }.Generate().MakeDMesh(); }

        /// <summary> flat n x n grid of unit squares in the XZ plane, two triangles each </summary>
        static DMesh3 Grid(int n)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i, 0, j));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, c, d);
                    m.AppendTriangle(a, d, b);
                }
            return m;
        }

        // --- DMesh3 ---

        [Fact]
        public void CompactInPlace_MapVCoversUnmovedVertices_AndUpdatesTimestamp()
        {
            DMesh3 mesh = Sphere();
            foreach (int tid in mesh.TriangleIndices().Take(10).ToArray())
                mesh.RemoveTriangle(tid, true, false);
            var before = mesh.VertexIndices().ToDictionary(v => v, v => mesh.GetVertex(v));
            int ts = mesh.ShapeTimestamp;
            DMesh3.CompactInfo info = mesh.CompactInPlace(true);
            Assert.True(mesh.IsCompact);
            Assert.NotEqual(ts, mesh.ShapeTimestamp);
            foreach (var kv in before) {
                int newID = info.MapV[kv.Key];
                Assert.True(mesh.IsVertex(newID), $"vertex {kv.Key} maps to {newID}");
                Assert.Equal(kv.Value, mesh.GetVertex(newID));
            }
        }

        [Fact]
        public void TriangleGroups_AllocateReturnsUnusedIds()
        {
            DMesh3 mesh = Grid(2);      // no groups yet (the generated spheres already have some)
            mesh.EnableTriangleGroups(3);
            int g1 = mesh.AllocateTriangleGroup(), g2 = mesh.AllocateTriangleGroup();
            Assert.Equal(4, g1);
            Assert.Equal(5, g2);
            Assert.Equal(6, mesh.MaxGroupID);

            var m2 = new DMesh3();
            m2.EnableTriangleGroups();
            m2.AppendTriangle(m2.AppendVertex(Vector3d.Zero), m2.AppendVertex(Vector3d.AxisX), m2.AppendVertex(Vector3d.AxisY), 0);
            Assert.Equal(1, m2.AllocateTriangleGroup());       // 0 is in use, 1 is the next free id
        }

        [Fact]
        public void Copy_DropsGroupsAndInvalidatesCaches()
        {
            DMesh3 target = Sphere();
            target.EnableTriangleGroups(5);
            AxisAlignedBox3d oldBounds = target.CachedBounds;
            DMesh3 source = Grid(2);
            target.Copy(source);
            Assert.False(target.HasTriangleGroups);
            Assert.True(target.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(2, target.CachedBounds.Max.x, 12);
            Assert.NotEqual(oldBounds.Max.x, target.CachedBounds.Max.x);
        }

        [Fact]
        public void SetTriangle_ReorientBoundaryTriangle()
        {
            var mesh = new DMesh3();
            int a = mesh.AppendVertex(Vector3d.Zero), b = mesh.AppendVertex(Vector3d.AxisX), c = mesh.AppendVertex(Vector3d.AxisY);
            int tid = mesh.AppendTriangle(a, b, c);
            Assert.Equal(MeshResult.Ok, mesh.SetTriangle(tid, new Index3i(b, a, c), false));
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(MeshResult.Ok, mesh.SetTriangle(tid, new Index3i(a, b, c), true));
            Assert.True(mesh.CheckValidity(false, FailMode.ReturnOnly));
            Assert.Equal(new Index3i(a, b, c), mesh.GetTriangle(tid));
        }

        [Fact]
        public void DMesh3Builder_Build_GroupsFollowTheirTriangles()
        {
            // the third triangle would make edge (0,1) non-manifold and is skipped
            var vertices = new[] { new Vector3d(0, 0, 0), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, -1, 0), new Vector3d(0, 0, 1), new Vector3d(2, 2, 2) };
            var triangles = new[] { new Index3i(0, 1, 2), new Index3i(1, 0, 3), new Index3i(0, 1, 4), new Index3i(1, 5, 2) };
            var groups = new[] { 10, 11, 12, 13 };
            DMesh3 mesh = DMesh3Builder.Build<Vector3d, Index3i, Vector3f>(vertices, triangles, null, groups);
            Assert.Equal(3, mesh.TriangleCount);
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                int input = Array.FindIndex(triangles, x => x == t);
                Assert.Equal(groups[input], mesh.GetTriangleGroup(tid));
            }
        }

        // --- NTMesh3 ---

        [Fact]
        public void NTMesh3_OrientedBoundaryEdge()
        {
            var m = new NTMesh3();
            int a = m.AppendVertex(Vector3d.Zero), b = m.AppendVertex(Vector3d.AxisX), c = m.AppendVertex(Vector3d.AxisY), d = m.AppendVertex(new Vector3d(1, 1, 0));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(b, d, c);
            int eid = m.FindEdge(b, d);
            Assert.Equal(new Index2i(b, d), m.GetOrientedBoundaryEdgeV(eid));
            Assert.Equal(new Index2i(c, a), m.GetOrientedBoundaryEdgeV(m.FindEdge(a, c)));
        }

        [Fact]
        public void NTMesh3_PokeTriangle_UsesBarycentricCoordinates()
        {
            var m = new NTMesh3();
            int tid = m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(Vector3d.AxisX), m.AppendVertex(Vector3d.AxisY));
            Assert.Equal(MeshResult.Ok, m.PokeTriangle(tid, new Vector3d(0.8, 0.1, 0.1), out NTMesh3.PokeTriangleInfo info));
            Assert.True(m.GetVertex(info.new_vid).Distance(new Vector3d(0.1, 0.1, 0)) < 1e-12);
        }

        [Fact]
        public void NTMesh3_FromDMesh3_CopiesNormalsAndGroups()
        {
            DMesh3 src = Grid(3);
            MeshNormals.QuickCompute(src);
            src.EnableTriangleGroups(7);
            var nt = new NTMesh3(src);
            Assert.True(nt.HasVertexNormals);
            foreach (int vid in src.VertexIndices())
                Assert.Equal(src.GetVertexNormal(vid), nt.GetVertexNormal(vid));
            Assert.True(nt.HasTriangleGroups);
            Assert.All(nt.TriangleIndices(), tid => Assert.Equal(7, nt.GetTriangleGroup(tid)));
        }

        // --- MeshEditor rollback ---

        /// <summary> two triangles sharing edge p-q: that edge cannot get a third triangle </summary>
        static DMesh3 SharedEdgeMesh(out int p, out int q)
        {
            var m = new DMesh3();
            p = m.AppendVertex(new Vector3d(0, 0, 0));
            q = m.AppendVertex(new Vector3d(1, 0, 0));
            int r = m.AppendVertex(new Vector3d(0, 1, 0)), s = m.AppendVertex(new Vector3d(0, -1, 0));
            m.AppendTriangle(p, q, r);
            m.AppendTriangle(q, p, s);
            return m;
        }

        [Fact]
        public void MeshEditor_StitchLoop_FailureRemovesEverythingAdded()
        {
            DMesh3 m = SharedEdgeMesh(out int p, out int q);
            int u = m.AppendVertex(new Vector3d(-1, 0, 1));
            int[] loop2 = Enumerable.Range(0, 3).Select(i => m.AppendVertex(new Vector3d(i, 2, 2))).ToArray();
            // step 1 uses edge p-q: fails after step 0 added two triangles and step 1 added one
            Assert.Null(new MeshEditor(m).StitchLoop(new[] { u, p, q }, loop2));
            Assert.Equal(2, m.TriangleCount);

            // failure in the very first step
            DMesh3 m2 = SharedEdgeMesh(out p, out q);
            int[] l2 = Enumerable.Range(0, 3).Select(i => m2.AppendVertex(new Vector3d(i, 2, 2))).ToArray();
            int w = m2.AppendVertex(new Vector3d(5, 5, 5));
            Assert.Null(new MeshEditor(m2).StitchLoop(new[] { p, q, w }, l2));
            Assert.Equal(2, m2.TriangleCount);
        }

        [Fact]
        public void MeshEditor_StitchSpan_FailureRemovesEverythingAdded()
        {
            DMesh3 m = SharedEdgeMesh(out int p, out int q);
            int u = m.AppendVertex(new Vector3d(-1, 0, 1));
            int[] span2 = Enumerable.Range(0, 3).Select(i => m.AppendVertex(new Vector3d(i, 2, 2))).ToArray();
            Assert.Null(new MeshEditor(m).StitchSpan(new[] { u, p, q }, span2));
            Assert.Equal(2, m.TriangleCount);
        }

        [Fact]
        public void MeshEditor_StitchUnorderedEdges_FailureRemovesEverythingAdded()
        {
            DMesh3 m = SharedEdgeMesh(out int p, out int q);
            // two separate triangles facing each other: their edges e1, e2 can be stitched
            int a = m.AppendVertex(new Vector3d(5, 0, 0)), b = m.AppendVertex(new Vector3d(6, 0, 0)), c = m.AppendVertex(new Vector3d(5, 1, 0));
            int d = m.AppendVertex(new Vector3d(5, 0, 1)), e = m.AppendVertex(new Vector3d(6, 0, 1)), f = m.AppendVertex(new Vector3d(5, 1, 1));
            m.AppendTriangle(a, b, c);
            m.AppendTriangle(d, f, e);
            int e1 = m.FindEdge(a, b), e2 = m.FindEdge(d, e);
            // the first pair stitches, the second uses the interior edge p-q and fails
            var pairs = new List<Index2i>() { new Index2i(e1, e2), new Index2i(m.FindEdge(p, q), e2) };
            Assert.Null(new MeshEditor(m).StitchUnorderedEdges(pairs));
            Assert.Equal(4, m.TriangleCount);
        }

        [Fact]
        public void MeshEditor_AddTriangleFan_OrderedEdgeLoop_FailureRemovesEverythingAdded()
        {
            DMesh3 m = SharedEdgeMesh(out int p, out int q);
            int boundary = m.FindEdge(p, m.GetTriangle(0).c);
            int interior = m.FindEdge(p, q);
            int center = m.AppendVertex(new Vector3d(0, 0, 1));
            Assert.Null(new MeshEditor(m).AddTriangleFan_OrderedEdgeLoop(center, new[] { boundary, interior }));
            Assert.Equal(2, m.TriangleCount);
        }

        // --- EdgeLoop / MeshMeasurements / MeshNormals ---

        [Fact]
        public void EdgeLoop_IsSameLoop()
        {
            DMesh3 mesh = Grid(3);
            var loops = new MeshBoundaryLoops(mesh);
            EdgeLoop loop = loops[0];
            // start both loops away from vertex 0: the start vertex was looked up by loop index, not vertex id
            int N = loop.Vertices.Length, k0 = Array.IndexOf(loop.Vertices, 0);
            int[] verts1 = Enumerable.Range(0, N).Select(i => loop.Vertices[(k0 + 1 + i) % N]).ToArray();
            int[] verts2 = Enumerable.Range(0, N).Select(i => loop.Vertices[(k0 + 3 + i) % N]).ToArray();
            var loop1 = EdgeLoop.FromVertices(mesh, verts1);
            var loop2 = EdgeLoop.FromVertices(mesh, verts2);
            Assert.True(loop1.IsSameLoop(loop2));
        }

        [Fact]
        public void MeshMeasurements_MassProperties_BodyCoords()
        {
            DMesh3 mesh = Sphere(10);
            MeshTransforms.Translate(mesh, new Vector3d(10, 0, 0));
            MeshMeasurements.MassProperties(mesh, out double m1, out Vector3d c1, out double[,] world, false);
            MeshMeasurements.MassProperties(mesh, out double m2, out Vector3d c2, out double[,] body, true);
            // about the center of mass the inertia of a sphere is much smaller than about the origin, 10 away
            Assert.True(body[1, 1] < 0.1 * world[1, 1], $"body {body[1, 1]} world {world[1, 1]}");
        }

        [Fact]
        public void MeshMeasurements_ExtremeVertices_MonotoneOrder()
        {
            var mesh = new DMesh3();
            // vertices in decreasing x: the first vertex is the maximum
            for (int i = 0; i < 3; ++i)
                mesh.AppendVertex(new Vector3d(2 - i, 0, 0));
            Interval1i ext = MeshMeasurements.ExtremeVertices(mesh, Vector3d.AxisX);
            Assert.Equal(2, ext.a);
            Assert.Equal(0, ext.b);
        }

        [Fact]
        public void MeshNormals_UsesVertexF()
        {
            DMesh3 mesh = Grid(2);      // normals +Y
            var normals = new MeshNormals(mesh);
            normals.VertexF = (vid) => { Vector3d v = mesh.GetVertex(vid); return new Vector3d(v.x, v.z, 0); };   // the grid in the XY plane
            normals.Compute();
            Assert.True(Math.Abs(normals.Normals[0].z) > 0.99, $"normal {normals.Normals[0]}");
        }

        // --- FaceGroupUtil ---

        [Fact]
        public void FaceGroupUtil_GroupsOutsideMaxGroupID()
        {
            DMesh3 mesh = Grid(2);
            mesh.EnableTriangleGroups(0);
            mesh.SetTriangleGroup(1, 1);        // MaxGroupID 2
            mesh.SetTriangleGroup(0, -1);
            int[][] sets = FaceGroupUtil.FindTriangleSetsByGroup(mesh);
            Assert.Equal(mesh.TriangleCount, sets.Sum(s => s.Length));
            var counts = FaceGroupUtil.CountAllGroups(mesh);
            Assert.Equal(1, counts[-1]);
            Assert.Equal(1, counts[1]);
            Assert.Equal(mesh.TriangleCount - 2, counts[0]);
        }

        // --- MeshDecomposition ---

        [Fact]
        public void MeshDecomposition_ComponentsReferenceTheRightTriangles()
        {
            // vertex ids decrease along x, so a component's first vertex is often its largest id; the range of
            // used vertex ids then missed it, and the leftover mapping corrupted the next component
            DMesh3 grid = Grid(6), mesh = new DMesh3();
            int NV = grid.MaxVertexID;
            for (int k = 0; k < NV; ++k) mesh.AppendVertex(grid.GetVertex(NV - 1 - k));
            foreach (Index3i t in grid.Triangles()) mesh.AppendTriangle(NV - 1 - t.a, NV - 1 - t.b, NV - 1 - t.c);
            var collector = new ComponentCollector();
            var decomp = new MeshDecomposition(mesh, collector) { MaxComponentSize = 4 };
            decomp.BuildLinear();
            int bad = 0, total = 0;
            foreach (var c in collector.Components) {
                for (int ti = 0; ti < c.tri_count; ++ti) {
                    int a = c.source_vertices[c.triangles[3 * ti]], b = c.source_vertices[c.triangles[3 * ti + 1]], d = c.source_vertices[c.triangles[3 * ti + 2]];
                    if (mesh.FindTriangle(a, b, d) == DMesh3.InvalidID)
                        bad++;
                    total++;
                }
            }
            Assert.Equal(mesh.TriangleCount, total);
            Assert.Equal(0, bad);
        }
        class ComponentCollector : IMeshComponentManager
        {
            public List<MeshDecomposition.Component> Components = new List<MeshDecomposition.Component>();
            public void AddComponent(MeshDecomposition.Component C) { Components.Add(C); }
            public void ClearAllComponents() { Components.Clear(); }
        }

        // --- SimpleMesh ---

        [Fact]
        public void SimpleMesh_AppendKeepsAttributes()
        {
            var gen = new Sphere3Generator_NormalizedCube() { EdgeVertices = 3 }.Generate();
            var m = new SimpleMesh();
            m.Initialize(true, true, true, true);
            m.AppendVertices(gen.vertices, gen.normals, null, gen.uv);
            m.AppendTriangles(gen.triangles);
            Assert.True(m.HasVertexNormals);
            Assert.True(m.HasVertexColors);
            Assert.True(m.HasVertexUVs);
            Assert.True(m.HasTriangleGroups);
            m.AppendVertices(gen.vertices, gen.normals, null, gen.uv);
            m.AppendTriangles(gen.triangles);
            Assert.True(m.HasVertexNormals && m.HasVertexColors && m.HasVertexUVs && m.HasTriangleGroups);
        }

        // --- MeshTransforms ---

        [Fact]
        public void MeshTransforms_RotateQuaternionf_DoublePrecision()
        {
            var mesh = new DMesh3();
            mesh.AppendVertex(new Vector3d(1e6 + 0.123456, 0, 0));
            MeshTransforms.Rotate(mesh, Vector3d.Zero, Quaternionf.Identity);
            Assert.Equal(1e6 + 0.123456, mesh.GetVertex(0).x, 9);
        }

        // --- MeshWeights ---

        [Fact]
        public void MeshWeights_VoronoiArea_ScalesWithArea()
        {
            // a vertex whose one triangle is obtuse
            Func<double, DMesh3> fan = (s) => {
                var m = new DMesh3();
                m.AppendTriangle(m.AppendVertex(Vector3d.Zero), m.AppendVertex(new Vector3d(4 * s, 0, 0)), m.AppendVertex(new Vector3d(-1 * s, 0.5 * s, 0)));
                return m;
            };
            double a1 = MeshWeights.VoronoiArea(fan(1), 0), a10 = MeshWeights.VoronoiArea(fan(10), 0);
            Assert.Equal(100 * a1, a10, 9);
            Assert.Equal(0.5 * 1.0, a1, 9);     // obtuse at the vertex: half of the triangle area (area 1)
        }

        [Fact]
        public void MeshWeights_CotanCentroid_RightAngles()
        {
            // lift the center vertex of a 2x2 grid: its cotan centroid is the center of the flat neighborhood.
            // The diagonal edges have right angles opposite to them, whose zero cotangent made the old code
            // treat the triangle as degenerate and return the vertex itself
            DMesh3 mesh = Grid(2);
            int center = 4;
            mesh.SetVertex(center, new Vector3d(1, 0.5, 1));
            Vector3d c = MeshWeights.CotanCentroid(mesh, center);
            Assert.True(c.Distance(new Vector3d(1, 0, 1)) < 1e-9, $"centroid {c}");
        }

        // --- constraints / Remesher ---

        [Fact]
        public void MeshConstraintUtil_ConstrainVtxLoopTo_Remesher_PassesSetID()
        {
            DMesh3 mesh = Grid(3);
            var loop = new MeshBoundaryLoops(mesh)[0];
            var r = new Remesher(mesh);
            MeshConstraintUtil.ConstrainVtxLoopTo(r, loop.Vertices, new DCurveProjectionTarget(loop.ToCurve()), 42);
            EdgeConstraint ec = r.Constraints.GetEdgeConstraint(loop.Edges[0]);
            Assert.Equal(42, ec.TrackingSetID);
        }

        [Fact]
        public void Remesher_SplitInheritsFixedSetZero()
        {
            var mesh = new DMesh3();
            mesh.AppendTriangle(mesh.AppendVertex(Vector3d.Zero), mesh.AppendVertex(new Vector3d(4, 0, 0)), mesh.AppendVertex(new Vector3d(0, 0, 4)));
            var cons = new MeshConstraints();
            foreach (int vid in mesh.VertexIndices())
                cons.SetOrUpdateVertexConstraint(vid, new VertexConstraint(true, 0));
            foreach (int eid in mesh.EdgeIndices())         // vertex constraints are only inherited along constrained edges
                cons.SetOrUpdateEdgeConstraint(eid, new EdgeConstraint(EdgeRefineFlags.NoFlip));
            var r = new Remesher(mesh) { EnableFlips = false, EnableCollapses = false, EnableSmoothing = false };
            r.SetExternalConstraints(cons);
            r.SetTargetEdgeLength(1.0);
            r.BasicRemeshPass();
            var newVerts = mesh.VertexIndices().Where(v => v >= 3 && mesh.IsBoundaryVertex(v)).ToList();
            Assert.NotEmpty(newVerts);
            foreach (int v in newVerts) {
                VertexConstraint vc = cons.GetVertexConstraint(v);
                Assert.True(vc.Fixed && vc.FixedSetID == 0, $"vertex {v}: fixed={vc.Fixed} set={vc.FixedSetID}");
            }
        }
    }
}
