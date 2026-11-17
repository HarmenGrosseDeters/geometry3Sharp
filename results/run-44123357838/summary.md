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
| R | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 0 / 0 | 0 | 4644 |
| P | 52 / 748 | 548 | 2 | 2 | 0 | 550 | 677 | 0 / 1 | 0 | 275 |
| Q | 133 / 667 | 540 | 5 | 13 | -8 | 554 | 677 | 2 / 3 | 0 | 1064 |
| S | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 1 / 1 | 0 | 4599 |
| L | 800 / 0 | 545 | 0 | 3 | -3 | 545 | 676 | 0 / 2 | 0 | 4582 |
| G | 108 / 692 | 553 | 5 | 0 | 5 | 553 | 681 | 5 / 2 | 0 | 1981 |
| T | 800 / 0 | 547 | 0 | 1 | -1 | 547 | 677 | 1 / 2 | 0 | 4583 |

### P

- gained (2): 100032 (first; solidify -> parts; IoU -> ok; parts 5/5), 95777 (first; solidify -> parts; IoU -> ok; parts 5/5)
- lost (2): 255241 (first; exact -> parts; ok -> not clean (degenerate by the scene only); parts 3/3), 996811 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 2/2)
- part criterion lost (1): 96306 (holdout; wrap -> parts; Hausdorff -> Hausdorff; parts 2/2)

### Q

- gained (5): 100032 (first; solidify -> parts; IoU -> ok; parts 5/5), 1619332 (first; exact -> parts; Hausdorff -> ok; parts 1/10), 56535 (first; exact -> parts; Hausdorff -> ok; parts 1/10), 697602 (holdout; solidify -> parts; IoU -> ok; parts 1/4), 95777 (first; solidify -> parts; IoU -> ok; parts 5/5)
- lost (13): 1037019 (holdout; solidify -> parts; ok -> not clean (degenerate by the scene only); parts 1/8), 1037029 (first; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/14), 110378 (first; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/7), 195683 (first; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/2), 195685 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/4), 255241 (first; exact -> parts; ok -> not clean (degenerate by the scene only); parts 3/3), 37011 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/5), 409632 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/3), 63467 (holdout; exact -> parts; ok -> Hausdorff; parts 1/16), 64329 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/6), 77525 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/2), 88379 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 1/13), 996811 (holdout; exact -> parts; ok -> not clean (degenerate by the scene only); parts 2/2)
- part criterion gained (2): 1037019 (holdout; solidify -> parts; ok -> not clean (degenerate by the scene only); parts 1/8), 59196 (holdout; wrap -> parts; Hausdorff -> Hausdorff; parts 1/2)
- part criterion lost (3): 101952 (holdout; solidify -> parts; Hausdorff -> Hausdorff; parts 1/2), 180689 (holdout; exact -> parts; Hausdorff -> Hausdorff; parts 1/6), 96306 (holdout; wrap -> parts; Hausdorff -> Hausdorff; parts 2/2)

### S

- part criterion gained (1): 318843 (holdout; exact -> exact; ok -> ok; parts 1/1)
- part criterion lost (1): 49162 (holdout; exact -> solidify; Hausdorff -> Hausdorff; parts 1/1)

### L

- lost (3): 1037029 (first; exact -> solidify; ok -> IoU; parts 1/14), 318843 (holdout; exact -> solidify; ok -> Hausdorff; parts 1/1), 388565 (holdout; exact -> exact; ok -> Hausdorff; parts 1/1)
- part criterion lost (2): 611684 (holdout; kept -> kept; ok -> ok; parts 1/1), 95763 (first; kept -> kept; ok -> ok; parts 3/1)

### G

- gained (5): 100032 (first; solidify -> solidify; IoU -> ok; parts 5/5), 109923 (holdout; solidify -> solidify; IoU -> ok; parts 1/1), 1368057 (first; solidify -> solidify; Hausdorff -> ok; parts 1/1), 55928 (first; solidify -> solidify; Hausdorff -> ok; parts 1/1), 697602 (holdout; solidify -> solidify; IoU -> ok; parts 1/4)
- part criterion gained (5): 131438 (holdout; solidify -> wrap; Hausdorff -> IoU; parts 1/1), 135753 (first; solidify -> wrap; Hausdorff -> IoU; parts 1/1), 136405 (holdout; solidify -> wrap; Hausdorff -> IoU; parts 1/1), 95777 (first; solidify -> wrap; IoU -> IoU; parts 5/5), 99944 (first; solidify -> wrap; Hausdorff -> Hausdorff; parts 1/1)
- part criterion lost (2): 118657 (holdout; wrap -> solidify; IoU -> Hausdorff; parts 1/1), 96306 (holdout; wrap -> solidify; Hausdorff -> Hausdorff; parts 2/2)

### T

- lost (1): 1037029 (first; exact -> solidify; ok -> IoU; parts 1/14)
- part criterion gained (1): 136405 (holdout; solidify -> wrap; Hausdorff -> IoU; parts 1/1)
- part criterion lost (2): 1039823 (holdout; exact -> exact; Hausdorff -> Hausdorff; parts 1/3), 1102667 (first; exact -> solidify; ok -> ok; parts 1/1)

Misses of R repaired by at least one of PQSLT (strict criterion): 5; by G (resolution, not scale): 5.

T: 800 paired; the strict outcome differs on 1, the path on 7: 1037029 (exact -> solidify), 106620 (solidify -> wrap), 1102667 (exact -> solidify), 136405 (solidify -> wrap), 77958 (wrap -> solidify), 79136 (wrap -> solidify), 94484 (wrap -> exact).

