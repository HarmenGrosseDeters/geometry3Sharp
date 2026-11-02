# Snap-rounding experiment

The measurement of [docs/design/SAFE_ROUNDING.md](../../docs/design/SAFE_ROUNDING.md) section 8: SafeRounding
(`MeshRoundingRepair`, the default of `ExactMeshBoolean`) against two prototypes for what it leaves.

- **A, separation** (`MeshRoundingRepair.Separate`, internal, `mesh_ops/MeshRoundingRepair.Separation.cs`): the
  expansion stage of Milenkovic and Sacks extended to degenerate triangles; sequential linear programs over the
  vertices around a defect, one multi-vertex move checked exactly.
- **B, snap fallback** (`SnapFallback.cs`, only here): a second resolve of the result, then the vertices of the
  defect triangles snapped to grids of 2^j ulps and resolved again (after Valque and Lazard), within the drift left.

Methods: `plain` (SafeRounding off), `safe` (the default), `again` (safe, then one more resolve), `A`, `B`, `AB`.

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
- a file list (`@list`): the self-unions of meshes, eg the Thingi10K 400 set of
  `benchmarks/MeshRepairBenchmark/fetch_data.py` (not committed: the meshes have their own licenses).

## Running

```sh
dotnet build benchmarks/SnapRoundingExperiment -c Release
cd benchmarks/SnapRoundingExperiment
python3 run.py out/clusters.jsonl clusters --methods plain,safe -j 4          # all cases
python3 run.py out/clusters.jsonl clusters --methods again,A,B,AB --left -j 4  # where safe is not clean
python3 summarize.py out/clusters.jsonl
ls ../MeshRepairBenchmark/data/meshes/* > thingi.txt
python3 run.py out/thingi.jsonl @thingi.txt --methods plain,safe -j 4 && python3 run.py out/thingi.jsonl @thingi.txt --methods again,A,B,AB --left -j 4
```

Each case runs in its own process with a time limit (`--timeout`, per method), so a pathological case cannot stop the
run; the output is appended one line per result, and a run resumes where it stopped. A single case:
`dotnet bin/Release/net10.0/SnapRoundingExperiment.dll run cluster:Cluster:0:4:5 safe A B` (`SEP_DEBUG=1` prints the
linear programs of A and the pairs the certificate rejects).

The results are in SAFE_ROUNDING.md section 8.
