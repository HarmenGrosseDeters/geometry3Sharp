#!/usr/bin/env python3
"""Decide from the changed files of a pull request which parts of CI have to run (docs/QA.md, "Test tiers").

A PR that changes only tools, benchmarks or docs that the library build and the test projects do not read need not run
the library test suites (net10.0, net48 shards, Clipper2): the library is the same, and the tests of the tool or
benchmark that changed run anyway (tool tests, benchmark builds, the package checks). Anything not known to be
irrelevant counts as relevant: a path is "tool only" only if it is under tools/, benchmarks/ or docs/ and is not
one the test projects read (SHARED below; test_ci_scope.py checks that every path a tests/*.csproj includes from
there is listed; a test that opens a file there by path needs a hand-made entry). Markdown files outside docs/design are not CI triggers at all (the paths filter of ci.yml).

Prints key=value lines for $GITHUB_OUTPUT:
    library=true|false    the library test suites run (net10.0, net48, Clipper2)
    g3cli=true|false      the g3cli tests run (the library changed, or tools/g3cli did)
    ps1=true|false        the benchmark checks of the net48 job run: the reporting regression (benchmarks/MeshRepairBenchmark changed) and
                          the net48 / net10.0 smoke run of benchmarks/net48-compare (that directory or one of its three benchmarks changed)

Usage:
    git diff --name-only HEAD^1 HEAD | python3 tools/ci_scope.py
    python3 tools/ci_scope.py --all        # push to master, manual runs: everything
"""

import sys

# directories and files below tools/, benchmarks/, docs/ that the test projects (tests/*/*.csproj) or the library read
SHARED = (
    "benchmarks/QuadtreeBenchmark/",
    "benchmarks/Meshing2Benchmark/Oracle/",
    "benchmarks/Meshing2Benchmark/Candidates/",
    "benchmarks/Meshing2Benchmark/MeshCommand.cs",
    "benchmarks/Meshing2Benchmark/golden/",
    "benchmarks/TerrainBenchmark/ClosestFields.cs",
    "benchmarks/ReducerRemesherBaseline/Harness.cs",
    "benchmarks/BooleanComparison/ContactTransforms.cs",       # ThingiContactTransformTests
    "tools/analyzers/",
    "tools/PredicateCompiler/Compiler.cs",
    "tools/PredicateCompiler/Monomials.cs",
    "tools/PredicateCompiler/predicates.spec",
    "tools/PredicateCompiler/indirect.spec",
    "tools/PredicateCompiler/doubles.spec",
    "tools/g3cli/TrianglePolyFormat.cs",
    "tools/ReaderFuzzPilot/FuzzTarget.cs",        # ReaderFuzzReplayTests
    "tools/ReaderFuzzPilot/ReaderTargets.cs",
    "tools/ReaderFuzzPilot/ReplayMutations.cs",
    "docs/examples/",
    "docs/OPERATIONS.md",             # DocExamplesTests
    "docs/ROBUSTNESS_SWEEP.md",      # RobustnessSweepTests
    "docs/design/EXACT_FALLBACK_MONOMIALS.md",
    "docs/design/FIXED_WIDTH_EXACT_POINTS.md",
    "docs/design/DETERMINISM_PROOF.md",       # DeterminismScopeAnalyzerTests (the stage 0 ledger)
    "docs/design/INDIRECT_PREDICATES.md",
    "docs/design/MESHING2D_GOLDEN_FORMAT.md",
    "docs/design/MESHING2D_CATALOGUE.md",
)
TOOL_DIRS = ("tools/", "benchmarks/", "docs/")
PS1_DIRS = ("benchmarks/MeshRepairBenchmark/", "benchmarks/net48-compare/", "benchmarks/BooleanComparison/", "benchmarks/SmallInputWrapBenchmark/")


def is_shared(path):
    return any(path == s or (s.endswith("/") and path.startswith(s)) for s in SHARED)


def affects_library_tests(path):
    """True if the library test suites must run for a change to this path."""
    if path.startswith(TOOL_DIRS) and not is_shared(path):
        return False
    return True


def scope(paths):
    paths = [p.strip() for p in paths if p.strip()]
    library = any(affects_library_tests(p) for p in paths)
    return {
        "library": library,
        "g3cli": library or any(p.startswith("tools/g3cli/") for p in paths),
        "ps1": library or any(p.startswith(PS1_DIRS) for p in paths),
    }


def main(argv):
    if "--all" in argv or not paths_available():
        result = {"library": True, "g3cli": True, "ps1": True}
    else:
        result = scope(sys.stdin.read().split("\n"))
    for k, v in result.items():
        print(f"{k}={'true' if v else 'false'}")
    return 0


def paths_available():
    return not sys.stdin.isatty()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
