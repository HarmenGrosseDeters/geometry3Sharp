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
    /// use a mesh-arrangement method instead.
    /// </summary>
    public class MeshWindingNumberBoolean
    {
        public enum Operations { Union = 0, Intersection = 1, Difference = 2 }

        public DMesh3 MeshA, MeshB;
        public Operations Operation;

        /// <summary> grid cell size. If zero, uses bounding box diagonal of both inputs / GridResolution </summary>
        public double CellSize = 0;
        public int GridResolution = 128;

        /// <summary> run MeshPostRepairCleanup on the result </summary>
        public bool CleanupResult = true;
        /// <summary> simplify the result as long as the error stays below about ReduceTolerance * CellSize (0 = off) </summary>
        public double ReduceTolerance = 0.05;
        /// <summary> accuracy of the fast winding number (DMeshAABBTree3.FWNBeta) </summary>
        public double FastWindingBeta = 1.5;


        /// <summary>
        /// If the result has hidden gaps (gaps narrower than a cell, see DualContouring.HiddenGapF), the cell
        /// size is halved until they are gone, plus one more level (see WindingContouring.ContourRefined); at
        /// most this many halvings. Hidden gaps make the surfaces on both sides of the gap intersect. Each level
        /// roughly quadruples the work (the octree work scales with the surface). Adaptive (octree) mode only.
        /// </summary>
        public int MaxRefineLevels = 2;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        public int NonManifoldTriangles;
        public int WindingEvaluations;
        /// <summary> hidden gap edges in the final result (0 = none), and the cell size it was computed with </summary>
        public int HiddenGapEdges;
        /// <summary> hidden gap edges at each refinement level </summary>
        public List<int> GapEdgesPerLevel = new List<int>();
        public double UsedCellSize;
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();


        public MeshWindingNumberBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }


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
                CancelF = CancelF
            };
            DMesh3 contour = wc.ContourRefined(ref cellSize, MaxRefineLevels);
            WindingEvaluations = wc.WindingEvaluations;
            NonManifoldTriangles = wc.NonManifoldTriangles;
            HiddenGapEdges = wc.HiddenGapEdges;
            GapEdgesPerLevel = new List<int>(wc.GapEdgesPerLevel);
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
