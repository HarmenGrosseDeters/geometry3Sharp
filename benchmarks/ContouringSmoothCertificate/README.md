# ContouringSmoothCertificate

Cost and yield of the internal topology certificate for the `DualContouring` mesh of a smooth implicit field
(`ContouringSmoothCertificate`, [docs/design/CONTOURING_TOPOLOGY.md](../../docs/design/CONTOURING_TOPOLOGY.md) section
7.13, Theorem 7). The certificate has no public API; this project reaches it through `InternalsVisibleTo`.

## What it runs

The six nodal functions of `TpmsLattice` (`IntervalTpms`), level 0, closed by a smooth cap
`g(K x) + c (|x|^2 / 0.81)^3` so that the level set is a closed surface in a box of side 2.2, with one and two periods
across the box, on grids of 32 and 64 cells (so 16, 32 and 64 cells per period). For each: `DualContouring.Generate`
(`Simplify` off, `RecordCertificateData` on, the field in plain doubles), then the certificate serial and parallel.
Times are the median of three runs.

```
dotnet run -c Release --project benchmarks/ContouringSmoothCertificate > benchmarks/ContouringSmoothCertificate/results/run.csv
```

Columns: `surface_cells` (cells not proven zero-free), `uncertified_cells`, `certified`, `passed_t0` .. `passed_t2`
(cells that passed the gradient test with the cell cut into 1, 8 and 64 sub-boxes), `topology` (components and total
genus of the contour), and the three times in ms.

## Results

`results/run.csv`, one run on 4 cores (2026-11-22), times the median of three:

| Field | Periods | Cells per period | Surface cells | Uncertified cells | Certified | Passed at t = 0 / 1 / 2 | Contour ms | Certificate ms, serial | Certificate ms, parallel |
|---|---|---|---|---|---|---|---|---|---|
| Gyroid | 1 | 32 | 1748 | 0 | yes | 1747 / 1 / 0 | 74 | 81 | 29 |
| Gyroid | 1 | 64 | 7015 | 0 | yes | 7015 / 0 / 0 | 82 | 136 | 49 |
| Gyroid | 2 | 16 | 2628 | 71 | no | 2467 / 76 / 14 | 36 | 98 | 45 |
| Gyroid | 2 | 32 | 11071 | 0 | yes | 11071 / 0 / 0 | 89 | 211 | 72 |
| SchwarzP | 1 | 32 | 0 | 0 | yes | 0 / 0 / 0 | 2 | 3 | 2 |
| SchwarzP | 1 | 64 | 0 | 0 | yes | 0 / 0 / 0 | 7 | 6 | 3 |
| SchwarzP | 2 | 16 | 3771 | 219 | no | 3447 / 76 / 29 | 13 | 173 | 66 |
| SchwarzP | 2 | 32 | 17539 | 0 | yes | 17528 / 11 / 0 | 60 | 305 | 89 |
| SchwarzD | 1 | 32 | 1966 | 29 | no | 1818 / 77 / 42 | 8 | 77 | 31 |
| SchwarzD | 1 | 64 | 8377 | 0 | yes | 8377 / 0 / 0 | 39 | 170 | 51 |
| SchwarzD | 2 | 16 | 2820 | 300 | no | 1473 / 879 / 168 | 13 | 332 | 178 |
| SchwarzD | 2 | 32 | 13220 | 26 | no | 12931 / 222 / 41 | 58 | 336 | 96 |
| Neovius | 1 | 32 | 0 | 0 | yes | 0 / 0 / 0 | 3 | 4 | 2 |
| Neovius | 1 | 64 | 0 | 0 | yes | 0 / 0 / 0 | 8 | 7 | 4 |
| Neovius | 2 | 16 | 3870 | 833 | no | 2386 / 475 / 176 | 10 | 492 | 144 |
| Neovius | 2 | 32 | 19404 | 1157 | no | 17238 / 698 / 311 | 42 | 905 | 273 |
| IWP | 1 | 32 | 118 | 0 | yes | 118 / 0 / 0 | 1 | 49 | 17 |
| IWP | 1 | 64 | 0 | 0 | yes | 0 / 0 / 0 | 11 | 54 | 18 |
| IWP | 2 | 16 | 4167 | 0 | yes | 4154 / 13 / 0 | 22 | 187 | 47 |
| IWP | 2 | 32 | 17516 | 0 | yes | 17516 / 0 / 0 | 64 | 689 | 187 |
| Lidinoid | 1 | 32 | 1552 | 254 | no | 842 / 345 / 111 | 7 | 376 | 146 |
| Lidinoid | 1 | 64 | 7126 | 288 | no | 5990 / 645 / 203 | 39 | 634 | 218 |
| Lidinoid | 2 | 16 | 2565 | 1430 | no | 491 / 367 / 277 | 21 | 1523 | 526 |
| Lidinoid | 2 | 32 | 13812 | 1816 | no | 10220 / 1339 / 437 | 61 | 2456 | 766 |

Reading:

- Certified in full: the gyroid and Schwarz P at 32 cells per period (the gyroid also at 64), I-WP at 16 and 32,
  Schwarz D at 64. Not certified: the gyroid and Schwarz P at 16 cells per period (2.7 and 5.8 % of the cells fail the
  gradient test), Schwarz D at 16 and at 32 (26 to 300 cells), Neovius and the Lidinoid at every resolution here: their
  nodal functions have more terms and a higher curvature against the period, and the natural interval extension of
  their gradients is loose (the Lidinoid passes an eighth to a third of its cells only after tightening).
- A row with no surface cells is a field with no zero in the box at that level and cap (nothing to certify; the
  contour is empty). I-WP with one period at 32 cells has 118 cells that are not proven zero-free and an empty contour:
  they pass the gradient test, which is consistent (a cell that passes need not hold a zero).
- Cost against a contouring that evaluates the field in plain doubles: for the runs that are certified, the serial
  certificate takes 1.1 to 11 times the contouring and the parallel one 0.4 to 2.9 times (gyroid at 64 cells, two
  periods: 89 ms contouring, 211 ms serial, 72 ms parallel). An interval evaluation of a trigonometric field costs
  about ten times a double evaluation, and a cell that needs tightening costs 9 or 73 enclosures instead of one: the
  Lidinoid runs take 16 to 72 times the contouring in the serial path.

## The certificate record of `TwoLevelMarchingTetrahedra` (mode `record`)

```
dotnet run -c Release --project benchmarks/ContouringSmoothCertificate -- record > benchmarks/ContouringSmoothCertificate/results/record.csv
```
The cost of the internal record that a certificate of the mesher's result reads
([CONTOURING_BLOCK_CERTIFICATE.md](../../docs/design/CONTOURING_BLOCK_CERTIFICATE.md), Lemma 9, remark 6): `Generate`
with and without it, 9 runs each in alternation (median, smallest and largest in the file), serial, one machine, on walls
around two capped TPMS at 64, 128 and 192 cells across, half a cell and two cells thick. `results/record.csv`,
2026-11-22:

| | Range over the 12 cases |
|---|---|
| time of `Generate` with the record against without (medians) | +2.2 % to +12.4 %, median +5.2 % |
| bytes allocated by `Generate` | +6.5 % to +9.5 % |
| payload of the record against the mesh's vertex and triangle arrays | 35 to 39 % at 64 cells, 47 to 54 % at 128, 59 to 69 % at 192 |

- The time difference is small against the spread of the runs of one case (the largest run is 10 to 109 % above the
  smallest), but it has the same sign in all 12 cases: about 5 %.
- The payload is one byte per grid node, nine bytes per vertex and one per triangle. The byte per node is what grows
  against the mesh (nodes grow with the cube of the resolution, the mesh with its square): at 192 cells it is 7.2 of the
  10.8 to 11.7 million bytes. Against the mesher's own working arrays (two doubles per node) it is one sixteenth. The
  record's lists grow by doubling and are not trimmed, so the instance can hold up to twice their part.
- Off by default; the mesh is the same with and without it.

## The per-level certificate of `TwoLevelMarchingTetrahedra` (mode `sheet`)

```
dotnet run -c Release --project benchmarks/ContouringSmoothCertificate -- sheet > benchmarks/ContouringSmoothCertificate/results/sheet.csv
```

The yield and the cost of `TwoLevelSheetCertificate` (internal;
[CONTOURING_BLOCK_CERTIFICATE.md](../../docs/design/CONTOURING_BLOCK_CERTIFICATE.md), section 4b) on walls around six
capped TPMS (a smooth field, no domain cut): 32 and 64 cells across, one and two periods, half a cell and two cells
thick (two cells only at one period). `results/sheet.csv`, 2026-11-22, one machine, serial, medians of 3 runs. 36
runs; in 8 (Schwarz P and Neovius at one period) the wall is empty and the run is certified in 6 to 33 ms; the other 28:

| | |
|---|---|
| runs certified | 13 of 28 |
| gyroid, Schwarz P, Schwarz D, Neovius at 32 or 64 cells per period | 11 of 12 (the one left: Schwarz D, 64 cells, two periods, 41 cubes) |
| the same four at 16 cells per period | 0 of 4 (17 to 841 cubes left) |
| IWP and Lidinoid | 2 of 12 (IWP at two periods); 160 to 1483 cubes left in the others |
| cubes that may hold a zero, both levels, all 28 runs | 278187: 86.6 % certified alone, 10.6 % in blocks, 2.8 % left (the blocks hold 30461 cubes, those without a zero included) |
| cubes that may hold a zero and are not certified alone: in a block | 78.9 % (29365 of 37219) |
| cubes left, by the condition that failed last | 3549 direction (B3), 4305 block limit; none by node, edge, face, circle count or mesh |
| time of the certificate against the mesher with its record | 29 to 306 times, median 158 times |
| time per cube that may hold a zero | 0.44 to 1.28 ms, median 0.53 ms |
| time with blocks against without (`MaxBlockSide = 1`) | 0.98 to 1.26 times, median 1.10 |

- No run is certified wrong by the benchmark's own view: the certified runs have two components (one per level); it
  does not check more than that. The tests have a sampling oracle.
- The cost is far above the 10 % bar: the certificate takes about 160 times as long as the mesh (1.3 s to 25 s here
  against 7 to 114 ms). It is a check to run on demand, not a part of meshing. Nothing was done for speed: every cube
  that may hold a zero asks for interval enclosures on its nodes, 12 edges, 6 faces and its inside, each up to twice
  halved per axis, and nothing but the edge and face results is shared between cubes.
- Why IWP and Lidinoid fail at one period was not diagnosed. Their walls have 12 components (IWP) and thin parts at
  the cap; the cubes left are counted under the block limit and the direction test.
- 16 cells per period is too coarse for the direction test on gyroid, Schwarz P, Schwarz D and Neovius; 32 is enough
  for these four. IWP is certified at 16 cells per period (two periods) and not at 32 or 64 (one period).
