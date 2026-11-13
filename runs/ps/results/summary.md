# Position search cohort run: base (master) against cand (the position search)

16239 operations of 5413 models; rows: base 16239, cand 16239

- base: 15771 clean, 412 empty, 56 unclean
- cand: 15775 clean, 412 empty, 52 unclean

cand: 16234 operations took the verdict of base (the same result hash), the others were evaluated

## 1a. clean -> not clean (stop rule: each one analysed before the merge): 0


## 1b. not clean -> clean: 4

| id | op | base | cand | base drift | cand drift | base smallest rejected | cand smallest rejected |
|---|---|---|---|---|---|---|---|
| 837999 | difference | unclean: degenerate triangles | clean | 0.7860136203925612 | 0.7860136203925612 | 1.2503035907339242 | 1.2503035907339242 |
| 837999 | intersection | unclean: degenerate triangles | clean | 0.01817014070560448 | 0.06250000000245858 | Infinity | Infinity |
| 958469 | difference | unclean: degenerate triangles | clean | 0.36682007235707004 | 0.36682007235707004 | 1.5341827206178225 | 1.5341827206178225 |
| 958470 | difference | unclean: degenerate triangles | clean | 0.5389587006006196 | 0.5389587006006196 | 5.256856498645682 | 5.256856498645682 |

## 1c. other status changes (eg clean <-> empty, one reason for another): 0


## 2. rounding clean in base: 16232 operations, 0 with a different result (stop rule: each one analysed)

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
| base | 15827 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1.8e-13 | 0.217 | 1 | 241 |
| cand | 15827 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1.8e-13 | 0.217 | 1 | 242 |
| base, the operations the search moved a vertex in | 6 | 0.0182 | 0.0182 | 0.367 | 0.405 | 0.405 | 0.405 | 0.539 | 0.786 | 0.786 | 0.786 | 0.969 | 1 |
| cand, the same | 6 | 0.0625 | 0.0625 | 0.367 | 0.539 | 0.539 | 0.539 | 0.786 | 0.969 | 0.969 | 0.969 | 1 | 2 |

MaxDrift above MaxLength in cand: 0 (expected 0).

## 4. times (ms), correctness pass (one core per process): 16239 operations

| build | field | sum | median | p95 | max |
|---|---|---|---|---|---|
| base | ms | 5437838 | 129 | 1266 | 52081 |
| base | rounding_ms | 1091313 | 20 | 274 | 18884 |
| cand | ms | 5444224 | 128 | 1296 | 51039 |
| cand | rounding_ms | 1104363 | 20 | 274 | 18202 |
| base | rounding_ms, rounding clean in base | 1088744 | 20 | 272 | 18884 |
| cand | rounding_ms, rounding clean in base | 1100322 | 20 | 272 | 18202 |
| base | rounding_ms, not rounding clean in base | 2569 | 366 | 859 | 859 |
| cand | rounding_ms, not rounding clean in base | 4041 | 438 | 1498 | 1498 |

Slower by more than 20 % and 0.5 s: 14

| id | op | base ms | cand ms | base rounding ms | cand rounding ms | rounding clean in base | search moves |
|---|---|---|---|---|---|---|---|
| 668057 | intersection | 4992 | 6885 | 655 | 710 | True | 0 |
| 1063860 | difference | 2872 | 4576 | 581 | 1623 | True | 0 |
| 688367 | difference | 3788 | 5205 | 462 | 754 | True | 0 |
| 99269 | difference | 3280 | 4530 | 594 | 1431 | True | 0 |
| 99269 | intersection | 1775 | 2972 | 150 | 274 | True | 0 |
| 472139 | difference | 4798 | 5856 | 818 | 930 | True | 1 |
| 73416 | difference | 3911 | 4774 | 549 | 1448 | True | 0 |
| 688367 | intersection | 2792 | 3426 | 417 | 715 | True | 0 |
| 1417966 | union | 1061 | 1675 | 321 | 518 | True | 0 |
| 74730 | difference | 2916 | 3526 | 540 | 1122 | True | 0 |
| 1396906 | union | 1820 | 2413 | 531 | 670 | True | 0 |
| 212138 | intersection | 1272 | 1862 | 153 | 258 | True | 0 |
| 762586 | union | 2690 | 3269 | 752 | 933 | True | 0 |
| 74730 | intersection | 2480 | 3029 | 545 | 822 | True | 0 |

## Notes of the build session (not from analyze.py)

- The full cohort (owner decision 2026-11-13): all 5413 models, 16 239 operations. The 3000 models of the cut ran
  2026-11-12T23:48Z to 2026-11-13T01:03Z (one restart of the machine in between; `summary_cut3000.md` is that run's
  summary; its rows are part of base.jsonl / cand.jsonl here), the other 2413 on 2026-11-13 from 05:14:43Z to
  06:04:59Z, in the same work directory with the same builds: about 124 minutes of run time in all. Models 117423 and
  56265 failed to export once (download) and were run again.
- Section 4 is a rough comparison (one core per process, four at a time); see the re-timing of three models alone on
  the machine in `summary_cut3000.md`.
