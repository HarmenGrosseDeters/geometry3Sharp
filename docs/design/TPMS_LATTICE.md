# TPMS / lattice structures from implicit functions (Phase 5 item 5)

Status: **approved by the coordinator 2026-11-12 (decisions 1-4 of section 8, as recommended); implemented in the first PR** (owner approved the topic 2026-11-12). Section 4 and 5 describe the implemented behaviour. Tier L (a new module
with a stated guarantee): design note first, then one draft PR with the code, tests and benchmark.

Goal: triply periodic minimal surface (TPMS) lattices for additive manufacturing: gyroid, Schwarz P, Schwarz D,
Neovius, I-WP (Lidinoid later); sheet and skeletal (network) variants; cell size, volume fraction or wall thickness,
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
public enum TpmsType   { Gyroid, SchwarzP, SchwarzD, Neovius, IWP }       // Lidinoid: same pattern, added if cheap
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
`3(cos x + cos y + cos z) + 4 cos x cos y cos z`; I-WP `2(cos x cos y + cos y cos z + cos z cos x) - (cos 2x + cos 2y + cos 2z)`.

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
  every variant (sheet: both walls thicken; skeletal: the solid grows). `F` is 1-Lipschitz in length (`(g - c) / Lw`), so the change of F per
  cube edge is at most `h` and every crossing is at least `Snap / h` of an edge from its node. For `Enclosure` the sample error is one-sided and below
  `2 Snap` in field units: the level pad pays `2 Snap` (the audit's `e` term) on top of `sqrt(3) h / 2`. On the constant lattices of the tests
  13 of 15 type / style cases are bit for bit unchanged; Neovius skeletal changes by less than 5 % of a cube and 2 % of the volume. An on-level sample (a box plane on a node) is now snapped instead of throwing, so the grid
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
4. Lidinoid and graded cell size: not in the first PR.
