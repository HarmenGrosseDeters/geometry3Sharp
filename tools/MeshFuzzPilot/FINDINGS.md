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
5. (clipper2, libFuzzer 10 min, 2 fork jobs, 1.3M execs: 70 crashes; by the replay messages: 27 a filled region missing from the union, 26 a region in the union that neither input fills, 18 orientation; the groups sum to 71, one artifact is probably counted twice)
   - Fixed in the adapter (`ToGeneralPolygons`): Clipper2's `PolyTree64` nests by containment, which a self-touching outer ring fools, in both directions.
     A filled region of the same orientation as the outer ring that the tree nests as its hole had its area subtracted (27 crashes: a filled region
     missing from the union); a clockwise path inside a counterclockwise one that the tree reports at the top level was reoriented into an outer ring and
     its area added (10 of the 26 'region in the union' crashes; the other 16 are Clipper2 itself, below). The tree stays the default; a same-sign hole is a region of its own, and a negative top-level path is a hole of the smallest positive path that holds it, else a filled lobe (`Clipper2ResultOrientationTests`).
   - Pilot judgement: rings of a few grid cells of area, whose area in doubles is 0 or of either sign (the vertices are converted from the grid, where the ring
     is oriented), are not judged for orientation below `OrientationSlack` (16 squared grid units plus the rounding of a double area): the representation limit
     already documented for far-from-origin coordinates (16 of the 18 orientation crashes).
   - Open, minor (the other 2 orientation crashes, Offset of the union of a ring of 1e-12 extent with a ring 1.7 away, frame resolution 1e-23): an Offset result has an outer
     ring with a negative area of -1.7e-16 (5 vertices, one spike vertex at 1.2e-4 beside vertices at 1e-12) and another of -2.6e-25; the grid orientation
     is positive. Not analysed further. The pilot's `OrientationSlack` no longer flags the second one (area below the slack) and still flags the first
     (artifacts: `crash-bf45adc8`, `crash-d4e84c7e` of the 2026-11-11 campaign; the check is relaxed, not the cause understood).
   - Checked, not a bug (2026-11-11; the third review of #583 had reported it as one): a single tree ring that touches itself at a repeated vertex, with a
     clockwise lobe, is converted as one polygon whose net area is the signed sum (A - |B|). Splitting such rings at their repeated vertices into simple
     cycles (the stack method, each cycle classified by its own sign and containment) was implemented and measured against an independent sampled
     winding truth on 64 000 results (random rings on a 0..4 lattice, Union/Intersection/Difference/Xor, NonZero and EvenOdd): it changed 13 results and
     all 13 got worse (e.g. Xor NonZero 4.848 -> 5.348 against a truth of 4.83), none better; the review's hand-built example
     (`Grid(2,1, 1,3, 1,1, 0,3, 1,3, 0,4, 0,0)`) has the clockwise lobe inside the other, so it is a hole and 3.5 is right for that tree (the 5.5 expected
     there came from the inputs, where Clipper2's own output is wrong). The signed sum stays; the split was not merged. On the same data #583 changes
     none of the 64 000 results (it acts on the rarer degenerate rings the fuzzer found); 1.7% of the results of both versions differ from the sampled
     truth by more than 0.25 (Clipper2 itself or the sampling; not analysed).
   - **Clipper2 2.0.0 itself (open, 16 crashes; not reported upstream, owner decision 2026-11-11: the C# upstream is archived, see below):** `Clipper.Union` of two self-intersecting rings (NonZero) returns a region that
     neither input fills (inclusion-exclusion off by 10 percent, the winding of the union's flat paths is 1 where each input's is 0). No adapter code is
     involved (the flat paths of a raw `Clipper64.Execute` show it). Input (doubles, A then B):
     A = (0,0) (0.125,0) (0.125,0.125) (0,0.125) (0.5703125,0.5703125) (0.6953125,0.5703125) (0.125,0.125) (0,0.125);
     B = (0.33984375,0.08984375) (-0.58984375,-0.58984375) (0.22265625,0.21484375) (0.48828125,-0.02734375) (0.21484375,0.08984375) (0.78515625,0.66015625);
     `Clipper2Polygons.BooleanPaths(ClipType.Union, {A}, {B})`, probe point (0.25463542, 0.20286458).
     **Measured (2026-11-11), the class does not depend on `PrecisionBits` and needs exact coincidences, not grid size:**
     - The repro alone: consistent at `PrecisionBits` 24, 32, 36 and 44, inconsistent at 38 and 40 (the default): a coincidence of this one case.
     - Random pairs of self-intersecting rings (seeded, `A|B` against `A + B - A&B` on the flat Clipper2 paths; failures per 3000 pairs):

       | PrecisionBits | 24 | 32 | 36 | 38 | 40 | 44 | 48 |
       |---|---|---|---|---|---|---|---|
       | 8-gons, coordinates k/64 in [-1,1] | 2 | 1 | 1 | 1 | 1 | 2 | 1 |
       | 16-gons, coordinates k/64 in [-1,1] | 1 | 2 | 2 | 1 | 1 | 2 | 1 |

       The failing pairs are mostly the same at every precision (e.g. cases 459, 1700, 2038, 2354, 2877 of one 16-gon seed fail at all of 24 to 50).
       Lowering the default `PrecisionBits` does not avoid the class.
     - By coordinate step (16-gons, 6000 pairs, `PrecisionBits` 24 / 40): step 1/64: 3 / 4 failures; 1/1024, 1e-6 and 1e-9: 0 / 0. With generic
       coordinates (no vertex on another edge, no collinear overlap) no failure was seen.
     - The repro translated (dyadic or random) or scaled by 2^k still fails (200/200 translated, 154/200 scaled, `PrecisionBits` 40), perturbed by 1e-9 or
       1e-3 it never fails (0/200 each): it needs the exact degeneracy (vertices on edges, collinear overlaps), not a grid position.
     - Clipper2's documentation (angusj.com) could not be read from this environment (the host is not reachable). The C# source (`Clipper.Core.cs`) computes
       `CrossProduct` and `GetLineIntersectPt` in double ("typecast to double to avoid potential int overflow") and has exact 128-bit paths only for
       `ProductsAreEqual` / `CrossProductSign`; the source states no precision limit for self-touching input. Not verified against the 2.0.0 tag.
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

## T13 libFuzzer campaign (2026-11-11): 17 targets, 10 minutes each, 2 fork jobs

`run.sh <new-dir> libfuzzer-dotnet sharpfuzz dotnet 100000000 600 2` with `MESH_FUZZ_OPS=<target>`, one target per run, on the tree of master at #583
(the quality-mesh2 and clipper2 runs on the trees just before). Replay: `MESH_FUZZ_OPS=<target> dotnet MeshFuzzPilot.dll replay <file>`; the hex below is the
smallest artifact of each message class (write it with `python3 -c "open('x.bin','wb').write(bytes.fromhex('...'))"`). The campaign fixed nothing except the
Clipper2 result classification (#583, item 5 above).

| target | crashes | executions | classes (rank in the list below) |
|---|---|---|---|
| quality-mesh2 | 1 | 89 000 | rank 7 |
| clipper2 | 70 | 1 321 000 | item 5 above |
| alpha-wrap | 0 | 299 000 | none |
| remesher | 0 | 314 000 | none |
| reducer | 0 | 18 600 (slow: 7 exec/s) | none, weak evidence |
| partitioned-reducer | 17 | 13 600 | rank 6 |
| union, intersection, difference | 24, 12, 20 | 17 700, 17 400, 25 400 | rank 2 |
| robust-repair | 0 crashes, 6 slow inputs | 4 300 | rank 8 |
| robust-repair-soup | 1 crash, 1 timeout (the same throw after 146 s), 1 slow input | 2 300 | rank 3 |
| rounding-repair | 41 | 6 600 | rank 1 |
| tree-nearest | 997 | 1 626 000 | rank 5 |
| tree-ray | 0 | 950 000 | none |
| tree-inside | 1 | 325 000 | rank 5 |
| batch-queries | 0 | 2 300 (slow: 3 exec/s) | none, weak evidence |
| tree-winding | 0 | 3 685 000 | none |

Ranked (a wrong result that claims to be right first):

1. **MeshRoundingRepair: results that claim to be clean are not** (41 crashes; #513 (D1, head b26f016d) replayed: all 41 behave exactly as on master).
   - "the mesh is not valid after the rounding repair" (21), 8 bytes: `000000101e000105`.
   - "IsClean but N degenerate triangles (at the rounding's scale)" (20, N from 1 to 8), 4 bytes: `3f1e6040` (an open cube of edge 5e-293 and a tetrahedron of 3.6e-307).
2. **ExactMeshBoolean with `RoundingClean`: results that claim to be clean are not** (union 24, intersection 12, difference 20).
   - "RoundingClean but N degenerate triangles (at the rounding's scale)": union 32 bytes `0000000000000000000203000203fe03defc0000010000000000008500000302`; intersection 16 bytes `00000000000000000000000103fe0300`; difference 28 bytes `0001000000000107fefe0503030300feffffffffbfbf050000002d01`.
   - "RoundingClean but inside-out components" (intersection, 5 artifacts), 83 bytes: `000203fe030104030501070207040a020e030a0203fe030104030501050f0315040d0614041c0510071905230613081e062a0716092307ab4a123a00000000000102000000010403050107022e2e2e07040a02`.
   - "RoundingClean but self-intersections" (difference, 1), 139 bytes: `00fffffffffe0000000000000000000000000000000000000000000103fe030002030000000103fe03000203fe03fe04030200000000002c0505fe0000004b0000fe0000ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff00000000050000002c0505fe0000004b050505050505050505050505050505050505`.
3. **DualContouring.find_crossings throws on finite input** (robust-repair-soup, 2 artifacts): `DMeshAABBTree3.FindNearestHitTriangle: ray direction is not normalized`,
   an `AggregateException` from the parallel loop at `DualContouring.cs:1122`. The crash input (79 bytes): `000103fe03fe0004034848484848484848484848484848484848484848484848480000ffffffff000000000000abffff00000000000002fe03fe000000000000000000000000000002fe03fe000300`; the timeout input (51 bytes, 146 s before the same throw): `000103fe15001103fe03fe0000000000000000000000000000000100000000040d061403fe0614030000000000010000000004`.
4. **Clipper2 2.0.0 itself** (item 5): a Union of degenerate self-intersecting rings returns an area that neither input fills; independent of `PrecisionBits`;
   not reported upstream (owner 2026-11-11): AngusJohnson/Clipper2 was archived on 2026-10-28 and its C# port is no longer
   developed; Clipper2 continues as a C++ only library at PolyClipping/Clipper2 (maintainer Seth Hillibrand). Whether to
   integrate the C# sources and fix it ourselves is an open roadmap item ([platform.md](../../docs/roadmap/platform.md#open)).
5. **AABB tree queries at tiny coordinates** (tree-nearest 997 crashes: the nearest triangle is not the nearest, e.g. at squared distance 4.5e-277 where the minimum is 0, or the brute force minimum is NaN;
   tree-inside 1: "2 crossings, brute force 0" at raw scale). Smallest tree-nearest, 20 bytes: `000503fe0700fe0000000000000000ffffffff05` (a cube of edge 2.4e-35 with a vertex at 7.6e-321 and one at -3.2e-173).
   Cause found and fixed in #586: not the tree but the point-triangle distance arithmetic (`a00`, `a11` and `det` leave the accurate range for tiny or sliver triangles). The "(raw scale)" messages with squared distance 4.5e-277 against minimum 0 are a false positive of the pilot (exact equality on the normalised copy); the raw tree answer is right. The tree-inside artifact (133 bytes): `000403fe030100080000ffffffff3f03fe03fe00010000000000000002000000000000000000000000010000800000000000ffffff3f03fe03fe0001000000000000000203fe03fe0203040000005400000000000000000300000000fe03fe0203040000000000000000000000020300000a0403ff240f0000000104030500000015000000`.
6. **PartitionedReducer: the result for the copy scaled by 2^k is not the scaled result** (17), 8 bytes: `0001f74800010348`.
   **Resolved in part, the rest is a limit of the arithmetic** (replay of the 17 saved artifacts on master with #595, #614: 12 pass, including the 8-byte one; 5 differ: `04782003` 99 bytes, `06c064ac` 149, `21a71a03` 162, `29bdf55d` 81, `cf702744` 100).
   - Not an oracle exactness problem: `MakeSetup` already drops an inexact copy, and the reducer runs on the power-of-two normalized mesh (box diagonal in [1, 2)), whose 2^40 copy is exact.
   - Common property of all 17 (passing and failing): a nonzero coordinate of 1e-170 to 1e-307 times the extent; the 5 that still differ have one at 1e-280 to 1e-290. Example `29bdf55d`: a tetrahedron of edge 2.4e-35 and a vertex at (1e-173, 5e-217, 1.3e-308) next to the origin; `04782003`: vertices on z = 7.6e-270 of a mesh of extent 2.4e-35 and one at (1.9e-260, 3.9e-312, 0).
   - Not the partition (`PartitionedReduction.round` is scale free: quantile cuts, a gap compare). The serial `Reducer` on the same subdivided mesh gives the scaled result at 2^40 in 3 of 4 probed cases while the partitioned result differs in size (8 against 24 triangles, 18 against 36); in `04782003` the serial `Reducer` differs too (25 against 5 triangles), so that case is a serial facet.
   - Measured range of the scale claim of the `Reducer` (a wobbly 10 x 10 grid, `ReduceToTriangleCount(40)`, unit size against the 2^k copy): bit for bit for k = -500 .. 900; for k <= -600 the same vertex positions but another triangle list (another collapse order, ties in the queue); for k = 1000 another topology (30 against 29 vertices).
   - Cause (hypothesis that fits the data): products and squares of components of 1e-170 and smaller leave the normal doubles in one copy and are subnormal or zero in the other (a component of 1e-172 squared is 1e-344, zero; in the 2^40 copy 1e-160 squared is 1e-320, subnormal and not zero), so the quadrics and the flip decisions differ. The frame of the quadrics (`quadricScale` from the median edge) cannot help: the tiny components are far below the median.
   - Pilot: the scale claim now has a domain (`HasTinyComponents`): a normal-size mesh with a nonzero coordinate or edge difference below 2^-200 is out (the quadric terms are products of up to four components, a squared area; 2^-200 to the 4th is 2^-800, inside the normal doubles in both copies; all 17 artifacts are below it, the failures start at 1e-280 = 2^-930). The other claims (valid, closed, compact, threads and runs bit for bit) are judged for those meshes as before. Regression: the self-test runs all 17 saved artifacts (as hex, 1.6 kB; the whole self-test takes 5 s) and two of them as meshes (`29bdf55d`, `04782003`) through the whole target.
   - **Open (b)**: per-triangle scale-safe arithmetic in `Reducer.tri_info` / `plane_quadric` (as `flip_edge_scale` does for the flip test, #614) would extend the range to meshes with such components; a library change, with the owner.
7. **QualityMesh2: `Failed (RefinementLimit)` on a valid input** (1): the defect cap of the refinement loop (`steps > 64 * (limit + slots) + 100000`, "refinement did not settle within 296225 steps"),
   a livelock on a point 1e-286 of the extent away from the others (a cube projected to xy, MinAngle 25, SteinerLimit 1500), 144 bytes: `00020303fe03fe00dcf9ecf70000000000000000000000000000000000000000000010000000000000000000000000f8fffc00000000003100000000000000000000000000000102000000010403050107001007e7fadcf9ecf7e1f50000000000c2dcf90200000000000000000000000000000000000007040a020e030a050f031504e1f52aff13092307324a123a16`.
8. **MeshRobustRepair slow inputs** (robust-repair, 6; 2.7 to 10.8 s on this machine, over the 5 s target, no wrong result). The slowest, 62 bytes (10.8 s): `000103fe03fe00000000000000000000000000000011002000002c0000000000100000000000000000000000000002030203fe03000000000300002c0000`.
9. **`DMesh3.CheckValidity` reports a valid mesh as invalid from a vertex magnitude of about 1e154** (found while pinning the Reducer range, #595): `mesh/DMesh3_debug.cs:259-260` requires `v.LengthSquared` to be finite per vertex, which overflows for a vertex with all three components above about 7.7e153, or a single component above 1.34e154 (`sqrt(double.MaxValue)`). Repro: `new Sphere3Generator_NormalizedCube() { Radius = 1, EdgeVertices = 10 }.Generate().MakeDMesh()` with every vertex multiplied by 1e160: `CheckValidity(false, FailMode.ReturnOnly)` is false for the unreduced input (true at 1e150). Not a mesh-operation bug: the operations that work at these sizes (Reducer, since #595, up to 1e300) return meshes that this check then rejects, so tests of the range cannot use it above 1e150. **Fixed** by comparing the components (`CheckValidityRangeTests`; the lines were 259-260). The same `IsNaN/IsInfinity(v.LengthSquared)` pattern in `mesh/NTMesh3.cs`, `curve/DGraph3.cs` and `curve/DGraph2.cs` is **fixed** the same way (`NTMesh3ValidityRangeTests`, `DGraph3ValidityRangeTests`, `DGraph2ValidityRangeTests`); the two commented-out lines in `curve/DGraph.cs:586-587` are dead code and stay.
10. **The flip test of the edge collapse accepts a collapse when its normals overflow to NaN** (`MeshRefinerBase.edge_flip_metric`, found in the second review of #595): above an edge length of about 1e154 the cross products of the face normals overflow; a component that is infinite refuses the collapse, but inf - inf is NaN, `NaN <= tol` is false and the flip is accepted blindly. Refusing a NaN metric is safer, but two existing tests pin the reduction at those sizes: `RobustnessSweepRemainingTests.Reducer_ExtremeMidpointsRemainFinite` (1e308, 1.7e308) and `ReducerFoldOverOptionsTests.HugeCoordinates_NoReference_SameReductionAsWithoutTheCheck` (2^540); a change has to rescale the normals (the edges are the better input: the cross product of unit-sized edges) rather than refuse. **Fixed**: the edges of a triangle are scaled by one factor (the reciprocal of the longest component) outside [1e-70, 1e70] before the cross products, for the old and the new normal (`FlipTestHugeScaleTests`); the two pinning tests needed no change (the sphere still reduces at 1e308 and 2^540); the same scaling also removes the lower limit of #595 (a tiny sphere reduces down to 1e-300).

Not found in 10 minutes: alpha-wrap, remesher, tree-ray, tree-winding. reducer and batch-queries ran only 18 600 and 2 300 executions (7 and 3 per second): a longer run is needed before they count as clean.
## Judgement notes

- **The tree and the brute force use different distance code.** `DMeshAABBTree3.FindNearestTriangle` computes the distance of a triangle with
  `DistPoint3Triangle3`, the pilot's brute force with the inline copy in `MeshQueries.TriDistanceSqr`. Both are legitimate and differ by rounding
  (about one ulp, or 1e-17 where the minimum is 0 on a mesh of scale 1). Unifying them would change the bits of `TriDistanceSqr` results for no gain in
  correctness, so the pilot compares up to `4 * eps * (meshDiagonal^2 + distance)` (`NearestTolerance`): an absolute 1e-17 against 0 passes only
  relative to the mesh scale. Measured on the 997 `tree-nearest` artifacts of the T13 campaign: 99 reports before, 0 after; a mutant that returns
  the wrong triangle is still caught (43 of the first 60 artifacts at k = 4; the rest have one triangle or equally near ones).
