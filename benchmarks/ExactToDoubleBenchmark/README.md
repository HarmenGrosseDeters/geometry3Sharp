# ExactToDoubleBenchmark

`ExactPoint3.ToDouble(num, den, scaleExp)` in isolation (random `BigInteger` numerators and denominators of 1 to 300 bits, results
normal and subnormal) and `ExactPoint3.ToVector3d`. Issue #1410: the conversion was a 64-bit truncated quotient and a truncating
`(double)BigInteger` (up to 1 ulp off); it is now the correctly rounded value (one division, integer rounding on 64 bits).

Run: `DOTNET_TieredCompilation=0 DOTNET_TieredPGO=0 dotnet run -c Release`. The numbers are ns per call, the minimum of 9
repetitions, net10.0, three alternating runs of the old and the new build on a quiet machine (the range of the three).

| case | before (truncating) | after (correctly rounded) |
|---|---|---|
| num 100 bit / den 100 bit | 234 to 237 ns | 234 to 238 ns |
| num 200 bit / den 150 bit | 268 to 271 ns | 253 to 256 ns |
| num 60 bit / den 1 bit (W = 1) | 142 ns | 135 to 143 ns |
| num 300 bit / den 290 bit | 337 to 341 ns | 303 to 350 ns |
| subnormal results | 252 to 260 ns | 200 to 341 ns |
| `ExactPoint3.ToVector3d(-30)` | 698 to 709 ns | 690 to 732 ns |
