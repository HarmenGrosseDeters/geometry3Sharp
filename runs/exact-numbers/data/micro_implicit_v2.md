## edge-plane points (fixed width today): X 192 bits, W 138 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 7898 | 6072 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 3734 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 3503 | 0 | 0 |
| Orient | BigInteger (today) | 934 | 1131 |  |
| Orient | FixedArith, loaded from BigInteger per call | 596 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 412 | 0 | 0 |
| plane side | BigInteger (today) | 318 | 448 |  |
| plane side | FixedArith, loaded from BigInteger per call | 338 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 180 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 1279 | 1640 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 627 | 280 |  |

## three-plane points beyond the fixed width: X 309 bits, W 255 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 18868 | 9375 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 7516 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 8644 | 0 | 0 |
| Orient | BigInteger (today) | 1963 | 1568 |  |
| Orient | FixedArith, loaded from BigInteger per call | 1085 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 890 | 0 | 0 |
| plane side | BigInteger (today) | 498 | 560 |  |
| plane side | FixedArith, loaded from BigInteger per call | 398 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 300 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 2010 | 2365 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 1146 | 440 |  |

## piece centroids: X 807 bits, W 753 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 85341 | 23968 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 61750 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 54327 | 0 | 0 |
| Orient | BigInteger (today) | 9338 | 3641 |  |
| Orient | FixedArith, loaded from BigInteger per call | 3479 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 3267 | 0 | 0 |
| plane side | BigInteger (today) | 627 | 1008 |  |
| plane side | FixedArith, loaded from BigInteger per call | 446 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 317 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 17340 | 5880 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 7201 | 1208 |  |

