# Z3 verification pilot

Tier M, verification tools/tests only. Implements the two bounded tasks from
ROADMAP's 2026-11-04 SMT pilot: SnapGrid.Pixel's fast branch, and one documented
surviving boundary-margin mutant from #376. No geometry, arithmetic, error bounds,
API or mutation tooling changes. Stop after these tasks; do not generalize to
large Orient/InCircle bounds or whole Boolean correctness.
Pilot 2 (step 2, section "Pilot 2" below, `filter_rules.py`) adds the per-operation error-bound rules of
`tools/PredicateCompiler`, one lemma per rule; still no whole predicate.

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
(Since #947 v is `Quotient(n, d)` when a part of the quotient overflows a double: the leading 62 bits of both,
scaled by powers of two; for n>=0 it is >= 0 too, and the pad formula is the same. The obligations below take v as
any nonnegative binary64, so they do not depend on how v was computed; the source contract pins the method's text.)
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

## Pilot 2: the error-bound rules of the predicate generator

Step (2) of pilot 2 (docs/roadmap/research.md, "SMT checks with Z3"): the rules from which
`tools/PredicateCompiler` derives the static error bounds of its floating-point filters
(`Compiler.cs`, `filter_bounds`, `emit_filter_value`, `emit_filter_decision`; today the filters of
`Orient3dLPI` and `Orient3dTPI` in `math/Generated/IndirectPredicates.cs`) are stated one lemma per rule and
checked by Z3 (`filter_rules.py`). No whole predicate is proved; the composition over an expression graph is
stated below and checked without Z3. No library or generator code changed: every rule held.

```sh
/tmp/g3-z3/bin/python tools/Z3Pilot/filter_rules.py --out /tmp/z3-filter-rules.json
```

The acceptance is the same as in pilot 1: every obligation (assumptions AND NOT claim) returns UNSAT, its
assumptions return SAT on a fixed witness (and the real-arithmetic witnesses also hold when re-evaluated with
Python fractions), and every rule has at least one controlled mutant that returns SAT with a witness that is
replayed outside Z3 (exact fractions, or Python's own binary64 arithmetic). SAT on an obligation, UNKNOWN anywhere,
an UNSAT mutant or a witness that does not replay all fail the run. Before any solver call the rule code of
`Compiler.cs` is compared with the transcription in `filter_rules.py` (`source_contracts`; the constants
u = 2^-53, 960, 52, -62 and 1000 are read back from it), and the generated filters are re-derived from the
generated code (`composition_check`).

### The rules and their lemmas

Notation: for every value of the filter, `e` is the exact value of the formula on the (exact double) inputs, `e~`
the computed double, `m~` the computed magnitude (the permanent: the same graph on absolute values, sums for
differences), and the invariant is `|e~ - e| <= c m~` and `|e~| <= m~`. `u = 2^-53`.

| rule | Compiler.cs | lemma (assumptions => claim) | model | mutants (SAT, replayed) |
|---|---|---|---|---|
| R0 value rule | `value_bounds` | `|x| <= Mx, |y| <= My => |x +- y| <= Mx + My, |x y| <= Mx My` | reals (NRA) | product bound `Mx + My` |
| R1 input | `c = 0`, `m~ = |x|` | definitional (`e~ = e = x`), no lemma | | |
| R2 leaf (input +- input) | `c = u`, `m~ = |e~|` | `e = e~ (1 + d), |d| <= u => |e~ - e| <= u |e~|` | NRA, Higham (2.5) | `c` one ulp below u (and the tight binary64 case `1 + 2^-53 -> 1`, error exactly `u |e~|`); the leaf rule applied to an operand with an error (`cx > 0`: cancellation) |
| R3 sum / difference | `c = (u + max(cx, cy)) / (1 - u)`, `m~ = fl(m~x + m~y)` | operand invariants, `e~ = (x~ +- y~)(1 + d1)`, `m~ = (m~x + m~y)(1 + d2)`, `|di| <= u`, `M >= cx, cy` => `|e~ - e| <= (u + M) / (1 - u) m~` (one obligation each for + and -) | NRA, Higham (2.4) | without `/ (1 - u)`; `cx` instead of the max |
| R4 product | `c = (u + cx + cy + cx cy) / (1 - u)`, `m~ = fl(m~x m~y)` | operand invariants, `e~ = x~ y~ (1 + d1)`, `m~ = m~x m~y (1 + d2)` => `|e~ - e| <= c m~` | NRA, Higham (2.4) | without `cx cy`; without `/ (1 - u)` |
| M magnitude | `|e~| <= m~` ("rounding is monotone") | `|x~| <= m~x, |y~| <= m~y => |RN(x~ +- y~)| <= RN(m~x + m~y)`, `|RN(x~ y~)| <= RN(m~x m~y)`; plus, bit-precise in binary64: `-RN(a + b) = RN(-a + -b)`, `|RN(a b)| = RN(|a| |b|)` | reals with RN uninterpreted, monotone and odd (quantified); binary64 RNE | magnitude of a difference taken as `fl(m~x - m~y)` (binary64) |
| R5 decision | `if (!(Math.Abs(e~) > K * m~)) return 2;`, `K = CeilDouble(c / (1 - u))` | `|e~ - e| <= c m~, m~ > 0, K (1 - u) >= c, |e~| > K m~ (1 + d), |d| <= u => e e~ > 0` | NRA | `K = c`; `>=` instead of `>` |
| R6 zero shortcut | `if (m~ == 0) return 0;` | `|e~ - e| <= c m~, |e~| <= m~, m~ = 0 => e = 0` | NRA (`c m~`) | testing `e~ == 0` instead of `m~` |
| E gate exponents | `GateLow = 960 / k - 52`, `K >= 2^-62` | `k >= 1, 1 <= j <= k, a = 960 div k - 52 >= 1 => j (a + 52) <= 960` and `-62 - 960 >= -1022` | integers (NIA) | `GateLow` one larger; `K >= 2^-63` |
| G grid (no underflow) | the comment "No underflow" | binary64 `|x| >= 2^52 => x` integral; with precision 53 and a 15-bit exponent: `x, y` integral => `RN(x + y)`, `RN(x - y)`, `RN(x y)` integral (or infinite) | binary64 / (15, 53) RNE | threshold `2^51`; the frame without its range assumption (a subnormal product differs) |
| O overflow | "an overflow would give inf or NaN in m~" | binary64: `m~x, m~y >= 0` or NaN, and `fl(m~x + m~y)` (or `fl(m~x m~y)`) finite => `m~x, m~y` finite and not NaN; `m~ = +inf` or NaN, `K > 0` finite => neither `m~ == 0` nor `|e~| > fl(K m~)` | binary64 RNE | NaN counted as finite (`0 * inf`); the test written `<=` (a NaN magnitude would decide) |

What the lemmas give together, by structural induction over the expression graph in topological order (a shared
value is one node, so the invariant of a value is used by all its users): for every value whose `m~` is finite,
`|e~ - e| <= c m~` and `|e~| <= m~` with the `c` of the generator; a sign factor is then decided only if its sign is
right (R5, R6), so the product of the decided factors' signs is the predicate's sign. The no-underflow part: the
gate admits inputs 0 or of magnitude in `[2^-a, 2^b]`; a nonzero input is then a multiple of `2^-(a + 52)` (G,
scaled by a power of two); a value of degree `j` is a sum (homogeneous: equal degrees) of products of `j` such
inputs, so by G it is a multiple of `2^-(j (a + 52))`, and nonzero values and magnitudes are at least `2^-960` (E).
So every exact result of an operation is 0 or at least `2^-960` in magnitude and the rounding model of R2-R5 holds
without an underflow term (a product of nonzero values is nonzero), also for the decision product `K m~ >= 2^-1022`
(E). The overflow part does not need `GateHigh` at all: if an `m~` overflows, every `m~` it feeds is infinite or NaN
(O), and such a factor never decides (O); a finite `m~` has only finite `m~` (and so finite `e~`, by M) below it.
`GateHigh` (the value rule R0 with every bound below `2^1000`) only keeps the filter useful. The decision product
`fl(K m~)` cannot overflow for a finite `m~` (every `K` is below 1, about `1e-15`), and if it did, `|e~| > inf` would
be false and the factor would not decide.

`composition_check` re-derives, from the generated code alone and with Python fractions (a third implementation
next to `Compiler.cs` and `GeneratedPredicatesIndirectTests`): the `m~` expression of every value line (the rule
shape of R1-R4), its `c` against the `// c = ... u` comment, every `K` as the smallest double at or above
`c / (1 - u)` and at least `2^-62`, homogeneity and the degree. Every line of a filter body must be one of the
expected forms, in order: the value lines (operators `+`, `-`, `*` only), `int __r = 1;`, per sign factor the
comment, `if (m~ == 0) return 0;`, the `K` test and `if (e~ < 0) __r = -__r;`, then `return __r;` (an inserted
`return` fails). The gate exponents come from the rules (`960 / k - 52`, and the value rule for the upper end), and
are checked against the literals that run: the `__gate(x, lo, hi)` conjunction of the entry on doubles (over every
point, right before the call of the filter), the `__s.Gate` of `NamePrepare` (over the prepared points, followed by
`if (!__s.Gate) return;`) and the gate of the prepared call (over the other points, with `__s.Gate`), plus the
generated `__in` / `__gate` helpers verbatim; the doc comment must agree as well. The result:
`[2^-108, 2^164]` for LPI, degree 6, `K` = 18 u and 9 u; `[2^-54, 2^109]` for TPI, degree 9, `K` = 28 u and 17 u.
The values of the unsplit `NameFilter` methods only are re-derived; that `NameFilterPrepared` equals them bit for bit
is tested in .NET (`Prepared_EqualsUnsplit`).

The drift check compares `filter_bounds`, `emit_filter_value` and `emit_filter_decision` verbatim and, line by line,
the rule code around them: `Rational.U`, `Node.Leaf`, `Rational.Max`, the degree and homogeneity lines, the `__in`
and `__gate` helpers and the three gate emitters with their `lo` / `hi` literals. Not compared: `CeilDouble`,
`lit` and `InUnitsOfU` (formatting and rounding up of `K`); `composition_check` re-derives the leaf decision, every
`K` and the homogeneity from the generated code instead, and the .NET tests (`GeneratedPredicatesIndirectTests`)
fail when the checked-in generated code differs from what the generator emits.

### Model choices, and what Z3 did not prove

- **The rounding model itself.** R2-R5 use the standard model of Higham (2002, section 2.2, (2.4) and (2.5)):
  for a binary64 result whose exact value is 0 or of magnitude at least `2^-1022` and that does not overflow,
  `fl(s) = s (1 + d) = s / (1 + d')` with `|d|, |d'| <= u`. A bit-precise binary64 proof of this was tried and is
  not counted: with `fp.to_real` Z3 returns UNKNOWN (incomplete arithmetic) within a second, and a bracket of the
  exact result in a wider format (binary128, directed roundings) did not finish in 15 minutes (95 s already for
  binary16 with a binary64 bracket). The model's tightness is shown instead: the binary64 case `1 + 2^-53`
  rounds to 1 with an error of exactly `u |e~|`, so the leaf constant cannot be smaller.
- **Monotonicity of rounding.** M models round to nearest as a monotone odd function of the exact result: that
  is the definition of an IEEE 754 operation (the exact result rounded by one rounding function). Oddness is
  also checked bit-precisely for binary64 sums and products. A bit-precise binary64 query of M itself (four
  operands) returned UNKNOWN after 300 s and is not counted (the sum query for binary16 is UNSAT in 13 s, an
  experiment, not part of the gate).
- **The grid frame.** G is stated for integers in a format with binary64's precision and a 15-bit exponent, so
  that scaling a multiple of `2^-s` to an integer is exact for every exponent the gate admits (a binary64 value
  of degree 9 times `2^960` would overflow binary64). The bridge: an operation whose exact result is 0 or of
  magnitude at least `2^-1022` and below the overflow threshold rounds the same in binary64 and in that format
  (both round the exact value to 53 significant bits). The bridge needs the range of the *exact* result: Z3
  finds a binary64 product that rounds up to `2^-1022` (normal) from an exact value below it, where the two
  formats differ (an experiment, SAT: `0x1.179a11596a923p+0 * 0x0.ea63f4f9b1461p-1022`, exact value about
  `0.9999999999999999 * 2^-1022`, binary64 result `2^-1022`). The derivation only uses exact results of at least `2^-960`.
  A bit-precise proof of the bridge was too slow (over 7 minutes per query) and is not counted.
- **The composition.** The structural induction above is stated and reviewed, not proved by Z3; its application to
  the generated code is re-derived by `composition_check`. Also by review only: the product of the decided sign
  factors, and that the runtime evaluates every operation in strict binary64 (no FMA contraction, no x87 extended
  precision; RyuJIT on SSE2, the same assumption as `AdaptivePredicates`).
- **Not in scope.** The length rule of `FixedArith` (scratch limbs) and the expansion lengths: they bound memory,
  not signs, and are tested in .NET (`GeneratedPredicatesTests`, `GeneratedPredicatesIndirectTests`).

### Pilot 2 result

All 20 obligations UNSAT with SAT assumptions, all 17 mutants SAT with replayed witnesses, source contracts and
composition check pass; the captured report is `results/filter_rules_2026-11-08.json` (Z3 4.15.4, one local run,
about 84 s of solver time in all). The times are local and vary between machines and runs (the grid product lemma
took about 13 s here and 31.6 s on a reviewer's machine): the slow queries are the sum rule (about 14 s each for + and -),
the product rule (about 16 s), the grid lemmas in the (15, 53) format (about 9-32 s) and `rn_odd_sum` (about 4 s);
every other obligation and every mutant took below 3 s here. In CI
both pilots run in one job (`.github/workflows/z3-pilot.yml`, read-only token), which now also triggers on
`Compiler.cs` and `IndirectPredicates.cs`.

Recommendation: the rule-per-lemma method works for the generator: small NRA lemmas with the standard rounding
model are fast and their mutants are found at once; bit-precise binary64 works for symmetry, integrality and
special values, but not for statements that mix the exact real result with binary64 (rounding model,
monotonicity), which stay textbook assumptions. A new rule of the generator (a division, a square root, an
expansion stage) gets its lemma and mutant here, and the transcription in `filter_rules.py` changes with it.

Sources: N. J. Higham, *Accuracy and Stability of Numerical Algorithms*, 2nd ed., SIAM 2002, section 2.2 (the
standard model and its variant (2.5)); J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast
Robust Geometric Predicates*, DCG 1997 (filters with error bounds of the form `c m~`); M. Attene, *Indirect
Predicates for Geometric Constructions*, CAD 2020 (the filtered indirect predicates being checked). Only results
are used, no code.

## Conservative Marching Cubes: source-coupled adapter

```sh
/tmp/g3-z3/bin/python tools/Z3Pilot/conservative_mc.py --out /tmp/z3-conservative-mc.json
/tmp/g3-z3/bin/python -m unittest discover -s tools/Z3Pilot -p test_conservative_mc.py -v
# Optional unreduced nonlinear zero-point experiment, UNKNOWN is a failure:
/tmp/g3-z3/bin/python tools/Z3Pilot/conservative_mc.py --direct --timeout-ms 1000 --out /tmp/z3-mc-direct.json
```

The per-query timeout is `--timeout-ms`, default `Z3_PILOT_TIMEOUT_MS` or 20 s
(CI sets 120 s, as for the other pilots); UNKNOWN fails.

Source: only Jacobson, *A Sharp Conservative Offset for Marching Cubes*,
[arXiv:2609.13430v1](https://arxiv.org/html/2609.13430v1), sections 1, 3, 3.1.
[Prerequisite audit and recipe](../../docs/design/CONSERVATIVE_MC_AUDIT.md).
The default gate is a **modular adapter certificate**, not the initially proposed
monolithic `x`, eight samples and nonlinear inside query. It reuses the paper's
continuous-function/convex-lift theorem. Every table row is checked, without a
15-case symmetry assumption. The optional `--direct` mode does formulate that
zero-point query, over-approximating the high region by its boundary convex hull.
Do not count UNKNOWN as evidence. Infeasible zero-point assumptions are listed
as exclusions, not as SAT-guarded proofs: the all-high case above the threshold
has no feasible zero point, so requiring SAT assumptions in all 256 such queries
would itself be incorrect. The default adapter guards are SAT for every row.

### Composition and what Z3 checks

1. Parse the actual active edge/triangle tables of both implementations; verify
   crossing masks, edge numbering and table identity (semantic checks of the parsed
   tables). In addition, a SHA-256 hash of each whole source file and of the
   whitespace-normalized `find_iso` body is compared with a reviewed value before
   any solver query. This hash is a **review tripwire, not a semantic check**: any
   edit of either file fails the pilot until someone re-reviews it and updates the
   hashes, but nothing checks that `find_iso` still computes what the FP model of
   the next section assumes. A mutation that restores the endpoint nudge, with the
   hashes updated, keeps this pilot green; only the native tests
   (`MarchingCubesConservativeTests`) and the review catch it. The link between
   the code and the FP model is therefore a review obligation. The semantic
   substance of this pilot is the combinatorial table certificate of step 2.
2. For each row construct the **high-side boundary chain**: reverse the MC
   triangles and close them with outward-oriented high-side cube-face polygons.
   Verify the cap arcs form disjoint oriented cycles and every edge cancels.
   Verify face choices depend only on their four signs and match neighboring
   cells. All cap vertices are high corners or emitted crossings. A cap polygon
   can be triangulated as an oriented fan using its boundary vertices; this is a
   signed chain, so convexity of the polygon is unnecessary.
3. Z3 checks two small lemmas: the linear edge lift identity (the interpolated
   crossing has lift exactly sigma) and the nonnegative-product lemma. The 256
   per-case `case_NNN_lift` queries then all check **the same trivial linear
   lemma**, only with a different number of nodes: convex weights `w_i >= 0` with
   sum one and contributions `c_i = w_i` (crossing) or `c_i >= w_i` (high corner)
   sum to at least one. This is elementary algebra, not an independent check of
   each case; the per-case structure only ties the node list (high corners plus
   crossings of the row) to the parsed table. The substance per case is the table
   certificate of step 2, checked without Z3. Each query also checks that its
   assumptions are SAT, which here only rules out contradictory guards; the
   non-vacuity of the method is shown by the two SAT controls of step 5.
4. Mathematical bridge (not proved again by SMT): a closed finite oriented
   boundary chain has winding zero outside its vertices' convex hull. By (3)
   its hull lies in the paper's projected convex lift `P`. The paper excludes
   true zero points from that hull above half the cell diagonal. Adding the
   high chains over the grid cancels internal caps. Require every sample on
   the actual extraction boundary to be high (`f >= sigma`) and the complete
   target zero set to lie inside that boundary. The remaining chain is then
   the full extraction-box boundary minus the MC surface. Thus a true zero point inside the box has MC
   winding number one. This establishes **winding enclosure**, not embeddedness
   or a guarantee for every ray-parity implementation on self intersections.
   Runtime success must retain every required triangle; `LinearEdges` throws on
   a crossing on a corner or a collapsed triangle (coincident crossings), and
   keeps a throw on a rejected triangle as a guard. Within the grid limits below,
   every grid edge visited has its own vertex (MarchingCubes: distinct positions
   on an exact grid; Pro: distinct 16-bit hashes), and by the case table no edge
   between two crossings is used by more than two triangles, so the guard is not
   expected to fire; beyond the limits Pro's hash aliased edges silently. Bounds must fully contain the
   offset surface AND enclose the bounded low solid, with all extraction-boundary
   samples high; containing the offset surface alone is insufficient. The field
   must be deterministic/thread-safe for parallel use,
   and canceled or failed runs are never certificates. Two further assumptions are
   checked by review and by the native tests only, not by this pilot: a crossing
   shared by neighbouring cells is computed bit-identically in each (MarchingCubes
   identifies vertices by their exact position in `VertexHash`, so it relies on
   the same corner positions and samples and on `find_iso` being symmetric in its
   endpoints; MarchingCubesPro computes each crossing once per edge, keyed by its
   edge hash), and the JIT does not contract the interpolation into fused
   multiply-adds (RyuJIT emits FMA only for explicit `Math.FusedMultiplyAdd`;
   other runtimes, eg Mono or IL2CPP, are not checked; the FP envelope of the next
   section assumes separately rounded operations). Pro continuation can miss
   components and is excluded. Pro's 16-bit index hashing allows corner indices
   up to 65535: at most 65535 cells per axis for `Generate()`, 65534 for
   `GenerateContinuation()` (it also visits the cell of index n, as
   `AxisAlignedBox3i.Contains` is inclusive), whose seeds must lie in cells 0 to
   n (seed cells are not clamped). `LinearEdges` refuses a larger grid, a seed
   outside it and a cube size that is not finite and positive with
   `ArgumentException` before any work, so no silent aliasing remains in this
   mode (the legacy modes alias silently). Use full `Generate()`.
5. Required SAT controls: the positive-radius cell-center sphere below the
   threshold, and a deliberately incorrect crossing placement with a lift below
   sigma. These check satisfiability, not merely absence of counterexamples.

### FP bridge and normalized recipe

The two FP obligations use **relative rounding errors for sample arithmetic**
and the existing absolute coordinate envelope. This removes the old
`|sample| <= 1` and `sample gap >= 2^-20` conditions. There is no replacement
sample-magnitude ceiling or positive gap floor. Samples and level must be finite;
a sorted crossing still has a strictly positive exact gap, by definition.
The [relative-error design](../../docs/design/CONSERVATIVE_MC_RELATIVE_ERROR.md)
gives the complete algebraic bridge and the scope of the independent bounds review.

Let `u = 2^-53`, exact `n = sigma-fa`, `d = fb-fa > 0`, and `T = n/d` in [0,1].
For finite rounded differences, `RN(n) = n(1+dn)` and `RN(d) = d(1+dd)`, with
`|dn|, |dd| <= u`. This includes subnormal differences: binary64 operands are
integer multiples of `2^-1074`, so an exact difference below the normal range is
itself exactly representable. Cancellation therefore needs no minimum gap.

Division needs a small absolute underflow allowance as well:
`RN(z) = z(1+dq) + eta`, with `|dq| <= u`, `|eta| <= u^2`. For a normal exact
result choose eta=0; for a subnormal exact result choose dq=0 and use the absolute
rounding bound `2^-1075 < u^2`. The loose `u^2 = 2^-106` bound is on the
dimensionless parameter; it is not a sample-gap condition. A purely relative
model is false even for a nonzero quotient: `RN(2^-1074 / 0.75) = 2^-1074`
has relative error 1/4. An actual binary64 replay pins this case.

Canceling positive d yields the normalized equation checked by Z3:

```
(t_raw - eta)(1 + dd) = T(1 + dn)(1 + dq)
```

For all T in [0,1] and allowed errors, `|t_raw - T| <= 4u`. The query has no
sample-magnitude or gap parameters to restrict. This normalization is an explicit
algebraic bridge to the source, not a proof that an arbitrary floating expression
matches it. Clamping the parameter to [0,1] cannot increase its error.

The coordinate obligation keeps the previous loose operation allowances for
endpoints in [-1,1]: subtraction error <= 2u and product/addition error each
<= 4u, including underflow. With the improved `4u` parameter bound it proves the
same `E = 2^-28` position envelope. Projection to the original edge interval
cannot increase the error; only one coordinate varies on an axis-aligned grid,
so E is a Euclidean bound. All grid nodes, including the extra boundary cell,
must still lie in [-1,1] and be exactly representable; for example, an origin
whose coordinates are integer multiples of a power-of-two spacing, with all
node integer coordinates small enough to be represented exactly.

Standard binary64 RNE with gradual underflow (no FTZ/DAZ), no fused contraction
and no extended intermediates are assumptions, not re-proved IEEE lemmas.
Nonfinite samples are excluded. Existing `LinearEdges` guards reject overflowing
differences/nonfinite quotients and collapsed crossings; failed runs never
certify a mesh. Thus successful extraction does not need a magnitude ceiling to
make the subtraction model valid.

For exact 1-Lipschitz samples the linear edge slope has magnitude <= 1, so the
lift deficit is <= E. High corners and crossings then have lift >= sigma-E.
The paper's argument applies at that lowered lift. One deliberately loose,
concrete recipe for an exact dyadic grid with `0 < h <= 1` is:

```cs
mc.RootMode = MarchingCubes.RootfindingModes.LinearEdges;
mc.IsoValue = Math.Sqrt(3.0) * mc.CubeSize / 2 + Math.Pow(2, -26);
mc.Generate(); // Do not use a partial, canceled or failed result.
```

For Pro use its corresponding enum. `LinearEdges` bypasses Pro's float sample
cache and evaluates doubles, but double field evaluation is still not exact
mathematics. The `2^-26` pad dominates `E` and rounding in the displayed level
formula under these coordinate conditions. There is no sample-magnitude bound
or minimum sample gap. Require finite samples/level, no corner exactly at the
level, and strictly interior representable crossings. Require all samples on the actual extraction boundary (including the extra
cell) to be high and the entire true zero set to be inside. Check the final mesh
is closed and valid; a finite mesh is not by itself an enclosure certificate.

For approximate samples with a **proved** absolute evaluation error `e` and a
bound `S` on the sampled linear edge slopes, use a level strictly above
`half_diagonal + e + S*E`, also allowing for level computation. One may bound
`S <= 1 + 2*e/min_edge_length` when a true 1-Lipschitz field underlies the samples.
An unquantified mesh-SDF approximation or arbitrary additional epsilon is not
certified by this recipe. Far coordinates, nonexact grids and other root modes
remain outside these FP lemmas. Overflow remains a rejected extraction, not a
certified output.

### Recorded outcome

`results/conservative-mc.json` records the original #423 absolute-envelope gate: the table certificate
(256 rows, closed high chains, matching neighbour faces), 260 UNSAT queries with
SAT assumptions (the edge-identity and product lemmas, the 256 instances of the
same trivial lift lemma described in step 3, and the two FP envelopes) and both
SAT controls with exact-rational replay.
`results/conservative-mc-relative.json` records the relative-model follow-up:
the same 260 SAT-guarded UNSAT obligations, a required SAT `2u`-bound mutant
replayed with exact fractions (a counterexample to the independent-error
envelope, not necessarily an attainable C# input), and actual binary64 replays for large samples,
tiny/subnormal differences, subnormal division, subtraction ties, and field
scalings from `2^-1070` through `2^1000`. Removing the division underflow
allowance can leave the abstract real lemmas true; the actual subnormal replay
then fails and the gate rejects the model. A too-small parameter bound and an
UNKNOWN control are likewise rejected. The level pad and coordinate profile
are unchanged. `results/conservative-mc-relative-validation.json` records the
commands and verification results, including the initial unrelated timeout and
successful repeat without changing the older rule or its timeout.
`results/conservative-mc-direct-summary.json` records the separate short-budget
experiment (`--direct --timeout-ms 100`): one direct UNSAT with SAT assumptions,
255 direct UNKNOWN results. This is explicitly **not** a completed direct NRA
certificate. The modular certificate above is the verification used by the
conditional recipe; its winding-chain and rounding bridges remain reviewable
mathematical steps. Independent review found and fixed incomplete source
coupling and the missing outer-boundary-high precondition.

`results/conservative-mc-validation.json` records commands (relative paths,
`<dir>` for scratch directories), exit codes and test counts of the native tests,
the controlled mutations, all Python pilot tests, both library targets, net48
compilation and API package validation; no logs. New pilot solver
tests use child processes to keep their expression context out of existing
pilots: an initial shared-context run timed out in Pilot 2's product lemma;
isolated execution passes without changing that lemma or accepting UNKNOWN.

## Pilot 2, step 1: the D1 drift lemma of SafeRounding (apex rule)

Step (1) of pilot 2: the lemma of `docs/design/ROUNDING_DRIFT_APEX.md` (branch `claude/d1-drift-lemma`; checked at
880f2a9a, its section 3 corrected to the (2.5) rounding model in fd931c69), checked before the rule is built
(`drift_apex.py`). The rule: an edit that removes the triangles `R`, adds `A` and moves the surface by at most `δ` is
accepted if `M + δ <= maxLength` (`M` the largest drift over the vertices of `R` and `A`, unchanged); with an **apex**
`c` (a vertex in every triangle of `A`) only `c` gets `d'(c) = M + δ`, every other drift is kept; without one every
vertex of the edit gets `M + δ` (today's rule). The invariant, with `D(T)` the largest drift of the corners of `T`:
(I1) every point `x` of a triangle `T` of `S` has `dist(x, S0) <= D(T)`; (I2) every point `y` of `S0` has a `T` in
`S` with `dist(y, T) <= D(T)`. Claim of one edit, given (E1) and (E2): `I(S', d')`, `d' >= d`,
`max d' <= maxLength`. (No new intersection and no degeneracy are checked exactly in `improves()`; they are not part
of this lemma.)

Which edits have an apex follows from the triangles the code adds (`apexes()`), not from the edit's name: a move,
slide, contraction, vertex removal and flip have one; a zip (`try_zip`) adds four triangles
`(c, d, x), (d, b, x), (d, c, y), (c, a, y)` without a common vertex and keeps today's rule; a strip zip
(`try_strip`) removes the outer triangle at every loop edge and re-adds it split at the chain vertices, so for a
loop of three vertices `e0, m, e1` (a sliver, also with inner vertices) every added triangle contains the middle
chain vertex `m`, the apex, and longer loops have none (a common tip of all outer triangles is rejected by the
code: both chains would create the same directed edge); an ear contraction (`try_collapse` at a vertex with a
single triangle) adds none, because `collapse_keeps_boundary` never moves the kept boundary vertex (`A` empty).

```sh
/tmp/g3-z3/bin/python tools/Z3Pilot/drift_apex.py --out /tmp/z3-drift-apex.json
```

### Model

- **The specification drives the obligations.** `drift_apex.CHARGING` (`with_apex`, `without_apex`, `M_over`,
  `acceptance`) is read by `edit_formula`, `generic_formula` and `obligations`; a value the lemma does not cover
  (for instance `with_apex = 'first vertex of A[0]'`, `without_apex = 'apex'`) raises, and `M_over = 'A'` or `'R'`
  makes obligations SAT (tested). The source contract that compared the Boolean 2.0 code with this specification is
  retired with the apex rule (Boolean 2.1, the reference rule of step 1b); the lemma stays as the comparison of the
  D1 note's section 3.3.
- The distances are real variables and the geometry enters as hypotheses, per point the note's proof uses:
  (I1) for a point `x` of an added triangle `T'`: the (E1) witness `z` in some removed triangle with
  `|x - z| <= δ` (which removed triangle is a disjunction), (I1) before at `z`, and the triangle inequality
  `dist(x, S0) <= |x - z| + dist(z, S0)`; claim `dist(x, S0) <= D'(T')`. One query per added triangle (the claim
  is a conjunction over them; the larger contractions take 0.2 s instead of 50 s); a row counts as UNSAT only if all
  its queries are, and the number of queries must equal the number of added triangles. (I2) for `y` of `S0` near a
  removed triangle: (I2) before gives `z` in a removed triangle with `|y - z| <= D(T)`, (E2) gives an added triangle
  (for `A` empty: an untouched triangle that holds the counterpart simplex) within `δ` of `z`, the triangle
  inequality per target; claim: some target `T'` has `dist(y, T') <= D'(T')`. An untouched triangle keeps its bound
  by `d' >= d` (a claim of its own).
- `M` is the maximum over the vertices of `R` and `A` (`M >= d(v)` for each, equal to one), `d(v) <= maxLength`
  before the edit, `M + δ <= maxLength`. The edit obligations are linear real arithmetic (the maxima as `If` terms);
  the chain step below is nonlinear.
- **The shapes are illustrative.** The proof uses only that the apex is in every triangle of `A` and that `M` is over
  `R` and `A`; the shape-free rows `generic_*` state exactly that (`D(T) <= M` for a removed `T`, `D'(T') >= d'(c)`
  for an added `T'`, `d(c) <= M`), so boundaries, open fans, ring vertices shared between sides and the like are
  covered without a shape of their own. The shapes: a move or slide of valence 3 to 8 (closed fan); a contraction of
  `a-b` into `b` with 2 to 6 triangles of `a` and 2 to 6 of `b` besides the two at the edge (25 shapes); a removal of
  valence 3; a flip; a strip zip with a loop of three vertices (a sliver, and with an inner vertex; apex `m`); and
  without an apex the zip (4 + 4 triangles), a strip zip with a loop of four vertices (2 + 4 removed, 6 added) and an
  ear contraction (`A` empty; its points go to the edge across, which lies in an untouched triangle with a
  third vertex outside the edit). Every apex of a shape is checked (the code takes the first in `A[0]`).
- Non-vacuity: the assumptions of every query are SAT with `δ > 0` (the model replayed with Python fractions), and for
  every apex of every shape a SAT check that the rule really leaves another vertex below `M + δ`.
- The rounding of the stored drift, one step `k -> k + 1` of the chain bound `d <= d̂ (1 + u)^k + k ε`: true
  `M <= M̂ g + k ε` with `g = (1 + u)^k >= 1`, true `δ <= δ̂ + ε`, stored `fl(M̂ + δ̂)` (`M̂` is an exact maximum in the
  code); claim `M + δ <= fl(M̂ + δ̂) g (1 + u) + (k + 1) ε`. The model is Higham (2.5), `fl(s) = s / (1 + η)`,
  `|η| <= u`, valid for round to nearest. The note's first version used (2.4), `fl(s) = s (1 + η)`; with that the
  step is **SAT**, kept as a witness mutant. The stored witness is `M̂ = 1`, `δ = δ̂ = ε = 0`, `k = 0`, `g = 2`,
  `η = -18014398509481983 / 2^107` (about `-u`) and a true `M = (2^108 - 1) / 2^107`, just below `M̂ g = 2`, which
  is above the stored bound `M̂ (1 + η) g (1 + u)`: `(1 - u)`-rounding needs the
  factor `(1 - u)^-k`; round to nearest rounds down by at most `1 / (1 + u)`, so `(1 + u)^k` holds (corrected in the
  note, fd931c69).

### Result

All 168 obligations UNSAT (386 queries), their assumptions SAT with `δ > 0` and replayed; 38 non-vacuity checks of
"only the apex" SAT and replayed; 13 mutants SAT with witnesses replayed with fractions:

- charging a vertex that is not an apex: the vanishing `a` of a contraction; the flip's `a`, in one added triangle
  only (I1 and I2); a ring vertex of a move; the zip's `d` (in three of its four added triangles);
- `M` without `δ` (I1 of a move, I2 of a flip); the apex charged `δ` only (`d' >= d` fails); no acceptance test
  (`max d' <= maxLength` fails);
- `M` over `A` only (a contraction, a removal; for moves and flips `R` and `A` have the same vertices and the mutant
  is equivalent there);
- an ear in place charging only its vanishing vertex (why edits without an apex keep today's rule);
- the chain step in the (2.4) model.

About 3 s of solver time locally (7 s for the run, report `results/drift_apex_2026-11-08.json`).

### Review only (not checked by Z3)

- (E1) and (E2) per edit type: that every point of an added triangle is within `δ` of a removed one and back, with
  the `δ` each edit computes (SAFE_ROUNDING.md section 1 and the class comment of `MeshRoundingRepair`); for every
  edit where the code finds an apex (also a strip zip, whose apex is the middle chain vertex exactly for a loop of
  three vertices, and boundary fans) that (E2) points into `A`; that an ear contraction never moves the kept vertex
  (`collapse_keeps_boundary`), so it has `A` empty; for edits without an apex, that the counterpart is a simplex of the
  edit's vertices in an untouched triangle.
- Untouched triangles keep their geometry: every triangle of a moved vertex is in `R` (and back in `A`); no edit adds
  vertices or reuses vertex ids.
- The point/triangle distance model: that a point of a triangle, `dist` to a closed set and the triangle inequality
  capture the geometry (the shapes fix the combinatorics only; positions are not modelled).
- I1 and I2 together give `H(S, S0) <= MaxDrift` (the definition of the Hausdorff distance), and the induction over
  the edits from `I(S0, 0)`.
- The chain: one counter `k` for every vertex, taken as the largest over the vertices (the step lemma holds for
  that `k`). The note's "k becomes the length of a chain through one vertex" is better read as the depth of the
  charging DAG (an apex's drift depends on the drifts of every vertex of its edit, which in turn were charged by
  earlier edits).
- That the code charged as `CHARGING` says was held by a source contract while the apex rule was the code (Boolean
  2.0); retired with it.
- `ε` itself (a few ulps of the largest coordinate per distance computation): stated, not derived.

## Pilot 2, step 1b: the reference rule for SafeRounding's drift (D1, implemented in Boolean 2.1)

`drift_reference.py`, `test_drift_reference.py`; the note is `docs/design/SAFE_ROUNDING_D1_DRIFT.md` (the rule in
section 3, this check in section 4). The pilot checked the lemma before the rule was built, and searches tiny
configurations for counterexamples; `MeshRoundingRepair.cs` implements it since Boolean 2.1.

```sh
python tools/Z3Pilot/drift_reference.py --out /tmp/z3-drift-reference.json      # about 2.5 minutes
python -m unittest discover -s tools/Z3Pilot -p 'test_drift_reference.py' -v
```

- **A, convexity** (nonlinear real arithmetic): a point of a triangle moves by at most the largest move of a corner,
  in R^3, as two queries (the dot bound in coordinates, the claim over the Gram entries). The claim on the
  coordinates directly returned unknown and is not counted.
- **B, the one-edit lemma**, shape-free, distances as real variables, as `drift_apex.generic_formula`. `lemma_pair_corners`: the joint move
  (`try_move_pair`) is an instance of the displacement edit: its bound is the largest distance of any corner from its anchor, so the joint check is the
  conjunction of the checks at the two moved vertices (the check that drops the second vertex's corner is the SAT mutant).
- **C, the bounded search** (linear real arithmetic, complete for its configurations): chains of 3 to 5 vertices on
  the real line, every script of up to three edits (two for five vertices), 7038 scripts, each with one query for
  "a segment has a point beyond its bound, or the start has a point beyond the largest bound, or a bound exceeds
  the apex rule's drift, or a bound at the end exceeds the reported drift". `Chain` is written once over a backend, so the same code replays a witness with fractions.
  **The joint move** (`Chain.move_pair`, edit `move_pair`): two distinct vertices, each at its own position (`q{k}`, `r{k}`), as one displacement
  edit; judged by the largest bound of the segments at either vertex and of the ghosts at either, all at the new positions. The scripts with at
  least one joint move (`PAIR_SEARCH`: 3 vertices up to 3 edits, 4 and 5 vertices up to 2; 2150 scripts) are asked the same four claims plus a
  fifth, the reduction: the script with every joint move replaced by the two single moves in turn ends with the same positions and the same bound
  at every segment and ghost (a displacement edit changes no anchor), so the lemma of the single move covers each bound the joint move
  accepts. Mutants: the second vertex's segments and ghosts not evaluated, its ghosts not evaluated, its segments evaluated with it still in place.
- **D, the rounding**: `Vector3d.Distance` in three steps (Higham (2.4) per operation), and the induction step of
  the chain bound (Higham (2.5) for the stored sums; the (2.4) variant is the SAT mutant, as in step 1).
- **Mutants**: 20, each SAT with a replayed witness; the ten of the rule (seven of the single edits, three of the joint move) are pinned in the test (`PINNED`) as
  regression cases for the implementation, among them the spike that shows why collapsed triangles must be kept as
  ghosts and why one anchor per vertex is not enough. As meshes they are the scripted cases of
  `MeshRoundingRepairTestsDriftReference` (each fails with its mutant in the C# code).
- **Source contract**: the facts about `MeshRoundingRepair.cs` and `Vector3d.Distance` that the lemma assumes (one
  writer of positions; only `try_move`, `try_collapse` and the joint move `try_move_pair` propose a moved vertex and list its triangles corner for
  corner), the joint move itself (`try_move_pair`: the triangles of both stars, a triangle at both once, both positions proposed together and judged by
  `improves`, both written afterwards; `pos`; `search_pairs`: the sizes of each vertex from its own `room_at`, every candidate through `try_move_pair`),
  and the rule as implemented: the methods of the bounds, the proposal, the ghosts and the commit compared as
  text (`RULE_METHODS`), the drift checked first in `improves`, no move proposed past the evaluation limit (and no second copy of the rule),
  the position search (`search_corners`) taking only its candidate sizes from `room_at` and passing every candidate to
  `try_move`, every generic edit naming its added triangles, the records starting at `Apply`. A test applies the seven
  mutants, eleven changes to the joint move and seven other changes to the code and expects the contract to fail on each.

Review only: the two-sided bounds (E1), (E2) per edit type; ghosts on edges (a chain has ghosts at vertices only);
which simplices an edit removes; the induction over the edits; the rounding model. Result of 2026-11-09:
`results/drift_reference_2026-11-09.json` (19 obligation rows UNSAT with satisfiable assumptions, 8 of them the
7038 scripts of the search; 16 mutants SAT; 137 s of solver time).

## Pilot 4: the Devillers-Guigue prefilter of the arrangement

`tritri_prefilter.py` checks the lemma of docs/design/TRITRI_PREFILTER.md over the reals (the code's predicates,
`ExactPredicates.Orient3d` on doubles, are exact, so no rounding is modelled). `U` is normalized to
`(0,0,0), (1,0,0), (0,1,0)` by an affine map with positive determinant (the bridge in the note), `T`'s nine
coordinates are free. The segments `T n plane(U)` and `U n plane(T)` are built from their definitions, not from the
predicates. UNSAT: the order of each segment's ends along `d = nT x nU`, each predicate's equivalence with one
comparison of ends, and the lemma both ways. SAT: the assumptions, both decisions, touching (no vacuous proof), and
four mutants. `test_tritri_prefilter.py` runs it and checks a source contract on `apart_on_the_line` (the lines
that the model describes). All 14 queries take well under a second.

## Pilot 7: the separating-axis prefilter (a candidate rule, not library code)

`separating_axis.py` checks the lemma of docs/design/SNAP_ROUNDING_3D.md section 5.3: two triangles whose projections
onto any vector, computed in doubles with the margin `8u` times the largest absolute projection sum, are strictly
apart have no common point. R1 (the rounding error of a projection, each operation within `1 + d`, `|d| <= u`), R2 (the
margin computed in doubles covers it), R3 (the decision from the computed bounds) and G1 (the geometry, free real
coordinates) are UNSAT with satisfiable assumptions; six mutants are SAT (a bound of `2u`, a margin of `2u`, the margin
of one corner, non-strict comparisons, a corner left out). Bridges by review: monotonic rounding, `fl(|a||x|) =
|fl(a x)|`. Assumed: no product of an axis component and a coordinate underflows or overflows (not implied by the
coordinate range of `MeshRoundingRepair`; a build scales the axes by a power of two). The rule was measured
and not recommended for building (the note, 5.3); `test_separating_axis.py` holds a source contract on the prototype
`benchmarks/SnapRoundingExperiment/CandidateCulling.cs`. 14 queries, about 3.5 s; result of 2026-11-12:
`results/separating_axis_2026-11-12.json`.

## Pilot 9: the segment test and the wide bound of the Antipodal sum (#1587, library code)

`antipodal_segment.py` checks the two changes of #1587 in `ExactMeshBoolean.antipodal_sum` (an exact-code file). The
segment test "the point on or next to the segment" is now `|a + b| <= tol`, linear in the angle between `-a` and `b`; the
old `1 + a.b <= tol` was quadratic in it against a linear `tol`, so it fired up to about `sqrt(2 tol)` away and the piece
was undecided, read as outside. N1 (the rounded norm of the rounded `a + b`, within `4u`; as N1a-c, one rounding step
each, and their linear composition), N2 (a normalized vector has `|a|^2` within `10u` of 1; N2a per coordinate, then the
composition), D1 (the rounded dot product), S1 (a point on the segment gives status 1), S2 (status 1 gives
`|A + B| <= 1.2 tol` for the exact unit vectors) and S3 (the new status 1 implies the old one, so every value that was
decided keeps its bits) are UNSAT with satisfiable assumptions. T1 bounds a term of the wide evaluation of an undecided
value (`maxBound` 0.25): with `tol <= 0.4 hypot` per term the sum's `4 tol / hypot` still bounds twice the angle of the
term's error: T1 proves `cos(phi) >= 1 - 2 t^2 + (2/3) t^4`, which is at least `cos(2 t)`, so `phi <= 2 t` (review of
#1624: the earlier `1 - 2 t^2`, the lower bound of `cos(2 t)`, gave only `phi <= 2 asin t`). Bridges by review (the docstring, B1-B4): the error of the unit vectors from the rounding of the point and
the ends (the sum's own model, with its 16 times margin), the triangle inequality and Cauchy-Schwarz, `cos x <= 1 - x^2/2 + x^4/24`,
and the invariance of T1 under scale and rotation. Eight mutants are SAT: the old quadratic test (S2), `tol` without its
factor 16 (S1), without its `1e-15` (S3), the condition `0.8 hypot` and the angle bound `tol / hypot` (T1), a dot bound of
`2u`, a norm bound of `3u` (N1), a normalization bound of `6u` (N2). `test_antipodal_segment.py` holds the source contract on `mesh_ops/ExactMeshBoolean.cs` and `math/Vector3d.cs`.
30 queries in about 0.6 s in all, the slowest (D1) about 0.5 s (0.45 to 0.53 s measured; one query of the whole norm took up to the 120 s timeout,
review of #1624, hence the split); the report records each query's time; result of 2026-11-25: `results/antipodal_segment_2026-11-25.json`.

## Pilot 8: the plateau variant of the position search (a design, not library code)

`plateau_search.py` checks the argument of docs/design/SNAP_ROUNDING_3D.md section 10 for a position search that also
takes steps with equal defect counts (not built; the acceptance rule and the rollback are prototyped in
`benchmarks/SnapRoundingExperiment/LatticeProbe.cs`). A chain of such steps is committed only when a step removes a
defect, and undone after `B` draws or `K` sideways steps.
- T, termination: the measure (defects, "the round committed a chain", chains left, draws left) falls
  lexicographically in each of the five transitions and stays within fixed bounds (5 UNSAT with satisfiable
  assumptions; the well-foundedness of the order is the bridge). `K` is carried in the state and is not needed for
  termination (a sideways step uses a draw); it bounds how far a chain wanders.
- A, the counts: a sideways step keeps the number of degenerate triangles and of intersecting pairs, an improving
  step lowers the sum and raises neither (bridge: the defects away from the moved vertex are unchanged).
- R, the rollback: a journal of up to three steps undone the last first restores every position, also when a vertex
  moved twice (longer journals by induction, a bridge).
- D, the bound: a step is applied only if checked, so the largest bound stays within `L`.
- P2 (exact integers, not Z3): the longest run from `N0` defects with `S` chains per defect and `B` draws per chain has
  `B S N0 (N0 + 1) / 2` draws, found by a search over every draw at which a chain can commit, for `N0 <= 6`.

Nine mutants are SAT: a sideways step that counts as a move of the round, a rejected draw that is not counted, a
sideways step that refunds the draws and is not capped, a new round after a round without a commit, acceptance by the sum of the counts
(twice), the journal undone the first entry first, a journal of the new positions, a sideways step without the bound
check. Not modelled: the candidates and their seeded order (any sequence terminates), the invariance under scaling
(the candidates are those of the built search), and the state besides the positions that a build must restore (the
note, 10.5). 28 queries and P2, under a second; result of 2026-11-15: `results/plateau_search_2026-11-15.json`.
`test_plateau_search.py` also checks that no state but the end is stuck, and holds a source contract on the probe.

## Mesher checks (pilot 3, `mesher_checks.py`, 2026-11-08)

Z3 checks for the 2D mesher `QualityMesh2` (owner 2026-11-08): the error bound of the exact-corner filter `ShortestEdgeCorner` (#437) and
inequalities behind the termination arguments of the small-angle rules (#394, rule C in
`docs/design/MESHING2D_SMALL_ANGLES_CONFORMING.md`). Nothing here is part of the library; the module reads the C# sources and fails when the
code it models has changed (`source_contracts`, with a test that drift is detected).

```sh
/tmp/g3-z3/bin/python tools/Z3Pilot/mesher_checks.py --out /tmp/mesher-checks.json
/tmp/g3-z3/bin/python -m unittest discover -s tools/Z3Pilot -p 'test_mesher_checks.py' -v
```

Every claim is a counterexample query that must return UNSAT; the assumptions alone must be SAT (no vacuous proof); a mutant must
return SAT. UNKNOWN is a failure. Results of the run: `results/mesher_checks_2026-11-08.json`.

### What Z3 proves (real arithmetic, under the stated model)

Model: the standard model of binary64 round to nearest, `fl(x) = x (1 + d)`, `|d| <= 2^-53`, except that a product that underflows has an
absolute error of at most `2^-1075`. Additions and subtractions of doubles are exact when their result is subnormal (reviewed, below).

1. **S1** `fl(fl(x1 - x2)^2)`: relative error `(1 + u)^3 - 1`.
2. **S2** `l = fl(fl(dx dx) + fl(dy dy))`: `|l - L| <= ((1 + u)^4 - 1) L + 2 (1 + u) 2^-1075`, with `L` the exact squared length.
3. **S3** with the guard of the code (`l > 1e-280`): `|l - L| <= 2^-50 L`. The mutant without the lower guard is SAT.
   - Reported, not required: **2^-51 is not proved.** The derivation gives `(1 + u)^4 - 1`, which is above `2^-51`; the sharp bound of S2 with `2^-51`
     is SAT (Z3 finds the parameters), but that only says the derivation does not prove it, not that the real error reaches it.
4. **Order (a2):** if the computed values `la <= lb <= lc` of the three squared lengths each are within `2^-50` (relative) of their exact values and
   `lb - la > lb 2^-40`, then `La < Lb` and `La < Lc`: the minimum chosen in doubles is the exact minimum. The margin `2^-52` is SAT (mutant).
   - Reported: the margin `2^-49` is also UNSAT (safe) with `2^-50` per length.
   - When the test in doubles fails (gap at most the margin) the code takes the exact BigInteger path, so no claim is made there.
5. **Margin constant (bit-precise, FP only):** for `rival` in `(1e-280, 1e140)`, `rival * 2^-40` is exact in binary64.
6. **(b1) the lens predicate:** for `a = (-1, 0)`, `c = (1, 0)` and `p` not an end of `ac`: the predicate of `InLens`
   (`dot < 0` and `4 dot^2 >= |pa|^2 |pc|^2`) holds iff the angle `apc` is at least 120 degrees (`dot <= -|pa||pc|/2`) iff `p` is in both disks of radius
   `|ac| / sqrt 3` through `a` and `c`. **This proves that the code's predicate is the 120 degree lens, not
   Shewchuk's termination theorem (2002, section 6.4) and not the link between that lens and the bound `sin^2 theta <= 3/16` (25.65 degrees),
   which is Shewchuk's analysis and is not encoded.**
7. **(b2) the O2 step of rule C:** `u = (-1, 0)`, `w = (1, 0)`, apexes `p` above and `q` below the line: if neither apex is strictly inside the
   diametral circle (angles at most 90 degrees), `q` is not strictly inside the circumcircle of `u w p` (locally Delaunay).
8. **(b3) the arithmetic of the factor 1.5 in #394:** ends at radii `r1 <= r2 <= 2 r1` and a vertex at radius `rho` between them: `rho` is within
   the factor `sqrt 2` of one end (`rho^2 <= 2 r1^2` or `r2^2 <= 2 rho^2`), hence within 1.5.

### What is reviewed, not proved

- The standard model of IEEE round to nearest (textbook, Higham 2002, section 2.2), and that sums and differences of doubles are exact when their
  result is subnormal (Hauser 1996). Z3 returns UNKNOWN on binary64 values mixed with reals across `2^+-1000`, so the monolithic
  bit-precise form of S2 and of the margin test was not obtained.
- The bridge from the test in doubles to the real inequality: `fl(rival - lj) > m` with `m = rival 2^-40` a double implies `rival - lj > m`
  (round to nearest is monotone).
- Similarity (WLOG `a = (-1, 0)`, `c = (1, 0)`) in (b1) and (b2): both sides of the equivalences are invariant under similarities.
- (b3): the geometric step that an encroaching vertex projects into the piece.
- Not encoded at all: Shewchuk's termination theorems, the 25.65 degree value as a number (`arcsin(sqrt 3 / 4) = 25.659 degrees` is a numeric
  fact, not checked by Z3), and every termination argument of #394 and rule C beyond the inequalities above (they are measured, not proved).

## Split termination (pilot 4, `split_termination.py`, QA 2.0 T14, 2026-11-12)

The termination of the midpoint split of the `DMeshAABBTree3` top-down build (`split_tri_set_midpoint`), the rule that #451 broke: with
centers that are adjacent doubles the midpoint `(b + a) * 0.5` rounds onto the upper end, the right side is empty and the same set was
split again without end. Checked in binary64 (Z3 floating point, round to nearest even, subnormals included) for finite centers of any
magnitude (the overflow fallback of the build gives finite centers up to `MaxValue`): where the sum does not overflow the midpoint lies in
[a, b] (O1: three sign cases, two claims each); it can round onto b and onto a, and the sum can overflow to +infinity and to -infinity
(SAT witnesses, replayed in plain floats; O1 does not hold there and is not needed); for any size n0 of the left side, after the guard
`n0 < 1 || n1 < 1` with the half and half fallback both sides are non-empty and smaller than the set (O3, integers); a split without the
guard is SAT. `source_contracts` pins the text of the leaf test, the leaf limit passed as `minTriCount`, the two scans, the guard, the
fallback and the recursion on the two sides, each with a drift test. Not modelled as loops: the swap loop and the scans (their bounds are
source contracts); the expansion sums of `AdaptivePredicates` (the owner decides whether they are wanted). About 30 s; part of the `Z3 pilot`
workflow (its report is uploaded; the paths filter includes `DMeshAABBTree.cs` and `Interval1d.cs`).

```sh
python3 tools/Z3Pilot/split_termination.py --out /tmp/split-termination.json
python3 -m unittest discover -s tools/Z3Pilot -p 'test_split_termination.py' -v

## Step loops (pilot 5, `step_loops.py`, QA 2.0 T14, 2026-11-12)

The step loops of `MeshRoundingRepair` (`for (double s = s0; s <= bound; s *= 4)` in `fix_degenerate` twice and in `try_separate`, `s *= 16`
in `fix_intersection` twice), the class of #452 (a start that underflowed to 0 never advanced). Checked in binary64 (Z3 floating point,
round to nearest even, subnormals included): for every finite s > 0 the product with 4 and with 16 is at least s + s or infinite, so
the loop advances (P1 for a bound that is finite or NaN, P1b: at least twice or infinite); with 2^2100 * 2^-1074 above `double.MaxValue` (an exact integer check, not a Z3 result) it ends within 2100 iterations for a finite bound; the bit
manipulation of `ulp` is positive and finite for every finite x >= 0 (so `ulp(bound)` and `4 * ulp(largest)` are never the stuck start 0).
Mutants: the P1 query without the start guard (the code before #452) and without the bound guard (`inf <= inf`) is SAT, with witnesses
replayed in plain floats. That the bound is finite or NaN is the guard `if (!(ml < double.PositiveInfinity)) ml = double.NaN;` of
`maxLengthC`, pinned by `source_contracts` together with the loop headers, the start guards and the `ulp` function; a test shows each kind
of drift is detected. Runs in under a second; part of the `Z3 pilot` workflow (its report is uploaded).

```sh
python3 tools/Z3Pilot/step_loops.py --out /tmp/step-loops.json
python3 -m unittest discover -s tools/Z3Pilot -p 'test_step_loops.py' -v

## Clipper2 frame (pilot 6, `clipper_frame.py`, QA 2.0 T10, 2026-11-12)

The frame of the Clipper2 adapter (`ClipperFrame.ForBounds`): `CeilExponent` on the 64 bits of a normal number is the smallest power of two above it
(Z3 on the bits), the subnormal loop ends within 52 steps with the same property (Z3, unrolled bit vectors), `PowerOfTwo` is exactly 2^n for
all 2046 exponents and every accepted frame (86 817 combinations of `precisionBits` 8..50 and exponent) maps the extent to at most 2^bits <= 2^50,
below `MaxIntegerCoordinate` = 2^52, with a finite `1 / scale` (exhaustive), and the extent plus margin is never below the larger distance (binary64).
Mutants: a floor instead of the ceiling (SAT, witness replayed) and the enumeration with `precisionBits` up to 53 (finds a frame with integers above 2^52, replayed). Not proved by Z3 (it does not finish on the bit-blasted
monotonicity of a rounded subtraction or product): that a coordinate within the bounds scales to an integer of magnitude at most 2^bits
(monotone rounding of `p - Origin` and `d * Scale`; reviewed). `source_contracts` holds the model to the source. `source_contracts` also pins the `precisionBits` range guard and the extent with its margin (drift tests). About 5 s; part of the `Z3 pilot` workflow (paths filter, uploaded report).

```sh
python3 tools/Z3Pilot/clipper_frame.py --out /tmp/clipper-frame.json
python3 -m unittest discover -s tools/Z3Pilot -p 'test_clipper_frame.py' -v
```

## Exact-bound inventory lemmas (2026-11-25, `docs/qa/exact-bound-inventory.md`)

Three lemmas for the top gaps of the inventory. Each obligation is an UNSAT counterexample query with its assumptions SAT;
each has a controlled mutant that must be SAT and is replayed outside Z3; each reads the constants it uses from the source
and fails on a change of the code around them. Part of the `Z3 pilot` workflow (paths filter, uploaded reports), about 1 to 2 min
together.

**`adaptive_range.py` (G2, the hand mutant H6 of #1572).** The range of `Expansion.InRange` against what `AdaptivePredicates`
needs. Proved (binary64 / wide frame, bit-precise): a double >= 2^-m is a multiple of 2^-(m + 52), and RNE sums, differences and
products of integers in the wide frame (precision 53, 15-bit exponent) are integers (TwoSum, the split and TwoProduct are
chains of these). With m and M read from `math/ExactPredicates.cs` (powers of two, else the contract fails): for each predicate
of degree d (orient2d 2, orient3d 3, incircle 4, reviewed) d (m + 52) <= 1022 (no underflow) and d (M + 1) + 33 <= 1023 (no
overflow; 33 counts the terms, the sums and the splitter, as AP-1 of `tools/ErrorBoundAudit` does). Stated, not queried: the
bridge, that a binary64 operation whose exact result is 0 or normal is the wide frame's (the IEEE definition; Z3 does not decide
the frame equality for + and * in 300 s, and the variant with the condition on the rounded result is false: an exact product
just below 2^-1022 rounds up to a normal number). Mutants: H6 (2^-250: a degree-4 product of the grid unit 2^-302 is 0 in
binary64; the .NET replay is `ExactPredicatesRangeEdgeTests`, InCircle +1 for exactly cocircular points at 2^-220), the
maximum 2^250, the grid one bit too fine, a quotient of integers. Margins in the report: the lemma admits MinMagnitude down to
2^-203 and MaxMagnitude up to 2^246 for the expansions' intermediates (the stage B/C filter products, eg IccErrBoundC * permanent,
are not in the lemma and underflow earlier). The degrees and the growth count are reviewed against `AdaptivePredicates.cs`, whose
normalized text is pinned by its SHA-256: any change fails the contract until they are reviewed again. The contract also pins the `InRange` overloads (each checks all its arguments) and the public predicates of
`AdaptivePredicates`, and scans the library for calls of `AdaptivePredicates.Orient2d / Orient3d / InCircle` without
`if (Expansion.InRange(<all the call's arguments>) == false)` in the three lines before (`tools/ErrorBoundAudit/range_callers.py`;
its own test runs in `ci.yml` on every library change, since this workflow runs only on its paths): the two calls in
`mesh_generators/DualContouring.cs` (place_face_vertex) are the known exception; a new one fails.

**`may_meet_box.py` (G3).** `ContouringTopologyCertificate.may_meet_box` drops a triangle if fl(bmax + err) < fl(tmin - err) (or
the mirror), err = fl(fl(12 u M) + 4 eta). Linear real arithmetic over the rounding model |P - p| <= u |P| + eta (products),
|S - s| <= u |S| (sums): the computed separation implies the exact one, for one box corner and one triangle point (the corner of
the largest projection by monotone rounding, tmin below every point's projection). Mutants: the factor 1 instead of 3, no eta
term (SAT, models replayed in exact rationals). The smallest integer factor the lemma admits is 3 (16 u M is not enough in the
model): the code's 24 u M has no spare factor, as the comment's 22 u M derivation says.

**`slope_approx.py` (G1, the corner error of the terrain prefilter; issue #1586, fixed by #1619).** `TerrainSlopeMap.prefilter_skips`
assumes the corners from `ExactSlopeKernel.Approx` are within e = 4e-12 (|x| + |y|) + 2^-1074. Approx used to accept if
err <= 1e-12 (|x| + |y|); at the subnormal edge that failed (the finding, kept as the IEEE replay `issue_1586`: X = [2^-1074,
2^-1074], Y = [0, 0], W = [-8, 4] gave x = y = -0 accepted, while the exact vertex for W* = 2^-1000 is x* = 2^-74). The fix
(`approx_position`) also needs 2^-1022 <= |x| + |y| <= double.MaxValue and a narrow W ((W.Hi - W.Lo) 5e11 <= |w|); each of the
two halves alone fails (replays `width_only`, `guard_only`), and so does an overflowed |x| + |y| (`overflow`). Proved: four
lemmas, one per step (width of W, width of X and Y, one coordinate, the claim), linear except the coordinate step (nlsat),
under two seconds; the midpoint in its interval by monotone rounding, or with Z3's binary64 theory (`--fp`, 89 s here, 253 s on another machine). Mutants
(SAT): the claim without the guard, with twice the width, with a consumer of 3e-12 (the last two show the 2x slack of the proof, which uses |W* - w| <= dW where the midpoint gives dW / 2, not wrong code; a width condition of 1e11 is wrong, killed by a .NET test). Run it as a plain script or from the module directory, not with `python3 -I` (it imports `pilot` from its own directory). Kept from the first version (its scope:
no underflow): acceptance gives W* the sign of w (both signs); mutant: err without its W term.

```sh
python3 tools/Z3Pilot/adaptive_range.py --out /tmp/adaptive-range.json
python3 tools/Z3Pilot/may_meet_box.py
python3 tools/Z3Pilot/slope_approx.py [--fp]
python3 -m unittest discover -s tools/Z3Pilot -p 'test_adaptive_range.py' -v
```

## The rounding oracle (`rounding_oracle.py`, #1465, 2026-11-25)

A research spike, not a gate: can the degenerate triangles that `MeshRoundingRepair` leaves be removed by moving up to k
vertices within their drift bounds, or is that provably impossible? Note: `docs/research/ROUNDING_Z3_ORACLE_2026-11.md`.

```sh
dotnet run --project benchmarks/BooleanInputOrder -c Release -- oracle dump --case <spec> --out state.json [--nojoint]
python tools/Z3Pilot/rounding_oracle.py state.json [...] --timeout 120 --out report.json
dotnet run --project benchmarks/BooleanInputOrder -c Release -- oracle verify --case <spec> --witness w.json
python -m unittest discover -s tools/Z3Pilot -p 'test_rounding_oracle.py'
```

The model is a relaxation of the repair's own acceptance (drift balls of every triangle and ghost at a moved vertex, the
area test of `is_bad`, `IntersectsBeyondShared` as weak separation along the nonzero separating axes), so UNSAT is a proof
and SAT a candidate that the library checks. Z3's nonlinear arithmetic did not decide the state's polynomials with their long
dyadic coefficients (even the drift and one area in 30 s), so the decision procedure is a branch and bound over boxes of the
moves whose per-box linear relaxations (rigorously padded floats, exact Dyadic on small boxes) z3 decides as linear real
arithmetic with booleans. The test checks the encoding on a sliver in a square tube: one move repairs it (SAT, clean exactly
and after the rounding to doubles), a narrow tube makes it impossible (UNSAT by the wall constraints, SAT without them), a
small bound makes it impossible (UNSAT by the area); and the pair encoding on crossing, apart and touching triangles.

## Follow-up on PRs #1656, #1657 and #1658

`pilot_followups.py` checks additional obligations, with the original
pilot assumptions kept explicit. Run the follow-up with the pinned solver.

```sh
python3 -m unittest discover -s tools/Z3Pilot -p test_pilot_followups.py -v
```

| Pilot | Follow-up result and remaining obligation |
| --- | --- |
| Continuous collision | The axis-only root shortcut visits the vertex-triangle simplex or edge-edge square corners at both times. Both strict discard implications carry through convex combinations using the original corner bound. Normal-direction culling needs its own interval proof. |
| Ray/AABB | The entry lower bound follows from the two normal-rounded slab operations and the normal subtraction model. The final absolute subtraction and zero clamp preserve it. Entry bounds through subnormal intermediates remain open in the current code. The proposed conservative alternative uses zero below 2^-969 for the rounded entry, which ensures that the correction product is normal on the other path. |
| Terrain box | A straight-toe build reaches the offset case from #1657. Outward rounding encloses the reference reach in the model. The straight-toe build succeeded, and no missing face was demonstrated. A full implication from the domain edge to output coverage remains open. |
| Saturating conversions | The 32/64-bit floor and grid neighbor obligations in #1657 close the stated arithmetic checks. Runtime casts remain tied to the documented .NET semantics. |
| Intrinsic flip | Conservation of crossing arcs on the shared edge makes the doubled flip count even. This supplies the parity step for realizable inputs satisfying that conservation law. The roundabout update and common subdivision extraction still need separate arguments. |
| Limb negation | The carry invariant now composes for an arbitrary number of limbs, starting with carry one and the empty prefix. The arena sizing proof still uses the reviewed width bounds for each arithmetic operation. |
| Tetrahedron stitching | With the two sheet fields summing to a strictly negative value at both ends, both outsides cannot overlap on their affine segment. This supplies the order condition for the domain clipping case. The rounding and coincident-zero cases still need separate coverage. |
| Coplanar propagation | The fold replay below disproves triangle-plane label completeness for the retained segment labels. Since `Partners` retains the noncoplanar incident triangle, a traversal can force a new winding seed where that partner contains the edge. The `Partners` guard and coplanar label preservation through splitting need verification in the propagation implementation. |
| SecondPoint | The subnormal rounding case becomes a missed hit in `IntersectionUtil.Intersects`. A finite second point cannot represent a forward ray from `double.MaxValue` along +x. The candidate direction-based predicate below needs a separate exact-code review. |
| Volume filters | The normal-rounding product induction for a general summation length is machine-checked. The normal-case final inflation also covers the evaluation once the accumulated component bounds satisfy the stated gamma premise. The per-determinant enclosure and the additive underflow budget still need to be linked end to end. |
| Polyhedral envelope | The local delta and search box checks remain valid. The independent-rounding model is inconclusive for the current residual factor 5 and proves the enclosure at factor 6. This establishes a candidate margin and no production counterexample. Actual facet displacement and direction-error constants remain open. |
| Geodesic window | The midpoint disk proof now covers the differently rounded endpoint radii used by `TransformDisk`. The original subnormal length witness is rejected by the full guard. The final radius inflation covers its own normal rounding when the constituent bounds are valid. Certification of the rotation and center terms, and the global length-lower-bound, remain open. |
| Double-word division | The divisor and error-inflation checks in #1658 use a certified residual as a premise. The operand-error propagation inequality is checked. The remaining bridge is the source-level error of the residual arithmetic across its supported ranges. |
| Contouring bins | A checked bounded-search alternative halves the remaining integer interval and matches an inclusive scan on 315 plateau and boundary cases. It refuses an unbracketed flat grid and checks coarse-to-fine index products before querying the grid. The solver proves that every interior pair of fine indices fits Int32 once the two bracket endpoints fit. The existing adjustment loops still require an estimate and a target in range, and a bracketed monotone grid. There is no end-to-end proof of those premises through the contour builder. |

### Production replays

* Let `e = double.Epsilon`. `IntersectionUtil.Intersects` returns false for
  origin `(e,0,0)`, direction `(e,4e,0)`, and triangle vertices
  `(2e,8e,-2e)`, `(5e,8e,-2e)`, `(2e,8e,2e)`. The supplied ray crosses at
  `(3e,8e,0)`, with barycentric coordinates `(1/6,1/3,1/2)`. The rounded
  second-point ray has x coordinate `e`. The same replay with a normally
  scaled unit direction returned true through both ray APIs. An exact hit
  predicate can use three determinants with the supplied direction and the
  triangle vertices relative to the origin. The solver checks that their
  sum equals the signed direction dot normal. A closed-hit implementation
  still needs exact dyadic fallback, compatible treatment of boundary hits,
  and review of the separate symbolically perturbed winding path.
* `ExactMeshArrangement` was run with a source triangle
  `(0,0,0),(8,0,0),(0,8,0)` and a fin with vertices
  `(3,1,0),(1,1,0),(1,1,2)` in another mesh. The source retained line
  label 4 and partner 1. Adding coplanar triangle
  `(1,1,0),(3,1,0),(1,3,0)` to the second mesh makes a fold. The retained
  source line labels were 7, 5 and 6, all coplanar. Partner 2 was preserved.
  Sorting coincident segment labels and keeping the first explains the loss.
* `TransformDisk` returns false for point boxes at `(0,0)` and
  `(2e,2e)`. The adjusted length is three subnormal steps, above the exact
  length, and the endpoint error padding makes this particular guard fail.

### Exact-direction hit predicate under review

Keep the public ray signatures and the existing closed-hit semantics.
For a finite origin and supplied nonzero direction, compute the three
edge determinants of the direction and the triangle corners translated
by the origin. Opposite nonzero signs reject the line. The determinant
sum is the signed direction dot normal; a zero sum rejects the parallel
or degenerate case. With an accepted line crossing, the signed
origin-to-plane relation distinguishes a forward crossing from one
behind the origin, with a separate exact zero check for an origin hit.

Reuse the existing dyadic representation for the exact fallback. A fast
path that continues using a constructed second point must certify that
its displacement is a positive multiple of the supplied direction. Two exact cross-component equalities against a nonzero pivot and a positive pivot scale suffice, as the follow-up check establishes.
If a hit parameter is needed on the exact route, round the dyadic
plane-offset quotient once using the existing exact quotient conversion.
The symbolic perturbation used by winding queries needs independent
review before sharing the direction path.

Acceptance should cover the strict-interior replay above, normalized and
unnormalized directions, shared-edge hits, parallel and origin hits,
power-of-two scale invariance, and agreement between the AABB-tree and
linear query routes. Compare ordinary-case ray batches against the
existing path before choosing a fast-path certification strategy. The exact determinant prototype agrees with an independent rational plane-hit and Gram-coordinate oracle on 4096 seeded cases and hits the subnormal replay triangle. Explicit edge, vertex, origin, coplanar and behind-origin cases also agree at three exact dyadic scales.
