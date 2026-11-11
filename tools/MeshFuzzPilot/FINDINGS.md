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
uninstrumented build, one operation selected with `MESH_FUZZ_OPS`. The first smoke runs (about 1 800 inputs per target) took their options from the
queries' directions, which are constants for most inputs: their "no finding" meant little (the independent review found it); the options are four raw
bytes now and the numbers below are from the second round.

| Target | Inputs | Result |
|---|---|---|
| remesher | 2 000 + 1 250 | no finding |
| reducer | 2 000 + 1 250 | no finding |
| clipper2 | 2 000 + 4 000 | no finding; coordinates more than 2^16 extents from the origin (1e14 against an extent of 1) gave rings of zero area (the grid, 2^-40 of the extent, is finer than a double there): a limit of the representation, judged there only for no crash, Success and finite vertices |
| quality-mesh2 | 2 000 + 2 500 | the open classes below |

Open (quality-mesh2, to be triaged before any fix; the mesher and its oracle are another session's area; both are gated in the target, `MESH_FUZZ_STRICT=1`
judges them):

1. `AngleBelowBound` ("triangle ... is below 20 (25) degrees with no forced small angle"), about 1 in 700 inputs, **in every quality mode** (constrained,
   conforming and neither) with a minimum angle of 20 or 25 degrees, on a projected polyhedron with 17 to 36 crossing segments over 8 to 16 points (the
   Steiner limit is not reached). The first guess "constrained mode only" was wrong (the options were constants then). Whether the mesher or the oracle's notion of a
   forced small angle at crossings is at fault is not known: the shortest first-round input had points about 1e-25 from zero next to coincident points,
   so a snap or tube interplay is as likely as the crossings. Replay with `MESH_FUZZ_OPS=quality-mesh2` (the options are the last four bytes of an input; the
   hex of the first-round input, whose options are the old constants, is no longer a valid reproducer; `random 2500 22` with that filter finds it again).
2. `VertexUnused` / `SegmentMissing` with status Success for coordinates more than 2^16 extents from the origin (about 2^35 seen: automatic snap grid): a false
   success inside the documented range (up to 1e70).
3. `VertexUnused` / `SegmentMissing` with status Success for a coordinate of a tiny magnitude beside an extent of about 1 (1e-119 seen): the same gate.
4. (clipper2, fixed) `BooleanPaths` of a self-intersecting raw ring (NonZero) returned an outer ring of negative area (clockwise: a 3-vertex lobe of area 3e-5),
   against the documented orientation of results ("outer rings counterclockwise, holes clockwise"). Clipper2 reports a lobe of negative winding as a clockwise path
   even where it is an outer boundary; `ClipperFrame.ToGeneralPolygons` now orients every outer ring and hole by its grid area (`Clipper2ResultOrientationTests`).
   Input: `MESH_FUZZ_OPS=clipper2 MeshFuzzPilot random 4000 24` (0 findings after the fix; the target no longer gates it).
5. (clipper2, libFuzzer 10 min, 2 fork jobs, 1.3M execs: 70 crashes in 4 classes; 3 fixed in the adapter or the pilot's judgement, 1 is Clipper2 itself)
   - Fixed in the adapter (`ToGeneralPolygons`): Clipper2's `PolyTree64` nests by containment, which a self-touching outer ring fools, in both directions.
     A filled region of the same orientation as the outer ring that the tree nests as its hole had its area subtracted (27 crashes: a filled region
     missing from the union); a clockwise path inside a counterclockwise one that the tree reports at the top level was reoriented into an outer ring and
     its area added. Hole or outer is now decided by orientation and geometric containment (`Clipper2ResultOrientationTests`).
   - Pilot judgement: rings of a few grid cells of area, whose area in doubles is 0 or of either sign (the vertices are converted from the grid, where the ring
     is oriented), are not judged for orientation below `OrientationSlack` (16 squared grid units plus the rounding of a double area): the representation limit
     already documented for far-from-origin coordinates (18 crashes).
   - **Clipper2 2.0.0 itself (open, 16 crashes, not reported upstream):** `Clipper.Union` of two self-intersecting rings (NonZero) returns a region that
     neither input fills (inclusion-exclusion off by 10 percent, the winding of the union's flat paths is 1 where each input's is 0). It depends on the grid
     scale: with `PrecisionBits` 24 to 36 and 44 the same input is consistent, with 38 and the default 40 it is not (area of A|B 0.2210 against
     0.2000 = A + B - A&B). No adapter code is involved (the flat paths of a raw `Clipper64.Execute` show it). Input (doubles, A then B):
     A = (0,0) (0.125,0) (0.125,0.125) (0,0.125) (0.5703125,0.5703125) (0.6953125,0.5703125) (0.125,0.125) (0,0.125);
     B = (0.33984375,0.08984375) (-0.58984375,-0.58984375) (0.22265625,0.21484375) (0.48828125,-0.02734375) (0.21484375,0.08984375) (0.78515625,0.66015625);
     `Clipper2Polygons.BooleanPaths(ClipType.Union, {A}, {B})`, probe point (0.25463542, 0.20286458). Likely the cross products of large grid
     coordinates in Clipper2's double arithmetic; a lower default `PrecisionBits` would avoid this class, at a coarser grid (owner decision).
`ArgumentException` for coordinates above 1e70 is the documented contract: the target judges it (the normalized copy keeps the case's offset, so it is reached; accepted
input beyond 1e70 would be a finding).
The reducer's 2^40 scale check is judged only inside the Reducer's range (nonzero coordinates 2^-200 .. 2^150, QA.md 2.5a); two inputs with a vertex of magnitude 1e-235
beside others of 1 (the case's offset against its extent) gave a mesh that is not the scaled one: outside the range, no finding.

### Independent review of the second wave (2026-11-10) and what was done

Done in this PR: options from raw bytes; the 2^16 cut-offs removed for the mesher (judged at any distance; classes gated) and replaced for Clipper2 by a
judgment for no crash / Success / finite; Offset and Simplify results checked for finite vertices (holes too) and orientation; the self-test contradictions reach
every branch of `CheckRemesh` and `CheckReduce` that can be reached (the validity branches cannot: DMesh3 cannot hold an invalid mesh) and
`ExpectFinding` checks the branch by its message; `random` writes the running input's hex first (`MESH_FUZZ_LAST`), stops at a hang and uses the 8 MB stack
of libFuzzer's main thread; `run.sh` also instruments `geometry3Sharp.Clipper2.dll` and `Clipper2Lib.dll`; `MeshCommand.cs` and `TrianglePolyFormat.cs` are
no longer compiled. Not done: the oracle and the adapter of the mesher are not instrumented (the oracle judges, the mesher is `geometry3Sharp.dll`).
