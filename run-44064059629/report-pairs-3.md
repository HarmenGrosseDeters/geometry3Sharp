| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 250 | 500 | 500 | 0 / 0 / 0 / 0 | 0 | – | – | 418 | – | 0 | 119 | 854 |
| thingi-pairs | float_cut | 250 | 500 | 201 | 0 / 28 / 0 / 0 | 65 | degenerate triangles 190, self-intersections 14 | 2 | 390 | 0 | – | 78 | 2066 |
| thingi-pairs | exact_union | 250 | 250 | 250 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 166 | 1116 |
| thingi-pairs | float_union | 250 | 250 | 48 | 0 / 26 / 0 / 0 | 148 | bowties 13, degenerate triangles 13, self-intersections 2 | – | – | – | – | 724 | 16562 |

Time ratio float / exact (geometric mean over the operations both methods ran ok; ms of the operation alone, at least 1):

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.83 | 472 | 4.61 | 224 |

Disagreements (exact against float, per operation): 501 (thingi-pairs: only exact good 501)

| class | case | operation | kind | exact | float |
|---|---|---|---|---|---|
| thingi-pairs | p0750_1158268+135215 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0750_1158268+135215 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0750_1158268+135215 | union | only exact good | good | not closed |
| thingi-pairs | p0751_237630+271869 | union | only exact good | good | not closed |
| thingi-pairs | p0752_1099667+118882 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0752_1099667+118882 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0752_1099667+118882 | union | only exact good | good | not closed |
| thingi-pairs | p0753_113866+668419 | union | only exact good | good | not closed |
| thingi-pairs | p0754_702414+1083352 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0754_702414+1083352 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0754_702414+1083352 | union | only exact good | good | not closed |
| thingi-pairs | p0755_1146181+132440 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0755_1146181+132440 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0755_1146181+132440 | union | only exact good | good | not closed |
| thingi-pairs | p0757_108341+100336 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0757_108341+100336 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0757_108341+100336 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0758_58938+1536356 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0759_90359+196208 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0759_90359+196208 | b_by_a | only exact good | good | opened |
| thingi-pairs | p0759_90359+196208 | union | only exact good | good | not closed |
| thingi-pairs | p0760_70829+53048 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0761_77016+43690 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0761_77016+43690 | union | only exact good | good | not closed |
| thingi-pairs | p0762_958479+139767 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0762_958479+139767 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0762_958479+139767 | union | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0763_73464+370886 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0763_73464+370886 | union | only exact good | good | unclean: bowties |
| thingi-pairs | p0764_247674+567037 | union | only exact good | good | not closed |
| thingi-pairs | p0765_77015+135215 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0765_77015+135215 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0765_77015+135215 | union | only exact good | good | not closed |
| thingi-pairs | p0766_196127+1063862 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0766_196127+1063862 | union | only exact good | good | not closed |
| thingi-pairs | p0767_51011+54467 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0767_51011+54467 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0767_51011+54467 | union | only exact good | good | not closed |
| thingi-pairs | p0768_1063852+635749 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0768_1063852+635749 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0768_1063852+635749 | union | only exact good | good | not closed |
| thingi-pairs | p0769_51015+122550 | a_by_b | only exact good | good | unclean: self-intersections |
| thingi-pairs | p0769_51015+122550 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0769_51015+122550 | union | only exact good | good | not closed |
| thingi-pairs | p0771_80353+650181 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0771_80353+650181 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0771_80353+650181 | union | only exact good | good | not closed |
| thingi-pairs | p0772_635751+113862 | a_by_b | only exact good | good | opened |
| thingi-pairs | p0772_635751+113862 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0772_635751+113862 | union | only exact good | good | not closed |
| thingi-pairs | p0775_98660+350274 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0775_98660+350274 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0775_98660+350274 | union | only exact good | good | not closed |
| thingi-pairs | p0776_77927+1036654 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0776_77927+1036654 | union | only exact good | good | not closed |
| thingi-pairs | p0777_129912+839723 | a_by_b | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0777_129912+839723 | b_by_a | only exact good | good | unclean: degenerate triangles |
| thingi-pairs | p0777_129912+839723 | union | only exact good | good | not closed |
| thingi-pairs | p0778_500105+130979 | a_by_b | only exact good | good | exception |
| thingi-pairs | p0778_500105+130979 | b_by_a | only exact good | good | opened |

(441 more in the raw rows)
