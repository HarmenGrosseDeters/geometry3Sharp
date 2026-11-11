# Changed-line coverage gate

Tier M. Goal: report (nightly on master and on demand, `.github/workflows/coverage.yml`; it blocks no PR) the unexecuted changed production lines, each with the PR that introduced it. Scope: the three existing
Linux test projects (the core one in four shards), collector settings, a standard-library Python gate and its tests, CI and QA docs. Non-goals:
library/mesher/oracle logic, API changes, whole-library thresholds, assertion-quality claims, mutation tooling,
FPTaylor and SharpFuzz. No clean-room restricted code was consulted.

Interface: `python3 tools/changed_line_coverage.py --base <commit> --coverage-dir <results> --json <output>`
[`--slow-coverage-dir <dir>`: the core report of the Category=Slow run; a changed line only it hits is reported as
`covered_only_by_slow` and does not fail; the workflow does not use it, it collects the Slow tier with the rest;
`--attribute` names the PR per line; `--expect-shards core=4` requires a report in each shard directory].
The tested HEAD and its merge base are recorded. One collector attachment at
`<suite>/<attachment-id>/coverage.cobertura.xml` in each of `cli/` and `clipper2/`, and one per shard at
`core/<shard>/<attachment-id>/coverage.cobertura.xml`, is mandatory. TRX's
nested `In/<host>/` copy is ignored. Hit counts merge by maximum: a hit in any suite covers the line. Filenames are resolved
against the actual checkout and Cobertura sources, never suffix-matched. A changed file that no report lists has no executable code and is only reported.
See [QA section 2.6](../../docs/QA.md#26-coverage-in-ci) for commands, scope and conservative limitations.

Acceptance: a real git-history fixture with an uncovered changed executable line exits 1; adding its hit in a
different suite exits 0; missing reports or shards and invalid inputs exit 2. Tests also cover merge bases, new files,
renames, deletion-only changes, comments, unusual paths, repeated calls and deterministic merging. Break the
decision and evidence checks by hand and require these tests to fail. Stop after this gate, not the other tools.

## Building blocks and prior art

The repository had no coverage collector, runsettings or diff gate; `tools/new_tests_on_base.py` already uses
standard-library Python and git histories for a separate assertion-quality check. This gate reuses that tooling
style, not its informational failure policy. No new runtime library dependency.

- [coverlet VSTest integration](https://github.com/coverlet-coverage/coverlet/blob/v6.0.4/Documentation/VSTestIntegration.md)
  (MIT): collect in the existing VSTest runs, with assembly filters and Cobertura source/line records.
- [.NET code coverage](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage):
  `--collect:"XPlat Code Coverage"`, collector package and results artifacts.
- [diff-cover](https://github.com/Bachmann1234/diff_cover) (Apache-2.0): intersect changed lines with executable
  report lines and union hits across reports. This gate uses the same approach, with stricter missing-report and
  missing-file handling and no extra Python package. Implementation is original; no third-party code ported.

Run the bounded hand mutations with
`python3 tools/coverage/check_mutants.py --output <output.json>` (isolated temporary copies; no Stryker changes).
Validation commands and results are recorded in `validation.json`. This gate measures execution only; it does
not replace the existing mutation workflow or independent review.
