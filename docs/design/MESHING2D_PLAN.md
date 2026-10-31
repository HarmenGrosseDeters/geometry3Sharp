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
