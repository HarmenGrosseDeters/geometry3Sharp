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
