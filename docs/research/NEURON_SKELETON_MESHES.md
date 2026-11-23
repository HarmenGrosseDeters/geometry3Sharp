# Procedural neuron-like branching tube meshes from skeletons as a stress family

Study for priority 5 of [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424).

**Answer to the falsifiable question: no defect, wrong genus, or performance cliff in the exact Boolean or `SolidBooleanPipeline`; expected bridging limits observed in convolution contouring and alpha wrap.**
Across 9 procedural SWC-style branching tube skeletons — including extreme `4°` grazing forks, two distal branches whose meshes are a measured `1e-2` and `1e-6` apart, an intentional genus-1 loop, a tightly bundled 25-segment tree (`14°–24°` sibling angles), and a 45-segment (91-primitive) multi-level dendrite tree — `ExactMeshBoolean` (both single-arrangement multi-mesh and binary-tree union) and `SolidBooleanPipeline` returned `Success`, `NotCleanReason == ""` (with self-intersection checks enabled), `0` bowties, `0` boundary edges, and the **exact expected component count and genus on 9 of 9 cases**, each in well under half a second (`28–463 ms` per route in two single runs, the first call with JIT warm-up excluded). Under plan #5's stop rule, **stop and park** — no library fix or dedicated skeleton-meshing / convolution-surface operator is warranted.

## Experiment

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/NeuronSkeletonProbe -c Release
```

The [C# probe](../../benchmarks/NeuronSkeletonProbe/Program.cs) generates procedural SWC-style skeletons (soma node + tapered parent-to-child frustum segments with spherical joint caps built via `CappedCylinderGenerator` and `Sphere3Generator_NormalizedCube`):

- **`y-junction-60deg`, `acute-fork-12deg`, `acute-fork-4deg`:** soma + trunk + two daughter branches at `60°`, `12°` and extreme `4°` grazing angles (expected: `1` component, genus `0`, $\chi = 2$).
- **`near-tangent-gap-1e-2`, `near-tangent-gap-1e-6`:** two branches curving toward each other so the distal tip spheres pass within an exterior gap of `1e-2` and `1e-6` without touching (expected: `1` component, genus `0`, $\chi = 2$; false fusion creates genus `1`, $\chi = 0$). The tip spheres use `EdgeVertices = 5`: an odd count puts a vertex on each face centre, so the meshes reach exactly `±r` along the gap axis. The probe prints the measured minimum distance between the two branch meshes (`DMeshAABBTree3.FindNearestTriangles`): `0.01` and `1e-6` (to six digits). An earlier version of the probe used `EdgeVertices = 4`, whose sphere reaches only about `0.945 r` along the axis; its real mesh gaps were `0.0298` and `0.0198`, so it did not test a near-tangent case.
- **`intentional-loop`:** two branches diverging from a soma and rejoining downstream (expected: `1` component, genus `1`, $\chi = 0$).
- **`dendrite-tree-21` (21 segments = 3 + 6 + 12, 43 primitives), `tight-bundle-25` (25 segments, 51 primitives, `14°–24°` angles), `dendrite-tree-45` (45 segments, 91 primitives):** multi-level tapering bifurcating trees (`r = 0.50 -> 0.06`, expected: `1` component, genus `0`, $\chi = 2$). For these the probe also prints the minimum distance between the meshes of two segments that share no node.

Three routes to a closed surface are compared, checking `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true)`, `SolidBooleanPipeline.Report`, volume, and operator wall time:
1. **Exact Boolean / pipeline (`bool-multi`, `bool-tree`, `pipeline`):** `ExactMeshBoolean(parts, Union)`, binary-tree pairwise `ExactMeshBoolean` (both with `StrictRounding = true`), and `SolidBooleanPipeline(parts, Union)`.
2. **Convolution-surface contouring (`conv-mc`):** closed-form Cauchy-kernel line-segment convolution field (McCormack & Sherstyuk 1998, after the convolution surfaces of Bloomenthal & Shoemake 1991) plus a soma point kernel, contoured serially with `MarchingCubes` (10-step bisection).
3. **Alpha wrap (`wrap`):** `MeshAlphaWrap` on the appended tube and joint meshes.

## Results

Measured in Release on Linux x64 with .NET SDK 10.0.115. Every run produced `0` boundary edges, `0` bowties, and `clean` (`NotCleanReason == ""`). Asterisks (`*`) mark topology mismatches against the expected `(comp, euler)`. All columns except time were identical in two runs. **Times are single runs on one machine** (the first run below); they varied by up to about 30% between runs for the same route and should be read as rough magnitudes.

| Case | Route | Setting | Tris | Comp | Euler | Genus | Vol | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| y-junction-60deg | bool-multi / bool-tree / pipeline | s12/p7 | 1130 / 1188 / 1130 | 1 | 2 | 0 | 1.108 | 666† / 81 / 129 |
| y-junction-60deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 8236 / 970 | 1 / 1 | 2 / 2 | 0 / 0 | 1.692 / 1.239 | 247 / 182 |
| acute-fork-12deg | bool-multi / bool-tree / pipeline | s12/p7 | 1522 / 1814 / 1522 | 1 | 2 | 0 | 1.039 | 113 / 170 / 102 |
| acute-fork-12deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 7484 / 852 | 1 / 1 | 2 / 2 | 0 / 0 | 1.697 / 1.166 | 69 / 84 |
| acute-fork-4deg | bool-multi / bool-tree / pipeline | s12/p7 | 1540 / 1870 / 1540 | 1 | 2 | 0 | 0.977 | 121 / 103 / 142 |
| acute-fork-4deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 7292 / 788 | 1 / 1 | 2 / 2 | 0 / 0 | 1.675 / 1.076 | 65 / 79 |
| near-tangent-gap-1e-2 | bool-multi / bool-tree / pipeline | s12/p9 | 2046 / 2228 / 2046 | 1 | 2 | 0 | 1.182 | 77 / 141 / 94 |
| near-tangent-gap-1e-2 | conv-mc / wrap | h=0.05 / 0.12,0.015 | 12668 / 1062 | 1 / 1 | 2 / **0\*** | 0 / **1** | 2.066 / 1.320 | 149 / 146 |
| near-tangent-gap-1e-6 | bool-multi / bool-tree / pipeline | s12/p9 | 2076 / 2258 / 2076 | 1 | 2 | 0 | 1.183 | 82 / 117 / 78 |
| near-tangent-gap-1e-6 | conv-mc / wrap | h=0.05 / 0.12,0.015 | 12680 / 1080 | 1 / 1 | 2 / **0\*** | 0 / **1** | 2.068 / 1.318 | 150 / 101 |
| intentional-loop | bool-multi / bool-tree / pipeline | s12/p8 | 1378 / 1566 / 1378 | 1 | 0 | 1 | 1.229 | 33 / 60 / 52 |
| intentional-loop | conv-mc / wrap | h=0.06 / 0.14,0.015 | 8888 / 864 | 1 / 1 | 0 / 0 | 1 / 1 | 2.079 / 1.365 | 114 / 35 |
| dendrite-tree-21 | bool-multi / bool-tree / pipeline | s10/p43 | 6028 / 7428 / 6028 | 1 | 2 | 0 | 3.367 | 134 / 298 / 204 |
| dendrite-tree-21 | conv-mc / wrap | h=0.07 / 0.15,0.015 | 18564 / 2774 | 1 / 1 | 2 / 2 | 0 / 0 | 6.158 / 3.902 | 2042 / 107 |
| tight-bundle-25 | bool-multi / bool-tree / pipeline | s10/p51 | 8612 / 11724 / 8612 | 1 | 2 | 0 | 3.251 | 219 / 341 / 237 |
| tight-bundle-25 | conv-mc / wrap | h=0.07 / 0.15,0.015 | 17900 / 2506 | 1 / 1 | 2 / 2 | 0 / 0 | 6.297 / 3.767 | 2360 / 157 |
| dendrite-tree-45 | bool-multi / bool-tree / pipeline | s8/p91 | 9836 / 12344 / 9836 | 1 | 2 | 0 | 4.014 | 204 / 462 / 263 |
| dendrite-tree-45 | conv-mc / wrap | h=0.09 / 0.18,0.02 | 16320 / 3106 | 1 / 1 | **0\*** / 2 | **1** / 0 | 7.462 / 5.190 | 4118 / 159 |

† First call includes JIT cold-start warmup.

Measured clearances (identical in both runs): `near-tangent-gap-1e-2` and `-1e-6`: `0.01` and `1e-6` between the two branch meshes. Minimum distance of the meshes of two segments that share no node: `intentional-loop` `0.731`; `dendrite-tree-21` `0.151` (segments `3-8` and `9-21`); `tight-bundle-25` `0` (segments `2-6` and `7-16` overlap; the tight bundle has non-adjacent tubes that intersect, which the Boolean unites as intended; so its expected genus `0` no longer follows from the tree construction alone: it rests on the agreement of the Boolean, the wrap and conv-mc, not on a known answer); `dendrite-tree-45` `0.0748` (segments `3-8` and `9-20`).

## Key findings

1. **Exact Boolean and `SolidBooleanPipeline` handle every branching-tube stress case cleanly.** Even `acute-fork-4deg` (long grazing slivers), `near-tangent-gap-1e-6` (branch meshes a measured `1e-6` apart), `tight-bundle-25` (51 primitives, with intersecting non-adjacent tubes), and `dendrite-tree-45` (91 primitives) all succeeded with exact genus, identical volume across multi-mesh and tree unions, and `NotCleanReason == ""`. Unlike dense 3D ball packings ([PR #1455](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1455)), a neuron skeleton has a sparse 1D overlap graph; single-arrangement `bool-multi` was faster than `bool-tree` on the trees in both runs (`204 ms` vs. `462 ms` at 91 primitives, with 20% fewer triangles). On the small forks the two were close: on `acute-fork-4deg` `bool-tree` was faster in both runs here (`103 / 124 ms` vs. `121 / 132 ms`) and slower in an earlier run, so no consistent order is claimed there. The times do not show a clean scaling law (43 / 51 / 91 primitives took `134 / 219 / 204 ms` with `bool-multi`); the cost depends on the overlap structure, not only on the primitive count.
2. **Convolution surfaces (`conv-mc`) inflate volume and create a spurious handle on the densest tree.** The inflation is already present on the plain `y-junction-60deg` (`1.692` vs. `1.108`, 1.53×) and grows only to 1.8–1.9× on the trees (`7.462` vs. `4.014` on `dendrite-tree-45`), so it comes mostly from the field and threshold design of this probe (likely the overlapping kernel contributions of the soma and the segments near every joint, which a threshold set for one straight tube does not account for; not measured separately), with a smaller share from summing many nearby branches. On `dendrite-tree-45` the contour has genus `1` (`Euler = 0*`). **Hypothesis, not localised:** the inflated field fuses two nearby non-adjacent branches into a handle; the closest pair of non-adjacent segments there is `0.0748` apart (segments `3-8` and `9-20`), below the cell size `h = 0.09`, which makes that pair a candidate. `conv-mc` also cost `2.0–4.1 s` on the trees, about 10–20× the Boolean, because every grid sample sums all segments ($O(M \cdot N_{\mathrm{grid}})$, no spatial pruning).
3. **`MeshAlphaWrap` is a fast outer envelope (`35–210 ms` in two runs) with predictable offset bridging.** It kept exact genus on all three multi-level dendrite trees and `intentional-loop`, while bridging `near-tangent-gap-1e-2` and `near-tangent-gap-1e-6` into genus `1`: both measured gaps (`0.01`, `1e-6`) are smaller than `2 × offset = 0.03`. (The wrap also bridged the larger `0.0198` / `0.0298` gaps of the earlier `EdgeVertices = 4` version, which are below `0.03` as well.)

## Recommendation

**Stop and park (no library code or new skeleton-meshing operator).** Under plan #5's stop rule, the branching-tube stress suite revealed no defect or scaling cliff in `ExactMeshBoolean`, `SolidBooleanPipeline`, or `MeshAlphaWrap`. Callers who need exact topology on SWC-like skeletons can already union tapered `CappedCylinderGenerator` frusta and joint spheres directly with `SolidBooleanPipeline` (`220–263 ms` for 91 primitives in two runs). If a compact PR-tier regression family is later desired, `acute-fork-4deg`, `near-tangent-gap-1e-6`, `intentional-loop`, and `tight-bundle-25` exercise all topology regimes; their `SolidBooleanPipeline` runs took about 0.5 s in total in two runs (all three Boolean routes about 1.6 s).
