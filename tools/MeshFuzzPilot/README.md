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
| 2 + 2 | the exponents e, f in [-1022, 1023] of a scale 2^e and of an offset unit 2^f |
| 3 | the offset, three signed bytes times 2^f |
| per corner: 1 + 3, or 1 + 24 | a mode byte. Grid mode: the corner times 16 plus three signed bytes (used if bit 0 is set), times 2^e, plus the offset. Raw mode (top three bits set): three raw doubles |

So the inputs reach coplanar, coincident and collinear corners, inverted and self-intersecting shapes, and every
binary scale from subnormal to near 1e308. A coordinate that is not finite becomes 0: the operations reject such
input by contract (`InvalidInput`), which is not a finding.

## What counts as a finding

An exception (none is documented for finite input), a timeout (libFuzzer's `-timeout`), or a result that
contradicts what the operation says about it:

- ExactMeshBoolean: `Compute()` disagrees with `Status`; `Success` without a `Result`; a result vertex that is
  not finite; `RoundingClean` while `MeshRobustRepair.NotCleanReason(Result)` is not empty (self-intersections
  are not checked when the result has `Contacts`; "degenerate triangles" is accepted for a result of several
  components, where the rounding's per-component scale and the diagonal of all components differ by design).
- MeshRobustRepair: `Apply()` true with `ResultReason` "" while the result has a vertex that is not finite or
  `NotCleanReason` is not empty (the same exception for several components).
- MeshRoundingRepair: `Apply()` disagrees with `IsClean`; a vertex that is not finite; `IsClean` with exact
  self-intersections left.

A finding is reduced and becomes a procedural regression test in its own `bugfix` PR. Inputs are not committed.

## Setup and a bounded run

The same tools as ReaderFuzzPilot: .NET 10, [SharpFuzz.CommandLine 2.2.0](https://www.nuget.org/packages/SharpFuzz.CommandLine/2.2.0)
(MIT) and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet/releases/tag/v2025.05.02.0904) (MIT;
libFuzzer is Apache-2.0 with LLVM exceptions); see that README for the install and the checksum.

```sh
bash tools/MeshFuzzPilot/run.sh /path/to/new-work-dir /path/to/libfuzzer-dotnet /path/to/sharpfuzz-tools/sharpfuzz /path/to/dotnet 1000000 1800
```

`run.sh` refuses an existing work directory. It publishes a clean `replay/` build, runs its self-test, writes the
18 seeds (every operation on the plain shapes), copies the build into `bin/` and instruments only the copy of
`geometry3Sharp.dll`. The run uses a fixed seed, at most 512 bytes per input, a 10 s timeout per input, a
libFuzzer-side 3 GiB RSS limit, and stops after the given number of runs or seconds, or at the first finding.
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

The checks run in CI without a fuzzing campaign: the self-test (a deterministic decoder, seeds that cover every
operation and run without a finding, short and empty inputs), the seed writer (refuses a directory that is not
empty) and the replay of every seed.
