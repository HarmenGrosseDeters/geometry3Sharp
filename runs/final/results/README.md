Results of runs/final/job.sh (raw rows *.jsonl, class.tsv, summary_*.md) are committed here by the runner.

`target_rerun_pr474.jsonl`: phase target again for the 339 models with an unclean operation in `target.jsonl`
(`target_rerun_pr474_ids.txt`), on PR #474 (32558f9b: SafeRounding's degenerate area relative to the box of all
triangles unless components are far apart), run by the implementing session on a 4-core cloud machine (timings not
comparable). With these rows in place of target.jsonl's: 84 unclean of 16239 (35 degenerate triangles, 49
self-intersections), 412 empty, 0 failures; before 472 unclean. No operation clean in target.jsonl became unclean.

`target_rerun_pr483.jsonl`: the 10 models with an op that #474 left RoundingClean but degenerate by NotCleanReason
(fragmented results, box / largest 8 to 16), on PR #483 (1d2e12bf, FarApart 100), same machine as the #474 re-run.
Of their 30 ops: 25 clean, 5 degenerate with RoundingClean false, 0 failures. With these rows and #474's in place of
target.jsonl's: 75 unclean of 16239 (26 degenerate triangles, 49 self-intersections), 412 empty.
