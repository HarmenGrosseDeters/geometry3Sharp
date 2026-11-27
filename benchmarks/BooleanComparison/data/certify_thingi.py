# The certifying checker on Thingi10K (docs/research/CERTIFICATE_HARDENING_2026-11.md): the driver of
# `BooleanComparison certify thingi` (CertifyThingi.cs). Nothing of the data is part of this repository; the models are
# fetched at run time, and the output holds result rows only (ids, sizes, verdicts, clauses, times), never a mesh.
#
#   python3 data/certify_thingi.py plan <cases.jsonl> [--self N] [--pairs N] [--contact N] [--max-tris T]
#           the cases: self-unions of the first N models of clean_models.csv in a SplitMix64 order (seed 20261125), the
#           first N pairs of pairs.csv and N per variant of contact_pairs.csv; only models with at most T triangles
#   python3 data/certify_thingi.py fetch <dir> <cases.jsonl>     the models of the cases -> <dir>/meshes (huggingface)
#   python3 data/certify_thingi.py run <cases.jsonl> <dir> <out.jsonl> [--timeout S] [--tool BooleanComparison.dll]
#           one process per case (all three operations), a timeout per case; resumes (cases in out.jsonl are skipped)
#   python3 data/certify_thingi.py summary <out.jsonl>...        the counts by verdict and clause, the cost against the size
#   run ... --command region-compare   the gate of E' stage 4 (#1630): each operation with and without RegionClipping (RegionClippingCompare.cs); summary then
#           counts "same" and the differences and gives the time with and without (python3 data/certify_thingi.py summary --region <out.jsonl>...)
import argparse, csv, json, os, subprocess, sys, time, statistics as st
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))     # (also under python3 -I)
import thingi_pairs as tp

HERE = os.path.dirname(os.path.abspath(__file__))
LISTS = os.path.join(os.path.dirname(HERE), "thingi-pairs")
SEED = 20261125
OPS = ["union", "intersection", "difference"]

def rows(name):
    with open(os.path.join(LISTS, name)) as f:
        return list(csv.DictReader(l for l in f if not l.startswith("#")))

def plan(out, n_self, n_pairs, n_contact, max_tris):
    tris = {k: n for k, n in tp.read_clean()}
    ok = lambda *ids: all(tris.get(i, 1 << 62) <= max_tris for i in ids)
    cases = []
    pool = sorted((k for k in tris if ok(k)), key=int)
    rng = tp.SplitMix64(SEED)
    for i in range(len(pool) - 1, 0, -1):            # Fisher-Yates
        j = rng.below(i + 1)
        pool[i], pool[j] = pool[j], pool[i]
    for k in pool[:n_self]:
        cases.append({"case": f"self-{k}", "args": ["self", k], "ids": [k]})
    for r in [r for r in rows("pairs.csv") if ok(r["a"], r["b"])][:n_pairs]:
        cases.append({"case": f"pair-{r['pair']}", "args": ["pair", r["a"], r["b"]] + [r[x] for x in ("qx", "qy", "qz", "qw", "tx", "ty", "tz")], "ids": [r["a"], r["b"]]})
    contact = rows("contact_pairs.csv")
    for v in sorted({r["variant"] for r in contact}):
        for r in [r for r in contact if r["variant"] == v and ok(r["a"], r["b"])][:n_contact]:
            cases.append({"case": f"contact-{r['pair']}-{v}", "args": ["contact", r["a"], r["b"], v, r["axis"], r["sign"], r["param"]], "ids": [r["a"], r["b"]]})
    with open(out, "w") as f:
        f.write("".join(json.dumps(c) + "\n" for c in cases))
    print(len(cases), "cases,", len({i for c in cases for i in c["ids"]}), "models, at most", max_tris, "triangles per model")

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))     # the repository root

def outside_repository(d):
    """the download directory must not be inside the repository: models are not committed (CLAUDE.md, no mesh files)"""
    d, r = os.path.realpath(d), os.path.realpath(ROOT)
    return os.path.commonpath([d, r]) != r

def fetch(d, cases):
    if outside_repository(d) == False:
        sys.exit(f"fetch: {d} is inside the repository {ROOT}; use a directory outside it (models are never committed)")
    os.makedirs(d, exist_ok=True)
    files = tp.file_list(d)
    ids = sorted({i for c in load_cases(cases) for i in c["ids"]}, key=int)
    with tp.ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda k: tp.fetch_file(f"{tp.BASE}/resolve/main/{files[k]}", os.path.join(d, "meshes", os.path.basename(files[k]))), ids))
    print(len(ids), "models fetched")

def load_cases(p):
    with open(p) as f:
        return [json.loads(l) for l in f if l.strip()]

def mesh_path(d, k):
    m = os.path.join(d, "meshes")
    for n in os.listdir(m):
        if n.split(".")[0] == k:
            return os.path.join(m, n)
    raise FileNotFoundError(k)

def run(cases, d, out, timeout, tool, command="certify"):
    done = set()
    if os.path.exists(out):
        for r in load_rows([out]):
            done.add(r["case"])
    for c in load_cases(cases):
        if c["case"] in done:
            continue
        args = list(c["args"])
        n_ids = 1 if args[0] == "self" else 2
        for i in range(1, 1 + n_ids):
            args[i] = mesh_path(d, args[i])
        t0 = time.time()
        try:
            p = subprocess.run(["dotnet", tool] + (["region-compare", "thingi"] if command == "region-compare" else ["certify", "thingi"]) + args, capture_output=True, text=True, timeout=timeout)
            lines = [l for l in p.stdout.splitlines() if l.startswith("{")]
            got = [json.loads(l) for l in lines]
            if p.returncode != 0 or not got:
                got.append({"verdict": "process failed", "message": (p.stderr or "")[-300:]})
        except subprocess.TimeoutExpired as e:
            got = [json.loads(l) for l in (e.stdout.decode() if isinstance(e.stdout, bytes) else (e.stdout or "")).splitlines() if l.startswith("{")]
            got.append({"verdict": "timeout", "timeout_s": timeout})
        with open(out, "a") as f:
            for r in got:
                r["case"] = c["case"]
                r["wall_s"] = round(time.time() - t0, 1)
                f.write(json.dumps(r) + "\n")
        print(c["case"], " ".join(f"{r.get('op', '-')}:{r['verdict']}" for r in got), flush=True)
    # the gate of the region comparison: any row that is not "same" (a difference, a timeout, an error) makes the run, and so the job, fail
    if command == "region-compare":
        bad = [r for r in load_rows([out]) if r["verdict"] not in OK_VERDICTS]
        if bad:
            print(len(bad), "rows are not 'same'", file=sys.stderr)
            return 1
    return 0

def load_rows(paths):
    out = []
    for p in paths:
        with open(p) as f:
            for l in f:
                try:
                    out.append(json.loads(l))
                except ValueError:
                    pass
    return out

def summary(paths):
    rs = load_rows(paths)
    cls = lambda r: r["case"].split("-")[0] + ("-" + r["case"].split("-", 2)[2] if r["case"].startswith("contact") else "")
    classes = sorted({cls(r) for r in rs})
    verdicts = ["certified", "uncovered", "removed parts", "rejected", "not applicable", "boolean failed", "boolean exception", "checker exception", "error", "timeout", "process failed"]
    print("| class | operations | " + " | ".join(verdicts) + " |")
    print("|---|---:|" + "---:|" * len(verdicts))
    for c in classes + ["all"]:
        sel = [r for r in rs if c == "all" or cls(r) == c]
        print(f"| {c} | {len(sel)} | " + " | ".join(str(sum(1 for r in sel if r['verdict'] == v)) for v in verdicts) + " |")
    rej = [r for r in rs if r["verdict"] in ("rejected", "uncovered", "boolean failed", "boolean exception", "checker exception", "timeout", "process failed")]
    if rej:
        print("\nnot certified (case, op, verdict, clause, message):\n")
        for r in rej:
            print(f"- {r['case']} {r.get('op', '-')} {r['verdict']} {r.get('clause', '')}: {r.get('message', '')[:200]}")
    tim = [r for r in rs if "t_check_ms" in r and "trisA" in r]
    if tim:
        print("\n| input triangles (A + B) | operations | boolean ms median | checker ms median | checker ms max | checker / boolean median |")
        print("|---|---:|---:|---:|---:|---:|")
        for lo, hi in ((0, 5000), (5000, 10000), (10000, 20000), (20000, 40000), (40000, 1 << 40)):
            sel = [r for r in tim if lo <= r["trisA"] + r["trisB"] < hi]
            if sel:
                print(f"| {lo}-{hi if hi < 1 << 40 else ''} | {len(sel)} | {st.median(r['t_bool_ms'] for r in sel):.0f} | {st.median(r['t_check_ms'] for r in sel):.0f} | "
                      f"{max(r['t_check_ms'] for r in sel):.0f} | {st.median(r['t_check_ms'] / max(r['t_bool_ms'], 1e-3) for r in sel):.1f} |")

OK_VERDICTS = ("same", "not compared (not clean)")     # (the second is counted apart: the results are not clean, so their surfaces are not compared)

def summary_region(paths):
    rs = load_rows(paths)
    verdicts = ["same", "not compared (not clean)", "failed differently", "status differs", "reason differs", "surface differs", "difference failed", "certificate rejected", "certificate run failed",
                "certificate exception", "exception off", "exception on", "error", "timeout", "process failed"]
    cls = lambda r: r["case"].split("-")[0] + ("-" + r["case"].split("-", 2)[2] if r["case"].startswith("contact") else "")
    print("| class | operations | " + " | ".join(verdicts) + " |")
    print("|---|---:|" + "---:|" * len(verdicts))
    for c in sorted({cls(r) for r in rs}) + ["all"]:
        sel = [r for r in rs if c == "all" or cls(r) == c]
        print(f"| {c} | {len(sel)} | " + " | ".join(str(sum(1 for r in sel if r["verdict"] == v)) for v in verdicts) + " |")
    certs = {}
    for r in rs:
        if "cert" in r: certs[r["cert"]] = certs.get(r["cert"], 0) + 1
    print("\ncertificate on the result with the switch on (a third run): " + (", ".join(f"{k} {v}" for k, v in sorted(certs.items())) or "none"))
    bad = [r for r in rs if r["verdict"] not in OK_VERDICTS]
    if bad:
        print("\ndifferences (case, op, verdict, message):\n")
        for r in bad:
            print(f"- {r['case']} {r.get('op', '-')} {r['verdict']}: {r.get('message', '')[:300]}")
    tim = [r for r in rs if "t_off_ms" in r and "t_on_ms" in r]
    if tim:
        print(f"\ntime over {len(tim)} operations: without {sum(r['t_off_ms'] for r in tim) / 1000:.1f} s, with {sum(r['t_on_ms'] for r in tim) / 1000:.1f} s")
        print("\n| input triangles | operations | median ms without | median ms with | max ms without | max ms with | slowest with / without |")
        print("|---|---:|---:|---:|---:|---:|---:|")
        for lo, hi in ((0, 5000), (5000, 20000), (20000, 100000), (100000, 1 << 40)):
            sel = [r for r in tim if lo <= r.get("tris", r.get("trisA", 0) + r.get("trisB", 0)) < hi]
            if sel:
                print(f"| {lo}-{hi if hi < 1 << 40 else ''} | {len(sel)} | {st.median(r['t_off_ms'] for r in sel):.0f} | {st.median(r['t_on_ms'] for r in sel):.0f} | "
                      f"{max(r['t_off_ms'] for r in sel):.0f} | {max(r['t_on_ms'] for r in sel):.0f} | {max(r['t_on_ms'] / max(r['t_off_ms'], 1e-3) for r in sel):.1f} |")
    return 1 if bad or not rs else 0        # a timeout, an error or a difference makes the job red

def main():
    ap = argparse.ArgumentParser()
    sp = ap.add_subparsers(dest="cmd", required=True)
    p = sp.add_parser("plan"); p.add_argument("out"); p.add_argument("--self", type=int, default=20); p.add_argument("--pairs", type=int, default=20)
    p.add_argument("--contact", type=int, default=3); p.add_argument("--max-tris", type=int, default=20000)
    p = sp.add_parser("fetch"); p.add_argument("dir"); p.add_argument("cases")
    p = sp.add_parser("run"); p.add_argument("cases"); p.add_argument("dir"); p.add_argument("out"); p.add_argument("--timeout", type=int, default=600); p.add_argument("--command", choices=["certify", "region-compare"], default="certify")
    p.add_argument("--tool", default=os.path.join(os.path.dirname(HERE), "bin", "Release", "net10.0", "BooleanComparison.dll"))
    p = sp.add_parser("summary"); p.add_argument("paths", nargs="+"); p.add_argument("--region", action="store_true")
    a = ap.parse_args()
    if a.cmd == "plan": plan(a.out, a.self, a.pairs, a.contact, a.max_tris)
    elif a.cmd == "fetch": fetch(a.dir, a.cases)
    elif a.cmd == "run": sys.exit(run(a.cases, a.dir, a.out, a.timeout, a.tool, a.command))
    elif a.region: sys.exit(summary_region(a.paths))
    else: summary(a.paths)

if __name__ == "__main__":
    main()
