# D1 apex rule against master

168 operations; errors: none; clean status differing between rounds: none

| build | clean | of them D1 models | total ms | rounding ms | ops > 5 s | results differing from master |
|---|---|---|---|---|---|---|
| master | 143 of 168 | 21 of 42 | 408809 | 152612 | 20 | 0 |
| d1 | 155 of 168 | 33 of 42 | 405209 | 149469 | 20 | 46 |

## Operations whose clean status differs from master

| case | op | master | d1 | D1 model |
|---|---|---|---|---|
| 1012236 | difference | not clean | clean | yes |
| 1012236 | intersection | not clean | clean | yes |
| 1053875 | union | not clean | clean | yes |
| 118037 | difference | not clean | clean | yes |
| 118037 | union | not clean | clean | yes |
| 129937 | difference | not clean | clean | yes |
| 129937 | intersection | not clean | clean | yes |
| 1396900 | union | not clean | clean | yes |
| 472043 | intersection | not clean | clean | yes |
| 472043 | union | not clean | clean | yes |
| 548875 | intersection | clean | not clean | yes |
| 548875 | union | not clean | clean | yes |
| 548878 | union | not clean | clean | yes |
| 551020 | intersection | not clean | clean | yes |

## Operations not clean in d1

100344 difference, 100344 union, 119829 difference, 119829 union, 119830 difference, 119830 union, 1514904 intersection, 472043 difference, 472046 difference, 548875 intersection, 669979 intersection, 686056 difference, 686056 union

## Time ratio d1 / master (total, best of 2), operations over 1 s in master

median 0.999, min 0.874, max 1.144 over 64 operations
