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
