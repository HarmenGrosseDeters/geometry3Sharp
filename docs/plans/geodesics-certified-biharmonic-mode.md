# Plan: certified biharmonic deformation mode (#976, #980)

- **Board row:** OPEN_TOPICS.md §4 "Parked or on hold by owner decision", row "Geodesics/param: certified biharmonic mode"
- **Kind:** parked (owner 2026-11-16; trigger: owner go to resume certification + clear gates #976 and #980)
- **Tier:** L (certified numerical error bounds, spectral forest bound, exact/interval verification, >20 s latency tail)
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

- Standard ARAP and biharmonic deformation are merged (#896, #897, 2026-11-16; issue #886 closed;
  [BIHARMONIC_DEFORMATION.md](../design/BIHARMONIC_DEFORMATION.md)).
- The **certified (strict) biharmonic mode** was parked on 2026-11-16 (branch `claude/biharmonic-certificate-parked`)
  because two hard release gates remain open:
  - **#976 (20 s tail at 10k):** strict 100×100 aligned grid took **26.45 s** under concurrent local load (rotated:
    19.28 s), exceeding the CLAUDE.md 20 s single-operation rule.
  - **#980 (100k aligned accuracy & cold latency):** forest spectral bound avoids interval $Q$ assembly and a second
    factorization, yet procedural strict runs (1e-8 position/response tolerances) still gave:
    - 50k rotated: `Success/Certified` in **25.48 s**
    - 100k aligned: **`AccuracyNotEstablished`** in **34.89 s** (repeat 36.89 s)
    - 100k rotated: `Success/Certified` in **60.03 s**

## Trigger / decision needed

Owner go to invest in certified biharmonic deformation (requires a concrete caller needing a-posteriori certified 1e-8
error bounds on biharmonic solves rather than standard double-precision biharmonic deformation). Owner also decides
whether the 20 s rule applies to 100k strict certification or if a relaxed time budget is granted for 100k.

## Goal

Make the opt-in certified biharmonic mode achieve `Success/Certified` on 100k aligned and rotated grids at 1e-8
tolerance, with 10k strict <= 5 s alone (<= 10 s under load) and 100k strict <= 20 s (or within an owner-approved budget).

## Scope

- `mesh_ops/BiharmonicDeformation.cs` (and forest certificate files from `claude/biharmonic-certificate-parked`)
- `math/SparseCholesky.cs` (if parallel / faster factorization or triangular solve is needed; bit-identical for existing
  callers)
- `benchmarks/BiharmonicDeformationBenchmark/` (`large_certificate_profile.py`)
- `tests/geometry3Sharp.Tests/BiharmonicDeformation*Tests.cs`
- `docs/design/BIHARMONIC_DEFORMATION.md`

## Non-goals

Default (uncertified) `BiharmonicDeformation` output and speed must stay bit-identical; no weakening of the 1e-8
certification soundness (a certificate must never falsely claim `Certified` when true error > tolerance).

## Interface

Opt-in `BiharmonicDeformation.Certify` (bool, default `false`), tolerance properties, and certification status enum
(`Certified`, `AccuracyNotEstablished`, etc.) — keeping exact signatures compatible with #897.

## Look before you build

1. Library: `BiharmonicDeformation`, `SparseCholesky` (incl. parallel kernels #1103), parked branch
   `claude/biharmonic-certificate-parked`, `benchmarks/BiharmonicDeformationBenchmark/large_certificate_profile.py`,
   issues #976 and #980.
2. Literature: Rump (2010, "Verification methods: Rigorous results using floating-point arithmetic", Acta Numerica —
   a-posteriori verification of SPD linear systems via residual + smallest singular value / Cholesky lower bound);
   Higham (2002, *Accuracy and Stability of Numerical Algorithms*); Jacobson et al. (2011, bounded biharmonic weights).

## Steps (one PR, clean commits)

1. **Diagnose 100k aligned `AccuracyNotEstablished` (#980):** measure whether the failure is (a) loose forest spectral
   lower bound on the aligned grid's smallest eigenvalue, or (b) genuine residual growth from unrefined double solve
   (if (b), one iterative refinement step in double-double / extended residual cuts residual by ~1e-8 cheaply!).
2. **Profile 10k/50k/100k latency (#976, #980):** break down symbolic/numeric factorization, triangular solves, residual
   computation, and forest bound assembly (self time vs GC).
3. Implement tighter spectral lower bound / iterative refinement and optimize the dominant latency stage (bit-identical
   for default uncertified mode).
4. Verify soundness with an independent higher-precision oracle (or known analytic biharmonic solutions) + invariance,
   determinism, reuse tests; close #976 and #980.

## Acceptance

- 10k aligned & rotated: `Success/Certified`, < 5 s alone (and < 20 s under load); #976 closed.
- 50k & 100k aligned & rotated: `Success/Certified` at 1e-8 tolerance, within 20 s (or owner-approved budget); #980
  closed.
- Soundness tests: intentionally perturbed solution (error = 2e-8) is **never** marked `Certified`.
- Independent bounds review (REVIEW_CHECKLIST exact/error-bound classes) + benchmark command/output in PR.

## Sources and licences

Rump (2010), Higham (2002) — papers/books only; no GPL/non-permissive code.

## Budget and stop rule

Start with a 0.5-day diagnosis of #980 (`AccuracyNotEstablished`) + latency breakdown. Stop if the 100k Cholesky
factorization alone exceeds 20 s without a supernodal/multifrontal solver (report that upper bound to the owner).

## Risks and open questions

- Supernodal Cholesky or AMD fill on 100k 2D biharmonic (squared Laplacian, wider stencil) may be the fundamental
  latency floor; measure factorization time alone first before optimizing the certificate.
