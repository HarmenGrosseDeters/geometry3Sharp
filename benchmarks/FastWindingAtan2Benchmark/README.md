# FastWindingAtan2Benchmark

End-to-end cost of the correctly rounded `CoreMath.Atan2` in `MathUtil.TriSolidAngle` and `FastWindingLeafKernel` (#1577; before, the platform
`Math.Atan2`), on the operations that use the floating-point winding number: `MeshRobustRepair` (Solidify path) on three procedural broken inputs,
`MeshWindingNumberSolidify` on a 49,152-triangle soup, and `MeshWindingNumberBoolean` (union, intersection) of two overlapping spheres.

```sh
dotnet run -c Release --project benchmarks/FastWindingAtan2Benchmark -- 5      # best of 5 per operation, in ms
```

Baseline and candidate run the same program: build it in a `git worktree` of the base commit and in the candidate and compare. The last
column of a line is the result (path and triangle count), so a change of the output shows up.

## Result (2026-11-25, linux x64, net10.0 Release, library netstandard2.0; best of 5, minimum of two program runs)

| operation | base (ms) | CoreMath.Atan2 (ms) | change |
|---|---:|---:|---:|
| MeshRobustRepair, sphere soup jitter 1e-3 | 582.5 | 598.3 | +2.7 % |
| MeshRobustRepair, sphere flip / holes / dups | 779.2 | 830.5 | +6.6 % |
| MeshRobustRepair, box flip / holes / dups | 1042.1 | 1061.6 | +1.9 % |
| MeshWindingNumberSolidify, 49,152-triangle soup | 237.5 | 224.0 | -5.7 % |
| MeshWindingNumberBoolean union, two overlapping spheres | 152.0 | 158.8 | +4.5 % |
| MeshWindingNumberBoolean intersection, two overlapping spheres | 130.5 | 123.7 | -5.2 % |

The two base runs differ by up to 8 % per line, so every change is inside the noise. Worst case of the kernel alone, 200,000 queries within 2 % of
the surface of a 199,692-triangle sphere: 3.5-3.7 s against 4.8-5.0 s (+30 to +36 %); far-field queries (1.5 to 4.5 away): 41 against 36 ms.
The sums are bit-identical in both cases. The result of one line differs: the box case gives 14,712 triangles on the base and 14,754 with the
correctly rounded term (a 1 ulp difference of a term moves the contour; 5 of the 37 demo repair cases are that sensitive).
