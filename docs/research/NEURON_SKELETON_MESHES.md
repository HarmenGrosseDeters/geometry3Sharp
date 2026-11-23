# Procedural neuron-like branching tube meshes from skeletons as a stress family

Study for priority 5 of [PR #1424](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1424).

**Answer to the falsifiable question: no defect, wrong genus, or performance cliff in the exact Boolean or `SolidBooleanPipeline`; expected bridging limits observed in convolution contouring and alpha wrap.**
Across 9 procedural SWC-style branching tube skeletons — including extreme `4°` grazing forks, `1e-6` near-tangent distal branch clearances, an intentional genus-1 loop, a tightly bundled 25-segment tree (`14°–24°` sibling angles), and a 45-segment (91-primitive) multi-level dendrite tree — `ExactMeshBoolean` (both single-arrangement multi-mesh and binary-tree union) and `SolidBooleanPipeline` returned `Success`, `NotCleanReason == ""` (with self-intersection checks enabled), `0` bowties, `0` boundary edges, and the **exact expected component count and genus on 9 of 9 cases** in `57–553 ms`. Single-arrangement multi-mesh union scaled linearly on sparse branching trees (`451 ms` at 91 primitives, faster than binary-tree union at `729 ms`). Under plan #5's stop rule, **stop and park** — no library fix or dedicated skeleton-meshing / convolution-surface operator is warranted.

## Experiment

Reproduce from the repository root:

```sh
LC_ALL=C dotnet run --project benchmarks/NeuronSkeletonProbe -c Release
```

The [C# probe](../../benchmarks/NeuronSkeletonProbe/Program.cs) generates procedural SWC-style skeletons (soma node + tapered parent-to-child frustum segments with spherical joint caps built via `CappedCylinderGenerator` and `Sphere3Generator_NormalizedCube`):

- **`y-junction-60deg`, `acute-fork-12deg`, `acute-fork-4deg`:** soma + trunk + two daughter branches at `60°`, `12°` and extreme `4°` grazing angles (expected: `1` component, genus `0`, $\chi = 2$).
- **`near-tangent-gap-1e-2`, `near-tangent-gap-1e-6`:** two branches curving toward each other so distal tip spheres pass within exterior gap `1e-2` and `1e-6` without touching (expected: `1` component, genus `0`, $\chi = 2$; false fusion creates genus `1`, $\chi = 0$).
- **`intentional-loop`:** two branches diverging from a soma and rejoining downstream (expected: `1` component, genus `1`, $\chi = 0$).
- **`dendrite-tree-15` (43 primitives), `tight-bundle-25` (51 primitives, `14°–24°` angles), `dendrite-tree-45` (91 primitives):** multi-level tapering bifurcating trees (`r = 0.50 -> 0.06`, expected: `1` component, genus `0`, $\chi = 2$).

Three routes to a closed surface are compared, checking `MeshTopologySummary`, `MeshRobustRepair.NotCleanReason(mesh, checkSelfIntersections: true)`, `SolidBooleanPipeline.Report`, volume, and operator wall time:
1. **Exact Boolean / pipeline (`bool-multi`, `bool-tree`, `pipeline`):** `ExactMeshBoolean(parts, Union)`, binary-tree pairwise `ExactMeshBoolean`, and `SolidBooleanPipeline(parts, Union)` with `StrictRounding = true`.
2. **Convolution-surface contouring (`conv-mc`):** closed-form Cauchy-kernel line-segment convolution field (Bloomenthal & Shoemake 1991) plus soma point kernel, contoured serially with `MarchingCubes` (10-step bisection).
3. **Alpha wrap (`wrap`):** `MeshAlphaWrap` on the appended tube and joint meshes.

## Results

Measured in Release on Linux x64 with .NET SDK 10.0.115. Every run produced `0` boundary edges, `0` bowties, and `clean` (`NotCleanReason == ""`). Asterisks (`*`) mark topology mismatches against the expected `(comp, euler)`.

| Case | Route | Setting | Tris | Comp | Euler | Genus | Vol | Time (ms) |
|---|---|---|---:|---:|---:|---:|---:|---:|
| y-junction-60deg | bool-multi / bool-tree / pipeline | s12/p7 | 1130 / 1188 / 1130 | 1 | 2 | 0 | 1.108 | 1137† / 234 / 200 |
| y-junction-60deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 8236 / 970 | 1 / 1 | 2 / 2 | 0 / 0 | 1.692 / 1.239 | 323 / 286 |
| acute-fork-12deg | bool-multi / bool-tree / pipeline | s12/p7 | 1522 / 1814 / 1522 | 1 | 2 | 0 | 1.039 | 173 / 313 / 190 |
| acute-fork-12deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 7484 / 852 | 1 / 1 | 2 / 2 | 0 / 0 | 1.697 / 1.166 | 117 / 155 |
| acute-fork-4deg | bool-multi / bool-tree / pipeline | s12/p7 | 1540 / 1870 / 1540 | 1 | 2 | 0 | 0.977 | 305 / 360 / 305 |
| acute-fork-4deg | conv-mc / wrap | h=0.06 / 0.14,0.015 | 7292 / 788 | 1 / 1 | 2 / 2 | 0 / 0 | 1.675 / 1.076 | 171 / 131 |
| near-tangent-gap-1e-2 | bool-multi / bool-tree / pipeline | s12/p9 | 1588 / 1772 / 1588 | 1 | 2 | 0 | 1.178 | 136 / 302 / 81 |
| near-tangent-gap-1e-2 | conv-mc / wrap | h=0.05 / 0.12,0.015 | 12668 / 1106 | 1 / 1 | 2 / **0\*** | 0 / **1** | 2.066 / 1.314 | 285 / 192 |
| near-tangent-gap-1e-6 | bool-multi / bool-tree / pipeline | s12/p9 | 1628 / 1812 / 1628 | 1 | 2 | 0 | 1.179 | 60 / 81 / 73 |
| near-tangent-gap-1e-6 | conv-mc / wrap | h=0.05 / 0.12,0.015 | 12680 / 1102 | 1 / 1 | 2 / **0\*** | 0 / **1** | 2.068 / 1.315 | 314 / 87 |
| intentional-loop | bool-multi / bool-tree / pipeline | s12/p8 | 1378 / 1566 / 1378 | 1 | 0 | 1 | 1.229 | 76 / 99 / 57 |
| intentional-loop | conv-mc / wrap | h=0.06 / 0.14,0.015 | 8888 / 864 | 1 / 1 | 0 / 0 | 1 / 1 | 2.079 / 1.365 | 179 / 71 |
| dendrite-tree-15 | bool-multi / bool-tree / pipeline | s10/p43 | 6028 / 7428 / 6028 | 1 | 2 | 0 | 3.367 | 331 / 606 / 307 |
| dendrite-tree-15 | conv-mc / wrap | h=0.07 / 0.15,0.015 | 18564 / 2774 | 1 / 1 | 2 / 2 | 0 / 0 | 6.158 / 3.902 | 3325 / 216 |
| tight-bundle-25 | bool-multi / bool-tree / pipeline | s10/p51 | 8612 / 11724 / 8612 | 1 | 2 | 0 | 3.251 | 542 / 674 / 553 |
| tight-bundle-25 | conv-mc / wrap | h=0.07 / 0.15,0.015 | 17900 / 2506 | 1 / 1 | 2 / 2 | 0 / 0 | 6.297 / 3.767 | 3880 / 256 |
| dendrite-tree-45 | bool-multi / bool-tree / pipeline | s8/p91 | 9836 / 12344 / 9836 | 1 | 2 | 0 | 4.014 | **451** / 729 / **482** |
| dendrite-tree-45 | conv-mc / wrap | h=0.09 / 0.18,0.02 | 16320 / 3106 | 1 / 1 | **0\*** / 2 | **1** / 0 | 7.462 / 5.190 | 6584 / **297** |

† First call includes JIT cold-start warmup.

## Key findings

1. **Exact Boolean and `SolidBooleanPipeline` handle every branching-tube stress case cleanly and fast.** Even `acute-fork-4deg` (long grazing slivers), `near-tangent-gap-1e-6`, `tight-bundle-25` (51 primitives), and `dendrite-tree-45` (91 primitives) all succeeded with exact genus, identical volume across multi-mesh and tree unions, and `NotCleanReason == ""`. Unlike dense 3D ball packings ([PR #1455](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1455)), a neuron skeleton has a sparse 1D overlap graph, so single-arrangement `bool-multi` / `SolidBooleanPipeline` stays faster than `bool-tree` all the way to 91 primitives (`451 ms` vs. `729 ms`, with 20% fewer triangles).
2. **Infinite-support convolution surfaces (`conv-mc`) inflate volume and create spurious handles on dense trees.** Because the Cauchy kernel decays algebraically ($1/d^3$), summing 45 nearby branches inflated volume by ~1.86× (`7.462` vs. `4.014`), fused nearby non-touching subtrees on `dendrite-tree-45` into a spurious genus-1 loop (`Euler = 0*`), and cost `6584 ms` (14× slower than exact Boolean due to $O(M \cdot N_{\mathrm{grid}})$ evaluation without spatial pruning).
3. **`MeshAlphaWrap` is the fastest outer envelope (`71–297 ms`) with predictable offset bridging.** It kept exact genus on all three multi-level dendrite trees and `intentional-loop`, while bridging `near-tangent-gap-1e-2` and `near-tangent-gap-1e-6` into genus `1` because both gaps are smaller than `2 × offset = 0.03`.

## Recommendation

**Stop and park (no library code or new skeleton-meshing operator).** Under plan #5's stop rule, the branching-tube stress suite revealed no defect or scaling cliff in `ExactMeshBoolean`, `SolidBooleanPipeline`, or `MeshAlphaWrap`. Callers who need exact topology on SWC-like skeletons can already union tapered `CappedCylinderGenerator` frusta and joint spheres directly with `SolidBooleanPipeline` (`< 0.5 s` for 91 primitives). If a compact PR-tier regression family is later desired, `acute-fork-4deg`, `near-tangent-gap-1e-6`, `intentional-loop`, and `tight-bundle-25` exercise all topology regimes in `< 1.0 s` total.
