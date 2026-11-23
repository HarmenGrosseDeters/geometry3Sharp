# Procedural sphere unions as a geometry stress test

## Experiment

Run `dotnet run -c Release --project benchmarks/ProceduralSphereUnionProbe`. The first probe uses four arrangements of abstract unit balls with centers on one axis. The pairs have center spacings 1.5, 2 − 1/128 and 2 + 1/128. The fourth arrangement is a three-ball chain with spacing 1.5. Only adjacent balls can intersect, and no three have a common intersection. The connected cases have one boundary component with Euler characteristic 2. The disjoint pair has two components and total Euler characteristic 4.

For center spacing d, let h = max(0, 1 − d/2). An overlap lens consists of two caps with individual volume πh²(1 − h/3) and area 2πh. Subtract the lens volume for each adjacent pair and remove both hidden cap areas from the sum of individual sphere areas. This is the pairwise case of the inclusion–exclusion approach. [Edelsbrunner, The Union of Balls and Its Dual Shape](https://pub.ista.ac.at/~edels/Papers/1995-02-UnionBallsDualShape.pdf)

The probe compares three existing routes.

- `MarchingCubes` samples the minimum of the sphere fields. This field has the exact union sign and boundary, although its negative values in overlaps need not be signed distances to the union boundary. It is run serially with bisection at spacings 1/16 and 1/32, with the grid centered on the pair's separating plane or shifted by half a cell along the center axis.
- `ExactMeshBoolean` chains strict unions on inscribed cube-sphere meshes with 9 vertices per cube edge. Its input solids are polyhedra. The smooth volume and area differences include input approximation.
- `MeshAlphaWrap` encloses the appended input sphere meshes with alpha 0.20 and offset 0.02. A wrap may bridge a gap smaller than twice the offset. Its volume and area differences are comparisons with the unexpanded analytic spheres. [Alpha wrap paper notes](../design/ALPHA_WRAP_PAPER_NOTES.md)

The probe uses `MeshTopologySummary` for component count, boundary edges, bowties and Euler characteristic, and `MeshMeasurements.VolumeArea` for signed volume and area. The reported times cover the operator calls and exclude input construction and measurement of the resulting mesh.

## Results

Both mesh operators reported `Success` on all four arrangements. Every row had zero boundary edges and zero bowties. Times are elapsed milliseconds from one local Release run and include warm-up effects.

| Case | Route | Setting | Components | Euler characteristic | Volume error (%) | Area error (%) | ms |
|---|---|---|---:|---:|---:|---:|---:|
| overlap | mc | 16-phase0 | 1 | 2 | -0.1716 | -0.078299 | 265 |
| overlap | mc | 16-phase1 | 1 | 2 | -0.11938 | -0.62676 | 81 |
| overlap | mc | 32-phase0 | 1 | 2 | -0.044132 | -0.020222 | 493 |
| overlap | mc | 32-phase1 | 1 | 2 | -0.028995 | -0.3068 | 409 |
| overlap | boolean | 9 | 1 | 2 | -1.5326 | -0.53361 | 790 |
| overlap | wrap | 0.20/0.02 | 1 | 2 | 3.0029 | 1.0417 | 353 |
| thin-neck | mc | 16-phase0 | 1 | 2 | -0.18783 | -0.072741 | 48 |
| thin-neck | mc | 16-phase1 | 1 | 2 | -0.11989 | -1.2331 | 92 |
| thin-neck | mc | 32-phase0 | 1 | 2 | -0.048001 | -0.018422 | 414 |
| thin-neck | mc | 32-phase1 | 1 | 2 | -0.029312 | -0.64802 | 309 |
| thin-neck | boolean | 9 | 1 | 2 | -1.673 | -0.66555 | 35 |
| thin-neck | wrap | 0.20/0.02 | 1 | 2 | 3.867 | -1.0199 | 135 |
| small-gap | mc | 16-phase0 | 2 | 4 | -0.18815 | -0.092109 | 71 |
| small-gap | mc | 16-phase1 | 1 | 2 | -0.12146 | -1.0819 | 63 |
| small-gap | mc | 32-phase0 | 2 | 4 | -0.048093 | -0.023448 | 399 |
| small-gap | mc | 32-phase1 | 1 | 2 | -0.030511 | -0.48593 | 249 |
| small-gap | boolean | 9 | 2 | 4 | -1.674 | -0.82868 | 14 |
| small-gap | wrap | 0.20/0.02 | 1 | 2 | 3.9852 | -0.4682 | 199 |
| chain | mc | 16-phase0 | 1 | 2 | -0.16583 | -0.072916 | 49 |
| chain | mc | 16-phase1 | 1 | 2 | -0.093147 | -0.83941 | 63 |
| chain | mc | 32-phase0 | 1 | 2 | -0.042681 | -0.018898 | 600 |
| chain | mc | 32-phase1 | 1 | 2 | -0.022478 | -0.42029 | 355 |
| chain | boolean | 9 | 1 | 2 | -1.4826 | -0.41558 | 92 |
| chain | wrap | 0.20/0.02 | 1 | 2 | 3.2049 | 0.27233 | 76 |

The small-gap pair is the distinguishing case. With an x = 0 sample plane, the sampled column between the two balls is outside everywhere and Marching Cubes returns two components. With a half-cell shift, the x-directed edge at y = z = 0 from −Δ/2 to Δ/2 spans the gap g = 1/128. Both endpoints are inside when g < Δ, as in both tested grids. The edge contains two crossings but offers no endpoint sign change, so edge bisection cannot recover either crossing. Marching Cubes returns one closed component at both resolutions despite a relative volume error of only −0.031% at the finer spacing. This is a sampling limitation of the chosen grid and field representation.

The strict Boolean kept both components. Its volume error on the gap case is the volume deficit of the two inscribed input polyhedra relative to the smooth spheres. The wrap joined the pair at offset 0.02. The two offset neighborhoods can meet at g/2 = 1/256, so this outcome is consistent with the wrap's enclosure objective.

## Decision

Keep the procedural pair as a compact topology oracle. A PR-tier candidate uses the strict Boolean with the 9-vertex-per-edge inputs and aligned Marching Cubes with Δ = 1/16. Check two closed components, zero bowties and Euler characteristic 4 before evaluating volume accuracy. The calls took 14 ms and 71 ms in the recorded run, respectively. Keep the shifted case as a negative control for any future topology certificate. A candidate nightly extension would sweep gap, orientation and grid phase, with topology evaluated independently of metric error.

The measured cases establish the grid-phase failure mode and give a bounded positive control. They reveal no new library defect to fix. Park a new union-of-balls implementation on the evidence of this probe. The numerical conclusions cover the collinear equal-radius families above. Rolling-probe surfaces, noncollinear packings and large-input scaling remain open.
