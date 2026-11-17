| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 250 | 500 | 500 | 0 / 0 / 0 / 0 | 0 | – | – | 364 | – | 1 | 114 | 856 |
| thingi-pairs | float_cut | 250 | 500 | 227 | 0 / 36 / 0 / 0 | 53 | degenerate triangles 172, self-intersections 10 | 2 | 328 | 0 | – | 71 | 3025 |
| thingi-pairs | exact_union | 250 | 250 | 250 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 162 | 875 |
| thingi-pairs | float_union | 250 | 250 | 72 | 0 / 34 / 0 / 0 | 120 | bowties 11, degenerate triangles 12, self-intersections 1 | – | – | – | – | 721 | 21286 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.84 | 464 | 4.38 | 216 |

Disagreements (exact against float, per operation): 451 (thingi-pairs: only exact good 451)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| thingi-pairs | p0250_1492345+387871 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0250_1492345+387871 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0250_1492345+387871 | union | only exact good | good | not closed |
| thingi-pairs | p0251_129911+94662 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0251_129911+94662 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0251_129911+94662 | union | only exact good | good | exception |
| thingi-pairs | p0252_129910+668057 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0252_129910+668057 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0252_129910+668057 | union | only exact good | good | exception |
| thingi-pairs | p0253_135219+66486 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0253_135219+66486 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0253_135219+66486 | union | only exact good | good | exception |
| thingi-pairs | p0254_741529+196194 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0255_894835+84757 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0255_894835+84757 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0255_894835+84757 | union | only exact good | good | not closed |
| thingi-pairs | p0256_104639+40988 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0256_104639+40988 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0257_49529+472034 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0257_49529+472034 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0257_49529+472034 | union | only exact good | good | not closed |
| thingi-pairs | p0259_90980+462510 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0259_90980+462510 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0259_90980+462510 | union | only exact good | good | not closed |
| thingi-pairs | p0260_472118+1623064 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0261_1483721+212133 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0261_1483721+212133 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0261_1483721+212133 | union | only exact good | good | not closed |
| thingi-pairs | p0262_138557+98567 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0262_138557+98567 | union | only exact good | good | not closed |
| thingi-pairs | p0263_73427+185290 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0263_73427+185290 | union | only exact good | good | not closed |
| thingi-pairs | p0266_375257+37012 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0266_375257+37012 | b_by_a | only exact good | good | gaps |
| thingi-pairs | p0266_375257+37012 | union | only exact good | good | not closed |
| thingi-pairs | p0267_789083+168078 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0267_789083+168078 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0267_789083+168078 | union | only exact good | good | exception |
| thingi-pairs | p0268_919995+76206 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0268_919995+76206 | union | only exact good | good | not closed |
| thingi-pairs | p0270_251163+494998 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0270_251163+494998 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0270_251163+494998 | union | only exact good | good | exception |
| thingi-pairs | p0272_100336+1396901 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0272_100336+1396901 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0272_100336+1396901 | union | only exact good | good | not closed |
| thingi-pairs | p0273_73486+43850 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0273_73486+43850 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0273_73486+43850 | union | only exact good | good | not closed |
| thingi-pairs | p0274_1273629+240292 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0274_1273629+240292 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0274_1273629+240292 | union | only exact good | good | exception |
| thingi-pairs | p0275_472020+135365 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0275_472020+135365 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0275_472020+135365 | union | only exact good | good | exception |
| thingi-pairs | p0276_1681123+101333 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0276_1681123+101333 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0276_1681123+101333 | union | only exact good | good | not closed |
| thingi-pairs | p0277_50881+1322465 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0278_151365+186202 | b_by_a | only exact good | good | unclean: self-intersections |

(391 more in the raw rows)
