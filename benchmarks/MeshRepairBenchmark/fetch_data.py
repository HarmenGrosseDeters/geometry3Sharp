# Downloads a reproducible selection of Thingi10K meshes (https://huggingface.co/datasets/Thingi10K/Thingi10K).
#   data/meshes:       6 defect categories x 60 broken meshes (<= 100k faces) + 40 clean controls
#   data/meshes_large: 30 broken meshes with 100k..1M faces  (skip with --no-large)
# --holdout: a disjoint second selection of the same shape (other seeds, the default selection excluded)
#   in data/holdout/ (selection_holdout.csv, selection_large_holdout.csv, meshes, meshes_large)
# Thingi10K models carry individual licenses (mostly Creative Commons), see data/input_summary.csv.
import csv, json, os, random, re, sys, urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"
os.chdir(os.path.dirname(os.path.abspath(__file__)))
os.makedirs("data", exist_ok=True)

def fetch(url, path):
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path)

for f in ["geometry_data.csv", "input_summary.csv"]:
    fetch(f"{BASE}/resolve/main/metadata/{f}", f"data/{f}")

if not os.path.exists("data/raw_files.tsv"):
    url, out = f"{BASE.replace('/datasets/', '/api/datasets/')}/tree/main/raw_meshes?limit=1000", []
    while url:
        r = urllib.request.urlopen(url)
        out += [(x["path"], x["size"]) for x in json.load(r)]
        m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
        url = m.group(1) if m else None
    open("data/raw_files.tsv", "w").write("".join(f"{p}\t{s}\n" for p, s in out))

g = {r["file_id"]: r for r in csv.DictReader(open("data/geometry_data.csv"))}
s = {r["ID"]: r for r in csv.DictReader(open("data/input_summary.csv"))}
files = {p.split("/")[1].split(".")[0]: p for p, _ in (l.rstrip("\n").split("\t") for l in open("data/raw_files.tsv"))}
cats = ["Closed", "Edge manifold", "Vertex manifold", "No degenerate faces", "No duplicated faces", "PWN"]

def faces(k): return int(g[k]["num_faces"])

def select(seed, seed_large, exclude=frozenset()):
    random.seed(seed)
    ok = [k for k in s if k in g and k in files and faces(k) <= 100000 and k not in exclude]
    sel = {}
    for c in cats:
        pool = [k for k in ok if s[k][c] == "FALSE" and k not in sel]
        for k in random.sample(pool, min(60, len(pool))):
            sel[k] = "broken:" + c
    clean = [k for k in ok if all(s[k][c] == "TRUE" for c in cats + ["Single Component"])]
    for k in random.sample(clean, 40):
        sel[k] = "clean"
    random.seed(seed_large)
    pool = [k for k in s if k in g and k in files and 100000 < faces(k) <= 1000000 and k not in exclude
            and any(s[k][c] == "FALSE" for c in cats)]
    large = {k: "large:" + "/".join(c for c in cats if s[k][c] == "FALSE") for k in random.sample(pool, 30)}
    return sel, large

sel, large = select(42, 7)
prefix, suffix = "", ""
if "--holdout" in sys.argv:
    # independent test set: other seeds, every mesh of the default selection excluded
    sel, large = select(2026, 2027, set(sel) | set(large))
    prefix, suffix = "holdout/", "_holdout"
    os.makedirs("data/holdout", exist_ok=True)
selections = [(f"{prefix}selection{suffix}.csv", f"{prefix}meshes", sel)]
if "--no-large" not in sys.argv:
    selections.append((f"{prefix}selection_large{suffix}.csv", f"{prefix}meshes_large", large))

for csvname, folder, chosen in selections:
    os.makedirs(f"data/{folder}", exist_ok=True)
    with open(f"data/{csvname}", "w") as f:
        f.write("file_id,path,category\n")
        for k, v in chosen.items():
            f.write(f"{k},{files[k]},{v}\n")
    with ThreadPoolExecutor(8) as ex:
        list(ex.map(lambda k: fetch(f"{BASE}/resolve/main/{files[k]}", f"data/{folder}/{os.path.basename(files[k])}"), chosen))
    print(f"data/{folder}: {len(chosen)} meshes")
