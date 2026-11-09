# QA 2.0, step 1: inventory of tools x areas

Status: analysis only (2026-11-09, Sonnet analysis session for the coordinator). No library or test code changes.
Part of the [QA 2.0 milestone](../roadmap/platform.md#qa-20-milestone); strategy in [QA.md](../QA.md).

How to read it: section 1 is the state of master at `2c7ba9f`. Section 2 is the grid (one glance), section 3 has
one table per area (what exists, what the tool would check, expected finds, cost, constraints), section 4 the gap
list, section 5 the rollout order with "done when", section 6 the open questions. Numbers marked **(est.)** are
estimates from the cost of the pilots and of the mutation / CI logs in the docs, not measurements; the first task of
each tool replaces them with measured values.

## 1. State of the tools on master (what is real today)

| Tool | State on master | Where | Not on master |
|---|---|---|---|
| **SharpFuzz + libFuzzer** | STL reader pilot only (#442). One harness, 4 KiB inputs, 1 000-iteration local run, no finding; `ft` 487 -> 1077. Replay build and `verify_crash_path.py` run in CI without a campaign. | `tools/ReaderFuzzPilot` | The **mesh-ops pilot** of the Boolean session: its two findings (#451 `DMeshAABBTree3` build, #452 `MeshRoundingRepair.fix_degenerate`) and the sweep list are in [platform.md](../roadmap/platform.md#qa-20-milestone), but no harness code is in `tools/` on master. The pilot's harness must land (task F1) before any mesh-ops cell can be called "exists". |
| **FPTaylor** | Install script and smoke test; two comparisons against the generator's rules (`Orient3dLPI` leaf `ux`, composed `nx`), single domain each (#436). Not a certificate of any predicate. | `tools/FPTaylorPilot` | A runner in CI (needs an OCaml / Num build, LGPL toolchain kept out of the repo; only the Python comparison tests run in CI). |
| **Z3** | Six pilots, all in the `Z3 pilot` workflow (15 min): SnapGrid pixel fast branch and a #376 survivor; the error-bound rules of `tools/PredicateCompiler` (one lemma per rule); the SafeRounding D1 drift lemma (apex rule); conservative MarchingCubes (adapter, FP envelopes); the Devillers-Guigue prefilter of the arrangement; mesher checks (exact-corner filter bound, small-angle inequalities, #443). | `tools/Z3Pilot`, `.github/workflows/z3-pilot.yml` | Lemmas for the hand-derived filters of `ExactPredicates`, `ExactTriangleIntersection`, `PolyhedralEnvelope`; termination lemmas. |
| **CsCheck** | One area: `ExactMeshBoolean` (`ExactBooleanPropertyTests.CsCheck.cs`, `ExactBooleanMetamorphicTests`; net10.0 only, own shrinking, seed replay). The rest of the list in platform.md is open. | `tests/geometry3Sharp.Tests` | Repair / rounding, mesher, alpha wrap, MarchingCubes, terrain campaigns. |
| **Changed-line coverage (coverlet)** | Nothing: #434 is in rework and not merged; `coverlet` is not referenced by the test project. | - | The whole tool. |
| **Stryker shards** | 24 configs (arithmetic + 23 mesh) run by `mutation.yml`: arithmetic (3 files), arrangement 1-4, boolean 1-2, planar (2 files), fixed, generated, envelope 1-4, rounding 1-7, terrain-arith / terrain-kernel / terrain-overlay. Incremental gate on PRs, full runs on master after the merge (owner 2026-11-09). | `tests/mutation` | Any shard outside the exact code: repair, alpha wrap, readers, mesher, Clipper2, MarchingCubes, spatial structures, remesher / reducer. |
| **Test tiers and time limit** | CLAUDE.md rule: <= 5 s per test, exceptions to 10 s with `Slow`. #447 (tiers, `Slow` trait, fast tier) and the backlog program are not on master. Backlog before #447: 113 tests at 5-10 s, 44 at 10-30 s, 14 above 30 s. | CLAUDE.md, platform.md | The ratchet (`tools/trx_durations.py`), the fast tier. |
| **ReaderFuzzTests** | Seeded, blind mutation of valid files: STL (ASCII, binary), 3MF, GLB, OBJ, OFF, PLY (ASCII, binary), `.g3mesh`; "Ok means finite coordinates" twice; Triangle `.poly` / `.node` / `.ele` in `g3cli.Tests` (`MutatedTriangleFiles_ReadSafely`). | `ReaderFuzzTests.cs`, `TrianglePolyCliTests.cs` | Coverage guidance, `MiniJson` on its own, the MCP server's JSON-RPC input, the SVG / writers (not untrusted input). |
| **Similarity.All invariance** | The harness is used by 90 test files (`grep -rl Similarity.All tests`). Rule in CLAUDE.md for every new operation. | `Harness/Invariance.cs` | See the gap list for operations without one. |
| **Robustness sweep** (not in the task's tool list, but the cheapest instrument we have) | `RobustnessSweepTests.Ops` (25 `new Op` rows, counted in the file) x `Similarity.All()` + 2^-24 and 2^20 + NaN / Inf vertex and parameter. Found R1-R23 ([ROBUSTNESS_SWEEP.md](../ROBUSTNESS_SWEEP.md)). Also `RegressionCorpusTests` (64 defect meshes x 7 pipelines), `DefectInputTests`, `*IssueCasesTests`. | | The operations of the gap list. |

Reading rule for "exists": a class named in a cell was seen in the file listing and its header, not re-run here.

## 2. The grid

Legend: **done** = the tool is applied to the area to the extent the QA 2.0 "done when" asks; **part** = applied in
part (what, see the area table); **pilot** = pilot only; **open** = nothing yet, worth doing; **n/a** = the tool does
not fit; **low** = open but expected to give little.

| Area | SharpFuzz | FPTaylor | Z3 | CsCheck | Coverage gate | Stryker | Tiers / limit | ReaderFuzz (seeded) | Invariance | Sweep |
|---|---|---|---|---|---|---|---|---|---|---|
| A. Readers / writers | pilot (STL) | n/a | n/a | n/a | open | **open** (cheap) | applies | **done** (blind) | n/a | n/a |
| B. Repair / rounding (`MeshRobustRepair`, `MeshRoundingRepair`, `SafeRounding`) | open (finds exist, #452) | n/a | part (D1 drift) | open | open | part (rounding 7 shards; none for `MeshRobustRepair`) | applies (slowest tests) | n/a | done | part (`MeshAutoRepair`, `MeshPostRepairCleanup`) |
| C. Exact boolean, arrangement | open | part (hand check only) | part (prefilter) | **done** | open | **done** | applies | n/a | done | n/a (own corpora) |
| D. Exact arithmetic, predicates | n/a (no untrusted input) | pilot | part (generator rules) | low (differential tests exist) | open | **done** (arith, generated, fixed) | applies | n/a | done | n/a |
| E. Alpha wrap | open | n/a | n/a | open | open | open | applies | n/a | part | gap |
| F. 2D mesher `QualityMesh2` + oracle | open | n/a | part (mesher checks, pixel) | open | open | open (oracle tests slow) | applies (oracle) | part (`.poly`) | done | part (2 ops) |
| G. Clipper2 addon | open | n/a | open (frame bound) | open | open | open | applies | n/a | done (`Clipper2RobustnessTests`) | gap |
| H. MarchingCubes / contouring / DualContouring | open | n/a | part (conservative MC) | open | open | open | applies | n/a | part | part (SDF + MC) |
| I. Terrain | open | n/a | open | open | open | part (3 configs, not in CI) | applies (Terrain backlog) | n/a | done | gap |
| J. Remesher / Reducer | open | n/a | n/a | open | open | open | applies | n/a | done | part |
| K. Spatial structures / queries | **open (finds: #451)** | n/a | open (build termination) | open | open | open | applies | n/a | part | part (AABB, hash grid) |
| L. Other untrusted geometry (section 4) | open | n/a | n/a | open | open | n/a | applies | n/a | gap | **gap** |

Cross-cutting: the coverage gate and the tier work have no per-area content; the gate is one task (section 5, T10).

## 3. The cells, per area

Cost units: **A** agent hours of a bounded Sonnet task (test-only, includes the PR and its review round), **CI**
minutes added to one PR run (net10.0 unit tier) or per scheduled run, **R** runner hours (Haiku runner or the
owner's machine, long campaigns only). All **(est.)**. Constraints that hold everywhere: test time <= 5 s per test;
a new campaign gets its own test category that the Stryker filters exclude; CI runs a small seeded campaign; every
failing seed becomes a fixed example test (QA.md 2.4); no mesh files committed; the exact-code rules (FMA, `float`,
parallel loops banned in `math/Exact*`, `AdaptivePredicates.cs`, `mesh_ops/ExactMesh*` and their callees) apply to
code, not to test code, but a test must not touch those files; licences: SharpFuzz MIT, libfuzzer-dotnet MIT,
CsCheck Apache-2.0, FPTaylor MIT (its OCaml / Num toolchain LGPL-2.1 with linking exception, built locally, never
shipped), Z3 MIT, coverlet MIT, ReportGenerator Apache-2.0.

Evidence sources used below: [QA.md section 1](../QA.md#1-where-we-stand) (45 bug-fix PRs by fault class),
[ISSUE_CASE_FINDINGS.md](../ISSUE_CASE_FINDINGS.md) (R1, G1-G3, P1-P4, S1-S3, Q1-Q3), [ROBUSTNESS_SWEEP.md](../ROBUSTNESS_SWEEP.md)
(R1-R23), the mesh-ops fuzz findings (#451, #452), `git log`.

### A. Readers and writers (OBJ, OFF, STL, PLY, 3MF, glTF/GLB, `.g3mesh`, Triangle `.poly` `.node` `.ele`, `MiniJson`, MCP JSON-RPC)

Exists: `ReaderFuzzTests` (blind, seeded, mutates valid files: flipped bytes, truncation, duplicated slices, extreme
header numbers, extreme words, text tokens; checks "result or documented error", "Ok means finite"); `Reader*FixesTests`,
`GLTFFormatTests`, `PLYFormatTests`, `ThreeMFFormatTests`, `G3MeshReaderTests`, `SerializationRoundTripTests`;
`TrianglePolyCliTests`. STL pilot under SharpFuzz (`tools/ReaderFuzzPilot`).

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| SharpFuzz | One harness per reader on the `FormatReader.ReadFile(Stream, ...)` path (not `StandardMeshReader`, which hides exceptions), with the structural checks of `ReaderFuzzTests` added to the pilot (finite coordinates when Ok, index ranges, a size budget for allocation). Seeds generated procedurally per format (PLY binary with properties, 3MF zip + XML, GLB with accessors and buffer views, `.g3mesh`). Dictionaries for tokens. Extra targets: `MiniJson` alone, `TrianglePolyFormat.ReadPoly/ReadNode/ReadEle`, `McpServer` request parsing. | Medium. Blind fuzzing already found bugs in three PRs (#130 `.g3mesh` trusted buffers, #131 OBJ / OFF / PLY index overflow and header-sized allocation, #136 non-finite coordinates). Coverage guidance reaches nested structure that byte flips seldom hit: 3MF (ZIP central directory, XML), GLB (JSON, accessor / bufferView arithmetic), PLY property lists. The STL pilot found nothing in 1 000 iterations (a smoke run, not evidence of safety). Expect 1-3 defects over all formats. | F2: 3-4 A for the generalised harness + 6 seeds; R 1 h per target on first run, 8-10 targets (est.) | Large campaigns on a runner, not CI. Replay corpus not committed if it is a mesh file: commit only a generated seed script; a crash becomes a procedural byte-builder test. |
| Stryker | One shard over `io/*Reader.cs`; the reader tests are fast (seeded fuzz plus format tests). Survivors in bounds checks are silent-OOB risks. | Medium-low: it finds untested guards (the #131 class) | 1-2 A + CI (a shard of about 1-2 h, est.) | Needs a test filter that excludes the fuzz theory if it is slow per mutant. |
| Coverage gate | Applies to every reader change. | - | with T10 | - |
| ReaderFuzzTests | Done for the formats; add `MiniJson`, MCP JSON-RPC and CLI argument parsing as seeded tests (cheap). | Low | 1 A | The seeded fuzz stays in the unit suite. |

### B. Repair and rounding (`MeshRobustRepair`, `MeshAutoRepair`, `MeshPostRepairCleanup`, `MeshRoundingRepair` / `SafeRounding`)

Exists: `MeshRobustRepairTests`, `MeshRepairIssueCasesTests`, `RobustRepairScaleDisparityTests`, `RobustRepairFarPartTests`,
`MeshRoundingRepair*Tests` (8 classes), `SafeRounding*`, `Rounding*Tests`, `HugeCoordinatesRepair*`, `DefectInputTests`,
`RegressionCorpusTests`; mutation: `rounding-1..7` (threshold 59 each, 145-294 min per part); Z3 D1 drift lemma
(`drift_apex.py`). No shard for `MeshRobustRepair`, `MeshAutoRepair`, `MeshPostRepairCleanup`.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Generator of multi-component inputs with very different sizes and distances (the platform.md item 1). Properties: `Apply() == true` implies clean by `NotCleanReason`; no defect the input did not have; every vertex displacement <= MaxDrift; 2^k scale invariance bit for bit; reuse. Shrinking by component, then triangle (`MeshMinimizer`). | **High.** It would have found #420 and #425 (far parts) by the roadmap's own statement; fault classes "silent success" (10 fixes), "scale / far from origin" (8), "degenerate input" (7), "open / bowtie" (5) in QA.md. R22, R23 (SafeRounding) came from a sweep, so more of these are likely at the boundary of the bound. | C1: 6-8 A, CI +1-2 min seeded, R 2-4 h for large campaigns (est.) | Repair takes 4-11 s on 4-12 triangle inputs through the wrap / solidify fallbacks (roadmap sweep list): a campaign needs generators that avoid the fallback or budgets per case; the 5 s test limit is binding. Keep it out of the Stryker filters. |
| SharpFuzz | Mesh-ops target: triangle soup from bytes (counts, coordinates as doubles incl. subnormal, huge, adjacent) into `MeshRoundingRepair`, `MeshRobustRepair`, `MeshAutoRepair`; oracle = no exception outside documented types, a time budget, finite output, `NotCleanReason` when `Apply()` true. | **High, evidenced:** #452 (`fix_degenerate` hang below ~1e-154), 4-11 s fallbacks. | F1 (the mesh-ops harness, shared with K, C, E, J): 4-6 A; then R 2-3 h per target | Hang detection needs the per-input timeout of the pilot (5 s); a target that routinely takes seconds needs a size cap in the harness. |
| Z3 | Termination / progress lemmas of `fix_degenerate` (the #452 loop under subnormal scale); more drift accounting (D2, D3) next to D1. | Low-medium: it explains and pins what the fuzzer finds; D1 exists | 3-4 A each (est.) | Model-to-source correspondence stays a review item. |
| Stryker | `MeshRobustRepair`, `MeshPostRepairCleanup` and the result checks (`NotCleanReason`, `MeshRepairMetrics`: QA.md 2.2 names them as next target; a survivor there is a silent-success risk). | Medium-high for the result checks | Tests are slow (repair tests 30 s class in the old suite): needs a reduced filter first; a shard is 3-5 h CI (est.). Do after the tier work. | Combined-gate rules of AGENT_WORKFLOW section 8. |
| Invariance, sweep | Done for the main operations. The sweep covers `MeshAutoRepair` and `MeshPostRepairCleanup` only. | - | - | - |

### C. Exact boolean and arrangement (`ExactMeshBoolean`, `ExactMeshArrangement`, `ExactPlanarSimplify`, `ExactTriangulation2`)

Exists: ~25 test classes (`ExactMeshBoolean*`, `ExactBoolean*`), CsCheck campaigns and metamorphic tests,
`ExactBooleanIssueCasesTests`, `BooleanReviewTests`; mutation: arrangement 1-4 (thresholds 67-72), boolean 1-2 (79),
planar (67), plus the shared tests; Z3 prefilter pilot. This is the best-covered area.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| SharpFuzz | Boolean on two byte-built meshes (inputs closed or not): clean-or-documented status, `NotCleanReason`, volume identity when both closed, order independence. | Medium. The CsCheck campaigns cover valid and near-degenerate closed inputs by construction; the fuzzer adds open, non-manifold and tiny / huge inputs. Evidence: R22 / R23 (found by the sweep), #451-class overflow in shared code. | shares F1; R 2-3 h | Exact files are on the banned-API list; a harness lives in `tools/`, not in them. |
| FPTaylor | The hand-derived filter bounds of `ExactTriangleIntersection`, the arrangement's `Devillers-Guigue` prefilter and `ExactPlanarSimplify`; compare with FPTaylor's bound as in the `Orient3dLPI` pilots. | Low-medium. #137 found an InSphere bound that was too small by differential test, so hand bounds do fail; the generator's bounds are already Z3-checked per rule. | 2-3 A per filter, one domain each (est.) | Only the expression and range; gates and subnormals stay outside; one result per run (the runner accepts exactly one expression). |
| Z3 | Lemmas for filter rules of hand-written filters and for termination rules of the arrangement (the "every filter or termination rule" clause of the done-when). | Medium (pilot 4 found nothing recorded as a defect; its value is a pinned proof). | 3-4 A per lemma | Real arithmetic models need an explicit FP bridge as in the pilots. |
| Coverage gate | New lines in `ExactMesh*` must be executed by the unit tier. | - | with T10 | Parallel loops banned, so no race coverage issue. |

### D. Exact arithmetic and predicates (`math/Exact*`, `AdaptivePredicates`, `math/Generated`, `FixedPoint3`)

Exists: `ExactPredicatesTests`, `PredicateDifferentialTests` (BigInteger, subnormal to 1e300, near-degenerate),
`ExactPoint3*`, `GeneratedPredicates*`, `ExactFilterBoundTests`, `ExactIntervalsTests`; mutation: arithmetic (86),
generated (92), fixed (81), arithmetic survivors recorded as equivalent or open (tests/mutation/README.md);
`tools/PredicateCompiler` with Z3-checked rules; FPTaylor pilot (two comparisons).

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| FPTaylor | Each hand-derived or generated filter bound, one expression per file: `ExactPredicates` Orient2d / Orient3d / InCircle / InSphere filters, the `Orient3dLPI` and `LPI` / `TPI` family beyond `ux` and `nx`. | Low-medium. Value is independent derivation, not bug yield; the one past bug of this class (#137) was a bound too small by hand. | 3-4 A for the first 4 predicates, then 1 A each (est.) | Binary64 round-to-nearest only; trigonometric and constraints not covered; model-to-source drift check as in `compare_nx.py`; subnormal inputs need an explicit gate. |
| Z3 | Remaining per-rule lemmas, `AdaptivePredicates` expansion-sum / termination lemmas (hard), the `FixedPoint3` width bounds (`FIXED_WIDTH_EXACT_POINTS.md` table). | Low, but it closes the "every filter has a lemma" criterion | 4-6 A for expansion lemmas (est., risky; "not worth it now" is a valid result) | Keep to quantifier-free binary64 queries; UNKNOWN never counts. |
| CsCheck | Low: differential tests with BigInteger and exponent spread already do this. A campaign would only add shrinking. | Low | 2 A, skip unless a bug appears | - |
| Stryker | Done; open survivors listed in the README. | - | - | - |

### E. Alpha wrap (`MeshAlphaWrap`, `AlphaWrapOracle`, quality pass)

Exists: `AlphaWrap*Tests` (9 classes; `CheckWrap`: closed, manifold, no self-intersection, encloses, distance band against
a brute-force oracle), `AlphaWrapIssueCasesTests` (9 failure modes), `RegressionCorpusTests` (alpha wrap clean on 62 of 64).
No fuzz, no shard.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Random soups (platform.md item 3): closed, manifold, no self-intersection, encloses the input, distance band, deterministic, 2^k invariance; shrinking to minimal pinch / cap cases. | Medium. The area changed a lot (pinch refinement, local trials, quality pass, Phi gates; 5 fix commits in the last weeks); the corpus (64 cases) is fixed. | C3: 5-6 A; CI +1 min seeded; R 2 h | Wrap on random soups can be slow: small point counts and a fixed alpha; use `CheckWrap` as the oracle. |
| SharpFuzz | Mesh-ops target: soup bytes, alpha / offset parameters (incl. 0, NaN, huge) into the wrap; budget and exception policy. | Medium-high for parameters and extremes (hard limit vs success, point limit) | shares F1 | The point limit is a documented stop; the harness must not count it as a failure. |
| Stryker | Not yet: wrap tests are slow, and there is no baseline. | - | after tier work | - |

### F. 2D mesher `QualityMesh2` with its independent oracle (`comp_geom/QualityMesh2.cs`, `SnapRounding2`, `ConstrainedDelaunayMesh2`, Meshing2 acceptance)

Exists: 27 `QualityMesh2*Tests` plus `Meshing2*` (catalogue, golden, oracle modes, NTS differential in CI),
`StarPolygonFuzz` (seeds 0-2999, a deterministic fuzz of small snapped inputs), `SnapRounding2Tests`,
`TrianglePolyCliTests`; the oracle's checks; Z3 pilot 3 (`mesher_checks.py`: the exact-corner filter bound and the
small-angle inequalities) and pilot 1 (the `SnapGrid.Pixel` branch). Many rule fixes in the last two weeks
(small-angle rule, conforming mode: #441), which is the best evidence of where a campaign finds things.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Random polygons with holes, near-parallel and near-touching segments against the oracle (fast since #411 / #427). Properties: input segments are unions of output edges; angle bounds; conformity; areas sum to the domain; snapped input stays within the snap tolerance. | **Medium-high.** `StarPolygonFuzz` seeds 0-2999 rejected 211 (197 by one rule) at the time of Q17: a seeded generator already found real rule gaps; a shrinking campaign with richer inputs (holes, collinear chains) should find more. | C2: 6-8 A; CI +1-2 min; R 2-4 h | The oracle uses BigInteger rationals: keep to inputs where it takes < 1 s per case (oracle timing tests run in a `DisableParallelization` collection; do not add a wall-clock test). |
| SharpFuzz | Byte-built point / segment lists and parameters into the mesher; checks the documented status, output validity by the cheap parts of the oracle, no hang (mesher hangs on adversarial input would be the #452 class; the `.poly` d^2 cost is parked). | Medium | shares F1; extra 3 A for the oracle-light checks | Triangle-style input has parked limits (platform.md Parked): list them as known, do not "find" them again. |
| Z3 | More of `MESHING2D_SMALL_ANGLES` inequalities and the zone filters (`Meshing2OracleZoneFilterTests` sit behind the pilot). | Low-medium | 3 A per filter | As in D. |
| Stryker | Not now: the mesher's slow tests (a TagCheck of 753 s, a conformity sweep) make per-mutant runs infeasible before the Meshing2 oracle tier cut. | - | after the backlog program | - |

### G. Clipper2 addon (`addons/geometry3Sharp.Clipper2`: `Clipper2Polygons`, `ClipperFrame`)

Exists: `geometry3Sharp.Clipper2.Tests` (`Clipper2RobustnessTests`: frame, `Similarity.All()`, determinism, degenerate and
invalid input; `Clipper2ReviewFindingsTests`, offset, examples, an independent `Oracle.cs`); CI builds and tests it on
net10.0 and net48; package smoke test. No fuzz, no property campaign, no shard.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Random polygon sets against `Oracle.cs`: boolean identities (area(A u B) + area(A n B) = area A + area B), difference / xor partition, offset monotonicity, simplify within epsilon, frame round trip (the grid is a power-of-two scale with 40 bits). Regions near the double range. | Medium. The review findings test file (huge miter limit, near-double-range results, invalid frame, far clip polygon, ...) shows what the adapter's risk is: the frame and the conversion, not Clipper2's core. | C4: 4-5 A; CI +<1 min | Own test project; the addon's tests also build for net48 (CsCheck is net10.0 only: keep a `*.CsCheck.cs` split as in the main project). The addon is Boost-licensed; do not port Clipper2 code. |
| SharpFuzz | Polygon / path bytes into `Boolean`, `Offset`, `Simplify`, `MinkowskiSum` (public methods taking untrusted polygons). | Medium (the `Result` codes for overflow and invalid input; Minkowski and offset parameters) | 3 A once F1 exists | Clipper2 core is a dependency (Boost): the fuzzer will find defects in the dependency too; report upstream, do not patch. |
| Z3 | A lemma that the frame's scale and origin keep every coordinate within the Int64 range Clipper2 needs (`ClipperFrame.ForBounds` 2^40 margin). | Low (the test already checks 2^39 < extent * scale < 2^40) | 2 A | Cheap, tidy first Z3 cell outside the core. |
| Stryker | `ClipperFrame.cs` (small) in the addon's own test project. | Low-medium | 2 A + 20-40 min CI (est.) | The mutation config is per test project: needs its own config. |

### H. MarchingCubes, contouring, DualContouring, signed distance grids (`MarchingCubes`, `MarchingCubesPro`, `DualContouring`, `TerrainDualContouring`, `MeshSignedDistanceGrid`, `WindingContouring`)

Exists: `MarchingCubesConservativeTests`, `MarchingCubesContinuationTests`, `ContouringQuality*Tests`,
`DualContouringTests`, sweep entry "MeshSignedDistanceGrid+MarchingCubes" (R3, R9, R10 found there),
`ThreadingFixesTests`; Z3 conservative-MC pilot (adapter, FP envelopes, relative-error validation); fixes for
`LinearEdges` (grid limit, continuation seeds, inclusive grids).

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | `LinearEdges` enclosure on random SDF compositions; vertices stay in cells; closed manifold output where documented; bit determinism parallel vs serial. | Medium. The area had nondeterminism (#124 first-evaluation double vs float), absolute 1e-5 root-finder thresholds (R10), grid limits and hash overflow (4 recent fix commits on `LinearEdges`). | C5: 4-5 A; CI +1 min | Keep grids small (a 64^3 grid is already seconds). |
| SharpFuzz | Grid size / bounds / spacing / field value inputs (arg validation is the risk: R9 found IndexOutOfRange in a parallel region). | Medium-low (R9 already closed the known path) | shares F1 | - |
| Z3 | Done for the conservative enclosure; extend to `MarchingCubesPro` hash / grid-limit arithmetic (the "16-bit hash" refusal). | Low | 2-3 A | - |

### I. Terrain (`TerrainGreedyTIN`, `TerrainDualContouring`, `TerrainSlope*`, `TerrainSafety`, `TerrainOffset`, `TerrainVolumeDifference`, `ExactSlope*`)

Exists: ~50 `Terrain*Tests` classes incl. oracle tests (`TerrainSlopeOracleTests`), invariance (`TerrainSlope*Invariance*`),
snap certificates, the longest tests in the old suite (safety lines 35 s); three Stryker configs for `ExactIntervals`,
`ExactSlopePredicates`, `TerrainSlope`, `ExactSlopeKernel`, `TerrainSlopeOverlay` without `break` thresholds, not in
`mutation.yml`; benchmark `TerrainBenchmark`.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Metamorphic relations (platform.md item 4): result never steeper than the design slope; cut + fill consistent; translation by UTM and 2^k scale; DEM with no-data cells. | Medium. Terrain is the area with the most distinct design classes (toe crossing, bands, catch lines); QA.md ranks "scale / far from origin" at 8 fixes. | C6: 5-6 A; CI +1-2 min | Terrain tests are the largest part of the 5-30 s backlog: the campaign must be cheap (small DEMs) and must not add to the backlog. |
| Stryker | Wire the three existing configs into the workflow with first thresholds (they exist). | Medium: `ExactSlope*` are exact-style code | 2 A + (CI h est. from the configs' own runs) | The terrain tests are slow: a reduced filter first. |
| SharpFuzz | DEM / polygon inputs: low priority; `Terrain*` takes grids, not files. | Low | later | - |
| Z3 | `ExactSlopePredicates` rules (exact intervals): optional. | Low | 3 A | - |

### J. Remesher and Reducer (`Remesher`, `RemesherPro`, `RegionRemesher`, `Reducer`, `PartitionedReduction`)

Exists: ~20 test classes (`Remesher*`, `Reducer*`), issue cases (13 + 10 failure modes), sweep entries (`Reducer`,
`Remesher`), `RemesherNonFiniteInputTests`, `DefectInputTests`, corpus (12 clean of 64 each: the pipelines are known to
fail on many defect inputs, by design). No shard, no campaign.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| CsCheck | Properties on random clean closed meshes: valid mesh, Euler characteristic and components preserved (Reducer), no fold-over (`PreventFoldOver`), bounds within MaxDeviation, deterministic parallel vs serial, bowtie vertices kept. Shrinks to a few triangles. | Medium-high. S1 (fold-over chains), the bowtie fixes, "Stale state, reused instances" (5 fixes) and nondeterminism ties (R7) show what this finds; R13 / R14 still deferred. | C7: 5-6 A; CI +1-2 min | `Remesher` is upstream code with many absolute constants (R14); a campaign at 2^k scale will rediscover known deferred items: list them as known first. |
| SharpFuzz | Soup + parameter bytes: termination (slivers, needles: `RemesherIssueCasesTests` has the cases), non-finite spreading (R13 deferred). | Medium | shares F1 | Hangs are the target: needs the 5 s timeout and the sizes capped. |
| Stryker | Skip: float code, ties, and no exact claims. | Low | - | - |

### K. Spatial structures and queries (`DMeshAABBTree3`, `PointAABBTree3`, `WindingNumberOctree`, `MeshBatchQueries`, quadtrees, hash grids, `MeshQueries`, `RayIntersection`, `IntrRay3Triangle3`, point / ray / inside)

Exists: `AABBTreeDifferentialTests`, `BoxTree*Tests`, `SpatialQueryIssueCasesTests` (12 modes; tree equals brute force),
`RayQueryWatertightTests`, `ExactRayTriangleTests` (BigInteger), `MeshBatchQueriesTests`, `AdaptiveOctreeTests`,
`WindingNumberTests`, `HashGridRangeTests`, the quadtree tests (`Spatial/*`, NTS and STRtree differential in CI),
sweep entries (`DMeshAABBTree3`, `PointHashGrid3d`, `WindingNumberOctree`). Findings: R4 (hash grid hang at cell index >= 2^31),
Q1-Q3, #451 (top-down build: stack overflow / IndexOutOfRange when extreme centroids are adjacent doubles; every triangle
dropped above ~1e154 by an overflowing `LengthSquared`), and on the sweep list: `DMeshAABBTree3` ray and inside queries
fail on a sphere of radius 1e100.

| Tool | Would check | Expected finds | Cost | Constraints |
|---|---|---|---|---|
| SharpFuzz | **Best fit of all.** Byte-built triangles (incl. huge, subnormal, adjacent-double centroids, duplicates, NaN / Inf) into the tree build, then nearest point, ray, `IsInside`, batch queries, compared with a brute-force loop in the harness (the oracle already exists in `SpatialQueryIssueCasesTests`). Same for `PointAABBTree3`, `WindingNumberOctree`, hash grids and the quadtrees. | **High, evidenced:** #451 came out of the pilot in minutes, the 1e100 ray / inside failure is the same class (absolute tolerances); R4 and Q1-Q3 would likely have shown up. | F1 first target: 2 A on top of F1; R 1-2 h per structure | The structure's contract for non-finite input must be stated first (throw vs ignore); the harness encodes it. |
| CsCheck | Differential vs brute force on random meshes with ties and degenerate triangles, 2^k scale invariance of query results, tree built top-down vs bottom-up equal. | Medium (`AABBTreeDifferentialTests` exists; adds shrinking and range) | 3 A | - |
| Z3 | Termination / progress lemma of the median split on binary64 (midpoint of adjacent doubles equals an endpoint); would have named #451 directly. | Low-medium (explains and pins) | 2 A | A tiny, cheap lemma. |
| Stryker | `DMeshAABBTree3` build and ray code: one shard; tests are fast (differential, issue cases). | Medium | 2-3 A + CI 1-3 h (est.) | The file is large; split by offset with `suggest_split.py`. |
| Invariance | The Q-class results already run over `Similarity.All()`; extend with 2^-24 and 2^20 for ray and inside (the 1e100 failure says the extremes are untested). | Medium | 1-2 A | - |

### L. Other public operations that take untrusted geometry

See the gap list (section 4) for the operations and the cell each gets. The tools that fit are the same as in J / K:
the sweep first (cheapest), then the mesh-ops fuzz target, then CsCheck for the robustness-critical ones.

## 4. Gap list: public operations that take untrusted geometry and have no fuzz harness and no property campaign

Method: every class that takes a mesh, polygon, point set, field or grid from outside and is public API. "Sweep" means
`RobustnessSweepTests.Ops` (scale + non-finite); "tests" are the classes found by name in `tests/`. A class counts as a
gap when it has neither a fuzz harness (only the STL pilot exists) nor a property campaign (only `ExactMeshBoolean` has
one). Having no sweep entry is marked, because the sweep is the cheapest instrument. The count of test files per class
was taken by `grep` on the class name (a rough guide, not a coverage measure); classes with 0-1 matching files are marked **thin**.

Almost everything in the library is a gap in this strict sense: the fuzz pilot covers one reader and no mesh operation,
and the campaigns cover one operation. The useful list is therefore ordered by risk (untrusted input x complexity x no sweep entry):

| Priority | Operation | Why it is a risk | Sweep | Tests (rough) | Cell to fill |
|---|---|---|---|---|---|
| 1 | `ExactMeshBoolean` and its n-ary / `Resolve` forms (open inputs, non-finite, extremes) | The most complex public operation; takes arbitrary soups | none (own corpora) | 48 files | F1 target + the existing CsCheck extended to open inputs |
| 1 | `MeshRobustRepair`, `MeshAutoRepair`, `MeshRoundingRepair` | Takes any soup; #420 / #425 / #452 | `MeshAutoRepair` only | 49 / 22 | C1, F1 |
| 1 | `DMeshAABBTree3` build and queries, `PointAABBTree3`, `WindingNumberOctree`, `MeshBatchQueries` | Every operation sits on them; #451 | tree and octree entries | 1-5 files each (**thin** for `PointAABBTree3`, `MeshBatchQueries`) | F1 (first target), K |
| 1 | `MeshAlphaWrap` | Takes any soup and parameters | none | 16 | C3, F1 |
| 2 | `MeshMeshCut`, `MeshBoolean` (upstream; documented as not robust), `MeshInsertPolygon`, `MeshInsertProjectedPolygon`, `MeshTrimLoop`, `MeshStitchLoops`, `MeshExtrudeMesh` | Upstream editing operations on arbitrary meshes; 0-1 test files each (**thin**); the upstream boolean has R6, R19 | `MeshBoolean` only | 0-1 | Sweep entries; fuzz only the ones the CLI exposes |
| 2 | `QualityMesh2`, `ConstrainedDelaunayMesh2`, `SnapRounding2`, `Arrangement2d`, `.poly` import | Takes arbitrary point / segment lists | 2 `QualityMesh2` ops | 27 / 6 / 3 / 1 | C2, F-mesher target |
| 2 | `Clipper2Polygons.*` (Boolean, Offset, Simplify, Minkowski, ClipPolyLines) | Polygons from users; wraps Clipper2 | none | own project | C4 + F-clipper target |
| 2 | `MeshWindingNumberBoolean`, `MeshWindingNumberSolidify`, `VolumeSampleCells`, `MeshEnvelope`, `PolyhedralEnvelope` | Take any soup; envelope has a SAT filter with a hand bound | none | 10 / 12 / 2 / 2 / 3 | Sweep entries; Stryker exists for the envelope |
| 2 | `Reducer`, `Remesher`, `RegionOperator`, `RemoveOccludedTriangles` | Absolute constants (R13, R14 deferred); `RegionOperator` has 0 test files by name (**thin**) | Reducer, Remesher | 20 classes | C7 |
| 3 | `MarchingCubes*`, `DualContouring`, `VoxelSurfaceGenerator`, `MeshSignedDistanceGrid`, `MeshWindingNumberGrid`, `CachingMeshSDF` | Grids and fields from users; R3, R9, R10 | SDF + MC | 5-6 / **thin** for `VoxelSurfaceGenerator` | C5 |
| 3 | `Terrain*` (`TerrainGreedyTIN`, `TerrainDualContouring`, `TerrainSlope*`, `TerrainSafety`, `TerrainOffset`) | DEM grids, polygons | none | ~50 | C6 |
| 3 | `MeshGeodesicDistance`, `MeshGeodesicPath`, `VectorHeatMethod`, intrinsic triangulation, `MeshLocalParam`, `LSCM` / `ARAP` / `SLIM` / BFF / `UVAtlasGenerator` | Heavy numerics on any mesh; P1-P4, G1-G3 | BFF, geodesic distance | 3-8 | Sweep entries first; fuzz for termination |
| 3 | `MeshWallThickness`, `MeshDeviation`, `MeshHausdorffDistance`, `MeshFidelityMetrics`, `MeshCurvature`, `MeshTopologySummary`, `PrimitiveFit3` (analysis, `MeshICP`) | Read-only analysis of any mesh; wrong numbers instead of exceptions | ICP, Hausdorff | 1-15 | Sweep entries for the thin ones |
| 3 | `PlanarComplex`, `GeneralPolygon2d`, `Polygon2d` operations, `PolySimplification2`, `DGraph2`, `ConvexHull2`, `Delaunay3`, `SegmentQuadtree2d`, `QuadtreeExactQuery2d` | 2D curve / polygon code taking arbitrary points; 0-2 test files for several (**thin**: `ConvexHull2`, `QuadtreeExactQuery2d` by name) | none | 0-8 | Sweep entries; Delaunay3 / ConvexHull2 are fuzz-friendly |
| 4 | `MiniJson` (glTF), the MCP server's JSON-RPC input, `CommandArgumentSet`, `g3cli` option parsing | Untrusted text from the network (MCP) | n/a | `McpTests`, `CliTests` | Seeded test now; fuzz target in F2 |
| 4 | `SVGWriter`, `OBJWriter` etc. (writers) | Output only, but file-path and encoding handling | n/a | round-trip tests | Low priority |

What the rollout does with the list: the sweep extension (T1) covers everything marked "none" or "thin" for scale and
non-finite behaviour with about 30 new rows; F1 gives the first four priority-1 entries a coverage-guided harness; the
CsCheck campaigns cover the robustness-critical ones.

## 5. Rollout order by ROI

Order by (expected finds x evidence) / cost. Each task: one tool x one area, test-only, one PR, with its "done when".
Findings go to the owning area as bugfix PRs with procedural regression tests. **Marked done:** the Boolean CsCheck
campaigns (C x CsCheck), Stryker for the exact arithmetic and mesh shards (C, D x Stryker), seeded reader fuzzing
(A x ReaderFuzz), all six Z3 pilots, the FPTaylor pilot, the STL fuzz pilot.

| # | Task (tool x area) | Expected finds, with evidence | Cost (est.) | Done when |
|---|---|---|---|---|
| T1 | **Sweep table extension** (not a tool of the list; sweep x gap list): add the priority-1/2/3 operations with "none" or "thin" to `RobustnessSweepTests.Ops`: `MeshAlphaWrap`, `MeshRobustRepair`, `MeshRoundingRepair`, `MeshWindingNumberBoolean`, `MeshWindingNumberSolidify`, `MeshEnvelope`, `VolumeSampleCells`, `MeshInsertPolygon`, `MeshTrimLoop`, `MeshStitchLoops`, `MeshExtrudeMesh`, `Delaunay3`, `ConvexHull2`, `PointAABBTree3`, `MeshBatchQueries`, `MarchingCubesPro`, the analysis operations | **High.** The first sweep found R1-R23 (the largest yield per hour in the project's history): absolute thresholds (8 + 7 fix classes of QA.md). The new rows hit code that has had less scale testing. | 6-8 A for ~30 rows, CI +1-2 min | every row in the gap list's "none" / "thin" cells has an `Op` or an entry in `KnownFailures` with a finding id; `G3_SWEEP_REPORT` output attached to the PR |
| T2 | **F1: SharpFuzz mesh-ops harness x spatial structures (K), repair / rounding (B)**: land the Boolean session's pilot as `tools/MeshOpsFuzzPilot` (or merge its branch) with a target selector; first targets `DMeshAABBTree3` build + queries vs brute force, `MeshRoundingRepair`, `MeshRobustRepair`; a replay workflow as in `ReaderFuzzPilot`, no corpus committed | **High, evidenced:** #451 and #452 in 45 min with 3 workers; the sweep list (1e100 ray / inside, 4-11 s repair) is the next yield. | 4-6 A for landing and 3 targets, R 2-3 h per target | the harness is on master with a README and a replay test in CI (no campaign); each target ran >= 2 h on a runner with the result recorded (iterations, `ft`, findings); every crash has a fixed procedural test; the sweep list items are fixed or pinned |
| T3 | **CsCheck x repair / rounding (B)** (platform.md item 1) | **High:** would have found #420, #425; properties on `NotCleanReason`, MaxDrift, scale invariance. | 6-8 A; CI +1-2 min seeded; R 2-4 h | the campaign runs seeded in CI under 5 s per test, a 2 h runner campaign recorded; each failing seed is a fixed example test; `Apply() == true` implies `NotCleanReason == null` is a property |
| T4 | **SharpFuzz x all readers (A)** (F2): generalise the STL pilot to OBJ, OFF, PLY, 3MF, GLB, `.g3mesh`, `MiniJson`, `TrianglePolyFormat`, MCP request parsing; add the structural checks of `ReaderFuzzTests` to the pilot (finite when Ok, index ranges, budgets) | **Medium.** Blind fuzzing found reader defects in three PRs (#130, #131, #136; 4 fixes in QA.md's table); coverage guidance should reach nested structure (3MF XML / ZIP, GLB accessors). The STL run (1 000 iterations) found nothing and proves little. | 3-4 A; R 1 h per target, 8-10 targets | every reader and parser in the list has a harness and a replay test in CI and a recorded >= 1 h runner run; "a replay corpus" is generated by script (no mesh file committed) |
| T5 | **CsCheck x 2D mesher against the oracle (F)** (platform.md item 2) | **Medium-high:** the rule fixes of the last weeks (small-angle rule, conforming mode) and `StarPolygonFuzz` (211 of 3000 rejected at Q17) show the generator finds real gaps; richer inputs (holes, near-parallel, near-touching) should find more. | 6-8 A; CI +1-2 min; R 2-4 h | properties (segments are unions of output edges, angle bounds, conformity) over polygons with holes, seeded in CI, long run recorded; oracle cost per case < 1 s |
| T6 | **CsCheck x alpha wrap (E)** (item 3) | Medium: the area changed most recently; closed / manifold / encloses / band properties via `CheckWrap`. | 5-6 A; CI +1 min; R 2 h | as T3 |
| T7 | **CsCheck x remesher / reducer (J)** | Medium-high: S1, bowtie, reuse and determinism classes; Reducer `PreventFoldOver`, Euler characteristic. | 5-6 A | as T3; known deferred items (R13, R14) listed before the campaign starts |
| T8 | **Changed-line coverage gate (coverlet)**: finish #434 after the tier PR (#447) | No direct finds; stops untested new lines (the mutation runs find them hours later). | 3 A after the rework + CI +3-5 min as its own job over the fast tier (est.) | the gate fails a PR that adds an uncovered line in a mutated file; threshold and exemptions documented; runs on the fast tier |
| T9 | **Stryker x spatial structures** (one shard `DMeshAABBTree3`, K) and **Stryker x readers** (A) | Medium-low: untested guards (#131 class); cheap because the tests are fast. | 2-3 A each; CI 1-3 h per shard (est.) | shards with first thresholds seeded on master (workflow_dispatch) and the survivors triaged |
| T10 | **CsCheck x Clipper2 (G)** and **Z3 frame lemma** | Medium-low: frame and conversion are the risk (the review-findings tests) | 4-5 A + 2 A | properties against `Oracle.cs` on net10.0; the lemma runs in `Z3 pilot` |
| T11 | **CsCheck x MarchingCubes / terrain (H, I)** (item 4) | Medium: nondeterminism history and absolute thresholds (#124, R10); terrain scale class. | 4-6 A each | as T3 |
| T12 | **FPTaylor x hand-derived filters (D, C)**: `ExactPredicates` Orient/InCircle/InSphere filters, `ExactTriangleIntersection`, then the rest of `Orient3dLPI` | Low-medium: independent derivation; #137 shows hand bounds fail rarely. | 3-4 A for the first 4, then 1-2 A each | each filter with a bound has the FPTaylor comparison test and the drift check as in `compare_nx.py`; the "bounds review" checklist item cites it |
| T13 | **SharpFuzz mesh-ops targets, second wave**: boolean, alpha wrap, remesher / reducer, mesher, Clipper2 | Medium: depends on T2's yield | 2-3 A each | the "every public operation that takes untrusted geometry" clause of the done-when for the priority-1/2 list |
| T14 | **Z3 termination / progress lemmas** (`fix_degenerate`, median split, expansion sums) | Low-medium: explains and pins fuzz findings | 2-4 A each | each lemma checked in `Z3 pilot`; `AdaptivePredicates` expansion lemmas only if the owner wants them ("not worth it now" is a valid result) |
| T15 | **Stryker for the terrain configs, repair result checks, mesher** | Medium, but long and slow tests | after the backlog program | after the tier work: reduced filters, then one shard each |

T1-T5 add up to about 25-34 A and 10-25 R (est.); T1-T3 give most of the yield (the two classes the
project already found most bugs in: absolute thresholds / scale, and degenerate or extreme input).

Dependencies: T2 before T13; T8 after #447; T9, T15 after the tier work; T3, T5-T7 need the test category that the
Stryker filters exclude (a one-line convention, to be fixed in T3's PR).

## 6. Open points for the owner and the coordinator

1. The mesh-ops harness is not on master (task T2). If the Boolean session's branch is still open, T2 is "merge and
   extend"; if not, it is "rebuild from its README".
2. FPTaylor stays a local tool (OCaml / Num toolchain): it does not run in CI, so its comparison tests do not gate a
   change to a filter bound. The checklist can name it as an item of the bounds review instead.
3. Cost numbers are estimates; the first PR of each tool replaces them. The mutation cost figures (>= 29 runner-hours
   for a full run, rounding 145-294 min per part) are from platform.md.
4. The grid counts a Z3 or fuzz pilot as "pilot", not "done"; "done when" in platform.md needs a harness per reader and
   per untrusted-geometry operation, which this inventory sizes at about 15 harness targets after T2 and T4.
5. No claim in this document comes from a run: all "exists" statements are from file listings and headers on master
   `2c7ba9f`; "expected finds" follow the evidence named in the row.
