# Handover: runner coordination (Exact Boolean 2.0 and the queued runner jobs)

Written 2026-11-10 by the Exact Boolean 2.0 implementing session (session_01AaGT5CTqL86zQ1wj56oXAq, archived after
the sign-off) for a fresh runner-coordination session. Owner decision (budget): this session finished the Boolean 2.0
sign-off; the new session only routes and evaluates the runner jobs below. No library work.

## People and how to reach them

All messages go through the Claude Code Remote `send_message` tool (session id), never as a GitHub comment.

| Who | Session | Role |
|---|---|---|
| Coordinator | session_01M4BQDeN5rS3AzKxoEWqYuU | decides, merges, routes; receives every result |
| Runner (Haiku) | session_01AaGT5CAakamrXJTKz38VBb | runs the jobs literally on its 4-core / 16 GB machine; no code changes, no decisions |
| D1 build session | session_01AaGT59iMN5zA9sD5GwWRyT | owner of PR #513 and of the D1 cohort job |
| 3.0 pre-study | session_01AaGT5ATvj52jcajcJ6yYAS | Antipodal method (#539), targets T1 (parallel) / T2 (memory) |

The runner's habits: it reports every chunk (its harness stops a background task after 2 h; it removes the partial
`*.tmp` file of the cut-off model and reruns the same command), error rows and anything unexpected; it asks before
changing anything (scripts, id lists, its checkout while a job runs). Answer its questions with a concrete command;
anything that changes a script or a plan goes to the coordinator first. Times for the owner in Europe/Berlin.

## Job queue, in order

### 0. FINAL-1 rest phase (running when this note was written)

- Branch `runs/boolean2-final`, `runs/final/job.sh` (pinned build master 79a2c1ab). Command on the runner:
  `bash runs/final/job.sh rest ~/b2final` (resumable, chunks of 2 h).
- After "phase rest done": the 429 retry pass, approved by the coordinator. (1) `git fetch origin runs/boolean2-final &&
  git checkout a5411ed0 -- runs/final/fetch_thingi.py` (that file only: retry with backoff for HTTP 429 / 5xx); (2)
  delete `out/rest/<id>.jsonl` and `.err` of the HTTP-429 ids (814664, 888457, 931878, 931901, 976646 and any later
  ones), keep `out/class/<id>`; (3) rerun the same command once; a model still at 429 after 7 attempts stays
  "unavailable" and is listed. Not retried: 996816 (exporter out of memory, unavailable since the target phase), 81313,
  101633, 102610, 242236, 242237 (export failed).
- Then the runner commits `rest.jsonl` and `class.tsv` (see the job.sh header) and sends the commit.
- Evaluation (if not already in the sign-off below): the rest models with reason "self-intersections" go through the
  contact check: `CACHE=<cache> IDS_FILE=<ids> bash runs/final/job2.sh correct <work dir>
  master:79a2c1ab15d3ce9e419b2a994af343e32877881c target,selfint,unknown,open` (FinalRunner writes `si_pairs`,
  `si_crossing`, `shared_positions`, `verdict`; verdict "contacts" = every pair only touches where a position has
  several vertices, counted apart from unclean, owner 2026-11-10). Commit `results/contacts_check_rest.jsonl` and the
  per-class line "clean / contacts / unclean / empty / unavailable"; the 429 reruns as a separate line.

### 1. D1 cohort (JOB-d1-cohort)

- Branch `runs/d1-cohort` at 841c11d9, job file `jobs/JOB-d1-cohort.md` (the runner executes it literally): base
  f2245b6f (master, apex rule) against cand c623df4c (PR #513, reference rule), 5413 target models, 16 239 ops.
- Passes: correctness (`bash runs/d1/job.sh correctness $HOME/d1-cohort-run`, about 4 h, 1 core per worker), then timing
  (`bash runs/d1/job.sh timing $HOME/d1-cohort-run`, about 1 h, 400 models, alone). Smoke run first (step 2 of the file).
- Results: `runs/d1/results/{base,cand,timing_base,timing_cand}.jsonl`, `builds.txt`, `summary.md`
  (`python3 runs/d1/analyze.py`). Recipients: the D1 build session and the coordinator (sections 1a, 1b, 2, 4 of
  summary.md, counts per status, deciles, wall times, commit).
- Note: its verdicts are NotCleanReason's; a "self-intersections" row there may be contacts. If the D1 session asks,
  run the contact check above on those ids for both builds.

### 2. Open-class re-measure after #528 (sign-off line of the open-operand fix)

- Branch `runs/boolean2-final` at a5411ed0 (includes the fetch backoff). Command: `git checkout a5411ed0 && bash
  runs/final/open_remeasure.sh ~/b2final/out/rest ~/openre` (needs the finished rest phase; resumable).
- What it does: a 300-model sample of the 1474 open models (`open_sample.py`: every open model slower than 5 s, not
  clean (apart from the expected "not closed") or with an error in the rest phase, filled stratified, seed 20261110); `job2.sh correct` on 293d0e3b (#528, 1
  core per worker, 7200 s timeout); `job2.sh timing` of a 40-model subsample (pre 79a2c1ab and fix 293d0e3b
  interleaved, 2 rounds, alone, nothing else running); `open_compare.py` writes `open_line.md`.
- Expected wall time: correct pass about 4-8 h (dominated by the slow "must" models; the 100-model sample needed
  7502 s of single-core op time on 293d0e3b, the 300-model sample is skewed to slow models), plus one worker held 2 h by
  1368052's timeout; timing pass about 1 h. The "must" count is printed at the start: if it is far above 300, stop and
  ask the coordinator (the "not closed" verdict of open results is expected and does not count). 1368052 (2.44M triangles per operand) is an expected timeout (column "timeout, size", its 4-core numbers in
  open_compare.py) and is not in the timing sample.
- Results to commit (runner): `open_line.md` as generated, `open_sample.txt`, `open_timing.txt`, `out/correct_fix.jsonl`,
  `out/timing.jsonl` into `runs/final/results/open_remeasure/`. Recipient: the coordinator. The early check
  (`results/open_sample100/`, 100 random open models in the implementing session's container) showed no problem:
  verdict changes only where the old result was the defect or gains open edges at the input's hole; time fix / pre
  median 2.0x, p95 4.5x, max 7.0x.

### 3. thingi-pairs (EMBER protocol, #418)

- `BooleanComparison` class `thingi-pairs` (branch `feature/thingi-pairs`, `benchmarks/BooleanComparison/thingi-pairs/`,
  `ThingiPairs.cs`, `data/thingi_pairs.py`); the plan is in `docs/roadmap/exact-boolean.md` ("thingi-pairs runs (owner
  2026-11-08)": 1000 pairs plus the 100k-1M size class, ours and Manifold, CGAL EPECK on the fixed 100-pair sample, as
  the final run of Boolean 2.0). The job file is the coordinator's; ask it for the exact commit and command before
  routing. Recipient: the coordinator.

### 4. Tet meshing JOB-2 (fTetWild as a black box)

- Branch `runs/tet-meshing`, `jobs/JOB-2.md` (after JOB-1 of the same branch; same machine, alone). Licence rule:
  fTetWild is MPL-2.0, binary only, no source read or copied. Results: `REPORT-2.md` and two CSVs on
  `runs/tet-meshing`. Recipient: the coordinator.

## Pitfalls (seen in FINAL-1)

- **HF 429:** huggingface.co rate-limits bursts. `fetch_thingi.py` from f693a716 / a5411ed0 retries with backoff
  (Retry-After, else 30 s doubling, 7 attempts); the old one records the model as "unavailable". Never raise fetch
  parallelism, never switch to another source without the coordinator.
- **Chunks:** the runner's background tasks end after 2 h; every job here is resumable by rerunning the same command
  (finished models are skipped). A cut-off model leaves `*.tmp`, which the runner removes.
- **Class files:** in `job.sh` rest, a model without `out/class/<id>` is skipped; never delete class files to force a
  rerun (delete the result row instead).
- **One core per worker:** `job2.sh correct` and the D1 correctness pass pin 4 workers to 1 core each; their times are
  not comparable (only the timing passes, alone, are). Do not report correctness-pass times as performance.
- **Timing rules:** timing passes run alone on the machine, builds interleaved per model, 2 rounds; nothing else may run
  meanwhile (not even another session's job).
- **Big models:** 1368052 (open, 4.87M triangles) needs about 10 min per op on 4 cores and 8.7 GB heap on the old
  build; 252784 / 252786 needed 6-10 min and 9-10 GB. The 16 GB machine is enough for one at a time.
- **Unavailable models:** 81313, 101633, 102610, 242236, 242237 (export failed), 996816 (exporter out of memory).
- **Contacts:** NotCleanReason reports "self-intersections" for results whose triangles only touch where the solids
  touch (duplicated vertices, Contacts > 0). The runner's FinalRunner (since 25012928) adds `verdict` with the contact
  check; count "contacts" apart from unclean; NotCleanReason itself stays unchanged (owner).
- **No meshes are committed** anywhere (Thingi10K licences); `results/` holds rows only.

## Where the Boolean 2.0 numbers are

`runs/final/results/` on `runs/boolean2-final`: `README.md` (each file explained), `target.jsonl` (+ the #474 / #483
re-runs), `control.jsonl` (#385 baseline cd7fb8ab), `contacts_check_*.jsonl` (the contact re-tally),
`open_sample100/` (the open-class early check), `SIGNOFF.md` (the full tally, target + control + rest).
