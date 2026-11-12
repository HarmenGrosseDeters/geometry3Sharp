# JOB-1: PR #661 (encroached O11-unsplittable piece off the grid) against its base, public corpus

Written by the implementing session for a runner. Run it literally; ask the implementing session if anything differs (a command
fails, a path is missing). No code, docs or PRs; results only, on this branch.

- BASE = `04f81eaf2f790172c38884c78f9cea44938461e6`  (master, the merge base of PR #661)
- CAND = `4ee0d12b0db9dfc0ea6a7f22a299c3b312e4773f`  (head of PR #661)

Repository: https://github.com/HarmenGrosseDeters/geometry3Sharp. This data branch is `runs/meshing2d-unsplittable-encroach` (never merged;
no meshes, no foreign outputs). Use a worktree for it: `git worktree add ../runs-wt runs/meshing2d-unsplittable-encroach`.

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

Each as a harness-tracked background task (timeout at most 2 h; never nohup). A run takes about an hour (the oracle dominates), so
about 4 h in all. If a process is killed, rerun that run from the start and say so. Push partial results about every hour
(`git add results; git commit; git push`).

## 3. Analysis

    python3 ../runs-wt/jobs/analyze_corpus.py ../runs-wt/results/corpus_base.txt ../runs-wt/results/corpus_cand.txt > ../runs-wt/results/summary_default.txt
    python3 ../runs-wt/jobs/analyze_corpus.py ../runs-wt/results/corpus_cq_base.txt ../runs-wt/results/corpus_cq_cand.txt > ../runs-wt/results/summary_constrained.txt

## 4. Report

Commit and push `results/`. Then, with the send_message tool of the claude-code-remote MCP server:
1. to the implementing session (session_01AaGT5BwFKGL9MeyCre9ry2): both summary files in full;
2. to the coordinator (session_01M4BQDeN5rS3AzKxoEWqYuU): one line: "JOB-1 #661 done: <verdict changes default / constrained>, details sent".
Then pause until a session messages you.
