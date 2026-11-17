| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|

### Exact intermediates (one pass over the original operands) against round-each-step

Clean: NotCleanReason(result, false) in {'', 'empty'} on the unwelded in-memory result and RoundingClean (not the abc-chained eval,
which welds the OFF result, splits pinches and tests self-intersections). Ratios are medians of per-result ratios; the volume
deviation is |V chained - V one pass| / V one pass (both rounded; the one pass is the exact chain rounded once).

| family | step | rows | chained clean | one pass clean | unclean only chained (intermediate rounding) | unclean in both (final rounding) | unclean only one pass | removed comps chained / one pass | median tris chained / exact / one pass rounded | median ms chained (rounding) / one pass / exact unrounded | max num / W bits | mean bits per vertex | vertices double / LPI / TPI / edge-TPI / beyond (sums) | tris chained / exact, median (max) | volume deviation median (max) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| abc-dui | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 19430 / 19430 / 19430 | 174 (56) / 126 / 122 | 104 / 105 | 102 | 265611/4129/0/0/0 | 1.00 (1.00) | 0.0e+00 (0.0e+00) |
| abc-dui | 2 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 27843 / 34430 / 27455 | 224 (71) / 352 / 290 | 189 / 192 | 118 | 472379/47540/0/0/0 | 1.00 (1.03) | 3.6e-16 (1.8e-11) |
| abc-dui | 3 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 20086 / 20353 / 19526 | 1488 (104) / 243 / 219 | 189 / 192 | 105 | 317579/4506/0/0/0 | 1.00 (1.99) | 2.3e-15 (1.4e-09) |
| abc-iud | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 1590 / 1590 / 1590 | 102 (24) / 96 / 80 | 114 / 115 | 127 | 57422/15085/0/0/0 | 1.00 (1.00) | 0.0e+00 (0.0e+00) |
| abc-iud | 2 | 30 | 28/28 | 28/28 | 0 | 0 | 0 | 0 / 0 | 18531 / 21934 / 19646 | 172 (60) / 1116 / 800 | 192 / 196 | 118 | 325922/25823/0/0/0 | 0.99 (1.00) | 1.2e-15 (1.3e-09) |
| abc-iud | 3 | 30 | 24/28 | 28/28 | 4 | 0 | 0 | 1166 / 983 | 10543 / 15817 / 7580 | 1163 (510) / 879 / 647 | 143 / 145 | 111 | 191762/143679/0/0/0 | 1.00 (1.09) | 3.7e-16 (4.6e-09) |
| abc-ucd | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 29107 / 29107 / 29107 | 233 (66) / 166 / 164 | 106 / 111 | 102 | 450445/2730/0/0/0 | 1.00 (1.00) | 0.0e+00 (0.0e+00) |
| abc-ucd | 2 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 36 / 36 | 28228 / 29094 / 28234 | 291 (68) / 336 / 347 | 122 / 124 | 106 | 428306/40361/0/0/0 | 1.00 (1.00) | 0.0e+00 (1.2e-13) |
| abc-ucd | 3 | 30 | 28/30 | 30/30 | 2 | 0 | 0 | 129 / 45 | 15378 / 15473 / 15245 | 1126 (35) / 246 / 237 | 122 / 124 | 105 | 219217/35901/0/0/0 | 1.00 (1.09) | 5.0e-16 (1.3e-03) |

All steps: 266 results with both variants; chained clean 260, one pass clean 266; unclean only chained 6, in both 0, only one pass 0.

| family | k | chains | one pass ms of step k / summed chained ms of steps 1..k: median | p90 | max |
|---|---|---|---|---|---|
| abc-dui | 1 | 30 | 0.74 | 0.90 | 1.03 |
| abc-dui | 2 | 30 | 0.69 | 0.83 | 0.95 |
| abc-dui | 3 | 30 | 0.19 | 0.49 | 0.60 |
| abc-iud | 1 | 30 | 0.88 | 0.99 | 1.06 |
| abc-iud | 2 | 28 | 1.15 | 9.11 | 25.08 |
| abc-iud | 3 | 28 | 0.34 | 0.89 | 0.96 |
| abc-ucd | 1 | 30 | 0.69 | 0.77 | 0.91 |
| abc-ucd | 2 | 30 | 0.60 | 0.85 | 0.93 |
| abc-ucd | 3 | 30 | 0.14 | 0.58 | 0.60 |

Unclean reasons (results with both variants; the repair's evaluation limit noted where reached): chained degenerate triangles: 1; chained degenerate triangles, evaluation limit: 1; chained inside-out components: 1; chained inside-out components, evaluation limit: 1; chained rounding not clean: 2
