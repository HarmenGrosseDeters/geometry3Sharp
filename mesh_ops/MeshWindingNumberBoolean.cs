// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using g3;

namespace gs
{
    /// <summary>
    /// Volumetric boolean of two meshes: the winding numbers of the inputs are combined into one field
    /// (union = max, intersection = min, difference = min(wA, 1 - wB)) and its 0.5 iso-surface is extracted
    /// with dual contouring on an adaptive octree (see MeshWindingNumberSolidify).
    ///
    /// Works for any input - open meshes, triangle soups, self-intersecting or overlapping shells - and
    /// always gives a closed result. Hermite data comes from exact intersections of the grid edges with the
    /// input meshes, so faces and the sharp edges where the inputs intersect are reproduced; coplanar faces
    /// need no special handling.
    ///
    /// The result is a new mesh (the input triangulation and attributes are not kept), and features smaller
    /// than CellSize (thin walls, narrow gaps) are lost. For exact booleans that keep the input geometry,
    /// use ExactMeshBoolean (mesh arrangement) instead. The inputs are not modified.
    /// </summary>
    public class MeshWindingNumberBoolean
    {
        /// <summary> boolean operation; Difference is A minus B </summary>
        public enum Operations { Union = 0, Intersection = 1, Difference = 2 }

        /// <summary> input meshes (not modified) </summary>
        public DMesh3 MeshA, MeshB;
        /// <summary> the operation to compute </summary>
        public Operations Operation;

        /// <summary> grid cell size, in the units of the inputs at every scale. If zero, uses bounding box diagonal of both inputs / GridResolution; NaN or infinite: Compute fails </summary>
        public double CellSize = 0;
        /// <summary> number of cells along the bounding box diagonal (of both inputs), used if CellSize is zero </summary>
        public int GridResolution = 128;

        /// <summary> run MeshPostRepairCleanup on the result </summary>
        public bool CleanupResult = true;
        /// <summary> simplify the result as long as the error stays below about ReduceTolerance * CellSize (0 = off) </summary>
        public double ReduceTolerance = 0.05;
        /// <summary> accuracy of the fast winding number (DMeshAABBTree3.FWNBeta) </summary>
        public double FastWindingBeta = 1.5;


        /// <summary>
        /// If the result has hidden gaps or walls (gaps or material narrower than a cell, see
        /// DualContouring.HiddenGapF / HiddenWallF), the cell size is halved until the narrowest is about two
        /// cells wide (see WindingContouring.ContourRefined); at most this many halvings. Hidden gaps make the
        /// surfaces on both sides intersect, thin walls vanish or break up. With LocalRefinement only the
        /// cells around them are refined; otherwise each level roughly quadruples the work (the octree work
        /// scales with the surface). Adaptive (octree) mode only.
        /// </summary>
        public int MaxRefineLevels = 2;
        /// <summary>
        /// refine only for at least this many hidden gap / wall edges (fewer are reported in HiddenGapEdges /
        /// HiddenWallEdges)
        /// </summary>
        public int MinRefineEdges = 8;
        /// <summary>
        /// refine only the cells around hidden gaps / walls (the rest keeps the cell size, see
        /// WindingContouring.ContourRefined); false: refine the whole grid
        /// </summary>
        public bool LocalRefinement = true;

        /// <summary> return true to cancel; Compute() then returns false </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> the boolean result computed by Compute() (a new mesh; empty if the result is empty) </summary>
        public DMesh3 Result;
        public int NonManifoldTriangles;
        public int WindingEvaluations;
        /// <summary> hidden gap edges in the final result (0 = none), and the cell size it was computed with </summary>
        public int HiddenGapEdges;
        /// <summary> grid edges through material thinner than a cell in the final result (0 = none) </summary>
        public int HiddenWallEdges;
        /// <summary> some hidden gaps / walls of the final result (center, width), for diagnostics </summary>
        public List<KeyValuePair<Vector3d, double>> HiddenSamples = new List<KeyValuePair<Vector3d, double>>();
        /// <summary> hidden gap edges at each refinement level </summary>
        public List<int> GapEdgesPerLevel = new List<int>();
        /// <summary> finest cell size used (CellSize unless refined) </summary>
        public double UsedCellSize;
        /// <summary> local refinement: number of cells (of CellSize) refined, and the refinement factor (CellSize / UsedCellSize) </summary>
        public int RefinedCells, RefineFactor = 1;
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();


        public MeshWindingNumberBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }


        /// <summary>
        /// Compute the boolean into Result. Outside an extent of [2^-32, 2^32] (of the triangles of both inputs) it runs on copies scaled by
        /// a power of two to an extent in [1, 2) and maps the result back (exact; CellSize and UsedCellSize stay in the units of the inputs), so
        /// a 2^k scaled input gives the scaled result at every size.
        /// </summary>
        /// <returns>
        /// false if both inputs are empty, CellSize is NaN or infinite, the cell size is not positive or too small
        /// for the octree, the extent is outside the band and the scale is not exact (a used coordinate that would be subnormal after it,
        /// or above 1e300, or an extent that cannot be brought into the band, or a CellSize that does not exist in the scaled frame), or the
        /// computation was cancelled, with Result null (also after an earlier success);
        /// true otherwise (Result may be an empty mesh, eg an intersection with an empty input).
        /// </returns>
        public bool Compute()
        {
            // Outside an extent of [2^-32, 2^32] the computation runs on copies scaled by a power of two to an extent in [1, 2) (exact), as
            // MeshWindingNumberSolidify does: the winding-number field overflows (the extent to the 4th power: no surface was found from about
            // 1e79 to 1e103 and above 1e153, and Compute() succeeded with an empty mesh) and its float and absolute tolerances degrade below
            // about 1e-12. A 2^k scale then gives the unit-scale result times 2^k. Inside the band nothing changes.
            reset_outputs();      // (also if the scaled run below throws)
            double s = unit_scale();
            if (s == 0) {
                // the extent is outside the band, but the scale is not exact for these inputs (a used coordinate that would be subnormal
                // after it, one above 1e300): the field would overflow or degrade, so report it instead of an empty or wrong mesh
                return false;
            }
            if (s == 1 || MathUtil.IsFinite(CellSize) == false)
                return compute_unit();
            double scaledCell = CellSize > 0 ? CellSize * s : CellSize;
            if (CellSize > 0 && (!(scaledCell > 0) || double.IsInfinity(scaledCell))) {
                return false;           // a cell size that does not exist in the scaled frame (as a cell too small for the octree)
            }
            DMesh3 a = new DMesh3(MeshA), b = new DMesh3(MeshB);
            MeshRepairMetrics.ScaleVertices(a, s);
            MeshRepairMetrics.ScaleVertices(b, s);
            var inner = new MeshWindingNumberBoolean(a, b, Operation) {
                CellSize = scaledCell, GridResolution = GridResolution, CleanupResult = CleanupResult, ReduceTolerance = ReduceTolerance,
                FastWindingBeta = FastWindingBeta, MaxRefineLevels = MaxRefineLevels, MinRefineEdges = MinRefineEdges,
                LocalRefinement = LocalRefinement, CancelF = CancelF
            };
            bool ok = inner.compute_unit();
            Result = inner.Result;
            if (Result != null)
                MeshRepairMetrics.ScaleVertices(Result, 1 / s);
            NonManifoldTriangles = inner.NonManifoldTriangles;
            WindingEvaluations = inner.WindingEvaluations;
            HiddenGapEdges = inner.HiddenGapEdges; HiddenWallEdges = inner.HiddenWallEdges;
            HiddenSamples = new List<KeyValuePair<Vector3d, double>>();
            foreach (var kv in inner.HiddenSamples)
                HiddenSamples.Add(new KeyValuePair<Vector3d, double>(kv.Key * (1 / s), kv.Value / s));
            GapEdgesPerLevel = inner.GapEdgesPerLevel;
            UsedCellSize = inner.UsedCellSize / s;
            RefinedCells = inner.RefinedCells; RefineFactor = inner.RefineFactor;
            PhaseMilliseconds = inner.PhaseMilliseconds;
            return ok;
        }

        // The power of two that brings the extent of both inputs' triangles to [1, 2) if it is outside [2^-32, 2^32], else 1; 0 if it
        // is outside but the scaling is not exact for a used vertex (a coordinate that would be subnormal after it) or a coordinate
        // would exceed 1e300.
        double unit_scale()
        {
            AxisAlignedBox3d box = AxisAlignedBox3d.Empty;
            if (MeshA.TriangleCount > 0) box.Contain(MeshRepairMetrics.TriangleBounds(MeshA));
            if (MeshB.TriangleCount > 0) box.Contain(MeshRepairMetrics.TriangleBounds(MeshB));
            double s = MeshRepairMetrics.unit_scale(box, -32, 32);
            if (s == 1)
                return 1;
            // (a scale is limited to 2^+-1023: an extent below 2^-1055 is not brought into the band)
            double scaledExtent = box.MaxDim * s;
            if (!(scaledExtent >= 2.3283064365386963e-10 && scaledExtent <= 4294967296.0))
                return 0;
            foreach (DMesh3 mesh in new DMesh3[] { MeshA, MeshB }) {
                foreach (int v in mesh.VertexIndices()) {
                    Vector3d p = mesh.GetVertex(v);
                    bool used = mesh.GetVtxEdgeCount(v) > 0;
                    for (int k = 0; k < 3; ++k) {
                        double x = Math.Abs(p[k]);
                        if (!(x * s <= 1e300))
                            return 0;
                        if (used && x != 0 && x * s < 2.2250738585072014e-308)
                            return 0;
                    }
                }
            }
            return s;
        }

        void reset_outputs()
        {
            Result = null;      // set only on success, below
            UsedCellSize = 0;
            NonManifoldTriangles = 0;
            WindingEvaluations = 0;
            HiddenGapEdges = 0;
            HiddenWallEdges = 0;
            HiddenSamples = new List<KeyValuePair<Vector3d, double>>();
            GapEdgesPerLevel = new List<int>();
            RefinedCells = 0;
            RefineFactor = 1;
            PhaseMilliseconds.Clear();
        }

        bool compute_unit()
        {
            reset_outputs();
            // (checked here: a NaN would pass as "automatic" below, +infinity as a valid size)
            if (MathUtil.IsFinite(CellSize) == false)
                return false;
            if (MeshA.TriangleCount == 0 && MeshB.TriangleCount == 0)
                return false;
            // empty inputs do not count (their bounds would drag in the origin)
            AxisAlignedBox3d bounds = WindingNumberOctree.Bounds(new DMesh3[] { MeshA, MeshB });
            double cellSize = (CellSize > 0) ? CellSize : bounds.DiagonalLength / GridResolution;
            if (!(cellSize > 0) || WindingNumberOctree.RequiredDepth(bounds, cellSize) > WindingNumberOctree.MaxSupportedDepth)
                return false;

            timer.Restart();
            // an empty input has winding number 0 everywhere: A u 0 = A - 0 = A, 0 u B = B, the rest is empty
            DMesh3[] inputs = new DMesh3[] { MeshA, MeshB };
            Func<double[], double> combine = CombineF(Operation);
            bool emptyA = MeshA.TriangleCount == 0, emptyB = MeshB.TriangleCount == 0;
            if (emptyA || emptyB) {
                combine = null;
                if (emptyB && Operation != Operations.Intersection)
                    inputs = new DMesh3[] { MeshA };
                else if (emptyA && Operation == Operations.Union)
                    inputs = new DMesh3[] { MeshB };
                else {
                    Result = new DMesh3();
                    UsedCellSize = cellSize;
                    return true;
                }
            }
            WindingContouring wc = new WindingContouring(inputs, combine, FastWindingBeta) {
                CancelF = CancelF, LocalRefinement = LocalRefinement
            };
            DMesh3 contour = wc.ContourRefined(ref cellSize, MaxRefineLevels, MinRefineEdges);
            WindingEvaluations = wc.WindingEvaluations;
            NonManifoldTriangles = wc.NonManifoldTriangles;
            HiddenGapEdges = wc.HiddenGapEdges;
            HiddenWallEdges = wc.HiddenWallEdges;
            HiddenSamples = wc.HiddenSamples;
            GapEdgesPerLevel = new List<int>(wc.GapEdgesPerLevel);
            RefinedCells = wc.RefinedCells; RefineFactor = wc.RefineFactor;
            foreach (var kv in wc.PhaseMilliseconds) PhaseMilliseconds["contour." + kv.Key] = kv.Value;
            UsedCellSize = cellSize;
            end_phase("contour");
            if (CancelF() || contour == null)
                return false;

            Result = WindingContouring.PostProcess(contour, CleanupResult, ReduceTolerance, cellSize, CancelF, end_phase);
            return true;
        }


        /// <summary> the field combination for an operation, on the winding numbers (wA, wB) </summary>
        public static Func<double[], double> CombineF(Operations op)
        {
            switch (op) {
                case Operations.Union: return (w) => Math.Max(w[0], w[1]);
                case Operations.Intersection: return (w) => Math.Min(w[0], w[1]);
                default: return (w) => Math.Min(w[0], 1 - w[1]);
            }
        }


        Stopwatch timer = new Stopwatch();
        void end_phase(string name)
        {
            PhaseMilliseconds[name] = timer.ElapsedMilliseconds;
            timer.Restart();
        }
    }
}
