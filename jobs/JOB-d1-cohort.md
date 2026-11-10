# JOB d1-cohort: D1 reference rule, cohort run of the Thingi10K target class (16 239 operations)

Owner of the job: the D1 build session (library PR: see #513 below). Runner: the Haiku runner, queued after the
Boolean 2.0 confirmation run. Execute literally; ask instead of improvising.

## What

Every model of the Thingi10K target class of the Boolean 2.0 final run (5413 models, `runs/d1/target_ids.txt`, from
`runs/boolean2-final` `runs/final/results/class.tsv`: closed, no self-intersection), with its #385 pairing (B = A
rotated 20 degrees about (1, 1, 0) and shifted by a quarter of the box, as `BooleanComparison export` makes it),
union / intersection / difference, SafeRounding on. Two builds per model, one after the other, on the same export,
on this machine:

- base: `f2245b6fc632fd4997521adbef158c5607c11ce8` (master, the PR's merge base: the apex rule)
- cand: `c623df4ce76fcc51ca58736618e0adbd8ce89572` (the head of PR #513: the reference rule)

Per operation and build one JSON row (`runs/d1/D1Runner`): ok, status, RoundingClean, NotCleanReason, the remaining
degenerate triangles and intersections, MaxDrift / MaxLength, SmallestRejectedDrift / MaxLength, the edit counts,
EvaluationLimitReached, the ms of the operation and of the rounding, a SHA-256 of the result mesh.

## Steps

1. `git fetch origin runs/d1-cohort && git checkout runs/d1-cohort` (a fresh clone or worktree of the repository).
2. Smoke run (about 10 minutes): `head -3 runs/d1/target_ids.txt > /tmp/d1-smoke.txt && IDS_FILE=/tmp/d1-smoke.txt
   bash runs/d1/job.sh $HOME/d1-smoke` then `python3 runs/d1/analyze.py $HOME/d1-smoke/out`. Expected: 9 rows per
   build, no `error` or `exception` field. If not, stop and report the output.
3. The run: `bash runs/d1/job.sh $HOME/d1-cohort-run`, as a harness-tracked background task of at most 2 h per chunk;
   when a chunk ends (or the machine restarts), run the same command again: finished models are skipped, an
   interrupted model is run again for both builds. Nothing else runs on the machine meanwhile.
4. About every hour (between chunks): `mkdir -p runs/d1/results && for b in base cand; do cat
   $HOME/d1-cohort-run/out/$b/*.jsonl > runs/d1/results/$b.partial.jsonl; done`, commit, `git push origin
   runs/d1-cohort`.
5. At the end: `python3 runs/d1/analyze.py $HOME/d1-cohort-run/out > runs/d1/results/summary.md`; copy
   `$HOME/d1-cohort-run/out/base.jsonl`, `cand.jsonl` and `builds.txt` to `runs/d1/results/`, delete the partial
   files, commit, push to `runs/d1-cohort`.
6. Report to the D1 build session and the coordinator: sections 1a, 1b, 2 and 4 of `summary.md` (the tables as they
   are, or their counts if longer than 30 rows), the counts per status of both builds, the deciles table, the total
   wall time, and the branch commit.

## Rules

- No mesh files are committed (the raw meshes and exports are deleted per model by the job; `results/` holds rows
  only).
- Do not change the scripts, the builds or the id list. If a step fails, report the command and its last 30 lines.
- Timings: both builds of a model run back to back on this machine; report them as they are.
