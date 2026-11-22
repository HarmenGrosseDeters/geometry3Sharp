# Envelope-guaranteed simplification with the paper's algorithm (HP 10, 2026-11-22)

Research spike, no library code (owner go 2026-11-22; plan #1288). The question: can a simplification be guaranteed
to stay inside an ε-envelope of the input, closed, manifold and free of self-intersections, with the algorithm of
Wang et al. 2020 at a practical cost, and how does that compare with the current `Reducer` on procedural inputs
(reduction reached at a given ε, time, Hausdorff distance)?

Prototype: `benchmarks/ReducerBenchmark/EnvelopeSimplification.cs` (mode `envelope`), a `Reducer` subclass in the
benchmark. Raw output: [benchmarks/ReducerBenchmark/README.md](../../benchmarks/ReducerBenchmark/README.md#reduction-driven-by-the-exact-envelope-envelope-november-2026).

## Verdict

**Leaning: no-go for the paper's algorithm as the `Reducer`'s check (option 1 below). The guarantee holds, the cost
and the reduction do not.**

| question | answer, with numbers |
|---|---|
| Is the result guaranteed inside the ε-envelope? | Yes, exactly: every triangle of every envelope reduction is inside the polyhedral envelope (checked again on the result), measured h(result, input) 0.57-0.65 ε. |
| Closed, manifold, free of self-intersections? | Yes, with the checks the `Reducer` already has (link condition, `PreventSelfIntersections`): `NotCleanReason` is empty for both variants. The envelope adds nothing here. |
| Reduction reached at the same ε | Worse: 1.13-1.93x the triangles of `MaxDeviation` on curved inputs (terrain, bumpy sphere), equal on a flat-faced cube. The cause is the shape (ε/√3 polyhedra), not the algorithm. |
| Time | 1.4-98x the current `Reducer` on curved inputs (5-98x in 5 of the 6 runs), 39-119x on the cube. Per query 0.6-5.7 ms against 20-46 µs for `IsWithin` (153-783 µs on the cube). |
| Upper bound, before the prototype | `MaxDeviation` at ε and at 0.9 ε/√3 brackets what the envelope can reach: 1.75-2.9x the triangles at the inner radius. The envelope lands between the two, near the inner one. |
| What the paper's exactness buys over `IsWithin` | Nothing measurable: ENVELOPE.md §6 found no accepted triangle closer to ε than 1e-8 ε, with rounding at about 1e-13 ε. |

Side finding, for its own issue: the *current* `Reducer` with `MaxDeviation` takes 76 s on a flat-faced cube of 4332
triangles (section 5). That is the practical gap for CAD-like inputs, and it is not an envelope question.

## 1. The method, in our words

Source: B. Wang, T. Schneider, Y. Hu, M. Attene, D. Panozzo, *Exact and Efficient Polyhedral Envelope Containment
Check*, ACM TOG 39(4), 2020. The algorithm is described in [design/ENVELOPE.md](../design/ENVELOPE.md) §3 (from the
full text, read for #258); this note does not repeat it.

- Each input triangle gets an open convex polyhedron of thickness δ = ε/√3 (floor, ceiling, three sides, caps at acute
  vertices); every point of it is within ε of the triangle. The envelope is the union, never built explicitly.
- A query triangle is inside if its vertices are inside (C1), every crossing of an edge with a facet plane is inside
  another polyhedron (C2), and every point where two facet planes meet the triangle's plane inside the triangle is
  inside a third (C3). All tests are signs of indirect predicates (`Orient3dLPI`, `Orient3dTPI`): no constructions.
- For simplification the check is a filter on the collapse: every triangle the collapse creates must be inside, else
  the collapse is refused. That is how the prototype uses it, and how established libraries offer it (section 6).
- The guarantee is one-sided (result within ε of the input), as `Reducer.MaxDeviation`. The other direction
  (`MaxDeviationTwoSided`) and intersection-freeness are separate mechanisms in both cases.

## 2. Building blocks in the library (look before you build)

| block | state | use here |
|---|---|---|
| `PolyhedralEnvelope` (`mesh_ops/`, internal, exact file) | built on #263 / #268: proven construction, point / segment / triangle queries, cover set, oracle-tested | the paper's check, unchanged |
| `TriangleDeviationBounds.IsWithin` (Sacht & Jacobson 2024, #848 certified patch bound) | the `Reducer`'s `MaxDeviation` certificate | the baseline |
| `Reducer`: link condition, `PreventSelfIntersections` (exact), `MaxDeviationTwoSided`, `FastCollapseEndpointFallback` (opt-in) | built | closed, manifold, intersection-free, two-sided: nothing new is needed |
| `MeshHausdorffDistance`, `MeshRobustRepair.NotCleanReason` | built | measuring the results |
| `ExactPlanarSimplify` (internal) | removes vertices inside exactly flat regions of boolean results | a candidate block for the flat-input finding (section 5), not for the envelope |

Already measured and closed (ENVELOPE.md §6, 2026-11-01): per query on the triangles of a finished `MaxDeviation`
reduction, the envelope took about 5x `IsWithin` and accepted 8-17 % of them. Not measured then, and the point of this
spike: a whole reduction *driven* by the envelope, which never produces the triangles it would refuse.

## 3. Upper bound first

The polyhedra contain every point within 0.9 δ of their triangle (the construction keeps at least 0.9 δ per facet) and
lie within ε of it. So the envelope E(ε) sits between two distance envelopes: B(0.9 ε/√3) ⊂ E(ε) ⊂ B(ε). The built-in
`Reducer` at these two tolerances brackets the reduction the envelope can reach, at the cost of two fast runs
(`envelope bracket`):

| mesh | triangles | ε / diag | A: `MaxDeviation` ε: triangles | C: `MaxDeviation` 0.9 ε/√3: triangles | C / A |
|---|---|---|---|---|---|
| bumpy sphere 40 | 18252 | 0.001 | 1706 | 3616 | 2.12 |
| bumpy sphere 40 | 18252 | 0.003 | 518 | 1062 | 2.05 |
| bumpy sphere 40 | 18252 | 0.01 | 146 | 292 | 2.00 |
| terrain 80 | 12800 | 0.001 | 2941 | 6524 | 2.22 |
| terrain 80 | 12800 | 0.003 | 932 | 1627 | 1.75 |
| terrain 80 | 12800 | 0.01 | 117 | 340 | 2.91 |

On curved surfaces the triangle count grows about like 1/ε, so the factor 1/0.52 ≈ 1.9 was expected. The bound says:
an envelope reduction can at best match A and will be near C. By the stop rule this was the point to stop; the
prototype was still run, because the reduction actually reached and the cost of a driven reduction were the open
numbers of the plan.

## 4. The prototype and its numbers

`HookReducer` overrides `Reducer.CollapseEdge`, gathers the triangles the collapse creates and asks a callback before
calling the base. Variants:
- **A**: the built-in `Reducer { MaxDeviation = ε }`.
- **C**: the built-in `Reducer` at 0.9 ε/√3 (the bracket).
- **control**: the hook with `IsWithin` and the built-in check's point samples. Its result equals A bit for bit in all
  12 runs, so the hook asks about the right triangles and positions.
- **B**: the hook with `PolyhedralEnvelope.Contains` on the new vertex and on four samples per triangle, then
  `ContainsTriangle` per triangle (parallel over the triangles of a collapse, as the built-in check).

Machine: Linux container, 4 cores, net10.0, Release, one run per entry. Times include the construction of the
envelope (5-81 ms). Hausdorff distances are certified upper bounds to 0.01 ε. Inputs are procedural and small on
purpose: at 12800 triangles (terrain 80) the envelope reduction did not finish in 10 minutes.

| mesh (triangles) | ε / diag | A: triangles, ms | C: triangles | B: triangles, ms | B / A triangles | B / A time | A: h(R,M) / h(M,R) in ε | B: h(R,M) / h(M,R) in ε | A's triangles inside E(ε) |
|---|---|---|---|---|---|---|---|---|---|
| terrain 40 (3200) | 0.001 | 2770, 283 | 3153 | 3138, 388 | 1.13 | 1.4 | 1.01 / 1.02 | 0.57 / 0.68 | 1897 of 2770 |
| terrain 40 (3200) | 0.003 | 951, 248 | 1761 | 1590, 3133 | 1.67 | 12.6 | 1.00 / 1.03 | 0.65 / 0.61 | 166 of 951 |
| terrain 40 (3200) | 0.01 | 104, 223 | 244 | 201, 21726 | 1.93 | 97.5 | 0.99 / 1.03 | 0.64 / 0.70 | 15 of 104 |
| bumpy sphere 20 (4332) | 0.001 | 1740, 667 | 2762 | 2630, 3354 | 1.51 | 5.0 | 1.01 / 1.01 | 0.58 / 0.58 | 454 of 1740 |
| bumpy sphere 20 (4332) | 0.003 | 528, 570 | 1116 | 994, 7397 | 1.88 | 13.0 | 1.01 / 1.02 | 0.60 / 0.59 | 42 of 528 |
| bumpy sphere 20 (4332) | 0.01 | 148, 338 | 296 | 270, 20943 | 1.82 | 61.9 | 1.00 / 1.00 | 0.59 / 0.58 | 23 of 148 |
| cube 10 (972) | 0.001 | 12, 3665 | 12 | 12, 141337 | 1.00 | 38.6 | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 |
| cube 10 (972) | 0.003 | 12, 2554 | 12 | 12, 149954 | 1.00 | 58.7 | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 |
| cube 10 (972) | 0.01 | 12, 1262 | 12 | 12, 149722 | 1.00 | 118.6 | 0.01 / 0.01 | 0.01 / 0.01 | 12 of 12 |

(h values of 1.00-1.03 ε and 0.01 ε are the 1 % accuracy of the measurement. The hook checks before the flip test,
the built-in check after it; on the cube that alone costs 4-5x (control 5.5-19.7 s against A's 1.3-3.7 s), so there B
is 7-27x the control. On the curved inputs the control is within 0.7-1.1x of A.)

Reading:
- **Reduction.** B ends with 1.13-1.93x the triangles of A on the curved inputs, a little better than the inner
  bracket C (the polyhedra reach beyond 0.9 δ sideways), and its measured deviation is 0.57-0.65 ε: it uses about
  ε/√3 of the tolerance it was given. On the cube both reach the 12 triangles: in a plane the thin slab does not bind.
- **Not a verifier either.** Of A's result triangles only 8-68 % are inside E(ε), so the envelope cannot certify a
  `MaxDeviation` reduction after the fact.
- **Time.** B / A grows with the reduction: the further it goes, the larger the triangles and the more polyhedra each
  one spans.

Per query (the queries of the envelope reduction, every fourth collapse, replayed serially; best of 3 for the envelope):

| mesh | ε / diag | envelope µs | of it the linear box scan | `IsWithin` µs | one polyhedron holds the triangle | full test (C2, C3): inside / outside | envelope µs: one polyhedron / full inside / full outside |
|---|---|---|---|---|---|---|---|
| terrain 40 | 0.003 | 908 | 4 | 22 | 3 % | 89 % / 8 % | 7 / 1009 / 85 |
| terrain 40 | 0.01 | 4377 | 5 | 20 | 11 % | 87 % / 2 % | 7 / 4952 / 4306 |
| bumpy sphere 20 | 0.001 | 832 | 6 | 46 | 2 % | 89 % / 9 % | 8 / 929 / 74 |
| bumpy sphere 20 | 0.003 | 1281 | 7 | 43 | 3 % | 95 % / 2 % | 8 / 1346 / 230 |
| bumpy sphere 20 | 0.01 | 3008 | 7 | 22 | 12 % | 88 % / 0 % | 8 / 3397 / 4598 |
| cube 10 | 0.001 | 5431 | 3 | 783 | 2 % | 98 % / 0 % | 7 / 5562 / - |
| cube 10 | 0.003 | 5674 | 1 | 538 | 2 % | 98 % / 0 % | 6 / 5811 / - |
| cube 10 | 0.01 | 5686 | 3 | 153 | 2 % | 98 % / 0 % | 6 / 5823 / - |

- **Stratum and cost boundary:** the queries of a driven reduction on these small procedural inputs, wall time per
  call including its allocations (candidate and facet lists), serial. Not Thingi10K, not the slow tail.
- The paper's fast path (one polyhedron holds all three vertices, 6-8 µs) decides 2-12 % of the queries. A reducer's
  new triangles are larger than one input triangle by construction, so 88-98 % run the full test at 0.9-5.8 ms. The
  missing box tree (`candidates_box` scans linearly) costs 1-7 µs here and is not the problem.
- Idealised cost: even if every full test cost what `IsWithin` costs, B would still stop at 1.1-1.9x the triangles.
  The speed is not the binding limit; the shape is.

**The guaranteed pipeline** (`envelope psi`: `PreventSelfIntersections` on, closed inputs, ε = 0.003·diag):

| mesh | A + PSI: triangles, ms, `NotCleanReason` | B + PSI: triangles, ms, `NotCleanReason` | B inside E(ε) | B / A triangles | B / A time |
|---|---|---|---|---|---|
| bumpy sphere 20 | 528, 1029, clean | 994, 8098, clean | 994 of 994 | 1.88 | 7.9 |
| cube 10 | 12, 2521, clean | 12, 157183, clean | 12 of 12 | 1.00 | 62.3 |

Both are closed, manifold and free of self-intersections. Those properties come from the `Reducer`'s own checks in
both columns.

## 5. Side finding: `MaxDeviation` on flat inputs

Found while choosing the CAD-like case. The built-in `Reducer { MaxDeviation = 0.001·diag }` on a welded cube with
flat faces (scratch probe, same machine):

| cube, triangles | result | time |
|---|---|---|
| 300 | 14 | 0.8 s |
| 972 | 12 | 3.7-4.4 s |
| 2028 | 16 | 8.2 s |
| 4332 | 30 | 75.9 s |

Above the 20 s line of CLAUDE.md, so it gets an issue. Diagnosis so far:
- `IsWithin` costs 153 / 538 / 783 µs per call on the cube at ε = 0.01 / 0.003 / 0.001·diag, against 20-46 µs on the
  curved inputs, although the true distance of every queried triangle is 0. The time grows as ε shrinks, so the bound
  of a piece that lies exactly on the input is not 0 but scales with the piece, and the branch and bound subdivides
  down to pieces of about ε. The curved cases never have triangles this large against ε.
- Nothing stops the reduction on a flat face, so the triangles grow to the size of the face, and each collapse checks
  about 7 of them.
- The results are right (12-30 triangles, deviation 0); only the time is the defect. The cube of 4332 does not reach
  the 12 triangles of the smaller ones, which is worth a look in the same issue.
- Candidate levers, not measured: an exact coplanarity shortcut in the check (a query triangle in the plane of the
  input triangles it lies over has distance 0; `ExactPlanarSimplify` has the exact flat-region machinery), or a
  tighter bound for pieces whose nearest triangles are coplanar with them (#873 fixed the same kind of stall in
  `MeshHausdorffDistance`).

## 6. How others solve it (prior-art check)

Limits of this check: the session's network policy blocked the paper hosts (arxiv.org, the authors' pages, doc.cgal.org,
hal.science), so only search-result extracts were read, marked "(extract)". The paper's algorithm itself rests on the
full-text read recorded in ENVELOPE.md. No source code of any library was opened.

- **Wang et al. 2020**: the check is integrated into two surface remeshing algorithms and into fTetWild (extract of the
  abstract); it wins against sampling when the envelope is thin. Its users ask small local queries (ENVELOPE.md §6), the
  opposite of a reducer's.
- **CGAL** `Surface_mesh_simplification` (manual, extract; no code): the same envelope as a placement filter, rejecting
  a collapse when a triangle of its profile leaves the envelope, with cheaper filters nested in front. The manual states
  that the polyhedral envelope lies inside the distance envelope, so the test is conservative for it. No timings.
- **fTetWild** (Hu et al. 2020, extract): envelope by sampling as the default, ε = diag/1000, cost rising steeply as
  the envelope shrinks.
- **Mandad, Cohen-Steiner, Alliez 2015**, *Isotopic Approximation within a Tolerance Volume* (extract of the
  abstract): a different route, simplification of a 3D triangulation of the tolerance volume; intersection-free and
  isotopic output, at the cost of a volume mesh. Not a per-collapse filter.
- **Permission grids, simplification envelopes, carried error volumes**: reviewed in
  [design/RESEARCH_REDUCER_REMESHER.md](../design/RESEARCH_REDUCER_REMESHER.md) §1.2.
- **Newer work** (extracts only): GPU Hausdorff distance used for a Hausdorff-bounded simplifier, and GPU
  intersection-free low-poly meshing. Neither speeds up the envelope test, and both are outside the constraints below.
  Nothing was found that replaces the Wang et al. test for simplification (terms: polyhedral envelope simplification,
  bounded Hausdorff simplification, tolerance volume, envelope containment GPU / interval); a weak negative.

## 7. Fit with the constraints

- **CPU only, netstandard2.0 and net10.0:** no obstacle; `PolyhedralEnvelope` already builds for both.
- **Bit-for-bit determinism:** the envelope's answers are exact, so they do not depend on threads or runtime; the
  prototype's parallel loop only ands exact answers. `IsWithin` is deterministic too, but its answer near ε depends on
  floating-point bounds (ENVELOPE.md §6: no decision that close on the measured inputs).
- **Exact files:** a consumer would call `mesh_ops/PolyhedralEnvelope.cs` as it is. Any speed-up of the query touches an
  exact file: exact review and a mutation run.
- **API:** an opt-in option on `Reducer` would be additive.

## 8. Options, ranked

| # | option | gain | effort | risk |
|---|---|---|---|---|
| 1 | **Stop: the `Reducer` with `MaxDeviation` (+ `MaxDeviationTwoSided`, `PreventSelfIntersections`) stays the guaranteed simplifier; `PolyhedralEnvelope` stays an internal exact tool.** Close HP 10's "paper's algorithm" question with these numbers. | no cost; nothing lost that was measured | none (this PR) | none |
| 2 | Fix `MaxDeviation` on flat inputs (section 5): issue, then a diagnosis PR | 76 s on 4332 flat triangles is the real gap for CAD-like inputs | S for the issue; M for a fix outside exact code | low; results must stay the same |
| 3 | The envelope as an opt-in exact check in `Reducer` (the paper's algorithm, as in the prototype) | an exact one-sided certificate instead of a floating-point bound that no measured decision depends on | M: option, tests, docs; plus query speed-ups in an exact file (L) to be usable at all | high: 1.1-1.9x the triangles and 5-100x the time; users would get a worse result slower |
| 4 | A wider proven polyhedron (0.8 / 0.6 prism, 16 facets) | accepts about 66 % instead of 8-17 % of `IsWithin`'s triangles (ENVELOPE.md §6) | L: proof, bounds review, mutation | high: 4x the facet pairs, measured at 130-146x `IsWithin` per query |
| 5 | Faster full test for large queries (subdividing the query, sweeping facet pairs) | at best the time of option 3 drops; the reduction stays at 1.1-1.9x | L, exact file | medium; dominated by option 1 while the shape binds |

**Leaning: 1 and 2.** Option 1 because the bracket and the prototype agree that the shape, not the speed, limits the
paper's envelope for simplification, and the properties asked for are already guaranteed by the current `Reducer`.
Option 2 because it is the one measured defect a user of guaranteed simplification would hit.

What would reopen 3: a consumer that needs an exact certificate by contract (for example a pipeline whose next stage
assumes containment in a polyhedral envelope, as fTetWild-style meshers do), or inputs heavily oversampled against ε
(triangles much smaller than ε, where one polyhedron holds most queries). Neither exists in the library today.

## 9. Reproduce

```
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope bracket
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope full "terrain 40"
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope full "bumpy sphere 20"
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope full "cube 10"
dotnet run -c Release --project benchmarks/ReducerBenchmark -- envelope psi "bumpy sphere 20"
```

The larger cases (`bumpy sphere 40`, `terrain 80`, `cube 20`) are in the case list for `bracket`; `full` on them takes
more than 10 minutes each.
