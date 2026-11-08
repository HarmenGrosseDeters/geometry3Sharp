# Boolean property coverage follow-up

Task brief: tier M, test-only quality assurance following the Z3 pilot. Close the
family-coverage gap in [the accepted plan](../../../docs/design/BOOLEAN_PROPERTY_TESTS.md).
Scope is the existing Boolean tests/helpers and their documentation. No production
Boolean/arithmetic code, dependencies, public API, CI or mutation tooling changes.
If a real production defect appears, retain the procedural repro and report it
separately; never discard a case or weaken a property to make the campaign pass.

## Added coverage

The four original generated campaigns remain unchanged. Eight additional
`DirectedBoxes` campaigns guarantee disjoint, identical, strictly nested,
partially overlapping, coplanar-overlapping, face-contact, edge-contact and
vertex-contact cases. Each has its own fixed campaign seed (11 through 18), 32
normal cases, and the existing exact dyadic frame generator. Total: 256 additional
cases, each running the existing six-operation algebraic/analytic check.

Construction uses positive bounded integer intervals. The independent classifier
uses overlap lengths, containment and plane equalities; it is checked on every
candidate including shrinks. `overlapping` means generic partial overlap (no shared
boundary plane), which keeps it disjoint from `coplanar_overlap`. Eight wrong-family
negative controls ensure that simply accepting every descriptor is detected; 27
targeted neighbour controls (`DirectedFamilyCheck_RejectsNeighbouringRelations`:
one contact dimension more or less, a zero gap, a shared plane, non-strict or
reversed containment) catch classifiers weakened toward an adjacent family, and
eight canonical members (`DirectedFamilyCheck_AcceptsCanonicalCase`) ensure the
classifier does not simply reject everything. All generated world coordinates
remain exact under the existing 2^k scale and dyadic translation bounds.

`DirectedCampaign_ShrinksAndReplaysWithinItsFamily` injects a **test-local wrong
operation** for disjoint and face-touching cases. It runs Difference but asks the
analytic oracle for Union. Both original and minimized descriptors must remain
in their named family, shrinking must make at least one change, and direct
`Gen.Sample` replay of the minimized seed must reproduce the descriptor and fail
the oracle again. It catches this expected failure so the committed suite is green.
The emitted failure text is captured in the TRX-derived evidence. As with the
older harness self-test, its generic suggested CLI filter is not the replay path
used by the self-test; minimized-seed replay is explicitly inside the test.

Review also exposed an existing test-oracle weakness: empty results skipped finite
coordinate validation. Three controlled malformed-empty fixtures (NaN, +infinity,
-infinity vertices) now fail `CleanReason` and `CheckBoxResult`. Truly empty meshes
still pass without invoking nonempty-surface checks. These fixtures do not imply
that the production Boolean returned such malformed output.

## Commands and replay

Use the pinned CsCheck 4.9.1 already referenced privately by the net10.0 test
project; no new dependency or external geometry source is needed. Building blocks
are the existing interval/mesh generators, BooleanOracle's analytic cell reference,
Campaign's deterministic seeds and shrinking, and the CsCheck API documented by
the original plan. The fixed deterministic regressions, including malformed-empty
controls, continue to compile for net48.

```sh
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter 'FullyQualifiedName~ExactBooleanProperty|FullyQualifiedName~ExactBooleanMetamorphic'
# Only the directed campaigns:
dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~ExactBooleanPropertyTests.DirectedBoxes
# Replay an actual DirectedBoxes failure (other directed families run normally):
G3_PROPERTY_CAMPAIGN='DirectedBoxes(face_touching)' G3_PROPERTY_REPLAY='<reported seed>' dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~ExactBooleanPropertyTests.DirectedBoxes
# Larger opt-in campaign; every sample/shrink is still bounded:
G3_PROPERTY_SAMPLES=2000 dotnet test tests/geometry3Sharp.Tests -c Release -f net10.0 --filter FullyQualifiedName~ExactBooleanPropertyTests.DirectedBoxes
# Controlled helper mutations, restoring files and rerunning the full property suite:
python3 tests/geometry3Sharp.Tests/property_validation/check_mutations.py dotnet /tmp/property-mutations.json
```

The script temporarily moves the empty-result shortcut ahead of finite checks,
makes the family classifier always accept or always reject, restores the
nondeterministic shrink source, and weakens single families toward a neighbour
(eight variants). Their dedicated tests must fail. Finally it restores both sources and reruns the full
property/metamorphic selection. No production files or Stryker configuration are
mutated. Run it in a clean worktree without other builds.

Captured baseline, final campaign/shrink output, mutation results and build/API
validation are in `results/`. Runtime is a bounded single-run observation, not a
performance improvement claim. The default of 32 guarantees dedicated family
coverage while retaining all old campaign counts; raise it for longer local runs.

## Captured outcome (2026-11-08)

[campaigns_2026-11-08.json](results/campaigns_2026-11-08.json) is a compact summary
of the TRX files (counts, per-campaign durations, original/minimized descriptors of
the controlled shrink; no paths or logs). Both runs come from pure trees: the
baseline from master `12ae9276` (this PR's base after merging master), the final
run from this branch; neither contains the Z3 pilot (#400) tests. An earlier capture
(2026-11-07) came from a tree that also compiled the #400 tests and is superseded.
Baseline: 67 passed, no skips, about 16 s. Final: 124 passed, no skips, about 18 s
(57 added tests: 8 directed campaigns, 8 + 27 + 8 classifier controls, 2 shrink
tests, 1 shrink-determinism test, 3 malformed-empty controls). These are single
bounded runs, not a statistical overhead estimate. The eight 32-case campaigns took
about 0.3–0.9 s apiece. All original sample counts remain.

Shrinking is deterministic for a given failing seed. The harness used to shrink with
CsCheck's `Sample(seed, iter: 400)`, which runs the seed first and draws every
further iteration from `PCG.ThreadPCG`: a thread-static generator seeded differently
in each process (checked: it prints a different seed per process) and advanced by
every test on the thread. The minimized seed and the shrink count therefore changed
from run to run. `ExactBooleanPropertyTests.Shrink` now derives candidate k's seed
from the failing seed and k only (SplitMix64), and otherwise follows CsCheck's rule:
a candidate not smaller by CsCheck's size order is skipped, a smaller failing one
becomes the new best. `Shrink_IsDeterministicForAGivenSeed` shrinks the same seed
twice, with `PCG.ThreadPCG` advanced in between, and requires the same seed, count
and case; it fails when the candidates come from `PCG.ThreadPCG` again (mutant
`nondeterministic_shrink`). In every run both controlled faults (disjoint and face
contact, wrong operation) shrink four times, to `3xHD4wyiBWVv` (both families map the
same seed sequence through the same generator structure), stay in their family, and
the replayed minimized seed fails the analytic Union oracle again. "Smaller" is
CsCheck's size ordering, not a globally minimal mesh.

[mutations_2026-11-08.json](results/mutations_2026-11-08.json) records the helper
faults: empty-before-finiteness fails 3/3 dedicated tests; always-accept-family
fails 8/8 wrong-relation controls; always-reject-family fails 8/8 canonical accepts;
the nondeterministic shrink fails the determinism test.
Each of the eight neighbour-directed weakenings fails 1 to 6 of the 27 neighbour
controls; seven of them still pass the original eight wrong-relation controls. The
restored full selection passes 124/124. No real production defect was found by the
added cases.

[Build/API evidence](results/builds_2026-11-08.json): the library (net10.0,
netstandard2.0), both net48 test-project builds and both API-validated packs (core
and Clipper2) pass. The full unrelated .NET suite and net48 execution were not run
locally; remote CI runs them. This is a test-only feature: no claim that new
geometry properties should fail a correct old library. Controlled faulty
results/helpers provide non-vacuity evidence instead.
