# Exact Boolean 2.0 final run

Builds: master 79a2c1ab15d3ce9e419b2a994af343e32877881c; base385 cd7fb8abba0af09d5725b60c700dfec95234e7ad

## Phase `target`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| target | 5413 | 16239 | 15355 | 412 | 472 | 0 | 0.083 | 0.778 | 15.622 | 0 | 0 |

target: unclean:degenerate triangles 432, unclean:self-intersections 40

#385 baseline (cd7fb8ab, another machine): 16239 ops, 81 unclean; timing over all classes p95 4.502 s, max 281.956 s. This phase: 16239 ops, 472 unclean, p95 0.778 s, max 15.622 s.

## Phase `control`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| target | 5413 | 16239 | 15746 | 412 | 81 | 0 | 0.100 | 0.969 | 35.653 | 11 | 0 |

target: unclean:self-intersections 50, unclean:degenerate triangles 31

target, over max(5 s, 20 us x pieces), not a documented exception (11):

| id | op | s | pieces | target s | rounding s | verdict |
|---|---|---:|---:|---:|---:|---|
| 519511 | union | 35.65 | 1147764 | 22.96 | 13.17 | clean |
| 119829 | union | 26.30 | 260444 | 5.21 | 25.10 | unclean:degenerate triangles |
| 119830 | union | 26.18 | 270220 | 5.40 | 24.92 | unclean:degenerate triangles |
| 519511 | difference | 25.08 | 1147764 | 22.96 | 3.56 | clean |
| 519511 | intersection | 23.79 | 1147764 | 22.96 | 1.52 | clean |
| 1517923 | union | 10.09 | 280656 | 5.61 | 2.99 | clean |
| 1517923 | difference | 8.09 | 280656 | 5.61 | 2.08 | clean |
| 1517923 | intersection | 7.33 | 280656 | 5.61 | 0.98 | clean |
| 110044 | union | 5.88 | 88556 | 5.00 | 1.13 | clean |
| 119829 | difference | 5.83 | 260444 | 5.21 | 5.14 | unclean:degenerate triangles |
| 119830 | difference | 5.81 | 270220 | 5.40 | 5.13 | unclean:degenerate triangles |

#385 baseline (cd7fb8ab, another machine): 16239 ops, 81 unclean; timing over all classes p95 4.502 s, max 281.956 s. This phase: 16239 ops, 81 unclean, p95 0.969 s, max 35.653 s.

## Phase `rest`

| class | models | ops | clean | empty | unclean | failures | median s | p95 s | max s | over target | of which documented exceptions |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| selfint | 2920 | 8760 | 6260 | 178 | 2322 | 0 | 0.328 | 2.838 | 84.214 | 59 | 11 |
| unknown | 187 | 561 | 356 | 9 | 193 | 3 | 4.801 | 24.769 | 633.316 | 4 | 0 |
| open | 1474 | 4422 | 748 | 142 | 3532 | 0 | 0.665 | 15.159 | 581.758 | 117 | 3 |
| unavailable | 6 | 18 | 0 | 0 | 0 | 18 | nan | nan | nan | 0 | 0 |

selfint: unclean:self-intersections 1391, unclean:degenerate triangles 843, unclean:not closed 62, unclean:inside-out components 26

selfint, over max(5 s, 20 us x pieces), not a documented exception (48):

| id | op | s | pieces | target s | rounding s | verdict |
|---|---|---:|---:|---:|---:|---|
| 252785 | difference | 84.21 | 4068208 | 81.36 | 0.19 | unclean:self-intersections |
| 252785 | union | 82.49 | 4068208 | 81.36 | 1.08 | unclean:degenerate triangles |
| 252785 | intersection | 82.44 | 4068208 | 81.36 | 0.00 | empty |
| 247517 | union | 61.20 | 1628208 | 32.56 | 6.34 | unclean:degenerate triangles |
| 247517 | difference | 60.65 | 1628208 | 32.56 | 4.49 | unclean:degenerate triangles |
| 247517 | intersection | 57.98 | 1628208 | 32.56 | 0.02 | clean |
| 247516 | union | 51.85 | 1664882 | 33.30 | 8.57 | unclean:degenerate triangles |
| 247516 | difference | 48.09 | 1664882 | 33.30 | 5.31 | unclean:degenerate triangles |
| 247516 | intersection | 41.32 | 1664882 | 33.30 | 0.02 | clean |
| 996802 | union | 20.00 | 703348 | 14.07 | 6.13 | unclean:degenerate triangles |
| 91312 | union | 19.10 | 302852 | 6.06 | 3.32 | unclean:degenerate triangles |
| 91117 | union | 17.95 | 623682 | 12.47 | 5.10 | unclean:not closed |
| 91312 | difference | 17.35 | 302852 | 6.06 | 2.29 | unclean:degenerate triangles |
| 91312 | intersection | 15.26 | 302852 | 6.06 | 0.48 | clean |
| 996802 | difference | 14.83 | 703348 | 14.07 | 2.84 | unclean:degenerate triangles |
| 91117 | difference | 13.68 | 623682 | 12.47 | 3.09 | unclean:not closed |
| 91117 | intersection | 12.52 | 623682 | 12.47 | 2.00 | unclean:not closed |
| 270401 | union | 10.94 | 333028 | 6.66 | 3.59 | unclean:degenerate triangles |
| 102375 | union | 10.51 | 294660 | 5.89 | 5.81 | unclean:degenerate triangles |
| 84131 | union | 9.72 | 199802 | 5.00 | 5.45 | unclean:degenerate triangles |
| 278141 | union | 9.21 | 377466 | 7.55 | 3.34 | unclean:not closed |
| 1514500 | union | 8.61 | 198554 | 5.00 | 2.34 | unclean:degenerate triangles |
| 1356639 | union | 7.66 | 243836 | 5.00 | 2.67 | unclean:degenerate triangles |
| 84130 | union | 7.39 | 136016 | 5.00 | 4.26 | unclean:degenerate triangles |
| 75493 | union | 7.34 | 359680 | 7.19 | 4.10 | unclean:degenerate triangles |
| 270401 | difference | 6.95 | 333028 | 6.66 | 0.24 | unclean:self-intersections |
| 102375 | difference | 6.88 | 294660 | 5.89 | 3.84 | unclean:degenerate triangles |
| 270401 | intersection | 6.67 | 333028 | 6.66 | 0.02 | clean |
| 996814 | union | 6.65 | 249490 | 5.00 | 2.85 | unclean:degenerate triangles |
| 102375 | intersection | 6.17 | 294660 | 5.89 | 3.10 | unclean:degenerate triangles |
| 112929 | union | 6.14 | 148404 | 5.00 | 1.86 | unclean:degenerate triangles |
| 1591710 | union | 6.09 | 129684 | 5.00 | 3.17 | unclean:degenerate triangles |
| 423069 | union | 6.08 | 140466 | 5.00 | 3.36 | unclean:degenerate triangles |
| 1514500 | difference | 6.07 | 198554 | 5.00 | 1.57 | unclean:degenerate triangles |
| 92482 | union | 6.04 | 198790 | 5.00 | 2.26 | unclean:degenerate triangles |
| 996800 | union | 5.72 | 157124 | 5.00 | 2.48 | unclean:degenerate triangles |
| 225354 | union | 5.65 | 18714 | 5.00 | 3.93 | unclean:degenerate triangles |
| 40632 | union | 5.59 | 28938 | 5.00 | 3.03 | unclean:degenerate triangles |
| 39123 | union | 5.32 | 238762 | 5.00 | 2.79 | unclean:degenerate triangles |
| 42330 | union | 5.29 | 121352 | 5.00 | 2.21 | unclean:degenerate triangles |

unknown: unclean:self-intersections 103, unclean:degenerate triangles 76, unclean:not closed 13, failure:exception 3, unclean:inside-out components 1

unknown, over max(5 s, 20 us x pieces), not a documented exception (4):

| id | op | s | pieces | target s | rounding s | verdict |
|---|---|---:|---:|---:|---:|---|
| 252786 | union | 633.32 | 22440442 | 448.81 | 20.73 | unclean:degenerate triangles |
| 1716279 | union | 190.96 | 3604716 | 72.09 | 48.52 | unclean:degenerate triangles |
| 1716279 | difference | 136.85 | 3604716 | 72.09 | 10.09 | unclean:degenerate triangles |
| 1716279 | intersection | 132.03 | 3604716 | 72.09 | 2.11 | unclean:degenerate triangles |

open: unclean:not closed 3010, unclean:self-intersections 388, unclean:degenerate triangles 118, unclean:inside-out components 16

open, over max(5 s, 20 us x pieces), not a documented exception (114):

| id | op | s | pieces | target s | rounding s | verdict |
|---|---|---:|---:|---:|---:|---|
| 1368052 | union | 581.76 | 11558507 | 231.17 | 79.64 | unclean:not closed |
| 55928 | union | 544.40 | 15120912 | 302.42 | 36.24 | unclean:not closed |
| 55928 | intersection | 529.45 | 15120912 | 302.42 | 16.33 | unclean:not closed |
| 1368052 | difference | 515.85 | 11558507 | 231.17 | 20.34 | unclean:not closed |
| 55928 | difference | 513.73 | 15120912 | 302.42 | 0.14 | unclean:self-intersections |
| 1368052 | intersection | 496.93 | 11558507 | 231.17 | 16.61 | unclean:not closed |
| 1527410 | union | 101.16 | 4718212 | 94.36 | 31.17 | unclean:not closed |
| 242554 | union | 76.32 | 3602290 | 72.05 | 25.37 | unclean:not closed |
| 45811 | union | 74.70 | 2610014 | 52.20 | 10.41 | unclean:degenerate triangles |
| 804302 | union | 69.92 | 3297969 | 65.96 | 37.15 | unclean:not closed |
| 789801 | union | 64.62 | 2821192 | 56.42 | 18.20 | unclean:degenerate triangles |
| 45811 | difference | 61.08 | 2610014 | 52.20 | 1.13 | unclean:self-intersections |
| 1527416 | union | 59.74 | 2906353 | 58.13 | 15.61 | unclean:not closed |
| 45811 | intersection | 59.41 | 2610014 | 52.20 | 0.11 | clean |
| 90151 | union | 54.81 | 2207988 | 44.16 | 10.13 | unclean:not closed |
| 90151 | difference | 48.42 | 2207988 | 44.16 | 5.72 | unclean:not closed |
| 90151 | intersection | 46.73 | 2207988 | 44.16 | 3.83 | unclean:not closed |
| 46024 | union | 41.74 | 1803041 | 36.06 | 9.49 | unclean:not closed |
| 46017 | union | 41.34 | 1798529 | 35.97 | 9.31 | unclean:not closed |
| 1368057 | union | 28.11 | 1014150 | 20.28 | 10.27 | unclean:degenerate triangles |
| 42329 | union | 25.08 | 1028714 | 20.57 | 4.97 | unclean:not closed |
| 42326 | union | 24.80 | 1028902 | 20.58 | 5.04 | unclean:not closed |
| 42325 | union | 24.47 | 1027886 | 20.56 | 4.90 | unclean:not closed |
| 42324 | union | 24.46 | 1028034 | 20.56 | 4.91 | unclean:not closed |
| 42327 | union | 24.42 | 1027754 | 20.56 | 4.58 | unclean:not closed |
| 42211 | union | 24.39 | 1006156 | 20.12 | 5.14 | unclean:not closed |
| 42328 | union | 24.18 | 1029274 | 20.59 | 4.90 | unclean:not closed |
| 42323 | union | 24.11 | 1029774 | 20.60 | 4.65 | unclean:not closed |
| 42331 | union | 24.06 | 1028582 | 20.57 | 4.64 | unclean:not closed |
| 42325 | difference | 21.55 | 1027886 | 20.56 | 2.30 | unclean:not closed |
| 622327 | union | 21.44 | 847702 | 16.95 | 4.51 | unclean:degenerate triangles |
| 42329 | difference | 21.39 | 1028714 | 20.57 | 1.99 | unclean:not closed |
| 42329 | intersection | 21.26 | 1028714 | 20.57 | 2.21 | unclean:not closed |
| 42324 | intersection | 21.24 | 1028034 | 20.56 | 2.29 | unclean:not closed |
| 42326 | difference | 21.09 | 1028902 | 20.58 | 2.15 | unclean:not closed |
| 42324 | difference | 20.94 | 1028034 | 20.56 | 2.15 | unclean:not closed |
| 42331 | intersection | 20.89 | 1028582 | 20.57 | 2.39 | unclean:not closed |
| 42326 | intersection | 20.77 | 1028902 | 20.58 | 2.28 | unclean:not closed |
| 42325 | intersection | 20.75 | 1027886 | 20.56 | 2.35 | unclean:not closed |
| 42327 | difference | 20.67 | 1027754 | 20.56 | 2.15 | unclean:not closed |

unavailable: failure:export failed 18

## Final against the #385 baseline build on the same machine (target class)

16239 operations timed in both: total 4324 s -> 3360 s (-22.3 %); per operation ratio median 0.85, p95 1.19.

| | baseline cd7fb8ab | final |
|---|---:|---:|
| median s | 0.100 | 0.083 |
| p95 s | 0.969 | 0.778 |
| max s | 35.653 | 15.622 |
| over max(5 s, 20 us x pieces) | 11.000 | 0.000 |
| unclean | 81.000 | 472.000 |

unclean in the baseline, not in the final: 15; unclean in the final, not in the baseline: 406  (100344 intersection, 100606 intersection, 100831 difference, 100831 intersection, 101954 intersection, 101955 intersection, 1021166 difference, 102249 difference, 102250 difference, 102250 intersection, 102250 union, 102296 difference, 102296 intersection, 103194 intersection, 1036402 difference, 1036402 intersection, 1036402 union, 1043471 intersection, 104563 difference, 104563 intersection, 1053875 intersection, 105799 intersection, 105862 intersection, 106100 intersection, 106557 intersection, 1080516 intersection, 108147 intersection, 110213 intersection, 110699 intersection, 111240 intersection)

