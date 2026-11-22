# MagnitudeRobustness

Probes of the magnitude study [docs/research/MAGNITUDE_ROBUSTNESS_2026-11.md](../../docs/research/MAGNITUDE_ROBUSTNESS_2026-11.md)
(#1377, #1376, #1352): what the geodesic and intrinsic classes and `DualContouring` return for an input scaled by 2^e, which
expression leaves the double range first, and what an exact power-of-two prescaling of the input gives and costs. The
prescaling is a wrapper in this project; the library is not changed.

```
dotnet run --project benchmarks/MagnitudeRobustness -c Release -- [stage ...]
```

| Stage | What it prints | Time |
|---|---|---|
| `roots` | the edge lengths of the test sphere, the range of the product of four lengths, and one triangle through the area, angle and cotangent formulas at ten scales | < 1 s |
| `sweep` | per operation, the answer at every tested exponent from -1070 to 1020 as runs: `exact` (the unit result times 2^e, bit for bit), `status X`, `threw X`, or `another ...` (a success with another result) | 11 s |
| `prescale` | the same sweep through the wrapper `Program.Prescaled` (the largest coordinate into [1, 2), run, lengths scaled back) | 11 s |
| `dc` | `DualContouring`: the values times 2^e, plain and with the wrapper `DcStage.Prescaled`; the level sets of exp(-40 r^2); the wrapper against the plain run on five ordinary fields; the wrapper's time | 14 s |
| `dcbig` | `DualContouring` above 2^100, split into field and geometry, geometry only, values only, per option; the counters of the simplification around 2^262 | 6 s |
| `cost` | plain against prescaled, median of 5, on spheres of 3176, 13256 and 54152 vertices | 215 s |
| `formula` | the area formula with a power-of-two normalization on a rare path: bits against today's formula, ns per call | 9 s |

Inputs are procedural: the sphere of 152 vertices at (0.1, 0.2, 0.3) of the second metamorphic hunt, a sphere of 728 vertices
for the quad remesher, the sheared 3 x 3 grid of the comment on #1352, and the distance field of a sphere.

`results/master_e875d747.txt` is the raw output of all stages on master `e875d747` (Linux x64, net10.0, 4 cores).
`experiments/area_from_lengths.patch` is a local experiment, **not applied**: `IntrinsicTriangulation.area_from_lengths` with a
power-of-two normalization outside 2^-250 .. 2^250; `results/experiment_area_from_lengths_e875d747.txt` is the `sweep` stage
with it applied (section 5 of the note).
`experiments/common_subdivision_nan_safe.patch`, also **not applied**: the two area checks of `IntrinsicCommonSubdivision` made
NaN-safe; with it `ExtractCommonSubdivision` answers `Inconsistent` from 2^256 up and is unchanged below.
