# ContouringCertificate

The topology certificate of the contouring, stage 1
([docs/design/CONTOURING_TOPOLOGY.md](../../docs/design/CONTOURING_TOPOLOGY.md), on #749): class P, the winding
number of a clean closed mesh, on the leaf grid of `MeshWindingNumberSolidify` (adaptive).

## Spike (commit 0): what the exact level-set tests refuse, and what they cost

Measurement only. The prototype (in commit 0 `LevelSetCertificate.cs`, since commit 1 the library's
`ContouringTopologyCertificate`, with the same refusals in every run) of the exact tests of Theorem 3 (section 7.4): per grid
cell that the level set L or the contour touches, general position, the crossing count of every edge (at most one,
and one exactly where the contour's node signs change), the parts of L on every face (`|T| - |M| = 1`, two ends, the
pairing of the face rule on faces with four crossings) and in the cell (`|T| - |M| + |V| = 1`). L is the input's
components across which the exact winding number steps from 0 to 1 (`LevelSetComponents`: the signed crossings of
a line through one vertex per component, `VolumeSampleCells.WindingAlong` and the perturbed
`ExactRayTriangle.LineCrossing`). Exact: `Orient2d`, `Orient3d` and comparisons only; serial. The grid is the
unrefined leaf grid (`WindingNumberOctree`, the grid offset of `WindingContouring`), the node signs the float values
`DualContouring` compares; the contouring cells are the octree's surface cells and the cells around their
sign-changing edges. The mesh side (Theorem 2) is not part of the spike.

```
dotnet build benchmarks/ContouringCertificate -c Release
B="dotnet run -c Release --no-build --project benchmarks/ContouringCertificate --"
dotnet build-server shutdown                    # (a lingering compiler server skews the timings)
python3 benchmarks/LevyClassification/fetch_thingi.py <dir> <the 20 T1 ids below>
$B spike spike.jsonl mesh:all <dir>/<id>.stl... -- 64,128
python3 benchmarks/ContouringCertificate/analyze.py spike.jsonl
$B predicates                                   # ns per Orient3d / Orient2d call (filter path)
```

Inputs: the lever-6 set of `benchmarks/SharpFeatureProbe` (T1 Thingi10K 96675 96773 64983 43551 1582399 57084
104640 94664 73088 1582404 37731 57714 116871 109459 472011 37111 113418 79961 1038438 66112, fetched at run time,
not committed; the 6 procedural CAD solids). Clean = closed and manifold after merging vertices with identical
coordinates, no triangle of zero area, consistently oriented, no self-intersections (exact): 14 T1 meshes and the 6
solids, the same 20 as the design note's section 2.2. Master 58644af, 4 cores, 2026-11-13.

| set | cell | inputs | surface cells | refused | share | max per input | by reason (cells) | wrong contours (refused cells) | check ms | contour ms |
|---|---|---|---|---|---|---|---|---|---|---|
| procedural | / 64 | 6 | 41420 | 44 | 0.11 % | 0.61 % | edge 44 | - | 834 | 1468 |
| T1 clean | / 64 | 14 | 63874 | 2095 | 3.28 % | 14.02 % | edge 1234, face 17, pairing 848 | 96773 (228), 116871 (483) | 1483 | 2565 |
| procedural | / 128 | 6 | 164508 | 0 | 0.00 % | 0.00 % | - | - | 3229 | 3501 |
| T1 clean | / 128 | 14 | 265039 | 2321 | 0.88 % | 4.27 % | cell 2, edge 2294, face 13, pairing 13 | 116871 (1070), 472011 (90), 79961 (118) | 8883 | 5929 |

- Surface cells: cells L meets. Refused: those that fail a test of their own, of a face or of an edge (an edge with
  two crossings refuses the four cells around it). "By reason" counts every tested cell, also the few the contour
  touches and L does not.
- **General position refuses nothing** (0 cells in all 40 runs): the odd grid offset of `WindingContouring` keeps the
  CAD faces at round coordinates off the grid planes. No grid shift or symbolic rule is needed for this set.
- **Every wrong contour is refused**: the five runs at / 64 and / 128 whose raw contour has the wrong topology
  (design note, section 2.2) all have refused cells; no right run is certified wrongly (there is no mesh side yet).
- Most refusals are edges that L crosses twice (thin features, the hidden gaps and walls), as expected.
- Time: "check" is the prototype (binning, the cell tests and the level-set classification, which takes 0-109 ms);
  "contour" the whole contouring phase of `MeshWindingNumberSolidify` (octree, crossings, Hermite data, vertices,
  self-intersection check), cleanup and reduction off. At / 128 the prototype costs 1.5 times the contouring on the
  clean T1 meshes, 0.9 times on the solids. One run each, not a timing benchmark.

Upper bound before any performance work (all 20 clean inputs at / 128, one run): the check took 12.0 s against
9.9 s of contouring. Its exact predicates: 13.4 M segment-triangle tests (2-5 `Orient3d` each) and 25.0 M
segment-face tests (up to 4 `Orient2d`), at 56 ns per `Orient3d` and 9 ns per `Orient2d` (`predicates`): about 3.1 s.
Rejecting pairs whose closed bounding boxes are apart (exact comparisons) cut the segment-triangle tests to 4.2 M
and the time only to 11.2 s: the rest is the prototype's bookkeeping (hash sets and dictionaries per cell and face,
40 % of the time in cells that L does not meet at all). The predicates alone bound an implementation with pooled
per-cell arrays at about 1-1.5 s, 10-15 % of the contouring; that is the target for commit 1.

## Commit 1: the exact tests in the library, serial

`mesh_ops/ContouringTopologyCertificate.cs` (exact-code rules: `.editorconfig`, no `float`, no FMA, no parallel
loop). The same refused cells as the spike in every run. What took the time from 1.5 times the contouring to a third
(a profile with `dotnet-trace` after each step; the exact predicates were never more than a tenth of it):

- per-cell hash sets replaced by pooled arrays and stamp marks; the face and edge tables presized (their growth was a
  third of the time); sorted packed bins instead of a dictionary of lists;
- a conservative floating-point test per triangle and cell (the triangle's plane, as computed in double, separates the
  box; Higham's dot-product bound, `may_meet_box`), so that the faces and edges see only the triangles that may meet
  them, and large slanted triangles are binned only where their plane passes;
- for an axis-parallel grid edge the line test is the point-in-triangle test of the projection along the axis
  (`Orient2d`); `Orient3d` only where the line hits the triangle.

Three runs (contouring and check in the same process, `spike ... -- 64,128`; check = the certificate's construction,
the cell tests and the level-set classification; contour = the contouring phase of `MeshWindingNumberSolidify`):

| set | cell | check ms | contour ms | check / contour |
|---|---|---|---|---|
| procedural | / 64 | 209-229 | 784-1197 | 19-29 % |
| T1 clean | / 64 | 465-506 | 2771-2943 | 16-18 % |
| procedural | / 128 | 648-751 | 3655-3886 | 17-21 % |
| T1 clean | / 128 | 1689-1891 | 5410-5510 | 31-34 % |

The gate (about 10-15 %, stop above 25 % at / 128) is not met by the serial path on the clean T1 meshes: what is
left is about 4-9 us per surface cell of fixed costs (about three new faces and three new edges per cell, each with
hash lookups and short loops). Reported to the coordinator with the options (a deterministic parallel path over
chunks of cells, or the serial path for now).

## Commits 2-3: the mesh side and the wiring (`MeshWindingNumberSolidify.ComputeTopologyCertificate`)

```
$B solid solid.jsonl mesh:all <dir>/<id>.stl... -- 64,128,256
```

`solid`: `MeshWindingNumberSolidify` as by default (adaptive, local refinement for hidden gaps), cleanup and reduction
off, once without and once with `ComputeTopologyCertificate`; the truth of a clean input is the topology of its
level-set components (`ContouringLevelSet`), the result's that of `Result` (`MeshTopologySummary`). Master 58644af plus
this PR, 4 cores, 2026-11-13, one run:

| set | cell | runs | certified | uncertified cells (by reason) | wrong results | wrong and certified | certificate ms | contour ms |
|---|---|---|---|---|---|---|---|---|
| procedural | / 64 | 6 | 5 | 44 (edge 44) | 0 | 0 | 233 | 752 |
| T1 clean | / 64 | 14 | 2 | 1269 (edge 1194, pairing 58, face 17) | 2 | **0** | 1099 | 3292 |
| procedural | / 128 | 6 | 6 | 0 | 0 | 0 | 667 | 4016 |
| T1 clean | / 128 | 14 | 4 | 2322 (edge 2294, pairing 13, face 13, cell 2) | 3 | **0** | 2005 | 6106 |
| procedural | / 256 | 6 | 6 | 0 | 0 | 0 | 2823 | 12846 |
| T1 clean | / 256 | 14 | 2 | 4731 (edge 4667, pairing 44, face 18, cell 2) | 2 | **0** | 7793 | 28057 |

- **Never certified on a wrong result** (0 of 78 runs). The seven wrong clean runs of the design note's section 2.2 are
  exactly the wrong runs here, and all seven are reported (96773 at / 64 with 58 cells, 116871 at / 64, / 128, / 256
  with 483, 1071, 2302, 472011 at / 128 and / 256 with 90 and 138, 79961 at / 128 with 118).
- Certified share: of the runs, the CAD solids 17 of 18 (vnotch at / 64: 44 cells where grid edges cross it twice), the
  clean T1 meshes 8 of 42; of the surface cells, 99.1 % at / 128 on the clean T1 meshes (the spike's count of surface
  cells). The refused cells are mostly grid edges a surface crosses twice (thin features, and grid lines nearly tangent
  to a curved surface): what the refinement of stage 2 is for. Without the certificate nothing is certified (before).
- The mesh side refused no cell in any run (no face vertex failed, no transition without face vertices, every vertex
  strictly inside its cell, face or edge); 96773 at / 64 ran on a two-level grid (refine factor 4).
- The six inputs that are not clean report `InputNotClean` (open, a non-manifold weld, a triangle of no area).
- Cost with the wiring (`contour.certificate`: the clean copy with its exact self-intersection check of the input,
  the level-set classification, the cell tests, the mesh side), over two runs: 17-34 % of the contouring on the solids,
  28-41 % on the clean T1 meshes (/ 128: 33-34 %); cell cost about 7 us per surface cell of the T1 meshes at / 128
  (1790-2005 ms for 265040 cells). The second run, on the final code, gave the same results in all 78 runs.
  The cleanup and the reduction come on top of the contouring by default, so against a default `Compute` the share is
  lower.

## Commit 4: the parallel path (`ContouringTopologyCertificate.Cells`)

The cells in contiguous chunks (4 per core), each tested by a worker with its own marks and tables over the shared,
read-only L, bins and grid; faces and edges on the borders between chunks are tested twice. A face's or an edge's
record, and so a cell's reason, is a function of its geometry alone, so the result equals the serial loop bit for bit
(tests: parallel against serial in repeated runs, chunks of 16 and 64 cells; the lever-6 runs below give the serial
runs' statuses and cells in all 52 runs). One entry in `core/ExactCodeAllowList.cs`. Also: the uncertified cells are
sorted at the end instead of all cells, and the serial tables are allocated on first use.

Two runs on 4 cores (`solid ... -- 64,128`; certificate = the clean copy and its self-intersection check, the
level-set classification, the bins, the cell lists, the cell tests and the mesh side):

| set | cell | certificate ms | contour ms | certificate / contour | of it: clean, bins, cell tests |
|---|---|---|---|---|---|
| procedural | / 64 | 198-220 | 768-791 | 26-28 % | 8-10, 26-34, 144-157 |
| T1 clean | / 64 | 628-677 | 2715-2792 | 23-24 % | 63-70, 145-163, 310-404 |
| procedural | / 128 | 413-429 | 3414-3559 | 12 % | 4-6, 58-71, 278-281 |
| T1 clean | / 128 | 871-1002 | 5292-5454 | 16-19 % | 58-64, 210-239, 476-653 |

Against the serial path (commit 3) at / 128: the clean T1 meshes from 33-34 % to 16-19 %, the solids from 17-21 % to
12 %. The target (15 % at most) is met on the solids, not quite on the clean T1 meshes; at / 64 the contouring is short
and the fixed costs (the bins, the clean copy) weigh more. Chunks of 2 or 1 per core were not faster (three runs each).
