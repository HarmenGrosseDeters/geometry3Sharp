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
    /// </summary>
    public class MeshWindingNumberSolidify
    {
        public DMesh3 Mesh;

        public enum Methods { DualContouring = 0, MarchingCubes = 1 }
        public Methods Method = Methods.DualContouring;

        /// <summary> grid cell size. If zero, uses bounding box diagonal / GridResolution </summary>
        public double CellSize = 0;
        public int GridResolution = 128;

        public double WindingIso = 0.5;

        /// <summary>
        /// Use an adaptive octree (WindingNumberOctree) to evaluate the winding number only near the
        /// surface instead of a full grid. Same result, much less work at fine resolutions.
        /// Dual contouring only (ignored for marching cubes).
        /// </summary>
        public bool Adaptive = false;

        /// <summary> accuracy of the fast winding number (DMeshAABBTree3.FWNBeta); larger is more exact and slower </summary>
        public double FastWindingBeta = 1.5;

        /// <summary> max number of input surfaces considered along one grid edge </summary>
        public int MaxEdgeHits = 8;

        /// <summary> run MeshPostRepairCleanup on the result (removes marching cubes slivers etc) </summary>
        public bool CleanupResult = true;

        /// <summary>
        /// If > 0, simplify the result with a quadric-error Reducer, collapsing edges as long as the error
        /// stays below about (ReduceTolerance * CellSize) distance. Flat regions collapse to few triangles.
        /// </summary>
        public double ReduceTolerance = 0.05;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        public DMesh3 Result;
        public int NonManifoldTriangles;
        /// <summary> number of winding number evaluations </summary>
        public int WindingEvaluations;
        /// <summary> milliseconds per phase of the last Compute(), eg "winding", "contour", "cleanup", "reduce" </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        DMesh3 input;
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

        public bool Compute()
        {
            double cellSize = (CellSize > 0) ? CellSize : Mesh.CachedBounds.DiagonalLength / GridResolution;
            if (cellSize <= 0 || Mesh.TriangleCount == 0)
                return false;

            // The winding and distance grids put their origin at the bounding box min, so axis-aligned faces
            // on the bounding box (and faces at round coordinates, common in CAD data) land exactly on grid
            // nodes, where inside/outside is ambiguous. An unreferenced vertex slightly below the min shifts
            // the grid by an odd fraction of a cell; it does not change winding numbers or distances.
            input = new DMesh3(Mesh);
            // an inside-out input has winding number -1 inside, so nothing would be above WindingIso
            if (MeshMeasurements.VolumeArea(input, input.TriangleIndices(), input.GetVertex).x < 0)
                input.ReverseOrientation(false);
            input.AppendVertex(input.CachedBounds.Min - cellSize * new Vector3d(0.1371, 0.2713, 0.3891));

            DMeshAABBTree3 spatial = new DMeshAABBTree3(input, true);
            spatial.FWNBeta = FastWindingBeta;
            spatial.FastWindingNumber(Vector3d.Zero);   // build the winding number cache once, before parallel use

            PhaseMilliseconds.Clear();
            phase_timer.Restart();
            if (Adaptive && Method == Methods.DualContouring) {
                if (compute_adaptive(spatial, cellSize) == false)
                    return false;
            } else {
                if (compute_grid(spatial, cellSize) == false)
                    return false;
            }
            if (CancelF() || Result == null)
                return false;

            if (CleanupResult) {
                MeshPostRepairCleanup cleanup = new MeshPostRepairCleanup(Result);
                cleanup.Apply();
                end_phase("cleanup");
            }
            if (ReduceTolerance > 0 && CancelF() == false) {
                // unweighted plane quadrics: the error is a sum of squared distances to the original planes,
                // so corners and edges only move if they stay within the tolerance of every adjacent plane
                Reducer reducer = new Reducer(Result) { AreaWeightedQuadrics = false };
                double tol = ReduceTolerance * cellSize;
                reducer.ReduceToMaxQuadricError(tol * tol);
                end_phase("reduce");
            }
            Result = new DMesh3(Result, true);
            return true;
        }


        // Hermite data from the input mesh: preferably where the grid edge hits the input (exact point on
        // the edge), otherwise the nearest input point to the interpolated crossing. In both cases only
        // surfaces where the winding number actually changes count, so interior shells are ignored.
        bool compute_grid(DMeshAABBTree3 spatial, double cellSize)
        {
            MeshWindingNumberGrid mwn = new MeshWindingNumberGrid(input, spatial, cellSize);
            mwn.WindingIsoValue = (float)WindingIso;
            mwn.UseFastWindingNumber = true;
            mwn.CancelF = CancelF;
            mwn.Compute();
            if (CancelF())
                return false;
            end_phase("winding");

            DenseGridTrilinearImplicit iso = new DenseGridTrilinearImplicit(mwn.Grid, mwn.GridOrigin, mwn.CellSize);
            iso.Outside = 0;    // default is a large value (meant for distance fields), but winding number 0 = outside
            AxisAlignedBox3d bounds = iso.Bounds();
            bounds.Expand(3 * cellSize);

            WindingEvaluations = mwn.WindingEvaluations;

            if (Method == Methods.DualContouring) {
                DualContouring dc = new DualContouring() {
                    Implicit = iso, IsoValue = WindingIso, Bounds = bounds, CellSize = cellSize, CancelF = CancelF
                };
                dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                    hermite_from_input(spatial, cellSize, a, b, p, out point, out normal);
                dc.Generate();
                Result = dc.Mesh;
                NonManifoldTriangles = dc.NonManifoldTriangles;
                if (NonManifoldTriangles > 0)
                    fill_small_holes(Result);
                end_phase("contour");
            } else {
                MarchingCubes mc = new MarchingCubes() {
                    Implicit = iso, IsoValue = WindingIso, Bounds = bounds, CubeSize = cellSize,
                    RootMode = MarchingCubes.RootfindingModes.Bisection, RootModeSteps = 5, CancelF = CancelF
                };
                mc.Generate();
                Result = mc.Mesh;
                // MarchingCubes orients for "inside = less than iso" (distance fields), winding number is the opposite
                Result.ReverseOrientation(false);
                NonManifoldTriangles = 0;
                end_phase("contour");
            }
            return true;
        }


        // winding number only near the surface (octree), then sparse dual contouring on its leaf cells
        bool compute_adaptive(DMeshAABBTree3 spatial, double cellSize)
        {
            WindingNumberOctree octree = new WindingNumberOctree(input, spatial) {
                TargetLeafCellSize = cellSize, WindingIso = WindingIso, CancelF = CancelF
            };
            octree.Build();
            if (CancelF())
                return false;
            end_phase("winding");

            DualContouring dc = new DualContouring() {
                Implicit = new FastWindingImplicit() { Spatial = spatial },
                IsoValue = WindingIso, CellSize = octree.LeafCellSize, CancelF = CancelF,
                Bounds = new AxisAlignedBox3d(octree.Origin, octree.Origin + octree.LeafCount * octree.LeafCellSize * Vector3d.One),
                CandidateCells = octree.SurfaceCells,
                NodeValueF = octree.NodeValue
            };
            dc.HermiteF = (Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal) =>
                hermite_from_input(spatial, cellSize, a, b, p, out point, out normal);
            dc.Generate();
            if (CancelF())
                return false;
            Result = dc.Mesh;
            NonManifoldTriangles = dc.NonManifoldTriangles;
            if (NonManifoldTriangles > 0)
                fill_small_holes(Result);
            end_phase("contour");
            WindingEvaluations = octree.WindingEvaluations;
            return true;
        }

        // field gradient fallback for dual contouring where no Hermite data is found
        class FastWindingImplicit : ImplicitFunction3d
        {
            public DMeshAABBTree3 Spatial;
            public double Value(ref Vector3d pt) { return Spatial.FastWindingNumber(pt); }
        }


        bool hermite_from_input(DMeshAABBTree3 spatial, double cellSize, Vector3d a, Vector3d b, Vector3d p, out Vector3d point, out Vector3d normal)
        {
            point = p; normal = Vector3d.Zero;
            double len = a.Distance(b);
            Vector3d dir = (b - a) / len;
            Vector3d origin = a - 0.01 * cellSize * dir;
            Ray3d ray = new Ray3d(origin, dir);
            double maxT = len + 0.02 * cellSize;
            // collect the input surfaces along the edge
            int nHits = 0;
            int[] hitTris = new int[MaxEdgeHits];
            double[] hitT = new double[MaxEdgeHits];
            double t0 = 0;
            while (nHits < MaxEdgeHits) {
                int hit = spatial.FindNearestHitTriangle(ray, maxT - t0);
                if (hit == DMesh3.InvalidID)
                    break;
                IntrRay3Triangle3 intr = MeshQueries.TriangleIntersection(input, hit, ray);
                if (intr.Find() == false)
                    break;
                hitTris[nHits] = hit;
                hitT[nHits] = t0 + intr.RayParameter;
                nHits++;
                double advance = intr.RayParameter + 1e-6 * cellSize;
                t0 += advance;
                ray = new Ray3d(ray.PointAt(advance), dir);
            }

            // The edge goes from inside (a) to outside (b). With a single surface on it, that surface is the
            // boundary. With several, find where the winding number crosses the iso-value between them
            // (interior shells and overlaps) and use the transition closest to the interpolated crossing.
            int best = -1;
            if (nHits == 1) {
                best = 0;
            } else if (nHits > 1) {
                bool insideBefore = true;
                double bestDist = double.MaxValue;
                for (int i = 0; i < nHits; ++i) {
                    bool insideAfter = (i == nHits - 1) ? false
                        : spatial.FastWindingNumber(origin + 0.5 * (hitT[i] + hitT[i + 1]) * dir) > WindingIso;
                    if (insideBefore != insideAfter) {
                        double d = Math.Abs(hitT[i] - (p - origin).Dot(dir));
                        if (d < bestDist) { bestDist = d; best = i; }
                    }
                    insideBefore = insideAfter;
                }
            }
            if (best >= 0) {
                Vector3d n = input.GetTriNormal(hitTris[best]);
                if (n.LengthSquared > 0.5) {
                    point = origin + hitT[best] * dir; normal = n;
                    return true;
                }
            }

            int tid = spatial.FindNearestTriangle(p, 1.5 * cellSize);
            if (tid == DMesh3.InvalidID)
                return false;
            DistPoint3Triangle3 dist = MeshQueries.TriangleDistance(input, tid, p);
            Vector3d tn = input.GetTriNormal(tid);
            if (tn.LengthSquared < 0.5 || is_boundary(spatial, dist.TriangleClosest, tn, cellSize) == false)
                return false;
            point = dist.TriangleClosest; normal = tn;
            return true;
        }

        // Dual contouring leaves out the rare quads that would make an edge non-manifold (an ambiguous face
        // where both cells have a single patch). Fill the resulting small holes; the bowties this can leave
        // are removed by the cleanup pass.
        static void fill_small_holes(DMesh3 mesh)
        {
            // the holes' borders often run through bowtie vertices, which MeshBoundaryLoops cannot resolve
            new MeshEditor(mesh).DisconnectAllBowties();
            MeshBoundaryLoops loops = null;
            try {
                loops = new MeshBoundaryLoops(mesh, true);
            } catch (Exception) {
            }
            if (loops != null) {
                foreach (EdgeLoop loop in loops) {
                    MinimalHoleFill fill = new MinimalHoleFill(mesh, loop);
                    if (fill.Apply() == false)
                        new SimpleHoleFiller(mesh, loop).Fill();
                }
            }
            fan_fill_remaining_holes(mesh);
        }

        // Fallback for holes MeshBoundaryLoops cannot extract (border edges with inconsistent direction):
        // walk the border edges into loops and fill each with a fan, orienting every fan triangle to match
        // the triangle on the other side of its border edge.
        static void fan_fill_remaining_holes(DMesh3 mesh)
        {
            HashSet<int> remaining = new HashSet<int>(MeshIterators.BoundaryEdges(mesh));
            while (remaining.Count > 0) {
                int start = remaining.First();
                List<int> loopEdges = new List<int>();
                int cur = start, vCur = mesh.GetEdgeV(start).b;
                while (true) {
                    loopEdges.Add(cur);
                    remaining.Remove(cur);
                    int next = -1;
                    foreach (int eid in mesh.VtxEdgesItr(vCur))
                        if (remaining.Contains(eid)) { next = eid; break; }
                    if (next < 0)
                        break;
                    Index2i nv = mesh.GetEdgeV(next);
                    vCur = (nv.a == vCur) ? nv.b : nv.a;
                    cur = next;
                }
                if (loopEdges.Count < 3)
                    continue;
                Vector3d center = Vector3d.Zero;
                foreach (int eid in loopEdges) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    center += 0.5 * (mesh.GetVertex(ev.a) + mesh.GetVertex(ev.b));
                }
                int c = mesh.AppendVertex(center / loopEdges.Count);
                foreach (int eid in loopEdges) {
                    Index2i ev = mesh.GetEdgeV(eid);
                    Index3i t = mesh.GetTriangle(mesh.GetEdgeT(eid).a);
                    // the existing triangle traverses the edge a->b or b->a; the new one goes the other way
                    bool aToB = IndexUtil.find_tri_ordered_edge(ev.a, ev.b, ref t) != DMesh3.InvalidID;
                    if (aToB)
                        mesh.AppendTriangle(ev.b, ev.a, c);
                    else
                        mesh.AppendTriangle(ev.a, ev.b, c);
                }
            }
        }


        bool is_boundary(DMeshAABBTree3 spatial, Vector3d q, Vector3d n, double cellSize)
        {
            double eps = 0.05 * cellSize;
            double w0 = spatial.FastWindingNumber(q + eps * n), w1 = spatial.FastWindingNumber(q - eps * n);
            return (w0 > WindingIso) != (w1 > WindingIso);
        }
    }
}
