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
| 1 (fixed, #516) | partitioned-reducer | **in** | closed skewed tetrahedron, 3x subdivided (256 triangles), error (0.01 diagonal)^2, no area weights, MinTriangles 0: result 3 triangles, open; the serial Reducer gives 4, closed. Normalized corners (0,0,0), (1,.25,.1875), (0,1.25,.1875), (0,.25,1.1875) | `000000000000000403fe0cfe0000fe0000000000000000000000000000000000000000050300` |
| 2 (resolved, #516: subnormal coordinate) | partitioned-reducer | out: subnormal | the result for the 2^40 scaled copy is not the scaled result (documented contract) | `000403fe03fe000004040483fe000000000403fe03fe0000fe0000010000` |
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

## Classes under the supported ranges (2026-11-10, QA.md 2.5a table)

| # | Range of the module | Class now | Action |
|---|---|---|---|
| 3 | robust repair: magnitude 2^-200 .. 2^400, extent >= 2^-40 of it | out (1e176) **hang** | range check, `Apply()` false with a reason (bugfix PR) |
| 4 | alpha wrap: magnitude 2^-128 .. 2^128 | out (1e-276, 3e17) **false success** | range check, `InvalidInput` (bugfix PR) |
| 5a, 5b | `RoundingClean` of the Boolean: the SafeRounding stage (`MeshRoundingRepair`) 2^-200 .. 2^200, disparity 2^60; the exact arrangement has no range | out **false success** | range check in `MeshRoundingRepair` (follows, held until #513) |
| 5c, 5d | robust repair: no subnormal coordinates | out **false success** | range check (bugfix PR) |
| 6 | rounding repair: magnitude 2^200 | out, wrong result, honest | range check of the rounding repair (follows, held until #513): not clean, mesh not touched |
| 7 | alpha wrap: usable 2^-160 .. 2^160 | out, honest `Failed` / hang at 2^+-180..200 | range check (hang), rest documented |
| 8 | alpha wrap: extent >= 2^-40 of the magnitude | out, honest `Failed` | range check (#527): `InvalidInput` |

Since 2026-11-10 the decoder samples the scale exponents inside the supported ranges (QA.md 2.5a) three times in four and
judges out-of-range cases only for no crash, no hang, no false success (README, "Range domains"). The hex strings above decode to other
cases now; with the range checks of the per-module PRs, the original cases (decoded by the earlier decoder, see `git log` of Program.cs)
return the documented statuses: 3, 5c, 5d `OutsideRange`, 4, 7, 8 `InvalidInput`; 5a, 5b and 6 follow with the range check of the rounding repair (held until #513).

## Second wave (QA 2.0 T13, 2026-11-10): smoke runs of the new targets

`MeshFuzzPilot random <n> <seed>` (no coverage guidance, not libFuzzer; the libFuzzer campaigns of the new targets are still to run) on the
uninstrumented build, one operation selected with `MESH_FUZZ_OPS`:

| Target | Inputs | Result |
|---|---|---|
| remesher | 1 800 (seeds 1, 7) | no finding |
| reducer | 1 800 (seeds 1, 7) | no finding |
| clipper2 | 4 800 (seeds 1, 7, 8) | no finding in the domain; outside it (coordinates more than 2^16 extents from the origin, 1e14 against an extent of 1) three results with a zero-area outer ring of two distinct vertices: the grid (2^-40 of the extent) is finer than a double at that magnitude, a limit of the representation, now outside the domain of the target |
| quality-mesh2 | about 5 000 (seeds 1, 2, 3, 5, 11, while the domain was being fixed) | **open class** below; `ArgumentException` for coordinates above 1e70 is the documented contract (outside the domain); `VertexUnused` / `SegmentMissing` with coordinates 2^35 extents from the origin (the automatic snap grid) outside the domain |

Open (quality-mesh2, to be triaged before any fix; the mesher and its oracle are another session's area): `AngleBelowBound` ("triangle ... is below 20 (25)
degrees with no forced small angle"), about 1 in 1 000 inputs, always `ConstrainedQuality` with a minimum angle of 20 or 25 degrees on a projected
polyhedron whose 11 to 36 segments cross (the Steiner limit is not reached). Replay with `MESH_FUZZ_OPS=quality-mesh2`; the shortest input:
`c7b3da9eaada3cdb135e1e5eda17447e855accdce3e983d947d76a8fee214eb017fcac7edb93c5ceec3c90806b3f4d5c1e79d624a78300de2f0056d26faded` (16 points, 32 segments, 4 coincident
points, angle 25, no explicit snap). Whether the mesher or the oracle's notion of a forced small angle at crossings is at fault is not known yet.

### Independent review of the second wave (2026-11-10) and what it changes: to do before the PR

- **The smoke numbers above are weaker than they read.** The option bytes are taken from the first query's direction (`ParamByte`), and in 6 of 8
  inputs (and in all seeds) that direction is an axis vector, so the options are constants (0, 127 or 255): the remesher then often runs with
  flips, collapses, splits and smoothing all disabled (a no-op), Clipper2 only reaches union and xor, the mesher and reducer options are
  fixed. "No finding" for remesher, reducer and clipper2 holds for those few option sets only. Fix: read the options from raw bytes after the
  queries in `Decode`, not from the query directions (the README says "bytes 0, 1, 2"), then repeat the smoke runs.
- **Domains.** The cut-offs (2^16 extents from the origin for the mesher and Clipper2, the dropped collapsed rings) return before anything is
  judged; the first-wave convention judges out-of-range input for no crash, no hang, no false success. The mesher's `VertexUnused` /
  `SegmentMissing` with status Success at 2^35 extents is such a false success and has to be judged (or classified as a limit with a number),
  not skipped. The 1e70 check is dead (normalized meshes). `Setup == null` and `CheckValidity` returns drop tiny-triangle and coincident-corner
  inputs silently; `Subdivide` keeps the vertices, so the comment about degenerate subdivision is wrong.
- **The open `AngleBelowBound` class needs a known-issue gate** before a libFuzzer run (it would stop every run) and its triage hint ("segments
  cross") may be wrong: the shortest input has points about 1e-25 from zero next to coincident points, so a snap or tube interplay is as likely.
- **Instrumentation.** `run.sh` instruments only `geometry3Sharp.dll`: the Clipper2 addon (and the oracle) get no coverage guidance; instrument the
  addon's dll too.
- **Checks that cannot fire.** The Offset and Simplify calls pass fixed `true` orientations and no membership check; the finite check covers
  outer rings only; `ExpectFinding` accepts any finding, and the self-test does not reach the invalid / empty / components branches of
  `CheckRemesh` and the not-finite / invalid / Euler / components branches of `CheckReduce`.
- **`random`:** a hung thread keeps running after the watchdog; the hex is printed after the run (a stack overflow or OOM loses the input; print it
  first); a 64 MB stack hides overflows that the 8 MB main thread of libFuzzer would see.
- **csproj:** `MeshCommand.cs` is compiled but not used by the pilot (drop it); changes to `benchmarks/Meshing2Benchmark/Candidates` now affect this
  tool's build (check `tools/ci_scope.py`).
