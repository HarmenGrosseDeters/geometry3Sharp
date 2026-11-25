# ExactHotspotsStudy: the two hot spots of the exact Boolean on the slow tail

The probes of [docs/research/EXACT_HOTSPOTS_STUDY_2026-11.md](../../docs/research/EXACT_HOTSPOTS_STUDY_2026-11.md): the
sign-bearing exact stage of `orient3d` and the projected triangle-triangle filter (`TriangleTriangleFilter.by_projection`), on
procedural degenerate inputs. Nothing here is library code, and nothing here is a gate. The note has the method, the tables
and the options; this file has the commands and the layout.

```
dotnet build benchmarks/ExactHotspotsStudy -c Release
cd benchmarks/ExactHotspotsStudy
dotnet bin/Release/net10.0/ExactHotspotsStudy.dll cases > results/cases.txt
```

The modes `filter`, `pairs` and `cache` need temporary counters, and the spike's cache, inside the library. To run them:
1. Apply `results/probe_counters.patch` (to `math/ExactPredicates.cs` and `mesh_ops/MeshRoundingRepair.cs`).
2. Build with `-p:RunAnalyzers=false`: the determinism analyzer refuses the counters in the exact files.
3. Run the modes:
   ```
   dotnet bin/Release/net10.0/ExactHotspotsStudy.dll filter > results/filter.txt
   dotnet bin/Release/net10.0/ExactHotspotsStudy.dll pairs > results/pairs.txt
   dotnet bin/Release/net10.0/ExactHotspotsStudy.dll cache > results/cache.txt
   ```
4. Revert the patch.

The patch was never committed to the library. Without it the three modes stop with a message.

Measured 2026-11-25 in a Linux container, 4 cores (Xeon 2.1 GHz), .NET 10.0.115, on master 25b5cc8. Each mode takes under
2 minutes. The hot-spot shares come from the `dotnet-trace` profile of the same workload in
`benchmarks/HigherMathStudy` (PR #1563, not yet merged), folded by `callpath.py` (`results/callpath_degenerate.txt`).

## Layout

| file | what |
|---|---|
| `Workload.cs` | The slow-tail workload: coplanar boxes, and the 9 `DegenerateFamilies` at 2^-20 and 2^-40, each against a shifted copy and against itself, as exact unions. Mode `cases` gives the time per case |
| `FilterProbe.cs` | Hot spot 1. Records every `orient3d` call that reaches the exact stage (up to 200 000 per case). For each one it reports the path taken (`orient3d_int` or the expansions) and the ratio r = \|det\| / (u · permanent) of the exact determinant against the filter's scale. It also times the exact stage and the filter per call on the recorded inputs |
| `PairProbe.cs` | Hot spot 2. Counts the triangle-triangle tests of `MeshRoundingRepair` and how many distinct pairs they are (by the 18 coordinates) |
| `CacheProbe.cs` | The spike of the next candidate: a cache of those tests keyed by the 18 coordinates. A/B time (minimum of 3 alternating runs) and a bit-identity check of every result |
| `callpath.py` | Folds a speedscope thread-time profile by call path: the callers and the deepest frames of the hot spots, and the inclusive shares of the repair and the arrangement |
| `results/` | the raw outputs quoted in the note, and the temporary patch |
