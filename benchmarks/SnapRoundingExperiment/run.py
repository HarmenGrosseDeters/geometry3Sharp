#!/usr/bin/env python3
"""Runs the snap-rounding experiment: every case of a set (or the mesh files given) and method in its own process with a
time limit, appending one JSON line per result to the output file; cases already there are skipped (resumable).

  python3 run.py <out.jsonl> <set or @file-list> [--methods plain,safe,again,B,Bpaper] [--timeout 300] [-j 2] [--left]

With --left, only the cases whose "safe" result (already in out.jsonl) is not clean: again and B change nothing
where SafeRounding's result is clean, so a run is plain,safe first and the other methods with --left.

A set is one of the tool's (clusters, utm, cylinder, booleans); @list is a file with one mesh path per line (each case
is the self-union of the mesh); @bc:list a file with one BooleanComparison case directory and operation per line
(<dir>:<Union|Intersection|Difference>). Summarize with summarize.py.
"""
import argparse, json, os, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(HERE, "bin", "Release", "net10.0", "SnapRoundingExperiment.dll")


def cases(spec):
    if spec.startswith("@bc:"):
        with open(spec[4:]) as f:
            return ["bc:" + line.strip() for line in f if line.strip()]
    if spec.startswith("@"):
        with open(spec[1:]) as f:
            return ["file:" + line.strip() for line in f if line.strip()]
    out = subprocess.run(["dotnet", TOOL, "list", spec], capture_output=True, text=True, check=True).stdout
    return [line for line in out.splitlines() if line]


def run_case(case, methods, timeout):
    """all methods of a case in one process; after a timeout or a crash, each method alone"""
    try:
        p = subprocess.run(["dotnet", TOOL, "run", case] + methods, capture_output=True, text=True, timeout=timeout * len(methods))
        rows = [json.loads(l) for l in p.stdout.splitlines() if l.startswith("{")]
        if len(rows) == len(methods):
            return rows
    except subprocess.TimeoutExpired:
        pass
    if len(methods) == 1:
        return [{"case": case, "method": methods[0], "ok": False, "error": "timeout or crash (%d s)" % timeout}]
    return [r for m in methods for r in run_case(case, [m], timeout)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("spec")
    ap.add_argument("--methods", default="plain,safe,again,B")
    ap.add_argument("--timeout", type=int, default=300)
    ap.add_argument("-j", type=int, default=2)
    ap.add_argument("--left", action="store_true", help="only the cases whose 'safe' result in out is not clean")
    ap.add_argument("--rounding-left", action="store_true",
                    help="only the cases whose 'safe' result in out is not rounding clean (degenerate triangles or "
                         "intersections left by SafeRounding; not the defects of open inputs)")
    args = ap.parse_args()
    done = set()
    if os.path.exists(args.out):
        with open(args.out) as f:
            for line in f:
                try:
                    r = json.loads(line)
                    done.add((r["case"], r["method"]))
                except (ValueError, KeyError):
                    pass
    left = None
    if args.left or args.rounding_left:
        key = "rounding_clean" if args.rounding_left else "clean"
        with open(args.out) as f:
            left = set(r["case"] for r in map(json.loads, f) if r.get("method") == "safe" and r.get("ok") and not r.get(key))
    todo = []
    for c in cases(args.spec):
        if left is not None and c not in left:
            continue
        ms = [m for m in args.methods.split(",") if (c, m) not in done]
        if ms:
            todo.append((c, ms))
    print("%d cases to run, %d results done" % (len(todo), len(done)), file=sys.stderr)
    with open(args.out, "a") as out, ThreadPoolExecutor(args.j) as pool:
        for rows in pool.map(lambda cm: run_case(cm[0], cm[1], args.timeout), todo):
            for r in rows:
                out.write(json.dumps(r) + "\n")
            out.flush()


if __name__ == "__main__":
    main()
