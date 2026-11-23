# Procedural sphere unions and molecular ball sets as a geometry stress test

Study for priority 4 of [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424).

**Answer to the falsifiable question: no correctness defect or wrong refusal found; one superlinear multi-mesh scaling step measured.**
Across 20 procedural cases — analytic pairs/chains, exact tangency, gaps and overlaps down to `1e-12`, coplanar 6-ball rings, tetrahedral and octahedral enclosed cavities, connected 16- and 32-ball overlapping helices, and dense 12- to 96-ball multi-radii atomic packings — `ExactMeshBoolean` (multi-mesh and binary-tree union) and `SolidBooleanPipeline` returned `Success` with zero boundary edges, zero bowties, and `NotCleanReason == ""` on **19 of 20** cases (and `contacts(1)` with `OnlyContacts == true` on the single exact-tangent point contact). The main operational finding is **superlinear growth of the single-arrangement union in dense 3D ball packings**: multi-mesh union (`ExactMeshBoolean(parts, Union)`) grew about 6× from 48 to 96 balls in every run (`331 -> 2054 ms` in the run below), while a binary-tree pairwise union took `737 ms` at 96 balls (about 2.5–3.5× faster across runs, at 19% more triangles); at 48 balls the two were about even. Under plan #4's stop rule, **no library bugfix or dedicated union-of-balls operator is warranted**; park the topic and keep only the compact stress fixtures if desired.

## Experiment

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/ProceduralSphereUnionProbe -c Release
```

The [C# probe](../../benchmarks/ProceduralSphereUnionProbe/Program.cs) tests three groups of procedural ball sets:

1. **Analytic baselines (`overlap-2`, `thin-neck-2`, `small-gap-2`, `chain-3`):** collinear unit balls with exact inclusion–exclusion volume and area ([Edelsbrunner 1995](https://pub.ista.ac.at/~edels/Papers/1995-02-UnionBallsDualShape.pdf)).
2. **Degenerate, near-tangent, coplanar-ring and cavity clusters:** exact tangency (`d = 2`), gaps `1e-6 / 1e-9 / 1e-12`, overlaps `1e-6 / 1e-9 / 1e-12`, a coplanar 6-ball ring (`coplanar-ring-6`, genus 1, $\chi = 0$), and two enclosed-cavity polyhedral clusters (`tetra-cavity-4` at edge length `1.68` and `octa-cavity-6` at `a = 1.15`, each sealing a central void: 2 closed shells, $\chi = 4$).
3. **Molecule-like helices and dense multi-radii packings:** connected overlapping alpha-helix chains (`helix-16`, `helix-32`, 3.6 balls/turn) and seeded random clusters (`packing-12`, `24`, `48`, `96`) using scaled van der Waals radii (`0.60, 0.76, 0.775, 0.85, 0.90`).

Five routes are compared on every family, checking `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true)`, `SolidBooleanPipeline.Report`, `MeshMeasurements.VolumeArea`, and operator wall time:
- **`bool-multi`:** single-arrangement `ExactMeshBoolean(parts, Union)` with `StrictRounding = true`.
- **`bool-tree`** (Group C): balanced binary-tree pairwise `ExactMeshBoolean(a, b, Union)` with `StrictRounding = true`.
- **`pipeline`:** end-to-end `SolidBooleanPipeline(parts, Union)`.
- **`mc`:** serial `MarchingCubes` with 10-step bisection on the exact union implicit $\min_i (\|x - c_i\| - r_i)$.
- **`wrap`:** `MeshAlphaWrap` on the appended input sphere meshes (rolling-ball outer envelope).

## Results

Measured in Release on Linux x64 with .NET SDK 10.0.115. Every Boolean, pipeline and wrap run reported `Success` with `0` boundary edges and `0` bowties. All columns except time were identical across repeated runs. **Times are single runs on one machine** and vary between runs (up to about 2× for the small cases; a second run gave e.g. `1963 / 771 ms` for `packing-96` `bool-multi` / `bool-tree`, and an earlier run `4253 / 1216 ms`); read them as rough magnitudes only. The first Boolean of the process (`overlap-2`, not listed) includes JIT warm-up.

### Group A: Analytic pairs and chain (`EdgeVertices = 9`)

| Case | Route | Setting | Clean | Tris | Comp | Euler | Vol err | Area err | Time (ms) |
|---|---|---|---|---:|---:|---:|---:|---:|---:|
| overlap-2 | pipeline | ev9 | clean | 1504 | 1 | 2 | -1.53% | -0.53% | 67 |
| overlap-2 | mc | 16-p0 / 16-p1 | clean | 16744 / 16704 | 1 / 1 | 2 / 2 | -0.17% / -0.12% | -0.08% / -0.63% | 81 / 84 |
| overlap-2 | wrap | 0.20/0.02 | clean | 856 | 1 | 2 | +3.00% | +1.04% | 197 |
| thin-neck-2 | bool-multi / pipeline | ev9 | clean | 1552 | 1 | 2 | -1.67% | -0.67% | 14 / 20 |
| small-gap-2 | bool-multi / pipeline | ev9 | clean | 1536 | 2 | 4 | -1.67% | -0.83% | 29 / 21 |
| small-gap-2 | mc | 16-p0 / 16-p1 | clean | 19048 / 18992 | 2 / **1** | 4 / **2** | -0.19% / -0.13% | -0.09% / -1.01% | 70 / 70 |
| small-gap-2 | wrap | 0.20/0.02 | clean | 948 | **1** | **2** | +3.99% | -0.47% | 70 |
| chain-3 | bool-multi / pipeline | ev9 | clean | 2240 | 1 | 2 | -1.48% | -0.42% | 25 / 37 |

### Group B: Near-tangent contacts, coplanar ring and enclosed cavities

| Case | Route | Clean | Tris | Comp | Euler | Vol | Area | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| exact-tangent-2 | bool-multi | self-intersections | 1536 | 2 | 4 | 8.237 | 24.924 | 12 |
| exact-tangent-2 | pipeline | `contacts(1)` | 1536 | 2 | 4 | 8.237 | 24.924 | 27 |
| gap-1e-6 / 1e-9 / 1e-12 | pipeline | clean (all 3) | 1536 | 2 | 4 | 8.237 | 24.924 | 18 / 17 / 18 |
| gap-1e-9 / 1e-12 | mc | `degenerate triangles` | 19032 | 2 | 4 | 8.362 | 25.110 | 75 / 71 |
| overlap-1e-6 / 1e-9 / 1e-12 | pipeline | clean (all 3) | 1552 / 1552 / 1542 | 1 | 2 | 8.237 | 24.924 | 12 / 10 / 15 |
| overlap-1e-6 | mc | `degenerate triangles` | 19040 | 1 | 2 | 8.362 | 25.110 | 16 |
| coplanar-ring-6 | bool-multi / pipeline | clean | 2800 | 1 | 0 | 21.558 | 52.480 | 84 / 63 |
| coplanar-ring-6 | mc / wrap | clean | 22348 / 1724 | 1 / 1 | 0 / 0 | 22.045 / 23.084 | 52.302 / 52.417 | 31 / 69 |
| tetra-cavity-4 | bool-multi / pipeline | clean | 2872 | **2** | **4** | 15.631 | 38.550 | 38 / 55 |
| tetra-cavity-4 | mc | clean | 29064 | **6** | **12** | 15.827 | 38.376 | 33 |
| tetra-cavity-4 | wrap | clean | 1436 | 1 | 2 | 16.431 | 37.620 | 40 |
| octa-cavity-6 | bool-multi / pipeline | clean | 2208 | **2** | **4** | 22.228 | 49.420 | 58 / 69 |
| octa-cavity-6 | mc / wrap | clean | 28480 / 1856 | 2 / 1 | 4 / 2 | 22.695 / 23.468 | 48.990 / 47.524 | 48 / 47 |

### Group C: Connected helices and 12–96-ball multi-radii packings (`EdgeVertices = 5`)

| Case | Route | Clean | Tris | Comp | Euler | Vol | Area | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| helix-16 | bool-multi / bool-tree / pipeline | clean | 3934 / 4022 / 3934 | 1 | -2 | 85.805 | 194.592 | 38 / 128 / 44 |
| helix-16 | mc / wrap | clean | 29160 / 5256 | 1 / 1 | -16 / 2 | 90.507 / 94.055 | 190.366 / 171.302 | 60 / 159 |
| helix-32 | bool-multi / bool-tree / pipeline | clean | 7978 / 8180 / 7978 | 1 | -8 | 170.192 | 383.522 | 77 / 245 / 77 |
| helix-32 | mc / wrap | clean | 43760 / 10282 | 1 / 1 | -48 / 2 | 179.367 / 187.315 | 373.015 / 331.046 | 136 / 318 |
| packing-12 | bool-multi / bool-tree / pipeline | clean | 3040 / 3184 / 3040 | 1 | -6 | 18.046 | 53.431 | 28 / 78 / 33 |
| packing-12 | mc / wrap | clean | 15480 / 1768 | 1 / 1 | **0** / 2 | 18.925 / 20.035 | 51.411 / 47.168 | 28 / 57 |
| packing-24 | bool-multi / bool-tree / pipeline | clean | 5180 / 5500 / 5180 | 1 | 0 | 32.020 | 93.295 | 69 / 147 / 108 |
| packing-24 | mc / wrap | clean | 19120 / 3312 | 1 / 1 | **2** / 2 | 33.480 / 34.959 | 91.732 / 90.257 | 72 / 148 |
| packing-48 | bool-multi / bool-tree / pipeline | clean | 6956 / 7922 / 6956 | **2** | **4** | 51.135 | 113.149 | 331 / 357 / 396 |
| packing-48 | mc / wrap | clean | 16680 / 3296 | 1 / 1 | 2 / 2 | 53.008 / 55.354 | 109.230 / 105.164 | 94 / 136 |
| packing-96 | bool-multi / bool-tree / pipeline | clean | 10212 / 12168 / 10212 | **2** | **4** | 74.587 | 155.105 | 2054 / **737** / 1926 |
| packing-96 | mc / wrap | clean | 17428 / 4130 | 1 / 1 | 0 / 2 | 77.097 / 81.612 | 148.987 / 142.775 | 216 / 170 |

## Key findings

1. **Exact Boolean and `SolidBooleanPipeline` passed the degenerate and large-cluster stress tests on 19 of 20 cases cleanly.** Near-tangent gaps and overlaps down to `1e-12`, coplanar 6-ball rings, enclosed tetrahedral/octahedral cavities, overlapping helices, and 96-ball packings all succeeded with `0` bowties, `0` boundary edges, identical volumes/topologies between multi-mesh and tree union, and `NotCleanReason == ""`. The exception is `exact-tangent-2`: the plain `bool-multi` result reports `self-intersections` (the two spheres touch in one point, which the Boolean keeps), and `SolidBooleanPipeline` classifies the same result as `contacts(1)` with `OnlyContacts = true`, which is how the pipeline reports operands that only touch. No bug issue is warranted.
2. **Dense 3D packing scaling (`bool-multi` vs. `bool-tree`).** On the 1D helices (`helix-16 -> 32`) and the 12- and 24-ball packings, single-arrangement `bool-multi` was about 1.5–3.5× faster than `bool-tree` across runs. On dense 3D packings with heavy interior overlap, `bool-multi` / `SolidBooleanPipeline` grows superlinearly (`28 -> 69 -> 331 -> 2054 ms` from 12 to 96 balls in one run): the jump from 48 to 96 balls was about 6× in every run (6.2× and 6.1× here, 6.2× in an earlier run) and is the robust part of the finding. At 48 balls the two routes were about even (`331 / 357 ms` and `322 / 342 ms` here, `691 / 501 ms` earlier), so there is no measured crossover point; at 96 balls `bool-tree` was faster in every run (`737 ms` vs. `2054 ms` here, a ratio of 2.5–3.5× across the three runs, at 19% more triangles). **Hypothesis, not profiled:** the single arrangement keeps all 96 overlapping operands and their buried interior intersections in one arrangement and winding-number classification, while the tree drops buried geometry at every merge level. Profiling the stages is the follow-up ([#1466](https://github.com/HarmenGrosseDeters/geometry3Sharp/issues/1466)).
3. **Contouring and wrap limits quantified.** `MarchingCubes` produced `degenerate triangles` on `gap-1e-9`, `gap-1e-12` and `overlap-1e-6`, split `tetra-cavity-4` into 6 components (`Euler = 12` instead of `4`), missed the enclosed cavities in `packing-48`/`96`, and bridged `small-gap-2` under a half-cell grid shift. It also changed the genus where the balls form tunnels: `Euler = -16` / `-48` on `helix-16` / `helix-32` (Boolean `-2` / `-8`) and `0` / `2` on `packing-12` / `packing-24` (Boolean `-6` / `0`), i.e. at these cell sizes MC opens or closes handles in narrow grooves. `MeshAlphaWrap` was always clean (`25–318 ms` in one run) and acts as an outer rolling-ball envelope (sealing cavities and gaps narrower than `2 × offset`).

## Recommendation

**Stop and park (no library code or new union-of-balls operator).** Under plan #4's stop rule, the stress suite revealed no defect in `ExactMeshBoolean`, `SolidBooleanPipeline`, or `MeshAlphaWrap`. If a PR-tier regression family is later desired, `gap-1e-12`, `overlap-1e-12`, `exact-tangent-2`, `tetra-cavity-4`, and `packing-24` are cheap: their Boolean and pipeline routes took about 0.6–0.7 s in total in two runs (all five routes about 1.2 s); `packing-96` (`bool-multi` vs. `bool-tree`, about 2–4 s) is a useful nightly scaling check if multi-operand dense union speed is ever lifted from the speed stop (`#1253`).
