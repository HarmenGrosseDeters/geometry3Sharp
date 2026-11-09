Results of runs/final/job.sh (raw rows *.jsonl, class.tsv, summary_*.md) are committed here by the runner.

`target_rerun_pr474.jsonl`: phase target again for the 339 models with an unclean operation in `target.jsonl`
(`target_rerun_pr474_ids.txt`), on PR #474 (32558f9b: SafeRounding's degenerate area relative to the box of all
triangles unless components are far apart), run by the implementing session on a 4-core cloud machine (timings not
comparable). With these rows in place of target.jsonl's: 84 unclean of 16239 (35 degenerate triangles, 49
self-intersections), 412 empty, 0 failures; before 472 unclean. No operation clean in target.jsonl became unclean.
