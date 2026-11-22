# SparseCholeskyBenchmark

`SparseCholesky.Factorize()` (minimum-degree ordering, symbolic part, numeric part) and `Solve()` on procedural symmetric
positive definite matrices with the structure of mesh systems: a triangulated grid with 1 to 3 unknowns per node (`bs=2` is the
shape of the interleaved 2n x 2n `CrossField` system) and a 3D grid. The last column is a hash of the solution vector; it must be
the same before and after a change of the kernels and on every machine (the benchmark compares it with the values below and exits with 1 on a difference) (the dense kernels give the same bits as the scalar code,
`SparseCholeskyKernelTests`).

```
dotnet run -c Release --project benchmarks/SparseCholeskyBenchmark -- [only=<regex>] [reps=3]
```

## Results (2026-11-17, net10.0 Release, one thread, 4-core Xeon at 2.1 GHz in a cloud container, best of 3)

Scalar dot kernels (master at da564d55) against the vector kernels (`SparseCholeskyKernels`: the four partial sums of
the scalar dot in the four lanes of a `Vector256`, four dot products at once in the update and the dense factorization of the
supernodes). `Solve` is unchanged (its loops are one sequential sum per entry).

| matrix | nnz(L) | Factorize before (s) | Factorize after (s) | Solve before / after (ms) | solution hash (both) |
|---|---|---|---|---|---|
| grid2d bs=1 n=40000 | 1.6M | 0.22 | 0.19 | 6.3 / 6.3 | 5af59d2818b8f21d |
| grid2d bs=2 n=80000 (CrossField-like) | 6.3M | 0.84 | 0.52 | 32.9 / 33.5 | ce7f838b19a99e97 |
| grid2d bs=2 n=120000 (CrossField-like) | 9.9M | 1.29 | 0.79 | 51.2 / 53.0 | 34e470467305cff4 |
| grid3d n=27000 | 6.1M | 1.99 | 1.04 | 43.7 / 45.1 | 641b48317c4b9c92 |

Factorize includes the serial analysis (ordering and symbolic part, about 0.4 s per 100k unknowns), which the kernels do not touch. End to end, `CrossFieldBenchmark h=0.017` (one thread, `reps=1`):

| shape | field | V | before (s) | after (s) |
|---|---|---|---|---|
| sphere | smoothest | 54944 | 5.3 to 5.5 | 3.0 |
| sphere | aligned s=1 | 54944 | 5.5 to 6.0 | 3.1 |
| torus | smoothest | 60712 | 5.9 to 6.3 | 3.6 |
| torus | aligned s=1 | 60712 | 5.6 to 6.1 | 3.6 |

(The field, the eigenvalues and the singular faces are unchanged.)

## Shared analysis (`CholeskyAnalysis`, 2026-11-17)

`SparseCholesky` and `SparseHermitianCholesky` share one analysis (ordering, elimination tree, structure of L, supernodes). The
tree is now found from the pattern of A (Liu's algorithm), so the structure of L is computed once, for the postordered matrix,
instead of twice. The arrays, the factor and the solutions are the same bits as before (`CholeskyAnalysisTests`, golden hashes of
the previous code; the benchmark compares the solution hashes with those of the previous code and exits with 1 on a difference).
Factorize (s), best of 3, same machine, before / after:

| matrix | before | after |
|---|---|---|
| grid2d bs=1 n=40000 | 0.17 | 0.15 |
| grid2d bs=2 n=80000 | 0.48 | 0.45 |
| grid2d bs=2 n=120000 | 0.75 | 0.65 |
| grid3d n=27000 | 0.97 | 0.89 |
| hermitian grid2d n=40000 | 0.28 | 0.27 |
| hermitian grid2d n=60000 | 0.43 | 0.42 |

`CrossFieldBenchmark h=0.017` (one thread, best of 3, s, `SparseHermitianCholesky`): sphere smoothest 1.66 -> 1.61, sphere aligned
1.80 -> 1.63, torus smoothest 2.08 -> 2.06, torus aligned 2.14 -> 1.95. The gain is small because the analysis of the 55k-vertex
system is now mostly the minimum degree ordering (0.26 s of 0.33 s; the structure of L takes 0.05 s); a faster ordering with the
same result is the next lever of the analysis.

## Vector kernels of `SparseHermitianCholesky` (2026-11-17)

The complex dot product of the supernode updates and of the dense block Cholesky (`SparseHermitianCholeskyKernels`) runs four products
at a time on `Vector128` lanes, like `SparseCholeskyKernels` for the real class. The scalar `dotc` keeps two partial sums per
component (element k into sum k mod 2); the vector version holds the two sums in the two lanes, with the same separate multiplies
and adds (no FMA), so factor and solutions are the same bits (the golden hashes of `CholeskyAnalysisTests`, taken before
the analysis was shared, still hold; `SparseHermitianCholeskyKernelTests`).

Factorize (s), best of 3, master at 464988c8 / this change: hermitian grid2d n=40000 0.26 / 0.20, n=60000 0.43 / 0.33 (solution hashes
unchanged). `CrossFieldBenchmark h=0.017` (one thread, best of 3, s, before / after):

| shape | field | V | solves | before | after |
|---|---|---|---|---|---|
| sphere | smoothest | 54944 | 6 | 1.61 | 1.36 |
| sphere | aligned s=1 | 54944 | 6 | 1.71 | 1.37 |
| torus | smoothest | 60712 | 14 | 2.02 | 1.72 |
| torus | aligned s=1 | 60712 | 12 | 2.02 | 1.66 |
| noisy-torus | smoothest | 60712 | 34 | 3.24 | 2.90 |
| noisy-torus | aligned s=1 | 60712 | 21 | 2.43 | 2.16 |

Where the time goes after this change (same runs, with temporary stopwatch prints, s): sphere 1.4 = ordering 0.22 + rest of the
analysis 0.08 + numeric 0.48 + 6 solves 0.28 + other 0.33 (frames, curvature, assembly, per face results); torus smoothest
1.7 to 1.8 = ordering 0.24 + 0.08 + numeric 0.5 + 14 solves 0.5 + other 0.35; noisy torus smoothest 2.9 to 3.0 = ordering 0.23 + 0.07 +
numeric 0.5 + 34 solves 1.2 + other 0.33. The minimum degree ordering alone is 8 % to 16 % of the total, so a free ordering would save at
most 0.22 to 0.26 s.

## What is left (2026-11-18)

The speed sequence of `CrossField` stops here (the 2 s per 50k vertices gate is met serially on the sphere and the torus; see
[QUAD_REMESHING.md](../../docs/design/QUAD_REMESHING.md) 4.1). The measured upper bounds of the remaining levers, on the 55k to 61k
vertex systems above (one thread now, 4 cores for the bounds, ideal list scheduling of the supernode tree with the measured time of each
supernode, no overhead):

| lever | now (s) | upper bound (s) | note |
|---|---|---|---|
| parallel solves | 0.28 (6 solves) to 1.2 (34) | about 0.1 to 0.4 | needs per-supernode contribution buffers for a fixed order |
| parallel numeric factorization | 0.5 | about 0.15 | subtree parallelism alone 1.4x (critical path 70 %); parallel dense kernels in the root supernodes needed |
| nested dissection ordering | | 1.8x fewer flops, critical path 49 % instead of 68 % | changes the factor and the results: owner decision |
| faster minimum degree with the same output | 0.22 to 0.26 | at most 0.25 | 8 % to 16 % of the total |
| per-vertex and per-face loops of `CrossField` | 0.33 | about 0.1 | `gParallel`, no change of bits |

The root supernode of the factor holds 36-56 % of the numeric time (the real 2n form on a 55k sphere: width 1764, 57 %), and
the supernode tree has 30 000 supernodes of which 25 000 are narrower than 8 columns (0.8 % of the time), so a task per supernode
is too fine below a threshold and the parallel work belongs in the few large ones. A dot kernel with a single vector chain
is not faster than the four-accumulator scalar one (the add latency bounds both); the gain of the vector kernels comes from running four
independent products at once.

## Parallel solves (2026-11-18)

`Solve()` of `SparseCholesky` and `SparseHermitianCholesky` runs subtrees of the supernode tree as parallel tasks
(`CholeskySolveSchedule`). Forward solve: the tasks run in parallel; a supernode adds its updates to the rows inside its task in
place and writes the updates for the rows above the task into a per-supernode buffer; one serial pass in supernode order then
applies the buffers and does the top supernodes, so every entry gets its subtractions in the order of the serial loop. Backward
solve: the top supernodes first, then the tasks in parallel. The solutions are the same bits for every schedule and thread count
(`CholeskyParallelSolveTests`, which compare with the old serial loops; the hashes of this benchmark are unchanged).

The top supernodes (the 25 supernodes above the tasks on a 55k vertex system hold about half of the factor entries) dominate what
is left, and the backward substitution reads a column of a row-major block, a cache miss per entry in a wide block (7.6 ms for the
961 wide root alone, 16 ns per entry; a loop of the same chain in cache takes 1.4 ns). Blocks at least 64 columns wide are read
from a column-major copy, built in parallel on the first `Solve` after a factorization (about 24 MB on the 55k sphere; only
for systems with at least 200 000 entries in such blocks, so small systems keep the old path and memory). The copy only moves values.

`SparseCholeskyBenchmark`, best of 3, 4 cores, Solve (ms), before (master 3a991801) / after (parallel / forced serial tasks, which
still reads the column copy):

| matrix | before | after parallel | after, tasks off |
|---|---|---|---|
| grid2d bs=1 n=40000 | 6.9 | 3.9 | 4.7 |
| grid2d bs=2 n=80000 | 35.6 | 19.3 | 29.6 |
| grid2d bs=2 n=120000 | 54.8 | 28.9 | 48.9 |
| grid3d n=27000 | 47.7 | 25.1 | 29.2 |
| hermitian grid2d n=40000 | 17.1 | 5.8 | 8.5 |
| hermitian grid2d n=60000 | 24.1 | 10.7 | 19.0 |

`CrossFieldBenchmark h=0.017` (4 cores, best of 3, s, master / this change, two interleaved runs, the better of each):

| shape | field | V | solves | before | after | gain |
|---|---|---|---|---|---|---|
| sphere | smoothest | 54944 | 6 | 1.33 | 1.18 | 11 % |
| sphere | aligned s=1 | 54944 | 6 | 1.38 | 1.21 | 12 % |
| torus | smoothest | 60712 | 14 | 1.72 | 1.47 | 15 % |
| torus | aligned s=1 | 60712 | 12 | 1.74 | 1.47 | 16 % |
| noisy-sphere | smoothest | 54944 | 22 | 2.21 | 1.66 | 25 % |
| noisy-torus | smoothest | 60712 | 34 | 3.36 | 2.44 | 27 % |
| noisy-torus | aligned s=1 | 60712 | 21 | 2.25 | 1.87 | 17 % |

Left in the solves: the serial top supernodes (the forward pass over them took about 7 ms per solve on the sphere before the column copy; the backward pass was not timed again after it; the rows below the
diagonal block of a top supernode are independent and could run in parallel, the triangle itself is one chain per column).

## Parallel numeric factorization (2026-11-18)

`Factorize()` of both classes runs the numeric part in parallel when its work is at least 6 million multiplications
(`CholeskyNumericPlan`). The serial left-looking loop keeps its updates in linked lists (`head`, `link`, `pos`), whose order fixes the
order of the subtractions in every entry of L. The plan replays that loop on the structure only (no values) and keeps, for every supernode J,
the table of its updates (K, row range) in exactly that order. With the table, J only reads supernodes of its own subtree, so:

- subtrees (tasks of at most 1/48 of the work, adjacent ones joined) run in parallel, each worker with its own `relmap`;
- the supernodes above the tasks run in ascending order; an update larger than 300 000 multiplications splits its rows over the
  threads (every row writes its own row of the block of J), and a block at least 64 columns wide is factorized in panels of 32 columns:
  the small triangle of the panel row by row, then the rows below the panel, which are independent of each other, in parallel.
  Entry (i, c) is `(L[i,c] - dot(L[i,:c], L[c,:c])) * inv[c]` with the same dot kernel as the serial loop, so it has the same bits;
- the failure of a matrix that is not positive definite is the one the serial loop reports (the failing supernode with the smallest index,
  the same message and `IsValid`).

Where the numeric time was (sphere, 55k vertices, one thread, ms): 500 in all; the supernodes at least 500 wide 218 (update 127,
dense block 91), 100 to 500 wide 129, 8 to 100 wide 82, narrower than 8 12.

`SparseCholeskyBenchmark`, Factorize (s), best of 3, 4 cores, master (b0969fbb) / this change (the solution hashes are unchanged):

| matrix | before | after |
|---|---|---|
| grid2d bs=1 n=40000 | 0.16 | 0.17 |
| grid2d bs=2 n=80000 | 0.33 | 0.31 |
| grid2d bs=2 n=120000 | 0.60 | 0.42 |
| grid3d n=27000 | 0.83 | 0.50 |
| hermitian grid2d n=40000 | 0.16 | 0.15 |
| hermitian grid2d n=60000 | 0.31 | 0.23 |

(Factorize includes the serial analysis, 0.2 to 0.4 s per 100k unknowns.) `CrossFieldBenchmark h=0.017` (4 cores, best of 3, s,
two interleaved runs, the better of each; field, eigenvalue and singular faces unchanged):

| shape | field | solves | before | after | gain |
|---|---|---|---|---|---|
| sphere | smoothest | 6 | 1.16 | 0.83 | 28 % |
| sphere | aligned s=1 | 6 | 1.15 | 0.90 | 22 % |
| torus | smoothest | 14 | 1.27 | 1.13 | 11 % |
| torus | aligned s=1 | 12 | 1.34 | 1.13 | 16 % |
| noisy-sphere | smoothest | 22 | 1.58 | 1.35 | 15 % |
| noisy-torus | smoothest | 34 | 2.39 | 1.96 | 18 % |
| noisy-torus | aligned s=1 | 21 | 1.76 | 1.48 | 16 % |

## What is left after the parallel solves and the parallel numeric factorization (2026-11-19)

The first two levers of the table above are done (the parallel solves, #1136, and the parallel numeric factorization, #1141; bit
identical, 11 % to 28 % of the `CrossField` total each time it was measured). The third, the loops of `CrossField` outside the
solver, is not worth a change. `CrossFieldBenchmark h=0.017` on the 55k vertex sphere, 4 cores, one run (ms): `ParamMesh` 85 to 97,
frames, topology and edge weights 64 (smoothest) to 175 (aligned: `MeshPrincipalCurvature`), assembly 21, ordering and numeric
factorization 520 to 530, solves 590 to 1190 (6 to 34 solves), per-vertex and per-face results about 45. The loops that can be parallel with
the same bits already are (`gParallel` over the faces, the edges and the corners and vertices of `MeshPrincipalCurvature`); the rest
are scatter-adds whose per-vertex sum order is the triangle order, and dictionary-based edge building. The only clean candidates are the
triangle frames of `ParamMesh` (about 21 ms) and its bowtie test (about 28 ms): at most 4 % of the sphere, 3 % of the torus, under 2 % of
the noisy torus. With every non-solver phase perfectly parallel (not possible with the same bits) the bound is 20 %, 14 % and 8 %.

What remains is in the solver: the minimum degree ordering (0.2 to 0.25 s, serial; a parallel one must give the same permutation bit for
bit) and the serial top supernodes of the solves (25 supernodes above the tasks on the 55k sphere); a nested dissection ordering changes
the factor and the results (owner decision).

## The parallel frames and bowtie test of `ParamMesh` (2026-11-19)

The two clean serial loops of the `ParamMesh` constructor (used by `CrossField`) are `gParallel.ForRange` loops now: the triangle
frames (each into its own slot; the surface area is still summed serially in triangle order) and `IsBowtieVertex` (flags per vertex,
then the first flagged vertex in index order, so the error names the same vertex as before). The results are the bits of the serial
loops (`ParamMeshParallelTests`: frames, area and error against the serial run, repeated runs). The loops respect the `gParallel` serial
switches.

Measured on the 4-core container (all cores = 4), net10.0 Release, 55k vertex UV sphere, best of 60 constructions in one process, the
serial switch (`gParallel.SerialOnThisThread`) against the parallel loops, ms for the whole `ParamMesh` constructor (the rest of it,
the topology, stays serial):

| cores | serial loops | parallel loops |
|---|---|---|
| 1 | 30.3 | 33.2 |
| 2 | 30.9 | 25.9 |
| 4 | 30.5 | 24.6 |

The gain is 6 ms at 4 cores, under 1 % of the 0.75 to 0.85 s `CrossField` at `h=0.017`, below the 3 to 4 % the earlier cold
measurement (21 ms frames, 28 ms bowtie in the first call) suggested: on repeated constructions the two loops take only about 12 ms
together, and the rest of the constructor is serial. `CrossFieldBenchmark only=^sphere$ h=0.017 reps=5` (best of 5, s; the bounds of the
noise are larger than the gain) at 1, 2 and 4 cores, before / after: smoothest 1.22 / 1.24, 0.89 / 0.94, 0.75 / 0.78; aligned s=1
1.29 / 1.43, 1.05 / 1.01, 0.86 / 0.86. The whole-run difference is not measurable; at 1 core the parallel loops cost about 3 ms.

## Opt-in patch-guided nested dissection (`CholeskyOrdering.NestedDissection`, 2026-11-21)

`SparseCholesky.Ordering` and `SparseHermitianCholesky.Ordering` add an opt-in `CholeskyOrdering.NestedDissection` mode
(`CholeskyNestedDissection`), keeping `CholeskyOrdering.MinimumDegree` as the default (all golden hashes in `CholeskyAnalysisTests`
and this benchmark are bit-identical). It builds a deterministic greedy patch quotient graph once, runs multilevel HEM coarsening
with orthogonal Fiedler + GGG initial splits and FM refinement, extracts minimum vertex separators via Hopcroft–Karp / König's
theorem, refines separators on the fine graph via two-sided FM, and orders independent leaves in parallel with halo-aware local AMD.

Command (2-core Xeon Platinum 8481C, `net10.0` Release, AVX2 `Vector256`, best of 3):

```
dotnet run -c Release --project benchmarks/SparseCholeskyBenchmark -- compare=nd reps=3
```

| matrix | nnz(L) AMD / ND | flops AMD / ND | maxSnW AMD / ND | Factorize AMD / ND (s) | Solve AMD / ND (ms) | relRes ND |
|---|---|---|---|---|---|---|
| grid2d bs=1 n=40000 | 1.58M / 1.50M (-5.5%) | 220M / 167M (-24.2%) | 438 / 338 | 0.502 / 0.206 (-59.0%) | 8.0 / 5.1 | 6.58e-16 |
| grid2d bs=2 n=80000 | 6.29M / 5.94M (-5.5%) | 1755M / 1328M (-24.3%) | 876 / 676 | 0.547 / 0.285 (-47.9%) | 14.6 / 12.5 | 9.59e-16 |
| grid2d bs=2 n=120000 | 9.92M / 9.61M (-3.0%) | 2879M / 2571M (-10.7%) | 910 / 836 | 0.483 / 0.394 (-18.4%) | 26.4 / 23.0 | 9.63e-16 |
| grid3d n=27000 | 6.06M / 4.99M (-17.7%) | 5969M / 3199M (-46.4%) | 1921 / 1302 | 0.616 / 0.298 (-51.7%) | 13.0 / 13.3 | 1.13e-15 |
| sphere V=54152 | 2.94M / 2.65M (-9.8%) | 838M / 539M (-35.6%) | 882 / 653 | 0.233 / 0.151 (-35.3%) | 7.1 / 5.9 | 1.78e-15 |
| biharmonic2d n=40000 | 5.81M / 5.08M (-12.6%) | 2047M / 1350M (-34.0%) | 987 / 625 | 0.463 / 0.263 (-43.2%) | 12.4 / 10.8 | 5.90e-16 |
| hermitian grid2d n=40000 | 1.58M / 1.50M (-5.5%) | 220M / 167M (-24.2%) | 438 / 338 | 0.175 / 0.145 (-17.3%) | 6.2 / 5.8 | 6.64e-16 |
| hermitian grid2d n=60000 | 2.49M / 2.42M (-3.0%) | 361M / 323M (-10.7%) | 455 / 418 | 0.261 / 0.204 (-21.9%) | 9.8 / 10.1 | 6.68e-16 |

The largest gains are on 3D volumes (`grid3d`: -46.4% flops, -51.7% Factorize), closed surface meshes (`sphere V=54k`: -35.6% flops,
-35.3% Factorize), and denser 2-ring stencils (`biharmonic2d`: -34.0% flops, -43.2% Factorize). Changing the default of downstream
callers (`CrossField`, `BiharmonicMeshDeformer`, `HeatMethod`) changes factor/solution rounding bits and remains a separate owner decision.

