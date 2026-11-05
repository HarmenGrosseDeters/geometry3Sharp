# Thingi10K full-corpus run: interim publication

The original interim publication was Tier S: an immutable snapshot and its reporting
harness. The subsequent evaluation-pipeline change is Tier M: separate durable
computation/evaluation scheduling and diagnostic phase timing. The measured library,
Boolean/Wrap algorithm binaries, operation/evaluation timeouts, API, mutation workflow
and historical snapshot remain unchanged. This changes scheduling, not an algorithm.

## Durable evaluation pipeline (Tier M)

[run_pipeline.py](run_pipeline.py) is a separate runner; [run_full.py](run_full.py)
stays byte-identical for historical reproducibility. There is one Wrap producer,
one Boolean producer and two evaluation workers. Slow evaluation therefore cannot
occupy the Wrap producer. This is not a claimed library speedup: evaluation can
still time out, and concurrent throughput requires its own before/after measurement.

Boolean results and original operation times are flushed to a hashed external job
manifest **before evaluation**. `results/computed.jsonl` records pending computations;
only evaluated or explicitly failed/not-run records enter final `boolean.jsonl` and
the legacy CSVs. Pending work is never counted as clean. Manifests retain input info,
operation reports, frozen binary hashes and output-artifact sizes/SHA256s. Resume
recovers a missing computed record from its manifest, skips final records and removes
already-finalized artifacts only after final JSONL persistence. Incomplete publications
and interrupted computation directories are retained externally for inspection and
recomputation, not silently deleted. Corrupt/missing artifacts stop the runner.

Backpressure pauses only Boolean production at 16 pending jobs or a 4 GiB queued-byte
high-water mark. The latter is **not a hard peak-space bound**: one running Boolean
computation can exceed it. Admission also requires space for not-yet-downloaded inputs
plus an 8 GiB temporary-space reserve; it does not prove that an arbitrary model fits.
Evaluation releases queued artifacts after the final record is durable. Wrap has no
output queue, but its temporary working memory/space must still be available.

The diagnostic [probe project](probe/EvaluationProbe.csproj) keeps the original friend
assembly name in a separate directory. It references the **measured DLL**, never the
publication library project. [prepare_probe.py](prepare_probe.py) verifies the frozen
driver-source hash and generates instrumented source only at a new external path.
The original driver handles export/Boolean computation; only `eval` is routed to the
probe. The diagnostic `NotCleanReason` copy preserves the pinned order, thresholds,
early returns and self-intersection cutoff. [probe_smoke.py](probe_smoke.py) compares
all emitted metrics against the original evaluator on procedural soups, including
clean, empty, open, inverted, degenerate, bowtie and self-intersecting cases at binary
scales and translations. Neither self-intersection pass is removed.

Phase events in `results/diagnostics/<file>/eval.phases.jsonl` contain operation,
phase, PID, UTC timestamp, started/completed state and elapsed milliseconds. They
separate soup reading, welding, topology, reconstruction, volume/area, intersection
counting and individual cleanliness checks. An unfinished started event on timeout
identifies the interrupted phase; it is not a fabricated completed timing. Events
are flushed durably, so this instrumented evaluator has small additional logging work.

From the publication harness directory, with explicit external paths:

```powershell
python prepare_probe.py D:\Thingi10K-probe\Program.cs --source-sha256 <frozen-Program.cs-SHA256>
dotnet build probe\EvaluationProbe.csproj -c Release -p:ProbeSource=D:\Thingi10K-probe\Program.cs -p:FrozenLibrary=<measured-geometry3Sharp.dll> -o D:\Thingi10K-probe\bin
python probe_smoke.py --original <original-BooleanComparison.dll> --probe D:\Thingi10K-probe\bin\BooleanComparison.dll --report D:\Thingi10K-probe\smoke.json
python -m unittest test_runner test_snapshot test_analysis test_prepare_dataset test_pipeline -v
python pipeline_mutations.py --report D:\Thingi10K-probe\mutations.json
```

Before activation, stop the legacy parent and its exact child PIDs, take an external
backup of final results/configuration/diagnostics, and verify frozen hashes. Do not
allow the old runner to clean in-flight scratch directories before they are secured.
[adopt_legacy.py](adopt_legacy.py) accepts only jobs with matching export/operation
commands, complete export info, successful finite operation timings and all output
files. Legacy logs did not record exit codes: the adoption evidence explicitly says
so. Incomplete jobs are kept outside the queue and recomputed. Full compute wall time
for adopted jobs is unknown (`null`), not reconstructed from partial timings.

```powershell
python adopt_legacy.py --run-root <stopped-external-run-root> <complete-legacy-scratch-directory>
```

Install the new runner beside the unchanged legacy runner, and the probe assets into
`bin/EvaluationProbe` under that external run root. Extend `config.json` with `pipeline`:
version `1`, `runner_sha256`, `probe_sha256`, and `probe_files_sha256` (all deployed
probe DLL/runtime/dependency files). Record generated-source/generator/project hashes,
validation evidence and the migration backup/adoption list separately in the migration
journal. Startup verifies frozen binaries/sources, the old runner, the new runner and
probe assets, including the unchanged probe library reference. It refuses a second
pipeline instance using an OS-level run lease; the lease does not protect against a
separately started legacy runner.

```powershell
python <external-run-root>\run_pipeline.py
```

`progress.json` reports final counts, computed Boolean counts, pending jobs/bytes and
active producers/evaluators. A `STOP` file under the external run root requests a
cooperative stop after running operations/evaluations finish; pending artifacts survive.
Remove it before resume. New model `wall_ms` is compute plus evaluation wall time,
**excluding queue wait**; per-operation `ms` remains the original algorithm timing.
Adopted model `wall_ms` stays unknown. The frozen snapshot below is never rewritten.

The synchronization/backpressure design uses Python's standard-library
[queue concepts](https://docs.python.org/3/library/queue.html), durable file flushes
([os.fsync](https://docs.python.org/3/library/os.html#os.fsync)) and .NET
[Stopwatch](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch).
No new third-party geometry implementation is used.

## Measurement and snapshot

The live run measures **all raw files**, without a face-count sampling filter:

- Exact Booleans: `BooleanComparison`, method `ours`, union/intersection/difference.
- Alpha Wrap: `MeshRepairBenchmark`, `BENCH_REPAIR=wrap`.
- Measured library commit: `cd7fb8abba0af09d5725b60c700dfec95234e7ad`.
- Dataset: [Thingi10K on Hugging Face](https://huggingface.co/datasets/Thingi10K/Thingi10K),
  revision `2d5d3b2f3cd3711028ad75b12788c13b25559ec6`.
- The publication branch is newer than the measured library. Its current library
  build is a compatibility check, **not** the binary used for these results.

[The snapshot](snapshots/20261104-interim/summary.json) was captured at
2026-11-04 20:56:12 +01:00. It contains **1,432 Wrap records and 1,432 Boolean model
records**, out of 10,000 expected per mode. This is **not a completed corpus run**.
A processed record can be a timeout, evaluation failure or unclean result; it is not
necessarily a success. The live run continues independently of this PR.

These counts and the status breakdown were produced by:

```powershell
python snapshot.py <external-run-root> snapshots\20261104-interim
```

The raw command output is represented by the generated, hashed
[summary](snapshots/20261104-interim/summary.json). The snapshotter takes bounded,
independent reads of the two live JSONL files and includes only newline-terminated
records. CSVs are rebuilt from those exact records, not copied from changing live
CSVs. This is a consistent publication of each stream, not a simultaneous checkpoint
of both workers. Local filesystem prefixes are redacted; metric values are unchanged.

## Contents

- [Per-model CSV](snapshots/20261104-interim/results/per_model.csv): Thingi-ID, mode,
  status, algorithm time, wall time, failure reason, actual `NotCleanReason` and commit.
- [Per-operation CSV](snapshots/20261104-interim/results/per_operation.csv): separate
  Boolean operation results, including times retained when later evaluation fails.
- [Wrap JSONL](snapshots/20261104-interim/results/wrap.jsonl) and
  [Boolean JSONL](snapshots/20261104-interim/results/boolean.jsonl): complete captured
  raw metric records, with redacted local paths where present.
- [Configuration](snapshots/20261104-interim/config.json): binary/source hashes,
  parameters, timeouts, commands, reporting changes and runner recovery history.
- [Procedural smoke evidence](snapshots/20261104-interim/smoke.json).
- [Runner](run_full.py), [regression tests](test_runner.py), [smoke harness](smoke.py),
  [read-only snapshotter](snapshot.py) and [snapshot tests](test_snapshot.py).
- [Driver sources](sources/benchmarks): measured reporting-only driver copies and the
  pinned `compare.py`. [The reporting diff](reporting-changes.diff) documents changes
  against the measured commit; it is evidence, not a directly applyable Git patch.
- [Input-stratified analysis](analysis/20261104-interim/summary.json), with
  [candidate operations](analysis/20261104-interim/candidates.csv),
  [empty results](analysis/20261104-interim/empty_results.csv),
  [evaluation failures](analysis/20261104-interim/evaluation_failures.csv) and the
  [50 slowest timed operations](analysis/20261104-interim/slowest_operations.csv).
- [Analysis script](analyze.py) and [offline regression tests](test_analysis.py).
- [Metadata regenerator](prepare_dataset.py) and [offline tests](test_prepare_dataset.py).
  The raw inventory and `file_id,num_faces` projection are regenerated externally,
  not committed. Missing face counts retain the runner's large/unknown timeout.

No original or generated mesh files, compiled binaries, caches, or scratch outputs
are committed. Sources are this repository's own benchmark code, not Triangle,
Triangle.NET, the owner's quadtree, or a new third-party algorithm port.

## Input-stratified interim results

Reproduce this analysis into a **new external destination**, from this directory:

```powershell
python analyze.py snapshots\20261104-interim D:\Thingi10K-analysis
```

The command reads only the immutable snapshot. Its
[summary](analysis/20261104-interim/summary.json) records the measured commit,
input JSONL hashes, classification rules, timing sample sizes and percentile method.
Every row below counts **operations**, not models: union, intersection and difference
are counted separately. Empty and unperformed operations are retained.

| Input class | Models | Operations | ok | unclean | Evaluation failed | Quality unchecked | Not run |
|---|---:|---:|---:|---:|---:|---:|---:|
| Closed, self-intersection count exactly zero | 769 | 2307 | 2291 | 14 | 0 | 2 | 0 |
| Closed, positive self-intersection count | 409 | 1227 | 865 | 361 | 0 | 1 | 0 |
| Closed, self-intersection count unknown | 15 | 45 | 8 | 15 | 18 | 4 | 0 |
| Open or only winding-closed | 237 | 711 | 128 | 499 | 84 | 0 | 0 |
| Input unavailable (export failed) | 2 | 6 | 0 | 0 | 0 | 0 | 6 |

**Important correction:** `input.selfint = -1` means unchecked, not zero. Combining
those inputs with checked-zero inputs reproduces the preliminary 2352-operation
cohort (2299 ok and 29 unclean), but cannot certify all its inputs as self-intersection
free. In the checked-zero cohort, 2291/2307 operations are ok (99.3%); the 14 unclean
operations report 12 degenerate-triangle reasons and 2 bowties. The other 15 candidate
operations have unknown input self-intersections (1 degenerate-triangle reason,
8 bowties, 6 not closed). All 29 remain in the candidate CSV with their input class;
they need procedural reproduction and input/contract checks, not automatic bug labels.
No extrapolation to the full corpus is made from this prefix.

The 49 empty operations in the combined closed/non-positive-count cohort consist
of 33 intersections, 8 unions and 8 differences. All 8 empty unions report zero
signed volumes for both inputs. This is not evidence that a nonzero-volume solid was
lost; zero signed volume alone also does not prove an empty solid. The CSV retains
input/output volumes and volume-identity errors for later triage. B is **rotated and
translated**, not an identical overlapping copy of A; empty intersections or
differences are not automatically wrong. These cases need solid/winding semantics
and geometric checks before being called defects.

The 102 evaluation-failed operations (34 models) are harness evaluation timeouts,
not Boolean algorithm failures: 84 operations have open inputs and 18 have closed
inputs with unknown self-intersections. They are separate from unclean outputs.
Open outputs from open inputs are documented by design in
[OPERATIONS](../../docs/OPERATIONS.md#the-examples); closed self-intersecting cases
require winding/closure and rounding-contract triage, not blanket acceptance or
blanket failure. `Success` alone does not promise a clean rounded result.

Alpha Wrap: 1431 ok and 1 timeout. Among 1431 reported algorithm times, the median is
1.151 s and maximum 19.557 s. Boolean timing uses all 4290 available operation times,
including operations whose later evaluation failed: median 0.1625 s, p95 5.587 s,
maximum 70.096 s. Missing times are not replaced by zero; the p95 uses nearest rank.
These are observations on the pinned measured binary and this interim cohort, not
before/after performance claims or a full-corpus proof.

## Interpretation

`time_ms` is the driver-reported algorithm time, **not** end-to-end processing time.
Boolean model totals are populated only when all operation times exist; missing
values stay empty, never zero. `wall_ms` includes process/JIT and evaluation work.

Unperformed self-intersection checks are explicitly unknown, not successes.
Wrap checks are limited to 300,000 result triangles; Boolean checks to 400,000.
Valid empty Boolean results are allowed. The diagnostic self-intersection count and
`NotCleanReason` can repeat work; this publication does not optimize either check.
Do not extrapolate this prefix or compare it as the same cohort as ROADMAP section B
or Phase 7b's earlier small-sample measurements.

A Windows `WinError 5` while replacing the progress file interrupted the run.
Previously persisted results were preserved. The resumed runner retries only Windows
replace errors 5/32/33 with a bounded budget; persistent errors still propagate.
The identity of the file-lock holder was not established.

## Run the tests

From this directory, with Python 3.14 (the measured environment):

```powershell
python -m unittest test_runner test_snapshot test_analysis test_prepare_dataset -v
```

These offline tests do not need data, .NET binaries, network access, or a corpus run.
The same suite runs in the Linux CI job. The procedural smoke additionally needs
the frozen runtime layout below:

```powershell
python smoke.py
```

Smoke generates its tetrahedron at runtime, checks closed/manifold/finite results and
self-intersections, then verifies that forcing unknown-quality Booleans to succeed
is detected by a regression test.

## External runtime layout

Never launch a corpus run in the Git checkout. First regenerate the metadata into a
new external `<base>\dataset` directory, from this directory:

```powershell
python prepare_dataset.py D:\Thingi10K-reproduction\dataset --revision 2d5d3b2f3cd3711028ad75b12788c13b25559ec6
```

This downloads metadata only, never meshes. It follows bounded pagination, checks
the pinned inventory and CSV blob/projection hashes, and refuses existing destinations
or destinations inside Git checkouts. The inventory contains all raw formats, not just
STL. The regenerated files were verified byte-identical to the original published
inventory and projection; the original large metadata files are removed from this
PR's history. This does not change the live run's metadata.

Copy the harness files and `sources` into the external `<base>\run` directory.
The runner downloads raw meshes into `<base>\dataset` from the regenerated inventory.
Supply the measured frozen DLL in `run\reference\geometry3Sharp.dll` and the two
frozen driver output directories under `run\bin\<project>`. Copy the configuration
into `run\config.json`. Published placeholders describe redacted paths only;
they are not executable build commands. Runtime provenance checks use the commit,
dataset revision and relative binary hashes.

With the frozen reference DLL, the included driver projects can be built using:

```powershell
dotnet build sources\benchmarks\BooleanComparison\BooleanComparison.csproj -c Release -o bin\BooleanComparison
dotnet build sources\benchmarks\MeshRepairBenchmark\MeshRepairBenchmark.csproj -c Release -o bin\MeshRepairBenchmark
python -u run_full.py
```

The runner rejects changed binary hashes. Exact continuation requires binaries
matching the recorded SHA256 values. Rebuilding on another path/SDK can change hashes;
do not rewrite the recorded snapshot or mix fresh binaries with its results.
For a new measurement, use a new empty run directory and a new configuration recording
its own binaries and build commands. Build the reference library from the measured
commit, not from the newer publication branch.

The runner resumes from its external JSONL, repairs only an incomplete final line,
rejects interior corruption, and retries download failures. Copying snapshot results
for exact resume is permissible only with matching provenance; preserve an untouched
copy of the published snapshot.

## Validation evidence

[Validation](validation.json) records commands and outcomes: Python regression tests,
procedural smoke, snapshot consistency/hashes, mutation checks, library builds for
net10.0/netstandard2.0, net48 test-project builds, package API validation and both copied
driver builds. [Analysis validation](analysis-validation.json) adds the input-grouping
and metadata-regeneration tests, byte-identical reconstruction, mutation checks,
and fresh publication builds/API pack.

[Pipeline validation](pipeline-validation.json) records the later Tier M change:
38 offline tests, 42 original/probe metric-equal procedural cases, four detected
runtime mutations, target builds and both API-validation packs, with commands and
external-log hashes. The pipeline was activated after securing the stopped legacy
runner's outputs: two complete jobs were adopted with unchanged operation timings,
and the original final JSONL byte prefixes and frozen provenance were verified.
The live process has separate Wrap/Boolean producers and two probe evaluators.
Its first large-job phase logs place the ongoing work in `edge_topology`; these are
unfinished phase observations, not completed timings or a speedup measurement.
The recorded activation counts are a point-in-time observation, not a new corpus
snapshot. The original interim snapshot and analysis stay unchanged.

Direct checklist review was completed without subagents. Independent review remains
outstanding; this PR stays a draft. Full .NET test suites and independent
ready-for-review assessment are not claimed by this publication.
