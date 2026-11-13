#!/usr/bin/env python3
"""Runs the mesher and the oracle on the inputs of an ALKIS sample (alkis_sample.py / alkis_tiles.py) and records one line per run.

    python3 benchmarks/Meshing2Benchmark/corpus/alkis_run.py --sample <dir with sample.json> [--dll <Meshing2Benchmark.dll>]
        [--timeout 900] [--only STRATUM ...] [--no-oracle] [--summary]

One `mesh --input <file> --output <file>` process per input (mesher, then the oracle of the same milestone), so a crash or a hang of
one run does not stop the others. Results are appended to <dir>/results.jsonl (resumable: a run with a line is skipped). The line:
{stratum, tile, name, milestone, verdict (VALID | INVALID | MESHER-FAILED | TIMEOUT | CRASH), detail, triangles, vertices, mesh_ms,
oracle_ms, steiner_limit_reached, wall_s, flags}. Flags: SLOW (mesher above 20 s), SLOW-ORACLE (oracle above 20 s, noted only), STEINER (limit reached), and the verdicts above.
Every flagged run is a finding (README, "ALKIS sample").
"""
import argparse
import json
import os
import re
import subprocess
import sys
import time


def run_one(dll, path, out, milestone, timeout, oracle):
    cmd = ["dotnet", dll, "mesh", "--input", path, "--output", out, "--milestone", milestone] + ([] if oracle else ["--no-oracle"])
    t0 = time.time()
    try:
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return {"verdict": "TIMEOUT", "wall_s": time.time() - t0, "detail": f"no result within {timeout} s"}
    wall = time.time() - t0
    text = p.stdout
    r = {"wall_s": round(wall, 2)}
    m = re.search(r"^status (\S+?),? (?:(\d+) triangles, (\d+) vertices)?", text, re.M)
    if p.returncode != 0 and not m:
        r.update(verdict="CRASH", detail=(p.stderr or text).strip().splitlines()[-1][:300] if (p.stderr or text).strip() else f"exit {p.returncode}")
        return r
    if m:
        r["status"] = m.group(1)
        if m.group(2):
            r["triangles"], r["vertices"] = int(m.group(2)), int(m.group(3))
    t = re.search(r"time: mesh (\d+) ms, oracle (\d+) ms", text)
    if t:
        r["mesh_ms"], r["oracle_ms"] = int(t.group(1)), int(t.group(2))
    else:
        t = re.search(r"time: mesh (\d+) ms", text)
        if t:
            r["mesh_ms"] = int(t.group(1))
    r["steiner_limit_reached"] = "steiner_limit_reached true" in text
    if not m or not m.group(1).startswith("Success"):
        r.update(verdict="MESHER-FAILED", detail=(m.group(0) if m else text.strip()[:300]))
    elif not oracle:
        r["verdict"] = "NOT-JUDGED"
    else:
        o = re.search(r"^oracle (VALID|INVALID.*)$", text, re.M)
        if o is None:
            r.update(verdict="CRASH", detail="no oracle line: " + (p.stderr or text).strip()[-300:])
        elif o.group(1) == "VALID":
            r["verdict"] = "VALID"
        else:
            r.update(verdict="INVALID", detail=o.group(1)[:600])
    return r


def flags(r):
    f = []
    if r["verdict"] not in ("VALID", "NOT-JUDGED"):
        f.append(r["verdict"])
    if r.get("mesh_ms", 0) > 20000 or (r.get("mesh_ms") is None and r.get("wall_s", 0) > 20):
        f.append("SLOW")             # the mesher alone took more than 20 s: a finding
    if r.get("oracle_ms", 0) > 20000:
        f.append("SLOW-ORACLE")      # the oracle took more than 20 s: noted (the oracle is 2 to 10 times the mesher on these inputs)
    if r.get("steiner_limit_reached"):
        f.append("STEINER")
    return f


def milestone_of(name):
    return "M2" if name.startswith("m2_") else "M3"


def summary(results, tiles):
    meta = {(t["stratum"], t["tile"]): t for t in tiles}
    print("stratum\ttile\tpoints\tsegments\trun\tverdict\ttriangles\tmesh_ms\toracle_ms\twall_s\tflags")
    for r in results:
        t = meta.get((r["stratum"], r["tile"]), {})
        print("\t".join(str(x) for x in (r["stratum"], r["tile"], t.get("points", ""), t.get("segments", ""), r["name"].split("_")[0] + "_" + r["name"].rsplit("_", 1)[-1] if not r["name"].startswith("m2_") else "m2",
                                       r["verdict"], r.get("triangles", ""), r.get("mesh_ms", ""), r.get("oracle_ms", ""), r["wall_s"], ",".join(flags(r)))))


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--sample", required=True)
    ap.add_argument("--dll", default=os.path.join(here, "..", "bin", "Release", "net10.0", "Meshing2Benchmark.dll"))
    ap.add_argument("--timeout", type=int, default=900)
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--no-oracle", action="store_true")
    ap.add_argument("--known", help="JSON {run name: issue} of known findings: exit 1 on a failing run not listed, and on a listed run that now passes")
    ap.add_argument("--expect-runs", type=int, help="with --known: the number of runs the sample must have (an empty or partial draw fails)")
    ap.add_argument("--summary", action="store_true", help="print the table of results.jsonl and stop")
    args = ap.parse_args()
    manifest = json.load(open(os.path.join(args.sample, "sample.json"), encoding="utf-8"))["tiles"]
    path = os.path.join(args.sample, "results.jsonl")
    done = {}
    if os.path.exists(path):
        for line in open(path, encoding="utf-8"):
            r = json.loads(line)
            done[(r["stratum"], r["name"])] = r
    if args.summary:
        summary(list(done.values()), manifest)
        return 0
    with open(path, "a", encoding="utf-8") as log:
        for t in manifest:
            if args.only and t["stratum"] not in args.only:
                continue
            for name in t["files"]:
                if (t["stratum"], name) in done:
                    continue
                src = os.path.join(args.sample, t["stratum"], name + ".txt")
                out = os.path.join(args.sample, t["stratum"], name + ".out")
                r = run_one(os.path.abspath(args.dll), src, out, milestone_of(name), args.timeout, not args.no_oracle)
                r.update(stratum=t["stratum"], tile=t["tile"], name=name, milestone=milestone_of(name))
                r["flags"] = flags(r)
                log.write(json.dumps(r, ensure_ascii=False) + "\n")
                log.flush()
                print(name, r["verdict"], r.get("triangles", ""), f"mesh {r.get('mesh_ms', '-')} ms oracle {r.get('oracle_ms', '-')} ms wall {r['wall_s']} s", ",".join(r["flags"]), flush=True)
                if os.path.exists(out) and os.path.getsize(out) > 50_000_000:
                    os.remove(out)   # keep the disk for the next runs; a finding keeps its input, the run can be repeated
    return check_known(args, manifest, path)


def check_known(args, manifest, path):
    """Exit status for the scheduled job: every run VALID except the known findings (a ratchet, like herne_ratchet.py)."""
    if not args.known:
        return 0
    known = json.load(open(args.known, encoding="utf-8"))
    results = {}
    for line in open(path, encoding="utf-8"):
        r = json.loads(line)
        results[(r["stratum"], r["name"])] = r
    wanted = {(t["stratum"], n) for t in manifest if not args.only or t["stratum"] in args.only for n in t["files"]}
    bad = 0
    if not wanted or (args.expect_runs is not None and len(wanted) != args.expect_runs):
        print(f"the sample has {len(wanted)} runs, expected {args.expect_runs if args.expect_runs is not None else 'at least 1'}: an empty or partial draw is a failure")
        bad += 1
    for key in sorted(wanted):
        r = results.get(key)
        name = key[1]
        failing = r is None or r["verdict"] not in ("VALID", "NOT-JUDGED") or "SLOW" in r["flags"] or "STEINER" in r["flags"]
        if failing and name not in known:
            print(f"NEW FINDING {name}: {r['verdict'] if r else 'no result'} {r.get('detail', '') if r else ''}"[:400])
            bad += 1
        elif not failing and name in known:
            print(f"FIXED {name} ({known[name]}): remove it from {args.known}")
            bad += 1
    print("known findings held" if bad == 0 else f"{bad} runs differ from {args.known}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
