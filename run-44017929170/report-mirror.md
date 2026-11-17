| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| mirror | exact_imprint | 99 | 198 | 105 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 93 | – | 198 | – | 93 | 262 | 8107 |
| mirror | float_cut | 99 | 198 | 37 | 0 / 40 / 0 / 0 | 72 | degenerate triangles 48, self-intersections 1 | 0 | 158 | 6 | – | 124 | 1177 |
| mirror | exact_union | 99 | 99 | 97 | 0 / 0 / 0 / 0 | 0 | self-intersections 2 | – | – | – | – | 255 | 12453 |
| mirror | float_union | 99 | 99 | 0 | 0 / 24 / 0 / 0 | 74 | degenerate triangles 1 | – | – | – | – | 1057 | 63203 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| mirror | 0.56 | 158 | 5.06 | 75 |

Disagreements (exact against float, per operation): 276 (mirror: both fail 64, mirror: curve length 23, mirror: float inserted nothing 2, mirror: only exact good 176, mirror: only float good 11)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| mirror | mirror_p0000_509309+509309 | a_by_b | only exact good | good | exception |
| mirror | mirror_p0000_509309+509309 | b_by_a | only exact good | good | exception |
| mirror | mirror_p0000_509309+509309 | union | only exact good | good | exception |
| mirror | mirror_p0001_591248+591248 | a_by_b | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0001_591248+591248 | b_by_a | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0001_591248+591248 | union | only exact good | good | not closed |
| mirror | mirror_p0002_78976+78976 | a_by_b | only exact good | good | opened |
| mirror | mirror_p0002_78976+78976 | b_by_a | only exact good | good | exception |
| mirror | mirror_p0002_78976+78976 | union | only exact good | good | exception |
| mirror | mirror_p0003_37288+37288 | a_by_b | only exact good | good | unclean: degenerate triangles |
| mirror | mirror_p0003_37288+37288 | b_by_a | curve length 0.222 of exact | good | good |
| mirror | mirror_p0003_37288+37288 | union | only exact good | good | not closed |
| mirror | mirror_p0004_55353+55353 | a_by_b | curve length 0.212 of exact | good | good |
| mirror | mirror_p0004_55353+55353 | b_by_a | curve length 0.212 of exact | good | good |
| mirror | mirror_p0004_55353+55353 | union | only exact good | good | not closed |
| mirror | mirror_p0005_84781+84781 | a_by_b | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0005_84781+84781 | b_by_a | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0005_84781+84781 | union | only exact good | good | not closed |
| mirror | mirror_p0006_101636+101636 | a_by_b | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0006_101636+101636 | b_by_a | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0006_101636+101636 | union | only exact good | good | exception |
| mirror | mirror_p0007_119918+119918 | a_by_b | only exact good | good | opened |
| mirror | mirror_p0007_119918+119918 | b_by_a | only exact good | good | exception |
| mirror | mirror_p0007_119918+119918 | union | only exact good | good | exception |
| mirror | mirror_p0008_472172+472172 | a_by_b | only exact good | good | exception |
| mirror | mirror_p0008_472172+472172 | b_by_a | only exact good | good | exception |
| mirror | mirror_p0008_472172+472172 | union | only exact good | good | exception |
| mirror | mirror_p0009_106785+106785 | a_by_b | only exact good | good | unclean: degenerate triangles |
| mirror | mirror_p0009_106785+106785 | b_by_a | only exact good | good | unclean: degenerate triangles |
| mirror | mirror_p0009_106785+106785 | union | only exact good | good | not closed |
| mirror | mirror_p0010_1120768+1120768 | a_by_b | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0010_1120768+1120768 | b_by_a | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0010_1120768+1120768 | union | only exact good | good | not closed |
| mirror | mirror_p0011_1454015+1454015 | a_by_b | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0011_1454015+1454015 | b_by_a | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0011_1454015+1454015 | union | only exact good | good | exception |
| mirror | mirror_p0012_1458684+1458684 | a_by_b | curve length 0.846 of exact | good | good |
| mirror | mirror_p0012_1458684+1458684 | b_by_a | curve length 0.846 of exact | good | good |
| mirror | mirror_p0012_1458684+1458684 | union | only exact good | good | not closed |
| mirror | mirror_p0013_442382+442382 | a_by_b | only exact good | good | unclean: degenerate triangles |
| mirror | mirror_p0013_442382+442382 | b_by_a | only exact good | good | unclean: degenerate triangles |
| mirror | mirror_p0013_442382+442382 | union | only exact good | good | not closed |
| mirror | mirror_p0014_200964+200964 | a_by_b | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0014_200964+200964 | b_by_a | both fail | unclean: degenerate triangles | exception |
| mirror | mirror_p0014_200964+200964 | union | only exact good | good | exception |
| mirror | mirror_p0015_37928+37928 | a_by_b | only exact good | good | opened |
| mirror | mirror_p0015_37928+37928 | b_by_a | only exact good | good | opened |
| mirror | mirror_p0015_37928+37928 | union | only exact good | good | not closed |
| mirror | mirror_p0016_106100+106100 | a_by_b | only exact good | good | opened |
| mirror | mirror_p0016_106100+106100 | b_by_a | only exact good | good | opened |
| mirror | mirror_p0016_106100+106100 | union | only exact good | good | not closed |
| mirror | mirror_p0017_130962+130962 | a_by_b | both fail | unclean: degenerate triangles | opened |
| mirror | mirror_p0017_130962+130962 | union | only exact good | good | not closed |
| mirror | mirror_p0018_215846+215846 | a_by_b | only float good | unclean: degenerate triangles | good |
| mirror | mirror_p0018_215846+215846 | b_by_a | only float good | unclean: degenerate triangles | good |
| mirror | mirror_p0018_215846+215846 | union | only exact good | good | not closed |
| mirror | mirror_p0019_622000+622000 | a_by_b | only exact good | good | opened |
| mirror | mirror_p0019_622000+622000 | b_by_a | only exact good | good | opened |
| mirror | mirror_p0019_622000+622000 | union | only exact good | good | not closed |
| mirror | mirror_p0020_375267+375267 | a_by_b | only float good | unclean: degenerate triangles | good |

(216 more in the raw rows)

Cases not written (placement refused as inexact, or load failed): 1
