# Coverage-guided fuzzing of the mesh operations (pilot)

This test-only pilot runs the mesh operations under SharpFuzz and libFuzzer. It is like
[ReaderFuzzPilot](../ReaderFuzzPilot/README.md), but the input bytes describe meshes rather than a file.
No mesh files, corpus files or instrumented binaries are committed (owner decision 2026-11-09; Exact Boolean 2.0).

## What an input means

`Program.Decode` reads an operation and two small closed meshes A and B (zero bytes past the end of the input).

| Bytes | Meaning |
|---|---|
| 1 | the operation, a number n selecting `n % (number of selected operations)` of: `union`, `intersection`, `difference` (ExactMeshBoolean A op B), `robust-repair` (MeshRobustRepair of A), `robust-repair-soup` (of A and B appended), `rounding-repair` (MeshRoundingRepair of A and B appended), `tree-nearest`, `tree-ray`, `tree-inside`, `tree-winding` (DMeshAABBTree3 queries on A against brute force), `batch-queries` (MeshBatchQueries on A against the single calls), `alpha-wrap` (MeshAlphaWrap of A and B appended), `partitioned-reducer` (PartitionedReducer on A subdivided three times, 256 to 1536 triangles), and the second wave (QA 2.0 T13): `remesher`, `reducer`, `quality-mesh2`, `clipper2` (below) |
| per mesh: 1 | a base shape: tetrahedron, octahedron, cube (closed, consistently oriented), two disjoint tetrahedra (two components), a cube without its bottom face (open), two cubes shifted by half an edge (closed, self-intersecting) |
| 2 + 2 | the exponents e and f of a scale 2^e and of an offset unit 2^f: each a big-endian 16-bit number n, e = n % 2046 - 1022, in [-1022, 1023] |
| 3 | the offset, three signed bytes times 2^f |
| per corner: 1 + 3, or 1 + 24 | a mode byte. Grid mode: the corner times 16 plus three signed bytes (used if bit 0 is set), times 2^e, plus the offset. Raw mode (top three bits set): three raw doubles |
| 8 times 6 | the queries of the tree and batch targets: an origin kind (a vertex of A or B, a triangle centroid, an edge midpoint, a point of A's box, a vertex nudged by 1/1024 of the diagonal, the box centre), three index bytes, a direction kind (the six axes, towards a vertex of A, three signed bytes) and a byte for it |

So the inputs reach coplanar, coincident and collinear corners, inverted and self-intersecting shapes, and every
binary scale up to near 1e308 (grid corners are multiples of 2^-1022; subnormal ones come from raw mode). A coordinate that is not finite becomes 0: the operations reject such
input by contract (`InvalidInput`), which is not a finding.

`MESH_FUZZ_OPS` (an environment variable, a comma separated list of operation names or numbers) restricts a
campaign to those operations: the first byte then selects among them. **A replay needs the same value** (the
artifacts of a campaign are only meaningful with the filter it ran with); the seeds are written for the selected
operations only. `dump <input>` prints the decoded case with round-trip coordinates, for the procedural regression test.

## What counts as a finding

An exception (none is documented for finite input), a timeout (libFuzzer's `-timeout`), or a result that
contradicts what the operation says about it (`CheckBoolean`, `CheckRepair`, `CheckRounding` in `Program.cs`). The
checks judge a copy scaled by a power of two so that its largest component's diagonal is in [1, 2) (exact unless
coordinates become subnormal): the thresholds of 1e-12 times a squared diagonal underflow below about 2^-517 and
would see nothing there. Degenerate triangles are judged as the rounding judges them: area below 1e-12 times the
squared diagonal of the largest component, or exactly collinear corners.

- ExactMeshBoolean (the inputs are always closed): `Compute()` disagrees with `Status`; `Success` without a
  `Result`; a result vertex that is not finite; a non-empty result that is not closed or has bowties (documented:
  closed and manifold for closed inputs); `NonManifoldTriangles` or `ConflictEdges` not 0 (documented 0 for closed
  inputs); with `RoundingClean`, any defect of `MeshRobustRepair.FindDefects` (self-intersections not checked when
  the result has `Contacts`, which that check counts) or a degenerate triangle.
- MeshRobustRepair: `Apply()` true with `ResultReason` "" while the result has a vertex that is not finite or
  `NotCleanReason` is not empty.
- MeshRoundingRepair: `Apply()` disagrees with `IsClean`; a vertex that is not finite; a mesh that is not valid
  afterwards, or not closed for closed input; with `IsClean`, an exact self-intersection, a degenerate triangle, or
  `MaxDrift` above `MaxLength`.
- ExactMeshBoolean on an input with a boundary edge (allowed by default: nothing is promised about the result's
  shape): with `AllowOpenMeshes = false` the status is `OpenInput` exactly for such input.
- `tree-nearest`: `FindNearestTriangle` returns a triangle at the brute force minimum squared distance, bit for bit
  (`MeshQueries.TriDistanceSqr`), and reports that distance; the same for the raw tree where the squared distances are
  representable (diagonal within 2^+-450); `FindNearestVertex` likewise.
- `tree-ray`: `FindNearestHitTriangle` is the brute force nearest hit (smallest ray parameter, the lowest id among
  equal ones) and `FindAllHitTriangles` the set of hit triangles, on the normalized mesh and on the raw one. A query with
  a hit within 2^-30 of the triangle's plane (or in it) is judged by its set of hits and the existence and validity of
  the nearest, because its ray parameter is a quotient of rounding errors.
- `tree-inside`: the tree's exact crossing count of the perturbed ray (`IsInside`'s parity) is the brute force count
  (`ExactRayTriangle.RayHitPerturbed` over all triangles), normalized and raw; for a mesh without defects and a point
  off its surface also the sign of the winding number.
- `tree-winding`: `WindingNumber` is finite, agrees with the brute force sum of solid angles (1e-6) and is the same
  at the raw scale.
- `batch-queries`: every result of `IntersectBatch`, `AnyHitBatch`, `NearestPointBatch` and `IsInsideBatch` (both
  methods) is the single call's, bit for bit, with and without sorting and threads, with and without a distance limit,
  with queries that are not normalized or not finite among them (1030 queries, so that the sorted and the parallel path run).
- `alpha-wrap` (a coarse wrap, relative alpha 5, at most 3000 Steiner points): `Compute()` agrees with `Status`;
  `EmptyInput`, `InvalidParameters` (for an input of positive size), `Failed` and `Cancelled` never happen;
  `InvalidInput` only for input that is not finite or whose box does not fit; a `Success` or `PointLimitReached`
  result is finite, valid, closed, without bowties, and every input vertex is inside it.

**Second wave (QA 2.0 T13).** The four targets run on the power-of-two normalized copy of A (box diagonal in [1, 2)); their options come
from the three components of the first query's direction (bytes 0, 1, 2).
- `remesher`: A subdivided once, three passes with a target edge length of 0.45 to 1.6 mean edges; flips, collapses, splits, smoothing,
  parallel smoothing and `PreventNormalFlips` on or off. Valid, finite, a closed input stays closed with the same Euler characteristic and
  number of components, parallel and serial smoothing and a second run equal bit for bit.
- `reducer`: A subdivided twice, `ReduceToTriangleCount` to 25 / 50 / 75 %; quadric or midpoint position, area weights, `PreventFoldOver`
  and a `MaxDeviation` of 0.02 diagonals. Valid, finite, no larger than the input, a closed input stays closed with the same topology, every
  vertex within `MaxDeviation` of the input, a second run and the copy scaled by 2^40 equal bit for bit.
- `quality-mesh2`: the projection of A to xy, its corners as points and its triangle edges as segments (they cross), no minimum angle or
  20 / 25 degrees, constrained or conforming mode, Steiner limit 1500. The status is not Failed and the acceptance oracle of the mesher
  (milestone M3) reports no violation. Domain: coordinates at most 2^16 (extents) from the origin and at most 1e70 (the mesher's contract).
- `clipper2`: `BooleanPaths` of the rings of A's and B's corners (in index order, so simple, self-intersecting or degenerate; B moved onto
  A) with one of the four operations, then `Offset` (round or miter, both signs) and `Simplify` of the union. The status is Success, vertices
  finite, outer rings counterclockwise and holes clockwise, and on a 12 x 12 grid of points beyond four grid resolutions from every ring the
  result contains the point exactly if the operation on the windings of the rings does. Domain: coordinates at most 2^16 extents from the origin
  (the grid of the adapter, 2^-40 of the extent, must be representable at the coordinates' magnitude; beyond that, rings collapse when rounded to doubles).

`MeshFuzzPilot random <n> <seed>` runs n pseudo-random inputs of the selected operations in one process with a 60 s watchdog and prints the hex
of every input that gives a finding or an exception: a smoke test without coverage guidance and without libFuzzer, for a first look at a target
(`MESH_FUZZ_TRACE=1` prints some internals of the 2D mesher and Clipper2 targets).

**Range domains (QA.md 2.5a, 2026-11-10).** The decoder takes the scale 2^e and the offset exponent 2^f three times in four inside
+-150 (one in eight of those at the edges, +-100 .. +-150) and one time in four anywhere in [-1022, 1023]. Inside a module's
supported range every claim applies. Outside it only the second property is judged, by the operation's own range (the table
in QA.md 2.5a): `robust-repair` (outside [2^-200, 2^400] or an extent below 2^-40 of the largest magnitude) must return false,
`alpha-wrap` (outside [2^-128, 2^128], or an extent below
2^-40 of the largest magnitude) must return `InvalidInput`; `rounding-repair` and the Boolean operations are judged as always (the rounding repair's range check follows in its own PR) (the exact arrangement has
no range; `RoundingClean` must not be claimed where the rounding result is not clean). Inputs recorded before this change decode
to other cases: the hex reproducers of FINDINGS.md belong to the earlier decoder (`dump` of an old file shows what it was).

`tree-ray` at a raw diagonal beyond about 2^110 needs DMeshAABBTree3 to take double.MaxValue as no limit (it took float.MaxValue until the fix of the ray-queries PR). The query targets judge a power-of-two normalized copy of the whole mesh (their domain: coordinates up to 2^1000,
an exact copy, no triangle below 2^-50 of the mesh, queries within 2^20 diagonals of the mesh; for `tree-ray` also no
sliver below 2^-30 of the longest edge squared: its ray parameter is not comparable with the boxes). The raw tree is
compared with the brute force at its own scale.

The self-test hands every check a contradiction and requires a finding.

A finding is reduced and becomes a procedural regression test in its own `bugfix` PR (use `dump` for the numbers).
Inputs are not committed.

Triage of a campaign: replay every artifact (same `MESH_FUZZ_OPS`), group the messages by their text with numbers
removed, and take the smallest artifact of each group. A group that is a limit of the representation and not a
defect (a sliver's ray parameter, a query 1e280 diagonals away) becomes a narrower domain of the claim in `Targets.cs`
with a comment, not a suppressed finding.

## Setup and a bounded run

The same tools as ReaderFuzzPilot: .NET 10, [SharpFuzz.CommandLine 2.2.0](https://www.nuget.org/packages/SharpFuzz.CommandLine/2.2.0)
(MIT) and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet/releases/tag/v2025.05.02.0904) (MIT;
libFuzzer is Apache-2.0 with LLVM exceptions); see that README for the install and the checksum.

```sh
bash tools/MeshFuzzPilot/run.sh /path/to/new-work-dir /path/to/libfuzzer-dotnet /path/to/sharpfuzz-tools/sharpfuzz /path/to/dotnet 1000000 1800 [jobs]
```

`run.sh` refuses an existing work directory. It publishes a clean `replay/` build, runs its self-test, writes the
102 seeds (every operation on each of the six shapes), copies the build into `bin/` and instruments only the copy of
`geometry3Sharp.dll`. The run uses a fixed seed, at most 512 bytes per input, a 30 s timeout per input (an alpha
wrap of a flat input with the default alpha takes about 4 s uninstrumented and more than 10 s instrumented: slow,
not hung), a libFuzzer-side 3 GiB RSS limit (it covers the libFuzzer process, not the .NET child: use an external limit for
the child if needed), and stops after the given number of runs or seconds, or at the first
finding. With `jobs` > 0 it runs libFuzzer's fork mode with that many workers, which goes on after a crash or a
timeout and keeps every artifact.
A run is slow by fuzzing standards (a few inputs per second: every input runs exact arithmetic), so campaigns
belong on a runner or the owner's machine, not in CI.

Replay an artifact with the **unmodified** build:

```sh
/path/to/dotnet /path/to/new-work-dir/replay/MeshFuzzPilot.dll replay /path/to/new-work-dir/artifacts/crash-...
```

It prints the decoded case and throws the finding again. libFuzzer also keeps `slow-unit-*` inputs (above its
slow-unit threshold): replay them with a stopwatch; one above the 5 s per-operation target is a performance
finding, not a crash.

## Validation

```sh
dotnet build tools/MeshFuzzPilot -c Release
MESH_FUZZ_DLL="$PWD/tools/MeshFuzzPilot/bin/Release/net10.0/MeshFuzzPilot.dll" DOTNET_HOST_PATH="$(command -v dotnet)" python3 -m unittest discover -s tools/MeshFuzzPilot -p 'test_*.py' -v
```

The checks run in CI without a fuzzing campaign: the self-test (a deterministic decoder with finite closed shapes
also for extreme bytes, seeds that cover every operation and run without a finding, and every check firing on a
contradiction), the seed writer (refuses a directory that is not empty), the replay of every seed and of short
inputs for each operation, each with a time limit. The library's `InternalsVisibleTo` lists this tool: the oracles
use `ExactRayTriangle` and `DMeshAABBTree3.CountCrossings`.
