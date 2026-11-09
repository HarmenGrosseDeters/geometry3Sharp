# Source coverage diagnostics

`Sources` infers constrained edges when an output has no subsegment report. The
previous code tried sources in order for every output edge, with the exact
`WithinTube(source, endpoint)` predicate on both endpoints. A 60,000-point mesher
lens has 119,997 edges and 60,000 source occurrences: at most 7,199,820,000 candidate
visits (and twice that many distance tests) in this phase alone.

The oracle now reuses its own tube-widened source boxes and `BoxGrid`, retaining
both exact predicates. `SourceCovers` also serves Coverage. Source order and
reports are unchanged. `G3_ORACLE_SOURCE_INDEX=scan` selects the previous scan;
`grid` is the default, blank/unset means default, and values are case-insensitive.
Invalid values fail before geometry work. Reference and exact-coordinate runs
retain the scan. No geometry3Sharp dependency is added to the independent oracle.
The equivalence argument is in
[MESHING2D_FAST_ORACLE.md](../../docs/design/MESHING2D_FAST_ORACLE.md).

## Commands

```sh
dotnet build benchmarks/OracleSourcePilot -c Release
dotnet build benchmarks/Meshing2Benchmark -c Release -f net10.0
dotnet run --project benchmarks/OracleSourcePilot -c Release -- lens-input 30001 lens.input.txt
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --no-oracle --milestone M1 --input lens.input.txt --output lens.mesh.txt
dotnet run --project benchmarks/OracleSourcePilot -c Release -- phase lens.input.txt lens.mesh.txt
G3_ORACLE_SOURCE_INDEX=scan dotnet run --project benchmarks/OracleSourcePilot -c Release -- phase lens.input.txt lens.mesh.txt
dotnet run --project benchmarks/OracleSourcePilot -c Release -- full lens.input.txt lens.mesh.txt report.json
```

`phase` uses reflection to invoke the real M1 preparation and `Sources` methods,
then stops. It deliberately supports only the M1 zero-tube diagnostic here. It
does not run the other checks, and every record says `judgment_complete=false`.
It runs against the original oracle without changing or instrumenting it. A
renamed/missing preparation member fails loudly. `full` calls the ordinary
`Oracle.Check` and serializes **all public report fields**, with exact rationals
and BigInteger values encoded as strings; it exits unsuccessfully for an invalid result.
The native equivalence tests also compare every public report field, including
ordered violation details and optional snapped-area/restoration facts.

`profile_sources.py --help` describes the resumable controller. Supply separate,
frozen output directories for the unmodified and indexed pilot builds, one frozen
mesher producer, and one writable work directory. It generates each procedural
lens once, binds its input/mesh/runtime hashes, and reuses it across controls.
Completed records are skipped only if metadata and fixture hashes still match;
timeouts are retained as censored results. The timer covers grid construction and
the whole Sources call. Separate process wall time includes parsing/preparation.
Do not run builds or other measurements beside the diagnostics. Host activity is
not instrumented, so these local numbers do not guarantee isolated hardware.

`check_reported_boundary.py` adds a separate semantic control for the large lens:
the original oracle receives the known cyclic boundary as an explicit optional
subsegment report, while the indexed oracle receives the original unreported
mesh. Both run the full checker and their entire reports must match byte for
byte. Their serialized mesh hashes differ; this is **not** a timing comparison
on identical files. The old unreported inference scan remains separately
measured, including its timeout. The tool checks identity mapping and the absent
subsegment report; use it only with the generated convex lenses.

## Building blocks and sources

The library search found `Sources`, `SourceCovers`, `SegmentBox`, `BoxGrid`, the
Marks source query and their tested conservative-envelope argument. This fix uses
those existing oracle components. The prior design's published reference is
McConnell, Mehlhorn, Näher and Schweitzer, *Certifying algorithms*, Computer Science
Review 2011 (approach only); the exact tube and source definitions are the
repository's meshing spec. No foreign implementation, Triangle/Triangle.NET code,
owner quadtree code or mesher predicates were read or copied for this change.

The grid retains a linear-size registration bound (wide sources join the unknown
list). Its coverage query caps enumeration at 4096 units, then restarts the exact
scan rather than copying/sorting all sources. This protects the old scan's early
exit on dense or unknown boxes. Sorting has a bounded candidate count; grid
construction and the existing amortized stamp-generation reset are separate.
The exact fallback itself can still be quadratic when no early source matches.
The lens regression test counts work at n and 4n rather than asserting elapsed
time; separate cases pin dense known buckets, unknown sources and stamp reuse.

The [measured diagnosis and controls](results/2026-11-08/README.md) contain the
original/scan/indexed records, operation and Amdahl bounds, complete report
comparisons, hashes and commands. `mutate_source_coverage.py --work <external-dir>`
replays the six targeted hand mutants and restores the original source bytes in
`finally`. Run it serially, without concurrent source edits or builds consuming
those files; its artifacts/logs stay in the supplied external directory.

`verify_results.py --runs <runs> --boundary <control>` is the final acceptance
gate, including after a resumed campaign. It recomputes fixture/report hashes,
requires byte-identical small-case full reports, checks all completed phase
fingerprints against the cyclic boundary, and verifies the separate large
semantic control. An exit-zero checker with a changed diagnostic fact still
fails this gate. Its negative tests run in CI without fixture assets.
