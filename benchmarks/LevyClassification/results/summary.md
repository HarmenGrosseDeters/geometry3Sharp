## Build `master`

| cohort | ops | Compute s | classification s | share | ops > 5 s | ops still > 5 s if replaceable part = 0 |
|---|---|---|---|---|---|---|
| open | 27 | 433.6 | 99.1 | 22.8 % | 18 | 18 |
| selfint | 30 | 228.0 | 44.8 | 19.6 % | 18 | 16 |
| unknown | 21 | 373.0 | 59.9 | 16.1 % | 16 | 15 |
| all | 78 | 1034.6 | 203.7 | 19.7 % | 52 | 49 |

| model | cohort | op | ms | classification ms | patches | pieces | winding_plane_sides | components A+B | exact winding A/B | status |
|---|---|---|---|---|---|---|---|---|---|---|
| 1087134 | unknown | union | 49084 | 10836 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1087134 | unknown | intersection | 33751 | 10786 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1087134 | unknown | difference | 37565 | 9906 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1005586 | open | intersection | 23317 | 8530 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 1005586 | open | union | 37483 | 7777 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 112798 | open | union | 30449 | 6766 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 1005586 | open | difference | 27515 | 6644 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 112798 | open | intersection | 18765 | 6411 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 112798 | open | difference | 19774 | 6038 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 117959 | open | intersection | 20447 | 5926 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 117959 | open | union | 19110 | 5279 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 107554 | open | union | 27479 | 5218 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 107554 | open | difference | 16502 | 5103 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 117959 | open | difference | 23852 | 5034 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 113906 | open | union | 23525 | 4962 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 107554 | open | intersection | 15874 | 4843 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 113906 | open | difference | 12087 | 4815 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 113906 | open | intersection | 10818 | 4537 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 107543 | selfint | union | 19057 | 4068 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 107543 | selfint | difference | 15661 | 3893 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 107543 | selfint | intersection | 15100 | 3798 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 1087141 | open | union | 54547 | 3468 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 107545 | selfint | union | 18100 | 3362 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 107545 | selfint | intersection | 12522 | 3289 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 107545 | selfint | difference | 14055 | 3275 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 1087141 | open | intersection | 18539 | 3104 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 103354 | unknown | intersection | 16041 | 2891 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 103354 | unknown | difference | 25481 | 2879 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 103354 | unknown | union | 32828 | 2812 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 105693 | selfint | union | 19469 | 2598 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 1087141 | open | difference | 23932 | 2565 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 111170 | unknown | union | 45363 | 2464 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 105693 | selfint | difference | 12670 | 2456 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 105693 | selfint | intersection | 11197 | 2431 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 113685 | selfint | intersection | 8350 | 2270 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 113685 | selfint | difference | 8268 | 2220 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 113685 | selfint | union | 13807 | 2214 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 111170 | unknown | difference | 10707 | 2181 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 111170 | unknown | intersection | 7840 | 2138 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 120628 | unknown | difference | 18379 | 1995 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 120628 | unknown | union | 29163 | 1990 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 120628 | unknown | intersection | 10970 | 1849 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 102375 | selfint | union | 7852 | 1371 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 1059579 | selfint | union | 8648 | 1344 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 1059579 | selfint | intersection | 6321 | 1343 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 1059579 | selfint | difference | 6694 | 1229 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 1074637 | unknown | union | 17569 | 1169 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 1074637 | unknown | intersection | 5629 | 1034 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 102375 | selfint | intersection | 4942 | 986 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 1074637 | unknown | difference | 8212 | 964 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 102375 | selfint | difference | 5586 | 955 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 115423 | unknown_rand | union | 4486 | 862 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 115423 | unknown_rand | intersection | 3999 | 816 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 112535 | open_rand | union | 2911 | 771 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 120477 | unknown_rand | union | 6558 | 744 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 115423 | unknown_rand | difference | 3322 | 685 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 106337 | selfint | union | 8400 | 577 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 120477 | unknown_rand | difference | 3602 | 485 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 112536 | open_rand | union | 2224 | 443 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 120477 | unknown_rand | intersection | 2467 | 411 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 106337 | selfint | intersection | 3503 | 386 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 106337 | selfint | difference | 4236 | 341 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 112536 | open_rand | intersection | 1010 | 215 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 112535 | open_rand | intersection | 818 | 183 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 112536 | open_rand | difference | 1005 | 139 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 112535 | open_rand | difference | 905 | 127 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 110709 | selfint_rand | union | 1364 | 115 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 100030 | open_rand | union | 484 | 74 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 110709 | selfint_rand | difference | 314 | 60 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 110709 | selfint_rand | intersection | 356 | 59 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 1043436 | selfint_rand | union | 588 | 50 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 100030 | open_rand | difference | 113 | 44 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 107544 | selfint_rand | union | 384 | 43 | 57 | 3994 | 1328 | 2 | 1/1 | Success |
| 100030 | open_rand | intersection | 100 | 36 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 1043436 | selfint_rand | intersection | 215 | 18 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 1043436 | selfint_rand | difference | 234 | 13 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 107544 | selfint_rand | intersection | 30 | 4 | 57 | 3994 | 1328 | 2 | 1/1 | Success |
| 107544 | selfint_rand | difference | 39 | 4 | 57 | 3994 | 1328 | 2 | 1/1 | Success |

## Build `levers`

| cohort | ops | Compute s | classification s | share | ops > 5 s | ops still > 5 s if replaceable part = 0 |
|---|---|---|---|---|---|---|
| open | 27 | 347.5 | 102.6 | 29.5 % | 18 | 18 |
| selfint | 30 | 199.2 | 45.7 | 23.0 % | 19 | 17 |
| unknown | 21 | 294.6 | 55.6 | 18.9 % | 16 | 14 |
| all | 78 | 841.3 | 204.0 | 24.3 % | 53 | 49 |

| model | cohort | op | ms | classification ms | patches | pieces | winding_plane_sides | components A+B | exact winding A/B | status |
|---|---|---|---|---|---|---|---|---|---|---|
| 1087134 | unknown | difference | 28284 | 9820 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1087134 | unknown | union | 38462 | 9587 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1087134 | unknown | intersection | 24910 | 9543 | 227906 | 2272904 | 77969382 | 2 | 1/1 | Success |
| 1005586 | open | intersection | 23226 | 9146 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 1005586 | open | union | 36687 | 8184 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 112798 | open | intersection | 18507 | 7565 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 112798 | open | union | 27277 | 7064 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 1005586 | open | difference | 25509 | 6893 | 1650 | 3912290 | 0 | 150 | 0/0 | Success |
| 112798 | open | difference | 19206 | 6081 | 270 | 3203850 | 0 | 92 | 0/0 | Success |
| 117959 | open | intersection | 17232 | 5889 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 113906 | open | union | 18018 | 5756 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 117959 | open | union | 17061 | 5558 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 117959 | open | difference | 18075 | 5269 | 119 | 2585382 | 0 | 6 | 0/0 | Success |
| 107554 | open | union | 20492 | 4983 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 113906 | open | intersection | 10803 | 4799 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 113906 | open | difference | 11361 | 4783 | 34168 | 756209 | 0 | 1090 | 0/0 | Success |
| 107554 | open | difference | 13694 | 4762 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 107554 | open | intersection | 13904 | 4717 | 177704 | 1009014 | 0 | 4 | 0/0 | Success |
| 107543 | selfint | union | 16886 | 4081 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 107543 | selfint | intersection | 14011 | 4028 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 107543 | selfint | difference | 14065 | 3960 | 161025 | 918168 | 20355480 | 2 | 1/1 | Success |
| 107545 | selfint | union | 15726 | 3537 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 107545 | selfint | intersection | 11684 | 3359 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 107545 | selfint | difference | 12132 | 3336 | 140083 | 815488 | 17141074 | 4 | 1/1 | Success |
| 1087141 | open | union | 21775 | 3274 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 103354 | unknown | intersection | 17344 | 3121 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 1087141 | open | intersection | 12205 | 2957 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 103354 | unknown | union | 29480 | 2830 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 103354 | unknown | difference | 22659 | 2647 | 1848 | 5937642 | 185088 | 2 | 1/1 | Success |
| 1087141 | open | difference | 13807 | 2424 | 299 | 1574056 | 0 | 2 | 0/0 | Success |
| 105693 | selfint | union | 11182 | 2387 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 105693 | selfint | difference | 8673 | 2295 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 111170 | unknown | union | 16932 | 2220 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 113685 | selfint | union | 11356 | 2155 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 105693 | selfint | intersection | 8041 | 2118 | 60850 | 395172 | 20865470 | 44 | 1/1 | Success |
| 111170 | unknown | intersection | 7613 | 2108 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 113685 | selfint | intersection | 7925 | 2045 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 111170 | unknown | difference | 9599 | 2005 | 57487 | 1300854 | 4762216 | 430 | 1/1 | Success |
| 113685 | selfint | difference | 7950 | 2000 | 58254 | 476310 | 10605074 | 118 | 1/1 | Success |
| 120628 | unknown | union | 23915 | 1980 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 120628 | unknown | intersection | 10686 | 1897 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 120628 | unknown | difference | 15431 | 1830 | 28 | 5127028 | 1178 | 4 | 1/1 | Success |
| 1059579 | selfint | union | 8849 | 1643 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 102375 | selfint | union | 9343 | 1458 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 1059579 | selfint | difference | 6194 | 1328 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 1059579 | selfint | intersection | 6348 | 1296 | 49524 | 314856 | 5249442 | 2 | 1/1 | Success |
| 102375 | selfint | difference | 6131 | 1056 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 102375 | selfint | intersection | 5065 | 1046 | 28744 | 294660 | 13515084 | 2 | 1/1 | Success |
| 106337 | selfint | union | 7723 | 907 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 115423 | unknown_rand | union | 4570 | 871 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 1074637 | unknown | union | 15852 | 837 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 1074637 | unknown | difference | 7312 | 803 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 112535 | open_rand | union | 2569 | 770 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 115423 | unknown_rand | intersection | 3548 | 747 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 1074637 | unknown | intersection | 5132 | 723 | 2706 | 2103278 | 184478 | 16 | 1/1 | Success |
| 106337 | selfint | difference | 3641 | 712 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 115423 | unknown_rand | difference | 2813 | 677 | 200 | 2191512 | 5540 | 2 | 1/1 | Success |
| 106337 | selfint | intersection | 3136 | 652 | 5590 | 529358 | 763380 | 2 | 1/1 | Success |
| 112536 | open_rand | union | 2094 | 611 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 120477 | unknown_rand | union | 5060 | 508 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 120477 | unknown_rand | difference | 2726 | 450 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 120477 | unknown_rand | intersection | 2222 | 440 | 76 | 1029676 | 2410 | 4 | 1/1 | Success |
| 112535 | open_rand | intersection | 950 | 367 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 112536 | open_rand | intersection | 1005 | 342 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 112535 | open_rand | difference | 669 | 144 | 923 | 102752 | 0 | 12 | 0/0 | Success |
| 112536 | open_rand | difference | 712 | 126 | 64 | 117264 | 0 | 6 | 0/0 | Success |
| 110709 | selfint_rand | union | 1244 | 109 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 100030 | open_rand | union | 462 | 80 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 110709 | selfint_rand | intersection | 379 | 63 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 1043436 | selfint_rand | union | 515 | 49 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 100030 | open_rand | difference | 104 | 45 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 107544 | selfint_rand | union | 365 | 43 | 57 | 3994 | 1328 | 2 | 1/1 | Success |
| 100030 | open_rand | intersection | 85 | 34 | 138 | 5510 | 0 | 562 | 0/0 | Success |
| 110709 | selfint_rand | difference | 217 | 34 | 2225 | 17020 | 40082 | 20 | 1/1 | Success |
| 1043436 | selfint_rand | difference | 225 | 18 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 1043436 | selfint_rand | intersection | 155 | 12 | 56 | 27236 | 3284 | 2 | 1/1 | Success |
| 107544 | selfint_rand | intersection | 25 | 8 | 57 | 3994 | 1328 | 2 | 1/1 | Success |
| 107544 | selfint_rand | difference | 28 | 3 | 57 | 3994 | 1328 | 2 | 1/1 | Success |

