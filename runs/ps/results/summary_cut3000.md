# Position search cohort run: base (master) against cand (the position search)

9000 operations of 3000 models; rows: base 9000, cand 9000

- base: 8739 clean, 205 empty, 56 unclean
- cand: 8743 clean, 205 empty, 52 unclean

cand: 8995 operations took the verdict of base (the same result hash), the others were evaluated

## 1a. clean -> not clean (stop rule: each one analysed before the merge): 0


## 1b. not clean -> clean: 4

| id | op | base | cand | base drift | cand drift | base smallest rejected | cand smallest rejected |
|---|---|---|---|---|---|---|---|
| 837999 | difference | unclean: degenerate triangles | clean | 0.7860136203925612 | 0.7860136203925612 | 1.2503035907339242 | 1.2503035907339242 |
| 837999 | intersection | unclean: degenerate triangles | clean | 0.01817014070560448 | 0.06250000000245858 | Infinity | Infinity |
| 958469 | difference | unclean: degenerate triangles | clean | 0.36682007235707004 | 0.36682007235707004 | 1.5341827206178225 | 1.5341827206178225 |
| 958470 | difference | unclean: degenerate triangles | clean | 0.5389587006006196 | 0.5389587006006196 | 5.256856498645682 | 5.256856498645682 |

## 1c. other status changes (eg clean <-> empty, one reason for another): 0


## 2. rounding clean in base: 8993 operations, 0 with a different result (stop rule: each one analysed)

Not rounding clean in base: 7 operations, 5 with a different result; the search moved a vertex in 6 operations (11 moves).

| id | op | base status | cand status | base left (degenerate, pairs) | cand left | base drift | cand drift | base edits | cand edits (the last: search moves) | limit base / cand |
|---|---|---|---|---|---|---|---|---|---|---|
| 837999 | difference | unclean: degenerate triangles | clean | 1, 0 | 0, 0 | 0.7860136203925612 | 0.7860136203925612 | 9/0/0/0/0/0/0 | 9/0/1/0/0/0/1 | False / False |
| 837999 | intersection | unclean: degenerate triangles | clean | 3, 0 | 0, 0 | 0.01817014070560448 | 0.06250000000245858 | 9/1/0/0/0/0/0 | 9/1/3/0/0/0/3 | False / False |
| 838003 | difference | unclean: degenerate triangles | unclean: degenerate triangles | 2, 0 | 1, 0 | 0.9687824125858481 | 0.9687824125858481 | 4/1/1/0/0/0/0 | 4/1/2/0/0/0/1 | False / False |
| 958469 | difference | unclean: degenerate triangles | clean | 3, 0 | 0, 0 | 0.36682007235707004 | 0.36682007235707004 | 5/1/0/0/0/0/0 | 5/1/3/0/0/0/3 | False / False |
| 958470 | difference | unclean: degenerate triangles | clean | 2, 0 | 0, 0 | 0.5389587006006196 | 0.5389587006006196 | 12/0/0/0/0/0/0 | 12/0/2/0/0/0/2 | False / False |

Of them with more defects left in cand than in base: 0 (expected 0: no edit adds a defect).

## 3. MaxDrift / MaxLength, deciles (operations with a rounding)

| build | n | 0 | 0.1 | 0.2 | 0.3 | 0.4 | 0.5 | 0.6 | 0.7 | 0.8 | 0.9 | 1 | > 0.9 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 8795 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0.217 | 1 | 135 |
| cand | 8795 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0.217 | 1 | 136 |
| base, the operations the search moved a vertex in | 6 | 0.0182 | 0.0182 | 0.367 | 0.405 | 0.405 | 0.405 | 0.539 | 0.786 | 0.786 | 0.786 | 0.969 | 1 |
| cand, the same | 6 | 0.0625 | 0.0625 | 0.367 | 0.539 | 0.539 | 0.539 | 0.786 | 0.969 | 0.969 | 0.969 | 1 | 2 |

MaxDrift above MaxLength in cand: 0 (expected 0).

## 4. times (ms), correctness pass (one core per process): 9000 operations

| build | field | sum | median | p95 | max |
|---|---|---|---|---|---|
| base | ms | 3377367 | 145 | 1477 | 52081 |
| base | rounding_ms | 683196 | 23 | 305 | 18884 |
| cand | ms | 3379013 | 146 | 1469 | 51039 |
| cand | rounding_ms | 692338 | 22 | 305 | 18202 |
| base | rounding_ms, rounding clean in base | 680627 | 22 | 302 | 18884 |
| cand | rounding_ms, rounding clean in base | 688297 | 22 | 303 | 18202 |
| base | rounding_ms, not rounding clean in base | 2569 | 366 | 859 | 859 |
| cand | rounding_ms, not rounding clean in base | 4041 | 438 | 1498 | 1498 |

Slower by more than 20 % and 0.5 s: 9

| id | op | base ms | cand ms | base rounding ms | cand rounding ms | rounding clean in base | search moves |
|---|---|---|---|---|---|---|---|
| 99269 | difference | 3280 | 4530 | 594 | 1431 | True | 0 |
| 99269 | intersection | 1775 | 2972 | 150 | 274 | True | 0 |
| 472139 | difference | 4798 | 5856 | 818 | 930 | True | 1 |
| 73416 | difference | 3911 | 4774 | 549 | 1448 | True | 0 |
| 1417966 | union | 1061 | 1675 | 321 | 518 | True | 0 |
| 74730 | difference | 2916 | 3526 | 540 | 1122 | True | 0 |
| 1396906 | union | 1820 | 2413 | 531 | 670 | True | 0 |
| 212138 | intersection | 1272 | 1862 | 153 | 258 | True | 0 |
| 74730 | intersection | 2480 | 3029 | 545 | 822 | True | 0 |

## Notes of the build session (not from analyze.py)

- The run: 3000 models, 4 workers, started 2026-11-12T23:48:13Z, interrupted once by a restart of the session's
  machine at about 00:50Z with 2553 models done, resumed 00:51:36Z, done 01:03:00Z: about 74 minutes of run time.
  Model 117423 failed to export in the first chunk (download) and was run again in the second.
- Section 4 is a rough comparison (one core per process, four at a time). Three of the listed models were timed again
  alone on the machine with every core (`BooleanComparison profile --repeat 1 --safe-rounding`, two rounds, base and
  cand in turn; rounding ms): 99269 difference 405, 406 against 466, 561 and intersection 53, 47 against 55, 126;
  73416 difference 339, 234 against 251, 217; 74730 difference 233, 378 against 244, 261 and intersection 300, 222
  against 263, 325: within the noise, with the same hashes.
- 472139 difference is rounding clean in base and has one search move with the same result hash: the search moved a
  vertex of a component that was then removed as tiny (the search runs before the removal).
