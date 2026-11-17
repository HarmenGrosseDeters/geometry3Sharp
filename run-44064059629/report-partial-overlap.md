| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| partial-overlap | exact_imprint | 73 | 146 | 146 | 0 / 0 / 0 / 0 | 0 | – | – | 128 | – | 0 | 87 | 313 |
| partial-overlap | float_cut | 73 | 146 | 47 | 0 / 14 / 0 / 0 | 27 | degenerate triangles 55, self-intersections 3 | 0 | 114 | 0 | – | 54 | 5311 |
| partial-overlap | exact_union | 73 | 73 | 73 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 111 | 328 |
| partial-overlap | float_union | 73 | 73 | 10 | 0 / 12 / 0 / 0 | 47 | degenerate triangles 4 | – | – | – | – | 601 | 18320 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| partial-overlap | 0.93 | 132 | 6.13 | 61 |

Disagreements (exact against float, per operation): 165 (partial-overlap: curve length 3, partial-overlap: only exact good 162)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| partial-overlap | partial-overlap_p0000_39641+224026 | a_by_b | curve length 0.934 of exact | good | good |
| partial-overlap | partial-overlap_p0000_39641+224026 | b_by_a | only exact good | good | exception |
| partial-overlap | partial-overlap_p0000_39641+224026 | union | only exact good | good | exception |
| partial-overlap | partial-overlap_p0003_814670+77336 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0003_814670+77336 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0005_472152+212134 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0005_472152+212134 | b_by_a | only exact good | good | exception |
| partial-overlap | partial-overlap_p0005_472152+212134 | union | only exact good | good | exception |
| partial-overlap | partial-overlap_p0006_248189+132536 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0006_248189+132536 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0006_248189+132536 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0008_92119+849721 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0008_92119+849721 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0008_92119+849721 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0010_383022+805914 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0010_383022+805914 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0010_383022+805914 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0013_212098+38645 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0013_212098+38645 | b_by_a | only exact good | good | exception |
| partial-overlap | partial-overlap_p0013_212098+38645 | union | only exact good | good | exception |
| partial-overlap | partial-overlap_p0014_914673+462517 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0015_1535134+98330 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0015_1535134+98330 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0015_1535134+98330 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0016_70835+75269 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0017_509313+1223673 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0017_509313+1223673 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0017_509313+1223673 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0018_125066+472032 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0018_125066+472032 | b_by_a | only exact good | good | opened |
| partial-overlap | partial-overlap_p0018_125066+472032 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0019_38498+471993 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0019_38498+471993 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0020_86236+314439 | union | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0021_162863+479931 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0021_162863+479931 | b_by_a | only exact good | good | exception |
| partial-overlap | partial-overlap_p0021_162863+479931 | union | only exact good | good | exception |
| partial-overlap | partial-overlap_p0024_78738+38261 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0024_78738+38261 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0024_78738+38261 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0025_60863+636796 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0025_60863+636796 | b_by_a | only exact good | good | opened |
| partial-overlap | partial-overlap_p0025_60863+636796 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0026_126283+635751 | b_by_a | only exact good | good | unclean: self-intersections |
| partial-overlap | partial-overlap_p0026_126283+635751 | union | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0029_38643+267784 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0029_38643+267784 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0029_38643+267784 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0030_136634+42025 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0030_136634+42025 | b_by_a | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0030_136634+42025 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0034_230152+1600247 | a_by_b | only exact good | good | opened |
| partial-overlap | partial-overlap_p0034_230152+1600247 | b_by_a | only exact good | good | opened |
| partial-overlap | partial-overlap_p0034_230152+1600247 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0035_140511+42193 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0035_140511+42193 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0037_69696+237643 | a_by_b | only exact good | good | unclean: degenerate triangles |
| partial-overlap | partial-overlap_p0037_69696+237643 | b_by_a | only exact good | good | opened |
| partial-overlap | partial-overlap_p0037_69696+237643 | union | only exact good | good | not closed |
| partial-overlap | partial-overlap_p0038_45404+387872 | union | only exact good | good | not closed |

(105 more in the raw rows)

Cases not written (placement refused as inexact, or load failed): 27
