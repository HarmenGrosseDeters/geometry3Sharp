# ContouringCertificate

The topology certificate of the contouring, stage 1
([docs/design/CONTOURING_TOPOLOGY.md](../../docs/design/CONTOURING_TOPOLOGY.md), on #749): class P, the winding
number of a clean closed mesh, on the leaf grid of `MeshWindingNumberSolidify` (adaptive).

## Spike (commit 0): what the exact level-set tests refuse, and what they cost

Measurement only. `LevelSetCertificate.cs` is the prototype of the exact tests of Theorem 3 (section 7.4): per grid
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
