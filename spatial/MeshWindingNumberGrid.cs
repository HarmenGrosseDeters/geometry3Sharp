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
    /// The spatial tree must be built. Sample (i,j,k) is located at GridOrigin + CellSize*(i,j,k).
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
        /// this MWN iso-value
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
        /// hierarchical exact one. The exact one is fast for closed subtrees but degrades towards O(N)
        /// per query on broken meshes / triangle soups; the fast one stays fast and is accurate to
        /// a small fraction near the 0.5 iso-value.
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
        Vector3f grid_origin;
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
            winding_evaluations = 0;
            // figure out origin & dimensions
            AxisAlignedBox3d bounds = Mesh.CachedBounds;

            float fBufferWidth = 2 * BufferCells * (float)CellSize;
            grid_origin = (Vector3f)bounds.Min - fBufferWidth * Vector3f.One;
            Vector3f max = (Vector3f)bounds.Max + fBufferWidth * Vector3f.One;
            int ni = (int)((max.x - grid_origin.x) / (float)CellSize) + 1;
            int nj = (int)((max.y - grid_origin.y) / (float)CellSize) + 1;
            int nk = (int)((max.z - grid_origin.z) / (float)CellSize) + 1;

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

        /// <summary> Position of sample (i,j,k), ie GridOrigin + CellSize*(i,j,k) (a grid point, not a cell center) </summary>
        public Vector3f CellCenter(int i, int j, int k)
        {
            return new Vector3f((float)i * CellSize + grid_origin.x,
                                (float)j * CellSize + grid_origin.y,
                                (float)k * CellSize + grid_origin.z);
        }




        void make_grid(Vector3f origin, float dx,
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

            // compute MWN at surface voxels
            double ox = (double)origin[0], oy = (double)origin[1], oz = (double)origin[2];
            gParallel.ForEach(gIndices.Grid3IndicesYZ(nj, nk), (jk) => {
                if (CancelF())
                    return;
                for (int i = 0; i < ni; ++i) {
                    Vector3i ijk = new Vector3i(i, jk.y, jk.z);
                    float dist = distances[ijk];
                    // this could be tighter? but I don't think it matters...
                    if (dist < CellSize) {
                        Vector3d gx = new Vector3d((float)ijk.x * dx + origin[0], (float)ijk.y * dx + origin[1], (float)ijk.z * dx + origin[2]);
                        winding[ijk] = (float)winding_number(gx);
                    }
                }
            });
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
                            Vector3d gx = new Vector3d((float)nijk.x * dx + origin[0], (float)nijk.y * dx + origin[1], (float)nijk.z * dx + origin[2]);
                            val2 = (float)winding_number(gx);
                            winding[nijk] = val2;
                        }
                        if (bits[nijk] == false) {
                            // this is a 'new' voxel this round.
                            // If we have a MWN-iso-crossing, add it to the front next round
                            bool crossing = (val < WindingIsoValue && val2 > WindingIsoValue) ||
                                            (val > WindingIsoValue && val2 < WindingIsoValue);
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










        void make_grid_dense(Vector3f origin, float dx,
                             int ni, int nj, int nk,
                             DenseGrid3f winding)
        {
            winding.resize(ni, nj, nk);

            winding_number(Vector3d.Zero);
            bool abort = false; int count = 0;
            gParallel.ForEach(winding.Indices(), (ijk) => {
                Interlocked.Increment(ref count);
                if (count % 100 == 0)
                    abort = CancelF();
                if (abort)
                    return;

                Vector3d gx = new Vector3d((float)ijk.x * dx + origin[0], (float)ijk.y * dx + origin[1], (float)ijk.z * dx + origin[2]);
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
