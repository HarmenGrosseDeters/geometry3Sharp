# 2D meshing: plan and handover

For the session that implements the mesher, or anyone starting without context. Written 2026-10-31 by a session that prepared acceptance and implemented nothing in the library. Read this
first; it is enough to start.

## 1. The situation

The goal is a clean-room 2D mesher in this library: Delaunay of points (M1), constrained Delaunay with rings, holes and regions (M2), quality refinement (M3), per
[MESHING2D_SPEC.md](MESHING2D_SPEC.md), whose section 5 holds the owner's decisions and wins over earlier sections. Tier L in [AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md).

| Document | What |
|---|---|
| [ACCEPTANCE_STRATEGY.md](ACCEPTANCE_STRATEGY.md) | The method: oracle, differential, golden master. Accepted by the owner |
| [MESHING2D_DECISIONS.md](MESHING2D_DECISIONS.md) | The domain decisions of parked PR #201, questioned, with the defaults to build. **D1, D5, D9 need the owner** |
| [MESHING2D_CATALOGUE.md](MESHING2D_CATALOGUE.md) | The F cases, one state per row |
| [MESHING2D_GOLDEN_FORMAT.md](MESHING2D_GOLDEN_FORMAT.md) | Rows, fingerprints, text formats, standalone oracle: self-contained, for other producers too |
| [MESHING2D_PROVENANCE.md](MESHING2D_PROVENANCE.md) | The source (paper and section, or spec item) of every non-trivial routine; owner decision of 2026-11-01 |
| [benchmarks/Meshing2Benchmark/README.md](../../benchmarks/Meshing2Benchmark/README.md) | The gates as commands, and the pilot's result |

**Clean room, always:** never open, clone, search or read Triangle, Triangle.NET or a fork of them (including `HarmenGrosseDeters/Triangle.Net-Old-Custom`, and `D:\work\Triangle.NET` on the owner's machine), never GPL
code (CGAL). Papers named in the spec are fine. The reference session of the strategy (section 3) is separate, and its rows are data, read from outside the repository.

Do not change `math/Exact*`, `math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`: `ExactPredicates.Orient2d` and `InCircle` are public and were judged correct by the oracle (section 5, M1). Stay API compatible with upstream 1.0.324. Net48 is a hard requirement (Q15).

## 2. Verdict on PR #200 (M1): rewrite the kernel, keep little else

Tested: the production code of #200 (`comp_geom/DelaunayMesh2.cs`, `TriangulationMesh2.cs` at a2c0d64) through the new oracle.

**It is correct on everything tried**, rerun on the final oracle. 28 CI cases and 7 larger ones with oracle OK; 6000 fuzz inputs (3181 with ties, including duplicates, 2^-1040 and 2^200 scales, last-bit nudges) with 0 failures; the same triangles as NetTopologySuite wherever
that is exact. Its legalization did not cycle or give up on any input. I looked for wrong results and did not find one, so this is not a verdict about correctness.

**It cannot become the mesher.** Rebuilding all edges per insertion and per flip, a text copy of the whole edge list per flip to detect cycles, and exhaustive validation in `Compute` make it about quadratic or worse:

| Input | n | Time | Allocated |
|---|---|---:|---:|
| uniform | 1024 | 3.2 s | 1.5 GB |
| uniform | 2048 | 14.6 s | 6.2 GB |
| uniform | 4096 | 66.8 s | 24.7 GB |
| grid | 1024 | 9.6 s | 5.4 GB |
| grid | 2025 | 56.0 s | 29.4 GB |

(net10, Windows, one run each on a machine with other work on it; scratch harness around `new DelaunayMesh2().Compute`. Rough, so only the growth counts, and it is no benchmark claim.) That is a factor of about 4.6 per doubling. The spec's target is 1 000 000 points in 1.3 to 2.4 s and 100 000 in 0.1 s (3.1).
Extrapolated, not measured, 1 000 000 points take months. It is no matter of tuning: M2 and M3 need local operations on an adjacency structure, and this kernel has none, so nothing of its core carries over.

| Part of #200 | Decision |
|---|---|
| `Builder` (the kernel) | **Rewrite.** Copied, as a test subject only, to `benchmarks/Meshing2Benchmark/Candidates/Prototype200Candidate.cs`. Delete that file when the real one arrives |
| No supertriangle; hull expansion with exact signs only | Keep the idea: it needs no invented coordinates. A rewrite may also choose otherwise, if the oracle agrees |
| `Triangulation2Result`, `Triangulation2Status`, identity rules (`InputToVertex`, alias slots), canonical triangle order | Keep the shape. Fix: nothing ever sets `ProblemCoordinate` (Q10); `NumericalFailure` is unused, remove it |
| `TriangulationMesh2.TryLocate`, `IncidentTriangles` | Keep the semantics; replace the linear scans (a walk from a hint): R7 is a per-Steiner-point query in production |
| `IsValid` | Keep as an optional diagnostic with the exhaustive part behind it, not inside `Compute` by default. Decide with numbers: Q10's "never an invalid mesh" asks for a cheap check, the oracle for the full one |
| `benchmarks/Meshing2Benchmark` of #200 and its oracle | **Discard.** Its oracle truncates coordinates (`new BigInteger(p.x * 2^40)`), so it cannot judge finer inputs, and it rejects only five broken kinds. Its perf CSV measures the kernel to be discarded |
| Tests of #200 | Reread for ideas (reflections, cancellation after snapshot); not evidence. "Hidden" and "independent" acceptance were neither; its reviews did not run tests, one failed with HTTP 502 |
| Bounds in its design document (2M, 8M², 192M⁴) | Fine but moot: the predicates carry their own proofs. The cap 1e70 stays |
| The fixture commit `74af291` | Already on master by its own PR |

Recommendation to the owner: close #200 unmerged once a new M1 is up. Its PR is kept until then as the source of the test subject.

## 3. The order of work

Before M2: get D1, D5 and D9 from the owner, or build their defaults and say so. D1 first: it decides the vertex type of M2 and M3.

1. **M1: new kernel.** Delaunay of points in `comp_geom/`: exact predicates, spatial ordering (Hilbert or similar, sorted without floats deciding anything), adjacency arrays, a walk to locate, Lawson flips or cavity. Papers: Lawson 1977; Bowyer 1981 and Watson 1981; Guibas and Stolfi 1985
   for divide and conquer, an alternative. Tie rule of D18. Cancellation (O20); `TryLocate` by a walk. Then the switch (section 4), then golden rows of the tied cases with the reason "tie rule". Spec 3.1 sets the performance target; benchmark numbers in the README, on net10 and net48.
2. **M2a: constrained, no crossings.** Segment recovery by flips (Sloan 1993, also the source of `ExactTriangulation2`, which is internal and has other preconditions), rings, holes by Q7, regions. Features `m2.cdt`, `m2.domain`, `m2.regions`. Decide how the mesh stores subsegments (R4).
3. **M2b: noding.** Crossings, overlaps, T-junctions; a crossing per D1. `m2.noding`. F-1 to F-9 are the gate; F-2 is a former stack overflow, so no recursion on the input size.
4. **M2c: snapping.** Automatic and explicit tolerance. `m2.snap`. D14: choose the method against F-10 to F-22, which are hard, and read them yourself; F-16 asks for a shared piece that #201's chosen routing does not make in F-15, and #201 never ran it.
5. **M3: refinement.** Minimum angle, area, size function, Steiner limit, locks, z on the reference mesh. Ruppert 1995, Chew 1993, Shewchuk 2002 (as used in `IntrinsicTriangulation`, see SOURCES.md). `m3.refine`, `m3.locks`, then `m3.refine-again`.

Not in this plan: grid, wedge, Steiner reduction and smoothing (O12 to O15). The catalogue has rows for them as `m4.*`, a stage only #200's document proposes. Ask the owner when M3 is done; the production path uses them ([USED]).

## 4. How a milestone arrives in the gates

`benchmarks/Meshing2Benchmark/Candidates/CandidateRegistry.cs` is the switch. A new mesher:

1. Write an adapter `IMeshing2Candidate` next to the prototype: input `Mesh2Input`, output `Mesh2Output`, with the mesh's subsegments, neighbours, labels, bad list and flag. Argument errors are thrown, as the library will.
2. Register it in `For(milestone)` and its features in `Features`; add the real class name to `ProductionTypes` if it differs from `g3.DelaunayMesh2`, `g3.ConstrainedDelaunayMesh2`, `g3.QualityMesh2`.
3. Run `Meshing2CatalogueTests`. **Every row of that feature now fails** with "still pending". Move the rows to covered: in `Catalogue.Rows`, make `Mesh` set the state by feature (or a row at a time), run them, and fix what fails. Do not move a row without its fixture passing; if the fixture is wrong, change it with a note.
4. Regenerate the catalogue table (section 5, gate C). Then record the golden rows.

**Recording M2 and M3 golden values** (none exist yet, by design):

```
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- golden update --milestone M2 --reasons <file>
```

`<file>` has one line per case, `<case><TAB><reason>`. The command recomputes every case of the milestone whose catalogue row is **covered**, and writes `golden/m2.json` only if all pass the oracle and every new or changed row has a reason. For M1 it also runs the NetTopologySuite differential; for M2 and M3 no reference exists in this repository, so
the oracle alone gates it until the owner decides on the reference rows. `golden check` compares, the test `Meshing2GoldenTests` does on net10 and net48: extend `M1_RecordedRows_AreReproduced` to the new file. CI's step "A changed golden row" fails a pull request whose changed row has no new reason.

Keep the golden subset small: the CI part should stay within about a minute. If Steiner coordinates differ by runtime (they should not, unless trigonometry or an inexact sum enters), give them a stated tolerance or one value per runtime, and never loosen `triangles_fp`'s meaning for what has no constructed vertices.

## 5. The gates, and their state now

Commands from the repository root. "Now" is 2026-10-31 on the branches of this preparation; see the PRs for CI.

| Gate | Command | Now |
|---|---|---|
| A. Oracle self-check | `dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- selfcheck` | **Runs and passes**, 153 checks; independent review done, its findings fixed (section 6) |
| B. M1 differential | `... -- differential --larger` | **Runs, passes** on the prototype: 24 same, 7 both valid; NetTopologySuite at fault on 4 |
| C. Catalogue | `dotnet test tests/geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~Meshing2CatalogueTests"`, table: `... -- catalogue` | **Runs, passes**: 16 covered, 99 pending, 8 deferred, 3 blocked. A pending row fails once its feature is registered |
| D. Golden M1 | `... -- golden check`, and `Meshing2GoldenTests` | **Runs, passes**: 28 rows with `producer` `pr200-prototype`. Identical on net10 and net48 on Windows, by running; Linux net10 in CI. Tied rows will change once with the new kernel, with that reason |
| E. Golden M2, M3 | `... -- golden check --milestone M2` | **No values**: nothing to record until the features exist. Prints "no candidate yet" |
| F. All together, as CI | `... -- ci`, in `.github/workflows/ci.yml` | **Runs, passes** on the owner's Windows machine; in CI on the Linux job (the benchmark project is built for net10 there; the tests on net48 cover the rest) |
| G. Operation tests | `dotnet test tests/geometry3Sharp.Tests -c Release --filter "FullyQualifiedName~Meshing2CandidateTests"` | **Runs, passes** on the prototype: oracle per case, invariance over `Similarity.All()`, reuse, repeated, parallel, range. They follow the registered candidate |
| H. Other producers | `... -- compare --rows <file>`, or `G3_MESH2_REFERENCE_ROWS` | **Runs, skipped with a message**: no rows. Not in CI. The reference session is planned, not started |
| I. M1 accepted | B, D, G on the **new** kernel, plus A-B timing per spec 3.1 in the README | **Not met**: the kernel does not exist. Performance gate not written: there is nothing to measure yet; compare the targets of spec 3.1 on net10 and net48 with procedural inputs |
| J. M2 accepted | all `m2.*` rows covered; golden M2 recorded; D1, D5 settled | **Not met**: nothing exists. F-1 to F-22 and F-43 to F-48 are the fixtures |
| K. M3 accepted | all `m3.*` rows covered; golden M3 recorded; review of the bounds | **Not met**: nothing exists |
| L. CLAUDE.md, before every push | the list there; add to it mentally: gate F | See each PR |
| M. Mutation | `tests/mutation` covers exact code only. For the mesher, hand mutations until it is added: section 6 | See section 6 |

## 6. What the oracle was checked against

**Independent review.** A subagent that got the code and the spec but not the author's reasoning found 13 wrong verdicts by running them, among them: a filled hole accepted because a large tube gave area slack; a self-granted "Steiner limit reached" that lifted all bounds; a vertex outside the hull with a notch that cancelled its area;
"few ulps off a segment" excusing gross violations; a correct mesh of F-43/b rejected because a removed stretch of the ring still demanded edges. All are fixed, and each is a regression in `SelfCheck.ReviewFindings`. Its arithmetic held up: 0 errors in 20 000 random rational cases, 104 000 M1 fuzz runs and 10 400 M2 fuzz runs with no wrong verdict.

Left as they are, with the reason:

- **A chord within a large tube.** With an explicit snap tolerance, a short edge with both ends within the tube of a segment can be reported as a subsegment, which spares it the Delaunay check. Only endpoints are tested. Suspected by the review, not demonstrated; bounded by the tolerance.
- **A short subsegment near a crossing**, within the tube of both sources, expects marker 0 (trap 6).
- **`AngleBelowBound` may be too strict near small input angles**: a correct refinement can leave small angles in triangles that do not touch the apex (Shewchuk 2002). If a real result fails this way, widen the exemption with a reason; do not lower the check in general. F-38 therefore has no angle guarantee.
- **The Steiner flag** must show at least half of the limit as constructed vertices, and the area exemption at locked edges is "height at most half of the edge". Both are judgement calls of this session, not spec values, each at one place in `Oracle.cs`.

**Mutations** (by hand, restored after each; list in the PR): 48 mutants of `Oracle.cs`, `Exact.cs`, `Domain.cs` against the self-check, each check removed or weakened: all killed, one of them by the timeout, after five first survivors became new self-check items (a triangle without area; one triangle twice; three triangles on one edge; the lifted angle at a locked segment; a marker conflict).
26 mutants of `Golden.cs`, `Fingerprint.cs`, `TextFormat.cs`, `Differential.cs` and the prototype kernel against the Meshing2 tests: 22 killed. The kernel's mutants (no legalization, inverted in-circle, no on-edge split, no mapping of duplicates, no range check, and others) all fail the oracle. Survivors: dead code in `Fingerprint.Coordinate`, removed; "changed row without a reason" in `Update`, now tested and killed by `Update_ChangedRow_WithoutANewReason_IsRefused`; `-0` written unnormalized by the kernel, harmless because every consumer treats the zeros as equal; and the "different triangle sets" line of the differential, which only a hole in the oracle can reach, as two valid results on a unique input cannot differ. It stays as the alarm.
Also checked by mutation: registering `m2.noding` fails its 15 pending rows, and a production type without its feature fails its test. Not a score: hand-picked mutants. Stryker over `Oracle/` and the new mesher is open; `tests/mutation` needs a new config for it.

## 7. Known traps

1. **Golden rows do not prove anything**: they pin. Record only after oracle and differential. Of the 28 rows 4 have no differential: NetTopologySuite is at fault there, as their reasons say.
2. **`unique` is decided by the oracle per result**: on valid output it is a property of the input. A difference there between producers is a real finding.
3. **Do not use `Sin`, `Cos` in fixtures.** `Math.Pow(2, k)` and basic arithmetic are the same everywhere. `double.Parse` on net48 is not correctly rounded in all cases: fixtures use dyadic literals, and `0x` tokens in text.
4. **Culture.** This machine runs German: `0.5.ToString()` gives `0,5`. All text goes through invariant formatting; a catalogue id with a decimal point was once wrong this way.
5. **The oracle is slow by design**: `BigInteger`, about 1 to 2 s per 1000 points, with its global pass up to `GlobalDelaunayLimit`, and its segment checks are quadratic in constraints times vertices. Use it on the subset; for performance runs validate one sample and measure the kernel alone. F-21 and F-27 are "performance rows": their time is the benchmark's job.
6. **Tubes.** `Tube = null` means automatic, 0 means exact, and tube 0 fails on a rounded crossing by design (D1). Marker checks near crossings use the tube; in a very short subsegment both sources count.
7. **Seed-only holes**: the oracle has no arrangement of input constraints, so a case with a seed outside all hole rings must state its area (otherwise `CannotJudge`), and it still demands chains of segments that lie inside such a hole. No fixture has both today; extend `RetainedPieces` if one must.
8. **Fixtures are analogs.** The spec's decimals became dyadic, its `sin(i)` a table, its seeds another generator. F-5's seeds do not reproduce its cases. Some rows state a sensible tolerance of mine (`StatedAreaTolerance`), which the spec gives only loosely. If a row looks unreachable, reread the spec's text, then change the row with a note.
9. **F-30/a** expects a mesh today; D12's default turns it into an argument error. Change the row when building it.
10. **The full test suite has time limits that react to load** (`RobustnessSweepTests`; ROADMAP: measure alone). A timeout in a sweep during parallel work is no finding about this module until it repeats alone. This session shared the machine with another session's builds and tests throughout. **No timing measurements while the machine is shared**, and give every mutation run a time limit and kill what it leaves: a hung test host reached 8 GB.
11. **Spec items the plan leaves open for you**: O8 policies (splitting per outer boundary) map onto `Locked` in fixtures, the API is yours; I8 and F-33 need an API for an existing mesh; F-50 needs D10's helper.

## 8. What to reuse

- `tests/geometry3Sharp.Tests/Meshing2/`: oracle tests, candidate tests (the CLAUDE.md requirements for an operation), golden and catalogue tests; they follow the registry.
- `ExactPredicates.Orient2d`, `InCircle` (public). No new predicates.
- `mesh_ops/ExactTriangulation2.cs`: internal; useful for reading how constraints are recovered, unusable as it stands (distinct points in one enclosing triangle, already split constraints).
- `SelfCheck.DelaunayByDefinition`, an O(n^4) answer for small inputs in general position.
- #201's M2 document for ideas, under `git show origin/docs/meshing2d-m2-design:docs/design/MESHING2D_M2.md`, with the warnings of the decisions document. Do not start from its API.

## 9. Progress of the implementing session (from 2026-11-01)

Kept up to date by the session that implements M1 to M3; it overrides the "now" column of section 5 where they differ.

**Oracle review.** A second independent review (code and spec only) of the oracle and the golden tool demonstrated seven defects, all fixed, each a
regression in `SelfCheck.SecondReviewFindings` that fails on the old code (checked): a gap bounded by constraints hidden in the area slack of an
unrelated merge; hull corners not forcing their angle; the standalone oracle rejecting every M2/M3 text result (no neighbours in the format); culture-dependent
hashes and texts (sv-SE minus sign); lenient text and JSON readers; a re-removed golden row without a new reason; an unchecked bad list. One old self-check
item ("the same small angle, not forced") was itself wrong and is replaced. Left for M2/M3, where the rows are built (review findings 8 to 10):

- F-16's third segment passes S at about 0.7, not 1 mm: correct the fixture before building `m2.snap`;
- F-31/a says "locked outer boundary", but rings cannot be locked: add a lock to `RingInput` (O8 policy 1) with M3;
- F-13's micro rings are open polylines and their noise exceeds 1e-13: close them, scale the noise;
- F-30/a must expect an argument error (D12): the oracle needs that expectation;
- `TouchesLocked` exempts every triangle at a locked edge from the angle bound; the area check has a narrower rule. Narrow it with M3.

**M1: done** (gate I, except net48 timing). `comp_geom/DelaunayKernel2.cs` (internal kernel), `DelaunayMesh2`, `TriangulationMesh2`,
`Triangulation2Result`. Numbers in the [benchmark README](../../benchmarks/Meshing2Benchmark/README.md#m1-the-production-kernel-g3delaunaymesh2-2026-11-01):
1 000 000 points in 1.9 to 2.7 s, 65 536 in 0.11 to 0.15 s, 4096 in 6 to 10 ms and about 1 MB (PR #200: 86 s and 23.6 GB); net48 timing open. Decisions taken:

- No supertriangle: ghost triangles at a vertex at infinity, Bowyer-Watson cavities. This is the structure M2 (cavities that stop at constrained edges) and
  M3 (Steiner insertion) build on.
- D18: each cocircular Delaunay cell is a fan from its smallest input index, applied in one pass after the insertion; the output is independent of the
  insertion order (test `TieRule_ResultDoesNotDependOnTheInsertionOrder`). The golden fingerprints of #200 did not change.
- Triangles are not sorted globally (that cost a second at 1M points through cache misses); their order is deterministic, each triangle starts at its
  smallest id. `TryLocate` keeps "the smallest id among the containing triangles".
- `IsValid` is linear and exact: orientation, adjacency, local Delaunay, a convex hull winding once, Euler. `Compute` runs the same checks on the kernel
  and returns `Failed` with `ProblemCoordinate` on a defect. `NumericalFailure` and `IterationLimit` are gone (D15).
- `Prototype200Candidate.cs` is deleted, as planned. #200 is still open; closing it needs the owner.

**M2 (a, b): built** (PR on branch `meshing2d/m2`, stacked on M1). `ConstrainedDelaunayMesh2` with `ConstrainedMesh2Input` (points, segments with marker and
lock, rings, hole seeds, regions by ring or seed, optional heights) and `ConstrainedMesh2` (vertices, input map, triangles, neighbours, labels, subsegments with
marker, marker list and lock, heights, `TryLocate` in non-convex domains), `Triangulation2Status` and two new failure kinds. All 26 catalogue rows of
`m2.cdt`, `m2.domain`, `m2.regions`, `m2.noding` with milestone M2 pass the oracle; golden M2 recorded. Decisions taken:

- D1 as decided: crossings exact, rounded to nearest double (own integer rounding, no BigInteger-to-double conversion that may differ by runtime).
- Automatic tolerance for routing: 2^-41 of max(largest coordinate, extent), half the oracle's 2^-40, so routing decisions in doubles stay inside the tube.
  A vertex within it of a piece and of all the piece's input segments becomes a vertex of it. No vertex is merged with another (only exact duplicates).
- Markers (D7): a subsegment carries the markers of all input segments within 2^-40 of both its ends, so nearly coincident lines share their pieces' markers.
- Heights (D11): input heights pass through (lowest index wins, with a diagnostic); a crossing gets the height interpolated along the later piece. Q9's
  interpolation of Steiner points on the reference mesh is M3.
- Oracle change: a chain may end at a vertex in the tubes of its segment and of the cutting ring edge (a crossing at a tiny angle is ill-conditioned along
  the lines: found by fuzzing, 7.5e-6 along at an angle of 1e-10); the movement it measures counts only the distance off the two lines; a ring edge
  along the source itself never counts as cutting (the independent review found that hole; negative self-check "two parcels without their shared border").
- Independent review of M2: six defects fixed with regressions (a non-Delaunay edge after a crossing: restore before inserting, and a final linear
  check; tolerance tests underflowing at 2^-560: scaled differences, exact collinear direction; markers missing sources hidden behind a free point: a
  grid of sources; crossing heights depending on the order: the segment given first wins, with a diagnostic; rounding in the subnormal range documented
  as within one ulp). Crossings in the subnormal range can lie farther from their segments than the tolerance, because the double grid there is coarser.

Not yet: `m2.snap` (explicit tolerance, vertex clustering, wedges; D14: choose against F-10 to F-22), and the M3-milestone rows of the M2 features.
Performance after the review fixes: 400 000 coastline points in 3.1 s (spec: below 5 s), 400 000 parcel points in 3.7 s; the crossing-heavy case allocates too much (2 GB for 490 000 crossings).

**M3: built, with known limits** (PR on branch `meshing2d/m3`, stacked on M2). `QualityMesh2` (`MinAngle` default 20, `MaxArea`, `SizeFunction` by D9, `SteinerLimit`
by Q13, `Conforming`) on the M2 builder: Ruppert's algorithm with Shewchuk's refinements. Encroached subsegments first (exact dot product), circumcentres of bad
triangles unless they encroach (then the subsegment), concentric shells around input points and crossings, split points computed on the original input
segment. Triangle tags (domain, region) live in the kernel and follow splits and flips; after every operation the rewritten triangles take their tags anew from
unchanged neighbours (ghosts count as outside). Without outer rings the hull edges become constraints (D13). Heights of Steiner points by linear interpolation
on the reference triangulation (Q9). Decisions and findings:

- D12 built: `MinAngle` outside [0, 60) is an `ArgumentException`; catalogue row F-30/a now points to the test `QualityMesh2Tests.InvalidMinAngle_IsAnArgumentError`.
- Exemptions from the angle bound: the small corner between two constraints (O3), and, new, a skinny triangle whose shortest edge spans a small input angle
  (Shewchuk 2002). The oracle got the matching exemption `SpansSmallInputAngle` with an accept and a reject self-check. Without it F-39 took 103 000 vertices
  (the Steiner limit), with it 649.
- Points within the automatic tolerance of an earlier point are merged into it (O11, Q14), for M2 and M3 (F-3/c needed it: a point 2^-39 from a segment's end).
- Oracle changes found with M3, each with its reason in the code: at the Steiner limit the mesh is judged as constrained, not conforming Delaunay (O7; a flipped
  free edge at the limit is still rejected, self-check 9); a region seed on a Steiner vertex is not ambiguous (only on input points or constraints); boundary
  vertices within the tube of a hull edge count as moved (O2, D13: a split of a hull edge in doubles cannot stay exactly on it).
- Bugs found and fixed on the way: splitting a segment lifted its constraint before inserting (non-Delaunay edges and mixed tags, as in M2's crossings);
  the source lists of an edge grew without bound when a piece was re-inserted (now a union; out of memory before); split points on hull edges moved inwards
  left slivers at unconstrained hull edges (now they may round outwards; Lawson's flips remove the old hull edge).

**Review of M3** (independent agent, 2026-11-01), fixed on the same branch:
- Critical: a split lifts the constraint of its piece, and the flips around it may cross the boundary; a small hole or region enclosed by constraints then
  kept the tag of the slots it was rewritten into (filled holes, wrong labels, regions with an area bound never refined). Now the pieces of a split give
  their sides the tags the lifted piece had. Conforming mode also counts vertices of holes and of the outside as encroaching (O2 is about all vertices).
  New fuzz over holes, regions with and without area bounds (`Fuzz_HolesAndRegions_PassTheOracle`, 80 inputs).
- Angles, areas and circumcentres underflowed at 2^-500 and refinement silently did nothing; now computed on edges scaled by a power of two
  (`ExtremeScales_RefineTheSameMesh`, 2^-560 to 2^225, bit for bit).
- The small-input-angle exemption was a factor-2 window in distance; now 1.5, in the library and in the oracle, with a known-wrong self-check at 1.53. All 41
  covered M3 rows still pass (`rows --milestone M3`, on de7cf59). Vertex counts: unchanged except F-5 8 547 (was 6 229: the narrower window exempts fewer
  triangles near its small angles) and F-4/c 37 568 (was 37 567). F-44's mesh changed through the conforming fix (golden row updated with that reason;
  the same 28 vertices).
- The step cap reported the Steiner limit; it is now a failure `RefinementLimit`, and the limit flag is set only when an insertion was refused (a limit of 0
  on a good mesh gives no flag). The oracle's default limit counts ring edges as segments, like the library.
- Heights: the walk on the reference triangulation (not Delaunay with breaklines) is a stochastic walk from the last hit, with a scan as fallback.
- Oracle: a floating-point filter in front of the exact in-circle test (the holes fuzz went from 84 s to 10 s); negative self-checks for the D13 hull rule and
  for region seeds on input points (checked by mutation).
- Locked segments: a triangle whose circumcentre lies behind a locked segment is left as it is, so the bounds do not hold near locked segments (52 of 80
  random inputs). A point pulled back in front of the segment instead ran to the Steiner limit on 45 of 60 inputs (points creep towards the segment), so
  this is documented on `QualityMesh2` and stays with m3.locks; `Fuzz_LockedSegments_StayWholeAndValid` checks what holds.

**Corpus run** (owner's request, before M4; `corpus` command, details and table in the benchmark README): earcut, delaunator and poly2tri fixtures,
84 inputs, 336 runs; after the fixes 332 pass the oracle, none fails, 4 very large results are not judged. It found three library bugs in M3 and the hull
handling (stale tags after a split onto another constraint; chains merged at the tip of a narrow spike; points a few ulps inside a hull edge) and three
oracle gaps (empty on a sub-tolerance sliver, the merge zone at a small apex, its speed). Not yet run: Natural Earth, GEOS and CGAL data, the NRW
ALKIS and DGM tiles (local only).

**What remains (hand-over).**

1. **Over-refinement where many segments cross at small angles**, much reduced by the corpus fix that keeps split points out of the tolerance of
   other constraints: F-4/a 3 073 vertices (was 101 078 at the Steiner limit), F-5 5 116 (was 8 547), F-26 and F-27 909 (913). Earlier state:
   F-4/a (random segments at 25 degrees) ended at the default Steiner limit (101 078
   vertices; F-4/b needs 8 594, F-4/c 37 567, F-5 6 229, final run of `rows`); the oracle accepts them (a valid constrained Delaunay mesh, limit flagged), but Triangle-like meshers need a few thousand. The refinement
   descends towards apexes such as (3, 4) of F-4/a, where five segments cross at angles from 7 degrees up. Next step: Shewchuk's 2002 rules for small input
   angles beyond the shortest-edge exemption (do not insert a circumcentre that would encroach a segment incident to the small angle; protect the apex by a
   ball), then measure the vertex counts of F-4 and F-5 against the spec's "fewer than 5 000" of F-24.
2. **Slow oracle on large results: done** (owner's priority, branch `meshing2d/oracle-speed`): a fast path with floating-point filters and spatial
   indexes, the reference path kept (`--oracle reference`), verdicts compared in `Meshing2OracleModesTests`; `rows` 705 s of oracle time to 48 s,
   water-huge at 30 degrees about 170 s to 10 s (benchmark README). poly2tri's debug2 in M3 (a star of 10 000 sharp spikes) still runs into the
   Steiner limit at every angle, item 1.
3. **Slow rows.** `Catalogue.SlowRows` (13 rows with 16 000 to 200 000 vertices) are judged by `rows --milestone M3` (about 25 minutes in total, mostly oracle
   time); the test suite only checks their status unless `G3_MESH2_SLOW` is set. Speeding up the oracle on large results (orientation and edge maps take
   seconds at 100 000 vertices) would let them back into CI.
4. **Not built:** `m3.locks` (4 rows; locked segments are never split, but the bounds are not met near them: see the review of M3 above; a bounded
   off-centre point in front of the segment, with a minimum distance from it, is the next thing to try),
   `m3.refine-again` (I8, F-33, O17: refining an existing mesh needs an API), `m2.snap` (22 rows: explicit snap tolerance, D14), F-31/a's ring lock (O8).
5. Performance of M3 is not yet measured against spec 3.1 (F-27: more than 50 000 vertices in about 2 s; we do 60 000 in 0.4 s but at the limit), and net48 timing
   is open for all milestones.
