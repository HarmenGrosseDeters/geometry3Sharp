# Resumable runs: prints the mesh paths of a list (stdin) whose result is missing from any of the given jsonl files,
# after dropping, from every file, the lines of meshes that are not in all of them (an interrupted run, or a timing
# pair / triple cut off in the middle, is run again for every file). A line counts if it parses and has a "file".
# Usage: ls data/x/meshes/* | python3 pending.py results/a.jsonl [results/b.jsonl ...] | xargs ... >> results/a.jsonl
# Safety: an interrupted run leaves at most a few unpaired lines (one per parallel process). If the drop would remove more
# than --max-drop lines (default 8; eg a finished file next to a new or mistyped one), nothing is rewritten or printed
# and the script exits with 2; --force drops them anyway. Rows of timeouts and crashes ({"status": "timeout_or_crash"})
# count as done: they are not run again.
import json, os, sys

max_drop = int(sys.argv[sys.argv.index("--max-drop") + 1]) if "--max-drop" in sys.argv else 8
force = "--force" in sys.argv
files = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith("--") and sys.argv[i - 1] != "--max-drop"]
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
drops = [(f, sum(1 for name, _ in keep if name not in complete)) for f, _, keep in done]
if sum(d for _, d in drops) > max_drop and not force:
    sys.stderr.write("pending.py: would drop " + ", ".join(f"{d} lines of {f}" for f, d in drops if d)
                     + f" (more than --max-drop {max_drop}); nothing changed. Check the file names, or pass --force\n")
    sys.exit(2)
for f, names, keep in done:
    if os.path.exists(f) or keep:
        with open(f, "w") as o:
            o.writelines(line for name, line in keep if name in complete)
for path in sys.stdin:
    path = path.strip()
    if path and os.path.basename(path) not in complete:
        print(path)
