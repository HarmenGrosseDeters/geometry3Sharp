# MeshFuzzPilot campaign grp-a: union, intersection, difference

Issue #624. Raw output only: logs, `dump` and replay text, stopwatch timing. No corpus, no crash or timeout files
(they stay in the container at `/work/grp-a/artifacts`, reproducers are the input bytes there).

## Campaign

- Host: Podman VM (WSL2), Ubuntu 24.04 container, x86-64, .NET 10.0.404 (SDK image). 6 CPUs, 5.9 GB for the VM (the
  issue assumed 16 GB), so 3 fork workers instead of 4 to 6.
- Tools: SharpFuzz.CommandLine 2.2.0, libfuzzer-dotnet v2025.05.02.0904 (SHA-256 `c2c2a90d...` verified).
- Validation: `MeshFuzzPilot` built (Release), `python3 -m unittest` 7 tests OK, `self-test` passed inside `run.sh`.
- Filter: `MESH_FUZZ_OPS=union,intersection,difference`, seed 20261109 (chunk seeds 20261109 + chunk number), 512 bytes,
  30 s timeout per input, RSS 3 GiB, fork mode.
- Blocks (wall time, runs and last progress line of each; see `grp-a.chunk*.out`):

| Block | Start (Berlin) | Length | Runs | cov | ft | corp | exec/s | crash/timeout |
|---|---|---|---|---|---|---|---|---|
| 1 | 2026-11-11 22:23 | 1857 s (stopped by a VM stop) | 60 952 | 88 247 | 375 537 | 6 446 | 9 | 0 / 0 |
| 2 | 2026-11-11 22:57 | 7209 s | 85 028 | 354 622 | 412 995 | 7 012 | 5 | 0 / 1 |
| 3 | 2026-11-12 00:57 | 7258 s | 47 400 | 378 064 | 421 646 | 6 585 | 2 | 0 / 0 (crash, see below) |
| 4 | 2026-11-12 02:57 | 7293 s | 36 190 | 393 732 | 429 610 | 6 659 | 4 | 0 / 0 (1 crash) |
| 5 | 2026-11-12 04:59 | 7256 s | 25 765 | 400 097 | 432 167 | 6 274 | 3 | 0 / 0 |

Total 255 335 runs. The VM stopped once (Hyper-V time provider, 2026-11-11 about 21:54 UTC); the campaign was resumed on
the same work directory and corpus within about 2 minutes (`fuzz-before-chunk*.log` keep the logs before each restart).
Blocks 2 to 5 are not the same libFuzzer process as block 1 (fork mode restarted), so cov is comparable only within a block.
The jump of cov between block 1 and 2 is a change of the instrumented counting after the restart, not coverage gained.

## Artifacts

- 2 crashes, 1 timeout, 60 `slow-unit-*` (not findings, see below). List with sizes: `artifact-list.txt`.
- Triage per README: replay with the same `MESH_FUZZ_OPS`, group by message text. All three non-slow artifacts have a
  different message, so each is its own group, and each is the only member of its group.

### 1. `crash-e0036eeca3b3fc4faa5b6f10089e2da1a89d81b7` (intersection)

Message: `RoundingClean but 1 degenerate triangles (at the rounding's scale)` (`CheckBoolean`, Program.cs:296).
Dump: `dump-crash-e0036...txt`. Replay: `replay-crash-e0036...txt`.

Inputs carry coordinates of 1.8e-284 and 2.7e-317 (subnormal) next to 1e-32 offsets. This is the class of FINDINGS.md
item 5a (`RoundingClean` claimed where the rounding's scale is out of range). Not confirmed as the same case; the
range check of the rounding repair is the open follow-up named there.

### 2. `crash-3a18fc50a391ad4d4cdfbaa5f0cb53102838b1ff` (difference)

Message: `System.InvalidOperationException: ExactTriangulation2: constraint was not inserted (0 crossing edges, 8 collinear vertex uses)`
from `ExactTriangulation2.InsertConstraint` (ExactTriangulation2.cs:332) via `ExactMeshArrangement.triangulate_touched`
(ExactMeshArrangement.cs:2222) in `Compute` (line 1078). Dump: `dump-crash-3a18fc...txt`.

An exception in the exact arrangement. The exact arrangement has no range (README), so this is not a range issue: it is
an unhandled exception on finite closed input at a magnitude of 3.7e-223, with vertices of 1e-284 among them. Belongs to
the Exact Boolean 3.0 queue (per #624).

### 3. `timeout-251fe028ac01b4b0db737de05913b7a23a3b4450` (difference)

No finding on replay (rc 0), but **47 s** uninstrumented (`time`, stopwatch) against the fuzzer's 30 s timeout and the
5 s per-operation target. Dump: `dump-timeout-251fe...txt`. Performance finding, not a hang (it ends). Belongs to the
Exact Boolean 3.0 queue.

## slow-unit-* (60)

Replayed one by one with a stopwatch (`slow-timing.txt`, replay text not kept). All 60 pass (rc 0, no finding) and all
take more than 5 s: 12 s to 35 s. The timing ran while three fuzz workers ran on the same VM, so the numbers are high. A
clean re-timing on an idle machine is still to do before a performance claim. The units are the same operations as
above, so they go to the same queue as one performance item.
