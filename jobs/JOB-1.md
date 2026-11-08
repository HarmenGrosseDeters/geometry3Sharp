# JOB-1: rule C (default-mode small-angle rule) against master, corpus and Herne

Written by the implementing session (Sonnet) for the Haiku runner. Run it literally; ask the implementing session if anything differs
(a command fails, a path is missing, a placeholder below is not filled). No code, docs or PRs; results only, on this branch.

**Placeholders (the implementing session fills them before sending; if one still reads `<...>`, stop and ask):**
- BASE = `<BASE_SHA>`  (master just before PR #441: the first parent of the merge commit)
- CAND = `<CAND_SHA>`  (the merge commit of PR #441 on master)

Repository: https://github.com/HarmenGrosseDeters/geometry3Sharp (clone it if it is not there). This data branch is `runs/meshing2d-rule-c`
(never merged; no meshes, no foreign outputs on it). Use a worktree for the data branch so that checkouts of BASE and CAND do not touch it:
`git worktree add ../runs-wt runs/meshing2d-rule-c`  (relative to the repository; results go to `../runs-wt/results/`).

Machine: nothing else may run while the two corpus runs are timed (they are compared by time). One job at a time.

## 0. Setup (once)

```
apt-get update && apt-get install -y dotnet-sdk-10.0      # skip if `dotnet --version` is 10.x already
python3 benchmarks/Meshing2Benchmark/corpus/fetch.py        # public fixtures, git-ignored (run it from a checkout of CAND)
```

## 1. Corpus, default mode, 20 and 30 degrees: base then candidate (same machine, same job)

For X in BASE, CAND (in this order; the output file name in brackets):

```
git checkout <X sha>
dotnet build benchmarks/Meshing2Benchmark/Meshing2Benchmark.csproj -c Release -f net10.0 -o /tmp/bm_<base|cand>
python3 benchmarks/Meshing2Benchmark/corpus/fetch.py
dotnet /tmp/bm_<base|cand>/Meshing2Benchmark.dll corpus --angles 20,30 --seconds 120 --judge-limit 60000 \
  --dir benchmarks/Meshing2Benchmark/corpus/data > ../runs-wt/results/corpus_<base|cand>.txt 2>&1
```

Run it as a harness-tracked background task (the Bash tool's `run_in_background`, timeout at most 2 h; never `nohup`). A run takes about
an hour (the oracle dominates). If the process is killed or the machine restarts, rerun that side from the start (the output file is
overwritten) and say so in the report. Push partial results to this branch about every hour (`git add results; git commit; git push`).
Keep a self check-in every 15 minutes (`send_later`) while a job runs; on every wake check that the process still runs and its output grows.

## 2. Analysis

```
python3 ../runs-wt/jobs/analyze_corpus.py ../runs-wt/results/corpus_base.txt ../runs-wt/results/corpus_cand.txt > ../runs-wt/results/corpus_summary.txt
cat ../runs-wt/results/corpus_summary.txt
```

## 3. Herne default mode (only if the prepared tiles are on this machine)

`benchmarks/Meshing2Benchmark/herne_ratchet.py` takes `--inputs <dir>` with the prepared ALKIS tiles (snap 0.01 m); they are not in the
repository. If you do not have the directory, skip this step and say so in the report (do not search for or download the tiles). If you have
it: run `python3 benchmarks/Meshing2Benchmark/herne_ratchet.py --inputs <dir>` from a checkout of BASE and of CAND (build as in step 1) and
save both outputs to `../runs-wt/results/herne_base.txt` and `herne_cand.txt`. Rule C is off on the snap grid, so the two are expected to be
identical; the point is to confirm that.

## 4. Report

Commit and push `results/` to this branch. Then send, with the send_message tool of the claude-code-remote MCP server:
1. to the implementing session (session_01AaGT59D1meuiE9gdHxCag1): the content of `corpus_summary.txt` (verdict counts of both sides,
   every verdict change, the triangle table, the time sums, the Steiner-limit lists) and the Herne result or "skipped, no tiles";
2. to the coordinator (session_01M4BQDeN5rS3AzKxoEWqYuU): one line: "JOB-1 done: <number of verdict changes> verdict changes, <median triangle ratio>,
   details sent to the implementing session, raw results on runs/meshing2d-rule-c".
Then pause (no check-ins, no polling) until a session messages you.
