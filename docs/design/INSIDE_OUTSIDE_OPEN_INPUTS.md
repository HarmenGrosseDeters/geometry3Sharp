# Inside / outside for open and broken inputs (ROADMAP Phase 5 #5): upper bound, literature, sketch

Owner decision 2026-11-02: report only, nothing built. The owner decides after this note.

## 1. Upper bound: what hinges on the inside / outside decision

Data: `MeshRobustRepair` on the 400 set and the holdout (798 meshes), master fe46f5e. The settings were
`BENCH_FIDELITY=0` and the wrap quality pass off; neither changes the path a mesh takes. There were no timeouts. 424
of the 798 inputs are open (they have boundary edges).

| path | meshes | of them open | median time | total time | share of the time |
|---|---:|---:|---:|---:|---:|
| repair (the input repair is clean) | 348 | 72 | 0.9 s | 473 s | 10 % |
| exact resolve | 339 | 270 | 3.7 s | 1614 s | 35 % |
| solidify fallback | 77 | 54 | 7.0 s | 785 s | 17 % |
| alpha wrap fallback | 34 | 28 | 42.4 s | 1686 s | 37 % |

All 798 results are clean. Of the 111 meshes that leave the exact resolve for a fallback, the reason
(`ExactResolveReason`) is:

| reason | solidify | wrap | open inputs |
|---|---:|---:|---:|
| the resolve's result is closed, but differs in volume from the **input's winding-number solid** ("volume differs from the input; input: not closed (winding number)") | 45 | 21 | 63 of 66 |
| the resolve's result itself is **not winding-closed** ("not closed (winding number)") | 22 | 7 | 6 of 29 |
| other reasons with an open input (a failed resolve, degenerate triangles) | 2 | 3 | 5 |
| degenerate triangles, too many self-intersections, volume (closed input) | 8 | 3 | 8 |

- **The bound.** About 100 of the 111 fallbacks (90 %) come down to the inside / outside decision.
  - In the 66 cases of the first row, the input is open. The volume check compares the resolve with the input's
    solid (generalized winding number > 0.5), and for an open input that solid is not well-defined, so the check
    rejects results that may be fine.
  - In the 29 cases of the second row, the winding numbers of the resolved arrangement do not give a closed surface.
    23 of these inputs are closed but self-intersecting, with inconsistent orientation or nesting.
  - These fallbacks take 54 % of the total repair time.
- **What the wrap fallback costs** (the 34 cases, paired runs, quality pass off):
  - volume IoU against the input: median 0.77 (p10 0.07);
  - one-sided Hausdorff result → input: median 2.4 % of the diagonal (p90 6.4 %), against a median of 0.56 % for the
    exact resolve (benchmark README, "exact resolve keeps the geometry");
  - normal deviation: median 2.4°;
  - time: median 42 s, 11 times the exact resolve.
- **The solidify fallback** keeps the geometry better, but it approximates the surface by contouring. Its Hausdorff
  median is 0.48 % on its own cases (benchmark README).
- **Self-unions** of `ExactMeshBoolean` ([SAFE_ROUNDING.md](SAFE_ROUNDING.md) §8.4, the 400 set): 124 of the 212
  results that are not clean are open inputs: "not closed" alone 32, with self-intersections 82, with bowties 9, with
  an inside-out component 1. Their large Hausdorff distances to the exact result come in part from the parts of open
  inputs that the union discards: the same decision, inside the Boolean.

  The brief mentions "9 open inputs where snap rounding B failed the Hausdorff bound". In §8.4 I can identify the 9
  open inputs with bowties, and the open inputs among the 38 rounding failures (32). I list both and make no claim
  beyond §8.4.

A better decision on these inputs could move up to about 100 meshes from the fallbacks to the exact resolve, which
keeps the input surface. The time saved is at most about half of the repair time.

## 2. Literature

| work | what it gives | applicable here |
|---|---|---|
| Jacobson, Kavan, Sorkine-Hornung, *Robust Inside-Outside Segmentation using Generalized Winding Numbers*, ACM TOG 2013 | the generalized winding number (GWN) as a smooth inside / outside measure for open, non-manifold, self-intersecting meshes; graph cut segmentation of a tet mesh | in use already (volume IoU, `FastWindingNumber`); the segmentation step is the missing part |
| Barill, Dickson, Schmidt, Levin, Jacobson, *Fast Winding Numbers for Soups and Clouds*, ACM TOG 2018 | hierarchical, approximate GWN evaluation | in use (`DMeshAABBTree3.FastWindingNumber`); approximate, not suited to an exact decision |
| Feng, Gillespie, Crane, *Winding Numbers on Discrete Surfaces*, ACM TOG 2023 | winding numbers of curves on surfaces (jump Laplace equation), with broken and non-bounding curves fixed | a model (integer jumps across a cut, the fix for non-bounding parts); for curves on surfaces, not surfaces in space |
| Feng, Crane, *A Heat Method for Generalized Signed Distance*, ACM TOG 2024 | signed distance to broken geometry (holes, self-intersections, noise, non-orientable) by diffusing normals, then a Poisson solve; on tet meshes, grids, point clouds | yes, as a robust sign for the volume check and for labelling cells; needs a 3D volume discretization and a sparse solve |
| Martens, Trettner, Bessmeltsev, *The Antipodal Method: Fast, Accurate, and Robust 3D Generalized Winding Numbers*, arXiv 2605.01536 (May 2026) | the GWN as a signed ray-crossing count plus a boundary integral; exact for meshes, fast | the closest fit for an exact or certified per-cell decision (the ray count is exact; only the boundary term is real-valued) |
| *Fast and Exact Winding Numbers for Triangle Meshes*, ACM TOG (DOI 10.1145/3811339; year and authors to be confirmed: doi.org is blocked in this session) | the same decomposition: one ray-mesh test plus a sum over boundary edges | the same use; read the full text before relying on it |
| Xu, Barbič, *Signed Distance Fields for Polygon Soup Meshes*, GI 2014 | signed distance for soups via an offset surface | older alternative, close to what the wrap fallback already does |
| Spainhour, Weiss and co-authors 2024 / 2026 | GWN for parametric (rational) curves and surfaces | not for triangle meshes |

Licenses (AGENT_WORKFLOW §3):
- **geometry-central** (with the signed heat solver): MIT. It may be read and ported, with a SOURCES entry.
- **libigl**: MPL-2.0. Do not port.
- **The authors' signed-heat-3d** and the **antipodal code** (github.com/MartensCedric/antipodal): check the license
  before reading.
- **The papers**: implement from them.

## 3. What the library has, what is missing

Has:
- `DMesh3.WindingNumber` (exact sum of solid angles, O(n) per query) and `DMeshAABBTree3.FastWindingNumber` (Barill
  et al., approximate).
- `WindingNumberOctree` / `MeshWindingNumberSolidify` (propagated node values, deterministic) and
  `MeshWindingNumberGrid`.
- `ExactMeshBoolean`: the exact arrangement, the classification of cells by exact integer winding numbers, and
  `IsWindingClosed`, the edge balance test that fails for open inputs.
- `MeshSignedDistanceGrid`, `CachingMeshSDF`.
- `VectorHeatMethod`, `MeshGeodesicDistance` (heat method on surfaces), `IntrinsicTriangulation`.
- `SparseSymmetricCG`, `SparseCholesky`.

Missing:
- **A certified GWN decision.** The GWN of a point compared with ½ is decided in floating point today. An exact ray
  count plus a boundary term with an error bound (antipodal / ray decomposition) would decide most cells exactly, with
  a rule for the ambiguous ones.
- **A cell labelling for open operands in `ExactMeshBoolean`.** Today the exact winding needs winding-closed input.
- **A 3D volume Laplacian / heat solver** (tet or grid) for the signed heat method. The library's heat methods work on
  surfaces only.

## 4. Sketch: where it would plug in

1. **The volume check of `MeshRobustRepair` (66 cases).**
   - Today the resolve is compared with the input's GWN > 0.5 solid, by sampling.
   - Change: for an open input, use the same labelling as the resolve's cells (see 2), so the check compares like with
     like.
   - Alternatively, compare with the input's signed-heat solid, if 2 is not built.
   - This is the cheapest step: it changes only which reference the check uses.
2. **`ExactMeshBoolean` with open operands (29 cases plus the self-unions).**
   - Label each cell of the exact arrangement by the GWN of the input at a point inside the cell, compared with ½.
     Inputs whose integer winding is consistent keep their labels.
   - A surface between cells labelled in and out is closed by construction, so the result is closed and the
     resolve's "not winding-closed" exit goes away.
   - **Decision:** exact ray parity plus a boundary term with an error bound. If the bound does not decide a cell,
     use a second rule: the label of the neighbouring cell across the face with the largest area, or the signed heat
     sign.
   - **Exact code** (CLAUDE.md): `mesh_ops/ExactMesh*` bans FMA, `float` and parallel loops. The GWN evaluation that
     the labelling uses must follow that, or live outside the exact files as a pure input to them.
3. **The 23 closed self-intersecting inputs** (row 2) are covered by the same cell labelling, since GWN handles
   nesting and orientation issues.
4. **The signed heat method** only if 1-2 leave many cases. It needs a volume mesh or grid and a Poisson solve, and
   it would be a larger, separate piece.

Determinism and invariance: the cell labelling must use a fixed summation order (serial) and only relative
tolerances. Bit-identical under 2^k scales and the existing invariance tests apply.

## 5. Stop rule (proposal)

- First an upper-bound spike in scratch (not merged): label the cells of the resolve by the floating-point GWN at a
  cell point, and measure on the 400 set and the holdout how many of the about 100 cases go from a fallback to a clean
  exact result.
- **Go** if at least 50 of them do, with fidelity at least that of the exact path (Hausdorff < 1 %, IoU > 0.99 against
  the input's GWN solid) and no regression on the meshes that are resolved today.
- **Stop** if fewer than 25 do, or if the ambiguous cells (GWN within the error bound of ½) are more than a few
  percent of the cells on these meshes. Then the certified decision is not cheap, and the signed heat method becomes
  the question, as its own item.
- Step 1 (the volume check reference) can be measured on its own first, as the cheapest part of the bound.
