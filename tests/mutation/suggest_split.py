#!/usr/bin/env python3
"""Suggests where to split a mutated file into shards (tests/mutation/README.md, "Splits"): a manual tool, nothing here
runs in CI. The offsets stay committed in the stryker-mesh-*.json configs, which is what keeps the incremental runs
(incremental.py) stable.

  python3 suggest_split.py <report> <repo-relative file> [--parts N | --target-minutes M] [--concurrency 2]
                           [--b 10] [--timeout-ratio 1.5] [--additional-timeout 10] [--overhead-minutes 60]
                           [--source <current file>]

<report> is the JSON report of a FULL run of the shard(s) of that file: the cached baseline (stryker-report.json) or
the mutation-report.json of the artifact. The tool weighs every mutant by what it cost, from its status:
  Timeout      timeout-ratio * b + additional-timeout   (the run waits for the whole timeout)
  Survived     b                                        (all covering tests run to the end)
  Killed       0.3 * b                                  (the run stops at the first failing test)
  NoCoverage   0                                        (never tested)
with b, the seconds of the tests that cover a mutant, 10 by default (measured shards: b between 8 and 20 s; Stryker's
timeout is 1.5 b + additional-timeout, README "Timeouts"). It then proposes offsets at the start of a method (the
first attribute or comment line above it) so that the longest shard is as short as possible, for N parts or the fewest parts
whose estimate (testing time / concurrency + the overhead of build and coverage capture, 60 minutes by default) stays
within the target. Each shard runs `testing / concurrency` plus the overhead: a part that is too small only repeats the
overhead (the coverage capture of arrangement-2/3/4 costs 56-71 minutes each).

The estimate is a model, not a measurement: use it to choose offsets, then look at the real run time of each part and
move an offset. After a split: put the method into SplitMethod of MutationConfigTests, and give every part the same
test filter and break threshold (README). A mutant that reaches over a split point (a block around the method start) is
left out of both parts: at a method start that is only the class's own block.

The offsets are character offsets of the source text the report holds; with --source the tool checks that this file
is the same text (else the report is from another version of the file and the offsets would not fit).
"""
import argparse
import json
import re
import sys

import incremental

KEYWORDS = {"if", "for", "foreach", "while", "switch", "return", "new", "else", "using", "lock", "catch", "throw",
            "case", "do", "try", "fixed", "yield", "await", "typeof", "sizeof", "default", "checked", "unchecked",
            "when", "nameof", "var", "in", "is", "as", "out", "ref", "params", "this", "base", "goto", "break", "continue"}
MODIFIERS = r"(?:(?:public|private|internal|protected|static|unsafe|override|virtual|abstract|sealed|extern|partial|async|readonly|new)\s+)*"
# a member signature: modifiers, a return type, a name, a parameter list that opens here
_METHOD = re.compile(r"^\s*" + MODIFIERS + r"(?P<type>[A-Za-z_][\w<>,.?\[\]() ]*?)\s+(?P<name>[A-Za-z_]\w*)\s*(?:<[^>()]*>)?\(")
_CLASS = re.compile(r"\b(class|struct|interface)\s+\w+")


def weights(b=10.0, ratio=1.5, extra=10.0):
    """seconds a mutant costs, by status"""
    return {"Timeout": ratio * b + extra, "Survived": b, "Killed": 0.3 * b, "NoCoverage": 0.0}


def class_body_lines(lines):
    """for every line: True if the innermost brace open at its start is the body of a class, struct or interface (so a
    declaration there is a member, not a statement or local function of a body). Strings, chars and comments are skipped
    (an approximation: verbatim and raw strings with braces are not handled)."""
    stack, flags, pending, in_block = [], [], False, False
    for line in lines:
        flags.append(bool(stack) and stack[-1] == "class")
        if _CLASS.search(re.sub(r'"(?:[^"\\]|\\.)*"', '""', line.split("//")[0])):
            pending = True
        i, n = 0, len(line)
        while i < n:
            c = line[i]
            if in_block:
                if line.startswith("*/", i):
                    in_block, i = False, i + 1
            elif line.startswith("//", i):
                break
            elif line.startswith("/*", i):
                in_block, i = True, i + 1
            elif c == '"':
                i += 1
                while i < n and line[i] != '"':
                    i += 2 if line[i] == "\\" else 1
            elif c == "'":
                i += 1
                while i < n and line[i] != "'":
                    i += 2 if line[i] == "\\" else 1
            elif c == "{":
                stack.append("class" if pending else "other")
                pending = False
            elif c == "}" and stack:
                stack.pop()
            i += 1
    return flags


def method_starts(text):
    """[(offset, signature line, [acceptable offsets])]: every method (or constructor; properties and fields are not)
    declared directly in a class body. The offset is the first line of the attributes and comments directly above it, as
    MutationConfigTests reads a split offset (it skips `[` and `//` lines); the third item lists the line starts from
    there to the signature, which that test accepts as well."""
    lines = text.split("\n")
    starts, pos = [], 0
    for ln in lines:
        starts.append(pos)
        pos += len(ln) + 1
    in_class = class_body_lines(lines)
    out = []
    for i, line in enumerate(lines):
        s = line.strip()
        if not in_class[i] or not s or s.startswith(("[", "//", "/*", "*", "{", "}")):
            continue
        if s.endswith(";") and not re.search(r"\)\s*=>", s):
            continue                                    # a statement-like line, a field (an expression-bodied member has `) =>`)
        m = _METHOD.match(line)
        if not m or m.group("name") in KEYWORDS or m.group("type").split()[-1] in KEYWORDS:
            continue
        k = i
        while k > 0 and lines[k - 1].strip().startswith(("[", "//")):
            k -= 1                                      # the attributes and comments belong to the method
        out.append((starts[k], s, [starts[j] for j in range(k, i + 1)]))
    return out


def mutant_offset(source_starts, mutant):
    """character offset of the start of a mutant (Stryker: 1-based line and column)"""
    loc = mutant["location"]["start"]
    return source_starts[loc["line"] - 1] + loc["column"] - 1


def partition(costs, parts):
    """cut points (indices into costs, ascending, parts - 1 of them) so that the largest sum of a part is smallest"""
    def cuts_for(limit):
        cuts, acc = [], 0.0
        for i, c in enumerate(costs):
            if acc + c > limit and acc > 0 and len(cuts) < parts - 1:
                cuts.append(i)
                acc = 0.0
            acc += c
        return cuts, acc

    def fits(limit):
        cuts, last = cuts_for(limit)
        if last > limit:
            return False
        sums, acc = [], 0.0
        for i, c in enumerate(costs):
            if i in cuts:
                sums.append(acc)
                acc = 0.0
            acc += c
        sums.append(acc)
        return max(sums) <= limit + 1e-9
    lo, hi = 0.0, sum(costs)
    for _ in range(80):
        mid = (lo + hi) / 2
        if fits(mid):
            hi = mid
        else:
            lo = mid
    return cuts_for(hi)[0]


def suggest(report, path, parts=None, target=None, concurrency=2, b=10.0, ratio=1.5, extra=10.0, overhead=60.0, source=None):
    """{'parts': [(start, end, first line of the method at start, mutants, minutes)], 'offsets': [...]}"""
    bf = incremental.baseline_file(report, path)
    if bf is None or "source" not in bf:
        raise ValueError("%s is not in the report" % path)
    text = bf["source"]
    if "\r" in text:
        raise ValueError("the source has \\r line breaks: the offsets would differ from Stryker's (it reads the text as is)")
    if source is not None and source != text:
        raise ValueError("--source differs from the source in the report: take the report of a full run on this version")
    line_start, pos = [], 0
    for ln in text.split("\n"):
        line_start.append(pos)
        pos += len(ln) + 1
    w = weights(b, ratio, extra)
    cand = method_starts(text)
    cand = [(o, s) for o, s, _ in cand if 0 < o < len(text)]
    offs = [0] + [o for o, _ in cand] + [len(text)]
    cost = [0.0] * (len(offs) - 1)
    count = [0] * (len(offs) - 1)
    for m in bf.get("mutants", []):
        c = w.get(m.get("status"))
        if c is None:
            continue
        o = mutant_offset(line_start, m)
        k = max(i for i in range(len(offs) - 1) if offs[i] <= o)
        cost[k] += c
        count[k] += 1
    total_minutes = sum(cost) / concurrency / 60.0

    def build(n):
        cuts = partition(cost, n) if n > 1 else []
        bounds = [0] + cuts + [len(cost)]
        rows = []
        for a, z in zip(bounds, bounds[1:]):
            start, end = offs[a], offs[z]
            first = "the start of the file" if a == 0 else next(s for o, s in cand if o == start)
            rows.append((start, end, first, sum(count[a:z]), overhead + sum(cost[a:z]) / concurrency / 60.0))
        return rows
    if parts is None:
        if target is None:
            raise ValueError("--parts or --target-minutes")
        parts = 1
        while parts < len(cost) and max(r[4] for r in build(parts)) > target:
            parts += 1
    return {"parts": build(parts), "testing_minutes": total_minutes, "candidates": len(cand)}


def main(argv):
    ap = argparse.ArgumentParser(description="Suggests offsets to split a mutated file into shards (see the module doc).")
    ap.add_argument("report")
    ap.add_argument("file")
    ap.add_argument("--parts", type=int)
    ap.add_argument("--target-minutes", type=float)
    ap.add_argument("--concurrency", type=int, default=2)
    ap.add_argument("--b", type=float, default=10.0)
    ap.add_argument("--timeout-ratio", type=float, default=1.5)
    ap.add_argument("--additional-timeout", type=float, default=10.0)
    ap.add_argument("--overhead-minutes", type=float, default=60.0)
    ap.add_argument("--source")
    a = ap.parse_args(argv)
    if (a.parts is None) == (a.target_minutes is None):
        ap.error("exactly one of --parts and --target-minutes")
    with open(a.report, encoding="utf-8-sig") as f:
        report = json.load(f)
    source = None
    if a.source:
        with open(a.source, encoding="utf-8-sig", newline="") as f:
            source = f.read()
    try:
        r = suggest(report, a.file, a.parts, a.target_minutes, a.concurrency, a.b, a.timeout_ratio, a.additional_timeout,
                    a.overhead_minutes, source)
    except ValueError as e:
        print("error: %s" % e, file=sys.stderr)
        return 2
    print("%s: %d method starts to choose from; testing time of the whole file about %.0f minutes at concurrency %d "
          "(b = %g s)" % (a.file, r["candidates"], r["testing_minutes"], a.concurrency, a.b))
    print("%-4s %-8s %-8s %-8s %-8s %s" % ("part", "start", "end", "mutants", "minutes", "starts at"))
    for i, (s, e, first, n, minutes) in enumerate(r["parts"], 1):
        print("%-4d %-8d %-8d %-8d %-8.0f %s" % (i, s, e, n, minutes, first))
    print("\nmutate entries (the last part runs to the end of the file):")
    for i, (s, e, _, _, _) in enumerate(r["parts"]):
        print('  "**/%s{%d..%d}"' % (a.file, s, 1000000 if i == len(r["parts"]) - 1 else e))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
