# Collapse point and certificate lifetime (measurement, no library change)

Output of `dotnet run -c Release --project benchmarks/ReducerBenchmark -- collapsepoint 3 <six Thingi10K files>` (Linux, 4 cores, net10.0, medians of 3). ReducerProbe / ReverseProbe are benchmark-only copies of mesh/Reducer.cs and mesh/ReducerReverseCoverage.cs; with the endpoint retry off they reproduce the library result bit for bit (column "check").

rounds: 3, processors: 4

(a) midpoint collapse attempts of FastCollapsePass(0.05 diag, 5) with MaxDeviationTwoSided, rejections by reason (an attempt is an edge per round)
| case | triangles | attempts | collapsed | rejected (retried) | by reason |
|---|---|---|---|---|---|
| bumpy sphere 40 @ 0.003 | 18252 | 17338 | 8659 | 8679 | Flip 3235, ForwardDev 5400, Reverse 44 |
| bumpy sphere 40 @ 0.01 | 18252 | 14126 | 8828 | 5298 | Flip 3443, ForwardDev 1434, Reverse 421 |
| spiky sphere 60 @ 0.003 | 41772 | 38446 | 20294 | 18152 | Flip 11920, ForwardDev 5828, Reverse 404 |
| spiky sphere 60 @ 0.01 | 41772 | 33142 | 20469 | 12673 | Flip 9736, ForwardDev 1918, Reverse 1019 |
| terrain 80 @ 0.003 | 12800 | 15325 | 5651 | 9674 | Flip 3143, ForwardDev 6445, Reverse 86 |
| terrain 80 @ 0.01 | 12800 | 11831 | 6019 | 5785 | Flip 4105, ForwardDev 1269, Reverse 411 |
| 1036464 @ 0.003 | 7068 | 6949 | 3202 | 3747 | Flip 1351, ForwardDev 2361, Reverse 35 |
| 1036464 @ 0.01 | 7068 | 5387 | 3373 | 2014 | Flip 1485, ForwardDev 492, Reverse 37 |
| 1436278 @ 0.003 | 18610 | 19543 | 9100 | 10443 | Flip 9500, ForwardDev 878, Reverse 65 |
| 1436278 @ 0.01 | 18610 | 18725 | 9186 | 9539 | Flip 9268, ForwardDev 135, Reverse 136 |
| 472201 @ 0.003 | 5572 | 3956 | 2623 | 1333 | Flip 846, ForwardDev 451, Reverse 36 |
| 472201 @ 0.01 | 5572 | 3787 | 2685 | 1102 | Flip 853, ForwardDev 191, Reverse 58 |
| 51011 @ 0.003 | 8656 | 11855 | 3511 | 8344 | Flip 2669, ForwardDev 5507, Reverse 168 |
| 51011 @ 0.01 | 8656 | 8082 | 3929 | 4153 | Flip 2737, ForwardDev 1198, Reverse 218 |
| 60516 @ 0.003 | 14628 | 16703 | 6563 | 10140 | Flip 5290, ForwardDev 4753, Reverse 97 |
| 60516 @ 0.01 | 14628 | 13908 | 6912 | 6994 | Flip 5664, ForwardDev 595, Reverse 735 |
| 894833 @ 0.003 | 7952 | 12887 | 3261 | 9626 | Flip 2226, ForwardDev 7351, Reverse 49 |
| 894833 @ 0.01 | 7952 | 7669 | 3705 | 3964 | Flip 2392, ForwardDev 1447, Reverse 125 |

(b) midpoint rejected, an endpoint would be accepted (dry run, every check repeated; feat = the end of larger angle defect kept)
| case | rejected | hit | hit rate | feature end ok | other end ok | both ok | by reason |
|---|---|---|---|---|---|---|---|
| bumpy sphere 40 @ 0.003 | 8679 | 3645 | 42.0 % | 2945 | 701 | 1 | Flip: 3235 rej, 973 hit (30.1 %), feat 606, other 367, both 0; ForwardDev: 5400 rej, 2634 hit (48.8 %), feat 2317, other 318, both 1; Reverse: 44 rej, 38 hit (86.4 %), feat 22, other 16, both 0 |
| bumpy sphere 40 @ 0.01 | 5298 | 2732 | 51.6 % | 1967 | 765 | 0 | Flip: 3443 rej, 1341 hit (38.9 %), feat 825, other 516, both 0; ForwardDev: 1434 rej, 1050 hit (73.2 %), feat 907, other 143, both 0; Reverse: 421 rej, 341 hit (81.0 %), feat 235, other 106, both 0 |
| spiky sphere 60 @ 0.003 | 18152 | 7712 | 42.5 % | 4044 | 3670 | 2 | Flip: 11920 rej, 4132 hit (34.7 %), feat 1169, other 2963, both 0; ForwardDev: 5828 rej, 3302 hit (56.7 %), feat 2675, other 629, both 2; Reverse: 404 rej, 278 hit (68.8 %), feat 200, other 78, both 0 |
| spiky sphere 60 @ 0.01 | 12673 | 5944 | 46.9 % | 3166 | 2778 | 0 | Flip: 9736 rej, 3707 hit (38.1 %), feat 1386, other 2321, both 0; ForwardDev: 1918 rej, 1410 hit (73.5 %), feat 1163, other 247, both 0; Reverse: 1019 rej, 827 hit (81.2 %), feat 617, other 210, both 0 |
| terrain 80 @ 0.003 | 9674 | 3337 | 34.5 % | 2809 | 528 | 0 | Flip: 3143 rej, 488 hit (15.5 %), feat 416, other 72, both 0; ForwardDev: 6445 rej, 2805 hit (43.5 %), feat 2354, other 451, both 0; Reverse: 86 rej, 44 hit (51.2 %), feat 39, other 5, both 0 |
| terrain 80 @ 0.01 | 5785 | 2849 | 49.2 % | 2362 | 489 | 2 | Flip: 4105 rej, 1547 hit (37.7 %), feat 1262, other 285, both 0; ForwardDev: 1269 rej, 985 hit (77.6 %), feat 903, other 84, both 2; Reverse: 411 rej, 317 hit (77.1 %), feat 197, other 120, both 0 |
| 1036464 @ 0.003 | 3747 | 1518 | 40.5 % | 916 | 602 | 0 | Flip: 1351 rej, 533 hit (39.5 %), feat 321, other 212, both 0; ForwardDev: 2361 rej, 962 hit (40.7 %), feat 589, other 373, both 0; Reverse: 35 rej, 23 hit (65.7 %), feat 6, other 17, both 0 |
| 1036464 @ 0.01 | 2014 | 1154 | 57.3 % | 692 | 462 | 0 | Flip: 1485 rej, 840 hit (56.6 %), feat 482, other 358, both 0; ForwardDev: 492 rej, 280 hit (56.9 %), feat 192, other 88, both 0; Reverse: 37 rej, 34 hit (91.9 %), feat 18, other 16, both 0 |
| 1436278 @ 0.003 | 10443 | 3230 | 30.9 % | 2065 | 1165 | 0 | Flip: 9500 rej, 2807 hit (29.5 %), feat 1775, other 1032, both 0; ForwardDev: 878 rej, 377 hit (42.9 %), feat 263, other 114, both 0; Reverse: 65 rej, 46 hit (70.8 %), feat 27, other 19, both 0 |
| 1436278 @ 0.01 | 9539 | 3271 | 34.3 % | 2043 | 1228 | 0 | Flip: 9268 rej, 3066 hit (33.1 %), feat 1900, other 1166, both 0; ForwardDev: 135 rej, 103 hit (76.3 %), feat 69, other 34, both 0; Reverse: 136 rej, 102 hit (75.0 %), feat 74, other 28, both 0 |
| 472201 @ 0.003 | 1333 | 849 | 63.7 % | 470 | 379 | 0 | Flip: 846 rej, 557 hit (65.8 %), feat 261, other 296, both 0; ForwardDev: 451 rej, 260 hit (57.6 %), feat 193, other 67, both 0; Reverse: 36 rej, 32 hit (88.9 %), feat 16, other 16, both 0 |
| 472201 @ 0.01 | 1102 | 802 | 72.8 % | 448 | 354 | 0 | Flip: 853 rej, 594 hit (69.6 %), feat 314, other 280, both 0; ForwardDev: 191 rej, 156 hit (81.7 %), feat 110, other 46, both 0; Reverse: 58 rej, 52 hit (89.7 %), feat 24, other 28, both 0 |
| 51011 @ 0.003 | 8344 | 2095 | 25.1 % | 1347 | 748 | 0 | Flip: 2669 rej, 666 hit (25.0 %), feat 457, other 209, both 0; ForwardDev: 5507 rej, 1318 hit (23.9 %), feat 815, other 503, both 0; Reverse: 168 rej, 111 hit (66.1 %), feat 75, other 36, both 0 |
| 51011 @ 0.01 | 4153 | 1637 | 39.4 % | 1131 | 506 | 0 | Flip: 2737 rej, 1011 hit (36.9 %), feat 688, other 323, both 0; ForwardDev: 1198 rej, 497 hit (41.5 %), feat 347, other 150, both 0; Reverse: 218 rej, 129 hit (59.2 %), feat 96, other 33, both 0 |
| 60516 @ 0.003 | 10140 | 2610 | 25.7 % | 1634 | 976 | 0 | Flip: 5290 rej, 1166 hit (22.0 %), feat 719, other 447, both 0; ForwardDev: 4753 rej, 1368 hit (28.8 %), feat 864, other 504, both 0; Reverse: 97 rej, 76 hit (78.4 %), feat 51, other 25, both 0 |
| 60516 @ 0.01 | 6994 | 1977 | 28.3 % | 1277 | 700 | 0 | Flip: 5664 rej, 1461 hit (25.8 %), feat 903, other 558, both 0; ForwardDev: 595 rej, 308 hit (51.8 %), feat 224, other 84, both 0; Reverse: 735 rej, 208 hit (28.3 %), feat 150, other 58, both 0 |
| 894833 @ 0.003 | 9626 | 1737 | 18.0 % | 1096 | 642 | 1 | Flip: 2226 rej, 662 hit (29.7 %), feat 426, other 236, both 0; ForwardDev: 7351 rej, 1043 hit (14.2 %), feat 645, other 398, both 0; Reverse: 49 rej, 32 hit (65.3 %), feat 25, other 8, both 1 |
| 894833 @ 0.01 | 3964 | 1582 | 39.9 % | 1084 | 498 | 0 | Flip: 2392 rej, 949 hit (39.7 %), feat 610, other 339, both 0; ForwardDev: 1447 rej, 547 hit (37.8 %), feat 427, other 120, both 0; Reverse: 125 rej, 86 hit (68.8 %), feat 47, other 39, both 0 |

(b2) why the endpoints were rejected (dry run)
| case | feature end rejected by | other end rejected by |
|---|---|---|
| bumpy sphere 40 @ 0.003 | Flip 3169, ForwardDev 2544, Reverse 21 | Flip 3314, ForwardDev 4642, Reverse 22 |
| bumpy sphere 40 @ 0.01 | Flip 2867, ForwardDev 331, Reverse 133 | Flip 3105, ForwardDev 1221, Reverse 207 |
| spiky sphere 60 @ 0.003 | Flip 11269, ForwardDev 2666, Reverse 173 | Flip 7675, ForwardDev 6431, Reverse 376 |
| spiky sphere 60 @ 0.01 | Flip 8626, ForwardDev 588, Reverse 293 | Flip 6807, ForwardDev 1995, Reverse 1093 |
| terrain 80 @ 0.003 | Flip 2491, ForwardDev 3325, Reverse 1049 | Flip 4058, ForwardDev 4930, Reverse 158 |
| terrain 80 @ 0.01 | Flip 3035, ForwardDev 253, Reverse 135 | Flip 4206, ForwardDev 966, Reverse 124 |
| 1036464 @ 0.003 | Flip 1311, ForwardDev 1501, Reverse 19 | Flip 1363, ForwardDev 1774, Reverse 8 |
| 1036464 @ 0.01 | Flip 1047, ForwardDev 264, Reverse 11 | Flip 1230, ForwardDev 322 |
| 1436278 @ 0.003 | Flip 7843, ForwardDev 519, Reverse 16 | Flip 8549, ForwardDev 697, Reverse 32 |
| 1436278 @ 0.01 | Flip 7399, ForwardDev 54, Reverse 43 | Flip 8144, ForwardDev 74, Reverse 93 |
| 472201 @ 0.003 | Flip 616, ForwardDev 242, Reverse 5 | Flip 605, ForwardDev 342, Reverse 7 |
| 472201 @ 0.01 | Flip 557, ForwardDev 76, Reverse 21 | Flip 616, ForwardDev 117, Reverse 15 |
| 51011 @ 0.003 | Flip 2865, ForwardDev 4074, Reverse 58 | Flip 3314, ForwardDev 4269, Reverse 13 |
| 51011 @ 0.01 | Flip 2198, ForwardDev 748, Reverse 76 | Flip 2597, ForwardDev 976, Reverse 74 |
| 60516 @ 0.003 | Flip 5330, ForwardDev 3148, Reverse 28 | Flip 5843, ForwardDev 3285, Reverse 36 |
| 60516 @ 0.01 | Flip 4946, ForwardDev 479, Reverse 292 | Flip 5374, ForwardDev 417, Reverse 503 |
| 894833 @ 0.003 | Flip 2248, ForwardDev 6270, Reverse 12 | Flip 2763, ForwardDev 6215, Reverse 6 |
| 894833 @ 0.01 | Flip 1865, ForwardDev 973, Reverse 42 | Flip 2151, ForwardDev 1285, Reverse 30 |

(c) full run with endpoint fallback (midpoint, then feature end, then other end) against the two-sided baseline; bounds are h(M,S) / h(S,M) upper bounds in eps
| case | baseline: triangles, ms | fallback: triangles, ms | time | baseline bounds | fallback bounds | collapses by fallback | repeated runs | check |
|---|---|---|---|---|---|---|---|---|
| bumpy sphere 40 @ 0.003 | 934, 6513 | 688, 12002 | 1.84x | 1.010 / 1.009 | 1.010 / 1.009 | 5660 | same | probe == lib (probe, no retry: 6577 ms) |
| bumpy sphere 40 @ 0.01 | 596, 6038 | 216, 10095 | 1.67x | 1.009 / 1.007 | 1.006 / 1.006 | 3541 | same | probe == lib (probe, no retry: 6103 ms) |
| spiky sphere 60 @ 0.003 | 1184, 16391 | 622, 39496 | 2.41x | 1.010 / 1.009 | 1.009 / 1.009 | 14253 | same | probe == lib (probe, no retry: 16373 ms) |
| spiky sphere 60 @ 0.01 | 834, 13613 | 236, 32268 | 2.37x | 1.008 / 1.010 | 1.007 / 1.009 | 8755 | same | probe == lib (probe, no retry: 13168 ms) |
| terrain 80 @ 0.003 | 1498, 2214 | 1354, 4192 | 1.89x | 1.010 / 1.006 | 1.009 / 1.006 | 4587 | same | probe == lib (probe, no retry: 2252 ms) |
| terrain 80 @ 0.01 | 762, 2397 | 404, 6035 | 2.52x | 1.009 / 1.006 | 1.008 / 1.001 | 3085 | same | probe == lib (probe, no retry: 2418 ms) |
| 1036464 @ 0.003 | 664, 3231 | 554, 4836 | 1.50x | 1.009 / 1.007 | 1.008 / 1.003 | 1602 | same | probe == lib (probe, no retry: 3221 ms) |
| 1036464 @ 0.01 | 322, 1247 | 250, 1979 | 1.59x | 1.009 / 1.006 | 1.006 / 0.999 | 1445 | same | probe == lib (probe, no retry: 1247 ms) |
| 1436278 @ 0.003 | 410, 7692 | 284, 17113 | 2.22x | 1.009 / 1.008 | 1.010 / 1.008 | 4452 | same | probe == lib (probe, no retry: 7784 ms) |
| 1436278 @ 0.01 | 238, 4133 | 140, 8218 | 1.99x | 1.005 / 0.960 | 1.007 / 1.002 | 3950 | same | probe == lib (probe, no retry: 4231 ms) |
| 472201 @ 0.003 | 326, 2762 | 254, 3663 | 1.33x | 1.009 / 1.002 | 1.007 / 0.997 | 1193 | same | probe == lib (probe, no retry: 2779 ms) |
| 472201 @ 0.01 | 202, 863 | 154, 1328 | 1.54x | 1.004 / 0.999 | 0.998 / 1.006 | 1003 | same | probe == lib (probe, no retry: 902 ms) |
| 51011 @ 0.003 | 1634, 1371 | 1338, 1963 | 1.43x | 1.006 / 1.008 | 1.009 / 1.008 | 1628 | same | probe == lib (probe, no retry: 1319 ms) |
| 51011 @ 0.01 | 798, 842 | 650, 1338 | 1.59x | 1.004 / 1.002 | 1.004 / 1.003 | 1446 | same | probe == lib (probe, no retry: 859 ms) |
| 60516 @ 0.003 | 1502, 5889 | 1082, 9058 | 1.54x | 1.009 / 1.009 | 1.010 / 1.006 | 3193 | same | probe == lib (probe, no retry: 5751 ms) |
| 60516 @ 0.01 | 804, 2969 | 644, 4923 | 1.66x | 1.010 / 1.008 | 1.006 / 1.003 | 2505 | same | probe == lib (probe, no retry: 2923 ms) |
| 894833 @ 0.003 | 1430, 1592 | 1180, 2315 | 1.45x | 1.009 / 1.001 | 1.010 / 1.005 | 1420 | same | probe == lib (probe, no retry: 1533 ms) |
| 894833 @ 0.01 | 542, 913 | 408, 1212 | 1.33x | 1.008 / 1.002 | 1.008 / 1.005 | 1291 | same | probe == lib (probe, no retry: 932 ms) |

(4) certificate lifetime (baseline sequence; committed / refused collapses; certificates evaluated incl. refused checks; recertification events of committed collapses: actual, with the longest-lived single-triangle carrier chosen in hindsight; share of the time in the reverse check; upper bound of the saved share of the pass)
| case | committed / refused | evaluated | events | hindsight | saved | reverse check time | saved share of total time (bound) |
|---|---|---|---|---|---|---|---|
| bumpy sphere 40 @ 0.003 | 8659 / 44 | 442969 | 441540 | 441540 | 0 (0.0 % of events, 0.0 % of evaluations) | 1088 of 6577 ms (17 %) | 0.0 % |
| bumpy sphere 40 @ 0.01 | 8828 / 421 | 588454 | 565624 | 484029 | 81595 (14.4 % of events, 13.9 % of evaluations) | 833 of 6103 ms (14 %) | 1.9 % |
| spiky sphere 60 @ 0.003 | 20294 / 404 | 1194285 | 1175943 | 1166122 | 9821 (0.8 % of events, 0.8 % of evaluations) | 2175 of 16373 ms (13 %) | 0.1 % |
| spiky sphere 60 @ 0.01 | 20469 / 1019 | 1722924 | 1634287 | 1270769 | 363518 (22.2 % of events, 21.1 % of evaluations) | 2348 of 13168 ms (18 %) | 3.8 % |
| terrain 80 @ 0.003 | 5651 / 86 | 175785 | 172753 | 172753 | 0 (0.0 % of events, 0.0 % of evaluations) | 432 of 2252 ms (19 %) | 0.0 % |
| terrain 80 @ 0.01 | 6019 / 411 | 308950 | 286288 | 225364 | 60924 (21.3 % of events, 19.7 % of evaluations) | 425 of 2418 ms (18 %) | 3.5 % |
| 1036464 @ 0.003 | 3202 / 35 | 122831 | 121390 | 116816 | 4574 (3.8 % of events, 3.7 % of evaluations) | 1829 of 3221 ms (57 %) | 2.1 % |
| 1036464 @ 0.01 | 3373 / 37 | 175412 | 173350 | 128487 | 44863 (25.9 % of events, 25.6 % of evaluations) | 488 of 1247 ms (39 %) | 10.0 % |
| 1436278 @ 0.003 | 9100 / 65 | 716533 | 708130 | 541242 | 166888 (23.6 % of events, 23.3 % of evaluations) | 3136 of 7784 ms (40 %) | 9.4 % |
| 1436278 @ 0.01 | 9186 / 136 | 906515 | 824538 | 554464 | 270074 (32.8 % of events, 29.8 % of evaluations) | 1562 of 4231 ms (37 %) | 11.0 % |
| 472201 @ 0.003 | 2623 / 36 | 126617 | 120997 | 106899 | 14098 (11.7 % of events, 11.1 % of evaluations) | 1072 of 2779 ms (39 %) | 4.3 % |
| 472201 @ 0.01 | 2685 / 58 | 125860 | 118116 | 86275 | 31841 (27.0 % of events, 25.3 % of evaluations) | 268 of 902 ms (30 %) | 7.5 % |
| 51011 @ 0.003 | 3511 / 168 | 99132 | 94400 | 92997 | 1403 (1.5 % of events, 1.4 % of evaluations) | 226 of 1319 ms (17 %) | 0.2 % |
| 51011 @ 0.01 | 3929 / 218 | 147983 | 136145 | 97246 | 38899 (28.6 % of events, 26.3 % of evaluations) | 234 of 859 ms (27 %) | 7.2 % |
| 60516 @ 0.003 | 6563 / 97 | 329770 | 321064 | 291032 | 30032 (9.4 % of events, 9.1 % of evaluations) | 1654 of 5751 ms (29 %) | 2.6 % |
| 60516 @ 0.01 | 6912 / 735 | 498771 | 451900 | 332636 | 119264 (26.4 % of events, 23.9 % of evaluations) | 810 of 2923 ms (28 %) | 6.6 % |
| 894833 @ 0.003 | 3261 / 49 | 99665 | 97825 | 95480 | 2345 (2.4 % of events, 2.4 % of evaluations) | 374 of 1533 ms (24 %) | 0.6 % |
| 894833 @ 0.01 | 3705 / 125 | 142797 | 133182 | 108264 | 24918 (18.7 % of events, 17.4 % of evaluations) | 248 of 932 ms (27 %) | 4.7 % |
