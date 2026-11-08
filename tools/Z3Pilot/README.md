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
   crossing masks, edge numbering, table identity and full reviewed source hashes.
   Source drift in classification, corner sampling, grid formation, interpolation
   or emitted winding must fail before any solver query.
2. For each row construct the **high-side boundary chain**: reverse the MC
   triangles and close them with outward-oriented high-side cube-face polygons.
   Verify the cap arcs form disjoint oriented cycles and every edge cancels.
   Verify face choices depend only on their four signs and match neighboring
   cells. All cap vertices are high corners or emitted crossings. A cap polygon
   can be triangulated as an oriented fan using its boundary vertices; this is a
   signed chain, so convexity of the polygon is unnecessary.
3. Z3 checks the linear edge lift identity and nonnegative-product lemma. For
   each actual table row, abstract each convex weight times its normalized lift
   as a contribution. A crossing contributes its weight; a high corner
   contributes at least its weight. Contributions therefore sum to at least one.
   Each case is a SAT-guarded UNSAT query. This abstraction uses the independently
   proved product and edge lemmas, not an assumed conclusion about enclosure.
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
   collapsed crossings or rejected triangles. Bounds must fully contain the
   offset surface AND enclose the bounded low solid, with all extraction-boundary
   samples high; containing the offset surface alone is insufficient. The field
   must be deterministic/thread-safe for parallel use,
   and canceled or failed runs are never certificates. Pro continuation can miss
   components and is excluded; its 16-bit index hashing requires dimensions
   below 65536. Use full `Generate()`.
5. Required SAT controls: the positive-radius cell-center sphere below the
   threshold, and a deliberately incorrect crossing placement with a lift below
   sigma. These check satisfiability, not merely absence of counterexamples.

### FP bridge and normalized recipe

The two FP obligations use **real rounding-error envelopes**, as a modular
Pilot-2-style proof. Standard correctly-rounded IEEE binary64 RNE operations
without fused contraction are assumptions, not re-proved IEEE lemmas. For
`|fa|, |fb|, |sigma| <= 1`, exact positive sample gap `d >= 2^-20`, and
`n = sigma-fa` with `0 <= n <= d`, both differences have absolute rounding error
at most `u = 2^-52` (including subnormals). The rounded quotient's magnitude is
below two, giving the same absolute error envelope. Z3 proves the quotient's
error from the exact interpolation parameter is at most `2^-30`.

Clamping the parameter to `[0,1]` cannot increase this error. With endpoint
coordinates in `[-1,1]`, subtraction error is at most `u`; the multiply/add
errors are each at most `2u`. The second obligation proves position error at
most `E = 2^-28`. Coordinate clamping to the original edge interval cannot
increase it. On an axis-aligned grid only one coordinate varies, so `E` is also
a Euclidean error bound. This does not certify construction of the grid itself:
the recipe requires exactly representable grid nodes, e.g. dyadic origin and
power-of-two spacing with small integer indices. All generated nodes, including
the extra boundary cell, must satisfy the coordinate bounds.

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
formula under these normalized conditions. Require all sample magnitudes and
crossing gaps to meet the bounds above, no corner exactly at the level, and
strictly interior representable crossings. Require all samples on the actual extraction boundary (including the extra
cell) to be high and the entire true zero set to be inside. Check the final mesh
is closed and valid; a finite mesh is not by itself an enclosure certificate.

For approximate samples with a **proved** absolute evaluation error `e` and a
bound `S` on the sampled linear edge slopes, use a level strictly above
`half_diagonal + e + S*E`, also allowing for level computation. One may bound
`S <= 1 + 2*e/min_edge_length` when a true 1-Lipschitz field underlies the samples.
An unquantified mesh-SDF approximation or arbitrary additional epsilon is not
certified by this recipe. Far coordinates, smaller gaps, nonexact grids,
overflow and other root modes remain outside these FP lemmas.

### Recorded outcome

`results/conservative-mc.json` records the default source-coupled gate: 260
SAT-guarded UNSAT obligations and both SAT controls with exact-rational replay.
`results/conservative-mc-direct-summary.json` records the separate short-budget
experiment (`--direct --timeout-ms 100`): one direct UNSAT with SAT assumptions,
255 direct UNKNOWN results. This is explicitly **not** a completed direct NRA
certificate. The modular certificate above is the verification used by the
conditional recipe; its winding-chain and rounding bridges remain reviewable
mathematical steps. Independent review found and fixed incomplete source
coupling and the missing outer-boundary-high precondition.

`results/conservative-mc-validation.json` records native tests, the controlled
legacy-nudge mutation (two expected failures), all Python pilot tests, both
library targets, net48 compilation and API package validation. New pilot solver
tests use child processes to keep their expression context out of existing
pilots: an initial shared-context run timed out in Pilot 2's product lemma;
isolated execution passes without changing that lemma or accepting UNKNOWN.
