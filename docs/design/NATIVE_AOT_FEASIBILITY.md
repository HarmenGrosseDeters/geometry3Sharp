# Native library for other languages via .NET NativeAOT: feasibility

Task (owner approved 2026-11-17): can C, C++, Rust and Python call `ExactMeshBoolean` and `ExactMeshImprint` through a
NativeAOT shared library, without a port? Method: a spike (`tools/NativeAotSpike`, not packed, no change to the public C#
API), measured on the cloud container (4 cores, shared; .NET SDK 10.0.115, `dotnet-sdk-aot-10.0`, linux-x64, gcc 13). Numbers
are single-container timings: read the ratios.

## Verdict

**Go for a supported native package, as an opt-in addon; the decisions below go to the owner.**

- It compiles. `PublishAot=true` for the library plus a C-ABI wrapper gives a 2.9 MB `g3native.so` (symbols in a separate
  4.7 MB `.dbg`), depending only on libc and libm, in about 11 s. No trim or AOT warning touches the boolean path.
- The result is **bit-identical** to the JIT build: the FNV hash of the result's vertex doubles and index triples is equal for
  union, intersection, difference and imprint, on offset-sphere pairs of 4 300 to 69 000 result triangles (12 cases, section 5).
- Speed: after warm-up native is about equal to the JIT build (0.9x to 1.1x, within the noise); the **first call is 5 to 6 times
  faster** (no JIT) and a one-shot process 3.5 times faster with 27 % less memory (section 4).
- Not measured here: Windows and macOS (no cross-compilation of NativeAOT; the new workflow `native-aot.yml` runs both on the PR),
  and cross-platform bit identity (section 7, risk 1).

## 1. Does it compile, and what warns

`dotnet publish tools/NativeAotSpike -c Release -r linux-x64 -p:PublishNative=true` (the project sets `PublishAot`,
`NativeLib=Shared`, `IlcOptimizationPreference=Speed`, `InvariantGlobalization`). The ILC compile gives **no warning** with the
default settings (`TrimmerSingleWarn`). To see the library's own findings the library was compiled with the analyzers on
(`-p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true -p:EnableSingleFileAnalyzer=true -p:TrimmerSingleWarn=false -t:Rebuild`):

| warning | where | what | on the boolean path? |
|---|---|---|---|
| IL3050 (6) | `core/DVector.cs:314` (`GetBytes`), `core/Util.cs:224-238` (`StructureToByteArray`, `ByteArrayToStructure`), `io/STLReader.cs:163` | `Marshal.SizeOf(Type)`, `StructureToPtr(object)`, `PtrToStructure(nint, Type)`: marshalling code generated at run time | no: serialisation and the STL reader; unreachable from the exports, so ILC trims them away |
| IL2026 (trim) | none | | |

A search of `core/ math/ mesh/ mesh_ops/` for `Activator`, `GetMethod`, `Reflection.Emit`, `Expression.Compile`,
`MakeGenericMethod`, `Assembly.Load`, `GetCustomAttribute` finds nothing. The six findings are fixable without an API change
(`Marshal.SizeOf<T>()` overloads) if the readers are ever exported. `ExactMeshBoolean` and `ExactMeshImprint` use no
reflection and no dynamic code; the parallel loops (`Parallel.For`) work in a NativeAOT library (the 4 cores are used).

## 2. The C ABI (`tools/NativeAotSpike/g3native.h`, `Exports.cs`)

An opaque handle per object, all functions return an error code, nothing throws across the boundary.

```c
int32_t g3_mesh_create(const double* xyz, int64_t nv, const int32_t* tri, int64_t nt, g3_mesh** out);  // arrays are copied
void    g3_mesh_free(g3_mesh*);
int32_t g3_boolean(const g3_mesh* a, const g3_mesh* b, int32_t op /*0 union 1 intersection 2 difference*/,
                   const g3_options* opt /*NULL = defaults*/, g3_result** out);
int32_t g3_imprint(const g3_mesh* target, const g3_mesh* cutter, const g3_options* opt, g3_result** out);
int32_t g3_result_status(const g3_result*);                       // the library's BooleanStatus / ImprintStatus as an error code
int32_t g3_result_counts(const g3_result*, int64_t* nv, int64_t* nt);
int32_t g3_result_copy(const g3_result*, double* xyz, int32_t* tri);        // caller-allocated arrays of 3*nv, 3*nt
const char* g3_result_not_clean_reason(const g3_result*);         // MeshRobustRepair.NotCleanReason: "" if clean
const char* g3_result_error(const g3_result*);                    // the library's Error text
void    g3_result_free(g3_result*);
const char* g3_last_error(void);                                  // thread-local text of the last failing call
```

- **Options:** `g3_options { struct_size, safe_rounding, strict_rounding, allow_open_meshes, seed, max_pieces }`, filled by
  `g3_options_default`; fields are read only up to `struct_size`, so new options can be appended without an ABI break. The
  other options of the C# classes (`Cancellation`, `WindingPredicate`, n-ary booleans, `ComputeExactVertices`, the imprint's
  curve labels and `SourceTriangle`) are not wrapped; a supported package needs a decision on which (section 6).
- **Ownership:** every handle is created and freed by the library; input arrays are copied at `g3_mesh_create`; result arrays
  are copied out by `g3_result_copy` into memory the caller owns; strings belong to the handle they came from. A result handle
  is returned also when the status is not `G3_OK` (it carries `g3_result_error`).
- **Errors:** `G3_NULL_ARGUMENT`, `G3_INVALID_MESH` (index out of range, non-manifold edge on creation), `G3_INVALID_INPUT`,
  `G3_ROUNDING_NOT_CLEAN`, `G3_BUDGET_EXCEEDED`, `G3_CANCELLED`, `G3_EXCEPTION` (managed exception, text in `g3_last_error`),
  `G3_BAD_OPTIONS`.
- **Threads:** distinct handles may be used from different threads; the exports are plain static calls, and the library's
  parallel loops run on the .NET thread pool inside the call.
- `NotCleanReason` is computed on first request (it is a separate check, not free) and its semantics are untouched.

`tools/NativeAotSpike/c/caller.c` (written for the spike, no external code) builds two cubes from arrays and runs the three
operations; the volumes come out as 1.875, 0.125 and 0.875 from the C side, and the error paths (bad index, `NULL` mesh, bad
operation, `max_pieces = 3` exceeded) return their codes:

```
union        rc=0 status=0 vertices=20 triangles=36 not_clean_reason=''   volume=1.875000
intersection rc=0 status=0 vertices=8 triangles=12 not_clean_reason=''    volume=0.125000
difference   rc=0 status=0 vertices=14 triangles=24 not_clean_reason=''   volume=0.875000
bad index    rc=2 (triangle 0: index out of range)
max_pieces=3 rc=5 error='the arrangement would have 48 pieces, more than MaxPieces = 3'
```

`tools/NativeAotSpike/run_spike.sh [rid] [outdir]` publishes, builds the C caller and the managed driver, runs the self test and
the bit-identity comparison; `.github/workflows/native-aot.yml` runs it on linux-x64 and osx-arm64 and publishes on win-x64.

## 3. Settings of the measurements

- Native: `PublishAot`, `NativeLib=Shared`, `IlcOptimizationPreference=Speed`, self-contained, no static PGO profile, default
  GC (workstation, concurrent). Called from C (`caller bench <op> <n> <runs>`, `clock_gettime` around `g3_boolean`).
- JIT: `net10.0`, `dotnet NativeAotDriver.dll <op> <n> <runs>` (the same managed core, `Stopwatch`). Default settings
  (TieredCompilation and TieredPGO on, ReadyToRun framework) and three variants: `DOTNET_TieredPGO=0`,
  `DOTNET_TieredCompilation=0`, `DOTNET_TC_CallCountThreshold=10`. Note that `ExactMeshBoolean.Compute` and parts of the exact
  kernel carry `MethodImpl(JitHints.Optimize)` (aggressive optimisation), which bypasses tiering and PGO for those methods.
- Input: two normalized-cube spheres, `Sphere3Generator_NormalizedCube`, radius 1 at the origin and radius 0.9 at
  (0.7, 0.3, 0.2): the offset-sphere pair of `benchmarks/ExactImprint` (23 064, 95 256 and 387 096 input triangles for
  n = 32, 64, 128). Union, defaults of the options (SafeRounding on). Median of 20 runs (n = 128: 5 runs).

## 4. Results (union, ms; "first" is the first call in the process)

| n (input triangles) | native first | native median | JIT default first / median | JIT PGO=0 first / median | JIT TC=0 first / median |
|---|---|---|---|---|---|
| 32 (23 064) | 83 | 50 | 520 / 102 | 426 / 62 | 475 / 57 |
| 64 (95 256) | 201 | 201 | 883 / 186 | 678 / 179 | 613 / 188 |
| 128 (387 096; 5 runs) | 677 | 673 | 1565 / 1068 | | |

- Steady state: native is within about ±10 % of the JIT build once the JIT is warm (n = 64: 201 against 179 to 188 ms: native
  about 8 % slower; n = 32: 50 against 57 to 102 ms, noise and tier-up dominate). With the library's `Optimize` hints most hot
  methods are compiled once at full optimisation, so the tiering and PGO settings change little; TieredPGO does not help
  this workload (PGO=0 is not slower). With 5 runs at n = 128 the JIT had not yet reached steady state.
- The first call, which is what a native caller pays in a short process: 5 to 6 times faster at n = 32 (JIT cost 0.4 s), 4 times
  at n = 64.
- Whole process, one boolean: n = 16 (managed, includes the JIT of the whole boolean) 0.52 s against a native self test of
  7 ms for three cube booleans; n = 64: 0.35 s and 110 MB peak RSS native against 1.2 s and 150 MB managed.
- Size: `g3native.so` 2.9 MB (1 MB less than the managed `geometry3Sharp.dll` of 3.2 MB, which still needs the 70 MB-class .NET
  runtime); the native library embeds its runtime and starts in ms. ILC trimming reduces the library to what the 16 exports reach.
- PGO for the native build (a static profile through `ilc --mibc`) was not tried; the gap is small enough that it is not needed
  for a first release.

## 5. Bit identity, C# against native

`run_spike.sh` compares, for union, intersection, difference and imprint at n = 16, 32, 64 (the T6 range of 1k to 100k triangles;
the T6 inputs themselves are Thingi10K meshes and cannot be used: no mesh files, no network in a PR-tier test), the hash of the result of the managed library
(`NativeAotDriver`) with the one the C caller gets from the native library: **12 of 12 identical** (e.g. union n = 64: 69 264
triangles, hash `d99882f5f952dc93`; imprint n = 64: 49 796 triangles, `5506e5809c74a215`), with `NotCleanReason` `''` in all.
The hash covers the doubles bit for bit and the index order. This is on one CPU architecture (x64) and one OS. The existing
cross-platform bit-identity measurement ([DETERMINISM.md](DETERMINISM.md)) covers net10.0 on Linux and Windows and net48; it does
not include NativeAOT, ILC on arm64 or macOS.

## 6. Effort for a supported native package (rough, one engineer-session each)

1. **API surface and ABI design (owner decision first)**: which operations (boolean n-ary, winding predicate, imprint curve
   labels, `SourceTriangle`, exact vertices, cancellation through a callback or flag, `MaxPieces`), a versioned header, a naming
   and prefix scheme. 2 to 3 sessions plus the owner's review. A stable ABI is a long-term commitment.
2. **Project and build** (`addons/geometry3Sharp.Native`, not in the existing NuGet pack): ILC publish per RID, `Shared` library,
   strip, header generation (hand-written or `cbindgen`-like check), `Directory.Build` isolation so the AOT analyzers run on
   the library. 1 to 2 sessions.
3. **CI on linux, windows, mac** (win-x64 with MSVC; linux-x64, linux-arm64 on a hosted arm runner or cross-sysroot; osx-arm64 and
   osx-x64): publish, C caller test, the bit-identity check against the managed library and **between platforms** (the hash of
   one case per platform compared in a final job), size ratchet. 2 sessions, mostly runner debugging; cross-compilation of
   NativeAOT is limited (Linux arm64 from x64 needs a cross toolchain), so build on native runners.
4. **Packaging**: a NuGet `runtimes/<rid>/native` package for .NET callers is unnecessary; the value is a tarball and wheel
   (Python `cffi` or `ctypes` wrapper), a Rust `-sys` crate with a build script that downloads the release, a C header. Signing
   (macOS notarisation) and Windows DLL signing if distributed. 3 to 5 sessions, depends on how many languages are first-class.
5. **Tests and fuzzing** of the ABI: invalid handles, double free, null pointers, concurrent use of distinct handles, allocation
   failure, a reader-fuzz-style test of `g3_mesh_create` (`ReaderFuzzTests` rule: it is an input path). 1 to 2 sessions.
6. **Docs, versioning, release process**, relation to the 1.0.324 API check (none: the addon does not change the public API). 1 session.

Total: about 10 to 15 sessions for linux-x64, windows-x64, osx-arm64 with C header, a Python wrapper and a Rust sys crate; about 5
for a linux-x64 only drop (steps 1, 2, 5 and the Linux part of 3).

## 7. Risks and open points

1. **Cross-platform bit identity** is the library's selling point and is unmeasured for NativeAOT on arm64 and macOS. ILC
   does not contract multiply-add into FMA (neither does RyuJIT), and the exact code bans FMA, but the non-exact parts (rounding,
   generators) and `Math.*` calls (libm of the platform, in the native build linked from the system libm) can differ per platform. The
   determinism harness should get a NativeAOT leg before a release claims it.
2. **Memory and failure**: a managed `OutOfMemoryException` becomes `G3_EXCEPTION`; a native caller cannot catch a stack overflow
   (deep recursion in the library would abort the process). Not looked for here.
3. **Threads**: the library's parallel loops use the .NET thread pool inside the host process; a host that forks or has its own
   signal handling (Python with `fork`) needs a note.
4. **Handles** are GC handles: a leaked result handle leaks the mesh; the header documents the ownership but cannot enforce it.
5. The six IL3050 sites are harmless now; a supported package should run the AOT analyzers in CI so a reflection use on the
   boolean path cannot appear unnoticed.
6. Windows and macOS were not measured here (section 4 is linux-x64 only).

## 8. Decisions for the owner (via the coordinator)

- Go or no-go for a supported native package, and the first platforms and languages (suggestion: linux-x64 + win-x64 + osx-arm64,
  C header, Python `ctypes` wrapper; Rust after).
- Distribution (GitHub release assets vs. wheels / crates), as an addon outside the existing NuGet pack.
- The ABI scope (section 6, step 1) and the support promise for a stable C ABI.
