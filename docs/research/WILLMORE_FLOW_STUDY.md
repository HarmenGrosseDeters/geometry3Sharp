# Willmore energy and remeshing a moving ring torus

## Decision

Revised on 2026-11-25. Every guarded `Remesher` pass in this study now sets `PreventNormalFlips` as well as `PreventSelfIntersections`. The
first version guarded with `PreventSelfIntersections` alone. Its full passes folded 13 to 69 triangles against the surface, which the clean-mesh
check does not see, because the mesh stays consistently oriented and free of intersections. Those folds produced the large energy rises it
reported. See [Earlier runs without the normal guard](#earlier-runs-without-the-normal-guard).

With both guards:
- **Every remeshed torus is clean**, with no intersecting pairs and no triangle inverted against the analytic torus.
- **The first moving pass changes the energy little.** It takes the bending energy from 24.65 to 29.20, and the static pass at R = 3 from
  31.26 to 35.51.
- **Later passes can lower it.** The pass at R = 1.40625 lowers it from 42.16 to 26.71, so a pass does not raise the energy by a fixed
  fraction.

On the paired experiment, an energy-gated rollback works as a benchmark acceptance rule. Each optional mesh pass is kept only if the output is
clean, has no triangle inverted against the torus, has energy at most the old energy, and improves the worst triangle quality.
- **On the regular torus** the full pass never qualifies. The gentle smoothing pass gives a tiny quality gain (0.07 %).
- **On the crowded torus** the budget rule accepts one full pass and three gentle passes (gentle, full, gentle, gentle). They raise the worst triangle quality by 63 % over
  motion alone (0.1146 against 0.0702), at 10 % higher final discrete energy (21.456 against 19.500).

Whether that trade is wanted is the caller's choice. The energy rule alone does not decide it.

The existing operators serve different objectives:
- `MeshCurvature` provides the ingredients for a vertex quadrature of bending energy.
- `LaplacianMeshSmoother` minimizes a constrained graph-Laplacian residual.
- `BiharmonicMeshDeformer` minimizes a displacement energy with handles.

None of them exposes descent of the current surface's bending energy. Crane, Pinkall and Schröder describe conformal curvature flow with
curvature-space updates and fixed connectivity. The experiment below uses a one-parameter surface family to test the library's curvature and
remeshing operations. The practicality of a general Willmore fairing operator remains open. [Source](../sources/meshing-and-repair.md)

## Reproduce

Run `LC_ALL=C dotnet run -c Release --project benchmarks/WillmoreFlowProbe`. The probe generates periodic triangulated ring tori with unit tube radius and major radius R. For the smooth torus, the principal curvatures up to orientation are 1 and cos(v)/(R + cos(v)), and the area element is (R + cos(v)) du dv. Integrating the squared half-sum gives W(R) = π² R² / √(R² − 1). Its minimum in this family occurs at R = √2 with W = 2π². The probe sums the squared signed mean curvature times mixed vertex area, validating every curvature sample. On n × 2n grids the absolute energy errors are:

| Tube samples n | R = √2 | R = 3 |
|---:|---:|---:|
| 12 | 0.860867 | 0.551988 |
| 24 | 0.228949 | 0.141799 |
| 48 | 0.058206 | 0.035766 |

On the structured 48 × 24 torus a centered difference with radius increment 1e-4 chooses the descent direction. Starting at R = 3, try a
displacement of 0.75 (three target edge lengths) and halve it until R > 1 and the measured structured energy decreases. This is restricted
descent in R, without a physical time parameter or an area or volume constraint. Apply each accepted radius change to a second mesh by moving
vertices radially in the xy plane. Then run one serial `Remesher.BasicRemeshPass` per move, with target edge length 0.25 and nearest-point
projection onto the new smooth torus. The control keeps R fixed for one pass.
- **Guarded** sets `PreventSelfIntersections` and `PreventNormalFlips`.
- **Unguarded** keeps the library defaults: neither guard.
- **Inverted** counts the triangles whose normal points against the outward normal of the smooth torus at their centroid.

`--no-normal-guard` reproduces the earlier runs, guarded by `PreventSelfIntersections` alone.

| Motion | Guard | R | Structured W | Moving mesh W before pass | W after pass | Intersecting pairs | Inverted triangles |
|---|---|---:|---:|---:|---:|---:|---:|
| Static | Off | 3 | 31.2631 | 31.2631 | 77.9338 | 7 | 15 |
| Static | On | 3 | 31.2631 | 31.2631 | 35.5149 | 0 | 0 |
| Moving | Off | 2.25 | 24.6537 | 24.6537 | 110.9539 | 15 | 69 |
| Moving | On | 2.25 | 24.6537 | 24.6537 | 29.2013 | 0 | 0 |
| Moving | On | 1.5 | 19.6642 | 34.8364 | 39.3950 | 0 | 0 |
| Moving | On | 1.40625 | 19.5078 | 42.1571 | 26.7117 | 0 | 0 |
| Moving | On | 1.40039 | 19.5076 | 26.7865 | 26.3363 | 0 | 0 |

Each input to a remeshing pass passed the clean-mesh check. Unguarded runs stop at their first positive pair count. Guarded outputs also passed
the checks for a single closed, consistently oriented genus-one component, valid curvature samples and absence of self-intersections. The clean
check does not detect inverted triangles; the separate count does.

**Where the unguarded intersections come from.** Switching the sub-operations of one static pass off one at a time locates the cause in the
valence flips after the splits. Splits and flips alone, without smoothing, collapses or projection, give 75 inverted triangles and 22 intersecting
pairs. Without flips, every count stays 0 over five passes. By default (as upstream) a valence flip has no normal test.
`PreventNormalFlips`, `MinTriangleQuality > 0` and `ApplySafePreset()` each add one, and each kept every count at 0 over five passes. The
remeshing guide states that the upstream defaults do not guard against flipped normals. So these intersections are expected without that
guard, not a Remesher defect.

On the first guarded step the vertex count changed from 1,152 to 2,060. The before/after energy difference includes changes in vertices,
triangles and curvature quadrature. The energy measurement costs one curvature evaluation and one vertex sum per mesh, linear in mesh size. Each
radius trial repeats this on the structured grid. Remeshing and the exact intersection checks add separate geometry-dependent costs. The energy
oracle and the observed near-fourfold error reduction apply to this refinement family. The experiment does not establish stability of
unconstrained fourth-order flow, model membrane equilibria under physiological constraints, or give a general remeshing guarantee.

## Keeping the shape change when a mesh pass fails the energy check

Run `LC_ALL=C dotnet run -c Release --project benchmarks/WillmoreFlowProbe -- --acceptance`. It replays the same four proposed radii from R = 3
on the 48 × 24 torus. The second fixture uses longitude u + 0.8 sin(u) in place of u, which crowds vertices near one side while preserving
their order and the underlying analytic surface. All policies start from identical meshes for a given fixture.

For each step, keep the old mesh and form a separate, clean moved mesh. The baseline accepts the motion if its energy is at most the old
energy. The other policies try a guarded pass (both guards) on a copy. Each accepted mesh pass must improve the moved mesh's worst triangle
quality, using the library's scale-independent score (1 for an equilateral triangle).
- **The full pass** enables splits, collapses and flips.
- **The gentler pass** enables only the existing smoothing and analytic projection.

Each candidate starts from the same moved mesh. The probe checks cleanliness, genus and curvature validity on each candidate and reports its
inverted triangles. A candidate with an inverted triangle is rejected, whatever its energy and quality. No guarded candidate had one, so this
rule changes no result here; it only closes the gap that the clean check leaves.

- **Strict** tries the full pass, then the gentler pass. Energy must be at most both the old energy and the moved mesh's energy.
- **Budget** tries the same passes, allowing redistribution to spend part of the energy reduction earned by the shape change. The accepted mesh
  must still have energy at most the old energy.
- **Smooth-only** applies the budget rule to the gentler pass directly.

If no candidate qualifies, all three policies retain the clean moved mesh if its energy is at most the old energy, and otherwise the old mesh.

| Longitude warp | Policy | Accepted full / gentle passes | Final energy | Worst triangle quality |
|---:|---|---:|---:|---:|
| 0 | Baseline | 0 / 0 | 19.507591 | 0.333486 |
| 0 | Strict | 0 / 1 | 19.507634 | 0.333685 |
| 0 | Budget or smooth-only | 0 / 4 | 19.508077 | 0.333732 |
| 0.8 | Baseline | 0 / 0 | 19.500048 | 0.070219 |
| 0.8 | Strict | 0 / 1 | 19.500137 | 0.070380 |
| 0.8 | Budget | 1 / 3 | 21.456335 | 0.114595 |
| 0.8 | Smooth-only | 0 / 3 | 19.500702 | 0.070706 |

All four radii were accepted on both fixtures, and the accepted energy decreased at every step.
- **The guarded full passes no longer cost several times the energy.** They gave 29.20, 25.56, 23.57 and 23.51 on the regular torus, and
  42.09, 23.37, 22.93 and 22.82 for strict on the crowded one. On the regular torus no policy accepts one, for two reasons. At step 1 the full
  pass (29.20) is below the old energy (31.26), so budget would allow it, but it does not improve the worst triangle quality; strict also
  rejects it, because it is above the moved energy (24.65). At steps 2 to 4 it is above the old energy.
- **On the crowded torus the budget rule accepts a full pass at step 2.** It returned 23.370, against an old energy of 24.641 and a moved energy
  of 19.656. It spends most of that step's descent on redistribution and lifts the worst quality from 0.0878 to 0.0952. A gentle pass comes
  before it at step 1, and two more follow at steps 3 and 4. They lower the energy to 21.456 and raise the quality to 0.1146.
- **The two budget variants now differ.** Budget and smooth-only choose different meshes on the crowded torus. Smooth-only ends at 0.070706,
  0.69 % above baseline, and budget at 63 % above baseline.
- **On the regular torus** budget and smooth-only stay identical: the full pass never qualifies there. Their gain is 0.074 % over baseline.

The practical result is a small, testable rollback rule. It keeps every accepted step clean and energy-decreasing. On a crowded mesh, the
budget variant buys a large gain in worst quality with part of the energy decrease that the shape change earned. These percentages describe
mesh quality and discrete energy at the same final radius, not an improvement in the analytic Willmore energy of the surface. This experiment
gives no performance or stability guarantee for unrestricted vertex motion, topology-changing remeshing on other inputs, or constrained
membrane dynamics.

Add `--timings` after `--acceptance` for end-to-end policy times. Each process warms up on one unreported budget trajectory. These are the
medians of three separate Release runs in a 4-core cloud container, for the four steps together:

| Warp | Motion alone | Strict | Budget, full pass first | Budget, gentle pass only |
|---:|---:|---:|---:|---:|
| 0 | 39 ms | 599 ms | 403 ms | 99 ms |
| 0.8 | 18 ms | 323 ms | 276 ms | 99 ms |

These times include mesh copies, clean-mesh checks, energy, quality and inverted-triangle evaluations, candidate passes and row output. They
exclude process startup and warm-up. The gentle-only policy took about 2.5 to 5.5 times the baseline time and 25 to 36 % of the full-first
budget time.

## Thingi10K thin-walled part

**Unverified after the guard change.** The rows below were measured with `PreventSelfIntersections` alone. The probe's `--model` command now
also sets `PreventNormalFlips`, so it no longer reproduces them. They were not re-run because the file is not part of the repository. The two
"Full" rows in particular are likely affected by folds, as the full passes on the torus were. Treat this section as a record of the earlier run
until it is repeated with a local copy.

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

## Earlier runs without the normal guard

`--no-normal-guard` (or `--acceptance --no-normal-guard`) reproduces the first version of this study bit for bit, with an added
inverted-triangle count:
- **The moving guarded passes:** W after pass 111.63, 90.52, 62.73 and 64.33. They were clean and free of intersections, but had 69, 17, 15
  and 13 inverted triangles.
- **The static guarded pass:** 76.96, with 14 inverted triangles.
- **The acceptance full passes:** 61.8 to 111.6 with 17 to 69 inverted triangles on the regular torus, and 67.5 to 88.9 with 29 to 38 on the
  crowded torus. No policy accepted any of them, so budget and smooth-only picked identical meshes.

Its conclusions were artefacts of those folds:
- that a guarded pass raised the energy more than fourfold;
- that skipping all full passes gives the same accepted results;
- that budget and smooth-only always pick the same mesh;
- that the quality gain is at most 0.69 %.
