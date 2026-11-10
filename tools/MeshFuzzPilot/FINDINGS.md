# Findings of the first mesh-ops campaigns (2026-11-09, QA 2.0 T2)

Reproducers are the input bytes (hex) of the decoder in `Program.cs`; write them to a file with
`python3 -c "open('x.bin','wb').write(bytes.fromhex('<hex>'))"` and replay with
`MESH_FUZZ_OPS=<operations> dotnet MeshFuzzPilot.dll replay x.bin` (the operations column; the filter changes the
meaning of the first byte). `dump x.bin` prints the decoded case. Class as of QA.md 2.5a: **in** the supported range,
or **out** (magnitude, scale disparity, size) with the consequence (hang, false success, wrong result without a false
success). Campaigns: 3 workers, libFuzzer fork mode, instrumented build; the library state is master (2026-11-09) plus
PR #493 and #495 where noted.

## Fixed (bugfix PRs)

| Target | Finding | PR |
|---|---|---|
| tree-ray | no limit was float.MaxValue: rays and IsInside lost every hit beyond 3.4e38 (sphere of radius 1e100) | #493 |
| tree-ray | `IntrRay3Triangle3` ray parameter from cubes: 0 / lost below 1e-103 and above 1e102, the tree contradicted its boxes | #493 |
| tree-ray | centroid of a triangle above 6e307 overflowed: the triangle was left out of the tree | #493 |
| tree-winding | `TriSolidAngle`: 0 below 1e-103, NaN above 1e102 | #495 |

## Open

| # | Operations | Class | Symptom | Hex |
|---|---|---|---|---|
| 1 | partitioned-reducer | **in** | closed skewed tetrahedron, 3x subdivided (256 triangles), error (0.01 diagonal)^2, no area weights, MinTriangles 0: result 3 triangles, open; the serial Reducer gives 4, closed. Normalized corners (0,0,0), (1,.25,.1875), (0,1.25,.1875), (0,.25,1.1875) | `000000000000000403fe0cfe0000fe0000000000000000000000000000000000000000050300` |
| 2 | partitioned-reducer | **in** | the result for the 2^40 scaled copy is not the scaled result (documented contract) | `000403fe03fe000004040483fe000000000403fe03fe0000fe0000010000` |
| 3 | robust-repair (with the six Boolean/repair operations) | out: magnitude 1e176; **hang** | `MeshRobustRepair` runs over 120 s on collinear points at y, z about 1.9e176 (20 of 32 artifacts of one run) | `030303fefe08000303` |
| 4 | alpha-wrap | out: size disparity 1e293; **false success** | Success, an input vertex is outside the wrap (tetrahedron 1e-276 .. 3e17) | `3b5454204854542054545474a35454545454545454542c54cc54cc000000cc0000005454` |
| 5a | union (+ six ops) | out: scale below 1e-285, pair more than 2^60 apart; **false success** | `RoundingClean` with degenerate triangles at the rounding's scale (cube 2.6e-290, tetrahedron 3.6e-307) | `fcfefff8fbff00` |
| 5b | difference (+ six ops) | out: as 5a | the same claim | `020200fe0101` |
| 5c | robust-repair (+ six ops) | out: subnormal coordinate (2.5e-316); **false success** | `Apply()` true, ResultReason "", but inside-out components | `ffffffffffff02050107ffffffffffff0203` |
| 5d | robust-repair (+ six ops) | out: subnormal coordinate (1.5e-323); **false success** | clean, but self-intersections | `030203fe03fe000000000000000000000000000000000000000000000000003d03fe030000000000000000000000000000000000000000000000003d03fe03000000000000000000000203fe03fe04030200000000000000000000000000000000000000000000000000000000000000000001020000000104030501070207040a020e030a050f0315040d0614041c0510071905230613081e062a071609230731` |
| 6 | rounding-repair (+ six ops) | out: magnitude 4e176, all corners equal; wrong result | the mesh is not valid after the repair | `05050201fe049401fe` |
| 7 | alpha-wrap | out: magnitude | usable only between about 2^-250 and 2^250 (octahedron, relative alpha 5, relative offset 100): InvalidParameters at 2^-700 and below and 2^700 and above, Failed ("box corners on the result") at 2^+-300 to 2^+-500, a wrap of 520 instead of 48 triangles at 2^+-200 | none (a scaled octahedron) |
| 8 | alpha-wrap | out: extent about 2e-15 of the position magnitude; honest failure | Failed ("box corners on the result") for a point cluster of extent 5e-14 around -26.5 | `000903cbcbcbcbcbcbcb0000000000000071cb0000000000000000000001020000000104d8cbcbcbcbcbcb4bcbcbcbcbcbcbcbcbcbcbcbcbcbd8d8d8d8000000d8d8d8cbcbcbcb00` |

"six ops" = `MESH_FUZZ_OPS=union,intersection,difference,robust-repair,robust-repair-soup,rounding-repair`; items 1 and 2 use
`partitioned-reducer`, items 4, 7 and 8 `alpha-wrap`. The harness keeps the out-of-range classes out of its fuzz domain
(README, "domains"); once the per-module ranges exist, the domains follow them.

Since 2026-11-10 the decoder samples the scale exponents inside the supported ranges (QA.md 2.5a) three times in four and
judges out-of-range cases only for no crash, no hang, no false success (README, "Range domains"). The hex strings above decode to other
cases now; with the range checks of the per-module PRs, the original cases (decoded by the earlier decoder, see `git log` of Program.cs)
return the documented statuses: 3, 5c, 5d `OutsideRange`, 4, 7, 8 `InvalidInput`, 5a, 5b not `RoundingClean`, 6 not clean.
