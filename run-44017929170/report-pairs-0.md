| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 250 | 500 | 500 | 0 / 0 / 0 / 0 | 0 | – | – | 372 | – | 1 | 107 | 970 |
| thingi-pairs | float_cut | 250 | 500 | 228 | 0 / 47 / 0 / 0 | 49 | degenerate triangles 164, self-intersections 11 | 1 | 325 | 0 | – | 71 | 4646 |
| thingi-pairs | exact_union | 250 | 250 | 250 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 150 | 1742 |
| thingi-pairs | float_union | 250 | 250 | 68 | 0 / 41 / 0 / 0 | 112 | bowties 12, degenerate triangles 15, self-intersections 2 | – | – | – | – | 762 | 7894 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.92 | 453 | 4.80 | 209 |

Disagreements (exact against float, per operation): 454 (thingi-pairs: only exact good 454)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| thingi-pairs | p0000_248192+509314 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0000_248192+509314 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0000_248192+509314 | union | only exact good | good | not closed |
| thingi-pairs | p0001_269123+535201 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0002_347932+376235 | union | only exact good | good | not closed |
| thingi-pairs | p0003_1277085+105226 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0003_1277085+105226 | union | only exact good | good | not closed |
| thingi-pairs | p0005_762603+57937 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0005_762603+57937 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0005_762603+57937 | union | only exact good | good | exception |
| thingi-pairs | p0006_81291+93545 | union | only exact good | good | not closed |
| thingi-pairs | p0009_1016856+509319 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0009_1016856+509319 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0009_1016856+509319 | union | only exact good | good | not closed |
| thingi-pairs | p0011_472204+509314 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0011_472204+509314 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0011_472204+509314 | union | only exact good | good | not closed |
| thingi-pairs | p0012_84915+67408 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0012_84915+67408 | b_by_a | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0012_84915+67408 | union | only exact good | good | not closed |
| thingi-pairs | p0013_1681122+472196 | union | only exact good | good | not closed |
| thingi-pairs | p0015_301798+69883 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0016_1312958+238413 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0017_472079+498430 | union | only exact good | good | not closed |
| thingi-pairs | p0018_77019+1536355 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0018_77019+1536355 | union | only exact good | good | not closed |
| thingi-pairs | p0020_442384+372602 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0020_442384+372602 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0020_442384+372602 | union | only exact good | good | not closed |
| thingi-pairs | p0021_196126+200697 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0021_196126+200697 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0021_196126+200697 | union | only exact good | good | not closed |
| thingi-pairs | p0022_58564+84042 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0022_58564+84042 | b_by_a | only exact good | good | exception |
| thingi-pairs | p0022_58564+84042 | union | only exact good | good | exception |
| thingi-pairs | p0024_472088+37928 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0024_472088+37928 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0024_472088+37928 | union | only exact good | good | not closed |
| thingi-pairs | p0025_90287+1146169 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0027_124035+44011 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0027_124035+44011 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0027_124035+44011 | union | only exact good | good | exception |
| thingi-pairs | p0028_697226+98940 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0028_697226+98940 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0028_697226+98940 | union | only exact good | good | not closed |
| thingi-pairs | p0029_59230+40119 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0029_59230+40119 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0029_59230+40119 | union | only exact good | good | not closed |
| thingi-pairs | p0033_905406+134619 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0033_905406+134619 | union | only exact good | good | not closed |
| thingi-pairs | p0035_77930+216763 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0035_77930+216763 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0035_77930+216763 | union | only exact good | good | exception |
| thingi-pairs | p0036_129288+93540 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0036_129288+93540 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0037_472017+740392 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0037_472017+740392 | union | only exact good | good | not closed |
| thingi-pairs | p0039_60094+248188 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0039_60094+248188 | union | only exact good | good | not closed |
| thingi-pairs | p0040_822696+118914 | a_by_b | only exact good | good | exception |

(394 more in the raw rows)
