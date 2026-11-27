# Bisector predicates of the restricted Voronoi diagram (design, for review)

Status: **PR A of certified remeshing stage 1b** ([CERTIFIED_REMESHING.md](CERTIFIED_REMESHING.md) section 11; the plan
was approved by the coordinator on 2026-11-27). These are the exact predicates and the symbolic perturbation that the
restricted Delaunay refinement needs. They are internal and have no caller yet (that comes in PR B). Files:

- `tools/PredicateCompiler/bisector.spec`;
- the generated `math/Generated/BisectorPredicates.cs`;
- the hand-written `math/ExactBisectorSoS.cs`;
- the opt-in `Delaunay3.SymbolicPerturbation`;
- the tests `GeneratedPredicatesBisectorTests` and `ExactBisectorSoSTests`.

## 1. Why these predicates (the route of stage 1b)

Stage 1b computes the **restricted Voronoi diagram** (RVD) of the samples on the input mesh S exactly, the way Yan, Lévy
et al. (SGP 2009) compute it in floating point:

- Every input triangle t is clipped by the bisector half-spaces of a site p and its Delaunay neighbours. The result is the
  convex polygon t ∩ V(p).
- The cells of a triangle are found by propagation across shared edges.
- The pieces are glued by symbolic vertex keys.

Every vertex of such a polygon is of one of three kinds:

| kind | point | symbolic key |
|---|---|---|
| 1 | a mesh vertex v | v |
| 2 | the crossing of a mesh edge q0 q1 with bisector(a, b) | (edge, {a, b}) |
| 3 | the point of bisector(a, b), bisector(a, c) and the plane of t (a point of the Voronoi edge of a, b, c; BTPI in section 14.4) | (t, {a, b, c}) |

The only decision the clipping makes is "which side of bisector(a, e) is this vertex on". That decision is P3, P4 or P1 of
CERTIFIED_REMESHING.md section 14.4, by kind. Everything the certificate needs follows from the clipped polygons and is
counted on their keys:

- the restricted Delaunay facets are the kind-3 vertices (the dual Voronoi edge meets S);
- (E') is the number of kind-3 vertices per site triple;
- (F') is the bisector arcs per site pair, glued at their ends;
- (C') is the parts and the Euler characteristic of each cell's pieces (Lemma 1);
- R2 is the distance of every polygon vertex from its site (Theorem D).

So P2 (Orient2dBTPI) and P5 (circumcentre on a plane) of section 14.4 are not needed:

- P2: the polygon starts as the triangle itself, so "inside t" is never tested.
- P5: a Voronoi vertex on S is a P1 tie, and the perturbation of section 3 resolves it.

## 2. The predicates (generated)

All of them are `ipredicate ... inputs xyz` entries of `bisector.spec`. Each is written relative to a site, in coordinate
differences only. The generator derives for each:

- the floating-point filter with its error constant and its range gate;
- the exact stage on FixedArith (after aligning the exponents to two limbs), and BigInteger beyond that;
- the bound table below.

In the formulas, b' = b − a, the bisector of a and b is 2 b'·y = |b'|², and y = x − a.

| predicate | sign of | meaning | degree | filter range |
|---|---|---|---:|---|
| `BisectorSide1(v, a, e)` | 2 e'·v' − \|e'\|² | P3: < 0 when v is closer to a | 2 | [2^-428, 2^497] |
| `BisectorSide2(q0, q1, a, b, c)` | (2 c'·N − \|c'\|² d) · sign(d), with N = q'0 l1 − q'1 l0, d = l1 − l0, l_k = 2 b'·q'_k − \|b'\|² | P4 at the kind-2 point | 4 | [2^-188, 2^247] |
| `BisectorSide3(a, b, c, t0, t1, t2, e)` | (e'·N − \|e'\|² det) · sign(det), with N = \|b'\|² (c' × n) + \|c'\|² (n × b') + 2h (b' × c'), det = b'·(c' × n), n = (t1 − t0) × (t2 − t0), h = n·(t0 − a) | P1 at the kind-3 point x = a + N / (2 det) | 6 | [2^-108, 2^164] |
| `DotSites(o, p, q0, q1)` | (p − o)·(q1 − q0) | a coefficient of the perturbation of P4 | 2 | [2^-428, 2^498] |
| `DetSitesNormal(o, p, q, t0, t1, t2)` | ((p − o) × (q − o))·n | a coefficient of the perturbation of P1 | 4 | [2^-188, 2^248] |
| `DistanceAbove1(v, a, r)` | \|v − a\|² − r² | R2 at a kind-1 point | 2 | [2^-428, 2^498] |
| `DistanceAbove2(q0, q1, a, b, r)` | (\|N\|² − r² d²) · sign(d²) | R2 at a kind-2 point | 6 | [2^-108, 2^163] |
| `DistanceAbove3(a, b, c, t0, t1, t2, r)` | (\|N\|² − 4 r² det²) · sign(det²) | R2 at a kind-3 point, and the radius of the surface Delaunay ball | 10 | [2^-44, 2^97] |
| `RadiusEdgeAbove(a, b, c, p, q, k)` | k.y \|ab\|² \|bc\|² \|ca\|² − 4 k.x \|pq\|² \|n\|², with k = (rho², 1, ·) | R3: > 0 iff the circumradius exceeds rho \|pq\| | 7 as written (6 in the points) | [2^-85, 2^140] |

Derivation of the kind-3 point. Cramer's rule for the rows (2b', 2c', n) with right-hand side (|b'|², |c'|², h):
the inverse has the columns (2c' × n, n × 2b', 2b' × 2c') / det(2b', 2c', n). Since det(2b', 2c', n) = 4 det, dividing
numerator and denominator by 2 gives y = N / (2 det). The side is then 2 e'·y − |e'|² = (e'·N − |e'|² det) / det. For the
kind-2 point, y = q'0 + t (q'1 − q'0) with t = l0 / (l0 − l1), so y = N / d. A sign of 0 also means "the point does not
exist" (d = 0 or det = 0): an edge parallel to the bisector, or a Voronoi line parallel to the plane. For R2 this is a
separate factor (d², det²), so that the result is 0 and not the sign of |N|².

Notes on the formulas:

- **R2 and R3 are exact**, not the conservative floating-point tests that section 14.4 allowed. Every decision is then
  exact and 2^k invariant, and no error bound of the library is written by hand.
- The radius r is passed as the coordinate r.x of a point. In R3, k.x = rho² and k.y = 1 are dimensionless, so the sign is
  homogeneous of degree 6 in the points. The generator counts k as degree 1 on both sides, which keeps its homogeneity
  check valid.
- R3 is tested per edge: R ≤ rho l_min holds iff R ≤ rho |pq| for every edge pq. So no comparison of the edge lengths is
  needed.

The highest degree is 10 (`DistanceAbove3`), the same range as `Orient3dTPI` (degree 9).

Three predicates have static expansion lengths within the limit of 1024: `BisectorSide1`, `DotSites` and `DistanceAbove1`.
The generator has no expansion stage, and it refuses to skip one silently. The spec therefore says `expansion none` for
them: the filter goes straight to FixedArith, which is exact and allocation-free but slower than an expansion would be.
`GeneratedPredicatesBisectorTests.ExpansionNone_IsExplicitAndOnce` pins this directive.

### 2.1 The error bounds

Every bound is the generator's, with no hand-written error bound anywhere. The filter rule is in the header of
`filter_bounds` in `tools/PredicateCompiler/Compiler.cs` (round to nearest, u = 2^-53):

- Per value, the error coefficient c satisfies |e~ − e| ≤ c m~, where m~ is the computed magnitude. For x ± y the
  coefficient is (u + max(cx, cy)) / (1 − u); for x y it is (u + cx + cy + cx cy) / (1 − u); a difference of two inputs
  has c = u.
- A sign factor is decided if m~ = 0, or if |e~| > fl(K m~) with the double K ≥ c / (1 − u).

The range gate keeps every computed value normal and finite:

- **No underflow.** A nonzero input is a multiple of 2^-(GateLow + 52), and GateLow = 960 / degree − 52, so every nonzero
  value is at least 2^-960.
- **No overflow.** GateHigh is the largest b for which every value's bound at |x| ≤ 2^b stays below 2^1000.
- **Subnormal inputs** are outside the gate and go to the exact stage.

The exact stage:

- **FixedArith** takes the coordinates times 2^-emin (integers within two limbs, which `__fits` checks). The limbs and the
  scratch of every value come from the bound table below, under the value rule (|x ± y| ≤ Mx + My, |x y| ≤ Mx My).
- **BigInteger** takes everything else, with no bound needed.

`GeneratedPredicatesBisectorTests` pins all of this:

- the checked-in file equals the generator's output (`GeneratedFile_IsUpToDate`);
- this table equals the derivation (`DesignDocTable_IsTheDerivation`);
- the reviewed degrees, gates and root bounds (`Degrees_Gates_AndRootBounds_AreTheReviewedOnes`);
- every value stays within its bound at ±m (`Bounds_Hold_AtTheBound`);
- the filter is never wrong where it decides (every oracle test checks it).

<!-- generated by tools/PredicateCompiler --table; do not edit -->
| predicate | values | bound | bits | limbs | limbs taken by FixedArith |
|---|---|---|---|---|---|
| `BisectorSide1` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `BisectorSide1` | `vax`, `vay`, `vaz`, `eax`, `eay`, `eaz` | `2 m` | 65 | 2 | 2 |
| `BisectorSide1` | `vax * eax` and 5 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `BisectorSide1` | `(vax * eax) + (vay * eay)` and 1 more of its shape | `8 m^2` | 131 | 3 | 4 |
| `BisectorSide1` | `ev`, `ee` | `12 m^2` | 132 | 3 | 4 |
| `BisectorSide1` | `ev + ev` | `24 m^2` | 133 | 3 | 4 |
| `BisectorSide1` | the determinant | `36 m^2` | 134 | 3 | 4 |
| `BisectorSide2` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `BisectorSide2` | `bax`, `bay`, `baz`, `p0x`, `p0y`, `p0z`, `p1x`, `p1y`, `p1z`, `cax`, `cay`, `caz` | `2 m` | 65 | 2 | 2 |
| `BisectorSide2` | `bax * bax` and 11 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `BisectorSide2` | `(bax * bax) + (bay * bay)` and 3 more of its shape | `8 m^2` | 131 | 3 | 4 |
| `BisectorSide2` | `bb`, `bp0`, `bp1`, `cc` | `12 m^2` | 132 | 3 | 4 |
| `BisectorSide2` | `bp0 + bp0` and 1 more of its shape | `24 m^2` | 133 | 3 | 4 |
| `BisectorSide2` | `l0`, `l1` | `36 m^2` | 134 | 3 | 4 |
| `BisectorSide2` | `d` (a sign factor) | `72 m^2` | 135 | 3 | 4 |
| `BisectorSide2` | `p0x * l1` and 5 more of its shape | `72 m^3` | 199 | 4 | 5 |
| `BisectorSide2` | `Nx`, `Ny`, `Nz` | `144 m^3` | 200 | 4 | 5 |
| `BisectorSide2` | `Nx * cax` and 2 more of its shape | `288 m^4` | 265 | 5 | 6 |
| `BisectorSide2` | `(Nx * cax) + (Ny * cay)` | `576 m^4` | 266 | 5 | 6 |
| `BisectorSide2` | `cN` | `864 m^4` | 266 | 5 | 6 |
| `BisectorSide2` | `ccd` | `864 m^4` | 266 | 5 | 6 |
| `BisectorSide2` | `cN + cN` | `1728 m^4` | 267 | 5 | 6 |
| `BisectorSide2` | the determinant (sign factor 1) | `2592 m^4` | 268 | 5 | 6 |
| `BisectorSide3` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `BisectorSide3` | `bax`, `bay`, `baz`, `cax`, `cay`, `caz`, `ux`, `uy`, `uz`, `wx`, `wy`, `wz`, `tax`, `tay`, `taz`, `eax`, `eay`, `eaz` | `2 m` | 65 | 2 | 2 |
| `BisectorSide3` | `uy * wz` and 20 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `BisectorSide3` | `nx`, `ny`, `nz`, `bcx`, `bcy`, `bcz` and 3 more like `(bax * bax) + (bay * bay)` | `8 m^2` | 131 | 3 | 4 |
| `BisectorSide3` | `nx * tax` and 14 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `BisectorSide3` | `cnx`, `cny`, `cnz`, `nbx`, `nby`, `nbz` and 1 more like `(nx * tax) + (ny * tay)` | `32 m^3` | 197 | 4 | 5 |
| `BisectorSide3` | `h` | `48 m^3` | 198 | 4 | 5 |
| `BisectorSide3` | `hh` | `96 m^3` | 199 | 4 | 5 |
| `BisectorSide3` | `bb`, `cc`, `ee` | `12 m^2` | 132 | 3 | 4 |
| `BisectorSide3` | `bax * cnx` and 2 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `BisectorSide3` | `(bax * cnx) + (bay * cny)` | `128 m^4` | 263 | 5 | 6 |
| `BisectorSide3` | `det` (a sign factor) | `192 m^4` | 264 | 5 | 6 |
| `BisectorSide3` | `cnx * bb` and 5 more of its shape | `384 m^5` | 329 | 6 | 7 |
| `BisectorSide3` | `(cnx * bb) + (nbx * cc)` and 2 more of its shape | `768 m^5` | 330 | 6 | 7 |
| `BisectorSide3` | `hh * bcx` and 2 more of its shape | `768 m^5` | 330 | 6 | 7 |
| `BisectorSide3` | `Nx`, `Ny`, `Nz` | `1536 m^5` | 331 | 6 | 7 |
| `BisectorSide3` | `Nx * eax` and 2 more of its shape | `3072 m^6` | 396 | 7 | 8 |
| `BisectorSide3` | `(Nx * eax) + (Ny * eay)` | `6144 m^6` | 397 | 7 | 8 |
| `BisectorSide3` | `eN` | `9216 m^6` | 398 | 7 | 8 |
| `BisectorSide3` | `eed` | `2304 m^6` | 396 | 7 | 8 |
| `BisectorSide3` | the determinant (sign factor 1) | `11520 m^6` | 398 | 7 | 8 |
| `DotSites` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DotSites` | `ox`, `oy`, `oz`, `qx`, `qy`, `qz` | `2 m` | 65 | 2 | 2 |
| `DotSites` | `ox * qx` and 2 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DotSites` | `(ox * qx) + (oy * qy)` | `8 m^2` | 131 | 3 | 4 |
| `DotSites` | the determinant | `12 m^2` | 132 | 3 | 4 |
| `DetSitesNormal` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DetSitesNormal` | `px`, `py`, `pz`, `qx`, `qy`, `qz`, `ux`, `uy`, `uz`, `wx`, `wy`, `wz` | `2 m` | 65 | 2 | 2 |
| `DetSitesNormal` | `uy * wz` and 11 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DetSitesNormal` | `nx`, `ny`, `nz`, `mx`, `my`, `mz` | `8 m^2` | 131 | 3 | 4 |
| `DetSitesNormal` | `nx * mx` and 2 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `DetSitesNormal` | `(nx * mx) + (ny * my)` | `128 m^4` | 263 | 5 | 6 |
| `DetSitesNormal` | the determinant | `192 m^4` | 264 | 5 | 6 |
| `DistanceAbove1` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DistanceAbove1` | `vax`, `vay`, `vaz` | `2 m` | 65 | 2 | 2 |
| `DistanceAbove1` | `vax * vax` and 2 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DistanceAbove1` | `(vax * vax) + (vay * vay)` | `8 m^2` | 131 | 3 | 4 |
| `DistanceAbove1` | `((vax * vax) + (vay * vay)) + (vaz * vaz)` | `12 m^2` | 132 | 3 | 4 |
| `DistanceAbove1` | `r.x * r.x` | `m^2` | 128 | 2 | 2 |
| `DistanceAbove1` | the determinant | `13 m^2` | 132 | 3 | 4 |
| `DistanceAbove2` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DistanceAbove2` | `bax`, `bay`, `baz`, `p0x`, `p0y`, `p0z`, `p1x`, `p1y`, `p1z` | `2 m` | 65 | 2 | 2 |
| `DistanceAbove2` | `bax * bax` and 8 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DistanceAbove2` | `(bax * bax) + (bay * bay)` and 2 more of its shape | `8 m^2` | 131 | 3 | 4 |
| `DistanceAbove2` | `bb`, `bp0`, `bp1` | `12 m^2` | 132 | 3 | 4 |
| `DistanceAbove2` | `bp0 + bp0` and 1 more of its shape | `24 m^2` | 133 | 3 | 4 |
| `DistanceAbove2` | `l0`, `l1` | `36 m^2` | 134 | 3 | 4 |
| `DistanceAbove2` | `d` | `72 m^2` | 135 | 3 | 4 |
| `DistanceAbove2` | `p0x * l1` and 5 more of its shape | `72 m^3` | 199 | 4 | 5 |
| `DistanceAbove2` | `Nx`, `Ny`, `Nz` | `144 m^3` | 200 | 4 | 5 |
| `DistanceAbove2` | `rd` | `72 m^3` | 199 | 4 | 4 |
| `DistanceAbove2` | `dd` (a sign factor) | `5184 m^4` | 269 | 5 | 6 |
| `DistanceAbove2` | `Nx * Nx` and 2 more of its shape | `20736 m^6` | 399 | 7 | 8 |
| `DistanceAbove2` | `(Nx * Nx) + (Ny * Ny)` | `41472 m^6` | 400 | 7 | 8 |
| `DistanceAbove2` | `((Nx * Nx) + (Ny * Ny)) + (Nz * Nz)` | `62208 m^6` | 400 | 7 | 8 |
| `DistanceAbove2` | `rd * rd` | `5184 m^6` | 397 | 7 | 8 |
| `DistanceAbove2` | the determinant (sign factor 1) | `67392 m^6` | 401 | 7 | 8 |
| `DistanceAbove3` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `DistanceAbove3` | `bax`, `bay`, `baz`, `cax`, `cay`, `caz`, `ux`, `uy`, `uz`, `wx`, `wy`, `wz`, `tax`, `tay`, `taz` | `2 m` | 65 | 2 | 2 |
| `DistanceAbove3` | `uy * wz` and 17 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `DistanceAbove3` | `nx`, `ny`, `nz`, `bcx`, `bcy`, `bcz` and 2 more like `(bax * bax) + (bay * bay)` | `8 m^2` | 131 | 3 | 4 |
| `DistanceAbove3` | `nx * tax` and 14 more of its shape | `16 m^3` | 196 | 4 | 5 |
| `DistanceAbove3` | `cnx`, `cny`, `cnz`, `nbx`, `nby`, `nbz` and 1 more like `(nx * tax) + (ny * tay)` | `32 m^3` | 197 | 4 | 5 |
| `DistanceAbove3` | `h` | `48 m^3` | 198 | 4 | 5 |
| `DistanceAbove3` | `hh` | `96 m^3` | 199 | 4 | 5 |
| `DistanceAbove3` | `bb`, `cc` | `12 m^2` | 132 | 3 | 4 |
| `DistanceAbove3` | `bax * cnx` and 2 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `DistanceAbove3` | `(bax * cnx) + (bay * cny)` | `128 m^4` | 263 | 5 | 6 |
| `DistanceAbove3` | `det` | `192 m^4` | 264 | 5 | 6 |
| `DistanceAbove3` | `cnx * bb` and 5 more of its shape | `384 m^5` | 329 | 6 | 7 |
| `DistanceAbove3` | `(cnx * bb) + (nbx * cc)` and 2 more of its shape | `768 m^5` | 330 | 6 | 7 |
| `DistanceAbove3` | `hh * bcx` and 2 more of its shape | `768 m^5` | 330 | 6 | 7 |
| `DistanceAbove3` | `Nx`, `Ny`, `Nz` | `1536 m^5` | 331 | 6 | 7 |
| `DistanceAbove3` | `rd` | `192 m^5` | 328 | 6 | 6 |
| `DistanceAbove3` | `rd2` | `384 m^5` | 329 | 6 | 7 |
| `DistanceAbove3` | `dd` (a sign factor) | `36864 m^8` | 528 | 9 | 10 |
| `DistanceAbove3` | `Nx * Nx` and 2 more of its shape | `2359296 m^10` | 662 | 11 | 12 |
| `DistanceAbove3` | `(Nx * Nx) + (Ny * Ny)` | `4718592 m^10` | 663 | 11 | 12 |
| `DistanceAbove3` | `((Nx * Nx) + (Ny * Ny)) + (Nz * Nz)` | `7077888 m^10` | 663 | 11 | 12 |
| `DistanceAbove3` | `rd2 * rd2` | `147456 m^10` | 658 | 11 | 12 |
| `DistanceAbove3` | the determinant (sign factor 1) | `7225344 m^10` | 663 | 11 | 12 |
| `RadiusEdgeAbove` | every input component | `m = 2^64 - 1` | 64 | 1 | |
| `RadiusEdgeAbove` | `abx`, `aby`, `abz`, `bcx`, `bcy`, `bcz`, `cax`, `cay`, `caz`, `acx`, `acy`, `acz`, `pqx`, `pqy`, `pqz` | `2 m` | 65 | 2 | 2 |
| `RadiusEdgeAbove` | `abx * abx` and 17 more of its shape | `4 m^2` | 130 | 3 | 4 |
| `RadiusEdgeAbove` | `nx`, `ny`, `nz` and 4 more like `(abx * abx) + (aby * aby)` | `8 m^2` | 131 | 3 | 4 |
| `RadiusEdgeAbove` | `lab`, `lbc`, `lca`, `lpq` | `12 m^2` | 132 | 3 | 4 |
| `RadiusEdgeAbove` | `nx * nx` and 2 more of its shape | `64 m^4` | 262 | 5 | 6 |
| `RadiusEdgeAbove` | `(nx * nx) + (ny * ny)` | `128 m^4` | 263 | 5 | 6 |
| `RadiusEdgeAbove` | `nn` | `192 m^4` | 264 | 5 | 6 |
| `RadiusEdgeAbove` | `lab * lbc` | `144 m^4` | 264 | 5 | 6 |
| `RadiusEdgeAbove` | `lca * (lab * lbc)` | `1728 m^6` | 395 | 7 | 8 |
| `RadiusEdgeAbove` | `L` | `1728 m^7` | 459 | 8 | 8 |
| `RadiusEdgeAbove` | `nn * lpq` | `2304 m^6` | 396 | 7 | 8 |
| `RadiusEdgeAbove` | `R1` | `2304 m^7` | 460 | 8 | 8 |
| `RadiusEdgeAbove` | `R2` | `4608 m^7` | 461 | 8 | 9 |
| `RadiusEdgeAbove` | `R4` | `9216 m^7` | 462 | 8 | 9 |
| `RadiusEdgeAbove` | the determinant | `10944 m^7` | 462 | 8 | 9 |
| `BisectorSide1` | scratch of one call (sum over the values; lengths only: 73) | | | 48 | 60 |
| `BisectorSide2` | scratch of one call (sum over the values; lengths only: 364) | | | 175 | 217 |
| `BisectorSide3` | scratch of one call (sum over the values; lengths only: 770) | | | 395 | 479 |
| `DotSites` | scratch of one call (sum over the values; lengths only: 35) | | | 27 | 32 |
| `DetSitesNormal` | scratch of one call (sum over the values; lengths only: 155) | | | 103 | 126 |
| `DistanceAbove1` | scratch of one call (sum over the values; lengths only: 38) | | | 26 | 32 |
| `DistanceAbove2` | scratch of one call (sum over the values; lengths only: 405) | | | 172 | 209 |
| `DistanceAbove3` | scratch of one call (sum over the values; lengths only: 886) | | | 423 | 504 |
| `RadiusEdgeAbove` | scratch of one call (sum over the values; lengths only: 363) | | | 201 | 241 |
<!-- end of the generated table -->

## 3. Symbolic perturbation (SoS) of the site weights

**Definition.** Every site s has an id (its `Delaunay3` vertex id) and an infinitesimal weight w_s. Distances are the power
distances π_s(x) = |x − s|² − w_s. A smaller id has the dominant weight: w_i / w_j is infinite for i < j, and every w is
infinitely small against any nonzero value of the input (for example w_i = ε^(2^i), ε → 0). The power bisector of a and b,
π_a = π_b, is 2 b'·y = |b'|² − (w_b − w_a): it is parallel to the plain bisector. A predicate's perturbed value is its
value on the weighted configuration. Write δ_s = w_s − w_a.

Every value is **linear** in the weights:

- For P3 the perturbed value is f = f0 + δ_e.
- For P4, l_k gains δ_b and d does not change, so N gains δ_b (q'0 − q'1), and f = f0 + 2 δ_b c'·(q'0 − q'1) + δ_c d.
- For P1, the right-hand sides |b'|², |c'|² lose δ_b, δ_c. Then f = f0 − δ_b D(e', c') − δ_c D(b', e') + δ_e D(b', c'),
  with D(X, Y) = det(X, Y, n) and D(b', c') = det.

So the perturbed sign is sign(f0) if f0 ≠ 0. Otherwise it is the sign of the coefficient of the most dominant weight whose
coefficient is not 0, which is the first nonzero coefficient in id order (no product of weights occurs). Expanding the δ's
gives the coefficients per weight. The coefficient of w_a is minus the sum of the others, by translation invariance:

| predicate (value before the factor sign(d) / sign(det)) | w_a | w_b | w_c | w_e |
|---|---|---|---|---|
| P3 `Side1(v; a, e)` | −1 | | | +1 |
| P4 `Side2(q0, q1; a, b; c)` | `DotSites(b, c, q0, q1)` = sign 2 (c − b)·dq | −`DotSites(a, c, q0, q1)` | `DotSites(a, b, q0, q1)` = sign d | |
| P1 `Side3(a, b, c; t; e)` | −`DetSitesNormal(e, b, c; t)` | −`DetSitesNormal(a, e, c; t)` | −`DetSitesNormal(a, b, e; t)` | `DetSitesNormal(a, b, c; t)` = sign det |

Here dq = q1 − q0. The w_a coefficient of P1 is −D(b − e, c − e), because D(b', c') − D(e', c') − D(b', e') = D(b' − e',
c' − e').

**Lemma S1 (the cascade always decides).** If the point exists (d ≠ 0, det ≠ 0), the perturbed sign of P3, P4 and P1 is
never 0.

*Proof.* The coefficient of the tested site's weight (w_e, w_e, w_c) is ±1, ±d or ±det, which is nonzero, so the cascade
stops at it at the latest. ∎

**Lemma S2 (it is a perturbation of the geometry).** The perturbed signs are the signs of the weighted configuration for
all small enough weights with the stated dominance. They are therefore consistent with one another: the same point and
the same bisector always give the same answer, whichever site anchors the formula (BTPI(a, b, c) = BTPI(b, a, c) as
points).

*Proof.* The weighted configuration is a genuine set of weighted points. Each predicate is the sign of a polynomial in
the coordinates and the weights, and for each such polynomial there is a δ0 > 0 below which its sign is the sign of its
leading term in the dominance order. The finitely many polynomials of a computation then have one common δ0. ∎

**Points that do not exist are never constructed.** The clipping constructs a kind-2 point only on a mesh edge whose ends
are strictly on two sides of the power bisector (perturbed P3). If d = 0, the edge is parallel to the bisector: l0 = l1
holds also with the weights (d does not depend on them), so the ends are on the same side. In the same way, a kind-3
point is constructed only on a polygon edge on bisector(a, b) whose ends are on two sides of bisector(a, c). If det = 0,
the two bisector lines in the plane of t are parallel, and the side value is constant along the edge with and without the
weights. So the callers never see the 0 of a point that does not exist.

**Delaunay3.** The triangulation whose neighbours the clipping uses must be the regular triangulation of the same weighted
sites. Otherwise a power facet could be missing from a cell's neighbour list, where ties among cospherical sites are
broken differently.

`ExactBisectorSoS.InSphere` is the power test, the sign of the determinant with the rows (p, |p|² − w_p, 1). It is linear
in the weights and antisymmetric in its rows:

- the coefficient of w_e is Orient3d(a, b, c, d), because a larger weight puts e further inside;
- row swaps give the others: w_a −Orient3d(e, b, c, d), w_b −Orient3d(a, e, c, d), w_c −Orient3d(a, b, e, d), w_d
  −Orient3d(a, b, c, e).

The first nonzero one in id order decides (Edelsbrunner-Mücke; Devillers-Teillaud 2011). The cascade stops at w_e at the
latest for a cell of positive volume, so the test is never 0 there.

`Delaunay3.SymbolicPerturbation` is internal, off by default, and must be set before the first insertion. It uses this
test, with the id the new vertex will get (`VertexCount`), for the conflict region and in `Validate`. Two facts make the
result the regular triangulation:

- **The initial triangulation of the box is already regular for the weighted corners.** Corner 0 has the dominant weight,
  so the regular triangulation is the pulling triangulation from 0: the six tetrahedra around the diagonal 0-7. The faces
  not at 0 are pulled from their lowest corner (1, 2, 4), which gives the diagonals 1-7, 2-7 and 4-7. These are exactly
  `Delaunay3`'s initial cells; a test checks this with the perturbed test.
- **Bowyer-Watson with a test that is never 0 builds the regular triangulation** (the conflict region is star-shaped; for
  a new point in the plane of a boundary facet, both cells of the facet are in conflict or neither is, because the power
  functions of their two orthospheres differ by an affine function that vanishes on that plane). The star-shape growth of
  `Insert` therefore adds nothing.

Round insertion (`PlanInsert`) does not support the perturbation; the ids are assigned later there.

With the perturbation off, `Delaunay3` is unchanged: the same tests on the same paths. A test pins the triangulations of
the existing fixtures bit for bit against master.

## 4. Tests

- `GeneratedPredicatesBisectorTests`:
  - the generated file and the table;
  - the reviewed degrees, gates and bounds, and the bounds at ±m;
  - an oracle with its own formulas (a parameter on the segment, Cramer's rule with replaced columns, BigInteger after
    scaling by 2^-emin) on random inputs of 3-53 bits at exponents −300 to 300, and on wide exponent spans (the
    BigInteger path);
  - exact zeros (sites at equal distance from a lattice point, the distance exactly r, a right triangle at rho² = 1/4) and
    the signs next to them;
  - points that do not exist;
  - subnormal and non-finite inputs, and no allocation on the FixedArith path;
  - nearly degenerate inputs (the filter refuses or is right);
  - the harness similarities (2^k scaling bit for bit);
  - the `--code` class-name argument and `expansion none`.
- `ExactBisectorSoSTests`:
  - every perturbed sign against an oracle that evaluates the weighted construction itself, with explicit rational
    weights w_i = 2^(-E 2^rank(i)) (E above the bit length of every coefficient), at exact zeros and on random inputs;
  - antisymmetry and anchor independence (Lemma S2);
  - the perturbed InSphere against the 5 × 5 lifted determinant with the same weights;
  - `Delaunay3`: the initial cells are regular; cospherical lattices give a regular triangulation (every vertex outside
    every cell's perturbed sphere) in every insertion order (each order gives other ids, so another tie-break);
    off-mode fixtures are pinned against master.

- `tools/Z3Pilot/bisector_sos.py` (Z3, real arithmetic; its source contracts pin the transcribed spec entries and cascades):
  - Cramer's rule and the geometric value of each perturbed side;
  - the linear form in the weights with the cascade's coefficients, which sum to 0;
  - the cascade decides wherever the point exists;
  - anchor independence;
  - crossings strictly inside a clipped edge, and none on a parallel edge;
  - the R2 rewrite (|N|² − r²d² for |y|² − r²) and the circumradius identity behind R3.

  Each of its 21 lemmas has a SAT mutant. The error bounds stay with the generator and the Symbolic Taylor check.

## 5. Sources

Papers only, no code read (the coordinator's decision of 2026-11-27: not Geogram's RVD / PCK code either, although it is
BSD):

- H. Edelsbrunner, E. P. Mücke, *Simulation of Simplicity*, ACM TOG 9(1), 1990.
- O. Devillers, M. Teillaud, *Perturbations for Delaunay and weighted Delaunay 3D triangulations*, CGTA 44(3), 2011.
- B. Lévy, *Robustness and efficiency of geometric programs: the Predicate Construction Kit (PCK)*, CAD 72, 2016.
- D.-M. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, *Isotropic remeshing with fast and exact computation of restricted Voronoi
  diagram*, SGP 2009.
