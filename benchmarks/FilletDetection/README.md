# FilletDetection

Research spike (2026-11): can the fillets of a CAD mesh be found, and the sharp design edge rebuilt? Study and numbers in
[docs/research/DEFILLET_SPIKE_2026-11.md](../../docs/research/DEFILLET_SPIKE_2026-11.md). No library change; everything here is
prototype code.

```
dotnet run -c Release --project benchmarks/FilletDetection -- crease   > results/crease.md
dotnet run -c Release --project benchmarks/FilletDetection -- detect   > results/detect.md      # filters: box plate lprofile gear rod, cad, fine, n=8, fast, bound=4
dotnet run -c Release --project benchmarks/FilletDetection -- defillet > results/defillet.md
python3 benchmarks/FilletDetection/summarize.py benchmarks/FilletDetection/results/detect.md > results/summary.md
# results/bound.md: detect cad n=8 fast bound=1.1 | 4 | 10 (the medial rows)
```

The raw output of the runs behind the study is in `results/` (2026-11-22, cloud container, 4 cores; about 15 minutes in all).

## Inputs (`Solids.cs`, generated, with the answer per triangle)

| solid | fillets | what it tests |
|---|---|---|
| box | all 12 edges of a 2 x 1.4 x 1 box rounded with 0.15: quarter cylinders and 8 sphere octants | edge and vertex fillets, no sharp edge left |
| plate | the same on a 2 x 1.4 x 0.2 plate with radius 0.04 | parallel walls close to the fillets |
| lprofile | extruded L, vertical edges rounded (5 convex with 0.1, the concave one with 0.2), sharp rims | convex and concave, two radii, fillets that end on a sharp rim |
| gear | 12 trapezoid teeth, tips rounded with 0.03, roots with 0.04, a bore of radius 0.3, sharp rims | small radii, many fillets, a cylinder that is no fillet |
| rod | cylinder of radius 0.5 with sharp rims | nothing is a fillet (the radius bound given to the detectors is 1.0, above the cylinder) |

Each with 2, 4, 8, 16 and 32 chords per quarter circle, as **CAD strips** (one quad per chord along the whole edge, one quad per
face, as an STL export of a CAD system gives) and **subdivided** (every edge longer than 1.5 chords of the smallest fillet split at
its midpoint, the same faceted surface with near uniform triangles: the tessellation the paper assumes; only where that stays
below 150k triangles), and for 8 chords also rotated by 37 degrees about (1, 2, 3) and moved. The triangle group carries the
class (planar, edge fillet, vertex fillet, cylinder) and the radius; `Solid.Sharp` is the solid before filleting.

## Detectors

| name | what | parameters given |
|---|---|---|
| curvature | `MeshPrincipalCurvature`: a triangle is a fillet if the mean of max(abs K1, abs K2) of its vertices is at least 1 / bound | bound = 2 x the largest fillet radius |
| curvature (oracle band) | the band of that curvature with the best F1 against the answer (40 x 40 bounds): the upper bound of any curvature threshold | the answer |
| DeFillet | `VoronoiDetector`: section 4.2 of the paper on `Delaunay3` (see the class comment for the differences) | paper defaults (tolerance 0.006, radius bound 0.06 of the bounding sphere radius), and with the bound of the other detectors |
| medial ball | `MedialDetector`: the largest empty ball per planar facet and side, accepted if it rolls in a groove (class comment) | bound = 2 x the largest fillet radius |

Scores are weighted by area. Radius error: area weighted median of abs(r - r_true) / r_true over the true positives.

`Defillet.cs` moves the vertices of the detected fillet triangles to the least squares point of the planes of the nearest
tangent supports (one closed-form step, connectivity unchanged) and compares with `Solid.Sharp`.

## Results

See the study for the tables and their reading. In short (`results/summary.md`): on CAD strips the curvature threshold reaches F1
0.72 (0.87 with the oracle band), the paper's detector as implemented here 0.43 (radius bound set; 0.05 with the paper's default bound, which is below the radii
of the box and the L profile), the medial ball 1.00 (worst 0.99); on the subdivided meshes 0.45 (0.55), 0.48 and 1.00. The reconstruction is exact on the box, the plate and the gear when the
labels are right and breaks where a support face shares vertices with a fillet (the caps of the L profile) or a few fillet
triangles were missed (the coarse sphere octants).
