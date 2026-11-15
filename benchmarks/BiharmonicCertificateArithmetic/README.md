# Experimental binary interval arithmetic and LDLᵀ

## Euclidean accuracy contract

The `mesh` mode independently rebuilds L/M/Q from its binary64 JSON fixture and now checks both the per-vertex absolute Euclidean error and the component-wide relative free-response condition in [BIHARMONIC_CERTIFICATE_PROOF.md](../../docs/design/BIHARMONIC_CERTIFICATE_PROOF.md). Squared comparisons use integer interval endpoints and conservative lower bounds for the rest radius and candidate response. The earlier per-axis tolerance remains an additional conservative gate. The fixed-point tolerance is the lower enclosure of binary64 `1e-8`; the Python pilot now uses that same binary64 value. An independent bounds review checked the integer scales and component assumptions.

The JSON mesh mode requires that its supplied vertices form one connected component; it checks connectivity from faces and rejects disconnected inputs. Index zero remains the local rest anchor. It cannot prove that the exported list and order match an external DMesh3, or that its candidate came from the library. Those remain integration requirements; this executable is still a pilot.

After building the project, run a reproducible Python/.NET comparison, tiny200 regression and disconnected control with:

```
DOTNET_CLI_HOME=/workspace/dotnet-user python3 benchmarks/BiharmonicCertificateArithmetic/run_euclidean_controls.py /workspace/dotnet/dotnet /workspace/arap-886-artifacts/bin/BiharmonicCertificateArithmetic/release/BiharmonicCertificateArithmetic.dll
```

The script writes generated JSON only to a temporary directory. The recorded single run is in `euclidean-results.txt`: both implementations accept the 20x20 xyz fixture, reject the lost tiny200 response (absolute passes, relative fails), and the C# pilot rejects a disconnected input. Its 20x20 C# mesh check took about 1.26 s and 191 MB peak working set, excluding Python fixture generation; this is not an end-to-end library benchmark or a large-mesh guarantee. Independent review checked the connectivity and bounds comparisons. Existing x/y/z, handle and over-shift controls also reject on the current build.

This standalone .NET 10 probe is **not used by the library**. It represents interval endpoints as arbitrary-size integers with a fixed binary fractional scale. Arithmetic rounds outward; square roots use integer square root. The probe checks multiplication, division, square-root and binary64 conversion enclosures with exact integer inequalities on 10,000 deterministically generated mixed-scale cases per precision. It also checks zero-crossing division, exact square roots and decimal endpoint order. An independent reviewer inspected the arithmetic and factorization.

```
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- 64
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- 128
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- 512
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- 1024
```

`arithmetic-results.txt` records one Linux x64 / .NET SDK 10.0.403 run. The timings include the random generation and exact reference checks; they are not the cost of a single arithmetic operation or a mesh solve.

The optional `factor` mode accepts an interval matrix exported by the independent Python pilot. It converts each decimal interval outward to fixed-point endpoints and applies sparse LDLᵀ updates. Positive lower pivot bounds prove positive definiteness of every symmetric matrix enclosed **by the imported entries**. This conclusion is conditional on the Python export correctly and completely enclosing the shifted mesh matrix: the C# probe does not independently assemble the mesh, verify omitted entries, or validate that the printed `shift_binary64` was subtracted. SHA-256 identifies only the exact file bytes consumed; it does not prove export correctness or completeness. The Python and C# paths were reviewed separately.

Example, with Python, NumPy, SciPy and mpmath installed (the last is used by the companion feasibility probe):

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0 indexed /tmp/biharmonic20.json
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- factor 256 /tmp/biharmonic20.json
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 30 80 0.5 0 indexed /tmp/biharmonic30.json
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- factor 256 /tmp/biharmonic30.json
```

`ldl-results.txt` contains the raw outputs: the 20×20 export at 256 bits and 30×30 export at 256/512 bits have positive pivots; 30×30 at 128 bits and a 20×20 matrix shifted above the numerical eigenvalue estimate are inconclusive/rejected. One 30×30 factorization took about 1.1 s at 256 bits and 2.0 s at 512 bits, excluding Python assembly, JSON transfer, decimal conversion and process setup. These are single measurements on one grid. They do not certify the mesh output, the residual, other inputs, or scalability to large meshes.

## Independent matrix assembly and candidate check

The `mesh` mode ignores `entries` and reconstructs cotangents, barycentric masses, the permuted shifted matrix, and the free-vertex residual independently from the binary64 coordinate, topology, constraint, scale and shift fields. It checks the permutation, exact fixed-handle bits, and all three coordinate axes. Positive pivots plus the outward-rounded residual norm give a normalized coordinate-error bound. If candidate or target fields are missing, it prints `position_certificate=not_checked`; a positive pivot result alone is not a position certificate.

The proof applies to the mesh, targets and candidate **described in the JSON bytes**. A SHA-256 digest binds outputs to those bytes, but does not prove that those bytes contain every face/handle of an external library mesh, nor does this prototype check mesh validity against another source. Before production, assemble directly from the library input, independently verify the symbolic pattern is complete, and enforce resource/precision limits.

The Python and C# programs can also export each pivot interval. A comparison uses exact rational arithmetic, checks the expected count and requires both pivot files to carry the SHA-256 of the same final JSON. Interval overlap is a consistency check between the two implementations, not a proof that they describe the intended external mesh. Run the producing commands successfully before comparing:

```
OPENBLAS_NUM_THREADS=1 python3 benchmarks/BiharmonicDeformationBenchmark/certificate_probe/interval_ldl_pilot.py 20 40 0.5 0 indexed /tmp/biharmonic20.json /tmp/python-pivots20.txt
dotnet run --project benchmarks/BiharmonicCertificateArithmetic -c Release -- mesh 256 /tmp/biharmonic20.json /tmp/csharp-pivots20.txt
python3 benchmarks/BiharmonicCertificateArithmetic/compare_pivots.py /tmp/python-pivots20.txt /tmp/csharp-pivots20.txt 256 /tmp/biharmonic20.json
```

`mesh-results.txt`, `pivot-comparison-results.txt` and `control-results.txt` contain one local run. The 20×20, 30×30, 40×40 and 50×50 fixtures used 256, 256, 512 and 768 binary fractional bits respectively. Each independently rebuilt C# matrix had positive pivot intervals, the all-axis candidate bound was within 1e-8, and all 360, 840, 1,520 and 2,400 Python/C# pivot intervals overlapped. The 50×50 C# run took about 27 seconds including JSON reading, independent assembly, factorization and residual checks, with about 241 MB peak process working set; the separate Python generation is additional. These are single runs, not a 100k performance claim.

`make_controls.py /tmp/biharmonic20.json /tmp/biharmonic-controls` creates modified candidates, a missing-candidate case, a matrix-entry removal and a tiny scale. On the recorded fixture, the x/y/z and handle mutations must return exit 2; missing candidate must print `not_checked`, the ignored-matrix case must reproduce all pivots, and tiny scale must return `uncertain`/exit 1. Mutations that are too small for another input can legitimately still certify, so check the actual status; a changed bit pattern alone does not establish a meaningful negative test. Separate controls reject mismatched hashes, truncated pivot files and an overlarge shift.
