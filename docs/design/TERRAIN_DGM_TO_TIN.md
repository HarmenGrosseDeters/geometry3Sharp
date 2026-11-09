# DGM to TIN with breaklines: constrained quality meshing in 2.5D

Status: **implemented as `gs.TerrainGridTin`** (`mesh_ops/TerrainGridTin.cs`, owner decisions 2026-11-09: D1 surface error, B2 chosen over greedy+correction); the sections below are the design it follows. Measured cost: section 11. Design only; no code. Part of the terrain restart
([roadmap/terrain.md](../roadmap/terrain.md)); follows the DGM reader (`HeightGrid`, `HeightGridReader`, PR #479).
Owner decision 2026-11-08: the step is built on `QualityMesh2` (its exact predicates, oracle and golden tests), not on a
new triangulator.

## 1. Goal and non-goals

**Goal.** From a `HeightGrid` (dense DGM, NaN gaps) and optional breaklines, build a `DMesh3` TIN that

1. is a height field (CCW seen from above, no overlap in XY), so the slope code accepts it (`RequireHeightField`);
2. contains every breakline as a chain of edges (bench crests and toes, pit rims);
3. stays within a caller-given vertical error `Epsilon` of the grid surface, with far fewer vertices than the 2 triangles per cell
   of `HeightGrid.BuildMesh`;
4. reports what it achieved (error, counts), so that the slope accuracy budget (TERRAIN.md Q2: approximation, margin, rounding are
   kept apart) can add it to `ε_D`.

**Non-goals (v1).** Automatic breakline detection (`TerrainBreakLines` can feed the input later); GeoTIFF; triangle quality
(`MinAngle`); changes to `QualityMesh2` itself; a CLI command (comes through the CLI/MCP session).

## 2. Reference surface and what "error" means

*Truth* is the grid TIN: `HeightGrid.Height` (fixed diagonal, no-data cells have no surface), documented equal to `BuildMesh()`.

* **Node error** of a TIN `T`: `max over valid grid nodes |T(x_ij, y_ij) - z_ij|`. This is what `TerrainGreedyTIN` guarantees today
  (Garland and Heckbert 1995) and what the existing benchmark numbers are for.
* **Surface error**: `max |T - S|` over the grid TIN `S`. Both are piecewise linear, so the difference is piecewise linear on the
  overlay and its maximum is at overlay vertices: grid nodes, TIN vertices, and crossings of TIN edges with grid lines and
  diagonals. The node error is **not** a bound for it: along a grid edge the difference has kinks where TIN edges cross, and there it can exceed
  the node bound (the bound is only at the nodes).

Proposal: **guarantee the node error** (`AchievedNodeError <= Epsilon`), and **compute the surface error exactly in a verification
routine** (the overlay vertices above, `O(edges x cells crossed)`), reported in the result and used by tests and the benchmark.
The spike measures `surface / node`; if it is far from 1, an option `GuaranteeSurface` adds the nodes next to violating overlay vertices
until the exact surface error is within `Epsilon`.

## 3. Pipeline

```
HeightGrid + breaklines
  1. domain       cell mask -> boundary rings and holes (cells with all four corners valid)   [new, small]
  2. index space  coordinates (i, j) in cell units, exact small numbers                          [decision D3]
  3. seed         domain corners/ring nodes + breakline vertices (z from the grid TIN)
  4. loop         ConstrainedMesh2 (QualityMesh2, MinAngle 0, no refinement)  ->  node errors on the real output
                  -> insert the worst node of every triangle above the round's threshold      -> repeat until max <= Epsilon
  5. output       DMesh3 (x = X0 + i * Cell, z = grid), edge flags for breaklines, result record
```

**Why an outer loop on top of a constrained Delaunay engine.** `QualityMesh2` is driven by angle, area and a size function; it has no
error-driven insertion. Alternatives:

| | A: new greedy CDT (TerrainGreedyTIN with constraints) | B1: size-field meshing | **B2: error-driven rounds around `QualityMesh2` (proposed)** |
|---|---|---|---|
| Triangulator | new, own predicates and tests | `QualityMesh2` | `QualityMesh2` (CDT, `MinAngle = 0`) |
| Bound | by construction | a-posteriori only (`h = sqrt(8 eps / kappa)` from curvature, then verify and shrink) | by construction (the loop ends when the checked output is within `Epsilon`; terminates: inserting every node gives error 0) |
| Vertex count | best known (sequential greedy) | usually higher, vertices off the grid | between greedy and about 1.3x of it (to be measured) |
| Owner decision 2026-11-08 | violates it | fits | fits |

B2 needs no change of the mesher. The error is measured on the **real output** of the mesher each round, so the choice of
diagonals at cocircular points (the F-63 failure of the old library, a regular grid is all cocircular) cannot invalidate the bound.
Each round is a full constrained Delaunay of the current point set. Inserting the worst node of every triangle above a threshold
(for example `max(Epsilon, 0.5 x current maximum)`) keeps the rounds to roughly 10-15; the spike measures rounds, count and time against
`TerrainGreedyTIN` (3.2 s and 0.52M-0.76M vertices per km^2 at 0.05 m, benchmarks/TerrainBenchmark/README.md).

## 4. Breaklines

* Input: `TerrainBreakline { IReadOnlyList<Vector3d> Points; bool Closed; }`, XY in plan coordinates, z optional.
* In the mesher they are **locked segments** (spec O9): only split where they cross other segments (`ResolveCrossings`). Their
  intermediate vertices are ours: when the error along a breakline is above `Epsilon` the loop inserts the point of the breakline
  at the worst place (z from the grid TIN there), so the breakline is densified by the loop, never by the mesher.
* **z of a breakline** (decision D4): `FromGrid` (default: the grid is the truth, the bound holds along the breakline; the survey z is
  not used) or `FromInput` (survey z; the bound against the grid cannot be guaranteed along it, and the result reports the deviation
  on the breakline separately). Crossing breaklines with different z are only possible in `FromInput`; the rule of spec D11 applies
  (lowest index wins), reported.
* Validation with a status (no exceptions): non-finite input, a breakline vertex in a no-data cell, a breakline outside the domain (clipped to the domain, count reported),
  duplicate vertices (merged).
* Output: every breakline subsegment is an edge of the `DMesh3`; `Result.BreaklineEdges` lists them, so the slope code need not
  rediscover creases (TERRAIN.md 2.4: every output triangle must lie in a single facet, so creases must be edges).

## 5. Index space, exactness, determinism

Meshing happens in **grid index coordinates** `(i, j)` (breakline vertices fractional). The coordinates are small, so predicates are
cheap and exact, and the TIN of a grid does not depend on the origin or the cell size: **scaling by 2^k and translating the origin give the same
index-space mesh bit for bit** (this is the invariance test of CLAUDE.md, and it holds without tolerances). Output vertices are
`X0 + i * Cell`, `Y0 + j * Cell`; the heights are the grid values at nodes (exact) and `HeightGrid.Height` at breakline points.
`SnapTolerance` is set to 0 (no rounding of breakline crossings beyond the mesher's automatic one). Results do not depend on thread count (the
mesher is deterministic, spec O22; the error scan may be parallel per triangle with an order-independent maximum).

## 6. How the slope functions consume it

* Input type is `DMesh3` (height field), as today. The TIN replaces the 2-triangles-per-cell mesh as the terrain of
  `TerrainSlopeOverlay` / `TerrainVolumeDifference`; no change in the slope code.
* The catch line's horizontal uncertainty is `ε_D / |grad(S - T)|` (TERRAIN.md 5.5): `Epsilon` (and the reported node/surface error)
  enters `ε_D` as the terrain's approximation term, next to the grid's own accuracy.
* Breaklines as edges keep bench crests and toes exact; without them a triangle straddling a toe is the "interpolation across a
  crease" of TERRAIN.md 2.4. The `BreaklineEdges` list can feed `TerrainBreakLines`-style consumers and safety lines.
* No-data holes become holes of the TIN (the slope code already rejects or handles them as for any open terrain).

## 7. Decimation

v1: forward selection only (the rounds above). `TerrainGreedyTIN.RemoveVertices` removes another 31-46 % of the vertices at the same
bound, at 5-7x the time. Plan: build v1, compare the vertex count with `TerrainGreedyTIN` (with and without removal) in the benchmark;
a removal pass (vertex removal by ear clipping, breakline edges and ring vertices not removable) is added only if the count is above
1.3x the greedy count or the owner wants the minimum. A removal pass has to re-verify the node error and, by D2, runs on the
`DMesh3` result, not inside the mesher.

## 8. Tests (acceptance)

Procedural terrains only (no geodata): a paraboloid pit with benches (breaklines along crest and toe), a ridge/valley terrain, a terrain with holes.

* Node error `<= Epsilon` by brute force for several `Epsilon`; the exact surface error equals an independent sampling bound from above (sampled maximum <= exact).
* Height field: all triangles CCW, no XY overlap (the exact overlap check of the slope code), mesh closed except at the domain boundary.
* Every breakline subsegment is an edge chain; the chain lies on the breakline within the stated tolerance (exactly in `FromInput` XY).
* Invariance over `Similarity.All()` where meaningful (2^k scales and translations: bit for bit; rotations: grid not rotatable, so not applicable).
* Reuse (compute twice on one instance), determinism (repeated, parallel scan equals serial).
* Mutation checks of the loop (threshold, insertion rule, error scan), `Slow` trait if a case needs more than 5 s.
* Golden: the pit terrain's vertex/triangle counts pinned in the format of MESHING2D_GOLDEN_FORMAT.md; end-to-end with the slope design on the pit.
* Differential: vertex count and node error against `TerrainGreedyTIN` on the benchmark windows (fetch_data.py data, not committed).

## 9. Plan

1. **Spike** (benchmark only, `benchmarks/TerrainBenchmark`): rounds/threshold, vertex count and time against `TerrainGreedyTIN`, ratio surface/node
   error, memory. Upper-bound check first: the sequential greedy numbers are the reference; stop if the vertex count is above 2x
   of the greedy count or the time above 5x, and report.
2. Library PR (one): domain extraction, loop, breaklines, result record, tests above. Additive API only.
3. Docs and CLI/MCP command through the CLI/MCP session. Later: `GuaranteeSurface`, removal pass, `MinAngle` quality option (must re-verify the bound).

## 10. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Bound: node error guaranteed + exact surface error reported, or surface error guaranteed | Node error guaranteed, surface error reported; `GuaranteeSurface` later if the spike shows a large ratio |
| D2 | Triangulator: B2 (rounds around `QualityMesh2`), B1 (size field), or port greedy with constraints | B2 |
| D3 | Mesh in grid index space (exact, origin- and scale-free) | Yes |
| D4 | Breakline z: from the grid (bound holds) or from the survey (reported deviation) | `FromGrid` default, `FromInput` as option |
| D5 | Decimation in v1 | Forward only; removal pass only if the count is above 1.3x greedy |
| D6 | Triangle quality (`MinAngle`) | Off in v1 (the slope code does not need it; sliver terrain triangles are harmless there) |
| D7 | Breaklines: caller-provided only in v1; detection by `TerrainBreakLines` as an optional converter later | Yes |
| D8 | Spike before the library PR, with the stop rule of section 9 | Yes |

## How others solve it

* TIN extraction from dense grid DEMs: Fowler and Little, *Automatic extraction of Irregular Network digital terrain models*, SIGGRAPH 1979
  ([doi:10.1145/800249.807444](https://doi.org/10.1145/800249.807444)): characteristic points and lines first; the VIP method (Chen and Guevara, *Systematic selection
  of very important points from digital terrain models*, Auto-Carto 8, 1987) ranks points by significance and drops those below a threshold.
* Garland and Heckbert 1995 (greedy insertion, the library's `TerrainGreedyTIN`); Lee 1991 compares drop-heuristic, VIP and related selections on DEMs
  (the comparison reports the drop heuristic as the most accurate and the most expensive; secondary source: GRASS/ArcGIS documentation and a Virginia Tech thesis).
* Breaklines in GIS TINs: constrained Delaunay (each breakline edge inserted as one edge, fewer vertices) versus conforming Delaunay (densified, better triangles)
  (ArcGIS "Fundamentals of TIN triangulation", [GRASS wiki: TIN with breaklines](https://grasswiki.osgeo.org/wiki/TIN_with_breaklines)). B2 uses the constrained form with locked breaklines
  (spec O9) and densifies only where the vertical bound requires it.
* The error-driven refinement loop around a Delaunay engine is the batched form of the greedy method; the old library's own terrain simplification failed
  exactly where this design measures the real output (F-63, MESHING2D_SPEC).
* Not found: a published method that gives a *guaranteed* vertical bound together with locked breaklines on a constrained Delaunay engine; the guarantee here comes from the a-posteriori check of each round.
  (Searched: "terrain TIN grid DEM breaklines vertical error bound", "Fowler Little very important points".)

## 11. Implemented: `TerrainGridTin` and what the surface guarantee costs

API (additive): `TerrainGridTin { Grid, Epsilon, Breaklines, MaxRounds, CancelF; Compute(); Status, Message, Result (DMesh3), SurfaceError, NodeError, Rounds, BreaklineEdges, LockedCreases, DroppedBreaklineSegments }`,
`TerrainBreakline(points)`, `TerrainGridTinStatus`, and `TerrainGridTin.ErrorOfTin(grid, tin, out nodeError, out surfaceError)` (the exact scan of any TIN, eg `TerrainGreedyTIN`'s result).
Differences from the proposal: breaklines take their heights from the grid only (D4 `FromGrid`; `FromInput` is not implemented); the domain is every cell with all four
corners valid: the boundary runs are locked segments and the triangles over cells without data are dropped (a breakline segment must lie in the data over its whole length, else it is dropped); creases of the grid surface (cell diagonals and cell sides) are locked
where a violation sits on them and all corners are selected (cell sides are needed as soon as breakline vertices lie off the lattice); the error scan runs in parallel, the
violations are acted on in triangle order.

Measured (pit terrain of `benchmarks/TerrainBenchmark` `tinspike`, 513 x 513 nodes, cloud runner, single runs, `Epsilon` 0.1 unless noted; `TerrainGreedyTIN` has the node guarantee only,
and its own surface error is 0.19 / 0.44 / 1.55 for the 3 m / 8 m / 30 m terrains):

| Terrain | TerrainGridTin vertices (surface error) | time | Rounds | TerrainGreedyTIN vertices (time) | Ratio |
|---|---|---|---|---|---|
| 3 m hills | 4991 (0.0999) | 0.84 s | 17 | 3042 (0.08 s) | 1.64x |
| 8 m | 6679 (0.1000) | 1.06 s | 19 | 3908 (0.09 s) | 1.71x |
| 30 m, cliffs | 10 988 (0.1000) | 1.64 s | 19 | 4896 (0.11 s) | 2.24x |
| 30 m, eps 0.2 | 6863 (0.1993) | 0.94 s | 17 | 3958 (0.09 s) | 1.73x |
| 30 m + 4 breaklines (1800 segments) | 14 040 (0.1000) | 2.35 s | 14 | | |
| 30 m + breaklines + 39 x 29 gap | 14 006 (0.1000) | 2.10 s | 13 | | |

Rounds: the first 6 rounds act on the worst violation of each triangle, later rounds on up to 16 separated ones (at least 16 cells apart); without the later multiple requests a long straight step needs
rounds in proportion to its length (300 cells: more than 200), with them 7-20 rounds (steps of 300-1000 cells), and the vertex count on the pit terrains grows by 3 % (acting on several violations from the first round
would double it).

Cost of the surface guarantee: against a node-only guarantee in the same method +10 % vertices on gentle terrain and +33 % on the cliff terrain (spike, benchmarks/TerrainBenchmark README); against `TerrainGreedyTIN`
1.6-2.2x the vertices and about 10-20x the time (the time is the constrained Delaunay of every round, 90 %). Decimation (D5) is not part of it.
