# Cellular sheaves and obstruction cohomology for the quad pipeline: spike (2026-11-22)

Research spike (owner go 2026-11-22), item 3 of [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md). No library code
changes. The prototype is [benchmarks/QuadSheafObstruction](../../benchmarks/QuadSheafObstruction/README.md); the pipeline it looks at is
stage 3B of [QUAD_REMESHING.md](../design/QUAD_REMESHING.md) (`QuadRemesher.PureQuads`, `QuadFlowRepair`).

**Question.** Can a sheaf Laplacian / cohomology computation on the cross field and the integer offsets of the position field predict, before
the extraction, where and why the lattice cannot be integrated (the folds and the `BudgetExceeded` fallbacks of `PureQuads`), and does that
give a cheaper or more reliable repair?

**Answer in short.**
- *Whether*: yes. One linear solve before the flow ranks the fallbacks almost perfectly on the 54 gate runs (AUC 0.97 with PR #1293, 0.92 on
  master).
- *Where*: no. The folds are spread over the surface; the marked places hold 20 to 42 % of them.
- *Why*: partly. The fallbacks that remain after PR #1293 are maps whose lattice area is far below the surface area. That area is a
  cohomological invariant of the flow's result, which the greedy moves and the SAT cannot change.
- *Gain*: small as a predictor. A perfect one saves about 0.2 s per failing input and converts at most 3 of 48 inputs, and trying a second
  configuration reaches the same 3 without it. The useful by-product is the area invariant: an exact integer check that also finds maps
  that pass as clean today although they keep 10 to 63 % of the cells.

## 1. Method, in our words

**Sheaf Laplacian** (Hansen and Ghrist 2019). A cellular sheaf puts a vector space on every cell of a complex and a linear map on every
incidence. Its coboundary `delta` compares the data of neighbouring cells, the Laplacians are `delta^T delta` and `delta delta^T`, their
kernels are the cohomology (`H^0`: the globally consistent sections), and small eigenvalues measure how far the data is from consistent. The
connection Laplacian of Singer and Wu (2012) is the special case of rotations as maps. The theory is over real vector spaces; it has no
integer or torsion part.

**The sheaf of the quad pipeline.** The stalk is the lattice plane (Z^2 for the offsets, R^2 for the relaxation), the maps are the quarter
turns between the frames of neighbours. Three objects of the library are already cochains of it:

| library object | in the sheaf's terms |
|---|---|
| `CrossField` energy (Knöppel et al. 2013) | the 0-th sheaf Laplacian of the rotation bundle; its smallest eigenvector is the field. Nothing to add |
| integer offset per triangle edge (`QuadFlowRepair`), in the frame of the triangle | a 1-cochain `d` with Z^2 values |
| "the three offsets of a triangle sum to zero" | `delta d = 0`; a triangle with a non-zero sum is a dislocation (its sum is the Burgers vector) |
| the curl mass of `IntegrableFrames` | the L1 norm of `delta g`, where `g` is the edge vector in the cross frame in lattice units (the real cochain) |
| the min-cost flow | the L1-smallest integer correction `c` with `delta (d + c) = 0`, on the columns that are network columns after the breadth-first rotation |
| the frozen edges (cycles with a holonomy) | the columns where the twist breaks total unimodularity. For untwisted coefficients Dey, Hirani and Krishnamoorthy (2011) tie total unimodularity to the absence of relative torsion; the twisted case is outside their theorem |
| a greedy vertex move, a SAT region | adding a coboundary, or a closed cochain supported inside a region: the cohomology class relative to the region's border stays |
| a fold (`det < 0`) | **not linear**: an inequality on pairs of offsets. No cohomology group contains it |

**Three statements that follow** (derived here; the first two are not in the papers read):
1. *No parity obstruction.* The only torsion a quarter-turn twist can produce is Z/2 (the lattice modulo the image of `R - I`), detected by
   the parity of `x + y`. That parity is the same for an offset and its rotated negative, and every edge enters two triangles, so the total
   is always even: closing all triangles over the integers is never obstructed by torsion. This matches `FlowInfeasible` on 0 of 54 runs.
2. *The lattice area is an invariant of the flow's result.* `A = (1/2) sum det(D0, D1)` over the triangles of a closed integer map is fixed
   by the offsets on the border of any region in which the map changes: a subdivision moves area between the two sides of the split edge, a
   vertex move and a SAT region keep their border, and around a cone the border fixes the cone's place in the lattice (`I - R` is
   invertible). So everything after the flow keeps `A`. If the repair succeeds, `A` is the number of quads plus the cancelled 2-gons. The
   prototype reads `A` from the final offsets; the equality of `A` before and after the repair is derived, not measured (it would need a
   hook in `QuadFlowRepair`).
3. *The filling energy.* `L = delta delta^T` on the triangles is 3 I on the diagonal and `-R^k` between neighbours (2 x 2 blocks), the
   connection Laplacian of the dual graph. With `L psi = delta g`, the cochain `g - delta^T psi` is the closed real map nearest to `g` in
   L2, and `E = (delta g)^T psi` is the squared size of that correction. `E` is small when dislocations of opposite sign are near each other
   or near a cone, and large when the curl must be carried far. It is the L2 companion of the flow's L1 cost, with the cones free to absorb
   (no frozen edge).

**Prior art** (search of 2026-11-22; arXiv pages and search results only, several publisher pages were not reachable).
- No paper was found that uses cellular sheaves, sheaf Laplacians or twisted cohomology to predict the failure of an integer-grid map.
  Terms searched: "sheaf Laplacian cross field", "twisted cohomology seamless parametrization", "local system quad mesh", "integer grid map
  cohomology", "Burgers vector quad mesh dislocation".
- How others solve it: the obstruction is stated as a holonomy signature (cone angles plus the quarter turns of the loops): Campen, Shen,
  Zhou, Zorin 2019 (a seamless map exists for admissible cones with one known exception); Shen et al. 2022. The integers are chosen on a
  T-mesh or motorcycle-graph partition, before the map is optimized: Campen, Bommes, Kobbelt 2015 (Diophantine systems); Lyon, Campen,
  Kobbelt 2021 (an ILP); Heistermann, Warnett, Bommes 2023 (a flow in a bi-directed graph, which is the published answer to the columns that
  are not network columns). Or the field is made integrable first: Diamanti et al. 2015; Capouellez and Zorin 2024; Couplet et al. 2026.
  None of them repairs per-edge offsets after the fact, which is what QuadriFlow (Huang et al. 2018) and our stage 3B do.

## 2. Fit with the library

- **Building blocks that exist** (search, AGENT_WORKFLOW sections 2 and 3): `SparseCholesky` with 2 x 2 blocks solves `L`; `PositionField`
  and `CrossField` expose what the complex needs; `IntegrableFrames.CurlBefore` already is the L1 norm of `delta g`; `QuadFlowRepair` has the
  integer offsets and the exact determinants. The prototype needed no new numeric code beyond assembling `L`.
- **Constraints.** CPU only, netstandard2.0 and net10.0: no issue (the solve is the library's Cholesky). Determinism: the area invariant is
  integer arithmetic; the solve is a serial Cholesky, deterministic like the cross field. No training.
- **Cost.** 0.11 s per run for the complex, the factorization and two solves (6.1 s over the 54 runs, 3k to 73k triangles), against 0.26 s
  for a failing pure-quad attempt with PR #1293. The area invariant costs one pass over the triangles.

## 3. Measurement (2026-11-22)

Setup: the 54 runs of `QuadFlow3BGate`, net10.0 Release, 4-core cloud container, once on master e340ef5c and once with the same benchmark
built against the head of PR #1293 (885b514b), which a separate session is integrating. Commands and the full tables: the benchmark README
and its `results_*.txt`. "Cell" is one lattice cell, the geometric area over the squared spacing.

### 3.1 Does it predict the fallback?

| quantity before the flow | AUC, master (25 clean / 29 fallback) | AUC, PR #1293 (43 / 11) | rows right with one threshold, PR #1293 |
|---|---|---|---|
| curl mass per cell (what `IntegrableFrames` has) | 0.65 | 0.76 | 44 / 54 |
| integer defect mass per cell | 0.67 | 0.84 | 46 / 54 |
| filling energy of the curl per cell | 0.82 | **0.97** | **51 / 54** |
| largest entry of the minimal correction | **0.92** | 0.95 | 50 / 54 |
| share of edges with a correction of half a cell or more | 0.89 | 0.96 | 51 / 54 |
| number of singular faces; number of triangles | 0.52; 0.50 | 0.43; 0.31 | 43; 46 |
| always the majority class | 0.50 | 0.50 | 43 / 54 |

The amount of curl does not decide; how far it must be carried does. Per shape the energy per cell is 0.002-0.004 on the filleted box,
0.02-0.10 on sphere, torus, genus 2 and bumpy sphere, 0.06-0.21 on the noisy sphere, 0.15-0.24 on the capped cylinder and 0.41-0.60 on the
capsule; the last two are the shapes that still fall back. The numbers are in sample (54 rows, 9 shapes), so they describe these inputs.

### 3.2 Does it say where?

Final folds of the fallback rows against the input triangles a quantity marks, within 1.5 s:

| marked set | master: folds near it (surface near it) | PR #1293: folds near it (surface near it) |
|---|---|---|
| singular faces | 17.5 % (3.7 %) | 30.2 % (3.4 %) |
| triangles with an integer defect | 43.3 % (33.1 %) | 58.1 % (32.7 %) |
| edges with a correction of half a cell or more | 19.8 % (5.5 %) | 41.9 % (5.4 %) |
| folds of the relaxed map | 6.9 % (1.1 %) | 7.0 % (0.8 %) |

The marked places are 4 to 8 times richer in folds than the surface, but most folds are elsewhere (1778 folds on master, 43 with PR #1293,
there none of the 18 on the capped cylinder is near a marked place). This agrees with the fold diagnosis on `runs/quad-folds`: every fold
sits on a flow correction and the corrections run across the whole surface. The falsification probe of the survey ("do the modes isolate the
faces that trigger the fallback") is answered with no.

### 3.3 Why: the area of the map

Lattice area of the library's final map over the geometric area in cells:

| | master | PR #1293 |
|---|---|---|
| clean rows: min / median / max | 0.79 / 0.96 / 1.07 | 0.10 / 0.94 / 1.14 |
| fallback rows: min / median / max | 0.07 / 0.69 / 1.18 | 0.06 / 0.49 / 0.75 |
| clean rows below 0.75 | 0 / 25 | 6 / 43 |
| fallback rows below 0.75 | 19 / 29 | 10 / 11 |

- With PR #1293 every fallback but one (0.754) is a map that lost more than a quarter of its cells in the flow: capsule 0.06-0.19, capped
  cylinder 0.49-0.75. The repair cannot bring the area back (statement 2), which is why a larger SAT budget never helped there
  (`runs/quad-folds`: 0 of 26 with 100 times the budget).
- The relaxed map with free cones keeps 0.68-0.78 of the area on the capsule and 0.87-0.92 on the capped cylinder (0.93-1.00 on the other
  shapes). So most of the capsule's loss comes from the frozen edges, as PR #1293 describes it (the barrel loop is locked to the sum of the
  fixed cap edges), and about a quarter is forced by the field itself: four cones at the pole cannot carry the barrel's lattice lines. The
  correction there reaches 1.7-3.5 cells per edge, so even the relaxed map is strongly distorted at the caps.
- **Clean but collapsed.** With PR #1293 six rows are `Success` and pass the independent check of the gate with an area ratio below 0.75: the
  capsule at scale 0.8 (0.095; corner deviation p95 65.8 degrees) and five of the noisy sphere and bumpy sphere rows (0.50-0.74). They are
  valid pure-quad meshes with far fewer quads than the target edge length asks for. No status reports this today. (On the noisy sphere the
  geometric area includes the noise, so part of its ratio is the input.)

### 3.4 Upper bounds

| use of a perfect predictor | best possible gain on the 54 runs (PR #1293) |
|---|---|
| skip the pure-quad attempt on inputs that will fall back | 2.4 s over 11 rows (2.9 s for the attempts, 0.5 s of it is the stage 2 the fallback needs anyway): 0.22 s per failing input. The real predictor costs 0.11 s on every input (6.1 s), more than it can save at a fallback rate of 20 % |
| stop the SAT on a map that cannot be repaired | at most the SAT share of a fallback, about 40 % of the attempt (the gate README): 0.1 s per failing input |
| choose the configuration (`CurvatureAlignment` 0 or 10, `SizeAdaptation` Off / Scale / ScaleAndRotation) per input | 45 of 48 inputs are clean with at least one configuration, 42 with the best single one (ScaleAndRotation): **3 inputs**. Trying the second configuration after a fallback reaches the same 45 without a predictor. All 3 are capsule rows that come out clean but collapsed (corner p95 33-62 degrees) |
| the same on master (alignment only) | 24 of 48 against 19 |

Three inputs stay fallbacks under every configuration (capsule at scale 1 and 1.5, capped cylinder at ratio 0.35).

## 4. Options, ranked

| # | option | what it gives | effort | risk |
|---|---|---|---|---|
| A | **Area ratio of the pure-quad map** as a reported number (`QuadRemesher`, exact integers, right after the flow), optionally a fallback reason below a threshold | names the cause of 10 of the 11 remaining fallbacks, finds the 6 clean-but-collapsed rows, and can end a hopeless repair before the SAT | S (a sum over the triangles, tests on capsule and sphere) | low for the number. As a fallback reason it changes which results count as `Success`: owner decision, threshold from the gate runs |
| B | Keep the benchmark as the diagnostic for stage 3B work (nothing in the library) | the energy per cell tells in 0.1 s whether a new shape is in the capsule class | none (this PR) | none |
| C | Integer jumps up front: let the cones absorb the curl (free or period-matched values on the frozen edges, or the bi-directed flow of Heistermann et al. 2023, from the paper) | upper bound from the relaxation: the capsule keeps 0.68-0.78 of its area instead of 0.06-0.19, the capped cylinder 0.87-0.92 instead of 0.49-0.75. Reaches the 3 inputs no lever reaches | L (a new solver for the non-network columns, hooks in `QuadFlowRepair`) | high: the correction at the caps is 1.7-3.5 cells, so the result may be valid and ugly; the integer problem is not a network flow |
| D | Sheaf-Laplacian predictor in the library (skip, or choose the lever) | at most 0.2 s per failing input or 3 of 48 inputs, both reachable without it | M | low, and no gain |
| E | Move the singularities (a field whose cones sit where the lattice lines must end) | the capsule's remaining quarter (3.3) is a field problem; `CurvatureAlignment` 10 does not solve it (3 rows, collapsed) | L, a separate topic (integrable or odeco fields, Couplet et al. 2026) | high |

**Leaning.** Build A after PR #1293 is merged, first as a reported number, with the fallback reason as a separate owner decision. Keep B.
Do not build D. C is the one option with a real upper bound on the remaining failures, but it is a solver project: if wanted, start with a
hooked prototype that only replaces the values of the frozen edges and measure it on the capsule and the capped cylinder before anything
else. E belongs to the cross-field roadmap, not here.

## 5. Limits of this spike

- 54 runs on 9 procedural closed shapes; the thresholds and AUC values are in sample.
- The numbers for PR #1293 are for its head 885b514b and may move with the integration.
- The area ratio is read after the repair; its equality with the value right after the flow is derived (statement 2), not measured.
- The fold locations are those of the final map, read by reflection from an internal class; the folds right after the flow were not
  available without a hook.
- The relaxation is L2 with free cones. The L1 integer problem with free cones (option C) was not solved, so its fold count is unknown.
- Open meshes are not covered (`PureQuads` rejects them).

## 6. Sources

Papers only; no code of Instant Meshes, QuadriFlow, QuadWild, LEMON, CoMISo or of the other authors below was read.

- J. Hansen, R. Ghrist, *Toward a spectral theory of cellular sheaves*, J. Appl. Comput. Topology 3, 2019, arXiv:1808.01513.
- A. Singer, H.-T. Wu, *Vector diffusion maps and the connection Laplacian*, CPAM 65(8), 2012, arXiv:1102.0075.
- T. K. Dey, A. N. Hirani, B. Krishnamoorthy, *Optimal homologous cycles, total unimodularity, and linear programming*, SIAM J. Comput.
  40(4), 2011, arXiv:1001.0338.
- J. Huang, Y. Zhou, M. Niessner, J. R. Shewchuk, L. J. Guibas, *QuadriFlow*, CGF 37(5), 2018 (as used in `QuadFlowRepair`).
- M. Campen, H. Shen, J. Zhou, D. Zorin, *Seamless Parametrization with Arbitrary Cones for Arbitrary Genus*, ACM TOG 39(1), 2019,
  arXiv:1810.02460 (abstract).
- H. Shen, L. Zhu, R. Capouellez, D. Panozzo, M. Campen, D. Zorin, *Which Cross Fields can be Quadrangulated?*, ACM TOG 41(4), 2022
  (search result only).
- M. Campen, D. Bommes, L. Kobbelt, *Quantized Global Parametrization*, ACM TOG 34(6), 2015; M. Lyon, M. Campen, L. Kobbelt, *Quad Layouts
  via Constrained T-Mesh Quantization*, CGF 40(2), 2021; M. Heistermann, J. Warnett, D. Bommes, *Min-Deviation-Flow in Bi-directed Graphs
  for T-Mesh Quantization*, ACM TOG 42(4), 2023 (search results only).
- O. Diamanti, A. Vaxman, D. Panozzo, O. Sorkine-Hornung, *Integrable PolyVector Fields*, ACM TOG 34(4), 2015 (search result only);
  R. Capouellez, D. Zorin, *Seamless Parametrization in Penner Coordinates*, ACM TOG 43(4), 2024, arXiv:2407.21342 (abstract);
  M. Couplet, A. Chemin, D. Bommes, E. Chien, *Surface Quadrilateral Meshing from Integrable Odeco Fields*, SGP 2026, arXiv:2604.03889
  (abstract).
