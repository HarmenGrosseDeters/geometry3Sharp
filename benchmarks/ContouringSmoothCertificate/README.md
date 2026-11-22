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
