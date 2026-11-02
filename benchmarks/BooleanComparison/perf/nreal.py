#!/usr/bin/env python3
"""The n-ary class from real data ('nreal'): multi-component Thingi10K meshes, each split into its connected
components (one input each, the one-pass union of all: overlapping shells of one STL, merged before printing), and
arrays of one CGAL part with transforms. Writes $BDATA/cases/nreal/<id> and $BDATA/cases/narray/<part>-<n>
(N000.off, ...), the cases of perf/cases-nreal.txt. Nothing of this is part of the repository (licenses: Thingi10K
per model, data/input_summary.csv of the dataset; CGAL data as in data/fetch.py).
  python3 perf/nreal.py <tool dir> <cgal mesh dir>        (cgal mesh dir: <fetch.py target>/repo/Data/data/meshes)
Selection (reproducible): closed, edge-manifold Thingi10K meshes without duplicated faces, 3..40 connected components,
2k..100k faces and self-intersections (overlapping shells), 16 drawn with random.seed(2026); 613665 is dropped (an
OBJ whose vertices the reader does not weld: 25996 components)."""
import csv, json, os, random, re, subprocess, sys, urllib.request

BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"
ARRAYS = [("fandisk", 8), ("pinion", 8), ("anchor", 8)]

def fetch(url, path):
    if not os.path.exists(path):
        urllib.request.urlretrieve(url, path)

def main():
    tool, cgal = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
    bdata = os.environ.get("BDATA", os.path.expanduser("~/bdata"))
    src = os.path.join(bdata, "src", "thingi_multi")
    os.makedirs(src, exist_ok=True)
    for f in ["geometry_data.csv", "input_summary.csv"]:
        fetch(f"{BASE}/resolve/main/metadata/{f}", os.path.join(src, f))
    g = {r["file_id"]: r for r in csv.DictReader(open(os.path.join(src, "geometry_data.csv")))}
    cand = sorted(k for k, r in g.items() if 3 <= int(r["num_connected_components"]) <= 40
                  and 2000 <= int(r["num_faces"]) <= 100000 and int(r["num_self_intersections"]) > 0
                  and int(r["num_boundary_edges"]) == 0 and int(r["num_duplicated_faces"]) == 0 and r["edge_manifold"] == "1")
    random.seed(2026)
    sel = [k for k in random.sample(cand, 16) if k != "613665"]
    listing = os.path.join(src, "raw_files.tsv")
    if not os.path.exists(listing):
        url, out = f"{BASE.replace('/datasets/', '/api/datasets/')}/tree/main/raw_meshes?limit=1000", []
        while url:
            r = urllib.request.urlopen(url)
            out += [x["path"] for x in json.load(r)]
            m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
            url = m.group(1) if m else None
        open(listing, "w").write("\n".join(out) + "\n")
    files = {os.path.basename(p).split(".")[0]: p for p in open(listing).read().split()}
    dll = os.path.join(tool, "BooleanComparison.dll")
    for k in sel:
        mesh = os.path.join(src, os.path.basename(files[k]))
        fetch(f"{BASE}/resolve/main/{files[k]}", mesh)
        subprocess.run(["dotnet", dll, "components", mesh, os.path.join(bdata, "cases", "nreal", k)], check=True)
    for part, n in ARRAYS:
        subprocess.run(["dotnet", dll, "array", os.path.join(cgal, part + ".off"),
                        os.path.join(bdata, "cases", "narray", f"{part}-{n}"), str(n)], check=True)

if __name__ == "__main__":
    main()
