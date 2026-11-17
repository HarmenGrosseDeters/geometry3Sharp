| variant | method | chains | steps run (step 1/2/3) | chosen result clean or empty (step 1/2/3) | all steps clean | ended early: empty result / failed | unclean reasons of the chosen results | max identity error (step 1/2/3) | removed components (chosen results) | median ms of the chosen op, ok results (step 1/2/3) |
|---|---|---|---|---|---|---|---|---|---|---|

### Exact intermediates (one pass over the original operands) against round-each-step

| family | step | rows | chained clean | one pass clean | unclean only chained (intermediate rounding) | unclean in both (final rounding) | unclean only one pass | removed comps chained / one pass | median tris chained / exact / one pass rounded | median ms chained (rounding) / one pass / exact unrounded | max num / W bits | mean bits per vertex | vertices double / LPI / TPI / edge-TPI / beyond (sums) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| abc-dui | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 19430 / 19430 / 19430 | 532 (100) / 174 / 171 | 104 / 105 | 102 | 265611/4129/0/0/0 |
| abc-dui | 2 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 27843 / 34430 / 27455 | 422 (127) / 406 / 387 | 189 / 192 | 118 | 472379/47540/0/0/0 |
| abc-dui | 3 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 20086 / 20353 / 19526 | 1840 (128) / 316 / 289 | 189 / 192 | 105 | 317579/4506/0/0/0 |
| abc-iud | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 1590 / 1590 / 1590 | 444 (58) / 138 / 108 | 114 / 115 | 127 | 57422/15085/0/0/0 |
| abc-iud | 2 | 30 | 28/28 | 28/28 | 0 | 0 | 0 | 0 / 0 | 18531 / 21934 / 19646 | 216 (65) / 1506 / 1106 | 192 / 196 | 118 | 325922/25823/0/0/0 |
| abc-iud | 3 | 30 | 24/28 | 28/28 | 4 | 0 | 0 | 1166 / 983 | 10543 / 15817 / 7580 | 1494 (656) / 1149 / 830 | 143 / 145 | 111 | 191762/143679/0/0/0 |
| abc-ucd | 1 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 0 / 0 | 29107 / 29107 / 29107 | 607 (114) / 232 / 238 | 106 / 111 | 102 | 450445/2730/0/0/0 |
| abc-ucd | 2 | 30 | 30/30 | 30/30 | 0 | 0 | 0 | 36 / 36 | 28228 / 29094 / 28234 | 380 (78) / 464 / 384 | 122 / 124 | 106 | 428306/40361/0/0/0 |
| abc-ucd | 3 | 30 | 28/30 | 30/30 | 2 | 0 | 0 | 129 / 45 | 15378 / 15473 / 15245 | 1347 (38) / 285 / 262 | 122 / 124 | 105 | 219217/35901/0/0/0 |

All steps: 266 results with both variants; chained clean 260, one pass clean 266; unclean only chained 6, in both 0, only one pass 0.

Unclean reasons: chained degenerate triangles: 2; chained inside-out components: 2; chained rounding not clean: 2
