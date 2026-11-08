# Z3 verification pilot

Tier M, verification tools/tests only. Implements the two bounded tasks from
ROADMAP's 2026-11-04 SMT pilot: SnapGrid.Pixel's fast branch, and one documented
surviving boundary-margin mutant from #376. No geometry, arithmetic, error bounds,
API or mutation tooling changes. Stop after these tasks; do not generalize to
large Orient/InCircle bounds or whole Boolean correctness.

The pinned `z3-solver==4.15.4.0` package is MIT, in this separate Python tool only.
It bundles a native Linux solver; neither a NuGet reference nor a native solver
is added to geometry3Sharp. Python bindings were chosen for this bounded pilot
because they expose the same Z3 IEEE-754 theory directly and bundle the native
library. The roadmap's Microsoft.Z3 NuGet option remains an alternative for a
future .NET verification project, not an additional dependency here.

```sh
python3 -m venv /tmp/g3-z3
/tmp/g3-z3/bin/pip install --require-hashes -r tools/Z3Pilot/requirements.txt
/tmp/g3-z3/bin/python tools/Z3Pilot/pilot.py --out /tmp/z3-report.json
/tmp/g3-z3/bin/python -m unittest discover -s tools/Z3Pilot -p 'test_*.py' -v
# Optional monolithic experiment; UNKNOWN never counts as proof:
/tmp/g3-z3/bin/python tools/Z3Pilot/pilot.py --direct --out /tmp/z3-direct.json
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~SnapGridPixelFilterTests|FullyQualifiedName~Meshing2OracleZoneFilterTests'
```

## What is proved, and what is assumed

The default gate checks six quantifier-free **binary64** counterexample queries,
with roundNearestTiesToEven for each floating operation and roundTowardNegative
for Math.Floor. Each must return UNSAT; SAT and UNKNOWN both fail. Every obligation also checks
that its assumptions are SAT, so an impossible guard cannot produce vacuous success. A separate
controlled mutant must return SAT and its exact-rational replay must differ.
The normal source methods are checked against reviewed source contracts before
running any solver query. This couples the model to current code, rather than
silently validating a stale transcription. The .NET tests replay actual methods.

This is a **modular proof**: Z3 proves the finite IEEE-754 lemmas below; the
bridges using monotonic rounding and binary64 representability are mathematical
steps stated explicitly here. It is not a monolithic machine-checked theorem
about x/W and PixelExact, nor a proof of the entire mesher. It assumes standard
IEEE binary64 RNE operations, including division and square root, and no extended
intermediate precision or fused contraction. Inputs of the theorem are finite x,
finite W>0; other widths and exact fallback behavior remain outside the proof.

### Pixel: floor(x/W + 1/2)

Let q be the exact real x/W, u=RN(q), r=RN(u+1/2), f=floor(r).
The model lets u range over **all binary64 values**, an over-approximation of every
division result, including overflow, underflow and NaN. A non-finite r cannot
satisfy the fast-branch guard f<r.

Z3 finds no counterexample to:

1. `f<r` implies `-2^52 <= f < 2^52`, so the cast to Int64 is safe.
2. If `f>-2^52`, acceptance implies `u>f-1/2` (the subtraction is an RNE FP operation).
3. At `f=-2^52`, acceptance implies `u>f-1`, hence `u>=f` by binary64 spacing.
4. Acceptance implies `u<f+1/2` (the addition is an RNE FP operation).

Bridges: f is integral. In the regular range both f-1/2 and f+1/2 are exactly
representable. Monotonic RNE and RN(t)=t for a representable t imply:
`u>f-1/2 => q>=f-1/2`, and `u<f+1/2 => q<f+1/2`.
At the lower endpoint, f-1/2 is the midpoint between f-1 and f. If q were below
that midpoint, RNE(q) would be at most f-1, contradicting (3). Therefore in every
accepted case `f <= q+1/2 < f+1`, the exact index. The integer-rounded cases are
left to PixelExact, as before. Equality cannot be admitted indiscriminately.

The optional direct query includes fp.div, fp.to_real and real products with W.
The captured run returns **UNKNOWN (incomplete arithmetic reasoning)**; earlier
bounded attempts also timed out. This is recorded separately and
contributes nothing to the proof. A SAT direct result is a counterexample and
fails the entire pilot, even when the modular obligations pass. The useful result is the six small UNSAT
obligations plus these reviewed bridges, not a claim that the direct query passed.

### A genuine equivalent survivor from #376

`docs/design/MESHING2D_FAST_ORACLE.md` records surviving single-margin mutations
of DistanceQueryBox. This pilot chooses **remove the final `+1e-300`** in
`reach = sqrt(max(0,d2hi)) * (1+1e-12) + 1e-300`.

For d2>=0 and a successful Q.Bounds, its rounded quotient v is nonnegative.
The fifth SMT obligation proves that
`RN(v + RN(RN(v*1e-13)+1e-300)) >= binary64(1e-300)`.
If an intermediate overflows, both original and mutant refuse the box.
Otherwise monotonic correctly rounded sqrt gives at least sqrt(binary64(1e-300));
multiplication by binary64(1+1e-12)>1 cannot reduce a nonnegative value. This is
larger than binary64(1e-151), a deliberately loose lower bound. The sixth
obligation proves for **every binary64 b>=1e-151**, including +infinity, that
`RN(b+binary64(1e-300)) == b`. Thus the removed addition changes no bits of reach,
no output bound, and no return value. It is equivalent under the actual caller's
preconditions; no invented mutant-killing test is needed and no production code
is deleted. The native test compares all four actual query bounds with the
single-margin mutant on zero, dyadic and rational squared radii over large scales,
at a UTM-sized point and at the origin and tiny coordinates. The small points matter:
at UTM magnitudes (ulp about 1e-10) a pad changed to 1e-20 or even 1e-12 would be
absorbed as well, while near zero with d2=0 such a change alters the box bits, so the
test fails for those mutants (checked) and passes only for the proved-equivalent one.

Not covered: `Oracle.cs` has two further identical pads,
`reach = Math.Sqrt(t2hi) * (1 + 1e-12) + 1e-300` for the tube in the domain check (around
line 1820) and in `Filters()` (around line 1847), and a similar one with `1 + 1e-9` (around
line 2057). The model and the source contract cover only `DistanceQueryBox`; the same
argument likely applies there (`t2hi` also comes from `Q.Bounds`), but it is not checked.

### A SAT witness pinned as a regression

For a deliberately wrong Pixel guard `f<=r`, Z3 returns
`x bits=4602678819172646911`, `x=0x1.fffffffffffffp-2`, W=1.
The exact fraction plus 1/2 is less than 1 (pixel 0); the rounded sum is 1 (mutant
pixel 1). Python Fraction checks this independently. The new native test
`SnapGridPixelFilterTests.SmtWitness_IntegralRoundedSumMustTakeTheExactPath`
pins these exact bits and replays power-of-two scalings through Pixel/PixelExact.
This witness concerns a **controlled new fault**, not a discovered production bug.

## Pilot outcome and sources

The captured report and validation output live in `results/`. The source/solver
contracts, negative drift checks, UNKNOWN handling, and actual .NET replay are
part of acceptance. CI (`.github/workflows/z3-pilot.yml`, read-only token, hash-pinned
`pip install --require-hashes`) runs the bounded gate for changes to this tool or its
modeled source files. The solver runs once, inside
`test_all_obligations_and_exact_witness_replay`, with a 120 s per-query timeout
(`Z3_PILOT_TIMEOUT_MS`; the slowest query took 8.4 s locally, so the 20 s default of
`pilot.py` leaves little headroom and a timeout would read UNKNOWN), and its report
(`Z3_PILOT_REPORT`) is uploaded as an artifact. No solver is required to run the
ordinary .NET suite.

Recommendation: use this modular method for small FP filters and finite rule
systems, with explicit source coupling, counterexample replay and independent
review. Do not require a solver to solve mixed FP/nonlinear-real formulas, and
do not turn UNKNOWN into success. A new filter still needs its own derivation and
review; this pilot does not certify a reusable generic error-bound prover.

Prior art / building-block check:

- Existing `SnapGrid.Pixel`, `PixelExact`, `SnapGridPixelFilterTests`, Oracle's
  `DistanceQueryBox`, `Q.Bounds`, and documented #376 survivors were inspected.
- [SMT-LIB FloatingPoint theory](https://smt-lib.org/theories-FloatingPoint.shtml):
  binary64 format, RNE, roundToIntegral, comparisons, fp.to_real semantics.
- [Z3 Python API](https://z3prover.github.io/api/html/namespacez3py.html): solver
  status, FP constructors/operators and exact IEEE-bit extraction.
- [Z3 source/license](https://github.com/Z3Prover/z3): MIT; only public API and
  package metadata are used. No external geometry implementation was read.
