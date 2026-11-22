# Plan: batch query extras (`Span` overloads, early any-hit, all-hits, CLI)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Other: batch query extras (Span overloads, early any-hit, all-hits, CLI)"
- **Kind:** parked (not done by owner decision 2026-11-08/09; trigger: a concrete caller needing zero-copy `Span` slices, early-out `AnyHitBatch`, `AllHitsBatch`, or `g3cli raycast` / `inside`)
- **Tier:** M (additive overloads in `spatial/MeshBatchQueries.cs`, optional internal early-out in `DMeshAABBTree.cs`, CLI commands)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): item 4 is answered on master by `g3cli query3d` (batch ray hits with `--any-hit`, nearest points, inside tests with `--inside crossings|winding_number|auto`, camera depth images; also an MCP tool; [analysis-commands.md](../../tools/g3cli/docs/analysis-commands.md#query3d-meshbatchqueries-dmeshaabbtree3)). Items 1-3 remain parked until a caller needs them.

## Current state

- `spatial/MeshBatchQueries.cs` merged in #448 (2026-11-09; [BATCH_QUERIES.md](../design/BATCH_QUERIES.md) §§0-0a):
  `IntersectBatch`, `AnyHitBatch`, `NearestPointBatch`, `IsInsideBatch` over `DMeshAABBTree3` (Morton-sorted + parallel
  via `gParallel`: 3-5x over serial loops, allocation-free per query, bit-identical to single calls).
- Internal caller profile merged in #678 (2026-11-12): every internal caller already runs parallel loops over coherent
  input order, so **nothing internal was switched** (`BATCH_QUERIES.md` §7).
- Four additive extras were explicitly deferred ("later" / not done by owner decision):
  1. **`Span<T>` / `ReadOnlySpan<T>` (and offset/count) overloads** (`#if NET` for `Span`, offset/count for
     `netstandard2.0`) — today arrays only.
  2. **Early-out `AnyHitBatch`:** today `AnyHitBatch` runs `FindNearestHitTriangle` (full nearest-hit traversal) because
     early-out requires touching `DMeshAABBTree3` internals (which were frozen during the boolean merge queue in Nov 2026).
  3. **`AllHitsBatch`:** collect all ray-triangle intersections per ray (CSR offset array + hit buffer, zero per-ray
     allocations).
  4. **CLI / MCP commands (`g3cli raycast`, `g3cli inside`):** done as `g3cli query3d` (see the plan status); nothing
     left to build for this item.

## Trigger / decision needed

A concrete caller (Unity / Rhino / CAD plugin using `Span` slices, shadow/visibility ray batches wanting early-out
`AnyHit`, penetration/CSG thickness queries wanting `AllHits`, or headless CLI/MCP users wanting `raycast` / `inside`).
Item (4) is covered by `g3cli query3d` (verified 2026-11-22).

## Goal

Add the requested `MeshBatchQueries` extras with zero breaking changes, bit-identical results against single calls, and
measured speedups for early-out `AnyHitBatch`.

## Scope

- `spatial/MeshBatchQueries.cs` (`Span` / offset-count overloads, `AllHitsBatch`, early-out `AnyHitBatch` call)
- `spatial/DMeshAABBTree.cs` (only for item 2: internal `FindAnyHitTriangle` returning on the first valid hit `t <= maxT`,
  and `FindAllHitTriangles` for item 3)
- `tools/g3cli/` (item 4 only, if absent: `raycast` / `inside` commands + MCP tool definitions)
- `tests/geometry3Sharp.Tests/MeshBatchQueriesTests.cs`, `tests/g3cli.Tests/`
- `benchmarks/BatchQueryBenchmark/` (before/after A/B for early-out `AnyHitBatch`)

## Non-goals

No second flat BVH (b2) and no SIMD tree (both measured and rejected in `BATCH_QUERIES.md` §1); no switching of existing
internal callers (#678); keep `netstandard2.0` + `net48` builds green (`Span` overloads guarded by `#if NET` or
`System.Memory` only if already referenced — never add a new NuGet dependency to `netstandard2.0`).

## Interface

Additive signatures on `MeshBatchQueries`:
- `ReadOnlySpan<Ray3d>` / `Span<RayHit>` overloads (on `net10.0`) + `(T[] array, int offset, int count)` on all TFMs.
- `AllHitsBatch(DMeshAABBTree3 tree, Ray3d[] rays, List<RayHit> hitsOut, int[] rayOffsetsOut, BatchOptions options)`
  (deterministic ascending-`T` / triangle-id order per ray).
- `AnyHitBatch` keeps its exact signature; only its traversal gains early-out.

## Look before you build

1. Library: `spatial/MeshBatchQueries.cs`, `spatial/DMeshAABBTree.cs`, `benchmarks/BatchQueryBenchmark/README.md`,
   `tools/g3cli/` (check existing commands first).
2. Prior art: Embree `rtcOccluded1` / `rtcOccludedN` (early-out shadow rays); BVH any-hit traversal (skipping further
   children immediately on first triangle intersection).

## Steps (one PR per requested extra, or combined if small)

1. For early-out `AnyHitBatch`: run an upper-bound probe on `BatchQueryBenchmark` (dense mesh + occluded rays vs miss
   rays) — proceed only if early-out speeds up occluded batches by >= 25 % without slowing miss batches.
2. Implement `FindAnyHitTriangle` / `FindAllHitTriangles` in `DMeshAABBTree3` (existing nearest-hit path untouched and
   bit-identical) and wire `AnyHitBatch` / `AllHitsBatch` / `Span` overloads.
3. Add tests: bit-identical `AnyHitBatch` verdict vs `IntersectBatch` on all rays (hit/miss/maxDist/non-finite);
   `AllHitsBatch` vs brute-force per-triangle intersection; `Span` slice tests; invariance, determinism, reuse, zero
   per-query allocations.

## Acceptance

- `AnyHitBatch` verdicts 100 % identical to `IntersectBatch(t <= maxT)` on every test; `AllHitsBatch` matches brute
  force in hit count and sorted `T` values.
- `BatchQueryBenchmark` before/after table + command in PR: early-out `AnyHitBatch` >= 25 % faster on occluded scenes,
  never slower on miss-heavy scenes (median of 5).
- `netstandard2.0`, `net10.0`, `net48`, and `dotnet pack` API validation (1.0.324) all green.

## Sources and licences

Own code; standard BVH traversal textbook methods.

## Budget and stop rule

1 M session. Stop early-out `AnyHitBatch` if occluded-ray speedup is < 15 % on `BatchQueryBenchmark`.

## Risks and open questions

- `Span<T>` cannot be captured directly in `gParallel.ForRange` lambdas without unsafe pointers or array backing: on
  `net10.0` check whether serial `Span` + `Memory<T>` / `ReadOnlyMemory<T>` for parallel is cleaner and avoids `unsafe`.
