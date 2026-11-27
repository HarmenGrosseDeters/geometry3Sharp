# Can a pipeline run be certified against its inputs alone? Phase 1 (2026-11-25)

Research spike, owner approval 2026-11-25. **Draft, phase 1.** The question: can a run of `SolidBooleanPipeline` be certified
against its inputs alone, without a reference result? That is, can the pipeline say, with a checker that shares no code with the
kernel, "this output is the right point set up to the rounding bound, and it is clean", and say "I cannot certify this run" where
it cannot? Phase 1 touches only tests and benchmarks: **no library file was changed.** The hook in `mesh_ops/SolidBooleanPipeline.cs`
(phase 2) waits for #1488 (the pipeline's `Verification`), because several changes queue on that file.

It builds on [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md) (the checker of `ExactMeshBoolean`, its cost, the
rounding-dropped solids), [CERTIFYING_BOOLEANS_2026-11.md](CERTIFYING_BOOLEANS_2026-11.md), [HIGHER_MATH_STUDY_2026-11.md](HIGHER_MATH_STUDY_2026-11.md)
(the valuation certificate: at most a cheap pre-check, not used here) and [SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md)
(the procedural adversarial corpus, which is run here). The approach is that of McConnell, Mehlhorn, Näher and Schweitzer,
*Certifying algorithms*, Computer Science Review 5(2), 2011: an output plus a checkable witness, and a checker simpler than the algorithm.

## Result in short

| Question | Answer |
|---|---|
| What does a passing certificate prove? | For a direct run: the output is closed, oriented, manifold, free of self-intersection (contacts aside) and within the rounding bound of the exact result of the operation on the inputs as passed, in winding number semantics, with nothing invented and the volume within 1e-3 (section 1). Not the intent behind the inputs, not anything below the bound, not the steps before and after the exact stage. |
| Harness | `PipelineCertificate` (tests/Harness, used by tests and the benchmark only) with the verdicts `Certified`, `NotCertifiable(route)` and `Rejected(clause, numbers)`, written against `IExactStageRun` (the exact stage's `ExactMeshBoolean`, the route, the inputs as passed, the input displacement). `DirectRun` implements it for a plain run; the benchmark also captures the boolean of a real pipeline run through `ConfigureBoolean`, which is how phase 2's hook will hand it over. 10 tests, the slowest (the invariance test over `Similarity.All()`) about 3 s, the whole class about 10 s. |
| Seeded defects | 10 of 10 injected defects are rejected, each by the clause that should catch it (section 3); the negative control (a vertex moved by half the bound) is correctly not seen. Hand mutants of the harness: section 3. |
| Verdicts, procedural adversarial corpus (600 runs, direct) | certified 76 % (53 % plain, 16 % with an input winding number outside {0, 1}, 6 % with contacts, 2 % with parts the rounding removed), `NotCertifiable` 21 % (refused by the exact stage: open, orientation, rounding not clean; and 0.5 % the coverage test undecided), `Rejected` 3 % (all 19 are the rounding removing the whole result: real, section 4.1). |
| Verdicts, Thingi10K pairs (154 models of a seeded sample of 300, up to 5000 triangles each, 462 runs) | certified 89 % (with the deeper coverage retry), `NotCertifiable` 11 % (10 % refused: open or inconsistently oriented models, rounding not clean; 1.3 % coverage undecided), **`Rejected` 0**. |
| Cost, Thingi10K | the certificate costs a median 93 times the boolean (p90 200, max 1092) with BigInteger; with the filtered embedding check of `Certify.cs` in place of C2 a median 40 times (p90 80). C2 is 74 % of the certificate in the median; the other clauses stay BigInteger. |
| Does it find a wrong result that today's checks miss? | **No.** On the procedural corpus through the real pipeline, 28 runs are `Success` and wrong against the audit's oracle. 8 are results the rounding emptied: today's report already says `Clean = false`, and the certificate rejects them (`NotClean`, with the removed part listed). 20 are inputs given with the wrong orientation (a cavity wall outward): `Clean = true` today, and the exact-stage certificate holds for them (it is about the winding numbers of the inputs as given). On Thingi10K there is no reference, and no run is rejected. |
| What is new and cheap | A caveat from the certificate's own witness: an input mesh with a winding number outside {0, 1} (it overlaps itself or is inside out). It flags 20 of the 20 silent-wrong pipeline runs (and 31 correct runs: precision 39 %; direct runs: 60 of 60 wrong, and 34 correct), and costs nothing: the winding numbers are in the witness. It does not need the BigInteger checker. |
| Stop rule | **Applies; the spike stops after phase 1** (owner 2026-11-25) (section 6): no wrong result found that today's checks miss, and the cost is 40 to 100 times the boolean, with a filter on one clause only. On the routes that alter the inputs the rounding checks work as #1488 describes; for a direct run the certificate adds nothing that today's checks do not already say. |

## 1. The statement (the repair contract)

Notation. The pipeline's exact stage is one `ExactMeshBoolean` over the meshes it was *fed* (F_1 .. F_k, the inputs after any
repair, drop or snap of the route) with the operation's predicate P on the winding numbers. E is the exact result: the oriented
boundary of {x : P(w(x))}, the kept pieces of the arrangement. E_s is E with its vertices as exact points. R is the output after
the rounding repair (`ExactMeshBoolean.Result`). delta = `MeshRoundingRepair.MaxLength` of the run plus four ulps of the largest
coordinate (the checker derives it from the output and the settings, never from a file).

**What a passing certificate proves.** A run is `Certified` when all of the following passed:

| Check | Proves |
|---|---|
| InputsFed (identity of the meshes, then by coordinates) | on the direct route, the meshes the exact stage got are the inputs as passed, triangle for triangle, orientation included |
| C0 | the arrangement was built from exactly the fed meshes (reversed triangles only as many as `ReorientedTriangles`); no non-degenerate triangle is missing or doubled; every culled triangle meets nothing of the culling mesh and lies outside it; the rounding's bound is at most the documented one for the settings |
| C1 | every piece of the arrangement lies in its source triangle in the same plane and orientation, and the pieces of a source tile it exactly |
| C2 | no two pieces meet except in a corner or along an edge they share by exact point (the arrangement is embedded) |
| C3 and C4 | the winding numbers on both sides of every classified piece are right (locally consistent around every edge, anchored by one exact ray per connected set), so the kept pieces are exactly those where P differs between the two sides, oriented by the side that is inside |
| C5 | E_s is the kept pieces (as an oriented chain, by planes, and by area); R is closed and oriented (every directed edge once, with its reverse once); every corner of R lies within delta of E_s and every triangle of E_s is covered within delta by R |
| NotClean | R has no degenerate triangle, is manifold apart from contacts, and has no self-intersection (the library's exact search on the doubles of R); contacts alone (`OnlyContacts`, told by the pipeline's own `FinalCheck`) are a caveat of the verdict, not a failure |
| NoInventedSurface (#1488, provisional) | every sample of R is within 3 x (bound) of an input as passed: nothing was made up. On the direct route this follows from C0 to C5; the check matters on the routes that alter the inputs |
| RoundingVolume (#1488, provisional) | the rounding repair changed the volume by at most 1e-3 of the volume before, or by an immaterial amount against the smallest input volume (no allowance for thin parts here: stricter than #1488) |

Together, for a direct run: **R is a closed, oriented, manifold surface without self-intersection (contacts aside), lying within
delta of the boundary of the exact result of the operation on the inputs as passed (in both directions), made of nothing but
pieces of those inputs up to delta, with a volume within 1e-3 of the volume before the rounding.** The exact result is taken in the winding
number semantics of the inputs as passed (what `ExactMeshBoolean` documents), and the proof does not depend on the arrangement
code: C1 to C5 are re-derived from the inputs with `BigInteger` and the checker's own predicates.

**What it does not prove.**

1. **Intent.** The certificate is about the inputs as fed, in winding number semantics. An outward cavity wall (audit F2) or an inside-out
   input is certified as the solid its winding numbers define, which is not the solid the caller meant (measured below: 60 of 60 such direct runs are `Certified` while the audit's oracle, which knows the intent, says wrong). The verdict carries a caveat that names the input and the winding number.
2. **Anything below the rounding bound.** Closeness within delta in both directions is a statement about surfaces, not about solids: a part thinner than delta, a tunnel narrower than delta or a gap that closes can change topology or flip while R stays within delta of E_s. RoundingVolume sees such a change only if it moves the volume by more than 1e-3 (and more than the floor); the sign and the topology of a thin part are not checked, and the orientation of R is not compared with E_s's pointwise. The negative control of section 3 shows the boundary.
3. **Solids the rounding removed.** The checker exempts components of E_s that the rounding repair removed (tiny or thin ones) from the coverage and lists them (audit F1; `RemovedParts`). A run with such parts is the verdict kind `CertifiedWithRemovals` (the label `Certified, rounding removed parts` of the benchmark), not plain `Certified`; `BooleanCertificate.Check()` calls the same evidence "not clean". The certificate says the parts were removed and says nothing about whether they mattered. The exemption is wider than the removal (once any component was removed, `CheckResult` skips every thin or tiny component of the exact result), so the harness requires the skipped components to be no more than the removed ones, and the count of removals to match `Rounding.RemovedComponentInfos`; else the verdict is `NotCertifiable(removals-unmatched)`. If R is empty because of it, `NotClean` rejects.
4. **The steps between the caller and the exact stage.** On the direct route they are the identity (checked). Input repair (`MeshAutoRepair`, `MeshRobustRepair`), the grid snap, the drop of inside-out parts (#1555, issue #1567) and reorientation change the fed meshes: the certificate is then about what the exact stage got. NoInventedSurface sees surface that lies on no original input (a hole fill, a lid); **nothing sees surface that was lost** (a dropped plate, a removed component) except the InputsFed comparison. On the direct route any difference is `Rejected(InputsFed)`. On every other route, input triangles of the inputs as passed that the exact stage did not get, and that the route does not list as an explained drop (`PipelineEvidence.ExplainedDroppedTriangles`, none today), make the verdict `NotCertifiable(inputs-altered)`; added triangles stay a caveat, since NoInventedSurface covers them. For `GridRetry` a triangle matches within the input displacement. With the repair route's input repair this makes the two procedural cases that drop debris or a zero-area triangle `NotCertifiable(inputs-altered)` until phase 2 hands over the drops the repair made on purpose (section 4.3).
5. **The steps after the exact stage** (`MinThickness`, the reduction, `DropThinInsideOut` of results): not covered; the certificate is about `ExactMeshBoolean.Result`, so the hook must say whether `Pipeline.Result` is that mesh.
6. **Routes without an exact arrangement:** the wrap, and any route where the exact stage had open inputs (the sheet role: generalized winding numbers, `BooleanCertificate` is `n/a`), are `NotCertifiable(route)`. An Intersection with the early-out on is `NotCertifiable` too (the checker's class: run it with `CullOutsideA = false`).
7. **The trust base.** `ExactPoint3` and `BigInteger` arithmetic, the checker's ray and plane code, `MeshRobustRepair.NotCleanReason`, and (for NoInventedSurface) the library's closest-point search. The checker shares no arrangement code, but it is not formally verified.

## 2. The harness

`tests/geometry3Sharp.Tests/Harness/PipelineCertificate.cs` (test code; the benchmark links it, `benchmarks/BooleanComparison/PipelineCertBench.cs`).

- `IExactStageRun`: `Route` (Direct, GridRetry, SheetRole, InputRepair, Fallback, Wrap), `Boolean` (the `ExactMeshBoolean` after `Compute`, with `RecordCertificate` and `ComputeExactVertices` on), `OriginalInputs` (as passed to the pipeline), `InputDisplacement` (the largest distance the route moved an input point: the grid cell times the square root of 3 for the Grid step, else 0).
- `DirectRun.Compute(inputs, op)`: the pipeline's direct route without input repair, with the pipeline's flags (`StrictRounding`, no open inputs) and the recording. For an Intersection it turns the early-out off (`CullOutsideA = false`), as `BooleanCertificate.CheckBoolean` does; with `certifiableIntersection: false` the verdict is `NotCertifiable(n/a)`, because the checker does not apply to culling against several meshes.
- `PipelineEvidence.From(run)` collects what the verdict needs: the `BooleanCertificate` of the instance, the inputs as fed and as passed, the rounding repair's bound and its volumes before and after. `PipelineCertificate.Judge(evidence)` gives the verdict, so that a test can change the evidence first (the seeded defects); `Certify(run)` does both.
- The order of the checks: route; InputsFed; C0, C1, C2, C3/C4, C5 of `BooleanCertificate` (called one by one in the order of `Check()` and timed; a test asserts that the chain agrees with `Check()`); the deeper coverage retry (split depth 6, 9, 12) if C5 leaves triangles undecided; `NotCleanReason` of the output; the rounding checks of #1488; the caveats (winding outside {0, 1}, contacts, parts the rounding removed, inputs altered by the route).
- **Provisional until #1488 is on master:** `NoInventedSurface` and `RoundingVolume` are reimplemented here from the PR text (samples at 3 vertices, centroid and 3 midpoints; tolerance 3 times the larger of the rounding bound and 256 ulp, plus the input displacement; the closest-point search for every sample the tree does not decide; volume change limit 1e-3 and the floor of 1e-6 of the smallest input volume). The thin-part allowance of #1488 is not reimplemented, so the harness refuses at least what #1488 refuses. When #1488 merges, the harness should call its code.
- Verdicts: `Certified` (with the caveats listed), `CertifiedWithRemovals` (as `Certified`, but the rounding removed parts), `NotCertifiable(key)` with the key `route` (a route without an arrangement), `n/a` (open inputs, Intersection with the early-out), `no-result` (the exact stage failed or ran without the recording), `inputs-altered` (a route other than Direct lost input surface that no explained drop covers), `removals-unmatched` (the checker's exemption for thin parts is wider than the rounding's removals), `coverage-undecided` (C5's coverage test did not decide after the retries: the checker's limit, section 4.7); `Rejected(clause)` with the clause C0, C1, C2, C3/C4, C5, NotClean, NoInventedSurface, RoundingVolume or InputsFed and the numbers.

## 3. Seeded defects (also the mutation check of the checker)

`PipelineCertificateTests.SeededDefects_AreRejected_ByTheRightClause` and `SeededRoundingDefects_AreRejected_ByTheRoundingChecks`: two overlapping spheres, each defect injected into the evidence of a certified run (the unmutated run is `Certified`).

| Seeded defect | Verdict | The clause's message |
|---|---|---|
| a classified piece's keep label flipped | `Rejected(C3/C4)` | keep False but the predicate is False in front and True behind |
| the winding number in front of a piece changed by 1 | `Rejected(C3/C4)` | keep True but the predicate is True in front and True behind |
| a piece dropped (of a split source triangle) | `Rejected(C1)` | the pieces' areas do not add up to the source's |
| a piece duplicated | `Rejected(C1)` | directed edge used twice |
| an output vertex moved by 10 times the rounding bound | `Rejected(C5)` | a corner of the result is farther than delta from the result before rounding |
| the orientation of one output triangle swapped | `Rejected(C5)` | edge used 2 times the same way |
| a closed patch of invented surface (a tetrahedron) next to the output | `Rejected(C5)`; `NoInventedSurface` alone also sees it | 4 triangles, share 1.2e-5, farthest sample 0.106 against the tolerance 1.3e-4 |
| a part of input B (a plate) dropped before the exact stage, original inputs unchanged | `Rejected(InputsFed)` on the direct route | 12 triangles of the inputs as passed are not fed |
| the rounding repair's volume changed by 1 % | `Rejected(RoundingVolume)` | change 0.01 > 0.001 and above the floor |
| a hole fill of an open input (the lid): `NoInventedSurface` of the closed box against the open input | 1 invented triangle | unit test; the benchmark finds the same on the InputRepair route, section 4.3 |
| negative control: an output vertex moved by half the bound | `Certified` | by design: the certificate is about the point set up to the bound (section 1, item 2) |

C2, the embedding, is not in this table because the evidence does not reach it: its seeded defects (a missed intersection: 378 of 378 caught in `certify selftest`, the crossing sheet, the folded copy) are in `BooleanCertificateTests` and [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md). The order of the clauses decides which one reports first when several fail; a swapped output triangle is invisible to C1 to C4, which are about the pieces.

**Hand mutants of the harness** (`PipelineCertificate.Judge`, one edit each, the tests of `PipelineCertificateTests` run): 16 of 16 killed: InputsFed off, C0 off (seeded through `Judge` on the repair route, where `Certificate.Inputs` and the passed inputs are reversed together; on Direct the InputsFed clause would see a change of the list first), C2 off (a split source left unsplit, as in `BooleanCertificateTests`), the `inputs-altered` rule off, the removals match off, C1 off, C3/C4 off, C5 off, NotClean off, the contact acceptance off, the winding caveat off, the rule "an empty output is clean where the exact result is empty" off, the coverage retry's `NotCertifiable` off, `NoInventedSurface` never refusing, its tolerance 1e6 times wider, `RoundingVolume` never refusing. A review of the first version found that C0 and C2 survived (no seeded defect reached them through `Judge`); the seeds above kill them. The first run of this check let three survive (`NoInventedSurface` and `NotClean` as reached through `Judge`, and the undecided coverage): the tests `TheOtherClauses_ReachTheVerdict_ThroughJudge` and `CoverageUndecided_IsNotCertifiable_NotRejected_AndNotCertified` were added for them. The checker's own clauses (C0 to C5) are mutation-checked in `BooleanCertificateTests`.

## 4. Measurements

Machine: Windows, 12 logical cores, .NET 10.0, Release, other sessions running; the boolean (the parallel `ExactMeshBoolean` with the pipeline's flags, one run) and the certificate (serial) were timed in one process, so ratios vary by some tens of percent between runs. The raw output is in `benchmarks/BooleanComparison/perf/pipeline_cert_*.txt`; the modes are `pipeline-cert procedural | extras | pipeline | pipeline-repair | thingi | diag` of `benchmarks/BooleanComparison`. The Thingi10K models are read from a local copy of the dataset and referred to as `<dataset>/raw_meshes/<id>.stl`; no mesh file is in the repository.

### 4.1 The procedural adversarial corpus, direct (`procedural`, `pipeline_cert_procedural_direct.txt`)

The 60 cases of the pipeline audit ([SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md](SOLID_BOOLEAN_PIPELINE_AUDIT_2026-11.md), `benchmarks/SolidBooleanAudit/Corpus.cs`) in its 10 frames (identity, soup, 2^40, 2^-40, UTM offset, 1e12 offset, rotation, rotation with offset, two float32 frames): 600 runs of one `ExactMeshBoolean` on the inputs as given.

| Verdict | Runs | Share |
|---|---|---|
| Certified | 318 | 53.0 % |
| Certified, an input has a winding number outside {0, 1} | 94 | 15.7 % |
| Certified, with contacts | 34 | 5.7 % |
| Certified, the rounding removed parts | 10 | 1.7 % |
| NotCertifiable(refused): the exact stage refused (open input 90, inconsistent orientation 20, rounding not clean 12) | 122 | 20.3 % |
| NotCertifiable(coverage-undecided) | 3 | 0.5 % |
| Rejected(NotClean): the output is empty because the rounding removed the exact result | 19 | 3.2 % |

Against the audit's oracle (ground truth from the intent operands; it samples 3000 points and cannot see solids thinner than about 1e-4 of the diagonal): of the 456 certified runs, 396 are right and 60 are wrong. The 60 are the cavity wall given outward (10) and the orientation class (inside-out box, inside-out hollow, inside-out tool, self-intersecting operand with a cavity given outward, two components with one inside-out: 10 each, all frames): the caller's intent differs from the winding numbers of the given inputs, so the certificate, which is about the latter, cannot see it. **All 60 carry the winding caveat**, as do 34 correct runs (inputs that overlap by design: the same box twice, two overlapping boxes). The 19 rejections: 8 are wrong against the oracle (a thin slab or wall of 2^-45 in four exact frames), 11 are right against it but the oracle's sampling cannot see the removed solid (a one-ulp slab, 2^-30 and 2^-45 in the rotated frames). **Diagnosis: all 19 are real:** the exact result is not empty, the rounding repair removed it (audit F1), and the output is empty. They are no checker limitation, and today's checks do not miss them (`Clean = false`, section 4.2). The `NotClean` rejection of an empty output depends on the exact result being non-empty: the intersection of disjoint solids is clean.

### 4.2 The same corpus through `SolidBooleanPipeline`, the boolean captured (`pipeline`)

`ConfigureBoolean` sets the recording on and keeps the last boolean the pipeline creates (the run that is the result); the route is read from `Report.Route`; the audit's oracle judges `pipeline.Result`. This is a prototype of phase 2's hook from the outside.

| Verdict | Runs | Share |
|---|---|---|
| Certified | 322 | 53.7 % |
| Certified, an input has a winding number outside {0, 1} | 51 | 8.5 % |
| Certified, with contacts | 34 | 5.7 % |
| Certified, the rounding removed parts | 10 | 1.7 % |
| NotCertifiable(refused): the pipeline refused (open, orientation) | 151 | 25.2 % |
| NotCertifiable(n/a): the sheet role (open tool, generalized winding numbers) | 10 | 1.7 % |
| NotCertifiable(coverage-undecided) | 3 | 0.5 % |
| Rejected(NotClean) | 19 | 3.2 % |

The pipeline's 28 `Success` runs that are wrong against the oracle (the audit's 28): **8 rejected** (the emptied results; today's report says `Clean = false`, `RoundingRemoved = 1`), **20 certified with the winding caveat** (`Clean = true`, nothing removed, nothing dropped: today's report gives no signal; 10 are the nested class's cavity wall given outward, 10 the self-intersecting operand with a cavity given outward). The caveat flags 51 runs of the 600, 20 of them wrong. Routes: 586 direct, 4 grid retry (the subnormal coordinate: certified), 10 sheet role.

### 4.3 The InputRepair route (`pipeline-repair`: the open cases with `InputRepair = AutoRepair`, identity frame)

9 runs: 3 `Rejected(NoInventedSurface)`: a missing triangle or face filled by the repair (farthest sample 0.03 to 0.5 against a tolerance of 1e-4): the repair invented surface, which is #1488's case, and refused by its default; 3 `Certified` (the fill lies inside the other operand, or the open input has nothing to fill: a duplicated face, a T-junction cube), with the caveat that the fed inputs differ from the inputs passed; 2 `NotCertifiable(inputs-altered)` (the repair dropped the debris triangle and the zero-area triangle: lost input surface that no explained drop covers; the first version of the harness certified them, a review found that a lost input part is then invisible; phase 2's hook has to hand over the drops the repair made on purpose); 1 `NotCertifiable(n/a)` (the sheet role). So on the routes that alter the inputs the rounding checks do what #1488 describes, and the exact-stage certificate holds for the repaired copy only.

### 4.4 The silent-success repros and the F1 cases (`extras`)

| Case | Verdict |
|---|---|
| issue #1566 (a nested outward shell becomes a cavity in `MeshRobustRepair`) | not applicable: it is in `MeshRobustRepair`, not in the exact stage; `ExactMeshBoolean.Resolve` and the pipeline keep volume 8 per the issue |
| issue #1567 (a 10 x 10 x 0.01 inside-out plate in B dropped next to a large A): the exact stage fed the plate whole | `Certified`, caveat winding outside {0, 1} (the plate's winding is -1) |
| the same, the plate dropped by the caller | `Certified` when the plate is not among the inputs as passed (row 2); where it is (the run labelled as the pipeline's drop): `NotCertifiable(inputs-altered)` on a route other than Direct, and `Rejected(InputsFed)` on the direct route (seeded defect); the hook must pass what the route dropped (section 5) |
| issue #1569 (TpmsLattice, world transform after the check) | not applicable: no exact stage |
| F1 tiny cube 2^-9 and 2^-12 beside a 1024 cube, union (identity, rotated, rotated with offset) | `NotCertifiable(refused)`: with `StrictRounding` the exact stage refuses (RoundingNotClean); the silent case of the audit does not happen on the direct route |
| F1 thin slab, wall and plate, 2^-12 to 2^-35, three frames | certified, except the 2^-35 slab and wall at rotation with offset: `Rejected(NotClean)`, empty (removed); the 2^-35 plate there is `Certified, rounding removed parts` (12 triangles, area 8, listed) |

### 4.5 Thingi10K pairs (`thingi`, `pipeline_cert_thingi.txt`)

A seeded sample (seed 20261125) of 300 models of the local copy; 146 have more than 5000 triangles and are not run (the BigInteger checker would need minutes each); 154 run, 3 operations each, B = A rotated by 20 degrees about (1, 1, 0) and shifted by a quarter of the box (the pairing of `BooleanComparison export`, #385). The pairs have 88 to 9000 triangles.

| Verdict | Runs (462) | Share |
|---|---|---|
| Certified | 320 | 69.3 % |
| Certified, an input has a winding number outside {0, 1} | 50 | 10.8 % |
| Certified, with contacts | 15 | 3.2 % |
| Certified, the rounding removed parts (before the rerun below: 9 after it) | 7 | 1.5 % |
| NotCertifiable(refused): open input 27, inconsistent orientation 12, rounding not clean 6 | 45 | 9.7 % |
| NotCertifiable(coverage-undecided) at split depth 6 | 25 | 5.4 % |
| Rejected | 0 | 0 % |

The 25 undecided runs were run again with the retry to split depth 12 (`pipeline_cert_thingi_undecided_rerun.txt`): 19 are decided (certified), 6 stay undecided (1.3 % of all runs). The final shares are certified 89.0 % (411), `NotCertifiable` 10.9 %, `Rejected` 0. The 9 runs with removed parts (7 before the rerun, 2 of the 19 newly decided: 56795 Union and 45619 Difference) are all on models whose input has winding numbers outside {0, 1} (an overlapping or inside-out input): the removed parts are components of the exact result that the rounding took as thin or tiny, 4 to 62 triangles, area up to 69 (their thickness and volume were not examined: a zero-volume residue of the overlap is the likely reading, not a diagnosis).

### 4.6 Cost

The certificate (all clauses, serial, BigInteger as built) over the plain boolean (parallel), medians over the certified runs:

| Corpus | Runs | median | p90 | max | with the filtered embedding check |
|---|---|---|---|---|---|
| Thingi10K pairs, 88 to 9000 triangles (the 392 decided runs before the rerun) | 392 | 93 | 200 | 1092 | median 40, p90 80, max 921 |
| procedural, direct | 456 | 5.6 | 34 | 5829 (the 1e12 frame) | median 3.8, p90 21 |
| procedural, through the pipeline | 417 | 4.6 | 21 | 8820 (the 1e12 frame) | median 3.1, p90 14 |

Per clause, summed over the Thingi10K runs and divided by the summed boolean time: C2 54, C5 16, C1 4.5, C3 1.9, C0 1.8, NotClean and the rounding checks 0.6; the filtered C2a alone is 10. So the filter of `Certify.cs` (which exists for C2 only) takes the certificate from 79 to 35 times the boolean on this corpus, and C5 (the rational coverage test) and C1 are the next costs. In the 1e12 frame C5 costs 17 to 220 s on 860 to 2900 triangles (800 to 5800 times the boolean; one run of the same case took 117 s): the BigInteger rationals of the coverage test at a coordinate range of 1e12 with a bound near 1e-7; a checker limit, not a library defect, and a reason why a hook needs a time budget (`NotCertifiable(budget)`) and the pipeline's `Cancellation`. The small procedural cases have booleans of 1 to 20 ms, so their ratios are dominated by fixed costs. The estimate of [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md) for a fully filtered checker (3 to 16 times) was not tested here.

### 4.7 Every `Rejected` and every undecided run, with a diagnosis

- **19 x `Rejected(NotClean)`, "empty; the rounding removed 1 part"** (procedural, direct and through the pipeline): the one-ulp slab of "one ulp apart D" (5 frames), the overlap slab and the wall at 2^-30 (rotated with offset) and 2^-45 (6 frames each). The exact result is a thin solid, the rounding repair removes it, the output is empty. **A real wrong result** (audit F1, the documented removal rule of SafeRounding), also visible today (`Clean = false`, `RoundingRemoved = 1`). The verdict names the removed part (box, triangles, area); a non-empty output with a removed thin part is `Certified, rounding removed parts`.
- **2 x the same in `extras`** (slab and wall 2^-35 at rotation with offset): same diagnosis.
- **No other `Rejected`** in 1062 runs of the procedural and Thingi10K corpora; the three rejections of section 4.3 are the intended invented-surface cases (hole fills).
- **`NotCertifiable(coverage-undecided)`, 3 procedural runs and 6 Thingi10K runs after the retry:** a triangle of the output that no single triangle of the exact result covers within the bound, after splits to depth 12. `diag` on 3 of the Thingi10K cases: the sampled distance from the output to the result before rounding is 1.2e-5, 1.9e-10 and 3.4e-6 against bounds of 5.5e-4, 1.9e-4 and 1.2e-3. **A checker limitation:** the coverage test covers a triangle by one triangle with splits, not by a set of triangles, so where the rounding merged two coplanar triangles of the exact result into one output triangle (or the triangulation of a plane differs) it needs deeper splits than any fixed depth. The fix is a coverage test against the set of triangles (a nearest search per sample point, as `NoInventedSurface` does), not a deeper split.
- **`Certified, rounding removed parts`** (10 + 10 + 9 + 1 runs): not rejections; each lists the removed parts. Whether such a part is a lost solid (F1) or a zero-volume residue is not decided by the certificate.

## 5. What phase 2's hook must expose

The hook in `SolidBooleanPipeline` does not have to contain the checker. The least that makes the certificate possible, and keeps the library unchanged in behaviour (an internal observer in the style of `RepairCheckObserverForTests`, called with one object per run that is the result), is:

1. **The `ExactMeshBoolean` instance of the run that is the result** (after a grid retry or the fallback: the last `runBoolean`), with the recording on: `RecordCertificate` and `ComputeExactVertices` set inside `runBoolean` (they are internal; `ConfigureBoolean` can set them from the outside, as the benchmark does, but the pipeline must not let a caller's `ConfigureBoolean` switch them off), and `CullOutsideA = false` for an Intersection (otherwise `NotCertifiable(n/a)`; the cost is the early-out). The instance carries the arrangement, the pieces, the witness (`CertificateFront`, `CertificateKeep`, `CertificateFlip`), `CertificateAssembled`, `ResultExactVertices`, `Result`, `Rounding` (`MaxLength`, `VolumeBefore`, `VolumeAfter`, `RemovedComponentInfos`), the settings and `Meshes` (the fed inputs). It must be kept alive until the check, which costs memory.
2. **The route**, as an enum or flags, not as text: Direct, GridRetry (with the cell), SheetRole (open inputs: `n/a`), InputRepair (which inputs, which method), Fallback, Wrap. Today it is derived from the step names of `Report.Route` and `Report.DroppedInsideOutComponents`.
3. **The inputs as passed and the relation to the inputs as fed**: the originals (before repair, snap and reorientation), and what the route changed, in a form a checker can compare: the triangles of each input that were dropped (the drop of inside-out components, #1555), the repaired copies, the snap displacement bound (the cell times the square root of 3), the triangles the reorientation reversed. Without it a dropped part is invisible (section 4.4); with it the `InputsFed` clause can say "the fed input is the passed input minus these listed triangles".
4. **The rounding bound and the rounding volumes**, from the instance (`Rounding.MaxLength`, `VolumeBefore`, `VolumeAfter`; the pipeline's `Report.RoundingVolumeBefore` and `RoundingVolumeAfter` are the same numbers).
5. **Whether `Pipeline.Result` is `ExactMeshBoolean.Result`**: `MinThickness`, the reduction (`MaxDeviation`), `DropThinInsideOut` and any other step after the exact stage change the mesh; the certificate is about the instance's `Result`. The hook must say which steps ran, or the verdict must say "of the exact stage's output".
6. **A time budget and the `Cancellation` token:** the checker is serial and BigInteger; at the coordinate range of the 1e12 frame one clause takes minutes.
7. **Where the checker lives.** `BooleanCertificate` is test code. A library mode would have to move it into exact code (the `math/Exact*` rules: the analyzers G3E0001 to 3, an entry in `core/ExactCodeAllowList.cs` for any parallel loop, a mutation shard, reviewed error bounds for the filters), which is the work estimated in [BOOLEAN_CERTIFIED_MODE_2026-11.md](BOOLEAN_CERTIFIED_MODE_2026-11.md). Given section 6, the observer for the test and benchmark harness is the proportionate phase 2; the library mode is not.

## 6. The stop rule

The stop rule of the plan: if the measurement finds no wrong result that today's checks miss, and the cost is above the use, say so plainly.

**It applies.** The spike stops after phase 1 (owner decision 2026-11-25): the certificate does not become a library feature, phase 2 (the hook in `SolidBooleanPipeline` and the run on the owner's inputs) is not done, and the harness stays in the tests as a QA tool for corpora without a reference. The winding-number caveat goes on as a warning field of the pipeline's report, together with the result kind of #1576 (a separate PR).

- *No wrong result found that today's checks miss and that the certificate catches.* On the procedural corpus and Thingi10K (1062 runs), plus 57 extras and 9 repair runs, the certificate rejects 21 results the rounding emptied, which the pipeline's report already marks (`Clean = false`, `RoundingRemoved`), and 3 results with invented surface, which #1488 already refuses. The 20 runs where the pipeline succeeds with a wrong result and a clean report are all inputs whose intent differs from their winding numbers: the exact-stage certificate holds for them by construction and cannot see them. On Thingi10K there are 0 rejections in 462 runs; there is no reference there, so this says only that no clause fired.
- *The cost is above the use.* 93 times the boolean in the median on the Thingi10K pairs, 40 with the one filter that exists, for a verdict that on these corpora was `Certified` for 89 % of the runs and never `Rejected`. A fully filtered checker is estimated at 3 to 16 times; that is not measured and not built.
- *What survives:* (a) the winding-number caveat, free from the witness (it flags all 20 silent-wrong runs of the corpus at 39 % precision; `Report.InsideOutCells` already covers the negative side); (b) `InputsFed` for the direct route, if the hook hands over what the route dropped (the only check that sees a dropped plate); (c) the seeded-defect table as the checker's own mutation check; (d) `NoInventedSurface` and `RoundingVolume` of #1488, which this harness reproduces on the routes that alter the inputs.

What would change the verdict: a wrong result on the owner's private inputs that the exact-stage certificate rejects and #1488's checks do not (section 7), or a filtered C1, C3 and C5 that brings the cost to the single digits.

## 7. Owner's inputs

Not run: phase 2 is not done (section 6). The owner's private inputs went through the pipeline's verification (#1488) and a second Boolean implementation before this spike; on every accepted result that differed from the owner's reference the second implementation agreed with the pipeline (observed by the owner on private inputs). A wrong result that the certificate rejects and the verification lets through is therefore not expected there, which was the only way the verdict could still change.

**Why the certificate is blind to intent errors.** It proves that the output matches the exact result of the operation on the inputs as passed, in winding-number semantics. When the inputs describe a different solid than the caller meant (a cavity wall facing outward, an inside-out part, a shell nested where a cavity was meant), the exact result is that other solid, and certifying it is correct by the certificate's definition. Only a signal about the inputs, such as the winding caveat, can point at such a case; it cannot decide it.

## 8. Reproduce

```
dotnet build benchmarks/BooleanComparison -c Release
BC="dotnet benchmarks/BooleanComparison/bin/Release/net10.0/BooleanComparison.dll pipeline-cert"
$BC procedural                  # 600 direct runs of the audit corpus
$BC pipeline                    # the same through SolidBooleanPipeline, the boolean captured
$BC pipeline-repair id          # the open cases with InputRepair = AutoRepair
$BC extras                      # F1 cases and the plate of #1567
$BC thingi <dataset>/raw_meshes 300 5000        # the seeded sample, about 55 minutes
$BC diag <dataset>/raw_meshes/<id>.stl Union    # coverage at growing split depths
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter "FullyQualifiedName~PipelineCertificateTests"
```

`BOOLCMP_ONLY_IDS=a,b` restricts the Thingi10K run to some models of the sample.

## 9. Sources

R. M. McConnell, K. Mehlhorn, S. Näher, P. Schweitzer, *Certifying algorithms*, Computer Science Review 5(2), 2011 (listed in [../sources/exact-arithmetic-and-booleans.md](../sources/exact-arithmetic-and-booleans.md)). The checks are own work; no third-party code. The rounding checks follow the text of #1488.
