# Plan: golden masters across runtimes vs bit-for-bit determinism (research study)

- **Board row:** OPEN_TOPICS.md §5 "Research / hard open problems (no known solution; a study first)", row "Platform/QA/CI: golden masters across runtimes vs bit-for-bit"
- **Kind:** research study (lead untried; trigger: golden-master test drift between `net48`, `net10`, x64, and ARM64 or Stage 3 determinism design)
- **Tier:** S/M (testing / acceptance methodology study — `docs/design/ACCEPTANCE_STRATEGY.md` + `DeterminismHarness`)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Recorded in [hotspots.md](../roadmap/hotspots.md#open-for-the-future-2026-11-15) lines 58, 73 and
  [ACCEPTANCE_STRATEGY.md](../design/ACCEPTANCE_STRATEGY.md) §4:
  - Golden masters (exact hashes / digests of accepted mesh outputs, as in the prism fixtures, `TetRefinementWorkBoundTests`
    16 fingerprints, `Meshing2` golden rows, and `DeterminismHarness`) catch unintended regressions immediately.
  - **Tension:**
    - For **exact-arithmetic & `CoreMath` paths** (booleans, 2D meshing, terrain slopes, geodesics), bit-for-bit hashes
      match across `net48` / `net10` and x64 / ARM64 (proven/probed via HP 11).
    - For **floating-point operations** (`Remesher`, `Reducer`, `QuadRemesher`, `DualContouring`, `SparseCholesky`,
      `TpmsLattice` using `System.Math`), a 1-ulp libm or JIT difference across OS/CPU/runtime can break a strict
      byte-hash golden master even when the geometry is mathematically equivalent!
  - Untried lead: establish a principled boundary & testing policy deciding when a golden master should assert **exact
    bit-for-bit digest** vs **canonical geometric/topological invariants** (topology hash + bounded Hausdorff/volume/angle
    tolerance), using what the cross-runtime golden subset actually finds as empirical material for HP 11.

## Trigger / decision needed

Owner wants a unified golden-master policy across CI runtimes (`net48` Windows, `net10` Linux x64, ARM64, and future
`.NET 11`), or existing golden-master tests begin flapping when a new runtime/CPU leg is added.

## Goal

Audit every golden-master test in the repository across `net48`, `net10` x64, and `net10` ARM64, classify which
operations are genuinely bit-for-bit invariant vs tolerance-bound, and codify the golden-master standard in
`docs/design/ACCEPTANCE_STRATEGY.md` and `docs/QA.md`.

## Scope

- Study & audit only: `docs/design/GOLDEN_MASTER_POLICY.md` (or new section in `docs/design/ACCEPTANCE_STRATEGY.md`),
  `docs/QA.md`.
- Diagnostic probe in `tools/DeterminismHarness/` running all existing golden-master fixtures on `win-x64` (`net48`,
  `net10`), `linux-x64`, and `linux-arm64` via `determinism.yml`.

## Non-goals

Never weaken bit-for-bit assertions on exact-arithmetic or `CoreMath`-backed operations (those must stay strictly
bit-identical); never hide real nondeterminism bugs (hash-order / thread-schedule bugs) behind loose tolerances!

## Interface

None public (internal test harness helpers only, e.g. `MeshCanonicalSignature` separating combinatorial topology digest
from coordinate tolerance if adopted).

## Look before you build

1. Library: `docs/design/ACCEPTANCE_STRATEGY.md` §4, `docs/design/DETERMINISM_PROOF.md`, `tools/DeterminismHarness/`,
   all `*Golden*` / fingerprint tests in `tests/geometry3Sharp.Tests/` (`grep -rn 'Digest\|Fingerprint\|Golden\|Hash' tests/`).
2. Prior art: IEEE 754 reproducible testing in scientific libraries (CGAL / Eigen / libigl separate exact-kernel
   combinatorial equality from floating-point geometric epsilon checks; LLVM/Rust/NumPy distinguish bit-exact kernels
   from ULP-bounded numeric kernels).

## Steps (study PR, clean commits)

1. Inventory every golden-master / digest / fingerprint test currently in `tests/geometry3Sharp.Tests/` and group by
   operation class (exact, `CoreMath`, pure FP, parallel FP).
2. Run them via `determinism.yml` across `linux-x64`, `win-x64` (`net10` + `net48`), and `linux-arm64`; record every
   case that matches bit-for-bit vs differs (and why: libm, FMA, `Double.ToString`, sort tie, parallel reduction).
3. Classify each operation into Tier 1 (strict bit-for-bit golden hash across all platforms) vs Tier 2 (combinatorial
   topology hash + geometric bound + same-runtime bit-for-bit repeatability).
4. Write the policy into `docs/design/ACCEPTANCE_STRATEGY.md` and `docs/QA.md` (linking to HP 11).

## Acceptance

- Complete inventory table of all existing golden-master tests with live cross-platform run IDs (`determinism.yml`).
- Clear, enforceable checklist for authors and reviewers (added to `docs/QA.md`) distinguishing Tier 1 vs Tier 2 golden
  masters; any true nondeterminism defect uncovered gets its own `bugfix` issue.

## Sources and licences

Own test suite & CI infrastructure.

## Budget and stop rule

1 short S/M session + 1 `determinism.yml` dispatch run. Stop after documenting the policy (do not refactor passing
tests unless a real cross-platform failure is found).

## Risks and open questions

- Relaxing a golden test from bit-for-bit to tolerance-based can accidentally mask schedule nondeterminism: Tier 2
  must STILL enforce same-platform bit-for-bit repeatability (serial == parallel, repeated runs) and relax only across
  different OS/CPU/runtime boundaries.
