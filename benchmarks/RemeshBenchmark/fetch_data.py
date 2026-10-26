# Downloads a reproducible selection of clean Thingi10K meshes (https://huggingface.co/datasets/Thingi10K/Thingi10K)
# to data/meshes: 20 closed, manifold, single-component meshes with 2k..50k faces (seed 42).
# Thingi10K models carry individual licenses (mostly Creative Commons), see data/input_summary.csv; nothing is
# committed.
import csv, json, os, random, re, urllib.request
from concurrent.futures import ThreadPoolExecutor

BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"
os.chdir(os.path.dirname(os.path.abspath(__file__)))
os.makedirs("data/meshes", exist_ok=True)

def fetch(url, path):
    # to a temporary name first, so an interrupted download is not taken for a complete file
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path + ".part")
        os.replace(path + ".part", path)

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
cats = ["Closed", "Edge manifold", "Vertex manifold", "No degenerate faces", "No duplicated faces", "PWN", "Single Component"]

random.seed(42)
pool = sorted(k for k in s if k in g and k in files and 2000 <= int(g[k]["num_faces"]) <= 50000
              and all(s[k][c] == "TRUE" for c in cats))
chosen = random.sample(pool, 20)
with open("data/selection.csv", "w") as f:
    f.write("file_id,path,faces\n")
    for k in chosen:
        f.write(f"{k},{files[k]},{g[k]['num_faces']}\n")
with ThreadPoolExecutor(8) as ex:
    list(ex.map(lambda k: fetch(f"{BASE}/resolve/main/{files[k]}", f"data/meshes/{os.path.basename(files[k])}"), chosen))
print(f"data/meshes: {len(chosen)} meshes")
