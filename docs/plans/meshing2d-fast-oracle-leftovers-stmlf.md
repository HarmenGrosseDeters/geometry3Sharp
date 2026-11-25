# Plan: 2D meshing fast-oracle leftovers and `stmlf`

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "2D meshing: fast-oracle leftovers and stmlf"
- **Kind:** parked (owner 2026-11-16; `stmlf` blocked on inputs; fast-oracle leftovers parked while PR CI stays fast)
- **Tier:** M (test oracle / benchmark harness only — `benchmarks/Meshing2Benchmark/Oracle`, no library code change)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only. **Clean-room rule applies.**

## Current state

- Fast oracle merged (#376, #379; wide-exponent fix #975); `QualityMesh2` and `ConstrainedDelaunayMesh2` verdicts are
  unaffected.
- Parked items ([meshing2d.md](../roadmap/meshing2d.md#open-for-the-future-2026-11-15)):
  1. **Fast-oracle leftovers:** `373600 q30` Delaunay remainder (9.8 s; pass on remaining needles takes 7.5 s), plus
     corpus `segments` and `conformity` checks in `benchmarks/Meshing2Benchmark/Oracle`. Not running in PR CI today
     (only Nightly / manual benchmark runs).
  2. **`stmlf`:** blocked on obtaining the input geometry / dataset (no issue open).
  - Note: huge third-party outputs (Herne q20 fork outputs, `GlobalDelaunayIndexed` witness remainder, 72 run-A oracle
    timeouts) were **closed** by owner 2026-11-16 and are NOT part of this item.

## Trigger / decision needed

- For fast-oracle leftovers: PR CI or Nightly oracle jobs slow down noticeably (e.g., oracle tests approach the 5 s /
  10 s CLAUDE.md rule or Nightly nears its time budget).
- For `stmlf`: the owner supplies or authorizes a clean procedural equivalent / permissible source of the `stmlf` input.

## Goal

Speed up the remaining slow paths in `benchmarks/Meshing2Benchmark/Oracle` (`373600 q30` needle pass, corpus `segments`,
`conformity`) with **bit-identical verdicts** against the exact oracle, and add `stmlf` coverage once inputs exist.

## Scope

- `benchmarks/Meshing2Benchmark/Oracle/*` (needle pass filter, `segments` and `conformity` fast paths)
- `tests/geometry3Sharp.Tests/Meshing2Oracle*Tests.cs` (verify fast path == exact path on every catalogue & corrupted row)
- No library code (`comp_geom/`) touched.

## Non-goals

- **STRICT CLEAN-ROOM:** never inspect Triangle / Triangle.NET code.
- Never weaken an oracle verdict: every corrupted row rejected by the exact path must still be rejected by the fast path
  (`Meshing2OracleModesTests`).
- Do not reopen the closed Herne q20 / huge third-party output items (closed 2026-11-16).

## Interface

None public (internal benchmark oracle only).

## Look before you build

1. Library / repo: `benchmarks/Meshing2Benchmark/Oracle/*`, #376, #379, #975 (`Q.Bounds` + per-vertex/pair memoization),
   `Meshing2OracleModesTests`, `Meshing2OracleFastPathCheckTests`.
2. Literature: McConnell et al. (2011) certifying algorithms (fast filtered check falling back to exact `BigInteger` /
   interval predicate only on indeterminate cases — same pattern as #376/#975).

## Steps (one PR, clean commits)

1. Profile `373600 q30` (7.5 s needle pass) and corpus `segments` / `conformity` to measure the upper bound of exact
   fallbacks that a sound floating-point / interval filter can discharge.
2. Implement sound filtered fast paths (fallback to exact path whenever interval bounds overlap 0); prove soundness in
   a comment and test against exact path on all catalogue + corrupted + wide-exponent fuzz inputs.
3. If `stmlf` inputs become available, run the oracle + mesher on them and pin any new edge case procedurally.

## Acceptance

- 100 % identical verdicts between fast path and exact path on all catalogue rows, corrupted rows, and the 69 wide-range
  fuzz cases (`Meshing2OracleModesTests` + `Meshing2OracleFastPathCheckTests`).
- Before/after timing table on `373600 q30`, `segments`, and `conformity` (command + raw output in PR): >= 3x speedup on
  the targeted pass (or >= 10 % total oracle time per owner 2026-11-16 rule).
- All PR tests <= 5 s each.

## Sources and licences

Clean-room only (own oracle + published interval/certifying papers). No mesh files committed to the repo.

## Budget and stop rule

1 M session. Stop if the profile shows the remaining 7.5 s is irreducible exact arithmetic on genuine near-cocircular
needles (< 20 % filterable).

## Risks and open questions

- A buggy fast path in a test oracle silently hides mesher bugs: every fast-path branch must be attacked by corrupted-row
  tests that fail when the exact fallback is removed.
