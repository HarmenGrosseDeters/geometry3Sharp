# Multiscale sparse (inverse) Cholesky as a preconditioner: spike

Date: 2026-11-22 (Berlin). Research note and a benchmark prototype only — no library code changed.

Topic: item 2 of [UNTAPPED_SCIENCE_GEOMETRY_2026-11.md](UNTAPPED_SCIENCE_GEOMETRY_2026-11.md) (operator-adapted sparse
inverse Cholesky: gamblets, KL minimisation), plus the second arm asked for in section 5.6 of
[CROSS_FIELD_TRANSFER_2026-11.md](CROSS_FIELD_TRANSFER_2026-11.md) (approximate elimination).

Question: for which of the library's systems (Laplacian, heat, bi-Laplacian, cross field, frames) would an approximate
sparse (inverse) factor, used as the preconditioner of a fixed-order deterministic conjugate gradient iteration, beat the
direct `SparseCholesky` in time or memory at our sizes, with results that are bit-identical across runs and thread counts?

## 1. Result in short

- **None of the methods of the papers gains time on a system of the library at the sizes measured** (Poisson to 1M
  unknowns, heat to 219k, bi-Laplacian to 205k, connection Laplacian to 438k). On surface meshes the exact factor with the
  opt-in nested dissection (#1300) is small (46 to 65 entries per unknown for the Laplacian), and one direct solve on 4
  cores costs about as much as 10 to 20 serial products with A. Only setup time and memory are left to win.
- **The KL-minimisation closed form does not apply to our problem.** It needs entries of the *inverse* of the matrix it
  factors (a covariance or Green's matrix); that is the argument. For a given sparse A it becomes FSAI. As a side check the
  probe ran FSAI on the pattern of the multiscale incomplete factor (not the wider pattern the theory gives for an inverse
  factor): 1.3 to 2.1 times the iterations of the incomplete Cholesky on that pattern.
- **The multiscale incomplete Cholesky (Chen et al. 2021) loses on every surface system.** On the Poisson, heat and
  connection systems, at the pattern width that gives few iterations (rho = 8), its factor is larger than our exact factor
  and its setup is 5 to 8 times slower than the whole direct factorisation. Its solve in the prototype is 50 to 270 times
  slower than the direct solve on Poisson and connection (9 times on heat); that number is the prototype's (section 5.1),
  the factor size is not. On the bi-Laplacian the prototype needs diagonal shifts and does not converge in 3000 iterations
  at 205k. The kill criterion of the survey (2x on the cold biharmonic case) is missed by a wide margin.
- **Approximate elimination (Kyng and Sachdeva 2016) is the only arm from the papers with a measurable gain, and the gain
  is memory**: 7 to 10 times fewer entries than the exact factor on the Poisson system, at a cold time (setup + one solve)
  of 1.6 to 2.1 times that of the direct solver. It only applies to Laplace-type systems, not to the bi-Laplacian, the
  connection Laplacian or the frames systems.
- **The heat system `M / t + L` at t = h² is so well conditioned that plain IC(0) solves it in 13 iterations**: 4 to 5
  times faster cold than a factorisation. That is a small opt-in option, independent of the multiscale papers.
- Leaning: **no build** from the papers of this spike. Details and options in sections 6 to 8.

## 2. The methods, in our words

Papers read in the text only; no code of any of them was read (licences in section 9).

**A. KL minimisation (Schäfer, Katzfuss, Owhadi 2021).** Given a dense positive definite matrix Θ (a kernel matrix, ie the
Green's matrix of an elliptic operator) and a sparsity pattern, the sparse triangular factor L that minimises the
Kullback-Leibler divergence between N(0, Θ) and N(0, (L Lᵀ)⁻¹) has a closed form: column i is the first column of the
inverse of the small dense block of Θ on the pattern of that column, scaled. The columns are independent of each other.
With the points ordered coarse to fine (maximin: each next point is the farthest from those taken) and the pattern "all
earlier-or-equal points within rho times the length scale of the point", the factor has O(N rho^d) entries and the error
decays exponentially in rho. The paper itself states that the formula is the one of the Vecchia approximation and of the
factorized sparse approximate inverse (FSAI) of Kolotilina and Yeremin, and of Kaporin's condition number minimisation.
The important point for us: **the input is the inverse of the matrix whose factor comes out.** The result is a factor of
Θ⁻¹ = A, computed from entries of Θ = A⁻¹. We have A and want to apply A⁻¹, which is the other direction.

**B. Sparse Cholesky factors of dense kernel matrices (Schäfer, Sullivan, Owhadi 2021).** The theory behind A and C: in the
maximin ordering the Cholesky factor of Θ is sparse up to an exponentially small error, with O(N log N rho^d) entries, and
in the reverse ordering the factor of the stiffness matrix A is sparse on the pattern with the *smaller* of the two length
scales, with O(N rho^d) entries. The second statement is the basis of C.

**C. Multiscale incomplete Cholesky (Chen, Schäfer, Huang, Desbrun 2021).** For a given sparse A: order the unknowns by
reverse maximin (fine first, coarse last), take the pattern `dist(i, j) <= rho * min(l_i, l_j)`, and compute a zero fill-in
incomplete Cholesky on that pattern; use it as the preconditioner of CG. The paper adds supernodes, a colouring for parallel
factorisation and a diagonal shift when a pivot fails. It recommends rho = 6.5 to 8.5 in 2D and 2.5 to 4 in 3D, and its
target is large, badly conditioned 3D problems (heterogeneous elasticity). In the paper's words: "For small to intermediate
size problems, Cholmod is vastly preferable for its computational efficiency". Its own table gives, for a 2D Poisson
problem with 500k unknowns at rho = 7.5, a factor of 2.26e8 entries (about 450 per unknown, 1.69 GB) and 24 s of
precomputation (ordering, pattern, colouring) next to 4.2 s of factorisation; the comparisons of the paper leave the
precomputation out. Our exact nested dissection factor at 1M unknowns has 65 entries per unknown and takes 4.1 s in total.
The paper has no bi-Laplacian test and does not discuss bit reproducibility.

**D. Sparse recovery of elliptic solvers from matrix-vector products (Schäfer, Owhadi 2024)** builds the inverse factor
from O(log N log^d(N/eps)) solves with a black-box solver. It needs an existing solver for A; it is a compression method
for a solution operator, not a way to solve a given sparse system faster. **Chen, Owhadi, Schäfer 2025** extends A to
kernel matrices with derivative entries (PDE solving with Gaussian processes); not our problem.

**E. Approximate elimination (Kyng, Sachdeva 2016; Gao, Kyng, Spielman 2023).** For a graph Laplacian: eliminate the
vertices one after the other, and where exact elimination would add a clique among the neighbours of the eliminated vertex,
add a random sparse sample of that clique whose expectation is the clique (one edge per neighbour). The factor stays as
sparse as the graph, and it is a preconditioner with a logarithmic iteration bound in theory. The 2023 paper describes the
engineering (ordering by current degree, merging of multi-edges, sorting the neighbours by weight). It needs a symmetric
diagonally dominant matrix with non-positive off-diagonals.

## 3. Fit with the library

Building blocks that exist: `SparseSymmetricCG` (with a preconditioner callback), `SparseCholesky` with the default minimum
degree ordering and the opt-in `CholeskyOrdering.NestedDissection` (parallel numeric phase and solves, bit-identical for
every thread count), `SparseHermitianCholesky`, `PackedSparseMatrix`, `SymmetricSparseMatrix`. Missing for any of the
methods: an incomplete factorisation, a farthest-point ordering with length scales, a range search for the pattern.

How the users solve:

| user | system | solves per factor | what counts |
|---|---|---|---|
| `BiharmonicMeshDeformer` | `Lᵀ M⁻¹ L` on the free vertices | 3 (x, y, z), more on handle moves | cold factor time (#980, #976) |
| `CrossField` | Hermitian connection Laplacian + 1e-8 mass shift | 6 to 34 (inverse iteration) | factor + many solves |
| `MeshGeodesicDistance`, `VectorHeatMethod` | heat `M / t + L`, then Poisson | 1 per source, factors cached | cold factor, then solve time |
| `IntegrableFrames` | `JᵀJ + 0.01 I`, refactored every few steps | several | factor time |
| `ArapMeshDeformer` | Laplacian | one per iteration, many | solve time |

Every user except a single cold geodesic query solves several times per factor, which is the case where a direct factor is
hardest to beat. `SolveLog` of the heat method (log-domain substitution) needs the triangular factor itself.

Constraints:

- CPU only, netstandard2.0 + net10.0: no obstacle for any arm; all of them are scalar loops.
- Bit-for-bit determinism: achievable for all arms. FSAI rows are independent; the incomplete factorisation is serial or
  level-scheduled; approximate elimination is serial with a seeded generator; the CG sums in a fixed order. The probe is
  serial (except the FSAI setup) and its iteration counts repeat exactly between runs. But **a preconditioned iteration
  returns different bits than the direct solver**, so any use in an existing operation is opt-in (owner decision: no
  default solver result changes).
- No neural training: none involved.
- Libm rule: none of the arms needs transcendental functions in the solver.

## 4. Prior art: how others solve these systems

- Geometry processing libraries (libigl, geometry-central, CGAL's solver traits, Blender's deformers) use a direct sparse
  Cholesky (CHOLMOD, Eigen, Pardiso, Accelerate) for exactly these systems, with the factor cached across solves.
- Multigrid on surfaces (Liu et al. 2021; Wiersma et al. 2023, Gravo MG) is the published alternative for one or few
  solves. Gravo MG reports, for a Poisson problem at tolerance 1e-4: 152k vertices 0.14 + 0.19 s (setup + solve) against
  0.58 + 0.04 s for Pardiso; 1M vertices 0.89 + 0.94 s against 4.54 + 0.22 s. The same paper: "A direct solver is often
  preferable in settings where high accuracy is required." Our direct solver with nested dissection is at 0.15 s (54k) and
  4.2 s (1M) cold on 4 cores; the two sets of numbers are from different machines and tolerances, so this only says that
  multigrid is the candidate with a published gain of this kind, not how large it would be here.
- Hierarchical sparsification (Krishnan, Fattal, Szeliski 2013) and approximate elimination (E) are for M-matrices only.
- Not verified in this spike: the numbers of Botsch, Bommes, Kobbelt 2005 (direct against multigrid on meshes); the hosts
  were not reachable.

## 5. Measurements

Prototype: [benchmarks/MultiscaleCholeskyProbe](../../benchmarks/MultiscaleCholeskyProbe/README.md) (all tables and the
commands are there). Procedural systems on a bumpy cube sphere and a 3D grid; 4 cores; the direct solver uses them, the
preconditioned arms are serial (except the FSAI setup). "Cold" is setup plus one solve to a relative residual of 1e-8;
"entries" are the stored numbers of the factor or preconditioner. "Direct" is `SparseCholesky` with the opt-in nested
dissection (the default minimum degree ordering is 1.9 to 3.7 times slower to factor on these inputs). Timings of a shared
machine: ratios below 1.5 mean nothing. For the approximate elimination and IC(0) arms the numbers are an upper bound of
what a tuned implementation would need; for the multiscale arm see section 5.1.

**Poisson (cotan Laplacian, one pinned vertex)**

| n | method | setup (s) | entries | iterations | solve (ms) | cold (s) |
|---|---|---|---|---|---|---|
| 54k | direct | 0.14 | 2.51M | 1 | 6.4 | 0.15 |
| 54k | IC(0) | 0.01 | 0.22M | 286 | 529 | 0.54 |
| 54k | multiscale IC rho=4 | 0.24 | 0.95M | 139 | 661 | 0.90 |
| 54k | multiscale IC rho=8 | 0.96 | 3.68M | 31 | 315 | 1.28 |
| 54k | multiscale FSAI rho=4 | 0.32 | 0.95M | 297 | 1222 | 1.54 |
| 54k | approximate elimination | 0.15 | 0.37M | 38 | 87 | 0.24 |
| 219k | direct | 0.56 | 12.17M | 1 | 29 | 0.59 |
| 219k | IC(0) | 0.03 | 0.88M | 582 | 4127 | 4.16 |
| 219k | multiscale IC rho=8 | 4.24 | 15.04M | 59 | 4356 | 8.60 |
| 219k | approximate elimination | 0.80 | 1.49M | 43 | 458 | 1.26 |
| 999k | direct | 4.07 | 65.46M | 1 | 149 | 4.22 |
| 999k | multiscale IC rho=8 | 20.13 | 68.49M | 127 | 40341 | 60.5 |
| 999k | approximate elimination | 5.22 | 6.82M | 51 | 3154 | 8.37 |

At rho = 8 the multiscale factor has more entries than the exact one. That part does not depend on the prototype: the
paper's own table has about 450 entries per unknown for 2D Poisson. The iteration count of the prototype doubles per
refinement (31, 59, 127), like IC(0) (286, 582, 1164); a multiscale factor of sufficient width should be nearly
independent of n, so the prototype is below that width (section 5.1).

**Heat (`M / h² + L`, the library default `TimeFactor` = 1)**

| n | method | setup (s) | entries | iterations | solve (ms) | cold (s) |
|---|---|---|---|---|---|---|
| 54k | direct | 0.15 | 2.52M | 1 | 6.3 | 0.16 |
| 54k | Jacobi | 0 | 0.05M | 42 | 43 | 0.04 |
| 54k | IC(0) | 0.01 | 0.22M | 13 | 24 | 0.03 |
| 54k | multiscale IC rho=4 | 0.22 | 0.95M | 8 | 32 | 0.25 |
| 219k | direct | 0.57 | 12.07M | 1 | 31 | 0.60 |
| 219k | IC(0) | 0.03 | 0.88M | 13 | 109 | 0.14 |
| 219k | multiscale IC rho=4 | 1.04 | 3.84M | 8 | 153 | 1.19 |

The iteration count of IC(0) does not grow with n here (the time step scales with h²). The multiscale pattern buys nothing
over IC(0). A larger `TimeFactor` makes the system worse conditioned and was not measured.

**Bi-Laplacian (`L M⁻¹ L`, fixed caps)**

| n | method | setup (s) | entries | iterations | solve (ms) |
|---|---|---|---|---|---|
| 51k | direct | 0.34 | 6.76M | 1 | 17.9 |
| 51k | IC(0), shift 0.064 | 0.02 | 0.51M | not in 3000 | - |
| 51k | multiscale IC rho=8, shift 0.004 | 1.79 | 3.42M | 2550 | 27352 |
| 205k | direct | 1.72 | 34.29M | 1 | 71 |
| 205k | multiscale IC rho=8, shift 0.016 | 8.74 | 14.06M | not in 3000 | - |

Here the incomplete factors are smaller than the exact one, but every one of them needed a diagonal shift under the
prototype's rule (relative shift 0.001, times 4 until no pivot fails, so the needed shift may be up to 4 times smaller than
the one reported), and the FSAI arm, which needs no shift, does not converge in 3000 iterations either. The condition
number is the square of the Laplacian's (at about 9k unknowns all arms still converge, in about 1000 iterations). Approximate
elimination does not apply: 24 % of the off-diagonal weight is positive. The one measured gain for this system is the one
already merged: nested dissection against minimum degree (0.74 s to 0.34 s at 51k, 4.63 s to 1.72 s at 205k).

**Connection Laplacian (two real unknowns per vertex, the cross-field system)**

| n | method | setup (s) | entries | iterations | solve (ms) |
|---|---|---|---|---|---|
| 108k | direct | 0.42 | 10.01M | 1 | 30 |
| 108k | IC(0) | 0.02 | 0.81M | 225 | 1073 |
| 108k | multiscale IC rho=8 | 3.03 | 14.68M | 23 | 1590 |
| 438k | direct | 2.26 | 48.02M | 1 | 105 |
| 438k | multiscale IC rho=8 | 12.57 | 59.92M | 46 | 13716 |

With 6 to 34 solves per factor in `CrossField`, the direct solver wins by one to two orders of magnitude.

**3D grid Laplacian (control; no library user)**

| n | method | setup (s) | entries | iterations | solve (ms) | cold (s) |
|---|---|---|---|---|---|---|
| 111k | direct | 2.63 | 39.05M | 1 | 193 | 2.82 |
| 111k | IC(0) | 0.01 | 0.44M | 58 | 207 | 0.22 |
| 111k | approximate elimination | 0.52 | 0.95M | 25 | 178 | 0.70 |
| 111k | multiscale IC rho=4 | 2.62 | 7.67M | 20 | 811 | 3.43 |

This is where iterative methods win (the exact factor has 350 entries per unknown and grows faster than n), and it is the
setting of the papers. The library has no volumetric user of `SparseCholesky` today. Even here the plain IC(0) is the
fastest cold arm at this size.

**Frames systems** (`JᵀJ + 0.01 I` of `IntegrableFrames`) were not built in the probe. They are a normal-equations matrix
like the bi-Laplacian (squared condition number, mixed signs), refactored and solved several times; by the bi-Laplacian and
connection results there is no reason to expect a gain.

### 5.1 Caveats of the prototype

- **The multiscale arm is not an upper bound of the method's solve time.** The coarse-to-fine sampling is a level-wise
  stand-in for the maximin ordering, and its length scale is up to 2 times below the true spacing, so rho = 8 here is 4 to
  8 in the paper's terms, at or below its 2D range; the growth of the iterations with n shows it. A true maximin ordering
  at the paper's rho would need fewer iterations and more entries per row. The verdict on this arm therefore rests on the
  part that does not depend on the prototype: by the paper's own table the factor is several times larger than our exact
  nested dissection factor on a surface, and one application of it per iteration cannot be cheaper than one exact solve.
- The FSAI arm runs on the pattern of the incomplete factor, not on the wider pattern (larger of the two length scales,
  O(N log N rho^d) entries) that the theory gives for a factor of the inverse. It is a side check, not a test of method A.
- No supernodes, no parallel triangular solves: the solve times of the preconditioned arms could drop by a factor of 2 to
  4 with engineering. For the multiscale arm the gap to the direct solve is 50x and more. For approximate elimination it
  is 14x to 21x per solve, so such a gain would matter there for the cold time, not for many solves per factor.
- Approximate elimination: one seed, no tuning (the papers split edges or keep several samples to trade entries for
  iterations); its setup time grows faster than n in the prototype (priority queue and adjacency lists with poor locality).
- Setups and iterations are timed once, on a shared machine (see the README of the probe).

## 6. Answer per system

| system | answer |
|---|---|
| Laplacian / Poisson | Time: no (direct is 1.6 to 2.1 times faster cold than the best arm, to 1M). Memory: approximate elimination, 7 to 10 times fewer entries. |
| Heat `M / t + L` | Yes at t = h², but with plain IC(0) or Jacobi, not with a multiscale factor: 4 to 5 times faster cold. |
| Bi-Laplacian | No. No arm converges in useful time. |
| Cross field (connection Laplacian) | No. Many solves per factor; iterations lose by 10x to 100x. |
| Frames (`JᵀJ`) | Not measured; no reason to expect a gain. |

## 7. Options, ranked

| # | option | effort | risk | measured gain |
|---|---|---|---|---|
| 1 | No build. Keep the direct solver; the probe and this note stay as the record. | none | none | - |
| 2 | Opt-in IC(0)-preconditioned CG for the heat step (`M / t + L`) of the heat method, for cold single-source queries. | small (an IC(0) of about 150 lines, `SparseSymmetricCG` exists; tests for determinism and tolerance) | Result bits differ from the direct path, so opt-in only. `SolveLog` and cached multi-source use still need the factor. Conditioning at a larger `TimeFactor` must be measured first. The Poisson solve of the same query stays direct, so a cold query saves less than half of its factor time. | 0.03 s against 0.16 s (54k), 0.14 s against 0.60 s (219k) for the heat solve alone |
| 3 | Approximate elimination as an opt-in low-memory solver for Laplace-type systems. | medium (about 400 lines, seeded, serial; an invariance and a determinism test; a guard for non-Delaunay input) | Randomised quality (seeded, so reproducible, but no worst-case bound we can test); positive cotan weights must be dropped or the mesh made intrinsic Delaunay first; slower than direct at every size measured. | 7 to 10 times fewer factor entries (6.8M against 65.5M at 1M; peak memory of the setup not measured); cold time 1.6x to 2.1x of direct |
| 4 | Multiscale incomplete Cholesky (paper C) with supernodes and a true maximin ordering. | large (weeks: ordering, pattern search, supernodal incomplete factor, parallel schedule) | Factor larger than the exact one on surfaces by the paper's own numbers; needs shifts on the bi-Laplacian. | none on our systems; only on 3D volumes, where IC(0) is as good at our sizes |
| 5 | KL-minimisation inverse factor (paper A) of the solution operator. | large | Needs entries of A⁻¹, ie a solver; on the entries of A it is FSAI on a pattern with O(N log N rho^d) entries. | none |

## 8. Leaning

Option 1: **no build.** The premise of the survey item (direct Cholesky "stalls on fill-in" for our systems) does not hold
for surface meshes at our sizes once nested dissection is on: for the Laplace-type and connection systems the exact factor
is no larger than the multiscale one that gives few iterations. The cold biharmonic latency (#980, #976), which motivated
the item, is not helped by any arm; the lever there remains the ordering and the factorisation itself.

Option 2 is cheap, but its gain is 0.1 to 0.5 s on one of two factorisations of one use case, at different result bits;
worth a go only if cold single-source geodesic queries on large meshes become a stated need. Option 3 is worth a second
look when a user with several million unknowns or a 3D volumetric system (tet Laplacian) appears; multigrid (section 4)
would then be the alternative to compare against. Options 4 and 5 are no-go.

## 9. Sources and licences

| source | used for | code |
|---|---|---|
| F. Schäfer, M. Katzfuss, H. Owhadi, *Sparse Cholesky Factorization by Kullback-Leibler Minimization*, SIAM J. Sci. Comput. 43(3), 2021, arXiv:2004.14455 | method A, the FSAI arm | paper only (an MIT implementation exists; not read) |
| F. Schäfer, T. J. Sullivan, H. Owhadi, *Compression, Inversion, and Approximate PCA of Dense Kernel Matrices at Near-Linear Computational Complexity*, Multiscale Model. Simul. 19(2), 2021, arXiv:1706.02205 | ordering and pattern theory | paper only |
| J. Chen, F. Schäfer, J. Huang, M. Desbrun, *Multiscale Cholesky Preconditioning for Ill-Conditioned Problems*, ACM Trans. Graph. 40(4), 2021 | method C, the multiscale IC arm | paper only (the authors' code is GPL v3; never read) |
| F. Schäfer, H. Owhadi, *Sparse Recovery of Elliptic Solvers from Matrix-Vector Products*, SIAM J. Sci. Comput. 46(2), 2024, arXiv:2110.05351 | method D (not applicable) | paper only |
| Y. Chen, H. Owhadi, F. Schäfer, *Sparse Cholesky Factorization for Solving Nonlinear PDEs via Gaussian Processes*, Math. Comp. 2025, arXiv:2304.01294 | checked, not applicable | paper only |
| L. Yu. Kolotilina, A. Yu. Yeremin, *Factorized Sparse Approximate Inverse Preconditionings I. Theory*, SIAM J. Matrix Anal. Appl. 14(1), 1993 | FSAI | as cited by A |
| R. Kyng, S. Sachdeva, *Approximate Gaussian Elimination for Laplacians: Fast, Sparse, and Simple*, FOCS 2016, arXiv:1605.02353; Y. Gao, R. Kyng, D. Spielman, *Robust and Practical Solution of Laplacian Equations by Approximate Elimination*, arXiv:2303.00709 | method E, the approximate elimination arm | papers only |
| R. Wiersma, A. Nasikun, E. Eisemann, K. Hildebrandt, *A Fast Geometric Multigrid Method for Curved Surfaces*, SIGGRAPH 2023; H. Liu, J. Zhang, M. Ben-Chen, A. Jacobson, *Surface Multigrid via Intrinsic Prolongation*, ACM Trans. Graph. 40(4), 2021; D. Krishnan, R. Fattal, R. Szeliski, *Efficient Preconditioning of Laplacian Matrices for Computer Graphics*, ACM Trans. Graph. 32(4), 2013 | prior art | papers only |

No code of CHOLMOD, SuiteSparse, MUMPS, PARDISO, METIS or Eigen's sparse solvers was read.
