# F1 A/B (R: with the final lost-part check, N: without)

paired meshes: 800
Apply() true -> false (N true, R false): 0
Apply() false -> true: 0
result hash differs: 0
strict criterion differs: 0
timeouts (either): 0
wall sum R / N (s): 4537 / 4461 (+1.7 %)

# Scale-free repair study: corpus run

Meshes with a row in R: 800 of 800.

## R (library defaults)

| | first | holdout | all |
|---|---|---|---|
| rows | 400 | 400 | 800 |
| repaired (strict) | 277 | 271 | 548 |
| misses | 123 | 129 | 252 |
| miss: Hausdorff | 117 | 125 | 242 |
| miss: IoU | 6 | 4 | 10 |
| repaired, but a closed part off by > 1 % of its own diagonal | 8 | 8 | 16 |
| Apply() true, a closed part (>= 4 triangles) lost (> 25 % of its diagonal) | 16 | 12 | 28 |
| input: >= 2 components | 233 | 212 | 445 |
| input: >= 2 parts, box rule (B stage 1) | 29 | 23 | 52 |
| input: >= 2 parts, padded rule over all components | 68 | 65 | 133 |
| input: far-part switch (a triangle below 1e-12 diag^2, >= 2 components) | 125 | 109 | 234 |
| input: a triangle degenerate by the scene, not by its component | 31 | 26 | 57 |
| far-part mode used (FarPartsHandled) | 10 | 5 | 15 |
| B stage 0 passed triangles through | 25 | 27 | 52 |
| misses with >= 2 parts (box rule) | 5 | 4 | 9 |
| misses with >= 2 parts (padded rule) | 22 | 28 | 50 |

## Oracles against R (paired, meshes with a row in both)

| config | rows (run / copied) | repaired | gained | lost | net | repaired, own-component degeneracy (D) | part criterion ok | part ok gained / lost | timeouts | wall sum (s) |
|---|---|---|---|---|---|---|---|---|---|---|
| R | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 0 / 0 | 0 | 4537 |
| N | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 0 / 0 | 0 | 4461 |

Misses of R repaired by at least one of  (strict criterion): 0; by G (resolution, not scale): n/a.

