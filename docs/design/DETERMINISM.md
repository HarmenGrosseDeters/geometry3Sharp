# Bit-for-bit determinism across runtimes: measurement and causes

Hard problem 11 ([research.md](../roadmap/research.md#open-research-questions)): the same input should give the same
result bit for bit on net48 and net10, serial and parallel, on x64 (ARM: measured, section 5). This note is the result of
steps 1 and 2 (harness, measurement, cause classification) and step 3 (fix proposals with cost). No library code was
changed. The harness is `tools/DeterminismHarness`, the runs are `.github/workflows/determinism.yml` (PR #545).

## 1. Method

- 49 cases with procedural inputs (own SplitMix64 generator, no mesh files): each `System.Math` function over 4096
  arguments (`libm.*`), `ExactMeshBoolean` (union, intersection, difference, with and without `SafeRounding`, box and
  sphere), `MeshRobustRepair`, `MeshAlphaWrap`, `MeshWindingNumberSolidify`, `Remesher`, `Reducer`, `DualContouring`,
  `MarchingCubes`, LSCM / ARAP / SLIM / BFF / Tutte, heat geodesic distance (plain and intrinsic), vector heat log map,
  `MeshGeodesicPath`, and runtime probes: long dependent arithmetic chains (`+ - * /`, `sqrt`, where fusing or extended
  precision would show), sorting with many equal keys (`Array.Sort`, `List.Sort`, LINQ), `double.Parse` of 17 to 20
  digit strings, `double.ToString` (`R`, `G17`, `F6`, `E10`), float casts, and OBJ / STL text writing and reading.
- Every case writes each result value as raw bits and a hash; `compare` reports per case the first diverging value, the
  diverging part of the result (`input`, `mesh`, `uv`, ...) and how many values differ. The inputs are part of the trace:
  a divergence in an `input*` section is a different input, not a different operation. The inputs of the main cases use
  only `+ - * /` and `sqrt` (exactly rounded by IEEE 754, so the same on every runtime); the cases ending in `.libm-input`
  use `Math.Sin / Cos / Exp` in the input on purpose.
- Configurations (GitHub-hosted runners, 4 cores): ubuntu net10.0 (serial, parallel twice, and with 1, 2, 3, 8
  processors via `DOTNET_PROCESSOR_COUNT`), windows net10.0 (serial, parallel), windows net48 x64 (serial, parallel
  twice), windows net48 as a 32-bit process. "Serial" sets `gParallel.SerialOnThisThread` and the per-class parallel flags
  (`Remesher`, `DualContouring`, `MarchingCubes`); other `Parallel.For` uses are not switchable (see section 6).
- net48 means the netstandard2.0 build of the library in a .NET Framework 4.8 process. Mono is not a stand-in: it uses
  the same glibc `libm` as net10 on Linux. All net48 data comes from the Windows runner.

Runs: 42838438223 (first, confounded by libm inputs), 42841615161, 42843078301 (inputs separated), 42843989517
(processor counts, x86, IO), 42845428775 (evidence notes). Raw traces are the artifacts of these runs.

## 2. Results

**Inside one runtime, every case is bit-identical** between serial and parallel, between repeated runs, and between 1, 2,
3, 4 and 8 processors (49 of 49, linux net10; 47 of 47 for the other runtimes before the IO cases were added). No
partition dependence of reductions, no dictionary or sort order dependence showed up on these inputs.

**Across runtimes (inputs libm-free):**

| group | linux net10 = windows net10 | windows net10 = windows net48 x64 | net48 x64 = net48 x86 |
|---|---|---|---|
| exact Boolean (6 variants, box-sphere), repair, alpha wrap, solidify | yes | **yes** | yes |
| Remesher, Reducer, DualContouring, MarchingCubes | yes | **yes** | yes |
| LSCM, SLIM, BFF | yes | **yes** | yes |
| ARAP, Tutte (circle boundary with `Math.Cos / Sin`) | no | no | no (ARAP), yes (Tutte) |
| geodesic distance (plain, intrinsic), log map, geodesic path | no | no | no |
| `Math.Sin Cos Tan Asin Acos Sinh Cosh Tanh Pow` | no | no | Sin / Cos / Tan yes, the rest no |
| `Math.Atan Atan2 Exp Log Log10 Sqrt` | Atan Atan2 Exp Log10 differ; Log, Sqrt same | yes | Atan Atan2 Exp Log10 no; Log, Sqrt same |
| `double.Parse` of 17+ digit strings | yes | **no** (24 of 8192) | no (7 of 8192) |
| `double.ToString` (`R`, default) | yes | **no** | no (last digit) |
| OBJ text written and read back | yes | **no** (net48 writes 15 digits, not exact) | yes |
| arithmetic chains, sort with ties | yes | **yes** | yes |
| float casts and float arithmetic | yes | yes | **no** (x87) |

`.libm-input` cases differ wherever the libm functions of the input differ, as expected.

## 3. The two questions of the coordinator

**(a) The 6 differing output coordinates of the exact Boolean (first run).** They are input vertices. The input (a bumpy
sphere) used `Math.Sin / Cos`, which differ in 6 of its vertices between the runtimes; the Boolean passes input vertices
through unchanged. In the second run `boolean.union.libm-input` shows `input-a: 6, mesh: 6` differing values, and with
libm-free inputs all seven Boolean cases are bit-identical on linux net10, windows net10, windows net48 x64 and x86.
The exact code has no libm call at all (no `Math.Sin Cos Tan Asin Acos Atan Atan2 Exp Log Pow` in `math/Exact*`,
`math/AdaptivePredicates.cs`, `mesh_ops/ExactMesh*`; checked with grep), `BannedSymbols.txt` and G3E0001-3 keep FMA, float
and hardware intrinsics out. So **the exact path does not depend on libm**; a result depends on the runtime only through
inputs that were computed with libm before the call.

**(b) The first libm call on the path of each differing case** (static reading of the call graph; the divergence is in
the result section, not in the input):

| case | first libm calls on the path | functions that differ between win net10 and win net48 |
|---|---|---|
| `geodesic.path`, `heat.intrinsic`, `vectorheat.logmap` | `IntrinsicTriangulation`: layout and flips (`Cos / Sin` lines 179, 395, 396), signposts (`Atan2 / Cos / Sin` 501-519, 719, 787), `ToWorld / FromWorld` (860-874), corner angles (`Atan2`, `angle_from_lengths`, 909); `VectorHeatMethod` (`Cos / Sin / Atan2`, 229, 441-482) | Sin, Cos (Atan2 is identical on windows, so Sin / Cos are the cause there) |
| `geodesic.heat` | `IntrinsicDelaunay` is on by default, so the intrinsic code above (edge flips with `Cos / Sin`) runs first; then the heat flow in log space: `Math.Exp / Log` (`MeshGeodesicDistance` 389, 469, 485, `SparseCholesky` 192, 248) | Sin, Cos (Exp and Log are identical windows-to-windows) |
| `param.arap` | `ARAPParameterization` local step (`Atan2`, `Cos`, `Sin`, lines 89, 174-176) | Sin, Cos |
| `param.tutte` | circle boundary placement `Math.Cos / Sin` (`SLIMParameterization.cs` 55) | Sin, Cos (differs windows net10 against the other three) |
| `param.lscm / slim / bff` | `Math.Atan2` (angles, `MeshParameterization.cs` 262-289, BFF 80) and `Cos / Sin` (BFF 194) only on diagnostics and the BFF boundary | identical on all four configurations on this input |

Where a libm function is only used for diagnostics or a rarely taken branch the results stay identical; which function
differs depends on the platform pair: Linux glibc and Windows net48 agree more often with each other than either with
Windows net10 (`geodesic.path` is identical on linux net10 and windows net48 x64 and differs on windows net10).

## 4. Causes

**C1. Transcendental functions differ in the last bit** between OS and runtime (`libm` of glibc, the Windows CRT used by
net10, the one used by .NET Framework). Per 8192 arguments between windows net10 and windows net48 x64: sin 146, cos 128,
tan 180, asin 48, acos 29, sinh 91, cosh 317, tanh 4, pow 6; between linux net10 and windows net10 additionally atan 83,
atan2 5, exp 16, log10 61, sinh 1158, cosh 1022. All are 1 ulp differences. They enter ARAP, Tutte, every intrinsic
triangulation algorithm (geodesics, vector heat, log map, geodesic Bezier) and every generator or input built with
trigonometry. Not in the exact code, not in the Boolean / repair / alpha wrap / solidify / remesh / reduce / contour
chains on the inputs tested.

**C2. `double.Parse` on .NET Framework is not correctly rounded for long digit strings.** Example:
`"5.6348333975312060066e-8"` gives `0x3e6e4074bfc9cfd2` on net10 (documented correctly rounded since .NET Core 3.0) and
`0x3e6e4074bfc9cfd1` on net48. 24 of 8192 strings of 17 to 20 digits differ. Every text reader (OBJ, STL, PLY, OFF, 3MF,
height grids) that uses `double.Parse` (`MeshIOUtil.ParseDouble`, `OBJReader`) can give a different vertex on net48 for the
same file when it carries 17 or more significant digits (writers of other tools often do).

**C3. Double formatting differs.** `x.ToString()` and `{0}` give the shortest round-trip string on net10
(`-7.730993158856909E-05`) and 15 significant digits on net48 (`-7.73099315885691E-05`); `"R"` is the shortest on net10 and
17 digits on net48; `"G17"` is the same on both x64 runtimes. `OBJWriter` writes `{0}` of a double: the OBJ file of the
same mesh differs (`v 0.5066380290214285 ...` against `v 0.506638029021429 ...`) and **the net48 file does not read back
to the same vertices** (`roundtrip exact: False`; net10: True). The STL text round trip is not exact on any runtime (not examined
further: probably the float precision of the format). On the x86 net48 process the 17-digit form differs in the last digit as well (x87).

**C4. 32-bit net48 (x87 / legacy FPU semantics).** Float arithmetic differs (`probe.float-cast`), and the libm functions
differ further (asin, acos, atan, exp, log10, sinh, cosh, tanh, atan2, pow against x64). Plain double arithmetic does not
(`probe.arith-chain` and every pure-input operation are identical to x64): the .NET Framework x86 JIT uses SSE2 for
double. Float code (`Vector3f`, `MeshLocalParam`, glTF) is where x86 would show.

**C5. Parallel reduction order, thread count.** No difference found (section 2). The library's own parallel loops write
disjoint outputs; reductions go through `PartitionedReduction` and the exact code forbids parallel loops without an entry
in the allow list. Not covered: loops not switchable by `gParallel.SerialOnThisThread`, other core counts on Windows.

**C6. FMA and JIT contraction.** No difference: x64 RyuJIT (net10) and the .NET Framework JIT do not fuse `a * b + c`
(`probe.arith-chain` identical on all four configurations, 256 chains of 64 steps each).

**C7. Sort stability and hash ordering.** No difference in the sort probe (`Array.Sort(keys, items)`,
`List.Sort(Comparison)`, `Array.Sort(Comparison)`, LINQ `OrderBy`, 20 to 1000 elements with 7 distinct keys); identical on
all configurations. Not audited: the library's use of unstable sorts where ties decide the output, and iteration order of
hash sets after removals.

## 5. ARM: what could differ, and the arm64 runs (2026-11-10)

Predictions before the runs:

- C1 again, with a third libm (glibc aarch64, Apple libm, Windows ARM CRT): expect the same class of 1 ulp differences.
- Fused multiply-add: the arm64 JIT contracts `a * b + c` only through explicit `Math.FusedMultiplyAdd` / vector
  intrinsics as far as we know (to verify with `probe.arith-chain` on arm64). In the exact code both are banned.
- `Vector128` / `Vector<T>` paths and `Math.Sqrt` are exactly rounded; `Math.Round`, `Floor` are exact.
- Denormal handling (flush to zero) differs only if a process sets it; not set by the library.

Result (workflow run of PR #574; GitHub-hosted runners `ubuntu-24.04-arm` (Ubuntu 24.04, glibc aarch64) and
`macos-latest` (macOS 26, Apple silicon), .NET 10.0.15; the x64 side is the existing linux x64 run on Ubuntu 26.04):

| comparison | cases identical / different |
|---|---|
| linux arm64 serial vs linux x64 serial (net10.0) | **50 / 0**: every case bit-identical, including all `libm.*` functions over 8192 arguments each, the geodesic family and the text I/O |
| linux arm64: parallel vs parallel2, parallel vs serial, serial vs 1 and 3 processors | 50 / 0 each (level L0 holds on arm64) |
| macOS arm64: parallel vs parallel2, parallel vs serial, serial vs 1 and 3 processors | 50 / 0 each (level L0 holds) |
| macOS arm64 serial vs linux x64 serial | 27 / 23: the differences are the libm functions (acos, asin, atan, atan2, cos, cosh, exp, log10, pow, sin, sinh, tan, tanh: Apple's libm differs from glibc by 1 ulp for 7 to 1607 of 8192 arguments) and, through them, `geodesic.*`, `param.arap` / `param.tutte`, `remesher.libm-input`, `boolean.union.libm-input`, `dualcontouring.blob.libm-input` |
| macOS arm64 vs linux arm64 | 27 / 23, the same cases |
| windows net48 x64 vs linux / macOS arm64 | the same 23 to 28 cases as net48 vs linux x64 (libm and text format, C2 and C3 before their fixes) |

Reading: the arm64 JIT does not contract `a * b + c` (no case outside the libm functions and their consumers differs, so
`probe.arith-chain` and every pure-input operation, the exact predicates and all Boolean / mesh cases without libm
input are bit-identical to x64), and the exact code is unaffected. The only dependency is the platform libm: **glibc on
aarch64 gives the same bits as glibc on x86-64 for all 12 288 sampled arguments of each function** (the same
algorithms, correctly rounded or not, run in the same arithmetic), so L1 extends to linux arm64 without work; Apple's
libm and the Windows CRT are the other two libms, and C1 (portable `DMath`) removes the remaining dependency. Windows
arm64 was not run (no net48 there, and its CRT is the one already measured on x64). One run per configuration, so
this is a measurement and not a proof; the compare job reruns on every harness change.

## 6. Limits of this measurement

One runner type, 4 cores; small inputs; 49 cases, not the whole API (not covered: `QualityMesh2`, plane cut, terrain,
ICP, smoothers, `MeshLocalParam`, the Boolean on large inputs, text IO other than OBJ and STL); `Parallel.For` outside
`gParallel` and the three per-class flags cannot be switched to serial; core counts other than 4 only on Linux; no
arm64. Absence of a difference is evidence on these inputs, not a proof.

## 6a. The libm rule (owner 2026-11-11)

Only the transcendental functions (`Sin`, `Cos`, `Tan`, `Atan2`, `Asin`, `Acos`, `Exp`, `Log`, general `Pow`) differ between
platforms (the harness: macOS arm64 differs from x64 only in libm and its consumers; `+ - * /`, `Sqrt`, `Floor`, `Round`,
`Abs`, `Min` / `Max` are IEEE-exact everywhere). The correctly rounded `CoreMath` (C1, vendored from CoreMathSharp, a port of
CORE-MATH, MIT; `docs/SOURCES.md`, design note `DMATH.md`) is used **only where the library promises cross-platform bit
identity** (first the geodesic family: `IntrinsicTriangulation*`, `IntrinsicCommonSubdivision`, `VectorHeatMethod`,
`MeshGeodesicDistance`, `MeshGeodesicPath`, `MeshGeodesicBezier` and `SparseCholesky.LogAdd`), not as a global replacement of
`System.Math` (cost 1.5 to 4 times per call, more with the software FMA on net48; no gain in accuracy that matters to
geometry tolerances). It is extended to further code only when the determinism harness shows a real difference that the
result depends on, with a harness case and benchmark numbers. Files using `CoreMath` may not call the `System.Math`
transcendentals: the analyzer G3E0004 (`tools/analyzers/G3ExactCodeAnalyzers`, scoped to those files by `.editorconfig`,
tested in `ExactCodeAnalyzerTests`) makes a `Math.Sin` & co. in them a build error.

## 7. Contract and fix proposals

**Levels** (proposal for the owner): L0, same runtime, OS and architecture: serial equals parallel equals repeated runs
(holds today on all cases tested, 1 to 8 processors). L1, x64 across net48 / net10 / Windows / Linux for operations with
exact or libm-free paths (holds today for exact Boolean, repair, alpha wrap, solidify, remesh, reduce, contouring, LSCM,
SLIM, BFF when the input is libm-free). L2, everything, including ARM and all I/O (needs C1 to C3).

| cause | proposal | cost | risk |
|---|---|---|---|
| C1 libm | a small portable `DMath` (sin, cos, atan2, acos, asin, exp, log, pow, using only `+ - * /` and `sqrt`, with tables and polynomials from the public fdlibm family; results identical on every IEEE 754 platform without FMA contraction), used in the files listed in section 3 and in the generators that feed exact operations. Building block: `portable_atan2` of PR #539 (within 4.4e-16 of `Math.Atan2`, pinned-bits test over 100k inputs on the net48 CI). A correctly rounded library (CORE-MATH) would be better still; its licence must be checked before any use (MIT / Apache / BSD only) | about one PR per group (angles: atan2, acos, asin; trig: sin, cos; exp, log, pow), tier L with pinned bits tests; about 1.5 to 4 times slower per call than the hardware function; the share of the run time in these calls is small in the intrinsic code (to be measured) | results change in the last bit for everyone once (golden masters of the geodesic tests move); not bit-identical to `Math.*` any more |
| C2 parse | one correctly rounded decimal-to-double parser for all runtimes (Eisel-Lemire as in fast_float, MIT / Apache, with a big-integer fallback), used by `MeshIOUtil.ParseDouble` and the readers | small PR, tier M; parsing speed about the same or better | none for net10 (already correct) |
| C3 format | writers use `"G17"` (equal on both x64 runtimes, exact round trip) or a shortest-round-trip formatter (Ryu, Apache-2.0 / Boost) written from the paper; OBJ, PLY, OFF, STL text, HeightGrid | small PR, tier M; files get longer with `G17` (17 digits) | file contents change on net10 (shortest form today); round trip exactness is the gain |
| C4 x86 | state in the docs that bit identity is for x64 (and later arm64) processes; no work | none | none |
| C5 / C7 | keep the determinism tests; add `DOTNET_PROCESSOR_COUNT` runs (done here) to the scheduled tier; audit unstable sorts with ties and hash set iteration after removals (one grep-and-read pass) | small | none |
| ARM | done (section 5): linux arm64 is bit-identical to linux x64; macOS arm64 differs only through Apple's libm | done | none |

**Order of value**: C3 and C2 first (small, and the OBJ round trip on net48 is a plain data-loss defect today), then the
ARM run, then C1 for the geodesic family if the owner wants L2 for it.

## 8. Owner decision (2026-11-10) and plan

Approved, in this order, one PR each: (1) C3: every text writer writes round-trip exact doubles on every runtime
(test: write on one runtime, read back bit-exact, on the net48 CI); (2) C2: a correctly rounded double parser for the
readers (or a fallback on net48 only), added to `ReaderFuzzTests`; (3) the arm64 run of the harness on a GitHub-hosted
arm64 runner (no self-hosted runner), result into this note; (4) C1: a small portable `DMath` for the geodesic /
intrinsic family (`portable_atan2` of #539 as building block once merged; permissive sources only after a licence check,
listed in `docs/SOURCES.md`), with the contract levels L0 / L1 / L2 documented. The state of each step is in its PR.

## 9. Reproduction

`dotnet run --project tools/DeterminismHarness -c Release -f net10.0 -- run --out <dir> [--serial] [--filter <text>]`,
`... -- compare <dirA> <dirB> [--brief]`. net48 on Windows: `dotnet build tools/DeterminismHarness -c Release` and run
`bin/Release/net48/DeterminismHarness.exe`. The workflow `determinism.yml` does all configurations and the comparisons
(on harness changes and on demand).
