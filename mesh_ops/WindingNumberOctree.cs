// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using g3;

namespace gs
{
    /// <summary>
    /// Adaptive octree over a mesh that finds the leaf cells the WindingIso surface of the mesh winding
    /// number can pass through, evaluating the winding number only at the corners of cells it subdivides.
    /// The work grows with the surface area (x4 per level) instead of the volume (x8 per level).
    ///
    /// A cell is subdivided if input triangles are near it, or if the winding number crosses WindingIso
    /// between its corners (this catches iso-surfaces spanning holes, away from the triangles).
    /// SurfaceCells are leaf cells in integer leaf-grid coordinates: cell (i,j,k) spans
    /// Origin + LeafCellSize * [i,i+1] x [j,j+1] x [k,k+1]. NodeValue() gives (cached) winding numbers at
    /// leaf-grid nodes, for use with DualContouring.CandidateCells / NodeValueF.
    ///
    /// For closed input meshes (no boundary edges), most node values are not evaluated but copied from a
    /// neighbour node: the winding number of a closed mesh is an integer that only changes where the
    /// surface is crossed, so if no triangle crosses the grid edge to the neighbour, the value is the same.
    /// With open borders (holes, cracks) the winding number is fractional and varies continuously
    /// everywhere, so then all values are evaluated.
    /// </summary>
    public class WindingNumberOctree
    {
        public DMesh3 Mesh;
        public DMeshAABBTree3 Spatial;

        public double WindingIso = 0.5;

        /// <summary> octree depth; leaf grid is 2^MaxDepth cells along each axis. Ignored if TargetLeafCellSize > 0 </summary>
        public int MaxDepth = 7;
        /// <summary> if > 0, MaxDepth is chosen so that leaf cells have this size </summary>
        public double TargetLeafCellSize = 0;
        /// <summary> empty leaf cells kept around the mesh bounds </summary>
        public int PaddingCells = 2;

        /// <summary> for closed meshes, copy neighbour values instead of evaluating (see class comment) </summary>
        public bool PropagateValues = true;
        /// <summary> only values with |w - WindingIso| above this are copied to neighbours </summary>
        public double PropagateMargin = 0.3;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public Vector3d Origin;
        public double LeafCellSize;
        public int LeafCount;
        public List<Vector3i> SurfaceCells = new List<Vector3i>();
        public int WindingEvaluations { get { return evaluations; } }
        /// <summary> number of nodes with a known winding number (evaluated or propagated) </summary>
        public int NodeCount { get { return cache == null ? 0 : cache.Count; } }

        public WindingNumberOctree(DMesh3 mesh, DMeshAABBTree3 spatial)
        {
            Mesh = mesh;
            Spatial = spatial;
        }


        public void Build()
        {
            AxisAlignedBox3d bounds = Mesh.CachedBounds;
            double maxDim = Math.Max(bounds.MaxDim, MathUtil.Epsilon);
            int depth = MaxDepth;
            if (TargetLeafCellSize > 0)
                depth = Math.Max(1, (int)Math.Ceiling(Math.Log(maxDim / TargetLeafCellSize + 2 * PaddingCells, 2)));
            if (depth > 20)
                throw new Exception("WindingNumberOctree: depth " + depth + " is too large");
            MaxDepth = depth;
            LeafCount = 1 << depth;
            LeafCellSize = (TargetLeafCellSize > 0) ? TargetLeafCellSize : maxDim / (LeafCount - 2 * PaddingCells);
            Origin = bounds.Min - PaddingCells * LeafCellSize * Vector3d.One;

            cache = new ConcurrentDictionary<long, float>();
            evaluations = 0;
            Spatial.FastWindingNumber(Vector3d.Zero);    // build the winding cache before parallel use
            propagate = PropagateValues && Mesh.IsClosed();

            ConcurrentBag<Vector3i> cells = new ConcurrentBag<Vector3i>();
            refine(Vector3i.Zero, LeafCount, cells, 0);
            SurfaceCells = cells.ToList();
            // deterministic order
            SurfaceCells.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : (a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)));
        }


        /// <summary> winding number at leaf-grid node (i,j,k), computed (or copied from a neighbour) on first use </summary>
        public double NodeValue(Vector3i node)
        {
            long key = node_key(node.x, node.y, node.z);
            float value;
            if (cache.TryGetValue(key, out value))
                return value;
            Vector3d pos = Origin + LeafCellSize * new Vector3d(node.x, node.y, node.z);
            if (propagate == false || try_copy_from_neighbour(node, pos, out value) == false)
                value = evaluate(pos);
            cache[key] = value;
            return value;
        }

        float evaluate(Vector3d pos)
        {
            Interlocked.Increment(ref evaluations);
            return (float)Spatial.FastWindingNumber(pos);
        }


        static readonly Vector3i[] axis_neighbours = {
            new Vector3i(-1, 0, 0), new Vector3i(1, 0, 0), new Vector3i(0, -1, 0),
            new Vector3i(0, 1, 0), new Vector3i(0, 0, -1), new Vector3i(0, 0, 1) };

        bool try_copy_from_neighbour(Vector3i node, Vector3d pos, out float value)
        {
            value = 0;
            for (int k = 0; k < 6; ++k) {
                Vector3i nb = node + axis_neighbours[k];
                float nbValue;
                if (cache.TryGetValue(node_key(nb.x, nb.y, nb.z), out nbValue) == false)
                    continue;
                if (Math.Abs(nbValue - WindingIso) <= PropagateMargin)
                    continue;
                // does any triangle cross the grid edge from the neighbour to this node?
                Vector3d nbPos = Origin + LeafCellSize * new Vector3d(nb.x, nb.y, nb.z);
                Ray3d ray = new Ray3d(nbPos, (pos - nbPos).Normalized);
                if (Spatial.FindNearestHitTriangle(ray, LeafCellSize * (1 + 1e-6)) != DMesh3.InvalidID)
                    continue;
                value = nbValue;
                return true;
            }
            return false;
        }


        ConcurrentDictionary<long, float> cache;
        bool propagate;
        int evaluations;

        static long node_key(int i, int j, int k)
        {
            return (long)i | ((long)j << 21) | ((long)k << 42);
        }


        void refine(Vector3i min, int size, ConcurrentBag<Vector3i> cells, int level)
        {
            if (CancelF())
                return;
            if (needs_refinement(min, size) == false)
                return;
            if (size == 1) {
                cells.Add(min);
                return;
            }
            int half = size / 2;
            Vector3i[] children = new Vector3i[8];
            for (int c = 0; c < 8; ++c)
                children[c] = min + half * new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1);
            if (level < 2)
                gParallel.ForEach(children, (child) => refine(child, half, cells, level + 1));
            else
                foreach (Vector3i child in children)
                    refine(child, half, cells, level + 1);
        }


        bool triangles_overlap(Vector3d center, double halfSize)
        {
            AxisAlignedBox3d box = new AxisAlignedBox3d(center, halfSize);
            bool found = false;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            var traversal = new DMeshAABBTree3.TreeTraversal() {
                NextBoxF = (treeBox, depth) => found == false && box.Intersects((AxisAlignedBox3d)treeBox),
                NextTriangleF = (tid) => {
                    if (found) return;
                    Mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    if (TriangleBoxOverlap(a - center, b - center, c - center, halfSize))
                        found = true;
                }
            };
            Spatial.DoTraversal(traversal);
            return found;
        }


        /// <summary>
        /// Separating axis test (Akenine-Moller) of a triangle against the cube [-h,h]^3; the triangle
        /// vertices are given relative to the cube center.
        /// </summary>
        public static bool TriangleBoxOverlap(Vector3d v0, Vector3d v1, Vector3d v2, double h)
        {
            // box face normals
            for (int k = 0; k < 3; ++k) {
                double mn = Math.Min(v0[k], Math.Min(v1[k], v2[k])), mx = Math.Max(v0[k], Math.Max(v1[k], v2[k]));
                if (mn > h || mx < -h)
                    return false;
            }
            // triangle plane
            Vector3d e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
            Vector3d n = e0.Cross(e1);
            double r = h * (Math.Abs(n.x) + Math.Abs(n.y) + Math.Abs(n.z));
            if (Math.Abs(n.Dot(v0)) > r)
                return false;
            // edge x box axis
            Vector3d[] edges = { e0, e1, e2 };
            for (int ei = 0; ei < 3; ++ei) {
                for (int k = 0; k < 3; ++k) {
                    Vector3d axis = Vector3d.Zero;
                    axis[k] = 1;
                    axis = edges[ei].Cross(axis);
                    double p0 = axis.Dot(v0), p1 = axis.Dot(v1), p2 = axis.Dot(v2);
                    double rad = h * (Math.Abs(axis.x) + Math.Abs(axis.y) + Math.Abs(axis.z));
                    if (Math.Min(p0, Math.Min(p1, p2)) > rad || Math.Max(p0, Math.Max(p1, p2)) < -rad)
                        return false;
                }
            }
            return true;
        }


        bool needs_refinement(Vector3i min, int size)
        {
            // input triangles overlapping the cell (slightly enlarged, so triangles exactly on a cell
            // face count for both cells)
            double halfSize = 0.5 * size * LeafCellSize;
            Vector3d center = Origin + LeafCellSize * new Vector3d(min.x, min.y, min.z) + halfSize * Vector3d.One;
            if (triangles_overlap(center, halfSize * (1 + 1e-6)))
                return true;

            // iso crossing between corners
            bool inside0 = NodeValue(min) > WindingIso;
            for (int c = 1; c < 8; ++c) {
                Vector3i node = min + size * new Vector3i(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                if ((NodeValue(node) > WindingIso) != inside0)
                    return true;
            }
            return false;
        }
    }
}
