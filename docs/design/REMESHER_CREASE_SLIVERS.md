# Remesher crease slivers: upper bounds (2026-11-09)

Question (coordinator, owner 2026-11-09): with `MinTriangleQuality = 0.2` the slivers that remain sit at the 30 degree crease constraints (section 7 of
[REDUCER_REMESHER_20.md](REDUCER_REMESHER_20.md); without creases p01 is 12 degrees but the F-score falls from 0.77 to 0.38 and the Hausdorff distance grows 4 x). Measure the upper bound of
(1) resampling the crease polylines to the target edge length before the surface passes, (2) tangential smoothing of the crease vertices along their curve instead of pinning them, and
(3) say whether feature-curve operators (Wang et al. 2019) are the only way left. Papers only; nothing is built, no default changes.

Setup: the 10 Thingi10K meshes with the most slivers or the most creases (104582, 125069, 135363, 196126, 105686, 78295, 93370, 299291, 39460, 1053376), the `g3cli remesh` settings of the
baseline harness (20 passes, smoothing 0.5, projection onto the input, boundary and 30 degree creases kept), `MinTriangleQuality = 0.2`, target edge 1 x the mean edge, 4 cores, scratch
programs (not committed) on top of the library at master a5b4e60; fidelity with the harness's measures (`MeshHausdorffDistance` at tolerance 0.001 of the diagonal, `MeshFidelityMetrics`
F-score at 0.1 %). Slivers = triangles below 0.5 or above 170 degrees. The only committed change is the harness knob `remesh_corner=<deg>`.

## What the slivers sit on

Of 878 slivers in the 10 results, 97 % have a **pinned** vertex (a vertex with `VertexConstraint.Fixed`), 87 % have **two or more** (104582: 408 of 412; 135363: 161 of 163). The pins are not the
corner pins of `PreserveBoundaryAndFeatures`: with the corner angle raised to 60, 90, 120 and 181 degrees (no corner pins at all, harness `remesh_corner`) p01, the sliver count and every fidelity measure stay
the same (x1: slivers 1173 / 1166 / 1161 / 1174 / 1181, p01 median 1.66-1.69, F 0.662, Hausdorff both ways equal to 3 digits; x2 the same). They are the **ends and junctions of the crease spans**: on noisy
meshes the 30 degree crease network falls apart into hundreds of short fragments (125069: 763 curve vertices, 2352 pinned vertices without corner pins), every fragment end is pinned at the input's position,
and a triangle between two pins is shaped by the input's own vertices: nothing the remesher moves can improve it.

## Levers

| variant (x1, 10 meshes) | p01 median | slivers | F 0.1 % median | Hausdorff in to out, median (max) | Hausdorff out to in, median (max) | time |
|---|---|---|---|---|---|---|
| current (robust setting) | 2.54 | 1169 | 0.671 | 3.56 % (24.0 %) | 1.58 % (11.6 %) | 7.4 s |
| (1) crease edges resampled first: 3 passes with only the crease edges allowed to split / collapse, then the normal 20 | 2.43 | 1277 | 0.690 | 3.26 % (23.3 %) | 1.56 % (11.6 %) | 7.9 s |
| (2) tangential smoothing of crease vertices along the curve | **in the current setting already**: `ComputeSmoothedVertexPos` moves a curve vertex towards its two neighbours on the same curve when `MinTriangleQuality > 0`; the row above includes it | | | | | |
| free ends of crease spans not pinned (slide along the curve) | 3.41 | 1133 | 0.671 | 3.54 % (24.0 %) | 1.55 % (11.6 %) | 6.9 s |
| crease spans of fewer than 4 edges not constrained | 3.97 | 512 | 0.522 | 5.12 % (23.9 %) | 2.16 % (11.6 %) | 6.3 s |
| ... fewer than 8 edges | 4.73 | 478 | 0.523 | 4.29 % (27.2 %) | 3.32 % (11.6 %) | 6.1 s |
| ... fewer than 16 edges | 5.58 | 444 | 0.486 | 5.39 % (27.2 %) | 3.33 % (11.6 %) | 6.2 s |
| 8 edges and the pre-pass | 4.65 | 475 | 0.520 | 5.07 % (27.9 %) | 3.33 % (11.6 %) | 7.8 s |
| refine locally (half the edge length within one edge length of every triangle below 5 or above 160 degrees), 3 rounds of 4 passes, then 3 normal | 2.68 | 1465 | 0.642 | 3.56 % (28.5 %) | 1.71 % (11.6 %) | 9.5 s |

Reading:
- **(1) does not pay.** The pre-pass changes nothing measurable (slivers +9 %, F-score +0.02): the crease edges are already within the 0.66-1.33 band after the normal passes; the short crease edges (p10 of the
  crease edge length is 0.05-0.2 of the target) sit between pinned ends, where no collapse is allowed and the sampling cannot change.
- **(2) is already in.** There is nothing left to build; unpinning more vertices is the free-ends row: p01 +0.9 degrees, slivers -3 %, fidelity unchanged. Real but small.
- **Dropping short crease fragments** removes 58 % of the slivers (the upper bound of what releasing the pins buys) and costs 22-28 % of the F-score and a doubling of the out-to-in Hausdorff distance: that
  is the "no creases" trade-off in a milder form, outside the T6 gate (not worse than 5 % at the median). It is not a lever; it is the fidelity price of the pins, measured.
- **Local refinement** makes it worse: the new vertices next to a pinned pair add triangles with the same two pins.

## Feature-curve operators (3)

What would reach the remaining slivers is not smoothing or sampling but **moving or inserting pins on the curve while keeping the geometry of the crease**: Wang et al. 2019 (insertion / removal of
vertices along feature curves) is the paper lead already in SOURCES.md; the bound to measure first is the one above, 87 % of the slivers having two pins, so at most -87 % of the slivers (1169 to
about 150) if every such triangle could be repaired, and the fragments of a noisy crease network would have to be merged or snapped, which is a fidelity decision (a crease network is only as good as its
30 degree threshold on the input's own noise). A cheaper first step that needs no paper: a **minimum span length** option (`MeshConstraintUtil.PreserveBoundaryAndFeatures(..., minSpanEdges)`) with the
measured trade-off above, and the free-ends unpin (+0.9 degrees p01 for nothing). Neither is built here: the first is a quality-for-fidelity trade-off the owner decides, the second is too small
to carry a PR on its own.

Proposal to the coordinator: (a) close (1) and (2); (b) the free-ends unpin and a `minSpanEdges` option as one small opt-in PR if wanted; (c) feature-curve operators only after the owner chooses
whether noisy crease fragments may be merged (the measurement says merging them is where the slivers are, and what it costs).
