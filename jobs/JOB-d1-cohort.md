# JOB d1-cohort: D1 reference rule, cohort run of the Thingi10K target class (16 239 operations)

Owner of the job: the D1 build session (library PR #513). Runner: the Haiku runner, queued after the Boolean 2.0
confirmation run. Execute literally; ask instead of improvising. Layout per the owner's decision of 2026-11-10:
correctness and timing apart, exports reused, NotCleanReason only where the result changed.

## What

Every model of the Thingi10K target class of the Boolean 2.0 final run (5413 models, `runs/d1/target_ids.txt`, from
`runs/boolean2-final` `runs/final/results/class.tsv`: closed, no self-intersection), with its #385 pairing (B = A
rotated 20 degrees about (1, 1, 0) and shifted by a quarter of the box, as `BooleanComparison export` makes it),
union / intersection / difference, SafeRounding on, on two builds:

- base: `f2245b6fc632fd4997521adbef158c5607c11ce8` (master, the PR's merge base: the apex rule)
- cand: `c623df4ce76fcc51ca58736618e0adbd8ce89572` (PR #513, the reference rule; its later commits change no
  library behaviour)

Two passes of `runs/d1/job.sh`:

- **correctness**: every model; 4 workers (fewer if the machine has fewer cores), each pinned to one core
  (`taskset`, `DOTNET_PROCESSOR_COUNT=1`); per model base, then cand. Each model is downloaded and exported once into
  the cache; cand takes base's NotCleanReason where its result hash equals base's (the same mesh) and evaluates it
  only where the result changed. One JSON row per operation and build: ok, status, RoundingClean, NotCleanReason,
  remaining defects, MaxDrift / MaxLength, SmallestRejectedDrift / MaxLength, edit counts, EvaluationLimitReached,
  a SHA-256 of the result.
- **timing**: the 400 models of `runs/d1/timing_ids.txt` (every model with an operation over 1 s in the Boolean 2.0
  final run, 332, plus 68 stratified by time), one at a time with every core, base and cand interleaved per model
  (the order alternates), from the cached exports, no NotCleanReason.

**Expected wall time** on a 4-core machine (the smoke run of 10 models took 16 s for correctness after the builds;
the Boolean 2.0 final run's operations took 0.93 h with 4 cores, the timing sample's 0.36 h): correctness about 4 h
(about 2.4 h of per-model overhead: download, export, two process starts; about 1.5 h of operations and evaluations
on single cores), timing about 1 h. Builds about 5 minutes. Disk: the cache keeps the exports of the 400 timing
models only (raw meshes are deleted at once).

## Steps

1. `git fetch origin runs/d1-cohort && git checkout runs/d1-cohort` (a fresh clone or worktree of the repository).
2. Smoke run (about 2 minutes): `head -6 runs/d1/target_ids.txt > /tmp/d1-smoke.txt && IDS_FILE=/tmp/d1-smoke.txt
   bash runs/d1/job.sh correctness $HOME/d1-smoke && IDS_FILE=/tmp/d1-smoke.txt bash runs/d1/job.sh timing
   $HOME/d1-smoke && python3 runs/d1/analyze.py $HOME/d1-smoke/out`. Expected: 18 rows per build in each pass, no
   `error` or `exception` field (`grep -l '"error"\|exception' $HOME/d1-smoke/out/*/*.jsonl` prints nothing). If
   not, stop and report the output.
3. Correctness: `bash runs/d1/job.sh correctness $HOME/d1-cohort-run`, as a harness-tracked background task of at
   most 2 h per chunk; when a chunk ends (or the machine restarts), run the same command again: finished models are
   skipped, an interrupted model is run again for both builds. Nothing else runs on the machine meanwhile.
4. About every hour (between chunks): `mkdir -p runs/d1/results && for b in base cand; do cat
   $HOME/d1-cohort-run/out/$b/*.jsonl > runs/d1/results/$b.partial.jsonl; done`, commit, `git push origin
   runs/d1-cohort`.
5. Timing, after the correctness pass has finished: `bash runs/d1/job.sh timing $HOME/d1-cohort-run`, the same way
   (resumable; nothing else runs on the machine).
6. At the end: `python3 runs/d1/analyze.py $HOME/d1-cohort-run/out > runs/d1/results/summary.md`; copy
   `$HOME/d1-cohort-run/out/{base,cand,timing_base,timing_cand}.jsonl` and `builds.txt` to `runs/d1/results/`,
   delete the partial files, commit, push to `runs/d1-cohort`.
7. Report to the D1 build session (session_01AaGT59iMN5zA9sD5GwWRyT) and the coordinator: sections 1a, 1b, 2 and 4
   of `summary.md` (the tables as they are, or their counts if longer than 30 rows), the counts per status of both
   builds, the deciles table, the wall time of each pass, and the branch commit.

## Rules

- No mesh files are committed (`results/` holds rows only; the cache stays in the work dir).
- Do not change the scripts, the builds or the id lists. If a step fails, report the command and its last 30 lines.
