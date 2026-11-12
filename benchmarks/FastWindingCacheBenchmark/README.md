# FastWindingCacheBenchmark

Build time of the fast-winding cache of `DMeshAABBTree3` (the cache is built by the first `FastWindingNumber` call on a
tree; the tree is built before the clock starts). Procedural input: a bumpy sphere with a cap removed (open), about 35k,
170k and 850k triangles, fixed seeds. Median of 7 rounds after a warm-up, serial (`gParallel.SerialOnThisThread`) and with
the parallel top levels; the checksum is the hash of the bits of 2000 winding numbers and must agree between library builds.

    dotnet run --project benchmarks/FastWindingCacheBenchmark -c Release [-- <rounds>]

## Per-box `List<int>` instead of `HashSet<int>` (2026-11-12)

The per-box triangle sets of the cache build are `List<int>` in the order the `HashSet<int>` enumerated them (no duplicates
occur: a triangle is below one leaf only), sized exactly. Container with 4 cores, single run, expect about ±15 % noise.
Checksums equal before and after in every row; `FastWindingCacheOrderTests` compares every box coefficient with the
former build bit for bit.

| triangles | mode | before ms | after ms | factor |
|---|---|---|---|---|
| 35 547 | serial | 38.3 | 33.8 | 1.13 |
| 35 547 | parallel | 19.4 | 16.8 | 1.15 |
| 169 823 | serial | 213.3 | 157.4 | 1.36 |
| 169 823 | parallel | 83.1 | 63.3 | 1.31 |
| 852 367 | serial | 1377.0 | 970.3 | 1.42 |
| 852 367 | parallel | 485.2 | 385.1 | 1.26 |
