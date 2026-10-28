# Working on geometry3Sharp

How we work with agents (tiers, task brief, review): [docs/AGENT_WORKFLOW.md](docs/AGENT_WORKFLOW.md).
Direction, phases and open items: [docs/ROADMAP.md](docs/ROADMAP.md). Sources of ported or implemented
methods: [docs/SOURCES.md](docs/SOURCES.md). Test strategy: [docs/QA.md](docs/QA.md), local
setup (SDK install): [docs/QA.md#5-local-setup](docs/QA.md#5-local-setup).

## Rules

- Answer the user in German. Code, comments, commit messages, PRs and docs stay in English.
- Every change comes with tests, if possible ones that fail on the old code. Check them with mutations
  (break the change, the tests must fail).
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
- Stay API compatible with upstream 1.0.324.

## Before every push

Run the steps of `.github/workflows/ci.yml` locally:

```
dotnet test tests/geometry3Sharp.Tests -c Release
dotnet test tests/g3cli.Tests -c Release
dotnet pack geometry3Sharp_netstandard.csproj -c Release -o <dir> -p:Version=1.0.0-ci.1
dotnet run --project tests/PackageSmokeTest -c Release -p:G3PackageVersion=1.0.0-ci.1 -p:G3PackageSource=<dir> -- <dir>/geometry3Sharp.1.0.0-ci.1.nupkg
dotnet build tests/geometry3Sharp.Tests -c Release -f net48 -p:TestNet48=true
dotnet build benchmarks/<each>/<each>.csproj -c Release
```

Every push gets a PR; watch its CI and fix red CI right away.

## Review before every merge

Before a PR is marked ready or merged, review its diff (`/code-review`, or an agent that gets the code but not
the author's reasoning, so it is not anchored on it), with [docs/REVIEW_CHECKLIST.md](docs/REVIEW_CHECKLIST.md)
(the fault classes earlier reviews found). Fix the findings in the same PR, or say in the PR why not. Changes
to exact arithmetic (`math/Exact*`, `math/AdaptivePredicates.cs`, implicit points, error bounds) and to
`mesh_ops/ExactMesh*` always get this review, including a check of every new error bound; on PRs that touch
the mutated files, the mutation workflow (`tests/mutation`) must stay above its threshold.

Performance or quality claims are backed by a benchmark (`benchmarks/`), with before/after numbers in the
benchmark README or the PR.
