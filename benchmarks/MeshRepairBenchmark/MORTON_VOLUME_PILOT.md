# Morton ordering in the repair volume check

The benchmark can evaluate samples in Morton order within each round with `BENCH_IOU_PACKED_MORTON=1`. It uses the existing winding predicates and writes each vote at its original sample index. Each sort word contains the 30-bit Morton key and the original local index. A buffer is reused as long as its capacity covers the current round. The switch is internal and defaults to input order. [Existing batch benchmark](../BatchQueryBenchmark/README.md)

## Results

The original probe used master revision `21fc38b53`, .NET 10, and two logical CPUs on an Intel Xeon Platinum 8573C. Timings below are medians. Time savings are medians of the paired percentage changes. The historical full-repair timer covers the driver stage around `MeshRobustRepair.Apply()`, including option setup and reporting. The driver checks the output mesh afterwards.

| Full repair | Input order ms | Packed Morton ms | Paired saving | Faster pairs |
|---|---:|---:|---:|---:|
| Thingi10K 356074 | 4,064.0 | 3,777.5 | 8.07% | 6/6 |
| Procedural B01, 12 triangles | 120.0 | 122.5 | −2.97% | 1/6 |
| Thingi10K 996816, solidification | 11,018.5 | 10,404.0 | 5.58% | 2/2 |

Each pair produced identical repair fingerprints, volume-check results and repair paths. Only two pairs cover 996816. The measured reduction in its volume sampling was smaller than the difference in full repair time. [Raw repair runs](results/morton/morton-packed-repair-2026-11-28.jsonl)

In the volume-check-only probe, packed sorting improved time by 18.54%, 8.09% and 15.23% on the 100,000-point Thingi10K, ABC and Objaverse streams respectively. On those three 100,000-point streams, it saved about 0.43 MB per check relative to sorting separate key and index arrays and added about 0.77 MB relative to input order, measured with process-wide allocated bytes during the check. A separate replay checked 625,000 points across five workloads and two seeds. Both votes, the sample coordinates, round boundaries, final IoU bits and acceptance matched. [Raw volume-check runs](results/morton/morton-packed-2026-11-28.jsonl) · [Raw equality checks](results/morton/morton-packed-verify-2026-11-28.jsonl)

B01 shows why enabling the opt-in globally is premature. A follow-up should measure sorting cost versus baseline winding cost on small meshes and cheap large-tree queries. Compare a mesh-size guard with a first-round work estimate, then check the guard on a separate corpus split.

## Reproduce the full repair comparison

Use the Thingi10K data pinned at revision `1e52df6` and the procedural B01 generator from the pinned targeted benchmark. ABC and Objaverse rows in the raw probe use the prepared pairs from that benchmark, with CadQuery 2.7.0 and g3cli revision `864cd5f98a70781b4c80bb0c214daa2f6b72bbb7`. The volume-check raw rows record the input hashes, seed, translation fraction, and working mesh fingerprints. [Thingi10K meshes](https://huggingface.co/datasets/Thingi10K/tree/1e52df6/raw_meshes) · [Targeted benchmark input preparation](https://github.com/HarmenGrosseDeters/geometry3Sharp/blob/0577940f6916f759e7f0dda7a7e690cb89842d4f/benchmarks/TargetedMeshRegression/README.md)

```bash
set -e
dotnet build benchmarks/MeshRepairBenchmark -c Release
app=benchmarks/MeshRepairBenchmark/bin/Release/net10.0/MeshRepairBenchmark.dll
export DOTNET_PROCESSOR_COUNT=2 DOTNET_TieredPGO=0 DOTNET_GCHeapHardLimit=0x150000000
export BENCH_REPAIR=robust BENCH_VOLUME_PROBE_ONLY=1
for input in "$thingi/356074.stl" "$fixtures/B01-disjoint-a.obj" "$thingi/996816.stl"; do
  pairs=6
  if [[ "$input" == */996816.stl ]]; then pairs=2; fi
  for ((pair=0; pair<pairs; ++pair)); do
    for trial in "$((pair%2))" "$((1-pair%2))"; do
      BENCH_IOU_PACKED_MORTON=$trial taskset -c 0,1 dotnet "$app" "$input"
    done
  done
done > morton-repair.jsonl
python benchmarks/MeshRepairBenchmark/check-morton-repair.py morton-repair.jsonl
```

The current driver records `robust_apply_ms` around `Apply()` alone. The verifier checks row counts, sorted-path coverage, mesh validity and fingerprints, and the paired repair and volume decisions.

The draft-branch smoke run covers one pair each on 356074 and B01. It passed the verifier and exercises the exact Apply timer and sorted-path counter. [Raw smoke runs](results/morton/morton-pr-smoke-2026-11-28.jsonl)
