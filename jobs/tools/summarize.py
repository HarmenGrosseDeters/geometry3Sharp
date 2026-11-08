#!/usr/bin/env python3
"""Reads a finished (or aborted) Stryker run in tests/mutation/StrykerOutput and writes a one-file summary.
Usage: python3 jobs/tools/summarize.py <label> <out json> [stryker log file]"""
import collections, glob, json, os, re, sys
label, out = sys.argv[1], sys.argv[2]
base = "tests/mutation/StrykerOutput"
runs = sorted(glob.glob(base + "/*"))
d = runs[-1] if runs else None
res = {"label": label, "run_dir": d}
log = " ".join(open(p, encoding="utf-8", errors="replace").read() for p in sorted(glob.glob(d + "/logs/log-*.txt"))) if d else ""
for key, pat in (("tests", r"Number of tests found: (\d+)"), ("mutants_created", r"(\d+) mutants created"),
                 ("to_test", r"(\d+)\s+total mutants will be tested"), ("elapsed", r"Time Elapsed (\S+)")):
    m = re.findall(pat, log)
    res[key] = m[-1] if m else None
for key, pat in (("created_at", r"(\d\d:\d\d:\d\d)\S* +\[INF\] \d+ mutants created"),
                 ("capture_end_at", r"(\d\d:\d\d:\d\d)\S* +\[INF\] \d+\s+total mutants will be tested")):
    m = re.findall(pat, log)
    res[key] = m[-1] if m else None
rep = sorted(glob.glob(base + "/*/reports/mutation-report.json"))
if rep:
    r = json.load(open(rep[-1]))
    c = collections.Counter()
    per_line = collections.defaultdict(list)
    for f, v in r["files"].items():
        for m in v["mutants"]:
            if m["status"] not in ("Ignored", "CompileError"):
                c[m["status"]] += 1
                per_line[m["status"]].append(m["location"]["start"]["line"])
    res["statuses"] = dict(c)
    det = c["Killed"] + c["Timeout"]; tot = det + c["Survived"] + c["NoCoverage"]
    res["score"] = round(100.0 * det / tot, 2) if tot else None
    res["killed_only_score"] = round(100.0 * c["Killed"] / tot, 2) if tot else None
    res["survivor_lines"] = sorted(per_line["Survived"]) + [-l for l in sorted(per_line["NoCoverage"])]
json.dump(res, open(out, "w"), indent=1)
print(json.dumps({k: v for k, v in res.items() if k != "survivor_lines"}))
