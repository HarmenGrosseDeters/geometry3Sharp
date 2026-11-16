# Biharmonic displacement benchmark (#886)

## Larger production inputs (2026-11-16)

Strict mode now admits one active component up to 10000 vertices on the supported
net10.0 x64 scalar runtime; other runtimes retain the 400-vertex interval path.
A checked defect of an untrusted numerical correction tightens the original-data
error bound without changing output positions or either requested tolerance.
The proof and size-dependent certificate budgets are in
[BIHARMONIC_CERTIFICATE_PROOF.md](../../docs/design/BIHARMONIC_CERTIFICATE_PROOF.md#implementation-evidence).
They do not bound the preceding numerical candidate factorization or total heap bytes.

Reproduce the previous-library rejection and current production outputs with:

```sh
python3 benchmarks/BiharmonicDeformationBenchmark/large_certificate_profile.py <dotnet> <producer.dll> --expect-rejection
python3 benchmarks/BiharmonicDeformationBenchmark/large_certificate_profile.py <dotnet> <producer.dll>
python3 benchmarks/BiharmonicDeformationBenchmark/large_certificate_profile.py <dotnet> <producer.dll> --reuse
```

The first command requires the library from PR head `856a332f` (local source
commit `4893c3b3`, identical tree) with the current procedural exporter. The next
two require the current library. Raw logs: `large-certificate-before.txt`,
`large-certificate-after.txt` and `large-certificate-reuse.txt`. Fresh processes
include JIT; reuse measures five target changes and compares every result with a
separately timed fresh solve, including output and diagnostic bits. The producer
checks finite valid meshes, unchanged input/topology and exact handles. Generated
meshes are temporary. File hashes identify the outputs, not an independent proof.

Measured Compute time (ms), five changing-target runs per fixture; fresh-reference
and reuse include their allocations/GC. Every reused result matches the fresh
result bit for bit. Fresh-reference timers are warmed by earlier calls.

| Vertices, orientation | Fresh median ms | Reuse median ms |
|---|---:|---:|
| 2500, aligned | 890.457 | 251.349 |
| 2500, rotated | 1620.565 | 654.210 |
| 10000, aligned | 6851.437 | 2029.827 |
| 10000, rotated | 8691.948 | 2647.580 |

The separate one-shot cold runs including JIT took 1891/2562 ms at 2500 vertices
and 7753/10302 ms at 10000 (aligned/rotated); the previous head rejected these
inputs in 44/44/65/50 ms without solving. This is an expanded certified domain,
not a timing speedup over rejection. These are fixture measurements, not a
worst-case wall-clock guarantee; see raw logs for ranges and allocations.

All four aligned/rotated 2500/10000-vertex fixtures change from
`AccuracyNotEstablished` to `Success/Certified`. The 441-vertex tests compare to
the independently known translation solution and check same-instance reuse.
`large-certificate-validation.txt` records the targeted corruption mutant and
local checks. Independent bounds review found no concrete incorrect-certificate
path. Small production outputs retain their separate interval-verifier checks.
The 100k strict path, general-mesh acceptance and proposed 15% cold-cost budget
remain open; these measurements are not a default-path speedup claim.

## Historical production scalar integration (2026-11-16)


The reviewed scalar spectral verifier is now connected to `BiharmonicSparseCertificate.Prepare` in production. Original-data interval Q assembly, final rounded-position residuals, both Euclidean comparisons and prepared-proof invalidation are retained. On net10.0 x64 it uses exact integer correction/radius accounting and the direct scalar recurrence under nearest-even binary64/gradual-underflow execution with no native floating-point control changes during Compute. Entry probes detect changed rounding/FTZ/DAZ; other targets retain interval factorization. A failed scalar check cannot release the candidate.

The existing 400-vertex, 2000-triangle, million-total-update and 50000-live-pattern-entry budgets remain: scalar symbolic work is charged against the remainder after Q assembly using the same Schur count and peak-live convention. All 28 focused tests pass, including the supported-runtime assertion, direct correction/radius and budget rejection controls, and existing end-to-end corruption/cache tests. The production connection is complete for the current admitted domain; larger rotated inputs and the cold performance budget remain release work. Full CI of the preceding master-merge head d95440f4 passed before this integration.

## Structural scaling diagnosis and separate scalar verifier

At the time of this structural diagnosis, production was bounded to 400 vertices. `--certificate-structure <n> <natural|solver> <shift> rotated <fresh.json>` generates procedural original-data metadata without a deformation candidate. It reconstructs the complete L/Q **pattern**, including zero-valued triangle edges. Solver ordering comes from the existing SparseCholesky on a strictly diagonally dominant surrogate with the identical pattern. Only that permutation is used. Scalar elimination counts are checked against the numerical solver's symbolic factor count and, on small fixtures, an independent dense boolean elimination. This is not a deformation timing or a position certificate.

The 50-million-Schur-update diagnostic cap stops the 100x100 cases. On early exit the counters are partial lower bounds and an unresolved entry-budget result is `unknown`. The production limits are never changed by this tool.

| Rotated grid / vertices | Natural total Q/Schur updates | Solver total Q/Schur updates | Solver peak live entries |
| --- | ---: | ---: | ---: |
| 20x20 / 400 | 248292 | 187413 | 3747 |
| 30x30 / 900 | 1361077 | 1049548 | 10696 |
| 50x50 / 2500 | 11233947 | 7033172 | 39931 |
| 100x100 / 10000 | incomplete, Schur cap | incomplete, Schur cap | at least 141059 |

Thus the existing million-update production budget is already exceeded at 900 vertices; 10000 vertices start with 96030 Q entries, exceeding the 50000-entry budget before fill. In the independent interval pilot the fixed shift 0.5 is inconclusive at 2500 vertices; the smaller proposal 0.125 proves positive interval pivots. A failed pivot alone is not a proof that the original matrix is indefinite.

The separate C# arithmetic pilot now offers `mesh-scalar 256 <json> [fresh-trace.jsonl]`. It implements the already specified scalar Cholesky recurrence and Rump trace correction from the paper, with exact integer radius/correction accounting in 2^-1074 units and downward-adjusted diagonals. Products are separate non-inlined calls, division is direct. Nonfinite/subnormal results, lost nonzero products/quotients and the minimum-normal product/quotient boundary are refused conservatively (even exactly normal results at that boundary). Its reference cap is 2500 free unknowns and 50 million products/symbolic pair updates. No public library mode, production budget or accuracy behavior changes.

**Scope:** an unaudited successful scalar run assumes correctly rounded IEEE binary64 execution. It always prints `position_certificate=not_checked`, even if candidate fields are supplied. A small trace audit proves the recorded matrix's bounds and the complete scalar operation sequence, not the completeness of that matrix with respect to an external mesh or correctness on another runtime/input.

Reproduce the experiment and controls (fresh processes, JIT/GC included; not the plan's warmed-process cold acceptance metric):

```
python3 benchmarks/BiharmonicDeformationBenchmark/certificate_structure_controls.py <dotnet> <producer.dll> <checker.dll>
python3 benchmarks/BiharmonicDeformationBenchmark/scalar_certificate_controls.py <dotnet> <producer.dll> <checker.dll>
python3 benchmarks/BiharmonicDeformationBenchmark/certificate_structure_profile.py <dotnet> <producer.dll> <checker.dll> --runs 5
```

Here producer is `BiharmonicDeformationBenchmark.dll` and checker is `BiharmonicCertificateArithmetic.dll`. Raw commands use the same artifact paths as the prepared-certificate command below. `structure-scalar-profile-results.txt` binds every matrix to its input hash and both DLL hashes. Both methods operate on the same original data, order and shift. Successful scalar product counts exactly match the independent interval update counts.

| Vertices / shift | Interval phase median ms (range) | Scalar phase median ms (range) | Whole matrix-run median ms interval → scalar |
| --- | ---: | ---: | ---: |
| 400 / 0.5 | 459.536 (266.481–1289.418) | 48.634 (44.891–100.723) | 943.389 → 615.949 |
| 900 / 0.5 | 1275.145 (1026.469–2189.384) | 149.507 (112.742–934.036) | 3010.362 → 1236.832 |
| 2500 / 0.125 | 6246.097 (6033.724–16643.814) | 276.332 (265.383–354.610) | 7444.771 → 1566.827 |

The interval phase includes its allocations/GC and numerical interval factorization. The scalar phase additionally includes midpoint/radius conversion, correction and symbolic work, with its allocations/GC. Whole matrix runs include original-data interval assembly; neither method runs a production candidate solve or its final-position residual here. Wide timing ranges limit universal claims. These results justify continuing the scalar path, not production release, the 15% budget, or 10k/100k feasibility.

`scalar-controls-results.txt` records 2000 directed-conversion cases, five underflow controls and exact rational audits of 14/506/3186 operations on 3x3/5x5/7x7 fixtures. The independent dense recurrence must match every operation's order, operand/result bits and full trace length. Bad shifts, corrupted product rounding, missing trace/radius corrections, truncated traces and correctly rounded but falsely linked operations reject. `scalar-mutation-results.txt` records a source mutation omitting the trace correction, caught by the exact bound check, followed by restored bytes and green controls. Reproduce after a fresh baseline build, alone in a dedicated worktree, with `scalar_certificate_controls.py <dotnet> <producer.dll> <checker.dll> --mutation-artifacts <artifacts>`. These controls do not replace incremental Stryker or an end-to-end mesh certificate. Independent delta/bounds review found no remaining concrete defect after fixing partial-counter reporting and trace completeness.

Next: connect actual larger production candidates and final-output residuals to the scalar proof under an explicit supported arithmetic contract, then measure full cold/warm cost. The original-data interval assembly remains a measured cost, not a fundamental lower bound. Keep default guards and current strict caps until those gates pass.

## Reuse the existing numerical ordering for the interval proof

The bounded sparse verifier now takes an independent permutation snapshot from the completed numerical SparseCholesky, validates its length/range/uniqueness and permutes only the interval Q matrix. It still proves positive shifted interval pivots from original coordinates; double factors are not proof evidence. Original L/M and per-output residuals retain their original indices. Prepared hits do not clone the permutation again. Natural ordering remains the direct verifier control.

Reproduce with the `prepared_certificate_profile.py` command below, once against each library build. `ordering-{before,after}-profile.txt` records one same-machine experiment with five calls per case:

| 400-vertex strict fixture | Before median ms (min–max) | After median ms (min–max) | Median allocated bytes before → after |
| --- | ---: | ---: | ---: |
| aligned, fresh cold | 371.541 (309.485–393.827) | 280.097 (267.274–349.452) | 229769272 → 179090224 |
| aligned, paired fresh with changing targets | 340.502 (309.515–418.231) | 282.586 (241.560–453.495) | 229873496 → 179195888 |
| rotated, paired fresh with changing targets | 404.428 (382.032–546.988) | 396.178 (370.440–456.317) | 270918104 → 222112864 |
| aligned, same instance | 34.090 (28.396–76.287) | 34.912 (28.609–84.517) | 20247912 → 20249296 |
| rotated, same instance | 45.870 (45.339–78.549) | 51.369 (43.842–91.991) | 26964496 → 26964496 |

Cold allocation decreases by about 49–51 MB on these fixtures. Time ranges overlap, especially on the rotated fixture and prepared hits; this experiment does not establish a universal elapsed-time gain or no-regression guarantee. All three exported candidate hashes match before/after and pass the independent checker with both Euclidean conditions. The 400-vertex and all arithmetic/work budgets remain unchanged; this is not evidence of 10k/100k feasibility or meeting the cold budget.

All 26 focused tests pass. They include permutation validation, independent snapshot ownership, a full certificate under a guaranteed nontrivial ordering with an identical bound to natural ordering, and wrong-size rejection. Independent delta review found no concrete error. `ordering-mutation-results.txt` records a green 26-test baseline and a killed duplicate-permutation-acceptance mutant in the named validation test, followed by restored original artifacts and a green rerun. Reproduce with `targeted_certificate_mutations.py <dotnet> <artifacts> <fresh-logs> --ordering`. This targeted check does not replace the incremental mutation gate; full readiness gates remain open.

## Numerical factor reuse paired with the geometric proof

Strict sparse mode now retains the numerical Cholesky only after successful certification, paired with that exact Prepared snapshot. A hit requires the proof object identity and the complete live geometry/topology/free-map key. Each target update assembles a fresh RHS and checks the final original-data certificate. A changed proof clears the factor pair, including after a failed output check; tighter tolerances with unchanged geometry do not invalidate the matrix. Normal mode is unchanged.

`factor-reuse-{before,after}-profile.txt` records one same-machine experiment using the `prepared_certificate_profile.py` command below (five calls per case, successful independent final certificate, bitwise fresh-reference checks). The three exported candidate hashes are identical before/after.

| 400-vertex strict fixture | Before reuse median ms (min–max) | After median ms (min–max) | Median allocated bytes before → after |
| --- | ---: | ---: | ---: |
| aligned, fresh cold | 364.918 (329.468–386.327) | 365.338 (326.624–400.471) | 229769232 → 229769272 |
| aligned, same instance | 36.347 (34.263–66.164) | 33.585 (31.474–67.749) | 21788904 → 20247912 |
| rotated, same instance | 53.060 (46.316–84.314) | 47.491 (43.251–77.857) | 28505488 → 26964496 |

Elapsed ranges overlap, so this is not a universal speedup or no-regression guarantee. It shows reduced median allocation by 1540992 bytes per target update on both fixtures, with cold cost effectively unchanged in this experiment. Retaining a factor also retains its matrix/factor storage; the table reports allocation during calls, not retained heap size. The strict cold-budget and larger-mesh problems remain open.

The 25 tests compare reused versus fresh results after target/handle changes, rest-position edits and restoration, winding changes, mesh replacement and a failed tighter tolerance followed by recovery. Internal identity assertions check that the factor object persists for same/target-only cases and changes with the handle mask. `factor-reuse-mutation-results.txt` records a green baseline and a killed `disable_factor_reuse` mutant; the named test must fail and original source/artifact bytes are restored. Reproduce after a fresh baseline build with `targeted_certificate_mutations.py <dotnet> <artifacts> <fresh-logs> --factor`. This targeted identity mutation does not replace Stryker or prove the absence of redundant Factorize calls on the same object; code review also checks that Factorize is skipped on a hit.

## Prepared geometric proof: reuse and invalidation

The strict sparse deformer now retains an opaque original-data L/M and positive-shift proof. Its key is checked against live mesh identity, exact position bits, vertex IDs/free map and every triangle ID/index/count. New targets/output residuals are always verified. At that stage only the geometric certificate was cached; numerical factor reuse is now added as described above.

`--certificate-reuse-profile` requires a successful strict sparse fixture (now 17–10000 vertices on the supported scalar runtime). It prepares the same instance once, then measures five calls with new right-boundary targets and unchanged handle IDs. Each result is compared to a separately timed fresh-deformer reference for the identical targets; metadata, vertex bits and topology must match. The final export is also hash-bound to the independent checker. The first call is explicitly labeled as including JIT and is not comparable to warmed cold timing. Reference work is outside each reuse timer and allocation sample; process peak memory includes all work. The profile measures same-instance reuse; direct tests separately assert that the same Prepared object is retained for target-only changes.

```
DOTNET_CLI_HOME=/workspace/dotnet-user python3 benchmarks/BiharmonicDeformationBenchmark/prepared_certificate_profile.py /workspace/dotnet/dotnet /workspace/arap-886-artifacts/bin/BiharmonicDeformationBenchmark/release/BiharmonicDeformationBenchmark.dll /workspace/arap-886-artifacts/bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll
```

One local five-run paired experiment (`prepared-certificate-profile-results.txt`):

| Strict 400-vertex fixture | Same-instance median ms (min–max) | Paired fresh median ms (min–max) | Allocated bytes reuse / fresh |
| --- | ---: | ---: | ---: |
| aligned | 34.161 (32.020–67.966) | 362.550 (330.866–398.640) | 21788904 / 229873456 |
| rotated | 64.845 (47.794–82.446) | 464.181 (449.428–465.431) | 28505488 / 270918064 |

The separate warmed cold measurement was 402.633 ms median (332.451–479.408). These data establish a reuse benefit on these fixtures, not a solution to cold cost or 10k/100k certification. Cold numerical work and all per-target validation still run.

25 focused tests pass, covering geometry, topology, mesh identity, free-map and target changes, an intentionally stale original-position array, and fresh-output checking with a retained proof. `prepared-certificate-mutation-results.txt` records a green 25-test baseline and kills for skip-accuracy, omit-z and skip-cache-key; the named expected tests must fail, and original source/artifact bytes are restored and checked afterward. These are targeted hand mutations, not completion of the incremental Stryker gate.

## Cold profile and exact endpoint-limit optimization

`--certificate-profile` performs one warmup then five sequential fresh-deformer Computes. Input generation, output comparisons and export are outside the timer; uncached validation, assembly, solve and verification are inside. It checks status/accuracy/iteration/residual/bound metadata, vertex bits and topology across repetitions. Allocation is process-wide cumulative allocation during each Compute; PeakWorkingSet is the process high-water mark including generation and warmup. Only successful, independently certified candidates are compared.

```
DOTNET_CLI_HOME=/workspace/dotnet-user python3 benchmarks/BiharmonicDeformationBenchmark/cold_certificate_profile.py /workspace/dotnet/dotnet /workspace/arap-886-artifacts/bin/BiharmonicDeformationBenchmark/release/BiharmonicDeformationBenchmark.dll /workspace/arap-886-artifacts/bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll
```

One same-machine before/after experiment replaces per-interval `ToByteArray().Length` checks with exact cached bounds -2^4095 and 2^4095-1, representing the same signed 512-byte range. Independent review checked equivalence and tests cover both accepted endpoints and their first excluded neighbors. Logs are `cold-profile-{before,after}-endpoint-check.txt`.

| 400-vertex fixture | Before median ms (min–max) | After median ms (min–max) | Median allocated bytes before → after |
| --- | ---: | ---: | ---: |
| aligned, normal | 18.875 (11.476–34.180) | 18.883 (11.998–31.602) | 2138368 → 2138368 |
| aligned, strict | 463.393 (430.370–531.891) | 362.351 (355.591–453.313) | 329410312 → 229735744 |
| rotated, strict | 544.109 (506.058–597.811) | 475.917 (438.087–531.921) | 380574728 → 270779496 |

All profiled candidate hashes match the pre-optimization production candidates in `sparse-production-certificate-results.txt`. Both Euclidean checks pass in the separate verifier. This demonstrates fewer allocations and lower medians for these strict fixtures, not a universal runtime or peak-memory improvement. The strict cold path still greatly exceeds the proposed 15% normal-path budget; this does not establish 10k/100k feasibility.

## Targeted certificate mutations and reuse

The 25-vertex verifier test now repeats Compute, changes a target and adds an interior handle, then compares against a fresh instance and exact handles. `certificate-mutation-results.txt` records two hand mutants: skipping final accuracy acceptance and omitting z from the residual-error sum. Each is killed by both direct verifier-negative tests; the original 24-test baseline passes before and after restoring artifacts.

For reproduction, first freshly build the original library and test project, then run the script alone in a dedicated worktree with fresh logs:

```
python3 benchmarks/BiharmonicDeformationBenchmark/targeted_certificate_mutations.py <dotnet> <artifacts-directory> <fresh-log-directory>
```

The script requires identical original library/test-copy DLLs and existing PDBs, checks its own green baseline, and requires the named expected verifier test to fail in TRX for each mutant. It temporarily modifies one source file and the two library copies and restores original bytes in finally. A hard process kill can prevent restoration. These two hand mutants are targeted evidence and do not replace the incremental Stryker/bounds readiness gate.

## Independent check of production candidates

The bounded sparse production verifier now extends strict mode to at most 400 vertices in one active component. `sparse-production-certificate-results.txt` records the updated harness: the actual rotated 20x20 strict output is `Success/Certified` and the separate fixed-point pilot confirms both Euclidean conditions for that same hash-bound candidate. The normal mode still rejects the fixture, retaining its previous loss guards. Corrupted x/y/z outputs and excessive shifts still reject. The older `production-certificate-results.txt` below documents the earlier limit and remains historical evidence. No 10k/100k certification or default-path speedup follows from this bounded reference integration.

`--certificate` builds an explicit single-component procedural DMesh3, snapshots input positions and triangle IDs before `Compute`, and compares input and output topology/IDs and input bits after it. It serializes the actual output positions, handle targets and original input bits to a fresh JSON path; it never overwrites an existing path or exports a failed solve. The verifier rebuilds its own interval matrix and residual from those bits. The shift 0.5 is an untrusted proposal: the verifier independently proves or rejects it. The exporter is limited to compact generated grid IDs, not a general DMesh3 certificate API.

Run after building `BiharmonicDeformationBenchmark` and `BiharmonicCertificateArithmetic` in Release:

```
DOTNET_CLI_HOME=/workspace/dotnet-user python3 benchmarks/BiharmonicDeformationBenchmark/production_certificate_controls.py /workspace/dotnet/dotnet /workspace/arap-886-artifacts/bin/BiharmonicDeformationBenchmark/release/BiharmonicDeformationBenchmark.dll /workspace/arap-886-artifacts/bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll
```

`production-certificate-results.txt` records a single local run, executable/library/source hashes, SDK, candidate hashes and the dirty source base; the hashes identify executed artifacts but do not by themselves prove which sources were used to build them. The independent verifier certifies actual 4x4 aligned/rotated outputs and the actual 20x20 aligned production output against the Euclidean absolute and component-relative contract. The harness requires the explicit certificate and solver statuses in addition to exit codes. All three separately corrupted free coordinates and an excessive trial shift reject; an existing export is not overwritten. In that historical run, the 20x20 rotated solver returned `NumericalRange` in normal mode and `AccuracyNotEstablished` in strict mode; the later sparse integration described above now certifies strict mode. The 20x20 aligned verifier takes about 1.33 s and 190 MB peak process working set in this single run, excluding solver/export time. This is not evidence of large-mesh performance or production integration; the checker consumes JSON and validates one listed connected component rather than a live arbitrary DMesh3. Independent review checked the snapshot, topology, handles and exporter.

On the rotated 20x20 input, `ProductionAssemblyDiagnostic` replays the assembly loss guards after verifying that `Compute` did not mutate the input. It reproduces a row guard at triangle 0, row/column (0,0): the accumulated value is approximately -3.035e-17 and adding 0.5000000000000001 rounds to the latter. The replay checks the preceding finite area and weights but is not an instrumented production execution; other NumericalRange paths exist. This is evidence for the guard mechanism, not evidence that discarding the smaller term is safe. The strict-mode rejection is independently caused by its current whole-mesh fallback cap. Independent review checked the replay against the compact single-component fixture.

As a targeted harness mutation, replacing the checker process with one that exits 0 but prints `position_certificate=not_checked` makes the harness fail on its missing Euclidean/certificate markers. This verifies that exit code alone cannot be counted as a certificate; the same harness checks the checker's input hash against the produced candidate file.

The procedural grid fixes the left boundary and displaces the right boundary in-plane and sinusoidally out of plane. The timer includes validation, assembly, factorization, the three solves, residual check and output copying; grid generation is outside it. The program checks finite output and exact handles. The allocation figure is cumulative managed allocation, not peak memory. The hash covers vertex IDs/positions and triangle IDs/indices, not mesh attributes.

Run on Linux x64, .NET SDK 10.0.403, 2026-11-15, base `41e6c0f0` with this implementation. One run per size, with environment variable `DOTNET_GCHeapHardLimit=0xC0000000`; raw output in `results.txt`:

| Grid | Status | Time | Peak process working set |
| --- | --- | ---: | ---: |
| 100 x 100 | Success | 498.5 ms | 114 MB |
| 400 x 250 | Success | 4193.1 ms | 852 MB |

```
dotnet run --project benchmarks/BiharmonicDeformationBenchmark -c Release -- 100 100
dotnet run --project benchmarks/BiharmonicDeformationBenchmark -c Release -- 400 250
```

These are single measurements of this mesh and include one direct solve, not a general speed comparison with ARAP. The explicit matrix and factorization can require considerably more memory for other topologies; the assembly contribution cap does not limit factorization fill.
