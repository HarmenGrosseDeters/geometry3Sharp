# Plan: 2D meshing small-angle cascade without a weaker guarantee (HP 6 — research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "2D meshing: small-angle cascade without a weaker guarantee (HP 6)"
- **Kind:** research study (current answer: exemption up to 60°, #496; trigger: a real input or spec need for a tighter proven bound near small input angles)
- **Tier:** L (clean-room mathematical proof + spec + independent oracle update; study first)
- **Plan status:** draft, 2026-11-20 (Berlin). **STRICT CLEAN-ROOM RULE APPLIES.** Updated 2026-11-22 (Berlin): the phase 1 study is done ([MESHING2D_HP6_HP7_SPIKE_2026-11.md](../research/MESHING2D_HP6_HP7_SPIKE_2026-11.md), prototypes in `benchmarks/Meshing2HardProblems`): the proof of Miller, Pav and Walkington (2005) does not carry over to the library's refiner; leaning no change of the mesher. Its option 1 (the oracle reports the excused triangles below the paper's floor) is merged (PR `#1358`). The proven rule as a mode (option 2) waits on the owner decision on the note's options and a use case.

## Current state

- Hard problem 6 ([research.md HP 6](../roadmap/research.md#hard-open-problems-candidates-for-a-stronger-model),
  [meshing2d.md](../roadmap/meshing2d.md)):
  - Owner 2026-11-06 decided option D (keep the exemption up to 60°, #496; Terminator option (c) not built).
  - Problem: near acute input corners ($\phi < 60^\circ$), standard Delaunay refinement cascades (ping-pong splits
    between encroached segments). Shewchuk's Terminator breaks the circumcentre link, **but** leaves skinny triangles
    outside the oracle's current excuse region; conversely, excusing everything near angles up to 60° weakens the
    quality guarantee more than necessary.
  - Open research directions:
    1. A **provably tight region-based oracle excuse** matching the theoretical floor $\arcsin(\sin(\phi/2)/\sqrt{2})$
       near acute-angle clusters (Miller, Pav, Walkington 2003 / Shewchuk 2000).
    2. Or a **new clean-room refinement rule with a termination & angle proof** that preserves the full angle bound
       outside the minimal unavoidable cluster neighbourhood.

## Trigger / decision needed

Update 2026-11-22: the study is done; what remains is the owner decision on the options of the [research note](../research/MESHING2D_HP6_HP7_SPIKE_2026-11.md). The original trigger:

A real 2D meshing use case where the current 60° exemption hides unacceptable skinny triangles far from acute corners,
or an owner decision to fund a clean-room proof study (option B reopenable per owner 2026-11-06).

## Goal

Produce a clean-room mathematical study deriving either (a) a provably tight cluster-region oracle excuse or (b) a
proven refinement rule that terminates on arbitrarily small input angles without leaving unexcused skinny triangles.

## Scope

- Phase 1 (study only): `docs/research/MESHING2D_SMALL_ANGLE_STUDY.md` (clean-room proof, oracle region definition,
  falsification checks on the existing 2D catalogue & corpus acute-corner fixtures).
- Phase 2 (only after owner approves spec update): `docs/design/MESHING2D_SPEC.md`,
  `benchmarks/Meshing2Benchmark/Oracle/*`, `comp_geom/QualityMesh2.cs`.

## Non-goals

- **STRICT CLEAN-ROOM:** never open, read, or inspect `triangle.c`, Triangle.NET, or any fork of them. Any agent that
  has seen that code must not work on this item.
- Do not change default `QualityMesh2` outputs during Phase 1.

## Interface

None (study only).

## Look before you build

1. Library: `docs/design/MESHING2D_SPEC.md`, `docs/roadmap/meshing2d.md`, #394, #496, `comp_geom/QualityMesh2.cs`,
   `benchmarks/Meshing2Benchmark/Oracle/*`.
2. Literature (published papers only):
   - Shewchuk (SoCG 2000), "Mesh Generation for Domains with Small Angles" (Terminator algorithm, concentric shells,
     proof of termination and angle floor).
   - Miller, Pav, Walkington (IMR 2003 / SIREV 2005), "When and why Ruppert's algorithm works" (local feature size
     clusters and exact angle floor around acute vertices).
   - Rand & Walkington (IMR 2008/2009), "Collars and Intestines: Practical Conforming Delaunay Refinement" (explicit
     collar geometry isolating acute corners with proven bounds).
   - Cheng, Dey, Shewchuk (2012), *Delaunay Mesh Generation* (Ch. 6: small input angles).

## Steps (Phase 1 study, one docs PR)

1. Audit every catalogue and corpus input that currently relies on the 60° exemption (#496): measure how far from acute
   vertices the excused skinny triangles actually lie and what their minimum angle is relative to
   $\arcsin(\sin(\phi/2)/\sqrt{2})$.
2. Evaluate Rand & Walkington's collar construction vs Shewchuk's concentric-shell Terminator with a tight region
   excuse: write the exact geometric definition of the excuse region using only exact predicates (`Orient2d`, squared
   distances / exact rationals) so the independent oracle can verify it without ambiguity.
3. Verify on the acute-angle catalogue rows (including 0.84° corners like `earcut/water-huge`) that the proposed rule
   terminates and shrinks the excused triangle count measurably.
4. Record clean-room provenance + study note in `docs/research/MESHING2D_SMALL_ANGLE_STUDY.md`.

## Acceptance

- Study contains a self-contained proof sketch of termination and angle floor, exact oracle predicate definition,
  before/after excused-triangle counts on the acute corpus fixtures, and clean-room provenance declaration.

## Sources and licences

Published papers only (Shewchuk 2000, Miller-Pav-Walkington 2003/2005, Rand-Walkington 2008/2009, Cheng-Dey-Shewchuk
2012). Strictly forbidden: Triangle / Triangle.NET code.

## Budget and stop rule

1 L research session. Stop if the collar/shell construction bloats vertex counts by > 3x on normal acute corners or
cannot be checked by an independent exact oracle in practical time.

## Risks and open questions

- Interacting clusters of multiple adjacent acute angles (e.g. a comb or fan of < 30° wedges) are where simple single-vertex
  shells break down; the study must explicitly test multi-vertex acute clusters.
