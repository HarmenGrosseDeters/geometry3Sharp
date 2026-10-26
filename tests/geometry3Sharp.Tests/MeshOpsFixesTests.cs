using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using g3;
using gs;
using Xunit;

namespace geometry3Sharp.Tests
{
    /// <summary>
    /// Bugs in mesh_ops/, mesh_generators/ and mesh_selection/ from the class-by-class review
    /// (docs/Analyse-2026-10.md, section 2.7).
    /// </summary>
    public class MeshOpsFixesTests
    {
        /// <summary> flat n x n grid of unit squares in the XY plane, counter-clockwise seen from +Z </summary>
        static DMesh3 GridXY(int n, double size = 1.0)
        {
            var m = new DMesh3();
            for (int j = 0; j <= n; ++j)
                for (int i = 0; i <= n; ++i)
                    m.AppendVertex(new Vector3d(i * size, j * size, 0));
            for (int j = 0; j < n; ++j)
                for (int i = 0; i < n; ++i) {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    m.AppendTriangle(a, b, d);
                    m.AppendTriangle(a, d, c);
                }
            return m;
        }

        static Vector3d FaceNormalAtVertex(DMesh3 m, int vid)
        {
            return m.GetTriNormal(m.VtxTrianglesItr(vid).First());
        }


        /*
         * mesh_generators
         */

        [Fact]
        public void TrivialBox_SharedVertexNormalsPointAwayFromCenter()
        {
            var box = new Box3d(new Vector3d(5, 2, -3), new Vector3d(1, 2, 3));
            var gen = new TrivialBox3Generator() { Box = box, NoSharedVertices = false };
            gen.Generate();
            for (int i = 0; i < 8; ++i) {
                Vector3d expect = (gen.vertices[i] - box.Center).Normalized;
                Vector3d n = (Vector3d)gen.normals[i];
                Assert.True(n.Distance(expect) < 1e-5, $"normal {i} is {n}, expected {expect}");
            }
        }

        [Fact]
        public void RoundRect_UVsSpanUnitSquareInBothDirections()
        {
            var gen = new RoundRectGenerator() { Width = 2, Height = 1, Radius = 0.2f, CornerSteps = 3 };
            gen.Generate();
            for (int k = 0; k < gen.vertices.Count; ++k) {
                Vector3d v = gen.vertices[k];
                Vector2f uv = gen.uv[k];
                Assert.True(Math.Abs(uv.x - (v.x + 1) / 2) < 1e-5, $"uv.x of vertex {k}");
                Assert.True(Math.Abs(uv.y - (v.z + 0.5)) < 1e-5, $"uv.y of vertex {k} is {uv.y}, z = {v.z}");
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData((int)(RoundRectGenerator.Corner.BottomLeft | RoundRectGenerator.Corner.TopRight))]
        [InlineData((int)(RoundRectGenerator.Corner.BottomRight | RoundRectGenerator.Corner.TopLeft))]
        public void RoundRect_BorderLoopMatchesMeshBoundary(int sharp)
        {
            var gen = new RoundRectGenerator() {
                Width = 3, Height = 2, Radius = 0.4f, CornerSteps = 4,
                SharpCorners = (RoundRectGenerator.Corner)sharp
            };
            DMesh3 mesh = gen.Generate().MakeDMesh();
            Vector3d[] border = gen.GetBorderLoop();

            var loops = new MeshBoundaryLoops(mesh);
            Assert.Single(loops.Loops);
            Vector3d[] meshLoop = loops[0].Vertices.Select(v => mesh.GetVertex(v)).ToArray();
            Assert.Equal(meshLoop.Length, border.Length);

            // same points in the same cyclic order (either direction)
            int N = border.Length;
            int start = Array.FindIndex(meshLoop, p => p.Distance(border[0]) < 1e-5);
            Assert.True(start >= 0, "first border point is not a mesh boundary vertex");
            bool fwd = true, bwd = true;
            for (int i = 0; i < N; ++i) {
                fwd &= meshLoop[(start + i) % N].Distance(border[i]) < 1e-5;
                bwd &= meshLoop[(start - i + N) % N].Distance(border[i]) < 1e-5;
            }
            Assert.True(fwd || bwd, "border loop is not in boundary order");
        }

        // closed curve with torsion: the rotation-minimizing frame does not return to itself
        static List<Vector3d> TorusKnot(int n)
        {
            var pts = new List<Vector3d>();
            for (int i = 0; i < n; ++i) {
                double t = MathUtil.TwoPI * i / n;
                double r = 3 + Math.Cos(3 * t);
                pts.Add(new Vector3d(r * Math.Cos(2 * t), r * Math.Sin(2 * t), Math.Sin(3 * t)));
            }
            return pts;
        }

        [Fact]
        public void Tube_ClosedLoop_SeamRingCoincidesWithFirstRing()
        {
            List<Vector3d> path = TorusKnot(120);
            var gen = new TubeGenerator() {
                Vertices = path, Polygon = Polygon2d.MakeCircle(0.3, 8),
                ClosedLoop = true, Capped = false, NoSharedVertices = true
            };
            gen.Generate();
            int ringSize = 9, NV = path.Count;
            for (int j = 0; j < ringSize; ++j) {
                double d = gen.vertices[NV * ringSize + j].Distance(gen.vertices[j]);
                Assert.True(d < 1e-4, $"seam ring vertex {j} is {d} away from the first ring");
            }
        }

        [Fact]
        public void Tube_ClosedLoop_SharedVertices_SeamIsNotTwisted()
        {
            List<Vector3d> path = TorusKnot(400);
            var gen = new TubeGenerator() {
                Vertices = path, Polygon = Polygon2d.MakeCircle(1.0, 8),
                ClosedLoop = true, Capped = false, NoSharedVertices = false
            };
            gen.Generate();
            int ringSize = 8, NV = path.Count;
            // distance between corresponding vertices of consecutive rings
            Func<int, int, double> ringDist = (r0, r1) =>
                Enumerable.Range(0, ringSize).Max(j => gen.vertices[r0 * ringSize + j].Distance(gen.vertices[r1 * ringSize + j]));
            double maxInner = Enumerable.Range(0, NV - 1).Max(r => ringDist(r, r + 1));
            double seam = ringDist(NV - 1, 0);
            Assert.True(seam < 1.5 * maxInner, $"seam ring distance {seam} vs. max {maxInner} elsewhere");
        }

        [Fact]
        public void Revolve_CapNormalsFollowRevolveAxis()
        {
            Vector3d[] curve = new Vector3d[] { new Vector3d(1, 0, 0), new Vector3d(1.2, 1, 0), new Vector3d(1, 2, 0) };

            var gen1 = new Curve3Axis3RevolveGenerator() { Curve = curve, Capped = true, NoSharedVertices = false };
            gen1.Generate();
            Assert.True(((Vector3d)gen1.normals[gen1.startCapCenterIndex]).Distance(-Vector3d.AxisY) < 1e-5,
                $"start cap normal {gen1.normals[gen1.startCapCenterIndex]}");
            Assert.True(((Vector3d)gen1.normals[gen1.endCapCenterIndex]).Distance(Vector3d.AxisY) < 1e-5,
                $"end cap normal {gen1.normals[gen1.endCapCenterIndex]}");

            var gen2 = new Curve3Curve3RevolveGenerator() {
                Curve = curve, Axis = new Vector3d[] { Vector3d.Zero, new Vector3d(0, 2, 0) },
                Capped = true, NoSharedVertices = false
            };
            gen2.Generate();
            Assert.True(((Vector3d)gen2.normals[gen2.startCapCenterIndex]).Distance(-Vector3d.AxisY) < 1e-5,
                $"start cap normal {gen2.normals[gen2.startCapCenterIndex]}");
            Assert.True(((Vector3d)gen2.normals[gen2.endCapCenterIndex]).Distance(Vector3d.AxisY) < 1e-5,
                $"end cap normal {gen2.normals[gen2.endCapCenterIndex]}");
        }

        [Fact]
        public void VerticalGeneralizedCylinder_UVsStayInUnitRange()
        {
            var gen = new VerticalGeneralizedCylinderGenerator() {
                Sections = new MeshGenerator.CircularSection[] { new MeshGenerator.CircularSection(1, 0), new MeshGenerator.CircularSection(0.8f, 1), new MeshGenerator.CircularSection(1, 2) },
                Slices = 8, NoSharedVertices = true
            };
            gen.Generate();
            for (int k = 0; k < gen.uv.Count; ++k)
                Assert.True(gen.uv[k].x <= 1.0f + 1e-6 && gen.uv[k].y <= 1.0f + 1e-6, $"uv {k} = {gen.uv[k]}");
            // the duplicated seam vertex closes the texture at u = 1
            Assert.Equal(1.0f, gen.uv[8].x, 5);
        }

        [Fact]
        public void PointSplats_NormalsAreTheInputNormals()
        {
            Vector3d[] pts = { Vector3d.Zero, new Vector3d(3, 0, 0) };
            Vector3d[] nrm = { Vector3d.AxisZ, new Vector3d(1, 1, 0).Normalized };
            var gen = new PointSplatsGenerator() {
                PointIndices = new int[] { 0, 1 }, PointF = i => pts[i], NormalF = i => nrm[i], Radius = 0.5
            };
            gen.Generate();
            for (int k = 0; k < 6; ++k) {
                Vector3d n = (Vector3d)gen.normals[k];
                Assert.True(n.Distance(nrm[k / 3]) < 1e-5, $"normal {k} is {n}");
            }
        }

        [Fact]
        public void VoxelSurfaceGenerator_GenerateTwice_DoesNotAccumulateMeshes()
        {
            var grid = new Bitmap3(new Vector3i(3, 3, 3));
            grid.Set(new Vector3i(1, 1, 1), true);
            var gen = new VoxelSurfaceGenerator() { Voxels = grid };
            gen.Generate();
            Assert.Single(gen.Meshes);
            gen.Generate();
            Assert.Single(gen.Meshes);
        }

        // guard for the corrected class documentation (behaviour unchanged)
        [Fact]
        public void CappedCylinder_GroupIDsMatchDocumentation()
        {
            var gen = new CappedCylinderGenerator() { Slices = 8, StartAngleDeg = 0, EndAngleDeg = 180 };
            DMesh3 m = gen.Generate().MakeDMesh();
            foreach (int tid in m.TriangleIndices()) {
                int gid = m.GetTriangleGroup(tid);
                Vector3d n = m.GetTriNormal(tid);
                Vector3d c = m.GetTriCentroid(tid);
                if (gid == 2) Assert.True(n.y < -0.99 && Math.Abs(c.y) < 1e-6, "group 2 is the bottom cap");
                else if (gid == 3) Assert.True(n.y > 0.99 && Math.Abs(c.y - 1) < 1e-6, "group 3 is the top cap");
                else Assert.Contains(gid, new int[] { 1, 4, 5 });
            }
        }


        /*
         * mesh_selection
         */

        [Fact]
        public void VertexFloodFill_DefaultFilter_SelectsWholeComponent()
        {
            DMesh3 m = GridXY(3);
            var sel = new MeshVertexSelection(m);
            sel.FloodFill(0);
            Assert.Equal(m.VertexCount, sel.Count);
        }

        /// <summary> annulus with a single ring of triangles: every vertex is on a boundary loop </summary>
        static DMesh3 ThinAnnulus(int n)
        {
            var m = new DMesh3();
            for (int i = 0; i < n; ++i) {
                double a = MathUtil.TwoPI * i / n;
                m.AppendVertex(new Vector3d(2 * Math.Cos(a), 2 * Math.Sin(a), 0));   // outer: 2*i
                m.AppendVertex(new Vector3d(Math.Cos(a), Math.Sin(a), 0));           // inner: 2*i+1
            }
            for (int i = 0; i < n; ++i) {
                int o0 = 2 * i, i0 = 2 * i + 1, o1 = 2 * ((i + 1) % n), i1 = o1 + 1;
                m.AppendTriangle(i0, o0, o1);
                m.AppendTriangle(i0, o1, i1);
            }
            return m;
        }

        [Fact]
        public void SelectConnectedBoundaryV_StaysOnSeedLoop()
        {
            DMesh3 m = ThinAnnulus(12);
            Assert.True(m.CheckValidity(false, FailMode.ReturnOnly));
            var sel = new MeshVertexSelection(m);
            sel.SelectConnectedBoundaryV(0);
            Assert.Equal(12, sel.Count);
            Assert.All(sel, vid => Assert.True(vid % 2 == 0, $"inner-loop vertex {vid} selected"));
        }

        [Fact]
        public void FaceSelection_ExpandRings_AfterMeshGrew()
        {
            DMesh3 m = GridXY(3);
            var sel = new MeshFaceSelection(m);
            sel.Select(0);
            sel.ExpandToOneRingNeighbours(2);       // allocates the internal bitmap

            // grow the mesh, so that new triangle ids exceed the bitmap size
            for (int k = 0; k < 6; ++k) {
                DMesh3.EdgeSplitInfo info;
                int eid = m.EdgeIndices().First(e => m.IsBoundaryEdge(e) == false);
                Assert.Equal(MeshResult.Ok, m.SplitEdge(eid, out info));
            }
            var sel2 = new MeshFaceSelection(sel);
            sel.Select(m.MaxTriangleID - 1);
            sel.ExpandToOneRingNeighbours(2);
            Assert.True(sel.Count > sel2.Count);
        }

        [Fact]
        public void BoundaryLoops_FilteredBowtie_EndsSpanAtFilteredVertex()
        {
            // quad fan (0,1,2),(0,2,3) and a triangle (0,4,5) that touches it only at vertex 0.
            // Triangle order makes edge 2-3 the first boundary edge, so the walk starts in the middle of the span.
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero);
            m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, 1, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendVertex(new Vector3d(-1, 0, 0)); m.AppendVertex(new Vector3d(-1, -1, 0));
            m.AppendTriangle(2, 3, 0);
            m.AppendTriangle(0, 1, 2);
            m.AppendTriangle(0, 4, 5);
            HashSet<int> quadBorder = new HashSet<int>() { m.FindEdge(1, 2), m.FindEdge(2, 3), m.FindEdge(3, 0) };

            // vertex 0 has four boundary edges, but only edge 3-0 passes the filter, so it is the
            // end of the open span 0-3-2-1 (and vertex 1 is the other end)
            var loops = new MeshBoundaryLoops(m, false) {
                EdgeFilterF = (eid) => quadBorder.Contains(eid)
            };
            Assert.True(loops.Compute());
            Assert.Empty(loops.Loops);
            Assert.Single(loops.Spans);
            Assert.Equal(new int[] { 0, 1, 2, 3 }, loops.Spans[0].Vertices.OrderBy(v => v).ToArray());

            // now also let edge 0-4 of the other triangle pass: two valid edges at vertex 0, so the
            // span continues straight through it (it is not a bowtie of the filtered boundary)
            quadBorder.Add(m.FindEdge(0, 4));
            loops = new MeshBoundaryLoops(m, false) {
                EdgeFilterF = (eid) => quadBorder.Contains(eid)
            };
            Assert.True(loops.Compute());
            Assert.Empty(loops.Loops);
            Assert.Single(loops.Spans);
            Assert.Equal(new int[] { 1, 2, 3, 0, 4 }, loops.Spans[0].Vertices);
        }


        /*
         * mesh_ops
         */

        [Fact]
        public void MergeCoincidentEdges_OnlyUniquePairs_SkipsEdgeWhosePartnerIsAmbiguous()
        {
            // three boundary edges along x, offset in z by 0, 0.6r, 1.2r: i~j and j~k, but not i~k.
            // i and k each have exactly one candidate (j), but j has two, so no pair is unique.
            double r = 0.001;
            var m = new DMesh3();
            double[] dz = { 0, 0.6 * r, 1.2 * r };
            for (int k = 0; k < 3; ++k) {
                int a = m.AppendVertex(new Vector3d(0, 0, dz[k]));
                int b = m.AppendVertex(new Vector3d(1, 0, dz[k]));
                int c = m.AppendVertex(new Vector3d(0.5, (k == 1) ? -1 : 1, dz[k]));
                if (k == 1) m.AppendTriangle(b, a, c); else m.AppendTriangle(a, b, c);
            }
            var merge = new MergeCoincidentEdges(m) { MergeDistance = r, OnlyUniquePairs = true };
            merge.Apply();
            Assert.Equal(9, m.EdgeCount);
            Assert.Equal(9, m.VertexCount);
        }

        [Fact]
        public void MergeCoincidentEdges_ClosesShortCrackWithinMergeDistance()
        {
            // the boundary edge midpoints span 0.01, so a hash grid of 64 cells over them has cells far smaller
            // than MergeDistance; FindInBall threw on those
            DMesh3 m = MeshTestUtil.CubeWithShortCrack(0.01, 0.0003);
            Assert.False(m.IsClosed());
            new MergeCoincidentEdges(m) { MergeDistance = 0.001 }.Apply();
            Assert.True(m.IsClosed());
            Assert.Equal(12, m.VertexCount);
            Assert.Equal(20, m.TriangleCount);
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            m.CheckValidity();
        }

        [Fact]
        public void MeshAutoRepair_MergesShortCrackInsteadOfFillingIt()
        {
            // repair_cracks swallowed the FindInBall exception; the hole filler then closed the crack with
            // extra triangles and kept the patch vertices
            DMesh3 m = MeshTestUtil.CubeWithShortCrack(0.01, 0.0003);
            Assert.True(new MeshAutoRepair(m) { RepairTolerance = 0.001 }.Apply());
            Assert.Equal("", MeshRobustRepair.NotCleanReason(m));
            Assert.Equal(12, m.VertexCount);
            Assert.Equal(20, m.TriangleCount);
        }

        [Fact]
        public void RemoveDuplicateTriangles_RemovesSmallDuplicatesWithinTolerance()
        {
            // the centroids coincide, so a hash grid over them had cells far smaller than VertexTolerance (zero
            // here); FindInBall threw on those
            DMesh3 m = MeshTestUtil.DuplicatedSmallTriangle(0.01, 2, false);
            var dupe = new RemoveDuplicateTriangles(m) { VertexTolerance = 0.001 };
            dupe.Apply();
            Assert.Equal(1, dupe.Removed);
            Assert.Equal(1, m.TriangleCount);
            Assert.Equal(3, m.VertexCount);
            m.CheckValidity();
        }

        [Fact]
        public void RemoveDuplicateTriangles_MoreDuplicatesThanTheQueryBuffer()
        {
            // 1100 copies of one triangle: FindInBall finds more points than the initial buffer of 1024 holds, and
            // the buffer was replaced by one of the same size, forever
            DMesh3 m = MeshTestUtil.DuplicatedSmallTriangle(0.01, 1100, true);
            var dupe = new RemoveDuplicateTriangles(m) { VertexTolerance = 0.001 };
            Assert.True(Timed.Finishes(() => dupe.Apply(), TimeSpan.FromSeconds(60)), "no result after 60 s");
            Assert.Equal(1099, dupe.Removed);
            Assert.Equal(2, m.TriangleCount);
            Assert.Equal(6, m.VertexCount);
        }

        [Fact]
        public void IsoCurves_RecomputeReplacesPreviousEdgeCrossings()
        {
            DMesh3 m = GridXY(4);
            var iso = new MeshIsoCurves(m, (v) => v.x - 1.3);
            iso.Compute();
            iso.ValueF = (v) => v.x - 1.6;
            iso.Compute();
            iso.SplitAtIsoCrossings();
            foreach (int vid in m.VertexIndices()) {
                double x = m.GetVertex(vid).x;
                Assert.True(Math.Abs(x - 1.3) > 1e-6, $"vertex {vid} inserted at the stale crossing x = 1.3");
            }
            Assert.Contains(m.VertexIndices(), vid => Math.Abs(m.GetVertex(vid).x - 1.6) < 1e-9);

            // splitting a second time must not split the (already split) edges again
            int nv = m.VertexCount;
            iso.SplitAtIsoCrossings();
            Assert.Equal(nv, m.VertexCount);

            // no crossing at all: nothing left over from the previous computation
            iso.ValueF = (v) => v.x + 10;
            iso.Compute();
            iso.SplitAtIsoCrossings();
            Assert.Equal(nv, m.VertexCount);
        }

        [Fact]
        public void IsoCurves_Split_MinLengthIsMeasuredFromCrossing()
        {
            DMesh3 m = GridXY(4);
            int nv = m.VertexCount;
            // crossing 0.001 from the vertices at x = 1, edges have length 1 or sqrt(2)
            var iso = new MeshIsoCurves(m, (v) => v.x - 1.001);
            iso.Compute();
            iso.SplitAtIsoCrossings(0.01);
            Assert.Equal(nv, m.VertexCount);
        }

        [Fact]
        public void InsertUVPolyCurve_CurveStartsNearVertexZero()
        {
            var m = new DMesh3();
            m.AppendVertex(new Vector3d(0, 0, 0)); m.AppendVertex(new Vector3d(1, 0, 0));
            m.AppendVertex(new Vector3d(1, 1, 0)); m.AppendVertex(new Vector3d(0, 1, 0));
            m.AppendTriangle(0, 1, 2); m.AppendTriangle(0, 2, 3);

            // within the 100*SpatialEpsilon snap distance of vertex 0, but farther than SpatialEpsilon
            var path = new PolyLine2d();
            path.AppendVertex(new Vector2d(1e-7, 0.5e-7));
            path.AppendVertex(new Vector2d(0.7, 0.4));
            var insert = new MeshInsertUVPolyCurve(m, path);
            Assert.True(insert.Apply());
            Assert.Equal(0, insert.CurveVertices[0]);
        }

        [Fact]
        public void ICP_NonCompactSource_UsesNormalOfMatchingVertex()
        {
            // target: plane z = 0, facing +Z
            DMesh3 target = GridXY(2, 4.0);
            MeshTransforms.Translate(target, new Vector3d(-4, -4, 0));
            var spatial = new DMeshAABBTree3(target, true);

            // source: vertex 0 is deleted; odd ids lie at z = 1 facing +Z (match the target),
            // even ids at z = 3 facing -Z (rejected by the normal test)
            var source = new DMesh3(MeshComponents.VertexNormals);
            source.AppendVertex(new NewVertexInfo(Vector3d.Zero, -Vector3f.AxisZ));
            for (int j = -1; j <= 1; ++j)
                for (int i = -1; i <= 1; ++i) {
                    source.AppendVertex(new NewVertexInfo(new Vector3d(i, j, 1), Vector3f.AxisZ));
                    source.AppendVertex(new NewVertexInfo(new Vector3d(i, j, 3), -Vector3f.AxisZ));
                }
            Assert.Equal(MeshResult.Ok, source.RemoveVertex(0));

            var icp = new MeshICP(source, spatial) { UseNormals = true, MaxIterations = 20 };
            icp.Solve();
            Assert.True(Math.Abs(icp.Translation.z + 1) < 1e-3, $"translation {icp.Translation}");
        }

        [Fact]
        public void ExtrudeLoop_ReturnsFalseWhenStitchFails()
        {
            // the loop around an interior vertex is not a boundary loop, so it cannot be stitched
            DMesh3 m = GridXY(4);
            int[] ring = { 6, 7, 8, 13, 18, 17, 16, 11 };     // around the center vertex 12
            for (int k = 0; k < ring.Length; ++k)
                Assert.False(m.IsBoundaryEdge(m.FindEdge(ring[k], ring[(k + 1) % ring.Length])));
            int vcount = m.VertexCount;
            var loop = EdgeLoop.FromVertices(m, ring);
            var extrude = new MeshExtrudeLoop(m, loop);
            int tcount = m.TriangleCount;
            Assert.False(extrude.Extrude());
            Assert.Equal(tcount, m.TriangleCount);
            Assert.Equal(vcount, m.VertexCount);
        }

        [Fact]
        public void ExtrudeMesh_ReturnsFalseWhenStitchFails()
        {
            // two triangles that share only vertex 0: the stitch strips of both boundary loops
            // need the same edge 0-0', which can only have two triangles
            var m = new DMesh3();
            m.AppendVertex(Vector3d.Zero);
            m.AppendVertex(new Vector3d(1, 0, 0)); m.AppendVertex(new Vector3d(1, 1, 0));
            m.AppendVertex(new Vector3d(-1, 0, 0)); m.AppendVertex(new Vector3d(-1, -1, 0));
            m.AppendTriangle(0, 1, 2);
            m.AppendTriangle(0, 3, 4);
            var extrude = new MeshExtrudeMesh(m);
            Assert.False(extrude.Extrude());
        }

        [Fact]
        public void PlanarHoleFiller_MergeFailureIsCountedAsFailedMerge()
        {
            // open cylinder whose top loop is slightly non-planar: the fill polygon is inserted
            // fine, but its vertices (on the plane) cannot be merged with the loop vertices
            DMesh3 m = new OpenCylinderGenerator() { Slices = 16, Height = 1 }.Generate().MakeDMesh();
            var loops = new MeshBoundaryLoops(m);
            EdgeLoop top = loops.Loops.First(l => m.GetVertex(l.Vertices[0]).y > 0.5);
            for (int k = 0; k < top.Vertices.Length; ++k) {
                Vector3d v = m.GetVertex(top.Vertices[k]);
                m.SetVertex(top.Vertices[k], v + new Vector3d(0, (k % 2 == 0) ? 1e-3 : -1e-3, 0));
            }
            var fill = new PlanarHoleFiller(m);
            fill.SetPlane(new Vector3d(0, 1, 0), Vector3d.AxisY);
            fill.AddFillLoop(top);
            Assert.False(fill.Fill());
            Assert.True(fill.OutputHasCracks);
            Assert.Equal(0, fill.FailedInsertions);
            Assert.Equal(1, fill.FailedMerges);
        }

        // guard: the new failure counter of MeshMeshCut stays 0 for a clean cut
        [Fact]
        public void MeshMeshCut_CleanCut_HasNoFailedSegmentInsertions()
        {
            DMesh3 target = new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 8 }.Generate().MakeDMesh();
            DMesh3 cutter = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(1, 0.1, 0.05), Vector3d.One) }.Generate().MakeDMesh();
            var cut = new MeshMeshCut() { Target = target, CutMesh = cutter };
            cut.Compute();
            Assert.Equal(0, cut.FailedSegmentInsertions);
            Assert.True(target.CheckValidity(false, FailMode.ReturnOnly));
        }
    }



    [CollectionDefinition("MeshOpsFixesTests.Console", DisableParallelization = true)]
    public class MeshOpsFixesConsoleCollection { }

    /// <summary>
    /// Library code must not write debug output to the console.
    /// </summary>
    [Collection("MeshOpsFixesTests.Console")]
    public class MeshOpsFixesConsoleTests
    {
        [Fact]
        public void MeshMeshCut_DuplicateTargetVertices_WritesNothingToConsole()
        {
            // two triangles with a coincident (but unshared) vertex
            var target = new DMesh3();
            target.AppendVertex(Vector3d.Zero); target.AppendVertex(new Vector3d(1, 0, 0)); target.AppendVertex(new Vector3d(0, 1, 0));
            target.AppendVertex(Vector3d.Zero); target.AppendVertex(new Vector3d(-1, 0, 0)); target.AppendVertex(new Vector3d(0, -1, 0));
            target.AppendTriangle(0, 1, 2);
            target.AppendTriangle(3, 4, 5);
            DMesh3 cutter = new TrivialBox3Generator() { Box = new Box3d(new Vector3d(10, 10, 10), Vector3d.One) }.Generate().MakeDMesh();

            TextWriter prev = Console.Out;
            var sw = new StringWriter();
            try {
                Console.SetOut(sw);
                var cut = new MeshMeshCut() { Target = target, CutMesh = cutter };
                cut.Compute();
            } finally {
                Console.SetOut(prev);
            }
            Assert.Equal("", sw.ToString());
        }
    }
}
