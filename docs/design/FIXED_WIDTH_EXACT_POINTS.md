# Fixed-width exact points in the arrangement

Status: **implemented** (design approved by the owner, 4 limbs per component). Tier L
([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md)). Results in section 11: `nary grid 10 onepass` **−25 %** (7291 -> 5481 ms),
GC pauses −48 %, allocations −66 %; real-world classes −8..−11 %; bit-identical (`exact_hashes.sh`: 0 differences).

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
- Sizes measured in #221 on grid 10, for the inputs of `InCircle` only: coordinates ≤ 109 bits, weights `W` ≤ 54
  bits; in the exact boolean tests up to 211 / 155 bits. Over *all* exact points they are larger (section 3a). A
  `BigInteger` fallback is required in any case.

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

## 3a. Step 0: measured (baseline = head of #222, `18138e0`)

A temporary probe (not committed) counted every exact point the arrangement computes (`new ExactBox`), with the
larger bit length of its coordinates and of `W`. "Fits L limbs" means every component is at most `64 L − 1` bits.

| case | point ids | exact points | `W = 1` | fit 1 limb | ≤ 2 limbs | ≤ 3 limbs | ≤ 4 limbs | max bits (coord / `W`) |
|---|---|---|---|---|---|---|---|---|
| grid 5 | 113k | 111k | 37 % | 42 % | 83 % | 99.0 % | 100 % | 214 / 160 |
| grid 10 | 332k | 331k | 41 % | 47 % | 85 % | 98.7 % | 100 % | 215 / 160 |
| spheres 20 | 252k | 55k | 51 % | 51 % | 51 % | 58 % | 98.9 % | 759 / 702 |

- **The earlier "≤ 109 / 54 bits" were the inputs of `InCircle` only.** Over all exact points, grid 10 needs up to
  215 bits; the medians are 102 / 48 bits. With 2 limbs 15 % of the grid-10 points (50k, the largest ones, each with
  four digit arrays) would stay in the fallback; on spheres 20 half of them.
- **On the grid, almost every point id gets its exact point** (331k of 332k), so the lazy indirection saves no
  memory there; on spheres 20 only 22 % do.
- **Live objects** (`dotnet-gcdump` of `nary grid 10 onepass`, at about 4.5 s and 6 s, i.e. in the triangulation and
  in the classification): 650k / 1.07M objects on the heap, of which `uint[]` (`BigInteger` digits) 291k / 579k and
  `ExactBox` 56k / 143k, together at most 53 % / 68 % (some `uint[]` belong to other `BigInteger`s: at 4.5 s there are 5.2 per box,
  more than the four a point can own). Most of the rest are `Double[]` (18k), `Plane[]` (12k), `InputTriangle`
  (24k) and the benchmark's input meshes. So the exact points are the bulk of the live objects: the stop condition of
  step 0 is not met.
- **GC:** `profile --repeat 2 grid:10`: 7.6 s (second run), 1.95 s pause, 426 gen0 and 85 gen1 collections, **no
  gen2**. The pauses are ephemeral collections, whose cost is the surviving objects they copy (every exact point is
  promoted twice) and the references from older chunks into young boxes they scan. A reference-free store removes
  both for the points that fit.

**Decision: 4 limbs per component** (256-bit two's complement, every component `|v| ≤ 2^255 − 1`). It covers 100 %
of the grid points and 98.9 % of spheres 20. The kernels trim their operands to the used limbs at run time, so the
wider slot costs memory (160 bytes per exact point, 53 MB on grid 10, plus 4 bytes per point id for `exactRef`, 1.3 MB; today about 250-290 bytes per point in objects: a 112-byte `ExactBox` and up to four digit arrays of 32-48 bytes;
264 bytes at the median sizes) but
not arithmetic on small values. The bounds of section 5 are redone for it.

## 4. Design

### 4.1 The fixed-width point

A new internal struct (working name `FixedPoint3`, file `math/FixedPoint3.cs`):

```csharp
internal unsafe struct FixedPoint3   // no reference fields: arrays of it are not traced by the GC
{
    public fixed ulong L[16];        // magnitudes of X, Y, Z, W, four 64-bit limbs each, little-endian; unused limbs 0
    public fixed byte M[4];          // per component: used limbs (0..4) in bits 0-2, 0x80 if negative
    public byte Flags;               // IsDouble
    public Vector3d D;               // the coordinates as doubles iff IsDouble (as ExactBox.D)
}
```

(As built: sign and magnitude instead of the two's complement of the first draft. The used lengths come for free,
the kernels work on trimmed magnitudes, and negation cannot overflow.)

- **Admission rule.** A canonical `ExactPoint3` is stored in fixed width iff every component `v` satisfies
  `|v| ≤ 2^255 − 1` (so `−2^255` is excluded and negation never overflows). Otherwise the point keeps today's
  `ExactBox` (the fallback). The rule depends only on the value, so **equal points always get the same
  representation**: equality and hashing never have to compare a fixed point with a fallback one.
- **Why four limbs:** section 3a (two limbs, the first proposal, leave 15 % of the grid points in the fallback).
- **Size.** 128 bytes of limbs + 24 bytes `D` + flags = 160 bytes (padded) per *computed* exact point.

### 4.2 Storage and publication (thread safety)

Points are computed lazily and concurrently (`exact_box` today publishes a reference; two threads may compute the
same point, and both store equal values). A 160-byte struct cannot be published atomically, so:

```
exactRef   : ChunkedList<int>          // per point id: 0 = not computed, k > 0 = fixed slot k - 1, k < 0 = fallback -k - 1
fixedStore : ChunkedList<FixedPoint3>  // dense; slots taken with Interlocked.Increment
fallback   : ChunkedList<ExactBox>     // dense; the points beyond 2^255
```

- A writer takes a fresh slot, writes the whole payload, then publishes the slot with `Volatile.Write` on
  `exactRef[id]` (release). A reader does `Volatile.Read` (acquire) and reads the payload only after a non-zero
  value. Two racing writers each take their own slot; one slot is wasted, both hold the same value, and either
  published index is valid. No payload is ever written after it was published.
- `ChunkedList` needs an index-level `Volatile` read/write (a small internal addition) and, for the store, a
  smaller chunk: 2048 × 160 bytes would put every chunk on the large object heap (round 3: larger chunks on the LOH
  were slower). A `ChunkedList` with a type-specific shift (256 × 160 = 40 KB) keeps them below the threshold.
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

- `AddPoint(ExactPoint3 p)`: if `p` fits, the key is a reference-free `FixedKey` (the 16 limbs) with a hash over
  the limbs, in its own 64 shards; otherwise the #222 path (`HashedPoint`) unchanged. Since the representation is
  a function of the value, the two indexes never hold the same point.
- `merge_cluster`: groups by a comparer on point ids (`Equals` / `GetHashCode` read the stored representation)
  instead of `Dictionary<ExactPoint3, List<int>>`. The result does not depend on the hash function or on the
  enumeration order (each group takes its smallest key); the review checks that again.

### 4.5 Predicates on fixed points

When all points of a call are fixed (the common case), the exact stage runs on limbs; if any is a fallback point,
the call materializes all of them and runs today's `BigInteger` code (identical sign, so identical results). The
`IsDouble` path (adaptive double predicates) stays first, as today.

- **Kernels (as built, `FixedArith`):** sign-magnitude numbers (`FixedInt`: pointer, used limbs, sign) with
  schoolbook multiplication and add / subtract / compare of trimmed magnitudes. Every result takes the limbs its
  operands' lengths allow (a product `la + lb`, a sum `max(la, lb) + 1`), from a scratch area on the caller's stack
  (`FixedArena`), so **no operation can overflow by construction**, independent of the value bounds; the area is
  sized by the worst case of those lengths (`Orient` 144, `InCircle` 1080, `ComparePoints` 128 limbs, comment in
  `FixedArith`) and `Take` throws if it were exceeded. The bounds of section 5 are then the proof that the values
  are what the lengths say, not a precondition of the arithmetic. `[SkipLocalsInit]` on .NET (the scratch is not
  zeroed; the multiplication clears its result).
- `Orient`: `Det3` of `(x_i, x_j, W)`, expanded along the first row as `ExactPoint3.Det3` does. The order does not
  matter for the bounds: all nine entries, `W` included, are below 2^255.
- `InCircle`: the rows `(A s, B s, A² + B²)` of today's code (`A = x w_d − x_d w`, `s = w w_d`; the `W = 1` shortcut
  of `incircle_row` kept, decided per row), then `Det3` along the first row, as today. Section 5 bounds both the
  mixed minors of that order and the pure ones.
- `ComparePoints`: `(α·p_a) W_b` vs `(α·p_b) W_a`, then per axis `x_a W_b` vs `x_b W_a`, with `α_k` the existing
  `AlphaInt` (integers, `α_k ≤ 2^53`).
- Optional, only if step 3 shows the classification regressing: `front_winding` gets the grid doubles of a point
  from the limbs (`GridDouble(id)`, bit-identical to today's `grid_double(Exact(id))`, which uses `(double)BigInteger`
  truncation and `ToDouble`) and materializes the points only when `orient3d_filtered` fails.

## 5. Bit bounds (each to be reviewed independently)

Inputs of a fixed point: every component, `W` included, `|v| ≤ m = 2^255 − 1 < 2^255`. (`W ≥ 1` is needed for the
sign arguments, `s > 0`, not for the bounds.) The kernels hold sign and magnitude (`FixedInt`), so a value with
`|v| < 2^b` needs `⌈b / 64⌉` limbs, no sign bit; negation or absolute values never overflow.

**`Orient` and `InCircle`: derived by machine** (section 13). `tools/PredicateCompiler` reads their formulas from
`tools/PredicateCompiler/predicates.spec`, written as the kernels compute them (the same operations in the same
order), and derives with exact integers, `|x ± y| ≤ M_x + M_y` and `|x y| ≤ M_x M_y` from `|v| ≤ m` for every input,
a bound for every intermediate value as a polynomial in `m`. The last column is the limbs `FixedArith` takes for the
value (a product `la + lb`, a sum `max(la, lb) + 1`, the operands' lengths capped by their bounds). The table is the
tool's output; `GeneratedPredicatesTests` fails if it differs, checks that every value stays within its bound on
inputs at `±m` (where the values reach their bound's limb count), that the spec's sign is the kernel's, and that
the kernels' scratch constants (`OrientScratch` 144, `InCircleScratch` 1080) cover the derived scratch.

<!-- generated by tools/PredicateCompiler --table; do not edit -->
| predicate | values | bound (m = 2^255 - 1) | bits | limbs | limbs taken by FixedArith |
|---|---|---|---|---|---|
| `Orient` | `b.y * c.w` and 5 more of its shape | `m^2` | 510 | 8 | 8 |
| `Orient` | `m1`, `m2`, `m3` | `2 m^2` | 511 | 8 | 9 |
| `Orient` | `t1`, `t2`, `t3` | `2 m^3` | 766 | 12 | 12 |
| `Orient` | `t1 - t2` | `4 m^3` | 767 | 12 | 13 |
| `Orient` | the determinant | `6 m^3` | 768 | 12 | 13 |
| `InCircle` | `as`, `bs`, `cs` and 12 more like `a.x * d.w` | `m^2` | 510 | 8 | 8 |
| `InCircle` | `aA`, `aB`, `bA`, `bB`, `cA`, `cB` | `2 m^2` | 511 | 8 | 9 |
| `InCircle` | `r1`, `r2`, `s1`, `s2`, `u1`, `u2` | `2 m^4` | 1021 | 16 | 16 |
| `InCircle` | `aA * aA` and 5 more of its shape | `4 m^4` | 1022 | 16 | 16 |
| `InCircle` | `r3`, `s3`, `u3` | `8 m^4` | 1023 | 16 | 17 |
| `InCircle` | `s2 * u3` and 3 more of its shape | `16 m^8` | 2044 | 32 | 32 |
| `InCircle` | `n1`, `n2` | `32 m^8` | 2045 | 32 | 33 |
| `InCircle` | `s1 * u2` and 1 more of its shape | `4 m^8` | 2042 | 32 | 32 |
| `InCircle` | `n3` | `8 m^8` | 2043 | 32 | 33 |
| `InCircle` | `t1`, `t2` | `64 m^12` | 3066 | 48 | 48 |
| `InCircle` | `t3` | `64 m^12` | 3066 | 48 | 48 |
| `InCircle` | `t1 - t2` | `128 m^12` | 3067 | 48 | 49 |
| `InCircle` | the determinant | `192 m^12` | 3068 | 48 | 49 |
| `Orient` | scratch of one call (sum over the values; lengths only: 143) | | | 132 | 137 |
| `InCircle` | scratch of one call (sum over the values; lengths only: 1035) | | | 936 | 950 |
<!-- end of the generated table -->

It reproduces the hand table it replaced (its `Orient` and `InCircle` rows, reviewed twice): every bound and limb
count agrees. The hand table gave the limbs in two's complement (`b + 1` bits): 13 for `Orient`'s determinant,
where 12 magnitude limbs suffice. The scratch per call: the lengths alone give 143 / 1035 limbs; the comment in
`FixedArith` counts 143 / 1078, its `InCircle` count an upper bound (it takes every determinant entry at 19 limbs,
the review of #223 found 594 instead of 637 for the determinant part, as here).

**`ComparePoints`: by hand** (not in the spec: a chain of comparisons, with the constants `α_k`):

| value | bound | limbs |
|---|---|---|
| `ComparePoints`: `α_k x_k` | `< 2^308` | 5 |
| `ComparePoints`: `α·p` and its partial sums (`α_k ≤ 2^53`) | `< 3 · 2^308 < 2^310` | 5 |
| `ComparePoints`: `(α·p) W'` | `< 2^565` | **9** |
| `ComparePoints`: `x W'` | `< 2^510` | **8** |

Notes for the review:
- `AlphaInt[0] = 1.0 · 2^53 = 2^53` exactly, so `α_k ≤ 2^53`, not `< 2^53`; `|α·p| ≤ 3 · 2^53 · (2^255 − 1) < 2^310`.
- The `W = 1` shortcut of `incircle_row` is decided per row, so one determinant can mix shortcut and general rows.
  A shortcut row is the general row with `s = 1`, so the general bounds cover the mix.
- The largest value is 48 limbs (384 bytes). One `InCircle` call holds all its intermediates at once in its
  scratch area: 1080 limbs (8.6 KB) for the length-derived worst case of the code (950 derived above), more than
  the 936 limbs a buffer of its bound for every value would take, since the code sizes by lengths (above);
  allocated once per call, never inside a loop. Grid-sized inputs use far fewer limbs: the run-time
  trimming makes the cost follow the actual sizes. For `InCircle` with `|x| < 2^109`, `W < 2^54` the bound is
  `< 3 · 2^874 < 2^876` (14 limbs); the measured maximum of its inputs' determinant on grid 10 was 833 bits.
- History: the first version of this table was for 2 limbs per component (`Orient` 7, `InCircle` 24,
  `ComparePoints` 5 limbs) and was reviewed independently once (all buffer sizes confirmed; the mixed minors,
  partial sums and the grid bound were added from that review). The 4-limb version was reviewed again: all bounds
  and limb counts confirmed; the stack total per call and wording fixes came from it. The code gets the same review.
  Then the `Orient` and `InCircle` rows were replaced by the derivation of `tools/PredicateCompiler` (section 13),
  which agrees with them.

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

0. **Done, section 3a.** **Measure first (no library change committed).** On the baseline, with a temporary probe: the number of point
   ids, of computed exact points, the share fitting 2 / 3 / 4 limbs (the n-ary cases), and
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
  255, for `0`, `±1`, `±(2^255 − 1)`; `±2^255` and beyond go to the fallback. Equality and hash consistent with
  `ExactPoint3.Equals`.
- **Kernels against `BigInteger`:** `Orient`, `InCircle`, `ComparePoints` on 100k+ random inputs of all sizes up to
  the admission limit (including all components at `±(2^255 − 1)`, which reaches the bounds of section 5), exactly
  collinear / cocircular / equal-key inputs, and mixed fixed / fallback calls.
- **Portable paths** (`BigMul` replacement, byte-array conversions) against the .NET operations.
- **Whole booleans with fixed width forced off** (an internal switch, tests only): bit-identical results to the
  default on the exact boolean meshes and on n-ary grids.
- **Invariance** over `Similarity.All()`: the existing invariance tests stay green. (Correction of the first
  draft: the 2^k scales do *not* move coordinates across the admission limit; the grid integers are the same at
  every 2^k scale, since only `ScaleExp` changes. Mixing is tested directly instead:
  `MixedRepresentations_SameWithoutFixedWidth` builds inputs whose constructed points exceed 2^255 next to fixed
  ones, and asserts that both representations occur.)
- **Concurrency:** many threads computing the exact points of the same ids at once; every reader sees a complete
  value (repeated runs). The existing determinism tests (serial equals parallel, repeated runs) stay green.
- **Mutation:** the carries, sign extension, trimming, the fit checks, the admission boundary and the publication
  order must be killed; survivors are explained here.

## 10. Risks

- **The gain may be smaller than hoped.** Other long-lived `BigInteger`s remain (triangle planes, `Center`), and
  the materialization adds short-lived garbage in the constructions and in `compare_along`. Step 0 bounds this
  before the main work.
- **Memory:** 160 bytes per computed exact point plus 4 bytes per point id, against 8 bytes per id plus the objects
  today; expected lower in total for points above 31 bits, measured in step 0.
- **Publication bugs** (torn reads) would be nondeterministic: the protocol is part of the review, and the
  concurrency test runs repeatedly.
- **Bounds:** every bound above is checked at run time as well (throwing), and reviewed independently.

## 11. Results

All on 4 cores, in one session, the tools alternating (A, B, G per round), no other load. A = head of #222
(`18138e0`), B = fixed-width storage and kernels (`b8f28b7`), G = B plus the follow-ups below. Command:
`BooleanComparison profile --repeat <r> <case>` (per round: grid 10 the second of 2 runs, the others the best of 3).
Same hash and triangle count for every run of every tool.

| case | A | B | G | G vs A |
|---|---|---|---|---|
| grid 10, total (median of 5 rounds) | 7291 ms | 5682 ms | **5481 ms** | **−24.8 %** |
| grid 10, rounds | 6946 7343 7291 8097 7171 | 5395 5858 5682 6091 5240 | 5481 5383 5134 5597 5572 | |
| grid 10, GC pause (median) | 1908 ms | 1025 ms | 996 ms | −48 % |
| grid 10, allocated | 7092 MB | 3246 MB | 2428 MB | −66 % |
| grid 10, gen0 / gen1 collections | 426 / 86 | 191 / 52 | 141 / 39 | |
| grid 5, total (median of 5) | 789 ms | 688 ms | 700 ms | −11 % |
| grid 5, GC pause | 175 ms | 106 ms | 107 ms | |
| spheres 20, total (median of 5) | 758 ms | 643 ms | 671 ms | −11 % |
| spheres 20, GC pause | 245 ms | 178 ms | 173 ms | |

Live objects on the heap (`dotnet-gcdump`, grid 10, in the triangulation / in the classification): A 650k / 1.07M,
B 259k / 419k.

**Real-world classes** (`perf/run_classes.sh`, warm, best of 3 per case and operation; two rounds A1 G1 A2 G2 in one
session; `python3 perf/summary.py`). Total of the best times, both rounds, and the GC share:

| class | A | G | change | GC share A -> G |
|---|---|---|---|---|
| `cherchi` (13 cases, 39 runs) | 30.30 / 30.22 s | 28.68 / 27.05 s | −8 % | 25 % -> 23 % |
| `cgal` (50, 150 runs) | 13.86 / 14.47 s | 12.71 / 13.21 s | −8 % | 17 % -> 13 % |
| Thingi10K (32, 96 runs) | 13.26 / 12.96 s | 11.31 / 12.09 s | −11 % | 19 % -> 15 % |
| n-ary (grid 5, grid 10, spheres 20) | 9.55 / 9.06 s | 6.75 / 7.04 s | −26 % | 25 % -> 17 % |

The allocations per run of the real-world classes are unchanged (307 / 37 / 53 MB), but the GC share drops: their
exact input points were long-lived `ExactBox` objects with digit arrays too. (The machine was slower than in the
hand-over's measurements, cherchi 30 s against 15 s there; only the comparison within the session counts.)

**Bit identity:** `perf/exact_hashes.sh` (263 runs: all `cgal` and Thingi10K cases, 5 `cherchi` cases, grid 5,
spheres 20, each with the hash of the result mesh and of the exact vertices) against
`perf/exact_hashes_reference.jsonl`: **0 differences** for A and for G.

**Follow-ups in G** (each measured by allocation on grid 10, same hash):

| change | allocated |
|---|---|
| B | 3246 MB |
| `front_winding` takes the corners' doubles from the arrangement (`TryExactDouble`); `BitLength` by `GetBitLength` | 3072 MB |
| `compare_along` on limbs (direction loaded in the exact stage, at most 8 limbs) | 2586 MB |
| the patch's center point and its doubles once per patch, not per candidate mesh | 2428 MB |

They cut allocations by a quarter, but the time only by about 3.5 % and the GC pause hardly: the remaining pauses
come from surviving objects, not from the allocation rate.

**Tried and dropped:** the chunks of `FixedPointStore` and of the reference-free `ChunkedList`s on the pinned object
heap (`GC.AllocateArray(pinned: true)`, so that they are never copied): no gain (measured under load, no trend).

**Checks.**
- 26 new tests (`ExactPoint3FixedWidthTests`); hand mutants: 19 of 20 killed (the survivor, a hash without the
  signs, is equivalent: it changes only the hash quality; the shard-spread test now covers it).
- Stryker on `math/FixedPoint3.cs` (shard `mesh-fixed`, its own threshold): 77.75 % at first, **83.91 %** after the
  tests for `DoubleOf`, the hash and the store growth. Survivors are equivalent: `>>>` on unsigned values, `>=` where
  equality was excluded before, trimming loops on values that are never zero, alpha values that are never zero,
  over-reads one element past a loop of equal values, and pointer offsets into unused stack. Threshold: `break` 81.
- Independent review of the code (diff, this document and the checklist only): no wrong result found; every kernel
  computes the same integer as the BigInteger code, the scratch and buffer sizes hold (InCircle's determinant needs
  at most 594 limbs, the comment's 637 is an upper bound). Its suggestions are done: a final mix in `Hash` (the
  low bits pick the index shard), `DoubleOf` only for new points in `AddPoint`, no copies in `FixedKey.Equals`, the
  corners materialized once when the orient3d filter fails, and `FixedWidthPoints` locked after the first point.
  Not done: caching the direction's limbs (no allocation on .NET; one per exact stage on netstandard2.0).

## 12. What is left, ranked

1. **Triangle planes in fixed width** (idea 1 step 2 of the performance hand-over): 12k `PlaneData` with four
   BigIntegers and a `double[]` each are the largest long-lived rest on grid 10; `line_direction` (4 % of the bytes)
   would follow. Needs its own bounds (normals up to 2 × 255 + 1 bits, `D` up to about 767) and review.
2. **The per-triangle collections as CSR arrays** (idea 5): about 90k long-lived objects on grid 10.
3. **`centroid`** (7 % of the bytes): needs the gcd reduction on limbs.
4. **The predicate compiler** (done as a bounds tool, section 13): extend `predicates.spec` to the bounds of 1 and
   3 when they are built (it needs constant inputs with their own bounds, e.g. `α_k` or plane coefficients).
   Generated kernels only if a profile shows the exact stages as a large share; on grid 10 they are about 11 % of
   the thread time (section 13).

## 13. Predicate compiler on fixed points (measured, kept as a bounds tool)

The task: generate the exact stages of `Orient` and `InCircle` with the generator of #221 on the 4-limb points,
let it derive the bit bounds, and keep the kernels only with a measured time gain (stop rule: otherwise keep only
the proof of the bounds).

**What was built.** `tools/PredicateCompiler`: the `ipredicate` part of #221, retargeted from two's complement
inputs with fitted bit bounds (`IntegerRuntime`) to `FixedPoint3` / `FixedInt` with `|v| ≤ 2^255 − 1`. It derives
exact magnitude bounds (section 5). The spike (branch `claude/predicate-compiler-fixed-spike`, `2eddb7d`)
additionally generated `math/Generated/FixedPredicates.cs`: one stack buffer per value of exactly the limbs of its
bound, the operations as `FixedArith.Mul / Add / Sub (a, b, r, cap)` (new overloads that throw when a result would
not fit; the arena versions called them, so one runtime), and changed the two call sites in
`ExactMeshArrangement`. The generated `InCircle` has no `W = 1` row shortcut (the general row has the same values).

**Speed first: how much do the kernels cost?**
- Per call on recorded operands (a temporary probe, not committed: every 8th `Orient` and every 4th `InCircle`
  call of `profile grid:10`, 348k / 299k calls, replayed single-threaded, 5 rounds): hand `Orient` 241-320 ns,
  generated 222-322 ns; hand `InCircle` 929-1132 ns, generated 910-1505 ns. Equal within the noise (±10 % between
  rounds on this machine); also with a `Mul` shortcut for factors of magnitude 1 (restoring the `W = 1` saving).
- Share: 2.78M exact `Orient` and 1.2M exact `InCircle` calls on grid 10, so about 0.7 + 1.3 = 2 s of thread time
  of about 18 s (4.5 s on 4 cores): about 11 %. (Stopwatch probes around the calls read 12 s and more; they count
  the time slices and GC suspensions of the other threads, single-core runs gave more kernel time than total time.)
  So even a kernel twice as fast would gain about 5 % on grid 10 and less on the other classes, where the exact
  stages are rarer.

**End to end.** `BooleanComparison profile`, A = head of #223 (`dfbf0e4`), G = the spike, alternating, 5 rounds in
one session (grid 10: the second of 2 runs, the others the best of 3), medians:

| case | A | G | |
|---|---|---|---|
| grid 10 | 4460 ms (4925 4506 4345 4460 4340) | 4374 ms (4374 4670 4237 4350 4418) | −1.9 % |
| grid 5 | 527 ms | 535 ms | +1.5 % |
| spheres 20 | 391 ms | 390 ms | 0 % |

Same hashes for every run. CLASSES_PLACEHOLDER

**Decision (stop rule):** no measurable gain, so the kernels stay as they are (hand-written, length-sized arenas)
and the generated code is not merged. What is kept is the derivation: the bound table of section 5 is the tool's
output, and the tests check the kernels against it (`GeneratedPredicatesTests`):
- the table in this document equals the derivation;
- on points at `±m` and random ones, every intermediate value of both formulas stays within its bound and reaches
  its bound's limb count (the bounds are tight in limbs), and the formula's sign equals `FixedArith`'s;
- `OrientScratch` (144) and `InCircleScratch` (1080) cover the derived arena use (137 / 950 limbs);
- the limb boundaries (`2m` 4 limbs, `3m` 5 limbs) and the parser's rejections.

Nine hand mutants of the rules and the spec (product bound as a sum, sum bound as a max, the length rule's carry
limb, product length and operand length, the limb rounding, `m`, a sign and an input of the spec): all killed,
each also by a test other than the table comparison (the limb rounding by the boundary test only). One more was
equivalent and removed from the code: capping an operand's length by what its operation took never changes it. The library
is unchanged, so there is no mutation shard and nothing for the exact-code analyzers.

**Findings for the review of the hand kernels.** The derivation agrees with every `Orient` and `InCircle` row of
the hand table. Two counts were loose, both on the safe side: the hand table gave two's complement limbs (13 for
`Orient`'s determinant, 12 suffice), and the scratch comment in `FixedArith` counts `InCircle` as 1078 limbs where
its operations take at most 1035 by lengths alone (950 with the value bounds).
