# Plan: Opt-In Attribute & Provenance Propagation for `ExactMeshBoolean` (Phase 1 — 2A)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 2A)
- **Kind:** Phase 1 — ready to start (no separate pre-implementation spike needed)
- **Tier:** M (touches `ExactMeshBoolean` assembly, `ExactPlanarSimplify`, `ExactMeshArrangementRegions` opt-in guard, and `MeshRoundingRepair` provenance preservation; zero changes to exact predicates or error bounds)
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- `ExactMeshBoolean` and `SolidBooleanPipeline` output clean, closed, manifold `DMesh3` meshes, but drop input `TriangleGroups`, face provenance `(sourceMesh, sourceTriangle)`, UVs, per-vertex normals, and colors.
- `ExactPlanarSimplify` merges coplanar faces purely by supporting plane, and `ExactMeshArrangementRegions` (`#1744`, default ON) merges adjacent same-plane triangles across flat edges without consulting `TriangleGroups`.
- `MeshRoundingRepair` splits/collapses/retriangulates local triangles during SafeRounding without carrying provenance or re-deriving sources from the pre-rounding mesh.

## Goal

Add opt-in, bit-identical-when-off **face provenance, `TriangleGroups` preservation, and local-frame barycentric attribute transfer (UVs, normals, colors, with optional seam splitting)** to `ExactMeshBoolean` and `SolidBooleanPipeline` — enabling CAD face IDs, multi-material rendering, UV-mapped assets, and 3D-printing region tags.

## Scope

- `mesh_ops/ExactMeshBoolean.cs`:
  - Opt-in `PreserveTriangleGroups` (`bool`, default `false`) and `TrackFaceProvenance` (`bool`, default `false`).
  - Expose `FaceProvenance` (`(int MeshIndex, int SourceTriangleId, bool Flipped)` per output triangle) and preserve `TriangleGroups` on `Result` (mapping `(meshIndex, inputGroup)` deterministically to output group IDs via an explicit group map or caller-provided group offsets).
  - `rederive_sources`: after `MeshRoundingRepair`, re-associate any locally edited/re-triangulated triangles to their nearest consistent pre-rounding provenance face (preserving provenance for 100% of untouched triangles directly via triangle-ID tracking, and exact/closest consistent fallback for the few rounding-edited triangles).
- `mesh_ops/ExactPlanarSimplify.cs`:
  - When `PreserveTriangleGroups` or `TrackFaceProvenance` (with group boundaries) is enabled, key planar regions by `(planeId, groupId)` instead of `planeId` alone so material/face-ID seams are never merged across.
- `mesh_ops/ExactMeshArrangementRegions.cs`:
  - When `PreserveTriangleGroups` is enabled, treat edges between two triangles with different `TriangleGroup` IDs as non-flat region boundaries in `flat_edge` (zero effect when `PreserveTriangleGroups == false`).
- `mesh_ops/ExactMeshAttributeTransfer.cs` (new helper):
  - Per-corner **local-frame barycentric interpolation** (shifted to source triangle corner `a` to avoid catastrophic cancellation at UTM `1e7` coordinates!) for vertex UVs, normals, and colors from `FaceProvenance`.
  - Opt-in `SplitAttributeSeams` pass that splits shared output vertices along UV/normal/material discontinuities into duplicate corner vertices (preserving exact geometric position, clean optional post-step so watertight geometry is never corrupted).
- `mesh_ops/SolidBooleanPipeline.cs`: forward `PreserveTriangleGroups` / `TrackFaceProvenance` cleanly (and preserve `TriangleGroups` boundaries in `Reducer` via `MeshConstraints` / group-boundary pinning when enabled).
- `tests/geometry3Sharp.Tests/ExactBooleanProvenanceTests.cs`.

## Non-goals

- Default behavior (`PreserveTriangleGroups = false`, `TrackFaceProvenance = false`) must be **bit-identical** to `master` with zero performance overhead.
- No multi-material interior partitions / non-manifold cellular complexes here (that is Phase 3 / Area 2B, [boolean-multimaterial-partition-spike-2b0.md](boolean-multimaterial-partition-spike-2b0.md)).
- No changes to exact predicates, error bounds, or upstream 1.0.324 API signatures.

## Interface

```csharp
// Additive on ExactMeshBoolean:
public struct TriangleProvenance
{
    public int MeshIndex;       // index into input meshes (-1 if none)
    public int SourceTriangle;  // triangle id in Inputs[MeshIndex] (-1 if none)
    public bool Reversed;       // true if piece orientation was flipped (e.g. Difference tool)
}

public bool PreserveTriangleGroups = false;
public bool TrackFaceProvenance = false;
public TriangleProvenance[] ResultProvenance { get; } // indexed by Result triangle id (null unless TrackFaceProvenance)
```
Plus `ExactMeshAttributeTransfer.Transfer(DMesh3 result, TriangleProvenance[] provenance, IReadOnlyList<DMesh3> inputs, AttributeTransferOptions options)` supporting UV, vertex normal, vertex color, and `SplitAttributeSeams`.

## Look before you build

1. Library: `mesh_ops/ExactMeshBoolean.cs`, `mesh_ops/ExactMeshResultBuilder.cs`, `mesh_ops/ExactPlanarSimplify.cs`, `mesh_ops/ExactMeshArrangementRegions.cs`, `mesh_ops/MeshRoundingRepair.cs`, `mesh_ops/ExactMeshImprint.cs`.
2. Literature / prior art: Manifold `originalID` + per-vertex property channels (Apache-2.0, concept only); CGAL PMP face-index maps; [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) §2.

## Steps (one PR, clean commits)

1. Carry `(meshIndex, sourceTriangle, reversed)` from `ExactMeshArrangement` pieces through `ExactMeshResultBuilder` into `Result` (exact 1-to-1 for every un-edited piece).
2. Add `(plane, group)` partitioning in `ExactPlanarSimplify` and group-boundary check in `ExactMeshArrangementRegions.flat_edge` (strictly gated on `PreserveTriangleGroups`).
3. Implement `rederive_sources` across `MeshRoundingRepair` edits (track unchanged triangles directly; re-derive only rounding-modified triangles by local plane/area overlap against pre-rounding triangles).
4. Implement `ExactMeshAttributeTransfer` with local-frame barycentric coordinates (tested at UTM `1e7` and `2^-20..2^20` scales!) and `SplitAttributeSeams`.
5. Wire `PreserveTriangleGroups` into `SolidBooleanPipeline` (respecting group boundaries during optional `Reducer` simplification).
6. Add full test suite: exact provenance accuracy on boxes/spheres/cylinders/coplanar flush cuts/n-ary unions, UV/normal interpolation error `< 1e-12` on linear fields at UTM `1e7`, `SplitAttributeSeams` non-bleeding check, `Similarity.All()`, reuse (toggling flags between `Compute()` calls), serial == parallel determinism, and hand-mutant checks.

## Acceptance

- With both flags `false` (default): bit-identical result hashes and `< 1%` timing delta against `master`.
- With `PreserveTriangleGroups = true`: 100% of output triangles carry the exact input group of their supporting face; coplanar faces with *different* groups are never merged by `ExactPlanarSimplify` or `RegionClipping`.
- With `TrackFaceProvenance = true`: 100% of output triangles have valid `MeshIndex` and `SourceTriangle` (including after `MeshRoundingRepair` edits on 1-ulp / rotated stress cases), and every output triangle's vertices lie on its cited source triangle up to rounding drift.
- Linear UV / normal / color fields transfer to `< 1e-11` accuracy even at UTM `1e7` offsets (`Similarity.All()`).
- Full local CI, `dotnet pack` API validation, and independent review green.

## Sources and licences

Conceptually aligned with Manifold `originalID` / property interpolation (Apache-2.0) and CGAL PMP face maps; pure clean implementation in `geometry3Sharp`.

## Budget and stop rule

1–2 M sessions. Stop and report if `PreserveTriangleGroups` causes >15% slowdown on flush multi-group meshes due to excessive region splitting in `RegionClipping`.
