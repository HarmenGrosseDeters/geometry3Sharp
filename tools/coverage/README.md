# Changed-line coverage gate

Tier M. Goal: reject a PR with unexecuted changed production lines during normal CI. Scope: the three existing
Linux test projects, collector settings, a standard-library Python gate and its tests, CI and QA docs. Non-goals:
library/mesher/oracle logic, API changes, whole-library thresholds, assertion-quality claims, mutation tooling,
FPTaylor and SharpFuzz. No clean-room restricted code was consulted.

Interface: `python3 tools/changed_line_coverage.py --base <commit> --coverage-dir <results> --json <output>`
[`--slow-coverage-dir <dir>`: the core report of the Category=Slow run; a changed line only it hits is reported as
`covered_only_by_slow` and does not fail; CI collects the fast tier first and the Slow tier only when lines are left].
The tested HEAD and its merge base are recorded. Exactly one collector attachment at
`<suite>/<attachment-id>/coverage.cobertura.xml` in each of `core/`, `cli/` and `clipper2/` is mandatory. TRX's
nested `In/<host>/` copy is ignored. Hit counts merge by maximum: a hit in any suite covers the line. Filenames are resolved
against the actual checkout and Cobertura sources, never suffix-matched. Unknown changed files fail closed.
See [QA section 2.6](../../docs/QA.md#26-coverage-in-ci) for commands, scope and conservative limitations.

Acceptance: a real git-history fixture with an uncovered changed executable line exits 1; adding its hit in a
different suite exits 0; missing reports/files and invalid inputs exit 2. Tests also cover merge bases, new files,
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
