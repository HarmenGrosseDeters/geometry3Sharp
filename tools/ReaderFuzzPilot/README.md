# Coverage-guided STL reader pilot

This test-only pilot runs the STL format reader directly under SharpFuzz and libFuzzer. It starts from one
procedurally generated binary STL triangle and retains mutated corpus files and crash/timeout artifacts in a
new, user-specified work directory. STL format detection can also send mutated data down the ASCII path.
No mesh files or instrumented binaries are committed. The other readers are future work.

The harness deliberately calls `STLFormatReader.ReadFile(Stream, ...)` rather than `StandardMeshReader`, which
catches every exception. It accepts only the exact parse-exception types already allowed by `ReaderFuzzTests`.
The binary STL reader itself converts caught loop exceptions to `GenericReaderError` with an `exception: `
prefix; the harness promotes that result to a crash candidate. Such candidates need triage because the reader
has discarded the exception type. A valid input used as the seed must read successfully as one triangle.
The harness does not yet enforce all structural or allocation invariants in `ReaderFuzzTests`.

## Setup and bounded run

Requires .NET 10, Python 3, [SharpFuzz.CommandLine 2.2.0](https://www.nuget.org/packages/SharpFuzz.CommandLine/2.2.0)
(MIT), and [libfuzzer-dotnet](https://github.com/Metalnem/libfuzzer-dotnet/releases/tag/v2025.05.02.0904)
(MIT; libFuzzer is Apache-2.0 with LLVM exceptions). Install the .NET tool in a user-owned directory. For
example on Linux x86-64 with the Ubuntu binary (tested on Debian 13):

```sh
# Run tool install outside the repository if the SDK asks which project to use.
dotnet tool install --tool-path /path/to/sharpfuzz-tools SharpFuzz.CommandLine --version 2.2.0
curl -fL https://github.com/Metalnem/libfuzzer-dotnet/releases/download/v2025.05.02.0904/libfuzzer-dotnet-ubuntu -o /path/to/libfuzzer-dotnet
# Expected SHA-256 of the pinned Ubuntu binary:
echo 'c2c2a90d94c409a4af339a0d4f244e0442c5a5d249be0f1252ba07871f285958  /path/to/libfuzzer-dotnet' | sha256sum -c -
chmod +x /path/to/libfuzzer-dotnet
bash tools/ReaderFuzzPilot/run.sh /path/to/new-work-dir /path/to/libfuzzer-dotnet /path/to/sharpfuzz-tools/sharpfuzz /path/to/dotnet 1000
```

`run.sh` refuses an existing work directory. It publishes a clean `replay/` build, checks its generated seed,
copies it into `bin/` and instruments only that copy. The run uses a fixed random seed, 4 KiB maximum input,
5-second per-input timeout, a libFuzzer-side 2 GiB RSS limit and the requested number of iterations. Corpus,
`build.log`, `fuzz.log`, the instrumented build and `artifacts/` are kept. The managed child may require an
external memory limit as well; the libFuzzer-side RSS limit alone is not a process-tree budget. Large campaigns
belong on a runner or the owner's machine, not normal CI. Arguments and limits can be adapted for a campaign;
use a fresh work directory so evidence is not overwritten.

Replay an artifact with the **unmodified** build, under the same runtime environment:

```sh
/path/to/dotnet /path/to/new-work-dir/replay/ReaderFuzzPilot.dll replay /path/to/new-work-dir/artifacts/crash-...
```

The instrumented build needs libFuzzer's shared memory and cannot be run as a standalone replay. Standalone
replay has no per-input timeout; instrumentation-specific failures and timeouts may need reproduction through
libFuzzer. Reduce and turn confirmed bugs into fixed regression tests; do not commit corpus or crash mesh files.

## Validation

```sh
dotnet build tools/ReaderFuzzPilot -c Release
READER_FUZZ_DLL="$PWD/tools/ReaderFuzzPilot/bin/Release/net10.0/ReaderFuzzPilot.dll" DOTNET_HOST_PATH="$(command -v dotnet)" python3 -m unittest discover -s tools/ReaderFuzzPilot -p 'test_*.py' -v
python3 tools/ReaderFuzzPilot/verify_crash_path.py --dotnet /path/to/dotnet --sharpfuzz /path/to/sharpfuzz-tools/sharpfuzz --libfuzzer /path/to/libfuzzer-dotnet
```

The unit/integration checks run in CI without a fuzzing campaign. The optional last command builds a temporary
mutant which throws on the generated seed, then requires a saved crash and successful reproduction of that
exception using the uninstrumented build. Three additional hand mutations remove the seed-validation guard, hide caught reader errors and swallow
unexpected reader exceptions; the integration tests must fail for all three.

One local pilot (2026-11-08, .NET SDK 10.0.403 and runtime 10.0.14, SharpFuzz 2.2.0, pinned launcher above):
`run.sh ... 1000` exited 0 with no artifact. Its log reported `INITED ft: 487` and `DONE ft: 1077` across
1,000 iterations. These are libFuzzer's feature counters for this run, not a line-coverage percentage or a
claim of exhaustive safety. The decisive unedited log lines are in [results/stl-pilot.txt](results/stl-pilot.txt); full local output is `$work/fuzz.log` and the generated corpus is retained there.
