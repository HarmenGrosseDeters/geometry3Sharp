# Converts a `MeshFuzzPilot dump` text (round-trip coordinates) into a BooleanComparison case directory (A.off, B.off).
#   python3 dump2off.py <dump.txt> <case dir>
# The coordinates are copied as text, so the OFF reader parses the same doubles. Prints the operation of the dump.
import os, re, sys
src, out = sys.argv[1], sys.argv[2]
lines = open(src).read().splitlines()
op = re.match(r"op (\d+) \((\w+)\)", lines[0]).group(2)
meshes, cur = {}, None
for l in lines[1:]:
    m = re.match(r"([AB]) vertices:", l)
    if m: cur = m.group(1); meshes[cur] = ([], []); continue
    m = re.match(r"([AB]) triangles: (.*)", l)
    if m: meshes[m.group(1)][1].extend(re.findall(r"\((\d+),(\d+),(\d+)\)", m.group(2))); cur = None; continue
    m = re.match(r"\s+\d+: (\S+) (\S+) (\S+)$", l)
    if m and cur: meshes[cur][0].append(m.groups())
os.makedirs(out, exist_ok=True)
for k, (v, t) in meshes.items():
    with open(os.path.join(out, k + ".off"), "w") as f:
        f.write(f"OFF\n{len(v)} {len(t)} 0\n")
        f.writelines(" ".join(p) + "\n" for p in v)
        f.writelines("3 " + " ".join(p) + "\n" for p in t)
print(op)
