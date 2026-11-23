# Willmore energy and remeshing a moving ring torus

## Decision

Use measured output energy as a benchmark acceptance check. The paired experiment below shows when an optional mesh pass can be discarded while retaining a clean, energy-decreasing shape change. The practicality of a general Willmore fairing operator remains open. The existing operators serve different objectives. `MeshCurvature` provides the ingredients for a vertex quadrature of bending energy. `LaplacianMeshSmoother` minimizes a constrained graph-Laplacian residual and `BiharmonicMeshDeformer` minimizes a displacement energy with handles. Neither exposes descent of the current surface's bending energy. Crane, Pinkall and Schröder describe conformal curvature flow with curvature-space updates and fixed connectivity. The experiment below uses a one-parameter surface family to test the library's curvature and remeshing operations. [Source](../sources/meshing-and-repair.md)

The guarded remesher preserved a clean torus through all four tested moves. Its first pass increased the measured bending energy more than fourfold. An intersection guard and analytic projection are therefore insufficient to guarantee energy descent on the evolving triangulation. A production stepping rule would need to check energy on the actual output mesh, including after connectivity changes. The unguarded static control also intersects, so the observed intersections do not establish a defect specific to curvature-driven motion.

## Reproduce

Run `LC_ALL=C dotnet run -c Release --project benchmarks/WillmoreFlowProbe`. The probe generates periodic triangulated ring tori with unit tube radius and major radius R. For the smooth torus, the principal curvatures up to orientation are 1 and cos(v)/(R + cos(v)), and the area element is (R + cos(v)) du dv. Integrating the squared half-sum gives W(R) = π² R² / √(R² − 1). Its minimum in this family occurs at R = √2 with W = 2π². The probe sums the squared signed mean curvature times mixed vertex area, validating every curvature sample. On n × 2n grids the absolute energy errors are:

| Tube samples n | R = √2 | R = 3 |
|---:|---:|---:|
| 12 | 0.860867 | 0.551988 |
| 24 | 0.228949 | 0.141799 |
| 48 | 0.058206 | 0.035766 |

On the structured 48 × 24 torus a centered difference with radius increment 1e-4 chooses the descent direction. Starting at R = 3, try a displacement of 0.75 (three target edge lengths) and halve it until R > 1 and the measured structured energy decreases. This is restricted descent in R without a physical time parameter or area/volume constraint. Apply each accepted radius change to a second mesh by moving vertices radially in the xy plane. Run one serial `Remesher.BasicRemeshPass` per move, with target edge length 0.25 and nearest-point projection onto the new smooth torus. The control keeps R fixed for one pass. The only setting changed between guarded and unguarded runs is `PreventSelfIntersections`.

| Motion | Guard | R | Structured W | Moving mesh W before pass | W after pass | Intersecting triangle pairs |
|---|---|---:|---:|---:|---:|---:|
| Static | Off | 3 | 31.2631 | 31.2631 | 77.9338 | 7 |
| Static | On | 3 | 31.2631 | 31.2631 | 76.9624 | 0 |
| Moving | Off | 2.25 | 24.6537 | 24.6537 | 110.9539 | 15 |
| Moving | On | 2.25 | 24.6537 | 24.6537 | 111.6296 | 0 |
| Moving | On | 1.5 | 19.6642 | 116.0239 | 90.5166 | 0 |
| Moving | On | 1.40625 | 19.5078 | 96.0104 | 62.7280 | 0 |
| Moving | On | 1.40039 | 19.5076 | 62.7913 | 64.3281 | 0 |

Each input to a remeshing pass passed the clean-mesh check. Unguarded runs stop at their first positive pair count. Guarded outputs also passed the checks for a single closed, consistently oriented genus-one component, valid curvature samples and absence of self-intersections. The moving guard rejected 125 operations or moves cumulatively. On the first guarded step the vertex count changed from 1,152 to 2,110. The before/after energy difference includes changes in vertices, triangles and curvature quadrature.

The energy measurement costs one curvature evaluation and one vertex sum per mesh, linear in mesh size. Each radius trial repeats this on the structured grid. Remeshing and the exact intersection checks add separate geometry-dependent costs. The energy oracle and the observed near-fourfold error reduction apply to this refinement family. The experiment does not establish stability of unconstrained fourth-order flow, model membrane equilibria under physiological constraints, or give a general remeshing guarantee.

## Keeping the shape change when a mesh pass fails the energy check

Run `LC_ALL=C dotnet run -c Release --project benchmarks/WillmoreFlowProbe -- --acceptance`. Replay the same four proposed radii from R = 3 on the 48 × 24 torus. The second fixture uses longitude u + 0.8 sin(u) in place of u. This crowds vertices near one side while preserving their order and the underlying analytic surface. All policies start from identical meshes for a given fixture.

For each step, keep the old mesh and form a separate, clean moved mesh. The baseline accepts the motion if its energy is at most the old energy. The other policies try a guarded pass on a copy. Each accepted mesh pass must improve the moved mesh's worst triangle quality, using the library's scale-independent score (1 for an equilateral triangle). The full pass enables splits, collapses and flips. The gentler pass enables only the existing smoothing and analytic projection. Each candidate starts from the same moved mesh. The probe checks cleanliness, genus and curvature validity on each candidate and aborts on an unexpected invalid mesh.

- **Strict** tries the full pass, then the gentler pass. Energy must be at most both the old energy and the moved mesh's energy.
- **Budget** tries the same passes, allowing redistribution to spend part of the energy reduction earned by the shape change. The accepted mesh must still have energy at most the old energy.
- **Smooth-only** applies the budget rule to the gentler pass directly. If no candidate qualifies, all three policies retain the clean moved mesh if its energy is at most the old energy. Otherwise they retain the old mesh.

| Longitude warp | Policy | Accepted full / gentle passes | Final energy | Worst triangle quality |
|---:|---|---:|---:|---:|
| 0 | Baseline | 0 / 0 | 19.507591 | 0.333486 |
| 0 | Strict | 0 / 1 | 19.507634 | 0.333685 |
| 0 | Budget or smooth-only | 0 / 4 | 19.508077 | 0.333732 |
| 0.8 | Baseline | 0 / 0 | 19.500048 | 0.070219 |
| 0.8 | Strict | 0 / 1 | 19.500137 | 0.070380 |
| 0.8 | Budget or smooth-only | 0 / 3 | 19.500702 | 0.070706 |

All four radii were accepted on both fixtures and the accepted energy decreased at every step. On the warped fixture's last budget step, the old energy was 19.500894, the moved energy was 19.500702, and the gentle pass returned 19.501005. Keeping the shape change alone preserved descent without a retry or a smaller step. The budget and smooth-only policies selected identical meshes by construction whenever the full pass failed. Their final minimum quality rose about 0.074% over baseline on the regular mesh and 0.69% on the warped mesh. The final discrete energy was slightly higher than baseline in both cases. These percentages describe mesh quality at the same final radius, not an improvement in the analytic Willmore energy of the surface.

The practical result is a small, testable rollback rule. On these inputs skipping full remeshing avoids four rejected candidates per trajectory and preserves all accepted results. The quality gain is modest even on the crowded mesh. This experiment gives no performance or stability guarantee for unrestricted vertex motion, topology-changing remeshing on other inputs, or constrained membrane dynamics.

Add `--timings` after `--acceptance` for end-to-end policy times. Each process warms up on one unreported budget trajectory. The medians of three separate Release runs on this machine, for the four steps together, were:

| Warp | Motion alone | Strict | Budget, full pass first | Budget, gentle pass only |
|---:|---:|---:|---:|---:|
| 0 | 69 ms | 690 ms | 605 ms | 168 ms |
| 0.8 | 61 ms | 544 ms | 457 ms | 161 ms |

These times include mesh copies, clean-mesh checks, energy and quality evaluations, candidate passes and row output. They exclude process startup and warm-up. The gentle-only policy took about 2.7 times the baseline time on the crowded mesh and about 35% of the full-first budget time. For this workload, use the energy check to make optional smoothing reversible when minimum quality matters. Motion alone is the cheaper baseline when the observed sub-percent quality gain is immaterial.

## Thingi10K thin-walled part

Thingi10K file 135363 (`4AAA_base.stl`, Thing 51376) is the thin-walled part already characterized in the [remeshing benchmark](../../benchmarks/RemeshQualityBenchmark/README.md). The input has 1,308 vertices, 2,612 triangles, one closed genus-zero component, mixed-area bending energy 3522.582 and worst triangle quality 0.0000191632. Its mean edge length h is 11.6226 model units. The earlier benchmark reports tube wall thickness 0.75 and base thickness 0.8. The very low worst quality and sharp features make the vertex energy a discretization diagnostic. There is no analytic Willmore reference for this model.

Run `dotnet run -c Release --project benchmarks/WillmoreFlowProbe -- --model /path/to/135363.stl` with a local copy. The opt-in command reads the model locally. The default benchmark still generates its inputs. File SHA-256 is `3c9ae76d07378a13e5dc4e1840469f3e693196656e73d494d1a0dc2d0a7af2cd`. The dataset lists Creative Commons Attribution–ShareAlike for this file. [Original design](https://www.thingiverse.com/thing:51376) · [Thingi10K mesh](https://huggingface.co/datasets/Thingi10K/Thingi10K/blob/main/raw_meshes/135363.stl) · [License metadata](https://huggingface.co/datasets/Thingi10K/Thingi10K/blob/main/metadata/input_summary.csv)

There is no prescribed motion. Each of four independent proposals starts from the input and runs one guarded serial pass targeting h, projecting to the input surface. The optional feature constraints use the library's 30° crease threshold. Acceptance requires a clean mesh with the same genus and component count, nonincreasing measured energy and improved minimum triangle quality. Symmetric Hausdorff distance to the input is reported independently using the library's lower and upper estimates with tolerance 0.01 h.

| Pass | Feature constraints | Energy | Worst triangle quality | Hausdorff distance / h | Accepted |
|---|---|---:|---:|---:|---|
| Smoothing only | Off | 3396.637 | 0.000012052 | 0.13308–0.14305 | No |
| Full | Off | 16077.051 | 0.000060481 | 0.45641–0.46502 | No |
| Smoothing only | On | 3672.552 | 0.000024188 | 0.00133–0.01134 | No |
| Full | On | 18781.942 | 0.000014841 | 0.08305–0.09239 | No |

All four outputs passed the clean-mesh and topology checks. Energy alone would accept the unconstrained smoothing pass. Its worst triangle quality fell by 37% and its symmetric surface distance was at least 1.54 model units, more than twice the nominal tube wall thickness. This is a global distance, so it does not locate the maximum deviation on a particular wall. Feature-constrained smoothing kept the distance below 0.132 units and improved worst quality by 26%, but raised measured energy by 4.3%. For this engineering part, retaining the input is a useful conservative outcome of the combined rule. If dimensional fidelity and triangle quality are the design requirements, the feature-constrained result warrants comparison to the caller's tolerance. The energy rule alone cannot decide that tradeoff. This single-pass test on one part does not establish a dataset-wide rate or simulate a free Willmore flow.
