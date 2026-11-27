# FastWindingAtan2Benchmark

End-to-end cost of the correctly rounded `CoreMath.Atan2` in the winding-number terms (#1577; before, the platform `Math.Atan2`): `MathUtil.TriSolidAngle`,
`FastWindingLeafKernel` and the boundary-edge angles of `ExactWindingCache` (the open-mesh part of `DMeshAABBTree3.WindingNumber`). The operations: `MeshRobustRepair`
(Solidify path) on three procedural broken inputs, `MeshWindingNumberSolidify` on a 49,152-triangle soup and, with `UseExactWinding`, on an open sphere,
`MeshWindingNumberBoolean` of two overlapping spheres, and the two queries alone (`FastWindingNumber` near and far field on a closed sphere,
`WindingNumber` on an open one).

```sh
dotnet run -c Release --project benchmarks/FastWindingAtan2Benchmark -- 5            # best of 5 per operation, in ms
dotnet run -c Release --project benchmarks/FastWindingAtan2Benchmark -- 9 "box flip"  # only the operations whose name contains the text
```

Baseline and candidate run the same program: build it in a `git worktree` of the base commit and in the candidate, and alternate the two (the machine drifts by
up to 14 % between runs). The last column of a line is the result (path and triangle count, or the sum of the queries), so a change of the output shows up.
The program references the library without a target framework, so it runs the net10.0 build: the vector leaf kernel (`FastWindingLeafKernel`, 256-bit lanes) is
in use; the netstandard2.0 build (the browser, net48) takes the scalar path, where a term is dearer.

## Result (2026-11-25, linux x64, net10.0 Release; best of 5, minimum of two runs of the full program; lines marked * best of 9, three alternating runs each)

| operation | base (ms) | CoreMath.Atan2 (ms) | change |
|---|---:|---:|---:|
| MeshRobustRepair, sphere soup jitter 1e-3 | 594.7 | 617.5 | +3.8 % |
| MeshRobustRepair, sphere flip / holes / dups | 760.0 | 794.3 | +4.5 % |
| MeshRobustRepair, box flip / holes / dups * (result: 14,712 -> 14,754 triangles) | 972.1 | 1098.7 | +13.0 % |
| MeshWindingNumberSolidify, 49,152-triangle soup | 244.6 | 222.1 | -9.2 % |
| MeshWindingNumberSolidify, `UseExactWinding`, open sphere * | 191.5 | 186.2 | -2.8 % |
| MeshWindingNumberBoolean union, two overlapping spheres * | 143.2 | 141.4 | -1.3 % |
| MeshWindingNumberBoolean intersection, two overlapping spheres | 105.1 | 108.8 | +3.5 % |
| `FastWindingNumber` alone, near field (100k queries within 2 % of a closed 49k sphere) | 1014.9 | 1107.7 | +9.1 % |
| `FastWindingNumber` alone, far field (100k queries, 1.5 to 4.5 away) | 17.4 | 17.7 | +1.7 % |
| `DMeshAABBTree3.WindingNumber` alone, open sphere (100k queries) * | 192.9 | 265.0 | +37.4 % |

The sums of the queries are bit-identical between base and candidate. Where the change comes from: in the leaf kernel the four lanes of a vector do the square
roots, dot products and the triple product, and `Atan2` is then one scalar call per triangle; `CoreMath.Atan2` costs about 34 ns more than the platform's
(54 -> 88 ns for a whole `TriSolidAngle` in a scalar micro-benchmark), so a query that is all leaf terms pays most (+9 % on the vector kernel; +30 to +36 % on
the scalar netstandard2.0 build, measured separately). An exact-winding query on an open mesh evaluates the `Atan2` of every boundary edge of the cache and
little else, so there it is +37 %; inside a Solidify the query is a small part of the work and the operation does not change. The one repair line that changes
the output (14,712 -> 14,754 triangles: a term moved by one ulp moves the contour; 5 of the 37 demo repair cases are that sensitive) also does a different amount of
work.

The plain `DMeshAABBTree3.WindingNumber` query on an open mesh is the one standalone cost above 15 % (+37.4 %: 193 -> 265 ms over 100k queries); no operation that uses it shows it (Solidify with `UseExactWinding`: -2.8 %). The box repair result changes from 14,712 to 14,754 triangles because the angles differ by 1 ulp in the last bit (about 0.3 % of the terms), not because a behaviour changed.
