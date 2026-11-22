# ReducerBenchmark

Cost of the `Reducer`'s certified deviation check (`MaxDeviation`, `TriangleDeviationBounds`). Each mesh is
reduced twice:
- **with check**: with `MaxDeviation`, until no collapse stays within the tolerance;
- **without check**: without it, to the same triangle count.

The difference in time is the most a faster containment check could save. The deviation is the certified
one-sided Hausdorff distance from the result to the input (`MeshHausdorffDistance`, upper bound, to 1 % of the
tolerance).

```
dotnet run -c Release --project benchmarks/ReducerBenchmark
```

Meshes (generated in the code, no mesh files):
- **bumpy sphere**: a unit sphere (`Sphere3Generator_NormalizedCube`, 130 vertices per edge) with smooth bumps.
- **terrain**: a 300 x 300 heightfield on the unit square, with hills and two ripple frequencies.

The tolerance is relative to the bounding box diagonal. Run on a Linux container, `net10.0`, Release.

## Results (October 2026)

| mesh | triangles | tolerance | with check: triangles | time with check | time without, same count | check share | deviation without check |
|---|---|---|---|---|---|---|---|
| bumpy sphere | 199692 | 0.001 | 1670 | 34476 ms | 1073 ms | 97 % | 1.68 x tolerance (with check: 1.01) |
| bumpy sphere | 199692 | 0.003 | 512 | 17813 ms | 934 ms | 95 % | 1.82 x tolerance (with check: 1.01) |
| bumpy sphere | 199692 | 0.01 | 152 | 12821 ms | 1038 ms | 92 % | 1.55 x tolerance (with check: 1.00) |
| terrain | 180000 | 0.001 | 3123 | 24206 ms | 963 ms | 96 % | 2.83 x tolerance (with check: 1.01) |
| terrain | 180000 | 0.003 | 923 | 13522 ms | 905 ms | 93 % | 2.35 x tolerance (with check: 1.01) |
| terrain | 180000 | 0.01 | 121 | 11808 ms | 950 ms | 92 % | 3.28 x tolerance (with check: 1.00) |

Findings:
- The check takes 92-97 % of the reduction time: reducing with it is 12-32 times slower.
- It is needed. Without it, the same triangle count deviates 1.5-3.3 times the tolerance.
- With it, the result stays within the tolerance. 1.01 is the 1 % accuracy of the measurement.

## Exact envelope instead of the distance bounds

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- queries
```

Compares the cost of one query: the triangles of a reduction with the distance bounds check are checked again,
with `TriangleDeviationBounds.IsWithin` and with `MeshEnvelope.ContainsTriangle` at the same tolerance. These
are the triangles the envelope would have to accept if it replaced the check.

Terrain 40 x 40 (3200 triangles), tolerance 0.003, 951 reduced triangles:

| check | within | time per triangle |
|---|---|---|
| distance bounds (`IsWithin`) | 951 | 62 µs |
| envelope (`ContainsTriangle`) | 625 | 7109 µs (728 exact) |

Findings:
- Reduced triangles span many prisms, and no single prism holds them. The floating-point filters cannot decide,
  so the exact clipping runs, about 115 times slower than the distance bounds.
- The envelope accepts fewer triangles, because its prisms are thinner than the tolerance ball.
- An earlier try of the envelope as the `Reducer`'s check (not kept) took 91 s instead of 0.84 s on this
  terrain. Sampling points on the triangle first rejected only a few of the queries.

## Profiling the distance bounds check

Terrain of the same kind, 150 x 150 cells (45000 triangles), tolerance 0.003, reduced to 903 triangles in
about 5.3 s:

- 94 % of the checked collapses are accepted (22300 of 23670). Each acceptance certifies the 6-8 new triangles
  around the vertex: 155800 `IsWithin` calls, 1.36 million nearest-triangle searches, about 34 µs per call.
- Of the reduction time, `IsWithin` takes 74 %: the polygon bounds 31 %, nearest-triangle searches 30 %,
  separating planes 6 %.

Tried, without a gain (same result, 903 triangles):

| change | time |
|---|---|
| none (parallel check of the new triangles) | 5.3 s |
| serial check with early exit | 2.8 s instead of 2.5 s (20000 triangles): slower |
| `MaxDeviationDepth` 8 / 6 / 4 instead of 10 | 5.2 / 5.5 / 5.5 s (depth 4: 913 triangles) |
| cache of the nearest-triangle searches by point | 5.8-6.3 s |

The check is not slow because of deep subdivision or repeated searches, but because every accepted collapse
certifies all its new triangles. A large speedup would need a different certification. The exact envelope is
not that either: even with floating-point filters in its clipping, about 20-50 µs per triangle can be expected
(estimate, not measured). Its gain would be exactness and platform independence, not speed.

## Shared deviation queries (`share [rounds]`)

`Reducer.ShareDeviationQueries` (on by default, internal) shares the BVH queries of one collapse's `MaxDeviation`
check through a `TriangleDeviationBounds.QueryCache`. The new vertex is a corner of all of the collapse's about 7 new
triangles, and the other corners and edge points are shared by two of them. The Reducer's own samples (the new
vertex, centroids, edge midpoints) are the same points. Only points on a triangle's original edges go through the cache;
interior subdivision points never repeat, so they skip it.

The results are the same, bit for bit; `share` throws if not. Before the change a spike measured 37-61 % of
`IsWithin`'s queries in a collapse as duplicates, with queries about a third of a call.

Alternating rounds, 3 each, full reductions to 4 triangles (ms, sorted):

| mesh | tolerance | triangles | without sharing | with sharing | median speedup |
|---|---|---|---|---|---|
| bumpy sphere (199692) | 0.001 | 1670 | 27006 / 27072 / 28219 | 25814 / 25915 / 27008 | 1.04× |
| bumpy sphere | 0.003 | 512 | 14418 / 14949 / 15299 | 13062 / 13983 / 14004 | 1.07× |
| bumpy sphere | 0.01 | 152 | 9448 / 9799 / 10486 | 9091 / 9400 / 9741 | 1.04× |
| terrain (180000) | 0.001 | 3123 | 18507 / 18818 / 19617 | 17956 / 18342 / 18777 | 1.03× |
| terrain | 0.003 | 923 | 10248 / 10373 / 10697 | 9192 / 9380 / 10286 | 1.11× |
| terrain | 0.01 | 121 | 7555 / 7811 / 8651 | 6955 / 7074 / 7147 | 1.10× |

(after the review fixes: the cache is made only after the new vertex's own query passes, with that result put in.
The first run, before them, gave 1.03-1.12×.)

`hashes` gives the SHA-256 (first 16 hex digits) of every reduced mesh, over its vertex ids and coordinates bit for
bit and its triangles. They are the same before (base 9d0a11e) and after:

| mesh | 0.001 | 0.003 | 0.01 |
|---|---|---|---|
| bumpy sphere | 316E9F66F671A50C | 12C9463B79A42156 | FE60CC69142232FC |
| terrain | 9049F4270ED99B4B | BBF23739034D0171 | B68A5666CD663A57 |

With `Reducer.PreventFoldOver` (default on since finding S1, section "Fold check" below) `hashes` gives (triangles in
brackets where the count changed: bumpy sphere 0.01 was 152, terrain 0.001 / 0.003 / 0.01 were 3123 / 923 / 121):

| mesh | 0.001 | 0.003 | 0.01 |
|---|---|---|---|
| bumpy sphere | 316E9F66F671A50C | 01444CB77C6BFFD8 | 2458A2BEBD4A4DD4 (150) |
| terrain | 33D1CAD15BB93B35 (3121) | A923C4FC33897780 (924) | 4541EF545EB850C5 (117) |

Caching every query point, interior ones included, gave 0.96-1.12×: the cache's cost ate the gain at tight
tolerances.

## Polyhedral envelope (`queries`, docs/design/ENVELOPE.md §6)

`queries` also runs `PolyhedralEnvelope.ContainsTriangle` (internal; paper polyhedra with δ = ε / √3). It now also
reports the bumpy sphere case. Each entry is triangles accepted, then µs per triangle:

| case | `IsWithin` | `MeshEnvelope` | ε/√3 naive (#268, first commit) | ε/√3 accelerated | ε/√3 + review speed-ups (2 runs) | 0.8 / 0.6 prism (measured once, 9472f19) |
|---|---|---|---|---|---|---|
| terrain 40 × 40 | 951, 48-53 | 625, 6425 | 164, 351 | 164, 359 | 164, 231 / 301 | 625, 6206 |
| bumpy sphere | 536, 229-303 | 258, 32826 | 41, 17175 | 41, 1708 | 41, 1263 / 1388 | 258, 33375 |

The stop rule was not met (within 5× of `IsWithin` and no fewer accepted than `MeshEnvelope`), so the work stopped (owner decision,
ENVELOPE.md §6).

## Reduction driven by the exact envelope (`envelope`, November 2026)

Research spike for HP 10 ([docs/research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md](../../docs/research/ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md)):
a whole reduction with `PolyhedralEnvelope.ContainsTriangle` (Wang et al. 2020, internal) as the collapse check, in a
`Reducer` subclass of the benchmark (`EnvelopeSimplification.cs`), against the built-in `MaxDeviation`.

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope bracket            (A and C only: the upper bound)
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope full "terrain 40"  (also "bumpy sphere 20", "cube 10")
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope psi "bumpy sphere 20"
```

- **A**: `Reducer { MaxDeviation = eps }`. **C**: the same at 0.9 eps / sqrt 3, the distance envelope inside the polyhedra,
  so A and C bracket what the envelope can reach. **B**: the envelope (point samples first, then `ContainsTriangle`).
- **control**: the hook with `IsWithin`; "same" means its result equals A bit for bit (SHA-256).
- Linux container, 4 cores, net10.0, Release, one run per entry; times in ms, the envelope's construction included.
  Hausdorff distances are certified upper bounds to 0.01 eps.
- "B without samples" was run for the terrain only (`full0`).
- The topology column of the bumpy sphere and cube rows was printed empty (an empty `NotCleanReason`, which means clean,
  before the label was fixed); it is written out here.

| mesh | eps / diag | A: triangles, ms | C: triangles | B (envelope, with samples): triangles, ms | B without samples: triangles, ms | B / A triangles | B / A time | control (hook + IsWithin): ms, result vs A | A: h(R,M) / h(M,R) in eps | B: h(R,M) / h(M,R) in eps | A's triangles in the envelope | triangle checks B / control | checked position differs | topology A / B |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| terrain 40 | 0.001 | 2770, 283 | 3153 | 3138, 388 (build 61) | 3138, 1409 | 1.13 | 1.4 | 141, same | 1.01 / 1.02 | 0.57 / 0.68 | 1897 of 2770 | 367 / 2993 | 0 | valid (open) / valid (open) |
| terrain 40 | 0.003 | 951, 248 | 1761 | 1590, 3133 (build 16) | 1590, 5035 | 1.67 | 12.6 | 266, same | 1.00 / 1.03 | 0.65 / 0.61 | 166 of 951 | 7392 / 8910 | 0 | valid (open) / valid (open) |
| terrain 40 | 0.01 | 104, 223 | 244 | 201, 21726 (build 13) | 201, 25141 | 1.93 | 97.5 | 230, same | 0.99 / 1.03 | 0.64 / 0.70 | 15 of 104 | 11166 / 11268 | 0 | valid (open) / valid (open) |
| bumpy sphere 20 | 0.001 | 1740, 667 | 2762 | 2630, 3354 (build 81) | - | 1.51 | 5.0 | 457, same | 1.01 / 1.01 | 0.58 / 0.58 | 454 of 1740 | 7745 / 10707 | 0 | closed, manifold / closed, manifold |
| bumpy sphere 20 | 0.003 | 528, 570 | 1116 | 994, 7397 (build 23) | - | 1.88 | 13.0 | 552, same | 1.01 / 1.02 | 0.60 / 0.59 | 42 of 528 | 12538 / 13756 | 0 | closed, manifold / closed, manifold |
| bumpy sphere 20 | 0.01 | 148, 338 | 296 | 270, 20943 (build 22) | - | 1.82 | 61.9 | 331, same | 1.00 / 1.00 | 0.59 / 0.58 | 23 of 148 | 14516 / 14858 | 0 | closed, manifold / closed, manifold |
| cube 10 | 0.001 | 12, 3665 | 12 | 12, 141337 (build 26) | - | 1.00 | 38.6 | 19656, same | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 | 63638 / 63638 | 0 | closed, manifold / closed, manifold |
| cube 10 | 0.003 | 12, 2554 | 12 | 12, 149954 (build 5) | - | 1.00 | 58.7 | 12561, same | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 | 63638 / 63638 | 0 | closed, manifold / closed, manifold |
| cube 10 | 0.01 | 12, 1262 | 12 | 12, 149722 (build 7) | - | 1.00 | 118.6 | 5495, same | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 | 63638 / 63638 | 0 | closed, manifold / closed, manifold |

Per query (the queries of B, every fourth collapse, replayed serially; best of 3 for the envelope):

| mesh | eps / diag | queries replayed | envelope µs (mean) | box scan µs (mean) | IsWithin µs (mean) | rejected at a vertex | one polyhedron holds it | full test: in | full test: out | envelope µs by class (vertex / one / in / out) | IsWithin accepts | envelope accepts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| terrain 40 | 0.001 | 100 | 556 | 4 | 21 | 0 % | 23 % | 68 % | 9 % | - / 7 / 810 / 39 | 98 % | 91 % |
| terrain 40 | 0.003 | 1815 | 908 | 4 | 22 | 0 % | 3 % | 89 % | 8 % | - / 7 / 1009 / 85 | 100 % | 92 % |
| terrain 40 | 0.01 | 2767 | 4377 | 5 | 20 | 0 % | 11 % | 87 % | 2 % | - / 7 / 4952 / 4306 | 100 % | 98 % |
| bumpy sphere 20 | 0.001 | 1962 | 832 | 6 | 46 | 0 % | 2 % | 89 % | 9 % | - / 8 / 929 / 74 | 100 % | 91 % |
| bumpy sphere 20 | 0.003 | 3174 | 1281 | 7 | 43 | 0 % | 3 % | 95 % | 2 % | - / 8 / 1346 / 230 | 100 % | 98 % |
| bumpy sphere 20 | 0.01 | 3626 | 3008 | 7 | 22 | 0 % | 12 % | 88 % | 0 % | - / 9 / 3397 / 4598 | 100 % | 100 % |
| cube 10 | 0.001 | 3168 | 5431 | 3 | 783 | 0 % | 2 % | 98 % | 0 % | - / 7 / 5562 / - | 100 % | 100 % |
| cube 10 | 0.003 | 3168 | 5674 | 1 | 538 | 0 % | 2 % | 98 % | 0 % | - / 6 / 5811 / - | 100 % | 100 % |
| cube 10 | 0.01 | 3168 | 5686 | 3 | 153 | 0 % | 2 % | 98 % | 0 % | - / 6 / 5823 / - | 100 % | 100 % |

Bracket on the larger inputs (`envelope bracket`):

| mesh | triangles | eps / diag | A: triangles, ms | C: triangles, ms | C / A triangles |
|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.001 | 1706, 3528 | 3616, 3263 | 2.12 |
| bumpy sphere 40 | 18252 | 0.003 | 518, 2119 | 1062, 3072 | 2.05 |
| bumpy sphere 40 | 18252 | 0.01 | 146, 947 | 292, 1498 | 2.00 |
| terrain 80 | 12800 | 0.001 | 2941, 1503 | 6524, 1004 | 2.22 |
| terrain 80 | 12800 | 0.003 | 932, 943 | 1627, 1239 | 1.75 |
| terrain 80 | 12800 | 0.01 | 117, 615 | 340, 942 | 2.91 |

With `PreventSelfIntersections` (`envelope psi`, eps = 0.003 of the diagonal):

| mesh | eps / diag | A + PSI: triangles, ms, NotCleanReason | B + PSI: triangles, ms, NotCleanReason | B: in the envelope | B / A triangles | B / A time |
|---|---|---|---|---|---|---|
| bumpy sphere 20 | 0.003 | 528, 1029, clean | 994, 8098, clean | 994 of 994 | 1.88 | 7.9 |
| cube 10 | 0.003 | 12, 2521, clean | 12, 157183, clean | 12 of 12 | 1.00 | 62.3 |

Findings:
- The envelope reduction is guaranteed (every result triangle is inside the envelope; measured deviation 0.57-0.65 eps),
  but it stops at 1.13-1.93x the triangles of A on the curved inputs and takes 5-98x the time in 5 of the 6 curved
  runs (1.4x in terrain 40 at 0.001, which barely reduces and where A's single run looks inflated: 2.8x against the
  control). Cube: the same 12 triangles, 39-119x; 7-27x against the control, which checks before the flip test and
  has no shared query cache.
- 77-98 % of its queries run the full test (0.8-5.8 ms when it ends inside); the paper's fast path (one polyhedron
  holds the triangle, 6-9 µs) decides 2-23 %. The linear box scan is 1-7 µs. The envelope's time is the best of 3
  passes, `IsWithin`'s one pass.
- `envelope full` on terrain 80 (12800 triangles) did not finish in 10 minutes.
- The built-in `MaxDeviation` is slow on flat inputs: a cube of 4332 triangles takes 76 s at 0.001 (the research note,
  section 5).

## Speculative parallel certification (measured and closed, 2026-11-02)

The next checks of the queue were certified in parallel ahead of the serial loop, and a certificate was used only if
everything the check reads was unchanged, bit for bit. The result was bit-identical on all 6 cases above, but slower
on 4 cores:
- bumpy sphere 0.001: 31 s on master, 46-51 s with the speculation;
- terrain 0.001: 21 s against 26-30 s;
- at 0.01: about equal.

Two reasons:
- Only 53-95 % of the certificates are used, depending on the window: after each collapse `UpdateNeighbours`
  reorders the queue.
- The existing parallel check over a collapse's new triangles already keeps 2.2 cores busy, while the speculation
  nearly doubles the CPU work.

Design, numbers and the reasons: [docs/design/REDUCER_SPECULATIVE_CERTIFICATION.md](../../docs/design/REDUCER_SPECULATIVE_CERTIFICATION.md).
Code: branch `claude/reducer-speculative-archive`.

## Fold check (`folds`, `Reducer.PreventFoldOver`, finding S1, 2026-11-03)

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- folds 5
```

`PreventFoldOver` off and on, alternating rounds (median of 5). "Turned" counts the triangles whose normal does not
point up (terrain) or away from the center (bumpy sphere). The hashes are those of `hashes` (`ReducerHashes.Hash`).

| mesh | reduction | off: triangles, turned, time | on: triangles, turned, time | time on/off | hashes off / on |
|---|---|---|---|---|---|
| bumpy sphere | to 20 % | 39938, 0, 542 ms | 39938, 0, 586 ms | 1.08 | 5E8B2650C77E17A9 / 5E8B2650C77E17A9 |
| bumpy sphere | to 5 % | 9984, 0, 609 ms | 9984, 0, 676 ms | 1.11 | 21200A74A1A113DA / 21200A74A1A113DA |
| bumpy sphere | to 1 % | 1996, 0, 669 ms | 1996, 0, 672 ms | 1.00 | 7FFA6B886EDB9806 / 7FFA6B886EDB9806 |
| bumpy sphere | MaxDeviation 3e-3 | 512, 0, 12371 ms | 512, 0, 12537 ms | 1.01 | 12C9463B79A42156 / 01444CB77C6BFFD8 |
| terrain | to 20 % | 36000, 7, 511 ms | 36000, 0, 524 ms | 1.02 | 8DCC7212BB6D48E7 / 396F318E3BDFFBC6 |
| terrain | to 5 % | 9000, 1, 592 ms | 9000, 0, 678 ms | 1.14 | E6F87EB4DEC929F9 / 67BC991627AB2BC2 |
| terrain | to 1 % | 1800, 0, 567 ms | 1799, 0, 632 ms | 1.11 | 0C26CB45B9EF68C1 / 7CE726A6E528729C |
| terrain | MaxDeviation 3e-3 | 923, 0, 8215 ms | 924, 0, 8416 ms | 1.02 | BBF23739034D0171 / A923C4FC33897780 |

Findings:
- On the terrain the check removes the folds the reduction left (7 at 20 %, 1 at 5 %).
- On the bumpy sphere it rejects nothing without MaxDeviation (the same meshes bit for bit).
- With MaxDeviation the hashes change: the off hashes are those of `hashes` before the change (table above), the on
  hashes those of `hashes` now.
- The cost: +0 to +14 % without MaxDeviation, +1 to +2 % with it (where the deviation check dominates). The check
  runs in the same pass over each one-ring as the flip test (`FusedFoldCheck`; the same decisions as two passes, bit
  for bit, `ReducerFoldOverOptionsTests.FusedPass_SameReductionsAsTwoPasses`). As a second pass it cost +16 to +26 %;
  an order of the tests that counted the boundary edges of every new triangle +34 to +51 %.

## Two-sided bound (`twosided`, November 2026)

`Reducer.MaxDeviationTwoSided` ([BOUNDED_SIMPLIFICATION.md](../../docs/design/BOUNDED_SIMPLIFICATION.md), option O1):
A is `MaxDeviation = eps` (one-sided, today), B adds `MaxDeviationTwoSided`; both reduce to 4 triangles (as far as the
checks allow). Rounds alternate A and B; times are medians of 3. The distances are `MeshHausdorffDistance` upper bounds
at a tolerance of 0.01 eps, in units of eps (A's lower bound in brackets: above 1 is a proven violation); M is the input,
S the result. Procedural meshes, plus six Thingi10K models fetched at run time (not committed; revision `2d5d3b2f`,
drawn with seed 731 among the closed, manifold, single-component meshes with 5k-20k faces, as in the design note).

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- twosided 3 [mesh files...]
```

The commit that adds the option, on master a5be280; Linux container, 4 cores, net10.0, Release:

| mesh | triangles | eps / diag | A: triangles, ms | B: triangles, ms | time B / A | A: h(M,S) / eps (lower) | B: h(M,S) / eps | B: h(S,M) / eps | B: certified / refused |
|---|---|---|---|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.001 | 1706, 3010 | 1706, 4481 | 1.49x | 1.011 (1.001) | 1.009 | 1.008 | 248304 / 1 |
| bumpy sphere 40 | 18252 | 0.003 | 518, 1769 | 518, 2643 | 1.49x | 1.007 (0.997) | 1.007 | 1.003 | 320371 / 0 |
| spiky sphere 60 | 41772 | 0.001 | 1252, 5940 | 1252, 9275 | 1.56x | 1.009 (1.000) | 1.009 | 1.009 | 765766 / 0 |
| spiky sphere 60 | 41772 | 0.003 | 432, 3356 | 434, 5544 | 1.65x | 1.024 (1.017) | 1.008 | 1.009 | 849560 / 2 |
| terrain 80 | 12800 | 0.001 | 2941, 1307 | 2942, 2003 | 1.53x | 1.176 (1.167) | 1.009 | 1.008 | 109311 / 2 |
| terrain 80 | 12800 | 0.003 | 932, 898 | 936, 1268 | 1.41x | 1.109 (1.099) | 1.009 | 1.002 | 159840 / 3 |
| 1036464 | 7068 | 0.001 | 940, 967 | 942, 1383 | 1.43x | 1.077 (1.068) | 1.006 | 1.009 | 78997 / 3 |
| 1036464 | 7068 | 0.003 | 390, 658 | 390, 907 | 1.38x | 1.008 (0.998) | 1.008 | 1.004 | 97502 / 0 |
| 1436278 | 18610 | 0.001 | 532, 3787 | 900, 4380 | 1.16x | 1.288 (1.279) | 1.006 | 1.004 | 372659 / 213 |
| 1436278 | 18610 | 0.003 | 318, 1881 | 320, 2508 | 1.33x | 1.007 (0.998) | 1.007 | 0.999 | 443290 / 1 |
| 472201 | 5572 | 0.001 | 346, 1271 | 348, 1614 | 1.27x | 1.010 (1.003) | 1.010 | 1.003 | 84274 / 1 |
| 472201 | 5572 | 0.003 | 182, 649 | 186, 843 | 1.30x | 1.041 (1.033) | 0.980 | 1.009 | 93364 / 3 |
| 51011 | 8656 | 0.001 | 1764, 1542 | 1764, 1992 | 1.29x | 1.017 (1.008) | 1.008 | 1.004 | 83171 / 1 |
| 51011 | 8656 | 0.003 | 902, 993 | 904, 1224 | 1.23x | 1.047 (1.037) | 1.005 | 0.998 | 100532 / 10 |
| 60516 | 14628 | 0.001 | 1652, 1889 | 1662, 2670 | 1.41x | 1.126 (1.116) | 1.007 | 1.009 | 178816 / 7 |
| 60516 | 14628 | 0.003 | 1000, 1125 | 1006, 1504 | 1.34x | 1.150 (1.141) | 0.992 | 1.007 | 205378 / 8 |
| 894833 | 7952 | 0.001 | 2034, 909 | 2034, 1376 | 1.51x | 0.995 (0.985) | 0.995 | 1.001 | 65229 / 0 |
| 894833 | 7952 | 0.003 | 858, 723 | 888, 1006 | 1.39x | 1.145 (1.135) | 1.006 | 1.007 | 93884 / 29 |

time B / A: median 1.41x, max 1.65x (target about 2x).

- A violates h(M, S) <= eps in 12 of 18 cases (lower bound above eps, up to 0.28 eps); B is within eps in all 18 (upper
  bounds <= 1.010 eps at the 0.01 eps tolerance), and its h(S, M) is unchanged.
- Triangles: B within 0-4 % of A, except 1436278 at 0.001 (532 -> 900), where A cut deepest.
- The reverse check runs last in `CollapseEdge` (after flip, fold, deviation and self-intersection checks), so it is paid
  only for collapses that would otherwise be done; it refuses few of them (0-213 per run).

### FastCollapsePass (#792)

Since #792 the option also covers `FastCollapsePass` (midpoint collapses of short edges). The second table of the same
run (`twosided 3`, the same meshes, at eps 0.003 and 0.01 of the diagonal): A is `FastCollapsePass(0.05 * diag, 5)` with `MaxDeviation`, B adds
`MaxDeviationTwoSided`; medians of 3 alternating rounds, on this branch (base master 08b61b9).

| mesh | triangles | eps / diag | A: triangles, ms | B: triangles, ms | time B / A | A: h(M,S) / eps (lower) | B: h(M,S) / eps | B: h(S,M) / eps |
|---|---|---|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.003 | 904, 4705 | 934, 6237 | 1.33x | 1.009 (1.001) | 1.010 | 1.009 |
| bumpy sphere 40 | 18252 | 0.01 | 296, 4838 | 596, 5878 | 1.21x | 1.009 (1.001) | 1.009 | 1.007 |
| spiky sphere 60 | 41772 | 0.003 | 990, 11818 | 1184, 14362 | 1.22x | 1.440 (1.431) | 1.010 | 1.009 |
| spiky sphere 60 | 41772 | 0.01 | 324, 11488 | 834, 12647 | 1.10x | 6.932 (6.932) | 1.008 | 1.010 |
| terrain 80 | 12800 | 0.003 | 1500, 1855 | 1498, 2166 | 1.17x | 1.023 (1.013) | 1.010 | 1.006 |
| terrain 80 | 12800 | 0.01 | 572, 1792 | 762, 2302 | 1.28x | 1.020 (1.013) | 1.009 | 1.006 |
| 1036464 | 7068 | 0.003 | 642, 1400 | 664, 3504 | 2.50x | 1.027 (1.017) | 1.009 | 1.007 |
| 1036464 | 7068 | 0.01 | 304, 760 | 322, 1282 | 1.69x | 1.090 (1.082) | 1.009 | 1.006 |
| 1436278 | 18610 | 0.003 | 400, 4689 | 410, 8123 | 1.73x | 1.169 (1.160) | 1.009 | 1.008 |
| 1436278 | 18610 | 0.01 | 158, 2566 | 238, 3989 | 1.55x | 2.333 (2.330) | 1.007 | 0.960 |
| 472201 | 5572 | 0.003 | 322, 1596 | 326, 2574 | 1.61x | 1.207 (1.198) | 1.009 | 1.002 |
| 472201 | 5572 | 0.01 | 200, 574 | 202, 826 | 1.44x | 1.190 (1.181) | 1.004 | 0.999 |
| 51011 | 8656 | 0.003 | 1598, 1029 | 1634, 1324 | 1.29x | 1.495 (1.485) | 1.006 | 1.008 |
| 51011 | 8656 | 0.01 | 744, 612 | 798, 853 | 1.39x | 1.262 (1.253) | 1.003 | 1.002 |
| 60516 | 14628 | 0.003 | 1456, 3984 | 1502, 5816 | 1.46x | 1.700 (1.690) | 1.009 | 1.009 |
| 60516 | 14628 | 0.01 | 480, 1983 | 804, 3000 | 1.51x | 1.879 (1.870) | 1.010 | 1.008 |
| 894833 | 7952 | 0.003 | 1402, 1101 | 1430, 1571 | 1.43x | 1.125 (1.119) | 1.009 | 1.001 |
| 894833 | 7952 | 0.01 | 524, 567 | 542, 894 | 1.57x | 1.130 (1.126) | 1.008 | 1.002 |

- Time B / A: median 1.44x, max 2.50x (1036464 at 0.003; 1.69x at 0.01).
- The one-sided fast pass violates h(M, S) <= eps in 18 of 18 cases (lower bounds 1.001 to 6.93 eps: it cuts the spikes);
  the two-sided one in none (upper bounds <= 1.010 eps at the 0.01 eps tolerance).
- Triangles: B keeps more where A cut features (spiky sphere at 0.01: 324 -> 834; 1436278: 158 -> 238; bumpy sphere at
  0.01: 296 -> 596), 0-4 % more elsewhere.

### Endpoint fallback (`FastCollapseEndpointFallback`, November 2026)

`Reducer.FastCollapseEndpointFallback` (opt-in, off by default): in `FastCollapsePass`, an edge whose midpoint collapse is
refused (flip / fold-over, `MaxDeviation`, the two-sided check, `PreventSelfIntersections`) is tried again as a collapse onto
one of its ends, the end of the larger angle defect (the sharper feature) first; every check applies to the end position, so
the bounds hold. Measured with the same meshes as above (`fallback 3`, `FastCollapsePass(0.05 * diag, 5)` with `MaxDeviation`
and `MaxDeviationTwoSided`, medians of 3 alternating rounds; Linux container, 4 cores, net10.0). The variants are internal
knobs (`FastCollapseFallbackBothEnds`: also the other end when the feature end is refused; `FastCollapseFallbackFirstRoundOnly`);
"off" is today's pass.

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- fallback 3 [mesh files...]
```

| mesh | triangles | eps / diag | off: triangles, ms | feature end: triangles, ms | both ends, round 1: triangles, ms | feature end, round 1: triangles, ms | both ends: triangles, ms | h(M,S) / h(S,M) / eps, both ends |
|---|---|---|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.003 | 934, 6838 | 696, 10559 | 706, 11655 | 720, 9733 | 688, 11901 | 1.010 / 1.009 |
| bumpy sphere 40 | 18252 | 0.01 | 596, 5756 | 216, 8707 | 224, 10086 | 226, 8678 | 216, 9979 | 1.006 / 1.006 |
| spiky sphere 60 | 41772 | 0.003 | 1184, 16664 | 652, 32366 | 624, 37839 | 658, 32319 | 622, 37240 | 1.009 / 1.009 |
| spiky sphere 60 | 41772 | 0.01 | 834, 12332 | 254, 25290 | 242, 31286 | 268, 26582 | 236, 30755 | 1.007 / 1.009 |
| terrain 80 | 12800 | 0.003 | 1498, 2038 | 1358, 3320 | 1356, 3527 | 1378, 3188 | 1354, 3698 | 1.009 / 1.006 |
| terrain 80 | 12800 | 0.01 | 762, 2432 | 500, 4457 | 448, 6026 | 506, 4578 | 440, 6017 | 1.002 / 1.009 |
| 1036464 | 7068 | 0.003 | 664, 3268 | 586, 4266 | 562, 4743 | 610, 4110 | 554, 4834 | 1.008 / 1.003 |
| 1036464 | 7068 | 0.01 | 322, 1201 | 272, 1703 | 256, 1926 | 278, 1653 | 250, 1928 | 1.006 / 0.999 |
| 1436278 | 18610 | 0.003 | 410, 7701 | 288, 14632 | 370, 16662 | 386, 14392 | 284, 17716 | 1.010 / 1.008 |
| 1436278 | 18610 | 0.01 | 238, 4034 | 184, 7148 | 212, 7793 | 242, 6906 | 140, 7936 | 1.007 / 1.002 |
| 472201 | 5572 | 0.003 | 326, 2645 | 266, 3038 | 266, 3498 | 272, 2998 | 254, 3591 | 1.007 / 0.997 |
| 472201 | 5572 | 0.01 | 202, 918 | 158, 1254 | 172, 1276 | 180, 1218 | 154, 1349 | 0.998 / 1.006 |
| 51011 | 8656 | 0.003 | 1634, 1477 | 1408, 1735 | 1378, 1895 | 1444, 1782 | 1338, 2096 | 1.009 / 1.008 |
| 51011 | 8656 | 0.01 | 798, 862 | 656, 1224 | 678, 1319 | 682, 1216 | 650, 1344 | 1.004 / 1.003 |
| 60516 | 14628 | 0.003 | 1502, 5839 | 1128, 8585 | 1108, 8975 | 1156, 8352 | 1082, 8708 | 1.010 / 1.006 |
| 60516 | 14628 | 0.01 | 804, 2905 | 624, 4943 | 678, 4924 | 694, 4421 | 644, 4931 | 1.006 / 1.003 |
| 894833 | 7952 | 0.003 | 1430, 1444 | 1190, 2097 | 1194, 2312 | 1232, 2113 | 1180, 2273 | 1.010 / 1.005 |
| 894833 | 7952 | 0.01 | 542, 872 | 436, 1105 | 442, 1160 | 476, 1175 | 408, 1257 | 1.008 / 1.005 |

| variant | triangles removed against off (sum over the rows) | extra time (sum, s) | triangles per extra second | share of the both-ends reduction | time against off (median of the rows) |
|---|---|---|---|---|---|
| feature end | 3808 | 57.2 | 67 | 91 % | 1.51x |
| both ends, round 1 | 3764 | 77.7 | 48 | 90 % | 1.69x |
| feature end, round 1 | 3272 | 56.2 | 58 | 78 % | 1.46x |
| both ends | 4186 | 78.3 | 53 | 100 % | 1.70x |

Rule for the shipped variant: the most triangles removed per extra second over the 18 rows, among the variants that keep at
least 75 % of the reduction of the full retry (both ends in every round). The feature end alone keeps 91 % of it at 67
triangles per extra second (both ends: 53, both ends in round 1: 48, feature end in round 1: 58, 78 %), so the feature end alone
is shipped (the other knobs default to false). Its time against off is 1.15x to 2.05x (median 1.51x) and its triangles 9 to 70 %
fewer. The last column of the table is the both-ends variant (at most 1.010 eps in all 18 rows); the bounds of the shipped feature
end alone (`FB_BOUNDS=1 ... -- fallback 1 [files]`, certified upper bounds at 0.01 eps tolerance, in eps) are at most 1.010 eps in all 18 rows too:

| mesh | triangles | eps / diag | off: triangles | feature end: triangles | feature end: h(M,S) / eps | feature end: h(S,M) / eps |
|---|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.003 | 934 | 696 | 1.010 | 1.008 |
| bumpy sphere 40 | 18252 | 0.01 | 596 | 216 | 1.007 | 1.009 |
| spiky sphere 60 | 41772 | 0.003 | 1184 | 652 | 1.008 | 1.008 |
| spiky sphere 60 | 41772 | 0.01 | 834 | 254 | 1.005 | 1.009 |
| terrain 80 | 12800 | 0.003 | 1498 | 1358 | 1.009 | 1.004 |
| terrain 80 | 12800 | 0.01 | 762 | 500 | 1.010 | 0.999 |
| 1036464 | 7068 | 0.003 | 664 | 586 | 1.009 | 1.005 |
| 1036464 | 7068 | 0.01 | 322 | 272 | 1.008 | 1.006 |
| 1436278 | 18610 | 0.003 | 410 | 288 | 1.007 | 1.006 |
| 1436278 | 18610 | 0.01 | 238 | 184 | 1.006 | 0.997 |
| 472201 | 5572 | 0.003 | 326 | 266 | 1.007 | 0.990 |
| 472201 | 5572 | 0.01 | 202 | 158 | 0.990 | 1.006 |
| 51011 | 8656 | 0.003 | 1634 | 1408 | 1.009 | 1.003 |
| 51011 | 8656 | 0.01 | 798 | 656 | 1.008 | 1.003 |
| 60516 | 14628 | 0.003 | 1502 | 1128 | 1.009 | 1.007 |
| 60516 | 14628 | 0.01 | 804 | 624 | 1.006 | 1.008 |
| 894833 | 7952 | 0.003 | 1430 | 1190 | 1.008 | 1.007 |
| 894833 | 7952 | 0.01 | 542 | 436 | 1.008 | 1.002 |

The result is the same on repeated runs.

With `PreventSelfIntersections` also on (`FB_PIT=1`, `fallback 1`, four meshes, 8 rows) the ranking is the same, the feature end
alone again the best ratio (129 triangles per extra second, 92 % of the reduction):

| mesh | triangles | eps / diag | off: triangles, ms | feature end: triangles, ms | both ends, round 1: triangles, ms | feature end, round 1: triangles, ms | both ends: triangles, ms | h(M,S) / h(S,M) / eps, both ends |
|---|---|---|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.003 | 934, 6480 | 696, 10158 | 706, 11393 | 720, 10157 | 688, 11724 | 1.010 / 1.009 |
| bumpy sphere 40 | 18252 | 0.01 | 596, 6534 | 216, 8900 | 224, 10149 | 226, 8797 | 216, 10920 | 1.006 / 1.006 |
| terrain 80 | 12800 | 0.003 | 1498, 2477 | 1358, 3970 | 1356, 3814 | 1378, 3691 | 1354, 4429 | 1.009 / 1.006 |
| terrain 80 | 12800 | 0.01 | 762, 2970 | 500, 5191 | 448, 6228 | 506, 4987 | 440, 6436 | 1.002 / 1.009 |
| 472201 | 5572 | 0.003 | 326, 2922 | 266, 3510 | 266, 3674 | 272, 3102 | 254, 3774 | 1.007 / 0.997 |
| 472201 | 5572 | 0.01 | 202, 1024 | 158, 1410 | 172, 1537 | 180, 1325 | 154, 1548 | 0.998 / 1.006 |
| 894833 | 7952 | 0.003 | 1430, 1718 | 1190, 2181 | 1194, 2279 | 1232, 2192 | 1180, 2368 | 1.010 / 1.005 |
| 894833 | 7952 | 0.01 | 542, 1165 | 436, 1355 | 442, 1393 | 476, 1324 | 408, 1505 | 1.008 / 1.005 |

| variant | triangles removed against off (sum over the rows) | extra time (sum, s) | triangles per extra second | share of the both-ends reduction | time against off (median of the rows) |
|---|---|---|---|---|---|
| feature end | 1470 | 11.4 | 129 | 92 % | 1.38x |
| both ends, round 1 | 1482 | 15.2 | 98 | 93 % | 1.54x |
| feature end, round 1 | 1300 | 10.3 | 126 | 81 % | 1.35x |
| both ends | 1596 | 17.4 | 92 | 100 % | 1.67x |
