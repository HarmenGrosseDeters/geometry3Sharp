Final 800 run of Alpha Wrap 2.0 (2026-11-09): master dfc93fdb, sample800 seed 2611, 300/3000, wrappipe, full fidelity,
warm (soup), one process at a time on a local 4-core machine. No meshes here (Thingi10K, fetched at run time).

- f800_passoff.jsonl: the library defaults (BENCH_WRAP_QUALITY=0, the quality pass off since #413). The final result.
- f800_passon.jsonl: the same with the quality pass (wrappipe ran it by default before #453).
- *_analysis.txt: analyze_wrap_pipe.py output.
Two results above the bench's 300k self-intersection limit (175914, 360069) were checked offline with the exact
check: 0 self-intersections, closed, same hashes as in the run.
