# Plan: `SolidBooleanPipeline.Verification` v2 & Internal Certificate Hygiene (Phase 1 — 1A + 1B)

- **Umbrella study:** [EXACT_BOOLEAN_NEXT_PHASE_2026-11.md](../research/EXACT_BOOLEAN_NEXT_PHASE_2026-11.md) (Area 1)
- **Kind:** Phase 1 — ready to start (no pre-implementation spike needed)
- **Tier:** S (1A test-only hygiene) + M (1B `SolidBooleanPipeline.Verification` v2; no exact-predicate or error-bound changes)
- **Plan status:** draft, 2026-11-28 (Berlin).

## Current state

- `SolidBooleanPipeline.Verification` (`#1488`) currently supports `NoInventedSurface` (sampled distance from result to inputs) and `RoundingVolume` (double-precision `RoundingVolumeBefore` vs. `RoundingVolumeAfter` with per-part thin-component exclusion).
- The internal test-only certifying checker (`tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`, `CertificateFile.cs`) works as an internal QA/fuzz oracle, but:
  1. `CertificateFile` has no `RegionClipping` bit (so region-clipped results after `#1744` cannot round-trip stand-alone; [COPLANAR_REGION_CLIPPING.md](../design/COPLANAR_REGION_CLIPPING.md)).
  2. `CheckSameChain` does not verify exact multiset triangle cancellation cleanly across arbitrary opposing stacks.
- Public "Certified Rounded Boolean" mode is **explicitly dropped/parked** after the conceptual audit (intent blindness, sub-$\delta$ rounding isotopy gap, Chazelle C2/TCB paradox, 40–100× checker cost in [PIPELINE_CERTIFICATE_SPIKE_2026-11.md](../research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md)).

## Goal

1. **Stage 1A (internal QA hygiene):** Keep `BooleanCertificate` / `CertificateFile` current with `#1744` (`RegionClipping` flag) and tighten `CheckSameChain` multiset cancellation.
2. **Stage 1B (production value):** Extend `SolidBooleanPipeline.Verification` with three fast, honest, conceptually airtight runtime checks (`ExactQuantities`, `TopologyPreservation`, `InputIntentGuard`) that guard real CAD and MCP workflows at ~0.1–0.6× boolean cost without making unprovable isotopy claims.

## Scope

- **Stage 1A (tests only):**
  - `tests/geometry3Sharp.Tests/Harness/CertificateFile.cs` (add 1-bit `RegionClipping` flag + version bump / backward-clean parsing)
  - `tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs` (exact multiset triangle cancellation in `CheckSameChain`)
  - `tests/geometry3Sharp.Tests/BooleanCertificateTests.cs` (round-trip with `RegionClipping = true` + seeded mutant tests)
- **Stage 1B (pipeline verification v2):**
  - `mesh_ops/SolidBooleanPipeline.cs` (`VerificationModes` flags, `SolidBooleanReport` exact quantity & topology fields, `VerificationFailed` diagnostics)
  - `mesh_ops/ExactMeshBoolean.cs` (read-only internal hook exposing exact unrounded interval volume/area from $E_s$ when requested — zero cost when `Verification == None`)
  - `tests/geometry3Sharp.Tests/SolidBooleanPipelineVerificationTests.cs`
  - `docs/guides/booleans-and-imprint.md` (document new `VerificationModes` flags honestly, including exact scope and non-guarantees)

## Non-goals

- No public `BooleanCertificate` API and no public "certified rounded boolean" promise.
- No changes to default `SolidBooleanPipeline.Verification = VerificationModes.None` behavior (bit-identical when off).
- No changes to `ExactMeshArrangement` / `ExactMeshBoolean` geometric predicates or rounding rules; upstream 1.0.324 API compatibility preserved.

## Interface

Additive flags on `SolidBooleanPipeline.VerificationModes` (opt-in, combinable with `|`):
- `ExactQuantities = 4`: computes sound `double` interval bounds `[ExactVolumeMin, ExactVolumeMax]` and `[ExactAreaMin, ExactAreaMax]` directly from the exact unrounded surface $E_s$ (before rounding and before `Reducer`), and checks rounded result volume against the exact interval plus `MaxRoundingVolumeChange` (catching silent volume drift even when `RoundingVolumeBefore` itself suffered double cancellation on UTM/large-offset frames!).
- `TopologyPreservation = 8`: compares connected-component counts, Euler characteristics $\chi = V - E + F$, and per-component volume signs between exact $E_s$, post-rounding result (accounting for documented `RoundingRemoved` / `MinThickness` drops), and post-`Reducer` result (catches silent topology corruption, component disappearance, or negative-volume flips).
- `InputIntentGuard = 16`: refuses (`VerificationFailed`) when `InputsWithWindingOutsideZeroOne` is non-empty (e.g., outward-pointing cavity wall, Audit F2) or `ResultKind == EmptyAfterRounding` — turning soft advisory signals into a hard fail-closed gate for callers and agents.
- Convenience preset: `Standard = NoInventedSurface | RoundingVolume | ExactQuantities | TopologyPreservation`.

Additive report fields on `SolidBooleanReport`:
- `ExactVolumeMin`, `ExactVolumeMax`, `ExactAreaMin`, `ExactAreaMax` (all `0` when `ExactQuantities` is off)
- `ExactComponents`, `ExactEulerCharacteristic`, `RoundedComponents`, `RoundedEulerCharacteristic`, `ReducedComponents`, `ReducedEulerCharacteristic`

## Look before you build

1. Library: `mesh_ops/SolidBooleanPipeline.cs` (`VerificationModes`, `RoundingVolume`, `NoInventedSurface`), `mesh_ops/BooleanVolumeGradient.cs` (exact volume from $E_s$), `tests/geometry3Sharp.Tests/Harness/BooleanCertificate.cs`, `CertificateFile.cs`.
2. Research: [CERTIFYING_BOOLEANS_2026-11.md](../research/CERTIFYING_BOOLEANS_2026-11.md), [PIPELINE_CERTIFICATE_SPIKE_2026-11.md](../research/PIPELINE_CERTIFICATE_SPIKE_2026-11.md), [SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](../research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md) (F1, F2, F3).

## Steps (one PR for 1A, one PR for 1B — or two clean commits in one PR)

1. Implement Stage 1A (`CertificateFile` `RegionClipping` flag + `CheckSameChain` multiset check) and verify existing certificate tests + new `RegionClipping = true` round-trip tests pass.
2. Implement `ExactQuantities` interval volume/area accumulator over $E_s$ (local origin shift to avoid catastrophic cancellation at UTM `1e7`, sound outward rounding via `ExactInterval` / IEEE error bound).
3. Implement `TopologyPreservation` (exact $E_s$ vs. post-rounding vs. post-reduction component count, $\chi$, and volume-sign check, respecting explicit `RoundingRemoved` / `RemovedThinSolids`).
4. Implement `InputIntentGuard` and `VerificationModes.Standard`.
5. Add unit, property, invariance (`Similarity.All()` including UTM + `2^k` scale), reuse, determinism, and seeded-failure tests (verify each check rejects injected defects that fail on the old code).
6. Benchmark wall-time overhead of `VerificationModes.Standard` vs. `None` on procedural boxes, spheres, and UTM frames (target: $\le 0.6\times$ boolean time; `0%` when `None`).

## Acceptance

- `Verification == None` is bit-identical to `master` (hash equal across all existing pipeline tests).
- `ExactQuantities` brackets exact rational volume on 100% of procedural test cases (including UTM `1e7` offset, rotated, and `2^-20..2^20` scales) with relative interval width $< 10^{-11}$ on well-conditioned solids.
- `TopologyPreservation` catches injected genus change, lost component, and flipped component sign; passes cleanly on valid multi-shell cavities and `MinThickness`-filtered results.
- `InputIntentGuard` refuses Audit F2 (outward cavity wall) and Audit F1 (`EmptyAfterRounding`) with `Status == VerificationFailed`.
- Measured overhead of `ExactQuantities | TopologyPreservation | InputIntentGuard` $\le 0.25\times$ boolean time (without `NoInventedSurface`) and full `Standard` $\le 0.6\times$ boolean time on 10k–100k-tri procedural solids.
- Full local CI, `dotnet pack` API validation, and independent review green.

## Sources and licences

Internal exact arithmetic (`ExactInterval`, `ExactMeshBoolean`); no external code ports.

## Budget and stop rule

1 M session. Stop if `ExactQuantities` interval width cannot stay $< 10^{-10}$ on UTM `1e7` meshes without `BigInteger` per triangle (use local-origin shifted exact/interval accumulation instead).
