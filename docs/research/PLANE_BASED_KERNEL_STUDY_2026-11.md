# A plane-based (EMBER-style) boolean kernel: what would it bring? (2026-11-19)

Design study only, no library code (owner 2026-11-19). The owner's question: if a plane-based boolean kernel
brings a gain without lowering quality in any way, months of work are acceptable. Does it bring one, and under which
quality constraints?

The quality bar. All of it must hold; otherwise the answer is no-go, or an opt-in path at most:
- exact on the input doubles, with no input quantization unless the input is already on the grid;
- the same clean, rounded double output (`NotCleanReason == None` on every T6 result today);
- the same robustness on the fuzz and CsCheck corpora;
- deterministic, and bit-identical across thread counts;
- API compatible.

Probe code and raw numbers are on the data branch `runs/plane-kernel-study` (commit 7277c9c4, no meshes).

## Verdict

**Decision: no-go (owner 2026-11-19)**, both for a replacement kernel and for an opt-in on-grid hybrid.
Nothing is to be built. The octree-BSP / EMBER architecture stays shelved (owner 2026-11-17), now with numbers.

| question | answer, with numbers |
|---|---|
| 1. What a plane-based kernel could replace | 33-35 % of our time at 1 / 4 / 12 threads: the arrangement, merge, triangulation, classification and simplify. The operand self-join is another 19-32 %. The quality bar keeps it: it is how we handle self-intersecting inputs. 33-46 % cannot be replaced: rounding 20-21 %, assembly, setup, input and untimed work. |
| 2. Upper bound (the replaceable part takes 0 s) | ours / CGAL EPECK, summed, at 12 threads: 1.43x → **0.93x** (−35 %). At 1 thread: 3.95x → 2.58x. Removing the self-join as well, which breaks the bar: 0.66x. |
| 2. Realistic | the replaceable part 2-4x faster (an estimate: no paper reports per-phase numbers that compare) gives **−17 to −26 %**, 1.18-1.06x CGAL at 12 threads. Against Geogram we are already at 0.31-0.40 (summed). |
| 3. Exact on doubles, without quantization | possible: it is today's arithmetic. Plane-based predicates on the arrangement's grid (B = 54-58 bits on T6) need 9B+19 ≈ 510-540 bits, against the 256 bits of the quantized kernels. With filters the cost equals today's. Without filters (the papers' choice) it is about as slow as CGAL's lazy kernel. The number type is not where the gain is: multiplications are under 1 % of our time, and exact arithmetic is 5-6 % self time. |
| 3. Hybrid for on-grid inputs | 0 of the 294 T6 booleans are on a short grid (the set normalizes and rotates its operands). Of the 190 raw T6 models, 10 (5 %) fit a 26-bit grid, the largest for which the side predicate fits 256 bits. That count is per model; a pair needs both operands on one grid. Every on-grid result still needs the full rounding, because its vertices are rationals. Not worth a second exact kernel. |
| 4. Iterated CSG | the gain of "no re-rounding between steps" is already captured by the merged `ExactMesh` expression (#1107, one exact n-ary pass), with bit lengths that do not grow with the number of steps: at most 10B+20 / 9B+18 for today's one-pass constructions, and 7B+14 / 6B+12 under plane-based provenance (conditions C1-C4 of the 4.0 analysis, closed inputs). EMBER lever 2 is bounded at at most −15 % at 12 threads and needs no new kernel. A plane-based kernel adds only materialized exact intermediates, and no consumer needs them. |
| 5. Effort and risk | 4-6 months, plus a second exact kernel to maintain. Today's kernel must stay for open and self-intersecting inputs. M1 (a probe, about 2 weeks) would be killed if the replaceable phases are not at least 3x faster. Its best realistic outcome (−17 to −26 %) is at or around the 25 % bar of the coordinator's brief of 2026-11-19. |

What would reopen it: a consumer with long iterated CSG on inputs that are already on a short integer grid (milling
or sweep simulation, voxel-based CAD), where Nehring-Wirxel et al. 2021 show their strength and the double output is
needed only at the end. Or a measured M1 factor of 4x or more (section 5).

## 1. Where the time goes today

Data: the owner's machine (i7-8700, 6 cores / 12 threads, net10.0), T6 set (98 Thingi10K pairs, 294 booleans,
SafeRounding on), #962 head bf34eff7, 3 rounds. Source: `runs/exact-numbers:runs/exact-numbers/results/ab-pr962-vs-master-2026-11-16/2-pr962-bf34eff7/`.

How the numbers are taken:
- Each boolean's time is the median over the three rounds. The table gives sums over the 294 booleans
  (`probe/phases.py`).
- Stratum: normal single booleans of raw, clean inputs, warm process.
- Shares are wall time per phase, including the allocation and GC cost incurred inside the phase.
- The `touch_only` filter (#1002) and #1064 came after this head. They shrink `intersections.pairs` (about −13 to −21 %
  of that phase). A new 12-thread run would show a smaller self-join share and larger shares for every other phase,
  K1 included.

| phase | 1 thr ms | 4 thr | 12 thr | share at 12 | plane-based kernel |
|---|---|---|---|---|---|
| input, setup.points / triangles | 1244 | 880 | 853 | 5.6 % | not replaced: the grid conversion stays; planes replace the triangle setup, at a similar cost |
| intersections.trees, cull | 2352 | 1119 | 790 | 5.2 % | replaced (an octree instead of the box trees) |
| intersections.pairs, A × B part (12.4 %) | 1912 | 923 | 413 | 2.7 % | replaced (local cells) |
| intersections.pairs, operand self-join (87.6 %) | 13 507 | 6517 | 2920 | 19.2 % | **kept by the bar**: robustness on self-intersecting inputs needs it, or an equivalent test |
| intersections.planes, intersect | 1393 | 686 | 413 | 2.7 % | replaced |
| merge (points, crossings, predict) | 3124 | 1469 | 1043 | 6.9 % | replaced in part: double output needs vertex identity, and a polygon soup with T-junctions has none |
| triangulation | 3016 | 1376 | 820 | 5.4 % | replaced in part: the convex cells still need T-junction resolution and triangles |
| classification | 1606 | 944 | 741 | 4.9 % | replaced (local winding propagation) |
| simplify (`ExactPlanarSimplify`) | 1150 | 1063 | 1059 | 7.0 % | replaced (coplanarity by plane id) |
| assembly without simplify | 1921 | 1351 | 1215 | 8.0 % | not replaced: a `DMesh3` with shared vertices is the output |
| rounding (SafeRounding) | 8837 | 4570 | 3072 | 20.2 % | **not replaced**: plane-based vertices are rationals too |
| untimed (outside the phase timers: `count_contacts`, set-up) | 1816 | 1822 | 1834 | 12.1 % | not replaced |
| **total** | **41 878** | **22 720** | **15 173** | | |

Summary by class:

| class | 1 thr | 4 thr | 12 thr |
|---|---|---|---|
| K1: replaceable by a plane-based kernel (generous: every "in part" counted as fully replaced) | 34.8 % | 33.4 % | 34.8 % |
| K2: the operand self-join (kept under the bar) | 32.3 % | 28.7 % | 19.2 % |
| N: not replaceable | 33.0 % | 38.0 % | 46.0 % |

Assumption: the self-join share of the pairs phase is 10.6 of 12.1 s, from probe A (#967, BOOLEAN_THREAD_SCALING.md 7.1).
It was measured at 1 thread on the 4-vCPU container and is applied to every thread count here.

**ABC chains.** The phase timers of the abc-chained class are not split this way. What the 4.0 stage 0 analysis
(EXACT_BOOLEAN_40_DESIGN.md section 4) measured: rounding is 3-44 % of a step on ABC, 11-30 % on generic procedural chains
and 23-81 % on contact chains. The rounding share of a chain step can therefore be lower than on T6 (3 % on some ABC
steps) or much higher, so T6's split does not carry over as a bound. On step 3, where
we are 1.1-2.2x slower than Geogram and CGAL, the cause is not diagnosed. Nothing points to the arithmetic.

## 2. Upper bound, then a realistic estimate

Starting point (T6 closing report, exact-boolean.md), #962 head: our time at 1 / 4 / 12 threads divided by CGAL EPECK's
single-threaded time. CGAL's time on the owner's machine is an estimate: its runner time from one run (43819984226), scaled
by one global machine factor. With per-operation factors the summed 12-thread ratio would be about 1.47x instead of
1.43x.

| | 1 thr | 4 thr | 12 thr |
|---|---|---|---|
| today, summed / median | 3.95x / 4.93x | 2.14x / 2.84x | 1.43x / 2.05x |
| upper bound under the bar (K1 = 0) | 2.58x / 3.21x | 1.43x / 1.89x | **0.93x / 1.34x** |
| upper bound breaking the bar (K1 = K2 = 0) | 1.30x / 1.63x | 0.81x / 1.08x | 0.66x / 0.94x |
| realistic, K1 2x faster | 3.26x | 1.78x | 1.18x |
| realistic, K1 4x faster | 2.92x | 1.60x | 1.06x |

(The median columns apply the summed class shares to the median ratio, which is an approximation.)

So the upper bound is about −35 % at every thread count, above the 25 % stop line of the coordinator's brief of
2026-11-19. The realistic gain, −17 to −26 %, is at or around that bar.

**Why 2-4x and not the factors in the papers.** The papers' factors compare against unfiltered or lazy exact kernels, or
against pipelines with different end points:
- **Nehring-Wirxel, Trettner, Kobbelt 2021** (CAD 135, arXiv 2103.02486):
  - Table 1 gives cycles per primitive at 256 bits against CGAL's `Lazy_exact_nt<Quotient<MP_Float>>`: about 10x for
    `intersect_3_planes` (402 against 4200 cycles) and 5x for `classify_vertex` (142 against 710).
  - All their tests are single-threaded (i7-7700K).
  - They use no floating-point filter: they found filters too slow or too coarse.
  - They need closed, watertight inputs.
  - Our predicates are filtered, and exact evaluation is rare. Their factor is against an exact kernel we do not use.
- **EMBER** (Trettner, Nehring-Wirxel, Kobbelt, ACM TOG 41(4), 2022). The paper was not reachable from this session
  (the host does not resolve). Our earlier notes (EXACT_BOOLEAN_30_PRESTUDY.md, "Published numbers") record:
  - 1000 Thingi10K pairs of 1k-100k faces, 8 cores, geomean 3.6 ms against 711 ms for CGAL 5.4 (the CGAL variant, Nef or
    corefinement, is not recorded in our notes);
  - about 5.5x speedup on 8 cores;
  - input quantized to 26 bits;
  - output a plane-based polygon soup with T-junctions, not a double mesh.
- **Cross-check (an inference, not a measurement).** The pre-study mapped our size-class median through Manifold onto
  trueform's numbers (1000 Thingi10K pairs of 100k-1M triangles per operand, M4 Max). There ours was about 4.8x EMBER,
  on code before #962. K1 + K2 are about 54 % of our time at 12 threads on T6. If EMBER spent all of its time on the work
  of K1 + K2, it would be about 2.6x faster on that part. That fits the 2-4x assumed above. It does not support 10x.
  Different size classes, code versions and machines make this an order-of-magnitude check only.

Against Geogram (public 1.10.2) the question does not arise: ours / Geogram is 0.31 (1 thread) and 0.40 (4 cores)
summed on T6 (GEOGRAM_BOOLEAN_COMPARISON_2026-11.md).

## 3. Exactness on input doubles

**Bit widths.** The arrangement puts all inputs on one integer grid, |x| < 2^B (coordinate = integer · 2^ScaleExp). On
T6, B = 54-58; on ABC, 37-59 (EXACT_BOOLEAN_40_DESIGN.md section 2). The bounds, from that design's section 2.2 plus the
predicate:

| quantity | bits | B = 26 (quantized kernels) | B = 58 (T6) |
|---|---|---|---|
| face plane normal n = (q − p) × (r − p) | 2B+3 | 55 | 119 |
| face plane offset d = n · p | 3B+5 | 83 | 179 |
| vertex from three planes, numerators X | 7B+14 | 196 | 420 |
| vertex from three planes, denominator W | 6B+12 | 168 | 360 |
| side of such a vertex against a fourth plane, n·X + d·W | 9B+19 | 253 | 541 |

At B = 26 the side predicate fits the 256 bits of Nehring-Wirxel et al.: their "256 bit with 27 bit positions and 55 bit
normals" (section 4.6) matches the n column.

**Without quantization.**
- *Possible.* The arrangement already stores three-plane vertices (`PointKind.Planes`), plus line-plane and segment
  points, over the original doubles. Its predicates are these polynomials, behind filters (Attene 2020).
- *A fixed-width kernel at B = 58.* It needs about 9 limbs per value instead of 4. Products scale with limbs squared, so
  about 5x the cycles of the 256-bit kernel: about 2000 cycles per `intersect_3_planes` and about 700 per
  `classify_vertex` (estimated from their Table 1 by limb count). That is CGAL-lazy speed. Without filters, a
  plane-based kernel on doubles loses the quantized kernels' advantage.
- *With filters.* It is today's arithmetic, so the cost should equal today's (an inference: the filter success rate of
  a plane-based kernel's predicates was not measured).
- *Wider grids.* Inputs with a large exponent span need more: the raw T6 models need up to B = 137 (below). So a
  `BigInteger` fallback stays, as today.
- *Where the gain would have to come from.* Not from the number type. The fixed-width study (FIXED_WIDTH_EXACT_POINTS.md
  section 1) puts the multiplications under 1 % of the time, and exact arithmetic is 5-6 % self time on normal booleans
  (EXTREME_RUNTIME_BOOLEANS.md). Any gain is in the structure: local cells instead of global pair tests, per-triangle
  CDTs and global classification.

**Hybrid: a fast plane path when all inputs lie on a short power-of-two grid.**
- **How often real inputs are on such a grid.** Probe `probe/gridbits.py`, the 190 raw Thingi10K models of T6, as
  downloaded, before the set's normalization. B is the bits of the smallest common power-of-two grid of a model's
  coordinates:

  | file kind | models | B min / median / p90 / max | B ≤ 26 | B ≤ 27 | B ≤ 31 | B ≤ 53 |
  |---|---|---|---|---|---|---|
  | binary STL (float32) | 119 | 23 / 34 / 81 / 137 | 10 | 13 | 32 | 90 |
  | ASCII STL (decimal) | 71 | 57 / 71 / 110 / 116 | 0 | 0 | 0 | 0 |

  - The counts are per model. A pair needs both operands, as placed, on one common grid, so the share of pairs is at
    most that of models. B ≤ 26 is the limit for 256 bits under the bounds above (9B+19 ≤ 256). Nehring-Wirxel et al.
    state 27-bit positions for 256 bits with their own, tighter bounds; the B ≤ 27 column shows that case.
  - float32 alone does not make a short grid: coordinates near zero carry fine bits, so B = 24 plus the exponent span.
  - Decimal values are almost never dyadic.
  - The T6 booleans themselves are 0 of 294: the set scales the operands to the unit box and rotates B in double.
  - CAD exports behave like these STLs: tessellated in double, written as float32 or decimal. Inputs on a grid by
    construction are integer or voxel models, or the output of `SolidBooleanPipeline.InputGrid`. That snap guarantees a
    power-of-two grid, but its multiples go up to 2^53, so a short B needs a coarse grid relative to the model size.
    That is quantization, which only the caller may choose.
- **What the hybrid would gain.**
  - On-grid inputs have rational result vertices, so the rounding (20 %) stays in full.
  - The fast path covers K1 (35 %) on at most 5 % of raw models (10 of 190 with B ≤ 26; fewer as pairs) and none of T6.
  - The self-join still runs, unless the caller certifies the inputs.
  - The expected gain over T6 is 0. Over raw models it is at most about 5 % × (17-26 %), roughly 1 %.
  - The cost is a second exact kernel, with its own review, bound tables and mutation shards.
- **Determinism and API.** An octree with a fixed cell order and per-cell work can be deterministic and bit-identical
  across thread counts. The kernel would be internal, so the API is not affected. Neither is the obstacle.

## 4. Iterated CSG

This is where plane-based kernels shine: their results stay plane-based and exact, so nothing is re-rounded between
steps.
- **Already built.** The `ExactMesh` lazy expression (Exact Boolean 4.0 stage 1, #1107, merged) evaluates a known chain
  as one exact n-ary boolean with one rounding. Its points are single-level constructions over the original doubles, so they stay within
  10B+20 / 9B+18 bits for any number of steps. The tighter 7B+14 / 6B+12 is the bound under plane-based provenance, for
  closed inputs under the conditions C1-C4 (EXACT_BOOLEAN_40_DESIGN.md sections 2.2-2.3).
- **The measured gain of no re-rounding** (4.0 stage 0, section 4):
  - the k − 1 saved roundings, 11-30 % of a step on generic chains, 3-44 % on ABC (upper bound);
  - of the 6 unclean ABC results, 4 become clean non-empty results and 2 become clean empty ones (iud p0008, p0029,
    where the final rounding removes the whole thin exact solid);
  - But on long contact chains, 21 of 108 results get worse: exact intermediates carry every sub-ulp slab to the final
    rounding.
- **What a plane-based kernel would add:**
  - materialized exact intermediates for a consumer that does not know its next operation (no such consumer now);
  - skipping the self-join of operands it produced itself. That is EMBER lever 2, bounded at at most −15 % at 12 threads
    (extrapolated), and it is possible on today's kernel.
  - Neither needs a new kernel.

## 5. Effort, risk, and the milestone if the owner wants the factor measured

Phases if built:

| phase | content | effort |
|---|---|---|
| M1 (falsification probe, benchmark code on the data branch only) | octree cells over the A × B candidates; per cell, plane-based clipping of the triangles and local winding classification. T6 is quantized to 26 bits *for the probe only*. Timed against today's K1 phases on the same cases, at 1 and 4 threads | about 2 weeks |
| M2 | plane predicates with filters at 9B+19 bits (PredicateCompiler, bound tables, exact review) | 3-4 weeks |
| M3 | T-junction resolution, vertex identity, triangles for `assemble` and the rounding; coplanar merge by plane id | 4-6 weeks |
| M4 | the open-input and self-intersecting-input paths fall back to today's kernel; differential and golden master against today's kernel on T6, ABC and the fuzz / CsCheck corpora | 4-6 weeks |
| M5 | mutation shards (2-3 new, a full run of 4-5 h each), exact-code review of every bound, determinism tests | 2-3 weeks |

In all, 4-6 months. After that, two exact kernels are maintained, because the open-input and self-intersection
handling stays on today's.

**M1 kill criterion.** The K1 phases must be at least 3x faster on the quantized probe; that is about −23 % of the
whole at 12 threads. At 4x (−26 %), M2 is reasonable to discuss. The probe measures the best case: quantized inputs and
no filters needed. With unquantized doubles, M2 can only lose ground.

**What could kill it beyond M1:**
- The vertex identity and T-junction work of M3 moves the cost back into merge and triangulation.
- Coplanar faces of different triangles have different integer planes unless normalized: a gcd of 119 / 179-bit
  coefficients per plane, or the exact comparison we already do.
- Open inputs have no plane-based story: no exact method in the literature handles open operands (EXACT_BOOLEAN_30_PRESTUDY.md).
  So the fallback is permanent.
- A patent check for EMBER was noted as open in the pre-study.

**Why not M1 now.** Its best realistic outcome (−17 to −26 % at 12 threads, under 2 % expected on raw inputs through the
hybrid) is at or around the 25 % bar of the coordinator's brief of 2026-11-19. Its upper bound is reached only by replacing the arrangement, which the
owner ruled out as a rewrite on 2026-11-07. The speed work stopped on 2026-11-16 at 1.43x CGAL. That gap is in the
range of the rounding, which no plane-based kernel removes.

## Prior art and sources

Papers only, no code read. Nehring-Wirxel, Trettner, Kobbelt, *Fast exact booleans for iterated CSG using
octree-embedded BSPs*, CAD 135, 2021, arXiv 2103.02486: read in HTML, sections 1-5. The PDF exceeded the fetch size, and
Figure 14 and sections 6-7 were not read. Trettner, Nehring-Wirxel, Kobbelt, *EMBER*, ACM TOG 41(4), 2022: not
reachable, numbers from our earlier notes. Bernstein and Fussell 2009 and Campen and Kobbelt 2010 (plane-based,
fixed precision, quantized input), Attene 2020 (indirect predicates, our basis): as cited in EXACT_BOOLEAN_40_DESIGN.md
section 7. No new literature search was run for this note. It relies on the check of the 4.0 stage 0 analysis
(2026-11-17), which found no work bounding plane-based booleans over unquantized double inputs, and on the two papers
above.
