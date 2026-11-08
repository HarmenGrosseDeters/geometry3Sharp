# JOB-1: the rounding mutation shard on the lever build (sizing and validation)

For the local runner (the owner's i7-8700K, 6 cores / 12 threads, 16 GB). You run jobs only: no code or doc edits, no PRs,
no decisions. Ask instead of improvising. Results go to the data branch `runs/mutation-local` (never merged).
Keep the machine otherwise idle during a run (the timings are the result). Times in the report: UTC.

## Why
In CI the rounding shard (`MeshRoundingRepair.cs`, 147 tests) did not fit the 350 min limit: the coverage pass alone took
about 200 min per part (and the same for every part of a split), the tests of a mutant 23-26 s on 4 vCPU with 2
parallel hosts. We need, on the build the combined gate will use (the lever build, commit 53665116): (A) the real time of
one of 7 equal parts, (B) how the coverage pass scales with the test filter, (C) whether a reduced filter keeps the kills.

## 0. Preconditions
.NET 10 SDK (`dotnet --version` 10.0.x), git, python3, 30 GB free disk, nothing else heavy running.
Record: `nproc; free -m; lscpu | grep 'Model name'; dotnet --version` into `results/machine.txt`.

## 1. Checkout (LF line endings matter: Stryker's spans are character offsets)
```
git config --global core.autocrlf false
git clone https://github.com/HarmenGrosseDeters/geometry3Sharp.git g3s && cd g3s
git fetch origin runs/b2-all runs/mutation-local
git checkout --detach 53665116
git log -1 --format=%s        # expect: Merge remote-tracking branch 'origin/claude/tritri-prefilter' into runs/b2-all
git archive origin/runs/mutation-local jobs | tar -x      # jobs/ without touching the index
grep -c $'\r' mesh_ops/MeshRoundingRepair.cs               # expect 0
dotnet tool restore                                        # dotnet-stryker 5.0.0 (.config/dotnet-tools.json)
dotnet build tests/geometry3Sharp.Tests -c Release -f net10.0   # expect 0 errors
mkdir -p results
```

## 2. The parts
```
G3_CONCURRENCY=3 python3 jobs/tools/make_parts.py 7
```
Expect `jobs/parts/part-1-of-7.json` .. `part-7-of-7.json` (about 340-390 proxy units each, the last about 240) and the file
size 102808 characters. Each part is the shard's config with `mutate` limited to a span between two method starts, `break` 0,
`concurrency` 3.

## 3. How to run one label (as CI does: in tests/mutation, with the config next to it)
```
export DOTNET_NOLOGO=1 DOTNET_GCHeapHardLimit=0x100000000      # 4 GB per test host (3 hosts = 12 of 16 GB)
run_one() {   # run_one <label> <config file>
  label=$1; cfg=$2
  [ -f results/$label.exit ] && { echo "$label done, skipping"; return; }
  rm -rf tests/mutation/StrykerOutput; cp "$cfg" tests/mutation/job1-$label.json
  date -u +%FT%TZ > results/$label.start
  ( cd tests/mutation && dotnet stryker --config-file job1-$label.json --log-to-file ); echo "exit=$?" > results/$label.exit
  date -u +%FT%TZ > results/$label.end
  python3 jobs/tools/summarize.py $label results/$label.json
  cp tests/mutation/StrykerOutput/*/reports/mutation-report.json results/$label-report.json 2>/dev/null
}
```
Run it as a harness-tracked background task, not under nohup. Watch progress with
`ls tests/mutation/StrykerOutput/*/logs | grep -c Runner` (test runs started) and the newest `log-*.txt` (the line
"N total mutants will be tested" marks the end of the coverage pass).

## 4. The labels, in this order
**A (required)**: `run_one A-part4 jobs/parts/part-4-of-7.json`. Full run, perTest as in CI. Expect 170-200 mutants; the
coverage pass 1-3 h on this machine, then 20-40 s per mutant. Record elapsed, capture minutes, statuses, score.

**B1 and B2 (capture time per filter; stop each run when "total mutants will be tested" appears)**:
```
mk_filter() {   # mk_filter <label> <filter>
  python3 - "$1" "$2" <<'PY'
import json,sys
c=json.load(open('jobs/parts/part-4-of-7.json')); c['stryker-config']['test-case-filter']=sys.argv[2]
json.dump(c,open('jobs/parts/part-4-'+sys.argv[1]+'.json','w'),indent=2)
PY
}
F1='FullyQualifiedName~MeshRoundingRepairTests|FullyQualifiedName~MeshRoundingRepairStrategyTests|FullyQualifiedName~SafeRoundingBoundTests|FullyQualifiedName~RoundingDirtyTreeTests|FullyQualifiedName~RoundingExactPairsTests|FullyQualifiedName~MeshRoundingRepairIncrementalTests'
F2="$F1|FullyQualifiedName~ExactBooleanIssueCasesTests"
mk_filter F1 "$F1"; mk_filter F2 "$F2"
```
Then `run_one B1-F1 jobs/parts/part-4-F1.json` and `run_one B2-F2 jobs/parts/part-4-F2.json`, each one killed
(`pkill -f dotnet-stryker`) as soon as the log shows "total mutants will be tested"; the exit file then shows a kill code, that is fine.
`summarize.py` gives `created_at` and `capture_end_at`: the difference is the capture time. Note the test count (line
"Number of tests found") of each filter.

**C (optional, only if A, B1 and B2 are done and you are asked)**: `run_one C-part4-F1 jobs/parts/part-4-F1.json` as a full run;
its statuses per mutant are compared with A afterwards (the question: does F1 keep A's kills?).

## 5. What to record and push
For each label: `results/<label>.json` (summary), `.start`, `.end`, `.exit`, and the report `results/<label>-report.json`
(a few MB; no meshes anywhere). Also `results/machine.txt`. Push after each label:
```
git fetch origin runs/mutation-local
git worktree add -B runs/mutation-local ../g3s-results origin/runs/mutation-local 2>/dev/null || true
cp results/* ../g3s-results/results/ && cd ../g3s-results && git add results && git commit -m "J1: <label>" && git push origin runs/mutation-local; cd ../g3s
```
No PR, no merge. Then send ONE line to the coordinator (session_01M4BQDeN5rS3AzKxoEWqYuU) and the analyst
(session_01AaGT59x9M1JVRLVyu3UVMh): label, elapsed, capture minutes, killed/timeout/survived/no coverage, score.

## 6. Resumability
Each label is independent: a label with `results/<label>.exit` is skipped by `run_one`. Stryker cannot resume inside a run: after a
restart delete `tests/mutation/StrykerOutput` and rerun that label from its start. Do not change the commit between labels.

## 7. Expected, for orientation (CI, 4 vCPU, 2 hosts, the slower old code)
Coverage pass 196-201 min for the whole 147-test filter; 23-26 s per mutant; a timeout counts as detected and varies between
runs. If this machine shows much smaller or larger numbers, report them: that is the finding, not a failure.
