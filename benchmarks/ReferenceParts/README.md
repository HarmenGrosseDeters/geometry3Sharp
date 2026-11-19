# ReferenceParts

A small suite of functional technical parts that measures what a part loses across a **chain** of operations, not per operation
(owner 2026-11-19). Its first failing steps decide which algorithm gets development time next. The parts, their checks and the chains
are one definition shared with the tests (`tests/geometry3Sharp.Tests/ReferenceParts/`, `ReferencePartsTests`, and
`tests/g3cli.Tests/ReferencePartsCliTests` for the export through `g3cli convert`); this program sweeps them over defect sets,
resolutions and size ratios.

    dotnet run --project benchmarks/ReferenceParts -c Release -- [--verbose] [--csv file] [--part flange|rib|screw] [--defects welded|soup|faulty|faulty+gap1e-5]

About 2 minutes on the 4-core container (the slow cases are the repair's solidify fallback). Committed run: `results-2026-11-19.csv`
(one row per part, defect set, parameter, chain and check). `--verbose` prints every step with every check value.

## The parts (built analytically, no boolean)

Every part is a triangle soup built from its own vertices (planar faces by ear clipping, bores as n-gon walls), so it is a closed
manifold with known target values: volume, bore radius and inradius, rib thickness, screw box. Coordinates are around (1000, 1000, 1000)
mm so that the float32 STL loses visibly (spacing 6.1e-5 there).

| part | construction | cut-out of the chain | functional checks |
|---|---|---|---|
| `flange` | block 80 x 80 x 20, bore r 8 along z and a cross bore r 4 along y (an n-gon each; they do not meet: an analytic intersection curve is out of scope) | pocket 20 x 25 x 10 in the top face | `pinMain`, `pinCross`: a pin of 0.97 x the bore radius passes: the certified distance from the bore's axis segment to the mesh (`CertifiedPointMeshDistance` at the ends of intervals, minus half the interval: a lower bound) is at least the pin radius; `volume` |
| `rib` | block 100 x 100 x 20 with a rib 60 x t x 15 on its top face (t 0.4 and 0.1), one closed solid | pocket 20 x 30 x 10, away from the rib | `ribConnected` (one component, rib top and base in it), `ribThickness` (the material interval across the rib's mid plane on 15 sample lines: all within 80-120 % of t); `volume` |
| `screw` | the same block and a separate screw (head r 0.3 s, shaft r 0.15 s, 16-gons) 0.1 s above it, size s = ratio x 100 | the same pocket, away from the screw | `screwSeparate` (exactly two bodies), `screwBox` (the smaller body's extents within 2 deviations + 1 % of s of the construction); `volume` |

`clean` (`MeshRobustRepair.NotCleanReason` empty) is reported for every step, but is descriptive: only the functional checks decide the
first failing step. `volume` is judged within `area x deviation + 1e-4 V` of the construction's (the cut-out's volume after the subtract).

Deliberate defects (`DefectSet`, applied after the construction): `welded` (the clean part), `soup` (unwelded: three vertices per
triangle, an STL-like soup), `faulty` (soup, every 13th triangle flipped, every 17th duplicated), `faulty+gap1e-5` (and the top plate moved
by 1e-5 of the part size along its normal: a crack along its edges).

## The chains

- `library`: construct, `MeshRobustRepair` (defaults), `ExactMeshBoolean` Difference with the cut-out, `Reducer` (MaxDeviation 0.02 mm, 1e-5 x the
  housing for the screw part and at most a tenth of the screw, `MaxDeviationTwoSided`, `PreventSelfIntersections`, down to a tenth of the triangles or as far as the
  bound allows), binary STL (float32) round trip, OBJ round trip.
- `pipeline`: construct, `SolidBooleanPipeline` (`InputRepair = AutoRepairThenRobust` set explicitly, Difference, the same deviation, two-sided,
  `TargetFraction` 0.1) in one step, STL, OBJ.
- `g3cli` (tests only): the last mesh written as STL, `g3cli convert` to OBJ, read back.

After every step every check runs on the step's mesh. The first failing step is the first step after the construction at which a functional
check fails, or at which the step itself fails; the construction with its deliberate defects is the starting point and is not judged. A
failed step stops the chain.

## First failing step (library / pipeline), `-` = every step passes

| part | parameter | welded | soup | faulty | faulty + gap 1e-5 |
|---|---|---|---|---|---|
| flange | n=24 | - / - | - / - | repair / pipeline | repair / - |
| flange | n=64 | - / - | - / - | repair / pipeline | repair / pipeline |
| flange | n=128 | - / - | - / - | - / pipeline | - / pipeline |
| rib | t=0.4 | - / - | - / - | - / - | - / - |
| rib | t=0.1 | repair / - | repair / - | repair / - | repair / - |
| screw | ratio 1e-1 .. 1e-4 | - / - | - / - | - / - | - / - |
| screw | ratio 1e-5 | repair / pipeline | repair / pipeline | repair / pipeline | repair / pipeline |
| screw | ratio 1e-6 | repair / pipeline | repair / pipeline | repair / pipeline | repair / pipeline |

Measured values behind the failures (canonical mm; `-` pass; the pin must clear 7.76 (main) and 3.88 (cross); the construction's n-gon has 7.93 and 3.97 at n=24):

| case | step | value |
|---|---|---|
| flange n=24 faulty, library | repair (path Solidify) | `pinMain` 7.20 (the voxel grid of the solidify, 128 cells over 80 mm, is 0.6 mm), later steps carry it on |
| flange n=64 faulty, library | repair (Solidify) | `pinCross` 3.15 |
| flange n=24 / 64 / 128 faulty, pipeline | pipeline | `volume` -0.9 / -1.1 / -0.7 %; `pinMain` 7.70 / 7.37 / 7.32; `pinCross` 3.88 (passes) / 0.04 / 2.89 |
| flange, duplicated faces only (test), library and pipeline | repair / pipeline (ExactResolve) | `pinMain` 7.45 |
| rib t=0.1, clean welded input, library | repair (path Repair, 28 to 26 triangles) | `ribThickness` 0.025 (0.1 built), `ribConnected` and `volume` pass |
| rib t=0.1 faulty, library | repair (Solidify) | `ribConnected` fails (the rib is gone from the solid), thickness 0 |
| screw ratio 1e-5, 1e-6 | repair (ExactResolve; degenerate triangles) and the pipeline's boolean | the screw is gone: one body; `volume` still passes (the screw is 1e-9 of it) |
| flange in UTM coordinates (test) | stl | float32 spacing 0.5 at y = 5.7e6: the bore no longer takes the pin |

Diagnosis of the screw loss (`ReferencePartsTests.Screw_AtRatio1e5_PinnedFailure_TheBooleanDropsTheScrew`): `ExactMeshBoolean` itself
drops the separate screw below a ratio of about 6e-5 (kept at 7e-5): SafeRounding's removal of closed components that are thinner than its
threshold (`Rounding.RemovedComponents` = 1; `RoundingMaxDisplacement = 1e-9` does not change it; `SafeRounding = false` keeps the screw).
The screw is a valid solid in the input and the result has no trace of it.

Float32: the STL step changes the measured pin clearance by about 1e-5 mm at offset 1000 (visible, far below the pin's margin of 0.17 mm)
and the `screwBox` error of the 0.01 mm screw (ratio 1e-4) by 1e-6 .. 3e-5 mm (library 2.26e-4 to 2.26e-4, pipeline 7.42e-4 to 7.75e-4; the
tolerance is 2.1e-3): the screw still passes. The reductions and the pipeline's reduction never failed a check at these deviation bounds.

## What this means for priorities

1. **Repair is the weak link of both chains, not the boolean or the reduction.** Every failing step of the flange and rib rows is the repair
   (or the pipeline's repair of its input): flipped and duplicated faces send `MeshRobustRepair` to ExactResolve or Solidify, which move a
   bore wall by 0.2-0.7 mm (a pin that fits the construction no longer fits) or drop a thin rib; the `Repair` path itself thins a clean 0.1 mm
   rib to 0.025. The wanted algorithm: a repair that fixes orientation and duplicates first (a combinatorial step: no geometry moves) and
   returns with a certified deviation bound (as `Reducer.MaxDeviation` does), and a gate that refuses a fallback that moves a feature
   beyond a stated bound instead of accepting it as clean. The first task is the clean welded rib: a repair of an input that is already clean
   must not change it.
2. **`SolidBooleanPipeline` inherits it**: with duplicated or flipped faces its result misses the volume by 0.7-1.1 % and a bore by up to 0.6 mm
   at the pipeline step; the same cause (the input repair), a better repair fixes both.
3. **Silent loss of a small separate body** (ratio below 6e-5 of the diagonal): the boolean's SafeRounding removes it and only
   `Rounding.RemovedComponents` shows it. A solid that is valid and exactly represented in the input should stay, or the pipeline should
   report the removal as a failure of the run (owner decision: default or opt-in).
4. **Export**: float32 STL is lossless enough at offset 1000 for features above about 1e-3 of the part, and breaks the part at UTM
   coordinates. A writer that recentres (or writes float64, or warns when the float32 spacing exceeds a stated fraction of the smallest
   feature) is cheap and removes a whole failure class; `g3cli convert` has the same behaviour.
5. **Reduction needs no time from this suite**: with a two-sided certified bound no check failed in any chain, and the screw and the rib
   survived it at deviations below their size.

Limits of the suite: one cut-out, one deviation bound per part, no intersecting cross bore (analytic construction of the intersection curve
is out of scope), pins as straight segments with a Lipschitz-certified clearance. A part that is not in this list is not covered by it.
