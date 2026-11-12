## edge-plane points (fixed width today): X 192 bits, W 138 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 22747 | 6072 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 5099 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 5442 | 0 | 0 |
| Orient | BigInteger (today) | 1874 | 1131 |  |
| Orient | FixedArith, loaded from BigInteger per call | 1292 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 1315 | 0 | 0 |
| plane side | BigInteger (today) | 838 | 448 |  |
| plane side | FixedArith, loaded from BigInteger per call | 847 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 433 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 2637 | 1640 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 1124 | 280 |  |

## three-plane points beyond the fixed width: X 309 bits, W 255 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 18917 | 9375 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 14142 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 9404 | 0 | 0 |
| Orient | BigInteger (today) | 1520 | 1568 |  |
| Orient | FixedArith, loaded from BigInteger per call | 932 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 691 | 0 | 0 |
| plane side | BigInteger (today) | 415 | 560 |  |
| plane side | FixedArith, loaded from BigInteger per call | 397 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 211 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 2315 | 2365 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 1112 | 440 |  |

## piece centroids: X 807 bits, W 753 bits, n = 4000
| operation | method | ns / call | B / call | wrong signs |
|---|---|---|---|---|
| InCircle | BigInteger (today) | 107506 | 23968 |  |
| InCircle | FixedArith, loaded from BigInteger per call | 65491 | 0 | 0 |
| InCircle | FixedArith, points stored as limbs | 61888 | 0 | 0 |
| Orient | BigInteger (today) | 8462 | 3641 |  |
| Orient | FixedArith, loaded from BigInteger per call | 3950 | 0 | 0 |
| Orient | FixedArith, points stored as limbs | 3816 | 0 | 0 |
| plane side | BigInteger (today) | 716 | 1008 |  |
| plane side | FixedArith, loaded from BigInteger per call | 567 | 0 | 0 |
| plane side | FixedArith, points stored as limbs | 341 | 0 | 0 |
| centroid (construction, result kept) | BigInteger (today) | 15396 | 5880 |  |
| centroid (construction, result kept) | FixedArith, one limb array per point | 6400 | 1208 |  |

