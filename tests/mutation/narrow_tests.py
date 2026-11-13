#!/usr/bin/env python3
"""Smaller test sets per mutation shard (tests/mutation/README.md, "Narrowed test sets").

A full shard run puts into each mutant's `killedBy` the tests that killed it. A mutant that a test of a smaller set
still kills keeps its kill when the other tests are not run, so a shard needs only a set of tests that contains one
recorded killer of every killed or timed-out mutant. Its coverage capture and its survivor / timeout runs then run
that set instead of the whole `test-case-filter`.

  python3 narrow_tests.py make  --report R --config C [--durations TRX] [--min-saving 0.2] [--timeouts all|one] -o OUT
  python3 narrow_tests.py apply --config C --filter F --repo ROOT -o RUN_CONFIG

`make` reads the JSON report of a FULL-FILTER full run of the shard and writes the filter file F: the kept tests (greedy
set cover; a test that is the only recorded killer of a mutant is always kept; with --durations, the .trx of a full
test run, the cover prefers cheap tests), plus what the filter was made from (below). A Killed mutant needs one of its
recorded killers. A Timeout mutant has NO recorded killer in Stryker's report (killedBy is empty for all of them), only the
tests that covered it, and which of them hung is unknown: `--timeouts all` (default) keeps every covering test of a timed-out
mutant, which is the only choice that cannot lose its timeout (and saves little on a shard with many timeouts);
`--timeouts one` keeps one covering test per such mutant, which usually still hangs but is not guaranteed to. It writes nothing when the
estimated saving of the mutant runs is below --min-saving (default 20 %): such a shard keeps its full filter.
The saving is estimated as in the cost model of suggest_split.py: the sum over the tested mutants of the duration of
the tests covering them (timeouts times 1.5), against the same sum over the kept tests only.

`apply` writes RUN_CONFIG, a copy of the config C. If the filter file F is current, `test-case-filter` of the copy is
the exact list of the kept tests; else the copy is the config unchanged and the reason is printed ("full: ...").
Current means, all of it:
  - the config (without `thresholds`, `reporters`, `concurrency`) is the one the filter was made for;
  - every mutated file and every test file that holds a test of the report has the content it had in the report
    (line breaks normalised): a new mutant or a changed test can be killed by a test that was not kept, which the
    filter cannot know;
  - no test file that is not in the report mentions a class name of the config's filter: a new test class.
The test projects are read from the config. Stale, missing or unreadable means "full": the guard never loses kills,
it only gives the saving up. A narrowed run's report must not be used to make a filter (its killedBy only names kept
tests, so a filter made from it could never grow): make from the report of the full filter that `apply` fell back to.

What this does not cover: a change in code the mutated files call (not mutated, so not in the guard), in a test helper
or data file without a test of its own, or in a test file linked from outside the test project, can make a kept test
stop killing a mutant that a dropped test would still kill. The unfiltered full run on master (the weekly run, or
`G3_FULL_TESTS=1`) is the check of record.
"""
import argparse
import collections
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

FORMAT = 1
TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
TIMEOUT_FACTOR = 1.5
TESTED = ("Killed", "Timeout", "Survived")
KILLS = ("Killed", "Timeout")
NOT_IN_CONFIG_HASH = ("thresholds", "reporters", "concurrency")
MIN_FLOOR = 0.01  # seconds a test without a recorded duration is assumed to take (keeps the ratio finite)


def digest(text):
    return hashlib.sha256(text.replace("\r\n", "\n").encode("utf-8")).hexdigest()


def digest_json(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode("utf-8")).hexdigest()


def read_text(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        return f.read()


def rel(path, root):
    return os.path.relpath(path, root).replace("\\", "/")


def durations_of(trx):
    """seconds per test method ("Class.Method", rows of a theory added) from a .trx"""
    out = collections.defaultdict(float)
    for r in ET.parse(trx).getroot().iter(TRX_NS + "UnitTestResult"):
        h, m, s = r.get("duration").split(":")
        out[r.get("testName").split("(")[0]] += int(h) * 3600 + int(m) * 60 + float(s)
    return out


def config_hash(config):
    """what decides the set of mutants and tests of a shard: the whole config but the reporting keys"""
    return digest_json({k: v for k, v in config.items() if k not in NOT_IN_CONFIG_HASH})


def method_of(name):
    """the filterable name of a test: no arguments of a theory row (vstest's FullyQualifiedName has none)"""
    return name.split("(")[0]


def vstest_escape(text):
    """vstest filter syntax: these characters are operators and need a backslash"""
    return re.sub(r"([\\()&|=!~])", r"\\\1", text)


def filter_terms(test_case_filter):
    """class names of a vstest filter: `FullyQualifiedName~A.B|FullyQualifiedName=C` -> {"A", "C"} (the part before the
    first dot of each name; `Category` and other properties are not names)"""
    terms = set()
    for m in re.finditer(r"FullyQualifiedName\s*[~=]\s*([A-Za-z0-9_.+]+)", test_case_filter or ""):
        terms.add(m.group(1).split(".")[0])
    return terms


def kept_tests(reports, cost, name=str, timeouts="all"):
    """Greedy cover. reports: list of mutants (dicts); cost: test id -> seconds. Returns the set of kept test ids; ties
    go to the test with the smallest name (ids are not stable). A Killed mutant needs one recorded killer. A Timeout
    mutant needs its killedBy if it has one, else (always, in Stryker's report) all its covering tests (timeouts "all") or
    one of them (timeouts "one")."""
    need, kept = [], set()
    for m in reports:
        if m["status"] not in KILLS:
            continue
        by = m.get("killedBy") or ([] if m["status"] == "Killed" else m.get("coveredBy", []))
        if m["status"] == "Timeout" and not m.get("killedBy") and timeouts == "all":
            kept |= set(by)
        elif by:
            need.append(frozenset(by))
    for n in need:
        if len(n) == 1:
            kept |= n
    left = [n for n in need if not n & kept]
    while left:
        count = collections.Counter(t for n in left for t in n)
        best = max(sorted(count, key=name), key=lambda t: count[t] / (cost(t) + MIN_FLOOR))
        kept.add(best)
        left = [n for n in left if best not in n]
    return kept


def mutant_cost(mutants, tests, cost):
    """sum over the tested mutants of the cost of the tests covering them (timeouts times 1.5); tests: ids to count"""
    total = 0.0
    for m in mutants:
        if m["status"] not in TESTED:
            continue
        w = TIMEOUT_FACTOR if m["status"] == "Timeout" else 1.0
        total += w * sum(cost(t) for t in m.get("coveredBy", []) if tests is None or t in tests)
    return total


def make(report_path, config_path, trx=None, min_saving=0.2, timeouts="all"):
    """-> (filter dict or None, message)"""
    report = json.load(open(report_path, encoding="utf-8"))
    config = json.load(open(config_path, encoding="utf-8"))["stryker-config"]
    root = report["projectRoot"]
    names = {}
    test_files = {}
    for path, tf in report.get("testFiles", {}).items():
        if tf.get("tests"):
            if rel(path, root).startswith(".."):
                return None, "test file outside the project root: %s" % path
            test_files[rel(path, root)] = digest(tf["source"])
            for t in tf["tests"]:
                names[t["id"]] = t["name"]
    mutants, mutated = [], {}
    for path, fi in report["files"].items():
        live = [m for m in fi["mutants"] if m["status"] in TESTED + ("NoCoverage",)]
        if live:
            if rel(path, root).startswith(".."):
                return None, "mutated file outside the project root: %s" % path
            mutated[rel(path, root)] = digest(fi["source"])
            mutants += live
    if not any(m["status"] in KILLS for m in mutants):
        return None, "no killed mutant in the report: nothing to base a filter on"
    dur = durations_of(trx) if trx else {}
    cost = (lambda t: dur.get(names.get(t, ""), 0.0)) if trx else (lambda t: 1.0)
    kept = kept_tests(mutants, cost, lambda t: names.get(t, t), timeouts)
    unknown = [t for t in kept if t not in names]
    if unknown:
        return None, "killer ids not in the report's test list: %s" % unknown[:3]
    all_cost, kept_cost = mutant_cost(mutants, None, cost), mutant_cost(mutants, kept, cost)
    saving = 1 - kept_cost / all_cost if all_cost else 0.0
    if saving < min_saving:
        return None, "estimated saving %.0f %% is below %.0f %%: full filter" % (saving * 100, min_saving * 100)
    return {
        "format": FORMAT,
        "config": config_hash(config),
        "timeouts": timeouts,
        "terms": sorted(filter_terms(config.get("test-case-filter", ""))),
        "kept": sorted({method_of(names[t]) for t in kept}),
        "tests_in_report": len(names),
        "mutated_files": dict(sorted(mutated.items())),
        "test_files": dict(sorted(test_files.items())),
        "estimated_saving": round(saving, 3),
        "weighted_by_duration": bool(trx),
    }, "kept %d of %d tests, estimated saving of the mutant runs %.0f %%" % (len(kept), len(names), saving * 100)


def stale_reason(flt, config, repo, config_dir):
    """None if the filter file is current for the config, else why not"""
    if flt.get("format") != FORMAT:
        return "filter file format %r, expected %d" % (flt.get("format"), FORMAT)
    if not flt.get("kept"):
        return "the filter keeps no test"
    if flt.get("config") != config_hash(config):
        return "the config changed"
    for group in ("mutated_files", "test_files"):
        for path, h in flt.get(group, {}).items():
            full = os.path.join(repo, path)
            try:
                now = digest(read_text(full))
            except OSError:
                return "%s is gone: %s" % (group[:-1].replace("_", " "), path)
            if now != h:
                return "%s changed: %s" % (group[:-1].replace("_", " "), path)
    terms = flt.get("terms", [])
    known = set(flt.get("test_files", {}))
    for project in config.get("test-projects", []):
        pdir = os.path.dirname(os.path.normpath(os.path.join(config_dir, project)))
        for dirpath, dirs, files in os.walk(pdir):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
            for fn in files:
                if not fn.endswith(".cs"):
                    continue
                path = rel(os.path.join(dirpath, fn), repo)
                if path in known:
                    continue
                try:
                    text = read_text(os.path.join(dirpath, fn))
                except OSError:
                    return "unreadable test file: %s" % path
                if any(t in text for t in terms):
                    return "new test file mentions the filter's classes: %s" % path
    return None


def apply(config_path, filter_path, repo, out_path):
    """-> message; out_path always gets a runnable config (the config unchanged unless the filter is current)"""
    raw = json.load(open(config_path, encoding="utf-8"))
    config = raw["stryker-config"]
    reason = None
    try:
        flt = json.load(open(filter_path, encoding="utf-8"))
        reason = stale_reason(flt, config, repo, os.path.dirname(os.path.abspath(config_path)))
        if reason is None:
            names = ["FullyQualifiedName=" + vstest_escape(n) for n in flt["kept"]]
            filter_text = "|".join(names)
            count = "%d of %d" % (len(flt["kept"]), flt.get("tests_in_report", 0))
    except FileNotFoundError:
        reason = "no filter file"
    except Exception as e:  # a filter file of the wrong shape must not end the run
        reason = "no usable filter file (%s)" % e.__class__.__name__
    if reason is None:
        config["test-case-filter"] = filter_text
        msg = "narrowed: %s tests" % count
    else:
        msg = "full: " + reason
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(raw, f, indent=2)
        f.write("\n")
    return msg


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    mk = sub.add_parser("make")
    mk.add_argument("--report", required=True)
    mk.add_argument("--config", required=True)
    mk.add_argument("--durations")
    mk.add_argument("--min-saving", type=float, default=0.2)
    mk.add_argument("--timeouts", choices=("all", "one"), default="all")
    mk.add_argument("-o", required=True)
    ap_ = sub.add_parser("apply")
    ap_.add_argument("--config", required=True)
    ap_.add_argument("--filter", required=True)
    ap_.add_argument("--repo", required=True)
    ap_.add_argument("-o", required=True)
    a = ap.parse_args(argv)
    if a.cmd == "make":
        flt, msg = make(a.report, a.config, a.durations, a.min_saving, a.timeouts)
        print(msg)
        if flt is not None:
            with open(a.o, "w", encoding="utf-8") as f:
                json.dump(flt, f, indent=1)
                f.write("\n")
        return 0
    print(apply(a.config, a.filter, a.repo, a.o))
    return 0


if __name__ == "__main__":
    sys.exit(main())
