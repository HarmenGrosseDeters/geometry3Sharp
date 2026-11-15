# #747 upper bound: which of the 800 sample wraps need a simplification retry on master d0298ad

Raw data only (never merged; no meshes). Mesh list `list800.txt` = sample800_2611 (as runs/aw3-rounds-800).

- `r800_master.jsonl` (108 rows, done by the implementing session): `benchmarks/MeshRepairBenchmark/run.sh`,
  `BENCH_WRAP_WARMUP=none BENCH_REPAIR=wrappipe BENCH_FIDELITY=none TIMEOUT=600`, soup; `simp` = the `simplify*` phases
  (a `simplify.retry*` key = the first pass was not certified). Times were taken 2-3 meshes in parallel: not comparable.
- `p800.jsonl` (rows so far; the job continues it): the probe `p747/` (one process: wrap at 300/3000, then the first
  simplification pass only, without retries, as in the pipeline: slabs 8, KeepClearOf = input). `certified` false = the
  pipeline would retry on that mesh. Uncertified raw wraps go to `raws/` locally (not committed).

## JOB-1 (runner): finish the sweep

One job at a time, alone on the machine. Commands from a checkout of the repository (any directory name; `$R` below):

```
R=$(pwd)                                   # the repository root
git fetch origin master runs/747-800
git checkout --detach d0298ad              # the library under test (master 2026-11-15)
git worktree add ../runs747 origin/runs/747-800   # this branch, next to the repository
J=$(cd ../runs747/747 && pwd)
apt-get install -y dotnet-sdk-10.0         # only if dotnet is missing (docs/QA.md section 5)
python3 benchmarks/MeshRepairBenchmark/fetch_data.py --sample 800     # about 40 s, 1.1 GB, data/ is gitignored
dotnet build $J/p747 -c Release -p:G3Root=$R
python3 -I -c "
import json; d={json.loads(l)['mesh'] for f in ['$J/r800_master.jsonl','$J/p800.jsonl'] for l in open(f)}
open('$J/rest.txt','w').write(''.join(m+'\n' for m in open('$J/list800.txt').read().split() if m not in d))"
mkdir -p $J/raws
```

Then repeat, each run in the foreground (resumable: it skips meshes already in `p800.jsonl`; 480 = seconds of budget
per chunk, it starts no new mesh after that), until `p800.jsonl` has a row for every mesh of `rest.txt`:

```
dotnet $J/p747/bin/Release/net10.0/ReducerBenchmark.dll $R/benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes $J/rest.txt $J/p800.jsonl 480 $J/raws
wc -l < $J/p800.jsonl
```

(About 13 meshes per minute on 4 cores; 585 meshes left, about 45 min.) A mesh that crashes or hangs: kill it, append
`{"mesh": "<id>", "status": "crash"}` to `p800.jsonl` and continue. Every hour and at the end: commit and push
`p800.jsonl` to `runs/747-800` (not `raws/`). Report: the output of `python3 -I $J/merge.py $J/r800_master.jsonl $J/p800.jsonl`.

## JOB-2 (runner, after JOB-1): timings of the retry meshes, alone on the machine

For each mesh `<id>` that merge.py lists (and 51705, already known), from `$R`, on d0298ad, one at a time:

```
dotnet build benchmarks/MeshRepairBenchmark -c Release
BENCH_REPAIR=wrappipe BENCH_FIDELITY=none TIMEOUT=600 benchmarks/MeshRepairBenchmark/run.sh benchmarks/MeshRepairBenchmark/data/sample800_2611/meshes/<id>.stl soup >> $J/retry_timings.jsonl
```

Three times each (BENCH_WRAP_WARMUP default = soup). Commit and push `retry_timings.jsonl`; report the file's rows.
