# RayQueryBenchmark

Time per ray query of `DMeshAABBTree3.FindNearestHitTriangle` and `IsInside` on a 200k-triangle sphere and a
100k-triangle terrain (oblique rays), and of the single-triangle `IntrRay3Triangle3.Intersects` on 1M random
triangles with random rays. The inputs are procedural with fixed seeds, so nothing is downloaded. The output is CSV
with the median of 5 rounds after a warm-up. The checksum column holds the hit ids, the inside counts, and the hit
count with the sum of t. It must agree between two builds whose decisions agree.

    dotnet run --project benchmarks/RayQueryBenchmark -c Release [-- <rays> <rounds>]

For an A/B, copy this folder into a worktree at the base commit and run it there too.

## Exact, watertight hits (findings Q2 and Q3, November 2026)

`IntrRay3Triangle3` decides hits exactly: it takes the `Orient3d` signs of the ray's line against the closed
triangle. `IsInside` counts the crossings of a symbolically perturbed ray. Before, upstream's Möller-Trumbore test
with a parallel tolerance decided hits. The three edge predicates share their differences and run `Orient3d`'s own
filter (`ExactPredicates.Orient3dFilter`); the exact stage runs only where that filter does not decide.

Base: master 48c50485. The fix is the Q2/Q3 branch on it. ns per query; two runs each, alternating, on an otherwise
idle 4-core container. The checksums are identical in every row.

| query | base | fix |
|---|---|---|
| sphere, `FindNearestHitTriangle` | 234 / 266 | 271 / 272 |
| sphere, `IsInside` | 4860 / 5234 | 5941 / 5653 |
| terrain, `FindNearestHitTriangle` | 3485 / 3654 | 4387 / 4702 |
| terrain, `IsInside` | 2325 / 2528 | 3047 / 3110 |
| single triangle, `Intersects` | 34.9 / 34.6 | 147.0 / 145.0 |

The tree queries cost +2 % to +29 %. The static single-triangle `Intersects` costs about 4.2x: upstream rejects
most rays after one cross and two dot products. A certified decision needs at least two edge signs with their error
bounds, plus the ray's second point once per call, since the static API cannot reuse it per ray. Before the shared
filter the single-triangle factor was about 10x and the repair ray time +43 %. Per-ray reuse and a coarser
per-triangle bound are in ROADMAP "Later" (the batch-query entry).

In a repair the effect is small:
- On 40 meshes of `MeshRepairBenchmark` (every 10th of the 400 set, robust mode), all ray time is
  `FindNearestHitTriangle` from `MeshRepairOrientation`.
- That ray time is 79 s base and 90 s fix, or 6.3 % and 7.2 % of the process CPU time, so the fix adds about 0.9 %
  of the repair.
- 2.4 % of the edge predicates reach the exact stage. These are mostly zero-area and duplicate triangles of the
  broken inputs, where the edge values are exactly 0. The exact stage takes under 1 s of the 90 s.
- A pre-pass over all 400 meshes compared every query with 1.0.324's test in the same process. 123 meshes have at
  least one differing nearest hit (1290 of 24.5M queries), and t never differs where both hit.
- Robust repair on those 123 meshes plus 20 random controls, base against fix: 142 of 143 results are bit-identical
  (hash of the result mesh). 72002 differs in its hash only, with every metric the same. Repair time changed by
  -0.3 % on the 123 meshes and +1.4 % on the controls.
