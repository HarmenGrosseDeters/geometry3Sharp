# Cross-field geometry: three short feasibility spikes

Follow-up to the [research report](../../docs/research/CROSS_FIELD_GEOMETRY.md), ideas 1–3.
Executed on Windows with Python 3.14.0 and .NET SDK 10.0.401, from base commit
`96d6398685caf4237a3b4351fad181f016c61a1d`. Commands below produced the recorded results.

## Brief and stop rule

- **Goal:** cheaply falsify the smallest useful safety assumptions before proposing library changes.
- **Scope:** original, standard-library Python probes, their tests, and documentation. Tier M for diagnostic tooling;
  no production predicates, error bounds, mesher, or rounding changes. The proposed library mechanisms remain unapproved.
- **Non-goals:** reference-speed claims, city/Thingi10K corpus runs, a replacement Q17 oracle, a general patch solver,
  or a proof that the library's remaining failures are fixed. No mesh assets, new dependencies, or mutation-tooling edits.
- **Interface:** deterministic JSON from `probe.py`, exact `Fraction` arithmetic on explicit fixtures.
- **Acceptance:** exact expected fixture decisions; tests reject deliberately broken motion/incidence/contact checks;
  repeatability and selected rational transformations. Preserve existing library regression results.
- **Budget/stop:** one finite synthetic enumeration per idea plus negative controls. Stop before integration or profiling;
  do not start the original report's multi-hour real-trace experiments under the label of a short probe.
- **Prior art/building blocks:** see [sources](../../docs/SOURCES.md#cross-field-research-and-synthetic-feasibility-probes),
  [SafeRounding](../../docs/design/SAFE_ROUNDING.md), and [Q17](../../docs/design/MESHING2D_HULL_GRID.md).
  Existing `SafeRoundingBoundTests`, `QualityMesh2KeepsChainsApartTests`, exact predicates and the independent oracle
  are the eventual integration points, not copied implementations. No forbidden implementation was inspected.

**Result:** the elementary clearance condition survives this model; vertex-only identification remains invisible to
edge-overlap and sometimes component-count checks; a fixed-collar rounding fixture has feasible alternatives only
under the larger *explicitly tested* budget. None demonstrates production speed or solves the real failure corpus.

## Reproduce

Run from the repository root (Python 3.10 or newer; only 3.14.0 was exercised here):

```powershell
$env:PYTHONDONTWRITEBYTECODE = '1'
python benchmarks\CrossFieldSpikes\probe.py
python -m unittest discover -s benchmarks\CrossFieldSpikes -p 'test_*.py' -v
python benchmarks\CrossFieldSpikes\check_mutations.py
```

The first command's complete stdout is [results.json](results.json). It is deterministic fixture output, not a
performance baseline. The test command reported `Ran 10 tests` and `OK`. The mutation command first reruns the
unmodified tests, then mutates isolated temporary copies and requires assertion failures, not import errors:

```text
ignore_second_motion: killed by test_clearance_equality_is_not_safe
forget_new_vertex_identification: killed by test_shared_tip_is_allowed_but_new_identification_is_not
permit_touching_sheets: killed by test_small_budget_only_contact_is_rejected
```

Temporary directories are automatically removed. These hand mutations concern only this diagnostic code, not
Stryker or the repository mutation gate. Samples of barycentric positions in the tests are negative controls;
the whole-face guarantee below comes from affine bounds, not from sampling.

## 1. Clearance budget: both moving sides matter

Two congruent parallel triangles have exact initial gap $g$. Their translations toward each other have magnitudes
$a,b$, so the independent model's residual gap is $g-a-b$. The certificate accepts strictly when $g>a+b$; equality
is contact and must fall back. The enumerator tries $a,b\in\{0,g/8,\ldots,g\}$.

Output from `python benchmarks\CrossFieldSpikes\probe.py`:

| Gap | Queries | Certified | False accepts | False accepts if second-side motion is ignored |
|---|---:|---:|---:|---:|
| 1/4 | 81 | 36 | 0 | 36 |
| 1 | 81 | 36 | 0 | 36 |
| 4 | 81 | 36 | 0 | 36 |

The denominator is a deliberately constructed motion grid, **not a measured SafeRounding filter hit rate**.
This checks a movement-expiry condition, not whether BVH lower-bound construction or reuse is profitable. In
particular, no neighbor list is constructed here, and no approximate closest point is interpreted as an empty ball.

**Decision:** retain the safety mechanism as a candidate; reject the one-sided movement shortcut. The next real
spike must instrument complete primitive clearance and repeated movement on a rounding trace and measure cost
against work avoided. No speed upper bound was measured, so no performance optimization is justified yet.

## 2. Q17: a vertex pinch without an overlapping edge

The inputs here are **declared already-snapped polylines**, not raw polygons passed through SRR. Parallel horizontal
chains from $(-2,-1)$ to $(2,-1)$ and from $(-2,1)$ to $(2,1)$ are rerouted through $(0,0)$. A second variant appends
the common tip $(4,0)$ to both chains before the reroute.

Output from the same probe command:

| Fixture | Components before → after | New common vertices | Positive-length edge overlaps |
|---|---|---:|---:|
| Separate chains | 2 → 1 | 1 | 0 |
| Already shared distant tip | 1 → 1 | 1 | 0 |

The second variant kills the proposition that unchanged connected-component count suffices. Both kill the proposition
that refusing coincident edges alone prevents every vertex-only identification. Unchanged shared tips are explicitly
accepted by the diagnostic `new_common_vertices` set difference; a new shared center is distinguished from that tip.

**Important limitation:** this function is **not a Q17 acceptance checker**. It detects only shared listed vertices;
it does not certify source correspondence, cyclic sectors, input-only restore, induced hot-pixel reroutes, geometric
crossings without listed vertices, or ambient isotopy. This construction does not prove that the production refiner
can generate these particular polylines. It is a counterexample to coarse invariants, not a reproduced city failure.
The test suite also applies exact scale, translation, quarter-turn and reflection transformations to the witness.

**Decision:** reject edge-overlap-only and component-count-only guards as complete proofs; retain the transaction/
embedding proposal. The next probe needs a minimized *actual* remaining oracle failure and a complete reroute
transaction, not a direct port of the set-difference helper into the library.

## 3. Fixed-collar local rounding: an exhaustive small configuration

Each of two surface patches is a four-triangle fan over the same square with XY corners $(\pm2,\pm2)$ and a center
at $(0,0)$. All lower collar vertices have $z=-1$ and all upper collar vertices have $z=1$; they stay fixed. Original
center heights are $-1/4$ and $1/4$. The experiment permits **only integer center heights**, preserving XY, face
connectivity and the collar. Original and candidate coordinates are exactly binary-representable in this fixture.

Each patch is the graph of a piecewise-affine function over the identical nondegenerate XY triangulation. Strictly
positive upper-minus-lower height at every corresponding vertex therefore proves separation over every face.
Fixed XY projection rules out folds within a fan. The maximum center displacement bounds the whole patch in both
directions by the same barycentric correspondence. The checker rejects projections outside this restricted family.
These are open patches, not certified closed solids; closed/manifold-solid claims would be inappropriate here.

Output from the same probe command:

| Displacement budget | Candidate pairs | Separated pairs | Nearest-rounding pair |
|---|---:|---|---|
| 1/4 | 1 | none | `(0, 0)`, contact |
| 3/4 | 4 | `(-1, 0)`, `(-1, 1)`, `(0, 1)` | `(0, 0)`, contact |

Every feasible pair in the second row uses maximum displacement exactly `3/4`. The script enumerates all integer
center heights within each stated budget, rather than truncating the search to a guessed neighborhood.

**Decision:** the small-budget search is infeasible **only for these fixed centers/XY/connectivity/grid choices**;
the larger-budget alternative demonstrates feasibility, not authorization to relax the library's budget. There is
no external mesh in this fixture, no implicit-point conversion, and no theorem about arbitrary collars or clusters.
The next probe needs a real defect component, exterior exclusion and a measured active-variable count. A larger
budget is not proposed as the solution to any production case.

## Library validation before publishing

The following commands were executed unchanged in the isolated worktree. The first command reported **34 passed,
0 failed, 0 skipped on each of net10.0 and net48**. These are existing library regressions, not new-feature tests and
not evidence that the remaining Q17 vertex-pinches are fixed.

```powershell
dotnet test tests\geometry3Sharp.Tests -c Release --filter 'FullyQualifiedName~SafeRoundingBoundTests|FullyQualifiedName~QualityMesh2KeepsChainsApartTests.RefinementKeepsASpikeTip'
dotnet build geometry3Sharp_netstandard.csproj -c Release
dotnet build tests\geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true
dotnet build tests\geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o "$env:TEMP\g3-crossfield-pack" -p:Version=1.0.0-ci.1
dotnet pack addons\geometry3Sharp.Clipper2\geometry3Sharp.Clipper2.csproj -c Release -o "$env:TEMP\g3-crossfield-pack" -p:Version=1.0.0-ci.1
```

All commands exited successfully. The library build covered net10.0 and netstandard2.0; the explicit net48 test
builds passed; both packages were produced, with the main package's configured upstream API validation enabled.
The Clipper2 build emitted existing CS1591 missing-public-XML-comment warnings in unchanged source; no warnings
were suppressed or unrelated files fixed. Package smoke tests and the full library suites were not run locally for
this tools/new-files-only change; the repository CI remains the full-suite gate. The new Python commands above
are local checks, not newly wired into the existing CI workflow.

**Review gate:** the independent review agent failed before returning findings (model-provider HTTP 502).
Author inspection and executable checks do not replace that review. Publication stays draft pending an independent
code/bounds review; no production integration or ready-for-merge claim is made.
