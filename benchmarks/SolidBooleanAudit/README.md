# SolidBooleanAudit

An end-to-end audit harness for `SolidBooleanPipeline` (mesh_ops/SolidBooleanPipeline.cs): a procedural adversarial corpus
(no mesh files), an oracle that shares no code with the pipeline, and the measurements behind
[docs/research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](../../docs/research/SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md).
It changes no library code and is not a test: it measures, and the note draws the conclusions.

```
dotnet run -c Release --project benchmarks/SolidBooleanAudit                       # corpus: 60 cases x 10 frames, defaults, then every escalation for what is not ok
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- corpus <class or part of a case name>
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- small              # thin solids at the limit of the rounding
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- tiny               # a small well-shaped solid beside a large one
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- touch              # how far a point in a wall comes apart from it under transforms (#1221)
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- negvol             # ByNesting's negative-volume test over the scale 2^k
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- utm32              # float32 coordinates at UTM magnitudes
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- repair             # the repairs a selection rule can choose from, against the intent
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- reduce             # TargetFraction 0.25 with and without MaxDeviation
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- report             # what Status, Error and the report say in the cases found
dotnet run -c Release --project benchmarks/SolidBooleanAudit -- perf               # the share of the pipeline's checks, ByNesting's cost
```

The raw output of every mode is in [results/](results/) (library `e340ef5c`, 2026-11-22, Linux container, 4 cores, `net10.0`,
Release; the whole set takes about 4 minutes, the corpus 100 s, no run above 20 s). The timings of `perf` and `repair` vary
by 10-20 % between runs; every other number is deterministic (fixed seeds).

## The oracle (Oracle.cs)

- **What a case means** is given by its *intent* meshes: the clean, closed, outward-oriented operands before the corpus
  damages them (a hole, flipped triangles, a cavity wall given outward, ...). For most cases the given meshes are the intent.
- **Truth at a point** p: `op(inside_1(p), ..., inside_n(p))`, where `inside_i(p)` is "the signed crossing number of a ray
  from p with intent mesh i is positive". **The result** is judged at the same points by its own signed crossing number.
- **Own arithmetic only.** The oracle reads triangles out of a `DMesh3` and does the rest on raw doubles in a local frame
  (the centre of the model, so that UTM and 1e12 offsets cost no digits): Möller-Trumbore crossings in a random direction; a
  ray that passes within 1e-9 (barycentric) of an edge, a vertex or its own start is thrown away and another direction is
  taken (24 tries, then the point is not counted). Volume by the divergence theorem, closedness by balanced directed edges
  on vertex positions, components by a union-find on positions. For distances of a few ulps (`touch`) it has its own exact
  dyadic numbers (`Dyadic`: `BigInteger` mantissa and a binary exponent). None of it calls a predicate, a tree, a winding
  number or a repair of the library.
- **Sample points**: 6000 per run, half uniform in the box of all meshes (inflated by 5 %), half within 1e-1 to 1e-4 of the
  diagonal of a random point of a surface.
- **Verdict**: `ok` (Status Success, at most 0.2 % of the points differ, the result is closed, and, where the case has a
  volume in closed form and the frame is exact, the volume agrees to 1e-9), `WRONG` (Success, but more points differ or
  the volume is off), `OPEN` (Success, result not closed), `R:<reason>` (refused).
- The oracle checks itself at every start (volume and winding of a cube and a hollow cube, closedness, the dyadic
  determinant) and exits with 2 if that fails.

Limits: sampling does not see a solid thinner than about 1e-4 of the diagonal; the thin cases are judged by their volume in
the exact frames (identity, soup, 2^40, 2^-40) and by `small` and `tiny`, which measure each small solid by its own
component volume. The corpus rows "plate 2^-40" and "small solid beside a large one" read `ok` for that reason although the
small solid is gone (its volume is below the 1e-9 the volume check resolves); `small` and `tiny` show it.
In the reduction mode the share of differing points is not a defect by itself (half of the points lie close to the surface,
and a reduction moves it); the volume is the measure there.

## The corpus (Corpus.cs)

60 cases in 7 classes, each in 10 frames (600 runs):

| class | cases | what |
|---|---|---|
| base | 7 | clean operands in general position (boxes, spheres, cylinder, torus; U, I, D; three inputs) |
| nested | 9 | hollow solids, an island in a cavity, a cavity made by a difference, a pocket, a cavity wall given outward |
| touch | 14 | face, partial face, edge and vertex contact, identical operands, flush cuts, one ulp apart, a subnormal coordinate |
| thin | 8 | slabs and walls of 2^-30 and 2^-45, a needle, a plate, a 1 : 2^20 pair |
| open | 9 | missing triangle / face, a hole inside the other operand, a cutting sheet, a T-junction, a duplicated face, debris, a zero-area triangle |
| nonmanifold | 6 | operands made of boxes sharing an edge / a vertex / a face, self-overlapping operands, the same box twice |
| orientation | 7 | inside-out operands and components, flipped triangles, a self-intersecting operand with a cavity given outward |

Frames: `id`, `soup` (unwelded), `2^40`, `2^-40`, `utm` (+ (500000, 5500000, 100)), `1e12`, `rot` (a generic rotation, so
exact contacts become near contacts), `rot+utm`, `rot,f32` (rotated, rounded to float32, unwelded) and `x16+utm,f32`
(16 times larger at the UTM point, rounded to float32: what an STL written there holds). The intent goes through the same
map, so a float32 frame is judged against the solid of the float32 meshes.

Escalations tried on every run that is not `ok`: `Orientation = ByNesting`, `AreaMajority`, `AllowInsideOutInputs`,
`InputRepair = AutoRepair`, `AutoRepairThenRobust`, `ByNesting + AutoRepairThenRobust`, `MeshRobustRepair` of every input
before the pipeline, and the plain `ExactMeshBoolean` with its defaults.

## Result (2026-11-22, `results/corpus.txt`)

| class | ok | refused, a setting gives the right result | refused, no setting does | WRONG |
|---|---|---|---|---|
| base | 70 | 0 | 0 | 0 |
| nested | 80 | 0 | 0 | 10 |
| touch | 136 | 0 | 4 | 0 |
| thin | 72 | 0 | 0 | 8 |
| open | 0 | 80 | 10 | 0 |
| nonmanifold | 60 | 0 | 0 | 0 |
| orientation | 0 | 60 | 0 | 10 |
| all | 418 | 140 | 14 | 28 |

No exception, no result that is not closed, no run above 20 s. What the 182 runs that are not `ok` are, and the other
modes' tables, is in the audit note.
