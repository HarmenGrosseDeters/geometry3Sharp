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
    /// For closed, consistently oriented input meshes, most node values are not evaluated but copied from a
    /// neighbour node: the winding number of such a mesh is an integer that only changes where the
    /// surface is crossed, so if no triangle touches the grid edge to the neighbour, the value is the same.
    /// With open borders (holes, cracks) or inconsistent orientation (edges whose two triangles run along
    /// them in the same direction act like borders) the winding number varies continuously, so then all
    /// values are evaluated. Values that may be copied are computed from the winding numbers rounded to
    /// integers, so the result does not depend on the order in which (parallel) evaluations fill the cache.
    ///
    /// With several input meshes, node values are CombineF(winding numbers of all meshes), eg max for a
    /// union (see MeshWindingNumberBoolean); cells overlapping triangles of any mesh are refined.
    /// </summary>
    public class WindingNumberOctree
    {
        public DMesh3 Mesh;
        public DMeshAABBTree3 Spatial;

        /// <summary> all input meshes and their trees (Mesh / Spatial are the first ones) </summary>
        public DMesh3[] Meshes;
        public DMeshAABBTree3[] Spatials;
        /// <summary> combines the winding numbers of the inputs into the node value (null for a single input) </summary>
        public Func<double[], double> CombineF = null;

        /// <summary> extra shift of the leaf grid, in cells, eg an odd fraction so faces at round coordinates miss the nodes </summary>
        public Vector3d GridOffset = Vector3d.Zero;

        public double WindingIso = 0.5;

        /// <summary> octree depth; leaf grid is 2^MaxDepth cells along each axis. Ignored if TargetLeafCellSize > 0 </summary>
        public int MaxDepth = 7;
        /// <summary> if > 0, MaxDepth is chosen so that leaf cells have this size </summary>
        public double TargetLeafCellSize = 0;
        /// <summary> empty leaf cells kept around the mesh bounds </summary>
        public int PaddingCells = 2;

        /// <summary> for closed, consistently oriented meshes, copy neighbour values instead of evaluating (see class comment) </summary>
        public bool PropagateValues = true;
        /// <summary>
        /// only values with |w - WindingIso| above this are copied to neighbours; with propagation they are
        /// computed from the rounded (integer) winding numbers (see NodeValue)
        /// </summary>
        public double PropagateMargin = 0.3;

        public Func<bool> CancelF = () => { return false; };

        /// <summary>
        /// optional: only octree cells for which this returns true are refined (cell min corner and size in
        /// leaf cells), eg to restrict a finer octree to a region
        /// </summary>
        public Func<Vector3i, int, bool> RegionF = null;

        // outputs
        public Vector3d Origin;
        public double LeafCellSize;
        public int LeafCount;
        public List<Vector3i> SurfaceCells = new List<Vector3i>();
        public int WindingEvaluations { get { return evaluations; } }
        /// <summary> number of nodes with a known winding number (evaluated or propagated) </summary>
        public int NodeCount { get { return cache == null ? 0 : cache.Count; } }

        public WindingNumberOctree(DMesh3 mesh, DMeshAABBTree3 spatial)
            : this(new DMesh3[] { mesh }, new DMeshAABBTree3[] { spatial }, null)
        {
        }

        public WindingNumberOctree(DMesh3[] meshes, DMeshAABBTree3[] spatials, Func<double[], double> combineF)
        {
            if (meshes.Length == 0 || meshes.Length != spatials.Length)
                throw new ArgumentException("WindingNumberOctree: need one tree per mesh");
            if (meshes.Length > 1 && combineF == null)
                throw new ArgumentException("WindingNumberOctree: several meshes need a CombineF");
            Meshes = meshes;
            Spatials = spatials;
            CombineF = combineF;
            Mesh = meshes[0];
            Spatial = spatials[0];
        }


        /// <summary> deepest supported octree (node keys have 21 bits per axis) </summary>
        public const int MaxSupportedDepth = 20;

        /// <summary> bounds of the meshes with triangles (empty meshes do not drag in the origin) </summary>
        public static AxisAlignedBox3d Bounds(IEnumerable<DMesh3> meshes)
        {
            AxisAlignedBox3d bounds = AxisAlignedBox3d.Empty;
            foreach (DMesh3 m in meshes)
                if (m.TriangleCount > 0)
                    bounds.Contain(m.CachedBounds);
            return bounds;
        }

        /// <summary> octree depth needed for leaf cells of the given size over the bounds </summary>
        public static int RequiredDepth(AxisAlignedBox3d bounds, double cellSize, int paddingCells = 2)
        {
            double maxDim = Math.Max(bounds.MaxDim, MathUtil.Epsilon);
            double n = maxDim / cellSize + 2 * paddingCells;
            if (!(n < (double)(1 << 30)))
                return int.MaxValue;
            return Math.Max(1, (int)Math.Ceiling(Math.Log(n, 2)));
        }

        public void Build()
        {
            AxisAlignedBox3d bounds = Bounds(Meshes);
            if (bounds.MaxDim < 0)
                throw new ArgumentException("WindingNumberOctree: no triangles");
            double maxDim = Math.Max(bounds.MaxDim, MathUtil.Epsilon);
            int depth = MaxDepth;
            if (TargetLeafCellSize > 0)
                depth = RequiredDepth(bounds, TargetLeafCellSize, PaddingCells);
            if (depth > MaxSupportedDepth)
                throw new ArgumentException("WindingNumberOctree: depth " + depth + " is too large (cell size too small for the bounds)");
            if (TargetLeafCellSize <= 0 && (1 << depth) <= 2 * PaddingCells)
                throw new ArgumentException("WindingNumberOctree: MaxDepth too small for the padding");
            MaxDepth = depth;
            LeafCount = 1 << depth;
            LeafCellSize = (TargetLeafCellSize > 0) ? TargetLeafCellSize : maxDim / (LeafCount - 2 * PaddingCells);
            Origin = bounds.Min - PaddingCells * LeafCellSize * Vector3d.One - LeafCellSize * GridOffset;
            build();
        }

        /// <summary>
        /// Build on a given leaf grid: 2^depth leaf cells of size leafCellSize along each axis from origin
        /// (eg a finer grid aligned with the grid of another octree, restricted with RegionF). Building again on
        /// the same grid (eg with a larger region) keeps the node values computed so far.
        /// </summary>
        public void BuildOnGrid(Vector3d origin, double leafCellSize, int depth)
        {
            if (depth < 1 || depth > MaxSupportedDepth)
                throw new ArgumentException("WindingNumberOctree: depth " + depth + " is not supported");
            keepCache = cache != null && origin == Origin && leafCellSize == LeafCellSize && depth == MaxDepth;
            MaxDepth = depth;
            LeafCount = 1 << depth;
            LeafCellSize = leafCellSize;
            Origin = origin;
            build();
        }

        bool keepCache = false;
        double cacheIso, cacheMargin;

        void build()
        {
            bool prop = PropagateValues;
            for (int i = 0; i < Meshes.Length; ++i) {
                Spatials[i].FastWindingNumber(Vector3d.Zero);    // build the winding cache before parallel use
                prop = prop && is_closed_and_oriented(Meshes[i]);
            }
            // the cached values were computed with the same rules (rounded or not, see evaluate_rounded)
            keepCache = keepCache && prop == propagate && WindingIso == cacheIso && PropagateMargin == cacheMargin;
            if (keepCache == false) {
                cache = new ConcurrentDictionary<long, float>();
                notCopyable = new ConcurrentDictionary<long, byte>();
                anyNotCopyable = false;
                evaluations = 0;
            }
            keepCache = false;
            propagate = prop;
            cacheIso = WindingIso;
            cacheMargin = PropagateMargin;

            ConcurrentBag<Vector3i> cells = new ConcurrentBag<Vector3i>();
            refine(Vector3i.Zero, LeafCount, cells, 0);
            SurfaceCells = cells.ToList();
            // deterministic order
            SurfaceCells.Sort((a, b) => a.z != b.z ? a.z.CompareTo(b.z) : (a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x)));
        }


        /// <summary>
        /// winding number at leaf-grid node (i,j,k), computed (or copied from a neighbour) on first use. With
        /// propagation, values away from WindingIso (by more than PropagateMargin) are computed from the
        /// winding numbers rounded to integers, so they do not depend on whether they were copied.
        /// </summary>
        public double NodeValue(Vector3i node)
        {
            long key = node_key(node.x, node.y, node.z);
            float value;
            if (cache.TryGetValue(key, out value))
                return value;
            Vector3d pos = Origin + LeafCellSize * new Vector3d(node.x, node.y, node.z);
            if (propagate == false)
                value = evaluate(pos);
            else if (try_copy_from_neighbour(node, pos, out value) == false)
                value = evaluate_rounded(key, pos);
            cache[key] = value;
            return value;
        }

        // With propagation, a node gets either its own value or a copy of a neighbour's, depending on which
        // neighbours the other threads have evaluated first. Off the surface of closed, oriented meshes the
        // winding numbers are integers, so values that may be copied (|value - WindingIso| > PropagateMargin)
        // are computed from the rounded winding numbers: then a copy equals the node's own value and the result
        // does not depend on the thread timing (without this, the fast winding number error of the two differed
        // in the last digits and moved contour vertices). Values that rounding would move into the margin or
        // across WindingIso (a large approximation error) are kept as they are and never copied.
        // (the margin is tested on the stored float, as try_copy_from_neighbour does)
        float evaluate_rounded(long key, Vector3d pos)
        {
            Interlocked.Increment(ref evaluations);
            double value, rounded;
            if (Meshes.Length == 1) {
                value = Spatial.FastWindingNumber(pos);
                rounded = Math.Round(value);
            } else {
                double[] w = winding_numbers(pos);
                value = CombineF(w);
                for (int i = 0; i < w.Length; ++i)
                    w[i] = Math.Round(w[i]);
                rounded = CombineF(w);
            }
            float fv = (float)value, fr = (float)rounded;
            if (Math.Abs(fv - WindingIso) <= PropagateMargin)
                return fv;
            if (Math.Abs(fr - WindingIso) > PropagateMargin && (fr > WindingIso) == (fv > WindingIso))
                return fr;
            notCopyable[key] = 0;
            anyNotCopyable = true;
            return fv;
        }

        // nodes whose value is outside the margin but must not be copied (see evaluate_rounded)
        ConcurrentDictionary<long, byte> notCopyable;
        volatile bool anyNotCopyable;

        // the winding number is piecewise constant (an integer) only for a closed, consistently oriented
        // mesh: an edge whose two triangles run along it in the same direction acts like a boundary
        static bool is_closed_and_oriented(DMesh3 mesh)
        {
            foreach (int eid in mesh.EdgeIndices()) {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.b == DMesh3.InvalidID)
                    return false;
                Index2i ev = mesh.GetEdgeV(eid);
                if (IndexUtil.find_tri_ordered_edge(ev.a, ev.b, mesh.GetTriangle(et.a)) >= 0 ==
                    IndexUtil.find_tri_ordered_edge(ev.a, ev.b, mesh.GetTriangle(et.b)) >= 0)
                    return false;
            }
            return true;
        }

        float evaluate(Vector3d pos)
        {
            Interlocked.Increment(ref evaluations);
            return (float)Evaluate(pos);
        }

        /// <summary> node value at an arbitrary point (single winding number, or CombineF of all) </summary>
        public double Evaluate(Vector3d pos)
        {
            if (Meshes.Length == 1)
                return Spatial.FastWindingNumber(pos);
            return CombineF(winding_numbers(pos));
        }

        double[] winding_numbers(Vector3d pos)
        {
            double[] w = new double[Meshes.Length];
            for (int i = 0; i < Meshes.Length; ++i)
                w[i] = Spatials[i].FastWindingNumber(pos);
            return w;
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
                long nbKey = node_key(nb.x, nb.y, nb.z);
                if (cache.TryGetValue(nbKey, out nbValue) == false || (anyNotCopyable && notCopyable.ContainsKey(nbKey)))
                    continue;
                if (Math.Abs(nbValue - WindingIso) <= PropagateMargin)
                    continue;
                // does any triangle touch the grid edge from the neighbour to this node? Tested against a thin
                // box around the edge, not with a ray: a ray through a shared edge or vertex of two triangles
                // can miss both, and a wrongly copied value then spreads through the whole region.
                Vector3d nbPos = Origin + LeafCellSize * new Vector3d(nb.x, nb.y, nb.z);
                Vector3d halfExtents = 1e-3 * LeafCellSize * Vector3d.One;
                halfExtents[k / 2] = 0.5 * LeafCellSize * (1 + 1e-3);
                bool crossed = false;
                for (int i = 0; i < Meshes.Length && crossed == false; ++i)
                    crossed = triangles_overlap(Meshes[i], Spatials[i], 0.5 * (pos + nbPos), halfExtents);
                if (crossed)
                    continue;
                value = nbValue;
                return true;
            }
            return false;
        }


        ConcurrentDictionary<long, float> cache;
        bool propagate;
        int evaluations;

        // biased, so nodes slightly outside the grid (sparse expansion) have distinct keys
        const int KeyBias = 1 << 19;
        static long node_key(int i, int j, int k)
        {
            return (long)(i + KeyBias) | ((long)(j + KeyBias) << 21) | ((long)(k + KeyBias) << 42);
        }


        void refine(Vector3i min, int size, ConcurrentBag<Vector3i> cells, int level)
        {
            if (CancelF())
                return;
            if (RegionF != null && RegionF(min, size) == false)
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
            for (int i = 0; i < Meshes.Length; ++i)
                if (triangles_overlap(Meshes[i], Spatials[i], center, halfSize * Vector3d.One))
                    return true;
            return false;
        }

        static bool triangles_overlap(DMesh3 mesh, DMeshAABBTree3 spatial, Vector3d center, Vector3d halfExtents)
        {
            AxisAlignedBox3d box = new AxisAlignedBox3d(center - halfExtents, center + halfExtents);
            bool found = false;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            var traversal = new DMeshAABBTree3.TreeTraversal() {
                NextBoxDF = (treeBox, depth) => found == false && box.Intersects(treeBox),
                NextTriangleF = (tid) => {
                    if (found) return;
                    mesh.GetTriVertices(tid, ref a, ref b, ref c);
                    if (TriangleBoxOverlap(a - center, b - center, c - center, halfExtents))
                        found = true;
                }
            };
            spatial.DoTraversal(traversal);
            return found;
        }


        /// <summary>
        /// Separating axis test (Akenine-Moller) of a triangle against the cube [-h,h]^3; the triangle
        /// vertices are given relative to the cube center.
        /// </summary>
        public static bool TriangleBoxOverlap(Vector3d v0, Vector3d v1, Vector3d v2, double h)
        {
            return TriangleBoxOverlap(v0, v1, v2, h * Vector3d.One);
        }

        /// <summary> as above, for the box [-h.x,h.x] x [-h.y,h.y] x [-h.z,h.z] </summary>
        public static bool TriangleBoxOverlap(Vector3d v0, Vector3d v1, Vector3d v2, Vector3d h)
        {
            // box face normals
            for (int k = 0; k < 3; ++k) {
                double mn = Math.Min(v0[k], Math.Min(v1[k], v2[k])), mx = Math.Max(v0[k], Math.Max(v1[k], v2[k]));
                if (mn > h[k] || mx < -h[k])
                    return false;
            }
            // triangle plane
            Vector3d e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
            Vector3d n = e0.Cross(e1);
            double r = h.x * Math.Abs(n.x) + h.y * Math.Abs(n.y) + h.z * Math.Abs(n.z);
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
                    double rad = h.x * Math.Abs(axis.x) + h.y * Math.Abs(axis.y) + h.z * Math.Abs(axis.z);
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
