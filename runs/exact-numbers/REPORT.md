# Exact number representation beyond BigInteger: measurements, prototypes, plan

Study of 2026-11-12 (owner decision of that day, roadmap/exact-boolean.md "Exact number representation beyond
`BigInteger`"). Measurement and local prototypes only: no library code was pushed. Everything here is on branch
`runs/exact-numbers` under `runs/exact-numbers/` (probe, scripts, specs, raw rows, the instrumentation as a patch).

**Commit set of every table, unless a table says otherwise:** library master `c0f8aa9` plus
`instrumentation.patch` (timers and counters on the exact paths; the prototypes are in it but off unless
`EXACT_PROTO` is set). Machine: 4-core cloud container, 15 GB, .NET SDK 10.0.115, Release, workstation GC.
Thingi10K models are fetched at run time (`scripts/fetch_cases.py`); nothing of them is committed.

## 1. Result in short

1. **Normal inputs: stop.** On 99 operations of clean Thingi10K models (20 pairs after the EMBER protocol and 13
   models with their rotated copy, three operations each) all exact representations beyond the double filters take
   4.7 % and 8.5 % of the time: BigInteger 3.4 % / 7.4 %, expansions 0.1 %, fixed-width limbs 1.2 % / 1.0 %; GC
   pauses 3.2 % / 3.5 %. The best prototype for those BigInteger paths is about 2x, so the reachable gain is 2-4 %.
   Below the 10 % stop rule: nothing to build for normal inputs.
2. **Alpha wrap: stop.** The AW3 sessions counted 0 exact `InSphere` fallbacks; the bound is 0.05 % of the pipeline
   (their rows, section 2.4). No `InSphere` kernel.
3. **The #711 class is the one large lever, and it has a bit-identical fix.** On the 61 fuzz units of grp-a
   (issue #711) 81 % of the time is `orient3d_bigint` / `orient2d_bigint`. A replacement that adds the exact
   monomials of the determinant into two stack accumulators (prototype P2, section 3.1) is 12-25x faster per call and
   allocates nothing. Whole operations: **298 s -> 64 s, median 4.2 -> 0.8 s, maximum 15.2 -> 2.8 s, none above 5 s
   (20 before), 243 -> 12 GB allocated; result hash, status, `RoundingClean`, all counts and the predicate counts
   equal in 61 of 61.** #711 then needs no range, bound or budget decision.
4. **The Thingi10K tail (self-intersecting and coplanar-heavy models) is at the threshold.** 27 operations on 9
   models: BigInteger 18.2 % of the time (median 15.3 %, 42327: 24-28 %), fixed-width limbs 8.3 %, GC pauses 6.6 %
   single-threaded (the boolean study measured 11.3 % on its inventory at 4 cores, 23-27 % on single models); 51 % of
   all allocated bytes come from the BigInteger paths. The same stages on stack limbs are about 2x faster with no
   allocation (prototype P3b, section 3.3): about 9 % of the tail's time plus the GC share of half the allocations.
   A candidate for the allocation work of the boolean study's lever 4, not a must.
5. **Not worth building (measured):** expansions with an extended exponent (1.5-7x slower than P2), a dense stack
   integer for the double-input fallback (2.5-2.7x gain against P2's 12-25x), a row-scaled double filter before P2
   (no gain on whole operations), intermediate-precision filters before the limb stages (bound about 3 % of the
   tail), an `InSphere` kernel, a Roslyn source generator.

## 2. Upper bound

### 2.1 Method

`instrumentation.patch` wraps every exact path beyond the double filters in a timer (Stopwatch ticks, calls and the
bytes allocated on the thread inside the region; only the outermost region of a thread is timed, so the paths are
disjoint). The timer's own cost (60-90 ns per region, calibrated per row) is subtracted from the path and from the
total. Paths: the double-input predicates (`orient3d_int`, the adaptive stages, `*_bigint`), the arrangement's
`Orient` / `InCircle` / `compare_along` / `ComparePoints` on fixed-width points and on BigInteger points, exact
point constructions, planes, local points, and the classification's BigInteger stages (plane sides, `orient3d` on
`ExactPoint3`, centroids).

**The shares are measured on one thread** (`EXACT_SERIAL=1`: `gParallel.SerialOnThisThread`, `taskset -c 0`,
`DOTNET_gcConcurrent=0`), so a region's wall time is its CPU time plus the collections its own allocations trigger.
With the thread pool the sums are wrong: threads wait inside the regions (collections, allocation, more runnable
threads than cores), and the path times add up to more than the process CPU time (up to 170 % on 42327). Those
rows are kept under `results/mt/` and `results/1core_pool/` and are not used. The results of a serial run are the
parallel run's (the library's parallel loops are deterministic).

### 2.2 Shares per stratum (single thread)

`python3 scripts/analyze_paths.py normal_pairs=results/normal_pairs.jsonl normal_copy=results/normal_copy.jsonl tail_small=results/tail_small.jsonl tail_big=results/tail_big.jsonl`

| stratum | ops | time s | BigInteger %: total / median / max | expansions % | fixed-width limbs %: total / max | GC pause % of wall: total / max | allocated GB | allocated inside BigInteger paths % |
|---|---|---|---|---|---|---|---|---|
| normal: 20 thingi-pairs (clean models, B rotated and shifted), 3 ops each | 60 | 9.6 | 3.4 / 2.9 / 21.1 | 0.1 | 1.2 / 7.9 | 3.2 / 8.6 | 2.3 | 3.8 |
| normal: 13 clean models with their rotated copy, 3 ops each | 39 | 7.2 | 7.4 / 9.1 / 35.3 | 0.1 | 1.0 / 4.9 | 3.5 / 8.6 | 1.5 | 8.6 |
| tail: 113685, 1059579, 105693, 107543, 107545, 107554, 113906, 42327, 67709 with their rotated copy, 3 ops each | 27 | 761.7 | 18.2 / 15.3 / 27.9 | 0.0 | 8.3 / 14.9 | 6.6 / 11.4 | 175.1 | 51.3 |
| tail, large models, union: 519507 | 1 | 10.1 | 17.7 | 0.0 | 1.6 | 6.7 | 1.5 | 18.9 |
| 1517923 | 1 | 14.5 | 1.3 | 0.0 | 2.7 | 2.2 | 1.0 | 11.2 |
| 519511 | 1 | 45.7 | 5.0 | 0.1 | 1.0 | 4.5 | 6.0 | 15.3 |
| 122796 | 1 | 25.5 | 5.8 | 0.0 | 0.1 | 4.9 | 2.6 | 15.5 |
| 1005586 | 1 | 162.2 | 6.3 | 0.0 | 0.0 | 4.5 | 15.9 | 15.5 |
| #711 class: 60 slow units of grp-a and the timeout unit (4 cores; the rounding is serial) | 61 | 298.2 | 80.9 in the double-input `*_bigint` (the arrangement's and the classification's BigInteger stages on the 2^840 grid come on top, about 100 s of thread time on 4 threads) | 0.0 | 0.0 | 0.4 | 243.4 | 99.5 (95.1 in `*_bigint`) |

(The maxima of the normal strata are operations of 20-50 ms.)

55928 (difference, with its rotated copy; 929 s single-threaded, `results/tail_55928.jsonl`): BigInteger 34.9 % of
the time, fixed-width limbs 1.8 %, GC pauses 5.2 % (4 cores, workstation GC: 24 % per #734), 204 GB allocated, 69.5 %
of it inside the BigInteger paths. By path: InCircle beyond the fixed width 19.6 % (12.43M stages at 14.7 us, 87 GB =
43.8 % of all bytes), exact centroids 8.5 % (8.7M at 9.0 us), exact point construction 2.4 %, `orient3d` on exact
points 2.0 %. 5.07M of its 7.48M exact points are beyond the fixed width (68 %).

### 2.3 Where the BigInteger time is

`python3 scripts/analyze_breakdown.py ...` (`data/breakdown.md`), share of the stratum's time:

| path | what it is | normal pairs | normal copy | tail (27 ops) | ns per call (tail) | bytes allocated inside (tail) |
|---|---|---|---|---|---|---|
| `arr.exact_point.big` | constructing an implicit point: BigInteger products, three gcds, conversion to fixed width | 2.0 % | 5.0 % | 4.0 % (519507: 13 %) | 4971 | 6.5 % |
| `arr.planes` | a triangle's plane and its double approximations | 0.9 % | 1.6 % | 0.4 % (1005586: 6.0 %, 122796: 4.8 %) | 1933 | 0.6 % |
| `bool.centroid.big` | exact centroid of a piece (classification) | 0.1 % | 0.1 % | 4.6 % | 3517 | 8.4 % |
| `bool.side.big` | plane side of an exact point: A X + B Y + C Z - D W | 0.0 % | 0.0 % | 3.9 % (42327: 11 %) | 456 | 20.0 % |
| `bool.orient3d.big` | 4x4 determinant of four exact points | 0.0 % | 0.0 % | 2.3 % | 4358 | 9.9 % |
| `arr.incircle.big` | InCircle on points beyond the fixed width | 0.0 % | 0.0 % | 1.7 % | 12311 | 3.9 % |
| `arr.incircle.fixed` | InCircle on fixed-width points (FixedArith) | 0.0 % | 0.0 % | 4.5 % | 2316 | 0 |
| `arr.orient.fixed` | Orient on fixed-width points | 0.3 % | 0.6 % | 2.8 % | 299 | 0 |
| `o3.int` | the 62-bit integer stage of `Orient3d` | 0.8 % | 0.3 % | 0.2 % | 28 | 0 |
| adaptive stages (Shewchuk B-D) | | 0.1 % | 0.1 % | 0.0 % | 230-270 | 0 |
| double-input `*_bigint` | | 0.0 % | 0.0 % | 0.0 % | | |

So outside the #711 class the double-input BigInteger fallback never runs, the expansions cost nothing, and the
BigInteger time is constructions (points, planes, centroids) and the classification's exact stages on centroids.

Long-lived exact objects: of the exact points an operation materializes, the BigInteger fallback points (beyond
the fixed width) are 0.3-0.4 % in the median of the normal strata (4-6 of 600-1600 points, at most 16 %) and 7.6 %
in the tail (1.7-17.5 %; 107554: 78k of 566k); a fallback point's four magnitudes take about 225 bytes in the
median, a fixed point 160. T2 (#619) and the fixed-width points already removed most of the retained
BigInteger memory; what is left is the allocation rate of temporaries (51 % of the tail's bytes, section 2.2).

### 2.4 Rows from the other studies (not re-measured)

- Boolean extreme-runtime study (#734, master 8124a7d3 + #675 a9b00da3, 66 ops, 2354 s): classification 68.5 %,
  triangulation 8.4 %, rounding 8.3 %, intersections 6.1 %, merge 4.9 %; GC pauses 11.3 %, 23-27 % on 107554, 498458,
  42327, 55928; server GC -19 % / -11 % on 107554 / 42327 with equal hashes. Its open candidates "BigInteger
  construction in `carried_crossings`" (19.4 % inclusive of 42327 intersection) and "a faster wide-integer kernel"
  are sections 3.3 and 3.1 here. Its closed items (leading-term certificate 1.4 %, 2^k scaling into the expansion
  range 3 %, the `BigInteger[4,4]` matrix) agree with the rows here and are not repeated.
- AW3 (master cab88798 / 1230482): about 38 filtered `InSphere` per insertion at about 140 ns, 0 exact fallbacks
  counted; exact predicates and their filters 10.0 % / 8.9 % of the wrap's CPU (tail / normal), the stages beyond
  double 0.05 %.
- Rounding (#721 trace, master 46529be): on ordinary coordinates a pair test costs 86-118 ns and the rounding is not
  in BigInteger at all.

## 3. Prototypes

### 3.1 The double-input fallback (`ExactPredicates.*_bigint`), #711 first

Four representations, all in `Probe/` (`Dyadic.cs`, `ExtExp.cs`, `Micro.cs`), each compared with the BigInteger
code on the same arguments:

- **P2, monomial accumulator** (variable-width limbs on the stack): every finite double is m 2^e with an integer
  |m| < 2^53. The predicate is the homogeneous determinant with a column of ones, expanded: 6 monomials of degree 2
  (orient2d), 24 of degree 3 (orient3d), 48 of degree 4 (incircle), 360 of degree 5 (insphere). Each monomial is an
  exact product of mantissas (2-5 limbs) added at bit offset (sum of its exponents - d emin) into one of two
  magnitude accumulators (positive, negative monomials); the sign is their comparison from the top limb. Integer
  only, no range limit, worst case 66 / 99 / 132 / 165 limbs per accumulator.
- **P3, dense stack integer** (the stack-allocated fallback): the BigInteger algorithm on `FixedArith` with a stack
  arena, coordinates aligned to the smallest exponent.
- **P1, expansions with an extended exponent:** each monomial as a Shewchuk expansion of the integer mantissas with
  an integer exponent; monomials sorted by exponent, cut into clusters at gaps of more than 170 binary orders, each
  cluster scaled and summed with Fast-Expansion-Sum; the first nonzero cluster decides.
- **P4, a filter on row-scaled differences** (the dyadic / interval filter for this stage): each difference vector
  scaled by a power of two to [1, 2), then the generated filter rule.

Recorded arguments of the #711 timeout unit (every 12th `*_bigint` call, 500k calls),
`Probe micro rec data/rec711.bin 5` (`data/micro_rec711.md`):

| method | orient3d ns / call | B / call | orient2d ns / call | B / call | wrong signs |
|---|---|---|---|---|---|
| today: `*_bigint` | 5077 | 4625 | 808 | 1399 | |
| P2 accumulator, unrolled (what a generator emits) | **202** | 0 | **67** | 0 | 0 |
| P2 accumulator, table-driven | 387 | 0 | 105 | 0 | 0 |
| P1 extended-exponent expansions | 782 | 0 | 102 | 0 | 0 |
| P3 dense stack integer | 1849 | 0 | 325 | 0 | 0 |
| P4 filter alone: decides 20.5 % (orient3d) / 65.9 % (orient2d) | 52 | 0 | 40 | 0 | 0 |

Synthetic classes, 20 000 cases each (`Probe micro synth`, `data/micro_synth_v2.md`); ns per call:

| class | predicate | today's exact stage | P2 unrolled (table-driven for incircle / insphere) | P1 | P3 |
|---|---|---|---|---|---|
| in range, exactly degenerate or one ulp off | orient3d | 191 (`orient3d_int`) | 238 | 900 | 1078 |
| | orient2d | 157 (adaptive) | 94 | 184 | 516 |
| | incircle | 340 (adaptive) | 1185 | | 1025 |
| | insphere | 3149 (BigInteger) | 10602 | | 1591 |
| uniformly tiny (x 2^-600) | orient2d | 494 (BigInteger) | 71 | 144 | 132 |
| | incircle | 1156 (BigInteger) | 1005 | | 510 |
| three scales (2^-943, 2^-106, 2^-100) | orient3d | 9768 | 283 | 817 | 4387 |
| | incircle | 16257 | 1026 | | 7766 |
| | insphere | 44911 | 8352 | | 24912 |
| any exponent, subnormals included | orient3d | 17959 | 269 | 682 | 8348 |
| | orient2d | 3017 | 115 | 139 | 1376 |
| | incircle | 28159 | 990 | | 14775 |
| | insphere | 108931 | 9689 | | 63379 |

Signs: 0 differences against `*_bigint` on 6 classes x 4 predicates x 50 000 cases, every method
(`data/micro_synth_v3_signs.md`; the sixth class takes the largest mantissas at both ends of the exponent range, so
the accumulators carry through their whole length), on the 500k recorded calls, and in the whole operations below.

Reading: P2 replaces the BigInteger fallback and nothing else. Inside the expansion range today's stages stay
(`orient3d_int` is faster; the adaptive incircle is 3.5x faster than P2's 48 monomials). Insphere has 360
monomials: P2 wins only for wide spans, and the wrap never reaches the stage.

(With the generated kernels of the PR, master a7693fd against the branch, uninstrumented: 289.4 -> 51.3 s, median
4.04 -> 0.61 s, maximum 14.0 -> 2.2 s, units above 5 s 18 -> 0, 243.4 -> 12.0 GB, hashes and counts equal 61 / 61;
`results/pr/fuzz_*.jsonl`. The table below is the prototype in the instrumented build.)

**Whole operations, the 61 fuzz units** (one process per variant over all units, 4 cores, same build, the fallback
switched by `EXACT_PROTO`; `python3 scripts/analyze_ab.py results/fuzz_base.jsonl ...`, `data/fuzz_ab.md`):

| variant | total s | median s | max s | units above 5 s | fallback share | allocated GB | hashes, status, RoundingClean, counts equal | predicate counts equal |
|---|---|---|---|---|---|---|---|---|
| today (BigInteger) | 298.2 | 4.21 | 15.2 | 20 | 80.9 % | 243.4 | | |
| P2 | 63.7 | 0.80 | 2.8 | 0 | 15.4 % | 12.0 | 61 / 61 | 61 / 61 |
| P4 before P2 | 66.0 | 0.93 | 2.5 | 0 | 13.2 % | 12.0 | 61 / 61 | 61 / 61 |

The timeout unit of #711: 15.2 -> 2.8 s (rounding 14.7 -> 2.3 s, 13.9 GB -> 0.29 GB). What is left in these units
after P2: the rounding's own search (42.6 s of 63.7 s for 110M predicate calls; the rounding sessions' area) and the
arrangement's BigInteger `InCircle` / `Orient` on implicit points of a grid that spans 2^840 (186k InCircle stages at
about 350 us each, inside 15 s of triangulation; T3a's area, not measured further here).

### 3.2 Bounds and specs

- `specs/dyadic.spec`: P2 as a proposed `dpredicate` entry of `tools/PredicateCompiler` with a new monomial pass
  (expand the sign expression into monomials; value rule per monomial; accumulator rule). Bound table, derived
  independently of the C# code and checked with exact integers for every exponent span s in [0, 2045]
  (`python3 scripts/check_bounds.py`): the sum of all monomial magnitudes stays below 2^(64 L(s)) and no monomial
  is written outside the accumulator, L(s) = floor(d s / 64) + ceil((53 d + g + 63) / 64).

  | predicate | degree d | monomials | guard bits g | bits of a monomial | limbs at s = 0 | limbs at s = 2045 | stack bytes worst case |
  |---|---|---|---|---|---|---|---|
  | orient2d | 2 | 6 | 3 | 106 | 3 | 66 | 1056 |
  | orient3d | 3 | 24 | 5 | 159 | 4 | 99 | 1584 |
  | incircle | 4 | 48 | 6 | 212 | 5 | 132 | 2112 |
  | insphere | 5 | 360 | 9 | 265 | 6 | 165 | 2640 |

  There is no floating-point error bound: the kernel has no floating-point operation (so no FMA, no rounding
  mode, no overflow or underflow, and the T3a rule "check the determinant's own overflow" has nothing to check).
  Z3 or FPTaylor do not apply; the check is the exact-integer script above and the differential tests.
- `specs/scaled_filter.spec`: P4 as an `ipredicate ... inputs xyz` with a proposed `scale rows` modifier. Its
  constants follow the compiler's existing rule (leaf difference u; sum (u + max) / (1 - u); product
  (u + cx + cy + cx cy) / (1 - u)): orient2d c = 4.0000000000000009 u, K = 0x1.0000000000002p-51; orient3d
  c = 8.0000000000000036 u, K = 0x1.0000000000003p-50; gate of the scaled differences 0 or in [2^-428, 2] /
  [2^-268, 2] (`GateLow = 960 / degree - 52`). The per-operation lemmas are the ones `tools/Z3Pilot/filter_rules.py`
  already proves; inside the gate the scaling is exact, so no new lemma is needed. (A variant with an absolute
  underflow term instead of the gate decides 61 % / 76 % of the #711 calls, but needs a new lemma.) Not proposed.
- `specs/wide.spec`: the arrangement's `Orient` and `InCircle` at input bounds of 320 and 832 bits, run through
  today's compiler unchanged (`--table`, `specs/wide_table.md`): scratch of one call 175 limbs (Orient320), 1228
  (InCircle320), 439 (Orient832), 3100 (InCircle832); the determinant of InCircle320 has 3848 bits (61 limbs).
- P1 has no spec: its only bound is the cluster gap (a nonzero cluster sum is a multiple of 2^Elow and everything
  below the cut is smaller than 24 x 2^159 x 2^(Elow - 171)), and it is not proposed.

### 3.3 Stages on implicit points and the classification (P3b: FixedArith beyond 4 limbs)

The same determinants as the BigInteger code, on `FixedArith` with a stack arena (`Probe/Implicit.cs`,
`Probe micro implicit 4000 7`, second pass after a warm-up pass, `data/micro_implicit_v2.md`); synthetic values of
the sizes measured on 55928; ns per call / bytes per call:

| operation | size (bits of X / W) | BigInteger (today) | FixedArith, points loaded from BigInteger per call | FixedArith, points stored as limbs |
|---|---|---|---|---|
| InCircle | 192 / 138 (fixed width today) | 7898 / 6072 | 3734 / 0 | 3503 / 0 |
| | 309 / 255 (three-plane points) | 18868 / 9375 | 7516 / 0 | 8644 / 0 |
| | 807 / 753 (centroids) | 85341 / 23968 | 61750 / 0 | 54327 / 0 |
| Orient | 192 / 138 | 934 / 1131 | 596 / 0 | 412 / 0 |
| | 309 / 255 | 1963 / 1568 | 1085 / 0 | 890 / 0 |
| | 807 / 753 | 9338 / 3641 | 3479 / 0 | 3267 / 0 |
| plane side | 192 / 138 | 318 / 448 | 338 / 0 | 180 / 0 |
| | 309 / 255 | 498 / 560 | 398 / 0 | 300 / 0 |
| | 807 / 753 | 627 / 1008 | 446 / 0 | 317 / 0 |
| centroid (construction, result kept) | 192 / 138 | 1279 / 1640 | | 627 / 280 |
| | 309 / 255 | 2010 / 2365 | | 1146 / 440 |
| | 807 / 753 | 17340 / 5880 | | 7201 / 1208 |

0 wrong signs. So limbs on the stack are 1.6-2.9x faster than BigInteger on these stages and allocate nothing (a
kept construction allocates one array instead of about twenty objects). .NET's BigInteger is not slow at these
sizes; the gain is the allocation and the object overhead, as the boolean study found for the 4x4 matrix.

Exact point construction (`Probe micro construct`, `data/micro_construct.md`; an edge-plane point from 56-bit grid
points and a 101 / 156-bit plane): BigInteger products and sums 2389 ns, the same on stack limbs 660 ns (3.6x, no
allocation); `ExactPoint3.Create` 3054 ns, of it 2699 ns the three gcds; `FixedPoint3.TryCreate` 1045 ns. So a
construction on limbs saves the arithmetic and the conversion (about half of the 6.5 us), and the rest is the gcd,
which needs a limb gcd of its own to gain more.

### 3.4 Lazy evaluation with intermediate-precision filters

The library is already lazy in the sense of Pion and Fabri: approximations with an error first, exact values on
demand and cached. What a further stage between the double filters and the exact stages could decide, counted in
the exact stages themselves (`EXACT_HIST=1`, `python3 scripts/analyze_hist.py results/hist.jsonl`; gap = bits of the
expression's scale minus bits of its exact value, the scale taken from the homogeneous form, so a gap also contains
the cancellation of a translation):

| stratum | exact stage | calls | exactly zero | gap < 96 bits (a double-double evaluation) | gap >= 192, nonzero |
|---|---|---|---|---|---|
| tail (5 models, intersection) | plane side, BigInteger | 20.3M | 16.2 % | 83.7 % | 0.0 % |
| | Orient, fixed width | 19.2M | 21.5 % | 53.1 % | 9.7 % |
| | InCircle, fixed width | 3.6M | 0.8 % | 43.9 % | 39.4 % |
| | InCircle, BigInteger | 0.25M | 0.0 % | 7.1 % | 84.6 % |
| | orient3d on exact points | 1.36M | 2.4 % | 1.3 % | 96.2 % |
| normal (13 models, union) | Orient, fixed width | 44k | 99.0 % | 0.7 % | 0.0 % |

Bound: a double-double stage on cached double-double coordinates could take half of the fixed-width Orient and
InCircle stages of the tail (2.8 % + 4.5 % of its time, at about a tenth of their cost): about 3 % of the tail, 0
on normal inputs (there the exact stages are exact zeros). Below the stop rule; the earlier filter attempt on 55928
(EXACT_BOOLEAN_TAIL_55928.md: decided 4.65M of 12.43M, no time gain) agrees. The plane sides are the exception
(84 % decidable), but there the cost is the exact centroid behind them, which is section 5's candidate B.

## 4. Roslyn source generator or the checked-in PredicateCompiler output

Extend `tools/PredicateCompiler`, as the owner's default says:

- The output is exact code. Checked in, it is reviewed line by line in the PR, falls under the exact-code analyzers
  and `BannedSymbols.txt`, is mutated by Stryker in its shard (`mesh-generated`), and `GeneratedPredicatesIndirectTests`
  keeps it equal to the generator. A Roslyn generator's output exists only in the compilation: none of these four
  mechanisms sees it without extra work.
- The library builds for netstandard2.0, net48 tests and as Unity sources (`geometry3Sharp.asmdef`). Checked-in C#
  works in all of them; a source generator needs the Roslyn pipeline of each consumer and an analyzer in the package,
  which `dotnet pack`'s validation and the smoke tests would have to cover.
- The bound table is a test input and a document block (`--doc`); the generator already produces both from the same
  derivation. A Roslyn generator would need a second path for the documents.
- There are few predicates (three or four more here) and they change rarely, so generation at build time buys
  nothing; it adds build time, a dependency on Microsoft.CodeAnalysis and a determinism question per SDK version.
- A Roslyn generator pays when users write predicates in their own code. That is not a current need.

## 5. Ranked plan

The exact code is single-writer; the items go into the serial queue (T3a: #710, #719, (A), #711) and do not change
its order.

| # | PR | files | unblocks | expected gain | review and mutation cost |
|---|---|---|---|---|---|
| 1 | **Allocation-free exact fallback of `ExactPredicates` outside the expansion range** (P2). A monomial pass in `tools/PredicateCompiler` (`dpredicate`, `specs/dyadic.spec`), the generated kernels Orient2d / Orient3d / InCircle in `math/Generated/`, three call sites in `math/ExactPredicates.cs`; `*_bigint` stays as the tests' oracle (and as the fallback of InSphere) | tools/PredicateCompiler, math/Generated (new file), math/ExactPredicates.cs, docs (PREDICATES.md, a bound-table block, SOURCES.md) | **#711**, at its place in the queue; it touches no `ExactMesh*` or rounding file | the 61 units 298 -> 64 s, none above 5 s, bit-identical; nothing on other inputs (the path never runs there) | tier L by the rule, but integer-only: no error bound to review, the bound table is machine-checked. About 150 generator lines and 250 generated lines. Tests: differential against `*_bigint` (the six classes of section 3.1), the carry cases at both ends of the exponent range, invariance over `Similarity.All()`, the netstandard2.0 multiply (`MulHighPortable`) and a clear loop instead of `Span.Clear`, a procedural #711 unit with a time bound (median unit 0.8 s; the timeout unit 2.8 s), hand mutants of the generator (a dropped monomial, a flipped sign, the limb count, the shift). Mutation: incremental on `ExactPredicates.cs`; the new generated file joins `mesh-generated` and gets its first full run on master. 1-2 days |
| 2 | **Decision, no code:** the cost-weighted rounding budget (boolean study lever 3) is not needed for #711 once 1 is in | | | avoids a results change | owner |
| 3 | Candidate B: **the classification's BigInteger stages on stack limbs**: plane side, `orient3d` on exact points, the centroid as one limb array, planes stored as limbs (this is the concrete form of the boolean study's "BigInteger construction in `carried_crossings`") | mesh_ops/ExactMeshBoolean.cs, math/FixedPoint3.cs, the compiler's spec (needs inputs with their own bounds, FIXED_WIDTH_EXACT_POINTS.md 12.4) | the allocation task (boolean study lever 4) | 10.8 % of the tail's time is in these three paths (42327: 23 %) and 38 % of its allocated bytes; at 1.8-2.4x: about 5 % of the tail's CPU, 11 % on 42327, plus the GC share of a third of the allocations (GC pauses are 23-27 % of 42327 and 107554 at 4 cores). Normal inputs: 0 | tier L, bit-identical by construction (same determinants), bounds from the compiler; shards mesh-boolean-* and mesh-fixed. 3-5 days. Build only if the owner wants the tail's 10 %; measure on 42327 and 107554 first |
| 4 | Candidate C: **exact point construction on limbs** (arithmetic on the stack, no BigInteger round trip; a limb gcd) | mesh_ops/ExactMeshArrangement.cs, math/FixedPoint3.cs | | the path is 2.0 % / 5.0 % of normal operations, 4.0 % of the tail, 13 % of 519507; at 2x without a limb gcd: 1-2.5 % normal, 6 % on 519507 | tier L; a limb gcd with its own tests. Below the stop rule except on intersection-heavy cases: not now |
| 5 | Candidate D: **InCircle and Orient beyond 4 limbs** on the stack (`specs/wide.spec`: 1228 limbs of scratch at 320 bits) | math/FixedPoint3.cs, mesh_ops/ExactMeshArrangement.cs | the "InCircle limb kernel" item of the roadmap | 1.7 % of the 27 tail operations at 2.2x; on 55928 it is the largest exact item: 19.6 % of the single-thread time and 44 % of the allocated bytes, so about 10 % of its CPU at 2.2x plus the GC share of those bytes (the tail note estimated 20-30 s of 370 s at 4 cores) | tier L; small once B's loading of BigInteger points exists. With or after B |
| | Not to build | | | P1, P3 for double inputs, P4, intermediate-precision filters, an InSphere kernel, a Roslyn generator (sections 3 and 4) | |

Open question for T3a, measured only as a count: on inputs like #711 the arrangement's grid spans 2^840, and its
BigInteger InCircle stages cost about 350 us each. The literature's answer is a mantissa-exponent number (Lévy 2024);
whether the arrangement's points could carry a power-of-two factor per coordinate is not examined here.

## 5a. Normal stratum (owner 2026-11-12: the 10 % stop rule is lifted, gains of a few percent count)

Stratum: normal operations = 60 operations on 20 thingi-pairs and 39 on 13 clean Thingi10K models with their rotated
copy (section 2.2). Shares are single-thread time and include the collections a path's own allocations trigger; the
whole-operation rows are wall time at 4 cores with GC pauses and allocation separate.

**What P2 gains there: nothing.** The stage it replaces is called 0 times in the 99 operations. Measured with the
generated kernels of the PR (master a7693fd against branch `claude/exact-fallback-monomial`, the sum of the
per-operation medians of 5 runs, `scripts/analyze_strata_ab.py results/pr <stratum> 5`):

| stratum | ops | wall time before -> after | range of the 5 run totals, before / after | GC pauses | allocated | hashes equal in all 10 runs |
|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.29 -> 6.24 s (-0.7 %) | 6.21-6.45 / 6.22-6.58 s | 0.13 -> 0.13 s | 2.36 -> 2.36 GB | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.17 -> 4.31 s (+3.1 %) | 4.14-4.47 / 4.16-4.77 s | 0.12 -> 0.12 s | 1.48 -> 1.48 GB | 39 / 39 |

Both differences are inside the run-to-run range (about 4-15 % of a total of 4-6 s).

**Where the exact share of the normal stratum is** (pairs / copies; total 4.7 % / 8.5 % beyond the double filters):

| path | share | calls | ns per call | what a lever could take |
|---|---|---|---|---|
| exact point construction (BigInteger arithmetic, three gcds, conversion to fixed width) | 2.0 % / 5.0 % | 27.7k / 45.0k | 7494 / 8349 | the arithmetic on stack limbs is 3.6x (2389 -> 660 ns), the conversion (1045 ns) and the BigInteger round trip of the inputs go away, the gcds (2699 ns) stay unless a limb gcd is written: about 46 % of the path without it (0.9 % / 2.3 % of the operations), about 62 % with a gcd twice as fast (1.2 % / 3.1 %) |
| triangle planes (`compute_planes`: fixed-width arithmetic, then BigInteger N and D and the double approximations) | 0.9 % / 1.6 % | 34.7k / 40.9k | 2785 / 2995 | keeping N and D as limbs and making the BigInteger copies lazy: about half of the path (0.5 % / 0.8 %); same files as the construction |
| the 62-bit integer stage of `Orient3d` | 0.8 % / 0.3 % | 5.2M / 2.0M | 17 / 11 | nothing per call; its count is the number of exactly coplanar tests |
| `Orient` on fixed-width points | 0.3 % / 0.6 % | 87k / 135k | 297 / 344 | 99 % of these stages are exact zeros (section 3.4): only a zero by construction could skip them |
| `same_plane` | 0.1 % / 0.3 % | | 475 / 653 | under 0.3 % |
| Shewchuk's adaptive stages B-D | 0.1 % / 0.1 % | 29k / 28k | 229 / 266 | nothing: the expansion paths and stage-B kernels are not a lever |
| the double filters (stage A of the predicates and the arrangement's filters) | not in these numbers | | | the boolean study's sampling profile of its normal stratum (36 unions, #734 section 8) has 8.9 % of the CPU samples in the filters next to 7.6 % in the exact stages; a cheaper filter was not prototyped here |

**Ranking by gain per effort (normal stratum):**

1. C: exact point construction on limbs. Bound 2.0 % / 5.0 %, realistic 0.9-1.2 % / 2.3-3.1 % (13 % of 519507 in the
   tail is the same path). Effort M (the three constructions on `FixedArith` with compiler-derived scratch, a limb gcd
   with its own tests, the canonical form unchanged so that hashes stay equal).
2. C': planes without the BigInteger round trip. Bound 0.9 % / 1.6 %, realistic about half. Effort S-M, with C.
3. The filters. Bound 8.9 % of the samples of the boolean study's normal stratum if they were free; what a cheaper
   evaluation (shared differences between the predicates of one pair test, fewer operations) can take of it needs a
   probe first. Effort unknown; not an exact-number topic.
4. Everything else is under 0.5 % of the normal stratum.

GC on the normal stratum: 3.2 % / 3.5 % of single-thread wall time (the boolean study: 8.6 % of the wall time of its
stratum, 1.4 % in the median operation); 3.8 % / 8.6 % of the allocated bytes come from the BigInteger paths, so
C and C' also remove most of that small share.

## 5b. Candidate B, the probe before building it (2026-11-13)

Owner decision: after PR 1 the candidates B, C, D, each after a small probe that confirms a whole-operation gain above
noise (median of 5, normal stratum and tail separately). Commit set: library c0f8aa9 with the study's instrumentation
(`instrumentation.patch`, which now holds `math/LimbProto.cs`), probe `Probe/`, 4-core cloud container, .NET SDK
10.0.115, Release, workstation GC. Every share below names its stratum and says whether collections are included.

**What was probed.** The classification's BigInteger sign stages of `ExactMeshBoolean` on stack limbs with the
library's `FixedArith` (`EXACT_PROTO=limbs`): the plane side `A X + B Y + C Z - D W` (five call sites), the dot of two
normals (two) and the 4 x 4 orientation of four homogeneous points (`orient3d_exact`). The operands stay BigInteger
and are loaded into stack limbs per call (`TryWriteBytes`, the two's complement negated in place): the weaker of the
two variants of section 3 (P3b), so the result is a lower bound of B with limbs stored at the points. The centroid
construction (`centroid_point`, a result that is kept) is not in it; that is candidate C.

**Signs.** With `EXACT_PROTO=limbs,check` every limb result is compared with the BigInteger expression: 0 mismatches
on 107554 (three operations: 2.4 million side calls, 215 thousand orientations each). The result hash, the status
and the counts are equal in every run below.

**Upper bound first** (stratum: slow tail, one thread, collections triggered by a path's own allocations included;
rows of section 2): on 42327 the three stages are 12.6 % (side), 4.9 % (orientation) and 7.2 % (centroid) of the
operation, and 15.2 of 21 GB of its allocation; on 107554 2.5 %, 2.1 % and 6 %. With the micro factors of section 3
(side 627 -> 446 ns loaded, 317 ns stored; orientation 9.3 -> 3.5 us) the CPU part of B is at most about 7 % (loaded)
to 10 % (stored) on 42327 and 2.5 % on 107554.

**Measured** (5 alternating runs each, timers off):

## tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections
| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |
|---|---|---|---|---|---|---|---|---|
| copy_107554 | 0 | 5/5 | 26417 (25798-26657) | 26214 (24076-26657) | -0.8 % | 5341 -> 4773 | 12.00 -> 10.49 | True |
| copy_107554 | 1 | 5/5 | 17089 (16466-17431) | 16372 (15638-16846) | -4.2 % | 4986 -> 4286 | 10.03 -> 8.51 | True |
| copy_107554 | 2 | 5/5 | 17386 (17213-18206) | 16572 (16100-17145) | -4.7 % | 5159 -> 4366 | 10.00 -> 8.49 | True |
| copy_42327 | 0 | 5/5 | 34476 (33528-34762) | 29638 (28603-30393) | -14.0 % | 6656 -> 3543 | 22.99 -> 10.17 | True |
| copy_42327 | 1 | 5/5 | 26908 (25654-27059) | 22319 (21699-25640) | -17.1 % | 6685 -> 3484 | 20.59 -> 7.77 | True |
| copy_42327 | 2 | 5/5 | 27001 (25557-28006) | 22857 (21794-23706) | -15.3 % | 6606 -> 3415 | 20.63 -> 7.81 | True |

## normal strata (4 cores, timers off): the sum of the per-operation medians
| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |
|---|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.32 (6.12-6.84) | 6.14 (5.95-6.51) | -2.8 % | 0.12 -> 0.12 | 2.36 -> 2.36 | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.14 (4.00-4.37) | 4.22 (4.13-4.38) | +2.0 % | 0.12 -> 0.11 | 1.48 -> 1.48 | 39 / 39 |

One thread, timers on (stratum: slow tail; a path's time includes the collections its allocations trigger), base row
of section 2 against the prototype: 42327 -7.1 / -8.9 / -11.4 % of the operation, the orientation stage 3.4 -> 1.9 s,
the side stage 9.0 -> 8.1 s (loading eight operands per call costs about what the BigInteger temporaries cost; only
the stored form of C makes it faster, 2 x in the micro benchmark), allocation 20.6 -> 7.8 GB; 107554 -1.3 / -11.7 /
-6.7 % (one run each, so the spread of single runs applies), the orientation stage 0.8 -> 0.45 s, the side stage
unchanged.

**Reading.**
- Slow tail, 4 cores, collections included: -14 to -17 % on 42327 (the ranges of base and prototype do not overlap in
  two of three operations and touch in the third), -4 to -5 % on two of the three operations of 107554 and nothing on
  the first. About two thirds of the gain on 42327 and all of it on 107554 is the pause of the collector (6.6 -> 3.5 s
  and 5.0 -> 4.3 s per operation): the stages allocate 12.8 GB less. Without the pauses 42327 gains about 8 %.
- Normal stratum, 4 cores, collections included: -2.8 % and +2.0 %, inside the run-to-run range (the totals overlap),
  allocation unchanged. The stages are hardly reached there. No gain, no cost.
- So B is confirmed for the slow tail and neutral for normal operations. In the loaded form it is mostly an
  allocation and collector gain; the CPU gain of the side stage needs the limbs stored with the points, which is the
  representation of candidate C. That argues for building B and C on one point representation: B's kernels first
  (they work on loaded operands and already give the tail gain), C's stored limbs under them next.

## 5c. Candidate C, part 1: the centroid on stack limbs (2026-11-13): not confirmed

`centroid_point` of `ExactMeshBoolean` (the centroid of an output piece, a homogeneous point that is kept) computed
with `FixedArith` on stack limbs and converted back to an `ExactPoint3` (`LimbProto.Centroid`, `EXACT_PROTO=limbs,centroid`),
against the B prototype alone (`EXACT_PROTO=limbs`); instrumented library on c0f8aa9, 5 alternating runs, medians;
"base" in the tables is B, "limbs" is B + centroid. Rows: `results/c`. The prototype's centroids equal the BigInteger
construction in all 795,531 calls per operation on 107554 (`EXACT_PROTO=...,check`), and every result hash is equal.

#### tail models (4 cores, timers off): per operation, median of the runs; ms is wall time with collections
| case | op | runs | base ms (range) | limbs ms (range) | change | GC pause ms base -> limbs | allocated GB base -> limbs | hash equal |
|---|---|---|---|---|---|---|---|---|
| copy_107554 | 0 | 5/5 | 26371 (25347-26808) | 25011 (24391-25347) | -5.2 % | 4649 -> 4319 | 10.49 -> 10.22 | True |
| copy_107554 | 1 | 5/5 | 16568 (16056-17505) | 16214 (15891-16860) | -2.1 % | 4368 -> 4064 | 8.51 -> 8.25 | True |
| copy_107554 | 2 | 5/5 | 16385 (15692-17680) | 16276 (16102-16756) | -0.7 % | 4376 -> 4087 | 8.49 -> 8.23 | True |
| copy_42327 | 0 | 5/5 | 29638 (29394-30964) | 29795 (28686-30208) | +0.5 % | 3309 -> 3286 | 10.17 -> 9.88 | True |
| copy_42327 | 1 | 5/5 | 22223 (21263-23342) | 23034 (22642-24749) | +3.6 % | 3233 -> 3266 | 7.77 -> 7.47 | True |
| copy_42327 | 2 | 5/5 | 23025 (21781-24458) | 22755 (22197-23130) | -1.2 % | 3556 -> 3062 | 7.81 -> 7.52 | True |

#### normal strata (4 cores, timers off): the sum of the per-operation medians
| stratum | operations | base s (range of the run totals) | limbs s (range) | change | GC pause s | allocated GB | hashes equal |
|---|---|---|---|---|---|---|---|
| 20 pairs | 60 | 6.17 (6.06-6.45) | 6.17 (5.88-6.47) | +0.0 % | 0.13 -> 0.13 | 2.36 -> 2.35 | 60 / 60 |
| 13 models with a rotated copy | 39 | 4.39 (4.10-4.60) | 4.29 (4.10-4.56) | -2.2 % | 0.12 -> 0.11 | 1.48 -> 1.48 | 39 / 39 |

#### single thread, timers on: B (limbs) against B + centroid on limbs (whole operation; the centroid stage with the collections it triggers; its allocation)
| case | op | operation ms | centroid stage s | centroid MB | allocated GB | hash equal |
|---|---|---|---|---|---|---|
| copy_107554 | 0 | 48046 -> 49269 (+2.5 %) | 2.33 -> 2.57 | 1028 -> 759 | 10.5 -> 10.3 | True |
| copy_107554 | 1 | 35030 -> 35302 (+0.8 %) | 2.26 -> 2.47 | 1028 -> 759 | 8.5 -> 8.2 | True |
| copy_107554 | 2 | 34386 -> 35518 (+3.3 %) | 2.23 -> 2.56 | 1028 -> 759 | 8.5 -> 8.2 | True |
| copy_42327 | 0 | 71500 -> 73483 (+2.8 %) | 4.68 -> 4.77 | 2044 -> 1743 | 10.2 -> 9.9 | True |
| copy_42327 | 1 | 62973 -> 64452 (+2.3 %) | 4.50 -> 4.86 | 2044 -> 1743 | 7.8 -> 7.5 | True |
| copy_42327 | 2 | 62322 -> 64939 (+4.2 %) | 4.81 -> 5.04 | 2044 -> 1743 | 7.8 -> 7.5 | True |

Reading: on the slow tail (4 cores, wall time with collections) the six operations change by -5.2 to +3.6 % and the
ranges of the 5 runs overlap in every row; the collector's pause and the allocation (-0.3 GB of 7.8 to 10.5 GB per
operation) hardly move. On the normal strata nothing changes. On one thread the operation is 0.8 to 4.2 % slower and the
stage itself does not get faster: the limbs save the temporaries (a quarter of the stage's allocation), but the result
has to become four BigIntegers again, and the conversion both ways costs what the temporaries cost. This is the finding
of the side stage in B again: a stage on limbs pays only where the conversion does not, and a construction gains only
if its result is stored as limbs. The centroid alone is dropped (coordinator, 2026-11-13); part 2 measures the upper
bound of points stored as limbs before any design.

## 5d. Candidate C, part 2: the upper bound of points kept as limbs (2026-11-13, measurement only)

What a representation that keeps points as limbs through the construction and the classification could remove at
most. One thread (`EXACT_SERIAL=1`, core 0, no concurrent GC), timers on, instrumented library on c0f8aa9 with the B
prototype (`EXACT_PROTO=limbs`); a region's time includes the collections its own allocation triggers and about 80 ns
of timer per call; shares are of the stratum's summed operation time and allocation. Rows: `results/c3`
(`results/c2`: the same without the conversion counters). New regions: the lazy exact point construction
(`exact_ref`) split into its arithmetic by kind (`arr.pt.edge`, `.segments`, `.planes`: `compute_exact` without the
reduction, with the fetch of the parent points), the gcd reduction (`arr.pt.reduce`) and the store
(`arr.pt.publish`: the conversion to fixed width or the BigInteger box); the conversion of a stored fixed-width point
to BigIntegers in `Exact(id)`, outside the timed regions and inside them (`arr.to_exact.*`).

#### normal, 20 pairs: 60 operations, 10.86 s, GC pause 0.37 s, allocated 2.31 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 27736 | 258 | 2.37 % | 54 | 2.3 % | 9.3 |
| arr.pt.input | 202 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 27534 | 146 | 1.34 % | 41 | 1.7 % | 5.3 |
| arr.pt.reduce | 27534 | 93 | 0.86 % | 8 | 0.3 % | 3.4 |
| arr.pt.publish | 27736 | 14 | 0.13 % | 6 | 0.2 % | 0.5 |
| bool.centroid.big | 642 | 7 | 0.06 % | 1 | 0.1 % | 10.7 |
| bool.side.big | 1189 | 3 | 0.02 % | 0 | 0.0 % | 2.1 |
| arr.orient.big | 518 | 1 | 0.01 % | 1 | 0.0 % | 2.3 |
| arr.compare.big | 45 | 0 | 0.00 % | 0 | 0.0 % | 8.9 |
| arr.same_plane.big | 20025 | 12 | 0.11 % | 3 | 0.1 % | 0.6 |
| arr.local_get.big | 18 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.planes | 34669 | 109 | 1.00 % | 31 | 1.3 % | 3.1 |
| arr.to_exact.outside | 28750 | 6 | 0.05 % | 4 | 0.2 % | 0.2 |
| arr.to_exact.inside | 58010 | 9 | 0.08 % | 6 | 0.3 % | 0.2 |
| pt.fixed | 60265 | 0 | 0.00 % | 9 | 0.4 % | 0.0 |
| pt.box | 640 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### normal, 13 models with a rotated copy: 39 operations, 6.70 s, GC pause 0.27 s, allocated 1.46 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 45004 | 328 | 4.89 % | 89 | 5.9 % | 7.3 |
| arr.pt.input | 256 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |
| arr.pt.edge | 44748 | 171 | 2.55 % | 68 | 4.5 % | 3.8 |
| arr.pt.reduce | 44748 | 127 | 1.90 % | 13 | 0.9 % | 2.8 |
| arr.pt.publish | 45004 | 21 | 0.31 % | 8 | 0.5 % | 0.5 |
| bool.centroid.big | 449 | 5 | 0.08 % | 1 | 0.0 % | 11.6 |
| bool.side.big | 828 | 3 | 0.04 % | 0 | 0.0 % | 3.1 |
| arr.orient.big | 1126 | 6 | 0.09 % | 1 | 0.1 % | 5.2 |
| arr.compare.big | 114 | 1 | 0.02 % | 0 | 0.0 % | 9.6 |
| arr.same_plane.big | 30669 | 23 | 0.34 % | 4 | 0.3 % | 0.8 |
| arr.local_get.big | 51 | 0 | 0.00 % | 0 | 0.0 % | 2.0 |
| arr.planes | 40924 | 114 | 1.70 % | 33 | 2.2 % | 2.8 |
| arr.to_exact.outside | 31440 | 7 | 0.10 % | 5 | 0.3 % | 0.2 |
| arr.to_exact.inside | 93140 | 12 | 0.19 % | 9 | 0.6 % | 0.1 |
| pt.fixed | 79744 | 0 | 0.00 % | 12 | 0.8 % | 0.0 |
| pt.box | 1140 | 0 | 0.00 % | 0 | 0.0 % | 0.0 |

#### slow tail, copy_107554: 3 operations, 133.35 s, GC pause 11.23 s, allocated 27.52 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 1334763 | 7604 | 5.70 % | 2419 | 8.6 % | 5.7 |
| arr.pt.input | 35295 | 24 | 0.02 % | 8 | 0.0 % | 0.7 |
| arr.pt.edge | 1038825 | 2833 | 2.12 % | 1397 | 5.0 % | 2.7 |
| arr.pt.segments | 106275 | 477 | 0.36 % | 198 | 0.7 % | 4.5 |
| arr.pt.planes | 154368 | 776 | 0.58 % | 316 | 1.1 % | 5.0 |
| arr.pt.reduce | 1299468 | 2814 | 2.11 % | 293 | 1.0 % | 2.2 |
| arr.pt.publish | 1334763 | 457 | 0.34 % | 207 | 0.7 % | 0.3 |
| bool.centroid.big | 2386593 | 8000 | 6.00 % | 3085 | 10.9 % | 3.4 |
| bool.side.big | 7215738 | 3767 | 2.82 % | 1 | 0.0 % | 0.5 |
| bool.orient3d.big | 645702 | 1534 | 1.15 % | 0 | 0.0 % | 2.4 |
| arr.incircle.big | 224319 | 3220 | 2.41 % | 1363 | 4.8 % | 14.4 |
| arr.orient.big | 126821 | 236 | 0.18 % | 165 | 0.6 % | 1.9 |
| arr.compare.big | 89270 | 293 | 0.22 % | 80 | 0.3 % | 3.3 |
| arr.along.big | 114444 | 169 | 0.13 % | 121 | 0.4 % | 1.5 |
| arr.same_plane.big | 357417 | 233 | 0.17 % | 34 | 0.1 % | 0.7 |
| arr.local_get.big | 184179 | 222 | 0.17 % | 85 | 0.3 % | 1.2 |
| arr.edge_planes.big | 55392 | 202 | 0.15 % | 109 | 0.4 % | 3.6 |
| arr.planes | 405735 | 1175 | 0.88 % | 234 | 0.8 % | 2.9 |
| arr.to_exact.outside | 6631103 | 1435 | 1.08 % | 727 | 2.6 % | 0.2 |
| arr.to_exact.inside | 10766445 | 2361 | 1.77 % | 1286 | 4.6 % | 0.2 |
| pt.fixed | 1462437 | 0 | 0.00 % | 223 | 0.8 % | 0.0 |
| pt.box | 235389 | 0 | 0.00 % | 50 | 0.2 % | 0.0 |

#### slow tail, copy_42327: 3 operations, 218.83 s, GC pause 7.38 s, allocated 25.82 GB (one thread, timers on)
| region | calls | ms | share of the time | MB | share of the allocation | us per call |
|---|---|---|---|---|---|---|
| arr.exact_point.big | 729870 | 3799 | 1.74 % | 1480 | 5.6 % | 5.2 |
| arr.pt.input | 4395 | 2 | 0.00 % | 1 | 0.0 % | 0.6 |
| arr.pt.edge | 294111 | 768 | 0.35 % | 399 | 1.5 % | 2.6 |
| arr.pt.segments | 210294 | 447 | 0.20 % | 393 | 1.5 % | 2.1 |
| arr.pt.planes | 221070 | 763 | 0.35 % | 386 | 1.5 % | 3.5 |
| arr.pt.reduce | 725475 | 1459 | 0.67 % | 190 | 0.7 % | 2.0 |
| arr.pt.publish | 729870 | 236 | 0.11 % | 112 | 0.4 % | 0.3 |
| bool.centroid.big | 3779985 | 16501 | 7.54 % | 6131 | 23.2 % | 4.4 |
| bool.side.big | 47816814 | 27458 | 12.55 % | 0 | 0.0 % | 0.6 |
| bool.orient3d.big | 2321109 | 6312 | 2.88 % | 0 | 0.0 % | 2.7 |
| arr.incircle.big | 86088 | 1142 | 0.52 % | 582 | 2.2 % | 13.3 |
| arr.orient.big | 149739 | 256 | 0.12 % | 220 | 0.8 % | 1.7 |
| arr.compare.big | 379728 | 342 | 0.16 % | 347 | 1.3 % | 0.9 |
| arr.along.big | 290307 | 372 | 0.17 % | 313 | 1.2 % | 1.3 |
| arr.same_plane.big | 48537 | 8 | 0.00 % | 5 | 0.0 % | 0.2 |
| arr.local_get.big | 267399 | 316 | 0.14 % | 137 | 0.5 % | 1.2 |
| arr.edge_planes.big | 4833 | 180 | 0.08 % | 10 | 0.0 % | 37.2 |
| arr.planes | 27477 | 54 | 0.02 % | 14 | 0.1 % | 2.0 |
| arr.to_exact.outside | 5626263 | 1508 | 0.69 % | 683 | 2.6 % | 0.3 |
| arr.to_exact.inside | 13738706 | 3415 | 1.56 % | 2031 | 7.7 % | 0.2 |
| pt.fixed | 756294 | 0 | 0.00 % | 115 | 0.4 % | 0.0 |
| pt.box | 160617 | 0 | 0.00 % | 36 | 0.1 % | 0.0 |


Bounds per stratum (the whole region removed, which no implementation reaches) and what is realistic:

| region | normal, 20 pairs | normal, 13 copies | tail, 107554 | tail, 42327 | realistic |
|---|---|---|---|---|---|
| exact point construction, time / allocation | 2.4 % / 2.3 % | 4.9 % / 5.9 % | 5.7 % / 8.6 % | 1.7 % / 5.6 % | under half: the arithmetic is about half of the region (3.6x on limbs, section 5a), the reduction 40 % (a limb gcd would have to beat BigInteger's), the store 6 % |
| centroid (`bool.centroid.big`) | 0.06 % / 0.1 % | 0.08 % / 0.0 % | 6.0 % / 10.9 % | 7.5 % / 23.2 % | about half, only if the centroid is stored as limbs (section 5c: with a conversion back, nothing) |
| loads in B's stages (side and orientation; the load is about half of the side stage, section 5b) | 0.01 % | 0.02 % | about 1.7 % | about 7.1 % | most of it, if planes and centroids are limbs |
| fixed-width point to BigInteger outside the regions | 0.05 % / 0.2 % | 0.10 % / 0.3 % | 1.1 % / 2.6 % | 0.7 % / 2.6 % | about half of the time is the timer |
| sum of the bounds, time | 2.5 % | 5.1 % | 14.5 % | 17 % | normal 1.2 % / 2.5 %, tail about 8 % / 12 % |

Reading:
- Normal stratum: the bound itself (2.5 % and 5.1 % of one thread's time if the construction cost nothing) is at the
  run-to-run range of the stratum's totals (4 cores: 6.06-6.45 s and 4.10-4.60 s today), and the realistic gain of
  1.2 % / 2.5 % is below it. The construction on limbs (the C of section 5) cannot be confirmed by a whole-operation
  measurement there; these are the numbers of section 5a again (2.0 % / 5.0 %), now with the split.
- Slow tail: the bound is above noise, and three quarters of it is not the construction but the classification: the
  centroid and the loads of B's stages. It needs the centroid kept as limbs (one array per centroid instead of four
  BigIntegers and their temporaries) and the planes' coefficients cached as limbs, both inside `ExactMeshBoolean`'s
  classification. The allocation these regions own is 22 % (107554) and 31 % (42327) of the operation's.
- The hypothesis that the conversion of stored fixed-width points to BigIntegers owns the unattributed allocation of
  the tail is wrong: 17 to 19 million conversions per model make 7 to 10 % of the allocation, most of it inside the
  regions above. About 60 % of the tail's allocation is outside every exact region (not an exact-number topic).
- T3a's lever 1 (#767) changes how often the classification's stages run (47.8 million side calls in the three
  operations on 42327); the bound of the classification part has to be measured again after it.

Decision (coordinator, 2026-11-13): (a) the construction part of C (exact point construction on limbs, a limb gcd)
is dropped: no measurable gain on the normal stratum, 1 to 3 % on the tail. (b) The centroid and the planes'
coefficients as limbs in the classification stay parked until T3a's lever 1 (#767) has landed and are then measured
again with this probe; T3a has the sketch as information. (c) D goes on as a probe only; its files are T3a's, so a
design goes to T3a and the coordinator before any code, and a PR only if the whole-operation gain clears the noise.

## 5e. Candidate D, the probe: InCircle and Orient beyond the fixed width on stack limbs (2026-11-13)

The arrangement decides `Orient` and `InCircle` on points that do not fit the fixed width (4 limbs per coordinate)
with BigInteger determinants (`arr.orient.big`, `arr.incircle.big`). Prototype (`LimbProtoD.cs`,
`proto_d_call_sites.patch`; on the head of #772, 93879ee, so on current master with B): the same determinants with
`FixedArith` on stack limbs, the operands copied from the points' BigIntegers per call as in B
(`EXACT_PROTO=incircle,orient`; with `check` every sign is compared with the BigInteger stage: 0 mismatches in
117,093 stages of 107554's union). Scratch 120 (wv + wd) + 200 limbs for InCircle (wv, wd: the widest operand of the
three points and of the fourth), 34 w + 32 for Orient; the widest call seen has wv + wd = 12 limbs; no call fell back.

4 cores, timers off, wall time with collections, 5 alternating runs, medians (range); rows `results/d`:

| case | operation | base | limbs | change | GC pause | allocated | hash equal | stages on limbs per operation (InCircle / Orient) |
|---|---|---|---|---|---|---|---|---|
| 55928 with its rotated copy | difference | 371.0 s (360.3-375.7) | 318.8 s (302.4-330.5) | -14.1 % | 83.8 -> 64.6 s | 180.1 -> 93.8 GB | yes | 12,426,168 / 1,093,971 |
| 107554 | union | 21.4 s (20.7-22.1) | 21.1 s (20.8-21.6) | -1.4 % | 4.4 -> 4.4 s | 9.2 -> 8.8 GB | yes | 74,773 / 42,320 |
| 107554 | intersection | 16.7 s (16.1-17.2) | 15.9 s (15.3-17.2) | -5.0 % | 4.2 -> 4.1 s | 8.5 -> 8.1 GB | yes | 74,773 / 42,250 |
| 107554 | difference | 16.7 s (16.3-17.5) | 16.2 s (15.6-16.9) | -3.5 % | 4.4 -> 4.1 s | 8.5 -> 8.0 GB | yes | 74,773 / 42,251 |
| 42327 | union | 22.3 s (21.2-24.1) | 23.2 s (22.7-24.1) | +4.1 % | 3.3 -> 3.4 s | 7.6 -> 7.4 GB | yes | 28,696 / 49,428 |
| 42327 | intersection | 21.1 s (20.7-23.3) | 22.7 s (20.9-23.3) | +7.4 % | 3.3 -> 3.2 s | 7.3 -> 7.1 GB | yes | 28,696 / 50,727 |
| 42327 | difference | 21.1 s (20.2-23.0) | 23.0 s (20.6-23.4) | +8.8 % | 3.2 -> 3.3 s | 7.4 -> 7.2 GB | yes | 28,696 / 49,584 |
| normal, 20 pairs | 60 operations, sum of the medians | 6.28 s (6.30-6.43) | 6.10 s (6.00-6.32) | -2.8 % | | | 60 / 60 | 0 / 518 in all |
| normal, 13 models with a rotated copy | 39 operations | 4.17 s (4.04-4.29) | 4.32 s (4.27-4.49) | +3.5 % | | | 39 / 39 | 0 / 1126 in all |

Reading:
- 55928 (the model on which this stage is the largest exact item): -14.1 %, the ranges do not overlap; the collector's
  pause drops by 19 s and the allocation by 86 GB (48 %). Of the 52 s gained, 19 s are the pause.
- 107554: -1.4 to -5.0 %, ranges overlapping: at most a small gain (the stage is 2.3 % of its one-thread time).
- 42327: the medians are 4 to 9 % HIGHER with overlapping ranges. The prototype runs 78 thousand stages per operation
  there (0.6 % of its one-thread time), which cannot cost seconds. Repeated on 42327 alone, 3 alternating rounds
  (`only42327_*`): 24.1 -> 24.7 s, 20.9 -> 21.2 s, 20.6 -> 21.9 s (+2.5 %, +1.4 %, +6 %), ranges overlapping. No
  mechanism found; counted as noise of this model on this machine (its run-to-run range is 10 %), and to be checked
  again on a built version, which must not be slower there.
- Normal strata: the stage is not reached (0 InCircle stages, under 30 Orient stages per operation); both differences
  are inside the run-to-run range.

So D is a gain for the class of 55928 (many InCircle tests on points beyond the fixed width) and nothing measurable
elsewhere.

## 5f. Candidate D, built, and the spread of 42327's classification between processes (2026-11-13)

Built on the branch of B (d555e22): `ExactLimbSigns.Orient2` / `InCircle`, the call sites `orient_exact` /
`incircle_exact` of `ExactMeshArrangement`; the local branch is in `d/` as patches. Before = d555e22, after = the
branch. 4 cores, timers off, wall time with collections, alternating runs (`results/d2`):

| stratum | operation | runs | before | after | change | GC pause | allocated |
|---|---|---|---|---|---|---|---|
| slow tail | 55928 with its rotated copy, difference | 5 | 352.8 s (349.1-376.2) | 310.4 s (291.8-331.3) | -12.0 % | 78.4 -> 63.0 s | 180.1 -> 93.8 GB |
| slow tail | 107554, union / intersection / difference | 5 | 23.5 / 17.9 / 18.1 s | 23.5 / 17.6 / 18.1 s | -0.1 / -1.2 / 0.0 % | 5.0 -> 5.1 s | 9.2 -> 8.8 GB |
| slow tail | 42327, union / intersection / difference | 21 | 24.4 / 22.6 / 22.7 s | 24.7 / 22.3 / 23.0 s | +1.2 / -1.3 / +1.3 % | 3.7 -> 3.7 s | 7.6 -> 7.4 GB |
| normal | 20 pairs, 60 operations | 5 | 6.79 s (6.72-7.09) | 6.70 s (6.68-7.06) | -1.3 % (inside the range) | 0.14 -> 0.15 s | 2.36 -> 2.36 GB |
| normal | 13 models with a rotated copy, 39 operations | 5 | 4.50 s (4.38-4.74) | 4.43 s (4.30-4.66) | -1.5 % (inside the range) | 0.13 -> 0.13 s | 1.48 -> 1.48 GB |

Hashes equal in every run. Coordinator: D passes.

The first series of 5 (107554, then 42327 in one process) showed 42327 at +2.1 / +9.1 / +10.3 %. What it was
(66 processes, 33 per side; `scripts/analyze_slowmode.py`, `scripts/analyze_slowcounts.py`):

- The classification of 42327 takes 12 to 16 s for the same work: every counter of the stage (plane sides
  9,715,682, exact 2,033,164, traced 142,003, carried 595,003, exact centroids 273,907, ...), the allocation and the
  hash are equal in all 66 processes. A slow process burns more CPU (65-70 -> 78-86 s per operation), the
  collector's pause and the rest of the operation (9 to 10 s) do not change.
- It is a state of the process and of this stage on this model: 107554 before or after it in the same process is at
  its normal time; the three operations of a process mostly share it; it switched between operations three times
  (fast to slow once, slow on the first operation only twice).
- Both sides: median classification of a process >= 14.4 s in 3 of 33 before, 5 of 33 after. The slow processes are
  adjacent pairs of the alternating schedule (after: rounds 3 and 4 of the first series; before: rounds 4 and 5 of the
  reversed one), which made each series of 5 look one-sided. Per-process sums of the six operations: 107554 first
  130.1 -> 136.9 s, 42327 first 129.7 -> 124.1 s, 107554 first with `DOTNET_TieredPGO=0` 130.1 -> 131.1 s.
- Checked and not the cause: the collector's pause; concurrent collection (`gcConcurrent=0`: 1 of 8 slow); the size
  of the thread pool (19 to 27 threads after every operation in every process, fast or slow; with
  `DOTNET_ThreadPool_ForceMaxWorkerThreads=4` slow classifications remain and nothing gets faster, `results/d4`); a
  difference in work; the per-process seed of `BigInteger.GetHashCode` (#789) alone (the state switches inside a
  process).
- The cause is the tiered JIT's dynamic PGO (`results/d5`, `scripts/slowmode_pgo.sh`: one 42327 union per process,
  40 processes per setting, alternating, the base binary): by default the classification takes 13.92 s in the median
  (12.46-16.43), 11 of 40 processes at 14.4 s or more; with `DOTNET_TieredPGO=0` 12.76 s (11.90-13.97), 0 of 40
  (Fisher, one-sided, p = 0.0002). The whole operation: 25.43 -> 24.06 s (-5.4 %), CPU 76.0 -> 70.5 s; counters,
  allocation and hash equal in all 80. Which methods is not known. Issue #825.

Wrong turns, for the record: concurrent GC (the run that seemed to show it had no comparison), then oversubscription
of the thread pool (the summed stage times of the instrumented run, `results/d3`, are inflated by the 20 pool threads
in every process, not only in the slow ones).

For A/B runs on this model: one slow process moves a median of 5 by up to 2.5 s. Run both sides with
`DOTNET_TieredPGO=0`, or compare per process, look at the classification stage and the counters before reading a
regression, and use clearly more than 5 processes per side.

## 6. Literature (papers only)

Marks: [V] checked against the paper or its abstract in this study, [S] from a citing paper or a search result.

- **Shewchuk 1997**, Adaptive precision floating-point arithmetic and fast robust geometric predicates, DCG 18 [V]:
  "does not address issues of overflow and underflow"; the source of the adaptive stages and of P1's building blocks
  (Two-Product, Scale-Expansion, Fast-Expansion-Sum).
- **Kulisch and Snyder 2011**, The exact dot product as basic tool for long interval arithmetic, Computing 91 [S]: a
  fixed-point register of 4288 bits holds any sum of two-factor products of doubles. P2's accumulator is this object
  for products of degree 2-5 (the limb counts above are this formula extended, derived here).
- **Collange, Defour, Graillat, Iakymchuk 2015** (Parallel Computing 49) [V abstract]; **Neal 2015**
  (arXiv:1505.05571) [V abstract]; **Zhu and Hayes 2009 / 2010** [S]; **Demmel and Hida 2004** [S]: exact sums with
  superaccumulators or exponent buckets; sums and dot products only, no geometric predicate.
- **Ratschek and Rokne 1999** (ESSA) [S]; **Gavrilova, Ratschek, Rokne 2000**, Exact computation of Delaunay and
  power triangulations [S]: the determinant expanded into a sum and the exact sign of the sum; double summands and
  iterative cancellation instead of an accumulator.
- **Mascarenhas 2021**, Computing the exact sign of sums of products with floating point arithmetic,
  arXiv:2109.07838 [V]: the closest statement of the problem (sign of a sum of products of doubles, underflow-safe,
  a double with an integer exponent); heaps of scaled doubles, cost exponential in the degree.
- **Fortune and Van Wyk 1996**, Static analysis yields efficient exact integer arithmetic, ACM TOG 15 [V via
  Shewchuk]: per-predicate generated, allocation-free multi-digit integer code with a static filter; cannot be used
  when the exponents of the coordinates vary. The model for the PredicateCompiler's fixed-width stages.
- **Ozaki, Bünger, Ogita, Oishi, Rump 2016**, Simple floating-point filters for the two-dimensional orientation
  problem, BIT 56 [V abstract]; **Bartels, Fisikopoulos, Weiser 2023**, Fast floating-point filters for robust
  predicates, BIT 63 [V]: filters that stay valid under underflow (an underflow-protected bound, 1-4 % cost; a
  counterexample to an unguarded stage A). The library's filters already guard this (`UnderflowLimit`,
  `UnderflowUnit`); they make the filter fail safely, not the exact stage faster.
- **Meyer and Pion 2008** (FPG) [S]; **Devillers and Pion 2003** [V abstract]; **Burnikel, Funke, Seel 2001**
  (EXPCOMP) [S]; **Brönnimann, Burnikel, Pion 2001** [S]; **Nanevski, Blelloch, Harper 2003** [S]: generated static,
  semi-static and interval filters; the exact stage behind them is arbitrary precision.
- **Lévy 2016** (PCK, CAD 72) [V abstract]; **Lévy 2024 / 2025**, Exact predicates, exact constructions and
  combinatorics for mesh CSG, arXiv:2405.12949 [V, partial]: an expansion kernel and a multi-precision kernel m 2^e
  (integer mantissa without trailing zeros, 32-bit exponent), built because in_circle on intersection points
  exhausts the exponents of expansions; the multi-precision kernel is up to about 6x faster than expansions on hard
  models (55928: 298 s against 46 s there). This is the published form of "expansions do not survive wide spans".
- **Pion and Fabri 2011**, A generic lazy evaluation scheme for exact geometric computations, SCP [V preprint];
  **Karamcheti, Li, Pechtchanski, Yap 1999** (CORE) [S]; **Burnikel, Funke, Mehlhorn, Schirra, Schmitt 2009** (the
  separation bound of leda_real) [S]: interval approximations and a DAG, exact rationals on demand. The library's
  approximate-then-exact points are this scheme without the DAG.
- **Boissonnat and Preparata 2000**, Robust plane sweep for intersecting segments, SIAM J. Comput. 29 [S]: the degree
  of a predicate fixes the precision it needs; the basis of every bound table here.
- **Attene 2020**, Indirect predicates for geometric constructions, CAD 126 [V]; **Cherchi, Livesu, Scateni, Attene
  2020**; **Cherchi, Pellacini, Attene, Livesu 2022**; **Diazzi, Panozzo, Vaxman, Attene 2023** [V text search]:
  filter, intervals, expansions; none states a coordinate range or treats overflow and underflow.
- **Nehring-Wirxel, Trettner, Kobbelt 2021** (CAD 135) [V]; **Trettner, Nehring-Wirxel, Kobbelt 2022** (EMBER) [S]:
  fixed 128 / 192 / 256-bit integers chosen by a bound analysis, input quantized to about 26 bits; at least 10x
  faster than GMP. Fixed width works once the spread is bounded, which is what the 4-limb points do here.
- **Guo and Fu 2024**, Exact and efficient intersection resolution for mesh arrangements, ACM TOG 43 [V abstract]:
  offset predicates that make the filters succeed more often; complementary.
- **Burnikel et al.** on cascaded filters is covered by EXPCOMP above. Not found: a published expansion with a
  separate integer exponent under that name; a SIMD or branch-free multi-limb predicate kernel; a library paper
  that bounds the supported exponent range of double input.

Prior art of P2: not found in this form (monomials of degree 3-5 of m 2^e inputs added into a fixed-point
accumulator for orientation and incircle signs). It is a composition of Kulisch's accumulator, the
sign-of-a-sum reduction of Ratschek, Rokne and Gavrilova, and the generated straight-line integer code of Fortune
and Van Wyk; `docs/SOURCES.md` should list these three with Shewchuk.

## 7. Reproduce

    # build the instrumented probe (a scratch worktree of master c0f8aa9 with instrumentation.patch applied)
    git worktree add --detach <lib> c0f8aa9 && (cd <lib> && git apply <branch>/runs/exact-numbers/instrumentation.patch)
    dotnet build Probe -c Release -p:G3PredicateStats=true -p:PathStats=true -p:G3Root=<lib> -o bin/inst
    python3 scripts/fetch_cases.py <repo> <mesh dir> data          # Thingi10K at run time, the case lists
    # shares, one thread
    EXACT_SERIAL=1 DOTNET_gcConcurrent=0 taskset -c 0 dotnet bin/inst/BooleanComparison.dll list data/cases_tail_small.txt results/tail_small.jsonl
    python3 scripts/analyze_paths.py tail_small=results/tail_small.jsonl --per-case
    # the fuzz units: MeshFuzzPilot dump of runs/mesh-fuzz grp-a units (MESH_FUZZ_OPS=union,intersection,difference)
    dotnet bin/inst/BooleanComparison.dll list data/cases_fuzz_slow.txt results/fuzz_base.jsonl
    EXACT_PROTO=dyadic dotnet bin/inst/BooleanComparison.dll list data/cases_fuzz_slow.txt results/fuzz_dyadic.jsonl
    python3 scripts/analyze_ab.py results/fuzz_base.jsonl P2=results/fuzz_dyadic.jsonl
    # micro benchmarks and bounds
    dotnet bin/inst/BooleanComparison.dll dump data/dump-timeout-251fe.txt --record data/rec711.bin 500000 12
    dotnet bin/inst/BooleanComparison.dll micro rec data/rec711.bin 5 ; ... micro synth 20000 3 ; ... micro implicit 4000 7 ; ... micro construct 20000 9
    python3 scripts/check_bounds.py
    dotnet run --project tools/PredicateCompiler -c Release -- --table specs/wide.spec
