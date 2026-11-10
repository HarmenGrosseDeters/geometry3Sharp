using System;
using System.Collections.Generic;
using System.Linq;
using g3;

namespace gs
{
    /// <summary>
    /// A surface through an unoriented point cloud with more detail than the alpha wrap (docs/design/POINT_CLOUD_RECONSTRUCTION.md,
    /// step C1). The normals come from <see cref="PointCloudNormals"/> (PCA axes oriented with an alpha wrap); the surface is
    /// the 0.5 iso-surface of the fast winding number of the oriented points (Barill et al. 2018: PointAABBTree3.FastWindingNumber),
    /// each point weighted with the area it stands for (PointCloudNormals.PointArea, from the density of its neighbours), extracted
    /// by marching cubes on a grid of CubeSize.
    ///
    /// Where the cloud has no data the alpha wrap is used instead of the winding number: a grid node whose nearest point is
    /// farther than HoleFactor times the local spacing of that point (a hole, a gap in the sampling, or the empty space far from the
    /// cloud) takes the signed distance to the wrap, blended over half that distance into the winding number field. So a hole is
    /// bridged by the wrap and not by the sagging winding number field, and the winding number is evaluated only within
    /// about 1.5 * HoleFactor local spacings of the points (the narrow band). The result is therefore not guaranteed to enclose the
    /// points (the guarantee of the wrap): it is the iso-surface of a blended field.
    ///
    /// The result is cleaned of small closed components (bubbles of the winding number field of noisy data): components with less
    /// than MinComponentAreaFraction of the area of the largest are removed, and the number removed and their share of the area are
    /// reported (ComponentsRemoved, RemovedAreaFraction). Nothing is removed silently. The Status is Success only for a
    /// single closed, manifold component.
    ///
    /// Limits: the surface follows the data to about CubeSize; sharp features are rounded; the normals are only as good as
    /// PointCloudNormals (an open sheet or thin walls have no orientation, the status says so); the cost is that of the grid
    /// (about (extent / CubeSize)^3 nodes with a nearest-point query and, near the data, a winding number evaluation each; the
    /// grid is evaluated in parallel). Compute() can be called again; parallel and serial runs give the same mesh.
    /// </summary>
    public class PointCloudSurface
    {
        public enum SurfaceStatus
        {
            Success = 0,
            /// <summary> the normals stage failed or is not reliable (see NormalsStatus): no surface is built </summary>
            NormalsFailed = 1,
            /// <summary> the iso-surface is empty </summary>
            EmptyResult = 2,
            /// <summary> the result is not a single closed manifold component (see Error); Result is set for diagnostics </summary>
            NotClean = 3,
            /// <summary> the grid would have more nodes than MaxGridNodes </summary>
            GridTooLarge = 4
        }

        public IList<Vector3d> Points;

        /// <summary> grid cell size; not positive: Spacing / 2.5 </summary>
        public double CubeSize = 0;
        /// <summary>
        /// the data zone around a point is HoleFactor local spacings; beyond it (blended over the next half) the wrap is used.
        /// Measured on a torus with a removed patch (6000 points): 2.5 let the winding number sag into the hole (max deviation
        /// 0.242 against 0.141 of the wrap), 1.5 gave 0.171, 1.0 gave 0.141 but 4 % of the surface of a clean cloud then comes from
        /// the wrap and 2-3 times as many bubble components appear on noisy data
        /// </summary>
        public double HoleFactor = 1.5;
        /// <summary> components with less than this fraction of the area of the largest are removed (and reported) </summary>
        public double MinComponentAreaFraction = 0.05;
        /// <summary> stop (GridTooLarge) if the grid has more nodes </summary>
        public long MaxGridNodes = 60000000;
        /// <summary> options of the normals stage (0: chosen by it) </summary>
        public double Alpha = 0, Offset = 0;
        public bool Parallel = true;

        // results
        public SurfaceStatus Status;
        public string Error = "";
        public DMesh3 Result;
        /// <summary> the normals stage with its results (Normals, Confident, Wrap, Spacing, ...) </summary>
        public PointCloudNormals NormalsStage;
        public double Spacing, UsedCubeSize;
        public int GridNodes;
        /// <summary> nodes where the winding number was evaluated (the narrow band), of GridNodes </summary>
        public long BandNodes;
        public int ComponentsRemoved;
        /// <summary> removed area as a fraction of the area before the cleanup </summary>
        public double RemovedAreaFraction;
        /// <summary> the part of the result area that lies in the no-data zone (where the wrap decides): holes, gaps </summary>
        public double WrapAreaFraction;
        /// <summary> time of the phases in milliseconds: normals, trees, grid, cleanup </summary>
        public Dictionary<string, long> PhaseMilliseconds = new Dictionary<string, long>();

        public PointCloudSurface(IList<Vector3d> points)
        {
            Points = points;
        }

        public bool Compute()
        {
            Result = null; NormalsStage = null; Spacing = UsedCubeSize = 0; GridNodes = 0; BandNodes = 0;
            ComponentsRemoved = 0; RemovedAreaFraction = 0; WrapAreaFraction = 0; Error = "";
            PhaseMilliseconds = new Dictionary<string, long>();
            var timer = System.Diagnostics.Stopwatch.StartNew();

            NormalsStage = new PointCloudNormals(Points) { Alpha = Alpha, Offset = Offset, Parallel = Parallel };
            bool ok = NormalsStage.Compute();
            Spacing = NormalsStage.Spacing;
            PhaseMilliseconds["normals"] = timer.ElapsedMilliseconds; timer.Restart();
            if (!ok) {
                Status = SurfaceStatus.NormalsFailed;
                Error = "normals: " + NormalsStage.Status + " " + NormalsStage.Error;
                return false;
            }

            // unique points with their oriented normals and areas (exact duplicates are one point)
            int n = Points.Count;
            var seen = new Dictionary<Vector3d, int>();
            var ps = new DMesh3(true, false, false, false);
            var area = new List<double>();
            for (int i = 0; i < n; ++i) {
                Vector3d p = Points[i];
                if (seen.ContainsKey(p)) continue;
                seen[p] = i;
                ps.AppendVertex(new NewVertexInfo(p, new Vector3f(NormalsStage.Normals[i])));
                area.Add(NormalsStage.PointArea[i]);
            }
            int m = ps.VertexCount;
            double[] pointSpacing = new double[m];
            for (int i = 0; i < m; ++i) pointSpacing[i] = Math.Sqrt(area[i]);
            var pointTree = new PointAABBTree3(ps, true);
            pointTree.FWNAreaEstimateF = (vid) => area[vid];
            pointTree.FastWindingNumber(ps.GetVertex(0));         // (build the cache before the parallel part)
            DMesh3 wrap = NormalsStage.Wrap;
            var wrapTree = new DMeshAABBTree3(wrap, true);
            wrapTree.FindNearestTriangle(ps.GetVertex(0));

            PhaseMilliseconds["trees"] = timer.ElapsedMilliseconds; timer.Restart();
            UsedCubeSize = CubeSize > 0 ? CubeSize : Spacing / 2.5;
            AxisAlignedBox3d bounds = wrap.CachedBounds;
            bounds.Expand(2 * UsedCubeSize);
            double cells = (bounds.Width / UsedCubeSize + 1) * (bounds.Height / UsedCubeSize + 1) * (bounds.Depth / UsedCubeSize + 1);
            if (!(cells <= MaxGridNodes)) {
                Status = SurfaceStatus.GridTooLarge;
                Error = "the grid has about " + cells.ToString("0") + " nodes";
                return false;
            }

            long band = 0;
            double hole = HoleFactor;
            // marching cubes asks for every node up to four times (once per cell around it): remember the values
            // (a value is a pure function of the node, so the order of the queries does not matter)
            double ox = bounds.Min.x, oy = bounds.Min.y, oz = bounds.Min.z, cs = UsedCubeSize;
            long nodesX = (long)Math.Floor(bounds.Width / cs) + 3, nodesY = (long)Math.Floor(bounds.Height / cs) + 3;
            var memo = new System.Collections.Concurrent.ConcurrentDictionary<long, double>();
            Func<Vector3d, double> rawField = null;
            Func<Vector3d, double> field = (p) => {
                // (the root finder of marching cubes also asks for points between the nodes: those are not remembered)
                double fx = (p.x - ox) / cs, fy = (p.y - oy) / cs, fz = (p.z - oz) / cs;
                double rx = Math.Round(fx), ry = Math.Round(fy), rz = Math.Round(fz);
                if (Math.Abs(fx - rx) > 1e-9 || Math.Abs(fy - ry) > 1e-9 || Math.Abs(fz - rz) > 1e-9)
                    return rawField(p);
                long key = ((long)rz * nodesY + (long)ry) * nodesX + (long)rx;
                double v;
                if (memo.TryGetValue(key, out v)) return v;
                v = rawField(p);
                memo[key] = v;
                return v;
            };
            rawField = (p) => {
                int j = pointTree.FindNearestPoint(p);
                double s = pointSpacing[j];
                double d = ps.GetVertex(j).Distance(p);
                double r = hole * s;
                double mask = d <= r ? 0 : (d >= 1.5 * r ? 1 : Smooth((d - r) / (0.5 * r)));
                double wrapField = 0;
                if (mask > 0) {
                    double sqr;
                    int t = wrapTree.FindNearestTriangle(p, out sqr);
                    double dist = t == DMesh3.InvalidID ? double.MaxValue : Math.Sqrt(sqr);
                    wrapField = wrapTree.IsInside(p) ? -dist : dist;
                    if (mask >= 1) return wrapField;
                }
                System.Threading.Interlocked.Increment(ref band);
                double w = pointTree.FastWindingNumber(p);
                double windField = (0.5 - w) * s;
                return mask > 0 ? (1 - mask) * windField + mask * wrapField : windField;
            };

            var mc = new MarchingCubes() {
                Implicit = new FuncImplicit(field), Bounds = bounds, CubeSize = UsedCubeSize, IsoValue = 0,
                RootMode = MarchingCubes.RootfindingModes.LerpSteps, ParallelCompute = Parallel
            };
            mc.Generate();
            GridNodes = mc.CellDimensions.x * mc.CellDimensions.y * mc.CellDimensions.z;
            BandNodes = band;
            PhaseMilliseconds["grid"] = timer.ElapsedMilliseconds; timer.Restart();
            DMesh3 mesh = mc.Mesh;
            if (mesh == null || mesh.TriangleCount == 0) {
                Status = SurfaceStatus.EmptyResult; Error = "the iso-surface is empty"; return false;
            }

            // small closed components (bubbles of the winding number field): removed, counted, reported
            var comps = new MeshConnectedComponents(mesh);
            comps.FindConnectedT();
            double[] areas = comps.Components.Select(c => c.Indices.Sum(t => mesh.GetTriArea(t))).ToArray();
            double total = areas.Sum(), biggest = areas.Max();
            double removed = 0;
            int removedCount = 0;
            for (int i = 0; i < areas.Length; ++i) {
                if (areas[i] >= MinComponentAreaFraction * biggest) continue;
                foreach (int t in comps.Components[i].Indices) mesh.RemoveTriangle(t, true);
                removed += areas[i]; removedCount++;
            }
            ComponentsRemoved = removedCount;
            RemovedAreaFraction = total > 0 ? removed / total : 0;
            Result = mesh;

            // the share of the surface in the no-data zone
            double wrapArea = 0, all = 0;
            foreach (int t in mesh.TriangleIndices()) {
                double a = mesh.GetTriArea(t);
                all += a;
                Vector3d c = mesh.GetTriCentroid(t);
                int j = pointTree.FindNearestPoint(c);
                if (ps.GetVertex(j).Distance(c) > hole * pointSpacing[j]) wrapArea += a;
            }
            WrapAreaFraction = all > 0 ? wrapArea / all : 0;

            PhaseMilliseconds["cleanup"] = timer.ElapsedMilliseconds;
            string reason = "";
            if (!mesh.IsClosed()) reason = "not closed";
            else if (mesh.Vertices().Any(v => !v.IsFinite)) reason = "non-finite vertices";
            else if (!mesh.CheckValidity(false, FailMode.ReturnOnly)) reason = "invalid mesh";
            else {
                var left = new MeshConnectedComponents(mesh);
                left.FindConnectedT();
                if (left.Count != 1) reason = left.Count + " components";
            }
            if (reason != "") {
                Status = SurfaceStatus.NotClean; Error = reason; return false;
            }
            Status = SurfaceStatus.Success;
            return true;
        }

        static double Smooth(double t)
        {
            t = Math.Min(1, Math.Max(0, t));
            return t * t * (3 - 2 * t);
        }

        class FuncImplicit : ImplicitFunction3d
        {
            readonly Func<Vector3d, double> f;
            public FuncImplicit(Func<Vector3d, double> f) { this.f = f; }
            public double Value(ref Vector3d pt) { return f(pt); }
        }
    }
}
