| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 250 | 500 | 499 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | 390 | – | 1 | 145 | 1199 |
| thingi-pairs | float_cut | 250 | 500 | 206 | 0 / 30 / 0 / 0 | 53 | degenerate triangles 189, self-intersections 17 | 5 | 360 | 0 | – | 101 | 3841 |
| thingi-pairs | exact_union | 250 | 250 | 250 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 209 | 2053 |
| thingi-pairs | float_union | 250 | 250 | 61 | 0 / 29 / 0 / 0 | 130 | bowties 15, degenerate triangles 14, self-intersections 1 | – | – | – | – | 924 | 15639 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.90 | 470 | 4.35 | 221 |

Disagreements (exact against float, per operation): 482 (thingi-pairs: only exact good 482)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| thingi-pairs | p0503_96752+1535137 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0503_96752+1535137 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0503_96752+1535137 | union | only exact good | good | not closed |
| thingi-pairs | p0504_139765+1458694 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0504_139765+1458694 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0504_139765+1458694 | union | only exact good | good | not closed |
| thingi-pairs | p0505_159696+789077 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0505_159696+789077 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0505_159696+789077 | union | only exact good | good | not closed |
| thingi-pairs | p0507_40601+124186 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0507_40601+124186 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0507_40601+124186 | union | only exact good | good | not closed |
| thingi-pairs | p0508_314000+472050 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0508_314000+472050 | b_by_a | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0508_314000+472050 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0509_68199+805914 | a_by_b | only exact good | good | gaps |
| thingi-pairs | p0509_68199+805914 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0509_68199+805914 | union | only exact good | good | not closed |
| thingi-pairs | p0510_472205+135222 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0510_472205+135222 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0510_472205+135222 | union | only exact good | good | exception |
| thingi-pairs | p0511_472032+130977 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0511_472032+130977 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0511_472032+130977 | union | only exact good | good | exception |
| thingi-pairs | p0512_87398+636807 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0513_116863+697211 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0513_116863+697211 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0513_116863+697211 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0516_472123+76538 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0516_472123+76538 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0516_472123+76538 | union | only exact good | good | not closed |
| thingi-pairs | p0517_40992+49378 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0518_47851+38638 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0518_47851+38638 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0518_47851+38638 | union | only exact good | good | not closed |
| thingi-pairs | p0519_102296+248189 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0519_102296+248189 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0519_102296+248189 | union | only exact good | good | not closed |
| thingi-pairs | p0520_462549+55506 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0520_462549+55506 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0520_462549+55506 | union | only exact good | good | not closed |
| thingi-pairs | p0522_1706462+58010 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0522_1706462+58010 | union | only exact good | good | not closed |
| thingi-pairs | p0523_204477+70834 | union | only exact good | good | not closed |
| thingi-pairs | p0524_1307399+1065030 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0524_1307399+1065030 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0524_1307399+1065030 | union | only exact good | good | not closed |
| thingi-pairs | p0525_65003+343538 | union | only exact good | good | not closed |
| thingi-pairs | p0526_500091+635752 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0526_500091+635752 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0526_500091+635752 | union | only exact good | good | not closed |
| thingi-pairs | p0528_94155+51884 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0528_94155+51884 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0528_94155+51884 | union | only exact good | good | exception |
| thingi-pairs | p0529_84621+65612 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0529_84621+65612 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0529_84621+65612 | union | only exact good | good | not closed |
| thingi-pairs | p0530_44399+140511 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0530_44399+140511 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0530_44399+140511 | union | only exact good | good | not closed |

(422 more in the raw rows)
