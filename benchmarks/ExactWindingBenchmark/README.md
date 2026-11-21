# ExactWindingBenchmark

Before/after comparison of `DMeshAABBTree3.WindingNumber` and `MeshWindingNumberGrid` comparing the legacy Jacobson
hierarchical `WindingCache` (`ForceLegacyWindingCache = true`), the exact ray-plus-boundary `ExactWindingCache`
(Antipodal / FEWN decomposition with exact `Orient2d`/`Orient3d` symbolic perturbation and `+x` row-ray reuse), and
`FastWindingNumber` on procedural meshes (no committed mesh files).

Command:

```bash
dotnet run --project benchmarks/ExactWindingBenchmark/ExactWindingBenchmark.csproj -c Release -- 5
```

All timings are medians of 5 rounds after multi-iteration Tier-1 JIT warm-up (Linux x64 container, .NET 10.0.15,
2 logical processors, 2026-11-21). Build time is the first query on a freshly built `DMeshAABBTree3`; query time is
the subsequent batch of random queries in `[-1.5, 1.5]^3`. Grid times include cache construction.

## Measured Results (2026-11-21)

### Point queries (`DMeshAABBTree3.WindingNumber`)

| case | tris | \|∂M\| | queries | legacy build ms | exact build ms | legacy query ms | exact query ms | FWN build/query ms | max \|exact-legacy\| | max \|FWN-legacy\| |
|---|---|---|---|---|---|---|---|---|---|---|
| closed sphere 20k | 20172 | 0 | 1000 | 32.5 | 12.5 (2.6x) | 40.9 | 0.41 (100.4x) | 22.0 / 2.10 | 8.88E-016 | 6.48E-002 |
| closed sphere 80k | 80688 | 0 | 1000 | 52.7 | 15.9 (3.3x) | 23.5 | 0.48 (48.9x) | 47.4 / 2.62 | 8.88E-016 | 6.58E-002 |
| open sphere 20k | 14126 | 204 | 1000 | 7.1 | 3.5 (2.0x) | 20.1 | 5.34 (3.8x) | 7.7 / 1.41 | 5.55E-016 | 5.35E-002 |
| flipped cap 20k | 20172 | 220 | 1000 | 11.3 | 5.1 (2.2x) | 36.1 | 5.80 (6.2x) | 10.3 / 2.07 | 1.32E-015 | 4.64E-002 |
| unwelded closed 20k | 20172 | 0 | 1000 | 10.7 | 4.0 (2.6x) | 1464.5 | 0.40 (3644.8x) | 11.2 / 1.76 | 2.78E-015 | 6.48E-002 |
| disconnected soup 5k | 5000 | 15000 (direct) | 200 | 2.1 | 5.2 (0.4x) | 72.7 | 25.28 (2.9x) | 2.7 / 0.73 | 8.88E-016 | 3.78E-003 |

### Grid evaluation (`MeshWindingNumberGrid`, including cache build)

| case | mode | grid | legacy ms | exact row-ray ms | FWN ms | max \|exact-legacy\| |
|---|---|---|---|---|---|---|
| closed sphere 20k | FullGrid | 64^3 | 5021.6 | 9.2 (545.0x) | 420.9 | 2.21E-009 |
| open sphere 20k | FullGrid | 64^3 | 2989.4 | 500.3 (6.0x) | 261.3 | 7.45E-009 |
| closed sphere 20k | NarrowBand | 64^3 | 1680.7 | 209.6 (8.0x) | 370.9 | 2.21E-009 |
| open sphere 20k | NarrowBand | 64^3 | 1138.2 | 296.1 (3.8x) | 289.1 | 7.45E-009 |

### Downstream adoption (`FastWindingNumber` vs opt-in exact winding)

| operation | case | FWN ms | exact ms | speedup vs FWN |
|---|---|---|---|---|
| `MeshWindingNumberSolidify` | closed sphere 20k (dense 64) | 223.9 | 54.8 | 4.1x |
| `MeshWindingNumberSolidify` | closed sphere 20k (adaptive 64) | 162.1 | 165.4 | 1.0x |
| `MeshWindingNumberSolidify` | unwelded closed 20k (dense 64) | 221.3 | 66.2 | 3.3x |
| `MeshBatchQueries.IsInsideBatch` | closed sphere 20k (20k pts) | 35.1 | 14.9 | 2.3x |
| `MeshBatchQueries.IsInsideBatch` | unwelded closed 20k (20k pts) | 32.6 | 11.1 | 2.9x |
| `MeshDeviation` | closed sphere 20k (2.4k query vtx) | 28.9 | 16.6 | 1.7x |

### Hierarchical boundary threshold queries (`BoundaryTree`, `MeshBatchQueries.IsInsideBatch`)

| case | tris | \|∂M\| | queries | linear exact ms | hierarchical exact ms | FWN ms | speedup vs linear | parity |
|---|---|---|---|---|---|---|---|---|
| open cup rim 2k | 22000 | 2000 | 2000 | 55.7 | 12.1 | 24.9 | 4.6x | 2000/2000 |
| open cup rim 4k | 28000 | 4000 | 2000 | 109.3 | 13.8 | 67.6 | 7.9x | 2000/2000 |

## Notes

- **Unwelded closed STL soups** (`unwelded closed 20k`): exterior edges are cancelled by canonicalized exact 3D vertex
  coordinates rather than vertex IDs, reducing `|∂M|` to `0` and delivering a **3,644.8x** query speedup over the
  legacy vertex-ID `WindingCache` (and **4.4x** faster than approximate `FastWindingNumber`).
- **Closed grids** (`FullGrid` 64^3): one BVH line traversal per `(y, z)` row plus a difference-array prefix sum
  evaluates all 262,144 voxels in **9.2 ms** (**545.0x** faster than legacy exact and **45.8x** faster than `FWN`).
- **Downstream opt-in exact winding:** On closed/unwelded-closed inputs, `MeshWindingNumberSolidify` (`UseExactWinding = true`,
  dense 64) uses exact `FullGrid` `+x` row-ray evaluation and runs **4.1x** faster end-to-end than `FastWindingNumber`
  (`54.8 ms` vs `223.9 ms`) with zero dipole error. `MeshBatchQueries.IsInsideBatch(..., InsideMethod.ExactWindingNumber)`
  runs **2.3x–2.9x** faster than FWN, and `MeshDeviation` (`Signs.ExactWindingNumber`) runs **1.7x** faster.
- **Hierarchical boundary tree (`BoundaryTree`):** On open meshes with large boundaries (`|∂M| >= 500`), certified
  dipole clustering over exterior boundary chains accelerates `EvaluateAbove` / `EvaluateAbsAbove` by **4.6x–7.9x**
  over linear boundary summation (**2.1x–4.9x** faster than `FastWindingNumber`) with **100%** exact threshold parity.
- **Disconnected soups** (`|∂M| >= F` and `F > 64`): automatically switches to the flat packed direct solid-angle sum,
  still **2.9x** faster per query than the legacy hierarchical cache.
