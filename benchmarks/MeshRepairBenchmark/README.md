# MeshRepairBenchmark

Measures how well `gs.MeshAutoRepair` repairs real-world broken meshes, and what it breaks on the way.
The data comes from [Thingi10K](https://huggingface.co/datasets/Thingi10K/Thingi10K).

## Usage
```
python3 fetch_data.py            # ~1.5 GB: 400 meshes <= 100k faces + 30 meshes 100k..1M faces (--no-large skips those)
./run_all.sh default             # MeshAutoRepair
./run_all.sh cleanup             # MeshAutoRepair with PostRepairCleanup = true
./run_all.sh instr               # instrumented pipeline copy, quality after every step
python3 analyze.py results/default.jsonl            # status per category, exceptions, triangle loss
python3 analyze_quality.py results/cleanup.jsonl    # quality before/after
python3 analyze_stages.py results/instr.jsonl       # which pipeline step introduces which defect
```

`run_all.sh <mode> [loader] [transform] [tolerance factor]` takes these options:
- **loader:** `std` (StandardMeshReader, STL welding), `soup` (no welding) or `manual` (own exact weld + `AppendTriangle`)
- **transform:** `meters` (scale ×0.001) or `geo` (translate by 500000, 5000000, 0)
- **tolerance factor:** multiplies `RepairTolerance` and `MinEdgeLengthTol`

Each mesh runs in its own process with a timeout (120 s, large meshes 900 s), so hangs and crashes are recorded too.

The data selection is fixed (seeded) in `fetch_data.py`:
- 6 defect categories × 60 broken meshes: not closed, not edge-manifold, not vertex-manifold,
  degenerate faces, duplicated faces, not PWN
- 40 clean controls
- 30 large broken meshes

Thingi10K models have individual licenses, see `data/input_summary.csv`.

## Quality metrics
Each metric is counted before and after the repair:

| Metric | Definition |
|---|---|
| boundary edges | open edges |
| bowtie vertices | non-manifold vertices |
| inconsistent orientation | interior edges whose two triangles traverse it in the same direction |
| duplicate triangles | same vertex set |
| degenerate triangles | area < 1e-12 · diag² |
| sliver triangles | aspect ratio > 1000 |
| folds | dihedral angle > 175° |
| self-intersections | triangle pairs without a shared vertex; coplanar overlaps are not detected |
| inside-out components | closed, negative volume, not a cavity of another component |

## Findings with geometry3Sharp 1.0.324 (NuGet)
- No exceptions, hangs or crashes in any of the following setups:
  - the three loaders
  - meshes up to 1M triangles
  - geo coordinates
- 398 of 400 meshes are closed after the repair, but only 144 are topologically clean.
- **`MinEdgeLengthTol` and `RepairTolerance` are absolute.** With data in meters instead of mm:
  - 118/400 meshes lose more than 10 % of their triangles (max 88 %).
  - 32 of the 40 clean meshes are changed.

  Scale both tolerances with the model size or unit.
- Defects introduced by the pipeline (meshes affected, out of 400):
  - duplicate triangles: 40
  - new degenerate triangles: 59
  - new folds: 66
  - inside-out components: 12
  - remaining bowties: 98
- Per step (`analyze_stages.py`):
  - Hole filling (`MinimalHoleFill`, `SimpleHoleFiller`) adds most degenerate triangles and folds.
  - `MeshEditor.DisconnectBowtie` opened 3108 new boundary edges in 9 meshes.
  - `MergeCoincidentEdges` adds duplicates and folds.
  - `MeshRepairOrientation.OrientComponents` can make orientation worse on non-orientable regions.

## Changes in this fork
1. **`MeshEditor.DisconnectBowtie` fix.** The old version grouped the one-ring triangles in a single,
   order-dependent pass and could split one fan into several pieces, which cut cracks into the mesh.
   It now unions the triangles over the edges at the vertex.
   Test: `tests/geometry3Sharp.Tests/MeshEditorBowtieTests.cs`. It fails with 1.0.324 (`dotnet test -p:UseNuGetG3=true`).
2. **`MeshPostRepairCleanup`** (new, `mesh_ops/`). It runs these steps:
   - remove duplicate triangle pairs
   - collapse needles and flip caps
   - flip folds, accepted only if the local fold count drops
   - disconnect bowties
   - reverse inside-out components that are not cavities
3. **`MeshAutoRepair.PostRepairCleanup`** (default `false`) runs the cleanup at the end of `Apply()`.

See the results table below.
