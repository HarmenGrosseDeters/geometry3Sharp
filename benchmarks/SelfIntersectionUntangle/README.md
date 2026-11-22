# SelfIntersectionUntangle

The measurement behind
[docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md](../../docs/research/SELF_INTERSECTION_UNTANGLING_2026-11.md):
is a local untangling that keeps the connectivity worth running before the alpha wrap of `MeshRobustRepair`? It changes
no library code and is not a test: it measures, and the note draws the conclusions (in short: no-go for untangling
the input, which the exact resolve already repairs; open, pending a real-mesh run, for the solidify's contour).

```
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle                 # 21 solids x 2 frames x 8 methods
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- open         # the same solids with a hole: the repair's path, with and without untangling first
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- pipeline     # SolidBooleanPipeline (defaults) on each solid
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- solidify     # thin inputs solidified coarsely: can a self-intersecting contour be untangled?
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- contour-probe  # procedural inputs through the repair: the contours it stops its solidify on
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- contour <mesh file> [rows.tsv]   # the same for one mesh file (the workflow below)
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- check        # the corpus self-check only
dotnet run -c Release --project benchmarks/SelfIntersectionUntangle -- only <part of a case name>
python3 benchmarks/SelfIntersectionUntangle/summarize.py benchmarks/SelfIntersectionUntangle/results/run1.txt
```

Raw output in [results/](results/) (library `dc572439`, 2026-11-22, Linux container, 4 cores, `net10.0`, Release):
`run1.txt` (default mode, about 5 minutes, most of it the Hausdorff measurements and the wraps), `open.txt`,
`pipeline.txt`, `solidify.txt`. Counts and distances are deterministic (fixed seeds); the times vary between runs.

## Parts

- **Cases.cs**: 21 procedural solids, each one closed, consistently oriented mesh whose only defect is that it
  intersects itself; 17 have an *intent* (the clean mesh the defect was injected into, same connectivity). Classes:
  `parts`, `pushed`, `flap`, `coplanar`, `cad`. The builders of `../SolidBooleanAudit/Corpus.cs` are linked in. Every
  start checks, in both frames, that each given mesh has self-intersections and, apart from inside-out components (a
  whole inverted cell), no other defect (`MeshRobustRepair.FindDefects`), and that each intent is clean and has the given
  mesh's vertex and triangle counts; the program exits with 2 otherwise, and if no case matches `only`.
- **Untangler.cs**: the prototype (floating point, vertices only move): a signed push from the winding number in front
  of each triangle, a minimum-translation push per intersecting pair, one-ring relaxation for folds, optional diffusion
  of the displacement, and a pull-back to the smallest clean fraction of the displacement. A result counts only if
  `MeshRobustRepair.NotCleanReason` is "" (exact self-intersection check); for an open input, if no exact
  self-intersection is left.
- **Program.cs**: the four prototype variants against `MeshRobustRepair` as it is (with its `ResultPath`),
  `ExactMeshBoolean.Resolve`, `MeshWindingNumberSolidify` (resolution 128) and `MeshAlphaWrap` (1/300, 1/3000, the
  repair's defaults). Columns: clean or the reason, time, `h(result->input)` and `h(input->result)`
  (`MeshHausdorffDistance` upper bounds in units of the input's diagonal, to 1e-4), the distance to the intent, the
  volume against the intent's, components and Euler characteristic against the intent's; for the prototype the largest
  vertex move. The run ends with the number of distances that did not converge to the tolerance (0 in `run1.txt`).

## Results (2026-11-22)

Clean results (exact check) of 42 runs, and the deviation of the clean ones (medians; distances in diagonals):

| method | clean | h(result→input) | max | h(input→result) | H(intent) | topology of the intent kept | ms (all runs) |
|---|---|---|---|---|---|---|---|
| untangle local | 22 / 42 | 0.0060 | 0.0974 | 0.0113 | 0.0152 | 22 / 22 | 34 |
| untangle local + diffusion | 22 / 42 | 0.0063 | 0.0974 | 0.0116 | 0.0152 | 22 / 22 | 34 |
| untangle signed | 40 / 42 | 0.0151 | 0.1715 | 0.0159 | 0.0394 | 34 / 34 | 44 |
| untangle signed + diffusion | 40 / 42 | 0.0152 | 0.1717 | 0.0159 | 0.0394 | 34 / 34 | 42 |
| `MeshRobustRepair` (exact resolve in all 42) | 42 / 42 | 0.0001 | 0.0011 | 0.0521 | 0.0864 | 14 / 34 | 38 |
| `ExactMeshBoolean.Resolve` alone | 40 / 42 | 0.0001 | 0.0001 | 0.0548 | 0.0898 | 14 / 32 | 4 |
| solidify alone | 42 / 42 | 0.0030 | 0.0094 | 0.0546 | 0.0865 | 13 / 34 | 546 |
| alpha wrap alone | 42 / 42 | 0.0027 | 0.0037 | 0.0046 | 0.0743 | 26 / 34 | 1813 |

- `open`: with a hole, `MeshRobustRepair` ends in the exact resolve in 42 of 42 runs, clean; after untangling
  (40 of 42 untangled) in `Repair` 37 times and in the exact resolve 3 times. Never in the solidify or the wrap.
- `pipeline`: 17 of 21 `Success` and clean, 1 `Success` with a result that is not clean ("self-intersections",
  `OnlyContacts`), 3 refused with `InsideOutInput`.
- `solidify`: of 72 coarse solidifies of 12 thin inputs, 3 are closed with self-intersections as their only defect;
  the signed prototype untangles all 3 in one round, largest vertex move 2e-13 to 7e-11 of the diagonal.

The per-class counts and the readings are in the note.

## The solidify contour before the wrap (option D of the note)

`MeshRobustRepair` takes the alpha wrap where its solidify's contour intersects itself. **Contours.cs** measures that
stage without changing the library: it runs the repair as it is and, if the solidify was stopped on self-intersections
(`MeshRobustRepair.Solidify.StoppedOnSelfIntersections`, the raw contour in `Solidify.Result`), tries on that contour
the existing contact separation (`MeshRobustRepair.SeparateTouchingVertices`) and the signed prototype, each judged by
`NotCleanReason`. It then runs the solidify's skipped cleanup and reduction (`FinishPostProcess`) on the raw, the
separated and the untangled contour and judges again: a clean finished contour is the solidify result the repair would
go on with instead of the wrap (its later checks, eg for lost parts, are not run here; and the finish runs on the
contour mapped back from the repair's working frame, the same computation up to a power-of-two scale, not bit for
bit). The prototype's rounds are capped at 150 s per contour. Columns (tab-separated, one row per input): the repair's path and time, the contour's triangles, defects
and intersecting pairs, the cell size, the two attempts with the largest vertex move per diagonal and per cell, the
three finished results, and the distance result → input of the wrap and of the finished untangled solidify (upper
bounds to 1e-4 of the diagonal; with clean parts passed through, the diagonal is still the whole input's).

- `contour-probe` ([results/contour_probe.txt](results/contour_probe.txt), about 10 minutes): 19 thin or open procedural
  inputs in 2 frames at solidify resolutions 24, 48 and 96, the repair stage and the exact resolve switched off so that
  the repair reaches its solidify. The repair stops its (adaptive) solidify on 2 of 114 runs (12 thin shards, a triangle
  missing in each). Both contours are untangled clean (2 rounds and 1 round, largest move 4e-11 and 1.2e-4 of the
  diagonal, 8e-9 and 0.05 of a cell) and their finished solidify is clean; the contact separation moves no vertex. Without
  any change the finished solidify is clean for the first (the contact goes away in the cleanup) and not for the second.
  The finished solidify lies 5.1 % of the diagonal from the open input, the wrap 0.37 %: for an open input the solidify
  is the coarser repair even when it is clean.
- **Real meshes:** `.github/workflows/untangle-contours.yml` (manual, GitHub-hosted) fetches the 36 Thingi10K models of
  [thingi_wrap_ids.txt](thingi_wrap_ids.txt) at run time (the meshes that reached the wrap in
  `../MeshRepairBenchmark/cases/robust_stages_2026-10-25.csv` and in the later runs of its README; no mesh is
  committed or uploaded), runs `contour` with one process per mesh and a timeout (600 s by default: about 1 runner hour
  expected, at most 6), and writes the table of `contour_summary.py`, with the ids that got no row, to the step summary.
  Results: pending the first run.
