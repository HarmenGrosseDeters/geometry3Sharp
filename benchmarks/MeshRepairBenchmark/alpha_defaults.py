#!/usr/bin/env python3
"""Driver of the alpha-wrap defaults measurement (#736 / #846), run by .github/workflows/alpha-defaults.yml.

Three configurations of the wrap pipeline (BENCH_REPAIR=wrappipe, alpha = diagonal / 300, offset = diagonal / 3000,
full fidelity) over the 800-mesh Thingi10K sample (cases/alpha_defaults_list800.txt = sample800_2611, the list of the
earlier 800-mesh sweeps):
  A: library defaults;  B: A + BENCH_WRAP_FEATURES=1 (#736);  C: A + BENCH_WRAP_BUDGET=300000 (#846);
  D: C + BENCH_WRAP_COARSEN_CAP=1.5;  E: C + BENCH_WRAP_COARSEN_CAP=1.2 (the limit on the coarsening, MaxAlphaCoarsening);
  F: A + BENCH_WRAP_PULL=1 (MeshAlphaWrap.PullPinchPoints, the quality-aware manifold step).

  alpha_defaults.py select  <list> <shard> <shards> [<first>:<last>]   print the ids of one shard (round robin over the range)
  alpha_defaults.py run     <ids-file> <out-dir> [--configs ABC] [--timeout 400] [--deadline-min 0]
                            one process per (mesh, config), interleaved per mesh; resumable (rows already in <out-dir>/raw are
                            skipped); stops starting new meshes after --deadline-min minutes (0: no deadline)
  alpha_defaults.py merge   <out-dir> <shard-dir>...                  merge the raw rows and wall.tsv of the shards into <out-dir>

Mesh files are read from data/ids/meshes (fetch_data.py --ids). Nothing here fetches from the network.
"""
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG_ENV = {"A": {}, "B": {"BENCH_WRAP_FEATURES": "1"}, "C": {"BENCH_WRAP_BUDGET": "300000"},
              # the budget with a limit on the coarsening (MeshAlphaWrap.MaxAlphaCoarsening, #846 follow-up, owner 2026-11-17)
              "D": {"BENCH_WRAP_BUDGET": "300000", "BENCH_WRAP_COARSEN_CAP": "1.5"},
              "E": {"BENCH_WRAP_BUDGET": "300000", "BENCH_WRAP_COARSEN_CAP": "1.2"},
              # the quality-aware manifold step (MeshAlphaWrap.PullPinchPoints, docs/design/ALPHA_WRAP_MANIFOLD_QUALITY.md)
              "F": {"BENCH_WRAP_PULL": "1"}}
UNSET = ("BENCH_WRAP_FEATURES", "BENCH_WRAP_BUDGET", "BENCH_WRAP_COARSEN_CAP", "BENCH_WRAP_PULL", "BENCH_FIDELITY")


def read_ids(path):
    return [x.strip() for x in open(path) if x.strip()]


def select(ids, shard, shards, rng=None):
    """Round robin shard `shard` of `shards` over the range 'first:last' (0-based, last exclusive) of the list."""
    if shards < 1 or not 0 <= shard < shards:
        raise ValueError("shard must be in [0, shards)")
    first, last = 0, len(ids)
    if rng:
        a, b = rng.split(":")
        first, last = int(a or 0), int(b or len(ids))
        if not 0 <= first <= last <= len(ids):
            raise ValueError(f"range {rng} is not within 0..{len(ids)}")
    return ids[first:last][shard::shards]


def done_files(path):
    seen = set()
    if os.path.exists(path):
        for line in open(path):
            line = line.strip()
            if line:
                try:
                    seen.add(json.loads(line)["file"])
                except (ValueError, KeyError):
                    pass  # a torn last line: the pair runs again
    return seen


def run_one(mesh, cfg, timeout, bench_dir=HERE):
    """One process like run.sh: the last stdout line is the row; none (timeout or crash) gives a status row."""
    env = {k: v for k, v in os.environ.items() if k not in UNSET}
    env.update(CONFIG_ENV[cfg])
    env.update(BENCH_REPAIR="wrappipe", TIMEOUT=str(timeout))
    t0 = time.monotonic()
    out = subprocess.run([os.path.join(bench_dir, "run.sh"), mesh], env=env, capture_output=True, text=True).stdout
    wall = time.monotonic() - t0
    lines = [x for x in out.splitlines() if x.strip()]
    row = json.dumps({"file": os.path.basename(mesh), "status": "timeout_or_crash"})
    if lines:
        try:
            if json.loads(lines[-1])["file"]:
                row = lines[-1]
        except (ValueError, KeyError, TypeError):
            pass  # a non-JSON last line (a crashed process): a status row
    return row, wall


def cmd_run(args):
    ids_file, out = args[0], args[1]
    opt = dict(zip(args[2::2], args[3::2]))
    unknown = set(opt) - {"--configs", "--timeout", "--deadline-min"}
    if unknown or len(args) % 2:
        sys.exit(f"unknown or incomplete options: {sorted(unknown)}")
    cfgs, timeout = opt.get("--configs", "ABC"), int(opt.get("--timeout", "400"))
    deadline = float(opt.get("--deadline-min", "0")) * 60
    meshes = os.path.join(HERE, "data", "ids", "meshes")
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    start = time.monotonic()
    for mid in read_ids(ids_file):
        if deadline and time.monotonic() - start > deadline:
            print(f"deadline reached before {mid}; the rest is left for a resumed run", flush=True)
            break
        f = mid + ".stl"
        for cfg in cfgs:
            raw = os.path.join(out, "raw", cfg + ".jsonl")
            if f in done_files(raw):
                continue
            row, wall = run_one(os.path.join(meshes, f), cfg, timeout)
            with open(raw, "a") as w:
                w.write(row + "\n")
            with open(os.path.join(out, "wall.tsv"), "a") as w:
                w.write(f"{f}\t{cfg}\t{wall:.2f}\n")
        print(f"{mid} done ({time.monotonic() - start:.0f} s)", flush=True)


def cmd_merge(args):
    out, shards = args[0], args[1:]
    os.makedirs(os.path.join(out, "raw"), exist_ok=True)
    for cfg in CONFIG_ENV:
        seen, rows = set(), []
        for d in shards:
            p = os.path.join(d, "raw", cfg + ".jsonl")
            if not os.path.exists(p):
                continue
            for line in open(p):
                if not line.strip():
                    continue
                try:
                    r = json.loads(line)
                    name = r["file"]
                except (ValueError, KeyError, TypeError):
                    print(f"merge: skipped an unparsable row in {p}", file=sys.stderr)
                    continue
                if name not in seen:  # the first row of a mesh wins (a resumed shard)
                    seen.add(name)
                    rows.append(line.rstrip("\n"))
        rows.sort(key=lambda l: json.loads(l)["file"])
        if not rows and cfg not in "ABC":
            continue  # (a configuration that was not run: no file, so the summary does not list it)
        with open(os.path.join(out, "raw", cfg + ".jsonl"), "w") as w:
            w.write("".join(l + "\n" for l in rows))
    walls = {}
    for d in shards:
        p = os.path.join(d, "wall.tsv")
        if os.path.exists(p):
            for line in open(p):
                parts = line.rstrip("\n").split("\t")
                if len(parts) == 3:
                    walls.setdefault((parts[0], parts[1]), parts[2])
    with open(os.path.join(out, "wall.tsv"), "w") as w:
        for (f, c), s in sorted(walls.items()):
            w.write(f"{f}\t{c}\t{s}\n")


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    cmd, args = argv[1], argv[2:]
    if cmd == "select":
        print("\n".join(select(read_ids(args[0]), int(args[1]), int(args[2]), args[3] if len(args) > 3 else None)))
    elif cmd == "run":
        cmd_run(args)
    elif cmd == "merge":
        cmd_merge(args)
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv)
