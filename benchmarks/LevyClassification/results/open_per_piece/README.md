A/B of the open-operand fix (per-piece classification of open meshes) on the 21 open models of `sample.txt` and
`sample_random.txt` (cohorts open, open_rand, open_rand2), 3 operations each, 2 rounds alternating, on a 4-core cloud
machine with nothing else running: `base` = Lévy phase 2 (#501), `branch` = the fix on top of it.
Total op time 500.5 -> 2171.7 s (4.34x), classification 95.5 -> 1758.2 s (18.4x); results differ on 40 of 63 ops
(expected: the open meshes' classification changes). Worst: 113906 intersection / difference / union 10.1 / 10.8 / 15.8
-> 100.4 / 98.4 / 104.8 s.
