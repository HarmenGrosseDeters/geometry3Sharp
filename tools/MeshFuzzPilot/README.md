# Coverage-guided fuzzing of the mesh operations (pilot)

This test-only pilot runs the mesh operations under SharpFuzz and libFuzzer. It is like
[ReaderFuzzPilot](../ReaderFuzzPilot/README.md), but the input bytes describe meshes rather than a file.
No mesh files, corpus files or instrumented binaries are committed (owner decision 2026-11-09; Exact Boolean 2.0).

## What an input means

`Program.Decode` reads an operation and two small closed meshes A and B (zero bytes past the end of the input).

| Bytes | Meaning |
|---|---|
| 1 | the operation: `union`, `intersection`, `difference` (ExactMeshBoolean A op B), `robust-repair` (MeshRobustRepair of A), `robust-repair-soup` (of A and B appended), `rounding-repair` (MeshRoundingRepair of A and B appended) |
| per mesh: 1 | a base shape: tetrahedron, octahedron or cube, closed and consistently oriented |
| 2 + 2 | the exponents e and f of a scale 2^e and of an offset unit 2^f: each a big-endian 16-bit number n, e = n % 2046 - 1022, in [-1022, 1023] |
| 3 | the offset, three signed bytes times 2^f |
| per corner: 1 + 3, or 1 + 24 | a mode byte. Grid mode: the corner times 16 plus three signed bytes (used if bit 0 is set), times 2^e, plus the offset. Raw mode (top three bits set): three raw doubles |

So the inputs reach coplanar, coincident and collinear corners, inverted and self-intersecting shapes, and every
binary scale up to near 1e308 (grid corners are multiples of 2^-1022; subnormal ones come from raw mode). A coordinate that is not finite becomes 0: the operations reject such
input by contract (`InvalidInput`), which is not a finding.

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
- MeshRoundingRepair (on the two closed shapes): `Apply()` disagrees with `IsClean`; a vertex that is not finite; a
  mesh that is not valid or not closed afterwards; with `IsClean`, an exact self-intersection, a degenerate
  triangle, or `MaxDrift` above `MaxLength`.

The self-test hands every check a contradiction and requires a finding.

A finding is reduced and becomes a procedural regression test in its own `bugfix` PR. Inputs are not committed.

## Setup and a bounded run

The same tools as ReaderFuzzPilot: .NET 10, [SharpFuzz.CommandLine 2.2.0](https://www.nuget.org/packages/SharpFuzz.CommandLine/2.2.0)
(MIT) and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet/releases/tag/v2025.05.02.0904) (MIT;
libFuzzer is Apache-2.0 with LLVM exceptions); see that README for the install and the checksum.

```sh
bash tools/MeshFuzzPilot/run.sh /path/to/new-work-dir /path/to/libfuzzer-dotnet /path/to/sharpfuzz-tools/sharpfuzz /path/to/dotnet 1000000 1800 [jobs]
```

`run.sh` refuses an existing work directory. It publishes a clean `replay/` build, runs its self-test, writes the
18 seeds (every operation on the plain shapes), copies the build into `bin/` and instruments only the copy of
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
inputs for each operation, each with a time limit.
