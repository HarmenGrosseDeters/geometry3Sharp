# D1 cohort run: base (apex rule) against cand (reference rule)

16239 operations of 5413 models; rows: base 16239, cand 16239

- base: 15750 clean, 412 empty, 77 unclean
- cand: 15771 clean, 412 empty, 56 unclean

cand: 15998 operations took the verdict of base (the same result hash), the others were evaluated

## 1a. clean -> not clean (stop rule: each one analysed before the merge): 0


## 1b. not clean -> clean: 21

| id | op | base | cand | base drift | cand drift | base smallest rejected | cand smallest rejected |
|---|---|---|---|---|---|---|---|
| 100344 | difference | unclean: degenerate triangles | clean | 0.9990184308212242 | 0.4607815210889035 | 1.0001723459548986 | Infinity |
| 100344 | union | unclean: degenerate triangles | clean | 0.9991395666062187 | 0.44872603978885434 | 1.00000238044233 | Infinity |
| 103194 | intersection | unclean: degenerate triangles | clean | 0.5618772644513138 | 0.6535212108650528 | 1.016603187345278 | Infinity |
| 118037 | intersection | unclean: degenerate triangles | clean | 0.9103818505310259 | 0.9294639253959351 | 1.161410561489459 | 1.1816393409398789 |
| 119829 | difference | unclean: degenerate triangles | clean | 0.9748901147333165 | 0.4547349082699781 | 1.0074783459933474 | Infinity |
| 119829 | union | unclean: degenerate triangles | clean | 0.9872979749521222 | 0.30112220860226235 | 1.0077774371488115 | Infinity |
| 119830 | difference | unclean: degenerate triangles | clean | 0.9748901147333165 | 0.4547349082699781 | 1.0074783459933474 | Infinity |
| 119830 | union | unclean: degenerate triangles | clean | 0.9872979749521222 | 0.30112220860226235 | 1.0077774371488115 | Infinity |
| 132448 | union | unclean: degenerate triangles | clean | 0.9999071693349956 | 0.9999331603831032 | 1.000044539456017 | 1.0000077276968307 |
| 135793 | intersection | unclean: degenerate triangles | clean | 0.991776593178335 | 0.7643940655029219 | 1.0500501830067626 | Infinity |
| 366253 | intersection | unclean: degenerate triangles | clean | 0.8725042004810575 | 0.8453452120157859 | 1.1224696118007935 | Infinity |
| 471990 | intersection | unclean: degenerate triangles | clean | 0.9692623570471145 | 0.9907958928114247 | 1.0101508401016066 | 1.0201787322015081 |
| 472043 | difference | unclean: degenerate triangles | clean | 0.9925085717750372 | 0.9773478684761115 | 1.0004477425120613 | 1.0116334689200888 |
| 472046 | difference | unclean: degenerate triangles | clean | 0.9994491558368149 | 0.9910909267548856 | 1.0001460197786434 | 1.4162813110271864 |
| 548875 | intersection | unclean: degenerate triangles | clean | 0.9972339818960736 | 0.9389350702149057 | 1.0061705621165058 | 2.4417068133010016 |
| 686056 | difference | unclean: degenerate triangles | clean | 0.9774542827269268 | 0.4737873750432335 | 1.0102380766639194 | Infinity |
| 686056 | union | unclean: degenerate triangles | clean | 0.9990088357675998 | 0.5395079910079851 | 1.0002177601058502 | Infinity |
| 1036402 | difference | unclean: degenerate triangles | clean | 0.935006725195047 | 0.9349487512640831 | 1.0264660048408163 | Infinity |
| 1514904 | intersection | unclean: degenerate triangles | clean | 0.9993129992082508 | 0.9763098580978751 | 1.0012913782077486 | 1.0012913782077486 |
| 1706466 | difference | unclean: degenerate triangles | clean | 0.9468474586124507 | 0.836666280434645 | 1.0043118471071482 | 1.0904660757432867 |
| 1706466 | union | unclean: degenerate triangles | clean | 0.9827985992517416 | 0.9849797020280248 | 1.0010743262758806 | 1.3812895204793885 |

## 1c. other status changes (eg clean <-> empty, one reason for another): 0


## 2. no edit rejected by the bound in base: 15513 operations, 0 with a different result (stop rule: each one analysed)

Operations with a rejected edit in base: 314, of them 73 with the same result.

## 3. MaxDrift / MaxLength, deciles (operations with a rounding)

| build | n | 0 | 0.1 | 0.2 | 0.3 | 0.4 | 0.5 | 0.6 | 0.7 | 0.8 | 0.9 | 1 | > 0.9 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 15827 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1.8e-13 | 0.233 | 1 | 306 |
| cand | 15827 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1.8e-13 | 0.217 | 1 | 241 |

## 4. times (ms), timing pass: 1200 operations of 400 models

| build | field | sum | median | p95 | max |
|---|---|---|---|---|---|
| base | ms | 1392586 | 1004 | 2765 | 16991 |
| base | rounding_ms | 273569 | 122 | 683 | 4972 |
| cand | ms | 1400135 | 1019 | 2798 | 16994 |
| cand | rounding_ms | 280023 | 127 | 708 | 5021 |

Slower by more than 20 % and 0.2 s: 13

| id | op | base ms | cand ms | base rounding ms | cand rounding ms |
|---|---|---|---|---|---|
| 133078 | union | 2489 | 3108 | 736 | 957 |
| 601646 | union | 2750 | 3328 | 824 | 1077 |
| 1514901 | union | 974 | 1384 | 451 | 756 |
| 69701 | union | 1382 | 1706 | 388 | 708 |
| 1481523 | union | 1232 | 1538 | 360 | 441 |
| 95037 | difference | 546 | 832 | 115 | 215 |
| 1411687 | intersection | 1321 | 1594 | 156 | 203 |
| 353686 | difference | 1002 | 1262 | 202 | 221 |
| 399561 | union | 894 | 1153 | 159 | 247 |
| 838000 | intersection | 1235 | 1489 | 109 | 98 |
| 441723 | union | 1011 | 1225 | 327 | 399 |
| 1706472 | difference | 738 | 952 | 67 | 73 |
| 441716 | union | 887 | 1094 | 256 | 372 |
