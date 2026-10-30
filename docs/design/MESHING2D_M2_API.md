# M2 public API proposal for owner review

**Owner note (2026-10-30): the approvals recorded here are disputed.** Where this document says the owner approved something, or calls the API frozen, that is the record of an earlier session. The owner says this is not really the case: whether these domain decisions are the right ones must be questioned. Treat them as proposals.

Status: **proposal, not implemented** (2026-10-30). This is the detailed interface review
supplementing [MESHING2D_M2.md](MESHING2D_M2.md). Existing M1 and upstream 1.0.324 APIs remain unchanged.
The owner approved pre-clipping normalization, full and explicit-only provenance lists, caller IDs/markers,
seed-only holes, default/span/diagnostic/witness conventions and cancellation on witness helpers.
Signatures below describe the frozen public surface, not compilable implementation. This approval is not a
bounds-review result or production acceptance.

## 1. Exact values and identity

```csharp
public readonly struct ExactRational2
{
    public ExactRational2(BigInteger numerator, BigInteger denominator);
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public static ExactRational2 FromDouble(double value);
    public double ToDisplayDouble();
}

public readonly struct ExactPlanarPoint2
{
    public ExactPlanarPoint2(BigInteger xNumerator, BigInteger yNumerator,
        BigInteger denominator);
    public BigInteger XNumerator { get; }
    public BigInteger YNumerator { get; }
    public BigInteger Denominator { get; }
    public static ExactPlanarPoint2 FromDouble(Vector2d point);
    public Vector2d ToDisplayDouble();
}
```

Denominators are positive and representations reduced. A zero supplied denominator throws
`ArgumentException`; default values represent exact zero (point origin), with public denominator 1.
Finite doubles are converted from their bits, including subnormals and signed zero. Equality and ordering
must be exact; the required equality interfaces/operators are to be included consistently with existing
library value types. Display conversion is round-to-nearest, ties-to-even, and is not topology certification.
Arbitrary user-created rationals outside the finite double range may display as infinity; mesh-produced
coordinates must display finite. No approximate constructor establishes an exact intersection.

Input list indices, never caller IDs, are used by segments, rings and topology. Input slots retain original
coordinates; the map points aliases to their surviving representative. New exact nodes follow reserved
input slots in canonical coordinate order. Caller IDs and original point markers are retained in the owned
original input. Default caller IDs are input indices and default point markers are zero. Output `VertexIds`
retain the original ID in every reserved input slot and are null for generated nodes; duplicate caller IDs
are allowed and never used for endpoint lookup. Output `VertexMarkers` keep original markers in alias slots;
the survivor keeps its own nonzero marker, or inherits the first nonzero alias marker in input order.
Generated nodes use the sole distinct nonzero incident source marker, or zero on conflict; full source
provenance remains available independently of the scalar marker.

## 2. Input and options

```csharp
public sealed class PlanarConstraintInput2
{
    public PlanarConstraintInput2(IReadOnlyList<Vector2d> points,
        IReadOnlyList<ConstraintSegment2> segments,
        IReadOnlyList<ConstraintRing2> rings = null,
        IReadOnlyList<ConstraintRegion2> regions = null,
        IReadOnlyList<int> pointIds = null,
        IReadOnlyList<int> pointMarkers = null,
        IReadOnlyList<ConstraintHoleSeed2> holeSeeds = null);
    public IReadOnlyList<Vector2d> Points { get; }
    public IReadOnlyList<ConstraintSegment2> Segments { get; }
    public IReadOnlyList<ConstraintRing2> Rings { get; }
    public IReadOnlyList<ConstraintRegion2> Regions { get; }
    public IReadOnlyList<int> PointIds { get; }
    public IReadOnlyList<int> PointMarkers { get; }
    public IReadOnlyList<ConstraintHoleSeed2> HoleSeeds { get; }
}
public sealed class ConstraintHoleSeed2
{
    public ConstraintHoleSeed2(Vector2d seed, int? associatedHoleRingIndex = null);
    public Vector2d Seed { get; }
    public int? AssociatedHoleRingIndex { get; }
}
public sealed class ConstraintSegment2
{
    public ConstraintSegment2(int a, int b, int marker = 0,
        bool allowRefinementSplit = true);
    public int A { get; }
    public int B { get; }
    public int Marker { get; }
    public bool AllowRefinementSplit { get; }
}
public enum ConstraintRingKind2 { Outer, Hole }
public sealed class ConstraintRing2
{
    public ConstraintRing2(IReadOnlyList<int> vertices,
        ConstraintRingKind2 kind, int marker = 0);
    public IReadOnlyList<int> Vertices { get; }
    public ConstraintRingKind2 Kind { get; }
    public int Marker { get; }
}
public enum ConstraintRegionKind2 { Ring, Seed }
public sealed class ConstraintRegion2
{
    public ConstraintRegion2(IReadOnlyList<int> vertices, int label,
        double? maximumArea = null);
    public ConstraintRegion2(Vector2d seed, int label,
        double? maximumArea = null);
    public ConstraintRegionKind2 Kind { get; }
    public IReadOnlyList<int> Vertices { get; }
    public Vector2d? Seed { get; }
    public int Label { get; }
    public double? MaximumArea { get; }
}
public sealed class ConstraintMeshingOptions2
{
    public bool ResolveCrossings { get; set; }
    public double SnapTolerance { get; set; }
}
```

Defaults are crossing resolution enabled and automatic tolerance (`SnapTolerance == 0`). A positive
explicit tolerance must be finite; negatives/nonfinite values are argument errors. Missing optional
collections become empty. Constructors own their input collections; each operation also snapshots options
at entry. Concurrent mutation by the caller during snapshot acquisition is not a supported synchronization
contract. Metadata and snapshots are read-only after construction. Unused region variants expose an empty
vertex list or a null seed. Invalid endpoint indices, ring kinds and invalid maximum areas are argument
errors. Closed repeated ring endpoints are accepted. Empty/degenerate rings receive deterministic handling
and diagnostics, not crashes. Optional point metadata lists must match the point count. Hole seed
coordinates undergo the same finite/range validation as points. An associated ring index must reference a
hole ring; paired ring/seed callers must supply that association so a collapsed ring cannot leave an active
seed behind. An associated seed for a collapsed/degenerate hole is ignored with a diagnostic. Standalone
seeds remove only bounded constrained cells, not an exterior flood. Boundary seeds, outside-domain seeds
and seeds in already removed areas are ignored with diagnostics. Hole rings and seed removals form a union,
with nested holes never restoring area. Region ring boundaries participate in the constrained arrangement;
seeds do not introduce constraint edges. Last matching entry in the shared region list wins label and area
metadata.

## 3. Provenance, coverage and diagnostics

```csharp
public enum ConstraintSourceKind2 { Segment, RingEdge, RegionRingEdge }
public readonly struct ConstraintSource2
{
    public ConstraintSourceKind2 Kind { get; }
    public int InputIndex { get; }
    public int EdgeIndex { get; }
}
public enum ConstraintSpanDisposition2 { Retained, Clipped, Collapsed }
public sealed class ConstraintSourceSpan2
{
    public ConstraintSource2 Source { get; }
    public ExactRational2 Start { get; }
    public ExactRational2 End { get; }
    public ConstraintSpanDisposition2 Disposition { get; }
    public int? SubsegmentIndex { get; }
    public bool Reversed { get; }
    public ExactRational2 MaximumSquaredDisplacementBound { get; }
}
public enum ConstraintDiagnosticKind2
{
    ZeroLengthSource, CollapsedSource, ClippedSource,
    DegenerateRing, BoundaryRegionSeed, OutsideRegionSeed,
    BoundaryHoleSeed, OutsideHoleSeed, RemovedHoleSeed, UnboundedHoleSeed,
    CollapsedHoleSeed, CrossingsDisabled, ConstructionFailure, NormalizationFailure,
    VerificationFailure
}
public sealed class ConstraintDiagnostic2
{
    public ConstraintDiagnosticKind2 Kind { get; }
    public ConstraintSource2? Source { get; }
    public int? RegionIndex { get; }
    public int? HoleSeedIndex { get; }
    public ExactPlanarPoint2? Coordinate { get; }
}
public enum ConstraintFailureKind2
{
    None, CrossingsDisabled, ConstructionFailure,
    NormalizationFailure, VerificationFailure
}
```

Segment source indices address the explicit segment list; ring-edge indices address the ring and its edge,
including the implicit closing edge when not explicitly repeated. Region ring edges address the shared
region list, never the outer/hole ring list. An explicit repeated closing vertex does not create an extra
implicit nonzero edge. `EdgeIndex` is -1 for explicit segments. Source ordering is kind, input index, edge
index, followed by increasing exact parameters in the caller's original direction. Collapsed zero-length
sources still have a `[0,1]` metadata span. Retained intervals identify a subsegment and its direction;
clipped/collapsed intervals have a null subsegment and `Reversed == false`. Spans cover `[0,1]` per source
without undocumented gaps. The proposed displacement bound is meaningful only for retained spans;
non-retained spans expose exact zero. This convention is part of the requested review.

Diagnostics are sorted by kind, source key, region index, hole seed index and exact coordinate with nulls first; identical
keys are deduplicated. Failures carry no partial normalized object or mesh. Diagnostics must not substitute
for an unapproved behavior, such as skipping a required snap to avoid a cycle. A scalar conflicting marker
of zero never discards per-source provenance. Maximum-area metadata is not enforced by M2.

## 4. Normalization and mesh results

```csharp
public sealed class ConstraintNormalizer2
{
    public ConstraintNormalization2Result Normalize(PlanarConstraintInput2 input,
        ConstraintMeshingOptions2 options = null,
        CancellationToken cancellationToken = default);
    public ConstraintNormalization2Result Normalize(NormalizedConstraintInput2 input,
        CancellationToken cancellationToken = default);
}
public sealed class ConstraintNormalization2Result
{
    public Triangulation2Status Status { get; }
    public ConstraintFailureKind2 FailureKind { get; }
    public NormalizedConstraintInput2 Input { get; }
    public IReadOnlyList<ConstraintDiagnostic2> Diagnostics { get; }
}
public sealed class NormalizedConstraintInput2
{
    public PlanarConstraintInput2 OriginalInput { get; }
    public bool ResolveCrossings { get; }
    public ExactRational2 ResolvedSnapTolerance { get; }
    public IReadOnlyList<ExactPlanarPoint2> ExactVertices { get; }
    public IReadOnlyList<int> InputVertexMap { get; }
    public IReadOnlyList<int?> VertexIds { get; }
    public IReadOnlyList<int> VertexMarkers { get; }
    public IReadOnlyList<ConstraintSubsegment2> Subsegments { get; }
    public IReadOnlyList<ConstraintSourceSpan2> SourceSpans { get; }
    public IReadOnlyList<ConstraintDiagnostic2> Diagnostics { get; }
}
public sealed class ConstrainedDelaunayMesh2
{
    public ConstraintTriangulation2Result Compute(PlanarConstraintInput2 input,
        ConstraintMeshingOptions2 options = null,
        CancellationToken cancellationToken = default);
    public ConstraintTriangulation2Result Compute(NormalizedConstraintInput2 input,
        CancellationToken cancellationToken = default);
}
public sealed class ConstraintTriangulation2Result
{
    public Triangulation2Status Status { get; }
    public ConstraintFailureKind2 FailureKind { get; }
    public ConstraintMesh2 Mesh { get; }
    public IReadOnlyList<ConstraintDiagnostic2> Diagnostics { get; }
}
public sealed class ConstraintSubsegment2
{
    public int A { get; }
    public int B { get; }
    public int Marker { get; }
    public bool AllowRefinementSplit { get; }
    public IReadOnlyList<int> SourceSegmentIndices { get; }
    public IReadOnlyList<int> SourceMarkers { get; }
    public IReadOnlyList<ConstraintSource2> Sources { get; }
    public IReadOnlyList<int> AllSourceMarkers { get; }
}
public sealed class ConstraintMesh2
{
    public NormalizedConstraintInput2 NormalizedInput { get; }
    public IReadOnlyList<Vector2d> Vertices { get; }
    public IReadOnlyList<ExactPlanarPoint2> ExactVertices { get; }
    public IReadOnlyList<int> InputVertexMap { get; }
    public IReadOnlyList<int?> VertexIds { get; }
    public IReadOnlyList<int> VertexMarkers { get; }
    public IReadOnlyList<Index3i> Triangles { get; }
    public IReadOnlyList<Index3i> Neighbours { get; }
    public IReadOnlyList<int> TriangleRegionLabels { get; }
    public IReadOnlyList<double?> TriangleMaximumAreas { get; }
    public IReadOnlyList<ConstraintSubsegment2> Subsegments { get; }
    public IReadOnlyList<ConstraintSourceSpan2> SourceSpans { get; }
    public bool TryLocate(Vector2d point, out int triangleId);
    public bool TryLocate(ExactPlanarPoint2 point, out int triangleId);
}
```

Results use existing `Success`, `Empty`, `Failed`. Normalization precedes domain clipping and returns
`Success` when at least one representative point remains, otherwise `Empty` with an owned empty snapshot.
It does not infer emptiness from absent domain faces. Mesh output contains clipped subsegments and coverage
spans; normalized and mesh subsegment indices address their respective owning collections. `Failed` implies
null result object and non-None failure kind; `Empty` CDT retains owned metadata with no triangles. Normalize operations never expose caller-mutatable snapshots. The
normalized object has no public constructor taking output edges as fresh sources. It retains the original
source-based certificates and resolved policy on re-normalization. Returned snapshots may share immutable
storage; reference identity is not an acceptance requirement. Reuse and cancellation do not mutate callers
or earlier results.

`Sources` and `AllSourceMarkers` are parallel lists preserving ring and explicit sources; the two
`SourceSegment*` lists are the explicit-segment-only projection, including zero and repeated marker values.
A subsegment's scalar marker is its sole distinct nonzero source marker or zero on conflict. Optional
refinement splitting requires every source to permit it; ring defaults are splittable. No M2 quality or
refinement algorithm is introduced. Exact faces are CCW, canonical, with neighbours opposite each vertex;
boundary neighbour is -1. Closed point location returns the lowest incident face ID, or false and -1.
Display geometry may degenerate; only exact reference geometry is certified.

## 5. Interior witnesses

```csharp
public static class ConstraintRingInterior2
{
    public static bool TryGetExactWitness(IReadOnlyList<ExactPlanarPoint2> ring,
        out ExactPlanarPoint2 witness, CancellationToken cancellationToken = default);
    public static bool TryConvertWitnessToInteriorDouble(
        IReadOnlyList<ExactPlanarPoint2> ring, ExactPlanarPoint2 witness,
        out Vector2d point, CancellationToken cancellationToken = default);
}
```

A repeated final point is accepted. Even-odd filled area is authoritative for self-crossing rings. The first
method returns false/default when there is no filled interior. The second certifies strict interior after
rounding the supplied witness, returning false/default otherwise; it is not a search for another double
point. A non-interior supplied exact witness returns false rather than throwing. Invalid collection/null
input is an argument error. Both helpers support cancellation. All operations check pre-cancellation and
throw `OperationCanceledException`; cancellation never appears as a successful/failed geometry result.

## 6. Owner decisions and remaining verification gates

The owner approved the five decision groups on 2026-10-30: pre-clipping normalization; full provenance plus
explicit-segment projections; caller IDs and point markers now with deterministic survivor precedence;
seed-only holes now with bounded-cell removal and ignored-invalid-placement diagnostics; and the remaining
value/span/diagnostic/witness conventions, including cancellation. Section 2 makes the paired-hole-seed
association explicit to preserve the collapsed-hole contract; standalone seeds remain a separate input form.

Independent tests must be integrated against this contract before production implementation; they must not
be rewritten to accommodate implementation errors. A compile-red caused by absent M2 types is expected,
not a test pass. Bounds/adversarial gates remain open. API approval alone does not establish any geometry
correctness, performance, quality-refinement capability or successful production acceptance.
