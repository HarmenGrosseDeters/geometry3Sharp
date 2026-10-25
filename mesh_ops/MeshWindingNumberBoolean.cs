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

        /// <summary> grid cell size. If zero, uses bounding box diagonal of both inputs / GridResolution </summary>
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
        /// Compute the boolean into Result.
        /// </summary>
        /// <returns>
        /// false if both inputs are empty, the cell size is not positive or too small for the octree, or the
        /// computation was cancelled; true otherwise (Result may be an empty mesh, eg an intersection with an
        /// empty input).
        /// </returns>
        public bool Compute()
        {
            if (MeshA.TriangleCount == 0 && MeshB.TriangleCount == 0)
                return false;
            // empty inputs do not count (their bounds would drag in the origin)
            AxisAlignedBox3d bounds = WindingNumberOctree.Bounds(new DMesh3[] { MeshA, MeshB });
            double cellSize = (CellSize > 0) ? CellSize : bounds.DiagonalLength / GridResolution;
            if (!(cellSize > 0) || WindingNumberOctree.RequiredDepth(bounds, cellSize) > WindingNumberOctree.MaxSupportedDepth)
                return false;

            PhaseMilliseconds.Clear();
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
