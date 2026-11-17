# Geogram survey: measurements of rows 1-3 (2026-11)

Owner-approved 2026-11-17: measure the three "worth a measurement" ideas at the top of the table in
[GEOGRAM_SURVEY_2026-11.md](GEOGRAM_SURVEY_2026-11.md) (rows 1-3). **Measurement only.** No library code changed, no
default changed. Each item was a bounded spike written from the papers; no geogram code was read for this note (the
survey read it before), and none was ported. An adoption is the owner's decision, with an A/B.

Before each item, the library and the docs were searched: no spectral or eigenvector solver, no lfs computation and no
restricted Voronoi code exist in the library (`MeshWeights`, `MeshCurvature` and `PointAABBTree3` use Voronoi areas
only), and the docs had no numbers for the three ideas (the survey marks all three "not measured"). The one earlier
mention of an lfs yardstick, stmlf/1461 at "6.8x the reference relative to the LFS yardstick" (meshing2d.md,
MESHING2D_WEDGE_COST.md), has no computation in the repository and is not for the survey's inputs.

Machine: the cloud container (4 cores), .NET 10, Release, master 5e3a4be9 plus this PR. Times are single runs on a
shared machine.

## Result in short

| # | Idea | Measured | Recommendation |
|---|---|---|---|
| 1 | Pin-free spectral conformal map (Mullen et al. 2008) | P3: asymmetry 9.4e-5 → 1.1e-13 of the UV size. P1: no collapse and close to the exact annulus where the start vector lies in one of the two annulus maps; but on symmetric annuli the smallest eigenvalue is double (both maps, either ring inside) and the result depends on the start (the plane start folds all four cylinders at h = 3.6, an xy start does not), and on jittered ones it is nearly double and 3 of the 4 converged eigenvectors fold. Discs: same mean distortion, max equal or lower (cap 0.8 pi: 1.086 → 1.058 at 48k), 1.65-1.85x LSCM's time at 48k | **go** for an opt-in disc-chart mode (owner decision, A/B in the atlas); **no-go** as the P1 fix (needs a subspace solve and a selection rule: research) |
| 2 | Ruppert lfs yardstick, I = ∫ 1/lfs² | f-11/a, f-11/b, f-40: ours 2.47 / 2.53 / 1.94 triangles per unit of I, the reference 1.24 / 1.43 / 1.37; on the 13 extra octaves of f-11/b ours 2.61, the reference 1.71 | **go** as a metric normalised by each case's lfs (benchmark command `lfs`); it is **no** lower bound in triangles (the constant is not known), so it does not say how far either mesher is from the optimum |
| 3 | True restricted Voronoi cells for the (C') / R2 check of RDR | On the 31 final states the proxy misses 2 R2 violations (lens psi 60, by < 0.05 %) and the 21 non-disk cells of the uncertified psi 45 case, and it does not check cells that hold no input vertex (up to 97 % of the cells on coarse inputs); before the region rounds it raises up to 69 false non-disk alarms per case (no true cell was a non-disk) and misses 13 of 44 R2 violations at creases. The float RVC check costs 0.003-0.28 s, less than the proxy on 21 of 31 cases | **go**: replace the proxy by RVCs in RDR stage 1b (float in the spike now; the exact version is the L item with the bisector predicates) |

Side finding (item 1): `LSCMParameterization` returns `Success` with mean conformal distortion 1.5e8 (max 9.8e10) on
the open cylinder 48 x 12 of height 3.6, whose far ring collapses to a near-segment without a flipped or (by the rule)
degenerate triangle; on a jittered 24 x 6 cylinder `Success` with max conformal 36.5. The P1 status check (flips and
`DegenerateUVTriangles`) does not catch a near-collapse. Not fixed here (it changes a status); reported to the
coordinator.

## 1. Pin-free spectral conformal parameterization

Code: `benchmarks/ParameterizationBenchmark/SpectralConformal.cs`, run with
`dotnet run --project benchmarks/ParameterizationBenchmark -c Release -- spectral [maxRings]`.

Method (from the paper, P. Mullen, Y. Tong, P. Alliez, M. Desbrun, *Spectral Conformal Parameterization*, SGP 2008): the
same energy as LSCM, E_C = E_D - A (plain cotan weights; the signed area as a sum over the boundary edges), but instead
of two pins minimize E_C subject to x^T B x = 1 and x B-orthogonal to the translations, B the lumped boundary mass (half
the length of the two boundary edges at each boundary vertex). The minimizer is the eigenvector of A x = λ B x of the
smallest nonzero eigenvalue, which is a pair (x and its rotation by 90 degrees: the same map). Computed by inverse power
iteration on `SparseCholesky` (block 2): A + σ B is factorized once (A alone is singular: the translations), σ = 1e-9
of trace(A) / trace(B), the iterate projected B-orthogonally off the translations, stop when the B-norm of the change is
below 1e-10 or after 200 iterations (reported as "not converged"). Start: the vertices projected on their least-squares plane. The map is scaled to the
surface area, as LSCM's.

### P1: open cylinders (radius 1, sides x rings, height h)

The exact conformal image of a cylinder of radius 1 and height h is an annulus with inner / outer radius e^-h.
"inner / outer" is the mean UV radius of the smaller boundary ring over that of the larger one, about the centroid of
the first ring. Height 1.8 is the issue's case.

| cylinder | LSCM (status, flips + degenerate, mean conformal, inner / outer) | spectral (flips, mean / max conformal, inner / outer, iterations) | exact |
|---|---|---|---|
| 8 x 3, h 0.9 | FlippedTriangles, 2, 3.2e12, 0.506 | 0, 1.546 / 1.615, 0.435, 7 | 0.407 |
| 8 x 3, h 1.8 | FlippedTriangles, 2, 2.0e13, 0.217 | 0, 1.650 / 1.785, 0.190, 9 | 0.165 |
| 16 x 4, h 1.8 | FlippedTriangles, 2, 9.1e10, 0.215 | 0, 1.345 / 1.399, 0.175, 7 | 0.165 |
| 24 x 6, h 1.8 | FlippedTriangles, 2, 7.5e10, 0.213 | 0, 1.219 / 1.245, 0.170, 6 | 0.165 |
| 48 x 12, h 1.8 | FlippedTriangles, 2, 2.1e8, 0.211 | 0, 1.105 / 1.111, 0.166, 5 | 0.165 |
| 16 x 4, 24 x 6, 48 x 12 at h 0.9 | FlippedTriangles (all) | 0 flips, mean 1.08-1.26, inner / outer 0.408-0.415 | 0.407 |
| all four at h 3.6 | FlippedTriangles (3), Success with mean 1.5e8 (48 x 12) | **2 flips, mean 1e9-4e9, inner / outer 1.0 (folded)** | 0.027 |
| all four at h 3.6, xy-plane start | | 0 flips, mean 1.18-1.99, inner / outer 0.028-0.042, 6-12 | 0.027 |

The fold at h = 3.6 is not a convergence failure (residual 2e-11 to 6e-11), and it depends on the start, not on the
height. The next eigenvalue pair (deflating the first pair and iterating again) equals the first to all printed digits on
every one of these cylinders, at all three heights: the triangulation is symmetric under the half turn about a
horizontal axis (θ → -θ, z → h - z), which maps the conformal annulus map z = e^w to e^(h-w) (the other ring inside),
so both have the same energy and the smallest eigenvalue has a four-dimensional space. The iteration returns the part
of the start vector in it. The default start is the least-squares plane: for h < √6 ≈ 2.45 that is the xy plane, whose
start (cos θ, sin θ) has only the angular frequency +1 and is B-orthogonal to the other map, so the result is the pure
map; for h = 3.6 the plane contains the axis, the start (sin θ, z) mixes both, and the mixture folds. With an xy start
at h = 3.6 the maps are clean (the row above); a start in a tilted plane would fold at h ≤ 1.8 too.

Symmetry broken:

| cylinder | LSCM | spectral (λ next / λ) |
|---|---|---|
| frustum (top radius 0.9), 8 x 3, h 1.8 / 3.6 | Success, mean / max 1.89 / 7.29, 2.48 / 11.7 | 0 flips, 1.62 / 1.74, 2.00 / 2.25 (1.42, 1.36) |
| frustum, 24 x 6, h 1.8 / 3.6 | Success, 1.25 / 2.68, 1.44 / 4.27 | 0 flips, 1.21 / 1.24, 1.39 / 1.46 (1.47, 1.41) |
| z jitter 10 % of the ring spacing, 8 x 3, h 1.8 / 3.6 | FlippedTriangles (2) | not converged in 200 iterations; with 5000: converged after 2317 / 1079, 2 flips and inner / outer 0.33 (folded) / 0 flips, mean / max 2.36 / 8.0 (1.008, 1.018) |
| z jitter, 24 x 6, h 1.8 / 3.6 | Success, max 36.5 / 11.4 | with 5000: converged after 2428 / 1598, 2 flips in both, inner / outer 0.45 / 0.12 (1.008, 1.012) |

So the spectral map fixes P1 only where the two annulus maps differ clearly in energy (the frustum: λ next / λ =
1.36-1.47). On the jittered cylinders λ next / λ is 1.008-1.018: the iteration needs 1000-2400 steps, and the
converged eigenvector is itself a mixture of the two maps, which folds in 3 of 4. A fix would compute the
four-dimensional space (subspace iteration with Rayleigh-Ritz) and pick the pure map in it (eg the one whose boundary
rings are concentric circles, or the injective one), which the paper does not cover: research, not this spike.

### P3: the plate with two mirrored holes

Metric of `TwoIdenticalRectangularHoles_SymmetricResult`: the largest difference of UV distances of mirrored vertex
pairs, over the UV diagonal (400 sampled pairs of pairs; and over all pairs of pairs).

| method | asymmetry (sampled / all) | mean / max conformal | iterations |
|---|---|---|---|
| LSCM, automatic pins | 9.4e-5 / 1.3e-4 | 1.002 / 1.005 | |
| LSCM, mirrored pins | 6.1e-14 / 7.7e-14 | 1.002 / 1.005 | |
| spectral, tol 1e-10 | 1.1e-13 / 1.3e-13 | 1.002 / 1.005 | 3 |

The spike's assumption holds: without pins the map is symmetric to rounding, with the same distortion.

### ParameterizationBenchmark meshes

Time: `LSCMParameterization.Compute` (includes its distortion pass) against the spectral spike (assembly, factorization
and iterations); distortion: `ParameterizationDistortion.Compute`, mean / max conformal (s1/s2), mean area distortion.

| mesh | V | LSCM time | LSCM conformal, area | spectral time (iterations) | spectral conformal, area |
|---|---:|---:|---|---:|---|
| hemisphere | 12k | 189 ms | 1.012 / 1.037, 0.351 | 184 ms (7) | 1.012 / 1.027, 0.350 |
| cap 0.8 pi | 12k | 135 ms | 1.029 / 1.147, 1.297 | 175 ms (10) | 1.029 / 1.105, 1.292 |
| ellipsoid | 12k | 106 ms | 1.008 / 1.039, 0.171 | 173 ms (7) | 1.008 / 1.032, 0.170 |
| cut cylinder | 15k | 159 ms | 1 / 1, 3e-10 | 971 ms (200) | 1 / 1, 3e-10 |
| hemisphere | 48k | 0.70 s | 1.006 / 1.023, 0.351 | 1.16 s (11) | 1.006 / 1.023, 0.350 |
| cap 0.8 pi | 48k | 0.73 s | 1.014 / 1.086, 1.298 | 1.21 s (11) | 1.014 / 1.058, 1.293 |
| ellipsoid | 48k | 0.68 s | 1.004 / 1.021, 0.171 | 1.25 s (12) | 1.004 / 1.021, 0.170 |
| cut cylinder | 58k | 0.92 s | 1 / 1, 1e-8 | 2.73 s (47) | 1 / 1, 8e-9 |

- On curved discs the mean distortion is the same and the maximum equal or lower (lower on the 0.8 pi cap at both
  sizes and on all three at 12k; equal on the hemisphere and the ellipsoid at 48k), at 1.65-1.85x LSCM's time at 48k
  (one factorization like LSCM's plus 11-12 solves of about 30 ms).
- The cut cylinder is developable: LSCM is exact there and so is the spectral map, but the iteration runs long (47-200
  iterations, 3-6x LSCM's time) without harm to the map. Why was not measured (λ was not printed for these meshes; a
  near-zero λ with close next modes, or the change stalling at rounding noise above the tolerance, are the candidates).
- The shift matters: with σ = 1e-6 of the trace ratio the 48k discs need 200 iterations (4.3-4.7 s), and the cut
  cylinder ends with area distortion 0.65 instead of 1e-8. 1e-9 is used above.

### Recommendation

- **Go, opt-in, for disc charts** (owner decision): it removes the pin choice (P3 to 1e-13) and keeps the maximum
  conformal distortion equal or slightly lower, at 1.65-1.85x LSCM's time on curved discs (48k). Before an adoption: an A/B in the atlas
  (`UVAtlasBenchmark`: the charts where LSCM is the fallback) and the mollified weights of `ParamMesh` (the spike uses
  plain cotan weights), plus a stop rule for developable charts (eg fall back to LSCM when λ is below a tolerance).
  A default change would move ARAP, SLIM and atlas results.
- **No-go as the P1 fix**: on symmetric annuli the smallest eigenvalue is double and the map depends on the start; on
  nearly symmetric ones it is nearly double, and the converged map folds in 3 of 4 cases.
  The P1 limitation stays as documented; the virtual-fan option of ISSUE_CASE_FINDINGS.md P1 is the other candidate.

## 2. Ruppert lfs as the yardstick of the 2D grading gap

Code: `benchmarks/Meshing2Benchmark/LfsYardstick.cs`, run with
`dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- lfs [--cases ...] [--levels 6]` (2 s).

lfs(p) is Ruppert's local feature size (J. Ruppert, *A Delaunay refinement algorithm for quality 2-dimensional mesh
generation*, J. Algorithms 1995): the radius of the smallest disc centred at p that meets two features (input vertices
and segments) that share no point. Computed by brute force over the features, with the segments split at input vertices
on them (f-40 has 3 random points on its outer square; without the split lfs is 0 there and the integral diverges). The
integral I = ∫ 1/lfs² over the domain by the centroid rule on QualityMesh2's output triangles, each split into 4^L
triangles; L = 0 .. 6 converge to 4 digits. Triangle counts of the reference mesher are those of
[MESHING2D_GRADING.md](../design/MESHING2D_GRADING.md) (default mode at the same angle bound; not run here).

| case | ours, default | ours, cq | I (L = 6) | ours / I (default, cq) | reference | reference / I | 1.25x target / I |
|---|---:|---:|---:|---|---:|---:|---:|
| f-11/a, 20° | 215 | 220 | 87.10 | 2.47, 2.53 | 108 | 1.24 | 1.55 |
| f-11/b, 20° | 363 | 386 | 143.7 | 2.53, 2.69 | 205 | 1.43 | 1.78 |
| f-40, 33.75° | 184 | 184 | 94.85 | 1.94, 1.94 | 130 | 1.37 | 1.71 |

- Check: f-11/b has 13 more octaves of distance from its cluster than f-11/a, and around a point lfs(p) ≈ |p - c|, so
  each octave adds 2 π ln 2 = 4.36 to I; measured 56.6 = 13 x 4.35.
- Per unit of I on those 13 octaves: ours 148 / 56.6 = 2.61 triangles, the reference 97 / 56.6 = 1.71 (11.4 and 7.5
  triangles per octave, as MESHING2D_GRADING.md section 4.5 counted).
- What the yardstick says: the triangle count of any mesh with a minimum angle α is at least c(α) · I (S. A. Mitchell,
  *Cardinality bounds for triangulations with bounded minimum angle*, CCCG 1994), and Ruppert's refinement uses at most
  C(α) · I (Ruppert 1995), but neither constant is known sharply, so I is no lower bound in triangles. "How far from the
  bound" therefore has no number; what I adds is a **density normalised by the case's lfs** (it still depends on the
  angle bound and on the case: the reference itself ranges over 1.24-1.43). The reference reaches 1.24-1.43 triangles per unit of I on these inputs at 20° and
  33.75°, ours 1.94-2.69, and the owner's 1.25x target means at most 1.55-1.78 here.
- Not done: a Herne tile (the survey's fourth input). It needs the ALKIS fetch, a spatial index for lfs (brute force is
  O(features) per quadrature point) and a reference count at the same settings, which the docs do not have for current
  master. Upper bound: about half a day.

### Recommendation

**Go as a metric, no-go as a bound.** Keep the `lfs` command as the grading yardstick: with a spatial index it makes
the grading target measurable on inputs without a reference count (the corpus), in triangles per unit of I. It does not
change the conclusion of GRADING.md (the excess is density per octave, not missing grading), and it offers no lever by
itself; a placement change stays with the coarsening project (meshing2d.md option 1).

## 3. True restricted Voronoi cells for the (C') / R2 check of certified remeshing

Code: `benchmarks/RestrictedDelaunaySpike/RvcCheck.cs` (and a measurement hook in `Rdr.cs`), run with
`dotnet run --project benchmarks/RestrictedDelaunaySpike -c Release -- rvc [only=<regex>]` (73 s for all 31 cases).

Method (from L. Yan, B. Lévy, Y. Liu, F. Sun, W. Wang, *Isotropic remeshing with fast and exact computation of
restricted Voronoi diagram*, SGP 2009; T14 of [CERTIFIED_REMESHING.md](../design/CERTIFIED_REMESHING.md)), in floating
point: each input triangle is clipped by the bisector planes of a sample and its Delaunay neighbours (from the spike's
`Delaunay3`, nearest first), stopping by the security radius (a neighbour farther than twice the largest distance of the
polygon from the sample cannot cut it); the samples whose cells meet the triangle are found by propagation across the
bisector edges of the clipped polygons, starting from the sample nearest the centroid. Per sample, the cell's pieces are
glued along shared input-edge segments by combinatorial vertex keys (input vertex; input edge and bisector; triangle and
two bisectors), which gives the components and the Euler characteristic; (C') holds if the cell is empty or one disk
(one component, χ = 1), R2 if its farthest point is within r_max of the sample. Checks: the cell areas sum to the area
of S in every case (ratio 1 to 4 digits); a self-test gives χ = 1 for the two cells of a sphere with samples at the poles
, χ = 0 (annuli) for three samples on the centre circle of a torus, and two components with χ = 4 for one sample
between two spheres.

Compared per sample with the proxy of `Rdr.region_violations` (input vertices labelled by their nearest sample, the
labelled region must be one disk, its farthest input vertex within r_max), on two states of each run: before the first
region round (after the facet rules) and the final one. Inputs: the 12 smooth cases and 4 lens cases (psi 45, 60, 90,
300) of the spike, and 15 coarse ones (normalized-cube spheres with 8, 16, 32 edge vertices, a bumpy sphere with 24, a
60 x 18 torus) at r_max = diag / 30, 60, 120, where the mean input edge is 0.5-8 r_max.

| cases | proxy misses (C') | proxy misses R2 | proxy false alarms (C') | cells the proxy does not see |
|---|---|---|---|---|
| smooth, d30 / d60 (8), final | 0 | 0 | 0 | 0 |
| smooth, d120 (4), before the region rounds | 0 (no true violation) | 0 | 11-69 per case | 0-136 |
| smooth, d120, final | 0 | 0 | 0 | 0-136 (of 7.6k-9.6k) |
| lens psi 45 / 60 / 300, before | 0 | 4 of 26 / 8 of 13 / 1 of 5 | 3 / 4 / 1 | 0 |
| lens psi 60, final | 0 | 2 (by < 0.05 % of r_max) | 0 | 0 |
| lens psi 45 (uncertified case), final | 21 non-disk cells, all in cells the proxy does not see | 0 | 0 | 357 of 2769 |
| coarse (15), final | 0 | 0 | 0 | 0-9748 (sphere8 d120: 97 % of 10044) |

- **What the proxy misses on these inputs.** On every final state of a certified case the true cells are disks within
  r_max, so the proxy did not let a violation through except 2 marginal R2 cells at the psi 60 crease. Its blind spots
  are real but did not bite here: a cell that holds no input vertex is not checked at all (most cells once the input
  edges are longer than r_max), and at creases the farthest point of a cell is often not an input vertex (before the
  region rounds it missed 13 of the 44 R2 violations on psi 45, 60 and 300).
- **What it costs instead.** Before the region rounds the true cells had no (C') or R2 violation in any smooth or coarse
  case, so the region rounds there were started by proxy false alarms (checked on that state only; later rounds and the
  refinement cascades of their insertions are counted in): 11-276 extra samples on the d120 cases (0.1-3.4 %;
  ellipsoid d120 +276 in 3 rounds), 4-39 on the coarse ones.
- **Time.** The float RVC check takes 0.003-0.28 s (171k clips on the d30 sphere, the security radius skipping 76 % of
  the neighbour planes), below the proxy's 0.001-2.15 s on 21 of 31 cases (the proxy's farthest-vertex scan is
  O(samples x vertices) as written), and 1-18 % of a refinement run (whose time includes the first comparison). The exact version (the bisector predicate family of
  CERTIFIED_REMESHING.md section 14.4, filtered) is not measured; its constructions are the cost driver.

### Recommendation

**Go**: use true restricted Voronoi cells as the (C') / R2 check of RDR stage 1b instead of the proxy. In floating
point its cost is comparable to the proxy's (lower on 21 of 31 cases, against a proxy whose farthest-vertex scan is
O(samples x vertices) as written; on the coarsest inputs, spheres of 8 and 16 edge vertices and the 60 x 18 torus, the proxy takes 1-16 ms against 4-45 ms), it sees every cell,
raises no false alarms and catches the crease R2 cases; the spike can
switch to it now (a float check, no certificate). The certificate needs the exact version (effort L: the planned
bisector predicates and new bounds, exact-code review), which stage 1b had planned anyway; this measurement says the
combinatorial part (propagation, gluing, χ) is cheap.

## Benchmark code in this PR

- `benchmarks/ParameterizationBenchmark/SpectralConformal.cs` (`-- spectral`), `Program.cs` (the command; `Cap` and
  `Cylinder` made internal).
- `benchmarks/Meshing2Benchmark/LfsYardstick.cs` (`lfs`), `Program.cs` (the command, before the oracle self-check).
- `benchmarks/RestrictedDelaunaySpike/RvcCheck.cs` (`rvc`), `Rdr.cs` (the proxy's per-sample verdict and a hook before
  each region round; the default run is unchanged), `Program.cs` (the command, the coarse inputs, the self-test).
