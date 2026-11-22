# ContouringIsotopySubdivision

Research spike (2026-11-22): a subdivision isotopy certificate for the `DualContouring` output of an implicit function,
after Plantinga and Vegter (2004 / 2007) and Burr and Byrd (arXiv 2302.04908). Note with the proof sketch, the failure
cases, the measurements and the options:
[docs/research/CONTOURING_ISOTOPY_SUBDIVISION_2026-11.md](../../docs/research/CONTOURING_ISOTOPY_SUBDIVISION_2026-11.md).

A measurement prototype. Public API only, no library change, procedural inputs, paper text only (no code of others).

## What it computes

For a field with enclosures of its value and gradient over a box (`Fields.cs`), on the uniform grid that
`DualContouring` uses:

| part | file | what |
|---|---|---|
| intervals | `Iv.cs` | a local copy of the semantics of the library's internal `ExactInterval` (round to nearest, widen by one unit in the last place), plus division, square root, and sin / cos by a Taylor polynomial with remainder (no libm) |
| PV | `Certificate.cs`, `GridCertificate` | every grid cell passes C0 (the enclosure of f excludes 0) or C1 (no two gradients in the cell are orthogonal); a failing cell is cut into 2^t sub-boxes per axis (t up to 4) only to tighten the enclosures; C1 is then tested for every pair of sub-boxes (`all`) or on the hull of their gradient enclosures (`hull`, the published form) |
| S | the same class | the cell test (S0) to (S3) of Theorem 5 of [CONTOURING_TOPOLOGY.md](../../docs/design/CONTOURING_TOPOLOGY.md) 7.8 with the same enclosures (t up to 3), for comparison |
| subdivision | `Subdivision` | Plantinga and Vegter's first phase: an octree split until C0 or C1 holds, to four levels below the grid; counts only, no balancing, no mesh |
| contour | `Program.cs` | `DualContouring` on the same grid and node values, its topology against the truth |
| class P | `Program.cs` | the built certificate (`MeshWindingNumberSolidify.ComputeTopologyCertificate`, blocks and growth) on a marching-cubes mesh of the same field (160 cubes across), same cell size; it certifies its own contour, not the one above |

Cases (`Make` in `Program.cs`): the feature-width families of `benchmarks/ContouringTopology` (torus, gap, shell,
tunnel, rod; width k cells), a sphere, three TPMS with a smooth cap (gyroid, Schwarz P, diamond; the gyroid also at
levels towards its pinch-off), Cassini surfaces towards their singular member (b / a towards 1), and a sphere through
grid nodes. Seed -1 is the shape as defined with a grid shift, seed 0 a random rotation and shift.

## Run

```
dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- selftest
dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- invariance
dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- hull > benchmarks/ContouringIsotopySubdivision/results/hull.csv
dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- all > benchmarks/ContouringIsotopySubdivision/results/all.csv
python3 benchmarks/ContouringIsotopySubdivision/analyze.py benchmarks/ContouringIsotopySubdivision/results/all.csv
dotnet run -c Release --project benchmarks/ContouringIsotopySubdivision -- run torus:2 32 0 classP
```

`selftest` checks the prototype itself: the sin / cos enclosures against `Math.Sin` / `Math.Cos`, every field's value
and finite-difference gradient at random points of random boxes, and a cell-level oracle of the C1 test (in every cell
it passes, sampled gradients are pairwise less than 90 degrees apart). `G3_CIS_MUTANT=1` is a hand mutant of the C1 test
(each sub-box against itself only); `selftest` must fail with it (222 of 2 033 cells). `invariance` scales ten cases by
2^10, 2^-10, 2^40 and 2^-40 and compares the rows bit for bit (39 of 40 equal; the quartic field at 2^-40 leaves the
`float` range of the contouring's node values and is refused). `all` takes about 40 minutes on 4 cores; its raw rows
are in `results/all.csv`, the tables of the note come from `analyze.py`. `run <case[:k]> <N> [seed] [classP] [maxT]
[maxTS]` is one row; `hull` (one minute) repeats the PV test of `all` in its published form (the hull of the sub-boxes'
gradient enclosures as one box, `results/hull.csv`: 26 runs certified against 28 in the pairwise form of `all.csv`; the published form is the one the
paper's proof covers, note section 4.3); `debug <case[:k]> <N> <seed>` prints sampled values and gradients of cells that fail the PV test.

## Columns of `all.csv`

- `truth`, `dc_sig`, `dc_right`, `dc_clean`: components and genus (`c=1;g=1`) by construction (for the capped TPMS: the
  contour of the finest grid whose PV certificate passes), the contour's, whether they agree, and the mesh side as far as the
  public API shows it (closed, `SelfIntersectionFree`, no non-manifold triangle).
- `surface_cells`: grid cells not proven zero-free. `pv_cells`, `s_cells`, `both`, `neither`, `pv_share`, `s_share`:
  certified by each test. `pv_certified` / `s_certified`: all surface cells certified and none in the outer layer.
- `pv_at_t0` .. `pv_at_t4`: the depth t of the tightening at which C1 passed (pairwise to t = 3, a 45 degree cone at t = 4). `pv_refuted`: cells with two points whose
  gradients have a certified inner product <= 0 (C1 is false, not only unproven).
- `not_smooth_cells`: a possible non-smooth point of f in the cell (two active pieces of a min / max).
  `uncertain_node_cells`: a node whose sign the enclosure does not decide.
- `ambiguous_faces_in_pv`, `multi_patch_in_pv`: faces with alternating corner signs, and cells with more than one patch,
  among the PV cells.
- `s_fail_*`: the first condition of Theorem 5 that failed (nodes, edges, faces, no graph direction, several circles).
- `sub_*`: the octree: leaves, C1 leaves, leaves unresolved four levels below the grid (and how many of them hold a
  possible non-smooth point), the deepest and the most frequent level of a C1 leaf (0 = a grid cell, positive = finer).
- `pv_evaluations`: field enclosures the PV test computed. `pv_ms`, `s_ms`, `sub_ms`, `dc_ms`: wall time of the PV test alone, the extra time of the S test, the octree, and
  `DualContouring.Generate`. One run, serial certificate code, 4 cores for the contouring.
- `class_p`, `class_p_uncertified`, `class_p_sig`, `class_p_ms`: the built certificate on the meshed field (`no-input`:
  the marching-cubes mesh is not closed or has the wrong topology itself).
