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
candidate including shrinks. Eight wrong-family negative controls ensure that
simply accepting every descriptor is detected. All generated world coordinates
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
and separately makes the family classifier always accept. Their dedicated
negative tests must fail. Finally it restores both sources and reruns the full
property/metamorphic selection. No production files or Stryker configuration are
mutated. Run it in a clean worktree without other builds.

Captured baseline, final campaign/shrink output, mutation results and build/API
validation are in `results/`. Runtime is a bounded single-run observation, not a
performance improvement claim. The default of 32 guarantees dedicated family
coverage while retaining all old campaign counts; raise it for longer local runs.

## Captured outcome (2026-11-07)

[campaigns_2026-11-07.json](results/campaigns_2026-11-07.json) contains TRX-derived
per-test output, seeds, original/minimized descriptors and durations. Baseline:
67 passed, no skips, suite summary 18 s. Final: 88 passed, no skips, suite summary
23 s. These are single bounded runs, not a statistical overhead estimate. The
new eight 32-case campaigns took about 0.3–4.6 s apiece in the final run; the
strictly nested campaign was the largest. All original sample counts remain.

The controlled disjoint fault shrank twice, to seed `56gv4DEPl3T1`; the
face-contact fault shrank once, to `aAfa5JNMAVJ1`. Both minimized seeds were
replayed inside the tests and retained their relation while failing the analytic
Union oracle. "Smaller" is CsCheck's size ordering, not a globally minimal mesh.

[mutations_2026-11-07.json](results/mutations_2026-11-07.json) records the two helper
faults: empty-before-finiteness fails 3/3 dedicated tests; always-accept-family
fails 8/8. The restored full selection passes 88/88. No real production defect
was found by the added cases. Independent review and recheck found no remaining
blocking issue after the malformed-empty oracle finding was fixed.

[Build/API evidence](results/builds_2026-11-07.json): both net48 test-project builds
and both API-validated packs (core and Clipper2, net10.0/netstandard2.0) pass. Existing
compiler warnings remain. The full unrelated .NET suite and net48 execution were
not run locally; remote CI runs them. This is a test-only feature: no claim that
new geometry properties should fail a correct old library. Controlled faulty
results/helpers provide non-vacuity evidence instead.
