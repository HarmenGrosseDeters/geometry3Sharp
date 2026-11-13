#!/usr/bin/env python3
"""Per-test-class coverage map and replay, the measurement step of docs/design/TEST_IMPACT_ANALYSIS.md.

collect: runs every test class of tests/geometry3Sharp.Tests on its own under coverlet (one `dotnet test` per class) and
         stores, per class, the lines it hit per library file plus the summed test duration (from the trx). Resumable:
         a class with a result file is skipped. `--shard I/N` splits the classes round robin (as tools/coverage/shard_filter.py).
replay:  for the last N first-parent merges of a base ref, computes which classes a selector would run and the share of the
         summed duration. Selection is file-level (a class that covers the file at all) and line-level (a class that hits
         a changed line; lines of the merge are translated to the map's commit, a line that cannot be translated falls
         back to its file). Forced-full rules and the always-run set of the design note are applied.

Usage:
    python3 tools/tia_map.py collect --out DIR [--shard I/N] [--only SUBSTR] [--list-file FILE]
    python3 tools/tia_map.py replay --map DIR --merges 50 [--from REF] [--json FILE] [--summary FILE]
"""

import argparse
import json
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import changed_line_coverage as clc
sys.path.insert(0, str(Path(__file__).resolve().parent / "coverage"))
import shard_filter as sf

PROJECT = "tests/geometry3Sharp.Tests"
BASE_FILTER = "Category!=Nightly"
# paths whose change can affect every test (design note, "Safety net": build-level files, exact code)
FORCE_FULL = (".csproj", ".props", ".targets", ".editorconfig", "BannedSymbols.txt", "global.json", "CompatibilitySuppressions.xml")
EXACT = ("math/Exact", "math/AdaptivePredicates.cs", "mesh_ops/ExactMesh", "core/ExactCodeAllowList.cs")
SHARD_A = re.compile(r"Mesh2|Meshing2|Terrain")  # the filter of the Windows shard a in ci.yml (full name contains)


def ranges(numbers):
    out = []
    for n in sorted(numbers):
        if out and n == out[-1][1] + 1:
            out[-1][1] = n
        else:
            out.append([n, n])
    return out


def in_ranges(rs, n):
    return any(a <= n <= b for a, b in rs)


def cobertura_hits(path, root):
    """{library file: set of lines with a hit} of one cobertura report (class-level line records, as the gate reads them)."""
    report = ET.parse(path).getroot()
    sources = [n.text for n in report.findall("./sources/source") if n.text]
    hits = {}
    for cls in report.iterfind("./packages/package/classes/class"):
        lines = [l for l in cls.findall("./lines/line") if int(l.get("hits")) > 0]
        if not lines:
            continue
        try:
            p = clc.coverage_path(cls.get("filename"), sources, root)
        except ValueError:
            continue
        if clc.in_scope(p):
            hits.setdefault(p, set()).update(int(l.get("number")) for l in lines)
    return hits


def trx_cost(path, cls):
    """(summed seconds, tests, failed) of the tests of class `cls` in a trx."""
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    total = failed = count = 0
    for r in ET.parse(path).getroot().iterfind(".//t:UnitTestResult", ns):
        if not r.get("testName", "").split("(")[0].startswith(cls.rsplit(".", 1)[-1] + ".") and cls not in r.get("testName", ""):
            continue
        h, m, s = r.get("duration", "0:0:0").split(":")
        total += int(h) * 3600 + int(m) * 60 + float(s)
        count += 1
        failed += r.get("outcome") == "Failed"
    return total, count, failed


def safe(name):
    return re.sub(r"[^A-Za-z0-9_.+-]", "_", name)


def collect(args):
    root = Path.cwd()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    listing = Path(args.list_file).read_text() if args.list_file else subprocess.check_output(
        ["dotnet", "test", PROJECT, "-c", "Release", "-f", "net10.0", "--no-build", "--list-tests", "--filter", BASE_FILTER], text=True)
    names = sf.classes(listing)
    if args.shard:
        i, n = map(int, args.shard.split("/"))
        names = names[i::n]
    if args.only:
        names = [c for c in names if args.only in c]
    for k, cls in enumerate(names):
        result = out / (safe(cls) + ".json")
        if result.exists():
            continue
        work = out / ("_work" + (args.shard or "").replace("/", "of"))
        subprocess.run(["rm", "-rf", str(work)])
        t0 = time.time()
        for attempt in range(3):  # a crashed test host (segfaults were seen with coverlet) leaves no tests or no lines: run again
            subprocess.run(["rm", "-rf", str(work)])
            proc = subprocess.run(
                ["dotnet", "test", PROJECT, "-c", "Release", "-f", "net10.0", "--no-build",
                 "--filter", f"({BASE_FILTER})&FullyQualifiedName~{cls}.", "--collect:XPlat Code Coverage",
                 "--settings", "tests/coverage.runsettings", "--logger", "trx;LogFileName=c.trx",
                 "--results-directory", str(work), "--blame-hang-timeout", "30m", "--blame-hang-dump-type", "none"],
                capture_output=True, text=True)
            reports = list(work.glob("*/coverage.cobertura.xml"))
            trx = work / "c.trx"
            if reports and trx.exists() and trx_cost(trx, cls)[1] > 0 and "Test Run Aborted" not in proc.stdout:
                break
        else:
            print(f"[{k + 1}/{len(names)}] {cls}: NO COMPLETE RUN (exit {proc.returncode})", flush=True)
            (out / (safe(cls) + ".missing")).write_text(proc.stdout[-2000:])
            continue
        hits = cobertura_hits(reports[0], root)
        seconds, tests, failed = trx_cost(trx, cls)
        result.write_text(json.dumps({
            "class": cls, "seconds": round(seconds, 3), "wall": round(time.time() - t0, 1), "tests": tests, "failed": failed,
            "files": {f: ranges(l) for f, l in sorted(hits.items())}}))
        print(f"[{k + 1}/{len(names)}] {cls}: {tests} tests, {seconds:.1f}s test time, {len(hits)} files, wall {time.time() - t0:.0f}s", flush=True)
    subprocess.run(["rm", "-rf", str(out / ("_work" + (args.shard or "").replace("/", "of")))])


def load_map(directory):
    return {d["class"]: d for d in (json.loads(p.read_text()) for p in sorted(Path(directory).glob("*.json")))}


def translate(root, base, head, path, lines):
    """Map new-side `lines` of `path` at `base` to their numbers at `head`; None for a line a hunk changed or removed.
    Returns None for the whole file when it is gone at head."""
    patch = clc.git(root, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", "--unified=0", base, head, "--", ":(literal)" + path)
    if not patch.strip():
        return {n: n for n in lines}
    if "deleted file mode" in patch:
        return None
    hunks = [(int(a), int(b) if b else 1, int(c), int(d) if d else 1)
             for a, b, c, d in re.findall(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", patch, re.M)]
    out = {}
    for n in lines:
        shift, ok = 0, True
        for os_, oc, ns_, nc in hunks:
            first, last = (os_, os_ + oc - 1) if oc else (os_ + 1, os_)  # old range, empty for pure insertions
            if oc and first <= n <= last:
                ok = False
                break
            if (os_ + oc - 1 if oc else os_) < n:
                shift += nc - oc
        out[n] = n + shift if ok else None
    return out


def declared_classes(root, rev, path, known):
    text = clc.git(root, "show", f"{rev}:{path}") if rev else Path(root, path).read_text(errors="replace")
    names = set(re.findall(r"\bclass\s+(\w+)", text))
    return [c for c in known if c.rsplit(".", 1)[-1].rsplit("+", 1)[-1] in names or c.rsplit("+", 1)[-1] in names]


def select(root, merge, head, cmap):
    """-> (reason or None, file-level class set, line-level class set) for one first-parent merge."""
    base = merge + "^1"
    paths = [p for p in clc.git(root, "diff", "--name-only", "--no-renames", base, merge).split("\n") if p]
    code = [p for p in paths if not p.endswith(".md") and not p.startswith(("docs/", ".github/", "tools/", "benchmarks/"))]
    if not code:
        return "no-code", set(), set()
    if any(p.endswith(FORCE_FULL) or p.startswith(EXACT) for p in code):
        return "force-full", set(cmap), set(cmap)
    tests = [p for p in code if p.startswith(PROJECT + "/") and p.endswith(".cs")]
    other = [p for p in code if p not in tests and not clc.in_scope(p)]
    if any(not p.startswith(PROJECT + "/") for p in other) and any(not p.startswith(("tests/g3cli", "tests/geometry3Sharp.Clipper2", "tests/mutation")) for p in other):
        return "force-full", set(cmap), set(cmap)
    always = set()
    for t in tests:
        try:
            always.update(declared_classes(root, merge, t, cmap))
        except subprocess.CalledProcessError:
            pass
    ancestor, changed = clc.changed_lines(root, base, merge)
    files, lines = set(always), set(always)
    for path, nums in changed.items():
        cover = [c for c, d in cmap.items() if path in d["files"]]
        files.update(cover)
        tr = translate(root, merge, head, path, nums)
        if tr is None or None in tr.values():
            lines.update(cover)  # cannot place a changed line in the map's tree: the whole file
            continue
        mapped = set(tr.values())
        executable = False
        for c in cover:
            rs = cmap[c]["files"][path]
            if any(in_ranges(rs, n) for n in mapped):
                lines.add(c)
        # a changed line no class hits may still be executable-but-unhit or a declaration: the design note selects all
        # classes of the file when the change has no hit line at all (no sequence point to attribute it to)
        if not any(c in lines for c in cover):
            lines.update(cover)
    return None, files, lines


def replay(args):
    root = Path.cwd()
    cmap = load_map(args.map)
    total = sum(d["seconds"] for d in cmap.values())
    shard_a = {c for c in cmap if SHARD_A.search(c)}
    head = clc.git(root, "rev-parse", "HEAD").strip()
    merges = clc.git(root, "log", "--first-parent", "--merges", "-n", str(args.merges), "--format=%H", args.start).split()
    rows = []
    for m in merges:
        reason, files, lines = select(root, m, head, cmap)
        def share(s, subset=None):
            sel = s if subset is None else s & subset
            den = total if subset is None else sum(cmap[c]["seconds"] for c in subset)
            return sum(cmap[c]["seconds"] for c in sel) / den if den else 0.0
        rows.append({"merge": m[:8], "reason": reason or "selected", "file_classes": len(files), "line_classes": len(lines),
                     "file_share": share(files), "line_share": share(lines),
                     "a_line": share(lines, shard_a), "b_line": share(lines, set(cmap) - shard_a)})
    code = [r for r in rows if r["reason"] != "no-code"]
    def mean(k, rs): return sum(r[k] for r in rs) / len(rs) if rs else 0.0
    def med(k, rs):
        v = sorted(r[k] for r in rs)
        return v[len(v) // 2] if v else 0.0
    forced = [r for r in code if r["reason"] == "force-full"]
    summary = {"classes": len(cmap), "total_test_seconds": round(total, 1), "merges": len(rows), "code_merges": len(code), "forced_full": len(forced),
               "shard_a_seconds": round(sum(cmap[c]["seconds"] for c in shard_a), 1),
               "line_share_mean_code": mean("line_share", code), "line_share_median_code": med("line_share", code),
               "file_share_mean_code": mean("file_share", code), "file_share_median_code": med("file_share", code),
               "line_share_mean_all": mean("line_share", rows), "a_line_mean_code": mean("a_line", code), "b_line_mean_code": mean("b_line", code),
               "failed_tests_in_map": sum(d["failed"] for d in cmap.values())}
    if args.json:
        Path(args.json).write_text(json.dumps({"summary": summary, "rows": rows}, indent=1))
    lines_out = ["| merge | reason | file-level classes | line-level classes | file share | line share |", "|---|---|---|---|---|---|"]
    lines_out += [f"| {r['merge']} | {r['reason']} | {r['file_classes']} | {r['line_classes']} | {r['file_share']:.1%} | {r['line_share']:.1%} |" for r in rows]
    text = "\n".join(lines_out) + "\n\n" + json.dumps(summary, indent=1)
    if args.summary:
        Path(args.summary).write_text(text + "\n")
    print(text)
    return 0


def main(argv):
    p = argparse.ArgumentParser()
    sub = p.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("--out", required=True)
    c.add_argument("--shard")
    c.add_argument("--only")
    c.add_argument("--list-file")
    r = sub.add_parser("replay")
    r.add_argument("--map", required=True)
    r.add_argument("--merges", type=int, default=50)
    r.add_argument("--from", dest="start", default="HEAD", help="where the first-parent walk starts (a pull request run: HEAD^1, the merge commit is not part of the history to replay)")
    r.add_argument("--json")
    r.add_argument("--summary")
    args = p.parse_args(argv)
    if args.cmd == "collect":
        collect(args)
        return 0
    return replay(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
