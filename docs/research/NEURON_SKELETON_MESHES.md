# Procedural branching tubes as a topology stress test

Bloomenthal and Shoemake use convolution to turn curve skeletons into smooth blended surfaces and demonstrate topology changes. This probe uses a hard union with known topology so a handle created during reconstruction can be identified independently of the field. The inputs are procedural geometry. [Convolution Surfaces](https://www.bloomenthal.com/JBloom/pdf/CSurfFinal.pdf)

## Fixture and routes

Two parallel capped arms run along y from 0 to 2 and grow from radius 1/8 to 1/4. Their axes lie at x = ±(1/4 + g/2). They are disjoint at every y and their minimum gap g is at the tips. A horizontal cylinder crosses both arms at their smaller ends. A tapered stem attaches below the crossbar and stops short of the arms. Each arm and the stem intersects only the crossbar, with positive overlap and a single attachment. Thickening this tree gives one closed genus-zero surface. A second connection across the tips would create a cycle.

Run `dotnet run -c Release --project benchmarks/BranchingTubeProbe`. The fixture compares gaps 1/8 and 1/128.

- `MarchingCubes` uses the exact zero set of the hard union of circular frusta. Each frustum field is the maximum of the two cap tests and radial distance minus the linearly interpolated radius. Cell spacings are 1/16 and 1/32. At each spacing, one lattice samples the symmetry plane x = 0 and the other shifts by half a cell along x. The y origin is offset by a quarter cell to keep sample planes away from the caps.
- `ExactMeshBoolean` chains strict unions of 16-sided inscribed meshes from `CappedCylinderGenerator`. Their expected attachment topology is the same as for the circular reference.
- `MeshAlphaWrap` encloses the appended triangle soup at alpha 0.06 and offset 0.012. The offset neighborhoods can meet across the small gap. [Alpha wrap paper notes](../design/ALPHA_WRAP_PAPER_NOTES.md)

`MeshTopologySummary` measures components, boundary edges, bowties, Euler characteristic and genus. Elapsed time covers only each operator call or the full Boolean chain.

## Results

Every row had one component, zero boundary edges and zero bowties. Both mesh operators reported `Success` for both gaps. Times are from one local Release run and include warm-up effects.

| Gap | Route | Setting | Euler characteristic | Genus | ms |
|---|---|---|---:|---:|---:|
| 0.125 | mc | 16-phase0 | 2 | 0 | 136 |
| 0.125 | mc | 16-phase1 | 2 | 0 | 109 |
| 0.125 | mc | 32-phase0 | 2 | 0 | 84 |
| 0.125 | mc | 32-phase1 | 2 | 0 | 66 |
| 0.125 | boolean | 16 | 2 | 0 | 466 |
| 0.125 | wrap | 0.06/0.012 | 2 | 0 | 374 |
| 0.0078125 | mc | 16-phase0 | 2 | 0 | 8 |
| 0.0078125 | mc | 16-phase1 | 0 | 1 | 8 |
| 0.0078125 | mc | 32-phase0 | 2 | 0 | 47 |
| 0.0078125 | mc | 32-phase1 | 0 | 1 | 52 |
| 0.0078125 | boolean | 16 | 2 | 0 | 25 |
| 0.0078125 | wrap | 0.06/0.012 | 0 | 1 | 317 |

For the narrow gap, the shifted grid adds a handle at both cell spacings. At the last y sample below the tips, y = 2 − 3Δ/4, the gap along z = 0 is g + 3Δ/32. It is smaller than Δ in both runs. The x-directed sample edge from −Δ/2 to Δ/2 therefore has two crossings and two inside endpoints. The aligned lattice samples the positive gap at x = 0 and retains genus 0. The sphere-pair study shows the same missed-crossing mechanism as a component merge. Here the arms are already connected at their base, so the added bridge changes genus while leaving the component count unchanged. [Sphere-pair study #1455](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/1455)

The strict Boolean retains genus 0. The wrapped narrow-gap case gives genus 1, consistent with bridging a gap smaller than twice the offset. The wider gap gives genus 0 on every route. These checks diagnose topology relative to the hard union. A blended field or an enclosing wrap has its own geometric target.

## Decision

Propose a PR-tier genus oracle using the narrow-gap aligned contour at Δ = 1/16 and the 16-sided strict Boolean. Require one closed component, zero bowties and Euler characteristic 2. These operator calls took 8 ms and 25 ms in the recorded run. Preserve the shifted case as a negative control for a future topology certificate. The observed changes follow from grid sampling and the chosen wrap offset, so the probe gives no new implementation defect to fix. Park a larger branching sweep. The measured scope is the two capped-tube fixtures above, with blended convolution fields and larger skeletons outside this experiment.
