# Bit-for-bit determinism across runtimes and CPUs: what holds, what can be proven, what stays a golden master

Research spike (owner go 2026-11-22) for hard problem 11, on the plans #1286 (umbrella), #1287 (golden masters across
runtimes) and #1241 (proof stage 2), with #1240 for context. It answers four questions: which public operations are
bit-identical today across net10.0, the netstandard2.0 build on .NET Framework 4.8, x64, arm64, Windows, Linux and macOS;
which are not, and why; what can be proven; and what has to be a golden master. No library code was changed. The
measurements before this study are [DETERMINISM.md](../design/DETERMINISM.md) (the harness, the causes C1 to C7) and
[DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md) (the proof method, stages 0 and 1); this note only adds to them.

The tools are in the PR: new cases of `tools/DeterminismHarness` (`Examples.cs`, `CrossRuntime.cs`), `cross_matrix.py`,
`scope_report.py`, the new tool `tools/DeterminismReach`, and new legs of `.github/workflows/determinism.yml`.

## 1. Answers

1. **Inside one runtime (level L0), one documented operation is not deterministic today: `LaplacianMeshSmoother`** (finding
   F1, section 3.3, issue #1314). Its result follows the order in which a parallel loop finishes; the same code is behind
   `LaplacianMeshDeformer` and `LaplacianCurveDeformer`. Every other case (183 of the 184, one of them different between
   processes by design) is bit-identical between serial and parallel, repeated processes, 1 to 8 processors, and backwards or
   permuted loop orders, on every runtime measured.
2. **Across JIT configurations of one machine nothing changes**, on linux x64, Windows x64 and linux arm64: with all vector
   instructions off (`DOTNET_EnableHWIntrinsic=0`, which also turns the two `Vector256` kernels of the library and the
   hardware FMA of `CoreMath` off), without AVX2, without tiered compilation and unoptimized (`JITMinOpts`), every case has
   the bits of the plain run. The JIT does not contract `a * b + c`, vector width does not reach a result, and the
   optimization level does not either (section 3.2).
3. **Linux x64 and linux arm64 agree on every case** except the bits of NaN results. **Across C libraries, 17 to 41 of the
   183 cases differ per pair, and every one of them is on the list of cases that reach a platform libm function** or is a
   known runtime fact (`Math.Min / Max` of zeros, casts, text formatting, `#if NET` branch flags). The differing documented
   operations are the same eight or fewer on every pair: `ContourLoft` (2 examples), `QuadRemesher`, `TpmsLattice`,
   `UVAtlasGenerator` / `ConeFlattening` (3), ARAP and Tutte parameterization and the example's own point sampler; plus 7 of
   the 12 mesh generators that run with defaults (F4). For `ContourLoftStack` the difference reaches the status, not only
   coordinates (F5).
   The exact Boolean (15 cases, with the new open-input case), the exact building blocks, repair, alpha wrap, solidify,
   `Remesher`, `Reducer`, both contouring operations, the geodesic family (`CoreMath`) and the terrain slope design agree on
   **all** pairs: linux, Windows, macOS; x64 and arm64; .NET 10 and .NET Framework 4.8 (64-bit and 32-bit).
4. **The C library also differs with the CPU, not only with the OS** (F6): the same .NET Framework x64 build gives other
   `sin`, `cos`, `tan`, `exp`, `pow` bits on the Windows arm64 runner (an emulated x64 process) than on the x64 runner, and
   macOS x64 differs from macOS arm64. "Same OS, same runtime" is therefore not a libm family.
5. **The static picture is wider than the harness**: 56 of the 142 public types named in the documentation can reach a
   platform libm function on static call edges (66 with dynamic dispatch), 18 of them without any harness case, and one of
   the 56 is `ExactMeshBoolean` itself (finding F2: `Math.Acos / Asin / Cos` in the hierarchical winding number of open
   inputs; an ulp there changes nothing, section 3.3, and the case agrees on all platforms, but the "no libm in the exact
   code" statement of DETERMINISM.md section 3 is no longer true and no rule guards it).
6. **Provable** (in the sense the owner fixed on 2026-11-13: a machine-checked reduction to listed assumptions and
   obligations): the exact Boolean, stage 2. Turning the scope on for `ExactMeshArrangement` and `ExactMeshBoolean` reports
   165 unlisted external members and 147 sites of restricted members (stage 1 started from 153 members), so it fits the
   plan's budget; the two open design points are the 36 members of `MeshRoundingRepair` it calls (3 259 lines of
   floating-point code outside every scope) and the libm calls of F2 (section 5).
7. **Golden master** (section 6): everything else. The recommendation is two tiers, decided by a machine check and not by
   the author: a bit-exact digest for operations whose reachable set has no platform libm (checked by `DeterminismReach`
   and confirmed by a harness case), and for the rest a digest per libm family plus invariants, never a silent tolerance.

## 2. What this study adds to the harness

The harness had 98 cases, written one by one, and referenced 58 of the library's 677 public types. It now has 184 cases
and references 141 types, 83 of the 142 documented ones.

| cases | what | count |
|---|---|---|
| `examples.*` | every public static method of the compiled examples of `docs/OPERATIONS.md` (`docs/examples/*.cs`), on fixed arguments, results traced by reflection (return value, `out` parameters, meshes changed in place, public members six levels deep; members named as timings, work counters or messages left out by whole words, anything else the trace cannot follow counted in a section `not-traced`) | 63 |
| `generators.*` | every public `MeshGenerator` with a parameterless constructor, with its defaults; 12 produce a mesh, 6 need a curve or polygon the defaults do not have and record only the exception type (both revolve generators, `PointSplatsGenerator`, `TriangulatedPolygonGenerator`, `TubeGenerator`, `VerticalGeneralizedCylinderGenerator`) | 18 |
| `boolean.open-winding` | a Boolean with an open input of more than 1000 boundary segments: the only case that reaches F2 | 1 |
| `winding.values` | raw `FastWindingNumber` / `WindingNumber` values and the inside decisions | 1 |
| `probe.libm-constfold`, `probe.linq-sum`, `probe.round-remainder` | JIT constant folding of transcendental functions, LINQ sums against a loop, `Math.Round` with digits, `%`, `IEEERemainder` | 3 |

The examples are the documented surface by construction: a new example becomes a case without a line of harness code, and
an example whose parameter the argument table does not know records `unsupported` (one today:
`IntrinsicTriangulation.CommonSubdivision`, which takes an `IntrinsicTriangulation`). The header of every run now has a line
`# machine` with the vector widths the JIT uses and whether the library's vector kernels are on, so a switch that had no
effect is visible (two of the switches tried locally, `DOTNET_EnableAVX512F` and `DOTNET_EnableFMA`, changed nothing in
that line on .NET 10 and are not counted).

## 3. Measurements

### 3.1 Local (linux x64, .NET 10.0.15, Intel Xeon with AVX-512, 4 cores)

`dotnet tools/DeterminismHarness/bin/Release/net10.0/DeterminismHarness.dll run --out <dir> [--serial]`, then `compare`:

| run against the serial run | identical / different (without `probe.hash-seeded`, which differs by design) |
|---|---|
| second serial process | 183 / 0 |
| parallel | 183 / 0 |
| `DOTNET_PROCESSOR_COUNT` 1, 2, 3, 8 (parallel) | 183 / 0 each in one series; `examples.Smoothing.Laplacian` differed for 1 and for 3 in two others (F1 is a race) |
| `DOTNET_EnableHWIntrinsic=0` (`Vector<double>.Count` 2, no `Vector128 / 256 / 512`, `FastWindingLeafKernel` off) | 183 / 0 |
| `DOTNET_EnableAVX2=0` (128-bit vectors only) | 183 / 0 |
| `DOTNET_JITMinOpts=1` | 183 / 0 |
| `DOTNET_ReadyToRun=0 DOTNET_TieredPGO=0` | 183 / 0 |
| `DOTNET_TieredCompilation=0` | 183 / 0 serial; in one parallel run `examples.Smoothing.Laplacian` differed (F1; the timing of the threads changed, not the arithmetic) |
| `adversary --seeds 5` (every `gParallel` loop backwards and in 5 permutations) | 183 same, 1 known to differ (F1), 0 new |

(The local counts were taken before the review widened the trace of the example cases; the workflow run of section 3.4
repeats the serial, parallel, processor-count and switch comparisons with the final code, with the same result.)

### 3.2 What the switches show, cause by cause

- **FMA contraction by the JIT**: none. With hardware FMA available and with it removed the results are the same, on every
  case, and `probe.arith-chain` agrees on all configurations of the earlier runs. The only FMA in the library is
  `Math.FusedMultiplyAdd` inside `CoreMath`, a correctly rounded operation with a software twin for .NET Framework
  (`CoreMathFma`); the reachability tool finds no other use (0 methods outside `CoreMath`).
- **`Vector<T>` and vector widths**: the library has two vector paths, both `Vector256` and both written to give the
  bits of their scalar twins (four fixed partial sums or four independent lanes, separate multiply and add):
  `SparseCholeskyKernels` (the factorization behind the geodesics, ARAP, biharmonic, BFF) and `FastWindingLeafKernel`.
  30 documented types reach one of them. On, off and at 128 bits the results are the same; arm64, where `Vector256` is not
  accelerated, takes the scalar twins.
- **Tiering and optimization level**: no effect on any case (`TieredCompilation=0` compiles everything fully optimized
  at the first call, `JITMinOpts=1` nothing). `probe.libm-constfold` compares a transcendental function of a literal with
  the same call on a value the JIT cannot see, in a method compiled with full optimization and in one compiled without:
  on linux x64 all 30 pairs are equal and the two methods agree. Its values differ between platforms as the `libm.*`
  cases do, and only its values: the equality flags are the same on linux, Windows x64 and arm64 (.NET 10) and .NET
  Framework (the step "Differing parts of the documented operations" of the workflow; macOS not read).
- **The BCL's own vectorization**: `probe.linq-sum`: `Enumerable.Sum / Average` of doubles equal the plain loop.
- **Parallel reductions and thread count**: no effect except F1.
- **Hash ordering**: no effect on any case between two processes (the hashes of `BigInteger`, tuples and strings are
  seeded per process on .NET, `probe.hash-seeded`).
- **`float` to `double`**: 350 methods use `float`, 66 documented types reach one; no difference on x64 or arm64 in the
  earlier runs, only in a 32-bit .NET Framework process (x87, owner decision C4: outside the claim).

### 3.3 Findings

**F1. `LaplacianMeshSmoother` depends on the schedule (level L0, a defect).** `PackedSparseMatrix.Square`
(`solvers/PackedSparseMatrix.cs`) computes the entries of the squared matrix in a `gParallel.BlockStartEnd` loop and
appends them to one list under a spin lock, in the order the blocks happen to finish. The rows of the result keep that
order, and `Multiply_Parallel` adds `row[k].d * X[row[k].j]` in row order, so the summation order of every product in the
conjugate-gradient solve follows the schedule. Reproducer:
`DeterminismHarness adversary --filter Smoothing` (backwards loop order: 365 of 3541 result values differ, last digits);
between processes it shows as a difference between processor counts (three different results for 1, 3 and the other
counts in one local series). Sorting the entries by (row, column) before the matrix is built removes it (checked in a
scratch copy: the adversary then reports no difference). The callers of `SymmetricSparseMatrix.SquarePackedParallel` are
`LaplacianMeshSmoother`, `LaplacianMeshDeformer` and `LaplacianCurveDeformer`. Inherited from upstream; not covered by the
harness before because no case used these classes. The adversary lists the case as `KNOWN` until it is fixed.

**F2. The exact Boolean calls the platform libm.** `ExactMeshBoolean.antipodal_tree_value` (the hierarchical evaluation
of the winding number of open inputs, since Exact Boolean 3.0) computes `gamma = Math.Acos(...)`, `alpha = Math.Asin(...)`
and two `Math.Cos` for `fullBound`, an upper bound of the error the full sum would have; `tree_decides` admits a tree
value only where `fullBound <= 0.999e-6`. The values do not enter the winding number itself (its terms use
`portable_atan2`). No harness case reached this code; `boolean.open-winding` does (24.2 million calls of the four sites).
Moving every result of the four calls by one ulp up, down or at random (a scratch copy of the library) changes no value of
that case: not the mesh, not the status, not the winding number of any open piece. The argument why it cannot: a flip of
`tree_decides` hands the piece to the full sum, whose own bound is then below 1e-6 by the factor 0.999, so it decides the
same threshold with the same ray; that is form P4 with a stated slack. It is an argument, not a rule: G3E0004 is not
scoped to `mesh_ops/ExactMeshBoolean.cs`, and DETERMINISM.md section 3 still says the exact code has no libm call. Stage 2
has to register the three members (section 5).

**F3. The winding number values of `DMeshAABBTree3` are libm values.** `FastWindingNumber` and `WindingNumber` end in
`Math.Atan2` per triangle (`MathUtil.TriSolidAngle`, `FastWindingLeafKernel`). The consumers compare with 0.5; among the
documented types that reach it are (`MeshRobustRepair`, `MeshAutoRepair`, `MeshRoundingRepair` and through it `ExactMeshImprint`
and the `SafeRounding` path, `MeshBatchQueries`, `MeshBoolean`, `MeshMeshCut`, `TpmsLattice`). `winding.values` records the
raw values and the decisions separately (section 3.4).

**F4. The generators with trigonometry make platform-dependent inputs.** Seven of the twelve generators that run with
their defaults differ between C libraries: `CappedCylinderGenerator`, `ConeGenerator`, `OpenCylinderGenerator`,
`PuncturedDiscGenerator`, `Radial3DArrowGenerator`, `RoundRectGenerator`, `TrivialDiscGenerator` (`Math.Cos / Sin`). A mesh
from them differs in the last bit of some vertices, and every operation passes that on (as the `*.libm-input` cases show
for the Boolean, the remesher and dual contouring). This is the most likely way a user sees "the Boolean is not
deterministic".

**F5. Across C libraries a status can differ, not only the last bits.** `examples.ContourLoft.LoftBranchingStack`
(`ContourLoftStack`) differs between Windows .NET 10 and .NET Framework, and between Windows x64 and arm64, in about 880
of its 1 293 traced values **and in `Status`**; `LoftFrustum` (`ContourLoft`) differs in the mesh and in `TotalCost`.
Their static path to the libm goes through the cap meshing (`QualityMesh2`), and they have angle computations of their
own (`--why ContourLoft libm` prints the shortest path). An operation whose status depends on the platform is a
behaviour difference, not a rounding difference, so this is the first candidate for the libm rule among the measured
ones. `QualityMesh2` itself, which reaches `Math.Sin` for its angle bound (one call per `Compute`), is bit-identical on
every pair measured.

**F6. The C library's results depend on the CPU.** On the Windows arm64 runner the .NET Framework harness runs as an
emulated x64 process (the header of the run says `X64`; there is no native arm64 .NET Framework leg), with the same C
runtime binary as on the x64 runner, and 34 cases differ from the x64 runner: `libm.sin / cos / tan / exp / pow / sinh /
cosh / tanh / acos / asin` and their consumers. .NET 10 on Windows arm64 differs from Windows x64 in `cos`, `exp`, `tan`
and integer powers; macOS x64 differs from macOS arm64 in 8 of the 16 functions. The likely cause is a dispatch on the
instruction sets of the machine inside the C runtime (not verified; the emulated process reports no FMA). Linux is the
exception: glibc on x64 and on arm64 agree on all sampled arguments. A "libm family" in the sense of section 6 is
therefore (C library, architecture, instruction sets), and only a function that does not call the C library is the same
everywhere.

### 3.4 Across runtimes, operating systems and architectures

Determinism workflow run 44987202489 (head b16ab53, GitHub-hosted runners, .NET 10.0.15 and .NET Framework 4.8.9363; the
table is the output of `cross_matrix.py` in the step "Cross-runtime matrix" of the job `compare`, columns shortened; a cell
is identical cases / cases, `probe.hash-seeded` left out). Runners: linux x64 (ubuntu-latest), linux arm64
(ubuntu-24.04-arm), Windows x64 (windows-latest), Windows arm64 (windows-11-arm), macOS arm64 (macos-latest), macOS x64
(macos-15-intel).

Inside one runtime every cell is full: parallel = serial, 1 and 3 processors, no vector instructions (linux x64, Windows
x64, linux arm64), no AVX2, no tiering, unoptimized (linux x64): 183 / 183 each.

| group (cases) | linux = Windows, .NET 10 | Windows: .NET 10 = net48 | linux .NET 10 = Windows net48 | net48: x64 = x86 | linux: x64 = arm64 | Windows .NET 10: x64 = arm64 | linux = macOS arm64 | linux = macOS x64 | macOS: arm64 = x64 |
|---|---|---|---|---|---|---|---|---|---|
| `boolean` (15), `stage1` (12), `coremath` (2), `geodesic` (7), `terrain` (2), `alphawrap`, `repair`, `solidify`, `remesher`, `reducer`, `dualcontouring`, `marchingcubes` (1 each) | all | all | all | all | all | all | all | all | all |
| `kernel` (10) | 10 | 9 | 9 | 8 | 10 | 10 | 10 | 10 | 10 |
| `examples` (63) | 55 | 56 | 56 | 56 | 63 | 57 | 56 | 55 | 55 |
| `generators` (18) | 11 | 11 | 12 | 12 | 18 | 18 | 11 | 11 | 11 |
| `param` (5) | 3 | 3 | 4 | 4 | 5 | 3 | 3 | 2 | 2 |
| `winding` (1) | 1 | 1 | 1 | 1 | 1 | 1 | 0 | 0 | 1 |
| `*.libm-input` (3) | 0 | 0 | 2 | 0 | 3 | 0 | 0 | 0 | 0 |
| `libm` (16) | 2 | 6 | 2 | 5 | 16 | 12 | 2 | 2 | 8 |
| `io` (2) | 2 | 1 | 1 | 2 | 2 | 2 | 2 | 2 | 2 |
| `ledger` (2) | 2 | 1 | 1 | 2 | 1 | 1 | 1 | 2 | 1 |
| `probe` (18) | 17 | 9 | 9 | 12 | 18 | 17 | 17 | 17 | 18 |

The cases behind the cells that are not full:

- **`examples`**, linux against Windows .NET 10 (8): `ContourLoft.LoftBranchingStack`, `ContourLoft.LoftFrustum`,
  `PointCloud.SampleUnitSphere` (the example's own sampler), `QuadRemesh.Run`, `TpmsLattice.GyroidSheet`, `UVAtlas.Cones`,
  `UVAtlas.CutAndFlatten`, `UVAtlas.Run`. The other pairs differ in subsets of these eight. All of them belong to
  documented types that reach the platform libm (section 4), the sampler aside, which calls it itself; the other 55
  example cases agree on every pair, among them the 2D mesher, among them every exact Boolean, imprint and pipeline
  example, repair, alpha wrap, the remesher and reducer, smoothing and deformation, offset, plane cut, hole filling, the
  tet mesher, the terrain tools, point cloud normals and surface, I/O, and the intrinsic and geodesic examples.
- **`generators`** (7, F4). Windows x64 and arm64 .NET 10 agree on them, linux x64 and arm64 too.
- **`param`**: `param.arap` and `param.tutte` (known, DETERMINISM.md); `param.bff` differs in addition where macOS x64 or
  the emulated .NET Framework process is on one side (its `Atan2` / `Cos`, identical on the four configurations of the
  earlier measurement).
- **`winding`**: `winding.values` differs between linux and macOS only (F3; the differing part is printed by the detail
  step); it agrees between linux, Windows .NET 10 and .NET Framework.
- **`kernel`, `ledger`, `io`, `probe`**: the known runtime facts of DETERMINISM_PROOF.md 5.3: the `#if NET` branch flag
  (`kernel.limbsigns.path`), `Math.Min / Max` of zeros and NaN bits (`ledger.math`; the only case that differs between
  linux x64 and arm64), casts, text formatting and parsing on .NET Framework, the x87 cases of the 32-bit process, and new:
  `probe.round-remainder` differs between .NET 10 and .NET Framework in 6 of its 26 635 values, all in the section of
  `Math.Floor / Ceiling / Truncate` (not `Math.Round` with digits, not the remainders; not diagnosed further, the sign of a
  zero result is the likely part), and `probe.libm-constfold` differs wherever the libm does (its values are libm values).
- **Windows arm64, .NET Framework** (not in the table): an emulated x64 process, 34 cases different from the x64 runner (F6).

The same run's opt-in job on the previous head (run 44986300782, job `suite-arm64`) ran the PR-tier test suite
(`Category!=Slow&Category!=Nightly`) on linux arm64: green. Every pinned value of the suite therefore holds on linux
x64, Windows .NET Framework 4.8 (both in `ci.yml`) and linux arm64.

## 4. Static reachability: which operations can depend on which source

The harness answers for the cases it has. `tools/DeterminismReach` reads the IL of the compiled library and answers for
every public type whether its public methods (the inherited ones included) can reach a source at all: `static` follows
calls, constructor calls, created delegates, static constructors and the callbacks of the BCL into types a method touches
(iterators, `CompareTo`, `ToString`); `cha` also follows a virtual or interface call to every implementation in the
library. `static` is meant as a lower and `cha` as an upper bound for code inside the library; the limits of both are in
the tool's README (delegates from outside, reflection, enumeration through LINQ, `string.Format`), so a "does not reach"
is evidence to be confirmed by a harness case, not a proof. Output of
`DeterminismReach report geometry3Sharp.dll --docs docs/OPERATIONS.md --docs docs/guides --harness DeterminismHarness.dll`
(14 756 methods, 677 public types, 142 named in the documentation):

| source (rule of DETERMINISM_PROOF.md 5) | methods using it | public types reaching it, static / cha | documented types, static / cha | documented, static, without a harness case |
|---|---|---|---|---|
| platform libm (R1) | 165 | 145 / 181 | 56 / 66 | 18 |
| `CoreMath` (correctly rounded) | 221 | 12 / 12 | 9 / 9 | 0 |
| FMA outside `CoreMath` (R3) | 0 | 0 / 0 | 0 / 0 | 0 |
| vector types (R3) | 12 | 48 / 71 | 30 / 34 | 5 |
| `float` (R2) | 350 | 377 / 397 | 114 / 114 | 46 |
| direct `Parallel`, threads (R12) | 15 | 122 / 142 | 61 / 61 | 17 |
| `Interlocked` (R13) | 89 | 68 / 68 | 37 / 37 | 9 |
| `Environment.ProcessorCount` (R9) | 9 | 31 / 31 | 20 / 20 | 1 |
| clock (R9) | 90 | 35 / 35 | 20 / 20 | 2 |
| `System.Random` (R9, R10) | 14 | 20 / 20 | 16 / 16 | 1 |
| enumeration of a hash container (R8) | 190 | 124 / 130 | 53 / 55 | 17 |
| unstable sorts (R11) | 169 | 135 / 136 | 63 / 64 | 18 |
| parsing and formatting of doubles (R17) | 52 | 119 / 137 | 53 / 60 | 16 |
| `Math.Min / Max` of doubles (R4) | 697 | 214 / 228 | 87 / 88 | 29 |

Reading it:

- "Reaches" is necessary for a dependence, not sufficient: `ExactMeshBoolean` reaches libm (F2) and is bit-identical;
  the `float` and `text` rows are wide because a `ToString` or a `float` conversion of a touched type counts;
  `Remesher` reaches it through `TriangleHashGrid.level_size` (a grid size) and is bit-identical on the harness inputs.
  The table is the list of places that need either a measurement or an argument; today they have neither.
- **86 documented types do not reach the platform libm on static edges** (76 not even with `cha`); for those the only
  remaining L1 causes are the closed-world ones (R4 `Min / Max` of zeros, R5 casts, R6 `#if NET`, R17 text, `Math.Round`
  with digits), which no harness case of a documented operation has shown to differ. They are the candidates for a
  bit-exact contract. The measurement agrees with the tool in both directions: every example case that differs across
  platforms belongs to a type that reaches libm, and no case of a type that does not reach it differs.
- The 18 documented types that reach libm and that the harness does not reference: `CappedCylinderGenerator`,
  `ConeGenerator` (both run as `generators.*`, through the base class), `TubeGenerator` (its case only throws: no input),
  `FoldoverFreeParameterization`, `MeshBoolean`, `MeshMeshCut`,
  `MeshChartSegmentation`, `MeshRoundingRepair`, `MeshPostRepairCleanup`, `ParameterizationDistortion`, `Polygon2d`,
  `TriangulationMesh2`, `MeshCurvature`, `MeshEnvelope`, `MeshWindingNumberGrid`, `PartitionedReducer`, `RemeshSizing`,
  `RemesherPro`.
- With `cha` only: `MarchingCubes`, `DualContouring`, `MarchingCubesPro` (any `ImplicitFunction3d` in the library, eg the
  TPMS functions), the 2D quadtrees, `PlanarComplex`, `PlanarHoleFiller`, `TerrainSlopeDesign`: these depend on what the
  caller passes in.
- 61 documented types reach a direct `Parallel.For` or thread: the loops `gParallel.SerialOnThisThread` cannot switch
  (DETERMINISM.md section 6) are on more paths than the four files named there.

The shortest call path to a source is printed per type (`--why <Type> <source>`), which is how F2 was found.

## 5. What can be proven: stage 2 for the exact Boolean

The method is decided (DETERMINISM_PROOF.md sections 4 and 8); the question of plan #1241 is whether stage 2 fits one PR.
Step 1 of the plan, done here in a scratch copy (both files added to the scope section of `.editorconfig`, the `type` line
of `DeterministicApis.txt` removed, `dotnet build geometry3Sharp_netstandard.csproj -c Release -f net10.0`, the log
through `tools/DeterminismHarness/scope_report.py`):

| | stage 2, first build | stage 1 for comparison |
|---|---|---|
| diagnostics | 847 (230 in `ExactMeshArrangement.cs`, 601 in `ExactMeshBoolean.cs`, 16 allow-list lines that are no longer external) | |
| unlisted external members (G3D0001) | 165: 60 of the BCL, 103 of geometry3Sharp outside the scope, 2 constructs | 153 in its first build |
| of the geometry3Sharp members | `MeshRoundingRepair` 36, `DMesh3` 26, `ExactMeshEdgeGroups` 10, `Vector3d` 9, `PredicateStats` 7, `PhaseClock` 3, 9 other types 12 | |
| sites of restricted members (G3D0002) | 147 in 19 entries: `Math.Max` 45 and `Math.Min` 16 of doubles, enumerations 14 + 8, `gParallel.ForRange` 11, `(double)BigInteger` 8, locks 7, `List.Sort` 5, `Volatile.Read` 5, `Interlocked.Increment` 4, `Random` 4, `[ThreadStatic]` 3, `Array.Sort` with items 2, `HashSet` enumeration 2, `Dictionary.Keys` 1 | 82 sites after the stage |
| `GetHashCode` outside a hash implementation | 3 | 0 |
| counted constructs | 51 delegate calls in 13 members, 28 string concatenations in 3, 1 interface call | 21 delegate calls, 29 concatenations |
| `double` to integer conversions (G3D0006) | 7 in 7 members | 6 in 5 |

So the registrations are about the size of stage 1 again, well below the plan's stop rule (twice stage 1). What the
numbers do not show, and the outline has to settle:

1. **The scope boundary at `MeshRoundingRepair`.** With `SafeRounding` (the default) the result of the Boolean is produced
   by `MeshRoundingRepair` (3 259 lines, floating point, its own sorts, hash containers and winding numbers, F3), plus
   `ExactMeshEdgeGroups` (344) and `ExactMeshResultBuilder` (162). Three ways: (a) claim stage 2 for the exact result only
   (`SafeRounding = false`, and `ExactMesh` chains) and list the rounding as an assumed stage 3 member; (b) pin the 36
   members by body hash, which freezes the file without analyzing it; (c) put `MeshRoundingRepair` into the scope, a stage
   of its own (it has no unique-answer argument: every tie-break and every floating-point decision needs an order
   argument). Recommended: (a), with the harness cases as its evidence, and (c) as stage 2b only if the owner wants the
   claim for the rounded result.
2. **F2.** `Math.Acos`, `Math.Asin`, `Math.Cos` are on the list of the 165. Either a `site` line of form P4 with the slack
   argument of section 3.3 and a test that perturbs the three values (the scratch experiment of this study, turned into a
   test hook), or `CoreMath.Acos / Asin / Cos` in `antipodal_tree_value` and G3E0004 scoped to the file. The second removes
   the argument; its cost is three calls per admitted tree node (24 million on the test input, to be measured against the
   Boolean benchmark; `CoreMath` has no `Asin` yet, `asin x = atan2(x, sqrt(1 - x^2))` or `pi / 2 - acos x` are composed
   and not correctly rounded, so a correctly rounded `Asin` port would come first).
3. **P5 (ids in schedule order).** 4 `Interlocked.Increment` sites, 7 locks, 11 parallel loops. The schedule adversary and
   the hash perturbation of #789 change no Boolean result on any case, the new open-input case included. The id adversary
   of the plan (a seeded permutation of the id allocator) is the missing attack; it needs a test hook in the library and is
   therefore not part of this spike.
4. **The four normalizing sorts that no determinism test detects** (DETERMINISM_PROOF.md 5.1) and the two `.Values`
   enumerations are among the 14 + 8 + 5 + 2 sites above: each gets a form or a fix, as planned.
5. **The 45 + 16 `Math.Max / Min` sites.** Stage 1 argued 7 by hand. At this count a helper is cheaper than 61 arguments:
   `MathUtil.MaxExact / MinExact` (they exist, #803) wherever a zero of either sign can be an operand, by a mechanical
   replacement that is output-identical on every input without a `-0` (to be checked on BooleanComparison bit for bit).

**Proposed stage 2 outline** (one L PR, as plan #1241, in this order; each step is a commit that builds):

| step | content | exit check |
|---|---|---|
| 1 | scope on for both files; the 60 BCL members and the pinned geometry3Sharp members listed with reasons; `MeshRoundingRepair` as `[assumed]` (decision 1a) | library builds with G3D0001 clean |
| 2 | R4: `MinExact / MaxExact` at the 61 sites, or a `site` line where the operands are provably not zeros | BooleanComparison and the harness bit-identical before and after |
| 3 | R8, R11: the 29 enumeration and sort sites with forms P2 / P3; the two `.Values` sites and the `merge_points` ties get a fix or an argument; totality tests of the comparers (S2-1) | hand mutant per form fails a named test |
| 4 | R12, R13, R14: the 11 loops, 7 locks, `Interlocked`, `Volatile`, `[ThreadStatic]` restated as forms; the 25 `ExactMesh` entries of `ExactCodeAllowList.cs` point to them | `OrderAdversaryTests`, `ExactBooleanDeterminismTests` |
| 5 | P5: the id adversary hook, run over `boolean.*`, `stage1.*`, `kernel.*`; owner decision G or C only if a result changes | harness equal under 3 id seeds |
| 6 | F2 (decision 2), R9 (`Random` with a seed: an own generator or P4), R10, the 7 casts, the delegate calls (S2-2, S2-3, S2-6) | the perturbation test of F2 |
| 7 | harness `stage2.*`, the ledger of section 5.5 generated, `determinism.yml` green on all legs | as plan #1241 |

**Stage 3 (floating-point operations)** stays what DETERMINISM_PROOF.md 6.1 says: only internal determinism can be
argued, and F1 shows what that is worth (a loop whose appends follow the schedule is exactly form P3 missing its sort). The
reachability table gives its size before any work: `MarchingCubes` reaches no libm on static edges and 2
`ProcessorCount` uses; `Reducer` reaches libm in `add_boundary_and_feature_quadrics`. Not recommended before stage 2;
the cheaper step for floating-point code is the harness-wide adversary this study extends (section 7, option 2).

## 6. Golden masters across runtimes (plan #1287)

**Where the pinned values are today.** In `tests/geometry3Sharp.Tests` (684 files): 20 files pin 136 64-bit hashes, 21
files use fingerprints, 15 golden rows or files, 7 digests; `benchmarks/Meshing2Benchmark/golden` holds the golden rows
of the 2D mesher. `ci.yml` runs the suite on linux x64 (net10.0) and on Windows (net48), so **every pinned value in the
suite already holds on two runtimes, two operating systems and two C libraries on x64**, or CI would be red. What no CI
run covered was a second architecture: the opt-in job `suite-arm64` of the determinism workflow (`workflow_dispatch`,
input `suite`) runs the PR-tier suite on linux arm64, and it is green (section 3.4). Not run: the suite on Windows .NET 10
and on macOS, the two C libraries that differ most from glibc in the harness.

**The tension of the plan, measured.** The plan expects that floating-point operations with `System.Math` break strict
hashes across platforms. On the harness inputs they mostly do not: `Remesher`, `Reducer`, `DualContouring`,
`MarchingCubes`, the repair chain, alpha wrap, solidify, smoothing, deformation, offset, the tet mesher and the terrain
tools are bit-identical on every pair measured (six operating system and architecture combinations, two runtimes)
although they are floating-point code, because their paths use only `+ - * /` and `sqrt` on those inputs. What differs
is confined to transcendental functions whose value reaches a coordinate (eight example cases, section 3.4). So the boundary is not "exact against floating point" but
"reaches a platform libm value or not", and that is checkable by a tool (section 4) instead of by judgement.

**Recommended policy** (for `ACCEPTANCE_STRATEGY.md` section 4 and `docs/QA.md`, after the owner's decision):

| tier | which operation | what the golden master pins | who decides the tier |
|---|---|---|---|
| 1, bit-exact | no platform libm on its reachable set (`DeterminismReach`, `cha`), or only inside a proof scope with an argument (F2) | one digest of the full result, the same on every runtime, OS and architecture | the tool; a new libm edge fails the check |
| 2, bit-exact per libm family | reaches a platform libm value | (a) the combinatorial result and every exact value in one digest for all platforms, where the operation promises them; (b) the coordinates as one digest **per libm family**, recorded from CI, never a tolerance alone. A family is the C library with its architecture and instruction sets (F6), so in practice one digest per CI leg: linux (x64 = arm64), Windows .NET 10 x64, Windows .NET Framework x64; (c) an invariant with a derived bound across families (Hausdorff, volume, angle) | the tool puts it there; leaving tier 2 means `CoreMath` on the path (the libm rule, owner 2026-11-11) |
| not a golden master | a defect of level L0 (F1) | nothing is pinned: the test is serial equals parallel equals adversary, and it stays red or `KNOWN` until fixed | the adversary |

The rule that keeps tier 2 from hiding defects (the risk the plan names): **inside one runtime every operation is tier 1**.
Serial, parallel, repeated runs and adversarial loop orders must agree bit for bit for every operation, with or without
libm; only the comparison across libm families is relaxed, and only to another exact digest. A tolerance appears only in
(c), with its derivation.

How others solve it: Box2D v3 and Rapier get cross-platform determinism by banning FMA contraction and replacing the
platform trigonometry with their own functions (Box2D: hand-written `b2Atan2` and `b2ComputeCosSin`, `-ffp-contract=off`;
Rapier: a feature flag that switches to software math and drops SIMD and parallel solving), which is the `CoreMath` route
with cheaper, not correctly rounded functions; NumPy's test suite compares in units in the last place
(`assert_array_max_ulp`) or relative tolerance where the platform libm is involved, which is tier 2 (c); IEEE 754-2019
clause 11 defines the reproducible subset this library's tier 1 already is.

## 7. Options, ranked

| rank | option | what the owner gets | effort | risk |
|---|---|---|---|---|
| 1 | **Fix F1** (sort the entries in `PackedSparseMatrix.Square`; test: the adversary case, serial equals parallel, processor counts) | the last known L0 defect of a documented operation gone | S / M, one small PR, label `bugfix` | results of `LaplacianMeshSmoother / Deformer` change in the last bits once, to the value of the serial run |
| 2 | **Keep the wide harness as the standing measurement** (this PR: examples and generators as cases, JIT switches, Windows arm64; then the 18 uncovered libm types as examples or cases) | every documented operation measured on every push that touches the harness, new examples covered automatically | done here; follow-up S | none; the workflow grows by one runner and four short runs |
| 3 | **Stage 2 with decisions 1a and 2** (section 5) | the generated claim for the exact Boolean without rounding, with the rounding as a named assumption | L, one PR, about the size of stage 1 | file churn from Boolean PRs (plan #1241); F2's `CoreMath` variant needs an `Asin` port and a benchmark |
| 4 | **Golden-master policy of section 6** with the reachability check as a CI step (a list of tier 1 types that may not gain a libm edge) | the tier is decided by a tool; a new `Math.Sin` on a tier 1 path fails CI instead of a hash on another platform months later | M: docs, one CI step, a baseline file | the `cha` bound puts operations with callbacks in tier 2; a baseline file is one more gate file |
| 5 | **A libm adversary** (DETERMINISM_PROOF.md 6.2: every `Math.Sin` & co. behind a wrapper that moves the result by an ulp in tests) | on one machine, for every case: does a libm ulp reach the result (what F2's experiment did by hand for four sites) | M / L: 165 methods call libm directly; a wrapper in each, behind a test hook | touches many files once; no behaviour change |
| 6 | **`CoreMath` in the generators, the contour loft and the winding number** (F3, F4, F5) | cylinders, cones, discs, lofts and the raw winding numbers identical everywhere; fewer tier 2 operations | M per group, with benchmark numbers (the libm rule asks for a measured difference: section 3.4 has it for the generators) | generator output changes in the last bit once; `Atan2` per triangle in the winding number is hot (1.5 to 4 times per call) |
| 7 | **Stage 2b (`MeshRoundingRepair` in scope) or stage 3** | a claim for floating-point code | L each, larger than stage 2 | no unique-answer argument; likely needs code changes |

**Leaning.** 1 now (it is a defect, small, and the test exists; issue #1314). 2 is in this PR. Then 3 as planned, with
decision 1a, and for F2 the `site` line with the perturbation test first and `CoreMath` only if the benchmark allows it.
4 with or right after stage 2, because the same tool then guards both. Of 6, the contour loft first (a status that
differs is a behaviour difference, F5), then the generators when a caller asks for identical inputs across platforms; not
the winding number. 5 only if 4 leaves too many operations in tier 2
without knowing whether they really differ. 7 not now.

**Decisions asked of the owner**
1. F1: fix now as a `bugfix` PR (results of the three Laplacian classes change in the last bits)?
2. Stage 2 scope: the exact result only, with `MeshRoundingRepair` assumed (1a), or the rounded result too (1c, a second stage)?
3. F2: an argued `site` with a perturbation test, or `CoreMath` in `antipodal_tree_value` (needs `Asin`, a benchmark)?
4. The two-tier golden-master policy of section 6, with the tier decided by `DeterminismReach`?
5. F5: the libm rule for `ContourLoft` / `ContourLoftStack` (their status differs between platforms on one input)?
6. Windows arm64 as a standing leg of `determinism.yml` (it is a measurement leg in this PR: its failure does not fail the run)?

## 8. Limits

- One run per configuration on GitHub-hosted runners; the inputs are small (tens of milliseconds per case). Absence of a
  difference is evidence on these inputs. The reachability table is the complement: it lists where a difference is
  possible at all.
- The example cases use one argument set each; the reflection trace follows public members six levels deep and skips
  members named like timings, counters and messages (by whole words; what else it cannot follow is counted and noted). A result that is only reachable through a method call (not a field or
  property) is not traced unless the example returns it.
- `DeterminismReach` does not follow delegates passed in from outside the library, or reflection; `cha` counts
  implementations an operation is never given; `hash-enum`, `text`, `float` and `parallel` see the direct forms only (the
  tool's README, "Limits"). Six generator cases record only an exception. No native arm64 .NET Framework process was
  measured (F6), and no .NET version other than 10.0.15.
- The reflection trace had one artefact of its own, found in the first cross-platform run: it marked an object seen twice
  by its identity, and .NET returns one shared empty array where .NET Framework allocates, so a result with empty `int[]`
  members (`QualityMesh2`, subsegment markers) looked different between the two. Sequences are no longer identified;
  other artefacts of this kind are possible wherever a case differs only in a count of values.
- The scratch experiments (the one-ulp perturbation of F2, the sort of F1, the stage 2 build) changed library code in a
  scratch copy only; nothing of them is in the PR. Their commands are in the PR description.
- `DOTNET_EnableAVX512F` and `DOTNET_EnableFMA` had no effect on the reported instruction sets in .NET 10.0.15 and prove
  nothing; `EnableHWIntrinsic=0` covers both.

## 9. Sources

Papers and standards as in [DETERMINISM_PROOF.md](../design/DETERMINISM_PROOF.md) section 9 (IEEE 754-2019 clause 11;
Monniaux 2008; Blelloch et al. 2012; Shi et al. 2016 (NonDex); Sibidanov, Zimmermann, Glondu 2022). Practice
(documentation only, no code read): E. Catto, "Determinism", box2d.org, 2024-08 (cross-platform determinism by avoiding
FMA contraction and by own `atan2`, `cos`, `sin`); Rapier user guide, "Determinism" (the `enhanced-determinism` feature);
NumPy `numpy.testing` reference (`assert_array_max_ulp`, `assert_allclose`); Microsoft documentation of
`Math.FusedMultiplyAdd` (one rounding, as opposed to the separate multiply and add). Tool: Mono.Cecil (MIT), used by
`tools/DeterminismReach` to read IL; not part of the library.
