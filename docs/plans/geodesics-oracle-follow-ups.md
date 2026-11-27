# Plan: geodesic oracle follow-ups (heat-method tests against the oracle, fragmentation lever)

- **Board row:** OPEN_TOPICS.md §3 "Ready to start", row "Geodesics/param: oracle follow-ups (check)"
- **Kind:** ready to start (part 1); part 2 is conditional
- **Tier:** M (tests); the fragmentation lever is L (interval error accounting in `PolyhedralGeodesicDistance`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- `mesh_ops/ExactMeshGeodesicDistance.cs` (internal, serial, certified [Lower, Upper] by window propagation in interval
  arithmetic; #869, A6 fix #1062, A7' #1147): flat and aligned inputs of 10^4 vertices in 3-6 s; width <= ~2e-8 on the
  procedural family ([GEODESICS.md](../design/GEODESICS.md) 6c/6d; `benchmarks/GeodesicOracleBenchmark`).
- Public `PolyhedralGeodesicDistance` for 10^5 vertices merged (#1185); its saddle tail #1186 closed.
- **Not done:** heat-method tests against the oracle. `grep -i heat tests/geometry3Sharp.Tests/ExactMeshGeodesicDistanceTests.cs`
  is empty; the heat method is `mesh_ops/MeshGeodesicDistance.cs` (`TimeFactor` x mean edge^2), the vector heat method
  `mesh_ops/VectorHeatMethod.cs`.
- Fragmentation lever ("merge with an already propagated window"): GEODESICS.md (2026-11-19 table) shows it covers only
  3-7 % of touching same-origin pairs (plane 41²: 999 of 13785; ridge 101²: 12058 of 452321); 86-97 % stay split by the
  #1058 inner-interval rule. The note states the lever is **not needed** unless the 6e time / memory targets fail.
- Open in the robustness sweep: R16 heat-method drift at rotated UTM (#1165, deferred) - same tests would catch it.

## Trigger / decision needed

Part 1: none. Part 2 (lever): only if `GeodesicOracleBenchmark` misses the 6e time / memory targets on 317² plane /
ridge / terrain; the note names that trigger.

## Goal

The heat method's accuracy is pinned against a certified reference with stated tolerances, so regressions (and the R16
drift) show as failing tests.

## Scope

- New `tests/geometry3Sharp.Tests/HeatGeodesicOracleTests.cs` (name chosen to match the geodesic shard filter; check
  `tests/mutation/stryker-mesh-geodesic-*.json` `test-case-filter`; if it cannot match, extend the filter and say so).
- Test-mesh generators already used by `ExactMeshGeodesicDistanceTests` (plane, dome, saddle, ridge, terrain).
- Part 2 only: `mesh_ops/PolyhedralGeodesicDistance.cs`, GEODESICS.md.

## Non-goals

No change to the heat method's defaults or results; no public API; no A9.

## Interface

None (tests). The oracle stays internal (`InternalsVisibleTo` exists).

## Look before you build

1. Library: `ExactMeshGeodesicDistance` (reference), `PolyhedralGeodesicDistance` (second, faster reference for 10^4+),
   `MeshGeodesicDistance` (heat), existing similarity / invariance helpers of the geodesic tests.
2. Literature: Crane, Weischedel, Wardetzky, "The Heat Method for Distance Computation" (CACM 2017): error decreases with
   mesh refinement, t = h² recommended; typical mean relative error 1-5 % on regular meshes. Surazhsky et al. 2005 (exact
   MMP-style windows) as the reference method.

## Steps (one PR, clean commits)

1. Measure: heat vs oracle on 6 procedural meshes at 2-3 resolutions; mean and max relative error, error vs h
   (convergence order). Choose tolerances from the measurement with margin (e.g. 1.5x the measured max), recorded.
2. Tests: (a) accuracy bound per family; (b) convergence: error at h/2 < error at h; (c) similarity invariance
   (translate, rotate, scale 2^k, UTM-size offset) - expected to expose R16, then marked with the issue number and
   skipped-with-reason or asserted as the documented bound; (d) determinism (two runs bit-identical); (e) reuse (one
   instance, two sources).
3. Each test checked by breaking the heat method (e.g. wrong `TimeFactor`) - fails.

## Acceptance

- `dotnet test tests/geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~HeatGeodesicOracleTests"`: each test
  <= 5 s (CLAUDE.md time rule), total < 30 s.
- Error table (command and output) in the PR and in GEODESICS.md.
- Mutation: geodesic-1/2 not gated (no exact code change); survivors in `MeshGeodesicDistance` noted for the next PR.

## Sources and licences

Crane et al. 2017 (paper); no code ports.

## Budget and stop rule

Part 1: one session. Part 2 only on the trigger, with an upper bound first (share of windows the lever can remove: 3-7 %).

## Risks and open questions

- Oracle time at 10^4 vertices (3-6 s) is above the 5 s per-test rule: use <= 41² meshes in tests, larger only in the
  benchmark.
