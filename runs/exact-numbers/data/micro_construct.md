edge-plane point: X 211 bits, W 155 bits, gcd 2 bits on average, n = 20000
| step | ns / point | B / point |
|---|---|---|
| construction with FixedArith on the stack (from limbs) | 660 | 0 |
| construction (BigInteger products and sums) | 2389 | 1128 |
| ExactPoint3.CreateUnreduced | 41 | 0 |
| ExactPoint3.Create (three gcds, four divisions) | 3054 | 159 |
|   of it: the three gcds | 2699 | 0 |
| FixedPoint3.TryCreate of the reduced point | 1045 | 0 |
fixed width admits 100.0 % of the reduced points
