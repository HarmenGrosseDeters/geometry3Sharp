# Downloads Thingi10K raw meshes by id (https://huggingface.co/datasets/Thingi10K/Thingi10K) into <out dir>.
# The meshes carry their own licenses and are never committed.
#   python3 fetch.py <out dir> <id>...
import json, os, re, sys, urllib.request
BASE = "https://huggingface.co/datasets/Thingi10K/Thingi10K"
out = sys.argv[1]; ids = sys.argv[2:]
os.makedirs(out, exist_ok=True)
tsv = os.path.join(out, "raw_files.tsv")
if not os.path.exists(tsv):
    url, rows = f"{BASE.replace('/datasets/', '/api/datasets/')}/tree/main/raw_meshes?limit=1000", []
    while url:
        r = urllib.request.urlopen(url)
        rows += [x["path"] for x in json.load(r)]
        m = re.search(r'<([^>]+)>;\s*rel="next"', r.headers.get("Link") or "")
        url = m.group(1) if m else None
    open(tsv, "w").write("".join(p + "\n" for p in rows))
files = {p.split("/")[1].split(".")[0]: p for p in open(tsv).read().split()}
for i in ids:
    p = files[i]; dst = os.path.join(out, os.path.basename(p))
    if not os.path.exists(dst):
        urllib.request.urlretrieve(f"{BASE}/resolve/main/{p}", dst)
    print(dst)
