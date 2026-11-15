# TpmsLattice: graded cell size (#816)

Status: **approved by the coordinator 2026-11-13 with decision 2 narrowed (below); implemented.** Tier L light (an extension of a merged module with a changed proof
obligation): this note, then one PR from master. Builds on [TPMS_LATTICE.md](TPMS_LATTICE.md) (#750).

Goal: a lattice whose period varies with position (small cells where the part is loaded, large cells elsewhere),
keeping everything the constant-period operation guarantees: a closed, manifold, self-intersection-free result checked at
runtime, the volume-fraction map, the domain clip, determinism and similarity equivariance. Today only the volume
fraction can be graded (`VolumeFractionAt`).

## 1. Look before we build

- Library: the field is `f(2 pi x / L)` evaluated per sample in `TpmsLattice.FieldFunction`; the level, the clip and the
  contouring do not care how the phase is computed. So graded cells are a different phase map in front of the same field.
- Literature (formulas in docs/SOURCES.md): Al-Ketan & Abu Al-Rub 2019 grade the cell size by replacing the phase
  `2 pi x / L` with a position dependent phase `2 pi psi(x)` (their "cell size gradient"); a constant `L` is `psi = x / L`.
  The known artifact: unless `psi` is a gradient field with `|grad psi| = 1 / L(x)` in every direction, which does not exist for a
  general `L(x)` in 3D, the cells are distorted, by an amount proportional to the variation of `L`. Everybody lives with
  this; it is a property of the method, not of the implementation. Others (the nTop-style tools, `MSLattice`) expose
  exactly a position dependent phase or an "effective cell size" field.

## 2. API (additive; three new members of `TpmsLattice`, one new class)

```cs
public Func<Vector3d, Vector3d> PhaseMap;   // general: local position -> phase in periods (null: x / CellSize); no Enclosure
public TpmsPhaseGrade Grade;                // a library-derived Jacobian bound (TpmsLattice.LinearGrade): the only graded form with Enclosure
public double MinLocalCellSize;             // output: 1 / the largest sampled phase gradient, with a map or a grade
public static TpmsPhaseGrade LinearGrade(int axis, double z0, double period0, double z1, double period1, double transverseHalfExtent);
public sealed class TpmsPhaseGrade { Map, JacobianBound, Axis, TransverseHalfExtent }
```

- The field is `f(2 pi Map(x))`, so periodicity in phase space is exact. A map must be pure, thread-safe and finite on the grid
  (`InvalidOptions` otherwise, and when it is constant on the domain). Set `PhaseMap` or `Grade`, not both.
- `CellSize` stays the nominal period of the grid (the cube is `CellSize / Divisions`): the user sets it to the **smallest** local
  period. `Compute` samples the map on a 9^3 set of points of the domain and at +-h/8 around them (the map must be finite slightly outside the domain; the largest column norm of the sampled Jacobian can be up to sqrt 3 below the operator norm, and a narrow feature between the samples can be missed: the check is a guard, the result is certified separately, and for a `Grade` the extremes sit at the sampled box corners), reports the smallest local period it saw
  (`MinLocalCellSize`) and refuses (`InvalidOptions`) a grid whose cube is larger than a quarter of it.
- `LinearGrade`: the period varies linearly along one axis, the phase along it is the exact integral `ln(period(z) / period0) / slope`,
  the transverse phase is the coordinate over the local period. It derives the Jacobian bound itself
  (`sqrt(3 / Lmin^2 + 2 (e |slope| / Lmin^2)^2)`, the Frobenius norm, with `e` the transverse half extent) and the library checks that the
  domain lies within that extent (`InvalidOptions` if not; the check is on the domain plus the grid pad of 3.5 cubes, since the grid reaches 3 cubes beyond the box and a shifted grid one more). `WallThickness` is not defined with a map or a grade.

## 3. What stays guaranteed, what is not

- **Always, checked at runtime on the result** (unchanged): `NotCleanReason == ""` plus the exact self-intersection check. Grading
  changes only the field, so this holds for any map.
- **Volume fraction**: the quantile table is per unit period, so the local fraction is the target where the map is locally a
  similarity (tested above z = 3 of the linear grade, where the period is constant); where the cells are distorted it deviates.
- **Enclosure**: with a `Grade` the field is Lipschitz with `Lf 2 pi J`, `J` the derived bound, and the recipe applies unchanged
  with `Lw = Lf 2 pi J` (the phase rounding term of the error bound becomes `4 u |phase| / (J s)`, checked against `2^-30`;
  after the first review the term is `8 u (2 phaseMax + 4 J h) / (J s)`: `LinearGrade` evaluates the exact phase integral with the
  `log(1 + x) / x` form (`Log1pOverX`), accurate to a few ulps also for gentle grades where `log(period / period0) / slope` would lose
  digits, and the phase magnitude is padded by the grid reach; the assumption of 8 u is on the map, the same kind of assumption as for
  `Math.Sin` and `Math.Cos`). A general
  `PhaseMap` with `Enclosure` is `InvalidOptions` (owner / coordinator decision 2026-11-13: a user-supplied bound is a certificate
  the library cannot check). A user-bound path can be a follow-up if someone needs it. The sampled Jacobian check that the first
  draft of this note had as a runtime refutation is a test of the helper's derived bound instead.

## 4. Resolution and cost

The grid is set by `CellSize / Divisions` over the box as before; `MaxCells` is checked before any allocation. The cost grows only if the user
sets `CellSize` small for a small region: a large part with a small minimum cell needs many cubes (reported by `TooLarge`).
An adaptive grid (coarser where the cells are large) is a different, larger feature (octree contouring) and is not proposed here.

## 5. Tests (each with a mutation)

- Clean (`NotCleanReason`, closed, finite) for each type with a linear grade and with a radial grade (delegate), box and mesh domain.
- Local period measured from the result: along the grade axis the spacing of the lattice's features (the distance between
  consecutive surface crossings along a line) follows `period(z)` within a stated tolerance (a mutant that ignores the map fails).
- Volume fraction in slabs of the graded result against the target within the distortion bound for the linear grade.
- Equivariance under `Similarity.All()` (the map is given in the local frame; scale: `PhaseMap` scaled with `CellSize` for 2^k
  bit for bit), reuse, determinism (parallel equals serial), `TooLarge`, bad map (NaN, infinite), `InvalidOptions` for an
  unresolved grid and for Enclosure without a bound.
- Enclosure with a supplied bound: points of the exact graded lattice are inside; a mutant that halves the bound fails the pin and a
  too-small supplied bound is refused by the sampled check on a case where it is wrong.
- Benchmark: the graded field costs one delegate call per sample on top of the constant field; the benchmark README gets one row.

## 6. Decisions (coordinator, 2026-11-13)

1. A general `PhaseMap` delegate plus the `LinearGrade` helper: yes.
2. `Enclosure` only where the library derives the bound (`Grade`); a general `PhaseMap` with `Enclosure` is `InvalidOptions`.
3. The distortion of a general grade is documented, not corrected; the tests check the local fraction only where the map is a similarity.
