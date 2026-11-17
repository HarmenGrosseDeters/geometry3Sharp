# Scale-free repair study: corpus run

Meshes with a row in R: 2 of 800.

## R (library defaults)

| | first | holdout | all |
|---|---|---|---|
| rows | 2 | 0 | 2 |
| repaired (strict) | 2 | 0 | 2 |
| misses | 0 | 0 | 0 |
| repaired, but a closed part off by > 1 % of its own diagonal | 0 | 0 | 0 |
| Apply() true, a closed part (>= 4 triangles) lost (> 25 % of its diagonal) | 0 | 0 | 0 |
| input: >= 2 components | 1 | 0 | 1 |
| input: >= 2 parts, box rule (B stage 1) | 0 | 0 | 0 |
| input: >= 2 parts, padded rule over all components | 1 | 0 | 1 |
| input: far-part switch (a triangle below 1e-12 diag^2, >= 2 components) | 0 | 0 | 0 |
| input: a triangle degenerate by the scene, not by its component | 0 | 0 | 0 |
| far-part mode used (FarPartsHandled) | 0 | 0 | 0 |
| B stage 0 passed triangles through | 1 | 0 | 1 |
| misses with >= 2 parts (box rule) | 0 | 0 | 0 |
| misses with >= 2 parts (padded rule) | 0 | 0 | 0 |

## Oracles against R (paired, meshes with a row in both)

| config | rows (run / copied) | repaired | gained | lost | net | repaired, own-component degeneracy (D) | part criterion ok | part ok gained / lost | timeouts | wall sum (s) |
|---|---|---|---|---|---|---|---|---|---|---|
| R | 2 / 0 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 13 |
| P | 0 / 2 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 0 |
| Q | 1 / 1 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 7 |
| S | 2 / 0 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 12 |
| L | 2 / 0 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 10 |
| G | 0 / 2 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 0 |
| T | 2 / 0 | 2 | 0 | 0 | 0 | 2 | 2 | 0 / 0 | 0 | 11 |

Misses of R repaired by at least one of PQSLT (strict criterion): 0; by G (resolution, not scale): 0.

T: 2 paired; the strict outcome differs on 0, the path on 0.

