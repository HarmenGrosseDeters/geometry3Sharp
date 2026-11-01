# Indirect predicates from the predicate compiler (design, for review)

**Status:** draft for the owner's review (tier L: design before code). Nothing is built yet. Stacked on #244 (the
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

## 5. Questions for the owner

1. **Expansion stage for high degrees.** TPI-based predicates have degree 9 or more; their expansions get long (Attene
   uses them with a fallback to multiprecision). Keep the stage only where its static worst case is below a limit
   (proposal: 1024 components), else go from the filter straight to `FixedArith`?
2. **Input bound of the integer fallback.** The grid coordinates of real inputs: up to 53 bits for doubles of one
   exponent range, more when a mesh mixes very different magnitudes. Proposal: generate for `b = 64` (one limb per
   coordinate) with `BigInteger` beyond; the measured distribution decides.
3. **Runtime.** Reuse `AdaptivePredicates`' expansion primitives (made internal static helpers), or a small new
   expansion runtime on stack buffers (as #221)? Proposal: reuse, to keep one implementation; it allocates through
   its workspace, which the generated stage would share.
4. **Order of work.** Proposal: (a) spec and graph for constructions, the exact `FixedArith` stage and the oracle
   tests; (b) the filter; (c) the expansion stage. Each step is a reviewable PR on its own branch.

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
