# JOB-2: PR #666 (#664, constraints that meet within the tolerance) against its base, public corpus

Written by the implementing session for a runner. Run it literally; ask the implementing session if anything differs (a command
fails, a path is missing). No code, docs or PRs; results only, on this branch.

- BASE = `eaf073d110b21a765a543a756a14e275a7faf386`  (master, the merge base of PR #666)
- CAND = `057a91342c9c3d283593931305c943b266ac053d`  (head of PR #666)

Repository: https://github.com/HarmenGrosseDeters/geometry3Sharp. This data branch is `runs/meshing2d-near-meet` (never merged;
no meshes, no foreign outputs). Use a worktree for it: `git worktree add ../runs-wt runs/meshing2d-near-meet`.

Machine: nothing else may run while the corpus runs are timed. One job at a time.

## 0. Setup (once)

    apt-get update && apt-get install -y dotnet-sdk-10.0      # skip if dotnet --version is 10.x already

## 1. Builds

For X in BASE, CAND: `git checkout <X sha>`, then
`dotnet build benchmarks/Meshing2Benchmark/Meshing2Benchmark.csproj -c Release -f net10.0 -o /tmp/bm_<base|cand>`.
Then, from the CAND checkout: `python3 benchmarks/Meshing2Benchmark/corpus/fetch.py` (public fixtures, git-ignored).

## 2. Corpus runs, 20 and 30 degrees (four runs, in this order)

    dotnet /tmp/bm_base/Meshing2Benchmark.dll corpus --angles 20,30 --seconds 120 --judge-limit 60000 --dir benchmarks/Meshing2Benchmark/corpus/data > ../runs-wt/results/corpus_base.txt 2>&1
    dotnet /tmp/bm_cand/Meshing2Benchmark.dll corpus --angles 20,30 --seconds 120 --judge-limit 60000 --dir benchmarks/Meshing2Benchmark/corpus/data > ../runs-wt/results/corpus_cand.txt 2>&1
    dotnet /tmp/bm_base/Meshing2Benchmark.dll corpus --angles 20,30 --seconds 120 --judge-limit 60000 --mode constrained --dir benchmarks/Meshing2Benchmark/corpus/data > ../runs-wt/results/corpus_cq_base.txt 2>&1
    dotnet /tmp/bm_cand/Meshing2Benchmark.dll corpus --angles 20,30 --seconds 120 --judge-limit 60000 --mode constrained --dir benchmarks/Meshing2Benchmark/corpus/data > ../runs-wt/results/corpus_cq_cand.txt 2>&1

Each as a harness-tracked background task (timeout at most 2 h; never nohup). A run took about 2 minutes in JOB-1 of #661, so about 10 minutes in all. If a process is killed, rerun that run from the start and say so. Push partial results about every hour
(`git add results; git commit; git push`).

## 3. Analysis

    python3 ../runs-wt/jobs/analyze_corpus.py ../runs-wt/results/corpus_base.txt ../runs-wt/results/corpus_cand.txt > ../runs-wt/results/summary_default.txt
    python3 ../runs-wt/jobs/analyze_corpus.py ../runs-wt/results/corpus_cq_base.txt ../runs-wt/results/corpus_cq_cand.txt > ../runs-wt/results/summary_constrained.txt

Then the count of the new excuse:

    python3 ../runs-wt/jobs/count_near_meet.py ../runs-wt/results/corpus_cand.txt ../runs-wt/results/corpus_cq_cand.txt > ../runs-wt/results/near_meet.txt

## 4. Report

Commit and push `results/`. Then, with the send_message tool of the claude-code-remote MCP server:
1. to the implementing session (session_01AaGT5BwFKGL9MeyCre9ry2): both summary files and near_meet.txt in full;
2. to the coordinator (session_01M4BQDeN5rS3AzKxoEWqYuU): one line: "JOB-2 #666 done: <verdict changes default / constrained>, details sent".
Then pause until a session messages you.
