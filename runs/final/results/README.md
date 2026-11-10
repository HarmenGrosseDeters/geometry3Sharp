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

`contacts_check_master.jsonl`, `contacts_check_base385.jsonl` (owner decision 2026-11-10: contacts counted apart from
unclean, NotCleanReason unchanged): the 25 models with an operation whose reason is "self-intersections" in the target
tally (24 models, 49 ops, with the #474 and #483 re-runs) or in `control.jsonl` (25 models, 50 ops)
(`contacts_check_ids.txt`), re-run with `job2.sh correct` on master 79a2c1ab and base385 cd7fb8ab, FinalRunner with the
contact check: for a "self-intersections" result, the evaluator's pairs (`si_pairs`), how many of them still cross with
the vertices welded by position (`si_crossing`), the positions with several vertices (`shared_positions`) and
`verdict` ("contacts" when every pair only touches at such a position). The reasons equal those of the runs on every
op. Result: all 49 target ops and all 50 control ops are "contacts", 0 pairs cross. So the tally is: target 26
unclean (degenerate triangles), 49 contacts, 412 empty of 16239; control 31 unclean (degenerate triangles), 50 contacts,
412 empty. The contacts are where the exact solids touch (Contacts, the duplicated vertices of ExactMeshBoolean; for
1041432's union, outside the tallied set: 16 contacts, the rounding's 15 false contacts all separated, 325 pairs, 0
crossing).
