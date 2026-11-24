# WrapAccuracyProbe

Measurement and prototype for two ideas on the accuracy of `MeshAlphaWrap` (research note
[docs/research/ALPHA_WRAP_ACCURACY_2026-11.md](../../docs/research/ALPHA_WRAP_ACCURACY_2026-11.md)). No library change, public API only
(the helpers `Procedural.cs` and `Snap.cs` of `benchmarks/SharpFeatureProbe` are linked in; the project needs no `InternalsVisibleTo`).
Every benchmark project under `benchmarks/*/` is built by the CI step of `ci.yml`, so the probe needs no registration.

- **Spike A, snap back.** The raw wrap (`MeshAlphaWrap`, `Alpha` = diagonal / 4, 20, 100, `Offset` = diagonal * 1e-3, 1e-4, 1e-5), then
  *naive* (every vertex to its nearest input point, no check) and *safe* (the same, a vertex only moves while exact checks hold for its
  ring, else 1/2, 1/4, 1/8 of the way or not at all, up to 4 rounds). The checks of the safe version (`SnapBack.cs`): no flipped triangle,
  no degenerate triangle (area >= 1e-12 diagonal^2, the threshold of `MeshRobustRepair.NotCleanReason`, and not exactly collinear), no
  intersection of a ring triangle with another triangle beyond the shared vertices or edge (`ExactTriangleIntersection.IntersectsBeyondShared`
  against a `TriangleHashGrid`, the check of the sharp-feature probe's `Snap.cs` and `AlphaWrapQualityPass`). Not checked, unlike the wrap's
  own passes: the distance to the input and intersections with it (the result is meant to touch it).
- **Spike B, keep the input, wrap only the gaps.** Solids with holes and an open sheet: area share of the result on no input surface
  ("added") and of the input on no result surface ("lost"), far parts of the wrap as patches against the input's boundary loops, for the
  wrap, the wrap after snap back, `MeshAutoRepair` and `MeshRobustRepair`.

Metrics per result: triangles, wall time (best of up to 3 runs when one run is under 5 s), signed volume error, certified one-sided Hausdorff
upper bounds both ways (`MeshHausdorffDistance`, tolerance 0.05 offset, `*` in the tables = not converged in 40 s), p50 / p90 / p99 of 20000
area-stratified sample distances both ways, the verdict of `MeshRobustRepair.NotCleanReason`, an independent self-intersection count
(`MeshRepairMetrics.SelfIntersectionsAtMost`), and for the snap back the share of vertices that moved fully / partially / not.
Timings are relative: other sessions ran on the machine, and up to three probe processes ran at the same time here.

## Commands

```
dotnet build benchmarks/WrapAccuracyProbe -c Release
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- selftest
# Spike A: proc:all = box lshape slot0.20 slot0.05 slot0.01 plate1e-2 plate1e-3 plate1e-4 cylinder sphere box_far (box_far: translated by 2^22)
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- a out.jsonl proc:all [--alphas 4,20,100] [--offsets 1e-3,1e-4,1e-5] [--limit 60]
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- a out.jsonl @list.txt          # a list file: one mesh path per line
# Spike B: sheet, hole:<box|cylinder|sphere>:<face|p:<share>|e:<share>|slit>, or mesh paths (open meshes)
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- b out.jsonl @results/spikeB_proc_inputs.txt
# Thingi10K selection (a local copy of the dataset; nothing is copied into the repository)
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- classify <dataset>/raw_meshes --seed 2611 --per-class 8 --out results/thingi
# (the committed results/thingi_*.txt lists name the meshes as <dataset>/raw_meshes/<id>.stl: replace <dataset> with the local copy's path; classify writes real paths)
# tables
dotnet run -c Release --project benchmarks/WrapAccuracyProbe -- report a|b out.jsonl [more.jsonl]
python benchmarks/WrapAccuracyProbe/summarize.py a out.jsonl [more.jsonl]       # hypothesis summaries (H1-H4)
```

A rerun skips the (input, alpha, offset) rows already in the output file.

## Self-test (`selftest`)

The numbers depend on the ring check of the snap back and on the metrics, so `selftest` checks them (exit code 0 = all pass):
a slab and a pyramid with known moves (a harmless move is accepted; a fold, a collapse onto a neighbour and a move through the opposite layer
are rejected for their reason); each check switched off lets its case through (mutation of the test itself); on a battery of five wraps the
safe result is clean by `NotCleanReason` and has 0 self-intersections by the independent `MeshRepairMetrics` count, while with all checks off
four of the five are not clean (and with only the self check off the cylinder has self-intersections, so the incremental check is needed and
is seen by the independent one); the signed volume of a 10 x 6 x 4 box is 240 from any origin; the Hausdorff interval of a box shifted by
0.25 contains 0.25; the far share of a mesh against itself is 0 and against a copy shifted by 5 is 1.

## Inputs

- Procedural (no mesh files): `Inputs.cs`; the polygon prism and polycube generators are those of `benchmarks/SharpFeatureProbe/Procedural.cs`.
- Thingi10K (the T1 list of the earlier wrap studies and a seeded sample): the ids, the seed and the selection rule are in
  [results/thingi_selection.md](results/thingi_selection.md); the meshes are read from a local copy and never copied or committed
  (licenses are individual; results name ids and numbers only).

## Results

Raw JSONL and logs in `results/`; the tables in [results/tables_A_procedural.md](results/tables_A_procedural.md),
[results/tables_A_thingi.md](results/tables_A_thingi.md), [results/tables_B_procedural.md](results/tables_B_procedural.md),
[results/tables_B_thingi.md](results/tables_B_thingi.md) (generated by `report`), the summaries in `results/summary_*.txt`.
The research note has the analysis.
