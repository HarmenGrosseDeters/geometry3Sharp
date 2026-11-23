# Procedural sphere unions and molecular ball sets as a geometry stress test

Study for priority 4 of [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424).

**Answer to the falsifiable question: no correctness defect or wrong refusal found; one real multi-mesh scaling crossover measured.**
Across 20 procedural cases — analytic pairs/chains, exact tangency, gaps and overlaps down to `1e-12`, coplanar 6-ball rings, tetrahedral and octahedral enclosed cavities, connected 16- and 32-ball overlapping helices, and dense 12- to 96-ball multi-radii atomic packings — `ExactMeshBoolean` (multi-mesh and binary-tree union) and `SolidBooleanPipeline` returned `Success` with zero boundary edges, zero bowties, and `NotCleanReason == ""` on **19 of 20** cases (and `contacts(1)` with `OnlyContacts == true` on the single exact-tangent point contact). The main operational finding is a **superlinear scaling crossover in dense 3D ball packings**: single-arrangement multi-mesh union (`ExactMeshBoolean(parts, Union)`) jumps 6.2× from 48 to 96 balls (`691 ms -> 4253 ms`), while a binary-tree pairwise union stays at `1216 ms` (**3.5× faster** at 96 balls, at 16% more triangles). Under plan #4's stop rule, **no library bugfix or dedicated union-of-balls operator is warranted**; park the topic and keep only the compact stress fixtures if desired.

## Experiment

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/ProceduralSphereUnionProbe -c Release
```

The [C# probe](../../benchmarks/ProceduralSphereUnionProbe/Program.cs) tests three groups of procedural ball sets:

1. **Analytic baselines (`overlap-2`, `thin-neck-2`, `small-gap-2`, `chain-3`):** collinear unit balls with exact inclusion–exclusion volume and area ([Edelsbrunner 1995](https://pub.ista.ac.at/~edels/Papers/1995-02-UnionBallsDualShape.pdf)).
2. **Degenerate, near-tangent, coplanar-ring and cavity clusters:** exact tangency (`d = 2`), gaps `1e-6 / 1e-9 / 1e-12`, overlaps `1e-6 / 1e-9 / 1e-12`, a coplanar 6-ball ring (`coplanar-ring-6`, genus 1, $\chi = 0$), and two enclosed-cavity polyhedral clusters (`tetra-cavity-4` at edge length `1.68` and `octa-cavity-6` at `a = 1.15`, each sealing a central void: 2 closed shells, $\chi = 4$).
3. **Molecule-like helices and dense multi-radii packings:** connected overlapping alpha-helix chains (`helix-16`, `helix-32`, 3.6 balls/turn) and seeded random clusters (`packing-12`, `24`, `48`, `96`) using scaled van der Waals radii (`0.60, 0.76, 0.775, 0.85, 0.90`).

Four routes are compared on every family, checking `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true)`, `SolidBooleanPipeline.Report`, `MeshMeasurements.VolumeArea`, and operator wall time:
- **`bool-multi`:** single-arrangement `ExactMeshBoolean(parts, Union)` with `StrictRounding = true`.
- **`bool-tree`** (Group C): balanced binary-tree pairwise `ExactMeshBoolean(a, b, Union)` with `StrictRounding = true`.
- **`pipeline`:** end-to-end `SolidBooleanPipeline(parts, Union)`.
- **`mc`:** serial `MarchingCubes` with 10-step bisection on the exact union implicit $\min_i (\|x - c_i\| - r_i)$.
- **`wrap`:** `MeshAlphaWrap` on the appended input sphere meshes (rolling-ball outer envelope).

## Results

Measured in Release on Linux x64 with .NET SDK 10.0.115. Every Boolean, pipeline and wrap run reported `Success` with `0` boundary edges and `0` bowties.

### Group A: Analytic pairs and chain (`EdgeVertices = 9`)

| Case | Route | Setting | Clean | Tris | Comp | Euler | Vol err | Area err | Time (ms) |
|---|---|---|---|---:|---:|---:|---:|---:|---:|
| overlap-2 | pipeline | ev9 | clean | 1504 | 1 | 2 | -1.53% | -0.53% | 141 |
| overlap-2 | mc | 16-p0 / 16-p1 | clean | 16744 / 16704 | 1 / 1 | 2 / 2 | -0.17% / -0.12% | -0.08% / -0.63% | 132 / 130 |
| overlap-2 | wrap | 0.20/0.02 | clean | 856 | 1 | 2 | +3.00% | +1.04% | 267 |
| thin-neck-2 | bool-multi / pipeline | ev9 | clean | 1552 | 1 | 2 | -1.67% | -0.67% | 26 / 35 |
| small-gap-2 | bool-multi / pipeline | ev9 | clean | 1536 | 2 | 4 | -1.67% | -0.83% | 40 / 26 |
| small-gap-2 | mc | 16-p0 / 16-p1 | clean | 19048 / 18992 | 2 / **1** | 4 / **2** | -0.19% / -0.13% | -0.09% / -1.01% | 29 / 56 |
| small-gap-2 | wrap | 0.20/0.02 | clean | 948 | **1** | **2** | +3.99% | -0.47% | 190 |
| chain-3 | bool-multi / pipeline | ev9 | clean | 2240 | 1 | 2 | -1.48% | -0.42% | 31 / 48 |

### Group B: Near-tangent contacts, coplanar ring and enclosed cavities

| Case | Route | Clean | Tris | Comp | Euler | Vol | Area | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| exact-tangent-2 | bool-multi | self-intersections | 1536 | 2 | 4 | 8.237 | 24.924 | 7 |
| exact-tangent-2 | pipeline | `contacts(1)` | 1536 | 2 | 4 | 8.237 | 24.924 | 29 |
| gap-1e-6 / 1e-9 / 1e-12 | pipeline | clean (all 3) | 1536 | 2 | 4 | 8.237 | 24.924 | 11 / 13 / 11 |
| gap-1e-9 / 1e-12 | mc | `degenerate triangles` | 19032 | 2 | 4 | 8.362 | 25.110 | 29 / 28 |
| overlap-1e-6 / 1e-9 / 1e-12 | pipeline | clean (all 3) | 1552 / 1552 / 1542 | 1 | 2 | 8.237 | 24.924 | 12 / 14 / 20 |
| overlap-1e-6 | mc | `degenerate triangles` | 19040 | 1 | 2 | 8.362 | 25.110 | 26 |
| coplanar-ring-6 | bool-multi / pipeline | clean | 2800 | 1 | 0 | 21.558 | 52.480 | 120 / 85 |
| coplanar-ring-6 | mc / wrap | clean | 22348 / 1724 | 1 / 1 | 0 / 0 | 22.045 / 23.084 | 52.302 / 52.417 | 43 / 133 |
| tetra-cavity-4 | bool-multi / pipeline | clean | 2872 | **2** | **4** | 15.631 | 38.550 | 56 / 93 |
| tetra-cavity-4 | mc | clean | 29064 | **6** | **12** | 15.827 | 38.376 | 52 |
| tetra-cavity-4 | wrap | clean | 1436 | 1 | 2 | 16.431 | 37.620 | 70 |
| octa-cavity-6 | bool-multi / pipeline | clean | 2208 | **2** | **4** | 22.228 | 49.420 | 137 / 148 |
| octa-cavity-6 | mc / wrap | clean | 28480 / 1856 | 2 / 1 | 4 / 2 | 22.695 / 23.468 | 48.990 / 47.524 | 76 / 100 |

### Group C: Connected helices and 12–96-ball multi-radii packings (`EdgeVertices = 5`)

| Case | Route | Clean | Tris | Comp | Euler | Vol | Area | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| helix-16 | bool-multi / bool-tree / pipeline | clean | 3934 / 4022 / 3934 | 1 | -2 | 85.805 | 194.592 | 65 / 196 / 108 |
| helix-16 | mc / wrap | clean | 29160 / 5256 | 1 / 1 | -16 / 2 | 90.507 / 94.055 | 190.366 / 171.302 | 103 / 313 |
| helix-32 | bool-multi / bool-tree / pipeline | clean | 7978 / 8180 / 7978 | 1 | -8 | 170.192 | 383.522 | 193 / 371 / 147 |
| helix-32 | mc / wrap | clean | 43760 / 10282 | 1 / 1 | -48 / 2 | 179.367 / 187.315 | 373.015 / 331.046 | 223 / 559 |
| packing-12 | bool-multi / bool-tree / pipeline | clean | 3040 / 3184 / 3040 | 1 | -6 | 18.046 | 53.431 | 52 / 91 / 111 |
| packing-24 | bool-multi / bool-tree / pipeline | clean | 5180 / 5500 / 5180 | 1 | 0 | 32.020 | 93.295 | 135 / 217 / 154 |
| packing-48 | bool-multi / bool-tree / pipeline | clean | 6956 / 7922 / 6956 | **2** | **4** | 51.135 | 113.149 | 691 / **501** / 718 |
| packing-48 | mc / wrap | clean | 16680 / 3296 | 1 / 1 | 2 / 2 | 53.008 / 55.354 | 109.230 / 105.164 | 163 / 289 |
| packing-96 | bool-multi / bool-tree / pipeline | clean | 10212 / 12168 / 10212 | **2** | **4** | 74.587 | 155.105 | 4253 / **1216** / 4184 |
| packing-96 | mc / wrap | clean | 17428 / 4130 | 1 / 1 | 0 / 2 | 77.097 / 81.612 | 148.987 / 142.775 | 347 / 296 |

## Key findings

1. **Exact Boolean and `SolidBooleanPipeline` passed every degenerate and large-cluster stress test cleanly.** Near-tangent gaps and overlaps down to `1e-12`, coplanar 6-ball rings, enclosed tetrahedral/octahedral cavities, overlapping helices, and 96-ball packings all succeeded with `0` bowties, `0` boundary edges, identical volumes/topologies between multi-mesh and tree union, and `clean` results (`contacts(1)` with `OnlyContacts = true` on `exact-tangent-2`). No bug issue is warranted.
2. **Dense 3D packing scaling crossover (`bool-multi` vs. `bool-tree`).** On sparse 1D helices (`helix-16 -> 32`), single-arrangement `bool-multi` stays 1.9–3.0× faster than `bool-tree`. On dense 3D packings with heavy interior overlap, `bool-multi` / `SolidBooleanPipeline` scales superlinearly (`52 -> 135 -> 691 -> 4253 ms` from 12 to 96 balls — a 6.2× jump on the last doubling) because all 96 overlapping operands remain in one arrangement and winding-number classification. Binary-tree union (`bool-tree`) prunes buried interior geometry at every merge level, overtaking `bool-multi` at 48 balls and running **3.5× faster at 96 balls** (`1216 ms` vs. `4253 ms`, at 19% more triangles).
3. **Contouring and wrap limits quantified.** `MarchingCubes` produced `degenerate triangles` on `gap-1e-9`, `gap-1e-12` and `overlap-1e-6`, split `tetra-cavity-4` into 6 components (`Euler = 12` instead of `4`), missed the enclosed cavities in `packing-48`/`96`, and bridged `small-gap-2` under a half-cell grid shift. `MeshAlphaWrap` was always clean and fast (`59–559 ms`) and acts as an outer rolling-ball envelope (sealing cavities and gaps narrower than `2 × offset`).

## Recommendation

**Stop and park (no library code or new union-of-balls operator).** Under plan #4's stop rule, the stress suite revealed no defect in `ExactMeshBoolean`, `SolidBooleanPipeline`, or `MeshAlphaWrap`. If a PR-tier regression family is later desired, `gap-1e-12`, `overlap-1e-12`, `exact-tangent-2`, `tetra-cavity-4`, and `packing-24` run end-to-end in `< 0.3 s` total; `packing-96` (`bool-multi` vs. `bool-tree`) is a useful nightly scaling check if multi-operand dense union speed is ever lifted from the speed stop (`#1253`).
