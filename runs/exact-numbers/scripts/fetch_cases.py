# Fetches the Thingi10K models of the exact-number study at run time (never committed) and writes the case lists
# for `Probe list`. Usage: python3 fetch_cases.py <repo> <mesh dir> <out dir>
import csv, json, os, re, sys, time, urllib.request
repo, meshdir, outdir = sys.argv[1:4]
BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"

def fetch_file(url, path):
    if os.path.exists(path): return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for attempt in range(5):
        try:
            urllib.request.urlretrieve(url, path + ".part"); os.replace(path + ".part", path); return
        except OSError:
            if attempt == 4: raise
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

files = file_list(meshdir)
def mesh(k):
    path = os.path.join(meshdir, os.path.basename(files[k]))
    fetch_file(f"{BASE}/resolve/main/{files[k]}", path)
    return path

lists = os.path.join(repo, "benchmarks/BooleanComparison/thingi-pairs")
pairs = [r for r in csv.DictReader(l for l in open(os.path.join(lists, "pairs.csv")) if not l.startswith("#"))]
clean = [r for r in csv.reader(l for l in open(os.path.join(lists, "clean_models.csv")) if not l.startswith("#"))][1:]
clean_ids = [r[0] for r in clean]

# normal stratum 1: the first 20 pairs of the committed thingi-pairs list, all three operations
with open(os.path.join(outdir, "cases_normal_pairs.txt"), "w") as f:
    for r in pairs[:20]:
        if r["a"] in files and r["b"] in files:
            f.write(f"pair pair{r['pair']}_{r['a']}+{r['b']} {mesh(r['a'])} {mesh(r['b'])} {r['qx']} {r['qy']} {r['qz']} {r['qw']} {r['tx']} {r['ty']} {r['tz']} 0,1,2\n")
# normal stratum 2: every 250th clean model with its rotated, shifted copy (class thingi10k: unrotated A keeps its
# exact coplanarities), all three operations
with open(os.path.join(outdir, "cases_normal_copy.txt"), "w") as f:
    for k in clean_ids[::250]:
        if k in files: f.write(f"copy copy_{k} {mesh(k)} 0,1,2\n")
# tail stratum: the documented extreme-runtime models (docs/roadmap/exact-boolean.md), copy class
tail_small = ["113685", "1059579", "105693", "107543", "107545", "107554", "113906", "42327", "67709"]
tail_big = ["519507", "1517923", "519511", "122796", "1005586"]
with open(os.path.join(outdir, "cases_tail_small.txt"), "w") as f:
    for k in tail_small:
        if k in files: f.write(f"copy copy_{k} {mesh(k)} 0,1,2\n")
with open(os.path.join(outdir, "cases_tail_big.txt"), "w") as f:
    for k in tail_big:
        if k in files: f.write(f"copy copy_{k} {mesh(k)} 0\n")
with open(os.path.join(outdir, "cases_tail_55928.txt"), "w") as f:
    if "55928" in files: f.write(f"copy copy_55928 {mesh('55928')} 2\n")
print("done")
