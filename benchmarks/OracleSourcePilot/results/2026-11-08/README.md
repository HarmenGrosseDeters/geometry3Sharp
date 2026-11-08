# Sources diagnosis and controlled fix

Measured on the local two-CPU container, .NET SDK 10.0.403/runtime 10.0.14. The
unmodified oracle is master `941d9473`; source/runtime hashes bind every record.
The indexed build changes source coverage only, with a separate scan control.
All meshes are generated once by that master's M1 mesher and reused byte for
byte across these three source-lookup variants. No meshes are committed.

## Diagnosis and upper bound

At 1,024 points, the original full process took
4.807810 s, of which its Sources lap was 4.3354 s:
90.17% of process wall time. If only that
phase became free and all other costs stayed fixed, the source-only Amdahl upper
bound is 10.177x for this run. This is an upper
bound on possible improvement, not a measured speedup or a bound for every input.

Sources scans input sources for each mesh edge when subsegments are unreported.
It can visit E*S candidates and make at most 2*E*S exact distance tests. The 60,000
point mesh has E=119,997 and S=60,000: candidate upper bound 7,199,820,000 and
exact-test upper bound 14,399,640,000. These are operation bounds, **not measured
counts or extrapolated runtimes**. The unmodified and final scan-control phase
diagnostics both reached Sources and timed out with a 90-second **process** budget;
that budget also includes parsing and preparation. No exact duration of their
unfinished Sources calls is claimed.

## Same-mesh source component

Original and scan control: one run per size. Indexed: median of three complete
runs. Timer covers the entire Sources call, including grid/box construction.
Preparation and parsing are excluded from that timer and separately included in
process time. Rows are generated M1 lenses, total points = 2*m-2.

| Points | Original Sources, s | Final scan control, s | Final grid median Sources, s | Grid source candidates visited |
|---|---:|---:|---:|---:|
| 256 | 0.357565 | 0.352220 | 0.009981 | 3,922 |
| 512 | 1.150012 | 1.098819 | 0.010173 | 9,540 |
| 1,024 | 4.472679 | 4.191312 | 0.016351 | 27,916 |
| 2,048 | 18.370476 | 17.926699 | 0.027880 | 71,466 |
| 60,000 | timeout (90 s process budget) | timeout (90 s process budget) | 1.363003 | 10,389,416 |

Every completed phase has the same inferred edge count and fingerprint as the
independently computed cyclic boundary; all completed old/control/indexed phase
fingerprints agree. The 60k case infers exactly 60,000 boundary edges, fingerprint
`5e4cb9e4c1f719f992b54ad89ae767fe5fe542aad89cf61857383a8dba930f85`.
The final 60k phase-tool process median is 4.869 s,
including parsing/preparation, versus the censored original process above.

The grid caps candidate enumeration at 4096 work units per attempt and restarts
the exact scan on exhaustion. This protects dense/unknown boxes where the old
scan can stop at its first source. The old scan itself may remain quadratic.
Sorting is bounded by candidate count; one-time construction and amortized stamp
reset are separate. Tests pin known dense buckets beside an outlier, all-unknown
sources, post-abort stamp reuse, and n-to-4n work growth. No elapsed-time assertion
is added.

## Complete reports

At each small size, all three indexed full runs and the final scan-control full
run have the **byte-identical complete OracleReport** of the unmodified oracle on
the same hashed input/mesh, and all are VALID. Report JSON covers every public
field, Q and BigInteger values exactly, and ordered violation details.

| Points | Original full process, s | Grid median full process, s | Observed process ratio |
|---|---:|---:|---:|
| 256 | 0.618 | 0.326 | 1.89 |
| 512 | 1.479 | 0.370 | 4.00 |
| 1,024 | 4.808 | 0.529 | 9.09 |

The original 60k mesh has input SHA-256
`a934309c13a896e26361db9cf167c6d754aa2727d33e1466bb0559cffd5633ea` and output SHA-256
`a639e9b02d2c4dc7af72c60d39a6bed8eebdb7d4133694b0b03d0bca4be883b2` (identical to the earlier #411 lens).
The indexed full checker completes in
24.430 s and returns VALID,
59,998 triangles. Its remaining large phases include Segments and Delaunay;
this change does not address those phases or claim the global 2x-mesher goal.

A second full-report control gives the **original** oracle the independently known
cyclic boundary through the optional subsegment report. It avoids the unreported
inference scan, then runs every ordinary check. It produces exactly the same full
report as the indexed checker on the original unreported mesh:
`84f1b7517cb06d9f69985325ddfa48128015988dc8722e0b32a35d046b80a8c8`.
The serialized meshes **differ** in this control: it is semantic equality across
report representations, not a before/after timing comparison on identical files.
The original unreported 60k scan is still separately retained as a timeout.

## Reproduction, scope and checks

Use `commands.sh` from the repository root with the source checkouts and external
work directory supplied through environment variables. Frozen producer/baseline
outputs must not be overwritten. `source-binding.json` names every original and
indexed oracle source hash; metadata binds the entire runtime, controller,
inputs/outputs, repeat count and process budget. Completed controller records
resume only if metadata and hashes match. `reports/` stores one canonical copy of
each complete report; all redundant runs bind those same report hashes.

These are local component diagnostics and complete-checker comparisons. Host
activity was not instrumented; no confidence intervals or isolated-hardware
guarantee are claimed. Do not infer a full-oracle speedup from the component-only
60k timeout. Exact/reference and source scan controls remain available. No
predicate, margin, mesher behavior, geometry3Sharp dependency or API changed.

Six targeted source/guard mutants are rejected (see `mutations.json`), including
removing the index, ignoring an endpoint, dropping unknown boxes, ignoring the
scan mode, and disabling either query budget. Source bytes are restored afterward.
Final native/target/package checks and independent review are recorded separately.

Final validation: **1178 native tests passed**, including all 25 new source-lookup
cases; default and scan command-line CI each passed 396 integrated self-checks
and 28/33/52/52 golden rows with zero differences. All-target builds, both net48
test-project builds and both API-validating packs passed. Invalid configuration
exits 2 before self-check/geometry work. Six hand mutants fail their targeted
tests and source bytes are restored. Five acceptance-gate tests pass, including a
changed diagnostic with a recomputed per-file hash. `verify_results.py` accepts
the retained dataset and is mandatory in `commands.sh`, including after resume.

Independent review verified the code, bound/fallback argument, numeric provenance
and complete reports, then reran a 512-point phase/full sample: both full reports
were byte-identical and both validity checks passed. It also reran the five Python
tests and complete dataset acceptance. Its initial missing-acceptance-gate finding
is fixed and recorded in the review checklist. No blocking findings remain.
Details and hashed local evidence are in `validation.json`, `review.json` and
`acceptance.json`.

Master integration after #411 merged: the frozen timing records above remain bound
to feature commit `3ad04065` on `941d9473`; they are not relabelled as new timings.
The source coverage implementation is unchanged. Combining the source constructor
with the conformity index fields preserves both controls, and both checklist
additions are retained. The combined 60k full-check smoke is VALID and produces the
same complete report SHA-256 as above. Post-integration checks are recorded in
`master-integration.json`.

The integrated merge `893151b8` passes 30 source/conformity tests and all target,
net48 and API package checks. Default and source-scan command-line CI each pass
412 self-checks and the same 28/33/52/52 golden rows with zero differences. The
fresh integrated build also completes the original 60k full check as VALID,
59,998 triangles, with the same complete report hash. No integration timing is
claimed. Independent resolution-only review found no issues; the previous feature
head passed GitHub CI, New tests on base and Z3 pilot.
