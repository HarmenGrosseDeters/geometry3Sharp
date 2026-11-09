# JOB-1 results (local Windows runner)

Job: `jobs/JOB-1.md`, lever commit `53665116`, shard `rounding` (`mesh_ops/MeshRoundingRepair.cs`), 7 parts, part 4 measured.

## Machine

See `machine.txt`: Intel Core i7-8700 (6 cores, 12 threads), 15.7 GB RAM, Windows 11 Pro 10.0.26300,
.NET SDK 10.0.401, git 2.51.1, Python 3.14.0. Timings are relative only (same job, same machine, not comparable with
the CI reference machine).

## Setup

- Checkout: a git worktree of the local repository, detached at `53665116`, with
  `git -c core.autocrlf=false` on every git call (the global `core.autocrlf` is `true` on this machine). Before the run:
  `mesh_ops/MeshRoundingRepair.cs` has 0 CR characters (LF), 102 808 characters, as the job expects.
- `dotnet tool restore`: dotnet-stryker 5.0.0. `dotnet build tests/geometry3Sharp.Tests -c Release -f net10.0`: 0 errors, 15 warnings.
- Parts: `python jobs/tools/make_parts.py 7` with `G3_CONCURRENCY=3`. Part spans: 0..32353, 32353..46431, 46431..58598,
  58598..71557, 71557..82299, 82299..95469, 95469..end (proxy units 382, 375, 386, 342, 389, 372, 239).
- Stryker: `DOTNET_GCHeapHardLimit=0x100000000`, `concurrency 3` in every label (as the job).
- Stryker's test filter for part 4 found 174 tests (the job's reference is 147; reported, not changed).

## Substitutions (same meaning, Windows)

| Job (Linux) | Here |
|---|---|
| `python3` | `python` (Python 3.14.0 on PATH; `python3` is not on PATH) |
| `pkill -f dotnet-stryker` | `Stop-Process` on the processes whose command line names the job's own config (`job1-<label>.json`), so only processes started by the job are stopped |
| `nproc`, `lscpu`, `free -m` | `Get-CimInstance Win32_Processor` and `Win32_OperatingSystem` (see `machine.txt`) |
| `git config --global core.autocrlf false` | `git -c core.autocrlf=false` per call, global setting unchanged |
| `git worktree add -B ...` | `git worktree add --detach ...` (no `-B`) |
| `nohup`-free background run | run as a detached process (owner and coordinator approval, the run exceeded the 2 h limit of a tracked background task); PIDs and start times were recorded |

## Deviations and events

- **Label A** (`A-part4`) started 20:05:50 (Berlin). The 2 h background limit of the tracked task was reached at 22:05; the
  Stryker process kept running detached and the original script finished it: exit code 0 is recorded in `A-part4.exit`.
  Stryker cannot resume, and the run was not restarted.
- **Label A is not a valid kill measurement.** Coverage capture took 6 h 34 min (20:11:45 to 02:44:53), and all 282
  mutants to test ended as `Timeout` (0 killed, 8 no coverage). Their lines spread over the whole part (1071 to 1301), and
  by the log times the mutant phase took about 5 s, which does not fit 282 real test runs. The score of 97.24 % counts
  timeouts as detected and is not a kill rate. The full log of this run was overwritten when label B1 started (the
  job's `rm -rf StrykerOutput`), so the cause is open. See `REPORT-A-part4.md`.
- The coverage phase (one test host, not parallel) took about two hours on this machine. Concurrency only affects the
  mutant phase.
- **Labels B1, B2:** the run is stopped as soon as Stryker logs `total mutants will be tested` (the job's stop rule); the
  process is found by its config name. The exit code is then the stop code, which the job allows.
- **Label B1 was stopped manually** at 07:20 (Berlin) by the owner, before its capture finished (3 h 24 min, lower bound).
  The stop used the process IDs of the Stryker runs matched by their config name (`job1-B1-F1.json`). No capture end, no
  mutant phase, no score. `B1-F1.exit` = 127 is the code of the stopped run, not a result. See `REPORT-B1-F1.md`.
- **Label C:** a full run; it was started only if the controller clock was before 02:00 (Berlin), to keep it inside the
  window before 07:00.
- The controller and the label scripts are not part of the repository; they run Stryker with the job's commands.

- **Label B2 was removed from the chain** (owner decision, 9 Nov, 03:55 Berlin): F2 is F1 plus more tests, so it cannot
  finish before 07:00. Its `B2-F2.exit` says `not-run`. F1 finds 171 tests against 174 for the full filter, so the filter
  saves little time in the coverage phase.
- **Scope on this machine:** kill measurements are no longer run here (owner decision). Kills are measured in CI only.
  The capture time of B1 is the measurement that stays.

## Times

Logs and `*.start`/`*.end` files are in UTC, as the job asks. The times in the chat were Berlin time (UTC+1).

## Files per label

- `<label>.json`: summary from `jobs/tools/summarize.py` (tests, mutants created, mutants to test, elapsed, capture
  start and end, statuses, score).
- `<label>-report.json`: Stryker's mutation report, sanitized (absolute repository paths and machine and user names
  replaced with `<repo>` and `<machine>`).
- `<label>.start`, `.end`, `.exit`: UTC timestamps and exit code.
- `REPORT-<label>.md` in the branch root: one-line result and the key numbers.
