### Boolean chains probe (rows: 170; chains: 30)

#### Chain, each step on the previous rounded result: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=30) | step 2 (n=25) | step 3 (n=25) |
|---|---|---|---|
| input | 2 (0.1) | 1 (0.0) | 0 (0.0) |
| setup | 4 (0.1) | 3 (0.1) | 2 (0.1) |
| trees | 5 (0.2) | 2 (0.1) | 2 (0.1) |
| cull | 1 (0.1) | 0 (0.0) | 1 (0.0) |
| pairs | 7 (0.2) | 9 (0.2) | 12 (0.3) |
| planes | 0 (0.0) | 1 (0.1) | 10 (0.2) |
| intersect | 0 (0.0) | 2 (0.1) | 43 (1.3) |
| merge | 4 (0.1) | 4 (0.1) | 38 (1.6) |
| triangulation | 3 (0.1) | 6 (0.2) | 16 (0.5) |
| classification | 3 (0.1) | 4 (0.1) | 38 (1.1) |
| simplify | 0 (0.0) | 3 (0.1) | 2 (0.0) |
| assembly | 4 (0.1) | 7 (0.2) | 6 (0.2) |
| rounding | 16 (0.6) | 18 (0.9) | 37 (2.0) |
| TOTAL | 67 (1.9) | 74 (2.2) | 320 (7.5) |

Share of the sum over chains (%): step 1: pairs 12, planes 2, intersect 0, merge 6, triangulation 5, classification 5, rounding 29; step 2: pairs 11, planes 5, intersect 3, merge 5, triangulation 8, classification 4, rounding 39; step 3: pairs 4, planes 3, intersect 17, merge 22, triangulation 6, classification 15, rounding 26

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 0 / 0.0 | 0 / 0.0 | 150689 / 12.4 |
| prefilter_apart | 7761 / 0.0 | 56100 / 0.3 | 53590 / 0.1 |
| construct_apart | 0 / 0.0 | 0 / 0.0 | 9908 / 0.1 |
| meet_point | 0 / 0.0 | 1756 / 0.0 | 860139 / 8.0 |
| meet_segment | 6156 / 0.4 | 13339 / 2.3 | 241238 / 3.2 |
| co:share3 trivial | 0 / 0.0 | 0 / 0.0 | 88422 / 7.4 |
| co:share2 trivial | 0 / 0.0 | 0 / 0.0 | 43330 / 3.2 |
| co:share1 trivial | 0 / 0.0 | 0 / 0.0 | 18573 / 1.4 |
| co:adds (not trivial) | 0 / 0.0 | 0 / 0.0 | 8 / 0.0 |
| prep (touched triangles) | 6090 / 0.2 | 13965 / 0.7 | 205544 / 3.0 |
| prep trivial | 0 / 0.0 | 32 / 0.0 | 192546 / 2.8 |
| tri (touched triangles) | 6090 / 3.1 | 13933 / 10.9 | 194717 / 10.1 |
| tri trivial | 0 / 0.0 | 0 / 0.0 | 181719 / 6.3 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 1.14, step 2: 1.34, step 3: 1.68

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | no | 6348 | 3.4 | 5.8 | 6.5 |
| 1 | 1 | no (raw input) | 6348 | 1.0 | 1.6 | 3.5 |
| 2 | 0 | yes | 6626 | 5.3 | 7.2 | 6.8 |
| 2 | 1 | no (raw input) | 6348 | 3.7 | 4.0 | 4.7 |
| 3 | 0 | no | 12024 | 4.4 | 1.4 | 1.5 |
| 3 | 1 | no (raw input) | 6348 | 3.3 | 1.1 | 1.1 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 2 | 25 | 1.9 | 1.9 | +2.4 | -0.3 | 25 of 25 | 0 |
| 3 | 23 | 7.1 | 7.0 | -0.7 | -3.1 | 23 of 23 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 30 | 1.7 | 1.8 | +7.2 | -3.6 | 30 of 30 |
| 2 | 25 | 1.9 | 2.2 | +17.6 | +8.3 | 25 of 25 |
| 3 | 25 | 7.6 | 7.4 | -3.0 | -2.5 | 25 of 25 |

Result hash of the probe runs (p1, p2s) differing from `off`: 0

#### ExactMesh expression, one n-ary arrangement per step: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=30) | step 2 (n=30) | step 3 (n=30) |
|---|---|---|---|
| input | 1 (0.0) | 0 (0.0) | 0 (0.0) |
| setup | 4 (0.1) | 3 (0.1) | 3 (0.1) |
| trees | 5 (0.2) | 3 (0.1) | 3 (0.1) |
| cull | 0 (0.0) | 0 (0.0) | 0 (0.0) |
| pairs | 7 (0.2) | 11 (0.4) | 10 (0.3) |
| planes | 0 (0.0) | 2 (0.1) | 1 (0.1) |
| intersect | 0 (0.0) | 2 (0.1) | 2 (0.1) |
| merge | 4 (0.1) | 6 (0.2) | 3 (0.1) |
| triangulation | 3 (0.1) | 8 (0.2) | 5 (0.2) |
| classification | 3 (0.1) | 7 (0.2) | 4 (0.2) |
| simplify | 0 (0.0) | 5 (0.2) | 4 (0.1) |
| assembly | 4 (0.1) | 10 (0.3) | 6 (0.2) |
| rounding | 14 (0.4) | 14 (0.7) | 8 (0.4) |
| TOTAL | 56 (1.6) | 75 (2.6) | 57 (1.9) |

Share of the sum over chains (%): step 1: pairs 13, planes 2, intersect 0, merge 7, triangulation 5, classification 6, rounding 26; step 2: pairs 14, planes 4, intersect 3, merge 6, triangulation 9, classification 8, rounding 28; step 3: pairs 17, planes 4, intersect 3, merge 6, triangulation 9, classification 9, rounding 23

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| prefilter_apart | 7761 / 0.1 | 99990 / 0.3 | 99990 / 0.5 |
| construct_apart | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| meet_point | 0 / 0.0 | 2784 / 0.0 | 2784 / 0.0 |
| meet_segment | 6156 / 0.5 | 30546 / 2.6 | 30546 / 3.2 |
| co:share3 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:share2 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:share1 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:adds (not trivial) | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| prep (touched triangles) | 6090 / 0.3 | 30588 / 1.0 | 30588 / 1.1 |
| prep trivial | 0 / 0.0 | 48 / 0.0 | 48 / 0.0 |
| tri (touched triangles) | 6090 / 4.1 | 30540 / 13.9 | 30540 / 12.3 |
| tri trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 1.27, step 2: 1.62, step 3: 1.72

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | if certified | 6348 | 3.7 | 6.8 | 7.6 |
| 1 | 1 | if certified | 6348 | 1.4 | 2.1 | 4.9 |
| 2 | 0 | if certified | 6348 | 4.2 | 4.7 | 4.2 |
| 2 | 1 | if certified | 6348 | 3.9 | 4.4 | 3.9 |
| 2 | 2 | if certified | 6348 | 4.0 | 4.8 | 4.3 |
| 3 | 0 | if certified | 6348 | 2.7 | 5.2 | 5.1 |
| 3 | 1 | if certified | 6348 | 2.8 | 5.1 | 4.8 |
| 3 | 2 | if certified | 6348 | 3.5 | 6.4 | 5.3 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 1 | 30 | 1.6 | 1.6 | -1.7 | +1.7 | 30 of 30 | 0 |
| 2 | 30 | 3.1 | 2.3 | -26.5 | -28.0 | 30 of 30 | 0 |
| 3 | 30 | 1.9 | 1.7 | -10.5 | -13.7 | 30 of 30 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 30 | 1.6 | 1.9 | +14.7 | +24.4 | 30 of 30 |
| 2 | 30 | 3.1 | 2.6 | -14.7 | -15.7 | 30 of 30 |
| 3 | 30 | 1.9 | 1.9 | +2.8 | +3.6 | 30 of 30 |

Result hash of the probe runs (p1, p2s) differing from `off`: 0
