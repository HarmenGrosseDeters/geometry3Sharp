# Slope envelope by divide and conquer (prototype)

Status: **prototype design** (owner 2026-11-10), behind an internal switch, not for merge. Context: the incremental construction of the slope map
(`SlopeMap.InsertPiece`, [TERRAIN_1C_PLAN.md](TERRAIN_1C_PLAN.md) section 9) builds 134k faces and 1.6M `split_face` calls for the 48 pieces of
`WavyToe_130mHigh` and simplifies them to 762 faces; the time follows that intermediate size (profile in [roadmap/terrain.md](../roadmap/terrain.md)).
A face merge during insertion shrank the map 20x, gained little (the splits, not the faces, cost) and changed near-degenerate outputs.

## Problem

The slope surface is the lower envelope `E = min(cap, min_k F_k)` over the pieces k of the toe (a strip and the fans at the vertices), where each
piece's surface `F_k` is the maximum of a few planes (its cone planes). The map stores `E` as a planar subdivision of the domain with a plane id
(the label) per face. Today: insert the pieces one by one into one map. A new piece `F_k` is compared with every face it reaches, and the planes of the
piece split the faces in the order of their ids, so the map carries the cuts of every plane that later loses.

## Construction

Divide and conquer over the pieces in index order (fixed, deterministic): `E(S) = min(E(S_left), E(S_right))`.

1. **Leaf.** One piece into a copy of the empty domain map (the same exact kernel, planes and lines are shared): `InsertPiece` as today. The copy of the
   domain triangulation is cheap (list copies); the piece meets about 100 faces.
2. **Merge `A <- min(A, B)`.** For every live face `r` of `B` with label `q` other than the cap (a convex polygon on which `B` is the plane `q`), carve
   in `A`: for every face `f` of `A` that meets `r`, `W = f ∩ r ∩ {P_q < label(f)}`. It is the existing `insert_into` with the planes `[q]` and, in
   addition, the half-planes of the edges of `r` as clip constraints (the lines of `B`'s edges are lines of the shared kernel, so all predicates and
   crossings are the same exact ones as today). `W` is cut out by `split_face` along the lines that bound it and relabelled `q`. The tie rule (the lower
   plane id wins an equal plane) is the one of `insert_into`.
3. **Compaction.** After a merge, faces of the same label across non-barrier edges are merged while the union is convex (exact orientation, or the
   shared kernel line of both edges). This is what keeps the intermediate map at the size of the envelope.
4. Simplify, realization and the exact checks run on the final map unchanged.

## Complexity and why the faces stay few

The lower envelope of n piecewise-linear surfaces of bounded description has complexity `O(n^2 alpha(n))` in the worst case (Edelsbrunner, Guibas, Sharir
1989/1990; Sharir and Agarwal 1995); the slope cones are the benign case (a weighted Voronoi-like diagram, Aurenhammer and Edelsbrunner 1984: `O(n)` for
cones of equal slope; the same order for the piecewise cones here, as the final 762 faces of 48 pieces show). In the incremental construction the size
at step k is the arrangement of all cuts made so far (it never forgets a plane that lost). In divide and conquer a merge of two envelopes of complexity
`m` creates at most `O(m + c)` faces (`c` crossings of the two diagrams, linear in practice), and compaction drops what the new minimum has made
uniform, so the size at every level is that of the envelope of the pieces below it. The work is `O(m)` carve operations per merge, `log n` levels:
`O(n log n)` carves of size about the final map instead of about 190k `insert_into` on a map of up to 134k faces.

## Exactness

No new predicate: `Side`, `Crossing`, `Orient`, `PlaneLine`, `Rounded` of `ExactSlopeKernel` on the same planes and lines. The labels are exact. The
envelope as a function is the same as today's (the minimum is associative and commutative; ties by plane id). What can differ is the **vertex set and
the face history**: the output of Simplify depends on the labels only, except where close vertices are merged (`merge_close_positions`,
`MergedPositions`), which sees vertices of planes that lost (found 2026-11-10: `Merge_RaisesTheToeLowering`, 80 instead of 81 triangles when ghost
vertices disappear). So near-degenerate designs can change: every difference is classified in the report, the default path is untouched.

## References

* H. Edelsbrunner, L. J. Guibas, M. Sharir, *The upper envelope of piecewise linear functions: algorithms and applications*, Discrete Comput. Geom. 4
  (1989) 311-336; and *The complexity of many cells in arrangements of planes and related problems*, Discrete Comput. Geom. 5 (1990) 197-216:
  divide and conquer by merging envelopes through their overlay.
* M. Sharir, P. K. Agarwal, *Davenport-Schinzel Sequences and Their Geometric Applications*, Cambridge University Press 1995 (lower envelopes of
  bivariate functions).
* F. Aurenhammer, H. Edelsbrunner, *An optimal algorithm for constructing the weighted Voronoi diagram in the plane*, Pattern Recognition 17 (1984) 251-257
  (cones as envelopes; already cited for the slope cones).
* The current construction and its references: [TERRAIN_1C_PLAN.md](TERRAIN_1C_PLAN.md) section 9, [TERRAIN.md](TERRAIN.md) (cone envelope), SOURCES.md.
