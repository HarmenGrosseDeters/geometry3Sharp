# Reducer / Remesher 2.0: baseline and target proposal

Status: proposal for the owner (2026-11-09), tier M (measurement and design, no library change). Decisions are marked
**[owner]**. Numbers: [benchmarks/ReducerRemesherBaseline](../../benchmarks/ReducerRemesherBaseline/README.md) (command in
its README; master 2c7ba9f, 4 cores, warm, net10.0; 4 procedural meshes + the 20 Thingi10K meshes of `RemeshBenchmark`;
raw rows not committed). Hand-over of the starting point: [PARTITIONED_REDUCTION_HANDOVER.md](PARTITIONED_REDUCTION_HANDOVER.md);
earlier literature review of the guarantees' cost: [RESEARCH_REDUCER_REMESHER.md](RESEARCH_REDUCER_REMESHER.md).

## 1. Prior art and building blocks (AGENT_WORKFLOW sections 2 and 3)

### 1.1 What the library has

| Block | State | Use for 2.0 |
|---|---|---|
| `Reducer` (QEM, `mesh/Reducer.cs`) | area-weighted quadrics, boundary and feature (`FeatureAngle`) planes, Trettner-Kobbelt regularization, `PreventFoldOver`, `PreventSelfIntersections` (exact, grid), `MaxDeviation` (certified, 12-32 x slower), `MinCollapseTriangleQuality` (absolute) | the base |
| `Remesher` / `RemesherPro` | Botsch-Kobbelt incremental remeshing, `MeshConstraints`, `PreventSelfIntersections`, `MinTriangleQuality` (opt-in), `RejectInvertingPasses`, `TargetEdgeLengthF`, parallel smoothing and projection | the base |
| `PartitionedReduction` (#432, internal) | 8 slabs, confinement + frozen obstacles, deterministic for any thread count, serial pass at the end; used by `SimplifyWithin` only | lever for `si` (section 3) |
| `CollapseQualityAgainstWorst` (#405, internal) | quality guard relative to the worst triangle around the edge | lever for general meshes (probe in 2.2) |
| `MeshConstraints` / `MeshConstraintUtil` | per edge / vertex constraints, set IDs | maps the caller's constraints into slabs |
| `MeshFidelityMetrics`, `MeshHausdorffDistance`, `TriangleQualityStatistics`, `MeshTopologySummary`, `MeshRobustRepair.NotCleanReasons` | the measures of the baseline | acceptance |
| `DefectFreeReduce` | test-side helper only (`DefectFreeReduceTests`), not a library class | none |
| Batch queries, memory layout (spatial-structure profile) | for `PreventSelfIntersections` candidate search | shared hotspots, see roadmap |

Already measured and closed (RESEARCH_REDUCER_REMESHER.md, hotspots.md): pair-result caching in the Remesher (at most 45 ms of 13.2 s), union-box
query (4-6 %), coplanar shortcut and clearance filter; permission grid (D) not built.

### 1.2 Literature and other implementations (approach only; licences as the rules require)

| Topic | Source | Licence / status |
|---|---|---|
| QEM and variants | Garland, Heckbert, *Surface simplification using quadric error metrics*, SIGGRAPH 1997; Lindstrom, Turk, *Fast and memory efficient polygonal simplification*, IEEE Vis 1998 (memoryless: no stored quadrics); Hoppe, *New quadric metric for simplifying meshes with appearance attributes*, IEEE Vis 1999; Trettner, Kobbelt, *Fast and Robust QEF Minimization using Probabilistic Quadrics*, EG 2020 (in the library) | papers |
| Parallel / partitioned simplification | Ozaki, Kyota, Kanai, partition-based out-of-core QEM, Eurographics PGV 2015 (found by search, full text not read); Mousa, Hussein, GPU edge contraction on independent regions; Lindstrom, *Out-of-core simplification of large polygonal models*, SIGGRAPH 2000; Franc, Skala, *Parallel triangular mesh decimation without sorting*, SCCG 2001 (last two from memory, verify before use). None found that is deterministic independent of the core count: #432's fixed slab count is that property | papers |
| Isotropic remeshing | Botsch, Kobbelt 2004 (in the library); Alliez et al., *Isotropic surface remeshing*, SMI 2003; Valette, Chassery, ACVD, Eurographics 2004; Dunyach et al. 2013 (sizing); Hu et al. 2017 and Wang et al. 2019 (min-angle improvement; both already in SOURCES.md as stage 7b leads) | papers |
| Feature preservation | feature planes in the quadric (library); Salinas, Lafarge, Alliez, *Structure-aware mesh decimation*, CGF 2015 (from memory, verify) | paper |
| Implementations | meshoptimizer (Kapoulkine; MIT, checked by search): error-bounded simplification, locked vertices, attribute-aware, `sloppy` mode; Fast-Quadric-Mesh-Simplification (Forstmann; MIT, from memory); geogram (Lévy; BSD-3-Clause, checked by search): vertex-clustering decimation, CVT-based `remesh_smooth`; OpenMesh decimater (BSD-3, from memory); Instant Meshes (BSD-3, from memory) | portable after a licence check of the file in question |
| Not usable | CGAL (GPL), TetWild (MPL), libigl (MPL-2.0), Blender and MeshLab (GPL); pmp-library: licence not checked | never read or ported |

How others solve the main problems: quality of QEM results on dirty inputs is handled by a compactness / normal-deviation test
relative to the neighbourhood (meshoptimizer, OpenMesh modules) rather than an absolute floor; self-intersection-free decimation
is a per-collapse test as ours (Gumhold et al. 2003), parallel versions partition and freeze the border (out-of-core QEM); isotropic
remeshing gets its angle bounds from the operator order and local checks (Hu 2017, Wang 2019).

## 2. Baseline (library defaults)

Full tables and definitions: the benchmark's README. In short (thingi = the 20 real meshes, procedural = the 4 clean ones):

| | Reducer 10 % / 1 % | Remesher x1 / x2 mean edge |
|---|---|---|
| time per input triangle, default | 3.2 µs (0.3 s for 100k) | 23 / 11 µs |
| time with `PreventSelfIntersections` | 21 µs (6.7 x) | 282 / 121 µs (12 x) |
| p01 min angle, thingi (input 0.1°) | 0.6° / 2.5° | 1.3° / 0° |
| p01, procedural (input 27°) | 8.2° / 8.4° | 40° / 42° |
| meshes with triangles < 0.5° or > 170° (of 20) | 19 / 15 | 18 / 19 |
| hits the target (within 1.1 x) | 1 % misses it on 8 of 20 (max 16.8 x; 4 of them are below the genus minimum) | - |
| Hausdorff in to out, thingi median (max) | 0.9 % (5.7 %) / 7 % (30 %) of the diagonal | 2.2 % / 4.0 % |
| inputs without pairs that get self-intersections (of 19; lower bound of the new ones) | 4 / 9 (up to 449 pairs) | 14 / 14 (up to 10737) |
| topology changed | 0 | 0 |
| degenerate triangles ("not clean", of 20) | 0 | 17 / 18 |
| repeat = serial = same bits | all | all (parallel on = off) |
| power-of-two scaling bit-identical | all | not in 4 cases |

Reading: the machinery is fast and deterministic and keeps topology; what is weak on real meshes is **triangle quality** (both),
**self-intersections by default** (both), **target met** (Reducer at 1 %, partly impossible: 4 targets lie below the minimum triangle count of the mesh's genus), and for the Remesher degenerate output and a scale
dependence. The checks (`si`) cost 84-92 % of the time.

Probes (no library change; knobs in the benchmark):
- `Reducer.MinCollapseTriangleQuality = 0.3` (plain): clean meshes p01 8.4° -> 12.8°, no slivers, same time and fidelity; thingi: target missed
  by 6 x (10 %) to 59 x (1 %): the inputs contain triangles below 0.3, so every collapse next to them is refused. The absolute floor is wrong
  for general meshes; the "against the worst" form (#405's `CollapseQualityAgainstWorst`) is the candidate.
- `Remesher.MinTriangleQuality = 0.2`: thingi x1 p01 1.3° -> 5.7°, slivers (both counts added) 6213 -> 1263, degenerate triangles 17 -> 0 of 20, cases with more pairs than the input 19 -> 13, time +40 %.
- Remesher phase profile: serial edge operations 53-74 % of the time (flips the largest part), smoothing + projection 26-47 % (parallel already).

## 3. Proposed targets (in the style of Alpha Wrap 2.0)

Measured on the benchmark set above (procedural + the 20 thingi meshes; the 800-mesh sample only for a default flip), 4 cores, warm.
Every target is a gate: not worse than the baseline on guarantees and fidelity unless stated.

| # | Target | Baseline | Lever | Upper bound of the lever |
|---|---|---|---|---|
| T1 | **Reducer quality**: result has no more triangles < 0.5° / > 170° than the input; procedural p01 >= 10°; the target count is met within 1.1 x on every thingi mesh whose target is feasible (a closed genus-g mesh needs at least 2V - 4 + 4g triangles with V >= (7 + sqrt(1 + 48g)) / 2: 16 of 20 at 1 %); the infeasible ones report the minimum | at 10 %: 1644 triangles < 0.5° and 1796 > 170° in 19 of 20 meshes; the 1 % target missed on 8 (4 infeasible) | `CollapseQualityAgainstWorst` guard as an option, then a default after the 800-mesh run | procedural 12.8° with the plain guard (probe); the count part is bounded by the genus minimum above; thingi: no probe yet for the "against the worst" form - **first work package**, stop rule if the target is missed on > 2 of the 16 feasible meshes |
| T2 | **Remesher quality and cleanliness**: 0 degenerate triangles on 20 of 20 (NotClean empty), thingi x1 p01 >= 5° (stretch 10°), slivers (< 0.5° plus > 170°, both counts added) at most the probe's 1263 in total (baseline 6213) | 17 of 20 not clean, p01 1.3° | `MinTriangleQuality` 0.2 as default / preset; stretch: Hu / Wang operators | 0.2 reaches 5.7° and 0 degenerate (probe); 10° has no probe: the operators of Wang 2019 need a measurement on 10 meshes before they are a lever |
| T3 | **No new self-intersections by default at <= 4 x the plain time (Reducer), <= 8 x (Remesher)** (today `si` is 6.7 x / 12 x and off) | 9 of 19 inputs without pairs (Reducer, 1 %), 14 of 19 (Remesher) get pairs; `si` ran on 12 of them (none above 30000 triangles) | slab-partitioned `si` for the Reducer (#432 as an option); Remesher: partition after T4 | Reducer: ideal 4-core split of the check gives 3.2 + 18/4 = 7.7 µs = 2.4 x; #432 realised 0.54 of the time on wraps, which gives 3.2 + 18 x 0.54 = 12.9 µs = 4 x. Remesher: `si` is 12 x the plain time and 92 % of it, inside the edge operations (the phase profile in section 2 is of the default variant). Ideal 4-core split of the check: 1 + 11/4 = 3.75 x; with #432's realised factor 0.54: 1 + 11 x 0.54 = 7 x, so 8 x is reachable only if the partition works as well as on wraps. Closed levers (cache, union box) give <= 6 % |
| T4 | **Remesher speed**: x1 default <= 15 µs per input triangle on 4 cores (1.5 x faster) | 23 µs | slab partition of the edge operations with limited smoothing | Amdahl on the serial edge-operation share of the default variant (53-74 %): ideal 4 cores 1.66-2.25 x; with the realised factor 0.54 on that share 1/(0.47 + 0.53 x 0.54) = 1.3 x to 1/(0.26 + 0.74 x 0.54) = 1.5 x; **[owner]** whether 1.5 x is worth the complexity (100k triangles: 2.3 s -> 1.5 s) |
| T5 | **Invariance**: power-of-two scaling gives the same bits in 100 % of the cases for both (Remesher fails 4 cases today); the triangle count under the other similarities stays within 5 % (Reducer shows 25 % in single cases, 31 % with `si`; Remesher 45 % with `si`) | see baseline | find the absolute constants (diagnosis first; the 4 failing Remesher cases are named in the JSON `inv_failed`: terrain x1 at 2^10, thingi 299291 at 2^-10 (also with `si`), thingi 93370 at 2^-10) | bound is the baseline itself: a bug class, not an optimisation; the 5 % bound is a hypothesis to measure on the corpus (float-noise path changes are legitimate) |
| T6 | **Fidelity non-regression**: with T1-T3 on, the per-mesh certified Hausdorff (both ways) and F-score at 0.1 % are not worse than baseline by more than 5 % at the median (the Hausdorff bounds must then be computed with `hd_tol=0.0001`: at the baseline's 0.001 their slack is as large as the values), topology unchanged on 20 of 20 | table above | a gate, not a lever | - |

Speed of the default Reducer is **not** a target: 3.2 µs per triangle (0.4 s for 100k) leaves at most 0.3 s to gain by 4-core
partition even if free (upper bound), below the noise of the callers' other work; the partition pays only for `si` (T3).

Determinism (repeat = serial, any thread count) is a guarantee of every lever, as in #432, not a target: it holds today and must keep holding
(tests per CLAUDE.md: invariance over `Similarity.All()`, reuse, determinism).

### Out of scope

Error-bounded reduction speed (`MaxDeviation`, 12-32 x; own work in RESEARCH_REDUCER_REMESHER.md bottleneck 1); fidelity improvement beyond a QEM
count target (no upper bound from the greedy method; the optimum for N triangles is unknown and ReduceToMaxQuadricError / MaxDeviation exist for error targets); attribute-aware
simplification (UV, normals, colours: the partitioned path would drop them today); GPU; curvature-adaptive sizing; quad meshing; CCD-based
guarantees; thin walls far above the wall thickness (roadmap bottleneck 5); comparisons with CGAL (black box only, owner's rule); the Alpha Wrap pipeline
(its own milestone, 3.0).

## 4. Options considered (decided in section 6)

1. **Defaults vs opt-in [owner].** Quality guards change results of existing calls (the library already differs from upstream there:
   `PreventFoldOver`). Options: (a) new opt-in options and a documented preset, defaults unchanged; (b) flip defaults for `Remesher.MinTriangleQuality`
   (0.2, already the `g3cli` default) and the Reducer guard after an 800-mesh run; (c) a new options class with presets. Leaning: (a) now,
   (b) for the Remesher only after the corpus run, as the roadmap's item (4). How others solve it: meshoptimizer exposes guards as options with conservative defaults;
   OpenMesh decimater modules are opt-in.
2. **Public partitioned reduction [owner].** Exposing #432 means fixing its contract differences (thread-safe `CancelF`, `AggregateException`,
   `PreserveBoundaryShape = false` inside regions, attributes dropped, triangle-count targets split over regions). Leaning: expose only
   as `Reducer` option for `PreventSelfIntersections` (T3), where it pays; default Reducer stays serial. How others solve it: partition + frozen border as out-of-core QEM
   (Lindstrom 2000, Ozaki 2015); none found deterministic across thread counts.
3. **Remesher speed (T4) [owner]**: worth building at 1.3-1.5 x? Leaning: only together with T3 for the Remesher, after T1, T2 and T5.
4. **Corpus [owner]**: the 20 + 4 set as the development set and the 800 sample of MeshRepairBenchmark for a default flip? Leaning: yes.
5. **Order**: T5 (a bug class, cheap) and T1 / T2 probes first; T3 / T4 only after their probes confirm the bound; stop rule: a lever whose probe
   shows less than half its bound is reported, not built.

## 5. Next steps if approved

Work packages (one PR each): (1) T5 diagnosis and fix of the Remesher's absolute constants; (2) T1: probe the "against the worst" guard on the 20 meshes,
then the option; (3) T2: preset / default `MinTriangleQuality`, probe of the Wang 2019 operators on 10 meshes; (4) T3 Reducer: `Partitioned` option for `si`;
(5) T3 / T4 Remesher after (1)-(4). Sources of ports are listed in SOURCES.md before code is written.

## 6. Owner decisions (2026-11-09, relayed by the coordinator)

1. **Defaults**: opt-in first, through existing properties or a "Robust" preset. A default flip only after an 800-mesh corpus run shows
   nothing worse and the speed target is met, as with Alpha Wrap 2.0 (section 4, item 1, options a / c).
2. **Scope of 2.0**: T1, T2, T3, T5, T6. T4 (Remesher speed) later, re-measured after T2 / T3. Partitioned reduction stays internal until T3 is measured
   (section 4, items 2 and 3).
3. **Order**, one PR per target, each with tests per CLAUDE.md, an upper-bound check before each lever, before / after numbers from this harness and an
   independent review: T5 (bit-identical 2^k, smallest), T2, T1, T3, T6. API compatible with 1.0.324; the test time limit holds.

## 7. T2 probe results (2026-11-09, upper-bound check before a lever)

20 Thingi10K meshes, Remesher with the `g3cli` settings, `MinTriangleQuality` (the existing opt-in) and the crease angle varied
(`remesh_q`, `remesh_crease` of the benchmark; fidelity on; raw rows not committed):

| setting | x1: p01 median, meshes < 5° / < 10° | slivers (< 0.5° plus > 170°), meshes not clean | x1: Hausdorff median (max), F 0.1 % median |
|---|---|---|---|
| default (quality 0) | 1.3°, - | 6213, 17 | 2.2 % (24 %), 0.76 |
| quality 0.2, creases 30° | 5.65°, 9 / 14 | 1263, 0 | 1.9 % (24 %), 0.77 |
| quality 0.3 / 0.4, creases 30° | 6.4° / 6.0°, 8 / 14 | 1375 / 1209, 0 / 1 | not measured |
| quality 0.2, no creases (boundary only) | 12.0°, 6 / 10 | 136, 0 | 7.6 % (38 %), 0.38 |

At x2 (coarsening) the median p01 is 1.8° with creases (3.5° without).

Reading:
- The committed T2 targets are met by the existing option at 0.2: 0 degenerate triangles on 20 of 20 (17 before), x1 p01 median 5.65° (>= 5°), slivers 6213 -> 1263 (the target),
  at +40 % time. Raising the threshold does not help (it saturates at about 6°).
- The remaining slivers sit at the crease constraints: without them the slivers fall to 136 and p01 to 12°, but the fidelity halves (F-score 0.77 -> 0.38, Hausdorff x4),
  so that is the upper bound of the quality in this setting, not a usable one. The stretch target of 10° with creases kept has no bound from the
  probes: it needs operators that act along the feature curves (Wang et al. 2019, section 1.2), whose bound has to be measured on 10 meshes first (not done).
- 5 of 20 meshes (104582, 125069, 135363, 196126, 105686) hold most of the remaining slivers; 135363 and 196126 are the thin-walled, ill-posed cases (Hausdorff 15-24 %).

Proposal for the T2 PR: no new code path; document 0.2 as the recommended robust setting in the class doc and OPERATIONS, add the knobs and these numbers
to the benchmark, and move on to T1. The stretch target and the x2 quality go to a follow-up after T1 / T3 if the owner wants them.

### Research note (owner 2026-11-09, not scheduled)

Feature-curve operators (Wang et al. 2019: insertion / removal of vertices along the feature curves) for the crease slivers with creases kept
(x1 p01 5.65° -> 10° stretch) and the coarsening quality (x2 p01 median 1.8°). Before anything is built: a bound from a probe on 10 meshes
(5 of 20 meshes hold most of the remaining slivers), and a check that the Hausdorff / F-score stay at the level of the creases-kept row above.

## 8. T1 probe results (2026-11-09, upper-bound check before a lever; stop rule applied)

20 Thingi10K meshes, `ReduceToTriangleCount` to 10 % and 1 %, no projection (`reduce_q`, `reduce_fold`, `reduce_worst` of the benchmark; the guard
by reflection; raw rows not committed). `missed` = result above 1.1 x the target; slivers = triangles < 0.5° plus > 170°.

| setting | 10 %: missed, p01 median, slivers | 1 %: missed, p01, slivers | 1 %: Hausdorff median (max), F 0.1 %, IoU | meshes with more self-int. pairs than the input (10 % / 1 %) |
|---|---|---|---|---|
| input | - | - | - | 22654 slivers |
| default | 1, 0.61°, 3440 | 8, 2.5°, 885 | 7.0 % (30 %), 0.17, 0.62 | 4 / 9 |
| `PreventFoldOver` off | 1, 0.85°, 2768 | 5, 3.0°, 582 | 6.0 % (19 %), 0.17, 0.60 | 5 / 11 |
| `MinCollapseTriangleQuality` 0.3 plus the internal "against the worst" form | 18, 0.26°, 12719 | 20, 0.26°, 12719 | 3.0 % (11 %), 0.75, 0.91 | 5 / 6 |
| `MinCollapseTriangleQuality` 0.3 plain, 0.1 plain / worst | 19-20 missed, p01 0.1-0.3° in all (earlier probe, 6 settings) | | | |

Reading:
- **The sliver part of T1 is met by the default**: on every one of the 20 meshes the result has fewer triangles < 0.5° or > 170° than the input
  (22654 in the input, 3440 at 10 %, 885 at 1 %). The target "no more than the input" does not need a lever.
- **The quality guard is not a lever for real meshes**, plain or "against the worst": it misses the count on 18-20 of 20 meshes (the inputs contain triangles below
  the threshold, so the collapses around them are refused and the reduction stops early with the input's slivers still in place), and p01 gets worse, not better
  (0.26° against 0.61°). On clean meshes it works (p01 8° -> 12.8°, earlier probe), which is why #405 uses it inside `SimplifyWithin`.
- **Why the count is missed at 1 %** (8 of 20): 4 targets are below the minimum triangle count of the mesh's genus (a closed genus-g mesh needs at least 2V - 4 + 4g triangles
  with V >= (7 + sqrt(1 + 48g)) / 2: 196126, 118679, 237642, 267659), 93370 sits at that minimum, and the rest are stopped by the
  reference-normal guard `PreventFoldOver`: with it off, 39460, 63709 and 104582 reach the target (5 meshes still miss). But switching it off is no gain in quality
  (p01 +0.5°, Hausdorff and F-score about the same, more meshes with extra self-intersections 11 against 9) and it is the guard against folded surfaces; the
  1 % results of these small, detailed meshes (30 to 400 triangles from 2.4k to 46k) are poor either way (IoU 0.6).
- **No safe lever found**, so by the stop rule T1 is reported and not built: nothing to change in the Reducer. What remains is documentation: `ReduceToTriangleCount` can stop above the
  target (guards, genus minimum), callers check `TriangleCount`; and the recommendation to use `FeatureAngle`, `MaxDeviation` or `ReduceToMaxQuadricError` when a quality criterion rather than a count is meant.

Proposal: T1 closed with this note (no code); T3 next (`PreventSelfIntersections` is the lever for the self-intersection columns above).

## 9. T3: `PartitionedReducer` (the public partitioned Reducer; owner decision 2026-11-09, option A)

New public sealed class `PartitionedReducer` (`mesh_ops/PartitionedReducer.cs`, purely additive): `PartitionedReduction` (#432) on 8 fixed slabs, with
`Reduce(mesh, maxQuadricError)`, `Configure(Action<Reducer>)`, `MaxThreads`, `CancelF`, `MinTriangles` (default 20000: below it one serial Reducer run), `UsedPartition`.
The contract (deterministic for any thread count, not the serial result, no attributes and no isolated vertices in the result, `PreserveBoundaryShape = false`, thread-safe `Configure` and `CancelF`,
`MaxDeviation` and external constraints in `Configure` refused, `AggregateException` for every `MaxThreads`, the two serial fallbacks) is in the class documentation and pinned by `PartitionedReducerTests`. No triangle-count target in v1. Opt-in; the plain reduction stays serial.

Measured (`ReducerRemesherBaseline partition=1 min_tris=0`, 24 inputs, `ReduceToMaxQuadricError` at 0.003 of the box diagonal, `AreaWeightedQuadrics = false`,
`PreserveBoundaryShape = false`, 4 cores, warm, one run each; the partition forced on). Time of the plain Reducer, of `PreventSelfIntersections` serial, and of the same on 8 slabs:

| mesh | triangles | plain s (tris) | si serial s (tris) | si on 8 slabs s (tris) | si / plain | slabs / plain | slabs speed-up over si serial | used partition | Hausdorff si serial / slabs (of diagonal) |
|---|---|---|---|---|---|---|---|---|---|
| bumpy_sphere | 95052 | 0.34 (4792) | 1.35 (4792) | 0.69 (4820) | 4.0 x | 2.0 x | 1.97 x | yes | 0.0012 / 0.0013 |
| torus | 67600 | 0.19 (4328) | 0.96 (4328) | 0.45 (4354) | 5.1 x | 2.4 x | 2.16 x | yes | 0.0012 / 0.0013 |
| terrain | 96800 | 0.35 (7121) | 1.45 (7121) | 0.46 (7109) | 4.1 x | 1.3 x | 3.14 x | yes | 0.0190 / 0.0190 |
| box | 57132 | 1.06 (12) | 57.87 (12) | 10.23 (14) | 54.7 x | 9.7 x | 5.65 x | yes | 0.0005 / 0.0004 |
| 1043471 | 20204 | 0.05 (1044) | 0.30 (1044) | 0.32 (1060) | 5.5 x | 5.9 x | 0.93 x | yes | 0.0020 / 0.0020 |
| 104582 | 39608 | 0.17 (3908) | 0.91 (3912) | 0.90 (3930) | 5.5 x | 5.5 x | 1.00 x | yes | 0.0025 / 0.0025 |
| 1053376 | 42612 | 0.15 (5592) | 0.78 (5756) | 0.33 (5724) | 5.2 x | 2.2 x | 2.37 x | yes | 0.0074 / 0.0067 |
| 78295 | 42492 | 0.15 (4018) | 1.10 (4018) | 0.67 (4080) | 7.4 x | 4.5 x | 1.65 x | yes | 0.0016 / 0.0021 |
| 93370 | 45694 | 0.12 (12078) | 1.20 (12080) | 0.53 (11560) | 9.8 x | 4.4 x | 2.25 x | yes | 0.0022 / 0.0022 |

Below 20000 triangles (15 meshes, 2.4k to 18k): the slabs take 0.8 to 1.5 x the serial `si` time (no gain; setup and the serial last pass dominate), at most 0.2 s, which is why
`MinTriangles` defaults to 20000.

Totals over the 24 meshes: plain 2.8 s, `si` serial 67.4 s (23.7 x), slabs 16.0 s (5.6 x plain, 4.2 x faster than `si` serial). Without the box (a closed box with large coplanar faces
makes the self-intersection check 55 x the plain time): plain 1.8 s, `si` 9.5 s (5.4 x), slabs 5.7 s (3.2 x, 1.7 x faster). Result sizes agree within 1 to 4 % (93370: 12080 against 11560;
the box: 12 against 14; 63709, whose input has 1298 self-intersecting pairs: 4506 against 4390). The certified one-sided Hausdorff distance to the input is equal or within 0.0008 of the
diagonal (78295: 0.0016 / 0.0021; 118679: 0.0015 / 0.0032; 1053376: 0.0074 / 0.0067). Not bit-identical to the serial run (as #432).

Reading against the target (T3, Reducer: `si` at <= 4 x the plain time): reached on the large smooth meshes (sphere 2.0 x, torus 2.4 x, terrain 1.3 x, against 4.0 to 5.1 x serial), not on
CAD-like inputs with large flat faces (box 9.7 x, against 55 x) and not below 20k triangles (5 to 24 x, but at most 0.2 s). An earlier scratch probe (area-weighted quadrics, 1.2 to 1.6 x) used a lighter reduction; these numbers are with the settings of the class documentation. The Remesher part of T3 is in the next subsection.

### Remesher part of T3: upper-bound probe (stop rule applied, not built)

The target was `PreventSelfIntersections` at <= 8 x the plain Remesher time (today 12.6 x at the median for x1, 12.3 x for x2; 20 Thingi10K meshes, `g3cli` settings, 16 of
them up to 30000 triangles with `si`). Probe: the phase profile of the `si` run (`profile=1`, 4 cores; 3 meshes) and the required speed-up per mesh from the baseline rows.
- Share of the serial edge operations (collapse, flip, split) in the `si` run: 73 % (78295, 15 s), 70 % (93370, 69 s), 44 % (299291, 3.9 s); smoothing and projection
  (with the move check) 27 / 30 / 56 %. With the default (no `si`) the operations are 66-88 %.
- Needed speed-up of the `si` run to reach 8 x the plain time: 1.58 x at the median (x1; 1.54 x for x2); 2 of 16 meshes are there already; one of the 16 (49 x) needs more than 4 x, more than 4 cores can give (the profiled 93370, 46k triangles, is at 64 x: 69 s against 1.07 s).
- Bound for slabs on 4 cores: partitioning only the operations (share 0.7) gives at most 1 / (0.3 + 0.7 / 4) = 2.1 x; the Reducer's slabs realised 2.0 to 3.1 x over serial `si` on 60-100k triangles
  and nothing below 20k, which is where 15 of the 16 meshes with `si` are. So the target is reachable, if at all, on the few large meshes, with a large build (slab limits for smoothing and
  projection, the iterative passes, constraints across slab borders, flips of border edges) and an unknown effect on the result.
- The absolute cost is small for most inputs: `si` takes 1.6 s at the median (x1) and 42 s at the maximum of the 16 meshes (and 69 s on the 46k-triangle 93370).

Result: no lever worth its cost on the evidence; T3 for the Remesher is closed with this note (owner decision 2026-11-09: T4 / Remesher speed is re-measured later anyway). What exists today:
`PreventSelfIntersections` is opt-in and removes the added self-intersections completely (0 of 16 meshes with more pairs than the input, against 14 of 19 without it), at 12 x the time.

## 10. T6: fidelity non-regression record (2026-11-09)

The same harness command as the baseline of #457 (24 inputs, `runs=2`, `fidelity=1`, `inv_max=100000`, 4 cores, warm), on master after #457, #466, #468, #469, #470, compared with the baseline rows:

- **Results unchanged**: 158 of 160 rows (every case, operation, target and variant, including `si`) have the same result hash, bit for bit. The 2 rows that changed are the Remesher cases of T5 (terrain at x1, box at x2) with the same triangle count, Hausdorff distance and F-score.
- **Medians over the Thingi10K meshes, before -> after, both operations and both targets**: p01, Hausdorff in to out and out to in, F-score at 0.1 % and 0.5 %, volume IoU: equal to the digits shown (eg Reducer 1 %: p01 2.50 degrees, Hausdorff 7.0 %, F 0.168, IoU 0.616; Remesher x1: p01 1.30 degrees, F 0.760).
- **Topology** changed: 0 of 160 -> 0. Rows with more self-intersecting pairs than the input: 51 -> 51. Rows not clean: 53 -> 53. Repeated runs and parallel = serial: all equal.
- **Scale invariance (2^k, bit for bit)**: 4 rows not identical -> 0.
- Time per input triangle is 17 to 25 % lower than in the baseline run; the code of these paths did not change (T5 changes a padding constant), so this is the state of the machine between the two runs, not a gain.

So the defaults of the Reducer and the Remesher did not change result or fidelity; the opt-in additions are the measured ones below.

**Documented robust settings** (`Remesher.MinTriangleQuality = 0.2`, `g3cli remesh`'s value; same harness, `remesh_q=0.2`, against the defaults on the same master):

| inputs, target | p01 median | slivers (< 0.5 or > 170 degrees) | not clean | Hausdorff in to out, median (max) | F 0.1 % / 0.5 % | volume IoU | meshes with more pairs than the input | time per input triangle |
|---|---|---|---|---|---|---|---|---|
| Thingi10K x1 | 1.30 -> 5.65 degrees | 6213 -> 1263 | 17 -> 0 of 20 | 2.2 % (24 %) -> 1.9 % (24 %) | 0.760 -> 0.771 / 0.978 -> 0.973 | 0.935 -> 0.939 | 19 -> 13 | +46 % (17.4 -> 25.4 µs) |
| Thingi10K x2 | 0 -> 1.81 degrees | 9005 -> 1359 | 18 -> 0 of 20 | 4.0 % (32 %) -> 3.4 % (19 %) | 0.613 -> 0.619 / 0.780 -> 0.811 | 0.818 -> 0.820 | 19 -> 15 | +39 % (8.7 -> 12.1 µs) |
| procedural x1 / x2 | 40.1 -> 38.3 / 42.5 -> 42.4 degrees | 0 -> 0 | 1 -> 1 (the open terrain) | 0.1 % -> 0.1 % | 1.0 -> 1.0 | 0.9998 -> 0.9998 | 0 -> 0 | +18 % / +22 % |

No fidelity measure is worse at the median, topology is kept, and the repeat / serial checks hold. The `PreventSelfIntersections` variants of both operations (`si`) are in the baseline rows above; `PartitionedReducer` is in section 9.

## 11. Close-out of Reducer / Remesher 2.0 (2026-11-09)

Delivered (PRs #457 baseline and proposal, #466 T5, #468 T2, #469 T1, #470 T3; no default changed):
- A fixed baseline harness (`benchmarks/ReducerRemesherBaseline`, tested logic in `Harness.cs`) with quality, fidelity, topology, determinism and scale-invariance columns, the probe knobs, and the baseline numbers.
- **T5**: the Remesher (and `MeshICP`, the winding number, `RemeshSizing`) are bit-identical under 2^k scaling; the cause was the fixed box padding of `DMeshAABBTree3`.
- **T2**: `Remesher.MinTriangleQuality = 0.2` is the documented robust setting (0 degenerate triangles on 20 of 20, p01 1.3 -> 5.65 degrees, slivers -80 %, +40 % time).
- **T3**: the public, opt-in `PartitionedReducer` (`PreventSelfIntersections` at 1.3 to 2.4 x the plain time instead of 4 to 5 x on large smooth meshes).
- **T1, T3 (Remesher)**: probed, no safe lever, stop rule applied (sections 8 and 9).
- **T6**: this record.

Measured limits (what the numbers do not give):
- `PartitionedReducer` helps from about 20k triangles; closed meshes with large flat faces (box) stay at 9.7 x (from 55 x). Below 20k there is no gain.
- The quality guard of the Reducer is no lever on inputs that already hold bad triangles; the 1 % target is missed where it lies below the minimum triangle count of the genus or the fold-over guard stops the reduction.
- The Remesher's remaining slivers sit at the crease constraints (without creases p01 12 degrees, but the fidelity halves); `PreventSelfIntersections` for the Remesher stays at 12 x the time (closed in section 9).

Open (not scheduled): **T4** (Remesher speed, 1.3 to 1.5 x at best on the serial edge operations; re-measure when wanted); **feature-curve operators** (Wang et al. 2019) for the crease slivers and the x2 quality (research note in section 7); a flip of any default needs a proposal and the 800-mesh corpus run (owner 2026-11-09), none is on the table.


## 12. T4: Remesher speed, 2.x levers (owner 2026-11-09)

Task: profile the plain `Remesher` first, then build only the levers whose upper bound pays; one PR per lever, results bit-identical where claimed.
Commands and raw rows: scratch (not committed); the A/B numbers are from `ReducerRemesherBaseline ops=remesh fidelity=0 invariance=0 runs=3` (24 inputs, 4 cores,
net10.0, master a5b4e60 against the PR, base and candidate alternating twice in one job, the minimum of the runs is compared).

### 12.1 Profile of the plain Remesher (before any change)

`g3cli remesh` settings (the baseline harness), x1 and x2 mean edge length, 24 inputs, a profiling build of `Remesher.ProcessEdge` with a timestamp per step (not committed; the
timestamps cost about 20 % of the run, so shares, not seconds). Share of the wall time:

| | x1 | x2 |
|---|---|---|
| serial edge operations (`ProcessEdge`) | 82 % | 78 % |
| of them: look-up of the edge, constraints, lengths | 10 % | 9 % |
| of them: collapse test and `CollapseEdge` | 9 % (the mesh operation 4.1 %) | 15 % (7.8 %) |
| of them: **flip test** and `FlipEdge` | **40 %** (the mesh operation 1.0 %) | **33 %** (1.1 %) |
| of them: split test and `SplitEdge` | 7 % (1.4 %) | 7 % (1.2 %) |
| smoothing (parallel) | 3 % | 4 % |
| projection onto the input (parallel, 4.4M / 1.0M nearest-point queries, 15 s / 3 s CPU) | 11 % | 9 % |
| allocation inside the edge operations | 3.7 GB, all of it in the flip test | 1.1 GB |

The flip *test* costs 40 times what the flip itself does: with `MeshIsClosed` false (it is only set by an explicit, undocumented-as-needed `Precompute()` call that nobody makes)
the test asked `DMesh3.IsBoundaryVertex` four times per edge, each time with an iterator allocation (256 bytes per edge, measured). The rest of the operation share is cache-bound
(look-ups of edges, vertices and valences).

With `PreventSelfIntersections` (15 meshes up to 20k triangles, `profile` build): the checks of the three edge operations are 41 % (x1) / 46 % (x2) of the run (collapse 24 / 34 %), the
vertex-move check of the smoothing and projection passes (`apply_moves_without_intersections`: an AABB tree built per pass, one parallel traversal over all triangles of moved vertices, the
grid rebuilt) is 23 + 17 % with their own work, and on 299291 (18k triangles, x1) 53 % of the run (tree build 5 %, first-round traversal 26 %, grid rebuild 8 %). The grid for the edge checks is
*already* updated incrementally per operation (rebuilt only once per pass, 5 %). 92 % of the 7.9M pair tests on 299291 are decided by `TriangleTriangleFilter` (27 % of the run), the exact
stage is called for 1 % of them (1 % of the run): the orientation prefilter of #419 is in `pair_intersects` already.

### 12.2 Upper bounds of the levers (stop rule: report, not build, below half the bound)

| lever | upper bound on the plain Remesher | decision |
|---|---|---|
| (d) the flip test: closed-mesh hint by the pass itself, no iterator in `IsBoundaryVertex` | flip test 40 % of the run, 256 bytes per edge; probe (`Precompute()` called) took the flip share on the sphere from 2.6 s to 1.2 s: 1.3 x | **built (PR 1)** |
| (a) projection through `NearestPointBatch` | the projection is already parallel (4 threads over `gParallel`), so the batch API's 3.7-7.2 x over a *serial* loop does not apply; what remains is the coherence from the sorted order (1.1-1.6 x serial, section "stage 2" of BatchQueryBenchmark): 11 % x (1 - 1/1.3) = 2.5 % (x1), at best 1.06 x | see 12.4 |
| (b) incremental tree for the checks | the edge-check grid is incremental already; the per-pass rebuilds (tree 5 %, grid 5-8 %) are at most 13 % of an `si` run, and they happen because all vertices move in a smoothing pass (no vertex stays still, so nothing is left to update incrementally) | not built, 12.4 |
| (c) orientation prefilter (#419) | already in front of the exact test (92 % of pairs decided by the 2D filter, exact stage 1 %); a cheaper float pre-test could save at most half of the filter's 27 %: about 1.15 x of an `si` run, with a new floating-point bound to review | not built, 12.4 |

### 12.3 PR 1: the closed-mesh hint and the allocation-free `IsBoundaryVertex` (lever d)

`Remesher` detects a closed mesh at the start of every pass (`RemesherPro`: at the first pass after construction / `ResetQueue()`), as `Precompute()` did on request; `Precompute()` stays
(and now documents that it changes nothing). `DMesh3.IsBoundaryVertex` walks the vertex's edge list without an iterator (`SmallListSet.AnyAtSlot`, the same pattern as `FindByKey`), which
also speeds up the open meshes and every other caller. No result changes: no vertex of a closed mesh is a boundary vertex, and the other path computes the same value.

A/B (24 inputs, 4 cores, the minimum of 2 alternating runs of 3 each; hashes = `MeshHash` of the result):

| variant, target | cases | base s | PR s | speed-up (total) | per case median | result hashes |
|---|---|---|---|---|---|---|
| default x1 | 24 | 17.7 | 11.8 | **1.49 x** | 1.36 x | 24 / 24 equal |
| default x2 | 24 | 6.3 | 4.9 | **1.29 x** | 1.21 x | 24 / 24 equal |
| `PreventSelfIntersections` x1 | 16 | 67.0 | 65.1 | 1.03 x | 1.04 x | 16 / 16 equal |
| `PreventSelfIntersections` x2 | 16 | 19.4 | 18.4 | 1.05 x | 1.02 x | 16 / 16 equal |

Procedural meshes at x1: sphere 3.10 -> 1.75 s, torus 2.21 -> 1.34 s, terrain (open) 3.02 -> 2.19 s, box 1.78 -> 1.12 s. The 2.0 doc's target for T4 (1.3 to 1.5 x on the plain Remesher) is met
by this lever alone, without a partition. The per-pass snapshot of `RejectInvertingPasses` (a mesh copy and two volume sums, about 5 % of the run on the sphere) is the next item of the plain profile.

### 12.4 Levers not built (probes and bounds)

**(a) `NearestPointBatch` for the projection.** Probe (scratch program, the library at this PR): the vertices of a remeshed result, displaced by 0.3 % of the mean edge as after smoothing, projected onto
the input's `DMeshAABBTree3`; the Remesher's own pattern (`gParallel.ForEach` over `target.Project`) against `MeshBatchQueries.NearestPointBatch` (sorted, parallel; one run of 5, the minimum):

| mesh (points, tree triangles) | current, parallel | batch, sorted, parallel | batch, unsorted, parallel | batch over current | positions bit-identical |
|---|---|---|---|---|---|
| bumpy sphere (48.6k, 95k) | 18.0 ms | 28.8 ms | 22.0 ms | 0.63 x | yes |
| torus (34.5k, 68k) | 12.4 ms | 23.8 ms | 15.7 ms | 0.52 x | yes |
| 78295 (12.7k, 42k) | 18.1 ms | 16.1 ms | 19.2 ms | 1.12 x | yes |
| 93370 (22.4k, 46k) | 41.5 ms | 33.3 ms | 42.6 ms | 1.24 x | yes |
| 299291 (9.1k, 18k) | 4.0 ms | 4.7 ms | 4.3 ms | 0.85 x | yes |

The batch API's 3.7-7.2 x is against a *serial* loop in shuffled order. The Remesher's projection is parallel already and its vertex ids are spatially coherent, so the Morton sort only adds cost
(slower on the smooth meshes, +12-24 % on two Thingi10K meshes). Projection is 11-14 % of the plain run after PR 1; 1.0-1.2 x of that is under 3 % overall. Not built (stop rule). The caller switch is
still possible later for callers that project unordered point sets.

**(b) Incremental tree for the self-intersection checks.** The edge checks use a `TriangleHashGrid` updated per operation since #405; it is rebuilt once per pass (5 % of an `si` run). The
move check of the smoothing and projection passes rebuilds an `DMeshAABBTree3` and the grid because the pass moves nearly every vertex; there is nothing incremental to exploit, and a refit
would save the second tree of a pass (the projection's) at best: 2.5 % of an `si` run. Not built. The cost of that check is the first-round traversal (26 % of the run on 299291), whose
only lever is cheaper pair tests (below) or a smaller candidate set (the clearance filter is closed, section 1.1).

**(c) Orientation prefilter (#419, Devillers-Guigue).** `Remesher.pair_intersects` runs `TriangleTriangleFilter` (2D projection, exact `Orient2d`), then the exact shared-vertex shortcut and
`ExactTriangleIntersection.IntersectsBeyondShared`, whose no-shared-vertex branch is Devillers-Guigue (the plane-side early exits). Counted on 299291 (17988 triangles, x1, `si`):
7.9M pair tests, 92 % decided by the filter, 0.4 % by the plane shortcut, 1.1 % reach the exact stage (0.06 s of 6.6 s); the filter itself is 2.2 s of 6.6 s (instrumented). So the prefilter is in
place; what is left is the cost of the filter's own `Orient2d` calls on ring triangles that all share the collapse vertex. A certified floating-point pre-test of the ring would need its own error bound
and review for at most 1.15 x of an `si` run. Not built.

**Left in the plain profile after PR 1** (profile build, same set): operations 77 % / 73 % (x1 / x2; look-up 13 / 11, collapse 11 / 17, flip test 25 / 20, split 9 / 8), smoothing 3-4 %, projection 14 / 11 %,
allocation in the operations 0.2 GB (from 3.7). The per-pass snapshot of `RejectInvertingPasses` is about 6 %; the rest is spread over cache-bound look-ups, none above 5 %.
