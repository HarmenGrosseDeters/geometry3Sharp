# Tests above 20 s in full runs: diagnosis (2026-11-24, #698)

Back to [research notes](README.md). Rule: CLAUDE.md "Test time" (a test above 20 s in any run is a potential defect and gets a
diagnosis). Earlier rounds on the same list: the comments of #698 (#1026, #1145; the Reducer loop experiments).

## Data

- **CI:** the "Potential defects" section of the `time-rule` job logs of 19 full runs (master and PRs, 2026-11-19 to 11-24, linux net10.0;
  the job logs are readable, the trx artifacts are not from here). 11 of the 19 runs had a test above 20 s. The section prints the
  full-run time and the time alone (job `isolated`).
- **Local, 4 cores, net10.0:** `dotnet test --filter "Category!=Nightly"` (19 236 tests, 21 min; summed test time 4 862 s, 3.9 x wall: xunit keeps
  four collections running), then the 260 tests of 9 s or more again alone (`xUnit.ParallelizeTestCollections=false`). Three tests were
  above 20 s locally (27.4, 20.0 and 18.8 s for the tests below).
- **Probes** (temporary tests, not committed): one campaign or test body on a dedicated thread, parallel (library default) against
  `gParallel.SerialOnThisThread = true`, alone and with four busy pool items (`Parallel.For` of 64 items of about 10 ms, in a loop).

## Which tests, how often

Above 20 s in the CI runs (count of the 19 runs; full run = worst; alone = range in the runs):

| test | runs | full (s) | alone (s) | cause |
|---|---|---|---|---|
| `ExactBooleanPropertyTests` Boxes (4 campaigns), `Tetrahedra`, `Boxes_RepresentationInvariance` | 7 | 20.3-52.0 (local 27.4 / 20.0 / 18.8) | 0.8-4.0 (local 3.5-5.6) | **thread-pool starvation** of the library's fine-grained loops (probe below) |
| `RemesherQualityTests.Primitives_NoFlatTriangles_FewerSelfIntersections` | 2 | 27.2-35.3 | 1.9-2.9 (local 3.6) | **starvation**, same mechanism (probe below) |
| `MeshRobustRepairTests.SolidifyNotClean_AlphaWrapped` | 2 | 21.9-29.0 | 3.0-4.7 (local 4.6) | starvation of a path that `SerialOnThisThread` does not cover (probe below); not fixable in the test |
| `RemeshReduceCampaignTests.Reducer`, `ReducerSharedDeviationQueriesTests.Reduction_SameWithAndWithoutSharing` | 1 (and 17.5-24 in 3 more) | 14.8-24.0 | 2.2-5.6 | starvation of `Reducer.collapse_within_deviation`'s loop over about 10 triangles (measured in #698 on 2026-11-17: serial and caller-first variants missed the bar; owner decision stands) |
| `Meshing2OracleFastPathCheckTests.CatalogueRow` F-41/a, F-38/d; `Meshing2OracleModesTests` CatalogueRowsSlow3 / CorruptedRowsSlow3 F-5 | 5 | 20.2-35.1 (local 17.4 / 15.1 / 10.8) | 4.0-8.4 | **own cost** (one oracle row, serial; 6-8 s alone) times 2-4 from the other collections; one row cannot be split |
| `TerrainSlopeBandRepairTests.WavyToeOnHighWaves_Similarities` (rotated, scale 1e-3, utm) | 1 | 25.0 (local 13.8) | 5.7-9.3 | own cost (three builds by design, #696) |
| `TetMeshLocalAlphaTests.MissedDihedralTarget_RunsOnceMoreWithASmootherField_AndKeepsTheBetterResult` | 1 | 20.5 (local 12.7) | 4.9-8.3 | own cost (two full retry runs), times 1.5-4 |

Not above 20 s but 3x or more their time alone in the local full run (same family, not probed): `SafeRoundingDegenerateInputTests`
`Family_RepairedCleanWithinTheBound` Sliver 10.7 / 2.2 s, Collinear 10.3 / 2.6, `DenseClusters` FoldedCluster 9.0 / 2.7 (ExactMeshBoolean
loops, as the Boxes); `RemesherSelfIntersectionTests.Primitives_NoSelfIntersections` 10.2 / 4.2 (Remesher loops).

No test is slow because of a shared fixture or a blocking wait of the test itself: no `Task.Wait`, `.Result` on tasks or
sync-over-async in the test project; the `Task.Run` / thread users (`Timed`, `WorkerTeamTests`, `ArrangementMemoryTests`,
`AlphaWrapRoundsTests`) are none of the tests above.

## Mechanism

`gParallel.ForRange` is `Parallel.For` over the items of one call. The library calls it for loops of a few dozen items (the 12-triangle
boxes of the property tests: some dozens per Boolean stage; the Remesher: per pass; the Reducer: per collapse). Alone this costs
nothing. In a full run the pool is busy with the other collections' loops, and the tiny loops wait for workers:

| probe (4 cores) | alone, parallel | alone, serial | pool busy, parallel | pool busy, serial |
|---|---|---|---|---|
| `Boxes(nested)` campaign, 300 cases | 5.1 s | 4.4 s | **944 s** | 14.2 s |
| `Primitives_NoFlatTriangles` | 3.6 s | 4.8 s | **> 240 s** (cap) | 6.2 s |
| `SolidifyNotClean_AlphaWrapped` | 4.6 s | 7.8 s | > 240 s (cap) | **> 600 s** (cap) |

The load of the probe (four loops that keep every pool thread busy) is harsher than a CI full run (the same tests take 3-5 times their
time alone there, not 100), but it shows the scaling: the cost of a call is the wait for a worker, not its work, and a test with
thousands of calls pays it thousands of times. Serial is within 1.0-1.3 x of parallel alone for these inputs (the loops are tiny), so
the test loses nothing alone. Server GC was measured earlier (#698) and is not the cause.

`SolidifyNotClean_AlphaWrapped` is different: it stays above 600 s with `SerialOnThisThread`, so some of its work starts parallel work that
does not go through `gParallel` (the library has about 30 files with a direct `Parallel.For`, `Task` or thread use, among them
`MeshRobustRepair`'s callees). That is not diagnosed; it needs a profile or a stack sample of a busy-pool run (library side).

## Fixes in this PR (tests only)

Both tests run on the serial path of `gParallel` (`SerialOnThisThread`, as 20 other test files already do). Their parallel path is
checked against the serial one elsewhere (`ExactBooleanDeterminismTests.SerialEqualsParallel_BitIdentical` and the salted shard
runs; `RemesherDeterminismTests`), so the power of the tests stays: the same inputs, the same checks, the same library code per item.

- `ExactBooleanPropertyTests.Campaign` (all campaigns of the class: Boxes, DirectedBoxes, Tetrahedra, `Boxes_RepresentationInvariance`,
  the shrink and replay tests): 5.1 s alone parallel -> 4.4 s serial; full class 24 s.
- `RemesherQualityTests.Primitives_NoFlatTriangles_FewerSelfIntersections`: 3.6 -> 4.8 s alone (still below 5 s), no longer exposed.
- Mutation checks (the mutant must fail the serial test as it failed the parallel one): `ExactMeshArrangement` vertex array wrong for every
  third point: 6 of 6 `Boxes`/`Tetrahedra` tests fail; `Remesher` with `collapse_creates_flat` and `flip_improves_poor` switched off:
  `Primitives_NoFlatTriangles_FewerSelfIntersections` fails.

## Not changed (library or not splittable), for the owner

1. **The library's tiny loops starve on a busy pool** (up to 100x in the probe). Options, all library changes needing a benchmark:
   a serial path below a work estimate (the Reducer loop experiment in #698: serial lost 16-58 % alone on large meshes, so per call site,
   not global); a caller-first loop (tried for the Reducer: removed the starvation, but 2.7-3.3x slower alone there). For callers that
   run the library inside a busy pool (a server) this is a real cost, not only a test issue. No single operation takes above 20 s on one
   input alone (all above 5 s alone are in the table: at most 9.3 s), so no defect issue by the 20 s rule; the finding is written down
   in #698 and in the issue named in the PR.
2. `SolidifyNotClean_AlphaWrapped` and callees: parallel work outside `gParallel` (see above).
3. Own-cost rows (Meshing2 oracle rows, `WavyToeOnHighWaves`, corpus parts): one case each, the rule is judged on the time alone (all under 10 s alone).
