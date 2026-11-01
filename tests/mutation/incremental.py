#!/usr/bin/env python3
"""Incremental mutation runs for source changes (.github/workflows/mutation.yml, README.md "Baseline").

A PR that changes a mutated file runs Stryker only on the changed lines, and the score of the whole shard is the
run's results for the changed code plus the baseline's results (the report of the last full run of the config) for
the unchanged code:

  plan <config> <baseline report> <run config>
      compares each mutated file with its source in the baseline report (line diff) and writes <run config>: the
      config with `mutate` limited to the changed lines (character spans, within the config's own span), and with
      `since` / `baseline` removed. Writes to GITHUB_OUTPUT: plan=run (mutants to test), plan=none (no changed line
      in the shard's spans: no Stryker run needed) or plan=full (no usable baseline: run the whole config).
  merge <config> <baseline report> <run report or "-"> [<summary file>]
      the results of the shard: the run's mutants, and the baseline's mutants that touch no changed line. Prints the
      counts and the score and exits 1 when the score is below the config's break threshold.

Stryker's own `--with-baseline` (5.0, experimental) is not used: it looks the baseline up by relative path while its
report stores absolute paths, and it matches mutants by source text and mutator name, which is ambiguous for about
half of the mutants (`a > b` gives two "Equality mutation"s), so those run again anyway. Here the mutants are matched
by position, through the line diff.

Limits: a mutant whose span reaches beyond the changed lines (a statement or block around a changed line) is neither
run (Stryker mutates only mutants inside the spans) nor taken from the baseline (its code changed): it is left out
until the next full run. A baseline result is reused even if a test or another file changed in the same PR; the weekly
full run is the check of record.
"""
import difflib
import json
import os
import re
import sys

COUNTED = {"Killed": "detected", "Timeout": "detected", "Survived": "undetected", "NoCoverage": "undetected"}
_SPAN = re.compile(r"^(?P<glob>.*?)(\{(?P<a>\d+)\.\.(?P<b>\d+)\})?$")


# ---- pure helpers (tested in test_incremental.py) ----

def parse_mutate(entry):
    """'**/mesh_ops/X.cs{0..52017}' -> ('mesh_ops/X.cs', (0, 52017)); without a span the span is None"""
    m = _SPAN.match(entry)
    glob = m.group("glob")
    path = glob[3:] if glob.startswith("**/") else glob
    span = (int(m.group("a")), int(m.group("b"))) if m.group("a") is not None else None
    return path, span


def line_starts(text):
    """character offset of the start of each line, and the length of the text as the last entry"""
    starts = [0]
    for i, ch in enumerate(text):
        if ch == "\n":
            starts.append(i + 1)
    if starts[-1] != len(text):
        starts.append(len(text))
    return starts


def changes(old_text, new_text):
    """(changed line ranges of the new text, changed line ranges of the old text), 0-based half-open [a, b). A pure
    insertion is an empty range in the old text, a pure deletion an empty range in the new text."""
    old, new = old_text.split("\n"), new_text.split("\n")
    sm = difflib.SequenceMatcher(None, old, new, autojunk=False)
    new_ranges, old_ranges = [], []
    for tag, i1, i2, j1, j2 in sm.get_opcodes():
        if tag != "equal":
            old_ranges.append((i1, i2))
            new_ranges.append((j1, j2))
    return new_ranges, old_ranges


def spans_of(new_text, new_ranges, limit=None):
    """character spans [a, b) of the changed new lines, merged, within limit (a, b) if given. A pure deletion (empty
    new range) gives the line around it, so mutants of the code that closes the gap run."""
    starts = line_starts(new_text)
    nlines = len(new_text.split("\n"))
    spans = []
    for a, b in new_ranges:
        if a == b:                      # deletion: the lines before and after it
            a, b = max(0, a - 1), min(nlines, b + 1)
        if a >= b:
            continue
        s = starts[a] if a < len(starts) else len(new_text)
        e = starts[b] if b < len(starts) else len(new_text)
        if limit is not None:
            s, e = max(s, limit[0]), min(e, limit[1])
        if s < e:
            if spans and s <= spans[-1][1]:
                spans[-1] = (spans[-1][0], max(spans[-1][1], e))
            else:
                spans.append((s, e))
    return spans


def touches(mutant, old_ranges):
    """whether a baseline mutant (1-based lines in its location) touches a changed range of the old text"""
    sl, el = mutant["location"]["start"]["line"] - 1, mutant["location"]["end"]["line"] - 1
    for a, b in old_ranges:
        if a < b:
            if sl < b and el >= a:
                return True
        else:                           # insertion before old line a: a mutant spanning lines a-1 and a encloses it
            if sl <= a - 1 and el >= a:
                return True
    return False


def baseline_file(report, path):
    """the file entry of the baseline report for a repository-relative path (keys may be absolute or relative)"""
    for key, f in report.get("files", {}).items():
        k = key.replace("\\", "/")
        # (relative to the project directory, which is the repository root here, or absolute)
        if k == path or k.endswith("/" + path) or path.endswith("/" + k):
            return f
    return None


def counted(mutants):
    """(detected, undetected) of the mutants that count for the score"""
    d = sum(1 for m in mutants if COUNTED.get(m.get("status")) == "detected")
    u = sum(1 for m in mutants if COUNTED.get(m.get("status")) == "undetected")
    return d, u


def score(detected, undetected):
    """Stryker's mutation score in percent (None without counted mutants)"""
    total = detected + undetected
    return 100.0 * detected / total if total else None


# ---- plan and merge ----

def read_text(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read()


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def plan(config, baseline, repo_root):
    """(run mutate entries, per file: (path, new ranges, old ranges)) or None when the baseline cannot be used"""
    entries = [parse_mutate(e) for e in config["mutate"]]
    mutate, files = [], []
    for path, span in entries:
        bf = baseline_file(baseline, path)
        full = os.path.join(repo_root, path)
        if bf is None or "source" not in bf or not os.path.isfile(full):
            return None
        new_text = read_text(full).replace("\r\n", "\n")
        new_ranges, old_ranges = changes(bf["source"].replace("\r\n", "\n"), new_text)
        files.append((path, new_ranges, old_ranges))
        for s, e in spans_of(new_text, new_ranges, span):
            mutate.append("**/%s{%d..%d}" % (path, s, e))
    return mutate, files


def merge(config, baseline, run_report, repo_root):
    """counts of the shard: {'run': (d, u), 'baseline': (d, u), 'replaced': baseline mutants on changed code}"""
    entries = [parse_mutate(e) for e in config["mutate"]]
    run_mutants, kept, left_out = [], [], 0
    for path, span in entries:
        bf = baseline_file(baseline, path)
        new_text = read_text(os.path.join(repo_root, path)).replace("\r\n", "\n")
        new_ranges, old_ranges = changes(bf["source"].replace("\r\n", "\n"), new_text)
        for m in bf.get("mutants", []):
            if m.get("status") not in COUNTED:
                continue
            if touches(m, old_ranges):
                left_out += 1
            else:
                kept.append(m)
        if run_report is not None:
            rf = baseline_file(run_report, path)
            if rf is not None:
                run_mutants += [m for m in rf.get("mutants", []) if m.get("status") in COUNTED]
    return {"run": counted(run_mutants), "baseline": counted(kept), "replaced": left_out}


def output(key, value):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            f.write("%s=%s\n" % (key, value))
    print("%s=%s" % (key, value))


def main(argv):
    repo_root = os.environ.get("REPO_ROOT") or os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    if len(argv) == 4 and argv[0] == "plan":
        doc = load_json(argv[1])
        config = doc["stryker-config"]
        baseline = load_json(argv[2]) if os.path.isfile(argv[2]) else None
        result = plan(config, baseline, repo_root) if baseline else None
        if result is None:
            print("no usable baseline for %s: the whole config runs" % argv[1])
            output("plan", "full")
            return 0
        mutate, files = result
        for path, new_ranges, _ in files:
            print("%s: changed lines %s" % (path, ", ".join("%d-%d" % (a + 1, b) for a, b in new_ranges) or "none"))
        if not mutate:
            print("no changed line in the spans of %s: nothing to run" % argv[1])
            output("plan", "none")
            return 0
        run = dict(config, mutate=mutate)
        run.pop("since", None)
        run.pop("baseline", None)
        with open(argv[3], "w", encoding="utf-8") as f:
            json.dump({"stryker-config": run}, f, indent=2)
        print("mutate: " + ", ".join(mutate))
        output("plan", "run")
        return 0
    if len(argv) in (4, 5) and argv[0] == "merge":
        config = load_json(argv[1])["stryker-config"]
        baseline = load_json(argv[2])
        run_report = load_json(argv[3]) if argv[3] != "-" else None
        c = merge(config, baseline, run_report, repo_root)
        d = c["run"][0] + c["baseline"][0]
        u = c["run"][1] + c["baseline"][1]
        s = score(d, u)
        brk = config.get("thresholds", {}).get("break", 0)
        lines = ["### Incremental mutation run (%s)" % os.path.basename(argv[1]), "",
                 "| | detected (killed, timeout) | undetected (survived, no coverage) |", "|---|---|---|",
                 "| tested in this run (changed code) | %d | %d |" % c["run"],
                 "| from the baseline (unchanged code) | %d | %d |" % c["baseline"], "",
                 "Baseline mutants on changed lines, not reused: %d (the run tests the mutants inside the changed lines; "
                 "a mutant that reaches beyond them is left out until the next full run)." % c["replaced"], "",
                 "Score of the shard: %s (break %s)." % ("%.2f %%" % s if s is not None else "n/a", brk)]
        text = "\n".join(lines) + "\n"
        print(text)
        if len(argv) == 5:
            with open(argv[4], "a", encoding="utf-8") as f:
                f.write(text)
        if s is not None and s < brk:
            print("::error::mutation score %.2f %% is below the break threshold %s" % (s, brk))
            return 1
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
