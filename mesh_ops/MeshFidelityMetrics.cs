// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
using System;
using System.Collections.Generic;
using g3;

namespace gs
{
    /// <summary>
    /// Surface fidelity of a mesh (eg a repair, remesh, solidify or wrap result) to a reference (eg its input),
    /// beyond the Hausdorff distance and the volume IoU of MeshRepairMetrics, which miss lost features,
    /// reversed patches and topology changes:
    ///   - F-score at thresholds tau (Tatarchenko et al., "What Do Single-view 3D Reconstruction Networks
    ///     Learn?", CVPR 2019): precision = share of the mesh's surface within tau of the reference, recall =
    ///     share of the reference's surface within tau of the mesh, F = 2 P R / (P + R);
    ///   - Chamfer distance: the mean distances of both directions;
    ///   - normal consistency: |cos| of the angle between the normal at a sample and the normal at its closest
    ///     point on the other surface (mean, and as a mean angle), in both directions; separately the share of
    ///     samples within the largest tau whose normals point the other way (cos &lt; 0): inverted patches;
    ///   - Edge Chamfer distance (as in Chen et al., "Neural Dual Contouring", SIGGRAPH 2022): Chamfer distance
    ///     between the sharp edges (dihedral angle above SharpEdgeAngle, not at slivers narrower than
    ///     SharpEdgeMinHeight) of both meshes, and the edge
    ///     precision / recall at the thresholds: feature preservation;
    ///   - the same against the outer surface of the reference only (OuterSurface), so interior faces that a
    ///     repair to a solid drops on purpose do not count as lost;
    ///   - triangle quality (TriangleQualityStatistics, Verdict definitions) and topology (MeshTopologySummary)
    ///     of both meshes, and the topology changes.
    ///
    /// Sampling: SurfaceSamples points per mesh, area weighted and stratified (sample i lies in the slice
    /// [i, i + 1) / n of the cumulative area), uniform inside the chosen triangle; EdgeSamples points per mesh on
    /// its sharp edges, stratified by length. The closest point on the other mesh comes from its AABB tree
    /// (exact point-triangle distance), on the other mesh's sharp edges from a bounding volume tree of the
    /// segments (exact point-segment distance). The normal at a closest point is the angle weighted
    /// pseudo-normal of the feature (face, edge or vertex) it lies on (Baerentzen and Aanaes 2005). Cost
    /// O((n + s) log n) for n triangles and s samples. Sampling noise of a share is about 1 / s.
    ///
    /// All distances and thresholds are relative to the size of the reference (Size: the diameter of the smallest
    /// sphere around the area centroid of its triangles that holds them; for a box-like shape about the diagonal
    /// of its bounding box, but independent of the rotation), so the results do not depend on position, rotation
    /// or scale, and scaling both meshes by a power of two gives the same results bit for bit. The meshes are
    /// moved (as copies) to the reference's centroid for the queries, so coordinates far from the origin (eg UTM)
    /// do not lose precision in the float boxes of the trees.
    /// Deterministic: the samples come from Seed in a fixed order, the queries write per sample and the sums
    /// run in sample order, so Parallel = false gives the same results bit for bit.
    ///
    /// Usage: <c>var f = new MeshFidelityMetrics(input, result); if (f.Compute()) use(f.FScore[0]);</c>
    /// The meshes are not modified. Weld triangle soups first: the sharp edges are found by position, but the
    /// pseudo-normals at edges and vertices and the topology (each soup triangle is a component) use the
    /// mesh's connectivity. Orientation matters for the reversed share only: a correct result of an
    /// input with inverted patches shows them as reversed, so orient the reference (eg MeshRepairOrientation)
    /// if only the result's orientation is to be judged.
    /// </summary>
    public class MeshFidelityMetrics
    {
        /// <summary> the reference (eg the input) and the mesh judged against it (not modified) </summary>
        public DMesh3 Reference, Mesh;

        /// <summary> surface samples per mesh </summary>
        public int SurfaceSamples = 50000;
        /// <summary> samples per mesh on its sharp edges </summary>
        public int EdgeSamples = 10000;
        /// <summary> thresholds tau for the F-scores, relative to Size </summary>
        public double[] RelativeThresholds = { 0.001, 0.005, 0.01 };
        /// <summary> an edge is sharp if the angle between the normals of its triangles exceeds this (degrees) </summary>
        public double SharpEdgeAngle = 30;
        /// <summary>
        /// edges at triangles whose height (twice the area over the longest edge) is at most this, relative to Size,
        /// are not sharp: the normal of such a sliver comes from the rounding of its corners (eg float STL
        /// coordinates), not from a feature, and a crease that narrow is far below the thresholds. On Thingi10K
        /// inputs every lost "sharp edge" of the repairs that kept the triangles was at a triangle below 1e-5.
        /// </summary>
        public double SharpEdgeMinHeight = 1e-5;
        /// <summary>
        /// also measure against the outer surface of the reference only (OuterRecall, OuterFScore, Outer*): the
        /// reference samples where the generalized winding number w of the reference (tested at OuterOffset * Size
        /// along the normal, w+ and w-) jumps by one (|w+ - w-| &gt; 0.5: a single layer) and the rest of the
        /// reference does not enclose the point (|w+ + w-| / 2 &lt; 0.75: 0.5 on the boundary of a solid, 0 on an open
        /// sheet). Leaves out faces inside the solid (single faces, nested shells: mean 1 or more), internal double
        /// walls and double-sided sheets (jump 0), which a repair to a solid drops on purpose; keeps cavity walls
        /// and open sheets. Orient inside-out components of the reference first (eg WindingContouring.OrientedCopy).
        /// </summary>
        public bool OuterSurface = true;
        /// <summary> offset of the two winding number tests of OuterSurface, relative to Size (walls thinner than that are not outer) </summary>
        public double OuterOffset = 1e-5;
        /// <summary> seed of the sampling </summary>
        public int Seed = 1;
        /// <summary> run the queries in parallel (the results are the same) </summary>
        public bool Parallel = true;
        /// <summary> if this function returns true, the computation stops and Compute() returns false </summary>
        public Func<bool> CancelF = () => { return false; };

        // outputs (NaN before Compute() and if it fails)
        /// <summary> output: size of the reference (see the class), the unit of all relative values </summary>
        public double Size = double.NaN;
        /// <summary> output: per threshold of RelativeThresholds, share of the mesh within tau of the reference (Precision), of the reference within tau of the mesh (Recall), and their harmonic mean </summary>
        public double[] Precision, Recall, FScore;
        /// <summary> output: mean distance of the mesh's samples to the reference, of the reference's samples to the mesh, and their mean (Chamfer-L1), relative to Size </summary>
        public double DistanceMeshToReference = double.NaN, DistanceReferenceToMesh = double.NaN, Chamfer = double.NaN;
        /// <summary> output: largest distance of the mesh's samples to the reference, and of the reference's to the mesh (a sampled lower bound of the one-sided Hausdorff distances), relative to Size </summary>
        public double MaxDistanceMeshToReference = double.NaN, MaxDistanceReferenceToMesh = double.NaN;
        /// <summary> output (OuterSurface): share of the reference's surface that is outer, see OuterSurface </summary>
        public double OuterShare = double.NaN;
        /// <summary>
        /// output (OuterSurface): per threshold, share of the reference's outer surface within tau of the mesh, and its
        /// harmonic mean with Precision; NaN if the reference has no outer surface
        /// </summary>
        public double[] OuterRecall, OuterFScore;
        /// <summary> output (OuterSurface): mean and largest distance of the outer surface's samples to the mesh, relative to Size </summary>
        public double OuterDistanceReferenceToMesh = double.NaN, OuterMaxDistanceReferenceToMesh = double.NaN;
        /// <summary> output: normal agreement of the mesh's samples with the reference, and of the reference's samples with the mesh </summary>
        public NormalAgreement NormalsMeshToReference, NormalsReferenceToMesh;
        /// <summary> output: mean |cos| of both directions (1 = all normals parallel at corresponding points) </summary>
        public double NormalConsistency = double.NaN;
        /// <summary> output: total length of the sharp edges, relative to Size </summary>
        public double SharpEdgeLengthReference = double.NaN, SharpEdgeLengthMesh = double.NaN;
        /// <summary>
        /// output: mean distance of the samples on the mesh's sharp edges to the reference's sharp edges, and the
        /// other way, relative to Size: NaN if the sampled mesh has no sharp edges, infinity if only the other
        /// has none
        /// </summary>
        public double EdgeDistanceMeshToReference = double.NaN, EdgeDistanceReferenceToMesh = double.NaN;
        /// <summary> output: mean of both edge distances; 0 if neither mesh has sharp edges, infinity if only one has </summary>
        public double EdgeChamfer = double.NaN;
        /// <summary>
        /// output: per threshold, share of the mesh's sharp edges within tau of the reference's (EdgePrecision; NaN
        /// if the mesh has none), of the reference's within tau of the mesh's (EdgeRecall; NaN if the reference has
        /// none), and their harmonic mean (NaN if either is NaN)
        /// </summary>
        public double[] EdgePrecision, EdgeRecall, EdgeFScore;
        /// <summary> output: triangle quality of both meshes </summary>
        public TriangleQualityStatistics ReferenceQuality, MeshQuality;
        /// <summary> output: topology of both meshes, and what changed from the reference to the mesh </summary>
        public MeshTopologySummary ReferenceTopology, MeshTopology;
        public MeshTopologySummary.Changes TopologyChanges;


        public MeshFidelityMetrics(DMesh3 reference, DMesh3 mesh)
        {
            Reference = reference; Mesh = mesh;
        }


        /// <summary>
        /// Compute all outputs. Returns false (all outputs NaN or null) if either mesh has no triangles, no
        /// triangle with area above 1e-12 Size^2 (nothing to sample) or a non-finite vertex, if the reference's
        /// triangles have zero extent, if SurfaceSamples or EdgeSamples is not positive, or if cancelled.
        /// </summary>
        public bool Compute()
        {
            reset();
            if (Reference == null || Mesh == null || Reference.TriangleCount == 0 || Mesh.TriangleCount == 0
                || finite(Reference) == false || finite(Mesh) == false || SurfaceSamples <= 0 || EdgeSamples <= 0)
                return false;
            Vector3d center;
            double size = SizeOf(Reference, out center);
            if (!(size > 0) || double.IsInfinity(size))
                return false;
            DMesh3 reference = local_copy(Reference, center), mesh = local_copy(Mesh, center);
            double degenerateArea = 1e-12 * size * size;
            int nt = RelativeThresholds.Length;
            double[] tau = new double[nt];
            double maxTau = 0;
            for (int k = 0; k < nt; ++k) {
                tau[k] = RelativeThresholds[k] * size;
                maxTau = Math.Max(maxTau, tau[k]);
            }

            // surface samples, both directions
            var rnd = new Random(Seed);
            SurfaceSample[] onMesh = sample_surface(mesh, SurfaceSamples, rnd, degenerateArea);
            SurfaceSample[] onRef = sample_surface(reference, SurfaceSamples, rnd, degenerateArea);
            if (onMesh.Length == 0 || onRef.Length == 0)
                return false;       // only degenerate triangles on one side
            var refSurface = new Surface(reference, degenerateArea);
            var meshSurface = new Surface(mesh, degenerateArea);
            query(onMesh, refSurface);
            if (CancelF())
                return fail();
            query(onRef, meshSurface);
            if (OuterSurface)
                classify_outer(onRef, refSurface, OuterOffset * size);
            if (CancelF())
                return fail();

            Precision = new double[nt]; Recall = new double[nt]; FScore = new double[nt];
            OuterRecall = new double[nt]; OuterFScore = new double[nt];
            int outer = 0;
            foreach (var smp in onRef)
                if (smp.Outer) outer++;
            SurfaceSample[] onOuter = new SurfaceSample[outer];
            outer = 0;
            foreach (var smp in onRef)
                if (smp.Outer) onOuter[outer++] = smp;
            for (int k = 0; k < nt; ++k) {
                Precision[k] = share_within(onMesh, tau[k]);
                Recall[k] = share_within(onRef, tau[k]);
                FScore[k] = fscore(Precision[k], Recall[k]);
                OuterRecall[k] = OuterSurface ? share_within(onOuter, tau[k]) : double.NaN;
                OuterFScore[k] = fscore(Precision[k], OuterRecall[k]);
            }
            DistanceMeshToReference = mean_distance(onMesh) / size;
            DistanceReferenceToMesh = mean_distance(onRef) / size;
            Chamfer = 0.5 * (DistanceMeshToReference + DistanceReferenceToMesh);
            MaxDistanceMeshToReference = max_distance(onMesh) / size;
            MaxDistanceReferenceToMesh = max_distance(onRef) / size;
            if (OuterSurface) {
                OuterShare = (double)onOuter.Length / onRef.Length;
                OuterDistanceReferenceToMesh = mean_distance(onOuter) / size;
                OuterMaxDistanceReferenceToMesh = max_distance(onOuter) / size;
            }
            NormalsMeshToReference = NormalAgreement.Compute(onMesh, maxTau);
            NormalsReferenceToMesh = NormalAgreement.Compute(onRef, maxTau);
            NormalConsistency = 0.5 * (NormalsMeshToReference.MeanAbsCos + NormalsReferenceToMesh.MeanAbsCos);

            // sharp edges
            List<Vector3d> refEdges = SharpEdges(reference, SharpEdgeAngle, SharpEdgeMinHeight * size);
            List<Vector3d> meshEdges = SharpEdges(mesh, SharpEdgeAngle, SharpEdgeMinHeight * size);
            SharpEdgeLengthReference = total_length(refEdges) / size;
            SharpEdgeLengthMesh = total_length(meshEdges) / size;
            double[] fromMesh = edge_distances(meshEdges, refEdges, rnd);
            if (CancelF())
                return fail();
            double[] fromRef = edge_distances(refEdges, meshEdges, rnd);
            if (CancelF())
                return fail();
            EdgePrecision = new double[nt]; EdgeRecall = new double[nt]; EdgeFScore = new double[nt];
            for (int k = 0; k < nt; ++k) {
                EdgePrecision[k] = share_within(fromMesh, tau[k]);
                EdgeRecall[k] = share_within(fromRef, tau[k]);
                EdgeFScore[k] = fscore(EdgePrecision[k], EdgeRecall[k]);
            }
            EdgeDistanceMeshToReference = mean(fromMesh) / size;
            EdgeDistanceReferenceToMesh = mean(fromRef) / size;
            if (meshEdges.Count == 0 && refEdges.Count == 0)
                EdgeChamfer = 0;
            else if (meshEdges.Count == 0 || refEdges.Count == 0)
                EdgeChamfer = double.PositiveInfinity;
            else
                EdgeChamfer = 0.5 * (EdgeDistanceMeshToReference + EdgeDistanceReferenceToMesh);

            // topology and triangle quality
            ReferenceTopology = MeshTopologySummary.Compute(reference);
            MeshTopology = MeshTopologySummary.Compute(mesh);
            TopologyChanges = MeshTopologySummary.Compare(ReferenceTopology, MeshTopology);
            ReferenceQuality = TriangleQualityStatistics.Compute(reference);
            MeshQuality = TriangleQualityStatistics.Compute(mesh);
            if (CancelF())
                return fail();
            Size = size;
            return true;
        }

        bool fail()
        {
            reset();
            return false;
        }

        void reset()
        {
            Size = double.NaN;
            Precision = Recall = FScore = null;
            OuterRecall = OuterFScore = null;
            DistanceMeshToReference = DistanceReferenceToMesh = Chamfer = double.NaN;
            MaxDistanceMeshToReference = MaxDistanceReferenceToMesh = double.NaN;
            OuterShare = OuterDistanceReferenceToMesh = OuterMaxDistanceReferenceToMesh = double.NaN;
            NormalsMeshToReference = NormalsReferenceToMesh = null;
            NormalConsistency = double.NaN;
            SharpEdgeLengthReference = SharpEdgeLengthMesh = double.NaN;
            EdgeDistanceMeshToReference = EdgeDistanceReferenceToMesh = EdgeChamfer = double.NaN;
            EdgePrecision = EdgeRecall = EdgeFScore = null;
            ReferenceQuality = MeshQuality = null;
            ReferenceTopology = MeshTopology = null;
            TopologyChanges = MeshTopologySummary.Changes.None;
        }

        // marks the reference samples on the outer surface (see OuterSurface), from the winding numbers w+, w- at
        // p +- offset n: the surface is a single layer there (|w+ - w-| about 1, not 0 as for two coincident opposite
        // faces) and the rest of the mesh does not enclose it (|w+ + w-| / 2 about 0.5 at the boundary of a solid, 0 at
        // an open sheet, 1 or more at a face inside a solid)
        void classify_outer(SurfaceSample[] samples, Surface reference, double offset)
        {
            reference.PrepareWinding();
            gParallel.BlockStartEnd(0, samples.Length - 1, (start, end) => {
                for (int i = start; i <= end; ++i) {
                    Vector3d p = samples[i].Point, d = offset * samples[i].Normal;
                    double front = reference.Winding(p + d), back = reference.Winding(p - d);
                    samples[i].Outer = Math.Abs(front - back) > 0.5 && Math.Abs(front + back) < 1.5;
                }
            }, 256, Parallel == false);
        }

        /// <summary>
        /// The size used by MeshFidelityMetrics: the diameter of the smallest sphere around the area centroid of the
        /// triangles (centroid) that holds them. Invariant under rotation (unlike the bounding box diagonal); for a
        /// box the same as its diagonal. NaN if the triangles have no area (centroid then the vertex average).
        /// </summary>
        public static double SizeOf(DMesh3 mesh, out Vector3d centroid)
        {
            Vector3d sum = Vector3d.Zero, vsum = Vector3d.Zero;
            double area = 0;
            int nv = 0;
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            foreach (int tid in mesh.TriangleIndices()) {
                mesh.GetTriVertices(tid, ref a, ref b, ref c);
                double w = (b - a).Cross(c - a).Length;
                sum += w * (a + b + c);
                area += w;
                vsum += a + b + c;
                nv += 3;
            }
            if (!(area > 0) || double.IsInfinity(area)) {
                centroid = nv > 0 ? vsum / nv : Vector3d.Zero;
                return double.NaN;
            }
            centroid = sum / (3 * area);
            double r2 = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j)
                    r2 = Math.Max(r2, mesh.GetVertex(t[j]).DistanceSquared(centroid));
            }
            return 2 * Math.Sqrt(r2);
        }

        static bool finite(DMesh3 mesh)
        {
            foreach (int vid in mesh.VertexIndices())
                if (mesh.GetVertex(vid).IsFinite == false)
                    return false;
            return true;
        }

        // a copy with the same ids, moved by -center (exact for scales by powers of two)
        static DMesh3 local_copy(DMesh3 mesh, Vector3d center)
        {
            var copy = new DMesh3(mesh, false, MeshComponents.None);
            foreach (int vid in copy.VertexIndices())
                copy.SetVertex(vid, copy.GetVertex(vid) - center);
            return copy;
        }

        // NaN if either share is (nothing sampled on that side)
        static double fscore(double p, double r)
        {
            if (double.IsNaN(p) || double.IsNaN(r))
                return double.NaN;
            return (p + r > 0) ? 2 * p * r / (p + r) : 0;
        }


        /// <summary> a point on a surface, its unit normal, and the results of the query against the other surface </summary>
        internal struct SurfaceSample
        {
            public Vector3d Point, Normal;
            /// <summary> distance to the other surface, and cos of the angle to its normal there (NaN if no normal) </summary>
            public double Distance, Cos;
            /// <summary> on the boundary of the solid of its mesh (reference samples, if OuterSurface) </summary>
            public bool Outer;
        }

        // n samples, area weighted and stratified: sample i in the slice [i, i + 1) / n of the cumulative area
        // (twice the triangle areas, exact under scaling by powers of two), uniform in its triangle
        internal static SurfaceSample[] sample_surface(DMesh3 mesh, int n, Random rnd, double degenerateArea)
        {
            var tids = new List<int>();
            var cumulative = new List<double>();
            double total = 0;
            foreach (int tid in mesh.TriangleIndices()) {
                double area;
                MeshAnalysisNormals.TriNormal(mesh, tid, out area);
                if (!(area > degenerateArea))
                    continue;       // no reliable normal (and almost no area)
                total += 2 * area;
                tids.Add(tid);
                cumulative.Add(total);
            }
            if (tids.Count == 0 || n <= 0)
                return new SurfaceSample[0];
            var samples = new SurfaceSample[n];
            Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
            for (int i = 0; i < n; ++i) {
                double u = (i + rnd.NextDouble()) / n * total;
                int k = cumulative.BinarySearch(u);
                k = (k < 0) ? ~k : k + 1;           // first entry > u
                if (k >= tids.Count) k = tids.Count - 1;
                mesh.GetTriVertices(tids[k], ref a, ref b, ref c);
                double s = Math.Sqrt(rnd.NextDouble()), t = rnd.NextDouble();
                samples[i].Point = (1 - s) * a + (s * (1 - t)) * b + (s * t) * c;
                samples[i].Normal = MeshAnalysisNormals.TriNormal(mesh, tids[k]);
            }
            return samples;
        }

        void query(SurfaceSample[] samples, Surface target)
        {
            gParallel.BlockStartEnd(0, samples.Length - 1, (start, end) => {
                for (int i = start; i <= end; ++i) {
                    Vector3d closestNormal;
                    samples[i].Distance = target.Distance(samples[i].Point, out closestNormal);
                    samples[i].Cos = (closestNormal == Vector3d.Zero) ? double.NaN : samples[i].Normal.Dot(closestNormal);
                }
            }, 256, Parallel == false);
        }

        static double share_within(SurfaceSample[] samples, double tau)
        {
            if (samples.Length == 0)
                return double.NaN;
            int n = 0;
            foreach (var s in samples)
                if (s.Distance < tau) n++;
            return (double)n / samples.Length;
        }

        static double share_within(double[] distances, double tau)
        {
            if (distances.Length == 0)
                return double.NaN;
            int n = 0;
            foreach (double d in distances)
                if (d < tau) n++;
            return (double)n / distances.Length;
        }

        static double mean_distance(SurfaceSample[] samples)
        {
            double sum = 0;
            foreach (var s in samples)
                sum += s.Distance;
            return samples.Length > 0 ? sum / samples.Length : double.NaN;
        }

        static double max_distance(SurfaceSample[] samples)
        {
            double m = double.NaN;
            foreach (var s in samples)
                if (!(s.Distance <= m)) m = s.Distance;
            return m;
        }

        static double mean(double[] values)
        {
            double sum = 0;
            foreach (double v in values)
                sum += v;
            return values.Length > 0 ? sum / values.Length : double.NaN;
        }


        // distances of EdgeSamples points on the segments edges (stratified by length) to the segments other:
        // empty if edges is empty, infinity if other is
        double[] edge_distances(List<Vector3d> edges, List<Vector3d> other, Random rnd)
        {
            int m = edges.Count / 2;
            if (m == 0 || EdgeSamples <= 0)
                return new double[0];
            double[] cumulative = new double[m];
            double total = 0;
            for (int j = 0; j < m; ++j) {
                total += edges[2 * j].Distance(edges[2 * j + 1]);
                cumulative[j] = total;
            }
            int n = EdgeSamples;
            var points = new Vector3d[n];
            for (int i = 0; i < n; ++i) {
                double u = (i + rnd.NextDouble()) / n * total;
                int k = Array.BinarySearch(cumulative, u);
                k = (k < 0) ? ~k : k + 1;
                if (k >= m) k = m - 1;
                double t = rnd.NextDouble();
                points[i] = (1 - t) * edges[2 * k] + t * edges[2 * k + 1];
            }
            var distances = new double[n];
            if (other.Count == 0) {
                for (int i = 0; i < n; ++i) distances[i] = double.PositiveInfinity;
                return distances;
            }
            var tree = new SegmentTree3(other);
            gParallel.BlockStartEnd(0, n - 1, (start, end) => {
                for (int i = start; i <= end; ++i)
                    distances[i] = Math.Sqrt(tree.NearestDistanceSqr(points[i]));
            }, 256, Parallel == false);
            return distances;
        }

        static double total_length(List<Vector3d> edges)
        {
            double sum = 0;
            for (int j = 0; j + 1 < edges.Count; j += 2)
                sum += edges[j].Distance(edges[j + 1]);
            return sum;
        }


        /// <summary>
        /// The sharp edges of the mesh, as endpoint pairs (a0, b0, a1, b1, ...). Edges are identified by the positions
        /// of their endpoints, so triangle soups (unwelded STL) give the same edges as welded meshes. An edge is
        /// sharp if the angle between the normals of its two triangles exceeds angleDeg, with the normals oriented
        /// consistently across the edge (an orientation flip at the edge is not a crease), or if more than two
        /// triangles share it (non-manifold). Boundary edges and edges at triangles of height at most minHeight
        /// (twice the area over the longest edge; no reliable normal) are not sharp. In the order of first
        /// appearance (triangle order).
        /// </summary>
        internal static List<Vector3d> SharpEdges(DMesh3 mesh, double angleDeg, double minHeight)
        {
            double cosMax = Math.Cos(angleDeg * MathUtil.Deg2Rad);
            var index = new Dictionary<(Vector3d, Vector3d), int>();
            var entries = new List<EdgeEntry>();
            Vector3d[] v = new Vector3d[3];
            foreach (int tid in mesh.TriangleIndices()) {
                Index3i t = mesh.GetTriangle(tid);
                for (int j = 0; j < 3; ++j)
                    v[j] = mesh.GetVertex(t[j]) + Vector3d.Zero;    // -0 to +0, for the hash
                double area;
                Vector3d n = MeshAnalysisNormals.TriNormal(mesh, tid, out area);
                double lmax = Math.Max(v[0].Distance(v[1]), Math.Max(v[1].Distance(v[2]), v[2].Distance(v[0])));
                if (!(2 * area > minHeight * lmax))
                    n = Vector3d.Zero;      // narrower than minHeight: its normal is rounding noise
                for (int j = 0; j < 3; ++j) {
                    Vector3d p = v[j], q = v[(j + 1) % 3];
                    int cmp = p.CompareTo(q);
                    if (cmp == 0)
                        continue;
                    bool forward = cmp < 0;
                    var key = forward ? (p, q) : (q, p);
                    int e;
                    if (index.TryGetValue(key, out e) == false) {
                        index[key] = entries.Count;
                        entries.Add(new EdgeEntry() { A = key.Item1, B = key.Item2, Normal = n, Forward = forward, Count = 1 });
                        continue;
                    }
                    EdgeEntry entry = entries[e];
                    entry.Count++;
                    if (entry.Count == 2) {
                        if (entry.Normal == Vector3d.Zero || n == Vector3d.Zero)
                            entry.Degenerate = true;
                        else {
                            // consistently oriented neighbours traverse the edge in opposite directions
                            Vector3d n2 = (forward == entry.Forward) ? -n : n;
                            entry.Sharp = entry.Normal.Dot(n2) < cosMax;
                        }
                    }
                    entries[e] = entry;
                }
            }
            var result = new List<Vector3d>();
            foreach (EdgeEntry entry in entries) {
                bool sharp = entry.Count > 2 || (entry.Count == 2 && entry.Sharp && entry.Degenerate == false);
                if (sharp) {
                    result.Add(entry.A);
                    result.Add(entry.B);
                }
            }
            return result;
        }

        struct EdgeEntry
        {
            public Vector3d A, B, Normal;
            public bool Forward, Sharp, Degenerate;
            public int Count;
        }


        // closest-point queries on a mesh, with the angle weighted pseudo-normal of the closest feature
        class Surface
        {
            readonly DMesh3 mesh;
            readonly DMeshAABBTree3 tree;
            readonly Vector3d[] faceNormals, vertexNormals;

            public Surface(DMesh3 mesh, double degenerateArea)
            {
                this.mesh = mesh;
                tree = new DMeshAABBTree3(mesh, true);
                faceNormals = new Vector3d[mesh.MaxTriangleID];
                vertexNormals = new Vector3d[mesh.MaxVertexID];
                foreach (int tid in mesh.TriangleIndices()) {
                    double area;
                    Vector3d n = MeshAnalysisNormals.TriNormal(mesh, tid, out area);
                    if (!(area > degenerateArea))
                        continue;
                    faceNormals[tid] = n;
                    Index3i t = mesh.GetTriangle(tid);
                    for (int j = 0; j < 3; ++j) {
                        double angle = MeshAnalysisNormals.InternalAngle(mesh, tid, j);
                        if (double.IsNaN(angle) == false)
                            vertexNormals[t[j]] += angle * n;
                    }
                }
            }

            // builds the winding number cache (before parallel queries)
            public void PrepareWinding() { tree.FastWindingNumber(Vector3d.Zero); }
            public double Winding(Vector3d p) { return tree.FastWindingNumber(p); }

            // distance of p to the mesh, and the unit pseudo-normal at the closest point (zero if none)
            public double Distance(Vector3d p, out Vector3d normal)
            {
                normal = Vector3d.Zero;
                int tid = tree.FindNearestTriangle(p);
                if (tid == DMesh3.InvalidID)
                    return double.PositiveInfinity;
                Index3i tv = mesh.GetTriangle(tid);
                Triangle3d tri = new Triangle3d(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c));
                Vector3d closest, bary;
                double d = Math.Sqrt(DistPoint3Triangle3.DistanceSqr(ref p, ref tri, out closest, out bary));
                normal = MeshAnalysisNormals.Unit(feature_normal(tid, tv, bary));
                return d;
            }

            // the face normal, the sum of both face normals at an edge, or the vertex pseudo-normal at a corner
            Vector3d feature_normal(int tid, Index3i tv, Vector3d bary)
            {
                const double featureTol = 1e-9;
                int zeros = 0, nonzero = -1, zero = -1;
                for (int j = 0; j < 3; ++j) {
                    if (bary[j] <= featureTol) { zeros++; zero = j; } else nonzero = j;
                }
                if (zeros >= 2 && nonzero >= 0)
                    return vertexNormals[tv[nonzero]];
                Vector3d n = faceNormals[tid];
                if (zeros == 1) {
                    int eid = mesh.FindEdge(tv[(zero + 1) % 3], tv[(zero + 2) % 3]);
                    if (eid != DMesh3.InvalidID) {
                        Index2i et = mesh.GetEdgeT(eid);
                        int other = (et.a == tid) ? et.b : et.a;
                        if (other != DMesh3.InvalidID)
                            n += faceNormals[other];
                    }
                }
                if (n == Vector3d.Zero)     // degenerate triangle: the pseudo-normals of its corners
                    n = vertexNormals[tv.a] + vertexNormals[tv.b] + vertexNormals[tv.c];
                return n;
            }
        }
    }


    /// <summary>
    /// Agreement of the normals at samples of one surface with the normals at their closest points on another
    /// (MeshFidelityMetrics). Samples without a normal at the closest point (only degenerate triangles there) are
    /// left out.
    /// </summary>
    public class NormalAgreement
    {
        /// <summary> samples with a normal at the closest point </summary>
        public int Samples;
        /// <summary> mean |cos| of the angle between the normals (1 = parallel, orientation ignored) </summary>
        public double MeanAbsCos = double.NaN;
        /// <summary> mean angle between the normals as lines, acos |cos|, in degrees (0..90) </summary>
        public double MeanAngle = double.NaN;
        /// <summary> samples within the largest threshold of the other surface (the ones that correspond to a point there) </summary>
        public int MatchedSamples;
        /// <summary>
        /// share of the matched samples whose normals point the other way (cos &lt; 0): inverted patches. Samples
        /// farther away are left out, as their closest point need not correspond (eg on a lost part). NaN if none is matched.
        /// </summary>
        public double ReversedFraction = double.NaN;

        internal static NormalAgreement Compute(MeshFidelityMetrics.SurfaceSample[] samples, double matchDistance)
        {
            var r = new NormalAgreement();
            double sumCos = 0, sumAngle = 0;
            int reversed = 0;
            foreach (var s in samples) {
                if (double.IsNaN(s.Cos))
                    continue;
                double c = Math.Min(1, Math.Abs(s.Cos));
                r.Samples++;
                sumCos += c;
                sumAngle += Math.Acos(c) * MathUtil.Rad2Deg;
                if (s.Distance < matchDistance) {
                    r.MatchedSamples++;
                    if (s.Cos < 0) reversed++;
                }
            }
            if (r.Samples > 0) {
                r.MeanAbsCos = sumCos / r.Samples;
                r.MeanAngle = sumAngle / r.Samples;
            }
            if (r.MatchedSamples > 0)
                r.ReversedFraction = (double)reversed / r.MatchedSamples;
            return r;
        }
    }


    // Bounding volume tree over segments (endpoint pairs a0, b0, a1, b1, ...) for exact nearest-segment distances.
    // Built by median splits along the longest axis of the segment centers (ties by index); double precision boxes.
    internal class SegmentTree3
    {
        readonly Vector3d[] a, b;
        readonly int[] order;
        // node k: box, and either children (left, right) or a leaf range [start, start + count) of order
        readonly List<Vector3d> boxMin = new List<Vector3d>(), boxMax = new List<Vector3d>();
        readonly List<int> left = new List<int>(), right = new List<int>(), start = new List<int>(), count = new List<int>();
        const int LeafSize = 4;

        public SegmentTree3(List<Vector3d> endpoints)
        {
            int m = endpoints.Count / 2;
            a = new Vector3d[m]; b = new Vector3d[m];
            order = new int[m];
            for (int j = 0; j < m; ++j) {
                a[j] = endpoints[2 * j]; b[j] = endpoints[2 * j + 1];
                order[j] = j;
            }
            if (m > 0)
                build(0, m);
        }

        int build(int s, int n)
        {
            int node = boxMin.Count;
            Vector3d lo = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue), hi = -lo;
            Vector3d clo = lo, chi = hi;
            for (int i = s; i < s + n; ++i) {
                int j = order[i];
                lo = min(lo, min(a[j], b[j]));
                hi = max(hi, max(a[j], b[j]));
                Vector3d c = 0.5 * (a[j] + b[j]);
                clo = min(clo, c); chi = max(chi, c);
            }
            boxMin.Add(lo); boxMax.Add(hi);
            left.Add(-1); right.Add(-1); start.Add(s); count.Add(n);
            if (n <= LeafSize)
                return node;
            Vector3d ext = chi - clo;
            int axis = (ext.x >= ext.y && ext.x >= ext.z) ? 0 : (ext.y >= ext.z ? 1 : 2);
            Array.Sort(order, s, n, Comparer<int>.Create((x, y) => {
                double cx = a[x][axis] + b[x][axis], cy = a[y][axis] + b[y][axis];
                return cx < cy ? -1 : (cx > cy ? 1 : x.CompareTo(y));
            }));
            int half = n / 2;
            int l = build(s, half);
            int r = build(s + half, n - half);
            left[node] = l; right[node] = r;
            return node;
        }

        static Vector3d min(Vector3d u, Vector3d v) { return new Vector3d(Math.Min(u.x, v.x), Math.Min(u.y, v.y), Math.Min(u.z, v.z)); }
        static Vector3d max(Vector3d u, Vector3d v) { return new Vector3d(Math.Max(u.x, v.x), Math.Max(u.y, v.y), Math.Max(u.z, v.z)); }

        public double NearestDistanceSqr(Vector3d p)
        {
            double best = double.PositiveInfinity;
            if (order.Length == 0)
                return best;
            var stack = new Stack<int>();
            stack.Push(0);
            while (stack.Count > 0) {
                int node = stack.Pop();
                if (box_distance_sqr(node, p) >= best)
                    continue;
                if (left[node] < 0) {
                    for (int i = start[node]; i < start[node] + count[node]; ++i) {
                        double d = segment_distance_sqr(p, a[order[i]], b[order[i]]);
                        if (d < best) best = d;
                    }
                    continue;
                }
                int l = left[node], r = right[node];
                // nearer child last, so it is visited first
                if (box_distance_sqr(l, p) < box_distance_sqr(r, p)) { stack.Push(r); stack.Push(l); } else { stack.Push(l); stack.Push(r); }
            }
            return best;
        }

        double box_distance_sqr(int node, Vector3d p)
        {
            Vector3d lo = boxMin[node], hi = boxMax[node];
            double d = 0;
            for (int k = 0; k < 3; ++k) {
                double e = (p[k] < lo[k]) ? lo[k] - p[k] : (p[k] > hi[k] ? p[k] - hi[k] : 0);
                d += e * e;
            }
            return d;
        }

        internal static double segment_distance_sqr(Vector3d p, Vector3d a, Vector3d b)
        {
            Vector3d d = b - a;
            double len2 = d.LengthSquared;
            double t = len2 > 0 ? MathUtil.Clamp((p - a).Dot(d) / len2, 0, 1) : 0;
            return p.DistanceSquared(a + t * d);
        }
    }
}
