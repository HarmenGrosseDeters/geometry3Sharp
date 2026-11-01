# Indirect predicates from the predicate compiler (design, for review)

**Status:** design reviewed by the owner (decisions in section 5). Step (a) is built (section 7). Stacked on #244 (the
predicate compiler as a bounds tool).

## 1. Goal

Extend `tools/PredicateCompiler` so that it generates **indirect predicates**: sign predicates whose inputs include
implicit points, i.e. points given by a construction of the arrangement's inputs, evaluated **without constructing
the point** (Attene 2020; Cherchi, Livesu, Scateni, Attene 2020). For each predicate the generator derives, from
its formula alone:

1. a floating-point **filter** with a proven error bound;
2. an **expansion stage** on exact double inputs (Shewchuk's expansions);
3. an exact **fallback** on fixed-width integers (`FixedArith`, with the bounds of #244), or `BigInteger` beyond them.

The output is checked-in code in `math/Generated/` with tests against an oracle at many scales, a bounds review and
mutation runs. **It is not wired into `ExactMeshBoolean`** in this work: whether that pays off is decided on the time
share of the exact constructions per class, which another session measures first.

The same generator should serve the polyhedral envelope (ROADMAP stage 7, Wang, Schneider, Hu, Attene, Panozzo
2020), whose predicates are orient3d on the same kinds of implicit points.

## 2. Scope and non-goals

- In scope: the generator, the generated kernels in `math/Generated/`, their tests, this document, `docs/SOURCES.md`.
- Not in scope: changes to `ExactMeshArrangement` / `ExactMeshBoolean` beyond what tests need (read-only use of
  their constructions as the oracle); no public API change (compatible with upstream 1.0.324); no Roslyn source
  generator (an offline tool, so the code is in the diff for the review).
- One expansion runtime: the expansion arithmetic of `AdaptivePredicates` (own implementation from Shewchuk's paper),
  made reusable, not #221's `PredicateRuntime` beside it. One integer runtime: `FixedArith`.

## 3. Current state

The arrangement has three kinds of implicit points (`PointKind`), each with a hand-derived approximation and error
bound (`segment_point`, `plane_value`, `planes_approx`, `orient2d_approx`):

| kind | construction | Attene's name |
|---|---|---|
| `EdgePlane` | segment p-q crossing the plane of a triangle (or one of its edge planes) | LPI (line-plane) |
| `Segments` | two coplanar segments crossing, in a projection | SSI (segment-segment, 2D) |
| `Planes` | three planes of different triangles | TPI (three planes) |

When a filter on the approximations fails, the point is **constructed** exactly (`compute_exact`: `BigInteger`,
gcd reduction, `ExactPoint3`), stored in fixed width when it fits (#223), and the predicate runs on it
(`FixedArith`). The inputs are integers on one grid (`ScaleExp`, centered); the input points are exact doubles in
almost all cases (`IsDouble`). Planes are integer vectors computed from the corners (normal ~2b + 1 bits, `D` ~3b + 2
bits for b-bit coordinates), stored as `BigInteger` with a double approximation (`PlaneApprox`, relative error
2^-52).

An indirect predicate replaces "construct, reduce, then evaluate" by "evaluate a polynomial in the inputs":
- **LPI** of p, q and the plane (n, D): `x = (p (n·q − D) − q (n·p − D)) / (n·q − n·p)` in homogeneous form, numerator
  of degree 4 and denominator of degree 3 in the coordinates (with n of degree 2, D of degree 3);
- **TPI** of three planes: Cramer's rule, numerators of degree 7 and denominator of degree 6 in the coordinates
  (degree 3 in the plane coefficients); orient3d with a TPI point then has degree 9;
- **SSI** in 2D: the analogue with orient2d values.

## 4. Design

### 4.1 Spec

Constructions become named point definitions with a homogeneous numerator and denominator; a predicate over points
substitutes them. Sketch:

```
construct LPI(p, q, r, s, t)            # segment p-q, plane through r, s, t (all input points)
    n = cross(s - r, t - r)
    lq = dot(n, q - r)
    lp = dot(n, p - r)
    num = p * lq - q * lp                # vector
    den = lq - lp

predicate Orient3dLPI(LPI a, point b, point c, point d)
    sign orient3d_h(a, b, c, d)          # orient3d with a homogeneous point: det of (a.num - a.den b, ...)
```

- Inputs: double coordinates of input points (exact doubles, the common case), and their integer view for the exact
  fallback (`FixedPoint3`, W = 1).
- The generator expands to a polynomial graph (the `Node` graph of #244), with vectors as triples of scalars.
- The sign of a homogeneous point's denominator is a separate predicate (or known: LPI's `den ≠ 0` by
  construction); the predicate's sign is `sign(poly) · sign(den)^k`, k the degree in that point. The generator
  emits that correction.

### 4.2 Stages

1. **Filter (stage A).** The error analysis of #221 (round to nearest, `u = 2^-53`, products that underflow with an
   absolute error term), applied to the expanded graph on the double inputs. Attene's semi-static form: a bound
   `C · m^d` with `m` the largest input magnitude after the predicate's leaf differences, `C` derived by machine.
   Range gate as in #221 (inputs 0 or in [2^-100, 2^100]; else straight to the exact stage).
2. **Expansion stage.** The graph evaluated exactly in expansions from the exact double inputs, with lengths known
   statically (#221 step 1). Its cost grows fast with the degree (TPI predicates reach degree 9+): the generator
   reports the static expansion lengths, and a predicate whose worst case is too long skips this stage.
3. **Exact fallback.** The same graph on `FixedArith` with bounds derived as in #244, from the integer grid bound of
   the inputs (`|x| ≤ 2^b − 1`, `b` per predicate); inputs beyond it go to `BigInteger` (one generic evaluator of
   the graph, generated too).

Each stage's bound is printed in the generated code and in a table in this document (as section 5 of
`FIXED_WIDTH_EXACT_POINTS.md`), checked by a test that the table is the derivation.

### 4.3 First predicates

The ones the arrangement and the envelope share, smallest first:
1. `Orient3d(LPI, b, c, d)` (which side of a plane an edge-plane point lies; envelope: segment against prism faces);
2. `Orient3d(TPI, b, c, d)` (envelope: critical points of three faces);
3. the arrangement's projected `Orient` with one LPI point, for comparison with today's construct-then-evaluate path.

### 4.4 Oracle and tests

- **Oracle:** construct the point with `BigInteger` (`ExactPoint3`, as `compute_exact` does), then evaluate the
  predicate exactly; no code shared with the generated stages.
- **Scales:** inputs at 2^k for k from −100 to 100 (`Similarity.All()` for the invariance test, 2^k scales bit for
  bit); nearly degenerate inputs (the filter must fail and the later stages decide); exactly degenerate inputs
  (sign 0), at all stages (stage entry forced in the tests).
- **Bounds review:** independent, of the generator's rules (once) and of every generated bound (the tables).
- **Mutation:** a shard for `math/Generated/*` with a measured threshold; hand mutants of the generator's rules.
- **Analyzers:** `math/Generated/*` in `.editorconfig` (G3E0001-3, RS0030) and in `ExactCodeAnalyzerTests`.
- **Speed:** a micro benchmark against construct-then-evaluate on recorded arrangement operands (as in section 13 of
  `FIXED_WIDTH_EXACT_POINTS.md`); no claim on the booleans until they are wired in.

## 5. Decisions (owner, on the questions of the draft)

1. **Expansion stage for high degrees:** only where its static worst case is below 1024 components; above it, the
   predicate goes from the filter straight to `FixedArith`.
2. **Input bound of the integer fallback:** generated for 64-bit grid coordinates (`|v| ≤ 2^64 − 1`, one limb), with
   `BigInteger` beyond; the measured distribution of real inputs decides the details.
3. **Runtime:** reuse the expansion primitives of `AdaptivePredicates` (one expansion runtime); `FixedArith` is the
   one integer runtime.
4. **Order of work:** (a) the constructions in the spec, the exact `FixedArith` stage and the oracle tests; (b) the
   filter; (c) the expansion stage. One PR each.

## 6. Sources

- M. Attene, *Indirect Predicates for Geometric Constructions*, Computer-Aided Design 2020 (the method only; its
  code is not read until its license is checked).
- G. Cherchi, M. Livesu, R. Scateni, M. Attene, *Fast and Robust Mesh Arrangements using Floating-point Arithmetic*,
  ACM TOG 2020 (the predicates on LPI / TPI points).
- J. R. Shewchuk, *Adaptive Precision Floating-Point Arithmetic and Fast Robust Geometric Predicates*, 1997 (the
  expansion stage).
- A. Meyer, S. Pion, *FPG*, RNC 2008 (deriving filters by machine; idea only).
- B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment Check*,
  ACM TOG 2020 (the envelope's predicates; `fast-envelope` is MIT and may be read).

## 7. Step (a): constructions and the exact stage (built)

**Spec** (`tools/PredicateCompiler/indirect.spec`). No new construct was needed for the constructions: a
construction is a set of named definitions over the inputs (the homogeneous numerator `N` and denominator `d`),
and the predicate is written on them. Two additions to the format:
- `inputs xyz`: the inputs are the coordinates of integer points (W = 1); the default stays the projected
  `x, y, w` of `predicates.spec`;
- `sign e1, e2`: the product of the signs, here `det(N − d c, a − c, b − c)` and `d`.

**Generated code** (`math/Generated/IndirectPredicates.cs`, `tools/PredicateCompiler --code`): per predicate
- `Name(ref FixedPoint3 ...)`: if every input component has at most one limb (`|v| ≤ 2^64 − 1`), the graph on
  `FixedArith` with a `FixedArena` of the derived scratch (`Take` throws beyond it, so a wrong derivation fails
  loudly); otherwise `NameBig`;
- `NameBig(ExactPoint3 ...)`: the same graph in `BigInteger`;
- both throw for points with `W ≠ 1`.

**Contract.** `Orient3dLPI(p, q, r, s, t, a, b, c)` and `Orient3dTPI(a1, ..., c3, a, b, c)` are the sign of
`det(x − c, a − c, b − c)`, which is `ExactPredicates.Orient3d(a, b, c, x)` for the implicit point `x` (tested). They
are also 0 when `x` does not exist (`d = 0`: the line parallel to the plane or in it, `p = q`, a degenerate triangle,
dependent planes); a caller that must tell this from "x lies on the plane a, b, c" checks `d` itself.

The file carries no `<auto-generated>` marker: Roslyn would skip it in the analyzers, and `math/Generated/*.cs` is
exact code (`.editorconfig`: G3E0001-3, RS0030; `ExactCodeAnalyzerTests`).

**Bounds** (the tool's output; `GeneratedPredicatesIndirectTests` fails if it differs):

<!-- generated by tools/PredicateCompiler --table; do not edit -->
| predicate | values | bound | bits | limbs | limbs taken by FixedArith |
|---|---|---|---|---|---|
| `Orient3dLPI` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `Orient3dLPI` | `ux`, `uy`, `uz`, `wx`, `wy`, `wz`, `prx`, `pry`, `prz`, `qrx`, `qry`, `qrz`, `acx`, `acy`, `acz`, `bcx`, `bcy`, `bcz` | `2 m` | 65 | 2 | 2 |
| `Orient3dLPI` | `uy * wz` and 11 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `Orient3dLPI` | `nx`, `ny`, `nz`, `m1`, `m2`, `m3` | `8 m^2` | 131 | 3 | 4 |
| `Orient3dLPI` | `nx * prx` and 5 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `Orient3dLPI` | `(nx * prx) + (ny * pry)` and 1 more of its shape | `32 m^3` | 197 | 4 | 5 |
| `Orient3dLPI` | `lp`, `lq` | `48 m^3` | 198 | 4 | 5 |
| `Orient3dLPI` | `d` (a sign factor) | `96 m^3` | 199 | 4 | 5 |
| `Orient3dLPI` | `p.x * lq` and 5 more of its shape | `48 m^4` | 262 | 5 | 5 |
| `Orient3dLPI` | `(p.x * lq) - (q.x * lp)` and 2 more of its shape | `96 m^4` | 263 | 5 | 6 |
| `Orient3dLPI` | `d * c.x` and 2 more of its shape | `96 m^4` | 263 | 5 | 5 |
| `Orient3dLPI` | `Nx`, `Ny`, `Nz` | `192 m^4` | 264 | 5 | 6 |
| `Orient3dLPI` | `t1`, `t2`, `t3` | `1536 m^6` | 395 | 7 | 8 |
| `Orient3dLPI` | `t1 - t2` | `3072 m^6` | 396 | 7 | 8 |
| `Orient3dLPI` | the determinant (sign factor 1) | `4608 m^6` | 397 | 7 | 8 |
| `Orient3dTPI` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `Orient3dTPI` | `u1_x`, `u1_y`, `u1_z`, `w1_x`, `w1_y`, `w1_z`, `u2_x`, `u2_y`, `u2_z`, `w2_x`, `w2_y`, `w2_z`, `u3_x`, `u3_y`, `u3_z`, `w3_x`, `w3_y`, `w3_z`, `acx`, `acy`, `acz`, `bcx`, `bcy`, `bcz` | `2 m` | 65 | 2 | 2 |
| `Orient3dTPI` | `u1_y * w1_z` and 23 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `Orient3dTPI` | `n1x`, `n1y`, `n1z`, `n2x`, `n2y`, `n2z`, `n3x`, `n3y`, `n3z`, `m1`, `m2`, `m3` | `8 m^2` | 131 | 3 | 4 |
| `Orient3dTPI` | `a1.x * n1x` and 8 more of its shape | `8 m^3` | 195 | 4 | 4 |
| `Orient3dTPI` | `(a1.x * n1x) + (a1.y * n1y)` and 2 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `Orient3dTPI` | `D1`, `D2`, `D3` | `24 m^3` | 197 | 4 | 5 |
| `Orient3dTPI` | `n2y * n3z` and 17 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `Orient3dTPI` | `e23x`, `e23y`, `e23z`, `e31x`, `e31y`, `e31z`, `e12x`, `e12y`, `e12z` | `128 m^4` | 263 | 5 | 6 |
| `Orient3dTPI` | `n1x * e23x` and 2 more of its shape | `1024 m^6` | 394 | 7 | 8 |
| `Orient3dTPI` | `(n1x * e23x) + (n1y * e23y)` | `2048 m^6` | 395 | 7 | 8 |
| `Orient3dTPI` | `d` (a sign factor) | `3072 m^6` | 396 | 7 | 8 |
| `Orient3dTPI` | `D1 * e23x` and 8 more of its shape | `3072 m^7` | 460 | 8 | 9 |
| `Orient3dTPI` | `(D1 * e23x) + (D2 * e31x)` and 2 more of its shape | `6144 m^7` | 461 | 8 | 9 |
| `Orient3dTPI` | `((D1 * e23x) + (D2 * e31x)) + (D3 * e12x)` and 2 more of its shape | `9216 m^7` | 462 | 8 | 9 |
| `Orient3dTPI` | `d * c.x` and 2 more of its shape | `3072 m^7` | 460 | 8 | 8 |
| `Orient3dTPI` | `Nx`, `Ny`, `Nz` | `12288 m^7` | 462 | 8 | 9 |
| `Orient3dTPI` | `t1`, `t2`, `t3` | `98304 m^9` | 593 | 10 | 11 |
| `Orient3dTPI` | `t1 - t2` | `196608 m^9` | 594 | 10 | 11 |
| `Orient3dTPI` | the determinant (sign factor 1) | `294912 m^9` | 595 | 10 | 11 |
| `Orient3dLPI` | scratch of one call (sum over the values; lengths only: 450) | | | 244 | 284 |
| `Orient3dTPI` | scratch of one call (sum over the values; lengths only: 1220) | | | 604 | 701 |
<!-- end of the generated table -->

**Tests** (`GeneratedPredicatesIndirectTests`, also on net48 except the generator checks):
- against an oracle that constructs the point as a reduced rational (LPI as `p + t (q − p)`, TPI by Cramer's rule
  with replaced columns, determinants by Sarrus' rule: no code and no expansion shared with the generator) and
  evaluates orient3d on it: 4000 inputs of 4 to 64 bits, at `±(2^64 − 1)`, beyond it (the `BigInteger` path), and
  mixed; the `FixedArith` and `BigInteger` paths agree on every input;
- exact zeros: the LPI point against its own plane and its own line, the TPI point against each of its planes, a
  line parallel to the plane and dependent planes (`d = 0`);
- the limb boundary `2^64 − 1` / `2^64`;
- every value within its derived bound at the bound;
- the path: the `FixedArith` path allocates nothing and the `BigInteger` path does (.NET), checked on every call;
  one component of one point at a time at `2^64 − 1` / `2^64`;
- `W ≠ 1` rejected at every position, on both paths of both predicates;
- the convention: equal to `ExactPredicates.Orient3d(a, b, c, x)` for integer implicit points;
- invariance over `Similarity.All()`: equal to the oracle on every transformed configuration; the `2^k` scales
  applied to the integer configuration (times `2^(k + 40)`, so that it crosses to the `BigInteger` path) give the
  untransformed signs.

## 8. Spike: an indirect `Orient` in the arrangement (measured before wiring, stopped)

The task: wire an indirect `Orient` on implicit points into `ExactMeshArrangement.Orient` and measure how much of
the upper bound of the exact work (10-13 % serial on the real classes, 41 % on n-ary) arrives.

**The question first.** An indirect `Orient` evaluates the predicate without constructing the point. It saves work
only for points that are then never constructed: the output rounds every result vertex from its exact point
(`PointToVector`), and the other predicates (`InCircle`, `ComparePoints`, ...) construct their points too. So the
upper bound of the spike is the constructions that `Orient` alone ever needs.

**Probe** (temporary, not committed; on #246's head): every implicit point (`EdgePlane`, `Segments`, `Planes`)
records whether `Orient`'s exact stage triggered its construction and whether anything else asked for its exact
value during the boolean. `BooleanComparison profile --repeat 1 <cases>`, sums over all cases and operations:

| class | implicit points constructed | first by `Orient` | needed by `Orient` only |
|---|---|---|---|
| `cgal` (150 runs) | 257 289 | 91 531 | 5 919 (2.3 %) |
| Thingi10K (96 runs) | 374 511 | 77 813 | 7 283 (1.9 %) |
| `cherchi` (5 small cases, 15 runs) | 18 114 | 18 114 | 0 |
| grid 5 / grid 10 | 12 972 / 126 446 | 0 / 0 | 0 / 0 |
| spheres 20 | 10 192 | 10 190 | 5 583 (55 %) |

**Decision (stop rule):** an indirect `Orient` can save at most about 2 % of the constructions on `cgal` and
Thingi10K and none on `cherchi` and the grids: a fraction of a percent of the time, below what the A/B runs can
resolve. It is not wired in. Only spheres 20 (few, large exact points) would gain.

**What the numbers say instead.** The exact work on the real classes is the constructions themselves, and almost
every constructed point is needed by the output. The lever is cheaper constructions, not fewer: building the
implicit points in fixed width (the homogeneous `N`, `d` of section 3 on `FixedArith`, as the generated graphs do)
and reducing them by a gcd on limbs, instead of `BigInteger` arithmetic and `BigInteger.GreatestCommonDivisor`. That
is item 3 of section 12 of `FIXED_WIDTH_EXACT_POINTS.md` widened to all constructions; the generator can derive its
bounds. Not started: it needs the owner's decision.

## 9. Step (b): the filter (built), with timings

**Entry on doubles.** For each `inputs xyz` predicate the generator now also emits `Name(Vector3d ...)`: inputs are
points with integer coordinates `|x| ≤ 2^53` given as doubles (the arrangement's grid; anything else throws). It
runs `NameFilter` and, if that does not decide, `NameExact`: the same `FixedArith` graph as step (a), loaded from the
doubles (one limb each), no allocation and no `BigInteger`.

**Filter rule** (header of `tools/PredicateCompiler/Compiler.cs`, `filter_bounds`). The inputs are integers, so no
product underflows and the error model is `fl(x op y) = (x op y)(1 + δ)`, `|δ| ≤ u = 2^-53`. For every value the
generator derives, with exact rationals, `c` with `|ẽ − e| ≤ c m̃` (`m̃` the computed magnitude, the permanent):
a difference of two inputs `c = u`, `m̃ = |ẽ|`; a sum `c = (u + max(cx, cy)) / (1 − u)`; a product
`c = (u + cx + cy + cx cy) / (1 − u)`. A sign factor is decided if `m̃ = 0` (then it is exactly 0) or
`|ẽ| > fl(K m̃)` with `K ≥ c / (1 − u)` the next double up. The constants: `Orient3dLPI` 18 u (determinant) and
9 u (denominator), `Orient3dTPI` 28 u and 17 u.

**Tests** (`GeneratedPredicatesIndirectTests`):
- the constants against an independent first-order count of the roundings (difference 1, sum `1 + max`, product
  `1 + kx + ky`): `K` within `[k u, k u (1 + 1e-9)]`;
- the double entry against the oracle at 3 to 53 bits; the filter is never wrong and decides more than 98 % of the
  random inputs;
- exact zeros moved by one grid step at every scale up to 2^52, and exact zeros: the filter fails or is right (the
  test asserts that more than 1000 cases reach the exact stage); all-zero inputs are decided as 0 (`m̃ = 0`);
- the gate (non-integers, `±2^54`, NaN, infinity rejected; `±2^53` and `−0` accepted, and exact at the edge);
- invariance under the harness's 2^k scales.

Hand mutants of the filter rules: `K = 0`, `K / 10^6`, `K / 1000`, `K / 30`, `K / 4`, the `m̃ = 0` shortcut dropped,
`c = 0` for a difference of inputs, the product rule replaced by the sum rule, a factor's sign dropped: all killed.
`K / 4`, the leaf rule and the product rule are killed only by the first-order count (no input reaches the slack of
the bound, as in #221); that test is the guard of the derivation.

**Timings** (`BooleanComparison indirect 7`: 2000 inputs of 30-bit integer points per row, the median of 7 rounds,
ns per call; two runs in one session, both shown; the signs of all three are checked against each other):

| predicate, inputs | construct, then orient3d (`BigInteger`, gcd) | generated exact stage | generated filter + exact |
|---|---|---|---|
| `Orient3dLPI`, general | 3293 / 3100 | 1290 / 1039 | **48 / 47** |
| `Orient3dTPI`, general | 6926 / 6897 | 2204 / 2150 | **84 / 80** |
| `Orient3dLPI`, degenerate (the point on the plane) | 3089 / 3019 | 1042 / 1036 | 1093 / 1091 |
| `Orient3dTPI`, degenerate | 6692 / 6914 | 2049 / 2122 | 2195 / 2191 |

The filter decides 100 % of the general inputs and 0 % of the degenerate ones (sign 0). On general inputs the
generated predicate is 65-85 times faster than constructing the point; where the filter fails, the exact stage is
still about 3 times faster than the construction and the filter costs about 50 ns more. (One grid step off the
plane is decided by the filter at 30 bits: its relative size, about 2^-30, is far above the bound.)

**Step (c), the expansion stage,** sits between the two: it would pay where the filter fails but the expansion
is shorter than the `FixedArith` evaluation. With integer inputs every failing case so far is a true zero or within
a few ulps of one, where an expansion stage does the full work too; whether it is worth building is decided on the
envelope's inputs.
