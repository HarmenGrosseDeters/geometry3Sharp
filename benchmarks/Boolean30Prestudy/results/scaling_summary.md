
### all

ops with all three core counts: 219

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| input | 48.6 | 37.2 | 36.9 | 1.31 | 0.32 | 7.0 % | 4.7 % |
| setup | 21.0 | 17.2 | 16.2 | 1.30 | 0.31 | 3.1 % | 2.1 % |
| intersections | 483.4 | 265.5 | 163.6 | 2.95 | 0.88 | 31.0 % | 8.1 % |
| merge | 71.6 | 55.5 | 53.4 | 1.34 | 0.34 | 10.1 % | 6.7 % |
| triangulation | 114.6 | 65.1 | 47.9 | 2.39 | 0.78 | 9.1 % | 3.6 % |
| classification | 103.8 | 76.4 | 74.8 | 1.39 | 0.37 | 14.2 % | 9.2 % |
| simplify | 21.6 | 18.8 | 19.8 | 1.09 | 0.11 | 3.8 % | 2.7 % |
| assembly | 32.6 | 25.3 | 25.4 | 1.28 | 0.29 | 4.8 % | 3.3 % |
| rounding | 188.7 | 111.3 | 90.2 | 2.09 | 0.70 | 17.1 % | 8.1 % |
| total | 1085.9 | 672.4 | 528.2 | 2.06 | 0.68 | 100.0 % | 48.6 % |

projection (Amdahl per phase, fitted 1 -> 4): 8 cores 435.3 s, 16 cores 388.8 s, 64 cores 353.9 s (4 cores measured 528.2 s); serial floor sum t1 (1 - f) = 342.3 s

- ops > 5 s at 1 core(s): 69
- ops > 5 s at 2 core(s): 29
- ops > 5 s at 4 core(s): 22


### all, sub-phases of intersections and merge

ops with all three core counts: 219

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| intersections.trees | 28.2 | 15.4 | 15.1 | 1.87 | 0.62 | 2.9 % | 1.5 % |
| intersections.pairs | 405.0 | 217.6 | 123.8 | 3.27 | 0.93 | 23.4 % | 4.3 % |
| intersections.planes | 13.9 | 8.3 | 6.8 | 2.04 | 0.68 | 1.3 % | 0.6 % |
| intersections.intersect | 36.0 | 23.9 | 17.6 | 2.05 | 0.68 | 3.3 % | 1.6 % |
| merge.points | 57.2 | 44.3 | 43.6 | 1.31 | 0.32 | 8.3 % | 5.6 % |
| merge.crossings | 11.3 | 8.8 | 7.4 | 1.53 | 0.46 | 1.4 % | 0.9 % |
| merge.points2 | 2.8 | 2.3 | 2.2 | 1.29 | 0.30 | 0.4 % | 0.3 % |
| total | 1085.9 | 672.4 | 528.2 | 2.06 | 0.68 | 100.0 % | 48.6 % |

- ops > 5 s at 1 core(s): 69
- ops > 5 s at 2 core(s): 29
- ops > 5 s at 4 core(s): 22


### cohort b20set

ops with all three core counts: 126

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| input | 2.2 | 1.6 | 1.5 | 1.49 | 0.44 | 0.9 % | 0.5 % |
| setup | 5.1 | 4.2 | 3.9 | 1.32 | 0.32 | 2.2 % | 1.5 % |
| intersections | 190.0 | 104.9 | 65.0 | 2.92 | 0.88 | 37.6 % | 10.1 % |
| merge | 16.7 | 10.7 | 9.0 | 1.84 | 0.61 | 5.2 % | 2.8 % |
| triangulation | 61.7 | 34.6 | 24.1 | 2.57 | 0.81 | 13.9 % | 5.0 % |
| classification | 21.6 | 15.0 | 13.4 | 1.61 | 0.51 | 7.8 % | 4.6 % |
| simplify | 14.8 | 12.9 | 13.5 | 1.09 | 0.11 | 7.8 % | 5.7 % |
| assembly | 11.8 | 9.9 | 10.0 | 1.18 | 0.20 | 5.8 % | 4.1 % |
| rounding | 86.0 | 46.9 | 32.4 | 2.65 | 0.83 | 18.8 % | 6.3 % |
| total | 409.9 | 240.6 | 172.7 | 2.37 | 0.77 | 100.0 % | 40.7 % |

projection (Amdahl per phase, fitted 1 -> 4): 8 cores 133.2 s, 16 cores 113.5 s, 64 cores 98.6 s (4 cores measured 172.7 s); serial floor sum t1 (1 - f) = 93.7 s

- ops > 5 s at 1 core(s): 34
- ops > 5 s at 2 core(s): 11
- ops > 5 s at 4 core(s): 6


### cohort open

ops with all three core counts: 36

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| input | 43.9 | 33.9 | 34.0 | 1.29 | 0.30 | 12.9 % | 8.8 % |
| setup | 9.4 | 8.3 | 7.7 | 1.23 | 0.25 | 2.9 % | 2.0 % |
| intersections | 223.4 | 121.7 | 72.3 | 3.09 | 0.90 | 27.5 % | 6.3 % |
| merge | 38.7 | 34.5 | 33.9 | 1.14 | 0.17 | 12.9 % | 9.2 % |
| triangulation | 27.1 | 16.2 | 12.0 | 2.27 | 0.75 | 4.5 % | 2.0 % |
| classification | 67.0 | 51.5 | 51.4 | 1.31 | 0.31 | 19.5 % | 13.2 % |
| simplify | 4.5 | 3.9 | 4.1 | 1.08 | 0.09 | 1.6 % | 1.2 % |
| assembly | 12.9 | 10.6 | 10.5 | 1.23 | 0.25 | 4.0 % | 2.8 % |
| rounding | 66.7 | 43.2 | 37.1 | 1.80 | 0.59 | 14.1 % | 7.8 % |
| total | 493.8 | 323.7 | 263.0 | 1.88 | 0.62 | 100.0 % | 53.1 % |

projection (Amdahl per phase, fitted 1 -> 4): 8 cores 224.5 s, 16 cores 205.3 s, 64 cores 190.9 s (4 cores measured 263.0 s); serial floor sum t1 (1 - f) = 186.1 s

- ops > 5 s at 1 core(s): 19
- ops > 5 s at 2 core(s): 14
- ops > 5 s at 4 core(s): 13


### cohort selfint

ops with all three core counts: 45

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| input | 0.6 | 0.5 | 0.5 | 1.24 | 0.26 | 1.1 % | 0.8 % |
| setup | 1.3 | 1.1 | 1.2 | 1.10 | 0.12 | 2.6 % | 1.9 % |
| intersections | 27.7 | 16.4 | 11.2 | 2.48 | 0.80 | 25.0 % | 9.5 % |
| merge | 11.4 | 7.4 | 7.6 | 1.50 | 0.45 | 17.0 % | 10.6 % |
| triangulation | 17.5 | 9.3 | 7.3 | 2.42 | 0.78 | 16.3 % | 6.4 % |
| classification | 7.0 | 4.3 | 3.9 | 1.80 | 0.59 | 8.7 % | 4.8 % |
| simplify | 1.5 | 1.3 | 1.4 | 1.08 | 0.10 | 3.1 % | 2.2 % |
| assembly | 3.0 | 1.8 | 1.9 | 1.57 | 0.48 | 4.2 % | 2.6 % |
| rounding | 16.1 | 10.0 | 9.8 | 1.64 | 0.52 | 22.0 % | 13.0 % |
| total | 86.1 | 52.0 | 44.6 | 1.93 | 0.64 | 100.0 % | 51.8 % |

projection (Amdahl per phase, fitted 1 -> 4): 8 cores 37.7 s, 16 cores 34.2 s, 64 cores 31.7 s (4 cores measured 44.6 s); serial floor sum t1 (1 - f) = 30.8 s

- ops > 5 s at 1 core(s): 5
- ops > 5 s at 2 core(s): 0
- ops > 5 s at 4 core(s): 0


### cohort unknown

ops with all three core counts: 12

| phase | 1 core s | 2 cores s | 4 cores s | speedup 4 | Amdahl f | share at 4 | bound: perfect 4-core scaling |
|---|---|---|---|---|---|---|---|
| input | 1.9 | 1.1 | 1.0 | 1.88 | 0.62 | 2.1 % | 1.1 % |
| setup | 5.2 | 3.6 | 3.5 | 1.49 | 0.44 | 7.3 % | 4.6 % |
| intersections | 42.2 | 22.6 | 15.1 | 2.79 | 0.86 | 31.6 % | 9.6 % |
| merge | 4.7 | 2.9 | 2.8 | 1.68 | 0.54 | 5.9 % | 3.4 % |
| triangulation | 8.2 | 5.0 | 4.6 | 1.78 | 0.58 | 9.6 % | 5.3 % |
| classification | 8.2 | 5.6 | 6.1 | 1.34 | 0.34 | 12.8 % | 8.5 % |
| simplify | 0.9 | 0.9 | 0.8 | 1.13 | 0.15 | 1.7 % | 1.2 % |
| assembly | 5.0 | 3.0 | 3.0 | 1.64 | 0.52 | 6.3 % | 3.7 % |
| rounding | 19.8 | 11.3 | 10.9 | 1.83 | 0.60 | 22.7 % | 12.3 % |
| total | 96.1 | 56.0 | 47.9 | 2.01 | 0.67 | 100.0 % | 49.8 % |

projection (Amdahl per phase, fitted 1 -> 4): 8 cores 39.8 s, 16 cores 35.8 s, 64 cores 32.8 s (4 cores measured 47.9 s); serial floor sum t1 (1 - f) = 31.8 s

- ops > 5 s at 1 core(s): 11
- ops > 5 s at 2 core(s): 4
- ops > 5 s at 4 core(s): 3


result hashes differing across core counts: 0 []

| model | op | cohort | 1 core ms | 4 cores ms | speedup | slowest phase at 4 |
|---|---|---|---|---|---|---|
| 1088138 | union | open_rand2 | 48577 | 25832 | 1.88 | rounding 6819 |
| 1088051 | union | open_rand2 | 46854 | 24315 | 1.93 | rounding 6681 |
| 1088054 | union | open_rand2 | 45906 | 23932 | 1.92 | rounding 6110 |
| 1087144 | union | open_rand2 | 46345 | 23450 | 1.98 | rounding 5848 |
| 519511 | union | b20set | 47089 | 18152 | 2.59 | intersections 5666 |
| 1088138 | difference | open_rand2 | 29442 | 17535 | 1.68 | intersections 5632 |
| 1088051 | difference | open_rand2 | 30915 | 17186 | 1.80 | intersections 5107 |
| 1088054 | difference | open_rand2 | 29814 | 16999 | 1.75 | intersections 5579 |
| 1087144 | difference | open_rand2 | 29236 | 15722 | 1.86 | intersections 4997 |
| 1088138 | intersection | open_rand2 | 28977 | 15398 | 1.88 | intersections 5837 |
| 1088054 | intersection | open_rand2 | 27726 | 15344 | 1.81 | intersections 5447 |
| 1087144 | intersection | open_rand2 | 28504 | 15295 | 1.86 | intersections 5491 |
| 1088051 | intersection | open_rand2 | 28478 | 15066 | 1.89 | intersections 5553 |
| 519511 | difference | b20set | 30421 | 12853 | 2.37 | intersections 5308 |
| 519511 | intersection | b20set | 29386 | 12697 | 2.31 | intersections 5806 |
| 1041432 | union | unknown_rand2 | 12622 | 7068 | 1.79 | rounding 2624 |
| 122796 | union | open_rand2 | 12136 | 6472 | 1.88 | classification 1969 |
| 1041448 | union | unknown_rand2 | 12549 | 6275 | 2.00 | rounding 2031 |
| 1517923 | union | b20set | 16204 | 5612 | 2.89 | intersections 3401 |
| 1231078 | union | unknown_rand2 | 7908 | 5235 | 1.51 | rounding 1651 |
| 519507 | union | b20set | 12530 | 5105 | 2.45 | intersections 1472 |
| 221686 | union | b20set | 10811 | 5076 | 2.13 | intersections 1255 |
| 668056 | union | b20set | 13821 | 4746 | 2.91 | intersections 1271 |
| 667995 | union | b20set | 12575 | 4713 | 2.67 | triangulation 1160 |
| 1517923 | intersection | b20set | 14067 | 4707 | 2.99 | intersections 3098 |
| 668057 | union | b20set | 13781 | 4670 | 2.95 | rounding 1187 |
| 1517923 | difference | b20set | 13497 | 4523 | 2.98 | intersections 2983 |
| 1041432 | difference | unknown_rand2 | 8278 | 4494 | 1.84 | rounding 1582 |
| 112826 | union | selfint_rand2 | 7426 | 4391 | 1.69 | rounding 1895 |
| 1017015 | union | open_rand2 | 10022 | 4373 | 2.29 | merge 1207 |
| 119829 | union | b20set | 5751 | 3993 | 1.44 | rounding 2395 |
| 1041448 | difference | unknown_rand2 | 9804 | 3908 | 2.51 | intersections 1194 |
| 1036403 | union | selfint_rand2 | 6173 | 3793 | 1.63 | intersections 1066 |
| 1063860 | union | b20set | 9068 | 3754 | 2.42 | intersections 1638 |
| 1041448 | intersection | unknown_rand2 | 9459 | 3732 | 2.53 | intersections 1295 |
| 122796 | intersection | open_rand2 | 8273 | 3729 | 2.22 | classification 1130 |
| 122796 | difference | open_rand2 | 6798 | 3713 | 1.83 | classification 1063 |
| 124526 | union | open_rand2 | 5490 | 3661 | 1.50 | classification 985 |
| 221686 | difference | b20set | 6283 | 3617 | 1.74 | intersections 863 |
| 119830 | union | b20set | 6377 | 3571 | 1.79 | rounding 2215 |
| 1230687 | union | unknown_rand2 | 6562 | 3544 | 1.85 | intersections 1546 |
| 519507 | difference | b20set | 6918 | 3393 | 2.04 | intersections 1054 |
| 519507 | intersection | b20set | 7609 | 3352 | 2.27 | intersections 1041 |
| 1041432 | intersection | unknown_rand2 | 7667 | 3340 | 2.30 | intersections 1245 |
| 221686 | intersection | b20set | 6803 | 3334 | 2.04 | intersections 891 |
| 668056 | difference | b20set | 7254 | 3280 | 2.21 | intersections 1064 |
| 668056 | intersection | b20set | 7343 | 3227 | 2.28 | intersections 1082 |
| 1017018 | union | selfint_rand2 | 6101 | 3079 | 1.98 | merge 1366 |
| 668057 | intersection | b20set | 7326 | 3065 | 2.39 | intersections 1108 |
| 668057 | difference | b20set | 7089 | 3061 | 2.32 | intersections 1071 |
| 519510 | union | b20set | 7659 | 3043 | 2.52 | triangulation 712 |
| 667995 | difference | b20set | 6261 | 2938 | 2.13 | intersections 986 |
| 1063866 | union | b20set | 6499 | 2932 | 2.22 | intersections 1432 |
| 667995 | intersection | b20set | 6334 | 2848 | 2.22 | intersections 917 |
| 1230687 | intersection | unknown_rand2 | 6479 | 2841 | 2.28 | intersections 1098 |
| 1231078 | difference | unknown_rand2 | 5154 | 2800 | 1.84 | intersections 839 |
| 1230687 | difference | unknown_rand2 | 5552 | 2789 | 1.99 | intersections 1327 |
| 110044 | union | b20set | 6217 | 2637 | 2.36 | intersections 1321 |
| 1065032 | union | selfint_rand2 | 3379 | 2626 | 1.29 | rounding 1042 |
| 1017015 | intersection | open_rand2 | 5775 | 2525 | 2.29 | merge 928 |
| 110044 | intersection | b20set | 6004 | 2453 | 2.45 | intersections 1095 |
| 1063860 | difference | b20set | 6149 | 2406 | 2.56 | intersections 1344 |
| 1017015 | difference | open_rand2 | 5156 | 2334 | 2.21 | merge 908 |
| 1063860 | intersection | b20set | 6597 | 2328 | 2.83 | intersections 1368 |
| 1017018 | intersection | selfint_rand2 | 4699 | 2311 | 2.03 | merge 1173 |
| 1063866 | intersection | b20set | 7297 | 2303 | 3.17 | intersections 1198 |
| 112865 | union | selfint_rand2 | 3198 | 2214 | 1.44 | rounding 1048 |
| 1063866 | difference | b20set | 5629 | 2213 | 2.54 | intersections 1118 |
| 1017018 | difference | selfint_rand2 | 4227 | 2180 | 1.94 | merge 1064 |
| 1036403 | intersection | selfint_rand2 | 5015 | 2158 | 2.32 | intersections 657 |
| 1231078 | intersection | unknown_rand2 | 4496 | 2106 | 2.13 | intersections 982 |
| 1036403 | difference | selfint_rand2 | 5078 | 2025 | 2.51 | intersections 609 |
| 519510 | difference | b20set | 4543 | 1900 | 2.39 | intersections 596 |
| 519510 | intersection | b20set | 5990 | 1857 | 3.23 | intersections 577 |
| 110044 | difference | b20set | 5781 | 1766 | 3.27 | intersections 1001 |
| 124526 | intersection | open_rand2 | 3911 | 1737 | 2.25 | classification 650 |
| 124526 | difference | open_rand2 | 3274 | 1722 | 1.90 | classification 548 |
| 1053391 | union | selfint_rand2 | 2572 | 1712 | 1.50 | rounding 511 |
| 118923 | union | open_rand2 | 2558 | 1599 | 1.60 | classification 391 |
| 123787 | union | selfint_rand2 | 3755 | 1598 | 2.35 | rounding 712 |
| 125102 | union | selfint_rand2 | 2301 | 1578 | 1.46 | rounding 669 |
| 119830 | difference | b20set | 2852 | 1577 | 1.81 | rounding 777 |
| 119829 | difference | b20set | 2719 | 1563 | 1.74 | rounding 862 |
| 112826 | difference | selfint_rand2 | 3851 | 1520 | 2.53 | merge 428 |
| 112826 | intersection | selfint_rand2 | 3423 | 1478 | 2.32 | merge 461 |
| 178340 | union | b20set | 2275 | 1449 | 1.57 | intersections 711 |
| 118923 | intersection | open_rand2 | 2112 | 1163 | 1.82 | intersections 255 |
| 109088 | union | open_rand2 | 1601 | 1137 | 1.41 | rounding 323 |
| 1065032 | intersection | selfint_rand2 | 2282 | 1075 | 2.12 | intersections 375 |
| 1481537 | union | b20set | 2146 | 1045 | 2.05 | intersections 256 |
| 178340 | intersection | b20set | 2367 | 982 | 2.41 | intersections 378 |
| 1065032 | difference | selfint_rand2 | 2446 | 952 | 2.57 | intersections 339 |
| 119829 | intersection | b20set | 1693 | 914 | 1.85 | intersections 452 |
| 112865 | intersection | selfint_rand2 | 1968 | 890 | 2.21 | intersections 282 |
| 119830 | intersection | b20set | 1715 | 890 | 1.93 | intersections 505 |
| 123787 | intersection | selfint_rand2 | 2425 | 881 | 2.75 | intersections 530 |
| 80761 | union | b20set | 1444 | 859 | 1.68 | intersections 397 |
| 1053391 | intersection | selfint_rand2 | 1883 | 856 | 2.20 | intersections 263 |
| 1053391 | difference | selfint_rand2 | 1693 | 843 | 2.01 | intersections 230 |
| 118923 | difference | open_rand2 | 1739 | 840 | 2.07 | intersections 253 |
| 123787 | difference | selfint_rand2 | 2047 | 830 | 2.47 | intersections 574 |
| 178340 | difference | b20set | 2276 | 792 | 2.87 | intersections 393 |
| 1481537 | intersection | b20set | 1444 | 782 | 1.85 | intersections 237 |
| 118289 | union | open_rand2 | 937 | 766 | 1.22 | classification 330 |
| 1481537 | difference | b20set | 1654 | 758 | 2.18 | intersections 206 |
| 125102 | intersection | selfint_rand2 | 1300 | 745 | 1.74 | intersections 326 |
| 80761 | intersection | b20set | 1214 | 732 | 1.66 | intersections 316 |
| 112865 | difference | selfint_rand2 | 1544 | 692 | 2.23 | triangulation 174 |
| 109088 | intersection | open_rand2 | 1010 | 659 | 1.53 | classification 194 |
| 125102 | difference | selfint_rand2 | 1660 | 599 | 2.77 | intersections 264 |
| 80761 | difference | b20set | 1238 | 556 | 2.23 | intersections 312 |
| 109088 | difference | open_rand2 | 1309 | 555 | 2.36 | classification 142 |
| 1064655 | difference | selfint_rand2 | 828 | 511 | 1.62 | intersections 160 |
| 78251 | union | b20set | 777 | 489 | 1.59 | rounding 160 |
| 1005281 | union | selfint_rand2 | 721 | 464 | 1.55 | rounding 156 |
| 1064655 | union | selfint_rand2 | 981 | 458 | 2.14 | intersections 117 |
| 1064655 | intersection | selfint_rand2 | 715 | 437 | 1.64 | rounding 121 |
| 278154 | union | b20set | 563 | 405 | 1.39 | rounding 117 |
| 118289 | intersection | open_rand2 | 697 | 382 | 1.82 | classification 148 |
| 118289 | difference | open_rand2 | 702 | 372 | 1.89 | intersections 72 |
| 1005281 | intersection | selfint_rand2 | 506 | 367 | 1.38 | rounding 104 |
| 278154 | difference | b20set | 380 | 349 | 1.09 | intersections 108 |
| 78251 | intersection | b20set | 439 | 325 | 1.35 | intersections 162 |
| 49544 | intersection | b20set | 437 | 296 | 1.48 | simplify 133 |
| 110031 | union | selfint_rand2 | 243 | 288 | 0.84 | rounding 218 |
| 105859 | union | b20set | 353 | 283 | 1.25 | intersections 91 |
| 78251 | difference | b20set | 570 | 283 | 2.01 | intersections 92 |
| 1005281 | difference | selfint_rand2 | 495 | 276 | 1.79 | intersections 105 |
| 49544 | union | b20set | 372 | 234 | 1.59 | intersections 69 |
| 59757 | union | b20set | 262 | 218 | 1.20 | rounding 52 |
| 49544 | difference | b20set | 319 | 210 | 1.52 | intersections 71 |
| 100032 | union | selfint_rand2 | 231 | 200 | 1.16 | rounding 65 |
| 228070 | union | b20set | 234 | 200 | 1.17 | rounding 62 |
| 1047981 | union | selfint_rand2 | 178 | 196 | 0.91 | rounding 77 |
| 278154 | intersection | b20set | 339 | 194 | 1.75 | intersections 77 |
| 105859 | difference | b20set | 279 | 188 | 1.48 | intersections 73 |
| 39025 | union | b20set | 212 | 183 | 1.16 | rounding 47 |
| 1461200 | union | b20set | 197 | 165 | 1.19 | intersections 48 |
| 264708 | difference | b20set | 178 | 165 | 1.08 | intersections 79 |
| 116062 | union | open_rand2 | 263 | 163 | 1.61 | classification 76 |
| 264708 | union | b20set | 229 | 162 | 1.41 | intersections 71 |
| 42372 | union | b20set | 247 | 161 | 1.53 | intersections 86 |
| 1146181 | union | b20set | 251 | 155 | 1.62 | rounding 47 |
| 116870 | union | b20set | 170 | 153 | 1.11 | rounding 53 |
| 59757 | difference | b20set | 221 | 145 | 1.52 | intersections 50 |
| 117020 | union | selfint_rand2 | 183 | 143 | 1.28 | rounding 71 |
| 228070 | difference | b20set | 162 | 142 | 1.14 | intersections 36 |
| 105859 | intersection | b20set | 255 | 139 | 1.83 | intersections 56 |
| 42372 | difference | b20set | 199 | 134 | 1.49 | intersections 72 |
| 264708 | intersection | b20set | 186 | 126 | 1.48 | intersections 50 |
| 39025 | difference | b20set | 153 | 125 | 1.22 | intersections 46 |
| 57332 | union | b20set | 173 | 125 | 1.38 | rounding 38 |
| 116870 | difference | b20set | 123 | 114 | 1.08 | rounding 42 |
| 37964 | union | b20set | 128 | 114 | 1.12 | rounding 29 |
| 59757 | intersection | b20set | 216 | 111 | 1.95 | intersections 35 |
| 89895 | union | b20set | 174 | 107 | 1.63 | intersections 33 |
| 100032 | difference | selfint_rand2 | 159 | 105 | 1.51 | intersections 29 |
| 1047981 | difference | selfint_rand2 | 119 | 104 | 1.14 | rounding 36 |
| 42372 | intersection | b20set | 184 | 104 | 1.77 | intersections 58 |
| 1461200 | difference | b20set | 165 | 102 | 1.62 | rounding 30 |
| 447334 | union | b20set | 194 | 102 | 1.90 | rounding 31 |
| 42545 | union | b20set | 144 | 101 | 1.43 | rounding 31 |
| 100032 | intersection | selfint_rand2 | 196 | 96 | 2.04 | intersections 33 |
| 116870 | intersection | b20set | 125 | 95 | 1.32 | rounding 28 |
| 1146181 | intersection | b20set | 156 | 90 | 1.73 | intersections 29 |
| 91023 | union | b20set | 144 | 88 | 1.64 | rounding 29 |
| 228070 | intersection | b20set | 151 | 87 | 1.74 | intersections 38 |
| 57332 | difference | b20set | 143 | 87 | 1.64 | intersections 22 |
| 1146181 | difference | b20set | 144 | 85 | 1.69 | intersections 29 |
| 57332 | intersection | b20set | 127 | 85 | 1.49 | intersections 24 |
| 39025 | intersection | b20set | 144 | 83 | 1.73 | intersections 29 |
| 89895 | difference | b20set | 144 | 79 | 1.82 | intersections 21 |
| 116062 | difference | open_rand2 | 107 | 77 | 1.39 | classification 30 |
| 89895 | intersection | b20set | 145 | 77 | 1.88 | intersections 20 |
| 116062 | intersection | open_rand2 | 109 | 76 | 1.43 | classification 40 |
| 37964 | intersection | b20set | 109 | 74 | 1.47 | intersections 19 |
| 118287 | union | b20set | 94 | 73 | 1.29 | rounding 26 |
| 91023 | intersection | b20set | 122 | 72 | 1.69 | rounding 22 |
| 1461200 | intersection | b20set | 151 | 70 | 2.16 | intersections 32 |
| 91023 | difference | b20set | 170 | 70 | 2.43 | rounding 22 |
| 42545 | intersection | b20set | 89 | 69 | 1.29 | intersections 31 |
| 37964 | difference | b20set | 110 | 68 | 1.62 | rounding 16 |
| 447334 | intersection | b20set | 115 | 68 | 1.69 | intersections 22 |
| 42545 | difference | b20set | 99 | 67 | 1.48 | intersections 19 |
| 447334 | difference | b20set | 122 | 67 | 1.82 | intersections 17 |
| 1621241 | union | b20set | 109 | 66 | 1.65 | rounding 17 |
| 1047981 | intersection | selfint_rand2 | 82 | 61 | 1.34 | intersections 33 |
| 117020 | intersection | selfint_rand2 | 77 | 57 | 1.35 | merge 25 |
| 110031 | intersection | selfint_rand2 | 45 | 54 | 0.83 | intersections 18 |
| 117020 | difference | selfint_rand2 | 59 | 54 | 1.09 | merge 20 |
| 107389 | union | b20set | 81 | 52 | 1.56 | rounding 20 |
| 1621241 | intersection | b20set | 79 | 50 | 1.58 | intersections 16 |
| 110031 | difference | selfint_rand2 | 50 | 49 | 1.02 | intersections 15 |
| 98668 | union | b20set | 57 | 48 | 1.19 | rounding 13 |
| 102815 | union | open_rand2 | 35 | 43 | 0.81 | classification 24 |
| 118287 | difference | b20set | 60 | 41 | 1.46 | intersections 13 |
| 1621241 | difference | b20set | 83 | 41 | 2.02 | intersections 11 |
| 101249 | union | selfint_rand2 | 52 | 39 | 1.33 | rounding 10 |
| 225952 | difference | b20set | 17 | 36 | 0.47 | setup 18 |
| 79194 | union | b20set | 43 | 36 | 1.19 | rounding 13 |
| 95433 | union | b20set | 62 | 35 | 1.77 | triangulation 12 |
| 118287 | intersection | b20set | 49 | 34 | 1.44 | intersections 9 |
| 95433 | intersection | b20set | 57 | 31 | 1.84 | triangulation 9 |
| 107389 | intersection | b20set | 52 | 30 | 1.73 | intersections 7 |
| 107389 | difference | b20set | 54 | 29 | 1.86 | intersections 9 |
| 95433 | difference | b20set | 47 | 29 | 1.62 | triangulation 10 |
| 79194 | difference | b20set | 24 | 25 | 0.96 | intersections 6 |
| 98668 | difference | b20set | 37 | 22 | 1.68 | intersections 6 |
| 98668 | intersection | b20set | 43 | 21 | 2.05 | intersections 6 |
| 101249 | difference | selfint_rand2 | 30 | 17 | 1.76 | intersections 5 |
| 1582414 | union | b20set | 20 | 17 | 1.18 | intersections 6 |
| 225952 | intersection | b20set | 9 | 16 | 0.56 | triangulation 9 |
| 79194 | intersection | b20set | 23 | 16 | 1.44 | intersections 4 |
| 101249 | intersection | selfint_rand2 | 25 | 15 | 1.67 | intersections 6 |
| 225952 | union | b20set | 19 | 15 | 1.27 | intersections 7 |
| 102815 | intersection | open_rand2 | 7 | 9 | 0.78 | classification 3 |
| 1582414 | intersection | b20set | 11 | 8 | 1.38 | triangulation 2 |
| 1582414 | difference | b20set | 12 | 7 | 1.71 | triangulation 2 |
| 102815 | difference | open_rand2 | 5 | 6 | 0.83 | classification 2 |
