# summary of the #747 upper-bound sweep: python3 -I merge.py r800_master.jsonl p800.jsonl
# r800_master rows: MeshRepairBenchmark wrappipe (retry = a "simplify.retry*" phase); p800 rows: p747 probe (certified = first pass certified)
import json, sys
rows = {}
for l in open(sys.argv[1]):
    x = json.loads(l); rows[x["mesh"]] = ("bench", any("retry" in k for k in x["simp"]) or x["status"] != "closed", x)
for l in open(sys.argv[2]):
    x = json.loads(l); rows.setdefault(x["mesh"], ("probe", not x.get("certified", False), x))
need = sorted(m for m, (_, r, _) in rows.items() if r)
print(f"meshes: {len(rows)} / 800; first pass not certified: {len(need)}: {' '.join(need)}")
for m in need:
    src, _, x = rows[m]
    print(m, src, json.dumps(x))
