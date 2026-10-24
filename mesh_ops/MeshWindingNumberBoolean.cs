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

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        public int NonManifoldTriangles;
        public int WindingEvaluations;
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();


        public MeshWindingNumberBoolean(DMesh3 a, DMesh3 b, Operations op)
        {
            MeshA = a; MeshB = b; Operation = op;
        }


        public bool Compute()
        {
            if (MeshA.TriangleCount == 0 && MeshB.TriangleCount == 0)
                return false;
            AxisAlignedBox3d bounds = MeshA.CachedBounds;
            bounds.Contain(MeshB.CachedBounds);
            double cellSize = (CellSize > 0) ? CellSize : bounds.DiagonalLength / GridResolution;
            if (cellSize <= 0)
                return false;

            PhaseMilliseconds.Clear();
            timer.Restart();
            WindingContouring wc = new WindingContouring(new DMesh3[] { MeshA, MeshB }, CombineF(Operation), FastWindingBeta) {
                CancelF = CancelF
            };
            DMesh3 contour = wc.ContourAdaptive(cellSize);
            NonManifoldTriangles = wc.NonManifoldTriangles;
            WindingEvaluations = wc.WindingEvaluations;
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
