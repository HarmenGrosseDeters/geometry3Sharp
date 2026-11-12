## rec:rec711.bin orient3d: n = 148760, sign 0 in 0.1 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 5077 | 4625 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 4994 | 4625 | 0 |  |
| P2 monomial accumulator, table-driven | 387 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 202 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 1849 | 0 | 0 |  |
| P1 extended-exponent expansion | 782 | 0 | 0 |  |
| P4 row-scaled double filter | 52 | 0 | 0 | 20.5 % |

## rec:rec711.bin orient2d: n = 351234, sign 0 in 1.3 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 808 | 1399 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 880 | 1399 | 0 |  |
| P2 monomial accumulator, table-driven | 105 | 0 | 0 |  |
| P2 monomial accumulator, unrolled | 67 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 325 | 0 | 0 |  |
| P1 extended-exponent expansion | 102 | 0 | 0 |  |
| P4 row-scaled double filter | 40 | 0 | 0 | 65.9 % |

## rec:rec711.bin incircle: n = 6, sign 0 in 0.0 %
| method | ns / call | B / call | wrong signs | decided |
|---|---|---|---|---|
| BigInteger (*_bigint, today's fallback) | 1095 | 1844 | 0 |  |
| today's exact stage (int / adaptive / BigInteger by range) | 1146 | 1844 | 0 |  |
| P2 monomial accumulator, table-driven | 989 | 0 | 0 |  |
| P3 FixedArith, variable width on the stack | 363 | 0 | 0 |  |

