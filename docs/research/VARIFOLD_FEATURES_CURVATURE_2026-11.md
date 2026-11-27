# Varifolds for crease detection and curvature on broken inputs

Status: **study, 2026-11-24** (owner go 2026-11-24). Candidate #1 of
[UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) promised two things that
[VARIFOLD_WRAP_QUALITY_2026-11.md](VARIFOLD_WRAP_QUALITY_2026-11.md) did not test: (1) crease detection on broken inputs
without repairing connectivity first, (2) curvature without connectivity on soups and point clouds. No library change, no
public API. Harness: `benchmarks/SharpFeatureProbe`, mode `soup`
([README](../../benchmarks/SharpFeatureProbe/README.md#mode-soup-creases-and-curvature-without-connectivity-2026-11-24)); every table below
is in [results/soup/tables.md](../../benchmarks/SharpFeatureProbe/results/soup/tables.md), from the raw rows next to it, made by
`benchmarks/SharpFeatureProbe/run_soup.sh` (4-core container, net10.0 Release).

## 1. Verdict

| question | answer |
|---|---|
| (1) creases on broken soups | **go: a clear win.** The varifold two-plane test keeps F1 0.998-1.000 (median of 4 shapes) on unwelded, T-junction, jittered, holed, duplicated, flipped and mixed soups up to `mix:0.5`, where the welded dihedral falls to 0.56 (T-junctions), 0.08 (20 % flipped triangles) and 0.03 (`mix:0.5`). The dihedral error is 0.3-1.5 degrees against 0.7-5.5 for the weld. 2.1 s at 143k triangles (100k clean, broken). It loses to the weld only on a clean or only-jittered input (equal F1 there), and both fail on gaps as wide as a triangle (`gap:1`: 0.37 against 0.08). |
| (2) curvature without connectivity | **no-go for the varifold estimator.** It beats `MeshPrincipalCurvature` after a weld on every broken soup (eg `mix:1`, fillet cylinders: e_K 0.036 against 0.31), but the classic 2-jet fit (Cazals-Pouget) on the same points is better or equal in every row (0.017 there), on clean soups and on point clouds without noise (0.02 against 0.05); on noisy clouds the two tie at their best radius. The regularised first variation (BLM mean curvature) is unusable on broken soups and random point sets (e_H 0.5-3.6 on flats). |

So the varifold view pays for crease detection (a measure on position x unoriented plane needs no edges, no orientation,
no weld), and does not pay for curvature (a least-squares fit of the same points does better). Section 7 lists the next
candidates with their bounds; the stop of (2) ends the varifold curvature design, not the search: the jet fit on the soup's
quadrature clears the threshold against the library's baseline by a factor 2-18 on broken soups.

## 2. Checks before the build

**Library.** No connectivity-free crease or curvature code exists:

- `AlphaWrapFeaturePass` (the sharp-feature pass of the wrap) takes its input creases from the dihedral angles of the input's
  own edges and skips boundary edges (`mesh_ops/AlphaWrapFeaturePass.cs`, `build_miters`, `et.b == DMesh3.InvalidID`): on an
  unwelded soup every edge is a boundary edge, so the pass finds no crease at all. This is the main consumer the candidate
  named (HP 4 in [OPEN_TOPICS.md](../OPEN_TOPICS.md)).
- `MeshPrincipalCurvature` (Rusinkiewicz 2004) needs one-rings: on a soup every vertex has one triangle and gets K = 0
  (measured: e_K = 1.000 on every curved query of an unwelded jittered soup, with `Valid` = true).
- `MeshFidelityMetrics` sharp edges (dihedral > 30 degrees), `EdgeClass` of this probe: welded dihedral, the baseline here.
- `PointCloudNormals` (PCA plus an alpha-wrap orientation) and `PointCloudSurface`: normals and a surface, no curvature; the
  probe uses its own kernel PCA for unoriented normals (orientation is not needed).
- Reused: `VarifoldMeasure.Sample` (the edge-midpoint quadrature of the earlier study) for the soup's measure; the cell-sorted
  neighbour layout of `VarifoldMeasure.Inner` (rewritten as `SortedGrid`, a query per point instead of a pair sum).

**Literature** (papers only, no code read; rows in [sources/meshing-and-repair.md](../sources/meshing-and-repair.md)):

- B. Buet, G. P. Leonardi, S. Masnou, *A varifold approach to surface approximation*, ARMA 226 (2017): the regularised first
  variation `H_e(x) = -(dV * rho'_e)(x) / (|V| * xi_e)(x)` of a discrete varifold as a convergent mean curvature when the
  kernel radius dominates the spacing.
- B. Buet, G. P. Leonardi, S. Masnou, *Weak and approximate curvatures of a measure: a varifold perspective*, Nonlinear
  Analysis 222 (2022), arXiv:1904.05930: Hutchinson's generalised second fundamental form for any varifold, approximate
  curvature tensors, the kernel pair `xi(r) = -r rho'(r) / d`, point clouds.
- B. Buet, M. Rumpf, *Mean curvature motion of point cloud varifolds*, ESAIM M2AN 56 (2022): point clouds with PCA tangents;
  consistency "provided ... a regular sampling" (section 6.3 shows what happens without it).
- J. E. Hutchinson, Indiana Univ. Math. J. 35 (1986): curvature as the weak derivative of the tangent projector.
- Prior art of the crease test: C. Weber, S. Hahmann, H. Hagen, *Sharp feature detection in point clouds*, SMI 2010 (Gauss
  map clustering of a neighbourhood). The two-plane test here is close to it; what the varifold adds is the mass weighting
  (duplicates, holes and non-uniform triangles are just mass), the unoriented plane (flips do not matter) and the condition
  e >> h under which the theory converges. Flagfolds (Buet, Pennec 2023) were not built: the two-plane split is the
  one-dimensional stratum they formalise.
- Baselines: M. Pauly, M. Gross, L. Kobbelt, IEEE Vis 2002 (surface variation of the position covariance); F. Cazals,
  M. Pouget, CAGD 22 (2005) (2-jet fit).

**Upper bound first.** Before building the varifold test, the baseline on the broken inputs fixes the room: the best of three
weld tolerances reaches F1 0.56 on T-junction seams, 0.37 on gaps of 0.3 h, 0.08 on 20 % flips and 0.03 on `mix:0.5`
(table 4.1), against an ideal 1.0. The cost bound: 3 edges per triangle, about 200 quadrature points per ball at e = 2 h,
so 6e7 point visits at 100k triangles (measured 2.1 s at 143k, section 5).

## 3. Method

**Inputs** (`SoupShapes.cs`, `SoupBreak.cs`; procedural only). Five shapes on X = 2, Y = 1.5, Z = 1, each a set of
parametric patches with an analytic outward normal and principal curvatures, each patch on its own grid of spacing h =
0.25 / m (all straight borders conform): `box` (12 creases of 90 degrees), `slot` (a 0.5 x 0.5 channel: convex and concave
creases), `vnotch` (a V groove: creases of 63 and 127 degrees, anisotropic 2.2 : 1 notch faces), `fillet` (vertical edges
rounded, R = 0.25: creases along lines and arcs, quarter cylinders with K = (4, 0)), `rbox` (all edges rounded: no crease,
cylinders (4, 0) and sphere octants (4, 4)). Ground truth: a patch border whose two analytic normals differ by more than 5
degrees is a crease (with that dihedral); a soup edge is a true crease edge when both ends and the midpoint of its clean copy
lie within 0.05 h of one. Breakages, one at a time and mixed:

| name | what |
|---|---|
| `soup` | unwelded: every triangle its own three vertices |
| `nonconf` | patch grids at 1, 1.37, 0.79, 1.18 times h: T-junctions on every seam |
| `jitter:a` | every corner of every triangle moved by up to a h, independently (the copies of a vertex part) |
| `holes:p`, `dup:p`, `flip:p` | a fraction p of the triangles dropped / duplicated (half reversed) / reversed |
| `gap:g` | every patch moved by g h in its own random direction (CAD gaps and overlaps between faces) |
| `mix:s` | all at level s: nonconf, gap 0.3 s, jitter 0.05 s, holes 0.05 s, dup 0.1 s, flip 0.2 s |

**Varifold.** The soup's measure: the quadrature points of `VarifoldMeasure.Sample` at spacing e / 2, each with its mass and
its triangle's unoriented plane (P = I - n n^T, the sign of n ignored); kernel `rho(r) = (1 - r^2 / e^2)^4`. All lengths in
units of the soup's own median edge length h (the method never sees the generator's h).

**Crease test** at a soup edge (p, q) of a triangle with normal n_t, ball of radius e = c h around its midpoint: plane A is
the mass whose plane is within 20 degrees of n_t's, plane B the rest, re-split around its main plane (near a corner the rest
holds a third plane); each needs 20 % of the mass. Each plane: mass centroid and main eigenvector of `sum m n n^T`. Their
line L; the dihedral is 180 degrees minus the angle between the two half-planes (from L towards each centroid), so it needs
no orientation. Crease: dihedral >= 30 degrees, both ends within `0.3 h + 3 rms` of L (rms: the planes' residuals), and the
edge within 30 degrees of L.

**Curvature** at a query point x (radius e): the tangent frame from the kernel mean of the planes; the projector derivative
`d_l P ~ sum m rho'(r)/r (y - x)_l (P_y - P_x) / ((1/2) sum m rho'(r) r)` (the BLM kernel pair; exact for a linear P on a
plane), its least-squares variant (`varifoldLS`: (P_y - P_x) n regressed on the tangent coordinates of y - x), the shape
operator `S_ab = -e_b . (D_a P) n`, |K1| >= |K2| its eigenvalues by magnitude. The BLM mean curvature `|H|` beside it. Error
`e_K = max(|k1 - K1|, |k2 - K2|) R` (units of 1 / R, R = 0.25), at up to 3000 soup corners farther than e + h from any crease
or curvature jump.

**Baselines.** Creases: the welded dihedral (vertex weld within 1e-6 h, 0.1 h, 0.3 h, the best of the three reported; a
triangle that would make an edge non-manifold is dropped) and the surface variation lambda_3 / trace of the kernel position
covariance (Pauly et al.), thresholded at its value on an ideal 30 degree crease. Curvature: `MeshPrincipalCurvature` (0 and 2
smoothing rounds) after the same three welds; the 2-jet fit in the PCA frame of the same quadrature points and radius.

## 4. Creases

### 4.1 Median over box, slot, vnotch, fillet (50k triangles clean; c = 2)

Command: `run_soup.sh` (`soup crease ... tris=50000 c=1.5,2,3`), raw `results/soup/crease.jsonl`.

| breakage | weld F1 (best tol) | varifold F1 | P | R | dihedral error, median (weld / varifold) | PCA F1 |
|---|---|---|---|---|---|---|
| clean | 1.000 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.255 |
| soup | 1.000 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.255 |
| nonconf | 0.563 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.227 |
| jitter:0.02 | 1.000 | 1.000 | 1.000 | 1.000 | 0.74 / 0.31 | 0.255 |
| jitter:0.1 | 1.000 | 0.998 | 0.999 | 1.000 | 3.69 / 1.51 | 0.255 |
| holes:0.1 | 0.951 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.255 |
| dup:0.2 | 0.910 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.255 |
| gap:0.3 | 0.370 | 0.874 | 0.999 | 0.777 | 2.45 / 0.00 | 0.248 |
| gap:1 | 0.082 | 0.371 | 0.637 | 0.277 | 3.59 / 0.00 | 0.233 |
| flip:0.2 | 0.082 | 1.000 | 1.000 | 1.000 | 0.00 / 0.00 | 0.255 |
| mix:0.25 | 0.078 | 0.999 | 1.000 | 0.999 | 3.03 / 0.20 | 0.224 |
| mix:0.5 | 0.034 | 0.998 | 1.000 | 0.997 | 5.54 / 0.40 | 0.229 |
| mix:1 | 0.022 | 0.910 | 0.996 | 0.838 | 90.00 / 0.80 | 0.230 |
| mix:2 | 0.023 | 0.563 | 0.708 | 0.486 | 90.00 / 1.71 | 0.220 |

Where the weld fails (box rows of the raw file): on T-junctions it keeps precision 1.0 and loses recall (0.18 to 0.48: the
seam edges stay boundary edges); holes and duplicates cost recall only (0.91, 0.84: an edge next to a hole has one
triangle, a duplicate's third triangle is dropped); a flipped triangle reads as a 180 degree dihedral against each neighbour
(precision 0.032 at 20 % flips); gaps wider than the tolerance unweld the seam. The varifold sees none of these, except the
gap: an edge of a patch moved by g h lies up to g h off the line L (section 7).

### 4.2 Per shape, false positives, radius

- Per shape (tables.md): box is exact to 0.002 on every breakage up to `mix:0.5`, fillet to 0.015; slot loses 0.7 % of recall at its
  concave corners; vnotch has precision 0.95-0.97 on every breakage, clean included (76 false positives at 50k: cap slivers of
  the anisotropic notch faces within 0.1-0.25 h of the crease, a ground-truth tolerance effect).
- `rbox` (no crease, 159k edges): 0 false positives up to `mix:0.25`, 38 at `mix:0.5`, 238 at `mix:1` and 209 at `jitter:0.1`
  (normals of tiny jittered triangles turn by more than 20 degrees); the weld: 50728 at 20 % flips, 33650 at `mix:0.5`; PCA:
  4698 at `gap:0.3` (two offset sheets look like a crease to the position covariance).
- Radius c = 1.5 / 2 / 3: equal to 0.002 up to `mix:0.5`; at `mix:2` a larger ball helps (box 0.52 / 0.58 / 0.61).
- PCA surface variation finds every crease (recall 1.0) but cannot place it (precision 0.12-0.16 on most rows: every edge within about
  e of a crease is above the threshold). Without the two-plane localisation the measure view gives no crease detector.

## 5. Time

Command: `run_soup.sh` (`soup time`), raw `results/soup/time.jsonl`; `mix:0.5` soups (the breakage adds about 40 % triangles
by duplicates and finer patches), 4 threads, seconds.

| shape | triangles | weld + dihedral | varifold creases | PCA creases | varifold curvature (one corner per triangle) | jet curvature | weld + `MeshPrincipalCurvature` |
|---|---|---|---|---|---|---|---|
| box | 13856 | 0.06 | 0.24 | 0.36 | 0.41 | 0.61 | 0.10 |
| box | 67192 | 0.19 | 0.97 | 0.97 | 0.86 | 1.96 | 0.22 |
| box | 142910 | 0.45 | 2.08 | 2.07 | 1.81 | 4.09 | 0.31 |
| box | 269933 | 0.94 | 4.05 | 3.77 | 3.61 | 7.87 | 1.14 |
| rbox | 138387 | 0.48 | 2.23 | 2.06 | 1.76 | 3.96 | 0.40 |

Linear in the size: about 15 microseconds per triangle for the crease test (3 queries), 2.1 s at 100k clean triangles,
inside the brief's "a few seconds at 100k" and about 4 times the weld. The curvature times are for twice the vertex count
of queries; at one query per vertex they halve. Against the alpha wrap that the creases would feed (median 4.1 s, the pipeline 4.9 s, on the
T1 meshes of the SharpFeatureProbe README; other sizes) the crease test at 100k is about 40 %; the earlier survey's kill line was 20 %
of the wrap. A crease test only where the feature pass asks (the wrap's crossing edges, not every soup edge) is the obvious
way down (section 7).

## 6. Curvature

Command: `run_soup.sh` (`soup curv`, `soup cloud`), raw `results/soup/curv.jsonl`, `cloud.jsonl`; e_K median / p90 in units of
1 / R (1.0 = an error of 4 on a curvature of 4). R / h = 11 (fillet) to 12 (rbox) at 50k triangles.

### 6.1 Soups, cylinders of the fillet block (rbox cylinders and spheres: same picture, tables.md)

| breakage | weld 0.3 h + MPC, 2 rounds | varifold e = 3 h | varifoldLS 3 h | jet 3 h | varifold 5 h | varifoldLS 5 h | jet 5 h |
|---|---|---|---|---|---|---|---|
| clean | 0.010 / 0.010 | 0.015 / 0.015 | 0.009 / 0.009 | 0.008 / 0.008 | 0.038 / 0.038 | 0.023 / 0.023 | 0.020 / 0.020 |
| jitter:0.1 | 0.049 / 0.091 | 0.094 / 0.182 | 0.107 / 0.212 | 0.078 / 0.154 | 0.052 / 0.080 | 0.048 / 0.076 | 0.025 / 0.045 |
| holes:0.1 | 0.027 / 0.046 | 0.022 / 0.063 | 0.010 / 0.027 | 0.008 / 0.009 | 0.037 / 0.060 | 0.024 / 0.028 | 0.020 / 0.021 |
| flip:0.2 | 0.256 / 0.733 | 0.015 / 0.015 | 0.009 / 0.009 | 0.008 / 0.008 | 0.038 / 0.038 | 0.023 / 0.023 | 0.020 / 0.020 |
| mix:0.5 | 0.108 / 0.426 | 0.039 / 0.069 | 0.037 / 0.063 | 0.026 / 0.041 | 0.040 / 0.060 | 0.023 / 0.039 | 0.020 / 0.027 |
| mix:1 | 0.337 / 0.662 | 0.069 / 0.112 | 0.074 / 0.127 | 0.051 / 0.086 | 0.044 / 0.084 | 0.036 / 0.067 | 0.017 / 0.033 |

`MeshPrincipalCurvature` is exact on a clean welded mesh (0.000 without smoothing) and breaks where the weld breaks: K = 0
on an unwelded jittered soup (e_K 1.000 with `Valid` true), 0.21-0.50 median with 20 % flips (the angle-weighted vertex
normals cancel), 0.30-0.62 at `mix:1`. The varifold estimators do not care about flips, duplicates or T-junctions, but the
jet fit on the same points and radius is at least as good in every row and region, by a factor 1.1-2.4 on the broken ones.
The bias of the kernel-ratio estimator grows as (e / R)^2 (clean: 0.015 at 3 h, 0.038 at 5 h); the least-squares variant
halves it.

### 6.2 BLM mean curvature

On clean soups the regularised first variation is good (e_H 0.004 on cylinders at 3 h, the best of all). It breaks as soon as
the query is off the measure's surface (a jittered corner, a gap) or the masses are uneven: e_H on flats 0.50 at `mix:0.5`
(3 h), against 0.02 for the jet fit (rows `eH_*` of curv.jsonl). This is the known condition of the method (consistency for a
point on the surface and regular sampling); a projection of x onto the local plane first would remove the offset part.

### 6.3 Point clouds (random area-weighted samples, unoriented PCA normals; noise along the normal in spacings)

| region, noise | varifold 3 s | varifoldLS 3 s | jet 3 s | varifold 5 s | varifoldLS 5 s | jet 5 s |
|---|---|---|---|---|---|---|
| rbox cylinders, 0 | 0.154 / 0.368 | 0.108 / 0.281 | 0.022 / 0.045 | 0.087 / 0.210 | 0.050 / 0.112 | 0.016 / 0.022 |
| rbox cylinders, 0.2 | 0.320 / 0.567 | 0.329 / 0.590 | 0.621 / 1.336 | 0.141 / 0.283 | 0.136 / 0.261 | 0.149 / 0.283 |
| rbox spheres, 0.2 | 0.578 / 0.866 | 0.535 / 0.918 | 0.958 / 1.768 | 0.228 / 0.401 | 0.180 / 0.338 | 0.172 / 0.274 |
| rbox flats, 0.2 | 0.376 / 0.673 | 0.414 / 0.743 | 1.032 / 1.845 | 0.155 / 0.266 | 0.178 / 0.312 | 0.201 / 0.354 |

The varifold estimators average normals, so they are less hurt by noise at a small radius (0.32 against 0.62 at 3 s); at
the better radius (5 s) they tie with the jet fit on noisy clouds and lose 3 times on clean ones. The BLM mean curvature is
far off on random samples (e_H 3.6 at 3 s on flats): with equal masses, the sampling's density fluctuation is a first
variation.

## 7. Next candidates and their bounds

| candidate | bound (measured) | status |
|---|---|---|
| Varifold creases as the input creases of `AlphaWrapFeaturePass` on soups (today it gets none) | the pass finds 0 creases on any unwelded input; the varifold gives F1 >= 0.998 up to `mix:0.5` | **the payoff; needs a library design (stop here, per the brief)** |
| Crease test only at the wrap's crossing edges instead of every soup edge | queries drop from 3 per input triangle to about the wrap's crossing edges (thousands): the 2.1 s at 100k is the upper bound, the sampling stays | design step of the row above |
| Adaptive localisation tolerance for gaps (the tolerance from the measured offset of parallel sheets) | oracle tolerances (`crease_gap.jsonl`): `gap:0.3` F1 0.87 -> 0.94-1.00 at 0.6 h, but `mix:1` precision drops 0.09-0.16; `gap:1` stays <= 0.6 at any tolerance (a gap as wide as a triangle is ambiguous at e = 2 h) | +10 % F1 on gaps only, a loss elsewhere: not worth it alone |
| Jet fit on the soup's quadrature as connectivity-free curvature (not a varifold) | e_K 2-18 times below weld + `MeshPrincipalCurvature` on broken soups (fillet cylinders: `jitter:0.1` 0.025 against 0.049, `mix:1` 0.017 against 0.31), equal on clean ones | clears the threshold; a library design only if a consumer needs curvature on soups (none named today) |
| Varifold curvature with projection of x onto the local plane, least squares | bounded by the jet fit (both are local fits of the same points; LS already halves the kernel bias) | stop |
| Flagfolds (corners and creases in one covariance object) | the two-plane test already finds corners' creases to within e; a corner detector has no consumer yet | parked |

## 8. Determinism and invariance

Command: `run_soup.sh` (`soup check`), raw `results/soup/check.jsonl`; fillet and rbox, `mix:0.5`, 20k triangles: crease
flags and dihedrals, varifold curvature at 4000 corners.

- Threads 1 against 4 and a repeated run: identical bits (both shapes). Rows are computed in parallel, each in a fixed
  order of cells and points; sampling and weld are serial.
- The `Similarity.All()` list (the harness's eight, copied into the probe: the probe does not reference the test project):
  scales 2^-10 and 2^10 give the same bits (curvature scaled back); scale 1e-3, 1e3 and the rotation agree in every crease
  flag with curvature within 2e-12 relative; the UTM translation (coordinates 6e6) within 6e-6, all three together
  (coordinates 6e6 at a spacing 1e-5) within 3e-3 relative, every flag equal.

## 9. What was not done

- No library code, no public API, no tests in the test project (research only; the probe's checks are section 8). The
  probe runs in about 25 minutes (`run_soup.sh`, mostly the 630 crease rows).
- One seed per case. The shapes are CAD-like solids; scans, thin walls (two sheets within e) and very small creases were not
  measured: a thin wall within e would read as a crease of 180 degrees at both sheets.
