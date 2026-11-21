# Plan: open research questions (metamorphic testing, agent APIs, showcases, sparse SDFs, Monte Carlo PDEs)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Other: open research questions (metamorphic testing, agent APIs, showcases)"
- **Kind:** research study (listed, not started as a single program; trigger: pick ONE sub-topic with a strong synergy with an existing feature per "depth over breadth", owner 2026-11-10)
- **Tier:** S/M for agent-API benchmark & showcases; M/L for sparse SDFs / Monte Carlo PDEs / expanded metamorphic suite
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

Tracked in [research.md](../roadmap/research.md#open-research-questions) lines 383-446 (this row bundles the remaining
unstarted research leads):

1. **Metamorphic testing (partially done!):** study merged 2026-11-16 ([METAMORPHIC_TESTING.md](../research/METAMORPHIC_TESTING.md),
   [METAMORPHIC_RELATIONS.md](../design/METAMORPHIC_RELATIONS.md)); relations **A1** (anisotropic cost) and **P1**
   (tiny perturbation) are **built** (#988, `MetamorphicRelationTests.cs`) and already caught real bugs (#985, #986).
   Remaining open: other parked relations from the catalogue (only if a new bug class justifies them) and optional
   black-box comparison across external kernels.
2. **Geometry APIs for coding agents (unstarted):** a benchmark of real mesh/CAD tasks solved by coding agents through
   `g3cli` / MCP (`g3cli mcp`), measuring which API patterns (clear status enums, `NotCleanReason`, options objects,
   JSON diagnostics, self-describing MCP schemas) actually reduce agent error rate and retries.
3. **Showcase domains with error bounds (unstarted):**
   - *Biomedical / orthotics:* geodesic cut lines (`MeshGeodesicPath` / `MeshGeodesicBezier`), wall thickness,
     feature-preserving offsets (`MeshOffset`), conservative TPMS lattices (`TpmsLattice`).
   - *Terrain:* parked until terrain resumes (tracked in `terrain-slope-blocks-berms-geotiff-showcase`).
4. **Other "Later" research candidates in `research.md` (unstarted, demand-driven):**
   - *Sparse SDF grids (OpenVDB/NanoVDB-style CPU tree, Museth 2013/2021):* callers would be `MeshOffset`, `TpmsLattice`,
     `DualContouring` (measure dense-grid memory/time upper bound first).
   - *Monte Carlo PDEs directly on polygon soups (Walk on Stars, Sawhney et al. 2023 / Zombie MIT):* grid-free Laplace /
     Poisson solves directly on dirty soups using `DMeshAABBTree3` + GWN (needs Phase 5 #6).
   - *Generalized winding numbers for rational curves (Spainhour et al. 2024, Axom BSD):* only if `PlanarComplex.FindSolidRegions`
     scale/near-touching probe shows a real defect.
   - *Intrinsic error-metric simplification (Liu et al. 2023):* synergy with intrinsic triangulations.

## Trigger / decision needed

Owner picks **at most one** sub-topic with the highest synergy with existing features (under the 2026-11-10 "depth over
breadth" rule). **Recommended first pick if any is chosen:** **(2) Geometry APIs for coding agents** (directly serves
the fork's #1 mission in `ROADMAP.md`: "a geometry platform that coding agents can build domain apps on", exercises
`g3cli` + MCP end-to-end, and requires zero risky core-library churn).

## Goal

Keep these exploratory research leads clearly scoped so that when one is selected, it starts with a bounded measurement
or pilot study rather than an open-ended rewrite.

## Scope

Depends on the single chosen sub-topic (one separate PR / study doc per chosen topic):
- **Sub-topic 2 (Agent API benchmark — recommended):** new `benchmarks/AgentApiBenchmark/` (10-15 procedural CAD/repair/
  boolean/tet/quad/TPMS tasks solved via `g3cli` / MCP, measuring success rate, tool-call count, and failure modes) +
  `docs/research/AGENT_API_BENCHMARK.md`.
- **Sub-topic 3 (Orthotics showcase):** procedural orthotic shell example in `docs/examples/` (compiled by
  `DocExamplesTests`) + CLI/MCP pipeline recipe exercising `MeshGeodesicPath`, `MeshOffset`, `TpmsLattice`, and
  `MeshRobustRepair`.
- **Sub-topic 1 (Metamorphic follow-ups):** only add a new relation to `MetamorphicRelationTests.cs` if a bug slips past
  A1, P1, and `Similarity.All()`.
- **Sub-topic 4 (Sparse SDF / Walk on Stars):** Stage 0 upper-bound study under `docs/research/` first (no library code).

## Non-goals

Never start more than one sub-topic at once; never pursue GPU kernels, jump flooding, or neural SDFs (explicitly
rejected by owner 2026-11-13 in `research.md`); never port GPL code; never commit mesh files.

## Interface

None until a sub-topic's study passes owner review.

## Look before you build

1. Library: `docs/roadmap/research.md` ("Later", "Phase 5", "Open research questions"), `tools/g3cli/`,
   `docs/examples/`, `tests/geometry3Sharp.Tests/MetamorphicRelationTests.cs`, `spatial/DSparseGrid3.cs`.
2. Literature: Museth (2013, VDB; 2021, NanoVDB — check Apache-2.0 license before reading any code); Sawhney & Crane
   (2020, Walk on Spheres) & Sawhney et al. (2023, Walk on Stars; Zombie MIT); Spainhour et al. (2024, Axom BSD);
   Liu et al. (2023, Intrinsic Simplification).

## Steps (for the recommended Agent API / Orthotics pilot)

1. Owner picks one sub-topic (e.g. Agent API benchmark or Orthotics showcase) and approves a tight brief.
2. Define 10-15 procedural tasks with machine-verifiable acceptance checks (closed, manifold, exact volume/thickness/
   Hausdorff bounds — no subjective visual grading).
3. Run the pilot, record concrete API/CLI friction points (missing status codes, ambiguous flags, silent defaults),
   and turn only verified high-impact friction points into small Tier S/M follow-up PRs.

## Acceptance

- Chosen pilot produces a committed report under `docs/research/` (or compiled example under `docs/examples/` verified by
  `DocExamplesTests`) with reproducible commands and quantitative findings.
- Any API improvement derived from it stays 100 % compatible with upstream 1.0.324.

## Sources and licences

Published papers and MIT/BSD/Apache-2.0 references only (cite in `docs/SOURCES.md`).

## Budget and stop rule

1 short session per pilot study. Stop any exploratory topic that does not reveal actionable defects or a >= 2x synergy
gain for an existing geometry3Sharp module.

## Risks and open questions

- Scope creep: this board row aggregates 6+ unrelated ideas — always split any selected idea into its own narrow task
  brief (AGENT_WORKFLOW §2) before starting work.
