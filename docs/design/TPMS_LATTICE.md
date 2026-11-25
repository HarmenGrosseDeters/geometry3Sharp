# TPMS / lattice structures from implicit functions (Phase 5 item 5)

Status: **approved by the coordinator 2026-11-12 (decisions 1-4 of section 8, as recommended); implemented in the first PR** (owner approved the topic 2026-11-12). Section 4 and 5 describe the implemented behaviour. Tier L (a new module
with a stated guarantee): design note first, then one draft PR with the code, tests and benchmark.

Goal: triply periodic minimal surface (TPMS) lattices for additive manufacturing: gyroid, Schwarz P, Schwarz D,
Neovius, I-WP, Lidinoid; sheet and skeletal (network) variants; cell size, volume fraction or wall thickness,
optionally graded; clipped to a box first, then to the inside of a closed mesh. The output is a closed, manifold,
self-intersection-free mesh. A CLI / MCP command is its own later PR.

## 1. Look before we build

Library (searched 2026-11-12):

- `MarchingCubes` with `RootMode = LinearEdges` ([CONSERVATIVE_MC_AUDIT.md](CONSERVATIVE_MC_AUDIT.md)): linear crossings
  from the original samples, no nudging, no dropped triangles, throws instead of repairing. Streaming slabs,
  `ParallelCompute` is id-identical to serial. This is the contourer.
- `DualContouring` is **not** used: the benchmark of [DC_INTERSECTION_FREE.md](DC_INTERSECTION_FREE.md) section 2 shows
  gyroid sheets at 48 cells across with 1708 non-manifold triangles (20 of 20 runs) and 80 % multi-patch cells; thin
  sheets below ~2 cells are exactly its weak case. Marching cubes with the table of the conservative audit is closed
  and edge-manifold by the table certificate (face choices depend only on the four face signs).
- Implicit operators (`implicit/`): `ImplicitIntersection3d` is `max`, so the domain clip is the same operation as
  "infill by intersecting implicitly before marching cubes" (roadmap synergy, cheaper than an exact boolean after).
- Domain mesh: `DMeshAABBTree3` nearest-triangle distance plus the fast winding number for the sign (built: no grid,
  no float SDF); `ExactMeshBoolean` stays the optional exact alternative (section 4).
- Checks: `MeshRobustRepair.NotCleanReason` (closed, manifold, finite, degenerate, orientation, self-intersections) and
  the exact `ExactTriangleIntersection` used by `DualContouring.ResolveSelfIntersections`.

Literature (sources go to `docs/SOURCES.md`; formulas are public mathematics, nothing is ported):

- Schoen 1970 (gyroid), Schwarz 1890 (P, D), Neovius 1883, Lidin & Larsson 1990 (Lidinoid), Gandy, Bardhan, Mackay & Klinowski
  2001 (nodal approximations of P, G, D, I-WP: the cosine / sine sums used here are these approximations).
- Al-Ketan & Abu Al-Rub 2019 (sheet vs skeletal "solid network" lattices, level-set offset `f = c`, grading by a
  position dependent `c`): the standard AM description; how nTop-style tools and the open `MSLattice` generator work.
  That is the approach everybody uses (level set of the nodal approximation on a grid, then marching cubes); we add the
  guarantees.
- Lorensen & Cline 1987; Jacobson, *A Sharp Conservative Offset for Marching Cubes* (arXiv:2609.13430) for the
  enclosure mode.

## 2. API (additive; new file `mesh_generators/TpmsLattice.cs`, namespace `g3`)

```cs
public enum TpmsType   { Gyroid, SchwarzP, SchwarzD, Neovius, IWP, Lidinoid }   // the Lidinoid was added after the first version
public enum TpmsStyle  { Sheet, SkeletalLow, SkeletalHigh }                 // |f|<=t ; f<=c ; f>=-c
public enum TpmsStatus { Ok, InvalidOptions, InvalidDomain, TooLarge, ContouringFailed, NotClean, SelfIntersecting, Cancelled }

public class TpmsLattice
{
    public TpmsType Type; public TpmsStyle Style;
    public double CellSize = 1;                 // period L of the lattice along each axis (> 0)
    public int Divisions = 16;                  // grid cells per period: the MC cube is L / Divisions (>= 4)
    public double VolumeFraction = 0.3;         // target solid fraction of the infinite lattice, in (0, 1)
    public double WallThickness = 0;            // if > 0 it replaces VolumeFraction (first-order map, section 3)
    public Func<Vector3d, double> VolumeFractionAt;   // optional grading (world position -> fraction); null = constant
    public Vector3d Origin; public Matrix3d Rotation = Matrix3d.Identity;   // lattice frame (phase 0 at Origin)
    public AxisAlignedBox3d Box;                // domain in the lattice frame, or ...
    public DMesh3 DomainMesh;                   // ... the inside of a closed mesh (world coordinates); wins over Box
    public bool VerifySelfIntersections = true; // exact check of the result (section 6)
    public bool Enclosure = false;              // conservative mode (section 6)
    public bool ParallelCompute = true; public long MaxCells = 1L << 28; public Func<bool> CancelF;

    public void Compute();                      // reusable: recomputes everything from the options
    public TpmsStatus Status; public string Message;
    public DMesh3 Mesh;                         // null unless Status == Ok
    public double Level;                        // c (or t) found for the volume fraction
    public double RealizedVolumeFraction;       // mesh volume / domain volume (box) or / domain mesh volume
    public double MeanWallThickness;            // 2 V / A (sheet) as a report, not a promise
    public int SelfIntersectionsFound;          // 0, or 1 when the exact check rejected the result (it stops at the first reason)
    public long CellsEvaluated; public int GridShiftsUsed; public string NotCleanReason;   // GridShiftsUsed: section 4
}
```

Notes: all-fields mutable options like `MarchingCubes`/`DualContouring`; no existing type changes, so
`dotnet pack` API validation stays green. The class is a generator, so it is equivariant, not invariant under rotating an input. The lattice frame (`Origin`, `Rotation`) makes the operation equivariant
under `Similarity.All()`: the grid, the box and the field live in the local frame, vertices are mapped out at the end.
For the 2^k scales the local frame sees identical numbers (grid index times `L/Divisions`, `x/L` exact), hence a bit
for bit identical mesh up to the exact scaling; rotated and translated members agree to the rounding of the final map
(tolerance stated in the test, as for the other operations).

## 3. Volume fraction and thickness

Fields (p = 2 pi x / L): gyroid `sin x cos y + sin y cos z + sin z cos x`; P `cos x + cos y + cos z`; D
`sin x sin y sin z + sin x cos y cos z + cos x sin y cos z + cos x cos y sin z`; Neovius
`3(cos x + cos y + cos z) + 4 cos x cos y cos z`; I-WP `2(cos x cos y + cos y cos z + cos z cos x) - (cos 2x + cos 2y + cos 2z)`; Lidinoid
`0.5 (sin 2x cos y sin z + sin 2y cos z sin x + sin 2z cos x sin y) - 0.5 (cos 2x cos 2y + cos 2y cos 2z + cos 2z cos 2x) + 0.15`
(the nodal approximation as given by Al-Ketan & Abu Al-Rub 2019, constant +0.15 included, checked against open-access works that
cite it; like every nodal formula it approximates the minimal surface, and the source does not say that the constant puts the
zero set exactly on it; `f < 0` covers 38.4 % of the cell at the constant 0.15, 50 % needs about 0.006; the formula is invariant under the cyclic permutation of the axes and even under the central inversion; it has no mirror
or axis swap symmetry). The zero set depends on the constant: other implementations write the same terms without the two factors 0.5 and the constant +0.3, which is
the same surface. `f < 0` covers about 38 % of the cell, so the zero set is not volume-balanced and the low and high skeletal styles give
different shapes; the volume fraction comes from the quantile table, not from the constant. Gradient bound for Enclosure: `d/dx = cos 2x cos y sin z + 0.5 sin 2y cos z cos x
- 0.5 sin 2z sin x sin y + sin 2x (cos 2y + cos 2z)`, so `|d/dx| <= 1 + 0.5 + 0.5 + 2 = 4` and `|grad f| <= 4 sqrt 3`; the sum of
the absolute terms is `0.5 * 3 + 0.5 * 3 + 0.15 = 3.15`. A Lidinoid sheet has a large area per period, so it needs
more cubes per period (about 22+) than the other types for the wall to be resolved.

The solid is `S = {f <= c}` (skeletal; `SkeletalHigh` is `{f >= -c}`) or `S = {|f| <= t}` (sheet). Its volume
fraction in the infinite lattice is the distribution function of `f` (or `|f|`) over one period, so the map is a
**sampled quantile**, no fit: sample the periodic field on a cell-centred `N^3` grid of one period (midpoint rule, `N = 64`,
262 144 evaluations, a few ms; cached per type), sort, read the quantile at the target fraction, interpolate linearly
between samples. Tolerance of the map: the sampled fraction differs from the true one by about the share of the cells the
level set cuts, `~ c A/(L N)` with `c` between 1 and sqrt 3 and the area ratio `A/L^2` (gyroid 3.09): a heuristic, not
a bound (the signs of the cell errors do not simply add). The tests state the numbers they measure (0.02 of the
range against a 112^3 table, +-0.007 on 1e5 random points). An exact quantile would need the contour area per level;
not worth it here. The realized fraction of a clipped, discretised result is reported
(`RealizedVolumeFraction`) and tested against the target with a stated tolerance (boundary share + discretisation).

`WallThickness w` is mapped through the area ratio: `fraction = (A/L^2) * (w/L)` (flat-wall, first order; sheets only,
the area ratios are computed once per type by contouring `f = 0` and cached). It is documented as approximate and
`MeanWallThickness = 2V/A` reports what was achieved.

Grading: `VolumeFractionAt(x)` gives a position dependent fraction; the level is looked up in the cached quantile
table (monotone piecewise linear, 1025 entries). Graded cell size (a phase integral) is a non-goal of the first PR.

## 4. Domain clip

Everything is done implicitly before contouring: `F(x) = max(g(x), d(x))`, where `g` is the lattice field
(`f - c`, `|f| - t`, `-f - c`, always scaled by `1 / Lw`, the proven Lipschitz bound, so both terms are in length
units and F is 1-Lipschitz) and `d` the domain signed distance (box: `max(|x - centre| - half)` in the frame; mesh: the
distance to the nearest triangle of the AABB tree with the sign from the fast winding number, evaluated per sample, no
grid; outside the mesh bounds the box distance, a lower bound). The solid is `{F <= 0}`, so the cut faces are part of the
solid and the result is closed.

- The extraction grid is the lattice grid (`h = L / Divisions`, origin on a period boundary), padded by two cells on
  every side so that every boundary sample is high (outside), as `LinearEdges` / the closure argument needs.
- A sample exactly on the iso value breaks `LinearEdges` (it throws), and a sample within rounding of it makes a
  sliver triangle (`NotCleanReason`: degenerate triangles). Cut planes that fall on grid planes (a box `[0, L]^3` does)
  are the common case, so the first grid is the lattice grid moved down by half a cube (fractions 0.5, 0.236, 0.764,
  0.382 of `h` for the four attempts, all inside the pad, defined in the lattice frame, hence the same under
  similarities), and `Compute` moves to the next shift when the contouring throws or the result has degenerate
  triangles only. The shift moves only the grid, not the lattice phase; with `Enclosure` only the dyadic shifts 0.5, 0.25, 0.75, 0.125 are used and a shift whose grid is not exactly representable is skipped (review finding); `ContouringFailed` if all four fail
  (`GridShiftsUsed` reports the attempt). Found by the tests: Neovius with a box on grid planes gave a sliver at a box
  edge on the unshifted grid.
- **Sliver guard** (bugfix after #750, found by a large graded lattice): with a level that varies continuously (`VolumeFractionAt`, a
  graded cell size) the node values are generic, and on a lattice of a few hundred thousand nodes a few lie within rounding of the
  level; their crossings sit at cube corners and give degenerate triangles that no grid shift avoids (a constant lattice on its own
  grid has a small discrete set of node values and was not hit). The field therefore treats a value within `Snap = h / 256 * ratio` of the level as
  inside (moved to `iso - Snap`, `TpmsLattice.SliverGuard`), where `ratio` is the sampled maximum of `|grad f|` over the proven bound (1 for
  the gyroid-like types, about 1/4 for Schwarz D and Neovius, whose bound is loose), so that the band is the same fraction of the change of f
  per edge for every type: every crossing is then at least 2^-8 of an edge from its node and no
  triangle is smaller than about 1e-5 cube^2. The field value of a snapped node moves by less than `2 Snap` and only down: the solid `{F <= iso}` can only grow, in
  every variant (sheet: both walls thicken; skeletal: the solid grows). Where `F` is 1-Lipschitz in length (`(g - c) / Lw` with a constant `c`), the change of F per
  cube edge is at most `h` and every crossing is at least `Snap / h = ratio / 256` of an edge from its node (about 1/1024 of an edge for Schwarz D
  and Neovius). This is a heuristic sizing, not a proof: the clip distance `d` changes by `h` per edge, and a position dependent level
  (`VolumeFractionAt`, the motivating case) makes `F` not 1-Lipschitz, so the crossing distance there is smaller; the guard still removes
  the exact-corner crossings, and the result is certified separately (`NotCleanReason`, exact self-intersection check). For `Enclosure` the sample error is one-sided and below
  `2 Snap` in field units: the level pad pays `2 Snap` (the audit's `e` term) on top of `sqrt(3) h / 2`. On the constant lattices of the tests (2 periods, 14 cubes per period, fraction 0.3; not a guarantee for other
  fractions, resolutions or box offsets) 15 of 18 type / style cases are bit for bit unchanged; Neovius skeletal and Lidinoid skeletal high change by less than 12 % of a cube (measured: 5 % and 9 %; where `f` is nearly flat a snapped node moves the surface by the guard band over the local gradient) and 2 % of the volume. An on-level sample (a box plane on a node) is now snapped instead of throwing, so the grid
  retries are rare and remain for what the guard cannot see.
- Cut edges are rounded by at most one cube (MC does not keep a sharp box edge). Documented, measured, not hidden.
  `DomainMesh` must be closed and manifold (`InvalidDomain` otherwise: an SDF sign of an open mesh is wrong).
- Alternative for an exact cut: the user builds the lattice on a bounding box and intersects with
  `ExactMeshBoolean`; cost is `O(T log T)` exact work on millions of triangles. Not wrapped in the first PR; a test
  shows both agree in volume within the rounding of the cut.

## 5. Guarantees (what we state, and what each rests on)

1. **Structural, checked at runtime on every result** (`Status == Ok` only then): closed, edge-manifold, finite,
   consistently oriented, no degenerate triangles (`NotCleanReason == ""` without the self-intersection part), and
   `SelfIntersectionsFound == 0` from the **exact** check on the double coordinates actually returned
   (`ExactTriangleIntersection` over an AABB tree, parallel) when `VerifySelfIntersections` is true. So the guarantee
   is a certificate of this output, not an argument about the algorithm. If the check fails we return
   `SelfIntersecting` and the mesh is not exposed; it is not repaired (no heuristic vertex moving).
   The certificate runs after the transform to world coordinates (`Origin + Rotation * v`), once per grid attempt (#1569:
   before, it ran on the lattice-frame mesh, and at an `Origin` near `1e13` the rounding of the transform made a mesh that
   passed it degenerate and self-intersecting while `Status` was `Ok`). An a-priori bound of the rounding (about an ulp of
   the world coordinates per vertex) would need the smallest separation of non-adjacent triangles to decide, which the exact
   check does not compute, so the world mesh is checked directly; the cost is unchanged.
   Reason for a runtime certificate: the table argument is exact-arithmetic; rounded crossings that nearly coincide
   can in theory break embeddedness and we do not claim to have excluded that.
2. **Periodicity and symmetry** hold for the field exactly in the mathematical sense and to evaluation rounding
   (1e-15 relative) for the mesh: tests check a translation by one period and the cubic symmetries of each type
   (the point group differs per type; the gyroid is chiral, so only its proper symmetries are tested).
3. **Enclosure (opt-in, `Enclosure = true`)**: with `Lf` a proved bound of `|grad f|` per type (triangle inequality on
   the closed forms, e.g. gyroid `2 sqrt 3`), the field `(f - c) / Lf` is 1-Lipschitz; contouring it with the recipe of
   CONSERVATIVE_MC_AUDIT (`LinearEdges`, level `sqrt(3) h / 2 + 2^-26` in normalised units, high boundary, dyadic
   grid) gives a mesh whose solid **contains** `{f <= c}` of the exact lattice: the printed wall is never thinner than
   designed. Price: the solid is larger (the offset is `~ sqrt 3 h / 2` in length), so this is a minimum-feature
   mode, not the volume-fraction mode; the realized fraction is reported. Preconditions of the recipe (exact grid,
   `0 < h <= 1` after normalising by the box size, finite samples) are checked and `InvalidOptions` otherwise.
   We do **not** claim a topology guarantee for the non-enclosure mode: a wall thinner than about one cube can
   disconnect or vanish (marching cubes samples it); the result is still a clean closed mesh. Rule of thumb printed
   in the docs: `wall >= 2 h`, i.e. `Divisions >= 2 * L / wall`.

## 6. Resolution and cost bound

- Samples: `((Box extent / h) + 5)^3`, checked against `MaxCells` before any work (`TooLarge`, never an allocation
  failure). Memory is bounded by the streaming slab evaluation plus the output mesh; `Divisions = 16` gives about
  `1.9 * A * Divisions^2 ~ 1.5e3` triangles per period for a skeletal lattice and twice that for a sheet (two sides).
- Time: field evaluation `O(samples)` (3 sin + 3 cos per sample, ~0.1 us), contouring `O(samples)`, the exact
  self-intersection check `O(T log T)` (candidate pairs from the AABB tree; for a manifold, locally flat lattice the
  candidate set is `O(T)`). The benchmark (`benchmarks/TpmsLattice`) reports samples, triangles, seconds per stage and
  the check's share, for 4^3 to 12^3 periods; the check is the stage most likely to dominate and may default to
  "on up to N triangles" if the numbers say so (decided with numbers, not here).
- Parallelism: only the contouring's slab evaluation (`ParallelCompute`, id-identical to serial by the existing
  contract) and the check; both covered by parallel-equals-serial and repeated-run tests. The field evaluation of the
  quantile table is serial and deterministic.

## 7. Tests (each with a mutation that must fail it)

- Clean: `NotCleanReason == ""` and `Status == Ok` for every type x style x {box, mesh domain}, several `Divisions`.
- Volume fraction: realized vs target within a tolerance computed from boundary share and `Divisions`; a mutated
  quantile (off by one index) must fail it; the quantile table against an independent Monte-Carlo-free oracle (a
  finer `N = 128` grid) within the stated bound.
- Periodicity and symmetry: shifting the box by one period gives the same mesh (vertex multiset, tolerance 1e-9 L);
  the field is invariant under the per-type symmetry (axis permutations / reflections that the type has).
- Independent oracle: sample points; `inside(mesh)` by generalized winding number (`MeshWindingNumber`, shares no code
  with the contourer) must equal the sign of `F` away from the surface by more than the stated error (`h`).
- `Similarity.All()` invariance (bit for bit at the 2^k scales), reuse (Compute twice, then change options), determinism
  (`ParallelCompute` true equals false, repeated runs), enclosure (sampled `f <= c` points are inside the mesh;
  a mutation that drops the level pad must fail it), `TooLarge` / `InvalidOptions` / open-domain statuses, the
  retry on a grid-aligned box.
- Benchmark `benchmarks/TpmsLattice` with the numbers in its README and the PR; `ReaderFuzzTests` is not touched (no
  reader).

## 8. Decisions needed before building

1. Marching cubes (not dual contouring) as the contourer; sharp cut edges are not provided (rounded by one cube).
   Recommended; DC would need the unfixed gyroid non-manifold cases.
2. `Enclosure` in the first PR (a minimum-feature mode) or as a follow-up. Recommended: first PR, it is the reason the
   `LinearEdges` work exists, and it is a small addition once the field is Lipschitz-normalised.
3. `WallThickness` as a first-order convenience in the first PR (it is approximate). Recommended: yes, with the
   reported `MeanWallThickness`.
4. Lidinoid and graded cell size: not in the first PR (the Lidinoid was added afterwards; graded cell size follows).

## 9. Hybrid of two types, in-plane (owner request 2026-11-15; built, see 9.7)

Question: can `TpmsLattice` and `g3cli tpms` blend two lattices, eg Gyroid and I-WP, `w(x) f_G + (1 - w(x)) f_IWP`, and what would
it take. Sections 9.1 to 9.6 are the design as approved; 9.7 records the owner decisions, what was built and the corrections found while building.

### 9.1 What the paper says (read from its open-access text)

Source: Fang, Yan, Xie, Wang, Ke, Zhang, Li, "In-plane hybrid design and mechanical properties of Ti-6Al-4V TPMS structures",
Sci. Rep. 16, 17947 (2026), DOI 10.1038/s41598-026-49318-0, CC BY 4.0. The formulas below are quoted from the article text;
the equation numbers are the paper's.

- Uniform parents (2), (3), with `X = 2 pi x` etc. (one period per unit length, the same for both): gyroid `sin X cos Y + sin Y cos Z + sin Z cos X = C`,
  I-WP `2[cos X cos Y + cos Y cos Z + cos Z cos X] - [cos 2X + cos 2Y + cos 2Z] = C`. `C` shifts the level set and sets the relative
  density; `C = 0` is the standard shell. These are the same nodal forms as section 3.
- Connection (4), (5), for `m` cells in a row along x and then along y:
  `gamma_n = (1 - A_1)(phi_1 - phi_2) + (1 - A_2)(phi_2 - phi_3) + ... + (1 - A_{m-1}) phi_{m-1} + A_{m-1} phi_m` (x),
  `f = gamma_1 B_1 + gamma_2 B_1 B_2 + gamma_3 B_2 B_3 + ... + gamma_{m-1} B_{m-2} B_{m-1} + gamma_m B_{m-1}` (y), with the `phi_i` the
  parent fields of the cells in order. For two cells (`m = 2`) the x form reads (my reading of the printed sum, not verified for larger
  `m`) `(1 - A_1) phi_1 + A_1 phi_2`: a convex blend of the two parent fields. The printed sums are not reproduced here beyond this
  case; the build needs only the two-parent blend with a weight, which is what they reduce to.
- Weight (6): the sigmoid `alpha(x, y, z) = 1 / (1 + exp(-k eta(x, y, z)))`, range (0, 1), `eta = 0` the midpoint of the transition, `k` the steepness.
  They compare `k = 0.5, 1, 5` (Fig. 1b, c): `k = 1` gives the smoothest interface and the lowest stress concentration (the stress claim is
  from their FE runs, not ours); `k = 5` is abrupt, `k = 0.5` distorts the parent cells over a wide zone. The text does not define
  `eta` (a coordinate or signed distance to the interface plane, presumably in units of the cell) and does not state the unit of `k`;
  the table of specimen sizes is an image that the text extraction does not contain.
- Layout: two hybrids on a 4 x 4 x 4 cell array, G-IWP-1 (gyroid and I-WP side by side along x, the same in every layer) and
  G-IWP-2 (the order reversed in the next layer: a checkerboard); the thickness (relative density) is uniform, relative density gradients
  are named as future work.
- Continuity: the paper says "seamless", "geometric continuity", and does not name a class. From the construction: a convex
  combination of two analytic fields with an analytic weight is analytic (`C^infinity`), and the weight is never exactly 0 or 1, so
  there is no compact transition zone (each parent is only recovered up to `exp(-k |eta|)`); a hard switch (`max`, a
  step) would be `C^0` only. The mechanical results and the "no abrupt change" argument are theirs; the geometric properties above
  are consequences of the formulas, not claims of the paper.
- Not read: Ren et al. 2021 (Mater. Des. 210, 110062, transition boundaries of multi-TPMS lattices) and Gao et al. 2022
  (Mater. Des. 221, 110995, configuration-varying TPMS with geometric continuity): both are Elsevier articles that the session's
  allowed hosts do not serve. Nothing here is taken from them; if the owner wants the comparison (a transition that keeps the
  wall thickness or the level constant through the zone), they need to be read before the build.

### 9.2 Fit into `TpmsLattice`

Blend the **style fields, each with its own level**, not the raw `f`: with `g_X = style value of type X (f, |f| or -f)` and `c_X` its
level for the target fraction from the existing per-type quantile table (section 3),

    h(p) = w(p) (g_A(p) - c_A) + (1 - w(p)) (g_B(p) - c_B),   solid S = {h <= 0}.

In the zones where `w` is near 1 or 0 the solid is the pure A or B lattice with exactly the requested volume fraction, so the
per-type table stays the only table (no table for the pair, and no `VolumeFractionAt` per position needed). Blending the raw `f`
with one level would give neither parent its fraction (the level sets of `f_G` and `f_IWP` are not balanced the same way). In the
transition the fraction is whatever the blend gives; it is reported (`RealizedVolumeFraction`), not promised. Sheets:
`g = |f|` and `c = t`, the blend of two non-smooth but 1-Lipschitz terms is still Lipschitz.

API (additive, one new member group; nothing changes when unset; as built: `SecondType` and `Blend` of the class `TpmsBlendWeight`, see 9.7):

- `TpmsType? SecondType` and `Func<Vector3d, double> BlendWeight` (local frame, values in [0, 1], 1 selects `Type`) with a
  library-derived bound `BlendGradientBound` (as `LinearGrade` does for the phase map: a delegate without a bound is allowed only
  without `Enclosure`). Helpers `SigmoidBlend(axis or normal, offset, width)` (the weight of (6) with `eta` the signed distance
  along the normal in length units and `k = 4 / width`, so the slope at the midpoint is `1 / width` and the 10 % to 90 % zone is `2 ln 9 / k = 1.10 width` wide, `|grad w| <= k / 4 = 1 / width`) and `CheckerBlend(A, B)` (the layout of G-IWP-2: `w = A B + (1 - A)(1 - B)`
  of two sigmoids along x and y, `|grad w| <= sqrt(kx^2 + ky^2) / 4`; this combination is mine, the paper builds the checkerboard with its
  sums (4), (5)). `w` is evaluated in a numerically stable form (`0.5 (1 + tanh(k eta / 2))`; the printed `1 / (1 + exp(-k eta))` overflows for large `-k eta`).
- Both types share `CellSize` and `Rotation` (the paper does the same); different periods per type are a non-goal (the phase map is
  per field and the transition would have no meaning). A `Grade` (graded cell size) applies to both fields at once: allowed, the
  Jacobian bound enters the bound below unchanged.
- `Compute()`: the quantile levels `c_A, c_B` and the area ratios come from the existing caches (per type); the field is
  `F = max(h / Lh, d)` with `d` the domain distance as today.

**Lipschitz bound for `Enclosure`** (the one new thing to prove). With `M_X = sum of the absolute values of the terms` of `f_X` (the
code has it for the rounding error: gyroid 3, I-WP 9, so `|g_X - c_X| <= M_X + |c_X|`), phase scale `2 pi / L`, and `G_X` the
gradient bound of section 5 (gyroid `2 sqrt 3`, I-WP `6 sqrt 3`):

    grad h = w grad g_A + (1 - w) grad g_B + (g_A - c_A - g_B + c_B) grad w,
    Lh = (2 pi / L) max(G_A, G_B) J + (M_A + |c_A| + M_B + |c_B|) * Wmax,

with `J` the Jacobian bound of the grade (1 without) and `Wmax` the bound of `|grad w|`. Gyroid / I-WP at `L = 1`, `k = 1` gives about
`65.3 + 3 = 68` (weight term: `(3 + 9 + |c_A| + |c_B|) * 0.25`, the levels small) versus `65.3` for pure I-WP: the weight term is small unless `k` is large against `2 pi / L`; for a steep weight the bound grows with it (`Lh` is then dominated by the weight term, and the sliver
guard is scaled by the phase part over `Lh`, see 9.7). The rounding error of an evaluation (`EnclosureErrorBlend`, with `u = 2^-53` and `msum >= |g_A - c_A| + |g_B - c_B|`, divided by `Lh s` like the single type term) is
`64 u max(TermSum_A, TermSum_B)` for the two type evaluations (the weights sum to 1), plus `6 u msum` for the six rounding steps of the combination (`g_A - c_A`,
`g_B - c_B`, `w (.)`, `1 - w`, `(1 - w)(.)` and the sum, each at most `u` times a value of at most `msum`), plus `eps_w msum` for the error `dw` of the weight times
`|g_A - c_A - g_B + c_B| <= msum`, plus the phase term of `EnclosureError`. For `SigmoidBlend`, `eps_w` is a relative-error argument: `z = 2 eta / width` with `eta = p - offset` has a relative
error of at most `2 u` (one rounding in the subtraction, one in the division; this also holds for `|offset| > s`, where an absolute argument fails); the propagated error of
`tanh` is `sech^2(z) |z| 2 u <= 0.45 * 2 u = 0.9 u` (`max z sech^2 z = 0.4506`); a `tanh` within 2 ulp adds `2 u`, and `1 + tanh` and the product by 0.5 (exact) add `2 u`,
so `dw <= 0.5 (0.9 u + 2 u + 2 u) = 2.5 u`. The code uses `eps_w = 8 u + 2 u s Wmax`: the `8 u` absorbs a `tanh` with an error of up to about 6 ulp (the platform libm is not specified), and the second term covers a delegate
weight whose coordinate rounding is at most `2 u s`. The recipe needs only the sampled values and the Lipschitz constant of `h / Lh`, not an exactly representable `h`, as for the constant lattice. Enclosure then means: the
solid contains the exact blend `{h <= 0}` of the stated `w`.

### 9.3 Fit into `g3cli tpms`

New options, no default of an existing option changes: `second_type` (same names as `type`; unset = no hybrid), `blend`
`'axis,offset,width'` for the single plane (axis 0..2, local frame) or `blend_xy` `'x_offset,x_width,y_offset,y_width'` for the checkerboard
(the exact spelling is the owner's call: it is new CLI surface). Result fields: `second_type`, the two levels, `blend_gradient_bound`.
`max_cells`, `domain` and `enclosure` behave as today (`enclosure` with a domain mesh stays refused).

### 9.4 Guarantees to keep

Unchanged and from the same mechanism: `Status Ok` only after `MeshRobustRepair.NotCleanReason` and the exact self-intersection
check (the contouring path and the certificate do not know the field), closed, manifold, oriented, finite. Not promised, as for the
constant lattice: topology of walls thinner than about two cubes; additionally the wall thickness inside the transition (the blend of
two fields at different wall thickness is not a wall of either); the resolution rule `Divisions >= 2 L / wall` is the caller's to apply against the thinner of the two
types (the library does not check it for the hybrid, as it does not for the constant lattice). The sliver guard needs the `GradientRatio` of the field: use the larger of the two per-type ratios (conservative: a larger
`Snap` only grows the solid, section 4), sampled ratio of the blend itself is a refinement, not needed. `Enclosure` keeps its
preconditions (dyadic grid, box domain, constant fractions, the bound above).

### 9.5 Tests the build needs

Each with a mutation that must fail it, as in section 7:

1. `w = 1` everywhere reproduces the pure `Type` mesh bit for bit, `w = 0` the pure `SecondType` (kills a swapped weight and a dropped level).
2. A single plane with a wide transition: far from it, the realized fraction of each half is the target of its type (tolerance as in section 7); between
   them the mesh is clean (closed, manifold, `NotCleanReason == null`, no self-intersection) for gyroid / I-WP at several `k` and all three styles.
3. Independent field copy in the test plus a winding-number oracle on points near the interface (as `TpmsLatticeTests` does for the constant lattice).
4. `Similarity.All()` invariance (`Harness/Invariance.cs`): the 2^k scales give the same mesh bit for bit, with `k` and the offsets given in scaled
   length units (the product `k * eta` is then identical), rotation / translation via the frame as for the constant lattice.
5. Reuse (`Compute()` twice on one instance) and determinism (parallel = serial, repeated runs).
6. `Enclosure`: the recipe test (a solid contains the exact blend at the worst places, as `Recipe_LevelEnclosesAnExactSolidAtTheWorstPlaces`)
   for the blend, with a steep `k` so that the weight term of `Lh` matters; a mutation that drops the `Wmax` term must fail it. Refusals: a
   weight delegate without a bound, `Enclosure` with a domain mesh.
7. Stable weight: `k eta` of +-800 gives 1 / 0 without NaN (kills the printed form).
8. CLI: option mapping against the library called directly (as `TpmsCliTests`), errors write nothing, MCP argument types.
9. Benchmark row in `benchmarks/TpmsLattice` (two types evaluate both fields: expect about twice the field time of one type, the domain distance and the certificate unchanged).

### 9.6 Effort and risks

One PR, library about 250 lines (weight helpers, blend in the field, the bound and the `EnclosureError` term), CLI about 100,
tests about 500, design notes and `SOURCES.md` (this paper, CC BY 4.0, the idea only; the sigmoid is textbook). Roughly 1.5 to 2 days of agent
work including the review. Risks: (a) the volume fraction inside the transition is not controlled (reported only); (b) the enclosure
bound is conservative where `k` is large; (c) the paper's `eta` and `k` units are not stated, so our `width` parametrisation is ours and
should not be presented as the paper's `k`; (d) Ren / Gao may offer a transition that keeps the wall thickness, which this simple
blend does not: a possible second step, only after reading them.

Decisions for the owner: the option spelling in 9.3; whether the checkerboard weight ships in the first PR or only the single plane;
whether reading Ren 2021 and Gao 2022 comes first.

### 9.7 Owner decisions (2026-11-15) and what was built

1. Single plane only (`SigmoidBlend`); no `CheckerBlend` and no `blend_xy` in the first PR.
2. CLI names as proposed: `second_type` and `blend 'axis,offset,width'`.
3. Ren 2021 and Gao 2022 are read later, not before the build.

Built as designed in 9.2 to 9.5 (`TpmsLattice.SecondType`, `Blend` (`TpmsBlendWeight`), `SigmoidBlend`, `SecondLevel`, `EnclosureErrorBlend`; `g3cli tpms`
`second_type`, `blend`; `TpmsHybridTests`, `TpmsCliTests`; benchmark rows), with these differences from the text above:

- The stable form `0.5 (1 + tanh(2 eta / width))` is used for exact symmetry `w(eta) + w(-eta) = 1`, not because the printed form fails: in IEEE doubles `1 / (1 + exp(+large))`
  is 0, not NaN (`exp` overflows to infinity), so test 7 pins the saturation, the symmetry and the scale invariance of the weight, and cannot tell the two forms apart.
- A weight delegate with an unknown gradient bound (NaN) leaves the weight term out of `Lh` and is refused with `Enclosure`; a weight outside [0, 1] is clamped (the
  clamp keeps the bound).
- Sliver guard: the larger of the two per-type `GradientRatio`s sizes `Snap` (pinned through the level pad under `Enclosure`).
- Cost: about 1.15 times the constant sheet in `Compute` at 6 periods, the same allocation (benchmark README; before the sliver guard was scaled, below, 2.4 times).
- Not done (as decided): the checkerboard weight and `blend_xy`; a hybrid of different periods; wall thickness control inside the transition.
- Corrections from the independent review of the build: (a) the sliver guard is scaled by `max(G_A, G_B) 2 pi J / Lh` (at most 1): without it a steep weight (width much
  below a cube) made `Snap * Lh` about one field unit and filled the pure halves of the lattice, while a larger guard "only grows the solid" was the (true but too
  weak) argument of 9.4; the `Enclosure` pad shrinks with it too, so "the enclosure offset hardly changes" holds for every width; (b) a gradient bound of the weight that
  is negative or infinite, or whose product with the value range overflows, is `InvalidOptions` for every hybrid (a negative one flipped the sign of the field, an
  infinite one zeroed it and gave a solid box as `Ok`); (c) a weight that returns NaN anywhere gives `ContouringFailed` instead of holes; (d) the enclosure claim "contains the exact
  blend of the stated `w`" holds for a delegate only if its bound and its rounding error (at most `8 u + 2 u s Wmax`) hold, which the library cannot check; the delegate must be pure and thread safe.
- Test limits: the enclosure containment test samples near-surface points of the exact blend; the derived `Lh` (and so a dropped weight term) is pinned by an exact-value test
  and a sampled-gradient bound, not by a worst-case-node construction as for the single point of `Recipe_LevelEnclosesAnExactSolidAtTheWorstPlaces`.

## 10. Thin sheet walls: `TwoLevelSheet` (owner go 2026-11-22; built)

Marching cubes on `F = max((|f| - t) / Lw, d)` sees a wall only where a grid node lies inside it, so a wall thinner than about two
cubes breaks up (section 5, "wall >= 2 h"). The spike [CONTOURING_SDF_SUBGRID_2026-11.md](../research/CONTOURING_SDF_SUBGRID_2026-11.md)
measured the alternative that is built here: the two sides of the wall as the two levels `f = +t` and `f = -t` of the surface function.

- **Option:** `TpmsLattice.TwoLevelSheet` (default false: the output of every existing call is unchanged). Sheet style only; `InvalidOptions`
  with `Enclosure` (the enclosure recipe is a marching cubes recipe) and with a hybrid (`h` of section 9 is not of the form `|g| - w`).
  It works with `WallThickness`, `VolumeFractionAt` (the half width then varies per node), `PhaseMap` / `Grade` and both domains.
- **Contouring:** `TwoLevelMarchingTetrahedra` (`mesh_generators/TwoLevelMarchingTetrahedra.cs`, public) with `g = f / Lw`, `w = t / Lw` and the
  domain distance d, on the same grid, shifts and retries as marching cubes. Each cube is split into five tetrahedra (alternating, so
  neighbours share their face diagonals); `g - w`, `-g - w` and d are linear in a tetrahedron, and the result is the exact boundary of the
  solid where all three are negative: per function the polygon of its zero plane in the tetrahedron, clipped by the other two.
- **Why the two sides cannot meet:** `(g - w) + (-g - w) = -2 w < 0` at the nodes and so in the whole tetrahedron: where one is zero the other
  is negative. The pieces of different tetrahedra have disjoint interiors. So the mesh is free of self-intersections by construction, at
  any wall thickness; the exact check of `VerifySelfIntersections` still runs and is the certificate of the returned coordinates.
- **Consistency across tetrahedra in floating point:** whether a function is negative at a crossing of another one on a grid edge is decided
  by the order of the two crossing parameters on that edge only (with a fixed tie rule), never by evaluating the function at the point.
  Every tetrahedron and polygon at an edge so sees one arrangement, and the two polygons that meet in a face agree on whether the face has
  a vertex of both planes (the test `CoincidentCrossings_WithoutTheRimGuard_StillGiveOneArrangement` and the mutants "order of crossings swapped" and "tie rule ignored" pin it; the
  review's fuzz of 2400 small grids with integer node values, so with many ties, found none open or non-manifold with the guards on).
- **Slivers:** two guards move node values, so the result stays the exact boundary of a piecewise linear solid. `SliverGuard` is the guard
  of section 4 (a value within 1/256 of the mean change per crossing edge counts as inside). `RimGuard` is new: where the cut of the domain
  and a side of the wall cross a grid edge within 1/512 of the edge, the domain value at one end is changed to separate them (best effort, 8 rounds;
  the cut moves by about 1.7 / 512 of a cube at the default; measured on 800
  clipped runs of four shapes, walls of 1.5 to 0.002 cells: 798 clean; without it about 10 % have triangles of almost no area). The two left
  are walls of 0.002 cells, thinner than the guard.
- **What is not claimed:** the topology of the true level sets (the grid must still resolve the surface itself, 8 to 16 cubes per period),
  and nothing about a wall given as a single field that is not `|g| - w`. Measured against marching cubes at 16 divisions, 3 cubes wall:
  volume fraction +2.5 % (marching cubes -2.5 %) against the sampled closed form, converging faster with the divisions.
- **Cost:** about 2.5 times the triangles of marching cubes on the same grid and no field evaluation beyond the nodes; times in
  [benchmarks/TpmsLattice](../../benchmarks/TpmsLattice/README.md).
- **Tests:** `TwoLevelMarchingTetrahedraTests`, `TpmsLatticeTwoLevelTests` (thin walls clean and connected where marching cubes breaks up,
  agreement on thick walls, invariance over `Similarity.All()` with the 2^k scales bit for bit, reuse, parallel equals serial, both guards).
- **Found on the way, not changed:** with a `DomainMesh`, the distance outside the bounds of the mesh is a lower bound (the box distance), so
  next to the bounds the cut lies up to a cube outside the mesh; marching cubes has the same (0.757 against a radius of 0.7 at 10 divisions).
