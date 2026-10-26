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
    /// (MeshHausdorffDistance), which also covers points inside the triangles.
    /// </summary>
    public class MeshDeviation
    {
        public DMesh3 Mesh, Reference;
        /// <summary>
        /// optional spatial data structure of the reference (built if null; for a reference far from the
        /// origin the queries then run on a copy moved to the origin, and this stays null)
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


        public MeshDeviation(DMesh3 mesh, DMesh3 reference)
        {
            Mesh = mesh; Reference = reference;
        }


        /// <summary> returns false if a mesh is empty or the computation was cancelled </summary>
        public bool Compute()
        {
            VertexDeviation = null; ClosestPoint = null; ClosestTriangle = null;
            Statistics = VertexStatistics = null;
            HausdorffLowerBound = HausdorffUpperBound = double.NaN;
            if (Mesh.TriangleCount == 0 || Reference.TriangleCount == 0)
                return false;
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

            if (ComputeCertifiedBound) {
                var h = new MeshHausdorffDistance(Mesh, Reference) {
                    SpatialB = (refMesh == Reference) ? refSpatial : null, Symmetric = false, Tolerance = CertifiedTolerance, CancelF = CancelF
                };
                if (h.Compute() == false)
                    return false;
                HausdorffLowerBound = h.LowerBound;
                HausdorffUpperBound = h.UpperBound;
            }
            return true;
        }


        /// <summary> signed distance of p to the reference (see Sign), its closest point and triangle </summary>
        public double SignedDistance(Vector3d p, out Vector3d closest, out int tid)
        {
            if (refSpatial == null)
                prepare_reference();
            if (Sign == Signs.PseudoNormal && vertexNormals == null)
                compute_pseudonormals();
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
        // coordinates) and no ReferenceSpatial was given, a copy moved to the origin with the same IDs. The
        // tree's boxes are floats, which round by 2^-24 of the coordinates (0.3 at 5e6): far from the origin
        // they would prune the nearest triangle wrongly and visit far too many boxes.
        DMesh3 refMesh;
        DMeshAABBTree3 refSpatial;
        Vector3d refOffset = Vector3d.Zero;

        void prepare_reference()
        {
            refOffset = Vector3d.Zero;
            if (ReferenceSpatial == null && MeshWallThickness.FarFromOrigin(Reference)) {
                refOffset = Reference.CachedBounds.Center;
                refMesh = new DMesh3(Reference, false, MeshComponents.None);
                MeshTransforms.Translate(refMesh, -refOffset);
                refSpatial = new DMeshAABBTree3(refMesh, true);
            } else {
                if (ReferenceSpatial == null)
                    ReferenceSpatial = new DMeshAABBTree3(Reference, true);
                refMesh = Reference;
                refSpatial = ReferenceSpatial;
            }
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
            double sw = 0, sum = 0, sumAbs = 0, sumSq = 0;
            s.Min = double.MaxValue; s.Max = double.MinValue; s.MaxAbsolute = 0;
            for (int i = 0; i < v.Count; ++i) {
                sw += w[i]; sum += w[i] * v[i]; sumAbs += w[i] * Math.Abs(v[i]); sumSq += w[i] * v[i] * v[i];
                s.Min = Math.Min(s.Min, v[i]); s.Max = Math.Max(s.Max, v[i]);
                s.MaxAbsolute = Math.Max(s.MaxAbsolute, Math.Abs(v[i]));
            }
            s.TotalWeight = sw;
            s.Mean = sum / sw;
            s.MeanAbsolute = sumAbs / sw;
            s.RMS = Math.Sqrt(sumSq / sw);
            double var = 0;
            for (int i = 0; i < v.Count; ++i)
                var += w[i] * (v[i] - s.Mean) * (v[i] - s.Mean);
            s.StdDev = Math.Sqrt(var / sw);

            sort(v, w, false, out s.sortedValues, out s.sortedValueWeights);
            sort(v, w, true, out s.sortedAbs, out s.sortedAbsWeights);
            return s;
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
                int b = (max > min) ? (int)Math.Floor((sortedValues[i] - min) / (max - min) * h.Length) : 0;
                h[MathUtil.Clamp(b, 0, h.Length - 1)] += w;
            }
            return h;
        }
    }
}
