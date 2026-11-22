# SelfIntersectionUntangle

The measurement behind
[docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md](../../docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md):
is a local untangling that keeps the connectivity worth running before the alpha wrap of `MeshRobustRepair`? It changes
no library code and is not a test: it measures, and the note draws the conclusions (in short: no-go, the exact resolve
already repairs these inputs without moving the surface).

```
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle                 # 21 solids x 2 frames x 8 methods
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- open         # the same solids with a hole: the repair's path, with and without untangling first
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- pipeline     # SolidBooleanPipeline (defaults) on each solid
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- check        # the corpus self-check only
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- only <part of a case name>
python3 benchmarks/SelfIntersectionUntangle/summarize.py benchmarks/SelfIntersectionUntangle/results/run1.txt
```

Raw output in [results/](results/) (library `dc572439`, 2026-11-22, Linux container, 4 cores, `net10.0`, Release):
`run1.txt` (default mode, about 5 minutes, most of it the Hausdorff measurements and the wraps), `open.txt`,
`pipeline.txt`. Counts and distances are deterministic (fixed seeds); the times vary between runs.

## Parts

- **Cases.cs**: 21 procedural solids, each one closed, consistently oriented mesh whose only defect is that it
  intersects itself; 17 have an *intent* (the clean mesh the defect was injected into, same connectivity). Classes:
  `parts`, `pushed`, `flap`, `coplanar`, `cad`. The builders of `../SolidBooleanAudit/Corpus.cs` are linked in. Every
  start checks that each given mesh has self-intersections and, apart from inside-out components, no other defect
  (`MeshRobustRepair.FindDefects`) and that each intent is clean; the program exits with 2 otherwise.
- **Untangler.cs**: the prototype (floating point, vertices only move): a signed push from the winding number in front
  of each triangle, a minimum-translation push per intersecting pair, one-ring relaxation for folds, optional diffusion
  of the displacement, and a pull-back to the smallest clean fraction of the displacement. A result counts only if
  `MeshRobustRepair.NotCleanReason` is "" (exact self-intersection check); for an open input, if no exact
  self-intersection is left.
- **Program.cs**: the four prototype variants against `MeshRobustRepair` as it is (with its `ResultPath`),
  `ExactMeshBoolean.Resolve`, `MeshWindingNumberSolidify` (resolution 128) and `MeshAlphaWrap` (1/300, 1/3000, the
  repair's defaults). Columns: clean or the reason, time, `h(result->input)` and `h(input->result)`
  (`MeshHausdorffDistance` upper bounds in units of the input's diagonal, to 1e-4), the distance to the intent, the
  volume against the intent's, components and Euler characteristic against the intent's.

## Results (2026-11-22)

Clean results (exact check) of 42 runs, and the deviation of the clean ones (medians; distances in diagonals):

| method | clean | h(result→input) | max | h(input→result) | H(intent) | topology of the intent kept | ms |
|---|---|---|---|---|---|---|---|
| untangle local | 22 / 42 | 0.0060 | 0.0974 | 0.0113 | 0.0152 | 22 / 22 | 34 |
| untangle local + diffusion | 22 / 42 | 0.0063 | 0.0974 | 0.0116 | 0.0152 | 22 / 22 | 34 |
| untangle signed | 40 / 42 | 0.0151 | 0.1715 | 0.0159 | 0.0394 | 34 / 34 | 42 |
| untangle signed + diffusion | 40 / 42 | 0.0152 | 0.1717 | 0.0159 | 0.0394 | 34 / 34 | 44 |
| `MeshRobustRepair` (exact resolve in all 42) | 42 / 42 | 0.0001 | 0.0011 | 0.0521 | 0.0864 | 14 / 34 | 36 |
| `ExactMeshBoolean.Resolve` alone | 40 / 42 | 0.0001 | 0.0001 | 0.0548 | 0.0898 | 14 / 32 | 4 |
| solidify alone | 42 / 42 | 0.0030 | 0.0094 | 0.0546 | 0.0865 | 13 / 34 | 532 |
| alpha wrap alone | 42 / 42 | 0.0027 | 0.0037 | 0.0046 | 0.0743 | 26 / 34 | 1680 |

- `open`: with a hole, `MeshRobustRepair` ends in the exact resolve in 42 of 42 runs, clean; after untangling
  (40 of 42 untangled) in `Repair` 37 times and in the exact resolve 3 times. Never in the solidify or the wrap.
- `pipeline`: 17 of 21 `Success` and clean, 1 `Success` with a result that is not clean ("self-intersections"),
  3 refused with `InsideOutInput`.

The per-class counts and the readings are in the note.
