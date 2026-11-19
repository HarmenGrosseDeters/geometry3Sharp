### Boolean chains probe (rows: 102; chains: 18)

#### Chain, each step on the previous rounded result: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=18) | step 2 (n=15) | step 3 (n=15) |
|---|---|---|---|
| input | 2 (0.0) | 2 (0.0) | 1 (0.0) |
| setup | 4 (0.1) | 4 (0.1) | 3 (0.0) |
| trees | 7 (0.1) | 8 (0.1) | 5 (0.1) |
| cull | 0 (0.0) | 0 (0.0) | 3 (0.0) |
| pairs | 12 (0.2) | 22 (0.3) | 35 (0.5) |
| planes | 1 (0.0) | 2 (0.0) | 22 (0.3) |
| intersect | 0 (0.0) | 1 (0.0) | 149 (2.4) |
| merge | 4 (0.1) | 8 (0.1) | 116 (1.4) |
| triangulation | 4 (0.1) | 8 (0.2) | 56 (0.9) |
| classification | 2 (0.0) | 4 (0.1) | 114 (1.9) |
| simplify | 0 (0.0) | 1 (0.0) | 2 (0.0) |
| assembly | 4 (0.1) | 7 (0.1) | 6 (0.1) |
| rounding | 15 (0.3) | 26 (0.5) | 48 (0.7) |
| TOTAL | 71 (1.3) | 99 (1.7) | 621 (8.5) |

Share of the sum over chains (%): step 1: pairs 18, planes 2, intersect 1, merge 9, triangulation 9, classification 4, rounding 24; step 2: pairs 20, planes 2, intersect 1, merge 9, triangulation 10, classification 4, rounding 27; step 3: pairs 6, planes 4, intersect 29, merge 17, triangulation 10, classification 22, rounding 8

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 0 / 0.0 | 0 / 0.0 | 108677 / 14.1 |
| prefilter_apart | 5049 / 0.1 | 7823 / 0.1 | 14159 / 0.1 |
| construct_apart | 0 / 0.0 | 0 / 0.0 | 7672 / 0.2 |
| meet_point | 0 / 0.0 | 0 / 0.0 | 602231 / 8.9 |
| meet_segment | 4107 / 0.5 | 5913 / 1.1 | 168883 / 2.7 |
| co:share3 trivial | 0 / 0.0 | 0 / 0.0 | 63779 / 8.8 |
| co:share2 trivial | 0 / 0.0 | 0 / 0.0 | 30990 / 3.1 |
| co:share1 trivial | 0 / 0.0 | 0 / 0.0 | 13586 / 1.4 |
| co:adds (not trivial) | 0 / 0.0 | 0 / 0.0 | 8 / 0.0 |
| prep (touched triangles) | 4059 / 0.3 | 5815 / 0.4 | 141063 / 3.1 |
| prep trivial | 0 / 0.0 | 0 / 0.0 | 135565 / 3.1 |
| tri (touched triangles) | 4059 / 5.0 | 5815 / 7.4 | 135424 / 9.2 |
| tri trivial | 0 / 0.0 | 0 / 0.0 | 129926 / 6.2 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 1.00, step 2: 1.00, step 3: 1.04

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | no | 6348 | 9.8 | 10.8 | 10.9 |
| 1 | 1 | no (raw input) | 6348 | 2.8 | 2.8 | 5.7 |
| 2 | 0 | yes | 6828 | 12.5 | 11.7 | 11.9 |
| 2 | 1 | no (raw input) | 6348 | 9.7 | 9.3 | 6.8 |
| 3 | 0 | yes | 12190 | 11.0 | 1.7 | 2.1 |
| 3 | 1 | no (raw input) | 6348 | 8.1 | 1.4 | 1.4 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 2 | 15 | 1.7 | 1.4 | -16.3 | -13.6 | 15 of 15 | 0 |
| 3 | 15 | 8.4 | 9.5 | +12.4 | +4.3 | 15 of 15 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 18 | 1.3 | 1.3 | +1.3 | +0.7 | 18 of 18 |
| 2 | 15 | 1.7 | 1.7 | +2.4 | +1.2 | 15 of 15 |
| 3 | 15 | 8.4 | 8.2 | -3.0 | -5.3 | 15 of 15 |

Pre-decision of trivial coplanar pairs (`fast` against `p1`, parallel; `fasts` against `p1s`, serial; sum of the median times over chains, s; `fastchk`: the decision against the clips' outcome)

| step | chains | p1 s | fast s | change % | median per-chain % | p1s s | fasts s | change % | hash equal (fast, fastchk) | agree / mismatch / missed pairs |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 18 | 1.3 | 1.3 | +2.5 | +1.6 | 1.2 | 1.4 | +9.6 | 18, 18 of 18 | 0 / 0 / 0 |
| 2 | 15 | 1.7 | 1.7 | -0.8 | -0.6 | 1.6 | 1.7 | +6.8 | 15, 15 of 15 | 0 / 0 / 0 |
| 3 | 15 | 8.4 | 6.5 | -22.7 | -21.1 | 9.2 | 7.2 | -22.2 | 15, 15 of 15 | 108355 / 0 / 0 |

Result hash of the probe runs (p1, p2s) differing from `off`: 0

#### ExactMesh expression, one n-ary arrangement per step: time split of today's boolean (`off`; median over chains, ms; sum over chains in brackets, s)

| phase | step 1 (n=18) | step 2 (n=18) | step 3 (n=18) |
|---|---|---|---|
| input | 2 (0.0) | 3 (0.1) | 3 (0.0) |
| setup | 4 (0.1) | 6 (0.1) | 5 (0.1) |
| trees | 7 (0.1) | 11 (0.2) | 6 (0.1) |
| cull | 0 (0.0) | 0 (0.0) | 0 (0.0) |
| pairs | 12 (0.2) | 34 (0.6) | 32 (0.6) |
| planes | 1 (0.0) | 5 (0.1) | 2 (0.0) |
| intersect | 0 (0.0) | 2 (0.0) | 2 (0.0) |
| merge | 5 (0.1) | 14 (0.3) | 9 (0.2) |
| triangulation | 4 (0.1) | 21 (0.4) | 13 (0.3) |
| classification | 3 (0.0) | 10 (0.2) | 10 (0.2) |
| simplify | 0 (0.0) | 5 (0.1) | 4 (0.1) |
| assembly | 4 (0.1) | 14 (0.2) | 8 (0.2) |
| rounding | 16 (0.3) | 28 (0.6) | 18 (0.4) |
| TOTAL | 71 (1.3) | 171 (3.0) | 112 (2.2) |

Share of the sum over chains (%): step 1: pairs 17, planes 2, intersect 1, merge 9, triangulation 9, classification 4, rounding 23; step 2: pairs 21, planes 3, intersect 1, merge 10, triangulation 14, classification 7, rounding 19; step 3: pairs 26, planes 2, intersect 2, merge 8, triangulation 11, classification 9, rounding 16

#### Pair stage and triangulation by class, serial CPU (`p2s`; median over chains of the share of the run's phase sum, %; counts: sum over chains)

| class | step 1 n / % time | step 2 n / % time | step 3 n / % time |
|---|---|---|---|
| sep_t | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| sep_u | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| coplanar | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| prefilter_apart | 5049 / 0.1 | 22533 / 0.2 | 22533 / 0.2 |
| construct_apart | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| meet_point | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| meet_segment | 4107 / 0.5 | 17460 / 1.6 | 17460 / 1.6 |
| co:share3 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:share2 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:share1 trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| co:adds (not trivial) | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| prep (touched triangles) | 4059 / 0.2 | 17100 / 0.8 | 17100 / 0.8 |
| prep trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |
| tri (touched triangles) | 4059 / 4.6 | 17100 / 15.7 | 17100 / 10.0 |
| tri trivial | 0 / 0.0 | 0 / 0.0 | 0 / 0.0 |

Probe overhead (p2s serial against off, median ratio of the whole boolean): step 1: 0.99, step 2: 0.99, step 3: 0.99

#### Self-join of the operands (lever 2): wall time in the parallel run `p1`, as a share of the boolean (median over chains, %), and what skipping it gives

| step | operand | certified | triangles (median) | self-join ms (median) | share of boolean % (median) | share of sums % |
|---|---|---|---|---|---|---|
| 1 | 0 | if certified | 6348 | 10.1 | 10.6 | 10.6 |
| 1 | 1 | if certified | 6348 | 2.7 | 2.6 | 5.7 |
| 2 | 0 | if certified | 6348 | 11.2 | 6.7 | 6.6 |
| 2 | 1 | if certified | 6348 | 9.8 | 6.2 | 6.2 |
| 2 | 2 | if certified | 6348 | 10.4 | 6.2 | 6.3 |
| 3 | 0 | if certified | 6348 | 10.1 | 8.1 | 8.4 |
| 3 | 1 | if certified | 6348 | 9.7 | 7.5 | 7.8 |
| 3 | 2 | if certified | 6348 | 10.2 | 8.5 | 8.3 |

Skip experiment (`skip` against `p1`, same process, same counters; sum of the median times over chains, s): 

| step | chains with skip | p1 s | skip s | change % | median per-chain change % | hash equal | skipped operands unclean (hash differs) |
|---|---|---|---|---|---|---|---|
| 1 | 18 | 1.3 | 1.1 | -14.8 | -15.7 | 18 of 18 | 0 |
| 2 | 18 | 3.0 | 2.5 | -18.1 | -23.8 | 18 of 18 | 0 |
| 3 | 18 | 2.2 | 1.6 | -24.4 | -24.1 | 18 of 18 | 0 |

Trivial-coplanar experiment (`triv` against `p1`): 

| step | chains | p1 s | triv s | change % | median per-chain change % | hash equal |
|---|---|---|---|---|---|---|
| 1 | 18 | 1.3 | 1.3 | +4.0 | -0.2 | 18 of 18 |
| 2 | 18 | 3.0 | 3.0 | +0.7 | -1.8 | 18 of 18 |
| 3 | 18 | 2.2 | 2.3 | +6.2 | +0.7 | 18 of 18 |

Pre-decision of trivial coplanar pairs (`fast` against `p1`, parallel; `fasts` against `p1s`, serial; sum of the median times over chains, s; `fastchk`: the decision against the clips' outcome)

| step | chains | p1 s | fast s | change % | median per-chain % | p1s s | fasts s | change % | hash equal (fast, fastchk) | agree / mismatch / missed pairs |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 18 | 1.3 | 1.3 | +1.0 | +0.2 | 1.3 | 1.3 | -0.7 | 18, 18 of 18 | 0 / 0 / 0 |
| 2 | 18 | 3.0 | 3.0 | -0.3 | -1.7 | 3.0 | 3.0 | -1.2 | 18, 18 of 18 | 0 / 0 / 0 |
| 3 | 18 | 2.2 | 2.3 | +5.0 | +2.8 | 2.2 | 2.2 | -2.7 | 18, 18 of 18 | 0 / 0 / 0 |

Result hash of the probe runs (p1, p2s) differing from `off`: 0
