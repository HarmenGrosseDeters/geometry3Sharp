// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using g3;

namespace gs
{
    /// <summary>
    /// This fills a hole in a mesh by doing a trivial fill, optionally offsetting along a fixed vector,
    /// then doing a remesh, then a laplacian smooth, then a second remesh.
    ///
    /// Gives a smooth, evenly tessellated patch with new interior vertices (prefer MinimalHoleFill for flat
    /// or CAD-like holes). The mesh is modified in place. TargetEdgeLength is absolute (model units) and its
    /// default of 2.5 is arbitrary: set it, eg to the average edge length of the hole boundary. With
    /// ConstrainToHoleInterior = false (default) triangles around the hole are remeshed too. Apply() returns
    /// false if no loop was found or the initial fan fill failed.
    ///
    /// With ConstrainToHoleInterior = true (and OffsetDistance = 0) the fill is checked: no fill triangle lies back over
    /// its neighbour across the seam (normals more than 170 degrees apart) or intersects another triangle of the mesh
    /// (exactly; intersections the input has are not counted). If it does not hold, the hole is reopened and filled again
    /// with TargetEdgeLength 0.75, 1 and 0.5 times the mean boundary edge length (an edge length well above the boundary's
    /// folds the coarse fan fill over the seam, #1507); if no attempt is sound, Apply() returns false and the hole is open
    /// again: the vertices and triangles of the input keep their ids, coordinates and (if the mesh has them) vertex normals; MaxVertexID and
    /// MaxTriangleID may be larger; FillTriangles and FillVertices are empty. A seam counts as a fold above 170 degrees, or above the
    /// sharpest dihedral of the input at the seam plus 3 degrees (a thin wedge with the hole along its edge). The check scans the
    /// whole mesh once per attempt (about 60 ms on 265k triangles, 9 ms even for a tiny fill: noticeable only when many holes are filled).
    /// </summary>
    public class SmoothedHoleFill
    {
        public DMesh3 Mesh;

        // after initial coarse hole fill, (optionally) offset fill patch in this direction/distance
        public Vector3d OffsetDirection = Vector3d.Zero;
        public double OffsetDistance = 0.0;

        // remeshing parameters
        /// <summary> target edge length (absolute, model units) of the remeshed fill </summary>
        public double TargetEdgeLength = 2.5;
        public double SmoothAlpha = 1.0;
        public int InitialRemeshPasses = 20;
        public bool RemeshBeforeSmooth = true;
        public bool RemeshAfterSmooth = true;

        // optionally allows extended customization of internal remesher
        // bool argument is true before smooth, false after
        public Action<Remesher, bool> ConfigureRemesherF = null;

        // the laplacian smooth is what gives us the smooth fill
        public bool EnableLaplacianSmooth = true;

        // higher iterations == smoother result, but more expensive
        // [TODO] currently only has effect if ConstrainToHoleInterior==true  (otherwise ROI expands each iteration...)
        public int SmoothSolveIterations = 1;

        /// <summary>If this is true, we don't modify any triangles outside hole (often results in lower-quality fill)</summary>
        public bool ConstrainToHoleInterior = false;


        /*
         *  ways to specify the hole we will fill
         *   (you really should use FillLoop unless you have a good reason not to)
         */

        // Option 1: fill this loop specifically
        public EdgeLoop FillLoop = null;

        // Option 2: identify right loop using these tris on the border of hole
        public List<int> BorderHintTris = null;


        /*
         *  Outputs
         */

        /// <summary> Final fill triangles. May include triangles outside initial fill loop, if ConstrainToHoleInterior=false </summary>
        public int[] FillTriangles;

        /// <summary> Final fill vertices </summary>
        public int[] FillVertices;


        /// <summary> fillLoop: the boundary loop to fill; if null, chosen in Apply() (see BorderHintTris) </summary>
        public SmoothedHoleFill(DMesh3 mesh, EdgeLoop fillLoop = null)
        {
            this.Mesh = mesh;
            this.FillLoop = fillLoop;
        }


        /// <summary>
        /// Fill the hole: FillLoop if set, otherwise the loop with the most edges in BorderHintTris, otherwise
        /// the loop with the most vertices. Sets FillTriangles and FillVertices.
        /// </summary>
        /// <returns>false if the mesh has no boundary loop, the initial fill (or offset extrusion) failed, or with ConstrainToHoleInterior no attempt gave a fill without a fold or intersection (the mesh is then unchanged); true otherwise</returns>
        public bool Apply()
        {
            EdgeLoop useLoop = null;

            if (FillLoop == null) {
                MeshBoundaryLoops loops = new MeshBoundaryLoops(Mesh, true);
                if (loops.Count == 0)
                    return false;

                if (BorderHintTris != null)
                    useLoop = select_loop_tris_hint(loops);
                if (useLoop == null && loops.MaxVerticesLoopIndex >= 0)
                    useLoop = loops[loops.MaxVerticesLoopIndex];
            } else {
                useLoop = FillLoop;
            }
            if (useLoop == null)
                return false;

            // The interior-constrained fill is checked against its surroundings and retried from the original hole with
            // shorter edges if it folds back over the seam or intersects the mesh (#1507)
            bool guarded = ConstrainToHoleInterior && OffsetDistance <= 0 && useLoop.Vertices.Length > 3;
            AttemptCount = 0;
            Dictionary<int, Vector3f> savedNormals = (guarded && Mesh.HasVertexNormals) ? save_normals_near(useLoop) : null;
            List<double> lengths = new List<double> { TargetEdgeLength };
            if (guarded) {
                double boundary = mean_boundary_edge(useLoop);
                foreach (double k in RetryBoundaryFactors) {
                    double retry = k * boundary;
                    if (retry > 0 && lengths.TrueForAll(l => Math.Abs(l - retry) > 0.01 * retry))
                        lengths.Add(retry);
                }
            }
            for (int attempt = 0; attempt < lengths.Count; ++attempt) {
                ++AttemptCount;
                int fill_result = fill_once(useLoop, lengths[attempt]);
                if (fill_result <= 0)
                    return fill_result == 0 ? false : true;
                if (FillIsSound(Mesh, FillTriangles))
                    return true;
                undo_fill();
            }
            if (savedNormals != null) {
                foreach (KeyValuePair<int, Vector3f> kv in savedNormals)
                    if (Mesh.IsVertex(kv.Key))
                        Mesh.SetVertexNormal(kv.Key, kv.Value);
            }
            FillTriangles = new int[0];
            FillVertices = new int[0];
            return false;
        }

        // the smoothing recomputes the vertex normals around the hole (a few rings outside it): keep them for a failed fill
        static Dictionary<int, Vector3f> save_normals_near(EdgeLoop loop)
        {
            DMesh3 mesh = loop.Mesh;
            Dictionary<int, Vector3f> saved = new Dictionary<int, Vector3f>();
            List<int> ring = new List<int>(loop.Vertices);
            foreach (int v in ring) saved[v] = mesh.GetVertexNormal(v);
            for (int k = 0; k < 4; ++k) {
                List<int> next = new List<int>();
                foreach (int v in ring)
                    foreach (int nbr in mesh.VtxVerticesItr(v))
                        if (saved.ContainsKey(nbr) == false) { saved[nbr] = mesh.GetVertexNormal(nbr); next.Add(nbr); }
                ring = next;
            }
            return saved;
        }


        // 1: filled, 0: the initial fill failed, -1: filled, the hole had three vertices or fewer
        int fill_once(EdgeLoop useLoop, double targetEdgeLength)
        {
            // step 1: do stupid hole fill
            SimpleHoleFiller filler = new SimpleHoleFiller(Mesh, useLoop);
            if (filler.Fill() == false)
                return 0;

            if (useLoop.Vertices.Length <= 3 ) {
                FillTriangles = filler.NewTriangles;
                FillVertices = new int[0];
                return -1;
            }

            MeshFaceSelection tris = new MeshFaceSelection(Mesh);
            tris.Select(filler.NewTriangles);

            // extrude initial fill surface (this is used in socketgen for example)
            if (OffsetDistance > 0) {
                MeshExtrudeFaces extrude = new MeshExtrudeFaces(Mesh, tris);
                extrude.ExtrudedPositionF = (v, n, vid) => {
                    return v + OffsetDistance * OffsetDirection;
                };
                if (!extrude.Extrude())
                    return 0;
                tris.Select(extrude.JoinTriangles);
            }

            // if we aren't trying to stay inside hole, expand out a bit,
            // which allows us to clean up ugly edges
            if (ConstrainToHoleInterior == false) {
                tris.ExpandToOneRingNeighbours(2);
                tris.LocalOptimize(true, true);
            }

            // remesh the initial coarse fill region
            if (RemeshBeforeSmooth) {
                RegionRemesher remesh = new RegionRemesher(Mesh, tris);
                remesh.SetTargetEdgeLength(targetEdgeLength);
                remesh.EnableSmoothing = (SmoothAlpha > 0);
                remesh.SmoothSpeedT = SmoothAlpha;
                if (ConfigureRemesherF != null)
                    ConfigureRemesherF(remesh, true);
                for (int k = 0; k < InitialRemeshPasses; ++k)
                    remesh.BasicRemeshPass();
                remesh.BackPropropagate();

                tris = new MeshFaceSelection(Mesh);
                tris.Select(remesh.CurrentBaseTriangles);
                if (ConstrainToHoleInterior == false)
                    tris.LocalOptimize(true, true);
            }

            if (ConstrainToHoleInterior) {
                for (int k = 0; k < SmoothSolveIterations; ++k ) {
                    smooth_and_remesh_preserve(tris, k == SmoothSolveIterations-1, targetEdgeLength);
                    tris = new MeshFaceSelection(Mesh); tris.Select(FillTriangles);
                }
            } else {
                smooth_and_remesh(tris, targetEdgeLength);
                tris = new MeshFaceSelection(Mesh); tris.Select(FillTriangles);
            }

            MeshVertexSelection fill_verts = new MeshVertexSelection(Mesh);
            fill_verts.SelectInteriorVertices(tris);
            FillVertices = fill_verts.ToArray();

            return 1;
        }




        /// <summary>
        /// ConstrainToHoleInterior (no offset): when the fill with TargetEdgeLength is not sound (see Apply), the
        /// attempts that follow, each from the original hole, use these multiples of the mean boundary edge length
        /// as target edge length, in this order. The remeshing of the coarse fan fill folds it back over the seam
        /// when the edge length is well above the boundary edge length, and runs into the surrounding mesh when it is
        /// far below (#1507); about the boundary edge length followed the seam in every case measured.
        /// </summary>
        /// <summary> number of fill attempts of the last Apply() (1 unless the first fill was not sound) </summary>
        internal int AttemptCount;

        static readonly double[] RetryBoundaryFactors = { 0.75, 1.0, 0.5 };

        static double mean_boundary_edge(EdgeLoop loop)
        {
            DMesh3 mesh = loop.Mesh;
            double sum = 0;
            foreach (int eid in loop.Edges) {
                Index2i ev = mesh.GetEdgeV(eid);
                sum += mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
            }
            return loop.Edges.Length == 0 ? 0 : sum / loop.Edges.Length;
        }

        /// <summary>
        /// seam triangle pairs with normals more than this many degrees apart are a fold (180: lying back over each other),
        /// unless the input is that sharp itself: the limit is at least the sharpest dihedral of the input triangles at the seam
        /// plus SeamMarginDeg (a thin wedge with the hole along its edge), and below 180 - SeamMarginDeg / 2
        /// </summary>
        const double MaxSeamAngleDeg = 170;
        const double SeamMarginDeg = 3;

        // the fill has no fold over its seam and no triangle that intersects another one of the mesh (exact test of
        // MeshRepairMetrics, only pairs with a fill triangle: intersections of the input are not ours). Ids that are no
        // triangle are skipped.
        internal static bool FillIsSound(DMesh3 mesh, int[] fillTriangles)
        {
            List<int> fillList = new List<int>(fillTriangles.Length);
            foreach (int tid in fillTriangles)
                if (mesh.IsTriangle(tid))
                    fillList.Add(tid);
            HashSet<int> fill = new HashSet<int>(fillList);
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            List<Index2i> seam = new List<Index2i>();       // (fill triangle, input triangle)
            foreach (int tid in fillList) {
                Index3i t = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j) {
                    Vector3d v = mesh.GetVertex(t[j]);
                    if (is_finite(v.x) == false || is_finite(v.y) == false || is_finite(v.z) == false)
                        return false;
                    box.Contain(v);
                }
                Index3i te = mesh.GetTriEdges(tid);
                for (int j = 0; j < 3; ++j) {
                    Index2i et = mesh.GetEdgeT(te[j]);
                    int other = (et.a == tid) ? et.b : et.a;
                    if (other == DMesh3.InvalidID || fill.Contains(other))
                        continue;
                    seam.Add(new Index2i(tid, other));
                }
            }

            // the sharpest dihedral of the input at the seam (between its triangles next to the seam and their neighbours)
            double inputSharpest = 0;
            HashSet<int> seen = new HashSet<int>();
            foreach (Index2i pair in seam) {
                if (seen.Add(pair.b) == false)
                    continue;
                Index3i oe = mesh.GetTriEdges(pair.b);
                for (int j = 0; j < 3; ++j) {
                    Index2i et = mesh.GetEdgeT(oe[j]);
                    if (et.b == DMesh3.InvalidID || fill.Contains(et.a) || fill.Contains(et.b))
                        continue;
                    double ang = Vector3d.AngleD(mesh.GetTriNormal(et.a), mesh.GetTriNormal(et.b));
                    if (ang > inputSharpest) inputSharpest = ang;
                }
            }
            double limit = Math.Min(Math.Max(MaxSeamAngleDeg, inputSharpest + SeamMarginDeg), 180 - SeamMarginDeg / 2);
            foreach (Index2i pair in seam)
                if (Vector3d.AngleD(mesh.GetTriNormal(pair.a), mesh.GetTriNormal(pair.b)) > limit)
                    return false;

            List<int> near = new List<int>(fillList);
            foreach (int tid in mesh.TriangleIndices()) {
                if (fill.Contains(tid))
                    continue;
                Index3i t = mesh.GetTriangle(tid);
                AxisAlignedBox3d tb = new AxisAlignedBox3d(mesh.GetVertex(t.a));
                tb.Contain(mesh.GetVertex(t.b)); tb.Contain(mesh.GetVertex(t.c));
                // (an input triangle with a non-finite vertex cannot be judged; the exact test rejects it)
                if (is_finite(tb.Min.x) == false || is_finite(tb.Min.y) == false || is_finite(tb.Min.z) == false ||
                    is_finite(tb.Max.x) == false || is_finite(tb.Max.y) == false || is_finite(tb.Max.z) == false)
                    continue;
                if (box.Intersects(tb))
                    near.Add(tid);
            }
            DSubmesh3 sub = new DSubmesh3(mesh) { ComputeTriMaps = true };
            sub.Compute(near);
            foreach (Index2i pair in MeshRepairMetrics.SelfIntersectionPairs(sub.SubMesh)) {
                if (fill.Contains(sub.MapTriangleToBaseMesh(pair.a)) || fill.Contains(sub.MapTriangleToBaseMesh(pair.b)))
                    return false;
            }
            return true;
        }

        static bool is_finite(double x) { return double.IsNaN(x) == false && double.IsInfinity(x) == false; }

        // reopen the hole: the seam edges are constrained in the remeshing, so the boundary loop is as before the fill
        void undo_fill()
        {
            foreach (int tid in FillTriangles)
                if (Mesh.IsTriangle(tid))
                    Mesh.RemoveTriangle(tid, true, false);
        }

        void smooth_and_remesh_preserve(MeshFaceSelection tris, bool bFinal, double targetEdgeLength)
        {
            if (EnableLaplacianSmooth) {
                LaplacianMeshSmoother.RegionSmooth(Mesh, tris, 2, 2, true);
            }

            if (RemeshAfterSmooth) {
                MeshProjectionTarget target = (bFinal) ? MeshProjectionTarget.Auto(Mesh, tris, 5) : null;

                RegionRemesher remesh2 = new RegionRemesher(Mesh, tris);
                remesh2.SetTargetEdgeLength(targetEdgeLength);
                remesh2.SmoothSpeedT = 1.0;
                remesh2.SetProjectionTarget(target);
                if (ConfigureRemesherF != null)
                    ConfigureRemesherF(remesh2, false);
                for (int k = 0; k < 10; ++k)
                    remesh2.BasicRemeshPass();
                remesh2.BackPropropagate();

                FillTriangles = remesh2.CurrentBaseTriangles;
            } else {
                FillTriangles = tris.ToArray();
            }
        }



        void smooth_and_remesh(MeshFaceSelection tris, double targetEdgeLength)
        {
            if (EnableLaplacianSmooth) {
                LaplacianMeshSmoother.RegionSmooth(Mesh, tris, 2, 2, false);
            }

            if (RemeshAfterSmooth) {
                tris.ExpandToOneRingNeighbours(2);
                tris.LocalOptimize(true, true);
                MeshProjectionTarget target = MeshProjectionTarget.Auto(Mesh, tris, 5);

                RegionRemesher remesh2 = new RegionRemesher(Mesh, tris);
                remesh2.SetTargetEdgeLength(targetEdgeLength);
                remesh2.SmoothSpeedT = 1.0;
                remesh2.SetProjectionTarget(target);
                if (ConfigureRemesherF != null)
                    ConfigureRemesherF(remesh2, false);
                for (int k = 0; k < 10; ++k)
                    remesh2.BasicRemeshPass();
                remesh2.BackPropropagate();

                FillTriangles = remesh2.CurrentBaseTriangles;
            } else {
                FillTriangles = tris.ToArray();
            }
        }









        EdgeLoop select_loop_tris_hint(MeshBoundaryLoops loops)
        {
            HashSet<int> hint_edges = new HashSet<int>();
            foreach ( int tid in BorderHintTris ) {
                if (Mesh.IsTriangle(tid) == false)
                    continue;
                Index3i et = Mesh.GetTriEdges(tid);
                for (int j = 0; j < 3; ++j) {
                    if (Mesh.IsBoundaryEdge(et[j]))
                        hint_edges.Add(et[j]);
                }
            }


            int N = loops.Count;
            int best_loop = -1;
            int max_votes = 0;
            for ( int li = 0; li < N; ++li ) {
                int votes = 0;
                EdgeLoop l = loops[li];
                foreach (int eid in l.Edges) {
                    if (hint_edges.Contains(eid))
                        votes++;
                }
                if ( votes > max_votes ) {
                    best_loop = li;
                    max_votes = votes;
                }
            }

            if (best_loop == -1)
                return null;
            return loops[best_loop];

        }


    }
}
