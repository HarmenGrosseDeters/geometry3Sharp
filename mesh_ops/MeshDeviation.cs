// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Deviation map of a mesh (eg a scan, a remeshed or simplified result) against a reference mesh:
    /// the signed distance of every vertex to the reference surface, positive outside (on the side the
    /// reference normals point to), negative inside, plus statistics over the vertices.
    ///
    /// The sign is taken from the closest point (Signs):
    ///   - PseudoNormal: the angle weighted pseudo-normal of the closest feature (face, edge or vertex) of
    ///     the reference, after Baerentzen and Aanaes, "Signed distance computation using the angle weighted
    ///     pseudonormal" (IEEE TVCG 2005). Correct for any point when the reference is closed, manifold and
    ///     consistently oriented; for open references it is the side of the nearest patch. Closed parts that
    ///     are inside out for their nesting, and an inside-out reference as a whole, are taken with flipped
    ///     normals (OrientInvertedParts, see MeshWallThickness.InvertedTriangles).
    ///   - WindingNumber: inside where the absolute generalized winding number of the reference is > 0.5
    ///     (Jacobson et al. 2013, via FastWindingNumber; absolute, so that inside-out parts count as solid).
    ///     Robust to holes, self-intersections and inconsistent orientation of a (nearly) closed reference,
    ///     but undefined for open sheets.
    ///   - Unsigned: the plain distance.
    ///
    /// The vertex values sample the deviation; the largest one is a lower bound of the one-sided Hausdorff
    /// distance from Mesh to Reference. ComputeCertifiedBound adds the certified interval of that distance
    /// (MeshHausdorffDistance), which also covers points inside the triangles. ComputeTriangleBounds adds
    /// such an interval per triangle (TriangleDeviationLower / Upper), so that the map is reliable between
    /// the vertices too: no point of a triangle is farther from the reference than its upper bound.
    /// </summary>
    public class MeshDeviation
    {
        public DMesh3 Mesh, Reference;
        /// <summary>
        /// optional spatial data structure of the reference. If null it is built (and rebuilt when Reference
        /// changes); for a reference far from the origin (MeshWallThickness.FarFromOrigin) the queries then
        /// run on a copy moved to the origin, with its own tree, and this stays null. A tree set here is used
        /// as it is (also far from the origin).
        /// </summary>
        public DMeshAABBTree3 ReferenceSpatial;

        public enum Signs { PseudoNormal, WindingNumber, Unsigned }
        public Signs Sign = Signs.PseudoNormal;
        /// <summary> PseudoNormal: take inside-out closed components and an inside-out reference as flipped </summary>
        public bool OrientInvertedParts = true;

        /// <summary> also compute the certified one-sided Hausdorff distance from Mesh to Reference </summary>
        public bool ComputeCertifiedBound = false;
        /// <summary> absolute tolerance of the certified bound; 0 = 1e-4 times the bounding box diagonal </summary>
        public double CertifiedTolerance = 0;
        /// <summary>
        /// also compute, per triangle of Mesh, the certified interval of the largest (unsigned) distance of its
        /// points to the reference, to CertifiedTolerance (TriangleDeviationBounds.MaxDistance)
        /// </summary>
        public bool ComputeTriangleBounds = false;
        /// <summary>
        /// ComputeTriangleBounds: subdivisions per triangle. A triangle that needs more keeps a wider, still
        /// valid interval and is counted in TriangleBoundsUnconverged. With ComputeCertifiedBound, upper
        /// bounds are capped by HausdorffUpperBound.
        /// </summary>
        public int MaxTriangleSubdivisions = 1000;

        public Func<bool> CancelF = () => { return false; };

        // outputs
        /// <summary> signed deviation per vertex ID of Mesh (NaN for unused IDs and isolated vertices) </summary>
        public double[] VertexDeviation;
        /// <summary> closest point on the reference and its triangle, per vertex ID of Mesh </summary>
        public Vector3d[] ClosestPoint;
        public int[] ClosestTriangle;
        /// <summary> statistics over the vertices, each vertex weighted by one third of the area of its triangles </summary>
        public DeviationStatistics Statistics;
        /// <summary> statistics over the vertices, unweighted </summary>
        public DeviationStatistics VertexStatistics;
        /// <summary> certified interval of the one-sided Hausdorff distance (if ComputeCertifiedBound) </summary>
        public double HausdorffLowerBound = double.NaN, HausdorffUpperBound = double.NaN;
        /// <summary>
        /// per triangle ID of Mesh (if ComputeTriangleBounds; NaN for unused IDs): the largest distance of a
        /// point of the triangle to the reference lies in [TriangleDeviationLower, TriangleDeviationUpper].
        /// Lower is attained (at TriangleWorstPoint) and at least the largest |VertexDeviation| of the corners;
        /// every point of the triangle is within Upper of the reference. Unsigned: the sign can change inside
        /// a triangle that crosses the reference.
        /// </summary>
        public double[] TriangleDeviationLower, TriangleDeviationUpper;
        /// <summary> per triangle ID: the point where TriangleDeviationLower is attained </summary>
        public Vector3d[] TriangleWorstPoint;
        /// <summary> number of triangles whose interval is wider than the tolerance (MaxTriangleSubdivisions) </summary>
        public int TriangleBoundsUnconverged;
        /// <summary>
        /// statistics of TriangleDeviationUpper weighted by triangle area. FractionWithin(tol) is a certified
        /// lower bound of the area fraction of Mesh within tol of the reference. An unresolved (infinite) upper
        /// bound enters as double.MaxValue, which leaves the fractions and percentiles meaningful but not the
        /// mean, RMS and standard deviation.
        /// </summary>
        public DeviationStatistics TriangleUpperStatistics;


        public MeshDeviation(DMesh3 mesh, DMesh3 reference)
        {
            Mesh = mesh; Reference = reference;
        }


        // The power of two the meshes are scaled by for the queries, or 1. Outside an extent of [2^-100, 2^100] the squares of
        // the distances overflow or underflow (QA 2.0 T1 finding R35: NaN deviations at 1e160 and 1e300, the weights of the
        // statistics 0 at 1e-310). Only for a tree the instance builds itself, and for meshes of about the same extent (the
        // smaller scale of the two: the extents of Mesh and Reference differ a little, and the larger one then stays below 4)
        // whose scaled coordinates are exact (normal numbers).
        double extreme_scale()
        {
            if (ReferenceSpatial != null && ReferenceSpatial != ownReferenceSpatial)
                return 1;
            double sm = MeshRepairMetrics.unit_scale_for_mesh(Mesh, true);
            double sr = MeshRepairMetrics.unit_scale_for_mesh(Reference, true);
            if (sm == 1 || sr == 1)
                return 1;
            double lo = Math.Min(sm, sr), hi = Math.Max(sm, sr);
            if (hi / lo > 1048576)
                return 1;
            return (scaling_is_exact(Mesh, lo) && scaling_is_exact(Reference, lo)) ? lo : 1;
        }

        // every coordinate times s is finite and normal (or 0)
        static bool scaling_is_exact(DMesh3 mesh, double s)
        {
            foreach (int v in mesh.VertexIndices()) {
                Vector3d p = mesh.GetVertex(v);
                for (int k = 0; k < 3; ++k) {
                    double x = Math.Abs(p[k] * s);
                    if (!(x <= 1e300) || (x != 0 && x < 2.2250738585072014e-308))
                        return false;
                }
            }
            return true;
        }

        // Compute on copies of the meshes scaled by the power of two scale (exact), the lengths mapped back (exact: the
        // result is that of the unit-scale problem, times 1 / scale, bit for bit)
        bool compute_scaled(double scale)
        {
            var local = new MeshDeviation(scaled_copy(Mesh, scale), scaled_copy(Reference, scale)) {
                Sign = Sign, OrientInvertedParts = OrientInvertedParts, ComputeCertifiedBound = ComputeCertifiedBound,
                CertifiedTolerance = CertifiedTolerance * scale, ComputeTriangleBounds = ComputeTriangleBounds,
                MaxTriangleSubdivisions = MaxTriangleSubdivisions, CancelF = CancelF
            };
            if (local.Compute() == false)
                return false;
            VertexDeviation = unscale(local.VertexDeviation, scale);
            ClosestPoint = unscale(local.ClosestPoint, scale);
            ClosestTriangle = local.ClosestTriangle;
            HausdorffLowerBound = local.HausdorffLowerBound / scale; HausdorffUpperBound = local.HausdorffUpperBound / scale;
            TriangleDeviationLower = unscale(local.TriangleDeviationLower, scale);
            TriangleDeviationUpper = unscale(local.TriangleDeviationUpper, scale);
            TriangleWorstPoint = unscale(local.TriangleWorstPoint, scale);
            TriangleBoundsUnconverged = local.TriangleBoundsUnconverged;
            // the statistics of the values in the units of Mesh (the weights are the areas of the scaled copy: only their ratios count)
            var values = new List<double>();
            var weights = new List<double>();
            foreach (int vid in local.Mesh.VertexIndices()) {
                if (double.IsNaN(VertexDeviation[vid])) continue;
                double area = 0;
                foreach (int tid in local.Mesh.VtxTrianglesItr(vid))
                    area += local.Mesh.GetTriArea(tid);
                values.Add(VertexDeviation[vid]);
                weights.Add(area / 3);
            }
            Statistics = DeviationStatistics.Compute(values, weights);
            VertexStatistics = DeviationStatistics.Compute(values);
            if (TriangleDeviationUpper != null) {
                values = new List<double>(); weights = new List<double>();
                foreach (int tid in local.Mesh.TriangleIndices()) {
                    double hi = TriangleDeviationUpper[tid];
                    if (double.IsNaN(hi)) continue;
                    values.Add(double.IsInfinity(hi) ? double.MaxValue : hi);
                    weights.Add(local.Mesh.GetTriArea(tid));
                }
                TriangleUpperStatistics = DeviationStatistics.Compute(values, weights);
            }
            return true;
        }

        static DMesh3 scaled_copy(DMesh3 mesh, double scale)
        {
            var m = new DMesh3(mesh, false, MeshComponents.None);
            MeshRepairMetrics.ScaleVertices(m, scale);
            return m;
        }

        static double[] unscale(double[] values, double scale)
        {
            if (values == null) return null;
            var r = new double[values.Length];
            for (int i = 0; i < r.Length; ++i) r[i] = values[i] / scale;
            return r;
        }

        static Vector3d[] unscale(Vector3d[] values, double scale)
        {
            if (values == null) return null;
            var r = new Vector3d[values.Length];
            for (int i = 0; i < r.Length; ++i) r[i] = values[i] / scale;
            return r;
        }

        /// <summary> returns false if a mesh is empty or the computation was cancelled </summary>
        public bool Compute()
        {
            VertexDeviation = null; ClosestPoint = null; ClosestTriangle = null;
            Statistics = VertexStatistics = null;
            HausdorffLowerBound = HausdorffUpperBound = double.NaN;
            TriangleDeviationLower = TriangleDeviationUpper = null; TriangleWorstPoint = null;
            TriangleBoundsUnconverged = 0; TriangleUpperStatistics = null;
            if (Mesh.TriangleCount == 0 || Reference.TriangleCount == 0)
                return false;
            double scale = extreme_scale();
            if (scale != 1)
                return compute_scaled(scale);
            prepare_reference();
            if (Sign == Signs.PseudoNormal)
                compute_pseudonormals();
            else if (Sign == Signs.WindingNumber)
                refSpatial.FastWindingNumber(Vector3d.Zero);     // cache before parallel use

            int NV = Mesh.MaxVertexID;
            VertexDeviation = new double[NV];
            ClosestPoint = new Vector3d[NV];
            ClosestTriangle = new int[NV];
            for (int i = 0; i < NV; ++i) {
                VertexDeviation[i] = double.NaN;
                ClosestTriangle[i] = DMesh3.InvalidID;
            }
            gParallel.ForEach(Mesh.VertexIndices(), (vid) => {
                if (CancelF() || Mesh.GetVtxEdgeCount(vid) == 0) return;
                Vector3d p = Mesh.GetVertex(vid);
                Vector3d c; int tid;
                VertexDeviation[vid] = SignedDistance(p, out c, out tid);
                ClosestPoint[vid] = c;
                ClosestTriangle[vid] = tid;
            });
            if (CancelF())
                return false;

            var values = new List<double>();
            var weights = new List<double>();
            foreach (int vid in Mesh.VertexIndices()) {
                if (double.IsNaN(VertexDeviation[vid])) continue;
                double area = 0;
                foreach (int tid in Mesh.VtxTrianglesItr(vid))
                    area += Mesh.GetTriArea(tid);
                values.Add(VertexDeviation[vid]);
                weights.Add(area / 3);
            }
            Statistics = DeviationStatistics.Compute(values, weights);
            VertexStatistics = DeviationStatistics.Compute(values);

            if (ComputeCertifiedBound == false && ComputeTriangleBounds == false)
                return true;
            // far from the origin: both meshes moved by the same offset, on the reference's local tree. The
            // vertex queries above used p - refOffset, the same coordinates as this copy
            DMesh3 from = Mesh;
            if (refOffset != Vector3d.Zero) {
                from = new DMesh3(Mesh, false, MeshComponents.None);
                MeshTransforms.Translate(from, -refOffset);
            }
            if (ComputeCertifiedBound) {
                var h = new MeshHausdorffDistance(from, refMesh) {
                    SpatialB = refSpatial, Symmetric = false, Tolerance = CertifiedTolerance, CancelF = CancelF
                };
                if (h.Compute() == false)
                    return false;
                HausdorffLowerBound = h.LowerBound;
                HausdorffUpperBound = h.UpperBound;
            }
            if (ComputeTriangleBounds && compute_triangle_bounds(from) == false)
                return false;
            return true;
        }


        // per triangle interval of the largest distance, on the local copies (from: Mesh with the reference's
        // offset); the corners reuse the vertex distances and closest triangles
        bool compute_triangle_bounds(DMesh3 from)
        {
            AxisAlignedBox3d box = from.CachedBounds;
            box.Contain(refMesh.CachedBounds);
            // relative to the size of the meshes (as MeshHausdorffDistance), not to the coordinates
            double tol = (CertifiedTolerance > 0) ? CertifiedTolerance : 1e-4 * box.DiagonalLength;
            int NT = Mesh.MaxTriangleID;
            double[] lo = new double[NT], hi = new double[NT];
            Vector3d[] worst = new Vector3d[NT];
            bool[] converged = new bool[NT];
            for (int i = 0; i < NT; ++i) {
                lo[i] = hi[i] = double.NaN;
                converged[i] = true;
            }
            var bounds = new TriangleDeviationBounds(refSpatial.Mesh);
            gParallel.ForEach(Mesh.TriangleIndices(), (tid) => {
                if (CancelF()) return;
                Index3i t = Mesh.GetTriangle(tid);
                Vector3d w;
                converged[tid] = bounds.MaxDistance(refSpatial, from.GetVertex(t.a), from.GetVertex(t.b), from.GetVertex(t.c),
                    Math.Abs(VertexDeviation[t.a]), Math.Abs(VertexDeviation[t.b]), Math.Abs(VertexDeviation[t.c]),
                    ClosestTriangle[t.a], ClosestTriangle[t.b], ClosestTriangle[t.c],
                    tol, MaxTriangleSubdivisions, out lo[tid], out hi[tid], out w);
                worst[tid] = w + refOffset;
                // no point is farther than the certified Hausdorff bound: it caps triangles that ran out of
                // subdivisions (lower is attained, so it stays below the bound up to rounding)
                if (ComputeCertifiedBound && hi[tid] > HausdorffUpperBound) {
                    hi[tid] = Math.Max(lo[tid], HausdorffUpperBound);
                    converged[tid] = !(hi[tid] > lo[tid] + tol);
                }
            });
            if (CancelF())
                return false;
            var values = new List<double>();
            var weights = new List<double>();
            foreach (int tid in Mesh.TriangleIndices()) {
                if (double.IsNaN(hi[tid])) continue;       // non-finite input, not an unconverged interval
                if (converged[tid] == false) TriangleBoundsUnconverged++;
                // an unresolved (infinite) bound must count as far, not be skipped
                values.Add(double.IsInfinity(hi[tid]) ? double.MaxValue : hi[tid]);
                weights.Add(Mesh.GetTriArea(tid));
            }
            TriangleDeviationLower = lo; TriangleDeviationUpper = hi; TriangleWorstPoint = worst;
            TriangleUpperStatistics = DeviationStatistics.Compute(values, weights);
            return true;
        }


        /// <summary> signed distance of p to the reference (see Sign), its closest point and triangle </summary>
        public double SignedDistance(Vector3d p, out Vector3d closest, out int tid)
        {
            lock (prepareLock) {
                prepare_reference();
                if (Sign == Signs.PseudoNormal && vertexNormals == null)
                    compute_pseudonormals();
            }
            Vector3d q = p - refOffset;
            double d = signed_distance_local(q, out closest, out tid);
            closest += refOffset;
            return d;
        }

        double signed_distance_local(Vector3d p, out Vector3d closest, out int tid)
        {
            tid = refSpatial.FindNearestTriangle(p);
            closest = p;
            if (tid == DMesh3.InvalidID)
                return double.NaN;
            Index3i tv = refMesh.GetTriangle(tid);
            Triangle3d tri = new Triangle3d(refMesh.GetVertex(tv.a), refMesh.GetVertex(tv.b), refMesh.GetVertex(tv.c));
            Vector3d bary;
            double d = Math.Sqrt(DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out closest, out bary));
            switch (Sign) {
                case Signs.Unsigned:
                    return d;
                case Signs.WindingNumber:
                    return (Math.Abs(refSpatial.FastWindingNumber(p)) > 0.5) ? -d : d;
                default:
                    Vector3d n = feature_normal(tid, tv, bary);
                    if (n == Vector3d.Zero)     // only degenerate triangles around: no direction to judge by
                        return (Math.Abs(refSpatial.FastWindingNumber(p)) > 0.5) ? -d : d;
                    return ((p - closest).Dot(n) < 0) ? -d : d;
            }
        }


        // The reference the queries run on: Reference itself, or, if it lies far from the origin (eg geo
        // coordinates) and the caller set no ReferenceSpatial, a copy moved to the origin with the same IDs.
        // The tree's boxes are floats, which round by 2^-24 of the coordinates (0.3 at 5e6): far from the
        // origin they would visit far too many boxes. Rebuilt only when Reference or ReferenceSpatial
        // changes (another object, or the reference was edited).
        DMesh3 refMesh;
        DMeshAABBTree3 refSpatial;
        Vector3d refOffset = Vector3d.Zero;
        DMesh3 refSource;
        int refStamp = -1;
        DMeshAABBTree3 refFromField;        // ReferenceSpatial as it was when the state was prepared
        DMeshAABBTree3 ownReferenceSpatial; // a tree this instance built on Reference itself
        readonly object prepareLock = new object();

        void prepare_reference()
        {
            if (refSpatial != null && refSource == Reference && refStamp == Reference.ShapeTimestamp
                && refFromField == ReferenceSpatial)
                return;
            // anything cached for the previous reference is stale
            vertexNormals = null; flipped = null;
            bool given = ReferenceSpatial != null && ReferenceSpatial != ownReferenceSpatial;
            refOffset = Vector3d.Zero;
            if (given) {
                refMesh = Reference;
                refSpatial = ReferenceSpatial;
            } else if (MeshWallThickness.FarFromOrigin(Reference)) {
                refOffset = Reference.CachedBounds.Center;
                refMesh = new DMesh3(Reference, false, MeshComponents.None);
                MeshTransforms.Translate(refMesh, -refOffset);
                refSpatial = new DMeshAABBTree3(refMesh, true);
                ReferenceSpatial = null;
            } else {
                ownReferenceSpatial = new DMeshAABBTree3(Reference, true);
                ReferenceSpatial = ownReferenceSpatial;
                refMesh = Reference;
                refSpatial = ownReferenceSpatial;
            }
            refSource = Reference; refStamp = Reference.ShapeTimestamp; refFromField = ReferenceSpatial;
        }


        // angle weighted vertex pseudo-normals of the reference (Baerentzen and Aanaes 2005)
        Vector3d[] vertexNormals;
        bool[] flipped;

        // zero for degenerate triangles (area below 1e-12 diag^2): their normal is rounding noise
        Vector3d tri_normal(int tid)
        {
            double area;
            Vector3d n = MeshAnalysisNormals.TriNormal(refMesh, tid, out area);
            if (!(area >= degenerateArea))
                return Vector3d.Zero;
            return (flipped != null && flipped[tid]) ? -n : n;
        }
        double degenerateArea;

        void compute_pseudonormals()
        {
            flipped = OrientInvertedParts ? MeshWallThickness.InvertedTriangles(refMesh) : null;
            double diag = refMesh.CachedBounds.DiagonalLength;
            degenerateArea = 1e-12 * diag * diag;
            // the winding number decides where degenerate triangles leave no normal: build its cache now,
            // before the parallel queries
            foreach (int tid in refMesh.TriangleIndices())
                if (tri_normal(tid) == Vector3d.Zero) {
                    refSpatial.FastWindingNumber(Vector3d.Zero);
                    break;
                }
            vertexNormals = new Vector3d[refMesh.MaxVertexID];
            foreach (int tid in refMesh.TriangleIndices()) {
                Vector3d n = tri_normal(tid);
                if (double.IsNaN(n.x)) continue;
                Index3i t = refMesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j) {
                    double angle = MeshAnalysisNormals.InternalAngle(refMesh, tid, j);
                    if (double.IsNaN(angle)) continue;
                    vertexNormals[t[j]] += angle * n;
                }
            }
        }

        // pseudo-normal of the closest feature: the face, the edge (sum of the face normals) or the vertex
        Vector3d feature_normal(int tid, Index3i tv, Vector3d bary)
        {
            const double featureTol = 1e-9;
            int zeros = 0, nonzero = -1, zero = -1;
            for (int j = 0; j < 3; ++j) {
                if (bary[j] <= featureTol) { zeros++; zero = j; } else nonzero = j;
            }
            if (zeros >= 2)
                return vertexNormals[tv[nonzero]];
            Vector3d n = tri_normal(tid);
            if (zeros == 1) {
                int a = tv[(zero + 1) % 3], b = tv[(zero + 2) % 3];
                int eid = refMesh.FindEdge(a, b);
                if (eid != DMesh3.InvalidID) {
                    Index2i et = refMesh.GetEdgeT(eid);
                    int other = (et.a == tid) ? et.b : et.a;
                    if (other != DMesh3.InvalidID)
                        n += tri_normal(other);
                }
            }
            if (n == Vector3d.Zero)     // degenerate triangle: the pseudo-normals of its corners
                n = vertexNormals[tv.a] + vertexNormals[tv.b] + vertexNormals[tv.c];
            return n;
        }
    }



    /// <summary>
    /// Summary statistics of (optionally weighted) signed values, eg deviations or thicknesses. NaN values
    /// are skipped. Percentiles are of the absolute values.
    /// </summary>
    public class DeviationStatistics
    {
        public int Count;
        public double Min = double.NaN, Max = double.NaN;
        public double Mean = double.NaN, StdDev = double.NaN, RMS = double.NaN;
        public double MeanAbsolute = double.NaN, MaxAbsolute = double.NaN;
        /// <summary> sum of the weights (eg the area) </summary>
        public double TotalWeight;

        double[] sortedAbs;
        double[] sortedAbsWeights;
        double[] sortedValues;
        double[] sortedValueWeights;


        public static DeviationStatistics Compute(IList<double> values, IList<double> weights = null)
        {
            var s = new DeviationStatistics();
            var v = new List<double>();
            var w = new List<double>();
            for (int i = 0; i < values.Count; ++i) {
                double wi = (weights == null) ? 1 : weights[i];
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || !(wi > 0)) continue;
                v.Add(values[i]); w.Add(wi);
            }
            s.Count = v.Count;
            if (s.Count == 0)
                return s;
            // the moments are accumulated in units scaled by powers of two (exact): at extreme sizes the products
            // w * v * v overflow or underflow (QA 2.0 T1 finding R35), and the ratios do not depend on the units
            double maxW = 0, maxV = 0;
            double total = 0;
            for (int i = 0; i < v.Count; ++i) {
                maxW = Math.Max(maxW, w[i]); maxV = Math.Max(maxV, Math.Abs(v[i]));
                total += w[i];
            }
            double ws = pow2_inverse(maxW), vs = pow2_inverse(maxV);
            double sw = 0, sum = 0, sumAbs = 0, sumSq = 0;
            s.Min = double.MaxValue; s.Max = double.MinValue; s.MaxAbsolute = 0;
            for (int i = 0; i < v.Count; ++i) {
                double wi = w[i] * ws, vi = v[i] * vs;
                sw += wi; sum += wi * vi; sumAbs += wi * Math.Abs(vi); sumSq += wi * vi * vi;
                s.Min = Math.Min(s.Min, v[i]); s.Max = Math.Max(s.Max, v[i]);
                s.MaxAbsolute = Math.Max(s.MaxAbsolute, Math.Abs(v[i]));
            }
            s.TotalWeight = total;
            double meanS = sum / sw;
            s.Mean = meanS / vs;
            s.MeanAbsolute = sumAbs / sw / vs;
            s.RMS = Math.Sqrt(sumSq / sw) / vs;
            double var = 0;
            for (int i = 0; i < v.Count; ++i) {
                double d = v[i] * vs - meanS;
                var += w[i] * ws * d * d;
            }
            s.StdDev = Math.Sqrt(var / sw) / vs;

            sort(v, w, false, out s.sortedValues, out s.sortedValueWeights);
            sort(v, w, true, out s.sortedAbs, out s.sortedAbsWeights);
            return s;
        }

        // 2^-e for x in [2^e, 2^(e+1)), x > 0 finite (1 for x = 0), e limited to [-1000, 1000]: a power of two, so that
        // scaling by it is exact, and x times it is about 1
        static double pow2_inverse(double x)
        {
            if (!(x > 0) || double.IsInfinity(x))
                return 1;
            long bits = BitConverter.DoubleToInt64Bits(x);
            int e = (int)((bits >> 52) & 0x7FF);
            if (e == 0) {
                bits = BitConverter.DoubleToInt64Bits(x * 18446744073709551616.0);    // 2^64 (subnormal: exact)
                e = (int)((bits >> 52) & 0x7FF) - 64;
            }
            e -= 1023;
            e = Math.Max(-1000, Math.Min(1000, e));
            return BitConverter.Int64BitsToDouble((long)(1023 - e) << 52);
        }

        static void sort(List<double> v, List<double> w, bool abs, out double[] values, out double[] cumWeights)
        {
            values = new double[v.Count];
            double[] wt = new double[v.Count];
            for (int i = 0; i < v.Count; ++i) {
                values[i] = abs ? Math.Abs(v[i]) : v[i]; wt[i] = w[i];
            }
            Array.Sort(values, wt);
            cumWeights = wt;
            for (int i = 1; i < cumWeights.Length; ++i)
                cumWeights[i] += cumWeights[i - 1];
        }

        static double quantile(double[] values, double[] cum, double q)
        {
            if (values == null || values.Length == 0)
                return double.NaN;
            double target = MathUtil.Clamp(q, 0, 1) * cum[cum.Length - 1];
            int lo = 0, hi = values.Length - 1;
            while (lo < hi) {
                int mid = (lo + hi) / 2;
                if (cum[mid] < target) lo = mid + 1; else hi = mid;
            }
            return values[lo];
        }

        /// <summary> weighted median of the signed values </summary>
        public double Median { get { return quantile(sortedValues, sortedValueWeights, 0.5); } }

        /// <summary> smallest absolute value that at least the fraction q (0..1) of the weight does not exceed, eg 0.95 </summary>
        public double AbsolutePercentile(double q)
        {
            return quantile(sortedAbs, sortedAbsWeights, q);
        }

        /// <summary> weighted quantile q (0..1) of the signed values </summary>
        public double Quantile(double q)
        {
            return quantile(sortedValues, sortedValueWeights, q);
        }

        /// <summary> fraction of the weight with |value| &lt;= tolerance </summary>
        public double FractionWithin(double tolerance)
        {
            if (sortedAbs == null || sortedAbs.Length == 0)
                return double.NaN;
            int k = Array.BinarySearch(sortedAbs, tolerance);
            if (k < 0) k = ~k - 1;
            else while (k + 1 < sortedAbs.Length && sortedAbs[k + 1] <= tolerance) k++;
            return (k < 0) ? 0 : sortedAbsWeights[k] / sortedAbsWeights[sortedAbsWeights.Length - 1];
        }

        /// <summary> weight per bin of [min, max] (values outside go to the first or last bin) </summary>
        public double[] Histogram(int bins, double min, double max)
        {
            double[] h = new double[Math.Max(1, bins)];
            if (sortedValues == null)
                return h;
            double prev = 0;
            for (int i = 0; i < sortedValues.Length; ++i) {
                double w = sortedValueWeights[i] - prev;
                prev = sortedValueWeights[i];
                int b = (max > min) ? MathUtil.FloorToIntSaturating((sortedValues[i] - min) / (max - min) * h.Length) : 0;
                h[MathUtil.Clamp(b, 0, h.Length - 1)] += w;
            }
            return h;
        }
    }
}
