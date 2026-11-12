#!/usr/bin/env python3
"""Runtime watch: per master commit, time a fixed list of known extreme cases of the Exact Boolean and the Alpha Wrap
and report the outliers (docs/QA.md, "Runtime watch"; workflow .github/workflows/runtime-watch.yml).

  validate  <cases.json>                  check the case list (no network, no dotnet)
  run       <cases.json> --out results.json [--only ID,...] [--cache DIR] [--work DIR] [--sha S]
                                          measure: one process per run, `repeats` runs per case, median; Thingi10K meshes
                                          of the cases are fetched here (nightly job only, never committed)
  evaluate  <results.json> --history history.json --out-history new.json --report report.md [--cases cases.json]
                                          judge the results against the history, write the report and the new history;
                                          prints `outliers=N` and `notify=0|1` (GITHUB_OUTPUT lines)

The outlier rule (cases.json "rules"): a case is a regression when its median is more than `ratio` times the median of its
last `window` successful runs AND more than `abs_s` seconds above it; it is "over the hard limit" above `hard_s` seconds
(owner 2026-11-12: more than 20 s is a potential defect); a failed or timed-out run is an outlier, too.
The time is the isolated time of one process (never several cases at once), the median of the repeats.
"""

import argparse
import json
import os
import platform
import re
import statistics
import subprocess
import sys
import time

DEFAULT_RULES = {"ratio": 1.3, "abs_s": 1.0, "hard_s": 20.0, "window": 10, "repeats": 3,
                 "repeat_budget_s": 60.0, "timeout_s": 900.0}
AREAS = ("boolean", "alphawrap")
KINDS = ("procedural", "thingi")
HISTORY_KEEP = 60
THINGI_URL = "https://huggingface.co/datasets/Thingi10K/Thingi10K/resolve/main/raw_meshes/{id}.stl"


# ---------------------------------------------------------------- case list

def load_cases(path):
    """(rules, cases) of a case-list file; raises ValueError listing every problem."""
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    errors = []
    if data.get("version") != 1:
        errors.append("version must be 1")
    rules = dict(DEFAULT_RULES)
    for k, v in (data.get("rules") or {}).items():
        if k not in DEFAULT_RULES:
            errors.append(f"unknown rule '{k}'")
        elif not isinstance(v, (int, float)) or isinstance(v, bool) or v <= 0:
            errors.append(f"rule '{k}' must be a positive number")
        else:
            rules[k] = v
    cases, seen = [], set()
    for i, c in enumerate(data.get("cases") or []):
        cid = c.get("id")
        where = f"case {i} ({cid})"
        if not isinstance(cid, str) or not cid or not all(ch.isalnum() or ch in "-_." for ch in cid):
            errors.append(f"case {i}: id must be a non-empty string of letters, digits, '-', '_', '.'")
            continue
        if cid in seen:
            errors.append(f"{where}: duplicate id")
        seen.add(cid)
        if c.get("area") not in AREAS:
            errors.append(f"{where}: area must be one of {AREAS}")
        if c.get("kind") not in KINDS:
            errors.append(f"{where}: kind must be one of {KINDS}")
        if not c.get("description"):
            errors.append(f"{where}: description missing")
        run = c.get("run")
        if not (isinstance(run, list) and run and all(isinstance(a, str) for a in run)):
            errors.append(f"{where}: run must be a non-empty list of strings (an argv)")
        for s in c.get("setup", []):
            if not (isinstance(s, list) and s and all(isinstance(a, str) for a in s)):
                errors.append(f"{where}: every setup step must be a non-empty argv list")
        thingi = c.get("thingi", [])
        if not (isinstance(thingi, list) and all(isinstance(t, str) and t.isdigit() for t in thingi)):
            errors.append(f"{where}: thingi must be a list of numeric ids")
        if (c.get("kind") == "thingi") != bool(thingi):
            errors.append(f"{where}: a case of kind 'thingi' lists its ids in 'thingi', a procedural one has none")
        tf = c.get("time_field", "wall")
        if not isinstance(tf, str) or not tf:
            errors.append(f"{where}: time_field must be a JSON field name of the run's last stdout line, or 'wall'")
        for arg in _all_args(c):
            for ph in _placeholders(arg):
                if ph.startswith("thingi:") and ph[7:] not in thingi:
                    errors.append(f"{where}: placeholder {{{ph}}} names an id that is not in 'thingi'")
                elif not (ph == "work" or ph.startswith("dll:") or ph.startswith("thingi:")):
                    errors.append(f"{where}: unknown placeholder {{{ph}}}")
        cases.append(c)
    if errors:
        raise ValueError("; ".join(errors))
    return rules, cases


def _all_args(case):
    for a in case.get("run", []):
        yield a
    for s in case.get("setup", []):
        for a in s:
            yield a


_PLACEHOLDER = re.compile(r"\{([A-Za-z][\w.]*(?::[\w.]+)?)\}")


def _placeholders(arg):
    """The {name} / {kind:value} placeholders of one argument (other braces, eg JSON in a python -c, are literal)."""
    return _PLACEHOLDER.findall(arg)


def expand(arg, work, thingi_paths, repo_root="."):
    """Replace {work}, {dll:Name} and {thingi:ID} in one argument."""
    for ph in _placeholders(arg):
        if ph == "work":
            val = work
        elif ph.startswith("dll:"):
            n = ph[4:]
            val = os.path.join(repo_root, "benchmarks", n, "bin", "Release", "net10.0", n + ".dll")
        else:
            val = thingi_paths[ph[7:]]
        arg = arg.replace("{" + ph + "}", val)
    return arg


# ---------------------------------------------------------------- outlier rule

def baseline(history, window):
    """Median seconds of the last `window` successful entries of one case's history, or None."""
    vals = [e["median_s"] for e in history if e.get("ok") and e.get("median_s") is not None]
    vals = vals[-int(window):]
    return statistics.median(vals) if vals else None


def judge(result, history, rules):
    """Classify one result against its case history. Returns a dict with:
    flags: subset of {'failed', 'hard', 'regression'}; baseline_s; ratio; new: flags the previous entry did not have."""
    prev = history[-1] if history else None
    base = baseline(history, rules["window"])
    flags = []
    t = result.get("median_s")
    if not result.get("ok") or t is None:
        flags.append("failed")
    else:
        if t > rules["hard_s"]:
            flags.append("hard")
        if base is not None and t > rules["ratio"] * base and t - base > rules["abs_s"]:
            flags.append("regression")
    prev_flags = set(prev.get("flags", [])) if prev else set()
    ratio = (t / base) if (t is not None and base) else None
    return {"flags": flags, "baseline_s": base, "ratio": ratio, "new": [f for f in flags if f not in prev_flags]}


def evaluate(results, history, rules):
    """(per-case judgements, new history). history: {'version': 1, 'entries': {id: [entry...]}}."""
    entries = {k: list(v) for k, v in (history.get("entries") or {}).items()}
    judged = []
    for r in results["cases"]:
        past = entries.get(r["id"], [])
        j = judge(r, past, rules)
        judged.append({**r, **j})
        entries.setdefault(r["id"], []).append({
            "sha": results.get("sha"), "date": results.get("date"), "ok": bool(r.get("ok")),
            "median_s": r.get("median_s"), "runs_s": r.get("runs_s"), "flags": j["flags"],
            "machine": results.get("machine", {}).get("cpu")})
    for k in entries:
        entries[k] = entries[k][-HISTORY_KEEP:]
    return judged, {"version": 1, "entries": entries}


def notify(judged):
    """Comment on the tracking issue only for something new: a regression, a failure, or a case newly over the hard limit
    (a case that was over it last night, too, stays in the job summary but is not repeated)."""
    return any(f in ("regression", "failed") or (f == "hard" and "hard" in j["new"])
               for j in judged for f in j["flags"])


def render_report(results, judged, rules):
    lines = [f"## Runtime watch {results.get('sha', '')[:10]}", ""]
    m = results.get("machine", {})
    lines.append(f"Machine: {m.get('cpu', '?')}, {m.get('cores', '?')} cores, {m.get('memory_gb', '?')} GB, "
                 f"{m.get('os', '?')}, dotnet {m.get('dotnet', '?')}. Isolated runs (one process each), median of the repeats.")
    lines.append(f"Rule: regression = more than {rules['ratio']}x the median of the last {int(rules['window'])} runs and "
                 f"more than {rules['abs_s']} s above it; hard limit {rules['hard_s']} s (potential defect).")
    lines.append("")
    bad = [j for j in judged if j["flags"]]
    lines.append(f"**{len(bad)} outlier(s) of {len(judged)} cases.**" if bad else f"No outliers in {len(judged)} cases.")
    lines += ["", "| case | area | median s | runs s | baseline s | ratio | flags |", "|---|---|---:|---|---:|---:|---|"]
    for j in judged:
        def f(x, d=2):
            return "-" if x is None else f"{x:.{d}f}"
        runs = ", ".join(f"{x:.2f}" for x in (j.get("runs_s") or []))
        flags = ", ".join(j["flags"]) if j["flags"] else ""
        if not j.get("ok") and j.get("error"):
            flags += f" ({j['error']})"
        lines.append(f"| {j['id']} | {j.get('area', '')} | {f(j.get('median_s'))} | {runs} | {f(j['baseline_s'])} | "
                     f"{f(j['ratio'])} | {flags} |")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------- measuring

def machine_info():
    cpu, mem = platform.processor() or "?", None
    try:
        with open("/proc/cpuinfo", encoding="utf-8") as f:
            for line in f:
                if line.startswith("model name"):
                    cpu = line.split(":", 1)[1].strip()
                    break
        with open("/proc/meminfo", encoding="utf-8") as f:
            mem = round(int(f.readline().split()[1]) / 1048576, 1)
    except OSError:
        pass
    try:
        dn = subprocess.run(["dotnet", "--version"], capture_output=True, text=True, timeout=60).stdout.strip()
    except (OSError, subprocess.SubprocessError):
        dn = "?"
    return {"cpu": cpu, "cores": os.cpu_count(), "memory_gb": mem, "os": platform.platform(), "dotnet": dn,
            "image": os.environ.get("ImageVersion", "")}


def fetch_thingi(tid, cache):
    """Download one Thingi10K mesh into the cache (nightly job only; the meshes have their own licences, never committed)."""
    import urllib.request
    os.makedirs(cache, exist_ok=True)
    path = os.path.join(cache, f"{tid}.stl")
    if not os.path.exists(path):
        urllib.request.urlretrieve(THINGI_URL.format(id=tid), path + ".part")
        os.replace(path + ".part", path)
    return path


def read_time(stdout, field, wall):
    """Seconds of one run: the JSON number `field` (milliseconds if it ends in 'ms') of the last JSON line of stdout, or the wall time."""
    if field == "wall":
        return wall
    for line in reversed(stdout.strip().splitlines()):
        line = line.strip()
        if line.startswith("{"):
            v = json.loads(line).get(field)
            if v is None:
                raise ValueError(f"no field '{field}' in the output")
            return v / 1000.0 if field.endswith("ms") else float(v)
    raise ValueError("no JSON line in the output")


def run_case(case, rules, cache, work_root, repo_root="."):
    repeats = int(case.get("repeats", rules["repeats"]))
    timeout = case.get("timeout_s", rules["timeout_s"])
    work = os.path.abspath(os.path.join(work_root, case["id"]))
    os.makedirs(work, exist_ok=True)
    res = {"id": case["id"], "area": case["area"], "kind": case["kind"], "ok": False, "runs_s": [], "median_s": None}
    try:
        paths = {t: fetch_thingi(t, cache) for t in case.get("thingi", [])}
    except Exception as e:  # network failure: reported, not an outlier of the library
        res["error"] = f"fetch failed: {e}"
        res["infra"] = True
        return res
    env = {**os.environ, **case.get("env", {})}
    def argv(a):
        return [expand(x, work, paths, repo_root) for x in a]
    try:
        for step in case.get("setup", []):
            p = subprocess.run(argv(step), env=env, capture_output=True, text=True, timeout=timeout)
            if p.returncode != 0:
                res["error"] = f"setup failed ({p.returncode}): {p.stderr.strip()[-200:]}"
                return res
        for _ in range(repeats):
            t0 = time.perf_counter()
            try:
                p = subprocess.run(argv(case["run"]), env=env, capture_output=True, text=True, timeout=timeout)
            except subprocess.TimeoutExpired:
                res["error"] = f"timeout after {timeout} s"
                res["runs_s"].append(float(timeout))
                res["median_s"] = float(timeout)
                return res
            wall = time.perf_counter() - t0
            if p.returncode != 0:
                res["error"] = f"exit {p.returncode}: {p.stderr.strip()[-200:]}"
                return res
            t = read_time(p.stdout, case.get("time_field", "wall"), wall)
            res["runs_s"].append(t)
            if t > rules["repeat_budget_s"]:
                break
    except (ValueError, OSError) as e:
        res["error"] = str(e)
        return res
    res["median_s"] = statistics.median(res["runs_s"])
    res["ok"] = True
    return res


# ---------------------------------------------------------------- CLI

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    v = sub.add_parser("validate")
    v.add_argument("cases")
    r = sub.add_parser("run")
    r.add_argument("cases")
    r.add_argument("--out", required=True)
    r.add_argument("--only", default="")
    r.add_argument("--cache", default="runtime-watch-meshes")
    r.add_argument("--work", default="runtime-watch-work")
    r.add_argument("--sha", default=os.environ.get("GITHUB_SHA", ""))
    e = sub.add_parser("evaluate")
    e.add_argument("results")
    e.add_argument("--history", required=True)
    e.add_argument("--out-history", required=True)
    e.add_argument("--report", required=True)
    e.add_argument("--cases", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "runtime-watch", "cases.json"))
    a = ap.parse_args(argv)
    if a.cmd == "validate":
        try:
            _, cases = load_cases(a.cases)
        except ValueError as ex:
            print(f"invalid: {ex}", file=sys.stderr)
            return 1
        print(f"ok: {len(cases)} cases")
        return 0
    if a.cmd == "run":
        rules, cases = load_cases(a.cases)
        only = {x for x in a.only.split(",") if x}
        if only - {c["id"] for c in cases}:
            print(f"unknown case(s): {sorted(only - {c['id'] for c in cases})}", file=sys.stderr)
            return 1
        out = {"sha": a.sha, "date": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "machine": machine_info(), "cases": []}
        for c in cases:
            if only and c["id"] not in only:
                continue
            res = run_case(c, rules, a.cache, a.work)
            print(f"{c['id']}: {res.get('median_s')} {res.get('error', '')}", flush=True)
            out["cases"].append(res)
            with open(a.out, "w", encoding="utf-8") as f:  # after every case: a killed job keeps what it measured
                json.dump(out, f, indent=1)
        return 0
    rules, _ = load_cases(a.cases)
    with open(a.results, encoding="utf-8") as f:
        results = json.load(f)
    history = {"version": 1, "entries": {}}
    if os.path.exists(a.history):
        with open(a.history, encoding="utf-8") as f:
            history = json.load(f)
    # a fetch failure is infrastructure, not a library outlier: not judged, not recorded
    infra = [c for c in results["cases"] if c.get("infra")]
    results["cases"] = [c for c in results["cases"] if not c.get("infra")]
    judged, new_hist = evaluate(results, history, rules)
    with open(a.out_history, "w", encoding="utf-8") as f:
        json.dump(new_hist, f, indent=1, sort_keys=True)
    report = render_report(results, judged, rules)
    if infra:
        report += "\nNot measured (infrastructure, not judged): " + ", ".join(f"{c['id']} ({c.get('error')})" for c in infra) + "\n"
    with open(a.report, "w", encoding="utf-8") as f:
        f.write(report)
    print(f"outliers={sum(1 for j in judged if j['flags'])}")
    print(f"notify={1 if notify(judged) else 0}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
