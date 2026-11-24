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
same repair without the removal ends clean (`no_removal_clean`, a probe outside the boolean) and what became of
each removed component (`no_removal_volume_ratio`: its volume after over before, 1 = kept as it was). Raw output:
`trace/results/bound_2026-11-22.jsonl`, `bound_scale_2026-11-22.jsonl`, `bound_drop_2026-11-22.jsonl`.

## The cost of the rounding: box neighbours, `near`, tree and setup (2026-11-22)

The measurement of [docs/research/ROUNDING_COST_2026-11.md](../../docs/research/ROUNDING_COST_2026-11.md) (hard problem
3, parts b to d; the tables and their reading are there). Nothing here is library code.

- `gen <shape> <dir>` (`ProceduralThin.cs`) writes a procedural case with long thin triangles on tilted flat faces:
  `cyl:<n>`, `gear:<n>`, `fins:<n>`, `plate:<n>` and the control `sphere:<n>`, each with the rotated, shifted copy of
  `BooleanComparison export` as B. The set of the note: `gear:500 cyl:8000 fins:400 plate:24 sphere:60`.
- `cull <dir> <op> --variants <list> [--parts k] [--visits] [--serial]` (`CandidateCulling.cs`) repeats the first full
  search outside the library with other candidate sets; every variant must report the same intersecting pairs
  (`same_pairs`). New variants: `enum`, `own-enum` (the enumeration alone), `box+axesf` (the separating axes of section
  5.3 of SNAP_ROUNDING_3D.md, cached flat), `box+dop` (an 18-DOP per triangle), `split`, `split+axes`, `split+axesf`,
  `split+dop`, `split-enum` (a tree over split-clipped sub-boxes, `SplitBoxes.cs`). `--visits` adds the nodes a query
  visits in the library's tree and in a median-split tree.
- `trace/instrument_phases.py` patches `MeshRoundingRepair.cs` (and, with a second argument, `ExactMeshArrangement.cs`)
  in a throw-away worktree of master `57623890`: `G3_ROUNDING_PHASES=1` prints the phases of every `Apply`, the parts
  of `near` and the tree builds; `G3_FIRST_SEARCH=axes|split|split+axes` and `G3_TREE_MEDIAN=1` switch the candidates
  on inside the rounding. `trace/run_ab_phases.sh` runs the A/B, `trace/summarize_phases.py` prints the tables
  (`phases`, `bound`, `near`, `ab`, `absum`, `cull`).
- Raw output: `trace/results/cost_*_2026-11-22.jsonl` (and `.err` for the phase lines).

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
SR="dotnet benchmarks/SnapRoundingExperiment/bin/Release/net10.0/SnapRoundingExperiment.dll"
for s in gear:500 cyl:8000 fins:400 plate:24 sphere:60; do $SR gen $s cases/proc/$(echo $s | tr : _); done
$SR cull cases/proc/gear_500 union --variants box,enum,box+axesf,box+dop,split,split-enum,split+axesf --visits

git worktree add --detach /tmp/wt 57623890
python3 benchmarks/SnapRoundingExperiment/trace/instrument_phases.py /tmp/wt/mesh_ops/MeshRoundingRepair.cs /tmp/wt/mesh_ops/ExactMeshArrangement.cs
dotnet build /tmp/wt/benchmarks/BooleanComparison -c Release -o /tmp/tool
G3_ROUNDING_PHASES=1 dotnet /tmp/tool/BooleanComparison.dll profile --repeat 1 --safe-rounding cases/proc/gear_500 > p.jsonl 2> p.err
python3 benchmarks/SnapRoundingExperiment/trace/summarize_phases.py phases p.jsonl p.err
DOTNET_TieredPGO=0 benchmarks/SnapRoundingExperiment/trace/run_ab_phases.sh /tmp/tool ab 2 cases/proc/* cases/rot/*
python3 benchmarks/SnapRoundingExperiment/trace/summarize_phases.py ab ab/base ab/axes 2
```
## Thin and tiny components: what the removal takes, and candidate rules (2026-11-22, #1216)

The measurement of [docs/research/ROUNDING_THIN_SOLIDS_2026-11.md](../../docs/research/ROUNDING_THIN_SOLIDS_2026-11.md)
(the tables and their reading are there). `thin <case>...` (`ThinSolids.cs`) prints one JSON line per case: the boolean
as it is today, the exact result plainly rounded, and the boolean's own repair run on it again with
`RemoveTinyComponents` off (the same options; the fixed vertices and point ids are read from the boolean by
reflection). Per closed component that is thin, tiny or of zero volume it lists area and exact volume before and after
the edits, whether an edit touched it and the defects left in it; every candidate rule is then applied to those
components and its outcome checked with `NotCleanReasons`. Today's rule, written out the same way, must reproduce the
boolean's own result (`replica`; it does on all 3474 non-empty results).

- `thin.py run <dir> [<set>...]` runs the sets `issue` (the rows of the issue), `sheet` (its repro scaled: thickness,
  size, offset, six frames), `plate`, `drop`, `copies` (A minus a nearly coincident copy, 1 to 2^36 ulps apart) and the
  generated sets below, one compressed file per set; a rerun gives the same files bit for bit.
- The set `fuzz` (not in the tables): the two cases the Boolean fuzz harness of PR #1382 reported, as bits.
- `thin.py tables <dir>` prints the tables of the note; `thin.py selftest` checks its classification.
- `thin dump <case>` prints the operands and the plainly rounded exact result; `thin cost <case>` times the one pass a
  candidate adds against the boolean and its rounding.
- Raw output: `trace/results/thin_2026-11-22/` (`<set>.jsonl.gz`, `tables.txt`, `cost.jsonl`).

The results of 2026-11-22 were measured before the rule was built: there the library had the rule `cur`, and the other
rules existed only as predicates in `ThinSolids.cs`. Since #1216 the library has rule (d1) at 4 ulps (`d_4`); `replica`
now compares that one with the boolean's own result, and a new `thin.py run` gives the library's new results under
`safe` (the tables of the note need the stored files).

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

## Comparing two builds

`compare_builds.py <dir A> <dir B> [<out.json>]` runs `run <case> safe` of 214 cases (every third of booleans, utm and clusters, all of
cylinder and drop, scale up to 81) on two copies of `bin/Release/net10.0`, one process per run, two passes with the order alternating, the
faster run per case counting, and prints the total times and the cases whose result (clean, triangles, edits, drift) differs. About 35
minutes. The cost of the cap limit of `MeshRoundingRepair` (docs/research/ROUNDING_THIN_VOLUME_2026-11.md, "Caps first") was measured
with the library built once with `CapRatio = 1e300` (no cap is taken) and once with 32:

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release && cp -r bin/Release/net10.0 /tmp/buildA   # after the edit of the constant
python3 compare_builds.py /tmp/buildA /tmp/buildB
```

