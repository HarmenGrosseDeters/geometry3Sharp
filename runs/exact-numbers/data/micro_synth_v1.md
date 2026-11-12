## inrange-degenerate orient3d: n = 20000, sign 0 in 67.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1388 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 182 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 421 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 225 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1176 | 0 | 0 |  |
| P1 extended-exponent expansion | 996 | 0 | 0 |  |
| P4 row-scaled double filter | 37 | 0 | 0 | 1.2 % |

## inrange-degenerate orient2d: n = 20000, sign 0 in 68.6 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 415 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 107 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 94 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 59 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 337 | 0 | 0 |  |
| P1 extended-exponent expansion | 120 | 0 | 0 |  |
| P4 row-scaled double filter | 18 | 0 | 0 | 16.4 % |

## inrange-degenerate incircle: n = 20000, sign 0 in 68.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 2156 | 1922 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 347 | 1 | 0 |  |
| P2 monomial accumulator, table-driven | 982 | 0 | 0 |  |

## inrange-degenerate insphere: n = 20000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3120 | 4568 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3056 | 4547 | 0 |  |
| P2 monomial accumulator, table-driven | 10402 | 0 | 6583 |  |

## inrange-general orient3d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1295 | 2139 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 171 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 540 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 277 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 485 | 0 | 0 |  |
| P1 extended-exponent expansion | 1976 | 0 | 0 |  |
| P4 row-scaled double filter | 29 | 0 | 0 | 100.0 % |

## inrange-general orient2d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 516 | 898 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 124 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 153 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 88 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 194 | 0 | 0 |  |
| P1 extended-exponent expansion | 255 | 0 | 0 |  |
| P4 row-scaled double filter | 33 | 0 | 0 | 100.0 % |

## inrange-general incircle: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1340 | 2042 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 955 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 1136 | 0 | 0 |  |

## inrange-general insphere: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3242 | 4869 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3152 | 4869 | 0 |  |
| P2 monomial accumulator, table-driven | 8350 | 0 | 20000 |  |

## tiny-uniform orient3d: n = 20000, sign 0 in 67.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1205 | 2079 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 171 | 0 | 0 |  |
| P2 monomial accumulator, table-driven | 426 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 238 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 415 | 0 | 0 |  |
| P1 extended-exponent expansion | 983 | 0 | 0 |  |
| P4 row-scaled double filter | 18 | 0 | 0 | 1.2 % |

## tiny-uniform orient2d: n = 20000, sign 0 in 68.6 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 553 | 826 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 619 | 809 | 0 |  |
| P2 monomial accumulator, table-driven | 143 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 84 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 178 | 0 | 0 |  |
| P1 extended-exponent expansion | 194 | 0 | 0 |  |
| P4 row-scaled double filter | 14 | 0 | 0 | 16.4 % |

## tiny-uniform incircle: n = 20000, sign 0 in 68.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1238 | 1921 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1144 | 1804 | 0 |  |
| P2 monomial accumulator, table-driven | 1054 | 0 | 0 |  |

## tiny-uniform insphere: n = 20000, sign 0 in 67.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3205 | 4564 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3237 | 4543 | 0 |  |
| P2 monomial accumulator, table-driven | 10518 | 0 | 6580 |  |

## wide-3scales orient3d: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 10995 | 6618 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 11800 | 6593 | 0 |  |
| P2 monomial accumulator, table-driven | 531 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 330 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 4514 | 0 | 0 |  |
| P1 extended-exponent expansion | 856 | 0 | 0 |  |
| P4 row-scaled double filter | 32 | 0 | 0 | 92.5 % |

## wide-3scales orient2d: n = 20000, sign 0 in 0.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1748 | 1979 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1844 | 1976 | 0 |  |
| P2 monomial accumulator, table-driven | 129 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 88 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 823 | 0 | 0 |  |
| P1 extended-exponent expansion | 152 | 0 | 0 |  |
| P4 row-scaled double filter | 18 | 0 | 0 | 90.6 % |

## wide-3scales incircle: n = 20000, sign 0 in 0.2 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 17049 | 7939 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 15688 | 7930 | 0 |  |
| P2 monomial accumulator, table-driven | 1005 | 0 | 0 |  |

## wide-3scales insphere: n = 20000, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 53189 | 23099 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 52292 | 23099 | 0 |  |
| P2 monomial accumulator, table-driven | 8192 | 0 | 20000 |  |

## wide-full orient3d: n = 20000, sign 0 in 1.4 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 20847 | 9067 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 18579 | 9033 | 0 |  |
| P2 monomial accumulator, table-driven | 556 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 292 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 8000 | 0 | 0 |  |
| P1 extended-exponent expansion | 671 | 0 | 0 |  |
| P4 row-scaled double filter | 49 | 0 | 0 | 50.4 % |

## wide-full orient2d: n = 20000, sign 0 in 6.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 3025 | 2512 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 3016 | 2459 | 0 |  |
| P2 monomial accumulator, table-driven | 174 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 110 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1218 | 0 | 0 |  |
| P1 extended-exponent expansion | 132 | 0 | 0 |  |
| P4 row-scaled double filter | 25 | 0 | 0 | 69.5 % |

## wide-full incircle: n = 20000, sign 0 in 5.5 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 31069 | 11281 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 32846 | 10904 | 0 |  |
| P2 monomial accumulator, table-driven | 1563 | 0 | 0 |  |

## wide-full insphere: n = 20000, sign 0 in 1.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 118675 | 35267 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 113033 | 35231 | 0 |  |
| P2 monomial accumulator, table-driven | 8815 | 0 | 19803 |  |

