## inrange-degenerate orient3d: n = 20000, sign 0 in 67.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 2988 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 191 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 437 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 238 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1078 | 0 | 0 |  |
| P1 extended-exponent expansion | 900 | 0 | 0 |  |
| P4 row-scaled double filter | 34 | 0 | 0 | 1.2 % |

## inrange-degenerate orient2d: n = 20000, sign 0 in 68.6 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1079 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 157 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 130 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 94 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 516 | 0 | 0 |  |
| P1 extended-exponent expansion | 184 | 0 | 0 |  |
| P4 row-scaled double filter | 24 | 0 | 0 | 16.4 % |

## inrange-degenerate incircle: n = 20000, sign 0 in 68.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1379 | 1922 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 340 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 1185 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1025 | 0 | 0 |  |

## inrange-degenerate insphere: n = 20000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3005 | 4568 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3149 | 4547 | 0 |  |
| P2 monomial accumulator, table-driven | 10602 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1591 | 0 | 0 |  |

## inrange-general orient3d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1341 | 2139 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 226 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 796 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 419 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 486 | 0 | 0 |  |
| P1 extended-exponent expansion | 1877 | 0 | 0 |  |
| P4 row-scaled double filter | 48 | 0 | 0 | 100.0 % |

## inrange-general orient2d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 501 | 898 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 120 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 127 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 82 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 178 | 0 | 0 |  |
| P1 extended-exponent expansion | 240 | 0 | 0 |  |
| P4 row-scaled double filter | 33 | 0 | 0 | 100.0 % |

## inrange-general incircle: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1587 | 2042 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 967 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 1158 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 397 | 0 | 0 |  |

## inrange-general insphere: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 2970 | 4869 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 2768 | 4869 | 0 |  |
| P2 monomial accumulator, table-driven | 10224 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1245 | 0 | 0 |  |

## tiny-uniform orient3d: n = 20000, sign 0 in 67.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1288 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 268 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 639 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 495 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 647 | 0 | 0 |  |
| P1 extended-exponent expansion | 1318 | 0 | 0 |  |
| P4 row-scaled double filter | 51 | 0 | 0 | 1.2 % |

## tiny-uniform orient2d: n = 20000, sign 0 in 68.6 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 447 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 494 | 809 | 0 |  |
| P2 monomial accumulator, table-driven | 108 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 71 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 132 | 0 | 0 |  |
| P1 extended-exponent expansion | 144 | 0 | 0 |  |
| P4 row-scaled double filter | 11 | 0 | 0 | 16.4 % |

## tiny-uniform incircle: n = 20000, sign 0 in 68.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1172 | 1921 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1156 | 1804 | 0 |  |
| P2 monomial accumulator, table-driven | 1005 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 510 | 0 | 0 |  |

## tiny-uniform insphere: n = 20000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3002 | 4564 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3093 | 4543 | 0 |  |
| P2 monomial accumulator, table-driven | 9971 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 884 | 0 | 0 |  |

## wide-3scales orient3d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 10453 | 6618 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 9768 | 6593 | 0 |  |
| P2 monomial accumulator, table-driven | 501 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 283 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 4387 | 0 | 0 |  |
| P1 extended-exponent expansion | 817 | 0 | 0 |  |
| P4 row-scaled double filter | 27 | 0 | 0 | 92.5 % |

## wide-3scales orient2d: n = 20000, sign 0 in 0.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1695 | 1979 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1801 | 1976 | 0 |  |
| P2 monomial accumulator, table-driven | 241 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 112 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1128 | 0 | 0 |  |
| P1 extended-exponent expansion | 212 | 0 | 0 |  |
| P4 row-scaled double filter | 35 | 0 | 0 | 90.6 % |

## wide-3scales incircle: n = 20000, sign 0 in 0.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 15350 | 7939 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 16257 | 7930 | 0 |  |
| P2 monomial accumulator, table-driven | 1026 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 7766 | 0 | 0 |  |

## wide-3scales insphere: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 51137 | 23099 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 44911 | 23099 | 0 |  |
| P2 monomial accumulator, table-driven | 8352 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 24912 | 0 | 0 |  |

## wide-full orient3d: n = 20000, sign 0 in 1.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 16201 | 9067 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 17959 | 9033 | 0 |  |
| P2 monomial accumulator, table-driven | 501 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 269 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 8348 | 0 | 0 |  |
| P1 extended-exponent expansion | 682 | 0 | 0 |  |
| P4 row-scaled double filter | 50 | 0 | 0 | 50.4 % |

## wide-full orient2d: n = 20000, sign 0 in 6.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 2854 | 2512 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3017 | 2459 | 0 |  |
| P2 monomial accumulator, table-driven | 175 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 115 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1376 | 0 | 0 |  |
| P1 extended-exponent expansion | 139 | 0 | 0 |  |
| P4 row-scaled double filter | 26 | 0 | 0 | 69.5 % |

## wide-full incircle: n = 20000, sign 0 in 5.5 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 30215 | 11281 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 28159 | 10904 | 0 |  |
| P2 monomial accumulator, table-driven | 990 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 14775 | 0 | 0 |  |

## wide-full insphere: n = 20000, sign 0 in 1.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 101400 | 35267 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 108931 | 35231 | 0 |  |
| P2 monomial accumulator, table-driven | 9689 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 63379 | 0 | 0 |  |

