### Boolean chains probe (rows: 270; chains: 90)

#### ExactMesh expression, one n-ary arrangement per step: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=90) | step 2 (n=90) | step 3 (n=90) |
|---|---|---|---|
| input | 3 (0.3) | 2 (0.2) | 1 (0.1) |
| setup | 7 (0.7) | 6 (0.5) | 6 (0.5) |
| trees | 9 (0.8) | 5 (0.5) | 5 (0.5) |
| cull | 1 (0.1) | 0 (0.0) | 0 (0.0) |
| pairs | 16 (1.6) | 41 (4.1) | 41 (4.0) |
| planes | 1 (0.1) | 3 (1.3) | 3 (1.3) |
| intersect | 0 (0.6) | 22 (15.5) | 22 (15.5) |
| merge | 6 (0.8) | 22 (8.1) | 22 (8.1) |
| triangulation | 2 (0.7) | 24 (18.7) | 22 (18.8) |
| classification | 6 (2.1) | 52 (23.6) | 56 (23.8) |
| simplify | 1 (0.7) | 16 (7.9) | 8 (4.3) |
| assembly | 7 (1.1) | 28 (8.7) | 14 (5.1) |
| rounding | 26 (2.4) | 28 (16.4) | 18 (34.2) |
| TOTAL | 106 (12.0) | 258 (98.1) | 245 (112.4) |

Share of the sum over chains (%): step 1: pairs 14, planes 1, intersect 5, merge 7, triangulation 6, classification 17, rounding 20; step 2: pairs 4, planes 1, intersect 16, merge 8, triangulation 19, classification 24, rounding 17; step 3: pairs 4, planes 1, intersect 14, merge 7, triangulation 17, classification 21, rounding 30

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 167752 / 0.0 | 3314217 / 6.4 | 3314217 / 6.3 |
| prefilter_apart | 37145 / 0.0 | 397463 / 0.1 | 397463 / 0.1 |
| construct_apart | 17718 / 0.1 | 230247 / 0.1 | 230247 / 0.1 |
| meet_point | 5346 / 0.0 | 98940 / 0.1 | 98940 / 0.1 |
| meet_segment | 49896 / 0.4 | 438993 / 0.6 | 438993 / 0.7 |
| co:share3 trivial | 0 / 0.0 | 3675 / 0.0 | 3675 / 0.0 |
| co:share2 trivial | 0 / 0.0 | 10570 / 0.0 | 10570 / 0.0 |
| co:share1 trivial | 11 / 0.0 | 31043 / 0.0 | 31043 / 0.0 |
| co:adds (not trivial) | 104104 / 0.0 | 1631459 / 3.7 | 1631459 / 3.8 |
| prep (touched triangles) | 85728 / 0.5 | 901260 / 2.5 | 901260 / 2.4 |
| prep trivial | 4569 / 0.0 | 38712 / 0.0 | 38712 / 0.0 |
| tri (touched triangles) | 81345 / 4.6 | 871041 / 11.4 | 871041 / 13.1 |
| tri trivial | 186 / 0.0 | 8493 / 0.0 | 8493 / 0.0 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 1.42, step 2: 1.88, step 3: 1.86

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | if certified | 16415 | 8.9 | 8.0 | 7.7 |
| 1 | 1 | if certified | 13962 | 1.9 | 1.6 | 4.5 |
| 2 | 0 | if certified | 16773 | 9.9 | 2.9 | 1.0 |
| 2 | 1 | if certified | 16185 | 10.1 | 3.1 | 1.0 |
| 2 | 2 | if certified | 19611 | 12.6 | 3.2 | 1.1 |
| 3 | 0 | if certified | 16773 | 9.8 | 3.3 | 0.8 |
| 3 | 1 | if certified | 16185 | 9.6 | 3.5 | 0.8 |
| 3 | 2 | if certified | 19611 | 12.0 | 3.4 | 0.9 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 1 | 90 | 12.2 | 10.1 | -17.0 | -13.8 | 90 of 90 | 0 |
| 2 | 90 | 99.8 | 95.7 | -4.1 | -13.2 | 90 of 90 | 0 |
| 3 | 90 | 113.2 | 110.2 | -2.6 | -10.3 | 90 of 90 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 90 | 12.2 | 11.1 | -9.1 | -1.3 | 90 of 90 |
| 2 | 90 | 99.8 | 98.9 | -0.9 | -0.9 | 90 of 90 |
| 3 | 90 | 113.2 | 113.0 | -0.1 | +0.2 | 90 of 90 |

Pre-decision of trivial coplanar pairs (`fast` against `p1`, parallel; `fasts` against `p1s`, serial; sum of the median times over chains, s; `fastchk`: the decision against the clips' outcome)

| step | chains | p1 s | fast s | change % | median per-chain % | p1s s | fasts s | change % | hash equal (fast, fastchk) | agree / mismatch / missed pairs |
|---|---|---|---|---|---|---|---|---|---|---|

Result hash of the probe runs (p1, p2s) differing from `off`: 0
