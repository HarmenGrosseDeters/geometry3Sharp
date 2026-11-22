# A kernel varifold distance as a quality measure of the sharp alpha wrap

Status: **study, 2026-11-22** (owner go 2026-11-22; start condition: the sharp-feature pass with its validity statement,
#1339). No library change, no public API. Harness: `benchmarks/SharpFeatureProbe`, mode `varifold`
([README](../../benchmarks/SharpFeatureProbe/README.md), all tables:
[varifold_tables.md](../../benchmarks/SharpFeatureProbe/results/varifold_tables.md)).

## 1. Question and answer

Does a kernel varifold distance (currents: Vaillant-Glaunès; varifolds: Charon-Trouvé; oriented varifolds:
Kaltenmark-Charlier-Charon) measure the normal and crease fidelity of a wrap better than the Hausdorff distance or the
area ratio?

**Answer: yes against a sharp reference at the wrap's offset, no against the input itself.**

- Against the mitered offset of a clean solid (the oracle O of the
  [sharp-wrap study](PROVABLE_SHARP_WRAP_STUDY.md)) the oriented varifold with a narrow normal kernel separates the
  feature pass (K) from the plain wrap (A) on 16 of 16 clean CAD-like cases, by a median factor 38 (3.2 to 81) at
  sigma = 10 offsets. The Hausdorff distance does not (median 1.35, above 2 on 1 of 16). The area defect
  |area ratio - 1| separates too (median 35) but is signed, cancels, and reads a result with 8 % of its creases
  missing as the best of the set (section 6.3).
- It also beats the two area-weighted measures the probe already has where the reference is curved: on the filleted
  block and the filleted slot the mean distance (Chamfer) separates A from K by 1.4 and 2.0 and the mean normal angle
  by 1.6 and 2.2, the varifold by 4.5 and 6.3 (12 and 13 at sigma = 40).
- It has a closed-form calibration: for damage narrower than sigma the squared distance is `sqrt(pi) sigma L c w^2`
  (crease length L, damage width w, a constant c of the damage's shape; section 4). So the result can be reported as
  an **equivalent chamfer width**: A 0.8 to 3.4 offsets, K 0.16 to 0.5 offsets on 14 of 16 cases (0.8 and 1.9 on the
  two V-notches), the quadrature floor about 0.1.
- Against the input (the only reference a dirty input has) it is useless as it stands: the wrap is one offset away
  from the input everywhere, which costs `1 - exp(-offset^2 / sigma^2)` (0.01 at sigma = 10 offsets) on the whole
  area, 7 to 140 times the plain wrap's whole crease signal. The median of A / K is 0.99 to 1.14 there, and no case
  reaches 2 with any kernel. Of the measures tried only the mean normal
  angle survives that reference (median 19 at a sampling spacing of 40 offsets; on the two cases also sampled at 5
  offsets it falls from 26 and 19 to 2.8 and 2.2, so it depends on the sampling).
- Currents (the linear normal kernel) are the wrong tool: a chamfer narrower than sigma does not change a current at
  all (c = 0; measured at the noise floor up to w = sigma / 10, and 60 times below the oriented kernel at sigma / 5).
- Cost of a fresh pair (median): 5.3 s at sigma = 10 offsets (1.3 and 1.0 million quadrature points), 1.5 s at 20,
  0.5 s at 40, against 4.9 s for the wrap pipeline and 26 ms for the point-to-mesh measures.

**Recommendation.** Keep it as a benchmark measure: the oriented varifold, normal kernel `((1 + u) / 2)^8`,
sigma = 20 offsets (2 alpha at the pipeline's 300 / 3000; 10 offsets, used in the tables below, separates the same
and costs 3.6 times the time), reference O, reported as the equivalent chamfer width.
**No internal library class**: its only user is this probe, and it needs a reference that only the probe has
(section 8). Nothing public is proposed.

## 2. Checks before the build

**Library.** No kernel-sum, current or varifold code exists (`grep -ri varifold|currents`: only the survey
[UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md), which lists varifolds as a candidate for
crease *detection*, a different use). Building blocks looked at: `MeshFidelityMetrics` (sampled Hausdorff, edge
Chamfer and F-score: point-to-mesh measures, used here as the comparison); `DMeshAABBTree3` nearest-triangle queries
(used for those); the tree sums of `FastWindingNumber` (a far-field expansion of a 1 / r^2 kernel; a Gaussian with a
cut-off has no far field worth expanding, a cell grid is enough); `PointHashGrid3d` (a dictionary of lists per cell:
the sorted-cell layout written here is the same idea without the per-point allocations); the probe's own procedural
solids, oracle and variants (reused as they are).

**Literature** (papers only, no code read):

- M. Vaillant, J. Glaunès, *Surface matching via currents*, IPMI 2005: surfaces as vector-valued measures, the kernel
  norm as a distance without point correspondences; opposite orientations cancel.
- N. Charon, A. Trouvé, *The varifold representation of nonoriented shapes for diffeomorphic registration*, SIAM J.
  Imaging Sci. 6(4), 2013 (arXiv 1304.6108): measures on position x unoriented normal, kernel `k_p(x - y) (n . m)^2`;
  no cancellation, consistent with the area.
- I. Kaltenmark, B. Charlier, N. Charon, *A general framework for curve and surface comparison and registration with
  oriented varifolds*, CVPR 2017: position x oriented normal with any kernel on the sphere; currents and varifolds are
  the linear and the squared case.
- H.-W. Hsieh, N. Charon, *Metrics, quantization and registration in varifold spaces*, Found. Comput. Math. 21, 2021:
  which kernels give a distance; discretisation.
- P. Roussillon, J. Glaunès, *Representation of surfaces with normal cycles and application to surface registration*,
  J. Math. Imaging Vis. 61, 2019; N. Charon, B. Charlier, J. Glaunès, P. Gori, P. Roussillon, *Fidelity metrics between
  curves and surfaces: currents, varifolds, and normal cycles* (book chapter, 2020): normal cycles add curvature (for
  a triangle mesh: terms on edges weighted by the dihedral angle). The closest thing to a "crease measure" in this
  family; not built here (three kernel terms on faces, edges and vertices; the edge term alone would be the next step
  if a crease-only number is wanted).
- How the field uses them: as the data term of registration and as an evaluation metric of surface reconstruction
  where meshes have no correspondence (eg BaRe-ESA, arXiv 2211.13185; Hartman et al., arXiv 2006.11652), usually
  `exp(-|x - y|^2 / sigma^2) (n . m)^2` with one hand-set sigma, summed over all face pairs on a GPU (KeOps). Nothing
  found on varifolds for sharp-feature or offset-surface quality (searched: "varifold distance mesh reconstruction
  metric sharp features", "varifold chamfer normal consistency", "normal cycles sharp edges").

**Upper bound of the cost.** N = c A / sigma^2 quadrature points per mesh and pi Cut^2 c neighbours each. First
estimate (c = 2, Cut = 3): 0.3 million points and 5e7 kernel evaluations at sigma = 10 offsets. Measured: c = 7.5 with
the degree-2 rule the accuracy needs (section 5), Cut = 3.5: 1.0 to 1.3 million points, about 600 candidates and 290
partners each, about 2e9 candidate pairs for the three inner products, 5.3 s on 4 cores. The estimate was low by a factor 10
in pairs, and the conclusion moves with it: as costly as the wrap at sigma = 10 offsets, cheap from 20 (1.5 s), and
17 to 20 s at 5.

## 3. Method

A triangle mesh S is the measure `sum_f a_f delta_(c_f, n_f)` on position x unit normal. With a Gaussian in space
and a kernel k on the normals' dot product,

```
<S, T> = sum_f sum_g a_f a_g exp(-|c_f - c_g|^2 / sigma^2) k(n_f . n_g),      d^2(S, T) = <S, S> - 2 <S, T> + <T, T>
```

Normal kernels measured (one pass computes all five):

| name | k(u) | what it is |
|---|---|---|
| mass | 1 | the area measure: no normals |
| current | u | Vaillant-Glaunès; opposite normals cancel |
| varifold | u^2 | Charon-Trouvé (Binet); orientation-blind |
| oriented2 | ((1 + u) / 2)^2 | oriented, wide: 0.73 at 45 degrees, 0.25 at 90 |
| oriented8 | ((1 + u) / 2)^8 | oriented, narrow: 0.28 at 45 degrees, 0.004 at 90, 0 for opposite normals |

All are positive semi-definite on the sphere (non-negative Legendre coefficients), so each d is a pseudo-distance:
symmetric, with the triangle inequality, but d = 0 does not imply equal surfaces (a polynomial kernel sees only the
normal moments up to its degree: the mass ignores normals, u^2 cannot tell n from -n; a true distance needs a kernel
with all degrees, Hsieh-Charon). For a quality measure against a fixed reference that is enough.

**Normalisation.** `rel = d^2 / (<S,S> + <T,T>)`: dimensionless, 0 for equal surfaces, 1 for surfaces farther apart
than the cut-off (for the kernels that cannot cancel). With sigma given in offsets and the offset a fixed fraction
of the diagonal (1 / 3000), `rel` does not change under a similarity of both meshes.

**Quadrature.** Each triangle is cut into k^2 congruent triangles, k = ceil(longest edge / sigma), each with the
edge-midpoint rule (exact for degree 2; midpoints inside the triangle are shared, 3 k (k + 1) / 2 points). A sliver
(longest^2 > 8 area) longer than sigma is first halved at its longest edge, otherwise it gets (longest / sigma)^2
points whatever its area (a chamfer strip 1 offset wide: 6000 points per sigma^2, and the sum is quadratic in that).
The points follow the triangles, so a rigid motion changes them only by rounding. The sum runs over pairs within
3.5 sigma (exp(-12.25) = 4.8e-6 of a flat sheet's sum is dropped on both sides alike), found through a grid of cells of
half that size with the points sorted by cell. Rows are computed in parallel and added in the sorted order: the result
does not depend on the threads.

## 4. Scale behaviour: what sigma does

Three closed forms explain every number below (L x L squares and the prism sweeps confirm them, section 5).

1. **Position error** (a sheet moved by x along its normal, the two-parallel-planes case):
   `rel = 1 - exp(-x^2 / sigma^2)`, about x^2 / sigma^2, for every kernel with k(1) = 1. Measured on the prism's own
   offset by 0.25 / 0.5 / 1 / 2 offsets at sigma = 10: 6.3e-4 / 2.52e-3 / 1.00e-2 / 3.96e-2 (formula: 6.2e-4 / 2.50e-3
   / 0.995e-2 / 3.92e-2; the rest is the miter's extra area).
2. **Crease damage narrower than sigma** (a strip of width about w along a crease of length L replaced by another):
   `d^2 = sqrt(pi) sigma L Q`, where Q is the kernel's quadratic form on the transverse mass defect. For a chamfer of
   a 90 degree crease, w wide on both faces: `Q = c w^2` with `c = 4 k(1) + 2 k(0) - 4 sqrt(2) k(cos 45)`:

   | kernel | mass | current | varifold | oriented2 | oriented8 |
   |---|---:|---:|---:|---:|---:|
   | c | 0.343 | **0** | 1.172 | 0.378 | 2.414 |

   So `rel = c L w^2 / (2 sqrt(pi) sigma A)`: quadratic in the width, linear in the damaged length, falling as
   1 / sigma. Measured on the L-prism (6 creases, w = 1 offset, sigma = 10): mass 3.46e-6, varifold 1.24e-5, oriented8
   2.56e-5; the formula gives 3.7e-6, 1.25e-5, 2.58e-5. The current sees nothing (-1.9e-7, the noise floor): the
   integral of n dA over a patch depends only on the patch's border, so a current cannot tell a chamfer or a fillet
   from the sharp crease until the damage is as wide as sigma. That rules currents out for this use.
3. **Damage wider than sigma** counts by area: `d^2 = pi sigma^2 x (unmatched area)`, linear in w. In the bevel sweep
   at sigma = 10 the factor per doubling of w is 4.0 up to w = 4, 3.9 at 8 and 3.5 at 16 offsets: the turn starts at
   w = sigma and is slow.

Consequences for the choice of sigma, in offsets (d = offset, a = alpha = 10 d):

- **Signal against position noise grows with sigma**: (2) / (1) = `c L sigma w^2 / (2 sqrt(pi) A x^2)`. A wrap's flat
  parts must be matched in position far better than its creases are damaged. They are: K's whole distance on the box
  is 2.3e-6, 3.5 times the quadrature floor, which by (1) bounds the RMS position error of its flat parts at
  0.015 offsets.
- **The plain wrap's damage is 1 offset wide at convex creases and up to alpha wide at concave ones.** sigma = alpha
  keeps both in the quadratic regime (2), where the width has a meaning; a smaller sigma buys nothing (same
  separation at sigma = 5, 3 to 4 times the time) and a larger one loses 1 / sigma of signal against a constant floor
  but is cheaper and separates the curved cases better (fillet 4.5 -> 12, fslot 6.3 -> 13 at sigma = 40, because the
  faceted fillet's own triangulation noise, one facet = 2.4 alpha, averages out).
- **Equivalent chamfer width** `w_eq = sqrt(d^2 / (sqrt(pi) sigma c L))` with L the length of the reference's sharp
  edges and c of oriented8: the width of a 45 degree chamfer of every sharp edge that would give the same distance. It
  is nearly independent of sigma (lshape A: 1.69 / 1.63 offsets at sigma = 10 / 40), which is the check that the law
  (2) describes the wrap's damage.

**Recommended:** oriented8, reference O, sigma = 20 offsets for routine use (the separation of sigma = 10 at 0.28
times the cost); sigma = 10 offsets (= alpha) is what the tables of section 6.1 use.

## 5. Checks of the measure itself

| check | result |
|---|---|
| closed form: two parallel L x L squares at distance g (one with 2 triangles, one an irregular 7 x 7 grid), `<S,T> = I(L, sigma)^2 exp(-g^2 / sigma^2) k(+-1)`, I by Simpson's rule; L = 10 sigma, g = 0 to 2 sigma, both orientations | largest relative error 9.5e-4 / 4.8e-5 / 3.4e-6 at spacing 2 sigma / sigma / sigma / 2 (order 4 down to the cut-off's 3e-6). The centroid rule tried first: 5.5e-3 at spacing sigma, order 2 |
| convergence of `rel` on a damaged prism (bevel 1 offset, sigma = 20) | spacing 2 sigma: 1.8e-3 (useless); 1.4 sigma: 7.6e-5; sigma: 1.27e-5; sigma / 2: 1.29e-5; sigma / 3: 1.30e-5. Spacing sigma is the default |
| floor: O against O with every triangle split at a random inner point (17 cases, 3 sigmas) | oriented8 `rel` -2.6e-7 to 1.6e-6 (2 of 51 negative): about 0.1 offsets of w_eq |
| scale by 2^10 and 2^-10 (both meshes, sigma with them) | `rel` identical bit for bit |
| scale 1e-3, a rotation, a translation by 6e6 (UTM) or 1e6 sizes | relative change of `rel` 1.4e-8, 1.0e-9, 2.8e-9, 5.6e-10 |
| rotation + scale 1e-3 + UTM translation (coordinates 6e6, mesh size 0.015) | 5.7e-6: the input's own rounding (2e-4 offsets per coordinate), not the measure |
| threads, repeated runs | rows added in a fixed order: the inner products of 2 floor cases at 2 sigmas are bit-identical between 4 threads and `DOTNET_PROCESSOR_COUNT=1`, and to the batch run before |

## 6. Numbers: the plain wrap against the feature pass

16 clean cases plus one soup, alpha = diagonal / 300, offset = diagonal / 3000: the probe's solids (box, L-shape,
octant, slot, V-notch, chamfered block), a block with filleted edges, a slot with filleted bottom edges (`fslot`), thin
walls 0.5 and 2 alphas thick (`fin:`), a gear, a notch 4 alphas wide, a 60 degree wedge and ridge, two posed cases.
A = wrap + simplification (the pipeline); K = wrap + `AlphaWrapFeaturePass` + simplification. All 51 results are
clean with 0 exact crossings of the input (the probe's own check).

### 6.1 Before and after (reference O, oriented8, sigma = 10 offsets)

| case | crease coverage 0.25 A -> K | `rel` A | `rel` K | A / K | w_eq A -> K (offsets) | Hausdorff A -> K (offsets) | area ratio A -> K |
|---|---|---:|---:|---:|---|---|---|
| box | 0 -> 0.953 | 8.65e-5 | 2.27e-6 | 38 | 0.98 -> 0.16 | 0.78 -> 0.63 | 0.99903 -> 0.99997 |
| lshape | 0 -> 0.962 | 3.22e-4 | 4.72e-6 | 68 | 1.69 -> 0.21 | 5.08 -> 4.02 | 0.99868 -> 0.99999 |
| octant | 0 -> 0.940 | 5.12e-4 | 6.36e-6 | 81 | 2.27 -> 0.25 | 5.03 -> 4.12 | 0.99865 -> 0.99997 |
| slot | 0 -> 0.959 | 1.09e-3 | 1.41e-5 | 77 | 2.86 -> 0.33 | 4.95 -> 3.95 | 0.99788 -> 1.00001 |
| vnotch | 0 -> 0.928 | 1.28e-3 | 3.94e-4 | 3.2 | 3.41 -> 1.90 | 15.4 -> 7.02 | 0.99740 -> 1.00027 |
| chamfer (45 degree creases) | 0.19 -> 0.951 | 7.19e-5 | 2.96e-6 | 24 | 0.80 -> 0.16 | 0.86 -> 0.59 | 0.99918 -> 0.99997 |
| fillet (rounded edges) | 0 -> 0.963 | 7.74e-5 | 1.71e-5 | 4.5 | 1.05 -> 0.49 | 1.16 -> 0.80 | 0.99922 -> 0.99994 |
| fslot (filleted slot) | 0 -> 0.953 | 1.12e-4 | 1.77e-5 | 6.3 | 1.03 -> 0.41 | 0.89 -> 0.89 | 0.99886 -> 0.99993 |
| fin:0.5 (thin wall, 5 offsets) | 0 -> 0.875 | 8.02e-4 | 1.34e-5 | 60 | 2.36 -> 0.31 | 5.18 -> 3.61 | 0.99794 -> 0.99987 |
| fin:2 | 0 -> 0.950 | 7.16e-4 | 1.15e-5 | 62 | 2.23 -> 0.28 | 4.84 -> 3.84 | 0.99809 -> 0.99998 |
| gear | 0 -> 0.920 | 1.38e-3 | 3.73e-5 | 37 | 2.18 -> 0.36 | 3.74 -> 3.31 | 0.99685 -> 1.00001 |
| notch:4 | 0 -> 0.954 | 5.08e-4 | 8.17e-6 | 62 | 2.15 -> 0.27 | 6.33 -> 3.90 | 0.99858 -> 0.99998 |
| wedge:60 | 0.02 -> 0.945 | 6.35e-4 | 7.03e-5 | 9.0 | 2.43 -> 0.81 | 8.46 -> 5.20 | 0.99834 -> 1.00003 |
| ridge:60 | 0 -> 0.888 | 1.24e-4 | 6.39e-6 | 19 | 1.12 -> 0.26 | 0.94 -> 0.85 | 0.99865 -> 0.99989 |
| lshape@rot | 0 -> 0.947 | 3.87e-4 | 7.90e-6 | 49 | 1.58 -> 0.23 | 4.67 -> 2.76 | 0.99818 -> 0.99995 |
| chamfer@rotfar | 0.18 -> 0.951 | 9.11e-5 | 3.48e-6 | 26 | 0.80 -> 0.16 | 0.81 -> 0.52 | 0.99895 -> 0.99996 |
| soup:lshape (the pass finds no crease) | 0 -> 0 | 3.22e-4 | 3.22e-4 | 1 | 1.69 -> 1.69 | 5.08 -> 5.08 | 0.99868 -> 0.99868 |

The raw wrap R equals A in this measure (median `rel` 4.47e-4 against 4.48e-4; the R rows are in the raw output, not
in the committed tables): the simplification neither damages nor repairs creases. Reading w_eq: the plain wrap is as far from the sharp offset as a chamfer 1 offset wide on convex
shapes (box, chamfer, ridge: the offset surface's rounding) and 1.6 to 3.4 offsets wide where concave creases are
bridged; after the pass 0.16 to 0.5 offsets remain, except at the two V-notches (0.8, 1.9: the sharp-wrap study's
unresolved crease ends at concave corners, its "fh max" tail).

### 6.2 Separation of A from K by measure (16 clean cases, paired ratio A / K)

| measure | reference | median | min | max | cases above 2 |
|---|---|---:|---:|---:|---:|
| oriented8, sigma = 10 | O | 37.5 | 3.24 | 80.5 | 16 / 16 |
| oriented8, sigma = 20 | O | 38.6 | 4.01 | 103 | 16 / 16 |
| oriented8, sigma = 40 | O | 41.0 | 4.73 | 173 | 16 / 16 |
| varifold (u^2), sigma = 10 | O | 32.0 | 2.72 | 62.3 | 16 / 16 |
| mass, sigma = 10 | O | 25.1 | 4.20 | 60.7 | 16 / 16 |
| Hausdorff (sampled, both ways) | O | 1.35 | 1.00 | 2.19 | 1 / 16 |
| Chamfer (area-weighted mean distance, both ways) | O | 32.5 | 1.43 | 73.5 | 14 / 16 |
| mean normal angle to the nearest triangle | O | 23.1 | 1.56 | 42.4 | 15 / 16 |
| area defect, \|area ratio - 1\| | O | 34.8 | 9.64 | 390 | 16 / 16 |
| every varifold kernel, sigma = 10 to 40 | I | 0.99 to 1.14 | 0.95 | 1.70 | 0 / 16 |
| Hausdorff | I | 1.18 | 0.62 | 2.38 | 1 / 16 |
| Chamfer | I | 1.00 | 1.00 | 1.01 | 0 / 16 |
| mean normal angle (sampling spacing 40 offsets, see section 1) | I | 19.4 | 1.53 | 34.6 | 15 / 16 |
| area defect | I | 0.52 | 0.004 | 0.71 | 0 / 16 (K is "worse") |

### 6.3 What each measure gets wrong

- **Hausdorff** (here a sampled lower bound) is the worst point: one unresolved crease end sets it (lshape 5.08 -> 4.02 with 96 % of the crease
  length recovered), it does not know how much is damaged (`count` sweep: 2.0 offsets for 1 to 6 damaged creases), and
  as a sampled value it depends on the sampling (lshape A: 5.08 at spacing 40 offsets, 7.91 at 5).
- **Area ratio** is one signed number: rounding a crease loses area, a wrinkle or an overshoot gains it. slot and gear
  K read 1.00001 (a defect of 1.4e-5 and 0.8e-5, smaller than the box's 3.2e-5) with 4 and 8 % of the crease length
  not recovered and w_eq 0.33 and 0.36, twice the box's; vnotch K reads 1.00027 (an excess) while it is the worst
  result of the set. Against the input the defect is larger for K than for A (the
  mitered offset has more area than the input's rounded offset), so the better result scores worse.
- **Chamfer and the mean normal angle** count the same things as the varifold on planar shapes (for crease damage the
  mean distance is also proportional to w^2 L / A) and are 200 times cheaper. They lose where the nearest-point map is
  not the right correspondence: on the faceted fillets (1.4 to 2.2 against 4.5 to 6.3), and Chamfer on the rotated
  cases (5.4 and 3.5 against 49 and 26). They have no closed-form calibration, and the normal angle is undefined at
  points equidistant from two faces (the harness samples it at centroids for that reason).
- **The varifold** needs the sharp reference at the same offset (section 1) and costs seconds. Its kernel must not be
  the current's (section 4), and the wide kernels (mass, oriented2) give 1.5 to 2.5 times less separation than the
  narrow one (3 times at sigma = 40) because their c is 7 times smaller while the floor is the same.

## 7. Cost

One process, 4 cores; a fresh pair: the quadrature of both meshes and the three inner products (the 17 floor pairs, O
against its re-triangulated copy; sigma = 5: 2 pairs):

| sigma (offsets) | points, result / reference (median) | ms median | ms min | ms max |
|---:|---:|---:|---:|---:|
| 5 | 4.5 M / 3.8 M | 18 500 | 16 900 | 20 100 |
| 10 | 1.32 M / 0.98 M | 5342 | 2630 | 41 674 |
| 20 | 0.36 M / 0.28 M | 1481 | 896 | 17 285 |
| 40 | 0.11 M / 0.07 M | 516 | 260 | 7108 |

The wrap pipeline (A) takes 4893 ms (median) and the point-to-mesh measures 26 ms per pair. The maxima are the two
filleted solids (2.5 to 3 million points at sigma = 10): their facets are narrow strips 4 units long, and a strip's
subdivision follows its longest edge after the sliver rule, so they get more points than their area asks for. In a
series against one reference its self product is computed once. The time is quadratic in
1 / sigma, and the point count is set by the area in units of sigma^2, not by the triangle count. Stratum: the 17
procedural cases; wall time including the sampling and the sorting, excluding the wrap.

## 8. An internal class? No

The brief allows an internal metric class "only if it earns its place". It does not:

- its only caller would be this probe (no pipeline step, test or gate consumes it);
- it needs the mitered offset of a clean reference, which exists only in the probe (`Bound.MiteredOffset`); against
  what the library has at hand, the input, it does not work;
- at 0.5 to 5 s per pair it cannot be a test oracle under the 5 s rule except on small inputs, where the existing
  crease-coverage checks of `AlphaWrapFeaturePassTests` are sharper.

So the code stays in `benchmarks/SharpFeatureProbe/VarifoldMeasure.cs` with its self test (`varifold ... selftest`:
the closed form, exit code 1 above 1e-4). If a later consumer appears (a crease-quality column in `bound`, or the
mitered offset as an operation, option 3 of the sharp-wrap study, which would bring its own reference), the class moves
to `mesh_ops` with the closed-form, invariance and determinism tests the checks of section 5 already describe.

## 9. Open ends (not started)

- A reference for dirty inputs. Moving each input triangle along its normal by one offset removes the position term
  but leaves gaps at convex and overlaps at concave creases of about one offset: by the law of section 4 that is a
  bias of w_eq about 0.9, the size of the plain wrap's own convex damage, so A and K would not be ranked reliably. Not
  measured.
- The edge term of normal cycles (dihedral-angle weighted segments) as a crease-only measure: it would drop the
  flat-part mass entirely and may work against the input. A separate spike.
- A per-crease map: the residual field `<S - T, delta_(x, n)>` evaluated on the reference's creases localises the
  damage (the a-posteriori recovery report of the sharp-wrap study, option 2, is the simpler route to the same).

## 10. Reproduce

```
dotnet build benchmarks/SharpFeatureProbe -c Release
P="dotnet run --no-build -c Release --project benchmarks/SharpFeatureProbe -- varifold"
$P self.jsonl selftest
$P damage.jsonl bevel:0.25,0.5,1,2,4,8,16 count:4 shift:0.25,0.5,1,2 -- sigmas=10,20,40
$P cases.jsonl lshape chamfer box fslot fin:0.5 fin:2 slot vnotch octant fillet gear notch:4 wedge:60 ridge:60 lshape@rot chamfer@rotfar soup:lshape -- sigmas=10,20,40
$P floor.jsonl floor:lshape floor:chamfer ... floor:soup:lshape -- sigmas=10,20,40
$P pose.jsonl pose:lshape -- sigmas=10,20
$P s5.jsonl lshape chamfer -- sigmas=5
python3 benchmarks/SharpFeatureProbe/varifold_tables.py damage.jsonl cases.jsonl s5.jsonl floor.jsonl pose.jsonl
```

Master 57623890 plus the harness, 4-core container, about 50 minutes; raw rows not committed, the tables are in
[varifold_tables.md](../../benchmarks/SharpFeatureProbe/results/varifold_tables.md).
