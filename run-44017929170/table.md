| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 1000 | 2000 | 1999 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | 1544 | – | 3 | 122 | 1199 |
| thingi-pairs | float_cut | 1000 | 2000 | 862 | 0 / 141 / 0 / 0 | 220 | degenerate triangles 715, self-intersections 52 | 10 | 1403 | 0 | – | 85 | 4646 |
| thingi-pairs | exact_union | 1000 | 1000 | 1000 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 173 | 2053 |
| thingi-pairs | float_union | 1000 | 1000 | 249 | 0 / 130 / 0 / 0 | 510 | bowties 51, degenerate triangles 54, self-intersections 6 | – | – | – | – | 842 | 21961 |
| face-contact | exact_imprint | 78 | 156 | 156 | 0 / 0 / 0 / 0 | 0 | – | – | 92 | – | 0 | 106 | 4082 |
| face-contact | float_cut | 78 | 156 | 140 | 0 / 1 / 0 / 0 | 5 | degenerate triangles 6, self-intersections 4 | 0 | 91 | 1 | – | 59 | 2277 |
| face-contact | exact_union | 78 | 78 | 70 | 0 / 0 / 0 / 0 | 0 | self-intersections 8 | – | – | – | – | 184 | 5908 |
| face-contact | float_union | 78 | 78 | 29 | 0 / 1 / 0 / 0 | 44 | bowties 1, self-intersections 3 | – | – | – | – | 509 | 7624 |
| partial-overlap | exact_imprint | 73 | 146 | 145 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | 128 | – | 1 | 80 | 317 |
| partial-overlap | float_cut | 73 | 146 | 47 | 0 / 14 / 0 / 0 | 27 | degenerate triangles 55, self-intersections 3 | 0 | 114 | 0 | – | 56 | 5343 |
| partial-overlap | exact_union | 73 | 73 | 73 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 107 | 334 |
| partial-overlap | float_union | 73 | 73 | 10 | 0 / 12 / 0 / 0 | 47 | degenerate triangles 4 | – | – | – | – | 605 | 18543 |
| ulp-shift | exact_imprint | 100 | 200 | 18 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 182 | – | 200 | – | 182 | 2692 | 6373 |
| ulp-shift | float_cut | 100 | 200 | 200 | 0 / 0 / 0 / 0 | 0 | – | 0 | 200 | 200 | – | 244 | 1076 |
| ulp-shift | exact_union | 100 | 100 | 99 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | – | – | – | 1206 | 6551 |
| ulp-shift | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1124 | 21945 |
| ulp-shift-few | exact_imprint | 100 | 200 | 8 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 192 | – | 200 | – | 192 | 2173 | 10441 |
| ulp-shift-few | float_cut | 100 | 200 | 199 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | 0 | 200 | 198 | – | 193 | 629 |
| ulp-shift-few | exact_union | 100 | 100 | 100 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 956 | 21312 |
| ulp-shift-few | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1089 | 9929 |
| mirror | exact_imprint | 99 | 198 | 105 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 93 | – | 198 | – | 93 | 262 | 8107 |
| mirror | float_cut | 99 | 198 | 37 | 0 / 40 / 0 / 0 | 72 | degenerate triangles 48, self-intersections 1 | 0 | 158 | 6 | – | 124 | 1177 |
| mirror | exact_union | 99 | 99 | 97 | 0 / 0 / 0 / 0 | 0 | self-intersections 2 | – | – | – | – | 255 | 12453 |
| mirror | float_union | 99 | 99 | 0 | 0 / 24 / 0 / 0 | 74 | degenerate triangles 1 | – | – | – | – | 1057 | 63203 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.91 | 1859 | 4.67 | 870 |
| face-contact | 0.65 | 155 | 2.85 | 77 |
| partial-overlap | 1.00 | 132 | 6.36 | 61 |
| ulp-shift | 0.10 | 200 | 0.88 | 100 |
| ulp-shift-few | 0.08 | 200 | 0.83 | 100 |
| mirror | 0.56 | 158 | 5.06 | 75 |

Disagreements (exact against float, per operation): 3066 (thingi-pairs: only exact good 1888, face-contact: both fail 5, face-contact: curve length 75, face-contact: only exact good 57, partial-overlap: curve length 3, partial-overlap: only exact good 162, partial-overlap: only float good 1, ulp-shift: both fail 1, ulp-shift: curve length 4, ulp-shift: float inserted nothing 196, ulp-shift: only exact good 99, ulp-shift-few: float inserted nothing 198, ulp-shift-few: only exact good 100, ulp-shift-few: only float good 1, mirror: both fail 64, mirror: curve length 23, mirror: float inserted nothing 2, mirror: only exact good 176, mirror: only float good 11)

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
| face-contact | face-contact_p0012_67986+80364 | b_by_a | curve length 0.531 of exact | good | good |
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
| face-contact | face-contact_p0021_1706461+636813 | b_by_a | curve length 0.755 of exact | good | good |
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

(3006 more in the raw rows)

Cases not written (placement refused as inexact, or load failed): 50
