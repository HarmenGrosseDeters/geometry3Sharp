# Plan: unread sources survey (permissive libraries, PaMO, RWTH, Minkowski, Sci. Rep.)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Other: unread sources (permissive libraries, PaMO, RWTH, Minkowski, Sci. Rep.)"
- **Kind:** parked (documented only; trigger: an active feature needs a specific reference, plus license / patent check first)
- **Tier:** S (research / license audit / reading notes — docs only; any subsequent port is a separate M/L PR with A/B)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

Tracked in [sources-surveys.md](../roadmap/sources-surveys.md#open-for-the-future-2026-11-17) (standing rule, owner
2026-11-03: every port from a permissive library is an owner decision with an A/B, and per-file headers + bundled
third-party code must be checked before reading):

1. **Other permissive libraries** (`sources-surveys.md` item 4, surveyed 2026-11-03 from GitHub `geometry-processing`):
   each row is catalogued by root `LICENSE` only — none read yet; `PyMesh`, `ManifoldPlus`, and `Directional` wait for
   verified license clarity before reading.
2. **PaMO** (`sources-surveys.md` item 7): no probes planned; **a patent check comes first** before reading or adopting
   any technique.
3. **RWTH Aachen papers & Minkowski / swept volume** (`sources-surveys.md` item 2): Campen & Kobbelt (2010, Minkowski /
   swept volume) next to `MeshOffset` (#799); not scheduled.
4. **Outer-hull and volumetric repair papers** (`sources-surveys.md` item 6): design input for repair pipeline; unread.
5. **Scientific Reports papers** (`sources-surveys.md` item 10): only Crossref metadata read (`nature.com` required
   cookie login during earlier session); full read waits for open access / PDF supply.

## Trigger / decision needed

**Demand-driven only (per "depth over breadth", owner 2026-11-10):** do NOT read these speculatively. Pull a single
entry from this backlog only when an active roadmap item hits a concrete gap that the source addresses (e.g.,
`MeshOffset` needs swept-volume Minkowski sums -> read Campen & Kobbelt 2010; a remeshing/repair feature wants a
permissive reference -> audit one library's per-file headers first).

## Goal

Provide a disciplined, license- and patent-safe intake checklist so any future session consulting one of these unread
sources produces a clean, reusable research note in `docs/research/` and updates `docs/SOURCES.md` without license
contamination.

## Scope

- Reading & audit notes only: `docs/research/<SOURCE>_SURVEY_YYYY-MM.md` (following `GEOGRAM_SURVEY_2026-11.md`)
- `docs/roadmap/sources-surveys.md`, `docs/sources/*.md` (license & attribution records)
- Zero production code changes in a survey PR.

## Non-goals

- Never read GPL / AGPL / non-commercial / all-rights-reserved code (CGAL, TetGen, Triangle, `boundaryblaze`,
  `MeshFix` GPL parts).
- Never implement patented techniques without owner patent review (specifically PaMO).
- Never port code without an owner-approved A/B benchmark PR.

## Interface

None (docs / research only).

## Look before you build

1. Library: `docs/roadmap/sources-surveys.md`, `docs/research/GEOGRAM_SURVEY_2026-11.md` (template for library surveys),
   `docs/research/GEOGRAM_MEASUREMENTS_2026-11.md`, `docs/SOURCES.md`.
2. Compliance checks: root `LICENSE`, per-file SPDX headers, bundled `third_party/` directories (to avoid hidden GPL
   submodules), patent disclosures on paper/project pages.

## Steps (when a single item is triggered; one S docs PR per source)

1. **License / patent gate first:** verify root LICENSE + per-file headers + third-party folders (for libraries) or
   patent status (for PaMO). Stop immediately and mark "forbidden" if GPL/AGPL/non-commercial or unencumbered patent
   status is unclear.
2. Read the target paper or permissive module strictly scoped to the triggering problem.
3. Write a concise survey note under `docs/research/` (what it solves, algorithmic idea, fit with geometry3Sharp's
   exact/determinism rules, top 1-3 measurable leads, explicit go/no-go recommendation for an A/B probe).
4. Update `docs/roadmap/sources-surveys.md` and `docs/sources/*.md`.

## Acceptance

- Survey note records exact commit SHA / DOI / license checked, distinguishes "read in full" vs "abstract/metadata
  only", and names concrete next-step A/B metrics (or explicit no-go).
- Docs-only PR: re-read diff, no CI required.

## Sources and licences

Boost / MIT / BSD / Apache-2.0 libraries and published papers only. Never GPL/AGPL/Triangle/company code.

## Budget and stop rule

Max 2-4 hours per triggered source (fresh short session per AGENT_WORKFLOW §8). Stop immediately on any license or
patent ambiguity and escalate to the owner.

## Risks and open questions

- Permissive top-level repositories frequently vendor GPL/LGPL/non-commercial sub-libraries (e.g., Triangle or TetGen
  inside geometry toolkits, as found in Geogram): always inspect per-file headers before opening any implementation file.
