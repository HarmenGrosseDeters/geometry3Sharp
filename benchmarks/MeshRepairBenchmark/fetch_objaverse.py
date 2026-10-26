# Downloads a reproducible, license-filtered selection of Objaverse 1.0 objects
# (https://huggingface.co/datasets/allenai/objaverse, Sketchfab models as glb) as a second corpus for the
# benchmark: game, scan and CAD exports, which are broken differently from the 3D-print meshes of Thingi10K.
#   data/objaverse/meshes/<uid>.glb   the selection (default 300 objects)
#   data/objaverse/manifest.csv       uid, license, source URL and attribution of every selected object
# Reproducible: the dataset revision is pinned, the metadata shards and the objects are drawn with fixed seeds.
# Licenses: the collection is ODC-By; every object has its own license. Only CC0 and CC-BY objects are selected
# (no NC, ND or SA variants). Attribution of CC-BY objects: see the manifest; nothing of this goes into the repository.
# Options: --count N (objects), --shards N (metadata shards of 5000 objects to draw from), --seed N
import argparse, csv, gzip, json, os, random, urllib.request
from concurrent.futures import ThreadPoolExecutor

REVISION = "556637099bf4fa79ea7b239d0d9c328b8a2e9ac8"     # allenai/objaverse, 2026-10-08
BASE = f"https://huggingface.co/datasets/allenai/objaverse/resolve/{REVISION}"
LICENSES = {"cc0": "CC0 1.0", "by": "CC BY 4.0"}
MIN_FACES, MAX_FACES = 500, 100000

ap = argparse.ArgumentParser()
ap.add_argument("--count", type=int, default=300)
ap.add_argument("--shards", type=int, default=4)
ap.add_argument("--seed", type=int, default=42)
a = ap.parse_args()

os.chdir(os.path.dirname(os.path.abspath(__file__)))
root = "data/objaverse"
os.makedirs(f"{root}/metadata", exist_ok=True)
os.makedirs(f"{root}/meshes", exist_ok=True)

def fetch(url, path):
    if not os.path.exists(path):
        tmp = path + ".part"
        urllib.request.urlretrieve(url, tmp)
        os.replace(tmp, path)

# the metadata shards 000-000 .. 000-159 hold 5000 objects each, the glbs/<shard>/ folders the matching models
rnd = random.Random(a.seed)
shards = sorted(rnd.sample([f"000-{i:03d}" for i in range(160)], a.shards))
candidates = []
for sh in shards:
    path = f"{root}/metadata/{sh}.json.gz"
    fetch(f"{BASE}/metadata/{sh}.json.gz", path)
    for uid, m in sorted(json.load(gzip.open(path)).items()):
        if (m.get("license") in LICENSES and not m.get("isAgeRestricted")
                and MIN_FACES <= (m.get("faceCount") or 0) <= MAX_FACES):
            candidates.append((sh, uid, m))
chosen = rnd.sample(candidates, min(a.count, len(candidates)))
print(f"{len(candidates)} CC0 / CC-BY objects with {MIN_FACES}..{MAX_FACES} faces in shards {', '.join(shards)}; {len(chosen)} chosen")

with open(f"{root}/manifest.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["uid", "file", "license", "faces", "name", "author", "source", "attribution"])
    for sh, uid, m in chosen:
        user = m.get("user") or {}
        author = user.get("displayName") or user.get("username") or ""
        w.writerow([uid, f"meshes/{uid}.glb", LICENSES[m["license"]], m.get("faceCount"), m.get("name", ""), author,
                    m.get("viewerUrl", ""),
                    f"\"{m.get('name', '')}\" by {author} ({user.get('profileUrl', '')}), {LICENSES[m['license']]}"])

with ThreadPoolExecutor(8) as ex:
    list(ex.map(lambda c: fetch(f"{BASE}/glbs/{c[0]}/{c[1]}.glb", f"{root}/meshes/{c[1]}.glb"), chosen))
print(f"{root}/meshes: {len(chosen)} objects, attribution in {root}/manifest.csv")
