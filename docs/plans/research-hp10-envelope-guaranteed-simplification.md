# Plan: envelope-guaranteed simplification — the paper's algorithm (HP 10 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Other: envelope-guaranteed simplification, the paper's algorithm (HP 10)"
- **Kind:** research study (envelope stopped 2026-11-01; Phase 5 #2 open; trigger: need for fast certified ε-envelope simplification in `Reducer` / `Remesher`)
- **Tier:** L (certified per-collapse ε-envelope bound, exact/interval triangle-prism tests, speed vs `MaxDeviation`; study first)
- **Plan status:** draft, 2026-11-20 (Berlin); updated 2026-11-22 (Berlin): the phase 1 study is done ([ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md](../research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md), prototype in `benchmarks/ReducerBenchmark`): guaranteed, but 1.1-1.9x the triangles and mostly 5-98x the time of `MaxDeviation`; leaning no-go. Only the owner decision on the note's options (and phase 2, if the owner says go) remains.

## Current state

- Hard problem 10 ([research.md HP 10](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model),
  [alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md#stage-7-envelope-containment),
  [ENVELOPE.md](../design/ENVELOPE.md), [CERTIFIED_PATCH_BOUND.md](../design/CERTIFIED_PATCH_BOUND.md)):
  - Envelope containment stopped at its stop criterion (owner 2026-11-01; #263, #268): the polyhedral envelope check
    is too slow for interactive simplification / remeshing.
  - In `Reducer`, `MaxDeviation` currently accounts for **92-97 %** of total wall time (`Reducer.MaxDeviationTwoSided`
    was built opt-in in #791, and `SampleDeviation` + post-hoc `SimplifyWithin` certification in #840 is the fast
    workaround for alpha wraps).
  - **Related building block now merged (#848, closed 2026-11-19):** certified patch bound of `TriangleDeviationBounds`
    for `MeshHausdorffDistance.CertifiedUpper` ([CERTIFIED_PATCH_BOUND.md](../design/CERTIFIED_PATCH_BOUND.md))!
  - **Still open (Phase 5 #2 / HP 10):** implementing a cheap certified per-collapse ε-envelope check (Wang et al. 2020
    *Fast Envelope* — indirect-predicate prism containment with spatial hashing/BVH + hierarchical point/triangle
    sampling filter, or leveraging #848's certified patch bound) fast enough to run inside `Reducer` and `Remesher` as
    the foundation for an envelope-guaranteed exact pipeline (Boolean -> rounding -> certified simplification).

## Trigger / decision needed

Update 2026-11-22: phase 1 is done; what is needed now is the owner decision on the options of the [research note](../research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md). The original trigger:

A caller or pipeline goal requiring fast, **guaranteed two-sided ε-envelope** mesh simplification / remeshing (where
post-hoc `SimplifyWithin` retry is too coarse or too slow, e.g. CAD LOD / FEM simplification with strict tolerance).

## Goal

Measure whether combining Wang et al. (2020) Fast-Envelope spatial-grid culling with our newly merged certified patch
bound (#848) can cut per-collapse certified envelope checking by >= 5-10x compared to `MaxDeviation` / `PolyhedralEnvelope`,
making guaranteed simplification practical inside `Reducer`.

## Scope

- Phase 1 (study & micro-benchmark only): `docs/research/FAST_ENVELOPE_SIMPLIFICATION_STUDY.md` (re-read Wang et al.
  2020 *Fast and Exact Envelope Containment*, compare against our existing `PolyhedralEnvelope` #263/#268 and #848
  `TriangleDeviationBounds`, measure upper bound on `ReducerBenchmark`).
- Phase 2 (only after owner go): fast certified envelope filter in `spatial/PolyhedralEnvelope.cs` (or new
  `FastEnvelope3.cs`), opt-in wiring into `mesh_ops/Reducer.cs`, tests verifying 100 % agreement with exact envelope
  oracle.

## Non-goals

Never port GPL code; verify license of Wang et al. 2020 reference code (`FastEnvelope` is MIT — verify per-file headers
and check whether it bundles GPL/GMP or indirect predicates before reading any code, or implement from paper only).
Do not change `Reducer` default path.

## Interface

Phase 1: none. Phase 2 (conditional): opt-in `Reducer.EnvelopeTolerance` (or accelerated backing for `MaxDeviation` /
`MaxDeviationTwoSided`) preserving exact bit-identical or strictly certified containment.

## Look before you build

1. Library: `docs/design/ENVELOPE.md`, `docs/design/CERTIFIED_PATCH_BOUND.md` (#848), `spatial/PolyhedralEnvelope.cs`,
   `mesh_ops/MeshHausdorffDistance.cs`, `mesh_ops/Reducer.cs` (`MaxDeviation`, `MaxDeviationTwoSided` #791,
   `SampleDeviation` #840), `math/AdaptivePredicates.cs`, `tests/mutation/stryker-mesh-envelope-1..4.json`.
2. Literature:
   - Wang, Schneider, Hu, Attene, Panozzo (ACM TOG / SIGGRAPH Asia 2020), "Exact and Efficient Polyhedral Envelope
     Containment Check" (Fast Envelope: regular grid of ε-enlarged prisms + indirect predicates + two-level triangle
     containment).
   - Cohen, Varshney, Manocha, Turk et al. (SIGGRAPH 1996), "Simplification Envelopes".
   - Hu et al. (2020, fTetWild — uses Fast Envelope inside local mesh ops).

## Steps (Phase 1 study, one docs PR)

1. Profile why our existing `PolyhedralEnvelope` (#263/#268) and `Reducer.MaxDeviation` are slow per collapse (grid/BVH
   traversal vs exact prism-triangle predicates vs per-collapse allocations vs thread starvation #698).
2. Check whether #848 (`TriangleDeviationBounds`) already provides a cheap sufficient condition for 80-95 % of
   candidate collapses (falling back to `PolyhedralEnvelope` only when the cheap bound exceeds ε!).
3. Benchmark a scratch prototype of (a) #848 cheap filter + `PolyhedralEnvelope` fallback and (b) Wang et al. (2020)
   uniform-grid prism lookup on `ReducerBenchmark` (bumpy 60/130, terrain 300, 10 Thingi10K meshes).
4. Commit `docs/research/FAST_ENVELOPE_SIMPLIFICATION_STUDY.md` with exact speedup numbers and go/no-go verdict.

## Acceptance

- Study names the exact per-collapse bottleneck (stratum, self-time vs GC), compares #848-filtered envelope vs
  `MaxDeviation` vs `SampleDeviation`, and establishes whether >= 5x speedup over `MaxDeviation` is achievable with zero
  containment violations (checked by `MeshHausdorffDistance.CertifiedUpper`).

## Sources and licences

Wang et al. (2020) paper (check MIT license + third-party headers before reading any code); Cohen et al. (1996) paper.
Never GPL code.

## Budget and stop rule

1 L research session for Phase 1. Stop Phase 2 if per-collapse certified checking remains > 10x slower than unguarded
QEM reduction even with the #848 filter and uniform grid (keep `SampleDeviation` + post-hoc certification instead).

## Risks and open questions

- One-sided envelope (result within ε of input) vs two-sided envelope (input also within ε of result): two-sided
  requires tracking input-triangle dependencies per collapse (as in #791) and can still bottleneck on dense regions.
