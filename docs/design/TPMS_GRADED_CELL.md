# TpmsLattice: graded cell size (#816)

Status: **design, awaiting coordinator approval.** Tier L light (an extension of a merged module with a changed proof
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

## 2. API (additive; two new members of `TpmsLattice`)

```cs
public Func<Vector3d, Vector3d> PhaseMap;      // local position -> phase in periods (null: x / CellSize, the constant lattice)
public double PhaseJacobianBound;               // an upper bound of the operator norm of the Jacobian of PhaseMap, in periods per length
```

- `PhaseMap` replaces `x / CellSize`; the field is `f(2 pi PhaseMap(x))`, so periodicity in phase space is exact. Called from
  several threads: pure and thread-safe, finite everywhere in the grid, else `ContouringFailed` (as for a NaN level).
- `CellSize` stays the nominal period of the grid (the cube is `CellSize / Divisions`): the user sets it to the **smallest**
  local period so that every cell is resolved (the rule wall >= 2 cubes now refers to the smallest cell). `Compute` samples
  the map on the grid and reports the smallest local period it saw (`MinLocalCellSize`, a report) and refuses (`InvalidOptions`)
  a grid whose cube is larger than 1/4 of that measured period, so an unresolved grading is not silently meshed.
- Helper `TpmsLattice.LinearGrade(int axis, double z0, double period0, double z1, double period1, out double jacobianBound)`:
  the period varies linearly along one axis (exact integral phase along it, transverse phase scaled by the local period).
  Another helper is easy to add; the general delegate covers the rest.

## 3. What stays guaranteed, what moves to the user

- **Always, checked at runtime on the result** (unchanged): `NotCleanReason == ""` plus the exact self-intersection check.
  Grading changes only the field, not the contouring or the certificate, so this holds for any phase map.
- **Volume fraction**: the quantile table is per unit period, so the local fraction is the target where the phase map is locally a
  similarity (the usual case); where the cells are distorted the local fraction deviates by the distortion. Documented, and the
  tests measure it for the linear grade.
- **Enclosure**: the field `f(2 pi psi(x))` is Lipschitz with `Lf * 2 pi * J`, `J` the Jacobian bound. With a **user supplied**
  `PhaseJacobianBound` (a proof obligation the library cannot check; we add a sampled check that refuses a bound that the sampled
  Jacobian exceeds, which can refute a wrong bound but never prove one) the existing recipe applies unchanged with `Lw = Lf 2 pi J`.
  The price is a larger pad, since `J >= 1 / smallest period`, so the enclosure is looser than for a constant lattice
  where the period is the smallest one; and a field that is dyadic-exactly evaluable is required only as before (the phase
  rounding bound is the one place that grows: `eNorm`'s phase term is `4 u` times the phase magnitude in periods over `J s`; the
  PR recomputes it and refuses when it exceeds `2^-30`).
  Without a bound, `Enclosure` with a `PhaseMap` is `InvalidOptions` (no silent unproven claim).

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

## 6. Decisions needed

1. A general `PhaseMap` delegate plus `LinearGrade` helper (recommended), or helpers only (less to prove, less general).
2. `Enclosure` with a user supplied Jacobian bound and a sampled refutation check (recommended), or no `Enclosure` with grading in this PR.
3. The distortion of the cells for a general grade is documented, not corrected (no 3D gradient phase exists); accepted?
