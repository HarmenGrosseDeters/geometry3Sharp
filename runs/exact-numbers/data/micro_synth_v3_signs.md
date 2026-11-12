## inrange-degenerate orient3d: n = 50000, sign 0 in 67.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3900 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 334 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 470 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 252 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1174 | 0 | 0 |  |
| P1 extended-exponent expansion | 993 | 0 | 0 |  |
| P4 row-scaled double filter | 59 | 0 | 0 | 1.0 % |

## inrange-degenerate orient2d: n = 50000, sign 0 in 68.7 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1078 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 169 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 116 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 78 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 426 | 0 | 0 |  |
| P1 extended-exponent expansion | 155 | 0 | 0 |  |
| P4 row-scaled double filter | 33 | 0 | 0 | 14.1 % |

## inrange-degenerate incircle: n = 50000, sign 0 in 68.7 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 5041 | 1922 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 577 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 1068 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1047 | 0 | 0 |  |

## inrange-degenerate insphere: n = 50000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 8677 | 4566 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 4447 | 4544 | 0 |  |
| P2 monomial accumulator, table-driven | 10757 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 2828 | 0 | 0 |  |

## inrange-general orient3d: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1521 | 2139 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 231 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 520 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 260 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1168 | 0 | 0 |  |
| P1 extended-exponent expansion | 1523 | 0 | 0 |  |
| P4 row-scaled double filter | 53 | 0 | 0 | 100.0 % |

## inrange-general orient2d: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 478 | 898 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 155 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 124 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 79 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 446 | 0 | 0 |  |
| P1 extended-exponent expansion | 238 | 0 | 0 |  |
| P4 row-scaled double filter | 44 | 0 | 0 | 100.0 % |

## inrange-general incircle: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1456 | 2042 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 940 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 1053 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 413 | 0 | 0 |  |

## inrange-general insphere: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 4457 | 4869 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3476 | 4869 | 0 |  |
| P2 monomial accumulator, table-driven | 10941 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1149 | 0 | 0 |  |

## tiny-uniform orient3d: n = 50000, sign 0 in 67.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1290 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 190 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 462 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 254 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 461 | 0 | 0 |  |
| P1 extended-exponent expansion | 961 | 0 | 0 |  |
| P4 row-scaled double filter | 112 | 0 | 0 | 1.0 % |

## tiny-uniform orient2d: n = 50000, sign 0 in 68.7 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 445 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 522 | 808 | 0 |  |
| P2 monomial accumulator, table-driven | 106 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 81 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 167 | 0 | 0 |  |
| P1 extended-exponent expansion | 158 | 0 | 0 |  |
| P4 row-scaled double filter | 61 | 0 | 0 | 14.1 % |

## tiny-uniform incircle: n = 50000, sign 0 in 68.7 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1271 | 1921 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1252 | 1802 | 0 |  |
| P2 monomial accumulator, table-driven | 2097 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 431 | 0 | 0 |  |

## tiny-uniform insphere: n = 50000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3083 | 4563 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3324 | 4541 | 0 |  |
| P2 monomial accumulator, table-driven | 10734 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1055 | 0 | 0 |  |

## wide-3scales orient3d: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 10487 | 6617 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 10699 | 6592 | 0 |  |
| P2 monomial accumulator, table-driven | 531 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 277 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 4349 | 0 | 0 |  |
| P1 extended-exponent expansion | 866 | 0 | 0 |  |
| P4 row-scaled double filter | 37 | 0 | 0 | 36.8 % |

## wide-3scales orient2d: n = 50000, sign 0 in 0.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1801 | 1977 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1983 | 1974 | 0 |  |
| P2 monomial accumulator, table-driven | 163 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 99 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 801 | 0 | 0 |  |
| P1 extended-exponent expansion | 173 | 0 | 0 |  |
| P4 row-scaled double filter | 24 | 0 | 0 | 61.5 % |

## wide-3scales incircle: n = 50000, sign 0 in 0.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 15000 | 7958 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 15265 | 7951 | 0 |  |
| P2 monomial accumulator, table-driven | 1020 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 10791 | 0 | 0 |  |

## wide-3scales insphere: n = 50000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 54384 | 23077 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 50895 | 23077 | 0 |  |
| P2 monomial accumulator, table-driven | 9646 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 26823 | 0 | 0 |  |

## wide-full orient3d: n = 50000, sign 0 in 1.5 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 21002 | 9072 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 19179 | 9039 | 0 |  |
| P2 monomial accumulator, table-driven | 566 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 299 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 8942 | 0 | 0 |  |
| P1 extended-exponent expansion | 726 | 0 | 0 |  |
| P4 row-scaled double filter | 44 | 0 | 0 | 1.4 % |

## wide-full orient2d: n = 50000, sign 0 in 5.9 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3146 | 2513 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3219 | 2461 | 0 |  |
| P2 monomial accumulator, table-driven | 186 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 123 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1411 | 0 | 0 |  |
| P1 extended-exponent expansion | 144 | 0 | 0 |  |
| P4 row-scaled double filter | 32 | 0 | 0 | 30.8 % |

## wide-full incircle: n = 50000, sign 0 in 5.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 30020 | 11286 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 30412 | 10926 | 0 |  |
| P2 monomial accumulator, table-driven | 1444 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 18360 | 0 | 0 |  |

## wide-full insphere: n = 50000, sign 0 in 1.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 116033 | 35258 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 123302 | 35219 | 0 |  |
| P2 monomial accumulator, table-driven | 9450 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 64694 | 0 | 0 |  |

## extreme-carry orient3d: n = 50000, sign 0 in 0.5 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 17607 | 8915 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 16865 | 8898 | 0 |  |
| P2 monomial accumulator, table-driven | 554 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 308 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 9199 | 0 | 0 |  |
| P1 extended-exponent expansion | 748 | 0 | 0 |  |
| P4 row-scaled double filter | 36 | 0 | 0 | 0.2 % |

## extreme-carry orient2d: n = 50000, sign 0 in 3.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3023 | 2619 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3334 | 2586 | 0 |  |
| P2 monomial accumulator, table-driven | 207 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 134 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1598 | 0 | 0 |  |
| P1 extended-exponent expansion | 161 | 0 | 0 |  |
| P4 row-scaled double filter | 26 | 0 | 0 | 5.6 % |

## extreme-carry incircle: n = 50000, sign 0 in 3.6 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 34425 | 12082 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 34959 | 11821 | 0 |  |
| P2 monomial accumulator, table-driven | 1077 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 21240 | 0 | 0 |  |

## extreme-carry insphere: n = 50000, sign 0 in 0.5 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 105852 | 35199 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 111173 | 35166 | 0 |  |
| P2 monomial accumulator, table-driven | 10014 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 68015 | 0 | 0 |  |

