# Error bound audit of the exact code (2026-11)

Research spike (owner 2026-11-22). Question: is any hand-written floating-point error bound in the exact code too
small? A filter with a bound that is too small claims a sign it cannot know, silently. This note is the inventory,
an independent re-derivation by a new checker (`tools/ErrorBoundAudit`), an adversarial run of the library's own
code against its exact types, and the findings. No exact code was changed. Base: master 8bb5327b.

## 1. Result

- **Inventory:** about 155 hand-written bounds that back an exact or certified decision, 28 uses of `ExactInterval`
  (sound by construction, one rule) and about 100 tolerances without a soundness claim (not audited).
- **Checked by the new tool: 31 bounds** (section 4). 32 proof obligations are derived and all hold, among them every
  sign filter of `ExactPredicates`, `CertifiedDet3`, the boolean's plane and orientation filters, the arrangement's
  approximate predicates and the snap grid's corner filter; 5 of the 32 are derived for the normal range only (the
  subnormal range argued and sampled). 9 further rows are checks of numbers (widths, ranges, rounding factors) and
  8 compare a constant with its published value; they are marked as such, not as derivations. The constants of
  Shewchuk's stage A hold to second order: this analysis proves Orient3d with 7u + 49u^2 (the code has 7u + 56u^2)
  and InSphere with 15u (the code has 16u). The tightest hand-made bound is `plane_side_abs` (margin 1 + 2u: sound,
  with nothing to spare).
- **Four findings** (section 3; issues #1347 to #1350): one bound that is too small with a reproducing input (F1, only below about 1e-103),
  two range assumptions that the callers do not enforce (F2, F3, extreme ranges), one bound that is too small against
  its stated margin and sound only through a spare tolerance downstream (F4).
- **Adversarial run:** 39 groups of near-degenerate inputs (20 000 each, 5 000 for the whole predicates) over each
  filter's exponent range, the library's code against `ExactDyadic`: no wrong decision in a bound the audit calls
  sound (section 6).
- **Not re-derived in this spike:** about 110 bounds whose core is an analytic or geometric argument, or interval
  code (section 5). They are listed with what was and was not checked; "not re-derived" is not a verdict of sound.

Verdicts, in the three classes of the brief:

| Verdict | Bounds |
|---|---|
| sound with margin x | the 31 of section 4 (margins in the table; 5 of them derived for the normal range only), apart from the finding rows of EB-7 and RC-4 |
| sound only under a range assumption the callers do not enforce | F2 `SecondPoint`, F3 `same_ray`, F4 the Biot-Savart sums (through the spare 1e-6) |
| too small, with an input | F1 `negative_volume` (subnormal range); F4 against its own stated margin from about 9 000 segments, with no wrong decision (the spare) |

## 2. Method

### 2.1 The model

Binary64, round to nearest, one rounding per operation (the exact code bans FMA and `float`; x87 is not a target):

    fl(x + y) = (x + y)(1 + d)             |d| <= u / (1 + u), u = 2^-53; exact where the sum is subnormal
    fl(x * y) = (x * y)(1 + d) + n         |n| <= eta = 2^-1075, n = 0 where the product is normal
    |fl(z) - z| <= u |fl(z)|               (half an ulp of the result)

Higham, sections 2.2, 2.8 and 3.1; the sharp constant u / (1 + u) is Knuth's, proved for all operations by Jeannerod
and Rump 2018. Division and the square root are used only in chains of such factors.

### 2.2 The checker (`tools/ErrorBoundAudit/fpbound.py`)

A computed double x^ that stands for an exact real x carries three facts, each a polynomial with non-negative rational
coefficients in named atoms (the magnitudes of the inputs, norms, error bounds given as doubles):

    |x^| <= hi        |x^ - x| <= err        |x^| >= lo - defect   (only without cancellation)

Each operation maps the facts of its operands to those of its result (a forward error analysis; the rules are next
to the operators). A model writes the guarded expression and the code's bound with these operators, in the order
of the source. The claim "err <= bound" for all inputs is then proved monomial by monomial: every coefficient of the
error is at most that of a lower bound of the computed bound. This is sufficient, needs no search over inputs, and
is exact (rationals). The **margin** is the smallest ratio of the two coefficients.

Four devices keep it sharp and honest:

- **Sign filters need less than an enclosure.** x^ = fl(z) has the sign of z, so only the error before the last
  rounding counts (Shewchuk 1997, section 4.2). `sign_dominates` uses it; an enclosure (`VolumeSumWithBound`,
  `CertifiedDet3`, the arrangement's `err` outputs) uses the full error.
- **Second order.** A constant such as (7 + 56u)u is decided at second order, where an error stated in the inputs
  is too coarse. `Mirrored` states the error in the computed permanent itself, |e^ - e| <= c m^ + A with |e^| <= m^
  by the monotonicity of rounding: the invariant of Shewchuk's proofs and of `tools/PredicateCompiler`'s filter
  rule. It was re-derived here, not taken over (the generator divides by 1 - u where this uses 1 + u).
- **Underflow.** The absolute terms eta are monomials of lower degree. They are matched against the code's explicit
  underflow term (`2^-1066 * sum`, `+ Tiny`), or against the floor that a guard gives (`permanent > 1e-200`),
  or excluded by a range fact (`minmag`: a product of values that are 0 or at least 2^-352 is normal). Where a model
  is derived only for normal products (`normal_only`, five arrangement entries), the table says "proved (normal
  range)"; the subnormal range is then argued per entry (section 4) and sampled against the final claim.
- **Mathematical lemmas are named, not hidden**: Hadamard's inequality for the perturbation of a determinant
  (EB-4, EB-5), x y <= (x^2 + y^2) / 2 for the cross terms of an underflow error (EP-3, EP-4), and
  sum of |x_i y_j z_k| over the six permutations <= sqrt(2) |x| |y| |z| (EB-7).

The analysis is itself tested: every model also runs on concrete doubles (Python floats are binary64) next to exact
fractions, and each node's three facts are checked at every sample; mutants (a smaller constant, no underflow term, a
product rule without its rounding, a guard removed from one of three functions that share its line) must fail
(`test_error_bound_audit.py`). The column "observed" is the largest error / bound seen in the concrete samples (300
per model; 40 and 1 for EP-5 with 7 and 40 triangles): values near 1 show a bound that is attained, not only proved.
"-" means the model has no such observation (a check of numbers, or an enclosure checked as contained or not).

The table's result column separates what was done: **proved** (a derivation by these rules), **proved (normal
range)**, **checked (arithmetic)** (numbers only: widths, ranges, chains of rounding factors; no expression of the
source is modelled) and **matches reference** (a constant parsed from the source equals its published value).
An independent review of the checker (a fresh agent with the diff, not the reasoning) found two gaps in the rules of
size u eta and u, fixed here without a change of any verdict, and the overclaims that this paragraph now avoids.

### 2.3 The adversarial run (`tools/ErrorBoundAudit/Adversarial`)

The model is a transcription; the source is what ships. So a second tool calls the library's own filters by
reflection (internal and private ones included) and compares with the exact value in the library's `ExactDyadic`.
Inputs are nearly degenerate (a point computed in the plane of three others, then moved by ulps or by 2^-50 to 2^-20
of its size) with mantissas that round badly, in groups of exponents over each filter's range, the subnormal range
included. Exact points behind approximate inputs are x (1 + k 2^-60), |k| <= 256, or x + t e.

### 2.4 Sources

Higham, *Accuracy and Stability of Numerical Algorithms*, 2nd ed. (the model, gamma_n, recursive summation); Shewchuk
1997 (the stage A to D bounds, the computed-permanent invariant); Jeannerod and Rump 2018 (u / (1 + u)); the Gappa
papers and manual (de Dinechin, Lauter, Melquiond, *Certifying the floating-point implementation of an elementary
function using Gappa*, IEEE TC 2011; Daumas and Melquiond, ACM TOMS 2010) for the idea of rewriting an error into
terms a rule bounds; text only, no code of Gappa was read or run. No code of CGAL, GEOS, fTetWild, TetGen, LEDA or
CORE was read. Prior work in the repository: `tools/Z3Pilot/filter_rules.py` proves the generator's per-operation
rules with Z3, and `tools/FPTaylorPilot` checks leaves of one generated filter; neither covers a hand-written bound.

## 3. Findings

Each was reported to the coordinator when found and is an issue (F1 #1347, F2 #1348, F3 #1349, F4 #1350); the fixes are not part of this spike. Reproducers: `dotnet run --project tools/ErrorBoundAudit/Adversarial -c Release`.

### F1 (#1347). `ExactMeshBoolean.negative_volume`: no underflow term (too small, with an input)

`mesh_ops/ExactMeshBoolean.cs:1056-1068`. The claim is that v6 < 0 is certain where `v6 < -(32 + n) u S`. In the
normal range it is proved (EB-7: one term has an error of at most 11.4 u T against the claimed 32 u T, and the sum
holds up to n = 2^28 triangles). But the bound has no absolute term and no guard. Where the products of three
coordinate differences are subnormal, the bound rounds to 0 and `v6 < -0` decides on a v6 that is all rounding.

Input (vertices; triangles (0, 1, 2) and (1, 3, 4); the component is both):

    (3.4063678720065236E-108, 3.40636787200652E-108, 8.515952165671821E-109)
    (-8.515919680016305E-109, -1.70318393600326E-108, -8.515919680016307E-109)
    (-3.406367872018913E-108, 6.81273574401304E-108, 3.4063678720065455E-108)
    (-3.4063678720065202E-108, -2.530540142149384E-108, 1.7031839360033087E-108)
    (-1.7031839360032601E-108, -8.515919680016305E-109, 1.7032099245276744E-108)

`negative_volume` returns true; the exact v6 is positive. Effect: `nest.NegativeVolume++`, the refusal of line 1136,
for a component that is not inside out. Reach: only where S is below the normal range, coordinate differences below
about 1e-103. Fix direction: the absolute term of `VolumeSumWithBound` (EP-5, proved), or `ExactPredicates.VolumeSign`.

### F2 (#1348). `ExactRayTriangle.SecondPoint`: the exponent clamp (range assumption not enforced)

`math/ExactRayTriangle.cs:56` clamps m to +-2000, but `exponent(mo) - exponent(md) + 1` reaches 2098.
`SecondPoint((2^1000, 0, 0), (2^-1074, 0, 0))` returns the origin: the scaled direction 2^926 is below half an ulp of
2^1000. The ray queries of `DMeshAABBTree3` and `IntrRay3Triangle3` then miss everything, for a finite nonzero
direction; the documentation promises the origin only for a zero direction or a value that is not finite. Also, the
comment "at least 2 mo" is not what the code gives (the scaled direction is only above mo); p != origin still
follows.

### F3 (#1349). `ExactTriangleIntersection.same_ray`: an unguarded dot product (range assumption not enforced)

`intersection/ExactTriangleIntersection.cs:397-400` takes `(a - s).Dot(b - s) > 0` as exact for collinear points.
The sign is right unless both products underflow to 0: `same_ray(0, (1e-170, 0), (2e-170, 0))` is false, and true
after scaling by 2^600. It is used in the coplanar corner tests of lines 325 and 361, so a coplanar overlap along two
shared rays can be missed below about 1e-162. The same pattern, by the inventory and not reproduced here:
`TerrainSlopeComposite` lines 451, 1400, 1403, 1488 and `TerrainSlopeMap.stitch` line 1321.

### F4 (#1350). The Biot-Savart sums are rounded up by a fixed factor (too small against the stated margin; absorbed)

`boundary_bound`, `gradient_bound`, `piece_cost`, `cluster_bound` (`mesh_ops/ExactMeshBoolean.cs:2057-2110,
2757-2820`). One term is sound (RC-4, RC-5: a length loses at most 4u and has 9u; a far term loses 2.65u and the
caller adds 1e-12). Two things are not in any factor:

1. A recursive sum of n non-negative terms can lose (n - 1) u. `boundary_bound` on one segment of length 1 and n of
   length 1.4999 * 2^-52: `Total` is below the exact sum of the lengths by 1.1e-13, 1.1e-11 and 1.1e-10 of `Total`
   for n = 1e3, 1e5, 1e6. The margin is 1e-12 (plus the slack 1e-12 s on the distance). The same holds for g.
2. The segments' grid doubles are the exact ends converted once (within 2^-52 each), which can shorten a segment by up
   to 2 * 2^-52 of the coordinate size; `Lengths` has no term for it. (The tree's `Len` has one: `(count + 4) 4.5e-16`.)

Where the fixed factor stops: the worst-case loss (n - 1) u exceeds 1e-12 from n = 9 008 boundary segments of one
open mesh (the slack on the distance adds between 4e-13 and 2e-9 depending on the geometry, so at the least from about
13 000). Reachable: an open operand with ten thousand boundary edges is ordinary input, and `Total` and the sum of the
near terms run over all of them. So the bound G, as a statement, is too small from there on.

No wrong decision follows. The rule compares the accumulated bound with `margin = |w - h| - e - 2e-6` and needs
only 1e-6 of that (EXACT_BOOLEAN_TAIL_55928.md, section 2; the L2 carry uses the same form), so 1e-6 absolute is
spare. It covers a relative loss of acc up to about 2e-6: n up to about 1.8e10 for the sums, which no mesh reaches
(triangle ids are 32-bit), and a mean segment length above about 1e-10 of the coordinate size for the ends, which a
valid input can violate (and the code does not check). Fix direction: the factor (1 + (n + c) u) and the end term,
as the tree; until then the spare 1e-6 is a documented part of the bound, not a reserve.

### Smaller observations (no defect)

- `ExactMeshArrangement.cs:836`: the comment says "rounding 2 U M^2", the code uses 3 U M^2 (the larger; AR-6 proves it).
- `tools/PredicateCompiler/Compiler.cs:251`: the comment says "K > 2^-60", the check is K >= 2^-62 (which is what the argument needs).
- "Relative error at most 2^-52" of a converted coordinate is used as |x^ - x| <= 2^-52 |x^| (one ulp of the double).
  That is what a truncating conversion gives, and the 2.3e-16 constants cover it with a margin of 1.036 (RC-2).
- Lead from the inventory, checked (reproducer L1 of the adversarial tool): `TriangleBoxTree.segment_hits` tests only
  p in its branch for a segment parallel to a slab (`|d| < 1e-300`), without slack. With pad 0 (the call of
  `PolyhedralEnvelope.cs:512`) it returns false for the box x in [1e-310, 1] and the segment from x = 0 to 2e-310,
  whose end is in the box. Not a finding: it needs coordinate differences below 1e-300; the boolean's callers pass a
  pad of at least 1e-300 + 1e-9 of the point's size; and in the envelope the search box has a margin of about
  9e-16 (Epsilon + M) around the polyhedron, so a dropped candidate matters only for Epsilon and coordinates below
  about 1e-285, where it turns `ContainsSegment` from true to false (the conservative side).

## 4. Machine-derived bounds

Generated by `python3 tools/ErrorBoundAudit/audit.py --samples 300 --markdown`. "margin 1 +k u" is 1 + k 2^-53.
The line is that of the bound's first anchor. u = 2^-53.

| id | file:line | claimed bound | assumption | claim | result | margin | observed |
|---|---|---|---|---|---|---|---|
| EP-1 Orient2d filter | `math/ExactPredicates.cs:21` | (3 + 16 u) u * sum | none on the coordinates; the guard sum > 1e-200 replaces the underflow term | sign | proved | 1 +1.33 u | 0.667 |
| EP-2 Orient3dFilter | `math/ExactPredicates.cs:22` | (7 + 56 u) u * permanent + 2^-1066 (\|wx\| + \|wy\| + \|wz\| + 1) | none (the underflow term covers subnormal products; with_underflow drops it only where it rounds away) | sign | proved | 1 +1 u | 0.286 |
| EP-3 InCircle filter | `math/ExactPredicates.cs:23` | (10 + 96 u) u * permanent + 2^-1066 (lifts + \|products\| + 1) | none (underflow term) | sign | proved | 1 +1.1 u | 0.3 |
| EP-4 InSphere filter | `math/ExactPredicates.cs:24` | (16 + 224 u) u * permanent + 2^-1066 ((lifts)(\|z\| + 1) + \|minors\| + 1) | none (underflow term) | sign | proved | 1.067 | 0.176 |
| EP-5 VolumeSumWithBound, n = 1 | `math/ExactPredicates.cs:511` | (Orient3dBound perm + 2^-1066 under + gamma_(n+2) abs)(1 + 3 gamma_(n+2)) | g < 1/4 (checked by the code); derived for n = 1, 2, 7, 40 triangles only (n is symbolic in the code) | enclosure, n = 1 | proved | 1 +10 u | 0.156 |
| EP-5 VolumeSumWithBound, n = 2 | `math/ExactPredicates.cs:511` | (Orient3dBound perm + 2^-1066 under + gamma_(n+2) abs)(1 + 3 gamma_(n+2)) | g < 1/4 (checked by the code); derived for n = 1, 2, 7, 40 triangles only (n is symbolic in the code) | enclosure, n = 2 | proved | 1 +11 u | 0.134 |
| EP-5 VolumeSumWithBound, n = 7 | `math/ExactPredicates.cs:511` | (Orient3dBound perm + 2^-1066 under + gamma_(n+2) abs)(1 + 3 gamma_(n+2)) | g < 1/4 (checked by the code); derived for n = 1, 2, 7, 40 triangles only (n is symbolic in the code) | enclosure, n = 7 | proved | 1 +22 u | 0.0991 |
| EP-5 VolumeSumWithBound, n = 40 | `math/ExactPredicates.cs:511` | (Orient3dBound perm + 2^-1066 under + gamma_(n+2) abs)(1 + 3 gamma_(n+2)) | g < 1/4 (checked by the code); derived for n = 1, 2, 7, 40 triangles only (n is symbolic in the code) | enclosure, n = 40 | proved | 1 +87 u | 0.0144 |
| CD-1 CertifiedDet3.Compute | `queries/CertifiedDet3.cs:22` | Up(Up(10 u perm) + Up(2^-1070 Up(M + 1))) | no overflow (checked: not finite is undecided) | enclosure (general branch) | proved | 1.25 | 0.207 |
| CD-1 CertifiedDet3.Compute | `queries/CertifiedDet3.cs:22` | Up(Up(10 u perm) + Up(2^-1070 Up(M + 1))) | no overflow (checked: not finite is undecided) | enclosure (absorbed branch) | proved | 1.25 | 0.207 |
| EP-6 TryCompareAbsVolume6 slack | `math/ExactPredicates.cs:542` | a relative slack of 4 u on each side | t a normal double (checked), 3 t finite (checked) | greater | proved | 1 +4 u | - |
| EP-6 TryCompareAbsVolume6 slack | `math/ExactPredicates.cs:542` | a relative slack of 4 u on each side | t a normal double (checked), 3 t finite (checked) | less | proved | 1 +3.67 u | - |
| EP-7 orient3d_int widths | `math/ExactPredicates.cs:257` | minors < 2^125, sum < 2^189 | \|difference\| < 2^62 in units of 2^e (checked by diff) | minor fits 128 bits | checked (arithmetic) | - | - |
| EP-7 orient3d_int widths | `math/ExactPredicates.cs:257` | minors < 2^125, sum < 2^189 | \|difference\| < 2^62 in units of 2^e (checked by diff) | sum fits 192 bits | checked (arithmetic) | - | - |
| AP-1 AdaptivePredicates range | `math/ExactPredicates.cs:648` | degree-4 products and their rounding errors are normal | every coordinate 0 or in [2^-100, 2^100] (Expansion.InRange, checked by the callers of AdaptivePredicates) | no underflow | checked (arithmetic) | - | - |
| AP-1 AdaptivePredicates range | `math/ExactPredicates.cs:648` | degree-4 products and their rounding errors are normal | every coordinate 0 or in [2^-100, 2^100] (Expansion.InRange, checked by the callers of AdaptivePredicates) | no overflow | checked (arithmetic) | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | ResultErrBound | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | CcwErrBoundB | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | CcwErrBoundC | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | O3dErrBoundB | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | O3dErrBoundC | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | IccErrBoundB | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | IccErrBoundC | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | Eps | matches reference | - | - |
| AP-2 AdaptivePredicates constants | `math/AdaptivePredicates.cs:29` | Shewchuk's constants, eps = 2^-53 | AP-1 | CcwErrBoundB, first order: the tails need 2 u | checked (arithmetic) | 1 +2.5 u | - |
| EB-1 front_side_filtered | `mesh_ops/ExactMeshBoolean.cs:4237` | 1e-14 * mag | inputs within 2^-52 of the exact values; mag >= 1e-290 (checked) | sign | proved | 12.87 | 0.0576 |
| EB-2 plane_side_filtered | `mesh_ops/ExactMeshBoolean.cs:4262` | 1e-14 * mag | as EB-1 | sign | proved | 12.87 | 0.0555 |
| EB-3 plane_side_abs | `mesh_ops/ExactMeshBoolean.cs:4289` | 1e-14 * mag + (\|A\| + \|B\| + \|C\|) e (1 + 1e-15) | plane coefficients within one ulp; mag >= 1e-290 (checked) | sign | proved | 1 +2 u | 1 |
| EB-4 orient3d_filtered | `mesh_ops/ExactMeshBoolean.cs:5137` | 1.01 ((\|u\|+eu)(\|v\|+ev)(\|w\|+ew) - \|u\|\|v\|\|w\| + 1e-15 permanent), eu = 3 * 2^-52 \|(\|b\|+\|a\|)\| | coordinates 0 or >= 2^-300 (checked: tiny); permanent >= 1e-280 (checked) | sign (row error 0.500 eu) | proved | 2.02 | - |
| EB-5 orient3d_filtered_err / _near / rows | `mesh_ops/ExactMeshBoolean.cs:5168` | eu = sqrt(3)(ea + eb)(1 + 1e-15) + u \|(\|b\|+\|a\|)\| | as EB-4; ea..ed >= 0 (checked) | root3 (1 + 1e-15) rounded is at least sqrt(3) | checked (arithmetic) | - | - |
| EB-5 orient3d_filtered_err / _near / rows | `mesh_ops/ExactMeshBoolean.cs:5168` | eu = sqrt(3)(ea + eb)(1 + 1e-15) + u \|(\|b\|+\|a\|)\| | as EB-4; ea..ed >= 0 (checked) | sign (row error 1.000000 eu) | proved | 1.01 | - |
| EB-6 widened | `mesh_ops/ExactMeshBoolean.cs:1824` | e (1 + 1e-9) + 1e-300 + 1e-14 \|v\| | none | enclosure | proved | 1 +9.01e+06 u | - |
| EB-7 negative_volume | `mesh_ops/ExactMeshBoolean.cs:1064` | (32 + n) u * sum of \|x\|\|y\|\|z\| | NO UNDERFLOW: every product of three differences normal. Not checked by the code (finding F1) | one term | checked (arithmetic) | - | - |
| EB-7 negative_volume | `mesh_ops/ExactMeshBoolean.cs:1064` | (32 + n) u * sum of \|x\|\|y\|\|z\| | NO UNDERFLOW: every product of three differences normal. Not checked by the code (finding F1) | n terms | checked (arithmetic) | - | - |
| EB-7 negative_volume | `mesh_ops/ExactMeshBoolean.cs:1064` | (32 + n) u * sum of \|x\|\|y\|\|z\| | NO UNDERFLOW: every product of three differences normal. Not checked by the code (finding F1) | subnormal products | FINDING F1 | - | - |
| AR-4 plane_value | `mesh_ops/ExactMeshArrangement.cs:809` | 1.01 (3 * 2^-52 sum\|terms\| + (\|A\|+\|B\|+\|C\|) e) | no product underflows: the coefficients are integers and e >= 1e-300 where a coordinate is not an integer (store adds Tiny); taken as (\|A\|+\|B\|+\|C\|) e >= 1e-300 or exact products | enclosure | proved | 1.01 | 0.99 |
| AR-6 orient2d_approx | `mesh_ops/ExactMeshArrangement.cs:842` | 1.01 (2 eps (2 M + eps) + 3 * 2^-52 M^2) + 1e-300, eps = 2 e + 2^-52 M | no overflow (checked: not finite is undecided) | enclosure | proved (normal range) | 1.01 | 0.99 |
| AR-8 order_key | `mesh_ops/ExactMeshArrangement.cs:961` | 1.01 (2.33 e + 2 * 2^-52 sum\|alpha_k a_k\|) + 1e-300 | none | enclosure | proved | 1.012 | 0.988 |
| AR-9 merge_points interval | `mesh_ops/ExactMeshArrangement.cs:1093` | 1.01 err + 2^-52 \|key\| | none | enclosure | proved (normal range) | 1.01 | - |
| AR-12 incircle_approx | `mesh_ops/ExactMeshArrangement.cs:1735` | 1.01 (3 eta (4 M^2 + eta) + 64 * 2^-52 M^4) + 1e-300 | no overflow (checked) | enclosure | proved (normal range) | 1.01 | 0.44 |
| AR-13 along_approx | `mesh_ops/ExactMeshArrangement.cs:1880` | 1.01 (2 e sum\|fd\| + 5 * 2^-52 sum\|fd_k u_k\|) + 1e-300 | none | enclosure | proved (normal range) | 1.01 | 0.99 |
| AR-14 BoxGrid2 box pad | `mesh_ops/ExactMeshArrangement.cs:2846` | e + 4 * 2^-52 (\|r\| + e) | none | enclosure | proved (normal range) | 1 +6 u | - |
| SN-5 SnapGrid.CrossesInteriorFilter | `comp_geom/SnapRounding2.cs:717` | 1e-14 (\|dx\| (\|ey\| + \|cy\|) + \|dy\| (\|ex\| + \|cx\|)) | every input, side and product 0 or in [1e-140, 1e140] (checked: Ok) | sign | proved | 30.02 | 0.0258 |
| RC-1 ExactPoint3.ApproxVector3d / AddPoint | `math/ExactPoint3.cs:106` | relative error below 4 * 2^-52 | (double)BigInteger is within one ulp of the integer (truncated on .NET, as FixedArith.ToDouble states and the tests of FixedPoint3 check); the quotient normal (checked: is_safe) | relative error | proved | 1.6 | - |
| RC-2 one conversion: grid_coord, LocalPoints, LocalFrame.PointError (2.3e-16) | `mesh_ops/ExactMeshArrangement.cs:1576` | 2.3e-16 \|x^\| | the value normal (checked: tiny / 1e-290 gates) | one ulp | proved | 1.036 | - |
| RC-3 norm (rounded up) | `mesh_ops/ExactMeshBoolean.cs:5226` | (1 + 1e-15) | no square underflows (the callers' tiny gate) | rounded up | proved | 1 +6 u | - |
| RC-4 boundary_bound Lengths | `mesh_ops/ExactMeshBoolean.cs:2106` | (1 + 1e-15) | the doubles are the segment: NOT so, they are the exact ends rounded once (finding F4); no underflow of the squares | one length | proved | 1 +5 u | - |
| RC-4 boundary_bound Lengths | `mesh_ops/ExactMeshBoolean.cs:2106` | (1 + 1e-15) | the doubles are the segment: NOT so, they are the exact ends rounded once (finding F4); no underflow of the squares | Total and g (sums of n terms) | FINDING F4 | - | - |
| RC-5 far_term | `mesh_ops/ExactMeshBoolean.cs:2206` | the callers' (1 + 1e-12); (1 + 1e-15) + 4 * 2^-1074 on the overflow path | one term; q and length / q normal (the callers have d >= 1e-3 s and the grid's tiny gate); the sums of terms are finding F4 | one term, rounded up by the caller | proved | 1 +9e+03 u | - |
| RC-5 far_term | `mesh_ops/ExactMeshBoolean.cs:2206` | the callers' (1 + 1e-12); (1 + 1e-15) + 4 * 2^-1074 on the overflow path | one term; q and length / q normal (the callers have d >= 1e-3 s and the grid's tiny gate); the sums of terms are finding F4 | overflow path | proved | 1 +5 u | - |
| RC-6 ExactInterval (one rule) | `math/ExactIntervals.cs:46` | one spacing on each side of the round-to-nearest result | none (NaN gives the whole line) | widening covers one rounding | checked (arithmetic) | - | - |

Notes on single entries:

- **EP-1 to EP-4.** The code's bound is `with_underflow(relative, sum)`, which drops the term where
  sum < 2^256. The model takes the full expression; that the two are the same double wherever a filter decides is
  checked as arithmetic (every constant >= 2^-51, permanent > 1e-200, so half an ulp of the relative part exceeds
  2^-769 > 2^-810).
- **EP-4.** The four terms are added in another order than their magnitudes; the model takes the code's two
  orders. The |minors| of the underflow sum are not needed by this proof (the lifts' squares cover them).
- **EP-5.** n is symbolic in the code; the derivation runs for n = 1, 2, 7, 40 only. The margin grows with n because
  gamma_(n+2) is charged where gamma_(n-1) is needed, which is the pattern a general proof by induction would use;
  that proof is the design note's (EXACT_VOLUME_SIGN.md), not this tool's.
- **AP-1, EP-7, RC-6** are checks of the numbers the comments state (grid 2^-152, widths 125 and 189 bits, half a
  spacing against a whole one); the integer routines and `ExactInterval`'s operators are not modelled.
- **AP-2.** The seven constants and Eps are parsed from the source and compared with Shewchuk's; the anchors pin
  their use. Stage B is checked at first order only (the tails of the differences need 2u; the estimate's own error
  is not in the model). Stages C and D are not re-derived.
- **EB-4, EB-5.** `orient3d_filtered_err_near` calls the same `orient3d_rows_filtered` with other rows; one model.
- **EB-7.** Proved for normal products; the missing absolute term is F1.
- **AR-6, AR-9, AR-12, AR-13, AR-14** are derived where no product is subnormal (with underflow terms the checker
  cannot match them monomial by monomial: an underflow in the bound's own evaluation needs the fact that the lost
  product was small). The other range, by hand: in AR-6 and AR-13 an underflow error is eta once, or eta times a
  factor whose product with it is itself a term of the bound when it is not tiny, and `+ Tiny` (1e-300) covers the
  rest. In AR-12 an underflow error of a 2x2 product is multiplied by a lift, eta M^2: `64 U M^4` covers it for
  M >= 2^-514, and below that it is far under Tiny. In AR-9 and AR-14 the product 2^-52 |v| is exact or, where it
  is subnormal, at least half an ulp of the sum it guards, and subnormal sums are exact. These five are also
  sampled with exponents down to -1070 against the final claim (AR-9, AR-14: the interval contains the exact value)
  without a violation; AR-12 is not sampled at mixed scales.
- **SN-5.** No range fact on the differences is used: the code's `Ok(l)`, `Ok(r)`, `Ok(bound)` make the products
  normal where the filter decides, and the model pays their underflow terms from the floor of `Ok(bound)`.
- **RC-5.** One term only, with q and length / q normal (the callers have d >= 1e-3 s).
- **The generator** (`tools/PredicateCompiler`, generated tables): its three rule sets were read once. The filter rule
  is the invariant of 2.2 with 1 / (1 - u) in place of 1 + u (slightly larger constants: sound); the value rule
  (|x +- y| <= Mx + My, |x y| <= Mx My) and the length rule are elementary; `Z3Pilot/filter_rules.py` has proved the
  filter rules per operation. No entry of a generated table was checked.

## 5. Not re-derived in this spike

Listed so that nothing reads as audited that was not. "Read" means the code and its comment were compared and the
rounding constants are of the right order; it is not a proof.

| Area | Bounds | Status |
|---|---|---|
| `AdaptivePredicates` stages C, D | 3 constants, the expansion routines | constants equal Shewchuk's; routines covered by `ExactPredicatesTests` against the integer determinants, not re-derived |
| `ExactMeshBoolean` Antipodal sum (`antipodal_sum`, `tol = 16 (ea + eb + 1e-15 + directionError)`, `4 tol / hypot`) | about 6 | the conversions (2.3e-16) and the n u summation term are right; the factor 16 is "taken generously" in the code, no derivation exists to check; `portable_atan2`'s 4e-15 is above a hand estimate of its roundings (about 1.6e-15) and 4 times the worst case its test admits (`PortableAtan2Tests`) |
| `ExactMeshBoolean` BoundaryTree (`antipodal_tree_value`: `tolNode`, `eps`, the dipole remainder, `vaError`, `Len`) | about 8 | read; analytic (multipole remainder); tested against the full sum in `ExactMeshBooleanTestsAntipodalTree` |
| `ExactMeshBoolean` L2 carry (`carry_bound`, `Center.Local` / `Grid` errors 1.2e-16, 3.4e-16, `input_segment` pad) | about 6 | read; the 3.4e-16 and 1.2e-16 terms match one conversion plus one rounded sum |
| `ExactMeshBoolean` projection prefilters (`SegmentProjection`, `pierce_prefilter`: 2.3e-16, 7e-16, 1.01) | about 5 | read only |
| `ExactMeshArrangement` `segment_point`, `planes_approx` (quotients with an error bound) | 2 | read; division is outside the checker's operators |
| `ExactMeshArrangement` `TriangleBoxTree.segment_hits` (slack 1e-9, 1e-12) | 1 | read; one unverified lead (section 3) |
| `SnapRounding2` `PixelOf` range, `Buckets`, `SnapGrid.Pixel`, `CircumcentrePixelCentre` | 4 | read; `Pixel` needs no bound (monotone rounding, exact fallback) |
| `FixedPoint3`, `ExactLimbSigns`, `ExactLimbPrepared` scratch and width counts | 15 | fail-safe (an arena overrun throws, no wrong sign); generator-checked for Orient and InCircle (`GeneratedPredicatesTests`) |
| `ExactPoint3.ToDouble`, `ScaleB` | 2 | read (truncating twice is truncating once; RC-2 uses it) |
| `PolyhedralEnvelope` (ENVELOPE.md 4.4: `Theta*`, offset margins, `unit`, `length`) | 8 | not read beyond the inventory |
| `ContinuousCollision` (filter bound B1, corner widths, separating direction) | 5 | not read beyond the inventory |
| `ExactMeshGeodesicDistance` (`CornerAngle`, `TransformDisk`, `Hypot`, merged radii) | 7 | not read beyond the inventory; `CornerAngle` calls itself "an assumption, not a proof" |
| `ContouringTopologyCertificate.may_meet_box` | 1 | not read beyond the inventory |
| Terrain (`TerrainSlope*`, `ExactSlopeKernel`: tolerance budget, `e_xy`, `e_eval`, `Slb`, `Reach`, prefilters, hand-rolled interval divisions) | 38 | not read beyond the inventory; the insertion prefilters have no exact fallback and rest on `(1 - 1e-6)`, `1.01`, `1e-9` factors and on a self-estimate of 1e-12 in `Approx` |

The inventories of the two search agents (file, line, quoted code, claimed constant, assumption, fallback for each of
these) were used for this table; they are leads, not verdicts. The Terrain, envelope, collision and geodesic bounds
are the largest block that no independent derivation has seen.

## 6. Adversarial run

`dotnet run --project tools/ErrorBoundAudit/Adversarial -c Release -- 20000 2` (8 s). "decided" counts the inputs on
which the filter gave a sign; "wrong" a sign or an enclosure that the exact value contradicts. The pairs are the
exponent ranges of the coordinates. The whole predicates (EP-1/3/4: filter and exact stages, on nearly collinear,
cocircular and cospherical points) run on 5 000 inputs per range and always answer. Where a filter decides nothing
(Orient3dFilter below its underflow limit) the line shows that it declines there, not that its bound was tested;
the plane-side filters decide only 24 to 79 of 20 000 at the low ranges, and `plane_value` is not run where a
product underflows.

```
EP-2 Orient3dFilter, exponents (-30, 30)     decided     5028 / 20000    wrong 0    worst |error| / bound NaN
EP-2 Orient3dFilter, exponents (-226, -219)  decided     4953 / 20000    wrong 0    worst |error| / bound NaN
EP-2 Orient3dFilter, exponents (-340, -300)  decided        0 / 20000    wrong 0    worst |error| / bound NaN
EP-2 Orient3dFilter, exponents (-1070, -1000) decided        0 / 20000    wrong 0    worst |error| / bound NaN
EP-2 Orient3dFilter, exponents (300, 330)    decided     5134 / 20000    wrong 0    worst |error| / bound NaN
EP-2 Orient3dFilter, exponents (-1074, 1000) decided      573 / 20000    wrong 0    worst |error| / bound NaN
EP-1/3/4 Orient2d, InCircle, InSphere (-30, 30) decided     5000 / 5000     wrong 0    worst |error| / bound NaN        wrong: 0 0 0; exact zeros: 79 115
EP-1/3/4 Orient2d, InCircle, InSphere (-270, -250) decided     5000 / 5000     wrong 0    worst |error| / bound NaN        wrong: 0 0 0; exact zeros: 0 0
EP-1/3/4 Orient2d, InCircle, InSphere (-540, -530) decided     5000 / 5000     wrong 0    worst |error| / bound NaN        wrong: 0 0 0; exact zeros: 0 0
EP-1/3/4 Orient2d, InCircle, InSphere (-1074, -1040) decided     5000 / 5000     wrong 0    worst |error| / bound NaN        wrong: 0 0 0; exact zeros: 192 83
EP-1/3/4 Orient2d, InCircle, InSphere (240, 255) decided     5000 / 5000     wrong 0    worst |error| / bound NaN        wrong: 0 0 0; exact zeros: 0 0
EB-4 orient3d_filtered (-30, 30)             decided     6305 / 20000    wrong 0    worst |error| / bound NaN
EB-5 orient3d_filtered_err (-30, 30)         decided     7448 / 20000    wrong 0    worst |error| / bound NaN
EB-4 orient3d_filtered (-299, -280)          decided    13962 / 20000    wrong 0    worst |error| / bound NaN
EB-5 orient3d_filtered_err (-299, -280)      decided    15423 / 20000    wrong 0    worst |error| / bound NaN
EB-4 orient3d_filtered (-110, -90)           decided    13687 / 20000    wrong 0    worst |error| / bound NaN
EB-5 orient3d_filtered_err (-110, -90)       decided    15186 / 20000    wrong 0    worst |error| / bound NaN
EB-4 orient3d_filtered (280, 330)            decided     7736 / 20000    wrong 0    worst |error| / bound NaN
EB-5 orient3d_filtered_err (280, 330)        decided     8996 / 20000    wrong 0    worst |error| / bound NaN
EB-2 plane_side_filtered (-30, 30)           decided     3888 / 20000    wrong 0    worst |error| / bound NaN
EB-1 front_side_filtered (-30, 30)           decided     3883 / 20000    wrong 0    worst |error| / bound NaN
EB-3 plane_side_abs (-30, 30)                decided     3886 / 20000    wrong 0    worst |error| / bound NaN
EB-2 plane_side_filtered (-150, -140)        decided       79 / 20000    wrong 0    worst |error| / bound NaN
EB-1 front_side_filtered (-150, -140)        decided       63 / 20000    wrong 0    worst |error| / bound NaN
EB-3 plane_side_abs (-150, -140)             decided       53 / 20000    wrong 0    worst |error| / bound NaN
EB-2 plane_side_filtered (-299, -290)        decided       34 / 20000    wrong 0    worst |error| / bound NaN
EB-1 front_side_filtered (-299, -290)        decided       24 / 20000    wrong 0    worst |error| / bound NaN
EB-3 plane_side_abs (-299, -290)             decided       34 / 20000    wrong 0    worst |error| / bound NaN
EB-2 plane_side_filtered (140, 160)          decided     1113 / 20000    wrong 0    worst |error| / bound NaN
EB-1 front_side_filtered (140, 160)          decided     1092 / 20000    wrong 0    worst |error| / bound NaN
EB-3 plane_side_abs (140, 160)               decided     1085 / 20000    wrong 0    worst |error| / bound NaN
CD-1 CertifiedDet3 (-30, 30)                 decided    20000 / 20000    wrong 0    worst |error| / bound 0.2691
CD-1 CertifiedDet3 (-360, -330)              decided    20000 / 20000    wrong 0    worst |error| / bound 0.2411
CD-1 CertifiedDet3 (-1074, -1040)            decided    20000 / 20000    wrong 0    worst |error| / bound 0
CD-1 CertifiedDet3 (300, 335)                decided    20000 / 20000    wrong 0    worst |error| / bound 0.2572
AR-6/12/13/4/8 arrangement (-10, 40)         decided    20000 / 20000    wrong 0    worst |error| / bound 0.9899     orient2d 0.281 incircle 0.0389 along 0.919 plane_value 0.9
AR-6/12/13/4/8 arrangement (-299, -270)      decided    20000 / 20000    wrong 0    worst |error| / bound 0.99       orient2d 0.438 incircle 3.69E-24 along 0.974 plane_value 0
AR-6/12/13/4/8 arrangement (-520, -500)      decided    20000 / 20000    wrong 0    worst |error| / bound 0.9901     orient2d 6.19E-16 incircle 6.05E-301 along 0.96 plane_valu
AR-6/12/13/4/8 arrangement (60, 70)          decided    20000 / 20000    wrong 0    worst |error| / bound 0.9901     orient2d 0.581 incircle 0.0525 along 0.989 plane_value 0.9
```

`plane_value`, `along_approx` and `order_key` reach 0.97 to 0.99 of their bounds: those are attained. InSphere's
reference expands the same determinant as the library, so it checks the rounding, not the sign convention.

## 7. Proposal: a CI test so that the bounds cannot drift

1. **The table as a test (done in the follow-up to this note's PR).** `tools/ErrorBoundAudit/test_error_bound_audit.py`:
   every anchor is still in the source; every derivation still proves its bound; the open findings are exactly F1 and
   F4; the models hold on concrete doubles; mutants of the checker fail. A changed line that a model transcribes or a
   guard it relies on (searched inside the model's function) fails the anchor test until the model is brought up to
   date. An operation added between the anchored lines is not seen: the anchors narrow the review, they do not replace
   re-reading a changed function. It runs in the `linux` job of `.github/workflows/ci.yml` (5 s, ten tests, the
   largest about 3 s), which every library change runs; the same step runs the adversarial tool at 2 000 inputs per
   group (about 3 s after its build) and fails on a wrong decision.
2. **The adversarial run as an xunit test.** `ErrorBoundAdversarialTests` in `tests/geometry3Sharp.Tests`
   (it has `InternalsVisibleTo`, so without reflection): the checks of section 6 at 2 000 inputs per range, under 1 s
   each. With each fix of F1 to F4 its reproducer becomes a test that fails on the old code (label `bugfix`).
3. **New bounds.** A review trigger in `docs/REVIEW_CHECKLIST.md` and `tools/review_triggers.py`: a diff that adds a
   rounding constant (`e-1[0-9]`, `Epsilon`, `2^-5[23]`, `1 + 1e-`) in a file of the exact list without a change to
   `tools/ErrorBoundAudit/bounds.py` is flagged; the reviewer asks for the model or for the reason it cannot have one.
4. **Next derivations**, in order of risk: the Terrain prefilters without an exact fallback; `planes_approx` and
   `segment_point` (they need division in the checker); the Antipodal factor 16 (a derivation has to be written first).

## 8. Reproduce

```
python3 tools/ErrorBoundAudit/audit.py --samples 300
python3 -m unittest discover -s tools/ErrorBoundAudit -p 'test_*.py'
dotnet run --project tools/ErrorBoundAudit/Adversarial -c Release -- 20000 2
```
