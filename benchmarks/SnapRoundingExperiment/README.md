# Snap-rounding experiment

The measurement of [docs/design/SAFE_ROUNDING.md](../../docs/design/SAFE_ROUNDING.md) section 8: SafeRounding
(`MeshRoundingRepair`, the default of `ExactMeshBoolean`) against two prototypes for what it leaves.

- **A, separation**: the expansion stage of Milenkovic and Sacks extended to degenerate triangles (sequential linear
  programs over the vertices around a defect, one multi-vertex move checked exactly). Measured and closed: its
  prototype and the methods `A` and `AB` of this tool are on the branch `claude/snap-rounding-A-archive` only.
- **B, snap fallback** (`SnapFallback.cs`, only here): a second resolve of the result, then the vertices of the
  defect triangles snapped to grids of 2^j ulps and resolved again (after Valque and Lazard), within the drift left.

Methods: `plain` (SafeRounding off), `safe` (the default), `again` (safe, then one more resolve), `B`,
`Bpaper` (B with the published grid of Valque and Lazard, SAFE_ROUNDING.md section 7: [2^23, 2^24) per axis, hot voxels,
at most 5 iterations, no drift bound), and `patch` (the upper bound of a patch-wise B: the defects grown by three rings,
resolved alone, timed: an optimistic estimate of its cost, see SAFE_ROUNDING.md section 8.7).

Per result: clean (rounding clean and `MeshRobustRepair.NotCleanReasons` empty), the drift against the bound
(`MaxLength`) and in ulps of the largest coordinate, the Hausdorff distance to the exact result rounded (an upper
bound, tolerance 1e-2 of the bound; it includes removed components), removed components, the time of the boolean
and the method, and the exact d-separation certificate (`tests/geometry3Sharp.Tests/Harness/SeparationCertificate.cs`:
disjoint features farther apart than 2e = √3 ulp of the largest coordinate; `uncertified` counts the pairs closer).

## Rounding cost and the leads of 3D snap rounding (2026-11-12)

The measurements of [docs/design/SNAP_ROUNDING_3D.md](../../docs/design/SNAP_ROUNDING_3D.md), which holds the tables and
the commands (section 9):
- `trace/`: `instrument.py` patches `MeshRoundingRepair.cs` in a throw-away worktree (never committed to the library)
  with a phase trace, a separating-axis prefilter and a stop at the evaluation limit behind environment variables;
  `run_trace.sh`, `run_ab.sh`, `summarize.py`; raw output in `trace/results/`.
- `cull <case dir> <operation>`: the first full self-intersection search of a rounded result with tighter candidate
  culling (`CandidateCulling.cs`), an upper bound outside the library.
- `lattice <case> [seconds] [seed]`: a random search over representable positions with the triangles unchanged on what
  SafeRounding leaves (`LatticeProbe.cs`; `LATTICE_MAX_MOVED=1`, `LATTICE_PLATEAU=0` for the ablations).
  For the plateau variant of the position search (the note, section 10, 2026-11-15; not built): `LATTICE_GRID=8` draws
  from the 26 x 8 candidates of the built search, `LATTICE_STRICT=1` keeps a step only if it adds no defect,
  `LATTICE_STALL=<B>` rolls the steps since the last improvement back after B evaluations without one,
  `LATTICE_TARGET=degenerate` searches the degenerate triangles only (real leftovers, whose results also hold
  contacts). Tables: `trace/summarize.py plateau <rows> [<run safe rows>]` and `leftovers <rows>`; raw output `trace/results/plateau_*_2026-11-15.jsonl` and
  `thingi400_rounding_2026-11-15.jsonl` (what SafeRounding leaves on the 400 self-unions). The lemma:
  `tools/Z3Pilot/plateau_search.py`.

## Upper bounds of a rounding with a guarantee (2026-11-22)

The measurement of [docs/research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md](../../docs/research/SNAP_ROUNDING_3D_GUARANTEE_2026-11.md)
(the tables and their reading are there): `bound <case>` (`GuaranteeBound.cs`) prints one JSON line with what
SafeRounding leaves and removes today (each removed component with its thickness in cells of the grid a guaranteed
scheme would use), a lower bound of the output size of the algorithm of Devillers, Lazard and Lenhart (SoCG 2018;
counted from the slabs its step 2 cuts every face by, not built), and the same count with the pairs closer than a cell
on the patch around the defects that are left. `bound.py run <out.jsonl> <set>...` runs a set, one process per case,
resumable; `bound.py tables <out.jsonl>...` prints the tables. `bound selfcheck` checks the slab count on a
hand-counted case. The sets `scale` (two rotated cube-spheres at six
resolutions) and `drop` (valid small and thin solids that SafeRounding removes: a cube beside a large box, a slab
axis-aligned and rotated) are for this measurement; where a component was removed, the line also says whether the
same repair without the removal ends clean (`no_removal_clean`, a probe outside the boolean). Raw output:
`trace/results/bound_2026-11-22.jsonl`, `bound_scale_2026-11-22.jsonl`, `bound_drop_2026-11-22.jsonl`.

## Sets

All generated in code (`tests/geometry3Sharp.Tests/Harness/DegenerateFamilies.cs`), no mesh files:
- `clusters`: the 198 dense and folded clusters of `SafeRoundingDegenerateInputTests.DenseClusters_*`;
- `utm`: the 114 cases of `SafeRoundingBoundTests.Utm_*` (rotated, scale 1e-3, near UTM coordinates);
- `cylinder`: the six steps of `ExactBooleanIssueCasesTests.CylinderToolSteppedDown_RepeatedDifference`;
- `booleans`: the 198 of `SafeRoundingDegenerateInputTests.DegenerateInput_InBooleans_*`;
- BooleanComparison case directories (`@bc:list`, lines `<dir>:<Union|Intersection|Difference>`, the directories of
  `BooleanComparison export` / `pair`);
- a file list (`@list`): the self-unions of meshes, eg the Thingi10K 400 set of
  `benchmarks/MeshRepairBenchmark/fetch_data.py` (not committed: the meshes have their own licenses).

## Running

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
cd benchmarks/SnapRoundingExperiment
python3 run.py out/clusters.jsonl clusters --methods plain,safe -j 4          # all cases
python3 run.py out/clusters.jsonl clusters --methods again,B,Bpaper --left -j 4  # where safe is not clean
python3 summarize.py out/clusters.jsonl      # the tables, and the numbers of the owner's criterion 2
ls ../MeshRepairBenchmark/data/meshes/* > thingi.txt
python3 run.py out/thingi.jsonl @thingi.txt --methods plain,safe -j 4 && python3 run.py out/thingi.jsonl @thingi.txt --methods again,B,Bpaper --left -j 4
```

Each case runs in its own process with a time limit (`--timeout`, per method), so a pathological case cannot stop the
run; the output is appended one line per result, and a run resumes where it stopped. A single case:
`dotnet bin/Release/net10.0/SnapRoundingExperiment.dll run cluster:Cluster:0:4:5 safe B` (`SEP_DEBUG=1` prints the
pairs the certificate rejects, `SNAP_TRACE=1` every grid round of B with its time).

The results are in SAFE_ROUNDING.md section 8.
