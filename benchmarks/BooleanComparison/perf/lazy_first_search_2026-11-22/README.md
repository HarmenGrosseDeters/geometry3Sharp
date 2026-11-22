# Lazy first pair search and per-block skipped count: measured, not merged (2026-11-22)

Prototype: branch `perf/rounding-lazy-pair-search`, commit e28849b (PR #1322, closed unmerged; bit-identical results, tests green).
`seam-levers --half 48 --ops union --parts boolean --repeat 3` (tubes, k = 96, 628 078 result triangles, 3 edits), best of 3 per row, base and
prototype alternating, 4-vCPU container, run-to-run noise about 5 %. Hash `C5689232CE10FDF1` on every run.

| variant | threads | total ms | rounding ms |
|---|---:|---:|---:|
| base | 1 | 2443 | 786 |
| lazy first search + per-block count | 1 | 2396 | 761 |
| base | 4 | 1168 | 483 |
| lazy first search + per-block count | 4 | 1194 | 516 |
| base (6 rounds) | 4 | 1078 | 472 |
| per-block count only (first search with the tree) | 4 | 1053 | 413 |
| base (4 rounds) | 1 | 2410 | 648 |
| per-block count only | 1 | 2460 | 715 |

First pair search alone (Stopwatch around `intersecting_pairs()`, three runs): with the tree of all triangles built inside it 410-527 ms at
1 thread, 122-138 ms at 4; without it 102-108 ms at 1 thread, 72-158 ms at 4. The tree is then built at the first `near` query
(`FIRSTNEAR` probe: before any edit is committed; about 30 `near` calls in the half-12 union).
