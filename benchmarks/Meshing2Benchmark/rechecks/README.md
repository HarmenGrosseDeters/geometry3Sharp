# Historical meshing exception recheck

Tier M, test/tool work only. The goal is dedicated constrained-quality golden
coverage plus a reproducible recheck of the exceptions in
`docs/design/MESHING2D_REFERENCE_COMPARISON.md` section 6, after #394–#396 and the
oracle acceleration #392. Production mesher, exact arithmetic, oracle predicates,
quality rules and tolerances are unchanged.

## Inputs and comparison scope

`historical-exceptions.json` pins 163 case/mode operations from the reference
session's run A: 79 operations whose verdict includes AngleBelowBound, and all
84 historical ORACLE-TIMEOUT operations (44 conforming, 40 constrained). Modes
are paired by triangulation type, as corrected in #393: our conforming output
against historical forkC, our constrained output against historical fork-cq.
A reference verdict is comparison evidence, not an oracle or a golden value.

Sources are **rows and input descriptions only** from
`HarmenGrosseDeters/g3-meshing-references`, branch `claude/reference-producer`,
pinned commit `6fb929f9c86decb2b33f28a939d1031e1cd13a55`. The historical g3 version
is `d9006aad9c83353b4a88dec7acc58d463b912548`. No Triangle, Triangle.NET or owner's
fork implementation code is read, cloned or ported. Reference producers are not
rerun in this clean session.

The old mesh outputs are not archived in those rows, so this is a **new meshing
and oracle run**, not re-judging the old outputs. It reports current results
against the historical verdicts. The old reference outputs are not re-judged with
the current oracle either. No entire 6000-case campaign or current cross-library
performance comparison is implied.

GEOS XML fixtures are fetched by pinned public commit URL and SHA-256. Preparation
reads A/B linework, preserves order and exact binary64 coordinates, and produces
the canonical text format; it does not repair geometries or run a reference mesher.
Shapely 2.1.2 (BSD-3-Clause, installed wheel) is used only for WKT/WKB parsing;
NumPy 2.5.4 is pinned as its dependency. They are separate tool dependencies, not
library dependencies. The manifest records test-data origins and hashes; licenses
and the Shapely wheel's bundled GEOS are documented in
[`docs/SOURCES.md`](../../../docs/SOURCES.md#historical-meshing-recheck-tools-and-data)
and the [meshing provenance](../../../docs/design/MESHING2D_PROVENANCE.md#corpus-public-test-inputs-data-only).
No GEOS implementation source is read. Neither
input datasets nor generated mesh files are committed.

All 163 base input hashes must match the historical rows. The 79 non-timeout
variants also have full historical variant hashes, checked directly. For the 84
old timeouts there is no completed historical row/variant hash: the variant is
reconstructed from the checked base plus the documented angle 20/25/30 and
Steiner limit 200000. Its newly computed canonical hash is recorded explicitly.
Four operations use the procedural catalogue F-12/b instead of external data.

## Reproduce

```sh
python3 -m venv /tmp/g3-meshing-rechecks
/tmp/g3-meshing-rechecks/bin/pip install -r benchmarks/Meshing2Benchmark/rechecks/requirements.txt
dotnet build benchmarks/Meshing2Benchmark -c Release
/tmp/g3-meshing-rechecks/bin/python benchmarks/Meshing2Benchmark/rechecks/prepare.py \
  --manifest benchmarks/Meshing2Benchmark/rechecks/historical-exceptions.json \
  --data /tmp/g3-geos-data --inputs /tmp/g3-recheck-inputs --out /tmp/g3-prepared.json \
  --dotnet /path/to/dotnet --benchmark /path/to/Meshing2Benchmark.dll
python3 benchmarks/Meshing2Benchmark/rechecks/run.py \
  --manifest benchmarks/Meshing2Benchmark/rechecks/historical-exceptions.json \
  --prepared /tmp/g3-prepared.json --inputs /tmp/g3-recheck-inputs --dotnet /path/to/dotnet \
  --benchmark /path/to/Meshing2Benchmark.dll --work /tmp/g3-recheck-run \
  --mesh-timeout 60 --oracle-timeout 120
```

`prepare.py` writes only `case|mode -> input_sha256` to `--out`; the inputs are
named by a hash of `case|mode` in `--inputs`, so no local path is recorded. The
runner joins this map with the historical manifest.

The runner is sequential and resumes only from durably completed records. Keep
its benchmark binaries fixed for a run; do not build into that directory while
it runs. Resume binds benchmark, oracle and library hashes, prepared manifest,
stage budgets and the empty variant environment. Any `G3_M2_*` or `G3_ORACLE_*`
variable is rejected, so a forced mode or experimental variant cannot silently
enter a default-version recheck. A missing/changed pinned input also fails.

Each stage has its own wall-time budget. A mesh timeout is not an oracle timeout,
and neither is a valid result. New budgets are 60 s mesh and 120 s oracle; the
historical mesher budget was 600 s, and its oracle budget changed from 600 to
120 s during run A. Therefore a new timeout is an unresolved bounded check, not
by itself a speed regression. Timings are diagnostic single runs, not benchmark
speedup evidence.

`results.jsonl` records commands (local paths as `<dotnet>`, `<benchmark>`,
`<inputs>`, `<work>`), input/output fingerprints, exit codes and oracle rows.
Captured stdout/stderr are not stored: each stage keeps per-violation-class counts,
line counts and SHA-256 digests; the oracle row keeps its area and reason text as
digests (the reason also as class counts). Single triangles are not named. It is flushed/fsynced before releasing large regenerable
mesh artifacts. Temporary per-attempt output/row files are cleared before retry.
`summary.json` lists verdicts and `verdicts.csv` one line per operation; the summary separately counts oracle-valid outputs at the
Steiner limit. Such outputs are valid under the operation's bounded contract;
they do not certify that every requested angle was achieved.

Exit 0 requires a nonempty, complete, entirely VALID batch. INVALID, errors,
timeouts or incomplete results give exit 1; the recorded evidence is retained.
An unsuccessful acceptance result is a useful finding, not a reason to relax the
oracle or update a golden row to an invalid mesh.

## Tests and acceptance

```sh
/tmp/g3-meshing-rechecks/bin/python -m unittest discover -s benchmarks/Meshing2Benchmark/rechecks -p 'test_*.py' -v
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~Meshing2GoldenTests|FullyQualifiedName~QualityMesh2SmallAngleRuleTests|FullyQualifiedName~QualityMesh2SmallAngleMarginTests|FullyQualifiedName~QualityMesh2PieceSettleTests'
```

The Python tests use procedural geometries and fake subprocesses: order/shared
vertices, WKB/empty data, base and variant hash mismatch, mode separation, explicit
timeouts, exit/verdict/hash agreement, stale artifacts, resume binding, variant
rejection, and visibility of reached Steiner limits. A path-scoped CI workflow
runs these without downloading external test data. The ordinary meshing CI gate
also checks all 52 constrained golden rows, independently judged at every run.

The task stops at test coverage and recorded findings. Production fixes need their
own scope and review; no newly failing real case is silently skipped or accepted
as a new golden value. Exact commands, validation output and final recheck counts
are recorded with the results, including any unresolved cases.

## Diagnose a large output

Set `G3_ORACLE_TIMING=1` only on a separate diagnostic oracle call to print phase
times. The acceptance runner deliberately rejects this variant environment.
On a retained nonempty `mesh2-output` file, count bounding-box candidates with:

```sh
/tmp/g3-meshing-rechecks/bin/python benchmarks/Meshing2Benchmark/rechecks/count_boxes.py /tmp/output.mesh.txt
```

This independent counting tool uses offline sweeps and prefix sums, not the
oracle's pair enumerator. It counts closed-box overlaps, subtracting triangle
corners and edge pairs with shared endpoints in separate totals. It does not
judge actual geometric intersections. Its counters are checked against exhaustive
enumeration on 100 procedural batches, including ties and degenerate boxes.
The current large-case phase diagnostic and candidate counts are in
[`results/2026-11-08`](results/2026-11-08).
