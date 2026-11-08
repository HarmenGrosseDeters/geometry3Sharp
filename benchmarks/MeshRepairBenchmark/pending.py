# Resumable runs: prints the mesh paths of a list (stdin) whose result is missing from any of the given jsonl files,
# after dropping, from every file, the lines of meshes that are not in all of them (an interrupted run, or a timing
# pair / triple cut off in the middle, is run again for every file). A line counts if it parses and has a "file".
# Usage: ls data/x/meshes/* | python3 pending.py results/a.jsonl [results/b.jsonl ...] | xargs ... >> results/a.jsonl
import json, os, sys

files = sys.argv[1:]
done = []
for f in files:
    names, keep = set(), []
    if os.path.exists(f):
        for line in open(f):
            try:
                r = json.loads(line)
            except ValueError:
                continue
            if "file" in r:
                names.add(r["file"])
                keep.append((r["file"], line if line.endswith("\n") else line + "\n"))
    done.append((f, names, keep))
complete = set.intersection(*(n for _, n, _ in done)) if done else set()
for f, names, keep in done:
    if os.path.exists(f) or keep:
        with open(f, "w") as o:
            o.writelines(line for name, line in keep if name in complete)
for path in sys.stdin:
    path = path.strip()
    if path and os.path.basename(path) not in complete:
        print(path)
