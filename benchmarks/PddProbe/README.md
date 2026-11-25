# PddProbe

This is the research probe of [PDD_PROBE_2026-11.md](../../docs/research/PDD_PROBE_2026-11.md). It asks whether Pointwise
Distance Distributions (Widdowson & Kurlin, arXiv:2108.04798) are a useful, deterministic shape invariant for three uses:

- near-duplicates under rigid motion and scale;
- lattice period and symmetry;
- a "same shape?" pre-check.

It is a prototype for a measurement, not a library candidate, and nothing here is used by the library. All inputs are
procedural: generator meshes, `TpmsLattice` and Gaussian clouds. There are no mesh files and no network access.

Verdict: **stop**. The simple D2 baseline beats PDD on discrimination and retrieval. The note gives the numbers and the
next candidates.

## Modes

```
dotnet run --project benchmarks/PddProbe -c Release -- disc 300      # discrimination with exact EMD and k = N-1 (550 s)
dotnet run --project benchmarks/PddProbe -c Release -- disc 2000     # discrimination, scalable variants (135 s)
dotnet run --project benchmarks/PddProbe -c Release -- retrieval 2000
dotnet run --project benchmarks/PddProbe -c Release -- retrieval 300 # adds exact EMD and k = N-1
dotnet run --project benchmarks/PddProbe -c Release -- time          # cost at 10k-200k points
dotnet run --project benchmarks/PddProbe -c Release -- determinism   # threads, runs, permutation, Similarity.All() (~6 s)
dotnet run --project benchmarks/PddProbe -c Release -- period        # TpmsLattice period / orbit probe
dotnet run --project benchmarks/PddProbe -c Release -- moments       # the candidate after the stop: exact surface moments
python3 benchmarks/PddProbe/analyze.py benchmarks/PddProbe/results/disc_300.txt   # verdicts per stratum and PDD variant
```

## What is implemented

- **kNN** by a uniform grid with ring search, plus brute force for k >= N/4. Each row depends only on the distances from
  its point, so the result is bit-identical for any thread count and input order.
- **Normalization** by the RMS radius, using sorted sums.
- **Distances between PDDs**:
  - AMD, as L_inf of the column means;
  - colW1, the maximum over columns of the 1D W1 distance, a lower bound of EMD_inf;
  - exact EMD, as the assignment problem with uniform weights, solved by the Hungarian algorithm.
- **Baselines**: D2 (sorted pairwise distances, 1D W1) and PCA (square roots of the covariance eigenvalues).
- **The `moments` mode**: the exact second moments of the surface from triangle integrals.
- **Random numbers**: SplitMix64 throughout.

## Results

`results/` holds the raw outputs on the cloud reference container (4 logical cores):

| File | Contents |
|---|---|
| `determinism.txt` | determinism checks |
| `time.txt` | cost |
| `disc_300.txt`, `disc_2000.txt` | discrimination |
| `analysis_300.txt`, `analysis_2000.txt` | verdicts per stratum, from `analyze.py` |
| `retrieval_300.txt`, `retrieval_2000.txt` | retrieval |
| `period.txt` | period / orbit probe |
| `moments.txt` | moments candidate |

The `disc` and `retrieval 2000` runs predate a refactor of the PCA baseline. That refactor computes the same quantity,
sqrt(eigenvalue) / sqrt(trace) where the RMS radius is sqrt(trace), so it differs only in rounding.
