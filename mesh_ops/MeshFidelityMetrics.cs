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
    ///     samples within the largest tau whose normals point the other way (cos &lt; -0.001): inverted patches;
    ///   - Edge Chamfer distance (as in Chen et al., "Neural Dual Contouring", SIGGRAPH 2022): Chamfer distance
    ///     between the sharp edges (dihedral angle above SharpEdgeAngle, not at slivers narrower than
    ///     SharpEdgeMinHeight) of both meshes, and the edge
    ///     precision / recall at the thresholds: feature preservation;
    ///   - the same against the outer surface of the reference only (OuterSurface), so interior faces that a
    ///     repair to a solid drops on purpose do not count as lost;
    ///   - volume IoU of the solids (generalized winding number &gt; 0.5, Jacobson et al. 2013), against the whole
    ///     reference and against the solid of its outer faces only (OuterVolumeIoU): interior faces, nested shells
    ///     and double-sided sheets distort the winding number solid of an open reference, not the solid a repair
    ///     encloses (VolumeSamples);
    ///   - curvature fidelity (Curvature): discrete mean and Gaussian curvature after Meyer et al. 2003
    ///     (MeshCurvature) at corresponding points of both surfaces, and the distance of their distributions;
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
        /// sheet), or one side is outside and the other inside the solid (|w| &lt; 0.25 and &gt; 0.75: coincident duplicate
        /// shells, w 0 and 2). Leaves out faces inside the solid (single faces, nested shells: mean 1 or more), internal
        /// double walls and double-sided sheets (jump 0), which a repair to a solid drops on purpose; keeps cavity
        /// walls, duplicated outer shells and open sheets. Orient inside-out components of the reference first (eg WindingContouring.OrientedCopy).
        /// </summary>
        public bool OuterSurface = true;
        /// <summary>
        /// offset of the two winding number tests of OuterSurface, relative to Size: both faces of a wall thinner than
        /// this, and faces across a gap narrower than this, are not outer
        /// </summary>
        public double OuterOffset = 1e-5;
        /// <summary>
        /// random points for the volume IoUs (VolumeIoU, OuterVolumeIoU; sampled as MeshRepairMetrics.VolumeIoU does, near
        /// the components of both meshes); 0 skips them (NaN). Sampling noise of the IoU about sqrt((1 - IoU) / n).
        /// </summary>
        public int VolumeSamples = 100000;
        /// <summary>
        /// compare the curvatures (MeshCurvature) of both meshes: at each surface sample within the largest threshold of
        /// the other surface, the mean curvature H and the Gaussian curvature K interpolated linearly from the Valid
        /// vertices of its triangle, against the same at its closest point on the other mesh (CurvatureMeshToReference,
        /// CurvatureReferenceToMesh); the 1-Wasserstein distance of the area weighted distributions of H and K of both
        /// meshes (MeanCurvatureDistance, GaussianCurvatureDistance); the total absolute curvatures. H is relative to
        /// 1 / Size (H * Size: 2 on a sphere), K to 1 / Size^2. Boundary, bowtie and area-less vertices are not Valid
        /// and left out of the interpolation; a sample whose triangle has no Valid vertex is left out. At sharp edges
        /// and corners the vertex values grow with the resolution (the curvature is concentrated there) while the area
        /// they spread over shrinks, so the means (area integrals) stay comparable, but two meshes with the same crease
        /// at different resolutions differ there. H is signed by the orientation, as the reversed share.
        /// </summary>
        public bool Curvature = true;
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
        /// <summary>
        /// output (OuterSurface): per threshold, share of the reference's outer sharp edges within tau of the mesh's
        /// sharp edges (NaN if the reference has none), and the share of the reference's sharp edges that are outer:
        /// edges inside the solid (eg of overlapping parts that a union removes) left out, by the same winding
        /// number rule as OuterSurface, tested along the edge's mean normal
        /// </summary>
        public double[] OuterEdgeRecall;
        public double OuterEdgeShare = double.NaN;
        /// <summary>
        /// output (VolumeSamples): volume IoU of the solid of the reference (generalized winding number &gt; 0.5; the reference
        /// reversed as a whole if its signed volume is negative, but its components are not oriented by their nesting as in
        /// MeshRepairMetrics.VolumeIoU: a shell nested in another with the same orientation stays a solid part, not a
        /// cavity; orient the reference first if that is wanted, eg WindingContouring.OrientedCopy) and of the mesh,
        /// and the parts of the reference's solid missing in the mesh and the mesh's solid not in the reference, relative
        /// to the reference's solid. NaN (all three) if the reference encloses no volume (eg an open sheet); 0 (missing and
        /// extra NaN) if its solid is too small to be hit by the samples
        /// </summary>
        public double VolumeIoU = double.NaN, VolumeMissing = double.NaN, VolumeExtra = double.NaN;
        /// <summary>
        /// output (VolumeSamples and OuterSurface): the same against the solid of the (oriented) reference's outer faces
        /// only: a face is outer unless none of its centroid and its three points (4 a + b + c) / 6 (and permutations) is
        /// on the outer surface by the rule of OuterSurface. Interior faces raise or lower each other's winding numbers
        /// (of two layers stacked inside a solid the lower one looks like a boundary while the upper one is there, and
        /// with many layers a face of the solid itself looks interior), so the faces are removed in rounds, each judging
        /// the faces left by the winding number of the faces left and removing only the clearest interior ones: double
        /// layers (no jump of the winding number at any test point) and faces that are not outer with a mean winding
        /// number (the lowest of their test points) of at least 0.75 and within 0.25 of the largest such mean. Removed
        /// faces stay removed; the rounds end when one removes nothing (OuterFacesConverged; at most MaxOuterFaceRounds,
        /// OuterFaceRounds). Faces that are not outer with a mean below 0.75 (eg lowered by interior faces in front of
        /// them) are not removed in that round. So faces inside the solid, nested shells, internal double walls and
        /// double-sided sheets are left out, faces crossing another surface (partly outer) and degenerate ones kept. NaN
        /// if no face is outer or the faces enclose no volume. Cost: a winding number tree and up to 8 winding numbers per
        /// face per round; a few rounds, about one per layer of a stack of interior faces
        /// </summary>
        public double OuterVolumeIoU = double.NaN, OuterVolumeMissing = double.NaN, OuterVolumeExtra = double.NaN;
        /// <summary> output (VolumeSamples and OuterSurface): share of the reference's area in the outer faces of OuterVolumeIoU </summary>
        public double OuterFaceShare = double.NaN;
        /// <summary>
        /// output (VolumeSamples and OuterSurface): rounds of the outer face classification, and whether the set of faces
        /// stopped changing within MaxOuterFaceRounds (if not, the faces of the last round are used)
        /// </summary>
        public int OuterFaceRounds;
        public bool OuterFacesConverged;
        /// <summary> output (Curvature): curvature differences at the samples of the mesh against the reference, and of the reference against the mesh </summary>
        public CurvatureAgreement CurvatureMeshToReference, CurvatureReferenceToMesh;
        /// <summary> output (Curvature): mean of both directions of CurvatureAgreement.MeanError and of GaussianError </summary>
        public double MeanCurvatureError = double.NaN, GaussianCurvatureError = double.NaN;
        /// <summary>
        /// output (Curvature): 1-Wasserstein distance (mean absolute difference of the quantile functions) of the area
        /// weighted distributions of H * Size, and of K * Size^2, of both meshes (from the values at all their surface
        /// samples): independent of where the curvature is, NaN if either mesh has no sample with a value
        /// </summary>
        public double MeanCurvatureDistance = double.NaN, GaussianCurvatureDistance = double.NaN;
        /// <summary>
        /// output (Curvature): total absolute mean curvature, the sum of |H| A_M over the Valid vertices relative to Size
        /// (2 pi on a sphere; at a crease of dihedral angle theta and length L about theta L / (2 Size)), and total absolute
        /// Gaussian curvature, the sum of |angle deficit| over the Valid vertices (4 pi on a convex closed surface), of
        /// both meshes: they do not depend on the resolution, also at sharp features
        /// </summary>
        public double TotalAbsMeanCurvatureReference = double.NaN, TotalAbsMeanCurvatureMesh = double.NaN;
        public double TotalAbsGaussianCurvatureReference = double.NaN, TotalAbsGaussianCurvatureMesh = double.NaN;
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
            var refEdgeNormals = new List<Vector3d>();
            List<Vector3d> refEdges = SharpEdges(reference, SharpEdgeAngle, SharpEdgeMinHeight * size, refEdgeNormals);
            List<Vector3d> meshEdges = SharpEdges(mesh, SharpEdgeAngle, SharpEdgeMinHeight * size);
            SharpEdgeLengthReference = total_length(refEdges) / size;
            SharpEdgeLengthMesh = total_length(meshEdges) / size;
            double[] fromMesh = edge_distances(meshEdges, refEdges, rnd);
            if (CancelF())
                return fail();
            Vector3d[] refEdgePoints; int[] refEdgeSegments;
            double[] fromRef = edge_distances(refEdges, meshEdges, rnd, out refEdgePoints, out refEdgeSegments);
            double[] fromOuterRef = OuterSurface ? outer_edge_distances(fromRef, refEdgePoints, refEdgeSegments, refEdgeNormals, refSurface, OuterOffset * size)
                : new double[0];
            if (CancelF())
                return fail();
            EdgePrecision = new double[nt]; EdgeRecall = new double[nt]; EdgeFScore = new double[nt]; OuterEdgeRecall = new double[nt];
            if (OuterSurface)
                OuterEdgeShare = fromRef.Length > 0 ? (double)fromOuterRef.Length / fromRef.Length : double.NaN;
            for (int k = 0; k < nt; ++k) {
                EdgePrecision[k] = share_within(fromMesh, tau[k]);
                EdgeRecall[k] = share_within(fromRef, tau[k]);
                OuterEdgeRecall[k] = OuterSurface ? share_within(fromOuterRef, tau[k]) : double.NaN;
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

            // volume IoUs, of the whole reference's solid and of its outer faces' (own random sequence, so the other
            // outputs do not depend on VolumeSamples)
            if (VolumeSamples > 0) {
                volume_ious(reference, mesh, meshSurface, size, degenerateArea);
                if (CancelF())
                    return fail();
            }

            // curvatures
            if (Curvature) {
                curvature(reference, mesh, onRef, onMesh, size, maxTau);
                if (CancelF())
                    return fail();
            }

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
            EdgePrecision = EdgeRecall = EdgeFScore = OuterEdgeRecall = null;
            OuterEdgeShare = double.NaN;
            VolumeIoU = VolumeMissing = VolumeExtra = double.NaN;
            OuterVolumeIoU = OuterVolumeMissing = OuterVolumeExtra = OuterFaceShare = double.NaN;
            OuterFaceRounds = 0;
            OuterFacesConverged = false;
            CurvatureMeshToReference = CurvatureReferenceToMesh = null;
            MeanCurvatureError = GaussianCurvatureError = MeanCurvatureDistance = GaussianCurvatureDistance = double.NaN;
            TotalAbsMeanCurvatureReference = TotalAbsMeanCurvatureMesh = double.NaN;
            TotalAbsGaussianCurvatureReference = TotalAbsGaussianCurvatureMesh = double.NaN;
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
                    samples[i].Outer = is_outer(front, back);
                }
            }, 256, Parallel == false);
        }

        // VolumeIoU (solid of the whole reference) and OuterVolumeIoU (solid of its outer faces) against the mesh's solid;
        // the reference reversed as a whole if its signed volume is negative
        void volume_ious(DMesh3 reference, DMesh3 mesh, Surface meshSurface, double size, double degenerateArea)
        {
            DMesh3 oriented = reference;
            if (MeshMeasurements.VolumeArea(reference, reference.TriangleIndices(), reference.GetVertex).x < 0) {
                oriented = new DMesh3(reference, false, MeshComponents.None);
                oriented.ReverseOrientation(false);
            }
            var refTree = new DMeshAABBTree3(oriented, true);
            refTree.FastWindingNumber(Vector3d.Zero);       // cache before parallel use
            meshSurface.PrepareWinding();
            Func<Vector3d, bool> inMesh = (p) => meshSurface.Winding(p) > 0.5;
            Func<Vector3d, bool> inRef = (p) => refTree.FastWindingNumber(p) > 0.5;
            bool serial = Parallel == false;
            VolumeIoU = iou(oriented, inRef, mesh, inMesh, serial, out VolumeMissing, out VolumeExtra);
            if (OuterSurface == false)
                return;
            double outerArea;
            DMesh3 outer = OuterFaces(oriented, refTree, OuterOffset * size, degenerateArea, serial, out outerArea);
            double area = 0;
            foreach (int tid in oriented.TriangleIndices())
                area += oriented.GetTriArea(tid);
            OuterFaceShare = area > 0 ? outerArea / area : double.NaN;
            if (outer.TriangleCount == 0)
                return;
            var outerTree = new DMeshAABBTree3(outer, true);
            outerTree.FastWindingNumber(Vector3d.Zero);
            OuterVolumeIoU = iou(outer, (p) => outerTree.FastWindingNumber(p) > 0.5, mesh, inMesh, serial, out OuterVolumeMissing, out OuterVolumeExtra);
        }

        // as MeshRepairMetrics.VolumeIoU: NaN (missing, extra NaN) if the reference encloses nothing, 0 (missing, extra
        // NaN) if its solid is too small to be hit by the samples
        double iou(DMesh3 reference, Func<Vector3d, bool> inRef, DMesh3 mesh, Func<Vector3d, bool> inMesh, bool serial, out double missing, out double extra)
        {
            double v = MeshRepairMetrics.volume_iou(inRef, MeshRepairMetrics.sample_region(reference, mesh), inMesh,
                out missing, out extra, VolumeSamples, Seed, serial);
            if (double.IsNaN(v)) {
                missing = extra = double.NaN;
                if (MeshRepairMetrics.has_solid(inRef, reference, Seed))
                    v = 0;
            }
            return v;
        }

        /// <summary> OuterVolumeIoU judges the reference's faces in at most this many rounds (see OuterFacesConverged) </summary>
        public const int MaxOuterFaceRounds = 64;

        // a copy of the reference without the faces that are not outer (see OuterVolumeIoU), and the area kept. Each round
        // judges the faces left against the winding number of the faces left, and removes the clearest interior ones
        // only: double layers, and faces whose mean winding number is at least 0.75 and within 0.25 of the largest one
        // (the innermost of a stack of interior faces raise the mean of every face around them, a face of the solid's
        // boundary among them included; removing them first lets the others be judged without them). Removed faces stay
        // removed; until a round removes nothing
        DMesh3 OuterFaces(DMesh3 reference, DMeshAABBTree3 referenceTree, double offset, double degenerateArea, bool serial, out double keptArea)
        {
            var left = new List<int>();
            foreach (int tid in reference.TriangleIndices()) {
                double area;
                MeshAnalysisNormals.TriNormal(reference, tid, out area);
                if (area > degenerateArea)
                    left.Add(tid);      // to be judged; degenerate ones have no normal to test along and stay
            }
            DMesh3 copy = new DMesh3(reference, false, MeshComponents.None);
            Func<Vector3d, double> winding = referenceTree.FastWindingNumber;
            OuterFaceRounds = 0;
            OuterFacesConverged = left.Count == 0;
            while (left.Count > 0 && OuterFaceRounds < MaxOuterFaceRounds) {
                OuterFaceRounds++;
                int[] cand = left.ToArray();
                // per face: outer; a double layer (no jump at any test point); the lowest mean winding number of its points
                bool[] outer = new bool[cand.Length], twin = new bool[cand.Length];
                double[] mean = new double[cand.Length];
                Func<Vector3d, double> w = winding;
                gParallel.BlockStartEnd(0, cand.Length - 1, (start, end) => {
                    Vector3d a = Vector3d.Zero, b = Vector3d.Zero, c = Vector3d.Zero;
                    for (int i = start; i <= end; ++i) {
                        Vector3d d = offset * MeshAnalysisNormals.TriNormal(reference, cand[i]);
                        reference.GetTriVertices(cand[i], ref a, ref b, ref c);
                        bool o = false, flat = true;
                        double m = double.MaxValue;
                        for (int k = 0; k < 4 && o == false; ++k) {
                            Vector3d p = (k == 0) ? (a + b + c) / 3 : (k == 1) ? (4 * a + b + c) / 6 : (k == 2) ? (a + 4 * b + c) / 6 : (a + b + 4 * c) / 6;
                            double wf = w(p + d), wb = w(p - d);
                            o = is_outer(wf, wb);
                            flat = flat && Math.Abs(wf - wb) <= 0.5;
                            m = Math.Min(m, 0.5 * (wf + wb));
                        }
                        outer[i] = o; twin[i] = flat; mean[i] = m;
                    }
                }, 256, serial);
                double maxMean = double.NegativeInfinity;
                for (int i = 0; i < cand.Length; ++i)
                    if (outer[i] == false && twin[i] == false) maxMean = Math.Max(maxMean, mean[i]);
                double threshold = Math.Max(0.75, maxMean - 0.25);
                left.Clear();
                int removed = 0;
                for (int i = 0; i < cand.Length; ++i) {
                    if (outer[i] == false && (twin[i] || mean[i] >= threshold)) {
                        copy.RemoveTriangle(cand[i], false, false);
                        removed++;
                    } else
                        left.Add(cand[i]);
                }
                if (removed == 0 || left.Count == 0) {
                    OuterFacesConverged = true;
                    break;
                }
                var tree = new DMeshAABBTree3(copy, true);
                tree.FastWindingNumber(Vector3d.Zero);      // cache before parallel use
                winding = tree.FastWindingNumber;
            }
            keptArea = 0;
            foreach (int tid in copy.TriangleIndices())
                keptArea += copy.GetTriArea(tid);
            return copy;
        }

        // the curvature outputs (see Curvature)
        void curvature(DMesh3 reference, DMesh3 mesh, SurfaceSample[] onRef, SurfaceSample[] onMesh, double size, double matchDistance)
        {
            MeshCurvature cr = MeshCurvature.Compute(reference), cm = MeshCurvature.Compute(mesh);
            double[] hm, km, hr, kr;
            CurvatureMeshToReference = CurvatureAgreement.Compute(onMesh, mesh, cm, reference, cr, size, matchDistance, out hm, out km);
            CurvatureReferenceToMesh = CurvatureAgreement.Compute(onRef, reference, cr, mesh, cm, size, matchDistance, out hr, out kr);
            MeanCurvatureError = 0.5 * (CurvatureMeshToReference.MeanError + CurvatureReferenceToMesh.MeanError);
            GaussianCurvatureError = 0.5 * (CurvatureMeshToReference.GaussianError + CurvatureReferenceToMesh.GaussianError);
            MeanCurvatureDistance = Wasserstein1(hm, hr);
            GaussianCurvatureDistance = Wasserstein1(km, kr);
            total_curvature(reference, cr, size, out TotalAbsMeanCurvatureReference, out TotalAbsGaussianCurvatureReference);
            total_curvature(mesh, cm, size, out TotalAbsMeanCurvatureMesh, out TotalAbsGaussianCurvatureMesh);
        }

        static void total_curvature(DMesh3 mesh, MeshCurvature c, double size, out double mean, out double gaussian)
        {
            mean = gaussian = 0;
            foreach (int vid in mesh.VertexIndices()) {
                if (c.Valid[vid] == false)
                    continue;
                mean += Math.Abs(c.Mean[vid]) * c.MixedArea[vid];
                gaussian += Math.Abs(c.AngleDeficit[vid]);
            }
            mean /= size;
        }

        /// <summary>
        /// 1-Wasserstein distance of the empirical distributions of a and b (each value of a weighing 1 / a.Length, of b
        /// 1 / b.Length): the integral of |F_a - F_b|. NaN if either is empty. Sorts both arrays.
        /// </summary>
        internal static double Wasserstein1(double[] a, double[] b)
        {
            if (a.Length == 0 || b.Length == 0)
                return double.NaN;
            Array.Sort(a);
            Array.Sort(b);
            int i = 0, j = 0;
            double x = Math.Min(a[0], b[0]), sum = 0;
            while (i < a.Length || j < b.Length) {
                bool fromA = j >= b.Length || (i < a.Length && a[i] <= b[j]);
                double next = fromA ? a[i] : b[j];
                // half the gaps: a gap between values of opposite sign can exceed double.MaxValue
                sum += Math.Abs((double)i / a.Length - (double)j / b.Length) * (0.5 * next - 0.5 * x);
                x = next;
                if (fromA) i++; else j++;
            }
            return 2 * sum;
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
            /// <summary> the triangle the sample lies in and its barycentric coordinates there </summary>
            public int Tid;
            public Vector3d Bary;
            /// <summary> the triangle of the closest point on the other surface and its barycentric coordinates (Tid -1 if none) </summary>
            public int ClosestTid;
            public Vector3d ClosestBary;
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
                samples[i].Tid = tids[k];
                samples[i].Bary = new Vector3d(1 - s, s * (1 - t), s * t);
            }
            return samples;
        }

        void query(SurfaceSample[] samples, Surface target)
        {
            gParallel.BlockStartEnd(0, samples.Length - 1, (start, end) => {
                for (int i = start; i <= end; ++i) {
                    Vector3d closestNormal;
                    samples[i].Distance = target.Distance(samples[i].Point, out closestNormal, out samples[i].ClosestTid, out samples[i].ClosestBary);
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
            Vector3d[] points; int[] segments;
            return edge_distances(edges, other, rnd, out points, out segments);
        }

        // also the sample points and the index of the segment each lies on
        double[] edge_distances(List<Vector3d> edges, List<Vector3d> other, Random rnd, out Vector3d[] points, out int[] segments)
        {
            int m = edges.Count / 2;
            points = new Vector3d[0]; segments = new int[0];
            if (m == 0 || EdgeSamples <= 0)
                return new double[0];
            double[] cumulative = new double[m];
            double total = 0;
            for (int j = 0; j < m; ++j) {
                total += edges[2 * j].Distance(edges[2 * j + 1]);
                cumulative[j] = total;
            }
            int n = EdgeSamples;
            points = new Vector3d[n];
            segments = new int[n];
            for (int i = 0; i < n; ++i) {
                double u = (i + rnd.NextDouble()) / n * total;
                int k = Array.BinarySearch(cumulative, u);
                k = (k < 0) ? ~k : k + 1;
                if (k >= m) k = m - 1;
                double t = rnd.NextDouble();
                points[i] = (1 - t) * edges[2 * k] + t * edges[2 * k + 1];
                segments[i] = k;
            }
            var distances = new double[n];
            if (other.Count == 0) {
                for (int i = 0; i < n; ++i) distances[i] = double.PositiveInfinity;
                return distances;
            }
            var tree = new SegmentTree3(other);
            Vector3d[] pts = points;
            gParallel.BlockStartEnd(0, n - 1, (start, end) => {
                for (int i = start; i <= end; ++i)
                    distances[i] = Math.Sqrt(tree.NearestDistanceSqr(pts[i]));
            }, 256, Parallel == false);
            return distances;
        }

        // the outer surface rule (see OuterSurface) on the winding numbers on both sides; also outer: one side outside
        // and the other inside the solid, eg coincident duplicate shells (w 0 and 2)
        static bool is_outer(double front, double back)
        {
            if (Math.Abs(front - back) > 0.5 && Math.Abs(front + back) < 1.5)
                return true;
            double lo = Math.Min(Math.Abs(front), Math.Abs(back)), hi = Math.Max(Math.Abs(front), Math.Abs(back));
            return lo < 0.25 && hi > 0.75;
        }

        // the distances of the edge samples on the reference's outer surface (see OuterSurface), tested at
        // p +- offset n along the edge's mean normal
        double[] outer_edge_distances(double[] distances, Vector3d[] points, int[] segments, List<Vector3d> normals,
            Surface reference, double offset)
        {
            reference.PrepareWinding();
            bool[] outer = new bool[distances.Length];
            gParallel.BlockStartEnd(0, distances.Length - 1, (start, end) => {
                for (int i = start; i <= end; ++i) {
                    Vector3d d = offset * normals[segments[i]];
                    if (d == Vector3d.Zero) continue;
                    double front = reference.Winding(points[i] + d), back = reference.Winding(points[i] - d);
                    outer[i] = is_outer(front, back);
                }
            }, 256, Parallel == false);
            var result = new List<double>();
            for (int i = 0; i < distances.Length; ++i)
                if (outer[i]) result.Add(distances[i]);
            return result.ToArray();
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
        /// (twice the area over the longest edge; no reliable normal) are not sharp; non-manifold edges are always sharp. In the order of first
        /// appearance (triangle order). normals (if given) gets the unit sum of the oriented normals of the first two
        /// faces of each sharp edge (zero if one is degenerate). At an edge where two solids only touch (non-manifold,
        /// the first two faces from one solid) that direction points into the other solid, and the edge is not outer.
        /// </summary>
        internal static List<Vector3d> SharpEdges(DMesh3 mesh, double angleDeg, double minHeight, List<Vector3d> normals = null)
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
                            entry.Normal2 = n2;
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
                    if (normals != null)
                        normals.Add(MeshAnalysisNormals.Unit(entry.Normal + entry.Normal2));
                }
            }
            return result;
        }

        struct EdgeEntry
        {
            public Vector3d A, B, Normal, Normal2;
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

            // distance of p to the mesh, the unit pseudo-normal at the closest point (zero if none), and the closest
            // point's triangle (-1 if none) and barycentric coordinates
            public double Distance(Vector3d p, out Vector3d normal, out int tid, out Vector3d bary)
            {
                normal = bary = Vector3d.Zero;
                tid = tree.FindNearestTriangle(p);
                if (tid == DMesh3.InvalidID)
                    return double.PositiveInfinity;
                Index3i tv = mesh.GetTriangle(tid);
                Triangle3d tri = new Triangle3d(mesh.GetVertex(tv.a), mesh.GetVertex(tv.b), mesh.GetVertex(tv.c));
                Vector3d closest;
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
        /// share of the matched samples whose normals point the other way (cos &lt; -ReversedCos): inverted patches. Samples
        /// farther away are left out, as their closest point need not correspond (eg on a lost part). NaN if none is matched.
        /// </summary>
        public double ReversedFraction = double.NaN;

        /// <summary>
        /// a normal is reversed if cos &lt; -ReversedCos (angle above 90.06 degrees): perpendicular normals (eg a face
        /// ending on another one) have a cos of 0 up to rounding, whose sign must not count
        /// </summary>
        public const double ReversedCos = 1e-3;

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
                    if (s.Cos < -ReversedCos) reversed++;
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


    /// <summary>
    /// Agreement of the curvatures (MeshCurvature) at samples of one surface with those at their closest points on another
    /// (MeshFidelityMetrics.Curvature). Only samples within the largest threshold of the other surface count (farther
    /// ones need not correspond, eg on a lost part, which the F-score shows), and only where both values exist.
    /// </summary>
    public class CurvatureAgreement
    {
        /// <summary> samples compared </summary>
        public int Samples;
        /// <summary> mean |H - H'| * Size over the samples (H at the sample, H' at its closest point); NaN if none </summary>
        public double MeanError = double.NaN;
        /// <summary> mean |K - K'| * Size^2 over the samples; NaN if none </summary>
        public double GaussianError = double.NaN;
        /// <summary> mean |H| * Size and |K| * Size^2 at the compared samples (the scale of the errors) </summary>
        public double MeanAbs = double.NaN, GaussianAbs = double.NaN;

        // values interpolated from the Valid vertices of triangle tid (NaN if it has none)
        internal static bool interpolate(DMesh3 mesh, MeshCurvature c, int tid, Vector3d bary, out double h, out double k)
        {
            h = k = double.NaN;
            if (tid < 0)
                return false;
            Index3i t = mesh.GetTriangle(tid);
            double w = 0, sh = 0, sk = 0;
            for (int j = 0; j < 3; ++j) {
                if (c.Valid[t[j]] == false)
                    continue;
                double bj = Math.Max(0, bary[j]);
                w += bj; sh += bj * c.Mean[t[j]]; sk += bj * c.Gaussian[t[j]];
            }
            if (!(w > 0)) {
                // the sample at a corner whose vertex is not Valid: the mean of the Valid others
                for (int j = 0; j < 3; ++j)
                    if (c.Valid[t[j]]) { w += 1; sh += c.Mean[t[j]]; sk += c.Gaussian[t[j]]; }
                if (!(w > 0))
                    return false;
            }
            h = sh / w; k = sk / w;
            return true;
        }

        // also the normalized values at all samples that have one (own triangle), for the distributions
        internal static CurvatureAgreement Compute(MeshFidelityMetrics.SurfaceSample[] samples, DMesh3 mesh, MeshCurvature c,
            DMesh3 other, MeshCurvature co, double size, double matchDistance, out double[] h, out double[] k)
        {
            var r = new CurvatureAgreement();
            var hl = new List<double>();
            var kl = new List<double>();
            double s2 = size * size, eh = 0, ek = 0, ah = 0, ak = 0;
            foreach (var s in samples) {
                double h0, k0, h1, k1;
                if (interpolate(mesh, c, s.Tid, s.Bary, out h0, out k0) == false)
                    continue;
                h0 *= size; k0 *= s2;
                if (double.IsInfinity(h0) || double.IsInfinity(k0))
                    continue;       // overflow of the normalization (a vertex area below 1e-308 of Size^2): no value
                hl.Add(h0); kl.Add(k0);
                if (!(s.Distance < matchDistance) || interpolate(other, co, s.ClosestTid, s.ClosestBary, out h1, out k1) == false)
                    continue;
                h1 *= size; k1 *= s2;
                if (double.IsInfinity(h1) || double.IsInfinity(k1))
                    continue;
                r.Samples++;
                eh += Math.Abs(h0 - h1); ek += Math.Abs(k0 - k1);
                ah += Math.Abs(h0); ak += Math.Abs(k0);
            }
            if (r.Samples > 0) {
                r.MeanError = eh / r.Samples; r.GaussianError = ek / r.Samples;
                r.MeanAbs = ah / r.Samples; r.GaussianAbs = ak / r.Samples;
            }
            h = hl.ToArray(); k = kl.ToArray();
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
