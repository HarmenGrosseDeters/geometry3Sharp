# Portable `DMath` for the geodesic / intrinsic family (design, C1 of DETERMINISM.md)

Status: design for the owner's review, no library code yet. Context: [DETERMINISM.md](DETERMINISM.md) (cause C1, levels
L0 / L1 / L2; section 5 for the arm64 result).

## 1. Problem

The geodesic family (intrinsic triangulation, FlipOut paths, geodesic Bézier curves, heat and vector heat methods) calls
the platform libm. Measured with the harness: the result differs between Windows net10, net48 and Linux, and (macOS Apple
libm) on arm64, in the cases `geodesic.heat`, `geodesic.heat.intrinsic`, `geodesic.path`, `geodesic.vectorheat.logmap`
and the cases fed by them. Linux x64 and Linux arm64 (both glibc) are bit-identical; the remaining spread is only the
libm, plain arithmetic is identical on every runtime tested.

## 2. Scope: only the functions the family calls

Counted in `mesh_ops/` (call sites, not executions):

| file | functions |
|---|---|
| `IntrinsicTriangulation.cs` | `Atan2` (5), `Sin` (8), `Cos` (8), `Sqrt` (2, exact) |
| `IntrinsicTriangulationRefinement.cs` | `Atan2` (3), `Sin` (4), `Cos` (4), `Log` (1, base 2: `Log(x, 2)` is `Log(x) / Log(2)`), `Pow(2, n)` (1, `n` an integer: exact with `Math.ScaleB`, no libm needed), `Round` (exact) |
| `IntrinsicCommonSubdivision.cs` | `Atan2` (2) |
| `VectorHeatMethod.cs` | `Atan2` (1), `Sin` (4), `Cos` (4), `Sqrt` (2, exact) |
| `MeshGeodesicDistance.cs` | `Exp` (3), `Log` (2) |

So five functions: `sin`, `cos`, `atan2`, `exp`, `log`. `sqrt`, `abs`, `round`, `floor`, `Pow(2, int)` are exactly rounded
or exact and need nothing. `acos` / `asin` are not called by this family (they sit in `Vector2d/3d.AngleR`, mesh
analysis and curvature, a later group if the owner wants it). `sinh`, `cosh`, `tanh`, `tan`, `pow` (general),
`log10` are not used here.

## 3. Method

Own implementations of the public fdlibm algorithms (Sun, via FreeBSD msun, BSD-2 with the Sun notice; the same
algorithms as Java `StrictMath`), written from the algorithm descriptions and the published coefficients, using only
`+ - * /`, comparisons and bit operations on the IEEE 754 representation (`BitConverter.DoubleToInt64Bits`), no `Math.*`
except `Math.Sqrt` if needed, no `Math.FusedMultiplyAdd`:

- `sin`, `cos`: argument reduction (Cody-Waite for `|x| < 2^20 * pi/2`, Payne-Hanek `__kernel_rem_pio2` beyond, which
  the geodesic code never needs: angles are in `[-2 pi, 2 pi]`, but the function stays total), then the two polynomial
  kernels on `[-pi/4, pi/4]`.
- `atan2`: fdlibm `atan` (argument reduction with four constants and an odd polynomial) plus the quadrant logic and the
  special cases (zeros, infinities, NaN) of `e_atan2.c`. `portable_atan2` of PR #539 (not merged at the time of writing:
  `master` has no `portable_atan2`) is taken as the starting point if it is merged by then, otherwise it is replaced by
  this.
- `exp`: reduction `x = k ln2 + r` with a split `ln2`, the rational approximation of `e_exp.c`, scaling by `2^k` by
  exponent arithmetic (with the subnormal and overflow cases).
- `log`: `e_log.c` (reduction to `[sqrt(2)/2, sqrt(2)]`, the `s = f / (2 + f)` series).
- `Log2(x)` as `log(x) / ln2` is wrong in the last bit for exact powers of two; the refinement code rounds the result
  (`Math.Round`), so only the integer part matters: `DMath.Log2Floor`-style helper not needed, an exact `ilogb`-based
  rounding replaces the call (to be decided in the PR, with a test on the boundaries `x = 2^k * sqrt(2)`).

**Why this is deterministic.** Every operation is a correctly rounded IEEE 754 binary64 add, sub, mul, div, compare or
integer operation. On x64 (SSE2) and arm64 these give the same bits on every runtime; .NET does not contract `a * b + c`
into an FMA on either (shown by `probe.arith-chain` on x64 and, in section 5 of DETERMINISM.md, by every pure-input case
on arm64). Not covered: 32-bit x87 processes (net48 x86), as already stated for C4.

**Error.** fdlibm documents less than 1 ulp for `sin`, `cos`, `atan2`, `exp`, `log` on all arguments. The PR measures it
instead of quoting it: over a fixed set of 1e6 SplitMix64 arguments per function against a double-double (or `BigInteger`
based) reference, maximum error in ulp, pinned as a test (`<= 1 ulp`; expected observed maximum about 0.8 ulp). So the new
results are not bit-identical to `Math.*` but as close as `Math.*` is to the truth (the platform libms are within 1 ulp as
well, which is where the 1 ulp spread between them comes from).

## 4. Cost

- Per call: about 1.5 to 4 times slower than the hardware / CRT function (the figure of DETERMINISM.md section 7; a C#
  fdlibm `sin` is roughly 15 to 30 ns). To be measured in the PR with `IntrinsicBenchmark` (before / after, numbers in the
  benchmark README).
- Share of the run time: the intrinsic code spends its time in the sparse solves and edge flips, not in trig; the call
  counts above are per edge or per vertex, not per inner loop. Expected effect on `MeshGeodesicPath` / heat solves: below
  a few percent. If the benchmark shows more, the polynomial kernels can be inlined.
- Code: about 600 lines (`math/DMath.cs`, internal, plus the tests), one file.

## 5. Contract levels (documented in DETERMINISM.md and OPERATIONS.md)

- **L0** (same runtime, OS, architecture): serial = parallel = repeated, already holds everywhere measured.
- **L1** (x64 across net48 / net10, Windows / Linux, and linux arm64): operations without libm, or with `DMath`, give the
  same bits. Holds today for the exact, repair, remesh, reduce, contouring, LSCM / SLIM / BFF operations with libm-free
  input; the geodesic family joins with `DMath`.
- **L2** (all platforms including macOS arm64, and all I/O): the geodesic family with `DMath`, plus the writers (C3,
  merged) and the readers (C2, merged); the rest of the library (`acos`, `asin`, `tan`, `sinh`... in curvature, mesh
  analysis, terrain, parameterization) is not L2 until its group is converted.

## 6. Plan, tests

One PR (the same area), one commit per function group, each with its own tests:

1. `DMath.Atan2` (+ special cases), `Sin`, `Cos`, `Exp`, `Log` with: error bound against the reference, special values
   (`0`, `-0`, `inf`, `NaN`, subnormals, huge arguments, the reduction boundaries), pinned bits (a hash over 100k fixed
   arguments per function; the same hash on the net48 and the Linux / Windows / macOS jobs), monotonicity and symmetry
   spot checks (`sin(-x) = -sin(x)` exactly, `cos(-x) = cos(x)` exactly).
2. Switch the five files of section 2; the geodesic tests that pin values move once (golden values regenerated, listed in
   the PR); invariance test over `Similarity.All()` for the touched operations (2^k scale gives the same bits: `atan2`
   computes `y / x` first, so it is scale covariant; verified, not assumed); the determinism harness gets a
   `geodesic.bezier` case and the arm64 and macOS jobs of #574 then show whether the geodesic cases become identical.
3. The harness comparison after the PR is the acceptance test: `geodesic.*` identical across windows net10, net48, linux
   x64 / arm64 and macOS arm64.

API: `DMath` is `internal` (upstream 1.0.324 API unchanged). Mutation tests apply (new file under `math/`); it is not
exact code (no entry in `ExactCodeAllowList`, FMA and `float` bans do not apply, but the file uses neither).

## 7. Sources and licence

fdlibm (`e_exp.c`, `e_log.c`, `k_sin.c`, `k_cos.c`, `e_rem_pio2.c`, `k_rem_pio2.c`, `s_atan.c`, `e_atan2.c`): Copyright (C)
1993 by Sun Microsystems, "Permission to use, copy, modify, and distribute this software is freely granted, provided that
this notice is preserved", the same text in FreeBSD msun (BSD-2) and in Java's `StrictMath`. This is permissive and fits
the rule "port only from MIT / Apache / BSD sources"; the implementation is rewritten in C# from the algorithms and the
published constants, the Sun notice goes into the file header and the sources into `docs/SOURCES.md`. To be confirmed by
the owner before the build: that the Sun / FreeBSD notice counts as BSD for the repository's rule. Not used: glibc
(LGPL), CORE-MATH (correctly rounded, but its licence and a per-function review are needed first; the better option if the
owner wants bit-exact against the true value rather than against fdlibm).

## 8. Decisions asked of the owner

1. Scope: the five functions of the geodesic family now (recommended), or also `acos` / `asin` / `tan` for the rest of the
   library in a second group.
2. Sources: fdlibm (Sun / FreeBSD notice) as above, or wait for a licence check of CORE-MATH (correctly rounded, larger,
   needs a per-function port).
3. Acceptance: bit-identical geodesic cases on all platforms of the harness (L2 for the family), with golden values of the
   geodesic tests moving once.
