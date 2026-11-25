# Pointwise Distance Distributions (PDD) as a shape invariant: research probe

Status: research probe, 2026-11-24 (owner go the same day). No library change, no new public API. Prototype:
[benchmarks/PddProbe](../../benchmarks/PddProbe/README.md); raw outputs in `benchmarks/PddProbe/results/`. This is candidate #11
of [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md), which ranked it "Tier 1 (Very High)" by
transfer value alone. This probe checks that rank against simple baselines.

**Verdict: stop (kill criterion met).** PDD is deterministic and cheap enough: 0.53 s at 100k points, k = 32, on 4 threads,
bit-identical across runs, thread counts, input order and `2^k` scales. But it is not better than the simple baselines on
uses (a) and (c); the sorted pairwise-distance histogram (D2) beats it clearly:

- Discrimination (a), N = 2000: PDD was clearly better in 5 of 144 cases, D2 or PCA in 58.
- "Same shape?" retrieval (c): 43/52 top-1 for the best PDD against 49/52 for D2, and 85 false positives at the
  no-false-negative threshold against 36.

The variants that are not scalable (exact EMD, full PDD with k = N-1) do not change this. For use (b), PDD rows pruned
the candidates of a lattice-period search, and the search recovered the gyroid's body-centred lattice. But `TpmsLattice`
knows its period, and checking it costs 2-13 ms, so no consumer needs the detection. Section 6 lists the next candidates
the measurements point to, with their bounds; one lead, exact moment invariants, is outside PDD and is handed back to the
coordinator.

## 1. The method and what was implemented

From the paper only: D. Widdowson, V. Kurlin, arXiv:2108.04798, v4 (29 Dec 2025, "will appear in SIAM J. Appl. Math.",
extension of the NeurIPS 2022 paper). For a finite set S of m points and an integer k:

- **PDD(S; k)**: the m x k matrix whose row i holds the distances from p_i to its k nearest neighbours, in ascending order.
  Rows that coincide are collapsed into one row with weight l/m (Definition 3.1), and the rows are kept in lexicographic
  order.
- **Metric**: the Earth Mover's Distance between two PDDs, with an L_q ground distance between rows. Here it is L_inf.
- **AMD** (Average Minimum Distance) is the weighted mean of each column. The paper bounds the EMD from below by the L_q
  distance of the AMDs, and from above under perturbation: moving every point by at most eps changes the invariant by a
  constant multiple of eps (abstract). The paper claims completeness for finite sets with distinct inter-point distances
  ("for finite unordered sets with distinct distances", introduction); the relevant theorems were read only in summary.

The prototype computes:

- kNN by a uniform grid with ring search (each row depends only on the multiset of distances from p_i, so the result does
  not depend on thread or order);
- rows normalized by the RMS radius about the centroid (sorted sums, so the result is order-independent and exactly
  covariant under `2^k` scaling);
- three distances between PDDs:
  - **AMD**: L_inf of the column means;
  - **colW1**: the maximum over columns of the 1D Wasserstein-1 distance. It is a lower bound of EMD_inf (project any
    transport plan onto one column), it costs O(m log m), and it is at least AMD;
  - **exact EMD**: for equal row counts with uniform weights an optimal plan is a permutation (Birkhoff), so the EMD is an
    assignment problem, solved with the Hungarian algorithm, O(m^3).
- Rows are not collapsed. Collapsing applies only to exact symmetric sets, and the period probe groups rows with a
  tolerance instead.

Baselines:

- **D2** (Osada et al. 2002): the sorted multiset of normalized pairwise distances, compared by 1D W1. It uses all pairs
  up to 200k pairs, otherwise 200k pairs from a fixed seed.
- **PCA**: the square roots of the covariance eigenvalues over the RMS radius (3 numbers, L_inf).

### Look before you build

| Building block | Status | Used |
|---|---|---|
| `PointAABBTree3` | nearest point and fast winding only, no k-nearest query | no (own grid) |
| `PointHashGrid3d` | `FindNearestInRadius` only | no |
| `gs.MinCostFlow` (solvers/MinCostFlow.cs) | integer successive shortest paths, `internal` (would need an `InternalsVisibleTo`, a library change) | no: EMD with uniform weights is an assignment, solved in the probe |
| `MeshICP` | needs a correspondence or an initial alignment; not an invariant | no |
| `MeshMeasurements.MassProperties` | volume, centroid and inertia tensor of a closed mesh (WildMagic5 port) | the building block for the moments lead of section 6 |
| [ACE_DESCRIPTOR_STUDY.md](ACE_DESCRIPTOR_STUDY.md) | same verdict pattern for a local descriptor: the limit lies outside the descriptor | context |

Literature (prior art):

- The PDD paper itself says PDD "appeared as a local distribution of distances in the finite case", citing Mémoli's
  Gromov-Wasserstein lower bounds (F. Mémoli, Found. Comput. Math. 2011). PDD rows are those local distance distributions,
  truncated at k.
- D2 (Osada, Funkhouser, Chazelle, Dobkin, ACM TOG 2002) is the global counterpart.
- The Springer and ACM pages could not be fetched from this container; both papers are cited from their well-known
  content, not re-read.

### Licence

- The paper is under CC BY-NC-ND 4.0 (arXiv page). It was read as text only.
- The reference package `average-minimum-distance` (dwiddo, PyPI 1.6.1) shows a CC BY-NC-SA 4.0 badge on GitHub and PyPI,
  with "CC-0" in the badge's alt text. Its LICENSE file was not read, and its code was not read. The candidate note says
  "reference Python code is MIT", which this check did not confirm (section 7).

## 2. Determinism (step 4)

```
dotnet run --project benchmarks/PddProbe -c Release -- determinism     # results/determinism.txt, ~6 s
```

The input is 50k gyroid samples with k = 32. The hash is FNV-1a over the bits of the normalized rows in lexicographic
order.

| Check | Result |
|---|---|
| threads 1, 2, 4, two runs each | `C2F2A59BE83B4E74` every time |
| permuted input | `C2F2A59BE83B4E74` |
| `Similarity.All()`: identity, scale 2^-10, scale 2^10 | bit-identical |
| utm / scale 1e-3 / scale 1e3 / rotated | differ; max relative AMD deviation 7.7e-11 / 3.7e-15 / 3.7e-15 / 8.1e-16 |
| rotated, scale 1e-3, utm | max relative AMD deviation 6.7e-8 (the UTM offset costs coordinate bits) |

(The eight similarities are copied from `tests/geometry3Sharp.Tests/Harness/Invariance.cs`; the probe does not reference
the test project.)

## 3. Cost (kill criterion: about 1 s at 100k points)

```
dotnet run --project benchmarks/PddProbe -c Release -- time    # results/time.txt; 4 logical cores, min of 3 runs, ms
```

| N | k | kNN 1 thread | kNN 4 threads | scale | sorted columns + AMD | compare (AMD + colW1) | D2, 1M pairs, both + W1 | PCA |
|---|---|---|---|---|---|---|---|---|
| 10k | 32 | 42 | 11 | 3 | 24 | 0.3 | 238 (k=16 row) | 0.0 |
| 100k | 16 | 320 | 89 | 37 | 150 | 2.2 | 244 | 0.3 |
| 100k | 32 | 581 | 160 | 37 | 329 | 4.6 | | |
| 100k | 64 | 847 | 220 | 34 | 633 | 11.6 | | |
| 200k | 32 | 1221 | 326 | 76 | 671 | 11.8 | | |

- **At 100k points and k = 32**, a PDD signature costs 0.53 s on 4 threads and 0.95 s on 1 thread. The column sort is
  serial. Comparing two signatures takes milliseconds. This passes the cost criterion, but only with the AMD and colW1
  bounds.
- **The exact EMD** is O(m^3): 8 / 37 / 182 ms at m = 200 / 400 / 800, so it does not scale to 100k rows. A full PDD
  (k = m-1) is O(m^2).

## 4. Discrimination (a) and "same shape?" (c)

### 4.1 Discrimination

```
dotnet run --project benchmarks/PddProbe -c Release -- disc 300     # results/disc_300.txt   (with exact EMD and k = N-1), 550 s
dotnet run --project benchmarks/PddProbe -c Release -- disc 2000    # results/disc_2000.txt, 135 s
python3 benchmarks/PddProbe/analyze.py benchmarks/PddProbe/results/disc_<N>.txt   # results/analysis_<N>.txt
```

Setup:

- **Shapes** (procedural): sphere; ellipsoid (1, 0.85, 0.7); torus (R = 1, r = 0.35); box (1, 0.8, 0.6); capped cylinder;
  `TpmsLattice` gyroid sheet and Schwarz P skeletal (2^3 cells); an anisotropic Gaussian point cloud ("blob").
- **Modes**:
  - "same": copies of one point set, like the vertices of a mesh;
  - "resample": fresh area-uniform samples for each copy.
- **Noise**: 0, 1e-3 or 1e-2, in units of shape size about 1.
- **Copies**:
  - Rigid copies get a random rotation, a scale in [1/2, 2] and a translation in [-10, 10]^3.
  - Deformations: stretch (x times 1+eps), a Gaussian bump of height eps, and a twist of eps rad per unit of z.
  - The reference is compared with 6 rigid copies and with 3 deformed copies per eps, eps in {1e-5, 3e-5, ..., 0.3}.
- **eps\*** is the smallest eps from which on every deformed copy is farther from the reference than every rigid copy.
- **Verdict**: "PDD" if the best PDD eps\* is at most half the best baseline eps\*, "BASE" the other way round.

| N = 2000: stratum | PDD (AMD/colW1, k = 8/32/128) better | D2/PCA better | tie | neither separates |
|---|---|---|---|---|
| same, noise 0 | 0 | 0 | 24 (all at the 1e-5 floor) | 0 |
| same, noise 1e-3 | 0 | 18 | 6 | 0 |
| same, noise 1e-2 | 1 | 20 | 3 | 0 |
| resample, noise 0 / 1e-3 / 1e-2 | 2 / 2 / 0 | 7 / 7 / 6 | 2 / 2 / 3 | 13 / 13 / 15 |
| **total** | **5** | **58** | 40 | 41 |

At N = 300 the variants that do not scale could also be run (`results/analysis_300.txt`, cases PDD / BASE):

| N = 300: PDD variant | PDD better | baseline better |
|---|---|---|
| AMD/colW1, k <= 128 | 9 | 43 |
| exact EMD, k = 32 | 5 | 47 |
| full PDD (k = N-1: EMD, colW1) | 32 | 20 |

The exact EMD adds nothing over its colW1 lower bound. Only the full PDD (k = N-1, the complete distance matrix row by row,
O(N^2) memory and time) wins more cases than it loses, and only by one ladder step (3e-1 against "not separated" in the
resampled bump cases; one step on noisy "same" sets).

### 4.2 Retrieval

```
dotnet run --project benchmarks/PddProbe -c Release -- retrieval 2000   # results/retrieval_2000.txt
dotnet run --project benchmarks/PddProbe -c Release -- retrieval 300    # results/retrieval_300.txt (adds exact EMD, k = N-1)
```

- **Corpus**: 13 shapes, including near twins: ellipsoid (1, 0.97, 0.94) against the sphere, torus r 0.33 against 0.35,
  box depth 0.62 against 0.6, cylinder height 1.45 against 1.5, and the gyroid, Schwarz P and Schwarz D lattices.
- **Queries**: 4 resampled rigid copies per shape with noise 1e-3.
- **top-1**: the nearest reference is the query's own shape.
- **FP**: (query, other shape) pairs at or below the threshold that gives no false negative.

| Metric | top-1, N = 2000 | FP of 624, N = 2000 | top-1, N = 300 | FP, N = 300 |
|---|---|---|---|---|
| AMD8 / colW1_8 | 38 / 36 | 104 / 123 | 25 / 27 | 266 / 304 |
| AMD32 / colW1_32 | 43 / 40 | 96 / 142 | 28 / 31 | 175 / 196 |
| AMD128 / colW1_128 | 43 / 43 | 97 / 85 | 33 / 33 | 91 / 127 |
| exact EMD, k = 32 | | | 31 | 212 |
| full PDD (EMD / colW1, k = N-1) | | | 42 / 42 | 526 / 529 |
| **D2** | **49** | **36** | **42** | 207 |
| PCA | 38 | 100 | 29 | 99 |

### 4.3 Why PDD loses here (diagnosis)

1. **With small k, a PDD row measures local sampling.** On a surface sampled with mean spacing h ~ sqrt(area / N), the
   k-NN distances are set by the Poisson sample and only weakly by the shape. A deformation of size eps changes them by
   about eps h. The global distances of D2 change by about eps, and they average over N^2 pairs.
2. **The same holds for noise of sigma.** It moves an NN distance by sigma relative to h, but a global distance by sigma
   relative to 1. That is why the "same, noisy" strata go to D2 (18 and 20 against 0 and 1).
3. **Completeness and the Lipschitz bound concern the exact point set.** On the exact set ("same", noise 0) every metric
   reaches the floor of the ladder, so completeness buys nothing over D2 for a metamorphic check of corresponding
   vertices.
4. **On resampled inputs every method is limited by the sampling noise.** In 41 of 144 cases at N = 2000 nothing
   separates even 30 % deformations: bumps and twists change D2, PDD and PCA by less than resampling does.

## 5. Symmetry and periodicity (b)

```
dotnet run --project benchmarks/PddProbe -c Release -- period    # results/period.txt
```

Setup: the vertices of a gyroid sheet `TpmsLattice` (cell size 1, 10 divisions, 1-3 cells per axis, plus one lattice
rotated by 37°), with k = 16.

- **Distinct rows** (L_inf tolerance 1e-9) count the vertex orbits.
- **PDD route**: the candidates are the vertices with the anchor's row; the shortest first, each verified by translating
  all vertices (99 % of the interior must match within 1e-6).
- **Baseline**: all vertices as candidates, capped at 400 verifications.

| Cells | Rotated | V | Distinct rows | PDD candidates | Verifications | ms | Basis found | Baseline: verifications, ms | Check of the known period, ms |
|---|---|---|---|---|---|---|---|---|---|
| 1 | no | 1032 | 349 | 6 | 5 | 12 | none | 401, 120, none | n/a |
| 2 | no | 7968 | 1604 | 162 | 132 | 251 | 3 x 0.866025 | 401, 982, none | 2 |
| 3 | no | 26568 | 4099 | 750 | 132 | 1208 | 3 x 0.866025 | 401, 4610, none | 13 |
| 2 | yes | 7968 | 5065 | 162 | 132 | 245 | 3 x 0.866025 | 401, 1008, none | 3 |

- **PDD finds a finer lattice than the nominal cell**: the body-centred vectors (±1/2, ±1/2, ±1/2), of length 0.866.
  The gyroid sheet {|f| <= t} is invariant under that translation (f(x + π(1,1,1)) = f(x), period 2π per cell). The
  rotated lattice gives the same basis.
- **The all-vertex baseline does not get there** within 400 verifications.
- **There is no consumer.** `TpmsLattice` takes its period as an input, so verifying it is one translation check
  (2-13 ms). An unknown-period search has no caller in the library or the tests today.
- **Symmetry by collapsed rows needs exact symmetry.** Rotation alone (5065 distinct rows instead of 1604) shows that
  rounding noise of the vertex coordinates breaks the 1e-9 grouping. On sampled clouds the collapse is undefined.

## 6. The stop and the next candidates (AGENT_WORKFLOW section 3: a stop ends the design, not the search)

The kill criterion of the brief: PDD is not clearly better than the simple baseline on (a) and (c), so the design stops.
The candidates the measurements point to, each with its bound measured in the same strata:

| Candidate | Bound (measured) | Clears? |
|---|---|---|
| Exact EMD instead of the column bounds | N = 300: 5 better / 47 worse vs. colW1 alone 9 / 43; O(m^3) | no |
| Full PDD (k = N-1) on a subsample of m points | N = 300: 32 / 20 cases (one ladder step); retrieval top-1 42/52 = D2, FP 526 vs 207; O(m^2) | no (no gain over D2 in (c), mixed in (a)) |
| PDD rows to prune a lattice-period search (b) | 132 verifications vs > 400; but the period of `TpmsLattice` is an input (2-13 ms check) | no consumer |
| D2 itself as the "same shape?" pre-check (c) | 49/52 top-1, 36 FP at N = 2000; 0.24 s for 1M pairs | it is the baseline: no new method; available to tests today |
| **Sampling-free moment invariants** (exact second moments of the surface from triangle integrals; for closed meshes the volume tensor of `MeshMeasurements.MassProperties`) | probe `moments` (below): stretch eps\* 1e-4 to 1e-2 against D2 3e-2 to 3e-1 on the non-degenerate shapes, 30-300x | **yes for global anisotropic changes**; different method, handed back |

```
dotnet run --project benchmarks/PddProbe -c Release -- moments    # results/moments.txt, 30 s
```

The reference is tessellation 0 of each shape. The positives are 6 rigid copies of 6 other tessellations (other grid
sizes, other `TpmsLattice` divisions 8-14), so they carry the tessellation spread. The negatives are deformed copies of 3 of
those tessellations. D2 and PDD run on 2000 area samples of the same meshes.

| Deformation | Shape | Moments | D2 | AMD32 | colW1_32 | PCA (samples) |
|---|---|---|---|---|---|---|
| stretch | ellipsoid / torus / cylinder / gyroid | 1e-4 / 3e-3 / 1e-3 / 1e-2 | 3e-2 / 3e-1 / 1e-1 / 3e-1 | 3e-1 / 1e-1 / 3e-1 / 3e-1 | 3e-1 / 1e-1 / 3e-1 / 3e-1 | 1e-1 / 1e-1 / 1e-1 / 1e-1 |
| bump | ellipsoid / torus / cylinder / gyroid | 1e-3 / 3e-2 / 3e-3 / 3e-1 | 3e-1 / - / - / - | - | - / - / 3e-1 / - | - |
| twist | ellipsoid / torus / cylinder / gyroid | 1e-1 / - / 1e-1 / - | - | - | - | - |

**Caveat, and why this is a lead, not a result.** On the sphere, box and Schwarz P the moments column reads 1e-5 even for a
twist of the sphere about its axis, which is no shape change. Those tessellations are exactly isotropic (cubic symmetry)
or geometrically identical (the box), so the positives have zero spread, and the invariant then sees the change of the
polyhedron, not of the shape. A real design would need a tolerance tied to the tessellation (a second-order error in the
edge length) and higher-order moments for changes the second moments cannot see (a torus twist is not detected). It is a
classical method, 3D moment invariants (Sadjadi & Hall 1980; Lo & Don 1989), not PDD, and outside this brief; the
coordinator decides whether it gets a brief of its own.

## 7. Corrections to the candidate note

- **"Reference Python code is MIT"**: not confirmed. The package shows CC BY-NC-SA 4.0 (a non-commercial Creative
  Commons licence, not a software licence; section 1). It is not a porting source under our rules either way.
- **"Generically complete, strictly stronger than pairwise-distance histograms"**: true for the exact point set, where
  D2 already reaches the floor (section 4.1). Under resampling, noise or modest k, the measured strength is below D2.
- **"Near-linear time"**: true for the kNN step and the AMD/colW1 bounds (section 3). The EMD itself is O(m^3) for
  generic sets.
