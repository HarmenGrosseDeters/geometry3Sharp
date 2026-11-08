## Build `master`

| cohort | ops | Compute s | classification s | share | ops > 5 s | ops still > 5 s if replaceable part = 0 |
|---|---|---|---|---|---|---|
| open | 36 | 463.6 | 47.0 | 10.1 % | 14 | 13 |
| selfint | 45 | 54.0 | 3.6 | 6.7 % | 0 | 0 |
| unknown | 12 | 54.2 | 5.0 | 9.2 % | 5 | 4 |
| all | 93 | 571.8 | 55.6 | 9.7 % | 19 | 17 |

| model | cohort | op | ms | classification ms | patches | pieces | winding_plane_sides | components A+B | exact winding A/B | status |
|---|---|---|---|---|---|---|---|---|---|---|
| 1088051 | open_rand2 | union | 71308 | 3470 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | union | 64743 | 3417 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | union | 63932 | 3402 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | difference | 23819 | 3392 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | union | 53876 | 3371 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | intersection | 19316 | 3136 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088051 | open_rand2 | intersection | 18401 | 3073 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | intersection | 18115 | 2936 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | intersection | 18589 | 2858 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | difference | 21559 | 2816 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | difference | 23536 | 2652 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088051 | open_rand2 | difference | 23416 | 2579 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 122796 | open_rand2 | union | 6361 | 1703 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 122796 | open_rand2 | intersection | 3799 | 1411 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 122796 | open_rand2 | difference | 3856 | 1298 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 124526 | open_rand2 | union | 3583 | 865 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 124526 | open_rand2 | intersection | 1685 | 665 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 1231078 | unknown_rand2 | union | 5968 | 611 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 1230687 | unknown_rand2 | union | 2996 | 588 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 124526 | open_rand2 | difference | 1537 | 528 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 1041448 | unknown_rand2 | union | 8702 | 526 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 118923 | open_rand2 | union | 2153 | 471 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 1041432 | unknown_rand2 | union | 8611 | 447 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 1230687 | unknown_rand2 | difference | 2013 | 418 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 1230687 | unknown_rand2 | intersection | 2305 | 405 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 1017015 | open_rand2 | union | 5775 | 382 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 1041448 | unknown_rand2 | intersection | 5037 | 376 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 1041448 | unknown_rand2 | difference | 5567 | 358 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 1017015 | open_rand2 | intersection | 2996 | 349 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 1041432 | unknown_rand2 | intersection | 3434 | 344 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 1231078 | unknown_rand2 | difference | 3010 | 315 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 1231078 | unknown_rand2 | intersection | 1902 | 313 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 118923 | open_rand2 | intersection | 1241 | 308 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 1017018 | selfint_rand2 | union | 3031 | 303 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 109088 | open_rand2 | union | 1442 | 295 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 1017015 | open_rand2 | difference | 3477 | 291 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 118289 | open_rand2 | union | 919 | 276 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 1041432 | unknown_rand2 | difference | 4629 | 273 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 1036403 | selfint_rand2 | union | 4299 | 271 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 109088 | open_rand2 | difference | 755 | 263 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 1017018 | selfint_rand2 | intersection | 2245 | 223 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 1036403 | selfint_rand2 | intersection | 2298 | 198 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 1017018 | selfint_rand2 | difference | 2301 | 191 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 118923 | open_rand2 | difference | 1021 | 186 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 109088 | open_rand2 | intersection | 760 | 184 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 112826 | selfint_rand2 | union | 4558 | 174 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 112826 | selfint_rand2 | intersection | 1601 | 168 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 1036403 | selfint_rand2 | difference | 2464 | 156 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 125102 | selfint_rand2 | union | 2046 | 154 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 1065032 | selfint_rand2 | union | 2642 | 148 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 112826 | selfint_rand2 | difference | 1588 | 132 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 118289 | open_rand2 | intersection | 354 | 125 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 112865 | selfint_rand2 | union | 3038 | 123 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 1065032 | selfint_rand2 | difference | 1138 | 107 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 1065032 | selfint_rand2 | intersection | 1255 | 106 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 1053391 | selfint_rand2 | union | 2119 | 105 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 125102 | selfint_rand2 | intersection | 780 | 99 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 125102 | selfint_rand2 | difference | 1095 | 95 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 116062 | open_rand2 | union | 432 | 86 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 1053391 | selfint_rand2 | intersection | 911 | 82 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 1064655 | selfint_rand2 | union | 987 | 77 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 112865 | selfint_rand2 | difference | 1120 | 66 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 100032 | selfint_rand2 | union | 500 | 64 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 112865 | selfint_rand2 | intersection | 1023 | 60 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 118289 | open_rand2 | difference | 397 | 60 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 117020 | selfint_rand2 | union | 522 | 58 | 27 | 2694 | 574 | 2 | 1/1 | Success |
| 1005281 | selfint_rand2 | union | 838 | 57 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 123787 | selfint_rand2 | union | 2870 | 55 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 1053391 | selfint_rand2 | difference | 953 | 49 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 1047981 | selfint_rand2 | union | 411 | 47 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 110031 | selfint_rand2 | union | 524 | 47 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 101249 | selfint_rand2 | union | 339 | 45 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 102815 | open_rand2 | union | 275 | 44 | 2 | 476 | 0 | 2 | 0/0 | Success |
| 116062 | open_rand2 | intersection | 76 | 40 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 116062 | open_rand2 | difference | 84 | 32 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 1005281 | selfint_rand2 | difference | 371 | 24 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 1064655 | selfint_rand2 | intersection | 521 | 21 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 1064655 | selfint_rand2 | difference | 550 | 21 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 123787 | selfint_rand2 | difference | 1101 | 17 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 1005281 | selfint_rand2 | intersection | 256 | 15 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 123787 | selfint_rand2 | intersection | 1086 | 15 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 101249 | selfint_rand2 | difference | 25 | 7 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 100032 | selfint_rand2 | intersection | 115 | 6 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 100032 | selfint_rand2 | difference | 151 | 6 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 117020 | selfint_rand2 | difference | 72 | 6 | 27 | 2694 | 574 | 2 | 1/1 | Success |
| 1047981 | selfint_rand2 | intersection | 38 | 5 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 1047981 | selfint_rand2 | difference | 54 | 5 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 110031 | selfint_rand2 | intersection | 26 | 4 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 110031 | selfint_rand2 | difference | 31 | 4 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 101249 | selfint_rand2 | intersection | 19 | 2 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 117020 | selfint_rand2 | intersection | 66 | 2 | 27 | 2694 | 574 | 2 | 1/1 | Success |
| 102815 | open_rand2 | intersection | 7 | 1 | 2 | 476 | 0 | 2 | 0/0 | Success |
| 102815 | open_rand2 | difference | 4 | 1 | 2 | 476 | 0 | 2 | 0/0 | Success |

## Build `levers`

| cohort | ops | Compute s | classification s | share | ops > 5 s | ops still > 5 s if replaceable part = 0 |
|---|---|---|---|---|---|---|
| open | 36 | 248.0 | 46.5 | 18.8 % | 14 | 12 |
| selfint | 45 | 42.0 | 3.8 | 9.1 % | 0 | 0 |
| unknown | 12 | 43.8 | 5.6 | 12.7 % | 3 | 2 |
| all | 93 | 333.7 | 55.9 | 16.7 % | 17 | 14 |

| model | cohort | op | ms | classification ms | patches | pieces | winding_plane_sides | components A+B | exact winding A/B | status |
|---|---|---|---|---|---|---|---|---|---|---|
| 1088051 | open_rand2 | union | 24750 | 3549 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | union | 23718 | 3487 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | union | 23289 | 3361 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | difference | 16207 | 3292 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | union | 22379 | 3210 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1088138 | open_rand2 | intersection | 13630 | 3125 | 311 | 1581226 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | intersection | 14860 | 3061 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1087144 | open_rand2 | difference | 15217 | 3017 | 302 | 1582986 | 0 | 2 | 0/0 | Success |
| 1088051 | open_rand2 | intersection | 13118 | 2845 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | intersection | 12063 | 2698 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088054 | open_rand2 | difference | 15126 | 2688 | 329 | 1586140 | 0 | 2 | 0/0 | Success |
| 1088051 | open_rand2 | difference | 16156 | 2585 | 340 | 1582368 | 0 | 2 | 0/0 | Success |
| 122796 | open_rand2 | union | 5643 | 1717 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 122796 | open_rand2 | intersection | 3471 | 1389 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 122796 | open_rand2 | difference | 3329 | 1294 | 471 | 638586 | 0 | 160 | 0/0 | Success |
| 124526 | open_rand2 | union | 3677 | 994 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 1230687 | unknown_rand2 | union | 3350 | 738 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 1041432 | unknown_rand2 | union | 6080 | 599 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 1230687 | unknown_rand2 | difference | 2269 | 589 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 1231078 | unknown_rand2 | union | 5071 | 579 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 124526 | open_rand2 | intersection | 1431 | 551 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 1041448 | unknown_rand2 | union | 6019 | 514 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 1230687 | unknown_rand2 | intersection | 2230 | 475 | 76 | 1299752 | 3828 | 30 | 1/1 | Success |
| 124526 | open_rand2 | difference | 1235 | 447 | 126 | 244072 | 0 | 12 | 0/0 | Success |
| 1041432 | unknown_rand2 | intersection | 2742 | 436 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 118923 | open_rand2 | union | 1657 | 417 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 1231078 | unknown_rand2 | difference | 2550 | 411 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 1017015 | open_rand2 | union | 5082 | 377 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 118289 | open_rand2 | union | 1046 | 370 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 1017018 | selfint_rand2 | union | 2802 | 365 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 1231078 | unknown_rand2 | intersection | 1874 | 339 | 100 | 1011468 | 2264 | 52 | 1/1 | Success |
| 1041448 | unknown_rand2 | intersection | 3759 | 324 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 1017015 | open_rand2 | intersection | 2346 | 320 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 1036403 | selfint_rand2 | union | 3609 | 295 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 118923 | open_rand2 | intersection | 1196 | 294 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 109088 | open_rand2 | difference | 699 | 282 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 1041448 | unknown_rand2 | difference | 4070 | 279 | 957 | 758058 | 109304 | 8 | 1/1 | Success |
| 109088 | open_rand2 | union | 1238 | 278 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 1041432 | unknown_rand2 | difference | 3758 | 276 | 918 | 747190 | 104990 | 2 | 1/1 | Success |
| 1017018 | selfint_rand2 | intersection | 1556 | 240 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 1017018 | selfint_rand2 | difference | 1508 | 225 | 8071 | 227746 | 848398 | 220 | 1/1 | Success |
| 1017015 | open_rand2 | difference | 2518 | 204 | 8163 | 232428 | 1370630 | 170 | 1/1 | Success |
| 1036403 | selfint_rand2 | intersection | 2149 | 201 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 109088 | open_rand2 | intersection | 625 | 199 | 1560 | 34337 | 0 | 158 | 0/0 | Success |
| 112826 | selfint_rand2 | union | 4155 | 197 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 1065032 | selfint_rand2 | union | 2374 | 180 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 1036403 | selfint_rand2 | difference | 1736 | 165 | 188 | 430538 | 30824 | 2 | 1/1 | Success |
| 112826 | selfint_rand2 | difference | 1408 | 160 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 112865 | selfint_rand2 | union | 2200 | 159 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 118289 | open_rand2 | intersection | 361 | 134 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 125102 | selfint_rand2 | union | 1391 | 131 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 118923 | open_rand2 | difference | 788 | 130 | 14 | 95100 | 0 | 2 | 0/0 | Success |
| 112826 | selfint_rand2 | intersection | 1052 | 117 | 4494 | 159434 | 270722 | 14 | 1/1 | Success |
| 1065032 | selfint_rand2 | difference | 715 | 102 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 1065032 | selfint_rand2 | intersection | 1062 | 98 | 100 | 223542 | 9578 | 2 | 1/1 | Success |
| 1053391 | selfint_rand2 | union | 1598 | 96 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 112865 | selfint_rand2 | intersection | 701 | 93 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 125102 | selfint_rand2 | intersection | 559 | 85 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 125102 | selfint_rand2 | difference | 782 | 84 | 16 | 170224 | 2172 | 2 | 1/1 | Success |
| 123787 | selfint_rand2 | union | 1583 | 79 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 1005281 | selfint_rand2 | union | 801 | 73 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 116062 | open_rand2 | union | 448 | 72 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 1053391 | selfint_rand2 | intersection | 915 | 71 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 112865 | selfint_rand2 | difference | 674 | 70 | 2810 | 57714 | 174734 | 8 | 1/1 | Success |
| 1053391 | selfint_rand2 | difference | 757 | 66 | 10 | 162282 | 1696 | 2 | 1/1 | Success |
| 1064655 | selfint_rand2 | union | 730 | 62 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 100032 | selfint_rand2 | union | 469 | 60 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 110031 | selfint_rand2 | union | 555 | 51 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 118289 | open_rand2 | difference | 270 | 51 | 12 | 45368 | 0 | 2 | 0/0 | Success |
| 101249 | selfint_rand2 | union | 344 | 46 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 1047981 | selfint_rand2 | union | 357 | 40 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 117020 | selfint_rand2 | union | 388 | 40 | 27 | 2694 | 574 | 2 | 1/1 | Success |
| 102815 | open_rand2 | union | 263 | 39 | 2 | 476 | 0 | 2 | 0/0 | Success |
| 116062 | open_rand2 | intersection | 77 | 32 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 123787 | selfint_rand2 | intersection | 715 | 27 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 1005281 | selfint_rand2 | intersection | 297 | 22 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 116062 | open_rand2 | difference | 58 | 22 | 120 | 5096 | 0 | 84 | 0/0 | Success |
| 123787 | selfint_rand2 | difference | 689 | 21 | 17 | 41672 | 11102 | 2 | 1/1 | Success |
| 1005281 | selfint_rand2 | difference | 273 | 18 | 64 | 29962 | 4906 | 2 | 1/1 | Success |
| 1064655 | selfint_rand2 | intersection | 346 | 17 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 1064655 | selfint_rand2 | difference | 345 | 14 | 53 | 34384 | 4114 | 6 | 1/1 | Success |
| 1047981 | selfint_rand2 | difference | 58 | 7 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 1047981 | selfint_rand2 | intersection | 39 | 5 | 16 | 14096 | 410 | 2 | 1/1 | Success |
| 110031 | selfint_rand2 | intersection | 25 | 5 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 100032 | selfint_rand2 | intersection | 66 | 4 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 100032 | selfint_rand2 | difference | 80 | 4 | 21 | 9216 | 1378 | 10 | 1/1 | Success |
| 110031 | selfint_rand2 | difference | 31 | 4 | 99 | 2886 | 3238 | 2 | 1/1 | Success |
| 117020 | selfint_rand2 | intersection | 36 | 2 | 27 | 2694 | 574 | 2 | 1/1 | Success |
| 101249 | selfint_rand2 | intersection | 13 | 1 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 101249 | selfint_rand2 | difference | 17 | 1 | 12 | 2560 | 170 | 4 | 1/1 | Success |
| 102815 | open_rand2 | intersection | 7 | 1 | 2 | 476 | 0 | 2 | 0/0 | Success |
| 102815 | open_rand2 | difference | 4 | 1 | 2 | 476 | 0 | 2 | 0/0 | Success |
| 117020 | selfint_rand2 | difference | 33 | 1 | 27 | 2694 | 574 | 2 | 1/1 | Success |

