| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ulp-shift | exact_imprint | 100 | 200 | 18 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 182 | – | 200 | – | 182 | 2692 | 6373 |
| ulp-shift | float_cut | 100 | 200 | 200 | 0 / 0 / 0 / 0 | 0 | – | 0 | 200 | 200 | – | 244 | 1076 |
| ulp-shift | exact_union | 100 | 100 | 99 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | – | – | – | 1206 | 6551 |
| ulp-shift | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1124 | 21945 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| ulp-shift | 0.10 | 200 | 0.88 | 100 |

Disagreements (exact against float, per operation): 300 (ulp-shift: both fail 1, ulp-shift: curve length 4, ulp-shift: float inserted nothing 196, ulp-shift: only exact good 99)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| ulp-shift | ulp-shift_p0000_76573+76573 | a_by_b | float inserted nothing (exact: 591 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0000_76573+76573 | b_by_a | float inserted nothing (exact: 620 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0000_76573+76573 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0001_204267+204267 | a_by_b | float inserted nothing (exact: 6492 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0001_204267+204267 | b_by_a | float inserted nothing (exact: 6397 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0001_204267+204267 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0002_100342+100342 | a_by_b | float inserted nothing (exact: 2343 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0002_100342+100342 | b_by_a | float inserted nothing (exact: 2648 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0002_100342+100342 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0003_51016+51016 | a_by_b | float inserted nothing (exact: 338 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0003_51016+51016 | b_by_a | float inserted nothing (exact: 340 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0003_51016+51016 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0004_73737+73737 | a_by_b | float inserted nothing (exact: 3 vertices) | good | good |
| ulp-shift | ulp-shift_p0004_73737+73737 | b_by_a | float inserted nothing (exact: 1 vertices) | good | good |
| ulp-shift | ulp-shift_p0004_73737+73737 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0005_80750+80750 | a_by_b | float inserted nothing (exact: 1785 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0005_80750+80750 | b_by_a | float inserted nothing (exact: 1868 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0005_80750+80750 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0006_1349593+1349593 | a_by_b | float inserted nothing (exact: 2339 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0006_1349593+1349593 | b_by_a | float inserted nothing (exact: 2362 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0006_1349593+1349593 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0007_702386+702386 | a_by_b | float inserted nothing (exact: 636 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0007_702386+702386 | b_by_a | float inserted nothing (exact: 774 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0007_702386+702386 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0008_958478+958478 | a_by_b | float inserted nothing (exact: 264 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0008_958478+958478 | b_by_a | float inserted nothing (exact: 216 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0008_958478+958478 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0009_124038+124038 | a_by_b | float inserted nothing (exact: 756 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0009_124038+124038 | b_by_a | float inserted nothing (exact: 1387 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0009_124038+124038 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0010_67501+67501 | a_by_b | float inserted nothing (exact: 3901 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0010_67501+67501 | b_by_a | float inserted nothing (exact: 3896 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0010_67501+67501 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0011_472202+472202 | a_by_b | float inserted nothing (exact: 3134 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0011_472202+472202 | b_by_a | float inserted nothing (exact: 3136 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0011_472202+472202 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0012_472192+472192 | a_by_b | float inserted nothing (exact: 6286 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0012_472192+472192 | b_by_a | float inserted nothing (exact: 6110 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0012_472192+472192 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0013_85493+85493 | a_by_b | float inserted nothing (exact: 196 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0013_85493+85493 | b_by_a | float inserted nothing (exact: 172 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0013_85493+85493 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0014_118680+118680 | a_by_b | float inserted nothing (exact: 3928 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0014_118680+118680 | b_by_a | float inserted nothing (exact: 3593 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0014_118680+118680 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0015_1146190+1146190 | a_by_b | float inserted nothing (exact: 1961 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0015_1146190+1146190 | b_by_a | float inserted nothing (exact: 1873 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0015_1146190+1146190 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0016_153951+153951 | a_by_b | float inserted nothing (exact: 620 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0016_153951+153951 | b_by_a | float inserted nothing (exact: 612 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0016_153951+153951 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0017_996810+996810 | a_by_b | float inserted nothing (exact: 376 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0017_996810+996810 | b_by_a | float inserted nothing (exact: 8 vertices) | good | good |
| ulp-shift | ulp-shift_p0017_996810+996810 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0018_129920+129920 | a_by_b | float inserted nothing (exact: 99 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0018_129920+129920 | b_by_a | float inserted nothing (exact: 129 vertices) | unclean: degenerate triangles | good |
| ulp-shift | ulp-shift_p0018_129920+129920 | union | only exact good | good | not closed |
| ulp-shift | ulp-shift_p0019_462546+462546 | a_by_b | float inserted nothing (exact: 24 vertices) | good | good |
| ulp-shift | ulp-shift_p0019_462546+462546 | b_by_a | float inserted nothing (exact: 40 vertices) | good | good |
| ulp-shift | ulp-shift_p0019_462546+462546 | union | only exact good | good | not closed |

(240 more in the raw rows)
