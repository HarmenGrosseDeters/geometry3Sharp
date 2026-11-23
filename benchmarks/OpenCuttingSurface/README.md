# OpenCuttingSurface

Prototype for the study [OPEN_CUTTING_SURFACE_2026-11.md](../../docs/research/OPEN_CUTTING_SURFACE_2026-11.md) (#1211): what a
Difference whose tool is an open cutting surface (a sheet) gives today, and what the candidate semantics would give. Public API
only, procedural inputs, no library code. The workpiece is the cube `[0,4]^3` (13 sheets) or a sphere (cost).

```
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- today             # the existing parts
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- semantics [n]     # the candidates A to E; C samples n^3 points (48)
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- cost [sizes...]   # sphere against a wavy sheet, median of 5
dotnet run -c Release --project benchmarks/OpenCuttingSurface -- all
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
- [results/open-inputs-without-effect.txt](results/open-inputs-without-effect.txt): `today` on the branch that adds
  `ExactMeshBoolean.OpenInputsWithoutEffect` (2026-11-23); the last column also prints that list. It names the sheet in the 10
  cases the volume proxy flags and for the disc inside the cube; not for the bucket (it cuts) and the sheet outside.
