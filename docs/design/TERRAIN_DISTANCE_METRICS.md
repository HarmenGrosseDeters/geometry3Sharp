# Honest terrain distance metrics

Status: planned, not implemented. Corresponds to the owner's **C2(A) / task 5**. The owner confirmed that
"normal distance" means the shortest 3D distance to the reference surface, with horizontal and vertical
components of the **same closest-point displacement**, not a ray along a reference normal.

## Task brief

- **Goal:** add interpretable 3D, horizontal and vertical error statistics to TerrainBenchmark without changing
  terrain construction, simplification, safety-line algorithms or the "never steeper" rule.
- **Tier:** S for this plan; M for the benchmark/test implementation and independent review.
- **Scope:** `benchmarks/TerrainBenchmark` measurement helper, `Program.cs`, `analyze.py`, README, and dedicated
  tests/project wiring under `tests/geometry3Sharp.Tests`. First integration: `tdc` and `gtin` against the input
  grid TIN; other benchmark operations are outside this first task.
- **Non-goals:** production geometry/API changes, slope certification, a new spatial index, replacing existing
  vertical-error checks, or claiming a sampled maximum is a guaranteed global Hausdorff bound.
- **Interface:** additive JSON fields and analysis columns, meters as units, and explicit sampling/validity metadata.
- **Budget:** one benchmark-only PR; bounded deterministic samples and separate measurement timings. Stop if a
  production nearest-point defect is found: record the repro and seek a separate fix rather than changing the metric.
- **Report:** analytic test output, same-input before/after benchmark rows and commands, sampling configuration,
  overhead and independent review. No performance or quality number without captured output.

## Definition

For a sampled output-surface point p and its closest point q on the input reference TIN, let delta = p - q:

- `d3 = sqrt(delta.x^2 + delta.y^2 + delta.z^2)`;
- `dh = sqrt(delta.x^2 + delta.y^2)`;
- `dv = abs(delta.z)` (vertical magnitude); optionally report signed `delta.z` separately if needed later.

The relation `d3^2 = dh^2 + dv^2` must hold sample by sample within documented floating-point error. Do not
combine a 3D nearest point with a separate XY nearest point or a vertical-ray hit. At an edge or vertex, delta
need not be parallel to a triangle normal; name the fields `closest_*`, not an unqualified normal error.
The existing `dz_*` fields remain vertical-ray errors at fixed XY positions with their existing sample domain.
They have different meanings and must remain separately labelled.

The measurement is directed **result -> reference**, not symmetric. Keep the existing missed-ray, XY-overlap and
validity checks visible: small directed distances do not prove that a result has no holes or missing features.
A symmetric coverage metric is separate work, not implied by these new fields.

## Implementation

0. Check what exists first: `MeshHausdorffDistance`, `MeshDeviation`, `MeshFidelityMetrics` (seeded, area-weighted samples), and
   `gtin fidelity=1`, which already emits sampled maximum distances (`FidelityFields.cs`). New here are only the
   closest-point components and `tdc`. Decision (2026-10-30, proposed to the owner): `fm_*` stays as it is, as those fields are shared with benchmarks
   that have no up axis. The new `closest_*` fields live in `TerrainBenchmark`, next to `fm_*`, and follow the same sampling (area weighted, fixed seed).
1. Add a benchmark-local helper that returns one closest reference point, distance and validity per sample.
   Reuse the existing AABB tree and point-triangle query API; no new library API. Compare the fast query against
   an independent brute-force point-to-triangle reference on small analytic fixtures. Test the closest point,
   not only its squared distance.
2. Query the full valid reference TIN, including edges and vertices. Do not reuse `Checks.DistanceToTIN` with a
   fixed `maxDist` as if it guaranteed a global nearest point: that helper searches only a bounded XY window.
   Use an unrestricted tree query or a search with a proven terminating bound. Do not silently clip large errors.
3. Use one shared deterministic, area-weighted result-surface sampling policy for `tdc` and `gtin`, with fixed
   seed/count recorded in JSON. Include triangle-interior samples, not only vertices, to avoid hiding errors
   between retained grid nodes. Reuse an existing suitable sampler if available; otherwise keep it benchmark-local.
   Select the default count by measuring runtime and sample-convergence on fixed cases; no unmeasured speed target.
4. Compute all three statistics from exactly the same valid correspondences. Emit `closest_d3_*`, `closest_dh_*`
   and `closest_dv_*` using the existing `_n`, `_min`, `_p01`, `_p50`, `_p99`, `_max`, `_mean`, `_rms` convention.
   Also emit direction, units, seed, requested/valid/invalid sample counts, invalid reference-triangle count and
   a validity/status field. No zero or `double.MaxValue` substituted for a failed nearest-point query.
5. Exclude non-finite/no-data triangles explicitly and report them. An empty reference or no valid samples is
   unavailable, not perfect agreement. Require finite JSON statistics; omit unavailable values or use JSON null.
   Do not quietly drop failed samples and describe the remaining statistics as a complete measurement.
6. Document tie handling: equal-distance but distinct closest points have the same d3 but potentially different
   components. `DMeshAABBTree3.FindNearestTriangle` uses a strict `<` in traversal order, so it offers no triangle-ID tie rule; the
   result is deterministic for a fixed tree, and that is all this plan may claim. Do not promise that dh/dv
   are unchanged by arbitrary retriangulation in these ambiguous cases; analytic tests use unique closest points
   or explicitly assert the documented tie outcome.
7. Update `analyze.py` additively. Older JSON rows lacking the fields render as unavailable, not zero or failure.
   Keep the old vertical columns. Separate reference-tree build and distance-measurement time from the existing
   algorithm timings so the additional checks cannot appear as a terrain-algorithm regression.

## Tests and sensitivity checks

Use procedural meshes and samples, no committed geodata or mesh files:

- Coincident plane: all distances zero. Horizontal parallel planes: d3 = dv, dh = 0.
- Sloping plane with known perpendicular displacement: compute the expected nearest point and all components
  analytically. This must distinguish shortest distance from the larger vertical-ray error.
- Finite triangle: projections inside the face, onto each edge and each vertex; a steep facet and a boundary
  case outside the reference footprint. Include multiple triangles where the nearest surface is outside an
  artificially small XY search window.
- Empty/no-data/non-finite reference, invalid sample, no valid samples and tied nearest points: explicit status,
  deterministic behavior, no misleading finite zero metric and valid JSON.
- Translation to UTM and `Similarity.All()`: d3 scales as a length. For rotations, recompute the expected rotated
  displacement in the fixed Z-up frame; dh and dv are not individually rotation invariant. Power-of-two scaling
  of controlled dyadic fixtures is bit-exact where the underlying operations allow it; other cases use a
  coordinate-resolution-derived tolerance. Repeated serial runs must agree; no parallel measurement path is needed.
- End-to-end `tdc`/`gtin` JSON and analyzer tests on a small procedural grid (analyzers have no test runner in CI today: add
  one, or say so in the PR): same sample counts across the three
  metrics, expected units, old fields retained, old JSON still readable, and algorithm output unchanged.
- Fault sensitivity: replace d3 with vertical-ray error, zero dh, mix correspondences, or cap the nearest search;
  each controlled test-local fault must be detected. Link the actual helper into tests (or reference the net10
  benchmark); do not test only a duplicated formula. Keep any net10-only project reference conditional so net48
  library tests still build.

## Measurement and acceptance

`Program.cs` loads grids only from `dataDir`, so synthetic grids need a test-local writer, or tests on the helper directly. Run a
synthetic flat/slope/ridge grid that way; then, if the existing local geodata is
available, use the same fixed Hambach window, algorithms, tolerances and local/UTM placements before and after.
No automatic large geodata download is required. Record raw JSON, input identity/checksum, sample policy and
command. Compare mesh outputs and pre-existing metrics before/after; new fields were **absent** before, not zero.
Use the analytic synthetic cases to demonstrate correctness and a fixed steep-terrain case to show how d3 and
the old vertical error differ. Report measurement overhead separately, with no invented speed or quality gain.

Planned targeted commands (not run during planning):

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~TerrainDistanceMetric
dotnet build benchmarks\TerrainBenchmark\TerrainBenchmark.csproj -c Release
# If the pre-existing data is available; run both base and changed benchmark on the same input.
dotnet run --project benchmarks\TerrainBenchmark -c Release -- tdc grid=dgm2022 win=326000,5641000,327000,5642000 coords=local tol=0.05
dotnet run --project benchmarks\TerrainBenchmark -c Release -- gtin grid=dgm2022 win=326000,5641000,327000,5642000 coords=local maxerr=0.05
```

Acceptance: analytic and integration tests pass, checking d3, dh and dv against independently known values (the identity
d3^2 = dh^2 + dv^2 holds by construction and proves nothing); missing
coverage is visible; old JSON and metrics remain usable; both algorithms emit the additive metrics; no slope
rule or production code changes. Run all local CI steps from `CLAUDE.md` before the implementation is pushed.
