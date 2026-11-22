# Plan: 2D meshing oracle speed — certifying-algorithm design (HP 7 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "2D meshing: oracle speed, certifying design (HP 7)"
- **Kind:** research study (fast oracle stopped at its stop rule: Herne 3.6x mesher vs <= 2x target; trigger: oracle speed becomes a bottleneck for CI / fuzzing / Nightly)
- **Tier:** L (clean-room certifying-algorithm proof + equivalence argument per replacement; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). **STRICT CLEAN-ROOM RULE APPLIES.** Updated 2026-11-22 (Berlin): the phase 1 study is done ([MESHING2D_HP6_HP7_SPIKE_2026-11.md](../research/MESHING2D_HP6_HP7_SPIKE_2026-11.md), prototypes in `benchmarks/Meshing2HardProblems`): a certifying checker is feasible for the core of the specification (prototype 1 to 13 % of the mesher time); its option 1 (speed up the two scans inside today's oracle) is merged (PR `#1353`, refs issue `#1319`, which is still open). The certifying core (option 2) waits on the owner decision on the note's options.

## Current state

- Hard problem 7 ([research.md HP 7](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model),
  [meshing2d.md](../roadmap/meshing2d.md)):
  - Fast oracle (#376, #379, #975) hit its stop rule: on Herne it runs at **3.6x** the mesher (target was <= 2x); on
    wide-exponent fuzz inputs (#975), the oracle takes 2-4.4 s per input (mostly `segments` exact selection/chain
    checks, `coverage`, and `marks`).
  - Remaining expensive oracle checks without a certifying fast design:
    1. **Coverage under the Q17 chain rules** (verifying the output triangulation covers the exact PSLG domain without
       gaps, overlaps, or inverted regions, accounting for Q17 collapsed chains).
    2. **Conforming-mode completion witness** (verifying global Delaunay empty-circle property and constraint conformity
       without running exact `BigInteger` checks on every edge/triangle).

## Trigger / decision needed

Update 2026-11-22: the study is done; what remains is the owner decision on the options of the [research note](../research/MESHING2D_HP6_HP7_SPIKE_2026-11.md). The original trigger:

Oracle runtime in Nightly, fuzzing campaigns (`MeshFuzzPilot`), or PR CI becomes a binding bottleneck and warrants
designing a true certifying-algorithm witness protocol (McConnell et al. 2011) between `QualityMesh2` and the oracle
without compromising oracle independence (`docs/design/ACCEPTANCE_STRATEGY.md`).

## Goal

Design a provably equivalent certifying-algorithm witness format and linear-time independent checker for (1) Q17
coverage and (2) conforming-mode Delaunay/segment completion, cutting oracle time below 1x-2x the mesher while
preserving 100 % independence and zero false passes.

## Scope

- Phase 1 (study only): `docs/research/MESHING2D_CERTIFYING_ORACLE_STUDY.md` (witness format, checker algorithm,
  formal equivalence proof showing `checker(input, output, witness) == true => spec(input, output) == true`, and
  fault-injection analysis).
- Phase 2 (only after owner go): opt-in witness emission in `comp_geom/QualityMesh2.cs` / `ConstrainedDelaunayMesh2.cs`
  (zero cost when off), checker in `benchmarks/Meshing2Benchmark/Oracle/*`, corrupted-witness tests in
  `tests/geometry3Sharp.Tests/Meshing2Oracle*Tests.cs`.

## Non-goals

- **STRICT CLEAN-ROOM:** never open or read Triangle / Triangle.NET code.
- Never trust an unverified witness from the mesher: per McConnell et al. (2011), even a malicious/buggy witness must
  never trick the checker into accepting an invalid mesh!
- Do not reopen the closed Herne q20 huge third-party output item (closed 2026-11-16).

## Interface

Phase 1: none. Phase 2 (conditional): internal/opt-in witness struct passed to the oracle (no public API break).

## Look before you build

1. Library: `docs/design/ACCEPTANCE_STRATEGY.md`, `docs/design/MESHING2D_SPEC.md`, `benchmarks/Meshing2Benchmark/Oracle/*`,
   `tests/geometry3Sharp.Tests/Meshing2OracleModesTests.cs`, #358 (Q17), #376, #379, #975.
2. Literature (papers only):
   - McConnell, Mehlhorn, Näher, Schweitzer (Computer Science Review 2011), "Certifying Algorithms" (specifically planar
     subdivision / Delaunay / convex-hull certification: local edge-Delaunay + topological winding-number / Euler
     characteristic check => global Delaunay in $O(n)$!).
   - Devillers, Liotta, Preparata, Tamassia (1998), "Checking the convexity of polytopes and the planarity of
     subdivisions" (linear-time planarity & coverage certificates).

## Steps (Phase 1 study, one docs PR)

1. Profile the exact breakdown of the remaining oracle cost on Herne and the 69 wide-exponent fuzz inputs (`segments`,
   `coverage`, `marks`, `conformity`, `GlobalDelaunay`).
2. Apply the McConnell et al. (2011) theorem:
   - Global Delaunay: prove that local `InCircle` on every non-constrained interior edge + positive orientation of every
     triangle + correct boundary winding/Euler count implies the global empty-circle property in $O(n)$ (eliminating
     spatial witness searches!).
   - Q17 coverage & segment chains: design a chain-map witness (mapping each input segment to an ordered list of output
     half-edges or Q17 collapsed vertex) verifiable in $O(|E|)$ using exact `Orient2d` / collinearity checks.
3. Write the equivalence proof for each replacement and list adversarial corrupted-mesh / corrupted-witness tests.
4. Commit `docs/research/MESHING2D_CERTIFYING_ORACLE_STUDY.md` with estimated speedup and go/no-go verdict.

## Acceptance

- Study includes exact per-part timing breakdown, formal equivalence proof (soundness + completeness) for every
  proposed checker replacement, and an explicit list of fault-injection tests (inverted triangle, missing hole, swapped
  chain, non-Delaunay flip, bogus witness).

## Sources and licences

McConnell et al. (2011), Devillers et al. (1998) — papers only. Strictly no Triangle / Triangle.NET code.

## Budget and stop rule

1 L research session. Stop if the profile shows exact `BigInteger` coordinate arithmetic on single `Orient2d`/`InCircle`
calls (rather than algorithmic $O(n \log n)$ / $O(n^2)$ lookups) accounts for > 80 % of the remaining time.

## Risks and open questions

- Subtle edge case of the local-to-global Delaunay theorem: it requires verifying that the triangulation is a simple
  embedding (no double-covering sheets / winding number == 1 everywhere in the domain, 0 outside) — omitting the
  embedding check allows an invalid folded mesh to pass local `InCircle`!
