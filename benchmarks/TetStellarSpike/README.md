# TetStellarSpike (throwaway branch runs/tet-stellar, never merged)

Measurement (owner 2026-11-18): which operations of B. M. Klingner, J. R. Shewchuk, *Aggressive tetrahedral mesh improvement*,
IMR 2007, that `TetMeshImprover` does not have would raise tet quality beyond the 10 degree floor, and at what cost. Paper only
(Klingner's thesis, people.eecs.berkeley.edu/~jrs/stellar/KlingnerPhD_small.pdf, chapters 3-5, for the operations and its
ablation table 5.12); no Stellar code read.

- `Imp2.cs`: a copy of `mesh_ops/TetMeshImprover.cs` (class `Imp2`) with the prototypes behind flags. With no flag it is master's
  improver (checked: same tets, same worst angle on the procedural set).
  - `p1` nonsmooth smoothing everywhere: the active-set direction (least-norm point of the hull of the near-worst gradients, as the
    rescue phase) in all passes and for boundary vertices (with the unchanged exact boundary checks)
  - `p2` multi-face removal (inverse of edge removal: the faces between two apexes t, b that form a disc, replaced by the tets around
    the new edge tb; rings up to 8)
  - `p3` cavity insertion of an interior vertex (circumcenter, centroid, face centres; Bowyer-Watson cavity enlarged until
    star-shaped; new vertex smoothed, edge removal around it; rolled back unless the neighbourhood's worst quality rises)
  - `p4` objective on both ends: the quality of a tet is min(smallest dihedral, F + (G - largest dihedral)), so a tet meets the
    target (smallest >= F, largest <= G) exactly when its quality >= F. `rNN` sets the refinement floor of stage 1 (decoupled from F).
- `Bounds.cs`: the upper-bound study (what a perfect single local operation can reach on the final mesh).
- `Proto.cs`, `Program.cs`: drivers (`proto`, `bounds`, `base`). Results in `results/` (`SUMMARY_tables.md`, raw CSVs; meshes are not kept).
- Invariants: every operation keeps the improver's exact checks (positive tets, boundary moves only through `boundary_move_ok`; p2 and
  p3 do not touch the boundary). Re-checked on the 40-mesh subset: valid, boundary without self-intersection, every sampled input vertex
  inside (`G3_CHECK=1`).
