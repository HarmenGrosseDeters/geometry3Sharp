### Boolean chains probe (rows: 266; chains: 90)

#### Chain, each step on the previous rounded result: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=90) | step 2 (n=88) | step 3 (n=88) |
|---|---|---|---|
| input | 5 (0.5) | 2 (0.2) | 2 (0.1) |
| setup | 11 (1.0) | 5 (0.5) | 6 (0.5) |
| trees | 12 (1.2) | 6 (0.5) | 6 (0.5) |
| cull | 2 (0.2) | 0 (0.1) | 4 (0.4) |
| pairs | 24 (2.4) | 32 (3.5) | 45 (5.0) |
| planes | 2 (0.2) | 2 (0.9) | 30 (3.2) |
| intersect | 1 (0.8) | 4 (12.7) | 335 (41.2) |
| merge | 8 (1.2) | 9 (5.3) | 118 (12.1) |
| triangulation | 4 (0.9) | 10 (13.4) | 136 (20.2) |
| classification | 8 (2.6) | 14 (18.2) | 183 (23.4) |
| simplify | 2 (0.8) | 7 (4.5) | 7 (2.7) |
| assembly | 11 (1.6) | 16 (5.4) | 14 (3.7) |
| rounding | 36 (3.4) | 36 (14.5) | 42 (47.5) |
| TOTAL | 140 (16.4) | 143 (75.9) | 1185 (158.6) |

Share of the sum over chains (%): step 1: pairs 15, planes 1, intersect 5, merge 7, triangulation 6, classification 16, rounding 21; step 2: pairs 5, planes 1, intersect 17, merge 7, triangulation 18, classification 24, rounding 19; step 3: pairs 3, planes 2, intersect 26, merge 8, triangulation 13, classification 15, rounding 30

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 167752 / 0.0 | 2125458 / 1.0 | 7525598 / 23.3 |
| prefilter_apart | 37145 / 0.0 | 216135 / 0.0 | 302352 / 0.0 |
| construct_apart | 17718 / 0.1 | 152492 / 0.1 | 230072 / 0.2 |
| meet_point | 5346 / 0.0 | 70165 / 0.0 | 3672630 / 1.9 |
| meet_segment | 49896 / 0.5 | 194204 / 0.4 | 1215872 / 1.4 |
| co:share3 trivial | 0 / 0.0 | 3675 / 0.0 | 661078 / 2.5 |
| co:share2 trivial | 0 / 0.0 | 10570 / 0.0 | 1180909 / 1.9 |
| co:share1 trivial | 11 / 0.0 | 30936 / 0.0 | 3931369 / 8.3 |
| co:adds (not trivial) | 104104 / 0.0 | 1025400 / 0.5 | 901895 / 0.8 |
| prep (touched triangles) | 85728 / 0.4 | 542422 / 0.9 | 2051288 / 4.2 |
| prep trivial | 4569 / 0.0 | 28229 / 0.0 | 1480024 / 2.1 |
| tri (touched triangles) | 81345 / 4.4 | 522953 / 7.6 | 1960537 / 11.2 |
| tri trivial | 186 / 0.0 | 8760 / 0.0 | 1389273 / 4.1 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 1.38, step 2: 1.67, step 3: 1.73

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | no | 16415 | 11.5 | 8.5 | 8.7 |
| 1 | 1 | no (raw input) | 13962 | 2.4 | 1.7 | 4.5 |
| 2 | 0 | no | 17560 | 16.1 | 4.1 | 1.9 |
| 2 | 1 | no (raw input) | 18284 | 13.1 | 2.9 | 1.5 |
| 3 | 0 | no | 24956 | 15.7 | 1.4 | 1.0 |
| 3 | 1 | no (raw input) | 16773 | 10.2 | 0.9 | 0.7 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 2 | 86 | 74.5 | 73.3 | -1.6 | -3.1 | 86 of 86 | 0 |
| 3 | 81 | 151.6 | 150.3 | -0.9 | -1.4 | 81 of 81 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 90 | 13.8 | 13.7 | -0.5 | +0.8 | 90 of 90 |
| 2 | 88 | 75.8 | 76.4 | +0.8 | +0.6 | 88 of 88 |
| 3 | 88 | 160.0 | 153.1 | -4.3 | -3.9 | 88 of 88 |

Result hash of the probe runs (p1, p2s) differing from `off`: 0
