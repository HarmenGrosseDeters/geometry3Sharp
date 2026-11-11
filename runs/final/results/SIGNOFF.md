# Exact Boolean 2.0 sign-off tally

Per operation (union, intersection, difference of each model with its #385 pairing). "contacts": NotCleanReason
says self-intersections, but every pair only touches where the solids touch (duplicated vertices; the contact
check of FinalRunner); counted apart from unclean (owner 2026-11-10). Unavailable: models whose export failed.

| phase | class | models | ops | clean | contacts | empty | unclean | failed | unavailable models |
|---|---|---|---|---|---|---|---|---|---|
| target, sign-off (D1 cohort base f2245b6f: master with #474 / #483 / #493) | target | 5413 | 16239 | 15752 | 49 | 412 | 26 | 0 | |
| target, FINAL-1 (1fd946aa, the unclean models re-run on #474 / #483) | target | 5413 | 16239 | 15752 | 49 | 412 | 26 | 0 | |
| control (#385 baseline cd7fb8ab) | target | 5413 | 16239 | 15746 | 50 | 412 | 31 | 0 | |
| rest (master 79a2c1ab) | open | 1474 | 4422 | 748 | 381 | 142 | 3151 | 0 | |
| rest (master 79a2c1ab) | selfint | 2920 | 8760 | 6260 | 1348 | 178 | 974 | 0 | |
| rest (master 79a2c1ab) | unknown | 187 | 561 | 356 | 101 | 9 | 92 | 3 | |
| rest (master 79a2c1ab) | unavailable | 6 | | | | | | | 81313, 101633, 102610, 242236, 242237, 996816 |

- target, sign-off (D1 cohort base f2245b6f: master with #474 / #483 / #493), target: unclean by reason: degenerate triangles 26
- target, FINAL-1 (1fd946aa, the unclean models re-run on #474 / #483), target: unclean by reason: degenerate triangles 26
- control (#385 baseline cd7fb8ab), target: unclean by reason: degenerate triangles 31
- rest (master 79a2c1ab), open: unclean by reason: not closed 3010, degenerate triangles 118, inside-out components 16, self-intersections 7
- rest (master 79a2c1ab), selfint: unclean by reason: degenerate triangles 843, not closed 62, self-intersections 43, inside-out components 26
- rest (master 79a2c1ab), unknown: unclean by reason: degenerate triangles 76, not closed 13, self-intersections 2, inside-out components 1
- rest (master 79a2c1ab), unknown: failed: 252786, 376253

## Inputs and builds

| file | what | build |
|---|---|---|
| `d1_base.jsonl` | D1 cohort base rows, all 5413 target models (16239 rows); sign-off row of the target class | f2245b6f (master with #474 / #483 / #493; contains 79a2c1ab); correctness pass 2026-11-10 |
| `contacts_check_d1base.jsonl` | contact check of the 24 models with a self-intersections row in `d1_base.jsonl` (72 rows) | f2245b6f |
| `target.jsonl` + `target_rerun_pr474.jsonl` + `target_rerun_pr483.jsonl` | FINAL-1 target phase (second target row) | 1fd946aa, re-runs on #474 / #483 |
| `control.jsonl`, `contacts_check_base385.jsonl` | control, #385 baseline | cd7fb8ab |
| `rest.jsonl` (13749 rows, with the 429 reruns) | rest phase: open, selfint, unknown classes | 79a2c1ab |
| `contacts_check_rest.jsonl` | contact check of the 1013 rest models with a self-intersections row (3039 rows, 0 error rows) | 79a2c1ab |

Contact checks: the reasons of every check row equal those of its source row (d1base 72 of 72, rest 3039 of 3039, 0
differences). D1 base: all 49 self-intersections rows are contacts. Rest: of 1882 self-intersections rows, 1830 are
contacts and 52 are unclean (pairs that cross; open 7, selfint 43, unknown 2).

## HTTP 429 reruns (rest phase)

Five models were rerun once with the fetch backoff (`fetch_thingi.py` from a5411ed0), all completed, none still at
429; their rows are in `rest.jsonl`:

| id | class | union | intersection | difference |
|---|---|---|---|---|
| 814664 | selfint | clean | clean | clean |
| 888457 | selfint | degenerate triangles | empty | degenerate triangles |
| 931878 | selfint | contacts | contacts | contacts |
| 931901 | open | degenerate triangles | degenerate triangles | degenerate triangles |
| 976646 | open | not closed | clean | not closed |

Not available (export failed or exporter out of memory): 81313, 101633, 102610, 242236, 242237, 996816. Failed
operations (OutOfMemoryException, not retried): 252786 (intersection, difference), 376253 (union), class unknown.
