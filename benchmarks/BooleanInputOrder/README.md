# BooleanInputOrder

The measurement harness of [docs/research/BOOLEAN_INPUT_ORDER_2026-11.md](../../docs/research/BOOLEAN_INPUT_ORDER_2026-11.md):
`ExactMeshBoolean` (default `SafeRounding`) and `SolidBooleanPipeline` on the same geometry with the input triangles and
vertex ids in another order, compared stage by stage. No library change; the inputs are generated (two nearly coincident
curved plates), no mesh files.

```bash
dotnet run --project benchmarks/BooleanInputOrder -c Release -- plates --orders 8 --stages --numbered
dotnet run --project benchmarks/BooleanInputOrder -c Release -- tessellations --orders 6 --numbered
```

| argument | what |
|---|---|
| `plates` | 396 cases: a plate (bowl, wave, dome; grids 6, 10, 14; thickness 0.02, 0.002) minus a copy that is shrunk, turned, turned and shifted by 1e-4 to 1e-10 or tessellated on another grid, and the thin strip it shares with a lifted copy (Intersection) |
| `tessellations` | 624 cases: two tessellations of one plate (grids 5 to 8 against +1, +2), one turned by 1e-5 to 1e-8, as `A - B` and `B - A` |
| `--orders N` | the orders per case: as given, triangles reversed, vertex ids reversed, both reversed, then seeded shuffles of both (default 8) |
| `--stages` | also the kept pieces of the arrangement (no simplification of flat regions), per order |
| `--numbered` | also the repair on the exact result numbered by its geometry (option A of the note), with the repair's settings of `StrictRounding` as the pipeline runs it; the emulation reads the boolean's private per-vertex data by reflection and is checked against the library's own repair in every run |
| `--filter text` | only the cases whose name contains the text |

One more mode searches for a state of the rounding repair (docs/research/ROUNDING_DEGENERATE_LEFT_2026-11.md, #1465):

```bash
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 4000 --seed 3 --tiny
dotnet run --project benchmarks/BooleanInputOrder -c Release -- search --count 1 --case wave,1,2,0.2,2e-9,-1.1,0,D
```

| mode | what |
|---|---|
| `search [--nojoint]` | a seeded sample of plate pairs (surface, grid n and offset, thickness, rotation 1e-5 to 1e-9 and its axis ratio, shift away from the origin, both ways round) through `ExactMeshBoolean` with `SafeRounding`; per case the class of the repair (clean, left-over degenerate triangles only, intersections, contacts, removed resolvable, other); a line `HIT` for each result that leaves only degenerate triangles, with the repair counts and whether the same result comes in five other orders. `--tiny` n 1 to 3, `--n N` fixes n, `--only I` runs the I-th case of the sample, `--case surface,n,dn,thickness,rotation,ratio,shift,D\|Drev` runs one case and prints the `StrictRounding` error text |
| `search --pipeline` | for every case whose repair leaves a degenerate triangle (class containing `degenerate`) or only a resolvable removed component, also `SolidBooleanPipeline` on the same inputs with `AutomaticSteps = None` and with the default; per run a line `PIPE` (status / BooleanStatus, route, the Grid step's cell and text, `Report.Clean`, `Report.OnlyContacts`, `MeshRobustRepair.NotCleanReason`, relative volume difference against the non-strict boolean of the unsnapped inputs), and a tally per group, mode and outcome at the end |
| `search --pipeline --verify [--controls N]` | for the same cases, per case a line `VER`: the volumes about the box centre of the exact result (`SafeRounding = false`), the non-strict result, the exact result of the snapped inputs, the retry's result, and `V_mc`, an estimate from the winding numbers of the inputs along vertical lines (no boolean), with its standard error, the two rules of the note's section 9 and `VERORIGIN` (the origin-based volume of section 8); `--controls N` adds N clean runs; `G3_VERIFY_DETAIL=1` adds the retry's components, rounding replica and symmetric difference |
| `joint --case <spec> [--case ...] [--sizes N] [--single]` | on the state the repair leaves (spec as of `search --case`), for each degenerate triangle the joint moves of two free corners (each by its room over 2^j, j < N = 5, along the 26 directions of the position search: 16 900 per pair) counted by the rule of `improves` (fewer degenerate triangles, no more intersecting pairs, no triangle turned over); the best pair is checked from scratch on the whole mesh; `--single` enumerates one corner (must accept nothing). Note section 10.1. Reflection on the repair's private methods |
| `cost [--family F --k K --count N --seed S --frame NAME] [--plate spec] [--repeats R]` | the time and the effort of the repair's joint stage on the given inputs with the stage off and on (the internal switch, by reflection): the median ms of R runs (three settings: the stage off, the free rounds only, the extension of 2026-11-26: pairs with a fixed corner and free pairs with two levels), the left-over defects, `EvaluationLimitReached`, the joint moves, the evaluations and the proposals that reached `improves`; a generated degenerate family (Union with an empty mesh) or a plate of `search --case` |
| `families` | the generated degenerate families and near-coincident sphere and box pairs through the pipeline with and without the default retry; for each retry success the volume against the winding-number estimate |

Two more modes compare a library change before and after (run the same harness on both builds):

```bash
dotnet run --project benchmarks/BooleanInputOrder -c Release -- timing 5
dotnet run --project benchmarks/BooleanInputOrder -c Release -- corpus
```

| mode | what |
|---|---|
| `timing [repeats]` | the default-mode boolean on 8 procedural cases of 4k to 58k result triangles (spheres, grid boxes with coplanar faces, plates): per case the median total time and the medians of the simplification, of the ordering of the pieces (`assembly.order_us`, 0 before #1464) and of the rounding |
| `corpus` | how many results of the generated degenerate families (`DegenerateFamilies`: Cluster, FoldedCluster, Collinear, Sliver; 7 sizes, 3 densities, 6 seeds) are clean in the default mode and succeed with `StrictRounding`, in three frames |

Per case one line: the number of different exact results (as sets of triangles by position), the pipeline's statuses, how many
orders are `RoundingClean` in the default mode, the number of different default-mode results and the relative spread of their
volumes; where the repair's counts differ between the orders or a result is not clean, one line per count vector. The totals follow at the end.

Raw outputs: [results/](results/) (2026-11-23, 4-core cloud container, .NET 10.0.115): `plates_*` and `tessellations_*` on master
083ef07b; `timing_before_*`, `corpus_before` on the PR's commit before the library change (cce2d1f3) and `timing_after_*`, `corpus_after`
with it, baseline and candidate alternating in one job.
