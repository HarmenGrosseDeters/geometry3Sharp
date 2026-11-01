# Fixed-width exact points in the arrangement (design, for approval)

Status: **design, not yet implemented.** Tier L ([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md)): the owner approves this
document before any code is written; every bit bound in section 5 gets an independent review.

## 1. Goal and hypothesis

**Goal.** Cut the GC pauses of the n-ary boolean by storing the arrangement's exact points in fixed-width integers
instead of `BigInteger`, with bit-identical results.

**Evidence** (from [perf/EXACT_BOOLEAN_PERFORMANCE.md](../perf/EXACT_BOOLEAN_PERFORMANCE.md) and the step-2 chapter
of `docs/design/PREDICATE_COMPILER.md` in #221), `BooleanComparison nary grid 10 onepass`:
- GC pauses are about 2.8 s of about 6.5 s. They stayed at 2.8 s when the fixed-width `InCircle` of #221 cut the
  allocations by 30 % (8.3 -> 5.8 GB). Short-lived garbage is cheap; the pauses come from **long-lived** objects.
- The long-lived ones are the exact points: an `ExactBox` object per computed point, holding an `ExactPoint3` of four
  `BigInteger`s, each with its own `uint[]` digit array once it exceeds 31 bits. That is up to five heap objects per
  point that every collection has to mark, and copy while they are young.
- The multiplications themselves are under 1 % of the time. At about 100 bits, `BigInteger` per predicate is only
  about 1.5× slower than generated fixed-width code, and loading `BigInteger` into limbs (`TryWriteBytes`, about
  23 ns per value) eats much of the difference. Points stored as limbs need no loading at all.
- Measured sizes on grid 10: coordinates ≤ 109 bits, weights `W` ≤ 54 bits. In the exact boolean tests: up to 211 /
  155 bits. So a `BigInteger` fallback is required.

**Hypothesis.** With the exact points as reference-free structs in chunked arrays, the live object graph of the
arrangement shrinks by up to five objects per exact point, the GC pause drops substantially, and the predicates
that read the points (`Orient`, `InCircle`, `ComparePoints`) no longer allocate. Together that makes grid 10 at least
10 % faster (the bar of the performance hand-over).

**Stop rule.** If `nary grid 10 onepass` does not get at least 10 % faster in total (median of alternating A/B
rounds, section 7), stop, write the numbers and the finding into this document, and do not merge library code.

## 2. Scope and non-goals

**In scope** (`mesh_ops/ExactMeshArrangement.cs`, the few call sites in `mesh_ops/ExactMeshBoolean.cs`, one new
internal file in `math/`):
- storage of the exact points of the arrangement (`exactPoints`, `ExactBox`);
- the index of rational points (`AddPoint`, sharded by #222) and the grouping in `merge_cluster`;
- the exact stages of `Orient`, `InCircle` and `ComparePoints` on fixed-width points;
- `front_winding` reading points without materializing them, if step 4 shows it is needed.

**Non-goals:**
- **Public API.** `ExactPoint3` (public) keeps its storage and all its members. The fixed-width form is a new
  *internal* type next to it. `ExactMeshArrangement.Exact(int)` keeps returning `ExactPoint3` (materialized on
  demand), as does `ExactMeshBoolean.ResultExactVertices`. Package validation against 1.0.324 must stay green.
- **Results.** Bit-identical: the arithmetic is exact either way. Checked as in section 7.
- **Other `BigInteger` users stay as they are**: the constructions (`edge_plane_exact`, `segments_exact`,
  `planes_exact`, `centroid`; their intermediates are short-lived and their *results* are converted once), the
  triangle planes `InputTriangle.P` (the `Int384` spike, idea 1 step 2, is a separate topic), `compare_along`
  (its direction has up to about 450 bits), `ToInputExact` / `ToInputVector`, and `MeshEnvelope`.
- **No dependency on #221.** Its generator (`tools/PredicateCompiler`, `ipredicate`) and its `IntegerRuntime` are a
  reference for the bit-bound method, the limb conventions and the portable paths. The spike writes its own small
  internal limb code; if #221 is merged later, the two can be unified.
- **netstandard2.0** keeps building and running: no `Span`, no `Int128`, no `Math.BigMul` without a portable
  replacement (section 6).

## 3. Current state (master + #222)

```
exactPoints : ChunkedList<ExactBox>      // per point id; null until the exact point is needed
ExactBox    : class { ExactPoint3 P; bool IsDouble; Vector3d D; }
ExactPoint3 : struct { BigInteger X, Y, Z, W; }   // canonical: W > 0, gcd = 1
rationalIndex : Dictionary<HashedPoint, int>[64]  // HashedPoint = ExactPoint3 + its hash (#222)
```

Readers of the exact points, with what they need:

| reader | needs | frequency on n-ary |
|---|---|---|
| `Orient` exact stage | `Det3` of rows `(x_i, x_j, W)` | 21 % of 18.5M calls exact |
| `InCircle` exact stage | `incircle_row` + `Det3` | 31 % exact, 1.4M via `BigInteger` |
| `ComparePoints` exact stage | `α·p W'`, then `x W'` per axis | 8.8 % exact |
| `merge_cluster` | equality and hash of points | per cluster member |
| `AddPoint` | equality and hash | per rational point (centroids of the classification) |
| `compare_along` | `d·p W'` with a `BigInteger` direction | 9.4 % exact |
| constructions, `centroid` | `BigInteger` arithmetic | once per constructed point |
| `front_winding`, `orient3d` (boolean) | doubles of the points; exact `Det4` when the filter fails | per ray-triangle candidate |
| `ToInputExact`, `PointToVector`, `ResultExactVertices` | `BigInteger` | output only |

## 4. Design

### 4.1 The fixed-width point

A new internal struct (working name `FixedPoint3`, file `math/FixedPoint3.cs`):

```csharp
internal struct FixedPoint3      // no reference fields: arrays of it are not traced by the GC
{
    // X, Y, Z, W as 128-bit two's complement, two 64-bit limbs each, little-endian (lo, hi)
    public ulong X0, X1, Y0, Y1, Z0, Z1, W0, W1;
    public Vector3d D;            // the coordinates as doubles iff Flags has IsDouble (as ExactBox.D)
    public byte Flags;            // IsDouble, WIsOne
}
```

- **Admission rule.** A canonical `ExactPoint3` is stored in fixed width iff every component `v` satisfies
  `|v| ≤ 2^127 − 1` (so `−2^127` is excluded and negation never overflows). Otherwise the point keeps today's
  `ExactBox` (the fallback). The rule depends only on the value, so **equal points always get the same
  representation**: equality and hashing never have to compare a fixed point with a fallback one.
- **Why two limbs.** It covers the grid (109 / 54 bits) and the input points of real inputs (62-70 bits) with a
  margin, and keeps the predicate kernels short. Step 0 measures the share of exact points that fit 2, 3 and 4
  limbs on grid 5 / 10, spheres 20 and the exact boolean tests; if 2 limbs leave a significant share of the n-ary
  points in the fallback, the width becomes 3 limbs and section 5 is redone for it before any code.
- **Size.** 64 bytes of limbs + 24 bytes `D` + flags = 96 bytes (padded) per *computed* exact point.

### 4.2 Storage and publication (thread safety)

Points are computed lazily and concurrently (`exact_box` today publishes a reference; two threads may compute the
same point, and both store equal values). A 96-byte struct cannot be published atomically, so:

```
exactRef   : ChunkedList<int>          // per point id: 0 = not computed, k > 0 = fixed slot k - 1, k < 0 = fallback -k - 1
fixedStore : ChunkedList<FixedPoint3>  // dense; slots taken with Interlocked.Increment
fallback   : ChunkedList<ExactBox>     // dense; the points beyond 2^127
```

- A writer takes a fresh slot, writes the whole payload, then publishes the slot with `Volatile.Write` on
  `exactRef[id]` (release). A reader does `Volatile.Read` (acquire) and reads the payload only after a non-zero
  value. Two racing writers each take their own slot; one slot is wasted, both hold the same value, and either
  published index is valid. No payload is ever written after it was published.
- `ChunkedList` needs an index-level `Volatile` read/write (a small internal addition) and, for the store, a
  smaller chunk: 2048 × 96 bytes would put every chunk on the large object heap (round 3: larger chunks on the LOH
  were slower). A `ChunkedList` with a type-specific shift (512 × 96 = 48 KB) keeps them below the threshold.
- The store's chunks hold no references, so gen0/gen1 collections neither trace nor copy their contents once they
  are promoted; the per-point objects disappear for every point that fits.

### 4.3 Conversion and materialization

- `BigInteger` -> limbs once per computed point (after the gcd reduction in `ExactPoint3.Create`): on .NET by
  `TryWriteBytes` into a stack buffer, on netstandard2.0 by `ToByteArray` (one short-lived allocation per point, not
  per predicate as in #221).
- limbs -> `ExactPoint3` on demand (`Exact(int)`): `new BigInteger(ReadOnlySpan<byte>)` on .NET, `byte[]` on
  netstandard2.0. Materialized values are short-lived garbage. The readers that stay on `BigInteger` (constructions,
  `compare_along`, output) pay this instead of a field read; step 3 measures whether that matters.
- Canonical form: the reduction stays in `BigInteger` (`Create`); fixed points are only made from reduced values,
  so equality of limbs is equality of points.

### 4.4 Index and grouping

- `AddPoint(ExactPoint3 p)`: if `p` fits, the key is a reference-free `FixedKey` (the 8 limbs) with a hash over
  the limbs, in its own 64 shards; otherwise the #222 path (`HashedPoint`) unchanged. Since the representation is
  a function of the value, the two indexes never hold the same point.
- `merge_cluster`: groups by a comparer on point ids (`Equals` / `GetHashCode` read the stored representation)
  instead of `Dictionary<ExactPoint3, List<int>>`. The result does not depend on the hash function or on the
  enumeration order (each group takes its smallest key); the review checks that again.

### 4.5 Predicates on fixed points

When all points of a call are fixed (the common case), the exact stage runs on limbs; if any is a fallback point,
the call materializes all of them and runs today's `BigInteger` code (identical sign, so identical results). The
`IsDouble` path (adaptive double predicates) stays first, as today.

- **Kernels:** small hand-written two's complement routines on `stackalloc`ed `ulong*` buffers (unsafe is already
  enabled): add / sub / multiply (schoolbook, with lengths trimmed to the used limbs at run time) and sign. Buffers
  are sized by the static bounds of section 5; every operation also checks at run time that its result fits its
  buffer and throws otherwise (as #221's `IntegerRuntime`), so a wrong bound fails loudly, never silently.
- `Orient`: `Det3` of `(x_i, x_j, W)`, expanded along the first row as `ExactPoint3.Det3` does. The order does not
  matter for the bounds: all nine entries, `W` included, are below 2^127.
- `InCircle`: the rows `(A s, B s, A² + B²)` of today's code (`A = x w_d − x_d w`, `s = w w_d`; the `W = 1` shortcut
  of `incircle_row` kept, decided per row), then `Det3` along the first row, as today. Section 5 bounds both the
  mixed minors of that order and the pure ones.
- `ComparePoints`: `(α·p_a) W_b` vs `(α·p_b) W_a`, then per axis `x_a W_b` vs `x_b W_a`, with `α_k` the existing
  `AlphaInt` (integers < 2^53).
- Optional, only if step 3 shows the classification regressing: `front_winding` gets the grid doubles of a point
  from the limbs (`GridDouble(id)`, bit-identical to today's `grid_double(Exact(id))`, which uses `(double)BigInteger`
  truncation and `ToDouble`) and materializes the points only when `orient3d_filtered` fails.

## 5. Bit bounds (each to be reviewed independently)

Inputs of a fixed point: every component, `W` included, `|v| ≤ 2^127 − 1 < 2^127`. (`W ≥ 1` is needed for the
sign arguments, `s > 0`, not for the bounds.) A result of magnitude `< 2^b` needs `b + 1` bits in two's complement,
i.e. `⌈(b + 1) / 64⌉` limbs. Every bound below is `< 2^b` with `b + 1 ≤ 64 · limbs`, so no buffer ever holds
`−2^(64 limbs − 1)`, and negation or absolute values never overflow.

| value | bound | limbs |
|---|---|---|
| product of two components | `< 2^254` | 4 |
| `Orient`: 2×2 minor `x_b y_c − x_c y_b` | `< 2^255` | 4 |
| `Orient`: term `W_a · minor` | `< 2^382` | 6 |
| `Orient`: partial sum of two terms | `< 2^383` | 6 |
| `Orient`: `Det3` = sum of three terms | `< 3 · 2^382 < 2^384` | **7** |
| `InCircle`: `A = x w_d − x_d w` (and `B`) | `< 2^255` | 4 |
| `InCircle`: `s = w w_d` | `< 2^254` | 4 |
| `InCircle`: `A s`, `B s` | `< 2^509` | 8 |
| `InCircle`: `A²`, `B²` | `< 2^510` | 8 |
| `InCircle`: `l = A² + B²` (≥ 0) | `< 2^511` | 8 |
| `InCircle`: product `(A s)·(B s)'` (pure) | `< 2^1018` | 16 |
| `InCircle`: product `(A s)·l'`, `(B s)·l'` (mixed, first-row order) | `< 2^1020` | 16 |
| `InCircle`: pure 2×2 minor | `< 2^1019` | 16 |
| `InCircle`: mixed 2×2 minor | `< 2^1021` | 16 |
| `InCircle`: term (`A s` or `B s` times a mixed minor, or `l` times a pure one) | `< 2^1530` | 24 |
| `InCircle`: partial sum of two terms | `< 2^1531` | 24 |
| `InCircle`: `Det3` = sum of three terms | `< 3 · 2^1530 < 2^1532` | **24** |
| `ComparePoints`: `α_k x_k` | `< 2^180` | 3 |
| `ComparePoints`: `α·p` and its partial sums (`α_k ≤ 2^53`) | `< 3 · 2^180 < 2^182` | 3 |
| `ComparePoints`: `(α·p) W'` | `< 2^309` | **5** |
| `ComparePoints`: `x W'` | `< 2^254` | **4** |

Notes for the review:
- `AlphaInt[0] = 1.0 · 2^53 = 2^53` exactly, so `α_k ≤ 2^53`, not `< 2^53`; `|α·p| ≤ 3 · 2^53 · (2^127 − 1) < 2^182`.
- The `W = 1` shortcut of `incircle_row` is decided per row, so one determinant can mix shortcut and general rows.
  A shortcut row is the general row with `s = 1`, so the general bounds cover the mix. With all four `W = 1`:
  `A < 2^128`, `l < 2^257`, determinant `< 2^516`.
- Grid-sized inputs (109 / 54 bits) use far fewer limbs than the buffers; the run-time trimming makes the cost
  follow the actual sizes. For `InCircle` with `|x| < 2^109`, `W < 2^54` the bound is `< 3 · 2^874 < 2^876`
  (14 limbs); the measured maximum on grid 10 was 833 bits.
- If step 0 chooses 3 limbs per component, every row of this table is recomputed (components `< 2^191`: `Orient`
  10 limbs, `InCircle` `< 2^2300`, 36 limbs).
- Reviewed independently once (all buffer sizes confirmed; the mixed minors, partial sums and the grid bound were
  added from that review). The code gets the same review again.

## 6. netstandard2.0

- No `Span`, `Int128` or `Math.BigMul` there. The 64 × 64 -> 128 bit product has a portable version from 32-bit
  halves; the .NET build uses `Math.BigMul` under `#if NET`. Both are compiled on every target, and the .NET tests
  check the portable one against `Math.BigMul` (as in #221).
- Conversion from / to `BigInteger` through `ToByteArray` / `new BigInteger(byte[])` on netstandard2.0, also
  compiled and tested everywhere.
- The net48 test build (`-p:TestNet48=true`) exercises the netstandard2.0 library.

## 7. Measurement and acceptance

- **Baseline:** master after #222, built into a frozen copy (`dotnet build benchmarks/BooleanComparison -c Release
  -o ~/tool_A`); the spike into `~/tool_B`.
- **Cases:** `BooleanComparison nary grid 5 onepass`, `nary grid 10 onepass`, `nary spheres 20 onepass`.
- **Metric:** total time and `gc.pause_ms` per run; A B A B … in one session, at least 3 rounds each (5 for grid
  10), median; the noise is up to ±10 % on one large case, so rounds are reported individually too.
- **Allocations and GC:** `dotnet-trace collect --profile gc-verbose` with the TraceEvent analyzer of the hand-over
  (bytes per type and frame, GC pauses, promoted bytes per generation); `dotnet-gcdump` for the live objects
  (expected: the `uint[]` and `ExactBox` counts drop). No conclusions from sampled thread time under GC.
- **Bar:** grid 10 total at least 10 % faster; grid 5 and spheres 20 not slower beyond the noise.
- **Bit identity:** `perf/exact_hashes.sh` + `perf/hashes.py` against the reference if the `cgal` / Thingi10K data
  can be fetched in the session; otherwise triangle counts and volumes of the three n-ary cases (grid 10: 16332
  triangles; grid 5: 2472) plus the exact boolean test suite.

## 8. Plan

0. **Measure first (no library change committed).** On the baseline, with a temporary probe: the number of point
   ids, of computed exact points, the share fitting 2 / 3 / 4 limbs (n-ary cases and the exact boolean tests), and
   a gcdump of the live objects at the end of the triangulation. Decides the width (4.1) and gives an upper bound on
   the possible GC gain. If the exact points are not the bulk of the live objects, stop here.
1. **Storage** (`FixedPoint3`, `exactRef`, store, fallback, conversion, materialization, index, `merge_cluster`).
   Predicates still materialize. Tests of section 9 for storage. Measure once (diagnostic only).
2. **Kernels** for `Orient`, `InCircle`, `ComparePoints`, with the differential tests. Measure A/B (section 7).
3. **`front_winding`** only if step 2 shows the classification slower than the baseline.
4. **Decision** by the bar. Below it: numbers and finding into this document, the branch is kept, no merge.
   Above it: independent review (diff + this document + `REVIEW_CHECKLIST.md`, bounds checked one by one), mutation
   workflow of the arrangement shards (offsets in `tests/mutation/stryker-mesh-*.json` updated,
   `MutationConfigTests`), the full local CI list, then ready for review.

Between steps only the filtered tests (`ExactBoolean*`, `ExactPoint3*`, `Determinism*`, the new test class) and the
builds of all targets run; pushes in larger stages.

## 9. Tests

- **Representation:** round trip `BigInteger` -> limbs -> `BigInteger` for random values of every bit length up to
  127, for `0`, `±1`, `±(2^127 − 1)`; `±2^127` and beyond go to the fallback. Equality and hash consistent with
  `ExactPoint3.Equals`.
- **Kernels against `BigInteger`:** `Orient`, `InCircle`, `ComparePoints` on 100k+ random inputs of all sizes up to
  the admission limit (including all components at `±(2^127 − 1)`, which reaches the bounds of section 5), exactly
  collinear / cocircular / equal-key inputs, and mixed fixed / fallback calls.
- **Portable paths** (`BigMul` replacement, byte-array conversions) against the .NET operations.
- **Whole booleans with fixed width forced off** (an internal switch, tests only): bit-identical results to the
  default on the exact boolean meshes and on n-ary grids.
- **Invariance** over `Similarity.All()`: the 2^k scales move coordinates across the 2^127 admission limit, so
  some scales run on fixed points and others on the fallback; the results must stay identical bit for bit. A case
  is chosen (or added) that actually crosses the limit.
- **Concurrency:** many threads computing the exact points of the same ids at once; every reader sees a complete
  value (repeated runs). The existing determinism tests (serial equals parallel, repeated runs) stay green.
- **Mutation:** the carries, sign extension, trimming, the fit checks, the admission boundary and the publication
  order must be killed; survivors are explained here.

## 10. Risks

- **The gain may be smaller than hoped.** Other long-lived `BigInteger`s remain (triangle planes, `Center`), and
  the materialization adds short-lived garbage in the constructions and in `compare_along`. Step 0 bounds this
  before the main work.
- **Memory:** 96 bytes per computed exact point plus 4 bytes per point id, against 8 bytes per id plus the objects
  today; expected lower in total for points above 31 bits, measured in step 0.
- **Publication bugs** (torn reads) would be nondeterministic: the protocol is part of the review, and the
  concurrency test runs repeatedly.
- **Bounds:** every bound above is checked at run time as well (throwing), and reviewed independently.
