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

With --quick (the job `quick` of ci.yml, the first stage of PR CI, owner 2026-11-23) it prints instead:
    filter=<dotnet test filter>   the net10.0 tests of the changed areas (empty: no library test to run)
    broad=true|false              a change too broad to map (test harness, build settings, a type that most tests use):
                                  the smoke set plus the directly named classes only; the full matrix (label `full-ci`) is the gate
and with --summary <file> a markdown summary of the selection (appended).

Usage:
    git diff --name-only HEAD^1 HEAD | python3 tools/ci_scope.py
    python3 tools/ci_scope.py --all        # push to master, manual runs: everything
    git diff --name-only HEAD^1 HEAD | python3 tools/ci_scope.py --quick [--summary <file>]
"""

import os
import re
import sys

# directories and files below tools/, benchmarks/, docs/ that the test projects (tests/*/*.csproj) or the library read
SHARED = (
    "benchmarks/QuadtreeBenchmark/",
    "benchmarks/Meshing2Benchmark/Oracle/",
    "benchmarks/Meshing2Benchmark/Candidates/",
    "benchmarks/Meshing2Benchmark/MeshCommand.cs",
    "benchmarks/Meshing2Benchmark/golden/",
    "benchmarks/TerrainBenchmark/ClosestFields.cs",
    "benchmarks/Shared/UvEvaluation.cs",   # UVAtlasEvaluationTests (the evaluation of a UV atlas)
    "benchmarks/ReducerRemesherBaseline/Harness.cs",
    "benchmarks/BooleanComparison/ContactTransforms.cs",       # ThingiContactTransformTests
    "benchmarks/BooleanComparison/Pinches.cs",                 # BooleanComparisonPinchesTests
    "benchmarks/BooleanComparison/InsideOutDiagnostics.cs",    # BooleanComparisonInsideOutDiagnosticsTests
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


# --- the quick stage: which net10.0 test classes a change needs (a signal; the full matrix on the final head is the gate)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEST_DIR = "tests/geometry3Sharp.Tests/"
HARNESS = TEST_DIR + "Harness/"
# more test files than this for one change: broad (the quick stage stays near 5 minutes)
MAX_CLASSES = 30
# a few fast classes over the core types, run for every library change (and alone for a broad one)
SMOKE = ("DMesh3PropertyTests", "MeshEditOpsPropertyTests", "VectorIsFiniteTests", "Frame3dQueryTests",
         "PolygonSignedAreaTests", "MeshNormalsScaleTests")
# files that the tests read by path and the classes that read them
READ_BY = {
    "docs/OPERATIONS.md": ("DocExamplesTests",),
    "docs/ROBUSTNESS_SWEEP.md": ("RobustnessSweepTests",),
}
QUICK_FILTER_TAIL = "Category!=Slow&Category!=Nightly"
_CLASS = re.compile(r"^[^/\n]*?\b(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)", re.M)
_TEST_ATTR = re.compile(r"\[(?:Fact|Theory|Property)\b")
_NAMESPACE = re.compile(r"^\s*namespace\s", re.M)


def _read(root, path):
    try:
        with open(os.path.join(root, path), encoding="utf-8", errors="replace") as f:
            return f.read()
    except OSError:
        return None


def declared_types(text):
    """the type names a C# file declares (not in a `//` comment line)"""
    return sorted({m.group(1) for m in _CLASS.finditer(text or "")})


def test_files(root):
    """repository-relative paths of the .cs files of the test project, without the harness"""
    out = []
    base = os.path.join(root, TEST_DIR)
    for d, _, files in os.walk(base):
        for f in files:
            if f.endswith(".cs"):
                rel = os.path.relpath(os.path.join(d, f), root).replace(os.sep, "/")
                if not rel.startswith(HARNESS) and "/bin/" not in rel and "/obj/" not in rel:
                    out.append(rel)
    return sorted(out)


def test_classes_of(text):
    """the test classes a test file declares (names with `Test` in them), [] for a file without tests"""
    if not text or not _TEST_ATTR.search(text):
        return []
    return [t for t in declared_types(text) if "Test" in t]


def quick_tests(paths, root=ROOT, files=None):
    """{"classes": sorted test class names, "broad": bool, "notes": [why]} for the changed paths"""
    paths = [p.strip() for p in paths if p.strip()]
    files = test_files(root) if files is None else files
    texts = {}

    def text(f):
        if f not in texts:
            texts[f] = _read(root, f) or ""
        return texts[f]

    classes, notes, broad, library = set(), [], False, False
    for p in paths:
        if not affects_library_tests(p) and p not in READ_BY:
            continue
        if p in READ_BY:
            classes |= set(READ_BY[p])
            library = True
            continue
        if p.startswith(HARNESS):
            broad = library = True
            notes.append(f"`{p}`: test harness")
            continue
        if p.startswith(TEST_DIR):
            library = True
            if not p.endswith(".cs"):
                broad = True
                notes.append(f"`{p}`: test project file")
                continue
            found = test_classes_of(text(p))
            if found:
                classes |= set(found)
            elif os.path.exists(os.path.join(root, p)):
                # a helper of the tests (no [Fact] in it): the tests that use its types
                broad |= _users(p, text, files, classes, notes)
            continue
        if p.startswith("tests/"):
            notes.append(f"`{p}`: another test project, full run only")
            continue
        if p.startswith("docs/examples/"):
            classes.add("DocExamplesTests")
            library = True
            continue
        if p.startswith("addons/"):
            notes.append(f"`{p}`: add-on, its tests run in the full run only")
            continue
        library = True
        if not p.endswith(".cs"):
            broad = True
            notes.append(f"`{p}`: build settings or data")
            continue
        if not os.path.exists(os.path.join(root, p)):
            # (a deleted file: the build is the check)
            notes.append(f"`{p}`: deleted, the build checks it")
            continue
        broad |= _users(p, text, files, classes, notes)
    if library:
        classes |= set(SMOKE)
    # classes in files without a namespace have the FullyQualifiedName `C.M` (no leading dot): quick_filter matches them by `C.`
    in_global = {c for f in files if not _NAMESPACE.search(text(f)) for c in test_classes_of(text(f))} & classes if classes else set()
    return {"classes": sorted(classes), "broad": broad, "notes": notes, "global": sorted(in_global)}


def _users(path, text, files, classes, notes):
    """adds the test classes of the files that name a type `path` declares; True if they are too many (broad): then only the
    classes whose names start with such a type are added"""
    # the file's own types: those whose names the file name starts with or that start with it (MeshRoundingRepairIncremental.cs:
    # partial class MeshRoundingRepair), else every type it declares; nested helpers (Before, Ghost, ...) would match far too much
    stem = os.path.basename(path)[:-len(".cs")].split(".")[0]
    declared = [t for t in declared_types(text(path)) if len(t) > 3]
    types = [t for t in declared if stem.startswith(t) or t.startswith(stem)] or declared
    if not types:
        notes.append(f"`{path}`: declares no type")
        return False
    word = re.compile(r"\b(?:" + "|".join(map(re.escape, types)) + r")\b")
    users = [f for f in files if f != path and word.search(text(f))]
    if len(users) > MAX_CLASSES:
        named = {c for f in users for c in test_classes_of(text(f)) if any(c.startswith(t) for t in types)}
        classes |= named
        notes.append(f"`{path}`: {len(users)} test files use {', '.join(types[:5])}{' ...' if len(types) > 5 else ''}; only the directly named classes")
        return True
    for f in users:
        classes |= set(test_classes_of(text(f)))
    return False


def quick_filter(classes, in_global=()):
    """the dotnet test filter for the classes: `.C.` matches the class C in any namespace (not C2 or XC), `+C.` a nested class C,
    and `C.` a class of the global namespace (FullyQualifiedName `C.M`; it may match more, never less); '' for none"""
    if not classes:
        return ""
    terms = []
    for c in sorted(classes):
        terms += [f"FullyQualifiedName~{c}."] if c in in_global else [f"FullyQualifiedName~.{c}.", f"FullyQualifiedName~+{c}."]
    return "(" + "|".join(terms) + ")&" + QUICK_FILTER_TAIL


def quick_summary(result):
    lines = ["### Quick stage: net10.0 tests of the changed areas", ""]
    if result["broad"]:
        lines.append("**Broad change: smoke set only** (plus the directly named classes). The full matrix (label `full-ci`) is the gate.")
        lines.append("")
    if result["classes"]:
        lines.append(f"{len(result['classes'])} classes, without Slow and Nightly: " + ", ".join(f"`{c}`" for c in result["classes"]))
    else:
        lines.append("No library test to run (build only).")
    for n in result["notes"]:
        lines.append(f"- {n}")
    return "\n".join(lines) + "\n"


# Markdown files that ci.yml does run for (its pull_request paths): design docs that a test checks against a generator
CI_MARKDOWN = ("docs/design/FIXED_WIDTH_EXACT_POINTS.md", "docs/design/DETERMINISM_PROOF.md")


def docs_only(paths):
    """True if ci.yml does not run for these changes at all (only Markdown outside CI_MARKDOWN): docs.yml then posts the
    merge gate `full-ci` itself (owner 2026-11-23), as no full matrix will ever run on such a PR"""
    paths = [p.strip() for p in paths if p.strip()]
    return bool(paths) and all(p.endswith(".md") and p not in CI_MARKDOWN for p in paths)


def main(argv):
    if "--docs-only" in argv:
        print("docs_only=" + ("true" if docs_only(sys.stdin.read().split("\n")) else "false"))
        return 0
    if "--quick" in argv:
        result = quick_tests(sys.stdin.read().split("\n"))
        print("filter=" + quick_filter(result["classes"], result["global"]))
        print("broad=" + ("true" if result["broad"] else "false"))
        if "--summary" in argv:
            with open(argv[argv.index("--summary") + 1], "a", encoding="utf-8") as f:
                f.write(quick_summary(result))
        return 0
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
