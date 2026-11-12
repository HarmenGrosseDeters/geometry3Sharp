# JOB ps-cohort: SafeRounding position search (PR #755), cohort run of the Thingi10K target class (16 239 operations)

Owner of the job: the position search build session (session_01AaGT5AET7mF6x8ExehSwJe, library PR #755). Approved by
the owner 2026-11-12. Execute literally; ask instead of improvising. The layout is the correctness pass of the D1
cohort job (`jobs/JOB-d1-cohort.md`); there is no timing pass.

## What

Every model of the Thingi10K target class of the Boolean 2.0 final run (5413 models, `runs/ps/target_ids.txt`, the
list of `runs/d1/target_ids.txt`), with its #385 pairing (B = A rotated 20 degrees about (1, 1, 0) and shifted by a
quarter of the box, as `BooleanComparison export` makes it), union / intersection / difference, SafeRounding on, on two
builds:

- base: `1f3b6b79ff5cbe65f823ea1f84a8815c3f856692` (master, the PR's merge base, with #725)
- cand: `36ff5bf909f9b84cddf7bbf7744ecc4721ebefb7` (PR #755, the position search)

One pass of `runs/ps/job.sh correctness`: 4 workers (fewer if the machine has fewer cores), each pinned to one core
(`taskset`, `DOTNET_PROCESSOR_COUNT=1`); per model base, then cand. Each model is downloaded and exported once; cand
takes base's NotCleanReason where its result hash equals base's and evaluates it only where the result changed. One
JSON row per operation and build: ok, status, RoundingClean, NotCleanReason, remaining defects, MaxDrift / MaxLength,
SmallestRejectedDrift / MaxLength, edit counts (cand: `position_search_moves`), EvaluationLimitReached, the times, a
SHA-256 of the result.

**Acceptance** (read from `summary.md`): section 1a empty (no operation newly unclean); section 2: 0 operations that
are rounding clean in base with a different result, and 0 with more defects left in cand; section 3: MaxDrift above
MaxLength 0. Reported: the unclean counts of both builds, 1b, the changed unclean operations, the deciles, section 4.

**Expected wall time** on a 4-core machine: about 4 h (the D1 job's correctness pass: about 2.4 h of per-model
overhead for download, export and two process starts, about 1.5 h of operations and evaluations on single cores).
The smoke run of 4 models took 31 s after the builds. Builds about 5 minutes. Disk: an export is deleted after its
model.

## Steps

1. `git fetch origin runs/position-search-cohort && git checkout runs/position-search-cohort` (a fresh clone or
   worktree of the repository; the job fetches the two commits itself).
2. Smoke run (about 2 minutes): `head -6 runs/ps/target_ids.txt > /tmp/ps-smoke.txt && IDS_FILE=/tmp/ps-smoke.txt bash
   runs/ps/job.sh correctness $HOME/ps-smoke && python3 runs/ps/analyze.py $HOME/ps-smoke/out`. Expected: 18 rows per
   build, no `error` or `exception` field (`grep -l '"error"\|exception' $HOME/ps-smoke/out/*/*.jsonl` prints
   nothing). If not, stop and report the output.
3. The run: `bash runs/ps/job.sh correctness $HOME/ps-cohort-run`, as a harness-tracked background task of at most 2 h
   per chunk (or one workflow job); when a chunk ends or the machine restarts, run the same command again: finished
   models are skipped, an interrupted model is run again for both builds. Nothing else runs on the machine meanwhile.
4. About every hour (between chunks): `mkdir -p runs/ps/results && for b in base cand; do cat
   $HOME/ps-cohort-run/out/$b/*.jsonl > runs/ps/results/$b.partial.jsonl; done`, commit, `git push origin
   runs/position-search-cohort`.
5. At the end: `python3 runs/ps/analyze.py $HOME/ps-cohort-run/out > runs/ps/results/summary.md`; copy
   `$HOME/ps-cohort-run/out/{base,cand}.jsonl` and `builds.txt` to `runs/ps/results/`, delete the partial files,
   commit, push to `runs/position-search-cohort`.
6. Report to the build session (session_01AaGT5AET7mF6x8ExehSwJe) and the coordinator: the status counts of both
   builds, sections 1a, 1b, 1c and 2 of `summary.md` (the tables as they are, or their counts if longer than 30 rows),
   the deciles, section 4, the wall time, and the branch commit.

## Rules

- No mesh files are committed (`results/` holds rows only; the cache stays in the work dir).
- Do not change the scripts, the builds or the id list. If a step fails, report the command and its last 30 lines.
