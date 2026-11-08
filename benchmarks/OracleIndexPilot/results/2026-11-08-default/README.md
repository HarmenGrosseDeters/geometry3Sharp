# Default promotion, 2026-11-08

After the point/edge experiments, the owner requested that both certified indexes
become the binary64 oracle default. No index variables are required. Explicit
`strtree` / `strtree-hulls` values remain supported; `sweep` restores each previous
path independently. Exact-reference and exact-coordinate runs retain their sweeps.
The point-only campaign controller explicitly requests the edge sweep.

The geometric algorithms, predicates, quality rules and mesher are unchanged.
The earlier point and edge result directories describe their frozen commits;
their source/runtime hashes and measured results have not been regenerated for
this selection change.

## Verification

- 663 native oracle/modes/box-pair/golden tests pass with no index settings.
- Default and explicit-sweep oracle selfchecks each pass 408 checks. Twelve new
  policy checks cover default/explicit indexes, fallback, reference and invalid
  settings without mutating the process environment in parallel tests.
- 312 point and 442 edge component checks, plus ten controller tests, pass.
- All-target core and both net48 test-project builds and both API-validating
  package builds pass. Commands and source/log hashes are retained separately.
- Three deliberate selection faults are rejected: old opt-in default, indexes on
  the reference path, and ignoring the sweep override. The source is restored
  byte-for-byte and its 408 selfchecks pass again.
- stmlf/1044 q30 conforming, 301453 triangles, is checked without index settings.
  It is VALID and its entire oracle row equals the edge-pilot result, not merely
  its status. The input/output hashes match. The retained duration is diagnostic
  under concurrent local validation load, not a new speedup measurement.

The independent review found no outstanding selection, fallback, controller or
documentation issue. No merge or change to core library dependencies is made.

## Committed records (review of #411)

Machine paths in the committed records are placeholders (`<benchmark>`, `<pilot>`, `<inputs>`, `<work>`, `<repo>`, `dotnet`),
and raw oracle stdout/stderr is reduced to the verdict, the triangle count, the phase times (`phase_ms`) and, for mutants, the
failure line. The validation scripts write to `$G3_PILOT_WORK` (default: a new temporary directory). Recorded hashes (rows,
records, binaries, inputs, outputs) refer to the original, unreduced records and binaries.
