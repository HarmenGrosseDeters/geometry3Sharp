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
   - Open, pre-existing (found by the third review of #583, not by the campaign; not a regression of the conversion): a single ring of the
     tree that touches itself at a repeated vertex and has lobes of opposite winding is converted as one polygon, so its net area is the signed sum
     (A - |B|) where the filled region (NonZero) is A + |B|. Hand-built tree, frame scale 1: `Grid(2,1, 1,3, 1,1, 0,3, 1,3, 0,4, 0,0)` (lobes 4.5 and
     -1; the conversion gives 3.5, the filled region is 5.5). Through Clipper2: Difference, NonZero, subject (4,2) (0,0) (0,4), clip (0,3) (1,1)
     (1,4) (4,0) (4,4). About 25 results with `Outer.SignedArea <= 0` and about 230 mixed-lobe rings in 48 000 random degenerate lattice combinations (0..4,
     all four operations and fill rules). Part of the gap in such cases is Clipper2's own error. A fix would split every ring at its repeated vertices
     into simple cycles and orient and classify each by its own sign; not done here.
   - **Clipper2 2.0.0 itself (open, 16 crashes, not reported upstream):** `Clipper.Union` of two self-intersecting rings (NonZero) returns a region that
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

`run.sh <new-dir> libfuzzer-dotnet sharpfuzz dotnet 100000000 600 2` with `MESH_FUZZ_OPS=<target>`, one target per run, the tree of master at #583 (the
quality-mesh2 and clipper2 runs before the merge of #583). Replay: `MESH_FUZZ_OPS=<target> dotnet MeshFuzzPilot.dll replay <file>`; the hex below is the
smallest artifact of each message class (write it with `python3 -c "open('x.bin','wb').write(bytes.fromhex('...'))"`). No code was fixed by this campaign
except the Clipper2 result classification (#583, item 5 above).

| target | crashes | classes (smallest repro) |
|---|---|---|
| quality-mesh2 | 1 | see 1.7 |
| clipper2 | 70 | item 5 above (fixed in #583 except the Clipper2-internal class and 2 Offset cases) |
| alpha-wrap, remesher, reducer, tree-ray, batch-queries, tree-winding | 0 | none in 10 minutes (420 000 to 3.7 million executions) |
| partitioned-reducer | 17 | 1.6 |
| union, intersection, difference | 24, 12, 20 | 1.2 |
| robust-repair | 0 crashes, 6 slow inputs | 1.8 |
| robust-repair-soup | 1 crash, 1 timeout (the same throw after 146 s), 1 slow input | 1.3 |
| rounding-repair | 41 | 1.1 |
| tree-nearest | 997 | 1.5 |
| tree-inside | 1 | 1.5 |

Ranked (by the risk of a wrong result that claims to be right first):

1. **MeshRoundingRepair: results that claim to be clean are not** (41 crashes; `#513` (D1, head b26f016d) replayed: all 41 behave exactly as on master).
   - "the mesh is not valid after the rounding repair" (21), 8 bytes: `000000101e000105`.
   - "IsClean but N degenerate triangles (at the rounding's scale)" (20, N from 1 to 8), 4 bytes: `3f1e6040` (an open cube of edge 5e-293 and a tetrahedron of 3.6e-307).
2. **ExactMeshBoolean with `RoundingClean`: results that claim to be clean are not** (union 24, intersection 12, difference 20).
   - "RoundingClean but N degenerate triangles (at the rounding's scale)" (union 32 bytes `0000000000000000000203000203fe03defc0000010000000000008500000302`; intersection 16 bytes `00000000000000000000000103fe0300`; difference 28 bytes `0001000000000107fefe0503030300feffffffffbfbf050000002d01`).
   - "RoundingClean but inside-out components" (intersection, 5 artifacts; 83 bytes `000203fe030104030501070207040a020e030a0203fe030104030501050f0315040d0614041c0510071905230613081e062a0716092307ab4a123a00000000000102000000010403050107022e2e2e0704`).
   - "RoundingClean but self-intersections" (difference, 1; 139 bytes `00fffffffffe0000000000000000000000000000000000000000000103fe030002030000000103fe03000203fe03fe04030200000000002c0505fe0000004b0000fe0000ffffffffffffffffffffffffffffff`).
3. **DualContouring.find_crossings throws on finite input** (robust-repair-soup, 2 artifacts: `DMeshAABBTree3.FindNearestHitTriangle: ray direction is not normalized`, an `AggregateException` from the parallel loop at `DualContouring.cs:1122`). 79 bytes `000103fe03fe0004034848484848484848484848484848484848484848484848480000ffffffff0000000`... (the file `/tmp` copy is gone after the session; replay with the artifact of the campaign, or the 51-byte timeout input that takes 146 s before the same throw).
4. **Clipper2 2.0.0 itself** (item 5): a Union of degenerate self-intersecting rings returns area neither input fills; independent of `PrecisionBits`; owner decision whether to report upstream.
5. **AABB tree queries at tiny coordinates** (tree-nearest 997 crashes: the nearest triangle is not the nearest, e.g. at squared distance 4.5e-277 where the minimum is 0, or the brute force minimum is NaN; tree-inside 1: "2 crossings, brute force 0" at raw scale). Smallest tree-nearest, 20 bytes: `000503fe0700fe0000000000000000ffffffff05` (a cube of edge 2.4e-35 with one vertex at 7.6e-321 and one at -3.2e-173). Looks like absolute tolerances in the tree search at coordinates near 1e-35; not analysed.
6. **PartitionedReducer: the result for the copy scaled by 2^k is not the scaled result** (17), 8 bytes: `0001f74800010348`.
7. **QualityMesh2: `Failed (RefinementLimit)` on a valid input** (1): the defect cap of the refinement loop (`steps > 64 * (limit + slots) + 100000`, "refinement did not settle within 296225 steps"), a livelock on a point 1e-286 of the extent away from another. 144 bytes `00020303fe03fe00dcf9ecf70000000000000000000000000000000000000000000010000000000000000000000000f8fffc00000000003100000000000000000000000000000102000000010403050107001007`.
8. **MeshRobustRepair slow inputs** (robust-repair, 6; 2.7 to 10.8 s; over the 5 s target, no wrong result): `3f2260a4...` family; the slowest 62 bytes (10.8 s).
