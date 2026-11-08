# The thingi-pairs data class: a pinned list of pairs of Thingi10K models after the EMBER protocol (README.md,
# "Thingi10K pairs"). Nothing of the data is part of this repository: the meshes are fetched at run time into a
# directory of your choice (Thingi10K models carry individual licenses, mostly Creative Commons).
#
#   python3 data/thingi_pairs.py fetch <dir> [--pairs]     --pairs: the models of the committed pair lists only (to run the
#                                                          benchmark); otherwise metadata and all candidate meshes (to rebuild
#                                                          the clean list with `filter`) -> <dir>/meshes
#   python3 data/thingi_pairs.py filter <dir>              NotCleanReason of every candidate -> <dir>/filter.csv, and
#                                                          thingi-pairs/clean_models.csv (committed)
#   python3 data/thingi_pairs.py generate                  the pair lists thingi-pairs/pairs.csv, pairs_size.csv from
#                                                          clean_models.csv and the seeds (no network, no meshes)
#   python3 data/thingi_pairs.py manifest <pairs.csv> <dir> <out.jsonl> [--limit N]
#                                                          the case manifest for compare.py
#   python3 data/thingi_pairs.py summary results.jsonl...  per method: status, triangles, time, volume errors
#
# `generate` is a pure function of clean_models.csv and the seeds: running it again gives the identical files
# (the generator has its own PRNG, SplitMix64, and writes fixed-precision decimals).
import csv, json, math, os, subprocess, sys, time, urllib.request, re, statistics as st
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
LISTS = os.path.join(os.path.dirname(HERE), "thingi-pairs")
BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"

# --- the protocol (README.md explains each choice)
SEED_NORMAL, SEED_SIZE, SEED_SAMPLE = 20261104, 20261105, 20261106     # the day of the owner's decision, and the next
PAIRS_NORMAL, PAIRS_SIZE, PAIRS_SAMPLE = 1000, 100, 100   # the sample: the pairs CGAL EPECK runs on
NORMAL_TRIS = (1000, 100000)                    # triangles per operand (EMBER: 1k-100k faces)
SIZE_TRIS = (100001, 1000000)                   # the size class: 100k-1M triangles per operand
CATEGORIES = ["Closed", "Edge manifold", "Vertex manifold", "No degenerate faces", "No duplicated faces", "PWN"]

class SplitMix64:
    def __init__(self, seed): self.s = seed & 0xFFFFFFFFFFFFFFFF
    def next(self):
        self.s = (self.s + 0x9E3779B97F4A7C15) & 0xFFFFFFFFFFFFFFFF
        z = self.s
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
        return z ^ (z >> 31)
    def uniform(self): return (self.next() >> 11) / float(1 << 53)    # [0, 1)
    def below(self, n): return min(int(self.uniform() * n), n - 1)

def pair_rows(models, seed, count):
    """count pairs of different models drawn with replacement across pairs; B gets a uniformly random rotation
    (Shoemake) and a translation uniform in [-0.5, 0.5]^3 (the operands are normalized to the unit box)."""
    rng = SplitMix64(seed)
    rows = []
    for i in range(count):
        a = models[rng.below(len(models))]
        b = a
        while b == a:
            b = models[rng.below(len(models))]
        u1, u2, u3 = rng.uniform(), rng.uniform(), rng.uniform()
        s1, s2 = math.sqrt(1 - u1), math.sqrt(u1)
        q = (s1 * math.sin(2 * math.pi * u2), s1 * math.cos(2 * math.pi * u2), s2 * math.sin(2 * math.pi * u3), s2 * math.cos(2 * math.pi * u3))
        t = tuple(rng.uniform() - 0.5 for _ in range(3))
        rows.append([f"{i:04d}", a, b] + [f"{x:.12f}" for x in q + t])
    return rows

HEADER = ["pair", "a", "b", "qx", "qy", "qz", "qw", "tx", "ty", "tz"]

def read_clean(path=None):
    path = path or os.path.join(LISTS, "clean_models.csv")
    return [(r["id"], int(r["tris"])) for r in csv.DictReader(l for l in open(path) if not l.startswith("#"))]

def write_pairs(path, comment, rows):
    with open(path, "w", newline="") as f:
        for c in comment:
            f.write("# " + c + "\n")
        w = csv.writer(f, lineterminator="\n")
        w.writerow(HEADER)
        w.writerows(rows)

def sample_rows(rows, seed, count):
    """count of the rows, chosen by a partial Fisher-Yates shuffle of the indices, in the order of the list (pair numbers kept)"""
    rng = SplitMix64(seed)
    idx = list(range(len(rows)))
    for i in range(count):
        j = i + rng.below(len(idx) - i)
        idx[i], idx[j] = idx[j], idx[i]
    return [rows[k] for k in sorted(idx[:count])]

def generate(clean=None, out=LISTS):
    clean = clean or read_clean()
    for name, seed, count, (lo, hi) in (("pairs.csv", SEED_NORMAL, PAIRS_NORMAL, NORMAL_TRIS), ("pairs_size.csv", SEED_SIZE, PAIRS_SIZE, SIZE_TRIS)):
        pool = sorted(k for k, n in clean if lo <= n <= hi)
        write_pairs(os.path.join(out, name), [
            f"thingi-pairs: {count} pairs drawn with SplitMix64 seed {seed} from the {len(pool)} clean models with {lo}-{hi} triangles",
            "of clean_models.csv (sorted by id); B = rotation (quaternion x y z w) and translation t of the unit-box normalized model",
        ], pair_rows(pool, seed, count))
        if name == "pairs.csv":
            rows = pair_rows(pool, seed, count)
            write_pairs(os.path.join(out, "pairs_cgal_sample.csv"), [
                f"thingi-pairs: {PAIRS_SAMPLE} of the {count} pairs of pairs.csv (same rows and pair numbers), chosen with SplitMix64 seed {SEED_SAMPLE}:",
                "the pairs that CGAL EPECK runs on (it is slow and single-threaded)",
            ], sample_rows(rows, SEED_SAMPLE, PAIRS_SAMPLE))

# --- fetching

def fetch_file(url, path):
    if os.path.exists(path):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for attempt in range(5):        # a connection can be reset under load: retry with a growing pause
        try:
            urllib.request.urlretrieve(url, path + ".part")
            os.replace(path + ".part", path)
            return
        except OSError:
            if attempt == 4:
                raise
            time.sleep(2 ** attempt)

def file_list(d):
    p = os.path.join(d, "raw_files.tsv")
    if not os.path.exists(p):
        url, out = f"{BASE.replace('/datasets/', '/api/datasets/')}/tree/main/raw_meshes?limit=1000", []
        while url:
            r = urllib.request.urlopen(url)
            out += [x["path"] for x in json.load(r)]
            m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
            url = m.group(1) if m else None
        open(p, "w").write("".join(x + "\n" for x in out))
    return {os.path.basename(x).split(".")[0]: x for x in (l.strip() for l in open(p)) if x}

def candidates(d):
    """metadata prefilter (not the criterion: NotCleanReason of the mesh decides): closed, edge and vertex manifold, no
    degenerate or duplicated faces, PWN, no self-intersections, triangles in the range of either class"""
    for f in ("geometry_data.csv", "input_summary.csv"):
        fetch_file(f"{BASE}/resolve/main/metadata/{f}", os.path.join(d, f))
    g = {r["file_id"]: r for r in csv.DictReader(open(os.path.join(d, "geometry_data.csv")))}
    s = {r["ID"]: r for r in csv.DictReader(open(os.path.join(d, "input_summary.csv")))}
    lo, hi = NORMAL_TRIS[0], SIZE_TRIS[1]
    return sorted((k for k in s if k in g and all(s[k][c] == "TRUE" for c in CATEGORIES) and g[k]["num_self_intersections"] == "0"
                   and lo <= int(g[k]["num_faces"]) <= hi), key=int)

def pair_models(*names):
    ids = set()
    for name in names:
        with open(os.path.join(LISTS, name)) as f:
            ids |= {r[k] for r in csv.DictReader(l for l in f if not l.startswith("#")) for k in ("a", "b")}
    return sorted(ids, key=int)

def cmd_fetch(d, only_pairs):
    """only_pairs: just the models of the committed pair lists (to run the benchmark); otherwise all candidates of the
    filter (to rebuild clean_models.csv)"""
    files = file_list(d)
    ids = pair_models("pairs.csv", "pairs_size.csv") if only_pairs else candidates(d)
    ids = [k for k in ids if k in files]
    if not only_pairs:
        open(os.path.join(d, "candidates.txt"), "w").write("".join(k + "\n" for k in ids))
    print(len(ids), "models; fetching")
    with ThreadPoolExecutor(4) as ex:
        list(ex.map(lambda k: fetch_file(f"{BASE}/resolve/main/{files[k]}", os.path.join(d, "meshes", os.path.basename(files[k]))), ids))

def mesh_path(d, k):
    m = [f for f in os.listdir(os.path.join(d, "meshes")) if f.split(".")[0] == k]
    return os.path.join(d, "meshes", m[0])

def tool():
    return ["dotnet", os.environ.get("BOOLCMP_TOOL", os.path.join(os.path.dirname(HERE), "bin/Release/net10.0/BooleanComparison.dll"))]

def cmd_filter(d):
    ids = [l.strip() for l in open(os.path.join(d, "candidates.txt")) if l.strip()]
    out = os.path.join(d, "filter.csv")
    byid = {f.split(".")[0]: f for f in os.listdir(os.path.join(d, "meshes"))}
    for i in range(0, len(ids), 200):       # chunks: the output is appended, an interrupted run resumes
        subprocess.run(tool() + ["thingi-filter", out] + [os.path.join(d, "meshes", byid[k]) for k in ids[i:i + 200]], check=True)
        print(f"{min(i + 200, len(ids))} / {len(ids)}", flush=True)
    rows = [l.rstrip("\n").split(",") for l in open(out)]
    reasons = {}
    for _, _, r in rows:
        reasons[r or "clean"] = reasons.get(r or "clean", 0) + 1
    print("NotCleanReason of the candidates:", reasons)
    clean = sorted(((k, n) for k, n, r in rows if r == ""), key=lambda x: int(x[0]))
    os.makedirs(LISTS, exist_ok=True)
    with open(os.path.join(LISTS, "clean_models.csv"), "w") as f:
        f.write("# Thingi10K models that pass the filter of README.md: metadata (closed, edge and vertex manifold, no degenerate or\n"
                "# duplicated faces, PWN, no self-intersections) and MeshRobustRepair.NotCleanReason(mesh) == \"\" on the model as read\n")
        f.write("id,tris\n" + "".join(f"{k},{n}\n" for k, n in clean))
    print(len(clean), "clean models")

def cmd_manifest(pairs, d, out, limit=None):
    rows = list(csv.DictReader(l for l in open(pairs) if not l.startswith("#")))[:limit]
    cls = "thingi-pairs-size" if "size" in os.path.basename(pairs) else "thingi-pairs"
    with open(out, "w") as f:
        for r in rows:
            f.write(json.dumps({"class": cls, "name": f"p{r['pair']}_{r['a']}+{r['b']}",
                                "thingi_pair": [mesh_path(d, r["a"]), mesh_path(d, r["b"])] + [r[k] for k in HEADER[3:]]}) + "\n")
    print(len(rows), "cases ->", out)

# --- summary

def geomean(x): return math.exp(sum(math.log(max(v, 1e-3)) for v in x) / len(x)) if x else float("nan")

def cmd_summary(files):
    rows = [json.loads(l) for f in files for l in open(f) if l.strip()]
    by = {}
    for r in rows:
        if r.get("method") != "export":
            by.setdefault((r["class"], r["method"]), []).append(r)
    ops = ["union", "intersection", "difference"]
    print("| class | method | pairs | operations | ok | rejected | failed / crash / timeout | clean | empty (volume-consistent) | unclean | max volume error | median ms | geomean ms | p95 ms | max ms | median result triangles |")
    print("|" + "---|" * 16)
    for (cls, m), rs in sorted(by.items()):
        n = ok = rej = bad = clean = empty = 0
        unclean = {}
        errs, ms, tris = [], [], []
        for r in rs:
            for op in ops:
                n += 1
                s = r.get(f"{op}_status")
                if s == "rejected": rej += 1; continue
                if s != "ok": bad += 1; continue
                ok += 1
                ms.append(r.get(f"{op}_ms") or 0)
                tris.append(r.get(f"{op}_tris", 0))
                reason = r.get(f"{op}_clean_reason")
                if reason == "": clean += 1
                elif reason == "empty": empty += 1
                else: unclean[reason] = unclean.get(reason, 0) + 1
            errs += [r[k] for k in ("ie_error", "diff_error") if r.get(k) is not None]
        ms.sort()
        p95 = ms[min(len(ms) - 1, int(0.95 * len(ms)))] if ms else float("nan")
        print(f"| {cls} | {m} | {len(rs)} | {n} | {ok} | {rej} | {bad} | {clean} | {empty} | {', '.join(f'{k} {v}' for k, v in sorted(unclean.items())) or '–'} | {max(errs, default=float('nan')):.2e} | "
              f"{st.median(ms) if ms else float('nan'):.0f} | {geomean(ms):.0f} | {p95:.0f} | {max(ms, default=float('nan')):.0f} | {st.median(tris) if tris else 0:.0f} |")

if __name__ == "__main__":
    a = sys.argv[1:]
    if a[:1] == ["fetch"]: cmd_fetch(a[1], "--pairs" in a)
    elif a[:1] == ["filter"]: cmd_filter(a[1])
    elif a[:1] == ["generate"]: generate()
    elif a[:1] == ["manifest"]: cmd_manifest(a[1], a[2], a[3], int(a[a.index("--limit") + 1]) if "--limit" in a else None)
    elif a[:1] == ["summary"]: cmd_summary(a[1:])
    else: sys.exit(__doc__)
