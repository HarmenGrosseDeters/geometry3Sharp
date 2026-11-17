| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 1000 | 2000 | 2000 | 0 / 0 / 0 / 0 | 0 | – | – | 1544 | – | 2 | 121 | 1200 |
| thingi-pairs | float_cut | 1000 | 2000 | 862 | 0 / 141 / 0 / 0 | 220 | degenerate triangles 715, self-intersections 52 | 10 | 1403 | 0 | – | 81 | 4622 |
| thingi-pairs | exact_union | 1000 | 1000 | 1000 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 173 | 1592 |
| thingi-pairs | float_union | 1000 | 1000 | 249 | 0 / 130 / 0 / 0 | 510 | bowties 51, degenerate triangles 54, self-intersections 6 | – | – | – | – | 806 | 30254 |
| face-contact | exact_imprint | 78 | 156 | 156 | 0 / 0 / 0 / 0 | 0 | – | – | 92 | – | 0 | 81 | 3029 |
| face-contact | float_cut | 78 | 156 | 140 | 0 / 1 / 0 / 0 | 5 | degenerate triangles 6, self-intersections 4 | 0 | 91 | 1 | – | 45 | 1765 |
| face-contact | exact_union | 78 | 78 | 70 | 0 / 0 / 0 / 0 | 0 | self-intersections 8 | – | – | – | – | 150 | 4251 |
| face-contact | float_union | 78 | 78 | 29 | 0 / 1 / 0 / 0 | 44 | bowties 1, self-intersections 3 | – | – | – | – | 342 | 5510 |
| partial-overlap | exact_imprint | 73 | 146 | 146 | 0 / 0 / 0 / 0 | 0 | – | – | 128 | – | 0 | 87 | 313 |
| partial-overlap | float_cut | 73 | 146 | 47 | 0 / 14 / 0 / 0 | 27 | degenerate triangles 55, self-intersections 3 | 0 | 114 | 0 | – | 54 | 5311 |
| partial-overlap | exact_union | 73 | 73 | 73 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 111 | 328 |
| partial-overlap | float_union | 73 | 73 | 10 | 0 / 12 / 0 / 0 | 47 | degenerate triangles 4 | – | – | – | – | 601 | 18320 |
| ulp-shift | exact_imprint | 100 | 200 | 46 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 154 | – | 200 | – | 154 | 2135 | 5273 |
| ulp-shift | float_cut | 100 | 200 | 200 | 0 / 0 / 0 / 0 | 0 | – | 0 | 200 | 200 | – | 161 | 770 |
| ulp-shift | exact_union | 100 | 100 | 99 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | – | – | – | 995 | 4512 |
| ulp-shift | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 840 | 14657 |
| ulp-shift-few | exact_imprint | 100 | 200 | 27 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 173 | – | 200 | – | 173 | 2728 | 13139 |
| ulp-shift-few | float_cut | 100 | 200 | 199 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | 0 | 200 | 198 | – | 268 | 766 |
| ulp-shift-few | exact_union | 100 | 100 | 100 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 1364 | 27379 |
| ulp-shift-few | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1286 | 13800 |
| mirror | exact_imprint | 99 | 198 | 164 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 34 | – | 198 | – | 34 | 345 | 11207 |
| mirror | float_cut | 99 | 198 | 37 | 0 / 40 / 0 / 0 | 72 | degenerate triangles 48, self-intersections 1 | 0 | 158 | 6 | – | 168 | 1544 |
| mirror | exact_union | 99 | 99 | 97 | 0 / 0 / 0 / 0 | 0 | self-intersections 2 | – | – | – | – | 319 | 17321 |
| mirror | float_union | 99 | 99 | 0 | 0 / 24 / 0 / 0 | 74 | degenerate triangles 1 | – | – | – | – | 1279 | 81739 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.86 | 1859 | 4.46 | 870 |
| face-contact | 0.63 | 155 | 2.63 | 77 |
| partial-overlap | 0.93 | 132 | 6.13 | 61 |
| ulp-shift | 0.08 | 200 | 0.78 | 100 |
| ulp-shift-few | 0.09 | 200 | 0.80 | 100 |
| mirror | 0.58 | 158 | 5.25 | 75 |

Disagreements (exact against float, per operation): 3080 (thingi-pairs: only exact good 1889, face-contact: both fail 5, face-contact: curve length 75, face-contact: only exact good 57, partial-overlap: curve length 3, partial-overlap: only exact good 162, ulp-shift: both fail 1, ulp-shift: curve length 4, ulp-shift: float inserted nothing 196, ulp-shift: only exact good 99, ulp-shift-few: float inserted nothing 198, ulp-shift-few: only exact good 100, ulp-shift-few: only float good 1, mirror: both fail 30, mirror: curve length 34, mirror: float inserted nothing 2, mirror: only exact good 224)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| face-contact | face-contact_p0001_815475+1514901 | a_by_b | curve length 0.314 of exact | good | good |
| face-contact | face-contact_p0001_815475+1514901 | b_by_a | curve length 0.301 of exact | good | good |
| face-contact | face-contact_p0001_815475+1514901 | union | only exact good | good | not closed |
| face-contact | face-contact_p0004_1455630+471999 | a_by_b | only exact good | good | unclean: degenerate triangles |
| face-contact | face-contact_p0004_1455630+471999 | b_by_a | curve length 0.856 of exact | good | good |
| face-contact | face-contact_p0004_1455630+471999 | union | only exact good | good | not closed |
| face-contact | face-contact_p0006_196191+490704 | a_by_b | curve length 0.280 of exact | good | good |
| face-contact | face-contact_p0006_196191+490704 | b_by_a | curve length 0.093 of exact | good | good |
| face-contact | face-contact_p0006_196191+490704 | union | only exact good | good | not closed |
| face-contact | face-contact_p0007_200965+568153 | a_by_b | curve length 0.156 of exact | good | good |
| face-contact | face-contact_p0007_200965+568153 | b_by_a | curve length 0.355 of exact | good | good |
| face-contact | face-contact_p0007_200965+568153 | union | only exact good | good | not closed |
| face-contact | face-contact_p0009_75119+702419 | a_by_b | curve length 0.285 of exact | good | good |
| face-contact | face-contact_p0009_75119+702419 | b_by_a | curve length 0.231 of exact | good | good |
| face-contact | face-contact_p0009_75119+702419 | union | only exact good | good | not closed |
| face-contact | face-contact_p0010_343543+501154 | a_by_b | curve length 0.686 of exact | good | good |
| face-contact | face-contact_p0010_343543+501154 | b_by_a | curve length 0.211 of exact | good | good |
| face-contact | face-contact_p0010_343543+501154 | union | only exact good | good | unclean: bowties |
| face-contact | face-contact_p0011_87659+110204 | a_by_b | curve length 0.273 of exact | good | good |
| face-contact | face-contact_p0011_87659+110204 | b_by_a | curve length 0.790 of exact | good | good |
| face-contact | face-contact_p0011_87659+110204 | union | only exact good | good | not closed |
| face-contact | face-contact_p0012_67986+80364 | a_by_b | only exact good | good | unclean: degenerate triangles |
| face-contact | face-contact_p0012_67986+80364 | b_by_a | curve length 0.353 of exact | good | good |
| face-contact | face-contact_p0012_67986+80364 | union | both fail | unclean: self-intersections | not closed |
| face-contact | face-contact_p0015_579371+1315837 | a_by_b | curve length 0.337 of exact | good | good |
| face-contact | face-contact_p0015_579371+1315837 | b_by_a | only exact good | good | opened |
| face-contact | face-contact_p0015_579371+1315837 | union | only exact good | good | not closed |
| face-contact | face-contact_p0016_482916+83649 | a_by_b | curve length 0.286 of exact | good | good |
| face-contact | face-contact_p0016_482916+83649 | b_by_a | curve length 0.483 of exact | good | good |
| face-contact | face-contact_p0016_482916+83649 | union | only exact good | good | not closed |
| face-contact | face-contact_p0018_211915+117525 | a_by_b | curve length 0.327 of exact | good | good |
| face-contact | face-contact_p0018_211915+117525 | b_by_a | only exact good | good | unclean: degenerate triangles |
| face-contact | face-contact_p0018_211915+117525 | union | only exact good | good | not closed |
| face-contact | face-contact_p0021_1706461+636813 | a_by_b | only exact good | good | unclean: self-intersections |
| face-contact | face-contact_p0021_1706461+636813 | b_by_a | curve length 0.694 of exact | good | good |
| face-contact | face-contact_p0021_1706461+636813 | union | both fail | unclean: self-intersections | not closed |
| face-contact | face-contact_p0026_619671+97124 | a_by_b | curve length 0.118 of exact | good | good |
| face-contact | face-contact_p0026_619671+97124 | b_by_a | curve length 0.184 of exact | good | good |
| face-contact | face-contact_p0026_619671+97124 | union | only exact good | good | not closed |
| face-contact | face-contact_p0032_276935+472116 | a_by_b | curve length 0.148 of exact | good | good |
| face-contact | face-contact_p0032_276935+472116 | b_by_a | curve length 0.817 of exact | good | good |
| face-contact | face-contact_p0032_276935+472116 | union | only exact good | good | not closed |
| face-contact | face-contact_p0034_110202+343530 | a_by_b | curve length 0.273 of exact | good | good |
| face-contact | face-contact_p0034_110202+343530 | b_by_a | curve length 0.225 of exact | good | good |
| face-contact | face-contact_p0034_110202+343530 | union | only exact good | good | not closed |
| face-contact | face-contact_p0036_94146+96781 | a_by_b | only exact good | good | unclean: self-intersections |
| face-contact | face-contact_p0036_94146+96781 | b_by_a | curve length 0.573 of exact | good | good |
| face-contact | face-contact_p0036_94146+96781 | union | only exact good | good | not closed |
| face-contact | face-contact_p0039_71265+163758 | b_by_a | curve length 0.649 of exact | good | good |
| face-contact | face-contact_p0039_71265+163758 | union | only exact good | good | not closed |
| face-contact | face-contact_p0041_375255+736124 | a_by_b | curve length 0.181 of exact | good | good |
| face-contact | face-contact_p0041_375255+736124 | b_by_a | curve length 0.555 of exact | good | good |
| face-contact | face-contact_p0041_375255+736124 | union | only exact good | good | not closed |
| face-contact | face-contact_p0043_45620+110950 | a_by_b | only exact good | good | exception |
| face-contact | face-contact_p0043_45620+110950 | b_by_a | only exact good | good | opened |
| face-contact | face-contact_p0043_45620+110950 | union | only exact good | good | exception |
| face-contact | face-contact_p0047_135805+70830 | a_by_b | curve length 0.238 of exact | good | good |
| face-contact | face-contact_p0047_135805+70830 | b_by_a | curve length 0.273 of exact | good | good |
| face-contact | face-contact_p0047_135805+70830 | union | only exact good | good | not closed |
| face-contact | face-contact_p0049_72581+77922 | a_by_b | curve length 0.735 of exact | good | good |

(3020 more in the raw rows)

Cases not written (placement refused as inexact, or load failed): 50
