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

| case | mode | grid | legacy ms | exact row-ray ms | FWN ms | max \|exact-legacy\| | max \|exact-legacy\|, off-surface nodes |
|---|---|---|---|---|---|---|---|
| closed sphere 20k | FullGrid | 64^3 | 5021.6 | 9.2 (545.0x) | 420.9 | 5.00E-001 | 2.01E-015 |
| open sphere 20k | FullGrid | 64^3 | 2989.4 | 500.3 (6.0x) | 261.3 | 5.00E-001 | 0 |
| closed sphere 20k | NarrowBand | 64^3 | 1680.7 | 209.6 (8.0x) | 370.9 | 5.00E-001 | 2.01E-015 |
| open sphere 20k | NarrowBand | 64^3 | 1138.2 | 296.1 (3.8x) | 289.1 | 5.00E-001 | 7.65E-002 |

(Timing columns are from the 2026-11-21 run; the two difference columns were measured on 2026-11-24 after the on-surface change, one round, on a loaded 12-processor Windows machine.) One node of the 64^3 grid lies exactly on a triangle of the sphere (node (34, 34, 2), p = (7.170353200081081e-11, 7.170353200081081e-11, -0.9997025727508673); two more on the closed sphere): the exact cache gives the principal value there, 0.5 on the closed sphere (0.4181034 on the open one), the legacy cache the rounded own-triangle term (1.0 and 0.9181034), so the difference is 0.5 and the first difference column is 0.5 in all four rows. The second column leaves out the nodes within 1e-8 of the mesh (a squared point-triangle distance has about 1e-17 of cancellation noise, so a node exactly on the mesh measures up to 3e-10) and shows that every other node agrees to 2e-15. The one exception, 7.65e-2 on the open sphere in NarrowBand mode, is a node at distance 6e-2 from the mesh; presumably (not traced) its value is filled in along its row from a computed neighbour, as the narrow band does between computed nodes, and the changed on-surface value lies in such a span (FullGrid computes every node and agrees: 0).

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

### BoundaryTree node query, before and after the per-edge error term (2026-11-22)

Query-only time of `MeshBatchQueries.IsInsideBatch(..., ExactWindingNumber)` for 20 batches of 2000 points after the caches are
built (build outside the timer), medians of 4 alternating runs (old / new / new / old ...) on the same 4-core container with
nothing else running; `f3b0fa5e` is the commit before the node bound got the term `1e-12 * (hi - lo)` and the shared
`node_query`. Queries that fall back on the full boundary sum: 4 per batch of 2000, both before and after (the term
changes no decision here), so its cost is below the noise.

| case | f3b0fa5e runs (ms) | now runs (ms) | medians |
|---|---|---|---|
| open cup rim 2k | 47.7 58.7 47.6 50.8 | 45.9 46.3 46.3 48.4 | 49.3 -> 46.3 ms (-6 %) |
| open cup rim 4k | 96.3 97.4 107.5 120.9 | 86.0 88.6 89.5 87.5 | 102.5 -> 88.1 ms (-14 %) |

A first version of `node_query` that returned a struct of all clause terms from the hot path cost +7 % to +37 % in the
rim-and-build timing of the table above; the clause terms are now written only for the test hook (`DescribeBoundaryNode`).

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

## Edge angle near open edges (2026-11-24)

`dotnet run --project benchmarks/ExactWindingBenchmark/ExactWindingBenchmark.csproj -c Release -- 15 near-edge` runs only this
section. Before / after of the accurate edge angle (`boundary_edge_angle_x`, design note "The accuracy of the edge angle"),
exact `DMeshAABBTree3.WindingNumber`, 2000 queries per case, median of 15 rounds, Windows 11 x64, .NET 10 (not the container of the
tables above, so the absolute times differ from them; compare the before and after columns of one case). "Near" = a random point of a random
boundary edge plus 10^-3 .. 10^-14 in a random direction.

| case | tris | us / query before (master) | us / query after | sum of the values before | after |
|---|---|---|---|---|---|
| closed sphere 20k, random | 20172 | 0.41 | 0.40 | 308 | 308 (same bits) |
| open sphere 20k, random (away from the edges) | 14126 | 7.80 | 7.92 | 216.93695380309467 | 216.93695380309467 (same bits) |
| open sphere 20k, near the boundary edges | 14126 | 8.64 | 18.86 | 612.891220238622 | 612.8910033301383 (the old sum is off by 2.2e-4) |
| open box, query grid in the plane of its rim (every query exactly on the yz line of rim edges) | 10 | 0.31 | 0.36 | 512 | 512 (same bits) |

(Median of 15 rounds, one machine, re-run of all columns in one session; the run to run noise is about 10 %: a first run of the box
case gave 0.52 and the sphere rows move by 0.3 us.) The closed mesh has no boundary edge (unchanged; the difference is noise).
Away from the edges the rounded path takes one more comparison per edge (inside the noise) and returns the same bits. Near the
edges, 10 us more per query: the edges of such a point that are within a factor 45 of cancellation (p near the open segment), about
one or two of them, take the accurate path (about 10 us each). A point exactly on the yz line of an edge (the box grid) does not enter
the integer evaluation: an earlier revision of this change sent it through it (the same box case 0.64 us, the sphere near-edge case
21.8 us before the accurate evaluation was shortened by `lenBits` from the bit length and the removal of the precision loop).
