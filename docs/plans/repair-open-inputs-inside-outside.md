# Plan: open inputs — inside / outside across patches (owner decision)

- **Board row:** OPEN_TOPICS.md §2 "Waiting for an owner decision", row "Rounding/repair: open inputs, inside / outside across patches"
- **Kind:** owner decision
- **Tier:** M (opt-in label source for `MeshWindingNumberSolidify` / wrap) if built; S to close as no-go
- **Plan status:** draft, 2026-11-20 (Berlin). Plan only.

## Current state

Study and prototype complete in [INSIDE_OUTSIDE_OPEN_INPUTS.md](../design/INSIDE_OUTSIDE_OPEN_INPUTS.md) (§§1-7,
stopped #331):

- On 80 open Thingi10K meshes: `ExactResolve` handles 59, `Repair` 9, `Solidify` 11, `AlphaWrap` 1 (12 fallbacks = 15 %
  of meshes, 38 % of repair time). In a random 120-mesh Thingi sample: 13 open meshes, 1 fallback.
- Procedural truth benchmark (§7.3, volume IoU vs true solid):
  - Generalized Winding Number (GWN, current): 0.975-0.996 up to 0.6 rad cap; drops to 0.870 (cap 1.0), 0.635 (cap 1.4),
    0.628 (two caps 1.0), 0.071 (8 holes 0.6), 0.028 (cap 1.0 with 30 % flipped triangles).
  - Signed heat method prototype (64³ matrix-free CG, 0.7-1.2 s/mesh): recovers big holes (0.635 -> 0.732, two caps
    0.628 -> 0.818, 8 holes 0.071 -> 0.910) and flipped normals (0.028 -> 0.887), **but loses** on overlapping/nested
    closed patches (0.998 -> 0.744, binary-only) and thin solids (closed torus 0.980 -> 0.938).
- Exact resolve remains blocked by `ConflictEdges` regardless of the classifier (§6).
- Study conclusion (§7.4): **leaning no-go** unless the owner names a concrete use case (large-hole scans with mostly
  consistent normals).

## Trigger / decision needed

Owner chooses between:

| Option | Effect | Cost / risk |
|---|---|---|
| A. **Close as no-go (leaning)** | Keep GWN + exact winding; no new classifier; document large-hole limit | Zero code / maintenance cost; large-hole open scans (> 1 rad) stay on GWN / alpha wrap |
| B. **Opt-in signed-heat label source on `MeshWindingNumberSolidify`** | Matrix-free CG on a 64³ grid, opt-in `LabelSource = WindingNumber | SignedHeat`, default unchanged | ~1 M PR; adds a second classifier that fails on nested/overlapping meshes and thin walls, needs similarity/determinism tests |
| C. **Exact-ray + bounded-boundary Antipodal / Xie-Hafner-Wojtan GWN** | Exact ray parity with our `Orient3d` + certified boundary solid-angle bound; faster / tighter GWN, not a large-hole fix | Tier L research; does not solve the large-hole ambiguity itself (still GWN) |

How others solve it: Jacobson, Kavan, Sorkine-Hornung (ACM TOG 2013) use GWN + graph-cut segmentation on a tet mesh;
Barill et al. (ACM TOG 2018) fast hierarchical GWN (already in geometry3Sharp); Feng & Crane (ACM TOG 2024, "A Heat
Method for Generalized Signed Distance") and Feng (CMU PhD thesis 2026, MIT code) use heat diffusion of normals + Poisson
solve for large holes and flipped normals while explicitly noting GWN is superior for nested/multi-region inputs;
Martens, Trettner, Bessmeltsev (ACM TOG 2026, Antipodal Method) and Xie, Hafner, Wojtan (ACM TOG 2026) compute exact GWN
via ray parity + boundary cone solid angles (code of the latter is all-rights-reserved — never read).

## Goal

Record the owner's decision (no-go vs opt-in large-hole label source), and if go, expose a deterministic opt-in
signed-heat label source for `MeshWindingNumberSolidify` without changing default outputs.

## Scope

- Option A: `docs/OPEN_TOPICS.md`, `docs/design/INSIDE_OUTSIDE_OPEN_INPUTS.md`, `docs/roadmap/alpha-wrap-and-repair.md`.
- Option B: new `mesh_ops/SignedHeatField.cs` (matrix-free CG on regular grid), opt-in hook in
  `mesh_ops/MeshWindingNumberSolidify.cs`, tests `tests/geometry3Sharp.Tests/SignedHeatSolidifyTests.cs`, docs in
  `docs/OPERATIONS.md` and `docs/SOURCES.md`.

## Non-goals

Never replace GWN as default; never use signed heat in `ExactMeshBoolean` or as the volume-IoU reference; never port
non-permissive code (never read `boundaryblaze`).

## Interface

Option B only: `public enum SolidifyLabelSource { WindingNumber, SignedHeat }` and
`MeshWindingNumberSolidify.LabelSource` (default `WindingNumber`), `SignedHeatGridResolution` (default 64).

## Look before you build

1. Library: `DMeshAABBTree3.FastWindingNumber`, `MeshWindingNumberSolidify`, `MeshGeodesicDistance` (heat method in 2D
   surface form), `MarchingCubes`.
2. Literature: Feng & Crane (2024), Feng (2026 thesis, MIT), Jacobson et al. (2013), Barill et al. (2018).

## Steps (one PR, clean commits)

1. Owner decides A vs B (or names a use case).
2. If A: close row and record decision with date in `INSIDE_OUTSIDE_OPEN_INPUTS.md` and `OPEN_TOPICS.md`.
3. If B: port prototype's matrix-free 7-point CG heat + Poisson solver cleanly (no `SparseCholesky`), wire opt-in into
   `MeshWindingNumberSolidify`, add invariance (`Similarity.All()`), determinism (bit-identical across runs), reuse,
   and procedural truth tests (8 holes, two caps, 30 % flipped normals).

## Acceptance

- Option A: docs diff reviewed.
- Option B: default `LabelSource == WindingNumber` bit-identical to master; procedural truth IoU >= 0.85 on 8-holes-0.6
  and cap-1.0-30%-flipped; every test <= 5 s; closed, manifold, finite outputs checked; `benchmarks/MeshRepairBenchmark`
  before/after table in PR.

## Sources and licences

Feng & Crane (TOG 2024) paper / Feng thesis (MIT); never read `boundaryblaze` (all rights reserved). Cite in
`docs/sources/meshing-and-repair.md`.

## Budget and stop rule

Option A: 5 min. Option B: one M session; stop if similarity invariance under rotated/UTM frames fails without a local
frame (reuse `LocalFrame` if needed) or if CG takes > 2 s at 64³.

## Risks and open questions

- Large holes remain fundamentally ambiguous (Feng 2026 thesis): the option's XML doc must state explicitly that it
  assumes consistently oriented normals and single-shell (non-nested) inputs.
