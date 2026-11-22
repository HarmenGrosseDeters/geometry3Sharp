# Meshing2HardProblems: prototypes of the HP 6 / HP 7 research spike

The prototypes and measurements of the research note
[docs/research/MESHING2D_HP6_HP7_SPIKE_2026-11.md](../../docs/research/MESHING2D_HP6_HP7_SPIKE_2026-11.md) (plans #1282 and
#1283). Nothing here is library code, and nothing here is a gate. The note has the method, the tables and the options; this
file has the commands and the layout.

```
dotnet build benchmarks/Meshing2HardProblems -c Release
dotnet benchmarks/Meshing2HardProblems/bin/Release/net10.0/Meshing2HardProblems.dll hp6 > perf/hp6-net10.csv
dotnet benchmarks/Meshing2HardProblems/bin/Release/net10.0/Meshing2HardProblems.dll hp7 --size large > perf/hp7-net10.txt
dotnet benchmarks/Meshing2HardProblems/bin/Release/net10.0/Meshing2HardProblems.dll selfcheck > perf/selfcheck-net10.txt
```

Measured 2026-11-22 in a Linux container, 4 cores, .NET 10.0.15: `hp6` 4.5 minutes, `hp7 --size large` 12.6 minutes
(`--size small` 1.3 minutes), `selfcheck` a few seconds. The timings of `hp7` are from one machine, with nothing else running.

## Layout

| file | what |
|---|---|
| `Pslg.cs` | the procedural inputs of HP 6 (8 families, one parameter: the small angle) and the wedges of an input |
| `Hp6Refiner.cs` | a reference refiner written from the papers: Ruppert with shells, the exemption of Miller, Pav and Walkington, a collar after Pav and Walkington. It rebuilds the triangulation with `ConstrainedDelaunayMesh2` every round and works in doubles |
| `Hp6.cs` | the library's mesher and the reference rules on every input; the triangles below the bound by class |
| `Hp7Checker.cs` | the prototype checker of a certifying design (rings, no snap grid, no excuses) and the derivation of its certificate |
| `Hp7.cs` | the oracle's time by check against the mesher and the checker; the fault injection of `selfcheck` |
| `perf/` | the raw output of the three commands |

## Commands

- `hp6 [--phi 40,20,10,5,1,0.1] [--bounds 20,25] [--filter fan] [--meshers g3-default,g3-cq,ruppert,mpw,collar]`:
  one CSV row per input, bound and mesher. Columns: status (for the library: the oracle's verdict), time, vertices,
  triangles, smallest and largest angle, the triangles below the bound and their classes (`apex_forced`, `across_shell`,
  `across_1.5`, `across_far`, `apex_unforced`, `other`), how many spanning triangles are below the floor
  arctan(sin φ / (2 − cos φ)), the smallest angle that is not forced, and the farthest bad triangle that is not forced, in
  units of the apex's shortest segment.
- `hp7 [--size small|large] [--filter parcels]`: one row per case (mesher, oracle and checker times, both verdicts), then
  per case the oracle's six largest checks (its own laps, `G3_ORACLE_TIMING`) and the checker's passes.
- `selfcheck`: the checker accepts the mesher's results and rejects 43 corrupted meshes and certificates, among them a double
  cover of an annulus. Exit code 1 if a case fails.
- `dump --case <name> --output <file>`: one `hp7` case in the text input format of `Meshing2Benchmark`, to judge it with
  `Meshing2Benchmark mesh --input`.

## Limits of the prototypes

- The reference refiner has no off-centres, no lenses, no grid and no area bound, as the proofs it follows. Its plain mode is
  stopped at 20 000 vertices, the others at 60 000. Its triangle counts are not comparable one to one with the library's.
- The checker models the core only: a triangulation that covers a domain given by rings, the chains of the sources, the local
  Delaunay test and the list of triangles below the angle. It decides near-ties of its tube and angle tests in doubles and
  counts them; a real checker decides them exactly. With a snap grid its acceptance says nothing about the grid rules.
- The certificate is derived from the geometry, outside the timing: the library's own record
  (`ConstrainedMesh2.SubsegmentSources`) is internal.
