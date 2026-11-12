# TextFormatBenchmark

Time per coordinate of `MeshIOUtil.FormatDouble` / `FormatFloat` (`ShortestDecimal`, Ryu) against the formats it replaced,
and of a whole OBJ write of 333k vertices. Inputs are procedural with fixed seeds (17 digit coordinates, any exponent,
3 decimals, floats), so nothing is downloaded. Median of 7 rounds (pass `<count> 7`; the default is 5) after a warm-up; the checksum is the total text length
and `same_as_R` counts the texts equal to .NET's shortest `"R"`.

    dotnet run --project benchmarks/TextFormatBenchmark -c Release [-- <count> <rounds>]

## Platform-independent shortest digits (November 2026)

Before, net10 wrote `"R"` and .NET Framework searched G15 / G16 / G17 with the platform parser, which is not always
correctly rounded there (a subnormal such as `5e-324` was written with 15 digits, and the text differed from net10's).
Now one integer implementation writes the same shortest text on every target; on net10 it is byte for byte the text of
`"R"` (tested against all 2^32 floats and 256M double bit patterns, and in the unit tests against millions).

Linux x64 container, net10.0, 1M values, ns per value, two runs:

| set | `"R"` (old net10) | G15/16/17 (old net48 path, run on net10) | `ShortestDecimal` |
|---|---|---|---|
| 17 digit coordinates | 164 / 162 | 519 | 128 / 129 |
| any exponent | 191 | 716 | 151 |
| 3 decimals | 115 | 201 | 132 |
| float | 134 / 126 | | 110 / 103 |
| whole OBJ write, 333k vertices, ns per vertex | 854 / 855 | | 802 / 773 |

The G15/16/17 column is the cost of the old .NET Framework path measured on net10 (its parse is faster there than on
.NET Framework itself, so the real saving on net48 is larger). Short decimals are 15 percent slower than `"R"` (the
Ryu general path), everything else is faster; the whole writer is 6 to 10 percent faster.
