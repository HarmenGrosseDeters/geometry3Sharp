# Downloads a reproducible selection of Thingi10K meshes (https://huggingface.co/datasets/Thingi10K/Thingi10K)
# for the UV atlas benchmark. No mesh files are committed; they stay in data/ (git-ignored).
#   data/meshes:       100 clean meshes (closed, manifold, one component), 50 open manifold meshes,
#                      50 manifold meshes with several components, 50 broken meshes (any defect); <= 100k faces
#   data/meshes_large: 20 clean meshes with 100k..500k faces (skip with --no-large)
# Thingi10K models carry individual licenses (mostly Creative Commons), see data/input_summary.csv.
import csv, json, os, random, re, sys, urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"
# dataset revision the selection was made on (October 2026), so that it stays the same
REV = "2d5d3b2f3cd3711028ad75b12788c13b25559ec6"
os.chdir(os.path.dirname(os.path.abspath(__file__)))
os.makedirs("data", exist_ok=True)

def fetch(url, path):
    # through a .part file, so that an interrupted download is fetched again
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path + ".part")
        os.replace(path + ".part", path)

for f in ["geometry_data.csv", "input_summary.csv"]:
    fetch(f"{BASE}/resolve/{REV}/metadata/{f}", f"data/{f}")

if not os.path.exists("data/raw_files.tsv"):
    url, out = f"{BASE.replace('/datasets/', '/api/datasets/')}/tree/{REV}/raw_meshes?limit=1000", []
    while url:
        r = urllib.request.urlopen(url)
        out += [(x["path"], x["size"]) for x in json.load(r)]
        m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
        url = m.group(1) if m else None
    open("data/raw_files.tsv", "w").write("".join(f"{p}\t{s}\n" for p, s in out))

g = {r["file_id"]: r for r in csv.DictReader(open("data/geometry_data.csv"))}
s = {r["ID"]: r for r in csv.DictReader(open("data/input_summary.csv"))}
files = {p.split("/")[1].split(".")[0]: p for p, _ in (l.rstrip("\n").split("\t") for l in open("data/raw_files.tsv"))}
defects = ["Closed", "Edge manifold", "Vertex manifold", "No degenerate faces", "No duplicated faces", "PWN"]

def faces(k): return int(g[k]["num_faces"])
def ok(k, lo, hi): return k in g and k in files and lo < faces(k) <= hi
def flags(k, true=(), false=()):
    return all(s[k][c] == "TRUE" for c in true) and all(s[k][c] == "FALSE" for c in false)

manifold = ["Edge manifold", "Vertex manifold", "No degenerate faces", "No duplicated faces"]
random.seed(42)
pools = [
    ("clean", 100, lambda k: flags(k, defects + ["Single Component"])),
    ("open", 50, lambda k: flags(k, manifold + ["Single Component"], ["Closed"])),
    ("multi", 50, lambda k: flags(k, manifold + ["Closed"], ["Single Component"])),
    ("broken", 50, lambda k: any(s[k][c] == "FALSE" for c in defects)),
]
sel = {}
for name, n, pred in pools:
    pool = sorted(k for k in s if ok(k, 0, 100000) and pred(k) and k not in sel)
    for k in random.sample(pool, min(n, len(pool))):
        sel[k] = name
random.seed(7)
pool = sorted(k for k in s if ok(k, 100000, 500000) and flags(k, defects + ["Single Component"]))
large = {k: "large" for k in random.sample(pool, min(20, len(pool)))}

selections = [("selection.csv", "meshes", sel)]
if "--no-large" not in sys.argv:
    selections.append(("selection_large.csv", "meshes_large", large))
for csvname, folder, chosen in selections:
    os.makedirs(f"data/{folder}", exist_ok=True)
    with open(f"data/{csvname}", "w") as f:
        f.write("file_id,path,category\n")
        for k, v in chosen.items():
            f.write(f"{k},{files[k]},{v}\n")
    with ThreadPoolExecutor(8) as ex:
        list(ex.map(lambda k: fetch(f"{BASE}/resolve/{REV}/{files[k]}", f"data/{folder}/{os.path.basename(files[k])}"), chosen))
    print(f"data/{folder}: {len(chosen)} meshes")
