# Exact boolean performance: state and hand-over (October 2026)

This is the hand-over for the performance work on `ExactMeshBoolean` / `ExactMeshArrangement` (with
`ExactTriangulation2`, `ExactPlanarSimplify`, `MeshRoundingRepair` in the boolean path). A new session can continue
from it without the earlier context. Its sources:

- **PRs:** #181 (rounds 1-3, merged) and #183 (round 4).
- **Measurement chapters:** [benchmarks/BooleanComparison/README.md](../../benchmarks/BooleanComparison/README.md),
  section "Profile and performance work", with all tables. This document summarizes them and adds what is not
  there.
- **Scripts:** [benchmarks/BooleanComparison/perf/](../../benchmarks/BooleanComparison/perf/).

## 1. Current profile

State: master after #181 plus #183. Warm runs: the booleans run in one process after a warm-up, the best of 3
runs is counted per case and operation, and union, intersection and difference each count once. 4 cores.

| class | runs | median / case | total | GC pause | alloc / run |
|---|---|---|---|---|---|
| `cherchi` (13 cases) | 39 | 555 ms | 15.0 s | 17 % | 307 MB |
| `cgal` (50) | 150 | 46 ms | 7.3 s | 5 % | 37 MB |
| Thingi10K (32) | 96 | 76 ms | 6.5 s | 0 % | 53 MB |
| n-ary: grid 5 / grid 10 / spheres 20 (one pass) | 3 | 631 / 5001 / 227 ms | 5.9 s | 32 % | 2.7 GB |

Phases as shares of the total time. A GC pause counts in the phase it falls into. Simplify is part of assembly.

| class | setup | trees | pair search | planes | tri-tri | merge | triangulation | classification | assembly | (simplify) |
|---|---|---|---|---|---|---|---|---|---|---|
| cherchi | 7 % | 8 % | 21 % | 6 % | 6 % | 3 % | 16 % | 13 % | 17 % | 5 % |
| cgal | 5 % | 5 % | 29 % | 2 % | 7 % | 5 % | 12 % | 10 % | 16 % | 5 % |
| thingi | 4 % | 3 % | 30 % | 2 % | 15 % | 8 % | 8 % | 8 % | 14 % | 6 % |
| n-ary | 0 % | 0 % | 2 % | 1 % | 15 % | 19 % | 31 % | 30 % | 1 % | 1 % |

**Filter failures** (exact stages / calls, library built with `-p:G3PredicateStats=true`):

| class | orient2d | orient3d | arr.orient | arr.incircle | arr.along | arr.compare |
|---|---|---|---|---|---|---|
| cgal | 10.3 % | 11.6 % | 11.1 % | 0.17 % | 0.05 % | 4.4 % |
| thingi | 7.0 % | 8.2 % | 10.0 % | 0.01 % | 0 % | 3.6 % |
| cherchi | 0 % | 0 % | 11.9 % | 0 % | 0.04 % | 0 % |
| n-ary | 36 % | 15.5 % | 19 % | 31 % | 9.4 % | 8.8 % |

- On real inputs, 99 % of the `arr.orient` exact stages are triples where one point is constructed on the line
  through the other two (see `claude/collinear-shortcut` below).
- `orient2d` / `orient3d` on `cgal` / Thingi fail where the inputs have exact coplanarities (CAD models).
- In the n-ary grid, `arr.incircle` fails on exactly cocircular grid points.

**Cold runs** (one process per boolean, as `compare.py` runs the tools): `cgal` median 199 ms, Thingi10K 325 ms,
5 small `cherchi` cases 846 ms (before the work: 602 / 917 / 2166 ms). A cold boolean takes about 3 times as long
as a warm one. The difference is the JIT: generic runtime code over the library's value types starts unoptimized.

**Scaling** (after round 2; `perf/scaling.sh`), 1 / 2 / 4 cores:

| inputs | 1 core | 2 cores | 4 cores | speedup 1 -> 4 |
|---|---|---|---|---|
| 5 cherchi cases | 6.1 s | 3.9 s | 2.9 s | 2.08 |
| 12 Thingi10K cases | 6.9 s | 4.6 s | 3.3 s | 2.09 |
| grid 5 | 1.47 s | 1.01 s | 0.73 s | 2.01 |
| bunny 200k + cow 200k | 4.8 s | 3.6 s | 2.8 s | 1.68 |

Merge, parts of the classification and the assembly are serial, and the GC on the large inputs is serial as well.

### How to reproduce

Data: `BDATA` holds `cases/<class>/<case>/{A,B}.off`, one directory per case.

- **Fetching the meshes:** `python3 benchmarks/BooleanComparison/data/fetch.py cgal|cherchi <dir>`. Thingi10K
  comes from `benchmarks/MeshRepairBenchmark/fetch_data.py`.
- **Writing the cases:**
  - `BooleanComparison export <mesh> $BDATA/cases/<class>/<name>` writes the mesh and a rotated, shifted copy.
    This is used for `cgal`, Thingi10K and the single `cherchi` meshes.
  - `BooleanComparison pair <A> <B> $BDATA/cases/cherchi/<A>+<B>` writes the `cherchi` pairs.
- **Case lists:** `perf/cases-*.txt`. A Thingi10K case is the file id.
- **Do not commit the meshes:** they have their own licenses.

```sh
dotnet build benchmarks/BooleanComparison -c Release -o ~/tool_new        # a frozen copy per state
perf/run_classes.sh ~/tool_new out_new          # warm, all classes (about 15 min)
python3 perf/summary.py out_old out_new         # per class: median, total, GC, alloc, phases
perf/exact_hashes.sh ~/tool_new hashes_new.jsonl && python3 perf/hashes.py perf/exact_hashes_reference.jsonl hashes_new.jsonl
perf/scaling.sh ~/tool_new scale_new            # 1 / 2 / 4 cores
perf/cold.sh ~/tool_new cold_new.jsonl cases-cgal.txt
dotnet build benchmarks/BooleanComparison -c Release -p:G3PredicateStats=true -o ~/tool_stats   # filter failures
```

Single cases: `BooleanComparison profile --repeat 5 --ops union,difference <case dir>...`. It prints one JSON line
with `phases_ms`, `phases_alloc_mb`, `gc`, `counts`, `predicates` (stats build) and `hash`.

### Measuring: what we learned

- **Compare only within one session.** The machine's speed drifted by 20-30 % between sessions; the same state
  measured 22.0 s and 15.0 s on `cherchi`.
- **Alternate the tools and run two rounds** (A B A B). The noise is ±5 % on class totals and up to ±10 % on a
  single large case.
- **Never conclude from one case.** The collinear shortcut gave -10..-19 % on fertility + spheres in one
  comparison, but nothing on the class. That comparison had the Int384 spike underneath; see section 2.
- **GC pauses move between phases.** A phase that gets faster can make a later one slower in the table. Look at
  the total and at `gc.pause_ms`.
- **`dotnet-trace` sampled thread time:**
  - Converted to speedscope, it is unreliable when the GC dominates: the threads sit in `PollGC`, and the stacks
    are attributed to the wrong frames.
  - Instead, use:
    - `dotnet-trace collect --profile gc-verbose` with a TraceEvent analyzer for allocation ticks per type and
      frame and the GC events (pause, promoted bytes per generation);
    - `dotnet-gcdump` during a run for the live objects (`report`, sorted by count);
    - direct Stopwatch instrumentation of a phase.
- **Temporary instrumentation must not read environment variables in the hot path.** `GetEnvironmentVariable` per
  call doubled the pair search; read it into a `static readonly` field.

The TraceEvent analyzer used:

<details><summary>alloc / GC analyzer (console app, package Microsoft.Diagnostics.Tracing.TraceEvent 3.1.13)</summary>

```csharp
using System; using System.Linq; using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing; using Microsoft.Diagnostics.Tracing.Etlx; using Microsoft.Diagnostics.Tracing.Parsers.Clr;
static class P {
  static void Main(string[] a) {      // P <trace.nettrace>: bytes by type and innermost g3 frame; P <trace> x: GC events
    string etlx = TraceLog.CreateFromEventPipeDataFile(a[0]);
    using var log = new TraceLog(etlx);
    if (a.Length > 1) {
      double start = 0; int gen = 0;
      foreach (var ev in log.Events) {
        if (ev is GCStartTraceData s) { start = s.TimeStampRelativeMSec; gen = s.Depth; }
        else if (ev is GCEndTraceData e) Console.WriteLine($"{start,10:F0} gen{gen} {e.TimeStampRelativeMSec - start,7:F1} ms");
        else if (ev is GCHeapStatsTraceData h) Console.WriteLine($"   promoted g0 {h.TotalPromotedSize0 >> 20} g1 {h.TotalPromotedSize1 >> 20} g2 {h.TotalPromotedSize2 >> 20} MB");
      }
      return;
    }
    var byType = new Dictionary<string, long>(); var byFrame = new Dictionary<string, long>();
    foreach (var ev in log.Events)
      if (ev is GCAllocationTickTraceData d) {
        byType[d.TypeName ?? "?"] = byType.GetValueOrDefault(d.TypeName ?? "?") + d.AllocationAmount64;
        for (var c = ev.CallStack(); c != null; c = c.Caller) {
          string n = c.CodeAddress.FullMethodName;
          if (string.IsNullOrEmpty(n) || !n.Contains("g3.")) continue;
          byFrame[n] = byFrame.GetValueOrDefault(n) + d.AllocationAmount64; break;
        }
      }
    long tot = byType.Values.Sum();
    foreach (var kv in byType.OrderByDescending(k => k.Value).Take(25)) Console.WriteLine($"{100.0 * kv.Value / tot,6:F1}% {kv.Key}");
    foreach (var kv in byFrame.OrderByDescending(k => k.Value).Take(30)) Console.WriteLine($"{100.0 * kv.Value / tot,6:F1}% {kv.Key}");
  }
}
```
</details>

## 2. Everything tried

The gains are the measured gains at the time, from the README tables. All kept changes are bit-identical. Round 1
was measured as a whole; the effects of its single changes were not separated, except where given.

**Round 1** (#181): before -> after, warm, median per case: cherchi 1208 -> 685 ms, cgal 67 -> 56 ms, Thingi
120 -> 106 ms. Cold: 2.3-2.8×.

| change | result | kept |
|---|---|---|
| `AggressiveOptimization` (`core/JitHints.cs`) on the pipeline and on `DMesh3`'s append path; loop bodies as named methods instead of lambdas | most of the cold gain (cold 2.3-2.8× for all of round 1); it does not stop inlining (checked in the disassembly) | yes |
| own stable LSD radix sort (11-bit digits) for the edge groups instead of `Array.Sort(long[], int[])` | the generic sort was 14 % of a cold boolean's main thread | yes |
| merge clusters by hashing the exact canonical point (sorting only for clusters > 64 with a point beyond the double range) | merge 5.8 -> 1.0 s on grid 10, together with the shared constructions | yes |
| shared constructions of coplanar triangles: bucket by the rounded plane approximation, exact proportionality by 2×2 minors, at most 8 per bucket | grid 10: 656k points -> 143k; 13.7 -> 5.7 s | yes |
| exact stages of the arrangement's `Orient` / `InCircle`: the adaptive double path for integer points; `InCircle` as a 3×3 determinant | fewer `BigInteger` operations | yes |
| box tree on arrays, queries without allocation, trees built in parallel; pair search in parallel (`gParallel.ForRange`) | part of round 1 | yes |
| input triangles built in parallel; degenerate test along the largest normal component first | part of round 1 | yes |
| classification in parallel (one `Random` per patch, exact winding numbers) | part of round 1 (scaling 1.55-1.82 -> 1.68-2.09) | yes |
| assembly: arrays instead of dictionaries; CSR incidence in `ExactPlanarSimplify`; `DMesh3.FindEdge` without an iterator; no checks that cannot fail | part of round 1 | yes |
| unboxed id enumeration (`RefCountVector.Indices()`), presized collections, lazy `PlaneData`, reused triangle arrays | with round 1: cherchi 467 -> 322 MB per run | yes |

**Round 2** (#181):

| change | result | kept |
|---|---|---|
| `ReadyToRun` (library precompiled) | same cold times: methods with the JIT hint are not precompiled, and the runtime's generic code over library types is not in the image | no (documented) |
| application setting `TieredCompilation=false` (or `TieredPGO=false`) | cold booleans 1.4-1.9× faster | an application's choice; documented in the README |

**Round 3** (#181):

| change | result | kept |
|---|---|---|
| `InputTriangle` without its own `int[3]` (`Index3i` field), input coordinates in one array of the arrangement | gen0 survivors 103 -> 64 MB; cherchi -8..-10 %, GC 27 -> 22 % | yes |
| closures of `prepare_triangle` / `triangulate` only for touched triangles | allocations -5 % | yes |
| larger chunks of the point lists (on the LOH) | slightly slower (more gen2 collections) | no |

**Round 4** (#183):

| change | result | kept |
|---|---|---|
| exact edge planes only when used (only their approximations are needed on real inputs) | live digit arrays 1.3-1.65M -> 0.6M; cherchi -3..-7 % | yes |
| `ExactPlanarSimplify.group_of`: gcd one component after the other, no division by 1 | simplify -20 % on fertility + spheres | yes |
| `MutationConfigTests` | guards the shard offsets | yes |

**Spikes and tried variants:**

| change | result | kept |
|---|---|---|
| **SIMD filter spike**: `Orient3d` of three points in `Vector256` lanes, same IEEE operations, no FMA. Branch `claude/boolean-simd-spike`, head `b4380c1` | filter ~10 % of samples; pair search -11 %, total ~2 % (noise); a scalar batched version was slower | no |
| **`Int384` spike**: the planes of input triangles in a fixed-width integer, .NET 8+ (`InlineArray`); checked against `BigInteger` on 1M operands. Branch `claude/int256-spike`, head `76863fd` (which also contains the collinear shortcut) | measured without the shortcut: planes phase 285 -> 53 ms on fertility + spheres, allocations -14 %, total about 0 % alone | no |
| **Collinear shortcut**: `Orient` = 0 without exact points when a point is constructed on the line of the other two. Branch `claude/collinear-shortcut`, head `09ca96d`, reviewed, 4/4 mutants killed | 99 % of the exact `Orient` stages on real inputs; GC pause on fertility + spheres -18 %, time 0 % alone. Classes: cherchi ±2 %, cgal -1 %, thingi -2 % | no: below the 10 % bar |
| **`Int384` + collinear shortcut together** (the head of `claude/int256-spike`) | fertility + spheres -5..-7 % (sum of the three operations, two rounds); not measured on the classes | candidate 1 below |
| reused buffers in the tri-tri intersection (pooling); `Span` / `stackalloc` / `ArrayPool` | no gain: short-lived garbage is cheap. The GC cost is the long-lived objects, which pooling does not shorten, and `BigInteger` allocates its digits itself | no |
| `readonly` members on the core structs | nothing to measure: the hot code makes no defensive copies | no |
| `ExactTriangulation2` on flat arrays (twice, also on the triangulation-heavy inputs) | no gain | no |
| GC settings of an application | server GC: -23 % on fertility + spheres, +5..25 % on small inputs; `DOTNET_GCgen0size=0x40000000`: -9 % there, neutral elsewhere; `gcConcurrent=0`: nothing | an application's choice |

## 3. Open ideas, ranked

Expected gain against effort. Each comes with its evidence and its risks.

**1. Exact arithmetic without `BigInteger` (fixed width), building on `Int384` and the collinear shortcut.**
- *Evidence:*
  - n-ary: 30 % classification, 31 % triangulation and 32 % GC are `BigInteger` work (cocircular and coincident
    grid points).
  - fertility + spheres: 32 % of the bytes are `BigInteger` digits, and 0.5-0.7M digit arrays survive: the exact
    plane `P` (4 per plane) and the exact input corners (`ExactBox`, 3 each).
  - The two spikes together already gave -5..-7 % there.
- *Steps:*
  1. Start from `claude/int256-spike`, which has both changes. Take the reviewed version of the shortcut, with its
     tests, from `claude/collinear-shortcut`. Measure the classes; they must clear the bar.
  2. Keep `P` and the corners in `Int384`, converting to `BigInteger` only in the rare exact stages.
  3. `line_direction` (normal × plane normal, up to 2^449) needs 512 bits.
  4. Implicit points need wider types: `EdgePlane` about 270+ bits, `Planes` more.
- *Risks:*
  - Every bit bound must be proven. Coordinates are limited by the grid width, 62-70 bits on real inputs (not 53:
    coordinates near 0 have small exponents). Keep a `BigInteger` fallback above the limit.
  - .NET's `(double)BigInteger` *cuts* to 53 bits (it does not round to nearest); `Int384.ToDouble` copies that,
    and the error bounds assume it (relative error 2^-52).
  - netstandard2.0 needs a fallback, or the same code without `InlineArray`.
  - Determinism is unaffected, because all of this is exact integer arithmetic.
  - The work falls under the mandatory review (CLAUDE.md, `docs/REVIEW_CHECKLIST.md`) and the mutation shard of
    `math/` and the arrangement.
- *Effort:* high; expected gain: large on n-ary and intersection-heavy inputs, small on `cgal` / Thingi.

**2. Indirect predicates instead of rational constructions** (the architectural difference to Cherchi et al.).
- They evaluate the predicates on implicit points (LPI, TPI) directly with floating-point filters and expansion
  arithmetic, without building a rational point: no allocation, no gcd.
- The reference is Attene, "Indirect predicates for geometric constructions" (2020), and Cherchi et al. 2022.
- *Risks:* new error bounds per predicate; a large change.
- License: implement from the papers; check the licenses before reading any code (CLAUDE.md: only MIT, Apache or
  BSD may be ported).
- *Effort:* very high; gain: the largest.

**3. Pair search within a mesh** (cgal / Thingi 29-30 % of the time).
- *Evidence* on bunny 200k + cow 200k: of 170 ms, the box queries of each triangle in its own mesh take about
  65 ms. The exact tests of the 2.4M overlapping pairs take about 90 ms. Of the pairs, 1.84M share a vertex, 0.6M
  an edge, and 120k reach `ExactTriangleIntersection.IntersectsBeyondShared`, which is 17 % of `cgal/fandisk`.
- *Ideas:*
  - a self-join of the box tree, pairs i < j in one traversal;
  - in `touch_only`, reuse the orientations computed by the one-sided tests instead of redoing them in
    `IntersectsBeyondShared`. Use a variant of its own: `IntersectsBeyondShared` is shared with `Reducer`,
    `MeshRepairMetrics` and `DualContouring`.
- *Risks:* exactness (the variant needs its own tests against the original); the pair order does not matter
  (pairs are intersected in parallel).
- *Effort:* medium; gain: about 5-10 % on cgal / Thingi.

**4. Serial phases and scaling** (1 -> 4 cores only about 2×).
- Merge, the serial bucket scan of the planes phase, parts of the classification and the assembly (union-find,
  `DMesh3` construction) run on one thread.
- *Ideas:*
  - a bulk constructor of `DMesh3` from the grouped edges (2-4 % alone);
  - a parallel union-find;
  - the bucket scan in parallel per bucket.
- *Risks:* determinism (point ids are assigned in parallel; output order by `ComparePoints`). The determinism
  tests must cover every new parallel path.
- *Effort:* medium; gain: grows with the core count.

**5. Remaining GC on large inputs** (cherchi 17 %).
- The 400k `InputTriangle` objects (88 bytes, 35 MB survivors) and about 8 collections per touched triangle
  (`HashSet` points, `List` segments, coplanar, output lists).
- *Ideas:*
  - triangles as a struct array; this changes all users of `Triangles`: boolean, simplify, tests;
  - the per-triangle lists in CSR arrays after the intersection phase.
- *Effort:* medium; gain: 3-5 % on large inputs.

**6. n-ary specifics.**
- `arr.incircle` fails on 31 % (exactly cocircular grid points), `orient2d` on 36 %.
- A construction-based shortcut like the collinear one, for points known to be cocircular, would need a
  combinatorial argument. A symbolic perturbation would change the output, so it is not acceptable.

**7. Cold start.**
- Only applications can set the JIT (`TieredCompilation`).
- The library could avoid generic runtime code over its value types on the hot path (an own hash map for
  `PointKey` instead of `Dictionary<PointKey, int>`), so that tier-0 code matters less.
- The gain is unknown; measure with `perf/cold.sh`.

## 4. Guard rails

- **Bit-identical results:**
  - `perf/exact_hashes.sh` runs 263 booleans: all `cgal` and Thingi10K cases and 5 `cherchi` cases, each with
    union, intersection and difference, plus grid 5 and spheres 20.
  - Each run hashes the result mesh (vertex bits, triangles, ids) and the exact result vertices.
  - `perf/hashes.py perf/exact_hashes_reference.jsonl new.jsonl` must report 0 differences after every change. The
    reference is master + #183 on x64 Linux.
  - A change that alters results on purpose is a different kind of PR: say so, and give the reason.
- **Determinism:**
  - `ExactBooleanDeterminismTests` (`SerialEqualsParallel_BitIdentical` with `gParallel.SerialOnThisThread`, and
    repeated runs);
  - the invariance tests over `Similarity.All()` (`InvarianceTests`, among them the coplanar grid);
  - runs on 1 / 2 / 4 cores give the same hashes.
- **Bounds:**
  - Every new or changed error bound or bit-width bound gets the independent review with
    `docs/REVIEW_CHECKLIST.md`, including a check of the bound itself (CLAUDE.md).
  - The filter-bound tests: `ExactFilterBoundTests`, and in `ExactImplicitPointTests` the approximations against
    their bounds, and the exact stages against reference determinants.
- **Mutation:**
  - The `exact-mesh` workflow runs shards (`tests/mutation/stryker-mesh-*.json`) split at the character offsets of
    `build_triangles` (`ExactMeshArrangement.cs`) and `front_winding` (`ExactMeshBoolean.cs`), each at its
    attribute line.
  - `MutationConfigTests` fails when an edit above them moves the offsets. Recompute them as described in
    `tests/mutation/README.md`.
  - The shards must stay above their `break` thresholds.
  - New test classes go into a shard's filter and into the workflow's `paths` / `mesh` pattern.
- **Before every push:** the CI steps of CLAUDE.md.

## 5. The gap to CGAL, Manifold and Cherchi et al.

Their times are from the earlier comparison run (README, "Results"; not rerun since). For a reliable statement,
rerun `compare.py` with all tools on one machine.

| | ours now | CGAL EPECK | Manifold | Cherchi et al. |
|---|---|---|---|---|
| cgal, median per case | warm ~46-60 ms, cold ~200 ms (was 532 ms cold) | 62 ms | 41 ms | 113 ms |
| cherchi, median per case | warm ~555-700 ms (was 3631 ms cold) | 645 ms | 304 ms | 1363 ms |

- **Warm**, we are about on par with CGAL (which runs on one core; we use four). Manifold is 1.5-2.5× faster, and
  we are faster than Cherchi et al.
- **Cold**, we are about 3× slower than CGAL and 5× slower than Manifold. The JIT start explains most of it; the
  C++ tools have none.

Where the rest comes from, as far as the profiles show:

1. **Exact constructions as `BigInteger` rationals.**
   - CGAL EPECK constructs lazily, with interval filters.
   - Cherchi et al. never construct implicit points: indirect predicates with expansions, no allocation.
   - We allocate the digits, and long-lived ones cost GC time: 17 % on cherchi, 32 % on n-ary.
2. **The GC of a managed runtime** on large live sets; the C++ tools have none.
3. **Serial phases.** Scaling is about 2× on 4 cores. Manifold parallelizes almost everything.
4. **Manifold works in floating point** with symbolic perturbation and no exactness guarantee. It does a different
   amount of work and is not a target for exact results.

## Branches

| branch | head | content |
|---|---|---|
| `claude/boolean-simd-spike` | `b4380c1` | SIMD `Orient3d` filter of three points |
| `claude/int256-spike` | `76863fd` | `Int384` planes (and an earlier copy of the collinear shortcut) |
| `claude/collinear-shortcut` | `09ca96d` | collinear-by-construction shortcut in `Orient`, with review fixes and tests (on top of #183's state) |

Keep these branches; do not delete them.
