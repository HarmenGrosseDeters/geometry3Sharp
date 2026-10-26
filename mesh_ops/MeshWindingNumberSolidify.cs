// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using g3;

namespace gs
{
    /// <summary>
    /// Rebuilds a (possibly broken) mesh as a closed solid: samples the fast mesh winding number of the
    /// input on a grid and extracts the WindingIso surface, which is closed, has no self-intersections
    /// and no interior parts. Holes in the input are spanned by the iso-surface.
    ///
    /// Methods:
    ///   - DualContouring: edge crossings are projected onto the input mesh and its triangle normals are
    ///     used as Hermite data, so flat faces and sharp edges of the input are reproduced. Far fewer
    ///     triangles than marching cubes. Can produce a few non-manifold spots, which the cleanup pass
    ///     and the result checks handle.
    ///   - MarchingCubes: robust, but rounds sharp edges and produces many triangles.
    ///
    /// Features smaller than CellSize are lost in both cases.
    ///
    /// Behaviour: the input mesh is not modified (an oriented copy is used, with inside-out parts flipped so
    /// inverted inputs still give their solid). Compute() writes a new mesh to Result; the input triangulation
    /// and attributes are not kept. Cost grows with the resolution: a full grid for Adaptive = false, about
    /// the surface area in cells for Adaptive = true (much cheaper at fine resolutions).
    ///
    /// Use this to rebuild any input (soups, open, self-intersecting) as a solid; prefer MeshAutoRepair to
    /// keep the input triangles, MeshRobustRepair to try that first and solidify only if needed, and
    /// ExactMeshBoolean.Resolve for an exact (not remeshed) resolution of self-intersections of closed meshes.
    /// </summary>
    public class MeshWindingNumberSolidify
    {
        /// <summary> input mesh (not modified) </summary>
        public DMesh3 Mesh;

        /// <summary> iso-surface extraction method, see class comment </summary>
        public enum Methods { DualContouring = 0, MarchingCubes = 1 }
        /// <summary> iso-surface extraction method; default DualContouring </summary>
        public Methods Method = Methods.DualContouring;

        /// <summary> grid cell size. If zero, uses bounding box diagonal / GridResolution </summary>
        public double CellSize = 0;
        /// <summary> number of cells along the bounding box diagonal, used if CellSize is zero </summary>
        public int GridResolution = 128;

        /// <summary> winding number iso-value of the surface: points with a winding number above it are inside </summary>
        public double WindingIso = 0.5;

        /// <summary>
        /// Use an adaptive octree (WindingNumberOctree) to evaluate the winding number only near the
        /// surface instead of a full grid. Same result, much less work at fine resolutions.
        /// Dual contouring only (ignored for marching cubes).
        /// </summary>
        public bool Adaptive = false;

        /// <summary> accuracy of the fast winding number (DMeshAABBTree3.FWNBeta); larger is more exact and slower </summary>
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

        /// <summary> max number of input surfaces considered along one grid edge </summary>
        public int MaxEdgeHits = 8;

        /// <summary> run MeshPostRepairCleanup on the result (removes marching cubes slivers etc) </summary>
        public bool CleanupResult = true;

        /// <summary>
        /// If > 0, simplify the result with a quadric-error Reducer, collapsing edges as long as the error
        /// stays below about (ReduceTolerance * CellSize) distance. Flat regions collapse to few triangles.
        /// </summary>
        public double ReduceTolerance = 0.05;

        /// <summary>
        /// If true, the contour is checked for self-intersections before the cleanup and the reduction; if it
        /// has any, those are skipped (they rarely remove self-intersections and are a large part of the cost):
        /// Compute() then returns true with the raw contour in Result and StoppedOnSelfIntersections set;
        /// FinishPostProcess() completes it. For callers that discard self-intersecting results anyway
        /// (MeshRobustRepair). The check is free where the dual contouring already verified its result.
        /// </summary>
        public bool StopIfSelfIntersecting = false;

        /// <summary> return true to cancel; Compute() then returns false </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> the solid surface computed by Compute() (a new, compacted mesh); null before </summary>
        public DMesh3 Result;
        /// <summary> dual contouring: number of triangles left out of the contour for non-manifold edges (0 for marching cubes) </summary>
        public int NonManifoldTriangles;
        /// <summary> adaptive dual contouring: refinements around polygons left out for non-manifold edges </summary>
        public int NonManifoldRefinements;
        /// <summary> number of winding number evaluations </summary>
        public int WindingEvaluations;
        /// <summary> hidden gap edges in the final result (0 = none), and the cell size it was computed with </summary>
        public int HiddenGapEdges;
        /// <summary> grid edges through material thinner than a cell in the final result (0 = none) </summary>
        public int HiddenWallEdges;
        /// <summary> hidden gap edges at each refinement level </summary>
        public List<int> GapEdgesPerLevel = new List<int>();
        /// <summary> finest cell size used (CellSize unless refined) </summary>
        public double UsedCellSize;
        /// <summary> local refinement: number of cells (of CellSize) refined, and the refinement factor (CellSize / UsedCellSize) </summary>
        public int RefinedCells, RefineFactor = 1;
        /// <summary>
        /// true if Compute() stopped before the post-processing because the contour self-intersects (see
        /// StopIfSelfIntersecting); Result is then the raw contour
        /// </summary>
        public bool StoppedOnSelfIntersections;
        /// <summary>
        /// milliseconds per phase of the last Compute(), eg "winding", "contour", "selfint" (the check for
        /// StopIfSelfIntersecting), "cleanup", "reduce"
        /// </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        Stopwatch phase_timer = new Stopwatch();
        void end_phase(string name)
        {
            PhaseMilliseconds[name] = phase_timer.ElapsedMilliseconds;
            phase_timer.Restart();
        }

        public MeshWindingNumberSolidify(DMesh3 mesh)
        {
            Mesh = mesh;
        }

        /// <summary>
        /// Compute the solid surface into Result.
        /// </summary>
        /// <returns>
        /// false if the input is empty, the cell size is not positive, the grid (or octree depth) would be too
        /// large for the cell size, or the computation was cancelled; true otherwise.
        /// </returns>
        public bool Compute()
        {
            StoppedOnSelfIntersections = false;
            double cellSize = (CellSize > 0) ? CellSize : Mesh.CachedBounds.DiagonalLength / GridResolution;
            if (!(cellSize > 0) || Mesh.TriangleCount == 0)
                return false;
            if (Adaptive && Method == Methods.DualContouring) {
                if (WindingNumberOctree.RequiredDepth(Mesh.CachedBounds, cellSize) > WindingNumberOctree.MaxSupportedDepth)
                    return false;
            } else {
                Vector3d n = Mesh.CachedBounds.Diagonal / cellSize + 16 * Vector3d.One;
                if (!(n.x * n.y * n.z <= DualContouring.MaxGridNodes))
                    return false;
            }

            PhaseMilliseconds.Clear();
            phase_timer.Restart();
            bool knownFree = false;
            DMesh3 contour;
            HiddenGapEdges = HiddenWallEdges = 0;
            if (Adaptive && Method == Methods.DualContouring) {
                WindingContouring wc = new WindingContouring(new DMesh3[] { Mesh }, null, FastWindingBeta) {
                    Iso = WindingIso, MaxEdgeHits = MaxEdgeHits, CancelF = CancelF, LocalRefinement = LocalRefinement
                };
                contour = wc.ContourRefined(ref cellSize, MaxRefineLevels, MinRefineEdges);
                WindingEvaluations = wc.WindingEvaluations;
                NonManifoldTriangles = wc.NonManifoldTriangles;
                NonManifoldRefinements = wc.NonManifoldRefinements;
                HiddenGapEdges = wc.HiddenGapEdges;
                HiddenWallEdges = wc.HiddenWallEdges;
                GapEdgesPerLevel = new List<int>(wc.GapEdgesPerLevel);
                RefinedCells = wc.RefinedCells; RefineFactor = wc.RefineFactor;
                knownFree = wc.SelfIntersectionFree;
                foreach (var kv in wc.PhaseMilliseconds) PhaseMilliseconds["contour." + kv.Key] = kv.Value;
                end_phase("contour");
            } else {
                contour = compute_grid(cellSize);
            }
            UsedCellSize = cellSize;
            if (CancelF() || contour == null)
                return false;

            if (StopIfSelfIntersecting && knownFree == false) {
                bool intersects = MeshRepairMetrics.HasSelfIntersections(contour);
                end_phase("selfint");
                if (intersects) {
                    Result = contour;
                    StoppedOnSelfIntersections = true;
                    return true;
                }
            }
            Result = WindingContouring.PostProcess(contour, CleanupResult, ReduceTolerance, cellSize, CancelF, end_phase);
            return true;
        }

        /// <summary>
        /// After Compute() stopped on self-intersections (StoppedOnSelfIntersections): runs the skipped
        /// cleanup and reduction on Result, which is then what Compute() would have given without
        /// StopIfSelfIntersecting. Does nothing otherwise.
        /// </summary>
        public void FinishPostProcess()
        {
            if (StoppedOnSelfIntersections == false || Result == null)
                return;
            phase_timer.Restart();
            Result = WindingContouring.PostProcess(Result, CleanupResult, ReduceTolerance, UsedCellSize, CancelF, end_phase);
            StoppedOnSelfIntersections = false;
        }


        // winding number on a full grid (narrow band + propagation), then dual contouring or marching cubes
        DMesh3 compute_grid(double cellSize)
        {
            // The winding and distance grids put their origin at the bounding box min, so axis-aligned faces
            // on the bounding box (and faces at round coordinates, common in CAD data) land exactly on grid
            // nodes, where inside/outside is ambiguous. An unreferenced vertex slightly below the min shifts
            // the grid by an odd fraction of a cell; it does not change winding numbers or distances.
            // The grids store float positions: computed relative to the center (UTM coordinates).
            DMesh3 shifted = new DMesh3(Mesh);
            Vector3d center = shifted.CachedBounds.Center;
            MeshTransforms.Translate(shifted, -center);
            shifted.AppendVertex(shifted.CachedBounds.Min - cellSize * new Vector3d(0.1371, 0.2713, 0.3891));
            WindingContouring wc = new WindingContouring(new DMesh3[] { shifted }, null, FastWindingBeta) {
                Iso = WindingIso, MaxEdgeHits = MaxEdgeHits, CancelF = CancelF
            };

            MeshWindingNumberGrid mwn = new MeshWindingNumberGrid(wc.Meshes[0], wc.Spatials[0], cellSize);
            mwn.WindingIsoValue = (float)WindingIso;
            mwn.UseFastWindingNumber = true;
            mwn.CancelF = CancelF;
            mwn.Compute();
            WindingEvaluations = mwn.WindingEvaluations;
            if (CancelF())
                return null;
            end_phase("winding");

            DenseGridTrilinearImplicit iso = new DenseGridTrilinearImplicit(mwn.Grid, mwn.GridOrigin, mwn.CellSize);
            iso.Outside = 0;    // default is a large value (meant for distance fields), but winding number 0 = outside
            AxisAlignedBox3d bounds = iso.Bounds();
            bounds.Expand(3 * cellSize);

            DMesh3 result;
            if (Method == Methods.DualContouring) {
                DualContouring dc = new DualContouring() {
                    Implicit = iso, IsoValue = WindingIso, Bounds = bounds, CellSize = cellSize, CancelF = CancelF
                };
                dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                    wc.Hermite(cellSize, a, b, p, out point, out normal);
                dc.Generate();
                result = dc.Mesh;
                NonManifoldTriangles = dc.NonManifoldTriangles;
                if (NonManifoldTriangles > 0)
                    WindingContouring.FillSmallHoles(result);
            } else {
                MarchingCubes mc = new MarchingCubes() {
                    Implicit = iso, IsoValue = WindingIso, Bounds = bounds, CubeSize = cellSize,
                    RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 5, CancelF = CancelF
                };
                mc.Generate();
                result = mc.Mesh;
                // MarchingCubes orients for "inside = less than iso" (distance fields), winding number is the opposite
                result.ReverseOrientation(false);
                NonManifoldTriangles = 0;
            }
            MeshTransforms.Translate(result, center);
            end_phase("contour");
            return result;
        }
    }
}
