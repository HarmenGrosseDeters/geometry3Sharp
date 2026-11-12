# Alpha Wrap 3.0: the 800 run after L1 and the tail report (2026-11-12)

Run: master 14a27e45 (after #681 L1, #670 L4), Thingi10K sample800 seed 2611 (`benchmarks/MeshRepairBenchmark/fetch_data.py
--sample 800 --seed 2611 --no-large`), `BENCH_REPAIR=wrappipe BENCH_FIDELITY=hd TIMEOUT=400 benchmarks/MeshRepairBenchmark/run.sh
<mesh>` (alpha = diag / 300, offset = diag / 3000, warm = soup, quality pass off), one process at a time on the 4-core cloud
container (default thread count = 4), `aw3/drive.sh`-style driver, two chunks. Rows: `f800_aw3.jsonl`; analysis `tail800.py`
(output `tail800.txt`), geometry `stlgeo.py` (`tailgeo.jsonl`, `medgeo.jsonl` for 40 random meshes below 20 s).
Time T = `pipe_ms` (wrap + simplification + certification), budget max(5 s, 50 us x raw triangles), this container 1.136x
slower than the f800 machine (cal.jsonl).

## Targets
- (a) within budget: **794 / 800 (99.2 %)** at f800-machine-equivalent times (T / 1.136); 758 at raw container times. The 2.0
  final run (f800) had 611. Over budget: 1514905 (1.28x), 69259 (1.17x), 860049 (1.13x), 64891 (1.06x), 78968 (1.04x), 39368 (1.00x).
- (b) time sum 0.732x of f800 (f800-equivalent); the A/B factors of the levers were L4 ~1.075x and L1 1.054x on the
  pipeline; the rest of the gap to f800 is master's other changes since dfc93fdb plus machine noise (not alternating).
- (c) 800/800 closed; output identical to f800 on 50 (L4's skipped confirmation and the contact fix change the others by
  design, as reported in #651 / #670).

## Distribution
p50 4.9 s, p90 9.8 s, p99 29.8 s, max 66.5 s. Time is linear in the raw wrap size: R^2 = 0.93 for T against raw triangles,
48.8 us per raw triangle (slope); median 37.6 us per raw triangle.

## The 17 meshes above 20 s (owner rule: diagnosis each)

See `tail800.txt` for the full table (phase split, parallel share, Amdahl ceilings). Summary:

| mesh | in tris | raw | T s | us/raw (x median) | area / diag^2 | components / far part |
|---|---:|---:|---:|---:|---:|---|
| 40118 | 4252 | 1204976 | 66.5 | 55 (1.47) | 10.0 | 1 |
| 860049 | 21373 | 840960 | 53.8 | 64 (1.70) | 6.9 | 10, largest 35 % |
| 69261 | 6720 | 725154 | 36.0 | 50 (1.32) | 5.8 | 11, diag 1.25x the largest's |
| 222695 | 64318 | 717714 | 34.8 | 49 (1.29) | 1.4 | 1 |
| 535629 | 2938 | 686084 | 33.6 | 49 (1.30) | 5.6 | 1 |
| 222687 | 32158 | 681274 | 32.6 | 48 (1.27) | 1.4 | 1 |
| 500097 | 7192 | 607884 | 31.9 | 52 (1.40) | 4.9 | 1 |
| 520646 | 708 | 669034 | 30.4 | 45 (1.21) | 5.4 | 1 |
| 500115 | 3832 | 580516 | 29.8 | 51 (1.36) | 4.7 | 1 |
| 78968 | 5182 | 486720 | 28.8 | 59 (1.58) | 4.0 | 1 |
| 679615 | 94 | 603690 | 28.1 | 47 (1.24) | 5.0 | 1 |
| 500099 | 348 | 604086 | 24.5 | 41 (1.08) | 4.9 | 1 |
| 49901 | 172 | 569246 | 23.4 | 41 (1.09) | 4.7 | 4, diag 1.5x the largest's |
| 111959 | 25562 | 412740 | 23.0 | 56 (1.48) | 1.5 | 674 (soup) |
| 224108 | 11180 | 539522 | 20.9 | 39 (1.03) | 1.1 | 1 |
| 49998 | 12188 | 445596 | 20.9 | 47 (1.25) | 3.7 | 4, diag 1.5x the largest's |
| 84042 | 9328 | 387524 | 20.3 | 52 (1.39) | 3.0 | 1 |

Diagnosis: **size from the fixed resolution, not pathology.** Every tail mesh has a raw wrap of 0.39-1.2 M triangles (17 of
the 20 meshes above 380k raw triangles are the tail) while the inputs have 94-64k triangles; the time per raw triangle is
1.03-1.70x the median (the higher ones carry more pinch-pass trial time: 860049 29 %, 111959 25 %, 78968 21 %). The raw size
follows the input's surface area against alpha^2 = (diag / 300)^2: the tail's median area / diag^2 is 4.7 against 1.1 for 40
random meshes below 20 s (lattices, perforated and thin plates, wire-like parts). Far parts that inflate the diagonal (the
#693 cause): 3 of 17 have a total diagonal 1.25-1.5x the largest component's, about the rate of the random set (5 of 40),
so they are not the driver of the tail; #692's cause (a fixed diag / 300 resolution regardless of the input's size) is.
No mesh is pathological in the sense of a hotspot (no phase above its usual share; the largest single phase is the carving
loop's oracle + touch at 13-31 %).

Parallel share today: 19-30 % (reduction rounds and certification). Amdahl on 16 cores with today's parallel parts: 40118
66.5 -> 56 s; with every part perfectly parallel: 26.8 s on 4 cores, 6.7 s on 16. So the owner's "90 s -> 10 s" needs the
wrap itself parallel ((B), round-based carving) or a smaller raw wrap.

## Lever proposals (owner decides)
1. **Wrap size budget**: choose alpha (and offset with it) so that the raw wrap stays below a budget (eg 300k triangles),
   from an estimate of the offset surface's area (input area plus the offset's share) / alpha^2, or from a coarse pass. Cuts
   every tail mesh to <= ~15 s at the cost of fidelity on high-area inputs (alpha grows by sqrt(raw / budget), up to 2x on
   40118); the result stays certified (the simplification's Hausdorff check uses the actual alpha). The relative alpha of
   the API stays a request; the budget would be an option or a repair default.
2. **Per component / largest component diagonal** (the #693 cause): alpha relative to the largest component's diagonal, or a
   wrap per component; small effect on the 800 tail (3 of 17), relevant for the repair fallback (#693, wrap_too_coarse).
3. **(B) round-based parallel carving**: the only lever that cuts the tail without changing the resolution; ceiling 4x on
   4 cores for the wrap part, more on 16; probe first (owner list item 3).
