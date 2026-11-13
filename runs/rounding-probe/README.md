# Probe: where the time of SafeRounding goes (the rounding search lever, #734 section 8)

Raw output of a probe by the position search session (session_01AaGT5AET7mF6x8ExehSwJe), 2026-11-13. Not a PR; nothing
here is library code.

- `round_probe.patch`: timers per sub-phase of `MeshRoundingRepair.Apply`, on master 1007c8d (apply in a throw-away
  worktree; `G3_ROUND_PROBE=1` prints one `ROUNDPROBE` line per Apply to stderr). The timers of the full search's
  loop body add two timestamps per tested pair, so `full` is a little above an uninstrumented run.
- `probe_run.py <out.jsonl> <repeat> <ops> <case dir>...`: `BooleanComparison profile --repeat <r> --safe-rounding`
  per case and operation with a tool built from the patched tree in `tool_probe/`, the Apply with the median total of
  the last r; `probe_sum.py` sums them.
- `probe_norm.jsonl`: the 20 Thingi10K models of the normal stratum of EXTREME_RUNTIME_BOOLEANS.md section 8 (union,
  median of 5). `probe_rot.jsonl`: the 33 rotated operations of #725 (median of 3). 4 cores. `summary.txt`: the sums.
- Fields (ms): total (Apply), scales (component_scales), positions (the position map), tree (every rebuild_tree),
  full / incr (the full and the incremental pair searches), degen (degenerate_triangles), passes (repair_passes
  without the searches, scans and rebuilds inside), near (inside passes), tiny (remove_tiny_components), body_cpu /
  test_cpu (CPU time of the full search's loop bodies and of the triangle tests in them, over all threads).
