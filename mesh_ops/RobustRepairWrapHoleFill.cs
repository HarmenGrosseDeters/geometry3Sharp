using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// The hole fill of MeshRobustRepair before the alpha wrap (#1542 option 1, #1575): the boundary loops of a welded mesh that are short against
    /// the wrap's alpha, and against the mesh, are the holes of a surface that is meant to be closed. An alpha ball that fits through such a hole
    /// passes it and wraps both sides of the surface (one component of high genus, twice the area, 2.2 times the density, a thin shell instead of a
    /// solid). The loops are filled (SimpleHoleFiller for a triangle, MinimalHoleFill above), and the copy is wrapped.
    /// A loop is filled only if (1) its length is at most the larger of MaxLoopAlphas * alpha and MaxLoopEdges median edge lengths, and at most MaxLoopDiagonalShare of the mesh's bounding box
    /// diagonal (an open end or the rim of a sheet is as long as the part it bounds: the second bound keeps a coarse alpha from closing it), (2) the
    /// fill is valid (an oriented boundary loop) and (3) the fill's area is at most half the area of the component the loop is on (a single
    /// unwelded triangle, or a small sheet, is not a hole in something: filling it makes a flat lens of twice the area).
    /// A mesh with no such loop is not copied (null): the wrap of a clean input is as before.
    /// </summary>
    internal static class RobustRepairWrapHoleFill
    {
        /// <summary> the largest loop length that is filled, in alphas of the wrap (the holes of the repro of #1575 are about 3 to 45 alphas long at the default alpha: a triangle of the input wide) </summary>
        internal const double MaxLoopAlphas = 64;
        /// <summary> the largest loop length that is filled, in median edge lengths of the mesh, where that is more than MaxLoopAlphas alphas (a coarse input: a hole of a few missing triangles is longer than 64 alphas, and the alpha ball passes it all the same) </summary>
        internal const double MaxLoopEdges = 8;
        /// <summary> the largest loop length that is filled, as a share of the diagonal of the bounding box of the mesh </summary>
        internal const double MaxLoopDiagonalShare = 0.25;
        /// <summary> the fill's area may be at most this share of the area of its component </summary>
        internal const double MaxFillAreaShare = 0.5;

        /// <summary> the mesh with its short boundary loops filled, or null if there is none (the mesh is closed, or no loop qualifies). The mesh is not changed </summary>
        internal static DMesh3 Fill(DMesh3 mesh, double alpha, double maxLoopAlphas, Func<bool> cancel, out int filled, out int candidates)
        {
            filled = candidates = 0;
            if (mesh.TriangleCount == 0 || !(alpha > 0) || !(maxLoopAlphas > 0))
                return null;
            bool open = false;
            foreach (int eid in mesh.BoundaryEdgeIndices()) {
                open = true;
                break;
            }
            if (open == false)
                return null;
            double diagonal = MeshRepairMetrics.TriangleBounds(mesh).DiagonalLength;
            double bound = Math.Min(Math.Max(maxLoopAlphas * alpha, MaxLoopEdges * MedianEdgeLength(mesh)), MaxLoopDiagonalShare * diagonal);
            if (!(bound > 0))
                return null;

            var loops = new MeshBoundaryLoops(mesh, false) { SpanBehavior = MeshBoundaryLoops.SpanBehaviors.Ignore };
            loops.Compute();
            var short_loops = new List<EdgeLoop>();
            foreach (EdgeLoop loop in loops.Loops) {
                if (loop.VertexCount < 3)
                    continue;
                double length = 0;
                for (int i = 0; i < loop.VertexCount; ++i)
                    length += loop.GetVertex(i).Distance(loop.GetVertex((i + 1) % loop.VertexCount));
                if (length <= bound)
                    short_loops.Add(loop);
            }
            candidates = short_loops.Count;
            if (short_loops.Count == 0)
                return null;

            // the area of each component (a loop is judged against its own)
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            var compOf = new Dictionary<int, int>();
            var compArea = new double[comps.Count];
            for (int c = 0; c < comps.Count; ++c)
                foreach (int tid in comps[c].Indices) {
                    compOf[tid] = c;
                    compArea[c] += mesh.GetTriArea(tid);
                }

            DMesh3 copy = null;
            foreach (EdgeLoop loop in short_loops) {
                if (cancel != null && cancel())
                    return null;
                int tid = mesh.GetEdgeT(loop.Edges[0]).a;
                int comp;
                if (compOf.TryGetValue(tid, out comp) == false)
                    continue;
                if (!(FanArea(loop) <= MaxFillAreaShare * compArea[comp]))
                    continue;
                if (copy == null)
                    copy = new DMesh3(mesh);
                var loopOnCopy = new EdgeLoop(copy, loop.Vertices, loop.Edges, true);
                var simple = new SimpleHoleFiller(copy, loopOnCopy);
                if (simple.Validate() != ValidationStatus.Ok)
                    continue;
                bool ok;
                if (loop.VertexCount == 3)
                    ok = simple.Fill();
                else {
                    var minimal = new MinimalHoleFill(copy, loopOnCopy);
                    ok = minimal.Apply() || simple.Fill();
                }
                if (ok)
                    ++filled;
            }
            return filled > 0 ? copy : null;
        }

        static double MedianEdgeLength(DMesh3 mesh)
        {
            var lengths = new List<double>(mesh.EdgeCount);
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i ev = mesh.GetEdgeV(eid);
                lengths.Add(mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b)));
            }
            if (lengths.Count == 0)
                return 0;
            lengths.Sort();
            return lengths[lengths.Count / 2];
        }

        // the area of the fan of the loop's vertices around their centroid (what a fill covers at least)
        static double FanArea(EdgeLoop loop)
        {
            Vector3d c = Vector3d.Zero;
            int n = loop.VertexCount;
            for (int i = 0; i < n; ++i)
                c += loop.GetVertex(i);
            c /= n;
            double area = 0;
            for (int i = 0; i < n; ++i)
                area += 0.5 * (loop.GetVertex(i) - c).Cross(loop.GetVertex((i + 1) % n) - c).Length;
            return area;
        }
    }
}
