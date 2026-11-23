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

Per case one line: the number of different exact results (as sets of triangles by position), the pipeline's statuses, how many
orders are `RoundingClean` in the default mode, the number of different default-mode results and the relative spread of their
volumes; where the repair's counts differ between the orders, one line per count vector. The totals follow at the end.

Raw outputs: [results/](results/) (2026-11-23, master 083ef07b, 4-core cloud container, .NET 10.0.115).
