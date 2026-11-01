# Downloads the public test inputs of the 2D mesher's corpus run to corpus/data (git-ignored; nothing is committed).
# Only test fixtures (data) are taken from these projects, never code; sources and licenses are listed in
# docs/design/MESHING2D_PROVENANCE.md (section "Corpus") and copied next to the data as LICENSE-<source>.
#
#   python3 benchmarks/Meshing2Benchmark/corpus/fetch.py
#   dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- corpus
import json, os, sys, urllib.request

EARCUT = "https://raw.githubusercontent.com/mapbox/earcut/main"            # ISC
DELAUNATOR = "https://raw.githubusercontent.com/mapbox/delaunator/main"    # ISC
POLY2TRI = "https://raw.githubusercontent.com/jhasse/poly2tri/master"      # BSD-3-Clause

DELAUNATOR_FIXTURES = ["robustness1", "robustness2", "robustness3", "robustness4", "issue43", "issue44", "ukraine"]
POLY2TRI_FIXTURES = ["2", "bird", "custom", "debug", "debug2", "diamond", "dude", "funny", "kzer-za", "nazca_heron",
                     "nazca_monkey", "sketchup", "star", "strange", "tank", "test"]

os.chdir(os.path.dirname(os.path.abspath(__file__)))


def fetch(url, path):
    # to a temporary name first, so an interrupted download is not taken for a complete file
    if os.path.exists(path):
        return
    os.makedirs(os.path.dirname(path), exist_ok=True)
    urllib.request.urlretrieve(url, path + ".part")
    os.replace(path + ".part", path)


fetch(f"{EARCUT}/LICENSE", "data/LICENSE-earcut")
fetch(f"{DELAUNATOR}/LICENSE", "data/LICENSE-delaunator")
fetch(f"{POLY2TRI}/LICENSE", "data/LICENSE-poly2tri")

# earcut lists its fixtures in test/expected.json (a data file)
fetch(f"{EARCUT}/test/expected.json", "data/earcut-expected.json")
earcut = sorted(json.load(open("data/earcut-expected.json"))["triangles"].keys())
for name in earcut:
    fetch(f"{EARCUT}/test/fixtures/{name}.json", f"data/earcut/{name}.json")
for name in DELAUNATOR_FIXTURES:
    fetch(f"{DELAUNATOR}/test/fixtures/{name}.json", f"data/delaunator/{name}.json")
for name in POLY2TRI_FIXTURES:
    fetch(f"{POLY2TRI}/testbed/data/{name}.dat", f"data/poly2tri/{name}.dat")

print(f"corpus/data: {len(earcut)} earcut, {len(DELAUNATOR_FIXTURES)} delaunator, {len(POLY2TRI_FIXTURES)} poly2tri inputs", file=sys.stderr)
