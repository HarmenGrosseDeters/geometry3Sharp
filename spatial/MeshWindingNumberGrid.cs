// Copyright (c) Ryan Schmidt (rms@gradientspace.com) - All Rights Reserved
// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using System.Threading;
using g3;

namespace gs
{

    /// <summary>
    /// Sample mesh winding number (MWN) on a discrete grid. Can sample full grid, or
    /// compute MWN values along a specific iso-contour and then fill in rest of grid
    /// by propagating the computed values along x-rows (this is the default, ComputeModes.NarrowBand).
    /// Values are winding numbers, not distances: about 1 inside and 0 outside a closed mesh with
    /// outward-facing normals, and fractional near holes, so this is useful for open / broken meshes.
    /// Extract a surface with <see cref="MarchingCubes"/> at IsoValue = WindingIsoValue (0.5).
    /// Usage: <c>var g = new MeshWindingNumberGrid(mesh, new DMeshAABBTree3(mesh, true), cellSize); g.Compute();</c>
    /// The spatial tree must be built. Sample (i,j,k) is located at GridOriginD + (float)CellSize*(i,j,k) (the spacing of the inner MeshSignedDistanceGrid).
    /// 
    /// What NarrowBand computes and guarantees (WindingIsoValue = iso, 0 &lt; iso, a node is computed when it has a winding number, otherwise filled):
    /// the winding number is computed at the nodes closer than one cell to the mesh, then a front grows from them: of the 26 neighbours of a front
    /// node, all are computed, and a neighbour joins the front when the pair is a crossing, ie not strictly on one side of iso (so a node exactly
    /// at iso is a crossing with a neighbour on either side, and, for an iso that is not an integer, with another node exactly at iso: a plateau of
    /// exact values, as on the plane of the opening of a symmetric open box, is followed to its end; for an integer iso the field is exactly iso over
    /// whole regions (0 outside and 1 inside a closed mesh), which are not followed). Every other node is filled along its x row with the last
    /// computed value, or 0 if that is below iso. So every cell that contains a front node has all its corners computed, and every pair of
    /// 26-neighbours that is a crossing and is connected to the first band by crossings is computed. Against ComputeModes.FullGrid this gives, for
    /// every node, the same side of iso (below, exactly at, above) and, where a node is computed, the same bits; along an x row, both nodes of any
    /// consecutive pair that is not strictly on one side of iso are computed. It does not give: the value of a filled node (0 or the last computed
    /// value: only its side of iso is meaningful), a contour that is not connected by crossings to the band (a closed component of the iso surface
    /// far from the mesh that no front node touches is missed, as in the upstream algorithm), or an iso that is not positive (outside is filled with 0).
    /// The number of winding evaluations in WindingEvaluations can differ by a few between runs (two threads may evaluate one neighbour); the grid and
    /// the set of computed nodes do not depend on the schedule.
    /// 
    /// TODO: 
    ///   - I think we are over-exploring the grid most of the time. eg along an x-ray that
    ///     intersects the surface, we only need at most 2 cells, but we are computing at least 3,
    ///     and possibly 5. 
    ///   - it may be better to use something like bloomenthal polygonizer continuation? where we 
    ///     are keeping track of active edges instead of active cells?
    /// 
    /// </summary>
    public class MeshWindingNumberGrid
    {
        /// <summary> Input mesh. It is not modified. </summary>
        public DMesh3 Mesh;
        /// <summary> Built AABB tree for Mesh, used to evaluate winding numbers (required) </summary>
        public DMeshAABBTree3 MeshSpatial;

        /// <summary> Size of cubes in the grid, in mesh units </summary>
        public double CellSize;

        /// <summary> How many cells around border should we keep </summary>
        public int BufferCells = 1;

        /// <summary>
        /// Should we compute MWN at all grid cells (expensive!!) or only in narrow band.
        /// In narrow-band mode, we guess rest of MWN values by propagating along x-rows
        /// </summary>
        public enum ComputeModes
        {
            FullGrid = 0,
            NarrowBand = 1
        }
        public ComputeModes ComputeMode = ComputeModes.NarrowBand;

        /// <summary>
        /// In narrow-band mode, if mesh is not closed, we will explore space around
        /// this MWN iso-value (a positive one: outside is filled with 0). Nodes exactly at it count as crossings, see the class comment.
        /// </summary>
        public float WindingIsoValue = 0.5f;

        /// <summary>
        /// In NarrowBand mode, we compute an (unsigned) mesh distance grid, if true then it can be accessed
        /// via SDFGrid property after Compute()
        /// </summary>
        public bool WantMeshSDFGrid = true;

        /// <summary> if this function returns true, we should abort calculation </summary>
        public Func<bool> CancelF = () => { return false; };

        public bool DebugPrint = false;

        /// <summary>
        /// Use the fast (dipole-approximation) winding number of Barill et al. 2018 instead of the
        /// exact ray-plus-boundary winding number (which reuses one +x BVH line traversal per grid row
        /// on plain <see cref="DMeshAABBTree3"/> instances and is faster than FWN on closed meshes,
        /// while FWN stays faster on meshes with very large open boundaries or disconnected soups).
        /// </summary>
        public bool UseFastWindingNumber = false;

        /// <summary> number of winding number evaluations in the last Compute() </summary>
        public int WindingEvaluations { get { return winding_evaluations; } }
        int winding_evaluations = 0;

        double winding_number(Vector3d p)
        {
            System.Threading.Interlocked.Increment(ref winding_evaluations);
            return UseFastWindingNumber ? MeshSpatial.FastWindingNumber(p) : MeshSpatial.WindingNumber(p);
        }

        // computed results
        Vector3d grid_origin;
        DenseGrid3f winding_grid;

        // sdf grid we compute in narrow-band mode
        MeshSignedDistanceGrid mesh_sdf;


        /// <summary>
        /// Set up the computation. spatial must be a built tree for mesh. Nothing is computed until Compute() is called.
        /// </summary>
        public MeshWindingNumberGrid(DMesh3 mesh, DMeshAABBTree3 spatial, double cellSize)
        {
            Mesh = mesh;
            MeshSpatial = spatial;
            CellSize = cellSize;
        }


        /// <summary>
        /// Compute the grid. After this, Grid, GridOrigin, Dimensions and the indexer are available.
        /// Returns early (with a partial grid) if CancelF returns true.
        /// </summary>
        public void Compute()
        {
            if (Mesh.VertexCount == 0)      // the grid is sized from the bounds, which are Empty
                throw new ArgumentException("MeshWindingNumberGrid.Compute: mesh is empty");
            winding_evaluations = 0;
            // figure out origin & dimensions
            AxisAlignedBox3d bounds = Mesh.CachedBounds;

            float fBufferWidth = 2 * BufferCells * (float)CellSize;
            // (the origin and the sample positions are doubles: upstream computed them in float, up to 0.25 off at UTM
            // coordinates; GridOrigin is the origin rounded to float, GridOriginD the exact one)
            Vector3d origin = bounds.Min - (double)fBufferWidth * Vector3d.One;
            Vector3i dims = SamplingGridSize.Dimensions(origin, bounds.Max + (double)fBufferWidth * Vector3d.One, (float)CellSize);
            if (dims.x < 0)
                throw new ArgumentException("MeshWindingNumberGrid.Compute: the cell size and the mesh bounds must be finite and give a grid that fits in an array");
            int ni = dims.x, nj = dims.y, nk = dims.z;
            grid_origin = origin;

            winding_grid = new DenseGrid3f();
            if ( ComputeMode == ComputeModes.FullGrid )
                make_grid_dense(grid_origin, (float)CellSize, ni, nj, nk, winding_grid);
            else
                make_grid(grid_origin, (float)CellSize, ni, nj, nk, winding_grid);
        }



        /// <summary> Number of samples (ni, nj, nk) along each axis, available after Compute() </summary>
        public Vector3i Dimensions {
            get { return new Vector3i(winding_grid.ni, winding_grid.nj, winding_grid.nk); }
        }

        /// <summary>
        /// winding-number grid available after calling Compute()
        /// </summary>
        public DenseGrid3f Grid {
            get { return winding_grid; }
        }

        /// <summary>
        /// Origin of the winding-number grid, in same coordinates as mesh
        /// </summary>
        public Vector3f GridOrigin {
            get { return (Vector3f)grid_origin; }
        }

        /// <summary>
        /// Origin of the grid (position of sample [0,0,0]) in double precision: sample (i,j,k) is at
        /// GridOriginD + CellSize*(i,j,k). GridOrigin is this rounded to float (up to 0.25 off at UTM coordinates).
        /// </summary>
        public Vector3d GridOriginD {
            get { return grid_origin; }
        }


        /// <summary>
        /// If ComputeMode==NarrowBand, then we internally compute an unsigned distance grid,
        /// which is kept here if WantMeshSDFGrid is true (otherwise null)
        /// </summary>
        public MeshSignedDistanceGrid SDFGrid {
            get { return mesh_sdf; }
        }


        /// <summary> Winding number at sample (i,j,k) </summary>
        public float this[int i, int j, int k] {
            get { return winding_grid[i, j, k]; }
        }

        /// <summary> Position of sample (i,j,k), ie GridOriginD + (float)CellSize*(i,j,k), rounded to float (a grid point, not a cell center) </summary>
        public Vector3f CellCenter(int i, int j, int k)
        {
            double dx = (float)CellSize;
            return (Vector3f)new Vector3d(i * dx + grid_origin.x, j * dx + grid_origin.y, k * dx + grid_origin.z);
        }




        void make_grid(Vector3d origin, float dx,
                             int ni, int nj, int nk,
                             DenseGrid3f winding)
        {
            winding.resize(ni, nj, nk);
            winding.assign(float.MaxValue); // sentinel

            // seed MWN cache
            winding_number(Vector3d.Zero);

            if (DebugPrint) System.Console.WriteLine("start");

            // Ok, because the whole idea is that the surface might have holes, we are going to 
            // compute MWN along known triangles and then propagate the computed region outwards
            // until any MWN iso-sign-change is surrounded.
            // To seed propagation, we compute unsigned SDF and then compute MWN for any voxels
            // containing surface (ie w/ distance smaller than cellsize)

            // compute unsigned SDF. Its band width sets its grid padding, so it must match
            // BufferCells for the SDF grid to line up with ours (ni,nj,nk / origin)
            MeshSignedDistanceGrid sdf = new MeshSignedDistanceGrid(Mesh, CellSize) { ComputeSigns = false, ExactBandWidth = BufferCells };
            sdf.CancelF = this.CancelF;
            sdf.Compute();
            if (CancelF())
                return;

            DenseGrid3f distances = sdf.Grid;
            if (WantMeshSDFGrid)
                mesh_sdf = sdf;
            if (DebugPrint) System.Console.WriteLine("done initial sdf");

            ExactWindingCache rowCache = get_row_winding_cache();

            // compute MWN at surface voxels
            double ox = (double)origin[0], oy = (double)origin[1], oz = (double)origin[2];
            if (rowCache != null) {
                int totalRows = nj * nk;
                if (totalRows > 0 && ni > 0) {
                    gParallel.BlockStartEnd(0, totalRows - 1, (start, end) => {
                        var hits = new List<ExactWindingCache.LineHit>(16);
                        var rowLeaves = new List<int>(16);
                        var xBuf = new int[ni];
                        var valBuf = new float[ni];
                        int localCount = 0;
                        for (int rowIdx = start; rowIdx <= end; ++rowIdx) {
                            if (CancelF())
                                break;
                            int j = rowIdx % nj;
                            int k = rowIdx / nj;
                            int xCount = 0;
                            for (int i = 0; i < ni; ++i) {
                                if (distances[i, j, k] < CellSize)
                                    xBuf[xCount++] = i;
                            }
                            if (xCount == 0)
                                continue;
                            double y = (double)j * dx + oy;
                            double z = (double)k * dx + oz;
                            rowCache.EvaluateSparseRowX(ox, dx, xBuf, xCount, y, z, hits, rowLeaves, valBuf);
                            for (int m = 0; m < xCount; ++m)
                                winding[xBuf[m], j, k] = valBuf[m];
                            localCount += xCount;
                        }
                        if (localCount > 0)
                            Interlocked.Add(ref winding_evaluations, localCount);
                    }, 32);
                }
            } else {
                gParallel.ForEach(gIndices.Grid3IndicesYZ(nj, nk), (jk) => {
                    if (CancelF())
                        return;
                    for (int i = 0; i < ni; ++i) {
                        Vector3i ijk = new Vector3i(i, jk.y, jk.z);
                        float dist = distances[ijk];
                        // this could be tighter? but I don't think it matters...
                        if (dist < CellSize) {
                            Vector3d gx = new Vector3d((double)ijk.x * dx + origin[0], (double)ijk.y * dx + origin[1], (double)ijk.z * dx + origin[2]);
                            winding[ijk] = (float)winding_number(gx);
                        }
                    }
                });
            }
            if (CancelF())
                return;

            if (DebugPrint) System.Console.WriteLine("done narrow-band");

            // Now propagate outwards from computed voxels.
            // Current procedure is to check 26-neighbours around each 'front' voxel,
            // and if there are any MWN sign changes, that neighbour is added to front.
            // Front is initialized w/ all voxels we computed above

            AxisAlignedBox3i bounds = winding.Bounds;
            bounds.Max -= Vector3i.One;

            // since we will be computing new MWN values as necessary, we cannot use
            // winding grid to track whether a voxel is 'new' or not. 
            // So, using 3D bitmap intead - is updated at end of each pass.
            Bitmap3 bits = new Bitmap3(new Vector3i(ni, nj, nk));
            List<Vector3i> cur_front = new List<Vector3i>();
            foreach (Vector3i ijk in winding.Indices()) {
                if (winding[ijk] != float.MaxValue) {
                    cur_front.Add(ijk);
                    bits[ijk] = true;
                }
            }
            if (CancelF())
                return;

            // Two nodes both exactly at the iso value are followed (a plateau of exact values), except for an integer iso
            // value: the winding number of a closed mesh is exactly 0 outside and 1 inside over whole regions, and following
            // those would compute the whole grid (a fractional value is exact only on surfaces: it is harmonic away from the
            // mesh, so constant over a volume only if constant everywhere).
            bool follow_plateaus = WindingIsoValue != Math.Floor(WindingIsoValue);

            if (rowCache != null) {
                // Row-batched BFS: group newly discovered 26-neighbors by (j, k) row so each touched row
                // runs a single +x BVH line traversal and a single (y, z) boundary-row preparation.
                int totalRows = nj * nk;
                Bitmap3 scheduled = new Bitmap3(new Vector3i(ni, nj, nk));
                for (int f = 0; f < cur_front.Count; ++f)
                    scheduled[cur_front[f]] = true;

                int[] rowHead = new int[totalRows];
                for (int r = 0; r < totalRows; ++r)
                    rowHead[r] = -1;
                var activeRows = new List<int>(Math.Min(totalRows, 1024));
                var pendingX = new List<int>(cur_front.Count * 2);
                var pendingNext = new List<int>(cur_front.Count * 2);
                var next_front = new List<Vector3i>();

                while (cur_front.Count > 0) {
                    if (CancelF())
                        return;

                    activeRows.Clear();
                    pendingX.Clear();
                    pendingNext.Clear();
                    for (int f = 0; f < cur_front.Count; ++f) {
                        if ((f & 1023) == 0 && CancelF())
                            return;
                        Vector3i ijk = cur_front[f];
                        for (int n = 0; n < 26; ++n) {
                            Vector3i nijk = ijk + gIndices.GridOffsets26[n];
                            if (nijk.x < 0 || nijk.x >= ni || nijk.y < 0 || nijk.y >= nj || nijk.z < 0 || nijk.z >= nk)
                                continue;
                            if (!scheduled[nijk]) {
                                scheduled[nijk] = true;
                                int rowIdx = nijk.y + nj * nijk.z;
                                int head = rowHead[rowIdx];
                                if (head < 0)
                                    activeRows.Add(rowIdx);
                                int node = pendingX.Count;
                                pendingX.Add(nijk.x);
                                pendingNext.Add(head);
                                rowHead[rowIdx] = node;
                            }
                        }
                    }

                    int nActiveRows = activeRows.Count;
                    if (nActiveRows > 0) {
                        gParallel.BlockStartEnd(0, nActiveRows - 1, (start, end) => {
                            var hits = new List<ExactWindingCache.LineHit>(16);
                            var rowLeaves = new List<int>(16);
                            var xBuf = new int[ni];
                            var valBuf = new float[ni];
                            int localCount = 0;
                            for (int r = start; r <= end; ++r) {
                                if (CancelF())
                                    break;
                                int rowIdx = activeRows[r];
                                int xCount = 0;
                                for (int node = rowHead[rowIdx]; node >= 0; node = pendingNext[node])
                                    xBuf[xCount++] = pendingX[node];
                                int j = rowIdx % nj;
                                int k = rowIdx / nj;
                                double y = (double)j * dx + oy;
                                double z = (double)k * dx + oz;
                                rowCache.EvaluateSparseRowX(ox, dx, xBuf, xCount, y, z, hits, rowLeaves, valBuf);
                                for (int m = 0; m < xCount; ++m)
                                    winding[xBuf[m], j, k] = valBuf[m];
                                localCount += xCount;
                            }
                            if (localCount > 0)
                                Interlocked.Add(ref winding_evaluations, localCount);
                        }, 16);
                        for (int r = 0; r < nActiveRows; ++r)
                            rowHead[activeRows[r]] = -1;
                        if (CancelF())
                            return;
                    }

                    next_front.Clear();
                    for (int f = 0; f < cur_front.Count; ++f) {
                        if ((f & 1023) == 0 && CancelF())
                            return;
                        Vector3i ijk = cur_front[f];
                        float val = winding[ijk];
                        for (int n = 0; n < 26; ++n) {
                            Vector3i nijk = ijk + gIndices.GridOffsets26[n];
                            if (nijk.x < 0 || nijk.x >= ni || nijk.y < 0 || nijk.y >= nj || nijk.z < 0 || nijk.z >= nk)
                                continue;
                            if (!bits[nijk]) {
                                float val2 = winding[nijk];
                                bool crossing = (val <= WindingIsoValue && val2 >= WindingIsoValue) ||
                                                (val >= WindingIsoValue && val2 <= WindingIsoValue);
                                if (crossing && !follow_plateaus && val == val2)
                                    crossing = false;
                                if (crossing) {
                                    bits[nijk] = true;
                                    next_front.Add(nijk);
                                }
                            }
                        }
                    }
                    if (DebugPrint) System.Console.WriteLine("front has {0} voxels", next_front.Count);
                    if (next_front.Count == 0)
                        break;

                    List<Vector3i> swap = cur_front;
                    cur_front = next_front;
                    next_front = swap;
                }
            } else {
                // Unique set of 'new' voxels to compute in next iteration.
                HashSet<Vector3i> queue = new HashSet<Vector3i>();
                SpinLock queue_lock = new SpinLock();

                while (true) {
                    if (CancelF())
                        return;

                    // can process front voxels in parallel
                    bool abort = false;  int iter_count = 0;
                    gParallel.ForEach(cur_front, (ijk) => {
                        Interlocked.Increment(ref iter_count);
                        if (iter_count % 100 == 0)
                            abort = CancelF();
                        if (abort)
                            return;

                        float val = winding[ijk];

                        // check 26-neighbours to see if we have a crossing in any direction
                        for (int k = 0; k < 26; ++k) {
                            Vector3i nijk = ijk + gIndices.GridOffsets26[k];
                            if (bounds.Contains(nijk) == false)
                                continue;
                            float val2 = winding[nijk];
                            if (val2 == float.MaxValue) {
                                Vector3d gx = new Vector3d((double)nijk.x * dx + origin[0], (double)nijk.y * dx + origin[1], (double)nijk.z * dx + origin[2]);
                                val2 = (float)winding_number(gx);
                                winding[nijk] = val2;
                            }
                            if (bits[nijk] == false) {
                                // this is a 'new' voxel this round.
                                // If we have a MWN-iso-crossing, add it to the front next round. A crossing is every pair that is
                                // not strictly on one side of the iso value: a node exactly at the iso value is a crossing with a
                                // neighbour on either side and, unless the iso value is an integer (follow_plateaus), with another
                                // node exactly at it, so a plateau of exact values (the plane of a hole in a symmetric mesh) is
                                // followed to its end. (Upstream tested strictly: the front stopped at such a node although the
                                // contour went on, and fill_spans filled the rows beyond it with values on the wrong side.)
                                // A NaN is no crossing.
                                bool crossing = (val <= WindingIsoValue && val2 >= WindingIsoValue) ||
                                                (val >= WindingIsoValue && val2 <= WindingIsoValue);
                                if (crossing && !follow_plateaus && val == val2)
                                    crossing = false;
                                if (crossing) {
                                    bool taken = false;
                                    queue_lock.Enter(ref taken);
                                    queue.Add(nijk);
                                    queue_lock.Exit();
                                }
                            }
                        }
                    });
                    if (DebugPrint) System.Console.WriteLine("front has {0} voxels", queue.Count);
                    if (queue.Count == 0)
                        break;

                    // update known-voxels list and create front for next iteration
                    foreach (Vector3i idx in queue)
                        bits[idx] = true;
                    cur_front.Clear();
                    cur_front.AddRange(queue);
                    queue.Clear();
                }
            }
            if (DebugPrint) System.Console.WriteLine("done front-prop");

            if (DebugPrint) {
                int filled = 0;
                foreach (Vector3i ijk in winding.Indices()) {
                    if (winding[ijk] != float.MaxValue)
                        filled++;
                }
                System.Console.WriteLine("filled: {0} / {1}  -  {2}%", filled, ni * nj * nk,
                                    (double)filled / (double)(ni * nj * nk) * 100.0);
            }

            if (CancelF())
                return;

            // fill in the rest of the grid by propagating know MWN values
            fill_spans(ni, nj, nk, winding);

            if (DebugPrint) System.Console.WriteLine("done sweep");


        }

        // Row-ray reuse is available whenever exact WindingNumber is selected on a plain DMeshAABBTree3
        // (subclasses that override WindingNumber keep going through the virtual call).
        ExactWindingCache get_row_winding_cache()
        {
            if (UseFastWindingNumber || MeshSpatial == null || MeshSpatial.GetType() != typeof(DMeshAABBTree3) || MeshSpatial.ForceLegacyWindingCache)
                return null;
            return MeshSpatial.GetExactWindingCache();
        }

        void make_grid_dense(Vector3d origin, float dx,
                             int ni, int nj, int nk,
                             DenseGrid3f winding)
        {
            winding.resize(ni, nj, nk);

            ExactWindingCache rowCache = get_row_winding_cache();
            if (rowCache != null) {
                int totalRows = nj * nk;
                if (totalRows > 0 && ni > 0) {
                    double ox = origin[0], oy = origin[1], oz = origin[2];
                    gParallel.BlockStartEnd(0, totalRows - 1, (start, end) => {
                        var hits = new List<ExactWindingCache.LineHit>(16);
                        var rowLeaves = new List<int>(16);
                        var diffBuf = new int[ni];
                        var rowBuf = new float[ni];
                        int localCount = 0;
                        for (int rowIdx = start; rowIdx <= end; ++rowIdx) {
                            if (CancelF())
                                break;
                            int j = rowIdx % nj;
                            int k = rowIdx / nj;
                            double y = (double)j * dx + oy;
                            double z = (double)k * dx + oz;
                            rowCache.EvaluateFullRowX(ox, dx, ni, y, z, hits, rowLeaves, diffBuf, rowBuf);
                            for (int i = 0; i < ni; ++i)
                                winding[i, j, k] = rowBuf[i];
                            localCount += ni;
                        }
                        if (localCount > 0)
                            Interlocked.Add(ref winding_evaluations, localCount);
                    }, 32);
                }
                return;
            }

            winding_number(Vector3d.Zero);
            bool abort = false; int count = 0;
            gParallel.ForEach(winding.Indices(), (ijk) => {
                Interlocked.Increment(ref count);
                if (count % 100 == 0)
                    abort = CancelF();
                if (abort)
                    return;

                Vector3d gx = new Vector3d((double)ijk.x * dx + origin[0], (double)ijk.y * dx + origin[1], (double)ijk.z * dx + origin[2]);
                winding[ijk] = (float)winding_number(gx);
            });

        }   // end make_level_set_3





        void fill_spans(int ni, int nj, int nk, DenseGrid3f winding)
        {
            gParallel.ForEach(gIndices.Grid3IndicesYZ(nj, nk), (idx) => {
                int j = idx.y, k = idx.z;
                float last = winding[0, j, k];
                if (last == float.MaxValue)
                    last = 0;
                for (int i = 0; i < ni; ++i) {
                    if (winding[i, j, k] == float.MaxValue) {
                        winding[i, j, k] = last;
                    } else {
                        last = winding[i, j, k];
                        if (last < WindingIsoValue)   // propagate zeros on outside
                            last = 0;
                    }
                }
            });
        }




        
        




    }
}
