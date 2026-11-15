# Biharmonic displacement benchmark (#886)

## Independent check of production candidates

The bounded sparse production verifier now extends strict mode to at most 400 vertices in one active component. `sparse-production-certificate-results.txt` records the updated harness: the actual rotated 20x20 strict output is `Success/Certified` and the separate fixed-point pilot confirms both Euclidean conditions for that same hash-bound candidate. The normal mode still rejects the fixture, retaining its previous loss guards. Corrupted x/y/z outputs and excessive shifts still reject. The older `production-certificate-results.txt` below documents the earlier limit and remains historical evidence. No 10k/100k certification or default-path speedup follows from this bounded reference integration.

`--certificate` builds an explicit single-component procedural DMesh3, snapshots input positions and triangle IDs before `Compute`, and compares input and output topology/IDs and input bits after it. It serializes the actual output positions, handle targets and original input bits to a fresh JSON path; it never overwrites an existing path or exports a failed solve. The verifier rebuilds its own interval matrix and residual from those bits. The shift 0.5 is an untrusted proposal: the verifier independently proves or rejects it. The exporter is limited to compact generated grid IDs, not a general DMesh3 certificate API.

Run after building `BiharmonicDeformationBenchmark` and `BiharmonicCertificateArithmetic` in Release:

```
DOTNET_CLI_HOME=/workspace/dotnet-user python3 benchmarks/BiharmonicDeformationBenchmark/production_certificate_controls.py /workspace/dotnet/dotnet /workspace/arap-886-artifacts/bin/BiharmonicDeformationBenchmark/release/BiharmonicDeformationBenchmark.dll /workspace/arap-886-artifacts/bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll
```

`production-certificate-results.txt` records a single local run, executable/library/source hashes, SDK, candidate hashes and the dirty source base; the hashes identify executed artifacts but do not by themselves prove which sources were used to build them. The independent verifier certifies actual 4x4 aligned/rotated outputs and the actual 20x20 aligned production output against the Euclidean absolute and component-relative contract. The harness requires the explicit certificate and solver statuses in addition to exit codes. All three separately corrupted free coordinates and an excessive trial shift reject; an existing export is not overwritten. The 20x20 rotated solver still returns `NumericalRange` in normal mode and `AccuracyNotEstablished` in strict mode, so no certificate is claimed for it. The 20x20 aligned verifier takes about 1.33 s and 190 MB peak process working set in this single run, excluding solver/export time. This is not evidence of large-mesh performance or production integration; the checker consumes JSON and validates one listed connected component rather than a live arbitrary DMesh3. Independent review checked the snapshot, topology, handles and exporter.

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
