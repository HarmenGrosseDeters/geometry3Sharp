# OpenCuttingSurface

Prototype for the study [OPEN_CUTTING_SURFACE_2026-11.md](../../docs/research/OPEN_CUTTING_SURFACE_2026-11.md) (#1211): what a
Difference whose tool is an open cutting surface (a sheet) gives today, and what the candidate semantics would give. Public API
only, procedural inputs, no library code. The workpiece is the cube `[0,4]^3` (13 sheets) or a sphere (cost).

```
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- today             # the existing parts
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- semantics [n]     # the candidates A to E; C samples n^3 points (48)
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- cost [sizes...]   # sphere against a wavy sheet, median of 5
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- all
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- gridfb            # AutomaticSteps None against the default GridBeforeFallback (not part of all)
```

- Candidate B (the sheet role of PR #1223) is called by reflection (`ExactMeshBoolean.Roles`), so the project builds on a
  library without it and prints `n/a`; with it the columns B and E fill in.
- Candidate C is not built as a mesh: the point set `{ p in W : w_S(p) <= tau }` is sampled at the cell centres of an `n^3` grid
  (the direct sum of the solid angles), for `tau = 1/2` (today's rule) and `tau = 0` (the sign rule).
- Candidate D builds the swept tool here (the sheet, its copy moved against the area-weighted mean normal beyond the common box,
  walls on the free edges; `MeshExtrudeMesh` builds the same shell) and runs the closed `ExactMeshBoolean`; it refuses a sheet
  with a triangle that does not face the sweep, which is necessary and not sufficient for an embedded tool (parts stacked along
  the direction pass). The direction and that test are in doubles (a prototype).
- `intent` is the volume of the cut by the sheet's sides, with the convention of #1223: the tool is behind the sheet (the side
  its normals point away from). `-` where the sheet does not define one. `ok` / `WRONG` compare a result's volume with it and, where the
  intent is a point set in closed form, the side at 216 points;
  `open(n)` counts the boundary edges of a result after a weld by position.

Raw output of `all` (2026-11-22, a cloud container, 4 cores, .NET 10.0.115; single container, read the ratios of the cost table,
not the milliseconds):

- [results/master.txt](results/master.txt): master `e875d747`.
- [results/sheet-role-pr1223.txt](results/sheet-role-pr1223.txt): the head of PR #1223 (`2f3f33ec`), the same source copied
  into a worktree of that branch.
- [results/sheet-route-pr1447.txt](results/sheet-route-pr1447.txt): PR #1447 (`1e328f8`, 2026-11-23), the sheet route.
- [results/open-inputs-without-effect.txt](results/open-inputs-without-effect.txt): `today` on the branch that adds
  `ExactMeshBoolean.OpenInputsWithoutEffect` (2026-11-23); the last column also prints that list. It names the sheet in the 10
  cases the volume proxy flags and for the disc inside the cube; not for the bucket (it cuts) and the sheet outside.

## The pipeline columns since the sheet route (#1447)

The three pipeline columns of `today` run `SolidBooleanPipeline` without `InputRoles`. Since #1447 an open tool of a
Difference without a stated role is tried as a sheet first and falls back to `InputRepair` when the sheet role refuses, so
these columns now show the route, not `InputRepair` alone (the two older result files show the pipeline before it). On the
13 cases:

| pipeline | before the route | with the route |
|---|---|---|
| `Refuse` (default) | `OpenInput` in 13 of 13 | the cut in the 6 cases whose sheet crosses the cube (all `Success`, clean, the intended volume); `OpenInput` in the other 7 (the sheet role refuses, the fallback refuses as before) |
| `AutoRepair`, `AutoRepairThenRobust` | `Success` and clean in 13 of 13, wrong in 6 of the 8 with a known intent | the same 6 cuts; the other 7 fall back to the repair as before (`Success`, clean, the cube unchanged), wrong in 1 of the 8 with a known intent (the sheet on the top face with its normals up: intent 0, result 64) |

The 7 cases the sheet role refuses are the half sheet, the sheet with a hole and the disc inside (a free edge inside the
cube), the three sheets on the top face (they only touch) and the sheet outside (uncut). The repair path of the fallback is
unchanged here; the self-intersection check of #1426 is not part of #1447.

## The Grid step before the fallback (`gridfb`)

`SolidBooleanPipeline.AutomaticSteps` defaults to `GridBeforeFallback | GridAfterRoundingNotClean` (the benchmark's cases are refused sheets, so the second bit adds nothing to them): a refused sheet (`RimInsideWorkpiece`) of an open tool the pipeline routed itself is
retried once on the data grid before the fallback. `gridfb` runs `None` (before) and the default (after) in one process, wall ms, median of 7 after a warm-up
(2026-11-24, a shared Windows machine, .NET 10; read the ratios of one row, the sphere rows are noisy and run the same code in both columns):

| case | None (before) | default (after) |
|---|---|---|
| sphere n=32/64/128 minus a wavy sheet (no refusal) | 206 / 227 / 1317, `SheetRole` | 105 / 243 / 1079, `SheetRole` (the same route: no extra work) |
| 64-gon cylinder rim +-3 ulps in the top face, 1.2e6, `Refuse` | 3.8, `OpenInput` | 8.8, `Success`, clean, `SheetRole,Grid,SheetRole` |
| the same, `AutoRepair` | 61.6, `Success`, not clean | 7.8, `Success`, clean |
| 512-gon, `Refuse` | 61, `OpenInput` | 115, `Success`, clean |
| 512-gon, `AutoRepair` | 318, `Success`, not clean | 112, `Success`, clean |
| patch well inside the block (the grid cannot help), `Refuse` | 0.3, `OpenInput` | 0.6, `OpenInput`, `SheetRole,Grid,Fallback,Refused` (one extra boolean) |
