# Working on geometry3Sharp

How we work with agents (tiers, task brief, review): [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md).
Direction, phases and open items: [docs/ROADMAP.md](docs/ROADMAP.md). Sources of ported or implemented
methods: [docs/SOURCES.md](docs/SOURCES.md). Test strategy: [docs/QA.md](docs/QA.md), local
setup (SDK install): [docs/QA.md#5-local-setup](docs/QA.md#5-local-setup).

## Rules

- Answer the user in German. Code, comments, commit messages, PRs and docs stay in English.
- Every change comes with tests, if possible ones that fail on the old code. Check them with mutations
  (break the change, the tests must fail). CI runs the test methods a PR adds against the base branch's library and
  reports which fail there (`New tests on base`, `tools/new_tests_on_base.py`); with the label `bugfix` at least one
  must fail.
- A new or changed operation also gets: an invariance test over `Similarity.All()`
  (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`; the 2^k scales must give the same result bit for bit),
  a reuse test (compute twice on one instance) if it has `Compute()`, and a determinism test (parallel equals
  serial, repeated runs) if it has a parallel path. Tests of a success path check the result (closed,
  manifold, finite, eg `MeshRobustRepair.NotCleanReason`), not only the status.
- A new reader or input format is added to `ReaderFuzzTests`.
- Licenses: implement from papers; port only from MIT / Apache / BSD sources, never from GPL code (CGAL,
  parts of TetWild). List the sources in `docs/SOURCES.md`.
- Do not commit mesh files (Thingi10K and other test meshes have their own licenses); generate test meshes
  procedurally.
- Stay API compatible with upstream 1.0.324. Enforced: `dotnet pack` validates the API against the 1.0.324
  package and fails on a break; an intentional break goes into `CompatibilitySuppressions.xml`, justified in the PR.
- In the exact code (`math/Exact*`, `math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*` and the exact files they call,
  listed in `.editorconfig`) FMA, `float` and parallel loops are build errors (`BannedSymbols.txt`, analyzers
  G3E0001-3). A new parallel loop there needs a review (no floating-point reduction across threads) and an entry in
  `core/ExactCodeAllowList.cs`.

## Before every push

Docs-only changes (only `*.md` files) need no local CI steps, and GitHub CI skips them (the `paths` filter in
`ci.yml`): re-read the diff and push. Exception: `docs/design/FIXED_WIDTH_EXACT_POINTS.md`, whose bound table
`GeneratedPredicatesTests` checks against the generator, runs CI and the generator tests. A change to existing library code, including code in `docs/examples`, runs
the full list. A change to tools, tests or new files only runs the filtered tests of the changed areas, the builds of
all targets (net10.0, netstandard2.0, the net48 test projects) and `dotnet pack` (API validation); GitHub CI runs the
rest. One PR per feature, as clean commits, not stacked sub-step PRs; Stryker locally once per shard to set its
threshold, then the CI run is the gate; an upper-bound check before any performance spike; and before designing an
optimization or algorithm, a search of the library for existing building blocks and a literature check
([AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md) sections 2, 3 and 8).

Run the steps of `.github/workflows/ci.yml` locally:

```
dotnet test tests/geometry3Sharp.Tests -c Release
dotnet test tests/g3cli.Tests -c Release
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o <dir> -p:Version=1.0.0-ci.1
dotnet run --project tests/PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=<dir> -- <dir>/geometry3Sharp.1.0.0-ci.1.nupkg
dotnet build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true
dotnet test tests/geometry3Sharp.Clipper2.Tests -c Release
dotnet pack addons/geometry3Sharp.Clipper2/geometry3Sharp.Clipper2.csproj -c Release -o <dir> -p:Version=1.0.0-ci.1
dotnet run --project tests/Clipper2PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=<dir> -- <dir>/geometry3Sharp.Clipper2.1.0.0-ci.1.nupkg
dotnet build tests/geometry3Sharp.Clipper2.Tests -c Release -f net48 -p:TestNet48=true
dotnet build benchmarks/<each>/<each>.csproj -c Release
dotnet build tools/PredicateCompiler -c Release
```

Draft PRs and spikes: before a push, build the library for all targets (net10.0, netstandard2.0) and the net48 test
project, and run the tests of the changed areas (`dotnet test --filter ...`). The full list above runs before the PR
is marked ready for review; the CI of every push runs it on GitHub anyway.

Every push gets a PR; watch its CI and fix red CI right away. CI runs on pull requests and on master only (a branch
without a PR, or a PR with a merge conflict, which GitHub does not run: resolve the conflict, or start it with
`workflow_dispatch`), and a new push cancels the PR's run under way.

## Review before every merge

Before a PR is marked ready or merged, review its diff (`/code-review`, or an agent that gets the code but not
the author's reasoning, so it is not anchored on it), with [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md)
(the fault classes earlier reviews found). Fix the findings in the same PR, or say in the PR why not. Changes
to exact arithmetic (`math/Exact*`, `math/AdaptivePredicates.cs`, implicit points, error bounds) and to
`mesh_ops/ExactMesh*` always get this review, including a check of every new error bound; on PRs that touch
the mutated files, the mutation workflow (`tests/mutation`) must stay above its threshold.

Performance or quality claims are backed by a benchmark (`benchmarks/`), with before/after numbers in the
benchmark README or the PR.
