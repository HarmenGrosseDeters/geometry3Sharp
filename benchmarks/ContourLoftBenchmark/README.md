# ContourLoftBenchmark

Scaling of `ContourLoft` (docs/design/CONTOUR_LOFT.md section 6): O(m n) triangle evaluations and O(m n log2 n) search
steps of 10 states each. Inputs: two wavy closed curves (radius 10 and 8, five lobes) one unit apart, m and n vertices.
The "ns per m n log2 n" column is flat when the search scales as claimed.

```
dotnet run --project benchmarks/ContourLoftBenchmark -c Release          # median of 5 runs per row
dotnet run --project benchmarks/ContourLoftBenchmark -c Release -- quick # 1 run, small sizes
```

Results (cloud container, 4 cores, the search is serial; commit 47a5d6c plus this README):

| m | n | m n | cost | median ms | ns per m n log2 n | status |
|---:|---:|---:|---|---:|---:|---|
| 256 | 256 | 65536 | area | 42.5 | 81.01 | Success |
| 256 | 256 | 65536 | constant (search only) | 27.0 | 51.42 | Success |
| 256 | 256 | 65536 | area + slope 1:1 | 99.2 | 189.18 | Success |
| 256 | 256 | 65536 | area + self-int. check | 35.8 | 68.28 | Success |
| 512 | 512 | 262144 | area | 142.8 | 60.53 | Success |
| 512 | 512 | 262144 | constant (search only) | 148.9 | 63.12 | Success |
| 512 | 512 | 262144 | area + slope 1:1 | 435.5 | 184.60 | Success |
| 512 | 512 | 262144 | area + self-int. check | 141.9 | 60.14 | Success |
| 1024 | 1024 | 1048576 | area | 634.6 | 60.52 | Success |
| 1024 | 1024 | 1048576 | constant (search only) | 559.1 | 53.32 | Success |
| 1024 | 1024 | 1048576 | area + slope 1:1 | 1677.9 | 160.02 | Success |
| 1024 | 1024 | 1048576 | area + self-int. check | 636.7 | 60.72 | Success |
| 2048 | 2048 | 4194304 | area | 2805.6 | 60.81 | Success |
| 2048 | 2048 | 4194304 | constant (search only) | 2401.6 | 52.05 | Success |
| 2048 | 2048 | 4194304 | area + slope 1:1 | 6869.2 | 148.88 | Success |
| 2048 | 2048 | 4194304 | area + self-int. check | 2715.1 | 58.85 | Success |
| 4096 | 1024 | 4194304 | area | 2572.5 | 61.33 | Success |
| 4096 | 1024 | 4194304 | constant (search only) | 2180.8 | 51.99 | Success |
| 4096 | 1024 | 4194304 | area + slope 1:1 | 6644.7 | 158.42 | Success |
| 4096 | 1024 | 4194304 | area + self-int. check | 2602.2 | 62.04 | Success |
| 16384 | 256 | 4194304 | area | 2289.0 | 68.22 | Success |
| 16384 | 256 | 4194304 | constant (search only) | 2036.9 | 60.71 | Success |
| 16384 | 256 | 4194304 | area + slope 1:1 | 6387.8 | 190.37 | Success |
| 16384 | 256 | 4194304 | area + self-int. check | 2173.6 | 64.78 | Success |

Before the inner-loop rewrite and the cached slope factors (commit 3273331, same machine, a review agent running alongside, so
the older numbers are an upper bound): 2048 x 2048 "area" 5192 ms, "constant" 4090 ms, "area + slope 1:1" 9949 ms; the
rewrite gives the same bands bit for bit (27 inputs compared, `/tmp` probe, not committed). Before the fix of the per-row
reset (commit 708d637) a 2048 x 2048 run did not finish within 10 minutes (O(m n^2)).

Reading: about 50-60 ns per m n log2 n for the search, the exact slope oracle adds about 100 ns per candidate
triangle pair (2 m n calls), the exact self-intersection check of the band is below the noise. At the default
`MaxCells` (2^22) a call takes about 3 s, 7 s with the slope oracle.
